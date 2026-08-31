using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI;

public class App : Application
{
	private static bool _started;

	private IServiceProvider? _serviceProvider;

	private StartupFailureView? _startupFailure;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			desktop.MainWindow = _startupFailure is null ? CreateMainWindow() : new ErrorWindow(_startupFailure);
		}

		base.OnFrameworkInitializationCompleted();
	}

	private Window CreateMainWindow()
	{
		if (_serviceProvider is null)
		{
			throw new InvalidOperationException(
				"ServiceProvider not set. Call Run() before starting the app.");
		}

		var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();

		return new MainWindow.MainWindow { DataContext = mainWindowViewModel };
	}

	public static void Run(StartupData startupData)
	{
		ArgumentNullException.ThrowIfNull(startupData);
		EnsureSingleStart();

		BuildAvaloniaApp()
			.AfterSetup(_ => InitializeServices(startupData))
			.AfterSetup(builder =>
			{
				var app = (App)builder.Instance!;
				app._serviceProvider = startupData.ServiceProvider;
			})
			.StartWithClassicDesktopLifetime([]);
	}

	/// <summary>
	/// The failure branch of startup: one window naming what broke and what to do, and no service
	/// resolution behind it. It shares <see cref="EnsureSingleStart"/> with <see cref="Run"/> because a
	/// process reaches exactly one of them — a second <c>BuildAvaloniaApp()</c> throws once Avalonia is
	/// initialised.
	/// </summary>
	public static void RunErrorWindow(StartupFailureView failure)
	{
		ArgumentNullException.ThrowIfNull(failure);
		EnsureSingleStart();

		BuildAvaloniaApp()
			.AfterSetup(builder =>
			{
				var app = (App)builder.Instance!;
				app._startupFailure = failure;
			})
			.StartWithClassicDesktopLifetime([]);
	}

	// Internal so a test reads the composed builder back and pins the three subsystems the desktop
	// application cannot start without. The test builder composes UseHeadless, which registers its own
	// rendering, windowing and shaping, so nothing headless covers this chain.
	internal static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured"; the headless
			// platform supplies its own shaper, so no test reaches this.
			.UseHarfBuzz()
			// Avalonia 12: UseReactiveUI takes a mandatory builder callback. Nothing here configures the
			// ReactiveUI builder, so the callback is empty.
			.UseReactiveUI(_ => { })
			.LogToTrace();
	}

	// UseReactiveUI() has registered AvaloniaScheduler as RxApp.MainThreadScheduler by now, so the UI
	// scheduler can only be captured here — after that ordering — and handed to the coordinator and the
	// view models. Everything this reads from the archive was read by StartupProbe before
	// Avalonia existed, so this awaits nothing and cannot throw an archive failure through AfterSetup.
	// Internal so a test drives the real startup body rather than a look-alike rebuilt in the test.
	internal static void InitializeServices(StartupData startupData)
	{
		var uiScheduler = AvaloniaScheduler.Instance;
		var serviceProvider = startupData.ServiceProvider;
		var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();
		var pens = startupData.Pens;

		var coordinator = new TrendCoordinator(
			dataProvider,
			pens,
			serviceProvider.GetRequiredService<IScheduler>(),
			uiScheduler);

		var chartViewModel = new TrendChartViewModel(
			coordinator,
			serviceProvider.GetRequiredService<IScheduler>(),
			uiScheduler,
			serviceProvider.GetRequiredService<ILogger<TrendChartViewModel>>());

		// Before the first history request and before the minimap exists: RequestInitialHistory queries
		// whatever window is in force, and the minimap reads it back when its own extent arrives.
		chartViewModel.Navigation.SeedFromArchiveExtent(startupData.Extent);

		foreach (var pen in pens)
		{
			chartViewModel.AddPen(pen);
		}

		var mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
		mainWindowViewModel.ChartViewModel = chartViewModel;

		var minimapViewModel = new MinimapViewModel(
			coordinator,
			chartViewModel.Navigation,
			uiScheduler,
			serviceProvider.GetRequiredService<ILogger<MinimapViewModel>>());
		mainWindowViewModel.MinimapViewModel = minimapViewModel;

		// Before Start, so the first poll tick's state reaches the banner rather than a stream nothing
		// is listening to yet: the coordinator's republished stream has no replay.
		mainWindowViewModel.ObserveArchiveConnection(coordinator.ConnectionFaults);

		coordinator.Start();

		chartViewModel.RequestInitialHistory();
		_ = minimapViewModel.LoadExtentAsync();
	}

	private static void EnsureSingleStart()
	{
		if (_started)
		{
			throw new InvalidOperationException(
				"App has already been started. Run() and RunErrorWindow() must be called at most once per process.");
		}

		_started = true;
	}
}
