using System.Reactive.Linq;
using System.Windows.Input;

using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.Tests.Unit.UI.Settings;
using SemiPlot.UI.Export;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

using MainWindowView = SemiPlot.UI.MainWindow.MainWindow;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>The settings, export and editor windows the main window owns without blocking it.</summary>
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class OwnedWindowsTests
{
	[AvaloniaFact]
	public async Task TheSettingsWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);

		ClickMenuItem(window, "EditMenu", "EditSettings");
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<SettingsDialog>().Any());

		var dialog = window.OwnedWindows.OfType<SettingsDialog>().Single();
		dialog.Owner.Should().BeSameAs(window);
		dialog.IsDialog.Should().BeFalse("the settings window leaves the main window usable");

		ClickMenuItem(window, "ViewMenu", "ViewLegend");
		Dispatcher.UIThread.RunJobs();

		stand.ViewModel.IsLegendVisible.Should().BeFalse("the main menu runs with the settings window open");
		dialog.IsVisible.Should().BeTrue();
		window.Close();
	}

	[AvaloniaFact]
	public async Task ASecondSettingsRequest_ActivatesTheOpenWindow_AndBuildsNoSecondViewModel()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		var activations = 0;
		dialog.Activated += (_, _) => activations++;
		settings.Password = "";

		await stand.ViewModel.ShowSettingsCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		window.OwnedWindows.OfType<SettingsDialog>().Should().ContainSingle().Which.Should().BeSameAs(dialog);
		dialog.DataContext.Should().BeSameAs(settings);
		activations.Should().Be(1, "the second request brings the open window to the front");
		settings.Password = "secret";
		((ICommand)settings.SaveCommand).CanExecute(null).Should()
			.BeTrue("the open window's view model is not disposed");
		window.Close();
	}

	[AvaloniaFact]
	public async Task ASecondSettingsRequest_RestoresTheMinimisedWindow()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		dialog.WindowState = WindowState.Minimized;
		Dispatcher.UIThread.RunJobs();

		await stand.ViewModel.ShowSettingsCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		window.OwnedWindows.OfType<SettingsDialog>().Should().ContainSingle().Which.Should().BeSameAs(dialog);
		dialog.WindowState.Should().Be(WindowState.Normal, "the second request restores the minimised window");
		window.Close();
	}

	[AvaloniaFact]
	public async Task AClosedSettingsWindow_DisposesItsViewModel_AndTheNextRequestBuildsANewOne()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		settings.Password = "";

		dialog.Close();
		Dispatcher.UIThread.RunJobs();

		window.OwnedWindows.Should().BeEmpty();
		ShouldBeDisposed(settings);

		var reopened = await OpenSettings(stand, window);

		reopened.Should().NotBeSameAs(dialog);
		reopened.DataContext.Should().BeOfType<SettingsViewModel>().Which.Should().NotBeSameAs(settings);
		window.Close();
	}

	[AvaloniaFact]
	public async Task Escape_ClosesTheSettingsWindow()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);
		var settings = dialog.DataContext.Should().BeOfType<SettingsViewModel>().Which;
		settings.Password = "";

		HeadlessInput.Press(dialog, PhysicalKey.Escape);
		Dispatcher.UIThread.RunJobs();

		dialog.IsVisible.Should().BeFalse();
		window.OwnedWindows.Should().BeEmpty();
		ShouldBeDisposed(settings);
		window.Close();
	}

	[AvaloniaFact]
	public async Task ShiftEscape_LeavesTheSettingsWindowOpen()
	{
		using var configDirectory = ShippedConfiguration.CopyToTemporaryDirectory();
		using var stand = NewWindowStand(configDirectory: configDirectory);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var dialog = await OpenSettings(stand, window);

		HeadlessInput.Press(dialog, PhysicalKey.Escape, RawInputModifiers.Shift);
		Dispatcher.UIThread.RunJobs();

		dialog.IsVisible.Should().BeTrue("only Escape with no modifier closes the window");
		window.OwnedWindows.Should().ContainSingle();
		window.Close();
	}

	[AvaloniaFact]
	public async Task TheExportWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns()
	{
		using var stand = NewWindowStand([new Pen(1, "Pen 1", [], "#ff0000")]);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);

		ClickMenuItem(window, "FileMenu", "FileExport");
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<CsvExportWindow>().Any());

		var exportWindow = window.OwnedWindows.OfType<CsvExportWindow>().Single();
		exportWindow.Owner.Should().BeSameAs(window);
		exportWindow.IsDialog.Should().BeFalse("the export window leaves the main window usable");

		ClickMenuItem(window, "ViewMenu", "ViewLegend");
		Dispatcher.UIThread.RunJobs();

		stand.ViewModel.IsLegendVisible.Should().BeFalse("the main menu runs with the export window open");
		exportWindow.IsVisible.Should().BeTrue();
		window.Close();
	}

	[AvaloniaFact]
	public async Task ASecondExportRequest_ActivatesTheOpenWindow_AndKeepsItsRange()
	{
		var reader = HeldCountReader();
		using var stand = NewWindowStand([new Pen(1, "Pen 1", [], "#ff0000")], rowReader: reader);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ExportCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<CsvExportWindow>().Any());
		Dispatcher.UIThread.RunJobs();
		stand.Scheduler.AdvanceBy(CsvExportViewModel.CountThrottle.Ticks);
		await HeadlessWait.Until(() => reader.CountCalls.Count == 1, "the open window's count to start");
		var exportWindow = window.OwnedWindows.OfType<CsvExportWindow>().Single();
		var export = exportWindow.DataContext.Should().BeOfType<CsvExportViewModel>().Which;
		var from = export.Form.From;
		var to = export.Form.To;
		var openCount = reader.CountCalls.Single();
		var activations = 0;
		exportWindow.Activated += (_, _) => activations++;
		var navigation = stand.ViewModel.ChartViewModel.Navigation;
		var chartFrom = navigation.From;
		navigation.SetWindow(navigation.From.AddHours(-1), navigation.To.AddHours(-1));
		navigation.From.Should().NotBe(chartFrom, "the chart moved before the second request");

		await stand.ViewModel.ExportCommand.Execute();
		Dispatcher.UIThread.RunJobs();
		stand.Scheduler.AdvanceBy(CsvExportViewModel.CountThrottle.Ticks);
		Dispatcher.UIThread.RunJobs();

		window.OwnedWindows.OfType<CsvExportWindow>().Should().ContainSingle().Which.Should().BeSameAs(exportWindow);
		exportWindow.DataContext.Should().BeSameAs(export);
		activations.Should().Be(1, "the second request brings the open window to the front");
		export.Form.From.Should().Be(from, "the open window keeps the range the operator left");
		export.Form.To.Should().Be(to);
		reader.CountCalls.Should().ContainSingle("the second request reads nothing");
		openCount.Token.IsCancellationRequested.Should().BeFalse("the open window's count still runs");
		window.Close();
	}

	[AvaloniaFact]
	public async Task Escape_ClosesTheExportWindow()
	{
		var reader = HeldCountReader();
		using var stand = NewWindowStand([new Pen(1, "Pen 1", [], "#ff0000")], rowReader: reader);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ExportCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<CsvExportWindow>().Any());
		Dispatcher.UIThread.RunJobs();
		stand.Scheduler.AdvanceBy(CsvExportViewModel.CountThrottle.Ticks);
		await HeadlessWait.Until(() => reader.CountCalls.Count == 1, "the open window's count to start");
		var exportWindow = window.OwnedWindows.OfType<CsvExportWindow>().Single();

		HeadlessInput.Press(exportWindow, PhysicalKey.Escape);
		Dispatcher.UIThread.RunJobs();

		exportWindow.IsVisible.Should().BeFalse();
		window.OwnedWindows.Should().BeEmpty();
		reader.CountCalls.Single().Token.IsCancellationRequested.Should()
			.BeTrue("the closed window disposes its view model");
		window.Close();
	}

	[AvaloniaFact]
	public async Task ThePenEditor_IsOwnedAndNotModal_AndTheMainMenuStillRuns()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);

		ClickMenuItem(window, "EditMenu", "EditPensAndGroups");
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<PenEditorWindow>().Any());

		var editor = window.OwnedWindows.OfType<PenEditorWindow>().Single();
		editor.Owner.Should().BeSameAs(window);
		editor.IsDialog.Should().BeFalse("the pen editor leaves the main window usable");

		ClickMenuItem(window, "ViewMenu", "ViewLegend");
		Dispatcher.UIThread.RunJobs();

		stand.ViewModel.IsLegendVisible.Should().BeFalse("the main menu runs with the pen editor open");
		editor.IsVisible.Should().BeTrue();
		window.Close();
	}

	[AvaloniaFact]
	public async Task ASecondPenEditorRequest_ActivatesTheOpenWindow_AndReadsTheCatalogueOnce()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		await stand.ViewModel.ShowPenEditorCommand.Execute();
		await HeadlessWait.Until(() => window.OwnedWindows.OfType<PenEditorWindow>().Any());
		Dispatcher.UIThread.RunJobs();
		var editor = window.OwnedWindows.OfType<PenEditorWindow>().Single();
		var penEditor = editor.DataContext.Should().BeOfType<PenEditorViewModel>().Which;
		var activations = 0;
		editor.Activated += (_, _) => activations++;

		stand.ViewModel.ShowPenEditorCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		window.OwnedWindows.OfType<PenEditorWindow>().Should().ContainSingle().Which.Should().BeSameAs(editor);
		editor.DataContext.Should().BeSameAs(penEditor);
		activations.Should().Be(1, "the second request brings the open window to the front");
		penCatalogueEditor.Calls.Should().Equal(new FakeEditorCall.Read());
		window.Close();
	}

	[AvaloniaFact]
	public void ThePenEditorCommand_CannotExecuteWhileItsReadRuns()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor { ReadResult = Result.Ok(ViewCatalogue) };
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var window = new MainWindowView { DataContext = stand.ViewModel };
		ViewProbe.Realise(window);
		var command = (ICommand)stand.ViewModel.ShowPenEditorCommand;
		var gate = penCatalogueEditor.HoldNextCall();

		stand.ViewModel.ShowPenEditorCommand.Execute().Subscribe();
		Dispatcher.UIThread.RunJobs();

		command.CanExecute(null).Should().BeFalse("a second click during the read is refused");

		gate.SetResult();
		Dispatcher.UIThread.RunJobs();

		command.CanExecute(null).Should().BeTrue("the command runs again once its read landed");
		window.Close();
	}

	[AvaloniaFact]
	public void AWindowThatFailsToBuild_IsReported_ForgottenAndDisposedOnce()
	{
		var owner = ViewProbe.Realise(new Window());
		var viewModel = new CountingDisposable();
		var failure = new InvalidOperationException("the window failed to build");
		var reported = new List<Exception>();
		var forgotten = new List<object>();

		DialogOpener.ShowOrActivate(owner, viewModel, () => throw failure, forgotten.Add, reported.Add);
		Dispatcher.UIThread.RunJobs();

		reported.Should().Equal(failure);
		forgotten.Should().Equal(viewModel);
		viewModel.DisposeCount.Should().Be(1);
		owner.OwnedWindows.Should().BeEmpty();
		owner.Close();
	}

	private sealed class CountingDisposable : IDisposable
	{
		public int DisposeCount { get; private set; }

		public void Dispose()
		{
			DisposeCount++;
		}
	}
}
