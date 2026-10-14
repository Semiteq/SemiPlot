using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

public sealed class ChartNavigationController
{
	/// <summary>
	/// The most visible pens the layer ceilings serve unshrunk, measured on the bench
	/// (docs/architecture/data-integration.md#layer-ladder).
	/// </summary>
	public const int ReferencePenCount = 16;

	// Margin past a layer ceiling the width must clear before the layer changes.
	private const double LayerHysteresisFraction = 0.1;

	// Margin past a quantisation boundary the reported width must clear before the column count moves. One
	// quantisation step doubles or halves every ceiling, so LayerHysteresisFraction cannot damp it.
	private const double ColumnCountHysteresisFraction = 0.1;
	private static readonly TimeSpan _defaultWindowWidth = TimeSpan.FromHours(1.0);
	private bool _hasData;
	private DateTime _liveEdge;
	private int _visiblePenCount;

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

	public DateTime FirstSample => _navigation.FirstSample;

	public bool IsSticky => _navigation.IsSticky;

	public AggregationLayer ActiveLayer { get; private set; }

	/// <summary>The newest sample the chart has seen, from the archive or the live edge; null until one arrives.</summary>
	public DateTime? NewestSample { get; private set; }

	// Number of pixel columns the canvas will draw. The stored value is quantized, so it does not read back
	// as the value passed to SetTargetColumnCount.
	public int TargetColumnCount { get; private set; } = HistoryColumnTarget.MaxColumns;

	public event EventHandler<NavigationWindow>? WindowChanged;

	/// <summary>
	/// Raised on every write of <see cref="NewestSample"/>, sticky or not; when the window also moves, after the
	/// move and ahead of <see cref="WindowChanged"/>.
	/// </summary>
	public event EventHandler<DateTime>? NewestSampleMoved;

	public void SetTargetColumnCount(int columns)
	{
		var quantized = QuantizeColumnCount(columns);
		if (quantized == TargetColumnCount)
		{
			return;
		}

		TargetColumnCount = quantized;
		ApplyWindowChange();
	}

	/// <summary>Scales the layer ceilings to the visible pen count; a moved layer raises a window change.</summary>
	public void SetVisiblePenCount(int count)
	{
		_visiblePenCount = count;
		if (LayerForCurrentWidth() == ActiveLayer)
		{
			return;
		}

		ApplyWindowChange();
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

	/// <summary>Moves the pan floor back to an earlier first sample and leaves the window where it is.</summary>
	public void WidenToArchiveExtent(ArchiveExtent extent)
	{
		if (extent.IsEmpty || extent.FirstUtc >= _navigation.FirstSample)
		{
			return;
		}

		_navigation = new TrendNavigationModel(
			_navigation.From, _navigation.To, extent.FirstUtc, _navigation.IsSticky);
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
		RecordNewestSample(lastSample);
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
			RecordNewestSample(now);

			return;
		}

		_navigation.OnLiveEdge(_liveEdge);

		ActiveLayer = LayerForCurrentWidth();
		RecordNewestSample(now);
		WindowChanged?.Invoke(
			this,
			new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer, RequiresHistoryRequery: false));
	}

	private void RecordNewestSample(DateTime sample)
	{
		if (NewestSample >= sample)
		{
			return;
		}

		NewestSample = sample;
		NewestSampleMoved?.Invoke(this, sample);
	}

	private void ApplyWindowChange()
	{
		ActiveLayer = LayerForCurrentWidth();
		WindowChanged?.Invoke(this, new NavigationWindow(_navigation.From, _navigation.To, ActiveLayer));
	}

	private AggregationLayer LayerForCurrentWidth()
	{
		return LayerForWidth(_navigation.Width, ActiveLayer, TargetColumnCount, _visiblePenCount);
	}

	// The layer in force is an argument because the hysteresis band widens the ceiling of that layer only, so
	// the same width answers differently depending on where the ladder was entered from.
	// docs/architecture/data-integration.md#layer-ladder
	public static AggregationLayer LayerForWidth(
		TimeSpan width,
		AggregationLayer currentLayer,
		int targetColumnCount,
		int visiblePenCount)
	{
		var penFactor = Math.Min(1.0, (double)ReferencePenCount / Math.Max(1, visiblePenCount));

		var rawCeiling = BoundaryWithHysteresis(
			AggregationLayer.Minute.ToPointSpacing() * targetColumnCount * penFactor,
			currentLayer == AggregationLayer.Raw);
		if (width <= rawCeiling)
		{
			return AggregationLayer.Raw;
		}

		var minuteCeiling = BoundaryWithHysteresis(
			AggregationLayer.Hour.ToPointSpacing() * targetColumnCount * penFactor,
			currentLayer == AggregationLayer.Minute);
		if (width <= minuteCeiling)
		{
			return AggregationLayer.Minute;
		}

		return AggregationLayer.Hour;
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
