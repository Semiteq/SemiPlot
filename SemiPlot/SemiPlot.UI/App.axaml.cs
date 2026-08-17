using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

using Microsoft.Extensions.DependencyInjection;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Minimap;

namespace SemiPlot.UI;

public class App : Application
{
	private static bool _started;

	private IServiceProvider? _serviceProvider;

	public override void Initialize()
	{
		AvaloniaXamlLoader.Load(this);
	}

	public override void OnFrameworkInitializationCompleted()
	{
		if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
		{
			if (_serviceProvider is null)
			{
				throw new InvalidOperationException(
					"ServiceProvider not set. Call Run() before starting the app.");
			}

			var mainWindowViewModel = _serviceProvider.GetRequiredService<MainWindowViewModel>();
			var mainWindow = new MainWindow.MainWindow { DataContext = mainWindowViewModel };
			desktop.MainWindow = mainWindow;
		}

		base.OnFrameworkInitializationCompleted();
	}

	public static void Run(IServiceProvider serviceProvider)
	{
		ArgumentNullException.ThrowIfNull(serviceProvider);
		EnsureSingleStart();

		BuildAvaloniaApp()
			.AfterSetup(_ => InitializeServices(serviceProvider))
			.AfterSetup(builder =>
			{
				var app = (App)builder.Instance!;
				app._serviceProvider = serviceProvider;
			})
			.StartWithClassicDesktopLifetime([]);
	}

	private static AppBuilder BuildAvaloniaApp()
	{
		return AppBuilder.Configure<App>()
			.UseWin32()
			.UseSkia()
			.UseReactiveUI()
			.LogToTrace();
	}

	// UseReactiveUI() has registered AvaloniaScheduler as RxApp.MainThreadScheduler by now, so the UI
	// scheduler can only be captured here — after that ordering — and handed to the coordinator and the
	// view-model factories.
	private static void InitializeServices(IServiceProvider serviceProvider)
	{
		var uiScheduler = AvaloniaScheduler.Instance;
		var dataProvider = serviceProvider.GetRequiredService<IDataProvider>();
		var pens = LoadPens(dataProvider);

		var coordinator = new TrendCoordinator(
			dataProvider,
			pens,
			serviceProvider.GetRequiredService<IScheduler>(),
			uiScheduler);

		var chartFactory =
			serviceProvider.GetRequiredService<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>();
		var chartViewModel = chartFactory(coordinator, uiScheduler);

		foreach (var pen in pens)
		{
			chartViewModel.AddPen(pen);
		}

		var mainWindowViewModel = serviceProvider.GetRequiredService<MainWindowViewModel>();
		mainWindowViewModel.ChartViewModel = chartViewModel;

		var minimapFactory = serviceProvider
			.GetRequiredService<Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>>();
		var minimapViewModel = minimapFactory(coordinator, chartViewModel.Navigation, uiScheduler);
		mainWindowViewModel.MinimapViewModel = minimapViewModel;

		coordinator.Start();

		_ = chartViewModel.RequestInitialHistory();
		_ = minimapViewModel.LoadExtentAsync();
	}

	// AfterSetup takes a synchronous delegate, so the one catalogue read the startup path needs blocks here.
	private static IReadOnlyList<Pen> LoadPens(IDataProvider dataProvider)
	{
		return dataProvider.QueryPensAsync().GetAwaiter().GetResult().Value;
	}

	private static void EnsureSingleStart()
	{
		if (_started)
		{
			throw new InvalidOperationException(
				"App has already been started. Run() must be called at most once per process.");
		}

		_started = true;
	}
}
