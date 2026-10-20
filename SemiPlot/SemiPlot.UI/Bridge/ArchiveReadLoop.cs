using System.Globalization;
using System.Reactive.Concurrency;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Bridge;

/// <summary>What every <see cref="ArchiveReadLoop{T}"/> shares.</summary>
internal static class ArchiveReadLoop
{
	/// <summary>The wait between two reads when no read is asked for sooner.</summary>
	public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(5);
}

/// <summary>
/// Re-reads one archive source every <see cref="ArchiveReadLoop.ReadInterval"/> or on <see cref="ReadNow"/>, applies
/// each success and reports any other failure than a lost connection from the third in a row. A lost connection only
/// logs; the catalogue loop's state sink carries it to the bar. Runs on the UI thread, unlocked.
/// </summary>
internal sealed class ArchiveReadLoop<T>(
	Func<CancellationToken, Task<Result<T>>> read,
	Action<T> apply,
	string readName,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	ILogger logger,
	Action<ArchiveConnectionState>? reportConnectionState) : IDisposable
{
	private readonly Func<CancellationToken, Task<Result<T>>> _read = read;
	private readonly Action<T> _apply = apply;
	private readonly string _readName = readName;
	private readonly MessagePanelViewModel _messagePanel = messagePanel;
	private readonly IScheduler _uiScheduler = uiScheduler;
	private readonly ILogger _logger = logger;
	private readonly Action<ArchiveConnectionState>? _reportConnectionState = reportConnectionState;

	private IDisposable? _loop;
	private TaskCompletionSource _wake = new();
	private int _consecutiveFailures;
	private int _consecutiveConnectionFailures;
	private ArchiveConnectionState? _lastReportedState;

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
		_consecutiveConnectionFailures = 0;
		ReportConnected();
		_apply(read.Value);
	}

	private void ReportConnected()
	{
		if (_reportConnectionState is null || _lastReportedState is { IsConnected: true })
		{
			return;
		}

		_lastReportedState = ArchiveConnectionState.Connected;
		_reportConnectionState(_lastReportedState);
	}

	private void ReportFailedRead(Result<T> read)
	{
		_consecutiveFailures++;

		if (read.Errors[0] is ArchiveError { Kind: ArchiveFault.Unreachable } connectionFailure)
		{
			_consecutiveConnectionFailures++;
			_logger.LogWarning(
				"The {Read} read failed for lack of a connection, {ConsecutiveConnectionFailures} in a row: {Reason}",
				_readName,
				_consecutiveConnectionFailures,
				connectionFailure.Message);
			RaiseFaultOnceReached(connectionFailure);

			return;
		}

		_consecutiveConnectionFailures = 0;

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

	private void RaiseFaultOnceReached(ArchiveError connectionFailure)
	{
		var isRunComplete = _consecutiveConnectionFailures >= ArchiveConnectionState.ConsecutiveFailuresBeforeFault;

		if (_reportConnectionState is null || !isRunComplete || _lastReportedState is { IsConnected: false })
		{
			return;
		}

		_lastReportedState = new ArchiveConnectionState(new ArchiveError(
			ArchiveFault.ConnectionLost,
			connectionFailure.Host,
			connectionFailure.Port,
			connectionFailure.Database,
			ArchiveConnectionState.ConsecutiveFailuresBeforeFault.ToString(CultureInfo.InvariantCulture)));
		_reportConnectionState(_lastReportedState);
	}
}
