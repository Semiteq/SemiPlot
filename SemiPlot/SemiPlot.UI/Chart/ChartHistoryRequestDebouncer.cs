using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// docs/architecture/data-integration.md#what-one-history-query-covers
public sealed class ChartHistoryRequestDebouncer : IDisposable
{
	private readonly Subject<StampedRequest?> _requests = new();

	// Nothing subscribes to it but the query pipeline, and only Admit and the slot release push into it,
	// which is what keeps a single query in flight.
	private readonly Subject<RunningQuery> _admitted = new();
	private readonly Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>> _applyHistory;
	private readonly Action<IReadOnlyList<IError>> _reportQueryFailure;
	private readonly IScheduler _uiScheduler;
	private readonly IDisposable _subscription;

	// Written from the emission head on the data scheduler, from the thread a query completes on, and on the
	// UI thread by Dispose, so every access to these three takes the one gate.
	private readonly Lock _gate = new();
	private SucceededRead? _lastSucceeded;
	private StampedRequest? _pending;
	private RunningQuery? _running;

	// Incremented by Deliver on the UI scheduler and read by Request on the caller's thread, which are one
	// thread only when the UI scheduler is the dispatcher.
	private int _deliveredReads;

	// Written on the UI thread that disposes and read on the data and query-completion threads.
	private volatile bool _isDisposed;

	public ChartHistoryRequestDebouncer(
		Func<HistoryRequest, CancellationToken, Task<Result<IReadOnlyList<PenHistoryEnvelope>>>> queryAsync,
		Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>> applyHistory,
		Action<IReadOnlyList<IError>> reportQueryFailure,
		TimeSpan debounceWindow,
		TimeSpan capInterval,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		_applyHistory = applyHistory;
		_reportQueryFailure = reportQueryFailure;
		_uiScheduler = uiScheduler;

		var trailing = _requests
			.Throttle(debounceWindow, dataScheduler)
			.Select(pushed => (Pushed: pushed, EndsGesture: true));
		var paced = _requests
			.Sample(capInterval, dataScheduler)
			.Select(pushed => (Pushed: pushed, EndsGesture: false));

		var emissions = trailing
			.Merge(paced)
			.ObserveOn(dataScheduler)
			.Subscribe(admission => Admit(admission.Pushed, admission.EndsGesture));

		var queries = _admitted
			.Select(query => Observable
				.FromAsync(() => queryAsync(query.Request, query.Cancellation.Token))
				.Select(result => new QueryCompletion(query.Request, result))
				// A thrown query joins the failure channel the provider already answers on, so the delivery
				// below has one failure path instead of two and the stream survives either.
				.Catch((Exception queryFailure) => Observable.Return(CompletionOf(query, queryFailure))))
			.Concat()
			.Do(CompleteQuery)
			.ObserveOn(uiScheduler)
			.Subscribe(Deliver, ReportPipelineFailure);

		_subscription = new CompositeDisposable(emissions, queries);
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_subscription.Dispose();
		_requests.Dispose();

		// Start can still push from the data or the completion thread past its disposed check; a completed
		// subject drops that push, where a disposed one would throw on that thread.
		_admitted.OnCompleted();

		lock (_gate)
		{
			if (_running is { } running)
			{
				CancelInBackground(running);
			}
		}
	}

	public void Request(HistoryRequest request)
	{
		_requests.OnNext(new StampedRequest(request, Volatile.Read(ref _deliveredReads)));
	}

	/// <summary>
	/// Says the window in view is drawn: nothing is read for it, and as a gesture's end it cancels the read in flight.
	/// </summary>
	public void RequestNothing()
	{
		_requests.OnNext(null);
	}

	private void Admit(StampedRequest? pushed, bool endsGesture)
	{
		RunningQuery query;

		lock (_gate)
		{
			var toRead = pushed is not null && !IsAnsweredByLastSucceeded(pushed) ? pushed : null;

			if (_running is { } running)
			{
				_pending = toRead;

				if (endsGesture && !Matches(running.Request, pushed?.Request))
				{
					CancelInBackground(running);
				}

				return;
			}

			if (toRead is null)
			{
				return;
			}

			query = TakeQuerySlot(toRead.Request);
		}

		Start(query);
	}

	private void CompleteQuery(QueryCompletion completion)
	{
		RunningQuery? next = null;

		lock (_gate)
		{
			_running = null;

			if (completion.Result is { IsSuccess: true })
			{
				_lastSucceeded = new SucceededRead(completion.Request, (_lastSucceeded?.Ordinal ?? 0) + 1);
			}

			var waiting = _pending;
			_pending = null;

			if (waiting is not null && !IsAnsweredByLastSucceeded(waiting))
			{
				next = TakeQuerySlot(waiting.Request);
			}
		}

		if (next is not null)
		{
			Start(next);
		}
	}

	private bool IsAnsweredByLastSucceeded(StampedRequest pushed)
	{
		return _lastSucceeded is { } read
			&& pushed.ReadsDeliveredAtPush < read.Ordinal
			&& Matches(read.Request, pushed.Request);
	}

	private RunningQuery TakeQuerySlot(HistoryRequest request)
	{
		_running = new RunningQuery(request, new CancellationTokenSource());

		return _running;
	}

	private void Start(RunningQuery query)
	{
		if (_isDisposed)
		{
			return;
		}

		_admitted.OnNext(query);
	}

	private void CancelInBackground(RunningQuery query)
	{
		// OnlyOnFaulted, so cancelled.Exception is never null.
		_ = query.Cancellation.CancelAsync().ContinueWith(
			cancelled => _uiScheduler.Schedule(() => ReportCancelFailure(cancelled.Exception!)),
			CancellationToken.None,
			TaskContinuationOptions.OnlyOnFaulted,
			TaskScheduler.Default);
	}

	// The request travels with its result so the consumer can tell "asked for and not returned" from "not
	// asked for": a pen added while the query was in flight is neither.
	private void Deliver(QueryCompletion completion)
	{
		if (completion.Result is not { } result)
		{
			return;
		}

		if (result.IsFailed)
		{
			_reportQueryFailure(result.Errors);

			return;
		}

		// Counted ahead of the apply, so a request the apply itself pushes is stamped as asked with these rows
		// in hand, and so is every request after an apply that threw.
		Interlocked.Increment(ref _deliveredReads);

		// A throw out of the consumer is a failure of this delivery, not of the pipeline: reporting it keeps
		// the one subscription that issues every history query alive for the rest of the session.
		try
		{
			_applyHistory(completion.Request, result.Value);
		}
		catch (Exception applyFailure)
		{
			ReportPipelineFailure(applyFailure);
		}
	}

	// The chart that would show it is gone once the debouncer is disposed.
	private void ReportCancelFailure(AggregateException cancelFailure)
	{
		if (!_isDisposed)
		{
			ReportPipelineFailure(cancelFailure.GetBaseException());
		}
	}

	private void ReportPipelineFailure(Exception failure)
	{
		_reportQueryFailure([new ExceptionalError(failure)]);
	}

	private static QueryCompletion CompletionOf(RunningQuery query, Exception queryFailure)
	{
		if (queryFailure is OperationCanceledException && query.Cancellation.IsCancellationRequested)
		{
			return new QueryCompletion(query.Request, null);
		}

		return new QueryCompletion(
			query.Request,
			Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(new ExceptionalError(queryFailure)));
	}

	private static bool Matches(HistoryRequest known, HistoryRequest? request)
	{
		return request is not null
			&& known.FromUtc == request.FromUtc
			&& known.ToUtc == request.ToUtc
			&& known.Layer == request.Layer
			&& known.TargetColumnCount == request.TargetColumnCount
			&& known.PenIds.SequenceEqual(request.PenIds);
	}

	private sealed record StampedRequest(HistoryRequest Request, int ReadsDeliveredAtPush);

	/// <summary>Ordinal counts successful reads from one, in the order they are delivered.</summary>
	private sealed record SucceededRead(HistoryRequest Request, int Ordinal);

	private sealed record RunningQuery(HistoryRequest Request, CancellationTokenSource Cancellation);

	/// <summary>A null result is a query the debouncer cancelled.</summary>
	private readonly record struct QueryCompletion(
		HistoryRequest Request,
		Result<IReadOnlyList<PenHistoryEnvelope>>? Result);
}
