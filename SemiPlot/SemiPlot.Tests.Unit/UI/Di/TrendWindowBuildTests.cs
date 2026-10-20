using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Reactive.Testing;

using ReactiveUI.Avalonia;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Startup;
using SemiPlot.UI;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Di;

/// <summary>
/// What <c>TrendWindow.Build</c> wires, driven through the real method: the panel the chart, the
/// minimap and the status bar all report into, and the catalogue read the bar's connection state comes from.
/// </summary>
[Collection(ProcessGlobalStateCollection.Name)]
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
			probe.Value,
			TestLaunch.LauncherAt(AppContext.BaseDirectory),
			AvaloniaScheduler.Instance,
			App.CsvDialectFor(UiLanguage.En),
			TimeZoneInfo.Utc);
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200.0).Ticks);
		Dispatcher.UIThread.RunJobs();

		var panel = container.GetRequiredService<MessagePanelViewModel>();

		// The failed extent leaves the minimap band unread, so the history failure here is the chart's own.
		panel.Entries.Select(entry => entry.View.Title).Should().Contain(
			[
				ArchiveFailureMapper.Map(FakeDataProvider.UnreachableArchive()).Title,
				Resources.FailureArchiveReadFailedTitle
			],
			"the chart and the minimap both report into the container's panel");
	}

	[AvaloniaFact]
	public async Task TheWindowReadsTheCatalogueAtOnce_OnTheRealScheduler()
	{
		using var stand = NewArchiveStand();
		var dataProvider = stand.Provider;

		var probe = await StartupProbe.ReadAsync(stand.Data.ServiceProvider, StartupProbe.DefaultReadBound);
		var pensQueriesBefore = dataProvider.PensQueryCount;

		using var window = TrendWindow.Build(
			probe.Value,
			TestLaunch.LauncherAt(AppContext.BaseDirectory),
			AvaloniaScheduler.Instance,
			App.CsvDialectFor(UiLanguage.En),
			TimeZoneInfo.Utc);
		Dispatcher.UIThread.RunJobs();

		dataProvider.PensQueryCount.Should().Be(pensQueriesBefore + 1);
	}

	// One fake serves both interfaces elsewhere, so only a second instance tells a cast from a resolve.
	[AvaloniaFact]
	public async Task TheWindowFollowsTheContainersLiveSource()
	{
		var liveSource = new FakeDataProvider(new TestScheduler(), TimeSpan.FromSeconds(1));
		using var stand = NewArchiveStand(liveSource: liveSource);

		var probe = await StartupProbe.ReadAsync(stand.Data.ServiceProvider, StartupProbe.DefaultReadBound);

		using var window = TrendWindow.Build(
			probe.Value,
			TestLaunch.LauncherAt(AppContext.BaseDirectory),
			AvaloniaScheduler.Instance,
			App.CsvDialectFor(UiLanguage.En),
			TimeZoneInfo.Utc);
		Dispatcher.UIThread.RunJobs();

		liveSource.LiveSubscriptionsOpened.Should().Be(1);
		stand.Provider.LiveSubscriptionsOpened.Should().Be(0);
	}
}
