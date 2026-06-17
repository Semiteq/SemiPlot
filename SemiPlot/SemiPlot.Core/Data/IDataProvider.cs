using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

public interface IDataProvider
{
	IReadOnlyList<Pen> Pens { get; }

	// Cold per call: each call returns an independent sequence and no samples flow until subscribed.
	// The subscriber owns the returned IDisposable and tears the subscription down by disposing it.
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds);

	// Returns a decimated min/max envelope per pen sized to targetColumnCount. The stub decimates
	// in process; a future server-side SQL aggregate replaces that behind this same seam.
	Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount);
}
