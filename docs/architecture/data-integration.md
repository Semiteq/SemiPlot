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
| `ArchiveExtent` | `FirstUtc`, `LastUtc`, `IsEmpty` | The span of the configured variables, consumed by the minimap (`TM-4`). `ArchiveExtent.Empty` is the no-span form; the two timestamps are meaningful only when `IsEmpty` is false. |
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

All statement text on the application and provider path lives in one place in
`SemiPlot.DataSource.Postgres`. No SQL exists anywhere else on that path. Parameters are always
bound, never interpolated. The bench seeder and the gated test harness own SQL of their own by
design — the schema resource, the partition DDL, the `COPY`, the catalogue upsert, `CREATE DATABASE`
and `DROP DATABASE` — and are outside the rule.

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
`ArchiveNotInitialisedError` with `Table` naming `semiplot_tags`.

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

Two consequences of that follow from the archive storing no offset, and no stateless conversion
avoids either. At the autumn fall-back both passes over the repeated hour carry identical naive
values, so the converted sequence repeats an hour. At the spring-forward gap a value inside the gap
takes the standard-time offset while the value after it takes the daylight one, so an ascending naive
sequence converts to a *descending* one across the transition. The strictly ascending
`PenHistoryEnvelope` contract is therefore not the converter's to keep — the component that assembles
envelopes owns what to do about both.

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
| The database does not exist (SQLSTATE `3D000`, the server answers) | failed `Result`, distinguished from a connection failure | "Archive database missing" — the remedy is running `semibase create` |
| The credentials are refused or a grant is missing (SQLSTATE `28P01`, `28000`, `42501`) | failed `Result`, distinguished from a connection failure | "Archive access denied" — the remedy is the user, password or grants, not the network |
| `trends` does not exist (SCADA never started) | failed `Result`, distinguished from a connection failure | "Archive not initialised" — a normal state on a fresh installation |
| `semiplot_tags` does not exist (provisioning unfinished) | failed `Result` carrying `ArchiveNotInitialisedError` whose `Table` is `semiplot_tags` | "Archive not initialised" — the remedy is running `semibase create` |
| `semiplot_tags` present but empty | empty pen list, success | "No variables configured" — commissioning is not finished |
| Archive present but no rows in the window | success, empty envelopes | Empty chart, no error |

### Two error planes

A failure crosses the provider boundary in one shape only. Inside the provider it is whatever Npgsql,
the file system or the YAML parser produced — an exception, a SQLSTATE string, a parse position — and
none of that crosses. At the boundary it is mapped onto one of a small set of sealed public error
types in `SemiPlot.Core/Data/Errors/`, beside `IDataProvider`, and the original rides
`.CausedBy(...)` so the log keeps the detail. The internal plane is free to change with the driver;
the public plane is the contract the UI maps onto states and tests assert against. Messages are built
in the base constructor and are not part of that contract — they may be reworded without a slice
noticing.

The rule that decides whether a type exists:

> A public error type exists if and only if a distinct operator-visible **failure** sentence exists.
> Operator-visible states that are not failures travel in the success channel.

The second sentence is why the last row of the table above carries no error type: an empty query
window is a state the operator reads, not a failure. An empty `semiplot_tags` sits on that same
side and a missing one does not, and the split is why no `EmptyTagCatalogError` exists. An empty
catalogue is a successful read of zero rows: the database answered correctly and nothing is broken,
and routing it as a failure would make every generic failure handler log a warning on every start of
a fresh installation. A missing `semiplot_tags` raises `42P01` and is a failure, carried by
`ArchiveNotInitialisedError` with `Table` naming the table. The two states stay distinguishable —
commissioning unfinished against provisioning unfinished — which is what the provisioning order in
`postgres-instance.md` requires, and the split needs no error type of its own.

| Type | Fields | Operator sentence |
| --- | --- | --- |
| `ConnectionFileNotFoundError` | path | The connection file is not where it was expected |
| `ConnectionFileInvalidError` | path, kind (`Unreadable` \| `Unparseable` \| `MissingField` \| `OutOfRange` \| `UnknownTimeZone`), reason | The file exists but cannot be read as configuration |
| `ConnectionFileVersionMismatchError` | path, foundVersion, expectedVersion | The file is a version this build does not accept |
| `ArchiveUnreachableError` | host, port, database | No connection to the archive |
| `ArchiveDatabaseMissingError` | host, port, database | The server answers but the database does not exist |
| `ArchiveAccessDeniedError` | host, port, database, username | The credentials or the grants are wrong |
| `ArchiveNotInitialisedError` | host, port, database, table | The database is there but a table the read needs is not |
| `ArchiveQueryTimedOutError` | host, port, database, timeout (the effective server `statement_timeout` the failing session ran under, read back from that session) | The read exceeded its configured bound |
| `ArchiveReadFailedError` | host, port, database, sqlState (empty when the failure carried none) | The archive rejected the read for a reason this build does not recognise |

The three connection-file types are raised by the settings loader. The six `Archive*` types are the
vocabulary the read path maps its SQLSTATEs onto — `3D000`, `28P01` and `42P01` are separate types
rather than one schema error because they send the operator to separate remedies: run `semibase
create`, fix the credentials, start the SCADA once. `ArchiveNotInitialisedError` carries the table
name rather than assuming `trends`, because `42P01` is table-agnostic and the remedy follows the
table — `trends` is the SCADA's, `semiplot_tags` is SemiBase's. `ArchiveReadFailedError` closes the
mapping: anything the table above does not name arrives as that type carrying its SQLSTATE, so
nothing escapes as an exception and nothing crosses as an untyped `Result.Fail(string)` a consumer
cannot route on.

`57014` maps unconditionally to `ArchiveQueryTimedOutError`. The server answers that SQLSTATE both
for `statement_timeout` and for a client-issued cancel, and the chart cancels in-flight reads when it
pans, so the two will have to be told apart — but no read on the provider path takes a
`CancellationToken` yet, and a caller's own cancellation raises `OperationCanceledException`, which
the mapper rethrows rather than turning into a failed `Result`. Slice `postgres-history-read` is the
first to hand a token down and owns splitting the two.

One further type, `ProviderNotImplementedError`, exists only while `PostgresDataProvider` is a
scaffold: its unimplemented members return a failed `Result` carrying it, so a mis-wired composition
fails loudly instead of drawing an empty chart. Slice `postgres-realtime-poll` implements the last of
those members and deletes the type with it.

## Configuration

A YAML file in a `--config-dir`, following the convention of the sibling project. All nine keys are
required; the loader reports an absent one rather than defaulting it:

```yaml
connection_file_version: "1.0"
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
read path derives its own per-command backstop from the effective bound it reads back from the
session. Loading returns a `Result`; a malformed file — unreadable, unparseable, missing a key,
holding a value outside its range, or naming a zone the machine does not know — is reported at
startup rather than at first query.

The password is stored in plain text. The mitigation is that SemiPlot connects under a read-only
role, so a leaked credential exposes reading the archive and nothing else. File permissions are the
operator's responsibility.

## Keeping this document honest

Documentation that describes SQL drifts from the SQL. Three artifacts prevent that:

1. All statement text on the application and provider path lives in one class; this document quotes
   it and names the file. Nothing else on that path issues SQL. The bench seeder and the test projects
   own SQL of their own by design and are outside the rule.
2. Unit tests pin the generated statement text and parameter names for every operation, so a change
   in the code that this document does not describe shows up as a failing test.
3. A gated integration test runs `EXPLAIN` on the extent statement and asserts the plan's shape: an
   index scan under each bounded subquery, and no sequential scan of a `trends` partition holding
   rows. The plan never names `tpk` — it is the parent partitioned index of a
   `PARTITION BY RANGE (t)` table and is never scanned, so what `EXPLAIN` prints is each partition's
   own cloned `<partition>_pkey`. The shape assertion survives partition renaming and still fails the
   moment a predicate is dropped, which turns an unbounded extent minimum from a warning in prose
   into an enforced invariant. The windowed history query and the realtime poll carry the same
   hazards — the missing layer predicate and the missing variable list — and get the same treatment
   in the slices that implement them.

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
