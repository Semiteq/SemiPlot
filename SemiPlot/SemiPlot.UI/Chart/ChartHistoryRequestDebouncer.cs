using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// The single chokepoint for gesture-driven history re-queries (zoom/pan). Rapid gestures push windows
// onto the subject; Throttle collapses them to one trailing request after the stream goes quiet so the
// query never runs once-per-wheel-notch on the UI thread. The query runs on the data scheduler and its
// result is applied on the UI scheduler; Switch drops any still-in-flight query when a newer window
// arrives (latest-wins), so a late stale response never overwrites the current window.
public sealed class ChartHistoryRequestDebouncer : IDisposable
{
	private readonly Subject<HistoryRequest> _requests = new();
	private readonly IDisposable _subscription;
	private bool _isDisposed;

	public ChartHistoryRequestDebouncer(
		Func<HistoryRequest, Task<Result<IReadOnlyList<PenHistoryEnvelope>>>> queryAsync,
		Action<TrendHistory, long> applyHistory,
		TimeSpan debounceWindow,
		IScheduler dataScheduler,
		IScheduler uiScheduler)
	{
		ArgumentNullException.ThrowIfNull(queryAsync);
		ArgumentNullException.ThrowIfNull(applyHistory);
		ArgumentNullException.ThrowIfNull(dataScheduler);
		ArgumentNullException.ThrowIfNull(uiScheduler);

		_subscription = _requests
			.Throttle(debounceWindow, dataScheduler)
			.Select(request => Observable
				.FromAsync(() => queryAsync(request))
				.Select(result => (request, result)))
			.Switch()
			.Where(pair => pair.result.IsSuccess)
			.Select(pair => (history: new TrendHistory(pair.request.Layer, pair.result.Value), pair.request.Sequence))
			.ObserveOn(uiScheduler)
			.Subscribe(pair => applyHistory(pair.history, pair.Sequence));
	}

	public void Request(HistoryRequest request)
	{
		ArgumentNullException.ThrowIfNull(request);
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		_requests.OnNext(request);
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
}
