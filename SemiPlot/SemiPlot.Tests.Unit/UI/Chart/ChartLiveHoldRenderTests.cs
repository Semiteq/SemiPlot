using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartLiveHoldRenderTests
{
	private const int PlotWidth = 800;
	private const int PlotHeight = 500;
	private const double AxisMin = 0.0;
	private const double AxisMax = 100.0;
	private const double SlowValue = 40.0;
	private const double SecondValue = 70.0;
	private const int WindowSeconds = 60;
	// A stroke one pixel wide lands on the projected row or, antialiased, on its neighbour.
	private const int RowTolerancePixels = 1;

	private static readonly Pen _slowPen = FakeDataProvider.FirstPen;
	private static readonly Pen _secondPen = FakeDataProvider.SecondPen;
	private static readonly DateTime _windowStart = ChartTestBuilder.From;
	private static readonly DateTime _windowEnd = _windowStart.AddSeconds(WindowSeconds);
	private static readonly DateTime _slowLastStamp = _windowStart.AddSeconds(WindowSeconds / 3.0);

	[AvaloniaFact]
	public void AConfirmedSlowPen_IsDrawnHeldToTheNewestSample()
	{
		var (viewModel, scheduler, _, provider) = ChartTestBuilder.CreateViewModel();
		using var chart = viewModel;
		var slow = chart.AddPen(_slowPen);
		chart.AddPen(_secondPen);
		chart.SetAxisLimits(_slowPen.PenId, AxisMin, AxisMax);
		chart.SetAxisLimits(_secondPen.PenId, AxisMin, AxisMax);
		slow.LoadHistory(
			new PenHistoryEnvelope(
				_slowPen.PenId,
				[_windowStart, _slowLastStamp],
				[SlowValue, SlowValue],
				[SlowValue, SlowValue],
				[SlowValue, SlowValue]),
			_windowEnd);

		provider.PushLiveSamples([new Sample(_slowPen.PenId, _slowLastStamp, SlowValue)]);
		provider.PushLiveSamples([new Sample(_secondPen.PenId, _windowEnd, SecondValue)]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks + 1);

		chart.Navigation.NewestSample.Should().Be(_windowEnd, "the second pen's sample is the newest the chart saw");
		var (column, row, rows) = ProbeHalfwayToTheEdge(chart, slow);
		rows.Should().Contain(
			found => Math.Abs(found - row) <= RowTolerancePixels,
			$"the slow pen is held at its last value across pixel column {column}");

		provider.PushLiveState(
			LiveSourceState.FaultedBy(new LiveSourceError(LiveSourceFault.Unreachable, "opc.tcp://127.0.0.1:4840")));

		ProbeHalfwayToTheEdge(chart, slow).Rows.Should().BeEmpty("no pen is held while the session is down");
	}

	private static (int Column, int Row, IReadOnlyList<int> Rows) ProbeHalfwayToTheEdge(
		TrendChartViewModel chart,
		TrendPenState slow)
	{
		var plot = chart.Plot;
		plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(_windowStart), LocalTimeAxis.ToAxis(_windowEnd));

		using var image = plot.GetImage(PlotWidth, PlotHeight);

		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;
		var halfway = (LocalTimeAxis.ToAxis(_slowLastStamp) + LocalTimeAxis.ToAxis(_windowEnd)) / 2.0;
		var pixel = slow.Line.PixelOf(halfway, SlowValue, dataRect);
		var column = (int)Math.Round(pixel.X);

		return (column, (int)Math.Round(pixel.Y), StrokeProbe.RowsIn(image.GetArrayRGB(), dataRect, column));
	}
}
