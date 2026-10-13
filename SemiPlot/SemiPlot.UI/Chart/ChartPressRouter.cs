using Avalonia;

namespace SemiPlot.UI.Chart;

public enum ChartPressAction
{
	PanOrPickPen,
	PlaceDeltaCursor,
	EditAxisScale
}

// Branch ordering: an axis-region hit pre-empts delta and pan; delta mode pre-empts pan.
public static class ChartPressRouter
{
	private const double ClickSlopPixels = 4.0;

	public static ChartPressAction Route(bool isAxisRegionHit, LeftButtonTool activeTool)
	{
		if (isAxisRegionHit)
		{
			return ChartPressAction.EditAxisScale;
		}

		return activeTool == LeftButtonTool.DeltaPlacement
			? ChartPressAction.PlaceDeltaCursor
			: ChartPressAction.PanOrPickPen;
	}

	public static bool IsPastClickSlop(Point origin, Point current)
	{
		return Point.Distance(origin, current) > ClickSlopPixels;
	}
}
