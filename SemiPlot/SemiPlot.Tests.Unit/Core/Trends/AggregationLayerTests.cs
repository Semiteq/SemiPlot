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
	[InlineData(AggregationLayer.Minute, 15)]
	[InlineData(AggregationLayer.Hour, 900)]
	public void ToPointSpacing_MapsEachLayerToExpectedSeconds(AggregationLayer layer, double expectedSeconds)
	{
		var spacing = layer.ToPointSpacing();

		spacing.Should().Be(TimeSpan.FromSeconds(expectedSeconds));
	}

	[Fact]
	public void ToPointSpacing_RawLayer_Throws()
	{
		var readRawSpacing = () => AggregationLayer.Raw.ToPointSpacing();

		readRawSpacing.Should().Throw<ArgumentOutOfRangeException>();
	}

	// The values are bound to the archive's l column and mirror the Simple-Scada layer codes, so both the
	// membership and the ordinals are part of the contract.
	[Fact]
	public void LayerCodes_KeepTheirOrdinalContract()
	{
		Enum.GetValues<AggregationLayer>().Should().Equal(
			AggregationLayer.Raw,
			AggregationLayer.Minute,
			AggregationLayer.Hour);

		((int)AggregationLayer.Raw).Should().Be(0);
		((int)AggregationLayer.Minute).Should().Be(1);
		((int)AggregationLayer.Hour).Should().Be(2);
	}

	[Fact]
	public void ToPointSpacing_UnknownLayer_Throws()
	{
		var readUnknownLayer = () => ((AggregationLayer)99).ToPointSpacing();

		readUnknownLayer.Should().Throw<ArgumentOutOfRangeException>();
	}
}
