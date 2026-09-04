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
/// Reads the archive over the pooled <see cref="NpgsqlDataSource"/>: <see cref="QueryPensAsync"/> answers the
/// configured variables, <see cref="QueryArchiveExtentAsync"/> the span they cover, <see cref="QueryHistoryAsync"/>
/// a window of one layer and <see cref="Subscribe"/> the live edge, all crossing the boundary in UTC.
/// </summary>
public sealed class PostgresDataProvider : IDataProvider
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly PostgresConnectionSettings _settings;
	private readonly IScheduler _scheduler;
	private readonly ILogger<PostgresDataProvider> _logger;

	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

	internal PostgresDataProvider(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		PostgresConnectionSettings settings,
		IScheduler scheduler,
		ILogger<PostgresDataProvider> logger)
	{
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
	/// The live edge of the requested variables. Cold: each subscription starts a poll loop of its own, on the
	/// injected scheduler and at the operator's <see cref="PostgresConnectionSettings.PollInterval"/>, holding
	/// its own baseline; disposing it cancels the loop's query and its wait, so no further statement is issued.
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
	/// </summary>
	public async Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		var arguments = ValidateArguments(fromUtc, toUtc, targetColumnCount);

		if (arguments.IsFailed)
		{
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(arguments.Errors);
		}

		// Behind the Result checks on purpose: a failed Result wins over the throw.
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
			return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(Map(exception, ArchiveStatements.TrendsRelation));
		}
	}

	/// <summary>
	/// The span the configured variables cover, in UTC. It is the span of the catalogue, not of the archive:
	/// rooted at <c>semiplot_tags</c>, so an empty catalogue over a full archive reports
	/// <see cref="ArchiveExtent.Empty"/>, same as a seeded catalogue over an empty <c>trends</c> — both succeed.
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
			// trends on purpose: the startup catalogue read already reports a missing semiplot_tags.
			return Result.Fail<ArchiveExtent>(Map(exception, ArchiveStatements.TrendsRelation));
		}
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
			// Disposal ends the loop.
		}
	}

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

	// Bound on the archive's own wall clock: a UTC round trip is not injective across DST.
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

	private async Task<IReadOnlyList<HistoryRowFold.Row>> FillFreshTailAsync(
		NpgsqlConnection connection,
		int[] penIds,
		DateTime fromLocal,
		DateTime toLocal,
		AggregationLayer layer,
		IReadOnlyList<HistoryRowFold.Row> coarseRows)
	{
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
		var error = _exceptionMapper.Map(exception, relation);

		// An empty-detail ReadFailed is a fault in this code, so it is logged.
		if (error is ArchiveError { Kind: ArchiveFault.ReadFailed, Detail.Length: 0 })
		{
			_logger.LogError(exception, "The archive read failed with an exception the provider did not expect.");
		}

		return error;
	}
}
