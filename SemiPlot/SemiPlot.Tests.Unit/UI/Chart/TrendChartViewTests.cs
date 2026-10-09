using System.Reactive.Concurrency;

using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;
using Avalonia.VisualTree;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using ScottPlot.Avalonia;

using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

using BitmapCache = Avalonia.Media.BitmapCache;


namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);

	[AvaloniaFact]
	public void RenderedFrame_ReportsItsDataAreaWidthToTheViewModel()
	{
		using var viewModel = CreateViewModel();

		// Binding the view is the whole setup: it subscribes the seam to the view model's plot.
		_ = new TrendChartView { DataContext = viewModel };

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);

		// A canvas this narrow leaves a data area under the minimum column count, so the reported width
		// lands on MinColumns whatever the exact axis padding is.
		viewModel.Plot.RenderInMemory(320, 240);
		Dispatcher.UIThread.RunJobs();

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);

		viewModel.Plot.RenderInMemory(2600, 800);
		Dispatcher.UIThread.RunJobs();

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public void TwoRendersAtOneWidth_ReportTheWidthOnce()
	{
		using var viewModel = CreateViewModel();
		_ = new TrendChartView { DataContext = viewModel };
		viewModel.Plot.RenderInMemory(320, 240);
		Dispatcher.UIThread.RunJobs();
		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);

		// A width reported past the view marks whether the second render posts its width again.
		viewModel.ReportDataAreaWidth(2600.0);
		viewModel.Plot.RenderInMemory(320, 240);
		Dispatcher.UIThread.RunJobs();

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MaxColumns);
	}

	[AvaloniaFact]
	public void DetachedViewModel_NoLongerReceivesWidthReports()
	{
		using var viewModel = CreateViewModel();
		var view = new TrendChartView { DataContext = viewModel };

		viewModel.Plot.RenderInMemory(320, 240);
		Dispatcher.UIThread.RunJobs();
		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);

		view.DataContext = null;

		viewModel.Plot.RenderInMemory(2600, 800);
		Dispatcher.UIThread.RunJobs();

		viewModel.Navigation.TargetColumnCount.Should().Be(HistoryColumnTarget.MinColumns);
	}

	[AvaloniaFact]
	public void ALoadedView_RepaintsThePlotWhenTheApplicationVariantChanges()
	{
		using var scope = ThemeProbe.PreserveVariant();
		var application = Application.Current!;
		using var viewModel = CreateViewModel();
		var window = new Window { Content = new TrendChartView { DataContext = viewModel } };
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			window.Show();
			Dispatcher.UIThread.RunJobs();
			var light = viewModel.Plot.FigureBackground.Color;

			application.RequestedThemeVariant = ThemeVariant.Dark;
			Dispatcher.UIThread.RunJobs();

			viewModel.Plot.FigureBackground.Color.Should().NotBe(light);
			viewModel.Plot.FigureBackground.Color.Should().Be(FigureBackgroundUnder(ThemeVariant.Dark));
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaFact]
	public void AViewWithNoViewModel_StillPaintsTheChartAreaFromThePalette()
	{
		using var scope = ThemeProbe.PreserveVariant();
		var application = Application.Current!;
		var window = new Window { Content = new TrendChartView() };
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Dark;
			window.Show();
			Dispatcher.UIThread.RunJobs();

			var plot = window.GetVisualDescendants().OfType<AvaPlot>().Single().Plot;

			plot.FigureBackground.Color.Should().Be(FigureBackgroundUnder(ThemeVariant.Dark));
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaFact]
	public void AnUnloadedView_StopsFollowingTheApplicationVariant()
	{
		using var scope = ThemeProbe.PreserveVariant();
		var application = Application.Current!;
		using var viewModel = CreateViewModel();
		var window = new Window { Content = new TrendChartView { DataContext = viewModel } };

		application.RequestedThemeVariant = ThemeVariant.Light;
		window.Show();
		Dispatcher.UIThread.RunJobs();

		window.Close();
		Dispatcher.UIThread.RunJobs();
		var afterUnload = viewModel.Plot.FigureBackground.Color;

		application.RequestedThemeVariant = ThemeVariant.Dark;
		Dispatcher.UIThread.RunJobs();

		viewModel.Plot.FigureBackground.Color.Should().Be(afterUnload);
	}

	// The empty catalogue is a state of the chart area, driven by the chart's own pen collection: the
	// sentence withdraws as soon as a pen arrives, and it does not depend on the window that hosts the view.
	[AvaloniaFact]
	public void EmptyCatalogueMessage_ShowsWithNoPensAndWithdrawsWhenOneArrives()
	{
		using var viewModel = CreateViewModel();
		var window = new Window { Content = new TrendChartView { DataContext = viewModel } };
		try
		{
			window.Show();
			Dispatcher.UIThread.RunJobs();
			var message = window
				.GetVisualDescendants()
				.OfType<TextBlock>()
				.Single(block => block.Name == "EmptyCatalogueMessage");

			message.IsVisible.Should().BeTrue();
			message.Text.Should().Be(Resources.FormatEmptyCatalogueMessage(
				Resources.MenuEdit,
				Resources.MenuEditPensAndGroups,
				Resources.PenEditorRefresh));
			message.Text.Should().Contain(Resources.MenuEdit)
				.And.Contain(Resources.MenuEditPensAndGroups)
				.And.Contain(Resources.PenEditorRefresh);

			viewModel.AddPen(new Pen(7, "Chamber pressure", ["Pressure"], "#3574F0"));
			Dispatcher.UIThread.RunJobs();

			message.IsVisible.Should().BeFalse();
		}
		finally
		{
			window.Close();
		}
	}

	[AvaloniaFact]
	public void ABurstOfRedrawRequests_JoinsOneFrame()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		DriveOneFrame();
		var framesBefore = shown.View.ServedFrameCount;

		for (var request = 0; request < 10; request++)
		{
			viewModel.SetDeltaModeEnabled(request % 2 == 0);
		}

		DriveOneFrame();

		shown.View.ServedFrameCount.Should().Be(framesBefore + 1, "the requests before one frame join it");

		DriveOneFrame();

		shown.View.ServedFrameCount.Should().Be(framesBefore + 1, "a frame nothing requested serves nothing");
	}

	[AvaloniaFact]
	public void ARequestOnADetachedView_LeavesTheNextRequestAfterAttachingServed()
	{
		using var viewModel = CreateLoadedViewModel();
		var view = new TrendChartView { DataContext = viewModel };
		viewModel.SetDeltaModeEnabled(true);
		var window = new Window { Width = 900, Height = 600 };
		try
		{
			window.Show();
			DriveOneFrame();

			window.Content = view;
			DriveOneFrame();
			DriveOneFrame();
			var framesAfterAttaching = view.ServedFrameCount;
			viewModel.SetDeltaModeEnabled(false);
			DriveOneFrame();

			framesAfterAttaching.Should().Be(1, "the view requests its first frame when it loads");
			view.ServedFrameCount.Should().Be(2, "the request on the detached view left no frame pending");
		}
		finally
		{
			window.Close();
		}
	}

	// A thread of its own rather than an await, so the dispatcher runs no frame while the request is refused.
	// docs/architecture/charting.md#the-frame-paced-redraw
	[AvaloniaFact]
	public void ARequestRefusedOffTheUiThread_LeavesTheViewsNextFrameServed()
	{
		using var scope = ThemeProbe.PreserveVariant();
		var application = Application.Current!;
		application.RequestedThemeVariant = ThemeVariant.Light;
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		DriveOneFrame();
		var framesBefore = shown.View.ServedFrameCount;
		Exception? refusal = null;

		var offTheUiThread = new Thread(() => refusal = Record.Exception(() => viewModel.SetActivePen(1)));
		offTheUiThread.Start();
		offTheUiThread.Join();
		application.RequestedThemeVariant = ThemeVariant.Dark;
		DriveOneFrame();

		refusal.Should().BeOfType<InvalidOperationException>("RequestAnimationFrame verifies the UI thread");
		shown.View.ServedFrameCount.Should().Be(framesBefore + 1, "the refused request left no frame pending");
	}

	[AvaloniaFact]
	public void AFramePendingWhenTheViewModelIsDisposed_ServesNothing()
	{
		var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		DriveOneFrame();
		var framesBefore = shown.View.ServedFrameCount;
		viewModel.SetDeltaModeEnabled(true);

		viewModel.Dispose();
		var drive = DriveOneFrame;

		drive.Should().NotThrow();
		shown.View.ServedFrameCount.Should().Be(framesBefore);
	}

	[AvaloniaFact]
	public void AFramePendingWhenTheViewModelIsReplaced_ServesNothing()
	{
		using var viewModel = CreateLoadedViewModel();
		using var replacement = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		DriveOneFrame();
		var framesBefore = shown.View.ServedFrameCount;
		viewModel.SetDeltaModeEnabled(true);

		shown.View.DataContext = replacement;
		DriveOneFrame();

		shown.View.ServedFrameCount.Should().Be(framesBefore);
	}

	// docs/architecture/charting.md#hover-and-the-plot-cache
	[AvaloniaFact]
	public void ThePlotIsCachedThroughItsOnlyAncestor()
	{
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);

		var layer = shown.View.FindControl<Decorator>("PlotLayer");

		layer.Should().NotBeNull();
		layer!.CacheMode.Should().BeOfType<BitmapCache>();
		layer.Child.Should().BeSameAs(shown.PlotControl);
		shown.PlotControl.CacheMode.Should().BeNull();
	}

	private static ScottPlot.Color FigureBackgroundUnder(ThemeVariant variant)
	{
		return ThemeProbe.PlotColour("AppPanelBackgroundBrush", variant);
	}

	// Both schedulers are virtual: docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view.
	private static TrendChartViewModel CreateViewModel()
	{
		var scheduler = new TestScheduler();
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
