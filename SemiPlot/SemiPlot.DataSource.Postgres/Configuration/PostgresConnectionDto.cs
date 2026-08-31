namespace SemiPlot.DataSource.Postgres.Configuration;

/// <summary>
/// Every member nullable so an absent field reaches the loader as a state to report; YAML keys follow
/// the underscored convention (<c>PollIntervalMs</c> reads <c>poll_interval_ms</c>).
/// </summary>
internal sealed class PostgresConnectionDto
{
	public string? Host { get; set; }

	public int? Port { get; set; }

	public string? Database { get; set; }

	public string? User { get; set; }

	public string? Password { get; set; }

	public string? SourceTimeZone { get; set; }

	public int? PollIntervalMs { get; set; }

	public string? Schema { get; set; }
}
