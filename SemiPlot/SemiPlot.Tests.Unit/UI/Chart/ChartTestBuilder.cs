using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The charts the chart, sidebar and main window tests build, and the one way they add a pen.</summary>
internal static class ChartTestBuilder
{
	public static readonly TimeSpan BatchWindow = TimeSpan.FromMilliseconds(33);

	/// <summary>The chart's own debounce window: one advance past it releases a queued history request.</summary>
	public static readonly TimeSpan HistoryDebounceWindow = TimeSpan.FromMilliseconds(150);

	// Both schedulers are virtual: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
	public static TrendChartViewModel CreateChart(TestScheduler scheduler, FakeDataProvider? provider = null)
	{
		provider ??= new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));

		return CreateChart(scheduler, CreateCoordinator(scheduler, provider), new MessagePanelViewModel());
	}

	public static TrendChartViewModel CreateChart(
		TestScheduler scheduler,
		TrendCoordinator coordinator,
		MessagePanelViewModel messagePanel)
	{
		return new TrendChartViewModel(
			coordinator,
			scheduler,
			scheduler,
			messagePanel,
			NullLogger<TrendChartViewModel>.Instance);
	}

	public static TrendCoordinator CreateCoordinator(TestScheduler scheduler, FakeDataProvider provider)
	{
		return new TrendCoordinator(provider, provider.Pens, scheduler, ImmediateScheduler.Instance, BatchWindow);
	}

	// The UI scheduler is immediate, so no test built here may realise a view. Realtime stays quiet unless a
	// test asks for it: advancing past the history throttle must not also pump samples into a pen whose
	// history the test asserts on.
	public static (TrendChartViewModel ViewModel, TestScheduler Scheduler, TrendCoordinator Coordinator,
		FakeDataProvider Provider) CreateViewModel(TimeSpan? realtimeInterval = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromHours(1));
		var coordinator = CreateCoordinator(scheduler, provider);
		var viewModel = new TrendChartViewModel(
			coordinator,
			scheduler,
			ImmediateScheduler.Instance,
			new MessagePanelViewModel(),
			NullLogger<TrendChartViewModel>.Instance);

		return (viewModel, scheduler, coordinator, provider);
	}

	// firstSample is the pan-backward floor; pass a value before the window start when a test pans into the past.
	public static Task LoadInitialHistory(
		TrendChartViewModel viewModel,
		TestScheduler scheduler,
		DateTime firstSample,
		DateTime to)
	{
		viewModel.Navigation.TrackDataExtents(firstSample, to);
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		return Task.CompletedTask;
	}

	/// <summary>Adds one pen after the ones the chart shows, as a catalogue read naming it does.</summary>
	public static TrendPenState AddPen(this TrendChartViewModel chart, Pen pen)
	{
		if (chart.FindPen(pen.PenId) is not { } state)
		{
			chart.ApplyCatalogue([.. chart.Catalogue, pen]);
			state = chart.FindPen(pen.PenId)!;
		}

		return state;
	}
}
