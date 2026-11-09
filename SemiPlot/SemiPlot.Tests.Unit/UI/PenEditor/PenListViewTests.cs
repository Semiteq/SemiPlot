using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Tables;

using Xunit;

using static SemiPlot.Tests.Unit.UI.InterMeasure;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;
using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The pen list as the operator drives it: rows, sort, columns and the selection highlight.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenListViewTests : IDisposable
{
	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void ThePenList_RealisesNoCheckBox()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			Descendants<CheckBox>(PenListOf(window)).Should().BeEmpty("the list is read-only and looks it");
			window.TryFindResource("CheckGeometry", out var checkGeometry).Should().BeTrue();
			StartGlyphOf(RowAt(window, 1)).Data.Should().BeSameAs(checkGeometry, "the start flag is the check glyph");
			StartGlyphOf(RowAt(window, 0)).IsVisible.Should().BeFalse("Argon is hidden on start");
			StartGlyphOf(RowAt(window, 1)).IsVisible.Should().BeTrue("Pressure is shown on start");
			StartGlyphOf(RowAt(window, 2)).IsVisible.Should().BeTrue("Power is shown on start");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARow_ShowsThePenAsStoredUnderItsHeader()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var argon = RowAt(window, 0);
			var power = RowAt(window, 2);

			TextOf(argon, "RowId").Should().Be("3");
			TextOf(argon, "RowName").Should().Be(Argon.Name);
			TextOf(argon, "RowUnit").Should().Be(Argon.Unit);
			TextOf(argon, "RowGroups").Should().Be(Chamber.Name);
			var swatch = Descendant<Border>(argon, "RowColor");
			swatch.Background.Should().BeAssignableTo<ISolidColorBrush>()
				.Which.Color.Should().Be(Color.Parse(Argon.Color!));
			swatch.Bounds.Width.Should().BePositive("the swatch renders");
			Descendant<Border>(power, "RowColor").Background.Should()
				.Be(Brushes.Transparent, "a pen with no colour draws no swatch");

			var nameCell = swatch.GetVisualAncestors().OfType<DataGridCell>().First();
			DataGridColumn.GetColumnContainingElement(nameCell).Should().BeAssignableTo<DataGridColumn>()
				.Which.Header.Should().Be(Resources.PenEditorColumnName);
			LeftOf(swatch, nameCell).Should().BeGreaterThanOrEqualTo(0, "the dot sits inside the name cell");
			RightOf(swatch, nameCell).Should().BeLessThanOrEqualTo(nameCell.Bounds.Width);
			argon.Bounds.Height.Should().Be(PenEditorWindow.RowHeight);
			window.TryFindResource("DataGridCellMinHeight", out var cellMinHeight).Should().BeTrue();
			cellMinHeight.Should().Be(PenEditorWindow.RowHeight, "Semi's cell minimum would otherwise raise every row");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void EveryTextCell_TrimsWithAnEllipsisAndCarriesItsTextAsATooltip()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var cells = Descendants<TextBlock>(RowAt(window, 1))
				.Where(cell => cell.Classes.Contains("grid-cell"))
				.ToList();

			cells.Should().HaveCount(4, "the id, the name, the unit and the groups are text");
			cells.Should().AllSatisfy(cell =>
			{
				cell.TextTrimming.Should().Be(TextTrimming.CharacterEllipsis);
				ToolTip.GetTip(cell).Should().Be(cell.Text);
			});
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			HeadlessInput.Click(window, RowAt(window, 1));
			var selected = viewModel.SelectedRow;
			var form = viewModel.SelectedForm;
			selected!.Pen.Should().Be(Pressure);

			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnName));

			RealisedPens(list).Should().Equal(Argon, Power, Pressure);
			HeaderOf(window, Resources.PenEditorColumnName).Classes.Should().Contain(":sortascending");
			list.SelectedItem.Should().BeSameAs(selected, "the list keeps the row it held");
			viewModel.SelectedRow.Should().BeSameAs(selected);
			viewModel.SelectedForm.Should().BeSameAs(form);

			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnName));

			RealisedPens(list).Should().Equal(Pressure, Power, Argon);
			HeaderOf(window, Resources.PenEditorColumnName).Classes.Should().Contain(":sortdescending");
			list.SelectedItem.Should().BeSameAs(selected);
			viewModel.SelectedRow.Should().BeSameAs(selected);
			viewModel.SelectedForm.Should().BeSameAs(form);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	// Pen 1, lowest id and shown on start, makes every ascending order differ from the catalogue's and the id's.
	[AvaloniaTheory]
	[InlineData(nameof(Resources.PenEditorColumnId), new[] { 1, 3, 7, 12 })]
	[InlineData(nameof(Resources.PenEditorColumnName), new[] { 1, 3, 12, 7 })]
	[InlineData(nameof(Resources.PenEditorColumnUnit), new[] { 1, 7, 3, 12 })]
	[InlineData(nameof(Resources.PenEditorColumnOnStart), new[] { 3, 1, 7, 12 })]
	[InlineData(nameof(Resources.PenEditorColumnGroups), new[] { 1, 12, 3, 7 })]
	public void EachHeaderClick_OrdersTheListByItsColumn(string headerKey, int[] ascendingIds)
	{
		var headerText = Resources.ResourceManager.GetString(headerKey, Resources.Culture)
			?? throw new InvalidOperationException($"No resource {headerKey}.");
		var catalogue = new PenCatalogue(
			[Argon, Pressure, Power, NumberedPen(1) with { EnabledOnStart = true }], [Chamber, Spare]);
		using var viewModel = _editor.EditorOver(catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			ScrollColumnIntoView(window, headerText);
			HeadlessInput.Click(window, HeaderOf(window, headerText));

			RealisedPens(PenListOf(window)).Select(pen => pen.Id).Should().Equal(ascendingIds);
			HeaderOf(window, headerText).Classes.Should().Contain(":sortascending");

			HeadlessInput.Click(window, HeaderOf(window, headerText));

			RealisedPens(PenListOf(window)).Select(pen => pen.Id).Should().Equal(ascendingIds.Reverse());
			HeaderOf(window, headerText).Classes.Should().Contain(":sortdescending");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefresh_KeepsTheSortAndItsArrow()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnId));
			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnId));
			HeadlessInput.Click(window, RowAt(window, 1));
			_editor.RegisterResult = Result.Ok(1);
			_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power, NumberedPen(4242)], [Chamber, Spare]));

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			RealisedPens(PenListOf(window)).Select(pen => pen.Id).Should().Equal(4242, 12, 7, 3);
			HeaderOf(window, Resources.PenEditorColumnId).Classes.Should().Contain(":sortdescending");
			viewModel.SelectedRow!.Pen.Should().Be(Pressure, "the refresh keeps the selected pen");
			PenListOf(window).SelectedItem.Should().BeSameAs(viewModel.SelectedRow, "the list follows the selection");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			var hint = Named<TextBlock>(window, "PenFormEmptyHint");

			HeadlessInput.Type(window, FilterBoxOf(window), "bias");

			RealisedPens(list).Should().Equal(Power);
			list.SelectedItem.Should().BeNull("the filter selects no row");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(hint).Should().BeTrue();

			HeadlessInput.Clear(window, FilterBoxOf(window));

			RealisedPens(list).Should().Equal(Argon, Pressure, Power);
			list.SelectedItem.Should().BeNull("a cleared filter selects no row");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(hint).Should().BeTrue();
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFilterAfterACtrlClickClearedTheSelection_SelectsNoRow()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			var argon = RowAt(window, 0);
			HeadlessInput.Click(window, argon);
			HeadlessInput.Click(window, argon, RawInputModifiers.Control);
			list.SelectedItem.Should().BeNull("a Ctrl+click on the selected row clears the selection");
			viewModel.SelectedRow.Should().BeNull();

			HeadlessInput.Type(window, FilterBoxOf(window), "bias");

			RealisedPens(list).Should().Equal(Power);
			list.SelectedItem.Should().BeNull("the filter selects no row");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFilterTheSelectedRowMatches_KeepsTheRowSelectedAndItsForm()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			HeadlessInput.Click(window, RowAt(window, 1));
			var selected = viewModel.SelectedRow;
			var form = viewModel.SelectedForm;
			selected!.Pen.Should().Be(Pressure);

			HeadlessInput.Type(window, FilterBoxOf(window), "press");

			RealisedPens(list).Should().Equal(Pressure);
			list.SelectedItem.Should().BeSameAs(selected, "the list keeps the row it held");
			viewModel.SelectedRow.Should().BeSameAs(selected);
			viewModel.SelectedForm.Should().BeSameAs(form);
			Named<TextBlock>(window, "PenFormEmptyHint").IsEffectivelyVisible.Should().BeFalse();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFilterTheSelectedRowFailsToMatch_ClearsTheListSelectionAndShowsTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			HeadlessInput.Click(window, RowAt(window, 2));
			viewModel.SelectedRow!.Pen.Should().Be(Power);

			HeadlessInput.Type(window, FilterBoxOf(window), "chamber");

			RealisedPens(list).Should().Equal(Argon, Pressure);
			list.SelectedItem.Should().BeNull("the selected row is filtered out");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
			Descendants<CheckBox>(GroupListOf(window))
				.Where(box => box.Name == "MembershipBox")
				.Should().NotBeEmpty()
				.And.OnlyContain(box => !box.IsEffectivelyEnabled, "no pen is selected to edit");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFilterUnderASort_KeepsTheOrderAndItsArrow()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnId));
			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnId));

			HeadlessInput.Type(window, FilterBoxOf(window), "chamber");

			RealisedPens(PenListOf(window)).Select(pen => pen.Id).Should().Equal(7, 3);
			HeaderOf(window, Resources.PenEditorColumnId).Classes.Should().Contain(":sortdescending");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AHeaderClickAfterTheSelectedRowStoppedMatching_KeepsTheRowShownAndSelected()
	{
		var catalogue = new PenCatalogue([Argon, Pressure, Power], [Chamber, Gas with { MemberPenIds = [3, 12] }]);
		using var viewModel = _editor.EditorOver(catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			HeadlessInput.Type(window, FilterBoxOf(window), "gas");
			HeadlessInput.Click(window, RowAt(window, 0));
			var argon = viewModel.SelectedRow;
			var form = viewModel.SelectedForm;
			argon!.Pen.Should().Be(Argon);
			HeadlessInput.Click(window, Descendant<CheckBox>(GroupRowAt(window, 1), "MembershipBox"));
			argon.GroupsText.Should().Be(Chamber.Name, "Argon left Gas");

			HeadlessInput.Click(window, HeaderOf(window, Resources.PenEditorColumnName));

			RealisedPens(list).Should().Equal(Argon, Power);
			list.SelectedItem.Should().BeSameAs(argon, "a sort re-applies no filter");
			viewModel.SelectedRow.Should().BeSameAs(argon);
			viewModel.SelectedForm.Should().BeSameAs(form);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARefreshThatMakesTheSelectedPenStopMatching_ClearsTheListSelectionAndShowsTheHint()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			HeadlessInput.Click(window, RowAt(window, 1));
			viewModel.SelectedRow!.Pen.Should().Be(Pressure);
			HeadlessInput.Type(window, FilterBoxOf(window), "chamber");
			_editor.ReadResult = Result.Ok(new PenCatalogue(
				[Argon, Pressure with { Name = "Gauge" }, Power], [Chamber with { MemberPenIds = [3] }, Spare]));

			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			RealisedPens(list).Should().Equal(Argon);
			list.SelectedItem.Should().BeNull("the refreshed pen no longer matches");
			viewModel.SelectedRow.Should().BeNull();
			IsOnScreen(Named<TextBlock>(window, "PenFormEmptyHint")).Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheGroupsPane_ListsEveryGroupWhileThePenListIsFiltered()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			HeadlessInput.Type(window, FilterBoxOf(window), "zzz");

			RealisedPens(PenListOf(window)).Should().BeEmpty("no pen matches");
			RealisedRows(GroupListOf(window))
				.OrderBy(row => row.Index)
				.Select(row => TextOf(row, "GroupName"))
				.Should().Equal(Chamber.Name, Spare.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFullCatalogue_RealisesOnlyTheRowsOnScreen()
	{
		const int PenCount = 500;
		using var viewModel = _editor.EditorOver(new PenCatalogue(PensNumbered(PenCount), []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var realised = RealisedRows(PenListOf(window)).Count();

			realised.Should().BePositive().And.BeLessThan(PenCount);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			var headers = Descendants<DataGridColumnHeader>(list).ToList();

			list.Columns.Should().HaveCount(5);
			list.Columns.Should().AllSatisfy(column =>
			{
				var header = headers.Single(candidate => DataGridColumn.GetColumnContainingElement(candidate) == column);
				column.CanUserResize.Should().BeTrue();
				var headerWidth = UnconstrainedWidthOf(window, header);
				column.MinWidth.Should().BeInRange(
					headerWidth, headerWidth + 1, "the header sets the minimum, the last one with its separator shown");
				column.ActualWidth.Should().BeGreaterThanOrEqualTo(column.MinWidth);
				header.Bounds.Width.Should().BeLessThanOrEqualTo(column.ActualWidth + 0.5);
				var headerText = Descendants<TextBlock>(header).Single(text => Equals(text.Text, column.Header));
				headerText.TextLayout.WidthIncludingTrailingWhitespace.Should()
					.BeLessThanOrEqualTo(headerText.Bounds.Width + 0.5, "the header shows its whole text");
			});
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AClickedRow_ShowsTheRowHighlightAndNoCellOutline()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = PenListOf(window);
			window.TryFindResource("DataGridRowSelectedBackground", window.ActualThemeVariant, out var resource)
				.Should().BeTrue();
			var selection = resource.Should().BeAssignableTo<ISolidColorBrush>().Subject;

			HeadlessInput.Click(window, RowAt(window, 1));

			var highlight = Descendant<Border>(RowAt(window, 1), "BackgroundBorder").Background
				.Should().BeAssignableTo<ISolidColorBrush>().Subject;
			(highlight.Color, highlight.Opacity).Should().Be((selection.Color, selection.Opacity));
			var currencyVisuals = Descendants<Rectangle>(list).Where(visual => visual.Name == "CurrencyVisual").ToList();
			var focusVisuals = Descendants<Panel>(list).Where(visual => visual.Name == "FocusVisual").ToList();
			currencyVisuals.Should().NotBeEmpty().And.OnlyContain(visual => !visual.IsEffectivelyVisible);
			focusVisuals.Should().NotBeEmpty().And.OnlyContain(visual => !visual.IsEffectivelyVisible);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = _editor.EditorOver(new PenCatalogue(PensNumbered(100), []), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			window.Width.Should().Be(PenEditorWindow.DesignedWidth);
			var list = PenListOf(window);
			Descendant<ScrollBar>(list, "PART_VerticalScrollbar").IsVisible.Should().BeTrue("100 pens scroll");
			using var semiBold = InterTypeface("SemiBold");
			var declaredWidths = DeclaredColumnWidths();
			var widths = list.Columns
				.Select((column, index) =>
				{
					var headerWidth = ContentWidthInInter(window, HeaderOf(list, (string)column.Header!), semiBold);

					return HeaderMeasuredColumns.GetIsFill(column) ? headerWidth : Math.Max(declaredWidths[index], headerWidth);
				})
				.ToList();
			var viewport = ViewportOf(list);

			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{culture}: columns {string.Join(", ", widths.Select(width => $"{width:0.#}"))} px, viewport {viewport:0.#} px");
			widths.Sum().Should().BeLessThanOrEqualTo(viewport, "the list opens with no horizontal scroll in Inter");
		}
		finally
		{
			Close(window);
		}
	}

	// The realised list raises each width to its header in the test font, so the declared widths come from an unshown window.
	private static List<double> DeclaredColumnWidths()
	{
		var window = new PenEditorWindow();
		try
		{
			return [.. PenListOf(window).Columns.Select(column => column.Width.Value)];
		}
		finally
		{
			window.Close();
		}
	}

	private static PathIcon StartGlyphOf(DataGridRow row)
	{
		return Descendant<PathIcon>(row, "RowEnabledOnStart");
	}
}
