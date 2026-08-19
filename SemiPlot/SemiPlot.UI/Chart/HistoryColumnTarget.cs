namespace SemiPlot.UI.Chart;

// Maps the chart data-area pixel width to the number of decimation columns to request, targeting roughly
// one column per horizontal pixel to remove sub-pixel oversampling.
public static class HistoryColumnTarget
{
	public const int MinColumns = 256;
	public const int MaxColumns = 2048;

	// A width without a canvas behind it (before the first render, a collapsed pane, a hidden tab) is not a
	// column count and is filtered by the caller, which keeps the last known width instead.
	public static int FromDataAreaWidth(double dataAreaWidthPixels)
	{
		if (!(dataAreaWidthPixels > 0.0))
		{
			throw new ArgumentOutOfRangeException(
				nameof(dataAreaWidthPixels), dataAreaWidthPixels, "Data area width must be positive.");
		}

		var rounded = (int)Math.Round(dataAreaWidthPixels);

		return Math.Clamp(rounded, MinColumns, MaxColumns);
	}
}
