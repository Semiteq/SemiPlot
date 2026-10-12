using ScottPlot;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>Runs a tick regeneration pass against a throwaway paint, as a render does.</summary>
internal static class TickRegeneration
{
	public const float AxisPixels = 400f;

	public static void Regenerate(ITickGenerator generator, CoordinateRange range, float pixels)
	{
		using var paint = Paint.NewDisposablePaint();
		generator.Regenerate(range, Edge.Left, new PixelLength(pixels), paint, new LabelStyle());
	}

	public static void Regenerate(IAxis axis)
	{
		using var paint = Paint.NewDisposablePaint();
		axis.RegenerateTicks(new PixelLength(AxisPixels), paint);
	}
}
