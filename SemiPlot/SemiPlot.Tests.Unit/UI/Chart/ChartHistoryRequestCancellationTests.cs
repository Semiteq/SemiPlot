using System.Collections.Concurrent;

using AwesomeAssertions;

using FluentResults;

using Microsoft.Reactive.Testing;

using SemiPlot.Core.Trends;
using SemiPlot.UI.Chart;

using Xunit;

using static SemiPlot.Tests.Unit.UI.Chart.HistoryDebouncerTestBuilder;

namespace SemiPlot.Tests.Unit.UI.Chart;

[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class ChartHistoryRequestCancellationTests
{
	// Longer than the cap interval, so a sample tick always finds the read it started still running.
	private static readonly TimeSpan _slowQuery = TimeSpan.FromMilliseconds(600);

	// Well under the cancel callback's own block, so a call that waited for it cannot pass.
	private static readonly TimeSpan _promptReturn = TimeSpan.FromSeconds(5.0);

	private const int LastDragNotch = 25;

	// A notch every 20 ms puts this one at 380 ms, the last before the first sample tick.
	private const int LastNotchBeforeTheFirstTick = 19;

	[Fact]
	public async Task APacedRequestNeverCancelsTheQueryInFlight()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync, (request, _) => applied.Enqueue(request));

		for (var notch = 1; notch <= 100; notch++)
		{
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
			debouncer.Request(RequestAtNotch(notch));

			if (queries.Running is { } running && scheduler.Clock - running.StartedAt >= _slowQuery.Ticks)
			{
				await queries.LandAndAwaitTheNextStart(running);
			}
		}

		applied.Should().HaveCount(2);
		queries.Started.Should().HaveCount(3)
			.And.OnlyContain(query => !query.Cancellation.IsCancellationRequested);
	}

	[Fact]
	public async Task AGestureEndCancelsTheLeftBehindQuery()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync, (request, _) => applied.Enqueue(request));

		DragUntilTheFirstReadIsLeftBehind(debouncer, scheduler);
		var leftBehind = queries.Started.Should().ContainSingle().Which;

		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		leftBehind.Cancellation.IsCancellationRequested.Should().BeTrue();
		await AwaitCondition(() => queries.Started.Count == 2);

		var final = queries.Started[1];
		final.Request.FromUtc.Should().Be(From.AddSeconds(LastDragNotch));
		final.Cancellation.IsCancellationRequested.Should().BeFalse();

		final.Gate.SetResult(Ok(final.Request));
		await AwaitCondition(() => !applied.IsEmpty);

		applied.Select(request => request.FromUtc).Should().Equal(From.AddSeconds(LastDragNotch));
	}

	[Fact]
	public async Task AGestureEndingOnTheWindowBeingReadLeavesTheReadRunning()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync, (request, _) => applied.Enqueue(request));

		for (var notch = 1; notch <= LastNotchBeforeTheFirstTick; notch++)
		{
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
			debouncer.Request(RequestAtNotch(notch));
		}

		scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
		var running = queries.Started.Should().ContainSingle().Which;
		running.Request.FromUtc.Should().Be(From.AddSeconds(LastNotchBeforeTheFirstTick));

		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		running.Cancellation.IsCancellationRequested.Should().BeFalse();
		queries.Started.Should().ContainSingle();

		running.Gate.SetResult(Ok(running.Request));
		await AwaitCondition(() => !applied.IsEmpty);

		applied.Select(request => request.FromUtc).Should().Equal(From.AddSeconds(LastNotchBeforeTheFirstTick));
	}

	[Fact]
	public async Task AGestureEndingOnTheDrawnWindowCancelsTheReadItLeftAndReadsNothing()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync, (request, _) => applied.Enqueue(request));

		await ApplyNotchZero(debouncer, scheduler, queries, applied);

		debouncer.Request(RequestAtNotch(1));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		var farRead = await queries.AwaitStart(1);

		debouncer.RequestNothing();
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		farRead.Cancellation.IsCancellationRequested.Should().BeTrue();

		debouncer.Request(RequestAtNotch(2));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		await AwaitCondition(() => queries.Started.Count == 3);

		queries.Started.Select(query => query.Request.FromUtc).Should()
			.Equal(From, From.AddSeconds(1), From.AddSeconds(2));
	}

	// The reads here ignore their tokens and the test ends each one, so the order the completions land in is
	// the test's own.
	[Fact]
	public async Task AGestureEndingOnTheDrawnWindowAfterACancelledReadReadsNothing()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler, endsOnCancellation: false);
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync, (request, _) => applied.Enqueue(request));

		await ApplyNotchZero(debouncer, scheduler, queries, applied);

		debouncer.Request(RequestAtNotch(1));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		debouncer.Request(RequestAtNotch(2));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		var cancelled = await queries.AwaitStart(1);
		cancelled.Cancellation.IsCancellationRequested.Should().BeTrue();
		cancelled.Gate.SetCanceled(cancelled.Cancellation);
		await AwaitCondition(() => queries.Started.Count == 3);

		debouncer.RequestNothing();
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		var running = queries.Started[2];
		running.Gate.SetResult(Ok(running.Request));
		await AwaitCondition(() => applied.Count == 2);

		debouncer.Request(RequestAtNotch(3));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		await AwaitCondition(() => queries.Started.Count == 4);

		queries.Started.Select(query => query.Request.FromUtc).Should()
			.Equal(From, From.AddSeconds(1), From.AddSeconds(2), From.AddSeconds(3));
	}

	[Fact]
	public async Task AGestureEndingOnTheDrawnWindowAfterAFailedReadCancelsTheReadItLeft()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var applied = new ConcurrentQueue<HistoryRequest>();
		var reportedFailures = new ConcurrentQueue<IReadOnlyList<IError>>();
		using var debouncer = CreateDebouncer(
			scheduler,
			queries.QueryAsync,
			(request, _) => applied.Enqueue(request),
			reportedFailures.Enqueue);

		await ApplyNotchZero(debouncer, scheduler, queries, applied);

		debouncer.Request(RequestAtNotch(1));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		(await queries.AwaitStart(1)).Gate.SetResult(Unreachable());
		await AwaitCondition(() => !reportedFailures.IsEmpty);

		debouncer.Request(RequestAtNotch(2));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		var farRead = await queries.AwaitStart(2);

		debouncer.RequestNothing();
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		farRead.Cancellation.IsCancellationRequested.Should().BeTrue();

		debouncer.Request(RequestAtNotch(3));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		await queries.AwaitStart(3);

		queries.Started.Select(query => query.Request.FromUtc).Should()
			.Equal(From, From.AddSeconds(1), From.AddSeconds(2), From.AddSeconds(3));
	}

	[Fact]
	public async Task AGestureEndNeverWaitsForTheCancelToReachTheServer()
	{
		var scheduler = new TestScheduler();
		var queries = new UnansweredCancels();
		using var debouncer = CreateDebouncer(scheduler, queries.QueryAsync);

		try
		{
			DragUntilTheFirstReadIsLeftBehind(debouncer, scheduler);
			var leftBehind = queries.Started.Should().ContainSingle().Which;

			var returned = await ReturnsPromptly(() => scheduler.AdvanceBy(DebounceWindow.Ticks + 1));

			returned.Should().BeTrue();
			leftBehind.IsCancellationRequested.Should().BeTrue();
		}
		finally
		{
			queries.Answer();
		}
	}

	[Fact]
	public async Task DisposingNeverWaitsForTheCancelToReachTheServer()
	{
		var scheduler = new TestScheduler();
		var queries = new UnansweredCancels();
		var debouncer = CreateDebouncer(scheduler, queries.QueryAsync);

		try
		{
			debouncer.Request(RequestForLayer(AggregationLayer.Raw));
			scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
			var running = queries.Started.Should().ContainSingle().Which;

			var returned = await ReturnsPromptly(debouncer.Dispose);

			returned.Should().BeTrue();
			running.IsCancellationRequested.Should().BeTrue();
		}
		finally
		{
			queries.Answer();
		}
	}

	[Fact]
	public async Task ACancelledQueryReportsNothing()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var reportedFailures = new ConcurrentQueue<IReadOnlyList<IError>>();
		var applied = new ConcurrentQueue<HistoryRequest>();
		using var debouncer = CreateDebouncer(
			scheduler,
			queries.QueryAsync,
			(request, _) => applied.Enqueue(request),
			reportedFailures.Enqueue);

		DragUntilTheFirstReadIsLeftBehind(debouncer, scheduler);
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		await AwaitCondition(() => queries.Started.Count == 2);

		queries.Started[0].Gate.Task.IsCanceled.Should().BeTrue();

		queries.Started[1].Gate.SetResult(Ok(queries.Started[1].Request));
		await AwaitCondition(() => !applied.IsEmpty);

		reportedFailures.Should().BeEmpty();
		applied.Should().ContainSingle();
	}

	[Fact]
	public void ACancellationTheDebouncerNeverAskedForIsReported()
	{
		var scheduler = new TestScheduler();
		var reportedFailures = new List<IReadOnlyList<IError>>();
		using var debouncer = CreateDebouncer(
			scheduler,
			(_, _) => Task.FromCanceled<Result<IReadOnlyList<PenHistoryEnvelope>>>(new CancellationToken(true)),
			reportQueryFailure: reportedFailures.Add);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		reportedFailures.Should().ContainSingle();
		reportedFailures[0].OfType<ExceptionalError>().Should()
			.ContainSingle().Which.Exception.Should().BeAssignableTo<OperationCanceledException>();
	}

	[Fact]
	public void DisposingCancelsTheRunningQuery()
	{
		var scheduler = new TestScheduler();
		var queries = new HeldQueries(scheduler);
		var debouncer = CreateDebouncer(scheduler, queries.QueryAsync);

		debouncer.Request(RequestForLayer(AggregationLayer.Raw));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);
		var running = queries.Started.Should().ContainSingle().Which;

		debouncer.Dispose();

		running.Cancellation.IsCancellationRequested.Should().BeTrue();
	}

	// A notch every 20 ms: the 400 ms sample tick starts the one read, and the drag stops 100 ms later on a
	// window that read does not cover.
	private static void DragUntilTheFirstReadIsLeftBehind(
		ChartHistoryRequestDebouncer debouncer,
		TestScheduler scheduler)
	{
		for (var notch = 1; notch <= LastDragNotch; notch++)
		{
			scheduler.AdvanceBy(TimeSpan.FromMilliseconds(20).Ticks);
			debouncer.Request(RequestAtNotch(notch));
		}
	}

	private static async Task ApplyNotchZero(
		ChartHistoryRequestDebouncer debouncer,
		TestScheduler scheduler,
		HeldQueries queries,
		ConcurrentQueue<HistoryRequest> applied)
	{
		debouncer.Request(RequestAtNotch(0));
		scheduler.AdvanceBy(DebounceWindow.Ticks + 1);

		var first = queries.Started.Should().ContainSingle().Which;
		first.Gate.SetResult(Ok(first.Request));

		await AwaitCondition(() => applied.Count == 1);
	}

	private static async Task<bool> ReturnsPromptly(Action action)
	{
		var call = Task.Run(action, TestContext.Current.CancellationToken);
		var promptly = Task.Delay(_promptReturn, TestContext.Current.CancellationToken);

		return await Task.WhenAny(call, promptly) == call;
	}

	private sealed record HeldQuery(
		HistoryRequest Request,
		long StartedAt,
		TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>> Gate,
		CancellationToken Cancellation);

	// A read that lands when the test sets its gate and, unless told otherwise, ends cancelled the moment its
	// token is, as the provider's does. StartedAt is the virtual clock, read while the test thread waits for
	// the start.
	private sealed class HeldQueries(TestScheduler scheduler, bool endsOnCancellation = true)
	{
		private readonly Lock _gate = new();
		private readonly List<HeldQuery> _started = [];

		public IReadOnlyList<HeldQuery> Started
		{
			get
			{
				lock (_gate)
				{
					return [.. _started];
				}
			}
		}

		public HeldQuery? Running => Started.LastOrDefault(query => !query.Gate.Task.IsCompleted);

		public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryAsync(
			HistoryRequest request,
			CancellationToken cancellation)
		{
			var query = new HeldQuery(request, scheduler.Clock, new(), cancellation);
			if (endsOnCancellation)
			{
				cancellation.Register(() => query.Gate.TrySetCanceled(cancellation));
			}

			lock (_gate)
			{
				_started.Add(query);
			}

			return query.Gate.Task;
		}

		// A read the pipeline queued behind a landed one starts once Rx unwinds that one on the thread it
		// resumed on, after the delivery the test observed.
		public async Task<HeldQuery> AwaitStart(int index)
		{
			await AwaitCondition(() => Started.Count > index);

			return Started[index];
		}

		public async Task LandAndAwaitTheNextStart(HeldQuery query)
		{
			var startedBefore = Started.Count;

			query.Gate.SetResult(Ok(query.Request));

			await AwaitCondition(() => Started.Count > startedBefore);
		}
	}

	// A read whose cancel callback blocks until the test answers, as Npgsql's does while it opens a connection
	// to a server that never replies. The read itself never lands.
	private sealed class UnansweredCancels
	{
		private readonly ManualResetEventSlim _serverAnswers = new();
		private readonly ConcurrentQueue<CancellationToken> _started = new();

		public IReadOnlyCollection<CancellationToken> Started => _started;

		public Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryAsync(
			HistoryRequest _,
			CancellationToken cancellation)
		{
			cancellation.Register(() => _serverAnswers.Wait(TestDeadline));
			_started.Enqueue(cancellation);

			return new TaskCompletionSource<Result<IReadOnlyList<PenHistoryEnvelope>>>().Task;
		}

		public void Answer()
		{
			_serverAnswers.Set();
		}
	}
}
