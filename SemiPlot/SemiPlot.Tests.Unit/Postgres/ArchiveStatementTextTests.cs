using System.Text.RegularExpressions;

using AwesomeAssertions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Unit.Postgres;

// Why each clause is written the way it is lives on the constant itself, in `ArchiveStatements.cs`.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatementTextTests
{
	// Npgsql strips the sigil, so the command carries "ids" where the statement carries "@ids".
	private static readonly Regex _parameterTokenPattern = new(@"@(\w+)");

	[Theory]
	// BucketedRowFold groups by consecutive identifier and needs one ascending run per pen, and its rows
	// arrive from a GROUP BY whose output order is the planner's business.
	[InlineData(ArchiveStatements.BucketedHistoryWindow, "ORDER BY id, t;")]
	[InlineData(ArchiveStatements.ExportSeedRow, "ORDER BY t DESC\nLIMIT 1;")]
	// A poll returns every subscribed pen in one result. The index scan behind it happens to yield
	// ascending time per identifier, so the loss of this ordering shows up only once the planner picks
	// another path.
	[InlineData(ArchiveStatements.RealtimePoll, "ORDER BY t;")]
	// The export writes the slice rows in the order they arrive, so the ascending order is the statement's,
	// with the checklist order breaking a tie.
	[InlineData(ArchiveStatements.ExportSliceRows, "ORDER BY t, array_position(@ids, id);")]
	public void AStatementEndsWithItsOuterOrdering(string statement, string ordering)
	{
		statement.ReplaceLineEndings("\n").Should().EndWith(ordering);
	}

	[Fact]
	public void TheExportSeedBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.ExportSeedRow);

		PostgresArchiveRowReader.BindSeed(command, 1, new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));

		AssertBinderNamesTheStatementsOwnParameters(command, ArchiveStatements.ExportSeedRow);
	}

	[Theory]
	[InlineData(ArchiveStatements.ExportSliceRows)]
	[InlineData(ArchiveStatements.ExportRowCount)]
	public void ThePensRangeBinderNamesExactlyTheParametersOfEachStatementItBinds(string statement)
	{
		using var command = new NpgsqlCommand(statement);

		PostgresArchiveRowReader.BindPensRange(
			command,
			[1, 2],
			new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
			new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Unspecified));

		AssertBinderNamesTheStatementsOwnParameters(command, statement);
	}

	[Theory]
	[InlineData(ArchiveStatements.ExportSeedRow)]
	[InlineData(ArchiveStatements.ExportSliceRows)]
	[InlineData(ArchiveStatements.ExportRowCount)]
	public void NoExportStatementSelectsTheQualityColumn(string statement)
	{
		statement.Should().NotMatchRegex(@"\bq\b");
	}

	[Theory]
	// Without the segment in the grouping key a bucket wider than a break spans it, and the fold anchors
	// the gap after a sample the plant recorded once archiving had resumed.
	[InlineData(ArchiveStatements.BucketedHistoryWindow, new[] { "GROUP BY id, segment, date_bin(@bucket, t, @from)" })]
	// min, max and the newest-value aggregate all skip a null, so without the flag a bucket holding data
	// and nulls comes back as an ordinary column and the line is drawn straight across the null run,
	// where the fold would otherwise write its gap column.
	[InlineData(
		ArchiveStatements.BucketedHistoryWindow,
		new[] { "bool_or(q = 32 OR v IS NULL) AS breaks", "(seed.q = 32 OR seed.v IS NULL) AS breaks" })]
	// The value is the bucket's newest non-null sample, so the timestamp beside it has to be that sample's
	// own: max(t) alone would report a value at a moment the archive stored NULL at.
	[InlineData(
		ArchiveStatements.BucketedHistoryWindow,
		new[] { "coalesce(max(t) FILTER (WHERE v IS NOT NULL), max(t)) AS t" })]
	// Both branches read the requested layer, so a coarse window is not seeded from a raw row.
	[InlineData(
		ArchiveStatements.BucketedHistoryWindow,
		new[] { "AND l = @layer AND t >= @from AND t < @to", "prior.l = @layer" })]
	// The export reads the main layer only: a coarse row is a derived aggregate, not a stored sample.
	[InlineData(ArchiveStatements.ExportSeedRow, new[] { "l = 0" })]
	[InlineData(ArchiveStatements.ExportSliceRows, new[] { "l = 0" })]
	[InlineData(ArchiveStatements.ExportRowCount, new[] { "l = 0" })]
	[InlineData(ArchiveStatements.ExportSliceRows, new[] { "id = ANY(@ids)" })]
	// The window branch takes `t >= @from`, so an inclusive seed bound returns the boundary row on both
	// branches.
	[InlineData(ArchiveStatements.BucketedHistoryWindow, new[] { "prior.t < @from" })]
	// The backwards seek is bounded by the wider of the requested window and one partition width. Drop
	// the floor and a pen quiet for days vanishes from the window instead of drawing at its last value.
	[InlineData(ArchiveStatements.BucketedHistoryWindow, new[] { "greatest(@to - @from, interval '1 day')" })]
	// A poll feeds the live edge, which draws raw samples. Without the layer filter the same tick also
	// returns the coarse rows LayerThinner writes for the same instants, and the fold reads them as
	// further samples of the same pen.
	[InlineData(ArchiveStatements.RealtimePoll, new[] { "AND l = 0" })]
	// The baseline is the instant the first poll starts from, so it has to be the newest *raw* row. Read
	// across every layer it would answer with a coarse timestamp, and the poll's strict `t > @lastSeen`
	// would then skip the raw samples already written under it.
	[InlineData(ArchiveStatements.RealtimeBaseline, new[] { "AND l = 0" })]
	// A caller may hand the same identifier twice, since nothing upstream deduplicates a pen list.
	// Without DISTINCT the lateral join runs once per copy and the baseline carries one row per copy,
	// which the fold reads as a second pen.
	[InlineData(ArchiveStatements.RealtimeBaseline, new[] { "DISTINCT unnest(@ids)" })]
	[InlineData(ArchiveStatements.ExportSeedRow, new[] { "t < @from" })]
	[InlineData(ArchiveStatements.ExportRowCount, new[] { "prior.t < @from" })]
	[InlineData(ArchiveStatements.ExportSeedRow, new[] { "t >= @from - interval '1 day'" })]
	[InlineData(ArchiveStatements.ExportRowCount, new[] { "prior.t >= @from - interval '1 day'" })]
	[InlineData(ArchiveStatements.ExportSliceRows, new[] { "t >= @from AND t < @to" })]
	[InlineData(ArchiveStatements.ExportRowCount, new[] { "t >= @from AND t < @to" })]
	[InlineData(
		ArchiveStatements.ExportRowCount, new[] { "ORDER BY prior.t DESC", "LIMIT 1", "DISTINCT unnest(@ids)" })]
	public void AStatementCarriesTheClauseItsReaderDependsOn(string statement, string[] fragments)
	{
		foreach (var fragment in fragments)
		{
			statement.Should().Contain(fragment);
		}
	}

	[Fact]
	public void TheBucketedWindowBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.BucketedHistoryWindow);

		PostgresDataProvider.BindLocalBucketedWindow(
			command,
			[1, 2],
			new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified),
			new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Unspecified),
			AggregationLayer.Minute,
			TimeSpan.FromMinutes(1));

		AssertBinderNamesTheStatementsOwnParameters(command, ArchiveStatements.BucketedHistoryWindow);
	}

	// A bucket is the window divided by the column target, and a window narrower than the target's own
	// millisecond floor would otherwise bind an interval of zero, which date_bin rejects.
	[Fact]
	public void TheBucketFloorsAtOneMillisecond()
	{
		var from = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

		PostgresDataProvider.BucketFor(from, from.AddMilliseconds(10), 64).Should().Be(TimeSpan.FromMilliseconds(1));
	}

	[Fact]
	public void TheBucketIsTheWindowDividedByTheColumnTarget()
	{
		var from = new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified);

		PostgresDataProvider.BucketFor(from, from.AddHours(1), 60).Should().Be(TimeSpan.FromMinutes(1));
	}

	[Fact]
	public void ThePollBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.RealtimePoll);

		RealtimePoll.BindPoll(command, [1, 2], new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Unspecified));

		AssertBinderNamesTheStatementsOwnParameters(command, ArchiveStatements.RealtimePoll);
	}

	[Fact]
	public void TheBaselineBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.RealtimeBaseline);

		RealtimePoll.BindBaseline(command, [1, 2]);

		AssertBinderNamesTheStatementsOwnParameters(command, ArchiveStatements.RealtimeBaseline);
	}

	// A tick must not inherit the connection string's five-minute backstop, or a server that accepts and
	// then answers nothing would freeze the chart for minutes before the fault threshold banner shows.
	[Theory]
	[InlineData(ArchiveStatements.RealtimeBaseline)]
	[InlineData(ArchiveStatements.RealtimePoll)]
	public void ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop(string statementText)
	{
		const int TickBoundSeconds = 10;

		using var connection = new NpgsqlConnection();

		using var command = RealtimePoll.CreateTickCommand(statementText, connection);

		command.CommandTimeout.Should().Be(TickBoundSeconds);
	}

	private static void AssertBinderNamesTheStatementsOwnParameters(NpgsqlCommand command, string statement)
	{
		var bound = command.Parameters
			.Select(parameter => parameter.ParameterName)
			.Order(StringComparer.Ordinal)
			.ToArray();

		var declared = _parameterTokenPattern.Matches(statement)
			.Select(match => match.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		declared.Should().NotBeEmpty();
		bound.Should().Equal(declared);
	}
}
