using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>
/// Turns a visible window into the wider window to fetch, and decides whether a fetched range still serves
/// the window in view.
/// </summary>
public static class HistoryPrefetch
{
	// Visible window widths of margin fetched on each side.
	public const int MarginWindows = 1;

	// Columns a request asks for per visible column, so the whole fetched range stays at one column per pixel.
	public const int MarginColumnFactor = (2 * MarginWindows) + 1;

	/// <summary>The columns a request asks for to hold a visible target across the whole fetched range.</summary>
	public static int ScaleColumnTarget(int columnTarget)
	{
		return columnTarget * MarginColumnFactor;
	}

	public static FetchRange Expand(
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget,
		DateTime firstSampleUtc)
	{
		var width = toUtc - fromUtc;
		var margin = MarginWindows * width;
		var expandedFrom = fromUtc - margin;

		if (expandedFrom < firstSampleUtc)
		{
			expandedFrom = firstSampleUtc;
		}

		return new FetchRange(
			expandedFrom, toUtc + margin, layer, ScaleColumnTarget(columnTarget), width);
	}

	/// <summary>
	/// True when the window in view is drawn entirely from the fetched range, so no query is due.
	/// </summary>
	public static bool Covers(
		FetchRange fetched,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget)
	{
		if (fetched.Layer != layer || fetched.ColumnTarget != ScaleColumnTarget(columnTarget))
		{
			return false;
		}

		if (toUtc - fromUtc != fetched.WindowWidth)
		{
			return false;
		}

		var rightMargin = MarginWindows * fetched.WindowWidth;

		// Expand clamps the left edge only, so what lies left of the visible window is the whole range less
		// that window and the unclamped right margin.
		var leftMargin = fetched.ToUtc - fetched.FromUtc - fetched.WindowWidth - rightMargin;

		// The inner band ends half of each side's own margin in from that fetched edge, so the next query is
		// issued while the rest of the margin still covers the gesture that triggered it.
		return fromUtc >= fetched.FromUtc + (leftMargin / 2)
			&& toUtc <= fetched.ToUtc - (rightMargin / 2);
	}
}
