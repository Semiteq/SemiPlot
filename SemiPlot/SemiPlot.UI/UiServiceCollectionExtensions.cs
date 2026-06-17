using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.MainWindow;

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
		// and history subscriptions observe on the same thread the coordinator publishes on.
		services.AddSingleton<Func<TrendCoordinator, IScheduler, TrendChartViewModel>>(_ =>
			(coordinator, uiScheduler) => new TrendChartViewModel(coordinator, uiScheduler));

		return services;
	}
}
