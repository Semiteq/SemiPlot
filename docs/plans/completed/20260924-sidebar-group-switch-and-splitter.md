# Sidebar: group switch and splitter

## Overview

Two mechanical gaps in the sidebar, `Semiteq/SemiPlot#68`:

- A group header is inert text. Turning off the sixteen heaters means sixteen clicks.
- The panel width is fixed: 280 expanded, 168 collapsed, with no way to drag it.

This plan adds a switch on every group header and a drag handle between the chart and the sidebar, and sets
the group headers apart from the pen rows. The change ships as one pull request for `#68`: the review fixes
and the header restyle touch both features. Nothing depends on SemiBase.

Behaviour:

- The header switch has three states: all pens of the group on, all off, mixed. It is derived from the
  pens and stored nowhere. A pen in several groups is switched by any of them, and every header it sits
  under re-derives. Every drawn header carries the switch, the ungrouped header included.
- The handle works in both panel states. Its width is not persisted across restarts. The collapse button
  switches between two widths, each remembered for the session: dragging changes the width of the state
  the panel is in.

## Context (from discovery)

- `SemiPlot.UI/Legend/TrendLegendViewModel.cs:12-15` — `TrendLegendGroupViewModel` is a record of name,
  `HasHeader` and rows; nothing on it can derive a state. `:27-34` build the rows and groups; `:38-48`
  `IsExpanded`, the command's single writer, raising `RequestedWidth` and `ToggleText`; `:50`
  `RequestedWidth => IsExpanded ? ExpandedWidth : CollapsedWidth`; `:19-22` the two constants, the first
  documented as the value `MainWindow.axaml` falls back to.
- `TrendLegendViewModel.cs:98-117` — `AppendUngrouped`; on a name collision `:114` rebuilds the colliding
  group with `with { Rows = ... }`, which a `ReactiveObject` does not have.
- `SemiPlot.UI/Legend/TrendLegendRowViewModel.cs:67` — `IsVisible`, whose setter routes to the chart;
  `:43` mirrors the chart's state back through `WhenAnyValue`. A pen listed under two headers is one row
  instance, so switching it under one header switches it under all.
- `SemiPlot.UI/Chart/TrendChartViewModel.cs:280-295` — `SetPenVisibility` runs `ActivateAVisiblePen`,
  `ApplyAxisModel` and `RequestRedraw` per pen.
- `SemiPlot.UI/Legend/TrendLegendView.axaml:29-33` — the header `TextBlock` named `GroupHeader`, visible by
  `HasHeader`; the row template follows.
- `SemiPlot.UI/MainWindow/MainWindow.axaml:37` — the content grid `ColumnDefinitions="*,Auto"`; `:45-55` the
  `LegendPanel` border, whose `Width` binds `LegendViewModel.RequestedWidth` with a `FallbackValue` of
  `ExpandedWidth`, whose `IsVisible` binds `IsLegendVisible`, and whose `BorderThickness="1,0,0,0"` draws
  the divider. `LegendViewModel` is null until `SetChart` (`MainWindowViewModel.cs:149`) and stays null
  after a startup failure; the fallback is the width that window renders.
- Tests the change touches: `TrendLegendViewTests.cs:132-137` (the row allowlist, exactly one `CheckBox`
  per row template — unaffected, the header sits outside the row template), `:180-183` (counts every
  `CheckBox` in the window and indexes `boxes[0]` — a header box changes the count and the index order),
  `:96-110` (`TheRequestedWidthAndTheToggleLabel_FollowTheState`), `:217-224` (`PressTheToggle`, which
  calls `Command.Execute`), `TrendLegendViewModelTests.cs:115-131`
  (`AGroupNamedLikeTheUngroupedHeader_TakesTheUngroupedRowsRatherThanASecondHeader`),
  `MainWindowViewTests.cs:80-113` (`EveryViewMenuRow_FollowsItsOwnFlagOnTheRealisedWindow`, which checks
  `Border.IsVisible` only), `:117-139` (`LegendPanelWidth_ReadsTheFallbackAndThenFollowsThePanelState`).
- `docs/architecture/charting.md:206-212` and `trend-interaction.md:262-269` describe the sidebar and the
  fixed 280/168 widths.
- CLAUDE.md, UI: a checkable control reads its flag `Mode=OneWay` and writes it only through the command it
  invokes, so the command is the flag's single writer. Every colour resolves to a `Palette.axaml` key.
- `.claude/rules/avalonia.md`: shared UI state has exactly one writer; code-behind holds input interop only
  and delegates the decision to the view model.

## Development Approach

- Testing approach: regular — code first, then tests, within the same task.
- Each task ends with `dotnet test SemiPlot.slnx` green, and is its own commit.
- Update this plan when the scope changes during implementation.

## Testing Strategy

- View-model tests for the derived state, the command and the width slots.
- Headless view tests with `[AvaloniaFact]` over the realised window for every binding: the repository has
  no compiled bindings, so only the rendered tree proves a binding resolves. Any chart a test builds gets a
  `TestScheduler` (`docs/architecture/testing-strategy.md`, the UI scheduler in a realised view).
- Controls are driven as the operator drives them: a click raised on the realised `CheckBox`, a drag raised
  on the realised `Thumb`. A test that calls `Command.Execute` or writes a width property proves nothing
  about the binding.
- Every acceptance item states its passed count; a filter matching nothing exits 0.

## Acceptance Evidence

1. **The header switch derives three states.** `dotnet test
   SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegendViewModel"`
   passes with a non-zero count: all pens on gives on, all off gives off, mixed gives indeterminate, and
   switching one pen re-derives every header it sits under.
2. **The switch sets the whole group.** Same filter: the command on a mixed or off header switches every
   pen of the group on; on an all-on header, off. A pen in a second group follows, and that group's header
   re-derives. `AGroupNamedLikeTheUngroupedHeader_TakesTheUngroupedRowsRatherThanASecondHeader` stays green.
3. **The rendered header carries the box and reflects the state.** `--filter
   "FullyQualifiedName~TrendLegendViewTests"` over the realised window: every drawn header, the ungrouped
   one included, shows one header `CheckBox` whose `IsChecked` is `true`, `false` or `null` per item 1; a
   catalogue with no groups draws no header and no header box. Clicks raised on the realised box, mixed
   to on, on to off, and, once the chart made the header mixed again, mixed to on, each leave `IsChecked`
   at the derived state; a mixed click is the one whose own toggle (to off) differs from the derived
   result, so a click that replaced the one-way binding fails there. The row allowlist still passes.
4. **The handle resizes the panel in both states.** `--filter "FullyQualifiedName~MainWindowViewTests"`: a
   pointer pressed on the realised handle and moved in two steps widens the panel by the total travel;
   collapsing shows the collapsed width, dragging it changes that width, expanding restores the width the
   expanded state last had. A drag below
   `PanelMinWidth` stops at it, and a drag that would leave the chart under `ChartMinWidth` stops there.
5. **Hiding the legend gives the chart the whole row.** Same filter: with View > Legend off,
   `ChartContent.Bounds.Width` equals the content grid's width.
6. **The startup-failure window keeps its width.** Same filter: with `LegendViewModel` null, `LegendPanel`
   renders at `ExpandedWidth`.
7. **`RequestedWidth` is gone.** `grep -rn "RequestedWidth" SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.Tests.Unit`
   returns nothing.
8. **Both suites green.** `dotnet test SemiPlot.slnx`.
9. **Manual smoke on the stand:**
   1. Uncheck the Heaters header: every heater line disappears, and the Watchlist header, which holds
      Heater 01, turns indeterminate.
   2. Check it again: all heaters return.
   3. Drag the handle wider, collapse, drag narrower, expand: the panel returns to the wider width.
   4. Drag the handle as far as it goes both ways: the panel stops at its floor, the chart at 320.
   5. Hide the legend from the View menu: the chart fills the row with no empty strip.
   6. Restart: both widths are back at their defaults.
   7. The group headers read as section captions, distinct from pen rows; the active pen is the one row
      with a tinted background and a left bar, and clicking another row or pen moves the mark.

## Progress Tracking

- Mark completed items `[x]` when done; new tasks get `+`, blockers `!`.

## Solution Overview

**The group becomes a view model.** `TrendLegendGroupViewModel` turns from a record into a `ReactiveObject`
that holds its rows and exposes `SwitchState` (`bool?`): an `ObservableAsPropertyHelper` over its rows'
`IsVisible`, true when all are on, false when all are off, null when mixed. `SwitchGroupCommand` is the only
writer: it sets every row's `IsVisible` to true unless all are already on, in which case false. The row's
setter already routes to the chart, and every other header the pen sits under re-derives from the same
rows. The header `CheckBox` binds `IsChecked="{Binding SwitchState, Mode=OneWay}"` and `Command="{Binding
SwitchGroupCommand}"`, is visible by `HasHeader`, and is not three-state for clicks, so a click never lands
the box on indeterminate by its own cycling. Group visibility is never stored.

The command switches row by row, so each pen runs its own `SetPenVisibility` and a sixteen-pen group
rebuilds the axis sixteen times and passes through transient mixed states. At catalogue scale this is
invisible, and no batch API is added for it.

`BuildGroups` computes the name-to-rows lists first, the ungrouped merge included, and constructs each
group view model exactly once: a group built and then replaced would leak the subscriptions it took on its
rows.

**The width has one writer, and the border keeps it.** `LegendPanel.Width` stays bound one-way to the view
model, now `PanelWidth`, with the same `FallbackValue` of `ExpandedWidth`, so the startup-failure window
renders as today and hiding the border still collapses its `Auto` column to zero. A thin `Thumb` with its own
inline template (Semi ships no `Thumb` theme, so an untemplated one is never hit) sits in a new `Auto`
column between the chart and the panel, visible while `IsLegendVisible` holds and `LegendViewModel` exists.
Its `DragDelta` handler in `MainWindow.axaml.cs` is input interop and nothing more: it reaches
`MainWindowViewModel.LegendViewModel` by pattern matching and calls `ResizePanel(double delta)`. The room
the panel may take is written by `FitPanel(double maximumWidth)` alone, called when the legend is assigned
and on every `ContentGrid.SizeChanged`, with the grid's width less `ChartMinWidth` and the handle. The view
model keeps two session slots, expanded and collapsed, initialised to 280 and 168; `ResizePanel` writes the
slot of the current state, and `PanelWidth` reads it clamped to `[PanelMinWidth, maximum]`, so a shrinking
window narrows the panel without losing the dragged width. The collapse command changes which slot is shown
and raises `PanelWidth`. So each slot has one writer, `ResizePanel` while its state is showing, the room has
one writer, `FitPanel`, and the displayed width has one derivation. No converter and no two-way binding
exist.

## Technical Details

- `PanelWidth` is `double`. The handle sits on the panel's left edge, so a drag to the left widens it:
  the new width is the current slot minus the horizontal delta.
- `PanelMinWidth = 120` holds the collapsed row: box, 12 px dot, padding and a short name. `ChartMinWidth =
  320`. Both are constants on `TrendLegendViewModel`; the stand confirms the first in Task 3.
- The handle's `Background` is a `Palette.axaml` key; the border's one-pixel divider stays.
- The header box's accessible name is `SwitchName => Resources.FormatLegendGroupSwitch(Name)` on the group
  view model, bound through `AutomationProperties.Name`.

## Implementation Steps

### Task 1: A switch on every group header

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx` (`LegendGroupSwitch`)
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`

- [x] turn `TrendLegendGroupViewModel` into a `ReactiveObject` with the derived `SwitchState`,
      `SwitchGroupCommand` and `SwitchName`, disposed with the legend view model
- [x] make `BuildGroups` compute the name-to-rows lists first, the ungrouped merge included, and construct
      each group view model once
- [x] add the header `CheckBox` beside the header text, visible by `HasHeader`, bound one-way with the
      command as its writer
- [x] write the view-model tests of acceptance items 1 and 2
- [x] write the view tests of acceptance item 3, with two real clicks, and correct the window-wide
      `CheckBox` count and index at `TrendLegendViewTests.cs:180-183`
- [x] describe the header switch in `charting.md:206-212` and `trend-interaction.md:262-269`
- [x] run tests — must pass before the next task

### Task 2: A drag handle between chart and sidebar

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`

- [x] replace `RequestedWidth` with `PanelWidth`, the two session slots, `ResizePanel` and the two minimum
      constants; bind `LegendPanel.Width` to `PanelWidth` with the existing `FallbackValue`, and correct the
      `ExpandedWidth` summary at `TrendLegendViewModel.cs:19`
- [x] add the handle column and the `Thumb`, visible by `IsLegendVisible`, with its `DragDelta` handler
      delegating to `ResizePanel`
- [x] write a `TrendLegendViewModelTests` case: drag expanded, collapse, drag collapsed, expand, and the
      clamp at both ends
- [x] rewrite `TheRequestedWidthAndTheToggleLabel_FollowTheState` and
      `LegendPanelWidth_ReadsTheFallbackAndThenFollowsThePanelState` for acceptance items 4 and 6, keeping
      the fallback half, and add the hidden-legend test of item 5
- [x] replace "narrows from 280 to 168. Nothing persists the state" in `charting.md:211-212`, and the
      matching text in `trend-interaction.md`, with the draggable width and its two session slots
- [x] run tests — must pass before the next task

### Task 3: Verify acceptance criteria

- [x] run acceptance items 1 to 8 and record each passed count
- [x] walk the manual smoke list, item 9, on the stand, and confirm `PanelMinWidth` against it
! steps 1 to 6 confirmed on the stand; `PanelMinWidth` stays 120. The walk found the header and the active
  row indistinguishable, which Task 5 answers; step 7 is walked after it
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files

! measured 2026-09-24, after the review fixes: items 1 and 2 (`~TrendLegendViewModel`) 24 passed, with
  `AGroupNamedLikeTheUngroupedHeader_TakesTheUngroupedRowsRatherThanASecondHeader` 1 passed on its own;
  item 3 (`~TrendLegendViewTests`) 14 passed; items 4 to 6 (`~MainWindowViewTests`) 9 passed; item 7 grep
  returned nothing (exit 1); item 8 `SemiPlot.Tests.Unit` 1047 passed, `SemiPlot.Tests.Integration` 97
  passed. `dotnet format --verify-no-changes` exit 0; `dotnet terse` over the touched `.cs` files exit 0.
! smoke item 9: steps 1 and 2 have headless substitutes (`SwitchingOnePen_ReDerivesEveryHeaderItSitsUnder`,
  `TheSwitchOnAnOnHeader_SwitchesEveryPenOffAndTheSharedPensOtherHeaderFollows`,
  `ClicksOnTheHeader_SwitchTheGroupOnThenOffAndAMixedHeaderOnAgain`), step 3
  (`ADragOnTheHandle_ResizesThePanelInBothStatesAndEachStateKeepsItsWidth`), step 4
  (`ADragOnTheHandle_StopsAtThePanelFloorAndAtTheChartFloor`), step 5
  (`HidingTheLegend_GivesTheChartTheWholeRow`); the drags are headless pointer presses on the realised
  handle. None covers the chart lines actually disappearing on the real seeded catalogue, the restart of
  step 6, or whether 120 px fits a real collapsed row, which rest on the operator alone.

### Task 4: Update documentation

- [x] `readme.md`: the group switch and the draggable panel in the feature list
- [x] move this plan to `docs/plans/completed/` — delivery work, after the operator has tested the branch
  (left to ship: the file stays in `docs/plans/` until ship moves it at delivery)

### Task 5: Tell a group header from a pen, and the active pen from both (+)

A group header and the active pen both render bold (`TrendLegendView.axaml`: `GroupHeader` `FontWeight="Bold"`,
the row name through `LegendConverters.ActiveToWeight`), so on the stand the eye cannot tell a header from
the active row. The header becomes a section caption and the active row is marked by its background:

- the group header text is smaller than a row name, in `AppSecondaryForegroundBrush`, in capitals, not bold;
  a one-pixel `AppSubtleLineBrush` line sits above every header but the first; the rows under a header are
  indented by the header box's width, so the two checkbox columns read as two levels;
- the active row keeps the regular weight and carries a background (`AppAccentFillBrush`) and a 3 px bar on
  its left edge (`AppAccentBrush`), both driven by `IsActive`; `ActiveToWeight` goes if nothing else uses it;
- every colour is an existing `Palette.axaml` key in both variants; a new key is added only when no existing
  one fits, with both variants and a `ThemeTests` probe;
- the row and header allowlists (`TrendLegendViewTests`) stay as they are: the bar and the background are
  `Border`s.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Legend/LegendConverters.cs` (if a converter is added or removed)
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`, `docs/architecture/ui-theme.md`

- [x] restyle the group header as a section caption with the separator and the row indent
- [x] mark the active row by background and left bar instead of bold
- [x] view tests over the realised window: the header text is not bold and paints the secondary brush;
      exactly the active row shows the bar and the background, and a pen activated from the chart moves them
- [x] update the sidebar description in `charting.md` and `trend-interaction.md`, and the brush consumer rows
      in `ui-theme.md`
- [x] run `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse`
      over the touched files

## Post-Completion

- Persisting the panel width across restarts is deliberately not done; it belongs with the settings file
  once there is a reason to remember a window preference.
- `CLAUDE.md`'s sidebar bullet names the row allowlist and `ThePanel_RealisesNoTextEditor` but not the
  group-header allowlist `TheGroupHeaderTemplate_CarriesOnlyReadOnlyControlsAndOneSwitch`.

**Executed by exec:**
- branch: sidebar-group-switch-and-splitter

## Verify it yourself

Automated, from the repository root:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegendViewModel|FullyQualifiedName~TrendLegendViewTests|FullyQualifiedName~MainWindowViewTests"
dotnet test SemiPlot.slnx
```

The first prints 53 passed (24 + 20 + 9); the second 1054 unit and 97 integration. The pointer-driven drag
tests `ADragOnTheHandle_*` fail on `7391aa2` (the untemplated handle: the panel stays at 280 against an
expected 340) and pass from `4f5af8b` on.

On the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), acceptance item 9:

1. Uncheck the Heaters header: every heater line disappears, the Watchlist header turns indeterminate.
2. Check it again: all heaters return.
3. Put the mouse on the 4 px strip left of the sidebar and drag it wider; collapse, drag narrower, expand:
   the panel returns to the wider width. Before `4f5af8b` the strip did nothing.
4. Drag as far as it goes both ways: the panel stops at 120 px with a collapsed row still readable, the
   chart at 320 px. Shrink the window after a wide drag: the chart keeps 320, and growing the window back
   restores the panel.
5. View > Legend off: the chart fills the row with no empty strip.
6. Restart: both widths are back at 280 and 168.
