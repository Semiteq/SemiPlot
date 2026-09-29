using System.Reactive;

using ReactiveUI;

namespace SemiPlot.UI.PenEditor;

/// <summary>One pen of the membership list, checked when the pen belongs to the selected group.</summary>
public sealed class PenMembershipViewModel : ReactiveObject, IDisposable
{
	private readonly PenGroupViewModel _group;
	private readonly PenGroupWriter _penGroupWriter;

	internal PenMembershipViewModel(PenRowViewModel row, PenGroupViewModel group, PenGroupWriter penGroupWriter)
	{
		Row = row;
		_group = group;
		_penGroupWriter = penGroupWriter;

		ToggleMembershipCommand = ReactiveCommand.CreateFromTask(ToggleMembershipAsync);
	}

	public PenRowViewModel Row { get; }

	public bool IsMember => _group.Group.MemberPenIds.Contains(Row.Pen.Id);

	/// <summary>The checkbox's one writer: puts the pen into the group or takes it out.</summary>
	public ReactiveCommand<Unit, Unit> ToggleMembershipCommand { get; }

	public void Dispose()
	{
		ToggleMembershipCommand.Dispose();
	}

	private async Task ToggleMembershipAsync()
	{
		var isMember = !IsMember;
		var written = await _penGroupWriter.RunAsync(
			editor => editor.SetMembershipAsync(Row.Pen, _group.Group, isMember));

		if (written.IsFailed)
		{
			_penGroupWriter.RefuseWrite(written);
		}
		else
		{
			_group.SetMembership(Row.Pen.Id, isMember);
			_penGroupWriter.ShowGroupsOn([Row.Pen.Id]);
		}

		// The checkbox flipped itself on the click; after a failure the unchanged value puts it back.
		this.RaisePropertyChanged(nameof(IsMember));
	}
}
