using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinMaxDecimatorTests
{
	private const int PenId = 42;

	private static readonly DateTime _origin = new(2026, 6, 15, 0, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Decimate_PreservesSpikeThatNthSamplingWouldDrop()
	{
		const int sampleCount = 1000;
		const int spikeIndex = 503;
		const double spikeValue = 9999.0;
		const int targetColumns = 50;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = 0.0;
		}

		values[spikeIndex] = spikeValue;

		var nthSamplingStride = sampleCount / targetColumns;
		var nthSamplingHitsSpike = (spikeIndex % nthSamplingStride) == 0;
		nthSamplingHitsSpike.Should().BeFalse("the test is only meaningful if naive sampling misses the spike");

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Max.Should().Contain(spikeValue);
	}

	[Fact]
	public void Decimate_ColumnCountStaysWithinConstantFactorOfTarget()
	{
		const int sampleCount = 100_000;
		const int targetColumns = 800;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = Math.Sin(index / 13.0);
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Timestamps.Count.Should().BeLessThanOrEqualTo(targetColumns * 4);
		envelope.Timestamps.Count.Should().BeGreaterThan(0);
	}

	[Fact]
	public void Decimate_DoesNotRetainBackingArraysSizedToTheInput()
	{
		const int sampleCount = 123_000;
		const int targetColumns = 2048;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = Math.Sin(index / 11.0);
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		CapacityOf(envelope.Timestamps).Should().BeLessThanOrEqualTo(targetColumns * 4);
		CapacityOf(envelope.Min).Should().BeLessThanOrEqualTo(targetColumns * 4);
		CapacityOf(envelope.Max).Should().BeLessThanOrEqualTo(targetColumns * 4);
		CapacityOf(envelope.Center).Should().BeLessThanOrEqualTo(targetColumns * 4);
	}

	[Fact]
	public void Decimate_ProducesMonotonicTimestamps()
	{
		const int sampleCount = 5000;
		const int targetColumns = 120;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = (index % 7) - 3.0;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Timestamps.Should().BeInAscendingOrder();
	}

	[Fact]
	public void Decimate_PassesThroughWhenInputAtOrBelowTarget()
	{
		var timestamps = BuildTimestamps(10);
		var values = new double?[10];
		for (var index = 0; index < 10; index++)
		{
			values[index] = index;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumnCount: 10);

		envelope.Timestamps.Should().Equal(timestamps);
		envelope.Center.Should().Equal(values.Select(value => value!.Value));
		envelope.Min.Should().Equal(envelope.Center);
		envelope.Max.Should().Equal(envelope.Center);
	}

	[Fact]
	public void Decimate_BandSpansMinAndMaxPerColumn()
	{
		const int sampleCount = 40;
		const int targetColumns = 4;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = index % 2 == 0 ? index : -index;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		for (var column = 0; column < envelope.Min.Count; column++)
		{
			envelope.Max[column].Should().BeGreaterThanOrEqualTo(envelope.Min[column]);
		}
	}

	[Fact]
	public void Decimate_MapsNullsToNaNGapColumnsAndSegmentsTheTimeline()
	{
		const int sampleCount = 600;
		const int targetColumns = 30;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = index;
		}

		for (var index = 200; index < 250; index++)
		{
			values[index] = null;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Center.Should().Contain(value => double.IsNaN(value));
		envelope.Min.Should().Contain(value => double.IsNaN(value));
		envelope.Max.Should().Contain(value => double.IsNaN(value));
		envelope.Timestamps.Should().BeInAscendingOrder();

		// No finite column may sit inside the gap interval, proving a column never straddles it.
		var gapStart = timestamps[200];
		var gapEnd = timestamps[249];
		for (var column = 0; column < envelope.Timestamps.Count; column++)
		{
			if (double.IsNaN(envelope.Center[column]))
			{
				continue;
			}

			var insideGap = envelope.Timestamps[column] > gapStart && envelope.Timestamps[column] < gapEnd;
			insideGap.Should().BeFalse();
		}
	}

	[Fact]
	public void Decimate_CenterTimestampSitsBetweenBucketBounds_AndXStaysAscending()
	{
		const int sampleCount = 200;
		const int targetColumns = 10;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];

		// Max precedes min in every bucket, so column X must be the center sample's timestamp (not an
		// extremum's) yet stay strictly ascending.
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = -index;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Timestamps.Should().BeInAscendingOrder();
		for (var column = 0; column < envelope.Timestamps.Count; column++)
		{
			envelope.Timestamps[column].Should().BeOnOrAfter(timestamps[0]);
			envelope.Timestamps[column].Should().BeOnOrBefore(timestamps[^1]);
		}
	}

	[Fact]
	public void Decimate_CenterValueIsPairedWithItsOwnTimestamp()
	{
		// Buckets {0,1} and {2,3,4}; the second's center index is 3, so its column X must be timestamps[3],
		// proving the center value and its X share one sample.
		var timestamps = BuildTimestamps(5);
		var values = new double?[] { 10.0, 5.0, 0.0, 7.0, 3.0 };

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumnCount: 2);

		envelope.Timestamps.Should().HaveCount(2);
		envelope.Center[1].Should().Be(7.0);
		envelope.Timestamps[1].Should().Be(timestamps[3]);
	}

	[Fact]
	public void Decimate_TrailingNullRun_AnchorsNaNGapAtWindowEnd()
	{
		const int sampleCount = 600;
		const int targetColumns = 30;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = index;
		}

		// Empty the right third so a chart without an edge gap would straight-line across it.
		for (var index = 400; index < sampleCount; index++)
		{
			values[index] = null;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Timestamps[^1].Should().Be(timestamps[^1]);
		double.IsNaN(envelope.Center[^1]).Should().BeTrue();
		envelope.Timestamps.Should().BeInAscendingOrder();
	}

	[Fact]
	public void Decimate_LeadingNullRun_AnchorsNaNGapAtWindowStart()
	{
		const int sampleCount = 600;
		const int targetColumns = 30;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = index;
		}

		for (var index = 0; index < 200; index++)
		{
			values[index] = null;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		envelope.Timestamps[0].Should().Be(timestamps[0]);
		double.IsNaN(envelope.Center[0]).Should().BeTrue();
		envelope.Timestamps.Should().BeInAscendingOrder();
	}

	[Fact]
	public void Decimate_AllNullInput_PassThrough_ProducesAllNaN()
	{
		var timestamps = BuildTimestamps(5);
		var values = new double?[] { null, null, null, null, null };

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumnCount: 5);

		envelope.Center.Should().OnlyContain(value => double.IsNaN(value));
	}

	[Fact]
	public void Decimate_NonPositiveTarget_Throws()
	{
		var timestamps = BuildTimestamps(3);
		var values = new double?[] { 1.0, 2.0, 3.0 };

		var act = () => MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumnCount: 0);

		act.Should().Throw<ArgumentOutOfRangeException>();
	}

	[Fact]
	public void Decimate_MismatchedLengths_Throws()
	{
		var timestamps = BuildTimestamps(3);
		var values = new double?[] { 1.0, 2.0 };

		var act = () => MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumnCount: 3);

		act.Should().Throw<ArgumentException>();
	}

	[Fact]
	public void Decimate_EmittedEnvelope_SatisfiesEnvelopeValidation()
	{
		const int sampleCount = 5000;
		const int targetColumns = 120;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = Math.Sin(index / 9.0);
		}

		for (var index = 1000; index < 1100; index++)
		{
			values[index] = null;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		// Re-running the columns back through the validating constructor proves the producer honors
		// the envelope invariants (equal column lengths, strictly ascending timestamps).
		var act = () => new PenHistoryEnvelope(
			envelope.PenId,
			envelope.Timestamps,
			envelope.Min,
			envelope.Max,
			envelope.Center);

		act.Should().NotThrow();
	}

	[Fact]
	public void Decimate_GapFollowedByShortSegment_EmitsStrictlyAscendingTimestamps()
	{
		const int sampleCount = 600;
		const int targetColumns = 50;

		var timestamps = BuildTimestamps(sampleCount);
		var values = new double?[sampleCount];
		for (var index = 0; index < sampleCount; index++)
		{
			values[index] = Math.Sin(index / 7.0);
		}

		for (var index = sampleCount - 50; index < sampleCount - 1; index++)
		{
			values[index] = null;
		}

		var envelope = MinMaxDecimator.Decimate(PenId, timestamps, values, targetColumns);

		for (var index = 1; index < envelope.Timestamps.Count; index++)
		{
			envelope.Timestamps[index].Should().BeAfter(envelope.Timestamps[index - 1]);
		}

		var rebuild = () => new PenHistoryEnvelope(
			envelope.PenId, envelope.Timestamps, envelope.Min, envelope.Max, envelope.Center);
		rebuild.Should().NotThrow();
	}

	private static int CapacityOf<T>(IReadOnlyList<T> column)
	{
		return column.Should().BeOfType<List<T>>().Subject.Capacity;
	}

	private static IReadOnlyList<DateTime> BuildTimestamps(int count)
	{
		var timestamps = new DateTime[count];
		for (var index = 0; index < count; index++)
		{
			timestamps[index] = _origin + TimeSpan.FromSeconds(index);
		}

		return timestamps;
	}
}
