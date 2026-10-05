namespace SemiPlot.Core.Trends;

internal static class ValueRange
{
	public static bool Widen(double value, bool isLogarithmic, ref double min, ref double max)
	{
		if (double.IsNaN(value) || (isLogarithmic && value <= 0.0))
		{
			return false;
		}

		min = Math.Min(min, value);
		max = Math.Max(max, value);

		return true;
	}
}
