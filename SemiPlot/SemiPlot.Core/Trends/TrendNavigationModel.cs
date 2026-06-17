namespace SemiPlot.Core.Trends;

// Renderer-agnostic time-navigation state machine for a single chart. It owns the visible view
// window [From, To] (UTC), the sticky flag, and the zoom width (= To - From). It is pure logic:
// "now" (the live edge) and the first stored sample arrive as inputs, never read from a clock, so
// the model stays deterministic and unit-testable.
//
// Sticky semantics (trend-interaction.md):
//   - When sticky, the window's right edge tracks the live edge as it advances; width is constant.
//   - Panning so the live edge scrolls out of the window (into the past) auto-detaches sticky.
//   - Jump-to-real-time re-attaches sticky and places the now-marker at the RIGHT edge (not centered).
//   - Panning back is clamped so From never precedes the first stored sample.
//   - Zoom width is clamped to [1 second, 1 year].
public sealed class TrendNavigationModel
{
	private static readonly TimeSpan MinimumWidth = TimeSpan.FromSeconds(1.0);
	private static readonly TimeSpan MaximumWidth = TimeSpan.FromDays(365.0);

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

	// Changes the window width about an anchor (held fixed in time), clamped to [1 s, 1 year]. The
	// anchor's relative position inside the window is preserved as the width scales.
	public void Zoom(double factor, DateTime anchor)
	{
		if (factor <= 0.0 || double.IsNaN(factor) || double.IsInfinity(factor))
		{
			throw new ArgumentOutOfRangeException(nameof(factor), factor, "Zoom factor must be positive and finite.");
		}

		var currentWidth = Width;
		var targetWidth = ClampWidthSpan(currentWidth * factor);

		var anchorFraction = (anchor - _from) / currentWidth;
		var from = anchor - (targetWidth * anchorFraction);

		_from = from;
		_to = from + targetWidth;
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

	// When sticky, advances the window so its right edge tracks the live edge, keeping width constant.
	// When not sticky, the window is left unchanged.
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
