using System.Reactive.Concurrency;
using System.Reactive.Linq;

using Microsoft.Extensions.Logging;

using Npgsql;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

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
	/// The live edge of the requested variables. Cold: each subscription starts a poll loop of its own, on the
	/// injected scheduler and at the poll interval, holding its own baseline; disposing it cancels the loop's
	/// query and its wait, so no further statement is issued.
	/// </summary>
	public IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)
	{
		var subscribedIds = penIds.ToArray();

		return Observable.Create<IReadOnlyList<Sample>>(observer =>
			_scheduler.ScheduleAsync((_, cancellationToken) => PollAsync(subscribedIds, observer, cancellationToken)));
	}

	private async Task PollAsync(
		int[] penIds,
		IObserver<IReadOnlyList<Sample>> observer,
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
				var samples = await poll.ReadOnceAsync(cancellationToken).ConfigureAwait(false);

				// A disposal landing while the query is in flight delivers nothing further.
				if (cancellationToken.IsCancellationRequested)
				{
					return;
				}

				if (samples.Count > 0)
				{
					observer.OnNext(samples);
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
