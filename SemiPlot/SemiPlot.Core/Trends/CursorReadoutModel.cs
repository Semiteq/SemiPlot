namespace SemiPlot.Core.Trends;

public sealed class CursorReadoutModel
{
	public IReadOnlyDictionary<int, double?> ReadAt(
		DateTime cursorTime,
		IReadOnlyCollection<PenHistoryEnvelope> envelopes)
	{
		var readouts = new Dictionary<int, double?>(envelopes.Count);

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

	// The prior range check guarantees a column at or after the cursor exists.
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
