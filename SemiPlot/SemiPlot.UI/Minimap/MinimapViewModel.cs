using System.Reactive;
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
using SemiPlot.UI.Navigation;

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
	private readonly ObservableAsPropertyHelper<string?> _hint;
	private readonly ObservableAsPropertyHelper<IReadOnlyList<DateTime>> _operatorMarkerTimes;

	private bool _isExtentMissing;
	private DateTimeOffset _nextMissingExtentRead = DateTimeOffset.MinValue;
	private bool _isDisposed;

	public MinimapViewModel(
		TrendCoordinator coordinator,
		TrendChartViewModel chart,
		IScheduler uiScheduler,
		TimeZoneInfo archiveZone,
		MessagePanelViewModel messagePanel,
		ILogger<MinimapViewModel> logger)
	{
		_coordinator = coordinator;
		_chart = chart;
		_uiScheduler = uiScheduler;
		ArchiveZone = archiveZone;
		_messagePanel = messagePanel;
		_logger = logger;
		_extentOutage = new OutageReport("archive extent read", messagePanel, logger);
		PresenceFeed = new ArchivePresenceFeed(
			coordinator, chart.Navigation, archiveZone, uiScheduler, messagePanel, logger);
		Pager = new MinimapPager(chart.Navigation, PresenceFeed, Extents(), archiveZone);
		BandFeed = new MinimapBandFeed(coordinator, chart, Pager, () => HasExtent, uiScheduler, messagePanel, logger);

		_disposables.Add(FollowNavigation());
		_disposables.Add(this
			.WhenAnyValue(
				viewModel => viewModel.IsArchiveEmpty,
				viewModel => viewModel.HasExtent,
				viewModel => viewModel.Pager.Page,
				viewModel => viewModel.PresenceFeed.Spans,
				viewModel => viewModel.BandFeed.HasReadFailed,
				MinimapHint.For)
			.ToProperty(this, viewModel => viewModel.Hint, out _hint));
		_disposables.Add(chart.Markers.Changed
			.StartWith(Unit.Default)
			.Select(_ => OperatorMarkerTimesOf(chart.Markers))
			.DistinctUntilChanged(SequenceComparer<DateTime>.Instance)
			.ToProperty(this, viewModel => viewModel.OperatorMarkerTimes, out _operatorMarkerTimes));
		RefreshShadedRuns();
		_disposables.Add(chart.Markers.Changed.Subscribe(_ => RefreshShadedRuns(), ReportFailure));
		_disposables.Add(_missingExtentRead);
		_disposables.Add(BandFeed);
		_disposables.Add(PresenceFeed);
		_disposables.Add(Pager);
	}

	public DateTime ExtentFirst
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The minimap's right bound: the later of the extent's last sample and the chart's newest sample.</summary>
	public DateTime ExtentLast
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public bool HasExtent
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>True once an extent read answered with no rows and no extent was ever read.</summary>
	public bool IsArchiveEmpty
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	/// <summary>The archive's wall-clock zone, which the day calendar and the detail track read.</summary>
	public TimeZoneInfo ArchiveZone { get; }

	public MinimapBandFeed BandFeed { get; }

	public ArchivePresenceFeed PresenceFeed { get; }

	public MinimapPager Pager { get; }

	/// <summary>The detail track's hint (docs/architecture/trend-interaction.md#archive-overview-minimap).</summary>
	public string? Hint => _hint.Value;

	/// <summary>The operator's markers for the day calendar's marker ticks.</summary>
	public IReadOnlyList<DateTime> OperatorMarkerTimes => _operatorMarkerTimes.Value;

	/// <summary>The recipe runs the Day page shades; empty while the View menu hides recipe markers.</summary>
	public IReadOnlyList<RecipeRun> ShadedRuns
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = [];

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

	/// <summary>The day calendar over the extent's wall-clock days; null without an extent or a width.</summary>
	public DayCalendar? Calendar(double width)
	{
		return HasExtent
			? DayCalendar.Over(
				ArchiveClock.DayOf(ExtentFirst, ArchiveZone), ArchiveClock.DayOf(ExtentLast, ArchiveZone), width)
			: null;
	}

	private IDisposable FollowNavigation()
	{
		var navigation = _chart.Navigation;
		navigation.NewestSampleMoved += OnNewestSampleMoved;

		return Disposable.Create(() => navigation.NewestSampleMoved -= OnNewestSampleMoved);
	}

	private IObservable<(DateTime First, DateTime Last)?> Extents()
	{
		return this.WhenAnyValue(
			viewModel => viewModel.HasExtent,
			viewModel => viewModel.ExtentFirst,
			viewModel => viewModel.ExtentLast,
			(hasExtent, first, last) => hasExtent ? (first, last) : ((DateTime First, DateTime Last)?)null);
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
		IsArchiveEmpty = !HasExtent && result.Value.IsEmpty;

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
		BandFeed.RequestRead();
		PresenceFeed.ReadExtent(ExtentFirst, ExtentLast);
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

	private void OnNewestSampleMoved(object? sender, DateTime newestSample)
	{
		RefreshShadedRuns();
		if (HasExtent)
		{
			ExtentLast = MinimapGeometry.RightBound(ExtentLast, newestSample);
			PresenceFeed.FollowNewestSample();
		}
		else if (_isExtentMissing && _uiScheduler.Now >= _nextMissingExtentRead)
		{
			ReadMissingExtent();
		}

		Pager.FollowNewestSample();
	}

	private static IReadOnlyList<DateTime> OperatorMarkerTimesOf(ChartMarkers markers)
	{
		return [.. markers.OperatorMarkers.Select(marker => marker.TimeUtc)];
	}

	/// <summary>Reassigns only on a change, so a live sample redraws nothing while the last run is closed.</summary>
	private void RefreshShadedRuns()
	{
		var markers = _chart.Markers;
		var runs = markers.ShowsRecipeMarkers
			? MarkerNavigation.Runs(markers.All, _chart.Navigation.NewestSample)
			: [];
		if (!runs.SequenceEqual(ShadedRuns))
		{
			ShadedRuns = runs;
		}
	}
}
