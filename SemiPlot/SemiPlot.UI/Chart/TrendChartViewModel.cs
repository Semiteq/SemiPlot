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
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Chart;

public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _redrawThrottle = TimeSpan.FromMilliseconds(33);
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan _historyCapInterval = TimeSpan.FromMilliseconds(400);
	private readonly ChartAxisBinder _axisBinder;
	private readonly ChartPenSet _penSet;

	private readonly TrendCoordinator _coordinator;
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly CompositeDisposable _disposables = [];
	private readonly Dictionary<int, PenHistoryEnvelope> _envelopesById = [];
	private readonly ChartHistoryRequestDebouncer _historyDebouncer;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly Subject<Unit> _redrawRequests = new();
	private readonly PenScaleModel _scaleModel = new();

	private readonly Dictionary<int, PenScale> _scalesByPenId = [];
	private readonly Subject<Unit> _historyApplied = new();
	private bool _isDisposed;
	private bool _isHistoryStarted;
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
		MessagePanelViewModel messagePanel,
		ILogger<TrendChartViewModel> logger)
	{
		_coordinator = coordinator;
		_messagePanel = messagePanel;
		_logger = logger;
		_axisBinder = new ChartAxisBinder(Plot);
		_penSet = new ChartPenSet(Plot, _axisBinder);
		_cursorReader = new ChartCursorReader(_penSet.ById, _envelopesById);
		_deltaCursorReader = new ChartDeltaCursorReader(_envelopesById);
		_realtimeApplier = new ChartRealtimeApplier(_penSet.ById, Navigation);
		_historyDebouncer = new ChartHistoryRequestDebouncer(
			QueryHistoryAsync,
			ApplyHistory,
			OnHistoryQueryFailed,
			_historyDebounceWindow,
			_historyCapInterval,
			dataScheduler,
			uiScheduler);
		_windowStart = Navigation.From;
		_windowEnd = Navigation.To;
		Navigation.WindowChanged += OnNavigationWindowChanged;
		AxisScale = new AxisScalePanelViewModel(this);

		RedrawRequested = _redrawRequests
			.Sample(_redrawThrottle, uiScheduler)
			.ObserveOn(uiScheduler);

		_disposables.Add(_coordinator.RealtimeBatches
			.Subscribe(ApplyRealtimeBatch));
		_disposables.Add(_coordinator.RealtimeFailures
			.Subscribe(ReportFailure));

		Plot.HideLegend();
	}

	public Plot Plot { get; } = new();

	public AxisScalePanelViewModel AxisScale { get; }

	public int ActivePenId
	{
		get;
		private set
		{
			if (field == value)
			{
				return;
			}

			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(DrawnPenId));
			RefreshDeltaReadout();
		}
	}

	/// <summary>The pen whose axis the plot draws: the active pen while it is shown, otherwise none.</summary>
	public int? DrawnPenId => MayDrawAxisFor(ActivePenId) ? ActivePenId : null;

	public ChartNavigationController Navigation { get; } = new();

	public IReadOnlyDictionary<int, PenScaleSettings> ScaleSettings => _penSet.ScaleSettings;

	public IObservable<Unit> RedrawRequested { get; }

	/// <summary>
	/// One pulse per history result applied, on the UI scheduler.
	/// </summary>
	public IObservable<Unit> HistoryApplied => _historyApplied;

	/// <summary>The pens in catalogue order; a new list, and a change notification, per catalogue applied.</summary>
	public IReadOnlyList<TrendPenState> Pens => _penSet.Ordered;

	/// <summary>The stored settings of every pen shown, as the last catalogue applied named them.</summary>
	public IReadOnlyList<Pen> Catalogue => _penSet.Catalogue;

	/// <summary>
	/// No pen to draw: unfinished provisioning shown as the chart area's own empty state, not an error.
	/// </summary>
	public bool HasNoPens => Pens.Count == 0;

	public int ScalesRevision { get; private set; }

	/// <summary>How many Y axes the binder has created so far; one per pen.</summary>
	public int AxisCount => _axisBinder.AxesByPenId.Count;

	public IYAxis? ActivePenAxis => _axisBinder.FindAxis(ActivePenId);

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
		AxisScale.Dispose();
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
		return _penSet.ById.GetValueOrDefault(penId);
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
	/// The first history request, for whatever window is in force; a gesture or a resize report arriving while
	/// it is in flight supersedes it. Until it, a catalogue applied queries nothing: this request covers every pen.
	/// </summary>
	public void RequestInitialHistory()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_isHistoryStarted = true;
		RequestWindowInForce();
	}

	/// <summary>
	/// Shows exactly <paramref name="catalogue"/>, compared against the pens shown rather than against the read
	/// before it, so a pen named again with other settings is revised whatever that read said.
	/// </summary>
	public void ApplyCatalogue(IReadOnlyList<Pen> catalogue)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		// docs/architecture/charting.md#applying-a-catalogue-read
		using (DelayChangeNotifications())
		{
			lock (Plot.Sync)
			{
				foreach (var penId in _penSet.Apply(catalogue).RemovedPenIds)
				{
					_envelopesById.Remove(penId);
				}

				SettleActivePen();
				ApplyAxisModel();
			}
		}

		MoveTheLiveEdgeOntoThePensShown();
		this.RaisePropertyChanged(nameof(Pens));
		this.RaisePropertyChanged(nameof(DrawnPenId));
		this.RaisePropertyChanged(nameof(HasNoPens));
		RequestRedraw();
	}

	public bool SetPenVisibility(int penId, bool isVisible)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (FindPen(penId) is not { } state)
		{
			return false;
		}

		state.IsVisible = isVisible;
		ActivateAVisiblePen();
		this.RaisePropertyChanged(nameof(DrawnPenId));
		ApplyAxisModel();
		RequestRedraw();

		return true;
	}

	// Only a visible pen's axis may be drawn, so a switched-off pen is refused rather than activated:
	// activating one leaves the plot with no Y axis, its delta readout empty and its axis region unreachable.
	public bool SetActivePen(int penId)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!MayDrawAxisFor(penId))
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
		RefreshDeltaReadout();
	}

	public void AutoscaleActivePen()
	{
		AutoscalePen(ActivePenId);
	}

	public void RestoreInitialScale()
	{
		RestoreInitialScale(ActivePenId);
	}

	/// <summary>False when the pen is not shown, which leaves its scale untouched.</summary>
	public bool AutoscalePen(int penId)
	{
		return MayDrawAxisFor(penId)
			&& UpdateAxisSettings(penId, settings => settings with { Mode = ScaleMode.Auto });
	}

	/// <summary>False when the pen is not shown, which leaves its scale untouched.</summary>
	public bool RestoreInitialScale(int penId)
	{
		return FindPen(penId) is { IsVisible: true } state
			&& UpdateAxisSettings(penId, _ => PenScaleSettings.InitialFor(state.Pen));
	}

	public bool SetAxisLimits(int penId, double min, double max)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return UpdateAxisSettings(
			penId,
			settings => settings with { Mode = ScaleMode.Manual, ManualMin = min, ManualMax = max });
	}

	private void MoveTheLiveEdgeOntoThePensShown()
	{
		if (_coordinator.PenIds.ToHashSet().SetEquals(_penSet.ById.Keys))
		{
			return;
		}

		_coordinator.SetPens([.. Pens.Select(state => state.Pen.PenId)]);

		if (_isHistoryStarted)
		{
			RequeryAllPens();
		}
	}

	private void RequeryAllPens()
	{
		_lastFetch = null;
		RequestWindowInForce();
	}

	private void RequestWindowInForce()
	{
		if (Pens.Count > 0 && !IsWindowFetched(Navigation.From, Navigation.To, Navigation.ActiveLayer))
		{
			RequestHistory(Navigation.From, Navigation.To, Navigation.ActiveLayer);
		}
	}

	private void SettleActivePen()
	{
		if (FindPen(ActivePenId) is null)
		{
			ActivePenId = Pens.Count > 0 ? Pens[0].Pen.PenId : 0;
		}

		ActivateAVisiblePen();
	}

	// Only the active pen's axis is drawn and only a visible pen's axis may be, so an active pen that is
	// switched off leaves the chart with no Y axis at all.
	private void ActivateAVisiblePen()
	{
		if (MayDrawAxisFor(ActivePenId))
		{
			return;
		}

		foreach (var candidate in Pens)
		{
			if (MayDrawAxisFor(candidate.Pen.PenId))
			{
				ActivePenId = candidate.Pen.PenId;

				return;
			}
		}
	}

	private bool MayDrawAxisFor(int penId)
	{
		return FindPen(penId) is { IsVisible: true };
	}

	private void RefreshDeltaReadout()
	{
		DeltaReadout = _deltaCursorReader.Measure(ActivePenId);
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
			[.. _penSet.ById.Keys],
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
			// docs/architecture/charting.md#applying-a-catalogue-read
			if (FindPen(envelope.PenId) is not { } state)
			{
				continue;
			}

			_envelopesById[envelope.PenId] = envelope;

			if (envelope.Timestamps.Count > 0)
			{
				Navigation.TrackDataExtents(envelope.Timestamps[0], envelope.Timestamps[^1]);
			}

			state.LoadHistory(envelope);
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
		// Runs as the failure branch of the debouncer's onNext and as its onError, so a throw out of here
		// would end the one subscription that issues every history query for the rest of the session.
		try
		{
			_messagePanel.ReportFailure(errors, _logger);

			ApplyAxisModel();
			RequestRedraw();
		}
		catch (Exception reportFailure)
		{
			_messagePanel.TryReportFailure(new ExceptionalError(reportFailure), _logger);
		}
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
			FindPen(penId)?.ClearHistory();
		}
	}

	private bool UpdateAxisSettings(int penId, Func<PenScaleSettings, PenScaleSettings> update)
	{
		if (!_penSet.UpdateScaleSettings(penId, update))
		{
			return false;
		}

		ApplyAxisModel();
		RequestRedraw();

		return true;
	}

	private void ApplyAxisModel()
	{
		var scales = _scaleModel.Compute(
			[.. _penSet.ScaleSettings.Values],
			_envelopesById,
			ActivePenId,
			_windowStart,
			_windowEnd);

		_axisBinder.Apply(scales, _penSet.ById);
		StoreScales(scales);
	}

	private void StoreScales(IReadOnlyList<PenScale> scales)
	{
		_scalesByPenId.Clear();
		foreach (var scale in scales)
		{
			_scalesByPenId[scale.PenId] = scale;
		}

		ScalesRevision++;
		this.RaisePropertyChanged(nameof(ScalesRevision));
	}

	private void ApplyRealtimeBatch(RealtimeBatch batch)
	{
		try
		{
			var foldIntoColumn = Navigation.ActiveLayer != AggregationLayer.Raw;
			_realtimeApplier.Apply(batch, foldIntoColumn);
			RequestRedraw();
		}
		catch (Exception applyFailure)
		{
			ReportFailure(applyFailure);
		}
	}

	/// <summary>The chart's own Rx pipelines, the view's included, route a terminal failure here.</summary>
	public void ReportFailure(Exception failure)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	private void RequestRedraw()
	{
		_redrawRequests.OnNext(Unit.Default);
	}
}
