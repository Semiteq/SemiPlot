using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Bridge;

/// <summary>What every <see cref="ArchiveReadLoop{T}"/> shares.</summary>
internal static class ArchiveReadLoop
{
	/// <summary>The wait between two reads when no read is asked for sooner.</summary>
	public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Re-reads one archive source every <see cref="ArchiveReadLoop.ReadInterval"/> or at once on
/// <see cref="ReadNow"/>, hands each successful read to the apply, and reports a failure from the third in a row.
/// Runs on the UI thread, unlocked.
/// </summary>
internal sealed class ArchiveReadLoop<T>(
	Func<CancellationToken, Task<Result<T>>> read,
	Action<T> apply,
	string readName,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	ILogger logger) : IDisposable
{
	private readonly Func<CancellationToken, Task<Result<T>>> _read = read;
	private readonly Action<T> _apply = apply;
	private readonly string _readName = readName;
	private readonly MessagePanelViewModel _messagePanel = messagePanel;
	private readonly IScheduler _uiScheduler = uiScheduler;
	private readonly ILogger _logger = logger;

	private IDisposable? _loop;
	private TaskCompletionSource _wake = new();
	private int _consecutiveFailures;

	/// <summary>Starts the loop; its first read comes one interval later.</summary>
	public void Start()
	{
		_loop ??= _uiScheduler.ScheduleAsync(RunAsync);
	}

	/// <summary>Reads at once while waiting; during a read, makes exactly one more read follow it.</summary>
	public void ReadNow()
	{
		_wake.TrySetResult();
	}

	public void Dispose()
	{
		_loop?.Dispose();
	}

	private async Task RunAsync(IScheduler scheduler, CancellationToken stopped)
	{
		using var stopRegistration = stopped.Register(() => _wake.TrySetResult());

		while (!stopped.IsCancellationRequested)
		{
			await WaitAsync(scheduler);

			if (stopped.IsCancellationRequested)
			{
				return;
			}

			try
			{
				await ReadAsync(stopped);
			}
			catch (OperationCanceledException) when (stopped.IsCancellationRequested)
			{
				return;
			}
			catch (Exception readFailure)
			{
				_messagePanel.TryReportFailure(new ExceptionalError(readFailure), _logger);
			}
		}
	}

	private async Task WaitAsync(IScheduler scheduler)
	{
		var wake = _wake;

		using (scheduler.Schedule(ArchiveReadLoop.ReadInterval, () => wake.TrySetResult()))
		{
			await wake.Task;
		}

		_wake = new TaskCompletionSource();
	}

	private async Task ReadAsync(CancellationToken stopped)
	{
		var read = await _read(stopped);

		if (stopped.IsCancellationRequested)
		{
			return;
		}

		if (read.IsFailed)
		{
			ReportFailedRead(read);

			return;
		}

		_consecutiveFailures = 0;
		_apply(read.Value);
	}

	private void ReportFailedRead(Result<T> read)
	{
		_consecutiveFailures++;

		if (_consecutiveFailures < ArchiveConnectionState.ConsecutiveFailuresBeforeFault)
		{
			_logger.LogWarning(
				"The {Read} read failed, {ConsecutiveFailures} in a row: {Reason}",
				_readName,
				_consecutiveFailures,
				read.Errors[0].Message);

			return;
		}

		_messagePanel.ReportFailure(read, _logger);
	}
}
