using Avalonia;
using Avalonia.Input;

namespace SemiPlot.UI.Chart;

public enum ChartPressAction
{
	PanOrPickPen,
	PlaceDeltaCursor,
	EditAxisScale,
	ShiftActivePen
}

public static class ChartPressRouter
{
	private const double ClickSlopPixels = 4.0;

	public static ChartPressAction Route(bool isAxisRegionHit, KeyModifiers modifiers, LeftButtonTool activeTool)
	{
		if (isAxisRegionHit)
		{
			return ChartPressAction.EditAxisScale;
		}

		if ((modifiers & KeyModifiers.Control) != 0)
		{
			return ChartPressAction.ShiftActivePen;
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
