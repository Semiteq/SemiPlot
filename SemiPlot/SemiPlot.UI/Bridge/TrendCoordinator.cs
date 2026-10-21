using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Bridge;

public sealed class TrendCoordinator : IDisposable
{
	internal static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(100);

	private readonly IDataProvider _dataProvider;
	private readonly ILiveSource _liveSource;
	private readonly IScheduler _dataScheduler;
	private readonly IScheduler _uiScheduler;
	private readonly BehaviorSubject<IReadOnlyList<int>> _penIds;

	// Written from the data scheduler (a failed window, a live source that faults) and completed from
	// the UI thread at disposal, so the subject serializes its callers.
	private readonly ISubject<Exception> _realtimeFailures = Subject.Synchronize(new Subject<Exception>());

	// Every pen set, pens here and each one SetPens pushes, holds identifiers the live source serves
	// (docs/architecture/data-integration.md#realtime).
	public TrendCoordinator(
		IDataProvider dataProvider,
		ILiveSource liveSource,
		IReadOnlyList<Pen> pens,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		_dataProvider = dataProvider;
		_liveSource = liveSource;
		_dataScheduler = dataScheduler;
		_uiScheduler = uiScheduler;
		_penIds = new BehaviorSubject<IReadOnlyList<int>>([.. pens.Select(pen => pen.PenId)]);
		RealtimeBatches = BuildRealtimeBatches();
		RealtimeFailures = _realtimeFailures.ObserveOn(_uiScheduler);
		LiveSourceStates = _liveSource.States.ObserveOn(_uiScheduler);
		LivePenFaults = _liveSource.PenFaults.ObserveOn(_uiScheduler);
	}

	/// <summary>
	/// The live edge. It never faults: a live source that faults is reported through
	/// <see cref="RealtimeFailures"/> and this stream completes instead.
	/// </summary>
	public IObservable<RealtimeBatch> RealtimeBatches { get; }

	/// <summary>
	/// Every realtime failure once: a throw inside the batch projection, which the pipeline skips, and a
	/// live source that faults. Republished on the UI scheduler, like <see cref="RealtimeBatches"/>.
	/// </summary>
	public IObservable<Exception> RealtimeFailures { get; }

	/// <summary>The live source's session state, the current one first, on the UI scheduler.</summary>
	public IObservable<LiveSourceState> LiveSourceStates { get; }

	/// <summary>The live source's pen faults, on the UI scheduler.</summary>
	public IObservable<LiveSourceError> LivePenFaults { get; }

	/// <summary>The pen set the live edge follows: the constructor's, or the last one handed to SetPens.</summary>
	public IReadOnlyList<int> PenIds => _penIds.Value;

	public void Dispose()
	{
		// Never disposed: a buffer flush still running on the data scheduler would throw out of
		// TryBuildRealtimeBatch, and Rx would turn that into the OnError the catch exists to prevent.
		_realtimeFailures.OnCompleted();
		_penIds.OnCompleted();
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

	public Task<Result<IReadOnlyList<DateOnly>>> QueryArchiveDaysAsync(
		DateOnly firstDay,
		DateOnly lastDay,
		CancellationToken cancellationToken = default)
	{
		return _dataProvider.QueryArchiveDaysAsync(firstDay, lastDay, cancellationToken);
	}

	private IObservable<RealtimeBatch> BuildRealtimeBatches()
	{
		return _penIds
			.Select(_liveSource.Subscribe)
			.Switch()
			.Buffer(BatchWindow, _dataScheduler)
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

	// A pen carries only its own samples, no filler at another pen's timestamp.
	private static PenRealtimeValues BuildPenValues(IGrouping<int, Sample> penSamples)
	{
		var ordered = penSamples.OrderBy(sample => sample.TimestampUtc).ToArray();

		return new PenRealtimeValues(
			penSamples.Key,
			Array.ConvertAll(ordered, sample => sample.TimestampUtc),
			Array.ConvertAll(ordered, sample => sample.Value));
	}
}
