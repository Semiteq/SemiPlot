using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeSubscriptionDisposalTests
{
	private const int CalibrationTicks = 3;

	// Silence alone proves nothing (a leaked loop against this unreachable address is also silent), so a
	// second, left-running live source's own failed ticks calibrate the wait.
	[Fact]
	public async Task SubscribingAndDisposingAtOnceDeliversNothingAndStopsThePoll()
	{
		var settings = UnreachableArchive.Settings(TimeSpan.FromMilliseconds(10));

		using var droppedGate = new ArmedGate();
		using var runningGate = new ArmedGate();
		using var droppedServices = UnreachableArchive.Services(settings, droppedGate);
		using var runningServices = UnreachableArchive.Services(settings, runningGate);

		var dropped = droppedServices.GetRequiredService<ILiveSource>();
		var running = runningServices.GetRequiredService<ILiveSource>();

		using var droppedBatches = new BatchCollector<LivePoll>(dropped.Subscribe([1]));

		droppedBatches.Dispose();

		using var control = running.Subscribe([1]).Subscribe(_ => { });

		await runningGate.FailuresReached(CalibrationTicks)
			.WaitAsync(ClonedArchiveTest.GateBound, TestContext.Current.CancellationToken);

		// The control needed three ticks to get here, so a loop that survived its own disposal has had the
		// same three plus this margin. Anything it logged has been recorded by now.
		await Task.Delay(settings.PollInterval * 10, TestContext.Current.CancellationToken);

		droppedBatches.Batches.Should().BeEmpty();
		droppedGate.HighestConsecutiveFailures.Should().BeLessThan(CalibrationTicks);
	}
}
