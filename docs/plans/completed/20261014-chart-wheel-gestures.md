# Wheel and Ctrl gestures on the chart

Issue: #74.

## Overview

- The chart gains four mouse gestures beside the existing ones. The full map:

  | Cursor over | Gesture | Effect | Status |
  | --- | --- | --- | --- |
  | data area | wheel | time zoom about the cursor | exists |
  | data area | Shift + wheel | time pan, 10% of the window per notch; wheel down moves toward newer data | new |
  | data area | Ctrl + wheel | shifts the active pen, 10% of its range per notch; wheel up moves the line up | new |
  | data area | drag | time pan | exists |
  | data area | Ctrl + drag | shifts the active pen continuously; the horizontal part of the move is ignored | new |
  | data area | click | picks a pen | exists |
  | data area | Ctrl + click | nothing | new |
  | active pen's axis | wheel | scales the active pen about the cursor | new |
  | active pen's axis | click | opens the axis scale panel | exists |

- "Shift the pen" moves its minimum and maximum together by one amount. "Line up" means both limits
  decrease, so the drawn curve rises.
- A pen in `ScaleMode.Auto` that receives a shift or a scale first takes the limits it shows now, switches to
  `ScaleMode.Manual` and then applies the gesture. Autoscale (View -> Pen scale -> Autoscale) brings tracking
  back.
- On a log10 axis the shift and the scale run in decades, so the minimum never reaches zero.
- The limits a gesture sets live in the window's session, like the limits typed into the axis scale panel.
  Nothing is written to the database or the configuration.
- A wheel event with no vertical component and no Shift held does nothing. Today it zooms out.
- PN-2 (`docs/architecture/trend-feature-spec.md:91-93`, MUST) asks for a mouse-driven vertical resize of one
  pen; the wheel over the axis is that gesture. TM-7 (`:41-43`) maps Ctrl + Up/Down to a vertical shift, and
  Ctrl + wheel up/down follows the same mapping.

## Context (from discovery)

- `UI/Chart/TrendChartView.axaml.cs:268-279`: `OnPointerWheelChanged` has no branch. It zooms by
  `ZoomInFactor` (0.8, `:24`) when `Delta.Y > 0` and by `ZoomOutFactor` (1.25, `:25`) otherwise, so an event
  with `Delta.Y == 0` (a horizontal wheel) zooms out today. It always sets `Handled`.
- `TrendChartView.axaml.cs:281-311`: `OnPointerPressed` switches on `ChartPressRouter.Route(IsOverActiveAxis(position),
  _viewModel.ActiveLeftButtonTool)`. `IsOverActiveAxis` (`:313-318`) builds a `ChartAxisRegion` for
  `_viewModel.ActivePenAxis`.
- `UI/Chart/ChartAxisRegion.cs`: `TryCreate(plot, axis)` reads `plot.RenderManager.LastRender.Layout` and
  returns null for a hidden axis or an empty data rect; `Contains(x, y)` tests the axis panel band between the
  data rect's top and bottom.
- `UI/Chart/ChartPressRouter.cs`: `ChartPressAction` is `PanOrPickPen | PlaceDeltaCursor | EditAxisScale`.
  `Route(isAxisRegionHit, activeTool)`: axis hit first, then `DeltaPlacement`, then `PanOrPickPen`; the comment
  at `:12` states that order. `IsPastClickSlop` is the 4 px slop. `UI/Chart/LeftButtonTool.cs` is
  `Pan | DeltaPlacement`.
- `TrendChartView.axaml.cs:328-382`: `BeginPan` stores `_dragOrigin`, calls `BeginDrag`, sets
  `_grabbingCursor` and captures the pointer. `OnPointerMoved` waits for the slop (`_panStarted`), then pans by
  `AnchorAt(origin) - AnchorAt(current)` and moves `_dragOrigin`. `OnPointerReleased` releases the capture
  and calls `PickPenAt` when no pan started; `OnPointerCaptureLost` (`:386-394`) runs `EndDrag` (`:396-402`).
- `UI/Chart/ChartNavigationController.cs:135-145`: `ZoomAt(factor, anchorUtc)` and `PanBy(TimeSpan)`.
  `Core/Trends/TrendNavigationModel.cs:36-52`: `Pan` clamps `From` at the first sample and detaches sticky once
  the live edge leaves the window.
- `UI/Chart/TrendChartViewModel.cs:434-441`: `SetAxisLimits` sets `Mode = Manual` with the given limits
  through `UpdateAxisSettings` (`:684-695`), which recomputes every pen's `PenScale` (`ApplyAxisModel`,
  `:697-708`), stores them in `_scalesByPenId` (`:40`, `:710-720`) and requests a redraw. `_scalesByPenId`
  therefore holds the limits each pen shows now, Auto pens included. `MayDrawAxisFor` is at `:532`.
- `UI/Chart/ChartAxisBinder.cs:105-115`: a log10 pen's axis carries `Math.Log10(min)..Math.Log10(max)`, so
  axis coordinates of a log pen are decades.
- `Core/Trends/LogAxis.cs:19`: `AdmitsMinimum` is the one test of a valid log minimum; `SetLogarithmic`
  (`TrendChartViewModel.cs:452`) and `PenScaleModel.SanitizeManualRange` (`PenScaleModel.cs:81`) call it.
- `Core/Trends/PenScaleModel.cs:71-91`: `SanitizeManualRange` falls back to the default range on a non-finite
  manual limit, swaps a reversed pair and passes an equal pair through unchanged.
- `SemiPlot.Tests.Unit/UI/HeadlessInput.cs:22-30`: `Click(topLevel, control, controlPoint)` sends
  `MouseDown`/`MouseUp` with no modifier. `AGENTS.md` routes every headless click through this class; a wheel,
  a drag and a held button are outside the rule.
- Avalonia.Headless 12.0.5 (`~/.nuget/packages/avalonia.headless/12.0.5/lib/*/Avalonia.Headless.xml`):
  `MouseDown`, `MouseMove`, `MouseUp` and `MouseWheel` each take a `RawInputModifiers` argument.
- `SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`: `CreateLoadedViewModel` leaves pens in Auto;
  `CreateViewModelWithFixedScales` makes them Manual. The wheel test is `:67-84`, the axis click
  `AClickOnTheActiveAxisRegion_PicksNothing` presses at `dataRect.Left - AxisRegionInsetPixels` (`:37`), and
  `AJitterIntoThePast_NeitherPansNorDetachesFollow` (`:151`) zooms in first to make room for a pan into the past.
  The fixture's `FirstSample` equals `From`, so a fresh fixture cannot pan backward.
- `UI/Chart/AxisScalePanelViewModel.cs`: the panel seeds its fields on `Seed()` and on a commit, not on
  `ScalesRevision`.
- Size: `TrendChartViewModel.cs` is 753 lines and `TrendChartView.axaml.cs` 508, both past the 300-line class
  limit. The range math goes to Core and the routing to the two routers, so each of these files gains only
  the calls.

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task)
- complete each task fully before moving to the next
- every task includes new or updated tests for the code it changes; all tests pass before the next task
- update this plan when scope changes during implementation

## Testing Strategy

- **unit tests**: `[Fact]`/`[Theory]` for the Core range math and both routers; `[AvaloniaFact]` for the view
  model and the realised chart view (`ChartPointerInputTests`).
- **e2e tests**: the project has no browser e2e suite; the headless view tests in `ChartPointerInputTests`
  carry the gesture-level coverage.
- Every new test class carries the `Component`, `Area` and `Category` traits.

## Acceptance Evidence

Automated, all in `SemiPlot.Tests.Unit`:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj `
  --filter "FullyQualifiedName~PenRangeGestureTests|FullyQualifiedName~ChartWheelRouterTests|FullyQualifiedName~ChartPressRouterTests|FullyQualifiedName~ChartPointerInputTests|FullyQualifiedName~TrendChartViewModelTests"
```

It must pass with these tests present, each in `ChartPointerInputTests` unless named otherwise:

- `ShiftWheelDown_PansTowardNewerDataByATenthOfTheWindow`: `Navigation.From` moves forward by 10% of the width
  and the width stays.
- `CtrlWheelUp_LowersTheActivePensLimitsAndPinsItToManual`: on `CreateLoadedViewModel` (Auto pens) both limits
  of the active pen drop by 10% of its span, its mode becomes `Manual`, and every other pen's `ScaleSettings`
  stays equal.
- `WheelUpOverTheActiveAxis_NarrowsThePenAboutTheCursor`: the span becomes 0.8 of the old one, and after one
  driven frame the value under the cursor sits at the same pixel within 1 px.
- `CtrlDiagonalDrag_ShiftsTheActivePenOnly`: an upward-left drag lowers the active pen's limits and leaves
  `Navigation.From` and `Navigation.IsSticky` unchanged.
- `CtrlClickOnALine_ChangesNothing`: `ActivePenId` and every scale stay unchanged, in pan mode and in delta mode,
  and delta mode places no cursor.
- `HorizontalWheel_LeavesTheWindowWidth`: `MouseWheel(point, new Vector(1, 0))` leaves `To - From` unchanged.

Manual, on Windows with a physical mouse (the headless platform does not reproduce how Win32 reports a
Shift-modified wheel):

1. Shift + wheel down over the data area moves the window toward newer data, wheel up toward older data.
   Ctrl + Shift + wheel up raises the active pen's line like Ctrl + wheel up. On a precision touchpad, note
   what a two-finger swipe and a pinch do: a pinch is expected to arrive as Ctrl + wheel and shift the pen.
2. Ctrl + wheel up raises the active pen's line; the value printed at the top of the axis decreases by one
   tenth of the span, and the tick marks move up with the line.
3. Wheel over the active pen's axis stretches and compresses that pen about the cursor; other pens stay put.
4. Ctrl + drag moves the active pen's line with the pointer; the time window does not move.
5. Plain wheel, drag, click and the axis click behave as before.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep the plan in sync with the work done

## Solution Overview

- **One range algorithm in Core.** `SemiPlot.Core/Trends/PenRangeGesture.cs` holds the shift and the scale of a
  `(min, max)` pair, linear or log10. It is renderer-agnostic and unit-tested. Every gesture reaches it through
  the view model; the view converts pixels into fractions of the data area and nothing else.
- **One decision point per event kind.** The press keeps `ChartPressRouter.Route` as its single decision; it
  gains the held modifiers and the `ShiftActivePen` action. The wheel gets the same shape in a new
  `ChartWheelRouter.Route`. Both are pure functions, so the gesture handlers carry no modifier test of their
  own, and the left button stays one gesture without overlapping hidden branches
  (`docs/architecture/trend-interaction.md:179-182`).
- **Order.** Press: axis region, then Ctrl, then delta mode, then pan. Wheel: axis region (modifiers ignored),
  then Ctrl, then Shift, then plain zoom. An explicit held key wins over the toolbar's delta mode.
- **One view-model entry per gesture kind.** `TrendChartViewModel.ShiftActivePen(fraction)` serves Ctrl + wheel
  and Ctrl + drag; `ScaleActivePen(factor, anchorFraction)` serves the wheel over the axis. Both go through one
  private method that reads the active pen's current `PenScale` from `_scalesByPenId`, applies the Core
  function and ends in `SetAxisLimits(ActivePenId, min, max)`. This is the Auto -> Manual rule in one place.
- `LeftButtonTool` stays `Pan | DeltaPlacement`: Ctrl is a held key, not a toolbar mode.

## Technical Details

- `PenRangeGesture` (static, Core):
  - `(double Min, double Max)? Shift(double min, double max, bool isLogarithmic, double fraction)`: in axis
    space `a`, `b` (decades when logarithmic), `span = b - a`, result `(a - fraction * span, b - fraction * span)`.
    A positive fraction moves the line up.
  - `(double Min, double Max)? Scale(double min, double max, bool isLogarithmic, double factor, double anchorFraction)`:
    `anchor = a + anchorFraction * span`, result `(anchor - (anchor - a) * factor, anchor + (b - anchor) * factor)`.
    `anchorFraction` is 0 at the bottom of the data area and 1 at the top.
  - Logarithmic results convert back with `Math.Pow(10, x)`. Both return null, and the pen keeps its limits,
    when a result limit is not finite, when `!(newMax > newMin)` (a zero-span input or a scale that collapsed
    the span), or when a log result fails `LogAxis.AdmitsMinimum`.
- `ChartWheelRouter` (UI, `Chart/ChartWheelRouter.cs`): `enum ChartWheelAction { None, ZoomTime, PanTime,
  ShiftActivePen, ScaleActivePen }`; `ChartWheelGesture Route(bool isAxisRegionHit, KeyModifiers modifiers,
  Vector delta)` returns the action and the notch sign. The notch is `Math.Sign(delta.Y)`. With Shift held and
  `delta.Y == 0` it is `Math.Sign(delta.X)` instead, so a Win32 build that delivers Shift + wheel horizontally
  still pans. A notch of 0 returns `None`.
- ASSUMPTION: Avalonia's Win32 backend delivers Shift + wheel as `Delta.Y` with `KeyModifiers.Shift`, or as
  `Delta.X` with the same sign convention. Manual check 1 verifies the sign on hardware.
- `ChartPressRouter.Route(bool isAxisRegionHit, KeyModifiers modifiers, LeftButtonTool activeTool)` and
  `ChartPressAction.ShiftActivePen`; the ordering comment at `ChartPressRouter.cs:12` names the Ctrl step.
- Constants in `TrendChartView`: `WheelPanFraction = 0.1` of the window width, `WheelShiftFraction = 0.1` of the
  pen span.
  - Shift + wheel: `Navigation.PanBy(width * WheelPanFraction * -notch)`, so wheel down pans toward newer data.
  - Ctrl + wheel: `ShiftActivePen(WheelShiftFraction * notch)`, so wheel up gives +0.1 and the line rises.
  - Axis wheel: `ScaleActivePen(notch > 0 ? WheelNarrowFactor : WheelWidenFactor, anchorFraction)`, the factors
    the time zoom shares (renamed from `ZoomInFactor`/`ZoomOutFactor` in review). The pen scale has
    no quantized ladder, so an in -> out pair restores the span only up to floating-point error.
- `anchorFraction = (dataRect.Bottom - y) / dataRect.Height` from `Plot.LastRender.DataRect`; the axis region
  already bounds `y` to the data rect.
- Drag state: one nullable field `ChartDrag(Point Origin, ChartPressAction Action, bool IsStarted)` replaces
  `_dragOrigin` and `_panStarted`, and `BeginPan` becomes `BeginDrag(eventArgs, action)`. The action is fixed at
  the press: releasing Ctrl mid-drag keeps shifting, and pressing Ctrl mid-pan keeps panning.
- Ctrl + drag past the slop: each move calls `ShiftActivePen((previous.Y - current.Y) / dataRect.Height)` and
  moves the origin. The cursor is `StandardCursorType.SizeNorthSouth` during this drag. A release inside the
  slop does nothing.
- A wheel gesture that changes the pen while the axis scale panel is open closes the panel without writing
  (`AxisScalePanelViewModel.CloseWhenOpenFor`). A commit writes the untouched bound at its seeded value, so
  a panel left open on the old seed would write that bound back over the gesture.

## What Goes Where

- **Implementation Steps**: code, tests and documentation in this repository.
- **Post-Completion**: the manual smoke checklist on a physical mouse, and the PR body.

## Implementation Steps

### Task 1: Pen range shift and scale in Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/PenRangeGesture.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/PenRangeGestureTests.cs`

- [x] implement `Shift` and `Scale` for linear and log10 ranges as in Technical Details
- [x] return null for a non-finite result, a span that is not above zero, or a log minimum `LogAxis.AdmitsMinimum` refuses
- [x] write tests: linear shift up and down by 10%, log shift moves both decades by one tenth of the decade span
- [x] write tests: scale keeps the anchor value fixed, 0.8 then 1.25 restores the span (`BeApproximately`), anchor 0 and 1 hold the bottom and top
- [x] write tests: an overflowing result, a zero-span input and a scale that collapses the span each return null
- [x] run tests - must pass before task 2

### Task 2: Routers for the wheel and the Ctrl press

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartWheelRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartWheelRouterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPressRouterTests.cs`

- [x] add `ChartWheelAction`, the action-and-notch result and `ChartWheelRouter.Route` with the order axis, Ctrl, Shift, plain, the Shift `Delta.X` fallback, and `None` for a zero notch
- [x] change `ChartPressRouter.Route` to take `KeyModifiers`, add `ChartPressAction.ShiftActivePen` after the axis hit and before delta mode, and update the comment at `:12`
- [x] update the one production caller in `TrendChartView.OnPointerPressed` to pass `KeyModifiers.None` for now
- [x] write the wheel router theory over every axis/modifier combination, Ctrl + Shift and the horizontal-only delta included
- [x] extend the press router theory with the Ctrl rows (axis wins, Ctrl beats delta mode)
- [x] run tests - must pass before task 3

### Task 3: Active pen shift and scale in the view model

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] add `void ShiftActivePen(double fraction)` and `void ScaleActivePen(double factor, double anchorFraction)` with the `ObjectDisposedException.ThrowIf` of `SetAxisLimits` (`:436`)
  - ⚠️ review: both guards removed; `SetAxisLimits` guards the one write, and a no-op after dispose needs none (`csharp.md`)
- [x] route both through one private method that returns early when `MayDrawAxisFor(ActivePenId)` refuses, `_scalesByPenId.TryGetValue` misses, or Core returns null, and otherwise calls `SetAxisLimits(ActivePenId, min, max)`
- [x] write tests: an Auto pen switches to `Manual` with its shown limits shifted by 10%
- [x] write tests: a log pen shifts in decades; scale about an anchor keeps that value
- [x] write tests: for the shift and for the scale, only the active pen's `ScaleSettings` change (PN-2 acceptance); a hidden active pen changes nothing
- [x] run tests - must pass before task 4

### Task 4: Wire the wheel gestures in the view

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`

- [x] switch `OnPointerWheelChanged` on `ChartWheelRouter.Route(IsOverActiveAxis(position), eventArgs.KeyModifiers, eventArgs.Delta)` with the calls and signs from Technical Details
- [x] set `Handled = true` for every routed action except `None`
- [x] write `ShiftWheelDown_PansTowardNewerDataByATenthOfTheWindow`; a Shift + wheel up test zooms in first, because the fixture's `FirstSample` equals `From`
- [x] write `CtrlWheelUp_LowersTheActivePensLimitsAndPinsItToManual` on `CreateLoadedViewModel`
- [x] write `WheelUpOverTheActiveAxis_NarrowsThePenAboutTheCursor`: wheel at `dataRect.Left - AxisRegionInsetPixels`, drive one frame, compare the anchor value's pixel within 1 px, and assert the time window is unchanged
- [x] write `HorizontalWheel_LeavesTheWindowWidth`; the existing plain-wheel test stays green
- [x] run tests - must pass before task 5

### Task 5: Ctrl + drag and Ctrl + click in the view

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`

- [x] pass `eventArgs.KeyModifiers` from the press into `ChartPressRouter.Route`
- [x] replace `_dragOrigin` and `_panStarted` with the `ChartDrag` field and rename `BeginPan` to `BeginDrag(eventArgs, action)`; both drags keep `Pointer.Capture(PlotControl)` and `BeginDrag()`, so the hover readout hides and `OnPointerCaptureLost` ends them
- [x] past the slop, a `ShiftActivePen` drag calls `ShiftActivePen` per move with the `SizeNorthSouth` cursor; its release inside the slop picks nothing
- [x] `EndDrag` clears the `ChartDrag` field and restores the hand cursor for both drags
- [x] add a `RawInputModifiers` overload of `HeadlessInput.Click` for the Ctrl + click test
- [x] write `CtrlDiagonalDrag_ShiftsTheActivePenOnly` and `CtrlClickOnALine_ChangesNothing` (pan mode and delta mode, on `CreateViewModelWithFixedScales`)
- [x] write a test: capture lost mid Ctrl + drag ends the drag
- [x] run tests - must pass before task 6

### Task 6: Verify acceptance criteria

- [x] verify every row of the gesture table in Overview has a test or a manual step
- [x] run the Acceptance Evidence command; add any new test class to its filter
- [x] run full test suite: `dotnet test SemiPlot.slnx`
  - ⚠️ no Docker daemon on this machine: the 163 tests of `ArchiveDatabaseCollection` fail with
    `DockerUnavailableException` from `PostgresContainerFixture.InitializeAsync`; the unit project (1944) and the
    four socket tests pass
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
- [x] `git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit` prints only `HeadlessInput.cs` lines

### Task 7: [Final] Update documentation

- [x] `docs/architecture/trend-interaction.md`: the gesture paragraph (`:172-199`) lists the wheel router and the Ctrl press branch; "Axis scaling gestures" (`:139-149`) adds the Ctrl + wheel, Ctrl + drag and axis-wheel gestures and the Auto -> Manual rule
  - ➕ `docs/architecture/charting.md`: the `ChartPressRouter` entry names `ShiftActivePen`, and a `ChartWheelRouter` entry joins the file list
- [x] `docs/architecture/trend-feature-spec.md`: a one-line note under PN-2 (`:91-93`) that the mouse resize is the wheel over the active pen's axis
- [x] `AGENTS.md`: the left-button bullet names the Ctrl pre-branch after the axis-region edit
- [ ] move this plan to `docs/plans/completed/`
  - ⚠️ not moved yet: archiving belongs to the delivery step after the manual test

## Post-Completion

**Manual verification:**
- run the five steps of the manual checklist in Acceptance Evidence on Windows with a physical mouse

**PR body:**
- issue #74's table maps Ctrl + wheel to the vertical scale; the PR body states the gesture map this change
  ships, and names the horizontal wheel that no longer zooms out and the touchpad pinch, which is expected to
  arrive as Ctrl + wheel and now shifts the active pen instead of zooming time

**Executed by exec:**
- branch: chart-wheel-gestures

## Verify it yourself

The automated half runs on any machine; the gestures did not exist on `master` (`f27bec0`), so the tests
below are absent there and pass at `f511632`.

1. Run the acceptance filter; it reports 212 passed:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj `
     --filter "FullyQualifiedName~PenRangeGestureTests|FullyQualifiedName~ChartWheelRouterTests|FullyQualifiedName~ChartPressRouterTests|FullyQualifiedName~ChartPointerInputTests|FullyQualifiedName~TrendChartViewModelTests"
   ```

2. Run the panel guard, which pins that a wheel gesture closes an open axis scale panel without writing:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AMouseGestureOnThePanelsPen_ClosesThePanel"`.
3. With Docker running, run `dotnet test SemiPlot.slnx`. The integration project did not run on the build
   machine (no Docker daemon); the branch touches no data-source code, so it is expected to stay green.
4. Start the viewer (`dotnet run --project SemiPlot/SemiPlot.AppHost`) and walk the five manual steps in
   Acceptance Evidence with a physical mouse. Before this branch each modified wheel zoomed time; after it:
   - Shift + wheel down moves the window toward newer data by a tenth of its width.
   - Ctrl + wheel up raises the active pen's line; the value at the top of its axis drops by a tenth of the span.
   - Wheel over the active pen's axis stretches it about the cursor; other pens stay put.
   - Ctrl + drag moves the active pen's line with the pointer; the time window stays.
   - Ctrl + click on a line selects nothing.
5. On a laptop touchpad, pinch over the chart. A pinch is expected to arrive as Ctrl + wheel and shift the
   active pen instead of zooming time; nothing headless can show this.
