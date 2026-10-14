using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>
/// Turns a visible window into the range to fetch, and decides whether a fetched range still serves the window
/// in view.
/// </summary>
public static class HistoryPrefetch
{
	// Visible window widths of margin fetched on each side.
	public const int MarginWindows = 1;

	/// <summary>
	/// The window in view alone, plus its right margin when <paramref name="withRightMargin"/> holds; the left edge is
	/// clamped at the first sample as <see cref="Expand"/> clamps it.
	/// </summary>
	public static FetchRange Visible(
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget,
		DateTime firstSampleUtc,
		bool withRightMargin)
	{
		var rightMarginWindows = withRightMargin ? MarginWindows : 0;

		return Build(fromUtc, toUtc, layer, columnTarget, firstSampleUtc, 0, rightMarginWindows);
	}

	public static FetchRange Expand(
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget,
		DateTime firstSampleUtc)
	{
		return Build(fromUtc, toUtc, layer, columnTarget, firstSampleUtc, MarginWindows, MarginWindows);
	}

	private static FetchRange Build(
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget,
		DateTime firstSampleUtc,
		int leftMarginWindows,
		int rightMarginWindows)
	{
		var width = toUtc - fromUtc;
		var expandedFrom = fromUtc - (leftMarginWindows * width);

		if (expandedFrom < firstSampleUtc)
		{
			expandedFrom = firstSampleUtc;
		}

		return new FetchRange(
			expandedFrom,
			toUtc + (rightMarginWindows * width),
			layer,
			columnTarget * (1 + leftMarginWindows + rightMarginWindows),
			width,
			leftMarginWindows,
			rightMarginWindows);
	}

	/// <summary>
	/// True when the window in view is drawn entirely from the fetched range, so no query is due.
	/// </summary>
	public static bool Covers(
		FetchRange fetched,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int columnTarget,
		DateTime firstSampleUtc)
	{
		if (fetched.Layer != layer || fetched.ColumnTarget != columnTarget * fetched.ColumnFactor)
		{
			return false;
		}

		if (toUtc - fromUtc != fetched.WindowWidth)
		{
			return false;
		}

		var rightMargin = fetched.RightMarginWindows * fetched.WindowWidth;

		// The left edge alone is ever clamped, so what lies left of the visible window is the whole range less
		// that window and the unclamped right margin.
		var leftMargin = fetched.ToUtc - fetched.FromUtc - fetched.WindowWidth - rightMargin;
		var reachesTheFirstSample = fetched.FromUtc <= firstSampleUtc;

		// The inner band ends half of each side's own margin in from that fetched edge, so the next query is
		// issued while the rest of the margin still covers the gesture that triggered it. No row lies before
		// the first sample, so a range reaching it has no left edge to approach.
		return (reachesTheFirstSample || fromUtc >= fetched.FromUtc + (leftMargin / 2))
			&& toUtc <= fetched.ToUtc - (rightMargin / 2);
	}
}
