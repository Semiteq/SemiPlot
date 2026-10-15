using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

namespace SemiPlot.UI.Chart;

public sealed record TimeWindowPreset(string Label, TimeSpan Width);

/// <summary>Sets the view window by its local edges or by its width; one day read per open.</summary>
public sealed class TimeWindowPanelViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<Unit> _closeRequests = new();
	private readonly SerialDisposable _dayRead = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly TrendChartViewModel _chart;
	private readonly TrendCoordinator _coordinator;
	private readonly IScheduler _uiScheduler;
	private DayCoverage? _dayCoverage;
	private DateTime? _newestDay;
	private bool _isCancelled;

	public TimeWindowPanelViewModel(
		TrendChartViewModel chart,
		TrendCoordinator coordinator,
		IScheduler uiScheduler,
		TimeZoneInfo timeZone)
	{
		_chart = chart;
		_coordinator = coordinator;
		_uiScheduler = uiScheduler;
		Form = new TimeWindowForm(timeZone);

		_disposables.Add(_closeRequests);
		_disposables.Add(_dayRead);
		_disposables.Add(CommitCommand = ReactiveCommand.Create(Commit));
		_disposables.Add(SetWidthCommand = ReactiveCommand.Create<TimeSpan>(SetWidth));
		_disposables.Add(CancelCommand = ReactiveCommand.Create(Cancel));
	}

	/// <summary>The panel asks its host to close through this; the host owns the flyout.</summary>
	public IObservable<Unit> CloseRequests => _closeRequests.AsObservable();

	public ReactiveCommand<Unit, Unit> CommitCommand { get; }

	public ReactiveCommand<TimeSpan, Unit> SetWidthCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

	public TimeWindowForm Form { get; }

	public IReadOnlyList<TimeWindowPreset> Presets { get; } =
	[
		new(Resources.TimeWindowPresetOneMinute, TimeSpan.FromMinutes(1)),
		new(Resources.TimeWindowPresetTwoMinutes, TimeSpan.FromMinutes(2)),
		new(Resources.TimeWindowPresetThreeMinutes, TimeSpan.FromMinutes(3)),
		new(Resources.TimeWindowPresetFiveMinutes, TimeSpan.FromMinutes(5)),
		new(Resources.TimeWindowPresetTenMinutes, TimeSpan.FromMinutes(10)),
		new(Resources.TimeWindowPresetThirtyMinutes, TimeSpan.FromMinutes(30)),
		new(Resources.TimeWindowPresetOneHour, TimeSpan.FromHours(1))
	];

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

	/// <summary>Fills the form from the window in force; empties the blackout days before it writes the dates.</summary>
	public void Seed()
	{
		_isCancelled = false;
		BlackoutDays = [];

		var navigation = _chart.Navigation;
		Form.Seed(navigation.From, navigation.To);

		var startDay = Form.LocalDay(navigation.From);
		var endDay = Form.LocalDay(navigation.To);
		_newestDay = navigation.NewestSample is { } newestSample ? Form.LocalDay(newestSample) : null;
		FirstDay = Earlier(Form.LocalDay(navigation.FirstSample), startDay);
		LastDay = Later(_newestDay ?? endDay, endDay);
		BlackoutDays = UnselectedDaysWithoutRows();
	}

	/// <summary>Reads which days of [FirstDay, the newest sample's day] hold a row.</summary>
	public void Opened()
	{
		if (_newestDay is not { } newestDay || newestDay < FirstDay)
		{
			return;
		}

		var firstDay = DateOnly.FromDateTime(FirstDay);
		var lastDay = DateOnly.FromDateTime(newestDay);
		_dayRead.Disposable = Observable
			.FromAsync(cancellationToken => _coordinator.QueryArchiveDaysAsync(firstDay, lastDay, cancellationToken))
			.ObserveOn(_uiScheduler)
			.Subscribe(result => TryApplyDays(result, firstDay, lastDay), _chart.ReportFailure);
	}

	public void Commit()
	{
		var wroteEdges = WritePendingEdges();
		var wroteWidth = WritePendingWidth();

		if (wroteEdges || wroteWidth)
		{
			Seed();
		}
	}

	public void Close()
	{
		if (!_isCancelled)
		{
			Commit();
		}

		_dayRead.Disposable = null;
		_dayCoverage = null;
	}

	public void ReportFailure(Exception failure)
	{
		_chart.ReportFailure(failure);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void SetWidth(TimeSpan width)
	{
		_chart.Navigation.SetWidth(width);
		Seed();
	}

	private void Cancel()
	{
		_isCancelled = true;
		_closeRequests.OnNext(Unit.Default);
	}

	private bool WritePendingEdges()
	{
		if (!Form.EdgesDifferFromSeed || Form.From is not { } typedFrom || Form.To is not { } typedTo)
		{
			return false;
		}

		// docs/architecture/trend-interaction.md#the-time-window-panel
		var navigation = _chart.Navigation;
		var from = Form.StartDiffersFromSeed ? typedFrom : navigation.From;
		var to = Form.EndDiffersFromSeed ? typedTo : navigation.To;
		var isResolvedPairValid = to > from && to - from <= TrendNavigationModel.MaximumWidth;
		navigation.SetWindow(isResolvedPairValid ? from : typedFrom, isResolvedPairValid ? to : typedTo);

		return true;
	}

	private bool WritePendingWidth()
	{
		if (!Form.WidthDiffersFromSeed || Form.Width is not { } width)
		{
			return false;
		}

		_chart.Navigation.SetWidth(width);

		return true;
	}

	private void TryApplyDays(Result<IReadOnlyList<DateOnly>> result, DateOnly firstDay, DateOnly lastDay)
	{
		try
		{
			if (result.IsFailed)
			{
				_chart.ReportFailure(result.Errors);

				return;
			}

			_dayCoverage = new DayCoverage(firstDay, lastDay, [.. result.Value]);
			BlackoutDays = UnselectedDaysWithoutRows();
		}
		catch (Exception applyFailure)
		{
			_chart.ReportFailure(applyFailure);
		}
	}

	private List<DateTime> UnselectedDaysWithoutRows()
	{
		var days = new List<DateTime>();

		if (_dayCoverage is not { } coverage)
		{
			return days;
		}

		var first = Later(coverage.FirstDay.ToDateTime(TimeOnly.MinValue), FirstDay);
		var last = Earlier(coverage.LastDay.ToDateTime(TimeOnly.MinValue), LastDay);

		for (var day = first; day <= last; day = day.AddDays(1))
		{
			if (!coverage.DaysWithRows.Contains(DateOnly.FromDateTime(day)) && !IsSelected(day))
			{
				days.Add(day);
			}
		}

		return days;
	}

	private bool IsSelected(DateTime day)
	{
		return day == Form.StartDate?.Date || day == Form.EndDate?.Date;
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
