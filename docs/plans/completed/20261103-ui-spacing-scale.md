# Every inset in the UI's AXAML on the 4/8/12/16/24/32 scale

## Overview

- 45 `Margin`, `Padding`, `Spacing`, `ColumnSpacing` and `RowSpacing` values in 12 AXAML files of
  `SemiPlot/SemiPlot.UI` carry a number off the `desktop-ui` scale 4, 8, 12, 16, 24, 32. The gate command under
  Acceptance Evidence prints all 45 at `12c7e37`.
- Each value moves to the scale value of the level it belongs to: window margin 16, label to field 12, row
  spacing 8, a section header 16 above and 8 below, a plain list cell inset 8, a gap between controls in one row
  8, an inset inside a control 4.
- 16 buttons in 8 files carry a literal padding that Semi's own button padding replaces: 14 `Padding="12,2"`
  dialog buttons, the status bar's `ConnectionIndicator` and the message panel's `ClearMessages` (`8,2`). Semi's
  padding is `12,0`, on the scale, and the button height stays Semi's 32 px.
- The sidebar's top-row pair keeps its own `Padding` setter, `8,2` to `8,0`: its text buttons have a fixed
  `Height` of 24 inside a 28 px row (`TrendLegendView.axaml:83`, `:102`). Fixed-size icon buttons keep
  `Padding="0"`: the top row in its icon form (`:60`), the fold toggle (`:189`) and the pen editor's
  `GroupActionsButton` (`PenEditorWindow.axaml:610`).
- The reserved message line keeps two 20 px lines (`Styles/Forms.axaml:29-36`); it is a height, not an inset.
- A new `ui-theme.md` section states the rule and its gate, a `git grep` that prints every off-scale inset left
  and must print nothing after this slice.
- Scope guard: no control or behaviour change, no palette change. Only inset values change, plus the tests and
  the docs that pin them.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice ui-spacing-scale

The slice has no issue (`20261102-visual-chore-roadmap.md:276`), so the PR body closes none. Its scope is
`:271-275`, its scope guard `:283`, its risk "widths derived from these insets ... may need re-measuring"
(`:279-280`). The roadmap's table counts "22 off-scale values (20, 14, 10, 6) in 7 AXAML files ... plus
`Padding="12,2"` on 15 buttons in 7 more files" (`:38`), verified on `df129f7` (`:18`). The count on `12c7e37`:

- 14 buttons carry `Padding="12,2"`, in 6 files: `git grep -n 'Padding="12,2"' -- 'SemiPlot/SemiPlot.UI/*.axaml'`
  prints 14 lines.
- 31 further values are off the scale. Besides the 20, 14, 10 and 6 the roadmap counted, 2, 3 and 5 px insets sit
  in the legend, the status bar, the navigation bar and the message panel. The roadmap's parenthetical window
  list (settings dialog, export window, time window panel, axis scale panel, chart view, legend, `:272`) omits
  those three; its scope is "every off-scale ... value left in the UI's AXAML" (`:271`), so they are in.
- The pen editor carries one off-scale value, the refresh button's `Padding="12,2"` (`PenEditorWindow.axaml:104`).

Stacking: this branch, `ui-spacing-scale`, starts at `12c7e37`, the tip of `axis-scale-checkbox-column`
(verified 2026-11-03: `git rev-parse HEAD axis-scale-checkbox-column` prints the same hash). Its merge base with
`master` is `df129f7`.

Rules (`desktop-ui` skill, `SKILL.md`):

- Spacing table `:93-104`: window margin 16; pane padding 16; section header 16 above, 8 below, the first 0
  above; row spacing, plain list cell inset and gap between controls in one row 8; table cell inset 12; label to
  field 12; inside a control 4; slack after the widest field 24; control height 32; reserved message line 40.
- `:90-91`: a child's inset never exceeds its parent's. `:106-107`: a list gets 4 px border padding, so cell text
  sits 4 + 8 from the border. `:109-110`: equal things get equal insets on every side. `:111-112`: an off-scale
  value already in a project (20, 14, 10, 6) is debt, fixed when its window is next touched. `:127-128`: centre a
  glyph with alignment, never with a hand-tuned margin.
- The skill has no button padding row. The levels that apply to a button are "control height 32" (`:103`) and
  the horizontal inset, which the theme supplies.

Semi's button:

- `SemiPlot/Directory.Packages.props:30` pins `Semi.Avalonia` 12.0.3, and
  `SemiPlot/Artifacts/obj/SemiPlot.UI/project.assets.json:710` resolves `Semi.Avalonia/12.0.3`.
- `ilspycmd ~/.nuget/packages/semi.avalonia/12.0.3/lib/net10.0/Semi.Avalonia.dll` decompiles the theme:
  - the `Button` `ControlTheme` sets `Padding` to `{DynamicResource ButtonDefaultPadding}` and `MinHeight` to
    `{DynamicResource ButtonDefaultHeight}`, which is `{StaticResource SemiHeightControlDefault}`;
  - `ButtonDefaultPadding` is `new Thickness(12.0, 0.0, 12.0, 0.0)`;
  - `SemiHeightControlDefault` is `32.0`.
- `ui-theme.md:296` records the same 32 px: "Every control keeps Semi's 32 px height".
- No style in this repository sets a `Button`'s `Padding` except the sidebar's top row
  (`TrendLegendView.axaml:55-62`, `:81-85`); `Styles/Buttons.axaml:1-21` holds only the `destructive` colours.
- Buttons with no padding override already exist: `AxisScaleInitialScaleButton` (`AxisScalePanel.axaml:126-131`),
  the time window panel's two, the navigation bar's (`NavigationBarView.axaml:17-18`).
- The status bar and the message panel sit in `Auto` rows of the main window (`MainWindow.axaml:22`, `:82-99`),
  so nothing caps their buttons below Semi's 32 px `MinHeight`.

Existing gates and precedents:

- No test scans AXAML source: `git grep -lnE '\.axaml"|\*\.axaml|EnumerateFiles|XDocument' --
  SemiPlot/SemiPlot.Tests.Unit` prints nothing.
- The palette rule is gated by a `git grep` in `ui-theme.md:782-795` that no CI job runs. This slice's gate
  follows that precedent.
- The roadmap guard "The settings dialog tests assert equal bounds before and after a field turns invalid"
  (`:141-142`) is `SettingsViewTests.TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`
  (`SettingsViewTests.cs:244-265`). It compares bounds within one layout, so it stays green through an inset
  change, and stays green after it.

Tests that pin an inset this slice changes (found by `git grep -nE "Thickness\(|\.Padding|\.Margin|RowSpacing|
ColumnSpacing|\.Spacing"` and by the pixel literals of each touched window's tests):

- `CsvExportWindowTests.cs`:
  - `ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder` (`:645-662`): `>= 6` at `:656`.
  - `EachEdge_PutsTheDateOverTheTimeAndTheMarkerBox_AndTheTabOrderFollows` (`:770-819`): the second line "10 px
    under" at `:792`, "the edges stand 20 px apart" at `:801`.
  - `TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge` (`:822-844`): `edge` 370 at `:836`, `edge + 20`
    at `:837`.
  - `UnderRussian_TheWidestDateInInter_FitsTheStartPickersTextViewportOnTheStand` (`:1381-1420`): `FormWidth = 350`
    at `:1385`, `FormColumnSpacing = 14` at `:1386`.
  - `TheMessageRow_SitsAboveTheButtons_AndSpansTheContentWidth` (`:746-765`): `window.Width` 390 at `:759`, which
    this slice does not change.
- `AxisScalePanelLayoutTests.TheScaleTypeRow_FitsThePanelInInter` reads `bounds.ColumnSpacing` and
  `row.ColumnSpacing` live (`AxisScalePanelLayoutTests.cs:186-187`), so it adapts: in Inter `75.0 + 12 + 140 =
  227` px stays inside `280 - 24`, and `75.0 + 12 + 149.2 = 236.2` px inside 280 (Inter widths,
  `ui-theme.md:413-417`).
- `AxisScalePanelViewTests`' `PressThePanelsEmptyArea` clicks 5 px above `AxisScaleBounds`
  (`AxisScalePanelViewTests.cs:403-405`); with an 8 px row gap the click still lands in the gap.
- `PenFormPaneViewTests.FormColumnSpacing = 12` (`PenFormPaneViewTests.cs:33`): the pen form is already on the
  scale and this slice does not touch it.
- `PenEditorLayoutViewTests.TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` asserts the box and
  the top bar at the refresh button's height (`PenEditorLayoutViewTests.cs:118-119`); the button stays 32 px.
- `TrendLegendTopRowViewTests.TheTopRow_ShowsTextFromTheThresholdUpAndIconsBelowIt` asserts the top-row buttons
  24 or 20 px tall (`TrendLegendTopRowViewTests.cs:60`); those heights are `Height` setters
  (`TrendLegendView.axaml:59`, `:83`), not insets.
- `NavigationBarViewTests.TheTimeGroupAndTheToolGroup_AreSeparatedByAHairline` checks only the hairline's 1 px
  `Width` on an unrealised view (`NavigationBarViewTests.cs:48-58`, `CreateView` at `:60-65`).
- `TimeWindowPanelViewTests.cs:372` sets `Margin = new Thickness(20)` on a test anchor. It is harness geometry in
  a test, not UI AXAML, and stays.
- No test asserts the settings dialog's, the time window panel's, the marker label editor's, the legend rows',
  the status bar's, the navigation bar's or the message panel's insets, and none reads the `Bounds` of
  `AboutClose`, `StartupFailure*`, `ConnectionIndicator`, `ClearMessages` or `PenEditorRefreshButton` beyond
  `:118-119` above.
- `AboutInfoTests.AboutDialog_ShowsTheNameTheVersionAndTheConfigurationDirectory` realises the about dialog
  (`AboutInfoTests.cs:21-39`: `new AboutDialog { DataContext = about }`, `Show()`, `RunJobs()`).
- `AppStatusBarViewTests` realises its view as `new Window { Content = view }`, `Show()` and
  `Dispatcher.UIThread.RunJobs()` (`AppStatusBarViewTests.cs:43-52`).

Test traits of the classes this slice adds to: `Component=UI` and `Category=Unit` in every one. `Area=Chart` in
`SettingsViewTests` (`:30-32`), `CsvExportWindowTests`, `PenEditorLayoutViewTests`, `ChartMarkerViewTests`
(`:35-37`), `AxisScalePanelLayoutTests` (`:19-21`), `TimeWindowPanelViewTests` (`:30-32`), `TrendLegendViewTests`
(`:37-39`), `TrendLegendTopRowViewTests`, `AppStatusBarViewTests` (`:28-30`), `NavigationBarViewTests` (`:15-17`)
and `AboutInfoTests` (`:16-18`). `Area=Messages` in `MessagePanelViewTests` (`:27-29`) and
`StartupFailureWindowTests` (`:29-31`).

Derived widths and heights (each checked against this slice's changes):

| Constant or record | Where | Depends on a changed inset | After |
| --- | --- | --- | --- |
| Settings dialog `Width="640"` | `SettingsDialog.axaml:12`, `ui-theme.md:254-279` | margin 20 sets the notice cell | stays 640; every cell gains 8 px |
| Export window `Width="390"` | `CsvExportWindow.axaml:13`, `ui-theme.md:287-366` | margin 20, column gap 14 | stays 390; form 358 px, right edge 374 px |
| Export floor, 371 px | `ui-theme.md:347` | the 40 px of margins | 331 + 32 = 363 px |
| Export pen list `MaxHeight="226"` | `CsvExportWindow.axaml:150` | eight 28 px rows and the border | unchanged |
| Export height estimate, 609 px | `ui-theme.md:331`, `:342-344` | margins, row gaps, two top margins | 585 px estimate (Technical Details) |
| Axis scale panel `Width="280"` | `AxisScalePanel.axaml:11` | column gap 14 | unchanged; the fit test reads the gap |
| Time window panel 360 px | `ui-theme.md:431` | column gap 14 | unchanged; the date column gains 4 px |
| Marker label editor `Width="360"` | `TrendChartView.axaml:31` | row gap 10 | unchanged |
| `PenEditorWindow.MinimumWidth` 1272, `MinimumHeight` 536, `FilterBoxWidth` 280 | `ui-theme.md:462-497` | the refresh button's height (top bar 32) | unchanged: the button stays 32 px and keeps its 12 px sides |
| Pen form label column 112 px, measured in Inter | `PenFormPaneViewTests.cs:31` | none | unchanged |
| Sidebar text threshold 263 px | `TrendLegendView.axaml:80`, `overview.md:955-956` | `LegendPanel` `Padding="4"` (`MainWindow.axaml:68`) and the top-row buttons' 8 px sides | unchanged |
| Sidebar header right margin 16 px | `TrendLegendView.axaml:153`, `overview.md:938-940` | none: only its bottom 2 changes | unchanged |
| `TrendLegendViewModel.PanelMinWidth` 120, expanded 280, collapsed 168 | `TrendLegendViewModel.cs:22`, `charting.md:657-658` | none | unchanged; a row's name cell loses 4 px to the two 8 px gaps |
| Message panel `MaxHeight="140"` | `MessagePanelView.axaml:50` | none | unchanged; each entry gains 4 px |
| Status bar connection button | `AppStatusBar.axaml:23-30` | its 8 px sides | 8 px wider: Semi's 12 px sides; height stays 32 |
| Message panel `Clear` button | `MessagePanelView.axaml:36-40` | its 8 px sides | 8 px wider; height stays 32 |

## Development Approach

- **Testing approach**: TDD. Each window task writes its inset test first, runs it red at HEAD, then changes the
  AXAML. The export task edits the expected values of five existing tests first and runs them red.
- Complete each task fully before the next; every task ends with its tests passing.
- Tasks 1 to 5 each end in a commit, so the `$BASE...HEAD` gates of Task 6 see the work.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every existing assertion keeps its meaning; only the expected inset values named under Context change.

## Testing Strategy

- **View tests**: one inset test per touched window or view, asserting realised edges (`ViewProbe.LeftOf`,
  `RightOf`, `TopOf`, `BottomOf`), never the attribute text. Each is red at HEAD by the old value.
- **Button tests**: each window or view whose button loses its literal asserts the realised `Padding`
  `new Thickness(12, 0)` and a 32 px height.
- **The 4 grid**: the legend row and a message entry whose texts each take one line assert a height that is a
  multiple of 4 (`SKILL.md:108`). A failure is recorded, not tuned (Technical Details, ASSUMPTIONs).
- **Gate**: the `git grep` under Acceptance Evidence, run before and after.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- Every new test sits in an existing class and so carries its three traits (Context).
- Every click, cleared field, typed text and key press goes through `HeadlessInput` (`AGENTS.md`, Test).
- Headless edges hold for insets because an inset does not depend on the font. A width the operator reads is
  measured in Inter, as `UnderRussian_TheWidestDateInInter_...` already does. A height that holds text, such as
  a legend row, is the headless font's and is re-measured on the stand.

## Acceptance Evidence

The defect is automatable. At `12c7e37` the gate prints 45 lines:

```powershell
git grep -nP '(?:Margin|Padding|Spacing)(?:\x22\s+Value)?=\x22(?!(?:0|4|8|12|16|24|32)(?:[, ]+(?:0|4|8|12|16|24|32)){0,3}\x22)' -- 'SemiPlot/SemiPlot.UI/*.axaml'
```

After the change it prints nothing. It matches every attribute whose name ends in `Margin`, `Padding` or
`Spacing` (so `ColumnSpacing` and `RowSpacing` too) and every `Setter Property="...Margin|Padding|Spacing"
Value=...`, and passes only a value of one to four scale numbers. `\x22` is the double quote, so the command runs
unchanged in PowerShell and in bash (checked 2026-11-03, Git 2.56.0: 45 lines in both). It needs a Git built with
PCRE; the fallback without `-P` is the two-stage pipeline under Technical Details. Its exemptions are listed in
Technical Details, "What the gate does not scan".

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SettingsViewTests|FullyQualifiedName~CsvExportWindowTests|FullyQualifiedName~StartupFailureWindowTests|FullyQualifiedName~PenEditorLayoutViewTests|FullyQualifiedName~ChartMarkerViewTests|FullyQualifiedName~AboutInfoTests|FullyQualifiedName~AxisScalePanelLayoutTests|FullyQualifiedName~AxisScalePanelViewTests|FullyQualifiedName~TimeWindowPanelViewTests|FullyQualifiedName~TrendLegendViewTests|FullyQualifiedName~TrendLegendTopRowViewTests|FullyQualifiedName~AppStatusBarViewTests|FullyQualifiedName~NavigationBarViewTests|FullyQualifiedName~MessagePanelViewTests"
```

It must report 0 failed. These named tests must be present and pass:

- `SettingsViewTests.TheButtons_TakeTheThemesPaddingAndHeight` - Task 1
- `CsvExportWindowTests.TheButtons_TakeTheThemesPaddingAndHeight` - Task 1
- `StartupFailureWindowTests.TheButtons_TakeTheThemesPaddingAndHeight` - Task 1
- `PenEditorLayoutViewTests.TheRefreshButton_TakesTheThemesPaddingAndHeight` - Task 1
- `ChartMarkerViewTests.TheLabelEditorButtons_TakeTheThemesPaddingAndHeight` - Task 1
- `AboutInfoTests.TheCloseButton_TakesTheThemesPaddingAndHeight` - Task 1
- `PenEditorLayoutViewTests.TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` (unchanged) - Task 1
- `SettingsViewTests.TheDialog_TakesItsInsetsFromTheSpacingScale` - Task 2
- `SettingsViewTests.TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` (unchanged) - Task 2
- `CsvExportWindowTests.TheForm_TakesItsGapsFromTheSpacingScale` - Task 3
- `CsvExportWindowTests.ThePenCheckboxes_StandEightPixelsOffTheListBorder` (renamed) - Task 3
- `CsvExportWindowTests.EachEdge_PutsTheDateOverTheTimeAndTheMarkerBox_AndTheTabOrderFollows` (8 and 16 px) -
  Task 3
- `CsvExportWindowTests.TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge` (374 and 16 px) - Task 3
- `CsvExportWindowTests.UnderRussian_TheWidestDateInInter_FitsTheStartPickersTextViewportOnTheStand` (358 and 12)
  - Task 3
- `CsvExportWindowTests.TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` (unchanged) - Task 3
- `AxisScalePanelLayoutTests.TheForm_TakesItsGapsFromTheSpacingScale` - Task 4
- `AxisScalePanelLayoutTests.TheScaleTypeRow_FitsThePanelInInter` (unchanged) - Task 4
- `AxisScalePanelViewTests.APressOnThePanelsEmptyArea_WritesThePendingPairWithThePanelOpen` (unchanged) - Task 4
- `TimeWindowPanelViewTests.TheGrid_TakesItsGapsFromTheSpacingScale` - Task 4
- `ChartMarkerViewTests.TheLabelEditor_StandsItsButtonsEightPixelsUnderTheBox` - Task 4
- `TrendLegendViewTests.ARow_TakesItsInsetsFromTheSpacingScale` - Task 5
- `TrendLegendTopRowViewTests.TheTopRow_ShowsTextFromTheThresholdUpAndIconsBelowIt` (asserts the padding) - Task 5
- `AppStatusBarViewTests.TheSeparatorAndTheIndicator_TakeTheirInsetsFromTheSpacingScale` - Task 5
- `NavigationBarViewTests.TheHairline_StandsFourPixelsInsideTheBar` - Task 5
- `MessagePanelViewTests.AnEntryRowAndTheClearButton_TakeTheirInsetsFromTheSpacingScale` - Task 5

`$BASE` is `12c7e37`, this branch's start (Context). If `git merge-base --is-ancestor 12c7e37 HEAD` exits 1 after
a rebase, `$BASE` is `$(git merge-base HEAD origin/master)`.

Gates, each run fresh in Task 6:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -nP '(?:Margin|Padding|Spacing)(?:\x22\s+Value)?=\x22(?!(?:0|4|8|12|16|24|32)(?:[, ]+(?:0|4|8|12|16|24|32)){0,3}\x22)' -- 'SemiPlot/SemiPlot.UI/*.axaml'   # prints nothing
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.UI ':!SemiPlot/SemiPlot.UI/*.axaml'   # prints nothing
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.DataSource.Postgres ConfigFiles SemiPlot/SemiPlot.UI/Styles/Palette.axaml   # prints nothing
```

The AXAML scope gate, in bash, prints nothing:

```bash
git diff $BASE...HEAD -U0 -- 'SemiPlot/SemiPlot.UI/*.axaml' \
  | grep -E '^[+-]' | grep -vE '^(\+\+\+|---) ' \
  | grep -vE 'Margin=|Padding=|Spacing=|Property="(Margin|Padding)"' \
  | sed -E 's/^[+-] *//; s| />$||' | sort | uniq -u
```

Every changed AXAML line is an inset line or a line whose only change is a moved ` />`. 14 of the 16 deleted
padding lines close their tag; all but `SettingsDialog.axaml:184` and `PenEditorWindow.axaml:104`. Each `/>`
moves onto the attribute line before it, so that line shows once as `-` and once as `+` with ` />` appended.
The `sed` stage strips the sign, the indent and a trailing ` />`, so each such pair becomes two equal lines and
`uniq -u` drops them. Any line left is a change outside the slice's scope. The 14 lines that take a `/>`:

| File | Attribute line that takes the `/>` |
| --- | --- |
| `Chart/TrendChartView.axaml` | `Click="OnMarkerLabelOkClick"`, `Click="OnMarkerLabelCancelClick"` |
| `Export/CsvExportWindow.axaml` | `IsEnabled="{Binding ExportCommand.CanExecute^}"`, `Command="{Binding CancelCommand}"`, `Click="OnCloseClick"` |
| `MainWindow/AboutDialog.axaml` | `Click="OnCloseClick"` |
| `MainWindow/AppStatusBar.axaml` | `Command="{Binding ToggleMessagePanelCommand}"` |
| `Messages/MessagePanelView.axaml` | `Command="{Binding ClearCommand}"` |
| `Settings/SettingsDialog.axaml` | `Command="{Binding SaveCommand}"`, `Click="OnCloseClick"` |
| `Startup/StartupFailureWindow.axaml` | `IsVisible="{Binding OffersSettings}"`, `IsVisible="{Binding OffersRestart}"`, `Command="{Binding ShowAboutCommand}"`, `Command="{Binding ExitCommand}"` |

The `12,2` gate runs in Task 7, after the docs change:

```powershell
git grep -n 'Padding="12,2"' -- SemiPlot docs/architecture   # prints nothing
```

The look in the real font and theme cannot be seen headlessly; the manual checks under Post-Completion cover it.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **Each value takes the scale value of its level.** The inventory under Technical Details names the level and
  the new value of each of the 45.
- **16 buttons lose their padding literal.** Semi's `ButtonDefaultPadding` is `12,0`, on the scale, and Semi's
  `MinHeight` of 32 px sets the height. A `12,2` or `8,2` button and a `12,0` button are all 32 px tall: the text
  line plus 4 px stays under 32, so `MinHeight` decides. The 16 buttons then match the buttons that never had an
  override (`AxisScaleInitialScaleButton`, the navigation bar's), so equal things get equal insets
  (`SKILL.md:109-110`). The status bar's and the message panel's buttons are ordinary 32 px Semi buttons, as the
  navigation bar's are, and widen by 8 px.
- **The sidebar's top-row pair keeps its setter at `8,0`.** Its buttons are 24 px tall by a `Height` setter in a
  28 px row and switch to 24 x 20 icons below 263 px (`TrendLegendView.axaml:55-62`, `:79-85`, `:102`,
  `overview.md:955-958`). That row is its own case; the 8 px sides fit the threshold the docs record. The
  fixed-size icon buttons (the top row's icon form, the fold toggle, `GroupActionsButton`) keep `Padding="0"`.
- **The gate is a `git grep`, documented in `ui-theme.md`.** It mirrors the palette gate (`ui-theme.md:782-795`).
- **Nothing else moves.** No control, binding, command, code-behind, view model, palette key or resource changes.

Design decisions, each with its reason:

- **No shared button style.** The target value is the theme's own default, so a `Button` style in
  `Styles/Buttons.axaml` would restate Semi's value and would also reach every other `Button` in the app,
  including the sidebar's top row, which then needs its setter to undo it. Deleting 16 literals is the smaller
  change and leaves one source of the value, the theme. `12,4` was the alternative: it reads off "inside a
  control 4" (`SKILL.md:101`), but at a 32 px `MinHeight` it renders the same as `12,0` and adds 16 literals of no
  effect.
- **The settings dialog's form-to-buttons gap is 12.** `SettingsDialog.axaml:27` is the gap between the form grid
  and the message-and-buttons row. 12 is on the scale and equal to the export window's root `RowSpacing="12"`
  (`CsvExportWindow.axaml:45`), so the two dialogs separate the same two parts by the same gap.
- **Section headers take 8 above, plus the 8 px row gap.** The settings dialog's Connection header
  (`SettingsDialog.axaml:68`) and the export window's pens header (`CsvExportWindow.axaml:135`) move from
  `0,10,0,0` to `0,8,0,0`. With `RowSpacing` 8 the header stands 16 below the row above and 8 above its first row,
  the skill's section header (`SKILL.md:97`). The first header of each form has no top margin.
- **The export window's two edges stand 16 apart.** The end label and date move from `0,10,0,0` to `0,8,0,0`
  (`CsvExportWindow.axaml:94`, `:101`), so 8 of row gap plus 8 of margin separate the edges, a group gap, while
  the two lines of one edge stand 8 apart.
- **The export pen row is a plain list cell: `8,0`.** `Margin="6,0"` (`CsvExportWindow.axaml:165`) becomes
  `8,0` (`SKILL.md:98`). The list keeps its border without padding; adding a 4 px border padding is a layout
  change beyond the slice's value-only scope.
- **The legend rows take 4 px top and bottom.** `RowContent`'s `4,2` and the headed `24,2,4,2`
  (`TrendLegendView.axaml:17`, `:20`) become `4,4` and `24,4,4,4`, the "inside a control" level
  (`SKILL.md:101`). Each row grows by 4 px. The header row's `0,4,16,2` (`:153`) becomes `0,4,16,4`. The dot and
  the value gaps, 6 (`:227`, `:240`), become 8, the gap between controls in one row; the checkbox-to-dot gap and
  the value-to-unit gap stay 4 (`:220`, `:248`). No test or doc pins a legend row height:
  `git grep -nE "RowRoot|RowContent" -- docs/architecture` prints nothing, and the tests that name `RowRoot`
  count and colour rows (`TrendLegendViewTests.cs:214`, `:725`).
- **The legend row keeps 4 px sides while the export cell moves to 8.** The legend row sits inside `LegendPanel`'s
  own 4 px padding (`MainWindow.axaml:68`), so its content stands 4 + 4 from the panel border, the skill's list
  arrangement (`SKILL.md:106-107`); the export list has no border padding, so its cell carries the full 8.
- **The message entry dot moves 1 px.** `Margin="0,5,8,0"` on the 8 px `Ellipse` (`MessagePanelView.axaml:67`)
  aligns the dot to the first line of a wrapping title, which alignment cannot do. It becomes `0,4,8,0`. The
  hand-centred margin itself stays a deviation from `SKILL.md:127-128`; Task 7 records it in `backlog.md`. The
  entry row's `0,2` (`:62`) becomes `0,4`, so two entries stand 8 apart, the row spacing.
- **The two hairline separators take 4 px ends.** `Margin="8,3"` (`AppStatusBar.axaml:34`) and `Margin="4,3"`
  (`NavigationBarView.axaml:26`) become `8,4` and `4,4`; each line is 2 px shorter.
- **The exemption rule is by property, and no file or line is exempt.** Every off-scale value found is an inset
  in a view or a `DataTemplate`; none sits inside a `ControlTemplate` or a `ControlTheme`
  (`TimeWindowPanel.axaml:119-126` is the only `ControlTheme` and holds no inset). So the gate excludes no path.
- **The gate does not run in CI**, as the palette gate does not (`ui-theme.md:792`). The new inset tests catch a
  regression in the windows they cover; the gate catches it anywhere.

## Technical Details

### Inventory

Command: the gate under Acceptance Evidence, at `12c7e37`. Levels read off `SKILL.md:93-104`. "Removed" means
the attribute line is deleted and Semi's `12,0` applies.

**`Settings/SettingsDialog.axaml`** (8)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:25` | root `Grid` `Margin` | `20` | window margin | `16` |
| `:27` | root `Grid` `RowSpacing` | `20` | on the scale, equal to the export root `RowSpacing` | `12` |
| `:32` | form `Grid` `ColumnSpacing` | `14` | label to field | `12` |
| `:33` | form `Grid` `RowSpacing` | `10` | row spacing | `8` |
| `:68` | Connection header `Margin` | `0,10,0,0` | section header, 16 above with the row gap | `0,8,0,0` |
| `:184` | `SettingsRestartNow` `Padding` | `12,2` | button | removed |
| `:197` | `SettingsSaveButton` `Padding` | `12,2` | button | removed |
| `:202` | `SettingsClose` `Padding` | `12,2` | button | removed |

**`Export/CsvExportWindow.axaml`** (10)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:43` | root `Grid` `Margin` | `20` | window margin | `16` |
| `:51` | form `Grid` `ColumnSpacing` | `14` | label to field | `12` |
| `:52` | form `Grid` `RowSpacing` | `10` | row spacing | `8` |
| `:94` | end label `Margin` | `0,10,0,0` | group gap between the edges, 16 with the row gap | `0,8,0,0` |
| `:101` | `ExportEndDate` `Margin` | `0,10,0,0` | same row as `:94` | `0,8,0,0` |
| `:135` | `ExportPensHeader` `Margin` | `0,10,0,0` | section header, 16 above with the row gap | `0,8,0,0` |
| `:165` | pen row `CheckBox` `Margin` | `6,0` | plain list cell inset | `8,0` |
| `:201` | `ExportSaveAs` `Padding` | `12,2` | button | removed |
| `:206` | `ExportCancel` `Padding` | `12,2` | button | removed |
| `:211` | `ExportClose` `Padding` | `12,2` | button | removed |

**`Chart/AxisScalePanel.axaml`** (4)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:22` | outer `Grid` `RowSpacing` | `10` | row spacing | `8` |
| `:46` | `AxisScaleBounds` `ColumnSpacing` | `14` | label to field | `12` |
| `:47` | `AxisScaleBounds` `RowSpacing` | `10` | row spacing | `8` |
| `:94` | `AxisScaleType` `ColumnSpacing` | `14` | label to field, equal to `:46` | `12` |

**`Chart/TimeWindowPanel.axaml`** (2)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:30` | `Grid` `ColumnSpacing` | `14` | label to field, field to field in one row | `12` |
| `:31` | `Grid` `RowSpacing` | `10` | row spacing | `8` |

**`Chart/TrendChartView.axaml`** (3)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:33` | `MarkerLabelFlyout` `Grid` `RowSpacing` | `10` | row spacing | `8` |
| `:46` | `MarkerLabelOk` `Padding` | `12,2` | button | removed |
| `:50` | `MarkerLabelCancel` `Padding` | `12,2` | button | removed |

**`Legend/TrendLegendView.axaml`** (6)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:17` | `Grid#RowContent` `Margin` setter | `4,2` | inside a control | `4,4` |
| `:20` | headed `Grid#RowContent` `Margin` setter | `24,2,4,2` | 24 indent kept, inside a control | `24,4,4,4` |
| `:84` | top-row `Button` `Padding` setter | `8,2` | the fixed 24 px top-row buttons; 8 px sides kept | `8,0` |
| `:153` | `GroupHeaderRow` `Margin` | `0,4,16,2` | inside a control; 16 right margin kept | `0,4,16,4` |
| `:227` | `RowColor` `Margin` | `0,0,6,0` | gap between controls in one row | `0,0,8,0` |
| `:240` | `RowValue` `Margin` | `6,0,0,0` | gap between controls in one row | `8,0,0,0` |

**`MainWindow/AppStatusBar.axaml`** (2)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:30` | `ConnectionIndicator` `Padding` | `8,2` | button | removed |
| `:34` | separator `Border` `Margin` | `8,3` | gap between controls 8; 4 inside the bar | `8,4` |

**`Messages/MessagePanelView.axaml`** (3)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:40` | `ClearMessages` `Padding` | `8,2` | button | removed |
| `:62` | entry `Grid` `Margin` | `0,2` | half the 8 px row spacing on each side | `0,4` |
| `:67` | severity `Ellipse` `Margin` | `0,5,8,0` | dot to first text line, inside a control; 8 gap kept | `0,4,8,0` |

**`Navigation/NavigationBarView.axaml`** (1)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:26` | `GroupSeparator` `Margin` | `4,3` | inside a control | `4,4` |

**`PenEditor/PenEditorWindow.axaml`** (1)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:104` | `PenEditorRefreshButton` `Padding` | `12,2` | button | removed |

**`MainWindow/AboutDialog.axaml`** (1)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:59` | `AboutClose` `Padding` | `12,2` | button | removed |

**`Startup/StartupFailureWindow.axaml`** (4)

| Line | Attribute | Now | Level | New |
| --- | --- | --- | --- | --- |
| `:50` | `StartupFailureSettings` `Padding` | `12,2` | button | removed |
| `:55` | `StartupFailureRestart` `Padding` | `12,2` | button | removed |
| `:59` | `StartupFailureAbout` `Padding` | `12,2` | button | removed |
| `:63` | `StartupFailureExit` `Padding` | `12,2` | button | removed |

Totals: 45 values; 16 removed, 29 rewritten. The other eight AXAML files (`App.axaml`, `MainWindow.axaml`,
`AppMenuBar.axaml`, `MinimapView.axaml`, `Styles/Buttons.axaml`, `Styles/Forms.axaml`, `Styles/Icons.axaml`,
`Styles/Palette.axaml`) carry no off-scale inset.

When a removed line carries the element's closing `/>`, the `/>` moves onto the attribute line before it.

### What the gate does not scan

The rule is by property: the gate reads the five inset properties and nothing else. These stay outside it, each
with its reason:

- Sizes and geometry that are not insets: `Width`, `Height`, `MinWidth`, `MinHeight`, `MaxHeight`, `LineHeight`,
  `BorderThickness`, `CornerRadius`, `FontSize`, `StrokeThickness` and `ColumnDefinitions` widths. Examples that
  stay: the 1 px hairlines (`TrendLegendView.axaml:134`, `:146`), the 3 px active bars (`:158`, `:207`), the
  12 px colour dot (`:224-226`), the 20 x 16 fold toggle (`:186-187`), the 28 px top row (`:102`), the 28 px pen
  rows of the export list (`CsvExportWindow.axaml:164`), the `110,*` time column (`:72`), `MaxHeight="226"`
  (`:150`).
- The reserved message line: `TextBlock.form-message` is `LineHeight` 20 and `Height` 40 (`Styles/Forms.axaml:34-35`),
  two 20 px lines, heights by `SKILL.md:104`.
- Insets inside a `ControlTemplate` or a `ControlTheme`: none exists today with an inset. One added later that must
  be off the scale names its file in the gate as a `':!path'` exclusion, with its reason in `ui-theme.md`.
- C# and the plot: `ChartMarkerReadings.TextPadding = 2f` (`Chart/ChartMarkerReadings.cs:14`) pads a ScottPlot
  label in plot pixels, not an Avalonia layout inset.
- Tests: `TimeWindowPanelViewTests.cs:372` places a test anchor.
- A non-numeric value such as `{StaticResource ...}` does fail the gate; none exists today, and one added later is
  reviewed by hand.

The fallback without PCRE, two stages, prints the same 45 lines at `12c7e37` (checked in bash):

```bash
git grep -noE '[A-Za-z]*(Margin|Padding|Spacing)="[^"]*"|Property="[A-Za-z]*(Margin|Padding|Spacing)" Value="[^"]*"' -- 'SemiPlot/SemiPlot.UI/*.axaml' \
  | grep -vE ':[0-9]+:(([A-Za-z]*(Margin|Padding|Spacing)=)|(Property="[A-Za-z]*" Value=))"(0|4|8|12|16|24|32)([, ]+(0|4|8|12|16|24|32)){0,3}"$'
```

### Derived numbers

Each is arithmetic on the recorded measurement and an inset change; the text widths do not change.

- Settings dialog (`ui-theme.md:254-279`), 640 px wide: the margin 20 becomes 16 on both sides, so every cell of
  the bottom row gains 8 px. Notice cell beside the button: 334 becomes 342 px in English, 241 becomes 249 px in
  Russian; message cell with the notice hidden: 457 becomes 465 px, 388 becomes 396 px. The natural width
  measured with `SizeToContent="WidthAndHeight"`, 245 and 278 px, loses the 8 px of margin and 2 px of column gap:
  235 and 268 px. ASSUMPTION: the natural width is the form grid's, not the bottom row's; the Post-Completion
  re-measure confirms it.
- Export window (`ui-theme.md:287-366`), 390 px wide: the form is 390 - 2 x 16 = 358 px; the right edge 16 + 358
  = 374 px. Date width: 358 - 12 - label column, 314 px in English (label 32) and 294 px in Russian (label 52).
  Marker box: date - 110 - 8, 196 and 176 px. Russian date text viewport: 228 + 10 = 238 px. Floor: 331 + 32 =
  363 px. List viewport: 348 + 8 = 356 px; the 120-character name trims at about 314 + 8 - 4 = 318 px, with the
  4 px the wider checkbox margins take (ASSUMPTION until re-measured). Height estimate: 609 - 8 (margins) - 12
  (six row gaps of 2) - 2 (end edge margin) - 2 (pens header margin) = 585 px.
- Axis scale panel: the field column starts at label column + 12, 81.6 px in English and 87.0 px in Russian in
  Inter (`ui-theme.md:415-416`).
- Time window panel, 360 px: the date column gains 2 x 2 = 4 px.
- Marker label editor, 360 px: the button row stands 8 px under the box.

### New tests

All in existing classes; each realises its view as its neighbours do and reads edges with `ViewProbe`.

- `SettingsViewTests.TheButtons_TakeTheThemesPaddingAndHeight` (`[AvaloniaFact]`): `SettingsSaveButton`,
  `SettingsClose` and `SettingsRestartNow` have `Padding` `new Thickness(12, 0)`; the two visible ones have
  `Bounds.Height` 32.
- `CsvExportWindowTests.TheButtons_TakeTheThemesPaddingAndHeight`: `ExportSaveAs`, `ExportCancel`, `ExportClose`,
  the same two assertions.
- `StartupFailureWindowTests.TheButtons_TakeTheThemesPaddingAndHeight`: the four `StartupFailure*` buttons; the
  padding on all four, the height on the visible ones, built as `TheSettingsButton_IsShownOverAnEmptyPassword`
  (`StartupFailureWindowTests.cs:126`) builds its window.
- `PenEditorLayoutViewTests.TheRefreshButton_TakesTheThemesPaddingAndHeight`: `PenEditorRefreshButton`.
- `ChartMarkerViewTests.TheLabelEditorButtons_TakeTheThemesPaddingAndHeight`: after `OpenLabelEditorToRename`
  (as `ChartMarkerViewTests.cs:665-669` opens it), `LabelEditorButton("MarkerLabelOk")` and
  `LabelEditorButton("MarkerLabelCancel")`.
- `AboutInfoTests.TheCloseButton_TakesTheThemesPaddingAndHeight`: the dialog realised as
  `AboutDialog_ShowsTheNameTheVersionAndTheConfigurationDirectory` realises it (`AboutInfoTests.cs:24-29`);
  `AboutClose` has `Padding` `new Thickness(12, 0)` and `Bounds.Height` 32.
- `SettingsViewTests.TheDialog_TakesItsInsetsFromTheSpacingScale`: the Interface header's `LeftOf` and `TopOf`
  in the dialog are 16; `dialog.Bounds.Width - RightOf(SettingsHost)` is 16;
  `dialog.Bounds.Height - BottomOf(SettingsValidationMessage)` is 16; `LeftOf(SettingsHost)` less the label's
  `LeftOf` and the form grid's `ColumnDefinitions[0].ActualWidth` is 12; `TopOf(SettingsTheme) -
  BottomOf(SettingsLanguage)` is 8; the Connection header's `TopOf` less `BottomOf(SettingsTheme)` is 16, and
  `TopOf(SettingsHost)` less the header's `BottomOf` is 8; `TopOf(SettingsSaveButton) -
  BottomOf(SettingsPollInterval)` is 12. The headers have no `Name`; the test finds each by its resource text.
  Red at HEAD: 20 at the first assertion.
- `CsvExportWindowTests.TheForm_TakesItsGapsFromTheSpacingScale` (`en`): `LeftOf(ExportStartDate)` less the start
  label's `LeftOf` and the form grid's column 0 width is 12; `TopOf(ExportPensHeader) - BottomOf(ExportEndMarker)`
  is 16; `TopOf(ExportPens) - BottomOf(ExportPensHeader)` is 8; `LeftOf(ExportPens)` in the window is 16 (the list
  spans both columns, `CsvExportWindow.axaml:145-146`).
- `AxisScalePanelLayoutTests.TheForm_TakesItsGapsFromTheSpacingScale`: on the opened panel,
  `LeftOf(AxisScaleMaximum, bounds) - bounds.ColumnDefinitions[0].ActualWidth` is 12, and the same holds for the
  box in `AxisScaleType`; `TopOf(AxisScaleMinimum) - BottomOf(AxisScaleMaximum)` is 8; `TopOf(AxisScaleType) -
  BottomOf(AxisScaleBounds)` and `TopOf(AxisScaleBounds)` less the header grid's `BottomOf` are 8.
- `TimeWindowPanelViewTests.TheGrid_TakesItsGapsFromTheSpacingScale`: `LeftOf(TimeWindowStartDate) -
  RightOf(TimeWindowStartTime)` is 12; `TopOf(TimeWindowEndTime) - BottomOf(TimeWindowStartTime)` is 8.
- `ChartMarkerViewTests.TheLabelEditor_StandsItsButtonsEightPixelsUnderTheBox`: `TopOf(MarkerLabelOk) -
  BottomOf(LabelBox)` is 8.
- `TrendLegendViewTests.ARow_TakesItsInsetsFromTheSpacingScale`: an expanded legend with one headed group and one
  pen. In the row: `TopOf(RowContent, RowRoot)` is 4 and `RowRoot.Bounds.Height - BottomOf(RowContent, RowRoot)` is
  4; `LeftOf(RowContent, RowRoot)` is 24; `LeftOf(RowName) - RightOf(RowColor)` is 8; `LeftOf(RowValue) -
  RightOf(RowName)` is 8. Under the header: `TopOf(RowRoot) - BottomOf(GroupHeaderRow)` is 4. Last assertion:
  `RowRoot.Bounds.Height % 4` is 0. The test writes the row height to the test output.
- `TrendLegendTopRowViewTests.TheTopRow_ShowsTextFromTheThresholdUpAndIconsBelowIt` gains one assertion per
  button: `Padding` is `new Thickness(8, 0)` when it shows text and `new Thickness(0)` when it shows the icon.
- `AppStatusBarViewTests.TheSeparatorAndTheIndicator_TakeTheirInsetsFromTheSpacingScale`: the view realised as
  `AppStatusBarViewTests.cs:43-52` realises it. The 1 px `Border` stands 4 px under the grid's top, 4 px over its
  bottom and 8 px from the indicator; `ConnectionIndicator.Padding` is `new Thickness(12, 0)` and its
  `Bounds.Height` 32.
- `NavigationBarViewTests.TheHairline_StandsFourPixelsInsideTheBar`: the view from `CreateView` shown in
  `new Window { Content = view }`, `Show()` and `Dispatcher.UIThread.RunJobs()` before any edge is read, as
  `AppStatusBarViewTests.cs:43-52` does; `GroupSeparator` stands 4 px under the panel's top and 4 px over its
  bottom. Red at HEAD: 3.
- `MessagePanelViewTests.AnEntryRowAndTheClearButton_TakeTheirInsetsFromTheSpacingScale`: two reported entries
  whose title, detail and remedy each take one line, in the window `TheEntryRow_ShowsTheLocalTimeAndHides...`
  builds (`MessagePanelViewTests.cs:73-84`). The entry grid stands 4 px inside its container at top and bottom,
  the dot's `TopOf` in its grid is 4, the container's `Bounds.Height % 4` is 0, `ClearMessages.Padding` is
  `new Thickness(12, 0)` and its `Bounds.Height` 32.

### Test changes

| Test | Today | After |
| --- | --- | --- |
| `CsvExportWindowTests.ThePenCheckboxes_StandAtLeastSixPixelsOffTheListBorder` `:645-662` | `>= 6` | renamed `ThePenCheckboxes_StandEightPixelsOffTheListBorder`, `Be(8)` |
| `EachEdge_...` `:792` | second line 10 px under | 8 px under |
| `EachEdge_...` `:801` | edges 20 px apart | 16 px apart |
| `TheDatesTheMarkerBoxes..._EndAtOneRightEdge` `:836-837` | edge 370, margin 20 | edge 374, margin 16 |
| `UnderRussian_TheWidestDateInInter_...` `:1385-1386` | `FormWidth` 350, `FormColumnSpacing` 14 | 358, 12 |
| `TheMessageRow_SitsAboveTheButtons_AndSpansTheContentWidth` `:759` | width 390 | unchanged |
| `AxisScalePanelLayoutTests.TheScaleTypeRow_FitsThePanelInInter` `:186-187` | reads `ColumnSpacing` | unchanged |
| `AxisScalePanelViewTests` `PressThePanelsEmptyArea` `:403-405` | 5 px above the bounds | unchanged: inside the 8 px gap |
| `PenEditorLayoutViewTests` `:118-119` | box and bar at the button's height | unchanged: 32 px |
| `TrendLegendTopRowViewTests` `:60` | 24 or 20 px | unchanged; padding assertion added |

### ASSUMPTIONs, each with its guard and fallback

- ASSUMPTION: removing the 2 px top and bottom changes no button height, because Semi's 32 px `MinHeight` exceeds
  the text line plus 4 px. Guard: each button test's height assertion, at HEAD with the literal and after. A
  height other than 32 is recorded under its task with the value and the windows whose height changes.
- ASSUMPTION: a legend row is about 24 px at HEAD and 28 px after (Semi's 16 px box with its 2 px top margin, a
  text line, and 2 + 2 or 4 + 4), and a one-line message entry's height is a multiple of 4 after. Guard: the two
  `% 4` assertions. If either fails, the run marks it ⚠️ under Task 5, adds a `backlog.md` entry under
  "Tooling and layout" naming the view and the measured height, and leaves the insets as the inventory states; it
  does not retune them. Every row height the run records is marked "headless, re-measure". Measured in Task 5:
  the legend row is 20 px at HEAD and 24 px after (headless, re-measure), not the predicted 24 and 28.
- ASSUMPTION: `RowName` stretches across its `*` column (a `TextBlock` defaults to `HorizontalAlignment.Stretch`),
  so its right edge is the column's. Guard: the 8 px assertion against `RowValue`. Fallback: measure the column
  edge from `RowContent.ColumnDefinitions[2]` instead.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): the button literals and their tests, each window's insets and
  tests, the gates, the architecture docs, the backlog entry.
- **Post-Completion** (no checkboxes): the look in the running viewer and the Skia and HarfBuzz re-measure of the
  recorded tables and row heights.

## Implementation Steps

### Task 1: Dialog buttons take Semi's padding, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupFailureWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorLayoutViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AboutInfoTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AboutDialog.axaml`

- [x] run the gate under Acceptance Evidence at HEAD: 45 lines; note the count under this task (45 at `03bafa0`)
- [x] write the six button tests as Technical Details states
- [x] run them at HEAD: each padding assertion red with `12,2`, each height assertion green at 32 (note the
      heights) (all six red on `12,2`; with the padding assertion relaxed, every button measured 32 px)
- [x] delete the 14 `Padding="12,2"` lines the inventory marks "removed"
- [x] run the six tests and `PenEditorLayoutViewTests`: every case passes (20 passed; the six classes: 154 passed)
- [x] run the gate: the 14 button lines are gone, 31 remain
- [x] commit the task

### Task 2: The settings dialog's insets, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`

- [x] write `TheDialog_TakesItsInsetsFromTheSpacingScale` as Technical Details states (label to field reads
      `LeftOf(SettingsHost) - RightOf(label)`: Avalonia 12's `ColumnDefinition.ActualWidth` includes the column
      gap, 252 = 238 + 14 at HEAD, so the column-width form measured 0)
- [x] run it at HEAD: red at the first assertion (20); note the actual values (margins 20, 20, 20, 20; label to
      field 14; row gap 10; header 20 above and 10 below; form to buttons 20)
- [x] change `:25`, `:27`, `:32`, `:33`, `:68` as the inventory states
- [x] run `SettingsViewTests` and `SettingsViewModelTests`: every case passes,
      `TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` included (52 passed)
- [x] commit the task

### Task 3: The export window's insets, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`

- [x] write `TheForm_TakesItsGapsFromTheSpacingScale` as Technical Details states (label to field reads
      `LeftOf(ExportStartDate) - RightOf(start label)`, as in Task 2: `ColumnDefinitions[0].ActualWidth` includes
      the column gap)
- [x] change the five existing tests as the Test changes table states, the rename included
- [x] run the six at HEAD: each red at its first changed assertion; note the actual values (label to field 14;
      checkbox 6 px off the border; second line 10 px under; edge 370; Russian Inter date cell 287 against 295)
- [x] change `:43`, `:51`, `:52`, `:94`, `:101`, `:135`, `:165` as the inventory states
- [x] run `CsvExportWindowTests` and `CsvExportViewModelTests`: every case passes; note the Inter date viewport
      the fit test computes (95 passed; Russian Inter viewport 239 px, date cell 295 px, widest date 79.6 px;
      gate 26 -> 19 lines)
- [x] commit the task

### Task 4: The chart folder: axis scale panel, time window panel, marker label editor, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelLayoutTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TimeWindowPanelViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanel.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`

- [x] write `AxisScalePanelLayoutTests.TheForm_TakesItsGapsFromTheSpacingScale`,
      `TimeWindowPanelViewTests.TheGrid_TakesItsGapsFromTheSpacingScale` and
      `ChartMarkerViewTests.TheLabelEditor_StandsItsButtonsEightPixelsUnderTheBox` as Technical Details states
      (label to field reads `LeftOf(field) - RightOf(label)`, as in Tasks 2 and 3)
- [x] run them at HEAD: red (14 and 10); note the actual values (axis scale label to field 14; time window
      field to field 14; marker editor box to buttons 10)
- [x] change `AxisScalePanel.axaml:22`, `:46`, `:47`, `:94`, `TimeWindowPanel.axaml:30`, `:31` and
      `TrendChartView.axaml:33` as the inventory states
- [x] run `AxisScalePanelLayoutTests`, `AxisScalePanelViewTests`, `AxisScalePanelViewModelTests`,
      `TimeWindowPanelViewTests` and `ChartMarkerViewTests`: every case passes; note the Inter widths
      `TheScaleTypeRow_FitsThePanelInInter` writes (142 passed; en labels 63.7, 59.8, 69.6 px, label column
      69.6 px, box 101.9 px; ru labels 72.8, 66.1, 75.0 px, label column 75.0 px, box 149.2 px; gate 19 -> 12)
- [x] commit the task

### Task 5: The legend, the status bar, the navigation bar and the message panel, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendTopRowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppStatusBarViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/NavigationBarViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/MessagePanelViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Navigation/NavigationBarView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Messages/MessagePanelView.axaml`
- Modify, only if a `% 4` assertion fails: `docs/plans/backlog.md`

- [x] write `ARow_TakesItsInsetsFromTheSpacingScale`,
      `TheSeparatorAndTheIndicator_TakeTheirInsetsFromTheSpacingScale`, `TheHairline_StandsFourPixelsInsideTheBar`
      and `AnEntryRowAndTheClearButton_TakeTheirInsetsFromTheSpacingScale`, and add the padding assertion to
      `TheTopRow_ShowsTextFromTheThresholdUpAndIconsBelowIt`, as Technical Details states (the status bar's window
      takes `SizeToContent.Height`, the `Auto` row the bar sits in)
- [x] run them at HEAD: each red at its first inset or padding assertion (`TheHairline_...` at 3); note the actual
      values and the legend row height, marked "headless, re-measure" (status bar hairline 3; top row `8,2`;
      legend row content 2, row 20 px, headless, re-measure; message entry 2, entry 52 px, headless, re-measure)
- [x] delete `AppStatusBar.axaml:30` and `MessagePanelView.axaml:40`; change the other 10 values of the four
      files as the inventory states
- [x] run `TrendLegendViewTests`, `TrendLegendTopRowViewTests`, `AppStatusBarViewTests`,
      `NavigationBarViewTests`, `MessagePanelViewTests` and `MainWindowViewTests`: every case passes; note the
      legend row and message entry heights after, marked "headless, re-measure" (75 passed; legend row 24 px,
      message entry 56 px, both headless, re-measure)
- [x] if a `% 4` assertion fails: mark it ⚠️ here and add the `backlog.md` entry the ASSUMPTION states (both
      passed; no entry)
- [x] run the gate: it prints nothing (12 -> 0 lines)
- [x] commit the task

### Task 6: Verify acceptance criteria

- [x] run the gate: it prints nothing
- [x] run the filtered command under Acceptance Evidence: 0 failed, every named test present (note the pass count)
      (265 passed; all 25 named tests present and passed)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed (2763 passed)
- [x] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the NU1902 for `MessagePack` 2.5.302 in
      `SemiPlot.AppHost` that the base reports
- [x] set `$BASE` as Acceptance Evidence states (`12c7e37`, an ancestor of HEAD)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse` over every
      `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0 (13 test files)
- [x] run the HeadlessInput gate, the two `--stat` scope gates and the AXAML scope gate under Acceptance
      Evidence: each prints what it must (only `HeadlessInput.cs` lines; the other three print nothing)

### Task 7: [Final] Update documentation

- [x] `docs/architecture/ui-theme.md`: a new section `## An off-scale inset in AXAML is a defect` before
      `## A colour literal in AXAML is a defect` (`:782`). It states: every `Margin`, `Padding` and `Spacing` value
      in the UI's AXAML is on the 4/8/12/16/24/32 scale (`desktop-ui` Spacing); buttons take Semi's `12,0` padding
      and 32 px height, and the sidebar's 24 px top-row buttons keep their own `8,0` setter; the gate command and
      that it prints nothing; what it does not scan (Technical Details); that no CI job runs it, and the inset
      tests of Task 1 to Task 5 that catch a regression per window
- [x] `ui-theme.md:254`: "a 16 px margin and 8 px between rows". `:255-259`: the natural width row becomes the
      derived 235 and 268 px, marked as derived from the 2026-09-25 measurement. `:263-273`: the table's two cell
      rows and the hidden-notice row take the derived widths (342, 249; 465, 396), marked as measured at the 20 px
      margin and derived for 16; `:282-285`: the 2026-10-01 cell widths marked the same way
- [x] `ui-theme.md:287-298`: a 16 px margin, the form grid `Auto,*` at 12 px, the lines of an edge 8 px apart, the
      end label and date with an 8 px top margin so 16 px separate the edges, the dates and the rows ending 16 px
      from the window edge. `:306`: `Margin="8,0"`, the plain list cell inset, so a box stands 8 px off the list
      border. `:331-339`: the size estimate 390 x 585, the right edges 358 and 374 px, the edge grid 32, 314; 110,
      196 px in English and 52, 294; 110, 176 px in Russian, the viewport 356 px, each marked as derived from the
      2026-10-14 measurement. `:342-347`: the 585 px estimate and its sum, the floor 331 + 32 = 363 px. `:348-351`:
      the dates 314 and 294 px, the Russian date's 238 px. `:355`: "the 358 px form less the 12 px column gap".
      `:363-365`: "the 16 px right margin" and "the 8, 8 and 16 px gaps"
- [x] `ui-theme.md:380`: "Both grids use a 12 px `ColumnSpacing`". `:395-402`: mark the rows "before the Scale type
      row" as measured at a 14 px gap; add the field column start in Inter, 81.6 and 87.0 px
- [x] `ui-theme.md:431-438`: the grid's 12 px column gap and 8 px row gap. `:450`: "8 px between them"
- [x] `ui-theme.md:483`: `PenEditorRefreshButton` takes Semi's padding, in place of "(`Padding="12,2"`)"
- [x] `docs/architecture/charting.md:622-626`, the `Legend/TrendLegendView` entry that lists a row's parts: the
      row's 4 px top and bottom inset, the 8 px gaps after the dot and before the value, the 24 px indent of a
      headed row, and the row height Task 5 noted, marked "headless, re-measure"
- [x] `docs/plans/backlog.md`, under "Tooling and layout" (`:202`): the message entry's severity dot is centred
      on the title's first line by a hand-tuned `Margin="0,4,8,0"` (`MessagePanelView.axaml:67`), which
      `desktop-ui` Alignment (`SKILL.md:127-128`) rules out; it needs a layout that aligns the dot to the first
      text line
- [x] run the `12,2` gate under Acceptance Evidence: it prints nothing (0 lines)
- [x] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** in the running viewer, in both languages and both theme variants:

1. Settings dialog (`Edit` -> `Settings`): the form starts 16 px from the window edge; rows stand 8 px apart;
   the Connection header stands 16 px under the Theme row; the restart notice still fits two lines in Russian.
2. Export window: the two edges stand 16 px apart; the pen rows' boxes stand 8 px off the list border; the
   buttons are 32 px tall and nothing overflows the 390 px window in Russian.
3. Axis scale panel and time window panel: labels 12 px from their fields, rows 8 px apart, nothing clipped in
   Russian.
4. Marker label editor: OK and Cancel stand 8 px under the box.
5. Sidebar: rows read evenly at the new height; the dot, the name and the value stand 8 px apart; a headed row
   keeps its 24 px indent; the fold toggle still clears the scroll bar at 120 px.
6. Status bar and navigation bar: each hairline is centred in its bar with 4 px ends; the connection button has
   12 px sides and fits the bar.
7. Message panel: two entries stand 8 px apart; each dot sits on its title's first line; `Clear` has 12 px sides.
8. Every dialog button (settings, export, startup failure, about, marker label editor, pen editor refresh) is
   32 px tall with 12 px sides.
9. Re-measure with Skia and HarfBuzz on the Windows machine fonts, the methods of 2026-09-25, 2026-09-30,
   2026-10-14 and 2026-10-16 (`ui-theme.md:254-285`, `:326-339`, `:395-408`), and replace every row Task 7 marked
   as derived. Measure the legend row and a one-line message entry the same way and replace every height marked
   "headless, re-measure".

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: none. ui-spacing-scale is the
last slice; the roadmap closes when its PR merges (`20261102-visual-chore-roadmap.md:288-291`).

**Executed by exec:**
- branch: ui-spacing-scale

## Verify it yourself

1. No off-scale inset is left: the gate under Acceptance Evidence prints nothing. At `axis-scale-checkbox-column`
   it prints 45 lines.
2. Buttons: every button that lost its literal has 12 px sides and a 32 px height
   (`SettingsViewTests.TheButtons_TakeTheThemesPaddingAndHeight` and its siblings, the status bar's and the
   message panel's included).
3. Settings dialog: 16 px margin, 12 px label gap, 8 px rows, a 16 px section gap
   (`SettingsViewTests.TheDialog_TakesItsInsetsFromTheSpacingScale`).
4. Export window: the right edge at 374 px, the edges 16 px apart, the pen boxes 8 px off the border
   (`CsvExportWindowTests.TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge`,
   `EachEdge_PutsTheDateOverTheTimeAndTheMarkerBox_AndTheTabOrderFollows`,
   `ThePenCheckboxes_StandEightPixelsOffTheListBorder`).
5. Chart forms: 12 px label gaps and 8 px rows (`AxisScalePanelLayoutTests.TheForm_TakesItsGapsFromTheSpacingScale`,
   `TimeWindowPanelViewTests.TheGrid_TakesItsGapsFromTheSpacingScale`).
6. Sidebar row: 4 px top and bottom, 8 px gaps, a height on the 4 grid
   (`TrendLegendViewTests.ARow_TakesItsInsetsFromTheSpacingScale`, `AnUngroupedRow_TakesItsInsetsFromTheSpacingScale`).
7. Validation still never resizes a form (`SettingsViewTests.TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`,
   `CsvExportWindowTests.TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`).

Run all of it: the Acceptance Evidence command of this plan (266 passed, 0 failed on `31790f1`).
