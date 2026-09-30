using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using AwesomeAssertions;

using FluentResults;

using Semi.Avalonia;

using SemiPlot.UI;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

// What App.Run runs inside AfterSetup. Semi's own control strings are the observable half: the
// override writes them into Application.Resources, so reading one back says which locale reached it.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppConfigurationTests
{
	private const string CopyMenuKey = "STRING_MENU_COPY";

	[AvaloniaFact]
	public void ASettingsFailure_StillHandsSemiTheBootstrapLocale()
	{
		var semiStrings = ConfigureAndReadSemiStrings(settings: null);

		semiStrings.Should().Be(SemiCopyStringOf(StartupSequence.BootstrapLocale));
	}

	[AvaloniaFact]
	public void ConfiguredSettings_HandSemiTheirOwnLocale()
	{
		var semiStrings = ConfigureAndReadSemiStrings(new AppSettings(UiLanguage.En, AppThemeVariant.Light));

		semiStrings.Should().Be(SemiCopyStringOf(UiLanguage.En));
	}

	[AvaloniaFact]
	public void TheTwoLocales_GiveSemiDifferentStrings()
	{
		SemiCopyStringOf(UiLanguage.Ru).Should().NotBe(SemiCopyStringOf(UiLanguage.En));
	}

	private static object? ConfigureAndReadSemiStrings(AppSettings? settings)
	{
		using var scope = new AppStateScope();

		App.Configure(scope.App, settings, FailedStartup(), configDirectory: null);

		scope.App.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

		return value;
	}

	private static Result<StartupData> FailedStartup()
	{
		return Result.Fail<StartupData>(
			new AppSettingsError("app", AppSettingsProblem.KeyMissing, AppSettingsLoader.LocaleKey));
	}

	private static object? SemiCopyStringOf(UiLanguage language)
	{
		var probe = new Border();

		SemiTheme.OverrideLocaleResources(probe, App.SemiLocaleFor(language));
		probe.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

		return value;
	}
}
