namespace SemiPlot.UI.Chart;

// Pure seeding for an axis-region range edit: the operator types one bound (MAX from the upper region,
// MIN from the lower region) and the untouched bound is carried over from the axis's currently computed
// range rather than a 0..1 default, so editing one end never silently collapses the other. The result
// is ordered so the returned minimum never exceeds the maximum.
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
