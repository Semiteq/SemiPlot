using FluentResults;

using SemiPlot.Core.Data;

namespace SemiPlot.Tests.Unit.UI.Markers;

/// <summary>
/// Answers a read with <see cref="ReadResult"/>; a read made after <see cref="HoldNextRead"/> waits on the returned
/// gate, and a cancelled token cancels it.
/// </summary>
internal sealed class FakeMarkerStore : FakeMarkerWriter, IMarkerStore
{
	private readonly Queue<TaskCompletionSource<Result<IReadOnlyList<StoredMarker>>>> _readGates = [];

	public int ReadCount { get; private set; }

	public Result<IReadOnlyList<StoredMarker>> ReadResult { get; set; } = Result.Ok<IReadOnlyList<StoredMarker>>([]);

	/// <summary>Holds the next read until the test completes the gate with that read's own answer.</summary>
	public TaskCompletionSource<Result<IReadOnlyList<StoredMarker>>> HoldNextRead()
	{
		var gate = new TaskCompletionSource<Result<IReadOnlyList<StoredMarker>>>();
		_readGates.Enqueue(gate);

		return gate;
	}

	public Task<Result<IReadOnlyList<StoredMarker>>> ReadAsync(CancellationToken cancellationToken = default)
	{
		ReadCount++;

		if (!_readGates.TryDequeue(out var gate))
		{
			return Task.FromResult(ReadResult);
		}

		cancellationToken.Register(() => gate.TrySetCanceled(cancellationToken));

		return gate.Task;
	}
}
