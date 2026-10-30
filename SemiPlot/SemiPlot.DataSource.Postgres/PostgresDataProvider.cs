using System.Diagnostics;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the archive over the pooled <see cref="NpgsqlDataSource"/>: <see cref="QueryCatalogueAsync"/> answers
/// the stored catalogue and the configured variables, <see cref="QueryArchiveExtentAsync"/> the span they cover
/// and <see cref="QueryHistoryAsync"/> a window of one layer, all crossing the boundary in UTC.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	/// <summary>
	/// The most pen groups one history read runs concurrently
	/// (docs/architecture/data-integration.md#operation-to-statement).
	/// </summary>
	internal const int HistoryReadConcurrency = 4;

	private const string HistoryReadRequestTemplate =
		"History read at {Layer} from {FromUtc:O} to {ToUtc:O}, {TargetColumnCount} columns for {PenCount} pens "
			+ "in {GroupCount} groups: ";

	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PostgresDataProvider> _logger;
	private readonly int _historyReadConcurrency;
	private readonly CatalogueNormaliser _catalogueNormaliser;

	internal PostgresDataProvider(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		TimeProvider timeProvider,
		ILogger<PostgresDataProvider> logger,
		int historyReadConcurrency)
	{
		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_timeProvider = timeProvider;
		_logger = logger;
		_historyReadConcurrency = historyReadConcurrency;
		_catalogueNormaliser = new CatalogueNormaliser(logger);
	}

	/// <summary>Reads both projections of the catalogue on one connection.</summary>
	public async Task<Result<CatalogueRead>> QueryCatalogueAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

			var (pens, groups) = await CatalogueRowReader.ReadAsync(connection, isBounded: true).ConfigureAwait(false);

			return Result.Ok(_catalogueNormaliser.Project(pens, groups));
		}
		catch (Exception exception)
		{
			return Result.Fail<CatalogueRead>(Map(exception, ArchiveStatements.CatalogueRelations));
		}
	}

	/// <summary>
	/// One envelope per requested pen that has rows in a window of one layer, in ascending pen identifier; no
	/// rows at all is a successful empty list. Every layer reduces to the column target server-side, and a
	/// coarse layer also patches its fresh tail from the raw layer.
	/// </summary>
	public async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount,
		CancellationToken cancellationToken = default)
	{
		// docs/architecture/data-integration.md#operation-to-statement
		int[] readPenIds = [.. penIds.Distinct().Order()];
		var groupSize = Math.Max(1, (readPenIds.Length + _historyReadConcurrency - 1) / _historyReadConcurrency);
		int[][] penGroups = [.. readPenIds.Chunk(groupSize)];
		var stopwatch = Stopwatch.StartNew();
		var outcome = HistoryReadOutcome.Failed;
		int? envelopeCount = null;

		try
		{
			var envelopes = await ReadHistoryAsync(
					readPenIds,
					penGroups,
					fromUtc,
					toUtc,
					layer,
					targetColumnCount,
					cancellationToken)
				.ConfigureAwait(false);

			outcome = HistoryReadOutcome.Read;
			envelopeCount = envelopes.Count;

			return Result.Ok(envelopes);
		}
		catch (OperationCanceledException)
		{
			outcome = HistoryReadOutcome.Cancelled;

			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(Map(exception, ArchiveStatements.TrendsRelation));
		}
		finally
		{
			LogHistoryRead(
				layer,
				fromUtc,
				toUtc,
				targetColumnCount,
				readPenIds.Length,
				penGroups.Length,
				outcome,
				envelopeCount,
				stopwatch);
		}
	}

	/// <summary>
	/// The span the configured variables cover, in UTC. It is the span of the catalogue, not of the archive:
	/// rooted at <c>semiplot_tags</c>, so an empty catalogue over a full archive reports
	/// <see cref="ArchiveExtent.Empty"/>, same as a seeded catalogue over an empty <c>trends</c>. Both succeed.
	/// </summary>
	public async Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = new NpgsqlCommand(ArchiveStatements.ArchiveExtent, connection);
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			return Result.Ok(await ReadExtentAsync(reader).ConfigureAwait(false));
		}
		catch (Exception exception)
		{
			return Result.Fail<ArchiveExtent>(Map(exception, ArchiveStatements.TagCatalogAndTrendsRelations));
		}
	}

	// docs/architecture/data-integration.md#time-boundary
	public async Task<Result<IReadOnlyList<DateOnly>>> QueryArchiveDaysAsync(
		DateOnly firstDay,
		DateOnly lastDay,
		CancellationToken cancellationToken = default)
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
			await using var command = new NpgsqlCommand(ArchiveStatements.ArchiveDays, connection);

			BindArchiveDays(command, firstDay, lastDay);

			await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

			var days = new List<DateOnly>();

			while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
			{
				days.Add(reader.GetFieldValue<DateOnly>(0));
			}

			return Result.Ok<IReadOnlyList<DateOnly>>(days);
		}
		catch (OperationCanceledException)
		{
			throw;
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<DateOnly>>(Map(exception, ArchiveStatements.TagCatalogAndTrendsRelations));
		}
	}

	// docs/architecture/data-integration.md#operation-to-statement
	private async Task<IReadOnlyList<PenHistoryEnvelope>> ReadHistoryAsync(
		int[] penIds,
		int[][] penGroups,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount,
		CancellationToken cancellationToken)
	{
		var fromLocal = _timeConverter.ToArchiveLocal(fromUtc);
		var toLocal = _timeConverter.ToArchiveLocal(toUtc);
		var bucket = BucketFor(fromLocal, toLocal, targetColumnCount);

		var rows = await ReadPenGroupsAsync(
				penGroups,
				(group, token) => ReadBucketedWindowAsync(group, fromLocal, toLocal, layer, bucket, token),
				cancellationToken)
			.ConfigureAwait(false);

		if (layer != AggregationLayer.Raw)
		{
			var presentLocal = _timeConverter.ToArchiveLocal(_timeProvider.GetUtcNow().UtcDateTime);
			var tailEdgeLocal = presentLocal < toLocal ? presentLocal : toLocal;

			rows = await FillFreshTailAsync(
					penIds,
					penGroups,
					fromLocal,
					toLocal,
					tailEdgeLocal,
					layer,
					bucket,
					rows,
					cancellationToken)
				.ConfigureAwait(false);
		}

		return BucketedRowFold.Fold(rows, _timeConverter);
	}

	private static async Task<IReadOnlyList<BucketedRowFold.Row>> ReadPenGroupsAsync(
		int[][] penGroups,
		Func<int[], CancellationToken, Task<IReadOnlyList<BucketedRowFold.Row>>> readGroup,
		CancellationToken cancellationToken)
	{
		using var groupCancellation = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

		var groupRows = await Task.WhenAll(penGroups.Select(ReadGroupAsync)).ConfigureAwait(false);

		return [.. groupRows.SelectMany(rows => rows)];

		async Task<IReadOnlyList<BucketedRowFold.Row>> ReadGroupAsync(int[] group)
		{
			try
			{
				return await readGroup(group, groupCancellation.Token).ConfigureAwait(false);
			}
			catch
			{
				await groupCancellation.CancelAsync().ConfigureAwait(false);

				throw;
			}
		}
	}

	private void LogHistoryRead(
		AggregationLayer layer,
		DateTime fromUtc,
		DateTime toUtc,
		int targetColumnCount,
		int penCount,
		int groupCount,
		HistoryReadOutcome outcome,
		int? envelopeCount,
		Stopwatch stopwatch)
	{
		if (envelopeCount is { } count)
		{
			_logger.LogDebug(
				HistoryReadRequestTemplate + "{Outcome} {EnvelopeCount} envelopes after {ElapsedMilliseconds} ms.",
				layer,
				fromUtc,
				toUtc,
				targetColumnCount,
				penCount,
				groupCount,
				outcome,
				count,
				stopwatch.ElapsedMilliseconds);

			return;
		}

		_logger.LogDebug(
			HistoryReadRequestTemplate + "{Outcome} after {ElapsedMilliseconds} ms.",
			layer,
			fromUtc,
			toUtc,
			targetColumnCount,
			penCount,
			groupCount,
			outcome,
			stopwatch.ElapsedMilliseconds);
	}

	// Bound on the archive's own wall clock: a UTC round trip is not injective across DST.
	internal static void BindLocalBucketedWindow(
		NpgsqlCommand command,
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer,
		TimeSpan bucket)
	{
		command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
		{
			Value = penIds
		});
		command.Parameters.Add(new NpgsqlParameter("layer", NpgsqlDbType.Smallint) { Value = (short)layer });
		command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Timestamp) { Value = fromLocal });
		command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Timestamp) { Value = toLocal });
		command.Parameters.Add(new NpgsqlParameter("bucket", NpgsqlDbType.Interval) { Value = bucket });
	}

	internal static void BindArchiveDays(NpgsqlCommand command, DateOnly firstDay, DateOnly lastDay)
	{
		command.Parameters.Add(new NpgsqlParameter("firstDay", NpgsqlDbType.Timestamp)
		{
			Value = firstDay.ToDateTime(TimeOnly.MinValue)
		});
		command.Parameters.Add(new NpgsqlParameter("lastDay", NpgsqlDbType.Timestamp)
		{
			Value = lastDay.ToDateTime(TimeOnly.MinValue)
		});
	}

	// One millisecond is the column's own resolution, so a narrower bucket buys no reduction and a zero one
	// is not an interval date_bin accepts. The result is truncated to the microsecond the interval wire
	// format carries, so it is the bucket the server bins by rather than the one asked for.
	internal static TimeSpan BucketFor(DateTime fromLocal, DateTime toLocal, int targetColumnCount)
	{
		var bucket = (toLocal - fromLocal) / targetColumnCount;

		if (bucket < TimeSpan.FromMilliseconds(1))
		{
			bucket = TimeSpan.FromMilliseconds(1);
		}

		return TimeSpan.FromTicks(bucket.Ticks - (bucket.Ticks % TimeSpan.TicksPerMicrosecond));
	}

	private async Task<IReadOnlyList<BucketedRowFold.Row>> ReadBucketedWindowAsync(
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer,
		TimeSpan bucket,
		CancellationToken cancellationToken)
	{
		await using var connection = await _dataSource.OpenConnectionAsync(cancellationToken).ConfigureAwait(false);
		await using var command = new NpgsqlCommand(ArchiveStatements.BucketedHistoryWindow, connection);

		BindLocalBucketedWindow(command, penIds, fromLocal, toLocal, layer, bucket);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken).ConfigureAwait(false);

		var rows = new List<BucketedRowFold.Row>();

		while (await reader.ReadAsync(cancellationToken).ConfigureAwait(false))
		{
			rows.Add(ReadBucketedRow(reader));
		}

		return rows;
	}

	// docs/architecture/data-integration.md#layer-ladder
	private async Task<IReadOnlyList<BucketedRowFold.Row>> FillFreshTailAsync(
		int[] penIds,
		int[][] penGroups,
		DateTime fromLocal,
		DateTime toLocal,
		DateTime tailEdgeLocal,
		AggregationLayer layer,
		TimeSpan bucket,
		IReadOnlyList<BucketedRowFold.Row> coarseRows,
		CancellationToken cancellationToken)
	{
		var seams = FreshTail.Seams(coarseRows, penIds, fromLocal);

		if (FreshTail.Start(layer, seams, tailEdgeLocal) is not { } tailStart)
		{
			return coarseRows;
		}

		var tailRows = await ReadPenGroupsAsync(
				penGroups,
				(group, token) => ReadBucketedWindowAsync(
					group,
					tailStart,
					toLocal,
					AggregationLayer.Raw,
					bucket,
					token),
				cancellationToken)
			.ConfigureAwait(false);

		return FreshTail.Merge(coarseRows, tailRows, seams, tailStart);
	}

	private static BucketedRowFold.Row ReadBucketedRow(NpgsqlDataReader reader)
	{
		return new BucketedRowFold.Row(
			reader.GetInt32(0),
			reader.GetDateTime(1),
			ReadValueOrGap(reader, 2),
			ReadValueOrGap(reader, 3),
			ReadValueOrGap(reader, 4),
			reader.GetBoolean(5));
	}

	// v is nullable in the archive. A bucket of nothing but nulls aggregates to null in all three series and
	// reads back as the gap column the chart draws; the statement's breaks flag carries the mixed bucket.
	private static double ReadValueOrGap(NpgsqlDataReader reader, int ordinal)
	{
		return reader.IsDBNull(ordinal) ? double.NaN : reader.GetDouble(ordinal);
	}

	private async Task<ArchiveExtent> ReadExtentAsync(NpgsqlDataReader reader)
	{
		if (!await reader.ReadAsync().ConfigureAwait(false) || reader.IsDBNull(0) || reader.IsDBNull(1))
		{
			return ArchiveExtent.Empty;
		}

		return new ArchiveExtent(
			_timeConverter.ToUtc(reader.GetDateTime(0)),
			_timeConverter.ToUtc(reader.GetDateTime(1)));
	}

	private Error Map(Exception exception, string relation)
	{
		return ArchiveFailureLog.LogIfUnexpected(_exceptionMapper.Map(exception, relation), exception, _logger);
	}

	private enum HistoryReadOutcome
	{
		Read,
		Failed,
		Cancelled
	}
}
