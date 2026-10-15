using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// Pens at one value trace the same pixels, so the order shows in the colour of the rows the line crosses;
// pens at two values do not overlap, so the width shows in the ink.
// docs/architecture/charting.md#per-pen-plottable-envelopeline
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartActivePenRenderTests
{
	private const int ProbeSeconds = 30;
	private const double LineValue = 50.0;
	private const int ActiveLineRows = 2;
	private const int SecondPenId = 2;
	private const double LowerLineValue = 30.0;
	private const double UpperLineValue = 70.0;

	[AvaloniaTheory]
	[InlineData(1, StrokeProbe.Red, StrokeProbe.Blue)]
	[InlineData(SecondPenId, StrokeProbe.Blue, StrokeProbe.Red)]
	public void TheActivePen_DrawsAboutTwiceTheInkOfTheOtherPen(int activePenId, int activeChannel, int otherChannel)
	{
		var (_, pixels, column, dataRect) = RenderWithActivePen(activePenId, LowerLineValue, UpperLineValue);

		StrokeProbe.InkOf(pixels, dataRect, column, activeChannel).Should().BeGreaterThan(
			StrokeProbe.InkOf(pixels, dataRect, column, otherChannel) * StrokeProbe.ThickInkRatio);
	}

	[AvaloniaFact]
	public void TheActivePen_DrawsTwoRowsAndWinsTheSharedPixels()
	{
		var (viewModel, pixels, column, dataRect) = RenderWithActivePen(FakeDataProvider.FirstPen.PenId);

		RowsOf(pixels, dataRect, column, StrokeProbe.Red).Should().HaveCountGreaterThanOrEqualTo(ActiveLineRows);
		RowsOf(pixels, dataRect, column, StrokeProbe.Blue).Should().BeEmpty();
		viewModel.Plot.PlottableList.Last().Should().BeSameAs(viewModel.FindPen(FakeDataProvider.FirstPen.PenId)!.Line);
	}

	[AvaloniaFact]
	public void SwitchingTheActivePen_SwapsTheWidthAndTheOrder()
	{
		var (_, pixels, column, dataRect) = RenderWithActivePen(SecondPenId);

		RowsOf(pixels, dataRect, column, StrokeProbe.Blue).Should().HaveCountGreaterThanOrEqualTo(ActiveLineRows);
		RowsOf(pixels, dataRect, column, StrokeProbe.Red).Should().BeEmpty();
	}

	private static (TrendChartViewModel ViewModel, byte[,,] Pixels, int Column, PixelRect DataRect)
		RenderWithActivePen(int activePenId, double firstValue = LineValue, double secondValue = LineValue)
	{
		var (viewModel, pixels, dataRect) = ChartTestBuilder.RenderTwoFlatPens(
			(new Pen(1, "Pen 1", ["Group A"], "#ff0000"), firstValue),
			(new Pen(SecondPenId, "Pen 2", ["Group A"], "#0000ff"), secondValue),
			ProbeSeconds * 2,
			activePenId);
		var probeX = LocalTimeAxis.ToAxis(ChartTestBuilder.From.AddSeconds(ProbeSeconds));
		var column = (int)Math.Round(
			viewModel.Plot.GetPixel(new Coordinates(probeX, ChartTestBuilder.FlatPlotAxisMin)).X);

		return (viewModel, pixels, column, dataRect);
	}

	private static List<int> RowsOf(byte[,,] pixels, PixelRect dataRect, int column, int channel)
	{
		var rows = new List<int>();
		for (var row = (int)Math.Ceiling(dataRect.Top); row <= (int)Math.Floor(dataRect.Bottom); row++)
		{
			if (StrokeProbe.Dominates(pixels, row, column, channel))
			{
				rows.Add(row);
			}
		}

		return rows;
	}
}
