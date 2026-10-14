using System.Globalization;

using ReactiveUI;

namespace SemiPlot.UI.Export;

/// <summary>Read as archive wall-clock time; the zone only prefills the fields from UTC edges.</summary>
public sealed class ExportRangeForm : ReactiveObject
{
	internal const string TimeFieldFormat = "HH:mm:ss";

	internal static readonly TimeSpan MaxSpan = TimeSpan.FromHours(24);

	public ExportRangeForm(DateTime fromUtc, DateTime toUtc, TimeZoneInfo timeZone)
	{
		var start = FloorToSecond(ToWallClock(fromUtc, timeZone));
		var end = CeilToSecond(ToWallClock(toUtc, timeZone));

		StartDate = start.Date;
		StartTimeText = FormatTime(start);
		EndDate = end.Date;
		EndTimeText = FormatTime(end);
	}

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

	public int TickedPenCount
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	}

	public bool IsStartDateInvalid => StartDate is null;

	public bool IsStartTimeInvalid => ParseTime(StartTimeText) is null;

	public bool IsEndDateInvalid => EndDate is null || IsEndOrderProblem;

	public bool IsEndTimeInvalid => ParseTime(EndTimeText) is null || IsEndOrderProblem;

	public bool IsValid => Problem == ExportRangeProblem.None;

	public ExportRangeProblem Problem
	{
		get
		{
			if (StartDate is null)
			{
				return ExportRangeProblem.StartDateMissing;
			}

			if (Bound(StartDate, StartTimeText) is not { } from)
			{
				return ExportRangeProblem.StartUnreadable;
			}

			if (EndDate is null)
			{
				return ExportRangeProblem.EndDateMissing;
			}

			if (Bound(EndDate, EndTimeText) is not { } to)
			{
				return ExportRangeProblem.EndUnreadable;
			}

			if (to <= from)
			{
				return ExportRangeProblem.EndNotAfterStart;
			}

			if (to - from > MaxSpan)
			{
				return ExportRangeProblem.SpanOverLimit;
			}

			return TickedPenCount == 0 ? ExportRangeProblem.NoPenTicked : ExportRangeProblem.None;
		}
	}

	public DateTime? From => IsValid ? Bound(StartDate, StartTimeText) : null;

	public DateTime? To => IsValid ? Bound(EndDate, EndTimeText) : null;

	private bool IsEndOrderProblem =>
		Problem is ExportRangeProblem.EndNotAfterStart or ExportRangeProblem.SpanOverLimit;

	private static DateTime? Bound(DateTime? date, string timeText)
	{
		return date is { } day && ParseTime(timeText) is { } time ? day.Date + time.ToTimeSpan() : null;
	}

	private static TimeOnly? ParseTime(string text)
	{
		return TimeOnly.TryParseExact(
			text.Trim(), TimeFieldFormat, CultureInfo.InvariantCulture, DateTimeStyles.None, out var parsed)
			? parsed
			: null;
	}

	private static string FormatTime(DateTime wallClock)
	{
		return wallClock.ToString(TimeFieldFormat, CultureInfo.InvariantCulture);
	}

	private static DateTime ToWallClock(DateTime utc, TimeZoneInfo timeZone)
	{
		return TimeZoneInfo.ConvertTimeFromUtc(DateTime.SpecifyKind(utc, DateTimeKind.Utc), timeZone);
	}

	private static DateTime FloorToSecond(DateTime value)
	{
		return value.AddTicks(-(value.Ticks % TimeSpan.TicksPerSecond));
	}

	private static DateTime CeilToSecond(DateTime value)
	{
		var remainder = value.Ticks % TimeSpan.TicksPerSecond;

		return remainder == 0 ? value : value.AddTicks(TimeSpan.TicksPerSecond - remainder);
	}

	private void RaiseDerived()
	{
		this.RaisePropertyChanged(nameof(Problem));
		this.RaisePropertyChanged(nameof(IsStartDateInvalid));
		this.RaisePropertyChanged(nameof(IsStartTimeInvalid));
		this.RaisePropertyChanged(nameof(IsEndDateInvalid));
		this.RaisePropertyChanged(nameof(IsEndTimeInvalid));
		this.RaisePropertyChanged(nameof(IsValid));
		this.RaisePropertyChanged(nameof(From));
		this.RaisePropertyChanged(nameof(To));
	}
}
