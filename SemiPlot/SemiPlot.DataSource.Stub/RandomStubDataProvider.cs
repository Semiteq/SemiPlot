using System.Reactive.Concurrency;
using System.Reactive.Linq;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

namespace SemiPlot.DataSource.Stub;

public sealed class RandomStubDataProvider : IDataProvider
{
	private static readonly TimeSpan _archiveDepth = TimeSpan.FromDays(7.0);

	private readonly long _seed;
	private readonly IScheduler _scheduler;
	private readonly TimeSpan _realtimeInterval;
	private readonly Func<DateTime> _utcNow;
	private readonly IReadOnlyDictionary<long, SyntheticPen> _pensById;

	public RandomStubDataProvider(
		IScheduler scheduler,
		long seed = 1,
		TimeSpan? realtimeInterval = null,
		Func<DateTime>? utcNow = null)
	{
		_scheduler = scheduler ?? throw new ArgumentNullException(nameof(scheduler));
		_seed = seed;
		_realtimeInterval = realtimeInterval ?? TimeSpan.FromSeconds(1);
		_utcNow = utcNow ?? (() => DateTime.UtcNow);

		var catalog = SyntheticPenCatalog.Build();
		_pensById = catalog.ToDictionary(pen => pen.ProjectVarId);
		Pens = catalog.Select(pen => pen.ToPen()).ToArray();
	}

	public IReadOnlyList<Pen> Pens { get; }

	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		var subscribed = penIds
			.Where(_pensById.ContainsKey)
			.Distinct()
			.ToArray();

		// Anchor realtime to the wall-clock at subscription start so emitted timestamps join the
		// history timeline queried with the same clock.
		var subscribedAtUtc = _utcNow();

		return Observable
			.Interval(_realtimeInterval, _scheduler)
			.Select(tick => (IReadOnlyList<Sample>)SamplesAt(subscribed, subscribedAtUtc, tick + 1));
	}

	public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
		IReadOnlyList<long> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer,
		int targetColumnCount)
	{
		ArgumentNullException.ThrowIfNull(penIds);

		if (fromUtc > toUtc)
		{
			return Task.FromResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(
				$"Invalid range: fromUtc ({fromUtc:O}) is after toUtc ({toUtc:O})."));
		}

		if (targetColumnCount < 1)
		{
			return Task.FromResult(Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(
				$"Invalid target column count: {targetColumnCount} (must be at least one)."));
		}

		var interval = layer.ToSampleInterval();
		var envelopes = penIds
			.Where(_pensById.ContainsKey)
			.Distinct()
			.Select(penId => BuildEnvelope(penId, fromUtc, toUtc, interval, targetColumnCount))
			.ToArray();

		return Task.FromResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(envelopes));
	}

	// The synthetic archive reaches back a fixed depth from the current wall-clock.
	public Task<Result<ArchiveExtent>> QueryArchiveExtentAsync()
	{
		var now = _utcNow();
		return Task.FromResult(Result.Ok(new ArchiveExtent(now - _archiveDepth, now)));
	}

	private PenHistoryEnvelope BuildEnvelope(
		long penId,
		DateTime fromUtc,
		DateTime toUtc,
		TimeSpan interval,
		int targetColumnCount)
	{
		var pen = _pensById[penId];
		var timestamps = new List<DateTime>();
		var values = new List<double?>();

		var tickIndex = 0L;
		for (var timestamp = fromUtc; timestamp <= toUtc; timestamp += interval)
		{
			timestamps.Add(timestamp);
			values.Add(ValueAt(pen, penId, tickIndex));
			tickIndex++;
		}

		return MinMaxDecimator.Decimate(penId, timestamps, values, targetColumnCount);
	}

	// Bad-quality samples map to null at the provider boundary so the null=gap path flows downstream.
	private double? ValueAt(SyntheticPen pen, long penId, long tickIndex)
	{
		if (SyntheticQuality.IsBad(penId, tickIndex))
		{
			return null;
		}

		return SyntheticValueWalk.Value(_seed, penId, tickIndex, pen.MinValue, pen.MaxValue);
	}

	private IReadOnlyList<Sample> SamplesAt(IReadOnlyList<long> penIds, DateTime anchorUtc, long tickIndex)
	{
		var timestamp = anchorUtc + TimeSpan.FromTicks(_realtimeInterval.Ticks * tickIndex);

		return penIds
			.Select(penId =>
			{
				var pen = _pensById[penId];
				var value = SyntheticValueWalk.Value(_seed, penId, tickIndex, pen.MinValue, pen.MaxValue);
				return new Sample(penId, timestamp, value);
			})
			.ToArray();
	}
}
