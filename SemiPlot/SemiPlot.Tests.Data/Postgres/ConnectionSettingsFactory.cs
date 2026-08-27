using SemiPlot.DataSource.Postgres.Configuration;

namespace SemiPlot.Tests.Data.Postgres;

// Points at an address nothing answers; no test built on it reaches the server. A test that does issue a
// read is an integration one and takes its settings from the seeded archive instead.
internal static class ConnectionSettingsFactory
{
	public const string Database = "semiplot_dev";

	public const string Username = "semiplot_reader";

	// The default interval matches the one a bench runs at. A test that has to watch a poll loop in real
	// time passes a shorter one rather than waiting a second per tick.
	private static readonly TimeSpan _defaultPollInterval = TimeSpan.FromSeconds(1);

	public static PostgresConnectionSettings Create(
		TimeZoneInfo? sourceTimeZone = null,
		string host = "127.0.0.1",
		int port = 1,
		TimeSpan? pollInterval = null)
	{
		return new PostgresConnectionSettings(
			Host: host,
			Port: port,
			Database: Database,
			Username: Username,
			Password: "unused",
			SourceTimeZone: sourceTimeZone ?? TimeZoneInfo.Utc,
			PollInterval: pollInterval ?? _defaultPollInterval,
			Schema: "public");
	}
}
