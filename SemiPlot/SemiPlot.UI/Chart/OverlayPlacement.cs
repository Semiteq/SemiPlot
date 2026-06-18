namespace SemiPlot.UI.Chart;

public readonly record struct OverlayPlacement(
	bool IsVisible,
	double LineX,
	double LineTop,
	double LineBottom)
{
	public static OverlayPlacement Hidden { get; } = new(false, 0.0, 0.0, 0.0);
}
