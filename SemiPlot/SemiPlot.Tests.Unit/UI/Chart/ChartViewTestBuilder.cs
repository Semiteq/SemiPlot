using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using ScottPlot.Avalonia;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The realised chart the pointer input and the axis panel tests drive through Avalonia's pipeline.</summary>
internal static class ChartViewTestBuilder
{
	private const int WindowWidth = 900;
	private const int WindowHeight = 600;

	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33.0);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);

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

		window.Show();
		Dispatcher.UIThread.RunJobs();

		var plotControl = view.GetVisualDescendants().OfType<AvaPlot>().Single();
		// Both sides, because RenderInMemory takes both: a zero either way reaches ScottPlot as a throw
		// rather than as a stated failure.
		plotControl.Bounds.Width.Should().BeGreaterThan(0.0, "the shown window must lay the chart view out");
		plotControl.Bounds.Height.Should().BeGreaterThan(0.0, "the shown window must lay the chart view out");
		viewModel.Plot.RenderInMemory((int)plotControl.Bounds.Width, (int)plotControl.Bounds.Height);
		viewModel.Plot.RenderManager.LastRender.Layout.DataRect.HasArea.Should().BeTrue(
			"the view's pixel-to-time maths reads the last render's data area");

		return new ShownChart(window, plotControl);
	}

	internal static TrendChartViewModel CreateLoadedViewModel()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10.0));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		// AvaloniaScheduler, as in production, and every test here disposes the view model:
		// docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
		var viewModel = new TrendChartViewModel(
			coordinator,
			scheduler,
			AvaloniaScheduler.Instance,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);
		var state = viewModel.AddPen(new Pen(1, "Pen 1", ["Group A"], "#ff0000"));
		state.LoadHistory(
			new PenHistoryEnvelope(
				1,
				[_from, _from.AddMinutes(1.0)],
				[1.0, 3.0],
				[5.0, 9.0],
				[2.0, 6.0]),
			_from.AddMinutes(1.0));

		return viewModel;
	}

	/// <summary>The window a test showed; disposing it closes the window.</summary>
	internal sealed record ShownChart(Window Window, AvaPlot PlotControl) : IDisposable
	{
		public void Dispose()
		{
			Window.Close();
		}
	}
}
