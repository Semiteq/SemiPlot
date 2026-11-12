using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Minimap;

using Xunit;

using Path = Avalonia.Controls.Shapes.Path;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapViewTests
{
	private const double RowInset = 8.0;
	private const double StepWidth = 24.0;
	private const double StripHeight = 36.0;
	private const double CalendarRowHeight = 16.0;
	private const double CalendarGap = 8.0;
	private const double ChevronGap = 8.0;

	private static readonly DateOnly _friday = new(2025, 12, 26);
	private static readonly DateOnly _saturday = new(2025, 12, 27);
	private static readonly DateOnly _monday = new(2025, 12, 29);
	private static readonly DateOnly _tuesday = new(2025, 12, 30);

	[AvaloniaFact]
	public async Task TheStrip_HoldsOneBandBelowTheMarker()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var children = stand.StripCanvas.Children;

		var bandLayer = children.OfType<MinimapBand>().Should().ContainSingle().Subject;

		children.IndexOf(bandLayer).Should().BeLessThan(children.IndexOf(stand.Named<Border>("WindowHighlight")));
		bandLayer.IsHitTestVisible.Should().BeFalse("the strip under the band takes the pointer");
	}

	[AvaloniaFact]
	public async Task TheStrip_DrawsNoBaseline_AndLabelsThePagesHoursUnderIt()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		var view = stand.View;
		var strip = stand.AreaInView(stand.StripCanvas);
		var labels = stand.Named<Canvas>("TickLabels").Children.OfType<TextBlock>().ToList();

		stand.StripCanvas.Children.Select(child => child.Name).Should().Equal(
			["BandLayer", "RunShading", "WindowHighlight", "TimeMarkerTicks", "HoverLine"],
			"the band, the runs, the box and the ticks, the hover line over all, and no line of its own");
		labels.Should().NotBeEmpty();
		labels[0].Text.Should().Be("07:00", "the Day page starts at the working hours' first hour");
		foreach (var label in labels.Select(stand.AreaInView))
		{
			label.Top.Should().BeGreaterThanOrEqualTo(strip.Bottom, "the labels sit under the strip, not on it");
			label.Bottom.Should().BeLessThanOrEqualTo(view.Bounds.Height, "the view must show the whole label");
			label.Right.Should().BeLessThanOrEqualTo(strip.Right, "no label runs past the strip");
		}

		stand.AreaInView(labels[0]).Left.Should().Be(strip.Left, "the first hour sits at the page's left edge");
	}

	[AvaloniaFact]
	public async Task TheView_KeepsItsHeightWhenTheExtentFillsItsLabels()
	{
		using var stand = MinimapViewStand.ShowWithoutExtent();
		var ticks = stand.Named<Canvas>("TickLabels");
		ticks.Children.Should().BeEmpty("no extent is read yet");
		var emptyHeight = stand.View.Bounds.Height;

		await stand.Model.LoadExtentAsync();
		Dispatcher.UIThread.RunJobs();

		ticks.Children.Should().NotBeEmpty("the extent landed");
		stand.Named<Canvas>("CalendarLabels").Children.Should().NotBeEmpty();
		stand.View.Bounds.Height.Should().Be(emptyHeight, "the minimap row never jumps when the labels fill");
	}

	[AvaloniaFact]
	public async Task TheBand_CoversTheStrip()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);

		stand.BandLayer.Bounds.Size.Should().Be(stand.StripCanvas.Bounds.Size);
	}

	[AvaloniaFact]
	public async Task APublishedBand_GivesTheControlFiguresInThePenColour()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var model = stand.Model;
		var page = model.ViewModel.Pager.Page;
		model.Band.Should().NotBeNull();

		stand.BandLayer.Figures.Should().NotBeEmpty();
		stand.BandLayer.Figures.Should().BeEquivalentTo(
			MinimapGeometry.BandFigures(
				model.Band!,
				page.FromUtc,
				page.ToUtc,
				stand.StripCanvas.Bounds.Width,
				stand.StripCanvas.Bounds.Height),
			options => options.WithStrictOrdering());
		stand.BandLayer.BandColor.Should().BeOfType<SolidColorBrush>()
			.Which.Color.Should().Be(Color.Parse(model.ViewModel.BandFeed.BandColor!));
	}

	[AvaloniaFact]
	public async Task TheRealisedStrip_ReadsTheBandOverThePageAtItsOwnWidth()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var page = stand.Model.ViewModel.Pager.Page;
		var stripWidth = stand.StripCanvas.Bounds.Width;

		var read = stand.Model.Provider.BandHistoryQueries[^1];

		read.TargetColumnCount.Should().Be(MinimapBandFeed.ColumnsForWidth(stripWidth));
		read.TargetColumnCount.Should().BeGreaterThan(
			MinimapBandFeed.ColumnStep, "a {0} px strip is wider than the floor", stripWidth);
		(read.FromUtc, read.ToUtc).Should().Be((page.FromUtc, page.ToUtc));
	}

	[AvaloniaFact]
	public async Task APan_LeavesTheBandFiguresAsTheyWere()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var highlight = stand.Named<Border>("WindowHighlight");
		var figures = stand.BandLayer.Figures;
		var leftBefore = Canvas.GetLeft(highlight);

		stand.Model.Navigation.PanBy(TimeSpan.FromMinutes(-10.0));

		Canvas.GetLeft(highlight).Should().NotBe(leftBefore, "the pan inside the page must move the box");
		stand.BandLayer.Figures.Should().BeSameAs(figures, "only the page, the band and the size reshape the band");
	}

	[AvaloniaFact]
	public async Task HidingTheDrawnPen_TakesTheFiguresAway()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var chart = stand.Model.Chart;
		stand.BandLayer.Figures.Should().NotBeEmpty();

		chart.SetPenVisibility(1, isVisible: false);
		stand.LandRead();
		chart.SetPenVisibility(2, isVisible: false);
		stand.LandRead();

		stand.Model.Band.Should().BeNull();
		stand.BandLayer.Figures.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task TheStrip_DrawsOneFullHeightTickPerOperatorMarkerOnThePage()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true);
		var page = stand.Model.ViewModel.Pager.Page;
		var quarter = page.FromUtc + ((page.ToUtc - page.FromUtc) / 4.0);

		stand.Model.MarkerChannel.Push(
			new StoredMarker(1, quarter, "1"),
			new StoredMarker(2, quarter.AddHours(1.0), "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(3, page.ToUtc.AddHours(1.0), "3"));
		Dispatcher.UIThread.RunJobs();

		var ticks = stand.Named<Path>("TimeMarkerTicks");
		var tick = ticks.Data.Should().BeOfType<GeometryGroup>().Subject.Children
			.Should().ContainSingle("a recipe marker shades its run instead, and a marker off the page has no place")
			.Which.Should().BeOfType<LineGeometry>().Subject;
		var strip = stand.StripCanvas.Bounds;
		tick.StartPoint.Should().Be(new Point(0.25 * strip.Width, 0.0));
		tick.EndPoint.Should().Be(new Point(0.25 * strip.Width, strip.Height));
		ticks.IsHitTestVisible.Should().BeFalse("a press on a tick navigates as a press on the strip does");
	}

	[AvaloniaFact]
	public async Task ANextPageClick_MovesTheChartAWorkingDay()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(_friday, 10), MinimapStand.At(_friday, 11));
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Click(stand.Window, stand.Named<Button>("NextPageButton"));

		(navigation.From, navigation.To).Should().Be(
			(MinimapStand.At(_monday, 10), MinimapStand.At(_monday, 11)),
			"the weekend holds no rows, so the step lands on Monday at the same clock time");

		HeadlessInput.Click(stand.Window, stand.Named<Button>("PreviousPageButton"));

		navigation.From.Should().Be(MinimapStand.At(_friday, 10), "the step back skips the weekend again");
	}

	[AvaloniaFact]
	public async Task FiveNextPageClicksFromAMonday_ReachTheNextMonday_EachOnADayWithRows()
	{
		var firstMonday = new DateOnly(2025, 12, 15);
		var workingDays = MinimapStand.WorkingDaysBetween(firstMonday, firstMonday.AddDays(16));
		using var stand = await MinimapViewStand.ShowAsync(
			showPens: true, workingDays, (MinimapStand.At(firstMonday, 0), MinimapStand.ExtentLast));
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(firstMonday, 10), MinimapStand.At(firstMonday, 11));
		Dispatcher.UIThread.RunJobs();
		var landedDays = new List<DateOnly>();

		for (var click = 0; click < 5; click++)
		{
			HeadlessInput.Click(stand.Window, stand.Named<Button>("NextPageButton"));
			landedDays.Add(DateOnly.FromDateTime(navigation.From));
			navigation.From.TimeOfDay.Should().Be(TimeSpan.FromHours(10.0), "every step keeps the clock time");
		}

		landedDays.Should().Equal(
			firstMonday.AddDays(1), firstMonday.AddDays(2), firstMonday.AddDays(3), firstMonday.AddDays(4),
			firstMonday.AddDays(7));
		landedDays.Should().OnlyContain(day => workingDays.Any(span => span.Day == day), "no step shows an empty day");
	}

	[AvaloniaFact]
	public async Task TheStrip_AndTheDayCalendarUnderIt_StackInANinetySevenPixelRow()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var view = stand.View.Bounds;
		var strip = stand.AreaInView(stand.StripCanvas);
		var ticks = stand.AreaInView(stand.Named<Canvas>("PageTickRow"));
		var cellRow = stand.AreaInView(stand.CalendarCellRow);
		var labels = stand.AreaInView(stand.Named<Canvas>("CalendarLabels"));
		var calendar = stand.AreaInView(stand.Calendar);

		view.Height.Should().Be(97.0, "border 1, inset 4, strip 36, ticks 14, gap 8, cells 16, labels 14, inset 4");
		strip.Top.Should().Be(5.0, "the strip opens the panel, directly under the chart");
		(ticks.Top, ticks.Height).Should().Be((strip.Bottom, 14.0));
		(cellRow.Top, cellRow.Height).Should().Be(
			(ticks.Bottom + CalendarGap, CalendarRowHeight), "8 px stand between the strip's ticks and the cells");
		(cellRow.Left, cellRow.Right).Should().Be((strip.Left, strip.Right), "the calendar spans the strip's width");
		(labels.Top, labels.Height).Should().Be((cellRow.Bottom, 14.0), "the day labels sit under the cells");
		(labels.Left, labels.Width).Should().Be((cellRow.Left, cellRow.Width));
		calendar.Should().Be(new Rect(cellRow.Left, cellRow.Top, cellRow.Width, 30.0), "a separator crosses both rows");
		labels.Bottom.Should().Be(view.Height - 4.0, "the label row closes the panel at its inset");
		stand.Calendar.IsHitTestVisible.Should().BeFalse("the cell row under the drawing takes the pointer");
		stand.CalendarCellRow.Cursor.Should().BeNull("the calendar keeps the arrow, so it reads as no button");
	}

	[AvaloniaFact]
	public async Task TheLastDaysCell_EndsAtTheStripsRightEdge_AndItsOutlineIsNotClipped()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var calendar = stand.ShownCalendar;
		var lastCell = calendar.Cell(calendar.DayCount - 1);

		(stand.AreaInView(stand.CalendarCellRow).Left + lastCell.Left + lastCell.Width).Should().Be(
			stand.AreaInView(stand.StripCanvas).Right, "the last archive day ends where the strip ends");
		var trackGrid = stand.Named<Grid>("TrackGrid");
		stand.Calendar.GetSelfAndVisualAncestors()
			.TakeWhile(visual => visual != trackGrid)
			.Where(visual => visual.ClipToBounds)
			.Select(visual => visual.GetType().Name)
			.Should().BeEmpty("the outline around the last day runs 2 px past the row, inside the grid");
	}

	[AvaloniaFact]
	public async Task TheLabelRows_TakeTenPixelSecondaryText()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var secondary = stand.View.FindResource(stand.View.ActualThemeVariant, "AppSecondaryForegroundBrush");
		TextBlock[] labels =
		[
			stand.Named<Canvas>("CalendarLabels").Children.OfType<TextBlock>().First(),
			stand.Named<Canvas>("TickLabels").Children.OfType<TextBlock>().First(),
			stand.Named<TextBlock>("HoverTimeLabel"),
			stand.Named<TextBlock>("HintLabel")
		];

		foreach (var label in labels)
		{
			label.FontSize.Should().Be(10.0, "{0} is a label row's text", label.Name ?? label.Text);
			label.Foreground.Should().BeSameAs(secondary);
		}
	}

	[AvaloniaFact]
	public async Task TheStrip_SpansThePanelBetweenTheTwoChevrons_EightPixelsFromEach_AndItsTicksFollowIt()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var view = stand.View.Bounds;
		var previous = stand.AreaInView(stand.Named<Button>("PreviousPageButton"));
		var next = stand.AreaInView(stand.Named<Button>("NextPageButton"));
		var strip = stand.AreaInView(stand.StripCanvas);
		var ticks = stand.AreaInView(stand.Named<Canvas>("PageTickRow"));

		strip.Height.Should().Be(StripHeight);
		(previous.Left, previous.Width).Should().Be((RowInset, StepWidth), "the earlier step opens the strip's row");
		(next.Right, next.Width).Should().Be((view.Width - RowInset, StepWidth), "the later step closes it");
		(previous.Top, previous.Height).Should().Be((strip.Top, strip.Height), "a step is the strip's height");
		(next.Top, next.Height).Should().Be((strip.Top, strip.Height));
		(strip.Left - previous.Right, next.Left - strip.Right).Should().Be(
			(ChevronGap, ChevronGap), "8 px stand between each chevron and the strip");
		(ticks.Left, ticks.Width).Should().Be((strip.Left, strip.Width), "a tick and the window box share one x");
		ticks.Top.Should().Be(strip.Bottom);
	}

	[AvaloniaFact]
	public async Task TheCalendar_TakesOneCellPerDay_AndFillsTheDaysWithRows_RightAligned()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var calendar = stand.ShownCalendar;
		var hours = stand.Model.ViewModel.Pager.Page.Hours;

		(calendar.FirstDay, calendar.DayCount).Should().Be((new DateOnly(2025, 12, 25), 8), "25 December to 1 January");
		calendar.Pitch.Should().Be(20.0, "eight days leave far more than the 20 px cap a day");
		stand.Calendar.Cells.Should().Equal(calendar.Cells(MinimapStand.WorkingDays, hours, TimeZoneInfo.Utc));
		stand.Calendar.Cells.Should().HaveCount(5, "the weekend and the empty last day draw no cell");
		stand.Calendar.Cells[^1].Left.Should().Be(
			stand.CalendarCellRow.Bounds.Width - calendar.CellWidth - calendar.Pitch,
			"31 December stands one pitch left of the empty 1 January, whose cell ends the row");
		stand.Calendar.Cells.Should().OnlyContain(cell => cell.Coverage == DayCoverage.Full, "07:00-17:00 each day");
	}

	[AvaloniaFact]
	public async Task ATwoDayArchive_DrawsTwoNineteenPixelCells_AtTheRightEndOfTheRow()
	{
		var day = new DateOnly(2025, 12, 30);
		using var stand = await MinimapViewStand.ShowAsync(
			showPens: true,
			MinimapStand.WorkingDaysBetween(day, day.AddDays(1)),
			(MinimapStand.At(day, 0), MinimapStand.At(day.AddDays(1), 12)));
		var rowWidth = stand.CalendarCellRow.Bounds.Width;

		stand.Calendar.Cells.Select(cell => (cell.Left, cell.Width)).Should().Equal(
			[(rowWidth - 39.0, 19.0), (rowWidth - 19.0, 19.0)],
			"two capped cells hang from the row's right end and leave the rest of it empty");
		var labels = stand.Named<Canvas>("CalendarLabels").Children.OfType<TextBlock>().ToList();
		labels.Should().ContainSingle("the first day's label; Wednesday is no Monday");
		(Canvas.GetLeft(labels[0]) + labels[0].Bounds.Width).Should().BeLessThanOrEqualTo(rowWidth);
	}

	[AvaloniaFact]
	public async Task ThePageOutline_RingsThePagesCells_AndFollowsAPageStep()
	{
		using var stand = await ShowOnTuesdayAsync();
		var calendar = stand.ShownCalendar;
		var pager = stand.Model.ViewModel.Pager;

		stand.Calendar.PageOutline.Should().Be(Ring(calendar, _tuesday, _tuesday), "a Day page rings its cell");

		HeadlessInput.Click(stand.Window, stand.Named<Button>("NextPageButton"));

		pager.Page.FirstDay.Should().Be(_tuesday.AddDays(1));
		stand.Calendar.PageOutline.Should().Be(Ring(calendar, _tuesday.AddDays(1), _tuesday.AddDays(1)));

		stand.Model.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_monday.AddDays(2), 10));
		Dispatcher.UIThread.RunJobs();

		calendar = stand.ShownCalendar;
		stand.Calendar.PageOutline.Should().Be(
			Ring(calendar, _monday, calendar.FirstDay.AddDays(calendar.DayCount - 1)),
			"the week runs past the archive's last day, so its outline stops at that day's cell");
	}

	[AvaloniaFact]
	public async Task ALiveSampleOnTheNewestDay_RegradesItsCellOnlyAcrossACoverageStep()
	{
		var newestDay = new DateOnly(2026, 1, 1);
		var newest = MinimapStand.At(newestDay, 7);
		IReadOnlyList<ArchiveDaySpan> spans = [.. MinimapStand.WorkingDays, new ArchiveDaySpan(newestDay, newest, newest)];
		using var stand = await MinimapViewStand.ShowAsync(
			showPens: true, spans, (MinimapStand.ExtentFirst, newest));
		var navigation = stand.Model.Navigation;
		navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		var (spansBefore, cellsBefore) = (stand.Model.Spans, stand.Calendar.Cells);
		cellsBefore[^1].Coverage.Should().Be(DayCoverage.Low, "the newest day holds one sample");

		var sample = newest.AddMinutes(5.0);
		navigation.OnLiveEdge(sample, sample);
		Dispatcher.UIThread.RunJobs();

		stand.Model.Spans.Should().NotBeSameAs(spansBefore, "the feed republishes the newest day's last sample");
		stand.Calendar.Cells.Should().BeSameAs(cellsBefore, "five minutes keep the day on its step");

		var noon = MinimapStand.At(newestDay, 12);
		navigation.OnLiveEdge(noon, noon);
		Dispatcher.UIThread.RunJobs();

		stand.Calendar.Cells[^1].Coverage.Should().Be(DayCoverage.Medium, "five of the ten working hours");
		stand.Calendar.Cells[^1].Left.Should().Be(cellsBefore[^1].Left);
	}

	[AvaloniaFact]
	public async Task TheMarkerTicks_MarkTheDaysOfTheOperatorsMarkersOnly()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var calendar = stand.ShownCalendar;

		stand.Model.MarkerChannel.Push(
			new StoredMarker(1, MinimapStand.At(_monday, 12), "operator"),
			new StoredMarker(2, MinimapStand.At(_monday, 15), "second"),
			new StoredMarker(3, MinimapStand.At(_tuesday, 12), null, MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(4, MinimapStand.At(_tuesday, 13), null, MarkerKind.RecipeEnd, IsLocked: true));
		Dispatcher.UIThread.RunJobs();

		stand.Calendar.MarkerTicks.Should().Equal(
			[calendar.Cell(calendar.IndexOf(_monday))], "two markers on Monday tick its cell once, recipes never");
	}

	[AvaloniaFact]
	public async Task WithoutDaySpans_TheCalendarFillsNoCell_WhileThePageOutlineStillShows()
	{
		using var withoutSpans = await MinimapViewStand.ShowAsync(showPens: true);

		withoutSpans.Model.Spans.Should().BeEmpty("a failed first read holds no spans either");
		withoutSpans.Calendar.Cells.Should().BeEmpty();
		withoutSpans.Calendar.PageOutline.Should().Be(
			withoutSpans.ShownCalendar.PageOutline(withoutSpans.Model.ViewModel.Pager.Page));
		withoutSpans.Calendar.PageOutline.Should().NotBeNull("the calendar never hides where the chart is");

		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		stand.Model.Navigation.SetWindow(MinimapStand.At(_saturday, 10), MinimapStand.At(_saturday, 11));
		Dispatcher.UIThread.RunJobs();
		var calendar = stand.ShownCalendar;

		stand.Calendar.PageOutline.Should().Be(Ring(calendar, _saturday, _saturday), "a day with no rows keeps it");
		stand.Calendar.Cells.Should().NotContain(cell => cell.Left == calendar.CellLeft(calendar.IndexOf(_saturday)));
	}

	[AvaloniaFact]
	public async Task TheLabelRow_OfAShortArchive_NamesItsMonths_AndDropsAMondayThatWouldTouchOne()
	{
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		var calendar = stand.ShownCalendar;
		var january = calendar.FirstDay.AddDays(calendar.DayCount - 1);

		var labels = stand.Named<Canvas>("CalendarLabels").Children.OfType<TextBlock>().ToList();

		DayCalendarLabels.Candidates(calendar).Select(label => (label.Day, label.Kind)).Should().Equal(
			(calendar.FirstDay, CalendarLabelKind.Month), (_monday, CalendarLabelKind.Day),
			(january, CalendarLabelKind.Month));
		labels.Select(label => label.Text).Should().Equal(
			MinimapLabels.Calendar(new CalendarLabel(0.0, calendar.FirstDay, CalendarLabelKind.Month)),
			MinimapLabels.Calendar(new CalendarLabel(0.0, january, CalendarLabelKind.Month)));
		Canvas.GetLeft(labels[0]).Should().Be(calendar.CellLeft(0) + 2.0);
		(Canvas.GetLeft(labels[1]) + labels[1].Bounds.Width).Should().Be(
			calendar.Right, "the last day's month ends at the row's right end, over the Monday's place");
		stand.Calendar.MonthSeparators.Should().BeEmpty("a calendar that names the Mondays draws no separator");
	}

	[AvaloniaFact]
	public async Task TheLabelRow_OfASixMonthArchive_NamesTheMonths_WithASeparatorBeforeEachStart()
	{
		var (first, last) = (new DateOnly(2025, 6, 1), new DateOnly(2025, 11, 30));
		using var stand = await MinimapViewStand.ShowAsync(
			showPens: true, extent: (MinimapStand.At(first, 0), MinimapStand.At(last, 12)));
		var calendar = stand.ShownCalendar;

		var texts = stand.Named<Canvas>("CalendarLabels").Children.OfType<TextBlock>().Select(label => label.Text);

		calendar.ShowsMondayLabels.Should().BeFalse("183 days are more than six weeks");
		texts.Should().Equal(Enumerable.Range(6, 6).Select(month => MinimapLabels.Calendar(
			new CalendarLabel(0.0, new DateOnly(2025, month, 1), CalendarLabelKind.Month))));
		stand.Calendar.MonthSeparators.Should().Equal(calendar.MonthSeparators()).And.HaveCount(5);
	}

	private static async Task<MinimapViewStand> ShowOnTuesdayAsync()
	{
		var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		stand.LandRead();

		return stand;
	}

	/// <summary>The outline around the cells from the first day to the last, its stroke outside them.</summary>
	private static TrackSpan Ring(DayCalendar calendar, DateOnly firstDay, DateOnly lastDay)
	{
		var left = calendar.CellLeft(calendar.IndexOf(firstDay)) - DayCalendar.OutlineStroke;
		var right = calendar.CellLeft(calendar.IndexOf(lastDay)) + calendar.CellWidth + DayCalendar.OutlineStroke;

		return new TrackSpan(left, right - left);
	}
}
