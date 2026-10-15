# Drop the active pen's emphasis on an empty click

## Overview

- A left click on the chart panel that lands farther than 6 px from every visible line removes the active
  pen's emphasis: its line draws 1 px wide like the others. The pen stays active. Its sidebar row stays
  marked, its axis stays drawn, and Δy, the axis scale panel, Ctrl + wheel, Ctrl + drag and the wheel over
  the axis keep acting on it.
- A click on any pen's line, the active pen's own line included, and a click on any sidebar row, the
  active pen's own row included, make that pen active and emphasized.
- The emphasis behaves like keyboard focus over a selection: the selection (`ActivePenId`) is kept, and only
  the mark on the chart goes away.
- The window opens with the first visible pen active and not emphasized: its row is marked and its axis drawn,
  and its line is 1 px wide until the operator picks a pen.
- Today a click farther than 6 px from every line changes nothing
  (`docs/architecture/trend-interaction.md:231-232`), so the operator cannot see every pen at one width
  without first switching the active pen.

## Context (from discovery)

- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:88-102`: `ActivePenId`, private setter, raises
  `DrawnPenId` and refreshes the delta readout. Writers: `SetActivePen` (`:325-340`), `SettleActivePen`
  (`:521-529`) and `ActivateAVisiblePen` (`:533-549`).
- `TrendChartViewModel.cs:325-340`: `SetActivePen` refuses a pen `MayDrawAxisFor` (`:561-564`) rejects. For an
  accepted pen it writes `ActivePenId`, calls `EmphasizeActivePen()`, `ApplyAxisModel()` and
  `RequestRedraw()`. It runs all of this for the id that is already active too: the `ActivePenId` setter
  returns early (`:93-96`), the rest still runs.
- `TrendChartViewModel.cs:342-348`: `PickPenAt` hands a hit to `SetActivePen` and does nothing on a miss.
- `TrendChartViewModel.cs:553-559`: `EmphasizeActivePen()` takes `lock (Plot.Sync)` around
  `_penSet.Emphasize(ActivePenId)`. Callers: `SetActivePen` (`:335`) and `SetPenVisibility` (`:283`). The
  catalogue apply calls `_penSet.Emphasize(ActivePenId)` directly inside its own lock block (`:229-239`).
- `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs:75-88`: `Emphasize(int activePenId)` sets 2 px on the active line
  and 1 px on the others through `EnvelopeLine.SetLineWidth`, then `_plot.MoveToTop` on the active line.
- `SemiPlot/SemiPlot.UI/Legend/TrendLegendRowViewModel.cs:31-34`: the row's `IsActive` reads only
  `ActivePenId`. `:54-57`: a row click calls `SetActivePen`. The sidebar needs no change.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:430-444`: the release calls `PickPenAt` only for a press
  routed `PanOrPickPen` that never passed the click slop. `ChartPressRouter` routes the axis region to
  `EditAxisScale`, Ctrl to `ShiftActivePen` and delta mode to `PlaceDeltaCursor`, so those presses never reach
  `PickPenAt` (`ChartPointerInputTests.cs:361-387` pin delta and the axis region through `ActivePenId`).
- `docs/architecture/charting.md:40-47`: `EnvelopeLine.DistanceTo` returns infinity for a click outside the
  data area. A click on the time axis labels or the plot margin is therefore a miss in `PickPenAt` already.
  The same holds before the first frame and on a chart with no pens; every miss clears the flag.
- `MainWindow/PenCatalogueApplier.cs:35-38` runs `ApplyAsync` through `Observable.FromAsync(..., uiScheduler)`,
  so `ApplyCatalogue`, `SetActivePen` and `PickPenAt` all run on the UI thread. A plain field holds the flag.
- `docs/architecture/charting.md:50`: the width is written through `EnvelopeLine.SetLineWidth` and read only by
  `Render`; nothing reads it back. Tests prove the width through rendered pixels, as
  `SemiPlot.Tests.Unit/UI/Chart/ChartActivePenRenderTests.cs:34-59` does with an ink measure (`InkOf`,
  `:77-87`, private).
- `SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs:123-145`: `RenderTwoFlatPens` loads history straight into
  each pen state, calls `SetActivePen`, then pins the Y limits with raw `Plot.Axes.SetLimitsY`. Both pens stay
  `ScaleMode.Auto` with no envelope, so any later `ApplyAxisModel` resets both axes to `(0, 1)`
  (`SemiPlot.Core/Trends/PenScaleModel.cs:52-55`, `:148-151`) and the lines leave the data area. A test that
  calls `SetActivePen`, a `PickPenAt` hit, `SetPenVisibility` or `ApplyCatalogue` after it must first pin both
  pens `Manual` through `viewModel.SetAxisLimits`, as `ChartPointerInputTests.CreateViewModelWithFixedScales`
  does.
- `TrendChartViewModelTests.cs:2435-2461`: the private wrapper `RenderTwoFlatPens(firstValue, secondValue,
  activePenId)` and the helpers `PixelOnLine` and `DistanceTo` the `PickPenAt` tests use (`:168-217`). Its pens
  are `FakeDataProvider.FirstPen` (`#ff0000`) and `SecondPen` (`#00ff00`)
  (`SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:23-24`). `StrokeProbe` names `Red = 0` and `Blue = 2`
  only (`StrokeProbe.cs:8-9`).
- Pointer tests: `docs/architecture/testing-strategy.md:323-327` says the headless `AvaPlot` renders nothing.
  The only `LastRender` in `ChartPointerInputTests` is the `RenderInMemory` at the control's bounds in
  `ChartViewTestBuilder.cs:63`. A render at any other size replaces the `DataRect` that the tests' point maths
  and the production `PickPenAt` read. The fixture's pens are the same red and green pair; pen 2 is a flat line
  with a pixel helper (`PixelOnSecondPensLine`), pen 1 is an envelope with none.
- The `ActivePenId` assertions of `TrendChartViewModelTests.PickPenAt_EmptySpace_LeavesTheActivePen`
  (`:178-188`) and `ChartPointerInputTests.AClickOnEmptySpace_KeepsTheActivePen` (`:268-282`) stay true. The
  z-order tests at `TrendChartViewModelTests.cs:100-152` stay true.
- Size: `TrendChartViewModel.cs` is 782 lines and `TrendChartView.axaml.cs` 572, both past the 300-line limit.
  This plan adds about fifteen lines to the view model and none to the view. Splitting either file is out of
  scope.

## Development Approach

- **testing approach**: TDD. Task 2 writes the view-model and pointer tests, runs them red, then changes the
  production code.
- Complete each task fully before the next; all tests pass before moving on.
- Every task adds or updates tests in the same task.
- Update this plan when scope changes during implementation.

## Testing Strategy

- **unit tests**: `TrendChartViewModelTests` holds every writer test of the flag, beside the `PickPenAt` tests
  and their pixel helpers. `ChartActivePenRenderTests` keeps its three render tests on the moved ink measure.
- **headless pointer tests**: `ChartPointerInputTests` through Avalonia's real input pipeline, one test per
  gesture that reaches `PickPenAt`. The routes that never reach it (delta, axis region, Ctrl) stay pinned by
  their existing `ActivePenId` assertions. Every new click goes through `HeadlessInput.Click`; the drag stays on
  raw `MouseDown`, `MouseMove` and `MouseUp`, like the existing drag tests.
- Traits stay `Component=UI`, `Area=Chart`, `Category=Unit`.

## Acceptance Evidence

Reproduce today (verified 2026-10-15 by reading `TrendChartViewModel.cs:342-348`: a miss in `PickPenAt` writes
nothing): after a click in empty space the active pen still draws 2 px wide.

Automated, red against the code before Task 2's production change and green after it:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartPointerInputTests"
```

Each outcome reads `ChartTestBuilder.InkRatio` (Technical Details) after the gesture:

1. `AClickOnEmptySpace_KeepsTheActivePenAndDropsItsEmphasis`: `ActivePenId == 1` and the red-to-green ratio is
   below 1.25. Red today.
2. `AClickBelowTheDataArea_DropsTheEmphasis`: a click on the time axis labels, below `DataRect.Bottom`, gives
   a red-to-green ratio below 1.25. Red today.
3. `AClickOnALineAfterAnEmptyClick_EmphasizesThatPen`: an empty click gives a red-to-green ratio below 1.25,
   then a click on pen 2's line gives `ActivePenId == 2` and a green-to-red ratio above 1.5. Red today on
   its first assertion.
4. `ADragThroughEmptySpace_KeepsTheEmphasis`: a press in empty space, a drag past the slop and a release keep
   the red-to-green ratio above 1.5. Green before and after, as a guard against a release that clears after a
   pan.

The same filter for `TrendChartViewModelTests` covers the writers (Task 2).

Full gate: `dotnet test SemiPlot.slnx` green; `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

Manual smoke on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`, 8 pens):

1. The window opens with every line 1 px wide; the first pen's sidebar row is marked and its axis is drawn.
2. A click on the sidebar row of the already active pen makes its line thick.
3. A click in empty plot area turns the thick line thin. The sidebar row stays marked, the axis stays.
4. A click on the time axis labels does the same.
5. After an empty click, Ctrl + drag still shifts the active pen and the line stays thin.
6. After an empty click, hiding the active pen in the sidebar moves the axis to the next pen, and that line
   stays thin.
7. After an empty click, a drag pans and the line stays thin; with the line thick, a drag keeps it thick.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix; document blockers with ⚠️ prefix.
- Keep this plan in sync with the work done.

## Solution Overview

- **One flag, one owner.** `TrendChartViewModel` holds a private `_isActivePenEmphasized`, `false` at
  construction, so a new window draws the active pen thin. `SetActivePen` sets it for every accepted pen.
  `PickPenAt` clears it on every miss. `ApplyCatalogue` clears it when a catalogue leaves no pen. No other code
  writes it. It is not a public property and raises no notification: nothing outside the view model reads it,
  and the sidebar keeps reading `ActivePenId`.
- **The automatic slot moves keep the flag.** `SettleActivePen` and `ActivateAVisiblePen` move the slot without
  an operator choice and do not write the flag: a thin line stays thin on the pen that takes the slot. The one
  exception is a catalogue that leaves no pen: the chart returns to the state of a new window, and the first
  pen a later read seats starts thin.
- **Width follows the flag, order does not.** `ChartPenSet.RestyleActiveLine(int activePenId, bool isEmphasized)`
  sets 2 px on the active line only when `isEmphasized`, 1 px everywhere else, and moves the active line to the
  top in both cases. The z-order tests stay valid, and the active pen still wins the shared pixels.
- **One call site for the width rule.** The private `EmphasizeActivePen()` becomes `RestyleActivePen()`, since
  it now also thins, and the catalogue apply calls it inside its existing `Plot.Sync` block instead of calling
  `_penSet.RestyleActiveLine` itself. `Monitor` is re-entrant, so the nested lock is safe.
- **No new route.** The miss branch lives in `PickPenAt`, which only the `PanOrPickPen` release reaches.
  Delta mode, Ctrl and the axis region already route elsewhere, and a drag past the slop never calls it. A click
  outside the data area is a miss because `DistanceTo` returns infinity there.
- **The lock rule holds.** The flag write sits outside `Plot.Sync`; the clear calls `RestyleActivePen()`, which
  locks only around `_penSet.RestyleActiveLine`, then `RequestRedraw()`. No notification is raised.

## Technical Details

- `ChartPenSet.RestyleActiveLine(int activePenId, bool isEmphasized)`: the active line gets
  `isEmphasized ? EnvelopeLine.ActiveLineWidth : EnvelopeLine.DefaultLineWidth`; `MoveToTop` as today.
- `TrendChartViewModel`:
  - `private bool _isActivePenEmphasized;`
  - `SetActivePen`: after the `MayDrawAxisFor` check, `_isActivePenEmphasized = true;` before
    `RestyleActivePen()`.
  - `PickPenAt`: on a miss, a private `DropEmphasis()` returns when the flag is already `false`; otherwise it
    clears the flag, calls `RestyleActivePen()` and
    `RequestRedraw()`. `PickPenAt` opens with `ObjectDisposedException.ThrowIf`, so a late miss and a late hit
    both throw.
  - `ApplyCatalogue` clears the flag when the catalogue leaves no pen (`HasNoPens`), between
    `SettleActivePen()` and `RestyleActivePen()`.
  - `RestyleActivePen()` passes `_isActivePenEmphasized` to `_penSet.RestyleActiveLine`; the catalogue apply
    calls it.
- Test helpers:
  - `StrokeProbe` gains `Green = 1`, the `InkOf(pixels, dataRect, column, channel)` measure moved from
    `ChartActivePenRenderTests.cs:77-87`, and the `ThinInkRatio` (1.25) and `ThickInkRatio` (1.5) bounds every
    ink assertion reads.
  - `ChartTestBuilder.InkRatio(TrendChartViewModel viewModel, int numeratorChannel,
    int denominatorChannel)` reads `Plot.RenderManager.LastRender.FigureRect`, renders `viewModel.Plot` with
    `GetImage` at that same size, takes the column at the middle of the data area and returns
    `InkOf(numerator) / InkOf(denominator)`. It asserts that `LastRender.Layout.DataRect` is the same before and
    after its render, so a measurement in mid-test leaves the pointer maths where it was. It asserts that the
    numerator and the denominator ink are both above zero, so a line clipped out of the data area or absent
    from the probed column fails as such, not as a ratio of zero or NaN.
  - `ChartTestBuilder.RenderTwoFlatPens` splits into `AddTwoFlatPens(viewModel, first, second, windowSeconds)`,
    which adds the pens through `AddFlatPen` and sets the X limits, and the existing render. The empty-chart
    test calls `PickPenAt` on a view model from `CreateViewModel` before `AddTwoFlatPens`.
  - `ChartTestBuilder.AddFlatPen(viewModel, pen, windowEnd, value)` loads one flat line and pins the pen
    `Manual` at `FlatPlotAxisMin`..`FlatPlotAxisMax` through `viewModel.SetAxisLimits`, so an `ApplyAxisModel`
    in a test keeps the line inside the data area.

## What Goes Where

- **Implementation Steps**: code, tests and `docs/architecture` updates in this repository.
- **Post-Completion**: the stand smoke.

## Implementation Steps

### Task 1: Ink measure and the width parameter

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/StrokeProbe.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartActivePenRenderTests.cs`

- [x] move `InkOf` into `StrokeProbe` and add `Green = 1`; the three `ChartActivePenRenderTests` stay green on
  the moved measure
- [x] add `isEmphasized` to `ChartPenSet.Emphasize`; rename `EmphasizeActivePen()` to `RestyleActivePen()`,
  pass `true`, and route the catalogue apply through it; behaviour is unchanged
- [x] run the `ChartActivePenRenderTests`, `TrendChartViewModelTests`, `TrendChartCatalogueTests` and
  `TrendChartRenderThreadTests` filters; must pass before Task 2

### Task 2: The view model owns the flag

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`

- [x] add `ChartTestBuilder.InkRatio` and `AddTwoFlatPens`; pin the `Manual` scales in
  `TrendChartViewModelTests.RenderTwoFlatPens`; the existing `PickPenAt` tests stay green
- [x] write the view-model tests in `TrendChartViewModelTests`, pens at two values, pen 1 active:
  - `PickPenAt` on a pixel between the lines: red-to-green ratio below 1.25, `ActivePenId == 1`, pen 1's line
    still last in `Plot.PlottableList`, and exactly one `RedrawRequested` signal; a second miss raises none
  - `PickPenAt` at a pixel below `DataRect.Bottom`: ratio below 1.25
  - after the miss, `SetActivePen(1)` (the sidebar re-click): ratio above 1.5
  - after the miss, `PickPenAt` on pen 1's line: ratio above 1.5
  - after the miss, `SetPenVisibility(1, false)` makes pen 2 active; `SetPenVisibility(1, true)` keeps pen 2
    active, and the green-to-red ratio is below 1.25
  - after the miss, a catalogue apply that adds a third pen leaves pen 1 active with a ratio below 1.25
  - on an empty chart, `PickPenAt` at any pixel, then `AddTwoFlatPens` and the `Manual` pin: pen 1 is active
    with a ratio above 1.5
- [x] write the four pointer tests of Acceptance Evidence: rename `AClickOnEmptySpace_KeepsTheActivePen` to
  `AClickOnEmptySpace_KeepsTheActivePenAndDropsItsEmphasis` and add its ratio assertion; add outcomes 2, 3 and 4
- [x] run both filters and record under this task which tests are red; the expected red set is the first, the
  second, the fifth and the sixth view-model test and pointer outcomes 1 to 3; the third, the fourth and the
  seventh pass before the change and guard the re-emphasis and the empty-chart rule once the clear exists
  - Red run (7 failed, 153 passed), exactly the expected set:
    - `TrendChartViewModelTests.PickPenAt_EmptySpace_KeepsTheActivePenAndDropsItsEmphasis`: `redraws` expected
      1, found 0 (a miss wrote nothing).
    - `TrendChartViewModelTests.PickPenAt_BelowTheDataArea_DropsTheEmphasis`: red-to-green ratio expected
      < 1.25, found 2.297.
    - `TrendChartViewModelTests.SwitchingTheActivePenOffAfterAMiss_LeavesThePenTakingTheSlotThin`:
      green-to-red ratio expected < 1.25, found 2.308.
    - `TrendChartViewModelTests.ACatalogueThatAddsAPenAfterAMiss_LeavesTheActivePenThin`: red-to-green ratio
      expected < 1.25, found 2.297.
    - `ChartPointerInputTests.AClickOnEmptySpace_KeepsTheActivePenAndDropsItsEmphasis`,
      `AClickBelowTheDataArea_DropsTheEmphasis` and `AClickOnALineAfterAnEmptyClick_EmphasizesThatPen` (its
      first assertion): red-to-green ratio expected < 1.25, found 2.667.
    - Green before the change: `SetActivePen_OfTheActivePenAfterAMiss_EmphasizesItAgain`,
      `PickPenAt_TheActivePensLineAfterAMiss_EmphasizesItAgain`,
      `PickPenAt_OnAChartWithNoPens_LeavesTheFirstPenSeatedEmphasized` and
      `ADragThroughEmptySpace_KeepsTheEmphasis`.
- [x] add `_isActivePenEmphasized`, the set in `SetActivePen`, `DropEmphasis()` on a miss in `PickPenAt`, and
  pass the flag from `RestyleActivePen()`
- [x] run the `TrendChartViewModelTests`, `ChartPointerInputTests`, `ChartActivePenRenderTests`,
  `TrendChartCatalogueTests` and `TrendChartRenderThreadTests` filters; must pass before Task 3

### Task 3: Verify acceptance criteria

- [x] every Overview item is implemented: a miss clears `_isActivePenEmphasized` through `DropEmphasis()` and
  keeps `ActivePenId` (`TrendChartViewModel.cs:344-367`); `SetActivePen` sets the flag for a line hit and for the
  sidebar row click (`TrendLegendRowViewModel.cs:56`); `ChartPenSet.RestyleActiveLine` thins without changing the order
- [x] the Acceptance Evidence tests pass, and their red run is recorded under Task 2: the four outcomes 4/4
  passed, the `ChartPointerInputTests` filter 30/30, the `TrendChartViewModelTests` filter 130/130
- [x] run `dotnet test SemiPlot.slnx`: `SemiPlot.Tests.Unit` 1962 passed, 0 failed, 0 skipped;
  `SemiPlot.Tests.Integration` 167 passed, 0 failed, 0 skipped (Docker 29.8.2)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files: both exit 0
  over the seven `.cs` files of `git diff --name-only master...HEAD`

### Task 4: [Final] Update documentation

- [x] `docs/architecture/trend-interaction.md:231-232`: replace "a click farther than 6 px from every line
  changes nothing" with the miss rule: the pen stays active and loses its emphasis; a click on a line or a
  sidebar row emphasizes again; a drag, Ctrl + click, delta mode and the axis region leave the emphasis as it
  is; a miss with no drawn pen changes nothing
- [x] `docs/architecture/charting.md:49-53` ("Active stroke"): the width follows the view model's emphasis flag,
  the order does not; the flag's three writers (`SetActivePen`, `DropEmphasis` through `PickPenAt`,
  `ApplyCatalogue`), and that the automatic slot moves keep it
- [x] `docs/architecture/trend-feature-spec.md:113-115` (PN-6 as built): one sentence on the empty click
- [x] `docs/architecture/bench.md:345`: the `ChartPointerInputTests` row names the empty click that drops the
  emphasis
- move this plan to `docs/plans/completed/` (left for the delivery step)

## Post-Completion

**Manual verification**:

- The seven-step stand smoke in Acceptance Evidence.
- Status: pending; not run on the demo stand.

**Executed by exec:**

- branch: worktree-pen-emphasis-off

## Verify it yourself

1. Automated acceptance, from the worktree:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartPointerInputTests"`.
   Every test passes, the four Acceptance Evidence outcomes among them. The tests landed together with the
   production change in `e47ee75`; their red run against the production code of `ade9c01` is recorded under
   Task 2: outcomes 1 to 3 measured a red-to-green ratio of 2.667 where below 1.25 is required.
   `TheShownChart_LeavesTheActivePenThin` pins the window opening with no pen emphasized; the tests that need a
   thick line first pick pen 1 through `SetActivePen`, as a sidebar click does.
2. Writers of the flag: the same command with `--filter "FullyQualifiedName~TrendChartViewModelTests"`. The
   first pens seated, the miss, the miss below the data area, the sidebar re-click, the click on the active
   pen's own line, the slot moves, the catalogue that leaves no pen, every pen switched off and the dispose
   guard each have a test.
3. Full gate: `dotnet test SemiPlot.slnx` (Docker running for the integration project) and
   `dotnet format SemiPlot.slnx --verify-no-changes`.
4. On the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), before versus after:
   - a new window: before, the first pen's line is 2 px; after, every line is 1 px while the first pen's row
     is marked and its axis drawn;
   - after a click on a sidebar row, a click in empty plot area: before, the active line stays 2 px; after, it
     turns 1 px, while the sidebar row stays marked and the axis stays drawn;
   - a click on the time axis labels: same as above;
   - a click on the active pen's sidebar row, or on its line, after that: the line turns 2 px again;
   - the seven-step smoke in Acceptance Evidence.
