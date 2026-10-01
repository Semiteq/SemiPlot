using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Navigation;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;
using SemiPlot.UI.Startup;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<AboutInfo> _aboutRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();
	private readonly Subject<SettingsViewModel> _settingsRequests = new();
	private readonly Subject<PenEditorViewModel> _penEditorRequests = new();

	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<MainWindowViewModel> _logger;
	private readonly IPenCatalogueEditor _penCatalogueEditor;
	private readonly Action _readCatalogueNow;
	private readonly InstanceLauncher _instanceLauncher;
	private readonly ObservableAsPropertyHelper<string> _penScaleHeader;

	/// <summary>
	/// The window's parts are built and disposed by <see cref="TrendWindow"/>; this view model holds them for the
	/// view and disposes only what it creates itself.
	/// </summary>
	public MainWindowViewModel(
		MessagePanelViewModel messagePanel,
		AppStatusBarViewModel statusBar,
		TrendChartViewModel chartViewModel,
		MinimapViewModel minimapViewModel,
		NavigationBarViewModel navigationBarViewModel,
		TrendLegendViewModel legendViewModel,
		Action readCatalogueNow,
		ILoggerFactory loggerFactory,
		IPenCatalogueEditor penCatalogueEditor,
		InstanceLauncher instanceLauncher)
	{
		MessagePanel = messagePanel;
		StatusBar = statusBar;
		ChartViewModel = chartViewModel;
		MinimapViewModel = minimapViewModel;
		NavigationBarViewModel = navigationBarViewModel;
		LegendViewModel = legendViewModel;
		_readCatalogueNow = readCatalogueNow;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<MainWindowViewModel>();
		_penCatalogueEditor = penCatalogueEditor;
		_instanceLauncher = instanceLauncher;

		_disposables.Add(_penScaleHeader = ChartViewModel
			.WhenAnyValue(chart => chart.DrawnPenId, chart => chart.Pens, (penId, _) => PenScaleHeaderFor(penId))
			.ToProperty(this, viewModel => viewModel.PenScaleHeader));

		_disposables.Add(_aboutRequests);
		_disposables.Add(_exitRequests);
		_disposables.Add(_settingsRequests);
		_disposables.Add(_penEditorRequests);

		_disposables.Add(ToggleNavigationBarCommand = ReactiveCommand.Create(
			() => { IsNavigationBarVisible = !IsNavigationBarVisible; }));
		_disposables.Add(ToggleLegendCommand = ReactiveCommand.Create(
			() => { IsLegendVisible = !IsLegendVisible; }));
		_disposables.Add(ToggleMinimapCommand = ReactiveCommand.Create(
			() => { IsMinimapVisible = !IsMinimapVisible; }));
		_disposables.Add(AutoscaleCommand = ReactiveCommand.Create(
			() => ChartViewModel.AutoscaleActivePen()));
		_disposables.Add(InitialScaleCommand = ReactiveCommand.Create(
			() => ChartViewModel.RestoreInitialScale()));
		_disposables.Add(NewWindowCommand = ReactiveCommand.Create(() => { StartCopy(); }));
		_disposables.Add(ExitCommand = ReactiveCommand.Create(
			() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ShowAboutCommand = ReactiveCommand.Create(
			() => _aboutRequests.OnNext(AboutInfo.ForCurrentProcess())));
		_disposables.Add(ShowSettingsCommand = ReactiveCommand.CreateFromTask(RequestSettingsAsync));
		_disposables.Add(ShowPenEditorCommand = ReactiveCommand.CreateFromTask(RequestPenEditorAsync));
	}

	/// <summary>The process-wide panel, owned by the container and shown in the window's panel row.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	public AppStatusBarViewModel StatusBar { get; }

	/// <summary>Opening a window is view work, so the window listens and this view model only asks.</summary>
	public IObservable<AboutInfo> AboutRequests => _aboutRequests.AsObservable();

	public IObservable<Unit> ExitRequests => _exitRequests.AsObservable();

	/// <summary>Each request carries a view model the listener owns and disposes when its dialog closes.</summary>
	public IObservable<SettingsViewModel> SettingsRequests => _settingsRequests.AsObservable();

	/// <summary>Each request carries a view model the listener owns and disposes when its window closes.</summary>
	public IObservable<PenEditorViewModel> PenEditorRequests => _penEditorRequests.AsObservable();

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

	public TrendChartViewModel ChartViewModel { get; }

	public NavigationBarViewModel NavigationBarViewModel { get; }

	public TrendLegendViewModel LegendViewModel { get; }

	public MinimapViewModel MinimapViewModel { get; }

	/// <summary>The View menu's scale submenu header; it names the pen whose axis the chart draws.</summary>
	public string PenScaleHeader => _penScaleHeader.Value;

	public ReactiveCommand<Unit, Unit> ToggleNavigationBarCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleLegendCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleMinimapCommand { get; }

	public ReactiveCommand<Unit, Unit> AutoscaleCommand { get; }

	public ReactiveCommand<Unit, Unit> InitialScaleCommand { get; }

	/// <summary>Starts another instance and keeps this window.</summary>
	public ReactiveCommand<Unit, Unit> NewWindowCommand { get; }

	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }

	/// <summary>Reads the catalogue and opens the editor over it.</summary>
	public ReactiveCommand<Unit, Unit> ShowPenEditorCommand { get; }

	/// <summary>The window's own code-behind route to the panel, for a throw it cannot let escape.</summary>
	public void ReportFailure(Exception failure)
	{
		MessagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	public void RestartApplication()
	{
		if (StartCopy())
		{
			_exitRequests.OnNext(Unit.Default);
		}
	}

	private bool StartCopy()
	{
		var started = _instanceLauncher.Start();

		MessagePanel.ReportFailure(started, _logger);

		return started.IsSuccess;
	}

	private string PenScaleHeaderFor(int? drawnPenId)
	{
		return drawnPenId is { } penId && ChartViewModel.FindPen(penId) is { } state
			? Resources.FormatMenuViewPenScaleFormat(state.Pen.Name)
			: Resources.MenuViewPenScale;
	}

	private async Task RequestSettingsAsync()
	{
		_settingsRequests.OnNext(await SettingsViewModel.OpenAsync(
			_instanceLauncher.ConfigDirectory, MessagePanel, _loggerFactory, RestartApplication));
	}

	private async Task RequestPenEditorAsync()
	{
		var catalogue = await _penCatalogueEditor.ReadAsync();

		if (catalogue.IsFailed)
		{
			MessagePanel.ReportFailure(catalogue, _logger);

			return;
		}

		_penEditorRequests.OnNext(new PenEditorViewModel(
			_penCatalogueEditor,
			catalogue.Value,
			MessagePanel,
			_loggerFactory.CreateLogger<PenEditorViewModel>(),
			_readCatalogueNow));
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}
}
