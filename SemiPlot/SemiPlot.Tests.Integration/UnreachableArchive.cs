using System.Reactive.Concurrency;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging;

using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Integration;

// Points at a port nothing answers, so a test built on it fails on the connect attempt and needs no
// container.
internal static class UnreachableArchive
{
	public static PostgresConnectionSettings Settings(TimeSpan pollInterval)
	{
		return new PostgresConnectionSettings(
			Host: "127.0.0.1",
			Port: 1,
			Database: "semiplot_dev",
			Username: "semiplot_reader",
			Password: "unused",
			SourceTimeZone: TimeZoneInfo.Utc,
			PollInterval: pollInterval,
			Schema: "public");
	}

	public static ServiceProvider Services(
		PostgresConnectionSettings settings,
		ILoggerProvider? loggerProvider = null,
		IScheduler? scheduler = null)
	{
		var services = new ServiceCollection()
			.AddTestLogging(loggerProvider)
			.AddPostgresData(settings);

		if (scheduler is not null)
		{
			// Registered after AddPostgresData's own scheduler, so every resolution takes this one.
			services.AddSingleton(scheduler);
		}

		return services.BuildServiceProvider();
	}
}
