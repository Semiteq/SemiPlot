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
	private readonly CompositeDisposable _subscriptions = [];

	private ObservableAsPropertyHelper<string?>? _archiveConnectionMessage;

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
		get;
		set
		{
			this.RaiseAndSetIfChanged(ref field, value);
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
		get;
		set
		{
			if (ReferenceEquals(field, value))
			{
				return;
			}

			ToolbarViewModel?.Dispose();
			LegendViewModel?.Dispose();
			field?.Dispose();

			this.RaiseAndSetIfChanged(ref field, value);
			this.RaisePropertyChanged(nameof(PenCount));
			this.RaisePropertyChanged(nameof(IsCatalogueEmpty));

			ToolbarViewModel = value is null ? null : new TrendToolbarViewModel(value);
			LegendViewModel = value is null ? null : new TrendLegendViewModel(value);
		}
	}

	public TrendToolbarViewModel? ToolbarViewModel
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
		set
		{
			field?.Dispose();
			this.RaiseAndSetIfChanged(ref field, value);
		}
	}

	public void Dispose()
	{
		_subscriptions.Dispose();
		ToolbarViewModel?.Dispose();
		LegendViewModel?.Dispose();
		MinimapViewModel?.Dispose();
		ChartViewModel?.Dispose();
	}
}
