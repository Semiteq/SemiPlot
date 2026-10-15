using ReactiveUI;

using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Export;

/// <summary>Read as archive wall-clock time; the zone only prefills the fields from UTC edges.</summary>
public sealed class ExportRangeForm : WallClockRangeForm
{
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

	public ExportRangeProblem Problem => RangeProblem switch
	{
		WallClockRangeProblem.None => OwnProblem(),
		WallClockRangeProblem.StartDateMissing => ExportRangeProblem.StartDateMissing,
		WallClockRangeProblem.StartUnreadable => ExportRangeProblem.StartUnreadable,
		WallClockRangeProblem.EndDateMissing => ExportRangeProblem.EndDateMissing,
		WallClockRangeProblem.EndUnreadable => ExportRangeProblem.EndUnreadable,
		WallClockRangeProblem.EndNotAfterStart => ExportRangeProblem.EndNotAfterStart,
		_ => throw new ArgumentOutOfRangeException(nameof(RangeProblem), RangeProblem, null)
	};

	public DateTime? From => IsValid ? RangeBounds?.From : null;

	public DateTime? To => IsValid ? RangeBounds?.To : null;

	private bool IsEndOrderProblem =>
		Problem is ExportRangeProblem.EndNotAfterStart or ExportRangeProblem.SpanOverLimit;

	protected override void RaiseDerived()
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

	private static DateTime CeilToSecond(DateTime value)
	{
		var remainder = value.Ticks % TimeSpan.TicksPerSecond;

		return remainder == 0 ? value : value.AddTicks(TimeSpan.TicksPerSecond - remainder);
	}

	private ExportRangeProblem OwnProblem()
	{
		if (RangeBounds is { } bounds && bounds.To - bounds.From > MaxSpan)
		{
			return ExportRangeProblem.SpanOverLimit;
		}

		return TickedPenCount == 0 ? ExportRangeProblem.NoPenTicked : ExportRangeProblem.None;
	}
}
