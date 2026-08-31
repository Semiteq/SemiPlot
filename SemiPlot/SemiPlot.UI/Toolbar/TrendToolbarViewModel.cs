using System.Reactive;
using System.Reactive.Disposables;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Toolbar;

// The sticky and delta-mode flags mirror their single sources of truth (the navigation controller and
// the chart view model).
public sealed class TrendToolbarViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly CompositeDisposable _disposables = new();

	private AggregationLayer _activeLayer;
	private bool _isDeltaModeEnabled;
	private bool _isSticky;
	private double _manualMax = 1.0;
	private double _manualMin;

	public TrendToolbarViewModel(TrendChartViewModel chartViewModel)
	{
		_chartViewModel = chartViewModel;
		_isSticky = chartViewModel.Navigation.IsSticky;
		_activeLayer = chartViewModel.Navigation.ActiveLayer;

		_disposables.Add(AutoscaleActiveAxisCommand = ReactiveCommand.Create(
			() => { _chartViewModel.AutoscaleAxis(_chartViewModel.ActivePenId); }));
		_disposables.Add(SetActiveAxisLimitsCommand = ReactiveCommand.Create(
			() => { _chartViewModel.SetAxisLimits(_chartViewModel.ActivePenId, _manualMin, _manualMax); }));
		_disposables.Add(JumpToNowCommand = ReactiveCommand.Create(_chartViewModel.Navigation.JumpToNow));
		_disposables.Add(ToggleStickyCommand = ReactiveCommand.Create(
			() => _chartViewModel.Navigation.SetSticky(!_chartViewModel.Navigation.IsSticky)));
		_disposables.Add(ToggleDeltaModeCommand = ReactiveCommand.Create(
			() => _chartViewModel.SetDeltaModeEnabled(!_chartViewModel.IsDeltaModeEnabled)));

		_chartViewModel.Navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() =>
			_chartViewModel.Navigation.WindowChanged -= OnNavigationWindowChanged));

		_disposables.Add(_chartViewModel
			.WhenAnyValue(viewModel => viewModel.DeltaReadoutText)
			.Subscribe(_ => this.RaisePropertyChanged(nameof(DeltaReadoutText))));

		_disposables.Add(_chartViewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(isEnabled => IsDeltaModeEnabled = isEnabled));
	}

	public ReactiveCommand<Unit, Unit> AutoscaleActiveAxisCommand { get; }

	public ReactiveCommand<Unit, Unit> SetActiveAxisLimitsCommand { get; }

	public ReactiveCommand<Unit, Unit> JumpToNowCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleStickyCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleDeltaModeCommand { get; }

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

	public bool IsDeltaModeEnabled
	{
		get => _isDeltaModeEnabled;
		private set => this.RaiseAndSetIfChanged(ref _isDeltaModeEnabled, value);
	}

	public string DeltaReadoutText => _chartViewModel.DeltaReadoutText;

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

	private void ToggleDeltaMode()
	{
		_chartViewModel.SetDeltaModeEnabled(!_chartViewModel.IsDeltaModeEnabled);
	}
}
