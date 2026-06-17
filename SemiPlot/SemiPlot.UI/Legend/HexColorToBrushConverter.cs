using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.Legend;

// Turns a pen's hex color string (e.g. "#ff0000") into a brush for the legend's color swatch, so the
// view model stays free of Avalonia media types. An unparsable value yields a transparent brush.
public sealed class HexColorToBrushConverter : IValueConverter
{
	public static readonly HexColorToBrushConverter Instance = new();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		if (value is string hex && Color.TryParse(hex, out var color))
		{
			return new SolidColorBrush(color);
		}

		return Brushes.Transparent;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
