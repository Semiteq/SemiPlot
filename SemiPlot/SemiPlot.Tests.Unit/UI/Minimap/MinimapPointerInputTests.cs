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

// The minimap's half of the input guard: MinimapPagerTests drive NavigateToPageFraction directly, so hit testing,
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
	private static readonly DateOnly _thursday = new(2025, 12, 25);
	private static readonly DateOnly _monday = new(2025, 12, 29);
	private static readonly DateOnly _tuesday = new(2025, 12, 30);
	private static readonly DateOnly _wednesday = new(2025, 12, 31);

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

		stand.AreaInStrip(stand.Named<Border>("HoverLine")).Left.Should().Be(
			hoverTo.X, "a layer that stopped delivering moves would pass the check below without routing anything");
		navigation.From.Should().Be(
			fromAfterRelease, "the release ends the drag, so a later move is a hover and navigates nothing");
		ExpectedCenter(stand, hoverTo).Should().NotBeCloseTo(
			stand.Model.WindowCenter,
			_timeTolerance,
			"the hover position must differ from the drag's, or the assertion above proves nothing");
	}

	[AvaloniaFact]
	public async Task AWindowPastTheExtent_KeepsItsBoxInsideTheStripOfItsOwnPage()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false);
		var navigation = stand.Model.Navigation;
		var highlight = stand.Named<Border>("WindowHighlight");

		var advanced = MinimapStand.ExtentLast + TimeSpan.FromDays(1.0);
		navigation.OnLiveEdge(advanced, advanced);
		Dispatcher.UIThread.RunJobs();

		navigation.From.Should().BeAfter(
			MinimapStand.ExtentLast, "the sticky window must have followed the live edge past the extent");
		stand.Model.ViewModel.Pager.Page.FromUtc.Should().BeOnOrAfter(
			MinimapStand.ExtentLast, "the page follows the window past the extent");
		highlight.IsVisible.Should().BeTrue();
		highlight.Width.Should().BeGreaterThanOrEqualTo(MinimumMarkerWidth);
		(Canvas.GetLeft(highlight) + highlight.Width).Should().BeLessThanOrEqualTo(
			stand.StripCanvas.Bounds.Width, "the box's right edge must stay inside the strip");
	}

	[AvaloniaTheory]
	[InlineData(27, 26)]
	[InlineData(28, 29)]
	public async Task AClickOnAWeekendCell_LandsOnTheNearerWorkingDaysData(
		int clickedDayOfDecember,
		int landingDayOfDecember)
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false, MinimapStand.WorkingDays);
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		var clickedDay = new DateOnly(2025, 12, clickedDayOfDecember);
		var landingDay = new DateOnly(2025, 12, landingDayOfDecember);

		HeadlessInput.Click(stand.Window, stand.CalendarCellRow, stand.CellPointAt(clickedDay));

		(navigation.From, navigation.To).Should().Be(
			(MinimapStand.At(landingDay, 10), MinimapStand.At(landingDay, 11)),
			"Saturday's noon lies nearer Friday's last sample and Sunday's nearer Monday's first, clock offset kept");
		stand.Model.ViewModel.Pager.Page.FirstDay.Should().Be(landingDay);
	}

	[AvaloniaFact]
	public async Task AClickOnACell_UnderAWeekWideWindow_ShowsTheWeekHoldingThatDay()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false, MinimapStand.WorkingDays);
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(_tuesday, 0), MinimapStand.At(_tuesday, 0).AddHours(36.0));
		Dispatcher.UIThread.RunJobs();
		stand.Model.ViewModel.Pager.Page.Unit.Should().Be(MinimapPageUnit.Week);

		HeadlessInput.Click(stand.Window, stand.CalendarCellRow, stand.CellPointAt(_thursday));

		navigation.From.Should().Be(MinimapStand.At(_thursday, 0), "the click lands on the day, clock offset kept");
		var page = stand.Model.ViewModel.Pager.Page;
		(page.Unit, page.FirstDay).Should().Be((MinimapPageUnit.Week, new DateOnly(2025, 12, 22)));
	}

	[AvaloniaFact]
	public async Task AClickBeforeTheFirstCell_MovesNothing()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false, MinimapStand.WorkingDays);
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		var row = stand.CalendarCellRow.Bounds;

		stand.ShownCalendar.CellLeft(0).Should().BeGreaterThan(1.0, "the cells hang from the row's right end");

		HeadlessInput.Click(stand.Window, stand.CalendarCellRow, new Point(1.0, row.Height / 2.0));

		navigation.From.Should().Be(MinimapStand.At(_tuesday, 10), "the row before the first cell holds no day");
	}

	[AvaloniaFact]
	public async Task ADragAlongTheCells_LandsOnEachDayUnderThePointer_AndAMoveAfterReleaseOnNone()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: false, MinimapStand.WorkingDays);
		var navigation = stand.Model.Navigation;
		var window = stand.Window;
		navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		var pressAt = stand.CellPointAt(_thursday);
		var dragTo = stand.CellPointAt(_monday);

		window.MouseDown(stand.CalendarInWindow(pressAt), MouseButton.Left);

		navigation.From.Should().Be(MinimapStand.At(_thursday, 10), "the press lands on the day under it");

		stand.MoveCalendarPointer(dragTo, RawInputModifiers.LeftMouseButton);
		window.MouseUp(stand.CalendarInWindow(dragTo), MouseButton.Left);

		navigation.From.Should().Be(MinimapStand.At(_monday, 10), "a move while the drag holds lands again");

		stand.MoveCalendarPointer(stand.CellPointAt(_wednesday));

		navigation.From.Should().Be(MinimapStand.At(_monday, 10), "the release ends the drag, so a move only hovers");
	}

	// Mirrors MinimapView.NavigateToStripX and MinimapPager.NavigateToPageFraction away from the page's start.
	private static DateTime ExpectedCenter(MinimapViewStand stand, Point stripPoint)
	{
		var page = stand.Model.ViewModel.Pager.Page;

		return MinimapGeometry.TimeAtFraction(page.FromUtc, page.ToUtc, stripPoint.X / stand.StripCanvas.Bounds.Width);
	}
}
