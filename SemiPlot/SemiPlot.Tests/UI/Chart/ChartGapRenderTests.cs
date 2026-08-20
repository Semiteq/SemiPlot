using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

// The break guard: a NaN column from MinMaxDecimator must leave the rendered line broken, never bridged
// by a straight segment across the missing hours.
//
// Pixels come out of ScottPlot 5.1.57 as Plot.GetImage(width, height) -> Image.GetArrayRGB(), a
// byte[row, column, channel] with channel 0 red, 1 green, 2 blue. GetImage rasterises through SkiaSharp
// with no Avalonia in the loop and populates RenderManager.LastRender, so DataRect comes from the same
// render the pixels do (RenderInMemory is GetImage with the image dropped).
//
// Sampling is by band, never by byte: a column of pixels either carries pen-coloured ones or it does not.
// Antialiasing, font metrics and theme changes move bytes without moving that answer, which is what lets
// the assertion survive a ScottPlot version bump.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartGapRenderTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const long PenId = 1L;
	private const string PenColorHex = "#ff0000";
	private const int SampleCount = 600;
	private const int GapFirstNullIndex = 250;
	private const int GapLastNullIndex = 349;
	// Distance from the gap's edges to the columns that must still carry the line, in samples.
	private const int ProbeOffsetSamples = 100;
	private const int TargetColumnCount = 200;
	private const double AxisMin = 0.0;
	private const double AxisMax = 100.0;
	// The pen draws #ff0000 and its band the same hue at low opacity over a white background, so a red
	// dominance this wide is drawn data and not an antialiased edge of a grey grid line or tick label.
	private const int RedDominanceThreshold = 24;
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void NaNGapColumn_LeavesTheGapCentreWithoutPenColor_WhileBothSidesStillCarryTheLine()
	{
		var (plot, pixels, dataRect) = RenderSeries(SeriesWithGap());
		var gapCentre = ColumnAt(plot, TimeAt((GapFirstNullIndex + GapLastNullIndex) / 2));
		var beforeGap = ColumnAt(plot, TimeAt(GapFirstNullIndex - ProbeOffsetSamples));
		var afterGap = ColumnAt(plot, TimeAt(GapLastNullIndex + ProbeOffsetSamples));

		ColumnCarriesPenColor(pixels, dataRect, gapCentre).Should().BeFalse(
			"a NaN column must break the curve, leaving background where the line would cross");
		ColumnCarriesPenColor(pixels, dataRect, beforeGap).Should().BeTrue(
			"the segment before the gap is drawn, so the gap column is not empty for want of any rendering");
		ColumnCarriesPenColor(pixels, dataRect, afterGap).Should().BeTrue(
			"the segment after the gap is drawn, so the gap column is not empty for want of any rendering");
	}

	[Fact]
	public void ContinuousSeries_LeavesNoColumnOfTheDataAreaWithoutPenColor()
	{
		var (_, pixels, dataRect) = RenderSeries(ContinuousSeries());
		var firstColumn = (int)Math.Ceiling(dataRect.Left) + 2;
		var lastColumn = (int)Math.Floor(dataRect.Right) - 2;
		var columnsWithoutPenColor = Enumerable
			.Range(firstColumn, lastColumn - firstColumn + 1)
			.Where(column => !ColumnCarriesPenColor(pixels, dataRect, column))
			.ToList();

		columnsWithoutPenColor.Should().BeEmpty(
			"an envelope with no NaN column draws an unbroken curve across the whole data area");
	}

	private static (Plot Plot, byte[,,] Pixels, PixelRect DataRect) RenderSeries(IReadOnlyList<double?> values)
	{
		var viewModel = CreateViewModel();
		var state = viewModel.AddPen(new Pen(PenId, "Pen 1", "Group A", PenColorHex));
		state.LoadHistory(MinMaxDecimator.Decimate(PenId, Timestamps(), values, TargetColumnCount));

		var plot = viewModel.Plot;
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(TimeAt(0)), LocalTimeAxis.ToAxis(TimeAt(SampleCount - 1)));
		plot.Axes.SetLimitsY(AxisMin, AxisMax, viewModel.ActivePenAxis!);

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return (plot, image.GetArrayRGB(), plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static TrendChartViewModel CreateViewModel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);

		return new TrendChartViewModel(
			coordinator, scheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);
	}

	private static int ColumnAt(Plot plot, DateTime timestampUtc)
	{
		var pixel = plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(timestampUtc), AxisMin));

		return (int)Math.Round(pixel.X);
	}

	private static bool ColumnCarriesPenColor(byte[,,] pixels, PixelRect dataRect, int column)
	{
		var firstRow = (int)Math.Ceiling(dataRect.Top) + 1;
		var lastRow = (int)Math.Floor(dataRect.Bottom) - 1;

		for (var row = firstRow; row <= lastRow; row++)
		{
			var red = pixels[row, column, 0];
			var green = pixels[row, column, 1];
			var blue = pixels[row, column, 2];
			if (red - Math.Max(green, blue) >= RedDominanceThreshold)
			{
				return true;
			}
		}

		return false;
	}

	private static IReadOnlyList<DateTime> Timestamps()
	{
		var timestamps = new List<DateTime>(SampleCount);
		for (var index = 0; index < SampleCount; index++)
		{
			timestamps.Add(TimeAt(index));
		}

		return timestamps;
	}

	private static IReadOnlyList<double?> ContinuousSeries()
	{
		var values = new List<double?>(SampleCount);
		for (var index = 0; index < SampleCount; index++)
		{
			values.Add(ValueAt(index));
		}

		return values;
	}

	private static IReadOnlyList<double?> SeriesWithGap()
	{
		var values = new List<double?>(ContinuousSeries());
		for (var index = GapFirstNullIndex; index <= GapLastNullIndex; index++)
		{
			values[index] = null;
		}

		return values;
	}

	// A varying signal so every decimated column keeps a Min/Max spread: a degenerate band is hidden by
	// TrendPenState, which would take FillY's own NaN handling out of the test.
	private static double ValueAt(int index)
	{
		return 50.0 + (20.0 * Math.Sin(index / 7.0));
	}

	private static DateTime TimeAt(int index)
	{
		return _from.AddSeconds(index);
	}
}
