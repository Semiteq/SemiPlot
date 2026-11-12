using AwesomeAssertions;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Unit;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ShiftScheduleTests
{
	private static readonly long _intervalTicks = TimeSpan.FromSeconds(30).Ticks;

	// Seven seconds divides neither a minute nor an hour, so a session bound cannot land on the lattice
	// by coincidence.
	private static readonly long _oddIntervalTicks = TimeSpan.FromSeconds(7).Ticks;

	private static readonly ShiftSchedule _schedule = new(SeederOptions.DefaultSeed, _intervalTicks);

	private static readonly DateOnly _firstDay = new(2026, 1, 5);

	[Fact]
	public void IdenticalSeedsDrawIdenticalSchedules()
	{
		var again = new ShiftSchedule(SeederOptions.DefaultSeed, _intervalTicks);
		var other = new ShiftSchedule(2, _intervalTicks);
		var days = Days(365).ToArray();

		days.Select(again.Session).Should().Equal(days.Select(_schedule.Session));
		days.SelectMany(again.RecipeRuns).Should().Equal(days.SelectMany(_schedule.RecipeRuns));
		days.Select(other.Session).Should().NotEqual(days.Select(_schedule.Session));

		var start = _firstDay.ToDateTime(TimeOnly.MinValue);
		var end = start.AddDays(60);

		again.Clip(start, end).OffPeriods.Should().Equal(_schedule.Clip(start, end).OffPeriods);
	}

	[Fact]
	public void NoSessionFallsOnAWeekend()
	{
		var weekend = Days(730).Where(day => day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday).ToArray();

		weekend.Should().NotBeEmpty();
		weekend.Should().AllSatisfy(day => _schedule.Session(day).Should().BeNull());
	}

	// Five weekdays off in a row is the maintenance week, every 26 weeks; random off days alone make one
	// about once in ten million weeks.
	[Fact]
	public void TheMaintenanceWeekRecursOncePerCycle()
	{
		var offWeeks = OffWeeks(156);

		offWeeks.Count.Should().BeGreaterThanOrEqualTo(5);

		for (var index = 1; index < offWeeks.Count; index++)
		{
			(offWeeks[index].DayNumber - offWeeks[index - 1].DayNumber).Should().Be(7 * 26);
		}
	}

	// The specified share is 4% of the weekdays outside the maintenance weeks.
	[Fact]
	public void RandomOffDaysTakeAboutTheirShare()
	{
		var maintenance = OffWeeks(156).ToHashSet();
		var weekdays = Days(156 * 7)
			.Where(day => day.DayOfWeek is not (DayOfWeek.Saturday or DayOfWeek.Sunday))
			.Where(day => !maintenance.Contains(day.AddDays(-(int)day.DayOfWeek + 1)))
			.ToArray();

		var share = weekdays.Count(day => !_schedule.IsOn(day)) / (double)weekdays.Length;

		share.Should().BeInRange(0.02, 0.06);
	}

	[Theory]
	[InlineData(30.0)]
	[InlineData(7.0)]
	public void SessionsStartFromSevenToHalfPastEightAndLastSixToTenHoursOnTheLattice(double changeSeconds)
	{
		var intervalTicks = TimeSpan.FromSeconds(changeSeconds).Ticks;
		var schedule = new ShiftSchedule(SeederOptions.DefaultSeed, intervalTicks);
		var sessions = Days(365).Select(day => (Day: day, Session: schedule.Session(day))).ToArray();

		sessions.Count(entry => entry.Session is not null).Should().BeGreaterThan(200);

		foreach (var (day, session) in sessions)
		{
			if (session is not { } window)
			{
				continue;
			}

			var midnight = day.ToDateTime(TimeOnly.MinValue);

			(window.Start - midnight).Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(7))
				.And.BeLessThanOrEqualTo(TimeSpan.FromHours(8.5));
			(window.End - window.Start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromHours(6))
				.And.BeLessThanOrEqualTo(TimeSpan.FromHours(10));
			(window.Start.Ticks % intervalTicks).Should().Be(0);
			(window.End.Ticks % intervalTicks).Should().Be(0);
		}
	}

	// Runs of 6-18 min after pauses of 4-20 min, or 40 min longer; one run in 15 is interrupted.
	[Fact]
	public void RecipeRunsLieInOrderInsideTheirSession()
	{
		var schedule = new ShiftSchedule(SeederOptions.DefaultSeed, _oddIntervalTicks);
		var runCount = 0;
		var interruptedCount = 0;

		foreach (var day in Days(365))
		{
			var runs = schedule.RecipeRuns(day);

			if (schedule.Session(day) is not { } session)
			{
				runs.Should().BeEmpty();

				continue;
			}

			runs.Should().NotBeEmpty();

			var previousEnd = session.Start;

			foreach (var run in runs)
			{
				(run.Start - previousEnd).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(4))
					.And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(60));
				(run.End - run.Start).Should().BeGreaterThanOrEqualTo(TimeSpan.FromMinutes(6))
					.And.BeLessThanOrEqualTo(TimeSpan.FromMinutes(18));
				run.End.Should().BeOnOrBefore(session.End);
				(run.Start.Ticks % _oddIntervalTicks).Should().Be(0);
				(run.End.Ticks % _oddIntervalTicks).Should().Be(0);

				previousEnd = run.End;
				runCount++;
				interruptedCount += run.IsInterrupted ? 1 : 0;
			}
		}

		(interruptedCount / (double)runCount).Should().BeInRange(1.0 / 30.0, 1.0 / 10.0);
	}

	[Fact]
	public void SessionsAndOffPeriodsTileTheSpan()
	{
		var start = _firstDay.ToDateTime(TimeOnly.MinValue);
		var end = start.AddDays(60);
		var span = _schedule.Clip(start, end);
		var sessions = Sessions(span);
		var drawn = Days(60).Select(_schedule.Session).OfType<BreakPlan.Window>().ToArray();

		span.End.Should().Be(end);
		sessions.Count.Should().Be(drawn.Length);
		sessions[..^1].Should().Equal(drawn[..^1]);
		sessions[^1].Start.Should().Be(drawn[^1].Start);

		for (var index = 0; index < span.OffPeriods.Count; index++)
		{
			span.OffPeriods[index].Start.Should().Be(sessions[index].End);
			span.OffPeriods[index].End.Should().Be(sessions[index + 1].Start);
		}
	}

	[Fact]
	public void AStartOnASaturdayOpensAtTheNextSession()
	{
		var saturday = FirstDay(_firstDay, day => day.DayOfWeek == DayOfWeek.Saturday);
		var opening = FirstDay(saturday, _schedule.IsOn);
		var start = saturday.ToDateTime(TimeOnly.MinValue);

		var span = _schedule.Clip(start, start.AddDays(14));

		span.Start.Should().Be(_schedule.Session(opening)!.Value.Start);
		span.Start.DayOfWeek.Should().NotBe(DayOfWeek.Saturday).And.NotBe(DayOfWeek.Sunday);
	}

	[Fact]
	public void AStartAtTwoInTheMorningOpensAtThatDaysSession()
	{
		var day = FirstDay(_firstDay, _schedule.IsOn);
		var start = day.ToDateTime(new TimeOnly(2, 0));

		_schedule.Clip(start, start.AddDays(7)).Start.Should().Be(_schedule.Session(day)!.Value.Start);
	}

	[Fact]
	public void AStartInsideASessionOpensAtTheNextOne()
	{
		var day = FirstDay(_firstDay, _schedule.IsOn);
		var next = FirstDay(day.AddDays(1), _schedule.IsOn);
		var start = _schedule.Session(day)!.Value.Start.AddHours(1);

		_schedule.Clip(start, start.AddDays(7)).Start.Should().Be(_schedule.Session(next)!.Value.Start);
	}

	[Fact]
	public void AnEndOnASundayStretchesFridaysSessionOverTheWeekend()
	{
		var sunday = FirstDay(_firstDay.AddDays(14), day =>
			day.DayOfWeek == DayOfWeek.Sunday && _schedule.IsOn(day.AddDays(-2)));
		var friday = _schedule.Session(sunday.AddDays(-2))!.Value;
		var end = sunday.ToDateTime(new TimeOnly(12, 0));

		var span = _schedule.Clip(end.AddDays(-14), end);

		span.End.Should().Be(end);
		span.OffPeriods[^1].End.Should().Be(friday.Start);
		Sessions(span)[^1].Should().Be(friday with { End = end });
	}

	[Fact]
	public void AnEndAtThreeInTheMorningStretchesThePreviousSession()
	{
		var previous = FirstDay(_firstDay.AddDays(14), _schedule.IsOn);
		var day = previous.AddDays(1);
		var end = day.ToDateTime(new TimeOnly(3, 0));

		var span = _schedule.Clip(end.AddDays(-14), end);

		Sessions(span)[^1].Should().Be(_schedule.Session(previous)!.Value with { End = end });
	}

	// A last session holding a single change would have to carry both break markers on one row.
	[Fact]
	public void ASessionStartingTooCloseToTheEndIsLeftOut()
	{
		var previous = FirstDay(_firstDay.AddDays(14), _schedule.IsOn);
		var day = FirstDay(previous.AddDays(1), _schedule.IsOn);
		var opening = _schedule.Session(day)!.Value.Start;

		var tooClose = _schedule.Clip(opening.AddDays(-14), opening.AddTicks(_intervalTicks));
		var holdsTwo = _schedule.Clip(opening.AddDays(-14), opening.AddTicks(_intervalTicks + 1));

		Sessions(tooClose)[^1].Start.Should().Be(_schedule.Session(previous)!.Value.Start);
		Sessions(holdsTwo)[^1].Start.Should().Be(opening);
	}

	[Fact]
	public void AOneDaySpanHoldsOneSession()
	{
		var day = FirstDay(_firstDay, _schedule.IsOn);
		var start = day.ToDateTime(TimeOnly.MinValue);

		var span = _schedule.Clip(start, start.AddDays(1));

		span.Start.Should().Be(_schedule.Session(day)!.Value.Start);
		span.End.Should().Be(start.AddDays(1));
		span.OffPeriods.Should().BeEmpty();
	}

	[Fact]
	public void AOneDaySpanOverAWeekendIsRefused()
	{
		var saturday = FirstDay(_firstDay, day => day.DayOfWeek == DayOfWeek.Saturday);
		var start = saturday.ToDateTime(TimeOnly.MinValue);

		var act = () => _schedule.Clip(start, start.AddDays(1));

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	private static IEnumerable<DateOnly> Days(int count)
	{
		return Enumerable.Range(0, count).Select(_firstDay.AddDays);
	}

	private static DateOnly FirstDay(DateOnly from, Func<DateOnly, bool> predicate)
	{
		var day = from;

		while (!predicate(day))
		{
			day = day.AddDays(1);
		}

		return day;
	}

	// The Mondays of the weeks whose five weekdays hold no session.
	private static List<DateOnly> OffWeeks(int weekCount)
	{
		return [.. Enumerable.Range(0, weekCount)
			.Select(week => _firstDay.AddDays(7 * week))
			.Where(monday => Enumerable.Range(0, 5).All(offset => !_schedule.IsOn(monday.AddDays(offset))))];
	}

	private static List<BreakPlan.Window> Sessions(ShiftSchedule.ClippedSpan span)
	{
		var sessions = new List<BreakPlan.Window>();
		var cursor = span.Start;

		foreach (var off in span.OffPeriods)
		{
			sessions.Add(new BreakPlan.Window(cursor, off.Start));
			cursor = off.End;
		}

		sessions.Add(new BreakPlan.Window(cursor, span.End));

		return sessions;
	}
}
