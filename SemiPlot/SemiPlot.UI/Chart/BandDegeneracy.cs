namespace SemiPlot.UI.Chart;

// A band whose every column has Top == Bottom draws nothing but still costs a full polygon path build
// each frame, so the renderer can skip it until a real spread appears.
public static class BandDegeneracy
{
	// NaN columns are gaps, not spread; a band of only gaps is treated as degenerate.
	public static bool IsDegenerate(IReadOnlyList<(double X, double Top, double Bottom)> bandPoints)
	{
		foreach (var (_, top, bottom) in bandPoints)
		{
			if (double.IsNaN(top) || double.IsNaN(bottom))
			{
				continue;
			}

			if (top != bottom)
			{
				return false;
			}
		}

		return true;
	}
}
