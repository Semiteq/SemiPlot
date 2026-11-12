using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class DayCalendarLabelsTests
{
	private const double RowWidth = 1102.0;
	private const double NarrowRowWidth = 200.0;
	private const double MonthLabelWidth = 30.0;
	private const double MondayLabelWidth = 34.0;

	[Fact]
	public void Candidates_OfATwoDayArchive_AreTheMonthAtTheFirstCell()
	{
		var firstDay = new DateOnly(2026, 11, 10);
		var calendar = DayCalendar.Over(firstDay, firstDay.AddDays(1), RowWidth)!.Value;

		var labels = DayCalendarLabels.Candidates(calendar);

		labels.Should().Equal(new CalendarLabel(calendar.CellLeft(0) + 2.0, firstDay, CalendarLabelKind.Month));
	}

	[Fact]
	public void Candidates_OfAThreeWeekArchive_AreTheMonthsAndTheMondays()
	{
		var firstDay = new DateOnly(2026, 1, 22);
		var calendar = DayCalendar.Over(firstDay, firstDay.AddDays(20), RowWidth)!.Value;

		var labels = DayCalendarLabels.Candidates(calendar);

		labels.Should().Equal(
			[
				new CalendarLabel(calendar.CellLeft(0) + 2.0, firstDay, CalendarLabelKind.Month),
				new CalendarLabel(calendar.CellLeft(4), new DateOnly(2026, 1, 26), CalendarLabelKind.Day),
				new CalendarLabel(calendar.CellLeft(10) + 2.0, new DateOnly(2026, 2, 1), CalendarLabelKind.Month),
				new CalendarLabel(calendar.CellLeft(11), new DateOnly(2026, 2, 2), CalendarLabelKind.Day),
				new CalendarLabel(calendar.CellLeft(18), new DateOnly(2026, 2, 9), CalendarLabelKind.Day)
			],
			"22 January 2026 is a Thursday, and 1 February a Sunday");
	}

	[Theory]
	[InlineData(RowWidth)]
	[InlineData(NarrowRowWidth)]
	public void ASixMonthArchive_NamesOnlyItsMonths_AtAnyWidth(double rowWidth)
	{
		var firstDay = new DateOnly(2026, 6, 15);
		var calendar = DayCalendar.Over(firstDay, firstDay.AddDays(182), rowWidth)!.Value;

		var candidates = DayCalendarLabels.Candidates(calendar);
		var fitted = DayCalendarLabels.Fitted(candidates, [.. candidates.Select(_ => MonthLabelWidth)], rowWidth);

		candidates.Select(label => label.Day).Should().Equal(
			firstDay, new(2026, 7, 1), new(2026, 8, 1), new(2026, 9, 1), new(2026, 10, 1), new(2026, 11, 1),
			new(2026, 12, 1));
		candidates.Should().OnlyContain(label => label.Kind == CalendarLabelKind.Month);
		fitted.Should().NotBeEmpty().And.Contain(label => label.Index == 0, "the first cell's month always shows");
	}

	[Fact]
	public void Fitted_DropsAMondayThatCollidesWithAMonth_WhereverItStands()
	{
		var firstDay = new DateOnly(2026, 1, 22);
		var calendar = DayCalendar.Over(firstDay, firstDay.AddDays(20), NarrowRowWidth)!.Value;
		var candidates = DayCalendarLabels.Candidates(calendar);
		var widths = candidates
			.Select(label => label.Kind == CalendarLabelKind.Month ? MonthLabelWidth : MondayLabelWidth)
			.ToList();

		var fitted = DayCalendarLabels.Fitted(candidates, widths, NarrowRowWidth);

		calendar.Pitch.Should().Be(9.0);
		fitted.Select(label => candidates[label.Index].Day).Should().Equal(
			[firstDay, new DateOnly(2026, 1, 26), new DateOnly(2026, 2, 1), new DateOnly(2026, 2, 9)],
			"the Monday of 2 February stands on February's label one cell before it");
	}

	[Fact]
	public void Fitted_DropsALabelCloserThanFourPixelsToALabelKeptBeforeIt()
	{
		var fitted = DayCalendarLabels.Fitted(Mondays(0.0, 10.0, 24.0), [20.0, 20.0, 20.0], 100.0);

		fitted.Should().Equal([new PlacedLabel(0, 0.0), new PlacedLabel(2, 24.0)], "10 starts inside the first's gap");
	}

	[Fact]
	public void Fitted_EndsALabelThatWouldRunPastTheRow_AtTheRowsRightEnd()
	{
		var fitted = DayCalendarLabels.Fitted(Mondays(0.0, 90.0), [20.0, 20.0], 100.0);

		fitted.Should().Equal([new PlacedLabel(0, 0.0), new PlacedLabel(1, 80.0)], "90 + 20 would end past 100");
	}

	[Fact]
	public void Fitted_DropsAShiftedLabel_ThatTheShiftBringsTooClose()
	{
		var fitted = DayCalendarLabels.Fitted(Mondays(60.0, 95.0), [20.0, 20.0], 100.0);

		fitted.Should().Equal([new PlacedLabel(0, 60.0)], "95 shifts to 80, inside the first label's gap");
	}

	[Fact]
	public void Fitted_KeepsAMonth_OverAnEarlierMondayItWouldCollideWith()
	{
		CalendarLabel[] candidates =
		[
			new(0.0, new DateOnly(2026, 1, 22), CalendarLabelKind.Month),
			new(40.0, new DateOnly(2026, 1, 26), CalendarLabelKind.Day),
			new(60.0, new DateOnly(2026, 2, 1), CalendarLabelKind.Month)
		];

		var fitted = DayCalendarLabels.Fitted(candidates, [30.0, 30.0, 30.0], 200.0);

		fitted.Should().Equal([new PlacedLabel(0, 0.0), new PlacedLabel(2, 60.0)]);
	}

	[Fact]
	public void Fitted_OnAOneDayCalendar_ShowsItsLabel_EndingAtTheRowsRightEnd()
	{
		var day = new DateOnly(2026, 11, 11);
		var calendar = DayCalendar.Over(day, day, RowWidth)!.Value;
		var candidates = DayCalendarLabels.Candidates(calendar);

		var fitted = DayCalendarLabels.Fitted(candidates, [MonthLabelWidth], RowWidth);

		candidates.Should().ContainSingle().Which.X.Should().Be(RowWidth - 17.0, "the only cell ends the row");
		fitted.Should().Equal([new PlacedLabel(0, RowWidth - MonthLabelWidth)]);
	}

	[Fact]
	public void Fitted_KeepsTheFirstCellsLabel_OverAMondayTooCloseToIt()
	{
		var sunday = new DateOnly(2026, 1, 4);
		var calendar = DayCalendar.Over(sunday, sunday.AddDays(6), RowWidth)!.Value;
		var candidates = DayCalendarLabels.Candidates(calendar);

		var fitted = DayCalendarLabels.Fitted(candidates, [MonthLabelWidth, MondayLabelWidth], RowWidth);

		candidates.Select(label => label.Kind).Should().Equal(CalendarLabelKind.Month, CalendarLabelKind.Day);
		fitted.Should().Equal(
			[new PlacedLabel(0, calendar.CellLeft(0) + 2.0)], "the Monday starts 20 px in, inside the first label");
	}

	private static List<CalendarLabel> Mondays(params double[] lefts)
	{
		return [.. lefts.Select(x => new CalendarLabel(x, new DateOnly(2026, 1, 5), CalendarLabelKind.Day))];
	}
}
