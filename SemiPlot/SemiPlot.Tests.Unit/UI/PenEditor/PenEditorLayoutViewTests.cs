using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.InterMeasure;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;
using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The editor window's composition: the top bar over the pen list, the pen pane and the groups pane.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenEditorLayoutViewTests : IDisposable
{
	private const double Gap = 16;

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListBorderOf(window);
			var penPane = Named<Border>(window, "PenFormPanel");
			var groupsPane = Named<Border>(window, "GroupsPanel");
			var topBar = Named<Grid>(window, "PenEditorTopBar");

			window.GetVisualDescendants().OfType<TabControl>().Should().BeEmpty("the window holds no tabs");
			TopOf(penPane, window).Should().Be(TopOf(list, window), "the three parts share one top");
			TopOf(groupsPane, window).Should().Be(TopOf(list, window));
			penPane.Bounds.Width.Should().Be(PenEditorWindow.PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(PenEditorWindow.GroupsPaneWidth);
			LeftOf(list, window).Should().Be(Gap, "the list starts at the window margin");
			(LeftOf(penPane, window) - RightOf(list, window)).Should().Be(Gap);
			(LeftOf(groupsPane, window) - RightOf(penPane, window)).Should().Be(Gap);
			(window.Bounds.Width - RightOf(groupsPane, window)).Should().Be(Gap, "the groups pane ends at the margin");
			(TopOf(list, window) - BottomOf(topBar, window)).Should().Be(Gap, "the top bar stands above the three");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var refresh = Named<Button>(window, "PenEditorRefreshButton");
			var addedCount = Named<TextBlock>(window, "PenEditorAddedCount");
			var groupsPane = Named<Border>(window, "GroupsPanel");
			_editor.RegisterResult = Result.Ok(2);
			_editor.ReadResult = Result.Ok(ViewCatalogue);

			HeadlessInput.Click(window, refresh);

			addedCount.Text.Should().Be(Resources.FormatPenEditorAddedCount(2));
			refresh.Content.Should().Be(Resources.PenEditorRefresh, "the button names what it refreshes");
			IsOnScreen(refresh).Should().BeTrue();
			IsOnScreen(addedCount).Should().BeTrue();
			RightOf(refresh, window).Should().Be(RightOf(groupsPane, window), "the bar is right-aligned");
			(LeftOf(refresh, window) - RightOf(addedCount, window)).Should().Be(8, "the count sits left of Refresh");
			BottomOf(refresh, window).Should().BeLessThanOrEqualTo(TopOf(groupsPane, window), "the bar is above");
			BottomOf(addedCount, window).Should().BeLessThanOrEqualTo(TopOf(groupsPane, window));
			PenListOf(window).Columns[3].Header.Should().Be(Resources.PenEditorColumnOnStart);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var box = FilterBoxOf(window);
			var topBar = Named<Grid>(window, "PenEditorTopBar");
			var refresh = Named<Button>(window, "PenEditorRefreshButton");
			var addedCount = Named<TextBlock>(window, "PenEditorAddedCount");

			box.GetVisualAncestors().Should().Contain(topBar, "the box sits in the top bar");
			box.Bounds.Width.Should().Be(PenEditorWindow.FilterBoxWidth).And.Be(280);
			LeftOf(box, window).Should().Be(LeftOf(PenListBorderOf(window), window), "the box starts above the list");
			LeftOf(box, window).Should().Be(Gap);
			box.Bounds.Height.Should().Be(refresh.Bounds.Height, "the box keeps the bar at the button's height");
			topBar.Bounds.Height.Should().Be(refresh.Bounds.Height);
			box.PlaceholderText.Should().Be(Resources.PenEditorFilterPlaceholder);
			RightOf(box, window).Should().BeLessThan(LeftOf(addedCount, window), "the box ends left of the count");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheRefreshButton_TakesTheThemesPaddingAndHeight()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			ShouldTakeTheThemesButtonInsets(
				Named<Button>(window, "PenEditorRefreshButton"), "the refresh button is a text button");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void ThePlaceholder_FitsTheFilterBoxInInter(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var box = FilterBoxOf(window);
			using var typeface = InterTypeface();
			using var font = InterFont(typeface, box.FontSize);
			var inner = box.Bounds.Width
				- box.Padding.Left - box.Padding.Right
				- box.BorderThickness.Left - box.BorderThickness.Right;
			var placeholder = font.MeasureText(
				box.PlaceholderText ?? throw new InvalidOperationException("The filter box shows no placeholder."));

			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{culture}: placeholder {placeholder:0.#} px, inner width {inner:0.#} px");
			placeholder.Should().BeLessThanOrEqualTo((float)inner, "the placeholder shows whole on one line in Inter");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AtTheMinimumWidth_BothPanesKeepTheirWidthAndThePenListScrolls()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			window.Width.Should().Be(PenEditorWindow.DesignedWidth);
			window.MinWidth.Should().Be(PenEditorWindow.MinimumWidth);

			window.Width = window.MinWidth;
			Dispatcher.UIThread.RunJobs();

			var list = PenListOf(window);
			var viewport = ViewportOf(list);
			Named<Border>(window, "PenFormPanel").Bounds.Width.Should().Be(PenEditorWindow.PenPaneWidth);
			Named<Border>(window, "GroupsPanel").Bounds.Width.Should().Be(PenEditorWindow.GroupsPaneWidth);
			list.Columns.Should().AllSatisfy(column => column.ActualWidth.Should().BeGreaterThanOrEqualTo(column.MinWidth));
			(list.Columns[0].ActualWidth + list.Columns[1].ActualWidth).Should()
				.BeLessThanOrEqualTo(viewport + 0.5, "Id and Name stay inside the list");
			list.Columns.Sum(column => column.ActualWidth).Should()
				.BeGreaterThan(viewport, "the rest of the columns scroll");
			Descendant<ScrollBar>(list, "PART_HorizontalScrollbar").IsVisible.Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void BothPanes_StayFixedWhenTheWindowWidens()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var penPane = Named<Border>(window, "PenFormPanel");
			var groupsPane = Named<Border>(window, "GroupsPanel");
			var list = PenListOf(window);
			var listWidth = list.Bounds.Width;
			penPane.Bounds.Width.Should().Be(PenEditorWindow.PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(PenEditorWindow.GroupsPaneWidth);

			window.Width += 300;
			Dispatcher.UIThread.RunJobs();

			penPane.Bounds.Width.Should().Be(PenEditorWindow.PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(PenEditorWindow.GroupsPaneWidth);
			list.Bounds.Width.Should().BeApproximately(listWidth + 300, 0.5, "the list takes the width");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows()
	{
		const double GroupsPaneInset = 1 + 16;
		const int ListedRows = 4;
		using var viewModel = _editor.EditorOver(new PenCatalogue(PensNumbered(20), GroupsNumbered(20)), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			window.Height = window.MinHeight;
			Dispatcher.UIThread.RunJobs();
			window.Height.Should().Be(PenEditorWindow.MinimumHeight);
			PickGroupMenuItem(window, 0, "GroupDeleteItem");

			var groupsPane = Named<Border>(window, "GroupsPanel");
			var actionBlock = Named<Panel>(window, "GroupActionBlock");
			var confirm = Named<Button>(window, "ConfirmDeleteButton");
			IsOnScreen(confirm).Should().BeTrue("a deletion is asked");
			BottomOf(confirm, actionBlock).Should().BeLessThanOrEqualTo(actionBlock.Bounds.Height);
			BottomOf(Named<TextBlock>(window, "GroupsMessage"), groupsPane).Should()
				.BeLessThanOrEqualTo(groupsPane.Bounds.Height - GroupsPaneInset, "the message line ends above the padding");
			RowsPresenterOf(GroupListOf(window)).Bounds.Height.Should()
				.BeGreaterThanOrEqualTo(ListedRows * PenEditorWindow.RowHeight);
			RowsPresenterOf(PenListOf(window)).Bounds.Height.Should()
				.BeGreaterThanOrEqualTo(ListedRows * PenEditorWindow.RowHeight);
		}
		finally
		{
			Close(window);
		}
	}

	[Theory]
	[InlineData(1920, 1440)]
	[InlineData(1440, 1440)]
	[InlineData(1366, 1366)]
	[InlineData(1280, 1280)]
	[InlineData(1024, 1272)]
	[InlineData(double.PositiveInfinity, 1440)]
	public void TheOpeningWidth_TakesTheDesignedWidthWhereTheWorkingAreaAllowsAndNeverLessThanTheMinimum(
		double workingAreaWidth,
		double expected)
	{
		PenEditorWindow.OpeningWidthFor(workingAreaWidth).Should().Be(expected);
	}

	private static DataGridRowsPresenter RowsPresenterOf(DataGrid list)
	{
		return Descendant<DataGridRowsPresenter>(list, "PART_RowsPresenter");
	}

	private static Border PenListBorderOf(PenEditorWindow window)
	{
		return PenListOf(window).Parent.Should().BeOfType<Border>().Subject;
	}
}
