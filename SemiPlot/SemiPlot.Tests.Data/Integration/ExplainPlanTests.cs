using System.Text.RegularExpressions;

using Npgsql;

using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The one gated test that reads a query plan instead of a result. EXPLAIN without ANALYZE executes
// nothing and ANALYZE writes no rows, so this class shares the SeededArchive clone and honours its
// leave-it-as-you-found-it contract.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ExplainPlanTests(
	PostgresContainerFixture postgresContainerFixture,
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	// The template is built by COPY and never analysed, and autovacuum has not run seconds after
	// CREATE DATABASE ... TEMPLATE. With no statistics the planner may pick a sequential scan over a
	// one-day partition and the test would fail for a reason unrelated to query shape. ANALYZE needs
	// table ownership or MAINTAIN, neither of which semiplot_reader holds, so the admin connection is
	// not a convenience here but the only role that can run it.
	private const string AnalyseTrendsCommand = "ANALYZE public.trends;";

	private const string AnalyseCatalogCommand = "ANALYZE public.semiplot_tags;";

	// One per per-variable subquery: the extent statement carries a bounded min(t) and a bounded max(t)
	// per configured variable.
	private const int BoundedSubqueryCount = 2;

	// The seeder's day partitions tpYYYYmMMdDD (SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs) are the
	// ones holding rows. tpdefault is deliberately outside the pattern: it is empty by design
	// (sql/semiplot_dev.sql), and the planner may legitimately read an empty analysed partition
	// sequentially, so an assertion covering it would fail a correct plan.
	private const string DayPartition = @"(public\.)?tp\d{4}m\d{2}d\d{2}\b";

	private static readonly Regex _sequentialScanOverRows = new(@"Seq Scan on " + DayPartition);

	// No index name is asserted. Backward is what a max(t) subquery reaches its edge through.
	private static readonly Regex _indexScanOverRows = new(
		@"Index (Only )?Scan( Backward)? using \S+ on " + DayPartition);

	[Fact]
	public async Task TheExtentPlanReachesEveryRowHoldingPartitionThroughAnIndex()
	{
		postgresContainerFixture.RequireAvailable();

		var database = seededArchive.Database;

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			AnalyseTrendsCommand,
			TestContext.Current.CancellationToken);

		await ArchiveDatabase.ExecuteAsync(
			database.AdminConnectionString,
			AnalyseCatalogCommand,
			TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.ArchiveExtent,
			TestContext.Current.CancellationToken);

		Assert.False(
			_sequentialScanOverRows.IsMatch(plan),
			"The extent statement's per-variable bounded subqueries have been lost: the plan reads a "
				+ "row-holding trends partition sequentially, so the read now walks the whole archive "
				+ $"instead of stepping to one index edge per variable.{Environment.NewLine}{plan}");

		Assert.True(
			_indexScanOverRows.Matches(plan).Count >= BoundedSubqueryCount,
			"The extent plan reaches no row-holding trends partition through an index. Each of the two "
				+ "per-variable bounded subqueries must contribute at least one index scan; a plan "
				+ $"without them scans the whole archive.{Environment.NewLine}{plan}");
	}

	// EXPLAIN runs as semiplot_reader, the role production reads with.
	private static async Task<string> ExplainAsync(
		string connectionString,
		string statement,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand("EXPLAIN " + statement, connection);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		var lines = new List<string>();

		while (await reader.ReadAsync(cancellationToken))
		{
			lines.Add(reader.GetString(0));
		}

		return string.Join(Environment.NewLine, lines);
	}
}
