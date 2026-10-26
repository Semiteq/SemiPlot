using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// The live edge of one subscription, as an awaitable read rather than as an observable; one instance per
/// subscription, and not thread-safe, the caller drives it from one loop at a time.
/// </summary>
internal sealed class RealtimePoll(
	NpgsqlDataSource dataSource,
	ArchiveTimeConverter timeConverter,
	ArchiveExceptionMapper exceptionMapper,
	int[] penIds,
	ILogger logger)
{
	// docs/architecture/scada-archive.md#quality-and-gaps
	private const int LastBeforeBreakQuality = 32;

	// A stalled tick holds the live edge for at most this long, not for the connection string's backstop.
	private const int TickCommandTimeoutSeconds = 10;

	// docs/architecture/data-integration.md#realtime
	private static readonly TimeSpan _breakAnchorStep = TimeSpan.FromMilliseconds(1);

	private static readonly IReadOnlyList<Sample> _noSamples = [];

	private int _consecutiveFailures;

	// The archive's own naive wall clock, null until the baseline is read; never taken from the local clock:
	// a clock difference between the two hosts would drop or repeat the first seconds.
	private DateTime? _lastSeen;

	/// <summary>
	/// One tick. The first one establishes the baseline and emits no sample; every later one emits the
	/// rows written since the previous tick, converted to UTC. A failed tick emits no sample.
	/// </summary>
	public async Task<IReadOnlyList<Sample>> ReadOnceAsync(CancellationToken cancellationToken)
	{
		try
		{
			await using var connection = await dataSource
				.OpenConnectionAsync(cancellationToken)
				.ConfigureAwait(false);

			var samples = _lastSeen is { } lastSeen
				? await ReadSamplesAsync(connection, lastSeen, cancellationToken).ConfigureAwait(false)
				: await ReadBaselineAsync(connection, cancellationToken).ConfigureAwait(false);

			_consecutiveFailures = 0;

			return samples;
		}
		catch (OperationCanceledException)
		{
			// Ahead of the general catch: a self-cancelled read is no failed tick.
			throw;
		}
		catch (Exception exception)
		{
			LogFailure(exception);

			return _noSamples;
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

			logger.LogDebug("The realtime baseline for {SubscribedPenCount} variables is {Baseline:O}.",
				penIds.Length, baseline);
		}
		else
		{
			logger.LogDebug("The {SubscribedPenCount} subscribed variables carry no archived row yet.",
				penIds.Length);
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

	private void LogFailure(Exception exception)
	{
		var error = exceptionMapper.Map(exception, ArchiveStatements.TrendsRelation);

		_consecutiveFailures++;

		logger.LogWarning(
			exception,
			"The realtime poll tick failed, {ConsecutiveFailures} in a row: {Reason}",
			_consecutiveFailures,
			error.Message);
	}
}
