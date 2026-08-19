# Layer ladder spacing

## Overview

The aggregation-layer ladder selects which archive resolution to read for a given time window. Its
arithmetic is wrong: it treats a layer's *period* as the distance between consecutive points, when
the Simple-Scada archive writes up to four points into every period. The real point spacing is a
quarter of the period — 15 s in the minute layer, 15 min in the hour layer, 6 h in the day layer.

Two consequences follow. Every rung is entered at the wrong window width, so the viewer reads finer
data than the canvas can show. And the ladder ignores the canvas width entirely, even though the
number of columns the canvas asks for varies by a factor of eight between a narrow and a maximised
window.

This slice derives every ceiling from the point spacing and the live column count, and keeps the
existing hysteresis. It touches no provider, no database and no new project.

Anchoring thesis: every resolution the trend canvas needs already exists in the vendor's archive, so
the provider only has to choose a layer, reduce it to the canvas width, and reconstruct gaps — it
never has to maintain data of its own.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice layer-ladder-spacing

Files involved, each claim read during planning:

- `SemiPlot/SemiPlot.Core/Trends/AggregationLayer.cs:14-24` — `ToSampleInterval` returns the layer's
  period: 1 s, 1 min, 1 h, 1 d.
- `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:11-13` — ceilings are fixed window widths:
  1 h, 2 d, 60 d. Applied in `LayerForWidth` at `:121-142`, with a 10% hysteresis margin defined at
  `:10` and applied at `:144-152`.
- `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:21-28` — the controller is constructed
  with no arguments and holds no reference to the canvas. `ActiveLayer` is recomputed only at `:56`,
  `:109` and `:117`, all driven by a window change.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:102` — the view model owns the controller
  (`public ChartNavigationController Navigation { get; } = new();`), and hands the same instance to
  `ChartRealtimeApplier` at `:72` and, through the factory at `App.axaml.cs:96`, to
  `MinimapViewModel`.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:442-445` — `CurrentColumnTarget()` derives the
  column count from `Plot.LastRender.DataRect.Width`.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:531` —
  `var foldIntoColumn = Navigation.ActiveLayer != AggregationLayer.Raw;`. The startup layer therefore
  decides whether realtime appends points or folds them into the last column.
- `SemiPlot/SemiPlot.UI/Chart/HistoryColumnTarget.cs` — maps pixel width to 256…2048 columns, one
  column per pixel, and returns `MaxColumns` before the first render when the data rectangle is zero.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:77` — the plot size-changed handler, and the
  `RedrawRequested` subscription at `:105-110`: the seams where the render size is freshest.
- `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:78` — the only production caller of
  `ToSampleInterval`; it uses the value as the synthetic sample step, consumed by the synthesis loop
  at `:108`.
- `SemiPlot/SemiPlot.Tests/Core/Trends/AggregationLayerTests.cs:15-18` — pins 1, 60, 3600 and 86400
  seconds.
- `SemiPlot/SemiPlot.Tests/UI/Chart/ChartNavigationControllerTests.cs:103-106, 119-122, 160-173` —
  pins the layer chosen for given window widths, the inclusivity of each ceiling, and the
  no-flip-flop behaviour across the raw boundary.
- `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs:460` and
  `SemiPlot/SemiPlot.Tests/UI/Toolbar/TrendToolbarViewModelTests.cs:122, 160` — assert the layer of
  the default startup window.

Related patterns: the controller already routes every boundary through one helper
(`BoundaryWithHysteresis`), so replacing constant ceilings with computed ones is a local change.

Dependencies: none. The slice is independent of every other slice in the roadmap.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests within the same task. This
  matches the convention already recorded in the provider plan.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every code task MUST include new/updated tests.** The final verification and
  documentation tasks are explicitly exempt: they add no code.
- **CRITICAL: all tests must pass before starting the next task**
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- maintain backward compatibility of the public enum and of `IDataProvider`

## Testing Strategy

- **unit tests**: required in every code task. The test files listed in Context assert the old
  numbers directly; their failing is the intended signal and their new values are the specification.
- **e2e tests**: the project has no UI-based end-to-end harness. The Avalonia headless tests in
  `SemiPlot.Tests/UI` are the closest equivalent and are covered by the unit requirement above.
- Express the new expectations as the rule, not as magic numbers: a test sets the column count
  explicitly and asserts against `spacing × columns`, so a future spacing change updates one place.

## Acceptance Evidence

The defect is a wrong number, so it is reproducible and measurable by test.

**How it reproduces at HEAD.** `ChartNavigationControllerTests.LayerFollowsZoomWidth` (`:103-106`)
and `LayerBoundaryCeilingsAreInclusive` (`:119-122`) encode ceilings of 1 h, 2 d and 60 d that bear no
relation to how much data a layer actually carries. A three-hour window at 1024 columns is 10.5 s per
column, which the raw layer fills exactly and the minute layer at 15 s spacing cannot; today that
window selects `Minute` and draws roughly 700 points where the canvas has 1024 columns.

**How the fix is measured.** All four cases below must hold afterwards. None of them holds at HEAD,
and the fourth cannot even be expressed at HEAD because the controller has no access to the column
count.

```
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj --filter "FullyQualifiedName~ChartNavigationControllerTests"
```

| Case | At HEAD | Required after |
| --- | --- | --- |
| 3-hour window, 1024 columns | `Minute` | `Raw` |
| 3-day window, 1024 columns | `Hour` | `Minute` |
| 100-day window, 1024 columns | `Day` | `Hour` |
| 4-hour window at 256 versus 2048 columns | `Minute` in both | `Minute` at 256, `Raw` at 2048 |

**Full-suite guard.** `dotnet test SemiPlot.slnx` must end at 250 or more tests passing with
zero failures. The baseline at `bef4823` is 250 passing, verified 2026-08-10.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues or blockers with ⚠️ prefix
- update the plan if implementation deviates from the original scope

## Solution Overview

`AggregationLayer` gains `ToPointSpacing`, returning the distance between consecutive points in that
layer, and `ToSampleInterval` is removed rather than left as a synonym — two names for one concept is
how the present confusion arose.

`ChartNavigationController` gains `TargetColumnCount`. Each ceiling becomes the point spacing of the
**next coarser** layer multiplied by that count, which is the same rule as "use the coarsest layer
whose spacing still fits inside one column", expressed as an upper bound so the existing
`width <= ceiling` shape and its hysteresis helper stay intact.

The write carries behaviour, not just storage: it clamps to the canvas bounds, returns early when
the value is unchanged, recomputes the active layer, and notifies. Without the unchanged-value guard
every render would re-enter it and every pixel of a resize would issue a history query.

⚠️ Revised during review: that write is `SetTargetColumnCount(int)`, a method, not a property setter.
The value does not read back as written (it is clamped and quantised) and the write mutates
`ActiveLayer` and raises `WindowChanged`, which is `SetSticky`'s shape, not a property's.
`TargetColumnCount` stays as a get-only property, and `LayerForWidth` became a static function of
(width, currentLayer, targetColumnCount) so the public query is genuinely a function of its arguments.
The event carries the cause as `NavigationWindow.IsColumnCountChange`, which replaced a re-entrancy
flag in the view model.

⚠️ Revised during review: the setter notifies on every *quantised* count change, not only when the
layer changed. A changed count also invalidates the decimation width the visible data was fetched at,
so a widened canvas would otherwise keep drawing the narrow canvas's resolution until the next
gesture. The quantisation deadband below caps this at one re-query per step, and the history
debouncer collapses a drag's steps into one request.

`TrendChartViewModel` pushes the count from the render seam (`ReportDataAreaWidth`, fed by
`Plot.RenderManager.RenderFinished`) rather than from a query path, because every query path reads the
layer before it derives its column count, so assigning there would compute each request's layer from
the previous column count.

⚠️ Revised during review: a width report that lands while the initial history query is still in flight
must not re-query. Applying the initial result snaps the window onto the archive's last sample, and a
request issued for the un-snapped window carries a higher sequence, so its result would overwrite the
snapped window's data. The view model therefore holds such a report and re-issues it, at a fresh
sequence, for the window as it stands once the snap has happened.

Why not a fixed reference column count: the canvas asks for between 256 and 2048 columns depending on
window size. A fixed reference would pick a layer for a canvas that is not the one being drawn, which
is the same class of error this slice exists to remove.

## Technical Details

Point spacing per layer, from the vendor's budget of four points per period
(`docs/architecture/scada-archive.md`):

| Layer | `l` | Period | Point spacing |
| --- | --- | --- | --- |
| Raw | 0 | — | the archiving interval |
| Minute | 1 | 1 min | 15 s |
| Hour | 2 | 1 h | 15 min |
| Day | 3 | 1 d | 6 h |

**Selection rule.** Use the coarsest layer whose point spacing still fits inside one pixel column:
the largest layer satisfying `width / columns >= spacing`. A layer is therefore entered at a window
width of `spacing × columns` and left when the next coarser layer's spacing fits, which yields the
upper bound the code compares against:

```
ceiling(layer) = nextCoarser(layer).ToPointSpacing() * TargetColumnCount
```

Raw's own spacing never enters the ladder, which is fortunate: the true raw spacing is the SCADA's
per-variable archiving interval, unknowable to the client until the provider exists. It survives only
as the stub's synthesis step.

| Columns | Raw ceiling | Minute ceiling | Hour ceiling |
| --- | --- | --- | --- |
| 256 | 64 min | 2.7 d | 64 d |
| 1024 | 4.3 h | 10.7 d | 256 d |
| 2048 | 8.5 h | 21.3 d | 512 d |

These reproduce the lower bounds tabulated in `docs/architecture/data-integration.md`: a layer's
ceiling is the next layer's entry width.

**Column count quantisation.** `HistoryColumnTarget` maps one pixel to one column, so dragging a
window edge emits a distinct count on every pixel and moves every ceiling with it — far outside the
10% hysteresis band, which only guards a boundary at a fixed count. The ladder therefore quantises
its column count to the nearest power of two in 256…2048, giving at most four discrete steps across a
full-width drag. The history query keeps the unquantised count: it decides resolution, not layer.

⚠️ Added during review: the quantisation carries a 10% deadband. The boundary between two counts is a
factor-of-two step, so one pixel of jitter across 724/725 px would otherwise double or halve every
ceiling — a 100% change that the 10% width hysteresis cannot damp.

**Startup behaviour is unchanged.** With the default of `MaxColumns`, the raw ceiling is 8.5 h, so the
default one-hour window still starts on `Raw` and the realtime fold decision at
`TrendChartViewModel.cs:584` behaves as before.

The stub provider's synthetic step follows the same spacing, so a minute-layer query synthesises a
point every 15 s instead of every minute — four times more points per query on every coarse layer,
which the decimator then folds to the requested column count.

## What Goes Where

- **Implementation Steps** (`[ ]`): code, tests and documentation inside this repository.
- **Post-Completion** (no checkboxes): the remaining roadmap slices and anything needing external
  action.

## Implementation Steps

### Task 1: Replace layer period with point spacing

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/AggregationLayer.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Trends/AggregationLayerTests.cs`

- [x] replace `ToSampleInterval` with `ToPointSpacing` returning 1 s, 15 s, 15 min and 6 h, with a
      comment naming the four-points-per-period budget as the source of the quarter
- [x] update the call site at `RandomStubDataProvider.cs:78` to the new name, noting at the synthesis
      loop (`:108`) that coarse layers now generate four times more points before decimation
- [x] rename and retarget the three tests in `AggregationLayerTests` to the new values, keeping the
      distinctness and monotonicity assertions
- [x] confirm no assertion in `RandomStubDataProviderTests` depends on the synthetic step — it asserts
      monotonicity, bounds and a column-count cap at `:84` — and leave the file untouched if so
- [x] run tests — must pass before task 2

### Task 2: Derive layer ceilings from spacing and column count

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartNavigationControllerTests.cs`

- [x] add `TargetColumnCount` whose setter clamps to `HistoryColumnTarget.MinColumns…MaxColumns`,
      quantises to the nearest power of two, returns early when unchanged, recomputes `ActiveLayer`,
      and raises `WindowChanged` with `RequiresHistoryRequery: true` only when the layer changed
      ⚠️ revised during review: it raises on every quantised count change, and the quantisation gained
      a 10% deadband — see the two notes in Solution Overview and Technical Details.
- [x] default it to `HistoryColumnTarget.MaxColumns`, matching what the view model already requests
      before the first render
- [x] replace the three static ceiling fields with a helper returning
      `nextCoarser(layer).ToPointSpacing() * TargetColumnCount`, leaving `LayerForWidth` and
      `BoundaryWithHysteresis` otherwise unchanged
- [x] re-derive the parameterised expectations in `LayerFollowsZoomWidth` and
      `LayerBoundaryCeilingsAreInclusive` from the rule, setting the column count explicitly in the
      test rather than relying on the default
      ⚠️ `ZoomAt` snaps width onto a 1.25^n ladder, so an exact ceiling width is unreachable through it.
      `LayerForWidth` was widened from private to public (a pure query, no state mutation) and the
      inclusivity test now feeds the ceiling straight into it; `LayerFollowsZoomWidth` keeps zooming but
      targets half a ceiling, clear of both the 25% quantisation step and the 10% hysteresis band.
- [x] write a test that the same window width selects different layers at 256 and at 2048 columns
- [x] write a test that a monotonic resize across a boundary produces at most one layer change and at
      most one re-query
- [x] write a test that the no-flip-flop guarantee still holds across a boundary at a fixed column
      count
      ⚠️ the old zoom-nudge form was vacuous: a ±2% nudge quantises back onto the same ladder rung, so
      the layer could not have changed either way. Replaced with `HysteresisHoldsTheCurrentLayerJustPastItsCeiling`,
      which asserts at all three boundaries that `ceiling × 1.05` keeps the current layer and
      `ceiling × 1.15` leaves it.
- [x] re-run the startup-layer assertions in `TrendChartViewModelTests` and `TrendToolbarViewModelTests`
      and confirm they still hold unchanged
- [x] run tests — must pass before task 3

### Task 3: Feed the live column count from the render seam

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`

- [x] add a view-model method that accepts the current data-area width and assigns the quantised
      column count to the controller
      ⚠️ `TrendChartViewModel.ReportDataAreaWidth` returns silently when the view model is disposed
      instead of throwing `ObjectDisposedException` like the other public mutators: an Avalonia layout
      pass can still deliver a size change after the window closed, and a throw there would crash.
- [x] call it from the plot size-changed handler (`TrendChartView.axaml.cs:77`) and from the
      redraw-requested path, not from the query paths, which read the layer before they compute the
      column target
      ⚠️ revised during review: both of those read `Plot.LastRender`, which describes the frame already
      on screen, so neither can report a resize reliably. The call now hangs off
      `Plot.RenderManager.RenderFinished`, which hands over the `DataRect` of the frame just
      rasterised. It fires on Avalonia's render thread, so the report is posted to the UI thread, and
      only a changed width is posted.
- [x] record in a comment that `Plot.LastRender.DataRect.Width` reports the previous render, so the
      first assignment after a resize lags one frame — accepted, since the following frame corrects it
      ⚠️ revised during review: nothing produced that following frame, and `Refresh()` only invalidates
      the visual, so no delay after it guarantees a rasterised frame. The width report no longer reads
      `LastRender`. A non-positive width (collapsed pane, hidden tab) is ignored instead of being read as the
      widest canvas, and the last reported width — unquantised — is what every history query is
      decimated to.
- [x] write a headless test that a change in the reported data-area width changes the layer chosen for
      an unchanged window
- [x] write a test that the pre-render state, where the data rectangle is zero, uses the maximum
      column count and therefore does not starve the initial query of resolution
- [x] ➕ cover the render seam itself, not only `ReportDataAreaWidth`: every other test drives the view
      model directly, which the earlier `Plot.LastRender` version of the seam also passed. New
      `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewTests.cs` binds the real view to a view model and
      calls `Plot.RenderInMemory`, which runs the ScottPlot pipeline onto an in-memory Skia surface and
      raises `RenderFinished` exactly as the running application does. Verified to fail when the
      `RenderFinished` subscription is removed. The view model there needs a virtual UI scheduler:
      subscribing to `RedrawRequested` samples on the UI scheduler, and `ImmediateScheduler` runs a
      periodic schedule by sleeping on the calling thread, so the subscription never returns.
- [x] ⚠️ deferred: `TrendChartViewModel` grew to ~596 lines against the 300-line preference, and this
      slice added a fourth responsibility (canvas-width tracking plus the startup request gate) on top of
      the existing sequence counters. No extraction was attempted here — it is a public-surface refactor
      unrelated to the ladder arithmetic, and the backlog already carries the `ChartInteractionViewModel`
      item, now extended with the history-lifecycle cluster as the better first cut.
- [x] ➕ hold a width report that arrives while the initial history query is in flight, and re-issue it
      after the extents snap — see the note in Solution Overview. Covered by
      `WidthReportedBeforeTheInitialHistoryLands_ReQueriesTheSnappedWindow` and
      `WidthReportedWhileAnInitialHistoryThatFailsIsInFlight_StillReQueriesOnce`.
- [x] run tests — must pass before task 4

### ➕ Task 3b: Seed the decimator envelope from the column count

Discovered while implementing task 1: coarse layers now synthesise four times more samples per query,
so seeding the envelope lists from the sample count retains a pre-decimation-sized array behind a few
thousand useful entries for the life of the pen's chart state.

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/MinMaxDecimator.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs`

- [x] seed `EnvelopeBuilder` from `min(sampleCount, targetColumnCount * 2 + 4)` — a capacity, not a
      limit, so a heavily gapped window still grows the lists on its own
- [x] test that the envelope's backing lists keep a capacity bounded by the column count, not by the
      sample count (`Decimate_DoesNotRetainBackingArraysSizedToTheInput`, 123 000 samples into 2048
      columns)

### Task 4: Verify acceptance criteria

- [x] verify the four cases in Acceptance Evidence produce the required layers
      ➕ no test asserted the table at its literal widths — the layer cases were only covered at
      fractions of a ceiling. Added `AcceptanceWindowWidths_SelectTheRequiredLayer`, a five-row theory
      pinning 3 h/1024→Raw, 3 d/1024→Minute, 100 d/1024→Hour and 4 h at 256→Minute versus
      2048→Raw. Every width sits clear of the 10% band, so the answer does not depend on the arrival
      path. `git show master:...ChartNavigationController.cs` confirms the pre-branch ceilings were
      1 h, 2 d and 60 d with no column count, so four of the five rows returned a coarser layer and the
      2048 row could not be expressed at all.
- [x] verify hysteresis still prevents flip-flop at every boundary, not only the raw one
      `HysteresisHoldsTheCurrentLayerJustPastItsCeiling` asserts hold-at-1.05 and release-at-1.15 at
      all three boundaries; `MonotonicResizeAcrossABoundary_ChangesTheLayerAtMostOnce` covers the
      resize axis in both directions. Both pass.
- [x] verify no consumer outside this slice changed behaviour: the enum members, their order and
      `IDataProvider` are untouched, and the startup layer is unchanged
      `git diff master...HEAD` shows the enum body untouched (only the extension method changed) and
      an empty diff for `IDataProvider`. The startup assertions in `TrendChartViewModelTests:460` and
      `TrendToolbarViewModelTests:122, 160` sit in files the branch never modified and still pass: the
      default one-hour window is inside the 8.5 h raw ceiling at the default 2048 columns.
      ➕ the ladder now reads the next coarser layer as `layer + 1`, so the enum ordinals became
      load-bearing with no test guarding them. Added `LayerCodes_KeepTheirOrdinalContract`.
- [x] run the full suite: `dotnet test SemiPlot.slnx` — 250 or more passing, zero failures
      268 passing, 0 failing, 0 skipped.
- [x] run `dotnet format SemiPlot.slnx` — clean, no files rewritten

### Task 5: Update documentation

- [x] correct the word "finest" to "coarsest" in the selection rule in
      `docs/architecture/data-integration.md` and in requirement DA-3 of
      `docs/architecture/trend-feature-spec.md`; both state the correct formula but describe it with
      the wrong superlative, which is exactly the inversion this slice had to unpick
      ➕ already corrected before this branch — both documents read "coarsest" at HEAD, so nothing to
      change. What was still wrong is recorded in the remaining items.
- [x] state in `docs/architecture/data-integration.md` that the ceiling follows the live column count
      rather than a fixed reference, and that the count is quantised to powers of two for layer
      selection only
      ➕ also removed the stale sentence claiming `AggregationLayerExtensions.ToSampleInterval`
      returns the period and must be changed — that method no longer exists. The ceiling formula is
      now quoted from the code, and the 1000-column threshold table was replaced by a 256/1024/2048
      table labelled an example rather than a constant.
- [x] note that the raw layer's spacing does not participate in layer selection, so the archiving
      interval need not be known by the client
      ➕ stated in both documents: the ladder compares only the next coarser layer's spacing.
- [x] decide where this plan lives until delivery: it stays at `docs/plans/` and is NOT moved to
      `docs/plans/completed/` in this branch. Archiving is delivery work that runs after the operator
      tests the branch, and the review and stats phases read this file at its current path.

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification:**

Open the application against the stub, widen and narrow the window at a fixed time span, and confirm
the toolbar's layer readout changes with the canvas width, changes at most a few times across a full
drag, and does not oscillate while a window edge is held.

**Remaining slices** of docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:

- archive-populator — local test bench: verified archive DDL plus a deterministic populator script
  outside the solution
- postgres-provider-scaffold — provider project, Npgsql, DI registration, YAML connection loader,
  time boundary converter
- postgres-catalog-and-extent — semiplot_tags catalogue, pen loading, archive extent, the gated
  integration test pattern
- postgres-history-read — the single SQL-owning class, direct layer read, envelope assembly, text
  pinning, EXPLAIN assertion
- postgres-bucketed-read — server-side pixel-bucket reduction and the choice between the two read
  paths
- postgres-gap-reconstruction — breaks from quality marks on both read paths, distinguished from
  unchanged values
- postgres-realtime-poll — cold observable polling the raw layer with the mandatory variable list and
  the monotonic seam
- postgres-startup-and-composition — startup schema probe, distinct failure states, provider
  selection by configuration

**The visual check belongs to `live-demo-and-stub-retirement`, not here.** Against
`RandomStubDataProvider` a wrong layer choice has no observable consequence: the stub calls
`layer.ToPointSpacing()` (`SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:82`) — the very
method this slice changes — synthesises points at whatever spacing it is handed, and then decimates to
the canvas column count. The rendered curve is identical either way, so maximising and restoring the
window cannot distinguish a correct ladder from a broken one. On a real archive the difference is
large: too fine a layer reads an order of magnitude more rows, too coarse loses detail. The evidence
for this slice is therefore its unit tests over the thresholds, the hysteresis and the fresh-tail
patch; the eyes-on confirmation runs in the slice that first draws real data.

**Executed by exec:**
- branch: layer-ladder-spacing

## Verify it yourself

The defect was arithmetic, so most of it is provable by test. One behaviour is not, and needs eyes.

**1. The ladder now enters each rung at the right width.** Three windows that chose the wrong layer
before this branch:

```
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~AcceptanceWindowWidths_SelectTheRequiredLayer"
```

At 1024 columns a 3-hour window resolves to `Raw` (it chose `Minute` before), a 3-day window to
`Minute` (it chose `Hour`), and a 100-day window to `Hour` (it chose `Day`). Against the pre-branch
code the same theory fails on all three rows, because the ceilings were the fixed 1 h, 2 d and 60 d
that ignored how much data a layer carries. `git show master:SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
shows those literals and the absence of any column count.

**2. The layer now follows the canvas, which it could not do at all before.**

```
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~SameWindowWidth"
```

One 4-hour window resolves to `Minute` on a 256-column canvas and to `Raw` on a 2048-column one.
This case could not even be expressed before the branch — the controller had no access to the
canvas width.

**3. The render seam is live, not merely present.**

```
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TrendChartViewTests"
```

These drive the real ScottPlot pipeline through `RenderInMemory` and fail if the `RenderFinished`
subscription is removed. That distinction matters here: two earlier versions of this seam were wrong
and still passed every other test in the suite.

**4. Manual, and the only end-to-end proof.** Run the application against the stub, pick a fixed time
span, then maximise and restore the window. The toolbar's layer readout must follow the canvas width
and settle — at most a few changes across a full drag, none while an edge is held. This is the one
check no test covers: `RenderInMemory` proves the subscription and the marshalling, not that the
on-screen draw path delivers the real data rectangle.

Full suite: `dotnet test SemiPlot.slnx` — 279 passing, zero failing, against a 250 baseline at
`bef4823`.
