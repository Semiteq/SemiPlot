using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

public interface IDataProvider
{
	IReadOnlyList<Pen> Pens { get; }

	// Cold per call: no samples flow until subscribed; the subscriber disposes the returned IDisposable.
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds);

	Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount);

	Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();
}
