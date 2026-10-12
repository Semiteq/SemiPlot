using SemiPlot.Core.Data;

namespace SemiPlot.Tests.Integration;

/// <summary>The nth Connected is the nth subscription's armed point; one caller awaits at a time.</summary>
internal sealed class ArmedGate : IDisposable
{
	private readonly List<ArchiveConnectionState> _states = [];

	private readonly Lock _guard = new();

	private readonly IDisposable _subscription;

	private (int Count, TaskCompletionSource Reached)? _waiter;

	private int _connectedCount;

	public ArmedGate(IObservable<ArchiveConnectionState> states)
	{
		_subscription = states.Subscribe(Record);
	}

	public bool NoFaultWasSeen
	{
		get
		{
			lock (_guard)
			{
				return _states.All(state => state.IsConnected);
			}
		}
	}

	public Task Armed(int subscriptionCount)
	{
		lock (_guard)
		{
			if (_connectedCount >= subscriptionCount)
			{
				return Task.CompletedTask;
			}

			var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_waiter = (subscriptionCount, waiter);

			return waiter.Task;
		}
	}

	public void Dispose()
	{
		_subscription.Dispose();
	}

	private void Record(ArchiveConnectionState state)
	{
		lock (_guard)
		{
			_states.Add(state);

			if (!state.IsConnected)
			{
				return;
			}

			_connectedCount++;

			if (_waiter is { } waiter && waiter.Count <= _connectedCount)
			{
				waiter.Reached.TrySetResult();
				_waiter = null;
			}
		}
	}
}
