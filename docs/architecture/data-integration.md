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

The UI reads through `IDataProvider` (`SemiPlot.Core/Data/IDataProvider.cs`) and the pen editor alone
writes, through `IPenCatalogueEditor` (The pen catalogue editor, below). Both interfaces and their
DTOs live in `SemiPlot.Core`; each concrete provider is a sibling `SemiPlot.DataSource.*`
project, so Core never references a data source. `PostgresDataProvider` in
`SemiPlot.DataSource.Postgres` is the only implementation of `IDataProvider`; tests build fakes
against the interface.

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
        int targetColumnCount,
        CancellationToken cancellationToken = default);

    Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();
}
```

| Type | Shape | Notes |
| --- | --- | --- |
| `Pen` | `PenId`, `Name`, `Groups`, `Color`, `Unit`, `Format`, `EnabledOnStart`, `ScaleMinOnStart`, `ScaleMaxOnStart`, `LineStyle` | `PenId` is the archive's `trends.id`. Equality compares `Groups` element by element, ordinal, so two reads of one stored pen are equal. |
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
- A history read whose `cancellationToken` is cancelled ends in `OperationCanceledException`, never in
  a failed `Result`. The chart treats only that exception, while its own token is cancelled, as its
  own cancellation.

## The pen catalogue editor

The write path is a second interface, `IPenCatalogueEditor` (`SemiPlot.Core/Data/IPenCatalogueEditor.cs`),
and `IDataProvider` stays read-only. The interface declares everything the editor window may do and
nothing more:

```csharp
public interface IPenCatalogueEditor
{
    Task<Result<PenCatalogue>> ReadAsync();
    Task<Result<int>> RegisterNewPensAsync();
    Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change);
    Task<Result<int>> CreateGroupAsync(string name);
    Task<Result> RenameGroupAsync(StoredGroup group, string name);
    Task<Result> DeleteGroupAsync(StoredGroup group);
    Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember);
}
```

No member adds a pen by hand, deletes one or changes an `id`: the key is the SCADA variable number and
belongs to the SCADA. The `semiplot` role draws the same line, with a column-level `UPDATE` of the eight
settings columns and no `INSERT` or `DELETE` on `semiplot_tags` (`postgres-instance.md`).
`PenCatalogueEditorSurfaceTests` pins the seven method names.

The read interface stays read-only because the chart consumes it. A write member on `IDataProvider`
would put a write in reach of every chart consumer; with the write path apart, `Chart/` and `Legend/`
never name `IPenCatalogueEditor`, `IDataProvider` carries no `Insert`, `Update`, `Write` or `Save`, and
two greps in `AGENTS.md` hold both. An axis the operator rescales stays a session edit (`charting.md`).

`PostgresPenCatalogueEditor` in `SemiPlot.DataSource.Postgres` is the only implementation.
`AddPostgresData()` registers it by factory beside `IDataProvider`, over the same `NpgsqlDataSource`
singleton, so one role and one connection string serve reads and writes.

| Type | Shape | Notes |
| --- | --- | --- |
| `PenCatalogue` | `Pens`, `Groups` | The catalogue as stored. |
| `StoredPen` | `Id`, `Name`, `Unit`, `Format`, `Color`, `LineStyle`, `EnabledOnStart`, `ScaleMinOnStart`, `ScaleMaxOnStart` | One `semiplot_tags` row; a null field is a stored `NULL`. |
| `StoredGroup` | `Id`, `Name`, `MemberPenIds` | Member ids ascending; the member count is `MemberPenIds.Count`. |
| `PenSettingChange` | `Name`, `Unit`, `Format`, `Color`, `LineStyle`, `EnabledOnStart`, `ScaleOnStart(Min, Max)` | A closed family, one arm per column; the scale pair is one arm. |

A parameter record carries the id the statement binds and the name a fault names. The implementation
reads only `Id` and `Name` from it, and no statement compares old values in its `WHERE`.

### The stored read

`ReadAsync` runs `StoredPens` and `StoredGroups` on one connection, with `PenCatalogRelations` as the
failure detail. It returns the values as stored, not as `PostgresDataProvider.ReadPen` normalises them:
a `NULL` colour stays null instead of `#808080`, and a mask `PenValueFormat.IsAcceptable` refuses comes
back as written instead of null. The form opens such a value as an invalid field, the way the settings
window opens a file value its rule refuses (`overview.md#the-settings-window`). `line_style` goes
through `StoredLineStyle.Read`, which the provider's read calls too; a value it does not recognise reads
as interpolated and is logged at the level the caller's function gives, asked only for such a value.
The editor gives `Warning` on every read, and the provider gives `Warning` once per pen and stored value
and `Debug` after that (`#the-catalogue-while-the-viewer-runs`, below).

The editor reads fresh when its window opens and after every refresh, never from the startup
catalogue: a form seeded from the startup read would revert a field to a value another instance has
already replaced.

### One setting, one column, one statement

| Operation | Constant | What the statement keeps |
| --- | --- | --- |
| `ChangeAsync`, every arm but `ScaleOnStart` | `UpdatePenName`, `UpdatePenUnit`, `UpdatePenFormat`, `UpdatePenColor`, `UpdatePenLineStyle`, `UpdatePenEnabledOnStart` | `UPDATE semiplot_tags SET <column> = @value WHERE id = @id`, the column fixed in the constant and never interpolated. A null unit or mask binds `NULL`; the form turns an empty field into null. |
| `ChangeAsync`, `ScaleOnStart` | `UpdatePenScaleOnStart` | Both bounds in one statement: `semiplot_tags_scale_paired` refuses a half-set pair between two. |
| `CreateGroupAsync` | `CreateGroup` | `INSERT ... RETURNING id`; the result is the new group's id. |
| `RenameGroupAsync` | `RenameGroup` | One row by id. |
| `DeleteGroupAsync` | `DeleteGroup` | `ON DELETE CASCADE` on `semiplot_pen_groups.group_id` removes the memberships. The pens stay, and a pen left in no group falls under the Ungrouped header at the next catalogue read. |
| `SetMembershipAsync` | `AddMembership`, `RemoveMembership` | Idempotent: adding a membership that exists is `ON CONFLICT (pen_id, group_id) DO NOTHING`, and removing one that does not touches no row and succeeds. |
| `RegisterNewPensAsync` | `RegisterNewPens` | `SELECT semiplot_register_new_pens();`, whose answer is the number of pens added. |

Every update, rename and delete must touch exactly one row. None touched means the pen or group is
gone, and the editor returns `ArchiveFault.RowGone`, never success. A change carries one column, so two
instances editing different settings of one pen never overwrite each other; two writes of the same
column keep the last one. Every parameter is bound with its type and every `NOT NULL` column the
editor writes takes a non-null value, so no statement it issues can raise `23502` or `22P02`.
`ArchiveStatementTextTests` does not pin these constants; `PenCatalogueEditorTests` runs every one of
them as `semiplot` against a container clone, which also proves the grant.

### Registration from the refresh button only

`semiplot_register_new_pens()` is SemiBase's (`sql/semiplot_register.sql`: `SECURITY DEFINER`,
`EXECUTE` for `semiplot` and none for `PUBLIC`). It adds a pen for every key in `trends` that has none:
named by its number, one of twelve colours by `id % 12`, `enabled_on_start = false`, no unit, no mask
and no scale.

The viewer calls it from one place, `PenEditorViewModel.RefreshCommand`, and never at start:

- A registered pen is hidden and named by its number, so the chart has nothing new to draw after it.
  The operator opens the editor to name and switch the pen on either way.
- The function finds the keys by a loose index scan whose cost grows with keys times partitions.
  SemiBase measures 119.0 ms for 50 keys over 91 partitions and extrapolates, without measuring,
  2.6 s to 4.8 s for 500 keys over 365 partitions (SemiBase `docs/architecture/provisioning.md:294-314`).
  At start, that cost would land on every start.
- A database whose provisioning lacks the function answers `42883`. At start that would fail every
  start; behind the button it fails the one refresh the operator pressed.

Two refreshes from two instances do not conflict. A call started while another is uncommitted waits on
the first call's keys, then skips them under `ON CONFLICT (id) DO NOTHING` and returns 0 with no fault.
`PenRegistrationTests` holds the first call's transaction open from a second connection and checks
exactly that, so the editor takes no advisory lock.

### The catalogue while the viewer runs

The editor writes the tables, and every running viewer reads them again: `Bridge/PenCatalogueSync`
issues `QueryPensAsync` every 5 s and at once after each write the editor lands, and the chart and the
sidebar apply what changed (`overview.md#the-live-catalogue`). Several instances may run on one
machine, and the last write wins per column. The read side keeps three rules for that loop:

- `PenCatalog` orders by `tag.name, tag.id`, so two pens that share a name keep one order across reads.
- `QueryPensAsync` builds a new group list per row, and `Pen` compares `Groups` element by element, so
  an unchanged catalogue reads as unchanged.
- `PostgresDataProvider` warns about a stored value it normalises, a `NULL` colour, a mask the rule
  refuses or an unknown `line_style`, once per process for each pen, column and stored value, and logs
  a repeat at `Debug`. The provider keeps the set of warned triples. A hand-edited row would otherwise
  write a warning every 5 s.

A read that adds or removes a pen moves the live edge onto the new pen set (Realtime, below).

## Operation to statement

Every statement the provider and the editor issue is a constant in
`SemiPlot.DataSource.Postgres/ArchiveStatements.cs`; parameters are always bound. The bench seeder
and the test harness own SQL of their own (`bench.md`). `ArchiveStatementTextTests` pins each
provider constant below clause by clause; `ExplainPlanTests` asserts each plan's shape against a
container. The editor's statements are in the table above.

| Operation | Constant | What the statement must keep, and why |
| --- | --- | --- |
| Pen catalogue | `PenCatalog` | `semiplot_tags` left-joined through `semiplot_pen_groups` to `semiplot_groups`, the names aggregated with `array_agg` under `GROUP BY tag.id`: a pen in two groups stays one row and a pen in none survives the outer join. `ORDER BY tag.name, tag.id`, because a pen has no single group to sort by, and the id keeps two pens that share a name in one order across reads. An empty table is an empty list; a missing one is `ArchiveFault.TableMissing` naming all three relations. |
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
the margin is half spent. A range that reaches the first sample has no left edge to approach, because
no row lies before it: `HistoryPrefetch.Covers` takes `Navigation.FirstSample` and holds the left side
of such a range whatever margin the clamp left. A window that starts before the first sample is
therefore read once and drawn from that read; an archive younger than the window opens on one, and a
zoom out past the archive followed by a jump to now leaves one. A first sample that moves back, after
a catalogue read adds a pen with older rows, reopens the question:
`TrendChartViewModel.WidenToArchiveExtent` asks `Covers` again with the new first sample, and a range
clamped at the old one no longer holds its left side, so the window in view is read once more from the
new first sample. A zoom, a layer change and a column-target change always re-fetch, because each
changes the resolution the range was read at.

Two column counts travel on one request, and the gate reads only one of them. `HistoryRequest.Range`
carries the quantized count `ChartNavigationController.TargetColumnCount` holds, which a deadband
keeps still while the Y tick labels widen and narrow the data rect by a pixel mid-gesture;
`HistoryRequest.TargetColumnCount` carries the unquantized pixel width the provider decimates to.
The range travels on the request so the result alone says which band the envelopes in hand describe:
`TrendChartViewModel` opens the gate on the range that came back, never on the one last asked for, and
only when that read carried every pen the chart shows. A read issued before the catalogue added a pen
holds no row of it, so its result leaves the gate shut and the window in view is asked for again with
the pens shown.

A gesture that never goes quiet still fetches. `ChartHistoryRequestDebouncer` merges the trailing
`Throttle` of 150 ms with a `Sample` of 400 ms over the same requests, so a continuous drag issues one
query per 400 ms and one more after it stops.

The chart alone knows what is drawn. It asks for a window only while its fetched range does not cover
the window in view, and otherwise pushes `RequestNothing`. The debouncer keeps no drawn window of its
own. It stamps each request with the number of successful reads delivered so far, and drops a request
the last successful read answers: the same pens, window and column target, pushed before that read was
delivered. That is how the two heads emitting one push, and a drag that stops on the window being
read, issue one query. A request pushed after that read was delivered is the chart asking again, and it
reaches the archive.

A read that is still in flight, that came back failed, or whose apply threw covers nothing, so the
chart asks for the same window again. `TrendChartViewModel.ApplyHistory` clears its fetched range
before it loads the envelopes and sets it only after, so an apply that throws part way leaves the gate
shut and the window in view is asked for again at the next move. A catalogue read that adds or removes
a pen drops envelopes outside that apply, so it clears the fetched range and asks for the window in
view with every pen shown. A pen removed and named again takes back its old place in the pen order, so
that request can name the same pens and window as the read applied before the removal; it is pushed
after that read was delivered, so it is read.

One query runs at a time, and the newest request that arrived while it ran runs when it lands. No
request waits when the newest push needs no read, and the window waiting behind a query is dropped
when that query's read answers it. The two heads treat the query in flight differently:

- A paced request never cancels it. A read slower than the cap interval therefore still completes
  during the gesture and lands, so the strip fills while the drag moves.
- A trailing request is the gesture's end. When it finds a query running for another window, it
  cancels that query, so the final window starts as soon as the cancelled one completes instead of
  waiting up to the 300 s command timeout behind a read for a window the gesture has left. The
  cancelled query completes once the server acknowledges the cancel, or when Npgsql breaks the socket
  after `CancellationTimeout`.

A gesture can end back inside the band the envelopes in hand cover, after a read for another band
started. A pan inside the fetched band then pushes `RequestNothing`: it reads nothing, drops the
request waiting behind the running query, and as the gesture's end it cancels the read for the band
the drag left. The gate is open only on a read of the pens shown, so that push never cancels a read
issued for a pen the catalogue added since. A read that failed changes nothing on screen and leaves the
gate as it was, so a gesture whose read failed still ends on the window drawn before it; an apply that
throws after the envelopes load leaves the gate open on them, with the same effect. A read that lands
while the drag is still moving is applied, and the window in view is re-requested from the range that
arrived.

"Trailing" means 150 ms without a request, not the release of a mouse button. Input that arrives in
steps more than 150 ms apart, such as wheel notches, key presses or a window resize, ends a gesture
at every step, so each step cancels the read the previous one started, and no step's read lands until
the input pauses for one whole read. A read on a local archive completes well inside that gap; the
trade-off is that a slow read shows the final window sooner and the intermediate ones not at all.

`Dispose` cancels the running query as well. A cancelled query reports nothing and starts the request
waiting behind it; the last window a gesture asked for is the last one applied. Each query runs under its own `CancellationTokenSource`, which the debouncer
creates under its gate and cancels with `CancelAsync`: the flag flips at once, and the callbacks run
on the thread pool. Npgsql's callback opens a second connection to send the cancel request, bounded
by the 15 s connect timeout, and blocks until the server closes it; run under the gate and Rx's own
locks, it would freeze the UI thread's next request and the window's close for that long. The source
is never disposed: it holds no timer and no wait handle, and disposing it before the queued callbacks
run drops them, so the provider would never hear of the cancel. Only a cancellation the debouncer
asked for is silent: an `OperationCanceledException` while its token is still live is a failed read
like any other.

The token is the trailing `cancellationToken` of `IDataProvider.QueryHistoryAsync`, threaded through
`TrendCoordinator.QueryHistoryAsync`. `PostgresDataProvider` passes it to the connection open, to
every `ExecuteReaderAsync` and to every row read of the window, the bucketed Raw window and the fresh
tail, and the mapper rethrows the `OperationCanceledException` it ends in, so a cancelled read never
becomes a failed `Result`. `PostgresHistoryReadTests.AReadCancelledMidStatementThrowsInsteadOfFailing`
cancels a Raw and a Minute read held on a lock of `trends`. A cancelled command costs one extra
connection for the cancel request, and its own connection for at most two seconds more: Npgsql first
sends PostgreSQL the cancel request, and if no answer arrives within `CancellationTimeout` (2000 ms by
default, which the connection string keeps) it breaks the socket. A broken connection leaves the pool,
and the pool opens a new one. The backend behind a broken socket can keep running the abandoned
statement until it notices the dead connection, so for that stretch the server runs two history
queries for one chart.

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

The pen set can change while the viewer runs. `TrendCoordinator` holds it in a
`BehaviorSubject<IReadOnlyList<int>>` seeded from its constructor's pens and subscribes through
`Select(_dataProvider.Subscribe)` and `Switch()`, ahead of the `Buffer`. `SetPens` pushes the list it
is handed, which the chart builds fresh for each call, and the chart calls it only when the id set it
shows differs from `PenIds`, the subject's current set, so the coordinator carries no change check of its
own.
`RealtimeBatches` stays one published stream: the chart's one subscription, which lives as long as
the window, sees no change. A switch disposes the old subscription and starts a new poll, whose first
tick reads the baseline and emits nothing. The rows written between the old poll's last tick and the
new baseline, at most one poll interval, are not delivered live; the history query the chart issues on
every set change covers them. Every set the chart pushes holds only pens of the provider's own
catalogue. `Dispose` completes the subject.

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
bar has a single writer, the stream `TrendWindow.Build` hands its constructor. Because every
subscription's first tick reports `Connected`, the recovery entry is written only once a fault has
been seen — otherwise every launch would announce a connection it never lost. The handler is
wrapped: `TrendCoordinator` forwards this stream with a bare `Subscribe`, so a throw out of the
handler would end the forwarding for the rest of the session instead of reaching an `onError`.

## Error semantics

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | The startup-failure window opens, titled "No connection to the archive", naming the host and port, with a remedy |
| Connection lost mid-session | failed `Result` on the query; realtime tick dropped | Chart keeps the data it has |
| Three consecutive realtime ticks fail | `ArchiveFault.ConnectionLost` on `ConnectionFaults`; the observable keeps running | The status indicator turns to the fault state and one `Warning` entry appears; the first tick that succeeds restores the indicator and adds one `Info` entry |
| A column the read needs is absent (`42703`) | `ArchiveFault.ShapeUnexpected` with the server's detail | "The archive has an unexpected shape" — run `semibase site`, then find what altered the table |
| Query timeout (`57014`) | `ArchiveFault.QueryTimedOut` | "The archive ended the statement"; `statement_timeout` is the `semiplot` role's own setting |
| The database does not exist (`3D000`) | `ArchiveFault.DatabaseMissing` | "The archive is not provisioned" — run `semibase site` |
| Credentials refused or a grant missing (`28P01`, `28000`, `42501`) | `ArchiveFault.AccessDenied` | "The archive refused access" — the user, password or grants |
| A relation or function a statement needs does not exist (`42P01`, `42883`) | `ArchiveFault.TableMissing` whose detail names every relation that statement touches, or `semiplot_register_new_pens()` | "The archive is not provisioned" — run `semibase site`, which creates them all |
| The catalogue re-read fails mid-session | failed `Result`; `PenCatalogueSync` keeps its snapshot | Nothing for the first two failures in a row, one log warning each; from the third one panel entry, coalesced with the ones after it. The next success applies everything stored since the last success |
| `semiplot_tags` present but empty | empty pen list, success | The chart area's own empty state, naming `Edit` -> `Pens and groups` -> `Refresh pen list`: no key has been registered as a pen yet. Not a message-panel entry: an empty catalogue is a state, not something that happened |
| Archive present but no rows in the window | success, empty envelope list | Empty chart, no error |
| A check constraint refuses a written value (`23514`) | `ArchiveFault.ValueRejected` | "The archive refused the value"; the field reverts, and the message line says why |
| A group name another group already carries (`23505`) | `ArchiveFault.NameTaken` | "The name is already taken" |
| A write names a pen or group that is gone (`23503`, or no row touched) | `ArchiveFault.RowGone` | "The entry no longer exists"; a refresh shows what the catalogue holds now |

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
| `TableMissing` | `42P01`, `42883` | every relation the failing statement touches: `trends` for the history and realtime reads, `semiplot_tags, trends` for the archive extent, all three catalogue tables for the pen catalogue and every editor write; `semiplot_register_new_pens()` for the registration |
| `ShapeUnexpected` | `42703` | the server's own message |
| `QueryTimedOut` | `57014` | empty |
| `ConnectionLost` | three consecutive failed poll ticks | the number of failures that raised it |
| `ValueRejected` | `23514` on an editor write | the pen's name |
| `NameTaken` | `23505` on an editor write | the group name asked for |
| `RowGone` | `23503` on an editor write, or an update, rename or delete that touched no row | the pen or group name |
| `ReadFailed` | any other SQLSTATE, or a client-side throw, on a read or a write | the SQLSTATE, or empty |

The write kinds come from `ArchiveExceptionMapper.MapWrite(exception, subject)`. It maps `23514`,
`23505` and `23503` to `ValueRejected`, `NameTaken` and `RowGone` carrying `subject`, and sends every
other exception to the read classification with `PenCatalogRelations` as its relation: every write
touches one of those three relations, and one provisioning run creates all three. `RowGone(subject)`
is the form for an update that touched no row. The subject is the pen's committed name for a pen
change, including a `Name` change; for a rename it is the new name when the server refuses it and the
stored name when the group is gone; a delete names the group; a membership names the pen when the
server reports the pen's key `semiplot_pen_groups_pen_id_fkey` violated, and the group otherwise. No word of either language lives in `Detail`: the mapper wraps the
subject in each kind's resourced detail. A `ReadFailed` with no detail is a fault in this code:
`ArchiveFailureLog.LogIfUnexpected`, which the provider and the editor both call, logs it with the
exception.

`AccessDenied`, `Unreachable`, `QueryTimedOut`, `TableMissing` and `ReadFailed` serve reads and writes
alike, so their operator text and their `ArchiveError.Describe` lines name a statement, never a read
(`ui-text.md#the-pen-editors-failure-text`). `ReadFailed` stays the default arm of both mappings and
keeps its name, which the operator never reads. There is no `WriteFailed` kind: its title, detail and
remedy would repeat the reworded `ReadFailed` ones. A missing registration function is a missing
relation to the operator, so `42883` takes the `TableMissing` remedy, "run semibase site".

`ArchiveFailureMapper` (`SemiPlot.UI/Messages/`) turns each kind into a title, a detail, a remedy
and a `MessageSeverity`, and is the one place a remedy is written. The severity is decided in the
mapper rather than at the call site, so a future error type gets one from the `MapUnknown` arm
without anyone remembering: `AccessDenied`, `TableMissing`, `DatabaseMissing` and `ShapeUnexpected`
are `Error`, because nothing recovers until someone changes a grant, a schema or a provisioning run;
`Unreachable`, `ConnectionLost`, `QueryTimedOut` and `ReadFailed` are `Warning`, because the poll
loop retries by itself. `ValueRejected`, `NameTaken` and `RowGone` are `Warning` too: the operator's
own edit was refused, and the next edit is the remedy.
`FailureSeverityTests.ArchiveFaults_SplitIntoWhatRetriesAndWhatNeedsTheOperator` holds them as a third
bucket. `ConnectionLost` never opens the startup-failure window; it is an entry in the message panel
under a chart that works.

The eleven rows above are the `ArchiveError` arm alone. `Map` has eight further arms — the startup
arguments, the log file, the configuration section, the app settings, the connection file, the
startup read timeout, an `IExceptionalError` and the `_` fallback — and every one of them is
`Error`, because each is reachable only at startup, where nothing recovers until the operator edits
something. The app settings and the configuration section arms are also reachable later, from the live
theme's reload and the settings save, and the same holds there (`overview.md#the-live-theme`). `MessageSeverity.Info` has exactly one writer in the whole tree, the connection-restored
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
only: the result it reports instead is reported by a handler that guards itself. The catalogue read
loop reports from the third failure in a row, the live edge's threshold
(`ArchiveConnectionState.ConsecutiveFailuresBeforeFault`); the first two log a warning
(`overview.md#a-failed-read`). The minimap's band and extent reads each report the first failure of an
outage and log every repeat at Warning (`trend-interaction.md#archive-overview-minimap`).

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

A handler that runs detached — an Rx `onNext`, an Rx `onError`, a job posted to the UI scheduler — wraps
its whole body, not its report alone, and hands the throw to `TryReportFailure`:
`TrendChartViewModel.OnHistoryQueryFailed`, `MainWindow/AppStatusBarViewModel.ApplyConnectionState`,
`Bridge/PenCatalogueSync.RunAsync`, `MainWindow/PenCatalogueApplier.ApplyAsync`, the last through
`TrendChartViewModel.ReportFailure`, the minimap band's `Minimap/MinimapBandFeed.ApplyRead`, whose guard
covers the next read's scheduling too, and `TryRequestRead`, which the schedule and the pen-change
subscription call, `Minimap/MinimapViewModel.TryApplyExtent` for the extent read once more after a
blank one, and `App.ApplyTheme`, which holds a failure for the first window
until it exists and has only the log after the failure window closes (`overview.md#the-live-theme`). The
guard belongs to the handler rather than to whoever invokes it, because the recovery around the report
would otherwise escape the same way the report can.
`Minimap/MinimapViewModel.LoadExtentAsync` carries no guard of its own: its apply runs through
`Observable.Start` on the UI scheduler and awaits it, so a throw reaches the awaiter, and both awaiters
report it, `TrendWindow.StartExtentLoad`'s continuation and `PenCatalogueApplier.ApplyAsync`'s catch.
`TrendChartViewModel.ReportFailure`, `MainWindowViewModel.ReportFailure`,
`StartupFailureViewModel.ReportFailure`, the `LoadExtentAsync` continuation and the ReactiveUI observer
call the guarded form directly.

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

The pen editor reports the same way. A failed write reverts its field, puts the title
`ArchiveFailureMapper.Map` gives the error on the form's message line, and adds the full entry to the
panel through `ResultReporting.ReportFailure`; a failed refresh reports and leaves the tables as they
were. The window's code-behind handlers are `async void` with the whole body in `try/catch`, and the
`catch` reaches the panel through `PenEditorViewModel.ReportFailure`, which takes the guarded form.

`57014` maps unconditionally to `QueryTimedOut`. The one statement a caller cancels is a history read
whose token the chart cancelled, and Npgsql raises `OperationCanceledException` for a cancellation it
requested, which the mapper rethrows; a `57014` that reaches the mapper is therefore the server's own
timeout or an administrator's cancel. No other member of `IDataProvider` or `IPenCatalogueEditor`
takes a `CancellationToken`.

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
`BuildAvaloniaApp()`, while the reads `TrendWindow.Build` starts inside `AfterSetup` are
asynchronous.

`StartupOptions.Parse(args)` runs ahead of all of it, because the logger's own path is an argument. It
returns `Result<StartupOptions>`, and on failure `Program.Main` applies the bootstrap culture, creates no
logger, opens the failure window through `App.RunFailed(null, failure, options: null)` and returns 1. On
success `LogFileTarget.Prepare` opens the file that `--log-file` names, creating its folder, and takes
the same route on failure, passing the parsed options so the window still offers Restart and Settings:
Serilog's file sink reports its own open failure only to `Serilog.Debugging.SelfLog` and then writes
nowhere, so a mistyped path would otherwise start the viewer with no log and no report. Only then is the
logger created, and `Program.Main` writes one Information line naming the configuration directory and the
logging level; `StartupProbe.Run` writes one more naming the time zone once the connection section loads.
A healthy run reaches Information nowhere else, so above that level the file `Prepare` opened stays empty
until the first failure.

`StartupSequence.Run` then takes these steps in order:

1. Set the bootstrap UI culture to Russian, so a failure naming the settings section itself can be
   read.
2. Load the `<ConfigDir>/app` section and apply its `locale`. A failure here short-circuits with null
   settings, before the connection section is touched, so a broken archive cannot mask a broken
   configuration (`ui-text.md`, `ui-theme.md`).
3. `StartupProbe.Run`: load the `<ConfigDir>/connection` section and register
   `AddPostgresData(settings)`.
4. Resolve `IDataProvider`, read the pen catalogue, then the archive extent.

The container, the pens and the extent cross the boundary in a `StartupData` record, so `TrendWindow.Build` awaits nothing. `Program.Main` passes the settings, the `StartupData` or the first `IError`, and the parsed `StartupOptions` to `App.RunStarted(AppSettings?, StartupData, StartupOptions)` on
success and to `App.RunFailed(AppSettings?, IError, StartupOptions?)` on failure, the configuration
directory reaching the settings window and the keys reaching `InstanceLauncher` on both paths (the failure window offers Settings only when both sections read and every
key the dialog edits is present; `overview.md#another-instance`): on success `App` builds one
`TrendWindow` (`overview.md#one-window-per-process`); on failure `App` maps the error through
`ArchiveFailureMapper` and opens `Startup/StartupFailureWindow`, which names what broke and what to do
and holds a message panel of its own. That window has no chart, legend or minimap and no service
provider. There is
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
states that the catalogue is empty and names the way in, `Edit` -> `Pens and groups` ->
`Refresh pen list` (`Chart/TrendChartViewModel.HasNoPens`, `ui-text.md#the-pen-editors-text`). It is a state of the
chart, not an entry in the message panel. Logging is configured before the probe runs; the log path
and the argument list are in `overview.md`.

No startup code calls `IPenCatalogueEditor`. The container constructs it when `TrendWindow.Build`
resolves it inside `.AfterSetup(...)`, and its constructor issues no statement. `RegisterNewPensAsync`
has one caller, `PenEditorViewModel`, so every write the viewer issues follows an operator action.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, provisioning did not complete: `semibase site` creates it and
   `semiplot_tags` in one run.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn.
   `Refresh pen list` in `Edit` -> `Pens and groups` registers it as a hidden pen, which every running viewer lists within
   5 s, switched off, and draws once it is switched on.
5. Does the window overlap the data? Compare against the extent. An offset of a whole number of
   hours means the SCADA stamps rows in a zone other than the machine's, and
   `[DEC:machine-time-zone]` does not hold on this installation (`sources.md`). At `information` or more
   verbose, the startup log line `Reading the archive in the time zone ...` names the zone the viewer
   applied.
6. Is `tpdefault` non-empty? Rows there mean the SCADA failed to create a daily partition. They are
   not a cause of an empty chart — every read still returns them — but partition elimination is
   lost for reads that cannot skip that partition.
