using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

namespace SemiPlot.UI.Chart;

// Drives the chart from the coordinator's observables without ever rebuilding the plot: pens are
// added/removed as plottable pairs and toggled via IsVisible, realtime batches append (or, at coarse
// layers, fold) into the center line, and history results reset each pen's buffers. Redraws are
// locked to 30 FPS and realtime input is coalesced to 10 Hz. Time navigation is delegated to the
// ChartNavigationController; this view model only translates its window changes into coordinator
// re-queries and exposes the gestures the view and toolbar invoke.
public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _redrawThrottle = TimeSpan.FromMilliseconds(33);
	private const double BandFillOpacity = 0.2;

	private readonly TrendCoordinator _coordinator;
	private readonly IScheduler _uiScheduler;
	private readonly PenScaleModel _scaleModel = new();
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly ChartAxisBinder _axisBinder;
	private readonly ChartNavigationController _navigation = new();
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly Dictionary<long, TrendPenState> _pensById = [];
	private readonly Dictionary<long, PenScaleSettings> _settingsById = [];
	private readonly Dictionary<long, PenHistoryEnvelope> _envelopesById = [];
	private readonly Subject<System.Reactive.Unit> _redrawRequests = new();
	private readonly IObservable<System.Reactive.Unit> _redrawRequested;
	private readonly CompositeDisposable _disposables = new();

	private readonly Dictionary<long, PenScale> _scalesByPenId = [];

	private long _activePenId;
	private DateTime _windowStart;
	private DateTime _windowEnd;
	private DateTime? _cursorTime;
	private IReadOnlyDictionary<long, double?> _cursorValues = new Dictionary<long, double?>();
	private DeltaReadout? _deltaReadout;
	private bool _isDisposed;

	public TrendChartViewModel(TrendCoordinator coordinator, IScheduler uiScheduler)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_coordinator = coordinator;
		_uiScheduler = uiScheduler;
		_axisBinder = new ChartAxisBinder(Plot);
		_cursorReader = new ChartCursorReader(_pensById, _envelopesById);
		_deltaCursorReader = new ChartDeltaCursorReader(_envelopesById);
		_realtimeApplier = new ChartRealtimeApplier(_pensById, _navigation);
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
			.Subscribe(ApplyHistory));

		// The chart view model is the coordinator's sole consumer and owns its lifetime: disposing the
		// view model disposes the coordinator (its realtime keep-alive subscription and Subject).
		_disposables.Add(_coordinator);
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

	// The latest computed axis range for a pen, or null before its first scale computation.
	public (double Min, double Max)? ScaleRangeForPen(long penId)
	{
		return _scalesByPenId.TryGetValue(penId, out var scale) ? (scale.Min, scale.Max) : null;
	}

	// The cursor X (UTC) the view draws a vertical line at, or null when the pointer leaves the plot.
	public DateTime? CursorTime
	{
		get => _cursorTime;
		private set => this.RaiseAndSetIfChanged(ref _cursorTime, value);
	}

	// Per-pen center-channel value at the cursor X, consumed by the legend and the readout.
	public IReadOnlyDictionary<long, double?> CursorValues
	{
		get => _cursorValues;
		private set => this.RaiseAndSetIfChanged(ref _cursorValues, value);
	}

	public bool DeltaCursorsEnabled => _deltaCursorReader.IsEnabled;

	public DateTime? DeltaFirstCursor => _deltaCursorReader.FirstCursor;

	public DateTime? DeltaSecondCursor => _deltaCursorReader.SecondCursor;

	// The active pen's Δt/Δy measurement once both delta cursors are placed, else null.
	public DeltaReadout? DeltaReadout
	{
		get => _deltaReadout;
		private set => this.RaiseAndSetIfChanged(ref _deltaReadout, value);
	}

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

	// Clicking a pen makes it active: its axis becomes the visible primary axis and every other axis
	// hides without any plottable being rebuilt.
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

	// Hovering the plot moves the X-trace cursor: it maps the cursor X to each visible pen's
	// center-channel value at X and publishes both the cursor time and the per-pen readout map.
	public void MoveCursor(DateTime cursorTime)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		CursorTime = cursorTime;
		CursorValues = _cursorReader.ReadAt(cursorTime);
	}

	// Pointer left the plot: hide the cursor and clear the readout.
	public void ClearCursor()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		CursorTime = null;
		CursorValues = new Dictionary<long, double?>();
	}

	// Toolbar toggle: turn the dual Δt/Δy cursors on or off; either edge clears any placed cursors.
	public void SetDeltaCursorsEnabled(bool isEnabled)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_deltaCursorReader.SetEnabled(isEnabled);
		DeltaReadout = null;
	}

	// Placing a delta cursor (only while enabled): the active pen drives Δy and gaps yield a null Δy.
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

	// Double-click an axis (or the toolbar autoscale command): revert the pen's axis to autoscale.
	public bool AutoscaleAxis(long penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(penId, settings => settings with { Mode = ScaleMode.Auto });
	}

	// Entering min/max values (axis editor or toolbar): pin the pen's axis to fixed manual limits.
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
		centerLine.LegendText = pen.Name;
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
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		_windowStart = window.From;
		_windowEnd = window.To;

		if (window.RequiresHistoryRequery)
		{
			_coordinator.SetLayer(window.Layer);
			_coordinator.RequestHistory([.. _pensById.Keys], window.From, window.To);
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	private void ApplyHistory(TrendHistory history)
	{
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
