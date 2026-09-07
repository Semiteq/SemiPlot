using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

/// <summary>One vertex of the polyline a pen's envelope columns are drawn as.</summary>
public readonly record struct EnvelopePoint(double X, double Y, bool StartsSegment);

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
		List<EnvelopePoint> destination)
	{
		destination.Clear();

		// NaN doubles as "no point yet", which a gap column restores; a drawn Y is never NaN.
		var previousY = double.NaN;

		for (var index = first; index < lastExclusive; index++)
		{
			var column = columns[index];
			if (double.IsNaN(column.Min) || double.IsNaN(column.Max))
			{
				previousY = double.NaN;
				continue;
			}

			var startsSegment = double.IsNaN(previousY);
			if (!startsSegment && lineStyle == PenLineStyle.Stepped)
			{
				destination.Add(new EnvelopePoint(column.X, previousY, false));
			}

			if (column.Min.Equals(column.Max))
			{
				destination.Add(new EnvelopePoint(column.X, column.Min, startsSegment));
				previousY = column.Min;

				continue;
			}

			// Entering at the edge nearer the previous Y keeps the crossing between two columns short.
			var entersAtMin = startsSegment
				|| Math.Abs(column.Min - previousY) <= Math.Abs(column.Max - previousY);
			var entry = entersAtMin ? column.Min : column.Max;
			var exit = entersAtMin ? column.Max : column.Min;

			destination.Add(new EnvelopePoint(column.X, entry, startsSegment));
			destination.Add(new EnvelopePoint(column.X, exit, false));
			previousY = exit;
		}
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

	private static int FirstAfter(IReadOnlyList<EnvelopeColumn> columns, double x)
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
