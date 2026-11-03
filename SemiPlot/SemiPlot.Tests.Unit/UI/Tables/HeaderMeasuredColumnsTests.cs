using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Data;
using Avalonia.Headless.XUnit;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using SemiPlot.UI.Tables;

using Xunit;

using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.Tables;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class HeaderMeasuredColumnsTests
{
	private const string NarrowHeader = "A header wider than its column";
	private const string WideHeader = "Short";
	private const string FillHeader = "Fill";
	private const double WindowWidth = 1200;

	[AvaloniaFact]
	public void AColumnNarrowerThanItsHeader_OpensAtTheHeadersUnconstrainedWidth()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var column = ColumnOf(grid, NarrowHeader);
			var headerWidth = UnconstrainedWidthOf(window, HeaderOf(grid, NarrowHeader));

			headerWidth.Should().BeGreaterThan(10, "the stand's header is wider than its declared column");
			column.MinWidth.Should().Be(headerWidth);
			column.ActualWidth.Should().Be(headerWidth);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AColumnDeclaredWiderThanItsHeader_KeepsItsDeclaredWidth()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var column = ColumnOf(grid, WideHeader);

			column.ActualWidth.Should().Be(300);
			column.MinWidth.Should().Be(UnconstrainedWidthOf(window, HeaderOf(grid, WideHeader)));
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheWidths_AreFittedInTheLayoutPassThatRealisesTheHeaders()
	{
		var (window, grid) = Stand(WindowWidth, rowCount: 3);
		try
		{
			window.Show();
			window.UpdateLayout();

			var column = ColumnOf(grid, NarrowHeader);
			var fittedWidth = column.Width.Value;

			fittedWidth.Should().Be(UnconstrainedWidthOf(window, HeaderOf(grid, NarrowHeader)));
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AnUnsortedHeader_ReservesTheSortArrowSlot()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var header = HeaderOf(grid, WideHeader);
			var sortIcon = Descendant<PathIcon>(header, "SortIcon");
			sortIcon.IsVisible.Should().BeTrue("the unsorted header lays the arrow slot out");
			sortIcon.Bounds.Width.Should().BeGreaterThan(0);
			var unsortedWidth = UnconstrainedWidthOf(window, header);

			HeadlessInput.Click(window, header);

			header.Classes.Contains(":sortascending").Should().BeTrue("the click sorts the column");
			UnconstrainedWidthOf(window, header).Should().Be(unsortedWidth);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheFillColumn_TakesTheViewportTheOtherColumnsLeave()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var others = ColumnOf(grid, NarrowHeader).ActualWidth + ColumnOf(grid, WideHeader).ActualWidth;

			ColumnOf(grid, FillHeader).ActualWidth.Should().BeApproximately(ViewportOf(grid) - others, 0.5);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheFillColumn_ShrinksWithTheGridDownToItsMinimumAndThenTheGridScrolls()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var fill = ColumnOf(grid, FillHeader);
			var openingWidth = fill.ActualWidth;

			window.Width = WindowWidth - 100;
			Dispatcher.UIThread.RunJobs();

			fill.ActualWidth.Should().BeApproximately(openingWidth - 100, 0.5, "the fill column gives up the width");

			window.Width = 200;
			Dispatcher.UIThread.RunJobs();

			fill.ActualWidth.Should().Be(fill.MinWidth, "the fill column stops at its header's width");
			grid.Columns.Sum(column => column.ActualWidth).Should()
				.BeGreaterThan(HeadersPresenterOf(grid).Bounds.Width, "the columns overflow and the grid scrolls");
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void TheFillColumn_GrowsBackWhenTheGridWidens()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var fill = ColumnOf(grid, FillHeader);
			var openingWidth = fill.ActualWidth;
			window.Width = 200;
			Dispatcher.UIThread.RunJobs();

			window.Width = WindowWidth + 100;
			Dispatcher.UIThread.RunJobs();

			fill.ActualWidth.Should().BeApproximately(openingWidth + 100, 0.5);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AResizedColumn_GivesItsDifferenceToTheFillColumn()
	{
		const double Difference = 40;
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var resized = ColumnOf(grid, WideHeader);
			var fill = ColumnOf(grid, FillHeader);
			var openingWidth = fill.ActualWidth;

			resized.Width = new DataGridLength(resized.ActualWidth + Difference);
			Dispatcher.UIThread.RunJobs();

			fill.ActualWidth.Should().BeApproximately(openingWidth - Difference, 0.5);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AVisibleVerticalScrollBar_IsLeftOutOfTheViewport()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 200);
		try
		{
			var scrollBar = Descendant<ScrollBar>(grid, "PART_VerticalScrollbar");
			scrollBar.IsVisible.Should().BeTrue("200 rows overflow the grid");
			scrollBar.Bounds.Width.Should().BeGreaterThan(0);

			grid.Columns.Sum(column => column.ActualWidth).Should()
				.BeApproximately(HeadersPresenterOf(grid).Bounds.Width - scrollBar.Bounds.Width, 0.5);
		}
		finally
		{
			Dismiss(window);
		}
	}

	[AvaloniaFact]
	public void AGridDetachedAndAttachedAgain_KeepsFittingTheFillColumn()
	{
		var (window, grid) = Realise(WindowWidth, rowCount: 3);
		try
		{
			var fill = ColumnOf(grid, FillHeader);
			var openingWidth = fill.ActualWidth;
			window.Content = null;
			Dispatcher.UIThread.RunJobs();
			window.Content = grid;
			Dispatcher.UIThread.RunJobs();

			window.Width = WindowWidth + 100;
			Dispatcher.UIThread.RunJobs();

			fill.ActualWidth.Should().BeApproximately(openingWidth + 100, 0.5);
		}
		finally
		{
			Dismiss(window);
		}
	}

	private static (Window Window, DataGrid Grid) Realise(double width, int rowCount)
	{
		var (window, grid) = Stand(width, rowCount);
		window.Show();
		Dispatcher.UIThread.RunJobs();

		return (window, grid);
	}

	private static (Window Window, DataGrid Grid) Stand(double width, int rowCount)
	{
		var grid = new DataGrid
		{
			ItemsSource = Enumerable.Range(0, rowCount).Select(index => new StandRow($"row {index}")).ToArray(),
			IsReadOnly = true,
			HeadersVisibility = DataGridHeadersVisibility.Column
		};
		grid.Columns.Add(TextColumn(NarrowHeader, 10));
		grid.Columns.Add(TextColumn(WideHeader, 300));
		var fill = TextColumn(FillHeader, 60);
		HeaderMeasuredColumns.SetIsFill(fill, true);
		grid.Columns.Add(fill);
		HeaderMeasuredColumns.SetIsEnabled(grid, true);

		return (new Window { Width = width, Height = 400, Content = grid }, grid);
	}

	private static DataGridTextColumn TextColumn(string header, double width)
	{
		return new DataGridTextColumn
		{
			Header = header,
			Width = new DataGridLength(width),
			Binding = new Binding(nameof(StandRow.Text))
		};
	}

	private static DataGridColumn ColumnOf(DataGrid grid, string header)
	{
		return grid.Columns.Single(column => Equals(column.Header, header));
	}

	private static DataGridColumnHeader HeaderOf(DataGrid grid, string header)
	{
		return grid.GetVisualDescendants().OfType<DataGridColumnHeader>()
			.Single(candidate => Equals(candidate.Content, header));
	}

	private static Control HeadersPresenterOf(DataGrid grid)
	{
		return Descendant<DataGridColumnHeadersPresenter>(grid, "PART_ColumnHeadersPresenter");
	}

	private static double ViewportOf(DataGrid grid)
	{
		var scrollBar = Descendant<ScrollBar>(grid, "PART_VerticalScrollbar");
		var scrollBarWidth = scrollBar.IsVisible ? scrollBar.Bounds.Width : 0;

		return HeadersPresenterOf(grid).Bounds.Width - scrollBarWidth;
	}

	private static double UnconstrainedWidthOf(Window window, DataGridColumnHeader header)
	{
		header.Measure(Size.Infinity);
		var width = Math.Ceiling(header.DesiredSize.Width);
		header.InvalidateMeasure();
		window.UpdateLayout();

		return width;
	}

	private sealed record StandRow(string Text);
}
