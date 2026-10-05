using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Messages;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapBandFeedTests
{
	private static readonly DateTime _extentFirst = MinimapStand.ExtentFirst;
	private static readonly DateTime _extentLast = MinimapStand.ExtentLast;

	[AvaloniaFact]
	public async Task ASevenDayExtent_ReadsTheDrawnPenAtTheHourLayer()
	{
		using var stand = new MinimapStand();
		stand.ShowPens();
		stand.Chart.DrawnPenId.Should().Be(1);

		await stand.LoadExtentAsync();
		stand.LandBandRead();

		var read = stand.Provider.BandHistoryQueries.Should().ContainSingle().Subject;
		read.PenIds.Should().Equal(1);
		read.FromUtc.Should().Be(_extentFirst);
		read.ToUtc.Should().Be(_extentLast);
		read.Layer.Should().Be(AggregationLayer.Hour);
		var band = stand.Band;
		band.Should().NotBeNull();
		band!.PenId.Should().Be(1);
		band.Timestamps.Should().Equal(_extentFirst, _extentLast);
	}

	[AvaloniaFact]
	public async Task AReloadedExtent_ReadsTheBandAgain()
	{
		using var stand = await MinimapStand.WithABandAsync();

		await stand.LoadExtentAsync();
		stand.LandBandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task ASupersededBandRead_IsCancelled()
	{
		using var stand = new MinimapStand();
		var provider = stand.Provider;
		stand.ShowPens();
		provider.GatedLayer = AggregationLayer.Hour;
		await stand.LoadExtentAsync();
		var held = provider.BandHistoryQueries.Should().ContainSingle().Subject;
		var heldGate = provider.HistoryGate;
		provider.HistoryGate = new TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>>();

		stand.Chart.SetActivePen(2).Should().BeTrue();

		held.CancellationToken.IsCancellationRequested.Should().BeTrue();
		provider.BandHistoryQueries[^1].PenIds.Should().Equal(2);
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(2)]));
		heldGate.TrySetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(1)]));
		stand.LandBandRead();
		stand.LandBandRead();

		stand.Band!.PenId.Should().Be(2);
		stand.Panel.Entries.Should().BeEmpty("a cancelled read is no failure");
	}

	[AvaloniaFact]
	public async Task AnOutage_ReportsOnceAndLogsEveryRepeat()
	{
		var logger = new RecordingLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		stand.ShowPens();
		stand.Provider.FailHistory = true;
		await stand.LoadExtentAsync();
		stand.LandBandRead();

		stand.Panel.Entries.Should().ContainSingle();

		// The catalogue sync's own entry tops the panel between two band reads, as it does every 5 s during an
		// outage, so a band repeat would not coalesce with the band's first entry.
		for (var repeat = 0; repeat < 2; repeat++)
		{
			stand.Panel.Report(ArchiveFailureMapper.Map(new Error("The pen catalogue read failed.")));
			stand.AdvanceToNextBandRead();
		}

		stand.Panel.Entries.Should().HaveCount(2, "the band's repeats add no entry");
		logger.Levels.Count(level => level == LogLevel.Warning).Should().Be(2);

		stand.Provider.FailHistory = false;
		stand.AdvanceToNextBandRead();
		stand.Provider.FailHistory = true;
		stand.AdvanceToNextBandRead();

		stand.Panel.Entries.Should().HaveCount(3, "a success arms the report again");
	}

	[AvaloniaFact]
	public async Task NoDrawnPen_LeavesTheBandEmptyAndReadsNothing()
	{
		using var stand = new MinimapStand();

		await stand.LoadExtentAsync();
		stand.LandBandRead();

		stand.ViewModel.HasExtent.Should().BeTrue();
		stand.Band.Should().BeNull();
		stand.ViewModel.BandFeed.BandColor.Should().BeNull();
		stand.Provider.BandHistoryQueries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task HidingTheDrawnPen_ClearsTheBandAndStopsTheReads()
	{
		using var stand = new MinimapStand();
		stand.Provider.Pens = [stand.Provider.Pens[0]];
		stand.ShowPens();
		await stand.LoadExtentAsync();
		stand.LandBandRead();
		stand.Band.Should().NotBeNull();

		stand.Chart.SetPenVisibility(1, isVisible: false);
		stand.LandBandRead();
		stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(2.0).Ticks);

		stand.Chart.DrawnPenId.Should().BeNull();
		stand.Band.Should().BeNull();
		stand.Provider.BandHistoryQueries.Should().ContainSingle();

		stand.Chart.SetPenVisibility(1, isVisible: true);
		stand.LandBandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2, "showing the pen again reads its band once");
		stand.Band.Should().NotBeNull();
	}

	[AvaloniaFact]
	public async Task ADrawnPenIdRaiseWithTheSameId_IssuesNoRead()
	{
		using var stand = await MinimapStand.WithABandAsync();

		stand.Chart.SetPenVisibility(2, isVisible: false);
		stand.LandBandRead();

		stand.Chart.DrawnPenId.Should().Be(1);
		stand.Provider.BandHistoryQueries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task TheNextRead_LandsTheReadDelayAfterTheLastOneLanded()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveFirstUtc = _extentLast - TimeSpan.FromHours(1.0);
		stand.ShowPens();
		await stand.LoadExtentAsync();
		stand.LandBandRead();
		var delay = MinimapBandFeed.NextReadDelay(stand.ViewModel.ExtentLast - stand.ViewModel.ExtentFirst);
		delay.Should().Be(TimeSpan.FromSeconds(3.6));

		stand.Scheduler.AdvanceBy(delay.Ticks - 1);
		stand.Provider.BandHistoryQueries.Should().ContainSingle();

		stand.Scheduler.AdvanceBy(1);
		stand.Provider.BandHistoryQueries.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task AThrowingRead_ReportsAndTheNextScheduledReadStillRuns()
	{
		using var stand = new MinimapStand();
		var thrown = new InvalidOperationException("the band read threw");
		stand.ShowPens();
		stand.Provider.HistoryReadException = thrown;
		await stand.LoadExtentAsync();
		stand.LandBandRead();

		stand.Panel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));

		stand.Provider.HistoryReadException = null;
		stand.AdvanceToNextBandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2);
		stand.Band.Should().NotBeNull();
	}

	[AvaloniaFact]
	public async Task ARecolourOfTheDrawnPen_ReachesBandColorWithoutARead()
	{
		using var stand = await MinimapStand.WithABandAsync();
		var pens = stand.Provider.Pens;
		stand.ViewModel.BandFeed.BandColor.Should().Be("#ff0000");

		stand.Chart.ApplyCatalogue([pens[0] with { Color = "#0000ff" }, pens[1]]);
		stand.LandBandRead();

		stand.ViewModel.BandFeed.BandColor.Should().Be("#0000ff");
		stand.Provider.BandHistoryQueries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task Dispose_LeavesNoScheduledReadToRun()
	{
		using var stand = await MinimapStand.WithABandAsync();

		stand.ViewModel.Dispose();
		var advance = () => stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(2.0).Ticks);

		advance.Should().NotThrow("a read left scheduled would request into the disposed pipeline");
	}

	[AvaloniaFact]
	public async Task APenChangeFollowedByAFailedRead_LeavesNoBand()
	{
		using var stand = await MinimapStand.WithABandAsync();
		stand.Provider.FailHistory = true;

		stand.Chart.SetActivePen(2).Should().BeTrue();
		stand.LandBandRead();

		stand.Band.Should().BeNull("pen 1's shape must not stay drawn in pen 2's colour");
		stand.ViewModel.BandFeed.BandColor.Should().Be("#00ff00");
	}

	[AvaloniaFact]
	public async Task AnAnswerQueuedBeforeAPenChange_IsDropped()
	{
		using var stand = new MinimapStand();
		var provider = stand.Provider;
		stand.ShowPens();
		await stand.LoadExtentAsync();
		provider.BandHistoryQueries.Should().ContainSingle("pen 1's answer waits on the UI scheduler");
		provider.GatedLayer = AggregationLayer.Hour;

		stand.Chart.SetActivePen(2).Should().BeTrue();
		stand.LandBandRead();
		stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(2.0).Ticks);

		stand.Band.Should().BeNull("pen 1's answer landed after pen 2 was drawn");
		provider.BandHistoryQueries.Should().HaveCount(2);
		provider.BandHistoryQueries[^1].CancellationToken.IsCancellationRequested.Should().BeFalse(
			"the dropped answer schedules no read that would cut off pen 2's");
	}

	[AvaloniaFact]
	public async Task AReadHeldPastTheReadDelay_IsNotCutOffByTheSchedule()
	{
		using var stand = await MinimapStand.WithABandAsync();
		var provider = stand.Provider;
		var delay = MinimapBandFeed.NextReadDelay(stand.ViewModel.ExtentLast - stand.ViewModel.ExtentFirst);
		provider.GatedLayer = AggregationLayer.Hour;
		await stand.LoadExtentAsync();
		var held = provider.BandHistoryQueries.Should().HaveCount(2).And.Subject.Last();

		stand.Scheduler.AdvanceBy(delay.Ticks);

		provider.BandHistoryQueries.Should().HaveCount(2, "the requested read replaced the scheduled one");
		held.CancellationToken.IsCancellationRequested.Should().BeFalse();

		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(1)]));
		stand.LandBandRead();
		stand.Scheduler.AdvanceBy(delay.Ticks - 1);
		provider.BandHistoryQueries.Should().HaveCount(2);

		stand.Scheduler.AdvanceBy(1);
		provider.BandHistoryQueries.Should().HaveCount(3, "the next read lands one delay after the held one");
	}

	[AvaloniaFact]
	public async Task ARepeatedThrowingRead_LogsItsExceptionAtWarning()
	{
		var logger = new RecordingLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		var thrown = new InvalidOperationException("the band read threw");
		stand.ShowPens();
		stand.Provider.HistoryReadException = thrown;
		await stand.LoadExtentAsync();
		stand.LandBandRead();

		stand.AdvanceToNextBandRead();

		logger.Levels.Should().Contain(LogLevel.Warning);
		logger.Exceptions[logger.Levels.LastIndexOf(LogLevel.Warning)].Should().BeSameAs(thrown);
	}

	[AvaloniaFact]
	public async Task ASpanJustAboveTheMinuteCeiling_StaysOnTheMinuteLayer()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveFirstUtc = _extentLast - TimeSpan.FromHours(62.0);
		stand.ShowPens();
		await stand.LoadExtentAsync();
		stand.LandBandRead();
		stand.Provider.BandHistoryQueries[^1].Layer.Should().Be(AggregationLayer.Minute);

		stand.Navigation.OnLiveEdge(_extentLast + TimeSpan.FromHours(2.0));
		stand.AdvanceToNextBandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2);
		stand.Provider.BandHistoryQueries[^1].Layer.Should().Be(
			AggregationLayer.Minute, "64 h is above the 62.5 h ceiling but inside the band's hysteresis");
	}

	[Fact]
	public void TheBandColumnCount_IsBelowTheChartsFloor()
	{
		MinimapBandFeed.MinimapColumns.Should().BeLessThan(HistoryColumnTarget.MinColumns);
	}

	[AvaloniaTheory]
	[InlineData(10.0 * 60.0, 2.0)]
	[InlineData(60.0 * 60.0, 3.6)]
	[InlineData(24.0 * 60.0 * 60.0, 60.0)]
	[InlineData(365.0 * 24.0 * 60.0 * 60.0, 60.0)]
	public void NextReadDelay_IsAThousandthOfTheSpanBetweenTwoSecondsAndAMinute(
		double spanSeconds,
		double expectedSeconds)
	{
		var delay = MinimapBandFeed.NextReadDelay(TimeSpan.FromSeconds(spanSeconds));

		delay.TotalSeconds.Should().BeApproximately(expectedSeconds, 1e-9);
	}

	private static PenHistoryEnvelope Envelope(int penId)
	{
		return new PenHistoryEnvelope(penId, [_extentFirst, _extentLast], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0]);
	}
}
