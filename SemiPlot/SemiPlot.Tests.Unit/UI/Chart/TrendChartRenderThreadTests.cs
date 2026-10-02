using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using ScottPlot;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

// docs/architecture/charting.md#applying-a-catalogue-read
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartRenderThreadTests
{
	private const int PlotWidth = 400;
	private const int PlotHeight = 300;

	// The narrow canvas leaves a data area under the minimum column count, the wide one over the maximum.
	private const int NarrowPlotWidth = 320;
	private const int WidePlotWidth = 2600;
	private const int FrameBudget = 300;
	private const int JoiningPenCount = 20;
	private const int TestTimeoutMilliseconds = 120_000;

	private static readonly TimeSpan _runBudget = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _joinBudget = TimeSpan.FromSeconds(30);

	[AvaloniaFact(Timeout = TestTimeoutMilliseconds)]
	public async Task ApplyCatalogue_WhileTheRenderThreadDraws_DoesNotThrow()
	{
		using var chart = CreateChart(new TestScheduler());
		IReadOnlyList<Pen> kept = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];
		IReadOnlyList<Pen> joined =
		[
			.. kept,
			.. Enumerable.Range(2, JoiningPenCount).Select(id => new Pen(id, $"Pen {id}", ["Group A"], "#00ff00"))
		];
		chart.ApplyCatalogue(kept);
		RenderFrame(chart.Plot);

		using var cancellation = new CancellationTokenSource(_runBudget);

		// Written by the render task, read by the test thread only after joining it.
		var frames = 0;

		var renderTask = Task.Run(
			() =>
			{
				while (frames < FrameBudget && !cancellation.Token.IsCancellationRequested)
				{
					RenderFrame(chart.Plot);
					frames++;
				}
			},
			TestContext.Current.CancellationToken);

		Exception? failure;

		try
		{
			while (!cancellation.Token.IsCancellationRequested && !renderTask.IsCompleted)
			{
				chart.ApplyCatalogue(joined);
				chart.ApplyCatalogue(kept);
			}
		}
		finally
		{
			await cancellation.CancelAsync();
			failure = await Record.ExceptionAsync(
				() => renderTask.WaitAsync(_joinBudget, TestContext.Current.CancellationToken));
		}

		failure.Should().BeNull(
			"a frame must never meet a half-applied delta; the render thread stopped at frame {0}",
			frames);
		frames.Should().Be(FrameBudget);
	}

	// docs/architecture/charting.md#per-pen-plottable-envelopeline
	[AvaloniaFact]
	public void ApplyCatalogue_SwitchesTheLiveEdgeWithThePlotUnlocked()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromHours(1));
		using var chart = CreateChart(scheduler, provider);
		var lockHeldAtEachSwitch = new List<bool>();
		provider.LiveSubscriptionChanging = () => lockHeldAtEachSwitch.Add(Monitor.IsEntered(chart.Plot.Sync));

		chart.ApplyCatalogue([provider.Pens[0]]);

		lockHeldAtEachSwitch.Should().NotBeEmpty().And.AllSatisfy(isHeld => isHeld.Should().BeFalse());
	}

	// docs/architecture/charting.md#the-plots-own-lists-and-the-render-thread
	[AvaloniaFact]
	public void ApplyCatalogue_RaisesNoNotificationWhileThePlotIsLocked()
	{
		using var chart = CreateChart(new TestScheduler());
		var first = new Pen(1, "Pen 1", ["Group A"], "#ff0000");
		var second = new Pen(2, "Pen 2", ["Group A"], "#00ff00");
		chart.ApplyCatalogue([first, second]);
		chart.SetActivePen(second.PenId);
		var raised = new List<(string? Name, bool IsLockHeld)>();
		chart.PropertyChanged += (_, args) => raised.Add((args.PropertyName, Monitor.IsEntered(chart.Plot.Sync)));

		chart.ApplyCatalogue([first]);
		chart.ApplyCatalogue([first, second]);
		chart.ApplyCatalogue([]);

		raised.Should().Contain(entry => entry.Name == nameof(chart.ActivePenId));
		raised.Should().Contain(entry => entry.Name == nameof(chart.DrawnPenId));
		raised.Should().Contain(entry => entry.Name == nameof(chart.ScalesRevision));
		raised.Should().AllSatisfy(entry => entry.IsLockHeld.Should().BeFalse());
	}

	// docs/architecture/charting.md#module-layout-avalonia-views--view-models--core-models
	[AvaloniaFact]
	public async Task ARepeatedDataAreaWidth_ChangesNothing()
	{
		using var chart = CreateChart(new TestScheduler());
		_ = new TrendChartView { DataContext = chart };
		await RenderFrameAsTheRenderThreadDoes(chart.Plot, NarrowPlotWidth, PlotHeight);
		var windowChanges = 0;
		chart.Navigation.WindowChanged += (_, _) => windowChanges++;
		var revisionBefore = chart.ScalesRevision;

		await RenderFrameAsTheRenderThreadDoes(chart.Plot, NarrowPlotWidth, PlotHeight);
		await RenderFrameAsTheRenderThreadDoes(chart.Plot, NarrowPlotWidth, PlotHeight);

		chart.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);
		windowChanges.Should().Be(0);
		chart.ScalesRevision.Should().Be(revisionBefore);
	}

	[AvaloniaFact]
	public async Task ANewDataAreaWidth_RetargetsTheColumnCount()
	{
		using var chart = CreateChart(new TestScheduler());
		_ = new TrendChartView { DataContext = chart };
		await RenderFrameAsTheRenderThreadDoes(chart.Plot, NarrowPlotWidth, PlotHeight);
		chart.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);
		var windowChanges = 0;
		chart.Navigation.WindowChanged += (_, _) => windowChanges++;

		await RenderFrameAsTheRenderThreadDoes(chart.Plot, WidePlotWidth, PlotHeight);

		chart.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
		windowChanges.Should().Be(1);
	}

	[AvaloniaFact]
	public async Task AReboundView_ReportsTheWidthToTheNewChartAlone()
	{
		using var first = CreateChart(new TestScheduler());
		using var second = CreateChart(new TestScheduler());
		var view = new TrendChartView { DataContext = first };

		view.DataContext = second;

		await RenderFrameAsTheRenderThreadDoes(first.Plot, NarrowPlotWidth, PlotHeight);
		await RenderFrameAsTheRenderThreadDoes(second.Plot, NarrowPlotWidth, PlotHeight);

		first.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns, "the view let go of it");
		second.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);
	}

	private static async Task RenderFrameAsTheRenderThreadDoes(Plot plot, int width, int height)
	{
		await Task.Run(() => RenderFrame(plot, width, height), TestContext.Current.CancellationToken);
		Dispatcher.UIThread.RunJobs();
	}

	private static void RenderFrame(Plot plot)
	{
		RenderFrame(plot, PlotWidth, PlotHeight);
	}

	private static void RenderFrame(Plot plot, int width, int height)
	{
		using var image = plot.GetImage(width, height);
	}
}
