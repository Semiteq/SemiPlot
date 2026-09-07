# Chart drag responsiveness

## Overview

Dragging the chart near the Raw/Minute layer boundary stutters, and the strip a pan exposes stays
empty until the gesture pauses. Two mechanisms, both measured on the demo stand on 2026-09-07
(`dotnet-trace`, 45 s of dragging, 8 bench pens, Release build):

- **The band fill is the frame.** ScottPlot's `FillY` renders one anti-aliased concave polygon of
  `2 × columns + 1` vertices per pen per frame; with 2048 columns that is ~4100 edges through
  `SKCanvas.DrawPath`, 20 ms each. `RenderOnce` averaged 62 ms (max 386 ms), ~10 frames per second.
  With `FillStyle.AntiAlias = false` the same scene averaged 4.8 ms per frame (max 34 ms) at the
  30 Hz redraw cap, and `DrawPath` vanished from the trace.
- **A pan fetches late and fetches too much.** The history path debounces with `Throttle(150 ms)`,
  which emits only after the gesture goes quiet; every request covers exactly the visible window,
  so any pan exposes a strip no envelope holds; and at Raw the server returns every row of the
  window for the client to decimate. Near the boundary the Raw window is `15 s × 2048 = 8.5 h`,
  which on the bench is 969 768 rows for 8 pens (`count(*)` alone 442 ms server-side, then the
  transfer and the fold), against 15 989 rows and 67 ms for the same window on Minute.

The pass turns off the fill's anti-aliasing (a one-column-per-pixel band has no edge to smooth),
moves Raw decimation to the server with `date_bin` (the `GROUP BY` reduction the chosen read-path
design already names and never shipped), bounds the auto scale to the visible window, fetches one
window width of margin on each side so a pan inside the margin never queries, and lets a continuous
gesture fetch at a bounded interval instead of waiting for silence.

## Context (from discovery)

Every line number is on `master` at 43dad25 unless stated.

**Band rendering.** `TrendChartViewModel.BuildPenState` (`SemiPlot.UI/Chart/TrendChartViewModel.cs:359-380`)
creates the band with `Plot.Add.FillY([], [], [])`, `FillColor`, `LineWidth = 0f` and
`MarkerStyle.None` (`:367-373`). ScottPlot 5.1.59 (`SemiPlot/Directory.Packages.props:26`):
`FillY` wraps a `Polygon` and `Render` delegates to it (`FillY.cs:13,104-106` at tag 5.1.59);
`Polygon.Render` builds an `SKPath` of every coordinate plus the closing point and calls
`Drawing.FillPath` when `FillStyle.HasValue` (`Polygon.cs:105-140`); `FillStyle.AntiAlias`
defaults to `true` and `ApplyToPaint` sets `paint.IsAntialias` from it (`FillStyle.cs:13,53`).
`HistoryColumnTarget` maps the data-area width to one column per pixel, clamped to 256…2048
(`SemiPlot.UI/Chart/HistoryColumnTarget.cs:7-8,20-22`). `TrendChartViewModelTests` asserts
`state.Band.IsVisible` and `pen.Band.Axes.XAxis` (`SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs:78,571`),
so a band-property test has a home.

**History request path.** `ChartNavigationController.ApplyWindowChange` raises `WindowChanged`
with `RequiresHistoryRequery` defaulting to `true` (`SemiPlot.UI/Chart/ChartNavigationController.cs:145-149`,
`SemiPlot.UI/Chart/NavigationWindow.cs:7-11`); `PanBy` and `ZoomAt` go through it (`:93-103`), the
sticky live-edge advance passes `false` (`:140-142`). `TrendChartViewModel.OnNavigationWindowChanged`
stores `_windowStart`/`_windowEnd` and calls `RequestHistory(window.From, window.To, window.Layer)`
when the flag is set (`TrendChartViewModel.cs:382-394`); `RequestHistory` builds a `HistoryRequest`
over exactly that window with `_reportedColumnTarget` (`:396-400`, `SemiPlot.UI/Chart/HistoryRequest.cs:5-10`).
`ChartHistoryRequestDebouncer` pipes `_requests.Throttle(debounceWindow, dataScheduler)` into
`FromAsync` under `Switch` (`SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs:28-39`); the window
is 150 ms (`TrendChartViewModel.cs:24`). `ChartHistoryRequestDebouncerTests` holds 4 facts on
`TestScheduler` with `ImmediateScheduler.Instance` as the UI scheduler
(`SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs:27-58,110-153`).

**Scale over the fetched envelope.** `PenScaleModel.Compute` takes `windowStart`/`windowEnd`
(`SemiPlot.Core/Trends/PenScaleModel.cs:10-15`); `AppendEnvelopeValues` filters by the window only
under `ScaleMode.AutoscaleToWindow` (`:132-137`), and `ScaleMode.Auto` reads every envelope column.
`AutoscaleToWindow` is declared at `SemiPlot.Core/Trends/ScaleMode.cs:7` and written by no
production code: the chart view model writes `Auto` and `Manual` (`TrendChartViewModel.cs:347,356`),
the only writer of the member is `SemiPlot.Tests.Unit/Core/Trends/PenScaleModelTests.cs:97`.
`ApplyAxisModel` passes `_windowStart`/`_windowEnd` (`TrendChartViewModel.cs:475-480`). Today the
fetched envelope equals the visible window, so `Auto` and `AutoscaleToWindow` compute the same
range; once the envelope carries a margin they diverge.

**The Raw read.** `PostgresDataProvider.QueryHistoryAsync` (`SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:110-151`)
reads `ArchiveStatements.SparseHistoryWindow` through `ReadWindowAsync` (`:269-290`), patches the
fresh tail for coarse layers only (`FillFreshTailAsync` `:292-316`, `Raw` returns the rows as they
are at `:300-303`), and folds with `HistoryRowFold.Fold(rows, _timeConverter, targetColumnCount)`
(`:145`). `SparseHistoryWindow` (`SemiPlot.DataSource.Postgres/ArchiveStatements.cs:69-88`) unions
one seed row per pen with the window rows under `ORDER BY id, t`. `HistoryRowFold.Fold`
(`SemiPlot.DataSource.Postgres/HistoryRowFold.cs:22-63`) groups consecutive identifiers, drops a
row whose UTC conversion does not advance, appends a null anchor one tick after a `q = 32` row
(`:44-53`), and hands each pen to `MinMaxDecimator.Decimate` (`:59`). The decimator buckets by
sample count inside non-null segments, keeps min and max per bucket, takes the middle sample as
the column's timestamp and center, and never lets a column straddle a gap
(`SemiPlot.Core/Trends/MinMaxDecimator.cs:3-4,113-167`). `HistoryRowFoldTests` holds 19 facts
(`SemiPlot.Tests.Unit/Postgres/HistoryRowFoldTests.cs:46-436`). `BindWindow`/`BindLocalWindow` bind
`ids`, `layer`, `from`, `to` (`PostgresDataProvider.cs:236-267`), and
`ArchiveStatementTextTests.TheWindowBinderNamesExactlyTheStatementsOwnParameters` pins the
parameter set (`SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs:85-100`).
`ExplainPlanTests.TheSeededWindowPlanWalksBackToTheSeedWithinItsBound`
(`SemiPlot.Tests.Integration/ExplainPlanTests.cs:115-151`) shows the plan-shape assertions
(`_sequentialScanOverRows`, `_indexReachedRows`, `_dayPartitionRead`, `:41-60`).
`PostgresHistoryReadTests` reads the seeded clone at `TargetColumnCount = 4096` so envelopes
compare against raw rows (`SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs:29-32,115-175`).

**Archive facts the statement rests on.** `trends(id, l, t timestamp(3), v double precision, q integer)`
with `PRIMARY KEY (id, l, t)`, partitioned by range on `t` (`docs/architecture/scada-archive.md:31-39`);
`q = 32` is the last sample before a break, `q = 16` the first after, both carrying a real value,
copied into every layer (`:200-213`); `v` was never null in the measured archive (`:215`).
Production and the bench run PostgreSQL 17 with 14 as the declared floor, and `date_bin` arrives in
14 (`docs/architecture/postgres-instance.md:22-28`). The chosen read-path option D reads the vendor's
layers "with PostgreSQL reducing to pixel columns by `GROUP BY` when the chosen layer is still
denser than the canvas" (`docs/architecture/history-read-path-evaluation.md:39-41`), and
`docs/architecture/data-integration.md:97-98` records the bucketed read as designed and not shipped.

**Bench measurements, 2026-09-07.** Stand up through `SemiPlot.AppHost` in Release, 8 pens at
0.5 s changes: `trends` held 2 728 920 raw rows over 24 h. Raw over the 8.5 h ending at the newest
raw row for 8 ids: 969 768 rows, `count(*)` 442 ms; Minute over the same window: 15 989 rows, 67 ms.
Trace 1 (anti-aliased fill): `RenderOnce` 472 calls, mean 61.7 ms, max 386 ms; `Polygon.Render`
27.1 s of the 45 s; `SKCanvas.DrawPath` 1265 calls, mean 20.3 ms; `OnNavigationWindowChanged`
540 calls, 1.0 s in total; `ApplyHistory` 8 calls. Trace 2 (`AntiAlias = false`): `RenderOnce`
1434 calls, mean 4.8 ms, max 34 ms; `Polygon.Render` mean 2.3 ms; `DrawPath` 28 calls, 52 ms in
total; `ApplyHistory` 24 calls. Counters: CPU 9.2 % then 7.3 %, allocation 51 then 78 MB/s (three
times the frames), time in GC 1.1 % then 3.7 %.

## Development Approach

- **testing approach**: Regular. Code first, then the tests in the same task.
- one mechanism per task, completed and green before the next; the order is the dependency order
  (the margin needs the window-bounded scale and the cheap Raw read ahead of it)
- every task adds or updates unit tests for the code it touches, success path and failure path;
  Task 2 also adds the statement-text, plan-shape and seeded-archive facts the read path keeps for
  every statement
- `dotnet build SemiPlot.slnx` runs with `TreatWarningsAsErrors`, and the pre-commit hook runs
  `dotnet format --verify-no-changes`; every commit is clean under both
- `lint-comments` over the touched `.cs` files exits 0 before a commit
- this plan is updated when the scope moves

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit`. Rx tests on `TestScheduler` with `ImmediateScheduler.Instance`
  as the UI scheduler (`ChartHistoryRequestDebouncerTests`); fold tests in the shape of
  `HistoryRowFoldTests`; the prefetch decision as a pure model with table-driven facts.
- **integration tests**: `SemiPlot.Tests.Integration` against the bench container: the bucketed
  statement's plan shape (`ExplainPlanTests`) and its envelope against the seeded rows
  (`PostgresHistoryReadTests`).
- no e2e suite exists; felt smoothness is the operator's check on the stand, with the trace as the
  number.

## Acceptance Evidence

**Automated, all tasks:**

```powershell
dotnet build SemiPlot.slnx
dotnet format SemiPlot.slnx --verify-no-changes
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj
```

**Frame cost, measured on the stand.** With `dotnet run --project SemiPlot/SemiPlot.AppHost -c Release`
up and the viewer showing all bench pens with a window just under the Raw ceiling:

```powershell
dotnet-trace collect -p (Get-Process SemiPlot.UI).Id --format Speedscope --duration 00:00:45 -o drag.speedscope.json
python scripts/perf/trace-shares.py drag.speedscope.speedscope.json
```

while dragging left and right for the whole 45 s. The script (Task 7 adds it) prints call count,
total, mean and max for `RenderOnce`, `Polygon.Render`, `SKCanvas.DrawPath`,
`OnNavigationWindowChanged`, `ApplyHistory` and `QueryHistoryAsync`. Pass: `RenderOnce` mean
≤ 10 ms and `SKCanvas.DrawPath` total ≤ 1 s over the capture (measured 4.8 ms and 0.05 s with the
anti-aliasing off on 2026-09-07, against 61.7 ms and 25.6 s before).

**Raw read size, automated.** `PostgresHistoryReadTests.ARawWindowDenserThanTheCanvasComesBackBucketed`
reads a seeded window holding more raw rows than columns at a small `targetColumnCount` and asserts
the envelope holds at most `targetColumnCount + breaks + 1` columns per pen; the same fact records
the statement's server time through `EXPLAIN (ANALYZE, TIMING OFF)` in its output, not as an
assertion.

**Pan without a query, automated.** `HistoryPrefetchTests` pins that a pan inside the fetched
band yields no request and a pan past it yields one; `TrendChartViewModelTests.APanInsideThePrefetchedBandIssuesNoHistoryQuery`
drives the view model with `FakeDataProvider` and asserts `HistoryQueryCount` stays at one across
a pan of half a window width and rises to two past a full width.

**Fetch during a gesture, automated.** `ChartHistoryRequestDebouncerTests.AContinuousGestureFetchesAtTheCapInterval`
pushes a request every 20 ms for 2 s on the `TestScheduler` and asserts exact counts, the schedule
there being deterministic: 5 queries during the gesture and a sixth trailing one after it stops.

**Felt behaviour, manual, on the stand:**

1. Window just under the Raw ceiling, drag continuously for 5 s: the exposed strip fills while the
   drag is still moving, never later than the cap interval plus one query.
2. Pan by half a window and stop: no query is issued (the viewer log shows no history read).
3. Pan by two windows and stop: exactly one query lands, for the new band.
4. Zoom in twice, zoom out twice: the auto scale follows the visible data only.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview

- **No anti-aliasing on the band fill.** `BuildPenState` sets `band.FillStyle.AntiAlias = false`.
  The band is one column per pixel, so its edges are one- or two-pixel steps that anti-aliasing
  cannot smooth; what it did was force Skia's software path renderer through a 4000-edge polygon
  on every frame. A test pins the property so a cleanup cannot silently restore the cost.
- **Raw decimates on the server.** A new statement, `BucketedRawWindow`, keeps the seed branch of
  `SparseHistoryWindow` and replaces its window branch with a `GROUP BY` over `date_bin(@bucket, t, @from)`
  and a break segment number, so a bucket never straddles a `q = 32` marker. Each group carries the
  column's timestamp and value (the newest sample in the bucket), `min(v)`, `max(v)` and whether it
  ends in a break. `QueryHistoryAsync` takes that path for `Raw` and folds it with a bucketed fold;
  the coarse layers keep `SparseHistoryWindow`, `HistoryRowFold` and the fresh tail unchanged. The
  fresh tail's own raw read stays row-level: it spans at most four coarse point spacings.
- **Auto scale over the visible window only.** `ScaleMode.Auto` filters columns by
  `[windowStart, windowEnd]`, which the view model already passes; `ScaleMode.AutoscaleToWindow`
  is deleted with `IsInWindow` folded into the auto path. Before the margin exists the two modes
  compute the same range, so no visible behaviour changes in this task.
- **One window of margin on each side.** A pure `HistoryPrefetch` model turns a visible window
  into a fetch range `[From - Width, To + Width]` with `3 × TargetColumnCount` columns, and decides
  whether a new visible window is still served by the last fetch: same layer, same column target,
  and the window inside the inner band `[fetched.From + Width / 2, fetched.To - Width / 2]`. The
  chart view model keeps the last fetch range and consults the model in `OnNavigationWindowChanged`;
  a zoom always re-fetches (the resolution changed), a pan inside the band never does. Envelopes
  now hold three windows of columns; the legend, the cursor and `TrackDataExtents` read them
  unchanged, and the scale is bounded by the task above.
- **A gesture fetches at a bounded interval.** The debouncer merges the trailing `Throttle` with a
  `Sample(capInterval)` of the same requests and drops a request the last successful query already
  applied, so a continuous drag issues one query per cap interval and one after it stops. The cap is
  400 ms. One query runs at a time and the newest request that arrived while it ran runs when it
  lands, so a read slower than the cap still completes during the gesture and no window is read for
  nothing.
- **Docs and the trace script.** `charting.md` states the band's fill setting and why;
  `data-integration.md` moves the bucketed read from "designed, not shipped" to the statement
  table and describes the margin; `scripts/perf/trace-shares.py` is the Speedscope reader the
  Acceptance Evidence runs.

## Technical Details

**`BucketedRawWindow` (Task 2).** Parameters `@ids`, `@from`, `@to`, `@bucket` (an `interval`,
`(@to - @from) / @columns` computed by the binder from the request's column target); `@layer` is
not bound, the statement is Raw by construction.

```sql
SELECT id, t, v, lo, hi, breaks
FROM (
    SELECT seed.id, seed.t, seed.v, seed.v AS lo, seed.v AS hi, (seed.q = 32) AS breaks
    FROM (SELECT DISTINCT unnest(@ids) AS id) requested
    CROSS JOIN LATERAL (
        SELECT prior.id, prior.t, prior.v, prior.q
        FROM trends prior
        WHERE prior.id = requested.id AND prior.l = 0
          AND prior.t < @from AND prior.t >= @from - greatest(@to - @from, interval '1 day')
        ORDER BY prior.t DESC
        LIMIT 1
    ) seed
    UNION ALL
    SELECT id,
           max(t) AS t,
           (array_agg(v ORDER BY t DESC))[1] AS v,
           min(v) AS lo,
           max(v) AS hi,
           bool_or(q = 32) AS breaks
    FROM (
        SELECT id, t, v, q,
               count(*) FILTER (WHERE q = 32)
                   OVER (PARTITION BY id ORDER BY t ROWS BETWEEN UNBOUNDED PRECEDING AND 1 PRECEDING) AS segment
        FROM trends
        WHERE id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to
    ) rows
    GROUP BY id, segment, date_bin(@bucket, t, @from)
) sample
ORDER BY id, t;
```

The window function counts the break markers strictly before each row, so the marker row closes its
own segment and the next segment opens on the row after it; a bucket therefore ends at a break and
`breaks` is true only for the last bucket of a segment. The column's timestamp is the bucket's
newest sample and its value the sample at that timestamp, which is what the legend reads at the
cursor. The rows reach the window function through the index `(id, l, t)`, and whether the planner
also sorts them by `(id, t)` first is its own choice.
`ExplainPlanTests.TheBucketedRawPlanFeedsItsWindowAggregateThroughAnIndex` therefore asserts no
sequential scan over a row-holding partition, the aggregate's input reached through an index scan or
a bitmap over one, and the same seed-walk shape the seeded-window test pins.

**`BucketedRowFold` (Task 2).** `readonly record struct Row(int PenId, DateTime ArchiveLocal, double Value, double Min, double Max, bool EndsBreak)`;
`Fold(rows, timeConverter)` groups consecutive identifiers, converts and drops non-advancing
timestamps exactly as `HistoryRowFold` does, appends `(t, Min, Max, Value)` per row and a `NaN`
anchor one tick after a row whose `EndsBreak` is set, and builds the `PenHistoryEnvelope` directly
(no `MinMaxDecimator` pass: the server already reduced). The seed row arrives as a one-sample
bucket and needs no special case. `PostgresDataProvider.QueryHistoryAsync` branches on
`layer == AggregationLayer.Raw` after `ValidateArguments`: `ReadBucketedWindowAsync` +
`BucketedRowFold.Fold`, else the existing path. `BindBucketedWindow` binds `ids`, `from`, `to`,
`bucket` (`NpgsqlDbType.Interval`, `TimeSpan`).

**`PenScaleModel` (Task 3).** `ScaleMode` keeps `Auto` and `Manual`; the auto path applies
`IsInWindow` unconditionally. `PenScaleModelTests:97` and its fact become the auto-mode window
fact. `PenScaleSettings`, `PenScale`, `TrendChartViewModel` and `TrendToolbarViewModel` lose
nothing else: no production member referenced `AutoscaleToWindow`.

**`HistoryPrefetch` (Task 4).** `SemiPlot.UI/Chart/HistoryPrefetch.cs`:

```csharp
public readonly record struct FetchRange(DateTime FromUtc, DateTime ToUtc, AggregationLayer Layer, int ColumnTarget);

public static class HistoryPrefetch
{
    public const int MarginWindows = 1;

    public static FetchRange Expand(DateTime fromUtc, DateTime toUtc, AggregationLayer layer, int columnTarget);
    public static bool Covers(FetchRange fetched, DateTime fromUtc, DateTime toUtc, AggregationLayer layer, int columnTarget);
}
```

`Expand` widens by `MarginWindows × (toUtc - fromUtc)` on each side and multiplies the column
target by `2 × MarginWindows + 1`, clamped to the archive's first sample on the left (the
navigation model knows it) and to nothing on the right (a future window is a successful empty
read). `Covers` requires the same layer and column target, the visible width equal to the width
the range was expanded from (a zoom re-fetches), and the window inside the inner band. The view
model stores the last `FetchRange` it requested, sets it in `RequestHistory`, and consults `Covers`
in `OnNavigationWindowChanged` before calling `RequestHistory`; `RequestInitialHistory` and a column
target report go through the same gate. `ApplyHistory` and `DropPensMissingFromHistory` are
unchanged: the request still carries the pen identifiers it asked for.

**`ChartHistoryRequestDebouncer` (Task 5).** Constructor gains `TimeSpan capInterval` after
`debounceWindow`:

```csharp
var trailing = _requests.Throttle(debounceWindow, dataScheduler);
var paced = _requests.Sample(capInterval, dataScheduler);

var emissions = trailing
    .Merge(paced)
    .Where(request => !ReadsWhatTheLastQueryApplied(request))
    .Subscribe(Admit);
```

The dedup compares the request against the window the last **successful** query applied, not against
the previous emission: `FromUtc`, `ToUtc`, `Layer`, `TargetColumnCount` and the pen identifiers. A
failed read clears that window, so the same request is read again. `Admit` runs one query at a time
and parks the newest request that arrived while it ran; the slot release starts that one when the
running query lands, and drops it when it is the window the query just applied. The applied window,
the slot and the parked request are written from the emission head on the data scheduler and from
the thread a query completes on, so a `Lock` guards all three. The view model passes
`_historyCapInterval = TimeSpan.FromMilliseconds(400)`.

**`scripts/perf/trace-shares.py` (Task 7).** Reads a dotnet-trace Speedscope export, matches a
fixed frame list by substring, and prints call count, total, mean and max per frame from the
evented profiles. Python, standard library only, ~60 lines.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): the five mechanisms, their tests, the trace script,
  the documentation.
- **Post-Completion** (no checkboxes): the two stand measurements and the felt checklist, which
  need the operator's machine.

## Implementation Steps

### Task 1: Fill the band without anti-aliasing

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `docs/architecture/charting.md`

- [x] `BuildPenState` sets `band.FillStyle.AntiAlias = false` with a one-line comment naming the cost it removes and the doc anchor
- [x] write `AddPen_BandFillIsNotAntiAliased` beside the existing band assertions
- [x] `charting.md`, "Per-pen plottables": the band fill is not anti-aliased, with the 2026-09-07 numbers (62 ms to 4.8 ms per frame)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before Task 2 (706 passed)

⚠️ Superseded on 2026-09-07: the band and its fill are gone, so `band.FillStyle.AntiAlias = false` and
`AddPen_BandFillIsNotAntiAliased` went with them. `Chart/EnvelopeLine` replaced the `Scatter` + `FillY`
pair (`charting.md#per-pen-plottable-envelopeline`). The checkboxes above record what shipped at the time.

### Task 2: Decimate the Raw window on the server

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/BucketedRowFold.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Postgres/BucketedRowFoldTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs`

- [x] add `ArchiveStatements.BucketedRawWindow` per Technical Details, with a summary naming the segment rule
- [x] add `BucketedRowFold` with its `Row` and `Fold`
- [x] `PostgresDataProvider`: `ReadBucketedWindowAsync`, `BindBucketedWindow`, and the `Raw` branch in `QueryHistoryAsync`; the bucket interval is `(toLocal - fromLocal) / targetColumnCount`, never below one millisecond (the column's own resolution)
- [x] write `BucketedRowFoldTests`: rows fold to one column each in order; a row with `EndsBreak` yields its column then one `NaN` anchor; a non-advancing converted timestamp is dropped; two pens yield two envelopes with the seed first; no rows yield no envelopes
- [x] `ArchiveStatementTextTests`: the bucketed statement ends with one outer ordering, its window branch groups by the segment, and `BindBucketedWindow` names exactly the statement's parameters
- [x] `ExplainPlanTests.TheBucketedRawPlanReadsInIndexOrderWithoutASort`: no sequential scan over a row-holding partition, rows reached through an index, no `Sort` node feeding the window aggregate, the seed walk bounded as in the seeded-window fact
- [x] `PostgresHistoryReadTests.ARawWindowDenserThanTheCanvasComesBackBucketed`: over a seeded window with more raw rows than a target of 64 columns, every pen's envelope holds at most `64 + breaks + 1` columns, each column's `Min ≤ Center ≤ Max`, the columns are strictly ascending, and the window's overall min and max equal the seeded rows' min and max
- [x] `PostgresHistoryReadTests.ARawWindowSparserThanTheCanvasComesBackRowForRow`: at `TargetColumnCount = 4096` the existing seeded-window facts still hold, and `AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn` passes unchanged on the bucketed path
- [x] run both test projects - must pass before Task 3 (717 unit, 83 integration)

⚠️ The plan fact is named `TheBucketedRawPlanFeedsItsWindowAggregateThroughAnIndex` and does not assert
"no `Sort`". Measured on the bench container on 2026-09-07: at every window width from two minutes to eight
hours the planner reaches the window branch's rows through a Bitmap Index Scan and sorts them by `(id, t)`
before the window aggregate. The fact pins what stays true instead - no sequential scan over a row-holding
partition, the aggregate's input an index scan or a bitmap over one, and the seed walk bounded as before.

⚠️ `PostgresHistoryReadTests` sizes its Raw column target from the window
(`ColumnTargetFor`, at least 4096 and never letting a bucket exceed `RawLayerGenerator.PollInterval`).
Bucketing reduces by time, so at a flat 4096 the ten-plus-minute break window would have merged the
anchor/change row pairs the seeder writes 100 ms apart and
`AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn` would have failed.

### Task 3: Bound the auto scale to the visible window

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/ScaleMode.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenScaleModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/PenScaleModelTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/trend-feature-spec.md`,
  `docs/architecture/trend-interaction.md`

- [x] delete `ScaleMode.AutoscaleToWindow`; the auto path in `PenScaleModel` filters by `IsInWindow` unconditionally
- [x] rewrite the fact at `PenScaleModelTests:97` as the auto-mode window fact: columns outside the window do not widen the range
- [x] write a fact that an auto range over a sticky window past the last fetched column falls back to the
  whole envelope, and one that an auto range with no envelope at all falls back to `DefaultRange`
- [x] run the unit tests - must pass before Task 4 (718 passed)

⚠️ `DefaultRange` is not what an empty window falls back to. `PenScaleModel.ComputeRange` reads the whole
envelope first, because a sticky window advances past the newest fetched column without a requery and a
default range there sends the live curve off-scale. `DefaultRange` is reached only when no envelope exists
at all.

⚠️ Three architecture documents named the mode and moved with it: `charting.md` lists the scale modes,
`trend-interaction.md` names them as `auto`, `manual`, `autoscale-to-window`, and
`trend-feature-spec.md` carried it as requirement AY-5. AY-5 is now folded into AY-4, which is the
window-bounded `Auto`. `grafana-vs-build-evaluation.md` still names AY-5: it is a dated decision
record, not stable design, and is left as written.

### Task 4: Fetch one window of margin and pan inside it without a query

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/HistoryPrefetchTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] add `HistoryPrefetch` with `FetchRange`, `Expand` and `Covers` per Technical Details
- [x] `TrendChartViewModel`: keep `_lastFetch`, expand in `RequestHistory`, gate `OnNavigationWindowChanged`, `RequestInitialHistory` and the column-target report on `Covers`
- [x] write `HistoryPrefetchTests`, table-driven: expansion widths and column targets; the left clamp at the first sample; `Covers` true for a pan of half a width, false past the inner band, false on a zoom, false on a layer change, false on a column-target change
- [x] write `TrendChartViewModelTests.APanInsideThePrefetchedBandIssuesNoHistoryQuery` and `APanPastTheBandIssuesOneQueryForTheNewRange` on `FakeDataProvider.HistoryQueryCount` and `LastQueriedFromUtc`/`LastQueriedToUtc`
- [x] the column-target facts expect `HistoryPrefetch.MarginColumnFactor` times the reported width, so
  `ReportedWidth_SetsTheQueryResolutionUnquantized` already pins the requested target against the margin
- [x] run the unit tests - must pass before Task 5
 (738 passed)

⚠️ `FetchRange` carries a fifth member, `WindowWidth`. The left clamp shortens the range, so
`(ToUtc - FromUtc) / 3` is not the width `Covers` compares a zoom against. `Expand` takes the archive first
sample as its last argument, read through the new `ChartNavigationController.FirstSample` accessor.

⚠️ Existing `TrendChartViewModelTests` facts moved with the margin, because the fake provider answers one
column per edge of the requested range. `PanBackward_ReQueriesShiftedWindow` is gone: a ten-minute pan no
longer queries, and `APanPastTheBandIssuesOneQueryForTheNewRange` covers what it pinned, panning a full
window and asserting both expanded edges. `AfterStreamGoesQuiet_TheLastWindowIsQueried` seeds a first sample
30 days back so the left clamp does not bite, and asserts both expanded edges; the five column-target
assertions expect `HistoryPrefetch.MarginColumnFactor` times the reported width;
`CenterLine_ScatterDataSourceReflectsLoadedAndAppendedPoints` appends past the fetched range; and
`DeltaMode_TwoClicks_PlaceBothCursorsAndSurfaceDeltaTimeAndActivePenDeltaY` places its second cursor on the
last fetched column instead of the window end.

### Task 5: Fetch at a bounded interval during a gesture

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs`

- [x] add `capInterval` to the debouncer and the merged `Throttle`/`Sample` head per Technical Details, the
  applied window compared under the same lock as the in-flight slot
- [x] `TrendChartViewModel` passes `_historyCapInterval` of 400 ms
- [x] write `AContinuousGestureFetchesAtTheCapInterval` per Acceptance Evidence
- [x] write `TheSameWindowRequestedTwiceQueriesOnce`: the trailing throttle and the sample emitting the same window issue one query
- [x] confirm `RapidRequests_CollapseToOneTrailingQuery` still holds for a gesture shorter than the cap
- [x] run the unit tests - must pass before Task 6 (740 passed)

⚠️ `AContinuousGestureFetchesAtTheCapInterval` asserts exact counts, not `floor(2000 / cap) ± 1`: on the
`TestScheduler` the schedule is deterministic. The loop advances 20 ms before each of its 100 notches, so the
sample tick due at 2000 ms runs ahead of the notch pushed at 2000 ms, giving 5 queries during the gesture and
the trailing throttle's sixth after it. No `TrendChartViewModelTests` fact moved: those drive `PanBy` through
the prefetch gate, which already collapses a pan inside the band, so a sample tick finds no new window.

### Task 6: Verify acceptance criteria

- [x] run `dotnet build SemiPlot.slnx` - 0 warnings, 0 errors
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` - exit 0
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` and record the count here - 759 passed, 0 failed, 0 skipped after the review rounds (740 at the end of the task loop)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` and record the count here - 85 passed, 0 failed, 0 skipped after the review rounds (83 at the end of the task loop)
- [x] run `lint-comments` over every `.cs` file the branch touched - exit 0 over the 17 files of `git diff --name-only master...HEAD -- '*.cs'`
- [x] grep `AutoscaleToWindow` and `SparseHistoryWindow` across `SemiPlot` and `docs`: `AutoscaleToWindow` has no hit; `SparseHistoryWindow` has 13, all on the coarse path - the statement and its summary in `ArchiveStatements.cs`, `HistoryRowFold`'s summary, `ReadWindowAsync` (`PostgresDataProvider.cs:329`), the statement-text, plan-shape and seeded-window tests, and the `data-integration.md` statement table

### Task 7: Documentation and the trace script

**Files:**
- Create: `scripts/perf/trace-shares.py`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/plans/backlog.md`

- [x] add `scripts/perf/trace-shares.py` per Technical Details, with its invocation in a module docstring
- [x] `data-integration.md`: `BucketedRawWindow` joins the statement table with its segment rule; the "designed but not shipped" sentence at `:97-98` goes; the margin and the cap interval are described under the history path; the fresh tail note states the tail stays row-level
- [x] `charting.md`: the history path description names the prefetch band, the cap interval and the window-bounded auto scale
- [x] `backlog.md`: one entry for a custom band plottable drawing vertical segments per column (removes ScottPlot's per-frame LINQ over the polygon vertices, measured 78 MB/s at 30 Hz with 8 pens), deferred because the frame cost is already under the redraw cap
- [x] run `dotnet build SemiPlot.slnx` - clean (0 warnings, 0 errors)

⚠️ `ruff` is not installed on this machine (`ruff --version` and `python -m ruff` both fail),
so `trace-shares.py` ships unlinted and unformatted by it. It was run against both 2026-09-07 captures
instead: the drag trace prints `RenderOnce` mean 61.7 ms and `SKCanvas.DrawPath` total 25.6 s, the
no-anti-aliasing trace 4.8 ms and 0.05 s, which are the plan's own numbers.

## Post-Completion

**The two questions the branch capture must answer.** The prefetch margin widens what one query
covers to three visible windows, and neither cost of that is measured on the development machine.
Both readings are required before the branch ships, and each carries its own fallback.

1. **Frame cost with the margin.** `TrendPenState.LoadHistory` hands the band the whole fetched
   envelope, so `Polygon.Render` walks `2 x 6144 + 1` vertices per pen per frame instead of
   `2 x 2048 + 1`, with no viewport culling. The 4.8 ms mean under Overview was measured at 2048
   columns, before the margin existed. Pass is the threshold Acceptance Evidence already names:
   `RenderOnce` mean at most 10 ms. Fallback if it fails: slice what the plottables receive down to
   the visible window plus a skirt, keeping the whole envelope for the cursor, the legend and
   `HistoryPrefetch.Covers`; halving `HistoryPrefetch.MarginWindows` is the smaller alternative.
   The fallback is now implemented: `Chart/EnvelopeLine` culls to the visible X range plus one column on
   each side, so the margin costs the buffer but not the frame, and the whole envelope stays in hand for
   the cursor, the legend and `HistoryPrefetch.Covers`.
2. **Server time at the Raw ceiling with the margin.** The branch made the Raw read smaller in rows
   returned, not cheaper to run: `BucketedRawWindow` still reads and sorts every row of the range
   before the window aggregate, and the range is now three visible windows wide. Near the Raw ceiling
   that is 25.5 h, some 2.9 M rows on the bench against the 969 768 rows and 442 ms `count(*)`
   measured over 8.5 h on 2026-09-07. Pass is a query near the ceiling that runs inside the cap
   interval of 400 ms, so a continuous drag never waits on the server.
   `PostgresHistoryReadTests.TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed` records
   `EXPLAIN (ANALYZE, TIMING OFF)` and the plan's row counts for the widest read the seeded archive
   allows, at `HistoryColumnTarget.MaxColumns` times `HistoryPrefetch.MarginColumnFactor` columns;
   the stand reading is the same statement against the demo archive. Fallback if it fails: halve
   `HistoryPrefetch.MarginWindows`, which halves both the rows read and the vertices drawn.

**Stand measurements, before the branch ships.** The 45 s drag capture and the four-step felt
checklist under Acceptance Evidence, once on `master` and once on the branch; both sets of numbers
go into the pull request body.

| reading | master | branch |
| --- | --- | --- |
| `RenderOnce` mean / max | 61.7 ms / 386 ms | |
| `SKCanvas.DrawPath` total | 25.6 s | |
| `ApplyHistory` calls | 8 | |
| `QueryHistoryAsync` rows per Raw query near the ceiling | 969 768 | |
| `BucketedRawWindow` execution time near the Raw ceiling | n/a | |

**Interaction with the audit fix pass.** `docs/plans/20260907-audit-fix-pass.md` also edits
`ChartHistoryRequestDebouncer` and `TrendChartViewModel`; it runs after this plan and its executor
reads the tree as this plan leaves it.

**Executed by exec:**
- branch: chart-drag-responsiveness

## Verify it yourself

Every automated check below runs from the repository root on the branch. `master` at 43dad25 is
the before state; none of the named tests exists there.

1. **The build gate and both suites.** `dotnet build SemiPlot.slnx` reports 0 warnings,
   `dotnet format SemiPlot.slnx --verify-no-changes` exits 0, and
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` prints 759 passed;
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` prints
   85 passed with Docker Desktop up.
2. **The band fill.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AddPen_BandFillIsNotAntiAliased"`
   passes. On the stand: the 45 s drag capture under Acceptance Evidence, then
   `python scripts/perf/trace-shares.py <capture>.speedscope.json`; before, `RenderOnce` averaged
   61.7 ms, and the same scene with the fill change measured 4.8 ms on 2026-09-07 at 2048 columns.
   The branch reading with the margin is the open question in Post-Completion.
3. **Raw decimates on the server.** `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ARawWindowDenserThanTheCanvasComesBackBucketed|FullyQualifiedName~ABucketWideEnoughToSpanABreakStillSplitsAtTheMarker|FullyQualifiedName~TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed|FullyQualifiedName~TheBucketedRawPlanFeedsItsWindowAggregateThroughAnIndex"`
   passes; the last fact prints the `EXPLAIN (ANALYZE, TIMING OFF)` plan and the row count for the
   widest Raw read the margin asks for. On the stand: with the viewer just under the Raw ceiling,
   `docker exec <bench> psql -U postgres -d semiplot_app -c "select count(*) from trends where l = 0 and t >= now() - interval '8.5 hours'"`
   gives the rows the old read transferred; the new read returns at most
   `3 × columns + breaks + 1` rows per pen.
4. **A pan inside the band asks nothing.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~HistoryPrefetchTests|FullyQualifiedName~APanInsideThePrefetchedBandIssuesNoHistoryQuery|FullyQualifiedName~APanPastTheBandIssuesOneQueryForTheNewRange|FullyQualifiedName~AResultForAWindowTheDragLeftReQueriesTheWindowInView|FullyQualifiedName~APanInsideTheBandAfterAFailedQueryAsksAgain"`
   passes. On the stand: pan by half a window and stop, the viewer log shows no history read; pan
   by two windows, exactly one read lands; drag out past the band and back, then stop: the strip is
   filled after the second read.
5. **A gesture fetches while it moves.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartHistoryRequestDebouncerTests"`
   passes, including `AContinuousGestureFetchesAtTheCapInterval`,
   `ASlowQueryLandsAndOnlyTheNewestPendingWindowRunsAfterIt`,
   `AnIdenticalWindowAskedForWhileAFailingQueryIsInFlightIsStillRead` and
   `AThrowingApplyIsReportedAndTheNextWindowIsStillApplied`. On the stand: drag continuously for
   5 s just under the Raw ceiling; the exposed strip fills while the drag is still moving, one read
   at a time.
6. **The auto scale follows the visible window and survives the live edge.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenScaleModelTests|FullyQualifiedName~AFailedQueryOutsideTheBandStillAppliesTheAxis"`
   passes. On the stand: zoom in twice and out twice, the axis follows the visible data only; leave
   the viewer sticky on the live edge for longer than one window width, the axis keeps the pen's
   range instead of snapping to 0..1.
7. **Nothing else moved.** `grep -rn "AutoscaleToWindow" SemiPlot docs/architecture` is empty, and
   `docs/architecture/data-integration.md` names `BucketedRawWindow`, the margin and the cap interval.
