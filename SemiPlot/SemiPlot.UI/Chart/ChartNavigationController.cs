using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartNavigationController
{
	// Margin past a layer ceiling the width must clear before the layer changes. Without it a zoom gesture
	// hovering on a boundary flip-flops the layer every notch, and at the Raw side the realtime tail
	// straight-lines a far-right raw point across the wide span (the right-side collapse artifact).
	private const double LayerHysteresisFraction = 0.1;

	// Margin past a quantisation boundary the reported width must clear before the column count moves. One
	// quantisation step doubles or halves every ceiling, so LayerHysteresisFraction cannot damp it: without
	// this deadband one pixel of jitter across the 724/725 px boundary would flip the layer and re-query in
	// each direction.
	private const double ColumnCountHysteresisFraction = 0.1;
	private static readonly TimeSpan _defaultWindowWidth = TimeSpan.FromHours(1.0);
	private bool _hasData;
	private DateTime _liveEdge;

	private TrendNavigationModel _navigation;
	private int _targetColumnCount = HistoryColumnTarget.MaxColumns;

	public ChartNavigationController()
	{
		var now = DateTime.UtcNow;
		var firstSample = now - _defaultWindowWidth;
		_liveEdge = now;
		_navigation = new TrendNavigationModel(firstSample, now, firstSample, isSticky: true);
		ActiveLayer = LayerForCurrentWidth();
	}

	public DateTime From => _navigation.From;

	public DateTime To => _navigation.To;

	public bool IsSticky => _navigation.IsSticky;

	public AggregationLayer ActiveLayer { get; private set; }

	// Number of pixel columns the canvas will draw. The stored value is quantized, so it does not read back
	// as the value passed to SetTargetColumnCount.
	public int TargetColumnCount => _targetColumnCount;

	public event EventHandler<NavigationWindow>? WindowChanged;

	// A changed count also invalidates the decimation width the visible data was fetched at, so the window is
	// re-queried even when the layer survives.
	public void SetTargetColumnCount(int columns)
	{
		var quantized = QuantizeColumnCount(columns);
		if (quantized == _targetColumnCount)
		{
			return;
		}

		_targetColumnCount = quantized;
		ActiveLayer = LayerForCurrentWidth();
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer));
	}

	/// <summary>
	/// Opens the window on the archive instead of on the wall clock, from the extent startup already read.
	/// </summary>
	/// <remarks>
	/// It routes through <see cref="TrackDataExtents"/> deliberately: that call sets the has-data latch, so
	/// the first history envelope does not snap the window a second time and undo the seed. An archive whose
	/// last sample is older than the opening window would otherwise never snap at all — no envelope has rows,
	/// nothing calls <see cref="TrackDataExtents"/>, and a pan into the past clamps to startup minus one hour,
	/// after the data the minimap is drawing.
	/// <para>
	/// An empty extent seeds nothing and leaves the wall-clock window, which is the only sensible view of an
	/// archive with no rows.
	/// </para>
	/// </remarks>
	public void SeedFromArchiveExtent(ArchiveExtent extent)
	{
		ArgumentNullException.ThrowIfNull(extent);

		if (extent.IsEmpty)
		{
			return;
		}

		TrackDataExtents(extent.FirstUtc, extent.LastUtc);
	}

	public void TrackDataExtents(DateTime firstSample, DateTime lastSample)
	{
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
		ActiveLayer = LayerForCurrentWidth();
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
		ActiveLayer = LayerForCurrentWidth();
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer, RequiresHistoryRequery: false));
	}

	private void ApplyWindowChange()
	{
		ActiveLayer = LayerForCurrentWidth();
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer));
	}

	private AggregationLayer LayerForCurrentWidth()
	{
		return LayerForWidth(_navigation.Width, ActiveLayer, _targetColumnCount);
	}

	// The layer in force is an argument because the hysteresis band widens the ceiling of that layer only, so
	// the same width answers differently depending on where the ladder was entered from.
	public static AggregationLayer LayerForWidth(
		TimeSpan width,
		AggregationLayer currentLayer,
		int targetColumnCount)
	{
		var rawCeiling = BoundaryWithHysteresis(
			LayerCeiling(AggregationLayer.Raw, targetColumnCount), currentLayer == AggregationLayer.Raw);
		if (width <= rawCeiling)
		{
			return AggregationLayer.Raw;
		}

		var minuteCeiling = BoundaryWithHysteresis(
			LayerCeiling(AggregationLayer.Minute, targetColumnCount), currentLayer == AggregationLayer.Minute);
		if (width <= minuteCeiling)
		{
			return AggregationLayer.Minute;
		}

		var hourCeiling = BoundaryWithHysteresis(
			LayerCeiling(AggregationLayer.Hour, targetColumnCount), currentLayer == AggregationLayer.Hour);
		if (width <= hourCeiling)
		{
			return AggregationLayer.Hour;
		}

		return AggregationLayer.Day;
	}

	// Use the coarsest layer whose point spacing still fits inside one pixel column: a layer is left once the
	// next coarser layer's spacing fits, so its upper bound is that spacing times the column count.
	// Precondition: Raw, Minute or Hour. Day tops the ladder and has no ceiling, so `layer + 1` would leave
	// the enum.
	private static TimeSpan LayerCeiling(AggregationLayer layer, int targetColumnCount)
	{
		var nextCoarser = layer + 1;

		return nextCoarser.ToPointSpacing() * targetColumnCount;
	}

	private int QuantizeColumnCount(int columns)
	{
		var clamped = Math.Clamp(columns, HistoryColumnTarget.MinColumns, HistoryColumnTarget.MaxColumns);

		// Counts are powers of two, so the boundary between two neighbours is their geometric midpoint, a
		// factor of sqrt(2) from each; the deadband pushes that boundary out by the hysteresis margin.
		var holdRatio = Math.Sqrt(2.0) * (1.0 + ColumnCountHysteresisFraction);
		if (clamped >= _targetColumnCount / holdRatio && clamped <= _targetColumnCount * holdRatio)
		{
			return _targetColumnCount;
		}

		var exponent = (int)Math.Round(Math.Log2(clamped));

		return Math.Clamp(1 << exponent, HistoryColumnTarget.MinColumns, HistoryColumnTarget.MaxColumns);
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
