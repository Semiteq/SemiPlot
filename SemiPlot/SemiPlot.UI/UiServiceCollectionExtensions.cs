using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

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

		// The UI scheduler is only known after UseReactiveUI() registers AvaloniaScheduler, so it is a
		// factory parameter rather than a container registration.
		services.AddSingleton<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>(provider =>
			(coordinator, uiScheduler) => new TrendChartViewModel(
				coordinator,
				provider.GetRequiredService<IScheduler>(),
				uiScheduler,
				provider.GetRequiredService<ILogger<TrendChartViewModel>>()));

		services
			.AddSingleton<Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>>(provider =>
				(coordinator, navigation, uiScheduler) => new MinimapViewModel(
					coordinator,
					navigation,
					uiScheduler,
					provider.GetRequiredService<ILogger<MinimapViewModel>>()));

		return services;
	}
}
