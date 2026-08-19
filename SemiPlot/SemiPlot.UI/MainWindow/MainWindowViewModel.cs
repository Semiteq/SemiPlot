using ReactiveUI;

using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Toolbar;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
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
		_toolbarViewModel?.Dispose();
		_legendViewModel?.Dispose();
		_minimapViewModel?.Dispose();
		_chartViewModel?.Dispose();
	}
}
