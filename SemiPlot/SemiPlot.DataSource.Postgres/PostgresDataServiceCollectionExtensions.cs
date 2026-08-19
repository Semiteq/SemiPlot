using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.DataSource.Postgres;

public static class PostgresDataServiceCollectionExtensions
{
	// Named apart from the stub's AddData so a composition root may reference both projects and pick one.
	public static IServiceCollection AddPostgresData(
		this IServiceCollection services,
		PostgresConnectionSettings settings)
	{
		ArgumentNullException.ThrowIfNull(services);
		ArgumentNullException.ThrowIfNull(settings);

		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.AddSingleton(settings);
		services.AddSingleton<ArchiveDataSource>();
		services.AddSingleton(new ArchiveTimeConverter(settings.SourceTimeZone));
		services.AddSingleton<MissingRelationProbe>();
		services.AddSingleton<StatementTimeoutReader>();
		services.AddSingleton(new ArchiveExceptionMapper(settings));

		// A factory rather than type activation: three of the provider's constructor parameters are internal
		// types, so its constructor is internal too and the container's public-constructor lookup would not
		// find it.
		services.AddSingleton<IDataProvider>(provider => new PostgresDataProvider(
			provider.GetRequiredService<ArchiveDataSource>(),
			provider.GetRequiredService<ArchiveTimeConverter>(),
			provider.GetRequiredService<ArchiveExceptionMapper>(),
			provider.GetRequiredService<MissingRelationProbe>(),
			provider.GetRequiredService<StatementTimeoutReader>(),
			provider.GetRequiredService<ILogger<PostgresDataProvider>>()));

		return services;
	}
}
