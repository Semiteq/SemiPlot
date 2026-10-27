using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>One vertex of the polyline a pen's envelope columns are drawn as.</summary>
public readonly record struct EnvelopePoint(double X, double Y, bool StartsSegment);

/// <summary>The span a pen is held across past its last column, and the value it is held at.</summary>
public readonly record struct HeldSpan(double FromX, double ToX, double Value);

// The geometry EnvelopeLine strokes, kept free of Skia so the culling window, the gap break and the step
// shape are unit-testable. docs/architecture/charting.md#per-pen-plottable-envelopeline
public static class EnvelopePath
{
	// One column beyond each edge, so a segment entering the viewport is drawn rather than starting at it.
	public static (int First, int LastExclusive) VisibleRange(
		IReadOnlyList<EnvelopeColumn> columns,
		double xMin,
		double xMax)
	{
		if (columns.Count == 0)
		{
			return (0, 0);
		}

		var first = Math.Max(0, FirstAtOrAfter(columns, xMin) - 1);
		var lastExclusive = Math.Min(columns.Count, FirstAfter(columns, xMax) + 1);

		return (first, Math.Max(first, lastExclusive));
	}

	public static void Build(
		IReadOnlyList<EnvelopeColumn> columns,
		int first,
		int lastExclusive,
		PenLineStyle lineStyle,
		double holdEndX,
		double unreadFromX,
		List<EnvelopePoint> destination)
	{
		destination.Clear();

		// NaN doubles as "no Y yet", which a gap column restores; a drawn Y is never NaN.
		var previousY = first > 0 ? columns[first - 1].Center : double.NaN;
		var segmentIsOpen = false;

		for (var index = first; index < lastExclusive; index++)
		{
			var column = columns[index];
			if (IsGap(column))
			{
				previousY = double.NaN;
				segmentIsOpen = false;
				continue;
			}

			if (lineStyle == PenLineStyle.Stepped && !double.IsNaN(previousY))
			{
				AddVertex(column.X, previousY);
			}

			if (column.Min.Equals(column.Max))
			{
				AddVertex(column.X, column.Min);
				previousY = column.Min;

				continue;
			}

			// Entering at the edge nearer the previous Y keeps the crossing between two columns short.
			var entersAtMin = double.IsNaN(previousY)
				|| Math.Abs(column.Min - previousY) <= Math.Abs(column.Max - previousY);
			var entry = entersAtMin ? column.Min : column.Max;
			var farExtreme = entersAtMin ? column.Max : column.Min;
			var heldY = double.IsNaN(column.Center) ? farExtreme : column.Center;

			AddVertex(column.X, entry);
			AddVertex(column.X, farExtreme);
			if (!heldY.Equals(farExtreme))
			{
				AddVertex(column.X, heldY);
			}

			previousY = heldY;
		}

		void AddVertex(double x, double y)
		{
			destination.Add(new EnvelopePoint(x, y, !segmentIsOpen));
			segmentIsOpen = true;
		}

		var reachesTheLastColumn = lastExclusive == columns.Count && destination.Count > 0;
		if (reachesTheLastColumn && Hold(columns, holdEndX, unreadFromX) is { } held)
		{
			if (!destination[^1].Y.Equals(held.Value))
			{
				destination.Add(new EnvelopePoint(held.FromX, held.Value, false));
			}

			destination.Add(new EnvelopePoint(held.ToX, held.Value, false));
		}
	}

	/// <summary>
	/// The held span when the last column is a value short of the hold end, capped at a pending unread mark; else
	/// null.
	/// </summary>
	public static HeldSpan? Hold(IReadOnlyList<EnvelopeColumn> columns, double holdEndX, double unreadFromX)
	{
		if (columns.Count == 0)
		{
			return null;
		}

		var last = columns[^1];
		var endsBeforeTheUnreadMark = last.X <= unreadFromX;
		var endX = endsBeforeTheUnreadMark ? Math.Min(holdEndX, unreadFromX) : holdEndX;
		var isHeld = IsValue(last) && endX > last.X;

		return isHeld ? new HeldSpan(last.X, endX, last.Center) : null;
	}

	/// <summary>The X a line breaks at after its hold: the drawn hold end, or just past a later last value.</summary>
	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	internal static double BreakAfterHold(IReadOnlyList<EnvelopeColumn> columns, double holdEndX, double unreadFromX)
	{
		if (Hold(columns, holdEndX, unreadFromX) is { } held)
		{
			return held.ToX;
		}

		if (columns.Count == 0 || double.IsNaN(holdEndX))
		{
			return double.NaN;
		}

		var last = columns[^1];

		return IsValue(last) && last.X >= holdEndX ? Math.BitIncrement(last.X) : double.NaN;
	}

	internal static bool IsGap(EnvelopeColumn column)
	{
		return double.IsNaN(column.Min) || double.IsNaN(column.Max);
	}

	/// <summary>A column a line can be held at: no gap, and a drawable last value.</summary>
	internal static bool IsValue(EnvelopeColumn column)
	{
		return !IsGap(column) && !double.IsNaN(column.Center);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	public static double ClippedSegmentDistance(Pixel start, Pixel end, Pixel click, PixelRect bounds)
	{
		double dx = end.X - start.X;
		double dy = end.Y - start.Y;
		var enter = 0.0;
		var leave = 1.0;

		var isInside = ClipEdge(-dx, start.X - bounds.Left, ref enter, ref leave)
			&& ClipEdge(dx, bounds.Right - start.X, ref enter, ref leave)
			&& ClipEdge(-dy, start.Y - bounds.Top, ref enter, ref leave)
			&& ClipEdge(dy, bounds.Bottom - start.Y, ref enter, ref leave);

		if (!isInside || enter > leave)
		{
			return double.PositiveInfinity;
		}

		var clippedStart = new Pixel((float)(start.X + (enter * dx)), (float)(start.Y + (enter * dy)));
		var clippedEnd = new Pixel((float)(start.X + (leave * dx)), (float)(start.Y + (leave * dy)));

		return SegmentDistance(clippedStart, clippedEnd, click);
	}

	public static double SegmentDistance(Pixel start, Pixel end, Pixel point)
	{
		double dx = end.X - start.X;
		double dy = end.Y - start.Y;
		double px = point.X - start.X;
		double py = point.Y - start.Y;
		var lengthSquared = (dx * dx) + (dy * dy);
		if (lengthSquared == 0.0)
		{
			return Math.Sqrt((px * px) + (py * py));
		}

		var along = Math.Clamp(((px * dx) + (py * dy)) / lengthSquared, 0.0, 1.0);
		var offsetX = px - (along * dx);
		var offsetY = py - (along * dy);

		return Math.Sqrt((offsetX * offsetX) + (offsetY * offsetY));
	}

	// One edge of the Liang-Barsky test: false when the segment runs parallel to the edge on its outer side.
	private static bool ClipEdge(double slope, double offset, ref double enter, ref double leave)
	{
		if (slope == 0.0)
		{
			return offset >= 0.0;
		}

		var crossing = offset / slope;
		if (slope < 0.0)
		{
			enter = Math.Max(enter, crossing);
		}
		else
		{
			leave = Math.Min(leave, crossing);
		}

		return true;
	}

	private static int FirstAtOrAfter(IReadOnlyList<EnvelopeColumn> columns, double x)
	{
		var low = 0;
		var high = columns.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (columns[middle].X < x)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}

	internal static int FirstAfter(IReadOnlyList<EnvelopeColumn> columns, double x)
	{
		var low = 0;
		var high = columns.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);
			if (columns[middle].X <= x)
			{
				low = middle + 1;
			}
			else
			{
				high = middle;
			}
		}

		return low;
	}
}
