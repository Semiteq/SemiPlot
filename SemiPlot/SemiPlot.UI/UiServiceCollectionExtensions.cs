using Microsoft.Extensions.DependencyInjection;

using SemiPlot.UI.MainWindow;

namespace SemiPlot.UI;

public static class UiServiceCollectionExtensions
{
	// The chart and minimap view models take the UI scheduler, which exists only after UseReactiveUI() has
	// run, so App.InitializeServices constructs them directly rather than resolving them here.
	public static IServiceCollection AddUi(this IServiceCollection services)
	{
		services.AddSingleton<MainWindowViewModel>();

		return services;
	}
}
