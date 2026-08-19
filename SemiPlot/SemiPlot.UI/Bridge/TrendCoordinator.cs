using System.Reactive.Concurrency;
using System.Reactive.Linq;

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
	}

	public IObservable<RealtimeBatch> RealtimeBatches { get; }

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_realtimeSubscription?.Dispose();
		_realtimeSubscription = null;
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
		var rowOfTimestamp = timestamps
			.Select((time, index) => (time, index))
			.ToDictionary(pair => pair.time, pair => pair.index);

		var pens = samples
			.GroupBy(sample => sample.PenId)
			.Select(group => new PenRealtimeValues(group.Key, BuildColumn(group, timestamps.Length, rowOfTimestamp)))
			.ToArray();

		return new RealtimeBatch(timestamps, pens);
	}

	private static IReadOnlyList<double?> BuildColumn(
		IEnumerable<Sample> penSamples,
		int length,
		IReadOnlyDictionary<DateTime, int> rowOfTimestamp)
	{
		var column = new double?[length];
		foreach (var sample in penSamples)
		{
			column[rowOfTimestamp[sample.TimestampUtc]] = sample.Value;
		}

		return column;
	}
}
