using AwesomeAssertions;

using Npgsql;

using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The operator's own sequence: a demo writer runs, is stopped, and a second one starts against the
// archive the first left. Gated because the failure is a 23505 under ArchiveWriter's binary COPY, and
// the case writes, so the class takes its own clone; SharedLatticeTests mirrors the restart in memory.
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

	// What Program.FollowAsync does before its first tick: read the archive's edge once, then open the first
	// window after it. Both pieces are the production ones, so a resume that lands back on the edge reaches
	// the COPY here exactly as it reaches it in a run.
	[Fact]
	public async Task ARestartOnALatticeAlignedEdgeAppendsWithoutADuplicateKey()
	{
		var cancellationToken = TestContext.Current.CancellationToken;

		// The precondition: the lattice a second run walks does produce the edge row again.
		LiveTailGenerator.Generate(Options(), _firstRunStart, _restartClock).Should().Contain(
			row => row.Timestamp == Edge);

		var freshness = await StaleArchiveGuard.CheckAsync(
			Database.WriterConnectionString, _restartClock, cancellationToken);
		freshness.Should().Be(Edge);

		var secondRunRows = LiveTailGenerator.Generate(Options(), freshness!.Value, _restartClock);

		secondRunRows.Should().NotBeEmpty();

		await Writer().WriteAsync(
			secondRunRows,
			secondRunRows.Min(row => row.Timestamp),
			_restartClock,
			allowExistingRows: true,
			cancellationToken);

		(await CountRawRowsAsync(cancellationToken)).Should().Be(_firstRunRows.Count + secondRunRows.Count);
	}

	private static FollowOptions Options()
	{
		return new("Host=localhost;Database=archive", TimeSpan.FromSeconds(1), PenCount, 1L, ChangeSeconds);
	}

	private async Task<long> CountRawRowsAsync(CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(Database.WriterConnectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand(_countRawRowsCommand, connection);

		return (long)(await command.ExecuteScalarAsync(cancellationToken))!;
	}
}
