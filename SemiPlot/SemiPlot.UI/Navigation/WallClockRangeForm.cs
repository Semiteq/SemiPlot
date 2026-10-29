using System.Globalization;

using ReactiveUI;

namespace SemiPlot.UI.Navigation;

/// <summary>The first rule a wall-clock range breaks, in the order a range form shows them.</summary>
public enum WallClockRangeProblem
{
	None,
	StartDateMissing,
	StartUnreadable,
	EndDateMissing,
	EndUnreadable,
	EndNotAfterStart
}

/// <summary>A start and an end typed as a date plus a <c>HH:mm:ss</c> time, read with no zone conversion.</summary>
public abstract class WallClockRangeForm : ReactiveObject
{
	internal const string TimeFieldFormat = "HH:mm:ss";

	private DateTime? _startDate;
	private string _startTimeText = string.Empty;
	private DateTime? _endDate;
	private string _endTimeText = string.Empty;

	public DateTime? StartDate
	{
		get => _startDate;
		set
		{
			this.RaiseAndSetIfChanged(ref _startDate, value);
			RaiseDerived();
		}
	}

	public string StartTimeText
	{
		get => _startTimeText;
		set
		{
			this.RaiseAndSetIfChanged(ref _startTimeText, value);
			RaiseDerived();
		}
	}

	public DateTime? EndDate
	{
		get => _endDate;
		set
		{
			this.RaiseAndSetIfChanged(ref _endDate, value);
			RaiseDerived();
		}
	}

	public string EndTimeText
	{
		get => _endTimeText;
		set
		{
			this.RaiseAndSetIfChanged(ref _endTimeText, value);
			RaiseDerived();
		}
	}

	protected WallClockRangeProblem RangeProblem
	{
		get
		{
			if (StartDate is null)
			{
				return WallClockRangeProblem.StartDateMissing;
			}

			if (StartBound is not { } from)
			{
				return WallClockRangeProblem.StartUnreadable;
			}

			if (EndDate is null)
			{
				return WallClockRangeProblem.EndDateMissing;
			}

			if (EndBound is not { } to)
			{
				return WallClockRangeProblem.EndUnreadable;
			}

			return to <= from ? WallClockRangeProblem.EndNotAfterStart : WallClockRangeProblem.None;
		}
	}

	protected (DateTime From, DateTime To)? RangeBounds =>
		StartBound is { } from && EndBound is { } to && to > from ? (from, to) : null;

	/// <summary>The typed start even while the range is invalid; null while it is unreadable.</summary>
	protected internal DateTime? StartBound => Bound(StartDate, StartTimeText);

	protected internal DateTime? EndBound => Bound(EndDate, EndTimeText);

	protected static TimeOnly? ParseTime(string text)
	{
		return TimeOnly.TryParseExact(
			text.Trim(), TimeFieldFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
			? parsed
			: null;
	}

	/// <summary>Writes the start date and time as one edit: no observer reads a bound with one half written.</summary>
	internal void PlaceStart(DateTime wallClock)
	{
		Place(ref _startDate, ref _startTimeText, wallClock, nameof(StartDate), nameof(StartTimeText));
	}

	/// <summary>Writes the end date and time as one edit: no observer reads a bound with one half written.</summary>
	internal void PlaceEnd(DateTime wallClock)
	{
		Place(ref _endDate, ref _endTimeText, wallClock, nameof(EndDate), nameof(EndTimeText));
	}

	internal static string FormatTime(DateTime wallClock)
	{
		return wallClock.ToString(TimeFieldFormat, CultureInfo.InvariantCulture);
	}

	internal static DateTime WallClockDay(DateTime utc, TimeZoneInfo timeZone)
	{
		return DateTime.SpecifyKind(ToWallClock(utc, timeZone).Date, DateTimeKind.Unspecified);
	}

	internal static DateTime ToWallClock(DateTime utc, TimeZoneInfo timeZone)
	{
		return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
	}

	internal static DateTime FloorToSecond(DateTime value)
	{
		return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
	}

	/// <summary>Raises every property derived from the four fields.</summary>
	protected virtual void RaiseDerived()
	{
	}

	private void Place(
		ref DateTime? date, ref string timeText, DateTime wallClock, string dateName, string timeTextName)
	{
		var placedDate = wallClock.Date;
		var placedTimeText = FormatTime(wallClock);
		var isDateChanged = date != placedDate;
		var isTimeTextChanged = timeText != placedTimeText;

		date = placedDate;
		timeText = placedTimeText;

		if (isDateChanged)
		{
			this.RaisePropertyChanged(dateName);
		}

		if (isTimeTextChanged)
		{
			this.RaisePropertyChanged(timeTextName);
		}

		RaiseDerived();
	}

	private static DateTime? Bound(DateTime? date, string timeText)
	{
		return date is { } day && ParseTime(timeText) is { } time ? day.Date + time.ToTimeSpan() : null;
	}
}
