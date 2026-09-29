using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

using RxUnit = System.Reactive.Unit;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class MainWindowViewModelTests
{
	[AvaloniaFact]
	public void SetChart_BuildsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		viewModel.NavigationBarViewModel.Should().NotBeNull();
		viewModel.LegendViewModel.Should().NotBeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithNull_DropsTheNavigationBarAndTheLegend()
	{
		using var viewModel = NewViewModel();
		viewModel.SetChart(CreateChartWithPens());

		viewModel.SetChart(null);

		viewModel.NavigationBarViewModel.Should().BeNull();
		viewModel.LegendViewModel.Should().BeNull();
	}

	[AvaloniaFact]
	public void SetChart_WithTheSameInstance_KeepsTheChartAlive()
	{
		using var viewModel = NewViewModel();
		var chart = CreateChartWithPens();
		viewModel.SetChart(chart);
		var activePenId = chart.ActivePenId;
		var navigationBar = viewModel.NavigationBarViewModel;

		viewModel.SetChart(chart);

		// Every mutating member throws ObjectDisposedException once the chart is disposed.
		chart.SetActivePen(activePenId).Should().BeTrue();
		viewModel.NavigationBarViewModel.Should().BeSameAs(navigationBar);
	}

	// The status bar outlives the chart, so the bar follows whichever chart is in force rather than being
	// rebuilt with it: a rebuilt bar would lose the connection stream bound once at startup.
	[AvaloniaFact]
	public void SetChart_PointsTheStatusBarAtItsLayer()
	{
		using var panel = new MessagePanelViewModel();
		var statusBar = NewStatusBar(panel);
		using var viewModel = new MainWindowViewModel(
			panel, statusBar, AppContext.BaseDirectory, NullLoggerFactory.Instance);
		var chart = CreateChartWithPens();

		viewModel.SetChart(chart);

		statusBar.ActiveLayer.Should().Be(chart.Navigation.ActiveLayer);
		viewModel.StatusBar.Should().BeSameAs(statusBar);
	}

	[AvaloniaFact]
	public void StartupFailure_WhenSet_MakesThePanelVisibleAndTheChartNull()
	{
		var failure = new ArchiveFailureView(
			"Startup failed",
			"detail",
			"remedy",
			MessageSeverity.Error);

		using var viewModel = NewViewModel();
		viewModel.StartupFailure = failure;

		viewModel.HasStartupFailure.Should().BeTrue();
		viewModel.ChartViewModel.Should().BeNull();
	}

	// The code-behind route: the About dialog's throw cannot escape an async void handler, so it reports
	// through the view model rather than through a logger the view would have to hold itself.
	[AvaloniaFact]
	public void ReportFailure_PutsTheThrowInThePanel()
	{
		using var panel = new MessagePanelViewModel();
		using var viewModel = new MainWindowViewModel(
			panel, NewStatusBar(panel), AppContext.BaseDirectory, NullLoggerFactory.Instance);

		viewModel.ReportFailure(new InvalidOperationException("the dialog refused"));

		panel.Entries.Should().ContainSingle();
		panel.Entries[0].View.Detail.Should().Contain("the dialog refused");
		panel.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ShowSettings_WithNoDirectory_CannotExecute()
	{
		using var viewModel = NewViewModel(configDirectory: null);

		viewModel.ShowSettingsCommand.Should().NotBeNull();
		CanShowSettings(viewModel).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ShowSettings_WithADirectory_EmitsOneViewModelHoldingItsValues()
	{
		var configDirectory = Directory.CreateTempSubdirectory("semiplot-main-settings-").FullName;
		try
		{
			WriteSection(configDirectory, StartupSequence.SettingsDirectoryName, "app.yaml", "locale: en\ntheme: dark\n");
			WriteSection(
				configDirectory,
				StartupProbe.ConnectionDirectoryName,
				"connection.yaml",
				"host: 10.20.30.40\nport: 5433\ndatabase: archive\nuser: viewer\npassword: secret\n"
				+ "poll_interval_ms: 250\n");
			using var viewModel = NewViewModel(configDirectory);
			var requests = new List<SettingsViewModel>();
			using var subscription = viewModel.SettingsRequests.Subscribe(requests.Add);

			CanShowSettings(viewModel).Should().BeTrue();
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
	public void ShowPenEditor_WithNoEditor_CannotExecute()
	{
		using var viewModel = NewViewModel(AppContext.BaseDirectory);

		viewModel.ShowPenEditorCommand.Should().NotBeNull();
		CanExecute(viewModel.ShowPenEditorCommand).Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ShowPenEditor_WithAnEditor_ReadsTheCatalogueAndEmitsOneViewModelOverIt()
	{
		var pen = new StoredPen(
			7, "Chamber pressure", "Pa", null, "#1F77B4", PenLineStyle.Interpolated, true, null, null);
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Ok(new PenCatalogue([pen], [new StoredGroup(1, "Chamber", [7])]))
		};
		using var viewModel = NewViewModel(AppContext.BaseDirectory, penCatalogueEditor);
		var requests = new List<PenEditorViewModel>();
		using var subscription = viewModel.PenEditorRequests.Subscribe(requests.Add);

		CanExecute(viewModel.ShowPenEditorCommand).Should().BeTrue();
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
		using var viewModel = NewViewModel(AppContext.BaseDirectory, penCatalogueEditor);
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
		var stand = NewLiveCatalogueStand(penCatalogueEditor: new FakePenCatalogueEditor());
		using var viewModel = stand.ViewModel;
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
			1, "Pen 1", null, null, "#ff0000", PenLineStyle.Interpolated, true, null, null);
		var penCatalogueEditor = new FakePenCatalogueEditor
		{
			ReadResult = Result.Ok(new PenCatalogue([pen], []))
		};
		var stand = NewLiveCatalogueStand(penCatalogueEditor: penCatalogueEditor);
		using var viewModel = stand.ViewModel;
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
		var stand = NewLiveCatalogueStand(penCatalogueEditor: penCatalogueEditor);
		using var viewModel = stand.ViewModel;
		using var penEditor = await OpenPenEditorAsync(viewModel);

		penEditor.Groups.NewGroupName = "Gas";
		await penEditor.Groups.CreateGroupCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		penCatalogueEditor.Calls.Should().Contain(new FakeEditorCall.CreateGroup("Gas"));
		stand.Provider.PensQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public void ADeltaAddingAPenToAChartWithPens_ReachesTheChartAndTheSidebarAndReadsTheExtentOnce()
	{
		var stand = NewLiveCatalogueStand();
		using var viewModel = stand.ViewModel;
		stand.Provider.Pens = [.. stand.Provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		viewModel.ChartViewModel!.FindPen(3).Should().NotBeNull();
		viewModel.LegendViewModel!.Groups.SelectMany(group => group.Rows).Select(row => row.Name).Should()
			.Equal("Pen 1", "Pen 2", "Pen 3");
		stand.Provider.ExtentQueryCount.Should().Be(1);
		viewModel.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ADeltaAddingAPenWithOlderRows_LetsTheChartPanBackToThem()
	{
		var stand = NewLiveCatalogueStand();
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel!;
		var olderFirstSample = chart.Navigation.FirstSample - TimeSpan.FromDays(30.0);
		provider.ArchiveFirstUtc = olderFirstSample;
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);
		chart.Navigation.PanBy(TimeSpan.FromDays(-60.0));

		viewModel.MinimapViewModel!.ExtentFirst.Should().Be(olderFirstSample);
		chart.Navigation.From.Should().Be(olderFirstSample);
	}

	[AvaloniaFact]
	public void ADeltaGivingAnEmptyChartItsFirstPens_SeedsTheNavigationBeforeTheHistoryQuery()
	{
		var stand = NewLiveCatalogueStand(pens: []);
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel!;
		provider.GateExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeTrue("the pens wait for the extent");
		provider.HistoryQueryCount.Should().Be(0);

		var extent = new ArchiveExtent(provider.ArchiveFirstUtc, provider.ArchiveLastUtc);
		provider.ExtentGate.SetResult(Result.Ok(extent));
		Dispatcher.UIThread.RunJobs();
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeFalse();
		chart.Navigation.FirstSample.Should().Be(provider.ArchiveFirstUtc);
		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedPenIds.Should().Equal(1);
		provider.LastQueriedFromUtc.Should().BeBefore(provider.ArchiveLastUtc);
	}

	[AvaloniaFact]
	public void ADeltaGivingAnEmptyChartItsFirstPens_WhenTheExtentReadFails_StillAppliesThePens()
	{
		var stand = NewLiveCatalogueStand(pens: []);
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel!;
		var firstSampleBefore = chart.Navigation.FirstSample;
		provider.FailExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeFalse();
		chart.Navigation.FirstSample.Should().Be(firstSampleBefore, "no extent came back to seed it with");
		provider.ExtentQueryCount.Should().Be(1);
		provider.LastQueriedPenIds.Should().Equal(1);
		viewModel.MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Title.Should().Be(Resources.FailureArchiveReadFailedTitle);
	}

	[AvaloniaFact]
	public void ADeltaThatAddsNoPen_ReadsNoExtent()
	{
		var stand = NewLiveCatalogueStand();
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var stored = provider.Pens;

		provider.Pens = [stored[0]];
		AdvanceOneRead(stand);
		provider.Pens = [stored[0] with { Name = "Chamber pressure" }];
		AdvanceOneRead(stand);

		viewModel.ChartViewModel!.FindPen(2).Should().BeNull();
		viewModel.ChartViewModel.FindPen(1)!.Pen.Name.Should().Be("Chamber pressure");
		provider.ExtentQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public void ADeltaWhoseExtentReloadThrows_AddsOnePanelEntry_AndTheNextDeltaStillApplies()
	{
		var stand = NewLiveCatalogueStand();
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		provider.GateExtent = true;
		provider.ExtentGate.SetException(new InvalidOperationException("the extent read threw"));
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		viewModel.ChartViewModel!.FindPen(3).Should().NotBeNull("the reload follows the apply");
		provider.ExtentQueryCount.Should().Be(1);
		viewModel.MessagePanel.Entries.Should().ContainSingle();

		provider.Pens = [provider.Pens[0] with { Name = "Chamber pressure" }, provider.Pens[1], provider.Pens[2]];
		AdvanceOneRead(stand);

		viewModel.ChartViewModel.FindPen(1)!.Pen.Name.Should().Be("Chamber pressure");
		provider.ExtentQueryCount.Should().Be(1, "a delta that adds no pen reads no extent");
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	// A chart listener throws once, after the chart took pen 3 and before the sidebar learned of it.
	[AvaloniaFact]
	public void ADeltaWhoseApplyThrows_AddsOnePanelEntry_AndTheNextReadCarriesItAgain()
	{
		var stand = NewLiveCatalogueStand();
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel!;
		var throwsOnce = true;
		chart.PropertyChanged += (_, change) =>
		{
			if (throwsOnce && change.PropertyName == nameof(TrendChartViewModel.HasNoPens))
			{
				throwsOnce = false;

				throw new InvalidOperationException("a chart listener threw");
			}
		};
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		throwsOnce.Should().BeFalse("the listener has to have thrown");
		LegendRowNames(viewModel).Should().NotContain("Pen 3");
		viewModel.MessagePanel.Entries.Should().ContainSingle();

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		LegendRowNames(viewModel).Should().Contain("Pen 3");
		provider.LastQueriedPenIds.Should().Contain(3);
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	// The first delta waits for the extent while the second, read against it, queues behind. The first throws,
	// so the second reaches a chart that never took pen 1, and the rebase puts the loop's baseline behind both:
	// the next read names pen 2 as added, not revised.
	[AvaloniaFact]
	public void AnApplyThatThrowsWithTheNextDeltaQueued_StillTakesALaterRevisionOfThatDeltasPen()
	{
		var stand = NewLiveCatalogueStand(pens: []);
		using var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel!;
		var first = new Pen(1, "Pen 1", ["Group A"], "#ff0000");
		var second = new Pen(2, "Pen 2", ["Group A"], "#00ff00");
		provider.GateExtent = true;
		provider.Pens = [first];
		AdvanceOneRead(stand);
		provider.Pens = [first, second];
		AdvanceOneRead(stand);

		provider.GateExtent = false;
		provider.ExtentGate.SetException(new InvalidOperationException("the extent read threw"));
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.Pens.Select(state => state.Pen).Should().Equal(first, second);

		provider.Pens = [first, second with { Color = "#0000ff" }];
		AdvanceOneRead(stand);

		chart.FindPen(2)!.Pen.Color.Should().Be("#0000ff");
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public void AnApplyAwaitingTheExtentAtDisposal_AppliesNothingAndReportsNothing()
	{
		var stand = NewLiveCatalogueStand(pens: []);
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		provider.GateExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];
		AdvanceOneRead(stand);

		viewModel.Dispose();
		provider.ExtentGate.SetResult(Result.Ok(new ArchiveExtent(provider.ArchiveFirstUtc, provider.ArchiveLastUtc)));
		Dispatcher.UIThread.RunJobs();

		viewModel.MessagePanel.Entries.Should().BeEmpty();
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

	private static IEnumerable<string> LegendRowNames(MainWindowViewModel viewModel)
	{
		return viewModel.LegendViewModel!.Groups.SelectMany(group => group.Rows).Select(row => row.Name);
	}

	private static void AdvanceOneRead(LiveCatalogueStand stand)
	{
		Advance(stand, PenCatalogueSync.ReadInterval);
	}

	private static void Advance(LiveCatalogueStand stand, TimeSpan time)
	{
		stand.Scheduler.AdvanceBy(time.Ticks);
		Dispatcher.UIThread.RunJobs();
	}

	private static bool CanShowSettings(MainWindowViewModel viewModel)
	{
		return CanExecute(viewModel.ShowSettingsCommand);
	}

	private static bool CanExecute(ReactiveCommand<RxUnit, RxUnit> command)
	{
		bool? latest = null;

		using (command.CanExecute.Subscribe(value => latest = value))
		{
			return latest ?? throw new InvalidOperationException("The command replayed no execute state.");
		}
	}

	private static void WriteSection(string configDirectory, string section, string name, string content)
	{
		var directory = Directory.CreateDirectory(Path.Combine(configDirectory, section)).FullName;
		File.WriteAllText(Path.Combine(directory, name), content);
	}
}
