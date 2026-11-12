using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// Reads which archive wall-clock days hold rows over the strip's extent, and when each day's samples start
/// and end (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public sealed class ArchivePresenceFeed : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _failedReadRetryInterval = TimeSpan.FromSeconds(60.0);

	private readonly TrendCoordinator _coordinator;
	private readonly ChartNavigationController _navigation;
	private readonly TimeZoneInfo _archiveZone;
	private readonly IScheduler _uiScheduler;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger _logger;
	private readonly OutageReport _outage;
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<SpansRequest> _requests = new();

	private DateOnly? _firstDay;
	private DateOnly _lastDay;
	private DateOnly? _completeThrough;
	private SpansRequest? _readInFlight;
	private bool _isRetryArmed;
	private DateTimeOffset _nextRetry = DateTimeOffset.MinValue;

	internal ArchivePresenceFeed(
		TrendCoordinator coordinator,
		ChartNavigationController navigation,
		TimeZoneInfo archiveZone,
		IScheduler uiScheduler,
		MessagePanelViewModel messagePanel,
		ILogger logger)
	{
		_coordinator = coordinator;
		_navigation = navigation;
		_archiveZone = archiveZone;
		_uiScheduler = uiScheduler;
		_messagePanel = messagePanel;
		_logger = logger;
		_outage = new OutageReport("archive day spans read", messagePanel, logger);

		_disposables.Add(SubscribeToReads());
		_disposables.Add(_requests);
	}

	/// <summary>
	/// The days of the extent that hold rows, ascending; the newest day's last sample follows the live edge.
	/// </summary>
	public IReadOnlyList<ArchiveDaySpan> Spans
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = [];

	/// <summary>True from an applied extent until its first read lands or fails; page steps wait for it.</summary>
	public bool IsExtentReadPending
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>Reads every day of an applied extent, in place of the read in flight.</summary>
	public void ReadExtent(DateTime firstUtc, DateTime lastUtc)
	{
		var firstDay = DayOf(firstUtc);
		_firstDay = firstDay;
		_lastDay = DayOf(lastUtc);
		_completeThrough = null;
		IsExtentReadPending = true;
		Request(firstDay);
	}

	/// <summary>
	/// Extends the newest day to the newest sample; reads past the complete days on a new day or a due retry.
	/// </summary>
	public void FollowNewestSample()
	{
		if (_firstDay is not { } firstDay || _navigation.NewestSample is not { } newestSample)
		{
			return;
		}

		Spans = ExtendedToNewestSample(Spans);
		var newestDay = DayOf(newestSample);
		_lastDay = newestDay > _lastDay ? newestDay : _lastDay;

		if (_readInFlight is not null)
		{
			return;
		}

		if (_isRetryArmed)
		{
			if (_uiScheduler.Now < _nextRetry)
			{
				return;
			}

			_nextRetry = _uiScheduler.Now + _failedReadRetryInterval;
			Request(_completeThrough ?? firstDay);

			return;
		}

		if (_completeThrough is not { } completeThrough || _lastDay > completeThrough)
		{
			Request(_completeThrough ?? firstDay);
		}
	}

	private DateOnly DayOf(DateTime utc)
	{
		return ArchiveClock.DayOf(utc, _archiveZone);
	}

	private void Request(DateOnly firstDay)
	{
		var request = new SpansRequest(firstDay, _lastDay);
		_readInFlight = request;
		_requests.OnNext(request);
	}

	private IDisposable SubscribeToReads()
	{
		return _requests
			.Select(Read)
			.Switch()
			.ObserveOn(_uiScheduler)
			.Subscribe(ApplyRead, ReportFailure);
	}

	private IObservable<LandedRead> Read(SpansRequest request)
	{
		return Observable.Create<LandedRead>(async (observer, cancellation) =>
			observer.OnNext(new LandedRead(request, await ReadAsync(request, cancellation))));
	}

	/// <summary>Never throws: a thrown read comes back as a failed result and cannot end the pipeline.</summary>
	private async Task<Result<IReadOnlyList<ArchiveDaySpan>>> ReadAsync(
		SpansRequest request,
		CancellationToken cancellation)
	{
		var started = Stopwatch.GetTimestamp();
		Result<IReadOnlyList<ArchiveDaySpan>> result;

		try
		{
			result = await _coordinator.QueryArchiveDaySpansAsync(request.FirstDay, request.LastDay, cancellation);
		}
		catch (Exception readFailure)
		{
			result = Result.Fail(new ExceptionalError(readFailure));
		}

		_logger.LogDebug(
			"The archive day spans read {FirstDay} to {LastDay} in {Elapsed}",
			request.FirstDay,
			request.LastDay,
			Stopwatch.GetElapsedTime(started));

		return result;
	}

	private void ApplyRead(LandedRead read)
	{
		var isSuperseded = !ReferenceEquals(read.Request, _readInFlight);
		if (isSuperseded)
		{
			return;
		}

		_readInFlight = null;
		IsExtentReadPending = false;

		try
		{
			ApplyResult(read.Request, read.Result);
		}
		catch (Exception applyFailure)
		{
			ReportFailure(applyFailure);
		}
	}

	private void ApplyResult(SpansRequest request, Result<IReadOnlyList<ArchiveDaySpan>> result)
	{
		if (result.IsFailed)
		{
			_isRetryArmed = true;
			_outage.Failed(result.Errors);

			return;
		}

		_isRetryArmed = false;
		_outage.Succeeded();
		_completeThrough = request.LastDay;
		Spans = ExtendedToNewestSample(Merged(Spans, request.FirstDay, result.Value));
	}

	/// <summary>The held spans from the extent's first day to the read's first day, followed by the read.</summary>
	private IReadOnlyList<ArchiveDaySpan> Merged(
		IReadOnlyList<ArchiveDaySpan> held,
		DateOnly firstDay,
		IReadOnlyList<ArchiveDaySpan> read)
	{
		var merged = new List<ArchiveDaySpan>(held.Count + read.Count);
		foreach (var span in held)
		{
			if (span.Day >= _firstDay && span.Day < firstDay)
			{
				merged.Add(span);
			}
		}

		merged.AddRange(read);

		return merged;
	}

	/// <summary>A newest sample on a day no read has covered holds that day until a read replaces it.</summary>
	private IReadOnlyList<ArchiveDaySpan> ExtendedToNewestSample(IReadOnlyList<ArchiveDaySpan> spans)
	{
		if (_navigation.NewestSample is not { } newestSample
			|| spans is not [.., var newest]
			|| newestSample <= newest.LastUtc)
		{
			return spans;
		}

		var newestDay = DayOf(newestSample);
		if (newestDay != newest.Day)
		{
			return newestDay > _completeThrough
				? [.. spans, new ArchiveDaySpan(newestDay, newestSample, newestSample)]
				: spans;
		}

		var extended = spans.ToArray();
		extended[^1] = newest with { LastUtc = newestSample };

		return extended;
	}

	private void ReportFailure(Exception failure)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	private sealed record SpansRequest(DateOnly FirstDay, DateOnly LastDay);

	private sealed record LandedRead(SpansRequest Request, Result<IReadOnlyList<ArchiveDaySpan>> Result);
}
