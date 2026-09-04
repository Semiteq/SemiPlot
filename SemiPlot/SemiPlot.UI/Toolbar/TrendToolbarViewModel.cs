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
	private readonly CompositeDisposable _disposables = [];

	private AggregationLayer _activeLayer;
	private bool _isSticky;

	public TrendToolbarViewModel(TrendChartViewModel chartViewModel)
	{
		_chartViewModel = chartViewModel;
		_isSticky = chartViewModel.Navigation.IsSticky;
		_activeLayer = chartViewModel.Navigation.ActiveLayer;

		_disposables.Add(AutoscaleActiveAxisCommand = ReactiveCommand.Create(
			() => { _chartViewModel.AutoscaleAxis(_chartViewModel.ActivePenId); }));
		_disposables.Add(SetActiveAxisLimitsCommand = ReactiveCommand.Create(
			() => { _chartViewModel.SetAxisLimits(_chartViewModel.ActivePenId, ManualMin, ManualMax); }));
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
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public double ManualMax
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = 1.0;

	public bool IsSticky
	{
		get => _isSticky;
		private set => this.RaiseAndSetIfChanged(ref _isSticky, value);
	}

	public bool IsDeltaModeEnabled
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
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
}
