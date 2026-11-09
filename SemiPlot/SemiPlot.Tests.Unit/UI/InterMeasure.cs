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

	/// <summary>The header's width with its text measured in Inter, its padding and sort slot as realised.</summary>
	public static double HeaderWidthInInter(Window window, DataGridColumnHeader header, SKTypeface semiBold)
	{
		var text = ViewProbe.Descendants<TextBlock>(header).Single(block => Equals(block.Text, header.Content));
		var chrome = ViewProbe.UnconstrainedWidthOf(window, header) - ViewProbe.UnconstrainedWidthOf(window, text);
		using var font = InterFont(semiBold, header.FontSize);

		return chrome + font.MeasureText(text.Text);
	}
}
