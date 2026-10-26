using AwesomeAssertions;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class StaleArchiveGuardTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int PenId = 1;

	private static readonly DateTime _now = new(2026, 1, 2, 12, 0, 0, DateTimeKind.Unspecified);

	// The state the operator reached: a fill from the previous day under a writer started today. The
	// refusal names the verb that refills, because that is the whole remedy.
	[Fact]
	public async Task AnArchiveOlderThanTheBoundIsRefusedAndNamesConverge()
	{
		var newest = _now.AddMinutes(-793.7);

		await WriteRowsAsync(newest);

		Func<Task> act = () => StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, _now, TestContext.Current.CancellationToken);

		var refused = (await act.Should().ThrowAsync<SeederException>()).Which;

		refused.Message.Should().Contain("converge");
	}

	// The caller reads the absent timestamp as "start at the clock".
	[Fact]
	public async Task AnEmptyArchiveIsAcceptedAndReportsNoNewestRow()
	{
		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, _now, TestContext.Current.CancellationToken);
		outcome.Should().BeNull();
	}

	// The archive holds two rows one span apart, because the newest of them is what the guard owes the caller.
	[Fact]
	public async Task AnArchiveWithRowsReportsItsNewestRow()
	{
		var newest = ToMillisecond(_now.AddSeconds(-30));

		await WriteRowsAsync(newest.AddSeconds(-45), newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, _now, TestContext.Current.CancellationToken);
		outcome.Should().Be(newest);
	}

	// A database without public.trends surfaces Npgsql's own 42P01 rather than a controlled SeederException.
	[Fact]
	public async Task ADatabaseWithoutTheArchiveTableSurfacesUndefinedTable()
	{
		Func<Task> act = () => StaleArchiveGuard.CheckAsync(
			Fixture.Server.AdminConnectionString, _now, TestContext.Current.CancellationToken);

		var thrown = (await act.Should().ThrowAsync<PostgresException>()).Which;

		thrown.SqlState.Should().Be(PostgresErrorCodes.UndefinedTable);
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
