using System.Reactive.Concurrency;
using System.Reactive.Linq;
using System.Reactive.Threading.Tasks;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHistoryRequestDebouncerTests
{
	private static readonly TimeSpan _debounceWindow = TimeSpan.FromMilliseconds(150);
	private static readonly TimeSpan _capInterval = TimeSpan.FromMilliseconds(400);
	private static readonly TimeSpan _testDeadline = TimeSpan.FromSeconds(10.0);
	private static readonly DateTime _from = new(2026, 6, 15, 8, 0, 0, DateTimeKind.Utc);
	private static readonly DateTime _to = new(2026, 6, 15, 9, 0, 0, DateTimeKind.Utc);

	[Fact]
	public void RapidRequests_CollapseToOneTrailingQuery()
	{
		// The gesture runs 100 ms, shorter than the cap interval, so no sample tick falls inside it.
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
			_capInterval,
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

	// The emission head runs on the TestScheduler; the hand-over runs on the thread Rx resumes the released
	// task on, which is what the gate below waits for.
	[Fact]
	public async Task ASlowQueryLandsAndOnlyTheNewestPendingWindowRunsAfterIt()
	{
		var scheduler = new TestScheduler();
		var firstQueryGate = new TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>>();
		var lastApplied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var queriedLayers = new List<AggregationLayer>();
		var appliedLayers = new List<AggregationLayer>();

		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queriedLayers.Add(request.Layer);

				return request.Layer == AggregationLayer.Minute
					? firstQueryGate.Task
					: Task.FromResult(Ok(request));
			},
			(request, _) =>
			{
				appliedLayers.Add(request.Layer);
				if (request.Layer == AggregationLayer.Day)
				{
					lastApplied.TrySetResult();
				}
			},
			_ => { },
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Minute));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		// Two more windows arrive while that read is held; only the newer of them is worth reading.
		debouncer.Request(RequestForLayer(AggregationLayer.Hour));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);
		debouncer.Request(RequestForLayer(AggregationLayer.Day));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queriedLayers.Should().Equal(AggregationLayer.Minute);

		firstQueryGate.SetResult(Ok(RequestForLayer(AggregationLayer.Minute)));
		await lastApplied.Task.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);

		queriedLayers.Should().Equal(AggregationLayer.Minute, AggregationLayer.Day);
		appliedLayers.Should().Equal(AggregationLayer.Minute, AggregationLayer.Day);
	}

	[Fact]
	public void ThrowingQuery_IsReportedAndDropped_WithoutKillingTheStream()
	{
		var scheduler = new TestScheduler();
		var reportedFailures = new List<IReadOnlyList<IError>>();
		var appliedLayers = new List<AggregationLayer>();
		using var debouncer = new ChartHistoryRequestDebouncer(
			_ => throw new InvalidOperationException("query failed"),
			(request, _) => appliedLayers.Add(request.Layer),
			reportedFailures.Add,
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		debouncer.Request(RequestForLayer(AggregationLayer.Hour));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		reportedFailures.Should().HaveCount(2);
		appliedLayers.Should().BeEmpty();

		// The thrown exception reaches the consumer, which is what a log line needs to be worth reading.
		reportedFailures[0].OfType<ExceptionalError>().Should()
			.ContainSingle().Which.Exception.Should().BeOfType<InvalidOperationException>();
	}

	// The provider's own failure channel is a failed Result, not a throw, and the consumer has to hear about
	// it too: without this the chart holds an axis it can never leave and nothing says why.
	[Fact]
	public void AFailedResult_IsReportedToTheConsumer()
	{
		var scheduler = new TestScheduler();
		var reportedFailures = new List<IReadOnlyList<IError>>();
		var appliedLayers = new List<AggregationLayer>();
		using var debouncer = new ChartHistoryRequestDebouncer(
			_ => Task.FromResult(
				Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("The archive is unreachable.")),
			(request, _) => appliedLayers.Add(request.Layer),
			reportedFailures.Add,
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		reportedFailures.Should().ContainSingle();
		reportedFailures[0].Should().ContainSingle()
			.Which.Message.Should().Be("The archive is unreachable.");
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
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestOver([4, 7], _from, _to, AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		appliedPenIds.Should().Equal(4, 7);
	}

	[Fact]
	public void AContinuousGestureFetchesAtTheCapInterval()
	{
		// The drag pushes a request every 20 ms for 2 s, each over its own window. Sample ticks at 400, 800,
		// 1200, 1600 and 2000 ms with a fresh request behind every tick, so the gesture issues
		// floor(2000 / 400) = 5 queries, and the throttle issues the notch no tick saw once it stops: 6.

		// Every read here completes on the tick that starts it, so the one query slot is free at each of
		// them; a read that outlived a tick would move its query behind the one in flight, never add one.
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
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		for (var notch = 1; notch <= 100; notch++)
		{
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
			debouncer.Request(RequestAtNotch(notch));
		}

		queryCount.Should().Be(5);

		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(6);
	}

	[Fact]
	public void TheSameWindowRequestedTwiceQueriesOnce()
	{
		// The trailing throttle emits the window and its result is applied; the sample tick that follows
		// carries the same window, which the applied envelopes already cover, so the archive is read once.
		var scheduler = new TestScheduler();
		var queryCount = 0;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queryCount++;

				return Task.FromResult(Ok(request));
			},
			(_, _) => { },
			_ => { },
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(200).Ticks);
		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_capInterval.Ticks + 1);

		queryCount.Should().Be(1);
	}

	// A read that failed covers nothing, so the same window asked for again has to reach the archive: the
	// duplicate drop is about the two heads emitting together, never about a query that came back empty.
	[Fact]
	public void TheSameWindowRequestedAgainAfterAFailedQueryIsRead()
	{
		var scheduler = new TestScheduler();
		var queryCount = 0;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queryCount++;

				return Task.FromResult(queryCount == 1
					? Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("The archive is unreachable.")
					: Ok(request));
			},
			(_, _) => { },
			_ => { },
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(1);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(2);
	}

	// The counterpart of AnIdenticalWindowAskedForWhileAFailingQueryIsInFlightIsStillRead, on the success
	// path: the window waiting behind a query can be the one that query applies, and reading it again would
	// spend a query on rows already drawn.
	[Fact]
	public async Task AnIdenticalWindowAskedForWhileTheQueryIsInFlightIsDroppedWhenItLands()
	{
		var scheduler = new TestScheduler();
		var queryGate = new TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>>();
		var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var queryCount = 0;

		using var debouncer = new ChartHistoryRequestDebouncer(
			request =>
			{
				queryCount++;

				return queryCount == 1 ? queryGate.Task : Task.FromResult(Ok(request));
			},
			(_, _) => applied.TrySetResult(),
			_ => { },
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);
		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		queryCount.Should().Be(1);

		queryGate.SetResult(Ok(RequestForLayer(AggregationLayer.Raw)));
		await applied.Task.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);

		queryCount.Should().Be(1);
	}

	// The failing case the applied-window record exists for: a window asked for again while its first read is
	// still in flight must not be silenced by that read, because the read can come back with nothing.
	[Fact]
	public async Task AnIdenticalWindowAskedForWhileAFailingQueryIsInFlightIsStillRead()
	{
		var firstQueryGate = new TaskCompletionSource();
		var firstQueryStarted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var applied = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);
		var queryCount = 0;
		var shortWindow = TimeSpan.FromMilliseconds(20);

		using var debouncer = new ChartHistoryRequestDebouncer(
			async request =>
			{
				if (Interlocked.Increment(ref queryCount) > 1)
				{
					return Ok(request);
				}

				firstQueryStarted.TrySetResult();
				await firstQueryGate.Task;

				return Result.Fail<IReadOnlyList<PenHistoryEnvelope>>("The archive is unreachable.");
			},
			(_, _) => applied.TrySetResult(),
			_ => { },
			shortWindow,
			_capInterval,
			DefaultScheduler.Instance,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		await firstQueryStarted.Task.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		firstQueryGate.SetResult();

		await applied.Task.WaitAsync(_testDeadline, TestContext.Current.CancellationToken);
		queryCount.Should().BeGreaterThanOrEqualTo(2);
	}

	// Without the guard the throw tears down the one subscription that issues every history query, and the
	// chart stops reading the archive for the rest of the session.
	[Fact]
	public void AThrowingApplyIsReportedAndTheNextWindowIsStillApplied()
	{
		var scheduler = new TestScheduler();
		var reportedFailures = new List<IReadOnlyList<IError>>();
		var appliedWindows = new List<DateTime>();
		var applyCount = 0;
		using var debouncer = new ChartHistoryRequestDebouncer(
			request => Task.FromResult(Ok(request)),
			(request, _) =>
			{
				applyCount++;

				if (applyCount == 1)
				{
					throw new InvalidOperationException("The axis model rejected the envelope.");
				}

				appliedWindows.Add(request.FromUtc);
			},
			reportedFailures.Add,
			_debounceWindow,
			_capInterval,
			scheduler,
			ImmediateScheduler.Instance);

		debouncer.Request(RequestAtNotch(1));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		reportedFailures.Should().ContainSingle();
		reportedFailures[0].Should().ContainSingle()
			.Which.Message.Should().Be("The axis model rejected the envelope.");

		debouncer.Request(RequestAtNotch(2));
		scheduler.AdvanceBy(_debounceWindow.Ticks + 1);

		appliedWindows.Should().Equal(_from.AddSeconds(2));
	}

	private static Result<IReadOnlyList<PenHistoryEnvelope>> Ok(HistoryRequest request)
	{
		return Result.Ok<IReadOnlyList<PenHistoryEnvelope>>(
			[new PenHistoryEnvelope(request.PenIds[0], [request.FromUtc], [0.0], [0.0], [0.0])]);
	}

	private static HistoryRequest RequestForLayer(AggregationLayer layer)
	{
		return RequestOver([1], _from, _to, layer);
	}

	private static HistoryRequest RequestAtNotch(int notch)
	{
		return RequestOver([1], _from.AddSeconds(notch), _to.AddSeconds(notch), AggregationLayer.Raw);
	}

	private static HistoryRequest RequestOver(
		IReadOnlyList<int> penIds,
		DateTime fromUtc,
		DateTime toUtc,
		AggregationLayer layer)
	{
		return new HistoryRequest(
			penIds,
			new FetchRange(fromUtc, toUtc, layer, HistoryColumnTarget.MaxColumns, toUtc - fromUtc),
			HistoryColumnTarget.MaxColumns);
	}
}
