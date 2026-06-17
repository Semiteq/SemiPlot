using ScottPlot;

using SemiPlot.Core.Trends;

namespace SemiPlot.UI.Chart;

// Maps the renderer-agnostic Core PenLineStyle to ScottPlot's Scatter ConnectStyle.
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
