using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Semi.Avalonia;

using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Startup;

// Semi's own control strings are the observable half: the
// override writes them into Application.Resources, so reading one back says which locale reached it.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class AppConfigurationTests
{
	private const string CopyMenuKey = "STRING_MENU_COPY";

	private static readonly TimeSpan _severalQuietPeriods = TimeSpan.FromSeconds(1);

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

	[AvaloniaFact]
	public async Task AThemeSavedOnDiskReachesTheRunningApplication()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			App.ConfigureFailed(scope.App, LightSettings(), FailedStartup(), TestLaunch.OptionsAt(configDirectory));
			scope.App.RequestedThemeVariant.Should().Be(ThemeVariant.Light);

			ReplaceAppFile(configDirectory, "theme: light", "theme: dark");

			await HeadlessWait.Until(() => scope.App.RequestedThemeVariant == ThemeVariant.Dark);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task AThemeSavedOnDiskReachesAStartedApplication()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			using var archive = NewArchiveStand();
			App.ConfigureStarted(scope.App, LightSettings(), archive.Data, TestLaunch.OptionsAt(configDirectory));
			var window = Show(scope.App);

			ReplaceAppFile(configDirectory, "theme: light", "theme: dark");

			await HeadlessWait.Until(() => scope.App.RequestedThemeVariant == ThemeVariant.Dark);
			Close(window);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task AnAppFolderThatCannotBeListed_StillStartsAndReachesThePanel()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			using var archive = NewArchiveStand();
			Window window;

			using (DirectoryAccessDenial.OfListing(AppDirectoryOf(configDirectory)))
			{
				var refusal = RefusalOf(AppDirectoryOf(configDirectory));
				App.ConfigureStarted(scope.App, LightSettings(), archive.Data, TestLaunch.OptionsAt(configDirectory));
				window = Show(scope.App);
				var panel = App.ResolveMessagePanel()!;

				await HeadlessWait.Until(() => panel.Entries.Count > 0);

				panel.Entries.Select(entry => entry.View).Should().Contain(
					ArchiveFailureMapper.Map(refusal), "the start's load may add the section's own failure beside it");
			}

			Close(window);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task AnAppFolderThatCannotBeListed_ReachesTheFailureWindowsPanel()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			Window window;

			using (DirectoryAccessDenial.OfListing(AppDirectoryOf(configDirectory)))
			{
				var refusal = RefusalOf(AppDirectoryOf(configDirectory));
				App.ConfigureFailed(scope.App, LightSettings(), FailedStartup(), TestLaunch.OptionsAt(configDirectory));
				window = scope.App.CreateMainWindow();
				var panel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which.MessagePanel;

				await HeadlessWait.Until(() => panel.Entries.Count > 0);

				panel.Entries.Select(entry => entry.View).Should().Contain(
					ArchiveFailureMapper.Map(refusal), "the start's load may add the section's own failure beside it");
			}

			Close(window);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public void AStartWithoutAnAppFolder_ReportsNothingMoreAboutIt()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-no-app-folder-").FullName;
		try
		{
			using var scope = new AppStateScope();
			using var archive = NewArchiveStand();

			App.ConfigureStarted(scope.App, LightSettings(), archive.Data, TestLaunch.OptionsAt(configDirectory));
			var window = Show(scope.App);

			App.ResolveMessagePanel()!.Entries.Should().BeEmpty("the start has already reported the missing folder");
			Close(window);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task ABrokenAppFileOnDiskReachesTheShownWindowsPanel()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			App.ConfigureFailed(scope.App, LightSettings(), FailedStartup(), TestLaunch.OptionsAt(configDirectory));
			var window = scope.App.CreateMainWindow();
			var panel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which.MessagePanel;

			ReplaceAppFile(configDirectory, "theme: light", "theme: [unclosed");

			await HeadlessWait.Until(() => panel.Entries.Count > 0);
			var expected = ArchiveFailureMapper.Map(AppSettingsLoader.Load(AppDirectoryOf(configDirectory)).Errors[0]);
			panel.Entries.Should().ContainSingle().Which.View.Should().Be(expected);
			scope.App.RequestedThemeVariant.Should().Be(ThemeVariant.Light);
			Close(window);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task AFailedReloadWithNoWindowOpen_LeavesTheWatchRunning()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();
			App.ConfigureFailed(scope.App, LightSettings(), FailedStartup(), TestLaunch.OptionsAt(configDirectory));
			App.ResolveMessagePanel().Should().BeNull("no window has been created");

			ReplaceAppFile(configDirectory, "theme: light", "theme: [unclosed");
			await PumpFor(_severalQuietPeriods);
			ReplaceAppFile(configDirectory, "theme: [unclosed", "theme: dark");

			await HeadlessWait.Until(() => scope.App.RequestedThemeVariant == ThemeVariant.Dark);
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	private static AppSettings LightSettings()
	{
		return new AppSettings(UiLanguage.Ru, AppThemeVariant.Light);
	}

	private static string AppDirectoryOf(string configDirectory)
	{
		return Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName);
	}

	private static IError RefusalOf(string sectionDirectory)
	{
		var opened = AppSectionWatcher.Open(sectionDirectory);
		opened.ValueOrDefault?.Dispose();

		opened.IsFailed.Should().BeTrue("the denial refuses the listing, unless the account bypasses permissions");

		return opened.Errors[0];
	}

	/// <summary>Stages the new text beside the folder and replaces the file with it, as a settings save does.</summary>
	private static void ReplaceAppFile(string configDirectory, string oldText, string newText)
	{
		var target = Path.Combine(AppDirectoryOf(configDirectory), "app.yaml");
		var staged = Path.Combine(configDirectory, "app.yaml.staged");

		File.WriteAllText(staged, File.ReadAllText(target).Replace(oldText, newText));
		File.Replace(staged, target, destinationBackupFileName: null);
	}

	private static async Task PumpFor(TimeSpan duration)
	{
		var end = DateTime.UtcNow + duration;

		while (DateTime.UtcNow < end)
		{
			await Task.Delay(10);
			Dispatcher.UIThread.RunJobs();
		}
	}

	private static Window Show(App app)
	{
		var window = app.CreateMainWindow();
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return window;
	}

	private static void Close(Window window)
	{
		window.Close();
		Dispatcher.UIThread.RunJobs();
	}

	private static object? ConfigureAndReadSemiStrings(AppSettings? settings)
	{
		using var scope = new AppStateScope();

		App.ConfigureFailed(scope.App, settings, FailedStartup(), options: null);

		scope.App.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

		return value;
	}

	private static AppSettingsError FailedStartup()
	{
		return new AppSettingsError("app", AppSettingsProblem.KeyMissing, AppSettingsLoader.LocaleKey);
	}

	private static object? SemiCopyStringOf(UiLanguage language)
	{
		var probe = new Border();

		SemiTheme.OverrideLocaleResources(probe, App.SemiLocaleFor(language));
		probe.Resources.TryGetResource(CopyMenuKey, ThemeVariant.Light, out var value);

		return value;
	}
}
