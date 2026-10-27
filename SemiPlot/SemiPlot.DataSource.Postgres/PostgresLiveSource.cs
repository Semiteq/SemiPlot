using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.Core.Data;

namespace SemiPlot.DataSource.Postgres;

/// <summary>Serves the live edge by polling the archive over the pooled <see cref="NpgsqlDataSource"/>.</summary>
public sealed class PostgresLiveSource : ILiveSource
{
	private readonly NpgsqlDataSource _dataSource;
	private readonly ArchiveTimeConverter _timeConverter;
	private readonly ArchiveExceptionMapper _exceptionMapper;
	private readonly IScheduler _scheduler;
	private readonly ILogger<PostgresLiveSource> _logger;
	private readonly TimeSpan _pollInterval;

	// Set before each read and cleared by its answer, and kept across subscriptions, so the first answer after a
	// failed or switch-cancelled read is marked. Written by the poll loop on the injected scheduler.
	private volatile bool _previousReadFailed;

	internal PostgresLiveSource(
		NpgsqlDataSource dataSource,
		ArchiveTimeConverter timeConverter,
		ArchiveExceptionMapper exceptionMapper,
		IScheduler scheduler,
		ILogger<PostgresLiveSource> logger,
		TimeSpan pollInterval)
	{
		_dataSource = dataSource;
		_timeConverter = timeConverter;
		_exceptionMapper = exceptionMapper;
		_scheduler = scheduler;
		_logger = logger;
		_pollInterval = pollInterval;
	}

	/// <summary>
	/// The live edge of the requested variables, one poll per answered read. Cold: each subscription starts a
	/// poll loop of its own, on the injected scheduler and at the poll interval, holding its own baseline;
	/// disposing it cancels the loop's query and its wait, so no further statement is issued.
	/// </summary>
	public IObservable<LivePoll> Subscribe(IReadOnlyList<int> penIds)
	{
		var subscribedIds = penIds.ToArray();

		return Observable.Create<LivePoll>(observer =>
			_scheduler.ScheduleAsync((_, cancellationToken) => PollAsync(subscribedIds, observer, cancellationToken)));
	}

	private async Task PollAsync(
		int[] penIds,
		IObserver<LivePoll> observer,
		CancellationToken cancellationToken)
	{
		var poll = new RealtimePoll(
			_dataSource,
			_timeConverter,
			_exceptionMapper,
			penIds,
			_logger);

		try
		{
			while (!cancellationToken.IsCancellationRequested)
			{
				// docs/architecture/data-integration.md#realtime
				var answeredAtUtc = _scheduler.Now.UtcDateTime;
				var resumesAfterFailure = _previousReadFailed;
				_previousReadFailed = true;
				var samples = await poll.ReadOnceAsync(cancellationToken).ConfigureAwait(false);

				// A disposal landing while the query is in flight delivers nothing further.
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (samples is not null)
				{
					observer.OnNext(new LivePoll(answeredAtUtc, samples, resumesAfterFailure));
					_previousReadFailed = false;
				}

				await _scheduler.Sleep(_pollInterval, cancellationToken).ConfigureAwait(false);
			}
		}
		catch (OperationCanceledException)
		{
			// Disposal ends the loop.
		}
	}
}
