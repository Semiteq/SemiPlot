using Avalonia;
using Avalonia.Input;

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
	[InlineData(false, KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.PanOrPickPen)]
	[InlineData(false, KeyModifiers.None, LeftButtonTool.DeltaPlacement, ChartPressAction.PlaceDeltaCursor)]
	[InlineData(true, KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.EditAxisScale)]
	[InlineData(true, KeyModifiers.None, LeftButtonTool.DeltaPlacement, ChartPressAction.EditAxisScale)]
	[InlineData(false, KeyModifiers.Control, LeftButtonTool.Pan, ChartPressAction.ShiftActivePen)]
	[InlineData(false, KeyModifiers.Control, LeftButtonTool.DeltaPlacement, ChartPressAction.ShiftActivePen)]
	[InlineData(true, KeyModifiers.Control, LeftButtonTool.Pan, ChartPressAction.EditAxisScale)]
	[InlineData(true, KeyModifiers.Control, LeftButtonTool.DeltaPlacement, ChartPressAction.EditAxisScale)]
	[InlineData(false, KeyModifiers.Shift, LeftButtonTool.Pan, ChartPressAction.PanOrPickPen)]
	public void Route_SendsALeftPressToTheAxisEditThenCtrlThenTheTool(
		bool isAxisRegionHit,
		KeyModifiers modifiers,
		LeftButtonTool tool,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(isAxisRegionHit, modifiers, tool).Should().Be(expected);
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
