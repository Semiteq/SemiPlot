namespace SemiPlot.Tools.ArchiveSeeder;

/// <summary>A plasma tool's working pattern: one weekday session of recipe runs, pure in seed and day.</summary>
public sealed class ShiftSchedule(long seed, long intervalTicks)
{
	private const int MaintenanceCycleWeeks = 26;
	private const double RandomOffDayShare = 0.04;
	private const double InterruptedRunShare = 1.0 / 15.0;
	private const double LongPauseShare = 0.1;

	// Hash coordinates clear of the pen identifiers (1000 and up) and of BreakPlan's small negatives.
	private const long MaintenanceCoordinate = 1L << 41;
	private const long DayCoordinates = 1L << 40;

	// A session holds at most 60 runs of four draws each, after the day's three draws.
	private const long DrawsPerDay = 1024;
	private const int OffDayDraw = 0;
	private const int SessionStartDraw = 1;
	private const int SessionLengthDraw = 2;
	private const int FirstRunDraw = 3;
	private const int DrawsPerRun = 4;

	private static readonly TimeSpan _earliestSessionStart = TimeSpan.FromHours(7);
	private static readonly TimeSpan _latestSessionStart = TimeSpan.FromHours(8.5);
	private static readonly TimeSpan _shortestSession = TimeSpan.FromHours(6);
	private static readonly TimeSpan _longestSession = TimeSpan.FromHours(10);
	private static readonly TimeSpan _shortestRun = TimeSpan.FromMinutes(6);
	private static readonly TimeSpan _longestRun = TimeSpan.FromMinutes(18);
	private static readonly TimeSpan _shortestPause = TimeSpan.FromMinutes(4);
	private static readonly TimeSpan _longestPause = TimeSpan.FromMinutes(20);
	private static readonly TimeSpan _longPauseExtra = TimeSpan.FromMinutes(40);

	public static ShiftSchedule For(SeederOptions options)
	{
		return new ShiftSchedule(options.Seed, RawLayerGenerator.ChangeIntervalTicks(options.ChangeSeconds));
	}

	public bool IsOn(DateOnly day)
	{
		if (day.DayOfWeek is DayOfWeek.Saturday or DayOfWeek.Sunday || IsMaintenanceWeek(day))
		{
			return false;
		}

		return Draw(day, OffDayDraw) >= RandomOffDayShare;
	}

	public BreakPlan.Window? Session(DateOnly day)
	{
		if (!IsOn(day))
		{
			return null;
		}

		var midnight = day.ToDateTime(TimeOnly.MinValue);
		var start = LatticePoint(midnight + _earliestSessionStart, midnight + _latestSessionStart,
			Draw(day, SessionStartDraw));
		var end = LatticePoint(start + _shortestSession, start + _longestSession, Draw(day, SessionLengthDraw));

		return new BreakPlan.Window(start, end);
	}

	/// <summary>The runs of the day's drawn session; a run that would pass the session's end is not drawn.</summary>
	public IReadOnlyList<ScheduledRun> RecipeRuns(DateOnly day)
	{
		if (Session(day) is not { } session)
		{
			return [];
		}

		var runs = new List<ScheduledRun>();
		var cursor = session.Start;

		for (var draw = FirstRunDraw; ; draw += DrawsPerRun)
		{
			var pause = Draw(day, draw) < LongPauseShare ? _longPauseExtra : TimeSpan.Zero;
			var start = LatticePoint(
				cursor + _shortestPause + pause,
				cursor + _longestPause + pause,
				Draw(day, draw + 1));
			var end = LatticePoint(start + _shortestRun, start + _longestRun, Draw(day, draw + 2));

			if (end > session.End)
			{
				return runs;
			}

			runs.Add(new ScheduledRun(start, end, Draw(day, draw + 3) < InterruptedRunShare));
			cursor = end;
		}
	}

	/// <summary>
	/// The span from the first session start at or after start to end, and the off periods inside it.
	/// </summary>
	public ClippedSpan Clip(DateTime start, DateTime end)
	{
		var sessions = new List<BreakPlan.Window>();

		for (var day = DateOnly.FromDateTime(start); day <= DateOnly.FromDateTime(end); day = day.AddDays(1))
		{
			if (Session(day) is { } session && session.Start >= start && HoldsTwoChanges(session.Start, end))
			{
				sessions.Add(session);
			}
		}

		if (sessions.Count == 0)
		{
			throw new ArgumentOutOfRangeException(
				nameof(start),
				start,
				$"no shift session starts between {start:O} and {end:O} early enough to hold two changes.");
		}

		// The last session runs to the end whatever the clock, so the demo writer finds a fresh edge.
		sessions[^1] = sessions[^1] with { End = end };

		var offPeriods = new List<BreakPlan.Window>(sessions.Count - 1);

		for (var index = 1; index < sessions.Count; index++)
		{
			offPeriods.Add(new BreakPlan.Window(sessions[index - 1].End, sessions[index].Start));
		}

		return new ClippedSpan(sessions[0].Start, end, offPeriods);
	}

	private bool IsMaintenanceWeek(DateOnly day)
	{
		var phase = (long)(SyntheticValueWalk.Fraction(seed, MaintenanceCoordinate) * MaintenanceCycleWeeks);

		// DayNumber zero is a Monday, so whole weeks of day numbers are calendar weeks.
		var week = day.DayNumber / 7;

		return (week + MaintenanceCycleWeeks - phase) % MaintenanceCycleWeeks == 0;
	}

	private double Draw(DateOnly day, int slot)
	{
		return SyntheticValueWalk.Fraction(seed, DayCoordinates + (day.DayNumber * DrawsPerDay) + slot);
	}

	// Two lattice changes is the least an archiving run bounded by a break can carry its marker pair on.
	private bool HoldsTwoChanges(DateTime sessionStart, DateTime end)
	{
		return sessionStart.Ticks + intervalTicks < end.Ticks;
	}

	// Drawn as a lattice index, so the point stays inside its bounds whatever the change interval.
	private DateTime LatticePoint(DateTime earliest, DateTime latest, double fraction)
	{
		var first = (earliest.Ticks + intervalTicks - 1) / intervalTicks;
		var last = Math.Max(first, latest.Ticks / intervalTicks);
		var index = first + (long)(fraction * (last - first + 1));

		return new DateTime(index * intervalTicks);
	}

	public readonly record struct ScheduledRun(DateTime Start, DateTime End, bool IsInterrupted);

	public sealed record ClippedSpan(DateTime Start, DateTime End, IReadOnlyList<BreakPlan.Window> OffPeriods);
}
