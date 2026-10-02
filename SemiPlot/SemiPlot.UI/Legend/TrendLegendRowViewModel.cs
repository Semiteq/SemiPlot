using System.Reactive;
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
	private readonly ObservableAsPropertyHelper<string> _currentValueText;
	private readonly ObservableAsPropertyHelper<bool> _isActive;
	private readonly CompositeDisposable _subscriptions = [];

	public TrendLegendRowViewModel(TrendChartViewModel chartViewModel, TrendPenState penState)
	{
		_chartViewModel = chartViewModel;
		PenState = penState;

		_currentValueText = penState
			.WhenAnyValue(state => state.CurrentValue)
			.Select(value => FormatReading(value, penState.Pen.Format))
			.ToProperty(this, row => row.CurrentValueText);
		_subscriptions.Add(_currentValueText);

		_isActive = chartViewModel
			.WhenAnyValue(chart => chart.ActivePenId)
			.Select(activePenId => activePenId == penState.Pen.PenId)
			.ToProperty(this, row => row.IsActive);
		_subscriptions.Add(_isActive);

		ToggleVisibilityCommand = ReactiveCommand.Create(() => SetVisibility(!PenState.IsVisible));
		_subscriptions.Add(ToggleVisibilityCommand);
	}

	public TrendPenState PenState { get; }

	public bool IsActive => _isActive.Value;

	public string CurrentValueText => _currentValueText.Value;

	public ReactiveCommand<Unit, Unit> ToggleVisibilityCommand { get; }

	public void Dispose()
	{
		_subscriptions.Dispose();
	}

	public void Select()
	{
		_chartViewModel.SetActivePen(PenState.Pen.PenId);
	}

	public void SetVisibility(bool isVisible)
	{
		_chartViewModel.SetPenVisibility(PenState.Pen.PenId, isVisible);
	}

	// The provider accepted or dropped the mask already: docs/architecture/charting.md.
	private static string FormatReading(double? value, string? mask)
	{
		return value is { } reading ? PenValueFormat.Format(reading, mask) : Resources.NoValuePlaceholder;
	}
}
