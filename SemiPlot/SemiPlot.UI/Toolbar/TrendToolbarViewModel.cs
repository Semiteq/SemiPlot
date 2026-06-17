using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Toolbar;

// Exposes the chart actions as ReactiveUI commands so the toolbar duplicates the axis gestures
// (autoscale / fixed limits) and carries the layer indicator plus the jump-to-now and sticky-toggle
// controls. The axis commands operate on the active pen; jump-to-now and the sticky toggle drive the
// chart's navigation controller, and the sticky flag mirrors the controller's state. The aggregation
// layer is chosen automatically from the zoom width, so the layer property is a read-only reflection of
// the controller's active layer rather than a user-settable input.
public sealed class TrendToolbarViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly CompositeDisposable _disposables = new();

	private AggregationLayer _activeLayer;
	private double _manualMin;
	private double _manualMax = 1.0;
	private bool _isSticky;
	private bool _deltaCursorsEnabled;

	public TrendToolbarViewModel(TrendChartViewModel chartViewModel)
	{
		ArgumentNullException.ThrowIfNull(chartViewModel);
		_chartViewModel = chartViewModel;
		_isSticky = chartViewModel.Navigation.IsSticky;
		_activeLayer = chartViewModel.Navigation.ActiveLayer;

		_disposables.Add(AutoscaleActiveAxisCommand = ReactiveCommand.Create(AutoscaleActiveAxis));
		_disposables.Add(SetActiveAxisLimitsCommand = ReactiveCommand.Create(SetActiveAxisLimits));
		_disposables.Add(JumpToNowCommand = ReactiveCommand.Create(JumpToNow));
		_disposables.Add(ToggleStickyCommand = ReactiveCommand.Create(ToggleSticky));
		_disposables.Add(ToggleDeltaCursorsCommand = ReactiveCommand.Create(ToggleDeltaCursors));

		_chartViewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_chartViewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));
	}

	public ReactiveCommand<Unit, Unit> AutoscaleActiveAxisCommand { get; }

	public ReactiveCommand<Unit, Unit> SetActiveAxisLimitsCommand { get; }

	public ReactiveCommand<Unit, Unit> JumpToNowCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleStickyCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleDeltaCursorsCommand { get; }

	// Read-only reflection of the layer auto-selected from the current zoom width.
	public AggregationLayer ActiveLayer
	{
		get => _activeLayer;
		private set => this.RaiseAndSetIfChanged(ref _activeLayer, value);
	}

	public double ManualMin
	{
		get => _manualMin;
		set => this.RaiseAndSetIfChanged(ref _manualMin, value);
	}

	public double ManualMax
	{
		get => _manualMax;
		set => this.RaiseAndSetIfChanged(ref _manualMax, value);
	}

	public bool IsSticky
	{
		get => _isSticky;
		private set => this.RaiseAndSetIfChanged(ref _isSticky, value);
	}

	public bool DeltaCursorsEnabled
	{
		get => _deltaCursorsEnabled;
		private set => this.RaiseAndSetIfChanged(ref _deltaCursorsEnabled, value);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ActiveLayer = window.Layer;
		IsSticky = _chartViewModel.Navigation.IsSticky;
	}

	private void AutoscaleActiveAxis()
	{
		_chartViewModel.AutoscaleAxis(_chartViewModel.ActivePenId);
	}

	private void SetActiveAxisLimits()
	{
		_chartViewModel.SetAxisLimits(_chartViewModel.ActivePenId, _manualMin, _manualMax);
	}

	private void JumpToNow()
	{
		_chartViewModel.Navigation.JumpToNow();
	}

	private void ToggleSticky()
	{
		_chartViewModel.Navigation.SetSticky(!_chartViewModel.Navigation.IsSticky);
	}

	private void ToggleDeltaCursors()
	{
		_chartViewModel.SetDeltaCursorsEnabled(!_chartViewModel.DeltaCursorsEnabled);
		DeltaCursorsEnabled = _chartViewModel.DeltaCursorsEnabled;
	}
}
