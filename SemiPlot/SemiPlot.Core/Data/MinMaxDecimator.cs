using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

// Reduces a pen's raw samples to a min/max envelope sized for a target number of pixel columns.
// Each column preserves both the minimum and the maximum value it covers, so a single-sample spike
// survives that naive Nth-element sampling would drop. Gaps (null values) split the timeline into
// independent segments separated by NaN columns, so a column never straddles a gap and aliases a
// spike across it. Inputs already at or below the target are passed through unchanged.
public static class MinMaxDecimator
{
	public static PenHistoryEnvelope Decimate(
		long penId,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(timestamps);
		ArgumentNullException.ThrowIfNull(values);

		if (timestamps.Count != values.Count)
		{
			throw new ArgumentException(
				$"Timestamps ({timestamps.Count}) and values ({values.Count}) must have equal length.",
				nameof(values));
		}

		if (targetColumnCount < 1)
		{
			throw new ArgumentOutOfRangeException(
				nameof(targetColumnCount), targetColumnCount, "Target column count must be at least one.");
		}

		var builder = new EnvelopeBuilder(penId, timestamps.Count);

		if (timestamps.Count <= targetColumnCount)
		{
			AppendPassThrough(builder, timestamps, values);
			return builder.Build();
		}

		AppendDecimatedSegments(builder, timestamps, values, targetColumnCount);
		return builder.Build();
	}

	private static void AppendPassThrough(
		EnvelopeBuilder builder,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values)
	{
		for (var index = 0; index < timestamps.Count; index++)
		{
			var value = values[index];
			if (value is null)
			{
				builder.AppendGap(timestamps[index]);
			}
			else
			{
				builder.AppendColumn(timestamps[index], value.Value, value.Value, value.Value);
			}
		}
	}

	private static void AppendDecimatedSegments(
		EnvelopeBuilder builder,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values,
		int targetColumnCount)
	{
		var segments = SplitIntoNonNullSegments(values);
		var totalPopulated = segments.Sum(segment => segment.Length);
		var appendedSegment = false;

		foreach (var segment in segments)
		{
			if (appendedSegment)
			{
				builder.AppendGap(timestamps[segment.Start]);
			}

			var columnsForSegment = AllocateColumns(segment.Length, totalPopulated, targetColumnCount);
			DecimateSegment(builder, timestamps, values, segment, columnsForSegment);
			appendedSegment = true;
		}
	}

	private static void DecimateSegment(
		EnvelopeBuilder builder,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values,
		Segment segment,
		int columnCount)
	{
		for (var column = 0; column < columnCount; column++)
		{
			var bucketStart = segment.Start + (int)((long)column * segment.Length / columnCount);
			var bucketEnd = segment.Start + (int)((long)(column + 1) * segment.Length / columnCount);
			if (bucketEnd <= bucketStart)
			{
				continue;
			}

			AppendBucket(builder, timestamps, values, bucketStart, bucketEnd);
		}
	}

	private static void AppendBucket(
		EnvelopeBuilder builder,
		IReadOnlyList<DateTime> timestamps,
		IReadOnlyList<double?> values,
		int bucketStart,
		int bucketEnd)
	{
		var minIndex = bucketStart;
		var maxIndex = bucketStart;

		for (var index = bucketStart + 1; index < bucketEnd; index++)
		{
			var value = values[index]!.Value;
			if (value < values[minIndex]!.Value)
			{
				minIndex = index;
			}

			if (value > values[maxIndex]!.Value)
			{
				maxIndex = index;
			}
		}

		var minValue = values[minIndex]!.Value;
		var maxValue = values[maxIndex]!.Value;
		var centerIndex = (bucketStart + bucketEnd - 1) / 2;
		var centerValue = values[centerIndex]!.Value;

		// The column's X is the center sample's timestamp so the plotted center point and its
		// cursor/legend readout sit at the right time. Buckets never overlap and centerIndex is
		// strictly increasing across them, so X stays ascending.
		var columnTimestamp = timestamps[centerIndex];
		builder.AppendColumn(columnTimestamp, minValue, maxValue, centerValue);
	}

	private static int AllocateColumns(int segmentLength, int totalPopulated, int targetColumnCount)
	{
		var share = (int)((long)segmentLength * targetColumnCount / totalPopulated);
		return Math.Clamp(share, 1, segmentLength);
	}

	private static IReadOnlyList<Segment> SplitIntoNonNullSegments(IReadOnlyList<double?> values)
	{
		var segments = new List<Segment>();
		var runStart = -1;

		for (var index = 0; index < values.Count; index++)
		{
			if (values[index] is not null)
			{
				if (runStart < 0)
				{
					runStart = index;
				}
			}
			else if (runStart >= 0)
			{
				segments.Add(new Segment(runStart, index - runStart));
				runStart = -1;
			}
		}

		if (runStart >= 0)
		{
			segments.Add(new Segment(runStart, values.Count - runStart));
		}

		return segments;
	}

	private readonly record struct Segment(int Start, int Length);

	private sealed class EnvelopeBuilder
	{
		private readonly long _penId;
		private readonly List<DateTime> _timestamps;
		private readonly List<double> _min;
		private readonly List<double> _max;
		private readonly List<double> _center;

		public EnvelopeBuilder(long penId, int capacity)
		{
			_penId = penId;
			_timestamps = new List<DateTime>(capacity);
			_min = new List<double>(capacity);
			_max = new List<double>(capacity);
			_center = new List<double>(capacity);
		}

		public void AppendColumn(DateTime timestamp, double min, double max, double center)
		{
			_timestamps.Add(timestamp);
			_min.Add(min);
			_max.Add(max);
			_center.Add(center);
		}

		public void AppendGap(DateTime timestamp)
		{
			AppendColumn(timestamp, double.NaN, double.NaN, double.NaN);
		}

		public PenHistoryEnvelope Build()
		{
			return new PenHistoryEnvelope(_penId, _timestamps, _min, _max, _center);
		}
	}
}
