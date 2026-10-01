using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;

using ReactiveUI.Avalonia;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Unit.UI.Startup;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Di;

/// <summary>
/// What <c>TrendWindow.Build</c> wires, driven through the real method: the panel the chart, the
/// minimap and the status bar all report into, the connection stream the bar follows, and the catalogue read loop.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class TrendWindowBuildTests
{
	// The chart and the minimap take the panel by hand, so a second instance built at the wiring site would
	// collect every failure into a panel no window shows.
	[AvaloniaFact]
	public async Task TheChartAndTheMinimap_ReportIntoThePanelTheWindowShows()
	{
		using var stand = NewArchiveStand();
		var scheduler = stand.Scheduler;
		var dataProvider = stand.Provider;
		var container = stand.Data.ServiceProvider;

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		dataProvider.FailHistory = true;
		dataProvider.FailExtent = true;
		using var window = TrendWindow.Build(
			probe.Value, TestLaunch.LauncherAt(AppContext.BaseDirectory), AvaloniaScheduler.Instance);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);
		Dispatcher.UIThread.RunJobs();

		var panel = container.GetRequiredService<MessagePanelViewModel>();

		panel.Entries.Select(entry => entry.View.Title).Should().Contain(
			[
				ArchiveFailureMapper.Map(new Error("Forced history failure.")).Title,
				Resources.FailureArchiveReadFailedTitle
			],
			"the chart and the minimap both report into the container's panel");
	}

	[AvaloniaFact]
	public async Task TheStatusBar_FollowsTheCoordinatorsConnectionState()
	{
		using var stand = NewArchiveStand();
		var scheduler = stand.Scheduler;
		var dataProvider = stand.Provider;
		var container = stand.Data.ServiceProvider;

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		using var window = TrendWindow.Build(
			probe.Value, TestLaunch.LauncherAt(AppContext.BaseDirectory), AvaloniaScheduler.Instance);
		var mainWindowViewModel = window.ViewModel;

		mainWindowViewModel.StatusBar.IsConnected.Should().BeTrue();

		dataProvider.ReportConnectionState(
			new ArchiveConnectionState(
				new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3")));
		Dispatcher.UIThread.RunJobs();

		mainWindowViewModel.StatusBar.IsConnected.Should().BeFalse("the bar is bound to the coordinator");
		container.GetRequiredService<MessagePanelViewModel>().Entries.Should().NotBeEmpty(
			"the fault reaches the one panel the window shows");
	}

	// Without the window's read loop, or without its start, no editor write reaches the running chart.
	[AvaloniaFact]
	public async Task AnEditorWrite_ReadsTheCatalogueThroughTheLoopTheWindowOwns()
	{
		using var stand = NewArchiveStand();
		var scheduler = stand.Scheduler;
		var dataProvider = stand.Provider;
		var container = stand.Data.ServiceProvider;

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		using var window = TrendWindow.Build(
			probe.Value, TestLaunch.LauncherAt(AppContext.BaseDirectory), AvaloniaScheduler.Instance);
		Dispatcher.UIThread.RunJobs();
		var mainWindowViewModel = window.ViewModel;
		var requests = new List<PenEditorViewModel>();

		using (mainWindowViewModel.PenEditorRequests.Subscribe(requests.Add))
		{
			await mainWindowViewModel.ShowPenEditorCommand.Execute();
		}

		using var penEditor = requests.Should().ContainSingle().Which;
		var pensQueriesBefore = dataProvider.PensQueryCount;
		penEditor.Groups.NewGroupName = "Gas";
		await penEditor.Groups.CreateGroupCommand.Execute();
		Dispatcher.UIThread.RunJobs();

		dataProvider.PensQueryCount.Should().Be(pensQueriesBefore + 1);
	}
}
