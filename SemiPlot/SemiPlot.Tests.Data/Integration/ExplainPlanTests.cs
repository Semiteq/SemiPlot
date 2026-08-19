using System.Text.RegularExpressions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Data.Integration;

// The gated tests that read a query plan instead of a result. EXPLAIN without ANALYZE executes
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

	// A strict subset of the eight seeded pens, so the explained shape is the one production issues: a
	// caller asks for the pens it draws, never for every configured variable.
	private const int ExplainedPenCount = 2;

	// The seeder's day partitions tpYYYYmMMdDD (SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs) are the
	// ones holding rows. tpdefault is deliberately outside the pattern: it is empty by design
	// (sql/semiplot_dev.sql), and the planner may legitimately read an empty analysed partition
	// sequentially, so an assertion covering it would fail a correct plan.
	private const string DayPartition = @"(public\.)?tp\d{4}m\d{2}d\d{2}\b";

	private static readonly Regex _sequentialScanOverRows = new(@"Seq Scan on " + DayPartition);

	// No index name is asserted: trends is PARTITION BY RANGE (t), so tpk is the parent index and never
	// scanned, and each partition's own index carries a generated name. Backward is what a max(t) subquery
	// reaches its edge through.
	private const string IndexScan = @"Index (Only )?Scan( Backward)? using \S+ on ";

	// The extent statement is held to the strict form. Its per-variable subqueries reach a bound by
	// stepping to one index edge and stopping, which is an index scan and nothing else; a bitmap here would
	// mean the plan gave that up and started collecting a day partition's rows before reducing them, the
	// regression this assertion exists to catch.
	private static readonly Regex _indexEdgeReachedRows = new(IndexScan + DayPartition);

	// The windowed read is held to the looser form, because it reads a range rather than an edge and a
	// Bitmap Heap Scan is by construction driven by a Bitmap Index Scan. Either way the rows are found
	// through an index rather than by reading rows nobody wants, and which of the two the planner picks
	// over a two-minute window is its own decision.
	private static readonly Regex _indexReachedRows = new(
		"(" + IndexScan + @"|Bitmap Heap Scan on )" + DayPartition);

	// Narrow, so the planner is choosing between an index and a scan of a partition it would have to read
	// almost whole.
	private static readonly TimeSpan _explainedWindow = TimeSpan.FromMinutes(2);

	[Fact]
	public async Task TheExtentPlanReachesEveryRowHoldingPartitionThroughAnIndex()
	{
		postgresContainerFixture.RequireAvailable();

		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.ArchiveExtent,
			null,
			TestContext.Current.CancellationToken);

		Assert.False(
			_sequentialScanOverRows.IsMatch(plan),
			"The extent statement's per-variable bounded subqueries have been lost: the plan reads a "
				+ "row-holding trends partition sequentially, so the read now walks the whole archive "
				+ $"instead of stepping to one index edge per variable.{Environment.NewLine}{plan}");

		Assert.True(
			_indexEdgeReachedRows.Matches(plan).Count >= BoundedSubqueryCount,
			"The extent plan reaches no row-holding trends partition through an index scan. Each of the "
				+ "two per-variable bounded subqueries must step to one index edge; a bitmap or a plan "
				+ $"without them reads rows the bound does not need.{Environment.NewLine}{plan}");
	}

	[Fact]
	public async Task TheWindowedHistoryPlanReachesItsRowsThroughAnIndex()
	{
		postgresContainerFixture.RequireAvailable();

		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.SparseHistoryWindow,
			BindWindowParameters,
			TestContext.Current.CancellationToken);

		Assert.False(
			_sequentialScanOverRows.IsMatch(plan),
			"The windowed history statement no longer narrows on the primary key: the plan reads a "
				+ "row-holding trends partition sequentially, so a two-minute window over two pens walks "
				+ $"the whole day.{Environment.NewLine}{plan}");

		Assert.True(
			_indexReachedRows.IsMatch(plan),
			"The windowed history plan reaches no row-holding trends partition through an index, neither "
				+ "directly nor through a bitmap, so the read finds its rows by reading rows it does not "
				+ $"want.{Environment.NewLine}{plan}");
	}

	// Bound through the shipped binder, so the plan is read over the parameters production sends. The
	// statement's bounds are 'timestamp without time zone', the archive's own naive wall clock, and a UTC
	// converter leaves the seeder's timestamps unchanged on the way to the parameter.
	private static void BindWindowParameters(NpgsqlCommand command)
	{
		var from = DateTime.SpecifyKind(ArchiveTemplate.Slice.Start, DateTimeKind.Utc);

		PostgresDataProvider.BindWindow(
			command,
			new ArchiveTimeConverter(TimeZoneInfo.Utc),
			ExplainedPenIds(),
			from,
			from + _explainedWindow,
			AggregationLayer.Raw);
	}

	private static int[] ExplainedPenIds()
	{
		return RawLayerGenerator.SelectPens(ArchiveTemplate.Slice.PenCount)
			.Take(ExplainedPenCount)
			.Select(pen => (int)pen.PenId)
			.ToArray();
	}

	private static async Task AnalyseAsync(ArchiveDatabase database, CancellationToken cancellationToken)
	{
		await ArchiveDatabase.ExecuteAsync(database.AdminConnectionString, AnalyseTrendsCommand, cancellationToken);

		await ArchiveDatabase.ExecuteAsync(database.AdminConnectionString, AnalyseCatalogCommand, cancellationToken);
	}

	// EXPLAIN runs as semiplot_reader, the role production reads with. A statement carrying parameters has
	// to arrive with them bound: Npgsql sends a one-shot extended-protocol statement, so the server plans it
	// against the actual values rather than against a generic plan no read ever executes.
	private static async Task<string> ExplainAsync(
		string connectionString,
		string statement,
		Action<NpgsqlCommand>? bindParameters,
		CancellationToken cancellationToken)
	{
		await using var connection = new NpgsqlConnection(connectionString);

		await connection.OpenAsync(cancellationToken);

		await using var command = new NpgsqlCommand("EXPLAIN " + statement, connection);

		bindParameters?.Invoke(command);

		await using var reader = await command.ExecuteReaderAsync(cancellationToken);

		var lines = new List<string>();

		while (await reader.ReadAsync(cancellationToken))
		{
			lines.Add(reader.GetString(0));
		}

		return string.Join(Environment.NewLine, lines);
	}
}
