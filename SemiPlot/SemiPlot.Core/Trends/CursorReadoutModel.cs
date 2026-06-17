namespace SemiPlot.Core.Trends;

// Renderer-agnostic X-trace cursor model returning each pen's center-channel value at a cursor X.
// Gap-aware: exact hits return Center, between columns interpolates, but a NaN gap or out-of-range X
// yields null so the readout never bridges a gap.
public sealed class CursorReadoutModel
{
	public IReadOnlyDictionary<long, double?> ReadAt(
		DateTime cursorTime,
		IReadOnlyCollection<PenHistoryEnvelope> envelopes)
	{
		ArgumentNullException.ThrowIfNull(envelopes);

		var readouts = new Dictionary<long, double?>(envelopes.Count);

		foreach (var envelope in envelopes)
		{
			readouts[envelope.PenId] = ReadPenValue(cursorTime, envelope);
		}

		return readouts;
	}

	private static double? ReadPenValue(DateTime cursorTime, PenHistoryEnvelope envelope)
	{
		var timestamps = envelope.Timestamps;
		var centers = envelope.Center;

		if (timestamps.Count == 0)
		{
			return null;
		}

		if (cursorTime < timestamps[0] || cursorTime > timestamps[^1])
		{
			return null;
		}

		var upperIndex = FindUpperBound(timestamps, cursorTime);

		if (timestamps[upperIndex] == cursorTime)
		{
			return Finite(centers[upperIndex]);
		}

		var lowerIndex = upperIndex - 1;
		return Interpolate(
			timestamps[lowerIndex], centers[lowerIndex],
			timestamps[upperIndex], centers[upperIndex],
			cursorTime);
	}

	// First column whose timestamp is at or after the cursor; the range check guarantees one exists.
	private static int FindUpperBound(IReadOnlyList<DateTime> timestamps, DateTime cursorTime)
	{
		var low = 0;
		var high = timestamps.Count - 1;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);

			if (timestamps[middle] < cursorTime)
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

	private static double? Interpolate(
		DateTime lowerTime, double lowerValue,
		DateTime upperTime, double upperValue,
		DateTime cursorTime)
	{
		if (double.IsNaN(lowerValue) || double.IsNaN(upperValue))
		{
			return null;
		}

		var span = (upperTime - lowerTime).TotalSeconds;

		if (span <= 0)
		{
			return lowerValue;
		}

		var fraction = (cursorTime - lowerTime).TotalSeconds / span;
		return lowerValue + ((upperValue - lowerValue) * fraction);
	}

	private static double? Finite(double value)
	{
		return double.IsNaN(value) ? null : value;
	}
}
