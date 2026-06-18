# Post-Review Fixes: Cleanups, Envelope Hardening, History-Pipeline Consolidation

## Overview

Acts on the findings from the critical review of the `avalonia-scottplot-implement` branch,
limited to the three groups that are confirmed worth doing. Each is independent and can ship
separately:

- **Safe cleanups** — split `RealtimeBatch.cs` to one record per file; unify the
  `ProjectVarId` / `PenId` naming across the public surface.
- **Envelope hardening** — give `PenHistoryEnvelope` a validating constructor that enforces its
  implicit invariants (equal column lengths, strictly ascending timestamps), removing the latent
  index/sort traps in `CursorReadoutModel` and `PenScaleModel`.
- **History-pipeline consolidation** — remove the redundant standalone history path on
  `TrendCoordinator` (`RequestHistory` / `SetLayer` / `HistoryResults`) and drive the initial load
  through the same `QueryHistoryAsync` + `ApplyHistory` + monotonic-sequence path the debouncer
  already uses.

Explicitly **out of scope** (deferred design decisions, not defects): `Pan` past the live edge,
zero-delta pan sticky-detach, and toolbar `ManualMin`/`ManualMax` input validation.

## Context (from discovery)

- Files/components involved:
  - Core: `Trends/RealtimeBatch.cs`, `Trends/Pen.cs`, `Trends/PenHistoryEnvelope.cs`,
    `Trends/CursorReadoutModel.cs`, `Trends/PenScaleModel.cs`
  - UI: `Bridge/TrendCoordinator.cs`, `Chart/TrendChartViewModel.cs`,
    `Chart/ChartHistoryRequestDebouncer.cs` (unchanged, reference), `App.axaml.cs`,
    plus the `ProjectVarId` callers `Chart/ChartCursorReader.cs`, `Chart/ChartHoverReadout.cs`,
    `Legend/TrendLegendRowViewModel.cs`
  - DataSource.Stub: `SyntheticPen.cs`, `RandomStubDataProvider.cs`, `MinMaxDecimator.cs`
  - Tests: `UI/Bridge/TrendCoordinatorTests.cs`, `UI/Bridge/FakeDataProvider.cs`,
    `Core/Trends/PenScaleModelTests.cs`, `Chart/CursorReadoutModelTests.cs`,
    `Chart/DeltaCursorModelTests.cs`, `UI/Chart/TrendChartViewModelTests.cs`,
    `UI/Legend/TrendLegendViewModelTests.cs`, `UI/Chart/ChartAxisRegionEditTests.cs`,
    `UI/Chart/ChartHistoryRequestDebouncerTests.cs`,
    `Core/Data/RandomStubDataProviderTests.cs`, `Core/Data/SyntheticPenCatalogTests.cs`
  - Docs: `docs/architecture/data-integration.md`, `docs/architecture/overview.md`,
    `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`,
    `docs/plans/backlog.md` (closes the frozen-sequence item at `backlog.md:35-38`)
- Related patterns found:
  - The debouncer (`ChartHistoryRequestDebouncer`) already implements latest-wins via `Switch`
    over `NextHistorySequence()` stamps. The coordinator's parallel path has no such guard; it is
    saved today only by being invoked exactly once at startup (`App.axaml.cs:101`).
  - `PenHistoryEnvelope` is four index-aligned `IReadOnlyList`s consumed positionally by binary
    search and by ScottPlot `FillY(xs, mins, maxs)`. The columnar shape is intentional for the
    render path; only the missing validation is the problem.
- Dependencies identified:
  - The naming rename and the contract-table edit both touch `data-integration.md`.
  - Layer selection already travels per-request through the debouncer (`window.Layer` →
    `HistoryRequest.Layer` → `QueryHistoryAsync`), so the coordinator's stateful `_currentLayer`
    is dead once the standalone path is removed.
  - **Test seam (the load-bearing dependency):** `coordinator.RequestHistory(...)` is the
    synchronous history-injection seam used by 12 behavioral tests in `TrendChartViewModelTests.cs`
    (lines 85, 97, 225, 240, 396, 410, 530, 549, 560, 580, 603, 618) and 2 in
    `TrendLegendViewModelTests.cs` (98, 117) — cursor, sticky, drag, hover, and scale assertions,
    NOT pipeline tests. Removing `HistoryResults` invalidates all 14; they must be migrated to a new
    seam (decided below), not deleted. Only the `RequestHistory_*`/`SetLayer_*` tests in
    `TrendCoordinatorTests.cs` are genuinely obsolete.

## Development Approach

- Testing approach: **Regular** (implement, then add/adjust tests in the same task).
- Complete each task fully before the next; keep changes small and focused.
- Every task that changes behavior adds or updates tests for that behavior, success and error
  paths, as separate checklist items.
- Tasks 1 and 2 are pure refactors (file move, rename) with no behavior change: the existing suite
  is the regression guard, so they add no new unit tests — this is deliberate, not an omission.
  They still must leave the full suite green before the next task.
- All tests must pass before starting the next task.
- Run `dotnet format SemiPlot/SemiPlot.slnx` before finishing (pre-commit hook enforces it).
- Update this plan file if scope shifts during implementation.

## Testing Strategy

- Unit tests via `dotnet test SemiPlot/SemiPlot.slnx`. No e2e/UI-driver suite exists; headless
  `[AvaloniaFact]` tests are the closest equivalent and already cover the VM/coordinator seams.
- New coverage lands in Tasks 3 and 4 (envelope validation; the consolidated initial-load path).

## Progress Tracking

- Mark completed items `[x]` immediately.
- New tasks get a ➕ prefix; blockers get a ⚠️ prefix.

## Solution Overview

- **Cleanups** are mechanical and carry no behavior change.
- **Envelope hardening** converts `PenHistoryEnvelope` from a positional record to a record with an
  explicit validating constructor of the *same* parameter order, so all 15 existing
  `new PenHistoryEnvelope(...)` call sites compile unchanged; only invalid inputs now throw.
- **History consolidation** makes the debouncer's `QueryHistoryAsync` → `ApplyHistory` →
  `NextHistorySequence()` triad the single history pipeline. The initial load calls
  `coordinator.QueryHistoryAsync(...)` directly and applies the result through `ApplyHistory` with a
  fresh sequence — immediate (no 150 ms throttle) and ordered by the same monotonic counter as every
  re-query. The coordinator loses its stateful query/publish machinery.

## Technical Details

- `PenHistoryEnvelope` validation rules:
  - `Min.Count == Max.Count == Center.Count == Timestamps.Count` else `ArgumentException`.
  - `Timestamps` strictly ascending else `ArgumentException` (empty/single-element is valid).
  - Null lists rejected via `ArgumentNullException`.
- `TrendCoordinator` after consolidation keeps: `Pens`, `RealtimeBatches`, `Start`,
  `QueryHistoryAsync`, `QueryArchiveExtentAsync`, `Dispose`. It loses: `RequestHistory`, `SetLayer`,
  `HistoryResults`, `_historyResults`, `QueryAndPublishHistoryAsync`, and the cached-request fields
  (`_lastRequestedPenIds`, `_lastFromUtc`, `_lastToUtc`, `_lastTargetColumnCount`,
  `_hasHistoryRequest`, `_currentLayer`).
- `TrendChartViewModel` after consolidation loses `_coordinatorHistorySequence` and the
  `HistoryResults` subscription; `RequestInitialHistory` becomes `async Task`, performs a direct
  `QueryHistoryAsync` and applies via `ApplyHistory(history, NextHistorySequence())`.
- **Failure-path decision:** the VM does NOT gain an `ILogger`. The debouncer path already silently
  drops failed results (`ChartHistoryRequestDebouncer.cs:37` `.Where(IsSuccess)`), so the initial
  load mirrors that — on a failed `Result`, return without applying. This keeps the VM constructor
  and the `Func<TrendCoordinator, IScheduler, TrendChartViewModel>` factory unchanged (no
  `App.axaml.cs` churn) and is consistent across both history entry points. The lost coordinator
  log line is acceptable for the stub; revisit when the real provider surfaces typed errors.
- **Test-seam decision:** `RequestInitialHistory` returning `Task` becomes the synchronous injection
  seam. `FakeDataProvider` returns `Task.FromResult` on `ImmediateScheduler`, so a test sets the
  navigation window (and pens) then `await chartViewModel.RequestInitialHistory()` and the envelopes
  are loaded deterministically — no `TestScheduler`/throttle dance. The 14 migrated tests replace
  `coordinator.RequestHistory([1], from, to)` with: set `Navigation` to the desired window, then
  `await RequestInitialHistory()`. `App.axaml.cs:101` becomes `_ = chartViewModel.RequestInitialHistory();`
  (fire-and-forget, matching the adjacent `_ = minimapViewModel.LoadExtentAsync();`).

## What Goes Where

- Implementation Steps below (`[ ]`): code, tests, and the two doc edits in `data-integration.md`.
- Post-Completion: none requiring external systems; one manual smoke-run of the app is advisory.

## Implementation Steps

### Task 1: Split `RealtimeBatch.cs` into one record per file

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/PenRealtimeValues.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs`

- [x] move `PenRealtimeValues` into its own file with the file-scoped namespace
- [x] leave `RealtimeBatch` as the sole type in `RealtimeBatch.cs`
- [x] build `SemiPlot/SemiPlot.slnx` to confirm both consumers (`TrendCoordinator`, tests) resolve
- [x] run full suite - must pass before next task (no new tests: pure file move)

### Task 2: Unify `ProjectVarId` to `PenId` on `Pen`

Decision: rename `Pen.ProjectVarId` to `PenId` so the entity matches the `PenId` / `penIds` term
already used by `Sample`, `PenHistoryEnvelope`, `PenScaleSettings`, and `IDataProvider`. Keep the
Simple-Scada origin documented in the `data-integration.md` identity note (the value still *is* the
Simple-Scada project-variable id; only the surface name unifies). If review prefers the opposite
direction (standardize on `ProjectVarId` everywhere) this task inverts cleanly — flag at review.

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/Pen.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`,
  `SemiPlot/SemiPlot.UI/Chart/ChartCursorReader.cs`,
  `SemiPlot/SemiPlot.UI/Chart/ChartHoverReadout.cs`,
  `SemiPlot/SemiPlot.UI/Legend/TrendLegendRowViewModel.cs`,
  `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/SyntheticPen.cs`,
  `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`,
  `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`,
  `SemiPlot/SemiPlot.Tests/Core/Data/SyntheticPenCatalogTests.cs`,
  `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] rename the `Pen.ProjectVarId` property to `PenId`
- [x] update all references across UI, DataSource.Stub, and Tests
- [x] update the `data-integration.md` identity bullet and the `Pen` catalog contract row to say
      `PenId` while retaining the "Simple-Scada project-variable id" note
- [x] run full suite - must pass before next task (no new tests: rename only)

### Task 3: Add a validating constructor to `PenHistoryEnvelope`

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenHistoryEnvelope.cs`
- Create: `SemiPlot/SemiPlot.Tests/Core/Trends/PenHistoryEnvelopeTests.cs`

- [x] convert `PenHistoryEnvelope` from a positional record to a record with an explicit
      constructor of identical parameter order (`penId, timestamps, min, max, center`) and `init`
      properties, so all 15 existing construction sites (8 files) are source-compatible
- [x] validate equal counts across `Timestamps`/`Min`/`Max`/`Center` (`ArgumentException`)
- [x] validate strictly ascending `Timestamps` (`ArgumentException`); allow empty/single element
- [x] null-guard the four list parameters (`ArgumentNullException`)
- [x] write tests: valid construction (incl. empty and single-element) succeeds
- [x] write tests: mismatched column lengths throw; non-ascending/duplicate timestamps throw; null
      lists throw
- [x] add/confirm a `MinMaxDecimator` test asserting its emitted envelope passes the new validation
      (the real regression risk lives in the producer, not the synthetic test data)
- [x] run full suite (confirm the 15 existing construction sites still pass) - must pass before next task

### Task 4a: Consolidate the history pipeline (production code)

Split from the test migration (4b) because the test fallout is larger than the production change;
keeping them separate makes the "must pass before next task" gate tractable. 4a and 4b together
must be green before Task 5 — within 4a the suite will be red (expected) until 4b completes, so 4a's
final gate is "production compiles + the unmigrated tests are the only failures."

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`

- [x] in `TrendChartViewModel`, change `RequestInitialHistory` to `async Task`, call
      `_coordinator.QueryHistoryAsync(penIds, From, To, ActiveLayer, columnTarget)` directly and
      apply the success result via `ApplyHistory(new TrendHistory(layer, value), NextHistorySequence())`;
      on a failed `Result` return without applying (mirrors the debouncer's silent drop — see
      Failure-path decision)
- [x] remove the `HistoryResults` subscription and the `_coordinatorHistorySequence` field from the VM
- [x] update `App.axaml.cs:101` to `_ = chartViewModel.RequestInitialHistory();`
- [x] in `TrendCoordinator`, delete `RequestHistory`, `SetLayer`, `HistoryResults`, `_historyResults`,
      `QueryAndPublishHistoryAsync`, and the cached-request fields; keep `QueryHistoryAsync`,
      realtime, extent, `Start`, `Dispose`
- [x] remove the now-stale UI-thread-only request-state comment in `TrendCoordinator`
- [x] build `SemiPlot/SemiPlot.slnx` - production must compile before starting 4b

### Task 4b: Migrate tests and architecture docs to the single pipeline

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `docs/architecture/data-integration.md`, `docs/architecture/overview.md`,
  `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`
- Modify: `docs/plans/backlog.md`

- [x] migrate the 12 `coordinator.RequestHistory(...)` injections in `TrendChartViewModelTests.cs`
      to: set `Navigation` to the test's window, then `await chartViewModel.RequestInitialHistory()`
- [x] migrate the 2 `coordinator.RequestHistory(...)` injections in `TrendLegendViewModelTests.cs`
      the same way
- [x] delete the genuinely obsolete `TrendCoordinatorTests`: `RequestHistory_*`, `SetLayer_*`,
      `RequestHistory_ProviderFailure_*`; keep `QueryHistoryAsync_*`, `Pens_*`, realtime, dispose
- [x] add a `TrendChartViewModelTests` case: `RequestInitialHistory` loads envelopes, and a later
      debounced gesture result with a higher sequence supersedes it (latest-wins across the unified
      `NextHistorySequence()` counter)
- [x] update `data-integration.md` (drop the `RequestHistory(...)`/`SetLayer(layer)` contract rows;
      state history flows through `QueryHistoryAsync` + the debouncer), `overview.md:76`,
      `charting.md:166`, and `trend-interaction.md:104` to remove references to the deleted methods
- [x] mark the frozen-sequence item in `backlog.md:35-38` resolved (now fixed by the unified counter)
- [x] run full suite - must pass before next task

### Task 5: Verify acceptance criteria

- [x] confirm Core no longer declares two records in one file and `Pen` exposes `PenId`
- [x] confirm `PenHistoryEnvelope` rejects malformed input and all consumers still pass
- [x] confirm a single history path remains: `grep -rn "HistoryResults\|\.RequestHistory(\|\.SetLayer("`
      restricted to `SemiPlot/**/*.cs` returns nothing (docs and completed plans are out of scope
      for this gate)
- [x] run full suite: `dotnet test SemiPlot/SemiPlot.slnx`
- [x] run `dotnet format SemiPlot/SemiPlot.slnx`

### Task 6: [Final] Update documentation and close out

- [x] confirm `data-integration.md` reflects the unified naming and the single history path
- [x] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** (advisory):
- Launch the app (`dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`) and confirm the
  initial trend renders immediately (no perceptible startup delay from the removed publish path) and
  that pan/zoom/layer re-queries still update the chart.
