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

	// The provider's stream is hot and never terminates, so the coordinator holds its own subscription to it
	// rather than letting each consumer reach the provider directly: disposal stops the forwarding, and a
	// consumer of a disposed coordinator hears nothing further.
	private readonly Subject<ArchiveConnectionState> _connectionFaults = new();

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
		ArgumentNullException.ThrowIfNull(dataProvider);
		ArgumentNullException.ThrowIfNull(pens);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_dataProvider = dataProvider;
		_dataScheduler = dataScheduler;
		_uiScheduler = uiScheduler;
		_batchWindow = batchWindow ?? _defaultBatchWindow;
		RealtimeBatches = BuildRealtimeBatches(pens);
		ConnectionFaults = _connectionFaults.AsObservable();
		_connectionSubscription = dataProvider.ConnectionFaults
			.ObserveOn(_uiScheduler)
			.Subscribe(_connectionFaults.OnNext);
	}

	public IObservable<RealtimeBatch> RealtimeBatches { get; }

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
	}

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_realtimeSubscription ??= RealtimeBatches.Subscribe();
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(penIds);
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
			.Where(window => window.Count > 0)
			.Select(BuildRealtimeBatch)
			.Where(batch => batch is not null)
			.Select(batch => batch!)
			.ObserveOn(_uiScheduler)
			.Publish()
			.RefCount();
	}

	private static RealtimeBatch? BuildRealtimeBatch(IList<IReadOnlyList<Sample>> window)
	{
		var samples = window.SelectMany(batch => batch).ToArray();
		if (samples.Length == 0)
		{
			return null;
		}

		var timestamps = samples.Select(sample => sample.TimestampUtc).Distinct().OrderBy(time => time).ToArray();

		var pens = samples
			.GroupBy(sample => sample.PenId)
			.Select(BuildPenValues)
			.ToArray();

		return new RealtimeBatch(timestamps, pens);
	}

	// A pen carries the samples it actually has and nothing else. The batch's shared timestamp list is the
	// union of every pen's, so a column over it would need a filler at every timestamp this pen did not
	// sample — and the only filler a double? column offers is a null, which the chart draws as a break the
	// archive never recorded.
	private static PenRealtimeValues BuildPenValues(IGrouping<long, Sample> penSamples)
	{
		var ordered = penSamples.OrderBy(sample => sample.TimestampUtc).ToArray();

		return new PenRealtimeValues(
			penSamples.Key,
			Array.ConvertAll(ordered, sample => sample.TimestampUtc),
			Array.ConvertAll(ordered, sample => sample.Value));
	}
}
