using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Bridge;

public sealed class TrendCoordinator : IDisposable
{
	// Realtime input is coalesced at <= 10 Hz: samples arriving within this window are batched into a
	// single columnar RealtimeBatch on the data scheduler before crossing to the UI thread.
	private static readonly TimeSpan _defaultBatchWindow = TimeSpan.FromMilliseconds(100);
	public const int DefaultTargetColumnCount = 1024;

	private readonly IDataProvider _dataProvider;
	private readonly ILogger<TrendCoordinator> _logger;
	private readonly IScheduler _dataScheduler;
	private readonly IScheduler _uiScheduler;
	private readonly TimeSpan _batchWindow;
	private readonly IObservable<RealtimeBatch> _realtimeBatches;
	private readonly Subject<TrendHistory> _historyResults = new();

	// RequestHistory/SetLayer and the history-request fields below are touched only on the UI thread.
	// The realtime Buffer runs on the data scheduler and crosses to the UI thread via ObserveOn, so it
	// never reads or writes this state.
	private IDisposable? _realtimeSubscription;
	private bool _isDisposed;

	private IReadOnlyList<long> _lastRequestedPenIds = [];
	private DateTime _lastFromUtc;
	private DateTime _lastToUtc;
	private bool _hasHistoryRequest;
	private AggregationLayer _currentLayer = AggregationLayer.Raw;

	public TrendCoordinator(
		IDataProvider dataProvider,
		ILogger<TrendCoordinator> logger,
		IScheduler dataScheduler,
		IScheduler uiScheduler,
		TimeSpan? batchWindow = null)
	{
		ArgumentNullException.ThrowIfNull(dataProvider);
		ArgumentNullException.ThrowIfNull(logger);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_dataProvider = dataProvider;
		_logger = logger;
		_dataScheduler = dataScheduler;
		_uiScheduler = uiScheduler;
		_batchWindow = batchWindow ?? _defaultBatchWindow;
		_realtimeBatches = BuildRealtimeBatches();
	}

	public IReadOnlyList<Pen> Pens => _dataProvider.Pens;

	public IObservable<RealtimeBatch> RealtimeBatches => _realtimeBatches;

	public IObservable<TrendHistory> HistoryResults => _historyResults;

	public void Start()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_realtimeSubscription ??= _realtimeBatches.Subscribe();
	}

	public void RequestHistory(IReadOnlyList<long> penIds, DateTime fromUtc, DateTime toUtc)
	{
		ArgumentNullException.ThrowIfNull(penIds);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_lastRequestedPenIds = penIds;
		_lastFromUtc = fromUtc;
		_lastToUtc = toUtc;
		_hasHistoryRequest = true;

		_ = QueryAndPublishHistoryAsync();
	}

	public void SetLayer(AggregationLayer layer)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_currentLayer = layer;

		if (_hasHistoryRequest)
		{
			_ = QueryAndPublishHistoryAsync();
		}
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

	// Pass-through to the provider's archive-extent seam (mirrors QueryHistoryAsync). The minimap view
	// model awaits this and marshals the result onto the UI scheduler, so the minimap never holds the
	// IDataProvider directly.
	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		return _dataProvider.QueryArchiveExtentAsync();
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_realtimeSubscription?.Dispose();
		_realtimeSubscription = null;
		_historyResults.Dispose();
	}

	private IObservable<RealtimeBatch> BuildRealtimeBatches()
	{
		var penIds = _dataProvider.Pens.Select(pen => pen.ProjectVarId).ToArray();

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

	// Inserts null where the pen has no sample at a timestamp so the column stays index-aligned with
	// the union timestamp grid.
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

	private async Task QueryAndPublishHistoryAsync()
	{
		var result = await _dataProvider.QueryHistoryAsync(
			_lastRequestedPenIds, _lastFromUtc, _lastToUtc, _currentLayer, DefaultTargetColumnCount);
		if (result.IsFailed)
		{
			_logger.LogWarning("History query failed: {Errors}", FormatErrors(result));
			return;
		}

		_historyResults.OnNext(new TrendHistory(_currentLayer, result.Value));
	}

	private static string FormatErrors(IResultBase result)
	{
		return string.Join("; ", result.Errors.Select(error => error.Message));
	}
}
