using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHistoryRequestDebouncerTests
{
	private static readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void RapidRequests_CollapseToOneTrailingQuery()
	{
		var scheduler = new TestScheduler();
		var queryCount = 0;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queryCount++;

				return Observable
					.Return(Ok(request))
					.ToTask();
			},
			(_, _) => { },
			_ => { },
			_debounceWindow,
			scheduler,
			ImmediateScheduler.Instance);

		for (var notch = 0; notch < 5; notch++)
		{
			debouncer.Request(RequestForLayer(AggregationLayer.Raw));
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		}

		queryCount.Should().Be(0);

		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(1);
	}

	[Fact]
	public async Task StaleResponse_IsDroppedWhenANewerWindowSupersedesAnInFlightQuery()
	{
		// A slow first query is in flight when a newer window arrives; Switch must drop its late response
		// (latest-wins). Real schedulers are used because the guard spans a real async query boundary.
		// The two gates the test body awaits run their continuations asynchronously, so completing one
		// from inside a debouncer callback cannot resume this method inline on the callback's thread and
		// re-enter the Rx pipeline from within its own notification.
		var firstQueryGate = new TaskCompletionSource();
		var firstQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var applied = new List<AggregationLayer>();
		var secondApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var shortWindow = TimeSpan.FromMilliseconds(20);

		using var debouncer = new ChartHistoryRequestDebouncer(
			async request =>
			{
				if (request.Layer == AggregationLayer.Minute)
				{
					firstQueryStarted.TrySetResult();
					await firstQueryGate.Task;
				}

				return Ok(request);
			},
			(request, _) =>
			{
				applied.Add(request.Layer);
				if (request.Layer == AggregationLayer.Hour)
				{
					secondApplied.TrySetResult();
				}
			},
			_ => { },
			shortWindow,
			DefaultScheduler.Instance,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Minute));
		await firstQueryStarted.Task;

		debouncer.Request(RequestForLayer(AggregationLayer.Hour));
		await secondApplied.Task;

		// Release the stale first query after the newer one was applied; Switch must drop its result.
		firstQueryGate.SetResult();
		await Task.Delay(50, TestContext.Current.CancellationToken);

		applied.Should().ContainSingle();
		applied[0].Should().Be(AggregationLayer.Hour);
	}

	[Fact]
	public void ThrowingQuery_IsReportedAndDropped_WithoutKillingTheStream()
	{
		var scheduler = new TestScheduler();
		var reportedFailures = new List<Exception>();
		var appliedLayers = new List<AggregationLayer>();
		using var debouncer = new ChartHistoryRequestDebouncer(
			_ => throw new InvalidOperationException("query failed"),
			(request, _) => appliedLayers.Add(request.Layer),
			reportedFailures.Add,
			_debounceWindow,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		debouncer.Request(RequestForLayer(AggregationLayer.Hour));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		reportedFailures.Should().HaveCount(2);
		appliedLayers.Should().BeEmpty();
	}

	[Fact]
	public void AppliedResult_CarriesTheIdentifiersItsRequestAskedFor()
	{
		// Without them the consumer cannot tell a pen the provider omitted from one it was never asked for.
		var scheduler = new TestScheduler();
		IReadOnlyList<int>? appliedPenIds = null;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request => Task.FromResult(Ok(request)),
			(request, _) => appliedPenIds = request.PenIds,
			_ => { },
			_debounceWindow,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(new HistoryRequest(
			[4, 7], _from, _to, AggregationLayer.Raw, HistoryColumnTarget.MaxColumns));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		appliedPenIds.Should().Equal(4, 7);
	}

	private static Result<IReadOnlyList<PenHistoryEnvelope>> Ok(HistoryRequest request)
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[new PenHistoryEnvelope(request.PenIds[0], [request.FromUtc], [0.0], [0.0], [0.0])]);
	}

	private static HistoryRequest RequestForLayer(AggregationLayer layer)
	{
		return new HistoryRequest([1], _from, _to, layer, HistoryColumnTarget.MaxColumns);
	}
}
