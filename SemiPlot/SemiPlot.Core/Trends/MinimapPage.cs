using SemiPlot.Core.Data;

namespace SemiPlot.Core.Trends;

public enum MinimapPageUnit
{
	Day,
	Week,
	Month
}

public enum PageDirection
{
	Previous,
	Next
}

/// <summary>A Day page's common hours as archive wall-clock whole hours; an end of 24 is the next midnight.</summary>
public readonly record struct DayHours(int StartHour, int EndHour)
{
	public const int HoursPerDay = 24;

	public static readonly DayHours FullDay = new(0, HoursPerDay);
}

/// <summary>
/// One minimap detail page: a working day, a Monday-to-Monday week or a calendar month of the archive's wall
/// clock. Every function reads its spans ascending by day, as the provider returns them.
/// </summary>
public readonly record struct MinimapPage(
	MinimapPageUnit Unit,
	DateOnly FirstDay,
	DayHours Hours,
	DateTime FromUtc,
	DateTime ToUtc)
{
	public const int DaysPerWeek = 7;

	private const int LowPercent = 5;
	private const int HighPercent = 95;

	/// <summary>The day after the page's last day.</summary>
	public DateOnly EndDay => EndDayOf(Unit, FirstDay);

	public bool Holds(DateOnly day)
	{
		return day >= FirstDay && day < EndDay;
	}

	public bool Holds(DateTime fromUtc, DateTime toUtc)
	{
		return fromUtc >= FromUtc && toUtc <= ToUtc;
	}

	/// <summary>
	/// The smallest page that holds the window, its end clipped to the newest sample: its start's Day page, else
	/// the Week page, else the Month page of its start, which a window wider than a month overruns.
	/// </summary>
	public static MinimapPage Holding(
		DateTime windowFromUtc,
		DateTime windowToUtc,
		DateTime? newestSampleUtc,
		DayHours hours,
		IReadOnlyList<ArchiveDaySpan> spans,
		TimeZoneInfo zone)
	{
		var heldToUtc = newestSampleUtc < windowToUtc ? newestSampleUtc.Value : windowToUtc;
		var day = Snap(MinimapPageUnit.Day, windowFromUtc, hours, spans, zone);
		if (day.Holds(windowFromUtc, heldToUtc))
		{
			return day;
		}

		var week = Snap(MinimapPageUnit.Week, windowFromUtc, hours, spans, zone);

		return week.Holds(windowFromUtc, heldToUtc)
			? week
			: Snap(MinimapPageUnit.Month, windowFromUtc, hours, spans, zone);
	}

	/// <summary>P5 of each day's first sample to P95 of each day's last, rounded out to whole hours.</summary>
	public static DayHours WorkingHours(IReadOnlyList<ArchiveDaySpan> spans, TimeZoneInfo zone)
	{
		if (spans.Count == 0)
		{
			return DayHours.FullDay;
		}

		var firsts = new TimeSpan[spans.Count];
		var lasts = new TimeSpan[spans.Count];
		for (var index = 0; index < spans.Count; index++)
		{
			firsts[index] = TimeIntoDay(spans[index].FirstUtc, spans[index].Day, zone);
			lasts[index] = TimeIntoDay(spans[index].LastUtc, spans[index].Day, zone);
		}

		Array.Sort(firsts);
		Array.Sort(lasts);
		var startHour = Math.Min(FloorHour(NearestRank(firsts, LowPercent)), DayHours.HoursPerDay - 1);
		var endHour = Math.Max(CeilingHour(NearestRank(lasts, HighPercent)), startHour + 1);

		return new DayHours(startHour, endHour);
	}

	/// <summary>The page of <paramref name="unit"/> that holds the archive wall-clock day of the time.</summary>
	public static MinimapPage Snap(
		MinimapPageUnit unit,
		DateTime timeUtc,
		DayHours hours,
		IReadOnlyList<ArchiveDaySpan> spans,
		TimeZoneInfo zone)
	{
		return PageOn(unit, ArchiveClock.DayOf(timeUtc, zone), hours, spans, zone);
	}

	/// <summary>The nearest page in the direction whose days hold a span, or null past the last one.</summary>
	public static MinimapPage? Step(
		MinimapPage page,
		PageDirection direction,
		IReadOnlyList<ArchiveDaySpan> spans,
		TimeZoneInfo zone)
	{
		var index = direction == PageDirection.Next
			? FirstIndexFrom(page.EndDay, spans)
			: FirstIndexFrom(page.FirstDay, spans) - 1;
		if (index < 0 || index >= spans.Count)
		{
			return null;
		}

		return PageOn(page.Unit, spans[index].Day, page.Hours, spans, zone);
	}

	/// <summary>The window at its clock offset on the page, or at the page's first sample if that misses.</summary>
	public static (DateTime FromUtc, DateTime ToUtc) Land(
		DateTime windowFromUtc,
		DateTime windowToUtc,
		MinimapPage page,
		IReadOnlyList<ArchiveDaySpan> spans,
		TimeZoneInfo zone)
	{
		var width = windowToUtc - windowFromUtc;
		var windowWallClock = ArchiveClock.WallClockOf(windowFromUtc, zone);
		var windowUnitStart = UnitStart(page.Unit, DateOnly.FromDateTime(windowWallClock));
		var offset = windowWallClock - ArchiveClock.Midnight(windowUnitStart);
		var fromUtc = ArchiveClock.ToUtc(ArchiveClock.Midnight(page.FirstDay) + offset, zone);
		var toUtc = fromUtc + width;

		var first = FirstIndexFrom(page.FirstDay, spans);
		var end = FirstIndexFrom(page.EndDay, spans);
		if (first == end)
		{
			return (fromUtc, toUtc);
		}

		for (var index = first; index < end; index++)
		{
			if (spans[index].FirstUtc <= toUtc && spans[index].LastUtc >= fromUtc)
			{
				return (fromUtc, toUtc);
			}
		}

		return (spans[first].FirstUtc, spans[first].FirstUtc + width);
	}

	/// <summary>The span of the time's own day, else the day whose span lies nearer; a tie takes the earlier.</summary>
	public static ArchiveDaySpan? SnapToData(DateTime timeUtc, IReadOnlyList<ArchiveDaySpan> spans, TimeZoneInfo zone)
	{
		if (spans.Count == 0)
		{
			return null;
		}

		var day = ArchiveClock.DayOf(timeUtc, zone);
		var index = FirstIndexFrom(day, spans);
		if (index == 0 || (index < spans.Count && spans[index].Day == day))
		{
			return spans[index];
		}

		if (index == spans.Count)
		{
			return spans[^1];
		}

		var before = spans[index - 1];
		var after = spans[index];

		return timeUtc - before.LastUtc <= after.FirstUtc - timeUtc ? before : after;
	}

	private static MinimapPage PageOn(
		MinimapPageUnit unit,
		DateOnly day,
		DayHours hours,
		IReadOnlyList<ArchiveDaySpan> spans,
		TimeZoneInfo zone)
	{
		if (unit != MinimapPageUnit.Day)
		{
			var first = UnitStart(unit, day);

			return new MinimapPage(
				unit,
				first,
				hours,
				ArchiveClock.ToUtc(ArchiveClock.Midnight(first), zone),
				ArchiveClock.ToUtc(ArchiveClock.Midnight(EndDayOf(unit, first)), zone));
		}

		var startHour = hours.StartHour;
		var endHour = hours.EndHour;
		var index = FirstIndexFrom(day, spans);
		if (index < spans.Count && spans[index].Day == day)
		{
			startHour = Math.Min(startHour, FloorHour(TimeIntoDay(spans[index].FirstUtc, day, zone)));
			endHour = Math.Max(endHour, CeilingHour(TimeIntoDay(spans[index].LastUtc, day, zone)));
		}

		return new MinimapPage(
			unit,
			day,
			hours,
			ArchiveClock.ToUtc(ArchiveClock.Midnight(day).AddHours(startHour), zone),
			ArchiveClock.ToUtc(ArchiveClock.Midnight(day).AddHours(endHour), zone));
	}

	private static DateOnly UnitStart(MinimapPageUnit unit, DateOnly day)
	{
		return unit switch
		{
			MinimapPageUnit.Week =>
				day.AddDays(-(((int)day.DayOfWeek + DaysPerWeek - (int)DayOfWeek.Monday) % DaysPerWeek)),
			MinimapPageUnit.Month => new DateOnly(day.Year, day.Month, 1),
			_ => day
		};
	}

	private static DateOnly EndDayOf(MinimapPageUnit unit, DateOnly firstDay)
	{
		return unit switch
		{
			MinimapPageUnit.Week => firstDay.AddDays(DaysPerWeek),
			MinimapPageUnit.Month => firstDay.AddMonths(1),
			_ => firstDay.AddDays(1)
		};
	}

	/// <summary>The index of the first span on or after the day, or the count when none is.</summary>
	private static int FirstIndexFrom(DateOnly day, IReadOnlyList<ArchiveDaySpan> spans)
	{
		return SortedSearch.FirstIndex(spans.Count, position => spans[position].Day >= day);
	}

	private static TimeSpan NearestRank(TimeSpan[] sorted, int percent)
	{
		var rank = ((percent * sorted.Length) + 99) / 100;

		return sorted[Math.Max(rank, 1) - 1];
	}

	private static TimeSpan TimeIntoDay(DateTime utc, DateOnly day, TimeZoneInfo zone)
	{
		var timeIntoDay = ArchiveClock.WallClockOf(utc, zone) - ArchiveClock.Midnight(day);

		return Clamp(timeIntoDay, TimeSpan.Zero, TimeSpan.FromHours(DayHours.HoursPerDay));
	}

	private static TimeSpan Clamp(TimeSpan value, TimeSpan minimum, TimeSpan maximum)
	{
		return value < minimum ? minimum : value > maximum ? maximum : value;
	}

	private static int FloorHour(TimeSpan timeOfDay)
	{
		return (int)(timeOfDay.Ticks / TimeSpan.TicksPerHour);
	}

	private static int CeilingHour(TimeSpan timeOfDay)
	{
		return (int)((timeOfDay.Ticks + TimeSpan.TicksPerHour - 1) / TimeSpan.TicksPerHour);
	}
}
