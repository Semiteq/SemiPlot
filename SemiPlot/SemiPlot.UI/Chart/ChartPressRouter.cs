namespace SemiPlot.UI.Chart;

// The pure left-button press dispatch decision the view's OnPointerPressed performs: an axis-region hit
// pre-empts pan and delta (a double-click autoscales, a single click edits a bound), then delta mode
// places a cursor, otherwise the press starts a hand-pan. Kept separate from the view so the branch
// ordering is unit-testable without driving real pointer events through the AvaPlot control.
public static class ChartPressRouter
{
	public static ChartPressAction Route(bool isAxisRegionHit, int clickCount, LeftButtonTool activeTool)
	{
		if (isAxisRegionHit)
		{
			return clickCount >= 2 ? ChartPressAction.AutoscaleAxis : ChartPressAction.EditAxisBound;
		}

		return activeTool == LeftButtonTool.DeltaPlacement
			? ChartPressAction.PlaceDeltaCursor
			: ChartPressAction.Pan;
	}
}
