using System.Reactive;
using System.Reactive.Disposables;

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
/// Current state and nothing else: whether the archive answers, and the layer the chart reads. The
/// connection entries it writes go to the message panel.
/// </summary>
public sealed class AppStatusBarViewModel(
	MessagePanelViewModel messagePanel,
	ILogger<AppStatusBarViewModel> logger) : ReactiveObject, IDisposable
{
	private readonly SerialDisposable _layerSubscription = new();

	private IDisposable? _connectionSubscription;

	private bool _hasSeenFault;

	/// <summary>The indicator opens and closes the panel with the command the View menu also invokes.</summary>
	public ReactiveCommand<Unit, Unit> ToggleMessagePanelCommand => messagePanel.ToggleCommand;

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

	/// <summary>
	/// Binds the bar to the coordinator's republished state stream, which already arrives on the UI
	/// scheduler.
	/// </summary>
	public void TrackArchiveConnection(IObservable<ArchiveConnectionState> connectionStates)
	{
		if (_connectionSubscription is not null)
		{
			throw new InvalidOperationException(
				"The status bar's connection state is already bound. It has one writer, bound once.");
		}

		_connectionSubscription = connectionStates.Subscribe(ApplyConnectionState);
	}

	/// <summary>Follows one chart's layer, and stops following the chart it replaces.</summary>
	public void TrackLayer(ChartNavigationController? navigation)
	{
		if (navigation is null)
		{
			_layerSubscription.Disposable = null;

			return;
		}

		navigation.WindowChanged += OnNavigationWindowChanged;
		_layerSubscription.Disposable = Disposable.Create(
			() => navigation.WindowChanged -= OnNavigationWindowChanged);
		ActiveLayer = navigation.ActiveLayer;
	}

	public void Dispose()
	{
		_connectionSubscription?.Dispose();
		_layerSubscription.Dispose();
	}

	// TrendCoordinator forwards the connection stream with a bare Subscribe, so a throw out of this handler
	// would end that forwarding for the rest of the session rather than reaching an onError.
	private void ApplyConnectionState(ArchiveConnectionState state)
	{
		try
		{
			IsConnected = state.IsConnected;

			if (state.Fault is { } fault)
			{
				_hasSeenFault = true;
				messagePanel.ReportFailure(fault, logger);

				return;
			}

			// ArchiveConnectionState: every subscription's first tick reports Connected, so a recovery
			// entry is owed only once a fault has been seen.
			if (_hasSeenFault)
			{
				var restored = ConnectionRestored();

				// The one entry with no error behind it, so its log line is written here rather than by
				// ResultReporting.
				logger.LogInformation("{Title}. {Detail}", restored.Title, restored.Detail);

				// Cleared after the entry lands: a throw out of the report would otherwise leave the flag
				// down and no later Connected would write the recovery entry either.
				messagePanel.Report(restored);
				_hasSeenFault = false;
			}
		}
		catch (Exception handlerFailure)
		{
			messagePanel.TryReportFailure(new ExceptionalError(handlerFailure), logger);
		}
	}

	private static string LayerNameOf(AggregationLayer layer)
	{
		return layer switch
		{
			AggregationLayer.Raw => Resources.StatusLayerRaw,
			AggregationLayer.Minute => Resources.StatusLayerMinute,
			AggregationLayer.Hour => Resources.StatusLayerHour,
			AggregationLayer.Day => Resources.StatusLayerDay,
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

	private void OnNavigationWindowChanged(object? sender, NavigationWindow window)
	{
		ActiveLayer = window.Layer;
	}
}
