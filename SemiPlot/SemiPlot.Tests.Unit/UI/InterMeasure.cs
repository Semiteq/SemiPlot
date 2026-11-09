using Avalonia.Controls;
using Avalonia.Platform;

using SkiaSharp;

namespace SemiPlot.Tests.Unit.UI;

/// <summary>Inter through Skia, for text widths the headless shaper does not render.</summary>
internal static class InterMeasure
{
	public static SKTypeface InterTypeface(string face = "Regular")
	{
		using var stream = AssetLoader.Open(new Uri($"avares://Avalonia.Fonts.Inter/Assets/Inter-{face}.ttf"));
		using var data = SKData.Create(stream);

		return SKTypeface.FromData(data);
	}

	public static SKFont InterFont(SKTypeface typeface, double fontSize)
	{
		return new SKFont(typeface, (float)fontSize) { LinearMetrics = true, Subpixel = true };
	}

	/// <summary>The control's width with its text content measured in Inter, its chrome as realised.</summary>
	public static double ContentWidthInInter(Window window, ContentControl control, SKTypeface typeface)
	{
		var text = ViewProbe.Descendants<TextBlock>(control).Single(block => Equals(block.Text, control.Content));
		var chrome = ViewProbe.UnconstrainedWidthOf(window, control) - ViewProbe.UnconstrainedWidthOf(window, text);
		using var font = InterFont(typeface, text.FontSize);

		return chrome + font.MeasureText(text.Text);
	}
}
