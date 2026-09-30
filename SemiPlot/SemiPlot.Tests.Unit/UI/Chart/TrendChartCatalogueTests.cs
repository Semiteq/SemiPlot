using Avalonia.Headless.XUnit;

using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.ChartTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

// docs/architecture/charting.md#applying-a-catalogue-read
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class TrendChartCatalogueTests
{
	private static readonly TimeSpan _testDeadline = TimeSpan.FromSeconds(10.0);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[AvaloniaFact]
	public void ARemoval_DropsThePenState()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		viewModel.AddPen(new Pen(7, "Heater", ["Group A"], "#ff0000"));

		viewModel.ApplyCatalogue([]);

		viewModel.Pens.Should().BeEmpty();
		viewModel.FindPen(7).Should().BeNull();
		viewModel.Plot.GetPlottables().Should().BeEmpty();
	}

	[AvaloniaFact]
	public async Task AColourAndLineStyleRevision_RestylesTheLineAndKeepsTheSession()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		viewModel.SetActivePen(2);
		viewModel.SetPenVisibility(1, false);
		var revised = catalogue[0] with { Color = "#0000ff", LineStyle = PenLineStyle.Stepped };

		viewModel.ApplyCatalogue([revised, catalogue[1]]);

		var state = viewModel.FindPen(1)!;
		state.Pen.Should().Be(revised);
		state.IsVisible.Should().BeFalse();
		state.Line.Columns.Should().HaveCount(2);
		viewModel.ActivePenId.Should().Be(2);

		viewModel.SetPenVisibility(1, true);

		var (onDiagonal, onHeldStep) = RenderedRise.Draw(viewModel, state, RenderedRise.Blue);

		onDiagonal.Should().BeFalse();
		onHeldStep.Should().BeTrue();
	}

	[AvaloniaFact]
	public void TheCatalogue_IsWhatTheChartShowsInOrder()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		IReadOnlyList<Pen> catalogue = [provider.Pens[1], provider.Pens[0]];

		viewModel.ApplyCatalogue(catalogue);

		viewModel.Catalogue.Should().Equal(catalogue);
	}

	[AvaloniaFact]
	public void APenRevision_IsAnnounced()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		var state = viewModel.FindPen(1)!;
		var raised = new List<string?>();
		state.PropertyChanged += (_, change) => raised.Add(change.PropertyName);

		viewModel.ApplyCatalogue([catalogue[0] with { Unit = "kPa" }, catalogue[1]]);

		raised.Should().Equal(nameof(TrendPenState.Pen));
	}

	[AvaloniaFact]
	public void AStoredScaleChange_ReplacesTheSessionAxisOfThatPenOnly()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		viewModel.SetAxisLimits(2, 20.0, 80.0);
		var revised = catalogue[0] with { ScaleMin = 0.0, ScaleMax = 50.0 };

		viewModel.ApplyCatalogue([revised, catalogue[1]]);

		viewModel.ScaleSettings[1].Should().Be(new PenScaleSettings(1)
		{
			Mode = ScaleMode.Manual,
			ManualMin = 0.0,
			ManualMax = 50.0
		});
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((0.0, 50.0));
		viewModel.ScaleSettings[2].ManualMin.Should().Be(20.0);
		viewModel.ScaleSettings[2].ManualMax.Should().Be(80.0);
	}

	[AvaloniaFact]
	public void ARevisionWithoutAScaleChange_KeepsTheSessionAxis()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		viewModel.SetAxisLimits(1, 10.0, 90.0);

		viewModel.ApplyCatalogue([catalogue[0] with { Name = "Renamed" }, catalogue[1]]);

		viewModel.ScaleSettings[1].Mode.Should().Be(ScaleMode.Manual);
		viewModel.ScaleRangeForPen(1)!.Value.Should().Be((10.0, 90.0));
	}

	[AvaloniaFact]
	public void AnEnabledOnStartRevision_KeepsTheVisibility()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var hidden = new Pen(3, "Pen 3", ["Group B"], "#0000ff", EnabledOnStart: false);
		IReadOnlyList<Pen> catalogue = [.. provider.Pens, hidden];
		viewModel.ApplyCatalogue(catalogue);

		viewModel.ApplyCatalogue(
			[catalogue[0] with { EnabledOnStart = false }, catalogue[1], hidden with { EnabledOnStart = true }]);

		viewModel.FindPen(1)!.IsVisible.Should().BeTrue();
		viewModel.FindPen(1)!.Pen.EnabledOnStart.Should().BeFalse();
		viewModel.FindPen(3)!.IsVisible.Should().BeFalse();
		viewModel.FindPen(3)!.Pen.EnabledOnStart.Should().BeTrue();
	}

	[AvaloniaFact]
	public void AnAddedPenDisabledOnStart_JoinsHidden()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		var added = new Pen(3, "Pen 3", ["Group B"], "#0000ff", EnabledOnStart: false);

		viewModel.ApplyCatalogue([.. catalogue, added]);

		viewModel.FindPen(3)!.IsVisible.Should().BeFalse();
		viewModel.FindPen(3)!.Line.IsVisible.Should().BeFalse();
	}

	[AvaloniaFact]
	public void AnAddedPenEnabledOnStart_JoinsVisible()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		var added = new Pen(3, "Pen 3", ["Group B"], "#0000ff");

		viewModel.ApplyCatalogue([.. catalogue, added]);

		viewModel.FindPen(3)!.IsVisible.Should().BeTrue();
		viewModel.FindPen(3)!.Line.IsVisible.Should().BeTrue();
	}

	[AvaloniaFact]
	public async Task AnAddition_HandsTheCoordinatorTheNewSetAndQueriesHistoryForEveryPen()
	{
		var (viewModel, scheduler, coordinator, provider) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var queriesBefore = provider.HistoryQueryCount;
		var batches = new List<RealtimeBatch>();
		using var watch = coordinator.RealtimeBatches.Subscribe(batches.Add);
		provider.Pens = [.. catalogue, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];

		viewModel.ApplyCatalogue(provider.Pens);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore + 1);
		provider.LastQueriedPenIds.Should().BeEquivalentTo([1, 2, 3]);
		batches[^1].Pens.Select(values => values.PenId).Should().BeEquivalentTo([1, 2, 3]);
	}

	[AvaloniaFact]
	public async Task ARemoval_HandsTheCoordinatorTheNewSetAndQueriesHistoryForEveryPen()
	{
		var (viewModel, scheduler, coordinator, provider) = CreateViewModel(
			realtimeInterval: TimeSpan.FromMilliseconds(10));
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var queriesBefore = provider.HistoryQueryCount;
		var batches = new List<RealtimeBatch>();
		using var watch = coordinator.RealtimeBatches.Subscribe(batches.Add);
		provider.Pens = [catalogue[0]];

		viewModel.ApplyCatalogue(provider.Pens);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		viewModel.FindPen(2).Should().BeNull();
		provider.HistoryQueryCount.Should().Be(queriesBefore + 1);
		provider.LastQueriedPenIds.Should().Equal(1);
		batches[^1].Pens.Select(values => values.PenId).Should().Equal(1);
	}

	// ActivePenId is raised inside the apply, ahead of the live edge move, so its listener stands for any throw
	// there; the pens shown then differ from the set the live edge follows until the next catalogue.
	[AvaloniaFact]
	public void AnApplyThatThrowsBeforeTheLiveEdgeMoves_IsCaughtUpByTheNextCatalogue()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var throwsOnce = true;
		viewModel.PropertyChanged += (_, change) =>
		{
			if (throwsOnce && change.PropertyName == nameof(TrendChartViewModel.ActivePenId))
			{
				throwsOnce = false;

				throw new InvalidOperationException("a chart listener threw");
			}
		};
		var subscriptionsBefore = provider.LiveSubscriptionsOpened;
		IReadOnlyList<Pen> firstPenOnly = [provider.Pens[0]];

		var apply = () => viewModel.ApplyCatalogue(firstPenOnly);

		apply.Should().Throw<InvalidOperationException>();
		provider.LiveSubscriptionsOpened.Should().Be(subscriptionsBefore);

		viewModel.ApplyCatalogue(firstPenOnly);

		provider.LiveSubscriptionsOpened.Should().Be(subscriptionsBefore + 1);
	}

	[AvaloniaFact]
	public void TheStartCatalogue_OpensOneLiveSubscription()
	{
		var (viewModel, _, _, provider) = CreateViewModel();

		viewModel.ApplyCatalogue(provider.Pens);

		provider.LiveSubscriptionsOpened.Should().Be(1);
		provider.OpenLiveSubscriptionCount.Should().Be(1);
	}

	[AvaloniaFact]
	public void PensJoiningBeforeTheFirstHistoryRequest_QueryNothing()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();

		viewModel.ApplyCatalogue(provider.Pens);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(0);
	}

	[AvaloniaFact]
	public async Task ARevisionOnlyDelta_HandsTheCoordinatorNothingAndQueriesNoHistory()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		await LoadInitialHistory(viewModel, scheduler, _from, _to);
		var queriesBefore = provider.HistoryQueryCount;
		var subscriptionsBefore = provider.LiveSubscriptionsOpened;

		viewModel.ApplyCatalogue([catalogue[0] with { Unit = "kPa" }, catalogue[1]]);
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);

		provider.HistoryQueryCount.Should().Be(queriesBefore);
		provider.LiveSubscriptionsOpened.Should().Be(subscriptionsBefore);
	}

	// The dictionary keeps insertion order, so pen 7 would take the slot if the fallback walked it instead.
	[AvaloniaFact]
	public void RemovingTheActivePen_HandsTheSlotToTheFirstVisiblePenInCatalogueOrder()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		var active = new Pen(1, "A", ["Group A"], "#ff0000");
		var hidden = new Pen(9, "B", ["Group A"], "#00ff00", EnabledOnStart: false);
		var third = new Pen(7, "C", ["Group A"], "#0000ff");
		var fourth = new Pen(3, "D", ["Group A"], "#ffff00");
		viewModel.ApplyCatalogue([active, hidden, third, fourth]);
		viewModel.ActivePenId.Should().Be(1);

		viewModel.ApplyCatalogue([fourth with { Name = "0 D" }, hidden, third]);

		viewModel.ActivePenId.Should().Be(3);
	}

	[AvaloniaFact]
	public void Pens_FollowsTheOrderOfTheRead()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		var penTwo = viewModel.FindPen(2);

		viewModel.ApplyCatalogue([catalogue[1] with { Name = "A pen" }, catalogue[0]]);

		viewModel.Pens.Select(state => state.Pen.PenId).Should().Equal(2, 1);
		viewModel.Pens[0].Should().BeSameAs(penTwo);
	}

	[AvaloniaFact]
	public void AnEmptyChartReceivingPens_WithdrawsHasNoPens()
	{
		var (viewModel, _, _, provider) = CreateViewModel();
		var raised = new List<string?>();
		viewModel.PropertyChanged += (_, change) => raised.Add(change.PropertyName);
		viewModel.HasNoPens.Should().BeTrue();

		viewModel.ApplyCatalogue(provider.Pens);

		viewModel.HasNoPens.Should().BeFalse();
		raised.Should().Contain([nameof(TrendChartViewModel.Pens), nameof(TrendChartViewModel.HasNoPens)]);
	}

	[AvaloniaFact]
	public void ADeltaRemovingEveryPen_LeavesAnEmptyChartOnAnEmptyLiveEdge()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		viewModel.RequestInitialHistory();
		// Past the first query and the one its result asks for, so no query is left to land after the removal.
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);
		viewModel.SetAxisLimits(1, 10.0, 90.0);
		var revisionBefore = viewModel.ScalesRevision;
		var queriesBefore = provider.HistoryQueryCount;
		var subscriptionsBefore = provider.LiveSubscriptionsOpened;

		viewModel.ApplyCatalogue([]);
		scheduler.AdvanceBy(TimeSpan.FromSeconds(1).Ticks);

		viewModel.HasNoPens.Should().BeTrue();
		viewModel.ActivePenId.Should().Be(0);
		viewModel.ScaleRangeForPen(1).Should().BeNull();
		viewModel.ScaleRangeForPen(2).Should().BeNull();
		viewModel.ScalesRevision.Should().Be(revisionBefore + 1);
		provider.LiveSubscriptionsOpened.Should().Be(subscriptionsBefore + 1, "the live edge moves onto no pen");
		provider.HistoryQueryCount.Should().Be(queriesBefore);
	}

	[AvaloniaFact]
	public async Task AHistoryResultLandingAfterARemoval_LeavesNoEnvelopeForTheRemovedPen()
	{
		var (viewModel, scheduler, _, provider) = CreateViewModel();
		var catalogue = provider.Pens;
		viewModel.ApplyCatalogue(catalogue);
		var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		using var watch = viewModel.HistoryApplied.Subscribe(_ => applied.TrySetResult());

		provider.GatedLayer = AggregationLayer.Raw;
		viewModel.RequestInitialHistory();
		scheduler.AdvanceBy(HistoryDebounceWindow.Ticks + 1);
		viewModel.ApplyCatalogue([catalogue[0]]);

		provider.HistoryGate.SetResult(Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
		[
			new PenHistoryEnvelope(1, [_from, _to], [1.0, 2.0], [1.0, 2.0], [1.0, 2.0]),
			new PenHistoryEnvelope(2, [_from, _to], [9.0, 9.0], [9.0, 9.0], [9.0, 9.0])
		]));
		await applied.Task.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);

		viewModel.FindPen(2).Should().BeNull();

		provider.GatedLayer = null;
		provider.FailHistory = true;
		viewModel.ApplyCatalogue(catalogue);
		viewModel.MoveCursor(_to);

		viewModel.CursorValues.Should().ContainKey(1);
		viewModel.CursorValues.Should().NotContainKey(2, "pen 2 has read no history since it came back");
	}

	[AvaloniaFact]
	public void ADeltaAdding500Pens_ComputesTheAxisModelOnce()
	{
		var (viewModel, _, _, _) = CreateViewModel();
		IReadOnlyList<Pen> catalogue =
			[.. Enumerable.Range(1, 500).Select(id => new Pen(id, $"Pen {id}", ["Group A"], "#ff0000"))];
		var revisionBefore = viewModel.ScalesRevision;

		viewModel.ApplyCatalogue(catalogue);

		viewModel.Pens.Should().HaveCount(500);
		viewModel.ScalesRevision.Should().Be(revisionBefore + 1);
	}
}
