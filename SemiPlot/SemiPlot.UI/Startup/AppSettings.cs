using System.Globalization;

using Avalonia.Styling;

namespace SemiPlot.UI.Startup;

/// <summary>The interface language the <c>locale</c> key selects.</summary>
public enum UiLanguage
{
	Ru,
	En
}

/// <summary>The theme variant the <c>theme</c> key selects.</summary>
public enum AppThemeVariant
{
	Light,
	Dark
}

/// <summary>What one language means to the file, to the resource sets and to Semi's own control strings.</summary>
internal sealed record UiLanguageEntry(
	UiLanguage Language,
	string YamlToken,
	CultureInfo UiCulture,
	CultureInfo SemiCulture);

/// <summary>What one theme means to the file and to Avalonia.</summary>
internal sealed record AppThemeEntry(AppThemeVariant Theme, string YamlToken, ThemeVariant Variant);

/// <summary>docs/architecture/ui-theme.md#how-the-variant-reaches-the-application</summary>
internal static class SettingsVocabulary
{
	// Semi keys its own control strings by specific culture and falls through to zh-CN for a neutral one.
	internal static readonly UiLanguageEntry[] Languages =
	[
		new(UiLanguage.Ru, "ru", CultureInfo.GetCultureInfo("ru"), CultureInfo.GetCultureInfo("ru-RU")),
		new(UiLanguage.En, "en", CultureInfo.GetCultureInfo("en"), CultureInfo.GetCultureInfo("en-US"))
	];

	internal static readonly AppThemeEntry[] Themes =
	[
		new(AppThemeVariant.Light, "light", ThemeVariant.Light),
		new(AppThemeVariant.Dark, "dark", ThemeVariant.Dark)
	];

	internal static UiLanguageEntry Of(UiLanguage language)
	{
		return Array.Find(Languages, entry => entry.Language == language)
			?? throw new ArgumentOutOfRangeException(nameof(language), language, null);
	}

	internal static AppThemeEntry Of(AppThemeVariant theme)
	{
		return Array.Find(Themes, entry => entry.Theme == theme)
			?? throw new ArgumentOutOfRangeException(nameof(theme), theme, null);
	}
}

/// <summary>
/// The interface settings, read from the required configuration file. There is no default instance:
/// every value comes from the file, and a value the file does not carry is a startup failure.
/// </summary>
public sealed record AppSettings(UiLanguage Locale, AppThemeVariant Theme);
