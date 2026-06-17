using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;

namespace SemiPlot.Core.Data;

public static class DataServiceCollectionExtensions
{
	public static IServiceCollection AddData(this IServiceCollection services)
	{
		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.AddSingleton<IDataProvider, RandomStubDataProvider>();

		return services;
	}
}
