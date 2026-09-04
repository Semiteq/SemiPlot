using AwesomeAssertions;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The instants are relative to the machine's own clock rather than to a fixed calendar date, because
// the bound the guard applies is against that clock.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class StaleArchiveGuardTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int PenId = 1;

	// The state the operator reached: a fill from the previous day under a writer started today. The
	// refusal names the verb that refills, because that is the whole remedy.
	[Fact]
	public async Task AnArchiveOlderThanTheBoundIsRefusedAndNamesConverge()
	{
		var newest = Now().AddMinutes(-793.7);

		await WriteRowsAsync(newest);

		Func<Task> act = () => StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		var refused = (await act.Should().ThrowAsync<SeederException>()).Which;

		refused.Message.Should().Contain("converge");
	}

	// The caller reads the absent timestamp as "start at the clock".
	[Fact]
	public async Task AnEmptyArchiveIsAcceptedAndReportsNoNewestRow()
	{
		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);
		outcome.Should().BeNull();
	}

	// The archive holds two rows one span apart, because the newest of them is what the guard owes the caller.
	[Fact]
	public async Task AnArchiveWithRowsReportsItsNewestRow()
	{
		var newest = ToMillisecond(Now().AddSeconds(-30));

		await WriteRowsAsync(newest.AddSeconds(-45), newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);
		outcome.Should().Be(newest);
	}

	// The instant is stated rather than derived from MaximumAge, so it stays a live archive whatever the
	// bound is set to.
	[Fact]
	public async Task AnArchiveAWriterIsKeepingLiveIsAccepted()
	{
		var newest = Now().AddSeconds(-2);

		await WriteRowsAsync(newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		outcome.Should().NotBeNull();
	}

	// A database without public.trends surfaces Npgsql's own 42P01 rather than a controlled SeederException.
	[Fact]
	public async Task ADatabaseWithoutTheArchiveTableSurfacesUndefinedTable()
	{
		Func<Task> act = () => StaleArchiveGuard.CheckAsync(
			Fixture.Server.AdminConnectionString, Now(), TestContext.Current.CancellationToken);

		var thrown = (await act.Should().ThrowAsync<PostgresException>()).Which;

		thrown.SqlState.Should().Be(PostgresErrorCodes.UndefinedTable);
	}

	private static DateTime Now()
	{
		return DateTime.SpecifyKind(DateTime.Now, DateTimeKind.Unspecified);
	}

	// public.trends.t is timestamp(3), so an instant a test compares against what it reads back carries
	// no ticks below a millisecond.
	private static DateTime ToMillisecond(DateTime instant)
	{
		return new DateTime(
			instant.Ticks - (instant.Ticks % TimeSpan.TicksPerMillisecond), instant.Kind);
	}

	// Every row of a case goes in one WriteAsync, because the writer refuses a second seeding call
	// against an archive that already carries rows and only a follow run may set allowExistingRows.
	private async Task WriteRowsAsync(params DateTime[] timestamps)
	{
		var rows = timestamps
			.Select(timestamp =>
				new ArchiveRow(PenId, ArchiveRow.RawLayer, timestamp, 1.0, ArchiveRow.OrdinaryQuality))
			.ToArray();

		await Writer().WriteAsync(
			rows,
			timestamps.Min(),
			timestamps.Max().AddSeconds(1),
			cancellationToken: TestContext.Current.CancellationToken);
	}
}
