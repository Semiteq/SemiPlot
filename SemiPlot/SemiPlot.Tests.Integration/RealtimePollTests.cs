using AwesomeAssertions;

using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres.Configuration;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// The failure ladder alone, which needs no server: every tick connects to an address nothing answers and
// fails there. What a successful tick reads is a database question and is covered by RealtimePollReadTests.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimePollTests
{
	private static readonly PostgresConnectionSettings _settings = UnreachableArchive.Settings(TimeSpan.FromSeconds(1));

	[Fact]
	public async Task ASingleFailureReportsNoSampleAndRaisesNoFault()
	{
		await using var services = UnreachableArchive.Services(_settings);

		var tick = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeNull();
	}

	[Fact]
	public async Task TheThirdConsecutiveFailureRaisesExactlyOneFaultNamingTheArchive()
	{
		await using var services = UnreachableArchive.Services(_settings);

		var states = await TickAsync(NewPoll(services), ConsecutiveFailuresBeforeFault + 2);

		states.Take(ConsecutiveFailuresBeforeFault - 1).Should().AllSatisfy(state => state.Should().BeNull());

		var thirdFault = states[ConsecutiveFailuresBeforeFault - 1]?.Fault;
		var fault = thirdFault.Should().BeOfType<ArchiveError>().Which;

		fault.Kind.Should().Be(ArchiveFault.ConnectionLost);
		fault.Detail.Should().Be($"{ConsecutiveFailuresBeforeFault}");
		fault.Host.Should().Be(_settings.Host);
		fault.Port.Should().Be(_settings.Port);
		fault.Database.Should().Be(_settings.Database);
		states.Skip(ConsecutiveFailuresBeforeFault).Should().AllSatisfy(state => state.Should().BeNull());
	}
}
