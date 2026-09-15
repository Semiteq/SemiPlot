using System.Globalization;

using FluentResults;

namespace SemiPlot.UI.Startup;

/// <summary>What the startup steps produce: the settings, null when their own load failed, and the probe.</summary>
internal sealed record StartupOutcome(AppSettings? Settings, Result<StartupData> Startup);

/// <summary>docs/architecture/data-integration.md#startup</summary>
internal static class StartupSequence
{
	internal const string SettingsDirectoryName = "app";

	/// <summary>The language a failure reporting the settings section itself is read in.</summary>
	internal const UiLanguage BootstrapLocale = UiLanguage.Ru;

	private static readonly CultureInfo _bootstrapCulture = CultureFor(BootstrapLocale);

	internal static StartupOutcome Run(StartupOptions options)
	{
		ApplyBootstrapCulture();

		var settings = AppSettingsLoader.Load(Path.Combine(options.ConfigDir, SettingsDirectoryName));

		if (settings.IsFailed)
		{
			return new StartupOutcome(null, Result.Fail<StartupData>(settings.Errors));
		}

		ApplyCulture(CultureFor(settings.Value.Locale));

		return new StartupOutcome(settings.Value, StartupProbe.Run(options));
	}

	internal static void ApplyBootstrapCulture()
	{
		ApplyCulture(_bootstrapCulture);
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
