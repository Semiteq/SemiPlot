namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// What the file looks like: every member nullable so an absent field reaches the loader as a state to
/// report rather than as a silent default. The YAML key of each member is its name in the underscored
/// convention the loader's deserializer applies, so <c>PollIntervalMs</c> reads <c>poll_interval_ms</c>.
/// </summary>
internal sealed class PostgresConnectionDto
{
	public string? ConnectionFileVersion { get; set; }

	public string? Host { get; set; }

	public int? Port { get; set; }

	public string? Database { get; set; }

	public string? User { get; set; }

	public string? Password { get; set; }

	public string? SourceTimeZone { get; set; }

	public int? PollIntervalMs { get; set; }

	public string? Schema { get; set; }
}
