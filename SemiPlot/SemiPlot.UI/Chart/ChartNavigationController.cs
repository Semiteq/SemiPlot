using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Owns the time-navigation state machine and the layer-by-zoom mapping so the chart view model and
// the view stay thin. Scroll/drag gestures (mapped by the view) and the toolbar commands flow through
// here onto the TrendNavigationModel; after every window change the controller picks the aggregation
// layer from the window width and raises WindowChanged carrying [From, To] + Layer. The view model
// translates that into a coordinator re-query and the view drives the chart's bottom (time) axis.
public sealed class ChartNavigationController
{
	private static readonly TimeSpan RawLayerCeiling = TimeSpan.FromHours(1.0);
	private static readonly TimeSpan MinuteLayerCeiling = TimeSpan.FromDays(2.0);
	private static readonly TimeSpan HourLayerCeiling = TimeSpan.FromDays(60.0);
	private static readonly TimeSpan DefaultWindowWidth = TimeSpan.FromHours(1.0);

	private TrendNavigationModel _navigation;
	private AggregationLayer _activeLayer;
	private DateTime _liveEdge;
	private DateTime _firstSample;
	private bool _hasData;

	public ChartNavigationController()
	{
		var now = DateTime.UtcNow;
		_firstSample = now - DefaultWindowWidth;
		_liveEdge = now;
		_navigation = new TrendNavigationModel(_firstSample, now, _firstSample, isSticky: true);
		_activeLayer = LayerForWidth(_navigation.Width);
	}

	public event EventHandler<NavigationWindow>? WindowChanged;

	public DateTime From => _navigation.From;

	public DateTime To => _navigation.To;

	public bool IsSticky => _navigation.IsSticky;

	public AggregationLayer ActiveLayer => _activeLayer;

	// Records the data extents discovered from a history load so the model can clamp pan-back to the
	// first stored sample and so the initial window snaps onto real data on the first load.
	public void TrackDataExtents(DateTime firstSample, DateTime lastSample)
	{
		_firstSample = firstSample;

		if (_hasData)
		{
			return;
		}

		_hasData = true;
		_liveEdge = lastSample;
		var width = _navigation.Width;
		_navigation = new TrendNavigationModel(lastSample - width, lastSample, firstSample, isSticky: true);

		// The seed history load that triggered this first snap already populated the chart over the
		// default window; snapping onto real data only repositions the navigation window and axis, so it
		// must NOT re-query a full window again (that would load history twice at startup). Later
		// navigation gestures go through ApplyWindowChange and do re-query.
		_activeLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, _activeLayer, RequiresHistoryRequery: false));
	}

	public void ZoomAt(double factor, DateTime anchorUtc)
	{
		_navigation.Zoom(factor, anchorUtc);
		ApplyWindowChange();
	}

	public void PanBy(TimeSpan delta)
	{
		_navigation.Pan(delta, _liveEdge);
		ApplyWindowChange();
	}

	public void JumpToNow()
	{
		_navigation.JumpToNow(_liveEdge);
		ApplyWindowChange();
	}

	public void SetSticky(bool isSticky)
	{
		if (isSticky)
		{
			_navigation.JumpToNow(_liveEdge);
		}
		else
		{
			_navigation.DetachSticky();
		}

		ApplyWindowChange();
	}

	// Called as the live edge advances (latest realtime timestamp). When sticky, the window tracks the
	// live edge keeping width; otherwise the window is left untouched and no re-query is raised.
	public void OnLiveEdge(DateTime now)
	{
		if (now > _liveEdge)
		{
			_liveEdge = now;
		}

		if (!_navigation.IsSticky)
		{
			return;
		}

		_navigation.OnLiveEdge(_liveEdge);

		// A sticky live-edge advance keeps width constant, so the layer cannot change and the realtime
		// tail already carries the new live edge. Shift the axis but do not trigger a history re-query.
		_activeLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, _activeLayer, RequiresHistoryRequery: false));
	}

	private void ApplyWindowChange()
	{
		_activeLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, _activeLayer));
	}

	// Coarser layers as the window widens so the decimated column count stays bounded; at coarse layers
	// realtime points fold into the current decimation column rather than drawing raw samples.
	private static AggregationLayer LayerForWidth(TimeSpan width)
	{
		if (width <= RawLayerCeiling)
		{
			return AggregationLayer.Raw;
		}

		if (width <= MinuteLayerCeiling)
		{
			return AggregationLayer.Minute;
		}

		if (width <= HourLayerCeiling)
		{
			return AggregationLayer.Hour;
		}

		return AggregationLayer.Day;
	}
}
