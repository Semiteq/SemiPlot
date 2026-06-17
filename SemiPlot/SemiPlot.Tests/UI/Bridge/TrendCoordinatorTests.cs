using System.Reactive.Concurrency;

using AwesomeAssertions;

using Microsoft.Extensions.Logging.Abstractions;
using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Bridge;

using Xunit;

namespace SemiPlot.Tests.UI.Bridge;

[Trait("Component", "UI")]
[Trait("Area", "Bridge")]
[Trait("Category", "Unit")]
public sealed class TrendCoordinatorTests
{
	private static readonly TimeSpan _batchWindow = TimeSpan.FromMilliseconds(33);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void Pens_ExposesTheProviderCatalog()
	{
		var (coordinator, _, provider) = CreateCoordinator();

		coordinator.Pens.Should().BeSameAs(provider.Pens);
	}

	[Fact]
	public void Start_EmitsOneRealtimeBatchPerBufferWindow()
	{
		var (coordinator, scheduler, _) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		coordinator.Start();

		scheduler.AdvanceBy(_batchWindow.Ticks);
		batches.Should().HaveCount(1);

		scheduler.AdvanceBy(_batchWindow.Ticks);
		batches.Should().HaveCount(2);
	}

	[Fact]
	public void RealtimeBatch_IsColumnar_WithOneColumnPerPen()
	{
		var (coordinator, scheduler, provider) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);

		coordinator.Start();
		scheduler.AdvanceBy(_batchWindow.Ticks);

		var batch = batches.Single();
		batch.Pens.Should().HaveCount(provider.Pens.Count);
		batch.Pens.Select(column => column.PenId).Should()
			.BeEquivalentTo(provider.Pens.Select(pen => pen.ProjectVarId));
		batch.Pens.Should().OnlyContain(column => column.Values.Count == batch.Timestamps.Count);
	}

	[Fact]
	public void RequestHistory_QueriesProviderAndEmitsHistory()
	{
		var (coordinator, _, provider) = CreateCoordinator();
		var histories = new List<TrendHistory>();
		using var subscription = coordinator.HistoryResults.Subscribe(histories.Add);

		coordinator.RequestHistory([1, 2], _from, _to);

		provider.HistoryQueryCount.Should().Be(1);
		provider.LastQueriedPenIds.Should().Equal(1, 2);
		histories.Should().ContainSingle();
		histories[0].Pens.Select(pen => pen.PenId).Should().Equal(1, 2);
	}

	[Fact]
	public void SetLayer_RequeriesLastWindowAtNewLayerAndEmitsFreshHistory()
	{
		var (coordinator, _, provider) = CreateCoordinator();
		var histories = new List<TrendHistory>();
		using var subscription = coordinator.HistoryResults.Subscribe(histories.Add);
		coordinator.RequestHistory([1, 2], _from, _to);

		coordinator.SetLayer(AggregationLayer.Hour);

		provider.HistoryQueryCount.Should().Be(2);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Hour);
		provider.LastQueriedPenIds.Should().Equal(1, 2);
		provider.LastQueriedFromUtc.Should().Be(_from);
		provider.LastQueriedToUtc.Should().Be(_to);

		histories.Should().HaveCount(2);
		histories[^1].Layer.Should().Be(AggregationLayer.Hour);
	}

	[Fact]
	public void SetLayer_WithoutPriorHistoryRequest_DoesNotQuery()
	{
		var (coordinator, _, provider) = CreateCoordinator();

		coordinator.SetLayer(AggregationLayer.Day);

		provider.HistoryQueryCount.Should().Be(0);
	}

	[Fact]
	public void RequestHistory_ProviderFailure_DoesNotCrashAndEmitsNoHistory()
	{
		var (coordinator, _, provider) = CreateCoordinator();
		provider.FailHistory = true;
		var histories = new List<TrendHistory>();
		using var subscription = coordinator.HistoryResults.Subscribe(histories.Add);

		var act = () => coordinator.RequestHistory([1], _from, _to);

		act.Should().NotThrow();
		histories.Should().BeEmpty();
	}

	[Fact]
	public async Task QueryHistoryAsync_PassesThroughToProviderWithTargetColumnCount()
	{
		var (coordinator, _, provider) = CreateCoordinator();

		var result = await coordinator.QueryHistoryAsync([1, 2], _from, _to, AggregationLayer.Minute, 256);

		result.IsSuccess.Should().BeTrue();
		result.Value.Select(pen => pen.PenId).Should().Equal(1, 2);
		provider.LastQueriedLayer.Should().Be(AggregationLayer.Minute);
		provider.LastQueriedTargetColumnCount.Should().Be(256);
	}

	[Fact]
	public void Dispose_DropsTheRealtimeKeepAliveSubscription()
	{
		var (coordinator, scheduler, _) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		var batches = new List<RealtimeBatch>();
		coordinator.Start();
		using var subscription = coordinator.RealtimeBatches.Subscribe(batches.Add);
		scheduler.AdvanceBy(_batchWindow.Ticks);
		var countBeforeDispose = batches.Count;
		countBeforeDispose.Should().BeGreaterThan(0);

		coordinator.Dispose();
		subscription.Dispose();
		scheduler.AdvanceBy(_batchWindow.Ticks * 5);

		batches.Should().HaveCount(countBeforeDispose);
	}

	[Fact]
	public void Dispose_PreventsRestartingRealtimeViaStart()
	{
		var (coordinator, _, _) = CreateCoordinator(realtimeInterval: TimeSpan.FromMilliseconds(10));
		coordinator.Dispose();

		var act = coordinator.Start;

		act.Should().Throw<ObjectDisposedException>();
	}

	private static (TrendCoordinator Coordinator, TestScheduler Scheduler, FakeDataProvider Provider)
		CreateCoordinator(TimeSpan? realtimeInterval = null)
	{
		var scheduler = new TestScheduler();
		var provider = new FakeDataProvider(scheduler, realtimeInterval ?? TimeSpan.FromMilliseconds(10));
		var coordinator = new TrendCoordinator(
			provider,
			NullLogger<TrendCoordinator>.Instance,
			scheduler,
			ImmediateScheduler.Instance,
			_batchWindow);

		return (coordinator, scheduler, provider);
	}
}
