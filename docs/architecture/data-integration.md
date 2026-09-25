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
| Knowledge of the archive's time zone | not stored anywhere | takes the machine's zone `[DEC:machine-time-zone]` | |
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
| Pen catalogue | `PenCatalog` | `semiplot_tags` left-joined through `semiplot_pen_groups` to `semiplot_groups`, the names aggregated with `array_agg` under `GROUP BY tag.id`: a pen in two groups stays one row and a pen in none survives the outer join. `ORDER BY tag.name`, because a pen has no single group to sort by. An empty table is an empty list; a missing one is `ArchiveFault.TableMissing` naming all three relations. |
| Archive extent | `ArchiveExtent` | Rooted at `semiplot_tags`, one `min(t)`/`max(t)` subquery pair per configured `id` at `l = 0`. A bare `min(t)` over `trends` cannot use `PRIMARY KEY (id, l, t)` and scans the archive. Nulls map to `ArchiveExtent.Empty`; an empty catalogue over a full archive is also `Empty`, since no pen could draw it. |
| History, coarse layers | `SparseHistoryWindow` | Two branches under one outer `ORDER BY id, t`: the window rows, and per pen one seed row strictly before `@from`, bounded to the wider of the window and one day. `HistoryRowFold` groups by consecutive identifier, so the single total ordering is what keeps each pen one run; the bound is what prunes older partitions from the seed's `Merge Append`; the seed is what keeps a steady variable on the chart as a horizontal line. |
| History, Raw | `BucketedRawWindow` | The same seed branch, and a window branch the server reduces to one row per column: `GROUP BY id, segment, date_bin(@bucket, t, @from)`. `segment` counts the `q = 32` markers strictly before each row, so a marker closes its own bucket and no bucket straddles a break. Each bucket carries `min(v)`, `max(v)`, whether it ends in a gap, and the bucket's newest non-null sample as the column's timestamp and value, which is what the legend reads at the cursor. A `q = 32` marker and a null `v` both end the bucket in a gap. Raw by construction: `l = 0`, no `@layer`. `@bucket` is `(to - from) / targetColumnCount`, never below one millisecond, the column's own resolution. |
| Realtime poll | `RealtimePoll` | `id = ANY(@ids) AND l = 0 AND t > @lastSeen ORDER BY t`. The variable list is mandatory or the read scans the day's partition; the bound is strict so the row that set `@lastSeen` is never returned twice. |
| Realtime baseline | `RealtimeBaseline` | The extent's lateral shape over `DISTINCT unnest(@ids)`: one index probe per variable. `NULL` means no row yet — a state, not a failure. |

Four statements bind parameters through a binder of their own, each pinned against its statement's
parameter names: `PostgresDataProvider.BindWindow`, `PostgresDataProvider.BindBucketedWindow`,
`RealtimePoll.BindPoll` and `RealtimePoll.BindBaseline`. A gap explanation over `messages` is
designed but not shipped; nothing issues it.

`BucketedRawWindow` reaches the window rows through the index on `(id, l, t)`, and on the bench the
planner sorts them by `(id, t)` before the window aggregate rather than reading them in index order,
at every width from two minutes to eight hours.
`ExplainPlanTests.TheBucketedRawPlanFeedsItsWindowAggregateThroughAnIndex` therefore pins what holds
either way: no sequential scan over a row-holding partition, the aggregate's input an index scan or
a bitmap over one, and the seed walk bounded as in the sparse statement's fact.

### What one history query covers

The window a query reads is wider than the window in view. `HistoryPrefetch.Expand`
(`SemiPlot.UI/Chart/HistoryPrefetch.cs`) adds one visible window width of margin on each side and
asks for three times the column target, so the fetched range stays at one column per pixel. The left
edge is clamped to the archive's first sample; the right edge is not, because a range reaching past
the live edge is a successful empty read. A new window is drawn from the last fetch while it stays
inside the inner band, half a window width in from each fetched edge, so a pan issues no query until
the margin is half spent. A zoom, a layer change and a column-target change always re-fetch,
because each changes the resolution the range was read at.

Two column counts travel on one request, and the gate reads only one of them. `HistoryRequest.Range`
carries the quantized count `ChartNavigationController.TargetColumnCount` holds, which a deadband
keeps still while the Y tick labels widen and narrow the data rect by a pixel mid-gesture;
`HistoryRequest.TargetColumnCount` carries the unquantized pixel width the provider decimates to.
The range travels on the request so the result alone says which band the envelopes in hand describe:
`TrendChartViewModel` opens the gate on the range that came back, never on the one last asked for.

A gesture that never goes quiet still fetches. `ChartHistoryRequestDebouncer` merges the trailing
`Throttle` of 150 ms with a `Sample` of 400 ms over the same requests and drops the request whose
window the last applied result already covers, so a continuous drag issues one query per 400 ms and
one more after it stops. A read that is still in flight or that came back failed covers nothing, so
the same window asked for again reaches the archive.

One query runs at a time, and the newest request that arrived while it ran runs when it lands. A read
slower than the cap interval therefore still completes during the gesture, the server sees one
history query per chart, no window is read for nothing, and the last window a gesture asked for is
the last one applied. The window waiting behind a query is dropped when that query turns out to have
applied it. The provider takes no `CancellationToken`, and this shape needs none: the query in flight
is never abandoned.

A failed read reaches the chart. `ChartHistoryRequestDebouncer` delivers the `Result`'s errors on the
UI scheduler, a thrown query joining the same channel as an `ExceptionalError`;
`TrendChartViewModel.OnHistoryQueryFailed` logs them and re-applies the axis over the envelopes still
in hand. Skipping the axis there would freeze it with no way back, because the gate that would ask
again opens only on a result. A throw out of the apply, and any fault the pipeline itself raises,
reach the same channel: without that the subscription issuing every history query would tear down,
and the chart would stop reading the archive for the rest of the session.

The margin makes the Raw read smaller in rows returned, not cheaper on the server: the window
function still reads and sorts every row of the range, and the range is three visible windows wide.
`PostgresHistoryReadTests.TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed` records
`EXPLAIN (ANALYZE, TIMING OFF)` for the widest read the margin can ask for, and the plan's row counts
and timing are what say what it costs.

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
  row covers is not a gap and would draw as one straight segment. The tail's own raw read stays
  row-level: it issues `SparseHistoryWindow` at `l = 0`, not the bucketed statement, because the tail
  spans at most four coarse point spacings and no canvas is denser than that.

Correctness of the envelope at every layer rests on the vendor's selection preserving each period's
extremes `[FORUM:1974]` — well supported, not yet measured (`scada-archive.md`, open questions).

## Time boundary

The archive stores naive local wall-clock time; everything above the provider is UTC.

- Reading: `t` is interpreted in the machine's time zone and converted to
  `DateTime(Kind = Utc)` by `ArchiveTimeConverter.ToUtc`.
- Query bounds: UTC window edges are converted back to naive local (`ToArchiveLocal`) before binding.
- The conversion happens only at the provider edge. Display-local rendering is a separate, later
  conversion in `LocalTimeAxis`.

The database does not record the zone. The SCADA, its archive and the viewer share one machine, so
the provider converts in that machine's zone `[DEC:machine-time-zone]`: `PostgresConnectionLoader`
fills `PostgresConnectionSettings.SourceTimeZone` with `TimeZoneInfo.Local`, and no file names a zone.
Tests that build the settings directly pass a fixed zone through the same property. Daylight-saving
transitions are accepted as cosmetic, and the archive stores no offset to make them anything else.
`HistoryRowFold` keeps a row only when its converted timestamp exceeds the previous kept one for
that pen, which is what keeps the envelope strictly ascending: at the spring gap that drops the one
or two rows the conversion put out of order; at the autumn fall-back it drops the whole repeated
hour, for every pen, once a year, and stamps the surviving hour an hour late. Pinned by
`HistoryRowFoldTests.TheSecondPassOverTheRepeatedHourIsDropped`. `BucketedRowFold` drops a
non-advancing bucket by the same rule.

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

On the Raw path the marker travels as the bucket's `breaks` flag and `BucketedRowFold` writes the
`NaN` column itself: the server already reduced the window, so no decimator pass follows to split a
series on a null.

A null `v` sets the same flag, because a bucket is the finest resolution this path has: `min(v)`,
`max(v)` and the newest-value aggregate all skip a null, so without the flag a bucket holding data
and nulls together would come back as an ordinary column and the line would run straight across the
null run. The column's timestamp is the newest **non-null** sample's, coupled to the value beside
it; a bucket of nothing but nulls keeps `max(t)` and reads back `NaN` in all three series. The
coarse path splits at the null itself rather than at the bucket, so the two paths agree that a null
is a hole and differ only in where the hole starts. `scada-archive.md` records that `v` was never
null in the measured archive, so no integration fact can reach this: the seeded archive writes no
nulls either, and `ArchiveStatementTextTests` plus `BucketedRowFoldTests` pin the rule instead.

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

`MainWindow/AppStatusBarViewModel` renders the state: the indicator reads connected or not, and
every fault is mapped by `Messages/ArchiveFailureMapper.Map` into an entry in the message panel. The
bar has a single writer, the stream it binds once through `TrackArchiveConnection`. Because every
subscription's first tick reports `Connected`, the recovery entry is written only once a fault has
been seen — otherwise every launch would announce a connection it never lost. The handler is
wrapped: `TrendCoordinator` forwards this stream with a bare `Subscribe`, so a throw out of the
handler would end the forwarding for the rest of the session instead of reaching an `onError`.

## Error semantics

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | The main window opens with the chart empty and its startup-failure panel titled "No connection to the archive", naming the host and port, with a remedy |
| Connection lost mid-session | failed `Result` on the query; realtime tick dropped | Chart keeps the data it has |
| Three consecutive realtime ticks fail | `ArchiveFault.ConnectionLost` on `ConnectionFaults`; the observable keeps running | The status indicator turns to the fault state and one `Warning` entry appears; the first tick that succeeds restores the indicator and adds one `Info` entry |
| A column the read needs is absent (`42703`) | `ArchiveFault.ShapeUnexpected` with the server's detail | "The archive has an unexpected shape" — run `semibase site`, then find what altered the table |
| Query timeout (`57014`) | `ArchiveFault.QueryTimedOut` | The server ended the read; `statement_timeout` is the `semiplot` role's own setting |
| The database does not exist (`3D000`) | `ArchiveFault.DatabaseMissing` | "The archive is not provisioned" — run `semibase site` |
| Credentials refused or a grant missing (`28P01`, `28000`, `42501`) | `ArchiveFault.AccessDenied` | "The archive refused the credentials" — the user, password or grants |
| A relation a read needs does not exist (`42P01`) | `ArchiveFault.TableMissing` whose detail names every relation that read touches | "The archive is not provisioned" — run `semibase site`, which creates them all |
| `semiplot_tags` present but empty | empty pen list, success | The chart area's own empty state — no key has been registered as a pen yet. Not a message-panel entry: an empty catalogue is a state, not something that happened |
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
| `ConnectionFileError` | path, kind (`Unparseable` \| `MissingField` \| `OutOfRange` \| `HostNotIPv4`), reason |
| `ConfigurationSectionError` | section (`App` \| `Connection`), directory, problem (`DirectoryMissing` \| `NoFiles` \| `Unlistable` \| `Unreadable` \| `DuplicateKey` \| `KeyConflict` \| `Unwritable` \| `KeyAbsent`), key, file names |
| `ArchiveError` | kind (`ArchiveFault`), host, port, database, detail |

`ConfigurationSectionError` lives in `SemiPlot.Core/Configuration/` and is raised by the section
reader, ahead of any typed deserialize, so reaching the archive at all is not a precondition for it,
and by the settings save: `Unwritable` for a target or staging folder it cannot write, `KeyAbsent` for
an edited key no file carries (`overview.md#the-settings-window`).
Reading a section folder is the only file access left on the connection path, which is why
`ConnectionFileError` no longer carries a file-access kind.

| `ArchiveFault` | Raised by | Detail |
| --- | --- | --- |
| `Unreachable` | a socket failure, a client bound firing, any `NpgsqlException` without a SQLSTATE | empty |
| `AccessDenied` | `28P01`, `28000`, `42501` | the username |
| `DatabaseMissing` | `3D000` | empty |
| `TableMissing` | `42P01` | every relation the failing statement touches: `trends` for the history and realtime reads, `semiplot_tags, trends` for the archive extent, all three catalogue tables for the pen catalogue |
| `ShapeUnexpected` | `42703` | the server's own message |
| `QueryTimedOut` | `57014` | empty |
| `ConnectionLost` | three consecutive failed poll ticks | the number of failures that raised it |
| `ReadFailed` | any other SQLSTATE, or a client-side throw | the SQLSTATE, or empty |

`ArchiveFailureMapper` (`SemiPlot.UI/Messages/`) turns each kind into a title, a detail, a remedy
and a `MessageSeverity`, and is the one place a remedy is written. The severity is decided in the
mapper rather than at the call site, so a future error type gets one from the `MapUnknown` arm
without anyone remembering: `AccessDenied`, `TableMissing`, `DatabaseMissing` and `ShapeUnexpected`
are `Error`, because nothing recovers until someone changes a grant, a schema or a provisioning run;
`Unreachable`, `ConnectionLost`, `QueryTimedOut` and `ReadFailed` are `Warning`, because the poll
loop retries by itself. `ConnectionLost` never opens the startup failure panel; it is an entry in the
message panel under a chart that works.

The eight rows above are the `ArchiveError` arm alone. `Map` has eight further arms — the startup
arguments, the log file, the configuration section, the app settings, the connection file, the
startup read timeout, an `IExceptionalError` and the `_` fallback — and every one of them is
`Error`, because each is reachable only at startup, where nothing recovers until the operator edits
something. `MessageSeverity.Info` has exactly one writer in the whole tree, the connection-restored
entry `AppStatusBarViewModel` writes. `SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`
holds one table per enum the two mappers switch on and asserts each table covers `Enum.GetValues`,
so a new member leaves a table short and turns red.

### No failure stops at the log

Every failure on the data path reaches the operator through `Messages/MessagePanelViewModel`, not
only the log. The three that used to log a warning and return now report as well: the failed history
query in `Chart/TrendChartViewModel` (which still runs its `ApplyAxisModel` / `RequestRedraw`
recovery afterwards), the failed extent query in `Minimap/MinimapViewModel`, and the unobserved
`LoadExtentAsync` task the composition root starts. `ApplyRealtimeBatch` carries the
try-report-continue shape that `Chart/ChartHistoryRequestDebouncer.Deliver` applies to the history it
hands on, so one bad batch does not end the realtime subscription. `Deliver` guards that hand-off
only: the result it reports instead is reported by a handler that guards itself.

`Messages/ResultReporting` treats the log and the panel as two independent sinks: the panel edit runs
first and the log line from a `finally`, so whichever of the two refuses the failure, the other still
has it. The panel's `Report` answers whether it coalesced, and that answer picks the log level, so the
demotion rule lives in one place rather than being evaluated twice. A result carrying several errors
becomes one entry from `Errors[0]` and the rest reach the log as warnings, which is the overload the
chart's history failures take as well. `ReportFailure` lets a refusal reach its caller;
`TryReportFailure` is the form for a caller whose escape would end an Rx stream or a dispatcher job,
and it logs the refusal once, guarded, because the log sink itself is one of the two things that can
have thrown. Its scheduler overload carries the report to the UI thread first, for the two callers
that report from a thread of their own.

A handler that runs detached — an Rx `onNext`, an Rx `onError`, a job posted to the UI scheduler —
wraps its whole body, not its report alone, and hands the throw to `TryReportFailure`:
`TrendChartViewModel.OnHistoryQueryFailed`, `Minimap/MinimapViewModel.ApplyExtent` and
`MainWindow/AppStatusBarViewModel.ApplyConnectionState`. The guard belongs to the handler rather than
to whoever invokes it, because the recovery around the report would otherwise escape the same way the
report can. `TrendChartViewModel.ReportFailure`, `MainWindowViewModel.ReportFailure`, the
`LoadExtentAsync` continuation and the ReactiveUI observer call the guarded form directly.

A history query reissued on every pan is what the panel's coalescing exists for: during an outage a
ten-second drag produces roughly twenty-five identical failures, and they become one entry with a
repeat count and a moving last-seen time. Coalescing is against the newest entry only, so a
different failure in between starts a new one, and the key is the whole mapped view rather than its
title. The list is capped at `MessagePanelViewModel.MaximumEntries` (200) with the oldest dropped:
this viewer runs for weeks. The operator's shortest way to it is the status bar's connection
indicator, a `Button` bound to the panel's own `ToggleCommand` — the same command the View menu
invokes. `overview.md` holds the panel's place in the window and the reach of the ReactiveUI
exception observer under it.

`TrendCoordinator.RealtimeBatches` needs one more guard than the connection stream does. Its
projection runs inside `Select`, and Rx turns a throwing projection into `OnError`, which would end
the `Publish().RefCount()` stream for every subscriber. `TryBuildRealtimeBatch` catches instead,
skips the window and publishes the exception on `TrendCoordinator.RealtimeFailures`. A terminal
failure out of the provider takes the same channel: a `Catch` ahead of the `Publish` turns it into
one `RealtimeFailures` message and completes the batch stream, so `RealtimeBatches` never faults and
the one failure is not counted once per subscriber of it. `TrendChartViewModel` is the only reader of
that channel, and it reports through the guarded `ResultReporting.TryReportFailure` because an escape
would end the subscription that feeds the live edge. `TrendChartView`'s own three subscriptions report
through
`TrendChartViewModel.ReportFailure`, because a pipeline that ends stops repainting the chart.

`57014` maps unconditionally to `QueryTimedOut`: no member of `IDataProvider` takes a
`CancellationToken`, so no read on the provider path is cancelled by a caller, and a caller's own
cancellation raises `OperationCanceledException`, which the mapper rethrows.

## Configuration

The `connection/` section folder under `--config-dir`, which is a required launch key with no
default (`overview.md`). `ConfigurationSection.Read` merges every `*.yaml` in that folder into one
mapping and `PostgresConnectionLoader` deserializes it. Every key but `schema` is required; an
absent required key is reported, an unknown key ignored. `schema` defaults to `public` when absent:

```yaml
host: 127.0.0.1
port: 5432
database: semiplot_dev
user: semiplot
password: "change me"
poll_interval_ms: 1000
```

The set that ships is `ConfigFiles/connection/connection.yaml` in the repository, with an empty
`password` so it cannot start unedited, and `SemiPlot.Tests.Unit/DeliveredConfigurationTests` runs
this loader over it.

`host` is an IPv4 address of exactly four decimal numbers from 0 to 255, such as `127.0.0.1`.
`PostgresConnectionLoader.IsIPv4Address` is the one rule: the loader refuses anything else with
`HostNotIPv4`, and the settings window disables its save on the same predicate. A host name,
`localhost` included, an IPv6 address, a shortened form such as `127.1`, and an octet with a leading
zero, which some resolvers read as octal, are all refused at startup rather than at the first connect.
`IPAddress.TryParse` is not the rule, because it accepts `1` and `127.1`.

The file names no time zone: the provider reads the archive in the machine's zone (Time boundary,
above). The viewer therefore runs on the SCADA machine, and `host` is normally `127.0.0.1`. A viewer
pointed at a SCADA machine in another zone shifts every reading by the difference between the two
zones, and no key overrides the zone `[DEC:machine-time-zone]`.

The file states no query bound: `statement_timeout` belongs to the `semiplot` role and SemiBase owns
it (`postgres-instance.md`). The connection
string carries `Command Timeout=300` as a client backstop; the live-edge poll uses a 10 s bound of
its own on every tick. Loading returns a `Result`; a malformed file is reported at startup, not at
first query. The password is stored in plain text; the mitigation is a role that cannot write the
archive.

## Startup

Startup splits at the Avalonia boundary because `AfterSetup` is synchronous: a blocking read inside
it would hold Avalonia's setup. `StartupSequence.Run` (`SemiPlot.UI/Startup/StartupSequence.cs`)
therefore holds the ordered blocking steps and `Program.Main` calls it ahead of
`BuildAvaloniaApp()`, while the reads `InitializeServices` starts inside `AfterSetup` are
asynchronous.

`StartupOptions.Parse(args)` runs ahead of all of it, because the logger's own path is an argument.
It returns `Result<StartupOptions>`, and on failure `Program.Main` applies the bootstrap culture,
creates no logger, opens the failure window through `App.Run(null, failure, configDirectory: null)`
and returns 1. On
success `LogFileTarget.Prepare` opens the file that `--log-file` names, creating its folder, and
takes the same route on failure: Serilog's file sink reports its own open failure only to
`Serilog.Debugging.SelfLog` and then writes nowhere, so a mistyped path would otherwise start the
viewer with no log and no report. Only then is the logger created, and `Program.Main` writes one
Information line naming the configuration directory and the logging level; `StartupProbe.Run` writes
one more naming the time zone once the connection section loads. A healthy run reaches Information
nowhere else, so above that level the file `Prepare` opened stays empty until the first
failure.

`StartupSequence.Run` then takes these steps in order:

1. Set the bootstrap UI culture to Russian, so a failure naming the settings section itself can be
   read.
2. Load the `<ConfigDir>/app` section and apply its `locale`. A failure here short-circuits with null
   settings, before the connection section is touched, so a broken archive cannot mask a broken
   configuration (`ui-text.md`, `ui-theme.md`).
3. `StartupProbe.Run`: load the `<ConfigDir>/connection` section and register
   `AddPostgresData(settings)`.
4. Resolve `IDataProvider`, read the pen catalogue, then the archive extent.

The container, the pens and the extent cross the boundary in a `StartupData` record inside a
`Result`, so `App.InitializeServices` awaits nothing. `Program.Main` passes the settings and that
`Result` and the configuration directory to `App.Run(AppSettings?, Result<StartupData>, string?)`
unconditionally, the directory reaching the settings window on both paths: on success it runs as
today; on failure `App` maps the error through `ArchiveFailureMapper` and opens the main window with
`MainWindowViewModel.StartupFailure` set — the
startup-failure panel names what broke and what to do, and the chart, legend and minimap bind to null and
render empty, because `CreateMainWindow` builds that view model without a service provider. There is
no second data source to fall back to: synthetic data would let an operator read invented numbers as
process data. `Program.Main` returns 1 once that window closes.

- **The startup reads are bounded by the caller**, `Task.WaitAsync` at `StartupProbe.DefaultReadBound`
  (30 s). The expiring bound is `StartupReadTimedOutError`, distinct from `ArchiveFault.QueryTimedOut`,
  which means the server ended the read.
- **The read bound sits above the connect timeout** (`PostgresConnectionSettings.ConnectTimeoutSeconds`,
  15 s), so an unreachable host fails inside the connect attempt and reports `Unreachable` rather
  than a timeout. `StartupProbeTests.DefaultReadBound_StaysAboveTheConnectTimeout` pins the ordering.
- **A throw on the startup path is a failed `Result`.** `StartupProbe.ReadAsync` catches, logs with
  the stack, disposes the container and returns an `ExceptionalError`, which `ArchiveFailureMapper`
  maps through its `IExceptionalError` arm.

An empty pen catalogue is a successful start: the window opens, draws nothing, and the chart area
states that the catalogue is empty (`Chart/TrendChartViewModel.HasNoPens`). It is a state of the
chart, not an entry in the message panel. Logging is configured before the probe runs; the log path
and the argument list are in `overview.md`.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, provisioning did not complete: `semibase site` creates it and
   `semiplot_tags` in one run.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn.
5. Does the window overlap the data? Compare against the extent. An offset of a whole number of
   hours means the SCADA stamps rows in a zone other than the machine's, and
   `[DEC:machine-time-zone]` does not hold on this installation (`sources.md`). At `information` or more
   verbose, the startup log line `Reading the archive in the time zone ...` names the zone the viewer
   applied.
6. Is `tpdefault` non-empty? Rows there mean the SCADA failed to create a daily partition. They are
   not a cause of an empty chart — every read still returns them — but partition elimination is
   lost for reads that cannot skip that partition.
