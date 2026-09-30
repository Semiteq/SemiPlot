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
using SemiPlot.UI.Messages;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Navigation;
using SemiPlot.UI.PenEditor;
using SemiPlot.UI.Settings;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<AboutInfo> _aboutRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();
	private readonly Subject<SettingsViewModel> _settingsRequests = new();
	private readonly Subject<PenEditorViewModel> _penEditorRequests = new();

	private readonly string _configDirectory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<MainWindowViewModel> _logger;
	private readonly IPenCatalogueEditor _penCatalogueEditor;
	private readonly Action _readCatalogueNow;

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
		string configDirectory,
		ILoggerFactory loggerFactory,
		IPenCatalogueEditor penCatalogueEditor)
	{
		MessagePanel = messagePanel;
		StatusBar = statusBar;
		ChartViewModel = chartViewModel;
		MinimapViewModel = minimapViewModel;
		NavigationBarViewModel = navigationBarViewModel;
		LegendViewModel = legendViewModel;
		_readCatalogueNow = readCatalogueNow;
		_configDirectory = configDirectory;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<MainWindowViewModel>();
		_penCatalogueEditor = penCatalogueEditor;

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

	public ReactiveCommand<Unit, Unit> ToggleNavigationBarCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleLegendCommand { get; }

	public ReactiveCommand<Unit, Unit> ToggleMinimapCommand { get; }

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

	private async Task RequestSettingsAsync()
	{
		_settingsRequests.OnNext(await SettingsViewModel.OpenAsync(_configDirectory, MessagePanel, _loggerFactory));
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
