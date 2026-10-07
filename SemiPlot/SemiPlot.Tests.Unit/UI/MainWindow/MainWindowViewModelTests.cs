using System.Diagnostics;
using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests
{
	// The code-behind route: the About dialog's throw cannot escape an async void handler, so it reports
	// through the view model rather than through a logger the view would have to hold itself.
	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInThePanel()
	{
		using var panel = new MessagePanelViewModel();
		using var stand = NewWindowStand(panel: panel);
		var viewModel = stand.ViewModel;

		viewModel.ReportFailure(new InvalidOperationException("the dialog refused"));

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Detail.Should().Contain("the dialog refused");
		panel.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task ShowSettings_WithADirectory_EmitsOneViewModelHoldingItsValues()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-main-settings-").FullName;
		try
		{
			WriteSection(
				configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml", "locale: en\ntheme: dark\n");
			WriteSection(
				configDirectory,
				StartupProbe.ConnectionDirectoryName,
				"connection.yaml",
				"host: 10.20.30.40\nport: 5433\ndatabase: archive\nuser: viewer\npassword: secret\n"
				+ "poll_interval_ms: 250\n");
			using var stand = NewWindowStand(configDirectory: configDirectory);
			var viewModel = stand.ViewModel;
			var requests = new List<SettingsViewModel>();
			using var subscription = viewModel.SettingsRequests.Subscribe(requests.Add);

			await viewModel.ShowSettingsCommand.Execute();

			using var settings = requests.Should().ContainSingle().Which;
			settings.SelectedLanguage!.Token.Should().Be("en");
			settings.SelectedTheme!.Token.Should().Be("dark");
			settings.Host.Should().Be("10.20.30.40");
			settings.Port.Should().Be(5433);
			settings.Database.Should().Be("archive");
			settings.User.Should().Be("viewer");
			settings.Password.Should().Be("secret");
			settings.PollInterval.Should().Be(250);
			viewModel.MessagePanel.Entries.Should().BeEmpty();
		}
		finally
		{
			Directory.Delete(configDirectory, recursive: true);
		}
	}

	[AvaloniaFact]
	public async Task ShowPenEditor_WithAnEditor_ReadsTheCatalogueAndEmitsOneViewModelOverIt()
	{
		var pen = new StoredPen(
			7, "Chamber pressure", "Pa", null, "#1F77B4", PenLineStyle.Interpolated, true, null, null, false);
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Ok(new PenCatalogue([pen], [new StoredGroup(1, "Chamber", [7])]))
		};
		using var stand = NewWindowStand(
			penCatalogueEditor: penCatalogueEditor, configDirectory: AppContext.BaseDirectory);
		var viewModel = stand.ViewModel;
		var requests = new List<PenEditorViewModel>();
		using var subscription = viewModel.PenEditorRequests.Subscribe(requests.Add);

		await viewModel.ShowPenEditorCommand.Execute();

		using var penEditor = requests.Should().ContainSingle().Which;
		penCatalogueEditor.Calls.Should().Equal(new FakeEditorCall.Read());
		penEditor.Rows.Select(row => row.Pen).Should().Equal(pen);
		penEditor.Groups.Groups.Select(group => group.Group.Name).Should().Equal("Chamber");
		viewModel.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ShowPenEditor_WhenTheReadFails_ReportsOnceAndEmitsNothing()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Fail<PenCatalogue>(
				new ArchiveError(ArchiveFault.Unreachable, "scada-host", 5432, "semiplot"))
		};
		using var stand = NewWindowStand(
			penCatalogueEditor: penCatalogueEditor, configDirectory: AppContext.BaseDirectory);
		var viewModel = stand.ViewModel;
		var requests = new List<PenEditorViewModel>();
		using var subscription = viewModel.PenEditorRequests.Subscribe(requests.Add);

		await viewModel.ShowPenEditorCommand.Execute();

		requests.Should().BeEmpty();
		viewModel.MessagePanel.Entries.Should().ContainSingle();
		penCatalogueEditor.Calls.Should().Equal(new FakeEditorCall.Read());
	}

	[AvaloniaFact]
	public async Task AnEditorWriteThatLands_ReadsTheCatalogueBeforeTheIntervalPasses()
	{
		using var stand = NewWindowStand(penCatalogueEditor: new FakePenCatalogueEditor());
		var viewModel = stand.ViewModel;
		using var penEditor = await OpenPenEditorAsync(viewModel);

		penEditor.Groups.NewGroupName = "Gas";
		await penEditor.Groups.CreateGroupCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		stand.Provider.PensQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task APenWriteThatLands_ReadsTheCatalogueBeforeTheIntervalPasses()
	{
		var pen = new StoredPen(
			1, "Pen 1", null, null, "#ff0000", PenLineStyle.Interpolated, true, null, null, false);
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Ok(new PenCatalogue([pen], []))
		};
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var viewModel = stand.ViewModel;
		using var penEditor = await OpenPenEditorAsync(viewModel);
		penEditor.SelectedRow = penEditor.Rows.Single();
		var form = penEditor.SelectedForm!;

		form.Name = "Chamber pressure";
		await form.EndEditAsync(PenField.Name);
		Dispatcher.UIThread.RunJobs();

		penCatalogueEditor.Changes.Should().ContainSingle();
		stand.Provider.PensQueryCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task AnEditorWriteTheEditorRefuses_ReadsNothing()
	{
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			CreateGroupResult = Result.Fail<int>(FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, "Gas"))
		};
		using var stand = NewWindowStand(penCatalogueEditor: penCatalogueEditor);
		var viewModel = stand.ViewModel;
		using var penEditor = await OpenPenEditorAsync(viewModel);

		penEditor.Groups.NewGroupName = "Gas";
		await penEditor.Groups.CreateGroupCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		penCatalogueEditor.Calls.Should().Contain(new FakeEditorCall.CreateGroup("Gas"));
		stand.Provider.PensQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public void RestartExitsOnlyAfterTheCopyStarted()
	{
		var started = 0;
		var exits = 0;
		var exitsWhenTheCopyStarted = -1;
		using var stand = NewWindowStand(start: _ =>
		{
			started++;
			exitsWhenTheCopyStarted = exits;
		});
		using var subscription = stand.ViewModel.ExitRequests.Subscribe(_ => exits++);

		stand.ViewModel.RestartApplication();

		started.Should().Be(1);
		exitsWhenTheCopyStarted.Should().Be(0, "the exit request follows the started copy");
		exits.Should().Be(1);
	}

	[AvaloniaFact]
	public void AFailedRestart_ReportsAndKeepsTheWindow()
	{
		var exits = 0;
		using var panel = new MessagePanelViewModel();
		using var stand = NewWindowStand(
			panel: panel, start: _ => throw new InvalidOperationException("no such file"));
		using var subscription = stand.ViewModel.ExitRequests.Subscribe(_ => exits++);

		stand.ViewModel.RestartApplication();

		exits.Should().Be(0);
		panel.Entries.Should().ContainSingle().Which.View.Severity.Should().Be(MessageSeverity.Error);
	}

	[AvaloniaFact]
	public async Task NewWindow_StartsACopyAndKeepsTheWindow()
	{
		var started = new List<ProcessStartInfo>();
		var exits = 0;
		using var stand = NewWindowStand(start: started.Add);
		using var subscription = stand.ViewModel.ExitRequests.Subscribe(_ => exits++);

		await stand.ViewModel.NewWindowCommand.Execute();

		started.Should().ContainSingle();
		exits.Should().Be(0);
	}

	private static async Task<PenEditorViewModel> OpenPenEditorAsync(MainWindowViewModel viewModel)
	{
		var requests = new List<PenEditorViewModel>();

		using (viewModel.PenEditorRequests.Subscribe(requests.Add))
		{
			await viewModel.ShowPenEditorCommand.Execute();
		}

		return requests.Should().ContainSingle().Which;
	}

	private static void WriteSection(string configDirectory, string section, string name, string content)
	{
		var directory = Directory.CreateDirectory(Path.Combine(configDirectory, section)).FullName;
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
