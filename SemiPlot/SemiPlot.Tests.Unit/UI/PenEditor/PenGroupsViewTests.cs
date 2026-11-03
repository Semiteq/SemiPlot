using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Controls.Shapes;
using Avalonia.Headless;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
using Avalonia.Media;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.InterMeasure;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;
using static SemiPlot.Tests.Unit.UI.ViewProbe;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The editor window's groups pane as the operator drives it: values read off realised controls.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenGroupsViewTests : IDisposable
{
	private const double MinimumNameWidth = 100;

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void WithNoPenSelected_EveryMembershipBoxIsDisabledAndUnchecked()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var boxes = Descendants<CheckBox>(GroupListOf(window)).Where(box => box.Name == "MembershipBox").ToList();

			boxes.Should().HaveCount(2);
			boxes.Should().AllSatisfy(box =>
			{
				box.IsEffectivelyEnabled.Should().BeFalse("a membership needs a selected pen");
				box.IsChecked.Should().Be(false, "a box with no pen reads unchecked, never indeterminate");
			});
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AMembershipBoxClicked_WritesThePenIntoTheGroupAndThePenListNamesIt()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			SelectPen(window, 2);
			var box = MembershipBoxAt(window, 0);
			box.IsChecked.Should().BeFalse("the pen is not in the group yet");

			HeadlessInput.Click(window, box);

			_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Power, Chamber, true));
			box.IsChecked.Should().BeTrue();

			TextOf(RowAt(window, 2), "RowGroups").Should().Be(Chamber.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public async Task AMembershipBoxWhoseWriteIsHeld_StaysDisabledUntilTheWriteLands()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			SelectPen(window, 2);
			var box = MembershipBoxAt(window, 0);
			var write = _editor.HoldNextCall();

			HeadlessInput.Click(window, box);

			box.IsEffectivelyEnabled.Should().BeFalse("a second click waits for the write it would undo");

			write.SetResult();
			await HeadlessWait.Until(() => box.IsEffectivelyEnabled, "the landed write to free the box");

			_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Power, Chamber, true));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AMembershipTheArchiveRefuses_PutsTheBoxBackAndReportsOnce()
	{
		_editor.MembershipResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, Chamber.Name));
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			SelectPen(window, 2);
			var box = MembershipBoxAt(window, 0);

			HeadlessInput.Click(window, box);

			_editor.Calls.Should().ContainSingle();
			box.IsChecked.Should().BeFalse("the refused toggle puts the box back");
			_messagePanel.Entries.Should().ContainSingle();
			Named<TextBlock>(window, "GroupsMessage").Text.Should().NotBeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AnotherPenSelected_TheBoxesShowItsGroups()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			SelectPen(window, 0);

			MembershipBoxAt(window, 0).IsChecked.Should().BeTrue("Argon is in Chamber");
			MembershipBoxAt(window, 1).IsChecked.Should().BeFalse("Argon is not in Spare");

			SelectPen(window, 2);

			MembershipBoxAt(window, 0).IsChecked.Should().BeFalse();
			MembershipBoxAt(window, 1).IsChecked.Should().BeFalse();
			MembershipBoxAt(window, 0).IsEffectivelyEnabled.Should().BeTrue();
			_editor.Calls.Should().BeEmpty("a selection writes nothing");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AGroupRow_ShowsTheNameAndTheCountWithItsTooltip()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var row = GroupRowAt(window, 0);
			var name = Descendant<TextBlock>(row, "GroupName");
			var count = Descendant<TextBlock>(row, "GroupMemberCount");
			var nameCell = name.GetVisualAncestors().OfType<DataGridCell>().First();

			name.Text.Should().Be(Chamber.Name);
			count.Text.Should().Be("2");
			ToolTip.GetTip(count).Should().Be(Resources.PenGroupsMemberCountTip);
			count.GetVisualAncestors().OfType<DataGridCell>().First().Should().BeSameAs(nameCell);
			DataGridColumn.GetColumnContainingElement(nameCell).Should().BeAssignableTo<DataGridColumn>()
				.Which.Header.Should().Be(Resources.PenGroupsColumnGroup);
			LeftOf(count, nameCell).Should().BeGreaterThanOrEqualTo(RightOf(name, nameCell), "the count stands right");
			RightOf(count, nameCell).Should().BeLessThanOrEqualTo(nameCell.Bounds.Width);
			row.Bounds.Height.Should().Be(PenEditorWindow.RowHeight);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARowClick_ShowsNoSelectionHighlightAndNoCellOutline()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = GroupListOf(window);

			HeadlessInput.Click(window, Descendant<TextBlock>(GroupRowAt(window, 0), "GroupName"));

			GroupRowAt(window, 0).Classes.Should().Contain(":selected", "the click selects the row the style repaints");
			Hover(window, Named<TextBlock>(window, "GroupsTitle"));
			BackgroundOf(GroupRowAt(window, 0)).Should().Be(BackgroundOf(GroupRowAt(window, 1)), "at rest");
			Hover(window, GroupRowAt(window, 0));
			var hoveredSelected = BackgroundOf(GroupRowAt(window, 0));
			Hover(window, GroupRowAt(window, 1));
			hoveredSelected.Should().Be(BackgroundOf(GroupRowAt(window, 1)), "hovered");
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

	[AvaloniaFact]
	public void TheGroupGrid_SortsNothingOnAHeaderClick()
	{
		using var viewModel = _editor.EditorOver(new PenCatalogue([Argon, Pressure, Power], [Spare, Chamber]), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var header = HeaderOf(GroupListOf(window), Resources.PenGroupsColumnGroup);

			HeadlessInput.Click(window, header);

			RealisedGroupNames(window).Should().Equal(Spare.Name, Chamber.Name);
			header.Classes.Should().NotContain(":sortascending").And.NotContain(":sortdescending");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData(0)]
	[InlineData(2)]
	public void EveryGroupColumn_OpensNoNarrowerThanItsHeader(int groupCount)
	{
		var catalogue = new PenCatalogue(ViewCatalogue.Pens, [.. ViewCatalogue.Groups.Take(groupCount)]);
		using var viewModel = _editor.EditorOver(catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = GroupListOf(window);
			var headers = Descendants<DataGridColumnHeader>(list).ToList();

			list.Columns.Should().HaveCount(3);
			list.Columns.Select(column => column.CanUserResize).Should().Equal(false, true, false);
			list.Columns.Should().AllSatisfy(column =>
			{
				var header = headers.Single(candidate => DataGridColumn.GetColumnContainingElement(candidate) == column);
				var headerWidth = UnconstrainedWidthOf(window, header);
				column.MinWidth.Should().BeInRange(
					headerWidth, headerWidth + 1, "the header sets the minimum, the last one with its separator shown");
				column.ActualWidth.Should().BeGreaterThanOrEqualTo(column.MinWidth);
			});
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheRowMenu_HoldsRenameTheFoldFlagAndDelete()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var button = GroupActionsButtonAt(window, 0);
			ToolTip.GetTip(button).Should().Be(Resources.PenGroupsActionsTip);

			var items = OpenGroupMenu(window, 0).Items.Cast<Control>().ToList();

			items.Should().HaveCount(4);
			items[2].Should().BeOfType<Separator>();
			var menuItems = items.OfType<MenuItem>().ToList();
			menuItems.Select(item => item.Header).Should().Equal(
				Resources.PenGroupsRename, Resources.PenGroupsFoldedOnStartLabel, Resources.PenGroupsDelete);
			menuItems[1].ToggleType.Should().Be(MenuItemToggleType.CheckBox);
			menuItems.Should().OnlyContain(item => item.Command != null);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheFoldItemClicked_WritesOnceAndTheItemFollowsTheFlag()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 1, "GroupFoldItem");

			_editor.Calls.Should().Equal(new FakeEditorCall.SetFoldedOnStart(Spare, true));
			FoldItemOf(OpenGroupMenu(window, 1)).IsChecked.Should().BeTrue("the reopened menu shows the stored flag");
			CloseMenu(window, 1);
			FoldItemOf(OpenGroupMenu(window, 0)).IsChecked.Should().BeFalse("another group keeps its own flag");
			_editor.Calls.Should().ContainSingle();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFoldTheArchiveRefuses_LeavesTheItemUncheckedAndReportsOnce()
	{
		_editor.FoldedOnStartResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, Chamber.Name));
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupFoldItem");

			_editor.Calls.Should().ContainSingle();
			FoldItemOf(OpenGroupMenu(window, 0)).IsChecked.Should().BeFalse("the refused toggle puts the flag back");
			_messagePanel.Entries.Should().ContainSingle();
			Named<TextBlock>(window, "GroupsMessage").Text.Should().NotBeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheFoldItemSetInCode_WritesNothingAndTheItemFollowsTheCommand()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var chamber = viewModel.Groups.Groups[0];
			var item = FoldItemOf(OpenGroupMenu(window, 0));

			item.IsChecked = true;
			Dispatcher.UIThread.RunJobs();

			_editor.Calls.Should().BeEmpty("the item is not a writer of the flag");
			chamber.FoldedOnStart.Should().BeFalse();

			item.IsChecked = false;
			chamber.ToggleFoldedOnStartCommand.Execute().Subscribe();
			Dispatcher.UIThread.RunJobs();

			_editor.Calls.Should().Equal(new FakeEditorCall.SetFoldedOnStart(Chamber, true));
			item.IsChecked.Should().BeTrue();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void RenameChosenOnTheSecondRow_OpensTheBlockOnThatGroupWithItsNameFocusedAndSelected()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var rename = Named<TextBox>(window, "GroupRename");

			PickGroupMenuItem(window, 1, "GroupRenameItem");

			viewModel.Groups.PendingRename.Should().BeSameAs(viewModel.Groups.Groups[1]);
			IsOnScreen(rename).Should().BeTrue();
			rename.Text.Should().Be(Spare.Name);
			rename.IsFocused.Should().BeTrue("the operator types the new name at once");
			(rename.SelectionStart, rename.SelectionEnd).Should().Be((0, Spare.Name.Length));

			PickGroupMenuItem(window, 1, "GroupRenameItem");

			rename.IsFocused.Should().BeTrue("Rename on the open group puts the caret back in the field");
			rename.Text.Should().Be(Spare.Name);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenDoneClicked_RenamesOnceAndClosesTheBlock()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			Retype(window, Named<TextBox>(window, "GroupRename"), "Chamber A");

			HeadlessInput.Click(window, Named<Button>(window, "GroupRenameDoneButton"));

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse();
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be("Chamber A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ABlankRenameThenDoneClicked_KeepsTheBlockOpenMarkedWithTheRule()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			var rename = Named<TextBox>(window, "GroupRename");
			HeadlessInput.Clear(window, rename);

			HeadlessInput.Click(window, Named<Button>(window, "GroupRenameDoneButton"));

			rename.Text.Should().Be(Chamber.Name, "the blank draft reverts");
			rename.Classes.Should().Contain("invalid");
			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeTrue();
			Named<TextBlock>(window, "GroupsMessage").Text.Should().Be(Resources.PenGroupsNameRequired);
			_editor.Calls.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ABlankRenameThenDoneClickedTwice_ClosesTheBlockOnTheStoredName()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			HeadlessInput.Clear(window, Named<TextBox>(window, "GroupRename"));
			var done = Named<Button>(window, "GroupRenameDoneButton");

			HeadlessInput.Click(window, done);
			HeadlessInput.Click(window, done);

			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse();
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be(Chamber.Name);
			_editor.Calls.Should().BeEmpty();

			PickGroupMenuItem(window, 0, "GroupRenameItem");

			Named<TextBox>(window, "GroupRename").Classes.Should().NotContain("invalid", "the second Done dropped the mark");
			Named<TextBlock>(window, "GroupsMessage").Text.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	// The archive answers after the click lands, as a real one does; the fake would otherwise answer inside the press.
	[AvaloniaFact]
	public void ARenameTheArchiveRefusesThenDoneClicked_ClosesTheBlockAndTheMessageLineNamesTheRefusal()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, Spare.Name);
		_editor.RenameGroupResult = Result.Fail(refusal);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			Retype(window, Named<TextBox>(window, "GroupRename"), Spare.Name);
			var write = _editor.HoldNextCall();

			HeadlessInput.Click(window, Named<Button>(window, "GroupRenameDoneButton"));
			write.SetResult();
			Dispatcher.UIThread.RunJobs();

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, Spare.Name));
			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse();
			Named<TextBlock>(window, "GroupsMessage").Text.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
			_messagePanel.Entries.Should().ContainSingle();
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be(Chamber.Name);

			PickGroupMenuItem(window, 0, "GroupRenameItem");

			var rename = Named<TextBox>(window, "GroupRename");
			rename.Text.Should().Be(Chamber.Name, "the refused rename reverted the draft");
			rename.Classes.Should().Contain("invalid");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenAnotherRowsMenuOpened_RenamesTheFirstGroupOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			Retype(window, Named<TextBox>(window, "GroupRename"), "Chamber A");

			OpenGroupMenu(window, 1);

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be("Chamber A");
			TextOf(GroupRowAt(window, 1), "GroupName").Should().Be(Spare.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenThePanesEmptyAreaClicked_RenamesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			var rename = Named<TextBox>(window, "GroupRename");
			var message = Named<TextBlock>(window, "GroupsMessage");
			Retype(window, rename, "Chamber A");
			message.Text.Should().BeNullOrEmpty("the empty message line is the pane's empty area");

			HeadlessInput.Click(window, message);
			HeadlessInput.Click(window, message);

			rename.IsFocused.Should().BeFalse("a click on empty space takes the keyboard from the field");
			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be("Chamber A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameEnteredWhileItsWriteIsHeldThenDoneClicked_RenamesOnce()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			Retype(window, Named<TextBox>(window, "GroupRename"), "Chamber A");
			var write = _editor.HoldNextCall();

			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Click(window, Named<Button>(window, "GroupRenameDoneButton"));
			write.SetResult();
			Dispatcher.UIThread.RunJobs();

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse();
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be("Chamber A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing()
	{
		_editor.ReadResult = Result.Ok(ViewCatalogue);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupRenameItem");
			var rename = Named<TextBox>(window, "GroupRename");
			var registration = _editor.HoldNextCall();
			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			rename.IsEffectivelyEnabled.Should().BeFalse("the groups pane waits for the rebuild");
			Named<TextBox>(window, "NewGroupName").IsEffectivelyEnabled.Should().BeFalse();
			HeadlessInput.Type(window, rename, " typed during refresh");
			registration.SetResult();
			Dispatcher.UIThread.RunJobs();

			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse("the refresh closes the block");
			TextOf(GroupRowAt(window, 0), "GroupName").Should().Be(Chamber.Name);
			_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void DeleteChosen_AsksNamingTheGroupAndItsCountAndCancelWritesNothing()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var block = Named<StackPanel>(window, "GroupDeletionBlock");
			var confirm = Named<Button>(window, "ConfirmDeleteButton");
			IsOnScreen(confirm).Should().BeFalse("nothing asks before delete is chosen");

			PickGroupMenuItem(window, 0, "GroupDeleteItem");

			Named<TextBlock>(window, "GroupDeletionQuestion").Text.Should()
				.Be(Resources.FormatPenGroupsDeleteQuestion(Chamber.Name));
			Named<TextBlock>(window, "GroupDeletionCount").Text.Should()
				.Be(Resources.FormatPenGroupsDeleteCount(Chamber.MemberPenIds.Count));
			Named<TextBlock>(window, "GroupDeletionNote").Text.Should().Be(Resources.PenGroupsDeleteKeepsPens);
			IsOnScreen(confirm).Should().BeTrue();

			HeadlessInput.Click(window, Named<Button>(window, "CancelDeleteButton"));

			_editor.Calls.Should().BeEmpty();
			block.IsVisible.Should().BeFalse();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ADeleteConfirmed_DeletesThatGroupAndItsRowGoes()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupDeleteItem");

			HeadlessInput.Click(window, Named<Button>(window, "ConfirmDeleteButton"));

			_editor.Calls.Should().Equal(new FakeEditorCall.DeleteGroup(Chamber));
			RealisedGroupNames(window).Should().Equal(Spare.Name);
			Named<StackPanel>(window, "GroupDeletionBlock").IsVisible.Should().BeFalse();

			TextOf(RowAt(window, 0), "RowGroups").Should().BeEmpty("the deleted group leaves the pen list");
			TextOf(RowAt(window, 1), "RowGroups").Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheActionBlock_KeepsItsBoundsEmptyRenamingAndAskingToDelete()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var block = Named<Panel>(window, "GroupActionBlock");
			var panel = Named<Border>(window, "GroupsPanel");
			var blockBounds = block.Bounds;
			var panelBounds = panel.Bounds;

			PickGroupMenuItem(window, 0, "GroupRenameItem");

			(block.Bounds, panel.Bounds).Should().Be((blockBounds, panelBounds), "renaming");
			ShownInside(block).Should().Contain(Named<TextBox>(window, "GroupRename"));

			PickGroupMenuItem(window, 0, "GroupDeleteItem");

			(block.Bounds, panel.Bounds).Should().Be((blockBounds, panelBounds), "asking to delete");
			ShownInside(block).Should().Contain(Named<Button>(window, "ConfirmDeleteButton"))
				.And.Contain(Named<Button>(window, "CancelDeleteButton"));
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void TheConfirmButton_IsDestructiveFromThePaletteNotSemisDanger()
	{
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var confirm = Named<Button>(window, "ConfirmDeleteButton");

			confirm.Classes.Should().Contain("destructive").And.NotContain("Danger");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANewNameCreated_AppendsTheGroupAndClearsTheField()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var create = Named<Button>(window, "CreateGroupButton");
			create.IsEffectivelyEnabled.Should().BeFalse("a blank name creates nothing");

			HeadlessInput.Type(window, Named<TextBox>(window, "NewGroupName"), "Gas");
			HeadlessInput.Click(window, create);

			_editor.Calls.Should().Equal(new FakeEditorCall.CreateGroup("Gas"));
			RealisedGroupNames(window).Should().Equal(Chamber.Name, Spare.Name, "Gas");
			TextOf(GroupRowAt(window, 2), "GroupMemberCount").Should().Be("0");
			Named<TextBox>(window, "NewGroupName").Text.Should().BeEmpty();
			Named<StackPanel>(window, "GroupRenameBlock").IsVisible.Should().BeFalse("a created group opens no block");
			Named<StackPanel>(window, "GroupDeletionBlock").IsVisible.Should().BeFalse();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFullCatalogue_RealisesOnlyTheGroupRowsOnScreen()
	{
		const int GroupCount = 500;
		using var viewModel = _editor.EditorOver(new PenCatalogue([Argon], GroupsNumbered(GroupCount)), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			RealisedRows(GroupListOf(window)).Count().Should().BePositive().And.BeLessThan(GroupCount);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void TheGroupColumns_LeaveTheNameAtLeast100PxInInter(string culture)
	{
		const double NameInsets = 12 + 8;
		const double CountGap = 8;
		const double ActionsOpeningWidth = 40;
		using var cultureScope = new CultureScope(culture);
		using var viewModel = _editor.EditorOver(new PenCatalogue([Argon], GroupsNumbered(40)), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			var list = GroupListOf(window);
			Descendant<ScrollBar>(list, "PART_VerticalScrollbar").IsVisible.Should().BeTrue("40 groups scroll");
			using var semiBold = InterTypeface("SemiBold");
			using var regular = InterTypeface();
			var count = Descendant<TextBlock>(GroupRowAt(window, 0), "GroupMemberCount");
			using var countFont = InterFont(regular, count.FontSize);
			var viewport = ViewportOf(list);
			var membership = list.Columns[0].ActualWidth;
			var actions = Math.Max(
				ActionsOpeningWidth, HeaderWidthInInter(window, HeaderOf(list, Resources.PenGroupsColumnActions), semiBold));
			var groupHeader = HeaderWidthInInter(window, HeaderOf(list, Resources.PenGroupsColumnGroup), semiBold);
			var groupColumn = viewport - membership - actions;
			var name = groupColumn - NameInsets - CountGap - countFont.MeasureText("999");

			TestContext.Current.TestOutputHelper?.WriteLine(
				$"{culture}: actions {actions:0.#} px, group {groupColumn:0.#} px, name text {name:0.#} px");
			name.Should().BeGreaterThanOrEqualTo(MinimumNameWidth);
			groupHeader.Should().BeLessThanOrEqualTo(groupColumn, "the group header fits its column in Inter");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaTheory]
	[InlineData("en")]
	[InlineData("ru")]
	public void ThePaneTexts_FitTheirLinesInInter(string culture)
	{
		using var cultureScope = new CultureScope(culture);
		using var viewModel = _editor.EditorOver(ViewCatalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			PickGroupMenuItem(window, 0, "GroupDeleteItem");
			var subtitle = Named<TextBlock>(window, "GroupsSubtitle");
			var deletionCount = Named<TextBlock>(window, "GroupDeletionCount");
			using var typeface = InterTypeface();
			using var subtitleFont = InterFont(typeface, subtitle.FontSize);
			using var deletionFont = InterFont(typeface, deletionCount.FontSize);
			var line = subtitle.Bounds.Width;

			subtitleFont.MeasureText(Resources.PenGroupsSubtitle).Should().BeLessThanOrEqualTo((float)line);
			deletionFont.MeasureText(Resources.FormatPenGroupsDeleteCount(999)).Should().BeLessThanOrEqualTo((float)line);
			deletionFont.MeasureText(Resources.PenGroupsDeleteKeepsPens).Should().BeLessThanOrEqualTo((float)line);
		}
		finally
		{
			Close(window);
		}
	}

	private static void SelectPen(PenEditorWindow window, int index)
	{
		HeadlessInput.Click(window, RowAt(window, index));
	}

	private static CheckBox MembershipBoxAt(PenEditorWindow window, int index)
	{
		return Descendant<CheckBox>(GroupRowAt(window, index), "MembershipBox");
	}

	private static void CloseMenu(PenEditorWindow window, int index)
	{
		var menu = (MenuFlyout)GroupActionsButtonAt(window, index).Flyout!;
		menu.Hide();
		Dispatcher.UIThread.RunJobs();
	}

	private static MenuItem FoldItemOf(MenuFlyout menu)
	{
		return menu.Items.OfType<MenuItem>().Single(item => item.Name == "GroupFoldItem");
	}

	private static IEnumerable<string> RealisedGroupNames(PenEditorWindow window)
	{
		return RealisedRows(GroupListOf(window))
			.OrderBy(row => row.Index)
			.Select(row => ((PenGroupViewModel)row.DataContext!).Group.Name);
	}

	private static (Color? Color, double? Opacity) BackgroundOf(DataGridRow row)
	{
		var background = Descendant<Border>(row, "BackgroundBorder").Background;

		return (ColourOf(background), background?.Opacity);
	}

	private static void Hover(Window window, Control control)
	{
		var center = control.TranslatePoint(new Point(control.Bounds.Width / 2, control.Bounds.Height / 2), window)
			?? throw new InvalidOperationException("The control is not in the window's visual tree.");
		window.MouseMove(center, RawInputModifiers.None);
		Dispatcher.UIThread.RunJobs();
	}

	/// <summary>The block's visible descendants, each asserted inside the block's bounds.</summary>
	private static List<Control> ShownInside(Panel block)
	{
		var shown = Descendants<Control>(block).Where(control => control.IsEffectivelyVisible).ToList();
		shown.Should().AllSatisfy(control =>
		{
			LeftOf(control, block).Should().BeGreaterThanOrEqualTo(0, control.Name);
			TopOf(control, block).Should().BeGreaterThanOrEqualTo(0, control.Name);
			RightOf(control, block).Should().BeLessThanOrEqualTo(block.Bounds.Width, control.Name);
			BottomOf(control, block).Should().BeLessThanOrEqualTo(block.Bounds.Height, control.Name);
		});

		return shown;
	}
}
