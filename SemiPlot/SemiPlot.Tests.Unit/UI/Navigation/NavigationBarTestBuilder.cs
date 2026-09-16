using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;
using SemiPlot.UI.Navigation;

namespace SemiPlot.Tests.Unit.UI.Navigation;

internal static class NavigationBarTestBuilder
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	public static (TrendChartViewModel Chart, NavigationBarViewModel Bar) CreateBar()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var chart = new TrendChartViewModel(
			coordinator,
			scheduler,
			ImmediateScheduler.Instance,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);

		return (chart, new NavigationBarViewModel(chart));
	}
}
