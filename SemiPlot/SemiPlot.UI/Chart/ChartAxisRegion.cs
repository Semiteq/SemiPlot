using ScottPlot;

namespace SemiPlot.UI.Chart;

// View-side hit-test for a single Y-axis panel. Pixel-Y maps onto the axis Range with the top pixel as
// the maximum (pixel-Y inversion).
public sealed class ChartAxisRegion
{
	private readonly double _axisMax;
	private readonly double _axisMin;
	private readonly float _dataBottom;
	private readonly float _dataTop;
	private readonly float _panelLeft;
	private readonly float _panelRight;

	private ChartAxisRegion(
		float panelLeft,
		float panelRight,
		float dataTop,
		float dataBottom,
		double axisMin,
		double axisMax)
	{
		_panelLeft = panelLeft;
		_panelRight = panelRight;
		_dataTop = dataTop;
		_dataBottom = dataBottom;
		_axisMin = axisMin;
		_axisMax = axisMax;
	}

	public static ChartAxisRegion? TryCreate(Plot plot, IYAxis axis)
	{
		ArgumentNullException.ThrowIfNull(plot);
		ArgumentNullException.ThrowIfNull(axis);

		var layout = plot.RenderManager.LastRender.Layout;
		var dataRect = layout.DataRect;

		if (!dataRect.HasArea
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
			dataRect.Bottom,
			axis.Range.Min,
			axis.Range.Max);
	}

	internal static ChartAxisRegion ForTesting(
		float panelLeft,
		float panelRight,
		float dataTop,
		float dataBottom,
		double axisMin,
		double axisMax)
	{
		return new ChartAxisRegion(panelLeft, panelRight, dataTop, dataBottom, axisMin, axisMax);
	}

	public bool Contains(float pixelX, float pixelY)
	{
		return pixelX >= _panelLeft
			   && pixelX <= _panelRight
			   && pixelY >= _dataTop
			   && pixelY <= _dataBottom;
	}

	public bool IsUpperHalf(float pixelY)
	{
		return pixelY < (_dataTop + _dataBottom) / 2f;
	}

	// Top pixel is the maximum, bottom pixel the minimum, so the fraction is inverted before interpolation.
	public double ValueAt(float pixelY)
	{
		var height = _dataBottom - _dataTop;
		// Zero-height data area (no real render layout): no valid mapping, fall back to the axis maximum.
		if (height <= 0f)
		{
			return _axisMax;
		}

		var fractionFromTop = (pixelY - _dataTop) / height;
		var fractionFromBottom = 1.0 - fractionFromTop;

		return _axisMin + ((_axisMax - _axisMin) * fractionFromBottom);
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
