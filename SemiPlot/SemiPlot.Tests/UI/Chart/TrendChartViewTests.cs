using System.Reactive.Concurrency;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Tests.UI.Bridge;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

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
