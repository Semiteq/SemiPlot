using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Bridge;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class TrendCoordinatorTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void RealtimeBatches_EmitOnePerBufferWindow()
	{
		var (coordinator, scheduler, _) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(_batchWindow.Ticks);
		batches.Should().HaveCount(1);

		scheduler.AdvanceBy(_batchWindow.Ticks);
		batches.Should().HaveCount(2);
	}

	[Fact]
	public void RealtimeBatch_CarriesOneEntryPerPen()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(_batchWindow.Ticks);

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
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		provider.StaggerRealtimeTimestamps = true;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(_batchWindow.Ticks);

		var batch = batches.Single();
		batch.Timestamps.Should().HaveCount(batch.Pens.Sum(values => values.TimestampsUtc.Count));
		batch.Pens.Should().OnlyContain(values => values.TimestampsUtc.Count * 2 == batch.Timestamps.Count);
		batch.Pens.SelectMany(values => values.TimestampsUtc).Should().OnlyHaveUniqueItems();
	}

	[Fact]
	public async Task QueryHistoryAsync_PassesThroughToProviderWithTargetColumnCountAndToken()
	{
		var (coordinator, _, provider) = CreateCoordinator();
		using var cancellation = new CancellationTokenSource();

		var result = await coordinator.QueryHistoryAsync(
			[1, 2],
			_from,
			_to,
			AggregationLayer.Minute,
			256,
			cancellation.Token);

		result.IsSuccess.Should().BeTrue();
		result.Value.Select(pen => pen.PenId).Should().Equal(1, 2);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedTargetColumnCount.Should().Be(256);
		provider.LastQueriedCancellationToken.Should().Be(cancellation.Token);
	}

	[Fact]
	public void RealtimeBatches_WithAnEmptyCatalog_EmitNothing()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10));
		using var coordinator = new TrendCoordinator(
			provider,
			[],
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(_batchWindow.Ticks * 5);

		batches.Should().BeEmpty();
	}

	// The UI scheduler is the coordinator's own, not the provider's, so a state pushed from the data side is
	// held until the UI scheduler runs it. A view model binding to this stream therefore never has to marshal.
	[Fact]
	public void ConnectionFaults_ReachTheConsumerOnTheUiScheduler()
	{
		var uiScheduler = new TestScheduler();
		var (coordinator, _, provider) = CreateCoordinator(uiScheduler: uiScheduler);
		using var ownedCoordinator = coordinator;
		var states = new List<ArchiveConnectionState>();
		using var subscription = coordinator.ConnectionFaults.Subscribe(states.Add);

		provider.ReportConnectionState(ArchiveConnectionState.Connected);
		states.Should().BeEmpty();

		uiScheduler.AdvanceBy(1);

		states.Should().Equal(ArchiveConnectionState.Connected);
	}

	[Fact]
	public void ConnectionFaults_CarryTheProvidersFault()
	{
		var uiScheduler = new TestScheduler();
		var (coordinator, _, provider) = CreateCoordinator(uiScheduler: uiScheduler);
		using var ownedCoordinator = coordinator;
		var states = new List<ArchiveConnectionState>();
		using var subscription = coordinator.ConnectionFaults.Subscribe(states.Add);

		provider.ReportConnectionState(
			new ArchiveConnectionState(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3")));
		uiScheduler.AdvanceBy(1);

		states.Should().ContainSingle().Which.IsConnected.Should().BeFalse();
		states[0].Fault!.Kind.Should().Be(ArchiveFault.ConnectionLost);
		states[0].Fault!.Detail.Should().Be("3");
	}

	[Fact]
	public void Dispose_StopsForwardingTheProvidersConnectionState()
	{
		var uiScheduler = new TestScheduler();
		var (coordinator, _, provider) = CreateCoordinator(uiScheduler: uiScheduler);
		var states = new List<ArchiveConnectionState>();
		using var subscription = coordinator.ConnectionFaults.Subscribe(states.Add);

		coordinator.Dispose();
		provider.ReportConnectionState(ArchiveConnectionState.Connected);
		uiScheduler.AdvanceBy(1);

		states.Should().BeEmpty();
	}

	// The two streams are independent: a fault must not stand in for a batch, and a chart holding history
	// keeps drawing it while the banner is up.
	[Fact]
	public void AConnectionFault_LeavesTheRealtimeBatchesUntouched()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		using var ownedCoordinator = coordinator;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		scheduler.AdvanceBy(_batchWindow.Ticks);

		provider.ReportConnectionState(
			new ArchiveConnectionState(new ArchiveError(ArchiveFault.ConnectionLost, "bench", 5432, "semiplot_dev", "3")));

		batches.Should().HaveCount(1);

		scheduler.AdvanceBy(_batchWindow.Ticks);

		batches.Should().HaveCount(2);
	}

	// The provider ending the stream reaches every batch observer: with no onError Rx rethrows it out of the
	// advance below and takes the emitting thread down.
	[Fact]
	public void AFaultingRealtimeStream_ReachesTheFailureChannelInsteadOfTheScheduler()
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, TimeSpan.FromMilliseconds(10))
		{
			RealtimeStreamFailure = new InvalidOperationException("the provider ended the live edge")
		};
		using var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);
		var failures = new List<Exception>();
		using var reported = coordinator.RealtimeFailures.Subscribe(failures.Add);
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(_ => { });

		var advance = () => scheduler.AdvanceBy(_batchWindow.Ticks * 2);

		advance.Should().NotThrow();
		failures.Should().ContainSingle()
			.Which.Message.Should().Be("the provider ended the live edge");
	}

	[Fact]
	public void Dispose_CompletesTheConnectionFaults()
	{
		var (coordinator, _, _) = CreateCoordinator();
		var isCompleted = false;
		using var subscription = coordinator.ConnectionFaults.Subscribe(_ => { }, () => isCompleted = true);

		coordinator.Dispose();

		isCompleted.Should().BeTrue("a consumer of the stream learns that the window is gone");
	}

	// A window failing while disposal runs: OnNext on a disposed subject throws, and Rx turns that into the
	// OnError the catch inside TryBuildRealtimeBatch exists to prevent.
	[Fact]
	public void AFailedWindowAfterDisposal_LeavesTheStreamAlive()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		var failures = new List<Exception>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add, failures.Add);
		scheduler.AdvanceBy(_batchWindow.Ticks);
		coordinator.Dispose();
		provider.PoisonRealtimeWindow = true;

		var advance = () => scheduler.AdvanceBy(_batchWindow.Ticks * 3);

		advance.Should().NotThrow();
		failures.Should().BeEmpty("the subject is completed at disposal, not disposed");
	}

	[Fact]
	public void SetPens_WithAnAddedId_ReachesTheOneSubscriberOfTheLiveEdge()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		using var ownedCoordinator = coordinator;
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);
		scheduler.AdvanceBy(_batchWindow.Ticks);
		batches.Should().ContainSingle().Which.Pens.Select(values => values.PenId).Should().Equal(1, 2);

		provider.Pens = [.. provider.Pens, new Pen(3, "Pen 3", ["Group B"], "#0000ff")];
		coordinator.SetPens([1, 2, 3]);
		scheduler.AdvanceBy(_batchWindow.Ticks);

		batches.Should().HaveCount(2);
		batches[1].Pens.Select(values => values.PenId).Should().Equal(1, 2, 3);
	}

	[Fact]
	public void SetPens_LeavesTheProviderHoldingExactlyOneLiveSubscription()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		using var ownedCoordinator = coordinator;
		using var subscription = coordinator.RealtimeBatches.Subscribe(_ => { });
		scheduler.AdvanceBy(_batchWindow.Ticks);
		provider.OpenLiveSubscriptionCount.Should().Be(1);

		coordinator.SetPens([1]);
		scheduler.AdvanceBy(_batchWindow.Ticks);
		coordinator.SetPens([1, 2]);
		scheduler.AdvanceBy(_batchWindow.Ticks);

		provider.OpenLiveSubscriptionCount.Should().Be(1);
	}

	[Fact]
	public void AProviderFailureAfterASwitch_ReachesRealtimeFailuresOnce()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		using var ownedCoordinator = coordinator;
		var failures = new List<Exception>();
		using var reported = coordinator.RealtimeFailures.Subscribe(failures.Add);
		using var liveEdge = coordinator.RealtimeBatches.Subscribe(_ => { });
		scheduler.AdvanceBy(_batchWindow.Ticks);

		var streamFailure = new InvalidOperationException("the provider ended the switched live edge");
		provider.RealtimeStreamFailure = streamFailure;
		coordinator.SetPens([1]);
		var advance = () => scheduler.AdvanceBy(_batchWindow.Ticks * 3);

		advance.Should().NotThrow();
		failures.Should().ContainSingle().Which.Should().BeSameAs(streamFailure);
	}

	private static (TrendCoordinator Coordinator, TestScheduler Scheduler, FakeDataProvider Provider)
		CreateCoordinator(TimeSpan? realtimeInterval = null, IScheduler? uiScheduler = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			provider.Pens,
			scheduler,
			uiScheduler ?? ImmediateScheduler.Instance,
			_batchWindow);

		return (coordinator, scheduler, provider);
	}
}
