using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

using static SemiPlot.Tests.Unit.Core.Trends.BerlinClock;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DayCalendarTests
{
	// 1 100 px right of the 2 px outline inset at the left end.
	private const double RowWidth = 1102.0;

	private static readonly DayHours _officeHours = new(7, 17);

	[Theory]
	[InlineData(2, 20.0)]
	[InlineData(21, 20.0)]
	[InlineData(183, 6.0)]
	public void Over_TakesTheWholePitchThatFits_CappedAtTwentyPixels(int dayCount, double pitch)
	{
		var calendar = Calendar(Day(1, 5), dayCount, RowWidth);

		calendar.DayCount.Should().Be(dayCount);
		calendar.Pitch.Should().Be(pitch, "two days and three weeks cap at 20 px, six months floor 1100 / 183");
		calendar.Gap.Should().Be(1.0);
		calendar.CellWidth.Should().Be(pitch - 1.0);
	}

	[Theory]
	[InlineData(4.0, 1.0)]
	[InlineData(3.0, 0.0)]
	public void Gap_IsOnePixelFromAFourPixelPitch(double pitch, double gap)
	{
		var calendar = Calendar(Day(1, 5), 100, (100 * pitch) + 2.0);

		calendar.Pitch.Should().Be(pitch);
		calendar.Gap.Should().Be(gap);
		calendar.CellWidth.Should().Be(pitch - gap);
	}

	[Fact]
	public void Over_OfMoreDaysThanPixels_TakesAFractionalPitch_AndStillEndsAtTheRowsRightEnd()
	{
		var calendar = Calendar(Day(1, 5), 2200, RowWidth);

		calendar.Pitch.Should().Be(0.5);
		calendar.Gap.Should().Be(0.0);
		calendar.CellWidth.Should().Be(1.0, "a cell is one pixel column");
		(calendar.CellLeft(2199) + calendar.CellWidth).Should().Be(RowWidth);
		calendar.CellLeft(0).Should().BeGreaterThanOrEqualTo(DayCalendar.OutlineStroke - 1.0);
	}

	[Fact]
	public void Over_WithNoRoomRightOfTheInset_IsNull()
	{
		DayCalendar.Over(Day(1, 5), Day(1, 6), DayCalendar.OutlineStroke).Should().BeNull();
	}

	// Two days, three weeks and six months on one row: the last day ends at the row's right end and the cells
	// fill leftwards, one pitch apart.
	[Theory]
	[InlineData(2, 1063.0, 19.0)]
	[InlineData(21, 683.0, 19.0)]
	[InlineData(183, 5.0, 5.0)]
	public void TheCells_EndAtTheRowsRightEnd_AndFillLeftwards(int dayCount, double firstLeft, double cellWidth)
	{
		var calendar = Calendar(Day(1, 5), dayCount, RowWidth);
		var last = dayCount - 1;

		calendar.Right.Should().Be(RowWidth);
		calendar.Cell(last).Should().Be(new TrackSpan(RowWidth - cellWidth, cellWidth), "today ends the row");
		calendar.Cell(0).Should().Be(new TrackSpan(firstLeft, cellWidth));
		(calendar.CellLeft(last) - calendar.CellLeft(last - 1)).Should().Be(calendar.Pitch);
		calendar.CellLeft(0).Should().BeGreaterThanOrEqualTo(DayCalendar.OutlineStroke, "the outline keeps its inset");
	}

	[Theory]
	[InlineData(1102.0, 7)]
	[InlineData(1082.5, 7)]
	[InlineData(1082.0, 6)]
	[InlineData(962.0, 0)]
	[InlineData(942.5, 0)]
	public void DayAt_IsThePitchUnderX_ItsLeadingGapIncluded(double x, int dayIndex)
	{
		var calendar = Calendar(Day(1, 5), 8, RowWidth);

		calendar.DayAt(x).Should().Be(Day(1, 5).AddDays(dayIndex));
	}

	[Theory]
	[InlineData(942.0)]
	[InlineData(100.0)]
	[InlineData(1102.5)]
	public void DayAt_LeftOfTheFirstCellOrRightOfTheLast_IsNull(double x)
	{
		Calendar(Day(1, 5), 8, RowWidth).DayAt(x).Should().BeNull();
	}

	[Theory]
	[InlineData(7, 17, DayCoverage.Full)]
	[InlineData(7, 12, DayCoverage.Medium)]
	[InlineData(9, 16, DayCoverage.Medium)]
	[InlineData(7, 9, DayCoverage.Low)]
	[InlineData(5, 8, DayCoverage.Low)]
	[InlineData(18, 20, DayCoverage.Low)]
	public void ACell_IsGradedByTheShareOfTheWorkingHoursItsSpanCovers(
		int firstHour,
		int lastHour,
		DayCoverage expected)
	{
		var span = new ArchiveDaySpan(Day(1, 7), Berlin(1, 7, firstHour), Berlin(1, 7, lastHour));

		GradeOf(span, _officeHours).Should().Be(expected);
	}

	[Theory]
	[InlineData(0, 23, DayCoverage.Full)]
	[InlineData(0, 12, DayCoverage.Medium)]
	[InlineData(8, 12, DayCoverage.Low)]
	public void ACell_OnARoundTheClockArchive_IsGradedOverTheWholeDay(int firstHour, int lastHour, DayCoverage expected)
	{
		var span = new ArchiveDaySpan(Day(1, 7), Berlin(1, 7, firstHour), Berlin(1, 7, lastHour));

		GradeOf(span, DayHours.FullDay).Should().Be(expected);
	}

	[Fact]
	public void TodaysCell_IsGradedByItsSpanSoFar()
	{
		var today = Day(1, 7);
		var early = new ArchiveDaySpan(today, Berlin(1, 7, 7), Berlin(1, 7, 8).AddMinutes(11));
		var later = early with { LastUtc = Berlin(1, 7, 10).AddMinutes(11) };

		GradeOf(early, _officeHours).Should().Be(DayCoverage.Low, "1 h 11 min of 10 h");
		GradeOf(later, _officeHours).Should().Be(DayCoverage.Medium, "3 h 11 min of 10 h");
	}

	[Fact]
	public void Cells_GradeEachSpanOnTheCalendar_AndSkipDaysOffIt()
	{
		var calendar = Calendar(Day(1, 5), 3, RowWidth);
		ArchiveDaySpan[] spans =
		[
			new(Day(1, 4), Berlin(1, 4, 7), Berlin(1, 4, 17)),
			new(Day(1, 5), Berlin(1, 5, 7), Berlin(1, 5, 17)),
			new(Day(1, 7), Berlin(1, 7, 7), Berlin(1, 7, 9)),
			new(Day(1, 8), Berlin(1, 8, 7), Berlin(1, 8, 17))
		];

		calendar.Cells(spans, _officeHours, BerlinZone).Should().Equal(
			new CalendarCell(1043.0, 19.0, DayCoverage.Full),
			new CalendarCell(1083.0, 19.0, DayCoverage.Low));
	}

	// 2 200 days on 1 100 px: days 2k and 2k + 1 share a pixel column. Every even day is full, every odd one
	// short, and day 0 too, so the first column holds two short days.
	[Fact]
	public void Cells_OfDaysSharingAPixelColumn_AreOneCellOfTheirHighestGrade_AndNeverOverlap()
	{
		var calendar = Calendar(Day(1, 5), 2200, RowWidth);
		var spans = Enumerable.Range(0, 2200)
			.Select(index => SpanOn(Day(1, 5).AddDays(index), isFull: index % 2 == 0 && index > 0));

		var cells = calendar.Cells([.. spans], _officeHours, BerlinZone);

		cells.Should().HaveCount(1100, "a pixel column draws one cell");
		cells.Zip(cells.Skip(1)).Should().OnlyContain(pair => pair.First.Left + pair.First.Width <= pair.Second.Left);
		cells[0].Coverage.Should().Be(DayCoverage.Low, "two short days stay short, never a darker mix");
		cells.Skip(1).Should().OnlyContain(cell => cell.Coverage == DayCoverage.Full, "a column takes its fuller day");
	}

	[Fact]
	public void PageOutline_OfADay_RingsItsCellWithTheStroke()
	{
		var calendar = Calendar(Day(1, 5), 21, RowWidth);

		calendar.PageOutline(Page(MinimapPageUnit.Day, Day(1, 8))).Should().Be(new TrackSpan(741.0, 23.0));
	}

	[Fact]
	public void PageOutline_OfTheLastDay_DrawsItsStrokePastTheRowsRightEnd()
	{
		var calendar = Calendar(Day(1, 5), 21, RowWidth);

		calendar.PageOutline(Page(MinimapPageUnit.Day, Day(1, 25))).Should().Be(
			new TrackSpan(RowWidth - 21.0, 23.0), "the stroke runs 2 px into the gap before the step column");
	}

	[Fact]
	public void PageOutline_OfAWeek_RingsItsSevenCells()
	{
		var calendar = Calendar(Day(1, 5), 21, RowWidth);

		calendar.PageOutline(Page(MinimapPageUnit.Week, Day(1, 12))).Should().Be(
			new TrackSpan(calendar.CellLeft(7) - 2.0, (6 * 20.0) + 19.0 + 4.0));
	}

	[Fact]
	public void PageOutline_OfAWeekRunningPastTheLastDay_StopsAtItsCell()
	{
		var calendar = Calendar(Day(1, 5), 10, RowWidth);

		calendar.PageOutline(Page(MinimapPageUnit.Week, Day(1, 12))).Should().Be(
			new TrackSpan(calendar.CellLeft(7) - 2.0, (2 * 20.0) + 19.0 + 4.0), "the last day is Wednesday");
	}

	[Theory]
	[InlineData(1, 1)]
	[InlineData(1, 19)]
	public void PageOutline_OfAPageOffTheCalendar_IsNull(int month, int day)
	{
		Calendar(Day(1, 5), 10, RowWidth).PageOutline(Page(MinimapPageUnit.Day, Day(month, day))).Should().BeNull();
	}

	[Fact]
	public void MarkerTicks_AreOnePerDayWithAMarker_InTheArchiveWallClock_Ascending()
	{
		var calendar = Calendar(Day(1, 5), 7, RowWidth);
		DateTime[] markers =
		[
			Berlin(1, 9, 12),
			new(2026, 1, 5, 23, 30, 0, DateTimeKind.Utc),
			Berlin(1, 6, 9),
			Berlin(1, 20, 9)
		];

		calendar.MarkerTicks(markers, BerlinZone).Should().Equal(calendar.Cell(1), calendar.Cell(4));
	}

	[Fact]
	public void MarkerCountsByDay_CountTheMarkersOfEachArchiveWallClockDay()
	{
		DateTime[] markers =
		[
			Berlin(1, 9, 12),
			new(2026, 1, 5, 23, 30, 0, DateTimeKind.Utc),
			Berlin(1, 6, 9),
			Berlin(1, 9, 15)
		];

		DayCalendar.MarkerCountsByDay(markers, BerlinZone).Should().BeEquivalentTo(
			new Dictionary<DateOnly, int> { [Day(1, 6)] = 2, [Day(1, 9)] = 2 },
			"23:30 UTC on the 5th is half past midnight on the 6th in Berlin");
	}

	[Theory]
	[InlineData(42, true)]
	[InlineData(43, false)]
	public void TheMondays_AreNamedOnAnArchiveOfSixWeeksOrLess_AtAnyWidth(int dayCount, bool namesMondays)
	{
		Calendar(Day(1, 5), dayCount, RowWidth).ShowsMondayLabels.Should().Be(namesMondays);
		Calendar(Day(1, 5), dayCount, 200.0).ShowsMondayLabels.Should().Be(namesMondays, "the width never decides");
	}

	[Fact]
	public void MonthSeparators_StandInTheGapBeforeEachMonthStart_WhileOnlyTheMonthsAreNamed()
	{
		var calendar = Calendar(new DateOnly(2026, 6, 1), 183, RowWidth);

		calendar.ShowsMondayLabels.Should().BeFalse();
		calendar.MonthSeparators().Should().Equal(
			new[] { 30, 61, 92, 122, 153 }.Select(index => calendar.CellLeft(index) - 1.0));
	}

	[Fact]
	public void MonthSeparators_AreNone_WhileTheMondaysAreNamed()
	{
		var calendar = Calendar(new DateOnly(2026, 1, 20), 21, RowWidth);

		calendar.ShowsMondayLabels.Should().BeTrue();
		calendar.MonthSeparators().Should().BeEmpty();
	}

	private static ArchiveDaySpan SpanOn(DateOnly day, bool isFull)
	{
		var midnight = ArchiveClock.Midnight(day);

		return new ArchiveDaySpan(
			day,
			ArchiveClock.ToUtc(midnight.AddHours(7), BerlinZone),
			ArchiveClock.ToUtc(midnight.AddHours(isFull ? 17 : 8), BerlinZone));
	}

	private static DayCoverage GradeOf(ArchiveDaySpan span, DayHours hours)
	{
		return Calendar(span.Day, 1, RowWidth).Cells([span], hours, BerlinZone).Single().Coverage;
	}

	private static DayCalendar Calendar(DateOnly firstDay, int dayCount, double width)
	{
		return DayCalendar.Over(firstDay, firstDay.AddDays(dayCount - 1), width)
			?? throw new InvalidOperationException("The width leaves room for the cells.");
	}

	private static MinimapPage Page(MinimapPageUnit unit, DateOnly firstDay)
	{
		return new MinimapPage(unit, firstDay, DayHours.FullDay, default, default);
	}

	private static DateOnly Day(int month, int day)
	{
		return new DateOnly(2026, month, day);
	}
}
