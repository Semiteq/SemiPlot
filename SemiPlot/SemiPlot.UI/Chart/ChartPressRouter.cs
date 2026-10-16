using Avalonia;
using Avalonia.Input;

namespace SemiPlot.UI.Chart;

public enum ChartPressAction
{
	PanOrPickPen,
	PanOrEditTimeWindow,
	PlaceDeltaCursor,
	EditAxisScale,
	ShiftActivePen,
	DragMarker,
	AddMarker,
	DeleteMarker
}

public enum ChartPressRegion
{
	DataArea,
	ActivePenAxis,
	TimeAxis,
	Marker
}

public static class ChartPressRouter
{
	private const double ClickSlopPixels = 4.0;

	public static ChartPressAction Route(
		ChartPressRegion region,
		KeyModifiers modifiers,
		LeftButtonTool activeTool,
		int clickCount)
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

		if (activeTool == LeftButtonTool.DeltaPlacement)
		{
			return ChartPressAction.PlaceDeltaCursor;
		}

		var isDoubleClick = clickCount == 2;
		if (region == ChartPressRegion.Marker)
		{
			return isDoubleClick ? ChartPressAction.DeleteMarker : ChartPressAction.DragMarker;
		}

		return isDoubleClick ? ChartPressAction.AddMarker : ChartPressAction.PanOrPickPen;
	}

	public static bool IsPastClickSlop(Point origin, Point current)
	{
		return Point.Distance(origin, current) > ClickSlopPixels;
	}
}
