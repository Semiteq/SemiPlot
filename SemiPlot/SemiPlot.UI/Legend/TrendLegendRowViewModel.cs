using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Legend;

public sealed class TrendLegendRowViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly TrendPenState _penState;
	private readonly int _penId;
	private readonly ObservableAsPropertyHelper<double?> _currentValue;
	private readonly ObservableAsPropertyHelper<string> _currentValueText;
	private readonly ObservableAsPropertyHelper<bool> _isActive;
	private readonly CompositeDisposable _subscriptions = [];
	private bool _isVisible;
	private bool _isSettingVisibilityFromChart;

	public TrendLegendRowViewModel(TrendChartViewModel chartViewModel, TrendPenState penState)
	{
		_chartViewModel = chartViewModel;
		_penState = penState;
		_penId = penState.Pen.PenId;
		_isVisible = penState.IsVisible;

		_currentValue = penState
			.WhenAnyValue(state => state.CurrentValue)
			.ToProperty(this, row => row.CurrentValue);
		_subscriptions.Add(_currentValue);

		_currentValueText = penState
			.WhenAnyValue(state => state.CurrentValue)
			.Select(value => FormatReading(value, penState.Pen.Format))
			.ToProperty(this, row => row.CurrentValueText);
		_subscriptions.Add(_currentValueText);

		_subscriptions.Add(penState
			.WhenAnyValue(state => state.IsVisible)
			.Subscribe(MirrorVisibilityFromChart));

		_isActive = chartViewModel
			.WhenAnyValue(chart => chart.ActivePenId)
			.Select(activePenId => activePenId == _penId)
			.ToProperty(this, row => row.IsActive);
		_subscriptions.Add(_isActive);
	}

	public string Name => _penState.Pen.Name;

	public IReadOnlyList<string> Groups => _penState.Pen.Groups;

	public string ColorHex => _penState.Pen.Color;

	public string Unit => _penState.Pen.Unit ?? string.Empty;

	public bool IsActive => _isActive.Value;

	public double? CurrentValue => _currentValue.Value;

	public string CurrentValueText => _currentValueText.Value;

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

	public void Dispose()
	{
		_subscriptions.Dispose();
	}

	public void Select()
	{
		_chartViewModel.SetActivePen(_penId);
	}

	// The provider accepted or dropped the mask already: docs/architecture/charting.md.
	private static string FormatReading(double? value, string? mask)
	{
		return value is { } reading ? PenValueFormat.Format(reading, mask) : Resources.NoValuePlaceholder;
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
