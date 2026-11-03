using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>A group delete the operator asked for and has not confirmed yet, with the lines naming it.</summary>
public sealed record PendingGroupDeletion(PenGroupViewModel Group, string Question, string Count);

/// <summary>
/// The groups pane: create, rename and delete groups, each group's fold flag and the selected pen's membership. Every
/// editor call runs through the queue the pen editor owns.
/// </summary>
public sealed class PenGroupsViewModel : ReactiveObject, IDisposable
{
	private readonly IReadOnlyList<PenRowViewModel> _rows;
	private readonly PenGroupWriter _penGroupWriter;
	private readonly CompositeDisposable _disposables = [];
	private readonly Dictionary<(PenRowViewModel Row, PenGroupViewModel Group), PenMembershipViewModel> _memberships = [];
	private readonly ObservableAsPropertyHelper<string> _message;

	public PenGroupsViewModel(
		IReadOnlyList<PenRowViewModel> rows,
		IReadOnlyList<StoredGroup> groups,
		IObservable<PenRowViewModel?> selectedRow,
		IPenCatalogueEditor penCatalogueEditor,
		ArchiveCallQueue archiveCallQueue,
		MessagePanelViewModel messagePanel,
		ILogger logger)
	{
		_rows = rows;
		_penGroupWriter = new PenGroupWriter(penCatalogueEditor, archiveCallQueue, messagePanel, logger, ShowGroupsOn);

		Groups = [.. groups.Select(GroupOf)];

		_disposables.Add(selectedRow
			.CombineLatest(this.WhenAnyValue(pane => pane.Groups), (row, shown) => (Row: row, Groups: shown))
			.Subscribe(selection => ShowMembershipsOf(selection.Row, selection.Groups)));

		_message = this.WhenAnyValue(pane => pane.PendingRename)
			.Select(group => group?.WhenAnyValue(pending => pending.Message) ?? Observable.Return(string.Empty))
			.Switch()
			.CombineLatest(_penGroupWriter.WhenAnyValue(writer => writer.Refusal), FirstNonEmpty)
			.ToProperty(this, pane => pane.Message);
		_disposables.Add(_message);

		var hasNewGroupName = this.WhenAnyValue(pane => pane.NewGroupName)
			.Select(name => !string.IsNullOrWhiteSpace(name));
		var hasPendingRename = this.WhenAnyValue(pane => pane.PendingRename)
			.Select(group => group is not null);
		var hasPendingDeletion = this.WhenAnyValue(pane => pane.PendingDeletion)
			.Select(deletion => deletion is not null);

		_disposables.Add(CreateGroupCommand = ReactiveCommand.CreateFromTask(CreateGroupAsync, hasNewGroupName));
		_disposables.Add(CloseRenameCommand = ReactiveCommand.Create(CloseRename, hasPendingRename));
		_disposables.Add(ConfirmDeleteCommand = ReactiveCommand.CreateFromTask(ConfirmDeleteAsync, hasPendingDeletion));
		_disposables.Add(CancelDeleteCommand = ReactiveCommand.Create(CancelDelete, hasPendingDeletion));
	}

	/// <summary>Replaced whole on create and on delete; a rename keeps the group's place.</summary>
	public IReadOnlyList<PenGroupViewModel> Groups
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public string NewGroupName
	{
		get;
		set => this.RaiseAndSetIfChanged(ref field, value);
	} = string.Empty;

	/// <summary>The group the rename block edits; null while the block shows no rename.</summary>
	public PenGroupViewModel? PendingRename
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public PendingGroupDeletion? PendingDeletion
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>
	/// The renamed group's message while the rename block is open; otherwise the last refusal of a pane write;
	/// otherwise empty.
	/// </summary>
	public string Message => _message.Value;

	/// <summary>Creates a group named as typed; a refused name stays in the field.</summary>
	public ReactiveCommand<Unit, Unit> CreateGroupCommand { get; }

	/// <summary>Closes the rename block, but only while its draft is valid, so a refused draft stays in view.</summary>
	public ReactiveCommand<Unit, Unit> CloseRenameCommand { get; }

	public ReactiveCommand<Unit, Unit> ConfirmDeleteCommand { get; }

	public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }

	public void Dispose()
	{
		_disposables.Dispose();

		foreach (var membership in _memberships.Values)
		{
			membership.Dispose();
		}

		foreach (var group in Groups)
		{
			group.Dispose();
		}
	}

	private PenGroupViewModel GroupOf(StoredGroup group)
	{
		return new PenGroupViewModel(group, _penGroupWriter, AskToRename, AskToDelete);
	}

	private void ShowMembershipsOf(PenRowViewModel? row, IReadOnlyList<PenGroupViewModel> groups)
	{
		foreach (var group in groups)
		{
			group.Membership = row is null ? null : MembershipOf(row, group);
		}
	}

	// docs/architecture/overview.md#a-field-writes-when-its-edit-ends
	private PenMembershipViewModel MembershipOf(PenRowViewModel row, PenGroupViewModel group)
	{
		if (!_memberships.TryGetValue((row, group), out var membership))
		{
			membership = new PenMembershipViewModel(row, group, _penGroupWriter);
			_memberships.Add((row, group), membership);
		}

		return membership;
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

		Groups = [.. Groups, GroupOf(new StoredGroup(created.Value, name, []))];

		if (NewGroupName == name)
		{
			NewGroupName = string.Empty;
		}
	}

	private void AskToRename(PenGroupViewModel group)
	{
		PendingDeletion = null;
		PendingRename = group;
	}

	private void CloseRename()
	{
		if (PendingRename is { IsNameValid: true })
		{
			PendingRename = null;
		}
	}

	private void AskToDelete(PenGroupViewModel group)
	{
		PendingRename = null;
		PendingDeletion = new PendingGroupDeletion(
			group,
			Resources.FormatPenGroupsDeleteQuestion(group.Group.Name),
			Resources.FormatPenGroupsDeleteCount(group.MemberCount));
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

		Groups = [.. Groups.Where(group => group != pending.Group)];
		DisposeMembershipsOf(pending.Group);
		pending.Group.Dispose();

		ShowGroupsOn(pending.Group.Group.MemberPenIds);
	}

	private void DisposeMembershipsOf(PenGroupViewModel group)
	{
		foreach (var (key, membership) in _memberships.Where(entry => entry.Key.Group == group).ToList())
		{
			_memberships.Remove(key);
			membership.Dispose();
		}
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

	private static string FirstNonEmpty(string first, string second)
	{
		return first.Length > 0 ? first : second;
	}
}
