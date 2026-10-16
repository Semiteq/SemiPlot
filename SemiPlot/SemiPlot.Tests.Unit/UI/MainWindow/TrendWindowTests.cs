using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.Tests.Unit.UI.Startup;
using SemiPlot.UI;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Startup;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

/// <summary>
/// The window's composition and owner: what <c>TrendWindow.Build</c> builds and <c>Dispose</c> releases.
/// </summary>
[Trait("Component", "UI")]
[Trait("Area", "Di")]
[Trait("Category", "Unit")]
public sealed class TrendWindowTests
{
	[AvaloniaFact]
	public void DisposingTheWindowClosesTheLiveEdgeBeforeTheConnectionStream()
	{
		var stand = NewWindowStand();
		var provider = stand.Provider;
		var connectionObserversAtLiveEdgeClose = new List<int>();
		provider.OpenLiveSubscriptionCount.Should().Be(1);
		provider.ConnectionFaultsObserverCount.Should().Be(1);
		provider.LiveSubscriptionChanging = () =>
			connectionObserversAtLiveEdgeClose.Add(provider.ConnectionFaultsObserverCount);

		stand.Dispose();

		connectionObserversAtLiveEdgeClose.Should().Equal(
			[1],
			"the live edge closed once, with the coordinator's connection subscription still open");
		provider.OpenLiveSubscriptionCount.Should().Be(0);
		provider.ConnectionFaultsObserverCount.Should().Be(0, "the coordinator is disposed after the chart");
	}

	[AvaloniaFact]
	public void DisposingTheWindowStopsTheCatalogueRead()
	{
		var stand = NewWindowStand();
		var pensQueriesBefore = stand.Provider.PensQueryCount;

		stand.Dispose();
		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks * 2);

		stand.Provider.PensQueryCount.Should().Be(pensQueriesBefore);
	}

	[AvaloniaFact]
	public void TheWindowReadsItsMarkersAtOnce_AndDisposingItStopsTheMarkerRead()
	{
		var stand = NewWindowStand();
		stand.MarkerStore.ReadCount.Should().Be(1, "the window shows its markers right after it opens");

		stand.Dispose();
		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks * 2);

		stand.MarkerStore.ReadCount.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task TheChart_ReadsAndWritesThroughTheMarkerSyncTheWindowStarts()
	{
		using var stand = NewWindowStand();
		var marker = new StoredMarker(1, new DateTime(2026, 1, 1, 8, 0, 0, DateTimeKind.Utc), "Etch start");
		stand.MarkerStore.ReadResult = Result.Ok<IReadOnlyList<StoredMarker>>([marker]);
		var markers = stand.ViewModel.ChartViewModel.Markers;

		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks);
		markers.Markers.Should().Equal(marker);

		await markers.DeleteAsync(marker);

		stand.MarkerStore.Writes.Should().Equal(new FakeMarkerWrite.Delete(marker));
	}

	// The TestScheduler defers a fault reported as the live edge opens past Build, so the bar is bound by then.
	[AvaloniaFact]
	public void AFaultReportedAsTheLiveEdgeOpens_ReachesTheStatusBar()
	{
		using var archive = NewArchiveStand();
		var lost = new ArchiveConnectionState(
			FakePenCatalogueEditor.Refusal(ArchiveFault.ConnectionLost, "3"));
		archive.Provider.LiveSubscriptionChanging = () => archive.Provider.ReportConnectionState(lost);
		using var window = TrendWindow.Build(
			archive.Data,
			TestLaunch.LauncherAt(AppContext.BaseDirectory),
			archive.Scheduler,
			App.CsvDialectFor(UiLanguage.En),
			TimeZoneInfo.Utc);
		archive.Provider.LiveSubscriptionChanging = null;

		archive.Scheduler.AdvanceBy(1);

		window.ViewModel.StatusBar.IsConnected.Should().BeFalse();
	}

	[AvaloniaFact]
	public void TheStatusBar_FollowsTheChartsLayer()
	{
		using var stand = NewWindowStand();

		DriveToLayer(stand.ViewModel.ChartViewModel.Navigation, AggregationLayer.Hour);

		stand.ViewModel.StatusBar.ActiveLayer.Should().Be(AggregationLayer.Hour);
	}
}
