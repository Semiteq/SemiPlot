using System.Linq;

using ReactiveUI;

using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Legend;

// The grouped mini-legend: one row per chart pen grouped by Pen.Group, projecting the chart view model's
// read surface into a bindable shape. Holds no scale or render logic.
public sealed class TrendLegendViewModel : ReactiveObject, IDisposable
{
	private readonly IReadOnlyList<TrendLegendRowViewModel> _rows;

	public TrendLegendViewModel(TrendChartViewModel chartViewModel)
	{
		ArgumentNullException.ThrowIfNull(chartViewModel);

		_rows = chartViewModel.Pens
			.Select(pen => new TrendLegendRowViewModel(chartViewModel, pen))
			.ToArray();

		Groups = _rows
			.GroupBy(row => row.GroupName)
			.Select(group => new TrendLegendGroupViewModel(group.Key, group.ToArray()))
			.ToArray();
	}

	public IReadOnlyList<TrendLegendGroupViewModel> Groups { get; }

	public void Dispose()
	{
		foreach (var row in _rows)
		{
			row.Dispose();
		}
	}
}
