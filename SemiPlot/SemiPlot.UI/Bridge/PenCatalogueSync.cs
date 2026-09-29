using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Bridge;

/// <summary>
/// Re-reads the pen catalogue and emits what changed since the last successful read, the first time against
/// <paramref name="pens"/>, the catalogue the start sequence read. Everything runs on the UI thread, unlocked.
/// </summary>
public sealed class PenCatalogueSync(
	IDataProvider dataProvider,
	IReadOnlyList<Pen> pens,
	MessagePanelViewModel messagePanel,
	IScheduler uiScheduler,
	ILogger<PenCatalogueSync> logger) : IDisposable
{
	/// <summary>The wait between two reads when no read is asked for sooner.</summary>
	public static readonly TimeSpan ReadInterval = TimeSpan.FromSeconds(5);

	private readonly IDataProvider _dataProvider = dataProvider;
	private readonly MessagePanelViewModel _messagePanel = messagePanel;
	private readonly IScheduler _uiScheduler = uiScheduler;
	private readonly ILogger<PenCatalogueSync> _logger = logger;
	private readonly Subject<PenListDelta> _deltas = new();

	private IReadOnlyList<Pen> _snapshot = pens;
	private IDisposable? _loop;
	private TaskCompletionSource _wake = new();
	private int _consecutiveFailures;

	/// <summary>Every read that changed something, on the UI thread, in the order read.</summary>
	public IObservable<PenListDelta> Deltas => _deltas.AsObservable();

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

	/// <summary>The next read is compared against <paramref name="shown"/> instead of the last read.</summary>
	public void Rebase(IReadOnlyList<Pen> shown)
	{
		_snapshot = shown;
	}

	public void Dispose()
	{
		_loop?.Dispose();
		_deltas.OnCompleted();
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
			catch (Exception readFailure)
			{
				_messagePanel.TryReportFailure(new ExceptionalError(readFailure), _logger);
			}
		}
	}

	private async Task WaitAsync(IScheduler scheduler)
	{
		var wake = _wake;

		using (scheduler.Schedule(ReadInterval, () => wake.TrySetResult()))
		{
			await wake.Task;
		}

		_wake = new TaskCompletionSource();
	}

	private async Task ReadAsync(CancellationToken stopped)
	{
		var read = await _dataProvider.QueryPensAsync();

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
		var delta = PenListDelta.Between(_snapshot, read.Value);
		_snapshot = read.Value;

		if (!delta.IsEmpty)
		{
			_deltas.OnNext(delta);
		}
	}

	private void ReportFailedRead(Result<IReadOnlyList<Pen>> read)
	{
		_consecutiveFailures++;

		if (_consecutiveFailures < ArchiveConnectionState.ConsecutiveFailuresBeforeFault)
		{
			_logger.LogWarning(
				"The pen catalogue read failed, {ConsecutiveFailures} in a row: {Reason}",
				_consecutiveFailures,
				read.Errors[0].Message);

			return;
		}

		_messagePanel.ReportFailure(read, _logger);
	}
}
