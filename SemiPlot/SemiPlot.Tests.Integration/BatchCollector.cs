namespace SemiPlot.Tests.Integration;

/// <summary>The gate is taken before the triggering row is written and awaited after it.</summary>
internal sealed class BatchCollector<T> : IDisposable
{
	private readonly List<T> _batches = [];

	private readonly Lock _guard = new();

	private readonly IDisposable _subscription;

	private TaskCompletionSource? _nextBatch;

	private (int Count, TaskCompletionSource Reached)? _countWaiter;

	public BatchCollector(IObservable<T> batches)
	{
		_subscription = batches.Subscribe(Collect);
	}

	public IReadOnlyList<T> Batches
	{
		get
		{
			lock (_guard)
			{
				return [.. _batches];
			}
		}
	}

	public Task NextBatch()
	{
		lock (_guard)
		{
			var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_nextBatch = gate;

			return gate.Task;
		}
	}

	public Task Reached(int count)
	{
		lock (_guard)
		{
			if (_batches.Count >= count)
			{
				return Task.CompletedTask;
			}

			if (_countWaiter is not null)
			{
				throw new InvalidOperationException("A second Reached waiter would replace the pending one.");
			}

			var gate = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_countWaiter = (count, gate);

			return gate.Task;
		}
	}

	public void Dispose()
	{
		_subscription.Dispose();
	}

	private void Collect(T batch)
	{
		lock (_guard)
		{
			_batches.Add(batch);

			_nextBatch?.TrySetResult();

			if (_countWaiter is { } waiter && _batches.Count >= waiter.Count)
			{
				waiter.Reached.TrySetResult();
				_countWaiter = null;
			}
		}
	}
}
