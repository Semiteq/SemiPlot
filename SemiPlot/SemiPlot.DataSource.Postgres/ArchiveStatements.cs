namespace SemiPlot.DataSource.Postgres;

/// <summary>The column order of <see cref="ArchiveStatements.PenCatalog"/>, named so a transposition shows.</summary>
internal static class PenCatalogColumn
{
	public const int Id = 0;
	public const int Name = 1;
	public const int Unit = 2;
	public const int Format = 3;
	public const int Color = 4;
	public const int LineStyle = 5;
	public const int EnabledOnStart = 6;
	public const int ScaleMin = 7;
	public const int ScaleMax = 8;
	public const int Groups = 9;
}

/// <summary>
/// Every statement the provider issues; parameters are bound, never interpolated.
/// </summary>
internal static class ArchiveStatements
{
	/// <summary>
	/// SemiBase's catalogue of configured variables.
	/// </summary>
	public const string TagCatalogRelation = "semiplot_tags";

	public const string GroupsRelation = "semiplot_groups";

	public const string PenGroupsRelation = "semiplot_pen_groups";

	/// <summary>
	/// The sample table the SCADA writes rows into.
	/// </summary>
	public const string TrendsRelation = "trends";

	/// <summary>
	/// Every relation <see cref="PenCatalog"/> touches, for the detail line of a failed read.
	/// </summary>
	public const string PenCatalogRelations = $"{TagCatalogRelation}, {GroupsRelation}, {PenGroupsRelation}";

	/// <summary>
	/// Every relation <see cref="ArchiveExtent"/> touches, for the detail line of a failed read.
	/// </summary>
	public const string ArchiveExtentRelations = $"{TagCatalogRelation}, {TrendsRelation}";

	/// <summary>
	/// One row per pen whatever its group count. <c>GROUP BY tag.id</c> alone is legal because it is the key.
	/// </summary>
	public const string PenCatalog = """
	                                 SELECT tag.id, tag.name, tag.unit, tag.format, tag.color, tag.line_style,
	                                        tag.enabled_on_start, tag.scale_min, tag.scale_max,
	                                        coalesce(array_agg(grp.name ORDER BY grp.name)
	                                                 FILTER (WHERE grp.name IS NOT NULL), '{}') AS groups
	                                 FROM semiplot_tags tag
	                                 LEFT JOIN semiplot_pen_groups membership ON membership.pen_id = tag.id
	                                 LEFT JOIN semiplot_groups grp ON grp.id = membership.group_id
	                                 GROUP BY tag.id
	                                 ORDER BY tag.name;
	                                 """;

	/// <summary>
	/// The oldest and newest raw timestamps across the whole catalogue, read once to bound the chart; the
	/// lateral pair is load-bearing, a bare <c>min(t)</c>/<c>max(t)</c> loses the index-edge transform.
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
	/// Every raw sample newer than the last one a subscription saw, issued once per poll tick with a strict
	/// <c>&gt;</c> so the row that set <c>@lastSeen</c> never returns twice (docs/architecture/scada-archive.md).
	/// </summary>
	public const string RealtimePoll = """
	                                   SELECT id, t, v, q
	                                   FROM trends
	                                   WHERE id = ANY(@ids) AND l = 0 AND t > @lastSeen
	                                   ORDER BY t;
	                                   """;

	/// <summary>
	/// The newest raw timestamp across the subscribed variables, read once to establish where a poll starts;
	/// a <c>NULL</c> answer is a content state, not a failure. Lateral on purpose: a bare <c>max(t)</c>
	/// under <c>id = ANY(...)</c> loses the index-edge transform.
	/// </summary>
	public const string RealtimeBaseline = """
	                                       SELECT max(hi) AS last
	                                       FROM (SELECT DISTINCT unnest(@ids) AS id) requested
	                                       CROSS JOIN LATERAL (
	                                           SELECT (SELECT max(t) FROM trends WHERE id = requested.id AND l = 0) AS hi
	                                       ) bounds;
	                                       """;

	/// <summary>
	/// A window of one layer, left-edge seeded so a pen whose last sample predates the window still draws;
	/// one statement, not two, folded under one outer <c>ORDER BY id, t</c> with a strict <c>&lt;</c> seed
	/// bound so no boundary row returns on both branches (docs/architecture/scada-archive.md#reader-hazards).
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

	/// <summary>
	/// Raw by construction with no layer bound: the seed branch of <see cref="SparseHistoryWindow"/>
	/// unchanged, and a window branch whose buckets end in a gap at a <c>q = 32</c> marker or a null
	/// (docs/architecture/data-integration.md#quality-and-gaps).
	/// </summary>
	public const string BucketedRawWindow = """
	                                        SELECT id, t, v, lo, hi, breaks
	                                        FROM (
	                                            SELECT seed.id, seed.t, seed.v, seed.v AS lo, seed.v AS hi,
	                                                   (seed.q = 32 OR seed.v IS NULL) AS breaks
	                                            FROM (SELECT DISTINCT unnest(@ids) AS id) requested
	                                            CROSS JOIN LATERAL (
	                                                SELECT prior.id, prior.t, prior.v, prior.q
	                                                FROM trends prior
	                                                WHERE prior.id = requested.id AND prior.l = 0
	                                                  AND prior.t < @from
	                                                  AND prior.t >= @from - greatest(@to - @from, interval '1 day')
	                                                ORDER BY prior.t DESC
	                                                LIMIT 1
	                                            ) seed
	                                            UNION ALL
	                                            SELECT id,
	                                                   coalesce(max(t) FILTER (WHERE v IS NOT NULL), max(t)) AS t,
	                                                   (array_agg(v ORDER BY t DESC) FILTER (WHERE v IS NOT NULL))[1] AS v,
	                                                   min(v) AS lo,
	                                                   max(v) AS hi,
	                                                   bool_or(q = 32 OR v IS NULL) AS breaks
	                                            FROM (
	                                                SELECT id, t, v, q,
	                                                       count(*) FILTER (WHERE q = 32) OVER (
	                                                           PARTITION BY id ORDER BY t
	                                                           ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING
	                                                       ) AS segment
	                                                FROM trends
	                                                WHERE id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to
	                                            ) windowed
	                                            GROUP BY id, segment, date_bin(@bucket, t, @from)
	                                        ) sample
	                                        ORDER BY id, t;
	                                        """;
}
