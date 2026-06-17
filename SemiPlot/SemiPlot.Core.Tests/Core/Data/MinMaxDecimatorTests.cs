using AwesomeAssertions;

using SemiPlot.DataSource.Stub;

using Xunit;

namespace SemiPlot.Tests.Core.Data;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class MinMaxDecimatorTests
{
	private const long PenId = 42;

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

		// No finite band column may sit inside the gap interval — that proves a column never straddles it.
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

		// Make the max precede the min inside every bucket so the column X cannot be the min timestamp;
		// it must be the center sample's timestamp, and the result must still be strictly ascending.
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
		// Five samples, two target columns → buckets {0,1} and {2,3,4}. The second bucket's center index
		// is 3; its column timestamp must be timestamps[3] (the center sample's time), proving the center
		// value and its X share one sample rather than the center value riding an extremum timestamp.
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

		// Empty the right third of the window so a chart without an edge gap would straight-line across it.
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
