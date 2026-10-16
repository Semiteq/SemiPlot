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
			readouts[envelope.PenId] = ReadPenAt(cursorTime, envelope);
		}

		return readouts;
	}

	public static double? ReadPenAt(DateTime cursorTime, PenHistoryEnvelope envelope)
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

		var upperIndex = envelope.FirstIndexAtOrAfter(cursorTime);

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
