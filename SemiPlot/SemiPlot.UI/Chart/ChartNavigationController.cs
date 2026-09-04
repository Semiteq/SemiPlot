using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartNavigationController
{
	// Margin past a layer ceiling the width must clear before the layer changes.
	private const double LayerHysteresisFraction = 0.1;

	// Margin past a quantisation boundary the reported width must clear before the column count moves. One
	// quantisation step doubles or halves every ceiling, so LayerHysteresisFraction cannot damp it.
	private const double ColumnCountHysteresisFraction = 0.1;
	private static readonly TimeSpan _defaultWindowWidth = TimeSpan.FromHours(1.0);
	private bool _hasData;
	private DateTime _liveEdge;

	private TrendNavigationModel _navigation;

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
	public int TargetColumnCount { get; private set; } = HistoryColumnTarget.MaxColumns;

	public event EventHandler<NavigationWindow>? WindowChanged;

	public void SetTargetColumnCount(int columns)
	{
		var quantized = QuantizeColumnCount(columns);
		if (quantized == TargetColumnCount)
		{
			return;
		}

		TargetColumnCount = quantized;
		ActiveLayer = LayerForCurrentWidth();
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer));
	}

	/// <summary>
	/// Opens the window on the archive instead of on the wall clock, from the extent startup already read.
	/// </summary>
	/// <remarks>
	/// Routes through <see cref="TrackDataExtents"/> so the latch is set and the first envelope does not snap
	/// again.
	/// </remarks>
	public void SeedFromArchiveExtent(ArchiveExtent extent)
	{
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
		return LayerForWidth(_navigation.Width, ActiveLayer, TargetColumnCount);
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
		if (clamped >= TargetColumnCount / holdRatio && clamped <= TargetColumnCount * holdRatio)
		{
			return TargetColumnCount;
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
