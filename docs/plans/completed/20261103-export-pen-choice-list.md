# Export window: the pen list as checkboxes with no row selection

## Overview

- The export window's pen list is a `ListBox` whose rows each hold a `CheckBox` (`CsvExportWindow.axaml:149-174`).
  A row carries two states: the `ListBox` selection, which nothing reads, and the tick, which the export reads.
  A click on a row selects it and paints the selection fill; the selection does nothing.
- The list becomes a bordered, scrolling list of checkboxes with no row selection: a `Border` around a
  `ScrollViewer` around an `ItemsControl` over a `VirtualizingStackPanel`. A row carries one state, its tick.
- Kept: the 226 px height cap, eight 28 px rows on screen, the vertical scroll bar that never auto-hides, no
  horizontal scroll, the trimmed label and its tooltip, the 6 px inset of each box, the `AppBorderBrush` border,
  the freeze while an export runs, virtualisation over 500 pens, the list as one Tab stop, and Up/Down moving
  between rows. Gained: Space ticks the focused box.
- The five `ListBoxItem*` palette keys lose their one consumer and go, with the theme test that read them.
- Scope guard: no change to the export range, the markers or the file writer.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice export-pen-choice-list

The slice has no issue (`20261102-visual-chore-roadmap.md:244`), so the PR body closes none. Its scope is
`:240-243`, its scope guard `:250`. The roadmap's table names the defect "checkbox inside a selectable `ListBox`
row" with the cost "two states per row" (`:36`) and states that the export list's selection is never read
(`:124-125`).

Stacking: this branch, `export-pen-choice-list`, starts at `931841c`, the tip of `pen-list-filter` (verified
2026-11-03: `git rev-parse HEAD pen-list-filter` prints the same hash), which stacks on `pen-editor-groups-pane`,
`pen-editor-pens-pane` and `hex-color-input`; none is merged.

- Window (`SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`):
  - The window-scoped style `ListBox#ExportPens ListBoxItem` sets `Padding="6,0"` and `MinHeight="0"` (`:35-38`).
  - The list `ListBox` `ExportPens` (`:149-174`): `Grid.Row="5"`, `Grid.ColumnSpan="2"`, `BorderThickness="1"`,
    `BorderBrush="{DynamicResource AppBorderBrush}"`, `MaxHeight="226"`, the attached
    `ScrollViewer.HorizontalScrollBarVisibility="Disabled"`, `VerticalScrollBarVisibility="Auto"`,
    `AllowAutoHide="False"`, `ItemsSource="{Binding Pens}"`, a `VirtualizingStackPanel` items panel, and an item
    template typed `local:ExportPenChoice`: a `CheckBox` `Height="28"`, `IsChecked="{Binding IsTicked}"`,
    `ToolTip.Tip="{Binding Label}"`, holding a `TextBlock` of `Label` with `TextTrimming="CharacterEllipsis"`.
  - The form grid that holds the list disables itself while `ExportCommand.IsExecuting` (`:51-52`).
  - Around the list: `ExportEndTime` `TextBox` (`:119-123`), `ExportEndMarker` `ComboBox` enabled only when
    `EndMarkers.Count` is non-zero (`:125-132`, `:128`), `ExportPensHeader` (`:136-147`), `ExportCount`
    (`:176-180`), `ExportMessage` (`:184-189`), and the buttons `ExportSaveAs`, `ExportCancel`, `ExportClose`
    (`:191-212`).
- Code-behind `CsvExportWindow.axaml.cs`: the constructor and the blackout-day sync (`:19-66`); nothing in it
  reads `ExportPens`.
- View model: `CsvExportViewModel.Pens` is an `IReadOnlyList<ExportPenChoice>` (`CsvExportViewModel.cs:112`);
  `TickedPens()` reads `IsTicked` only (`:237-240`). `ExportPenChoice.IsTicked` raises its change
  (`ExportPenChoice.cs:14-18`). Nothing in `SemiPlot.UI/Export` reads a selection: `grep -rn "ExportPens\|ListBox"
  SemiPlot/SemiPlot.UI/Export/*.cs` prints nothing.
- Precedent: the message panel is `ScrollViewer MaxHeight="140" HorizontalScrollBarVisibility="Disabled"` around
  an `ItemsControl` `MessageEntries` with a `VirtualizingStackPanel` (`Messages/MessagePanelView.axaml:50-58`).
- `ListBox` in the tree: the export list is the only `ListBox` in this repository's AXAML (`grep -rn "ListBox"
  --include=*.axaml SemiPlot` lists only `CsvExportWindow.axaml:35`, `:149-174`). The window's two marker boxes
  `ExportStartMarker` and `ExportEndMarker` are `ComboBox`es, so the window holds `SelectingItemsControl`s and,
  with a list open, `ComboBoxItem`s (a `ListBoxItem` subclass) of its own.
- Palette: `ListBoxItemPointeroverForeground`, `ListBoxItemPressedForeground`, `ListBoxItemSelectedForeground`,
  `ListBoxItemSelectedBackground`, `ListBoxItemSelectedPointeroverBackground` in light (`Styles/Palette.axaml:44-48`)
  and dark (`:134-138`). `ui-theme.md:60-61` names their consumer: "A hovered, pressed or selected row of the
  export window's pen list".
- The colour picker holds a `ListBox` but reads none of those keys (decompiled 2026-11-03):
  `ColorView.IsColorPaletteVisible` defaults to true (`Avalonia.Controls.ColorPicker` 12.0.5), so the pen editor's
  `FormColorPicker` (`PenEditorWindow.axaml:385-396`) shows a palette tab. In `Semi.Avalonia.ColorPicker` 12.0.3
  that tab's `ListBox` takes `ItemContainerTheme="ColorViewPaletteListBoxItemTheme"`, a `ControlTheme` for
  `ListBoxItem` with no `BasedOn`, a fixed transparent `Background` and its own template whose selected and hovered
  strokes come from a `ContrastBrushConverter` binding; no `ListBoxItem*` key name occurs in the assembly. In
  `Semi.Avalonia` 12.0.3 the five keys are read only by the `ListBoxItem` `ControlTheme`, which `ListBox`,
  `DatePicker`/`TimePicker` (`DateTimePickerShared.axaml`) and `Carousel` reach; the tree holds none of them.
- Avalonia 12.0.5 keyboard and virtualisation facts (decompiled 2026-11-03):
  - Tab and Shift+Tab go through `KeyboardNavigationHandler` to `TabNavigation.GetNextTab` / `GetPrevTab`, a walk
    of the visual tree; `INavigableContainer` serves only the directional keys.
  - `ListBox` overrides `KeyboardNavigation.TabNavigation` to `Once`; `ItemsControl.OnGotFocus` records the
    focused descendant as `TabOnceActiveElement`.
  - `ItemsControl.OnKeyDown` is a bubble class handler and skips handled events. For Up/Down/Home/End it loops
    `VirtualizingStackPanel.GetControl`, which returns `ScrollIntoView(next)`, past every non-focusable
    `ContentPresenter`, so one key press scrolls the list to its far end and leaves the key unhandled.
  - `KeyboardNavigationHandler` routes arrows to `XYFocus`, whose `NavigationModes` default is
    `Gamepad | Remote`; the keyboard gets no directional focus.
  - `VirtualizingStackPanel` recycles a row by hiding it and reuses it for another index without moving it in
    `Children`; `TabNavigation` walks visual children in order. Its `CacheLength` defaults to 0, so it realises
    only the rows that intersect the viewport.
  - `ScrollViewer.BringIntoViewOnFocusChange` is an attached property defaulting to true.
  - `KeyboardNavigationHandler.GetNext(IInputElement, NavigationDirection)` is public static;
    `ItemsControl.ContainerFromIndex`, `IndexFromContainer`, `ItemsPanelRoot` and `ScrollIntoView(int)` are public.
  - `ScrollViewer` and `ItemsControl` set no `Focusable` default of their own, so neither is a tab stop.
- Tests:
  - `SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs` (traits `Component=UI`, `Area=Chart`,
    `Category=Unit`, `ProcessGlobalStateCollection`, `:39-43`) reads `ExportPens` as a `ListBox` at `:407`, `:425`,
    `:445`, `:489`, `:506`, `:548`, `:628`, `:648`, `:722`:
    `ThePenList_GrowsWithItsRowsUpToItsMaxHeight` (`:400-415`, `Bounds.Height` equals `MaxHeight`,
    `BorderThickness` 1); `ThePenList_MaxHeight_ShowsExactlyEightRowsOf28Pixels` (`:418-436`, the row is the first
    `ListBoxItem`, `:426`); `ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder` (`:438-456`);
    `ThePenList_WithTwoPens_IsBelowItsMaxHeight` (`:482-497`); `ThePenList_NeverAutoHidesItsScrollBar`
    (`:499-517`, reads `PART_ScrollViewer` at `:506`); `TheMessageRow_SitsAboveTheButtons_AndSpansTheContentWidth`
    (`:539-559`); `TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge` (`:615-637`, `:628`);
    `ALongPenLabel_IsTrimmedInsideTheList_AndItsToolTipCarriesTheWholeLabel` (`:639-666`, `PART_ScrollViewer` at
    `:649`); `WhileAnExportRuns_SaveAsIsDisabledAndCancelIsEnabled_AndTheReverseHoldsAfterIt` (`:690-738`, the
    frozen controls at `:718-725`).
  - Helpers: `BuildWithPens(count)` ticks pen 1 of `Damper 01..` (`:1308-1317`); `LabelOf(CheckBox)` (`:1228-1231`);
    `CsvExportStand.Build` takes `markers = null` by default (`CsvExportStand.cs:34-41`), so `ExportEndMarker` is
    disabled in `BuildWithPens`. `ViewProbe.Named<T>` is `FindControl<T>` and throws when the name is absent or the
    type does not match (`ViewProbe.cs:27-31`); `Descendant<T>` and `Descendants<T>` (`:34-45`).
  - `HeadlessInput.Click` (`HeadlessInput.cs:16-59`) and `Press(TopLevel, PhysicalKey[, RawInputModifiers])`
    (`:87-97`).
  - Model for the virtualisation guard: `PenListViewTests.AFullCatalogue_RealisesOnlyTheRowsOnScreen`
    (`PenListViewTests.cs:433-448`).
  - `ThemeTests.EveryListSurface_PaintsItselfFromThePalette` (`ThemeTests.cs:205-242`) builds its own `ListBox`;
    its comment names the export list as the surface it stands for. `APaletteKeyCarryingOpacity_KeepsItUnderBothVariants`
    carries `ListBoxItemSelectedBackground` and `ListBoxItemSelectedPointeroverBackground` among six keys
    (`:481-494`, `:482-483`).
- Docs: the export paragraph `ui-theme.md:286-303` (the list at `:297-303`), the palette rows `:60-61`, the six
  pen-editor-and-export theme tests `:123-133` (`:124` the list test), the translucent count `:134-136`, the corner
  radius exception `:166-168`; the pen checklist row `overview.md:1065`.
- Rules applied: `desktop-ui` One state per row (`SKILL.md:192-199`); `avalonia.md` Lists (a bare `ItemsControl`
  needs an enclosing `ScrollViewer` to virtualise), Views and code-behind (input interop only, pattern-matched
  `DataContext`, every subscription paired with its removal, constructor `+=` only for the control's own events);
  `base.md` Dead code; `AGENTS.md` UI (every colour resolves to a palette key) and Test (HeadlessInput, traits).

## Development Approach

- **Testing approach**: TDD. Each task writes its tests first; a view test that reads a control by a name or type
  the change introduces fails at HEAD at run time, which is its red state.
- Complete each task fully before the next; every task ends with its tests passing.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every assertion of the nine existing list tests keeps its meaning: only the control each one reads changes.

## Testing Strategy

- **View tests**: `CsvExportWindowTests` gains the one-state reproduction, the click, the virtualisation guard and
  the keyboard walk; its nine list reads move to the new controls.
- **Theme tests**: `ThemeTests` loses the `ListBox` surface test and two opacity cases with the keys.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- New tests join `CsvExportWindowTests`, so they carry its traits and its collection.
- Every click and key press goes through `HeadlessInput` (`AGENTS.md`, Test). Moving the focus to the start
  control before a keyboard walk is `Control.Focus()`, a set-up step, not a gesture.

## Acceptance Evidence

The defect is automatable. At HEAD the new `CsvExportWindowTests.ThePenList_IsCheckboxesWithNoRowSelection`
compiles and fails at run time: `Named<Border>(window, "ExportPens")` throws, because `ExportPens` is a `ListBox`
holding three `ListBoxItem`s.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~CsvExportWindowTests|FullyQualifiedName~CsvExportViewModelTests|FullyQualifiedName~ThemeTests|FullyQualifiedName~MessagePanelViewTests"
```

It must report 0 failed. These named tests must be present and pass:

- `CsvExportWindowTests.ThePenList_IsCheckboxesWithNoRowSelection` (the reproduction) — Task 1
- `CsvExportWindowTests.AClickOnAPenLabel_TogglesItsTickAndSelectsNoRow` — Task 1
- `CsvExportWindowTests.ThePenList_PaintsNoGroundOfItsOwnAndItsBorderFromThePalette` — Task 1
- `CsvExportWindowTests.AFullCatalogueOf500Pens_RealisesOnlyTheRowsOnScreen` — Task 1
- `CsvExportWindowTests.ThePenList_GrowsWithItsRowsUpToItsMaxHeight` (moved to the `Border`) — Task 1
- `CsvExportWindowTests.ThePenList_MaxHeight_ShowsExactlyEightRowsOf28Pixels` (the row is the panel's container) — Task 1
- `CsvExportWindowTests.ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder` (unchanged body) — Task 1
- `CsvExportWindowTests.ThePenList_WithTwoPens_IsBelowItsMaxHeight` — Task 1
- `CsvExportWindowTests.ThePenList_NeverAutoHidesItsScrollBar` (reads `ExportPensScroller`) — Task 1
- `CsvExportWindowTests.TheMessageRow_SitsAboveTheButtons_AndSpansTheContentWidth` — Task 1
- `CsvExportWindowTests.TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge` (theory: `ru`, `en`) — Task 1
- `CsvExportWindowTests.ALongPenLabel_IsTrimmedInsideTheList_AndItsToolTipCarriesTheWholeLabel` (reads `ExportPensScroller`) — Task 1
- `CsvExportWindowTests.WhileAnExportRuns_SaveAsIsDisabledAndCancelIsEnabled_AndTheReverseHoldsAfterIt` — Task 1
- `CsvExportWindowTests.TwoPensSharingAName_ShowTheirIdsInTheChecklist` (unchanged) — Task 1
- `CsvExportWindowTests.ThePensHeader_ReadsTheTickedAndTotalCounts_AndFollowsAClick` (unchanged) — Task 1
- `CsvExportWindowTests.TheSaveAsButton_IsDisabledWhenNoPenIsTicked` (unchanged) — Task 1
- `CsvExportWindowTests.TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder` — Task 2
- `CsvExportWindowTests.SpaceOnAFocusedPenBox_TogglesItsTick` — Task 2
- `ThemeTests.APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` (four keys) — Task 3
- `ThemeTests.EveryColourPickerSurface_PaintsItselfFromThePalette` (unchanged) — Task 3

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the two NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -n "ListBox" -- SemiPlot/SemiPlot.UI                # prints nothing (Task 3's fallback: only Palette.axaml lines)
git grep -n "ListBox" -- SemiPlot/SemiPlot.Tests.Unit/UI/Export   # prints nothing
git grep -nE "<(ListBox|DatePicker|TimePicker|Carousel)[ >.]" -- '*.axaml'   # prints nothing
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.UI/Export SemiPlot/SemiPlot.Core/Export ':!SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml' ':!SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs'   # prints nothing
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.DataSource.Postgres ConfigFiles   # prints nothing
```

The look of a row under the pointer and the feel of the arrow keys through a long list cannot be seen headlessly;
the manual checks under Post-Completion cover them.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The list is the message panel's shape inside a border.** `Border` `ExportPens` carries the height cap, the
  border and the grid placement; `ScrollViewer` `ExportPensScroller` carries the three scroll settings as its own
  properties; `ItemsControl` `ExportPenItems` carries `ItemsSource`, the one-Tab-stop setting, the
  `VirtualizingStackPanel` and the item template. Each row is the `CheckBox` and nothing else: no container that
  selects, no selection fill.
- **The view model does not change.** It reads `IsTicked` and never read a selection (Context).

Design decisions, each with its reason:

- **An `ItemsControl`, not a `ListBox` with its selection suppressed.** Avalonia has no selection mode that turns
  selection off, and a `ListBoxItem` keeps its own focus, Space and click handling beside the checkbox's, so the
  row would still carry two states with one of them hidden. `desktop-ui` allows a checkbox list with no row
  selection (`SKILL.md:192-199`); an `ItemsControl` has no selection to suppress.
- **The `Border` wraps the `ScrollViewer`, and the name `ExportPens` moves to the `Border`.** `ScrollViewer` draws
  no border of its own, and an `ItemsControl` inside the scroller would put its border on the scrolled content.
  The `Border` is the control the operator sees as the list: its bounds are the 226 px cap, its right edge is the
  form's edge, and it greys out with the form. The existing tests that measure the list keep reading
  `ExportPens`.
- **The 6 px inset moves to `Margin="6,0"` on the template's `CheckBox`.** It was the `ListBoxItem`'s padding
  (`CsvExportWindow.axaml:36`); the `MinHeight="0"` setter has nothing left to zero. The value stays off the
  4/8/12/16/24/32 scale: the ui-spacing-scale slice owns every remaining inset.
- **No row fill under the pointer.** A hovered `ListBoxItem` drew Semi's row overlay; a checkbox row shows the
  checkbox's own hover state only. A row fill would read as a selectable row, the state this slice removes.
- **The list paints no ground of its own.** The `Border` sets no `Background`, so the window ground shows through,
  as around every other form control.
- **The list is one Tab stop; Up, Down, Home and End move focus between its boxes.** `ExportPenItems` sets
  `KeyboardNavigation.TabNavigation="Once"`, as the `ListBox` did, so Tab enters at a box and Tab or Shift+Tab from
  any box leaves the list, whatever the pen count. Focus is no row state: it paints the checkbox's own focus
  visual, no row fill, and nothing reads it (`desktop-ui` One state per row). The keys need a handler, because
  `ItemsControl.OnKeyDown` would scroll the whole list on each press (Context). The constructor adds
  `OnPenItemsKeyDown` to `ExportPenItems` with `AddHandler(KeyDownEvent, ..., RoutingStrategies.Tunnel)`, so it runs
  ahead of that class handler. For `Key.Up`, `Key.Down`, `Key.Home`, `Key.End`, `Key.PageUp` or `Key.PageDown` with no
  modifier, from a box inside a row container (the ancestor whose visual parent is `ExportPenItems.ItemsPanelRoot`),
  it sets `e.Handled`. With `index = IndexFromContainer(row)` and the target `index - 1`, `index + 1`, `0`,
  `ItemCount - 1`, or `index` minus or plus the whole rows the viewport holds (clamped to the ends), it does nothing
  past either end; otherwise it calls `ScrollIntoView(target)` and focuses the `CheckBox` of
  `ContainerFromIndex(target)` with `NavigationMethod.Directional`. Space stays the checkbox's own. The page keys are
  the handler's because the `ScrollViewer`'s page scroll leaves the focus on a box the list then recycles. Input interop is what code-behind may hold
  (`avalonia.md`); the handler reads no view model. `ExportPenItems` lives as long as the window and the handler is
  on its own event, so the registration needs no paired removal.
- **The five `ListBoxItem*` keys go, with `EveryListSurface_PaintsItselfFromThePalette` and the two opacity
  cases.** A palette key belongs where a control of this tree reads it (`ui-theme.md:29-30`); after the change no
  control does: the colour picker's palette `ListBox` paints from its own item theme, and the tree holds no
  `ListBox`, `DatePicker`, `TimePicker` or `Carousel` (Context). A test whose only subject is a control the tree no
  longer holds gates nothing (`base.md`, Dead code). The `CheckBox` and the `ScrollViewer` thumb, the list's
  remaining surfaces, stay gated by `EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette` and
  `EveryToggleAndScrollSurface_PaintsItselfFromThePalette` (`ui-theme.md:131-133`, `:115-118`).

## Technical Details

### `Export/CsvExportWindow.axaml`

- Delete the style `ListBox#ExportPens ListBoxItem` (`:35-38`); the other two window styles stay.
- Replace the `ListBox` (`:149-174`) with:

  ```xml
  <Border Grid.Row="5"
          Grid.ColumnSpan="2"
          Name="ExportPens"
          BorderThickness="1"
          BorderBrush="{DynamicResource AppBorderBrush}"
          MaxHeight="226">
      <ScrollViewer Name="ExportPensScroller"
                    HorizontalScrollBarVisibility="Disabled"
                    VerticalScrollBarVisibility="Auto"
                    AllowAutoHide="False">
          <ItemsControl Name="ExportPenItems"
                        KeyboardNavigation.TabNavigation="Once"
                        ItemsSource="{Binding Pens}">
              <ItemsControl.ItemsPanel>
                  <ItemsPanelTemplate>
                      <VirtualizingStackPanel />
                  </ItemsPanelTemplate>
              </ItemsControl.ItemsPanel>
              <ItemsControl.ItemTemplate>
                  <DataTemplate x:DataType="local:ExportPenChoice">
                      <CheckBox Height="28"
                                Margin="6,0"
                                IsChecked="{Binding IsTicked}"
                                ToolTip.Tip="{Binding Label}">
                          <TextBlock Text="{Binding Label}"
                                     TextTrimming="CharacterEllipsis" />
                      </CheckBox>
                  </DataTemplate>
              </ItemsControl.ItemTemplate>
          </ItemsControl>
      </ScrollViewer>
  </Border>
  ```

- Inner height at the cap: 226 - 2 = 224 px, eight 28 px rows, as before.

### `Export/CsvExportWindow.axaml.cs`

- The constructor adds `ExportPenItems.AddHandler(KeyDownEvent, OnPenItemsKeyDown, RoutingStrategies.Tunnel);`
  beside the existing `+=` lines (`:19-26`).
- `OnPenItemsKeyDown(object? sender, KeyEventArgs e)`: as the Solution Overview's keyboard decision states. The
  row container is found by walking `e.Source`'s visual ancestors to the one whose `VisualParent` is
  `ExportPenItems.ItemsPanelRoot`; a key from anywhere else, or any other key, returns untouched. The target
  index comes from one `switch` on `e.Key`.

### Test reads of the list

| Line | Today | After |
| --- | --- | --- |
| `:407`, `:445`, `:489`, `:548`, `:628`, `:648`, `:722` | `Named<ListBox>(window, "ExportPens")` | `Named<Border>(window, "ExportPens")` |
| `:426` | the first `ListBoxItem`'s height | `Named<ItemsControl>(window, "ExportPenItems").ContainerFromIndex(0)!.Bounds.Height` |
| `:506`, `:649` | `Descendant<ScrollViewer>(list, "PART_ScrollViewer")` | `Named<ScrollViewer>(window, "ExportPensScroller")` |

### ASSUMPTIONs, each with its guard and fallback

- ASSUMPTION: the `VirtualizingStackPanel` virtualises inside the plain `ScrollViewer`: over 500 pens it realises
  the rows on screen and fewer than 16 `CheckBox`es, two viewports of eight rows. Guard:
  `AFullCatalogueOf500Pens_RealisesOnlyTheRowsOnScreen`. Fallback: the `ItemsControl` takes a `Template` of
  `ScrollViewer` `ExportPensScroller` around an `ItemsPresenter` `PART_ItemsPresenter`, the shape of a `ListBox`
  template, so the presenter is the scroller's direct content; the `Border` stays outside; the two
  `ExportPensScroller` reads become `Descendant<ScrollViewer>(Named<ItemsControl>(window, "ExportPenItems"),
  "ExportPensScroller")`, and the other rows of the test table keep their names. The run records the realised
  count either way under Task 1.
- ASSUMPTION: with `TabNavigation="Once"` on `ExportPenItems`, Tab from `ExportEndTime` focuses the first box at
  offset 0, Tab from any box focuses `ExportSaveAs`, Shift+Tab from any box focuses `ExportEndTime`, and Shift+Tab
  from `ExportSaveAs` returns to the box focused last. Guard: `TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder`.
  Fallback: if an exit step fails, `OnPenItemsKeyDown` also takes `Key.Tab` from a box: forward it focuses
  `KeyboardNavigationHandler.GetNext(ExportCount, NavigationDirection.Next)`, with Shift
  `KeyboardNavigationHandler.GetNext(ExportPens, NavigationDirection.Previous)`, and sets `e.Handled`. If only the
  re-entry step fails, the test asserts a box of `Pens` inside the viewport instead of `Pens[39]`, and the run
  records a ⚠️ under Task 2.
- ASSUMPTION: neither the `ScrollViewer` nor the `ItemsControl` is a tab stop under Semi, so Tab from
  `ExportEndTime` lands on the first box (`ExportEndMarker` is disabled without markers). Guard: the first step of
  `TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder`. Fallback: `Focusable="False"` on whichever of the two took
  the focus.
- ASSUMPTION: Space on a focused `CheckBox` toggles it, as on a `ToggleButton`. Guard:
  `SpaceOnAFocusedPenBox_TogglesItsTick`. Fallback: none in this slice; the run records a ⚠️ and Task 5 states that
  a click is the only way to toggle a pen.
- ASSUMPTION: no control left in the tree reads a `ListBoxItem*` key. The colour picker and the `Semi.Avalonia`
  readers are ruled out from the decompiled themes (Context). Guard: the full `ThemeTests` class and the full unit
  suite after the keys go, and `git grep -nE "<(ListBox|DatePicker|TimePicker|Carousel)[ >.]" -- '*.axaml'` prints
  nothing after Task 1. Fallback: restore the five keys in both variants, keep
  `EveryListSurface_PaintsItselfFromThePalette` and the two opacity cases, rename the consumer in its comment and in
  `ui-theme.md:60-61` to the control that read them, and record the measured reader as a ⚠️ under Task 3.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): the list's controls, the test reads, the keyboard handler and its
  guard, the palette keys and their tests, the architecture docs.
- **Post-Completion** (no checkboxes): the look and the keyboard feel in the running viewer.

## Implementation Steps

### Task 1: Rebuild the pen list as checkboxes with no row selection, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`

- [x] write the failing `ThePenList_IsCheckboxesWithNoRowSelection` (`BuildWithPens(3)`): `Named<Border>(window,
      "ExportPens")` exists; `Descendants<SelectingItemsControl>(list)` and `Descendants<ListBoxItem>(list)` are
      empty, where `list` is that `Border` (the window's two marker `ComboBox`es are `SelectingItemsControl`s and
      stay); every realised row of `ExportPenItems` (`ContainerFromIndex(0..2)`) holds exactly one focusable
      control, a `CheckBox` whose `DataContext` is `viewModel.Pens[i]` (red at HEAD: `Named<Border>` throws)
- [x] write the failing `AClickOnAPenLabel_TogglesItsTickAndSelectsNoRow` (`BuildWithPens(3)`): `HeadlessInput.Click`
      on the second box's label flips `viewModel.Pens[1].IsTicked` to true and the header to
      `Resources.FormatExportPensHeader(2, 3)`; a second click flips it back and the header to
      `Resources.FormatExportPensHeader(1, 3)`; `Descendants<SelectingItemsControl>(Named<Border>(window,
      "ExportPens"))` is still empty
- [x] write the failing `ThePenList_PaintsNoGroundOfItsOwnAndItsBorderFromThePalette`: the `Border`'s `Background`
      is null, its `BorderThickness` is 1 and its `BorderBrush` is the brush
      `ExportPens.TryFindResource("AppBorderBrush", ExportPens.ActualThemeVariant, out var brush)` returns
- [x] write the failing `AFullCatalogueOf500Pens_RealisesOnlyTheRowsOnScreen` (`BuildWithPens(500)`): the realised
      `CheckBox` count of `ExportPenItems` is positive and below 16, two viewports of eight rows (the
      virtualisation ASSUMPTION's guard)
- [x] move the nine reads as the Technical Details table states; `ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder`
      keeps its body with the `Border`; `using Avalonia.Controls.Primitives` stays for `ScrollBar` and
      `SelectingItemsControl`
- [x] replace the `ListBox` and delete its window style as Technical Details states
- [x] run `CsvExportWindowTests` and `CsvExportViewModelTests`: every case passes; note under this task the realised
      count of the 500-pen test and whether the virtualisation fallback ran
      (91 passed, 0 failed; the 500-pen list realised 8 `CheckBox`es; the virtualisation fallback did not run)
- ⚠️ the reproduction asserts no `ISelectable` descendant in place of no `ListBoxItem`, so the Task 4 gate
      `git grep -n "ListBox" -- SemiPlot/SemiPlot.Tests.Unit/UI/Export` can print nothing; `TabNavigation="Once"` is left
      to Task 2, which owns the keyboard

### Task 2: Make the list one Tab stop and walk its boxes with the arrow keys, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs`

- [x] write `TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder` (`BuildWithPens(40)`): `ExportEndTime.Focus()`;
      `HeadlessInput.Press(window, PhysicalKey.Tab)` focuses the box of `Pens[0]`; the first `PhysicalKey.ArrowDown`
      focuses `Pens[1]` with `ExportPensScroller.Offset.Y` still 0; each of 38 more focuses `Pens[i]` inside the
      scroller's viewport, `Offset.Y` positive from `i` = 8; one more ArrowDown keeps `Pens[39]`; Tab focuses
      `ExportSaveAs`; Shift+Tab focuses `Pens[39]`; each of 39 `PhysicalKey.ArrowUp` presses focuses `Pens[i - 1]`
      inside the viewport; `PhysicalKey.End` focuses `Pens[39]`, `PhysicalKey.Home` `Pens[0]`; Shift+Tab focuses
      `ExportEndTime`. The focused element is read through
      `TopLevel.GetTopLevel(window)!.FocusManager!.GetFocusedElement()`. Shift+Tab goes through the `Press`
      overload with `RawInputModifiers.Shift`
- [x] write `SpaceOnAFocusedPenBox_TogglesItsTick` (`BuildWithPens(3)`): Tab from `ExportEndTime` to the first box,
      `HeadlessInput.Press(window, PhysicalKey.Space)`: `Pens[0].IsTicked` goes from true to false and the header
      reads `Resources.FormatExportPensHeader(0, 3)`
- [x] set `KeyboardNavigation.TabNavigation="Once"` on `ExportPenItems`; add `OnPenItemsKeyDown` and its tunnel
      registration in the constructor as Solution Overview states
- [x] run both: if a Tab step fails, apply the Tab ASSUMPTION's fallback; if Space fails, record the ⚠️ the Space
      ASSUMPTION names
      (red at HEAD: the walk failed, the first ArrowDown left the focus off the boxes; Space already passed, the
      checkbox's own handling; after the change both pass)
- [x] run `CsvExportWindowTests`: every case passes, the Task 1 tests included; note under this task whether the Tab
      fallback ran
      (`CsvExportWindowTests` + `CsvExportViewModelTests`: 93 passed, 0 failed; the Tab fallback did not run)
- [x] ➕ PageUp and PageDown move the focus one viewport of boxes, clamped at the list ends, through
      `OnPenItemsKeyDown`: the scroller's own page scroll left the focus on a recycled box, so the focus was lost.
      The walk test presses PageDown from `Pens[0]` to `Pens[8]` and PageUp back to `Pens[0]`, each inside the
      viewport

### Task 3: Remove the palette keys the list no longer reads

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] delete `EveryListSurface_PaintsItselfFromThePalette` with its comment (`ThemeTests.cs:205-242`) and the
      `InlineData` rows `ListBoxItemSelectedBackground` and `ListBoxItemSelectedPointeroverBackground` (`:482-483`);
      `APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` keeps `AppAccentFillBrush`,
      `ComboBoxItemSelectedBackground`, `DataGridRowSelectedBackground`, `DataGridRowSelectedPointeroverBackground`
- [x] delete the five `ListBoxItem*` brushes from both variants (`Palette.axaml:44-48`, `:134-138`) and the blank line
      each block leaves
- [x] remove any `using` the deletion leaves unused in `ThemeTests.cs` (the build fails on IDE0005 otherwise)
      (none: every `using` and helper the test read has other callers; the build reports 0 warnings)
- [x] run `ThemeTests` and the full `SemiPlot.Tests.Unit`: every case passes; if a test reads one of the deleted
      keys, apply the key ASSUMPTION's fallback
      (`ThemeTests` 23 passed; `SemiPlot.Tests.Unit` 2740 passed, 0 failed; the key fallback did not run)

### Task 4: Verify acceptance criteria

- [x] run the filtered command under Acceptance Evidence: 0 failed, every named test present (note the pass count)
      (124 passed, 0 failed; all 20 named tests passed, the two theories in every case)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
      (2740 passed, 0 failed)
- [x] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the two NU1902 for `MessagePack` 2.5.302 in
      `SemiPlot.AppHost` that the base reports
      (0 errors, 2 warnings, both the base's NU1902 for `MessagePack` 2.5.302; no package version changed)
- [x] set `$BASE` to `931841c`, this branch's start (Context); if `git merge-base --is-ancestor 931841c HEAD` exits 1
      after a rebase, set it to `$(git merge-base HEAD origin/master)`
      (`--is-ancestor` exited 0; `$BASE` = `931841c`)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse` over every
      `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
      (format exit 0; terse exit 0 over `CsvExportWindowTests.cs`, `ThemeTests.cs`, `CsvExportWindow.axaml.cs`)
- [x] run the HeadlessInput gate, the three `ListBox` greps and the two scope gates under Acceptance Evidence: each
      prints what it must
      (HeadlessInput gate: only `HeadlessInput.cs` lines; the three `ListBox` greps and the two scope gates print
      nothing)

### Task 5: [Final] Update documentation

- [x] `docs/architecture/ui-theme.md:297-303`: the list is a `Border` `ExportPens` with `AppBorderBrush` and the
      226 px `MaxHeight` (eight 28 px rows plus the border), around `ScrollViewer` `ExportPensScroller` (vertical bar
      `Auto`, `AllowAutoHide="False"`, horizontal disabled) around `ItemsControl` `ExportPenItems` with a
      `VirtualizingStackPanel`; each row is the `CheckBox` alone, `Margin="6,0"` off the border, with no row
      selection and no row fill under the pointer (`desktop-ui` One state per row); the list is one Tab stop
      (`TabNavigation="Once"`); Up, Down, Home and End move focus between the boxes through `OnPenItemsKeyDown`, a
      tunnel handler, because `ItemsControl`'s own handler would scroll the whole list on each press; Space toggles
      the focused box; PageUp and PageDown move the focus one viewport of boxes. Drop the window-scoped `ListBoxItem` style
      sentence
- [x] `ui-theme.md:60-61`: delete the two `ListBoxItem*` rows; `:123-125`: the export window's controls have five
      tests of their own, the `ListBox` row test gone; `:134-136`: four translucent keys; `:166-168`: the corner radius
      sentence keeps one exception, the combo box's drop-down and the picker's flyout at Semi's 6 px (if Task 3's
      fallback ran, keep the rows and the test with the measured consumer instead)
- [x] `docs/architecture/overview.md:1065`, the pen checklist row: the list carries one state per row, the tick, with
      no row selection; every box is reachable from the keyboard: the list is one Tab stop, the arrow keys move
      between boxes and Space toggles one; the list virtualises its rows
- [x] if a Task 1 or Task 2 fallback ran, state the built behaviour in the matching `ui-theme.md` sentence
      (no fallback ran; `ui-theme.md` states the reproduction's `ISelectable` check)
- [x] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** in the running viewer (`File` -> `Export...` with 12 or more pens), in both languages and
both theme variants:

1. The list shows its border, eight rows, and a vertical scroll bar that stays visible; no horizontal bar.
2. Click a pen's label: its box ticks and the header count follows. The row shows no selection fill, before or
   after the click; under the pointer only the box changes.
3. A long pen name ends in an ellipsis; its tooltip shows the whole name.
4. Tab from the end time: focus lands on the first pen; Down and Up move one box and the list scrolls with them;
   Tab from any box moves to `Save as...`; Space toggles the focused box.
5. While an export runs, the list greys out with the rest of the form.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: axis-scale-checkbox-column (the axis
scale panel's log checkbox in the field column, with a label in the label column); ui-spacing-scale (every
remaining inset onto the 4/8/12/16/24/32 scale, the `12,2` button padding with them).

**Executed by exec:**
- branch: export-pen-choice-list

## Verify it yourself

1. One state per row. In `File` -> `Export...`, click a pen: it ticks and nothing is highlighted. At
   `pen-list-filter` the clicked row also shows the selection fill
   (`CsvExportWindowTests.ThePenList_IsCheckboxesWithNoRowSelection`,
   `AClickOnAPenLabel_TogglesItsTickAndSelectsNoRow`).
2. The list keeps its shape: eight rows, the scroll bar always visible, long names trimmed with a tooltip
   (`ThePenList_MaxHeight_ShowsExactlyEightRowsOf28Pixels`, `ThePenList_NeverAutoHidesItsScrollBar`,
   `ALongPenLabel_IsTrimmedInsideTheList_AndItsToolTipCarriesTheWholeLabel`).
3. Keyboard. Tab from the end time enters the list once; Down and Up walk every pen box in order, scrolling as
   they go; Home and End jump to the ends; PageDown and PageUp move the focus eight boxes and keep it;
   Tab leaves to `Save as...`; Space toggles the focused box
   (`TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder`, `SpaceOnAFocusedPenBox_TogglesItsTick`).
4. A large catalogue stays light: 500 pens realise only the rows on screen
   (`AFullCatalogueOf500Pens_RealisesOnlyTheRowsOnScreen`).

Run all of it: the Acceptance Evidence command of this plan (124 passed, 0 failed on `ee26a69`).
