using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The break guard: a NaN column from MinMaxDecimator must leave the rendered line broken, never bridged.
// Pixels come from Plot.GetImage -> Image.GetArrayRGB() (SkiaSharp, no Avalonia in the loop), so DataRect
// matches the same render; sampling is by band, never by byte, so antialiasing cannot flip the answer.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartGapRenderTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const int PenId = 1;
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

	// Archive-shaped input at the window-to-break ratio that matters: the vendor's shortest q = 32/q = 16
	// pair (3.5 s) over a 120 s window is a 34:1 ratio, ~20 of the ~690 data-area pixel columns. Over a
	// half-hour window the same break falls under one column, where antialiasing alone satisfies the probe.
	private const double ArchivePollSeconds = 0.1;
	private const double ArchiveBreakSeconds = 3.5;
	private const double ArchiveWindowSeconds = 120.0;
	private const int ArchivePollsBeforeBreak = 600;
	private const int ArchivePollsAfterBreak = 565;
	// Distance from the break's edges to the columns that must still carry the line, in seconds.
	private const double ArchiveProbeOffsetSeconds = 5.0;
	// Guards the ratio itself, so a later width or window change that shrinks the break towards a single
	// column fails here rather than quietly turning the blank-column assertion into a formality.
	private const int MinimumBreakColumns = 12;

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

	// The only pixel assertion in the gap-reconstruction slice; envelope-column tests cover the rest. Input
	// is built in HistoryRowFold's own output shape — marker row, null anchor, resumption row — rather than
	// by calling the fold, which HistoryRowFoldTests pins separately.
	[Fact]
	public void ArchiveShapedBreak_WithTheFoldsNullAnchor_LeavesEveryBreakColumnWithoutPenColor()
	{
		var (timestamps, values) = ArchiveShapedBreak(withAnchor: true);
		var (plot, pixels, dataRect) = RenderSeries(
			timestamps, values, ArchiveTimeAt(0.0), ArchiveTimeAt(ArchiveWindowSeconds));
		var markerTime = MarkerTime();
		var resumptionTime = markerTime.AddSeconds(ArchiveBreakSeconds);
		var markerColumn = ColumnAt(plot, markerTime);
		var resumptionColumn = ColumnAt(plot, resumptionTime);
		var breakColumns = Enumerable
			.Range(markerColumn + 1, resumptionColumn - markerColumn - 1)
			.ToList();
		var beforeBreak = ColumnAt(plot, markerTime.AddSeconds(-ArchiveProbeOffsetSeconds));
		var afterBreak = ColumnAt(plot, resumptionTime.AddSeconds(ArchiveProbeOffsetSeconds));

		breakColumns.Count.Should().BeGreaterThanOrEqualTo(
			MinimumBreakColumns,
			"the window-to-break ratio must leave the break several pixel columns wide, or a blank column "
			+ "proves only that antialiasing missed it");
		breakColumns
			.Where(column => ColumnCarriesPenColor(pixels, dataRect, column))
			.Should().BeEmpty(
				"the null the fold appends one tick after the q = 32 row must break the line across the "
				+ "whole recorded break, not merely at its centre");
		ColumnCarriesPenColor(pixels, dataRect, beforeBreak).Should().BeTrue(
			"the run of rows before the marker is drawn, so the break columns are not blank for want of "
			+ "any rendering");
		ColumnCarriesPenColor(pixels, dataRect, afterBreak).Should().BeTrue(
			"the line resumes at the q = 16 row instead of staying broken to the window's end");
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
		return RenderSeries(Timestamps(), values, TimeAt(0), TimeAt(SampleCount - 1));
	}

	private static (Plot Plot, byte[,,] Pixels, PixelRect DataRect) RenderSeries(
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values,
		DateTime windowStart,
		DateTime windowEnd)
	{
		var viewModel = CreateViewModel();
		var state = viewModel.AddPen(new Pen(PenId, "Pen 1", "Group A", PenColorHex));
		state.LoadHistory(MinMaxDecimator.Decimate(PenId, timestamps, values, TargetColumnCount));

		var plot = viewModel.Plot;
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(windowStart), LocalTimeAxis.ToAxis(windowEnd));
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

	// Rows arrive at a fixed poll interval with the broken span simply absent; withAnchor: false drops the
	// fold's null anchor after the q = 32 row, leaving one segment — the plausible-but-wrong rendering this
	// test exists to reject.
	private static (IReadOnlyList<DateTime> Timestamps, IReadOnlyList<double?> Values) ArchiveShapedBreak(
		bool withAnchor)
	{
		var capacity = ArchivePollsBeforeBreak + ArchivePollsAfterBreak + 1;
		var timestamps = new List<DateTime>(capacity);
		var values = new List<double?>(capacity);

		for (var index = 0; index < ArchivePollsBeforeBreak; index++)
		{
			timestamps.Add(ArchiveTimeAt(index * ArchivePollSeconds));
			values.Add(ValueAt(index));
		}

		// The q = 32 marker row is the last one appended above: its value is kept and the null goes after
		// it, one tick later, never in its place.
		if (withAnchor)
		{
			timestamps.Add(timestamps[^1].AddTicks(1));
			values.Add(null);
		}

		var resumptionSeconds = ((ArchivePollsBeforeBreak - 1) * ArchivePollSeconds) + ArchiveBreakSeconds;
		for (var index = 0; index < ArchivePollsAfterBreak; index++)
		{
			timestamps.Add(ArchiveTimeAt(resumptionSeconds + (index * ArchivePollSeconds)));
			values.Add(ValueAt(ArchivePollsBeforeBreak + index));
		}

		return (timestamps, values);
	}

	private static DateTime MarkerTime()
	{
		return ArchiveTimeAt((ArchivePollsBeforeBreak - 1) * ArchivePollSeconds);
	}

	private static DateTime ArchiveTimeAt(double offsetSeconds)
	{
		return _from.AddSeconds(offsetSeconds);
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
