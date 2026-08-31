using System.Text.RegularExpressions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Why each clause is written the way it is lives on the constant itself, in `ArchiveStatements.cs`.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatementTextTests
{
	// Npgsql strips the sigil, so the command carries "ids" where the statement carries "@ids".
	private static readonly Regex _parameterTokenPattern = new(@"@(\w+)");

	// HistoryRowFold groups by consecutive identifier and needs one ascending run per pen. Only the
	// single outer ordering guarantees it: ExplainPlanTests asserts no ordering at all, and an index
	// scan's order matches by accident.
	[Fact]
	public void TheSeededWindowEndsWithOneOuterOrdering()
	{
		Assert.EndsWith("ORDER BY id, t;", ArchiveStatements.SparseHistoryWindow, StringComparison.Ordinal);
	}

	// The window branch takes `t >= @from`, so an inclusive seed bound returns the boundary row on both
	// branches.
	[Fact]
	public void TheSeededWindowsSeamBoundIsStrict()
	{
		Assert.Contains("prior.t < @from", ArchiveStatements.SparseHistoryWindow, StringComparison.Ordinal);
	}

	// The backwards seek is bounded by the wider of the requested window and one partition width. Drop
	// the floor and a pen quiet for days vanishes from the window instead of drawing at its last value.
	[Fact]
	public void TheSeededWindowsSeedBoundKeepsItsOneDayFloor()
	{
		Assert.Contains(
			"greatest(@to - @from, interval '1 day')",
			ArchiveStatements.SparseHistoryWindow,
			StringComparison.Ordinal);
	}

	// A poll feeds the live edge, which draws raw samples. Without the layer filter the same tick also
	// returns the coarse rows LayerThinner writes for the same instants, and the fold reads them as
	// further samples of the same pen.
	[Fact]
	public void ThePollReadsTheRawLayerAlone()
	{
		Assert.Contains("AND l = 0", ArchiveStatements.RealtimePoll, StringComparison.Ordinal);
	}

	// HistoryRowFold takes one ascending run per pen, and a poll returns every subscribed pen in one
	// result. The index scan behind the poll happens to yield ascending time per identifier, so the loss
	// of this ordering shows up only once the planner picks another path.
	[Fact]
	public void ThePollEndsWithItsAscendingTimeOrdering()
	{
		Assert.EndsWith("ORDER BY t;", ArchiveStatements.RealtimePoll, StringComparison.Ordinal);
	}

	// The baseline is the instant the first poll starts from, so it has to be the newest *raw* row. Read
	// across every layer it would answer with a coarse timestamp, and the poll's strict `t > @lastSeen`
	// would then skip the raw samples already written under it.
	[Fact]
	public void TheBaselineTakesItsMaximumFromTheRawLayerAlone()
	{
		Assert.Contains("AND l = 0", ArchiveStatements.RealtimeBaseline, StringComparison.Ordinal);
	}

	// A caller may hand the same identifier twice — nothing upstream deduplicates a pen list. Without
	// DISTINCT the lateral join runs once per copy and the baseline carries one row per copy, which the
	// fold reads as a second pen.
	[Fact]
	public void TheBaselineDeduplicatesTheRequestedIdentifiers()
	{
		Assert.Contains("DISTINCT unnest(@ids)", ArchiveStatements.RealtimeBaseline, StringComparison.Ordinal);
	}

	// The statement names the partition and so does the warning the reader raises. They are two constants,
	// and an operator sent to an object the read never touched is worse than no warning at all.
	[Fact]
	public void TheDefaultPartitionOccupancyStatementReadsTheRelationTheWarningNames()
	{
		Assert.Contains(
			ArchiveStatements.DefaultPartitionRelation,
			ArchiveStatements.DefaultPartitionOccupancy,
			StringComparison.Ordinal);
	}

	// The drift that breaks production is the binder naming a parameter the statement does not.
	[Fact]
	public void TheWindowBinderNamesExactlyTheStatementsOwnParameters()
	{
		using var command = new NpgsqlCommand(ArchiveStatements.SparseHistoryWindow);

		PostgresDataProvider.BindWindow(
			command,
			new ArchiveTimeConverter(TimeZoneInfo.Utc),
			[1, 2],
			new DateTime(2026, 1, 2, 0, 0, 0, DateTimeKind.Utc),
			new DateTime(2026, 1, 2, 1, 0, 0, DateTimeKind.Utc),
			AggregationLayer.Raw);

		AssertBinderNamesTheStatementsOwnParameters(command, ArchiveStatements.SparseHistoryWindow);
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

		Assert.NotEmpty(declared);
		Assert.Equal(declared, bound);
	}
}
