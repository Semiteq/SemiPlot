using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.Legend;

public static class LegendConverters
{
	// A group header is a section caption, and a TextBlock has no text transform to set it in capitals.
	public static readonly FuncValueConverter<string?, string> ToCapitals =
		new(text => text?.ToUpper(CultureInfo.CurrentCulture) ?? string.Empty);

	// An unparseable hex draws no dot rather than throwing out of a binding.
	public static readonly FuncValueConverter<string?, IBrush> HexToBrush =
		new(hex => hex is not null && Color.TryParse(hex, out var color) ? new SolidColorBrush(color) : Brushes.Transparent);
}
