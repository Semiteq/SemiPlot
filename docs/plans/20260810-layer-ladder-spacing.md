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

The setter carries behaviour, not just storage: it clamps to the canvas bounds, returns early when
the value is unchanged, recomputes the active layer, and notifies only when the layer actually
changed. Without the unchanged-value guard every render would re-enter it and every pixel of a resize
would issue a history query.

`TrendChartViewModel` pushes the count from the render seam rather than from the query seam, because
every query path reads the layer before it reaches `CurrentColumnTarget()`, so assigning there would
compute each request's layer from the previous column count.

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

**Startup behaviour is unchanged.** With the default of `MaxColumns`, the raw ceiling is 8.5 h, so the
default one-hour window still starts on `Raw` and the realtime fold decision at
`TrendChartViewModel.cs:531` behaves as before.

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

- [ ] replace `ToSampleInterval` with `ToPointSpacing` returning 1 s, 15 s, 15 min and 6 h, with a
      comment naming the four-points-per-period budget as the source of the quarter
- [ ] update the call site at `RandomStubDataProvider.cs:78` to the new name, noting at the synthesis
      loop (`:108`) that coarse layers now generate four times more points before decimation
- [ ] rename and retarget the three tests in `AggregationLayerTests` to the new values, keeping the
      distinctness and monotonicity assertions
- [ ] confirm no assertion in `RandomStubDataProviderTests` depends on the synthetic step — it asserts
      monotonicity, bounds and a column-count cap at `:84` — and leave the file untouched if so
- [ ] run tests — must pass before task 2

### Task 2: Derive layer ceilings from spacing and column count

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartNavigationControllerTests.cs`

- [ ] add `TargetColumnCount` whose setter clamps to `HistoryColumnTarget.MinColumns…MaxColumns`,
      quantises to the nearest power of two, returns early when unchanged, recomputes `ActiveLayer`,
      and raises `WindowChanged` with `RequiresHistoryRequery: true` only when the layer changed
- [ ] default it to `HistoryColumnTarget.MaxColumns`, matching what the view model already requests
      before the first render
- [ ] replace the three static ceiling fields with a helper returning
      `nextCoarser(layer).ToPointSpacing() * TargetColumnCount`, leaving `LayerForWidth` and
      `BoundaryWithHysteresis` otherwise unchanged
- [ ] re-derive the parameterised expectations in `LayerFollowsZoomWidth` and
      `LayerBoundaryCeilingsAreInclusive` from the rule, setting the column count explicitly in the
      test rather than relying on the default
- [ ] write a test that the same window width selects different layers at 256 and at 2048 columns
- [ ] write a test that a monotonic resize across a boundary produces at most one layer change and at
      most one re-query
- [ ] write a test that the no-flip-flop guarantee still holds across a boundary at a fixed column
      count
- [ ] re-run the startup-layer assertions in `TrendChartViewModelTests` and `TrendToolbarViewModelTests`
      and confirm they still hold unchanged
- [ ] run tests — must pass before task 3

### Task 3: Feed the live column count from the render seam

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`

- [ ] add a view-model method that accepts the current data-area width and assigns the quantised
      column count to the controller
- [ ] call it from the plot size-changed handler (`TrendChartView.axaml.cs:77`) and from the
      redraw-requested path, not from the query paths, which read the layer before they compute the
      column target
- [ ] record in a comment that `Plot.LastRender.DataRect.Width` reports the previous render, so the
      first assignment after a resize lags one frame — accepted, since the following frame corrects it
- [ ] write a headless test that a change in the reported data-area width changes the layer chosen for
      an unchanged window
- [ ] write a test that the pre-render state, where the data rectangle is zero, uses the maximum
      column count and therefore does not starve the initial query of resolution
- [ ] run tests — must pass before task 4

### Task 4: Verify acceptance criteria

- [ ] verify the four cases in Acceptance Evidence produce the required layers
- [ ] verify hysteresis still prevents flip-flop at every boundary, not only the raw one
- [ ] verify no consumer outside this slice changed behaviour: the enum members, their order and
      `IDataProvider` are untouched, and the startup layer is unchanged
- [ ] run the full suite: `dotnet test SemiPlot.slnx` — 250 or more passing, zero failures
- [ ] run `dotnet format SemiPlot.slnx`

### Task 5: Update documentation

- [ ] correct the word "finest" to "coarsest" in the selection rule in
      `docs/architecture/data-integration.md` and in requirement DA-3 of
      `docs/architecture/trend-feature-spec.md`; both state the correct formula but describe it with
      the wrong superlative, which is exactly the inversion this slice had to unpick
- [ ] state in `docs/architecture/data-integration.md` that the ceiling follows the live column count
      rather than a fixed reference, and that the count is quantised to powers of two for layer
      selection only
- [ ] note that the raw layer's spacing does not participate in layer selection, so the archiving
      interval need not be known by the client
- [ ] move this plan to `docs/plans/completed/`

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
