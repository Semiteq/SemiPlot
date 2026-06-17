using ScottPlot;

namespace SemiPlot.UI.Chart;

// View-side hit-test for a single Y-axis panel. ScottPlot's built-in input is disabled, so the
// pointer pixel is mapped to the axis region and to a value by hand from the last render layout:
// the axis panel occupies a vertical band the height of the data area, offset horizontally to the
// left (Edge.Left) or right (Edge.Right) of the data rectangle by the panel's measured size. A press
// inside that band is an axis-region edit; the upper half edits MAX, the lower half MIN, and the
// pixel-Y maps linearly onto the axis Range with the top pixel as the maximum (pixel-Y inversion).
public sealed class ChartAxisRegion
{
	private readonly float _panelLeft;
	private readonly float _panelRight;
	private readonly float _dataTop;
	private readonly float _dataBottom;
	private readonly double _axisMin;
	private readonly double _axisMax;

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

	// Builds the region for a Y axis from the plot's last render layout, or null when the axis was not
	// laid out (no render yet, axis hidden) so the caller falls through to the pan/delta routing.
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

	// Builds a region from explicit bounds so the pixel-mapping guards (e.g. a degenerate zero-height data
	// area) can be exercised without a real render layout, which never produces such a band.
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

	// The data area's pixel-Y origin is the top, so a pixel above the vertical midpoint is the upper
	// half (MAX); at or below it is the lower half (MIN).
	public bool IsUpperHalf(float pixelY)
	{
		return pixelY < (_dataTop + _dataBottom) / 2f;
	}

	// Maps a pixel-Y onto the axis value range. The top pixel is the maximum and the bottom pixel the
	// minimum, so the fraction is inverted before interpolation.
	public double ValueAt(float pixelY)
	{
		var height = _dataBottom - _dataTop;
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
