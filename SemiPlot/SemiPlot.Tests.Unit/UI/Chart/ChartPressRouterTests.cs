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
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.PanOrPickPen)]
	[InlineData(
		ChartPressRegion.DataArea,
		KeyModifiers.None,
		LeftButtonTool.DeltaPlacement,
		ChartPressAction.PlaceDeltaCursor)]
	[InlineData(ChartPressRegion.ActivePenAxis, KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.EditAxisScale)]
	[InlineData(
		ChartPressRegion.ActivePenAxis,
		KeyModifiers.None,
		LeftButtonTool.DeltaPlacement,
		ChartPressAction.EditAxisScale)]
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.Control, LeftButtonTool.Pan, ChartPressAction.ShiftActivePen)]
	[InlineData(
		ChartPressRegion.DataArea,
		KeyModifiers.Control,
		LeftButtonTool.DeltaPlacement,
		ChartPressAction.ShiftActivePen)]
	[InlineData(
		ChartPressRegion.ActivePenAxis,
		KeyModifiers.Control,
		LeftButtonTool.Pan,
		ChartPressAction.EditAxisScale)]
	[InlineData(
		ChartPressRegion.ActivePenAxis,
		KeyModifiers.Control,
		LeftButtonTool.DeltaPlacement,
		ChartPressAction.EditAxisScale)]
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.Shift, LeftButtonTool.Pan, ChartPressAction.PanOrPickPen)]
	public void Route_SendsALeftPressToTheAxisEditThenCtrlThenTheTool(
		ChartPressRegion region,
		KeyModifiers modifiers,
		LeftButtonTool tool,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(region, modifiers, tool).Should().Be(expected);
	}

	[Theory]
	[InlineData(KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.PanOrEditTimeWindow)]
	[InlineData(KeyModifiers.None, LeftButtonTool.DeltaPlacement, ChartPressAction.PanOrEditTimeWindow)]
	[InlineData(KeyModifiers.Shift, LeftButtonTool.Pan, ChartPressAction.PanOrEditTimeWindow)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.Pan, ChartPressAction.ShiftActivePen)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.DeltaPlacement, ChartPressAction.ShiftActivePen)]
	public void Route_SendsATimeAxisPressAfterCtrlAndAheadOfTheTool(
		KeyModifiers modifiers,
		LeftButtonTool tool,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(ChartPressRegion.TimeAxis, modifiers, tool).Should().Be(expected);
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
