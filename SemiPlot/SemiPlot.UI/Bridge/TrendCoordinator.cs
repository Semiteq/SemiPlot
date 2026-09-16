using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Bridge;

public sealed class TrendCoordinator : IDisposable
{
	private static readonly TimeSpan _defaultBatchWindow = TimeSpan.FromMilliseconds(100);
	private readonly TimeSpan _batchWindow;

	private readonly IDataProvider _dataProvider;
	private readonly IScheduler _dataScheduler;
	private readonly IScheduler _uiScheduler;
	private bool _isDisposed;

	private IDisposable? _realtimeSubscription;

	// Own subject so disposal stops forwarding to every consumer.
	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

	// Written from the data scheduler (a failed window, a provider that ends the stream) and completed from
	// the UI thread at disposal, so the subject serializes its callers.
	private readonly ISubject<Exception> _realtimeFailures = Subject.Synchronize(new Subject<Exception>());

	private readonly IDisposable _connectionSubscription;

	// pens must be dataProvider's own catalogue: the coordinator subscribes to these identifiers without
	// asking the provider whether it knows them, and a provider silently drops the ones it does not.
	public TrendCoordinator(
		IDataProvider dataProvider,
		IReadOnlyList<Pen> pens,
		IScheduler dataScheduler,
		IScheduler uiScheduler,
		TimeSpan? batchWindow = null)
	{
		_dataProvider = dataProvider;
		_dataScheduler = dataScheduler;
		_uiScheduler = uiScheduler;
		_batchWindow = batchWindow ?? _defaultBatchWindow;
		RealtimeBatches = BuildRealtimeBatches(pens);
		RealtimeFailures = _realtimeFailures.ObserveOn(_uiScheduler);
		ConnectionFaults = _connectionFaults.AsObservable();
		_connectionSubscription = dataProvider.ConnectionFaults
			.ObserveOn(_uiScheduler)
			.Subscribe(_connectionFaults.OnNext);
	}

	/// <summary>
	/// The live edge. It never faults: a provider that ends the stream is reported through
	/// <see cref="RealtimeFailures"/> and this stream completes instead.
	/// </summary>
	public IObservable<RealtimeBatch> RealtimeBatches { get; }

	/// <summary>
	/// Every realtime failure once: a throw inside the batch projection, which the pipeline skips, and a
	/// provider that ends the stream. Republished on the UI scheduler, like <see cref="ConnectionFaults"/>.
	/// </summary>
	public IObservable<Exception> RealtimeFailures { get; }

	/// <summary>
	/// The provider's connection state, republished on the UI scheduler so a view model binds to it directly.
	/// It neither completes nor faults; disposal stops it instead.
	/// </summary>
	public IObservable<ArchiveConnectionState> ConnectionFaults { get; }

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_realtimeSubscription?.Dispose();
		_realtimeSubscription = null;
		_connectionSubscription.Dispose();

		// Never disposed: a buffer flush still running on the data scheduler would throw out of
		// TryBuildRealtimeBatch, and Rx would turn that into the OnError the catch exists to prevent.
		_realtimeFailures.OnCompleted();
	}

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		// The keep-alive holds the RefCount open across a chart being replaced. The stream cannot fault, so
		// this observer has nothing to handle.
		_realtimeSubscription ??= RealtimeBatches.Subscribe();
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _dataProvider.QueryHistoryAsync(penIds, fromUtc, toUtc, layer, targetColumnCount);
	}

	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _dataProvider.QueryArchiveExtentAsync();
	}

	private IObservable<RealtimeBatch> BuildRealtimeBatches(IReadOnlyList<Pen> pens)
	{
		var penIds = pens.Select(pen => pen.PenId).ToArray();

		return _dataProvider
			.Subscribe(penIds)
			.Buffer(_batchWindow, _dataScheduler)
			.Select(TryBuildRealtimeBatch)
			.Where(batch => batch is { Timestamps.Count: > 0 })
			.Select(batch => batch!)
			.Catch<RealtimeBatch, Exception>(ReportTerminalFailure)
			.ObserveOn(_uiScheduler)
			.Publish()
			.RefCount();
	}

	private IObservable<RealtimeBatch> ReportTerminalFailure(Exception streamFailure)
	{
		_realtimeFailures.OnNext(streamFailure);

		return Observable.Empty<RealtimeBatch>();
	}

	private RealtimeBatch? TryBuildRealtimeBatch(IList<IReadOnlyList<Sample>> window)
	{
		try
		{
			return BuildRealtimeBatch(window);
		}
		catch (Exception buildFailure)
		{
			_realtimeFailures.OnNext(buildFailure);

			return null;
		}
	}

	private static RealtimeBatch BuildRealtimeBatch(IList<IReadOnlyList<Sample>> window)
	{
		var samples = window.SelectMany(batch => batch).ToArray();

		var timestamps = samples.Select(sample => sample.TimestampUtc).Distinct().OrderBy(time => time).ToArray();

		var pens = samples
			.GroupBy(sample => sample.PenId)
			.Select(BuildPenValues)
			.ToArray();

		return new RealtimeBatch(timestamps, pens);
	}

	// A pen carries only the samples it has; a filler null would draw a break the archive never recorded.
	private static PenRealtimeValues BuildPenValues(IGrouping<int, Sample> penSamples)
	{
		var ordered = penSamples.OrderBy(sample => sample.TimestampUtc).ToArray();

		return new PenRealtimeValues(
			penSamples.Key,
			Array.ConvertAll(ordered, sample => sample.TimestampUtc),
			Array.ConvertAll(ordered, sample => sample.Value));
	}
}
