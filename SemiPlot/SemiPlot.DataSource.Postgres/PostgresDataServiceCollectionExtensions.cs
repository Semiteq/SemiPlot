using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

public static class PostgresDataServiceCollectionExtensions
{
	public static IServiceCollection AddPostgresData(
		this IServiceCollection services,
		PostgresConnectionSettings settings)
	{
		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.AddSingleton(settings);
		services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
		services.AddSingleton(new ArchiveTimeConverter(settings.SourceTimeZone));
		services.AddSingleton(new ArchiveExceptionMapper(settings));

		// Factory, not type activation: the provider's constructor is internal.
		services.AddSingleton<IDataProvider>(provider => new PostgresDataProvider(
			provider.GetRequiredService<NpgsqlDataSource>(),
			provider.GetRequiredService<ArchiveTimeConverter>(),
			provider.GetRequiredService<ArchiveExceptionMapper>(),
			provider.GetRequiredService<PostgresConnectionSettings>(),
			provider.GetRequiredService<IScheduler>(),
			provider.GetRequiredService<ILogger<PostgresDataProvider>>()));

		return services;
	}
}
