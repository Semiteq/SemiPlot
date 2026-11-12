using System.Diagnostics;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Minimap;

/// <summary>
/// Reads the drawn pen's overview over the pager's page at the strip's width and publishes it with the pen's
/// colour (docs/architecture/trend-interaction.md#archive-overview-minimap).
/// </summary>
public sealed class MinimapBandFeed : ReactiveObject, IDisposable
{
	internal const int ColumnStep = 250;

	internal const int MaximumColumns = 2500;

	private const double ReadDelayDivisor = 1000.0;
	private static readonly TimeSpan _fastestRead = TimeSpan.FromSeconds(2.0);
	private static readonly TimeSpan _slowestRead = TimeSpan.FromSeconds(60.0);

	private readonly TrendCoordinator _coordinator;
	private readonly TrendChartViewModel _chart;
	private readonly MinimapPager _pager;
	private readonly Func<bool> _hasExtent;
	private readonly IScheduler _uiScheduler;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger _logger;
	private readonly OutageReport _outage;
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<BandRequest?> _requests = new();
	private readonly SerialDisposable _nextRead = new();
	private readonly ObservableAsPropertyHelper<string?> _bandColor;

	private BandRequest? _latestRequest;
	private AggregationLayer _layer;
	private int _columns = ColumnStep;

	internal MinimapBandFeed(
		TrendCoordinator coordinator,
		TrendChartViewModel chart,
		MinimapPager pager,
		Func<bool> hasExtent,
		IScheduler uiScheduler,
		MessagePanelViewModel messagePanel,
		ILogger logger)
	{
		_coordinator = coordinator;
		_chart = chart;
		_pager = pager;
		_hasExtent = hasExtent;
		_uiScheduler = uiScheduler;
		_messagePanel = messagePanel;
		_logger = logger;
		_outage = new OutageReport("minimap band read", messagePanel, logger);

		_disposables.Add(SubscribeToReads());
		_disposables.Add(_nextRead);
		_disposables.Add(_requests);
		_disposables.Add(ReadOnDrawnPenChange());
		_disposables.Add(ReadOnPageChange());
		_disposables.Add(DrawnPenColor().ToProperty(this, feed => feed.BandColor, out _bandColor));
	}

	/// <summary>The drawn pen's overview over the page; null with no drawn pen, no extent or no rows.</summary>
	public PenHistoryEnvelope? Band
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>True from a failed read until the next read lands; the band it held stays as it was.</summary>
	public bool HasReadFailed
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The drawn pen's stored colour, followed live; null with no drawn pen.</summary>
	public string? BandColor => _bandColor.Value;

	public void Dispose()
	{
		_disposables.Dispose();
	}

	/// <summary>Reads at the strip width's column count; a width inside the same 250 px step reads nothing.</summary>
	public void SetStripWidth(double stripWidth)
	{
		var columns = ColumnsForWidth(stripWidth);
		if (columns == _columns)
		{
			return;
		}

		_columns = columns;
		RequestRead();
	}

	internal static int ColumnsForWidth(double stripWidth)
	{
		var steps = (int)Math.Round(stripWidth / ColumnStep, MidpointRounding.AwayFromZero);

		return Math.Clamp(steps * ColumnStep, ColumnStep, MaximumColumns);
	}

	/// <summary>Reads the band now, in place of the read in flight and the scheduled one.</summary>
	public void RequestRead()
	{
		var request = NextRequest();
		if (request?.PenId != Band?.PenId)
		{
			Band = null;
		}

		if (request is null)
		{
			HasReadFailed = false;
		}

		_latestRequest = request;
		_nextRead.Disposable = null;
		_requests.OnNext(request);
	}

	private void TryRequestRead()
	{
		try
		{
			RequestRead();
		}
		catch (Exception requestFailure)
		{
			ReportFailure(requestFailure);
		}
	}

	private IDisposable SubscribeToReads()
	{
		return _requests
			.Select(Read)
			.Switch()
			.ObserveOn(_uiScheduler)
			.Subscribe(ApplyRead, ReportFailure);
	}

	private IDisposable ReadOnDrawnPenChange()
	{
		return _chart
			.WhenAnyValue(chart => chart.DrawnPenId)
			.Skip(1)
			.Subscribe(_ => TryRequestRead(), ReportFailure);
	}

	private IDisposable ReadOnPageChange()
	{
		return _pager
			.WhenAnyValue(pager => pager.Page)
			.Skip(1)
			.Subscribe(_ => TryRequestRead(), ReportFailure);
	}

	private IObservable<string?> DrawnPenColor()
	{
		return _chart
			.WhenAnyValue(chart => chart.DrawnPenId, chart => chart.Pens, (penId, _) => penId)
			.Select(penId => penId is { } drawnPenId ? _chart.FindPen(drawnPenId) : null)
			.Select(state => state?.WhenAnyValue(drawn => drawn.Pen.Color) ?? Observable.Return<string?>(null))
			.Switch()
			.DistinctUntilChanged();
	}

	private BandRequest? NextRequest()
	{
		if (!_hasExtent() || _chart.DrawnPenId is not { } penId)
		{
			return null;
		}

		var page = _pager.Page;
		_layer = ChartNavigationController.LayerForWidth(
			page.ToUtc - page.FromUtc, _layer, _columns, visiblePenCount: 1);

		return new BandRequest(penId, page.FromUtc, page.ToUtc, _layer, _columns);
	}

	private IObservable<LandedRead> Read(BandRequest? request)
	{
		return request is null
			? Observable.Empty<LandedRead>()
			: Observable.Create<LandedRead>(async (observer, cancellation) =>
				observer.OnNext(new LandedRead(request, await ReadAsync(request, cancellation))));
	}

	/// <summary>Never throws: a thrown read comes back as a failed result, so it cannot end the read pipeline.</summary>
	private async Task<Result<PenHistoryEnvelope?>> ReadAsync(BandRequest request, CancellationToken cancellation)
	{
		var started = Stopwatch.GetTimestamp();
		Result<PenHistoryEnvelope?> result;

		try
		{
			var read = await _coordinator.QueryHistoryAsync(
				[request.PenId], request.FromUtc, request.ToUtc, request.Layer, request.Columns, cancellation);
			result = read.Map(envelopes => envelopes.FirstOrDefault(envelope => envelope.PenId == request.PenId));
		}
		catch (Exception readFailure)
		{
			result = Result.Fail(new ExceptionalError(readFailure));
		}

		_logger.LogDebug(
			"The minimap band read {Layer} at {Columns} columns in {Elapsed}",
			request.Layer,
			request.Columns,
			Stopwatch.GetElapsedTime(started));

		return result;
	}

	private void ApplyRead(LandedRead read)
	{
		var isSuperseded = !ReferenceEquals(read.Request, _latestRequest);
		if (isSuperseded)
		{
			return;
		}

		try
		{
			var span = read.Request.ToUtc - read.Request.FromUtc;
			_nextRead.Disposable = _uiScheduler.Schedule(NextReadDelay(span), TryRequestRead);
			ApplyResult(read.Result);
		}
		catch (Exception applyFailure)
		{
			ReportFailure(applyFailure);
		}
	}

	internal static TimeSpan NextReadDelay(TimeSpan span)
	{
		var delay = span / ReadDelayDivisor;

		return TimeSpan.FromTicks(Math.Clamp(delay.Ticks, _fastestRead.Ticks, _slowestRead.Ticks));
	}

	private void ApplyResult(Result<PenHistoryEnvelope?> result)
	{
		HasReadFailed = result.IsFailed;
		if (result.IsFailed)
		{
			_outage.Failed(result.Errors);

			return;
		}

		_outage.Succeeded();
		Band = result.Value;
	}

	private void ReportFailure(Exception failure)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	private sealed record BandRequest(int PenId, DateTime FromUtc, DateTime ToUtc, AggregationLayer Layer, int Columns);

	private sealed record LandedRead(BandRequest Request, Result<PenHistoryEnvelope?> Result);
}
