using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.DependencyInjection.Extensions;
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
		return services.AddPostgresData(settings, PostgresDataProvider.HistoryReadConcurrency);
	}

	/// <summary>The same composition with the pen group count of one history read given.</summary>
	internal static IServiceCollection AddPostgresData(
		this IServiceCollection services,
		PostgresConnectionSettings settings,
		int historyReadConcurrency)
	{
		services.AddSingleton<IScheduler>(DefaultScheduler.Instance);
		services.TryAddSingleton(TimeProvider.System);
		services.AddSingleton(settings);
		services.AddSingleton(_ => NpgsqlDataSource.Create(settings.ConnectionString));
		services.AddSingleton(new ArchiveTimeConverter(settings.SourceTimeZone));
		services.AddSingleton(new ArchiveExceptionMapper(settings));

		// Factories, not type activation: the constructors are internal.
		services.AddSingleton<IDataProvider>(provider => new PostgresDataProvider(
			provider.GetRequiredService<NpgsqlDataSource>(),
			provider.GetRequiredService<ArchiveTimeConverter>(),
			provider.GetRequiredService<ArchiveExceptionMapper>(),
			provider.GetRequiredService<PostgresConnectionSettings>(),
			provider.GetRequiredService<IScheduler>(),
			provider.GetRequiredService<TimeProvider>(),
			provider.GetRequiredService<ILogger<PostgresDataProvider>>(),
			historyReadConcurrency));
		services.AddSingleton<IPenCatalogueEditor>(provider => new PostgresPenCatalogueEditor(
			provider.GetRequiredService<NpgsqlDataSource>(),
			provider.GetRequiredService<ArchiveExceptionMapper>(),
			provider.GetRequiredService<ILogger<PostgresPenCatalogueEditor>>()));
		services.AddSingleton<IArchiveRowReader>(provider => new PostgresArchiveRowReader(
			provider.GetRequiredService<NpgsqlDataSource>(),
			provider.GetRequiredService<ArchiveExceptionMapper>(),
			provider.GetRequiredService<ILogger<PostgresArchiveRowReader>>()));

		return services;
	}
}
