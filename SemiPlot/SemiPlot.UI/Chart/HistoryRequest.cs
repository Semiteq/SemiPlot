using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Sequence is a monotonic stamp assigned at request time so a slow query can never overwrite a newer
// window's result.
public sealed record HistoryRequest(
	IReadOnlyList<int> PenIds,
	DateTime FromUtc,
	DateTime ToUtc,
	AggregationLayer Layer,
	long Sequence,
	int TargetColumnCount);
