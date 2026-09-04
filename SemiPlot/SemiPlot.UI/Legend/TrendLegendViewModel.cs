using ReactiveUI;

using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Legend;

public sealed class TrendLegendViewModel : ReactiveObject, IDisposable
{
	private readonly IReadOnlyList<TrendLegendRowViewModel> _rows;

	public TrendLegendViewModel(TrendChartViewModel chartViewModel)
	{
		_rows = [.. chartViewModel.Pens.Select(pen => new TrendLegendRowViewModel(chartViewModel, pen))];

		Groups = [.. _rows
			.GroupBy(row => row.GroupName)
			.Select(group => new TrendLegendGroupViewModel(group.Key, [.. group]))];
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
