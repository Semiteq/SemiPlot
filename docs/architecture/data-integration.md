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
    IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

    // Hot, shared by every subscription and never terminating: it neither completes nor faults,
    // so a consumer subscribes with an onNext handler alone.
    IObservable<ArchiveConnectionState> ConnectionFaults { get; }

    Task<Result<IReadOnlyList<Pen>>> QueryPensAsync();

    Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
        IReadOnlyList<int> penIds,
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
| `ArchiveExtent` | `FirstUtc`, `LastUtc`, `IsEmpty` | The span of the configured variables, consumed by the minimap (`TM-4`). `ArchiveExtent.Empty` is the no-span form; the two timestamps are meaningful only when `IsEmpty` is false. |
| `AggregationLayer` | `Raw`, `Minute`, `Hour`, `Day` | Maps one-to-one onto the archive's `l` column. |
| `ArchiveConnectionState` | `Fault`, `IsConnected` | What the provider reports about its own connection. `Fault` is null while the archive answers and carries the typed error while it does not — **The connection state the poll reports** below. |

`QueryHistoryAsync`'s window is half-open — `fromUtc` inclusive, `toUtc` exclusive — in every
implementation, so two adjacent windows neither repeat nor drop a sample on the boundary and a
zero-width window selects nothing. The order of the envelopes it returns is unspecified: a consumer
keys them by `PenId` and never by position.

The pen catalogue is a query, not a property, because reading it can fail: the server can be
unreachable, the table can be absent, the read can time out. Like every other read on this interface
the failure travels as a failed `Result` and never as an exception crossing to the UI thread. The
error types that name those states are defined in `SemiPlot.Core/Data/Errors/`, beside
`IDataProvider`, so a second provider maps its own failures onto the same vocabulary.

That holds on the startup path too: the pen catalogue is read before any window opens, and a failed
`Result` there opens an error window naming the state rather than throwing through Avalonia's setup.
The **Startup** section below states the sequence.

One implementation: `PostgresDataProvider` in `SemiPlot.DataSource.Postgres`, which the composition
root registers. A test needing a provider it can steer builds a fake against this interface inside
the test project; nothing ships a second one, so an operator can never be shown invented numbers as
process data.

## Operation to SQL

All statement text on the application and provider path lives in one place in
`SemiPlot.DataSource.Postgres`. No SQL exists anywhere else on that path. Parameters are always
bound, never interpolated. The bench seeder and the gated test harness own SQL of their own by
design — the schema resource, the partition DDL, the `COPY`, the catalogue upsert, the follow
loop's closed-period `INSERT ... SELECT` that thins a closed period into a coarse layer, its
opening-row `INSERT` whose `LATERAL` probe writes each open period's first raw row,
`CREATE DATABASE` and `DROP DATABASE` — and are outside the rule.

### Pen catalog

```sql
SELECT id, name, group_name, color, line_style
FROM semiplot_tags
ORDER BY coalesce(group_name, ''), name;
```

The ordering coalesces because the read does: `group_name` is nullable and `Pen.Group` is not, so a
null is projected onto the empty string. PostgreSQL sorts nulls last and the empty string first, so
ordering on the raw column would return a list not ordered by the values it carries.

An empty table yields an empty pen list, not a failure — a fresh installation before commissioning
is a normal state. A missing table is the other state and is a failure: `42P01` maps to
`ArchiveFault.TableMissing` with the detail naming `semiplot_tags`.

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
the whole archive. Bounded per `id`, each subquery reaches an index edge per partition rather than
one edge overall: `trends` is partitioned on `t` and the statement carries no `t` predicate, so no
partition is pruned and each bound becomes a `MergeAppend` over one index scan per partition. The
cost scales with the partition count, not with the archive's row count — far cheaper than the
unbounded form, which reads every row of every partition.

An archive with no rows yields nulls, which map to `ArchiveExtent.Empty` rather than to an error.

The extent is the span of the configured variables, not of the archive. The statement is rooted at
`semiplot_tags`, so a present-but-empty catalogue over an archive holding months of rows also yields
`ArchiveExtent.Empty`. That is the intended behaviour: with no configured variables there is nothing
to draw, and a minimap strip spanning data no pen can render would be a lie.

### History, chosen layer already sparse enough

```sql
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
```

One statement in two branches under one outer `ORDER BY id, t`. The window branch returns the rows
inside the window. The seed branch returns, per requested pen, the row with the greatest `t`
**strictly** before the window start, so a pen whose last sample predates the window still draws
instead of being dropped from the chart. The bound is strict because the window branch already takes
`t >= @from`, and an inclusive one would return a boundary row on both branches.

The backwards seek is bounded by the wider of the requested window and one partition width. `trends`
is `PARTITION BY RANGE (t)` with a partition per calendar day and `PRIMARY KEY (id, l, t)` as its
only index, so an unbounded `ORDER BY t DESC LIMIT 1` plans as a `Limit` over a `Merge Append` of
every partition the bound leaves unpruned. That node opens and pulls the first tuple from all of them
before it can emit one, so the cost is one index descent per older partition, per pen, on every
window change, whether or not an older row is there to be found. The bound is what prunes those
partitions away. A pen with no row in the window and none inside the look-back gets no seed and no
envelope. The identifiers are unnested `DISTINCT`, so a caller passing the same pen twice still gets
one seed row for it.

One statement rather than two, because `HistoryRowFold` groups rows by consecutive identifier. A pen
arriving in two runs yields two envelopes for one pen, and `TrendChartViewModel.ApplyHistory` keys by
pen identifier, so one would silently overwrite the other. Under a single outer ordering each pen is
one ascending run, seed first. What breaks that is the loss of the single total ordering — the outer
`ORDER BY` removed, replaced by an ordering per branch, or a second statement feeding the same fold —
rather than a second branch. An `ORDER BY` added inside a `UNION ALL` branch while the outer clause
stands is inert in PostgreSQL and splits nothing.

Rows are folded into envelopes client-side by `HistoryRowFold` over `MinMaxDecimator`
(`SemiPlot.Core.Trends`), one envelope per pen. The decimator's `NaN` anchors (`DA-5`) fire on a null
`v` and on an empty leading or trailing sub-span of the rows it is handed. A vendor break is the
*absence* of rows marked by `q`, not a null `v`, so the fold reads `q` on every row of both branches
and supplies the null itself — the mapping is under Quality and gaps. The window bounds are converted
through `ArchiveTimeConverter.ToArchiveLocal` before binding and each row's `t` through `ToUtc` on
the way out; a row whose converted timestamp does not exceed the previous kept one for that pen is
dropped, which is how the assembler keeps the strictly ascending envelope contract across a
daylight-saving transition — at the cost stated under Time boundary.

A pen with neither a window row nor a seed row gets no envelope at all rather than an empty one,
because an envelope per requested pen would force every consumer to tell "no data" from "not asked
for".

The seed row is what keeps that rule narrow. A pen last written shortly before the window opens
carries its seed and draws; only a pen the archive holds nothing for within the look-back is
omitted.

The consumer side is settled. `TrendChartViewModel.ApplyHistory` receives the requested identifiers
beside the result and calls `DropPensMissingFromHistory`, which clears the curve and the envelope of
every requested pen the result omits. So an omitted pen draws nothing rather than keeping the
previous window's series under the cursor readers and the scale model.

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
degenerates into a sequential scan of the current day's partition on every tick. `t > @lastSeen` is
strict, so the row that set `@lastSeen` is never returned a second time — a repeat would draw a
segment running backwards across the plot.

### Realtime baseline

```sql
SELECT max(hi) AS last
FROM (SELECT DISTINCT unnest(@ids) AS id) requested
CROSS JOIN LATERAL (
    SELECT (SELECT max(t) FROM trends WHERE id = requested.id AND l = 0) AS hi
) bounds;
```

The point a poll starts from, read once per subscription. The lateral shape is what makes it one
index probe per variable on `PRIMARY KEY (id, l, t)`: `max(t)` under `id = ANY(...)` does not get
PostgreSQL's min/max index-edge transform and collects a partition's rows before reducing them. It is
the archive-extent statement's shape over the requested identifiers rather than over the whole
catalogue, and `DISTINCT unnest(@ids)` is the seeded window's own de-duplicating source, so a caller
repeating an identifier costs one probe rather than two. A `NULL` answer means those variables carry
no row yet — a content state, not a failure.

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
one quarter of the period, not the period itself (`AggregationLayerExtensions.ToPointSpacing`):

| Layer | `l` | Period | Point spacing |
| --- | --- | --- | --- |
| `Raw` | 0 | — | the archiving interval |
| `Minute` | 1 | minute | 15 s |
| `Hour` | 2 | hour | 15 min |
| `Day` | 3 | day | 6 h |

A layer becomes usable once `window / targetColumnCount ≥ spacing`.
`ChartNavigationController` expresses that as an upper bound per layer, which keeps the
`width <= ceiling` comparison and its hysteresis helper intact:

```
ceiling(layer) = nextCoarser(layer).ToPointSpacing() × TargetColumnCount
```

Only the *next coarser* layer's spacing enters the comparison, so the raw layer's own spacing never
participates in layer selection. That is what makes the ladder implementable: the true raw spacing
is the SCADA's per-variable archiving interval, which the client cannot know.

**The column count is live, not a fixed reference.** The view reports its data-area width,
`HistoryColumnTarget.FromDataAreaWidth` maps it to 256…2048 columns — one per pixel; `MaxColumns`
stands until the first render reports — and `ChartNavigationController.SetTargetColumnCount` clamps
that and quantises it to the nearest power of two, holding the current count until the reported width
clears the quantisation boundary by 10%. The quantisation applies to layer selection only: without it
every pixel of a resize drag would move every ceiling, far outside the hysteresis band, which guards a
boundary at a fixed count; without the deadband one pixel across a boundary would double or halve
every ceiling at once. The history query keeps the unquantised count, because that decides resolution
rather than layer. A changed *quantised* count re-queries the window even when the layer survives it,
because the visible data was decimated to the previous canvas width.

The two counts diverge inside the deadband, and only the quantised one triggers the re-query. With the
count held at 1024, every reported width from 659 to 1592 px returns early from
`SetTargetColumnCount`, so a resize across that range changes the requested resolution — up to 2.4× —
with no re-query, and the chart keeps drawing the columns fetched at the last queried width. The bound
is the width span of one deadband, `2 × 1.1² ≈ 2.42`: the drawn resolution can lag the canvas by that
factor at most, and the next navigation gesture re-queries at the current unquantised count and closes
the gap.

The ceilings therefore move with the canvas. The three rows below are an example, not constants —
2048 is the default and the widest canvas, 256 the narrowest:

| Columns | `Raw` ceiling | `Minute` ceiling | `Hour` ceiling |
| --- | --- | --- | --- |
| 256 | ≈ 64 minutes | ≈ 2.7 days | ≈ 64 days |
| 1024 | ≈ 4.3 hours | ≈ 10.7 days | ≈ 256 days |
| 2048 | ≈ 8.5 hours | ≈ 21.3 days | ≈ 512 days |

`TrendNavigationModel` caps a window at 365 days, so at a quantised count of 2048 the `Day` layer is
unreachable: its ceiling would start at 512 days. The threshold is the count, not a pixel width — the
deadband makes the count path-dependent, so 1449 px (the naked quantisation boundary) picks 2048 only
when the count did not already stand at 1024. Held at 1024, widths up to 1592 px keep an hour ceiling
of 256 days, and `Day` stays selectable for windows from 282 days to the 365-day cap. At 2048 columns
the exclusion of `Day` is the ladder's own answer rather than a defect — 365 days over 2048 columns is
4.3 hours per column, which the day layer's 6 h spacing cannot fill — so the hour layer is the correct
read there and the decimator folds the surplus.

Adjustments the ladder needs:

- **Hysteresis** — implemented. Layers switch on thresholds separated by a margin, so a window
  hovering on a boundary does not flip layer on every wheel notch and change the visible line
  thickness. The column count carries the same guard as a deadband.
- **Fresh tail** — implemented, in the provider (`FreshTail`). Coarse layers are flushed on their own
  cadence, so a window reaching "now" is short of up to one point spacing at its right edge in
  `l=1/2/3`. The provider reads that edge from `l = 0` and merges it into the coarse rows, per pen and
  under the one-ascending-run-per-pen ordering the fold requires. Three rules bound it:
  - **The seam is per pen** — the newest timestamp the coarse read returned for that pen, or the
    window start when it returned none.
  - **A layer fresh within one of its own points reads no tail**, so the ordinary case costs no extra
    round trip. Otherwise the tail starts at the earliest seam, clamped to four point spacings back
    from the window end. The clamp is a cost bound on one query, not a fault threshold: a coarse layer
    trailing the raw layer by less than a period is the ordinary case.
  - **A pen whose own seam precedes the tail's start contributes no tail row.** Its coarse rows stop
    before the tail begins, so appending tail rows would leave a range no row covers — and a range
    carrying no null is not a gap, since the fold emits one only from a null value. The hole would
    draw as a single straight segment across missing time, so such a pen keeps the short right edge
    it already had.

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

Two consequences of that follow from the archive storing no offset, and no stateless conversion
avoids either. At the autumn fall-back both passes over the repeated hour carry identical naive
values, so the converted sequence repeats an hour. At the spring-forward gap a value inside the gap
takes the standard-time offset while the value after it takes the daylight one, so an ascending naive
sequence converts to a *descending* one across the transition. The strictly ascending
`PenHistoryEnvelope` contract is therefore not the converter's to keep.

**The envelope assembler drops what does not ascend, and that decision is made.** `HistoryRowFold`
keeps a row only when its converted timestamp exceeds the previous kept one for that pen. At the
spring gap that drops the one or two rows the conversion put out of order. At the autumn fall-back it
costs a great deal more: both passes over the repeated hour carry identical naive values, so the
first pass occupies the second pass's instants and every second-pass row that does not advance past
them is dropped — for an archive written at a steady cadence, the whole repeated hour, for every pen,
once a year. The surviving hour is also stamped with the second pass's instants, an hour later than
the rows were taken. Nothing stateless does better, because the archive records no offset to tell the
two passes apart and the envelope contract admits no repeat. Pinned by
`HistoryRowFoldTests.TheSecondPassOverTheRepeatedHourIsDropped`.

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

`HistoryRowFold` is where the mark becomes a null. After appending a row whose `q` is `32` it appends
one more entry for that pen: the row's **converted** timestamp plus one tick, carrying a null value.
One tick is four orders of magnitude below the archive's `timestamp(3)` resolution, so no real row
lands inside it and the series stays strictly ascending, and the tick is added on the UTC side, where
no daylight-saving boundary reaches it. `MinMaxDecimator` splits its series on that null and emits the
`NaN` column between the segments, which is the drawn break. The anchor keeps its own column however
close the tick is: the decimator slices by index inside a non-null segment and appends the gap column
outside any bucket.

`q = 16` takes no branch. The point is kept like any other and the line resumes at it, because a
resumption is what the decimator already produces on the far side of a null segment. Anchoring on the
resumption as well would re-break every restart.

The anchor follows only a row the fold kept, so a `q = 32` row dropped by the strict-ascent guard
carries no anchor with it.

The seed row is a real archive row and carries its own `q`. A seed marked `32` opens the window inside
a gap: the anchor is the series' second entry, so the chart draws the last sample before the break and
then nothing until the window's first row. The anchor comes from the decimator's inter-segment
`AppendGap`, not from its leading-edge branch — the seed's own value is non-null at index 0, so the
first non-null segment starts there and the leading-edge branch does not fire.

The look-back that bounds the seed scales with the window, because the archive's value-unchanged state
does (`scada-archive.md`, the three-state table). A steady variable — a recipe setpoint written once at
process start — writes nothing for as long as it does not change, and it belongs on the chart as a
horizontal line at its last recorded value rather than as nothing at all. A window zoomed out to a week
reaches back a week for that sample; a two-minute window still costs the one-day floor and no more. A
pen quiet for longer than the wider of the two is still omitted, and zooming out is what reaches it.

The mapping is measured against the vendor's own rows rather than against this repository's model of
them: `RealArchiveGapTests` drives the fold with intervals, values and quality codes lifted from the
customer dump, including a 4 min 18 s absence carrying no marker — roughly 2 600 missing polls that
are not a break.

In the bucketed query the same information survives as `q_first`, `q_last` and `breaks`, and the
client walks buckets in time order holding one of two states: after a bucket whose `q_last` is 32 it
is inside a gap and empty buckets stay empty; otherwise empty buckets render as a horizontal
continuation of `v_last`.

## Realtime

`Subscribe` returns a cold observable: each subscription runs a poll loop of its own on the injected
data scheduler, at the operator's `poll_interval_ms`, holding a baseline of its own and carrying the
variable list in every query (`RT-1`). Disposing the subscription cancels the loop's query and its
wait, so no further statement is issued. Batching, the union timeline and the hand-off to the UI
scheduler happen above the provider, in `TrendCoordinator` (see `charting.md`).

The first tick reads the baseline and emits nothing. There is nothing to bind `@lastSeen` to, and
both alternatives are wrong: a null bound returns no row and leaves the subscription blind for good,
an unbounded read pours the whole archive into the chart. Every later tick reads the rows written
past `lastSeen`, converts them to UTC and emits them. `lastSeen` is the archive's own naive wall
clock rather than the local machine's, because a clock difference between the two hosts would drop
or repeat the first seconds of every subscription.

The invariants:

- **The sequence never completes and never faults.** A query error logs, drops that tick's rows and
  leaves the observable running, so nothing throws on the UI thread and the chart keeps the data it
  has.
- **No timestamp is emitted at or before the last one already delivered** (`DA-7`), which keeps the
  history-to-realtime seam monotonic. `t > @lastSeen` is strict and `lastSeen` only moves forward.
- **A row whose `v` is null is dropped, and `lastSeen` still advances past it.** `Sample.Value` is
  non-nullable, so a null has no representation on this seam. Reading it would throw, and the tick's
  own catch would count that throw as a connection failure.
- **A `q = 32` row opens no gap here.** It carries a real value and is emitted as an ordinary sample:
  the gap the history path draws is `HistoryRowFold`'s reconstruction from a null value, and `Sample`
  carries no null to rebuild it with. A break that opens at the live edge draws as a held line until
  the next history read covers it.

### The connection state the poll reports

`ConnectionFaults` is hot, shared by every subscription and never terminating.
`ArchiveConnectionState` carries the fault: null while the archive answers, the typed error while it
does not.

- **Every subscription's first successful tick reports `Connected`**, and so does the first success
  after a raised fault; an ordinary tick in between reports nothing. That first report is the only
  observable point at which a subscription is known to be armed, which is what a consumer sequences
  on — and why nothing filters the stream with `DistinctUntilChanged`, which would drop every report
  after the first.
- **A fault is raised after three consecutive failed ticks**, not after one. Npgsql opens a fresh
  physical connection after a reset, so a dropped packet or a recycled pool connection produces
  exactly one failed tick, and a fault raised on one failure would flap over a healthy archive. The
  count multiplies the operator's own `poll_interval_ms`: at the 1 s cadence a bench uses, that is a
  fault within about three seconds. The state carries `ArchiveFault.ConnectionLost`, naming the host,
  the port, the database and the threshold that raised it.
- **The fault is raised once per outage, and the number it carries is the threshold rather than a
  running count.** The poll keeps failing behind a raised fault and reports nothing further until a
  tick succeeds. Reporting the running total would mean raising on every tick, which is the
  banner-per-second the single raise exists to prevent, so a fault read ten minutes into an outage
  still names the number of failures that raised it.
- **A self-cancelled read is not a failure.** Disposal cancels the in-flight query, and
  `OperationCanceledException` ends the loop ahead of the mapper rather than counting towards the
  threshold.
`MainWindowViewModel` renders the state as one row of the archive banner over a chart that keeps its
history: `StartupFailureMapper.Describe(fault)`, which is the detail followed by the remedy, never
the error's own `Message`. That row has a single writer, the bound stream, so nothing else can set
or clear it.

## Error semantics

Everything on the data path returns `FluentResults`, caller-argument faults included: an inverted
window and a target column count below one are failed `Result`s carrying a plain message. A pen
identifier outside the archive's 32-bit identifier range is a failed `Result` too — the archive has
that column to overflow — while an identifier inside the range naming no pen the archive knows
selects no row and is not a failure.

Two preconditions leave the provider as an exception instead, each a defect in the calling code
rather than a state the archive or the operator can produce: `penIds` is non-null, asserted with
`ArgumentNullException.ThrowIfNull`, and `layer` is a defined member of `AggregationLayer`, asserted
with `ArgumentOutOfRangeException`. The null check comes first in every member taking `penIds`,
`Subscribe` included, which has no `Result` channel to fail through at all. The layer check runs
after the range and target-count checks, so a call carrying both an inverted window and an undefined
layer answers with the failed `Result`. One more exception crosses on the archive path:
`OperationCanceledException`, which `ArchiveExceptionMapper` rethrows rather than turning into a failed
`Result`, as the timeout paragraph below states. Nothing else leaves the provider as an exception,
so no failure crosses to the UI thread (`DA-1`).

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | `ErrorWindow` titled "No connection to the archive", naming the host and port, with a remedy and one **Close** button — no retry, the operator corrects the cause and starts again |
| Connection lost mid-session | failed `Result` on the query; realtime tick dropped | Chart keeps the data it has; staleness is visible |
| Three consecutive realtime ticks fail | `ArchiveFault.ConnectionLost` on `ConnectionFaults`; the observable keeps running | A banner row over the chart naming the live edge that stopped answering and the check to make — the server still running and still reachable — cleared by the first tick that succeeds |
| A column the read needs is absent (SQLSTATE `42703`) | failed `Result` carrying `ArchiveFault.ShapeUnexpected` with the server's own detail | "The archive has an unexpected shape" — the remedy is running `semibase site`, then finding what altered the table |
| Query timeout | failed `Result` | Same as above; the timeout is a configured bound, not an accident |
| The database does not exist (SQLSTATE `3D000`, the server answers) | failed `Result` carrying `ArchiveFault.DatabaseMissing`, distinguished from a connection failure | "The archive is not provisioned" — the remedy is running `semibase site` |
| The credentials are refused or a grant is missing (SQLSTATE `28P01`, `28000`, `42501`) | failed `Result`, distinguished from a connection failure | "The archive refused the credentials" — the remedy is the user, password or grants, not the network |
| `trends` does not exist (provisioning stopped part-way) | failed `Result` carrying `ArchiveFault.TableMissing` whose detail is `trends` | "The archive is not provisioned" — the remedy is running `semibase site` |
| `semiplot_tags` does not exist (provisioning unfinished) | failed `Result` carrying `ArchiveFault.TableMissing` whose detail is `semiplot_tags` | "The archive is not provisioned" — the remedy is running `semibase site` |
| `semiplot_tags` present but empty | empty pen list, success | A row stating the catalogue is empty and naming who fills it — commissioning is not finished |
| Archive present but no rows in the window | success, empty envelope list — no pen has rows, so no pen gets an envelope | Empty chart, no error |

The two table rows carry the same remedy. SemiBase creates `trends` and `semiplot_tags` in one run,
so either one absent is a provisioning that did not complete, and the rows differ only in the table
the detail line names. `postgres-instance.md` holds the full statement.

### Two error planes

A failure crosses the provider boundary in one shape only. Inside the provider it is whatever Npgsql,
the file system or the YAML parser produced — an exception, a SQLSTATE string, a parse position — and
none of that crosses. At the boundary it is mapped onto one of a small set of sealed public error
types in `SemiPlot.Core/Data/Errors/`, beside `IDataProvider`, and the original rides
`.CausedBy(...)` so the log keeps the detail. The internal plane is free to change with the driver;
the public plane is the contract the UI maps onto states and tests assert against. The contract is
the type and its fields. Messages are built in the base constructor and reach no operator: every
consumer renders `StartupFailureMapper`'s words instead — the error window in three parts, the
banner rows in one line each — so a reworded message moves nothing an operator reads, and tests
assert on the type and its fields rather than on message text.

The rule that decides whether a type exists:

> A public error type exists if and only if a distinct operator-visible **failure** sentence exists.
> Operator-visible states that are not failures travel in the success channel.

The second sentence is why the last row of the table above carries no error type: an empty query
window is a state the operator reads, not a failure. An empty `semiplot_tags` sits on that same
side and a missing one does not, and the split is why no `EmptyTagCatalogError` exists. An empty
catalogue is a successful read of zero rows: the database answered correctly and nothing is broken,
and routing it as a failure would make every generic failure handler log a warning on every start of
a fresh installation. A missing `semiplot_tags` raises `42P01` and is a failure, carried by
`ArchiveFault.TableMissing` with the detail naming the table. The two states stay distinguishable —
commissioning unfinished against provisioning unfinished — which is what the provisioning order in
`postgres-instance.md` requires, and the split needs no error type of its own.

| Type | Fields | Operator sentence |
| --- | --- | --- |
| `ConnectionFileError` | path, kind (`NotFound` \| `Unreadable` \| `Unparseable` \| `MissingField` \| `OutOfRange` \| `UnknownTimeZone`), reason | The connection file is absent, or exists but cannot be read as configuration |
| `ArchiveError` | kind (`ArchiveFault`), host, port, database, detail | One sentence per kind, below |

| `ArchiveFault` | Raised by | Detail | Operator sentence |
| --- | --- | --- | --- |
| `Unreachable` | a socket failure, a client bound firing, any `NpgsqlException` without a SQLSTATE | empty | No connection to the archive |
| `AccessDenied` | `28P01`, `28000`, `42501` | the username | The credentials or the grants are wrong |
| `DatabaseMissing` | `3D000` | empty | The server answers but holds no such database |
| `TableMissing` | `42P01` | the relation the failing statement touches | The database exists but a table the read needs does not |
| `ShapeUnexpected` | `42703` | the server's own message | The tables are there, but not the columns they are expected to carry |
| `QueryTimedOut` | `57014` | empty | The server ended the read: its `statement_timeout` passed or an administrator cancelled it |
| `ConnectionLost` | three consecutive failed poll ticks | the number of failures that raised it | The live edge stopped answering; the history already drawn is unaffected |
| `ReadFailed` | any other SQLSTATE, or a client-side throw | the SQLSTATE, or empty | The archive rejected the read for a reason this build does not recognise |

`ConnectionFileError` is raised by the settings loader. `ArchiveError` is the vocabulary the read path
maps its SQLSTATEs onto; `28P01` is a kind of its own rather than one schema error because it sends the
operator to a separate remedy, and `42703` is `ShapeUnexpected` rather than a missing relation because
the tables are there and it is their columns that are wrong. `TableMissing` carries the table name
rather than assuming `trends`, because `42P01` is table-agnostic and the name is what the detail line
reports; the remedy is `semibase site` for either table, since one provisioning run creates both.
`ReadFailed` closes the mapping, so nothing escapes as an exception and nothing crosses as an untyped
`Result.Fail(string)` a consumer cannot route on.

**Every kind reaches the operator.** `StartupFailureMapper` (`SemiPlot.UI/Startup/`) turns each into
a title, a detail and a remedy of its own, and it is the one place a remedy is written: no consumer
renders `IError.Message`. `ErrorWindow` lays the three parts out as three blocks; `Describe` joins the
detail and the remedy into the single line a banner row has room for. `ConnectionLost` never opens the
error window — it is drawn as a banner row over a chart that works. `StartupFailureMapperTests`
enumerates both enums and fails when a member maps to the catch-all arm.

`57014` maps unconditionally to `ArchiveFault.QueryTimedOut`. The server answers that SQLSTATE both
for `statement_timeout` and for a client-issued cancel, and the chart cancels in-flight reads when it
pans, so the two will have to be told apart — but no member of `IDataProvider` takes a
`CancellationToken`, so no read on the provider path hands one down, and a caller's own cancellation
raises `OperationCanceledException`, which the mapper rethrows rather than turning into a failed
`Result`. The slice that gives the interface tokens owns splitting the two.

The error carries no bound: `statement_timeout` is the reader role's own setting and the remedy names
it without a number.

## Configuration

A YAML file named `archive-connection.yaml`, read from the configuration directory —
`C:\DISTR\Config\SemiPlot` unless `--config-dir` names another one, following the `C:\DISTR\`
convention of the sibling project. The directory is correctable from the command line; the file name
is not. All eight keys are required; the loader reports an absent one rather than defaulting it, and ignores
keys the format does not name:

```yaml
host: scada-01
port: 5432
database: semiplot_dev
user: semiplot_reader
password: "change me"
source_time_zone: Europe/Berlin
poll_interval_ms: 1000
schema: public
```

`source_time_zone` takes an IANA identifier, which .NET resolves on Windows as well, and is the zone
the archive's naive timestamps are read in. The file states no query bound: the bound belongs to the
`semiplot_reader` role and SemiBase owns it (`postgres-instance.md`), and SemiPlot sends no
`statement_timeout` in any form. The connection string therefore carries `Command Timeout=0` so
Npgsql's implicit 30 s client bound cannot abort a read before the server answers `57014`, and the
read path stamps its own per-command backstop, a fixed five minutes, on every command it builds.
That backstop is a fixed bound rather than one derived from a value read at connection time, so it
is not guaranteed to sit above the server's: on a site whose reader role carries a
`statement_timeout` above five minutes the client cancel and the server's own cancel race, and a
slow-but-alive read can be reported as `ArchiveFault.Unreachable` rather than as
`ArchiveFault.QueryTimedOut`. Loading returns a `Result`; a malformed file — unreadable, unparseable,
missing a key, holding a value outside its range, or naming a zone the machine does not know — is
reported at startup rather than at first query.

The password is stored in plain text. The mitigation is that SemiPlot connects under a read-only
role, so a leaked credential exposes reading the archive and nothing else. File permissions are the
operator's responsibility.

## Startup

Startup splits at the Avalonia boundary, because the schedulers do: the UI scheduler exists only once
`UseReactiveUI()` has run inside `AfterSetup`, and `AfterSetup` takes a synchronous delegate, so an
archive read left inside it either blocks Avalonia's setup or throws through it.

`StartupProbe` (`SemiPlot.UI/Startup/StartupProbe.cs`) therefore runs in `Program`, ahead of
`BuildAvaloniaApp()`, and touches no Avalonia or ReactiveUI type. Its sequence:

1. Load `<ConfigDir>/archive-connection.yaml` and register `AddPostgresData(settings)`.
2. Resolve `IDataProvider`, read the pen catalogue, then read the archive extent.

Both reads answer with a `Result`. What the sequence carries — the container, the pens and the
extent — crosses the Avalonia boundary in a `StartupData` record, so
`App.InitializeServices` consumes data already read and awaits nothing. The settings do not travel that way: they reach the
provider through the DI singleton `AddPostgresData(settings)` registers. A failed step
short-circuits, disposes the container, and carries its error to `Program`, which maps it through
`StartupFailureMapper` and opens `ErrorWindow` in place of the main window. The two branches are
exclusive by structure: the failure branch returns rather than falling through, and both go through
one single-start guard, because a second `BuildAvaloniaApp()` throws once Avalonia is initialised.
There is no second data source to fall back to, by design: substituting synthetic data would let an
operator read invented numbers as process data.

**The startup reads are bounded by the caller, not by a token.** No member of `IDataProvider` takes a
`CancellationToken`, so the probe wraps each read in `Task.WaitAsync(TimeSpan)` with a 30 s bound: a
server that accepts TCP and answers nothing shows the error window instead of holding startup for the
provider's five-minute backstop. That abandons the wait, not the query — the read keeps running on
its pooled connection until the backstop ends it, and the error window opens without it. The expiring
bound is `StartupReadTimedOutError`, which lives in `SemiPlot.UI.Startup` and stays apart from
`ArchiveFault.QueryTimedOut`: the latter means the server ended the read, and would send the operator
after a `statement_timeout` that may be working as configured.

**The read bound sits above the connect timeout, and that ordering is load-bearing.**
`PostgresConnectionSettings.ConnectTimeoutSeconds` writes Npgsql's connect bound out as 15 s instead
of inheriting it, and `StartupProbe.DefaultReadBound` is 30 s. An unreachable host — wrong address,
host down, a firewall that drops — fails inside the connect attempt, so the wider caller bound lets
`ArchiveFault.Unreachable` win and the operator reads "no connection to the archive". Equal values race,
and the loser reports a timeout whose remedy states the connection was accepted, which is the opposite
of the truth on the single most common failure at a site.
`StartupProbeTests.DefaultReadBound_StaysAboveTheConnectTimeout` pins the ordering.

**A throw on the startup path is a failed `Result`, never an escape.** Resolving `IDataProvider`
constructs the `NpgsqlDataSource` and can throw, and `ArchiveExceptionMapper` rethrows
`OperationCanceledException` by design rather than mapping it. `StartupProbe.ReadAsync` catches both,
logs the exception with its stack, disposes the container and returns a FluentResults
`ExceptionalError`. `StartupFailureMapper` maps that through an `IExceptionalError` arm — the exception
equivalent of its catch-all — so the operator gets a window naming the exception type instead of a
process that exits with no window at all.

An empty pen catalogue is a successful start rather than a failure, as the error-semantics table
above requires: the window opens, draws nothing, and states that the catalogue is empty
(`MainWindowViewModel.IsCatalogueEmpty`).

Logging is configured before the probe runs, so every startup failure reaches the log file as well as
the window. The log path and the argument list are in `overview.md`; the default level is `Warning`.

## Keeping this document honest

Documentation that describes SQL drifts from the SQL. Nothing in this repository detects that drift
any more — no test reads this document. What follows narrows how far a drift can spread. Two of the
three steps are tests; the one that catches a drift in this document is a reader's, and it sits
inside step 2:

1. All statement text on the application and provider path lives in one class,
   `ArchiveStatements.cs`. Nothing else on that path issues SQL. The bench seeder and the test
   projects own SQL of their own by design and are outside the rule. Seven SQL blocks stand above,
   and five of them are shipped statements with a constant in that class: the pen catalogue, the
   archive extent, the sparse history window, the realtime poll and the realtime baseline. The other two — the bucketed history read and the gap
   explanation — have no constant behind them: `postgres-bucketed-read` is dropped, and no roadmap
   slice names the gap explanation. They are a design record until a slice ships them.
2. Unit tests pin the shipped statements clause by clause, in
   `SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs` against the constants: one assertion
   per guarantee whose loss nothing else catches without a container — the sparse history window's
   outer ordering, its strict seam bound and its one-day seed floor, the realtime poll's raw-layer
   filter and its time ordering, the realtime baseline's raw-layer filter and its de-duplicated
   identifiers. Three statements take
   parameters, and each binds through a binder of its own pinned against that statement's own
   parameter names: `PostgresDataProvider.BindWindow` over the sparse history window,
   `RealtimePoll.BindPoll` over the poll and `RealtimePoll.BindBaseline` over the baseline. A change
   to a pinned clause therefore shows up as a failing test, while a reformatting does not. None of
   it covers this document: nothing checks that the SQL quoted above still matches the constants, so
   whoever assembles a brief from this document re-reads by hand the five blocks that have a constant
   against `ArchiveStatements.cs`. The other two name no constant, so that re-read cannot cover
   them; they are checked against the code only when the slice that ships them lands.
3. Gated integration tests run `EXPLAIN` on the extent statement, the sparse history window, the
   realtime poll and the realtime baseline, and assert each plan's shape: an index scan under each
   bounded subquery, a seed walk whose backwards bound the planner pushed into the index, no older
   partition read for a pen with no prior rows, and no sequential scan of a `trends` partition
   holding rows. The plans never name `tpk` — it is the parent partitioned index of a
   `PARTITION BY RANGE (t)` table and is never scanned, so what `EXPLAIN` prints is each partition's
   own cloned `<partition>_pkey`. The shape assertions survive partition renaming and still fail the
   moment a predicate is dropped, which turns the hazards this document states in prose — an
   unbounded extent minimum, a missing layer predicate, a missing variable list — into enforced
   invariants. The two statements with no constant carry the same hazards and get the same treatment
   in the slices that implement them.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, provisioning did not complete: `semibase site` creates it and
   `semiplot_tags` in one run.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side, not ours.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn even though its data
   exists.
5. Does the window overlap the data? Compare against the extent, and suspect a `source_time_zone`
   mismatch if the offset looks like a whole number of hours.
6. Is `tpdefault` non-empty? Rows there indicate the SCADA failed to create a daily partition. They
   are **not** a cause of an empty chart: every read still returns them, and what is lost is
   partition elimination, so reads that cannot skip that partition are slower. The remedy is on the
   SCADA side.
