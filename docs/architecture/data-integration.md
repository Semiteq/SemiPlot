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
project, so Core never references a data source. `PostgresDataProvider` in
`SemiPlot.DataSource.Postgres` is the only implementation; tests build fakes against the interface.

```csharp
public interface IDataProvider
{
    // Cold per call: no samples flow until subscribed; the subscriber disposes the returned IDisposable.
    IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

    // Hot, shared by every subscription, never completes and never faults.
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
| `ArchiveExtent` | `FirstUtc`, `LastUtc`, `IsEmpty` | The span of the configured variables, consumed by the minimap (`TM-4`). `ArchiveExtent.Empty` is the no-span form. |
| `AggregationLayer` | `Raw`, `Minute`, `Hour`, `Day` | Maps one-to-one onto the archive's `l` column. |
| `ArchiveConnectionState` | `Fault`, `IsConnected` | `Fault` is null while the archive answers and carries the typed error while it does not. |

Contract points every implementation keeps:

- `QueryHistoryAsync`'s window is half-open — `fromUtc` inclusive, `toUtc` exclusive. The order of
  the envelopes is unspecified; consumers key by `PenId`.
- A pen with neither a window row nor a seed row gets no envelope rather than an empty one.
  `TrendChartViewModel.ApplyHistory` clears the curve of every requested pen the result omits.
- Every read answers with a `Result`. Only two things leave the provider as exceptions:
  `ArgumentNullException` for a null `penIds` and `ArgumentOutOfRangeException` for an undefined
  `AggregationLayer` — both defects in the caller — plus `OperationCanceledException`, which
  `ArchiveExceptionMapper` rethrows rather than mapping. Nothing else crosses to the UI thread
  (`DA-1`).
- An inverted window, a target column count below one, or a pen identifier outside the archive's
  32-bit range is a failed `Result` with a plain message.

## Operation to statement

Every statement on the provider path is a constant in
`SemiPlot.DataSource.Postgres/ArchiveStatements.cs`; parameters are always bound. The bench seeder
and the test harness own SQL of their own (`bench.md`). `ArchiveStatementTextTests` pins each
constant clause by clause; `ExplainPlanTests` asserts each plan's shape against a container.

| Operation | Constant | What the statement must keep, and why |
| --- | --- | --- |
| Pen catalogue | `PenCatalog` | `ORDER BY coalesce(group_name, ''), name`: `group_name` is nullable and `Pen.Group` is not, so the ordering coalesces the way the read does. An empty table is an empty list; a missing one is `ArchiveFault.TableMissing` naming `semiplot_tags`. |
| Archive extent | `ArchiveExtent` | Rooted at `semiplot_tags`, one `min(t)`/`max(t)` subquery pair per configured `id` at `l = 0`. A bare `min(t)` over `trends` cannot use `PRIMARY KEY (id, l, t)` and scans the archive. Nulls map to `ArchiveExtent.Empty`; an empty catalogue over a full archive is also `Empty`, since no pen could draw it. |
| History | `SparseHistoryWindow` | Two branches under one outer `ORDER BY id, t`: the window rows, and per pen one seed row strictly before `@from`, bounded to the wider of the window and one day. `HistoryRowFold` groups by consecutive identifier, so the single total ordering is what keeps each pen one run; the bound is what prunes older partitions from the seed's `Merge Append`; the seed is what keeps a steady variable on the chart as a horizontal line. |
| Realtime poll | `RealtimePoll` | `id = ANY(@ids) AND l = 0 AND t > @lastSeen ORDER BY t`. The variable list is mandatory or the read scans the day's partition; the bound is strict so the row that set `@lastSeen` is never returned twice. |
| Realtime baseline | `RealtimeBaseline` | The extent's lateral shape over `DISTINCT unnest(@ids)`: one index probe per variable. `NULL` means no row yet — a state, not a failure. |

Three statements bind parameters through a binder of their own — `PostgresDataProvider.BindWindow`,
`RealtimePoll.BindPoll`, `RealtimePoll.BindBaseline` — each pinned against its statement's
parameter names. A server-side bucketed read (`date_bin` per pixel column) and a gap explanation
over `messages` are designed but not shipped; nothing issues either.

## Layer ladder

The archive offers four resolutions; the renderer needs about one point per pixel column. The rule:
**choose the coarsest layer whose point spacing still fits inside one pixel column.** Point spacing
is one quarter of the period, following the vendor's budget of four points per period `[FORUM:1032]`
(`AggregationLayerExtensions.ToPointSpacing`):

| Layer | `l` | Period | Point spacing |
| --- | --- | --- | --- |
| `Raw` | 0 | — | the archiving interval |
| `Minute` | 1 | minute | 15 s |
| `Hour` | 2 | hour | 15 min |
| `Day` | 3 | day | 6 h |

`ChartNavigationController` expresses that as an upper bound per layer,
`ceiling(layer) = nextCoarser(layer).ToPointSpacing() × TargetColumnCount`, so the raw layer's own
spacing — the SCADA's per-variable archiving interval, which the client cannot know — never enters
the comparison.

The column count is live. `HistoryColumnTarget.FromDataAreaWidth` maps the view's data-area width
to 256…2048 columns; `ChartNavigationController.SetTargetColumnCount` quantises that to the nearest
power of two with a 10 % deadband, and only the quantised count moves the ceilings or triggers a
re-query. The history query keeps the unquantised count, because that decides resolution rather than
layer. Inside the deadband the drawn resolution can lag the canvas by up to `2 × 1.1² ≈ 2.42`; the
next navigation gesture closes the gap. At 2048 columns the `Day` layer is unreachable under the
365-day window cap (`TrendNavigationModel`): its ceiling would start at 512 days, and the hour layer
is the correct read there.

Two adjustments the ladder needs are implemented:

- **Hysteresis.** Layers switch on thresholds separated by a margin, so a window hovering on a
  boundary does not flip layer on every wheel notch.
- **Fresh tail** (`FreshTail`, in the provider). Coarse layers are flushed on their own cadence, so a
  window reaching "now" is short of up to one point spacing at its right edge. The provider reads
  that edge from `l = 0` and merges it per pen. The seam is per pen — the newest coarse timestamp
  returned, or the window start when none was. A layer fresh within one of its own points reads no
  tail; otherwise the tail starts at the earliest seam, clamped to four point spacings back from the
  window end. A pen whose seam precedes the tail's start contributes no tail row, because a range no
  row covers is not a gap and would draw as one straight segment.

Correctness of the envelope at every layer rests on the vendor's selection preserving each period's
extremes `[FORUM:1974]` — well supported, not yet measured (`scada-archive.md`, open questions).

## Time boundary

The archive stores naive local wall-clock time; everything above the provider is UTC.

- Reading: `t` is interpreted in the configured `source_time_zone` and converted to
  `DateTime(Kind = Utc)` by `ArchiveTimeConverter.ToUtc`.
- Query bounds: UTC window edges are converted back to naive local (`ToArchiveLocal`) before binding.
- The conversion happens only at the provider edge. Display-local rendering is a separate, later
  conversion in `LocalTimeAxis`.

The zone lives in configuration because the database does not record it. Daylight-saving
transitions are accepted as cosmetic, and the archive stores no offset to make them anything else.
`HistoryRowFold` keeps a row only when its converted timestamp exceeds the previous kept one for
that pen, which is what keeps the envelope strictly ascending: at the spring gap that drops the one
or two rows the conversion put out of order; at the autumn fall-back it drops the whole repeated
hour, for every pen, once a year, and stamps the surviving hour an hour late. Pinned by
`HistoryRowFoldTests.TheSecondPassOverTheRepeatedHourIsDropped`.

## Quality and gaps

The provider maps the archive's quality marks onto the envelope's gap representation (`DA-8`):

| Archive | Envelope |
| --- | --- |
| `q = 0` | ordinary point |
| `q = 32` (last sample before a break) | point kept, then a `NaN` anchor inserted after it |
| `q = 16` (first sample after a break) | point kept; the line resumes here |
| absence of rows without a preceding `q = 32` | no anchor — the value simply did not change |

The last row is the whole reason the marks exist: treating every row gap as a break would shred a
steady signal, and ignoring the marks would draw a straight line across hours of missing data.

`HistoryRowFold` appends, after a kept `q = 32` row, one entry at the row's converted timestamp plus
one tick with a null value; `MinMaxDecimator` splits its series on that null and emits the `NaN`
column. One tick is below `timestamp(3)` resolution, so no real row lands inside it, and it is added
on the UTC side, clear of daylight-saving boundaries. `q = 16` takes no branch: a resumption is what
the decimator already produces past a null segment. A seed row carries its own `q`, so a seed marked
`32` opens the window inside a gap. `RealArchiveGapTests` drives the fold with rows lifted from the
customer dump, including a 4 min 18 s absence carrying no marker.

## Realtime

`Subscribe` returns a cold observable: each subscription runs a poll loop of its own on the injected
data scheduler, at the operator's `poll_interval_ms`, holding a baseline of its own and carrying the
variable list in every query (`RT-1`). Disposing the subscription cancels the loop. Batching, the
union timeline and the hand-off to the UI scheduler happen above the provider, in `TrendCoordinator`
(`charting.md`).

The first tick reads the baseline and emits nothing; every later tick reads the rows past
`lastSeen`, converts them to UTC and emits them. `lastSeen` is the archive's own naive clock, not
the local machine's, so a clock difference between the two hosts drops or repeats nothing.

- **The sequence never completes and never faults.** A query error logs, drops that tick's rows and
  leaves the observable running.
- **No timestamp is emitted at or before the last one already delivered** (`DA-7`).
- **A row whose `v` is null is dropped, and `lastSeen` still advances past it.** `Sample.Value` is
  non-nullable.
- **A `q = 32` row opens no gap here.** `Sample` carries no null to rebuild one with; a break at the
  live edge draws as a held line until the next history read covers it.

### The connection state the poll reports

`ConnectionFaults` is hot, shared by every subscription and never terminating.

- Every subscription's first successful tick reports `Connected`, and so does the first success
  after a raised fault; ordinary ticks report nothing. That first report is how a consumer knows a
  subscription is armed, so nothing filters the stream with `DistinctUntilChanged`.
- A fault is raised after three consecutive failed ticks, not one: Npgsql opens a fresh physical
  connection after a reset, so one failed tick is ordinary. The state carries
  `ArchiveFault.ConnectionLost` with the host, port, database and the threshold that raised it.
- The fault is raised once per outage and carries the threshold, not a running count. The poll
  reports nothing further until a tick succeeds.
- A self-cancelled read is not a failure: disposal's `OperationCanceledException` ends the loop
  ahead of the mapper.

`MainWindowViewModel` renders the state as one row of the archive banner, through
`StartupFailureMapper.Describe(fault)`; that row has a single writer, the bound stream.

## Error semantics

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | `ErrorWindow` titled "No connection to the archive", naming the host and port, with a remedy and one **Close** button |
| Connection lost mid-session | failed `Result` on the query; realtime tick dropped | Chart keeps the data it has |
| Three consecutive realtime ticks fail | `ArchiveFault.ConnectionLost` on `ConnectionFaults`; the observable keeps running | A banner row over the chart, cleared by the first tick that succeeds |
| A column the read needs is absent (`42703`) | `ArchiveFault.ShapeUnexpected` with the server's detail | "The archive has an unexpected shape" — run `semibase site`, then find what altered the table |
| Query timeout (`57014`) | `ArchiveFault.QueryTimedOut` | The server ended the read; `statement_timeout` is the reader role's own setting |
| The database does not exist (`3D000`) | `ArchiveFault.DatabaseMissing` | "The archive is not provisioned" — run `semibase site` |
| Credentials refused or a grant missing (`28P01`, `28000`, `42501`) | `ArchiveFault.AccessDenied` | "The archive refused the credentials" — the user, password or grants |
| `trends` or `semiplot_tags` does not exist (`42P01`) | `ArchiveFault.TableMissing` whose detail names the table | "The archive is not provisioned" — run `semibase site`, which creates both |
| `semiplot_tags` present but empty | empty pen list, success | A row stating the catalogue is empty — commissioning is not finished |
| Archive present but no rows in the window | success, empty envelope list | Empty chart, no error |

### Two error planes

Inside the provider a failure is whatever Npgsql, the file system or the YAML parser produced. At
the boundary it is mapped onto one of two sealed public types in `SemiPlot.Core/Data/Errors/`, with
the original riding `.CausedBy(...)` for the log. The contract is the type and its fields; messages
are built in the base constructor and reach no operator, so tests assert on type and fields, never
on message text.

The rule that decides whether a type exists: **a public error type exists if and only if a distinct
operator-visible failure sentence exists.** Operator-visible states that are not failures — an
empty window, an empty `semiplot_tags` — travel in the success channel.

| Type | Fields |
| --- | --- |
| `ConnectionFileError` | path, kind (`NotFound` \| `Unreadable` \| `Unparseable` \| `MissingField` \| `OutOfRange` \| `UnknownTimeZone`), reason |
| `ArchiveError` | kind (`ArchiveFault`), host, port, database, detail |

| `ArchiveFault` | Raised by | Detail |
| --- | --- | --- |
| `Unreachable` | a socket failure, a client bound firing, any `NpgsqlException` without a SQLSTATE | empty |
| `AccessDenied` | `28P01`, `28000`, `42501` | the username |
| `DatabaseMissing` | `3D000` | empty |
| `TableMissing` | `42P01` | the relation the failing statement touches |
| `ShapeUnexpected` | `42703` | the server's own message |
| `QueryTimedOut` | `57014` | empty |
| `ConnectionLost` | three consecutive failed poll ticks | the number of failures that raised it |
| `ReadFailed` | any other SQLSTATE, or a client-side throw | the SQLSTATE, or empty |

`StartupFailureMapper` (`SemiPlot.UI/Startup/`) turns each kind into a title, a detail and a remedy,
and is the one place a remedy is written. `ConnectionLost` never opens the error window; it is a
banner row over a chart that works. `StartupFailureMapperTests` enumerates both enums and fails when
a member maps to the catch-all arm.

`57014` maps unconditionally to `QueryTimedOut`: no member of `IDataProvider` takes a
`CancellationToken`, so no read on the provider path is cancelled by a caller, and a caller's own
cancellation raises `OperationCanceledException`, which the mapper rethrows.

## Configuration

A YAML file named `archive-connection.yaml`, read from `C:\DISTR\Config\SemiPlot` unless
`--config-dir` names another directory. All keys are required; an absent one is reported, an unknown
one ignored:

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

`source_time_zone` takes any identifier `TimeZoneInfo.FindSystemTimeZoneById` resolves on the
machine running the viewer: an IANA name such as `Europe/Berlin`, or on Windows the id `tzutil /g`
prints. The file states no query bound: `statement_timeout`
belongs to the `semiplot_reader` role and SemiBase owns it (`postgres-instance.md`). The connection
string carries `Command Timeout=300` as a client backstop; the live-edge poll uses a 10 s bound of
its own on every tick. Loading returns a `Result`; a malformed file is reported at startup, not at
first query. The password is stored in plain text; the mitigation is the read-only role.

## Startup

Startup splits at the Avalonia boundary because `AfterSetup` is synchronous: a blocking read inside
it would hold Avalonia's setup. `StartupProbe` (`SemiPlot.UI/Startup/StartupProbe.cs`) therefore
runs in `Program`, ahead of `BuildAvaloniaApp()`, and the reads `InitializeServices` starts inside
`AfterSetup` are asynchronous:

1. Load `<ConfigDir>/archive-connection.yaml` and register `AddPostgresData(settings)`.
2. Resolve `IDataProvider`, read the pen catalogue, then the archive extent.

The container, the pens and the extent cross the boundary in a `StartupData` record, so
`App.InitializeServices` awaits nothing. A failed step disposes the container and carries its error to
`Program`, which maps it through `StartupFailureMapper` and opens `ErrorWindow` in place of the main
window. There is no second data source to fall back to: synthetic data would let an operator read
invented numbers as process data.

- **The startup reads are bounded by the caller**, `Task.WaitAsync` at `StartupProbe.DefaultReadBound`
  (30 s). The expiring bound is `StartupReadTimedOutError`, distinct from `ArchiveFault.QueryTimedOut`,
  which means the server ended the read.
- **The read bound sits above the connect timeout** (`PostgresConnectionSettings.ConnectTimeoutSeconds`,
  15 s), so an unreachable host fails inside the connect attempt and reports `Unreachable` rather
  than a timeout. `StartupProbeTests.DefaultReadBound_StaysAboveTheConnectTimeout` pins the ordering.
- **A throw on the startup path is a failed `Result`.** `StartupProbe.ReadAsync` catches, logs with
  the stack, disposes the container and returns an `ExceptionalError`, which `StartupFailureMapper`
  maps through its `IExceptionalError` arm.

An empty pen catalogue is a successful start: the window opens, draws nothing, and states that the
catalogue is empty (`MainWindowViewModel.IsCatalogueEmpty`). Logging is configured before the probe
runs; the log path and the argument list are in `overview.md`.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, provisioning did not complete: `semibase site` creates it and
   `semiplot_tags` in one run.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn.
5. Does the window overlap the data? Compare against the extent, and suspect a `source_time_zone`
   mismatch if the offset looks like a whole number of hours.
6. Is `tpdefault` non-empty? Rows there mean the SCADA failed to create a daily partition. They are
   not a cause of an empty chart — every read still returns them — but partition elimination is
   lost for reads that cannot skip that partition.
