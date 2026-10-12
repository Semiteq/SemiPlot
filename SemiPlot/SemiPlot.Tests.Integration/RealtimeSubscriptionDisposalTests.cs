using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeSubscriptionDisposalTests
{
	// Silence alone proves nothing (a leaked loop against this unreachable address is also silent), so a
	// second, left-running provider's own ArchiveFault.ConnectionLost calibrates the wait.
	[Fact]
	public async Task SubscribingAndDisposingAtOnceDeliversNothingAndStopsThePoll()
	{
		var settings = UnreachableArchive.Settings(TimeSpan.FromMilliseconds(10));

		using var droppedServices = UnreachableArchive.Services(settings);
		using var runningServices = UnreachableArchive.Services(settings);

		var dropped = droppedServices.GetRequiredService<IDataProvider>();
		var running = runningServices.GetRequiredService<IDataProvider>();

		var runningFaulted = new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously);

		using var droppedStates = new BatchCollector<ArchiveConnectionState>(dropped.ConnectionFaults);
		using var runningWatch = running.ConnectionFaults.Subscribe(_ => runningFaulted.TrySetResult());
		using var droppedBatches = new BatchCollector<IReadOnlyList<Sample>>(dropped.Subscribe([1]));

		droppedBatches.Dispose();

		using var control = running.Subscribe([1]).Subscribe(_ => { });

		await runningFaulted.Task.WaitAsync(ClonedArchiveTest.GateBound, TestContext.Current.CancellationToken);

		// The control needed three ticks to get here, so a loop that survived its own disposal has had the
		// same three plus this margin. Anything it raised has been recorded by now.
		await Task.Delay(settings.PollInterval * 10, TestContext.Current.CancellationToken);

		droppedBatches.Batches.Should().BeEmpty();
		droppedStates.Batches.Should().BeEmpty();
	}
}
