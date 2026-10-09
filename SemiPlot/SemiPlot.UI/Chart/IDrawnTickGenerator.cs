using ScottPlot;

namespace SemiPlot.UI.Chart;

// docs/architecture/charting.md#ticks-for-the-drawn-axis-only
/// <summary>A pen axis's tick generator, which generates nothing while its axis is not drawn.</summary>
public interface IDrawnTickGenerator : ITickGenerator
{
	/// <summary>Written on the UI thread by ChartAxisBinder, read on the render thread by Regenerate.</summary>
	bool IsDrawn { get; set; }
}
