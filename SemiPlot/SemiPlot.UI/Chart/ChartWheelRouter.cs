using Avalonia;
using Avalonia.Input;

namespace SemiPlot.UI.Chart;

public enum ChartWheelAction
{
	None,
	ZoomTime,
	PanTime,
	ShiftActivePen,
	ScaleActivePen
}

public readonly record struct ChartWheelGesture(ChartWheelAction Action, int Notch);

public static class ChartWheelRouter
{
	public static ChartWheelGesture Route(bool isAxisRegionHit, KeyModifiers modifiers, Vector delta)
	{
		var isShiftHeld = (modifiers & KeyModifiers.Shift) != 0;
		var notch = isShiftHeld && delta.Y == 0 ? Math.Sign(delta.X) : Math.Sign(delta.Y);

		if (notch == 0)
		{
			return new ChartWheelGesture(ChartWheelAction.None, 0);
		}

		if (isAxisRegionHit)
		{
			return new ChartWheelGesture(ChartWheelAction.ScaleActivePen, notch);
		}

		if ((modifiers & KeyModifiers.Control) != 0)
		{
			return new ChartWheelGesture(ChartWheelAction.ShiftActivePen, notch);
		}

		return new ChartWheelGesture(isShiftHeld ? ChartWheelAction.PanTime : ChartWheelAction.ZoomTime, notch);
	}
}
