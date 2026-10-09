# Chart render headroom

## Overview

A drag on the demo stand repaints the chart at about 30 frames per second and late, the hover crosshair
re-rasterizes the whole plot on every pointer move, and every hidden pen axis regenerates its ticks twice per
frame. No thread is saturated, so the operator feels latency and cadence rather than load, and the costs that
scale with the pen count leave little headroom for the 50-pen catalogue. This plan removes the self-imposed
delays and the wasted work:

- the chart redraws once per display frame, paced by the window's animation frame instead of a 33 ms timer;
- the plot visual is bitmap-cached by the compositor, so the overlay above it repaints without re-rendering
  the plot;
- a hidden Y axis generates no ticks, and a drawn axis reuses its ticks while its range and length stand;
- a paced history request leaves the debouncer's sample gate before the query starts;
- the chart reports its data-area width only when it changes.

Measured 2026-10-08 on `44bba75`, Release, 8 pens, Raw layer, 20 s trace (the first ~10 s warm-up):

- `RenderOnce` after warm-up: 338 frames, mean 2.39 ms, p90 3.97 ms, max 7.4 ms;
- inside a drag: plot frame spacing p50 33.9 ms, p90 51.1 ms, max 82.7 ms; pan step to the next `RenderOnce`
  start p50 20.3 ms, p90 42.3 ms, max 50.7 ms; the compositor itself runs 60.8 frames per second;
- while hovering: 52 plot renders per second (278 in 5.3 s) against 33 inside a drag;
- about 18 `NumericAutomatic.GenerateTicks` runs per frame (eight pen axes and the default right axis, twice
  each) with one Y axis drawn; `GenerateTicks` 0.35 ms and `LabelStyle.Measure` 0.72 ms per frame;
- one UI-thread stall of 10.15 ms: `OnNavigationWindowChanged` -> `Sample.OnNext` -> `Monitor.Enter_Slowpath`.

## Context (from discovery)

- `ChartRedrawSchedule` (`SemiPlot/SemiPlot.UI/Chart/ChartRedrawSchedule.cs`, 48 lines) schedules one emission
  33 ms ahead (`_redrawDelay`, `:12`) when none is scheduled and a view subscribes (`:26-35`); `Dispose` cancels
  it. `TrendChartViewModel` owns it (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:29`, `:60`, `:114`).
- The production UI scheduler is `AvaloniaScheduler` (ReactiveUI.Avalonia 12.0.3): a zero-delay schedule runs
  inline on the UI thread, a non-zero one goes through `DispatcherTimer.RunOnce`, which `Avalonia.Win32` 12.0.5
  backs with `SetTimer` on the system clock tick (15.625 ms unless raised); its `Now` is the wall clock
  (`LocalScheduler`). These facts come from the decompiled packages in the local NuGet cache.
- `TrendChartView` subscribes to `RedrawRequested` and calls `PlotControl.Refresh()` then
  `RepositionCursorOverlay()` (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:184-191`); `RepaintChart`,
  `PlaceDeltaCursor` and `BeginPan` call `Refresh()` as well. ScottPlot.Avalonia 5.1.59's `AvaPlot.Refresh()` is
  `Dispatcher.UIThread.InvokeAsync(InvalidateVisual, DispatcherPriority.Background)` (decompiled).
- The view constructor configures `PlotControl` (`TrendChartView.axaml.cs:45-63`); no test pins these settings
  today. `ReportDataAreaWidthTo` posts the data-area width to the UI thread on every `RenderFinished`
  (`TrendChartView.axaml.cs:122-129`).
- ScottPlot 5.1.59 regenerates every Y axis twice per frame at two lengths: `Layouts.Automatic.GetLayout` calls
  `RegenerateTicks(figureRect.Height)` for every panel, then `RenderActions.RegenerateTicks` calls
  `RegenerateTicks(rp.DataRect.Height)` for every plottable axis; neither checks `IsVisible`, while
  `YAxisBase.Measure` and `Render` return early for a hidden axis (decompiled).
- `ChartAxisBinder.Apply` (`SemiPlot/SemiPlot.UI/Chart/ChartAxisBinder.cs:32-75`) sets limits on every pen's
  axis on every window change, swaps the tick generator between `LogTickGenerator` and `NumericAutomatic`
  under `Plot.Sync` (`:56-65`), and sets `axis.IsVisible` for the active visible pen only (`:68`); `HideAxis`
  hides a removed pen's axis (`:24-30`). A new axis comes from `Axes.Left` or `AddLeftAxis()` (`CreateAxis`).
- An axis's log type lives in its generator: `LogTickGenerator.IsLogarithmic(axis)`
  (`SemiPlot/SemiPlot.UI/Chart/LogTickGenerator.cs:50-53`). `LogTickGenerator.Regenerate` keeps two cached
  results, `_recent` and `_older`, keyed on range, length and mask (`:16-17`, `:27-47`), and runs on the render
  thread under `Plot.Sync`.
- `PenScaleModel.Compute` scales an Auto pen from the window in view, so an Auto axis gets a new range on every
  pan step; a Manual axis keeps its range.
- `ChartHistoryRequestDebouncer` merges a `Throttle` head and a `Sample` head into one `Admit` subscriber
  (`SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs:52-61`); admitted queries start through
  `Observable.FromAsync` (`:63-73`). Rx 6.1 forwards both heads inside their gates.
- Tests pinning the redraw rule: `TrendChartViewModelTests` `_redrawDelay` 33 ms (`:39`), `AnIdleChartSchedulesNoRedraw`,
  `AChartNoViewSubscribesToSchedulesNoRedraw`, `DisposingTheChartCancelsTheScheduledRedraw`,
  `ABurstOfRequestsRedrawsOncePerSpanAndOnceAfterTheLastRequest` (`:350-413`), the redraw count at `:1959-1970`,
  and `ChartPointerInputTests`, which runs the production `AvaloniaScheduler`.
- Docs and specs stating the 33 ms / 30 FPS rule: `AGENTS.md:141`, `docs/architecture/charting.md:118-127`,
  `docs/architecture/trend-interaction.md:132` and `:323` (§RT-4 "30 FPS pan/zoom lock"),
  `docs/architecture/trend-feature-spec.md:220`, `docs/architecture/testing-strategy.md:199-210`,
  `docs/architecture/bench.md:364-368`. `testing-strategy.md:185-189` records that hidden axes cost nothing, a
  measurement taken on axes whose range was never set.
- `scripts/perf/trace-shares.py` prints call count, total, mean and max for six frames (`:23`, `:109-124`).
- The before trace is copied into the repository as `SemiPlot/Artifacts/perf/44bba75-drag.speedscope.json`
  (ignored by git with the rest of `Artifacts/`); its source is this session's scratchpad
  `C:\Users\admin\AppData\Local\Temp\claude\C--Users-admin-projects-SemiPlot\0a3f5e31-9cf1-4552-a073-b35ee9d955e0\scratchpad\perf\master.speedscope.json`.

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next; every task carries its tests
- every task ends on `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`
- update this plan when scope changes during implementation

## Testing Strategy

- unit tests in `SemiPlot.Tests.Unit`: the view model's redraw signal, the view's frame request through the
  headless render timer, the tick generators directly, the debouncer over `HistoryDebouncerTestBuilder`
- the compositor path (bitmap cache, frame cadence) cannot be observed headless; it is measured with
  `dotnet-trace` on the demo stand through `trace-shares.py`

## Acceptance Evidence

Automatable, each red before its task and green after:

1. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewTests.ABurstOfRedrawRequests_JoinsOneFrame"`:
   the requests before one frame join it. It pins the coalescing only; that the served frame invalidates the plot
   has no headless observable (`testing-strategy.md#frames-in-a-realised-view`).
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewTests.ThePlotIsCachedThroughItsOnlyAncestor"`:
   the cache sits on the `PlotLayer` decorator, which holds `PlotControl` alone, and `PlotControl` holds none
   (a cache on `AvaPlot` itself froze the plot on the stand, `charting.md#hover-and-the-plot-cache`).
3. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartTickGeneratorTests"`:
   a hidden axis generates nothing; a drawn axis regenerated at lengths A, B, A, B over one range generates twice,
   not four times.
4. `dotnet test SemiPlot.slnx` green.

Manual, on the demo stand, Release, `dotnet-trace collect -p <pid> --format Speedscope --duration 00:00:00:45`
started after warm-up, then `python scripts/perf/trace-shares.py <file>`; the before numbers come from the same
script over `SemiPlot/Artifacts/perf/44bba75-drag.speedscope.json`:

1. Drag for 45 s: drag-phase `RenderOnce` spacing p50 within 2 ms of the compositor frame interval the script
   reports (16.7 ms at 60 Hz), from 33.4 ms; pan step to next `RenderOnce` p50 under 12 ms, from 24.4 ms.
2. Follow for 20 s with the writer running and no input: `RenderOnce` at least once per writer tick. Hover
   without dragging over a still plot, follow off, for 20 s: `RenderOnce` about once per writer tick, from ~52
   per second; it counts only together with the follow check. Measured 2026-10-09: follow 13 sampled runs in
   20 s at a 1.5 s median spacing (0 with the cache on `AvaPlot`), hover 57 runs in 20 s with a 1000 ms median
   spacing during pointer moves.
3. Drag: `NumericAutomatic.GenerateTicks` 0.12 ms or less per `RenderOnce`, from 0.351 ms. ScottPlot calls it
   about 18 times per frame today (eight pen axes and the default right axis, twice each). The code leaves 4
   calls per drag frame: the drawn Auto pen axis misses its cache at both lengths, because each pan step moves
   its range, and the default right axis keeps an uncached `NumericAutomatic`, twice each; 4/18 of the before
   figure is 0.078 ms. The export is sampled, so the check reads the time, not a call count, and 0.12 ms
   (6/18) leaves a margin for sampling noise; one hidden axis still generating would add 2 calls per frame.
4. No `Monitor.Enter_Slowpath` under `TrendChartViewModel.OnNavigationWindowChanged`.

Before, `python scripts/perf/trace-shares.py SemiPlot/Artifacts/perf/44bba75-drag.speedscope.json --skip-seconds 10`:

| Output | p50 | p90 | max | n |
| --- | --- | --- | --- | --- |
| `RenderOnce` spacing, all, ms | 18.2 | 50.5 | 168.7 | 336 |
| `RenderOnce` spacing, drag phase, ms | 33.4 | 50.6 | 168.7 | 89 |
| compositor frame interval, ms | 16.8 | 32.8 | 116.4 | 505 |
| pan step to next `RenderOnce`, ms | 24.4 | 39.4 | 45.1 | 19 |

- `RenderOnce`: 337 runs, mean 2.4 ms, max 7.4 ms; compositor 59.4 frames/s at the median interval.
- `NumericAutomatic.GenerateTicks`: 66 runs, 0.351 ms per `RenderOnce`; `LabelStyle.Measure` 0.720 ms per
  `RenderOnce`, 0.281 ms of it under `NumericAutomatic.GenerateTicks`.
- `Monitor.Enter_Slowpath` under `TrendChartViewModel.OnNavigationWindowChanged`: 9 runs, max 2.25 ms; the
  10.15 ms stall falls in the skipped first 10 s (19 runs over the whole export).

## Solution Overview

**Frame-paced redraw.** The view model's `RedrawRequested` becomes a plain signal: `RequestRedraw` raises it at
once when a view subscribes and does nothing otherwise, and `Dispose` completes it. `ChartRedrawSchedule` and its
33 ms timer are deleted. The view coalesces: when no frame is pending, the handler calls
`TopLevel.RequestAnimationFrame` and sets the pending flag only after that call succeeded; a view with no top
level requests nothing and leaves the flag clear. The frame callback clears the flag and, when the view still has
a view model and is attached, calls `PlotControl.InvalidateVisual()` and `RepositionCursorOverlay()` and counts
the frame in an internal counter the tests read. `OnLoaded` requests a frame, so a view attached after requests it
could not serve redraws once; `OnDataContextChanged` and `OnUnloaded` clear the pending state, so a callback that
outlives its view or its plot does nothing. Every other `Refresh()` call in the view (`RepaintChart`,
`PlaceDeltaCursor`, `BeginPan`) goes through the same frame request.

Avalonia 12.0.5 runs the callback on the UI thread inside the next render pulse (`MediaContext` -> `Clock.Pulse`,
posted at `DispatcherPriority.Render`), in the same dispatcher job that records the visuals and commits the
batch, so the invalidation lands in that frame. Under Win32 the pulse follows the compositor's commits, one per
display frame; a request made inside the callback lands on the next pulse. The redraw then follows the display:
the first request of a burst draws on the next frame, every request until then joins it, and there is no timer,
no timer resolution and no clock arithmetic. `RequestAnimationFrame` verifies the UI thread, so
`RequestRedraw` is UI-thread only, enforced by Avalonia; every caller is on the UI thread today and requests
last in its method. The handler does nothing that can throw beyond requesting a frame, so the synchronous raise
inside `ApplyHistory`, `ApplyCatalogue` or `OnNavigationWindowChanged` re-enters nothing. §RT-4 changes from a
30 FPS lock to one redraw per display frame. At 60 Hz and ~2.4 ms per `RenderOnce` the render thread goes from
~22% to ~30% busy at 8 pens.

**Bitmap-cached plot.** `PlotControl.CacheMode = new BitmapCache()` in the view constructor. Avalonia 12's
compositor keeps the plot's raster in a layer whose dirty rects are its own, and blits it while only the overlay
(crosshair, readout) is dirty; `InvalidateVisual` dirties the cache and re-renders it once. The layer follows the
window scaling and the plot's pixel size, so resize and DPI changes need no code; the headless platform returns a
stub layer, so headless tests are unaffected. This is the layered design the overlay was written for: without
the cache the overlay's dirty rect lies inside the plot and the compositor replays ScottPlot's draw operation in
full. Cost: one chart-sized texture per window, about 8 MB at 1920x1080 and 100% scaling, about 19 MB at 150%.
The layer's text is grayscale-antialiased (`EnableClearType` false).

**Ticks for drawn axes only.** A new `LinearTickGenerator` wraps ScottPlot's `NumericAutomatic` and keeps two
cached results keyed on range, length and label font size, as `LogTickGenerator` does, because ScottPlot asks for
each axis at two lengths per frame. Both generators carry `IsDrawn`; a generator whose axis is not drawn returns
without generating and leaves `Ticks` empty. The generator type keeps carrying the log flag, so a hidden log pen
still projects through its axis. `ChartAxisBinder` gives every axis it creates or switches to linear a
`LinearTickGenerator` and writes `IsDrawn` beside every `axis.IsVisible` write in an order that never shows an
axis without ticks: `IsDrawn = true` before `IsVisible = true`, `IsVisible = false` before `IsDrawn = false`.
`IsDrawn` is written on the UI thread and read on the render thread; a bool write is atomic. The saving is the
hidden axes; a drawn Auto axis still regenerates on every pan step because its range moves, a Manual one hits.

**Sample gate.** The merged admission stream of `ChartHistoryRequestDebouncer` passes through
`ObserveOn(dataScheduler)` before `Admit`, so a paced emission leaves Rx's `Sample` gate before the admitted query
starts and a UI-thread `Request` never waits on it. The read stamping is taken in `Request` and is unaffected.

**Width on change.** `ReportDataAreaWidthTo` posts only when the width differs from the last one it posted.

**Not in scope.** `Win32PlatformOptions.CompositionMode = LowLatencyDxgiSwapChain`: it needs D3D feature level
11_3, falls through silently when unsupported, and buys about one compositor hop that nothing measured asks for.
Revisit with a PresentMon number and an operator latency complaint.

## Technical Details

- `trace-shares.py` gains: `RenderOnce` start-to-start spacing (p50, p90, max); the compositor frame interval
  (`ServerCompositor.RenderCore` spacing p50); pan step to next `RenderOnce` (an `OnPointerMoved` run containing
  `TrendChartViewModel.OnNavigationWindowChanged`, to the next `RenderOnce` start on the render thread, p50 and
  p90); `NumericAutomatic.GenerateTicks` runs and ms per `RenderOnce`; `LabelStyle.Measure` attributed by
  parent frame; and whether `Monitor.Enter_Slowpath` occurs under `TrendChartViewModel.OnNavigationWindowChanged`.
  A `--skip-seconds` option drops the warm-up. The export is sampled (about one stack per 1-2 ms), so a run
  count of a sub-millisecond frame is a lower bound and its milliseconds are the comparable figure.
- `LinearTickGenerator` cache key: `range.Min`, `range.Max`, `size.Length`, `labelStyle.FontSize`; the edge is
  fixed per axis. On a miss it calls `NumericAutomatic.Regenerate` and keeps its `Ticks`.
- Headless frames: `AvaloniaHeadlessPlatform.ForceRenderTimerTick()` ticks the server compositor and completes the
  committed batch; the animation-frame callback itself runs on the `Render` dispatcher operation that follows, so
  a test drives one frame as `Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
  Dispatcher.UIThread.RunJobs();`. The first callback of an idle view runs on a plain `RunJobs()`. A plain
  `RunJobs()` also fires the real-time 60 Hz headless render timer when 16 ms have elapsed, so a test counts frames
  per pulse through the view's internal frame counter, never per `RunJobs()` call. ScottPlot's `RenderFinished`
  never fires headless (the draw operation has no Skia lease), so it is no observable.

## Implementation Steps

### Task 1: Measure what the plan changes

**Files:**
- Modify: `scripts/perf/trace-shares.py`
- Modify: `docs/architecture/testing-strategy.md`

- [x] the outputs listed in Technical Details
- [x] copy the before trace to `SemiPlot/Artifacts/perf/44bba75-drag.speedscope.json`, run the script on it and
      record the before numbers in this plan's Acceptance Evidence
- [x] `testing-strategy.md` "Frame cost": the new outputs and the drag and hover checks
- [x] `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 2: Pace the redraw by the display frame

**Files:**
- Delete: `SemiPlot/SemiPlot.UI/Chart/ChartRedrawSchedule.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartViewTestBuilder.cs`
- Modify: `AGENTS.md`, `docs/architecture/charting.md`, `trend-interaction.md`, `trend-feature-spec.md`,
  `testing-strategy.md`, `bench.md`

- [x] view model: `RedrawRequested` raised at once with a subscriber, nothing without one, completed on dispose
- [x] view: frame request with the flag set only after success, the guarded callback, the internal frame counter,
      a request in `OnLoaded`, pending state cleared in `OnDataContextChanged` and `OnUnloaded`; `RepaintChart`,
      `PlaceDeltaCursor`, `BeginPan` through the same request
- [x] test `ABurstOfRedrawRequestsInvalidatesThePlotOncePerFrame`: a burst on a shown view, one pulse, one frame
      counted (renamed in review to `ABurstOfRedrawRequests_JoinsOneFrame`, the counter to `ServedFrameCount`:
      they pin the coalescing, not the repaint)
- [x] test: a request on a detached view, then attach to a shown window and request again; one frame is counted
      (for the later request; the attached view's `OnLoaded` frame runs after the first pulse and is the baseline)
- [x] test: a frame pending when the view model is disposed runs as a no-op
- [x] rewrite the view-model redraw tests to the signal (idle, no subscriber, dispose, counts) and the comments at
      `TrendChartViewModelTests.cs:1964-1965` and `:2090-2091`; run `ChartPointerInputTests` and fix what the
      change moves
- [x] docs and specs: frame-paced redraw and §RT-4; delete the `Chart/ChartRedrawSchedule` module entry
      (`charting.md:336-338`); replace `testing-strategy.md` "The UI scheduler in a realised view" (`:197-215`) and
      `AGENTS.md:141-144` with the frame protocol in Technical Details; drop the timer reasoning from
      `bench.md:364-368` and the comments of `TrendChartViewTests.CreateViewModel` and
      `ChartViewTestBuilder.CreateLoadedViewModel`
- [x] `git grep -n "33 ms\|33ms" -- docs AGENTS.md SemiPlot/SemiPlot.Tests.Unit` prints nothing about the redraw
      (matches remain only in `docs/plans/`: this plan and the completed plans, which record history)
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 3: Bitmap-cache the plot visual

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs`
- Modify: `docs/architecture/charting.md`

- [x] `PlotControl.CacheMode = new BitmapCache()` in the constructor, with a pointer to `charting.md`
- [x] test `ThePlotIsBitmapCached`
- [x] `charting.md:119-127`: the cached plot layer is what keeps the overlay cheap; memory per window (written
      as the new section `charting.md#the-cached-plot-layer` after the frame-paced redraw)
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`
- [x] withdrawn: on the stand the cache froze the plot (no `RenderOnce` in 15 s of live follow, 2026-10-09);
      the line, `ThePlotIsBitmapCached` and the section are removed, and `charting.md#hover-and-the-plot-cache`
      records why
- [x] redone on the wrapper: `PlotControl` alone inside the `PlotLayer` decorator, which holds the
      `BitmapCache`; test `ThePlotIsCachedThroughItsOnlyAncestor`; the stand follow and hover captures pass
      (Acceptance Evidence, manual item 2)

### Task 4: Generate ticks for drawn axes only

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/LinearTickGenerator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/LogTickGenerator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartAxisBinder.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTickGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartAxisBinderTests.cs`
- Modify: `docs/architecture/charting.md`, `testing-strategy.md`

- [x] `LinearTickGenerator` with the two-entry cache and `IsDrawn`; `IsDrawn` on `LogTickGenerator` (both
      implement `IDrawnTickGenerator` and share `TickCache`)
- [x] `ChartAxisBinder`: a `LinearTickGenerator` on every created or linear axis; `IsDrawn` beside every
      `axis.IsVisible` write in the stated order, `HideAxis` included
- [x] `ChartTickGeneratorTests`: hidden generates nothing; lengths A, B, A, B over one range generate twice; a new
      range regenerates; a log generator hidden then drawn ticks again
- [x] `ChartAxisBinderTests`: only the active visible pen's generator is drawn; a hidden log pen's axis stays
      logarithmic
- [x] docs: the tick rule in `charting.md` (`#ticks-for-the-drawn-axis-only`); replace the
      `testing-strategy.md` "A hidden axis costs nothing to render" paragraph with the 2026-10-08 measurement
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 5: Release the sample gate before a query starts

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestCancellationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] `ObserveOn(dataScheduler)` between the merged heads and `Admit`
- [x] a test that `Request` returns while a paced admission is starting its query; run
      `ChartHistoryRequestCancellationTests`, `TrendChartCatalogueTests` and `TrendChartViewModelTests` and add any
      test the extra scheduler step moves to this task (moved: an advance ending on a sample tick in
      `AContinuousGestureFetchesAtTheCapInterval`, `AGestureEndingOnTheWindowBeingReadLeavesTheReadRunning` and
      `AReadForAnOlderPenSetLeavesTheNewPensReadRunning`; the two legend `LoadInitialHistory` helpers, whose UI
      scheduler is the same `TestScheduler`, advance `+ 2`)
- [x] `data-integration.md`: one sentence on where admission runs
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 6: Report the data-area width on change

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs`

- [x] `ReportDataAreaWidthTo` posts only a changed width
- [x] test: two renders at one width post once (`TwoRendersAtOneWidth_ReportTheWidthOnce`, through the production
      `RenderFinished` by `Plot.RenderInMemory`)
- [x] `dotnet test SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 7: Verify acceptance criteria

- [x] the three automatable acceptance tests green, each red with its change reverted (green at `bc6db78`, 10
      of 10; reverted in the working tree only, then restored: `RequestFrame` without the pending guard drew 12
      frames for 3 expected; no `CacheMode` found `<null>`; the `IsDrawn` guard off in both generators failed the
      five hidden-axis cases; the linear `TickCache` bypassed failed `TheTwoLengthsOfOneFrame_GenerateTwice`.
      Tasks 5 and 6 carry no acceptance test in the list and were not reverted)
- [x] `dotnet test SemiPlot.slnx` (unit 1839 of 1839, integration 147 of 147)
- [x] `dotnet format SemiPlot.slnx --verify-no-changes`

### Task 8: [Final] Update documentation

- [x] `AGENTS.md` only if a new rule appeared beyond Task 2's edit (no edit: every chart, scheduler and test
      statement still holds against `cd20c20`, and the anchors and helpers it names exist; the later rules, the
      `IsDrawn`/`IsVisible` order with `ChartAxisBinder` as the axes' one writer and the UI-thread-only redraw, are
      chart internals that `charting.md#ticks-for-the-drawn-axis-only` and `#the-frame-paced-redraw` hold, and the
      redraw's one test consequence, the immediate UI scheduler throwing, is already in Task 2's edit)
- [x] move this plan to `docs/plans/completed/` (left for delivery)

## Post-Completion

**Manual verification**: the four trace checks in Acceptance Evidence, before shipping. If the hover check
fails, the fallback is an owned surface: a sealed `AvaPlot` subclass whose draw operation renders into a
device-pixel `SKSurface` only when the view's frame callback raises a dirty flag, then draws that image; same
render thread, same `Plot.Sync`. One visual check of tick-label edges with the cache on; if they fringe, set
`EnableClearType` and `SnapsToDevicePixels` on the cache. One minimized-window check: minimize the stand for 10 s
with the writer running and read the `RenderOnce` count from a trace; frames either stop and resume on restore or
continue unseen as today, and `charting.md` records which.

**After an Avalonia upgrade**: rerun the hover trace; `BitmapCache` is new in Avalonia 12 and its invalidation
rules may change.

**Executed by exec:**
- branch: chart-render-headroom

## Verify it yourself

Automated, from the repository root:

1. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartViewTests|FullyQualifiedName~ChartTickGeneratorTests|FullyQualifiedName~ChartAxisBinderTests|FullyQualifiedName~ChartHistoryRequestDebouncerTests"`
   passes at HEAD. Each change was checked red with its production edit reverted: no coalescing gives 12 frames
   instead of 3 (`ABurstOfRedrawRequests_JoinsOneFrame`), no `IsDrawn` guard fails the hidden-axis cases, a bypassed `TickCache` generates four times at lengths
   A, B, A, B, no `ObserveOn` hop times out `RequestReturnsWhileAPacedAdmissionIsStartingItsQuery`, no width check
   fails `TwoRendersAtOneWidth_ReportTheWidthOnce`.
2. `dotnet test SemiPlot.slnx` (unit 1843, integration 147) and `dotnet format SemiPlot.slnx --verify-no-changes`.

On the demo stand, Release (`dotnet run -c Release --project SemiPlot/SemiPlot.AppHost`), with the viewer's process
id from Task Manager:

1. Wait until the window finishes warming up (about 10 s), start
   `dotnet-trace collect -p <pid> --format Speedscope --duration 00:00:00:45 -o drag.nettrace` and drag the chart
   back and forth for the whole 45 s.
2. `python scripts/perf/trace-shares.py drag.speedscope.json --skip-seconds 0` and compare with the before table
   above (`... 44bba75-drag.speedscope.json --skip-seconds 10`): drag-phase `RenderOnce` spacing p50 near the
   compositor interval (16.8 ms) instead of 33.4 ms, pan step p50 under 12 ms instead of 24.4 ms, `GenerateTicks`
   0.12 ms or less per `RenderOnce` instead of 0.351 ms, no `Enter_Slowpath` run under `OnNavigationWindowChanged`.
3. Follow for 20 s with the writer running, no pointer in the window, no resize, under a 20 s trace: `RenderOnce`
   at least once per writer tick. Watch the bottom time axis advance for 10 s, then switch the theme and confirm
   the plot repaints without a resize.
4. Minimize the window for 10 s under a trace to record whether frames stop.

The behaviour the operator sees: the chart follows a drag every display frame instead of every second one, and
with the active pen switched off the plot shows no horizontal gridlines. Hover over a still plot re-renders it
only when new data arrive.
