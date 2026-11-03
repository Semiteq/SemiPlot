using Avalonia.Platform;

using SkiaSharp;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>Inter through Skia, for text widths the headless shaper does not render.</summary>
internal static class InterMeasure
{
	public static SKTypeface InterTypeface()
	{
		using var stream = AssetLoader.Open(new Uri("avares://Avalonia.Fonts.Inter/Assets/Inter-Regular.ttf"));
		using var data = SKData.Create(stream);

		return SKTypeface.FromData(data);
	}

	public static SKFont InterFont(SKTypeface typeface, double fontSize)
	{
		return new SKFont(typeface, (float)fontSize) { LinearMetrics = true, Subpixel = true };
	}
}
