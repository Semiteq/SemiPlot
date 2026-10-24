using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAheadBreakShadeTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const double AxisXMin = 0.0;
	private const double AxisXMax = 100.0;
	private const double AxisYMin = 0.0;
	private const double AxisYMax = 10.0;
	private const double Value = 5.0;
	private const double ValueBeforeX = 20.0;
	private const double WideSpanEndX = 80.0;
	private const double NarrowSpanEndX = 25.0;
	private const double TrailingValueX = 95.0;
	private const double EmptyLineValueX = 60.0;
	private const double WindowInsideTheSpanMin = 40.0;
	private const double NarrowVisiblePartMin = 75.0;
	// Glyph side bearings shift the drawn label off its measured centre by a pixel or two.
	private const float LabelCentreTolerancePixels = 4f;
	private const int TestTimeoutMilliseconds = 120_000;
	// The pen line's antialiasing at the span's edges.
	private const int EdgeTolerancePixels = 1;
	// The label sits 4 px above the data area's bottom at 12 px; this band holds it whole.
	private const int LabelBandPixels = 24;

	// Yields between rewrites so the render task reaches its frame budget inside the run budget.
	private static readonly TimeSpan _mutationPause = TimeSpan.FromMilliseconds(1);
	private static readonly Color _fill = new(0, 0, 255, 0x40);
	private static readonly Color _labelColor = new(255, 0, 0);

	[AvaloniaFact]
	public void TheBand_FillsTheAheadBreakSpanAndNothingOutsideIt()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);

		var (shaded, unshaded, dataRect) = RenderBoth(plot, line, shade);
		var (left, right) = SpanPixels(plot, dataRect, WideSpanEndX);
		var changedColumns = ChangedColumns(shaded, unshaded, dataRect);
		var middleRow = (int)(dataRect.Top + (dataRect.Height / 4f));
		var middleColumn = (int)((left + right) / 2f);

		changedColumns.Should().NotBeEmpty();
		changedColumns.Should().OnlyContain(
			column => column >= left - EdgeTolerancePixels && column <= right + EdgeTolerancePixels,
			"the band covers the span from the last value before the ahead-break to the first value after it");
		Differs(shaded, unshaded, middleRow, middleColumn).Should().BeTrue("the band fills the span's height");
		Differs(shaded, unshaded, middleRow, (int)left - 20).Should().BeFalse();
		Differs(shaded, unshaded, middleRow, (int)right + 20).Should().BeFalse();
	}

	[AvaloniaFact]
	public void AWideSpan_CarriesItsLabelAlongTheBottomEdge()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);

		var (shaded, _, dataRect) = RenderBoth(plot, line, shade);
		var (left, right) = SpanPixels(plot, dataRect, WideSpanEndX);
		var labelColumns = LabelColumns(shaded, dataRect);

		labelColumns.Should().NotBeEmpty("the span is wider than the label");
		labelColumns.Should().OnlyContain(column => column > left && column < right);
	}

	[AvaloniaFact]
	public void ANarrowSpan_IsShadedWithoutALabel()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, NarrowSpanEndX, BreakKind.Ahead);

		var (shaded, unshaded, dataRect) = RenderBoth(plot, line, shade);

		ChangedColumns(shaded, unshaded, dataRect).Should().NotBeEmpty("the narrow span is still shaded");
		LabelColumns(shaded, dataRect).Should().BeEmpty("a label wider than its band would cover archived rows");
	}

	[AvaloniaFact]
	public void AnOrdinaryBreak_IsNotShaded()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ordinary);

		var (shaded, unshaded, dataRect) = RenderBoth(plot, line, shade);

		ChangedColumns(shaded, unshaded, dataRect).Should().BeEmpty();
	}

	[AvaloniaFact]
	public void AnEmptyLinesSpan_OpensAtTheDataAreasLeftEdge()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		line.AppendAfterBreak(
			new EnvelopeColumn(EmptyLineValueX - 1.0, double.NaN, double.NaN, double.NaN, BreakKind.Ahead),
			new EnvelopeColumn(EmptyLineValueX, Value, Value, Value));
		line.AppendColumn(new EnvelopeColumn(TrailingValueX, Value, Value, Value));

		var (shaded, unshaded, dataRect) = RenderBoth(plot, line, shade);
		var right = plot.Axes.Bottom.GetPixel(EmptyLineValueX, dataRect);
		var changedColumns = ChangedColumns(shaded, unshaded, dataRect);

		changedColumns.Should().Contain(
			column => column <= dataRect.Left + EdgeTolerancePixels,
			"no value column precedes the ahead-break, so the span opens at the data area's left edge");
		changedColumns.Should().OnlyContain(
			column => column >= dataRect.Left - EdgeTolerancePixels && column <= right + EdgeTolerancePixels);
	}

	[AvaloniaFact]
	public void AWindowStartingInsideASpan_CentresTheLabelInTheVisiblePart()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);
		plot.Axes.SetLimitsX(WindowInsideTheSpanMin, AxisXMax);

		var (shaded, _, dataRect) = RenderBoth(plot, line, shade);
		var right = plot.Axes.Bottom.GetPixel(WideSpanEndX, dataRect);
		var labelColumns = LabelColumns(shaded, dataRect);

		labelColumns.Should().NotBeEmpty("the visible part of the span is wider than the label");
		var labelCentre = (labelColumns.Min() + labelColumns.Max()) / 2f;
		labelCentre.Should().BeApproximately((dataRect.Left + right) / 2f, LabelCentreTolerancePixels);
	}

	[AvaloniaFact]
	public void AWideSpanWhoseVisiblePartIsNarrow_IsShadedWithoutALabel()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);
		plot.Axes.SetLimitsX(NarrowVisiblePartMin, AxisXMax);

		var (shaded, unshaded, dataRect) = RenderBoth(plot, line, shade);

		ChangedColumns(shaded, unshaded, dataRect).Should().NotBeEmpty("the visible part is still shaded");
		LabelColumns(shaded, dataRect).Should().BeEmpty("the label fits the visible part of the span, not the span");
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task Render_WhileTheUiThreadRewritesTheLineAndSwitchesTheShade_DoesNotThrow()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);
		lock (plot.Sync)
		{
			plot.Add.Plottable(shade);
			plot.MoveToBottom(shade);
		}

		await RenderWhileMutating.RunAsync(
			() =>
			{
				using var image = plot.GetImage(PlotWidth, PlotHeight);
			},
			() =>
			{
				shade.ShowFor(line);
				line.ReplaceColumns([], AxisXMin);
				shade.ShowFor(null);
				WriteSpan(line, WideSpanEndX, BreakKind.Ahead);
				shade.ShowFor(line);

				Thread.Sleep(_mutationPause);
			},
			"the shade's lock and then the line's must hold against the UI thread's rewrites");
	}

	[AvaloniaFact]
	public void NoDrawnLine_ShadesNothing()
	{
		using var stand = NewStand();
		var (plot, line, shade) = stand;
		WriteSpan(line, WideSpanEndX, BreakKind.Ahead);
		var (withoutShade, dataRect) = Render(plot);

		shade.ShowFor(null);
		lock (plot.Sync)
		{
			plot.Add.Plottable(shade);
			plot.MoveToBottom(shade);
		}

		var (withShadeShowingNothing, _) = Render(plot);

		ChangedColumns(withShadeShowingNothing, withoutShade, dataRect).Should().BeEmpty();
	}

	private static ShadeStand NewStand()
	{
		var plot = new Plot();
		plot.HideGrid();
		plot.DataBackground.Color = Colors.White;

		var line = new EnvelopeLine();
		line.Axes.XAxis = plot.Axes.Bottom;
		line.Axes.YAxis = plot.Axes.Left;
		plot.Add.Plottable(line);

		var shade = new ChartAheadBreakShade();
		shade.Axes.XAxis = plot.Axes.Bottom;
		shade.ApplyPalette(new AheadBreakPalette(_fill, _labelColor));

		plot.Axes.SetLimitsX(AxisXMin, AxisXMax);
		plot.Axes.SetLimitsY(AxisYMin, AxisYMax);

		return new ShadeStand(plot, line, shade);
	}

	private static void WriteSpan(EnvelopeLine line, double spanEndX, BreakKind kind)
	{
		line.ReplaceColumns(
			[new EnvelopeColumn(10.0, Value, Value, Value), new EnvelopeColumn(ValueBeforeX, Value, Value, Value)],
			double.PositiveInfinity);
		line.AppendAfterBreak(
			new EnvelopeColumn(ValueBeforeX + 1.0, double.NaN, double.NaN, double.NaN, kind),
			new EnvelopeColumn(spanEndX, Value, Value, Value));
		line.AppendColumn(new EnvelopeColumn(TrailingValueX, Value, Value, Value));
	}

	private static (byte[,,] Shaded, byte[,,] Unshaded, PixelRect DataRect) RenderBoth(
		Plot plot,
		EnvelopeLine line,
		ChartAheadBreakShade shade)
	{
		lock (plot.Sync)
		{
			plot.Add.Plottable(shade);
			plot.MoveToBottom(shade);
		}

		shade.ShowFor(null);
		var (unshaded, dataRect) = Render(plot);
		shade.ShowFor(line);
		var (shaded, _) = Render(plot);

		return (shaded, unshaded, dataRect);
	}

	private static (byte[,,] Pixels, PixelRect DataRect) Render(Plot plot)
	{
		using var image = plot.GetImage(PlotWidth, PlotHeight);

		return (image.GetArrayRGB(), plot.RenderManager.LastRender.Layout.DataRect);
	}

	private static (float Left, float Right) SpanPixels(Plot plot, PixelRect dataRect, double spanEndX)
	{
		return (plot.Axes.Bottom.GetPixel(ValueBeforeX, dataRect), plot.Axes.Bottom.GetPixel(spanEndX, dataRect));
	}

	private static List<int> ChangedColumns(byte[,,] shaded, byte[,,] unshaded, PixelRect dataRect)
	{
		var columns = new List<int>();
		for (var column = 0; column < shaded.GetLength(1); column++)
		{
			for (var row = (int)Math.Ceiling(dataRect.Top); row <= (int)Math.Floor(dataRect.Bottom); row++)
			{
				if (Differs(shaded, unshaded, row, column))
				{
					columns.Add(column);

					break;
				}
			}
		}

		return columns;
	}

	private static bool Differs(byte[,,] first, byte[,,] second, int row, int column)
	{
		return first[row, column, StrokeProbe.Red] != second[row, column, StrokeProbe.Red]
			|| first[row, column, StrokeProbe.Green] != second[row, column, StrokeProbe.Green]
			|| first[row, column, StrokeProbe.Blue] != second[row, column, StrokeProbe.Blue];
	}

	private static List<int> LabelColumns(byte[,,] pixels, PixelRect dataRect)
	{
		var columns = new List<int>();
		var lastRow = (int)Math.Floor(dataRect.Bottom);
		for (var column = 0; column < pixels.GetLength(1); column++)
		{
			for (var row = lastRow - LabelBandPixels; row <= lastRow; row++)
			{
				if (StrokeProbe.Dominates(pixels, row, column))
				{
					columns.Add(column);

					break;
				}
			}
		}

		return columns;
	}

	private sealed record ShadeStand(Plot Plot, EnvelopeLine Line, ChartAheadBreakShade Shade) : IDisposable
	{
		public void Dispose()
		{
			Shade.Dispose();
			Plot.Dispose();
		}
	}
}
