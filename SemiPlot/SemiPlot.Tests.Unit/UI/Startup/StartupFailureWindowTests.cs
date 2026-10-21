using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.DataSource.OpcUa.Configuration;
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
		var failedStart = TestLaunch.FailedStartup();

		App.ConfigureFailed(scope.App, settings: null, failedStart, options: null);

		var window = scope.App.CreateMainWindow();
		var viewModel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which;

		window.Should().BeOfType<StartupFailureWindow>();
		viewModel.Failure.Should().Be(ArchiveFailureMapper.Map(failedStart));
		viewModel.MessagePanel.Should().BeSameAs(
			App.ResolveMessagePanel(), "the ReactiveUI handler reaches the panel this window shows");

		ViewProbe.Realise(window);
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

		ViewProbe.Realise(window);

		ReadText(window, "StartupFailureTitle").Should().Be(_failure.Title);
		ReadText(window, "StartupFailureDetail").Should().Be(_failure.Detail);
		ReadText(window, "StartupFailureRemedy").Should().Be(_failure.Remedy);
		ViewProbe.Named<Border>(window, "MessagePanel").IsVisible.Should()
			.BeFalse("nothing has reported into this window's panel, so it carries no row");
		window.Close();
	}

	[AvaloniaFact]
	public void TheSettingsButton_IsHiddenWithoutAConfigurationDirectory()
	{
		using var viewModel = NewFailureViewModel(configDirectory: null);

		viewModel.OffersSettings.Should().BeFalse("a failed argument parse names no directory");
		((ICommand)viewModel.ShowSettingsCommand).CanExecute(null).Should().BeFalse();
		ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeFalse();
	}

	[AvaloniaTheory]
	[InlineData("missing-directory")]
	[InlineData("unreadable-file")]
	[InlineData("key-absent")]
	[InlineData("no-files-in-a-section")]
	[InlineData("opc-missing-directory")]
	[InlineData("opc-no-files")]
	[InlineData("opc-unreadable-file")]
	[InlineData("opc-key-absent")]
	public void TheSettingsButton_IsHiddenWhenASectionCannotSave(string fault)
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		var appFile = Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml");
		var opcDirectory = Path.Combine(configDirectory, StartupProbe.OpcDirectoryName);
		switch (fault)
		{
			case "missing-directory":
				Directory.Delete(configDirectory, recursive: true);
				break;
			case "unreadable-file":
				File.WriteAllText(appFile, "locale: [unclosed\n");
				break;
			case "key-absent":
				File.WriteAllText(appFile, "theme: light\n");
				break;
			case "no-files-in-a-section":
				File.Delete(Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml"));
				break;
			case "opc-missing-directory":
				Directory.Delete(opcDirectory, recursive: true);
				break;
			case "opc-no-files":
				File.Delete(Path.Combine(opcDirectory, "opc.yaml"));
				break;
			case "opc-unreadable-file":
				File.WriteAllText(Path.Combine(opcDirectory, "opc.yaml"), "host: [unclosed\n");
				break;
			case "opc-key-absent":
				File.WriteAllText(Path.Combine(opcDirectory, "opc.yaml"), "host: 127.0.0.1\n");
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(fault), fault, null);
		}

		using var viewModel = NewFailureViewModel(configDirectory);

		viewModel.OffersSettings.Should().BeFalse();
		((ICommand)viewModel.ShowSettingsCommand).CanExecute(null).Should().BeFalse();
		ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheSettingsButton_IsShownOverAnEmptyPassword()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var viewModel = NewFailureViewModel(configDirectory);

		viewModel.OffersSettings.Should().BeTrue();
		ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheSettingsButton_IsShownOverAnOpcValueTheLoaderRefuses()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		var opcFile = Path.Combine(configDirectory, StartupProbe.OpcDirectoryName, "opc.yaml");
		File.WriteAllText(opcFile, "host: 127.0.0.1\nport: 70000\n");

		using var viewModel = NewFailureViewModel(configDirectory);

		viewModel.OffersSettings.Should().BeTrue("the dialog opens the refused port as an invalid field");
		ButtonIsVisible(viewModel, "StartupFailureSettings").Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheExitButton_ClosesTheWindow()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);

		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureExit"));

		window.IsVisible.Should().BeFalse("ExitRequests reaches the window's Close");
	}

	[AvaloniaFact]
	public async Task TheAboutButton_OpensTheAboutDialogOverTheWindow()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);

		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureAbout"));
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<AboutDialog>().Any());

		window.OwnedWindows.OfType<AboutDialog>().Single().Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
	}

	[AvaloniaFact]
	public async Task TheSettingsButton_OpensTheDialogOverTheWindowAndTheWindowDisposesItsViewModel()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var viewModel = NewFailureViewModel(configDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);

		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureSettings"));
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

	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInTheWindowsOwnPanel()
	{
		using var viewModel = NewFailureViewModel(AppContext.BaseDirectory);

		var thrown = new InvalidOperationException("the dialog refused");

		viewModel.ReportFailure(thrown);

		viewModel.MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));
		viewModel.MessagePanel.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task TheSettingsDialog_OverAnEmptyPassword_WritesItIntoTheExistingConnectionFileAndRestarts()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		var started = 0;
		using var viewModel = NewFailureViewModel(configDirectory, _ => started++);
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);
		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureSettings"));
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());
		var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		var save = ViewProbe.Named<Button>(dialog, "SettingsSaveButton");

		save.IsEffectivelyEnabled.Should().BeFalse("the shipped password is empty");
		HeadlessInput.Type(dialog, ViewProbe.Named<TextBox>(dialog, "SettingsPassword"), "secret");

		save.IsEffectivelyEnabled.Should().BeTrue(settings.ValidationMessage);
		HeadlessInput.Click(dialog, save);
		var restartNow = ViewProbe.Named<Button>(dialog, "SettingsRestartNow");
		await HeadlessWait.Until(() => restartNow.IsEffectivelyEnabled);

		var connectionDirectory = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName);
		PostgresConnectionLoader.Load(connectionDirectory).Value.Password.Should().Be("secret");
		Directory.GetFiles(connectionDirectory).Should().ContainSingle().Which.Should().EndWith("connection.yaml");
		viewModel.MessagePanel.Entries.Should().BeEmpty();

		HeadlessInput.Click(dialog, restartNow);
		Dispatcher.UIThread.RunJobs();

		started.Should().Be(1);
		window.IsVisible.Should().BeFalse("the restart closes the failure window");

		dialog.Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
	}

	[AvaloniaFact]
	public async Task TheSettingsDialog_OverARefusedOpcPort_WritesTheFixedPortIntoTheOpcFile()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		ShippedConfiguration.FillPassword(configDirectory, "secret");
		var opcDirectory = Path.Combine(configDirectory, StartupProbe.OpcDirectoryName);
		File.WriteAllText(Path.Combine(opcDirectory, "opc.yaml"), "host: 127.0.0.1\nport: 70000\n");
		var connectionFile = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName, "connection.yaml");
		var appFile = Path.Combine(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml");
		var connectionBefore = File.ReadAllBytes(connectionFile);
		var appBefore = File.ReadAllBytes(appFile);
		using var viewModel = NewFailureViewModel(configDirectory);
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);
		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureSettings"));
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());
		var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		var save = ViewProbe.Named<Button>(dialog, "SettingsSaveButton");
		var opcPort = ViewProbe.Named<NumericUpDown>(dialog, "SettingsOpcPort");
		var opcPortText = ViewProbe.Descendant<TextBox>(opcPort, "PART_TextBox");
		var message = ViewProbe.Named<TextBlock>(dialog, "SettingsValidationMessage");

		opcPort.Value.Should().BeNull("the loader refuses port 70000");
		opcPortText.Text.Should().BeNullOrEmpty();
		opcPort.Classes.Should().Contain(ViewProbe.InvalidClass);
		message.Text.Should().Be(Resources.SettingsOpcPortInvalid);
		save.IsEffectivelyEnabled.Should().BeFalse();
		HeadlessInput.Type(dialog, opcPortText, "49875");

		opcPort.Classes.Should().NotContain(ViewProbe.InvalidClass);
		message.Text.Should().BeEmpty();
		save.IsEffectivelyEnabled.Should().BeTrue(settings.ValidationMessage);
		HeadlessInput.Click(dialog, save);
		await HeadlessWait.Until(() => settings.IsRestartPending);

		OpcUaConnectionLoader.Load(opcDirectory).Value.Port.Should().Be(49875);
		File.ReadAllBytes(connectionFile).Should().Equal(connectionBefore);
		File.ReadAllBytes(appFile).Should().Equal(appBefore);
		viewModel.MessagePanel.Entries.Should().BeEmpty();

		dialog.Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
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
		ViewProbe.Realise(window);

		HeadlessInput.Click(window, ViewProbe.Named<Button>(window, "StartupFailureRestart"));

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
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var scope = new AppStateScope();

		App.ConfigureFailed(
			scope.App, settings: null, TestLaunch.FailedStartup(), TestLaunch.OptionsAt(configDirectory));

		var window = scope.App.CreateMainWindow();
		var viewModel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which;
		viewModel.OffersRestart.Should().BeTrue();
		viewModel.OffersSettings.Should().BeTrue();
		window.Close();
		Dispatcher.UIThread.RunJobs();
	}

	private static bool ButtonIsVisible(StartupFailureViewModel viewModel, string name)
	{
		var window = new StartupFailureWindow { DataContext = viewModel };
		ViewProbe.Realise(window);

		var visible = ViewProbe.Named<Button>(window, name).IsVisible;
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

	private static string? ReadText(Window window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}
}
