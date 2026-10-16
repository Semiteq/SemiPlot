using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Markers;

/// <summary>
/// Re-reads the stored markers and runs the chart's marker writes one at a time, reporting every failure to the
/// panel. Everything runs on the UI thread, unlocked.
/// </summary>
public sealed class MarkerSync : IMarkerChannel, IDisposable
{
	private readonly IMarkerStore _markerStore;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<MarkerSync> _logger;
	private readonly Subject<IReadOnlyList<StoredMarker>> _reads = new();
	private readonly ArchiveReadLoop<GenerationRead> _readLoop;
	private readonly ArchiveCallQueue _archiveCallQueue;

	private int _writeGeneration;

	public MarkerSync(
		IMarkerStore markerStore,
		MessagePanelViewModel messagePanel,
		IScheduler uiScheduler,
		ILogger<MarkerSync> logger)
	{
		_markerStore = markerStore;
		_messagePanel = messagePanel;
		_logger = logger;
		_readLoop = new ArchiveReadLoop<GenerationRead>(
			ReadAsync, Apply, "marker", messagePanel, uiScheduler, logger);
		_archiveCallQueue = new ArchiveCallQueue(() => _writeGeneration++);
	}

	public IObservable<IReadOnlyList<StoredMarker>> Reads => _reads.AsObservable();

	/// <summary>Starts the loop; its first read comes one interval later unless asked sooner.</summary>
	public void Start()
	{
		_readLoop.Start();
	}

	/// <inheritdoc cref="ArchiveReadLoop{T}.ReadNow"/>
	public void ReadNow()
	{
		_readLoop.ReadNow();
	}

	public Task<Result<long>> AddAsync(DateTime timeUtc, string? label)
	{
		return WriteAsync(() => _markerStore.AddAsync(timeUtc, label));
	}

	public Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc)
	{
		return WriteAsync(() => _markerStore.MoveAsync(marker, timeUtc));
	}

	public Task<Result> RenameAsync(StoredMarker marker, string? label)
	{
		return WriteAsync(() => _markerStore.RenameAsync(marker, label));
	}

	public Task<Result> DeleteAsync(StoredMarker marker)
	{
		return WriteAsync(() => _markerStore.DeleteAsync(marker));
	}

	public void Dispose()
	{
		_readLoop.Dispose();
		_reads.OnCompleted();
	}

	private async Task<T> WriteAsync<T>(Func<Task<T>> write)
		where T : IResultBase
	{
		try
		{
			var written = await _archiveCallQueue.RunAsync(write);
			_messagePanel.ReportFailure(written, _logger);

			return written;
		}
		finally
		{
			_readLoop.ReadNow();
		}
	}

	private async Task<Result<GenerationRead>> ReadAsync(
		CancellationToken stopped)
	{
		var generation = _writeGeneration;
		var read = await _markerStore.ReadAsync(stopped);

		return read.Map(markers => new GenerationRead(generation, markers));
	}

	private void Apply(GenerationRead read)
	{
		var isOvertakenByAWrite = read.Generation != _writeGeneration;

		if (!isOvertakenByAWrite)
		{
			_reads.OnNext(read.Markers);
		}
	}

	/// <summary>One read and the write generation it started under.</summary>
	private readonly record struct GenerationRead(int Generation, IReadOnlyList<StoredMarker> Markers);
}
