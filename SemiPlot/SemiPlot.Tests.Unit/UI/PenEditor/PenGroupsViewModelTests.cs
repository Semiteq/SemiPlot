using System.ComponentModel;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;
using System.Windows.Input;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

using Xunit;

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

// The confirmation and the rename rule read the resource set the UI culture selects.
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenGroupsViewModelTests : IDisposable
{
	private static readonly PenCatalogue _catalogue = new([Argon, Pressure, Power], [Chamber, Gas]);

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void EachGroup_ShowsTheSelectedPensMembership()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups.Groups;

		groups.Should().OnlyContain(group => group.Membership == null, "no pen is selected");

		editor.SelectedRow = RowsOf(editor)[0];

		groups.Select(group => group.Membership!.IsMember).Should().Equal(true, true);
		groups.Should().OnlyContain(group => group.Membership!.Row == RowsOf(editor)[0]);

		editor.SelectedRow = RowsOf(editor)[2];

		groups.Select(group => group.Membership!.IsMember).Should().Equal(false, false);

		editor.SelectedRow = RowsOf(editor)[1];

		groups.Select(group => group.Membership!.IsMember).Should().Equal(true, false);

		editor.SelectedRow = null;

		groups.Should().OnlyContain(group => group.Membership == null);
	}

	[AvaloniaFact]
	public async Task AMembershipToggle_PutsTheSelectedPenIntoTheGroupOnceAndNamesTheGroupInTheRow()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = RowsOf(editor)[2];
		var gas = editor.Groups.Groups[1];

		await gas.Membership!.ToggleMembershipCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Power, Gas, true));
		gas.Membership.IsMember.Should().BeTrue();
		gas.MemberCount.Should().Be(2);
		RowsOf(editor)[2].GroupsText.Should().Be("Gas");
	}

	[AvaloniaFact]
	public async Task TogglingOffAMember_TakesThePenOutAndClearsTheGroupFromTheRow()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = RowsOf(editor)[1];
		var chamber = editor.Groups.Groups[0];

		await chamber.Membership!.ToggleMembershipCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Pressure, Chamber, false));
		chamber.Membership.IsMember.Should().BeFalse();
		chamber.Group.MemberPenIds.Should().Equal(3);
		RowsOf(editor)[1].GroupsText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AFailedToggle_ReRaisesIsMemberUnchangedAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, "Gas");
		_editor.MembershipResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = RowsOf(editor)[2];
		var power = editor.Groups.Groups[1].Membership!;
		var raised = new List<string?>();
		power.PropertyChanged += Record;

		await power.ToggleMembershipCommand.Execute();

		raised.Should().Equal(nameof(PenMembershipViewModel.IsMember));
		power.IsMember.Should().BeFalse();
		RowsOf(editor)[2].GroupsText.Should().BeEmpty();
		editor.Groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		_messagePanel.Entries.Should().ContainSingle();

		void Record(object? sender, PropertyChangedEventArgs args)
		{
			raised.Add(args.PropertyName);
		}
	}

	[AvaloniaFact]
	public async Task AToggleLandingAfterThePenWasSelectedAgain_ShowsOnTheEntryThenShownAndItsNextClickUndoesIt()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var gas = editor.Groups.Groups[1];
		editor.SelectedRow = RowsOf(editor)[2];
		var toggling = gas.Membership!;
		var gate = _editor.HoldNextCall();
		var toggled = toggling.ToggleMembershipCommand.Execute().ToTask();

		editor.SelectedRow = RowsOf(editor)[0];
		editor.SelectedRow = RowsOf(editor)[2];
		var shown = gas.Membership!;
		var raised = new List<string?>();
		shown.PropertyChanged += Record;

		gate.SetResult();
		await toggled;

		shown.Should().BeSameAs(toggling, "the entry of a pen and a group lives as long as the pane");
		raised.Should().Contain(nameof(PenMembershipViewModel.IsMember), "the box shown binds the landed toggle");
		shown.IsMember.Should().BeTrue();

		await shown.ToggleMembershipCommand.Execute();

		_editor.Calls.OfType<FakeEditorCall.SetMembership>().Select(call => call.IsMember).Should().Equal(true, false);
		shown.IsMember.Should().BeFalse();

		void Record(object? sender, PropertyChangedEventArgs args)
		{
			raised.Add(args.PropertyName);
		}
	}

	[AvaloniaFact]
	public void RenameFromARow_OpensTheRenameBlockOnThatGroupAndClosesAPendingDeletion()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.Groups[0].AskDeleteCommand.Execute().Subscribe();

		groups.Groups[1].AskRenameCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeSameAs(groups.Groups[1]);
		groups.PendingDeletion.Should().BeNull("the block shows one action at a time");
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void DeleteFromARow_AsksNamingTheGroupAndItsCountClosesTheRenameAndWritesNothing()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.Groups[1].AskRenameCommand.Execute().Subscribe();

		groups.Groups[0].AskDeleteCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeNull("the block shows one action at a time");
		groups.PendingDeletion.Should().NotBeNull();
		groups.PendingDeletion!.Group.Should().BeSameAs(groups.Groups[0]);
		groups.PendingDeletion.Question.Should().Be(Resources.FormatPenGroupsDeleteQuestion("Chamber"));
		groups.PendingDeletion.Count.Should().Be(Resources.FormatPenGroupsDeleteCount(2));
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ConfirmingTheDeletion_DeletesOnceAndUpdatesTheAffectedRowsGroupText()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		var gas = groups.Groups[1];
		chamber.AskDeleteCommand.Execute().Subscribe();

		await groups.ConfirmDeleteCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.DeleteGroup(Chamber));
		groups.PendingDeletion.Should().BeNull();
		groups.Groups.Should().Equal(gas);
		RowsOf(editor).Select(row => row.GroupsText).Should().Equal("Gas", string.Empty, string.Empty);
	}

	[AvaloniaFact]
	public async Task AFailedDeletion_KeepsTheGroupFillsTheMessageAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, "Chamber");
		_editor.DeleteGroupResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.Groups[0].AskDeleteCommand.Execute().Subscribe();

		await groups.ConfirmDeleteCommand.Execute();

		groups.Groups.Should().HaveCount(2);
		groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		RowsOf(editor)[0].GroupsText.Should().Be("Chamber, Gas");
		_messagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task ARenameCommitOnOneGroup_WritesThatGroupOnlyAndRenamesItInTheRows()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var chamber = editor.Groups.Groups[0];
		var gas = editor.Groups.Groups[1];

		chamber.RenameDraft = "Process chamber";
		await chamber.EndRenameAsync();
		await gas.EndRenameAsync();

		_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Process chamber"));
		chamber.Group.Should().Be(Chamber with { Name = "Process chamber" });
		gas.Group.Should().Be(Gas);
		RowsOf(editor).Select(row => row.GroupsText)
			.Should()
			.Equal("Process chamber, Gas", "Process chamber", string.Empty);
	}

	[AvaloniaFact]
	public async Task ABlankRename_NeverReachesTheEditorAndRevertsMarksAndSaysWhy()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		chamber.AskRenameCommand.Execute().Subscribe();

		chamber.RenameDraft = "  ";
		await chamber.EndRenameAsync();

		_editor.Calls.Should().BeEmpty();
		chamber.RenameDraft.Should().Be("Chamber");
		chamber.IsNameValid.Should().BeFalse();
		chamber.Message.Should().Be(Resources.PenGroupsNameRequired);
		groups.Message.Should().Be(Resources.PenGroupsNameRequired);
	}

	[AvaloniaFact]
	public async Task ABlankRenameThenCloseRename_KeepsTheBlockOpenOnTheGroup()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		chamber.AskRenameCommand.Execute().Subscribe();
		chamber.RenameDraft = string.Empty;
		await chamber.EndRenameAsync();

		groups.CloseRenameCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeSameAs(chamber, "a refused draft stays in view");

		chamber.RenameDraft = "Chamber A";
		groups.CloseRenameCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeNull("a valid draft closes the block");
	}

	[AvaloniaFact]
	public async Task ASecondCloseRenameAfterARefusal_ClosesTheBlockAndWritesNothing()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		chamber.AskRenameCommand.Execute().Subscribe();
		chamber.RenameDraft = string.Empty;
		await chamber.EndRenameAsync();

		groups.CloseRenameCommand.Execute().Subscribe();
		groups.CloseRenameCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeNull("the second close leaves the reverted name as stored");
		chamber.RenameDraft.Should().Be(Chamber.Name);
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ASecondCloseRenameOnABlankDraft_KeepsTheBlockOpen()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		chamber.AskRenameCommand.Execute().Subscribe();
		chamber.RenameDraft = string.Empty;

		groups.CloseRenameCommand.Execute().Subscribe();
		groups.CloseRenameCommand.Execute().Subscribe();

		groups.PendingRename.Should().BeSameAs(chamber, "a blank draft never closes the block");
	}

	[AvaloniaFact]
	public async Task ARenameAskedWhileTheGroupsDeleteIsQueued_ClosesWhenTheDeleteLands()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		var gas = groups.Groups[1];
		chamber.AskDeleteCommand.Execute().Subscribe();
		var gate = _editor.HoldNextCall();
		var deleted = groups.ConfirmDeleteCommand.Execute().ToTask();

		chamber.AskRenameCommand.Execute().Subscribe();
		groups.PendingRename.Should().BeSameAs(chamber);

		gate.SetResult();
		await deleted;

		groups.PendingRename.Should().BeNull("the block never edits a deleted group");
		groups.Groups.Should().Equal(gas);
		_editor.Calls.Should().Equal(new FakeEditorCall.DeleteGroup(Chamber));
	}

	[AvaloniaFact]
	public async Task ADeleteAskedAgainWhileTheFirstIsQueued_ClosesWhenTheFirstLands()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		chamber.AskDeleteCommand.Execute().Subscribe();
		var gate = _editor.HoldNextCall();
		var deleted = groups.ConfirmDeleteCommand.Execute().ToTask();

		chamber.AskDeleteCommand.Execute().Subscribe();

		gate.SetResult();
		await deleted;

		groups.PendingDeletion.Should().BeNull("nothing asks to delete a deleted group");
		_editor.Calls.Should().ContainSingle();
	}

	[AvaloniaFact]
	public async Task AFailedRename_RevertsMarksSaysWhyAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, "Gas");
		_editor.RenameGroupResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var chamber = editor.Groups.Groups[0];

		chamber.RenameDraft = "Gas";
		await chamber.EndRenameAsync();

		_editor.Calls.Should().ContainSingle();
		chamber.RenameDraft.Should().Be("Chamber");
		chamber.Group.Should().Be(Chamber);
		chamber.IsNameValid.Should().BeFalse();
		chamber.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		editor.Groups.PendingRename.Should().BeNull();
		editor.Groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title, "the refusal lands with no block open");
		_messagePanel.Entries.Should().ContainSingle();

		chamber.RenameDraft = "Chamber A";

		chamber.IsNameValid.Should().BeTrue("the next draft change clears the mark");
	}

	[AvaloniaFact]
	public async Task ARenameTypedBackWhileTheFirstIsHeld_WritesBothInOrderAndEndsOnTheOriginal()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var chamber = editor.Groups.Groups[0];
		var gate = _editor.HoldNextCall();

		chamber.RenameDraft = "Process chamber";
		var renamed = chamber.EndRenameAsync();
		chamber.RenameDraft = Chamber.Name;
		var restored = chamber.EndRenameAsync();

		gate.SetResult();
		await editor.WhenIdleAsync();
		await Task.WhenAll(renamed, restored);

		_editor.Calls.Should().Equal(
			new FakeEditorCall.RenameGroup(Chamber, "Process chamber"),
			new FakeEditorCall.RenameGroup(Chamber with { Name = "Process chamber" }, Chamber.Name));
		chamber.Group.Should().BeEquivalentTo(Chamber);
		chamber.RenameDraft.Should().Be(Chamber.Name);
	}

	[AvaloniaFact]
	public async Task AFailedRenameWithALaterOneQueued_EndsOnTheLaterNameAndWritesNothingMore()
	{
		_editor.RenameGroupResult = Result.Fail(FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, "Gas"));
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var chamber = editor.Groups.Groups[0];
		var gate = _editor.HoldNextCall();

		chamber.RenameDraft = "Gas";
		var failed = chamber.EndRenameAsync();
		chamber.RenameDraft = "Process chamber";
		var renamed = chamber.EndRenameAsync();

		gate.SetResult();
		_editor.RenameGroupResult = Result.Ok();
		await editor.WhenIdleAsync();
		await Task.WhenAll(failed, renamed);

		chamber.Group.Name.Should().Be("Process chamber");
		chamber.RenameDraft.Should().Be("Process chamber", "a failed rename never reverts past a later one queued");
		chamber.IsNameValid.Should().BeTrue("the later rename succeeded");
		_messagePanel.Entries.Should().ContainSingle();

		await chamber.EndRenameAsync();

		_editor.Calls.Should().HaveCount(2);
	}

	[AvaloniaFact]
	public async Task AFoldToggle_WritesTheFlagOnceAndStoresItOnTheGroup()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var chamber = editor.Groups.Groups[0];

		chamber.FoldedOnStart.Should().BeFalse();
		_editor.Calls.Should().BeEmpty("opening the pane writes no flag");

		await chamber.ToggleFoldedOnStartCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.SetFoldedOnStart(Chamber, true));
		chamber.FoldedOnStart.Should().BeTrue();
		chamber.Group.Should().Be(Chamber with { FoldedOnStart = true });

		await chamber.ToggleFoldedOnStartCommand.Execute();

		_editor.Calls.Should().Equal(
			new FakeEditorCall.SetFoldedOnStart(Chamber, true),
			new FakeEditorCall.SetFoldedOnStart(Chamber with { FoldedOnStart = true }, false));
		chamber.Group.Should().Be(Chamber);
	}

	[AvaloniaFact]
	public async Task AFailedFoldToggle_ReRaisesTheFlagUnchangedFillsTheMessageAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, "Chamber");
		_editor.FoldedOnStartResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		var raised = new List<string?>();
		chamber.PropertyChanged += Record;

		await chamber.ToggleFoldedOnStartCommand.Execute();

		raised.Should().Equal(nameof(PenGroupViewModel.FoldedOnStart));
		chamber.FoldedOnStart.Should().BeFalse();
		chamber.Group.Should().Be(Chamber);
		groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		_messagePanel.Entries.Should().ContainSingle();

		void Record(object? sender, PropertyChangedEventArgs args)
		{
			raised.Add(args.PropertyName);
		}
	}

	[AvaloniaFact]
	public void CreateGroup_CannotExecuteWhileTheNameIsBlank()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		((ICommand)groups.CreateGroupCommand).CanExecute(null).Should().BeFalse();

		groups.NewGroupName = "   ";

		((ICommand)groups.CreateGroupCommand).CanExecute(null).Should().BeFalse();

		groups.NewGroupName = "Vacuum";

		((ICommand)groups.CreateGroupCommand).CanExecute(null).Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task CreateGroup_WritesTheNameAndAppendsTheNewEmptyGroup()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		groups.NewGroupName = "Vacuum";
		await groups.CreateGroupCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.CreateGroup("Vacuum"));
		groups.Groups.Select(group => group.Group.Name).Should().Equal("Chamber", "Gas", "Vacuum");
		var vacuum = groups.Groups[2];
		vacuum.Group.Should().BeEquivalentTo(new StoredGroup(9, "Vacuum", []));
		vacuum.Membership.Should().BeNull("no pen is selected");
		groups.PendingRename.Should().BeNull("a created group opens no block");
		groups.NewGroupName.Should().BeEmpty();

		editor.SelectedRow = RowsOf(editor)[2];

		vacuum.Membership!.IsMember.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task AGroupCreatedWithAPenSelected_ShowsThatPensEntryAtOnce()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		editor.SelectedRow = RowsOf(editor)[2];

		groups.NewGroupName = "Vacuum";
		await groups.CreateGroupCommand.Execute();

		var vacuum = groups.Groups[2];
		vacuum.Membership.Should().NotBeNull("the selected pen's box shows on the new group");
		vacuum.Membership!.Row.Should().BeSameAs(RowsOf(editor)[2]);
		vacuum.Membership.IsMember.Should().BeFalse();
	}

	[AvaloniaFact]
	public async Task ANameTypedWhileTheCreateIsHeld_StaysInTheField()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var gate = _editor.HoldNextCall();

		groups.NewGroupName = "Vacuum";
		var created = groups.CreateGroupCommand.Execute().ToTask();
		groups.NewGroupName = "Heaters";
		gate.SetResult();
		await created;

		groups.Groups.Select(group => group.Group.Name).Should().Equal("Chamber", "Gas", "Vacuum");
		groups.NewGroupName.Should().Be("Heaters");
	}

	[AvaloniaFact]
	public async Task ANameTakenCreate_KeepsTheNameFillsTheMessageAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.NameTaken, "Gas");
		_editor.CreateGroupResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		groups.NewGroupName = "Gas";
		await groups.CreateGroupCommand.Execute();

		groups.NewGroupName.Should().Be("Gas");
		groups.Groups.Should().HaveCount(2);
		groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		_messagePanel.Entries.Should().ContainSingle();

		_editor.CreateGroupResult = Result.Ok(9);
		groups.NewGroupName = "Gas lines";
		await groups.CreateGroupCommand.Execute();

		groups.Message.Should().BeEmpty("a later pane write that succeeds clears the refusal");
	}

	[AvaloniaFact]
	public async Task GroupCalls_WaitInTheQueueBehindAHeldPenCommit()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = RowsOf(editor)[1];
		var groups = editor.Groups;
		var gate = _editor.HoldNextCall();

		editor.SelectedForm!.Name = "Chamber pressure, main";
		var committed = editor.SelectedForm.EndEditAsync(PenField.Name);
		var toggled = groups.Groups[1].Membership!.ToggleMembershipCommand.Execute().ToTask();
		groups.Groups[0].RenameDraft = "Process chamber";
		var renamed = groups.Groups[0].EndRenameAsync();

		_editor.Calls.Should().ContainSingle("the group calls wait for the held commit");

		gate.SetResult();
		await editor.WhenIdleAsync();
		await Task.WhenAll(committed, toggled, renamed);

		_editor.Calls.Should().Equal(
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure, main")),
			new FakeEditorCall.SetMembership(Pressure with { Name = "Chamber pressure, main" }, Gas, true),
			new FakeEditorCall.RenameGroup(Chamber, "Process chamber"));
	}

	[AvaloniaFact]
	public async Task Refresh_RebuildsTheGroupsFollowsTheSelectedPenAndClosesTheActionBlock()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var before = editor.Groups;
		editor.SelectedRow = RowsOf(editor)[1];
		before.Groups[1].AskRenameCommand.Execute().Subscribe();
		var gas = Gas with { MemberPenIds = [3, 7] };
		var vacuum = new StoredGroup(9, "Vacuum", [12]);
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power], [Chamber, gas, vacuum]));

		await editor.RefreshCommand.Execute();

		editor.Groups.Should().NotBeSameAs(before);
		editor.Groups.Groups.Select(group => group.Group).Should().Equal(Chamber, gas, vacuum);
		editor.Groups.PendingRename.Should().BeNull("a refresh closes the action block");
		editor.Groups.PendingDeletion.Should().BeNull();
		editor.SelectedRow!.Pen.Should().Be(Pressure);
		editor.Groups.Groups.Should().OnlyContain(group => group.Membership!.Row == editor.SelectedRow);
		editor.Groups.Groups.Select(group => group.Membership!.IsMember).Should().Equal(true, true, false);
		before.Groups.Should().OnlyContain(
			group => group.Membership!.Row != editor.SelectedRow,
			"the replaced pane stops before the selection moves onto the new rows");

		var replacedMembership = before.Groups[0].Membership;
		editor.SelectedRow = RowsOf(editor)[0];

		before.Groups[0].Membership.Should().BeSameAs(replacedMembership, "the replaced pane follows no selection");
	}

	[AvaloniaFact]
	public void DisposingTheEditor_StopsTheCreateCommandFollowingTheName()
	{
		var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		editor.Dispose();
		groups.NewGroupName = "Vacuum";

		((ICommand)groups.CreateGroupCommand).CanExecute(null).Should().BeFalse();
	}
}
