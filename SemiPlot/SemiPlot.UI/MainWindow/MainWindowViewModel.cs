using ReactiveUI;

using SemiPlot.Core.Data;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Legend;
using SemiPlot.UI.Minimap;
using SemiPlot.UI.Toolbar;

namespace SemiPlot.UI.MainWindow;

public sealed class MainWindowViewModel : ReactiveObject, IDisposable
{
	private readonly IDataProvider _dataProvider;

	private TrendChartViewModel? _chartViewModel;
	private TrendLegendViewModel? _legendViewModel;
	private MinimapViewModel? _minimapViewModel;
	private TrendToolbarViewModel? _toolbarViewModel;

	public MainWindowViewModel(IDataProvider dataProvider)
	{
		ArgumentNullException.ThrowIfNull(dataProvider);
		_dataProvider = dataProvider;
	}

	public int PenCount => _dataProvider.Pens.Count;

	public TrendChartViewModel? ChartViewModel
	{
		get => _chartViewModel;
		set
		{
			_toolbarViewModel?.Dispose();
			_legendViewModel?.Dispose();
			_chartViewModel?.Dispose();

			this.RaiseAndSetIfChanged(ref _chartViewModel, value);
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
