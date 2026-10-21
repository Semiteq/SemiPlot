using Microsoft.Extensions.DependencyInjection;

using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Integration;

// Points at a port nothing answers, so a test built on it fails on the connect attempt and needs no
// container.
internal static class UnreachableArchive
{
	public static PostgresConnectionSettings Settings()
	{
		return new PostgresConnectionSettings(
			Host: "127.0.0.1",
			Port: 1,
			Database: "semiplot_dev",
			Username: "semiplot_reader",
			Password: "unused",
			SourceTimeZone: TimeZoneInfo.Utc,
			Schema: "public");
	}

	public static ServiceProvider Services(PostgresConnectionSettings settings)
	{
		return new ServiceCollection()
			.AddLogging()
			.AddPostgresData(settings)
			.BuildServiceProvider();
	}
}
