using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinimapPageTests
{
	private static readonly TimeZoneInfo _berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
	private static readonly DayHours _officeHours = new(7, 17);

	// Weekdays of two weeks, a nine-day gap from Saturday 17 to Sunday 25 January, then a third week.
	private static readonly IReadOnlyList<ArchiveDaySpan> _weeksWithMaintenance =
	[
		.. Weekdays(Day(1, 5), Day(1, 16)),
		.. Weekdays(Day(1, 26), Day(1, 30))
	];

	[Fact]
	public void WorkingHours_ArePercentilesOfFirstAndLastSamples_RoundedOutToWholeHours()
	{
		// Sorted, the second-earliest first sample is 06:40 and the second-latest last sample 17:30, the
		// nearest ranks of P5 and P95 over 22 days; the minimum, the maximum and the median all differ.
		var spans = Weekdays(Day(1, 7), Day(2, 2), first: (7, 20), last: (16, 40))
			.Append(Span(Day(1, 5), (6, 40), (16, 40)))
			.Append(Span(Day(1, 6), (7, 20), (17, 30)))
			.Append(Span(Day(1, 17), (2, 10), (23, 30)))
			.OrderBy(span => span.Day)
			.ToList();

		var hours = MinimapPage.WorkingHours(spans, _berlin);

		spans.Should().HaveCount(22);
		hours.Should().Be(new DayHours(6, 18));
	}

	[Fact]
	public void WorkingHours_KeepAnExactHour()
	{
		var spans = Weekdays(Day(1, 5), Day(2, 2), first: (7, 0), last: (17, 0));

		var hours = MinimapPage.WorkingHours(spans, _berlin);

		hours.Should().Be(new DayHours(7, 17));
	}

	[Fact]
	public void WorkingHours_OfAnArchiveRunningAroundTheClock_AreTheWholeDay()
	{
		var spans = new List<ArchiveDaySpan> { Span(Day(1, 1), (13, 0), (23, 59)) };
		for (var day = 2; day < 30; day++)
		{
			spans.Add(new ArchiveDaySpan(
				Day(1, day),
				At(1, day, 0, 0) + TimeSpan.FromSeconds(5),
				At(1, day, 23, 59) + TimeSpan.FromSeconds(55)));
		}

		spans.Add(Span(Day(1, 30), (0, 0), (10, 0)));

		var hours = MinimapPage.WorkingHours(spans, _berlin);

		spans[1].FirstUtc.Day.Should().Be(1, "the first sample after Berlin's midnight falls on the previous UTC day");
		hours.Should().Be(DayHours.FullDay);
	}

	[Fact]
	public void WorkingHours_WithoutSpans_AreTheWholeDay()
	{
		MinimapPage.WorkingHours([], _berlin).Should().Be(new DayHours(0, 24));
	}

	[Fact]
	public void WorkingHours_OfOneDay_AreThatDayRoundedOut()
	{
		var hours = MinimapPage.WorkingHours([Span(Day(1, 7), (9, 30), (11, 10))], _berlin);

		hours.Should().Be(new DayHours(9, 12));
	}

	[Fact]
	public void Snap_ADay_IsTheWorkingHoursOfThatDay()
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Day, At(1, 7, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		page.Should().Be(
			new MinimapPage(MinimapPageUnit.Day, Day(1, 7), _officeHours, At(1, 7, 7, 0), At(1, 7, 17, 0)));
	}

	[Fact]
	public void Snap_ADayWithSamplesOutsideTheHours_WidensItsOwnPageInWholeHours()
	{
		var spans = new[] { Span(Day(1, 16), (7, 12), (16, 48)), Span(Day(1, 17), (2, 10), (23, 30)) };

		var widened = MinimapPage.Snap(MinimapPageUnit.Day, At(1, 17, 12, 0), _officeHours, spans, _berlin);
		var neighbour = MinimapPage.Snap(MinimapPageUnit.Day, At(1, 16, 12, 0), _officeHours, spans, _berlin);

		widened.FromUtc.Should().Be(At(1, 17, 2, 0));
		widened.ToUtc.Should().Be(At(1, 18, 0, 0));
		widened.Hours.Should().Be(_officeHours);
		neighbour.FromUtc.Should().Be(At(1, 16, 7, 0));
		neighbour.ToUtc.Should().Be(At(1, 16, 17, 0));
	}

	[Fact]
	public void Snap_ADayWithoutSpan_IsThePlainHours()
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Day, At(1, 10, 3, 0), _officeHours, _weeksWithMaintenance, _berlin);

		page.FirstDay.Should().Be(Day(1, 10));
		page.FromUtc.Should().Be(At(1, 10, 7, 0));
		page.ToUtc.Should().Be(At(1, 10, 17, 0));
	}

	[Fact]
	public void Snap_TakesTheArchiveWallClockDay_NotTheUtcDay()
	{
		var halfPastMidnightInBerlin = new DateTime(2026, 1, 6, 23, 30, 0, DateTimeKind.Utc);

		var page = MinimapPage.Snap(
			MinimapPageUnit.Day, halfPastMidnightInBerlin, _officeHours, _weeksWithMaintenance, _berlin);

		page.FirstDay.Should().Be(Day(1, 7));
	}

	[Theory]
	[InlineData(5)]
	[InlineData(8)]
	[InlineData(11)]
	public void Snap_AWeek_RunsFromMondayToTheNextMonday(int dayOfJanuary)
	{
		var page = MinimapPage.Snap(
			MinimapPageUnit.Week, At(1, dayOfJanuary, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		page.FirstDay.Should().Be(Day(1, 5));
		page.EndDay.Should().Be(Day(1, 12));
		page.FromUtc.Should().Be(At(1, 5, 0, 0));
		page.ToUtc.Should().Be(At(1, 12, 0, 0));
	}

	[Fact]
	public void Snap_AWeekOverTheAutumnChange_RunsMondayToMondayInWallClock()
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Week, At(10, 22, 12, 0), _officeHours, [], _berlin);

		page.FromUtc.Should().Be(At(10, 19, 0, 0));
		page.ToUtc.Should().Be(At(10, 26, 0, 0));
		(page.ToUtc - page.FromUtc).Should().Be(TimeSpan.FromHours(169));
	}

	[Fact]
	public void Step_NextFromFriday_SkipsTheWeekend()
	{
		var friday = DayPage(1, 9);

		var next = MinimapPage.Step(friday, PageDirection.Next, _weeksWithMaintenance, _berlin);

		next.Should().Be(DayPage(1, 12));
	}

	[Fact]
	public void Step_PreviousFromMonday_SkipsTheWeekend()
	{
		var previous = MinimapPage.Step(DayPage(1, 12), PageDirection.Previous, _weeksWithMaintenance, _berlin);

		previous.Should().Be(DayPage(1, 9));
	}

	[Fact]
	public void Step_SkipsAMaintenanceWeek_InBothDirections()
	{
		var next = MinimapPage.Step(DayPage(1, 16), PageDirection.Next, _weeksWithMaintenance, _berlin);
		var previous = MinimapPage.Step(DayPage(1, 26), PageDirection.Previous, _weeksWithMaintenance, _berlin);

		(next?.FirstDay).Should().Be(Day(1, 26));
		(previous?.FirstDay).Should().Be(Day(1, 16));
	}

	[Fact]
	public void Step_PastEitherEndOfTheArchive_ReturnsNone()
	{
		MinimapPage.Step(DayPage(1, 5), PageDirection.Previous, _weeksWithMaintenance, _berlin).Should().BeNull();
		MinimapPage.Step(DayPage(1, 30), PageDirection.Next, _weeksWithMaintenance, _berlin).Should().BeNull();
	}

	[Fact]
	public void Step_FromADayWithoutSpan_GoesToTheNearestDaysWithSpans()
	{
		var saturday = DayPage(1, 10);

		var next = MinimapPage.Step(saturday, PageDirection.Next, _weeksWithMaintenance, _berlin);
		var previous = MinimapPage.Step(saturday, PageDirection.Previous, _weeksWithMaintenance, _berlin);

		(next?.FirstDay).Should().Be(Day(1, 12));
		(previous?.FirstDay).Should().Be(Day(1, 9));
	}

	[Fact]
	public void Step_AWeek_SkipsAWeekWithoutSpans()
	{
		var secondWeek = MinimapPage.Snap(
			MinimapPageUnit.Week, At(1, 14, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		var next = MinimapPage.Step(secondWeek, PageDirection.Next, _weeksWithMaintenance, _berlin);
		var back = MinimapPage.Step(next!.Value, PageDirection.Previous, _weeksWithMaintenance, _berlin);

		next.Value.FirstDay.Should().Be(Day(1, 26));
		next.Value.FromUtc.Should().Be(At(1, 26, 0, 0));
		next.Value.ToUtc.Should().Be(At(2, 2, 0, 0));
		back.Should().Be(secondWeek);
	}

	[Fact]
	public void Step_KeepsTheCommonHours_AndWidensTheTargetDay()
	{
		var spans = new[] { Span(Day(1, 9), (7, 12), (16, 48)), Span(Day(1, 12), (5, 40), (17, 10)) };
		var friday = MinimapPage.Snap(MinimapPageUnit.Day, At(1, 9, 12, 0), _officeHours, spans, _berlin);

		var monday = MinimapPage.Step(friday, PageDirection.Next, spans, _berlin);

		monday.Should().Be(
			new MinimapPage(MinimapPageUnit.Day, Day(1, 12), _officeHours, At(1, 12, 5, 0), At(1, 12, 18, 0)));
	}

	[Fact]
	public void Step_WithoutSpans_ReturnsNone()
	{
		MinimapPage.Step(DayPage(1, 9), PageDirection.Next, [], _berlin).Should().BeNull();
	}

	[Fact]
	public void Land_FromFridayToMonday_KeepsTheClockTime()
	{
		var window = MinimapPage.Land(At(1, 9, 10, 0), At(1, 9, 11, 0), DayPage(1, 12), _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 12, 10, 0), At(1, 12, 11, 0)));
	}

	[Fact]
	public void Land_AnOffsetHoldingNoSample_MovesToTheDaysFirstSample_KeepingTheWidth()
	{
		var window = MinimapPage.Land(At(1, 9, 2, 0), At(1, 9, 3, 30), DayPage(1, 12), _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 12, 7, 12), At(1, 12, 8, 42)));
	}

	[Fact]
	public void Land_AnOffsetTouchingTheDaysSamples_StaysAtTheOffset()
	{
		var window = MinimapPage.Land(At(1, 9, 6, 0), At(1, 9, 7, 30), DayPage(1, 12), _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 12, 6, 0), At(1, 12, 7, 30)));
	}

	[Fact]
	public void Land_OnAPageWithoutSpans_KeepsTheOffset()
	{
		var window = MinimapPage.Land(At(1, 9, 2, 0), At(1, 9, 3, 0), DayPage(1, 10), _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 10, 2, 0), At(1, 10, 3, 0)));
	}

	[Fact]
	public void Land_OnAWeek_KeepsTheWeekdayAndClockTime()
	{
		var nextWeek = MinimapPage.Snap(
			MinimapPageUnit.Week, At(1, 12, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		var window = MinimapPage.Land(At(1, 9, 10, 0), At(1, 9, 11, 0), nextWeek, _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 16, 10, 0), At(1, 16, 11, 0)));
	}

	[Fact]
	public void Land_OnAWeekWhoseOffsetHoldsNoSample_MovesToTheWeeksFirstSample()
	{
		var thirdWeek = MinimapPage.Snap(
			MinimapPageUnit.Week, At(1, 28, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		var window = MinimapPage.Land(At(1, 10, 10, 0), At(1, 10, 14, 0), thirdWeek, _weeksWithMaintenance, _berlin);

		window.Should().Be((At(1, 26, 7, 12), At(1, 26, 11, 12)));
	}

	[Fact]
	public void Land_OverTheAutumnChange_KeepsTheWallClockTime()
	{
		var spans = new[] { Span(Day(10, 23), (7, 0), (17, 0)), Span(Day(10, 26), (7, 0), (17, 0)) };
		var monday = MinimapPage.Snap(MinimapPageUnit.Day, At(10, 26, 12, 0), _officeHours, spans, _berlin);

		var window = MinimapPage.Land(At(10, 23, 10, 0), At(10, 23, 11, 0), monday, spans, _berlin);

		window.Should().Be((At(10, 26, 10, 0), At(10, 26, 11, 0)));
		window.FromUtc.Should().Be(new DateTime(2026, 10, 26, 9, 0, 0, DateTimeKind.Utc));
	}

	[Fact]
	public void Land_OnAnHourTheSpringChangeSkips_TakesTheBaseOffset()
	{
		var springSunday = MinimapPage.Snap(MinimapPageUnit.Day, At(3, 29, 12, 0), _officeHours, [], _berlin);

		var (fromUtc, toUtc) = MinimapPage.Land(At(3, 27, 2, 30), At(3, 27, 3, 30), springSunday, [], _berlin);

		fromUtc.Should().Be(new DateTime(2026, 3, 29, 1, 30, 0, DateTimeKind.Utc));
		toUtc.Should().Be(new DateTime(2026, 3, 29, 2, 30, 0, DateTimeKind.Utc));
	}

	[Fact]
	public void Snap_ADayStartingOnAnHourTheSpringChangeSkips_TakesTheBaseOffset()
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Day, At(3, 29, 12, 0), new DayHours(2, 17), [], _berlin);

		page.FromUtc.Should().Be(new DateTime(2026, 3, 29, 1, 0, 0, DateTimeKind.Utc));
		page.ToUtc.Should().Be(At(3, 29, 17, 0));
	}

	[Fact]
	public void SnapToData_OnASaturday_TakesTheNearerFriday()
	{
		var span = MinimapPage.SnapToData(At(1, 10, 10, 0), _weeksWithMaintenance, _berlin);

		(span?.Day).Should().Be(Day(1, 9));
	}

	[Fact]
	public void SnapToData_OnASundayEvening_TakesTheNearerMonday()
	{
		var span = MinimapPage.SnapToData(At(1, 11, 18, 0), _weeksWithMaintenance, _berlin);

		(span?.Day).Should().Be(Day(1, 12));
	}

	[Fact]
	public void SnapToData_HalfwayBetweenTwoDays_TakesTheEarlierDay()
	{
		var spans = new[] { Span(Day(1, 9), (7, 0), (17, 0)), Span(Day(1, 12), (7, 0), (17, 0)) };

		var span = MinimapPage.SnapToData(At(1, 11, 0, 0), spans, _berlin);

		(span?.Day).Should().Be(Day(1, 9));
	}

	[Fact]
	public void SnapToData_OnADayWithSpan_TakesThatDayEvenWhenTheNextLiesNearer()
	{
		var spans = new[] { Span(Day(1, 7), (7, 0), (9, 0)), Span(Day(1, 8), (7, 0), (17, 0)) };

		var span = MinimapPage.SnapToData(At(1, 7, 23, 0), spans, _berlin);

		(span?.Day).Should().Be(Day(1, 7));
	}

	[Fact]
	public void SnapToData_OutsideTheArchive_TakesTheEdgeDay()
	{
		(MinimapPage.SnapToData(At(1, 1, 12, 0), _weeksWithMaintenance, _berlin)?.Day).Should().Be(Day(1, 5));
		(MinimapPage.SnapToData(At(2, 7, 12, 0), _weeksWithMaintenance, _berlin)?.Day).Should().Be(Day(1, 30));
	}

	[Fact]
	public void SnapToData_WithoutSpans_ReturnsNone()
	{
		MinimapPage.SnapToData(At(1, 10, 10, 0), [], _berlin).Should().BeNull();
	}

	[Theory]
	[InlineData(1)]
	[InlineData(15)]
	[InlineData(31)]
	public void Snap_AMonth_RunsFromTheFirstToTheNextFirst(int dayOfJanuary)
	{
		var page = MinimapPage.Snap(
			MinimapPageUnit.Month, At(1, dayOfJanuary, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);

		page.FirstDay.Should().Be(Day(1, 1));
		page.EndDay.Should().Be(Day(2, 1));
		page.FromUtc.Should().Be(At(1, 1, 0, 0));
		page.ToUtc.Should().Be(At(2, 1, 0, 0));
	}

	[Fact]
	public void Snap_AMonthOverTheAutumnChange_RunsFirstToFirstInWallClock()
	{
		var page = MinimapPage.Snap(MinimapPageUnit.Month, At(10, 22, 12, 0), _officeHours, [], _berlin);

		page.FromUtc.Should().Be(At(10, 1, 0, 0));
		page.ToUtc.Should().Be(At(11, 1, 0, 0));
		(page.ToUtc - page.FromUtc).Should().Be(TimeSpan.FromHours((31 * 24) + 1));
	}

	[Fact]
	public void Step_AMonth_SkipsAMonthWithoutSpans_AndStopsPastTheLast()
	{
		var spans = new[] { Span(Day(1, 20), (8, 0), (16, 0)), Span(Day(4, 2), (8, 0), (16, 0)) };
		var january = MinimapPage.Snap(MinimapPageUnit.Month, At(1, 20, 12, 0), _officeHours, spans, _berlin);

		var next = MinimapPage.Step(january, PageDirection.Next, spans, _berlin);

		next?.Unit.Should().Be(MinimapPageUnit.Month);
		next?.FirstDay.Should().Be(Day(4, 1), "February and March hold no span");
		MinimapPage.Step(next!.Value, PageDirection.Next, spans, _berlin).Should().BeNull();
		MinimapPage.Step(next.Value, PageDirection.Previous, spans, _berlin)?.FirstDay.Should().Be(Day(1, 1));
	}

	[Fact]
	public void Land_OnAMonth_KeepsTheDayOfTheMonthAndClockTime()
	{
		var spans = new[] { Span(Day(1, 12), (8, 0), (16, 0)), Span(Day(2, 12), (8, 0), (16, 0)) };
		var february = MinimapPage.Snap(MinimapPageUnit.Month, At(2, 12, 12, 0), _officeHours, spans, _berlin);

		var landed = MinimapPage.Land(At(1, 10, 9, 0), At(1, 16, 9, 0), february, spans, _berlin);

		landed.Should().Be((At(2, 10, 9, 0), At(2, 16, 9, 0)));
	}

	[Fact]
	public void Holding_AWindowInsideTheDaysHours_IsTheDayPage()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 9, 0), At(1, 7, 15, 0));

		page.Should().Be(DayPage(1, 7));
	}

	[Fact]
	public void Holding_ATwelveHourWindow_OnTenWorkingHours_IsTheWeekPage()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 6, 0), At(1, 7, 18, 0));

		page.Unit.Should().Be(MinimapPageUnit.Week, "the Day page is its working hours, 07:00 to 17:00");
		page.FirstDay.Should().Be(Day(1, 5));
	}

	[Fact]
	public void Holding_AnHourBeforeTheWorkingHours_IsTheWeekPage()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 5, 0), At(1, 7, 6, 0));

		page.Unit.Should().Be(MinimapPageUnit.Week);
	}

	[Fact]
	public void Holding_AWindowAcrossAWeekend_IsTheMonthPage()
	{
		var page = HoldingOnTheWeeks(At(1, 9, 9, 0), At(1, 13, 9, 0));

		page.Should().Be(new MinimapPage(MinimapPageUnit.Month, Day(1, 1), _officeHours, At(1, 1, 0, 0), At(2, 1, 0, 0)));
	}

	[Fact]
	public void Holding_AWindowWiderThanAMonth_IsTheMonthOfItsStart()
	{
		var page = HoldingOnTheWeeks(At(1, 20, 0, 0), At(3, 20, 0, 0));

		page.Unit.Should().Be(MinimapPageUnit.Month);
		page.FirstDay.Should().Be(Day(1, 1));
	}

	[Fact]
	public void Holding_AWindowAcrossTheAutumnChange_IsTheWeekThatHoldsTheRepeatedHour()
	{
		var page = MinimapPage.Holding(At(10, 24, 12, 0), At(10, 25, 12, 0), null, _officeHours, [], _berlin);

		page.Unit.Should().Be(MinimapPageUnit.Week);
		page.FromUtc.Should().Be(At(10, 19, 0, 0));
		page.ToUtc.Should().Be(At(10, 26, 0, 0));
	}

	[Fact]
	public void Holding_ASlidingWindowPastTheDaysEnd_KeepsTheDayPage_WhileTheNewestSampleLiesOnIt()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 16, 30), At(1, 7, 17, 30), newestSampleUtc: At(1, 7, 16, 48));

		page.Should().Be(DayPage(1, 7), "the window holds no sample past 16:48, inside the 07:00-17:00 page");
	}

	[Fact]
	public void Holding_AWindowWhollyPastTheNewestSample_KeepsTheDayPageOfItsStart()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 19, 0), At(1, 7, 20, 0), newestSampleUtc: At(1, 7, 16, 48));

		page.Should().Be(DayPage(1, 7), "the evening after the shift holds no sample past the day's last");
	}

	[Fact]
	public void Holding_AHistoricalWindowWiderThanTheDay_IsStillTheWeekPage()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 6, 0), At(1, 7, 18, 0), newestSampleUtc: At(1, 30, 16, 48));

		page.Unit.Should().Be(MinimapPageUnit.Week, "a newest sample after the window clips nothing");
	}

	[Fact]
	public void Holding_WithNoNewestSample_JudgesTheWholeWindow()
	{
		var page = HoldingOnTheWeeks(At(1, 7, 16, 30), At(1, 7, 17, 30));

		page.Unit.Should().Be(MinimapPageUnit.Week, "the window's end passes the 17:00 page end");
	}

	private static MinimapPage HoldingOnTheWeeks(
		DateTime windowFromUtc,
		DateTime windowToUtc,
		DateTime? newestSampleUtc = null)
	{
		return MinimapPage.Holding(
			windowFromUtc, windowToUtc, newestSampleUtc, _officeHours, _weeksWithMaintenance, _berlin);
	}

	private static MinimapPage DayPage(int month, int day)
	{
		return MinimapPage.Snap(
			MinimapPageUnit.Day, At(month, day, 12, 0), _officeHours, _weeksWithMaintenance, _berlin);
	}

	private static List<ArchiveDaySpan> Weekdays(
		DateOnly firstDay,
		DateOnly lastDay,
		(int Hour, int Minute)? first = null,
		(int Hour, int Minute)? last = null)
	{
		var spans = new List<ArchiveDaySpan>();
		for (var day = firstDay; day <= lastDay; day = day.AddDays(1))
		{
			if (day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
			{
				spans.Add(Span(day, first ?? (7, 12), last ?? (16, 48)));
			}
		}

		return spans;
	}

	private static ArchiveDaySpan Span(DateOnly day, (int Hour, int Minute) first, (int Hour, int Minute) last)
	{
		return new ArchiveDaySpan(
			day,
			At(day.Month, day.Day, first.Hour, first.Minute),
			At(day.Month, day.Day, last.Hour, last.Minute));
	}

	private static DateOnly Day(int month, int day)
	{
		return new DateOnly(2026, month, day);
	}

	private static DateTime At(int month, int day, int hour, int minute)
	{
		return TimeZoneInfo.ConvertTimeToUtc(new DateTime(2026, month, day, hour, minute, 0), _berlin);
	}
}
