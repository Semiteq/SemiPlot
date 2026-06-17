using System.Linq;

using ReactiveUI;

using SemiPlot.UI.Chart;

namespace SemiPlot.UI.Legend;

// The grouped mini-legend. It builds one row per chart pen, groups them by Pen.Group, and lets each
// row reflect the pen's live state and drive visibility/active-pen back onto the chart. The legend
// holds no scale or render logic itself: it only projects the chart view model's read surface into a
// grouped, bindable shape and disposes the per-pen row subscriptions.
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
