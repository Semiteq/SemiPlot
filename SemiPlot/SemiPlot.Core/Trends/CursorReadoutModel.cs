namespace SemiPlot.Core.Trends;

public sealed class CursorReadoutModel
{
	public IReadOnlyDictionary<int, double?> ReadAt(
		DateTime cursorTime,
		IReadOnlyCollection<(PenHistoryEnvelope Envelope, PenLineStyle LineStyle)> pens)
	{
		var readouts = new Dictionary<int, double?>(pens.Count);

		foreach (var (envelope, lineStyle) in pens)
		{
			readouts[envelope.PenId] = ReadPenAt(cursorTime, envelope, lineStyle);
		}

		return readouts;
	}

	public static double? ReadPenAt(DateTime cursorTime, PenHistoryEnvelope envelope, PenLineStyle lineStyle)
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

		if (lineStyle == PenLineStyle.Stepped)
		{
			return Hold(centers[lowerIndex], centers[upperIndex]);
		}

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

	// A stepped line holds the lower value up to the next column and draws nothing into a gap column.
	private static double? Hold(double lowerValue, double upperValue)
	{
		return double.IsNaN(upperValue) ? null : Finite(lowerValue);
	}

	private static double? Finite(double value)
	{
		return double.IsNaN(value) ? null : value;
	}
}
