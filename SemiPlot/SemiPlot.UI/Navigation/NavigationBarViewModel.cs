using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Navigation;

// The sticky and delta-mode flags mirror their single sources of truth (the navigation controller and
// the chart view model).
public sealed class NavigationBarViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<string> _deltaReadoutText;

	public NavigationBarViewModel(TrendChartViewModel chartViewModel)
	{
		_chartViewModel = chartViewModel;
		IsSticky = chartViewModel.Navigation.IsSticky;

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
			.ToProperty(this, bar => bar.DeltaReadoutText, out _deltaReadoutText));

		_disposables.Add(_chartViewModel
			.WhenAnyValue(viewModel => viewModel.IsDeltaModeEnabled)
			.Subscribe(isEnabled => IsDeltaModeEnabled = isEnabled));
	}

	public ReactiveCommand<Unit, Unit> JumpToNowCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleStickyCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleDeltaModeCommand { get; }

	public bool IsSticky
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool IsDeltaModeEnabled
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string DeltaReadoutText => _deltaReadoutText.Value;

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		IsSticky = _chartViewModel.Navigation.IsSticky;
	}
}
