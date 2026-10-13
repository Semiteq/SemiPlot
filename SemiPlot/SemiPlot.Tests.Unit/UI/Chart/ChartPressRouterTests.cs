using Avalonia;

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
	[InlineData(false, LeftButtonTool.Pan, ChartPressAction.PanOrPickPen)]
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

	[Theory]
	[InlineData(0.0, 0.0, false)]
	[InlineData(4.0, 0.0, false)]
	[InlineData(0.0, -4.0, false)]
	[InlineData(3.0, 3.0, true)]
	[InlineData(-5.0, 0.0, true)]
	public void IsPastClickSlop_MeasuresTheEuclideanDistanceFromThePressPoint(
		double offsetX,
		double offsetY,
		bool expected)
	{
		var origin = new Point(100.0, 200.0);

		ChartPressRouter.IsPastClickSlop(origin, new Point(origin.X + offsetX, origin.Y + offsetY))
			.Should().Be(expected);
	}
}
