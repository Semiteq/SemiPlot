using Microsoft.Extensions.Logging;

namespace SemiPlot.Tests.Integration;

/// <summary>
/// Counts, across every logger of the container, the poll's baseline lines (the nth is the nth armed point) and
/// the highest run of failed ticks it logs; one caller awaits each count at a time.
/// </summary>
internal sealed class ArmedGate(ILoggerProvider? inner = null) : ILoggerProvider
{
	public const string BaselineKey = "SubscribedPenCount";

	public const string FailureRunKey = "ConsecutiveFailures";

	private readonly Lock _guard = new();

	private int _baselineLineCount;

	private int _highestConsecutiveFailures;

	private (int Count, TaskCompletionSource Reached)? _armedWaiter;

	private (int Count, TaskCompletionSource Reached)? _failureWaiter;

	public int HighestConsecutiveFailures
	{
		get
		{
			lock (_guard)
			{
				return _highestConsecutiveFailures;
			}
		}
	}

	public Task Armed(int baselineLineCount)
	{
		lock (_guard)
		{
			if (_baselineLineCount >= baselineLineCount)
			{
				return Task.CompletedTask;
			}

			var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_armedWaiter = (baselineLineCount, waiter);

			return waiter.Task;
		}
	}

	public Task FailuresReached(int consecutiveFailures)
	{
		lock (_guard)
		{
			if (_highestConsecutiveFailures >= consecutiveFailures)
			{
				return Task.CompletedTask;
			}

			var waiter = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

			_failureWaiter = (consecutiveFailures, waiter);

			return waiter.Task;
		}
	}

	public ILogger CreateLogger(string categoryName)
	{
		return new ForwardingLogger(this, inner?.CreateLogger(categoryName));
	}

	// The inner provider belongs to the caller that passed it in.
	public void Dispose()
	{
	}

	private void Record<TState>(TState state)
	{
		if (state is not IReadOnlyList<KeyValuePair<string, object?>> values)
		{
			return;
		}

		lock (_guard)
		{
			foreach (var (key, value) in values)
			{
				if (key == BaselineKey)
				{
					_baselineLineCount++;
					_armedWaiter = Release(_armedWaiter, _baselineLineCount);
				}
				else if (key == FailureRunKey && value is int consecutiveFailures)
				{
					_highestConsecutiveFailures = Math.Max(_highestConsecutiveFailures, consecutiveFailures);
					_failureWaiter = Release(_failureWaiter, _highestConsecutiveFailures);
				}
			}
		}
	}

	private static (int Count, TaskCompletionSource Reached)? Release(
		(int Count, TaskCompletionSource Reached)? waiter,
		int reached)
	{
		if (waiter is not { } pending || pending.Count > reached)
		{
			return waiter;
		}

		pending.Reached.TrySetResult();

		return null;
	}

	private sealed class ForwardingLogger(ArmedGate gate, ILogger? inner) : ILogger
	{
		public IDisposable? BeginScope<TState>(TState state)
			where TState : notnull
		{
			return inner?.BeginScope(state);
		}

		public bool IsEnabled(LogLevel logLevel)
		{
			return true;
		}

		public void Log<TState>(
			LogLevel logLevel,
			EventId eventId,
			TState state,
			Exception? exception,
			Func<TState, Exception?, string> formatter)
		{
			gate.Record(state);
			inner?.Log(logLevel, eventId, state, exception, formatter);
		}
	}
}
