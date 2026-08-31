using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The guard reads max(t) from a server, so every case is gated. Three of them write, and the class's
// contract is that no database it touched survives it, so each test method gets its own clone — xunit
// constructs the class once per method, which is what makes that per-method rather than per-class.
//
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
	// refusal names the script that refills, because that is the whole remedy.
	[Fact]
	public async Task AnArchiveOlderThanTheBoundIsRefusedAndNamesTheScript()
	{
		Fixture.RequireAvailable();

		var newest = Now().AddMinutes(-793.7);

		await WriteRowsAsync(newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsFailed);
		Assert.Contains(
			outcome.Errors,
			error => error.Message.Contains("scripts/bench-demo.ps1", StringComparison.Ordinal));
	}

	// Nothing has been written, so there is no fill for an append to stand apart from and nothing a hole
	// could be torn in. A refusal here would make a freshly provisioned database unusable, and there is
	// no edge to report: the caller reads the absent timestamp as "start at the clock".
	[Fact]
	public async Task AnEmptyArchiveIsAcceptedAndReportsNoNewestRow()
	{
		Fixture.RequireAvailable();

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess, ArchiveReadSupport.Describe(outcome));
		Assert.Null(outcome.Value);
	}

	// Program.FollowAsync starts its loop at the timestamp reported here, so the fill's own edge is what
	// the first tick continues. The archive holds two rows one span apart, because the newest of them is
	// what the guard owes the caller — an answer of "some row" would still leave a hole behind the first
	// tick.
	[Fact]
	public async Task AnArchiveWithRowsReportsItsNewestRow()
	{
		Fixture.RequireAvailable();

		var newest = ToMillisecond(Now().AddSeconds(-30));

		await WriteRowsAsync(newest.AddSeconds(-45), newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess, ArchiveReadSupport.Describe(outcome));
		Assert.Equal(newest, outcome.Value);
	}

	// The live case, which is what the bound exists to keep. A writer ticking every second leaves max(t)
	// a second or two behind the clock, and a guard that refused that would refuse every restart of the
	// stand. The instant is stated rather than derived from MaximumAge, so it stays a live archive
	// whatever the bound is set to.
	[Fact]
	public async Task AnArchiveAWriterIsKeepingLiveIsAccepted()
	{
		Fixture.RequireAvailable();

		var newest = Now().AddSeconds(-2);

		await WriteRowsAsync(newest);

		var outcome = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, Now(), TestContext.Current.CancellationToken);

		Assert.True(outcome.IsSuccess, ArchiveReadSupport.Describe(outcome));
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

		var written = await Writer().WriteAsync(
			rows,
			timestamps.Min(),
			timestamps.Max().AddSeconds(1),
			cancellationToken: TestContext.Current.CancellationToken);

		Assert.True(written.IsSuccess, ArchiveReadSupport.Describe(written));
	}
}
