using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Startup;

/// <summary>The window a failed start shows: the failure, its own panel, and the buttons it offers.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Messages")]
[Trait("Category", "Unit")]
public sealed class StartupFailureWindowTests
{
	private static readonly ArchiveFailureView _failure = new(
		"No connection to the archive",
		"SemiPlot could not open a connection to 'semiplot' at scada-host:5432.",
		"Check that the PostgreSQL server is running.",
		MessageSeverity.Error);

	[AvaloniaFact]
	public void AFailedStartShowsTheFailureWindowWithItsOwnPanel()
	{
		using var scope = new AppStateScope();
		var failedStart = FailedStartup();

		App.ConfigureFailed(scope.App, settings: null, failedStart, options: null);

		var window = scope.App.CreateMainWindow();
		var viewModel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which;

		window.Should().BeOfType<StartupFailureWindow>();
		viewModel.Failure.Should().Be(ArchiveFailureMapper.Map(failedStart));
		viewModel.MessagePanel.Should().BeSameAs(
			App.ResolveMessagePanel(), "the ReactiveUI handler reaches the panel this window shows");

		var scheduler = new TestScheduler();
		var observer = new UnhandledErrorObserver(App.ResolveMessagePanel, scheduler, NullLogger.Instance);

		observer.OnNext(new InvalidOperationException("a command body threw"));
		scheduler.AdvanceBy(1);

		viewModel.MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Detail.Should().Contain("a command body threw");

		window.Show();
		Dispatcher.UIThread.RunJobs();
		window.Close();
		Dispatcher.UIThread.RunJobs();

		App.ResolveMessagePanel().Should().BeNull("the window's panel goes with the window");
		var subscribe = () => viewModel.ExitRequests.Subscribe(_ => { });

		subscribe.Should().Throw<ObjectDisposedException>("closing the window disposes its view model");
	}

	[AvaloniaFact]
	public void TheWindow_ShowsTheFailureAndHidesTheEmptyPanel()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };

		window.Show();
		Dispatcher.UIThread.RunJobs();

		ReadText(window, "StartupFailureTitle").Should().Be(_failure.Title);
		ReadText(window, "StartupFailureDetail").Should().Be(_failure.Detail);
		ReadText(window, "StartupFailureRemedy").Should().Be(_failure.Remedy);
		window.FindControl<Border>("MessagePanel")!.IsVisible.Should()
			.BeFalse("nothing has reported into this window's panel, so it carries no row");
		window.Close();
	}

	[AvaloniaFact]
	public void TheSettingsButton_IsHiddenWithoutAConfigurationDirectory()
	{
		using var viewModel = NewFailureViewModel(configDirectory: null);

		viewModel.OffersSettings.Should().BeFalse("a failed argument parse names no directory");
		ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("missing-directory")]
	[InlineData("no-files-in-a-section")]
	[InlineData("unreadable-file")]
	[InlineData("key-absent")]
	public void TheSettingsButton_IsHiddenWhenASectionCannotSave(string fault)
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			switch (fault)
			{
				case "missing-directory":
					Directory.Delete(configDirectory, recursive: true);
					break;
				case "no-files-in-a-section":
					File.Delete(Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml"));
					break;
				case "key-absent":
					var appWithoutLocale = Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml");
					File.WriteAllText(appWithoutLocale, "theme: light\n");
					break;
				default:
					var appFile = Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml");
					File.WriteAllText(appFile, "locale: [unclosed\n");
					break;
			}

			using var viewModel = NewFailureViewModel(configDirectory);

			viewModel.OffersSettings.Should().BeFalse();
			((ICommand)viewModel.ShowSettingsCommand).CanExecute(null).Should().BeFalse();
			ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeFalse();
		}
		finally
		{
			if (Directory.Exists(configDirectory))
			{
				Directory.Delete(configDirectory, recursive: true);
			}
		}
	}

	[AvaloniaFact]
	public void TheSettingsButton_IsShownOverAnEmptyPassword()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var viewModel = NewFailureViewModel(configDirectory);

			viewModel.OffersSettings.Should().BeTrue();
			ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeTrue();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public void TheExitButton_ClosesTheWindow()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Click(window, window.FindControl<Button>("StartupFailureExit")!);

		window.IsVisible.Should().BeFalse("ExitRequests reaches the window's Close");
	}

	[AvaloniaFact]
	public async Task TheAboutButton_OpensTheAboutDialogOverTheWindow()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Click(window, window.FindControl<Button>("StartupFailureAbout")!);
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<AboutDialog>().Any());

		window.OwnedWindows.OfType<AboutDialog>().Single().Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
	}

	[AvaloniaFact]
	public async Task TheSettingsButton_OpensTheDialogOverTheWindowAndTheWindowDisposesItsViewModel()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var viewModel = NewFailureViewModel(configDirectory);
			var window = new StartupFailureWindow { DataContext = viewModel };
			window.Show();
			Dispatcher.UIThread.RunJobs();

			HeadlessInput.Click(window, window.FindControl<Button>("StartupFailureSettings")!);
			await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());

			var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
			var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
			settings.Host.Should().Be("127.0.0.1");
			viewModel.MessagePanel.Entries.Should().BeEmpty();

			dialog.Close();
			Dispatcher.UIThread.RunJobs();

			// A disposed command stops following its inputs, and the shipped blank password left it disabled.
			settings.Password = "secret";
			((ICommand)settings.SaveCommand).CanExecute(null).Should()
				.BeFalse("the window disposes the view model its dialog showed");
			window.OwnedWindows.Should().BeEmpty();
			window.Close();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInTheWindowsOwnPanel()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);

		viewModel.ReportFailure(new InvalidOperationException("the dialog refused"));

		viewModel.MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Detail.Should().Contain("the dialog refused");
		viewModel.MessagePanel.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheSettingsCommand_WithoutAConfigurationDirectory_CannotExecute()
	{
		using var viewModel = NewFailureViewModel(configDirectory: null);

		((ICommand)viewModel.ShowSettingsCommand).CanExecute(null).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task TheSettingsDialog_OverAnEmptyPassword_WritesItIntoTheExistingConnectionFileAndRestarts()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			var started = 0;
			using var viewModel = NewFailureViewModel(configDirectory, _ => started++);
			var window = new StartupFailureWindow { DataContext = viewModel };
			window.Show();
			Dispatcher.UIThread.RunJobs();
			HeadlessInput.Click(window, window.FindControl<Button>("StartupFailureSettings")!);
			await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());
			var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
			var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
			var save = dialog.FindControl<Button>("SettingsSaveButton")!;

			save.IsEffectivelyEnabled.Should().BeFalse("the shipped password is empty");
			HeadlessInput.Type(dialog, dialog.FindControl<TextBox>("SettingsPassword")!, "secret");

			save.IsEffectivelyEnabled.Should().BeTrue(settings.ValidationMessage);
			HeadlessInput.Click(dialog, save);
			await HeadlessWait.Until(() => settings.IsRestartPending);

			var connectionDirectory = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName);
			PostgresConnectionLoader.Load(connectionDirectory).Value.Password.Should().Be("secret");
			Directory.GetFiles(connectionDirectory).Should().ContainSingle().Which.Should().EndWith("connection.yaml");
			viewModel.MessagePanel.Entries.Should().BeEmpty();

			HeadlessInput.Click(dialog, dialog.FindControl<Button>("SettingsRestartNow")!);
			Dispatcher.UIThread.RunJobs();

			started.Should().Be(1);
			window.IsVisible.Should().BeFalse("the restart closes the failure window");

			dialog.Close();
			Dispatcher.UIThread.RunJobs();
			window.Close();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task TheSettingsDialog_OverAnUnreadableSection_StatesTheSectionAndKeepsSaveDisabled()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			var messagePanel = new MessagePanelViewModel();
			var loggerFactory = NullLoggerFactory.Instance;
			var connectionFile = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml");
			File.WriteAllText(connectionFile, "host: [unclosed\n");

			using var settings = await SettingsViewModel.OpenAsync(
				configDirectory, messagePanel, loggerFactory, restartApplication: () => { });
			var dialog = new SettingsDialog { DataContext = settings };
			dialog.Show();
			Dispatcher.UIThread.RunJobs();

			dialog.FindControl<TextBlock>("SettingsValidationMessage")!.Text.Should()
				.Be(Resources.FormatSettingsSectionUnreadable(Resources.SettingsConnectionHeader));
			dialog.FindControl<Button>("SettingsSaveButton")!.IsEffectivelyEnabled.Should().BeFalse();
			dialog.Close();
			messagePanel.Dispose();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public void TheRestartButton_IsPresentOnlyWithLaunchOptions()
	{
		using var withoutOptions = NewFailureViewModel(configDirectory: null);
		using var withOptions = NewFailureViewModel(AppContext.BaseDirectory);

		ButtonIsVisible(withoutOptions, "StartupFailureRestart").Should().BeFalse();
		ButtonIsVisible(withOptions, "StartupFailureRestart").Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheRestartButton_ClosesTheWindowOnlyAfterTheCopyStarted()
	{
		var started = 0;
		var windowVisibleWhenTheCopyStarted = false;
		StartupFailureWindow? window = null;
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory, _ =>
		{
			started++;
			windowVisibleWhenTheCopyStarted = window!.IsVisible;
		});
		window = new StartupFailureWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Click(window, window.FindControl<Button>("StartupFailureRestart")!);

		started.Should().Be(1);
		windowVisibleWhenTheCopyStarted.Should().BeTrue("the window closes after the copy started");
		window.IsVisible.Should().BeFalse("ExitRequests reaches the window's Close");
	}

	[AvaloniaFact]
	public void AFailedRestart_ReportsInTheWindowsOwnPanelAndKeepsIt()
	{
		var exits = 0;
		using var viewModel = NewFailureViewModel(
			AppContext.BaseDirectory, _ => throw new InvalidOperationException("no such file"));
		using var subscription = viewModel.ExitRequests.Subscribe(_ => exits++);

		viewModel.RestartApplication();

		exits.Should().Be(0);
		viewModel.MessagePanel.Entries.Should().ContainSingle().Which.View.Severity.Should().Be(MessageSeverity.Error);
	}

	[AvaloniaFact]
	public void AFailedStartWithLaunchOptions_OffersRestartAndSettingsOverTheirDirectory()
	{
		var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		try
		{
			using var scope = new AppStateScope();

			App.ConfigureFailed(scope.App, settings: null, FailedStartup(), TestLaunch.OptionsAt(configDirectory));

			var window = scope.App.CreateMainWindow();
			var viewModel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which;
			viewModel.OffersRestart.Should().BeTrue();
			viewModel.OffersSettings.Should().BeTrue();
			window.Close();
			Dispatcher.UIThread.RunJobs();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	private static bool ButtonIsVisible(StartupFailureViewModel viewModel, string name)
	{
		var window = new StartupFailureWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		var visible = window.FindControl<Button>(name)!.IsVisible;
		window.Close();

		return visible;
	}

	private static StartupFailureViewModel NewFailureViewModel(
		string? configDirectory,
		Action<ProcessStartInfo>? start = null)
	{
		var launcher = configDirectory is null ? null : TestLaunch.LauncherAt(configDirectory, start);

		return new StartupFailureViewModel(_failure, launcher, NullLoggerFactory.Instance);
	}

	private static AppSettingsError FailedStartup()
	{
		return new AppSettingsError("app", AppSettingsProblem.KeyMissing, AppSettingsLoader.LocaleKey);
	}

	private static string? ReadText(Window window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}
}
