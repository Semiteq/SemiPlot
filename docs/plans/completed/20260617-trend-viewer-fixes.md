# Trend Viewer Fixes & Refactors (post-replatform polish)

## Overview

A grouped fix/refactor pass on the Avalonia + ScottPlot trend viewer (branch `avalonia-scottplot-plan`)
addressing a user-reported issue list: structural cleanup (DataSource extraction, comment sweep, test
layout), pointer-interaction correctness (hand-pan, on-chart cursor readout, functional delta cursors,
axis-click range edit), zoom correctness/performance, a sticky-toggle desync, and an archive-overview
minimap. Root causes were established by a read-only investigation flow (KT IS/IS-NOT dossiers); this
plan turns those findings into sequenced, testable tasks. It does NOT re-investigate.

Dossier source (root-cause detail per item): the investigation output captured in the session task log.
Authoritative behavior spec: `docs/architecture/trend-interaction.md`, `docs/architecture/charting.md`.

## Context (from discovery)

- **Branch:** `avalonia-scottplot-plan`. Stack: .NET 10, Avalonia 11.3.8, ScottPlot.Avalonia 5.1.57,
  ReactiveUI. Full suite currently green (Core.Tests xunit.v3 + SemiPlot.Tests headless xunit v2).
- **Pointer pipeline (shared by b3/b4/b2/b7):** `SemiPlot.UI/Chart/TrendChartView.axaml(.cs)`
  `OnPointerPressed/Moved/Exited`; the hidden `DeltaCursorsEnabled`-hijacks-left-click branch
  (~TrendChartView.axaml.cs:110-116); `CreateCursorLine`/`MoveCursorTo` (~167-219).
- **Navigation/zoom:** `Core/Trends/TrendNavigationModel.cs` (pure), `Chart/ChartNavigationController.cs`
  (`LayerForWidth`, `ZoomAt`, `OnLiveEdge`), `Chart/TrendChartViewModel.cs` (`OnNavigationWindowChanged`
  → `coordinator.SetLayer` + `RequestHistory`), redraw coalesced at 33 ms via `Sample` (`TrendChartViewModel.cs:64-66`).
- **Sticky:** `Toolbar/TrendToolbarViewModel.cs` (IsSticky, OneWay-bound `ToggleButton`), `TrendNavigationModel.IsSticky`, auto-detach on pan-past-live.
- **Cursors:** `Core/Trends/CursorReadoutModel.cs`, `DeltaCursorModel.cs`; `Chart/ChartCursorReader.cs`, `ChartDeltaCursorReader.cs`; legend value-at-cursor column in `Legend/TrendLegendView.axaml`.
- **Data layer (s3):** `Core/Data/` = `RandomStubDataProvider`, `SyntheticPen(Catalog)`, `SyntheticValueWalk`, `SyntheticQuality`, `MinMaxDecimator`, `DataServiceCollectionExtensions` (AddData), `IDataProvider`. DTOs in `Core/Trends`. `MinMaxDecimator` is stub-only (sole caller `RandomStubDataProvider`).
- **Tests:** two projects by design (s1). `SemiPlot.Tests.csproj` references `SemiPlot.UI`; stub tests sit in `SemiPlot.Core.Tests/Core/Data`.

## Development Approach

- **Testing approach: Regular** (implement, then add/update tests within the same task).
- **VM-size discipline:** `TrendChartViewModel` is already ~437 lines (over the 300 soft limit; decomposition deferred from the replatform). New logic in Tasks 4/7/8/9 MUST land in `Chart/Chart*` helper classes (binder / navigation controller / appliers / cursor+delta readers / a small cursor sub-VM), NOT the VM. If a feature cannot be added without growing the VM, extract a helper.
- Complete each task fully; full suite green before the next. Commands: `dotnet build SemiPlot/SemiPlot.slnx`
  (0 errors) and `dotnet test SemiPlot/SemiPlot.slnx`. Pure-logic in `SemiPlot.Core.Tests` (plain `[Fact]`);
  VM/interaction in `SemiPlot.Tests` (headless `[AvaloniaFact]`); rendering/feel → manual verification.
- Commit ONLY each task's files explicitly (never `git add -A`); revert stray `dotnet format` BOM churn in
  unrelated files. Follow CLAUDE.md style (class ≤300 / method ≤50, one class per file, file-scoped namespaces, tabs).
- **Locked decisions** (do not relitigate): s1 = keep split (doc only); s3 = new `SemiPlot.DataSource.Stub`
  project; pointer model = drag is always hand-pan, hover shows an on-chart all-pens label, delta cursors via
  a toolbar mode; b1 = artifact CONFIRMED via `example_pics/1.png` (right-side straight-line collapse after repeated zoom) — proceed in sequence.

## Testing Strategy

- **Unit (Core.Tests):** navigation quantization (b1), any decimator change (stays with stub), sticky-state assertions reachable in Core.
- **Headless VM (SemiPlot.Tests):** b5 sticky propagation; b4 delta-mode state + Δt/Δy; b2 readout content; b3 drag-pan vs no-cursor; b7 axis-region edit commands; b8 debounce/latest-wins via TestScheduler; b6 minimap VM + extent query.
- **No e2e.** Zoom smoothness/feel, hand cursor, label placement, minimap visuals → manual verification (Post-Completion).

## Progress Tracking

- `[x]` when done; `➕` new tasks; `⚠️` blockers. Keep this file in sync.

## Solution Overview

- **Structure first** (s1 doc → s3 extraction) so later feature work does not move files underneath itself.
- **Pointer pipeline as one coordinated model** (b3→b4→b2, then b7): a single source of truth for the
  left-button gesture (pan vs delta-mode placement vs axis-region edit) and a single hover-readout suppressed
  during drag/delta.
- **Zoom: performance decouple now, correctness when unblocked.** b8 (route redraw through the 30 FPS stream,
  debounce history, offload the query off the UI thread) is what the user *feels*; b1 (zoom-math robustness)
  layers on the same path once the screenshot confirms the artifact.
- **Minimap** is additive (new data seam + isolated view), built after the DataSource home is fixed.
- **Comment sweep last**, so it deletes comments the refactors made redundant without churning in-flight files.

## Technical Details

- **s3 new project** `SemiPlot.DataSource.Stub` (Microsoft.NET.Sdk, plain `net10.0`, inherits Directory.Build.props):
  bare `PackageReference`s FluentResults / System.Reactive / Microsoft.Extensions.DependencyInjection.Abstractions
  (already centrally versioned), `ProjectReference` → SemiPlot.Core. Move `RandomStubDataProvider`, `SyntheticPen`,
  `SyntheticPenCatalog`, `SyntheticValueWalk`, `SyntheticQuality`, `MinMaxDecimator`, `DataServiceCollectionExtensions`
  (namespace `SemiPlot.Core.Data` → `SemiPlot.DataSource.Stub`). Keep `IDataProvider` in Core. Add to `slnx`; UI +
  `SemiPlot.Tests` add a `ProjectReference`; fix usings in `Program.cs`, `CompositionRootTests.cs`; stub tests
  (`SemiPlot.Core.Tests/Core/Data/*`) get a `ProjectReference` + namespace using update (tests stay in Core.Tests per s1).
- **b8 threading:** `OnNavigationWindowChanged` must not run `QueryHistoryAsync` synchronously on the UI thread;
  debounce `WindowChanged`-driven `RequestHistory` (guaranteed trailing emit), run the query on the data scheduler,
  apply results on the UI scheduler with a latest-wins guard (drop stale responses). Redraw via the existing
  `Sample(33ms, uiScheduler)` stream, not inline `Refresh()`. `RequestInitialHistory` must still fire once promptly.
- **b1 robustness (when unblocked):** quantize `TrendNavigationModel.Zoom` width so in/out cycles return to origin;
  add `LayerForWidth` hysteresis at the 1h boundary; keep `From ≥ FirstSample`; ensure no stale async repaint.
- **Pointer model:** one `enum`/state for the active left-button tool (Pan default | DeltaPlacement when delta mode).
  Hover readout = a ScottPlot Text/Annotation pinned to `Plot.Axes.Bottom` X, content = all visible pens' Center value
  at cursor + timestamp, hidden while dragging or in delta mode. Delta mode = a toolbar toggle; two clicks place the
  cursors; Δt + active-pen Δy shown in a readout (location confirmed in b4).
- **b6 data seam:** add `QueryArchiveExtentAsync()` (first..last sample) to `IDataProvider`; stub returns a synthetic
  depth (e.g. now-7d..now); minimap maps the full extent to a strip with the current `[From,To]` highlighted; click/drag
  navigates via `TrendNavigationModel`.

## What Goes Where

- **Implementation Steps (`[ ]`):** all code, tests, doc updates inside this repo.
- **Post-Completion:** manual run/feel verification, the "picture 1" repro for b1, the future Avalonia-12 test-merge, real PostgreSQL provider, IJ theming.

## Implementation Steps

### Task 1: Fix sticky-toggle desync (b5)

**Files:** Modify `SemiPlot/SemiPlot.UI/Toolbar/TrendToolbarViewModel.cs`; Modify `SemiPlot/SemiPlot.Tests/UI/Toolbar/TrendToolbarViewModelTests.cs`

- [x] In the existing navigation-window-changed handling, refresh the toolbar `IsSticky` from `Navigation.IsSticky` (single source of truth) so auto-detach (pan past live edge) and `JumpToNow` re-attach both reflect on the button.
- [x] Make `OnNavigationWindowChanged` the SINGLE writer of toolbar `IsSticky`; remove the now-redundant imperative `IsSticky = …` assignments in `JumpToNow`/`ToggleSticky` (otherwise two write paths remain — the exact desync this task claims to remove).
- [x] Confirm the `ToggleButton` stays consistent (OneWay `IsChecked` ← `IsSticky`); no double-path desync.
- [x] Write a regression test: pan-past-live auto-detach flips `IsSticky` to false on the toolbar VM; `JumpToNow` re-attaches to true.
- [x] `dotnet test SemiPlot/SemiPlot.slnx` green before Task 2.

### Task 2: Document the test-project split decision (s1)

**Files:** Modify `SemiPlot/CLAUDE.md`; Modify this plan (Post-Completion future item)

- [x] Record in CLAUDE.md WHY the two test projects stay split: `Avalonia.Headless.XUnit 11.3.8` is xunit v2; `SemiPlot.Core.Tests` is xunit.v3; one project cannot hold both majors; merging would downgrade Core to v2 and re-couple it to the UI build.
- [x] Add a tracked future item (Post-Completion / a backlog note) "bump Avalonia 11.3.8 → 12.0.x, verify ScottPlot.Avalonia on 12, then unify tests on xunit.v3 in one project".
- [x] No code change; `dotnet build SemiPlot/SemiPlot.slnx` stays green. (Doc-only task — no new tests.)

### Task 3: Extract SemiPlot.DataSource.Stub (s3)

**Files:** Create `SemiPlot/SemiPlot.DataSource.Stub/SemiPlot.DataSource.Stub.csproj`; Move (git mv + namespace) `SemiPlot/SemiPlot.Core/Data/{RandomStubDataProvider,SyntheticPen,SyntheticPenCatalog,SyntheticValueWalk,SyntheticQuality,MinMaxDecimator,DataServiceCollectionExtensions}.cs` → the new project; Modify `SemiPlot/SemiPlot.slnx`, `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`, `SemiPlot/SemiPlot.UI/Program.cs`, `SemiPlot/SemiPlot.Core.Tests/SemiPlot.Core.Tests.csproj`, `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`, `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`, the 3 stub tests' usings

- [x] Pre-step: confirm the working tree is clean for the 7 `Core/Data` files before `git mv` (commit/discard any pre-existing churn) so the move commit is a clean rename, not bundled BOM/format noise.
- [x] Create the new `net10.0` library; move the 7 stub files with namespace `SemiPlot.Core.Data` → `SemiPlot.DataSource.Stub`; keep `IDataProvider.cs` in Core.
- [x] Add the project to `slnx`; add `ProjectReference` from UI and from `SemiPlot.Tests` (for `AddData`); add `ProjectReference` from `SemiPlot.Core.Tests` (stub tests stay there per s1); update usings in `Program.cs`, `CompositionRootTests.cs`, and the 3 stub tests.
- [x] Verify CPM: no new `PackageVersion` needed (reuse existing); restore clean.
- [x] No new behavior; existing stub tests must still pass unchanged (only namespace/refs updated).
- [x] `dotnet build SemiPlot/SemiPlot.slnx` 0 errors and `dotnet test SemiPlot/SemiPlot.slnx` green before Task 4.

### Task 4: Zoom performance — decouple redraw + history from the UI thread (b8)

**Files:** Modify `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`, `Chart/ChartNavigationController.cs`, `Bridge/TrendCoordinator.cs` (if the query offload lands there); Modify/add tests in `SemiPlot/SemiPlot.Tests/UI/Chart/`

- [x] Apply the history debounce ONLY to the VM gesture path — `OnNavigationWindowChanged` where `RequiresHistoryRequery == true`. Use `Throttle(debounceWindow, scheduler)` (trailing-edge: always emits the last window after a quiet period), NOT `Sample` (which can drop the final emission). Run `QueryHistoryAsync` on the data scheduler; apply on the UI scheduler with a latest-wins guard (drop stale responses).
- [x] Do NOT place the debounce inside `TrendCoordinator.RequestHistory` (the shared entry point): `RequestInitialHistory` must bypass it and fire once immediately, and the `TrackDataExtents` `RequiresHistoryRequery:false` startup path must stay non-requerying (preserve the shipped single-initial-load guard).
- [x] Route per-zoom redraws through the existing 30 FPS coalesced `Sample(33ms)` stream rather than inline `Refresh()`.
- [x] Keep the debounce a single chokepoint on the gesture-driven `RequestHistory` so a later `TrendNavigationModel.Zoom` quantization (b1) needs no debounce change — locate it correctly rather than add hooks for b1.
- [x] Tests (`[AvaloniaFact]`, TestScheduler): rapid zoom emits ONE trailing history request (not one-per-notch); after the stream goes quiet the LAST window is always queried; a late stale response is dropped; redraw is coalesced; the existing `RequestInitialHistory_FiresExactlyOneHistoryQuery_NoDoubleLoad` test still passes unchanged. Green before Task 5.

### Task 5: Zoom correctness — repeated zoom collapses the right side into straight lines (b1)

**Files:** Modify `SemiPlot/SemiPlot.Core/Data/MinMaxDecimator.cs` (or stub provider) and/or `Chart/TrendPenState.cs`, `Chart/ChartNavigationController.cs`, `Core/Trends/TrendNavigationModel.cs`; Modify `SemiPlot/SemiPlot.Core.Tests/...`

CONFIRMED ARTIFACT (`example_pics/1.png`): after repeated wheel zoom, the left ~40% of the window renders correct dense multi-pen oscillation, but the right portion collapses into long near-straight lines fanning to the right edge — series are drawn as straight segments across a span that should be either densely sampled or NaN-segmented. Repro steps (zoom direction sequence / starting width / pen) still welcome but the symptom is characterized.

- [x] FIRST diagnose against the image symptom (KT): determine whether the right-side collapse is (a) the window `To` pushed past the densely-sampled/available data so each pen straight-lines to its last point, (b) `LayerForWidth` returning a coarse layer whose envelope yields ~one column for the right span, or (c) the center line joining across an interval that should be a NaN gap. Confirm which before coding (a focused look at the decimation output + window bounds for the exact zoom that produced the image). CONFIRMED cause (a)+(c): probes showed `Zoom` lets `From` drift ~17 days before `FirstSample` (no clamp) and the decimator emits NO column for an empty edge sub-span (trailing/leading null run), so the chart bridges the empty right span with a straight line from the last data column to the realtime live-edge point — fanning per-pen. Layer thrash at the 1h boundary (no hysteresis) compounds it by appending a far-right raw point at the Raw side.
- [x] Fix the confirmed cause; additionally harden the zoom path per the dossier: quantize `Zoom` width so in→out→in returns to the origin window; add `LayerForWidth` hysteresis at the 1h boundary; keep `From ≥ FirstSample`; eliminate stale async repaint (compose with the Task 4 latest-wins guard). Decimator now anchors a NaN gap at the window edge for leading/trailing empty sub-spans; `Zoom` snaps width onto a 1.25 ladder and clamps `From ≥ FirstSample`; `LayerForWidth` got a 10% hysteresis band on each ceiling. The Task 4 `Switch()` latest-wins debouncer already drops stale repaints and was left untouched.
- [x] Tests: a window whose right span has no/sparse data renders as a gap (NaN-segmented), NOT straight lines across it; zoom-in-then-out returns to the original `[From,To]` within tolerance; no layer flip-flop across the boundary under hysteresis.
- [x] Full suite green before proceeding.

### Task 6: Hand-pan drag (b3)

**Files:** Modify `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`, `Chart/TrendChartViewModel.cs`; Modify tests in `SemiPlot/SemiPlot.Tests/UI/Chart/`

- [x] Make left-drag the unambiguous "hand" pan: pointer capture on press, pan the X window via `TrendNavigationModel` on move, release ends; set a hand/grab cursor while panning. Introduce a left-button tool-state enum (Pan default | DeltaPlacement) and route the EXISTING delta branch through it, driven by the legacy `DeltaCursorsEnabled` flag for now — the state is not fully single until Task 7 re-sources it from the toolbar toggle and deletes the flag.
- [x] Suppress the hover readout/line while a drag is in progress.
- [x] Tests (`[AvaloniaFact]`): a left-drag pans the nav window and does NOT place a cursor or zoom; hover readout is hidden during drag. Green before Task 7.

### Task 7: Delta cursors via toolbar mode + Δt/Δy readout (b4)

**Files:** Modify `SemiPlot/SemiPlot.UI/Toolbar/TrendToolbarView.axaml(.cs)`, `Toolbar/TrendToolbarViewModel.cs`, `Chart/TrendChartView.axaml.cs`, `Chart/ChartDeltaCursorReader.cs`, `Chart/TrendChartViewModel.cs`; Modify/add tests in `SemiPlot/SemiPlot.Tests/UI/`

- [x] Flip the Task-6 tool-state source from the legacy `DeltaCursorsEnabled` flag to an explicit toolbar "Delta" mode toggle, and delete the flag + the old hidden hijack branch. In delta mode (state = DeltaPlacement), clicks place the two cursors and drag does NOT pan; re-click exits to Pan. DONE: source of truth is the toolbar `IsDeltaModeEnabled` toggle → chart `IsDeltaModeEnabled` → `ActiveLeftButtonTool`. Legacy `DeltaCursorsEnabled` flag, `SetDeltaCursorsEnabled`, and `ToggleDeltaCursorsCommand` deleted; `OnPointerPressed` already routes via the `LeftButtonTool` enum (no separate hijack branch). Exiting clears the placed cursors and the view hides the delta lines.
- [x] Surface Δt and active-pen Δy in a readout (location chosen during impl — recommend the docked message-panel row or an on-chart label; Δy for the active pen only). DONE: chosen location = inline toolbar `TextBlock` next to the Delta toggle (simplest, always visible, no MainWindow re-wiring). Reuses `DeltaCursorModel`/`ChartDeltaCursorReader`; formatting helper `ChartDeltaCursorReader.FormatReadout` keeps the VM thin. Δy for the active pen only per the locked decision.
- [x] Tests (`[AvaloniaFact]`): entering mode + two clicks places both cursors and computes Δt/Δy; drag does not pan while in mode; exiting clears mode. Green before Task 8.

### Task 8: On-chart hover value label, all visible pens (b2)

**Files:** Modify `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs` (and `.axaml` only if an Avalonia overlay is chosen); Modify tests in `SemiPlot/SemiPlot.Tests/UI/Chart/`

- [x] Add an on-chart readout (ScottPlot Text/Annotation pinned to `Plot.Axes.Bottom` X like the cursor line, or an Avalonia overlay) fed from the already-computed `CursorValues`: show every visible pen's Center value at the cursor X + the timestamp. DONE: a `ScottPlot.Plottables.Text` (`Add.Text`) pinned to `Plot.Axes.Bottom` (mirroring the cursor line's axis pinning so the shared-X invariant holds), wrapped by view-side helper `Chart/ChartHoverReadout.cs`. Content = local timestamp + one line per visible pen (`Name: FormatValue`); kept off the VM (view + helper), VM unchanged at 508 lines.
- [x] Suppress it while dragging (Task 6) and while delta mode is on (Task 7); reuse the synchronous `MoveCursor` value + existing Refresh path (no new observable). DONE: `TrendChartView.UpdateHoverReadout` suppresses when `IsDragging || IsDeltaModeEnabled`, called from the hover/exit/drag-begin/delta-toggle paths and fed from `CursorTime`/`CursorValues` (no new observable); reuses the existing `_plotControl.Refresh()`.
- [x] Tests (`[AvaloniaFact]`): on hover the readout content reflects all visible pens' values at X (incl. gap → no value); hidden during drag/delta. Green before Task 9. DONE: `ChartHoverReadoutTests` (5 tests) assert all-visible-pens content + timestamp, gap → dash, hidden pens skipped, hidden when suppressed (drag/delta), hidden with no cursor.

### Task 9: Y-axis click-region range edit (b7)

**Files:** Modify `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`, `Chart/ChartAxisBinder.cs`, `Chart/TrendChartViewModel.cs`, `Core/Trends/PenScaleModel.cs` (if needed); Modify tests in `SemiPlot/SemiPlot.Tests/UI/Chart/`

- [x] Confirm the ScottPlot 5.1.57 axis pixel / hit-test API (use the reverse-engineering skill on the assembly). Add a pre-branch in `OnPointerPressed`: a click on the active pen's Y axis upper region edits MAX, lower region edits MIN; double-click = autoscale (`ScaleMode.Auto`). It must NOT start a pan or place a delta cursor. DONE: confirmed API is `IAxis.GetCoordinate(float pixel, PixelRect dataArea)` / `IAxis.Range` plus `Plot.RenderManager.LastRender.Layout` (`DataRect`, `PanelSizes`/`PanelOffsets` keyed by `IPanel`) and `IYAxis.Edge`. New view-side helper `Chart/ChartAxisRegion.cs` hand-rolls the per-axis panel band (left/right of `DataRect` by the measured panel size/offset), the upper/lower-half split (data-area top = MAX), and the pixel→value mapping (inverted, top pixel = max). `TrendChartView.OnPointerPressed` gains `TryHandleAxisRegionPress` as the FIRST branch (before delta/pan): in-band single click opens the inline editor, double-click (`ClickCount >= 2`) calls `AutoscaleAxis`; the press is marked handled so it never starts a pan or places a delta cursor.
- [x] Inline editor / numeric input seeds the untouched bound from the computed range; feed `PenScaleModel` manual limits; top=max / bottom=min mapping correct (watch pixel-Y inversion). DONE: an Avalonia `TextBox` overlay (`AxisBoundEditor` in the axaml `Panel`) is seeded with the clicked value; pure helper `Chart/ChartAxisEdit.SeedManualLimits` carries the untouched bound from `ScaleRangeForPen` (not a 0..1 default) and orders the result; commit feeds the existing `TrendChartViewModel.SetAxisLimits` (Manual mode in `PenScaleModel`). VM growth limited to a 7-line read-only `ActivePenAxis` accessor delegating to `ChartAxisBinder.FindAxis`; all region/seed logic stays in `Chart/*`.
- [x] Tests (`[AvaloniaFact]`): upper-region edit sets MAX, lower sets MIN, double-click autoscales; no pan/delta side effect. Green before Task 10. DONE: `ChartAxisEditTests` (3, pure seeding/ordering), `ChartAxisRegionTests` (3, panel-band Contains + upper/lower split + top=max/bottom=min mapping against a real `RenderInMemory` layout), `ChartAxisRegionEditTests` (5 `[AvaloniaFact]`: upper→MAX Manual, lower→MIN Manual, double-click→Auto, axis-region edit leaves the nav window + delta cursors untouched, `ActivePenAxis` resolves to the pen's rendered axis instance). Full suite green: 84 Core.Tests + 110 SemiPlot.Tests = 194.

### Task 10: Archive-overview minimap (b6)

**Files:** Modify `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs` (+ stub impl in `SemiPlot.DataSource.Stub`, FakeDataProvider in tests), `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs` (extent pass-through); Create `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml(.cs)`, `Minimap/MinimapViewModel.cs`; Modify `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`, DI wiring; Create tests in `SemiPlot/SemiPlot.Tests/UI/Minimap/`

- [x] Add `QueryArchiveExtentAsync()` (first..last sample) to `IDataProvider`; implement in the stub (synthetic depth e.g. now-7d..now) and in the test `FakeDataProvider`. Expose it to the minimap VM via a `TrendCoordinator` pass-through (consistent with the existing `QueryHistoryAsync` seam + UI-scheduler discipline) — the minimap VM must NOT hold `IDataProvider` directly. DONE: new `ArchiveExtent(FirstUtc, LastUtc)` record in `Core/Data`; `IDataProvider.QueryArchiveExtentAsync()` returns `Task<Result<ArchiveExtent>>`; stub returns `now-7d..now` (fixed `_archiveDepth`), `FakeDataProvider` returns settable `ArchiveFirstUtc/LastUtc`. `TrendCoordinator.QueryArchiveExtentAsync()` mirrors the `QueryHistoryAsync` pass-through; `MinimapViewModel` awaits it and marshals the result onto the captured UI scheduler (never holds `IDataProvider`).
- [x] Build a lightweight Avalonia overview strip (preferred over a 2nd AvaPlot): render the full extent with the current `[From,To]` highlighted; click/drag on the strip navigates via `TrendNavigationModel`; the live edge follows realtime when sticky. DONE: `Minimap/MinimapView.axaml(.cs)` is a Canvas-based strip (no 2nd AvaPlot) with a highlight `Border` positioned from the VM's `WindowStartFraction/WindowWidthFraction` scaled by the live control width; press/drag converts pointer-X to a fraction → `MinimapViewModel.NavigateToFraction` → recenters via the shared `ChartNavigationController.PanBy`. The highlight tracks every `WindowChanged` (pan/zoom and sticky live-edge advance).
- [x] Add the minimap row to the MainWindow layout; wire the VM from DI. DONE: thin 36px `MinimapView` row inserted beneath the chart (RowDefinitions extended, MessagePanel/StatusBar shifted); a DI factory `Func<TrendCoordinator, ChartNavigationController, IScheduler, MinimapViewModel>` builds it in `App.InitializeServices` sharing the chart's navigation + coordinator; `MainWindowViewModel.MinimapViewModel` holds and disposes it; `LoadExtentAsync` fired at startup.
- [x] Tests (`[AvaloniaFact]` + Core where pure): extent query returns first..last; window rectangle maps to `[From,To]`; click maps a pixel to the right time and updates navigation. Green before Task 11. DONE: pure `MinimapGeometryTests` (window-fraction mapping/clamp/zero-span + fraction→time + clamp), stub `QueryArchiveExtentAsync_ReturnsSevenDayDepthEndingAtNow`, and `[AvaloniaFact]` `MinimapViewModelTests` (extent first/last, window-rectangle fractions, click recenters navigation, no-op before extent loaded). Full suite green: 92 Core.Tests + 114 SemiPlot.Tests = 206.

### Task 11: Comment cleanup sweep (s2)

**Files:** Modify the replatform UI + Core source per the dossier KEEP/STRIP/CONDENSE policy (Chart/*, Toolbar/*, Legend/*, MainWindow, App, Program, DI; Core/Trends/*, Core/Data abstraction)

- [x] STRIP comments that restate a name/signature/obvious purpose (per-member enum/record narration, one-line method-purpose comments). KEEP load-bearing rationale: ScottPlot live-reference/axis-instance contracts, scheduler/thread affinity (incl. `Bridge/TrendCoordinator.cs` realtime-coalesce-window + UI-thread-only-state invariants), startup double-load / sticky-no-requery guards, realtime cap, DST limit, log-axis clamp, coarse-layer fold. CONDENSE multi-line class-header overviews to 1-3 lines. DONE: stripped per-member enum/record narration (ScaleMode, PenScale, PenScaleSettings, PenLineStyle, RealtimeBatch, DeltaReadout, PenHistoryEnvelope, TrendHistory, ArchiveExtent) and one-line method/property restatements in TrendChartViewModel/TrendToolbarViewModel/legend/minimap; condensed class-header overviews (TrendNavigationModel, PenScaleModel, MinimapGeometry, CursorReadoutModel, DeltaCursorModel, TrendChartViewModel, TrendPenState, ChartNavigationController, ChartRealtimeApplier, ChartCursorReader, ChartDeltaCursorReader, ChartHoverReadout, legend/minimap VMs, toolbar VM, MinMaxDecimator). Kept all KEEP-listed rationale (TrendCoordinator invariants untouched, DST limit, log-axis clamp, NaN-gap anchor, layer hysteresis, zoom quantization, Throttle/Switch latest-wins, shared-X / live-reference / tick-generator-in-place / axis hit-test pixel-inversion, scheduler affinity, startup double-load, sticky-no-requery).
- [x] English only, no process-narration, prefer naming (CLAUDE.md). Comments-only — no behavior change.
- [x] `dotnet build` + `dotnet format` + `dotnet test SemiPlot/SemiPlot.slnx` all green (guards the comments-only edit). Green before Task 12. DONE: build 0 errors; full suite 92 Core.Tests + 114 SemiPlot.Tests = 206 unchanged; `dotnet format whitespace --verify-no-changes` reports the SAME 9 pre-existing CHARSET (utf-8-bom) findings as the HEAD baseline — no new format delta introduced by the sweep (`git diff --check` clean).

### Task 12: Verify acceptance criteria

- [x] Verify each addressed item behaves per `trend-interaction.md` / `charting.md`: b5 sticky in sync; b2 on-chart value visible; b3 clean hand-pan; b4 delta mode + Δt/Δy; b7 axis-click edit; b8 no UI freeze on zoom; b6 minimap navigates; s3 structure; s2 comments. (b1 only if unblocked + implemented.) DONE: all items implemented + test-evidenced (coverage checklist in progress log). No residual WebView2/uPlot in source (`.cs` grep clean; remaining hits are docs only). Build 0 errors.
- [x] Run `dotnet test SemiPlot/SemiPlot.slnx` (full suite) and `dotnet format SemiPlot/SemiPlot.slnx`. DONE: full suite green 92 Core.Tests + 114 SemiPlot.Tests = 206 (0 failed, 0 skipped). `dotnet format whitespace` normalized the 9 pre-existing missing-UTF-8-BOM files (CHARSET) — committed separately; re-verify reports 0 CHARSET findings and suite still green.
- [x] ⚠️ Confirm b1 status (done or still parked) and record it. DONE: b1 is **IMPLEMENTED (not parked)** — `MinMaxDecimator` anchors a NaN gap at leading/trailing empty edge sub-spans; `TrendNavigationModel.Zoom` snaps width onto a 1.25 ladder and clamps `From ≥ FirstSample`; `ChartNavigationController.LayerForWidth` has a 10% hysteresis band. Evidence: `MinMaxDecimatorTests` (leading/trailing NaN-gap anchor), `TrendNavigationModelTests.Zoom_InThenOut_ReturnsToOriginWindowWithinTolerance`, layer-hysteresis tests.

### Task 13: [Final] Documentation & plan close-out

- [x] Update `docs/architecture/*` if any interaction/data-seam behavior changed (pointer model, `QueryArchiveExtentAsync`, minimap); update CLAUDE.md if new conventions emerged. DONE: `trend-interaction.md` (left-button tool-state Pan|DeltaPlacement + hand-pan with pointer capture/grab cursor; on-chart all-pens hover readout suppressed during drag/delta; delta-cursor toolbar mode + Δt/active-pen-Δy readout; Y-axis click-region MAX/MIN edit + double-click autoscale; zoom 1.25-ladder quantization + `From ≥ FirstSample`; debounced off-UI-thread history via `ChartHistoryRequestDebouncer` Throttle→Switch latest-wins + 30 FPS coalesced redraw; `LayerForWidth` 10% hysteresis; `MinMaxDecimator` NaN-gap edge anchor; archive-overview minimap), `charting.md` (new `Chart/*` helpers — debouncer, hover readout, axis-region/edit, LeftButtonTool — + `MinimapGeometry`/`ArchiveExtent` Core, `MinMaxDecimator` moved to `SemiPlot.DataSource.Stub`, `QueryArchiveExtentAsync` data contract), `data-integration.md` (`QueryArchiveExtentAsync`/`ArchiveExtent` seam + `SemiPlot.DataSource.*` project home), `overview.md` (component box: Core = abstraction+DTOs, stub split into `SemiPlot.DataSource.Stub`), and `CLAUDE.md` (DataSource.* project layout + single-writer sticky / single-state left-button tool conventions). Grounded in the as-built code/commits, not just the plan.
- [x] (deferred to end of run — moved after review/finalize phases) Move this plan to `docs/plans/completed/` (only once b1 is resolved or explicitly deferred out of scope). b1 is IMPLEMENTED (Task 5/12), so the move is unblocked; physically deferred per run instruction to after the review/finalize phases.

## Post-Completion

*Items requiring manual intervention or external systems — informational only.*

**Manual verification:**
- Run the app (`dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`): hand-pan feel + cursor icon; on-chart all-pens readout; delta mode placement + Δt/Δy; axis-click range edit; zoom smoothness with NO UI freeze across 1s…1y; minimap navigation; sticky button never desyncs.
- **b1 (artifact confirmed, `example_pics/1.png`):** exact repro steps (zoom sequence / starting width / which pen) are still welcome to speed the focused diagnosis, but Task 5 is no longer blocked.

**Backlog / later:**
- Future: bump Avalonia 11.3.8 → 12.0.x (verify ScottPlot.Avalonia compatibility on 12 first), then unify the two test projects on xunit.v3 in one project (Avalonia.Headless.XUnit 12.x targets xunit.v3) — the deferred s1 PATH B.
- Real `SimpleScadaDataProvider` (OPC UA + PostgreSQL) as a sibling `SemiPlot.DataSource.*`; IJ theming; view persistence/export.
