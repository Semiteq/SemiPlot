namespace SemiPlot.UI.Legend;

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
