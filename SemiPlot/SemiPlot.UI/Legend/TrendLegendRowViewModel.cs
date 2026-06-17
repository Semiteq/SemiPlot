using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Legend;

// One pen's legend row. It mirrors the pen's live state (visibility, current value, value-at-cursor,
// active flag, axis range) from the chart view model and turns the two user gestures back into chart
// calls: the visibility checkbox toggles the pen on the chart, and selecting the row makes it active.
// All chart access is read-only state plus the two existing mutators; no rendering or scale logic.
public sealed class TrendLegendRowViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly TrendPenState _penState;
	private readonly long _penId;
	private readonly CompositeDisposable _subscriptions = new();

	private readonly ObservableAsPropertyHelper<bool> _isActive;
	private readonly ObservableAsPropertyHelper<double?> _currentValue;
	private readonly ObservableAsPropertyHelper<double?> _cursorValue;
	private readonly ObservableAsPropertyHelper<(double Min, double Max)?> _scaleRange;

	private bool _isVisible;
	private bool _isSettingVisibilityFromChart;

	public TrendLegendRowViewModel(TrendChartViewModel chartViewModel, TrendPenState penState)
	{
		ArgumentNullException.ThrowIfNull(chartViewModel);
		ArgumentNullException.ThrowIfNull(penState);

		_chartViewModel = chartViewModel;
		_penState = penState;
		_penId = penState.Pen.ProjectVarId;
		_isVisible = penState.IsVisible;

		_currentValue = penState
			.WhenAnyValue(state => state.CurrentValue)
			.ToProperty(this, row => row.CurrentValue);
		_subscriptions.Add(_currentValue);

		_subscriptions.Add(penState
			.WhenAnyValue(state => state.IsVisible)
			.Subscribe(MirrorVisibilityFromChart));

		_isActive = chartViewModel
			.WhenAnyValue(chart => chart.ActivePenId)
			.Select(activePenId => activePenId == _penId)
			.ToProperty(this, row => row.IsActive);
		_subscriptions.Add(_isActive);

		_cursorValue = chartViewModel
			.WhenAnyValue(chart => chart.CursorValues)
			.Select(values => values.TryGetValue(_penId, out var value) ? value : null)
			.ToProperty(this, row => row.CursorValue);
		_subscriptions.Add(_cursorValue);

		_scaleRange = chartViewModel
			.WhenAnyValue(chart => chart.ScalesRevision)
			.Select(_ => chartViewModel.ScaleRangeForPen(_penId))
			.ToProperty(this, row => row.ScaleRange);
		_subscriptions.Add(_scaleRange);

		_subscriptions.Add(this
			.WhenAnyValue(row => row.CurrentValue)
			.Subscribe(_ => this.RaisePropertyChanged(nameof(CurrentValueText))));
		_subscriptions.Add(this
			.WhenAnyValue(row => row.CursorValue)
			.Subscribe(_ => this.RaisePropertyChanged(nameof(CursorValueText))));
		_subscriptions.Add(this
			.WhenAnyValue(row => row.ScaleRange)
			.Subscribe(_ => this.RaisePropertyChanged(nameof(ScaleRangeText))));
	}

	public string Name => _penState.Pen.Name;

	public string GroupName => _penState.Pen.Group;

	public string ColorHex => _penState.Pen.Color;

	public bool IsActive => _isActive.Value;

	public double? CurrentValue => _currentValue.Value;

	public double? CursorValue => _cursorValue.Value;

	public (double Min, double Max)? ScaleRange => _scaleRange.Value;

	public string CurrentValueText => FormatValue(CurrentValue);

	public string CursorValueText => FormatValue(CursorValue);

	public string ScaleRangeText
	{
		get
		{
			var range = ScaleRange;
			return range is { } value
				? $"{value.Min:0.###}..{value.Max:0.###}"
				: "—";
		}
	}

	// Two-way bound to the checkbox; toggling it propagates the new visibility to the pen on the chart.
	public bool IsVisible
	{
		get => _isVisible;
		set
		{
			this.RaiseAndSetIfChanged(ref _isVisible, value);
			if (!_isSettingVisibilityFromChart)
			{
				_chartViewModel.SetPenVisibility(_penId, value);
			}
		}
	}

	// Selecting the row (clicking it) makes this pen the chart's active pen.
	public void Select()
	{
		_chartViewModel.SetActivePen(_penId);
	}

	public void Dispose()
	{
		_subscriptions.Dispose();
	}

	private static string FormatValue(double? value)
	{
		return value is { } number ? number.ToString("0.###") : "—";
	}

	private void MirrorVisibilityFromChart(bool isVisible)
	{
		_isSettingVisibilityFromChart = true;
		try
		{
			IsVisible = isVisible;
		}
		finally
		{
			_isSettingVisibilityFromChart = false;
		}
	}
}
