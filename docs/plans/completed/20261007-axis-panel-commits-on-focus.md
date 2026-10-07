# Axis scale panel commits on focus loss, logarithmic scale wording

## Overview

The axis scale panel (`Chart/AxisScalePanel`) writes its two bounds only through its Apply button,
which also closes the panel. The log axis checkbox in the same panel is checked against the bounds the
chart already holds. On a manual 0..100 pen, typing 1 into Minimum and ticking the box is refused with
the log-minimum message: the typed 1 is still only text (observed on the demo stand, 2026-10-07). The
operator has to press Apply, which closes the panel, reopen it and tick again.

The panel drops Apply and behaves like an ordinary form:

- the bound pair is written when keyboard focus leaves both bound fields, or on Enter, if the pair is
  valid and differs from what the chart holds; the panel stays open and re-seeds from the chart;
- a press on the panel's empty area takes focus off the fields, so it writes too;
- ticking the log box writes a pending valid pair first and then switches the axis on; unticking
  switches the axis off first and then writes the pending pair under the linear rule;
- a dismiss that closes the panel (a click outside, the window losing focus or activation) writes a
  pending valid pair;
- Escape closes the panel and writes nothing;
- a field that is invalid when the panel closes is discarded.

Autoscale and Restore initial scale keep acting and closing the panel. The Settings window keeps its
Save button: it writes YAML through a staged copy that the production loaders validate, and almost
every value takes effect at the next start. The pen editor already writes on focus loss. Axis labels
that a coarse mask cannot show stay unlabelled.

The operator text uses the industry wording, the form Excel, MasterSCADA 4D and Simple-Scada 2 use: the
panel box reads «Логарифмическая шкала» / "Logarithmic scale". The pen editor column and form label read
«Начальная шкала, логарифмическая» / "Initial scale, logarithmic", beside «Начальная шкала, от» and
«Начальная шкала, до», since all three are start values. The rule message reads «Для логарифмической шкалы
минимум должен быть больше нуля» / "A logarithmic scale needs a minimum above zero". The labels leave the
base of the logarithm out; the docs state it: the scale is base 10 (`docs/architecture/charting.md`).

This is the second plan on branch `log10-y-axis`, after `docs/plans/20261006-log10-y-axis.md`.

## Context (from discovery)

- `SemiPlot/SemiPlot.UI/Chart/AxisScalePanelViewModel.cs`:
  - `:30-32` `ApplyCommand` with `canExecute` on `IsValid`; `:36` `CancelCommand` is `RequestClose`;
    `:37` `ToggleLogarithmicCommand`;
  - `:69-92` the two text properties; `:84-87` a real minimum change clears the refusal flag;
  - `:107-109` `IsMinimumValid`, `IsValid`; `:111-133` `ValidationMessage`; `:140-143`
    `BreaksLogMinimum`, which reads `IsLogarithmic`;
  - `:146-167` `Seed`, which stores a `SeededBound` (text and exact value) per field and sets `_penId`;
  - `:174-181` `Apply` writes `SetAxisLimits` and closes; `:183-191` `ActOnThePen` (Autoscale, Restore)
    acts then closes; `:193-211` `ToggleLogarithmic` calls `SetLogarithmic` and re-seeds, or marks the
    refusal; `:221-225` `RequestClose` clears `_penId` before it raises the close request;
  - `:267-286` `TryReadRequired` maps a field still showing its seed text to the seed's exact value.
- `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`: `:13-16` Escape -> `CancelCommand`; `:55-58`,
  `:72-75` Enter in each field -> `ApplyCommand`; `:78-83` the log box, `IsChecked` `Mode=OneWay`,
  `Command` its one writer; `:87-90` the reserved message line; `:114-121` the Apply button row.
  `AxisScalePanel.axaml.cs:5-11` holds no logic. Compiled bindings are on, so a binding to a removed
  command fails the build.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml:16-20` declares the panel in a `Flyout`
  (`Placement="Pointer"`); `TrendChartView.axaml.cs:34` holds it as a `PopupFlyoutBase`, `:48-49`
  resolves it, `:136` sets the panel's DataContext to `AxisScalePanelViewModel`, `:144-147` hides it on
  `CloseRequests`, `:268-274` seeds and shows it.
- The pen editor's commit-on-focus-loss pattern, `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`:
  `:59-63` a tunnel `PointerPressed` handler and `:136-149` `EndEditOnPressOutsideFocusable`, which
  clears focus on a press outside any focusable control; `:203-221` the scale pair ends its edit only
  when focus leaves both boxes.
- `AGENTS.md:347-348` lists the view models code-behind reports failures through.
- Avalonia 12.0.5, read from the installed package:
  - a light dismiss (an overlay press, a non-client click, the window losing focus or activation)
    raises the flyout's `Closing`, then detaches the content, which raises the focused TextBox's
    `LostFocus`, then `Closed`;
  - a press on the CheckBox moves focus to it before the click and the command run (the click fires on
    release);
  - the UserControl's Escape `KeyBinding` runs before `FlyoutPresenter`'s own Escape handling.
- Resources: `AxisScaleApply` (`Resources.resx:120`, `Resources.ru.resx:120`), `AxisScaleLogarithmic`
  ("Logarithmic" / «Логарифмическая», `:136`), `ScaleLogMinimumPositive` (`:139`),
  `PenEditorColumnLogScaleOnStart` ("Log scale on start" / «Лог. шкала при запуске», `:226`), which is
  also the form label (`PenEditorWindow.axaml:440`).
- Tests bound to Apply: `ApplyCommand`/`AxisScaleApplyButton` appear 14 times in
  `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewModelTests.cs` and 4 times in
  `AxisScalePanelViewTests.cs` (verified 2026-10-07). View tests whose outcome reverses:
  `TypedBoundsAndTheApplyButton_WriteAManualScaleAndClosePanel` (`:63-80`),
  `AFractionInTheCurrentCulturesSeparator_IsWrittenByEnter` (`:96`),
  `AClickOutsideThePanel_ClosesItAndWritesNothing` (`:141-153`),
  `EnterOnTheFocusedAutoscaleButton_Autoscales_AndDoesNotApplyTheFields` (`:226-242`); kept green:
  `WhenThePenIsHiddenWhileThePanelIsOpen_ThePanelClosesWithoutWriting` (`:263-276`). View-model test
  `Apply_WithBothFieldsUntouched_KeepsTheExactBoundsAMaskRounded` (`:123-137`).
- Docs that describe the panel or the wording: `docs/architecture/trend-interaction.md:142` and
  `:214-252` (`#the-axis-scale-panel`), `docs/architecture/ui-theme.md:233-267` (layout, the measured
  360 x 264 px panel at `:248-250`) and `:299-317` (pen editor widths, the message figure at `:313`),
  `docs/architecture/ui-text.md:240` (cites `AxisScaleApply`), `docs/architecture/trend-feature-spec.md:63-64`,
  `:75`, `docs/architecture/charting.md`, `readme.md:33-34`.

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task)
- complete each task fully before moving to the next; every task ends with its tests green
- this plan's commits land on `log10-y-axis` beside the first plan's; `ship` finds both plans by the
  branch record
- update this plan when scope changes during implementation

## Testing Strategy

- **unit** (`SemiPlot.Tests.Unit`): view-model tests with `[AvaloniaFact]`; headless view tests drive
  every click, typed text and key press through `UI/HeadlessInput.cs`; new test classes carry the
  `Component`, `Area` and `Category` traits
- no container test changes: the panel writes session state only

## Acceptance Evidence

Today: on a manual 0..100 pen the panel refuses "type 1 into Minimum, tick the log box" with the
log-minimum message and the axis stays linear; the bounds reach the chart only through Apply, which
closes the panel.

Automated, after the change:

1. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AxisScalePanel"`
   passes, including these view tests:
   - manual 0..100: type 1 into Minimum, click the log box: the axis is logarithmic from 1, the panel
     open, the message line empty;
   - logarithmic 1..100: type 0 into Minimum, click the log box: the axis is linear from 0;
   - manual 0..100: type 50, press on the panel's empty area: 50..100 is written, the panel open;
   - manual 0..100: type 50 into Maximum, Tab to Minimum: nothing is written yet; Tab again: 0..50 is
     written;
   - manual 0..100: type 50, dismiss the flyout from outside: 50..100 is written in one axis-model step;
   - manual 0..100: type 50, press Escape: nothing is written, the panel closed;
   - manual 0..100: type 150 (above the maximum), dismiss: nothing is written;
   - autoscaled pen: Tab through both fields and press Enter without typing: the pen stays `Auto`;
   - manual 0..100: type "abc" into Minimum, click the log box: the toggle is refused, the text stays;
   - the panel and its presenter keep their size while the message line fills.
2. `git grep -nE "AxisScaleApply|ApplyCommand" -- SemiPlot docs ':!docs/plans'` prints nothing.
3. After task 6, `git grep -nE "«Логарифмическая»|Лог\. шкала при запуске|Лог\. шкале нужен|Log scale on start|log10 при запуске|Шкала log10|Начальная шкала, log10|Для логарифмического масштаба|Log10 scale|Initial scale, log10|an axis minimum above zero" -- SemiPlot docs readme.md ':!docs/plans'`
   prints nothing.

Manual smoke on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. Activate `Damper 01`, click its axis, type 1 into Minimum, tick «Логарифмическая шкала»: the axis turns
   logarithmic from 1, the panel stays open.
2. Type 80 into Minimum and click the panel's empty area: the axis rescales to 80..100 at once.
3. Type 20, press Escape: the axis stays 80..100.
4. Type 20, click the plot outside the panel: the panel closes and the axis is 20..100.
5. In `Edit` -> `Pens and groups` the column and the form label read «Начальная шкала, логарифмическая»,
   the column header on two lines.
6. Type 0 into Minimum of a log pen and tick nothing: the message line shows the whole rule message
   and the panel does not change size.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with the ➕ prefix, blockers with the ⚠️ prefix
- keep this plan in sync with the work actually done

## Solution Overview

**The bound pair is an edit that ends when focus leaves the pair**, as the pen editor's scale pair does
(`PenEditorWindow.axaml.cs:203-221`): tabbing from Minimum to Maximum writes no half pair. The panel
watches `IsKeyboardFocusWithin` of the grid holding the two fields and calls one view-model method,
`CommitBounds()`, when it turns false. Enter in either field calls it too. The panel is focusable and no
tab stop, so a click on its empty area moves the keyboard to the panel through Avalonia's press-to-focus
and ends the edit, while keys keep routing through the panel and Escape still closes it.

**`CommitBounds()` writes only a real change.** It reads the pair (`SeededBound.TryRead` maps an untouched
field to its seed's exact value), and writes `SetAxisLimits` only when `_penId` is set, the pair is
valid, the log rule admits the minimum, and the read pair differs from the seeded pair's values. A
value retyped in another notation, or focus passing through untouched fields, writes nothing, so an
autoscaled pen stays `Auto`. After a write it re-seeds from the chart and the panel stays open.

**A dismiss writes through the same path.** When the flyout closes on a light dismiss, Avalonia raises
`Closing`, then detaches the panel, which raises the field's `LostFocus` and so the pair's focus-within
change: `CommitBounds()` runs once. The panel has no separate close-time commit. Escape and the panel's
own close requests go through `RequestClose`, which clears `_penId` before the flyout hides, so the
detach-time `CommitBounds()` finds no pen and writes nothing. That invariant, `_penId == null` after
`RequestClose`, is what makes Escape discard and keeps `WhenThePenIsHiddenWhileThePanelIsOpen_...`
green. The host does not handle the flyout's `Closed`: after a light dismiss nothing reads `_penId` until
the next `Seed()` overwrites it.

**The log toggle orders its two writes by direction.** Switching on, it writes the pending pair first, so
a typed positive minimum is in the chart before the log rule checks it, then calls `SetLogarithmic(true)`.
Switching off, it calls `SetLogarithmic(false)` first, then writes the pending pair under the linear rule.
Either way it re-seeds once, after the switch. The toggle refuses over a pending pair that does not read
as a pair: the box stays as it was, the typed text stays, and the message line names the rule the pair
breaks.

**Behaviour change on Enter.** Enter on an untouched autoscaled pen no longer freezes the shown range
into a manual one; Apply used to. Autoscale and Restore initial scale still act and close the panel.

**The wording is the industry form.** Only the resource values change; the keys keep naming the
concept. The labels name the scale type as Excel, MasterSCADA 4D and Simple-Scada 2 do and leave the
base out; `charting.md` states base 10. The pen editor's log column is as wide as its two-line header
needs under the 1.1 margin `docs/architecture/ui-theme.md` documents, as its neighbours «Начальная шкала,
от/до» are, and the window grows with it while it stays under the 1280 px screen. The Russian rule
message is longer than the panel's width allows on one line, so the panel's reserved message line holds
two lines: the panel keeps one size whether the line is empty, holds a one-line rule or the two-line log
rule.

## Technical Details

- Remove: `ApplyCommand`, `Apply`, the Apply button row, the `AxisScaleApply` resource in both
  languages, and `IsValid` if `CommitBounds` does not use it as its guard.
- Add to the view model: `CommitBounds()` (`void`), `CommitBoundsCommand` without `canExecute`, and
  `ReportFailure(Exception)` forwarding to `TrendChartViewModel.ReportFailure`.
- `ToggleLogarithmic`: the direction-ordered sequence above, the refusal over a pending pair that does
  not read as a pair, and the existing refusal over a non-positive minimum when switching on.
- View (`AxisScalePanel.axaml(.cs)`): Enter in both fields binds to `CommitBoundsCommand`; the bounds
  grid gets a name and a synchronous `IsKeyboardFocusWithin` handler calling `CommitBounds`; the panel is
  `Focusable="True" IsTabStop="False"`, so a press outside a focusable control focuses the panel; the
  handler pattern-matches `DataContext is AxisScalePanelViewModel` and reports a caught exception through
  its `ReportFailure`.
- `AGENTS.md:347-348` adds `AxisScalePanelViewModel.ReportFailure` to the code-behind routes.
- Resources:

  | Key | en | ru |
  | --- | --- | --- |
  | `AxisScaleLogarithmic` | Logarithmic scale | Логарифмическая шкала |
  | `PenEditorColumnLogScaleOnStart` | Initial scale, logarithmic | Начальная шкала, логарифмическая |
  | `ScaleLogMinimumPositive` | A logarithmic scale needs a minimum above zero | Для логарифмической шкалы минимум должен быть больше нуля |

## What Goes Where

- Implementation Steps: code, tests and docs in this repository
- Post-Completion: the manual smoke on the demo stand

## Implementation Steps

### Task 1: One commit path and no Apply

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanelViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `AGENTS.md`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewModelTests.cs`

- [x] add `CommitBounds()`, `CommitBoundsCommand` and `ReportFailure`; remove `ApplyCommand`, `Apply`,
      and `IsValid` unless it becomes the commit guard
- [x] `ToggleLogarithmic` orders its writes by direction and refuses over a pending pair that does not
      read as a pair
- [x] AXAML: remove the Apply row and the `AxisScaleApply` resource; Enter in both fields binds to
      `CommitBoundsCommand`; `AGENTS.md:347-348` names the new `ReportFailure` route
- [x] tests, success: a changed valid pair is written and re-seeded with the panel open; type 1 then
      toggle on is admitted; type 0 then toggle off writes 0 under the linear rule; Escape writes
      nothing
- [x] tests, error and edge: untouched fields and a value retyped in another notation write nothing
      (replacing `Apply_WithBothFieldsUntouched_KeepsTheExactBoundsAMaskRounded`); an inverted pair and
      a minimum the log rule refuses are not written; an unreadable field refuses the toggle and keeps
      the text
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AxisScalePanelViewModel"` -
      must pass before task 2

### Task 2: The panel view ends the edit on focus loss

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`, `AxisScalePanel.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`

- [x] name the bounds grid and call `CommitBounds` when its `IsKeyboardFocusWithin` turns false (the log
      box moved out of that grid into its own row, aligned by a shared label column, so a press on it
      leaves the pair)
- [x] a press on the panel's empty area ends the edit: the panel is focusable and no tab stop, so
      Avalonia's press-to-focus moves the keyboard to it (the panel's background is `Transparent`, so the
      press reaches the panel)
- [x] ➕ Escape after an empty-area press closes the panel, a click from one bound into the other writes
      nothing, and the window losing activation writes the pending pair; the host keeps no `Closed`
      handler
- [x] view tests: the ten cases of acceptance check 1, each through `HeadlessInput`; the light-dismiss
      case asserts exactly one `SetAxisLimits` (one `ScalesRevision` step); the Tab case types into
      Maximum and tabs to Minimum, since `HeadlessInput.Press` sends no Shift+Tab
- [x] rewrite the reversed tests: delete `TypedBoundsAndTheApplyButton_...`; `AFractionInTheCurrentCulturesSeparator_IsWrittenByEnter`
      keeps the panel open; `AClickOutsideThePanel_...` becomes "writes the pending pair and closes";
      `EnterOnTheFocusedAutoscaleButton_...` asserts the 20..90 write when focus leaves the pair, then
      Autoscale; `WhenThePenIsHiddenWhileThePanelIsOpen_...` stays green
- [x] tests for Autoscale and Restore initial scale after typed valid text: their own result wins and
      the panel closes
- [x] ➕ drop the removed Apply button from `AnUnreadableEntry_IsMarkedAndEnterWritesNothing`,
      `AnInvertedPair_ShowsTheMessage...` and `AnEmptyField_ShowsTheMessage...`; since task 1 they fail
      on `FindControl<Button>("AxisScaleApplyButton")`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AxisScalePanel"` -
      must pass before task 3

### Task 3: Name the base of the logarithm

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs` (only if a measured width
  assertion changes)
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml` (the log column and window widths)
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs` (the message-line size test)

- [x] set the three resource values of Technical Details in both languages
- [x] measure with the headless Skia+HarfBuzz method `docs/architecture/ui-theme.md` records: the new
      log box, the panel and presenter without the Apply row, the message in both languages, the column
      header and the form label at `PenEditorWindow.axaml:440`; the column stays 108 px if the header
      keeps the 1.1 margin, otherwise it and the window width grow to the measured need (the Russian
      header's wider line is 88.9 px, 1.06 of the 94 px cell, so the column is 112 px and the window
      1248 px, `MinWidth` 1212 px)
- [x] the panel's message line reserves the height of the longest message as measured (two lines for
      the log rule); a view test asserts the panel keeps one size with the line empty, a one-line rule
      and the log rule, in both languages (the 40 px `form-message` line already holds the two-line log
      rule in both languages; no layout change)
- [x] the 1280 px window test and the panel size test pass
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenEditor|FullyQualifiedName~AxisScalePanel"` -
      must pass before task 4

### Task 4: Update documentation

- [x] `docs/architecture/trend-interaction.md:142` and `#the-axis-scale-panel` (`:214-252`): the pair
      edit ending on focus loss, Enter, the empty-area press, the toggle's ordered writes and its
      refusals, a dismiss writing once (window deactivation included), Escape writing nothing, an
      invalid field discarded, no Apply, Enter no longer freezing an autoscaled range
- [x] `docs/architecture/ui-theme.md:233-267`: the layout without the Apply row and the measured sizes;
      `:299-317`: the header, label and message figures
- [x] `docs/architecture/ui-text.md` (the `AxisScaleApply` citation at `:240` and the new texts),
      `trend-feature-spec.md:63-64`, `:75`, `charting.md` (no change needed), `readme.md:33-34`

### Task 5: Verify acceptance criteria

- [x] run acceptance checks 1-3
- [x] run `dotnet build SemiPlot.slnx` and the whole unit project
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files

### ➕ Task 6: Industry wording for the logarithmic scale

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml` (the log column and window widths)
- Modify: `docs/architecture/ui-text.md`, `ui-theme.md`, `trend-interaction.md`, `trend-feature-spec.md`,
  `charting.md`, `readme.md`, `docs/plans/20261006-log10-y-axis.md` (its operator-facing smoke texts)

- [x] set the three resource values of Technical Details in both languages
- [x] measure with the headless Skia+HarfBuzz method `docs/architecture/ui-theme.md` records: the log box,
      the message in the 360 px panel, the column header in DemiBold and the form label at
      `PenEditorWindow.axaml:440`, in both languages
- [x] the log column keeps 112 px if its wrapped header keeps the 1.1 margin; otherwise the column and the
      window `Width`/`MinWidth` grow to the measured need, the window under the 1280 px test (the Russian
      header needs «Начальная шкала,» on one line, 130.0 px, so the column is 160 px, the window 1272 px
      and `MinWidth` 1260 px)
- [x] docs: the new texts and measurements in `ui-text.md`, `ui-theme.md`, `trend-interaction.md`,
      `trend-feature-spec.md` and `readme.md`; `charting.md` states that the scale is base 10; the first
      plan's smoke steps read the current labels
- [x] run `dotnet build SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes`, `dotnet terse`
      over the touched `.cs` files, the whole unit project and acceptance checks 2 and 3

## Post-Completion

**Open for the operator**: the manual smoke above on the demo stand.

**Delivery**: `ship` delivers this plan with `docs/plans/20261006-log10-y-axis.md`; both move to
`docs/plans/completed/` then.

**Executed by exec:**

- branch: log10-y-axis

## Verify it yourself

1. Automated, on the branch:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AxisScalePanel"
   git grep -nE "AxisScaleApply|ApplyCommand" -- SemiPlot docs ':!docs/plans'
   ```

   On `2b17eba` (before this plan) the panel binds `ApplyCommand` and the grep prints it; on
   `a293213` the filter passes (82 tests) and the grep prints nothing.
   `ATypedPositiveMinimum_ThenTheLogarithmicBox_SwitchesTheAxisOnFromIt` and
   `Escape_AfterAPressOnThePanelsEmptyArea_ClosesThePanel` are the operator's two flows.

2. By hand on the demo stand, the six steps under Acceptance Evidence, Manual smoke. What each shows
   against `2b17eba`:

   - `Damper 01` (manual 0..100): type 1 into Minimum, tick «Логарифмическая шкала». Before: the box
     is refused with the log-minimum message and the axis stays linear. After: the axis turns
     logarithmic from 1 and the panel stays open.
   - Type 80 and click the panel's empty area: the axis rescales at once; before, nothing happened
     until Apply, which also closed the panel.
   - Type 20, press Escape: the axis keeps 80..100. Type 20, click the plot: the panel closes and the
     axis is 20..100.
   - `Edit` -> `Pens and groups`: the column and the form label read «Начальная шкала,
     логарифмическая»; the window opens 1272 px wide.
   - Type 0 into Minimum of a log pen: the two-line rule message shows in full and the panel keeps
     its size.
