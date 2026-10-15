using Avalonia;
using Avalonia.Input;

using AwesomeAssertions;

using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartWheelRouterTests
{
	private const KeyModifiers ControlShift = KeyModifiers.Control | KeyModifiers.Shift;

	[Theory]
	[InlineData(false, KeyModifiers.None, 0.0, 1.0, ChartWheelAction.ZoomTime, 1)]
	[InlineData(false, KeyModifiers.None, 0.0, -1.0, ChartWheelAction.ZoomTime, -1)]
	[InlineData(false, KeyModifiers.Shift, 0.0, 1.0, ChartWheelAction.PanTime, 1)]
	[InlineData(false, KeyModifiers.Shift, 0.0, -1.0, ChartWheelAction.PanTime, -1)]
	[InlineData(false, KeyModifiers.Control, 0.0, 1.0, ChartWheelAction.ShiftActivePen, 1)]
	[InlineData(false, KeyModifiers.Control, 0.0, -1.0, ChartWheelAction.ShiftActivePen, -1)]
	[InlineData(false, ControlShift, 0.0, 1.0, ChartWheelAction.ShiftActivePen, 1)]
	[InlineData(false, ControlShift, 0.0, -1.0, ChartWheelAction.ShiftActivePen, -1)]
	[InlineData(true, KeyModifiers.None, 0.0, 1.0, ChartWheelAction.ScaleActivePen, 1)]
	[InlineData(true, KeyModifiers.None, 0.0, -1.0, ChartWheelAction.ScaleActivePen, -1)]
	[InlineData(true, KeyModifiers.Shift, 0.0, 1.0, ChartWheelAction.ScaleActivePen, 1)]
	[InlineData(true, KeyModifiers.Shift, 0.0, -1.0, ChartWheelAction.ScaleActivePen, -1)]
	[InlineData(true, KeyModifiers.Control, 0.0, 1.0, ChartWheelAction.ScaleActivePen, 1)]
	[InlineData(true, KeyModifiers.Control, 0.0, -1.0, ChartWheelAction.ScaleActivePen, -1)]
	[InlineData(true, ControlShift, 0.0, 1.0, ChartWheelAction.ScaleActivePen, 1)]
	[InlineData(true, ControlShift, 0.0, -1.0, ChartWheelAction.ScaleActivePen, -1)]
	public void Route_SendsAVerticalNotchToTheAxisThenCtrlThenShiftThenTheZoom(
		bool isAxisRegionHit,
		KeyModifiers modifiers,
		double deltaX,
		double deltaY,
		ChartWheelAction expectedAction,
		int expectedNotch)
	{
		ChartWheelRouter.Route(isAxisRegionHit, modifiers, new Vector(deltaX, deltaY))
			.Should().Be(new ChartWheelGesture(expectedAction, expectedNotch));
	}

	[Theory]
	[InlineData(false, KeyModifiers.Shift, 1.0, ChartWheelAction.PanTime, 1)]
	[InlineData(false, KeyModifiers.Shift, -1.0, ChartWheelAction.PanTime, -1)]
	[InlineData(false, ControlShift, 1.0, ChartWheelAction.ShiftActivePen, 1)]
	[InlineData(true, KeyModifiers.Shift, -1.0, ChartWheelAction.ScaleActivePen, -1)]
	public void Route_WithShiftHeld_TakesTheNotchFromAHorizontalOnlyDelta(
		bool isAxisRegionHit,
		KeyModifiers modifiers,
		double deltaX,
		ChartWheelAction expectedAction,
		int expectedNotch)
	{
		ChartWheelRouter.Route(isAxisRegionHit, modifiers, new Vector(deltaX, 0.0))
			.Should().Be(new ChartWheelGesture(expectedAction, expectedNotch));
	}

	[Fact]
	public void Route_WithShiftHeld_PrefersTheVerticalComponent()
	{
		ChartWheelRouter.Route(false, KeyModifiers.Shift, new Vector(-1.0, 1.0))
			.Should().Be(new ChartWheelGesture(ChartWheelAction.PanTime, 1));
	}

	[Theory]
	[InlineData(false, KeyModifiers.None, 1.0, 0.0)]
	[InlineData(false, KeyModifiers.Control, -1.0, 0.0)]
	[InlineData(true, KeyModifiers.None, 1.0, 0.0)]
	[InlineData(false, KeyModifiers.None, 0.0, 0.0)]
	[InlineData(false, KeyModifiers.Shift, 0.0, 0.0)]
	[InlineData(true, ControlShift, 0.0, 0.0)]
	public void Route_WithoutANotch_DoesNothing(
		bool isAxisRegionHit,
		KeyModifiers modifiers,
		double deltaX,
		double deltaY)
	{
		ChartWheelRouter.Route(isAxisRegionHit, modifiers, new Vector(deltaX, deltaY))
			.Should().Be(new ChartWheelGesture(ChartWheelAction.None, 0));
	}
}
