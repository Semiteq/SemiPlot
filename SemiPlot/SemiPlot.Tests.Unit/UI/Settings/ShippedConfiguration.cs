using SemiPlot.Tools.ArchiveSeeder;
using SemiPlot.UI.Startup;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>Copies of the tracked <c>ConfigFiles/</c> set, which a test edits in place of the set itself.</summary>
internal static class ShippedConfiguration
{
	public static string Root { get; } = Path.Combine(AppContext.BaseDirectory, "ConfigFiles");

	public static string SectionDirectory(string section)
	{
		return Path.Combine(Root, section);
	}

	public static string ConnectionBody()
	{
		return File.ReadAllText(ConnectionFileWriter.PathFor(Root));
	}

	public static void CopyTo(string configDirectory, string? password = null)
	{
		foreach (var file in Directory.GetFiles(Root, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(configDirectory, Path.GetRelativePath(Root, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, overwrite: true);
		}

		if (password is not null)
		{
			FillPassword(configDirectory, password);
		}
	}

	public static TemporaryDirectory CopyToTemporaryDirectory()
	{
		var configDirectory = new TemporaryDirectory("semiplot-shipped-configuration");
		CopyTo(configDirectory);

		return configDirectory;
	}

	public static void FillPassword(string configDirectory, string password)
	{
		var file = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml");

		File.WriteAllText(file, File.ReadAllText(file).Replace("password: \"\"", $"password: {password}"));
	}
}
