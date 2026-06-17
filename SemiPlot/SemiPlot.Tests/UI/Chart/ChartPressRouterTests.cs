using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

// Asserts the left-button press dispatch decision the view performs in OnPointerPressed: the branch
// ordering (axis-region pre-empts delta and pan; delta mode pre-empts pan) and the single/double-click
// split on the axis region. This is the LOGIC of the pointer pipeline, decoupled from the AvaPlot
// control so it can be tested without injecting real pointer events.
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
