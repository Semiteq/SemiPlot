using Microsoft.Extensions.DependencyInjection;

using Npgsql;

using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Data.Integration;

// The provider every gated read runs through, built by AddPostgresData rather than by the internal
// constructor, so each test exercises the real registration and no test file repeats the argument list.
//
// Schema is why this is one shared helper instead of a settings record per test class: it becomes
// SearchPath on the connection string (PostgresConnectionSettings.ConnectionString), the harness's own
// connection strings set none (PostgresServer.ConnectionStringFor), and a wrong value makes
// semiplot_tags unresolvable — turning every catalogue test into a 42P01 that reads exactly like a
// correctly detected missing table.
public static class ArchiveProviderFactory
{
	private const string Schema = "public";

	private const string FileVersion = "1";

	// Non-UTC on purpose. Under UTC an unconverted, a doubly converted and a correctly converted extent
	// all read the same, so the time boundary would be invisible to every assertion in this folder.
	public static readonly TimeZoneInfo SourceTimeZone = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");

	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

	// The caller disposes what comes back, which returns the pooled connections before
	// ArchiveDatabase.DisposeAsync drops the database — a pooled connection makes DROP DATABASE refuse.
	public static ServiceProvider Build(string connectionString)
	{
		var services = new ServiceCollection();

		services.AddLogging();
		services.AddPostgresData(SettingsFor(connectionString));

		return services.BuildServiceProvider();
	}

	private static PostgresConnectionSettings SettingsFor(string connectionString)
	{
		var builder = new NpgsqlConnectionStringBuilder(connectionString);

		return new PostgresConnectionSettings(
			FileVersion: FileVersion,
			Host: builder.Host ?? "localhost",
			Port: builder.Port,
			Database: builder.Database ?? string.Empty,
			Username: builder.Username ?? string.Empty,
			Password: builder.Password ?? string.Empty,
			SourceTimeZone: SourceTimeZone,
			PollInterval: _pollInterval,
			Schema: Schema);
	}
}
