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
