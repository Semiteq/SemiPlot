using System.Globalization;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// One tick's result. <see cref="StateChange"/> is null when the tick changed nothing the operator has to
/// be told about; otherwise it is the connection state the subscription has just moved to.
/// </summary>
internal readonly record struct RealtimeTick(IReadOnlyList<Sample> Samples, ArchiveConnectionState? StateChange);

/// <summary>
/// The live edge of one subscription, as an awaitable read rather than as an observable.
/// <para>
/// One instance per subscription, and not thread-safe: the caller drives it from one loop at a time.
/// </para>
/// </summary>
internal sealed class RealtimePoll
{
	// Three: one Npgsql reconnect after a reset is one failed tick, and a fault on one would flap.
	private const int ConsecutiveFailuresBeforeFault = 3;

	// q = 32 marks the last sample before a break; read for the debug line only
	// (docs/architecture/scada-archive.md, Quality and gaps).
	private const int LastBeforeBreakQuality = 32;

	// Must stay far below the connection string's backstop: three stalled ticks must raise the fault
	// inside half a minute.
	private const int TickCommandTimeoutSeconds = 10;

	private static readonly IReadOnlyList<Sample> _noSamples = [];

	private readonly NpgsqlDataSource _dataSource;

	private readonly ArchiveTimeConverter _timeConverter;

	private readonly ArchiveExceptionMapper _exceptionMapper;

	private readonly PostgresConnectionSettings _settings;

	private readonly ILogger _logger;

	private readonly int[] _penIds;

	// Never taken from the local clock: a clock difference between the two hosts would drop or repeat
	// the first seconds.
	private DateTime? _lastSeen;

	private int _consecutiveFailures;

	private bool _faultRaised;

	private bool _reportedConnected;

	public RealtimePoll(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		PostgresConnectionSettings settings,
		int[] penIds,
		ILogger logger)
	{
		ArgumentNullException.ThrowIfNull(dataSource);
		ArgumentNullException.ThrowIfNull(timeConverter);
		ArgumentNullException.ThrowIfNull(exceptionMapper);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(penIds);
		ArgumentNullException.ThrowIfNull(logger);

		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_settings = settings;
		_penIds = penIds;
		_logger = logger;
	}

	/// <summary>
	/// The newest archive timestamp this subscription has read, in the archive's own naive wall clock, or
	/// null while the baseline is still unread. It only ever moves forward.
	/// </summary>
	public DateTime? LastSeen => _lastSeen;

	/// <summary>
	/// One tick. The first one establishes the baseline and emits no sample; every later one emits the
	/// rows written since the previous tick, converted to UTC.
	/// </summary>
	public async Task<RealtimeTick> ReadOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using var connection = await _dataSource
				.OpenConnectionAsync(cancellationToken)
				.ConfigureAwait(false);

			var samples = _lastSeen is { } lastSeen
				? await ReadSamplesAsync(connection, lastSeen, cancellationToken).ConfigureAwait(false)
				: await ReadBaselineAsync(connection, cancellationToken).ConfigureAwait(false);

			return new RealtimeTick(samples, Succeed());
		}
		catch (OperationCanceledException)
		{
			// Ahead of the general catch: a self-cancelled read must not count towards the fault threshold.
			throw;
		}
		catch (Exception exception)
		{
			return new RealtimeTick(_noSamples, Fail(exception));
		}
	}

	internal static NpgsqlCommand CreateTickCommand(string statementText, NpgsqlConnection connection)
	{
		return new NpgsqlCommand(statementText, connection) { CommandTimeout = TickCommandTimeoutSeconds };
	}

	internal static void BindPoll(NpgsqlCommand command, int[] penIds, DateTime lastSeenArchiveLocal)
	{
		BindIdentifiers(command, penIds);

		command.Parameters.Add(new NpgsqlParameter("lastSeen", NpgsqlDbType.Timestamp)
		{
			Value = lastSeenArchiveLocal
		});
	}

	internal static void BindBaseline(NpgsqlCommand command, int[] penIds)
	{
		BindIdentifiers(command, penIds);
	}

	private static void BindIdentifiers(NpgsqlCommand command, int[] penIds)
	{
		command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
		{
			Value = penIds
		});
	}

	// A null @lastSeen returns no row for good and an unbounded read dumps the archive, so the first tick
	// reads the edge and emits nothing.
	private async Task<IReadOnlyList<Sample>> ReadBaselineAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		await using var command = CreateTickCommand(ArchiveStatements.RealtimeBaseline, connection);

		BindBaseline(command, _penIds);

		var answer = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

		if (answer is DateTime baseline)
		{
			Advance(baseline);

			_logger.LogDebug("The realtime baseline for {PenCount} variables is {Baseline:O}.", _penIds.Length,
				baseline);
		}
		else
		{
			_logger.LogDebug("The {PenCount} subscribed variables carry no archived row yet.", _penIds.Length);
		}

		return _noSamples;
	}

	private async Task<IReadOnlyList<Sample>> ReadSamplesAsync(
		NpgsqlConnection connection,
		DateTime lastSeen,
		CancellationToken cancellationToken)
	{
		await using var command = CreateTickCommand(ArchiveStatements.RealtimePoll, connection);

		BindPoll(command, _penIds, lastSeen);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var samples = new List<Sample>();
		var newest = lastSeen;
		var rowCount = 0;
		var droppedCount = 0;
		var breakMarkCount = 0;

		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var archiveLocal = reader.GetDateTime(1);

			rowCount++;

			if (archiveLocal > newest)
			{
				newest = archiveLocal;
			}

			if (reader.GetInt32(3) == LastBeforeBreakQuality)
			{
				breakMarkCount++;
			}

			// Sample.Value is non-nullable; GetDouble would throw and count as a connection failure, so the
			// row is dropped and lastSeen still advances.
			if (reader.IsDBNull(2))
			{
				droppedCount++;

				continue;
			}

			samples.Add(new Sample(reader.GetInt32(0), _timeConverter.ToUtc(archiveLocal), reader.GetDouble(2)));
		}

		Advance(newest);

		_logger.LogDebug(
			"The realtime poll read {RowCount} rows past {LastSeen:O}: {SampleCount} samples, {DroppedCount} "
			+ "dropped for a null value, {BreakMarkCount} marking the last sample before a break.",
			rowCount,
			lastSeen,
			samples.Count,
			droppedCount,
			breakMarkCount);

		return samples;
	}

	private void Advance(DateTime archiveLocal)
	{
		if (_lastSeen is null || archiveLocal > _lastSeen)
		{
			_lastSeen = archiveLocal;
		}
	}

	private ArchiveConnectionState? Succeed()
	{
		_consecutiveFailures = 0;

		if (_reportedConnected && !_faultRaised)
		{
			return null;
		}

		_reportedConnected = true;
		_faultRaised = false;

		return ArchiveConnectionState.Connected;
	}

	private ArchiveConnectionState? Fail(Exception exception)
	{
		var error = _exceptionMapper.Map(exception, ArchiveStatements.TrendsRelation);

		_consecutiveFailures++;

		_logger.LogWarning(
			exception,
			"The realtime poll tick failed, {ConsecutiveFailures} in a row: {Reason}",
			_consecutiveFailures,
			error.Message);

		if (_consecutiveFailures < ConsecutiveFailuresBeforeFault || _faultRaised)
		{
			return null;
		}

		_faultRaised = true;

		return new ArchiveConnectionState(new ArchiveError(
			ArchiveFault.ConnectionLost,
			_settings.Host,
			_settings.Port,
			_settings.Database,
			ConsecutiveFailuresBeforeFault.ToString(CultureInfo.InvariantCulture)));
	}
}
