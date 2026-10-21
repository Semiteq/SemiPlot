using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tests.Unit.UI.Chart;
using SemiPlot.UI.Bridge;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Bridge;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class TrendCoordinatorTests
{
	private const string OpcEndpoint = "opc.tcp://127.0.0.1:4840";

	[Fact]
	public void RealtimeBatches_EmitOnePerBufferWindow()
	{
		var (coordinator, scheduler, _) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		batches.Should().HaveCount(1);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		batches.Should().HaveCount(2);
	}

	[Fact]
	public void RealtimeBatch_CarriesOneEntryPerPen()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		var batch = batches.Single();
		batch.Pens.Should().HaveCount(provider.Pens.Count);
		batch.Pens.Select(values => values.PenId).Should()
			.BeEquivalentTo(provider.Pens.Select(pen => pen.PenId));
		batch.Pens.Should().OnlyContain(values => values.Values.Count == values.TimestampsUtc.Count);
	}

	// The archive is per-variable and change-based, so two pens rarely share a t. Each pen carries the
	// samples it has and no filler at the timestamps only the other pen sampled: the batch's shared
	// timestamp list is the union of the two, and is only ever read for the live edge.
	[Fact]
	public void RealtimeBatch_KeepsEachPenOnItsOwnTimestamps()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		provider.StaggerRealtimeTimestamps = true;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		var batch = batches.Single();
		batch.Timestamps.Should().HaveCount(batch.Pens.Sum(values => values.TimestampsUtc.Count));
		batch.Pens.Should().OnlyContain(values => values.TimestampsUtc.Count * 2 == batch.Timestamps.Count);
		batch.Pens.SelectMany(values => values.TimestampsUtc).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public void ABreakTheLiveSourceReports_SurvivesTheBufferWindowOnItsOwnTimestamp()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		using var ownedCoordinator = coordinator;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		provider.BreakRealtimePenOnce(FakeDataProvider.FirstPen.PenId);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		var batch = batches.Single();
		var (_, _, breakUtc) = FakeDataProvider.TheBreakIn([batch], FakeDataProvider.FirstPen.PenId);
		batch.Timestamps.Should().Contain(breakUtc);
		batch.Pens.Should().OnlyContain(values => values.Values.Count == values.TimestampsUtc.Count);
		batch.Pens.Single(values => values.PenId == FakeDataProvider.SecondPen.PenId).Values.Should().NotContainNulls();

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		batches.Should().HaveCount(2);
		batches[1].Pens.Should().AllSatisfy(values => values.Values.Should().NotContainNulls());
	}

	[Fact]
	public void RealtimeBatches_WithAnEmptyCatalog_EmitNothing()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, ChartTestBuilder.RealtimeInterval);
		using var coordinator = new TrendCoordinator(
			provider,
			provider,
			[],
			scheduler,
			ImmediateScheduler.Instance);
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 5);

		batches.Should().BeEmpty();
	}

	// A live source that faults reaches every batch observer: with no onError Rx rethrows the fault out of the
	// advance below and takes the emitting thread down.
	[Fact]
	public void AFaultingRealtimeStream_ReachesTheFailureChannelInsteadOfTheScheduler()
	{
		var scheduler = new TestScheduler();
		var thrown = new InvalidOperationException("the live source ended the live edge");
		var provider = new FakeDataProvider(scheduler, ChartTestBuilder.RealtimeInterval)
		{
			RealtimeStreamFailure = thrown
		};
		using var coordinator = new TrendCoordinator(
			provider,
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance);
		var failures = new List<Exception>();
		using var reported = coordinator.RealtimeFailures.Subscribe(failures.Add);
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(_ => { });

		var advance = () => scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 2);

		advance.Should().NotThrow();
		failures.Should().ContainSingle().Which.Should().BeSameAs(thrown);
	}

	// A window failing while disposal runs: OnNext on a disposed subject throws, and Rx turns that into the
	// OnError the catch inside TryBuildRealtimeBatch exists to prevent.
	[Fact]
	public void AFailedWindowAfterDisposal_LeavesTheStreamAlive()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		var batches = new List<RealtimeBatch>();
		var failures = new List<Exception>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add, failures.Add);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		coordinator.Dispose();
		provider.PoisonRealtimeWindow = true;

		var advance = () => scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);

		advance.Should().NotThrow();
		failures.Should().BeEmpty("the subject is completed at disposal, not disposed");
	}

	[Fact]
	public void SetPens_WithAnAddedId_ReachesTheOneSubscriberOfTheLiveEdge()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		using var ownedCoordinator = coordinator;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		batches.Should().ContainSingle().Which.Pens.Select(values => values.PenId).Should().Equal(1, 2);

		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];
		coordinator.SetPens([1, 2, 3]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		batches.Should().HaveCount(2);
		batches[1].Pens.Select(values => values.PenId).Should().Equal(1, 2, 3);
	}

	[Fact]
	public void SetPens_LeavesTheLiveSourceHoldingExactlyOneLiveSubscription()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		using var ownedCoordinator = coordinator;
		using var subscription = coordinator.RealtimeBatches.Subscribe(_ => { });
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		provider.OpenLiveSubscriptionCount.Should().Be(1);

		coordinator.SetPens([1]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);
		coordinator.SetPens([1, 2]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		provider.OpenLiveSubscriptionCount.Should().Be(1);
	}

	[Fact]
	public void ALiveSourceFailureAfterASwitch_ReachesRealtimeFailuresOnce()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		using var ownedCoordinator = coordinator;
		var failures = new List<Exception>();
		using var reported = coordinator.RealtimeFailures.Subscribe(failures.Add);
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(_ => { });
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		var streamFailure = new InvalidOperationException("the live source ended the switched live edge");
		provider.RealtimeStreamFailure = streamFailure;
		coordinator.SetPens([1]);
		var advance = () => scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 3);

		advance.Should().NotThrow();
		failures.Should().ContainSingle().Which.Should().BeSameAs(streamFailure);
	}

	[Fact]
	public async Task TheLiveEdgeFollowsTheLiveSource_AndTheReadsGoToTheProvider()
	{
		const int ChartColumnCount = 100;
		var scheduler = new TestScheduler();
		var dataProvider = new FakeDataProvider(scheduler, ChartTestBuilder.RealtimeInterval);
		var liveSource = new FakeDataProvider(
			scheduler,
			ChartTestBuilder.RealtimeInterval,
			[new Pen(3, "Pen 3", ["Group B"], "#0000ff")]);
		using var coordinator = new TrendCoordinator(
			dataProvider,
			liveSource,
			liveSource.Pens,
			scheduler,
			ImmediateScheduler.Instance);
		var batches = new List<RealtimeBatch>();
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		batches.Should().ContainSingle().Which.Pens.Select(values => values.PenId).Should().Equal(3);

		await coordinator.QueryHistoryAsync(
			[FakeDataProvider.FirstPen.PenId],
			FakeDataProvider.RealtimeEpoch.AddHours(-1),
			FakeDataProvider.RealtimeEpoch,
			AggregationLayer.Raw,
			ChartColumnCount,
			TestContext.Current.CancellationToken);
		await coordinator.QueryArchiveExtentAsync();

		liveSource.LiveSubscriptionsOpened.Should().Be(1);
		liveSource.HistoryQueryCount.Should().Be(0);
		liveSource.ExtentQueryCount.Should().Be(0);
		dataProvider.LiveSubscriptionsOpened.Should().Be(0);
		dataProvider.HistoryQueryCount.Should().Be(1);
		dataProvider.ExtentQueryCount.Should().Be(1);
	}

	// docs/architecture/data-integration.md#realtime
	[Fact]
	public void ALiveSourceThatCompletes_LeavesTheLiveEdgeSilentUntilTheNextSetPens()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: ChartTestBuilder.RealtimeInterval);
		using var ownedCoordinator = coordinator;
		var batches = new List<RealtimeBatch>();
		var failures = new List<Exception>();
		var completed = false;
		using var reported = coordinator.RealtimeFailures.Subscribe(failures.Add);
		provider.RealtimeStreamCompletes = true;
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(batches.Add, () => completed = true);

		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks * 2);

		batches.Should().BeEmpty();
		failures.Should().BeEmpty();
		completed.Should().BeFalse();
		provider.OpenLiveSubscriptionCount.Should().Be(0);

		provider.RealtimeStreamCompletes = false;
		coordinator.SetPens([FakeDataProvider.FirstPen.PenId]);
		scheduler.AdvanceBy(TrendCoordinator.BatchWindow.Ticks);

		batches.Should().ContainSingle().Which.Pens.Select(values => values.PenId).Should()
			.Equal(FakeDataProvider.FirstPen.PenId);
		failures.Should().BeEmpty();
	}

	// The source raises both streams on its own loop thread, and the bar and the panel are bound state.
	[Fact]
	public void TheLiveSourcesStatesAndPenFaults_ArriveOnTheUiScheduler()
	{
		var dataScheduler = new TestScheduler();
		var uiScheduler = new TestScheduler();
		var provider = new FakeDataProvider(dataScheduler, ChartTestBuilder.RealtimeInterval);
		using var coordinator = new TrendCoordinator(provider, provider, provider.Pens, dataScheduler, uiScheduler);
		var states = new List<LiveSourceState>();
		var faults = new List<LiveSourceError>();
		using var stateSubscription = coordinator.LiveSourceStates.Subscribe(states.Add);
		using var faultSubscription = coordinator.LivePenFaults.Subscribe(faults.Add);
		var lost = LiveSourceState.FaultedBy(new LiveSourceError(LiveSourceFault.Unreachable, OpcEndpoint));
		var unserved = new LiveSourceError(LiveSourceFault.PensUnserved, OpcEndpoint, unknownPenIds: [7]);

		states.Should().BeEmpty("the replayed state waits for the UI scheduler");

		uiScheduler.AdvanceBy(1);

		states.Should().Equal(LiveSourceState.Connected);

		provider.PushLiveState(lost);
		provider.RaisePenFault(unserved);

		states.Should().HaveCount(1);
		faults.Should().BeEmpty();

		uiScheduler.AdvanceBy(1);

		states.Should().Equal(LiveSourceState.Connected, lost);
		faults.Should().ContainSingle().Which.Should().BeSameAs(unserved);
	}

	private static (TrendCoordinator Coordinator, TestScheduler Scheduler, FakeDataProvider Provider)
		CreateCoordinator(TimeSpan? realtimeInterval = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? ChartTestBuilder.RealtimeInterval);
		var coordinator = new TrendCoordinator(
			provider,
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance);

		return (coordinator, scheduler, provider);
	}
}
