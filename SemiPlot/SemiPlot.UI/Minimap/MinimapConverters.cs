using Avalonia.Data.Converters;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Minimap;

public static class MinimapConverters
{
	public static readonly FuncValueConverter<MinimapPageUnit, bool> IsWeek = new(unit => unit == MinimapPageUnit.Week);

	public static readonly FuncValueConverter<MinimapPageUnit, bool> IsMonth =
		new(unit => unit == MinimapPageUnit.Month);
}
