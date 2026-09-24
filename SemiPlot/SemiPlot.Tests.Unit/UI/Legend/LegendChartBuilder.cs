using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Legend;

/// <summary>The chart both sidebar test classes hang their legend on.</summary>
internal static class LegendChartBuilder
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	// The chart's UI scheduler is virtual: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
	public static TrendChartViewModel CreateChart(TestScheduler scheduler)
	{
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);

		return new TrendChartViewModel(
			coordinator,
			scheduler,
			scheduler,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);
	}
}
