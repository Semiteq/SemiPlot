using Avalonia;
using Avalonia.Media;
using Avalonia.Styling;

using AwesomeAssertions;

using PlotColor = ScottPlot.Color;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>Reads a palette key back and holds the application variant a test writes.</summary>
internal static class ThemeProbe
{
	internal static ThemeVariantScope PreserveVariant()
	{
		return new ThemeVariantScope(Application.Current!);
	}

	internal static ThemeVariantScope ApplyVariant(ThemeVariant variant)
	{
		var scope = PreserveVariant();

		Application.Current!.RequestedThemeVariant = variant;

		return scope;
	}

	internal static ISolidColorBrush Brush(string key, ThemeVariant variant)
	{
		Application.Current!.TryGetResource(key, variant, out var value)
			.Should().BeTrue("'{0}' has to resolve under {1}", key, variant);

		return value.Should().BeAssignableTo<ISolidColorBrush>().Subject;
	}

	internal static Color Colour(string key, ThemeVariant variant)
	{
		return Brush(key, variant).Color;
	}

	internal static PlotColor PlotColour(string key, ThemeVariant variant)
	{
		var colour = Colour(key, variant);

		return new PlotColor(colour.R, colour.G, colour.B, colour.A);
	}
}

/// <summary>The variant in force when the scope opened, restored when it closes.</summary>
internal sealed class ThemeVariantScope(Application application) : IDisposable
{
	private readonly ThemeVariant? _previous = application.RequestedThemeVariant;

	public void Dispose()
	{
		application.RequestedThemeVariant = _previous;
	}
}
