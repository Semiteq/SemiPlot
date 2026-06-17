using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

namespace SemiPlot.UI.Chart;

// Drives the chart from the coordinator's observables without ever rebuilding the plot, delegating
// time navigation to ChartNavigationController. Redraws are locked to 30 FPS, realtime input to 10 Hz.
public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _redrawThrottle = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private const double BandFillOpacity = 0.2;

	private readonly TrendCoordinator _coordinator;
	private readonly IScheduler _uiScheduler;
	private readonly PenScaleModel _scaleModel = new();
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly ChartAxisBinder _axisBinder;
	private readonly ChartNavigationController _navigation = new();
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly ChartHistoryRequestDebouncer _historyDebouncer;
	private readonly Dictionary<long, TrendPenState> _pensById = [];
	private readonly Dictionary<long, PenScaleSettings> _settingsById = [];
	private readonly Dictionary<long, PenHistoryEnvelope> _envelopesById = [];
	private readonly Subject<System.Reactive.Unit> _redrawRequests = new();
	private readonly IObservable<System.Reactive.Unit> _redrawRequested;
	private readonly CompositeDisposable _disposables = new();

	private readonly Dictionary<long, PenScale> _scalesByPenId = [];

	private long _activePenId;
	// Monotonic stamp assigned to every history request (initial/SetLayer and debounced gesture alike) so
	// ApplyHistory can drop a stale window: a slow query that finishes after a newer one issued must never
	// overwrite _envelopesById with the older window.
	private long _historyRequestSequence;
	private long _lastAppliedHistorySequence;
	// The sequence stamped on the coordinator's direct history path (RequestInitialHistory). It is the
	// shared counter's value at request time, so its result interleaves correctly with debounced gesture
	// results through the same ApplyHistory guard.
	private long _coordinatorHistorySequence;
	// Shadow copy of the navigation window the scale model fits against. It cannot just read _navigation
	// because ApplyHistory runs from the async/debounced result path, which must fit against the window
	// that produced the data, not whatever the controller has since panned to.
	private DateTime _windowStart;
	private DateTime _windowEnd;
	private DateTime? _cursorTime;
	private IReadOnlyDictionary<long, double?> _cursorValues = new Dictionary<long, double?>();
	private DeltaReadout? _deltaReadout;
	private bool _isDragging;
	private bool _isDisposed;

	public TrendChartViewModel(TrendCoordinator coordinator, IScheduler dataScheduler, IScheduler uiScheduler)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_coordinator = coordinator;
		_uiScheduler = uiScheduler;
		_axisBinder = new ChartAxisBinder(Plot);
		_cursorReader = new ChartCursorReader(_pensById, _envelopesById);
		_deltaCursorReader = new ChartDeltaCursorReader(_envelopesById);
		_realtimeApplier = new ChartRealtimeApplier(_pensById, _navigation);
		_historyDebouncer = new ChartHistoryRequestDebouncer(
			QueryHistoryAsync,
			ApplyHistory,
			_historyDebounceWindow,
			dataScheduler,
			_uiScheduler);
		_navigation.WindowChanged += OnNavigationWindowChanged;

		_redrawRequested = _redrawRequests
			.Sample(_redrawThrottle, _uiScheduler)
			.ObserveOn(_uiScheduler);

		// The coordinator publishes realtime batches already on the UI scheduler (its pipeline ends with
		// ObserveOn(uiScheduler)); history results flow through a plain Subject, so only that stream is
		// observed onto the UI thread here.
		_disposables.Add(_coordinator.RealtimeBatches
			.Subscribe(ApplyRealtimeBatch));

		_disposables.Add(_coordinator.HistoryResults
			.ObserveOn(_uiScheduler)
			.Subscribe(history => ApplyHistory(history, _coordinatorHistorySequence)));

		// The chart view model is the coordinator's sole consumer and owns its lifetime: disposing the
		// view model disposes the coordinator (its realtime keep-alive subscription and Subject).
		_disposables.Add(_coordinator);

		// The pen identities live in the right-side legend panel; the native in-plot legend would only
		// duplicate them. Hidden once here: AddPen/RemovePen mutate plottables, never the Legend instance,
		// so it stays hidden for the plot's lifetime.
		Plot.HideLegend();
	}

	public Plot Plot { get; } = new();

	public long ActivePenId
	{
		get => _activePenId;
		private set => this.RaiseAndSetIfChanged(ref _activePenId, value);
	}

	public ChartNavigationController Navigation => _navigation;

	public IReadOnlyDictionary<long, PenScaleSettings> ScaleSettings => _settingsById;

	public IObservable<System.Reactive.Unit> RedrawRequested => _redrawRequested;

	public IReadOnlyCollection<TrendPenState> Pens => _pensById.Values;

	// Bumped whenever the per-pen axis ranges are recomputed; the legend observes it to refresh the
	// shown Min..Max without holding a reference to the scale model or the renderer.
	public int ScalesRevision { get; private set; }

	// The ScottPlot Y axis the active pen renders against, or null before any axis is bound. The view's
	// axis-region edit hit-tests against this exact instance.
	public ScottPlot.IYAxis? ActivePenAxis =>
		_settingsById.TryGetValue(_activePenId, out var settings)
			? _axisBinder.FindAxis(settings.AxisKey)
			: null;

	public (double Min, double Max)? ScaleRangeForPen(long penId)
	{
		return _scalesByPenId.TryGetValue(penId, out var scale) ? (scale.Min, scale.Max) : null;
	}

	public DateTime? CursorTime
	{
		get => _cursorTime;
		private set => this.RaiseAndSetIfChanged(ref _cursorTime, value);
	}

	public IReadOnlyDictionary<long, double?> CursorValues
	{
		get => _cursorValues;
		private set => this.RaiseAndSetIfChanged(ref _cursorValues, value);
	}

	public bool IsDeltaModeEnabled => _deltaCursorReader.IsEnabled;

	// Delta mode routes a press to cursor placement and suppresses hand-pan; Pan is the default hand-drag.
	public LeftButtonTool ActiveLeftButtonTool =>
		IsDeltaModeEnabled ? LeftButtonTool.DeltaPlacement : LeftButtonTool.Pan;

	public bool IsDragging => _isDragging;

	public DateTime? DeltaFirstCursor => _deltaCursorReader.FirstCursor;

	public DateTime? DeltaSecondCursor => _deltaCursorReader.SecondCursor;

	public DeltaReadout? DeltaReadout
	{
		get => _deltaReadout;
		private set
		{
			this.RaiseAndSetIfChanged(ref _deltaReadout, value);
			this.RaisePropertyChanged(nameof(DeltaReadoutText));
		}
	}

	public string DeltaReadoutText => ChartDeltaCursorReader.FormatReadout(_deltaReadout);

	public TrendPenState? FindPen(long penId)
	{
		return _pensById.GetValueOrDefault(penId);
	}

	// Issues a first history request over the controller's default window/layer so the chart is
	// populated before any user pan/zoom. Without this the only re-query path is a navigation gesture,
	// which leaves the chart empty at startup. Call once after the initial pens are added.
	public void RequestInitialHistory()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.Count == 0)
		{
			return;
		}

		_windowStart = _navigation.From;
		_windowEnd = _navigation.To;
		_coordinatorHistorySequence = NextHistorySequence();

		_coordinator.SetLayer(_navigation.ActiveLayer);
		_coordinator.RequestHistory([.. _pensById.Keys], _navigation.From, _navigation.To);
	}

	public TrendPenState AddPen(Pen pen)
	{
		ArgumentNullException.ThrowIfNull(pen);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.TryGetValue(pen.ProjectVarId, out var existing))
		{
			return existing;
		}

		var state = BuildPenState(pen);
		_pensById.Add(pen.ProjectVarId, state);
		_settingsById.Add(pen.ProjectVarId, new PenScaleSettings(pen.ProjectVarId, pen.Group));

		if (_activePenId == 0)
		{
			ActivePenId = pen.ProjectVarId;
		}

		ApplyAxisModel();
		RequestRedraw();
		return state;
	}

	public bool RemovePen(long penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_pensById.Remove(penId, out var state))
		{
			return false;
		}

		_settingsById.Remove(penId);
		_envelopesById.Remove(penId);

		if (_activePenId == penId)
		{
			ActivePenId = _pensById.Keys.FirstOrDefault();
		}

		Plot.Remove(state.CenterLine);
		Plot.Remove(state.Band);
		ApplyAxisModel();
		RequestRedraw();
		return true;
	}

	public bool SetPenVisibility(long penId, bool isVisible)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_pensById.TryGetValue(penId, out var state))
		{
			return false;
		}

		state.IsVisible = isVisible;
		var settings = _settingsById[penId];
		_settingsById[penId] = settings with { IsVisible = isVisible };
		ApplyAxisModel();
		RequestRedraw();
		return true;
	}

	// Activating a pen only re-shows its axis as primary; no plottable is rebuilt.
	public bool SetActivePen(long penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_pensById.ContainsKey(penId))
		{
			return false;
		}

		ActivePenId = penId;
		ApplyAxisModel();
		RequestRedraw();
		return true;
	}

	public void BeginDrag()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_isDragging = true;
		ClearCursor();
	}

	public void EndDrag()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_isDragging = false;
	}

	// Hidden while a hand-pan drag is in progress.
	public void MoveCursor(DateTime cursorTime)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_isDragging)
		{
			return;
		}

		CursorTime = cursorTime;
		CursorValues = _cursorReader.ReadAt(cursorTime);
	}

	public void ClearCursor()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		CursorTime = null;
		CursorValues = new Dictionary<long, double?>();
	}

	// Either edge clears placed cursors so re-toggling returns to a clean Pan state.
	public void SetDeltaModeEnabled(bool isEnabled)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_deltaCursorReader.SetEnabled(isEnabled);
		DeltaReadout = null;
		this.RaisePropertyChanged(nameof(IsDeltaModeEnabled));
		this.RaisePropertyChanged(nameof(ActiveLeftButtonTool));
	}

	public void PlaceDeltaCursor(DateTime cursorTime)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_deltaCursorReader.IsEnabled)
		{
			return;
		}

		_deltaCursorReader.Place(cursorTime);
		DeltaReadout = _deltaCursorReader.Measure(_activePenId);
	}

	public bool AutoscaleAxis(long penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(penId, settings => settings with { Mode = ScaleMode.Auto });
	}

	public bool SetAxisLimits(long penId, double min, double max)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(
			penId,
			settings => settings with { Mode = ScaleMode.Manual, ManualMin = min, ManualMax = max });
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_navigation.WindowChanged -= OnNavigationWindowChanged;
		_historyDebouncer.Dispose();
		_disposables.Dispose();
		_redrawRequests.Dispose();
		Plot.Dispose();
	}

	private TrendPenState BuildPenState(Pen pen)
	{
		var color = new Color(pen.Color);

		// The center-line buffer is shared with the Scatter so the pen state's history/realtime mutations
		// are seen by the renderer (ScottPlot's Scatter holds a live reference to this list).
		var centerPoints = new List<Coordinates>();
		var centerLine = Plot.Add.Scatter(centerPoints, color);
		centerLine.MarkerStyle = MarkerStyle.None;

		var band = Plot.Add.FillY([], [], []);
		band.FillColor = color.WithAlpha(BandFillOpacity);
		band.LineWidth = 0f;

		// Shared-X invariant: every plottable is pinned to the single bottom (time) axis. Per-pen axes
		// are Y-only; no per-pen X axis is ever created.
		centerLine.Axes.XAxis = Plot.Axes.Bottom;
		band.Axes.XAxis = Plot.Axes.Bottom;

		return new TrendPenState(pen, centerLine, band, centerPoints);
	}

	// Re-queries history for the new window/layer and updates the X window the scale model fits against.
	// A sticky live-edge advance (RequiresHistoryRequery == false) only re-fits the scale window: the
	// realtime tail already carries the live edge, so re-querying a full window on every tick is avoided.
	// A gesture re-query (RequiresHistoryRequery == true) is debounced off the UI thread rather than run
	// synchronously, so rapid zoom/pan collapses to a single trailing query instead of one-per-notch.
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		_windowStart = window.From;
		_windowEnd = window.To;

		if (window.RequiresHistoryRequery)
		{
			_historyDebouncer.Request(
				new HistoryRequest([.. _pensById.Keys], window.From, window.To, window.Layer, NextHistorySequence()));
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	// The debounced query func handed to the history debouncer: it runs on the data scheduler after the
	// gesture stream goes quiet, so it must not touch UI-thread-only state.
	private Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(HistoryRequest request)
	{
		return _coordinator.QueryHistoryAsync(
			request.PenIds,
			request.FromUtc,
			request.ToUtc,
			request.Layer,
			TrendCoordinator.DefaultTargetColumnCount);
	}

	private long NextHistorySequence()
	{
		return ++_historyRequestSequence;
	}

	// Guards both history paths (debounced gesture and the coordinator's direct initial/SetLayer query):
	// a result whose request sequence is older than the most recently applied one is dropped so an older
	// window can never overwrite a newer one (latest-window-wins, not last-writer-wins).
	private void ApplyHistory(TrendHistory history, long sequence)
	{
		if (sequence < _lastAppliedHistorySequence)
		{
			return;
		}

		_lastAppliedHistorySequence = sequence;

		foreach (var envelope in history.Pens)
		{
			_envelopesById[envelope.PenId] = envelope;

			if (envelope.Timestamps.Count > 0)
			{
				_navigation.TrackDataExtents(envelope.Timestamps[0], envelope.Timestamps[^1]);
			}

			if (_pensById.TryGetValue(envelope.PenId, out var state))
			{
				state.LoadHistory(envelope);
			}
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	private bool UpdateAxisSettings(long penId, Func<PenScaleSettings, PenScaleSettings> update)
	{
		if (!_settingsById.TryGetValue(penId, out var settings))
		{
			return false;
		}

		_settingsById[penId] = update(settings);
		ApplyAxisModel();
		RequestRedraw();
		return true;
	}

	private void ApplyAxisModel()
	{
		if (_settingsById.Count == 0)
		{
			return;
		}

		var scales = _scaleModel.Compute(
			_settingsById.Values.ToArray(),
			_envelopesById,
			_activePenId,
			_windowStart,
			_windowEnd);

		_axisBinder.Apply(scales, _pensById);
		StoreScales(scales);
	}

	// Caches the latest per-pen axis range so the legend can show each pen's Min..Max, and signals the
	// change so the legend refreshes without coupling to the renderer.
	private void StoreScales(IReadOnlyList<PenScale> scales)
	{
		_scalesByPenId.Clear();
		foreach (var scale in scales)
		{
			foreach (var penId in scale.PenIds)
			{
				_scalesByPenId[penId] = scale;
			}
		}

		ScalesRevision++;
		this.RaisePropertyChanged(nameof(ScalesRevision));
	}

	private void ApplyRealtimeBatch(RealtimeBatch batch)
	{
		var foldIntoColumn = _navigation.ActiveLayer != AggregationLayer.Raw;
		_realtimeApplier.Apply(batch, foldIntoColumn);
		RequestRedraw();
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(System.Reactive.Unit.Default);
	}
}
