using ScottPlot;

namespace SemiPlot.UI.Chart;

// View-side hit-test for a single axis panel.
public sealed class ChartAxisRegion
{
	private readonly float _left;
	private readonly float _right;
	private readonly float _top;
	private readonly float _bottom;

	private ChartAxisRegion(
		float left,
		float right,
		float top,
		float bottom)
	{
		_left = left;
		_right = right;
		_top = top;
		_bottom = bottom;
	}

	public static ChartAxisRegion? TryCreate(Plot plot, IYAxis axis)
	{
		if (!TryReadPanel(plot, axis, out var dataRect, out var size, out var offset))
		{
			return null;
		}

		var right = dataRect.Left - offset;

		return new ChartAxisRegion(
			right - size,
			right,
			dataRect.Top,
			dataRect.Bottom);
	}

	public static ChartAxisRegion? TryCreateTimeAxis(Plot plot)
	{
		if (!TryReadPanel(plot, plot.Axes.Bottom, out var dataRect, out var size, out var offset))
		{
			return null;
		}

		var top = dataRect.Bottom + offset;

		return new ChartAxisRegion(
			dataRect.Left,
			dataRect.Right,
			top,
			top + size);
	}

	public bool Contains(float pixelX, float pixelY)
	{
		return pixelX >= _left
			   && pixelX <= _right
			   && pixelY >= _top
			   && pixelY <= _bottom;
	}

	// A hidden axis still carries a panel of size 0 at offset 0, whose band collapses onto the data rect's own
	// edge and would answer a press there with the panel of an axis nothing draws.
	private static bool TryReadPanel(Plot plot, IAxis axis, out PixelRect dataRect, out float size, out float offset)
	{
		var layout = plot.RenderManager.LastRender.Layout;
		dataRect = layout.DataRect;
		size = 0f;
		offset = 0f;

		return axis.IsVisible
			&& dataRect.HasArea
			&& layout.PanelSizes.TryGetValue(axis, out size)
			&& layout.PanelOffsets.TryGetValue(axis, out offset);
	}
}
