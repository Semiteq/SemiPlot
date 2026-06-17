using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class AggregationLayerTests
{
	[Theory]
	[InlineData(AggregationLayer.Raw, 1)]
	[InlineData(AggregationLayer.Minute, 60)]
	[InlineData(AggregationLayer.Hour, 3600)]
	[InlineData(AggregationLayer.Day, 86400)]
	public void ToSampleInterval_MapsEachLayerToExpectedSeconds(AggregationLayer layer, double expectedSeconds)
	{
		var interval = layer.ToSampleInterval();

		interval.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void ToSampleInterval_ProducesDistinctIntervalsAcrossLayers()
	{
		var intervals = new[]
		{
			AggregationLayer.Raw.ToSampleInterval(),
			AggregationLayer.Minute.ToSampleInterval(),
			AggregationLayer.Hour.ToSampleInterval(),
			AggregationLayer.Day.ToSampleInterval()
		};

		intervals.Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void ToSampleInterval_IncreasesWithCoarserLayer()
	{
		var raw = AggregationLayer.Raw.ToSampleInterval();
		var minute = AggregationLayer.Minute.ToSampleInterval();
		var hour = AggregationLayer.Hour.ToSampleInterval();
		var day = AggregationLayer.Day.ToSampleInterval();

		raw.Should().BeLessThan(minute);
		minute.Should().BeLessThan(hour);
		hour.Should().BeLessThan(day);
	}
}
