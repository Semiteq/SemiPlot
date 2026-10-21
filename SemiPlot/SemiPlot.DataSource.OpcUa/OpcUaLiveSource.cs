using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.DataSource.OpcUa.Configuration;

namespace SemiPlot.DataSource.OpcUa;

/// <summary>
/// Serves the live edge from the SCADA's OPC UA server over one session per process, started by the first
/// <see cref="Subscribe"/> (docs/architecture/data-integration.md#the-opc-ua-live-source).
/// </summary>
public sealed class OpcUaLiveSource : ILiveSource, IAsyncDisposable, IDisposable
{
	/// <summary>A server that starts beside the viewer answers within it, so its start writes no fault.</summary>
	internal static readonly TimeSpan Grace = TimeSpan.FromSeconds(30);

	// Covers the session's 2 s close bound; a close that outlives it finishes on the pool.
	private static readonly TimeSpan _disposeBound = TimeSpan.FromSeconds(3);

	private readonly ISubject<LiveSourceState> _states =
		Subject.Synchronize(new BehaviorSubject<LiveSourceState>(LiveSourceState.Connecting));

	private readonly ISubject<LiveSourceError> _penFaults = Subject.Synchronize(new Subject<LiveSourceError>());

	private readonly OpcUaSession _session;
	private readonly ILogger<OpcUaLiveSource> _logger;
	private readonly Lock _gate = new();
	private bool _isStarted;
	private bool _isDisposed;

	internal OpcUaLiveSource(OpcUaConnectionSettings settings, ILoggerFactory loggerFactory, TimeSpan grace)
	{
		_session = new OpcUaSession(settings, loggerFactory, grace, _states, _penFaults);
		_logger = loggerFactory.CreateLogger<OpcUaLiveSource>();
		States = _states.AsObservable();
		PenFaults = _penFaults.AsObservable();
	}

	public IObservable<LiveSourceState> States { get; }

	public IObservable<LiveSourceError> PenFaults { get; }

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)
	{
		return Observable.Create<IReadOnlyList<Sample>>(observer =>
		{
			StartOnce();

			return _session.Open(penIds, observer);
		});
	}

	public ValueTask DisposeAsync()
	{
		return new ValueTask(DisposeCoreAsync());
	}

	/// <summary>
	/// The container disposes synchronously when <c>Main</c> returns; the close runs on the pool, so no continuation
	/// needs a <see cref="SynchronizationContext"/> the caller still holds.
	/// </summary>
	public void Dispose()
	{
		var closing = Task.Run(DisposeCoreAsync);

		if (!closing.Wait(_disposeBound))
		{
			LogLateFault(closing);
		}
	}

	private void StartOnce()
	{
		lock (_gate)
		{
			if (_isStarted || _isDisposed)
			{
				return;
			}

			_isStarted = true;
			_session.Start();
		}
	}

	/// <summary>A close that outlived the bound still reaches the log if it fails.</summary>
	private void LogLateFault(Task closing)
	{
		// OnlyOnFaulted, so failed.Exception is never null.
		_ = closing.ContinueWith(
			failed => _logger.LogError(failed.Exception!.GetBaseException(), "Closing the OPC UA live source failed"),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	private async Task DisposeCoreAsync()
	{
		lock (_gate)
		{
			if (_isDisposed)
			{
				return;
			}

			_isDisposed = true;
		}

		try
		{
			await _session.DisposeAsync().ConfigureAwait(false);
		}
		finally
		{
			_states.OnCompleted();
			_penFaults.OnCompleted();
		}
	}
}
