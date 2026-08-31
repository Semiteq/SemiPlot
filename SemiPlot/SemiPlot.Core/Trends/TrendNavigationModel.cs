namespace SemiPlot.Core.Trends;

// The live edge and first stored sample arrive as inputs, never a clock.
// Sticky semantics are specified in trend-interaction.md.
public sealed class TrendNavigationModel
{
	// Zoom widths snap onto the geometric ladder _minimumWidth * ZoomQuantizationRatio^n. The wheel uses
	// reciprocal factors (zoom-in 0.8 = 1/1.25, zoom-out 1.25), so snapping onto this grid makes an
	// in-then-out cycle return to the exact origin width instead of drifting via floating-point error.
	private const double ZoomQuantizationRatio = 1.25;
	private static readonly TimeSpan _minimumWidth = TimeSpan.FromSeconds(1.0);
	private static readonly TimeSpan _maximumWidth = TimeSpan.FromDays(365.0);

	public TrendNavigationModel(DateTime from, DateTime to, DateTime firstSample, bool isSticky)
	{
		if (to <= from)
		{
			throw new ArgumentException("View window end must be after its start.", nameof(to));
		}

		FirstSample = firstSample;
		(From, To) = ClampWidth(from, to);
		IsSticky = isSticky;
	}

	public DateTime From { get; private set; }

	public DateTime To { get; private set; }

	public TimeSpan Width => To - From;

	public bool IsSticky { get; private set; }

	public DateTime FirstSample { get; }

	public void Pan(TimeSpan delta, DateTime now)
	{
		var width = Width;
		var from = From + delta;
		if (from < FirstSample)
		{
			from = FirstSample;
		}

		From = from;
		To = from + width;

		if (now > To || now < From)
		{
			IsSticky = false;
		}
	}

	public void Zoom(double factor, DateTime anchor)
	{
		if (factor <= 0.0 || double.IsNaN(factor) || double.IsInfinity(factor))
		{
			throw new ArgumentOutOfRangeException(nameof(factor), factor, "Zoom factor must be positive and finite.");
		}

		var currentWidth = Width;
		var targetWidth = ClampWidthSpan(QuantizeWidth(currentWidth * factor));

		var anchorFraction = (anchor - From) / currentWidth;
		var from = anchor - (targetWidth * anchorFraction);
		// Clamped rather than anchor-pinned: pinning would render the empty left span as data.
		if (from < FirstSample)
		{
			from = FirstSample;
		}

		From = from;
		To = from + targetWidth;
	}

	private static TimeSpan QuantizeWidth(TimeSpan width)
	{
		if (width <= _minimumWidth)
		{
			return _minimumWidth;
		}

		var steps = Math.Round(Math.Log(width / _minimumWidth) / Math.Log(ZoomQuantizationRatio));
		var quantizedSeconds = _minimumWidth.TotalSeconds * Math.Pow(ZoomQuantizationRatio, steps);

		return TimeSpan.FromSeconds(quantizedSeconds);
	}

	public void JumpToNow(DateTime now)
	{
		var width = Width;
		To = now;
		From = now - width;
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
		To = now;
		From = now - width;
	}

	private (DateTime From, DateTime To) ClampWidth(DateTime from, DateTime to)
	{
		var width = ClampWidthSpan(to - from);

		return (from, from + width);
	}

	private static TimeSpan ClampWidthSpan(TimeSpan width)
	{
		if (width < _minimumWidth)
		{
			return _minimumWidth;
		}

		if (width > _maximumWidth)
		{
			return _maximumWidth;
		}

		return width;
	}
}
