using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.PenEditor;

namespace SemiPlot.UI.Chart;

// The panel that edits one pen's two scale bounds, docs/architecture/trend-interaction.md#the-axis-scale-panel
public sealed class AxisScalePanelViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<Unit> _closeRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly TrendChartViewModel _chart;
	private int? _penId;
	private SeededBound _seededMaximum;
	private SeededBound _seededMinimum;

	public AxisScalePanelViewModel(TrendChartViewModel chart)
	{
		_chart = chart;

		_disposables.Add(_closeRequests);
		_disposables.Add(ApplyCommand = ReactiveCommand.Create(
			Apply,
			this.WhenAnyValue(panel => panel.IsValid)));
		_disposables.Add(AutoscaleCommand = ReactiveCommand.Create(() => ActOnThePen(_chart.AutoscalePen)));
		_disposables.Add(InitialScaleCommand = ReactiveCommand.Create(
			() => ActOnThePen(_chart.RestoreInitialScale)));
		_disposables.Add(CancelCommand = ReactiveCommand.Create(RequestClose));
		_disposables.Add(_chart
			.WhenAnyValue(chart => chart.DrawnPenId)
			.Subscribe(CloseWhenThePenIsNotDrawn, _chart.ReportFailure));
	}

	/// <summary>The panel asks its host to close through this; the host owns the flyout.</summary>
	public IObservable<Unit> CloseRequests => _closeRequests.AsObservable();

	public ReactiveCommand<Unit, Unit> ApplyCommand { get; }

	public ReactiveCommand<Unit, Unit> AutoscaleCommand { get; }

	public ReactiveCommand<Unit, Unit> InitialScaleCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelCommand { get; }

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
			this.RaiseAndSetIfChanged(ref field, value);
			RaiseValidationChanged();
		}
	} = string.Empty;

	public bool IsMaximumValid => TryReadMaximum(out _) && !IsInverted;

	public bool IsMinimumValid => TryReadMinimum(out _) && !IsInverted;

	public bool IsValid => TryReadPair(out _, out _);

	/// <summary>The rule the pair breaks, or an empty string while it is valid.</summary>
	public string ValidationMessage
	{
		get
		{
			if (IsUnreadable(MaximumText, _seededMaximum) || IsUnreadable(MinimumText, _seededMinimum))
			{
				return Resources.AxisScaleBoundInvalid;
			}

			if (IsEmpty(MaximumText, _seededMaximum) || IsEmpty(MinimumText, _seededMinimum))
			{
				return Resources.AxisScaleBoundsRequired;
			}

			return IsInverted ? Resources.AxisScaleMinimumBelowMaximum : string.Empty;
		}
	}

	private bool IsInverted =>
		TryReadMaximum(out var maximum)
		&& TryReadMinimum(out var minimum)
		&& minimum >= maximum;

	/// <summary>Fills the fields from the drawn pen; false when no pen's axis can be edited.</summary>
	public bool Seed()
	{
		if (_chart.DrawnPenId is not { } penId
			|| _chart.FindPen(penId) is not { } state
			|| _chart.ScaleRangeForPen(penId) is not { } range)
		{
			return false;
		}

		_penId = penId;
		PenName = state.Pen.Name;
		PenUnit = state.Pen.Unit ?? string.Empty;
		_seededMaximum = new SeededBound(PenValueFormat.Format(range.Max, state.Pen.Format), range.Max);
		_seededMinimum = new SeededBound(PenValueFormat.Format(range.Min, state.Pen.Format), range.Min);
		MaximumText = _seededMaximum.Text;
		MinimumText = _seededMinimum.Text;

		return true;
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void Apply()
	{
		if (_penId is { } penId && TryReadPair(out var minimum, out var maximum))
		{
			_chart.SetAxisLimits(penId, minimum, maximum);
			RequestClose();
		}
	}

	private void ActOnThePen(Func<int, bool> action)
	{
		if (_penId is { } penId)
		{
			action(penId);
		}

		RequestClose();
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
		this.RaisePropertyChanged(nameof(IsValid));
		this.RaisePropertyChanged(nameof(ValidationMessage));
	}

	private static bool IsUnreadable(string text, SeededBound seeded)
	{
		return !seeded.Matches(text) && !PenFormRules.TryReadBound(text, out _);
	}

	private static bool IsEmpty(string text, SeededBound seeded)
	{
		return !seeded.Matches(text) && string.IsNullOrWhiteSpace(text);
	}

	private bool TryReadPair(out double minimum, out double maximum)
	{
		minimum = 0.0;
		maximum = 0.0;

		return TryReadMinimum(out minimum)
			&& TryReadMaximum(out maximum)
			&& minimum < maximum;
	}

	private bool TryReadMaximum(out double bound)
	{
		return TryReadRequired(MaximumText, _seededMaximum, out bound);
	}

	private bool TryReadMinimum(out double bound)
	{
		return TryReadRequired(MinimumText, _seededMinimum, out bound);
	}

	// An empty field is no bound, which the pen editor allows and this panel does not. A field left as
	// seeded keeps the exact bound, so a mask that rounds never moves the scale.
	private static bool TryReadRequired(string text, SeededBound seeded, out double bound)
	{
		bound = 0.0;

		if (seeded.Matches(text))
		{
			bound = seeded.Value;

			return true;
		}

		if (!PenFormRules.TryReadBound(text, out var read) || read is not { } value)
		{
			return false;
		}

		bound = value;

		return true;
	}

	private readonly record struct SeededBound(string Text, double Value)
	{
		public bool Matches(string text)
		{
			return Text is not null && string.Equals(text, Text, StringComparison.Ordinal);
		}
	}
}
