using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The operator's own sequence: a demo writer runs, is stopped, and a second one starts against the
// archive the first left. Only a database can answer this — the failure it reproduces is the primary key
// refusing a second copy of a row, and ArchiveWriter appends by binary COPY, which has no conflict
// handling — so both cases are gated, and both write, so the class takes a clone of its own rather than
// the shared seeded archive.
//
// The change interval is half a second, the demo's own. Every row a follow run writes is a pure function
// of absolute time, so whichever row a stopped run left newest — a change row or the anchor one poll
// interval ahead of one — sits on a point the lattice produces again, and the restart is a collision
// rather than a near miss. The first case asserts that precondition instead of restating which of the two
// kinds the edge happens to be.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class FollowRestartTests(PostgresContainerFixture postgresContainerFixture)
	: ClonedArchiveTest(postgresContainerFixture, CloneSource.Provisioned)
{
	private const int PenCount = 3;

	private const double ChangeSeconds = 0.5;

	private static readonly string _countRawRowsCommand =
		$"SELECT count(*) FROM public.trends WHERE l = {ArchiveRow.RawLayer};";

	private static readonly DateTime _firstRunStart = new(2026, 1, 2, 8, 0, 0, DateTimeKind.Unspecified);

	private static readonly DateTime _firstRunStop = _firstRunStart.AddSeconds(10);

	// When the second writer is started. Inside StaleArchiveGuard.MaximumAge of the edge, as a restart by
	// hand is: the archive the first run left is fresh, and the guard accepts it.
	private static readonly DateTime _restartClock = _firstRunStop.AddSeconds(3);

	private static readonly IReadOnlyList<ArchiveRow> _firstRunRows =
		LiveTailGenerator.Generate(Options(), _firstRunStart, _firstRunStop);

	// The newest row the first run left, which is the row the second run must not write again.
	private static DateTime Edge => _firstRunRows.Max(row => row.Timestamp);

	protected override async ValueTask SeedAsync()
	{
		await Writer().WriteAsync(
			_firstRunRows,
			_firstRunStart,
			_firstRunStop,
			allowExistingRows: true,
			TestContext.Current.CancellationToken);
	}

	// The regression. A window closed at the edge regenerates the row sitting there and the COPY fails with
	// 23505 on the first tick, which from a run configuration looks like the writer vanishing rather than
	// reporting anything. The precondition: the lattice a second run walks does produce the edge row again.
	[Fact]
	public async Task ARestartOnALatticeAlignedEdgeAppendsWithoutADuplicateKey()
	{
		Fixture.RequireAvailable();

		var cancellationToken = TestContext.Current.CancellationToken;

		Assert.Contains(
			LiveTailGenerator.Generate(Options(), _firstRunStart, _restartClock),
			row => row.Timestamp == Edge);

		var secondRunRows = await ResumeAsync(cancellationToken);
		await Writer().WriteAsync(
			secondRunRows,
			secondRunRows.Min(row => row.Timestamp),
			_restartClock,
			allowExistingRows: true,
			cancellationToken);

		Assert.Equal(
			(long)(_firstRunRows.Count + secondRunRows.Count),
			await CountRawRowsAsync(cancellationToken));
	}

	// The other half of the fix: resuming past the edge must not open a hole at the seam. The next lattice
	// point is inside one change interval of the edge, and every pen crosses the restart with no gap a raw
	// window would draw.
	[Fact]
	public async Task TheFirstRowAfterTheRestartIsWithinOneChangeIntervalOfTheEdge()
	{
		Fixture.RequireAvailable();

		var secondRunRows = await ResumeAsync(TestContext.Current.CancellationToken);

		Assert.Equal(
			_firstRunRows.Select(row => row.Id).Distinct().Order(),
			secondRunRows.Select(row => row.Id).Distinct().Order());

		foreach (var pen in secondRunRows.GroupBy(row => row.Id))
		{
			var seam = pen.Min(row => row.Timestamp) - Edge;

			Assert.InRange(seam, TimeSpan.FromMilliseconds(1.0), ChangeInterval);
		}
	}

	private static TimeSpan ChangeInterval => TimeSpan.FromSeconds(ChangeSeconds);

	private static FollowOptions Options()
	{
		return new("Host=localhost;Database=archive", TimeSpan.FromSeconds(1), PenCount, 1L, ChangeSeconds);
	}

	// What Program.FollowAsync does before its first tick: read the archive's edge once, then open the first
	// window after it. Both pieces are the production ones, so a resume that lands back on the edge reaches
	// the COPY here exactly as it reaches it in a run.
	private async Task<IReadOnlyList<ArchiveRow>> ResumeAsync(CancellationToken cancellationToken)
	{
		var freshness = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, _restartClock, cancellationToken);
		Assert.Equal(Edge, freshness);

		var rows = LiveTailGenerator.Generate(Options(), freshness!.Value, _restartClock);

		Assert.NotEmpty(rows);

		return rows;
	}

	private async Task<long> CountRawRowsAsync(CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(_countRawRowsCommand, connection);

		return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
	}
}
