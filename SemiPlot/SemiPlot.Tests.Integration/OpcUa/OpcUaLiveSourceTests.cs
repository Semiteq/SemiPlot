using AwesomeAssertions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tests.Integration.DemoScada;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration.OpcUa;

// One demo server on a free loopback port serves the class, archiving in Asia/Tokyo, so a stamp sent as local time
// misses true UTC by nine hours even on a UTC machine.
[Collection(DemoScadaCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class OpcUaLiveSourceTests : IClassFixture<DemoScadaServerFixture>
{
	private const int TestTimeoutMilliseconds = 180000;
	private const int FirstUnknownPenId = 99998;
	private const int SecondUnknownPenId = 99999;

	private static readonly TimeSpan _connectBound = TimeSpan.FromSeconds(60);
	private static readonly TimeSpan _sampleBound = TimeSpan.FromSeconds(30);
	private static readonly TimeSpan _clockTolerance = TimeSpan.FromSeconds(60);

	// Longer than the 5 s a subscription waits for its items before it reports its unserved pens.
	private static readonly TimeSpan _settleWait = TimeSpan.FromSeconds(7);

	private readonly DemoScadaServerFixture _fixture;

	public OpcUaLiveSourceTests(DemoScadaServerFixture fixture)
	{
		_fixture = fixture;
		_fixture.Host.Logs.Clear();
	}

	private static IReadOnlyList<int> PenIds => LiveSourceHost.DemoPenIds;

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task EveryPen_DeliversTheLatticeValueAtItsTrueUtcStamp_InIncreasingOrder()
	{
		var options = DemoScadaServerFixture.Options;
		var converter = new ArchiveTimeConverter(DemoScadaHost.ArchiveZone);
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(_fixture.Server));

		live.Start();
		(await live.WaitForStatesAsync(2, _connectBound)).Should().BeTrue(Because(live));
		(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValuesForEach(PenIds, 3), _sampleBound)).Should()
			.BeTrue(Because(live));

		foreach (var penId in PenIds)
		{
			var received = live.Primary.SamplesOf(penId);
			var stamps = received.Select(sample => sample.Sample.TimestampUtc).ToArray();

			received.Should().HaveCountGreaterThanOrEqualTo(3, "pen {0}; {1}", penId, Because(live));
			received.Should().NotContain(sample => sample.IsBreak, "pen {0}; {1}", penId, Because(live));
			stamps.Should().BeInAscendingOrder("pen {0}; {1}", penId, Because(live))
				.And.OnlyHaveUniqueItems("pen {0}; {1}", penId, Because(live));

			foreach (var sample in received)
			{
				var stamp = sample.Sample.TimestampUtc;
				var expected = LiveTailGenerator.ChangesAt(options, converter.ToArchiveLocal(stamp))
					.Single(row => row.Id == penId);

				stamp.Kind.Should().Be(DateTimeKind.Utc, "pen {0}; {1}", penId, Because(live));
				stamp.Should().BeCloseTo(
					sample.ReceivedUtc,
					_clockTolerance,
					"a local stamp sent as UTC misses by the zone's offset; pen {0}; {1}",
					penId,
					Because(live));
				sample.Sample.Value.Should().Be(expected.Value, "pen {0} at {1:O}; {2}", penId, stamp, Because(live));
			}
		}

		live.Primary.Terminations.Should().BeEmpty(Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task TwoUnknownPens_RaiseOneFaultNamingBoth_AndASecondSubscriptionOfTheSameSetRaisesNone()
	{
		IReadOnlyList<int> penIds = [.. PenIds, FirstUnknownPenId, SecondUnknownPenId];
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(_fixture.Server));

		live.Start(penIds);
		(await LiveSourceHost.WaitUntilAsync(() => live.PenFaults.Count > 0, _connectBound)).Should()
			.BeTrue(Because(live));
		(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValuesForEach(PenIds), _sampleBound)).Should()
			.BeTrue("the served pens keep delivering; {0}", Because(live));

		live.Primary.Dispose();
		var second = live.Subscribe(penIds);
		(await LiveSourceHost.WaitUntilAsync(() => second.HasValuesForEach(PenIds), _sampleBound)).Should()
			.BeTrue(Because(live));
		await Task.Delay(_settleWait, TestContext.Current.CancellationToken);

		var fault = live.PenFaults.Should().ContainSingle(Because(live)).Subject;
		fault.Kind.Should().Be(LiveSourceFault.PensUnserved, Because(live));
		fault.UnknownPenIds.Should().Equal([FirstUnknownPenId, SecondUnknownPenId], Because(live));
		fault.NotNumericPenIds.Should().BeEmpty(Because(live));
		live.Primary.Terminations.Should().BeEmpty(Because(live));
		second.Terminations.Should().BeEmpty(Because(live));
	}

	[Fact(Timeout = TestTimeoutMilliseconds)]
	public async Task ASecondSubscriptionOfTheSameSet_OnTheLiveSession_WritesNoBreak_AndStartsAtEachPensNewestStamp()
	{
		await using var live = new LiveSourceHost(LiveSourceHost.PortOf(_fixture.Server));

		live.Start();
		(await LiveSourceHost.WaitUntilAsync(() => live.Primary.HasValuesForEach(PenIds, 2), _connectBound)).Should()
			.BeTrue(Because(live));
		live.Primary.Samples.Should().NotContain(sample => sample.IsBreak, Because(live));

		live.Primary.Dispose();
		var newestBeforeTheSwitch = PenIds.ToDictionary(
			penId => penId,
			penId => live.Primary.SamplesOf(penId).Max(sample => sample.Sample.TimestampUtc));
		var second = live.Subscribe(PenIds);
		(await LiveSourceHost.WaitUntilAsync(() => second.HasValuesForEach(PenIds, 2), _sampleBound)).Should()
			.BeTrue(Because(live));

		second.Samples.Should().NotContain(sample => sample.IsBreak, Because(live));

		foreach (var penId in PenIds)
		{
			second.SamplesOf(penId)[0].Sample.TimestampUtc.Should().BeOnOrAfter(
				newestBeforeTheSwitch[penId],
				"pen {0}; {1}",
				penId,
				Because(live));
		}

		live.Primary.Terminations.Should().BeEmpty(Because(live));
		second.Terminations.Should().BeEmpty(Because(live));
	}

	private string Because(LiveSourceHost live)
	{
		return $"{live.Describe()}; {_fixture.Host.ServerLog()}";
	}
}
