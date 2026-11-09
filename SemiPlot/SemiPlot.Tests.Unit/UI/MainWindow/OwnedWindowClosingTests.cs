using System.Diagnostics;
using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tests.Unit.UI.Export;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI.Export;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>
/// An owned window's own work holds every close of it, its owner's included, and a restart starts once.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class OwnedWindowClosingTests
{
	public enum CloseRoute
	{
		Close,
		Exit,
		Restart
	}

	[AvaloniaTheory]
	[InlineData(CloseRoute.Close)]
	[InlineData(CloseRoute.Exit)]
	[InlineData(CloseRoute.Restart)]
	public async Task ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow(CloseRoute route)
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		var started = new List<ProcessStartInfo>();
		var reader = HeldCountReader();
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(
			[new Pen(1, "Pen 1", [], "#ff0000")],
			penCatalogueEditor: penCatalogueEditor,
			configDirectory: configDirectory,
			start: started.Add,
			rowReader: reader);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ShowSettingsCommand.Execute();
		await stand.ViewModel.ExportCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.Count == 2);
		Dispatcher.UIThread.RunJobs();
		stand.Scheduler.AdvanceBy(CsvExportViewModel.CountThrottle.Ticks);
		await HeadlessWait.Until(() => reader.CountCalls.Count == 1, "the export's count to start");
		var settings = window.OwnedWindows.OfType<SettingsDialog>().Single().DataContext.Should()
			.BeOfType<SettingsViewModel>().Which;
		var editor = await OpenTheEditorAndRetypeTheFirstName(stand, window);
		var penEditor = editor.DataContext.Should().BeOfType<PenEditorViewModel>().Which;
		settings.Password = "";
		penEditor.Groups.NewGroupName = "";
		var write = penCatalogueEditor.HoldNextCall();

		switch (route)
		{
			case CloseRoute.Close:
				window.Close();
				break;
			case CloseRoute.Exit:
				stand.ViewModel.ExitCommand.Execute().Subscribe();
				break;
			case CloseRoute.Restart:
				stand.ViewModel.RestartApplication();
				break;
			default:
				throw new ArgumentOutOfRangeException(nameof(route), route, null);
		}

		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the editor holds the close while its write is in flight");
		window.OwnedWindows.Should().HaveCount(3).And.OnlyContain(owned => owned.IsVisible);
		penCatalogueEditor.Changes.Should()
			.Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon2")));

		if (route != CloseRoute.Close)
		{
			stand.ViewModel.RestartApplication();
			Dispatcher.UIThread.RunJobs();
			started.Should().HaveCount(
				route == CloseRoute.Restart ? 1 : 0, "a restart during a held exit starts no further copy");
		}

		write.SetResult();
		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the write");

		window.OwnedWindows.Should().BeEmpty();
		penCatalogueEditor.Changes.Should().ContainSingle("the held write is not repeated");
		ShouldBeDisposed(settings);
		ShouldBeDisposed(penEditor);
		reader.CountCalls.Single().Token.IsCancellationRequested.Should()
			.BeTrue("the export's view model is disposed");
		started.Should().HaveCount(route == CloseRoute.Restart ? 1 : 0);
	}

	[AvaloniaFact]
	public async Task ClosingTheMainWindow_WhileTheEditorDrainsItsOwnClose_StillClosesTheMainWindow()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var editor = await OpenTheEditorAndRetypeTheFirstName(stand, window);
		var write = penCatalogueEditor.HoldNextCall();

		editor.Close();
		Dispatcher.UIThread.RunJobs();
		window.Close();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the editor's drain holds the main window's close too");
		editor.IsVisible.Should().BeTrue();

		write.SetResult();
		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the drain");

		window.OwnedWindows.Should().BeEmpty();
		penCatalogueEditor.Changes.Should()
			.Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon2")));
	}

	[AvaloniaFact]
	public async Task ClosingTheMainWindow_WhileAWriteRunsWithNoEditOpen_WaitsForTheWrite()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var editor = await OpenTheEditorAndRetypeTheFirstName(stand, window);
		var write = penCatalogueEditor.HoldNextCall();
		RaisePlatformLostFocus(editor);
		penCatalogueEditor.Changes.Should()
			.Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon2")));

		window.Close();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the editor's write in flight holds the close with no edit left open");

		write.SetResult();
		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the write");

		window.OwnedWindows.Should().BeEmpty();
		penCatalogueEditor.Changes.Should().ContainSingle("the write is not repeated");
	}

	[AvaloniaFact]
	public async Task AWriteRefusedDuringTheDrain_StillClosesTheMainWindow()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Ok(ViewCatalogue),
			ChangeResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.Unreachable, "semiplot_tags"))
		};
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await OpenTheEditorAndRetypeTheFirstName(stand, window);
		var write = penCatalogueEditor.HoldNextCall();

		window.Close();
		Dispatcher.UIThread.RunJobs();
		window.IsVisible.Should().BeTrue("the editor holds the close while its write is in flight");

		write.SetResult();
		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the refused write");

		window.OwnedWindows.Should().BeEmpty();
		penCatalogueEditor.Changes.Should()
			.Equal(new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon2")));
	}

	[AvaloniaFact]
	public async Task ClosingTheMainWindow_DuringASave_ClosesItOnlyAfterTheSaveEnds()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		settings.Password = "secret";
		bool? restartPendingAtClose = null;
		dialog.Closing += (_, _) => restartPendingAtClose = settings.IsRestartPending;

		settings.SaveCommand.Execute().Subscribe();
		window.Close();

		settings.IsRestartPending.Should().BeFalse("the save's continuation waits for the UI dispatcher");
		window.IsVisible.Should().BeTrue("the settings window holds the close while its save runs");

		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the save");

		restartPendingAtClose.Should().BeTrue("the settings window closes only after the save ended");
		var connectionDirectory = Path.Combine(configDirectory, StartupProbe.ConnectionDirectoryName);
		PostgresConnectionLoader.Load(connectionDirectory).Value.Password.Should().Be("secret");
	}

	[AvaloniaFact]
	public async Task ClosingTheSettingsWindow_DuringASave_ClosesItOnlyAfterTheSaveEnds()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		settings.Password = "secret";
		bool? restartPendingAtClose = null;
		dialog.Closing += (_, _) => restartPendingAtClose = settings.IsRestartPending;

		settings.SaveCommand.Execute().Subscribe();
		dialog.Close();

		dialog.IsVisible.Should().BeTrue("the settings window holds its own close while its save runs");

		await HeadlessWait.Until(() => !dialog.IsVisible, "the settings window to close after the save");

		restartPendingAtClose.Should().BeTrue("the settings window closes only after the save ended");
		window.IsVisible.Should().BeTrue("the settings window's own close leaves the main window open");
		window.Close();
	}

	[AvaloniaFact]
	public async Task ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile()
	{
		using var directory = new TemporaryDirectory("owned-windows-export");
		var partialPath = Path.Combine(directory.Path, "out.csv.partial");
		var seedToken = new TaskCompletionSource<CancellationToken>(
			TaskCreationOptions.RunContinuationsAsynchronously);
		var reader = new FakeArchiveRowReader
		{
			OnSeeds = async call =>
			{
				seedToken.TrySetResult(call.Token);
				await Task.Delay(Timeout.Infinite, call.Token);

				return Result.Ok<IReadOnlyList<StoredArchiveRow>>([]);
			}
		};
		using var stand = NewWindowStand([new Pen(1, "Pen 1", [], "#ff0000")], rowReader: reader);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ExportCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<CsvExportWindow>().Any());
		var exportWindow = window.OwnedWindows.OfType<CsvExportWindow>().Single();
		var export = exportWindow.DataContext.Should().BeOfType<CsvExportViewModel>().Which;
		bool? partialFileAtClose = null;
		exportWindow.Closing += (_, _) => partialFileAtClose = File.Exists(partialPath);
		_ = export.ExportCommand.Execute(Path.Combine(directory.Path, "out.csv")).Subscribe(_ => { }, _ => { });
		var token = await seedToken.Task;
		File.Exists(partialPath).Should().BeTrue("the running export writes the partial file");

		window.Close();

		window.IsVisible.Should().BeTrue("the export window holds the close until its cancelled export ends");

		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the export ended");

		token.IsCancellationRequested.Should().BeTrue("the close cancels the export");
		partialFileAtClose.Should().BeFalse("the export window closes only after the partial file is deleted");
		File.Exists(partialPath).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task WorkStartedBetweenTwoResumes_IsHeldByTheNextClose()
	{
		using var directory = new TemporaryDirectory("owned-windows-two-holds");
		var seedStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var exportGate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var reader = new FakeArchiveRowReader
		{
			OnSeeds = async _ =>
			{
				seedStarted.TrySetResult();
				await exportGate.Task;

				return Result.Ok<IReadOnlyList<StoredArchiveRow>>([]);
			}
		};
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(
			[new Pen(1, "Pen 1", [], "#ff0000")], penCatalogueEditor: penCatalogueEditor, rowReader: reader);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ExportCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<CsvExportWindow>().Any());
		var exportWindow = window.OwnedWindows.OfType<CsvExportWindow>().Single();
		var export = exportWindow.DataContext.Should().BeOfType<CsvExportViewModel>().Which;
		_ = export.ExportCommand.Execute(Path.Combine(directory.Path, "out.csv")).Subscribe(_ => { }, _ => { });
		await seedStarted.Task;
		var editor = await OpenTheEditorAndRetypeTheFirstName(stand, window);
		var nameWrite = penCatalogueEditor.HoldNextCall();

		window.Close();
		Dispatcher.UIThread.RunJobs();
		window.IsVisible.Should().BeTrue("both owned windows hold the close");

		nameWrite.SetResult();
		Dispatcher.UIThread.RunJobs();
		window.IsVisible.Should().BeTrue("the editor's resume closes nothing while the export holds");
		editor.IsVisible.Should().BeTrue();

		Retype(editor, ViewProbe.Named<TextBox>(editor, "FormUnit"), "bar");
		var unitWrite = penCatalogueEditor.HoldNextCall();
		exportGate.SetResult();
		await HeadlessWait.Until(
			() => penCatalogueEditor.Changes.Count() == 2, "the export's resume to reach the editor's new edit");
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the editor holds the export's resume for its new edit");
		exportWindow.IsVisible.Should().BeTrue();
		((ICommand)export.CancelCommand).CanExecute(null).Should().BeFalse("the export no longer runs");

		unitWrite.SetResult();
		await HeadlessWait.Until(() => !window.IsVisible, "the main window to close after the second write");

		window.OwnedWindows.Should().BeEmpty();
		penCatalogueEditor.Changes.Should().Equal(
			new FakeEditorCall.Change(Argon, new PenSettingChange.Name("Argon2")),
			new FakeEditorCall.Change(Argon with { Name = "Argon2" }, new PenSettingChange.Unit("bar")));
	}

	private static async Task<PenEditorWindow> OpenTheEditorAndRetypeTheFirstName(
		WindowStand stand, MainWindowView window)
	{
		await stand.ViewModel.ShowPenEditorCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<PenEditorWindow>().Any());
		Dispatcher.UIThread.RunJobs();
		var editor = window.OwnedWindows.OfType<PenEditorWindow>().Single();
		HeadlessInput.Click(editor, RowAt(editor, 0));
		Retype(editor, ViewProbe.Named<TextBox>(editor, "FormName"), "Argon2");

		return editor;
	}
}
