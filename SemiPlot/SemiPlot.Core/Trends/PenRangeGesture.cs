namespace SemiPlot.Core.Trends;

/// <summary>Shifts and scales a pen's value range in axis space; a log10 axis works in decades.</summary>
public static class PenRangeGesture
{
	/// <summary>A positive fraction of the span lowers both limits, so the line rises; null keeps the range.</summary>
	public static (double Min, double Max)? Shift(double min, double max, bool isLogarithmic, double fraction)
	{
		var (bottom, top) = ToAxis(min, max, isLogarithmic);
		var offset = fraction * (top - bottom);

		return FromAxis(bottom - offset, top - offset, isLogarithmic);
	}

	/// <summary>The value at the anchor fraction (0 bottom, 1 top) stays fixed; null keeps the range.</summary>
	public static (double Min, double Max)? Scale(
		double min,
		double max,
		bool isLogarithmic,
		double factor,
		double anchorFraction)
	{
		var (bottom, top) = ToAxis(min, max, isLogarithmic);
		var anchor = bottom + (anchorFraction * (top - bottom));

		return FromAxis(anchor - ((anchor - bottom) * factor), anchor + ((top - anchor) * factor), isLogarithmic);
	}

	private static (double Bottom, double Top) ToAxis(double min, double max, bool isLogarithmic)
	{
		return isLogarithmic ? (Math.Log10(min), Math.Log10(max)) : (min, max);
	}

	private static (double Min, double Max)? FromAxis(double bottom, double top, bool isLogarithmic)
	{
		var (min, max) = isLogarithmic ? (Math.Pow(10.0, bottom), Math.Pow(10.0, top)) : (bottom, top);

		if (!double.IsFinite(min) || !double.IsFinite(max) || !(max > min)
			|| (isLogarithmic && !LogAxis.AdmitsMinimum(min)))
		{
			return null;
		}

		return (min, max);
	}
}
