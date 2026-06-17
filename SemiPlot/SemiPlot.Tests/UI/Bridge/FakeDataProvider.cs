using System.Reactive.Concurrency;
using System.Reactive.Linq;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.Tests.UI.Bridge;

internal sealed class FakeDataProvider : IDataProvider
{
	// A fixed anchor on purpose: the coordinator tests assert batch structure and dispatch, not a
	// history-to-realtime timestamp join, so a deterministic epoch keeps them stable.
	private static readonly DateTime RealtimeEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	private readonly IScheduler _scheduler;
	private readonly TimeSpan _realtimeInterval;

	public DateTime ArchiveFirstUtc { get; set; } = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);

	public DateTime ArchiveLastUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

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

	public IReadOnlyList<Pen> Pens { get; }

	public bool FailHistory { get; set; }

	// When set, a history query for this exact layer returns this gate's task instead of completing
	// synchronously, so a test can hold one query in flight while a newer one completes (cross-path race).
	public AggregationLayer? GatedLayer { get; set; }

	public TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>> HistoryGate { get; } = new();

	public int HistoryQueryCount { get; private set; }

	public IReadOnlyList<long>? LastQueriedPenIds { get; private set; }

	public AggregationLayer? LastQueriedLayer { get; private set; }

	public DateTime? LastQueriedFromUtc { get; private set; }

	public DateTime? LastQueriedToUtc { get; private set; }

	public int? LastQueriedTargetColumnCount { get; private set; }

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)
	{
		var subscribed = penIds.Where(id => Pens.Any(pen => pen.ProjectVarId == id)).ToArray();

		return Observable
			.Interval(_realtimeInterval, _scheduler)
			.Select(tick => (IReadOnlyList<Sample>)subscribed
				.Select(id => new Sample(
					id,
					RealtimeEpoch + TimeSpan.FromTicks(_realtimeInterval.Ticks * (tick + 1)),
					id + tick))
				.ToArray());
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

		var envelopes = penIds
			.Select(id => new PenHistoryEnvelope(
				id,
				[fromUtc, toUtc],
				[1.0, 2.0],
				[1.0, 2.0],
				[1.0, 2.0]))
			.ToArray();

		return Task.FromResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(envelopes));
	}

	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		return Task.FromResult(Result.Ok(new ArchiveExtent(ArchiveFirstUtc, ArchiveLastUtc)));
	}
}
