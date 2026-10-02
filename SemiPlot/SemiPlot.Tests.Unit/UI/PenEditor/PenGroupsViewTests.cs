using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Input;
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

using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorFixture;
using static SemiPlot.Tests.Unit.UI.PenEditor.PenEditorWindowDriver;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>The editor window's groups tab as the operator drives it: values read off realised controls.</summary>
[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class PenGroupsViewTests : IDisposable
{
	private static readonly PenCatalogue _catalogue = new([Argon, Pressure, Power], [Chamber, Spare]);

	private readonly FakePenCatalogueEditor _editor = new();

	private readonly MessagePanelViewModel _messagePanel = new();

	public void Dispose()
	{
		_messagePanel.Dispose();
	}

	[AvaloniaFact]
	public void AMembershipBoxClicked_WritesOnceAndThePensTabNamesTheGroup()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var box = MembershipBoxOf(window, Power);
			box.IsChecked.Should().BeFalse("the pen is not in the group yet");

			HeadlessInput.Click(window, box);

			_editor.Calls.Should().Equal(new FakeEditorCall.SetMembership(Power, Chamber, true));
			box.IsChecked.Should().BeTrue();

			HeadlessInput.Click(window, Named<TabItem>(window, "PensTab"));

			TextOf(RowAt(window, 2), "RowGroups").Should().Be(Chamber.Name);
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
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var box = MembershipBoxOf(window, Power);

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
	public void DeleteClicked_AsksNamingTheMemberCountAndCancelWritesNothing()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var row = Named<Grid>(window, "GroupDeletionRow");
			var text = Named<TextBlock>(window, "GroupDeletionText");
			var confirm = Named<Button>(window, "ConfirmDeleteButton");
			var rowBounds = row.Bounds;
			IsOnScreen(confirm).Should().BeFalse("nothing asks before delete is pressed");

			HeadlessInput.Click(window, Named<Button>(window, "DeleteGroupButton"));

			text.Text.Should()
				.Be(Resources.FormatPenGroupsDeleteConfirmation(Chamber.Name, Chamber.MemberPenIds.Count));
			IsOnScreen(confirm).Should().BeTrue();
			row.Bounds.Should().Be(rowBounds, "the tab reserves the confirmation row");

			HeadlessInput.Click(window, Named<Button>(window, "CancelDeleteButton"));

			_editor.Calls.Should().BeEmpty();
			text.Text.Should().BeNullOrEmpty();
			IsOnScreen(confirm).Should().BeFalse();
			row.Bounds.Should().Be(rowBounds);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ADeleteConfirmedAfterAnotherGroupWasSelected_DeletesTheAskedGroupAndKeepsTheSelection()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			var list = Named<ListBox>(window, "GroupList");
			HeadlessInput.Click(window, GroupAt(window, 0));
			HeadlessInput.Click(window, Named<Button>(window, "DeleteGroupButton"));
			HeadlessInput.Click(window, GroupAt(window, 1));
			var spare = viewModel.Groups.SelectedGroup;
			var memberships = viewModel.Groups.Memberships;

			HeadlessInput.Click(window, Named<Button>(window, "ConfirmDeleteButton"));

			_editor.Calls.Should().Equal(new FakeEditorCall.DeleteGroup(Chamber));
			viewModel.Groups.Groups.Select(group => group.Group).Should().Equal(Spare);
			viewModel.Groups.SelectedGroup.Should().BeSameAs(spare);
			viewModel.Groups.Memberships.Should().BeSameAs(memberships, "the selection never left the group");
			list.SelectedItem.Should().BeSameAs(spare, "the list keeps the group it held");
			Named<TextBox>(window, "GroupRename").Text.Should().Be(Spare.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ANewNameCreated_AddsTheGroupAndSelectsIt()
	{
		_editor.CreateGroupResult = Result.Ok(9);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			var create = Named<Button>(window, "CreateGroupButton");
			create.IsEffectivelyEnabled.Should().BeFalse("a blank name creates nothing");

			HeadlessInput.Type(window, Named<TextBox>(window, "NewGroupName"), "Gas");
			HeadlessInput.Click(window, create);

			_editor.Calls.Should().Equal(new FakeEditorCall.CreateGroup("Gas"));
			Named<ListBox>(window, "GroupList").SelectedItem.Should().BeSameAs(viewModel.Groups.SelectedGroup);
			viewModel.Groups.SelectedGroup!.Group.Should().BeEquivalentTo(new StoredGroup(9, "Gas", []));
			Named<TextBox>(window, "GroupRename").Text.Should().Be("Gas");
			Named<TextBox>(window, "NewGroupName").Text.Should().BeEmpty();
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenAnotherGroupClicked_RenamesTheFirstGroupOnce()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var rename = Named<TextBox>(window, "GroupRename");
			Retype(window, rename, "Chamber A");

			HeadlessInput.Click(window, GroupAt(window, 1));

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			viewModel.Groups.SelectedGroup!.Group.Should().Be(Spare);
			rename.Text.Should().Be(Spare.Name);
			TextOf(GroupAt(window, 0), "GroupName").Should().Be("Chamber A");
			TextOf(GroupAt(window, 1), "GroupName").Should().Be(Spare.Name);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenTheTabsEmptyAreaClicked_RenamesOnce()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var rename = Named<TextBox>(window, "GroupRename");
			var message = Named<TextBlock>(window, "GroupsMessage");
			Retype(window, rename, "Chamber A");
			message.Text.Should().BeNullOrEmpty("the empty message line is the tab's empty area");

			HeadlessInput.Click(window, message);
			HeadlessInput.Click(window, message);

			rename.IsFocused.Should().BeFalse("a click on empty space takes the keyboard from the field");
			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			TextOf(GroupAt(window, 0), "GroupName").Should().Be("Chamber A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void AFullCatalogue_RealisesOnlyTheMembershipBoxesOnScreen()
	{
		const int PenCount = 500;
		using var viewModel = _editor.EditorOver(new PenCatalogue(PensNumbered(PenCount), [Spare]), _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));

			var realised = Named<ItemsControl>(window, "MembershipList").GetVisualDescendants()
				.OfType<CheckBox>()
				.Count();

			realised.Should().BePositive().And.BeLessThan(PenCount);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public async Task ARenameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing()
	{
		_editor.ReadResult = Result.Ok(_catalogue);
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var rename = Named<TextBox>(window, "GroupRename");
			var registration = _editor.HoldNextCall();
			HeadlessInput.Click(window, Named<Button>(window, "PenEditorRefreshButton"));

			rename.IsEffectivelyEnabled.Should().BeFalse("the groups tab waits for the rebuild");
			Named<TextBox>(window, "NewGroupName").IsEffectivelyEnabled.Should().BeFalse();
			HeadlessInput.Type(window, rename, " typed during refresh");
			registration.SetResult();
			await HeadlessWait.Until(() => Named<ListBox>(window, "GroupList").IsEffectivelyEnabled);
			HeadlessInput.Click(window, GroupAt(window, 1));

			rename.Text.Should().Be(Spare.Name);
			TextOf(GroupAt(window, 0), "GroupName").Should().Be(Chamber.Name);
			_editor.Calls.Should().Equal(new FakeEditorCall.RegisterNewPens(), new FakeEditorCall.Read());
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public async Task AMembershipLandingAfterItsGroupWasSelectedAgain_ChecksTheBoxAndTheNextClickUnchecksIt()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			var toggle = _editor.HoldNextCall();
			HeadlessInput.Click(window, MembershipBoxOf(window, Power));

			HeadlessInput.Click(window, GroupAt(window, 1));
			HeadlessInput.Click(window, GroupAt(window, 0));
			toggle.SetResult();
			await HeadlessWait.Until(() => MembershipBoxOf(window, Power).IsChecked == true);

			HeadlessInput.Click(window, MembershipBoxOf(window, Power));
			await HeadlessWait.Until(() => _editor.Calls.OfType<FakeEditorCall.SetMembership>().Count() == 2);

			_editor.Calls.OfType<FakeEditorCall.SetMembership>()
				.Select(call => call.IsMember)
				.Should()
				.Equal(true, false);
			await HeadlessWait.Until(() => MembershipBoxOf(window, Power).IsChecked == false);
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameEnteredWhileItsWriteIsHeldThenAnotherGroupClicked_RenamesOnce()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		try
		{
			OpenGroupsTab(window);
			HeadlessInput.Click(window, GroupAt(window, 0));
			Retype(window, Named<TextBox>(window, "GroupRename"), "Chamber A");
			var write = _editor.HoldNextCall();

			HeadlessInput.Press(window, PhysicalKey.Enter);
			HeadlessInput.Click(window, GroupAt(window, 1));
			write.SetResult();
			Dispatcher.UIThread.RunJobs();

			_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));
			TextOf(GroupAt(window, 0), "GroupName").Should().Be("Chamber A");
		}
		finally
		{
			Close(window);
		}
	}

	[AvaloniaFact]
	public void ARenameTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone()
	{
		using var viewModel = _editor.EditorOver(_catalogue, _messagePanel);
		var window = Realise(viewModel);
		int? renamesWhenClosed = null;
		window.Closed += (_, _) => renamesWhenClosed = _editor.Calls.OfType<FakeEditorCall.RenameGroup>().Count();

		OpenGroupsTab(window);
		HeadlessInput.Click(window, GroupAt(window, 0));
		Retype(window, Named<TextBox>(window, "GroupRename"), "Chamber A");
		var write = _editor.HoldNextCall();

		window.Close();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeTrue("the close waits for the rename in flight");
		_editor.Calls.Should().Equal(new FakeEditorCall.RenameGroup(Chamber, "Chamber A"));

		write.SetResult();
		Dispatcher.UIThread.RunJobs();

		window.IsVisible.Should().BeFalse();
		renamesWhenClosed.Should().Be(1, "the rename is written once, before the window is gone");
	}

	private static CheckBox MembershipBoxOf(PenEditorWindow window, StoredPen pen)
	{
		return Named<ItemsControl>(window, "MembershipList").GetVisualDescendants()
			.OfType<CheckBox>()
			.Single(box => box.DataContext is PenMembershipViewModel membership && membership.Row.Pen.Id == pen.Id);
	}
}
