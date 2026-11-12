using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

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
	private static readonly DateOnly _monday = new(2025, 12, 29);
	private static readonly DateOnly _tuesday = new(2025, 12, 30);
	private static readonly DateOnly _wednesday = new(2025, 12, 31);

	[AvaloniaFact]
	public async Task AnExtent_ReadsTheDrawnPenOverThePageAtTheFloorCount()
	{
		using var stand = new MinimapStand();
		stand.ShowPens();
		stand.Chart.DrawnPenId.Should().Be(1);

		await stand.LoadExtentAsync();
		stand.LandRead();

		var page = stand.ViewModel.Pager.Page;
		page.ToUtc.Should().Be(page.FromUtc.AddDays(1.0), "with no day spans a Day page is the calendar day");
		var read = stand.Provider.BandHistoryQueries.Should().ContainSingle().Subject;
		read.PenIds.Should().Equal(1);
		(read.FromUtc, read.ToUtc).Should().Be((page.FromUtc, page.ToUtc));
		read.TargetColumnCount.Should().Be(MinimapBandFeed.ColumnStep, "the strip has reported no width");
		read.Layer.Should().Be(AggregationLayer.Minute);
		var band = stand.Band;
		band.Should().NotBeNull();
		band!.PenId.Should().Be(1);
		band.Timestamps.Should().Equal(page.FromUtc, page.ToUtc);
	}

	[AvaloniaFact]
	public async Task ADayPageReadsTheWorkingHoursAtTheStripWidth()
	{
		using var stand = await WithWorkingDaysAsync(days: 183, stripWidth: 1500.0);
		var day = new DateOnly(2025, 10, 15);
		var readsBefore = stand.Provider.BandHistoryQueries.Count;

		stand.Navigation.SetWindow(MinimapStand.At(day, 10), MinimapStand.At(day, 11));
		stand.LandRead();

		var read = stand.Provider.BandHistoryQueries.Skip(readsBefore).Should().ContainSingle().Subject;
		(read.FromUtc, read.ToUtc).Should().Be((MinimapStand.At(day, 7), MinimapStand.At(day, 17)));
		read.TargetColumnCount.Should().Be(1500);
		((read.ToUtc - read.FromUtc) / read.TargetColumnCount).Should().Be(TimeSpan.FromSeconds(24.0));
		stand.Band!.Timestamps.Should().Equal(MinimapStand.At(day, 7), MinimapStand.At(day, 17));
	}

	[AvaloniaFact]
	public async Task TheSpansLanding_ReadsTheBandAgainOverTheWorkingHours()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveDaySpans =
			MinimapStand.WorkingDaysBetween(DateOnly.FromDateTime(_extentFirst), _wednesday);
		stand.ShowPens();
		stand.Navigation.TrackDataExtents(_extentFirst, _extentLast);
		stand.Navigation.SetWindow(MinimapStand.At(_wednesday, 10), MinimapStand.At(_wednesday, 11));
		await stand.LoadExtentAsync();
		var calendarDay = stand.Provider.BandHistoryQueries.Should()
			.ContainSingle("the spans read has not landed").Subject;
		(calendarDay.FromUtc, calendarDay.ToUtc).Should()
			.Be((MinimapStand.At(_wednesday, 0), MinimapStand.At(_wednesday.AddDays(1), 0)));

		stand.LandRead();
		stand.LandRead();

		var workingHours = stand.Provider.BandHistoryQueries.Should().HaveCount(2).And.Subject.Last();
		(workingHours.FromUtc, workingHours.ToUtc).Should()
			.Be((MinimapStand.At(_wednesday, 7), MinimapStand.At(_wednesday, 17)));
		stand.Band!.Timestamps.Should().Equal(MinimapStand.At(_wednesday, 7), MinimapStand.At(_wednesday, 17));
	}

	[AvaloniaFact]
	public async Task APanInsideThePage_ReadsNothing_AndAPanOntoTheNextPageReadsIt()
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_monday, 11));
		stand.LandRead();
		var readsBefore = stand.Provider.BandHistoryQueries.Count;
		var bandShapedBefore = stand.BandShapedReadCount;

		stand.Navigation.PanBy(TimeSpan.FromHours(1.0));
		stand.LandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(readsBefore);
		stand.BandShapedReadCount.Should().Be(bandShapedBefore, "no read over a stale page passes for the chart's");

		stand.Navigation.PanBy(TimeSpan.FromDays(1.0));
		stand.LandRead();

		var read = stand.Provider.BandHistoryQueries.Skip(readsBefore).Should().ContainSingle().Subject;
		(read.FromUtc, read.ToUtc).Should().Be((MinimapStand.At(_tuesday, 7), MinimapStand.At(_tuesday, 17)));
	}

	[AvaloniaFact]
	public async Task AWidthInsideTheSameStep_ReadsNothing_AndEveryNewCountReadsOnce()
	{
		using var stand = await WithWorkingDaysAsync();
		var provider = stand.Provider;
		var counts = new List<int>();

		foreach (var stripWidth in new[] { 1500.0, 1620.0, 1625.0, 4000.0, 9000.0, 100.0 })
		{
			var readsBefore = provider.BandHistoryQueries.Count;
			stand.ViewModel.BandFeed.SetStripWidth(stripWidth);
			stand.LandRead();
			counts.AddRange(provider.BandHistoryQueries.Skip(readsBefore).Select(read => read.TargetColumnCount));
		}

		counts.Should().Equal(1500, 1750, MinimapBandFeed.MaximumColumns, MinimapBandFeed.ColumnStep);
	}

	[AvaloniaFact]
	public void BeforeAnExtent_NeitherAPageChangeNorAWidthChangeReads()
	{
		using var stand = new MinimapStand();
		stand.ShowPens();
		stand.Navigation.TrackDataExtents(_extentFirst, _extentLast);
		var pageBefore = stand.ViewModel.Pager.Page;

		stand.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_monday, 11));
		stand.ViewModel.BandFeed.SetStripWidth(1500.0);
		stand.LandRead();

		stand.ViewModel.Pager.Page.Should().NotBe(pageBefore);
		stand.Provider.BandHistoryQueries.Should().BeEmpty();
		stand.BandShapedReadCount.Should().Be(0);
		stand.Band.Should().BeNull();
	}

	[AvaloniaFact]
	public async Task AReloadedExtent_ReadsTheBandAgain()
	{
		using var stand = await MinimapStand.WithABandAsync();

		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task ASupersededBandRead_IsCancelled()
	{
		using var stand = new MinimapStand();
		var provider = stand.Provider;
		stand.ShowPens();
		provider.GatedLayer = AggregationLayer.Minute;
		await stand.LoadExtentAsync();
		var held = provider.BandHistoryQueries.Should().ContainSingle().Subject;
		var heldGate = provider.HistoryGate;
		provider.HistoryGate = new TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>>();

		stand.Chart.SetActivePen(2).Should().BeTrue();

		held.CancellationToken.IsCancellationRequested.Should().BeTrue();
		provider.BandHistoryQueries[^1].PenIds.Should().Equal(2);
		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(2)]));
		heldGate.TrySetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(1)]));
		stand.LandRead();
		stand.LandRead();

		stand.Band!.PenId.Should().Be(2);
		stand.Panel.Entries.Should().BeEmpty("a cancelled read is no failure");
	}

	[AvaloniaFact]
	public async Task AnOutage_ReportsOnceAndLogsEveryRepeat()
	{
		var logger = new FakeLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		stand.ShowPens();
		stand.Provider.FailHistory = true;
		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Panel.Entries.Should().ContainSingle();

		// The catalogue sync's own entry tops the panel between two band reads, as it does every 5 s during an
		// outage, so a band repeat would not coalesce with the band's first entry.
		for (var repeat = 0; repeat < 2; repeat++)
		{
			stand.Panel.Report(ArchiveFailureMapper.Map(new Error("The pen catalogue read failed.")));
			stand.AdvanceToNextBandRead();
		}

		stand.Panel.Entries.Should().HaveCount(2, "the band's repeats add no entry");
		logger.Levels().Count(level => level == LogLevel.Warning).Should()
			.Be(3, "the first report logs its own warning and each repeat adds one");

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
		stand.LandRead();

		stand.ViewModel.HasExtent.Should().BeTrue();
		stand.Band.Should().BeNull();
		stand.ViewModel.BandFeed.BandColor.Should().BeNull();
		stand.Provider.BandHistoryQueries.Should().BeEmpty();
		stand.BandShapedReadCount.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task HidingTheDrawnPen_ClearsTheBandAndStopsTheReads()
	{
		using var stand = new MinimapStand();
		stand.Provider.Pens = [stand.Provider.Pens[0]];
		stand.ShowPens();
		await stand.LoadExtentAsync();
		stand.LandRead();
		stand.Band.Should().NotBeNull();

		stand.Chart.SetPenVisibility(1, isVisible: false);
		stand.LandRead();
		stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(2.0).Ticks);

		stand.Chart.DrawnPenId.Should().BeNull();
		stand.Band.Should().BeNull();
		stand.Provider.BandHistoryQueries.Should().ContainSingle();
		stand.BandShapedReadCount.Should().Be(1, "no band read of the hidden pen passes for the chart's");

		stand.Chart.SetPenVisibility(1, isVisible: true);
		stand.LandRead();

		stand.Provider.BandHistoryQueries.Should().HaveCount(2, "showing the pen again reads its band once");
		stand.Band.Should().NotBeNull();
	}

	[AvaloniaFact]
	public async Task ADrawnPenIdRaiseWithTheSameId_IssuesNoRead()
	{
		using var stand = await MinimapStand.WithABandAsync();

		var bandShapedBefore = stand.BandShapedReadCount;

		stand.Chart.SetPenVisibility(2, isVisible: false);
		stand.LandRead();

		stand.Chart.DrawnPenId.Should().Be(1);
		stand.Provider.BandHistoryQueries.Should().ContainSingle();
		stand.BandShapedReadCount.Should().Be(bandShapedBefore);
	}

	[AvaloniaFact]
	public async Task TheNextRead_LandsTheReadDelayAfterTheLastOneLanded()
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_monday, 11));
		stand.LandRead();
		var readsBefore = stand.Provider.BandHistoryQueries.Count;
		var delay = MinimapBandFeed.NextReadDelay(stand.PageWidth);
		delay.Should().Be(TimeSpan.FromSeconds(36.0), "a thousandth of the 10 h working day");

		stand.Scheduler.AdvanceBy(delay.Ticks - 1);
		stand.Provider.BandHistoryQueries.Should().HaveCount(readsBefore);

		stand.Scheduler.AdvanceBy(1);
		stand.Provider.BandHistoryQueries.Should().HaveCount(readsBefore + 1);
	}

	[AvaloniaFact]
	public async Task AThrowingRead_ReportsAndTheNextScheduledReadStillRuns()
	{
		using var stand = new MinimapStand();
		var thrown = new InvalidOperationException("the band read threw");
		stand.ShowPens();
		stand.Provider.HistoryReadException = thrown;
		await stand.LoadExtentAsync();
		stand.LandRead();

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
		stand.LandRead();

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
		stand.LandRead();

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
		provider.GatedLayer = AggregationLayer.Minute;

		stand.Chart.SetActivePen(2).Should().BeTrue();
		stand.LandRead();
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
		var delay = MinimapBandFeed.NextReadDelay(stand.PageWidth);
		provider.GatedLayer = AggregationLayer.Minute;
		await stand.LoadExtentAsync();
		var held = provider.BandHistoryQueries.Should().HaveCount(2).And.Subject.Last();

		stand.Scheduler.AdvanceBy(delay.Ticks);

		provider.BandHistoryQueries.Should().HaveCount(2, "the requested read replaced the scheduled one");
		held.CancellationToken.IsCancellationRequested.Should().BeFalse();

		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>([Envelope(1)]));
		stand.LandRead();
		stand.Scheduler.AdvanceBy(delay.Ticks - 1);
		provider.BandHistoryQueries.Should().HaveCount(2);

		stand.Scheduler.AdvanceBy(1);
		provider.BandHistoryQueries.Should().HaveCount(3, "the next read lands one delay after the held one");
	}

	[AvaloniaFact]
	public async Task ARepeatedThrowingRead_LogsItsExceptionAtWarning()
	{
		var logger = new FakeLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		var thrown = new InvalidOperationException("the band read threw");
		stand.ShowPens();
		stand.Provider.HistoryReadException = thrown;
		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.AdvanceToNextBandRead();

		logger.Levels().Should().Contain(LogLevel.Warning);
		logger.Collector.GetSnapshot().Last(record => record.Level == LogLevel.Warning).Exception.Should().BeSameAs(
			thrown);
	}

	// 600 min of working day against a raw ceiling of 562.5 min at 2250 columns, 618.75 min with the margin.
	[AvaloniaTheory]
	[InlineData(2500.0, AggregationLayer.Raw)]
	[InlineData(250.0, AggregationLayer.Minute)]
	public async Task AStripNarrowedJustPastTheRawCeiling_KeepsTheLayerItCameFrom(
		double widthBefore,
		AggregationLayer expectedLayer)
	{
		using var stand = await WithWorkingDaysAsync();
		stand.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_monday, 11));
		stand.ViewModel.BandFeed.SetStripWidth(widthBefore);
		stand.LandRead();
		stand.Provider.BandHistoryQueries[^1].Layer.Should().Be(expectedLayer);

		stand.ViewModel.BandFeed.SetStripWidth(2250.0);
		stand.LandRead();

		var read = stand.Provider.BandHistoryQueries[^1];
		read.TargetColumnCount.Should().Be(2250);
		read.Layer.Should().Be(expectedLayer, "the band's hysteresis holds the layer it read last");
	}

	[AvaloniaFact]
	public async Task AWeekPageAtTheFloorCount_ReadsTheHourLayer()
	{
		using var stand = await WithWorkingDaysAsync();

		stand.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_wednesday, 10));
		stand.LandRead();

		var read = stand.Provider.BandHistoryQueries[^1];
		(read.FromUtc, read.ToUtc).Should().Be((MinimapStand.At(_monday, 0), MinimapStand.At(_monday.AddDays(7), 0)));
		read.Layer.Should().Be(AggregationLayer.Hour);
	}

	// Each page sits inside its layer's one-pen ceiling at its column count and past half of it, where the
	// chart's pen count would move the band to the next coarser layer.
	[AvaloniaTheory]
	[InlineData(false, 2500.0, AggregationLayer.Raw)]
	[InlineData(true, 1000.0, AggregationLayer.Minute)]
	[InlineData(true, 250.0, AggregationLayer.Hour)]
	public async Task WithTwiceTheReferencePenCountShown_TheBandKeepsTheOnePenLadder(
		bool isWeekPage,
		double stripWidth,
		AggregationLayer expectedLayer)
	{
		IReadOnlyList<Pen> pens =
		[
			.. Enumerable.Range(1, 2 * ChartNavigationController.ReferencePenCount)
				.Select(penId => new Pen(penId, $"Pen {penId}", ["Group A"], "#ff0000"))
		];
		using var stand = await WithWorkingDaysAsync(stripWidth: stripWidth, pens: pens);

		stand.Navigation.SetWindow(
			MinimapStand.At(_monday, 10),
			isWeekPage ? MinimapStand.At(_wednesday, 10) : MinimapStand.At(_monday, 11));
		stand.LandRead();

		stand.Provider.BandHistoryQueries[^1].Layer.Should().Be(expectedLayer);
	}

	[AvaloniaTheory]
	[InlineData(false)]
	[InlineData(true)]
	public async Task AChartReadAtAMultipleOfTheBandStep_IsNoBandRead(bool showsTheDrawnPenAlone)
	{
		using var stand = await MinimapStand.WithABandAsync();
		if (showsTheDrawnPenAlone)
		{
			stand.Chart.SetPenVisibility(2, isVisible: false);
		}

		var bandReads = stand.Provider.BandHistoryQueries;

		stand.Chart.ReportDataAreaWidth(500.0);
		stand.Scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		var page = stand.ViewModel.Pager.Page;
		var chartRead = stand.Provider.ChartHistoryQueries[^1];
		chartRead.PenIds.Should().Equal(showsTheDrawnPenAlone ? [1] : [1, 2]);
		chartRead.TargetColumnCount.Should().BeOneOf(500, 1000, 1500);
		(chartRead.FromUtc, chartRead.ToUtc).Should().NotBe((page.FromUtc, page.ToUtc));
		stand.Provider.BandHistoryQueries.Should().Equal(bandReads);
		stand.Provider.BandHistoryQueries.Should().AllSatisfy(read =>
		{
			read.PenIds.Should().Equal(1);
			(read.FromUtc, read.ToUtc).Should().Be((page.FromUtc, page.ToUtc));
		});
	}

	[Theory]
	[InlineData(0.0, 250)]
	[InlineData(124.0, 250)]
	[InlineData(375.0, 500)]
	[InlineData(1500.0, 1500)]
	[InlineData(1620.0, 1500)]
	[InlineData(1625.0, 1750)]
	[InlineData(2624.0, 2500)]
	[InlineData(9000.0, 2500)]
	public void ColumnsForWidth_RoundsToTheNearestStepBetweenTheFloorAndTheCap(double stripWidth, int expectedColumns)
	{
		MinimapBandFeed.ColumnsForWidth(stripWidth).Should().Be(expectedColumns);
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

	/// <summary>Weekdays 07:00-17:00 through the extent's last day, the spans landed and the band read.</summary>
	private static async Task<MinimapStand> WithWorkingDaysAsync(
		int days = 7,
		double stripWidth = 0.0,
		IReadOnlyList<Pen>? pens = null)
	{
		var stand = new MinimapStand();
		var firstUtc = _extentLast - TimeSpan.FromDays(days);
		stand.Provider.ArchiveFirstUtc = firstUtc;
		stand.Provider.ArchiveDaySpans = MinimapStand.WorkingDaysBetween(DateOnly.FromDateTime(firstUtc), _wednesday);
		stand.Provider.Pens = pens ?? stand.Provider.Pens;
		stand.ShowPens();
		stand.ViewModel.BandFeed.SetStripWidth(stripWidth);
		stand.Navigation.TrackDataExtents(firstUtc, _extentLast);
		await stand.LoadExtentAsync();
		stand.LandRead();
		stand.LandRead();

		return stand;
	}

	private static PenHistoryEnvelope Envelope(int penId)
	{
		return new PenHistoryEnvelope(penId, [_extentFirst, _extentLast], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0]);
	}
}
