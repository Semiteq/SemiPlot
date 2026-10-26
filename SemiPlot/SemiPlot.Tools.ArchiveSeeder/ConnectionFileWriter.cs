namespace SemiPlot.Tools.ArchiveSeeder;

public static class ConnectionFileWriter
{
	public const string DirectoryName = "connection";

	public const string FileName = "connection.yaml";

	/// <summary>The file this writer targets under a configuration root.</summary>
	public static string PathFor(string configDirectory)
	{
		return Path.Combine(configDirectory, DirectoryName, FileName);
	}

	public static async Task WriteAsync(
		string configDirectory,
		string host,
		int port,
		string database,
		string user,
		string password,
		TimeSpan pollInterval,
		CancellationToken cancellationToken = default)
	{
		var path = PathFor(configDirectory);

		Directory.CreateDirectory(Path.Combine(configDirectory, DirectoryName));

		var content =
			$"""
			host: {host}
			port: {port}
			database: {database}
			user: {user}
			password: "{password}"
			poll_interval_ms: {(int)pollInterval.TotalMilliseconds}
			""" + Environment.NewLine;

		await File.WriteAllTextAsync(path, content, cancellationToken);
	}
}
