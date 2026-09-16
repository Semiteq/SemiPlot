using System.Reflection;

using SemiPlot.UI.Localization;

namespace SemiPlot.UI.MainWindow;

/// <summary>What the About dialog shows: the product, the build, and the configuration this run read.</summary>
public sealed record AboutInfo(string ApplicationName, string Version, string ConfigurationDirectory)
{
	/// <summary>Reads the arguments Program.Main parsed, so the dialog names the directory the run read.</summary>
	public static AboutInfo ForCurrentProcess()
	{
		return For([.. Environment.GetCommandLineArgs().Skip(1)]);
	}

	// Program.ReportStartupFailure opens this window with the menu when the argument parse itself failed,
	// so the dialog is reachable with no directory to name.
	internal static AboutInfo For(string[] arguments)
	{
		var options = StartupOptions.Parse(arguments);

		return new AboutInfo(
			Resources.WindowTitle,
			AssemblyVersion(),
			options.IsSuccess ? options.Value.ConfigDir : Resources.AboutConfigurationUnknown);
	}

	private static string AssemblyVersion()
	{
		var assembly = typeof(AboutInfo).Assembly;

		var informational = assembly
			.GetCustomAttribute<AssemblyInformationalVersionAttribute>()
			?.InformationalVersion;

		return informational?.Split('+')[0]
			?? assembly.GetName().Version?.ToString()
			?? string.Empty;
	}
}
