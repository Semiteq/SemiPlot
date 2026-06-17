using System.Reactive.Concurrency;
using System.Reactive.Disposables;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Minimap;

// Drives the archive-overview strip: queries the full extent through the coordinator pass-through
// (never holding the IDataProvider), tracks the chart's navigation window for the highlight, and
// recenters navigation on a strip click through the same controller the chart navigates with.
public sealed class MinimapViewModel : ReactiveObject, IDisposable
{
	private readonly TrendCoordinator _coordinator;
	private readonly ChartNavigationController _navigation;
	private readonly IScheduler _uiScheduler;
	private readonly ILogger<MinimapViewModel> _logger;
	private readonly CompositeDisposable _disposables = new();

	private DateTime _extentFirst;
	private DateTime _extentLast;
	private bool _hasExtent;
	private double _windowStartFraction;
	private double _windowWidthFraction = 1.0;
	private bool _isDisposed;

	public MinimapViewModel(
		TrendCoordinator coordinator,
		ChartNavigationController navigation,
		IScheduler uiScheduler,
		ILogger<MinimapViewModel> logger)
	{
		ArgumentNullException.ThrowIfNull(coordinator);
		ArgumentNullException.ThrowIfNull(navigation);
		ArgumentNullException.ThrowIfNull(uiScheduler);
		ArgumentNullException.ThrowIfNull(logger);

		_coordinator = coordinator;
		_navigation = navigation;
		_uiScheduler = uiScheduler;
		_logger = logger;

		_navigation.WindowChanged += OnNavigationWindowChanged;
		_disposables.Add(Disposable.Create(() => _navigation.WindowChanged -= OnNavigationWindowChanged));
	}

	public DateTime ExtentFirst => _extentFirst;

	public DateTime ExtentLast => _extentLast;

	public bool HasExtent => _hasExtent;

	// Compact local-time labels drawn at the strip ends so the overview reads as a timeline rather than a
	// blank bar; empty until the extent loads so the view shows no misleading endpoints.
	public string ExtentFirstLabel => _hasExtent ? FormatEndpoint(_extentFirst) : string.Empty;

	public string ExtentLastLabel => _hasExtent ? FormatEndpoint(_extentLast) : string.Empty;

	public double WindowStartFraction
	{
		get => _windowStartFraction;
		private set => this.RaiseAndSetIfChanged(ref _windowStartFraction, value);
	}

	public double WindowWidthFraction
	{
		get => _windowWidthFraction;
		private set => this.RaiseAndSetIfChanged(ref _windowWidthFraction, value);
	}

	// Applies the result on the UI scheduler. Call once after construction.
	public async Task LoadExtentAsync()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var result = await _coordinator.QueryArchiveExtentAsync();
		_uiScheduler.Schedule(() => ApplyExtent(result));
	}

	// Recenters the navigation window (keeping width) on the timestamp at the given strip fraction.
	public void NavigateToFraction(double fraction)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!_hasExtent)
		{
			return;
		}

		var target = MinimapGeometry.TimeAtFraction(_extentFirst, _extentLast, fraction);
		var currentCenter = _navigation.From + ((_navigation.To - _navigation.From) / 2.0);
		_navigation.PanBy(target - currentCenter);
	}

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_disposables.Dispose();
	}

	private void ApplyExtent(Result<ArchiveExtent> result)
	{
		if (_isDisposed)
		{
			return;
		}

		if (result.IsFailed)
		{
			_logger.LogWarning(
				"Archive extent query failed; the minimap strip will not reflect the archive depth: {Errors}",
				string.Join("; ", result.Errors.Select(error => error.Message)));
			return;
		}

		_extentFirst = result.Value.FirstUtc;
		_extentLast = result.Value.LastUtc;
		_hasExtent = true;
		this.RaisePropertyChanged(nameof(ExtentFirst));
		this.RaisePropertyChanged(nameof(ExtentLast));
		this.RaisePropertyChanged(nameof(HasExtent));
		this.RaisePropertyChanged(nameof(ExtentFirstLabel));
		this.RaisePropertyChanged(nameof(ExtentLastLabel));
		RefreshWindowFraction(_navigation.From, _navigation.To);
	}

	private static string FormatEndpoint(DateTime utc)
	{
		return utc.ToLocalTime().ToString("MMM d HH:mm");
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		RefreshWindowFraction(window.From, window.To);
	}

	private void RefreshWindowFraction(DateTime from, DateTime to)
	{
		if (!_hasExtent)
		{
			return;
		}

		var (start, width) = MinimapGeometry.WindowFraction(_extentFirst, _extentLast, from, to);
		WindowStartFraction = start;
		WindowWidthFraction = width;
	}
}
