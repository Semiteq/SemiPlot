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
		ArgumentNullException.ThrowIfNull(timestamps);
		ArgumentNullException.ThrowIfNull(min);
		ArgumentNullException.ThrowIfNull(max);
		ArgumentNullException.ThrowIfNull(center);

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
}
