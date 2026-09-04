using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

// Branch ordering of the left-button press dispatch: axis-region pre-empts delta and pan, delta mode
// pre-empts pan, and the axis region splits single- vs double-click.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPressRouterTests
{
	[Fact]
	public void LeftPress_OverDataArea_InPanMode_Pans()
	{
		ChartPressRouter.Route(isAxisRegionHit: false, clickCount: 1, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.Pan);
	}

	[Fact]
	public void LeftPress_OverDataArea_InDeltaMode_PlacesADeltaCursor_DoesNotPan()
	{
		ChartPressRouter.Route(isAxisRegionHit: false, clickCount: 1, LeftButtonTool.DeltaPlacement)
			.Should().Be(ChartPressAction.PlaceDeltaCursor);
	}

	[Fact]
	public void AxisRegionPress_PreEmptsPan_EvenInPanMode()
	{
		ChartPressRouter.Route(isAxisRegionHit: true, clickCount: 1, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.EditAxisBound);
	}

	[Fact]
	public void AxisRegionPress_PreEmptsDeltaPlacement_EvenInDeltaMode()
	{
		ChartPressRouter.Route(isAxisRegionHit: true, clickCount: 1, LeftButtonTool.DeltaPlacement)
			.Should().Be(ChartPressAction.EditAxisBound);
	}

	[Fact]
	public void AxisRegionDoubleClick_Autoscales()
	{
		ChartPressRouter.Route(isAxisRegionHit: true, clickCount: 2, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.AutoscaleAxis);
	}

	[Fact]
	public void DataAreaDoubleClick_StillFollowsTheActiveTool_NotAutoscale()
	{
		ChartPressRouter.Route(isAxisRegionHit: false, clickCount: 2, LeftButtonTool.Pan)
			.Should().Be(ChartPressAction.Pan);
	}
}
