using System.Reactive;
using System.Reactive.Linq;

using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Testing;

using Npgsql;

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

		using var subscription = new BatchCollector<IReadOnlyList<Sample>>(
			liveSource.Subscribe(SubscribedPenIds).SampleBatches());

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

		using var first = new BatchCollector<IReadOnlyList<Sample>>(
			liveSource.Subscribe(SubscribedPenIds).SampleBatches());

		await armed.Armed(1).WaitAsync(GateBound, cancellationToken);

		var firstSeesTheEarlyRow = first.NextBatch();

		await AppendAsync(
			Database, SubscribedPenIds[0], beforeTheSecondSubscription, 1, cancellationToken: cancellationToken);
		await firstSeesTheEarlyRow.WaitAsync(GateBound, cancellationToken);

		using var second = new BatchCollector<IReadOnlyList<Sample>>(
			liveSource.Subscribe(SubscribedPenIds).SampleBatches());

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
			liveSource.Subscribe([SubscribedPenIds[0], UnservedPenId]).SampleBatches().Materialize());

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

	// Each answered poll writes its log line once its statement has answered, so an answer taken before the
	// statement lies after the previous poll's line and no later than its own. The test reads that order from
	// RealtimePoll.ReadOnceAsync logging after the statement; a line moved ahead of it voids the check.
	[Fact]
	public async Task APollWithNoNewRowEmitsItsAnswerTakenBeforeItsStatement()
	{
		const int PollCount = 3;
		var cancellationToken = TestContext.Current.CancellationToken;

		using var logs = new FakeLoggerProvider();
		using var armed = new ArmedGate(logs);
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		using var polls = new BatchCollector<LivePoll>(
			services.GetRequiredService<ILiveSource>().Subscribe(SubscribedPenIds));

		await polls.Reached(PollCount).WaitAsync(GateBound, cancellationToken);

		var answered = polls.Batches.Take(PollCount).ToArray();
		var lines = AnsweredPollEntries(logs).Take(PollCount).Select(entry => entry.Timestamp.UtcDateTime).ToArray();

		answered.Should().AllSatisfy(poll =>
		{
			poll.Samples.Should().BeEmpty();
			poll.ResumesAfterFailure.Should().BeFalse();
			poll.AnsweredAtUtc.Kind.Should().Be(DateTimeKind.Utc);
		});
		answered.Select(poll => poll.AnsweredAtUtc).Should().BeInAscendingOrder().And.OnlyHaveUniqueItems();
		lines.Should().HaveCount(PollCount);

		for (var index = 0; index < PollCount; index++)
		{
			answered[index].AnsweredAtUtc.Should().BeOnOrBefore(lines[index]);

			if (index > 0)
			{
				answered[index].AnsweredAtUtc.Should().BeOnOrAfter(lines[index - 1]);
			}
		}
	}

	// No answered poll can land while the table is hidden, so the gate taken before it returns opens on the
	// first answer after the failed run.
	[Fact]
	public async Task TheFirstAnswerAfterAFailedPollResumesAfterFailure()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);

		using var polls = new BatchCollector<LivePoll>(
			services.GetRequiredService<ILiveSource>().Subscribe(SubscribedPenIds));

		await armed.Armed(1).WaitAsync(GateBound, cancellationToken);
		await HideTrendsAsync(Database, cancellationToken);

		Task resumed;

		try
		{
			await armed.FailuresReached(1).WaitAsync(GateBound, cancellationToken);

			resumed = polls.NextBatch();
		}
		finally
		{
			await RestoreTrendsAsync(Database, cancellationToken);
		}

		await resumed.WaitAsync(GateBound, cancellationToken);

		var resumptions = polls.Batches.Select(poll => poll.ResumesAfterFailure).ToArray();

		resumptions[0].Should().BeFalse("the baseline poll follows no failure");
		resumptions.Should().ContainSingle(resumes => resumes);
		resumptions.SkipWhile(resumes => !resumes).Skip(1).Should().NotContain(true);
	}

	// The pen set moves to a new subscription while the archive is unreachable, as a catalogue edit can.
	[Fact]
	public async Task ASubscriptionTakingOverAfterAFailedPollResumesAfterFailure()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);
		var liveSource = services.GetRequiredService<ILiveSource>();

		using (new BatchCollector<LivePoll>(liveSource.Subscribe(SubscribedPenIds)))
		{
			await armed.Armed(1).WaitAsync(GateBound, cancellationToken);
			await HideTrendsAsync(Database, cancellationToken);

			try
			{
				await armed.FailuresReached(1).WaitAsync(GateBound, cancellationToken);
			}
			finally
			{
				await RestoreTrendsAsync(Database, cancellationToken);
			}
		}

		using var takenOver = new BatchCollector<LivePoll>(liveSource.Subscribe([SubscribedPenIds[0]]));

		await takenOver.Reached(1).WaitAsync(GateBound, cancellationToken);

		takenOver.Batches[0].ResumesAfterFailure.Should().BeTrue();
	}

	// The lock holds the poll's statement waiting on the server, as an unanswering archive does, until the
	// pen-set switch cancels it; the read neither answered nor failed on its own.
	[Fact]
	public async Task ASubscriptionTakingOverFromAReadCancelledInFlightResumesAfterFailure()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var armed = new ArmedGate();
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString, armed);
		var liveSource = services.GetRequiredService<ILiveSource>();

		await using (var locker = new NpgsqlConnection(Database.AdminConnectionString))
		{
			await locker.OpenAsync(cancellationToken);
			await using var transaction = await locker.BeginTransactionAsync(cancellationToken);

			using (new BatchCollector<LivePoll>(liveSource.Subscribe(SubscribedPenIds)))
			{
				await armed.Armed(1).WaitAsync(GateBound, cancellationToken);

				await using var lockTrends = new NpgsqlCommand(
					PostgresHistoryReadTests.LockTrendsCommand, locker, transaction);
				await lockTrends.ExecuteNonQueryAsync(cancellationToken);

				(await PostgresHistoryReadTests.AwaitALockWaitOnTrendsAsync(locker, transaction))
					.Should().BeTrue("the poll's statement has to be waiting on the server when the switch cancels it");
			}

			await transaction.RollbackAsync(cancellationToken);
		}

		using var takenOver = new BatchCollector<LivePoll>(liveSource.Subscribe([SubscribedPenIds[0]]));

		await takenOver.Reached(1).WaitAsync(GateBound, cancellationToken);

		takenOver.Batches[0].ResumesAfterFailure.Should().BeTrue();
	}

	private static IReadOnlyList<FakeLogRecord> PollEntries(FakeLoggerProvider logs)
	{
		return [.. logs.Collector.GetSnapshot().Where(entry => entry.GetStructuredStateValue("RowCount") is not null)];
	}

	// The baseline poll writes its line under the baseline key, every later one under its row count.
	private static IEnumerable<FakeLogRecord> AnsweredPollEntries(FakeLoggerProvider logs)
	{
		return logs.Collector.GetSnapshot().Where(entry =>
			entry.GetStructuredStateValue("RowCount") is not null
			|| entry.GetStructuredStateValue(ArmedGate.BaselineKey) is not null);
	}

	private static DateTime[] Delivered(BatchCollector<IReadOnlyList<Sample>> collector)
	{
		return [.. collector.Batches
			.SelectMany(batch => batch)
			.Select(sample => sample.TimestampUtc)];
	}
}
