using ScottPlot;

namespace SemiPlot.UI.Chart;

// View-side hit-test for a single Y-axis panel.
public sealed class ChartAxisRegion
{
	private readonly float _dataBottom;
	private readonly float _dataTop;
	private readonly float _panelLeft;
	private readonly float _panelRight;

	private ChartAxisRegion(
		float panelLeft,
		float panelRight,
		float dataTop,
		float dataBottom)
	{
		_panelLeft = panelLeft;
		_panelRight = panelRight;
		_dataTop = dataTop;
		_dataBottom = dataBottom;
	}

	public static ChartAxisRegion? TryCreate(Plot plot, IYAxis axis)
	{
		var layout = plot.RenderManager.LastRender.Layout;
		var dataRect = layout.DataRect;

		// A hidden axis still carries a panel of size 0 at offset 0, whose band collapses onto the data
		// rect's own edge and would answer a press there with the scale panel of a pen nothing draws.
		if (!axis.IsVisible
			|| !dataRect.HasArea
			|| !layout.PanelSizes.TryGetValue(axis, out var size)
			|| !layout.PanelOffsets.TryGetValue(axis, out var offset))
		{
			return null;
		}

		var (panelLeft, panelRight) = HorizontalBand(axis.Edge, dataRect, size, offset);

		return new ChartAxisRegion(
			panelLeft,
			panelRight,
			dataRect.Top,
			dataRect.Bottom);
	}

	public bool Contains(float pixelX, float pixelY)
	{
		return pixelX >= _panelLeft
			   && pixelX <= _panelRight
			   && pixelY >= _dataTop
			   && pixelY <= _dataBottom;
	}

	private static (float Left, float Right) HorizontalBand(
		Edge edge,
		PixelRect dataRect,
		float size,
		float offset)
	{
		if (edge == Edge.Right)
		{
			var left = dataRect.Right + offset;

			return (left, left + size);
		}

		var right = dataRect.Left - offset;

		return (right - size, right);
	}
}
