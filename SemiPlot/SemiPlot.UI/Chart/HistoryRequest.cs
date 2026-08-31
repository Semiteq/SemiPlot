using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed record HistoryRequest(
	IReadOnlyList<int> PenIds,
	DateTime FromUtc,
	DateTime ToUtc,
	AggregationLayer Layer,
	int TargetColumnCount);
