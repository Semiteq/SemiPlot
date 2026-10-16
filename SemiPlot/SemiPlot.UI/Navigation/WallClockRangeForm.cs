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

	public DateTime? StartDate
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	}

	public string StartTimeText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	} = string.Empty;

	public DateTime? EndDate
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	}

	public string EndTimeText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	} = string.Empty;

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

	protected DateTime? StartBound => Bound(StartDate, StartTimeText);

	protected DateTime? EndBound => Bound(EndDate, EndTimeText);

	protected static TimeOnly? ParseTime(string text)
	{
		return TimeOnly.TryParseExact(
			text.Trim(), TimeFieldFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
			? parsed
			: null;
	}

	protected static string FormatTime(DateTime wallClock)
	{
		return wallClock.ToString(TimeFieldFormat, CultureInfo.InvariantCulture);
	}

	internal static DateTime WallClockDay(DateTime utc, TimeZoneInfo timeZone)
	{
		return DateTime.SpecifyKind(ToWallClock(utc, timeZone).Date, DateTimeKind.Unspecified);
	}

	protected static DateTime ToWallClock(DateTime utc, TimeZoneInfo timeZone)
	{
		return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
	}

	protected static DateTime FloorToSecond(DateTime value)
	{
		return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
	}

	/// <summary>Raises every property derived from the four fields.</summary>
	protected virtual void RaiseDerived()
	{
	}

	private static DateTime? Bound(DateTime? date, string timeText)
	{
		return date is { } day && ParseTime(timeText) is { } time ? day.Date + time.ToTimeSpan() : null;
	}
}
