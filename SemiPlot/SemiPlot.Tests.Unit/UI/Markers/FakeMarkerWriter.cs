using FluentResults;

using SemiPlot.Core.Data;

namespace SemiPlot.Tests.Unit.UI.Markers;

/// <summary>One write a fake received, with the arguments it was handed.</summary>
internal abstract record FakeMarkerWrite
{
	private FakeMarkerWrite()
	{
	}

	public sealed record Add(DateTime TimeUtc, string? Label) : FakeMarkerWrite;

	public sealed record Move(StoredMarker Marker, DateTime TimeUtc) : FakeMarkerWrite;

	public sealed record Rename(StoredMarker Marker, string? Label) : FakeMarkerWrite;

	public sealed record Lock(StoredMarker Marker, bool IsLocked) : FakeMarkerWrite;

	public sealed record Delete(StoredMarker Marker) : FakeMarkerWrite;
}

/// <summary>
/// Answers each write with its result property as it stands at that moment; <see cref="HoldNextWrite"/> gates the
/// next write.
/// </summary>
internal abstract class FakeMarkerWriter
{
	private readonly Queue<TaskCompletionSource> _writeGates = [];

	public List<FakeMarkerWrite> Writes { get; } = [];

	public Result<long> AddResult { get; set; } = Result.Ok(1L);

	/// <summary>The answer to a move, a rename, a lock and a delete.</summary>
	public Result WriteResult { get; set; } = Result.Ok();

	/// <summary>Holds the next write until the test completes the gate.</summary>
	public TaskCompletionSource HoldNextWrite()
	{
		var gate = new TaskCompletionSource();
		_writeGates.Enqueue(gate);

		return gate;
	}

	public Task<Result<long>> AddAsync(DateTime timeUtc, string? label)
	{
		return AnswerAsync(new FakeMarkerWrite.Add(timeUtc, label), () => AddResult);
	}

	public Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc)
	{
		return AnswerAsync(new FakeMarkerWrite.Move(marker, timeUtc), () => WriteResult);
	}

	public Task<Result> RenameAsync(StoredMarker marker, string? label)
	{
		return AnswerAsync(new FakeMarkerWrite.Rename(marker, label), () => WriteResult);
	}

	public Task<Result> SetLockedAsync(StoredMarker marker, bool isLocked)
	{
		return AnswerAsync(new FakeMarkerWrite.Lock(marker, isLocked), () => WriteResult);
	}

	public Task<Result> DeleteAsync(StoredMarker marker)
	{
		return AnswerAsync(new FakeMarkerWrite.Delete(marker), () => WriteResult);
	}

	private async Task<T> AnswerAsync<T>(FakeMarkerWrite write, Func<T> answer)
	{
		Writes.Add(write);

		if (_writeGates.TryDequeue(out var gate))
		{
			await gate.Task;
		}

		return answer();
	}
}
