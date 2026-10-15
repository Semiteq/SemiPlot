using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Interactivity;

using AwesomeAssertions;

using ScottPlot;
using ScottPlot.Avalonia;

using SemiPlot.Core.Trends;
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
	private const double WheelPanFraction = 0.1;
	private const double WheelShiftFraction = 0.1;
	private const double WheelNarrowFactor = 0.8;
	private const double AnchorHeightFraction = 0.25;
	private const double LimitTolerance = 1e-9;
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
	public void ShiftWheelDown_PansTowardNewerDataByATenthOfTheWindow()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var fromBefore = viewModel.Navigation.From;
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;

		window.MouseWheel(
			ToWindow(plotControl, window, DataAreaCenter(viewModel)),
			new Vector(0.0, -1.0),
			RawInputModifiers.Shift);

		viewModel.Navigation.From.Should().BeCloseTo(fromBefore + (widthBefore * WheelPanFraction), _dragTolerance);
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(widthBefore);
	}

	[AvaloniaFact]
	public void ShiftWheelUp_PansTowardOlderDataByATenthOfTheWindow()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var wheelAt = ToWindow(plotControl, window, DataAreaCenter(viewModel));
		// Zoom quantizes the width, so one notch about the center leaves less than a tenth of it behind From.
		window.MouseWheel(wheelAt, new Vector(0.0, 1.0));
		window.MouseWheel(wheelAt, new Vector(0.0, 1.0));
		var fromBefore = viewModel.Navigation.From;
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;
		(fromBefore - viewModel.Navigation.FirstSample).Should().BeGreaterThan(
			widthBefore * WheelPanFraction, "a pan into the past must have room");

		window.MouseWheel(wheelAt, new Vector(0.0, 1.0), RawInputModifiers.Shift);

		viewModel.Navigation.From.Should().BeCloseTo(fromBefore - (widthBefore * WheelPanFraction), _dragTolerance);
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(widthBefore);
	}

	[AvaloniaFact]
	public void ShiftWheelUp_WithNoRoomIntoThePast_KeepsTheWindowAndFollow()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var fromBefore = viewModel.Navigation.From;
		fromBefore.Should().Be(viewModel.Navigation.FirstSample, "the viewer opens with no room into the past");
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;
		var stickyBefore = viewModel.Navigation.IsSticky;

		window.MouseWheel(
			ToWindow(plotControl, window, DataAreaCenter(viewModel)),
			new Vector(0.0, 1.0),
			RawInputModifiers.Shift);

		viewModel.Navigation.From.Should().Be(fromBefore);
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(widthBefore);
		viewModel.Navigation.IsSticky.Should().Be(stickyBefore);
	}

	[AvaloniaFact]
	public void CtrlWheelUp_LowersTheActivePensLimitsAndPinsItToManual()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Auto, "the gesture must pin an Auto pen");
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var step = (maxBefore - minBefore) * WheelShiftFraction;
		var secondPenBefore = viewModel.ScaleSettings[2];
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;

		window.MouseWheel(
			ToWindow(plotControl, window, DataAreaCenter(viewModel)),
			new Vector(0.0, 1.0),
			RawInputModifiers.Control);

		var settings = viewModel.ScaleSettings[1];
		settings.Mode.Should().Be(ScaleMode.Manual);
		settings.ManualMin.Should().BeApproximately(minBefore - step, LimitTolerance);
		settings.ManualMax.Should().BeApproximately(maxBefore - step, LimitTolerance);
		viewModel.ScaleSettings[2].Should().Be(secondPenBefore);
		viewModel.Navigation.From.Should().Be(fromBefore, "Ctrl + wheel leaves the time window alone");
		viewModel.Navigation.To.Should().Be(toBefore);
	}

	[AvaloniaFact]
	public void WheelUpOverTheActiveAxis_NarrowsThePenAboutTheCursor()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var dataRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		var wheelAt = new Point(
			dataRect.Left - AxisRegionInsetPixels,
			dataRect.Top + (AnchorHeightFraction * dataRect.Height));
		var axis = viewModel.ActivePenAxis!;
		var valueUnderCursor = axis.GetCoordinate((float)wheelAt.Y, dataRect);
		var spanBefore = ActivePenSpan(viewModel);
		var fromBefore = viewModel.Navigation.From;
		var toBefore = viewModel.Navigation.To;

		window.MouseWheel(ToWindow(plotControl, window, wheelAt), new Vector(0.0, 1.0));
		DriveOneFrame();

		ActivePenSpan(viewModel).Should().BeApproximately(WheelNarrowFactor * spanBefore, LimitTolerance);
		var renderedRect = viewModel.Plot.RenderManager.LastRender.Layout.DataRect;
		axis.GetPixel(valueUnderCursor, renderedRect).Should().BeApproximately((float)wheelAt.Y, 1f);
		viewModel.Navigation.From.Should().Be(fromBefore, "the axis wheel scales the pen, not the time window");
		viewModel.Navigation.To.Should().Be(toBefore);
	}

	[AvaloniaFact]
	public void HorizontalWheel_LeavesTheWindowWidth()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var fromBefore = viewModel.Navigation.From;
		var widthBefore = viewModel.Navigation.To - viewModel.Navigation.From;

		window.MouseWheel(ToWindow(plotControl, window, DataAreaCenter(viewModel)), new Vector(1.0, 0.0));

		viewModel.Navigation.From.Should().Be(fromBefore, "a horizontal wheel without Shift neither zooms nor pans");
		(viewModel.Navigation.To - viewModel.Navigation.From).Should().Be(widthBefore);
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

	[AvaloniaFact]
	public void CtrlDiagonalDrag_ShiftsTheActivePenOnly()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y - DragDistancePixels);
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var step = ExpectedPenShift(viewModel, DragDistancePixels);
		var secondPenBefore = viewModel.ScaleSettings[2];
		var fromBefore = viewModel.Navigation.From;
		var stickyBefore = viewModel.Navigation.IsSticky;

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left, RawInputModifiers.Control);
		window.MouseMove(ToWindow(plotControl, window, moveTo), RawInputModifiers.Control);
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left, RawInputModifiers.Control);

		var settings = viewModel.ScaleSettings[1];
		settings.ManualMin.Should().BeApproximately(minBefore - step, LimitTolerance, "an upward drag raises the line");
		settings.ManualMax.Should().BeApproximately(maxBefore - step, LimitTolerance);
		viewModel.ScaleSettings[2].Should().Be(secondPenBefore);
		viewModel.Navigation.From.Should().Be(fromBefore, "the horizontal part of a Ctrl drag is ignored");
		viewModel.Navigation.IsSticky.Should().Be(stickyBefore);
		viewModel.IsDragging.Should().BeFalse("the release must end the drag");
	}

	[AvaloniaTheory]
	[InlineData(false)]
	[InlineData(true)]
	public void CtrlClickOnALine_ChangesNothing(bool isDeltaModeEnabled)
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var onSecondLine = PixelOnSecondPensLine(viewModel);
		viewModel.SetDeltaModeEnabled(isDeltaModeEnabled);
		var firstPenBefore = viewModel.ScaleSettings[1];
		var secondPenBefore = viewModel.ScaleSettings[2];

		HeadlessInput.Click(window, plotControl, onSecondLine, RawInputModifiers.Control);

		viewModel.ActivePenId.Should().Be(1);
		viewModel.ScaleSettings[1].Should().Be(firstPenBefore);
		viewModel.ScaleSettings[2].Should().Be(secondPenBefore);
		viewModel.DeltaFirstCursor.Should().BeNull("Ctrl takes the press before delta mode");
		viewModel.IsDragging.Should().BeFalse();
	}

	[AvaloniaFact]
	public void CaptureLostMidCtrlDrag_EndsTheDrag_SoLaterMovesLeaveThePen()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressedPointer = CapturePointerOfNextPress(window);
		var pressAt = DataAreaCenter(viewModel);
		var dragTo = new Point(pressAt.X, pressAt.Y - DragDistancePixels);
		var hoverTo = new Point(dragTo.X, dragTo.Y - HoverDistancePixels);
		var (_, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var handCursor = plotControl.Cursor;

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left, RawInputModifiers.Control);
		window.MouseMove(ToWindow(plotControl, window, dragTo), RawInputModifiers.Control);
		viewModel.IsDragging.Should().BeTrue();
		var settingsAfterDrag = viewModel.ScaleSettings[1];
		settingsAfterDrag.ManualMax.Should().BeLessThan(maxBefore, "the drag must have shifted the pen");

		pressedPointer().Capture(null);

		viewModel.IsDragging.Should().BeFalse("losing capture must end the drag as a release would");
		plotControl.Cursor.Should().BeSameAs(handCursor, "losing capture restores the hand cursor");

		window.MouseMove(ToWindow(plotControl, window, hoverTo), RawInputModifiers.Control);

		viewModel.ScaleSettings[1].Should().Be(settingsAfterDrag, "no drag may remain in progress after capture loss");
		viewModel.CursorTime.Should().NotBeNull(
			"the move still reaches the view, so the unchanged pen is not an unrouted event");
	}

	[AvaloniaFact]
	public void ACtrlDragOverSeveralMoves_ShiftsThePenByTheTotalVerticalTravel()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var releaseAt = new Point(pressAt.X, pressAt.Y - DragDistancePixels);
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var step = ExpectedPenShift(viewModel, DragDistancePixels);

		double[] travels = [DragDistancePixels / 3.0, DragDistancePixels * 2.0 / 3.0, DragDistancePixels];

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left, RawInputModifiers.Control);
		foreach (var travelled in travels)
		{
			window.MouseMove(
				ToWindow(plotControl, window, new Point(pressAt.X, pressAt.Y - travelled)),
				RawInputModifiers.Control);
		}

		window.MouseUp(ToWindow(plotControl, window, releaseAt), MouseButton.Left, RawInputModifiers.Control);

		var settings = viewModel.ScaleSettings[1];
		settings.ManualMin.Should().BeApproximately(minBefore - step, LimitTolerance, "each move adds only its part");
		settings.ManualMax.Should().BeApproximately(maxBefore - step, LimitTolerance);
	}

	[AvaloniaFact]
	public void ACtrlDragMovingWithinTheSlopFirst_KeepsEveryPixelOfTheDrag()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X, pressAt.Y - DragDistancePixels);
		var (minBefore, maxBefore) = viewModel.ScaleRangeForPen(1)!.Value;
		var step = ExpectedPenShift(viewModel, DragDistancePixels);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left, RawInputModifiers.Control);
		window.MouseMove(
			ToWindow(plotControl, window, new Point(pressAt.X, pressAt.Y - SubSlopPixels)),
			RawInputModifiers.Control);
		window.MouseMove(ToWindow(plotControl, window, moveTo), RawInputModifiers.Control);
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left, RawInputModifiers.Control);

		var settings = viewModel.ScaleSettings[1];
		settings.ManualMin.Should().BeApproximately(minBefore - step, LimitTolerance);
		settings.ManualMax.Should().BeApproximately(maxBefore - step, LimitTolerance);
	}

	[AvaloniaFact]
	public void ReleasingCtrlMidDrag_KeepsShiftingThePen()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y - DragDistancePixels);
		var (minBefore, _) = viewModel.ScaleRangeForPen(1)!.Value;
		var step = ExpectedPenShift(viewModel, DragDistancePixels);
		var fromBefore = viewModel.Navigation.From;

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left, RawInputModifiers.Control);
		window.MouseMove(ToWindow(plotControl, window, moveTo));
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left);

		viewModel.ScaleSettings[1].ManualMin.Should().BeApproximately(
			minBefore - step, LimitTolerance, "the press fixes the drag's action");
		viewModel.Navigation.From.Should().Be(fromBefore);
	}

	[AvaloniaFact]
	public void PressingCtrlMidPan_KeepsPanning()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = DataAreaCenter(viewModel);
		var moveTo = new Point(pressAt.X - DragDistancePixels, pressAt.Y - DragDistancePixels);
		var firstPenBefore = viewModel.ScaleSettings[1];
		var fromBefore = viewModel.Navigation.From;
		var expectedShift = ExpectedDragShift(viewModel);

		window.MouseDown(ToWindow(plotControl, window, pressAt), MouseButton.Left);
		window.MouseMove(ToWindow(plotControl, window, moveTo), RawInputModifiers.Control);
		window.MouseUp(ToWindow(plotControl, window, moveTo), MouseButton.Left, RawInputModifiers.Control);

		viewModel.Navigation.From.Should().BeCloseTo(
			fromBefore + expectedShift, _dragTolerance, "the press fixes the drag's action");
		viewModel.ScaleSettings[1].Should().Be(firstPenBefore);
	}

	[AvaloniaFact]
	public void ACtrlPress_ShowsItsOwnCursor_AndTheReleaseRestoresTheHand()
	{
		using var viewModel = CreateViewModelWithFixedScales();
		using var shown = ShowChart(viewModel);
		var (window, plotControl) = shown;
		var pressAt = ToWindow(plotControl, window, DataAreaCenter(viewModel));
		var handCursor = plotControl.Cursor;

		window.MouseDown(pressAt, MouseButton.Left, RawInputModifiers.Control);
		var penShiftCursor = plotControl.Cursor;
		window.MouseUp(pressAt, MouseButton.Left, RawInputModifiers.Control);
		var cursorAfterRelease = plotControl.Cursor;
		window.MouseDown(pressAt, MouseButton.Left);
		var panCursor = plotControl.Cursor;
		window.MouseUp(pressAt, MouseButton.Left);

		penShiftCursor.Should().NotBeSameAs(handCursor);
		penShiftCursor.Should().NotBeSameAs(panCursor, "the pen shift and the pan show different cursors");
		cursorAfterRelease.Should().BeSameAs(handCursor);
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

	private static double ActivePenSpan(TrendChartViewModel viewModel)
	{
		var (min, max) = viewModel.ScaleRangeForPen(viewModel.ActivePenId)!.Value;

		return max - min;
	}

	private static double ExpectedPenShift(TrendChartViewModel viewModel, double verticalPixels)
	{
		var dataAreaHeight = viewModel.Plot.RenderManager.LastRender.Layout.DataRect.Height;

		return ActivePenSpan(viewModel) * verticalPixels / dataAreaHeight;
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
