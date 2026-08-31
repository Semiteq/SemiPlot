using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

public interface IDataProvider
{
	// Cold per call: no samples flow until subscribed; the subscriber disposes the returned IDisposable.
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

	// Hot, shared by every subscription and never terminating: it neither completes nor faults, so a
	// consumer subscribes with an onNext handler alone. Every subscription's first successful tick reports
	// ArchiveConnectionState.Connected on it — the only observable point at which that subscription is known
	// to be armed — and a run of failed ticks reports the fault on it rather than through OnError.
	IObservable<ArchiveConnectionState> ConnectionFaults { get; }

	Task<Result<IReadOnlyList<Pen>>> QueryPensAsync();

	Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount);

	Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();
}
