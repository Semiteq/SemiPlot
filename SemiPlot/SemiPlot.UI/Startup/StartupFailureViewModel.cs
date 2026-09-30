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

	private readonly string? _configDirectory;
	private readonly ILoggerFactory _loggerFactory;
	private readonly ILogger<StartupFailureViewModel> _logger;

	public StartupFailureViewModel(
		ArchiveFailureView failure,
		string? configDirectory,
		ILoggerFactory loggerFactory)
	{
		Failure = failure;
		MessagePanel = new MessagePanelViewModel();
		_configDirectory = configDirectory;
		_loggerFactory = loggerFactory;
		_logger = loggerFactory.CreateLogger<StartupFailureViewModel>();

		_disposables.Add(MessagePanel);
		_disposables.Add(_aboutRequests);
		_disposables.Add(_exitRequests);
		_disposables.Add(_settingsRequests);

		_disposables.Add(ExitCommand = ReactiveCommand.Create(
			() => _exitRequests.OnNext(Unit.Default)));
		_disposables.Add(ShowAboutCommand = ReactiveCommand.Create(
			() => _aboutRequests.OnNext(AboutInfo.ForCurrentProcess())));
		_disposables.Add(ShowSettingsCommand = ReactiveCommand.CreateFromTask(
			RequestSettingsAsync, Observable.Return(configDirectory is not null)));
	}

	public ArchiveFailureView Failure { get; }

	/// <summary>The panel of this window alone; what the window opens reports here.</summary>
	public MessagePanelViewModel MessagePanel { get; }

	/// <summary>False after a failed argument parse, which leaves no directory to fix.</summary>
	public bool HasConfigDirectory => _configDirectory is not null;

	public IObservable<AboutInfo> AboutRequests => _aboutRequests.AsObservable();

	public IObservable<Unit> ExitRequests => _exitRequests.AsObservable();

	/// <summary>Each request carries a view model the listener owns and disposes when its dialog closes.</summary>
	public IObservable<SettingsViewModel> SettingsRequests => _settingsRequests.AsObservable();

	public ReactiveCommand<Unit, Unit> ExitCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowAboutCommand { get; }

	public ReactiveCommand<Unit, Unit> ShowSettingsCommand { get; }

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

		_settingsRequests.OnNext(await SettingsViewModel.OpenAsync(configDirectory, MessagePanel, _loggerFactory));
	}

	public void Dispose()
	{
		_disposables.Dispose();
	}
}
