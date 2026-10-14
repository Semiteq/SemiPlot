# History read speed for 50 pens

## Overview

- The chart's history read grows linearly with the number of pens it names, and today it names every pen
  of the catalogue, hidden ones included. An installation carries 20 to 30 pens and SemiPlot must hold
  at 50, whether a few of them are visible or all of them.
- Four changes bring the first paint after a gesture under 1 s at 50 visible pens:
  1. the chart reads only the visible pens, and a pen switched on reads itself;
  2. the chart reads the visible window first and the prefetch margins after it;
  3. the provider splits one history read into up to four concurrent pen groups;
  4. the Raw and Minute ceilings shrink when more pens are visible than the ladder is sized for, so a
     crowded chart moves to the coarser layer earlier.
- What the operator sees today: the old picture stays and input never blocks during a read; a zoom-out to
  three weeks waits 1.4 s at 8 pens and about 9 s at 50 before the new columns appear.

## Context (from discovery)

- `TrendChartViewModel.RequestHistory` names `[.. _penSet.ById.Keys]`
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:517-526`); `_penSet` holds every pen the catalogue
  read returned (`ChartPenSet.Apply`, `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs:29-51`), and the
  catalogue statement has no filter (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:137-148`).
  SemiBase registers a `semiplot_tags` row for every variable in `trends` (`docs/architecture/sources.md`,
  `[DEC:semiplot-tags]`).
- Visibility is checked at draw time only: `SetPenVisibility` redraws and reads nothing
  (`TrendChartViewModel.cs:263-277`); the cursor and hover readouts skip hidden pens
  (`ChartCursorReader.cs:14`, `ChartHoverReadout.cs:21`).
- `_lastFetch` is a `FetchRange?` (`TrendChartViewModel.cs:45`; `readonly record struct FetchRange`,
  `SemiPlot/SemiPlot.UI/Chart/FetchRange.cs:6`). `ApplyHistory` nulls it first (`:544`) and sets it again only
  when the read named exactly the pens shown (`:566`, `ShowsExactly` at `:426-429`); a result that does not
  cover the window in view re-requests it (`:568-576`).
- `HistoryPrefetch.Expand` adds one visible width on each side, clamps the left edge at the first sample and
  triples the column target (`SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs:12-41`); `Covers` holds while the
  window stays half a margin inside each fetched edge (`:46-76`).
- `EnvelopeLine.ReplaceColumns` drops every live column after `requestedEndX`
  (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:127-146`), and `ApplyHistory` passes `request.ToUtc` as that
  end (`TrendChartViewModel.cs:561`). Today the request ends one window width past the live edge, so live
  samples appended during the read survive it.
- `ChartHistoryRequestDebouncer` runs one read at a time; a trailing request that does not match the read
  in flight cancels it (`SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs:116-145`, `Matches` at
  `:267-275`).
- `ChartNavigationController.LayerForWidth` derives the Raw and Minute ceilings from the next layer's point
  spacing times the column count, with a 10 % hysteresis that widens only the layer in force
  (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:194-217`, `:236-244`). Its callers:
  `LayerForCurrentWidth` (`:189-192`) and `MinimapBandFeed` (`SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs:146`),
  which reads one pen.
- `PostgresDataProvider.ReadHistoryAsync` reads all requested pens in one `BucketedHistoryWindow` statement
  on one connection, then fills the coarse layers' fresh tail
  (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:199-242`); `QueryHistoryAsync` wraps it with
  the log line and the error mapping (`:123-160`). The data source is built with no `MaxPoolSize`, so
  Npgsql's default of 100 applies (`PostgresDataServiceCollectionExtensions.cs:23`).
- `FreshTail.Start` takes one tail start from the earliest seam of the pens it is given
  (`SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs:39-61`), and the tail statement bins from that start.
  `PostgresHistoryReadTests` holds a fixture of pens with differing seams (`FreshPenId`, `LaggingPenId`,
  `ReachingPenId`, `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs:690`).
- Tests that pin today's behaviour: `RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad` and
  `AnArchiveYoungerThanTheWindowIsReadOnce` assert one query (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs:407-434`);
  `PostgresHistoryReadTests` asserts envelopes in ascending pen order (`:218`, `:239`, `:360`, `:413`, `:456`,
  `:504`, `:710`, `:1089`); `AHistoryReadLogsItsRequestOutcomeAndDuration` pins the log line (`:790`).
- The spec allows 50 pens (`docs/architecture/trend-interaction.md:155`).

### Measured 2026-10-13

Bench container `SemiPlot/bench` on PostgreSQL 17.11 with the `semibase site` settings passed as `-c`
flags (`shared_buffers=2GB`, `effective_cache_size=4GB`, `work_mem=64MB`, `maintenance_work_mem=512MB`,
`max_wal_size=8GB`, `checkpoint_timeout=30min`, `wal_compression=on`, `random_page_cost=1.1`), `jit = off`,
Docker Desktop with 10 CPUs. Every pen changes every second: Minute 2 rows per minute per pen. Server
execution time of `BucketedHistoryWindow` at the Minute layer, 15 min bucket:

| Read | 8 pens | 24 pens | 30 pens | 50 pens |
| --- | --- | --- | --- | --- |
| Minute ceiling, 3 widths (64 d at 6 144 columns), as today | 1 421 ms | 4 243 ms | - | - |
| Same, visible width only (21.3 d at 2 048 columns) | 462 ms | 1 396 ms | 1 893 ms | 3 159 ms |

- Read time is linear in visible width: at 8 pens the 3-width read takes 70 ms at 1 d visible, 469 ms at
  7 d and 998 ms at 14 d.
- The server never chooses a parallel plan for this statement: with `max_parallel_workers_per_gather` 0,
  2 or 4, and with `parallel_setup_cost = 0` and `parallel_tuple_cost = 0`, no plan holds a `Gather` and
  the time does not change.
- The visible-width read split into concurrent statements over pen groups (pgbench, `count(*)` over the
  statement, wall time of one round): 30 pens 1 045 ms in one statement, 806 ms in 2, 683 ms in 4, 530 ms
  in 8; 50 pens 2 083 ms, 1 238 ms, 988 ms, 844 ms.
- `work_mem` above 64 MB changes nothing; the sorts already run in memory at 64 MB.

## Development Approach

- **testing approach**: TDD. Each behaviour gets its failing test first.
- complete each task fully before moving to the next; make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change; `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet tool run terse`
  over the touched `.cs` files before each commit

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` for the chart view model, the prefetch gate, the navigation
  controller and the minimap feed; headless chart tests follow
  `docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view`.
- **integration tests**: `SemiPlot.Tests.Integration/PostgresHistoryReadTests` for the grouped read
  against the container.
- No timing is asserted in a test; read cost is measured and recorded in
  `docs/architecture/data-integration.md#read-cost`.

## Acceptance Evidence

1. Hidden pens are never read:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewModelTests"`
   passes a test that applies a catalogue of 50 pens with 8 switched on and asserts every history request
   names exactly those 8, and a test that switches one pen on and asserts the next request names that one
   pen alone over the range in hand.
2. The visible window is read first and the live edge survives it: the same filter passes a test asserting
   that a zoom's first request is the visible window and the request after its result is the
   `HistoryPrefetch.Expand` range, and a test that a realtime batch landing while a sticky window's first
   read is in flight is still drawn after that read lands.
3. A grouped read returns what one statement returns:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresHistoryReadTests"`
   passes a test that reads the fresh, lagging and reaching pens of the `:690` fixture together with
   ordinary pens, once as one group and once in pen groups, and asserts equal envelopes pen by pen at Raw
   and at Minute.
4. The ceilings follow the visible pen count:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartNavigationControllerTests"`
   passes tests that at `ReferencePenCount` visible pens or fewer the ceilings equal today's, and at twice
   that many each ceiling is half of today's. Skipped with Task 5 when `ReferencePenCount` is 50.
5. First paint under 1 s: the Task 6 measurement records, for a 50-pen catalogue at 2048 columns with 8, 30
   and 50 pens visible, the visible-window read of the Raw ceiling, Raw hysteresis edge, Minute ceiling,
   Minute hysteresis edge and Hour window cap, each width computed with the ceiling factor for its pen
   count; every median is at most 1 000 ms. The table lands in `docs/architecture/data-integration.md#read-cost`.
   A pan past the band issues the same detached visible-window read first, so the table covers it too.
6. `git grep -n "_penSet.ById.Keys" -- SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs` prints no line
   inside a method that builds a `HistoryRequest`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview

- **One request decision.** `ChartHistoryPlanner.Next` (`SemiPlot.UI/Chart/ChartHistoryPlanner.cs`) decides
  what to read from the range in hand, the
  pens read for it, the visible pens, the window in view, `Navigation.ActiveLayer` and the column target. It
  has four outcomes: nothing; the visible pens missing from the pens read over the range in hand; the
  visible window; the `Expand` range. `OnNavigationWindowChanged`,
  `RequestWindowInForce`, `SetPenVisibility` and `ApplyHistory` all ask it.
- **Visible pens only.** A request names visible pens in catalogue order. The planner holds the pens
  read for its range in hand; the window counts as fetched only when every visible pen is in it. It
  is never pruned on a catalogue removal, because it is only ever compared against visible pens.
- **Switching a pen on.** `SetPenVisibility` first hands the visible count to `Navigation.SetVisiblePenCount`
  (which may change the layer), then asks the decision. A pen missing from the pens read while the range in hand
  covers the window at `Navigation.ActiveLayer` is read alone over that range; its stale columns are cleared
  first, so no curve from another window is drawn or read by the cursor. Switching a pen off reads nothing.
  A hidden pen keeps its last envelope and is not drawn. The live poll keeps covering every catalogue pen,
  because a poll costs one indexed read per pen whatever is drawn.
- **Visible window first.** A window that is not covered is read first over the visible window, its left
  edge clamped at the first sample as `Expand` clamps it. A window reaching the newest sample keeps its
  right margin in that first read: nothing lies past the live edge, so it costs no rows, and live samples
  appended during the read survive `ReplaceColumns`. A request's column target is the visible target times
  one plus the margins it carries (`t` for the visible window, `2t` with the right margin, `3t` for
  `Expand`), so every read bins at `w / t`. When the result lands with no margins in hand, the decision
  returns the `Expand` range. It returns nothing instead when `Expand` adds no time that can hold rows:
  its clamped left edge equals the first read's and the right margin was read. The bucket width of both
  reads is the same unless `Expand` clamps at the first sample where the first read did not, and then the
  second read re-bins the columns. The cost: the server reads the visible window twice, 1.33x today's rows; a pan made after the
  first paint and before the margins land reads again; startup reads twice.
- **A margin-less read keeps the columns in hand outside it.** A read a gesture issues lands after the
  gesture moved on: a zoom out widened the window past it, or a drag, also one right after a zoom, moved
  the window off it. `EnvelopeSplice.Over` (`SemiPlot.UI/Chart/EnvelopeSplice.cs`) lays each pen's read
  over that pen's envelope in hand when the read's range overlaps the time that envelope was read over
  (its `ReadSpan`), and keeps the columns in hand past each side the range has no margin on; `ApplyHistory`
  stores the spliced envelope and loads it, so the cursor and the auto scale read what is drawn. That time
  is kept per pen, apart from the range in hand, because the gate shuts when the poll folds over Raw
  columns, which happens on every zoom out across the Raw ceiling, and when a read lands without a pen
  switched on meanwhile, and a shut gate must not drop the columns past the next read. The `Expand` read
  carries both margins and replaces every column, so nothing kept outlives it. A pan past the band
  therefore reads the visible window first like every other gesture; reading `Expand` at once would make
  a three-width first read, by linearity in width about three times the 747 to 816 ms Task 6 measures at
  50 pens (estimated, not measured).
- **Pen groups.** `PostgresDataProvider` splits the requested pens into up to `HistoryReadConcurrency` (4)
  contiguous groups of `ceil(n / 4)` pens (`Chunk`) and reads them in three phases, each under its own linked token: the window statements of all
  groups concurrently, each on its own pooled connection; then the seams over all rows and one
  `FreshTail.Start` across every pen, exactly as one statement computes it today; then the tail statements
  of all groups concurrently at that one start. A pen's envelope is therefore the same whatever group it
  sits in. The result is sorted by pen id. Four is the measured knee on 10 CPUs (2.1x at 50 pens; eight
  gives 2.5x) and leaves cores to the SCADA on the same machine. The read awaits every group
  (`Task.WhenAll`), so it returns only once every connection is back in the pool; a failing group cancels
  the others and its exception reaches the mapping. A cancelled grouped read opens up to four cancel
  connections instead of one.
- **Pen-aware ceilings.** `LayerForWidth` takes the visible pen count. Each ceiling is multiplied by
  `min(1, ReferencePenCount / max(1, visiblePens))`. `ReferencePenCount` is the largest measured visible pen
  count at which both the Raw and the Minute hysteresis-edge visible reads stay at or under 1 000 ms end to
  end with pen groups (Task 4); at 50 the factor is 1 everywhere and Task 5 is skipped. The cost: past that
  count the chart shows the coarser layer over windows that would have read the finer one, and since the
  hysteresis widens only the layer in force, switching one pen on can move the chart to the coarser layer
  while switching that pen off again keeps it there until a few more pens go off or the window narrows. The
  minimap passes one pen and keeps its ladder. `ReferencePenCount` is measured on the bench; on the plant it
  is a constant, and changing it takes a rebuild.
- **Rejected.** Server-side parallel query (no parallel plan at any setting); a client cache of coarse
  layers (the `date_bin` grid is anchored at each request's own `@from`, so a cache hits only on an
  identical grid, and coarse rows arrive up to 47 minutes late, so a cached tail goes stale); a session
  `work_mem` (the server's `semibase site` value already holds every sort in memory).

## Technical Details

- `ChartHistoryPlanner` (owned by `TrendChartViewModel`)
  - `Next(visiblePenIds, windowStart, windowEnd)` returns a `HistoryRequest?` per the four outcomes above;
    the view model pushes it or `RequestNothing()`.
  - `Land(request, rangeBefore, visiblePenIds)` replaces the pens read with the request's pens when the
    result's range differs from the range before it and adds to them when the range is the same; the range
    in hand stays set when the result's pens cover the visible pens or join the set for the same range.
  - `IsWindowFetched` additionally requires every visible pen to be in the pens read; `RangeInHand` is the
    gate, and `Forget()` shuts it without touching the per-pen read spans.
  - A switched-on pen joins the range in hand at the column count that range was read at.
- `TrendChartViewModel`
  - `ApplyHistory` splices each envelope with `EnvelopeSplice.Over` against the pen's `ReadSpan` in
    `_readSpansById`, records the new span after `LoadHistory` returns, and keeps passing the request's end
    as `requestedToUtc`; a first read at the live edge ends one margin past it, so the live columns are kept
    exactly as before.
  - `RequestInitialHistory`'s summary names the visible pens.
- `FetchRange` carries the margin windows it was expanded by on each side; `HistoryPrefetch.Covers` uses the
  fetched range's own margins, so a visible-only range covers exactly its window, and compares the column
  target against the range's own scaled target, the visible target times one plus its margins.
- A pen switched on and not yet read is cleared with `ClearHistory(RangeInHand?.ToUtc ?? windowEnd)`
  (`ClearAnUnreadCurve`), the end of the range in hand rather than the window end when one is in hand, so
  the live tail the poll appended survives the clear and the pen's read.
- `ChartNavigationController`
  - `public const int ReferencePenCount`, value from Task 4.
  - `SetVisiblePenCount(int count)`: stores the count, recomputes `ActiveLayer`, raises `WindowChanged`
    when the layer changes.
  - `LayerForWidth(width, currentLayer, targetColumnCount, visiblePenCount)`: ceilings
    `nextCoarser.ToPointSpacing() × targetColumnCount × min(1, ReferencePenCount / max(1, visiblePenCount))`,
    hysteresis unchanged.
- `PostgresDataProvider`
  - `internal const int HistoryReadConcurrency = 4`.
  - `ReadHistoryAsync`: partition into contiguous groups of `ceil(n / HistoryReadConcurrency)` pens with
    `Chunk`, at most `HistoryReadConcurrency` of them, the largest the size an even split gives; phase one the window statements, phase two `FreshTail.Seams` and `FreshTail.Start` over all
    rows, phase three the tail statements at that start; `Task.WhenAll` per phase, each phase under its own
    linked `CancellationTokenSource`; `BucketedRowFold.Fold` over the merged rows; envelopes sorted by `PenId`.
  - The debug log line gains the group count.

## What Goes Where

- **Implementation Steps**: code, tests, the end-to-end measurement and the documents in this repository.
- **Post-Completion**: the viewer smoke check on the stand, the plant log check, and delivery after
  `drop-day-layer`.

## Implementation Steps

### Task 1: Read only the visible pens

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] write failing tests: a 50-pen catalogue with 8 pens switched on issues requests naming exactly those
      8; switching a ninth pen on while the window is fetched issues one request naming that pen over the
      range in hand and clears its columns until the result lands; switching a pen off issues no request;
      a gesture after the switch-on names all 9
- [x] write failing tests: the window is not reported fetched while a visible pen is missing from the set
      read for the range; a result for one pen over the range in hand keeps the band fetched for the others;
      a result for one pen over another range replaces the set
- [x] introduce `NextHistoryRequest()` with the outcomes this task needs (nothing, missing pens, the `Expand`
      range as today; Task 2 replaces the third with the visible window and the `Expand` range) and
      route `OnNavigationWindowChanged`, `RequestWindowInForce`, `SetPenVisibility` and `ApplyHistory`
      through it
- [x] keep `_fetchedPenIds` beside `_lastFetch`, capturing the previous range in `ApplyHistory`, and revise
      the gate at `:566` and the `RequestInitialHistory` summary
- [x] update tests that assert the old all-pens request, such as `History_OmittingARequestedPen_ClearsThatPensCurve`
      (none needed: every existing test shows all its pens, so the visible-pen request equals the old one)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 2

### Task 2: Read the visible window before the margins

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/FetchRange.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/HistoryPrefetchTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] write failing `HistoryPrefetchTests`: a range with no margins covers its own window and nothing panned
      off it; a range with only a right margin covers its window and a pan right inside half that margin;
      an expanded range keeps today's inner band
- [x] write failing `TrendChartViewModelTests`: a detached zoom's first request is the visible window at
      the visible column target, a sticky zoom's first request carries the right margin at twice that target,
      the request after either result is the `Expand` range at three times it, and a pan inside the band after
      the second result issues no request; a gesture ending while the margin read is in flight cancels it and
      reads the new visible window first
- [x] write a failing test: on a sticky window, a realtime batch landing while the first read is in flight is
      still drawn after that read lands (passes on the old `Expand`-first code too, which also carried the right
      margin; it fails once the first read drops that margin)
- [x] update `RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad` to the first read and its margin
      read; keep `AnArchiveYoungerThanTheWindowIsReadOnce` at one read from the first sample (`:435`), because
      the first read clamps its left edge there and `Expand` adds no time that can hold rows
- [x] add the margins to `FetchRange`, make `Covers` honour them, and add the visible-window and `Expand`
      outcomes to `NextHistoryRequest()`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 3
- ➕ `ChartTestBuilder.LoadInitialHistory` lands both reads, and the tests that counted one read per gesture or
  asserted the `Expand` range as the first read (`TrendChartViewModelTests`, `TrendChartCatalogueTests`,
  `PenCatalogueApplierTests`) count or assert the visible window first

### Task 3: Read pen groups concurrently

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PenGroupTests.cs` (if the partition is a pure helper)

- [x] write failing integration tests: the fresh, lagging and reaching pens of the `:690` fixture with
      ordinary pens give equal envelopes pen by pen read as one group and as four, at Raw and at Minute; a
      grouped read cancelled mid-flight throws `OperationCanceledException` and
      `NpgsqlDataSource.Statistics` reports no busy connection after the awaited throw
      (`Statistics` is internal in Npgsql 10.0.3, so the test reads it by reflection; a late pen 6 joins the
      fixture so a tail start taken per group fails the wide-bucket Minute case)
- [x] write a failing test for the partition: `n` pens give `min(4, n)` contiguous groups whose sizes differ
      by at most one, in request order
- [x] read the groups in the three phases under one linked token and sort the envelopes by pen id
      (the ids are sorted before the split, so the groups' rows concatenate in ascending pen order)
- [x] add the group count to the debug log line and update `AHistoryReadLogsItsRequestOutcomeAndDuration`
      (`:790`); confirm the ascending-order assertions at `:218`, `:239`, `:360`, `:413`, `:456`, `:504`,
      `:710` and `:1089` still hold
- [x] run both test projects - must pass before task 4
- ➕ `PenGroups.cs` holds the partition, and the provider takes its group count as a constructor argument
  (`AddPostgresData` passes `HistoryReadConcurrency`) so the integration test can read one group

### Task 4: Measure the read end to end and set the reference pen count

**Files:**
- Modify: `docs/plans/20261013-history-read-speed.md` (this plan: the table under this task)

- [x] start a throwaway container from `SemiPlot/bench` on a free port (never 15432, 55432, 55433 or 5432;
      `pho-db` and `scada-capture` stay untouched) with the `semibase site` settings listed under
      "Measured 2026-10-13" as `-c` flags
- [x] fill `semiplot_provisioned` the way `docs/plans/20261012-drop-day-layer.md:520-525` describes, with 50
      pens instead of 8: day partitions from `PartitionScript.CreateStatements`, rows from `generate_series`
      with random values and `q = 0`, raw at 1 per second over 30 h, minute at :10 and :40 s over 72 days,
      hour at :10 and :40 min over 1 096 days, all ending 2026-10-12 00:00; then `VACUUM ANALYZE`
- [x] build the harness `docs/plans/20261012-drop-day-layer.md:526-538` describes (a Release console project
      in the scratchpad composing the provider through `AddPostgresData`, `TimeZoneInfo.Local`, the bench
      `semiplot` role); it is not committed
- [x] time the visible-window read of the Raw ceiling, Raw hysteresis edge, Minute ceiling, Minute
      hysteresis edge and Hour window cap at 2048 columns with 8, 16, 24, 30, 40 and 50 pens, each window
      ending one width before the archive end; 2 warm-up and 11 timed calls per case; median, minimum and
      maximum
- [x] set `ReferencePenCount` to the largest measured pen count whose Raw and Minute hysteresis-edge visible
      reads both have a median at most 1 000 ms, and record the table and that value under this task; when
      it is 50, mark Task 5 skipped
- [x] remove the container with its image and volume

Measured 2026-10-13: the `SemiPlot/bench` image on PostgreSQL 17.11 with the `semibase site` settings of
"Measured 2026-10-13" as `-c` flags; Docker Desktop 29.8.1 with 10 CPUs and 16 GB; .NET SDK 10.0.401 on
runtime 10.0.12, Npgsql 10.0.3, harness built in Release against `47760c1`; provider time zone
`Russian Standard Time`; `SHOW jit` over the resolved `NpgsqlDataSource` reads `off`; the provider composed
by `AddPostgresData` reads in `HistoryReadConcurrency` = 4 pen groups. The archive: 50 pens, 5 400 000 raw,
10 368 000 minute and 2 630 400 hour rows, in 1 096 day partitions, `VACUUM ANALYZE` after the load.

A case is the read `NextHistoryRequest` issues first for a detached window: `HistoryPrefetch.Visible` over
`[E - 2w, E - w]`, `E` the archive end 2026-10-12 00:00 archive-local. The window ends before the newest
sample and is not sticky, so the read carries no right margin and asks for 2048 columns; its left edge lies
after the first sample, so no clamp applies. Pens are ids 1 to n. Widths: Raw ceiling 15 s x 2048 = 8 h 32
min, Raw hysteresis edge 1.1x that, Minute ceiling 15 min x 2048 = 21 d 8 h, Minute hysteresis edge 1.1x
that, Hour window cap 365 d. Every envelope holds 2 049 columns (2 048 buckets and the seed row).

The host ran other sessions' container tests during the runs, and single rounds were noisy (the 16-pen Minute
hysteresis edge's round medians span 633 ms to 1 696 ms), so every case ran in eight rounds: two runs of four, the order of pen counts
and cases reversed every round. A round is 2 warm-up and 11 timed calls per case. A time is the median of the
88 timed calls, min to max in brackets.

| Case | 8 pens | 16 pens | 24 pens | 30 pens | 40 pens | 50 pens |
| --- | --- | --- | --- | --- | --- | --- |
| Raw ceiling | 149 (79-546) | 314 (210-1 354) | 494 (326-2 397) | 512 (403-2 250) | 808 (546-3 163) | 882 (678-4 005) |
| Raw hysteresis edge | 157 (89-1 164) | 397 (229-2 330) | 565 (352-2 219) | 564 (465-1 757) | 808 (581-5 889) | 1 012 (720-3 364) |
| Minute ceiling | 348 (268-1 677) | 861 (507-3 473) | 1 065 (741-4 062) | 1 290 (1 003-3 733) | 1 661 (1 236-7 158) | 2 050 (1 574-6 414) |
| Minute hysteresis edge | 406 (293-1 997) | 837 (547-2 789) | 1 141 (814-3 949) | 1 290 (1 047-3 785) | 2 053 (1 350-8 492) | 2 208 (1 717-8 197) |
| Hour window cap | 252 (186-1 940) | 388 (278-2 074) | 458 (373-2 596) | 578 (420-3 486) | 786 (580-3 614) | 839 (660-3 542) |

Round medians, ms, of the two cases that decide the count:

- Minute hysteresis edge, 16 pens: 1 671, 1 696, 717, 633, 644, 916, 929, 1 034
- Minute hysteresis edge, 24 pens: 1 152, 874, 1 270, 1 004, 1 138, 1 830, 1 361, 995

`ReferencePenCount` is 16: the Raw and Minute hysteresis edges hold at 397 ms and 837 ms there, and the
Minute hysteresis edge is the first case past 1 000 ms, at 1 141 ms with 24 pens. A further four rounds of
16 and 24 pens alone, on a quieter host, gave 647 ms and 963 ms; pooled with the eight rounds (132 calls) the
24-pen Minute hysteresis edge is 1 053 ms, still over. Task 5 runs.

### Task 5: Shrink the ceilings with the visible pen count

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartNavigationControllerTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapBandFeedTests.cs`

- [x] write failing `ChartNavigationControllerTests`: at `ReferencePenCount` pens or fewer the Raw and Minute
      ceilings equal today's at 256 and 2048 columns; at twice that count each is half; hysteresis still
      widens only the layer in force
- [x] write a failing test naming the counts: at a width just under the Minute ceiling for `ReferencePenCount`
      pens, switching on `floor(0.1 × ReferencePenCount) + 1` more pens moves `ActiveLayer` to Hour, past the
      10 % hysteresis, and issues one read at Hour and no single-pen read at Minute; switching the last of
      them off keeps Hour
      (run at the Raw ceiling instead, Raw to Minute at 512 columns: zoom widths step by 1.25, and no width
      at any column count lands in the 2.2 % below a Minute ceiling that two extra pens of 16 need)
- [x] write a failing `MinimapBandFeedTests` case: the minimap's layer is today's at every extent
      (with twice `ReferencePenCount` pens shown; it guards against the chart's count reaching the minimap
      and passes on the code before this task)
- [x] add `ReferencePenCount`, `SetVisiblePenCount` and the pen count argument; pass the visible count from
      `TrendChartViewModel` on every catalogue apply and visibility change, ahead of the request decision,
      and 1 from `MinimapBandFeed`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 6
- ➕ `TrendChartCatalogueTests.ADeltaAdding500Pens_ComputesTheAxisModelOnce` narrows its window first: 500
  visible pens move a 1 h window from Raw to Minute, and that window change computes the axis model again

### Task 6: Verify acceptance criteria

- [x] re-run the Task 4 measurement at the branch tip with each case's width computed with the ceiling factor
      for its pen count, and confirm Acceptance Evidence 5 for 8, 30 and 50 visible pens
- [x] run Acceptance Evidence 1, 2, 3, 4 and 6
      (6 prints one line, `ShowsExactly` at `:455`, which compares the coordinator's pen set and builds no
      `HistoryRequest`)
- [x] run the full suite: `dotnet test SemiPlot.slnx`
- [x] `dotnet build SemiPlot.slnx` reports 0 warnings

Measured 2026-10-13 at `5038016` under the conditions of Task 4: a fresh container from the `SemiPlot/bench`
image on PostgreSQL 17.11 with the same `-c` flags, the same 50-pen archive loaded the same way (5 400 000 raw,
10 368 000 minute and 2 630 400 hour rows, `VACUUM ANALYZE` after the load); Docker Desktop 29.8.1 with 10
CPUs and 16 GB; .NET SDK 10.0.401 on runtime 10.0.12, Npgsql 10.0.3; the harness of Task 4 built in Release
against `5038016`; provider time zone `Russian Standard Time`; `SHOW jit` reads `off`; four pen groups. A
case is the same detached visible-window read over `[E - 2w, E - w]` at 2048 columns, pens 1 to n. The
harness takes each width from the production `ChartNavigationController.LayerForWidth` with the pen count:
the widest width that still answers the case's layer, entered from the coarser layer for a ceiling and from
the layer itself for a hysteresis edge, so each ceiling carries the factor
`min(1, ReferencePenCount / max(1, pens))` = 1, 16/30 and 0.32. The Hour window cap is 365 d at every count.
Every envelope holds 2 049 columns.

| Case | 8 pens (factor 1) | 30 pens (factor 0.533) | 50 pens (factor 0.32) |
| --- | --- | --- | --- |
| Raw ceiling | 8 h 32 min | 4 h 33 min 4 s | 2 h 43 min 50.4 s |
| Raw hysteresis edge | 9 h 23 min 12 s | 5 h 0 min 22.4 s | 3 h 0 min 13.44 s |
| Minute ceiling | 21 d 8 h | 11 d 9 h 4 min | 6 d 19 h 50 min 24 s |
| Minute hysteresis edge | 23 d 11 h 12 min | 12 d 12 h 22 min 24 s | 7 d 12 h 13 min 26.4 s |
| Hour window cap | 365 d | 365 d | 365 d |

Single calls spiked up to 3.6 s, so every case ran in eight rounds as in Task 4: two runs of four, the order
of pen counts and cases reversed every round, 2 warm-up and 11 timed calls per case and round. A time is the
median of the 88 timed calls in ms, min to max in brackets.

| Case | 8 pens | 30 pens | 50 pens |
| --- | --- | --- | --- |
| Raw ceiling | 124 (74-257) | 280 (241-772) | 413 (278-1 923) |
| Raw hysteresis edge | 141 (81-222) | 296 (257-519) | 405 (301-1 642) |
| Minute ceiling | 305 (255-436) | 624 (526-3 507) | 685 (554-2 060) |
| Minute hysteresis edge | 318 (274-395) | 712 (552-3 515) | 747 (631-3 627) |
| Hour window cap | 206 (179-349) | 535 (421-2 804) | 816 (665-2 076) |

Every median is at most 1 000 ms; the highest is the 50-pen Hour window cap at 816 ms, whose round medians
are 876, 979, 944, 817, 827, 830, 705 and 707. Acceptance Evidence 5 holds.

### Task 7: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/plans/backlog.md`
- Modify: `AGENTS.md`

- [x] `data-integration.md#what-one-history-query-covers`: visible pens only, the request decision, the
      visible window first with the right margin kept at the live edge, the pen set kept beside the range,
      and the cancel paragraph's four cancel connections for a grouped read
- [x] `data-integration.md#layer-ladder`: the pen factor in the ceiling formula, `ReferencePenCount`, the
      hysteresis ratchet on a pen toggle (omitted when Task 5 is skipped)
- [x] `data-integration.md#operation-to-statement`: pen groups, the three phases, `HistoryReadConcurrency` and
      why four
- [x] `data-integration.md#read-cost`: the Task 6 table with its conditions, and the rejected options with the
      numbers above
- [x] `charting.md#applying-a-catalogue-read`: a hidden pen keeps its last envelope, and a pen switched on is
      cleared and read alone
- [x] `backlog.md`: close the Minute read cost entry (`:53-62`); rewrite the drag-lag entry (`:44-51`) so it no
      longer plans the coarse-layer cache and states why
- [x] `AGENTS.md`: the `BucketedHistoryWindow` bullet names the pen groups
- [x] ⚠️ deferred to delivery, not done on this branch: move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** on the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. With every pen switched on, zoom out to three weeks: the new columns appear in under a second and the
   margins fill shortly after.
2. Switch half the pens off and back on: a pen switched on draws its curve over the window in view, and the
   Debug log shows one read naming that pen alone.
3. In follow mode, zoom out and back in: the live edge shows no gap after either read lands.
4. With every pen on, the status bar shows the hour layer at a width where 8 pens show the minute layer
   (only when Task 5 ran).

**Plant check**: run the viewer at `--logging-level Debug` for one shift; every history read logs its layer,
range, pen count, group count and milliseconds. A read past 1 s at the plant's pen count means
`ReferencePenCount` is too high for that machine; it is a constant, and lowering it takes a rebuild.

**Delivery**: this branch is cut from `drop-day-layer` and ships after it.

**Executed by exec:**
- branch: history-read-speed

## Verify it yourself

Before state is `drop-day-layer` (`06ab658`): every history read names every catalogue pen and reads the
`Expand` range (three widths at 3x columns) as one statement; the layer ceilings ignore the pen count. The
tests below are new on this branch, so on `06ab658` they do not exist rather than fail; the measured
before-numbers are in `## Context` ("Measured 2026-10-13").

1. Hidden pens are not read, a pen switched on is read alone:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AFiftyPenCatalogueWithEightSwitchedOn_ReadsOnlyThoseEight|FullyQualifiedName~SwitchingAPenOn_ReadsItAloneOverTheRangeInHandAndClearsItsCurveUntilTheReadLands"`.
   On the stand: switch half the pens off and one back on; the Debug log shows one history read with
   `PenCount` 1.
2. The visible window is read first, then the margins, and the live edge survives:
   `--filter "FullyQualifiedName~ADetachedZoomReadsTheVisibleWindowFirstAndItsMarginsAfterIt|FullyQualifiedName~ABatchLandingWhileAStickyWindowsFirstReadIsInFlightIsDrawnAfterItLands"`.
   On the stand: zoom out from 1 h to three weeks with every pen on; the Debug log shows a read over the
   window alone, then a second read three widths wide.
3. A read landing mid-gesture keeps the columns already drawn:
   `--filter "FullyQualifiedName~ADragPastTheBandKeepsTheColumnsInViewWhenItsReadLands|FullyQualifiedName~AZoomsReadLandingDuringTheDragThatFollowsKeepsTheColumnsInView|FullyQualifiedName~AZoomOutAcrossTheRawCeilingKeepsTheRawColumnsPastTheCoarseReadAfterABatch"`.
   On the stand: wheel out steadily across the Raw ceiling (about 8.5 h) with live data arriving, then grab
   and drag; no strip beside the curves goes blank while the reads land. No reliable pass/fail repro exists
   by eye for a sub-second blank, so the tests are the evidence.
4. Pen groups return what one statement returns, and a failing group surfaces its own error:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~AReadSplitIntoPenGroupsReturnsWhatOneStatementReturns|FullyQualifiedName~AGroupFailingWhileTheOthersWaitFailsTheReadAfterEveryConnectionReturns"`
   (needs Docker). On the stand the Debug log line of every history read carries `GroupCount`.
5. The ceilings shrink past 16 visible pens:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AtTwiceTheReferencePenCount_EachCeilingIsHalved|FullyQualifiedName~ACatalogueShowingTwiceTheReferencePenCount_IsReadAtTheCoarserLayer"`.
   On the stand: at a 6 h window, 8 pens show Raw in the status bar; with 32 or more pens on, the same
   window shows Minute.
6. Read time: the Task 6 table (every median at or under 816 ms at 8, 30 and 50 visible pens) against the
   `## Context` before-numbers (24 pens, 21.33 d Minute read with margins: 4 243 ms). On the plant, the Debug
   log's milliseconds per history read are the check; a read past 1 s at the plant's pen count means
   `ReferencePenCount` is too high for that machine.
7. Whole suite and build: `dotnet test SemiPlot.slnx` (unit 1648, integration 146 at `915ade5`) and
   `dotnet build SemiPlot.slnx` with 0 warnings.
