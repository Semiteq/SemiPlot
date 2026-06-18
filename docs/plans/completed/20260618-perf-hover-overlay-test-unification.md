# Perf (cheap hover) + Test Unification + Comment Cleanup

## Overview

Three independent improvements to the Avalonia/ScottPlot trend viewer, delivered as three
separate PRs (one logical change each, per the repo's one-PR-one-change rule):

1. **Make hover cheap.** Today every pointer-move triggers a full synchronous ScottPlot
   re-render. Route cursor work off the per-event `Refresh()` path and lift the hover crosshair +
   readout into an Avalonia overlay so moving the mouse no longer re-rasterizes the series.
2. **Unify the two test projects into one** on xUnit v2 (`SemiPlot.Core.Tests` is merged into
   `SemiPlot.Tests`; the half-staged xUnit v3 packages are removed).
3. **Remove the comment slop** — the heavy per-method narration that violates the project's
   "comments only for genuinely non-obvious business logic" rule.

Explicitly **out of scope** (decided 2026-06-18): the Avalonia 11→12 bump and the xUnit v2→v3
migration. `ScottPlot.Avalonia` has no released build for Avalonia 12 — the latest release (5.1.58)
hard-crashes on it (`TypeLoadException` in `AvaPlot.CustomDrawOp.Render`), and the fix (PR #5233,
→ 5.1.59) is merged but unreleased with no committed date. We stay on the **currently pinned
Avalonia 11.3.8 + ScottPlot.Avalonia 5.1.57 + xUnit v2**; no package version changes except removing
the unused `xunit.v3` entry in PR2. Revisit the bump when 5.1.59 ships.

### Problem it solves

- The chart stutters and burns CPU/GC during hover and interaction because the cursor path calls
  `AvaPlot.Refresh()` (a full immediate-mode re-render of every pen's `Scatter` path + `FillY`
  polygon + axes + text) on **every** `PointerMoved`, `PointerExited`, delta-mode toggle, and delta
  placement — bypassing the view model's existing 30 FPS `RedrawRequested` coalescing seam.
- Two test projects exist only because of the xUnit v2/v3 boundary; with the v3 goal dropped the
  split is dead weight and the package set is half-migrated and contradictory.

## Context (from discovery)

- **Target branch**: `avalonia-scottplot-plan` (the Avalonia/ScottPlot implementation). `master` is
  the old WPF+WebView2/uPlot viewer and is unaffected.
- **Hover hot path**: `SemiPlot.UI/Chart/TrendChartView.axaml.cs`
  - `OnPointerMoved` → `MoveCursorTo` → `_plotControl.Refresh()` (per move, unthrottled).
  - `OnPointerExited`, `OnDeltaModeChanged`, delta placement also call `Refresh()` directly.
  - Existing throttled seam already in place and used by the pan/data path:
    `TrendChartViewModel.RedrawRequested` = `_redrawRequests.Sample(33ms).ObserveOn(uiScheduler)`,
    subscribed in the view to `_plotControl.Refresh()`. The hover path simply does not use it.
  - Cursor crosshair is a ScottPlot `VerticalLine` (`_cursorLine`); hover readout is a ScottPlot
    `Text` plottable (`ChartHoverReadout`). Both live *inside* the plot, so updating them requires a
    full plot render. Delta cursors are also `VerticalLine`s but are **click-driven** (infrequent).
  - Cursor state is already computed renderer-agnostically in the VM: `CursorTime`, `CursorValues`
    (via `ChartCursorReader`), exposed as observable properties. `ChartHoverReadout.BuildContent` is
    a pure string builder with existing unit coverage.
  - Pixel↔coordinate mapping available via `Plot.GetCoordinates(Pixel)` (used today) and its inverse
    `Plot.GetPixel(Coordinates)` (needed for the overlay).
- **Test projects** (both `net10.0-windows`):
  - `SemiPlot.Core.Tests` — `xunit.v3`, plain `[Fact]`, no UI reference. 8 files (decimation,
    navigation, scale, cursor/delta models, minimap geometry, synthetic catalog).
  - `SemiPlot.Tests` — `xunit` v2 + `Avalonia.Headless.XUnit`, references `SemiPlot.UI`,
    `[assembly: AvaloniaTestApplication]` in `TestAppBuilder.cs`, `[AvaloniaFact]` for UI tests.
  - Recorded rationale (CLAUDE.md): split exists solely because one project cannot hold both xUnit
    majors. Merging onto v2 is sound; the documented cost is that Core model tests get re-coupled to
    the UI build (lose independent runnability) — **an accepted trade-off** under this decision.
- **Packages** (`SemiPlot/Directory.Packages.props`): declares both `xunit` 2.9.3 and `xunit.v3`
  3.2.2 plus `xunit.runner.visualstudio` 3.1.5 (runner supports both majors). After unification the
  `xunit.v3` entry is removed.
- **Comment slop**: most methods/classes across `SemiPlot.UI` and `SemiPlot.Core` carry multi-line
  explanatory paragraphs. A subset are genuinely load-bearing invariants (e.g. the
  `TrendPenState` "centerPoints MUST be the exact instance ScottPlot holds" note, the single-writer
  `IsSticky` note) and must be **kept or converted to assertions**, not blindly deleted.

## Development Approach

- **Testing approach**: Regular (adjust/extend tests alongside each change; the suite already exists).
- Each PR is independent and must build + pass the full suite before the next.
- Backwards compatibility is **not** required (per user).
- Keep changes minimal and within the existing architecture (renderer-agnostic logic stays in
  Core/VM; only views touch `AvaPlot`).

## Testing Strategy

- **Unit tests**: required for behavioral changes (PR1). PR2 is a project-structure change whose
  deliverable is the full suite running green from a single project. PR3 is non-behavioral; the gate
  is the suite staying green plus `dotnet format`.
- No e2e/UI-automation harness exists; headless `[AvaloniaFact]` tests are the UI tier.
- **Known PR1 coverage gap (stated honestly):** the actual fix — removing the hover-path
  `Refresh()` from `TrendChartView.axaml.cs` — is in view code that is not headless-testable in the
  current setup, so it has **no automated regression guard**; it is verified by code-path inspection
  and manual run (Task 1.4) plus profiling (Post-Completion). What *is* unit-tested is the extracted
  pure projection helper (Task 1.2). Do not pretend a VM test covers the view-layer change.
- Per-PR gate command: `dotnet test SemiPlot/SemiPlot.slnx` must pass.

## Solution Overview

- **PR1 (perf)**: Stop the per-event full re-render. (a) Remove direct `Refresh()` calls from the
  hover/exit paths; only data/window/visibility changes drive ScottPlot redraws (already throttled).
  (b) Lift the **hover crosshair + readout** out of the plot into an Avalonia overlay (`Canvas` over
  the `AvaPlot`) drawn with Avalonia primitives, positioned by projecting VM cursor state through
  `Plot.GetPixel`. Moving the mouse updates only the overlay (cheap layout), never ScottPlot. The
  overlay is repositioned whenever the plot re-renders (pan/zoom/resize/live-edge) so it tracks the
  axis. Delta cursor lines stay as ScottPlot plottables (click-driven, not on the hot path).
- **PR2 (tests)**: Move the 8 `SemiPlot.Core.Tests` files into `SemiPlot.Tests`, drop the
  `SemiPlot.Core.Tests` project from disk and `.slnx`, remove `xunit.v3` from
  `Directory.Packages.props`, and update CLAUDE.md (rewrite the split rationale to a single-project
  v2 layout with the accepted trade-off documented).
- **PR3 (comments)**: Convention-driven comment sweep, preserving load-bearing invariant notes.

## Technical Details

### Overlay coordinate model (resolves the projection failure modes)

- **Project against the data rect, not the full control.** Position the overlay using
  `Plot.LastRender.DataRect` as the origin/bounds — `GetPixel` returns coordinates relative to the
  whole control (including axis/tick/label margins), so a data-area-sized Canvas fed full-control
  pixels would be offset by the left/top margin. Place the overlay Canvas over the full control and
  clamp drawing to `DataRect`.
- **Crosshair is a full-height vertical line clamped to `DataRect`.** Only the **X** projection
  matters; the line spans `DataRect.Top..DataRect.Bottom`. This deletes the multi-axis Y-mapping
  problem entirely (no need to pick a Y axis or read `YAxis.Max` as the old `ChartHoverReadout`
  did). The readout box anchors at `DataRect.Top` near the crosshair X.
- **DPI / scale contract — verify explicitly.** ScottPlot reports pixels in its own surface space;
  the Avalonia Canvas lays out in DIPs. The existing `AnchorAt` already round-trips
  `GetPosition(_plotControl)` (DIPs) → `GetCoordinates`, so the inverse `GetPixel` is expected to
  return DIP-space — but this MUST be confirmed at a non-1.0 render scale. If a mismatch appears,
  divide by `Plot.ScaleFactor` before placing on the Canvas. A positioning unit test pins the
  conversion (see Task 1.2); a manual high-DPI run is in Post-Completion.
- **Repositioning coupling (no drift during the 30 FPS window).** Reposition the overlay from the
  **same** `RedrawRequested` subscription that calls `_plotControl.Refresh()`, *after* the Refresh —
  not from raw pointer events — so the overlay always projects against the axis limits actually
  rendered. The hover overlay is already suppressed while dragging; during gesture-driven limit
  changes it stays hidden/re-projected post-Refresh rather than tracking stale limits per event.
- **Readout text**: keep `ChartHoverReadout.BuildContent` (pure, tested) as the string source; render
  it in an overlay `TextBlock`/`Border` instead of a ScottPlot `Text` plottable. Drop the plottable.

### xUnit v3→v2 source delta

- Plain `[Fact]`/`[Theory]` + `using Xunit;` + AwesomeAssertions are source-compatible across majors;
  the existing `SemiPlot.Core.Tests` files use only those (namespaces already `SemiPlot.Tests.Core.*`).
  During the move, check each file for any v3-only API (`Assert.*` additions, v3
  `ITestOutputHelper`/`TestContext`, `TheoryData` shape) and rewrite to v2. Expect little or no change.

## What Goes Where

- **Implementation Steps** (checkboxes): all code/test/doc changes in this repo.
- **Post-Completion** (no checkboxes): manual smoke-profile of hover under Rider/dotMemory to
  confirm the GC/CPU drop; revisit Avalonia 12 / xUnit v3 when ScottPlot 5.1.59 releases.

---

## Implementation Steps

> Each task ends by writing/adjusting tests and running the suite before the next task.

## PR1 — Cheap hover (throttle + cursor overlay)

### Task 1.1: Stop the per-event full re-render on hover

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`

- [x] Remove the direct `_plotControl.Refresh()` calls from `MoveCursorTo`, `OnPointerExited`, and
      `OnDeltaModeChanged` (delta *placement* may keep a single redraw — it is click-driven).
- [x] Ensure cursor state updates still flow to the VM (`MoveCursor` / `ClearCursor`) on pointer move.
- [x] Confirm the only ScottPlot redraws now originate from the VM's throttled `RedrawRequested`
      stream (data, window, visibility) — no per-pointer-event `Refresh()`. (Remaining `Refresh()`
      calls: RedrawRequested subscription, delta-cursor placement, and BeginPan — all click-driven.)
- [x] Verify by code-path inspection that no hover/exit path reaches `Refresh()`. (No automated test
      here — this is view-layer code with no headless harness; see the Testing Strategy coverage
      gap. The regression guard for this change is manual, recorded in Task 1.4.)
- [x] Run `dotnet test SemiPlot/SemiPlot.slnx` — must pass before next task. (231 passed, 0 failed.)

### Task 1.2: Extract and test the pure cursor-overlay projection

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartCursorOverlay.cs` (pure projection math only)
- Create: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartCursorOverlayTests.cs`

- [x] `ChartCursorOverlay` owns ONLY the positioning math worth isolating: given a cursor X
      coordinate, the `DataRect`, and the render scale, compute the crosshair line endpoints and the
      readout anchor in Canvas/DIP space, clamped to the data rect. It does NOT re-host the
      drag/delta suppression decision — that stays in the VM (`IsDragging`/`IsDeltaModeEnabled`,
      already tested). Keep this class free of `AvaPlot`/Avalonia-control dependencies so it is
      plain-`[Fact]` testable.
- [x] Write tests for the projection: X coordinate inside the data rect maps to the expected DIP X;
      coordinate outside is clamped/hidden; a non-1.0 render scale yields the correctly converted
      DIP position (the DPI contract); crosshair endpoints span the data-rect height.
- [x] Run `dotnet test SemiPlot/SemiPlot.slnx` — must pass before next task.

### Task 1.3: Wire the Avalonia cursor overlay into the view

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml` (add the overlay `Canvas` over the `AvaPlot`)
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs` (drive the overlay from VM cursor state)
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHoverReadout.cs` (reduce to the pure `BuildContent` text
      source; drop the ScottPlot `Text` plottable)
- Remove (from the plot): the `_cursorLine` `VerticalLine` hover crosshair (replaced by the overlay)

- [x] Add a transparent overlay `Canvas` (`IsHitTestVisible=false`) over the full control, holding a
      crosshair `Line` and a bordered readout `TextBlock`.
- [x] On `PointerMoved` (not dragging, not delta mode): update VM cursor state, then position the
      overlay via `ChartCursorOverlay` against `Plot.LastRender.DataRect`. Apply suppression from the
      VM flags (hidden while dragging or in delta mode).
- [x] Reposition the overlay from inside the `RedrawRequested` subscription (after `Refresh()`) and on
      `SizeChanged`, so it tracks pan/zoom/resize/live-edge without drift; do not reposition from raw
      pointer events during gestures.
- [x] Verify delta-cursor lines still render as ScottPlot plottables and are unaffected.
- [x] Keep `ChartHoverReadoutTests` green against `BuildContent` (text source unchanged).
- [x] Run `dotnet test SemiPlot/SemiPlot.slnx` — must pass before next task. (92 + 143 = 235 passed.)

### Task 1.4: PR1 acceptance

- [x] Confirm by code path that hover produces no ScottPlot `Refresh()` calls. (Only 3 `Refresh()`
      calls remain in `TrendChartView.axaml.cs`, all click-driven/throttled: line 113 RedrawRequested
      subscription, line 180 delta-cursor placement, line 285 BeginPan. `OnPointerMoved`/`OnPointerExited`
      reach only overlay updates — no `Refresh()`.)
- [x] manual run (skipped - not automatable in headless agent; covered by ChartCursorOverlay unit tests
      for the projection math + recorded for human verification)
- [x] `dotnet format SemiPlot/SemiPlot.slnx` clean; full suite green. (Format: only the 4 known
      pre-existing CHARSET files are dirty — BandDegeneracy.cs, HistoryColumnTarget.cs and their two
      test files; no PR1-touched file is unclean. Suite: 92 + 143 = 235 passed, 0 failed, 0 skipped.)

## PR2 — Unify test projects on xUnit v2

### Task 2.1: Move Core tests into SemiPlot.Tests

**Files:**
- Create: `SemiPlot/SemiPlot.Tests/Core/...` (8 files relocated from `SemiPlot.Core.Tests`)
- Modify: each moved file's namespace/usings as needed (v3→v2 if any v3-only API is used)

- [x] Relocate the 8 `SemiPlot.Core.Tests` files into `SemiPlot.Tests` under a `Core/` (and
      `Chart/`) folder mirroring their current layout; fix namespaces. (9 files moved via `git mv`:
      `Chart/CursorReadoutModelTests.cs`, `Chart/DeltaCursorModelTests.cs`,
      `Core/Data/{MinMaxDecimator,RandomStubDataProvider,SyntheticPenCatalog}Tests.cs`,
      `Core/Trends/{AggregationLayer,MinimapGeometry,PenScaleModel,TrendNavigationModel}Tests.cs`.
      Namespaces already `SemiPlot.Tests.Core.*` / `SemiPlot.Tests.Core.Chart` — no change needed;
      `SemiPlot.Tests.csproj` already references Core + Stub + UI, so no csproj edit.)
- [x] Audit each moved file for xUnit v3-only APIs; rewrite to v2 equivalents (expected: minimal).
      (None found — no `TestContext`/`ITestOutputHelper`/`Xunit.v3`/`TheoryData`/v3-only `Assert.*`;
      all files use plain `using Xunit;` + AwesomeAssertions. Zero rewrites.)
- [x] Keep `[Trait(...)]` categories intact so existing `--filter` commands still work. (All
      `[Trait("Component","Core")] [Trait("Area","Data")] [Trait("Category","Unit")]` preserved.)
- [x] Verify run behavior after the merge: run `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`
      twice. (Run 1: 235 passed/0 failed/0 skipped. Run 2: 235 passed/0 failed/0 skipped, identical —
      no nondeterminism/interleaving between plain `[Fact]` Core tests and `[AvaloniaFact]` headless UI
      tests under the single `[assembly: AvaloniaTestApplication]`. No collection/parallelization change
      needed.)
- [x] Run `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` — all moved tests green.
      (235 passed; 92 relocated Core + 143 existing UI. Full `dotnet test SemiPlot/SemiPlot.slnx` also
      green: empty `SemiPlot.Core.Tests` builds clean as a no-test assembly, deleted in Task 2.2.)

### Task 2.2: Delete SemiPlot.Core.Tests and clean packages/solution

**Files:**
- Delete: `SemiPlot/SemiPlot.Core.Tests/` (project + remaining files)
- Modify: `SemiPlot/SemiPlot.slnx` (remove the project entry)
- Modify: `SemiPlot/Directory.Packages.props` (remove `xunit.v3`; keep `xunit` 2.9.3 +
      `xunit.runner.visualstudio` 3.1.5)

- [x] Remove `SemiPlot.Core.Tests` from `.slnx` and delete the project directory. (`.slnx` entry
      removed; `.csproj` `git rm`'d; directory deleted incl. obj/bin artifacts.)
- [x] In `Directory.Packages.props`, remove ONLY the `xunit.v3` `PackageVersion` line. Do not touch
      `Avalonia` (11.3.8), `ScottPlot.Avalonia` (5.1.57), or any other pin — they stay frozen.
      (Only the `xunit.v3` 3.2.2 line removed; xunit 2.9.3 + runner 3.1.5 + all other pins unchanged.
      Grep over all `*.{slnx,csproj,props,targets,cs}` for `SemiPlot.Core.Tests`/`xunit.v3`: no matches.)
- [x] Restore/build the solution; confirm no dangling references. (`dotnet restore` + `dotnet build
      SemiPlot/SemiPlot.slnx`: 0 errors; only the pre-existing transitive Tmds.DBus.Protocol NU1903
      warning, unrelated to this change.)
- [x] Run full suite from the single project: `dotnet test SemiPlot/SemiPlot.slnx`. (235 passed,
      0 failed, 0 skipped — all from SemiPlot.Tests.dll.)

### Task 2.3: Update docs for the single-project layout

**Files:**
- Modify: `CLAUDE.md` (Test section)

- [x] Rewrite the "Tests are split into two projects" section to describe one project on xUnit v2.
- [x] Replace the "split is deliberate / Backlog: unify on v3" text with the accepted trade-off:
      Core model tests now build against the UI project (no independent run); v3/Avalonia-12
      unification deferred until `ScottPlot.Avalonia` ships an Avalonia-12 build.
- [x] Update the `dotnet test` example commands (drop the `SemiPlot.Core.Tests` line).
- [x] Run full suite — must stay green. (235 passed, 0 failed, 0 skipped.)

## PR3 — Comment cleanup

### Task 3.1: Convention-driven comment sweep

**Files:**
- Modify: source files across `SemiPlot.UI`, `SemiPlot.Core`, `SemiPlot.DataSource.Stub`
      (no behavioral edits)

- [x] Remove per-method/per-class narration and process notes that the code + naming already convey.
- [x] **Preserve (or convert to assertions)** two categories of load-bearing comment, do NOT delete:
      (a) **invariants** — shared buffer-instance contract in `TrendPenState`, single-writer
      `IsSticky` note, axis-pinning invariants, history-sequence latest-window-wins guard; and
      (b) **non-obvious third-party behavior notes** — e.g. the `band.MarkerStyle = MarkerStyle.None`
      "Polygon.Render walks every vertex" note and the `ApplyLocalTimeTicks` "assign in place rather
      than `DateTimeTicksBottom()` which replaces the axis" note. These explain ScottPlot/Avalonia/Rx
      quirks, not the local code, and are load-bearing.
- [x] Keep comments English-only; remove any `// TODO`/process comments.
- [x] Gate the change as comment-only: `git diff` must show no change to any non-comment token
      (i.e. ignoring comment-only lines, the diff is empty). (Verified: across all 61 source files,
      every changed non-blank line is a `//` comment; net -235 comment lines, zero code-token changes.
      `dotnet format` additionally added the UTF-8 BOM to the 4 pre-existing CHARSET files —
      BandDegeneracy.cs, HistoryColumnTarget.cs and their two test files — as expected.)
- [x] Run `dotnet format SemiPlot/SemiPlot.slnx` and the full suite — no behavioral change, green.
      (235 passed, 0 failed, 0 skipped — identical to baseline.)

### Task N: Finalize

- [x] Verify all three PRs build, format clean, and pass `dotnet test SemiPlot/SemiPlot.slnx`.
- [x] all three PRs implemented on branch perf-hover-overlay-test-unify; merge pending human review

## Post-Completion
*Informational — manual / external, no checkboxes.*

**Manual verification:**
- Profile hover under Rider / dotMemory before vs after PR1: confirm the per-move allocation/CPU
  spike is gone and redraws are bounded to ~30 FPS only on data/window changes.
- Compare total resource use against the `master` web build **system-wide** (including
  `msedgewebview2.exe` children) for a fair picture, not just the managed process.

**Deferred (external trigger):**
- When `ScottPlot.Avalonia 5.1.59` (Avalonia 12 support, PR #5233) is released to NuGet, open a
  follow-up plan for the Avalonia 11→12 bump + xUnit v2→v3 migration + re-unification on v3.
