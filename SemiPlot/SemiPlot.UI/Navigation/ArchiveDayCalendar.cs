using ReactiveUI;

namespace SemiPlot.UI.Navigation;

/// <summary>The days a pair of range calendars offers and the read days among them that hold no archive row.</summary>
public sealed class ArchiveDayCalendar(TimeZoneInfo timeZone) : ReactiveObject
{
	private DayCoverage? _coverage;
	private DateTime? _newestDay;

	/// <summary>The earliest local day the calendars offer: the first sample's day or an earlier selected one.</summary>
	public DateTime FirstDay
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The latest local day the calendars offer: the newest sample's day or a later selected one.</summary>
	public DateTime LastDay
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The read days that hold no archive row, never a selected date; raised on every write.</summary>
	public IReadOnlyList<DateTime> BlackoutDays
	{
		get;
		private set
		{
			field = value;
			this.RaisePropertyChanged(nameof(BlackoutDays));
		}
	} = [];

	/// <summary>The days a day read covers, [FirstDay, the newest sample's day]; null when there is no such day.</summary>
	public (DateOnly First, DateOnly Last)? ReadRange =>
		_newestDay is { } newestDay && newestDay >= FirstDay
			? (DateOnly.FromDateTime(FirstDay), DateOnly.FromDateTime(newestDay))
			: null;

	/// <summary>A picker's selected date must never be a blackout date, so this runs before the dates change.</summary>
	public void ClearBlackoutDays()
	{
		BlackoutDays = [];
	}

	/// <summary>Offers the sample days widened to the selected dates; keeps a landed read's coverage.</summary>
	public void Seed(DateTime firstSampleUtc, DateTime? newestSampleUtc, DateTime startDate, DateTime endDate)
	{
		_newestDay = newestSampleUtc is { } newestSample ? WallClockRangeForm.WallClockDay(newestSample, timeZone) : null;
		FirstDay = Earlier(WallClockRangeForm.WallClockDay(firstSampleUtc, timeZone), startDate.Date);
		LastDay = Later(_newestDay ?? endDate.Date, endDate.Date);
		BlackoutDays = UnselectedDaysWithoutRows(startDate, endDate);
	}

	public void ApplyCoverage(
		(DateOnly First, DateOnly Last) range,
		IReadOnlyList<DateOnly> daysWithRows,
		DateTime? startDate,
		DateTime? endDate)
	{
		_coverage = new DayCoverage(range.First, range.Last, [.. daysWithRows]);
		BlackoutDays = UnselectedDaysWithoutRows(startDate, endDate);
	}

	public void ForgetCoverage()
	{
		_coverage = null;
	}

	private List<DateTime> UnselectedDaysWithoutRows(DateTime? startDate, DateTime? endDate)
	{
		var days = new List<DateTime>();

		if (_coverage is not { } coverage)
		{
			return days;
		}

		var first = Later(coverage.FirstDay.ToDateTime(TimeOnly.MinValue), FirstDay);
		var last = Earlier(coverage.LastDay.ToDateTime(TimeOnly.MinValue), LastDay);

		for (var day = first; day <= last; day = day.AddDays(1))
		{
			var isSelected = day == startDate?.Date || day == endDate?.Date;

			if (!coverage.DaysWithRows.Contains(DateOnly.FromDateTime(day)) && !isSelected)
			{
				days.Add(day);
			}
		}

		return days;
	}

	private static DateTime Earlier(DateTime first, DateTime second)
	{
		return first < second ? first : second;
	}

	private static DateTime Later(DateTime first, DateTime second)
	{
		return first > second ? first : second;
	}

	private sealed record DayCoverage(DateOnly FirstDay, DateOnly LastDay, HashSet<DateOnly> DaysWithRows);
}
