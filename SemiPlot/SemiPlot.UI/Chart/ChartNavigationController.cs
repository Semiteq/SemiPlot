using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartNavigationController
{
	// Margin past a layer ceiling the width must clear before the layer changes. Without it a zoom gesture
	// hovering on a boundary flip-flops the layer every notch, and at the Raw side the realtime tail
	// straight-lines a far-right raw point across the wide span (the right-side collapse artifact).
	private const double LayerHysteresisFraction = 0.1;
	private static readonly TimeSpan _rawLayerCeiling = TimeSpan.FromHours(1.0);
	private static readonly TimeSpan _minuteLayerCeiling = TimeSpan.FromDays(2.0);
	private static readonly TimeSpan _hourLayerCeiling = TimeSpan.FromDays(60.0);
	private static readonly TimeSpan _defaultWindowWidth = TimeSpan.FromHours(1.0);
	private DateTime _firstSample;
	private bool _hasData;
	private DateTime _liveEdge;

	private TrendNavigationModel _navigation;

	public ChartNavigationController()
	{
		var now = DateTime.UtcNow;
		_firstSample = now - _defaultWindowWidth;
		_liveEdge = now;
		_navigation = new TrendNavigationModel(_firstSample, now, _firstSample, isSticky: true);
		ActiveLayer = LayerForWidth(_navigation.Width);
	}

	public DateTime From => _navigation.From;

	public DateTime To => _navigation.To;

	public bool IsSticky => _navigation.IsSticky;

	public AggregationLayer ActiveLayer { get; private set; }

	public event EventHandler<NavigationWindow>? WindowChanged;

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

		// The first snap must NOT re-query (would load history twice at startup); later navigation gestures
		// go through ApplyWindowChange and do re-query.
		ActiveLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer, RequiresHistoryRequery: false));
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

		// A sticky live-edge advance keeps width constant: shift the axis but do not re-query history.
		ActiveLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer, RequiresHistoryRequery: false));
	}

	private void ApplyWindowChange()
	{
		ActiveLayer = LayerForWidth(_navigation.Width);
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer));
	}

	private AggregationLayer LayerForWidth(TimeSpan width)
	{
		var rawCeiling = BoundaryWithHysteresis(_rawLayerCeiling, ActiveLayer == AggregationLayer.Raw);
		if (width <= rawCeiling)
		{
			return AggregationLayer.Raw;
		}

		var minuteCeiling = BoundaryWithHysteresis(_minuteLayerCeiling, ActiveLayer == AggregationLayer.Minute);
		if (width <= minuteCeiling)
		{
			return AggregationLayer.Minute;
		}

		var hourCeiling = BoundaryWithHysteresis(_hourLayerCeiling, ActiveLayer == AggregationLayer.Hour);
		if (width <= hourCeiling)
		{
			return AggregationLayer.Hour;
		}

		return AggregationLayer.Day;
	}

	private static TimeSpan BoundaryWithHysteresis(TimeSpan ceiling, bool isCurrentLayerBelowCeiling)
	{
		if (!isCurrentLayerBelowCeiling)
		{
			return ceiling;
		}

		return ceiling * (1.0 + LayerHysteresisFraction);
	}
}
