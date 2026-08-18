using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Data.Postgres;

// Points at an address nothing answers, which is safe because no caller issues a read.
internal static class ConnectionSettingsFactory
{
	public const string Database = "semiplot_dev";

	public const string Username = "semiplot_reader";

	public static PostgresConnectionSettings Create(
		TimeZoneInfo? sourceTimeZone = null,
		string host = "127.0.0.1",
		int port = 1)
	{
		return new PostgresConnectionSettings(
			FileVersion: "1",
			Host: host,
			Port: port,
			Database: Database,
			Username: Username,
			Password: "unused",
			SourceTimeZone: sourceTimeZone ?? TimeZoneInfo.Utc,
			PollInterval: TimeSpan.FromSeconds(1),
			Schema: "public");
	}
}
