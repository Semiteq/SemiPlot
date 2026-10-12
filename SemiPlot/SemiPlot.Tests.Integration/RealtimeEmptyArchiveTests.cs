using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;

using SemiPlot.Core.Data;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// The branch every other gated class seeds past: an archive with public.trends but no row for the
// subscribed variables, whose baseline scalar answers NULL and must still arm the subscription.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeEmptyArchiveTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	// A NULL answer leaves lastSeen unset, so the next tick repeats the baseline read rather than binding
	// @lastSeen to nothing and going blind for good. The first tick still arms the subscription.
	[Fact]
	public async Task TheBaselineBranchArmsTheSubscriptionAndRepeatsUntilTheArchiveCarriesARow()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);

		var first = await poll.ReadOnceAsync(cancellationToken);

		first.Samples.Should().BeEmpty();
		first.StateChange.Should().BeSameAs(ArchiveConnectionState.Connected);

		var second = await poll.ReadOnceAsync(cancellationToken);

		second.Samples.Should().BeEmpty();
		second.StateChange.Should().BeNull();
	}

	// A tick that emitted the first row ever written would have to emit every row since the archive began.
	[Fact]
	public async Task TheFirstEverRowBecomesTheBaselineAndTheNextOneIsDelivered()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var first = Day.AddMinutes(1);
		var second = Day.AddMinutes(2);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(Database, SubscribedPenIds[0], first, 1.5, cancellationToken: cancellationToken);

		var baseline = await poll.ReadOnceAsync(cancellationToken);

		baseline.Samples.Should().BeEmpty();

		await AppendAsync(Database, SubscribedPenIds[1], second, 2.5, cancellationToken: cancellationToken);

		var sample = (await poll.ReadOnceAsync(cancellationToken)).Samples.Should().ContainSingle().Which;

		sample.PenId.Should().Be(SubscribedPenIds[1]);
		sample.TimestampUtc.Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(second));
	}

	// The same branch through the composed provider, which is where a consumer meets it: the armed signal
	// has to reach ConnectionFaults over an archive that answers nothing.
	[Fact]
	public async Task ASubscriptionOverAnArchiveWithNoRowReportsConnected()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var provider = services.GetRequiredService<IDataProvider>();

		using var armed = new ArmedGate(provider.ConnectionFaults);
		using var subscription = provider.Subscribe(SubscribedPenIds).Subscribe(_ => { });

		await armed.Armed(1).WaitAsync(GateBound, TestContext.Current.CancellationToken);
	}
}
