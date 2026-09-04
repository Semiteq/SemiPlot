using AwesomeAssertions;

using Microsoft.Extensions.DependencyInjection;
using Microsoft.Extensions.Logging.Abstractions;

using Npgsql;

using NpgsqlTypes;

using SemiPlot.Core.Data;
using SemiPlot.Core.Data.Errors;
using SemiPlot.DataSource.Postgres;
using SemiPlot.DataSource.Postgres.Configuration;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

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
	// A strict subset of the seeded identifiers, so a tick that ignored @ids would fail.
	private static readonly int[] _subscribedPenIds = [1, 2];

	private const int UnsubscribedPenId = 3;

	// Restated here rather than read off RealtimePoll: the threshold is a promise to the operator, and a
	// test computing it from the constant it guards would pass whatever the constant became.
	private const int ConsecutiveFailuresBeforeFault = 3;

	// One calendar day, so the write creates the single partition tp2026m01d01 and every appended row
	// below falls inside it. Winter under Europe/Berlin, so the conversion out is an unambiguous +1 h.
	private static readonly DateTime _day = new(2026, 1, 1, 0, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _nextDay = _day.AddDays(1);

	// The newest seeded row, which is what a baseline read has to answer. Everything appended sits after
	// it and hours short of the partition's upper bound.
	private static readonly DateTime _seededLast = _day.AddMinutes(1);

	// Later than every subscribed variable's own newest row, so a baseline that ignored @ids would answer
	// this instant instead.
	private static readonly DateTime _unsubscribedLast = _day.AddMinutes(2);

	private static readonly ArchiveTimeConverter _timeConverter = new(ArchiveProviderFactory.SourceTimeZone);

	private const string AppendCommand = """
		INSERT INTO public.trends (id, l, t, v, q) VALUES (@id, 0, @t, @v, @q);
		""";

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(SeededRows(), _day, _nextDay);
	}

	// It emits nothing: a first tick that emitted rows would have to emit every row since the archive began.
	[Fact]
	public async Task TheFirstTickEmitsNoSampleAndReportsTheSubscriptionArmed()
	{
		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var tick = await NewPoll(services).ReadOnceAsync(TestContext.Current.CancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeSameAs(ArchiveConnectionState.Connected);
	}

	[Fact]
	public async Task TheFirstTickTakesItsBaselineFromTheArchivesOwnNewestRow()
	{
		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(TestContext.Current.CancellationToken);

		poll.LastSeen.Should().Be(_seededLast);
	}

	[Fact]
	public async Task ATickAfterAnAppendedRowEmitsExactlyThatRow()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = _seededLast.AddSeconds(1);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(_subscribedPenIds[0], appended, 42.5, ArchiveRow.OrdinaryQuality, cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		var sample = tick.Samples.Should().ContainSingle().Which;

		sample.PenId.Should().Be(_subscribedPenIds[0]);
		sample.Value.Should().Be(42.5);
		sample.TimestampUtc.Should().Be(_timeConverter.ToUtc(appended));

		// The subscription was armed by the first tick, so an ordinary delivery reports no state change.
		tick.StateChange.Should().BeNull();
	}

	[Fact]
	public async Task ATickWithNothingNewEmitsNothing()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

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

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			UnsubscribedPenId,
			_seededLast.AddSeconds(1),
			7,
			ArchiveRow.OrdinaryQuality,
			cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().BeEmpty();
	}

	// lastSeen still advances past it, or the poll would re-read the same null row on every later tick.
	[Fact]
	public async Task ARowCarryingANullValueEmitsNoSampleReportsNoFaultAndStillAdvancesTheLastSeen()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = _seededLast.AddSeconds(1);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(_subscribedPenIds[0], appended, null, ArchiveRow.OrdinaryQuality, cancellationToken);

		var tick = await poll.ReadOnceAsync(cancellationToken);

		tick.Samples.Should().BeEmpty();
		tick.StateChange.Should().BeNull();
		poll.LastSeen.Should().Be(appended);
	}

	// The archive's break mark carries a real value (docs/architecture/scada-archive.md, Quality and gaps).
	[Fact]
	public async Task ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var appended = _seededLast.AddSeconds(1);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);

		await poll.ReadOnceAsync(cancellationToken);
		await AppendAsync(
			_subscribedPenIds[0],
			appended,
			11.25,
			ArchiveRow.LastBeforeBreakQuality,
			cancellationToken);

		var sample = (await poll.ReadOnceAsync(cancellationToken)).Samples.Should().ContainSingle().Which;

		sample.Value.Should().Be(11.25);
		sample.TimestampUtc.Should().Be(_timeConverter.ToUtc(appended));
	}

	// The bound the next tick binds may only move forward.
	[Fact]
	public async Task TheLastSeenNeverMovesBackwardsAcrossTicks()
	{
		var cancellationToken = TestContext.Current.CancellationToken;
		var first = _seededLast.AddSeconds(1);
		var second = _seededLast.AddSeconds(2);

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);
		var seen = new List<DateTime?>();

		await poll.ReadOnceAsync(cancellationToken);
		seen.Add(poll.LastSeen);

		await AppendAsync(_subscribedPenIds[0], first, 1, ArchiveRow.OrdinaryQuality, cancellationToken);
		await poll.ReadOnceAsync(cancellationToken);
		seen.Add(poll.LastSeen);

		await poll.ReadOnceAsync(cancellationToken);
		seen.Add(poll.LastSeen);

		await AppendAsync(_subscribedPenIds[1], second, 2, ArchiveRow.OrdinaryQuality, cancellationToken);
		await poll.ReadOnceAsync(cancellationToken);
		seen.Add(poll.LastSeen);

		seen.Should().Equal(new DateTime?[] { _seededLast, first, first, second });
	}

	// Renamed rather than dropped: the container is shared. The tick after recovery must report nothing.
	[Fact]
	public async Task TheFirstSuccessAfterARaisedFaultReportsConnectedExactlyOnce()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		using var services = ArchiveProviderFactory.Build(Database.ReaderConnectionString);

		var poll = NewPoll(services);
		var states = new List<ArchiveConnectionState?>();

		await HideTrendsAsync(cancellationToken);

		try
		{
			for (var tick = 0; tick < ConsecutiveFailuresBeforeFault; tick++)
			{
				states.Add((await poll.ReadOnceAsync(cancellationToken)).StateChange);
			}
		}
		finally
		{
			await RestoreTrendsAsync(cancellationToken);
		}

		states.Add((await poll.ReadOnceAsync(cancellationToken)).StateChange);
		states.Add((await poll.ReadOnceAsync(cancellationToken)).StateChange);

		states.Take(ConsecutiveFailuresBeforeFault - 1).Should().AllSatisfy(state => state.Should().BeNull());

		var raisedFault = states[ConsecutiveFailuresBeforeFault - 1]?.Fault?.Kind;

		raisedFault.Should().Be(ArchiveFault.ConnectionLost);
		states[ConsecutiveFailuresBeforeFault].Should().BeSameAs(ArchiveConnectionState.Connected);
		states[ConsecutiveFailuresBeforeFault + 1].Should().BeNull();

		// The recovering tick is an ordinary baseline read, so it establishes the baseline it would have
		// established had nothing failed.
		poll.LastSeen.Should().Be(_seededLast);
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

	private RealtimePoll NewPoll(IServiceProvider services)
	{
		return new RealtimePoll(
			services.GetRequiredService<NpgsqlDataSource>(),
			services.GetRequiredService<ArchiveTimeConverter>(),
			services.GetRequiredService<ArchiveExceptionMapper>(),
			services.GetRequiredService<PostgresConnectionSettings>(),
			_subscribedPenIds,
			NullLogger.Instance);
	}

	// Written as scada_writer, the role the SCADA itself writes with, and one row at a time: a COPY would
	// go through ArchiveWriter, which refuses an archive already carrying rows.
	private async Task AppendAsync(
		int penId,
		DateTime archiveLocal,
		double? value,
		int quality,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(AppendCommand, connection);

		command.Parameters.Add(new NpgsqlParameter("id", NpgsqlDbType.Integer) { Value = penId });
		command.Parameters.Add(new NpgsqlParameter("t", NpgsqlDbType.Timestamp) { Value = archiveLocal });
		command.Parameters.Add(new NpgsqlParameter("v", NpgsqlDbType.Double)
		{
			Value = (object?)value ?? DBNull.Value
		});
		command.Parameters.Add(new NpgsqlParameter("q", NpgsqlDbType.Integer) { Value = quality });

		await command.ExecuteNonQueryAsync(cancellationToken);
	}

	// Two rows per subscribed variable, the second of them the baseline every test starts from. The
	// unsubscribed variable's newest row sits a minute later still, so a baseline read over the whole
	// archive rather than over the requested identifiers answers a different instant and fails.
	private static IReadOnlyList<ArchiveRow> SeededRows()
	{
		var rows = new List<ArchiveRow>();

		foreach (var penId in _subscribedPenIds)
		{
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, _day, penId, ArchiveRow.OrdinaryQuality));
			rows.Add(new ArchiveRow(penId, ArchiveRow.RawLayer, _seededLast, penId, ArchiveRow.OrdinaryQuality));
		}

		rows.Add(new ArchiveRow(
			UnsubscribedPenId,
			ArchiveRow.RawLayer,
			_unsubscribedLast,
			UnsubscribedPenId,
			ArchiveRow.OrdinaryQuality));

		return rows;
	}
}
