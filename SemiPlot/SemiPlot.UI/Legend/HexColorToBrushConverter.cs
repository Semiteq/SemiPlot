using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.Legend;

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
