using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// The branch every other gated class seeds past: an archive with public.trends but no row for the
// subscribed variables, whose baseline scalar answers NULL.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeEmptyArchiveTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	// A NULL answer leaves lastSeen unset, so the next tick repeats the baseline read rather than binding
	// @lastSeen to nothing and going blind for good.
	[Fact]
	public async Task TheBaselineBranchRepeatsUntilTheArchiveCarriesARow()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		(await poll.ReadOnceAsync(cancellationToken)).Should().BeEmpty();
		(await poll.ReadOnceAsync(cancellationToken)).Should().BeEmpty();

		logs.Collector.GetSnapshot()
			.Count(entry => entry.GetStructuredStateValue(ArmedGate.BaselineKey) is not null)
			.Should().Be(2);
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

		baseline.Should().BeEmpty();

		await AppendAsync(Database, SubscribedPenIds[1], second, 2.5, cancellationToken: cancellationToken);

		var sample = (await poll.ReadOnceAsync(cancellationToken)).Should().ContainSingle().Which;

		sample.PenId.Should().Be(SubscribedPenIds[1]);
		sample.TimestampUtc.Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(second));
	}

	[Fact]
	public async Task ASubscriptionOverAnArchiveWithNoRowLogsItsBaseline()
	{
		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		var provider = services.GetRequiredService<IDataProvider>();

		using var subscription = provider.Subscribe(SubscribedPenIds).Subscribe(_ => { });

		await armed.Armed(1).WaitAsync(GateBound, TestContext.Current.CancellationToken);
	}
}
