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
/// The live edge of one subscription, as an awaitable read rather than as an observable. It holds the
/// whole of that subscription's state — the baseline, the last timestamp seen, the consecutive failure
/// count and the raised-fault flag — so every rule the poll obeys is a property of
/// <see cref="ReadOnceAsync"/> and is asserted by awaiting it, with no scheduler involved.
/// <para>
/// One instance per subscription, and not thread-safe: the caller drives it from one loop at a time.
/// </para>
/// <para>
/// Nothing here throws except cancellation. A failing tick reports no sample, so the consumer keeps the
/// data it has, and the failure reaches the operator through <see cref="RealtimeTick.StateChange"/>
/// rather than through an error the consumer has no handler for.
/// </para>
/// </summary>
internal sealed class RealtimePoll
{
	// Three rather than one, because Npgsql opens a fresh physical connection after a reset: a dropped
	// packet or a recycled pool connection produces exactly one failed tick, and a fault raised on one
	// failure would flap over a healthy archive. Three rather than ten, because the count multiplies the
	// operator's own poll_interval_ms — at the 1 s cadence a bench uses that is a fault within about
	// three seconds, and at 5 s within fifteen.
	private const int ConsecutiveFailuresBeforeFault = 3;

	// The archive marks the last sample before a break q = 32 (docs/architecture/scada-archive.md,
	// Quality and gaps). It carries a real value and is emitted as an ordinary sample: the gap the
	// history path draws is HistoryRowFold's reconstruction, and Sample carries no null to rebuild it
	// with here. Read for the debug line and nowhere else.
	private const int LastBeforeBreakQuality = 32;

	// A tick runs every poll_interval_ms and must not inherit ArchiveDataSource's five-minute client
	// backstop: a server that accepts connections and then stops answering would hold each tick for
	// minutes and reach the fault threshold only after fifteen of them, leaving a frozen chart and no
	// banner in between. Ten seconds is an order of magnitude above the second a bench cadence gives a
	// tick, and low enough that three stalled ticks raise the fault inside half a minute. The connect
	// attempt keeps its own separate bound, PostgresConnectionSettings.ConnectTimeoutSeconds.
	private const int TickCommandTimeoutSeconds = 10;

	private static readonly IReadOnlyList<Sample> _noSamples = [];

	private readonly ArchiveDataSource _dataSource;

	private readonly ArchiveTimeConverter _timeConverter;

	private readonly ArchiveExceptionMapper _exceptionMapper;

	// The address a raised fault names. ArchiveExceptionMapper holds the same settings but exposes nothing:
	// a lost connection is not derived from an exception, so it is built here rather than mapped there.
	private readonly PostgresConnectionSettings _settings;

	private readonly ILogger _logger;

	private readonly int[] _penIds;

	// The archive's own naive wall clock, which is the side the statement binds on. Unset until a
	// baseline read answers. Never taken from the local clock: the archive stores the SCADA host's time,
	// and a clock difference between the two machines would drop or repeat the first seconds of realtime.
	private DateTime? _lastSeen;

	private int _consecutiveFailures;

	private bool _faultRaised;

	private bool _reportedConnected;

	public RealtimePoll(
		ArchiveDataSource dataSource,
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
			// Ahead of the mapper, which rethrows this exact type by design: a self-cancelled read is not
			// a failure of the archive and must not count towards the fault threshold. The caller's loop
			// ends on it.
			throw;
		}
		catch (Exception exception)
		{
			return new RealtimeTick(_noSamples, Fail(exception));
		}
	}

	// Both statements of a tick, carrying the tick's own bound instead of the data source's backstop.
	// Internal so a unit test reads that bound off a command built by the shipped path rather than off the
	// constant beside it.
	internal static NpgsqlCommand CreateTickCommand(
		ArchiveDataSource dataSource,
		string statementText,
		NpgsqlConnection connection)
	{
		var command = dataSource.CreateCommand(statementText, connection);

		command.CommandTimeout = TickCommandTimeoutSeconds;

		return command;
	}

	// Internal rather than private so a unit test can bind through this exact path and compare the names
	// it produces against the statement's own tokens, and so an EXPLAIN test plans the shipped shape.
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

	// The first tick has nothing to bind @lastSeen to, and both alternatives are wrong: a null bound
	// returns no row and leaves the subscription blind for good, and an unbounded read would dump the
	// whole archive into the chart. So it reads the edge and emits nothing. A NULL answer means the
	// subscribed variables carry no row yet, so lastSeen stays unset and the next tick repeats this read
	// — one index probe per variable, and the right behaviour for an archive nothing has written to.
	private async Task<IReadOnlyList<Sample>> ReadBaselineAsync(
		NpgsqlConnection connection,
		CancellationToken cancellationToken)
	{
		await using var command = CreateTickCommand(_dataSource, ArchiveStatements.RealtimeBaseline, connection);

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
		await using var command = CreateTickCommand(_dataSource, ArchiveStatements.RealtimePoll, connection);

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

			// Sample.Value is non-nullable (SemiPlot.Core/Trends/Sample.cs), so a null v has no
			// representation on the realtime seam and the row is dropped while lastSeen still advances
			// past it. Reading it with GetDouble instead would throw, and this tick's own catch would
			// count that throw as a connection failure — three of which raise a fault over a healthy
			// archive. No null has ever been observed in this column.
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

	// The armed point every consumer sequences on. Reported by the subscription's first successful tick
	// and by the first success after a raised fault; a later ordinary tick reports nothing, so a consumer
	// awaiting the signal is awaiting one event rather than filtering a stream of them.
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
		// Both statements touch one relation, so a 42P01 here can only mean trends.
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

		// The threshold rather than the running count: the fault is raised once and _consecutiveFailures
		// keeps climbing behind it, so anything read off the counter would be frozen at the raise anyway.
		// ArchiveConnectionLostError says so in its own summary.
		return new ArchiveConnectionState(new ArchiveConnectionLostError(
			_settings.Host,
			_settings.Port,
			_settings.Database,
			ConsecutiveFailuresBeforeFault));
	}
}
