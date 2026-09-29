using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>What <see cref="MainWindowTestBuilder.NewLiveCatalogueStand"/> builds, with its clock.</summary>
internal sealed record LiveCatalogueStand(
	MainWindowViewModel ViewModel,
	FakeDataProvider Provider,
	TestScheduler Scheduler);

/// <summary>The window's view models as every test in this folder builds them, plus the layer drive.</summary>
internal static class MainWindowTestBuilder
{
	public static MainWindowViewModel NewViewModel()
	{
		return NewViewModel(AppContext.BaseDirectory);
	}

	/// <summary>
	/// A null directory is the startup-failure window's shape after a failed argument parse; a null editor is
	/// that window's shape on every failed startup.
	/// </summary>
	public static MainWindowViewModel NewViewModel(
		string? configDirectory,
		IPenCatalogueEditor? penCatalogueEditor = null)
	{
		var panel = new MessagePanelViewModel();

		return new MainWindowViewModel(
			panel,
			NewStatusBar(panel),
			configDirectory,
			NullLoggerFactory.Instance,
			penCatalogueEditor);
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

	public static TrendChartViewModel CreateChartWithPens()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var chart = ChartTestBuilder.CreateChart(scheduler, provider);
		chart.ApplyCatalogue(provider.Pens);

		return chart;
	}

	/// <summary>
	/// A window view model over a chart, a minimap and a started catalogue read loop, wired in the order
	/// App.InitializeServices wires them, all on one virtual clock and one provider; the loop has entered its
	/// first wait.
	/// </summary>
	public static LiveCatalogueStand NewLiveCatalogueStand(
		IReadOnlyList<Pen>? pens = null,
		IPenCatalogueEditor? penCatalogueEditor = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1), pens);
		var viewModel = NewViewModel(AppContext.BaseDirectory, penCatalogueEditor);
		var messagePanel = viewModel.MessagePanel;
		var coordinator = ChartTestBuilder.CreateCoordinator(scheduler, provider);
		var chart = ChartTestBuilder.CreateChart(scheduler, coordinator, messagePanel);
		chart.ApplyCatalogue(provider.Pens);

		var minimap = new MinimapViewModel(
			coordinator,
			chart.Navigation,
			ImmediateScheduler.Instance,
			messagePanel,
			NullLogger<MinimapViewModel>.Instance);
		var catalogueSync = new PenCatalogueSync(
			provider,
			provider.Pens,
			messagePanel,
			scheduler,
			NullLogger<PenCatalogueSync>.Instance);

		viewModel.SetChart(chart);
		viewModel.SetMinimap(minimap);
		viewModel.SetCatalogueSync(catalogueSync, scheduler);
		catalogueSync.Start();
		chart.RequestInitialHistory();
		scheduler.AdvanceBy(1);

		return new LiveCatalogueStand(viewModel, provider, scheduler);
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
