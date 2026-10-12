using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Navigation;

namespace SemiPlot.Tests.Unit.UI.Navigation;

internal static class NavigationBarTestBuilder
{
	public static (TrendChartViewModel Chart, NavigationBarViewModel Bar) CreateBar()
	{
		var chart = ChartTestBuilder.CreateViewModel(ChartTestBuilder.RealtimeInterval).ViewModel;

		return (chart, new NavigationBarViewModel(chart));
	}
}
