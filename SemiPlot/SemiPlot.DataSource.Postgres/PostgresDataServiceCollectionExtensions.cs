using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

public static class PostgresDataServiceCollectionExtensions
{
	// Named for its own data source rather than a bare AddData, so a composition root referencing several
	// SemiPlot.DataSource.* projects names the one it registers.
	public static IServiceCollection AddPostgresData(
		this IServiceCollection services,
		PostgresConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(settings);

		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.AddSingleton(settings);
		services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
		services.AddSingleton(new ArchiveTimeConverter(settings.SourceTimeZone));
		services.AddSingleton(new ArchiveExceptionMapper(settings));

		// A factory rather than type activation: two of the provider's constructor parameters are internal
		// types, so its constructor is internal too and the container's public-constructor lookup would not
		// find it.
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
