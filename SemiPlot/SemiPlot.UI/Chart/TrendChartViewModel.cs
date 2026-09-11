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
	private static readonly TimeSpan _redrawThrottle = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan _historyCapInterval = TimeSpan.FromMilliseconds(400);
	private readonly ChartAxisBinder _axisBinder;

	private readonly TrendCoordinator _coordinator;
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly CompositeDisposable _disposables = [];
	private readonly Dictionary<int, PenHistoryEnvelope> _envelopesById = [];
	private readonly ChartHistoryRequestDebouncer _historyDebouncer;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly Dictionary<int, TrendPenState> _pensById = [];
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly PenScaleModel _scaleModel = new();

	private readonly Dictionary<int, PenScale> _scalesByPenId = [];
	private readonly Dictionary<int, PenScaleSettings> _settingsById = [];
	private readonly Subject<Unit> _historyApplied = new();
	private bool _isDisposed;
	// Decimation width of every history query, in columns: the last width the render seam reported. The
	// maximum stands until the first report so the initial query is not starved of resolution.
	private int _reportedColumnTarget = HistoryColumnTarget.MaxColumns;
	private FetchRange? _lastFetch;
	private DateTime _windowEnd;
	private DateTime _windowStart;

	public TrendChartViewModel(
		TrendCoordinator coordinator,
		IScheduler dataScheduler,
		IScheduler uiScheduler,
		ILogger<TrendChartViewModel> logger)
	{
		_coordinator = coordinator;
		_logger = logger;
		_axisBinder = new ChartAxisBinder(Plot);
		_cursorReader = new ChartCursorReader(_pensById, _envelopesById);
		_deltaCursorReader = new ChartDeltaCursorReader(_envelopesById);
		_realtimeApplier = new ChartRealtimeApplier(_pensById, Navigation);
		_historyDebouncer = new ChartHistoryRequestDebouncer(
			QueryHistoryAsync,
			ApplyHistory,
			OnHistoryQueryFailed,
			_historyDebounceWindow,
			_historyCapInterval,
			dataScheduler,
			uiScheduler);
		Navigation.WindowChanged += OnNavigationWindowChanged;

		RedrawRequested = _redrawRequests
			.Sample(_redrawThrottle, uiScheduler)
			.ObserveOn(uiScheduler);

		_disposables.Add(_coordinator.RealtimeBatches
			.Subscribe(ApplyRealtimeBatch));

		_disposables.Add(_coordinator);

		Plot.HideLegend();
	}

	public Plot Plot { get; } = new();

	public int ActivePenId
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public ChartNavigationController Navigation { get; } = new();

	public IReadOnlyDictionary<int, PenScaleSettings> ScaleSettings => _settingsById;

	public IObservable<Unit> RedrawRequested { get; }

	/// <summary>
	/// One pulse per history result applied, on the UI scheduler.
	/// </summary>
	public IObservable<Unit> HistoryApplied => _historyApplied;

	public IReadOnlyCollection<TrendPenState> Pens => _pensById.Values;

	public int ScalesRevision { get; private set; }

	/// <summary>How many Y axes the scale model has created so far; one per axis key.</summary>
	public int AxisCount => _axisBinder.AxesByKey.Count;

	public IYAxis? ActivePenAxis =>
		_settingsById.TryGetValue(ActivePenId, out var settings)
			? _axisBinder.FindAxis(settings.AxisKey)
			: null;

	public DateTime? CursorTime
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public IReadOnlyDictionary<int, double?> CursorValues
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = new Dictionary<int, double?>();

	public bool IsDeltaModeEnabled => _deltaCursorReader.IsEnabled;

	public LeftButtonTool ActiveLeftButtonTool =>
		IsDeltaModeEnabled ? LeftButtonTool.DeltaPlacement : LeftButtonTool.Pan;

	public bool IsDragging { get; private set; }

	public DateTime? DeltaFirstCursor => _deltaCursorReader.FirstCursor;

	public DateTime? DeltaSecondCursor => _deltaCursorReader.SecondCursor;

	public DeltaReadout? DeltaReadout
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(DeltaReadoutText));
		}
	}

	public string DeltaReadoutText => ChartDeltaCursorReader.FormatReadout(DeltaReadout);

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
		_historyApplied.Dispose();
		Plot.Dispose();
	}

	public (double Min, double Max)? ScaleRangeForPen(int penId)
	{
		return _scalesByPenId.TryGetValue(penId, out var scale) ? (scale.Min, scale.Max) : null;
	}

	public TrendPenState? FindPen(int penId)
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

	/// <summary>
	/// The first history request, for whatever window is in force. It is an ordinary request on the one
	/// history path, so a gesture or a resize report arriving while it is in flight supersedes it.
	/// </summary>
	public void RequestInitialHistory()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.Count == 0)
		{
			return;
		}

		_windowStart = Navigation.From;
		_windowEnd = Navigation.To;

		if (!IsWindowFetched(Navigation.From, Navigation.To, Navigation.ActiveLayer))
		{
			RequestHistory(Navigation.From, Navigation.To, Navigation.ActiveLayer);
		}
	}

	public TrendPenState AddPen(Pen pen)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_pensById.TryGetValue(pen.PenId, out var existing))
		{
			return existing;
		}

		var state = BuildPenState(pen);
		_pensById.Add(pen.PenId, state);
		_settingsById.Add(pen.PenId, new PenScaleSettings(pen.PenId, pen.Group));

		if (ActivePenId == 0)
		{
			ActivePenId = pen.PenId;
		}

		ApplyAxisModel();
		RequestRedraw();

		return state;
	}

	public bool RemovePen(int penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_pensById.Remove(penId, out var state))
		{
			return false;
		}

		_settingsById.Remove(penId);
		_envelopesById.Remove(penId);

		if (ActivePenId == penId)
		{
			ActivePenId = _pensById.Keys.FirstOrDefault();
		}

		Plot.Remove(state.Line);
		ApplyAxisModel();
		RequestRedraw();

		return true;
	}

	public bool SetPenVisibility(int penId, bool isVisible)
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

	public bool SetActivePen(int penId)
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
		CursorValues = new Dictionary<int, double?>();
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
		DeltaReadout = _deltaCursorReader.Measure(ActivePenId);
	}

	public bool AutoscaleAxis(int penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(penId, settings => settings with { Mode = ScaleMode.Auto });
	}

	public bool SetAxisLimits(int penId, double min, double max)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(
			penId,
			settings => settings with { Mode = ScaleMode.Manual, ManualMin = min, ManualMax = max });
	}

	private TrendPenState BuildPenState(Pen pen)
	{
		var line = new EnvelopeLine { Color = new Color(pen.Color) };

		// Shared-X invariant: every plottable is pinned to the single bottom (time) axis.
		line.Axes.XAxis = Plot.Axes.Bottom;
		Plot.Add.Plottable(line);

		return new TrendPenState(pen, line);
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		_windowStart = window.From;
		_windowEnd = window.To;

		if (window.RequiresHistoryRequery && !IsWindowFetched(window.From, window.To, window.Layer))
		{
			RequestHistory(window.From, window.To, window.Layer);

			if (!MayApplyAxisModel(window.From, window.To))
			{
				// docs/architecture/trend-feature-spec.md, AY-4
				RequestRedraw();

				return;
			}
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	private bool IsWindowFetched(DateTime fromUtc, DateTime toUtc, AggregationLayer layer)
	{
		return _lastFetch is { } fetched
			&& HistoryPrefetch.Covers(fetched, fromUtc, toUtc, layer, Navigation.TargetColumnCount);
	}

	private bool MayApplyAxisModel(DateTime fromUtc, DateTime toUtc)
	{
		return _lastFetch is not { } fetched || (fromUtc < fetched.ToUtc && toUtc > fetched.FromUtc);
	}

	private void RequestHistory(DateTime fromUtc, DateTime toUtc, AggregationLayer layer)
	{
		var range = HistoryPrefetch.Expand(
			fromUtc, toUtc, layer, Navigation.TargetColumnCount, Navigation.FirstSample);

		_historyDebouncer.Request(new HistoryRequest(
			[.. _pensById.Keys],
			range,
			HistoryPrefetch.ScaleColumnTarget(_reportedColumnTarget)));
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

	private void ApplyHistory(HistoryRequest request, IReadOnlyList<PenHistoryEnvelope> envelopes)
	{
		// The gate opens on the range that came back, never on the one last asked for: only a result that
		// landed says what the envelopes in hand cover.
		_lastFetch = request.Range;

		foreach (var envelope in envelopes)
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

		DropPensMissingFromHistory(envelopes, request.PenIds);

		var drawsTheWindowInView = IsWindowFetched(_windowStart, _windowEnd, Navigation.ActiveLayer);

		if (!drawsTheWindowInView)
		{
			// A drag can leave the band a query was issued for and re-enter the band the envelopes in hand
			// covered, which the gate then reads as fetched and asks nothing more. The result landing here
			// replaces those envelopes, so the window in view is re-requested from the range that arrived.
			RequestHistory(_windowStart, _windowEnd, Navigation.ActiveLayer);
		}

		if (drawsTheWindowInView || MayApplyAxisModel(_windowStart, _windowEnd))
		{
			ApplyAxisModel();
		}

		RequestRedraw();
		_historyApplied.OnNext(Unit.Default);
	}

	// The axis is applied whatever the window in view holds, because the whole-envelope fallback is the best
	// range there is until a read succeeds.
	private void OnHistoryQueryFailed(IReadOnlyList<IError> errors)
	{
		_logger.LogWarning(
			errors.OfType<ExceptionalError>().FirstOrDefault()?.Exception,
			"History query failed: {Reasons}",
			string.Join("; ", errors.Select(error => error.Message)));

		ApplyAxisModel();
		RequestRedraw();
	}

	// Only the identifiers the request carried are considered: a pen added while the query was in flight was
	// never asked about, and clearing it would drop a curve the result says nothing about.
	private void DropPensMissingFromHistory(IReadOnlyList<PenHistoryEnvelope> envelopes,
		IReadOnlyList<int> requestedPenIds)
	{
		var returnedPenIds = envelopes.Select(envelope => envelope.PenId).ToHashSet();

		foreach (var penId in requestedPenIds)
		{
			if (returnedPenIds.Contains(penId))
			{
				continue;
			}

			_envelopesById.Remove(penId);
			_pensById.GetValueOrDefault(penId)?.ClearHistory();
		}
	}

	private bool UpdateAxisSettings(int penId, Func<PenScaleSettings, PenScaleSettings> update)
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
			[.. _settingsById.Values],
			_envelopesById,
			ActivePenId,
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
