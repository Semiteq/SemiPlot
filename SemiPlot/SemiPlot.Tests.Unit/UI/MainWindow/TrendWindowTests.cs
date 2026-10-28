using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.Tests.Unit.UI.Markers;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

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
	public void DisposingTheWindowClosesTheLiveEdge()
	{
		var stand = NewWindowStand();
		var provider = stand.Provider;
		provider.OpenLiveSubscriptionCount.Should().Be(1);

		stand.Dispose();

		provider.OpenLiveSubscriptionCount.Should().Be(0);
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
		markers.ShownMarkers.Should().Equal(marker);

		await markers.DeleteAsync(marker);

		stand.MarkerStore.Writes.Should().Equal(new FakeMarkerWrite.Delete(marker));
	}

	[AvaloniaFact]
	public void TheWindowReadsTheCatalogueAtOnce()
	{
		using var stand = NewWindowStand();

		stand.Provider.PensQueryCount.Should().Be(1, "the connection state starts at window open");
	}

	[AvaloniaFact]
	public void ThreeUnreachableCatalogueReads_TurnTheBarToFault_WithOneConnectionLostEntry()
	{
		using var panel = new MessagePanelViewModel();
		using var stand = NewWindowStand(panel: panel);
		var statusBar = stand.ViewModel.StatusBar;
		var connectionLost = ArchiveFailureMapper.Map(
			FakeDataProvider.BenchArchiveError(ArchiveFault.ConnectionLost, "3"));
		stand.Provider.FailPens = true;

		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks * 3);

		statusBar.IsConnected.Should().BeFalse();
		var entry = panel.Entries.Should().ContainSingle().Which;
		entry.View.Should().Be(connectionLost);

		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks);

		panel.Entries.Should().ContainSingle().Which.Should().BeSameAs(entry);
		entry.RepeatCount.Should().Be(1, "a fourth failure neither adds nor restamps an entry");

		stand.Provider.FailPens = false;
		stand.Scheduler.AdvanceBy(ArchiveReadLoop.ReadInterval.Ticks);

		statusBar.IsConnected.Should().BeTrue();
		panel.Entries.Select(message => message.View.Severity).Should().Equal(
			MessageSeverity.Info, MessageSeverity.Warning);
		panel.Entries[0].View.Title.Should().Be(Resources.StatusConnectionRestoredTitle);
		panel.Entries[0].View.Detail.Should().Be(Resources.StatusConnectionRestoredDetail);
	}

	[AvaloniaFact]
	public void TheStatusBar_FollowsTheChartsLayer()
	{
		using var stand = NewWindowStand();

		DriveToLayer(stand.ViewModel.ChartViewModel.Navigation, AggregationLayer.Hour);

		stand.ViewModel.StatusBar.ActiveLayer.Should().Be(AggregationLayer.Hour);
	}
}
