using System.Reflection;

using Avalonia;
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

	// App.Configure writes the failure view into a private field on every call, and Application.Current
	// outlives this class; nothing on the production surface puts it back.
	private static readonly FieldInfo _startupFailureField =
		typeof(App).GetField("_startupFailure", BindingFlags.Instance | BindingFlags.NonPublic)
		?? throw new InvalidOperationException("App._startupFailure is gone, and the restore below with it.");

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
		var application = (App)Application.Current!;
		var previousResources = application.Resources.ToDictionary(entry => entry.Key, entry => entry.Value);
		var previousVariant = application.RequestedThemeVariant;
		var previousFailure = _startupFailureField.GetValue(application);
		try
		{
			App.Configure(application, settings, FailedStartup());

			application.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

			return value;
		}
		finally
		{
			Restore(application.Resources, previousResources);
			application.RequestedThemeVariant = previousVariant;
			_startupFailureField.SetValue(application, previousFailure);
		}
	}

	private static Result<StartupData> FailedStartup()
	{
		return Result.Fail<StartupData>(
			new AppSettingsError("ui/app.yaml", AppSettingsProblem.NotFound));
	}

	private static object? SemiCopyStringOf(UiLanguage language)
	{
		var probe = new Border();

		SemiTheme.OverrideLocaleResources(probe, App.SemiLocaleFor(language));
		probe.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

		return value;
	}

	private static void Restore(IResourceDictionary resources, Dictionary<object, object?> previous)
	{
		foreach (var key in resources.Keys.ToArray())
		{
			if (!previous.ContainsKey(key))
			{
				resources.Remove(key);
			}
		}

		foreach (var entry in previous)
		{
			resources[entry.Key] = entry.Value;
		}
	}
}
