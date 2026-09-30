namespace SemiPlot.UI.Chart;

public enum ChartPressAction
{
	Pan,
	PlaceDeltaCursor,
	EditAxisScale
}

// Branch ordering: an axis-region hit pre-empts delta and pan; delta mode pre-empts pan.
public static class ChartPressRouter
{
	public static ChartPressAction Route(bool isAxisRegionHit, LeftButtonTool activeTool)
	{
		if (isAxisRegionHit)
		{
			return ChartPressAction.EditAxisScale;
		}

		return activeTool == LeftButtonTool.DeltaPlacement
			? ChartPressAction.PlaceDeltaCursor
			: ChartPressAction.Pan;
	}
}
