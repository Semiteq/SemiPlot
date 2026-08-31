namespace SemiPlot.DataSource.Postgres;

/// <summary>
/// Every statement the provider issues; parameters are bound, never interpolated.
/// </summary>
internal static class ArchiveStatements
{
	/// <summary>
	/// SemiBase's catalogue of configured variables.
	/// </summary>
	public const string TagCatalogRelation = "semiplot_tags";

	/// <summary>
	/// The sample table the SCADA writes rows into.
	/// </summary>
	public const string TrendsRelation = "trends";

	/// <summary>
	/// Coalesce on purpose: nulls sort last, the empty string the read projects sorts first.
	/// </summary>
	public const string PenCatalog = """
	                                 SELECT id, name, group_name, color, line_style
	                                 FROM semiplot_tags
	                                 ORDER BY coalesce(group_name, ''), name;
	                                 """;

	/// <summary>
	/// The oldest and newest raw timestamps across the whole catalogue, read once to bound the chart.
	/// <para>
	/// The lateral pair is load-bearing: a bare <c>min(t)</c>/<c>max(t)</c> loses the index-edge transform.
	/// </para>
	/// </summary>
	public const string ArchiveExtent = """
	                                    SELECT min(lo) AS first, max(hi) AS last
	                                    FROM semiplot_tags tag
	                                    CROSS JOIN LATERAL (
	                                        SELECT (SELECT min(t) FROM trends WHERE id = tag.id AND l = 0) AS lo,
	                                               (SELECT max(t) FROM trends WHERE id = tag.id AND l = 0) AS hi
	                                    ) bounds;
	                                    """;

	/// <summary>
	/// Every raw sample newer than the last one a subscription saw. Issued once per poll tick.
	/// <para>
	/// The variable list is mandatory (docs/architecture/scada-archive.md, Reader hazards).
	/// Strict <c>&gt;</c>: the row that set <c>@lastSeen</c> must not return twice.
	/// </para>
	/// </summary>
	public const string RealtimePoll = """
	                                   SELECT id, t, v, q
	                                   FROM trends
	                                   WHERE id = ANY(@ids) AND l = 0 AND t > @lastSeen
	                                   ORDER BY t;
	                                   """;

	/// <summary>
	/// The newest raw timestamp across the subscribed variables, read once to establish the point a poll
	/// starts from. A <c>NULL</c> answer means those variables carry no row yet, which is a content state
	/// and not a failure.
	/// <para>
	/// Lateral on purpose: <c>max(t)</c> under <c>id = ANY(...)</c> loses the index-edge transform.
	/// </para>
	/// </summary>
	public const string RealtimeBaseline = """
	                                       SELECT max(hi) AS last
	                                       FROM (SELECT DISTINCT unnest(@ids) AS id) requested
	                                       CROSS JOIN LATERAL (
	                                           SELECT (SELECT max(t) FROM trends WHERE id = requested.id AND l = 0) AS hi
	                                       ) bounds;
	                                       """;

	/// <summary>
	/// A window of one layer, with the left edge seeded so a pen whose last sample predates the window
	/// still draws. Two branches under one outer <c>ORDER BY id, t</c>: the per-pen seed row, then the
	/// window rows.
	/// <para>
	/// One statement, not two: the fold groups by consecutive identifier.
	/// The seed bound is strict <c>&lt;</c>, so no boundary row returns on both branches.
	/// The look-back bound is what prunes partitions away
	/// (docs/architecture/scada-archive.md, the three-state table).
	/// </para>
	/// </summary>
	public const string SparseHistoryWindow = """
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
}
