using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.Legend;

public static class LegendConverters
{
	public static readonly FuncValueConverter<bool, FontWeight> ActiveToWeight =
		new(isActive => isActive ? FontWeight.Bold : FontWeight.Normal);

	public static readonly FuncValueConverter<string?, IBrush> HexToBrush =
		new(hex => hex is not null && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent);
}
