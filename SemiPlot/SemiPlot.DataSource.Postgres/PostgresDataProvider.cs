using System.Collections.Concurrent;
using System.Diagnostics;
using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the archive over the pooled <see cref="NpgsqlDataSource"/>: <see cref="QueryPensAsync"/> answers the
/// configured variables, <see cref="QueryArchiveExtentAsync"/> the span they cover, <see cref="QueryHistoryAsync"/>
/// a window of one layer and <see cref="Subscribe"/> the live edge, all crossing the boundary in UTC.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	/// <summary>
	/// The most pen groups one history read runs concurrently
	/// (docs/architecture/data-integration.md#operation-to-statement).
	/// </summary>
	internal const int HistoryReadConcurrency = 4;

	// docs/architecture/data-integration.md#configuration
	private const int CatalogueCommandTimeoutSeconds = 5;

	private const string UncommissionedPenColor = "#808080";
	private const string HistoryReadRequestTemplate =
		"History read at {Layer} from {FromUtc:O} to {ToUtc:O}, {TargetColumnCount} columns for {PenCount} pens "
			+ "in {GroupCount} groups: ";

	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly PostgresConnectionSettings _settings;
	private readonly IScheduler _scheduler;
	private readonly TimeProvider _timeProvider;
	private readonly ILogger<PostgresDataProvider> _logger;
	private readonly int _historyReadConcurrency;

	// docs/architecture/data-integration.md#the-catalogue-while-the-viewer-runs
	// Written from whichever pool thread a catalogue read resumes on.
	private readonly ConcurrentDictionary<(int PenId, int Column, string? StoredValue), byte> _warnedNormalisations =
		new();

	internal PostgresDataProvider(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		PostgresConnectionSettings settings,
		IScheduler scheduler,
		TimeProvider timeProvider,
		ILogger<PostgresDataProvider> logger,
		int historyReadConcurrency)
	{
		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_settings = settings;
		_scheduler = scheduler;
		_timeProvider = timeProvider;
		_logger = logger;
		_historyReadConcurrency = historyReadConcurrency;
	}

	/// <summary>
	/// The live edge of the requested variables. Cold: each subscription starts a poll loop of its own, on the
	/// injected scheduler and at the operator's <see cref="PostgresConnectionSettings.PollInterval"/>, holding
	/// its own baseline; disposing it cancels the loop's query and its wait, so no further statement is issued.
	/// </summary>
	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)
	{
		var subscribedIds = penIds.ToArray();

		return Observable.Create<IReadOnlyList<Sample>>(observer =>
			_scheduler.ScheduleAsync((_, cancellationToken) => PollAsync(subscribedIds, observer, cancellationToken)));
	}

	/// <summary>
	/// Every configured variable, ordered by name and then by id.
	/// </summary>
	public async Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = CreateCatalogueCommand(connection);
			await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

			var pens = new List<Pen>();

			while (await reader.ReadAsync().ConfigureAwait(false))
			{
				pens.Add(ReadPen(reader));
			}

			return Result.Ok<IReadOnlyList<Pen>>(pens);
		}
		catch (Exception exception)
		{
			return Result.Fail<IReadOnlyList<Pen>>(Map(exception, ArchiveStatements.PenCatalogRelations));
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

	private async Task PollAsync(
		int[] penIds,
		IObserver<IReadOnlyList<Sample>> observer,
		CancellationToken cancellationToken)
	{
		var poll = new RealtimePoll(
			_dataSource,
			_timeConverter,
			_exceptionMapper,
			penIds,
			_logger);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var samples = await poll.ReadOnceAsync(cancellationToken).ConfigureAwait(false);

				// A disposal landing while the query is in flight delivers nothing further: the subscriber
				// is gone, and a batch arriving behind its back is a leaked loop by another name.
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (samples.Count > 0)
				{
					observer.OnNext(samples);
				}

				await _scheduler.Sleep(_settings.PollInterval, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// Disposal ends the loop.
		}
	}

	internal static NpgsqlCommand CreateCatalogueCommand(NpgsqlConnection connection)
	{
		return new NpgsqlCommand(ArchiveStatements.PenCatalog, connection)
		{
			CommandTimeout = CatalogueCommandTimeoutSeconds
		};
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

	private Pen ReadPen(NpgsqlDataReader reader)
	{
		var penId = reader.GetInt32(PenCatalogColumn.Id);
		var (scaleMin, scaleMax) = ReadScalePair(reader);

		return new Pen(
			penId,
			reader.GetString(PenCatalogColumn.Name),
			reader.GetFieldValue<string[]>(PenCatalogColumn.Groups),
			ReadColor(reader, penId),
			reader.IsDBNull(PenCatalogColumn.Unit) ? null : reader.GetString(PenCatalogColumn.Unit),
			ReadFormat(reader, penId),
			reader.GetBoolean(PenCatalogColumn.EnabledOnStart),
			scaleMin,
			scaleMax,
			reader.GetBoolean(PenCatalogColumn.LogScaleOnStart),
			ReadLineStyle(reader, penId));
	}

	// semiplot_tags_scale_paired forbids a half-set pair, so one reaching here is a hand-edited row on an
	// installation without the constraint: it autoscales rather than opening on a bound with no partner.
	private static (double? Min, double? Max) ReadScalePair(NpgsqlDataReader reader)
	{
		if (reader.IsDBNull(PenCatalogColumn.ScaleMinOnStart) || reader.IsDBNull(PenCatalogColumn.ScaleMaxOnStart))
		{
			return (null, null);
		}

		return (reader.GetDouble(PenCatalogColumn.ScaleMinOnStart), reader.GetDouble(PenCatalogColumn.ScaleMaxOnStart));
	}

	// The server validates a stored colour against the hex pattern, so only its absence is handled here.
	private string ReadColor(NpgsqlDataReader reader, int penId)
	{
		if (!reader.IsDBNull(PenCatalogColumn.Color))
		{
			return reader.GetString(PenCatalogColumn.Color);
		}

		_logger.Log(
			NormalisationLevel(penId, PenCatalogColumn.Color, null),
			"Pen {PenId} carries no colour; it is drawn in {FallbackColor} until one is commissioned.",
			penId,
			UncommissionedPenColor);

		return UncommissionedPenColor;
	}

	// No server-side check can parse a .NET mask, so the character rule runs here and an unusable mask
	// leaves the record with none.
	private string? ReadFormat(NpgsqlDataReader reader, int penId)
	{
		if (reader.IsDBNull(PenCatalogColumn.Format))
		{
			return null;
		}

		var storedMask = reader.GetString(PenCatalogColumn.Format);

		if (PenValueFormat.IsAcceptable(storedMask))
		{
			return storedMask;
		}

		_logger.Log(
			NormalisationLevel(penId, PenCatalogColumn.Format, storedMask),
			"Pen {PenId} carries format {StoredMask}, which cannot render a reading; it is drawn under {FallbackMask}.",
			penId,
			storedMask,
			PenValueFormat.FallbackMask);

		return null;
	}

	private PenLineStyle ReadLineStyle(NpgsqlDataReader reader, int penId)
	{
		var storedValue = reader.GetInt16(PenCatalogColumn.LineStyle);

		return StoredLineStyle.Read(
			storedValue,
			penId,
			_logger,
			() => NormalisationLevel(
				penId,
				PenCatalogColumn.LineStyle,
				storedValue.ToString(CultureInfo.InvariantCulture)));
	}

	private LogLevel NormalisationLevel(int penId, int column, string? storedValue)
	{
		return _warnedNormalisations.TryAdd((penId, column, storedValue), 0) ? LogLevel.Warning : LogLevel.Debug;
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
