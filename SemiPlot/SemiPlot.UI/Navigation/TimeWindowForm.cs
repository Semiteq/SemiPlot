using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Navigation;

/// <summary>The first rule a time window breaks, in the order the panel shows them.</summary>
public enum TimeWindowProblem
{
	None,
	StartDateMissing,
	StartUnreadable,
	EndDateMissing,
	EndUnreadable,
	EndNotAfterStart,
	TimeSkipped,
	SpanOverLimit,
	WidthUnreadable,
	WidthOutOfRange
}

/// <summary>The view window as local edges and a width; the edges and the width validate apart.</summary>
public sealed class TimeWindowForm(TimeZoneInfo timeZone) : WallClockRangeForm
{
	private (DateTime? Date, string TimeText) _seededStart = (null, string.Empty);
	private (DateTime? Date, string TimeText) _seededEnd = (null, string.Empty);
	private string _seededWidthText = string.Empty;

	public string WidthText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseDerived();
		}
	} = string.Empty;

	public bool IsStartDateInvalid => StartDate is null;

	public bool IsStartTimeInvalid => ParseTime(StartTimeText) is null || IsSkipped(StartBound);

	public bool IsEndDateInvalid => EndDate is null || IsEndOrderProblem;

	public bool IsEndTimeInvalid => ParseTime(EndTimeText) is null || IsSkipped(EndBound) || IsEndOrderProblem;

	public bool IsWidthInvalid => WidthProblem != TimeWindowProblem.None;

	public TimeWindowProblem Problem => EdgeProblem is var edge and not TimeWindowProblem.None ? edge : WidthProblem;

	public string Message => Problem switch
	{
		TimeWindowProblem.None => string.Empty,
		TimeWindowProblem.StartDateMissing => Resources.TimeWindowMessageStartDateMissing,
		TimeWindowProblem.StartUnreadable => Resources.FormatTimeWindowMessageStartUnreadable(TimeFieldFormat),
		TimeWindowProblem.EndDateMissing => Resources.TimeWindowMessageEndDateMissing,
		TimeWindowProblem.EndUnreadable => Resources.FormatTimeWindowMessageEndUnreadable(TimeFieldFormat),
		TimeWindowProblem.EndNotAfterStart => Resources.TimeWindowMessageEndNotAfterStart,
		TimeWindowProblem.TimeSkipped => Resources.TimeWindowMessageTimeSkipped,
		TimeWindowProblem.SpanOverLimit =>
			Resources.FormatTimeWindowMessageSpanOverLimit(TrendNavigationModel.MaximumWidth.TotalDays),
		TimeWindowProblem.WidthUnreadable =>
			Resources.FormatTimeWindowMessageWidthUnreadable(WindowWidthText.FieldFormat),
		TimeWindowProblem.WidthOutOfRange => Resources.FormatTimeWindowMessageWidthOutOfRange(
			WindowWidthText.Format(TrendNavigationModel.MinimumWidth),
			WindowWidthText.Format(TrendNavigationModel.MaximumWidth)),
		_ => throw new ArgumentOutOfRangeException(nameof(Problem), Problem, null)
	};

	/// <summary>The left edge in UTC, or null while the pair breaks a rule; the width text does not count.</summary>
	public DateTime? From => UtcEdges?.From;

	/// <summary>The right edge in UTC, or null while the pair breaks a rule; the width text does not count.</summary>
	public DateTime? To => UtcEdges?.To;

	/// <summary>The typed width, or null while it breaks a rule; the pair does not count.</summary>
	public TimeSpan? Width => WidthProblem == TimeWindowProblem.None ? ParsedWidth : null;

	public bool StartDiffersFromSeed => (StartDate?.Date, StartTimeText) != _seededStart;

	public bool EndDiffersFromSeed => (EndDate?.Date, EndTimeText) != _seededEnd;

	public bool EdgesDifferFromSeed => StartDiffersFromSeed || EndDiffersFromSeed;

	public bool WidthDiffersFromSeed => WidthText != _seededWidthText;

	private TimeWindowProblem EdgeProblem => RangeProblem switch
	{
		WallClockRangeProblem.None => OwnEdgeProblem(),
		WallClockRangeProblem.StartDateMissing => TimeWindowProblem.StartDateMissing,
		WallClockRangeProblem.StartUnreadable => TimeWindowProblem.StartUnreadable,
		WallClockRangeProblem.EndDateMissing => TimeWindowProblem.EndDateMissing,
		WallClockRangeProblem.EndUnreadable => TimeWindowProblem.EndUnreadable,
		WallClockRangeProblem.EndNotAfterStart => TimeWindowProblem.EndNotAfterStart,
		_ => throw new ArgumentOutOfRangeException(nameof(RangeProblem), RangeProblem, null)
	};

	private TimeWindowProblem WidthProblem
	{
		get
		{
			if (ParsedWidth is not { } width)
			{
				return TimeWindowProblem.WidthUnreadable;
			}

			return width >= TrendNavigationModel.MinimumWidth && width <= TrendNavigationModel.MaximumWidth
				? TimeWindowProblem.None
				: TimeWindowProblem.WidthOutOfRange;
		}
	}

	private TimeSpan? ParsedWidth => WindowWidthText.TryParse(WidthText, out var width) ? width : null;

	private (DateTime From, DateTime To)? UtcEdges =>
		EdgeProblem == TimeWindowProblem.None && RangeBounds is { } bounds
			? (ToUtc(bounds.From), ToUtc(bounds.To))
			: null;

	private bool IsEndOrderProblem =>
		EdgeProblem is TimeWindowProblem.EndNotAfterStart or TimeWindowProblem.SpanOverLimit;

	public void Seed(DateTime fromUtc, DateTime toUtc)
	{
		var start = FloorToSecond(ToWallClock(fromUtc, timeZone));
		var end = FloorToSecond(ToWallClock(toUtc, timeZone));

		StartDate = start.Date;
		StartTimeText = FormatTime(start);
		EndDate = end.Date;
		EndTimeText = FormatTime(end);
		WidthText = WindowWidthText.Format(toUtc - fromUtc);

		_seededStart = (StartDate, StartTimeText);
		_seededEnd = (EndDate, EndTimeText);
		_seededWidthText = WidthText;
	}

	protected override void RaiseDerived()
	{
		this.RaisePropertyChanged(nameof(Problem));
		this.RaisePropertyChanged(nameof(Message));
		this.RaisePropertyChanged(nameof(IsStartDateInvalid));
		this.RaisePropertyChanged(nameof(IsStartTimeInvalid));
		this.RaisePropertyChanged(nameof(IsEndDateInvalid));
		this.RaisePropertyChanged(nameof(IsEndTimeInvalid));
		this.RaisePropertyChanged(nameof(IsWidthInvalid));
		this.RaisePropertyChanged(nameof(From));
		this.RaisePropertyChanged(nameof(To));
		this.RaisePropertyChanged(nameof(Width));
	}

	private TimeWindowProblem OwnEdgeProblem()
	{
		if (IsSkipped(StartBound) || IsSkipped(EndBound))
		{
			return TimeWindowProblem.TimeSkipped;
		}

		return RangeBounds is { } bounds && ToUtc(bounds.To) - ToUtc(bounds.From) > TrendNavigationModel.MaximumWidth
			? TimeWindowProblem.SpanOverLimit
			: TimeWindowProblem.None;
	}

	private bool IsSkipped(DateTime? wallClock)
	{
		return wallClock is { } value && timeZone.IsInvalidTime(DateTime.SpecifyKind(value, DateTimeKind.Unspecified));
	}

	// An ambiguous autumn hour resolves to the standard offset: docs/architecture/data-integration.md#time-boundary
	private DateTime ToUtc(DateTime wallClock)
	{
		return TimeZoneInfo.ConvertTimeToUtc(DateTime.SpecifyKind(wallClock, DateTimeKind.Unspecified), timeZone);
	}
}
