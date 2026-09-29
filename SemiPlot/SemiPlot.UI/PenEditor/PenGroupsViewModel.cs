using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>A group delete the operator asked for and has not confirmed yet, with the text naming it.</summary>
public sealed record PendingGroupDeletion(PenGroupViewModel Group, string Text);

/// <summary>
/// The groups tab: create, rename and delete groups, and the membership of the selected group. Every editor
/// call runs through the queue the pen editor owns.
/// </summary>
public sealed class PenGroupsViewModel : ReactiveObject, IDisposable
{
	private readonly IReadOnlyList<PenRowViewModel> _rows;
	private readonly PenGroupWriter _penGroupWriter;
	private readonly CompositeDisposable _disposables = [];
	private readonly Dictionary<PenGroupViewModel, PenMembershipViewModel[]> _membershipsOfGroup = [];
	private readonly ObservableAsPropertyHelper<IReadOnlyList<PenMembershipViewModel>> _memberships;
	private readonly ObservableAsPropertyHelper<string> _message;
	private bool _isReplacingGroups;

	public PenGroupsViewModel(
		IReadOnlyList<PenRowViewModel> rows,
		IReadOnlyList<StoredGroup> groups,
		IPenCatalogueEditor penCatalogueEditor,
		EditorCallQueue editorCallQueue,
		MessagePanelViewModel messagePanel,
		ILogger logger)
	{
		_rows = rows;
		_penGroupWriter = new PenGroupWriter(penCatalogueEditor, editorCallQueue, messagePanel, logger, ShowGroupsOn);

		Groups = [.. groups.Select(group => new PenGroupViewModel(group, _penGroupWriter))];

		_memberships = this.WhenAnyValue(tab => tab.SelectedGroup)
			.Select(MembershipsOf)
			.ToProperty(this, tab => tab.Memberships);
		_disposables.Add(_memberships);

		_message = this.WhenAnyValue(tab => tab.SelectedGroup)
			.Select(group => group?.WhenAnyValue(selected => selected.Message) ?? Observable.Return(string.Empty))
			.Switch()
			.CombineLatest(_penGroupWriter.WhenAnyValue(writer => writer.Refusal), FirstNonEmpty)
			.ToProperty(this, tab => tab.Message);
		_disposables.Add(_message);

		var hasNewGroupName = this.WhenAnyValue(tab => tab.NewGroupName)
			.Select(name => !string.IsNullOrWhiteSpace(name));
		var hasSelectedGroup = this.WhenAnyValue(tab => tab.SelectedGroup)
			.Select(group => group is not null);
		var hasPendingDeletion = this.WhenAnyValue(tab => tab.PendingDeletion)
			.Select(deletion => deletion is not null);

		_disposables.Add(CreateGroupCommand = ReactiveCommand.CreateFromTask(CreateGroupAsync, hasNewGroupName));
		_disposables.Add(DeleteGroupCommand = ReactiveCommand.Create(AskToDelete, hasSelectedGroup));
		_disposables.Add(ConfirmDeleteCommand = ReactiveCommand.CreateFromTask(ConfirmDeleteAsync, hasPendingDeletion));
		_disposables.Add(CancelDeleteCommand = ReactiveCommand.Create(CancelDelete, hasPendingDeletion));
	}

	/// <summary>Replaced whole on create and on delete; a rename keeps the group's place.</summary>
	public IReadOnlyList<PenGroupViewModel> Groups
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>
	/// Two-way from the group list, whose selection write-back is ignored while the list is replaced.
	/// </summary>
	public PenGroupViewModel? SelectedGroup
	{
		get;
		set
		{
			if (!_isReplacingGroups)
			{
				this.RaiseAndSetIfChanged(ref field, value);
			}
		}
	}

	/// <summary>One entry per pen for the selected group, in catalogue order; empty with no group selected.</summary>
	public IReadOnlyList<PenMembershipViewModel> Memberships => _memberships.Value;

	public string NewGroupName
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	public PendingGroupDeletion? PendingDeletion
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>
	/// The selected group's rename message; otherwise the last refusal of a tab write; otherwise empty.
	/// </summary>
	public string Message => _message.Value;

	/// <summary>Creates a group named as typed; a refused name stays in the field.</summary>
	public ReactiveCommand<Unit, Unit> CreateGroupCommand { get; }

	/// <summary>Asks to delete the selected group; nothing is written until the deletion is confirmed.</summary>
	public ReactiveCommand<Unit, Unit> DeleteGroupCommand { get; }

	public ReactiveCommand<Unit, Unit> ConfirmDeleteCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();

		foreach (var memberships in _membershipsOfGroup.Values)
		{
			DisposeAll(memberships);
		}
	}

	// docs/architecture/overview.md#a-field-writes-when-its-edit-ends
	private PenMembershipViewModel[] MembershipsOf(PenGroupViewModel? group)
	{
		if (group is null)
		{
			return [];
		}

		if (!_membershipsOfGroup.TryGetValue(group, out var memberships))
		{
			memberships = [.. _rows.Select(row => new PenMembershipViewModel(row, group, _penGroupWriter))];
			_membershipsOfGroup.Add(group, memberships);
		}

		return memberships;
	}

	private async Task CreateGroupAsync()
	{
		var name = NewGroupName;
		var created = await _penGroupWriter.RunAsync(editor => editor.CreateGroupAsync(name));

		if (created.IsFailed)
		{
			_penGroupWriter.RefuseWrite(created);

			return;
		}

		var group = new PenGroupViewModel(new StoredGroup(created.Value, name, []), _penGroupWriter);
		ReplaceGroups([.. Groups, group]);
		SelectedGroup = group;

		if (NewGroupName == name)
		{
			NewGroupName = string.Empty;
		}
	}

	private void AskToDelete()
	{
		if (SelectedGroup is not { } group)
		{
			return;
		}

		var text = Resources.FormatPenGroupsDeleteConfirmation(group.Group.Name, group.MemberCount);
		PendingDeletion = new PendingGroupDeletion(group, text);
	}

	private async Task ConfirmDeleteAsync()
	{
		if (PendingDeletion is not { } pending)
		{
			return;
		}

		PendingDeletion = null;
		var deleted = await _penGroupWriter.RunAsync(editor => editor.DeleteGroupAsync(pending.Group.Group));

		if (deleted.IsFailed)
		{
			_penGroupWriter.RefuseWrite(deleted);

			return;
		}

		ReplaceGroups([.. Groups.Where(group => group != pending.Group)]);

		if (_membershipsOfGroup.Remove(pending.Group, out var memberships))
		{
			DisposeAll(memberships);
		}

		if (SelectedGroup == pending.Group)
		{
			SelectedGroup = null;
		}

		ShowGroupsOn(pending.Group.Group.MemberPenIds);
	}

	private void ReplaceGroups(IReadOnlyList<PenGroupViewModel> groups)
	{
		// A new items source makes the list write a null selection back before it finds the group again,
		// which would rebuild the memberships of a selection that never changed.
		_isReplacingGroups = true;
		try
		{
			Groups = groups;
		}
		finally
		{
			_isReplacingGroups = false;
		}

		this.RaisePropertyChanged(nameof(SelectedGroup));
	}

	private void CancelDelete()
	{
		PendingDeletion = null;
	}

	private void ShowGroupsOn(IReadOnlyCollection<int> penIds)
	{
		var groups = Groups.Select(group => group.Group).ToArray();

		foreach (var row in _rows.Where(row => penIds.Contains(row.Pen.Id)))
		{
			row.ShowGroupsOf(groups);
		}
	}

	private static void DisposeAll(IEnumerable<PenMembershipViewModel> memberships)
	{
		foreach (var membership in memberships)
		{
			membership.Dispose();
		}
	}

	private static string FirstNonEmpty(string first, string second)
	{
		return first.Length > 0 ? first : second;
	}
}
