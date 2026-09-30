using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Avalonia;

using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.Unit.UI.Chart;

// The input guard: pointer events driven through Avalonia's real pipeline into TrendChartView's handlers,
// exercising hit testing, capture and routing that every other chart test bypasses by calling seams directly.
// Avalonia.Headless posts raw input in window-client coordinates; Plot.RenderInMemory forces the render.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPointerInputTests
{
	// Drag leftwards: Pan clamps From at FirstSample, so only a forward pan moves the window away from its
	// startup position.
	private const double DragDistancePixels = 120.0;
	private const double HoverDistancePixels = 40.0;

	[AvaloniaFact]
	public void PressMoveAndRelease_PanTheNavigationWindowByTheDraggedTimeDistance()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);
		var expectedShift = AnchorAt(viewModel.Plot, pressAt) - AnchorAt(viewModel.Plot, moveTo);
		var fromBefore = viewModel.Navigation.From;
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		viewModel.IsDragging.Should().BeTrue("the press must reach the view and start a drag");

		window.MouseMove(ToWindow(plotControl, window, moveTo));
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left);

		expectedShift.Should().BePositive("dragging leftwards moves the window forwards in time");
		viewModel.Navigation.From.Should().BeCloseTo(
			fromBefore + expectedShift,
			TimeSpan.FromMilliseconds(1.0),
			"the drag pans by the time distance its pixels cover");
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(
			widthBefore, "a pan shifts the window without resizing it");
		viewModel.IsDragging.Should().BeFalse("the release must end the drag");
	}

	[AvaloniaFact]
	public void WheelUpThenWheelDown_NarrowThenWidenTheNavigationWindow()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var wheelAt = ToWindow(plotControl, window, DataAreaCenter(viewModel));
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;

		window.MouseWheel(wheelAt, new Vector(0.0, 1.0));
		var widthAfterZoomIn = viewModel.Navigation.To - viewModel.Navigation.From;

		window.MouseWheel(wheelAt, new Vector(0.0, -1.0));
		var widthAfterZoomOut = viewModel.Navigation.To - viewModel.Navigation.From;

		widthAfterZoomIn.Should().BeLessThan(widthBefore, "a wheel notch up zooms in");
		widthAfterZoomOut.Should().BeGreaterThan(widthAfterZoomIn, "a wheel notch down zooms back out");
	}

	[AvaloniaFact]
	public void CaptureLostMidDrag_EndsTheDrag_SoLaterMovesHoverInsteadOfPanning()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressedPointer = CapturePointerOfNextPress(window);
		var pressAt = DataAreaCenter(viewModel);
		var dragTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);
		var hoverTo = new Point(dragTo.X - HoverDistancePixels, dragTo.Y);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(plotControl, window, dragTo));
		viewModel.IsDragging.Should().BeTrue();
		var fromAfterDrag = viewModel.Navigation.From;

		// What the platform does on a deactivation or focus steal: PlatformCaptureLost routes into the same
		// Capture(null), raising PointerCaptureLost with no release sent. Covers Capture(null) only; a
		// version that reroutes PlatformCaptureLost away from it leaves this test green.
		pressedPointer().Capture(null);

		viewModel.IsDragging.Should().BeFalse("losing capture must end the drag as a release would");

		window.MouseMove(ToWindow(plotControl, window, hoverTo));

		viewModel.Navigation.From.Should().Be(
			fromAfterDrag, "no drag may remain in progress after capture loss");
		viewModel.CursorTime.Should().NotBeNull(
			"the move still reaches the view, so the unchanged window is not an unrouted event");
	}

	private static Func<IPointer> CapturePointerOfNextPress(Window window)
	{
		IPointer? pressedPointer = null;
		window.AddHandler(
			InputElement.PointerPressedEvent,
			(_, eventArgs) => pressedPointer = eventArgs.Pointer,
			RoutingStrategies.Bubble,
			handledEventsToo: true);

		return () => pressedPointer
					?? throw new InvalidOperationException("No pointer press reached the window.");
	}

	private static Point DataAreaCenter(TrendChartViewModel viewModel)
	{
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;

		return new Point((dataRect.Left + dataRect.Right) / 2.0, (dataRect.Top + dataRect.Bottom) / 2.0);
	}

	private static Point ToWindow(AvaPlot plotControl, Window window, Point plotPoint)
	{
		return plotControl.TranslatePoint(plotPoint, window)
			?? throw new InvalidOperationException("The plot control is not in the window's visual tree.");
	}

	// Mirrors TrendChartView.AnchorAt: the same conversion the handlers apply to a pointer position.
	private static DateTime AnchorAt(Plot plot, Point plotPoint)
	{
		var x = plot.GetCoordinates(new Pixel((float)plotPoint.X, (float)plotPoint.Y)).X;

		return LocalTimeAxis.FromAxis(x);
	}
}
