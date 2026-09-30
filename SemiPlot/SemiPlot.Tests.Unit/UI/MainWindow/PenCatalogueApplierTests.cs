using System.Reactive.Linq;

using Avalonia.Headless.XUnit;
using Avalonia.Threading;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;
using SemiPlot.UI.Chart;
using SemiPlot.UI.Localization;
using SemiPlot.UI.MainWindow;
using SemiPlot.UI.Messages;

using Xunit;

using static SemiPlot.Tests.Unit.UI.MainWindow.MainWindowTestBuilder;

namespace SemiPlot.Tests.Unit.UI.MainWindow;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class PenCatalogueApplierTests
{
	[AvaloniaFact]
	public void ADeltaAddingAPenToAChartWithPens_ReachesTheChartAndTheSidebarAndReadsTheExtentOnce()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var extentQueriesAtStart = stand.Provider.ExtentQueryCount;
		stand.Provider.Pens = [.. stand.Provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		viewModel.ChartViewModel.FindPen(3).Should().NotBeNull();
		viewModel.LegendViewModel.Groups.SelectMany(group => group.Rows).Select(row => row.Name).Should()
			.Equal("Pen 1", "Pen 2", "Pen 3");
		stand.Provider.ExtentQueryCount.Should().Be(extentQueriesAtStart + 1);
		viewModel.MessagePanel.Entries.Should().BeEmpty();
	}

	[AvaloniaFact]
	public void ADeltaAddingAPenWithOlderRows_LetsTheChartPanBackToThem()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel;
		var olderFirstSample = chart.Navigation.FirstSample - TimeSpan.FromDays(30.0);
		provider.ArchiveFirstUtc = olderFirstSample;
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);
		chart.Navigation.PanBy(TimeSpan.FromDays(-60.0));

		viewModel.MinimapViewModel.ExtentFirst.Should().Be(olderFirstSample);
		chart.Navigation.From.Should().Be(olderFirstSample);
	}

	[AvaloniaFact]
	public void ADeltaGivingAnEmptyChartItsFirstPens_SeedsTheNavigationBeforeTheHistoryQuery()
	{
		using var stand = NewWindowStand(pens: []);
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel;
		provider.GateExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeTrue("the pens wait for the extent");
		provider.HistoryQueryCount.Should().Be(0);

		var extent = new ArchiveExtent(provider.ArchiveFirstUtc, provider.ArchiveLastUtc);
		provider.ExtentGate.SetResult(Result.Ok(extent));
		Dispatcher.UIThread.RunJobs();

		// The apply resumes on the dispatcher after the minimap's hop to the UI scheduler, so the debounce it
		// starts needs a second advance.
		Advance(stand, TimeSpan.FromSeconds(1));
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeFalse();
		chart.Navigation.FirstSample.Should().Be(provider.ArchiveFirstUtc);
		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedPenIds.Should().Equal(1);
		provider.LastQueriedFromUtc.Should().BeBefore(provider.ArchiveLastUtc);
	}

	[AvaloniaFact]
	public void ADeltaGivingAnEmptyChartItsFirstPens_WhenTheExtentReadFails_StillAppliesThePens()
	{
		using var stand = NewWindowStand(pens: []);
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel;
		var firstSampleBefore = chart.Navigation.FirstSample;
		var extentQueriesAtStart = provider.ExtentQueryCount;
		provider.FailExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.HasNoPens.Should().BeFalse();
		chart.Navigation.FirstSample.Should().Be(firstSampleBefore, "no extent came back to seed it with");
		provider.ExtentQueryCount.Should().Be(extentQueriesAtStart + 1);
		provider.LastQueriedPenIds.Should().Equal(1);
		viewModel.MessagePanel.Entries.Should().ContainSingle()
			.Which.View.Title.Should().Be(Resources.FailureArchiveReadFailedTitle);
	}

	[AvaloniaFact]
	public void ADeltaThatAddsNoPen_ReadsNoExtent()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var stored = provider.Pens;
		var extentQueriesAtStart = provider.ExtentQueryCount;

		provider.Pens = [stored[0]];
		AdvanceOneRead(stand);
		provider.Pens = [stored[0] with { Name = "Chamber pressure" }];
		AdvanceOneRead(stand);

		viewModel.ChartViewModel.FindPen(2).Should().BeNull();
		viewModel.ChartViewModel.FindPen(1)!.Pen.Name.Should().Be("Chamber pressure");
		provider.ExtentQueryCount.Should().Be(extentQueriesAtStart);
	}

	[AvaloniaFact]
	public void ADeltaWhoseExtentReloadThrows_AddsOnePanelEntry_AndTheNextDeltaStillApplies()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var extentQueriesAtStart = provider.ExtentQueryCount;
		provider.GateExtent = true;
		provider.ExtentGate.SetException(new InvalidOperationException("the extent read threw"));
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		viewModel.ChartViewModel.FindPen(3).Should().NotBeNull("the reload follows the apply");
		provider.ExtentQueryCount.Should().Be(extentQueriesAtStart + 1);
		viewModel.MessagePanel.Entries.Should().ContainSingle();

		provider.Pens = [provider.Pens[0] with { Name = "Chamber pressure" }, provider.Pens[1], provider.Pens[2]];
		AdvanceOneRead(stand);

		viewModel.ChartViewModel.FindPen(1)!.Pen.Name.Should().Be("Chamber pressure");
		provider.ExtentQueryCount.Should().Be(extentQueriesAtStart + 1, "a delta that adds no pen reads no extent");
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	// A chart listener throws once, after the chart took pen 3 and before the sidebar learned of it.
	[AvaloniaFact]
	public void ADeltaWhoseApplyThrows_AddsOnePanelEntry_AndTheNextReadCarriesItAgain()
	{
		using var stand = NewWindowStand();
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel;
		var throwsOnce = true;
		chart.PropertyChanged += (_, change) =>
		{
			if (throwsOnce && change.PropertyName == nameof(TrendChartViewModel.HasNoPens))
			{
				throwsOnce = false;

				throw new InvalidOperationException("a chart listener threw");
			}
		};
		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		AdvanceOneRead(stand);

		throwsOnce.Should().BeFalse("the listener has to have thrown");
		LegendRowNames(viewModel).Should().NotContain("Pen 3");
		viewModel.MessagePanel.Entries.Should().ContainSingle();

		AdvanceOneRead(stand);
		Advance(stand, TimeSpan.FromSeconds(1));

		LegendRowNames(viewModel).Should().Contain("Pen 3");
		provider.LastQueriedPenIds.Should().Contain(3);
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	// The first delta waits for the extent while the second, read against it, queues behind. The first throws,
	// so the second reaches a chart that never took pen 1, and the rebase puts the loop's baseline behind both:
	// the next read names pen 2 as added, not revised.
	[AvaloniaFact]
	public void AnApplyThatThrowsWithTheNextDeltaQueued_StillTakesALaterRevisionOfThatDeltasPen()
	{
		using var stand = NewWindowStand(pens: []);
		var viewModel = stand.ViewModel;
		var provider = stand.Provider;
		var chart = viewModel.ChartViewModel;
		var first = new Pen(1, "Pen 1", ["Group A"], "#ff0000");
		var second = new Pen(2, "Pen 2", ["Group A"], "#00ff00");
		provider.GateExtent = true;
		provider.Pens = [first];
		AdvanceOneRead(stand);
		provider.Pens = [first, second];
		AdvanceOneRead(stand);

		provider.GateExtent = false;
		provider.ExtentGate.SetException(new InvalidOperationException("the extent read threw"));
		Advance(stand, TimeSpan.FromSeconds(1));

		chart.Pens.Select(state => state.Pen).Should().Equal(first, second);

		provider.Pens = [first, second with { Color = "#0000ff" }];
		AdvanceOneRead(stand);

		chart.FindPen(2)!.Pen.Color.Should().Be("#0000ff");
		viewModel.MessagePanel.Entries.Should().ContainSingle();
	}

	[AvaloniaFact]
	public void AnApplyAwaitingTheExtentAtDisposal_AppliesNothingAndReportsNothing()
	{
		using var panel = new MessagePanelViewModel();
		var stand = NewWindowStand(pens: [], panel: panel);
		var provider = stand.Provider;
		provider.GateExtent = true;
		provider.Pens = [new Pen(1, "Pen 1", ["Group A"], "#ff0000")];
		AdvanceOneRead(stand);

		stand.Dispose();
		provider.ExtentGate.SetResult(Result.Ok(new ArchiveExtent(provider.ArchiveFirstUtc, provider.ArchiveLastUtc)));
		Dispatcher.UIThread.RunJobs();

		panel.Entries.Should().BeEmpty();
	}

	private static IEnumerable<string> LegendRowNames(MainWindowViewModel viewModel)
	{
		return viewModel.LegendViewModel.Groups.SelectMany(group => group.Rows).Select(row => row.Name);
	}

	private static void AdvanceOneRead(WindowStand stand)
	{
		Advance(stand, PenCatalogueSync.ReadInterval);
	}

	// One tick past the span: a virtual scheduler runs an action scheduled for now on the next tick, and the
	// extent reload the apply awaits schedules its own on the UI scheduler.
	private static void Advance(WindowStand stand, TimeSpan time)
	{
		stand.Scheduler.AdvanceBy(time.Ticks + 1);
		Dispatcher.UIThread.RunJobs();
	}
}
