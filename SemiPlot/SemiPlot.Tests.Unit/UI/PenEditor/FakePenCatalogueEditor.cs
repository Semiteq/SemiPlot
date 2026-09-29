using FluentResults;

using Microsoft.Extensions.Logging.Abstractions;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Messages;
using SemiPlot.UI.PenEditor;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

/// <summary>One call the fake editor received, with the arguments it was handed.</summary>
internal abstract record FakeEditorCall
{
	private FakeEditorCall()
	{
	}

	public sealed record Read : FakeEditorCall;

	public sealed record RegisterNewPens : FakeEditorCall;

	public sealed record Change(StoredPen Pen, PenSettingChange Setting) : FakeEditorCall;

	public sealed record CreateGroup(string Name) : FakeEditorCall;

	public sealed record RenameGroup(StoredGroup Group, string Name) : FakeEditorCall;

	public sealed record DeleteGroup(StoredGroup Group) : FakeEditorCall;

	public sealed record SetMembership(StoredPen Pen, StoredGroup Group, bool IsMember) : FakeEditorCall;
}

/// <summary>
/// Records every call in the order received and answers each with the result its property holds when the
/// call is answered. A call made after <see cref="HoldNextCall"/> waits on the returned gate.
/// </summary>
internal sealed class FakePenCatalogueEditor : IPenCatalogueEditor
{
	private readonly Queue<TaskCompletionSource> _gates = [];

	public List<FakeEditorCall> Calls { get; } = [];

	public IEnumerable<FakeEditorCall.Change> Changes => Calls.OfType<FakeEditorCall.Change>();

	public Result<PenCatalogue> ReadResult { get; set; } = Result.Ok(new PenCatalogue([], []));

	public Result<int> RegisterResult { get; set; } = Result.Ok(0);

	public Result ChangeResult { get; set; } = Result.Ok();

	public Result<int> CreateGroupResult { get; set; } = Result.Ok(1);

	public Result RenameGroupResult { get; set; } = Result.Ok();

	public Result DeleteGroupResult { get; set; } = Result.Ok();

	public Result MembershipResult { get; set; } = Result.Ok();

	/// <summary>
	/// Holds the next call until the test completes the gate. Awaited by production code and completed by the
	/// test, so the gate takes no RunContinuationsAsynchronously: completing it resumes the held call inline.
	/// </summary>
	public TaskCompletionSource HoldNextCall()
	{
		var gate = new TaskCompletionSource();
		_gates.Enqueue(gate);

		return gate;
	}

	/// <summary>A refusal the fake can answer with, against the bench archive.</summary>
	public static ArchiveError Refusal(ArchiveFault fault, string detail)
	{
		return new ArchiveError(fault, "bench", 5432, "semiplot_dev", detail);
	}

	/// <summary>The editor window's view model over the catalogue, writing through this fake.</summary>
	public PenEditorViewModel EditorOver(PenCatalogue catalogue, MessagePanelViewModel messagePanel)
	{
		return new PenEditorViewModel(
			this, catalogue, messagePanel, NullLogger<PenEditorViewModel>.Instance, () => { });
	}

	public Task<Result<PenCatalogue>> ReadAsync()
	{
		return AnswerAsync(new FakeEditorCall.Read(), () => ReadResult);
	}

	public Task<Result<int>> RegisterNewPensAsync()
	{
		return AnswerAsync(new FakeEditorCall.RegisterNewPens(), () => RegisterResult);
	}

	public Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change)
	{
		return AnswerAsync(new FakeEditorCall.Change(pen, change), () => ChangeResult);
	}

	public Task<Result<int>> CreateGroupAsync(string name)
	{
		return AnswerAsync(new FakeEditorCall.CreateGroup(name), () => CreateGroupResult);
	}

	public Task<Result> RenameGroupAsync(StoredGroup group, string name)
	{
		return AnswerAsync(new FakeEditorCall.RenameGroup(group, name), () => RenameGroupResult);
	}

	public Task<Result> DeleteGroupAsync(StoredGroup group)
	{
		return AnswerAsync(new FakeEditorCall.DeleteGroup(group), () => DeleteGroupResult);
	}

	public Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember)
	{
		return AnswerAsync(new FakeEditorCall.SetMembership(pen, group, isMember), () => MembershipResult);
	}

	private async Task<T> AnswerAsync<T>(FakeEditorCall call, Func<T> answer)
	{
		Calls.Add(call);

		if (_gates.TryDequeue(out var gate))
		{
			await gate.Task;
		}

		return answer();
	}
}
