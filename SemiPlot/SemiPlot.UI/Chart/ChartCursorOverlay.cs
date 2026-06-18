namespace SemiPlot.UI.Chart;

public static class ChartCursorOverlay
{
	public static OverlayPlacement Project(double cursorPixelX, DataRectPixels dataRect, double renderScale)
	{
		if (renderScale <= 0.0)
		{
			return OverlayPlacement.Hidden;
		}

		if (cursorPixelX < dataRect.Left || cursorPixelX > dataRect.Right)
		{
			return OverlayPlacement.Hidden;
		}

		var lineX = cursorPixelX / renderScale;
		var lineTop = dataRect.Top / renderScale;
		var lineBottom = dataRect.Bottom / renderScale;

		return new OverlayPlacement(true, lineX, lineTop, lineBottom);
	}
}
