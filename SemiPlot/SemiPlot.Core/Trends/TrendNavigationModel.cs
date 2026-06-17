namespace SemiPlot.Core.Trends;

// Renderer-agnostic time-navigation state machine owning the view window [From, To], the sticky flag,
// and the zoom width. Pure logic: the live edge and first stored sample arrive as inputs, never a clock.
// Sticky semantics are specified in trend-interaction.md.
public sealed class TrendNavigationModel
{
	private static readonly TimeSpan MinimumWidth = TimeSpan.FromSeconds(1.0);
	private static readonly TimeSpan MaximumWidth = TimeSpan.FromDays(365.0);

	// Zoom widths are snapped onto a geometric ladder MinimumWidth * ZoomQuantizationRatio^n. The wheel
	// uses reciprocal factors (zoom-in 0.8 = 1/1.25, zoom-out 1.25), so quantizing each result onto the
	// 1.25 grid makes an in-then-out cycle land back on the exact origin width instead of drifting away
	// through accumulated floating-point error across repeated notches.
	private const double ZoomQuantizationRatio = 1.25;

	private DateTime _from;
	private DateTime _to;
	private readonly DateTime _firstSample;

	public TrendNavigationModel(DateTime from, DateTime to, DateTime firstSample, bool isSticky)
	{
		if (to <= from)
		{
			throw new ArgumentException("View window end must be after its start.", nameof(to));
		}

		_firstSample = firstSample;
		(_from, _to) = ClampWidth(from, to);
		IsSticky = isSticky;
	}

	public DateTime From => _from;

	public DateTime To => _to;

	public TimeSpan Width => _to - _from;

	public bool IsSticky { get; private set; }

	public DateTime FirstSample => _firstSample;

	// Shifts the window by delta, keeping width constant. A negative delta pans into the past; if it
	// would push From before the first stored sample, the shift is clamped so From == FirstSample.
	// If the resulting window no longer contains the supplied live edge, sticky auto-detaches.
	public void Pan(TimeSpan delta, DateTime now)
	{
		var width = Width;
		var from = _from + delta;
		if (from < _firstSample)
		{
			from = _firstSample;
		}

		_from = from;
		_to = from + width;

		if (now > _to || now < _from)
		{
			IsSticky = false;
		}
	}

	// Changes the window width about an anchor (held fixed in time), clamped to [1 s, 1 year] and snapped
	// onto the zoom ladder so reciprocal in/out gestures round-trip. The anchor's relative position inside
	// the window is preserved as the width scales, and From is clamped so it never precedes the first
	// stored sample (a window that reached back past it would render the missing left span as data).
	public void Zoom(double factor, DateTime anchor)
	{
		if (factor <= 0.0 || double.IsNaN(factor) || double.IsInfinity(factor))
		{
			throw new ArgumentOutOfRangeException(nameof(factor), factor, "Zoom factor must be positive and finite.");
		}

		var currentWidth = Width;
		var targetWidth = ClampWidthSpan(QuantizeWidth(currentWidth * factor));

		var anchorFraction = (anchor - _from) / currentWidth;
		var from = anchor - (targetWidth * anchorFraction);
		// Tradeoff: when the computed From would reach back past the first stored sample it is clamped to
		// FirstSample, which means the anchor is no longer held exactly fixed in time for that one zoom.
		// Honouring the clamp (never rendering a span with no data on the left) is preferred over keeping
		// the anchor pinned, since the alternative would draw the missing left span as data.
		if (from < _firstSample)
		{
			from = _firstSample;
		}

		_from = from;
		_to = from + targetWidth;
	}

	// Snaps a width onto the geometric ladder MinimumWidth * ZoomQuantizationRatio^n so the reciprocal
	// wheel factors land on shared grid points and an in-then-out cycle returns to the origin width.
	private static TimeSpan QuantizeWidth(TimeSpan width)
	{
		if (width <= MinimumWidth)
		{
			return MinimumWidth;
		}

		var steps = Math.Round(Math.Log(width / MinimumWidth) / Math.Log(ZoomQuantizationRatio));
		var quantizedSeconds = MinimumWidth.TotalSeconds * Math.Pow(ZoomQuantizationRatio, steps);
		return TimeSpan.FromSeconds(quantizedSeconds);
	}

	// Re-attaches sticky and places the now-marker at the RIGHT edge, keeping the current width.
	public void JumpToNow(DateTime now)
	{
		var width = Width;
		_to = now;
		_from = now - width;
		IsSticky = true;
	}

	public void DetachSticky()
	{
		IsSticky = false;
	}

	public void OnLiveEdge(DateTime now)
	{
		if (!IsSticky)
		{
			return;
		}

		var width = Width;
		_to = now;
		_from = now - width;
	}

	private (DateTime From, DateTime To) ClampWidth(DateTime from, DateTime to)
	{
		var width = ClampWidthSpan(to - from);
		return (from, from + width);
	}

	private static TimeSpan ClampWidthSpan(TimeSpan width)
	{
		if (width < MinimumWidth)
		{
			return MinimumWidth;
		}

		if (width > MaximumWidth)
		{
			return MaximumWidth;
		}

		return width;
	}
}
