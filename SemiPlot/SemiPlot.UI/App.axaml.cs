using System.Globalization;
using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Styling;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using Semi.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Startup;

using Serilog.Extensions.Logging;

namespace SemiPlot.UI;

public class App : Application
{
	// Process lifetime by design: ReactiveUI takes its exception handler once, before any service
	// provider exists, so the observer outlives every window and resolves the panel on first use.
	private static readonly UnhandledErrorObserver _unhandledErrors = new(
		ResolveMessagePanel,
		AvaloniaScheduler.Instance,
		new SerilogLoggerFactory().CreateLogger(nameof(UnhandledErrorObserver)));

	private IServiceProvider? _serviceProvider;

	private ArchiveFailureView? _startupFailure;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = CreateMainWindow();
		}

		base.OnFrameworkInitializationCompleted();
	}

	private Window CreateMainWindow()
	{
		if (_startupFailure is not null)
		{
			// This path runs before any container exists, so the window gets a panel and a status bar of its
			// own. ResolveMessagePanel finds no container and returns null, so the About-dialog failure is the
			// only one that can open this panel; the startup-failure row shows the failure and hides the bar.
			var startupPanel = new MessagePanelViewModel();
			var startupLoggers = new SerilogLoggerFactory();

			return new MainWindow.MainWindow
			{
				DataContext = new MainWindowViewModel(
					startupPanel,
					new AppStatusBarViewModel(
						startupPanel, startupLoggers.CreateLogger<AppStatusBarViewModel>()),
					startupLoggers.CreateLogger<MainWindowViewModel>())
				{
					StartupFailure = _startupFailure
				}
			};
		}

		if (_serviceProvider is null)
		{
			throw new InvalidOperationException(
				"ServiceProvider not set. Call Run() before starting the app.");
		}

		var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

		return new MainWindow.MainWindow { DataContext = mainWindowViewModel };
	}

	/// <summary>
	/// <paramref name="settings"/> is null only when the settings load itself failed; that window renders
	/// on the variant <c>App.axaml</c> declares, and every other window follows the configured one.
	/// </summary>
	public static void Run(AppSettings? settings, Result<StartupData> startup)
	{
		BuildAvaloniaApp()
			.AfterSetup(builder => Configure((App)builder.Instance!, settings, startup))
			.StartWithClassicDesktopLifetime([]);
	}

	internal static void Configure(App app, AppSettings? settings, Result<StartupData> startup)
	{
		// Above the failure return, so an archive failure still renders on the configured variant.
		if (settings is not null)
		{
			app.RequestedThemeVariant = VariantFor(settings.Theme);
		}

		// A settings failure has no configured locale, and the bootstrap one is what its window is
		// already read in.
		SemiTheme.OverrideLocaleResources(
			app, SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale));

		if (startup.IsFailed)
		{
			app._startupFailure = ArchiveFailureMapper.Map(startup.Errors[0]);

			return;
		}

		InitializeServices(startup.Value);
		app._serviceProvider = startup.Value.ServiceProvider;
	}

	internal static CultureInfo SemiLocaleFor(UiLanguage locale)
	{
		return SettingsVocabulary.Of(locale).SemiCulture;
	}

	internal static ThemeVariant VariantFor(AppThemeVariant theme)
	{
		return SettingsVocabulary.Of(theme).Variant;
	}

	internal static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured".
			.UseHarfBuzz()
			// docs/architecture/overview.md#where-a-failure-goes
			.UseReactiveUI(builder => builder.WithExceptionHandler(_unhandledErrors))
			.LogToTrace();
	}

	/// <summary>The panel exists only once the container does; before that a failure is logged and nothing more.</summary>
	private static MessagePanelViewModel? ResolveMessagePanel()
	{
		return (Current as App)?._serviceProvider?.GetService<MessagePanelViewModel>();
	}

	internal static void InitializeServices(StartupData startupData)
	{
		var uiScheduler = AvaloniaScheduler.Instance;
		var serviceProvider = startupData.ServiceProvider;
		var messagePanel = serviceProvider.GetRequiredService<MessagePanelViewModel>();

		var coordinator = new TrendCoordinator(
			serviceProvider.GetRequiredService<IDataProvider>(),
			startupData.Pens,
			serviceProvider.GetRequiredService<IScheduler>(),
			uiScheduler);

		var chartViewModel = BuildChart(startupData, coordinator, messagePanel, uiScheduler);
		var minimapViewModel = BuildMinimap(startupData, coordinator, chartViewModel, messagePanel, uiScheduler);

		var mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
		mainWindowViewModel.SetChart(chartViewModel);
		mainWindowViewModel.SetMinimap(minimapViewModel);

		// Before Start, so the first poll tick's state reaches the status bar rather than a stream nothing
		// is listening to yet: the coordinator's republished stream has no replay.
		mainWindowViewModel.StatusBar.TrackArchiveConnection(coordinator.ConnectionFaults);

		coordinator.Start();

		chartViewModel.RequestInitialHistory();

		StartExtentLoad(startupData, minimapViewModel, messagePanel, uiScheduler);
	}

	private static TrendChartViewModel BuildChart(
		StartupData startupData,
		TrendCoordinator coordinator,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler)
	{
		var serviceProvider = startupData.ServiceProvider;

		var chartViewModel = new TrendChartViewModel(
			coordinator,
			serviceProvider.GetRequiredService<IScheduler>(),
			uiScheduler,
			messagePanel,
			serviceProvider.GetRequiredService<ILogger<TrendChartViewModel>>());

		// Before the first history request and before the minimap exists: RequestInitialHistory queries
		// whatever window is in force, and the minimap reads it back when its own extent arrives.
		chartViewModel.Navigation.SeedFromArchiveExtent(startupData.Extent);

		foreach (var pen in startupData.Pens)
		{
			chartViewModel.AddPen(pen);
		}

		return chartViewModel;
	}

	private static MinimapViewModel BuildMinimap(
		StartupData startupData,
		TrendCoordinator coordinator,
		TrendChartViewModel chartViewModel,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler)
	{
		return new MinimapViewModel(
			coordinator,
			chartViewModel.Navigation,
			uiScheduler,
			messagePanel,
			startupData.ServiceProvider.GetRequiredService<ILogger<MinimapViewModel>>());
	}

	private static void StartExtentLoad(
		StartupData startupData,
		MinimapViewModel minimapViewModel,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler)
	{
		var minimapLogger = startupData.ServiceProvider.GetRequiredService<ILogger<MinimapViewModel>>();

		// OnlyOnFaulted, so load.Exception is never null.
		_ = minimapViewModel.LoadExtentAsync().ContinueWith(
			load => messagePanel.TryReportFailure(
				new ExceptionalError(load.Exception!.GetBaseException()), minimapLogger, uiScheduler),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}
}
