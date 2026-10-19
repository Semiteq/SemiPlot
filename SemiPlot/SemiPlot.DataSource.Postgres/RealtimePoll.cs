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
/// The live edge of one subscription, as an awaitable read rather than as an observable; one instance per
/// subscription, and not thread-safe, the caller drives it from one loop at a time.
/// </summary>
internal sealed class RealtimePoll(
	NpgsqlDataSource dataSource,
	ArchiveTimeConverter timeConverter,
	ArchiveExceptionMapper exceptionMapper,
	PostgresConnectionSettings settings,
	int[] penIds,
	ILogger logger)
{
	// docs/architecture/scada-archive.md#quality-and-gaps
	private const int LastBeforeBreakQuality = 32;

	// Must stay far below the connection string's backstop: three stalled ticks must raise the fault
	// inside half a minute.
	private const int TickCommandTimeoutSeconds = 10;

	// docs/architecture/data-integration.md#realtime
	private static readonly TimeSpan _breakAnchorStep = TimeSpan.FromMilliseconds(1);

	private static readonly IReadOnlyList<Sample> _noSamples = [];

	private int _consecutiveFailures;

	private bool _faultRaised;

	private bool _reportedConnected;

	// The archive's own naive wall clock, null until the baseline is read; never taken from the local clock:
	// a clock difference between the two hosts would drop or repeat the first seconds.
	private DateTime? _lastSeen;

	/// <summary>
	/// One tick. The first one establishes the baseline and emits no sample; every later one emits the
	/// rows written since the previous tick, converted to UTC.
	/// </summary>
	public async Task<RealtimeTick> ReadOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using var connection = await dataSource
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

		BindBaseline(command, penIds);

		var answer = await command.ExecuteScalarAsync(cancellationToken).ConfigureAwait(false);

		if (answer is DateTime baseline)
		{
			Advance(baseline);

			logger.LogDebug("The realtime baseline for {PenCount} variables is {Baseline:O}.", penIds.Length,
				baseline);
		}
		else
		{
			logger.LogDebug("The {PenCount} subscribed variables carry no archived row yet.", penIds.Length);
		}

		return _noSamples;
	}

	private async Task<IReadOnlyList<Sample>> ReadSamplesAsync(
		NpgsqlConnection connection,
		DateTime lastSeen,
		CancellationToken cancellationToken)
	{
		await using var command = CreateTickCommand(ArchiveStatements.RealtimePoll, connection);

		BindPoll(command, penIds, lastSeen);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var samples = new List<Sample>();
		var newest = lastSeen;
		var rowCount = 0;
		var droppedCount = 0;
		var breakCount = 0;

		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			var penId = reader.GetInt32(0);
			var archiveLocal = reader.GetDateTime(1);
			var timestampUtc = timeConverter.ToUtc(archiveLocal);
			var isBreakMark = reader.GetInt32(3) == LastBeforeBreakQuality;

			rowCount++;

			if (archiveLocal > newest)
			{
				newest = archiveLocal;
			}

			// docs/architecture/data-integration.md#realtime
			if (reader.IsDBNull(2))
			{
				droppedCount++;
			}
			else
			{
				samples.Add(new Sample(penId, timestampUtc, reader.GetDouble(2)));
			}

			if (isBreakMark)
			{
				samples.Add(new Sample(penId, timestampUtc + _breakAnchorStep, null));
				breakCount++;
			}
		}

		Advance(newest);

		logger.LogDebug(
			"The realtime poll read {RowCount} rows past {LastSeen:O}: {ValueCount} value samples, {DroppedCount} "
			+ "rows dropped for a null value, {BreakCount} breaks.",
			rowCount,
			lastSeen,
			rowCount - droppedCount,
			droppedCount,
			breakCount);

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
		var error = exceptionMapper.Map(exception, ArchiveStatements.TrendsRelation);

		_consecutiveFailures++;

		logger.LogWarning(
			exception,
			"The realtime poll tick failed, {ConsecutiveFailures} in a row: {Reason}",
			_consecutiveFailures,
			error.Message);

		if (_consecutiveFailures < ArchiveConnectionState.ConsecutiveFailuresBeforeFault || _faultRaised)
		{
			return null;
		}

		_faultRaised = true;

		return new ArchiveConnectionState(new ArchiveError(
			ArchiveFault.ConnectionLost,
			settings.Host,
			settings.Port,
			settings.Database,
			ArchiveConnectionState.ConsecutiveFailuresBeforeFault.ToString(CultureInfo.InvariantCulture)));
	}
}
