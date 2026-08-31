using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Reads the archive over the pooled <see cref="NpgsqlDataSource"/>. <see cref="QueryPensAsync"/> answers
/// the configured variables, <see cref="QueryArchiveExtentAsync"/> the span they cover,
/// <see cref="QueryHistoryAsync"/> a window of one layer and <see cref="Subscribe"/> the live edge, all
/// crossing the boundary in UTC. Every failure leaves through the public error vocabulary — nothing
/// internal crosses the boundary — and a <c>42P01</c> is mapped against the relation the failing read
/// names, because each read knows which relations its own statement touches.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly PostgresConnectionSettings _settings;
	private readonly IScheduler _scheduler;
	private readonly ILogger<PostgresDataProvider> _logger;

	// Hot, shared across every subscription and never completed. A subscription's first successful tick
	// reports Connected on it, which is the only observable point at which that subscription is known to be
	// armed, and a run of failed ticks reports the fault on it rather than through OnError.
	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

	// Internal because two of its parameters are: a public constructor over an internal type is CS0051.
	internal PostgresDataProvider(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		PostgresConnectionSettings settings,
		IScheduler scheduler,
		ILogger<PostgresDataProvider> logger)
	{
		ArgumentNullException.ThrowIfNull(dataSource);
		ArgumentNullException.ThrowIfNull(timeConverter);
		ArgumentNullException.ThrowIfNull(exceptionMapper);
		ArgumentNullException.ThrowIfNull(settings);
		ArgumentNullException.ThrowIfNull(scheduler);
		ArgumentNullException.ThrowIfNull(logger);

		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_settings = settings;
		_scheduler = scheduler;
		_logger = logger;
	}

	/// <summary>
	/// The connection state of the live edge. Hot, shared by every subscription and never terminating, as
	/// <see cref="IDataProvider.ConnectionFaults"/> states.
	/// </summary>
	public IObservable<ArchiveConnectionState> ConnectionFaults => _connectionFaults;

	/// <summary>
	/// The live edge of the requested variables. Cold: each subscription starts a poll loop of its own, on
	/// the injected scheduler and at the operator's <see cref="PostgresConnectionSettings.PollInterval"/>,
	/// holding a baseline of its own. Disposing the subscription cancels the loop's query and its wait, so
	/// no further statement is issued.
	/// <para>
	/// The sequence never completes and never faults. Its consumer subscribes with an onNext handler alone,
	/// so an OnError would go unhandled on the UI scheduler; a failing tick therefore emits no sample — the
	/// consumer keeps the data it has — and says so on <see cref="ConnectionFaults"/> instead.
	/// </para>
	/// </summary>
	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		var subscribedIds = penIds.ToArray();

		return Observable.Create<IReadOnlyList<Sample>>(observer =>
			_scheduler.ScheduleAsync((_, cancellationToken) => PollAsync(subscribedIds, observer, cancellationToken)));
	}

	/// <summary>
	/// Every configured variable, ordered by group then name.
	/// </summary>
	public async Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);
			await using var command = new NpgsqlCommand(ArchiveStatements.PenCatalog, connection);
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
			return Result.Fail<IReadOnlyList<Pen>>(Map(exception, ArchiveStatements.TagCatalogRelation));
		}
	}

	/// <summary>
	/// A window of one layer for the pens the caller asks for, folded into one envelope per pen that has
	/// rows. A window holding no rows at all is a successful empty list rather than a failure.
	/// <para>
	/// The left edge is seeded: a pen whose last sample predates the window start is drawn from that
	/// sample, because <see cref="ArchiveStatements.SparseHistoryWindow"/> returns it on the same round
	/// trip as the window rows.
	/// </para>
	/// <para>
	/// A pen with no row in the window and none inside that statement's bounded look-back still gets no
	/// envelope, and the consumer side drops it — <c>TrendChartViewModel.ApplyHistory</c> drops a
	/// requested pen the result omits, so no pen carries the previous window's envelope. See
	/// docs/architecture/data-integration.md.
	/// </para>
	/// <para>
	/// The right edge is filled from the raw layer: a coarse layer is flushed on its own cadence, so a
	/// window reaching the live edge stops short of it. <see cref="FreshTail"/> holds the bound and the
	/// merge, and states why a pen too far behind that bound keeps its short edge instead.
	/// </para>
	/// </summary>
	public async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		// The target guard turns a fault that would otherwise be intermittent into a deterministic one —
		// the decimator is only reached when a pen has rows, so a target below one succeeds on an empty
		// window and fails on a full one.
		var arguments = ValidateArguments(fromUtc, toUtc, targetColumnCount);

		if (arguments.IsFailed)
		{
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(arguments.Errors);
		}

		// The layer guard sits behind the Result-returning checks rather than ahead of them: a caller
		// supplying two bad arguments at once is answered by the range and target checks first, so the
		// failed Result wins over the throw. That ordering is this provider's contract and
		// AnInvertedWindowAnswersAheadOfAnUndefinedAggregationLayer pins it.
		if (!Enum.IsDefined(layer))
		{
			throw new ArgumentOutOfRangeException(nameof(layer), layer, "Unknown aggregation layer.");
		}

		var ids = penIds.ToArray();

		try
		{
			await using var connection = await _dataSource.OpenConnectionAsync().ConfigureAwait(false);

			var fromLocal = _timeConverter.ToArchiveLocal(fromUtc);
			var toLocal = _timeConverter.ToArchiveLocal(toUtc);

			var rows = await ReadWindowAsync(connection, ids, fromLocal, toLocal, layer).ConfigureAwait(false);

			rows = await FillFreshTailAsync(connection, ids, fromLocal, toLocal, layer, rows).ConfigureAwait(false);

			return Result.Ok(HistoryRowFold.Fold(rows, _timeConverter, targetColumnCount));
		}
		catch (Exception exception)
		{
			// The statement touches one relation, so a 42P01 here can only mean trends.
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(Map(exception, ArchiveStatements.TrendsRelation));
		}
	}

	/// <summary>
	/// The span the configured variables cover, in UTC. It is the span of the catalogue rather than of the
	/// archive: the statement is rooted at <c>semiplot_tags</c>, so an empty catalogue over an archive full
	/// of rows reports <see cref="ArchiveExtent.Empty"/>, the same answer a seeded catalogue over an empty
	/// <c>trends</c> gives. Both are successful reads — a null bound is a content state, not a failure.
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
			// trends rather than semiplot_tags: this statement touches both, and at startup the catalogue
			// read runs first, so a missing semiplot_tags fails there and never reaches here. A catalogue
			// dropped under a live session is reported as a missing trends, which reaches a log line only
			// and sends the operator to the same remedy either table needs.
			return Result.Fail<ArchiveExtent>(Map(exception, ArchiveStatements.TrendsRelation));
		}
	}

	// One loop per subscription, holding a RealtimePoll of its own and therefore a baseline of its own.
	// Nothing leaves through OnError or OnCompleted: the consumer subscribes with an onNext handler alone,
	// and a failing tick is a state change on the signal stream rather than a fault on this sequence.
	private async Task PollAsync(
		int[] penIds,
		IObserver<IReadOnlyList<Sample>> observer,
		CancellationToken cancellationToken)
	{
		var poll = new RealtimePoll(
			_dataSource,
			_timeConverter,
			_exceptionMapper,
			_settings,
			penIds,
			_logger);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				var tick = await poll.ReadOnceAsync(cancellationToken).ConfigureAwait(false);

				// A disposal landing while the query is in flight delivers nothing further: the subscriber
				// is gone, and a batch arriving behind its back is a leaked loop by another name.
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (tick.Samples.Count > 0)
				{
					observer.OnNext(tick.Samples);
				}

				if (tick.StateChange is { } state)
				{
					_connectionFaults.OnNext(state);
				}

				await _scheduler.Sleep(_settings.PollInterval, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// The subscription was disposed, which is how this loop ends and the only way it does.
			// RealtimePoll lets this type out ahead of its mapper so a self-cancelled read never counts
			// towards the fault threshold.
		}
	}

	// Internal rather than private so a unit test can bind through this exact path and compare the names it
	// produces against the statement's own tokens — the drift no fence extractor sees.
	internal static void BindWindow(
		NpgsqlCommand command,
		ArchiveTimeConverter timeConverter,
		int[] penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer)
	{
		BindLocalWindow(
			command,
			penIds,
			timeConverter.ToArchiveLocal(fromUtc),
			timeConverter.ToArchiveLocal(toUtc),
			layer);
	}

	// The bounds the statement carries are the archive's own naive wall clock, so the tail read — whose
	// start is computed from timestamps the archive returned — binds them directly rather than converting
	// out and back, which is neither order-preserving nor injective across a daylight-saving boundary.
	private static void BindLocalWindow(
		NpgsqlCommand command,
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer)
	{
		command.Parameters.Add(new NpgsqlParameter("ids", NpgsqlDbType.Array | NpgsqlDbType.Integer)
		{
			Value = penIds
		});
		command.Parameters.Add(new NpgsqlParameter("layer", NpgsqlDbType.Smallint) { Value = (short)layer });
		command.Parameters.Add(new NpgsqlParameter("from", NpgsqlDbType.Timestamp) { Value = fromLocal });
		command.Parameters.Add(new NpgsqlParameter("to", NpgsqlDbType.Timestamp) { Value = toLocal });
	}

	// One bind of the windowed statement, on a connection the caller owns so the tail read shares it.
	private async Task<IReadOnlyList<HistoryRowFold.Row>> ReadWindowAsync(
		NpgsqlConnection connection,
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer)
	{
		await using var command = new NpgsqlCommand(ArchiveStatements.SparseHistoryWindow, connection);

		BindLocalWindow(command, penIds, fromLocal, toLocal, layer);

		await using var reader = await command.ExecuteReaderAsync().ConfigureAwait(false);

		var rows = new List<HistoryRowFold.Row>();

		while (await reader.ReadAsync().ConfigureAwait(false))
		{
			rows.Add(ReadHistoryRow(reader));
		}

		return rows;
	}

	// A coarse layer is flushed on its own cadence, so a window reaching the live edge stops up to one
	// point spacing short of it. The tail is a second bind of the same statement at layer 0 — no statement
	// of its own, so nothing new is pinned and the existing EXPLAIN guard already covers the shape.
	private async Task<IReadOnlyList<HistoryRowFold.Row>> FillFreshTailAsync(
		NpgsqlConnection connection,
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer,
		IReadOnlyList<HistoryRowFold.Row> coarseRows)
	{
		// Ahead of the seams, which walk the whole result set: the raw layer is what a tail is read from,
		// so this is the one read that never pays anything for the tail at all.
		if (layer == AggregationLayer.Raw)
		{
			return coarseRows;
		}

		var seams = FreshTail.Seams(coarseRows, penIds, fromLocal);

		if (FreshTail.Start(layer, seams, toLocal) is not { } tailStart)
		{
			return coarseRows;
		}

		var tailRows = await ReadWindowAsync(connection, penIds, tailStart, toLocal, AggregationLayer.Raw)
			.ConfigureAwait(false);

		return FreshTail.Merge(coarseRows, tailRows, seams, tailStart);
	}

	// The window bounds and the target column count are reported through the Result channel, in the wording
	// the caller reads back.
	private static Result ValidateArguments(DateTime fromUtc, DateTime toUtc, int targetColumnCount)
	{
		if (fromUtc > toUtc)
		{
			return Result.Fail($"Invalid range: fromUtc ({fromUtc:O}) is after toUtc ({toUtc:O}).");
		}

		if (targetColumnCount < 1)
		{
			return Result.Fail($"Invalid target column count: {targetColumnCount} (must be at least one).");
		}

		return Result.Ok();
	}

	// A plain projection of the columns: the fold owns the conversion, so the naive timestamp crosses
	// unchanged.
	private static HistoryRowFold.Row ReadHistoryRow(NpgsqlDataReader reader)
	{
		return new HistoryRowFold.Row(
			reader.GetInt32(0),
			reader.GetDateTime(1),
			reader.IsDBNull(2) ? null : reader.GetDouble(2),
			reader.GetInt32(3));
	}

	private Pen ReadPen(NpgsqlDataReader reader)
	{
		var penId = reader.GetInt32(0);

		return new Pen(
			penId,
			reader.GetString(1),
			reader.IsDBNull(2) ? string.Empty : reader.GetString(2),
			reader.IsDBNull(3) ? string.Empty : reader.GetString(3),
			ReadLineStyle(reader.GetInt16(4), penId));
	}

	// The stored value is the member's ordinal. An unrecognised value draws interpolated rather than
	// failing the read: one malformed row must not hide every other pen.
	private PenLineStyle ReadLineStyle(short storedValue, int penId)
	{
		if (Enum.IsDefined((PenLineStyle)storedValue))
		{
			return (PenLineStyle)storedValue;
		}

		_logger.LogWarning(
			"Pen {PenId} carries line_style {StoredValue}, which this build does not recognise; it is drawn interpolated.",
			penId,
			storedValue);

		return PenLineStyle.Interpolated;
	}

	// The archive stores naive local wall-clock time, so each bound crosses out through the converter
	// exactly once.
	private async Task<ArchiveExtent> ReadExtentAsync(NpgsqlDataReader reader)
	{
		if (!await reader.ReadAsync().ConfigureAwait(false))
		{
			return ArchiveExtent.Empty;
		}

		if (reader.IsDBNull(0) || reader.IsDBNull(1))
		{
			return ArchiveExtent.Empty;
		}

		return new ArchiveExtent(
			_timeConverter.ToUtc(reader.GetDateTime(0)),
			_timeConverter.ToUtc(reader.GetDateTime(1)));
	}

	// The relation the failing read names fills the 42P01 detail: each read knows the relation its own
	// statement touches, so nothing is asked of the server to learn it.
	private Error Map(Exception exception, string relation)
	{
		var error = _exceptionMapper.Map(exception, relation);

		// A read that fails with no server answer behind it — a null reference or a bad cast inside the row
		// read — is a fault in this code, and the typed error alone dresses it as a server state. It still
		// crosses typed, because nothing may escape the boundary; the log is where it stays visible.
		if (error is ArchiveError { Kind: ArchiveFault.ReadFailed, Detail.Length: 0 })
		{
			_logger.LogError(exception, "The archive read failed with an exception the provider did not expect.");
		}

		return error;
	}
}
