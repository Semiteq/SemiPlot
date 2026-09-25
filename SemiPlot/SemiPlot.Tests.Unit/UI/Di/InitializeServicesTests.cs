using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Di;

/// <summary>
/// What <c>App.InitializeServices</c> wires, driven through the real method: the panel the chart, the
/// minimap and the status bar all report into, and the connection stream the bar is bound to once.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class InitializeServicesTests
{
	// The chart and the minimap take the panel by hand, so a second instance built at the wiring site would
	// collect every failure into a panel no window shows.
	[AvaloniaFact]
	public async Task TheChartAndTheMinimap_ReportIntoThePanelTheWindowShows()
	{
		var scheduler = new TestScheduler();
		var dataProvider = NewProvider(scheduler);
		using var container = BuildContainer(scheduler, dataProvider);

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		dataProvider.FailHistory = true;
		dataProvider.FailExtent = true;
		App.InitializeServices(probe.Value);
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

	// The status bar's connection stream is bound once, at the wiring site. Without that line the indicator
	// reads "connected" for the life of the session and no live-edge fault ever reaches the panel.
	[AvaloniaFact]
	public async Task TheStatusBar_FollowsTheCoordinatorsConnectionState()
	{
		var scheduler = new TestScheduler();
		var dataProvider = NewProvider(scheduler);
		using var container = BuildContainer(scheduler, dataProvider);

		var probe = await StartupProbe.ReadAsync(container, StartupProbe.DefaultReadBound);

		App.InitializeServices(probe.Value);
		var mainWindowViewModel = container.GetRequiredService<MainWindowViewModel>();

		mainWindowViewModel.StatusBar.IsConnected.Should().BeTrue();

		dataProvider.ReportConnectionState(
			new ArchiveConnectionState(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3")));
		Dispatcher.UIThread.RunJobs();

		mainWindowViewModel.StatusBar.IsConnected.Should().BeFalse("the bar is bound to the coordinator");
		container.GetRequiredService<MessagePanelViewModel>().Entries.Should().NotBeEmpty(
			"the fault reaches the one panel the window shows");
	}

	// A TestScheduler, not CurrentThreadScheduler: InitializeServices calls TrendCoordinator.Start, and a
	// recurring realtime subscription on the current thread's trampoline never returns control.
	private static FakeDataProvider NewProvider(TestScheduler scheduler)
	{
		return new FakeDataProvider(scheduler, TimeSpan.FromSeconds(1));
	}

	private static ServiceProvider BuildContainer(TestScheduler scheduler, IDataProvider dataProvider)
	{
		var services =
			new ServiceCollection()
				.AddSingleton<IScheduler>(scheduler)
				.AddSingleton(dataProvider)
				.AddUi(AppContext.BaseDirectory);

		services.AddLogging();

		return services.BuildServiceProvider();
	}
}
