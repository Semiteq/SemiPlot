using System.Globalization;
using System.Reactive.Concurrency;
using System.Reactive.Disposables;
using System.Reactive.Linq;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.Minimap;

public sealed class MinimapViewModel : ReactiveObject, IDisposable
{
	private static readonly TimeSpan _missingExtentRetryInterval = TimeSpan.FromSeconds(60.0);

	private readonly TrendCoordinator _coordinator;
	private readonly TrendChartViewModel _chart;
	private readonly IScheduler _uiScheduler;
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<MinimapViewModel> _logger;
	private readonly OutageReport _extentOutage;
	private readonly CompositeDisposable _disposables = [];
	private readonly SerialDisposable _missingExtentRead = new();

	private bool _isExtentMissing;
	private DateTimeOffset _nextMissingExtentRead = DateTimeOffset.MinValue;
	private bool _isDisposed;

	public MinimapViewModel(
		TrendCoordinator coordinator,
		TrendChartViewModel chart,
		IScheduler uiScheduler,
		MessagePanelViewModel messagePanel,
		ILogger<MinimapViewModel> logger)
	{
		_coordinator = coordinator;
		_chart = chart;
		_uiScheduler = uiScheduler;
		_messagePanel = messagePanel;
		_logger = logger;
		_extentOutage = new OutageReport("archive extent read", messagePanel, logger);
		BandFeed = new MinimapBandFeed(coordinator, chart, ReadBounds, uiScheduler, messagePanel, logger);

		_disposables.Add(FollowNavigation());
		_disposables.Add(_missingExtentRead);
		_disposables.Add(BandFeed);
	}

	public DateTime ExtentFirst
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ExtentFirstLabel));
			this.RaisePropertyChanged(nameof(HoverLabel));
		}
	}

	/// <summary>The strip's right bound: the later of the extent's last sample and the chart's newest sample.</summary>
	public DateTime ExtentLast
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ExtentLastLabel));
			this.RaisePropertyChanged(nameof(HoverLabel));
		}
	}

	public bool HasExtent
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ExtentFirstLabel));
			this.RaisePropertyChanged(nameof(ExtentLastLabel));
		}
	}

	public string ExtentFirstLabel => HasExtent ? FormatTime(ExtentFirst) : string.Empty;

	public string ExtentLastLabel => HasExtent ? FormatTime(ExtentLast) : string.Empty;

	public double WindowStartFraction
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public double WindowWidthFraction
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = 1.0;

	public double? HoverFraction
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(HoverLabel));
		}
	}

	public string HoverLabel => HoverFraction is { } fraction
		? FormatTime(MinimapGeometry.TimeAtFraction(ExtentFirst, ExtentLast, fraction))
		: string.Empty;

	public MinimapBandFeed BandFeed { get; }

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_disposables.Dispose();
	}

	/// <summary>Reads the archive's extent and returns the read once the strip has applied it.</summary>
	public async Task<Result<ArchiveExtent>> LoadExtentAsync()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var result = await _coordinator.QueryArchiveExtentAsync();
		await Observable.Start(() => ApplyExtent(result), _uiScheduler);

		return result;
	}

	public void NavigateToFraction(double fraction)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!HasExtent)
		{
			return;
		}

		var target = MinimapGeometry.TimeAtFraction(ExtentFirst, ExtentLast, fraction);
		var navigation = _chart.Navigation;
		var currentCenter = navigation.From + ((navigation.To - navigation.From) / 2.0);
		navigation.PanBy(target - currentCenter);
	}

	public void HoverAt(double fraction)
	{
		if (!HasExtent)
		{
			return;
		}

		HoverFraction = fraction;
	}

	public void ClearHover()
	{
		HoverFraction = null;
	}

	private IDisposable FollowNavigation()
	{
		var navigation = _chart.Navigation;
		navigation.WindowChanged += OnNavigationWindowChanged;
		navigation.NewestSampleMoved += OnNewestSampleMoved;

		return Disposable.Create(() =>
		{
			navigation.WindowChanged -= OnNavigationWindowChanged;
			navigation.NewestSampleMoved -= OnNewestSampleMoved;
		});
	}

	private (DateTime First, DateTime Last)? ReadBounds()
	{
		return HasExtent ? (ExtentFirst, ExtentLast) : null;
	}

	private void ApplyExtent(Result<ArchiveExtent> result)
	{
		if (_isDisposed)
		{
			return;
		}

		_isExtentMissing = !HasExtent && (result.IsFailed || result.Value.IsEmpty);

		if (result.IsFailed)
		{
			_extentOutage.Failed(result.Errors);

			return;
		}

		_extentOutage.Succeeded();

		// An empty extent is a normal state of a fresh archive: leave HasExtent false so the strip stays
		// blank.
		if (result.Value.IsEmpty)
		{
			return;
		}

		var navigation = _chart.Navigation;
		ExtentFirst = result.Value.FirstUtc;
		ExtentLast = MinimapGeometry.RightBound(result.Value.LastUtc, navigation.NewestSample);
		HasExtent = true;
		RefreshWindowFraction(navigation.From, navigation.To);
		BandFeed.RequestRead();
	}

	private void ReadMissingExtent()
	{
		_isExtentMissing = false;
		_nextMissingExtentRead = _uiScheduler.Now + _missingExtentRetryInterval;
		_missingExtentRead.Disposable = Observable
			.Create<Result<ArchiveExtent>>(async observer => observer.OnNext(await _coordinator.QueryArchiveExtentAsync()))
			.ObserveOn(_uiScheduler)
			.Subscribe(TryApplyExtent, ReportFailure);
	}

	private void TryApplyExtent(Result<ArchiveExtent> result)
	{
		try
		{
			ApplyExtent(result);
		}
		catch (Exception applyFailure)
		{
			ReportFailure(applyFailure);
		}
	}

	private void ReportFailure(Exception failure)
	{
		_messagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	private static string FormatTime(DateTime utc)
	{
		return utc.ToLocalTime().ToString("MMM d HH:mm", CultureInfo.CurrentCulture);
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		RefreshWindowFraction(window.From, window.To);
	}

	private void OnNewestSampleMoved(object? sender, DateTime newestSample)
	{
		if (!HasExtent)
		{
			var isRetryDue = _uiScheduler.Now >= _nextMissingExtentRead;
			if (_isExtentMissing && isRetryDue)
			{
				ReadMissingExtent();
			}

			return;
		}

		var rightBound = MinimapGeometry.RightBound(ExtentLast, newestSample);
		if (rightBound == ExtentLast)
		{
			return;
		}

		ExtentLast = rightBound;
		RefreshWindowFraction(_chart.Navigation.From, _chart.Navigation.To);
	}

	private void RefreshWindowFraction(DateTime from, DateTime to)
	{
		if (!HasExtent)
		{
			return;
		}

		var (start, width) = MinimapGeometry.WindowFraction(ExtentFirst, ExtentLast, from, to);
		WindowStartFraction = start;
		WindowWidthFraction = width;
	}
}
