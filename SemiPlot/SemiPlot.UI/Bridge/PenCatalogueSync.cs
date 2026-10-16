using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Bridge;

/// <summary>
/// Re-reads the pen catalogue and emits what changed since the last successful read, the first time against
/// the catalogue the start sequence read. Everything runs on the UI thread, unlocked.
/// </summary>
public sealed class PenCatalogueSync : IDisposable
{
	private readonly Subject<PenListDelta> _deltas = new();
	private readonly ArchiveReadLoop<IReadOnlyList<Pen>> _readLoop;

	private IReadOnlyList<Pen> _snapshot;

	public PenCatalogueSync(
		IDataProvider dataProvider,
		IReadOnlyList<Pen> pens,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<PenCatalogueSync> logger)
	{
		_snapshot = pens;
		_readLoop = new ArchiveReadLoop<IReadOnlyList<Pen>>(
			_ => dataProvider.QueryPensAsync(), Apply, "pen catalogue", messagePanel, uiScheduler, logger);
	}

	/// <summary>Every read that changed something, on the UI thread, in the order read.</summary>
	public IObservable<PenListDelta> Deltas => _deltas.AsObservable();

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
	public void Rebase(IReadOnlyList<Pen> shown)
	{
		_snapshot = shown;
	}

	public void Dispose()
	{
		_readLoop.Dispose();
		_deltas.OnCompleted();
	}

	private void Apply(IReadOnlyList<Pen> read)
	{
		var delta = PenListDelta.Between(_snapshot, read);
		_snapshot = read;

		if (!delta.IsEmpty)
		{
			_deltas.OnNext(delta);
		}
	}
}
