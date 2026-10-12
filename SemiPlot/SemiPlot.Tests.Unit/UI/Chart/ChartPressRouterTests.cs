using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPressRouterTests
{
	[Theory]
	[InlineData(false, LeftButtonTool.Pan, ChartPressAction.Pan)]
	[InlineData(false, LeftButtonTool.DeltaPlacement, ChartPressAction.PlaceDeltaCursor)]
	[InlineData(true, LeftButtonTool.Pan, ChartPressAction.EditAxisScale)]
	[InlineData(true, LeftButtonTool.DeltaPlacement, ChartPressAction.EditAxisScale)]
	public void Route_SendsALeftPressToTheAxisEditBeforeTheToolAndOtherwiseToTheTool(
		bool isAxisRegionHit,
		LeftButtonTool tool,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(isAxisRegionHit, tool).Should().Be(expected);
	}
}
