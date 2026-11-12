using System.Globalization;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Navigation;

using Xunit;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Navigation;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class NavigationListViewTests
{
	private const double ContentWidth = 400.0;
	private const double ViewportHeight = 280.0;

	[AvaloniaFact]
	public void TheOpenList_FocusesTheFilter_AndGivesTheMarkerNearestTheWindowTheActiveLook()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 7, 0), Marker(2, 11, 20), Marker(3, 11, 50));

		var rows = stand.OpenMarkerList();

		stand.MarkerFilter.IsFocused.Should().BeTrue("the operator types the filter at once");
		var active = rows.Should().ContainSingle(row => row.Classes.Contains("active")).Subject;
		((NavigationListItem)active.DataContext!).Entry.Key.Should().Be(2, "11:20 lies nearest the window centre, 11:30");
		ColourOf(active.Background).Should().Be(ColourOf(ThemeProbe.Brush("AppAccentFillBrush", active.ActualThemeVariant)));
		Descendant<Border>(active, "ActiveBar").IsVisible.Should().BeTrue();
		rows.Where(row => row != active)
			.Should().OnlyContain(row => !Descendant<Border>(row, "ActiveBar").IsVisible);
		stand.MarkerListView.Bounds.Width.Should().Be(ContentWidth);
	}

	[AvaloniaFact]
	public void TheMonthHeaders_NameEachMonthInCapitals_WithItsCount()
	{
		using var culture = new CultureScope("en-US");
		using var stand = NavigationBarStand.Show();
		var may = NavigationBarStand.At(9, 0).AddDays(-20);
		stand.Push(Marker(1, 9, 0), Marker(2, 10, 0), new StoredMarker(3, may, "May", MarkerKind.User));

		stand.OpenMarkerList();

		var headers = stand.MarkerListView.GetVisualDescendants().OfType<Grid>()
			.Where(header => header.Name == "MonthHeader")
			.Select(header => (Descendant<TextBlock>(header, "MonthHeaderText").Text,
				Descendant<TextBlock>(header, "MonthHeaderCount").Text))
			.ToList();
		headers.Should().Equal(("JUNE 2026", "2"), ("MAY 2026", "1"));
	}

	[AvaloniaFact]
	public void TypingAFilter_NarrowsTheRowsByLabelAndByDate_AndKeepsTheListHeight()
	{
		using var culture = new CultureScope("en-US");
		using var stand = NavigationBarStand.Show();
		var may = NavigationBarStand.At(9, 0).AddDays(-20);
		stand.Push(
			new StoredMarker(1, NavigationBarStand.At(9, 0), "Etch start", MarkerKind.User),
			new StoredMarker(2, NavigationBarStand.At(10, 0), "Purge", MarkerKind.User),
			new StoredMarker(3, may, "Etch end", MarkerKind.User));
		stand.OpenMarkerList();
		var scroller = Descendant<ScrollViewer>(stand.MarkerListView, "ListScroller");
		var height = scroller.Bounds.Height;

		HeadlessInput.Type(stand.Window, stand.MarkerFilter, "ETCH");

		LabelsOf(stand).Should().Equal("Etch start", "Etch end");
		scroller.Bounds.Height.Should().Be(height, "typing never resizes the open list");

		HeadlessInput.Clear(stand.Window, stand.MarkerFilter);
		HeadlessInput.Type(stand.Window, stand.MarkerFilter, may.ToLocalTime().ToString("d MMM", CultureInfo.CurrentCulture));

		LabelsOf(stand).Should().Equal("Etch end");
		scroller.Bounds.Height.Should().Be(height);
	}

	[AvaloniaFact]
	public void AFilterWithNoMatch_ShowsOneLineSayingSo()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 9, 0));
		stand.OpenMarkerList();
		var noMatch = Descendant<TextBlock>(stand.MarkerListView, "NoMatchLine");
		noMatch.IsVisible.Should().BeFalse();

		HeadlessInput.Type(stand.Window, stand.MarkerFilter, "zzz");

		stand.MarkerRows().Should().BeEmpty();
		noMatch.IsVisible.Should().BeTrue();
		noMatch.Text.Should().Be(Resources.MarkerListNoMatch);
	}

	[AvaloniaFact]
	public void DownThenEnter_CentresTheNextOlderMarker_AndClosesTheList()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 7, 0), Marker(2, 8, 0), Marker(3, 11, 20));
		stand.OpenMarkerList();

		HeadlessInput.Press(stand.Window, PhysicalKey.ArrowDown);

		var active = stand.MarkerRows().Single(row => row.Classes.Contains("active"));
		((NavigationListItem)active.DataContext!).Entry.Key.Should().Be(2, "Down moves from the nearest marker to the next row");
		stand.MarkerFilter.IsFocused.Should().BeTrue("the keys move the active row, the caret stays in the box");

		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.MarkerList.IsOpen.Should().BeFalse();
		var (from, to) = stand.WindowOfChart();
		(from + ((to - from) / 2.0)).Should().Be(NavigationBarStand.At(8, 0), "Enter centres the active marker");
	}

	[AvaloniaFact]
	public void UpAtTheTop_StaysOnTheNewestMarker_AndEscapeClosesWithoutMoving()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 7, 0), Marker(2, 11, 20));
		stand.OpenMarkerList();
		var window = stand.WindowOfChart();

		HeadlessInput.Press(stand.Window, PhysicalKey.ArrowUp);
		HeadlessInput.Press(stand.Window, PhysicalKey.ArrowUp);

		((NavigationListItem)stand.MarkerRows().Single(row => row.Classes.Contains("active")).DataContext!).Entry.Key
			.Should().Be(2);

		HeadlessInput.Press(stand.Window, PhysicalKey.Escape);

		stand.MarkerList.IsOpen.Should().BeFalse();
		stand.WindowOfChart().Should().Be(window);
	}

	[AvaloniaFact]
	public void ALongList_OpensScrolledToTheActiveRow_AtTenRowsHigh()
	{
		using var stand = NavigationBarStand.Show();
		// Forty markers nine minutes apart from 11:50 back; the one at 07:02 lies nearest the centre, 07:00.
		var markers = Enumerable.Range(0, 40)
			.Select(index => new StoredMarker(
				index + 1, NavigationBarStand.At(11, 50).AddMinutes(-9 * index), $"#{index}", MarkerKind.User))
			.ToArray();
		stand.Push(markers);
		stand.Chart.Navigation.SetWindow(NavigationBarStand.At(6, 30), NavigationBarStand.At(7, 30));
		Dispatcher.UIThread.RunJobs();

		stand.OpenMarkerList();
		Dispatcher.UIThread.RunJobs();

		var scroller = Descendant<ScrollViewer>(stand.MarkerListView, "ListScroller");
		scroller.Bounds.Height.Should().Be(ViewportHeight);
		stand.MarkerRows().Should().HaveCountLessThan(20, "the list realises the rows in view, not all forty");
		var active = stand.MarkerRows().Should().ContainSingle(row => row.Classes.Contains("active")).Subject;
		((NavigationListItem)active.DataContext!).Entry.Key.Should().Be(33);
		var top = active.TranslatePoint(new Point(0.0, 0.0), scroller)!.Value.Y;
		top.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(scroller.Bounds.Height - active.Bounds.Height);
	}

	[AvaloniaFact]
	public void AFilterThatMovesTheActiveRowUp_ScrollsTheListToIt()
	{
		using var stand = NavigationBarStand.Show();
		// Forty markers nine minutes apart from 11:50 back: the newest fifteen are purges, the rest etches. The
		// window centre, 08:50, lies on #20, so the open list scrolls about twenty rows down.
		var markers = Enumerable.Range(0, 40)
			.Select(index => new StoredMarker(
				index + 1,
				NavigationBarStand.At(11, 50).AddMinutes(-9 * index),
				(index < 15 ? "Purge #" : "Etch #") + index,
				MarkerKind.User))
			.ToArray();
		stand.Push(markers);
		stand.Chart.Navigation.SetWindow(NavigationBarStand.At(8, 20), NavigationBarStand.At(9, 20));
		Dispatcher.UIThread.RunJobs();
		stand.OpenMarkerList();
		Dispatcher.UIThread.RunJobs();

		HeadlessInput.Type(stand.Window, stand.MarkerFilter, "etch");
		Dispatcher.UIThread.RunJobs();

		LabelsOf(stand).Should().OnlyContain(label => label.StartsWith("Etch", StringComparison.Ordinal));
		var scroller = Descendant<ScrollViewer>(stand.MarkerListView, "ListScroller");
		var active = stand.MarkerRows().Should().ContainSingle(row => row.Classes.Contains("active"),
			"the filter keeps #20 active, fifteen rows higher up the list").Subject;
		((NavigationListItem)active.DataContext!).Entry.Key.Should().Be(21);
		var top = active.TranslatePoint(new Point(0.0, 0.0), scroller)!.Value.Y;
		top.Should().BeGreaterThanOrEqualTo(0.0).And.BeLessThanOrEqualTo(scroller.Bounds.Height - active.Bounds.Height);
	}

	[AvaloniaFact]
	public void TheLastMarkerGoneWhileTheListIsOpen_ShowsTheNoMatchLine()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 9, 0));
		stand.OpenMarkerList();

		stand.Push();

		stand.MarkerRows().Should().BeEmpty();
		Descendant<TextBlock>(stand.MarkerListView, "NoMatchLine").IsVisible.Should().BeTrue("the frame says why it is empty");
	}

	[AvaloniaFact]
	public void AMarkerReadAfterTheListClosed_ChangesItsRowsWithoutAnError()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 9, 0));
		stand.OpenMarkerList();
		HeadlessInput.Press(stand.Window, PhysicalKey.Escape);
		stand.MarkerList.IsOpen.Should().BeFalse();

		stand.Push(Marker(1, 9, 0), Marker(2, 10, 0));

		stand.MarkerListView.DataContext.Should().BeOfType<NavigationListViewModel>()
			.Which.Rows.OfType<NavigationListItem>().Should().HaveCount(2);
		stand.OpenMarkerList().Should().HaveCount(2);
	}

	[AvaloniaFact]
	public void TheRunList_OpensFromItsButton_NewestFirst_WithRecipeFlagDateAndDuration()
	{
		using var culture = new CultureScope("en-US");
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());

		var rows = stand.OpenList(StandList.RecipeRuns);

		stand.Filter(StandList.RecipeRuns).IsFocused.Should().BeTrue();
		stand.ListView(StandList.RecipeRuns).Bounds.Width.Should().Be(ContentWidth);
		rows.Select(row => Descendant<TextBlock>(row, "EntryName").Text).Should().Equal(
			"Etch.csv", "Purge.csv", "Etch.csv");
		rows.Select(row => Descendant<TextBlock>(row, "EntryDuration").Text).Should().Equal(
			"1 h 05 min", "30 min", "20 min");
		rows.Select(row => Descendant<PathIcon>(row, "InterruptedGlyph").IsVisible).Should().Equal(false, true, false);
		rows.Should().OnlyContain(row => Descendant<Panel>(row, "EntryFlag").IsVisible);
		ToolTip.GetTip(Descendant<PathIcon>(rows[1], "InterruptedGlyph")).Should().Be(Resources.RunListInterruptedTip);
		ToolTip.GetTip(Descendant<TextBlock>(rows[0], "EntryName")).Should().Be("Etch.csv");
		var active = rows.Should().ContainSingle(row => row.Classes.Contains("active")).Subject;
		active.Should().BeSameAs(rows[0], "the newest run ends 25 min before the window centre, 11:30");
		stand.Filter(StandList.RecipeRuns).PlaceholderText.Should().Be(Resources.RunListFilterPlaceholder);
	}

	[AvaloniaFact]
	public void TheMarkerRows_ShowNoFlagAndNoDuration_AndBothListsShareTheDateColumn()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push([.. ThreeRuns(), Marker(7, 9, 0)]);

		var markerRow = stand.OpenMarkerList().Single();
		Descendant<Panel>(markerRow, "EntryFlag").IsVisible.Should().BeFalse();
		Descendant<TextBlock>(markerRow, "EntryDuration").IsVisible.Should().BeFalse();
		var markerDate = Descendant<TextBlock>(markerRow, "EntryDate");
		HeadlessInput.Press(stand.Window, PhysicalKey.Escape);
		var runRow = stand.OpenList(StandList.RecipeRuns)[0];
		var runDate = Descendant<TextBlock>(runRow, "EntryDate");

		markerDate.Bounds.Width.Should().Be(NavigationListView.DateColumnWidth);
		runDate.Bounds.Width.Should().Be(NavigationListView.DateColumnWidth);
		Descendant<TextBlock>(runRow, "EntryDuration").Bounds.Width.Should().Be(NavigationListView.DurationColumnWidth);
		Descendant<Panel>(runRow, "EntryFlag").Bounds.Width.Should().Be(NavigationListView.FlagColumnWidth);
	}

	// docs/architecture/ui-theme.md#a-tooltip-resets-what-it-inherits
	[AvaloniaFact]
	public void EveryRowTooltip_LaysOutItsWholeText_InsideTheFlyout()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push([.. ThreeRuns(), new StoredMarker(7, At(9, 0), "Vacuum check", MarkerKind.User)]);

		var interruptedRun = stand.OpenList(StandList.RecipeRuns)[1];
		ShouldShowTheWholeTip(Descendant<TextBlock>(interruptedRun, "EntryName"), "Purge.csv", "the run name");
		ShouldShowTheWholeTip(
			Descendant<PathIcon>(interruptedRun, "InterruptedGlyph"),
			Resources.RunListInterruptedTip,
			"the interrupted glyph");
		HeadlessInput.Press(stand.Window, PhysicalKey.Escape);

		var marker = stand.OpenMarkerList().Single();
		ShouldShowTheWholeTip(Descendant<TextBlock>(marker, "EntryName"), "Vacuum check", "the marker label");
	}

	[AvaloniaFact]
	public void TheRunListsFilter_KeepsTheRunsThatHoldEveryToken_AndSaysWhenNoneDoes()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		stand.OpenList(StandList.RecipeRuns);
		var filter = stand.Filter(StandList.RecipeRuns);

		HeadlessInput.Type(stand.Window, filter, "csv  ETCH");

		NamesOf(stand, StandList.RecipeRuns).Should().Equal("Etch.csv", "Etch.csv");

		HeadlessInput.Clear(stand.Window, filter);
		HeadlessInput.Type(stand.Window, filter, "csv etch purge");

		stand.Rows(StandList.RecipeRuns).Should().BeEmpty();
		var noMatch = Descendant<TextBlock>(stand.ListView(StandList.RecipeRuns), "NoMatchLine");
		noMatch.IsVisible.Should().BeTrue();
		noMatch.Text.Should().Be(Resources.RunListNoMatch);
	}

	[AvaloniaFact]
	public void DownThenEnter_FramesTheNextOlderRun_AndClosesTheRunList()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		stand.OpenList(StandList.RecipeRuns);

		HeadlessInput.Press(stand.Window, PhysicalKey.ArrowDown);
		HeadlessInput.Press(stand.Window, PhysicalKey.Enter);

		stand.ListFlyout(StandList.RecipeRuns).IsOpen.Should().BeFalse();
		stand.WindowOfChart().Should().Be((At(8, 57), At(9, 33)), "the 09:00-09:30 run and a tenth on each side");
	}

	[AvaloniaFact]
	public void AClickOnARunRow_FramesItsRun_AndAnOpenRunGrowingKeepsItsRowActive()
	{
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		var rows = stand.OpenList(StandList.RecipeRuns);

		stand.Chart.Navigation.OnLiveEdge(At(12, 5), At(12, 5));
		Dispatcher.UIThread.RunJobs();

		stand.Rows(StandList.RecipeRuns).Should().HaveCount(3, "a newest sample past every run changes no row");
		HeadlessInput.Click(TopLevel.GetTopLevel(rows[2])!, rows[2]);

		stand.WindowOfChart().Should().Be((At(7, 58), At(8, 22)));
		stand.ListFlyout(StandList.RecipeRuns).IsOpen.Should().BeFalse();
	}

	// Measured in Inter, because the headless shaper advances every glyph by the font size.
	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheWatermarkAndTheWidestHeader_FitTheList(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var stand = NavigationBarStand.Show();
		stand.Push(Marker(1, 9, 0));
		stand.OpenMarkerList();
		using var typeface = InterMeasure.InterTypeface();
		var filter = stand.MarkerFilter;
		var header = stand.MarkerListView.GetVisualDescendants().OfType<TextBlock>()
			.First(text => text.Name == "MonthHeaderText");
		var count = stand.MarkerListView.GetVisualDescendants().OfType<TextBlock>()
			.First(text => text.Name == "MonthHeaderCount");

		using var watermarkFont = InterMeasure.InterFont(typeface, filter.FontSize);
		var watermark = Math.Max(
			watermarkFont.MeasureText(Resources.MarkerListFilterPlaceholder),
			watermarkFont.MeasureText(Resources.RunListFilterPlaceholder));
		var widestHeader = Enumerable.Range(1, 12)
			.Select(month => new DateTime(2026, month, 1).ToString("MMMM yyyy", CultureInfo.CurrentCulture)
				.ToUpper(CultureInfo.CurrentCulture))
			.Max(text => MeasureIn(typeface, header.FontSize, text));
		var countWidth = MeasureIn(typeface, count.FontSize, "9999");
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{cultureName}: watermark {watermark:0.0} px, header {widestHeader:0.0} px");

		watermark.Should().BeLessThanOrEqualTo((float)(filter.Bounds.Width - filter.Padding.Left - filter.Padding.Right - 2.0));
		var headerRow = ((Control)header.Parent!).Bounds.Width;
		(widestHeader + 8.0 + countWidth).Should().BeLessThanOrEqualTo(headerRow, "a month header never trims");
	}

	// Every digit stands as the widest one, so the bound holds for any date and any duration up to 99 h 59 min.
	[AvaloniaTheory]
	[InlineData("en-US")]
	[InlineData("ru-RU")]
	public void TheDateAndDurationColumns_HoldTheirWidestText(string cultureName)
	{
		using var culture = new CultureScope(cultureName);
		using var stand = NavigationBarStand.Show();
		stand.Push(ThreeRuns());
		var row = stand.OpenList(StandList.RecipeRuns)[0];
		using var typeface = InterMeasure.InterTypeface();
		var fontSize = Descendant<TextBlock>(row, "EntryDate").FontSize;
		var widestDigit = "0123456789".MaxBy(digit => MeasureIn(typeface, fontSize, digit.ToString()));

		var widestDate = Enumerable.Range(1, 12)
			.Select(month => new DateTime(2026, month, 10).ToString("dd MMM HH:mm", CultureInfo.CurrentCulture))
			.Max(text => MeasureIn(typeface, fontSize, WithDigit(text, widestDigit)));
		var widestDuration = MeasureIn(
			typeface, fontSize, WithDigit(NavigationListRows.DurationText(TimeSpan.FromMinutes(5999)), widestDigit));
		TestContext.Current.TestOutputHelper?.WriteLine(
			$"{cultureName}: date {widestDate:0.0} px, duration {widestDuration:0.0} px");

		widestDate.Should().BeLessThanOrEqualTo(NavigationListView.DateColumnWidth);
		widestDuration.Should().BeLessThanOrEqualTo(NavigationListView.DurationColumnWidth);
	}

	private static string WithDigit(string text, char digit)
	{
		return new string([.. text.Select(character => char.IsAsciiDigit(character) ? digit : character)]);
	}

	private static double MeasureIn(SkiaSharp.SKTypeface typeface, double fontSize, string text)
	{
		using var font = InterMeasure.InterFont(typeface, fontSize);

		return font.MeasureText(text);
	}

	private static IReadOnlyList<string> NamesOf(NavigationBarStand stand, StandList list)
	{
		return [.. stand.Rows(list).Select(row => ((NavigationListItem)row.DataContext!).Name)];
	}

	/// <summary>08:00-08:20 ended, 09:00-09:30 interrupted, 10:00-11:05 ended.</summary>
	private static StoredMarker[] ThreeRuns()
	{
		return
		[
			Recipe(11, 8, 0, MarkerKind.RecipeStart, "Etch.csv"),
			Recipe(12, 8, 20, MarkerKind.RecipeEnd, "Etch.csv"),
			Recipe(13, 9, 0, MarkerKind.RecipeStart, "Purge.csv"),
			Recipe(14, 9, 30, MarkerKind.RecipeInterrupted, "Purge.csv"),
			Recipe(15, 10, 0, MarkerKind.RecipeStart, "Etch.csv"),
			Recipe(16, 11, 5, MarkerKind.RecipeEnd, "Etch.csv")
		];
	}

	private static StoredMarker Recipe(long id, int hour, int minute, MarkerKind kind, string label)
	{
		return new StoredMarker(id, NavigationBarStand.At(hour, minute), label, kind, IsLocked: true);
	}

	private static DateTime At(int hour, int minute)
	{
		return NavigationBarStand.At(hour, minute);
	}

	private static IReadOnlyList<string> LabelsOf(NavigationBarStand stand)
	{
		return [.. stand.MarkerRows().Select(row => ((NavigationListItem)row.DataContext!).Name)];
	}

	private static StoredMarker Marker(long id, int hour, int minute)
	{
		return new StoredMarker(id, NavigationBarStand.At(hour, minute), "#" + id, MarkerKind.User);
	}
}
