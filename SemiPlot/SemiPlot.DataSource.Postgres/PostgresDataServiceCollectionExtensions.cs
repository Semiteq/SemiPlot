using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;

namespace SemiPlot.DataSource.Postgres;

public static class PostgresDataServiceCollectionExtensions
{
	// Named apart from the stub's AddData so a composition root may reference both projects and pick one.
	public static IServiceCollection AddPostgresData(this IServiceCollection services)
	{
		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.AddSingleton<IDataProvider, PostgresDataProvider>();

		return services;
	}
}
