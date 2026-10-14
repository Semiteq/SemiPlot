using FluentResults;

using SemiPlot.Core.Data;

namespace SemiPlot.Tests.Unit.UI.Export;

internal sealed class FakeArchiveRowReader : IArchiveRowReader
{
	private readonly Lock _gate = new();
	private readonly List<CountCall> _countCalls = [];
	private readonly List<SeedCall> _seedCalls = [];
	private readonly List<SliceCall> _sliceCalls = [];

	public IReadOnlyList<CountCall> CountCalls => Snapshot(_countCalls);

	public IReadOnlyList<SeedCall> SeedCalls => Snapshot(_seedCalls);

	public IReadOnlyList<SliceCall> SliceCalls => Snapshot(_sliceCalls);

	public Func<CountCall, Task<Result<long>>> OnCount { get; set; } =
		_ => Task.FromResult(Result.Ok(0L));

	public Func<SeedCall, Task<Result<IReadOnlyList<StoredArchiveRow>>>> OnSeeds { get; set; } =
		_ => Task.FromResult(Result.Ok<IReadOnlyList<StoredArchiveRow>>([]));

	public Func<SliceCall, Task<Result<IReadOnlyList<StoredArchiveRow>>>> OnSlice { get; set; } =
		_ => Task.FromResult(Result.Ok<IReadOnlyList<StoredArchiveRow>>([]));

	public Task<Result<long>> CountRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default)
	{
		var call = new CountCall(penIds, from, to, cancellationToken);

		Record(_countCalls, call);

		return OnCount(call);
	}

	public Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSeedRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, CancellationToken cancellationToken = default)
	{
		var call = new SeedCall(penIds, from, cancellationToken);

		Record(_seedCalls, call);

		return OnSeeds(call);
	}

	public Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSliceAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default)
	{
		var call = new SliceCall(penIds, from, to, cancellationToken);

		Record(_sliceCalls, call);

		return OnSlice(call);
	}

	private IReadOnlyList<T> Snapshot<T>(List<T> calls)
	{
		lock (_gate)
		{
			return [.. calls];
		}
	}

	private void Record<T>(List<T> calls, T call)
	{
		lock (_gate)
		{
			calls.Add(call);
		}
	}

	public sealed record CountCall(IReadOnlyList<int> PenIds, DateTime From, DateTime To, CancellationToken Token);

	public sealed record SeedCall(IReadOnlyList<int> PenIds, DateTime From, CancellationToken Token);

	public sealed record SliceCall(IReadOnlyList<int> PenIds, DateTime From, DateTime To, CancellationToken Token);
}
