namespace SemiPlot.Tools.ArchiveSeeder;

// Writes the YAML PostgresConnectionLoader reads.
public static class ConnectionFileWriter
{
	public const string FileName = "archive-connection.yaml";

	public static async Task WriteAsync(
		string configDirectory,
		string host,
		int port,
		string database,
		string user,
		string password,
		string sourceTimeZoneId,
		TimeSpan pollInterval,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(configDirectory);

		var content =
			$"""
			host: {host}
			port: {port}
			database: {database}
			user: {user}
			password: "{password}"
			source_time_zone: {sourceTimeZoneId}
			poll_interval_ms: {(int)pollInterval.TotalMilliseconds}
			""" + Environment.NewLine;

		await File.WriteAllTextAsync(Path.Combine(configDirectory, FileName), content, cancellationToken);
	}
}
