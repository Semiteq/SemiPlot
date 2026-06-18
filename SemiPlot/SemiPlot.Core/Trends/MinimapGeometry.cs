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

		var start = Clamp01((windowFrom - extentFirst).TotalSeconds / span);
		var end = Clamp01((windowTo - extentFirst).TotalSeconds / span);

		return (start, end - start);
	}

	public static DateTime TimeAtFraction(DateTime extentFirst, DateTime extentLast, double fraction)
	{
		var span = extentLast - extentFirst;

		return extentFirst + (span * Clamp01(fraction));
	}

	private static double Clamp01(double value)
	{
		if (value < 0.0)
		{
			return 0.0;
		}

		if (value > 1.0)
		{
			return 1.0;
		}

		return value;
	}
}
