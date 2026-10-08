# Live tail gaps

## Overview

A pen in follow mode can lose samples the viewer already received or should have read, and the line then
draws one straight segment across the hole until a gesture re-reads the window. Three defects are confirmed;
the 40 s segment the operator saw on the bench is not explained by any of them on a healthy database, so a
log of every history read comes first.

- **History read log.** The provider logs nothing for a history read, so a bench session cannot tell a slow,
  cancelled or failed read from a normal one. The realtime poll already logs every tick
  (`SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs:217-218`).
- **The coarse fold hole.** At a coarse layer a realtime sample folds into the last column instead of
  appending. When the coarse read never applies, the columns in memory are still Raw and every live sample of
  the coarse phase folds into one Raw column; a zoom back to the same width finds the Raw fetch still covering
  the window and reads nothing. Verified 2026-10-07 with a held Minute read: a 40.17 s hole after 40 s zoomed
  out.
- **The history-apply race.** A history read reads the archive as of its statement's snapshot. Live columns
  appended between that snapshot and the apply are wiped by the apply, and the realtime poll never delivers
  them again. Verified 2026-10-07: 23 live columns appended while a Raw read was held were gone after its
  apply. On the bench the hole is about one sample; it grows with read latency.
- **The fresh tail never runs.** `QueryHistoryAsync` hands `FillFreshTailAsync` the expanded right edge, one
  window past the visible end, and `FreshTail.Start` clamps four point spacings back from it. Verified
  2026-10-07: a Minute read over the range the chart asks for ends at 09:04:00 while the raw newest is
  09:04:50. `FreshTail` was written when the chart read exactly the visible window; the prefetch margin of
  `HistoryPrefetch.Expand` (`SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs:39-40`) moved the edge.

Tasks 1-6 and the review fixes were built on one branch, `live-gap-probes`, one commit per task; delivery
decides how the branch ships.

## Context (from discovery)

- `PostgresDataProvider.QueryHistoryAsync` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:119-177`,
  59 lines) returns argument failures before its `try` (`:129-140`), reads Raw through `ReadBucketedWindowAsync`
  (`:151-162`) or a coarse layer through `ReadWindowAsync` plus `FillFreshTailAsync` (`:165-171`), and maps any
  exception through `Map` (`:173-176`, `:567-570`). `ArchiveExceptionMapper.Map` rethrows an
  `OperationCanceledException` (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:24-28`), so a
  cancelled read leaves `QueryHistoryAsync` by throwing
  (`PostgresHistoryReadTests.AReadCancelledMidStatementThrowsInsteadOfFailing`,
  `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs:219-259`).
- The provider holds `_logger` (`PostgresDataProvider.cs:35`) and `_timeConverter` (`:31`); its constructor is
  internal (`:50-65`), built by a factory in `AddPostgresData`
  (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:15-39`) and directly by
  `PostgresCompositionTests.NewProvider` (`SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs:32-41`),
  which also pins every registration `AddPostgresData` makes as a single singleton (`:122-137`).
- `FillFreshTailAsync` is `private static` (`PostgresDataProvider.cs:392-417`): it computes the seams, asks
  `FreshTail.Start(layer, seams, toLocal)` (`:403`) and reads raw rows from the tail start to `toLocal`
  (`:408-415`). `FreshTail.Start` returns the earliest seam at or past `windowEndLocal - 4 * spacing`, or null
  when none reaches it or the layer is fresh within one spacing (`SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs:41-62`).
  `FreshTailBoundTests` (`SemiPlot/SemiPlot.Tests.Unit/Postgres/FreshTailBoundTests.cs`) pins `Start` alone.
- `ArchiveProviderFactory.Build(connectionString, loggerProvider)` composes the provider for container tests
  (`SemiPlot/SemiPlot.Tests.Integration/ArchiveProviderFactory.cs:24-38`). `LiveCatalogueTests` hands it a
  `FakeLoggerProvider` and asserts on structured state (`SemiPlot/SemiPlot.Tests.Integration/LiveCatalogueTests.cs:126-127`,
  `:207-217`). The tail tests write a 2026-01-01 archive (`PostgresHistoryReadTests.cs:88-100`) and read the
  visible window itself (`:969-978`).
- `TimeProvider` is the clock seam the repository already uses (`SemiPlot/SemiPlot.UI/Messages/MessagePanelViewModel.cs:19-23`),
  with a private fixed-clock subclass in a test (`SemiPlot/SemiPlot.Tests.Unit/UI/Messages/MessagePanelViewTests.cs:147`).
- The live path never reads the local clock: `RealtimePoll.LastSeen` stays on the archive's own naive clock
  (`SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs:79-83`). The SCADA, its archive and the viewer
  share one machine `[DEC:machine-time-zone]` (`docs/architecture/data-integration.md:405-407`).
- `TrendChartViewModel.ApplyRealtimeBatch` folds when `Navigation.ActiveLayer != AggregationLayer.Raw`
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:674`), whatever layer the columns in memory hold.
- `_lastFetch` (`TrendChartViewModel.cs:46`) is the fetched range the gate reads: cleared at the start of
  `ApplyHistory` (`:551`), set at its end when the read named exactly the pens shown (`:573`), cleared by
  `RequeryAllPens` (`:429`). `IsWindowFetched` (`:512-517`) passes it to `HistoryPrefetch.Covers`, which
  compares layer, column target and width first (`HistoryPrefetch.cs:54-62`).
- `OnNavigationWindowChanged` pushes `RequestNothing` when the window is covered (`TrendChartViewModel.cs:491-494`).
  `OnLiveEdge` raises a window with no re-query (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:149-167`),
  so in follow mode a hole stays until a gesture leaves the band or changes width or layer.
- `ApplyHistory` (`TrendChartViewModel.cs:549-592`) calls `TrendPenState.LoadHistory` per envelope (`:568`)
  and `DropPensMissingFromHistory` (`:615-630`), which calls `ClearHistory` for a requested pen with no envelope.
- `TrendPenState.LoadHistory` builds the columns and calls `Line.ReplaceColumns`, then sets `CurrentValue`
  from the last non-gap center (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:55-70`); `ClearHistory` calls
  `Line.ClearColumns` (`:72-76`).
- `EnvelopeLine` holds `_columns` under `_renderStateLock`; `ReplaceColumns` clears and refills
  (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:117-124`), `ClearColumns` empties (`:126-132`), `AppendColumn`
  rejects `X <= last X` and trims `TrimChunk` oldest columns past `MaxColumns` (`:134-151`, constants `:14-15`).
- A Raw bucket is stamped with the newest timestamp of its rows (`ArchiveStatements.cs:235`); a coarse column
  is stamped with its bucket's center sample (`SemiPlot/SemiPlot.Core/Trends/MinMaxDecimator.cs:162-166`).
- The realtime poll only moves `LastSeen` forward (`RealtimePoll.Advance`, `RealtimePoll.cs:229-231`).
- Tests: `TrendChartViewModelTests` pins today's replace with
  `Realtime_AfterLoadHistoryMovesTheSeamBack_AppendsAgainstTheReloadedSeries`
  (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs:798`) and
  `Realtime_AfterClearHistory_AppendsAnyTimestampAgain` (`:816`), both appending a live column before the reload
  at a timestamp later than the reload's end; and `Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns`
  (`:894`), a coarse view with no history, zoomed with `ZoomAt(48.0, Navigation.To)`. Its private helpers
  `ReleaseAndAwaitResults` (`:1961`) and `AwaitQueryCount` (`:1947`) serve gated reads. `FakeDataProvider` holds
  a read with `GatedLayer`/`HistoryGate` (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:69`, `:77`),
  fails it with `FailHistory` (`:43`), and stamps realtime samples at 2026-01-01 00:00 UTC plus the interval per
  tick (`:24`, `:205`).
- An ungated fake read answers one column at each edge of the expanded range, the last one a window width
  past the visible end (`TrendChartViewModelTests.cs:79`, `:1494`). Live samples land before that column and
  `AppendColumn` rejects them, so every chart regression test below releases explicit envelopes through the gate.
- `Journeys/BreakRenderArchiveJourneyTests.cs` composes `AddPostgresData` with `TrendChartViewModel`, so both
  test projects cover both halves.
- Docs: `docs/architecture/charting.md:82-87` (realtime append and fold); `docs/architecture/data-integration.md`
  "What one history query covers" (`:234` onwards, the right edge "not clamped" at `:239`, the gate at
  `:275-278`), the fresh tail under "Layer ladder" (`:382-390`), the local-clock rule under "Realtime"
  (`data-integration.md#realtime`), "Field triage" (`:787`).

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next; every task carries its tests
- every task ends on `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`, the
  checks each PR's CI runs
- update this plan when scope changes during implementation

## Testing Strategy

- chart tests: `SemiPlot.Tests.Unit`, `[AvaloniaFact]`, in `TrendChartViewModelTests` and `EnvelopeLineTests`,
  reusing their private helpers; every gated read is released with an explicit envelope; private state is
  asserted through behaviour only
- provider tests: `SemiPlot.Tests.Integration` over `PostgresHistoryReadTests`' tail archive, the log through
  `FakeLoggerProvider` and `GetStructuredStateValue` on the template's argument names, never the message text;
  the clock through a private fixed-clock `TimeProvider` subclass; composition in `PostgresCompositionTests`

## Acceptance Evidence

1. History read log: `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresHistoryReadTests.AHistoryReadLogsItsRequestOutcomeAndDuration"`
   asserts one debug entry per read whose structured state carries the layer, the UTC range, the column
   target, the outcome, the count and the elapsed milliseconds.
2. Coarse fold hole, red on `master`, green after Task 2:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewModelTests.AReturnToRawAfterAHeldCoarseReadReadsRaw"`
   asserts that the return issues a Raw read (`LastQueriedLayer == Raw`, query count up by one from just before
   the return).
3. History-apply race, red on `master`, green after Task 3:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewModelTests.AnApplyKeepsTheLiveColumnsPastItsSnapshot"`
   asserts that the last column after the apply is the last live column appended while the read was held.
4. Fresh tail, red on `master`, green after Task 4:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresHistoryReadTests.TheRangeTheChartAsksForReachesTheRawLayersNewest"`
   reads the Minute layer over `[from - w, to + w]` with the clock at the window end and asserts the last
   timestamp is the raw newest (09:04:50 UTC, not 09:04:00).
5. Full suite green: `dotnet test SemiPlot.slnx`.

## Solution Overview

**History read log.** A private `LogHistoryRead(...)` helper writes one debug entry per read: layer, the UTC
range, the column target, the pen count, the outcome (`Read`, `Failed`, `Cancelled`), the envelope count on
success and the elapsed milliseconds from a `Stopwatch` started after the argument checks. `QueryHistoryAsync`
calls it once, from a `finally`: the outcome starts as `Failed`, the success path sets `Read` and the count,
and a `catch (OperationCanceledException)` ahead of the general catch sets `Cancelled` and rethrows, so the
entry does not depend on `Map`'s rethrow. The general catch only maps and returns. Argument rejections are not
logged: they return before any statement and already surface as a failed `Result`. The read body moves into a
private method so `QueryHistoryAsync` falls back under the 50-line cap. Debug level matches the poll's
per-tick entry, so a bench run at `--logging-level Debug` interleaves both.

**Coarse fold.** A fold while the fetched range is Raw invalidates it: in `ApplyRealtimeBatch`,
`if (foldIntoColumn && _lastFetch is { Layer: AggregationLayer.Raw }) { _lastFetch = null; }`. The return to
Raw is then uncovered and reads Raw, and the archive refills the folded span. The `Layer: Raw` guard keeps the
prefetch band at coarse layers: at a coarse layer every live sample folds into a coarse column, which leaves
nothing to refill, so a read there would be for nothing. No new state, and `:894` is unchanged.

A fold that meets a gap column appends instead. A `NaN` last column, which a coarse envelope ending on nulls
leaves (`MinMaxDecimator.AppendGap`), would otherwise reject every live sample and freeze `CurrentValue`
until the next read. `TrendPenState.FoldRealtime(timestampUtc, value)` hands the sample's column to
`FoldIntoLastColumn`, which appends it past a gap column through the same `X` ordering rule as
`AppendColumn`, and folds the later samples into it; the gap still draws as a break. The live edge carries
no null (`RealtimePoll` drops null rows, `PenRealtimeValues.Values` is `double`), so a kept tail never ends
on a gap; a history envelope is the only source of a gap last column.

**Race.** Every history apply keeps the live columns appended since the last replace whose X is after the new
last column and at or before the right edge of the range the read asked for. The rule is the same at every
layer: a kept column after a coarse envelope is a live sample the coarse read did not hold, and a fold at a
coarse layer then folds into it, as it folds into any last column. A sample folded into the last coarse
column after the next coarse read's snapshot leaves with that column; only appended columns survive.

- `EnvelopeLine` keeps the last X the last `ReplaceColumns` wrote (`_historyEndX`, negative infinity after
  an empty replace) under `_renderStateLock`. Every column past it is a live append, so neither trim path
  keeps any bookkeeping.
- `ReplaceColumns(columns, requestedEndX)` keeps the live tail: the new columns, then the appended columns
  whose X is after the new last column (all of them when the new list is empty) and at or before
  `requestedEndX`, then a trim to `MaxColumns` from the oldest end. The selector keeps only live appends, so
  a previous band's history never survives a pan. `TrendChartViewModel.ApplyHistory` passes the request's
  `ToUtc` through `TrendPenState.LoadHistory` and `ClearHistory`.
- `ClearColumns` loses its one caller and is deleted: `TrendPenState.ClearHistory` becomes
  `Line.ReplaceColumns([])`, so a requested pen absent from a result keeps its live tail.
- `TrendPenState.LoadHistory` and `ClearHistory` set `CurrentValue` from the last non-gap center of the merged
  columns, `null` when there is none.
- A pan into the past asks for a range that ends before the follow phase's tail, so the apply drops that tail
  and the band just read keeps the whole buffer, as on `master`. Kept, a tail of up to 100 000 columns would
  push the band out of the buffer at the merge or within the next appends. Samples that arrive after such a
  pan still append far right of the band, because `AppendColumn` accepts any X past the band's end.
- `:798` and `:816` pin the replace that drops a later live column, a case the poll cannot produce (it only
  moves forward). Both are rewritten to the new invariant: the later live column survives the reload, and an
  append at or before it is rejected.

**Fresh tail.** The tail's clamp is taken from the present, not from the requested edge. The coarse branch of
the private `ReadHistoryAsync`, which holds `toLocal` since Task 1, computes
`tailEdgeLocal = min(toLocal, ToArchiveLocal(_timeProvider.GetUtcNow().UtcDateTime))` and passes it to
`FillFreshTailAsync`, which stays static, hands it to `FreshTail.Start` and still reads raw rows up to `toLocal`.
A Raw read never reads the clock. `FreshTail.Start`'s behaviour is unchanged (its edge parameter is renamed
`tailEdgeLocal`), so `FreshTailBoundTests` stands. A window wholly in the past keeps `toLocal`.
The clock is the viewer's own, an exception to the live path's archive-clock rule (`data-integration.md#realtime`)
that holds because SCADA, archive and viewer share one machine `[DEC:machine-time-zone]`. Reading the archive's
`now()` on the open connection would remove even that dependency at the cost of a round trip per coarse read
and a clock no test can set. A skew larger than the coarse layer's own lag skips the tail, which is today's
behaviour; it never draws a wrong row.

## Technical Details

- After the race merge, `AppendColumn` still rejects an X at or before the last column, now the last kept tail
  column.
- `_envelopesById` keeps holding the envelope alone; the live tail was never part of it, so the cursor and
  scale model read what they read today.
- `AddPostgresData` registers `TimeProvider.System` with `TryAddSingleton`, so a test registers its own clock
  first. `ArchiveProviderFactory.Build` takes an optional `TimeProvider` and registers it before
  `AddPostgresData`. `MessagePanelViewModel`, which takes an optional `TimeProvider`, then resolves the same
  `TimeProvider.System` it defaults to today.

## Implementation Steps

### Task 1: Log every history read

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] `LogHistoryRead` helper; `catch (OperationCanceledException)` logs `Cancelled` and rethrows; the general
      catch logs `Failed`; success logs `Read`; the read body moves into a private method
- [x] test `AHistoryReadLogsItsRequestOutcomeAndDuration` over the tail archive with `FakeLoggerProvider`,
      asserting structured state for a Raw and a Minute read
- [x] extend `AReadCancelledMidStatementThrowsInsteadOfFailing` (`:219-259`) with a `FakeLoggerProvider` and one
      assertion on the `Cancelled` outcome
- [x] `data-integration.md`: the entry under "What one history query covers", with a pointer from "Field triage"
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` - must pass before Task 2

### Task 2: Read Raw again after a fold over Raw columns

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/data-integration.md`

- [x] in `ApplyRealtimeBatch`, null `_lastFetch` when the batch folds and the fetched range is Raw
- [x] test `AReturnToRawAfterAHeldCoarseReadReadsRaw`: data extents tracked up to the fake's realtime epoch so
      live appends land past the envelope; one `ZoomAt(0.8, Navigation.To)` puts the width on the zoom ladder;
      a Raw envelope ending at the epoch applied through the gate; `ZoomAt(48.0, Navigation.To)` with the
      Minute read held; realtime runs; `ZoomAt(1.0 / 48.0, Navigation.To)` returns to the same width; the
      return issues a Raw read
- [x] test: the Minute read fails (`FailHistory`), then the return reads Raw
- [x] test: the Minute read lands, then the return reads Raw
- [x] test: a coarse envelope applied at a coarse view keeps folding, the next batch issues no read, and a pan
      inside the band still pushes no read
- [x] `:894` stays green unchanged
- [x] `charting.md:82-87` and `data-integration.md` "What one history query covers": a fold over Raw columns
      opens the gate
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` - must pass before Task 3

### Task 3: Keep the live tail across a history apply

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/data-integration.md`

- [x] `EnvelopeLine`: the last X the last replace wrote, `ReplaceColumns` keeping the tail up to the requested
      edge and trimming after the merge; `ClearColumns` deleted
- [x] `TrendPenState`: `ClearHistory` replaces with no columns; `LoadHistory` and `ClearHistory` set
      `CurrentValue` from the merged columns
- [x] `EnvelopeLineTests`: only appended columns survive a replace (a replaced column right of the new end does
      not); after appends past the cap and a replace, the surviving columns are the newest appended ones past the
      new end; the merge trims to `MaxColumns`
- [x] test `AnApplyKeepsTheLiveColumnsPastItsSnapshot` (the race, through the gate)
- [x] tests: a requested pen absent from the result keeps its tail; `CurrentValue` is the tail's last value; an
      append at or before the kept end is rejected; a coarse envelope keeps the tail and the next fold widens the
      last kept column
- [x] rewrite `:798` and `:816` to the new invariant
- [x] `charting.md:82-87` and `data-integration.md`: an apply keeps the live tail past its snapshot
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` - must pass before Task 4

### Task 4: Clamp the fresh tail at the present

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ArchiveProviderFactory.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] inject `TimeProvider` into `PostgresDataProvider`; `AddPostgresData` registers `TimeProvider.System` with
      `TryAddSingleton`
- [x] `ReadHistoryAsync`'s coarse branch computes `tailEdgeLocal`; `FillFreshTailAsync` takes it for the clamp
      and still reads up to `toLocal`
- [x] `PostgresCompositionTests`: `NewProvider` passes a `TimeProvider`; `typeof(TimeProvider)` joins the
      singleton theory; a `TimeProvider` registered before `AddPostgresData` is the one resolved
- [x] `ArchiveProviderFactory.Build` takes an optional `TimeProvider`
- [x] test `TheRangeTheChartAsksForReachesTheRawLayersNewest`: Minute over `[from - w, to + w]`, clock at the
      window end
- [x] test: the visible tail window under a clock a day later still reaches 09:04:50 UTC (the `toLocal` side of
      the minimum)
- [x] test: a clock behind the fresh pen's seam reads no tail row and no row past the coarse newest
- [x] the existing tail tests stay green with the system clock
- [x] `data-integration.md:239`, `:382-390` and `#realtime`: the tail clamps at the present, why the expanded
      edge cannot serve, and the machine-clock exception
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` - must pass before Task 5

### Task 5: Verify acceptance criteria

- [x] the four acceptance tests green; the fold, race and tail tests each red with their fix reverted
      (green at HEAD: `AHistoryReadLogsItsRequestOutcomeAndDuration`, `AReturnToRawAfterAHeldCoarseReadReadsRaw`,
      `AnApplyKeepsTheLiveColumnsPastItsSnapshot`, `TheRangeTheChartAsksForReachesTheRawLayersNewest`. Reverted
      in the working tree only: fold, `AReturnToRawAfterAHeldCoarseReadReadsRaw` and `...AFailedCoarseRead...`
      red on the query count, `...ALandedCoarseRead...` green; race, `AnApplyKeepsTheLiveColumnsPastItsSnapshot`
      red, last X at the envelope end; tail, `TheRangeTheChartAsksForReachesTheRawLayersNewest` red, last
      timestamp 09:04:00, the other 23 `PostgresHistoryReadTests` green)
- [x] `dotnet test SemiPlot.slnx` (unit 1814 passed, integration 146 passed, 0 failed)
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` (exit 0)

### Task 6: [Final] Update documentation

- [x] `AGENTS.md` only if a new rule appeared (unchanged: no sentence went false, since the `IScheduler`
      registration line is not exhaustive and the new catches rethrow or return a failed `Result`; the fold
      gate, the kept live tail, the read log and the clock clamp are mechanism, held by `charting.md` and
      `data-integration.md`)
- [x] move this plan to `docs/plans/completed/` (left for delivery)

## Post-Completion

**Manual verification** on the demo stand, follow mode, Raw, `--logging-level Debug`:
- zoom out until the detail level changes, wait 40 s, zoom back by the same notches; no straight segment
  near the live edge
- a straight segment that still appears is matched against the history-read entries and the poll's entries
  around its timestamps

**Executed by exec:**
- branch: live-gap-probes

## Verify it yourself

1. History read log. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresHistoryReadTests.AHistoryReadLogsItsRequestOutcomeAndDuration|FullyQualifiedName~PostgresHistoryReadTests.ADroppedTrendsTableFailsNamingTrends|FullyQualifiedName~PostgresHistoryReadTests.AReadCancelledMidStatementThrowsInsteadOfFailing"`
   passes: one Debug entry per read with `Outcome` `Read`, `Failed` or `Cancelled`. On the stand, a run at
   `--logging-level Debug` shows one history-read line per read in `semiplot.log` next to the poll's per-tick
   lines. Before `29b0e7b` no history-read line exists.
2. Coarse fold hole. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewModelTests.AReturnToRaw"`
   passes at HEAD; at `29b0e7b` (before `f3e1077`) the held and failed cases fail on the query count. No
   reliable manual repro on a healthy bench: the hole needs a coarse read that does not apply while zoomed
   out. The smoke check above exercises the path.
3. History-apply race. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewModelTests.AnApplyKeepsTheLiveColumnsPastItsSnapshot|FullyQualifiedName~EnvelopeLineTests"`
   passes at HEAD; before `6809d62` the apply leaves the line ending at the envelope's end. On the bench the
   hole is about one sample and the line hides it, so the test is the evidence.
4. Fresh tail. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresHistoryReadTests.TheRangeTheChartAsksForReachesTheRawLayersNewest"`
   ends at 09:04:50 UTC at HEAD; with the clamp at the requested edge (before `732789b`) the read ends at
   09:04:00. The lost edge is at most one coarse period, so nothing on screen shows it on the bench.
5. Gap column. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AValueAfterACoarseEnvelopeEndingOnAGapTakesTheReading|FullyQualifiedName~AFoldAfterAGap_OpensAColumnPastItAndFoldsIntoThatOne"`
   passes at HEAD; before `1241a42` the reading stays at the value before the gap.
6. Everything: `dotnet test SemiPlot.slnx` (unit 1822, integration 147) and
   `dotnet format SemiPlot.slnx --verify-no-changes`.
