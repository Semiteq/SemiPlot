using System.Reactive;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using ScottPlot;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Chart;

public sealed class TrendChartViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _historyDebounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan _historyCapInterval = TimeSpan.FromMilliseconds(400);
	private readonly TrendCoordinator _coordinator;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<TrendChartViewModel> _logger;
	private readonly ChartAxisBinder _axisBinder;
	private readonly ChartPenSet _penSet;
	private readonly ChartCursorReader _cursorReader;
	private readonly ChartDeltaCursorReader _deltaCursorReader;
	private readonly ChartRealtimeApplier _realtimeApplier;
	private readonly ChartHistoryRequestDebouncer _historyDebouncer;
	private readonly ChartHistoryPlanner _historyPlanner;
	private readonly CompositeDisposable _disposables = [];
	private readonly Dictionary<int, PenHistoryEnvelope> _envelopesById = [];
	private readonly Dictionary<int, ReadSpan> _readSpansById = [];
	private readonly PenScaleModel _scaleModel = new();
	private readonly Dictionary<int, PenScale> _scalesByPenId = [];
	private readonly Subject<Unit> _redrawRequests = new();
	private bool _isDisposed;
	private bool _isHistoryStarted;
	private bool _isActivePenEmphasized;
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
		_historyPlanner = new ChartHistoryPlanner(Navigation);
		_windowStart = Navigation.From;
		_windowEnd = Navigation.To;
		Navigation.WindowChanged += OnNavigationWindowChanged;
		AxisScale = new AxisScalePanelViewModel(this);

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

	/// <summary>One signal per redraw request, raised at once on the caller's thread; completes on dispose.</summary>
	public IObservable<Unit> RedrawRequested => _redrawRequests.AsObservable();

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
		_redrawRequests.OnCompleted();
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

	// A render can deliver a width after the window has closed.
	public void ReportDataAreaWidth(double dataAreaWidthPixels)
	{
		if (_isDisposed || !(dataAreaWidthPixels > 0.0))
		{
			return;
		}

		var columnTarget = HistoryColumnTarget.FromDataAreaWidth(dataAreaWidthPixels);
		_historyPlanner.ReportedColumnTarget = columnTarget;
		Navigation.SetTargetColumnCount(columnTarget);
	}

	/// <summary>
	/// The first history request, for whatever window is in force; a gesture or a resize report arriving while
	/// it is in flight supersedes it. Before it, a catalogue applied reads only when its pen count moves the layer.
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
					DropEnvelope(penId);
				}

				SettleActivePen();
				if (HasNoPens)
				{
					_isActivePenEmphasized = false;
				}

				RestyleActivePen();
				ApplyAxisModel();
			}
		}

		Navigation.SetVisiblePenCount(VisiblePenIds().Count);
		MoveTheLiveEdgeOntoThePensShown();
		this.RaisePropertyChanged(nameof(Pens));
		this.RaisePropertyChanged(nameof(DrawnPenId));
		this.RaisePropertyChanged(nameof(HasNoPens));
		RequestRedraw();
	}

	/// <summary>
	/// Moves the pan floor back to an earlier first sample, and reads the window in view again when the range
	/// in hand stopped at the floor that moved (docs/architecture/charting.md#applying-a-catalogue-read).
	/// </summary>
	public void WidenToArchiveExtent(ArchiveExtent extent)
	{
		var firstSampleBefore = Navigation.FirstSample;
		Navigation.WidenToArchiveExtent(extent);

		if (_isHistoryStarted && Navigation.FirstSample != firstSampleBefore)
		{
			RequestWindowInForce();
		}
	}

	public void SetPenVisibility(int penId, bool isVisible)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (FindPen(penId) is not { } state)
		{
			return;
		}

		state.SetVisibility(isVisible);
		var readsTheSwitchedOnPen = isVisible && _isHistoryStarted;

		if (readsTheSwitchedOnPen)
		{
			ClearAnUnreadCurve(state);
		}

		ActivateAVisiblePen();
		RestyleActivePen();
		this.RaisePropertyChanged(nameof(DrawnPenId));

		// docs/architecture/charting.md#applying-a-catalogue-read
		var layerBefore = Navigation.ActiveLayer;
		Navigation.SetVisiblePenCount(VisiblePenIds().Count);
		var windowChangeReadAndDrew = Navigation.ActiveLayer != layerBefore;

		if (windowChangeReadAndDrew)
		{
			return;
		}

		if (readsTheSwitchedOnPen)
		{
			RequestWindowInForce();
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	// docs/architecture/charting.md#applying-a-catalogue-read
	private void ClearAnUnreadCurve(TrendPenState state)
	{
		if (_historyPlanner.HasRead(state.Pen.PenId))
		{
			return;
		}

		DropEnvelope(state.Pen.PenId);
		state.ClearHistory(_historyPlanner.RangeInHand?.ToUtc ?? _windowEnd);
	}

	private void DropEnvelope(int penId)
	{
		_envelopesById.Remove(penId);
		_readSpansById.Remove(penId);
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
		_isActivePenEmphasized = true;
		RestyleActivePen();
		ApplyAxisModel();
		RequestRedraw();

		return true;
	}

	public void PickPenAt(Pixel click)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (_penSet.PenNearest(click, Plot.LastRender.DataRect, ActivePenId) is { } penId)
		{
			SetActivePen(penId);
		}
		else
		{
			DropEmphasis();
		}
	}

	private void DropEmphasis()
	{
		if (!_isActivePenEmphasized)
		{
			return;
		}

		_isActivePenEmphasized = false;
		RestyleActivePen();
		RequestRedraw();
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

	/// <summary>Moves both active-pen limits by a fraction of its span; a positive fraction raises the line.</summary>
	public void ShiftActivePen(double fraction)
	{
		ApplyActivePenGesture(scale => PenRangeGesture.Shift(scale.Min, scale.Max, scale.IsLogarithmic, fraction));
	}

	/// <summary>Scales the active pen's span about the value at the anchor fraction (0 bottom, 1 top).</summary>
	public void ScaleActivePen(double factor, double anchorFraction)
	{
		ApplyActivePenGesture(
			scale => PenRangeGesture.Scale(scale.Min, scale.Max, scale.IsLogarithmic, factor, anchorFraction));
	}

	// docs/architecture/trend-interaction.md#single-chart--time-behavior
	private void ApplyActivePenGesture(Func<PenScale, (double Min, double Max)?> gesture)
	{
		if (!MayDrawAxisFor(ActivePenId)
			|| !_scalesByPenId.TryGetValue(ActivePenId, out var scale)
			|| gesture(scale) is not { } range)
		{
			return;
		}

		if (SetAxisLimits(ActivePenId, range.Min, range.Max))
		{
			AxisScale.CloseWhenOpenFor(ActivePenId);
		}
	}

	/// <summary>
	/// False for a pen the chart does not hold, or for the flag going on over a manual minimum at or below zero.
	/// </summary>
	public bool SetLogarithmic(int penId, bool isLogarithmic)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var breaksLogMinimum = isLogarithmic
			&& ScaleSettings.GetValueOrDefault(penId) is { Mode: ScaleMode.Manual } manual
			&& !LogAxis.AdmitsMinimum(manual.ManualMin);

		return !breaksLogMinimum
			&& UpdateAxisSettings(penId, settings => settings with { IsLogarithmic = isLogarithmic });
	}

	private void MoveTheLiveEdgeOntoThePensShown()
	{
		if (ShowsExactly(_coordinator.PenIds))
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
		_historyPlanner.Forget();
		RequestWindowInForce();
	}

	private bool ShowsExactly(IEnumerable<int> penIds)
	{
		return penIds.ToHashSet().SetEquals(_penSet.ById.Keys);
	}

	private void RequestWindowInForce()
	{
		if (NextHistoryRequest() is { } request)
		{
			_historyDebouncer.Request(request);
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

	// Only the plot edit runs under the lock: a notification raised there deadlocks against the render thread
	// (docs/architecture/charting.md#per-pen-plottable-envelopeline).
	private void RestyleActivePen()
	{
		lock (Plot.Sync)
		{
			_penSet.RestyleActiveLine(ActivePenId, _isActivePenEmphasized);
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

		if (window.RequiresHistoryRequery && NextHistoryRequest() is { } request)
		{
			_historyDebouncer.Request(request);

			if (!MayApplyAxisModel(window.From, window.To))
			{
				// docs/architecture/trend-feature-spec.md, AY-4
				RequestRedraw();

				return;
			}
		}
		else if (window.RequiresHistoryRequery)
		{
			_historyDebouncer.RequestNothing();
		}

		ApplyAxisModel();
		RequestRedraw();
	}

	private HistoryRequest? NextHistoryRequest()
	{
		return _historyPlanner.Next(VisiblePenIds(), _windowStart, _windowEnd);
	}

	private List<int> VisiblePenIds()
	{
		return [.. Pens.Where(state => state.IsVisible).Select(state => state.Pen.PenId)];
	}

	private bool MayApplyAxisModel(DateTime fromUtc, DateTime toUtc)
	{
		return _historyPlanner.RangeInHand is not { } fetched || (fromUtc < fetched.ToUtc && toUtc > fetched.FromUtc);
	}

	private Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		HistoryRequest request,
		CancellationToken cancellationToken)
	{
		return _coordinator.QueryHistoryAsync(
			request.PenIds,
			request.FromUtc,
			request.ToUtc,
			request.Layer,
			request.TargetColumnCount,
			cancellationToken);
	}

	// docs/architecture/data-integration.md#what-one-history-query-covers
	private void ApplyHistory(HistoryRequest request, IReadOnlyList<PenHistoryEnvelope> envelopes)
	{
		var rangeInHand = _historyPlanner.Forget();

		foreach (var envelope in envelopes)
		{
			// docs/architecture/charting.md#applying-a-catalogue-read
			if (FindPen(envelope.PenId) is not { } state)
			{
				continue;
			}

			var (drawn, span) = EnvelopeSplice.Over(
				envelope,
				_envelopesById.GetValueOrDefault(envelope.PenId),
				request.Range,
				_readSpansById.TryGetValue(envelope.PenId, out var spanInHand) ? spanInHand : null);

			// A load that throws leaves the envelope with no span, so the next read keeps none of its columns.
			_readSpansById.Remove(envelope.PenId);
			_envelopesById[envelope.PenId] = drawn;

			if (envelope.Timestamps.Count > 0)
			{
				Navigation.TrackDataExtents(envelope.Timestamps[0], envelope.Timestamps[^1]);
			}

			state.LoadHistory(drawn, request.ToUtc);
			_readSpansById[envelope.PenId] = span;
		}

		DropPensMissingFromHistory(envelopes, request);
		var visiblePenIds = VisiblePenIds();
		_historyPlanner.Land(request, rangeInHand, visiblePenIds);

		var drawsTheWindowInView = _historyPlanner.IsWindowFetched(visiblePenIds, _windowStart, _windowEnd);

		if (_historyPlanner.Next(visiblePenIds, _windowStart, _windowEnd) is { } next)
		{
			_historyDebouncer.Request(next);
		}

		if (drawsTheWindowInView || MayApplyAxisModel(_windowStart, _windowEnd))
		{
			ApplyAxisModel();
		}

		RequestRedraw();
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
	private void DropPensMissingFromHistory(IReadOnlyList<PenHistoryEnvelope> envelopes, HistoryRequest request)
	{
		var returnedPenIds = envelopes.Select(envelope => envelope.PenId).ToHashSet();

		foreach (var penId in request.PenIds)
		{
			if (returnedPenIds.Contains(penId))
			{
				continue;
			}

			DropEnvelope(penId);
			FindPen(penId)?.ClearHistory(request.ToUtc);
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

			// docs/architecture/data-integration.md#what-one-history-query-covers
			if (foldIntoColumn && _historyPlanner.RangeInHand is { Layer: AggregationLayer.Raw })
			{
				_historyPlanner.Forget();
			}

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
