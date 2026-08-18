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

// Queries the extent through the coordinator pass-through, never holding the IDataProvider directly.
public sealed class MinimapViewModel : ReactiveObject, IDisposable
{
	private readonly TrendCoordinator _coordinator;
	private readonly CompositeDisposable _disposables = new();
	private readonly ILogger<MinimapViewModel> _logger;
	private readonly ChartNavigationController _navigation;
	private readonly IScheduler _uiScheduler;

	private bool _isDisposed;
	private double _windowStartFraction;
	private double _windowWidthFraction = 1.0;

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

	public DateTime ExtentFirst { get; private set; }

	public DateTime ExtentLast { get; private set; }

	public bool HasExtent { get; private set; }

	public string ExtentFirstLabel => HasExtent ? FormatEndpoint(ExtentFirst) : string.Empty;

	public string ExtentLastLabel => HasExtent ? FormatEndpoint(ExtentLast) : string.Empty;

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

	public void Dispose()
	{
		if (_isDisposed)
		{
			return;
		}

		_isDisposed = true;
		_disposables.Dispose();
	}

	public async Task LoadExtentAsync()
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		var result = await _coordinator.QueryArchiveExtentAsync();
		_uiScheduler.Schedule(() => ApplyExtent(result));
	}

	public void NavigateToFraction(double fraction)
	{
		ObjectDisposedException.ThrowIf(_isDisposed, this);

		if (!HasExtent)
		{
			return;
		}

		var target = MinimapGeometry.TimeAtFraction(ExtentFirst, ExtentLast, fraction);
		var currentCenter = _navigation.From + ((_navigation.To - _navigation.From) / 2.0);
		_navigation.PanBy(target - currentCenter);
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

		// An empty extent is a normal state of a fresh archive: leave HasExtent false so the strip stays blank.
		if (result.Value.IsEmpty)
		{
			return;
		}

		ExtentFirst = result.Value.FirstUtc;
		ExtentLast = result.Value.LastUtc;
		HasExtent = true;
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
		if (!HasExtent)
		{
			return;
		}

		var (start, width) = MinimapGeometry.WindowFraction(ExtentFirst, ExtentLast, from, to);
		WindowStartFraction = start;
		WindowWidthFraction = width;
	}
}
