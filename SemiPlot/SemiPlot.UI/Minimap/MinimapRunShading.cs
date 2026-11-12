using Avalonia;
using Avalonia.Controls;
using Avalonia.Media;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Minimap;

/// <summary>The Day page's recipe runs as full-height shades, an interrupted run's end marked by an edge.</summary>
public sealed class MinimapRunShading : Control
{
	public static readonly StyledProperty<IReadOnlyList<RunShade>> ShadesProperty =
		AvaloniaProperty.Register<MinimapRunShading, IReadOnlyList<RunShade>>(nameof(Shades), []);

	public static readonly StyledProperty<IBrush?> FillBrushProperty =
		AvaloniaProperty.Register<MinimapRunShading, IBrush?>(nameof(FillBrush));

	public static readonly StyledProperty<IBrush?> EdgeBrushProperty =
		AvaloniaProperty.Register<MinimapRunShading, IBrush?>(nameof(EdgeBrush));

	private const double EdgeWidth = 2.0;

	static MinimapRunShading()
	{
		AffectsRender<MinimapRunShading>(ShadesProperty, FillBrushProperty, EdgeBrushProperty);
	}

	public IReadOnlyList<RunShade> Shades
	{
		get => GetValue(ShadesProperty);
		set => SetValue(ShadesProperty, value);
	}

	public IBrush? FillBrush
	{
		get => GetValue(FillBrushProperty);
		set => SetValue(FillBrushProperty, value);
	}

	public IBrush? EdgeBrush
	{
		get => GetValue(EdgeBrushProperty);
		set => SetValue(EdgeBrushProperty, value);
	}

	public override void Render(DrawingContext context)
	{
		var height = Bounds.Height;
		foreach (var shade in Shades)
		{
			if (FillBrush is { } fill)
			{
				context.FillRectangle(fill, new Rect(shade.Left, 0.0, shade.Width, height));
			}

			if (shade.HasEndEdge && EdgeBrush is { } edge)
			{
				var edgeWidth = Math.Min(EdgeWidth, shade.Width);
				context.FillRectangle(edge, new Rect(shade.Left + shade.Width - edgeWidth, 0.0, edgeWidth, height));
			}
		}
	}
}
