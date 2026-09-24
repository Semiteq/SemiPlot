using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The window's view models as every test in this folder builds them, plus the layer drive.</summary>
internal static class MainWindowTestBuilder
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	public static MainWindowViewModel NewViewModel()
	{
		var panel = new MessagePanelViewModel();

		return new MainWindowViewModel(
			panel,
			NewStatusBar(panel),
			NullLogger<MainWindowViewModel>.Instance);
	}

	public static AppStatusBarViewModel NewStatusBar(MessagePanelViewModel panel)
	{
		return new AppStatusBarViewModel(panel, NullLogger<AppStatusBarViewModel>.Instance);
	}

	// The narrowest column target puts every layer inside the model's 365-day width ceiling; at the widest
	// one the hour ceiling alone is 512 days and the Day layer is unreachable.
	public static ChartNavigationController NavigationAtRawLayer()
	{
		var navigation = new ChartNavigationController();
		navigation.SetTargetColumnCount(HistoryColumnTarget.MinColumns);
		navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);

		return navigation;
	}

	// Both schedulers are virtual: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
	public static TrendChartViewModel CreateChartWithPens()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var chart = new TrendChartViewModel(
			coordinator,
			scheduler,
			scheduler,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);

		foreach (var pen in provider.Pens)
		{
			chart.AddPen(pen);
		}

		return chart;
	}

	public static void DriveToLayer(ChartNavigationController navigation, AggregationLayer layer)
	{
		for (var step = 0; step < 200 && navigation.ActiveLayer != layer; step++)
		{
			navigation.ZoomAt(navigation.ActiveLayer < layer ? 2.0 : 0.5, navigation.To);
		}

		navigation.ActiveLayer.Should().Be(layer);
	}
}
