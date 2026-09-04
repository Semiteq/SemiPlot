using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using FluentResults;

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
			return new MainWindow.MainWindow
			{
				DataContext = new MainWindowViewModel { StartupFailure = _startupFailure }
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

	public static void Run(Result<StartupData> startup)
	{
		BuildAvaloniaApp()
			.AfterSetup(builder =>
			{
				var app = (App)builder.Instance!;

				if (startup.IsFailed)
				{
					app._startupFailure = ArchiveFailureMapper.Map(startup.Errors[0]);

					return;
				}

				InitializeServices(startup.Value);
				app._serviceProvider = startup.Value.ServiceProvider;
			})
			.StartWithClassicDesktopLifetime([]);
	}

	internal static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			// Avalonia 12: Skia no longer brings a text shaper with it. Without UseHarfBuzz the desktop
			// application fails at AppBuilder.Setup with "No text shaping system configured".
			.UseHarfBuzz()
			.UseReactiveUI(_ => { })
			.LogToTrace();
	}

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
}
