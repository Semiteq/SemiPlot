using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Bridge;

/// <summary>
/// Re-reads the pen catalogue and emits each read that does not match the last successful read, the first time
/// against the catalogue the start sequence read, and the archive connection state its reads show. Runs on the
/// UI thread.
/// </summary>
public sealed class PenCatalogueSync : IDisposable
{
	private readonly Subject<CatalogueRead> _changes = new();
	private readonly Subject<ArchiveConnectionState> _connectionStates = new();
	private readonly ArchiveReadLoop<CatalogueRead> _readLoop;

	private CatalogueRead _snapshot;

	public PenCatalogueSync(
		IDataProvider dataProvider,
		CatalogueRead catalogue,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<PenCatalogueSync> logger)
	{
		_snapshot = catalogue;
		_readLoop = new ArchiveReadLoop<CatalogueRead>(
			_ => dataProvider.QueryCatalogueAsync(),
			Apply,
			"pen catalogue",
			messagePanel,
			uiScheduler,
			logger,
			_connectionStates.OnNext);
	}

	/// <summary>Every read that does not match the one before it, on the UI thread, in the order read.</summary>
	public IObservable<CatalogueRead> Changes => _changes.AsObservable();

	/// <summary>
	/// Connected at the first success and at the first success after a fault; the fault once per outage, at the
	/// <see cref="ArchiveConnectionState.ConsecutiveFailuresBeforeFault"/>th lost connection in a row. On the UI
	/// thread.
	/// </summary>
	public IObservable<ArchiveConnectionState> ConnectionStates => _connectionStates.AsObservable();

	/// <summary>Starts the loop; its first read comes one interval later.</summary>
	public void Start()
	{
		_readLoop.Start();
	}

	/// <summary>Reads at once while waiting; during a read, makes exactly one more read follow it.</summary>
	public void ReadNow()
	{
		_readLoop.ReadNow();
	}

	/// <summary>The next read is compared against <paramref name="shown"/> instead of the last read.</summary>
	public void Rebase(CatalogueRead shown)
	{
		_snapshot = shown;
	}

	public void Dispose()
	{
		_readLoop.Dispose();
		_changes.OnCompleted();
		_connectionStates.OnCompleted();
	}

	private void Apply(CatalogueRead read)
	{
		var isChanged = !_snapshot.Matches(read);
		_snapshot = read;

		if (isChanged)
		{
			_changes.OnNext(read);
		}
	}
}
