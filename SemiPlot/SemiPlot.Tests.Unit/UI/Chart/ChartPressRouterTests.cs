using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPressRouterTests
{
	[Fact]
	public void LeftPress_OverDataArea_InPanMode_Pans()
	{
		ChartPressRouter.Route(isAxisRegionHit: false, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.Pan);
	}

	[Fact]
	public void LeftPress_OverDataArea_InDeltaMode_PlacesADeltaCursor_DoesNotPan()
	{
		ChartPressRouter.Route(isAxisRegionHit: false, LeftButtonTool.DeltaPlacement)
			.Should().Be(ChartPressAction.PlaceDeltaCursor);
	}

	[Fact]
	public void AxisRegionPress_PreEmptsPan_EvenInPanMode()
	{
		ChartPressRouter.Route(isAxisRegionHit: true, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.EditAxisScale);
	}

	[Fact]
	public void AxisRegionPress_PreEmptsDeltaPlacement_EvenInDeltaMode()
	{
		ChartPressRouter.Route(isAxisRegionHit: true, LeftButtonTool.DeltaPlacement)
			.Should().Be(ChartPressAction.EditAxisScale);
	}
}
