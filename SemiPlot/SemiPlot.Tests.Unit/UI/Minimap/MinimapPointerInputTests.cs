using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Trends;

using Xunit;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.Unit.UI.Minimap;

// The minimap's half of the input guard: MinimapViewModelTests drive NavigateToFraction directly, so hit testing,
// capture, the drag flag and pixel-to-fraction on the way to the chart's window are exercised only here.
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapPointerInputTests
{
	private const double PressFraction = 0.30;
	private const double DragFraction = 0.60;
	private const double HoverFraction = 0.80;
	private const double MinimumMarkerWidth = 6.0;
	private static readonly TimeSpan _timeTolerance = TimeSpan.FromSeconds(1.0);

	[AvaloniaFact]
	public async Task PressThenDrag_MovesTheChartWindowToEachPointerFraction()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false);
		var navigation = stand.Model.Navigation;
		var window = stand.Window;
		var pressAt = stand.StripPointAt(PressFraction);
		var dragTo = stand.StripPointAt(DragFraction);
		var widthBefore = navigation.To - navigation.From;

		window.MouseDown(stand.InWindow(pressAt), MouseButton.Left);

		stand.Model.WindowCenter.Should().BeCloseTo(
			ExpectedCenter(stand, pressAt),
			_timeTolerance,
			"the press must reach the strip and recenter the chart there");

		stand.MovePointer(dragTo, RawInputModifiers.LeftMouseButton);
		window.MouseUp(stand.InWindow(dragTo), MouseButton.Left);

		stand.Model.WindowCenter.Should().BeCloseTo(
			ExpectedCenter(stand, dragTo),
			_timeTolerance,
			"a move while the drag holds must keep recentering the chart");
		(navigation.To - navigation.From).Should().Be(
			widthBefore, "dragging the minimap moves the window without resizing it");
	}

	[AvaloniaFact]
	public async Task MoveAfterRelease_LeavesTheChartWindowWhereTheDragEndedIt()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false);
		var navigation = stand.Model.Navigation;
		var window = stand.Window;
		var pressAt = stand.StripPointAt(PressFraction);
		var dragTo = stand.StripPointAt(DragFraction);
		var hoverTo = stand.StripPointAt(HoverFraction);

		window.MouseDown(stand.InWindow(pressAt), MouseButton.Left);
		stand.MovePointer(dragTo, RawInputModifiers.LeftMouseButton);
		window.MouseUp(stand.InWindow(dragTo), MouseButton.Left);
		var fromAfterRelease = navigation.From;

		stand.MovePointer(hoverTo);

		stand.Model.ViewModel.HoverFraction.Should().Be(
			hoverTo.X / stand.StripCanvas.Bounds.Width,
			"a layer that stopped delivering moves would pass the check below without routing anything");
		navigation.From.Should().Be(
			fromAfterRelease, "the release ends the drag, so a later move is a hover and navigates nothing");
		ExpectedCenter(stand, hoverTo).Should().NotBeCloseTo(
			stand.Model.WindowCenter,
			_timeTolerance,
			"the hover position must differ from the drag's, or the assertion above proves nothing");
	}

	[AvaloniaFact]
	public async Task AWindowPastTheExtent_KeepsItsMarkerInsideTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false);
		var navigation = stand.Model.Navigation;
		var highlight = stand.Named<Border>("WindowHighlight");

		navigation.OnLiveEdge(MinimapStand.ExtentLast + TimeSpan.FromDays(1.0));
		Dispatcher.UIThread.RunJobs();

		navigation.From.Should().BeAfter(
			MinimapStand.ExtentLast, "the sticky window must have followed the live edge past the extent");
		highlight.Width.Should().Be(
			MinimumMarkerWidth, "only a floored marker at the right edge reaches past the strip unclamped");
		(Canvas.GetLeft(highlight) + highlight.Width).Should().BeLessThanOrEqualTo(
			stand.StripCanvas.Bounds.Width, "the marker's right edge must stay inside the strip");
	}

	// Mirrors MinimapView.NavigateToPointer and MinimapViewModel.NavigateToFraction.
	private static DateTime ExpectedCenter(MinimapViewStand stand, Point stripPoint)
	{
		return MinimapGeometry.TimeAtFraction(
			MinimapStand.ExtentFirst, MinimapStand.ExtentLast, stripPoint.X / stand.StripCanvas.Bounds.Width);
	}
}
