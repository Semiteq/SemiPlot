using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// The width is stored rather than derived, because the left clamp shortens the range.
public readonly record struct FetchRange(
	DateTime FromUtc,
	DateTime ToUtc,
	AggregationLayer Layer,
	int ColumnTarget,
	TimeSpan WindowWidth,
	int LeftMarginWindows,
	int RightMarginWindows)
{
	/// <summary>Columns the range asks for per visible column.</summary>
	public int ColumnFactor => 1 + LeftMarginWindows + RightMarginWindows;
}
