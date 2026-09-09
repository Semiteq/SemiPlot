using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// The width is stored rather than derived, because the left clamp shortens the range.
public readonly record struct FetchRange(
	DateTime FromUtc,
	DateTime ToUtc,
	AggregationLayer Layer,
	int ColumnTarget,
	TimeSpan WindowWidth);
