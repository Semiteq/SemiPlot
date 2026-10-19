using AwesomeAssertions;

using Microsoft.Extensions.Logging.Testing;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.Core.Trends;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

using static SemiPlot.Tests.Integration.RealtimeArchive;

namespace SemiPlot.Tests.Integration;

// The archive is written by this class rather than cloned from the bench template: a poll is asserted
// against a handful of rows at timestamps the test chose, and the template's own last timestamp is
// whatever the generator happened to produce.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class RealtimePollReadTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int UnsubscribedPenId = 3;

	// Later than every subscribed variable's own newest row, so a baseline that ignored @ids would answer
	// this instant instead.
	private static readonly DateTime _unsubscribedLast = Day.AddMinutes(2);

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(SeededRowsWithAnUnsubscribedVariable(), Day, NextDay);
	}

	// It emits nothing: a first tick that emitted rows would have to emit every row since the archive began.
	[Fact]
	public async Task TheFirstTickEmitsNoSampleAndReportsTheSubscriptionArmed()
	{
		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var tick = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeSameAs(ArchiveConnectionState.Connected);
	}

	[Fact]
	public async Task ATickAfterAnAppendedRowEmitsExactlyThatRow()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = SeededLast.AddSeconds(1);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			Database,
			SubscribedPenIds[0],
			appended,
			42.5,
			ArchiveRow.OrdinaryQuality,
			cancellationToken: cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		var sample = tick.Samples.Should().ContainSingle().Which;

		sample.PenId.Should().Be(SubscribedPenIds[0]);
		sample.Value.Should().Be(42.5);
		sample.TimestampUtc.Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(appended));

		// The subscription was armed by the first tick, so an ordinary delivery reports no state change.
		tick.StateChange.Should().BeNull();
	}

	[Fact]
	public async Task ATickWithNothingNewEmitsNothing()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeNull();
	}

	[Fact]
	public async Task ATickIgnoresARowOfAnUnsubscribedVariable()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			Database,
			UnsubscribedPenId,
			SeededLast.AddSeconds(1),
			7,
			ArchiveRow.OrdinaryQuality,
			cancellationToken: cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().BeEmpty();
	}

	// The poll advances past the null row, or it would re-read the same row on every later tick. A delivered
	// row cannot show that, so the tick after the null row reads its own debug entry: it must count no row.
	[Fact]
	public async Task ARowCarryingANullValueEmitsNoSampleReportsNoFaultAndIsNotReadAgain()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			Database,
			SubscribedPenIds[0],
			SeededLast.AddSeconds(1),
			null,
			ArchiveRow.OrdinaryQuality,
			cancellationToken: cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeNull();

		await poll.ReadOnceAsync(cancellationToken);

		PollCounts(logs).Should().Equal(("1", "0", "1", "0"), ("0", "0", "0", "0"));
	}

	[Fact]
	public async Task ARowMarkingTheLastSampleBeforeABreakEmitsItsValueAndThenABreak()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = SeededLast.AddSeconds(1);
		var appendedUtc = ArchiveProviderFactory.TimeConverter.ToUtc(appended);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			Database,
			SubscribedPenIds[0],
			appended,
			11.25,
			ArchiveRow.LastBeforeBreakQuality,
			cancellationToken: cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().Equal(
			new Sample(SubscribedPenIds[0], appendedUtc, 11.25),
			new Sample(SubscribedPenIds[0], appendedUtc.AddMilliseconds(1), null));
		PollCounts(logs).Should().Equal(("1", "1", "0", "1"));
	}

	// docs/architecture/data-integration.md#realtime
	[Fact]
	public async Task ARowMarkingTheLastSampleBeforeABreakWithANullValueEmitsOnlyTheBreak()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = SeededLast.AddSeconds(1);

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);
		using var logs = new FakeLoggerProvider();

		var poll = NewPoll(services, logs.CreateLogger("RealtimePoll"));

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			Database,
			SubscribedPenIds[0],
			appended,
			null,
			ArchiveRow.LastBeforeBreakQuality,
			cancellationToken: cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().Equal(new Sample(
			SubscribedPenIds[0],
			ArchiveProviderFactory.TimeConverter.ToUtc(appended).AddMilliseconds(1),
			null));
		tick.StateChange.Should().BeNull();

		await poll.ReadOnceAsync(cancellationToken);

		PollCounts(logs).Should().Equal(("1", "0", "1", "1"), ("0", "0", "0", "0"));
	}

	// Renamed rather than dropped: the container is shared. The tick after recovery must report nothing.
	[Fact]
	public async Task TheFirstSuccessAfterARaisedFaultReportsConnectedExactlyOnce()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.PlotConnectionString);

		var poll = NewPoll(services);
		var states = new List<ArchiveConnectionState?>();

		await HideTrendsAsync(cancellationToken);

		try
		{
			states.AddRange(await TickAsync(poll, ConsecutiveFailuresBeforeFault));
		}
		finally
		{
			await RestoreTrendsAsync(cancellationToken);
		}

		// The recovering tick is an ordinary baseline read, taken at the newest row then present: row A, so
		// it delivers nothing, and only row B written after it is delivered.
		var appendedFirst = SeededLast.AddSeconds(1);
		var appendedSecond = SeededLast.AddSeconds(2);

		await AppendAsync(
			Database,
			SubscribedPenIds[0],
			appendedFirst,
			1,
			ArchiveRow.OrdinaryQuality,
			cancellationToken: cancellationToken);

		var recovery = await poll.ReadOnceAsync(cancellationToken);

		states.Add(recovery.StateChange);
		recovery.Samples.Should().BeEmpty();

		await AppendAsync(
			Database,
			SubscribedPenIds[1],
			appendedSecond,
			2,
			ArchiveRow.OrdinaryQuality,
			cancellationToken: cancellationToken);

		var delivery = await poll.ReadOnceAsync(cancellationToken);

		states.Add(delivery.StateChange);

		var delivered = delivery.Samples.Should().ContainSingle().Which;

		delivered.PenId.Should().Be(SubscribedPenIds[1]);
		delivered.TimestampUtc.Should().Be(ArchiveProviderFactory.TimeConverter.ToUtc(appendedSecond));

		states.Take(ConsecutiveFailuresBeforeFault - 1).Should().AllSatisfy(state => state.Should().BeNull());

		var raisedFault = states[ConsecutiveFailuresBeforeFault - 1]?.Fault?.Kind;

		raisedFault.Should().Be(ArchiveFault.ConnectionLost);
		states[ConsecutiveFailuresBeforeFault].Should().BeSameAs(ArchiveConnectionState.Connected);
		states[ConsecutiveFailuresBeforeFault + 1].Should().BeNull();
	}

	// Renamed rather than dropped: the rows and the day partitions come back with it, so the recovering
	// tick reads the archive this class seeded rather than an empty one.
	private Task HideTrendsAsync(CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			"ALTER TABLE public.trends RENAME TO trends_hidden_by_test;",
			cancellationToken);
	}

	private Task RestoreTrendsAsync(CancellationToken cancellationToken)
	{
		return ArchiveDatabase.ExecuteAsync(
			Database.AdminConnectionString,
			"ALTER TABLE public.trends_hidden_by_test RENAME TO trends;",
			cancellationToken);
	}

	// The baseline entry carries no RowCount.
	private static IEnumerable<(string? Rows, string? Values, string? Dropped, string? Breaks)> PollCounts(
		FakeLoggerProvider logs)
	{
		return logs.Collector.GetSnapshot()
			.Where(entry => entry.GetStructuredStateValue("RowCount") is not null)
			.Select(entry => (
				entry.GetStructuredStateValue("RowCount"),
				entry.GetStructuredStateValue("ValueCount"),
				entry.GetStructuredStateValue("DroppedCount"),
				entry.GetStructuredStateValue("BreakCount")));
	}

	// The unsubscribed variable's newest row sits a minute after the subscribed ones, so a baseline read
	// over the whole archive rather than over the requested identifiers answers a different instant and fails.
	private static IReadOnlyList<ArchiveRow> SeededRowsWithAnUnsubscribedVariable()
	{
		return
		[
			.. SeededRows(),
			new ArchiveRow(
				UnsubscribedPenId,
				ArchiveRow.RawLayer,
				_unsubscribedLast,
				UnsubscribedPenId,
				ArchiveRow.OrdinaryQuality)
		];
	}
}
