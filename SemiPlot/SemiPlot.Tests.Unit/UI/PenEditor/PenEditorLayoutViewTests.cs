using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

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
	private const double PenPaneWidth = 420;

	private const double GroupsPaneWidth = 340;

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
			penPane.Bounds.Width.Should().Be(PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(GroupsPaneWidth);
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
			Named<Border>(window, "PenFormPanel").Bounds.Width.Should().Be(PenPaneWidth);
			Named<Border>(window, "GroupsPanel").Bounds.Width.Should().Be(GroupsPaneWidth);
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
			penPane.Bounds.Width.Should().Be(PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(GroupsPaneWidth);

			window.Width += 300;
			Dispatcher.UIThread.RunJobs();

			penPane.Bounds.Width.Should().Be(PenPaneWidth);
			groupsPane.Bounds.Width.Should().Be(GroupsPaneWidth);
			list.Bounds.Width.Should().BeApproximately(listWidth + 300, 0.5, "the list takes the width");
		}
		finally
		{
			Close(window);
		}
	}

	private static Border PenListBorderOf(PenEditorWindow window)
	{
		return PenListOf(window).Parent.Should().BeOfType<Border>().Subject;
	}
}
