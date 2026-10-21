using AwesomeAssertions;

using SemiPlot.Tests.Integration.DemoScada;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration.OpcUa;

// Every endpoint is a free loopback port from DemoScadaHost.
[Collection(DemoScadaCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class OpcUaLiveSourceRestartTests : IDisposable
{
	private const int TestTimeoutMilliseconds = 180000;

	private static readonly TimeSpan _connectBound = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _lossBound = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _sampleBound = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _breakStep = TimeSpan.FromMilliseconds(1);

	// Longer than the 4-5 s the stack takes to notice the loss, shorter than the 60 s session timeout.
	private static readonly TimeSpan _cutLength = TimeSpan.FromSeconds(8);

	private readonly DemoScadaHost _host = new();

	public void Dispose()
	{
		_host.Dispose();
	}

	private static FollowOptions Options => DemoScadaHost.Options with { ChangeSeconds = 0.25 };

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task AServerRestartedOnItsPort_BreaksEachPenOnceAtItsNewestStamp_BeforeTheNewSessionsSamples()
	{
		var penIds = LiveSourceHost.DemoPenIds;
		var first = await _host.StartAsync(Options);
		var port = LiveSourceHost.PortOf(first);
		await using var live = new LiveSourceHost(port);

		try
		{
			live.Start();
			(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValuesForEach(penIds), _connectBound)).Should()
				.BeTrue(Because(live));
		}
		finally
		{
			await first.DisposeAsync();
		}

		(await live.WaitForStatesAsync(3, _lossBound)).Should().BeTrue(Because(live));
		var restartUtc = DateTime.UtcNow;
		await using var restarted = await _host.StartOnPortOnceFreeAsync(Options with { OpcPort = port });
		(await live.WaitForStatesAsync(4, _connectBound)).Should().BeTrue(Because(live));
		(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValueAfterABreakForEach(penIds), _sampleBound))
			.Should().BeTrue(Because(live));

		Names(live).Should().Equal(["Connecting", "Connected", "Unreachable", "Connected"], Because(live));
		var resumed = ShouldBreakEachPenOnceAtItsNewestStamp(live.Primary, penIds, Because(live));
		resumed.Should().AllSatisfy(
			sample => sample.Sample.TimestampUtc.Should().BeOnOrAfter(restartUtc, Because(live)),
			"the new server stamps every first notification with its start; {0}",
			Because(live));
		live.Primary.Terminations.Should().BeEmpty(Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ACutTransport_BreaksEachPenOnce_ResumesPastTheBreaks_AndASubscriptionDisposedInTheCutStaysQuiet()
	{
		IReadOnlyList<int> keptPenIds = [.. LiveSourceHost.DemoPenIds.SkipLast(2)];
		IReadOnlyList<int> droppedPenIds = [.. LiveSourceHost.DemoPenIds.TakeLast(2)];
		await using var server = await _host.StartAsync(Options);
		await using var relay = LoopbackRelay.Start(LiveSourceHost.PortOf(server));
		await using var live = new LiveSourceHost(relay.Port);

		live.Start(keptPenIds);
		var dropped = live.Subscribe(droppedPenIds);
		(await LiveSourceHost.WaitUntilAsync(
			() => live.Primary.HasValuesForEach(keptPenIds) && dropped.HasValuesForEach(droppedPenIds),
			_connectBound)).Should().BeTrue(Because(live));

		var cutUtc = DateTime.UtcNow;
		relay.Cut();
		(await live.WaitForStatesAsync(3, _lossBound)).Should().BeTrue(
			"a session that kept the configured URL dials the relay and notices the cut; {0}",
			Because(live));
		dropped.Dispose();
		var droppedBatches = dropped.BatchCount;
		var restOfTheCut = _cutLength - (DateTime.UtcNow - cutUtc);

		if (restOfTheCut > TimeSpan.Zero)
		{
			await Task.Delay(restOfTheCut, TestContext.Current.CancellationToken);
		}

		relay.Resume();
		(await live.WaitForStatesAsync(4, _connectBound)).Should().BeTrue(Because(live));
		(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValueAfterABreakForEach(keptPenIds), _sampleBound))
			.Should().BeTrue(Because(live));

		Names(live).Should().Equal(["Connecting", "Connected", "Unreachable", "Connected"], Because(live));
		ShouldBreakEachPenOnceAtItsNewestStamp(live.Primary, keptPenIds, Because(live));

		// The recorder lets go of the stream on dispose, so this pins only that the dispose raised no fault and no
		// completion; PenSubscriptionTests holds what a closed subscription emits.
		dropped.BatchCount.Should().Be(droppedBatches, Because(live));
		dropped.Terminations.Should().BeEmpty(Because(live));
		live.Primary.Terminations.Should().BeEmpty(Because(live));
	}

	/// <summary>
	/// Each pen breaks exactly once, 1 ms past its newest value before the break, and every break arrives before the
	/// first value of any pen after its break; returns those first values, which also lie past their pen's break.
	/// </summary>
	private static IReadOnlyList<ReceivedSample> ShouldBreakEachPenOnceAtItsNewestStamp(
		SampleRecorder recorder,
		IReadOnlyList<int> penIds,
		string because)
	{
		var samples = recorder.Samples;
		var lastBreakIndex = -1;
		var firstResumedIndex = int.MaxValue;
		var resumed = new List<ReceivedSample>();

		foreach (var penId in penIds)
		{
			var indexed = samples
				.Select((sample, index) => (Sample: sample, Index: index))
				.Where(entry => entry.Sample.Sample.PenId == penId)
				.ToArray();
			var penBreak = indexed.Where(entry => entry.Sample.IsBreak).Should()
				.ContainSingle("pen {0}; {1}", penId, because).Subject;
			var newest = indexed
				.Where(entry => entry.Index < penBreak.Index)
				.Max(entry => entry.Sample.Sample.TimestampUtc);
			var next = indexed.First(entry => entry.Index > penBreak.Index);

			penBreak.Sample.Sample.TimestampUtc.Should().Be(newest + _breakStep, "pen {0}; {1}", penId, because);
			next.Sample.Sample.TimestampUtc.Should().BeAfter(
				penBreak.Sample.Sample.TimestampUtc,
				"pen {0}; {1}",
				penId,
				because);
			lastBreakIndex = Math.Max(lastBreakIndex, penBreak.Index);
			firstResumedIndex = Math.Min(firstResumedIndex, next.Index);
			resumed.Add(next.Sample);
		}

		lastBreakIndex.Should().BeLessThan(firstResumedIndex, "every break precedes the new session; {0}", because);

		return resumed;
	}

	private static IReadOnlyList<string> Names(LiveSourceHost live)
	{
		return [.. live.States.Select(state => state.Name)];
	}

	private string Because(LiveSourceHost live)
	{
		return $"{live.Describe()}; {_host.ServerLog()}";
	}
}
