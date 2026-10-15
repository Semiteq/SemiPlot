namespace SemiPlot.Core.Trends;

// The live edge and first stored sample arrive as inputs, never a clock.
// Sticky semantics are specified in trend-interaction.md.
public sealed class TrendNavigationModel
{
	public static readonly TimeSpan MinimumWidth = TimeSpan.FromSeconds(1.0);
	public static readonly TimeSpan MaximumWidth = TimeSpan.FromDays(365.0);

	// Zoom widths snap onto the geometric ladder MinimumWidth * ZoomQuantizationRatio^n. The wheel uses
	// reciprocal factors (zoom-in 0.8 = 1/1.25, zoom-out 1.25), so snapping onto this grid makes an
	// in-then-out cycle return to the exact origin width instead of drifting via floating-point error.
	private const double ZoomQuantizationRatio = 1.25;

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
		PlaceFrom(From + delta, Width);

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
		// Clamped rather than anchor-pinned: pinning would render the empty left span as data.
		PlaceFrom(anchor - (targetWidth * anchorFraction), targetWidth);
	}

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

	/// <summary>Sets both edges exactly, off the zoom ladder, and drops sticky.</summary>
	public void SetWindow(DateTime from, DateTime to)
	{
		if (to <= from)
		{
			throw new ArgumentException("View window end must be after its start.", nameof(to));
		}

		PlaceFrom(from, ClampWidthSpan(to - from));
		IsSticky = false;
	}

	/// <summary>Sets the width exactly, off the zoom ladder, keeping the right edge and sticky.</summary>
	public void SetWidth(TimeSpan width)
	{
		var clampedWidth = ClampWidthSpan(width);
		PlaceFrom(To - clampedWidth, clampedWidth);
	}

	private void PlaceFrom(DateTime from, TimeSpan width)
	{
		From = from < FirstSample ? FirstSample : from;
		To = From + width;
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

	private static (DateTime From, DateTime To) ClampWidth(DateTime from, DateTime to)
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
