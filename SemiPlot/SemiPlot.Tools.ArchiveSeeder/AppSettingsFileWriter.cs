namespace SemiPlot.Tools.ArchiveSeeder;

// Writes the YAML AppSettingsLoader reads: docs/architecture/bench.md#the-converge-verb.
public static class AppSettingsFileWriter
{
	public const string DirectoryName = "ui";

	public const string FileName = "app.yaml";

	public static async Task WriteAsync(string configDirectory, CancellationToken cancellationToken = default)
	{
		var directory = Path.Combine(configDirectory, DirectoryName);

		Directory.CreateDirectory(directory);

		var content =
			"""
			locale: ru
			theme: light
			""" + Environment.NewLine;

		await File.WriteAllTextAsync(Path.Combine(directory, FileName), content, cancellationToken);
	}
}
