namespace SemiPlot.UI.Legend;

// A pen group in the mini-legend (keyed by Pen.Group): a header name plus the rows that belong to it.
public sealed class TrendLegendGroupViewModel
{
	public TrendLegendGroupViewModel(string name, IReadOnlyList<TrendLegendRowViewModel> rows)
	{
		ArgumentNullException.ThrowIfNull(name);
		ArgumentNullException.ThrowIfNull(rows);

		Name = name;
		Rows = rows;
	}

	public string Name { get; }

	public IReadOnlyList<TrendLegendRowViewModel> Rows { get; }
}
