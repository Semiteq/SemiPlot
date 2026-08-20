# Render and input guards

## Overview

`avalonia-12-bump` moves seven Avalonia packages from 11.3.8 to 12, `ScottPlot.Avalonia` from 5.1.57
to 5.1.59, and `SemiPlot.Tests` from xunit 2 to xunit 3. Its risk is rendering and input regression,
and the demo stand that would have caught those by eye does not exist until this roadmap finishes.

This slice puts the instruments in place first. Green on Avalonia 11 and carried across the bump
unchanged, they assert the two stacks behave alike. Written inside the bump they would only describe
the new one.

Tests only. No production file changes.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice ui-render-and-input-guards

- `SemiPlot/SemiPlot.Tests/UI/Chart/ChartAxisRegionTests.cs:65` and
  `ChartAxisRegionEditTests.cs:113` already call `viewModel.Plot.RenderInMemory(PlotWidth, PlotHeight)`
  and read `Plot.RenderManager.LastRender.Layout.DataRect`. ScottPlot renders through SkiaSharp with
  no Avalonia in the loop, so the render half of this slice has a proven foothold.
- `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs` configures `UseHeadless(new AvaloniaHeadlessPlatformOptions())`
  — default headless drawing, no Skia. Avalonia's own `CaptureRenderedFrame` therefore yields no real
  pixels, which is why the render assertions go through ScottPlot's rasteriser instead of Avalonia's.
- `SemiPlot/SemiPlot.Core/Trends/MinMaxDecimator.cs` emits `double.NaN` for a gap column, which is
  what the renderer must break the line on.
- Nothing in the repository sends a pointer event through a headless window. `ChartPressRouter` is
  tested as a pure function, so Avalonia's input pipeline — capture, wheel, routing — is exercised by
  no test at all.

ASSUMPTION: ScottPlot 5.1.57's `Plot.GetImage(width, height)` exposes pixel access usable from a
test, whether directly or through an export to a byte buffer. `RenderInMemory` is confirmed in use;
the pixel-reading API is not. Establish it at implementation time and record which call it is. If no
pixel access exists, the render guard falls back to asserting the plottable's data — that `NaN`
survives into what the renderer receives — and the task says so rather than pretending to see pixels.

## Development Approach

- **testing approach**: Regular. These are tests, so "the code" is the test.
- **CRITICAL: no production file changes.** A guard that needed one would be describing behaviour
  this slice is not allowed to add.
- All tests land in `SemiPlot.Tests` — xunit v2, AwesomeAssertions, all three traits.
  `[AvaloniaFact]` for anything touching a window; plain `[Fact]` for the ScottPlot renderer, which
  needs no Avalonia.
- **Sample bands, never compare bytes.** A pixel assertion that fails on antialiasing is a test that
  gets deleted at the bump, which defeats the slice.

## Acceptance Evidence

**Evidence 1 — a gap is visible to a test.** The render guard fails when the gap column is removed
from the envelope, and fails when the line is drawn straight across it. Demonstrate both by editing
the input, not the assertion, and record what the failure said.

**Evidence 2 — the input pipeline is exercised.** The pointer guard fails when the drag handler is
disconnected. Demonstrate it and record the failure.

**Evidence 3 — nothing regressed.** `dotnet test SemiPlot.slnx` reports zero failures. Measured at
`9c4a4d9`, the branch point: `SemiPlot.Tests` 361 passed / 0 skipped, `SemiPlot.Tests.Data` 397
passed / 0 skipped, with Docker running and `semibase` on `PATH`.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**Evidence 4 — no production file changed.** `git diff master...HEAD --name-only` lists nothing
outside `SemiPlot/SemiPlot.Tests/` and `docs/`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix, blockers with ⚠️

## Solution Overview

**The render guard reads the rasteriser, not the screen.** ScottPlot's `Plot` renders to a bitmap
through SkiaSharp with no Avalonia involved, so a test can build an envelope, render it at a fixed
size, and look at the pixels inside `LastRender.Layout.DataRect`. A gap column carrying `NaN` must
leave a vertical band of background where the line would otherwise cross. That is the one automated
check that can see the failure this roadmap calls its worst — a break drawn as a straight line — and
it guards what a ScottPlot minor version can change without announcing it: how `Scatter` and `FillY`
treat `NaN`.

Sampling is by band, not by byte. Take a column of pixels at the gap's centre and assert none carries
the line colour; take columns a fixed distance either side and assert some do. Antialiasing, font
metrics and theme changes move bytes without moving that answer, which is what lets the assertion
survive a two-version bump.

**The pointer guard drives Avalonia's own pipeline.** A headless window hosting the chart view,
rendered once so `LastRender.Layout.DataRect` is populated, then a drag, a wheel and a capture loss
sent as real input events. The assertions are on the navigation window: it moved, it zoomed, and no
drag remained in progress. This is the only place Avalonia's capture semantics and event routing are
exercised, and a major version is exactly where those move.

**Golden images are not here, deliberately.** A two-version bump legitimately changes pixels, so a
baseline captured now would fail for benign reasons and be regenerated at the bump — at which point
it has verified nothing about the transition.

## Implementation Steps

### Task 1: See a gap through the rasteriser

**Files:**
- Create: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs`

- [x] establish how ScottPlot 5.1.57 exposes pixels from a render, and record the call in the file's
      header comment; if none does, fall back as the Context ASSUMPTION describes and say so
- [x] build an envelope with a `NaN` gap band through `MinMaxDecimator`, render it at a fixed size,
      and assert the gap's centre column inside `DataRect` carries no line colour
- [x] assert the columns a fixed distance either side of the gap do carry it, so the test cannot pass
      on a blank plot
- [x] write a second test that a continuous envelope leaves no such band, so the first cannot pass on
      any rendering
- [x] verify by editing the input: removing the gap must fail the first test, and a continuous
      envelope must fail if asserted as gapped. Record both failure messages
- [x] run tests — must pass before task 2

### Task 2: Drive the input pipeline

**Files:**
- Create: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartPointerInputTests.cs`

- [x] show a headless window hosting the chart view and render once so `DataRect` is populated
- [x] send a pointer press, a move and a release, and assert the navigation window panned by the
      distance those coordinates represent
- [x] send a wheel event and assert the window zoomed
- [x] send a capture loss mid-drag and assert no drag remains in progress
- [x] verify by disconnecting the drag handler and confirming the pan test fails; record what it said
- [x] run tests — must pass before task 3

### Task 3: The minimap's drag

**Files:**
- Create or modify: `SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapPointerInputTests.cs`

- [x] drive the minimap's drag-to-navigate through the same headless path and assert the chart window
      followed
- [x] if the minimap's input path turns out to be covered adequately by existing tests, say so and
      mark this task complete without adding a duplicate — it is not: `MinimapViewModelTests` calls
      `NavigateToFraction` directly and nothing constructs `MinimapView`, so the test was written
- [x] run tests — must pass before task 4

### Task 4: Verify acceptance criteria

- [x] run the full suite and record the counts
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0
- [x] confirm `git diff master...HEAD --name-only` lists nothing outside `SemiPlot/SemiPlot.Tests/`
      and `docs/`
- [x] record every negative check from Tasks 1 and 2 and what each failure said

**Measured.** `dotnet test SemiPlot.slnx`: `SemiPlot.Tests` 368 passed / 0 skipped / 0 failed,
`SemiPlot.Tests.Data` 397 passed / 0 skipped / 0 failed, with Docker running and `semibase` on
`PATH`. Against the `9c4a4d9` branch point that is +7 in `SemiPlot.Tests` (2 render, 3 chart pointer,
2 minimap pointer) and unchanged in `SemiPlot.Tests.Data`.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**Scope.** `git diff master...HEAD --name-only` lists four files:
`SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs`,
`SemiPlot/SemiPlot.Tests/UI/Chart/ChartPointerInputTests.cs`,
`SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapPointerInputTests.cs`,
`docs/plans/20260820-ui-render-and-input-guards.md`. Each of the three commits carries the same two
paths and nothing else, so no negative check's temporary production edit leaked. The working tree is
clean.

**Negative checks and what each failure said.**

| Check | Edit | Failure |
| --- | --- | --- |
| Task 1, gap test on a gapless input | fed the continuous series into the gap test | `Expected ColumnCarriesPenColor(pixels, dataRect, gapCentre) to be False because a NaN column must break the curve, leaving background where the line would cross, but found True.` |
| Task 1, continuous test on a gapped input | fed the gapped series into the continuous test | `Expected columnsWithoutPenColor to be empty because an envelope with no NaN column draws an unbroken curve across the whole data area, but found at least one item {349}.` |
| Task 2, drag handler disconnected | removed `_plotControl.PointerMoved += OnPointerMoved;` from `TrendChartView.InitializeComponent` | pan: `Expected viewModel.Navigation.From to be within 1ms from <2026-08-20 09:00:05.9717488> because the drag pans by the time distance its pixels cover, but <2026-08-20 08:51:33.5117488> was off by 8m, 32s and 460ms.` capture loss: `Expected viewModel.CursorTime to have a value because the move still reaches the view, so the unchanged window is not an unrouted event, but found <null>.` The wheel test stayed green, correctly — it does not use `PointerMoved` |
| Task 3, minimap move handler disconnected | commented out `_stripCanvas.PointerMoved += OnPointerMoved;` in `MinimapView.axaml.cs` | `Expected WindowCenter(navigation) to be within 1s from <2025-12-29 04:48:00> because a move while the drag holds must keep recentering the chart, but <2025-12-27 02:24:00> was off by 2d, 2h and 24m.` |

Evidence 1 asks for two demonstrations, the gap column removed and the line drawn straight across.
One edit was made, the two series swapped between the two render tests, and it was run in each
direction — the first two rows above. The gapless envelope *is* the line drawn straight across, so
the swap covers both demonstrations; no separate edit was made for the second.

Every edit was reverted with `git checkout` and the suite was green again before the task committed.

### Task 5: [Final] Record what the guards cover

**Files:**
- Modify: `docs/architecture/bench.md`

- [x] add to the application-bench section what these guards catch and what they miss, so the bump's
      plan can lean on them rather than rediscover them
- [x] move this plan to `docs/plans/completed/` — not done here. Archiving the plan is delivery work
      and belongs to whoever ships the branch; the execution run leaves the file in `docs/plans/`.

## Post-Completion

*Items requiring manual intervention — no checkboxes, informational only*

**Manual verification.** None. This slice adds no behaviour; the tests are their own evidence.

**What these guards still do not cover**, and the bump's plan must say so rather than imply otherwise:
the Avalonia 12 Win32 backend — windowing, DPI, real cursor changes, the render-thread interplay —
is exercised by nothing headless, and visual legibility is not a machine question. Both wait for the
demo stand.

**Remaining slices.** After this slice: avalonia-12-bump, postgres-gap-reconstruction,
postgres-live-edge-and-demo.

**Executed by exec:**

- branch: ui-render-and-input-guards

## Verify it yourself

**The suite.** `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` 368 passed / 0 skipped,
`SemiPlot.Tests.Data` 397 passed / 0 skipped, zero failures, with Docker running and `semibase` on
`PATH`. `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**No production file changed.** `git diff master...HEAD --name-only` lists three test files,
`docs/architecture/bench.md` and this plan. Nothing else. Each task commit carries its own test file
alone, so the three temporary production edits made for negative checks left no trace.

**The render guard sees a gap.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ChartGapRender"
```

Two tests. The first renders an envelope carrying a `NaN` band and asserts the gap's centre column
inside `DataRect` holds no pen-coloured pixel while columns either side do. The second renders a
continuous envelope and requires every column to carry pen colour, so the first cannot pass on a
blank or failed render — and it proves the channel order at the same time, because read as BGR the
line would score negative everywhere.

To see it fail, swap the two tests' input series. A gapless envelope fed to the first reports the
gap's centre column carrying pen colour; a gapped envelope fed to the second names the column that
does not.

**The pointer guards drive Avalonia's own pipeline.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PointerInput"
```

Five tests across the chart and the minimap. They show a headless window, render once so the plot's
`DataRect` is populated, then send real `MouseDown` / `MouseMove` / `MouseUp` / `MouseWheel` events
and assert what the navigation window became. The pan expectation is computed from plot coordinates
before any event is sent, so it cannot be a copy of the observed result.

To see them fail, comment out `_plotControl.PointerMoved += OnPointerMoved;` in
`TrendChartView.InitializeComponent`. The pan test reports the window off by about eight minutes and
the capture-loss test loses its hover witness, while the wheel test stays green — which is what makes
it a real negative check rather than a blanket break.

**What these guards are for.** `avalonia-12-bump` moves Avalonia 11.3.8 to 12 and
`ScottPlot.Avalonia` 5.1.57 to 5.1.59 with no demo stand to look at. Green here and carried across
that bump unchanged, these tests assert the two stacks behave alike. `docs/architecture/bench.md`
records what each covers, what neither covers, and the scheduler rule that hangs a test host
silently.
