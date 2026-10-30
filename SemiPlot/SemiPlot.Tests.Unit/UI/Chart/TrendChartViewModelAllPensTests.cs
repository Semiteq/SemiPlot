using System.Reactive.Linq;

using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Collection(ProcessGlobalStateCollection.Name)]
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartViewModelAllPensTests
{
	private static readonly IReadOnlyList<Pen> _sixPens =
	[
		.. Enumerable.Range(1, 6).Select(penId => new Pen(
			penId, $"Pen {penId}", ["Group A"], "#ff0000", EnabledOnStart: penId % 3 == 0))
	];

	[AvaloniaFact]
	public void HideAllThenShowAll_KeepsThePickedActivePen()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.ApplyCatalogue([.. _sixPens.Select(pen => pen with { EnabledOnStart = true })]);
		viewModel.SetActivePen(4).Should().BeTrue();

		viewModel.SetAllPensVisibility(false);

		viewModel.ActivePenId.Should().Be(4);
		viewModel.SetAllPensVisibility(true);

		viewModel.ActivePenId.Should().Be(4);
		viewModel.DrawnPenId.Should().Be(4);
		viewModel.Pens.Should().AllSatisfy(state => state.IsVisible.Should().BeTrue());
	}

	[AvaloniaFact]
	public void HideAll_LeavesNoVisiblePenAndNoDrawnAxis()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);

		viewModel.SetAllPensVisibility(false);

		viewModel.DrawnPenId.Should().BeNull();
		viewModel.Pens.Should().AllSatisfy(state =>
		{
			state.IsVisible.Should().BeFalse();
			state.Line.IsVisible.Should().BeFalse();
			state.Line.Axes.YAxis.IsVisible.Should().BeFalse();
		});
	}

	[AvaloniaFact]
	public void ShowAll_ReadsEverySwitchedOnPenInOneRead()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.SetAllPensVisibility(true);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.ChartHistoryQueries.Skip(queriesBefore).Should().ContainSingle()
			.Which.PenIds.Should().Equal(1, 2, 4, 5);
	}

	[AvaloniaFact]
	public void ShowAll_ClearsTheCurveAPenWasHiddenWithBeforeTheReadLands()
	{
		var (viewModel, scheduler, _, _) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var width = viewModel.Navigation.To - viewModel.Navigation.From;
		viewModel.SetPenVisibility(3, false);
		viewModel.Navigation.PanBy(-4 * width);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var switchedOn = viewModel.FindPen(3)!;
		switchedOn.Line.SnapshotColumns().Should().HaveCount(2, "the pen keeps the curve it was hidden with");

		viewModel.SetAllPensVisibility(true);

		switchedOn.Line.SnapshotColumns().Should().BeEmpty();
		viewModel.FindPen(6)!.Line.SnapshotColumns().Should().HaveCount(2, "a pen read for the range keeps it");
	}

	[AvaloniaFact]
	public void ShowAllBeforeTheHistoryStarts_ReadsAndClearsNothing()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);
		var hidden = viewModel.FindPen(1)!;
		hidden.LoadHistory(new PenHistoryEnvelope(1, [From, To], [1.0, 1.0], [1.0, 1.0], [1.0, 1.0]), To, null);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.SetAllPensVisibility(true);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
		hidden.Line.SnapshotColumns().Should().HaveCount(2);
	}

	[AvaloniaFact]
	public void ShowAllMovingTheLayer_DrawsAndReadsEveryPenOnceAtTheCoarserLayer()
	{
		const int ReferencePenCount = ChartNavigationController.ReferencePenCount;
		const int ColumnCount = 512;
		var allPenIds = Enumerable.Range(1, 2 * ReferencePenCount).ToArray();
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue([
			.. allPenIds.Select(penId => new Pen(
				penId, $"Pen {penId}", ["Group A"], "#ff0000", EnabledOnStart: penId <= ReferencePenCount))
		]);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		viewModel.ReportDataAreaWidth(ColumnCount);
		var rawCeiling = TimeSpan.FromSeconds(15.0) * ColumnCount;
		var navigation = viewModel.Navigation;
		navigation.ZoomAt(0.98 * rawCeiling / (navigation.To - navigation.From), navigation.To);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);
		navigation.ActiveLayer.Should().Be(AggregationLayer.Raw);
		var queriesBefore = provider.HistoryQueryCount;
		var revisionBefore = viewModel.ScalesRevision;
		var redraws = 0;
		using var counter = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		viewModel.SetAllPensVisibility(true);

		navigation.ActiveLayer.Should().Be(AggregationLayer.Minute);
		viewModel.ScalesRevision.Should().Be(revisionBefore + 1, "the window change settled the axes");
		redraws.Should().Be(1, "the window change drew");
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		var read = provider.ChartHistoryQueries.Skip(queriesBefore).Should().ContainSingle().Subject;
		read.Layer.Should().Be(AggregationLayer.Minute);
		read.PenIds.Should().Equal(allPenIds);
	}

	[AvaloniaFact]
	public void HideAll_ReadsNothing()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);
		LoadInitialHistory(viewModel, scheduler, From.AddDays(-30.0), To);
		var queriesBefore = provider.HistoryQueryCount;

		viewModel.SetAllPensVisibility(false);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1.0).Ticks);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaTheory]
	[InlineData(true)]
	[InlineData(false)]
	public void EachCall_SettlesTheAxesAndTheRedrawOnce(bool isVisible)
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);
		var revisionBefore = viewModel.ScalesRevision;
		var redraws = 0;
		using var counter = viewModel.RedrawRequested.Subscribe(_ => redraws++);

		viewModel.SetAllPensVisibility(isVisible);

		viewModel.ScalesRevision.Should().Be(revisionBefore + 1);
		redraws.Should().Be(1);
	}

	[AvaloniaFact]
	public void TheCommands_SwitchEveryPen()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.ApplyCatalogue(_sixPens);

		viewModel.ShowAllPensCommand.Execute().Subscribe();

		viewModel.Pens.Should().AllSatisfy(state => state.IsVisible.Should().BeTrue());

		viewModel.HideAllPensCommand.Execute().Subscribe();

		viewModel.Pens.Should().AllSatisfy(state => state.IsVisible.Should().BeFalse());
	}
}
