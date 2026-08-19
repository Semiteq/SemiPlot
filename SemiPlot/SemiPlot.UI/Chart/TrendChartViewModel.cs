using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

namespace SemiPlot.UI.Chart;

public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private const double BandFillOpacity = 0.2;
	private static readonly TimeSpan _redrawThrottle = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private readonly ChartAxisBinder _axisBinder;

	private readonly TrendCoordinator _coordinator;
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly CompositeDisposable _disposables = new();
	private readonly Dictionary<long, PenHistoryEnvelope> _envelopesById = [];
	private readonly ChartHistoryRequestDebouncer _historyDebouncer;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly Dictionary<long, TrendPenState> _pensById = [];
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly PenScaleModel _scaleModel = new();

	private readonly Dictionary<long, PenScale> _scalesByPenId = [];
	private readonly Dictionary<long, PenScaleSettings> _settingsById = [];
	private readonly IScheduler _uiScheduler;

	private long _activePenId;
	private DateTime? _cursorTime;
	private IReadOnlyDictionary<long, double?> _cursorValues = new Dictionary<long, double?>();
	private DeltaReadout? _deltaReadout;
	private bool _hasDeferredHistoryRequery;
	// Monotonic stamp assigned to every history request so ApplyHistory can drop a stale window: a slow
	// query that finishes after a newer one was issued must never overwrite a newer window's result.
	private long _historyRequestSequence;
	private bool _isDisposed;
	private bool _isInitialHistoryInFlight;
	private long _lastAppliedHistorySequence;
	// Decimation width of every history query, in columns: the last width the render seam reported. The
	// maximum stands until the first report so the initial query is not starved of resolution.
	private int _reportedColumnTarget = HistoryColumnTarget.MaxColumns;
	private DateTime _windowEnd;
	private DateTime _windowStart;

	public TrendChartViewModel(
		TrendCoordinator coordinator,
		IScheduler dataScheduler,
		IScheduler uiScheduler,
		ILogger<TrendChartViewModel> logger)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);
		ArgumentNullException.ThrowIfNull(logger);

		_coordinator = coordinator;
		_uiScheduler = uiScheduler;
		_logger = logger;
		_axisBinder = new ChartAxisBinder(Plot);
		_cursorReader = new ChartCursorReader(_pensById, _envelopesById);
		_deltaCursorReader = new ChartDeltaCursorReader(_envelopesById);
		_realtimeApplier = new ChartRealtimeApplier(_pensById, Navigation);
		_historyDebouncer = new ChartHistoryRequestDebouncer(
			QueryHistoryAsync,
			ApplyHistory,
			LogHistoryQueryFailure,
			_historyDebounceWindow,
			dataScheduler,
			_uiScheduler);
		Navigation.WindowChanged += OnNavigationWindowChanged;

		RedrawRequested = _redrawRequests
			.Sample(_redrawThrottle, _uiScheduler)
			.ObserveOn(_uiScheduler);

		_disposables.Add(_coordinator.RealtimeBatches
			.Subscribe(ApplyRealtimeBatch));

		_disposables.Add(_coordinator);

		Plot.HideLegend();
	}

	public Plot Plot { get; } = new();

	public long ActivePenId
	{
		get => _activePenId;
		private set => this.RaiseAndSetIfChanged(ref _activePenId, value);
	}

	public ChartNavigationController Navigation { get; } = new();

	public IReadOnlyDictionary<long, PenScaleSettings> ScaleSettings => _settingsById;

	public IObservable<Unit> RedrawRequested { get; }

	public IReadOnlyCollection<TrendPenState> Pens => _pensById.Values;

	public int ScalesRevision { get; private set; }

	public IYAxis? ActivePenAxis =>
		_settingsById.TryGetValue(_activePenId, out var settings)
			? _axisBinder.FindAxis(settings.AxisKey)
			: null;

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

	public LeftButtonTool ActiveLeftButtonTool =>
		IsDeltaModeEnabled ? LeftButtonTool.DeltaPlacement : LeftButtonTool.Pan;

	public bool IsDragging { get; private set; }

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

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		Navigation.WindowChanged -= OnNavigationWindowChanged;
		_historyDebouncer.Dispose();
		_disposables.Dispose();
		_redrawRequests.Dispose();
		Plot.Dispose();
	}

	public (double Min, double Max)? ScaleRangeForPen(long penId)
	{
		return _scalesByPenId.TryGetValue(penId, out var scale) ? (scale.Min, scale.Max) : null;
	}

	public TrendPenState? FindPen(long penId)
	{
		return _pensById.GetValueOrDefault(penId);
	}

	// Disposal is tolerated silently because a render can still deliver a width after the window has closed.
	public void ReportDataAreaWidth(double dataAreaWidthPixels)
	{
		if (_isDisposed || !(dataAreaWidthPixels > 0.0))
		{
			return;
		}

		_reportedColumnTarget = HistoryColumnTarget.FromDataAreaWidth(dataAreaWidthPixels);
		Navigation.SetTargetColumnCount(_reportedColumnTarget);
	}

	public async Task RequestInitialHistory()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.Count == 0)
		{
			return;
		}

		_windowStart = Navigation.From;
		_windowEnd = Navigation.To;

		var layer = Navigation.ActiveLayer;
		var sequence = NextHistorySequence();
		_isInitialHistoryInFlight = true;

		try
		{
			var result = await _coordinator.QueryHistoryAsync(
				[.. _pensById.Keys], Navigation.From, Navigation.To, layer, _reportedColumnTarget);
			if (result.IsFailed)
			{
				ReleaseInitialHistoryGate();

				return;
			}

			ScheduleHistoryApplyWhenAlive(new TrendHistory(layer, result.Value), sequence);
		}
		catch (Exception queryFailure)
		{
			LogHistoryQueryFailure(queryFailure);
			ReleaseInitialHistoryGate();
		}
	}

	// Re-issues, at a fresh sequence and for the snapped window, a width report held back while the initial
	// load was in flight: applying the initial result moves the window onto the archive's last sample, so a
	// request issued for the un-snapped window would carry a higher sequence and overwrite it.
	private void ReleaseInitialHistoryGate()
	{
		if (!_isInitialHistoryInFlight)
		{
			return;
		}

		_isInitialHistoryInFlight = false;

		if (!_hasDeferredHistoryRequery)
		{
			return;
		}

		_hasDeferredHistoryRequery = false;
		RequestHistory(Navigation.From, Navigation.To, Navigation.ActiveLayer);
	}

	private void ScheduleHistoryApplyWhenAlive(TrendHistory history, long sequence)
	{
		var scheduledApply = _uiScheduler.Schedule(() =>
		{
			if (_isDisposed)
			{
				return;
			}

			ApplyHistory(history, sequence);
		});
		_disposables.Add(scheduledApply);
	}

	private void LogHistoryQueryFailure(Exception queryFailure)
	{
		_logger.LogWarning(queryFailure, "History query failed.");
	}

	public TrendPenState AddPen(Pen pen)
	{
		ArgumentNullException.ThrowIfNull(pen);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.TryGetValue(pen.PenId, out var existing))
		{
			return existing;
		}

		var state = BuildPenState(pen);
		_pensById.Add(pen.PenId, state);
		_settingsById.Add(pen.PenId, new PenScaleSettings(pen.PenId, pen.Group));

		if (_activePenId == 0)
		{
			ActivePenId = pen.PenId;
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

		IsDragging = true;
		ClearCursor();
	}

	public void EndDrag()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		IsDragging = false;
	}

	public void MoveCursor(DateTime cursorTime)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (IsDragging)
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

	public void SetDeltaModeEnabled(bool isEnabled)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_deltaCursorReader.SetEnabled(isEnabled);
		DeltaReadout = null;
		this.RaisePropertyChanged(nameof(IsDeltaModeEnabled));
		this.RaisePropertyChanged(nameof(ActiveLeftButtonTool));
		RequestRedraw();
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

	private TrendPenState BuildPenState(Pen pen)
	{
		var color = new Color(pen.Color);

		// The center-line buffer is shared with the Scatter: ScottPlot's Scatter holds a live reference to
		// this list, so the pen state's mutations are seen by the renderer.
		var centerPoints = new List<Coordinates>();
		var centerLine = Plot.Add.Scatter(centerPoints, color);
		centerLine.MarkerStyle = MarkerStyle.None;

		var band = Plot.Add.FillY([], [], []);
		band.FillColor = color.WithAlpha(BandFillOpacity);
		band.LineWidth = 0f;

		// Leaving the default marker makes Polygon.Render walk every vertex calling a no-op marker draw
		// each frame.
		band.MarkerStyle = MarkerStyle.None;

		// Shared-X invariant: every plottable is pinned to the single bottom (time) axis; per-pen axes
		// are Y-only.
		centerLine.Axes.XAxis = Plot.Axes.Bottom;
		band.Axes.XAxis = Plot.Axes.Bottom;

		return new TrendPenState(pen, centerLine, band, centerPoints);
	}

	// A sticky live-edge advance (RequiresHistoryRequery == false) only re-fits the scale window: the
	// realtime tail already carries the live edge, so a full window is NOT re-queried on every tick.
	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		_windowStart = window.From;
		_windowEnd = window.To;

		if (window.RequiresHistoryRequery)
		{
			// A navigation gesture is not held: it is the user's own window choice, and its result already
			// supersedes the initial one through the sequence guard.
			if (_isInitialHistoryInFlight && window.IsColumnCountChange)
			{
				_hasDeferredHistoryRequery = true;
			}
			else
			{
				RequestHistory(window.From, window.To, window.Layer);
			}
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	private void RequestHistory(DateTime fromUtc, DateTime toUtc, AggregationLayer layer)
	{
		_historyDebouncer.Request(new HistoryRequest(
			[.. _pensById.Keys], fromUtc, toUtc, layer, NextHistorySequence(), _reportedColumnTarget));
	}

	private Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(HistoryRequest request)
	{
		return _coordinator.QueryHistoryAsync(
			request.PenIds,
			request.FromUtc,
			request.ToUtc,
			request.Layer,
			request.TargetColumnCount);
	}

	private long NextHistorySequence()
	{
		return ++_historyRequestSequence;
	}

	// Latest-window-wins: a result whose request sequence is older than the most recently applied one is
	// dropped so an older window can never overwrite a newer one.
	private void ApplyHistory(TrendHistory history, long sequence)
	{
		if (sequence < _lastAppliedHistorySequence)
		{
			ReleaseInitialHistoryGate();

			return;
		}

		_lastAppliedHistorySequence = sequence;

		foreach (var envelope in history.Pens)
		{
			_envelopesById[envelope.PenId] = envelope;

			if (envelope.Timestamps.Count > 0)
			{
				Navigation.TrackDataExtents(envelope.Timestamps[0], envelope.Timestamps[^1]);
			}

			if (_pensById.TryGetValue(envelope.PenId, out var state))
			{
				state.LoadHistory(envelope);
			}
		}

		ApplyAxisModel();
		RequestRedraw();
		ReleaseInitialHistoryGate();
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
		var foldIntoColumn = Navigation.ActiveLayer != AggregationLayer.Raw;
		_realtimeApplier.Apply(batch, foldIntoColumn);
		RequestRedraw();
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(Unit.Default);
	}
}
