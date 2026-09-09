using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Toolbar;

namespace SemiPlot.Tests.Unit.UI.Toolbar;

internal static class ToolbarTestBuilder
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	public static (TrendChartViewModel Chart, TrendToolbarViewModel Toolbar) CreateToolbar()
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
			coordinator, scheduler, ImmediateScheduler.Instance, NullLogger<TrendChartViewModel>.Instance);

		return (chart, new TrendToolbarViewModel(chart));
	}
}
