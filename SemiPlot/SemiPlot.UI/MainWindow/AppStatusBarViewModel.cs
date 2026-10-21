using System.Reactive;

using FluentResults;

using Microsoft.Extensions.Logging;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

namespace SemiPlot.UI.MainWindow;

/// <summary>
/// Current state and nothing else: whether the archive and the live source answer, and the layer the chart
/// reads. The connection entries it writes go to the message panel. <see cref="TrendWindow"/> builds and
/// disposes it.
/// </summary>
public sealed class AppStatusBarViewModel : ReactiveObject, IDisposable
{
	private readonly MessagePanelViewModel _messagePanel;
	private readonly ILogger<AppStatusBarViewModel> _logger;
	private readonly ChartNavigationController _navigation;
	private readonly ConnectionEntries _archiveEntries;
	private readonly ConnectionEntries _liveSourceEntries;
	private readonly IDisposable _connectionSubscription;
	private readonly IDisposable _liveSourceSubscription;

	/// <summary>
	/// Follows <paramref name="connectionStates"/> and <paramref name="liveSourceStates"/> (both already on the
	/// UI scheduler) and the layer of <paramref name="navigation"/>.
	/// </summary>
	public AppStatusBarViewModel(
		MessagePanelViewModel messagePanel,
		IObservable<ArchiveConnectionState> connectionStates,
		IObservable<LiveSourceState> liveSourceStates,
		ChartNavigationController navigation,
		ILogger<AppStatusBarViewModel> logger)
	{
		_messagePanel = messagePanel;
		_logger = logger;
		_navigation = navigation;
		_archiveEntries = new ConnectionEntries(messagePanel, logger, ConnectionRestored);
		_liveSourceEntries = new ConnectionEntries(messagePanel, logger, LiveConnectionRestored);

		ActiveLayer = navigation.ActiveLayer;
		navigation.WindowChanged += OnNavigationWindowChanged;
		_connectionSubscription = connectionStates.Subscribe(ApplyConnectionState);
		_liveSourceSubscription = liveSourceStates.Subscribe(ApplyLiveSourceState);
	}

	/// <summary>The indicator opens and closes the panel with the command the View menu also invokes.</summary>
	public ReactiveCommand<Unit, Unit> ToggleMessagePanelCommand => _messagePanel.ToggleCommand;

	public bool IsConnected
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(ConnectionText));
		}
	} = true;

	public string ConnectionText => IsConnected
		? Resources.StatusConnectionOk
		: Resources.StatusConnectionFault;

	public bool IsLiveConnected => LiveState.IsConnected;

	/// <summary>False while the source is still connecting, so the indicator keeps the bar's own colour then.</summary>
	public bool IsLiveFaulted => LiveState.Fault is not null;

	public string LiveConnectionText => LiveState switch
	{
		{ IsConnected: true } => Resources.StatusLiveConnectionOk,
		{ Fault: not null } => Resources.StatusLiveConnectionFault,
		_ => Resources.StatusLiveConnecting
	};

	public AggregationLayer ActiveLayer
	{
		get;
		private set
		{
			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(LayerText));
		}
	}

	public string LayerText => Resources.FormatStatusLayerFormat(LayerNameOf(ActiveLayer));

	private LiveSourceState LiveState
	{
		get;
		set
		{
			field = value;
			this.RaisePropertyChanged(nameof(IsLiveConnected));
			this.RaisePropertyChanged(nameof(IsLiveFaulted));
			this.RaisePropertyChanged(nameof(LiveConnectionText));
		}
	} = LiveSourceState.Connecting;

	public void Dispose()
	{
		_liveSourceSubscription.Dispose();
		_connectionSubscription.Dispose();
		_navigation.WindowChanged -= OnNavigationWindowChanged;
	}

	// PenCatalogueSync raises the state from inside its read loop, ahead of the read's apply, so a throw out of
	// this handler would skip that read's apply.
	private void ApplyConnectionState(ArchiveConnectionState state)
	{
		try
		{
			IsConnected = state.IsConnected;
			_archiveEntries.Apply(state.IsConnected, state.Fault);
		}
		catch (Exception handlerFailure)
		{
			_messagePanel.TryReportFailure(new ExceptionalError(handlerFailure), _logger);
		}
	}

	// The stream carries no onError, so a throw out of this handler would end on the UI scheduler.
	private void ApplyLiveSourceState(LiveSourceState state)
	{
		try
		{
			LiveState = state;
			_liveSourceEntries.Apply(state.IsConnected, state.Fault);
		}
		catch (Exception handlerFailure)
		{
			_messagePanel.TryReportFailure(new ExceptionalError(handlerFailure), _logger);
		}
	}

	private static string LayerNameOf(AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => Resources.StatusLayerRaw,
			AggregationLayer.Minute => Resources.StatusLayerMinute,
			AggregationLayer.Hour => Resources.StatusLayerHour,
			_ => throw new ArgumentOutOfRangeException(nameof(layer), layer, null)
		};
	}

	private static ArchiveFailureView ConnectionRestored()
	{
		return new ArchiveFailureView(
			Resources.StatusConnectionRestoredTitle,
			Resources.StatusConnectionRestoredDetail,
			string.Empty,
			MessageSeverity.Info);
	}

	private static ArchiveFailureView LiveConnectionRestored()
	{
		return new ArchiveFailureView(
			Resources.StatusLiveConnectionRestoredTitle,
			Resources.StatusLiveConnectionRestoredDetail,
			string.Empty,
			MessageSeverity.Info);
	}

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ActiveLayer = window.Layer;
	}

	/// <summary>One indicator's panel entries: each fault, and one recovery entry once a fault was seen.</summary>
	private sealed class ConnectionEntries(
		MessagePanelViewModel messagePanel,
		ILogger logger,
		Func<ArchiveFailureView> restored)
	{
		private bool _hasSeenFault;

		public void Apply(bool isConnected, IError? fault)
		{
			if (fault is not null)
			{
				_hasSeenFault = true;
				messagePanel.ReportFailure(fault, logger);

				return;
			}

			// The first state reports Connected too, so a recovery entry is owed only once a fault has been seen.
			if (!isConnected || !_hasSeenFault)
			{
				return;
			}

			var view = restored();

			// The one entry with no error behind it, so its log line is written here rather than by
			// ResultReporting.
			logger.LogInformation("{Title}. {Detail}", view.Title, view.Detail);

			// Cleared after the entry lands: a throw out of the report would otherwise leave the flag down and
			// no later Connected would write the recovery entry either.
			messagePanel.Report(view);
			_hasSeenFault = false;
		}
	}
}
