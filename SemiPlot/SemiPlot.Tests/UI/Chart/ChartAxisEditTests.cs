using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartAxisEditTests
{
	[Fact]
	public void SeedManualLimits_EditingMax_CarriesMinFromCurrentRange()
	{
		var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound: 90.0, editsMax: true, (Min: 10.0, Max: 50.0));

		min.Should().Be(10.0);
		max.Should().Be(90.0);
	}

	[Fact]
	public void SeedManualLimits_EditingMin_CarriesMaxFromCurrentRange()
	{
		var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound: 5.0, editsMax: false, (Min: 10.0, Max: 50.0));

		min.Should().Be(5.0);
		max.Should().Be(50.0);
	}

	[Fact]
	public void SeedManualLimits_TypedBoundCrossesTheOtherEnd_OrdersTheResult()
	{
		var (min, max) = ChartAxisEdit.SeedManualLimits(typedBound: 5.0, editsMax: true, (Min: 10.0, Max: 50.0));

		min.Should().Be(5.0);
		max.Should().Be(10.0);
	}
}
