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
	private const double JitterPixels = 2.0;
	private const double SubSlopPixels = 3.0;
	private const double AxisRegionInsetPixels = 10.0;
	private const double PickToleranceClearancePixels = 8.0;
	private static readonly TimeSpan _dragTolerance = TimeSpan.FromMilliseconds(1.0);

	[AvaloniaFact]
	public void PressMoveAndRelease_PanTheNavigationWindowByTheDraggedTimeDistance()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);
		var fromBefore = viewModel.Navigation.From;
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;
		var expectedShift = ExpectedDragShift(viewModel);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		viewModel.IsDragging.Should().BeTrue("the press must reach the view and start a drag");

		window.MouseMove(ToWindow(plotControl, window, moveTo));
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left);

		expectedShift.Should().BePositive("dragging leftwards moves the window forwards in time");
		viewModel.Navigation.From.Should().BeCloseTo(
			fromBefore + expectedShift, _dragTolerance, "the drag pans by the time distance its pixels cover");
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

	[AvaloniaFact]
	public void AClickOnALine_MakesThatPenActive()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var onSecondLine = PixelOnSecondPensLine(viewModel);
		var fromBefore = viewModel.Navigation.From;
		var stickyBefore = viewModel.Navigation.IsSticky;

		HeadlessInput.Click(window, plotControl, onSecondLine);

		viewModel.ActivePenId.Should().Be(2);
		viewModel.Navigation.From.Should().Be(fromBefore);
		viewModel.Navigation.IsSticky.Should().Be(stickyBefore);
	}

	[AvaloniaFact]
	public void AClickOnEmptySpace_KeepsTheActivePen()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var onSecondLine = PixelOnSecondPensLine(viewModel);
		var inEmptySpace = new Point(onSecondLine.X, onSecondLine.Y - (3.0 * PickToleranceClearancePixels));
		DistanceToLine(viewModel, 1, inEmptySpace).Should().BeGreaterThan(PickToleranceClearancePixels);
		DistanceToLine(viewModel, 2, inEmptySpace).Should().BeGreaterThan(PickToleranceClearancePixels);

		HeadlessInput.Click(window, plotControl, inEmptySpace);

		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaFact]
	public void AJitterIntoThePast_NeitherPansNorDetachesFollow()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		window.MouseWheel(ToWindow(plotControl, window, pressAt), new Vector(0.0, 1.0));
		viewModel.Navigation.From.Should().BeAfter(
			viewModel.Navigation.FirstSample, "a pan into the past must have room");
		viewModel.Navigation.IsSticky.Should().BeTrue("follow is what the jitter must not detach");
		var fromBefore = viewModel.Navigation.From;
		var jitteredTo = new Point(pressAt.X + JitterPixels, pressAt.Y);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(plotControl, window, jitteredTo));
		window.MouseUp(ToWindow(plotControl, window, jitteredTo), MouseButton.Left);

		viewModel.Navigation.From.Should().Be(fromBefore);
		viewModel.Navigation.IsSticky.Should().BeTrue();
	}

	[AvaloniaFact]
	public void ADragThatStartsOnALine_PansAndPicksNothing()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = PixelOnSecondPensLine(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);
		var fromBefore = viewModel.Navigation.From;
		var expectedShift = ExpectedDragShift(viewModel);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(plotControl, window, moveTo));
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left);

		viewModel.Navigation.From.Should().BeCloseTo(fromBefore + expectedShift, _dragTolerance);
		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaFact]
	public void ADragMovingWithinTheSlopFirst_KeepsEveryPixelOfTheDrag()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = PixelOnSecondPensLine(viewModel);
		var fromBefore = viewModel.Navigation.From;
		var expectedShift = ExpectedDragShift(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(plotControl, window, new Point(pressAt.X - SubSlopPixels, pressAt.Y)));
		window.MouseMove(ToWindow(plotControl, window, moveTo));
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left);

		viewModel.Navigation.From.Should().BeCloseTo(fromBefore + expectedShift, _dragTolerance);
	}

	[AvaloniaFact]
	public void AReleaseAfterTheCaptureIsLost_NeverPicks()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressedPointer = CapturePointerOfNextPress(window);
		var onSecondLine = PixelOnSecondPensLine(viewModel);

		window.MouseDown(ToWindow(plotControl, window, onSecondLine), MouseButton.Left);
		pressedPointer().Capture(null);
		window.MouseUp(ToWindow(plotControl, window, onSecondLine), MouseButton.Left);

		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaFact]
	public void AClickOnALineInDeltaMode_PlacesACursorAndPicksNothing()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var onSecondLine = PixelOnSecondPensLine(viewModel);
		viewModel.SetDeltaModeEnabled(true);

		HeadlessInput.Click(window, plotControl, onSecondLine);

		viewModel.ActivePenId.Should().Be(1);
		viewModel.DeltaFirstCursor.Should().NotBeNull("the press must have been routed as a delta placement");
	}

	[AvaloniaFact]
	public void AClickOnTheActiveAxisRegion_PicksNothing()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var onSecondLinesRow = new Point(dataRect.Left - AxisRegionInsetPixels, PixelOnSecondPensLine(viewModel).Y);

		HeadlessInput.Click(window, plotControl, onSecondLinesRow);

		viewModel.ActivePenId.Should().Be(1);
	}

	[AvaloniaFact]
	public void AnotherButtonsReleaseDuringALeftPress_NeverPicks()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var onSecondLine = ToWindow(plotControl, window, PixelOnSecondPensLine(viewModel));

		window.MouseDown(onSecondLine, MouseButton.Left);
		window.MouseDown(onSecondLine, MouseButton.Right);
		window.MouseUp(onSecondLine, MouseButton.Right);

		viewModel.ActivePenId.Should().Be(1);
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

	// The pens' axes hold fixed spans so the flat line of pen 2 lies inside the data area.
	private static TrendChartViewModel CreateViewModelWithFixedScales()
	{
		var viewModel = CreateLoadedViewModel();
		viewModel.SetAxisLimits(1, 0.0, 10.0);
		viewModel.SetAxisLimits(2, 0.0, 100.0);

		return viewModel;
	}

	// Midway between the two columns, where pen 2's flat line is farthest from pen 1's vertical strokes.
	private static Point PixelOnSecondPensLine(TrendChartViewModel viewModel)
	{
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var axes = viewModel.FindPen(2)!.Line.Axes;
		var point = new Point(
			axes.XAxis.GetPixel(LocalTimeAxis.ToAxis(ChartTestBuilder.From.AddMinutes(30.0)), dataRect),
			axes.YAxis.GetPixel(50.0, dataRect));
		DistanceToLine(viewModel, 2, point).Should().BeLessThan(1.0);
		DistanceToLine(viewModel, 1, point).Should().BeGreaterThan(
			PickToleranceClearancePixels, "the fixture must keep the two lines apart at the click");

		return point;
	}

	private static double DistanceToLine(TrendChartViewModel viewModel, int penId, Point point)
	{
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;

		return viewModel.FindPen(penId)!.Line.DistanceTo(new Pixel((float)point.X, (float)point.Y), dataRect);
	}

	private static TimeSpan ExpectedDragShift(TrendChartViewModel viewModel)
	{
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		var dataAreaWidth = viewModel.Plot.RenderManager.LastRender.Layout.DataRect.Width;

		return width * (DragDistancePixels / dataAreaWidth);
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
}
