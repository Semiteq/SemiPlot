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
	/// The sample table the SCADA writes rows into.
	/// </summary>
	public const string TrendsRelation = "trends";

	/// <summary>
	/// The catch-all partition of <see cref="TrendsRelation"/>, named as
	/// <see cref="DefaultPartitionOccupancy"/> reads it. Schema-qualified, because it names an object the
	/// operator goes looking for rather than a relation a bound statement resolves through the search path.
	/// </summary>
	public const string DefaultPartitionRelation = "public.tpdefault";

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
	/// Every raw sample newer than the last one a subscription saw. Issued once per poll tick.
	/// <para>
	/// The variable list is mandatory. <c>PRIMARY KEY (id, l, t)</c> is the only index on <c>trends</c>
	/// and leads with <c>id</c>, so a predicate over time alone cannot use it at all and degenerates into
	/// a sequential scan of the current day's partition on every tick
	/// (docs/architecture/scada-archive.md, Reader hazards). <c>@ids</c> binds an array so the read stays
	/// a bounded range per identifier on that key.
	/// </para>
	/// <para>
	/// <c>t &gt; @lastSeen</c> is strict, so the row that set <c>@lastSeen</c> is never returned twice —
	/// a repeat would draw a segment running backwards across the plot.
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
	/// The lateral shape is load-bearing. <c>max(t)</c> under <c>id = ANY(...)</c> does not get
	/// PostgreSQL's min/max index-edge transform, so it collects a partition's rows before reducing them;
	/// each lateral scalar subquery here is one index probe on <c>PRIMARY KEY (id, l, t)</c> per variable
	/// instead. It is <see cref="ArchiveExtent"/>'s shape over the requested identifiers rather than over
	/// the whole catalogue.
	/// </para>
	/// <para>
	/// <c>DISTINCT unnest(@ids)</c> is the same de-duplicating source
	/// <see cref="SparseHistoryWindow"/>'s seed branch uses, so a caller repeating an identifier costs one
	/// probe rather than two.
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
	/// One statement rather than two, because <see cref="HistoryRowFold"/> groups by consecutive
	/// identifier — a pen arriving in two runs would yield two envelopes for one pen, and no consumer
	/// rejects that. Under one ordering each pen is still one ascending run.
	/// </para>
	/// <para>
	/// The seed bound is <c>t &lt; @from</c> rather than <c>&lt;=</c>: the window branch already takes
	/// <c>t &gt;= @from</c>, so an inclusive bound would return a boundary row on both branches.
	/// </para>
	/// <para>
	/// The backwards seek is bounded by the wider of the requested window and one partition width.
	/// <c>trends</c> is <c>PARTITION BY RANGE (t)</c> with a partition per calendar day and
	/// <c>PRIMARY KEY (id, l, t)</c> as its only index, so an unbounded <c>ORDER BY t DESC LIMIT 1</c>
	/// plans as a <c>Limit</c> over a <c>Merge Append</c> of every partition the bound leaves unpruned.
	/// That node opens and pulls the first tuple from all of them before it can emit one, so the cost is
	/// one index descent per older partition, per pen, on every window change, whether or not an older
	/// row is there to be found. The bound is what prunes those partitions away.
	/// </para>
	/// <para>
	/// It scales with the window because the archive's value-unchanged state does
	/// (docs/architecture/scada-archive.md, the three-state table). A steady variable — a recipe setpoint
	/// written once at process start — writes nothing for as long as it does not change, and it belongs
	/// on the chart as a horizontal line at its last recorded value rather than as nothing at all. A
	/// window zoomed out to a week reaches back a week for that sample; a two-minute window still costs
	/// the one-day floor and no more. A pen with no row inside the look-back gets no seed and, having no
	/// window rows either, no envelope: the answer it already got, reached in bounded time.
	/// </para>
	/// <para>
	/// <c>@ids</c> binds an array rather than an expanded list so the read keeps the primary key, whose
	/// leading column is <c>id</c>, instead of reading every partition. The seed branch unnests the same
	/// array so each pen's probe carries an equality on that leading column. <c>q</c> is read by the fold
	/// on every row of both branches, so a seed marking a break opens the window inside one.
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

	/// <summary>
	/// Whether the default partition holds any row at all. Issued once, at startup, as a health check.
	/// <para>
	/// What makes the answer about the catch-all rather than about the archive is that the relation named is
	/// the leaf partition itself: naming <c>trends</c> is what would expand into its whole partition tree.
	/// <c>ONLY</c> blocks that expansion downwards, and <c>tpdefault</c> is a leaf with no children of its
	/// own, so here it changes no plan — it only starts mattering if the default partition is ever
	/// partitioned in turn. <c>EXISTS</c> is load-bearing for the same reason a count is not: the answer is
	/// a yes or a no, and the planner stops the scan at the first row instead of counting a partition that
	/// is never pruned.
	/// </para>
	/// <para>
	/// It is qualified rather than left to the search path because the partition is not a relation SemiPlot
	/// reads data from — it is an object named in the fault it reports, and the operator has to find that
	/// object under exactly that name.
	/// </para>
	/// </summary>
	public const string DefaultPartitionOccupancy = """
		SELECT EXISTS (SELECT 1 FROM ONLY public.tpdefault);
		""";

	/// <summary>
	/// The server's effective bound, read only after a read has already failed with <c>57014</c> and only
	/// to fill the number that error reports. <c>pg_settings.setting</c> is text carrying the value in the
	/// parameter's base unit — milliseconds for this parameter — so a reader role at <c>30s</c> reads back
	/// as <c>30000</c> and an unbounded server as <c>0</c>.
	/// <c>SHOW statement_timeout</c> returns the unit-suffixed display string <c>30s</c> instead and is
	/// the wrong query.
	/// </summary>
	public const string EffectiveStatementTimeout = """
		SELECT setting FROM pg_settings WHERE name = 'statement_timeout';
		""";
}
