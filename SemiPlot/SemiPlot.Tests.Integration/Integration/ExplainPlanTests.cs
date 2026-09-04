using System.Text.RegularExpressions;

using AwesomeAssertions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;
using SemiPlot.Tools.ArchiveSeeder;

using Xunit;

namespace SemiPlot.Tests.Integration;

// The gated tests that read a query plan instead of a result. EXPLAIN without ANALYZE executes
// nothing and ANALYZE writes no rows, so this class shares the SeededArchive clone and honours its
// leave-it-as-you-found-it contract.
[Collection(ArchiveDatabaseCollection.Name)]
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Integration")]
public sealed class ExplainPlanTests(
	SeededArchive seededArchive)
	: IClassFixture<SeededArchive>
{
	// The template is built by COPY and never analysed, so the planner may pick a sequential scan with
	// no stats. ANALYZE needs table ownership or MAINTAIN, which only the admin connection holds.
	private const string AnalyseTrendsCommand = "ANALYZE public.trends;";

	private const string AnalyseCatalogCommand = "ANALYZE public.semiplot_tags;";

	// One per per-variable subquery: the extent statement carries a bounded min(t) and a bounded max(t)
	// per configured variable.
	private const int BoundedSubqueryCount = 2;

	// A strict subset of the eight seeded pens, so the explained shape is the one production issues: a
	// caller asks for the pens it draws, never for every configured variable.
	private const int ExplainedPenCount = 2;

	// tpdefault is outside the pattern: empty and analysed, a Seq Scan on it is a correct plan.
	private const string DayPartition = @"(public\.)?tp\d{4}m\d{2}d\d{2}\b";

	private static readonly Regex _sequentialScanOverRows = new(@"Seq Scan on " + DayPartition);

	// No index name is asserted: trends is PARTITION BY RANGE (t), so tpk is the parent index and never
	// scanned, and each partition's own index carries a generated name. Backward is what a max(t) subquery
	// reaches its edge through.
	private const string IndexScan = @"Index (Only )?Scan( Backward)? using \S+ on ";

	// Strict form: a bitmap here means the plan collects rows before reducing them.
	private static readonly Regex _indexEdgeReachedRows = new(IndexScan + DayPartition);

	// Looser form: a range read may legitimately go through a Bitmap Heap Scan.
	private static readonly Regex _indexReachedRows = new(
		"(" + IndexScan + @"|Bitmap Heap Scan on )" + DayPartition);

	// Every scan node naming a day partition, whatever its kind. The partition's own index is
	// tpYYYYmMMdDD_pkey and the underscore keeps the word boundary from closing after the day, so
	// "Bitmap Index Scan on tp2026m01d01_pkey" is not counted as a partition read.
	private static readonly Regex _dayPartitionRead = new(@"\bon " + DayPartition);

	// The seed's backward walk together with the index condition on the line under it, captured so the
	// bound the planner actually pushed into the index is read rather than inferred. The condition is a
	// named group because DayPartition carries a group of its own.
	private static readonly Regex _seedBackwardWalk = new(
		@"Index Scan Backward using \S+ on " + DayPartition
			+ @"[^\r\n]*\r?\n\s*Index Cond: \((?<condition>[^\r\n]*)\)");

	// Narrow, so the planner is choosing between an index and a scan of a partition it would have to read
	// almost whole.
	private static readonly TimeSpan _explainedWindow = TimeSpan.FromMinutes(2);

	// How far back from the archive's end the explained poll's bound sits. A tick reads what was written
	// since the previous one, so its span is the poll interval and never a slice of the archive.
	private static readonly TimeSpan _polledTail = TimeSpan.FromMinutes(1);

	// Far enough into the archive that every explained pen has a row before the window opens, so the seed
	// branch has something to find.
	private static readonly TimeSpan _seedProbeOffset = TimeSpan.FromMinutes(5);

	// The seed's look-back over a two-minute window is its floor of one partition width, so it reaches the
	// window's own day and the day before it and no further. An unbounded backwards seek would name one
	// partition per older day instead.
	private const int MaximumDayPartitionsRead = 2;

	// A pen with no row before the window costs no read of a row-holding partition at all: the look-back
	// ends exactly on the archive's first partition boundary, so the seed branch prunes to the empty
	// default partition and the window branch is the plan's only partition read.
	private const int WindowBranchPartitionReads = 1;

	[Fact]
	public async Task TheExtentPlanReachesEveryRowHoldingPartitionThroughAnIndex()
	{
		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.ArchiveExtent,
			null,
			TestContext.Current.CancellationToken);

		_sequentialScanOverRows.IsMatch(plan).Should().BeFalse(
			"The extent statement's per-variable bounded subqueries have been lost: the plan reads a "
				+ "row-holding trends partition sequentially, so the read now walks the whole archive "
				+ $"instead of stepping to one index edge per variable.{Environment.NewLine}{plan}");

		(_indexEdgeReachedRows.Matches(plan).Count >= BoundedSubqueryCount).Should().BeTrue(
			"The extent plan reaches no row-holding trends partition through an index scan. Each of the "
				+ "two per-variable bounded subqueries must step to one index edge; a bitmap or a plan "
				+ $"without them reads rows the bound does not need.{Environment.NewLine}{plan}");
	}

	[Fact]
	public async Task TheSeededWindowPlanWalksBackToTheSeedWithinItsBound()
	{
		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.SparseHistoryWindow,
			command => BindWindowParametersAt(command, ArchiveTemplate.Slice.Start + _seedProbeOffset),
			TestContext.Current.CancellationToken);

		_sequentialScanOverRows.IsMatch(plan).Should().BeFalse(
			"The seeded window statement no longer narrows on the primary key: the plan reads a "
				+ "row-holding trends partition sequentially, so a two-minute window over two pens walks "
				+ $"the whole day.{Environment.NewLine}{plan}");

		_indexReachedRows.IsMatch(plan).Should().BeTrue(
			"The seeded window plan reaches no row-holding trends partition through an index, neither "
				+ "directly nor through a bitmap, so the read finds its rows by reading rows it does not "
				+ $"want.{Environment.NewLine}{plan}");

		var seedWalk = _seedBackwardWalk.Match(plan);

		seedWalk.Success.Should().BeTrue(
			"The seed branch reaches no row-holding trends partition through a backward index scan, so "
				+ "the row before the window is no longer found by stepping to one index edge."
				+ $"{Environment.NewLine}{plan}");

		seedWalk.Groups["condition"].Value.Should().Contain("t >=");

		(_dayPartitionRead.Matches(plan).Count <= MaximumDayPartitionsRead).Should().BeTrue(
			"The seed's backwards seek has lost its lower bound: the plan reads more day partitions than "
				+ "a look-back of one partition width can reach, which on a longer archive is one probe "
				+ $"per older day, per pen, on every window change.{Environment.NewLine}{plan}");
	}

	[Fact]
	public async Task TheSeededWindowPlanReadsNoOlderPartitionForAPenWithNoPriorRows()
	{
		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.SparseHistoryWindow,
			command => BindWindowParametersAt(command, ArchiveTemplate.Slice.Start),
			TestContext.Current.CancellationToken);

		_sequentialScanOverRows.IsMatch(plan).Should().BeFalse(
			"The seeded window statement no longer narrows on the primary key at the archive's first "
				+ $"instant: the plan reads a row-holding trends partition sequentially.{Environment.NewLine}{plan}");

		_indexReachedRows.IsMatch(plan).Should().BeTrue(
			"The window branch reaches no row-holding trends partition through an index at the archive's "
				+ $"first instant.{Environment.NewLine}{plan}");

		var partitionReads = _dayPartitionRead.Matches(plan).Count;

		(partitionReads == WindowBranchPartitionReads).Should().BeTrue(
			$"The plan reads {partitionReads} day partitions where the window branch alone accounts for "
				+ $"{WindowBranchPartitionReads}: the seed's look-back ends on the archive's first "
				+ "partition boundary, so a pen with no row before the window must cost no read of a "
				+ $"row-holding partition at all.{Environment.NewLine}{plan}");
	}

	// The poll's own index plan.
	[Fact]
	public async Task ThePollPlanReachesItsRowsThroughAnIndex()
	{
		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.RealtimePoll,
			command => RealtimePoll.BindPoll(command, ExplainedPenIds(), PolledFrom()),
			TestContext.Current.CancellationToken);

		_sequentialScanOverRows.IsMatch(plan).Should().BeFalse(
			"The realtime poll no longer narrows on the primary key: the plan reads a row-holding trends "
				+ "partition sequentially, so every tick walks the current day instead of stepping into an "
				+ $"index.{Environment.NewLine}{plan}");

		_indexReachedRows.IsMatch(plan).Should().BeTrue(
			"The realtime poll plan reaches no row-holding trends partition through an index, neither "
				+ "directly nor through a bitmap, so a tick finds its rows by reading rows it does not "
				+ $"want.{Environment.NewLine}{plan}");
	}

	// The baseline is held to the strict index-edge form, the same one the extent statement is held to.
	[Fact]
	public async Task TheBaselinePlanReachesEachBoundByAnIndexEdge()
	{
		var database = seededArchive.Database;

		await AnalyseAsync(database, TestContext.Current.CancellationToken);

		var plan = await ExplainAsync(
			database.ReaderConnectionString,
			ArchiveStatements.RealtimeBaseline,
			command => RealtimePoll.BindBaseline(command, ExplainedPenIds()),
			TestContext.Current.CancellationToken);

		_sequentialScanOverRows.IsMatch(plan).Should().BeFalse(
			"The baseline statement's per-variable bounded subquery has been lost: the plan reads a "
				+ "row-holding trends partition sequentially, so establishing a subscription's starting "
				+ $"point walks the archive.{Environment.NewLine}{plan}");

		_indexEdgeReachedRows.IsMatch(plan).Should().BeTrue(
			"The baseline plan reaches no row-holding trends partition through an index scan. The lateral "
				+ "subquery must step to one index edge per variable; a bitmap or a plan without it reads "
				+ $"rows the bound does not need.{Environment.NewLine}{plan}");
	}

	// Near the archive's end, which is where a live subscription's bound always sits: the poll reads the
	// tail written since the previous tick, never a span of the archive.
	private static DateTime PolledFrom()
	{
		return ArchiveTemplate.Slice.End - _polledTail;
	}

	// Bound through the shipped binder, over a UTC converter so the seeder's timestamps pass unchanged.
	private static void BindWindowParametersAt(NpgsqlCommand command, DateTime windowStart)
	{
		var from = DateTime.SpecifyKind(windowStart, DateTimeKind.Utc);

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
			.Select(pen => pen.PenId)
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
