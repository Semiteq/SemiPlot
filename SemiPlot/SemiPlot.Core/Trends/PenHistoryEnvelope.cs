namespace SemiPlot.Core.Trends;

public sealed record PenHistoryEnvelope
{
	public PenHistoryEnvelope(
		int penId,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double> min,
		IReadOnlyList<double> max,
		IReadOnlyList<double> center)
	{
		if (min.Count != timestamps.Count || max.Count != timestamps.Count || center.Count != timestamps.Count)
		{
			throw new ArgumentException(
				$"Timestamps ({timestamps.Count}), Min ({min.Count}), Max ({max.Count}), and Center "
				+ $"({center.Count}) must have equal length.",
				nameof(center));
		}

		for (var index = 1; index < timestamps.Count; index++)
		{
			if (timestamps[index] <= timestamps[index - 1])
			{
				throw new ArgumentException(
					"Timestamps must be strictly ascending.",
					nameof(timestamps));
			}
		}

		PenId = penId;
		Timestamps = timestamps;
		Min = min;
		Max = max;
		Center = center;
	}

	public int PenId { get; init; }

	public IReadOnlyList<DateTime> Timestamps { get; init; }

	public IReadOnlyList<double> Min { get; init; }

	public IReadOnlyList<double> Max { get; init; }

	public IReadOnlyList<double> Center { get; init; }

	/// <summary>The first column at or after <paramref name="time"/>, or the column count when none is.</summary>
	public int FirstIndexAtOrAfter(DateTime time)
	{
		var low = 0;
		var high = Timestamps.Count;

		while (low < high)
		{
			var middle = low + ((high - low) / 2);

			if (Timestamps[middle] < time)
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
