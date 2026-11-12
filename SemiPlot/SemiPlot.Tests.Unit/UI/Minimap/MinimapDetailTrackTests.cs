using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Navigation;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Minimap;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class MinimapDetailTrackTests
{
	private const double WindowBorderWidth = 2.0;
	private const double MinimumRunWidth = 2.0;

	private static readonly DateOnly _thursday = new(2025, 12, 25);
	private static readonly DateOnly _saturday = new(2025, 12, 27);
	private static readonly DateOnly _monday = new(2025, 12, 29);
	private static readonly DateOnly _tuesday = new(2025, 12, 30);
	private static readonly DateOnly _wednesday = new(2025, 12, 31);

	[AvaloniaFact]
	public async Task AnEmptyArchive_ShowsItsHint_AndDisablesTheControls()
	{
		using var stand = MinimapViewStand.ShowWithoutExtent();
		var hint = stand.Named<TextBlock>("HintLabel");
		hint.IsVisible.Should().BeFalse("no extent read has answered yet");
		stand.Model.Provider.Pens = [];

		await stand.Model.LoadExtentAsync();
		Dispatcher.UIThread.RunJobs();

		hint.IsVisible.Should().BeTrue();
		hint.Text.Should().Be(Resources.MinimapNoRowsInArchive);
		foreach (var name in new[] { "PreviousPageButton", "NextPageButton" })
		{
			stand.Named<Control>(name).IsEffectivelyEnabled.Should().BeFalse("{0} has no archive to page", name);
		}
	}

	[AvaloniaFact]
	public async Task APageWithNoRows_NamesItsPage_AndADayWithRowsShowsNoHint()
	{
		using var stand = await ShowOnTuesdayAsync();
		var hint = stand.Named<TextBlock>("HintLabel");
		hint.IsVisible.Should().BeFalse("Tuesday holds rows and its band landed");

		stand.Model.Navigation.SetWindow(MinimapStand.At(_saturday, 10), MinimapStand.At(_saturday, 11));
		Dispatcher.UIThread.RunJobs();

		hint.IsVisible.Should().BeTrue();
		hint.Text.Should().Be(Resources.FormatMinimapNoRowsOn(MinimapLabels.Day(_saturday)));
		stand.Named<Button>("NextPageButton").IsEffectivelyEnabled.Should().BeTrue("Monday holds rows");
	}

	[AvaloniaFact]
	public async Task AFailedBandRead_SaysSo_AndTheNextReadClearsIt()
	{
		using var stand = await ShowOnTuesdayAsync();
		var hint = stand.Named<TextBlock>("HintLabel");
		stand.Model.Provider.FailHistory = true;

		stand.Model.AdvanceToNextBandRead();
		Dispatcher.UIThread.RunJobs();

		hint.IsVisible.Should().BeTrue();
		hint.Text.Should().Be(Resources.MinimapBandNotRead);
		stand.Named<Button>("PreviousPageButton").IsEffectivelyEnabled.Should().BeTrue();

		stand.Model.Provider.FailHistory = false;
		stand.Model.AdvanceToNextBandRead();
		Dispatcher.UIThread.RunJobs();

		hint.IsVisible.Should().BeFalse("a band landed again");
	}

	[AvaloniaFact]
	public async Task AFailedDaySpansRead_DeclaresNoPageEmpty_AndTheStepsTakeCalendarSteps()
	{
		using var stand = MinimapViewStand.ShowWithoutExtent();
		var model = stand.Model;
		var navigation = model.Navigation;
		model.Provider.FailDaySpans = true;
		navigation.TrackDataExtents(MinimapStand.ExtentFirst, MinimapStand.ExtentLast);
		await model.LoadExtentAsync();
		stand.LandRead();
		navigation.SetWindow(MinimapStand.At(_saturday, 10), MinimapStand.At(_saturday, 11));
		Dispatcher.UIThread.RunJobs();

		model.Provider.DaySpansRanges.Should().ContainSingle("the first spans read is the one that failed");
		model.Panel.Entries.Should().ContainSingle("the failed spans read reaches the message panel");
		model.Spans.Should().BeEmpty();
		stand.Named<TextBlock>("HintLabel").IsVisible.Should().BeFalse("without spans no page is known to be empty");

		HeadlessInput.Click(stand.Window, stand.Named<Button>("NextPageButton"));

		navigation.From.Should().Be(MinimapStand.At(_saturday.AddDays(1), 10), "a calendar step lands on Sunday");
	}

	[AvaloniaFact]
	public async Task AOneDayArchive_DisablesBothSteps_WithTooltipsShownWhileDisabled()
	{
		var (first, last) = (MinimapStand.At(_tuesday, 7), MinimapStand.At(_tuesday, 17));
		using var stand = await MinimapViewStand.ShowAsync(
			showPens: true, [new ArchiveDaySpan(_tuesday, first, last)], (first, last));
		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		stand.LandRead();
		var (previous, next) = (stand.Named<Button>("PreviousPageButton"), stand.Named<Button>("NextPageButton"));

		stand.Calendar.Cells.Should().ContainSingle("the archive is one span");
		var ringWidth = 19.0 + (2.0 * DayCalendar.OutlineStroke);
		stand.Calendar.PageOutline.Should().Be(
			new TrackSpan(stand.CalendarCellRow.Bounds.Width - ringWidth + DayCalendar.OutlineStroke, ringWidth),
			"the page rings the one cell, which ends the row");
		stand.Named<Canvas>("CalendarLabels").Children.Should().ContainSingle("the one day keeps its label");
		previous.IsEffectivelyEnabled.Should().BeFalse();
		next.IsEffectivelyEnabled.Should().BeFalse();
		ToolTip.GetTip(previous).Should().Be(Resources.MinimapNoEarlierRowsTip);
		ToolTip.GetTip(next).Should().Be(Resources.MinimapNoLaterRowsTip);
		ToolTip.GetShowOnDisabled(previous).Should().BeTrue("a disabled step still tells why");
		ToolTip.GetShowOnDisabled(next).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task TheStepTooltips_NameTheUnitAndTheGesture()
	{
		using var stand = await ShowOnTuesdayAsync();
		var (previous, next) = (stand.Named<Button>("PreviousPageButton"), stand.Named<Button>("NextPageButton"));

		ToolTip.GetTip(previous).Should().Be(WindowGestures.PreviousDayTip);
		ToolTip.GetTip(next).Should().Be(WindowGestures.NextDayTip);
		WindowGestures.PreviousDayTip.Should().Contain("Alt+Left Arrow");
		WindowGestures.NextDayTip.Should().Contain("Alt+Right Arrow");

		stand.Model.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_wednesday, 10));
		Dispatcher.UIThread.RunJobs();

		ToolTip.GetTip(previous).Should().Be(WindowGestures.PreviousWeekTip);
		ToolTip.GetTip(next).Should().Be(
			Resources.MinimapNoLaterRowsTip, "the stand's newest week has no later week with rows");

		stand.Model.Navigation.SetWindow(MinimapStand.At(_thursday, 10), MinimapStand.At(_tuesday, 10));
		Dispatcher.UIThread.RunJobs();

		stand.Model.ViewModel.Pager.Page.Unit.Should().Be(MinimapPageUnit.Month);
		WindowGestures.PreviousMonthTip.Should().Contain("Alt+Left Arrow");
		ToolTip.GetTip(previous).Should().Be(Resources.MinimapNoEarlierRowsTip, "the stand's archive is one month");
		ToolTip.GetTip(next).Should().Be(Resources.MinimapNoLaterRowsTip);
		previous.Classes.Should().Contain("month").And.NotContain("week");
	}

	[AvaloniaFact]
	public async Task ARun_ShadesTheDayPage_AndNeitherTheWeekPageNorAHiddenRecipeMarker()
	{
		using var stand = await ShowOnTuesdayAsync();
		var viewModel = stand.Model.ViewModel;
		var shading = stand.Named<MinimapRunShading>("RunShading");
		shading.Shades.Should().BeEmpty("no recipe marker, no shading");

		stand.Model.MarkerChannel.Push(
			new StoredMarker(1, MinimapStand.At(_tuesday, 12), "Etch.csv", MarkerKind.RecipeStart, IsLocked: true),
			new StoredMarker(
				2, MinimapStand.At(_tuesday, 12, 30), "Etch.csv", MarkerKind.RecipeInterrupted, IsLocked: true));
		Dispatcher.UIThread.RunJobs();

		shading.Shades.Should().Equal(MinimapTrackGeometry.RunShades(
			viewModel.Pager.Page, viewModel.ShadedRuns, stand.StripCanvas.Bounds.Width, MinimumRunWidth));
		shading.Shades.Should().ContainSingle().Which.HasEndEdge.Should().BeTrue("the run was interrupted on the page");
		shading.Bounds.Size.Should().Be(stand.StripCanvas.Bounds.Size);

		stand.Model.Navigation.SetWindow(MinimapStand.At(_monday, 10), MinimapStand.At(_wednesday, 10));
		Dispatcher.UIThread.RunJobs();

		shading.Shades.Should().BeEmpty("a Week page shades no run");

		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		Dispatcher.UIThread.RunJobs();
		shading.Shades.Should().ContainSingle();
		stand.Model.Chart.Markers.ToggleRecipeMarkers();
		Dispatcher.UIThread.RunJobs();

		shading.Shades.Should().BeEmpty("hiding recipe markers hides the shading");
	}

	[AvaloniaFact]
	public async Task TheWindowBox_DrawsNoBorderOnAnEdgeThePageCuts()
	{
		using var stand = await ShowOnTuesdayAsync();
		var highlight = stand.Named<Border>("WindowHighlight");
		highlight.BorderThickness.Should().Be(new Thickness(WindowBorderWidth));

		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 0), MinimapStand.At(_tuesday.AddDays(40), 0));
		Dispatcher.UIThread.RunJobs();

		stand.Model.ViewModel.Pager.Page.Unit.Should().Be(
			MinimapPageUnit.Week, "the window holds no sample past New Year's midnight, inside Tuesday's week");
		(Canvas.GetLeft(highlight) + highlight.Width).Should().Be(
			stand.StripCanvas.Bounds.Width, "the window runs past the week's end");
		highlight.BorderThickness.Should().Be(
			new Thickness(WindowBorderWidth, WindowBorderWidth, 0.0, WindowBorderWidth));
	}

	[AvaloniaFact]
	public async Task TheHoverAndTheTicks_ReadTheArchiveWallClock()
	{
		var berlin = TimeZoneInfo.FindSystemTimeZoneById("Europe/Berlin");
		using var stand = await MinimapViewStand.ShowAsync(showPens: true, archiveZone: berlin);
		var page = stand.Model.ViewModel.Pager.Page;
		page.FromUtc.Should().Be(
			new DateTime(2025, 12, 31, 23, 0, 0, DateTimeKind.Utc), "Berlin's 1 January starts at 23:00 UTC");

		stand.MovePointer(new Point(0.0, stand.StripCanvas.Bounds.Height / 2.0));

		stand.Named<TextBlock>("HoverTimeLabel").Text.Should().Be("00:00");
		stand.Named<Canvas>("TickLabels").Children.OfType<TextBlock>().First().Text.Should().Be("00:00");
	}

	private static async Task<MinimapViewStand> ShowOnTuesdayAsync()
	{
		var stand = await MinimapViewStand.ShowAsync(showPens: true, MinimapStand.WorkingDays);
		stand.Model.Navigation.SetWindow(MinimapStand.At(_tuesday, 10), MinimapStand.At(_tuesday, 11));
		stand.LandRead();

		return stand;
	}
}
