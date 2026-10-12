using AwesomeAssertions;

using Xunit;

namespace SemiPlot.Tests.Unit.UI.Chart;

/// <summary>Repeats a mutation on the test thread while a render task draws its frame budget.</summary>
internal static class RenderWhileMutating
{
	private const int FrameBudget = 300;

	private static readonly TimeSpan _runBudget = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _joinBudget = TimeSpan.FromSeconds(30);

	public static async Task RunAsync(Action drawFrame, Action mutate, string because)
	{
		using var cancellation = new CancellationTokenSource(_runBudget);

		// Written by the render task, read by the test thread only after joining it.
		var frames = 0;

		var renderTask = Task.Run(
			() =>
			{
				while (frames < FrameBudget && !cancellation.Token.IsCancellationRequested)
				{
					drawFrame();
					frames++;
				}
			},
			TestContext.Current.CancellationToken);

		Exception? failure;

		try
		{
			while (!cancellation.Token.IsCancellationRequested && !renderTask.IsCompleted)
			{
				mutate();
			}
		}
		finally
		{
			await cancellation.CancelAsync();
			failure = await Record.ExceptionAsync(
				() => renderTask.WaitAsync(_joinBudget, TestContext.Current.CancellationToken));
		}

		failure.Should().BeNull(because + "; the render thread stopped at frame {0}", frames);
		frames.Should().Be(FrameBudget);
	}
}
