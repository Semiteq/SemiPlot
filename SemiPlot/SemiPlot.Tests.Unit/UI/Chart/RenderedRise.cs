using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>One pen drawn as a rise from 20 to 80 on a 0 to 100 axis, and its line read back as pixels.</summary>
internal static class RenderedRise
{
	private const int PlotWidth = 400;
	private const int PlotHeight = 300;
	private const double AxisMin = 0.0;
	private const double AxisMax = 100.0;
	private const double Low = 20.0;
	private const double High = 80.0;
	private const double Middle = 50.0;

	// Rows either side of the probed height, so a one-pixel antialiased stroke cannot slip between probes.
	private const int ProbeRows = 3;

	private static readonly DateTime _start = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _end = _start.AddMinutes(10.0);

	/// <summary>
	/// Whether the colour channel <paramref name="channel"/> dominates halfway along the rise at half height, where
	/// an interpolated line runs, and at the low value, where a stepped line holds before it jumps.
	/// </summary>
	public static (bool OnDiagonal, bool OnHeldStep) Draw(TrendChartViewModel chart, TrendPenState pen, int channel)
	{
		pen.LoadHistory(
			new PenHistoryEnvelope(pen.Pen.PenId, [_start, _end], [Low, High], [Low, High], [Low, High]),
			_end);

		var plot = chart.Plot;
		var yAxis = pen.Line.Axes.YAxis;
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(_start), LocalTimeAxis.ToAxis(_end));
		plot.Axes.SetLimitsY(AxisMin, AxisMax, yAxis);

		using var image = plot.GetImage(PlotWidth, PlotHeight);
		var pixels = image.GetArrayRGB();
		var halfway = LocalTimeAxis.ToAxis(_start + ((_end - _start) / 2));

		return (
			Dominates(pixels, plot.GetPixel(new Coordinates(halfway, Middle), plot.Axes.Bottom, yAxis), channel),
			Dominates(pixels, plot.GetPixel(new Coordinates(halfway, Low), plot.Axes.Bottom, yAxis), channel));
	}

	private static bool Dominates(byte[,,] pixels, Pixel probe, int channel)
	{
		var column = (int)Math.Round(probe.X);
		var centreRow = (int)Math.Round(probe.Y);

		for (var row = centreRow - ProbeRows; row <= centreRow + ProbeRows; row++)
		{
			if (StrokeProbe.Dominates(pixels, row, column, channel))
			{
				return true;
			}
		}

		return false;
	}
}
