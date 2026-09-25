using SemiPlot.UI.Startup;

namespace SemiPlot.Tests.Unit.UI.Settings;

/// <summary>Copies of the tracked <c>ConfigFiles/</c> set, which a test edits in place of the set itself.</summary>
internal static class ShippedConfiguration
{
	private static readonly string _root = Path.Combine(AppContext.BaseDirectory, "ConfigFiles");

	public static void CopyTo(string configDirectory, string? password = null)
	{
		foreach (var file in Directory.GetFiles(_root, "*", SearchOption.AllDirectories))
		{
			var target = Path.Combine(configDirectory, Path.GetRelativePath(_root, file));
			Directory.CreateDirectory(Path.GetDirectoryName(target)!);
			File.Copy(file, target, overwrite: true);
		}

		if (password is not null)
		{
			FillPassword(configDirectory, password);
		}
	}

	public static void FillPassword(string configDirectory, string password)
	{
		var file = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml");

		File.WriteAllText(file, File.ReadAllText(file).Replace("password: \"\"", $"password: {password}"));
	}
}
