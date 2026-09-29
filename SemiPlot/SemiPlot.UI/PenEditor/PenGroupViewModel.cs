using FluentResults;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.PenEditor;

/// <summary>One group of the groups tab: the group as last written and its rename draft.</summary>
public sealed class PenGroupViewModel : ReactiveObject
{
	private readonly PenGroupWriter _penGroupWriter;
	private readonly List<string> _queuedNames = [];
	private string? _refusal;

	internal PenGroupViewModel(StoredGroup group, PenGroupWriter penGroupWriter)
	{
		_penGroupWriter = penGroupWriter;
		Group = group;
		RenameDraft = group.Name;
	}

	public StoredGroup Group
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(MemberCount));
		}
	}

	public int MemberCount => Group.MemberPenIds.Count;

	public string RenameDraft
	{
		get;
		set
		{
			if (field == value)
			{
				return;
			}

			field = value;
			this.RaisePropertyChanged();
			OnDraftChanged();
		}
	} = string.Empty;

	/// <summary>False while the draft is blank or while the last rename of this group was refused.</summary>
	public bool IsNameValid => NameRule() is null && _refusal is null;

	/// <summary>The rule the draft breaks; otherwise the last refusal; otherwise empty.</summary>
	public string Message => NameRule() ?? _refusal ?? string.Empty;

	/// <summary>The name the group holds once every rename this group has queued succeeds.</summary>
	private string QueuedName => _queuedNames.Count > 0 ? _queuedNames[^1] : Group.Name;

	/// <summary>
	/// Writes the draft as the group's name when it is valid and differs from the name as queued; a refused or
	/// failed rename reverts the draft and marks the field, but never past a later rename still queued.
	/// </summary>
	public async Task EndRenameAsync()
	{
		if (NameRule() is { } brokenRule)
		{
			Refuse(brokenRule);

			return;
		}

		if (RenameDraft == QueuedName)
		{
			return;
		}

		var name = RenameDraft;
		var renamed = await WriteAsync(name);

		if (renamed.IsFailed)
		{
			var refusal = ArchiveFailureMapper.Map(renamed.Errors[0]).Title;

			if (_queuedNames.Count > 0)
			{
				Mark(refusal);
			}
			else
			{
				Refuse(refusal);
			}

			_penGroupWriter.ReportFailure(renamed);

			return;
		}

		Group = Group with { Name = name };

		if (_refusal is not null)
		{
			_refusal = null;
			RaiseValidity();
		}

		_penGroupWriter.ShowGroupsOn(Group.MemberPenIds);
	}

	/// <summary>Records a membership the editor has written.</summary>
	internal void SetMembership(int penId, bool isMember)
	{
		var others = Group.MemberPenIds.Where(memberPenId => memberPenId != penId);

		Group = Group with { MemberPenIds = isMember ? [.. others.Append(penId).Order()] : [.. others] };
	}

	// The group's renames finish in the order queued, so the one finishing is the first equal name in the list.
	private async Task<Result> WriteAsync(string name)
	{
		_queuedNames.Add(name);
		try
		{
			return await _penGroupWriter.RunAsync(editor => editor.RenameGroupAsync(Group, name));
		}
		finally
		{
			_queuedNames.Remove(name);
		}
	}

	private void OnDraftChanged()
	{
		_refusal = null;
		RaiseValidity();
	}

	// The draft goes back first and the mark follows, so the mark survives its own revert.
	private void Refuse(string refusal)
	{
		RenameDraft = QueuedName;
		Mark(refusal);
	}

	private void Mark(string refusal)
	{
		_refusal = refusal;
		RaiseValidity();
	}

	private void RaiseValidity()
	{
		this.RaisePropertyChanged(nameof(IsNameValid));
		this.RaisePropertyChanged(nameof(Message));
	}

	private string? NameRule()
	{
		return string.IsNullOrWhiteSpace(RenameDraft) ? Resources.PenGroupsNameRequired : null;
	}
}
