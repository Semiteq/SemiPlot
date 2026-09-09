namespace SemiPlot.UI.Chart;

public readonly record struct DataRectPixels(double Left, double Right, double Top, double Bottom);

public readonly record struct OverlayPlacement(
	bool IsVisible,
	double LineX,
	double LineTop,
	double LineBottom)
{
	public static OverlayPlacement Hidden { get; } = new(false, 0.0, 0.0, 0.0);
}

public static class ChartCursorOverlay
{
	public static OverlayPlacement Project(double cursorPixelX, DataRectPixels dataRect, double renderScale)
	{
		if (renderScale <= 0.0 || cursorPixelX < dataRect.Left || cursorPixelX > dataRect.Right)
		{
			return OverlayPlacement.Hidden;
		}

		var lineX = cursorPixelX / renderScale;
		var lineTop = dataRect.Top / renderScale;
		var lineBottom = dataRect.Bottom / renderScale;

		return new OverlayPlacement(true, lineX, lineTop, lineBottom);
	}
}
