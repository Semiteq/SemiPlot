using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// The one history path. Throttle collapses a gesture's notches to one trailing request; Switch drops any
// still-in-flight query when a newer request arrives, so a late stale response never overwrites the
// current window.
public sealed class ChartHistoryRequestDebouncer : IDisposable
{
	private readonly Subject<HistoryRequest> _requests = new();
	private readonly IDisposable _subscription;
	private bool _isDisposed;

	public ChartHistoryRequestDebouncer(
		Func<HistoryRequest, Task<Result<IReadOnlyList<PenHistoryEnvelope>>>> queryAsync,
		Action<HistoryRequest, IReadOnlyList<PenHistoryEnvelope>> applyHistory,
		Action<Exception> reportQueryFailure,
		TimeSpan debounceWindow,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		ArgumentNullException.ThrowIfNull(queryAsync);
		ArgumentNullException.ThrowIfNull(applyHistory);
		ArgumentNullException.ThrowIfNull(reportQueryFailure);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_subscription = _requests
			.Throttle(debounceWindow, dataScheduler)
			.Select(request => Observable
				.FromAsync(() => queryAsync(request))
				.Select(result => (request, result))
				.Catch((Exception queryFailure) => ReportAndDrop(reportQueryFailure, queryFailure)))
			.Switch()
			.Where(pair => pair.result.IsSuccess)
			.ObserveOn(uiScheduler)
			// The request travels with its result so the consumer can tell "asked for and not returned" from
			// "not asked for": a pen added while the query was in flight is neither.
			.Subscribe(pair => applyHistory(pair.request, pair.result.Value));
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
	}

	private static IObservable<(HistoryRequest request, Result<IReadOnlyList<PenHistoryEnvelope>> result)>
		ReportAndDrop(
			Action<Exception> reportQueryFailure,
			Exception queryFailure)
	{
		reportQueryFailure(queryFailure);

		return Observable.Empty<(HistoryRequest request, Result<IReadOnlyList<PenHistoryEnvelope>> result)>();
	}

	public void Request(HistoryRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_requests.OnNext(request);
	}
}
