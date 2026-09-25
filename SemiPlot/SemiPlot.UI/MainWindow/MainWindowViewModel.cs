using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Navigation;
using SemiPlot.UI.Settings;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<AboutInfo> _aboutRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();
	private readonly Subject<SettingsViewModel> _settingsRequests = new();

	private readonly string? _configDirectory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<MainWindowViewModel> _logger;

	public MainWindowViewModel(
		MessagePanelViewModel messagePanel,
		AppStatusBarViewModel statusBar,
		string? configDirectory,
		ILoggerFactory loggerFactory)
	{
		MessagePanel = messagePanel;
		StatusBar = statusBar;
		_configDirectory = configDirectory;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<MainWindowViewModel>();

		_disposables.Add(_aboutRequests);
		_disposables.Add(_exitRequests);
		_disposables.Add(_settingsRequests);

		_disposables.Add(ToggleNavigationBarCommand = ReactiveCommand.Create(
			() => { IsNavigationBarVisible = !IsNavigationBarVisible; }));
		_disposables.Add(ToggleLegendCommand = ReactiveCommand.Create(
			() => { IsLegendVisible = !IsLegendVisible; }));
		_disposables.Add(ToggleMinimapCommand = ReactiveCommand.Create(
			() => { IsMinimapVisible = !IsMinimapVisible; }));
		_disposables.Add(ExitCommand = ReactiveCommand.Create(
			() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ShowAboutCommand = ReactiveCommand.Create(
			() => _aboutRequests.OnNext(AboutInfo.ForCurrentProcess())));
		_disposables.Add(ShowSettingsCommand = ReactiveCommand.CreateFromTask(
			RequestSettingsAsync,
			Observable.Return(configDirectory is not null)));
	}

	/// <summary>The process-wide panel, owned by the container and shown in the window's panel row.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	/// <summary>The bar's connection state and layer, owned by the container and shown in the status row.</summary>
	public AppStatusBarViewModel StatusBar { get; }

	/// <summary>Opening a window is view work, so the window listens and this view model only asks.</summary>
	public IObservable<AboutInfo> AboutRequests => _aboutRequests.AsObservable();

	public IObservable<Unit> ExitRequests => _exitRequests.AsObservable();

	/// <summary>Each request carries a view model the listener owns and disposes when its dialog closes.</summary>
	public IObservable<SettingsViewModel> SettingsRequests => _settingsRequests.AsObservable();

	/// <summary>
	/// Set only on a failed startup, before a chart is ever built: the message panel shows it and the
	/// chart area renders empty.
	/// </summary>
	public ArchiveFailureView? StartupFailure
	{
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(HasStartupFailure));
		}
	}

	public bool HasStartupFailure => StartupFailure is not null;

	public bool IsNavigationBarVisible
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = true;

	public bool IsLegendVisible
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = true;

	public bool IsMinimapVisible
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	} = true;

	public TrendChartViewModel? ChartViewModel
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public NavigationBarViewModel? NavigationBarViewModel
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public TrendLegendViewModel? LegendViewModel
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public MinimapViewModel? MinimapViewModel
	{
		get;
		private set => this.RaiseAndSetIfChanged(ref field, value);
	}

	public ReactiveCommand<Unit, Unit> ToggleNavigationBarCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleLegendCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleMinimapCommand { get; }

	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }

	/// <summary>Cannot execute without a configuration directory, which a failed argument parse leaves.</summary>
	public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }

	/// <summary>
	/// Replaces the chart and the two view models built from it, and re-points the status bar at its layer.
	/// A method rather than a setter: a binding write must not dispose a chart or reach into another bar.
	/// </summary>
	public void SetChart(TrendChartViewModel? chartViewModel)
	{
		if (ReferenceEquals(ChartViewModel, chartViewModel))
		{
			return;
		}

		NavigationBarViewModel?.Dispose();
		LegendViewModel?.Dispose();
		ChartViewModel?.Dispose();

		ChartViewModel = chartViewModel;

		NavigationBarViewModel = chartViewModel is null ? null : new NavigationBarViewModel(chartViewModel);
		LegendViewModel = chartViewModel is null ? null : new TrendLegendViewModel(chartViewModel);
		StatusBar.TrackLayer(chartViewModel?.Navigation);
	}

	/// <summary>
	/// Replaces the minimap and disposes the one it replaces, for the reason <see cref="SetChart"/> carries.
	/// </summary>
	public void SetMinimap(MinimapViewModel? minimapViewModel)
	{
		if (ReferenceEquals(MinimapViewModel, minimapViewModel))
		{
			return;
		}

		MinimapViewModel?.Dispose();
		MinimapViewModel = minimapViewModel;
	}

	/// <summary>The window's own code-behind route to the panel, for a throw it cannot let escape.</summary>
	public void ReportFailure(Exception failure)
	{
		MessagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	private async Task RequestSettingsAsync()
	{
		if (_configDirectory is not { } configDirectory)
		{
			return;
		}

		var (app, connection) = await Task.Run(() => SettingsSave.ReadOwned(configDirectory));

		_settingsRequests.OnNext(new SettingsViewModel(
			configDirectory, app, connection, MessagePanel, _loggerFactory.CreateLogger<SettingsViewModel>()));
	}

	public void Dispose()
	{
		_disposables.Dispose();
		NavigationBarViewModel?.Dispose();
		LegendViewModel?.Dispose();
		MinimapViewModel?.Dispose();
		ChartViewModel?.Dispose();
	}
}
