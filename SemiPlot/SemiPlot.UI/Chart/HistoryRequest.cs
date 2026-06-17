using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// A debounced gesture-driven history re-query: the visible window, the aggregation layer chosen for
// it, and the pens to load. Raised by the chart view model's navigation path and collapsed to a single
// trailing request per quiet period before the query crosses to the data scheduler. Sequence is a
// monotonic stamp assigned at request time so a slow query can never overwrite a newer window's result.
public sealed record HistoryRequest(
	IReadOnlyList<long> PenIds,
	DateTime FromUtc,
	DateTime ToUtc,
	AggregationLayer Layer,
	long Sequence);
