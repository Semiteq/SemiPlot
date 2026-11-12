using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Tests.Unit.UI.Messages;
using SemiPlot.Tests.Unit.UI.PenEditor;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class ArchivePresenceFeedTests
{
	private static readonly DateOnly _extentFirstDay = new(2025, 12, 25);
	private static readonly DateOnly _extentLastDay = new(2026, 1, 1);
	private static readonly DateOnly _nextDay = new(2026, 1, 2);
	private static readonly ArchiveDaySpan _lastWorkingDay = Span(new DateOnly(2025, 12, 31), 7, 17);
	private static readonly ArchiveDaySpan _extentLastSpan =
		new(_extentLastDay, MinimapStand.ExtentLast, MinimapStand.ExtentLast);

	[AvaloniaFact]
	public async Task AnAppliedExtent_ReadsTheSpansOfEveryDayItCovers()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveDaySpans =
		[
			Span(new DateOnly(2025, 12, 24), 7, 17),
			Span(new DateOnly(2025, 12, 29), 7, 17),
			Span(new DateOnly(2025, 12, 30), 8, 16)
		];

		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Provider.DaySpansRanges.Should().Equal((_extentFirstDay, _extentLastDay));
		stand.Spans.Should().Equal(Span(new DateOnly(2025, 12, 29), 7, 17), Span(new DateOnly(2025, 12, 30), 8, 16));
	}

	[AvaloniaFact]
	public async Task TheReadDays_AreTheArchiveZonesWallClockDays()
	{
		using var stand = new MinimapStand(archiveZone: TimeZoneInfo.FindSystemTimeZoneById("America/New_York"));

		await stand.LoadExtentAsync();

		stand.Provider.DaySpansRanges.Should().Equal((new DateOnly(2025, 12, 24), new DateOnly(2025, 12, 31)));
	}

	[AvaloniaFact]
	public void ANewestSampleWithNoExtent_ReadsNothing()
	{
		using var stand = new MinimapStand();

		Sample(stand, At(_nextDay, 6));

		stand.Provider.DaySpansRanges.Should().BeEmpty();
		stand.Spans.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ANewDay_IsReadFromTheLastCompleteDay_AndASampleWithinADayReadsNothing()
	{
		using var stand = await WithSpansAsync(new MinimapStand());
		Sample(stand, At(_extentLastDay, 6));
		Sample(stand, At(_extentLastDay, 12));
		stand.Provider.DaySpansRanges.Should().ContainSingle();
		var finishedDay = new ArchiveDaySpan(_extentLastDay, MinimapStand.ExtentLast, At(_extentLastDay, 23));
		var newDay = new ArchiveDaySpan(_nextDay, At(_nextDay, 0, 1), At(_nextDay, 0, 1));
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay, finishedDay, newDay];

		Sample(stand, At(_nextDay, 0, 1));
		stand.LandRead();
		Sample(stand, At(_nextDay, 6));

		stand.Provider.DaySpansRanges.Should().Equal((_extentFirstDay, _extentLastDay), (_extentLastDay, _nextDay));
		stand.Spans.Should().Equal(_lastWorkingDay, finishedDay, newDay with { LastUtc = At(_nextDay, 6) });
	}

	[AvaloniaFact]
	public async Task ADayRollover_FollowsTheArchiveZonesMidnight()
	{
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		using var stand = await WithSpansAsync(new MinimapStand(archiveZone: berlin));

		Sample(stand, At(_extentLastDay, 22, 30));
		stand.Provider.DaySpansRanges.Should().ContainSingle("22:30 UTC is 23:30 of the same Berlin day");

		Sample(stand, At(_extentLastDay, 23, 30));
		stand.Provider.DaySpansRanges.Should().Equal((_extentFirstDay, _extentLastDay), (_extentLastDay, _nextDay));
	}

	[AvaloniaFact]
	public async Task TheNewestDay_FollowsTheNewestSample()
	{
		using var stand = await WithSpansAsync(new MinimapStand());

		Sample(stand, At(_extentLastDay, 0, 5));

		stand.Spans.Should().Equal(_lastWorkingDay, _extentLastSpan with { LastUtc = At(_extentLastDay, 0, 5) });
		stand.Provider.DaySpansRanges.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task ASampleSeenBeforeTheReadLands_ExtendsTheLandedNewestDay()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay, _extentLastSpan];
		Sample(stand, At(_extentLastDay, 0, 5));

		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Spans.Should().Equal(_lastWorkingDay, _extentLastSpan with { LastUtc = At(_extentLastDay, 0, 5) });
	}

	[AvaloniaFact]
	public async Task SamplesOnANewDayWhileItsReadIsInFlight_ReadOnce_AndTheOlderAnswerKeepsTheLiveDay()
	{
		using var stand = await WithSpansAsync(new MinimapStand());
		stand.Provider.GateDaySpans = true;

		Sample(stand, At(_nextDay, 0, 1));
		Sample(stand, At(_nextDay, 0, 2));
		Sample(stand, At(_nextDay, 0, 3));
		stand.Provider.DaySpansRanges.Should().Equal((_extentFirstDay, _extentLastDay), (_extentLastDay, _nextDay));
		stand.Spans.Should().Equal(
			[_lastWorkingDay, _extentLastSpan, new ArchiveDaySpan(_nextDay, At(_nextDay, 0, 1), At(_nextDay, 0, 3))],
			"the new day holds its live samples until its read lands");

		var newDay = new ArchiveDaySpan(_nextDay, At(_nextDay, 0, 1), At(_nextDay, 0, 1));
		stand.Provider.DaySpansGate.SetResult(Result.Ok<IReadOnlyList<ArchiveDaySpan>>([_extentLastSpan, newDay]));
		stand.LandRead();

		stand.Spans.Should().Equal(_lastWorkingDay, _extentLastSpan, newDay with { LastUtc = At(_nextDay, 0, 3) });
	}

	[AvaloniaFact]
	public async Task ANewerExtent_CancelsTheReadInFlight_AndReadsItsOwnDays()
	{
		using var stand = new MinimapStand();
		stand.Provider.GateDaySpans = true;
		await stand.LoadExtentAsync();
		var firstRead = stand.Provider.LastDaySpansCancellationToken;
		stand.Provider.ArchiveFirstUtc = MinimapStand.ExtentFirst.AddDays(-1.0);

		await stand.LoadExtentAsync();

		firstRead.IsCancellationRequested.Should().BeTrue();
		stand.Provider.DaySpansRanges.Should().Equal(
			(_extentFirstDay, _extentLastDay), (_extentFirstDay.AddDays(-1), _extentLastDay));

		stand.LandRead();
		stand.LandRead();

		stand.Panel.Entries.Should().BeEmpty("a cancelled read is no failure");
	}

	[AvaloniaFact]
	public async Task AnExtentThatLostItsOldestDays_DropsTheirSpans()
	{
		using var stand = await WithSpansAsync(new MinimapStand());
		stand.Provider.ArchiveFirstUtc = MinimapStand.ExtentLast;

		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Provider.DaySpansRanges.Should()
			.Equal((_extentFirstDay, _extentLastDay), (_extentLastDay, _extentLastDay));
		stand.Spans.Should().Equal(_extentLastSpan);
	}

	[AvaloniaFact]
	public async Task AThrowingRead_ReportsOnce_AndTheNextRetryStillReads()
	{
		using var stand = new MinimapStand();
		var thrown = new InvalidOperationException("the spans read threw");
		stand.Provider.DaySpansReadException = thrown;
		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Panel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(new ExceptionalError(thrown)));

		stand.Provider.DaySpansReadException = null;
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay];
		Sample(stand, MinimapStand.ExtentLast + TimeSpan.FromSeconds(1.0));
		stand.LandRead();

		stand.Provider.DaySpansRanges.Should().HaveCount(2);
		stand.Spans.Should().Equal(_lastWorkingDay);
		stand.Panel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AnAnswerLandingAfterANewerRequest_IsDiscarded()
	{
		using var stand = new MinimapStand();
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay];
		await stand.LoadExtentAsync();
		stand.Provider.FailDaySpans = true;

		stand.ViewModel.PresenceFeed.ReadExtent(MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		stand.LandRead();
		stand.Spans.Should().BeEmpty("the first answer was already queued when the newer, failing read replaced it");

		// ObserveOn delivers one queued answer per TestScheduler tick.
		stand.LandRead();

		stand.Panel.Entries.Should().ContainSingle();
		stand.Spans.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ASampleThatLeavesTheRightBound_StillRetriesAFailedRead()
	{
		using var stand = new MinimapStand();
		stand.Provider.FailDaySpans = true;
		await stand.LoadExtentAsync();
		stand.LandRead();

		Sample(stand, MinimapStand.ExtentLast - TimeSpan.FromHours(1.0));

		stand.ViewModel.ExtentLast.Should().Be(MinimapStand.ExtentLast);
		stand.Provider.DaySpansRanges.Should().Equal(
			(_extentFirstDay, _extentLastDay), (_extentFirstDay, _extentLastDay));
	}

	[AvaloniaFact]
	public async Task AFailedNewDayRead_KeepsTheSpans_AndTheRetryReadsFromTheLastCompleteDay()
	{
		using var stand = await WithSpansAsync(new MinimapStand());
		stand.Provider.FailDaySpans = true;
		Sample(stand, At(_nextDay, 0, 1));
		stand.LandRead();
		stand.Spans.Should().Equal(
			[_lastWorkingDay, _extentLastSpan, new ArchiveDaySpan(_nextDay, At(_nextDay, 0, 1), At(_nextDay, 0, 1))],
			"the failed read keeps the spans, and the new day holds its live sample");
		var newDay = new ArchiveDaySpan(_nextDay, At(_nextDay, 0, 1), At(_nextDay, 0, 2));
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay, _extentLastSpan, newDay];
		stand.Provider.FailDaySpans = false;

		Sample(stand, At(_nextDay, 0, 2));
		stand.LandRead();

		stand.Provider.DaySpansRanges.Should().Equal(
			(_extentFirstDay, _extentLastDay), (_extentLastDay, _nextDay), (_extentLastDay, _nextDay));
		stand.Spans.Should().Equal(_lastWorkingDay, _extentLastSpan, newDay);
	}

	[AvaloniaFact]
	public async Task ASteadyFailure_ReachesTheMessagePanelOnce_AndRetriesTheWholeExtentOnceAMinute()
	{
		var logger = new FakeLogger<MinimapViewModel>();
		using var stand = new MinimapStand(logger);
		var provider = stand.Provider;
		provider.FailDaySpans = true;
		await stand.LoadExtentAsync();
		stand.LandRead();
		stand.Panel.Entries.Should().ContainSingle()
			.Which.View.Should().Be(ArchiveFailureMapper.Map(
				FakePenCatalogueEditor.Refusal(ArchiveFault.ReadFailed, "42601")));
		var warningsAtStart = logger.Levels().Count(level => level == LogLevel.Warning);

		// Another entry tops the panel, so a repeat of the spans entry would not coalesce with it.
		stand.Panel.Report(ArchiveFailureMapper.Map(new Error("The pen catalogue read failed.")));
		for (var second = 1; second <= 170; second++)
		{
			stand.Scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
			Sample(stand, MinimapStand.ExtentLast + TimeSpan.FromSeconds(second));
		}

		stand.LandRead();

		provider.DaySpansRanges.Should()
			.HaveCount(4, "the read at start, one on the first sample and one a minute after each")
			.And.AllBeEquivalentTo((_extentFirstDay, _extentLastDay));
		stand.Panel.Entries.Should().HaveCount(2, "the retries add no entry");
		logger.Levels().Count(level => level == LogLevel.Warning).Should().Be(warningsAtStart + 3);

		provider.FailDaySpans = false;
		provider.ArchiveDaySpans = [_lastWorkingDay];
		stand.Scheduler.AdvanceBy(TimeSpan.FromMinutes(1.0).Ticks);
		Sample(stand, MinimapStand.ExtentLast + TimeSpan.FromMinutes(5.0));
		stand.LandRead();
		stand.Spans.Should().Equal(_lastWorkingDay);
		provider.FailDaySpans = true;
		await stand.LoadExtentAsync();
		stand.LandRead();

		stand.Panel.Entries.Should().HaveCount(3, "a success arms the report again");
		stand.Spans.Should().Equal([_lastWorkingDay], "a failed read keeps the spans it had");
	}

	private static async Task<MinimapStand> WithSpansAsync(MinimapStand stand)
	{
		stand.Provider.ArchiveDaySpans = [_lastWorkingDay, _extentLastSpan];
		await stand.LoadExtentAsync();
		stand.LandRead();
		stand.Spans.Should().Equal(_lastWorkingDay, _extentLastSpan);

		return stand;
	}

	private static void Sample(MinimapStand stand, DateTime sampleUtc)
	{
		stand.Navigation.OnLiveEdge(sampleUtc, sampleUtc);
	}

	private static DateTime At(DateOnly day, int hour, int minute = 0)
	{
		return day.ToDateTime(new TimeOnly(hour, minute), DateTimeKind.Utc);
	}

	private static ArchiveDaySpan Span(DateOnly day, int firstHour, int lastHour)
	{
		return new ArchiveDaySpan(day, At(day, firstHour), At(day, lastHour));
	}
}
