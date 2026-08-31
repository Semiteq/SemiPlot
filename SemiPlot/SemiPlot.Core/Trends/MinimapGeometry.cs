namespace SemiPlot.Core.Trends;

public static class MinimapGeometry
{
	// A zero-or-negative extent span (no data yet) yields the full strip so the highlight never collapses.
	public static (double Start, double Width) WindowFraction(
		DateTime extentFirst,
		DateTime extentLast,
		DateTime windowFrom,
		DateTime windowTo)
	{
		var span = (extentLast - extentFirst).TotalSeconds;
		if (span <= 0.0)
		{
			return (0.0, 1.0);
		}

		var start = Math.Clamp((windowFrom - extentFirst).TotalSeconds / span, 0.0, 1.0);
		var end = Math.Clamp((windowTo - extentFirst).TotalSeconds / span, 0.0, 1.0);

		return (start, end - start);
	}

	public static DateTime TimeAtFraction(DateTime extentFirst, DateTime extentLast, double fraction)
	{
		var span = extentLast - extentFirst;

		return extentFirst + (span * Math.Clamp(fraction, 0.0, 1.0));
	}
}
