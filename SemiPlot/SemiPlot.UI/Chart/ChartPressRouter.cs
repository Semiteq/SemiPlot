namespace SemiPlot.UI.Chart;

// Branch ordering: an axis-region hit pre-empts delta and pan; delta mode pre-empts pan.
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
