namespace SemiPlot.UI.Chart;

// Maps the chart data-area pixel width to the number of decimation columns to request, targeting roughly
// one column per horizontal pixel to remove sub-pixel oversampling.
public static class HistoryColumnTarget
{
	public const int MinColumns = 256;
	public const int MaxColumns = 2048;

	// Before the first render the data-area width is zero (no DataRect yet); the maximum is requested so
	// the initial query is not starved of resolution.
	public static int FromDataAreaWidth(double dataAreaWidthPixels)
	{
		if (!(dataAreaWidthPixels > 0.0))
		{
			return MaxColumns;
		}

		var rounded = (int)Math.Round(dataAreaWidthPixels);

		return Math.Clamp(rounded, MinColumns, MaxColumns);
	}
}
