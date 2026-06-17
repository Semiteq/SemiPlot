using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Minimap;

namespace SemiPlot.UI;

public static class UiServiceCollectionExtensions
{
	public static IServiceCollection AddUi(this IServiceCollection services)
	{
		services.AddSingleton<MainWindowViewModel>();

		// The UI scheduler is only known after UseReactiveUI() has registered AvaloniaScheduler, so it
		// is a factory parameter rather than a container registration. The data scheduler and remaining
		// dependencies are resolved from the container.
		services.AddSingleton<Func<IScheduler, TrendCoordinator>>(provider =>
			uiScheduler => new TrendCoordinator(
				provider.GetRequiredService<IDataProvider>(),
				provider.GetRequiredService<ILogger<TrendCoordinator>>(),
				provider.GetRequiredService<IScheduler>(),
				uiScheduler));

		// The chart view model shares the captured UI scheduler with the coordinator so its realtime
		// and history subscriptions observe on the same thread the coordinator publishes on. The data
		// scheduler is resolved from the container so the debounced gesture history query runs off the
		// UI thread.
		services.AddSingleton<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>(provider =>
			(coordinator, uiScheduler) => new TrendChartViewModel(
				coordinator,
				provider.GetRequiredService<IScheduler>(),
				uiScheduler));

		// The minimap shares the chart's navigation controller (so its highlight and click navigation
		// drive the same window) and the coordinator's extent seam; the UI scheduler is the captured one.
		services.AddSingleton<Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>>(provider =>
			(coordinator, navigation, uiScheduler) => new MinimapViewModel(
				coordinator,
				navigation,
				uiScheduler,
				provider.GetRequiredService<ILogger<MinimapViewModel>>()));

		return services;
	}
}
