using Avalonia.Data.Converters;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Localization;

namespace SemiPlot.UI.PenEditor;

public static class PenLineStyleConverters
{
	public static readonly FuncValueConverter<PenLineStyle, string> ToLabel = new(lineStyle => lineStyle switch
	{
		PenLineStyle.Interpolated => Resources.PenLineStyleInterpolated,
		PenLineStyle.Stepped => Resources.PenLineStyleStepped,
		_ => throw new ArgumentOutOfRangeException(nameof(lineStyle), lineStyle, null)
	});
}
