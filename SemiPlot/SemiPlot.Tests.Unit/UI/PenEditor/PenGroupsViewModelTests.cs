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
	public void Delete_SetsAPendingConfirmationCarryingTheMemberCountAndWritesNothing()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[0];

		groups.DeleteGroupCommand.Execute().Subscribe();

		groups.PendingDeletion.Should().NotBeNull();
		groups.PendingDeletion!.Group.Should().BeSameAs(groups.Groups[0]);
		groups.PendingDeletion.Group.MemberCount.Should().Be(2);
		groups.PendingDeletion.Text.Should().Be(Resources.FormatPenGroupsDeleteConfirmation("Chamber", 2));
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void CancellingTheDeletion_ClearsItAndWritesNothing()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[0];
		groups.DeleteGroupCommand.Execute().Subscribe();

		groups.CancelDeleteCommand.Execute().Subscribe();

		groups.PendingDeletion.Should().BeNull();
		groups.Groups.Should().HaveCount(2);
		_editor.Calls.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task ConfirmingTheDeletion_DeletesOnceAndUpdatesTheAffectedRowsGroupText()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var gas = groups.Groups[1];
		groups.SelectedGroup = groups.Groups[0];
		groups.DeleteGroupCommand.Execute().Subscribe();

		await groups.ConfirmDeleteCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.DeleteGroup(Chamber));
		groups.PendingDeletion.Should().BeNull();
		groups.Groups.Should().Equal(gas);
		groups.SelectedGroup.Should().BeNull();
		groups.Memberships.Should().BeEmpty();
		editor.Rows.Select(row => row.GroupsText).Should().Equal("Gas", string.Empty, string.Empty);
	}

	[AvaloniaFact]
	public async Task AFailedDeletion_KeepsTheGroupFillsTheMessageAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, "Chamber");
		_editor.DeleteGroupResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[0];
		groups.DeleteGroupCommand.Execute().Subscribe();

		await groups.ConfirmDeleteCommand.Execute();

		groups.Groups.Should().HaveCount(2);
		groups.Message.Should().Be(ArchiveFailureMapper.Map(refusal).Title);
		editor.Rows[0].GroupsText.Should().Be("Chamber, Gas");
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
		editor.Rows.Select(row => row.GroupsText)
			.Should()
			.Equal("Process chamber, Gas", "Process chamber", string.Empty);
	}

	[AvaloniaFact]
	public async Task ABlankRename_NeverReachesTheEditorAndRevertsMarksAndSaysWhy()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		var chamber = groups.Groups[0];
		groups.SelectedGroup = chamber;

		chamber.RenameDraft = "  ";
		await chamber.EndRenameAsync();

		_editor.Calls.Should().BeEmpty();
		chamber.RenameDraft.Should().Be("Chamber");
		chamber.IsNameValid.Should().BeFalse();
		chamber.Message.Should().Be(Resources.PenGroupsNameRequired);
		groups.Message.Should().Be(Resources.PenGroupsNameRequired);
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
	public void TheMemberships_ListEveryPenInCatalogueOrderForTheSelectedGroup()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		groups.Memberships.Should().BeEmpty();

		editor.SortCommand.Execute(PenColumn.Name).Subscribe();
		groups.SelectedGroup = groups.Groups[1];

		groups.Memberships.Select(membership => membership.Row.Pen).Should().Equal(Argon, Pressure, Power);
		groups.Memberships.Select(membership => membership.IsMember).Should().Equal(true, false, false);
	}

	[AvaloniaFact]
	public async Task AMembershipToggle_CallsSetMembershipOnceAndNamesTheGroupInTheRow()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[1];
		var power = groups.Memberships[2];

		await power.ToggleMembershipCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Power, Gas, true));
		power.IsMember.Should().BeTrue();
		groups.SelectedGroup.MemberCount.Should().Be(2);
		editor.Rows[2].GroupsText.Should().Be("Gas");
	}

	[AvaloniaFact]
	public async Task TogglingOffAMember_TakesItOutAndClearsTheGroupFromTheRow()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[0];
		var pressure = groups.Memberships[1];

		await pressure.ToggleMembershipCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Pressure, Chamber, false));
		pressure.IsMember.Should().BeFalse();
		groups.SelectedGroup.Group.MemberPenIds.Should().Equal(3);
		editor.Rows[1].GroupsText.Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AFailedToggle_ReRaisesIsMemberUnchangedAndReportsOnce()
	{
		var refusal = FakePenCatalogueEditor.Refusal(ArchiveFault.RowGone, "Gas");
		_editor.MembershipResult = Result.Fail(refusal);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[1];
		var power = groups.Memberships[2];
		var raised = new List<string?>();
		power.PropertyChanged += Record;

		await power.ToggleMembershipCommand.Execute();

		raised.Should().Equal(nameof(PenMembershipViewModel.IsMember));
		power.IsMember.Should().BeFalse();
		editor.Rows[2].GroupsText.Should().BeEmpty();
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
	public async Task CreateGroup_WritesTheNameAndSelectsTheNewEmptyGroup()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;

		groups.NewGroupName = "Vacuum";
		await groups.CreateGroupCommand.Execute();

		_editor.Calls.Should().Equal(new FakeEditorCall.CreateGroup("Vacuum"));
		groups.Groups.Select(group => group.Group.Name).Should().Equal("Chamber", "Gas", "Vacuum");
		groups.SelectedGroup!.Group.Should().BeEquivalentTo(new StoredGroup(9, "Vacuum", []));
		groups.Memberships.Should().OnlyContain(membership => !membership.IsMember);
		groups.NewGroupName.Should().BeEmpty();
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

		groups.Message.Should().BeEmpty("a later tab write that succeeds clears the refusal");
	}

	[AvaloniaFact]
	public async Task AToggleLandingAfterTheGroupWasSelectedAgain_ShowsOnTheEntryThenShownAndItsNextClickUndoesIt()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[1];
		var gate = _editor.HoldNextCall();
		var toggled = groups.Memberships[2].ToggleMembershipCommand.Execute().ToTask();

		groups.SelectedGroup = groups.Groups[0];
		groups.SelectedGroup = groups.Groups[1];
		var shown = groups.Memberships[2];
		var raised = new List<string?>();
		shown.PropertyChanged += Record;

		gate.SetResult();
		await toggled;

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
	public async Task GroupCalls_WaitInTheQueueBehindAHeldPenCommit()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		editor.SelectedRow = editor.Rows[1];
		var groups = editor.Groups;
		groups.SelectedGroup = groups.Groups[1];
		var gate = _editor.HoldNextCall();

		editor.SelectedForm!.Name = "Chamber pressure, main";
		var committed = editor.SelectedForm.EndEditAsync(PenField.Name);
		var toggled = groups.Memberships[2].ToggleMembershipCommand.Execute().ToTask();
		groups.Groups[0].RenameDraft = "Process chamber";
		var renamed = groups.Groups[0].EndRenameAsync();

		_editor.Calls.Should().ContainSingle("the group calls wait for the held commit");

		gate.SetResult();
		await editor.WhenIdleAsync();
		await Task.WhenAll(committed, toggled, renamed);

		_editor.Calls.Should().Equal(
			new FakeEditorCall.Change(Pressure, new PenSettingChange.Name("Chamber pressure, main")),
			new FakeEditorCall.SetMembership(Power, Gas, true),
			new FakeEditorCall.RenameGroup(Chamber, "Process chamber"));
	}

	[AvaloniaFact]
	public async Task Refresh_RebuildsTheGroupsKeepingTheSelectedGroupById()
	{
		using var editor = _editor.EditorOver(_catalogue, _messagePanel);
		var before = editor.Groups;
		before.SelectedGroup = before.Groups[1];
		var gas = Gas with { MemberPenIds = [3, 7] };
		var vacuum = new StoredGroup(9, "Vacuum", [12]);
		_editor.ReadResult = Result.Ok(new PenCatalogue([Argon, Pressure, Power], [Chamber, gas, vacuum]));

		await editor.RefreshCommand.Execute();

		editor.Groups.Should().NotBeSameAs(before);
		editor.Groups.Groups.Select(group => group.Group).Should().Equal(Chamber, gas, vacuum);
		editor.Groups.SelectedGroup!.Group.Id.Should().Be(Gas.Id);
		editor.Groups.Memberships.Select(membership => membership.IsMember).Should().Equal(true, true, false);
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
