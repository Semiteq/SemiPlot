namespace SemiPlot.Core.Trends;

/// <summary>Where the hover time sits in the label row, and which end label it hides.</summary>
public readonly record struct HoverTimePlacement(double Left, bool CoversFirst, bool CoversLast);

public static class MinimapGeometry
{
	private const double FlatBandPadding = 1.0;
	private const double LoneColumnWidth = 2.0;

	// A zero-or-negative extent span (no data yet) yields the full strip so the highlight never collapses.
	public static (double Start, double Width) WindowFraction(
		DateTime extentFirst,
		DateTime extentLast,
		DateTime windowFrom,
		DateTime windowTo)
	{
		var span = (extentLast - extentFirst).TotalSeconds;
		if (span <= 0.0)
		{
			return (0.0, 1.0);
		}

		var start = Math.Clamp((windowFrom - extentFirst).TotalSeconds / span, 0.0, 1.0);
		var end = Math.Clamp((windowTo - extentFirst).TotalSeconds / span, 0.0, 1.0);

		return (start, end - start);
	}

	public static (double Left, double Width) MarkerSpan(
		double startFraction,
		double widthFraction,
		double stripWidth,
		double minimumWidth)
	{
		if (stripWidth <= 0.0)
		{
			return (0.0, 0.0);
		}

		var width = Math.Min(stripWidth, Math.Max(minimumWidth, widthFraction * stripWidth));

		return (SpanLeftWithin(startFraction * stripWidth, width, stripWidth), width);
	}

	/// <summary>Moves a span's left edge so it lies inside [0, limit]; a wider span starts at 0.</summary>
	public static double SpanLeftWithin(double left, double width, double limit)
	{
		return Math.Max(0.0, Math.Min(left, limit - width));
	}

	/// <summary>Centres the hover time on the pointer in a label row whose end labels sit at its edges.</summary>
	public static HoverTimePlacement PlaceHoverTime(
		double pointerX,
		double timeWidth,
		double rowWidth,
		double firstLabelWidth,
		double lastLabelWidth,
		double coverDistance)
	{
		var left = SpanLeftWithin(pointerX - (timeWidth / 2.0), timeWidth, rowWidth);

		return new HoverTimePlacement(
			left,
			CoversFirst: left < firstLabelWidth + coverDistance,
			CoversLast: left + timeWidth > rowWidth - lastLabelWidth - coverDistance);
	}

	public static DateTime RightBound(DateTime extentLast, DateTime? newestSample)
	{
		return newestSample > extentLast ? newestSample.Value : extentLast;
	}

	public static DateTime TimeAtFraction(DateTime extentFirst, DateTime extentLast, double fraction)
	{
		var span = extentLast - extentFirst;

		return extentFirst + (span * Math.Clamp(fraction, 0.0, 1.0));
	}

	public static IReadOnlyList<BandFigure> BandFigures(
		PenHistoryEnvelope band,
		DateTime first,
		DateTime last,
		double width,
		double height)
	{
		var spanSeconds = (last - first).TotalSeconds;
		if (spanSeconds <= 0.0 || width <= 0.0 || height <= 0.0
			|| !TryReadBandRange(band, first, last, out var bottom, out var top))
		{
			return [];
		}

		var scale = new BandScale(first, spanSeconds, width, bottom, top, height);
		var figures = new List<BandFigure>();
		var runStart = -1;

		for (var index = 0; index <= band.Timestamps.Count; index++)
		{
			if (index < band.Timestamps.Count && IsDrawn(band, index, first, last))
			{
				runStart = runStart < 0 ? index : runStart;
				continue;
			}

			if (runStart >= 0)
			{
				figures.Add(Figure(band, runStart, index, scale));
				runStart = -1;
			}
		}

		return figures;
	}

	private static bool TryReadBandRange(
		PenHistoryEnvelope band,
		DateTime first,
		DateTime last,
		out double bottom,
		out double top)
	{
		bottom = double.MaxValue;
		top = double.MinValue;
		var hasValue = false;

		for (var index = 0; index < band.Timestamps.Count; index++)
		{
			if (IsDrawn(band, index, first, last))
			{
				hasValue |= ValueRange.Widen(band.Min[index], isLogarithmic: false, ref bottom, ref top);
				hasValue |= ValueRange.Widen(band.Max[index], isLogarithmic: false, ref bottom, ref top);
			}
		}

		if (hasValue && bottom == top)
		{
			bottom -= FlatBandPadding;
			top += FlatBandPadding;
		}

		return hasValue;
	}

	private static bool IsDrawn(PenHistoryEnvelope band, int index, DateTime first, DateTime last)
	{
		var timestamp = band.Timestamps[index];

		return timestamp >= first && timestamp <= last
			&& double.IsFinite(band.Min[index])
			&& double.IsFinite(band.Max[index])
			&& double.IsFinite(band.Center[index]);
	}

	private static BandFigure Figure(PenHistoryEnvelope band, int start, int end, BandScale scale)
	{
		if (end - start == 1)
		{
			return LoneColumn(band, start, scale);
		}

		var outline = new List<BandPoint>(2 * (end - start));
		var centerLine = new List<BandPoint>(end - start);

		for (var index = start; index < end; index++)
		{
			var x = scale.X(band.Timestamps[index]);
			outline.Add(new BandPoint(x, scale.Y(band.Max[index])));
			centerLine.Add(new BandPoint(x, scale.Y(band.Center[index])));
		}

		for (var index = end - 1; index >= start; index--)
		{
			outline.Add(new BandPoint(scale.X(band.Timestamps[index]), scale.Y(band.Min[index])));
		}

		return new BandFigure(outline, centerLine);
	}

	private static BandFigure LoneColumn(PenHistoryEnvelope band, int index, BandScale scale)
	{
		var center = scale.X(band.Timestamps[index]);
		var left = SpanLeftWithin(center - (LoneColumnWidth / 2.0), LoneColumnWidth, scale.Width);
		var right = Math.Min(scale.Width, left + LoneColumnWidth);
		var top = scale.Y(band.Max[index]);
		var bottom = scale.Y(band.Min[index]);
		var middle = scale.Y(band.Center[index]);

		return new BandFigure(
			[
				new BandPoint(left, top),
				new BandPoint(right, top),
				new BandPoint(right, bottom),
				new BandPoint(left, bottom)
			],
			[new BandPoint(left, middle), new BandPoint(right, middle)]);
	}

	private readonly record struct BandScale(
		DateTime First,
		double SpanSeconds,
		double Width,
		double Bottom,
		double Top,
		double Height)
	{
		public double X(DateTime timestamp)
		{
			return (timestamp - First).TotalSeconds / SpanSeconds * Width;
		}

		public double Y(double value)
		{
			return (Top - value) / (Top - Bottom) * Height;
		}
	}
}
