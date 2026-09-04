using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Toolbar;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _subscriptions = new();

	private ObservableAsPropertyHelper<string?>? _archiveConnectionMessage;
	private TrendChartViewModel? _chartViewModel;
	private TrendLegendViewModel? _legendViewModel;
	private MinimapViewModel? _minimapViewModel;
	private TrendToolbarViewModel? _toolbarViewModel;
	private ArchiveFailureView? _startupFailure;

	// Not re-notified when pens change after assignment.
	public int PenCount => ChartViewModel?.Pens.Count ?? 0;

	/// <summary>
	/// Chart built, no pens: unfinished provisioning shown as a state, not an error.
	/// </summary>
	public bool IsCatalogueEmpty => ChartViewModel is not null && PenCount == 0;

	/// <summary>
	/// Set only on a failed startup, before a chart is ever built: the message panel shows it and the
	/// chart area renders empty.
	/// </summary>
	public ArchiveFailureView? StartupFailure
	{
		get => _startupFailure;
		set
		{
			this.RaiseAndSetIfChanged(ref _startupFailure, value);
			this.RaisePropertyChanged(nameof(HasStartupFailure));
		}
	}

	public bool HasStartupFailure => StartupFailure is not null;

	/// <summary>
	/// What the live-edge poll reports about its own connection: null while the archive answers. Its only
	/// writer is the stream <see cref="ObserveArchiveConnection"/> binds.
	/// </summary>
	public string? ArchiveConnectionMessage => _archiveConnectionMessage?.Value;

	public bool HasArchiveConnectionMessage => ArchiveConnectionMessage is not null;

	/// <summary>
	/// Binds the connection row to the coordinator's republished state stream, which already arrives on
	/// the UI scheduler. Called once, at startup: a second bind would give the row a second writer.
	/// </summary>
	public void ObserveArchiveConnection(IObservable<ArchiveConnectionState> connectionStates)
	{
		if (_archiveConnectionMessage is not null)
		{
			throw new InvalidOperationException(
				"The archive connection row is already bound. It has one writer, bound once.");
		}

		_archiveConnectionMessage = connectionStates
			.Select(state => state.Fault is { } fault ? ArchiveFailureMapper.Describe(fault) : null)
			.ToProperty(this, viewModel => viewModel.ArchiveConnectionMessage);
		_subscriptions.Add(_archiveConnectionMessage);

		_subscriptions.Add(this
			.WhenAnyValue(viewModel => viewModel.ArchiveConnectionMessage)
			.Subscribe(_ => this.RaisePropertyChanged(nameof(HasArchiveConnectionMessage))));
	}

	public TrendChartViewModel? ChartViewModel
	{
		get => _chartViewModel;
		set
		{
			if (ReferenceEquals(_chartViewModel, value))
			{
				return;
			}

			_toolbarViewModel?.Dispose();
			_legendViewModel?.Dispose();
			_chartViewModel?.Dispose();

			this.RaiseAndSetIfChanged(ref _chartViewModel, value);
			this.RaisePropertyChanged(nameof(PenCount));
			this.RaisePropertyChanged(nameof(IsCatalogueEmpty));

			ToolbarViewModel = value is null ? null : new TrendToolbarViewModel(value);
			LegendViewModel = value is null ? null : new TrendLegendViewModel(value);
		}
	}

	public TrendToolbarViewModel? ToolbarViewModel
	{
		get => _toolbarViewModel;
		private set => this.RaiseAndSetIfChanged(ref _toolbarViewModel, value);
	}

	public TrendLegendViewModel? LegendViewModel
	{
		get => _legendViewModel;
		private set => this.RaiseAndSetIfChanged(ref _legendViewModel, value);
	}

	public MinimapViewModel? MinimapViewModel
	{
		get => _minimapViewModel;
		set
		{
			_minimapViewModel?.Dispose();
			this.RaiseAndSetIfChanged(ref _minimapViewModel, value);
		}
	}

	public void Dispose()
	{
		_subscriptions.Dispose();
		_toolbarViewModel?.Dispose();
		_legendViewModel?.Dispose();
		_minimapViewModel?.Dispose();
		_chartViewModel?.Dispose();
	}
}
