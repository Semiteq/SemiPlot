using System.Reactive.Concurrency;

using Microsoft.Extensions.Logging;
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
	/// <summary>The chart's own debounce window: one advance past it releases a queued history request.</summary>
	public static readonly TimeSpan HistoryDebounceWindow = TimeSpan.FromMilliseconds(150);

	public static readonly DateTime From = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	public static readonly DateTime To = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
	public static readonly TimeSpan RealtimeInterval = TimeSpan.FromMilliseconds(10);

	// Both schedulers are virtual: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
	public static TrendChartViewModel CreateChart(TestScheduler scheduler, FakeDataProvider? provider = null)
	{
		provider ??= new FakeDataProvider(scheduler, RealtimeInterval);

		return CreateViewModel(scheduler: scheduler, provider: provider, uiScheduler: scheduler).ViewModel;
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
		return new TrendCoordinator(provider, provider.Pens, scheduler, ImmediateScheduler.Instance);
	}

	// The UI scheduler is immediate unless a test passes its own, so no default-built test realises a view.
	// Realtime stays quiet unless asked for. A passed provider brings its own interval, so realtimeInterval
	// then has no effect.
	public static (TrendChartViewModel ViewModel, TestScheduler Scheduler, TrendCoordinator Coordinator,
		FakeDataProvider Provider) CreateViewModel(
		TimeSpan? realtimeInterval = null,
		TestScheduler? scheduler = null,
		FakeDataProvider? provider = null,
		IScheduler? uiScheduler = null,
		MessagePanelViewModel? messagePanel = null,
		ILogger<TrendChartViewModel>? logger = null)
	{
		scheduler ??= new TestScheduler();
		provider ??= new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromHours(1));
		var coordinator = CreateCoordinator(scheduler, provider);
		var viewModel = new TrendChartViewModel(
			coordinator,
			scheduler,
			uiScheduler ?? ImmediateScheduler.Instance,
			messagePanel ?? new MessagePanelViewModel(),
			logger ?? NullLogger<TrendChartViewModel>.Instance);

		return (viewModel, scheduler, coordinator, provider);
	}

	public static (TrendChartViewModel ViewModel, TestScheduler Scheduler, TrendCoordinator Coordinator,
		FakeDataProvider Provider) CreateViewModelOnOneScheduler(
		TimeSpan? realtimeInterval = null,
		MessagePanelViewModel? messagePanel = null,
		ILogger<TrendChartViewModel>? logger = null)
	{
		var scheduler = new TestScheduler();

		return CreateViewModel(
			realtimeInterval, scheduler, uiScheduler: scheduler, messagePanel: messagePanel, logger: logger);
	}

	// firstSample is the pan-backward floor; pass a value before the window start when a test pans into the past.
	public static void LoadInitialHistory(
		TrendChartViewModel viewModel,
		TestScheduler scheduler,
		DateTime firstSample,
		DateTime to)
	{
		viewModel.Navigation.TrackDataExtents(firstSample, to);
		viewModel.RequestInitialHistory();

		// The admission and the delivery each land one tick after the step that schedules them.
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 2);
	}

	// The result lands after release() returns, so the wait is for the redraw count it leads to, counted from
	// before the release. A mid-apply TrackDataExtents redraws first: a site with no earlier extent track
	// counts it, and passing 1 there returns before the history loads.
	public static async Task ReleaseAndAwait(TrendChartViewModel viewModel, int redraws, Action release)
	{
		var seen = 0;
		using var counter = viewModel.RedrawRequested.Subscribe(_ => Interlocked.Increment(ref seen));

		release();

		await HeadlessWait.Until(() => Volatile.Read(ref seen) >= redraws);
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
