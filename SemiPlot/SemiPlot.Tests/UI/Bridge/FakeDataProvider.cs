using System.Reactive.Concurrency;
using System.Reactive.Linq;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.Tests.UI.Bridge;

internal sealed class FakeDataProvider : IDataProvider
{
	// The default last-column center value an unoverridden layer returns; tests assert against this.
	public const double DefaultCenter = 2.0;
	// A deterministic epoch: tests assert batch structure and dispatch, not a history-to-realtime join.
	private static readonly DateTime _realtimeEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private readonly TimeSpan _realtimeInterval;

	private readonly IScheduler _scheduler;

	public FakeDataProvider(IScheduler scheduler, TimeSpan realtimeInterval)
	{
		_scheduler = scheduler;
		_realtimeInterval = realtimeInterval;
		Pens =
		[
			new Pen(1, "Pen 1", "Group A", "#ff0000"),
			new Pen(2, "Pen 2", "Group A", "#00ff00")
		];
	}

	public DateTime ArchiveFirstUtc { get; set; } = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);

	public DateTime ArchiveLastUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public bool FailHistory { get; set; }

	// When set, a query for this layer returns the gate's task, holding it in flight so a newer query can
	// complete first (cross-path race).
	public AggregationLayer? GatedLayer { get; set; }

	// Per-layer center value override: lets a test distinguish which layer's result was applied last
	// (e.g. an initial Raw load vs. a superseding coarser-layer gesture re-query).
	public Dictionary<AggregationLayer, double> LayerCenterOverrides { get; } = [];

	public TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>> HistoryGate { get; } = new();

	public int HistoryQueryCount { get; private set; }

	public IReadOnlyList<long>? LastQueriedPenIds { get; private set; }

	public AggregationLayer? LastQueriedLayer { get; private set; }

	public DateTime? LastQueriedFromUtc { get; private set; }

	public DateTime? LastQueriedToUtc { get; private set; }

	public int? LastQueriedTargetColumnCount { get; private set; }

	public IReadOnlyList<Pen> Pens { get; }

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)
	{
		var subscribed = penIds.Where(id => Pens.Any(pen => pen.PenId == id)).ToArray();

		return Observable
			.Interval(_realtimeInterval, _scheduler)
			.Select(tick => (IReadOnlyList<Sample>)subscribed
				.Select(id => new Sample(
					id,
					_realtimeEpoch + TimeSpan.FromTicks(_realtimeInterval.Ticks * (tick + 1)),
					id + tick))
				.ToArray());
	}

	public Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		return Task.FromResult(Result.Ok(Pens));
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		HistoryQueryCount++;
		LastQueriedPenIds = penIds;
		LastQueriedLayer = layer;
		LastQueriedFromUtc = fromUtc;
		LastQueriedToUtc = toUtc;
		LastQueriedTargetColumnCount = targetColumnCount;

		if (FailHistory)
		{
			return Task.FromResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("Forced history failure."));
		}

		if (GatedLayer == layer)
		{
			return HistoryGate.Task;
		}

		var center = LayerCenterOverrides.TryGetValue(layer, out var overridden)
			? overridden
			: DefaultCenter;

		var envelopes = penIds
			.Select(id => new PenHistoryEnvelope(
				id,
				[fromUtc, toUtc],
				[1.0, center],
				[1.0, center],
				[1.0, center]))
			.ToArray();

		return Task.FromResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(envelopes));
	}

	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		return Task.FromResult(Result.Ok(new ArchiveExtent(ArchiveFirstUtc, ArchiveLastUtc)));
	}
}
