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

	private readonly IDataProvider _dataProvider;
	private readonly IScheduler _dataScheduler;
	private readonly IScheduler _uiScheduler;
	private readonly TimeSpan _batchWindow;
	private readonly BehaviorSubject<IReadOnlyList<int>> _penIds;

	// Own subject so disposal stops forwarding to every consumer.
	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

	// Written from the data scheduler (a failed window, a provider that ends the stream) and completed from
	// the UI thread at disposal, so the subject serializes its callers.
	private readonly ISubject<Exception> _realtimeFailures = Subject.Synchronize(new Subject<Exception>());

	private readonly IDisposable _connectionSubscription;

	// Every pen set, pens here and each one SetPens pushes, must be dataProvider's own catalogue: the
	// coordinator subscribes to these identifiers without asking the provider whether it knows them, and a
	// provider silently drops the ones it does not.
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
		_penIds = new BehaviorSubject<IReadOnlyList<int>>([.. pens.Select(pen => pen.PenId)]);
		RealtimeBatches = BuildRealtimeBatches();
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
	/// It never faults; disposal completes it.
	/// </summary>
	public IObservable<ArchiveConnectionState> ConnectionFaults { get; }

	/// <summary>The pen set the live edge follows: the constructor's, or the last one handed to SetPens.</summary>
	public IReadOnlyList<int> PenIds => _penIds.Value;

	public void Dispose()
	{
		_connectionSubscription.Dispose();

		// Never disposed: a buffer flush still running on the data scheduler would throw out of
		// TryBuildRealtimeBatch, and Rx would turn that into the OnError the catch exists to prevent.
		_realtimeFailures.OnCompleted();
		_penIds.OnCompleted();
		_connectionFaults.OnCompleted();
	}

	/// <summary>Moves the live edge onto this pen set; the history query covers rows the switch skips.</summary>
	public void SetPens(IReadOnlyList<int> penIds)
	{
		_penIds.OnNext(penIds);
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount,
		CancellationToken cancellationToken = default)
	{
		return _dataProvider.QueryHistoryAsync(penIds, fromUtc, toUtc, layer, targetColumnCount, cancellationToken);
	}

	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		return _dataProvider.QueryArchiveExtentAsync();
	}

	private IObservable<RealtimeBatch> BuildRealtimeBatches()
	{
		return _penIds
			.Select(_dataProvider.Subscribe)
			.Switch()
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
