# Data integration — the boundary between SemiPlot and the archive

This document defines the contract: who owns what, what SemiPlot asks the database, and what it
does with the answers. The archive itself is described in `scada-archive.md`; the instance SemiPlot
reads is in `postgres-instance.md`. Requirement identifiers (`DA-`, `RT-`) refer to
`trend-feature-spec.md`. Claim provenance follows `sources.md`.

There is no application server. The desktop client connects to PostgreSQL directly, so this
document and the provider code are the entire integration surface.

## Responsibility zones

| Concern | Simple-Scada | SemiPlot | Neither — we add it |
| --- | --- | --- | --- |
| Schema of `trends` / `messages`, partition creation, writes, thinning | owns | reads only | |
| Executing retention (deleting old partitions) | owns | | |
| Choosing the retention depth | setting lives in the SCADA project | decision is ours `[DEC:common-retention]` | |
| PostgreSQL instance: installation, configuration, roles, backup, upgrade | client of it | client of it — provisioned by SemiBase, see `postgres-instance.md` | |
| Variable number to name mapping | absent | | `semiplot_tags` `[DEC:semiplot-tags]` |
| Knowledge of the archive's time zone | not stored anywhere | owns, in configuration | |
| Layer choice, decimation, gap rendering, envelope assembly | | owns | |
| Realtime freshness | write and flush cadence | poll cadence | |

Two rules follow and are not negotiable: SemiPlot never writes to vendor objects
`[DEC:read-only-consumer]`, and every additive object is prefixed `semiplot_`
`[DEC:additive-objects]`.

## The provider surface

The UI depends only on `IDataProvider` (`SemiPlot.Core/Data/IDataProvider.cs`). The interface and
its DTOs live in `SemiPlot.Core`; each concrete provider is a sibling `SemiPlot.DataSource.*`
project, so Core never references a data source.

```csharp
public interface IDataProvider
{
    // Cold per call: no samples flow until subscribed; the subscriber disposes the returned IDisposable.
    IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds);

    Task<Result<IReadOnlyList<Pen>>> QueryPensAsync();

    Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
        IReadOnlyList<long> penIds,
        DateTime fromUtc,
        DateTime toUtc,
        AggregationLayer layer,
        int targetColumnCount);

    Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();
}
```

| Type | Shape | Notes |
| --- | --- | --- |
| `Pen` | `PenId`, `Name`, `Group`, `Color`, `LineStyle` | `PenId` is the archive's `trends.id`. |
| `Sample` | `PenId`, `TimestampUtc`, `Value` | Realtime element. Timestamps are UTC by the time they leave the provider. |
| `PenHistoryEnvelope` | parallel `Timestamps` / `Min` / `Max` / `Center`, strictly ascending, `NaN` marks a gap | One per pen per history query. |
| `ArchiveExtent` | `FirstUtc`, `LastUtc` | Full stored span, consumed by the minimap (`TM-4`). |
| `AggregationLayer` | `Raw`, `Minute`, `Hour`, `Day` | Maps one-to-one onto the archive's `l` column. |

The pen catalogue is a query, not a property, because reading it can fail: the server can be
unreachable, the table can be absent, the read can time out. Like every other read on this interface
the failure travels as a failed `Result` and never as an exception crossing to the UI thread. The
error types that name those states are defined with the PostgreSQL provider.

As built, the composition root does not yet honour that last part for the catalogue: `App.axaml.cs`
reads the catalogue once at startup and lets a failed `Result` throw, so the process fails to start
instead of showing the "no connection to the archive" state the error-semantics table below promises.
This is the startup thread, before any UI thread exists. Turning it into an operator-visible state is
owned by slice `postgres-startup-and-composition`.

Implementations: `RandomStubDataProvider` in `SemiPlot.DataSource.Stub` (synthetic, used by tests
and demos) and `PostgresDataProvider` in `SemiPlot.DataSource.Postgres` (production). The
composition root picks one by configuration.

## Operation to SQL

All statement text lives in one place in `SemiPlot.DataSource.Postgres`. No SQL exists anywhere
else in the solution. Parameters are always bound, never interpolated.

### Pen catalog

```sql
SELECT id, name, group_name, color, line_style
FROM semiplot_tags
ORDER BY group_name, name;
```

An empty table yields an empty pen list, not a failure — a fresh installation before commissioning
is a normal state.

### Archive extent

```sql
SELECT min(lo) AS first, max(hi) AS last
FROM semiplot_tags tag
CROSS JOIN LATERAL (
    SELECT (SELECT min(t) FROM trends WHERE id = tag.id AND l = 0) AS lo,
           (SELECT max(t) FROM trends WHERE id = tag.id AND l = 0) AS hi
) bounds;
```

The per-variable subqueries are what make this cheap. A bare `SELECT min(t) FROM trends WHERE l = 0`
cannot use `PRIMARY KEY (id, l, t)` — the leading column is `id` — and degenerates into a scan of
the whole archive. Bounded per `id`, each subquery walks the index to its edge.

An archive with no rows yields nulls, which map to an empty extent rather than an error.

### History, chosen layer already sparse enough

```sql
SELECT id, t, v, q
FROM trends
WHERE id = ANY(@ids) AND l = @layer AND t >= @from AND t < @to
ORDER BY id, t;
```

Rows are folded into envelopes client-side by the existing min/max decimator, which also inserts the
`NaN` anchors that break the line at gaps (`DA-5`).

### History, chosen layer still denser than the canvas

```sql
SELECT id,
       date_bin(@bucket, t, @origin)                       AS bucket,
       min(v)                                              AS v_min,
       max(v)                                              AS v_max,
       (array_agg(v ORDER BY t))[1]                        AS v_first,
       (array_agg(v ORDER BY t DESC))[1]                   AS v_last,
       min(t)                                              AS t_first,
       max(t)                                              AS t_last,
       (array_agg(q ORDER BY t))[1]                        AS q_first,
       (array_agg(q ORDER BY t DESC))[1]                   AS q_last,
       count(*) FILTER (WHERE q = 32)                      AS breaks
FROM trends
WHERE id = ANY(@ids) AND l = @layer AND t >= @from AND t < @to
GROUP BY id, bucket
ORDER BY id, bucket;
```

`@bucket` is the window width divided by `targetColumnCount`, so the statement returns at most one
row per pixel column per pen (`DA-2`, `DA-6`). `date_bin` requires PostgreSQL 14 or newer, which our
instance satisfies. `@origin` is the window start, so buckets align to the visible window rather
than to an arbitrary epoch, which keeps the leftmost column from being clipped.

### Realtime poll

```sql
SELECT id, t, v, q
FROM trends
WHERE id = ANY(@ids) AND l = 0 AND t > @lastSeen
ORDER BY t;
```

The variable list is mandatory. A poll predicated on time alone cannot use the primary key and
degenerates into a sequential scan of the current day's partition on every tick.

### Gap explanation

```sql
SELECT t, n, v
FROM messages
WHERE gid = -6 AND t >= @from AND t < @to
ORDER BY t;
```

Read only to tell the operator why a gap exists. Never a source of trend data.

## Layer ladder

The archive offers four resolutions; the renderer needs about one point per pixel column. The rule
is: **choose the coarsest layer whose point spacing still fits inside one pixel column.**

Point spacing follows from the vendor's budget of four points per period `[FORUM:1032]`, so it is
one quarter of the period, not the period itself:

| Layer | `l` | Period | Point spacing | Lower bound of window width, 1000 columns |
| --- | --- | --- | --- | --- |
| `Raw` | 0 | — | the archiving interval | — |
| `Minute` | 1 | minute | 15 s | ≈ 4.2 hours |
| `Hour` | 2 | hour | 15 min | ≈ 10.4 days |
| `Day` | 3 | day | 6 h | ≈ 250 days |

Generally, a layer becomes usable once `window / targetColumnCount ≥ spacing`; the thresholds above
are that inequality solved for 1000 columns.

`AggregationLayerExtensions.ToSampleInterval` currently returns the period rather than the spacing,
which makes every threshold four times too conservative. It must return the spacing.

Two adjustments the ladder needs:

- **Hysteresis.** Switch layers on thresholds separated by a margin, so that a window hovering on a
  boundary does not flip layer on every wheel notch and change the visible line thickness.
- **Fresh tail.** Coarse layers are flushed on their own cadence, so a window reaching "now" has an
  empty tail in `l=1/2/3`. The provider fills the tail from `l=0` and concatenates. The seam is the
  newest timestamp present in the coarse layer.

Correctness of the envelope at every layer rests on the vendor's selection preserving each period's
extremes `[FORUM:1974]`. That is well supported but not yet measured by us — see the open questions
in `scada-archive.md`. If the pending experiment refutes it, the ladder collapses to raw plus
server-side bucketing and wide windows lose amplitude fidelity.

## Time boundary

The archive stores naive local wall-clock time; everything above the provider is UTC.

- Reading: `t` is interpreted in the configured `source_time_zone` and converted to
  `DateTime(Kind = Utc)`.
- Writing query bounds: UTC window edges are converted back to naive local before binding.
- The conversion happens only at the provider edge. No other component knows the archive's zone.
- Display-local rendering is a separate, later conversion performed by `LocalTimeAxis`.

The zone lives in configuration because the database does not record it and cannot be asked. If the
SCADA host's zone is changed, the configuration must be changed with it; historical data written
before the change stays in the old zone and is not correctable. Daylight-saving transitions shift or
duplicate an hour of history; this is accepted as cosmetic.

## Quality and gaps

The provider maps the archive's quality marks onto the envelope's gap representation (`DA-8`):

| Archive | Envelope |
| --- | --- |
| `q = 0` | ordinary point |
| `q = 32` (last sample before a break) | point kept, then a `NaN` anchor inserted after it |
| `q = 16` (first sample after a break) | point kept; the line resumes here |
| absence of rows without a preceding `q = 32` | no anchor — the value simply did not change |

The distinction in the last row is the whole reason the marks exist. Treating every row gap as a
line break would shred a steady signal into fragments; ignoring the marks would draw a straight line
across hours of missing data.

In the bucketed query the same information survives as `q_first`, `q_last` and `breaks`, and the
client walks buckets in time order holding one of two states: after a bucket whose `q_last` is 32 it
is inside a gap and empty buckets stay empty; otherwise empty buckets render as a horizontal
continuation of `v_last`.

## Realtime

`Subscribe` returns a cold observable. On subscription the provider polls the raw layer on the data
scheduler, advances `lastSeen` to the newest timestamp it received, and emits the new samples
converted to UTC (`RT-1`). Disposal stops the poll.

Two invariants:

- A query error logs and drops that tick. It never throws on the UI thread and never terminates the
  observable.
- The provider never emits a timestamp at or before the last one already delivered, which is what
  keeps the history-to-realtime seam monotonic (`DA-7`).

Batching, the union timeline and the hand-off to the UI scheduler happen above the provider, in
`TrendCoordinator` (see `charting.md`).

## Error semantics

Everything on the data path returns `FluentResults`. Exceptions never cross to the UI thread
(`DA-1`).

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | Explicit "no connection to the archive" state, retry available |
| Connection lost mid-session | failed `Result` on the query; realtime tick dropped | Chart keeps the data it has; staleness is visible |
| Query timeout | failed `Result` | Same as above; the timeout is a configured bound, not an accident |
| `trends` does not exist (SCADA never started) | failed `Result`, distinguished from a connection failure | "Archive not initialised" — a normal state on a fresh installation |
| `semiplot_tags` empty or missing | empty pen list, success | "No variables configured" — commissioning is not finished |
| Archive present but no rows in the window | success, empty envelopes | Empty chart, no error |

## Configuration

A YAML file in a `--config-dir`, following the convention of the sibling project. Fields: host,
port, database, user, password, `source_time_zone`, poll interval, schema, statement timeout, and a
file-version field checked on load. Loading returns a `Result`; a malformed file is reported at
startup rather than at first query.

The password is stored in plain text. The mitigation is that SemiPlot connects under a read-only
role, so a leaked credential exposes reading the archive and nothing else. File permissions are the
operator's responsibility.

## Keeping this document honest

Documentation that describes SQL drifts from the SQL. Three artifacts prevent that:

1. All statement text lives in one class; this document quotes it and names the file. Nothing else
   in the solution issues SQL.
2. Unit tests pin the generated statement text and parameter names for every operation, so a change
   in the code that this document does not describe shows up as a failing test.
3. Gated integration tests run `EXPLAIN` on the windowed history query and the realtime poll and
   assert that both use `tpk`. This turns the two documented hazards — the missing layer predicate
   and the missing variable list — into enforced invariants rather than warnings in prose.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, the SCADA project has never run against this database.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side, not ours.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn even though its data
   exists.
5. Does the window overlap the data? Compare against the extent, and suspect a `source_time_zone`
   mismatch if the offset looks like a whole number of hours.
6. Is `tpdefault` non-empty? Rows there indicate the SCADA failed to create a daily partition; they
   are outside every date range and effectively invisible.
