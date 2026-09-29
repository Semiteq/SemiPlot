using AwesomeAssertions;

using FluentResults;

using SemiPlot.Core.Data.Errors;
using SemiPlot.UI.PenEditor;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.PenEditor;

// Each gate is awaited by a call the queue runs and completed by the test, so it takes no
// RunContinuationsAsynchronously.
[Trait("Component", "UI")]
[Trait("Area", "Chart")]
[Trait("Category", "Unit")]
public sealed class EditorCallQueueTests
{
	private readonly EditorCallQueue _queue;

	private int _succeededCalls;

	public EditorCallQueueTests()
	{
		_queue = new EditorCallQueue(() => _succeededCalls++);
	}

	[Fact]
	public async Task AThrownCall_ReachesOnlyItsOwnAwaiterAndNeverStopsTheNext()
	{
		var gate = new TaskCompletionSource();

		var thrown = _queue.RunAsync<Result<int>>(async () =>
		{
			await gate.Task;

			throw new InvalidOperationException("the call threw");
		});
		var next = _queue.RunAsync(() => Task.FromResult(Result.Ok(7)));

		gate.SetResult();

		await FluentActions.Awaiting(() => thrown).Should().ThrowAsync<InvalidOperationException>();
		(await next).Value.Should().Be(7);
		await FluentActions.Awaiting(_queue.WhenIdleAsync).Should().NotThrowAsync();
	}

	[Fact]
	public async Task WhenIdle_WaitsAlsoForACallQueuedWhileItWaits()
	{
		var first = new TaskCompletionSource();
		var second = new TaskCompletionSource();
		_ = _queue.RunAsync(async () =>
		{
			await first.Task;

			return Result.Ok(1);
		});

		var idle = _queue.WhenIdleAsync();
		_ = _queue.RunAsync(async () =>
		{
			await second.Task;

			return Result.Ok(2);
		});
		first.SetResult();

		idle.IsCompleted.Should().BeFalse("a call queued after the wait began is still running");

		second.SetResult();

		await idle;
	}

	[Fact]
	public async Task ASuccessfulCall_InvokesTheCallbackOnceAfterItLands()
	{
		var gate = new TaskCompletionSource();
		var call = _queue.RunAsync(async () =>
		{
			await gate.Task;

			return Result.Ok();
		});

		_succeededCalls.Should().Be(0, "the call has not landed yet");

		gate.SetResult();
		await call;

		_succeededCalls.Should().Be(1);
	}

	[Fact]
	public async Task AFailedCall_NeverInvokesTheCallback()
	{
		var failed = await _queue.RunAsync(() => Task.FromResult(
			Result.Fail(new ArchiveError(ArchiveFault.RowGone, "bench", 5432, "semiplot_dev", "Pressure"))));

		failed.IsFailed.Should().BeTrue();
		_succeededCalls.Should().Be(0);
	}

	[Fact]
	public async Task AThrownCall_NeverInvokesTheCallback()
	{
		var thrown = _queue.RunAsync<Result>(() => throw new InvalidOperationException("the call threw"));

		await FluentActions.Awaiting(() => thrown).Should().ThrowAsync<InvalidOperationException>();
		_succeededCalls.Should().Be(0);
	}
}
