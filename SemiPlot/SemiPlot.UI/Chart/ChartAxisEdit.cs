namespace SemiPlot.UI.Chart;

public static class ChartAxisEdit
{
	public static (double Min, double Max) SeedManualLimits(
		double typedBound,
		bool editsMax,
		(double Min, double Max) currentRange)
	{
		var min = editsMax ? currentRange.Min : typedBound;
		var max = editsMax ? typedBound : currentRange.Max;

		return (Math.Min(min, max), Math.Max(min, max));
	}
}
