using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging;
using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Messages;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>The charts the chart, sidebar and main window tests build, their pens and their ink ratio.</summary>
internal static class ChartTestBuilder
{
	/// <summary>The chart's own debounce window: one advance past it releases a queued history request.</summary>
	public static readonly TimeSpan HistoryDebounceWindow = TimeSpan.FromMilliseconds(150);

	public static readonly DateTime From = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	public static readonly DateTime To = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);
	public static readonly TimeSpan RealtimeInterval = TimeSpan.FromMilliseconds(10);

	public const int FlatPlotWidth = 800;
	public const int FlatPlotHeight = 500;
	public const double FlatPlotAxisMin = 0.0;
	public const double FlatPlotAxisMax = 100.0;

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

		// The admission and the delivery each land one tick after the step that schedules them, and the margin
		// read is requested as the visible window's read lands.
		scheduler.AdvanceBy(2 * (HistoryDebounceWindow.Ticks + 2));
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

	public static (TrendChartViewModel ViewModel, byte[,,] Pixels, PixelRect DataRect) RenderTwoFlatPens(
		(Pen Pen, double Value) first,
		(Pen Pen, double Value) second,
		int windowSeconds,
		int activePenId)
	{
		var viewModel = CreateViewModel(RealtimeInterval).ViewModel;
		AddTwoFlatPens(viewModel, first, second, windowSeconds);
		viewModel.SetActivePen(activePenId);

		var plot = viewModel.Plot;
		using var image = plot.GetImage(FlatPlotWidth, FlatPlotHeight);

		return (viewModel, image.GetArrayRGB(), plot.RenderManager.LastRender.Layout.DataRect);
	}

	/// <summary>Adds two pens, each one flat line over the window that starts at <see cref="From"/>.</summary>
	public static void AddTwoFlatPens(
		TrendChartViewModel viewModel,
		(Pen Pen, double Value) first,
		(Pen Pen, double Value) second,
		int windowSeconds)
	{
		var windowEnd = From.AddSeconds(windowSeconds);
		AddFlatPen(viewModel, first.Pen, windowEnd, first.Value);
		AddFlatPen(viewModel, second.Pen, windowEnd, second.Value);
		viewModel.Plot.Axes.SetLimitsX(LocalTimeAxis.ToAxis(From), LocalTimeAxis.ToAxis(windowEnd));
	}

	/// <summary>Adds one pen drawn flat from <see cref="From"/>, its scale pinned to the flat plot's span.</summary>
	public static void AddFlatPen(TrendChartViewModel viewModel, Pen pen, DateTime windowEnd, double value)
	{
		viewModel.AddPen(pen).LoadHistory(
			new PenHistoryEnvelope(pen.PenId, [From, windowEnd], [value, value], [value, value], [value, value]),
			windowEnd);

		// An Auto pen with no envelope scales to (0, 1), which takes a flat line out of the data area.
		viewModel.SetAxisLimits(pen.PenId, FlatPlotAxisMin, FlatPlotAxisMax);
	}

	/// <summary>One channel's ink over another's in the data area's middle column, the last render repeated.</summary>
	public static double InkRatio(TrendChartViewModel viewModel, int numeratorChannel, int denominatorChannel)
	{
		var plot = viewModel.Plot;
		var figureRect = plot.RenderManager.LastRender.FigureRect;
		var dataRectBefore = plot.RenderManager.LastRender.Layout.DataRect;

		using var image = plot.GetImage((int)figureRect.Width, (int)figureRect.Height);

		var dataRect = plot.RenderManager.LastRender.Layout.DataRect;
		dataRect.Should().Be(dataRectBefore, "a measurement must leave the pointer maths where it was");
		var pixels = image.GetArrayRGB();
		var column = (int)Math.Round(dataRect.HorizontalCenter);
		var numeratorInk = StrokeProbe.InkOf(pixels, dataRect, column, numeratorChannel);
		var denominatorInk = StrokeProbe.InkOf(pixels, dataRect, column, denominatorChannel);
		numeratorInk.Should().BeGreaterThan(0.0, "the measured line must cross the middle of the data area");
		denominatorInk.Should().BeGreaterThan(0.0, "the reference line must cross the middle of the data area");

		return numeratorInk / denominatorInk;
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
