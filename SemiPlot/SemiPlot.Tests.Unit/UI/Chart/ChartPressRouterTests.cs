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
		ChartPressRouter.Route(region, modifiers, tool, clickCount: 1).Should().Be(expected);
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
		ChartPressRouter.Route(ChartPressRegion.TimeAxis, modifiers, tool, clickCount: 1).Should().Be(expected);
	}

	[Theory]
	[InlineData(KeyModifiers.None, LeftButtonTool.Pan, ChartPressAction.DragMarker)]
	[InlineData(KeyModifiers.Shift, LeftButtonTool.Pan, ChartPressAction.DragMarker)]
	[InlineData(KeyModifiers.None, LeftButtonTool.DeltaPlacement, ChartPressAction.PlaceDeltaCursor)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.Pan, ChartPressAction.ShiftActivePen)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.DeltaPlacement, ChartPressAction.ShiftActivePen)]
	public void Route_SendsAMarkerPressAfterCtrlAndTheDeltaTool(
		KeyModifiers modifiers,
		LeftButtonTool tool,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(ChartPressRegion.Marker, modifiers, tool, clickCount: 1).Should().Be(expected);
	}

	[Theory]
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.None, LeftButtonTool.Pan, 2, ChartPressAction.AddMarker)]
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.Shift, LeftButtonTool.Pan, 2, ChartPressAction.AddMarker)]
	[InlineData(ChartPressRegion.DataArea, KeyModifiers.None, LeftButtonTool.Pan, 3, ChartPressAction.PanOrPickPen)]
	[InlineData(ChartPressRegion.Marker, KeyModifiers.None, LeftButtonTool.Pan, 2, ChartPressAction.DeleteMarker)]
	[InlineData(ChartPressRegion.Marker, KeyModifiers.None, LeftButtonTool.Pan, 3, ChartPressAction.DragMarker)]
	[InlineData(
		ChartPressRegion.Marker,
		KeyModifiers.None,
		LeftButtonTool.DeltaPlacement,
		2,
		ChartPressAction.PlaceDeltaCursor)]
	[InlineData(
		ChartPressRegion.Marker,
		KeyModifiers.Control,
		LeftButtonTool.Pan,
		2,
		ChartPressAction.ShiftActivePen)]
	[InlineData(
		ChartPressRegion.DataArea,
		KeyModifiers.None,
		LeftButtonTool.DeltaPlacement,
		2,
		ChartPressAction.PlaceDeltaCursor)]
	[InlineData(
		ChartPressRegion.DataArea,
		KeyModifiers.Control,
		LeftButtonTool.Pan,
		2,
		ChartPressAction.ShiftActivePen)]
	[InlineData(
		ChartPressRegion.TimeAxis,
		KeyModifiers.None,
		LeftButtonTool.Pan,
		2,
		ChartPressAction.PanOrEditTimeWindow)]
	[InlineData(
		ChartPressRegion.ActivePenAxis,
		KeyModifiers.None,
		LeftButtonTool.Pan,
		2,
		ChartPressAction.EditAxisScale)]
	public void Route_TurnsOnlyTheSecondClickOfAPanPressIntoAnAddOrADelete(
		ChartPressRegion region,
		KeyModifiers modifiers,
		LeftButtonTool tool,
		int clickCount,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(region, modifiers, tool, clickCount).Should().Be(expected);
	}

	[Theory]
	[InlineData(KeyModifiers.None, LeftButtonTool.Pan, 1, ChartPressAction.PanOrPickPen)]
	[InlineData(KeyModifiers.None, LeftButtonTool.Pan, 2, ChartPressAction.PanOrPickPen)]
	[InlineData(KeyModifiers.Shift, LeftButtonTool.Pan, 2, ChartPressAction.PanOrPickPen)]
	[InlineData(KeyModifiers.None, LeftButtonTool.Pan, 3, ChartPressAction.PanOrPickPen)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.Pan, 1, ChartPressAction.ShiftActivePen)]
	[InlineData(KeyModifiers.Control, LeftButtonTool.Pan, 2, ChartPressAction.ShiftActivePen)]
	[InlineData(KeyModifiers.None, LeftButtonTool.DeltaPlacement, 1, ChartPressAction.PlaceDeltaCursor)]
	[InlineData(KeyModifiers.None, LeftButtonTool.DeltaPlacement, 2, ChartPressAction.PlaceDeltaCursor)]
	public void Route_SendsALockedMarkerPressToAPanForEveryClickCount(
		KeyModifiers modifiers,
		LeftButtonTool tool,
		int clickCount,
		ChartPressAction expected)
	{
		ChartPressRouter.Route(ChartPressRegion.LockedMarker, modifiers, tool, clickCount).Should().Be(expected);
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
