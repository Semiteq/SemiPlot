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
	private readonly Subject<HistoryRequest> _requests = new();

	// Nothing subscribes to it but the query pipeline, and only Admit and the slot release push into it,
	// which is what keeps a single query in flight.
	private readonly Subject<HistoryRequest> _admitted = new();
	private readonly Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>> _applyHistory;
	private readonly Action<IReadOnlyList<IError>> _reportQueryFailure;
	private readonly IDisposable _subscription;

	// Written from the emission head on the data scheduler and from the thread a query completes on, so every
	// access to these three takes the one gate.
	private readonly Lock _gate = new();
	private HistoryRequest? _lastApplied;
	private HistoryRequest? _pending;
	private bool _isQueryRunning;

	// Written on the UI thread that disposes and read on the data and query-completion threads.
	private volatile bool _isDisposed;

	public ChartHistoryRequestDebouncer(
		Func<HistoryRequest, Task<Result<IReadOnlyList<PenHistoryEnvelope>>>> queryAsync,
		Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>> applyHistory,
		Action<IReadOnlyList<IError>> reportQueryFailure,
		TimeSpan debounceWindow,
		TimeSpan capInterval,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		_applyHistory = applyHistory;
		_reportQueryFailure = reportQueryFailure;

		var trailing = _requests.Throttle(debounceWindow, dataScheduler);
		var paced = _requests.Sample(capInterval, dataScheduler);

		var emissions = trailing
			.Merge(paced)
			.Where(request => !ReadsWhatTheLastQueryApplied(request))
			.Subscribe(Admit);

		var queries = _admitted
			.Select(request => Observable
				.FromAsync(() => queryAsync(request))
				// A thrown query joins the failure channel the provider already answers on, so the delivery
				// below has one failure path instead of two and the stream survives either.
				.Catch((Exception queryFailure) => Observable.Return(
					Result.Fail<IReadOnlyList<PenHistoryEnvelope>>(new ExceptionalError(queryFailure))))
				.Select(result => (request, result)))
			.Concat()
			.Do(pair => CompleteQuery(pair.request, pair.result))
			.ObserveOn(uiScheduler)
			.Subscribe(pair => Deliver(pair.request, pair.result), ReportPipelineFailure);

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
		_admitted.Dispose();
	}

	public void Request(HistoryRequest request)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_requests.OnNext(request);
	}

	// Only the newest window waits behind the running query: an older one the gesture has already left would
	// read rows nothing draws.
	private void Admit(HistoryRequest request)
	{
		lock (_gate)
		{
			if (_isQueryRunning)
			{
				_pending = request;

				return;
			}

			_isQueryRunning = true;
		}

		Start(request);
	}

	private void CompleteQuery(HistoryRequest request, Result<IReadOnlyList<PenHistoryEnvelope>> result)
	{
		HistoryRequest? next;

		lock (_gate)
		{
			_lastApplied = result.IsSuccess ? request : null;

			next = _pending;
			_pending = null;

			// The window waiting behind a query can be the window that query just applied: a sample tick
			// fires on the request the gesture last pushed, whether or not a read for it is already running.
			if (Matches(_lastApplied, next))
			{
				next = null;
			}

			_isQueryRunning = next is not null;
		}

		if (next is not null)
		{
			Start(next);
		}
	}

	private void Start(HistoryRequest request)
	{
		if (_isDisposed)
		{
			return;
		}

		_admitted.OnNext(request);
	}

	// The request travels with its result so the consumer can tell "asked for and not returned" from "not
	// asked for": a pen added while the query was in flight is neither.
	private void Deliver(HistoryRequest request, Result<IReadOnlyList<PenHistoryEnvelope>> result)
	{
		if (result.IsFailed)
		{
			_reportQueryFailure(result.Errors);

			return;
		}

		// A throw out of the consumer is a failure of this delivery, not of the pipeline: reporting it keeps
		// the one subscription that issues every history query alive for the rest of the session.
		try
		{
			_applyHistory(request, result.Value);
		}
		catch (Exception applyFailure)
		{
			ReportPipelineFailure(applyFailure);
		}
	}

	private void ReportPipelineFailure(Exception failure)
	{
		_reportQueryFailure([new ExceptionalError(failure)]);
	}

	private bool ReadsWhatTheLastQueryApplied(HistoryRequest request)
	{
		lock (_gate)
		{
			return Matches(_lastApplied, request);
		}
	}

	private static bool Matches(HistoryRequest? applied, HistoryRequest? request)
	{
		return applied is not null
			&& request is not null
			&& applied.FromUtc == request.FromUtc
			&& applied.ToUtc == request.ToUtc
			&& applied.Layer == request.Layer
			&& applied.TargetColumnCount == request.TargetColumnCount
			&& applied.PenIds.SequenceEqual(request.PenIds);
	}
}
