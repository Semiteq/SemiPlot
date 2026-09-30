using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI;
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

		App.Configure(scope.App, settings: null, failedStart, configDirectory: null);

		var window = scope.App.CreateMainWindow();
		var viewModel = window.DataContext.Should().BeOfType<StartupFailureViewModel>().Which;

		window.Should().BeOfType<StartupFailureWindow>();
		viewModel.Failure.Should().Be(ArchiveFailureMapper.Map(failedStart.Errors[0]));
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
	public void TheSettingsButton_IsPresentOnlyWithAConfigurationDirectory()
	{
		using var withDirectory = NewFailureViewModel(AppContext.BaseDirectory);
		using var withoutDirectory = NewFailureViewModel(configDirectory: null);

		SettingsButtonIsVisible(withDirectory).Should().BeTrue();
		SettingsButtonIsVisible(withoutDirectory).Should().BeFalse("a failed argument parse names no directory");
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
	public async Task TheSettingsCommand_OverAMissingDirectory_ReportsBothSectionsAndStillOpens()
	{
		var missingDirectory = Path.Combine(Path.GetTempPath(), "semiplot-missing-" + Guid.NewGuid().ToString("N"));
		using var viewModel = NewFailureViewModel(missingDirectory);
		var requests = new List<SettingsViewModel>();
		using var subscription = viewModel.SettingsRequests.Subscribe(requests.Add);

		await viewModel.ShowSettingsCommand.Execute();

		viewModel.MessagePanel.Entries.Should().HaveCount(2, "each unreadable section reports into the window's panel");
		requests.Should().ContainSingle().Which.Dispose();
	}

	private static bool SettingsButtonIsVisible(StartupFailureViewModel viewModel)
	{
		var window = new StartupFailureWindow { DataContext = viewModel };
		window.Show();
		Dispatcher.UIThread.RunJobs();

		var visible = window.FindControl<Button>("StartupFailureSettings")!.IsVisible;
		window.Close();

		return visible;
	}

	private static StartupFailureViewModel NewFailureViewModel(string? configDirectory)
	{
		return new StartupFailureViewModel(
			_failure, configDirectory, NullLoggerFactory.Instance);
	}

	private static Result<StartupData> FailedStartup()
	{
		return Result.Fail<StartupData>(
			new AppSettingsError("app", AppSettingsProblem.KeyMissing, AppSettingsLoader.LocaleKey));
	}

	private static string? ReadText(Window window, string name)
	{
		return window.FindControl<TextBlock>(name)?.Text;
	}
}
