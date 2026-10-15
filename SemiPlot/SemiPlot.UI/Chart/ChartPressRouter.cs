using Avalonia;
using Avalonia.Input;

namespace SemiPlot.UI.Chart;

public enum ChartPressAction
{
	PanOrPickPen,
	PanOrEditTimeWindow,
	PlaceDeltaCursor,
	EditAxisScale,
	ShiftActivePen
}

public enum ChartPressRegion
{
	DataArea,
	ActivePenAxis,
	TimeAxis
}

public static class ChartPressRouter
{
	private const double ClickSlopPixels = 4.0;

	public static ChartPressAction Route(ChartPressRegion region, KeyModifiers modifiers, LeftButtonTool activeTool)
	{
		if (region == ChartPressRegion.ActivePenAxis)
		{
			return ChartPressAction.EditAxisScale;
		}

		if ((modifiers & KeyModifiers.Control) != 0)
		{
			return ChartPressAction.ShiftActivePen;
		}

		if (region == ChartPressRegion.TimeAxis)
		{
			return ChartPressAction.PanOrEditTimeWindow;
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
