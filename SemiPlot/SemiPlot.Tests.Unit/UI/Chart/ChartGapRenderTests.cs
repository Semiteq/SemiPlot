using Avalonia.Headless.XUnit;

using AwesomeAssertions;


using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The break guard: the provider fold's NaN gap column must leave the rendered line broken, never bridged.
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
	// Half the band each column spans around its sample: a column whose Min equals its Max is a single point,
	// and a run of them would leave the probe nothing stroked to find.
	private const double ColumnSpread = 2.0;
	private const double AxisMin = 0.0;
	private const double AxisMax = 100.0;

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
	// EnvelopeLine strokes one pixel wide, so the line ending at the marker and the one starting at the
	// resumption each reach half a pixel into the break.
	private const double StrokeHalfWidthPixels = 0.5;


	[AvaloniaFact]
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
	// is built in BucketedRowFold's own output shape, a marker column, the gap column one tick after it and
	// a resumption column, rather than by calling the fold, which BucketedRowFoldTests pins separately.
	[AvaloniaFact]
	public void ArchiveShapedBreak_WithTheFoldsGapColumn_LeavesEveryBreakColumnWithoutPenColor()
	{
		var (timestamps, values) = ArchiveShapedBreak();
		var (plot, pixels, dataRect) = RenderSeries(
			timestamps, values, ArchiveTimeAt(0.0), ArchiveTimeAt(ArchiveWindowSeconds));
		var markerTime = MarkerTime();
		var resumptionTime = markerTime.AddSeconds(ArchiveBreakSeconds);
		// Pixel column k covers [k, k + 1); a break column is one neither edge's stroke reaches.
		var firstBreakColumn = (int)Math.Ceiling(PixelXAt(plot, markerTime) + StrokeHalfWidthPixels);
		var lastBreakColumn = (int)Math.Floor(PixelXAt(plot, resumptionTime) - StrokeHalfWidthPixels) - 1;
		var breakColumns = Enumerable
			.Range(firstBreakColumn, lastBreakColumn - firstBreakColumn + 1)
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

	[AvaloniaFact]
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
		var viewModel = ChartTestBuilder.CreateViewModel(ChartTestBuilder.RealtimeInterval).ViewModel;
		var state = viewModel.AddPen(new Pen(PenId, "Pen 1", ["Group A"], PenColorHex));
		state.LoadHistory(EnvelopeOf(timestamps, values), windowEnd, null);

		var plot = viewModel.Plot;
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(windowStart), LocalTimeAxis.ToAxis(windowEnd));
		plot.Axes.SetLimitsY(AxisMin, AxisMax, viewModel.ActivePenAxis!);

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return (plot, image.GetArrayRGB(), plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static PenHistoryEnvelope EnvelopeOf(IReadOnlyList<DateTime> timestamps, IReadOnlyList<double?> values)
	{
		var min = values.Select(value => value is { } sample ? sample - ColumnSpread : double.NaN).ToArray();
		var max = values.Select(value => value is { } sample ? sample + ColumnSpread : double.NaN).ToArray();
		var center = values.Select(value => value ?? double.NaN).ToArray();

		return new PenHistoryEnvelope(PenId, timestamps, min, max, center);
	}

	private static int ColumnAt(Plot plot, DateTime timestampUtc)
	{
		return (int)Math.Round(PixelXAt(plot, timestampUtc));
	}

	private static double PixelXAt(Plot plot, DateTime timestampUtc)
	{
		return plot.GetPixel(new Coordinates(LocalTimeAxis.ToAxis(timestampUtc), AxisMin)).X;
	}

	private static bool ColumnCarriesPenColor(byte[,,] pixels, PixelRect dataRect, int column)
	{
		return StrokeProbe.RowsIn(pixels, dataRect, column).Count > 0;
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

	// Rows arrive at a fixed poll interval with the broken span simply absent; the fold's gap column follows
	// the q = 32 row.
	private static (IReadOnlyList<DateTime> Timestamps, IReadOnlyList<double?> Values) ArchiveShapedBreak()
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
		timestamps.Add(timestamps[^1].AddTicks(1));
		values.Add(null);

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
		return ChartTestBuilder.From.AddSeconds(offsetSeconds);
	}

	private static double ValueAt(int index)
	{
		return 50.0 + (20.0 * Math.Sin(index / 7.0));
	}

	private static DateTime TimeAt(int index)
	{
		return ChartTestBuilder.From.AddSeconds(index);
	}
}
