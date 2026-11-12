using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Minimap;

using Xunit;

using Point = Avalonia.Point;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapHoverTests
{
	private const double PressFraction = 0.30;
	private const double DragFraction = 0.60;
	private const double HoverLineWidth = 1.0;
	private const double PixelTolerance = 1.0;
	private const double OffStripDistance = 6.0;

	[AvaloniaFact]
	public async Task HoveringTheStrip_ShowsALineAndItsTimeAndLeavingHidesThem()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, hoverTime) = HoverControls(stand);
		var pointer = stand.StripPointAt(DragFraction);

		stand.MovePointer(pointer);

		hoverLine.IsVisible.Should().BeTrue("a pointer over the strip shows its line");
		var line = stand.AreaInStrip(hoverLine);
		line.Left.Should().BeApproximately(pointer.X, PixelTolerance, "the line sits under the pointer");
		line.Width.Should().Be(HoverLineWidth);
		line.Height.Should().Be(strip.Bounds.Height, "the line crosses the whole strip");
		hoverTime.IsVisible.Should().BeTrue("a pointer over the strip shows its time");
		hoverTime.Text.Should().Be(PageTimeLabel(stand, pointer.X / strip.Bounds.Width));
		var time = stand.AreaInStrip(hoverTime);
		time.Top.Should().BeGreaterThanOrEqualTo(strip.Bounds.Height, "the time sits in the row under the strip");
		time.Center.X.Should().BeApproximately(pointer.X, PixelTolerance, "the time is centred under the line");

		stand.MovePointer(new Point(pointer.X, strip.Bounds.Height + OffStripDistance));

		hoverLine.IsVisible.Should().BeFalse("leaving the strip hides the line");
		hoverTime.IsVisible.Should().BeFalse("leaving the strip hides the time");
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public async Task AHoverAtAnEnd_KeepsItsTimeInTheRowAndHidesOnlyTheTickLabelsItCovers(bool atLeftEnd)
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (_, hoverTime) = HoverControls(stand);
		var pointerX = atLeftEnd ? 0.0 : strip.Bounds.Width - 1.0;
		var row = stand.AreaInView(stand.Named<Canvas>("PageTickRow"));
		var ticks = stand.Named<Canvas>("TickLabels").Children;
		ticks.Should().HaveCountGreaterThan(2);

		stand.MovePointer(new Point(pointerX, strip.Bounds.Height / 2.0));

		var time = stand.AreaInView(hoverTime);
		time.Left.Should().BeGreaterThanOrEqualTo(row.Left, "the time never leaves the row");
		time.Right.Should().BeLessThanOrEqualTo(row.Right, "the time never leaves the row");
		foreach (var tick in ticks)
		{
			IsShown(tick).Should().Be(
				!time.Intersects(stand.AreaInView(tick)), "the time hides the tick labels it covers and only those");
		}

		ticks.Count(tick => !IsShown(tick)).Should().BePositive("a time at an end covers the tick label there");

		stand.MovePointer(new Point(pointerX, strip.Bounds.Height + OffStripDistance));

		ticks.Should().OnlyContain(tick => IsShown(tick), "leaving the strip shows every tick label again");
	}

	[AvaloniaFact]
	public async Task AHoverAtTheRightEdge_KeepsTheLineInsideTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, _) = HoverControls(stand);

		stand.MovePointer(new Point(strip.Bounds.Width - (HoverLineWidth / 2.0), strip.Bounds.Height / 2.0));

		hoverLine.IsVisible.Should().BeTrue();
		stand.AreaInView(hoverLine).Right.Should().BeLessThanOrEqualTo(
			stand.AreaInView(strip).Right, "the line stops at the strip's right edge");
	}

	[AvaloniaFact]
	public async Task AResizeDuringAHover_KeepsTheLineAtItsFraction()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var strip = stand.StripCanvas;
		var (hoverLine, hoverTime) = HoverControls(stand);
		var pointer = stand.StripPointAt(DragFraction);
		stand.MovePointer(pointer);
		var widthBefore = strip.Bounds.Width;
		var fraction = pointer.X / widthBefore;

		stand.Window.Width = 600;
		Dispatcher.UIThread.RunJobs();

		strip.Bounds.Width.Should().Be(widthBefore - 300, "the strip follows the window");
		var lineX = fraction * strip.Bounds.Width;
		stand.AreaInStrip(hoverLine).Left.Should()
			.BeApproximately(lineX, PixelTolerance, "the line keeps its fraction");
		stand.AreaInStrip(hoverTime).Center.X.Should().BeApproximately(
			lineX, PixelTolerance, "the time stays centred under the line");
	}

	[AvaloniaFact]
	public async Task APageTurnDuringAHover_RelabelsItsTime()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var (_, hoverTime) = HoverControls(stand);
		var pointer = stand.StripPointAt(DragFraction);
		stand.MovePointer(pointer);
		var timeBefore = hoverTime.Text;

		var page = stand.Model.ViewModel.Pager.Page;
		stand.Model.Navigation.SetWindow(page.ToUtc.AddDays(-1.5), page.ToUtc);
		Dispatcher.UIThread.RunJobs();

		stand.Model.ViewModel.Pager.Page.Unit.Should().NotBe(MinimapPageUnit.Day);
		hoverTime.Text.Should().NotBe(timeBefore, "the same pixel names another time on a wider page")
			.And.Be(PageTimeLabel(stand, pointer.X / stand.StripCanvas.Bounds.Width));
	}

	[AvaloniaFact]
	public async Task ADrag_KeepsTheLineUnderThePointerUntilItLeavesTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var (strip, window) = (stand.StripCanvas, stand.Window);
		var (hoverLine, _) = HoverControls(stand);
		var dragTo = stand.StripPointAt(DragFraction);

		window.MouseDown(stand.InWindow(stand.StripPointAt(PressFraction)), MouseButton.Left);
		stand.MovePointer(dragTo, RawInputModifiers.LeftMouseButton);

		hoverLine.IsVisible.Should().BeTrue("the exit the capture raises does not end the hover");
		stand.AreaInStrip(hoverLine).Left.Should().BeApproximately(
			dragTo.X, PixelTolerance, "the line follows the drag");

		var offStrip = new Point(dragTo.X, strip.Bounds.Height + OffStripDistance);
		stand.MovePointer(offStrip, RawInputModifiers.LeftMouseButton);

		hoverLine.IsVisible.Should().BeFalse("a captured drag that leaves the strip hides the line");

		window.MouseUp(stand.InWindow(dragTo), MouseButton.Left);
	}

	[AvaloniaFact]
	public async Task HoveringACell_RingsIt_AndNamesItsDayAndRowsInTheTooltip()
	{
		using var culture = new CultureScope("en-US");
		using var stand = await ShowOnTuesdayAsync();
		var monday = new DateOnly(2025, 12, 29);
		var calendar = stand.ShownCalendar;

		stand.MoveCalendarPointer(stand.CellPointAt(monday));

		stand.Calendar.HoveredCell.Should().Be(calendar.Cell(calendar.IndexOf(monday)));
		ToolTip.GetTip(stand.CalendarCellRow).Should().Be("Mon 29 Dec 2025 \u00b7 rows 07:00-17:00");
		stand.Model.ViewModel.Pager.Page.FirstDay.Should().Be(new DateOnly(2025, 12, 30), "a hover moves nothing");
	}

	[AvaloniaFact]
	public async Task APageChange_KeepsTheRowStill_AndTheRingAndTheTooltipOnTheCellUnderThePointer()
	{
		using var culture = new CultureScope("en-US");
		using var stand = await ShowOnTuesdayAsync();
		stand.MoveCalendarPointer(stand.CellPointAt(new DateOnly(2025, 12, 29)));
		var rowLeft = stand.AreaInView(stand.CalendarCellRow).Left;
		var ring = stand.Calendar.HoveredCell;
		ring.Should().NotBeNull("the pointer rings Monday");

		stand.Model.Navigation.SetWindow(
			MinimapStand.At(new DateOnly(2025, 12, 29), 10), MinimapStand.At(new DateOnly(2025, 12, 31), 10));
		Dispatcher.UIThread.RunJobs();

		stand.Model.ViewModel.Pager.Page.Unit.Should().Be(MinimapPageUnit.Week);
		stand.AreaInView(stand.CalendarCellRow).Left.Should().Be(rowLeft, "the row is the strip's column");
		stand.Calendar.HoveredCell.Should().Be(ring, "the pointer did not move");
		ToolTip.GetTip(stand.CalendarCellRow).Should().Be("Mon 29 Dec 2025 \u00b7 rows 07:00-17:00");
	}

	[AvaloniaTheory]
	[InlineData("en-US", 2, "Mon 29 Dec 2025 \u00b7 rows 07:00-17:00 \u00b7 2 markers")]
	[InlineData(
		"ru-RU",
		2,
		"\u043f\u043d 29 \u0434\u0435\u043a. 2025 \u00b7 \u0434\u0430\u043d\u043d\u044b\u0435 07:00-17:00"
		+ " \u00b7 2 \u043c\u0435\u0442\u043a\u0438")]
	[InlineData("en-US", 1, "Mon 29 Dec 2025 \u00b7 rows 07:00-17:00 \u00b7 1 marker")]
	public async Task HoveringADayWithOperatorMarkers_CountsThemInTheTooltip(
		string cultureName,
		int markerCount,
		string expected)
	{
		using var culture = new CultureScope(cultureName);
		using var stand = await ShowOnTuesdayAsync();
		var monday = new DateOnly(2025, 12, 29);
		stand.Model.MarkerChannel.Push(
			[
				.. Enumerable.Range(1, markerCount)
					.Select(index => new StoredMarker(index, MinimapStand.At(monday, 8 + index), "#" + index)),
				new StoredMarker(10, MinimapStand.At(monday, 9), "start", MarkerKind.RecipeStart, IsLocked: true)
			]);
		Dispatcher.UIThread.RunJobs();

		stand.MoveCalendarPointer(stand.CellPointAt(monday));

		ToolTip.GetTip(stand.CalendarCellRow).Should().Be(expected, "recipe markers are no operator markers");
	}

	[AvaloniaFact]
	public async Task HoveringADayWithNoRows_RingsItsEmptyCell_AndSaysItHoldsNoRows()
	{
		using var culture = new CultureScope("en-US");
		using var stand = await ShowOnTuesdayAsync();
		var saturday = new DateOnly(2025, 12, 27);
		var calendar = stand.ShownCalendar;

		stand.MoveCalendarPointer(stand.CellPointAt(saturday));

		stand.Calendar.HoveredCell.Should().Be(calendar.Cell(calendar.IndexOf(saturday)));
		ToolTip.GetTip(stand.CalendarCellRow).Should().Be("Sat 27 Dec 2025 \u00b7 no rows");
	}

	[AvaloniaFact]
	public async Task LeavingTheCellRow_OrPassingTheFirstCell_ClearsTheRingAndTheTooltip()
	{
		using var stand = await ShowOnTuesdayAsync();
		var row = stand.CalendarCellRow.Bounds;
		var pointer = stand.CellPointAt(new DateOnly(2025, 12, 29));
		stand.MoveCalendarPointer(pointer);
		stand.Calendar.HoveredCell.Should().NotBeNull();

		stand.MoveCalendarPointer(new Point(pointer.X, row.Height + OffStripDistance));

		stand.Calendar.HoveredCell.Should().BeNull("leaving the row hides the ring");
		ToolTip.GetTip(stand.CalendarCellRow).Should().BeNull();

		stand.MoveCalendarPointer(pointer);
		stand.MoveCalendarPointer(new Point(1.0, row.Height / 2.0));

		stand.Calendar.HoveredCell.Should().BeNull("the row before the first cell holds no day");
		ToolTip.GetTip(stand.CalendarCellRow).Should().BeNull();
	}

	private static async Task<MinimapViewStand> ShowOnTuesdayAsync()
	{
		var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		stand.Model.Navigation.SetWindow(
			MinimapStand.At(new DateOnly(2025, 12, 30), 10), MinimapStand.At(new DateOnly(2025, 12, 30), 11));
		stand.LandRead();

		return stand;
	}

	private static (Control Line, TextBlock Time) HoverControls(MinimapViewStand stand)
	{
		return (stand.Named<Border>("HoverLine"), stand.Named<TextBlock>("HoverTimeLabel"));
	}

	private static bool IsShown(Visual visual)
	{
		return visual.IsVisible && visual.Opacity > 0.0;
	}

	/// <summary>The time at a fraction of the page, as the hover names it in the stand's UTC archive zone.</summary>
	private static string PageTimeLabel(MinimapViewStand stand, double fraction)
	{
		var page = stand.Model.ViewModel.Pager.Page;

		return MinimapLabels.HoverTime(MinimapGeometry.TimeAtFraction(page.FromUtc, page.ToUtc, fraction), page.Unit);
	}
}
