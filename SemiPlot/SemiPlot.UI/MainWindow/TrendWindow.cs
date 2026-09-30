using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Navigation;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// One window's composition and its owner: builds every part once, in dependency order, and disposes them
/// in reverse (docs/architecture/overview.md#one-window-per-process). No part is ever replaced.
/// </summary>
internal sealed class TrendWindow : IDisposable
{
	private readonly TrendCoordinator _coordinator;
	private readonly TrendChartViewModel _chart;
	private readonly AppStatusBarViewModel _statusBar;
	private readonly MinimapViewModel _minimap;
	private readonly PenCatalogueSync _catalogueSync;
	private readonly NavigationBarViewModel _navigationBar;
	private readonly TrendLegendViewModel _legend;
	private readonly PenCatalogueApplier _catalogueApplier;

	private TrendWindow(
		TrendCoordinator coordinator,
		TrendChartViewModel chart,
		AppStatusBarViewModel statusBar,
		MinimapViewModel minimap,
		PenCatalogueSync catalogueSync,
		NavigationBarViewModel navigationBar,
		TrendLegendViewModel legend,
		PenCatalogueApplier catalogueApplier,
		MainWindowViewModel viewModel)
	{
		_coordinator = coordinator;
		_chart = chart;
		_statusBar = statusBar;
		_minimap = minimap;
		_catalogueSync = catalogueSync;
		_navigationBar = navigationBar;
		_legend = legend;
		_catalogueApplier = catalogueApplier;
		ViewModel = viewModel;
	}

	public MainWindowViewModel ViewModel { get; }

	public static TrendWindow Build(StartupData startupData, string configDirectory, IScheduler uiScheduler)
	{
		var serviceProvider = startupData.ServiceProvider;
		var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();
		var dataScheduler = serviceProvider.GetRequiredService<IScheduler>();
		var messagePanel = serviceProvider.GetRequiredService<MessagePanelViewModel>();
		var penCatalogueEditor = serviceProvider.GetRequiredService<IPenCatalogueEditor>();
		var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

		var coordinator = new TrendCoordinator(dataProvider, startupData.Pens, dataScheduler, uiScheduler);

		var chart = new TrendChartViewModel(
			coordinator,
			dataScheduler,
			uiScheduler,
			messagePanel,
			loggerFactory.CreateLogger<TrendChartViewModel>());

		// Before the first history request and before the minimap exists: RequestInitialHistory queries
		// whatever window is in force, and the minimap reads it back when its own extent arrives.
		chart.Navigation.SeedFromArchiveExtent(startupData.Extent);
		chart.ApplyCatalogue(startupData.Pens);

		var statusBar = new AppStatusBarViewModel(
			messagePanel,
			coordinator.ConnectionFaults,
			chart.Navigation,
			loggerFactory.CreateLogger<AppStatusBarViewModel>());

		var minimapLogger = loggerFactory.CreateLogger<MinimapViewModel>();
		var minimap = new MinimapViewModel(coordinator, chart.Navigation, uiScheduler, messagePanel, minimapLogger);
		var catalogueSync = new PenCatalogueSync(
			dataProvider,
			startupData.Pens,
			messagePanel,
			uiScheduler,
			loggerFactory.CreateLogger<PenCatalogueSync>());
		var navigationBar = new NavigationBarViewModel(chart);
		var legend = new TrendLegendViewModel(chart);
		var catalogueApplier = new PenCatalogueApplier(catalogueSync, chart, minimap, legend, uiScheduler);
		var viewModel = new MainWindowViewModel(
			messagePanel,
			statusBar,
			chart,
			minimap,
			navigationBar,
			legend,
			catalogueSync.ReadNow,
			configDirectory,
			loggerFactory,
			penCatalogueEditor);

		catalogueSync.Start();
		chart.RequestInitialHistory();
		StartExtentLoad(minimap, messagePanel, minimapLogger, uiScheduler);

		return new TrendWindow(
			coordinator, chart, statusBar, minimap, catalogueSync, navigationBar, legend, catalogueApplier, viewModel);
	}

	public void Dispose()
	{
		ViewModel.Dispose();
		_catalogueApplier.Dispose();
		_legend.Dispose();
		_navigationBar.Dispose();
		_catalogueSync.Dispose();
		_minimap.Dispose();
		_statusBar.Dispose();
		_chart.Dispose();
		_coordinator.Dispose();
	}

	private static void StartExtentLoad(
		MinimapViewModel minimap,
		MessagePanelViewModel messagePanel,
		ILogger<MinimapViewModel> logger,
		IScheduler uiScheduler)
	{
		// OnlyOnFaulted, so load.Exception is never null.
		_ = minimap.LoadExtentAsync().ContinueWith(
			load => messagePanel.TryReportFailure(
				new ExceptionalError(load.Exception!.GetBaseException()), logger, uiScheduler),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}
}
