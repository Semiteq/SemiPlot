using ScottPlot;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The one pixel probe: where a colour channel dominates, red by default, and how much ink it lays.</summary>
internal static class StrokeProbe
{
	public const int Red = 0;
	public const int Green = 1;
	public const int Blue = 2;

	// One line's ink over another's: a 1 px pair stays below the thin bound, a 2 px line over a 1 px one above
	// the thick bound.
	public const double ThinInkRatio = 1.25;
	public const double ThickInkRatio = 1.5;

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

	// A channel's lead over the other two sums to 255 per pixel of line width, whatever the antialiasing does.
	public static double InkOf(byte[,,] pixels, PixelRect dataRect, int column, int channel)
	{
		var ink = 0.0;
		for (var row = (int)Math.Ceiling(dataRect.Top); row <= (int)Math.Floor(dataRect.Bottom); row++)
		{
			var others = Math.Max(pixels[row, column, (channel + 1) % 3], pixels[row, column, (channel + 2) % 3]);
			ink += Math.Max(0, pixels[row, column, channel] - others);
		}

		return ink;
	}
}
