using Avalonia.Controls;
using Avalonia.Media;
using Avalonia.Styling;

using ScottPlot;

using Color = ScottPlot.Color;

namespace SemiPlot.UI.Chart;

/// <summary>Paints the five ScottPlot surfaces the library draws from its own defaults.</summary>
public static class ChartPalette
{
	private const string FigureBackgroundKey = "AppPanelBackgroundBrush";
	private const string DataBackgroundKey = "AppContentBackgroundBrush";
	private const string GridLineKey = "AppSubtleLineBrush";
	private const string MinorGridLineKey = "AppMinorGridLineBrush";
	private const string AxisKey = "AppSecondaryForegroundBrush";

	public static void Apply(Plot plot, IResourceHost resources, ThemeVariant variant)
	{
		var figureBackground = Resolve(resources, variant, FigureBackgroundKey);
		var dataBackground = Resolve(resources, variant, DataBackgroundKey);
		var gridLine = Resolve(resources, variant, GridLineKey);
		var minorGridLine = Resolve(resources, variant, MinorGridLineKey);
		var axisColor = Resolve(resources, variant, AxisKey);

		plot.FigureBackground.Color = figureBackground;
		plot.DataBackground.Color = dataBackground;
		plot.Grid.MajorLineColor = gridLine;
		plot.Grid.MinorLineColor = minorGridLine;

		foreach (var axis in plot.Axes.GetAxes())
		{
			axis.Label.ForeColor = axisColor;
			axis.TickLabelStyle.ForeColor = axisColor;
			axis.MajorTickStyle.Color = axisColor;
			axis.MinorTickStyle.Color = axisColor;
			axis.FrameLineStyle.Color = axisColor;
		}
	}

	private static Color Resolve(IResourceHost resources, ThemeVariant variant, string key)
	{
		if (!resources.TryFindResource(key, variant, out var value) || value is not ISolidColorBrush brush)
		{
			throw new InvalidOperationException($"Palette key '{key}' does not resolve to a brush under {variant}.");
		}

		return new Color(brush.Color.R, brush.Color.G, brush.Color.B, brush.Color.A);
	}
}
