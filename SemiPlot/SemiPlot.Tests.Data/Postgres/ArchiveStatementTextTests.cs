using System.Text.RegularExpressions;

using Npgsql;

using SemiPlot.Core.Trends;
using SemiPlot.DataSource.Postgres;

using Xunit;

namespace SemiPlot.Tests.Data.Postgres;

// Each operational statement is pinned by a plain literal held in this file and compared character for
// character against the constant in `ArchiveStatements.cs`, so an edit to the shipped SQL fails here.
// `EffectiveStatementTimeout` carries none: it is a cold-path diagnostic that runs only after a read has
// already failed. A new operational statement gains a literal here.
[Trait("Component", "Core")]
[Trait("Area", "Data")]
[Trait("Category", "Unit")]
public sealed class ArchiveStatementTextTests
{
	// Npgsql strips the sigil, so the command carries "ids" where the statement carries "@ids".
	private static readonly Regex _parameterTokenPattern = new(@"@(\w+)");

	// `ORDER BY coalesce(group_name, '')` is load-bearing: the row read projects a null group onto the empty
	// string, and PostgreSQL sorts nulls last while the empty string sorts first, so ordering on the raw
	// column would return a list not ordered by its own values.
	private const string PenCatalogStatement = """
		SELECT id, name, group_name, color, line_style
		FROM semiplot_tags
		ORDER BY coalesce(group_name, ''), name;
		""";

	[Fact]
	public void ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter()
	{
		Assert.Equal(PenCatalogStatement, ArchiveStatements.PenCatalog);
	}

	// The lateral pair of scalar subqueries is load-bearing: each one is an index probe on
	// `PRIMARY KEY (id, l, t)` per pen, so the bounds come from two descents per pen rather than from a scan
	// of `trends`.
	private const string ArchiveExtentStatement = """
		SELECT min(lo) AS first, max(hi) AS last
		FROM semiplot_tags tag
		CROSS JOIN LATERAL (
		    SELECT (SELECT min(t) FROM trends WHERE id = tag.id AND l = 0) AS lo,
		           (SELECT max(t) FROM trends WHERE id = tag.id AND l = 0) AS hi
		) bounds;
		""";

	[Fact]
	public void TheArchiveExtentStatementMatchesItsLiteralCharacterForCharacter()
	{
		Assert.Equal(ArchiveExtentStatement, ArchiveStatements.ArchiveExtent);
	}

	// Every line below is load-bearing. `t < @from` is strict because the window branch takes
	// `t >= @from`, so an inclusive seed bound would return a boundary row on both branches. The
	// `greatest(@to - @from, interval '1 day')` lower bound is the wider of the requested window and one
	// partition width: trends is PARTITION BY RANGE (t) by calendar day with PRIMARY KEY (id, l, t) as
	// its only index, so an unbounded backwards seek plans as a Limit over a Merge Append of every
	// unpruned partition, which opens and pulls a first tuple from each of them before emitting one —
	// one index descent per older day, per pen, on every window change, found row or not. It scales with
	// the window so a pen quiet for days still seeds a week-wide window instead of vanishing from it. The
	// single outer ORDER BY is what keeps each pen one consecutive ascending run for HistoryRowFold.
	private const string SeededWindowStatement = """
		SELECT id, t, v, q
		FROM (
		    SELECT seed.id, seed.t, seed.v, seed.q
		    FROM (SELECT DISTINCT unnest(@ids) AS id) requested
		    CROSS JOIN LATERAL (
		        SELECT prior.id, prior.t, prior.v, prior.q
		        FROM trends prior
		        WHERE prior.id = requested.id AND prior.l = @layer
		          AND prior.t < @from AND prior.t >= @from - greatest(@to - @from, interval '1 day')
		        ORDER BY prior.t DESC
		        LIMIT 1
		    ) seed
		    UNION ALL
		    SELECT id, t, v, q
		    FROM trends
		    WHERE id = ANY(@ids) AND l = @layer AND t >= @from AND t < @to
		) sample
		ORDER BY id, t;
		""";

	[Fact]
	public void TheSeededWindowStatementMatchesItsLiteralCharacterForCharacter()
	{
		Assert.Equal(SeededWindowStatement, ArchiveStatements.SparseHistoryWindow);
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

		var bound = command.Parameters
			.Select(parameter => parameter.ParameterName)
			.Order(StringComparer.Ordinal)
			.ToArray();

		var declared = _parameterTokenPattern.Matches(ArchiveStatements.SparseHistoryWindow)
			.Select(match => match.Groups[1].Value)
			.Distinct(StringComparer.Ordinal)
			.Order(StringComparer.Ordinal)
			.ToArray();

		Assert.NotEmpty(declared);
		Assert.Equal(declared, bound);
	}
}
