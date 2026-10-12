using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Threading;

using AwesomeAssertions;

using ReactiveUI.Avalonia;

using ScottPlot.Avalonia;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The realised chart the pointer input and the axis panel tests drive through Avalonia's pipeline.</summary>
internal static class ChartViewTestBuilder
{
	private const int WindowWidth = 900;
	private const int WindowHeight = 600;

	/// <summary>The pixel of the active pen's axis region nearest the plot's left edge.</summary>
	internal static Point AxisRegionPoint(TrendChartViewModel viewModel)
	{
		var region = ChartAxisRegion.TryCreate(viewModel.Plot, viewModel.ActivePenAxis!);
		region.Should().NotBeNull();
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var y = (dataRect.Top + dataRect.Bottom) / 2f;

		for (var x = 1f; x < dataRect.Left; x += 1f)
		{
			if (region!.Contains(x, y))
			{
				return new Point(x, y);
			}
		}

		throw new InvalidOperationException("No pixel left of the data area lies in the axis region.");
	}

	internal static ShownChart ShowChart(TrendChartViewModel viewModel)
	{
		var view = new TrendChartView
		{
			DataContext = viewModel
		};
		var window = new Window
		{
			Width = WindowWidth,
			Height = WindowHeight,
			Content = view
		};

		ViewProbe.Realise(window);

		var plotControl = ViewProbe.Named<AvaPlot>(view, "PlotControl");
		// Both sides, because RenderInMemory takes both: a zero either way reaches ScottPlot as a throw
		// rather than as a stated failure.
		plotControl.Bounds.Width.Should().BeGreaterThan(0.0, "the shown window must lay the chart view out");
		plotControl.Bounds.Height.Should().BeGreaterThan(0.0, "the shown window must lay the chart view out");
		viewModel.Plot.RenderInMemory((int)plotControl.Bounds.Width, (int)plotControl.Bounds.Height);
		viewModel.Plot.RenderManager.LastRender.Layout.DataRect.HasArea.Should().BeTrue(
			"the view's pixel-to-time maths reads the last render's data area");

		return new ShownChart(window, plotControl)
		{
			View = view
		};
	}

	/// <summary>One render pulse: docs/architecture/testing-strategy.md#frames-in-a-realised-view.</summary>
	internal static void DriveOneFrame()
	{
		Dispatcher.UIThread.RunJobs();
		AvaloniaHeadlessPlatform.ForceRenderTimerTick();
		Dispatcher.UIThread.RunJobs();
	}

	internal static TrendChartViewModel CreateLoadedViewModel()
	{
		// AvaloniaScheduler, as in production: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
		var viewModel = ChartTestBuilder.CreateViewModel(
			ChartTestBuilder.RealtimeInterval,
			uiScheduler: AvaloniaScheduler.Instance).ViewModel;
		var state = viewModel.AddPen(FakeDataProvider.FirstPen);
		state.LoadHistory(
			new PenHistoryEnvelope(
				1,
				[ChartTestBuilder.From, ChartTestBuilder.From.AddMinutes(1.0)],
				[1.0, 3.0],
				[5.0, 9.0],
				[2.0, 6.0]),
			ChartTestBuilder.From.AddMinutes(1.0));

		return viewModel;
	}

	/// <summary>The window a test showed; disposing it closes the window.</summary>
	internal sealed record ShownChart(Window Window, AvaPlot PlotControl) : IDisposable
	{
		public required TrendChartView View { get; init; }

		public void Dispose()
		{
			Window.Close();
		}
	}
}
