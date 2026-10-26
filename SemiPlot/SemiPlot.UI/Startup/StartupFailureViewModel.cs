using System.Reactive;
using System.Reactive.Disposables;
using System.Reactive.Linq;
using System.Reactive.Subjects;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Settings;

namespace SemiPlot.UI.Startup;

public sealed class StartupFailureViewModel : ReactiveObject, IDisposable
{
	private readonly Subject<AboutInfo> _aboutRequests = new();
	private readonly CompositeDisposable _disposables = [];
	private readonly Subject<Unit> _exitRequests = new();
	private readonly Subject<SettingsViewModel> _settingsRequests = new();

	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<StartupFailureViewModel> _logger;
	private readonly InstanceLauncher? _instanceLauncher;

	public StartupFailureViewModel(
		ArchiveFailureView failure,
		InstanceLauncher? instanceLauncher,
		ILoggerFactory loggerFactory)
	{
		Failure = failure;
		MessagePanel = new MessagePanelViewModel();
		OffersSettings = instanceLauncher is not null && EverySavedKeyPresent(instanceLauncher.ConfigDirectory);
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<StartupFailureViewModel>();
		_instanceLauncher = instanceLauncher;

		_disposables.Add(MessagePanel);
		_disposables.Add(_aboutRequests);
		_disposables.Add(_exitRequests);
		_disposables.Add(_settingsRequests);

		_disposables.Add(RestartCommand = ReactiveCommand.Create(
			RestartApplication, Observable.Return(OffersRestart)));
		_disposables.Add(ExitCommand = ReactiveCommand.Create(
			() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ShowAboutCommand = ReactiveCommand.Create(
			() => _aboutRequests.OnNext(AboutInfo.ForCurrentProcess())));
		_disposables.Add(ShowSettingsCommand = ReactiveCommand.CreateFromTask(
			RequestSettingsAsync, Observable.Return(OffersSettings)));
	}

	public ArchiveFailureView Failure { get; }

	/// <summary>The panel of this window alone; what the window opens reports here.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	/// <summary>
	/// True only when both configuration sections read as the files carry them; the dialog edits existing keys and
	/// creates no file, so any other state leaves the failure text alone to instruct the operator.
	/// </summary>
	public bool OffersSettings { get; }

	/// <summary>True only when the launch keys parsed, so there is a copy to start.</summary>
	public bool OffersRestart => _instanceLauncher is not null;

	public IObservable<AboutInfo> AboutRequests => _aboutRequests.AsObservable();

	public IObservable<Unit> ExitRequests => _exitRequests.AsObservable();

	/// <summary>Each request carries a view model the listener owns and disposes when its dialog closes.</summary>
	public IObservable<SettingsViewModel> SettingsRequests => _settingsRequests.AsObservable();

	public ReactiveCommand<Unit, Unit> RestartCommand { get; }

	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }

	/// <summary>The window's own code-behind route to the panel, for a throw it cannot let escape.</summary>
	public void ReportFailure(Exception failure)
	{
		MessagePanel.TryReportFailure(new ExceptionalError(failure), _logger);
	}

	public void RestartApplication()
	{
		if (_instanceLauncher is { } launcher)
		{
			Restart(launcher);
		}
	}

	private static bool EverySavedKeyPresent(string configDirectory)
	{
		var (app, connection) = SettingsSave.ReadOwned(configDirectory);

		return app.IsSuccess && connection.IsSuccess && SettingsSave.FirstAbsentKey(app.Value, connection.Value) is null;
	}

	private void Restart(InstanceLauncher launcher)
	{
		if (StartCopy(launcher))
		{
			_exitRequests.OnNext(Unit.Default);
		}
	}

	private bool StartCopy(InstanceLauncher launcher)
	{
		var started = launcher.Start();

		MessagePanel.ReportFailure(started, _logger);

		return started.IsSuccess;
	}

	private async Task RequestSettingsAsync()
	{
		if (_instanceLauncher is not { } launcher)
		{
			return;
		}

		_settingsRequests.OnNext(await SettingsViewModel.OpenAsync(
			launcher.ConfigDirectory, MessagePanel, _loggerFactory, () => Restart(launcher)));
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}
}
