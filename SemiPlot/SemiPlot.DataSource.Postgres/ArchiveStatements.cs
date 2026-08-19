namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Every SQL statement the application and provider path issues. No statement text exists anywhere else
/// in the solution; parameters are bound, never interpolated. The bench seeder and the test projects own
/// SQL of their own by design and are outside that rule.
/// </summary>
internal static class ArchiveStatements
{
	/// <summary>
	/// SemiBase's catalogue of configured variables.
	/// </summary>
	public const string TagCatalogRelation = "semiplot_tags";

	/// <summary>
	/// The SCADA's sample table.
	/// </summary>
	public const string TrendsRelation = "trends";

	/// <summary>
	/// Ordered by the coalesced group rather than by the raw column, because the row read projects a null
	/// group onto the empty string: PostgreSQL sorts nulls last while the empty string sorts first, so
	/// ordering on the raw column would return a list not ordered by the values it carries.
	/// </summary>
	public const string PenCatalog = """
		SELECT id, name, group_name, color, line_style
		FROM semiplot_tags
		ORDER BY coalesce(group_name, ''), name;
		""";

	public const string ArchiveExtent = """
		SELECT min(lo) AS first, max(hi) AS last
		FROM semiplot_tags tag
		CROSS JOIN LATERAL (
		    SELECT (SELECT min(t) FROM trends WHERE id = tag.id AND l = 0) AS lo,
		           (SELECT max(t) FROM trends WHERE id = tag.id AND l = 0) AS hi
		) bounds;
		""";

	/// <summary>
	/// <c>@ids</c> binds an array rather than an expanded list so the read keeps
	/// <c>PRIMARY KEY (id, l, t)</c>, whose leading column is <c>id</c>, instead of reading every partition.
	/// <c>q</c> is selected for the gap reconstruction that reads it; the fold ignores the column for now.
	/// </summary>
	public const string SparseHistoryWindow = """
		SELECT id, t, v, q
		FROM trends
		WHERE id = ANY(@ids) AND l = @layer AND t >= @from AND t < @to
		ORDER BY id, t;
		""";

	/// <summary>
	/// The server's effective bound, read once per physical connection. <c>pg_settings.setting</c> is
	/// text carrying the value in the parameter's base unit — milliseconds for this parameter — so a
	/// reader role at <c>30s</c> reads back as <c>30000</c> and an unbounded server as <c>0</c>.
	/// <c>SHOW statement_timeout</c> returns the unit-suffixed display string <c>30s</c> instead and is
	/// the wrong query.
	/// </summary>
	public const string EffectiveStatementTimeout = """
		SELECT setting FROM pg_settings WHERE name = 'statement_timeout';
		""";

	/// <summary>
	/// Answers which of the two relations a <c>42P01</c> refers to. The names are unqualified, matching
	/// the reads, so the probe resolves through the same <c>search_path</c> the failing statement did.
	/// </summary>
	public const string RelationProbe = """
		SELECT to_regclass('semiplot_tags') IS NOT NULL AS tags_present,
		       to_regclass('trends') IS NOT NULL AS trends_present;
		""";
}
