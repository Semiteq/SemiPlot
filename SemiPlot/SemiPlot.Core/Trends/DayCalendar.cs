using SemiPlot.Core.Data;

namespace SemiPlot.Core.Trends;

/// <summary>A horizontal span of the day calendar, in pixels from its left end.</summary>
public readonly record struct TrackSpan(double Left, double Width);

/// <summary>How much of the common working hours a day's span covers; a day without rows has no cell.</summary>
public enum DayCoverage
{
	Low,
	Medium,
	Full
}

/// <summary>One day cell of the calendar that holds rows, graded by its coverage.</summary>
public readonly record struct CalendarCell(double Left, double Width, DayCoverage Coverage);

/// <summary>
/// The minimap's day calendar: one cell per archive wall-clock day of the extent, the last day's cell ending at
/// <see cref="Right"/> (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public readonly record struct DayCalendar(DateOnly FirstDay, int DayCount, double Pitch, double Gap, double Right)
{
	/// <summary>The page outline's stroke, drawn outside the cells; the calendar keeps it free at its left end.</summary>
	public const double OutlineStroke = 2.0;

	/// <summary>The month separator's width, in the gap before a month's first cell.</summary>
	public const double SeparatorWidth = 1.0;

	private const double MaximumPitch = 20.0;
	private const int LongestMondayLabelledArchiveDays = 42;
	private const double NarrowestGappedPitch = 4.0;
	private const double CellGap = 1.0;
	private const double LowCoverageCeiling = 0.25;
	private const double MediumCoverageCeiling = 0.75;

	/// <summary>A cell's drawn width; at a pitch below one pixel a cell is one pixel column wide.</summary>
	public double CellWidth => Math.Max(Pitch - Gap, 1.0);

	/// <summary>True on an archive of six weeks or less, whose label row names its Mondays beside its months.</summary>
	public bool ShowsMondayLabels => DayCount <= LongestMondayLabelledArchiveDays;

	/// <summary>
	/// The calendar over the days from first to last, the last ending at the row's right end, at a whole pitch of
	/// at most 20 px, a 1 px gap from a 4 px pitch; a pitch below 1 px is fractional; null when no room is left.
	/// </summary>
	public static DayCalendar? Over(DateOnly firstDay, DateOnly lastDay, double width)
	{
		var dayCount = Math.Max(lastDay.DayNumber - firstDay.DayNumber + 1, 1);
		var available = width - OutlineStroke;
		if (available <= 0.0)
		{
			return null;
		}

		var wholePitch = Math.Min(MaximumPitch, Math.Floor(available / dayCount));
		var pitch = wholePitch >= 1.0 ? wholePitch : available / dayCount;

		return new DayCalendar(firstDay, dayCount, pitch, pitch >= NarrowestGappedPitch ? CellGap : 0.0, width);
	}

	/// <summary>The grade of a day's span against the common working hours of the archive's wall clock.</summary>
	private static DayCoverage CoverageOf(ArchiveDaySpan span, DayHours hours, TimeZoneInfo zone)
	{
		var midnight = ArchiveClock.Midnight(span.Day);
		var (hoursStart, hoursEnd) = (midnight.AddHours(hours.StartHour), midnight.AddHours(hours.EndHour));
		var first = Later(ArchiveClock.WallClockOf(span.FirstUtc, zone), hoursStart);
		var last = Earlier(ArchiveClock.WallClockOf(span.LastUtc, zone), hoursEnd);
		var covered = last > first ? (last - first) / (hoursEnd - hoursStart) : 0.0;

		return covered switch
		{
			< LowCoverageCeiling => DayCoverage.Low,
			< MediumCoverageCeiling => DayCoverage.Medium,
			_ => DayCoverage.Full
		};
	}

	/// <summary>A cell's left edge; a pitch below one pixel puts a day in the whole pixel column it falls in.</summary>
	public double CellLeft(int index)
	{
		return Right - CellWidth - Math.Floor((DayCount - 1 - index) * Pitch);
	}

	public TrackSpan Cell(int index)
	{
		return new TrackSpan(CellLeft(index), CellWidth);
	}

	/// <summary>The cell index of the day; negative before the calendar, the count or more past it.</summary>
	public int IndexOf(DateOnly day)
	{
		return day.DayNumber - FirstDay.DayNumber;
	}

	/// <summary>The day whose pitch holds x, its leading gap included; null outside the cells.</summary>
	public DateOnly? DayAt(double x)
	{
		if (x > Right)
		{
			return null;
		}

		var index = DayCount - 1 - (int)Math.Floor((Right - x) / Pitch);

		return index >= 0 ? FirstDay.AddDays(index) : null;
	}

	/// <summary>
	/// One cell per ascending span on the calendar, graded by <see cref="CoverageOf"/>; days that share a pixel
	/// column share one cell of their highest grade.
	/// </summary>
	public IReadOnlyList<CalendarCell> Cells(IReadOnlyList<ArchiveDaySpan> spans, DayHours hours, TimeZoneInfo zone)
	{
		var cells = new List<CalendarCell>(spans.Count);
		foreach (var span in spans)
		{
			var index = IndexOf(span.Day);
			if (index < 0 || index >= DayCount)
			{
				continue;
			}

			var cell = new CalendarCell(CellLeft(index), CellWidth, CoverageOf(span, hours, zone));
			if (cells.Count > 0 && cells[^1].Left == cell.Left)
			{
				var shared = cells[^1].Coverage;
				cells[^1] = cell with { Coverage = shared > cell.Coverage ? shared : cell.Coverage };
			}
			else
			{
				cells.Add(cell);
			}
		}

		return cells;
	}

	/// <summary>The page's cells on the calendar with the stroke around them; a week stops at the last day.</summary>
	public TrackSpan? PageOutline(MinimapPage page)
	{
		var first = Math.Max(IndexOf(page.FirstDay), 0);
		var last = Math.Min(IndexOf(page.EndDay) - 1, DayCount - 1);
		if (first > last)
		{
			return null;
		}

		var left = CellLeft(first) - OutlineStroke;
		var right = CellLeft(last) + CellWidth + OutlineStroke;

		return new TrackSpan(left, right - left);
	}

	/// <summary>The count of the markers on each archive wall-clock day that holds one.</summary>
	public static IReadOnlyDictionary<DateOnly, int> MarkerCountsByDay(
		IReadOnlyList<DateTime> markerTimesUtc,
		TimeZoneInfo zone)
	{
		var counts = new Dictionary<DateOnly, int>();
		foreach (var markerTimeUtc in markerTimesUtc)
		{
			var day = ArchiveClock.DayOf(markerTimeUtc, zone);
			counts[day] = counts.GetValueOrDefault(day) + 1;
		}

		return counts;
	}

	/// <summary>One cell-wide tick per calendar day that holds at least one of the markers, ascending.</summary>
	public IReadOnlyList<TrackSpan> MarkerTicks(IReadOnlyList<DateTime> markerTimesUtc, TimeZoneInfo zone)
	{
		var ticks = new List<TrackSpan>();
		foreach (var day in MarkerCountsByDay(markerTimesUtc, zone).Keys.Order())
		{
			var index = IndexOf(day);
			if (index >= 0 && index < DayCount)
			{
				ticks.Add(Cell(index));
			}
		}

		return ticks;
	}

	/// <summary>The x of a 1 px line before each month's first cell; none while the Mondays are named.</summary>
	public IReadOnlyList<double> MonthSeparators()
	{
		if (ShowsMondayLabels)
		{
			return [];
		}

		var separators = new List<double>();
		var month = new DateOnly(FirstDay.Year, FirstDay.Month, 1).AddMonths(1);
		for (; IndexOf(month) < DayCount; month = month.AddMonths(1))
		{
			separators.Add(CellLeft(IndexOf(month)) - SeparatorWidth);
		}

		return separators;
	}

	private static DateTime Later(DateTime first, DateTime second)
	{
		return first > second ? first : second;
	}

	private static DateTime Earlier(DateTime first, DateTime second)
	{
		return first < second ? first : second;
	}
}
