using Avalonia;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;

using AwesomeAssertions;

using ScottPlot;

using SemiPlot.UI;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Startup;

using Xunit;

using Color = ScottPlot.Color;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartPaletteTests
{
	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void Apply_PaintsEverySurfaceWithTheVariantsPalette(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);
		using var plot = new Plot();

		ChartPalette.Apply(plot, Application.Current!, variant);

		plot.FigureBackground.Color.Should().Be(Resolve("AppPanelBackgroundBrush", variant));
		plot.DataBackground.Color.Should().Be(Resolve("AppContentBackgroundBrush", variant));
		plot.Grid.MajorLineColor.Should().Be(Resolve("AppSubtleLineBrush", variant));

		var axisColor = Resolve("AppSecondaryForegroundBrush", variant);
		foreach (var axis in plot.Axes.GetAxes())
		{
			axis.Label.ForeColor.Should().Be(axisColor);
			axis.TickLabelStyle.ForeColor.Should().Be(axisColor);
			axis.MajorTickStyle.Color.Should().Be(axisColor);
			axis.MinorTickStyle.Color.Should().Be(axisColor);
			axis.FrameLineStyle.Color.Should().Be(axisColor);
		}
	}

	[AvaloniaFact]
	public void Apply_RepaintsEverySurfaceWhenTheVariantChanges()
	{
		using var plot = new Plot();

		ChartPalette.Apply(plot, Application.Current!, ThemeVariant.Light);
		var light = SurfacesOf(plot);

		ChartPalette.Apply(plot, Application.Current!, ThemeVariant.Dark);
		var dark = SurfacesOf(plot);

		dark.Figure.Should().NotBe(light.Figure);
		dark.Data.Should().NotBe(light.Data);
		dark.Grid.Should().NotBe(light.Grid);
		dark.Axis.Should().NotBe(light.Axis);
		dark.Figure.Should().Be(Resolve("AppPanelBackgroundBrush", ThemeVariant.Dark));
		dark.Data.Should().Be(Resolve("AppContentBackgroundBrush", ThemeVariant.Dark));
		dark.Grid.Should().Be(Resolve("AppSubtleLineBrush", ThemeVariant.Dark));
		dark.Axis.Should().Be(Resolve("AppSecondaryForegroundBrush", ThemeVariant.Dark));
	}

	[AvaloniaFact]
	public void Apply_PaintsAnAxisAddedAfterTheFirstApplication()
	{
		using var plot = new Plot();
		ChartPalette.Apply(plot, Application.Current!, ThemeVariant.Light);

		var added = plot.Axes.AddRightAxis();
		ChartPalette.Apply(plot, Application.Current!, ThemeVariant.Light);

		added.TickLabelStyle.ForeColor.Should().Be(Resolve("AppSecondaryForegroundBrush", ThemeVariant.Light));
	}

	private static (Color Figure, Color Data, Color Grid, Color Axis) SurfacesOf(Plot plot)
	{
		return (
			plot.FigureBackground.Color,
			plot.DataBackground.Color,
			plot.Grid.MajorLineColor,
			plot.Axes.Bottom.TickLabelStyle.ForeColor);
	}

	private static Color Resolve(string key, ThemeVariant variant)
	{
		return ThemeProbe.PlotColour(key, variant);
	}
}
