using Avalonia;
using Avalonia.Controls;
using Avalonia.Headless.XUnit;
using Avalonia.Styling;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using ScottPlot.Avalonia;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;
using static SemiPlot.Tests.Unit.UI.Chart.ChartViewTestBuilder;

using BitmapCache = Avalonia.Media.BitmapCache;


namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewTests
{

	[AvaloniaFact]
	public void DetachedViewModel_NoLongerReceivesWidthReports()
	{
		using var viewModel = CreateChart(new TestScheduler());
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
		using var viewModel = CreateChart(new TestScheduler());
		var window = new Window { Content = new TrendChartView { DataContext = viewModel } };
		try
		{
			application.RequestedThemeVariant = ThemeVariant.Light;
			ViewProbe.Realise(window);
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

	// docs/architecture/ui-theme.md#the-plot
	[AvaloniaFact]
	public void AnAxisAddedAfterTheFirstPaint_IsPaintedUnderTheCurrentTheme()
	{
		using var scope = ThemeProbe.ApplyVariant(ThemeVariant.Dark);
		using var viewModel = CreateLoadedViewModel();
		using var shown = ShowChart(viewModel);
		DriveOneFrame();
		var axesBefore = viewModel.AxisCount;

		var added = viewModel.AddPen(new Pen(3, "Pen 3", ["Group A"], "#0000ff"));
		DriveOneFrame();

		viewModel.AxisCount.Should().Be(axesBefore + 1);
		added.Line.Axes.YAxis.TickLabelStyle.ForeColor
			.Should().Be(ThemeProbe.PlotColour("AppSecondaryForegroundBrush", ThemeVariant.Dark));
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
			ViewProbe.Realise(window);

			var plot = ((TrendChartView)window.Content!).FindControl<AvaPlot>("PlotControl")!.Plot;

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
		using var viewModel = CreateChart(new TestScheduler());
		var window = new Window { Content = new TrendChartView { DataContext = viewModel } };

		application.RequestedThemeVariant = ThemeVariant.Light;
		ViewProbe.Realise(window);

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
		using var viewModel = CreateChart(new TestScheduler());
		var view = new TrendChartView { DataContext = viewModel };
		var window = new Window { Content = view };
		try
		{
			ViewProbe.Realise(window);
			var message = ViewProbe.Named<TextBlock>(view, "EmptyCatalogueMessage");

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
}
