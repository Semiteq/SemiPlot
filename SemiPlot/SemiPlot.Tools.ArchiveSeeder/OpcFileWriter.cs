namespace SemiPlot.Tools.ArchiveSeeder;

public static class OpcFileWriter
{
	public const string DirectoryName = "opc";

	public const string FileName = "opc.yaml";

	/// <summary>The file this writer targets under a configuration root.</summary>
	public static string PathFor(string configDirectory)
	{
		return Path.Combine(configDirectory, DirectoryName, FileName);
	}

	public static async Task WriteAsync(
		string configDirectory,
		string host,
		int port,
		CancellationToken cancellationToken = default)
	{
		Directory.CreateDirectory(Path.Combine(configDirectory, DirectoryName));

		var content =
			$"""
			host: {host}
			port: {port}
			""" + Environment.NewLine;

		await File.WriteAllTextAsync(PathFor(configDirectory), content, cancellationToken);
	}
}
