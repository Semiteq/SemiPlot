using Microsoft.Extensions.DependencyInjection;

using SemiPlot.UI.Messages;

namespace SemiPlot.UI;

public static class UiServiceCollectionExtensions
{
	// docs/architecture/overview.md#one-window-per-process
	public static IServiceCollection AddUi(this IServiceCollection services)
	{
		// docs/architecture/overview.md#where-a-failure-goes
		services.AddSingleton<MessagePanelViewModel>();

		return services;
	}
}
