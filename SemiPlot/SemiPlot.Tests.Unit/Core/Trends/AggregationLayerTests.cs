using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Unit.Core.Trends;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class AggregationLayerTests
{
	[Theory]
	[InlineData(AggregationLayer.Raw, 1)]
	[InlineData(AggregationLayer.Minute, 15)]
	[InlineData(AggregationLayer.Hour, 900)]
	[InlineData(AggregationLayer.Day, 21600)]
	public void ToPointSpacing_MapsEachLayerToExpectedSeconds(AggregationLayer layer, double expectedSeconds)
	{
		var spacing = layer.ToPointSpacing();

		spacing.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
	}

	// The layer ladder reads the next coarser layer as `layer + 1`, and the values mirror the Simple-Scada
	// archive layer codes, so both the membership and the ordinals are part of the contract.
	[Fact]
	public void LayerCodes_KeepTheirOrdinalContract()
	{
		Enum.GetValues<AggregationLayer>().Should().Equal(
			AggregationLayer.Raw,
			AggregationLayer.Minute,
			AggregationLayer.Hour,
			AggregationLayer.Day);

		((int)AggregationLayer.Raw).Should().Be(0);
		((int)AggregationLayer.Minute).Should().Be(1);
		((int)AggregationLayer.Hour).Should().Be(2);
		((int)AggregationLayer.Day).Should().Be(3);
	}

	[Fact]
	public void ToPointSpacing_UnknownLayer_Throws()
	{
		var readUnknownLayer = () => ((AggregationLayer)99).ToPointSpacing();

		readUnknownLayer.Should().Throw<ArgumentOutOfRangeException>();
	}
}
