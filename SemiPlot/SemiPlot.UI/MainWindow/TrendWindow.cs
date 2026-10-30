using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Export;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Export;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Markers;
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
	private readonly MarkerSync _markerSync;
	private readonly TrendChartViewModel _chart;
	private readonly MinimapViewModel _minimap;
	private readonly PenCatalogueSync _catalogueSync;
	private readonly AppStatusBarViewModel _statusBar;
	private readonly NavigationBarViewModel _navigationBar;
	private readonly TrendLegendViewModel _legend;
	private readonly PenCatalogueApplier _catalogueApplier;

	private TrendWindow(
		TrendCoordinator coordinator,
		MarkerSync markerSync,
		TrendChartViewModel chart,
		MinimapViewModel minimap,
		PenCatalogueSync catalogueSync,
		AppStatusBarViewModel statusBar,
		NavigationBarViewModel navigationBar,
		TrendLegendViewModel legend,
		PenCatalogueApplier catalogueApplier,
		MainWindowViewModel viewModel)
	{
		_coordinator = coordinator;
		_markerSync = markerSync;
		_chart = chart;
		_minimap = minimap;
		_catalogueSync = catalogueSync;
		_statusBar = statusBar;
		_navigationBar = navigationBar;
		_legend = legend;
		_catalogueApplier = catalogueApplier;
		ViewModel = viewModel;
	}

	public MainWindowViewModel ViewModel { get; }

	public static TrendWindow Build(
		StartupData startupData,
		InstanceLauncher launcher,
		IScheduler uiScheduler,
		CsvDialect csvDialect,
		TimeZoneInfo archiveZone)
	{
		var serviceProvider = startupData.ServiceProvider;
		var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();
		var liveSource = serviceProvider.GetRequiredService<ILiveSource>();
		var dataScheduler = serviceProvider.GetRequiredService<IScheduler>();
		var messagePanel = serviceProvider.GetRequiredService<MessagePanelViewModel>();
		var penCatalogueEditor = serviceProvider.GetRequiredService<IPenCatalogueEditor>();
		var archiveRowReader = serviceProvider.GetRequiredService<IArchiveRowReader>();
		var markerStore = serviceProvider.GetRequiredService<IMarkerStore>();
		var loggerFactory = serviceProvider.GetRequiredService<ILoggerFactory>();

		var coordinator = new TrendCoordinator(
			dataProvider, liveSource, startupData.Catalogue.Pens, dataScheduler, uiScheduler);
		var markerSync = new MarkerSync(
			markerStore, messagePanel, uiScheduler, loggerFactory.CreateLogger<MarkerSync>());

		var chart = new TrendChartViewModel(
			coordinator,
			markerSync,
			dataScheduler,
			uiScheduler,
			archiveZone,
			messagePanel,
			loggerFactory.CreateLogger<TrendChartViewModel>());

		// Before the first history request and before the minimap exists: RequestInitialHistory queries
		// whatever window is in force, and the minimap reads it back when its own extent arrives.
		chart.Navigation.SeedFromArchiveExtent(startupData.Extent);
		chart.ApplyCatalogue(startupData.Catalogue.Pens);

		var minimapLogger = loggerFactory.CreateLogger<MinimapViewModel>();
		var minimap = new MinimapViewModel(coordinator, chart, uiScheduler, messagePanel, minimapLogger);
		var catalogueSync = new PenCatalogueSync(
			dataProvider,
			startupData.Catalogue,
			messagePanel,
			uiScheduler,
			loggerFactory.CreateLogger<PenCatalogueSync>());

		// Subscribed before catalogueSync.Start(), so no state precedes the bar.
		var statusBar = new AppStatusBarViewModel(
			messagePanel,
			catalogueSync.ConnectionStates,
			chart.Navigation,
			loggerFactory.CreateLogger<AppStatusBarViewModel>());
		var navigationBar = new NavigationBarViewModel(chart);
		var legend = new TrendLegendViewModel(chart, startupData.Catalogue.Stored.Groups);
		var catalogueApplier = new PenCatalogueApplier(
			startupData.Catalogue, catalogueSync, chart, minimap, legend, uiScheduler);
		var viewModel = new MainWindowViewModel(
			messagePanel,
			statusBar,
			chart,
			minimap,
			navigationBar,
			legend,
			catalogueSync.ReadNow,
			loggerFactory,
			penCatalogueEditor,
			(fromUtc, toUtc, pens, markers) => new CsvExportViewModel(
				archiveRowReader,
				coordinator.QueryArchiveDaysAsync,
				csvDialect,
				messagePanel,
				loggerFactory.CreateLogger<CsvExportViewModel>(),
				uiScheduler,
				archiveZone,
				fromUtc,
				toUtc,
				chart.Navigation.FirstSample,
				chart.Navigation.NewestSample,
				pens,
				markers),
			launcher);

		catalogueSync.Start();
		catalogueSync.ReadNow();
		markerSync.Start();
		markerSync.ReadNow();
		chart.RequestInitialHistory();
		StartExtentLoad(minimap, messagePanel, minimapLogger, uiScheduler);

		return new TrendWindow(
			coordinator,
			markerSync,
			chart,
			minimap,
			catalogueSync,
			statusBar,
			navigationBar,
			legend,
			catalogueApplier,
			viewModel);
	}

	public void Dispose()
	{
		ViewModel.Dispose();
		_catalogueApplier.Dispose();
		_legend.Dispose();
		_navigationBar.Dispose();
		_statusBar.Dispose();
		_catalogueSync.Dispose();
		_minimap.Dispose();
		_chart.Dispose();
		_markerSync.Dispose();
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
