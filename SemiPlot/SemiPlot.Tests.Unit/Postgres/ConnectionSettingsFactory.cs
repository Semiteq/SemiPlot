using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Unit.Postgres;

// Points at an address nothing answers; no test built on it reaches the server. A test that does issue a
// read is an integration one and takes its settings from the seeded archive instead.
internal static class ConnectionSettingsFactory
{
	public const string Database = "semiplot_dev";

	public const string Username = "semiplot_reader";

	private static readonly TimeSpan _pollInterval = TimeSpan.FromSeconds(1);

	public static PostgresConnectionSettings Create(
		TimeZoneInfo? sourceTimeZone = null,
		string host = "127.0.0.1",
		int port = 1)
	{
		return new PostgresConnectionSettings(
			Host: host,
			Port: port,
			Database: Database,
			Username: Username,
			Password: "unused",
			SourceTimeZone: sourceTimeZone ?? TimeZoneInfo.Utc,
			PollInterval: _pollInterval,
			Schema: "public");
	}
}
