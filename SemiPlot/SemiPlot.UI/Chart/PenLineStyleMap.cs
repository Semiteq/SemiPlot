using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

internal static class PenLineStyleMap
{
	public static ConnectStyle ToConnectStyle(PenLineStyle lineStyle)
	{
		return lineStyle switch
		{
			PenLineStyle.Stepped => ConnectStyle.StepHorizontal,
			_ => ConnectStyle.Straight
		};
	}
}
