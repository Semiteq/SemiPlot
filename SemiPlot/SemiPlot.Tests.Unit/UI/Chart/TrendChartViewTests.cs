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

using SemiPlot.Tests.Unit.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

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

	private static ScottPlot.Color FigureBackgroundUnder(ThemeVariant variant)
	{
		return ThemeProbe.PlotColour("AppPanelBackgroundBrush", variant);
	}

	// Both schedulers are virtual here, unlike the other chart tests: the view subscribes to
	// RedrawRequested, whose Sample needs a scheduler that can time out. ImmediateScheduler runs a periodic
	// schedule by sleeping on the calling thread, so subscribing on it never returns.
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
			coordinator, scheduler, scheduler, NullLogger<TrendChartViewModel>.Instance);
	}
}
