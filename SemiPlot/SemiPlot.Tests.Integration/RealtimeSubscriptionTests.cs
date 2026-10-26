using System.Reactive;
using System.Reactive.Linq;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Trends;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// Every synchronisation point is an awaited emission (the poll's baseline log line, or a delivered batch)
// bounded by GateBound, except the disposal check, which waits out poll intervals because a stopped poll emits
// nothing. Appending before the baseline has run races the subscription and fails the await at the bound.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimeSubscriptionTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int FailureRunBound = 3;

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(SeededRows(), Day, NextDay);
	}

	// The logger belongs to this live source and this one subscription, so every per-tick entry that carries a
	// row count (written on each successful tick after the baseline) is the subscription's own. A poll the
	// disposal left running would write one a second.
	[Fact]
	public async Task DisposingASubscriptionStopsItsPoll()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var logs = new FakeLoggerProvider();
		using var armed = new ArmedGate(logs);
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		var liveSource = services.GetRequiredService<ILiveSource>();

		using var subscription = new BatchCollector<IReadOnlyList<Sample>>(liveSource.Subscribe(SubscribedPenIds));

		await armed.Armed(1).WaitAsync(GateBound, cancellationToken);

		var delivery = subscription.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], SeededLast.AddSeconds(1), 1, cancellationToken: cancellationToken);
		await delivery.WaitAsync(GateBound, cancellationToken);

		PollEntries(logs).Should().NotBeEmpty("the running subscription writes an entry for the tick that delivered");

		subscription.Dispose();

		// A tick already past its query when the subscription is dropped may still write its entry.
		await Task.Delay(ArchiveProviderFactory.PollInterval * 2, cancellationToken);

		var entriesAfterDisposal = PollEntries(logs).Count;

		await AppendAsync(
			Database, SubscribedPenIds[0], SeededLast.AddSeconds(2), 2, cancellationToken: cancellationToken);
		await Task.Delay(ArchiveProviderFactory.PollInterval * 3, cancellationToken);

		PollEntries(logs).Count.Should().Be(entriesAfterDisposal);
	}

	// The sequence is cold, so each subscription runs a RealtimePoll of its own and takes a baseline of its
	// own. The second one's baseline is already past the row the first was delivered, so that row must never
	// reach it, while the row appended after both are armed reaches both.
	[Fact]
	public async Task TwoSubscriptionsEachKeepTheirOwnLastSeen()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var beforeTheSecondSubscription = SeededLast.AddSeconds(1);
		var afterBoth = SeededLast.AddSeconds(2);

		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		var liveSource = services.GetRequiredService<ILiveSource>();

		using var first = new BatchCollector<IReadOnlyList<Sample>>(liveSource.Subscribe(SubscribedPenIds));

		await armed.Armed(1).WaitAsync(GateBound, cancellationToken);

		var firstSeesTheEarlyRow = first.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], beforeTheSecondSubscription, 1, cancellationToken: cancellationToken);
		await firstSeesTheEarlyRow.WaitAsync(GateBound, cancellationToken);

		using var second = new BatchCollector<IReadOnlyList<Sample>>(liveSource.Subscribe(SubscribedPenIds));

		await armed.Armed(2).WaitAsync(GateBound, cancellationToken);

		var firstSeesTheLateRow = first.NextBatch();
		var secondSeesTheLateRow = second.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], afterBoth, 2, cancellationToken: cancellationToken);
		await firstSeesTheLateRow.WaitAsync(GateBound, cancellationToken);
		await secondSeesTheLateRow.WaitAsync(GateBound, cancellationToken);

		armed.HighestConsecutiveFailures.Should().BeLessThan(
			FailureRunBound,
			"the test relies on an archive that answers the poll, and a run of failed ticks this long says it did not");
		Delivered(first).Should().Equal(
			[
				ArchiveProviderFactory.TimeConverter.ToUtc(beforeTheSecondSubscription),
				ArchiveProviderFactory.TimeConverter.ToUtc(afterBoth)
			]);
		Delivered(second).Should().Equal([ArchiveProviderFactory.TimeConverter.ToUtc(afterBoth)]);
	}

	// The archive holds no row for the second identifier, which is what an identifier the source does not
	// serve looks like to the poll.
	[Fact]
	public async Task AnIdentifierTheSourceDoesNotServeLeavesTheStreamRunning()
	{
		const int UnservedPenId = 9999;
		var cancellationToken = TestContext.Current.CancellationToken;

		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		var liveSource = services.GetRequiredService<ILiveSource>();

		using var notifications = new BatchCollector<Notification<IReadOnlyList<Sample>>>(
			liveSource.Subscribe([SubscribedPenIds[0], UnservedPenId]).Materialize());

		await armed.Armed(1).WaitAsync(GateBound, cancellationToken);

		var firstDelivery = notifications.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], SeededLast.AddSeconds(1), 1, cancellationToken: cancellationToken);
		await firstDelivery.WaitAsync(GateBound, cancellationToken);

		var secondDelivery = notifications.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], SeededLast.AddSeconds(2), 2, cancellationToken: cancellationToken);
		await secondDelivery.WaitAsync(GateBound, cancellationToken);

		notifications.Batches.Should().HaveCount(2)
			.And.OnlyContain(notification => notification.Kind == NotificationKind.OnNext);
		notifications.Batches
			.SelectMany(notification => notification.Value)
			.Select(sample => sample.PenId)
			.Should().Equal(SubscribedPenIds[0], SubscribedPenIds[0]);
	}

	private static IReadOnlyList<FakeLogRecord> PollEntries(FakeLoggerProvider logs)
	{
		return [.. logs.Collector.GetSnapshot().Where(entry => entry.GetStructuredStateValue("RowCount") is not null)];
	}

	private static DateTime[] Delivered(BatchCollector<IReadOnlyList<Sample>> collector)
	{
		return [.. collector.Batches
			.SelectMany(batch => batch)
			.Select(sample => sample.TimestampUtc)];
	}
}
