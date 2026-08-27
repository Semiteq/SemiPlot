using System.Reactive.Disposables;
using System.Reactive.Linq;

using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Startup;
using SemiPlot.UI.Toolbar;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly CompositeDisposable _subscriptions = new();

	private ObservableAsPropertyHelper<string?>? _archiveConnectionMessage;
	private string? _archiveHealthMessage;
	private TrendChartViewModel? _chartViewModel;
	private TrendLegendViewModel? _legendViewModel;
	private MinimapViewModel? _minimapViewModel;
	private TrendToolbarViewModel? _toolbarViewModel;

	// Notified only when ChartViewModel is assigned. TrendChartViewModel.Pens is a live view, so adding or
	// removing a pen after assignment leaves this stale — whoever makes the pen set dynamic owns that chain.
	public int PenCount => ChartViewModel?.Pens.Count ?? 0;

	/// <summary>
	/// A chart that was built and holds no pen. The archive answered and provisioning is unfinished, which
	/// is a success rather than an error window — so it needs a state the operator can read, otherwise an
	/// empty catalogue and a broken chart look the same from the outside. False before a chart exists,
	/// where nothing is drawn yet and there is nothing to explain.
	/// </summary>
	public bool IsCatalogueEmpty => ChartViewModel is not null && PenCount == 0;

	/// <summary>
	/// What the live-edge poll reports about its own connection: null while the archive answers, and while
	/// it does not, what <see cref="StartupFailureMapper.Describe"/> makes of the fault — the state plus
	/// the remedy, rather than the raw <see cref="FluentResults.IError.Message"/>, which names a state the
	/// operator can do nothing with. Its only writer is the stream
	/// <see cref="ObserveArchiveConnection"/> binds, so <see cref="ArchiveHealthMessage"/> can neither
	/// set nor clear it — the two rows are independent facts and are rendered as two rows.
	/// </summary>
	public string? ArchiveConnectionMessage => _archiveConnectionMessage?.Value;

	public bool HasArchiveConnectionMessage => ArchiveConnectionMessage is not null;

	/// <summary>
	/// A warning the startup read carried out of the archive — a fault the operator must act on that
	/// stopped nothing. Written once at startup and never again, and never by the connection stream.
	/// </summary>
	public string? ArchiveHealthMessage
	{
		get => _archiveHealthMessage;
		set
		{
			if (_archiveHealthMessage == value)
			{
				return;
			}

			this.RaiseAndSetIfChanged(ref _archiveHealthMessage, value);
			this.RaisePropertyChanged(nameof(HasArchiveHealthMessage));
		}
	}

	public bool HasArchiveHealthMessage => _archiveHealthMessage is not null;

	/// <summary>
	/// Binds the connection row to the coordinator's republished state stream, which already arrives on
	/// the UI scheduler. Called once, at startup: a second bind would give the row a second writer, which
	/// is what the split into two properties exists to prevent.
	/// </summary>
	public void ObserveArchiveConnection(IObservable<ArchiveConnectionState> connectionStates)
	{
		ArgumentNullException.ThrowIfNull(connectionStates);

		if (_archiveConnectionMessage is not null)
		{
			throw new InvalidOperationException(
				"The archive connection row is already bound. It has one writer, bound once.");
		}

		_archiveConnectionMessage = connectionStates
			.Select(state => state.Fault is { } fault ? StartupFailureMapper.Describe(fault) : null)
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
