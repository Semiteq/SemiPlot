using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.Tests.Unit.UI.Bridge;

internal sealed class FakeDataProvider(
	IScheduler scheduler,
	TimeSpan realtimeInterval,
	IReadOnlyList<Pen>? pens = null) : IDataProvider
{
	// The default last-column center value an unoverridden layer returns; tests assert against this.
	public const double DefaultCenter = 2.0;
	// A deterministic epoch: tests assert batch structure and dispatch, not a history-to-realtime join.
	private static readonly DateTime _realtimeEpoch = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);
	private readonly TimeSpan _realtimeInterval = realtimeInterval;

	private readonly IScheduler _scheduler = scheduler;

	// Hot and never completed, like the real provider's. A test pushes into it through ReportConnectionState
	// instead of waiting for a tick that this fake never runs.
	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

	public DateTime ArchiveFirstUtc { get; set; } = new(2025, 12, 25, 0, 0, 0, DateTimeKind.Utc);

	public DateTime ArchiveLastUtc { get; set; } = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Utc);

	public ArchiveExtent? ArchiveExtentOverride { get; set; }

	public bool FailHistory { get; set; }

	// The catalogue and extent reads succeed unconditionally otherwise, which is what the startup path
	// could never be tested against. Both switches answer with a typed error, the shape a real provider
	// returns, so a consumer routing on error type is exercised rather than one reading a message.
	public bool FailPens { get; set; }

	public bool FailExtent { get; set; }

	// Held tasks for the catalogue and extent reads, completed by the test or never, to drive a caller's own
	// bound. Awaited by production code and completed by the test body with an inline continuation, so none
	// of these gates may take RunContinuationsAsynchronously (the opposite of ChartHistoryRequestDebouncerTests').
	public TaskCompletionSource<Result<IReadOnlyList<Pen>>> PensGate { get; } = new();

	public TaskCompletionSource<Result<ArchiveExtent>> ExtentGate { get; } = new();

	public bool GatePens { get; set; }

	public bool GateExtent { get; set; }

	// When set, a query for this layer returns the gate's task, holding it in flight so a newer query can
	// complete first (cross-path race).
	public AggregationLayer? GatedLayer { get; set; }

	// Per-layer center value override: lets a test distinguish which layer's result was applied last
	// (e.g. an initial Raw load vs. a superseding coarser-layer gesture re-query).
	public Dictionary<AggregationLayer, double> LayerCenterOverrides { get; } = [];

	// Plain, for the reason stated at PensGate: TrendChartViewModelTests asserts inline after SetResult.
	public TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>> HistoryGate { get; } = new();

	// Pen identifiers the history read answers with no envelope at all, the shape a real provider returns
	// for a pen holding no row in the window. A requested pen missing from the result is not an error.
	public HashSet<int> OmittedPenIds { get; } = [];

	public int HistoryQueryCount { get; private set; }

	public IReadOnlyList<int>? LastQueriedPenIds { get; private set; }

	public AggregationLayer? LastQueriedLayer { get; private set; }

	public DateTime? LastQueriedFromUtc { get; private set; }

	public DateTime? LastQueriedToUtc { get; private set; }

	public int? LastQueriedTargetColumnCount { get; private set; }

	public IReadOnlyList<Pen> Pens { get; } = pens ??
		[
			new Pen(1, "Pen 1", "Group A", "#ff0000"),
			new Pen(2, "Pen 2", "Group A", "#00ff00")
		];

	// Off by default: every pen shares one timestamp per tick. Set, each pen's sample offsets by its own
	// position, matching the real archive's per-variable, change-based delivery, where two variables rarely
	// share a timestamp.
	public bool StaggerRealtimeTimestamps { get; set; }

	public IObservable<ArchiveConnectionState> ConnectionFaults => _connectionFaults;

	// The seam a test drives the connection banner from: the fake runs no poll, so nothing else would ever
	// push a state onto the stream.
	public void ReportConnectionState(ArchiveConnectionState state)
	{
		_connectionFaults.OnNext(state);
	}

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)
	{
		var subscribed = penIds.Where(id => Pens.Any(pen => pen.PenId == id)).ToArray();

		return Observable
			.Interval(_realtimeInterval, _scheduler)
			.Select(tick => (IReadOnlyList<Sample>)[.. subscribed
				.Select((id, index) => new Sample(
					id,
					_realtimeEpoch
					+ TimeSpan.FromTicks(_realtimeInterval.Ticks * (tick + 1))
					+ (StaggerRealtimeTimestamps ? TimeSpan.FromMilliseconds(index) : TimeSpan.Zero),
					id + tick))]);
	}

	public Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()
	{
		if (FailPens)
		{
			return Task.FromResult(
				Result.Fail<IReadOnlyList<Pen>>(new ArchiveError(ArchiveFault.Unreachable, "bench", 5432, "semiplot_dev")));
		}

		if (GatePens)
		{
			return PensGate.Task;
		}

		return Task.FromResult(Result.Ok(Pens));
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
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
			.Where(id => !OmittedPenIds.Contains(id))
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
		if (FailExtent)
		{
			return Task.FromResult(
				Result.Fail<ArchiveExtent>(new ArchiveError(ArchiveFault.ReadFailed, "bench", 5432, "semiplot_dev", "42601")));
		}

		if (GateExtent)
		{
			return ExtentGate.Task;
		}

		return Task.FromResult(Result.Ok(ArchiveExtentOverride ?? new ArchiveExtent(ArchiveFirstUtc, ArchiveLastUtc)));
	}
}
