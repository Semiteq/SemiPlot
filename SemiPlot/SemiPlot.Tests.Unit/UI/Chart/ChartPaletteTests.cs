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
		plot.Grid.YAxisStyle.MinorLineStyle.Color.Should().Be(Resolve("AppMinorGridLineBrush", variant));

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

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void MarkerColors_ResolveBothMarkerKeysUnderTheVariant(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);

		var palette = ChartPalette.MarkerColors(Application.Current!, variant);

		palette.Line.Should().Be(Resolve("AppMarkerLineBrush", variant));
		palette.Label.Should().Be(Resolve("AppMarkerLabelBrush", variant));
		palette.ReadingBackground.Should().Be(Resolve("AppContentBackgroundBrush", variant));
	}

	[AvaloniaTheory]
	[InlineData(AppThemeVariant.Light)]
	[InlineData(AppThemeVariant.Dark)]
	public void AheadBreakColors_ResolveTheFillWithItsAlphaUnderTheVariant(AppThemeVariant theme)
	{
		var variant = App.VariantFor(theme);

		var palette = ChartPalette.AheadBreakColors(Application.Current!, variant);

		palette.Fill.Should().Be(Resolve("AppAheadBreakFillBrush", variant));
		palette.Fill.A.Should().BeLessThan(byte.MaxValue, "the band is translucent through its colour's alpha");
		palette.Label.Should().Be(Resolve("AppSecondaryForegroundBrush", variant));
	}

	private static Color Resolve(string key, ThemeVariant variant)
	{
		return ThemeProbe.PlotColour(key, variant);
	}
}
