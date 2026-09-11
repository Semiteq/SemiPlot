using System.Globalization;

using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>What the startup steps produce: the settings, null when their own load failed, and the probe.</summary>
internal sealed record StartupOutcome(AppSettings? Settings, Result<StartupData> Startup);

/// <summary>docs/architecture/data-integration.md#startup</summary>
internal static class StartupSequence
{
	internal const string SettingsDirectoryName = "ui";

	internal const string SettingsFileName = "app.yaml";

	/// <summary>The language a failure reporting the settings file itself is read in.</summary>
	internal const UiLanguage BootstrapLocale = UiLanguage.Ru;

	private static readonly CultureInfo _bootstrapCulture = CultureFor(BootstrapLocale);

	internal static string SettingsPath(string configDirectory)
	{
		return Path.Combine(configDirectory, SettingsDirectoryName, SettingsFileName);
	}

	internal static StartupOutcome Run(StartupOptions options)
	{
		ApplyCulture(_bootstrapCulture);

		var settings = AppSettingsLoader.Load(SettingsPath(options.ConfigDir));

		if (settings.IsFailed)
		{
			return new StartupOutcome(null, Result.Fail<StartupData>(settings.Errors));
		}

		ApplyCulture(CultureFor(settings.Value.Locale));

		return new StartupOutcome(settings.Value, StartupProbe.Run(options));
	}

	internal static CultureInfo CultureFor(UiLanguage locale)
	{
		return SettingsVocabulary.Of(locale).UiCulture;
	}

	// The single writer of the interface language. docs/architecture/ui-text.md#two-sets-one-selector
	private static void ApplyCulture(CultureInfo culture)
	{
		CultureInfo.DefaultThreadCurrentUICulture = culture;
	}
}
