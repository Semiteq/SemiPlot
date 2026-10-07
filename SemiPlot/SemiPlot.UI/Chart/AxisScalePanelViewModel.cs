using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Chart;

// docs/architecture/trend-interaction.md#the-axis-scale-panel
public sealed class AxisScalePanelViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<Unit> _closeRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly TrendChartViewModel _chart;
	private int? _penId;
	private SeededBound _seededMaximum;
	private SeededBound _seededMinimum;
	private bool _isLogarithmicRefused;

	public AxisScalePanelViewModel(TrendChartViewModel chart)
	{
		_chart = chart;

		_disposables.Add(_closeRequests);
		_disposables.Add(CommitBoundsCommand = ReactiveCommand.Create(CommitBounds));
		_disposables.Add(AutoscaleCommand = ReactiveCommand.Create(() => ActOnThePen(_chart.AutoscalePen)));
		_disposables.Add(InitialScaleCommand = ReactiveCommand.Create(
			() => ActOnThePen(_chart.RestoreInitialScale)));
		_disposables.Add(CancelCommand = ReactiveCommand.Create(RequestClose));
		_disposables.Add(ToggleLogarithmicCommand = ReactiveCommand.Create(ToggleLogarithmic));
		_disposables.Add(_chart
			.WhenAnyValue(chart => chart.DrawnPenId)
			.Subscribe(CloseWhenThePenIsNotDrawn, _chart.ReportFailure));
	}

	/// <summary>The panel asks its host to close through this; the host owns the flyout.</summary>
	public IObservable<Unit> CloseRequests => _closeRequests.AsObservable();

	public ReactiveCommand<Unit, Unit> CommitBoundsCommand { get; }

	public ReactiveCommand<Unit, Unit> AutoscaleCommand { get; }

	public ReactiveCommand<Unit, Unit> InitialScaleCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleLogarithmicCommand { get; }

	public string PenName
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>Empty when the pen stores no unit.</summary>
	public string PenUnit
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public string MaximumText
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseValidationChanged();
		}
	} = string.Empty;

	public string MinimumText
	{
		get;
		set
		{
			if (!string.Equals(field, value, StringComparison.Ordinal))
			{
				_isLogarithmicRefused = false;
			}

			this.RaiseAndSetIfChanged(ref field, value);
			RaiseValidationChanged();
		}
	} = string.Empty;

	/// <summary>The pen's axis type in this window, as the chart held it at the last seed.</summary>
	public bool IsLogarithmic
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseValidationChanged();
		}
	}

	public bool IsMaximumValid => TryReadMaximum(out _) && !IsInverted;

	public bool IsMinimumValid => TryReadMinimum(out _) && !IsInverted && !BreaksLogMinimum;

	/// <summary>The rule the pair breaks, or an empty string while it is valid.</summary>
	public string ValidationMessage
	{
		get
		{
			if (_seededMaximum.IsUnreadable(MaximumText) || _seededMinimum.IsUnreadable(MinimumText))
			{
				return Resources.AxisScaleBoundInvalid;
			}

			if (_seededMaximum.IsEmpty(MaximumText) || _seededMinimum.IsEmpty(MinimumText))
			{
				return Resources.AxisScaleBoundsRequired;
			}

			if (IsInverted)
			{
				return Resources.AxisScaleMinimumBelowMaximum;
			}

			return BreaksLogMinimum || _isLogarithmicRefused ? Resources.ScaleLogMinimumPositive : string.Empty;
		}
	}

	private bool IsInverted =>
		TryReadMaximum(out var maximum)
		&& TryReadMinimum(out var minimum)
		&& minimum >= maximum;

	private bool BreaksLogMinimum =>
		IsLogarithmic
		&& TryReadMinimum(out var minimum)
		&& !LogAxis.AdmitsMinimum(minimum);

	/// <summary>Fills the fields from the drawn pen; false when no pen's axis can be edited.</summary>
	public bool Seed()
	{
		if (_chart.DrawnPenId is not { } penId
			|| _chart.FindPen(penId) is not { } state
			|| _chart.ScaleSettings.GetValueOrDefault(penId) is not { } settings
			|| _chart.ScaleRangeForPen(penId) is not { } range)
		{
			return false;
		}

		_penId = penId;
		_isLogarithmicRefused = false;
		IsLogarithmic = settings.IsLogarithmic;
		PenName = state.Pen.Name;
		PenUnit = state.Pen.Unit ?? string.Empty;
		_seededMaximum = new SeededBound(PenValueFormat.Format(range.Max, state.Pen.Format), range.Max);
		_seededMinimum = new SeededBound(PenValueFormat.Format(range.Min, state.Pen.Format), range.Min);
		MaximumText = _seededMaximum.Text;
		MinimumText = _seededMinimum.Text;

		return true;
	}

	/// <summary>Writes a valid pair that differs from the seeded one and re-seeds; the panel stays open.</summary>
	public void CommitBounds()
	{
		if (_penId is { } penId && WritePendingPair(penId))
		{
			Seed();
		}
	}

	public void ReportFailure(Exception failure)
	{
		_chart.ReportFailure(failure);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private bool WritePendingPair(int penId)
	{
		if (!TryReadPair(out var minimum, out var maximum) || BreaksLogMinimum || IsSeededPair(minimum, maximum))
		{
			return false;
		}

		return _chart.SetAxisLimits(penId, minimum, maximum);
	}

	private bool IsSeededPair(double minimum, double maximum)
	{
		return minimum.Equals(_seededMinimum.Value) && maximum.Equals(_seededMaximum.Value);
	}

	private void ActOnThePen(Func<int, bool> action)
	{
		if (_penId is { } penId)
		{
			action(penId);
		}

		RequestClose();
	}

	private void ToggleLogarithmic()
	{
		if (_penId is not { } penId || _chart.ScaleSettings.GetValueOrDefault(penId) is not { } settings)
		{
			return;
		}

		if (!TryReadPair(out _, out _))
		{
			KeepTheBoxAsItWas();

			return;
		}

		if (settings.IsLogarithmic)
		{
			SwitchLogarithmicOff(penId);

			return;
		}

		WritePendingPair(penId);
		var isSwitchedOn = _chart.SetLogarithmic(penId, true);
		Seed();

		if (!isSwitchedOn)
		{
			_isLogarithmicRefused = true;
			KeepTheBoxAsItWas();
		}
	}

	private void SwitchLogarithmicOff(int penId)
	{
		if (_chart.SetLogarithmic(penId, false))
		{
			IsLogarithmic = false;
			WritePendingPair(penId);
		}

		Seed();
	}

	// The box ticks itself before the command runs; only a notification makes its binding read the flag again.
	private void KeepTheBoxAsItWas()
	{
		this.RaisePropertyChanged(nameof(IsLogarithmic));
		this.RaisePropertyChanged(nameof(ValidationMessage));
	}

	private void CloseWhenThePenIsNotDrawn(int? drawnPenId)
	{
		if (_penId is { } penId && drawnPenId != penId)
		{
			RequestClose();
		}
	}

	private void RequestClose()
	{
		_penId = null;
		_closeRequests.OnNext(Unit.Default);
	}

	private void RaiseValidationChanged()
	{
		this.RaisePropertyChanged(nameof(IsMaximumValid));
		this.RaisePropertyChanged(nameof(IsMinimumValid));
		this.RaisePropertyChanged(nameof(ValidationMessage));
	}

	private bool TryReadPair(out double minimum, out double maximum)
	{
		maximum = 0.0;

		return TryReadMinimum(out minimum)
			&& TryReadMaximum(out maximum)
			&& minimum < maximum;
	}

	private bool TryReadMaximum(out double bound)
	{
		return _seededMaximum.TryRead(MaximumText, out bound);
	}

	private bool TryReadMinimum(out double bound)
	{
		return _seededMinimum.TryRead(MinimumText, out bound);
	}
}
