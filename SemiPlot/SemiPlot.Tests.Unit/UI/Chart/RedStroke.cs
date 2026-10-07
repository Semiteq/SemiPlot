using ScottPlot;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The rows of one pixel column inside the data area where a #ff0000 stroke is drawn.</summary>
internal static class RedStroke
{
	// A red dominance this wide is drawn data, not an antialiased edge of a grey grid line, tick label or frame.
	private const int DominanceThreshold = 24;

	public static IReadOnlyList<int> RowsIn(byte[,,] pixels, PixelRect dataRect, int column)
	{
		var rows = new List<int>();
		var firstRow = (int)Math.Ceiling(dataRect.Top);
		var lastRow = (int)Math.Floor(dataRect.Bottom);

		for (var row = firstRow; row <= lastRow; row++)
		{
			var red = pixels[row, column, 0];
			var green = pixels[row, column, 1];
			var blue = pixels[row, column, 2];
			if (red - Math.Max(green, blue) >= DominanceThreshold)
			{
				rows.Add(row);
			}
		}

		return rows;
	}
}
