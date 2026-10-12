using ScottPlot;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The one pixel probe: where a colour channel dominates, red by default.</summary>
internal static class StrokeProbe
{
	public const int Red = 0;
	public const int Blue = 2;

	// A channel this far above the other two is drawn data, not an antialiased edge of a grey grid line, tick
	// label or frame.
	private const int DominanceThreshold = 24;

	public static bool Dominates(byte[,,] pixels, int row, int column, int channel = Red)
	{
		var strongest = pixels[row, column, channel];
		var others = Math.Max(pixels[row, column, (channel + 1) % 3], pixels[row, column, (channel + 2) % 3]);

		return strongest - others >= DominanceThreshold;
	}

	public static IReadOnlyList<int> RowsIn(byte[,,] pixels, PixelRect dataRect, int column)
	{
		var rows = new List<int>();
		var firstRow = (int)Math.Ceiling(dataRect.Top);
		var lastRow = (int)Math.Floor(dataRect.Bottom);

		for (var row = firstRow; row <= lastRow; row++)
		{
			if (Dominates(pixels, row, column))
			{
				rows.Add(row);
			}
		}

		return rows;
	}
}
