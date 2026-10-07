using FluentResults;

using ReactiveUI;

using SemiPlot.Core.Data;

namespace SemiPlot.UI.PenEditor;

/// <summary>
/// One row of the pen table: the pen as last written, the pen as it stands once every write queued for it
/// succeeds, and the names of its groups.
/// </summary>
public sealed class PenRowViewModel(StoredPen pen, IEnumerable<StoredGroup> groups) : ReactiveObject
{
	private const string GroupNameSeparator = ", ";

	private readonly List<PenSettingChange> _queuedChanges = [];

	public StoredPen Pen
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = pen;

	/// <summary>Every form of the row seeds its drafts from this pen and compares them with it.</summary>
	public StoredPen QueuedPen
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = pen;

	/// <summary>The names of the groups holding the pen, in the order the groups are listed.</summary>
	public string GroupsText
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = GroupsTextOf(pen, groups);

	/// <summary>Counts the change as queued until the write settles; a successful write updates the pen.</summary>
	public async Task<Result> WriteAsync(PenSettingChange change, Func<Task<Result>> write)
	{
		_queuedChanges.Add(change);
		QueuedPen = WithQueuedChanges();
		try
		{
			var written = await write();

			if (written.IsSuccess)
			{
				Pen = Applied(Pen, change);
			}

			return written;
		}
		finally
		{
			// The row's writes finish in the order queued, so the one finishing is the first equal change.
			_queuedChanges.Remove(change);
			QueuedPen = WithQueuedChanges();
		}
	}

	/// <summary>True while a write of the same setting waits in the queue or runs.</summary>
	public bool IsQueued(PenSettingChange change)
	{
		return _queuedChanges.Exists(queued => queued.GetType() == change.GetType());
	}

	/// <summary>Names the groups among these that hold the pen, after a group write the editor has made.</summary>
	public void ShowGroupsOf(IEnumerable<StoredGroup> groups)
	{
		GroupsText = GroupsTextOf(Pen, groups);
	}

	private StoredPen WithQueuedChanges()
	{
		return _queuedChanges.Aggregate(Pen, Applied);
	}

	private static StoredPen Applied(StoredPen pen, PenSettingChange change)
	{
		return change switch
		{
			PenSettingChange.Name name => pen with { Name = name.Value },
			PenSettingChange.Unit unit => pen with { Unit = unit.Value },
			PenSettingChange.Format format => pen with { Format = format.Value },
			PenSettingChange.Color color => pen with { Color = color.Value },
			PenSettingChange.LineStyle lineStyle => pen with { LineStyle = lineStyle.Value },
			PenSettingChange.EnabledOnStart enabledOnStart => pen with { EnabledOnStart = enabledOnStart.Value },
			PenSettingChange.ScaleOnStart scale
				=> pen with { ScaleMinOnStart = scale.Min, ScaleMaxOnStart = scale.Max },
			_ => throw new ArgumentOutOfRangeException(nameof(change), change, null)
		};
	}

	private static string GroupsTextOf(StoredPen pen, IEnumerable<StoredGroup> groups)
	{
		var memberOf = groups.Where(group => group.MemberPenIds.Contains(pen.Id)).Select(group => group.Name);

		return string.Join(GroupNameSeparator, memberOf);
	}
}
