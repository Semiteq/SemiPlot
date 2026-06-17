using System.Globalization;

using Avalonia.Data.Converters;
using Avalonia.Media;

namespace SemiPlot.UI.Legend;

// Renders the active pen's legend row name in bold so the operator can see which pen drives the
// primary axis. Any non-active row stays at normal weight.
public sealed class ActiveToWeightConverter : IValueConverter
{
	public static readonly ActiveToWeightConverter Instance = new();

	public object Convert(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		return value is true ? FontWeight.Bold : FontWeight.Normal;
	}

	public object ConvertBack(object? value, Type targetType, object? parameter, CultureInfo culture)
	{
		throw new NotSupportedException();
	}
}
