using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Integration;

// Schema becomes SearchPath; a wrong value turns every catalogue read into a 42P01 look-alike.
public static class ArchiveProviderFactory
{
	private const string Schema = "public";

	// Non-UTC on purpose. Under UTC an unconverted, a doubly converted and a correctly converted extent
	// all read the same, so the time boundary would be invisible to every assertion in this folder.
	public static readonly TimeZoneInfo SourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	public static readonly ArchiveTimeConverter TimeConverter = new(SourceTimeZone);

	// The pool wait a test that caps the pool sets: one connect past the cap fails within it.
	private const int CappedPoolWaitSeconds = 2;

	// The caller disposes what comes back, which returns the pooled connections before
	// ArchiveDatabase.DisposeAsync drops the database, and a pooled connection makes DROP DATABASE refuse.
	public static ServiceProvider Build(
		string connectionString,
		ILoggerProvider? loggerProvider = null,
		TimeProvider? timeProvider = null,
		int historyReadConcurrency = PostgresDataProvider.HistoryReadConcurrency,
		int? maxPoolSize = null)
	{
		var services = new ServiceCollection();

		services.AddTestLogging(loggerProvider);

		if (timeProvider is not null)
		{
			services.AddSingleton(timeProvider);
		}

		var settings = SettingsFor(connectionString);
		services.AddPostgresData(settings, historyReadConcurrency);

		if (maxPoolSize is { } poolSize)
		{
			// Registered after AddPostgresData's own data source, so every resolution takes this one.
			var cappedConnectionString = new NpgsqlConnectionStringBuilder(settings.ConnectionString)
			{
				MaxPoolSize = poolSize,
				Timeout = CappedPoolWaitSeconds
			}.ConnectionString;
			services.AddSingleton(_ => NpgsqlDataSource.Create(cappedConnectionString));
		}

		return services.BuildServiceProvider();
	}

	/// <summary>Sends every log line from Debug up to <paramref name="loggerProvider"/>, when one is passed.</summary>
	internal static IServiceCollection AddTestLogging(this IServiceCollection services, ILoggerProvider? loggerProvider)
	{
		return services.AddLogging(builder =>
		{
			if (loggerProvider is not null)
			{
				builder.SetMinimumLevel(LogLevel.Debug);
				builder.AddProvider(loggerProvider);
			}
		});
	}

	private static PostgresConnectionSettings SettingsFor(string connectionString)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);

		return new PostgresConnectionSettings(
			Host: builder.Host ?? "127.0.0.1",
			Port: builder.Port,
			Database: builder.Database ?? string.Empty,
			Username: builder.Username ?? string.Empty,
			Password: builder.Password ?? string.Empty,
			SourceTimeZone: SourceTimeZone,
			Schema: Schema);
	}
}
