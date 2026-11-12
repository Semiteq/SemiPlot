using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.Navigation;

// The sticky and delta-mode flags mirror their single sources of truth (the navigation controller and
// the chart view model).
public sealed class NavigationBarViewModel : ReactiveObject, IDisposable
{
	private readonly TrendChartViewModel _chartViewModel;
	private readonly CompositeDisposable _disposables = [];
	private readonly ObservableAsPropertyHelper<string> _deltaReadoutText;
	private readonly ObservableAsPropertyHelper<IReadOnlyList<StoredMarker>> _operatorMarkers;
	private readonly ObservableAsPropertyHelper<bool> _hasOperatorMarkers;
	private readonly ObservableAsPropertyHelper<bool> _hasRecipeRuns;
	private readonly ObservableAsPropertyHelper<IReadOnlyList<NavigationListEntry>> _recipeRunEntries;
	private readonly ObservableAsPropertyHelper<bool> _hasRecipeRunEntries;

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

		_disposables.Add(_chartViewModel.Markers.Changed
			.StartWith(Unit.Default)
			.Select(_ => NewestFirstOperatorMarkers())
			.DistinctUntilChanged(SequenceComparer<StoredMarker>.Instance)
			.ToProperty(this, bar => bar.OperatorMarkers, out _operatorMarkers));
		_disposables.Add(this
			.WhenAnyValue(bar => bar.OperatorMarkers, markers => markers.Count > 0)
			.ToProperty(this, bar => bar.HasOperatorMarkers, out _hasOperatorMarkers));

		var steps = StepChanges().Replay(1).RefCount();
		_disposables.Add(PreviousRunCommand = StepCommand(steps, step => step.PreviousRun));
		_disposables.Add(NextRunCommand = StepCommand(steps, step => step.NextRun));
		_disposables.Add(LatestRunCommand = StepCommand(steps, step => step.LatestRun));
		_disposables.Add(PreviousMarkerCommand = StepCommand(steps, step => step.PreviousMarker));
		_disposables.Add(NextMarkerCommand = StepCommand(steps, step => step.NextMarker));
		_disposables.Add(steps
			.Select(step => step.LatestRun is not null)
			.ToProperty(this, bar => bar.HasRecipeRuns, out _hasRecipeRuns));
		_disposables.Add(RunChanges()
			.Select(_ => NewestFirstRunEntries())
			.DistinctUntilChanged(SequenceComparer<NavigationListEntry>.Instance)
			.ToProperty(this, bar => bar.RecipeRunEntries, out _recipeRunEntries));
		_disposables.Add(this
			.WhenAnyValue(bar => bar.RecipeRunEntries, entries => entries.Count > 0)
			.ToProperty(this, bar => bar.HasRecipeRunEntries, out _hasRecipeRunEntries));
		_disposables.Add(MarkerList = new NavigationListViewModel(
			this.WhenAnyValue(bar => bar.OperatorMarkers).Select(EntriesOf),
			WindowCentreUtc,
			entry => GoTo(NavigationSteps.Centred(entry.TimeUtc, CurrentWindow())),
			TimeZoneInfo.Local,
			Resources.MarkerListFilterPlaceholder,
			Resources.MarkerListNoMatch));
		_disposables.Add(RecipeRunList = new NavigationListViewModel(
			this.WhenAnyValue(bar => bar.RecipeRunEntries),
			WindowCentreUtc,
			GoToRun,
			TimeZoneInfo.Local,
			Resources.RunListFilterPlaceholder,
			Resources.RunListNoMatch));
	}

	public ReactiveCommand<Unit, Unit> JumpToNowCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleStickyCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleDeltaModeCommand { get; }

	/// <summary>Frames the last recipe run that ends before the window centre.</summary>
	public ReactiveCommand<Unit, Unit> PreviousRunCommand { get; }

	/// <summary>Frames the first recipe run that starts after the window centre.</summary>
	public ReactiveCommand<Unit, Unit> NextRunCommand { get; }

	public ReactiveCommand<Unit, Unit> LatestRunCommand { get; }

	/// <summary>Centres the last operator marker before the window centre, keeping the width.</summary>
	public ReactiveCommand<Unit, Unit> PreviousMarkerCommand { get; }

	/// <summary>Centres the first operator marker after the window centre, keeping the width.</summary>
	public ReactiveCommand<Unit, Unit> NextMarkerCommand { get; }

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

	/// <summary>The operator's markers, shown or hidden, newest first.</summary>
	public IReadOnlyList<StoredMarker> OperatorMarkers => _operatorMarkers.Value;

	/// <summary>True while an operator marker exists, the one thing the marker list needs to open.</summary>
	public bool HasOperatorMarkers => _hasOperatorMarkers.Value;

	/// <summary>The marker list's filter and rows over <see cref="OperatorMarkers"/>, in the machine's zone.</summary>
	public NavigationListViewModel MarkerList { get; }

	/// <summary>True while a recipe run with a sample in it exists, the one target the latest-run step needs.</summary>
	public bool HasRecipeRuns => _hasRecipeRuns.Value;

	/// <summary>Every recipe run, newest first, as the run list shows it; a duration moves it once a minute.</summary>
	public IReadOnlyList<NavigationListEntry> RecipeRunEntries => _recipeRunEntries.Value;

	/// <summary>True while a recipe run exists, the one thing the run list needs to open.</summary>
	public bool HasRecipeRunEntries => _hasRecipeRunEntries.Value;

	/// <summary>The run list's filter and rows over <see cref="RecipeRunEntries"/>, in the machine's zone.</summary>
	public NavigationListViewModel RecipeRunList { get; }

	public void Dispose()
	{
		_disposables.Dispose();
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		IsSticky = _chartViewModel.Navigation.IsSticky;
	}

	private IReadOnlyList<StoredMarker> NewestFirstOperatorMarkers()
	{
		return [.. _chartViewModel.Markers.OperatorMarkers.Reverse()];
	}

	private static IReadOnlyList<NavigationListEntry> EntriesOf(IReadOnlyList<StoredMarker> markers)
	{
		return [.. markers.Select(NavigationListEntry.Of)];
	}

	private IReadOnlyList<NavigationListEntry> NewestFirstRunEntries()
	{
		return [.. CurrentTargets().Runs.Reverse().Select(NavigationListEntry.Of)];
	}

	/// <summary>A marker change or a newest sample, the two things that pair the runs again.</summary>
	private IObservable<Unit> RunChanges()
	{
		var navigation = _chartViewModel.Navigation;
		var newestSamples = Observable
			.FromEventPattern<DateTime>(
				handler => navigation.NewestSampleMoved += handler,
				handler => navigation.NewestSampleMoved -= handler)
			.Select(_ => Unit.Default);

		return _chartViewModel.Markers.Changed
			.Merge(newestSamples)
			.StartWith(Unit.Default);
	}

	/// <summary>The runs are paired again on a marker change or a newest sample; a window move only searches.</summary>
	private IObservable<NavigationSteps> StepChanges()
	{
		var navigation = _chartViewModel.Navigation;
		var windows = Observable
			.FromEventPattern<NavigationWindow>(
				handler => navigation.WindowChanged += handler,
				handler => navigation.WindowChanged -= handler)
			.Select(_ => Unit.Default)
			.StartWith(Unit.Default);

		return RunChanges()
			.Select(_ => CurrentTargets())
			.CombineLatest(windows, (targets, _) => targets)
			.Select(targets => NavigationSteps.Of(targets, CurrentWindow(), navigation.FirstSample));
	}

	private ReactiveCommand<Unit, Unit> StepCommand(
		IObservable<NavigationSteps> steps,
		Func<NavigationSteps, (DateTime FromUtc, DateTime ToUtc)?> target)
	{
		return ReactiveCommand.Create(
			() => GoTo(target(CurrentSteps())),
			steps.Select(step => target(step) is not null));
	}

	private NavigationSteps CurrentSteps()
	{
		return NavigationSteps.Of(CurrentTargets(), CurrentWindow(), _chartViewModel.Navigation.FirstSample);
	}

	private MarkerTargets CurrentTargets()
	{
		var markers = _chartViewModel.Markers;

		return new MarkerTargets(
			MarkerNavigation.Runs(markers.All, _chartViewModel.Navigation.NewestSample),
			markers.OperatorMarkers);
	}

	private (DateTime FromUtc, DateTime ToUtc) CurrentWindow()
	{
		return (_chartViewModel.Navigation.From, _chartViewModel.Navigation.To);
	}

	private DateTime WindowCentreUtc()
	{
		return NavigationSteps.CentreOf(CurrentWindow());
	}

	/// <summary>Frames the run that starts at the entry's moment, as it stands now.</summary>
	private void GoToRun(NavigationListEntry entry)
	{
		foreach (var run in CurrentTargets().Runs)
		{
			if (run.StartUtc == entry.TimeUtc)
			{
				GoTo(MarkerNavigation.Frame(run));

				return;
			}
		}
	}

	private void GoTo((DateTime FromUtc, DateTime ToUtc)? window)
	{
		if (window is { } target)
		{
			_chartViewModel.Navigation.SetWindow(target.FromUtc, target.ToUtc);
		}
	}
}
