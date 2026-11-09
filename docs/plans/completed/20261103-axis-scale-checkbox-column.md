# Axis scale panel: the log checkbox in the field column, under a Scale type label

## Overview

- The axis scale panel's form has two left edges. The bound fields start at the field column, after the labels
  Maximum and Minimum. The log-scale checkbox `AxisScaleLogarithmic` starts at the panel's left edge, under the
  labels (`AxisScalePanel.axaml:86-90`).
- The checkbox moves into the field column. The label column of its row reads "Scale type" / "Тип шкалы", and the
  box reads "Logarithmic" / "Логарифмическая", the pen form's pair for the same setting. The form then has one
  label edge and one field edge.
- The checkbox keeps its own grid outside `AxisScaleBounds`, so a press or a Tab onto it still ends the pair's
  edit. The two grids share one label column width.
- An invalid bound shows the warning glyph inside its box, as the pen form's fields do, because
  `ui-theme.md:223-225` binds the glyph to the next form touched.
- The key `AxisScaleLogarithmic` loses its one consumer and goes from both resource files.
- Scope guard: no change to the scale rules or the panel's commit behaviour. No view model or code-behind change.
  The glyph is a style on the existing `invalid` class: it changes no validation rule and no commit path, so the
  guard holds.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice axis-scale-checkbox-column

The slice has no issue (`20261102-visual-chore-roadmap.md:259`), so the PR body closes none. Its scope is `:256-258`,
its scope guard `:265`. The roadmap's table names the defect "log checkbox at the label edge" with the cost "two
left edges in one form" (`:37`), and the target state puts the checkbox "in the field column, with a label in the
label column" (`:89-90`). The pen pane's target row for the same setting is "Тип шкалы (checkbox
"Логарифмическая")" (`:68`).

Stacking: this branch, `axis-scale-checkbox-column`, starts at `9c2f74c`, the tip of `export-pen-choice-list`
(verified 2026-11-03: `git rev-parse HEAD export-pen-choice-list` prints the same hash). Its merge base with
`master` is `df129f7`; the branches between are not merged.

- Panel (`SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`):
  - The `UserControl` is `Width="280"` (`:11`), focusable and no tab stop (`:13-14`). It carries
    `UserControl.KeyBindings` (`:16-19`) and no `Resources` or `Styles` block.
  - The outer `Grid` has five `Auto` rows at `RowSpacing="10"` (`:21-22`): the header (`:24-39`), the bounds grid
    (`:41-84`), the checkbox (`:86-90`), the two buttons (`:92-111`) and the message line (`:113-116`).
  - The bounds grid `AxisScaleBounds` is `ColumnDefinitions="Auto,*"`, `ColumnSpacing="14"`, `RowSpacing="10"`
    (`:44-46`). Column 0 holds the labels `AxisScaleMaximumLabel` and `AxisScaleMinimumLabel`, unnamed
    `TextBlock`s with `VerticalAlignment="Center"` (`:48-51`, `:66-69`). Column 1 holds the `TextBox`es
    `AxisScaleMaximum` and `AxisScaleMinimum`, `Width="140"`, `HorizontalAlignment="Left"`, with
    `Classes.invalid="{Binding !IsMaximumValid}"` and `{Binding !IsMinimumValid}` (`:53-64`, `:71-82`).
  - The checkbox is `Grid.Row="2"` of the outer grid, `Name="AxisScaleLogarithmic"`,
    `Content="{x:Static text:Resources.AxisScaleLogarithmic}"`, `IsChecked="{Binding IsLogarithmic, Mode=OneWay}"`,
    `Command="{Binding ToggleLogarithmicCommand}"` (`:86-90`). It sets no column and no `HorizontalAlignment`, so it
    starts at the panel edge and stretches across the row; a click anywhere on the row toggles it.
- Code-behind `AxisScalePanel.axaml.cs`: `OnLoaded` subscribes to `AxisScaleBounds`'s `IsKeyboardFocusWithinProperty`
  (`:21-22`), and `OnBoundsFocusWithinChanged` calls `CommitBounds` when it turns false (`:45-60`). This is the
  pair's commit trigger. A control inside `AxisScaleBounds` keeps focus-within true, so a Tab onto it would not
  commit. `trend-interaction.md:385-386` states the rule: the checkbox "sits in its own row under the bounds,
  outside `AxisScaleBounds`, so a press on it ends the pair's edit".
- Pen form precedents (`SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`):
  - `ColumnDefinitions x:Key="PenFormColumns" x:Shared="False"` with a 112 px label column and a 240 px field
    column (`:24-28`), read by each form grid as `ColumnDefinitions="{StaticResource PenFormColumns}"` (`:419-421`,
    `:441`).
  - The glyph style `Panel#PenForm TextBox.invalid` sets `InnerRightContent` through a `<Template>` to a 16 x 16
    `PathIcon`, `Margin="0,0,8,0"`, `Data="{StaticResource WarningGeometry}"`,
    `Foreground="{DynamicResource AppSeverityErrorBrush}"` (`:79-89`).
  - The scale type row: a `PenFormScaleTypeLabel` label in column 0 and a `PenFormLogarithmic` checkbox in column 1
    with `HorizontalAlignment="Left"` (`:472-483`).
- `ui-theme.md:219-225`: an invalid field shows the `invalid` border and the warning glyph; "The pen editor's form
  carries the glyph through its window-scoped style `Panel#PenForm TextBox.invalid`; another form adopts it when it
  is next touched, and until then marks an invalid field by the border alone."
- Resources:
  - `AxisScaleLogarithmic`: "Logarithmic scale" / "Логарифмическая шкала" (`Resources.resx:168-170`,
    `Resources.ru.resx:168-170`). Its one consumer is `AxisScalePanel.axaml:88`; its one test read is
    `AxisScalePanelViewTests.cs:329` (`git grep -n AxisScaleLogarithmic -- SemiPlot`).
  - `PenFormScaleTypeLabel`: "Scale type" / "Тип шкалы" (`Resources.resx:684-686`, `Resources.ru.resx:684-686`).
  - `PenFormLogarithmic`: "Logarithmic" / "Логарифмическая" (`Resources.resx:687-689`, `Resources.ru.resx:687-689`).
  - `ui-text.md:236-238`: the pen form's labels "reuse a key wherever the term is the same concept", and the pen form
    already reads the axis scale panel's `AxisScaleMinimumLabel` and `AxisScaleMaximumLabel`.
  - The resource class is generated from `Resources.resx` (`SemiPlot.UI.csproj:12`, `GenerateSource="true"`), so a
    deleted key deletes its accessor.
- Measured widths (`ui-theme.md:375-388`, Skia and HarfBuzz, 2026-10-16): the field column starts at 82.0 px in
  English and 88.0 px in Russian; the checkbox is 141 px wide in English and 199 px in Russian. The Russian field
  column is 280 - 88 = 192 px, narrower than the 199 px box that reads "Логарифмическая шкала".
- Headless measurement: the headless shaper advances every glyph by the font size (`ui-theme.md:347-348`), so a
  headless label is about twice as wide as in Inter. A headless `Auto` column therefore does not stand for the
  column on screen.
- Avalonia 12.0.5 carries `SharedSizeGroup` and `IsSharedSizeScope`: both strings occur in
  `~/.nuget/packages/avalonia/12.0.5/lib/net10.0/Avalonia.Controls.dll` (checked 2026-11-03 with `grep -c -a`). No
  AXAML in this repository uses them (`grep -rn SharedSizeGroup --include=*.axaml SemiPlot` prints nothing).
- Label fit: no test measures this panel's label column, which is `Auto` (`AxisScalePanel.axaml:44`). The pen
  form's `PenFormPaneViewTests.EveryPenFormLabel_FitsTheLabelColumnInInter` (`PenFormPaneViewTests.cs:256-282`)
  measures its ten labels in Inter against its fixed 112 px column (`:31`), in `en` and `ru`.
- Test helpers:
  - `InterMeasure.InterTypeface` and `InterFont` (`UI/InterMeasure.cs:11-22`); `HeaderWidthInInter` takes a
    control's chrome as `ViewProbe.UnconstrainedWidthOf(window, header) - ViewProbe.UnconstrainedWidthOf(window,
    text)` and adds the Inter width of the text (`:25-32`).
  - `ViewProbe.UnconstrainedWidthOf(Window, Control)` measures at `Size.Infinity`, ceils the desired width, then
    invalidates and re-lays out the window (`UI/ViewProbe.cs:91-99`); `LeftOf(Control, Visual ancestor)` (`:68`);
    `Descendant<T>(Visual, string)` (`:34`).
  - The glyph test model: `PenFormPaneViewTests.AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`
    (`PenFormPaneViewTests.cs:176-215`) and `AValidField_ShowsNoWarningGlyph` (`:217-234`).
- Tests (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`, traits `Component=UI`, `Area=Chart`,
  `Category=Unit`, `ProcessGlobalStateCollection`, `:25-28`). The checkbox reads:
  - `:234` `TabbingBetweenTheBounds_WritesNothing_UntilFocusLeavesThePair` (`:218-237`): the second Tab from
    Maximum focuses the box and the pair is written.
  - `:252` `OnAnAutoscaledPen_TabAndEnterThroughUntouchedBounds_KeepTheAxisAuto` (`:239-255`): Tab out of the pair
    focuses the box.
  - `:290` `EnterOnTheFocusedAutoscaleButton_Autoscales_AfterThePairWasWrittenOnLeavingIt` (`:280-298`): Tab from
    Minimum focuses the box, the pair is written, the next Tab focuses the Autoscale button.
  - `:326-338` `TheLogarithmicBox_SwitchesTheAxisAndKeepsThePanelOpenAtItsSize` (`:319-345`): `box.Content` equals
    `Resources.AxisScaleLogarithmic` (`:329`); the panel and presenter sizes are equal before and after a click.
  - `:355` `TheLogarithmicBox_OverAManualMinimumOfZero_StaysClearAndNamesTheRule` (`:347-367`).
  - `:388` `TheMessageLine_KeepsThePanelsSize_EmptyWithAOneLineRuleAndWithTheLogRule` (`:369-394`, `en-US`,
    `ru-RU`): sizes equal across three message states.
  - `AnUnreadableEntry_IsMarkedAndEnterWritesNothing` (`:80-98`) types "1x" under `en-US` and asserts the
    `invalid` class on `AxisScaleMaximum` (`:92`).
  - Helpers: `OpenPanel` (`:396-404`); `PressThePanelsEmptyArea` presses 5 px above `AxisScaleBounds` at the panel's
    right edge less 4 px (`:411-416`); `SizesOf` (`:423-426`); `TypeInto` through `HeadlessInput.Clear` and `Type`
    (`:428-434`).
  - `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries` (`UI/Localization/ResourcesTests.cs:93`) and
    `EveryGeneratedAccessor_ResolvesToANonEmptyValue` (`:24`).
- Docs: `ui-theme.md:219-225` (the glyph bullet); `ui-theme.md:363-398` (the panel paragraph `:365-373`: "the
  Logarithmic scale `CheckBox` row" `:367`, "The checkbox spans the panel from the left edge under the labels"
  `:370`, "a refused Logarithmic scale click" `:372`; the measured table `:381-382`); `ui-text.md:334-353` (the keys
  the panel reads `:336-337`, the checkbox "under Minimum reads `AxisScaleLogarithmic`" `:345-346`);
  `trend-interaction.md:168`, `:385` ("Logarithmic scale checkbox"); `trend-feature-spec.md:77` ("the axis scale
  panel's Logarithmic scale checkbox").
- Rules applied: `desktop-ui` Alignment (`SKILL.md:119-132`: one grid, one field edge, a checkbox in the field
  column, a label when the box's text does not name the setting), Field widths (`:148-150`: at least 24 px of slack
  after the widest field), Validation (`:215`: a red border plus an icon inside the field), Text in the UI
  (`:224-230`: labels never wrap, one term per concept, measure in every language), Spacing (`:108`, `:111-112`: the
  4 grid, off-scale values are debt); `base.md` Dead code; `AGENTS.md` UI (every operator string in both resx files,
  every colour a palette key) and Test (HeadlessInput, traits).

## Development Approach

- **Testing approach**: TDD. Task 1 writes the reproduction first and runs it red before the AXAML changes; Task 2
  writes the glyph test first and runs it red before the style lands.
- Complete each task fully before the next; every task ends with its tests passing.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every existing assertion of `AxisScalePanelViewTests` keeps its meaning; one line changes its expected key.

## Testing Strategy

- **View tests**: the one-edge reproduction, the Inter fit guard, the hit-area test and the glyph tests live in
  `AxisScalePanelLayoutTests`, beside `AxisScalePanelViewTests`, both over `AxisScalePanelDriver`; one content
  assertion in `AxisScalePanelViewTests` moves to the new key.
- **Resource tests**: `ResourcesTests` runs unchanged over both sets after the key goes.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- `AxisScalePanelLayoutTests` carries the three traits and the collection of `AxisScalePanelViewTests`.
- Every click, cleared field, typed text and key press goes through `HeadlessInput` (`AGENTS.md`, Test).
- Widths the operator sees are measured in Inter, as the pen form and the export window do; the headless font only
  gates edges and equal sizes.

## Acceptance Evidence

The defect is automatable. At HEAD the new
`AxisScalePanelLayoutTests.TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` compiles and fails at run
time: the box's left edge is 0 px in panel coordinates, and `AxisScaleMaximum`'s is the label column plus 14 px.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~AxisScalePanelViewTests|FullyQualifiedName~AxisScalePanelLayoutTests|FullyQualifiedName~AxisScalePanelViewModelTests|FullyQualifiedName~ResourcesTests"
```

It must report 0 failed. These named tests must be present and pass:

- `AxisScalePanelLayoutTests.TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` (the reproduction;
  theory: `en-US`, `ru-RU`) - Task 1
- `AxisScalePanelLayoutTests.TheScaleTypeRow_FitsThePanelInInter` (theory: `en-US`, `ru-RU`) - Task 1
- `AxisScalePanelLayoutTests.AClickOnTheScaleTypeLabelOrBesideTheBox_LeavesTheFlagAndGivesThePanelTheKeyboard` -
  review
- `AxisScalePanelViewTests.TheLogarithmicBox_SwitchesTheAxisAndKeepsThePanelOpenAtItsSize` (reads
  `PenFormLogarithmic`) - Task 1
- `AxisScalePanelViewTests.TabbingBetweenTheBounds_WritesNothing_UntilFocusLeavesThePair` (unchanged) - Task 1
- `AxisScalePanelViewTests.OnAnAutoscaledPen_TabAndEnterThroughUntouchedBounds_KeepTheAxisAuto` (unchanged) - Task 1
- `AxisScalePanelViewTests.EnterOnTheFocusedAutoscaleButton_Autoscales_AfterThePairWasWrittenOnLeavingIt`
  (unchanged) - Task 1
- `AxisScalePanelViewTests.TheLogarithmicBox_OverAManualMinimumOfZero_StaysClearAndNamesTheRule` (unchanged) - Task 1
- `AxisScalePanelViewTests.TheMessageLine_KeepsThePanelsSize_EmptyWithAOneLineRuleAndWithTheLogRule` (unchanged) -
  Task 1
- `AxisScalePanelViewTests.APressOnThePanelsEmptyArea_WritesThePendingPairWithThePanelOpen` (unchanged) - Task 1
- `AxisScalePanelLayoutTests.AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` - Task 2
- `AxisScalePanelLayoutTests.AValidBound_ShowsNoWarningGlyph` (both bounds) - Task 2
- `AxisScalePanelLayoutTests.ABoundCorrected_DropsTheWarningGlyph` - review
- `AxisScalePanelViewTests.AnUnreadableEntry_IsMarkedAndEnterWritesNothing` (unchanged) - Task 2
- `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries` (unchanged) - Task 3
- `ResourcesTests.EveryGeneratedAccessor_ResolvesToANonEmptyValue` (unchanged) - Task 3

`$BASE` is `9c2f74c`, this branch's start (Context). If `git merge-base --is-ancestor 9c2f74c HEAD` exits 1 after a
rebase, `$BASE` is `$(git merge-base HEAD origin/master)`.

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the two NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -nE "Resources\.AxisScaleLogarithmic|name=\"AxisScaleLogarithmic\"" -- SemiPlot   # prints nothing
git grep -n AxisScaleLogarithmic -- docs/architecture       # prints nothing, or only mentions of the control's Name
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.UI ':!SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml' ':!SemiPlot/SemiPlot.UI/Localization' ':!SemiPlot/SemiPlot.UI/Styles/Forms.axaml' ':!SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml'   # prints nothing
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.DataSource.Postgres ConfigFiles   # prints nothing
```

The key gate's lowercase `name=` is intentional: `git grep` is case-sensitive, so it matches the resx entry
`<data name="AxisScaleLogarithmic"` and not the control's `Name="AxisScaleLogarithmic"` in AXAML, which stays.

The look of the row in the real font and theme cannot be seen headlessly; the Inter guard covers the fit, and the
manual checks under Post-Completion cover the rest.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The panel stays two grids that share one label column.** `AxisScaleBounds` keeps the two bounds. A new grid
  `AxisScaleType` takes the outer grid's row 2 and holds the label and the checkbox. Both grids name their label
  column `SharedSizeGroup="AxisScaleLabel"`, and the outer grid sets `Grid.IsSharedSizeScope="True"`, so the label
  column is the width of the widest of the three labels in every language. Both grids use `ColumnSpacing="14"`, so
  the box starts at the fields' left edge.
- **An invalid bound shows the warning glyph.** A panel-scoped style `Grid#AxisScaleBounds TextBox.invalid` sets the
  pen form's `InnerRightContent` template.
- **Nothing else moves.** The header, the buttons, the message line, the view model and the code-behind stay.

Design decisions, each with its reason:

- **The checkbox stays outside `AxisScaleBounds`.** The commit trigger is that grid's `IsKeyboardFocusWithin`
  (`AxisScalePanel.axaml.cs:21-22`, `:45-60`). Inside it, a Tab from Minimum onto the box would keep focus-within
  true, write nothing, and turn the three focus pins at `AxisScalePanelViewTests.cs:234`, `:252` and `:290` red. That
  is a change to the panel's commit behaviour, which the scope guard forbids. One form grid with three rows would
  need a second commit trigger; two grids with a shared label column need none.
- **A shared `Auto` column first, a fixed `ColumnDefinitions` resource as the fallback.** The pen form's precedent is
  a fixed resource, `PenFormColumns` with `x:Shared="False"` (`PenEditorWindow.axaml:24-28`). This panel's column
  is `Auto` today (`AxisScalePanel.axaml:44`), and a shared `Auto` column keeps it at the widest label in each
  language by construction (`SKILL.md:120`), so a later label change needs no re-measured width. A fixed width
  needs an Inter test to stay right and leaves blank width in the language with the shorter labels. If the shared
  column does not line up, the panel takes the precedent: a `ColumnDefinitions` resource `AxisScaleColumns` in
  `UserControl.Resources`, `x:Shared="False"`, with a label column on the 4 grid (`SKILL.md:108`) and a `*` field
  column. The label width leaves at least 24 px of slack after the 140 px field in the 280 px panel
  (`SKILL.md:148-150`): width + 14 + 140 <= 256, so at most 100 px. It is the smallest multiple of 4 that holds the
  widest Inter label in both languages; 80 px is an example (Technical Details).
- **The label reads `PenFormScaleTypeLabel`, the box reads `PenFormLogarithmic`.** The roadmap's target puts a label
  in the label column (`20261102-visual-chore-roadmap.md:89-90`) and names the pen pane's row "Тип шкалы (checkbox
  "Логарифмическая")" (`:68`). The current text does not fit the field column in Russian: the 199 px box is wider
  than the 192 px column (Context). The box therefore takes the short state word, and "Logarithmic" alone no longer
  names the setting, so the label column names it (`SKILL.md:121-122`). Moving the current text into the label
  instead puts "Логарифмическая шкала" in the label column and widens it for every row, so the 140 px fields lose
  their slack. The pen form shows the same setting with the same two keys (`PenEditorWindow.axaml:472-483`), so the
  term is one across both windows (`SKILL.md:228`). Reusing a key for the same concept is this repository's rule
  (`ui-text.md:236-238`), and the pen form already reads two `AxisScale*` keys, so the `PenForm` prefix marks where
  the key started, not who may read it. No new key is added.
- **`AxisScaleLogarithmic` goes from both resource files.** After the change no AXAML and no C# reads it
  (`base.md`, Dead code). The control keeps `Name="AxisScaleLogarithmic"`, so every test read by name stays.
- **The panel width stays 280 px.** `TheScaleTypeRow_FitsThePanelInInter` checks in Inter that the label column, the
  gap and the 140 px field leave 24 px of slack, and that the label column, the gap and the box fit the panel, in
  both languages.
- **`ColumnSpacing="14"` is copied, not fixed.** 14 is off the 4/8/12/16/24/32 scale (`SKILL.md:111-112`). The
  ui-spacing-scale slice owns every remaining inset (`20261102-visual-chore-roadmap.md:270-275`); this slice only
  makes the two grids agree.
- **The box takes `HorizontalAlignment="Left"`**, as the pen form's box does (`PenEditorWindow.axaml:480`). Today the
  box stretches across the row, so a click anywhere on it toggles the flag; after the change only the box and its
  text toggle it, and a click on the label or on the row's empty right part gives the panel the keyboard, as a
  press on any other empty area does (`trend-interaction.md:357-359`).
- **The panel adopts the warning glyph.** `ui-theme.md:223-225` says another form adopts the glyph "when it is next
  touched", and this slice touches this form. The style sits in `UserControl.Styles`, scoped to `AxisScaleBounds`, as
  the pen form scopes its style to `Panel#PenForm` (`PenEditorWindow.axaml:79-89`). The `invalid` class and its
  bindings do not change (`AxisScalePanel.axaml:58`, `:76`), and each box keeps `Width="140"`, so the glyph takes
  room inside the box and the panel keeps its size.

## Technical Details

### `Chart/AxisScalePanel.axaml`

- A `UserControl.Styles` block after `UserControl.KeyBindings` (`:16-19`):

  ```xml
  <UserControl.Styles>
      <Style Selector="Grid#AxisScaleBounds TextBox.invalid">
          <Setter Property="InnerRightContent">
              <Template>
                  <PathIcon Width="16"
                            Height="16"
                            Margin="0,0,8,0"
                            Data="{StaticResource WarningGeometry}"
                            Foreground="{DynamicResource AppSeverityErrorBrush}" />
              </Template>
          </Setter>
      </Style>
  </UserControl.Styles>
  ```

- The outer grid (`:21-22`) gains `Grid.IsSharedSizeScope="True"`.
- `AxisScaleBounds` (`:41-46`) replaces `ColumnDefinitions="Auto,*"` with the element form, because the attribute
  shorthand cannot carry `SharedSizeGroup`:

  ```xml
  <Grid.ColumnDefinitions>
      <ColumnDefinition Width="Auto"
                        SharedSizeGroup="AxisScaleLabel" />
      <ColumnDefinition Width="*" />
  </Grid.ColumnDefinitions>
  ```

- The `CheckBox` (`:86-90`) becomes:

  ```xml
  <Grid Grid.Row="2"
        Name="AxisScaleType"
        ColumnSpacing="14">
      <Grid.ColumnDefinitions>
          <ColumnDefinition Width="Auto"
                            SharedSizeGroup="AxisScaleLabel" />
          <ColumnDefinition Width="*" />
      </Grid.ColumnDefinitions>

      <TextBlock Grid.Column="0"
                 Name="AxisScaleTypeLabel"
                 VerticalAlignment="Center"
                 Text="{x:Static text:Resources.PenFormScaleTypeLabel}" />

      <CheckBox Grid.Column="1"
                Name="AxisScaleLogarithmic"
                HorizontalAlignment="Left"
                Content="{x:Static text:Resources.PenFormLogarithmic}"
                IsChecked="{Binding IsLogarithmic, Mode=OneWay}"
                Command="{Binding ToggleLogarithmicCommand}" />
  </Grid>
  ```

- The fallback, only if the shared-size ASSUMPTION fails: drop `Grid.IsSharedSizeScope` and both `SharedSizeGroup`
  columns; add to a new `UserControl.Resources` block

  ```xml
  <ColumnDefinitions x:Key="AxisScaleColumns"
                     x:Shared="False">
      <ColumnDefinition Width="80" />
      <ColumnDefinition Width="*" />
  </ColumnDefinitions>
  ```

  and give both grids `ColumnDefinitions="{StaticResource AxisScaleColumns}"`. 80 is the example; the run sets the
  smallest multiple of 4 at or above the widest Inter label the fit test reports, at most 100.

### `Localization/Resources.resx`, `Resources.ru.resx`

- Delete the `AxisScaleLogarithmic` entry from both (`:168-170` in each). No entry is added.

### Test changes

| Test | Today | After |
| --- | --- | --- |
| `TheLogarithmicBox_SwitchesTheAxisAndKeepsThePanelOpenAtItsSize` `:329` | `box.Content` is `Resources.AxisScaleLogarithmic` | `box.Content` is `Resources.PenFormLogarithmic` |
| `:234`, `:252`, `:290` | Tab out of the pair focuses `AxisScaleLogarithmic` | unchanged: the label `TextBlock` is not focusable, so the box is still the next tab stop after Minimum |
| `:355`, `:388` | the box by name | unchanged |
| `:326-338`, `:369-394` size checks | sizes equal before and after a state change | unchanged: both sides of each comparison see the new row and the glyph |
| `AnUnreadableEntry_IsMarkedAndEnterWritesNothing` `:92` | the `invalid` class on `AxisScaleMaximum` | unchanged: the class drives the glyph |
| `PressThePanelsEmptyArea` `:411-416` | 5 px above `AxisScaleBounds` | unchanged: rows 0 and 1 do not move |

### The fit test's label column

`TheScaleTypeRow_FitsThePanelInInter` takes the label column as the widest Inter label of the three, since the
shipped column 0 of `AxisScaleBounds` is `Auto`; it reads the gap from each grid's `ColumnSpacing` and the field width
from `AxisScaleMaximum`. A headless `Auto` column is the headless font's width, 140 px for "Scale type" in
English (Context), and would fail a fit that holds in Inter; a fixed column is the same width headless and on
screen, so its realised width counts.

### ASSUMPTIONs, each with its guard and fallback

- ASSUMPTION: `SharedSizeGroup` on an `Auto` column, inside a `Grid.IsSharedSizeScope` ancestor, gives both grids
  one label column width in Avalonia 12.0.5. Guard: the `ActualWidth` and left-edge assertions of
  `TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` in both cultures. Fallback: the
  `AxisScaleColumns` resource (Technical Details), gated in Inter by `TheScaleTypeRow_FitsThePanelInInter`, which
  then also asserts every label at most the column width in `en-US` and `ru-RU`. The run records a ⚠️ under Task 1
  with the width chosen, and Task 5 states the fixed width.
- ASSUMPTION: headless, the English label column grows from 98 px ("Maximum", 7 glyphs at 14 px) to 140 px ("Scale
  type", 10 glyphs), so the 140 px bounds end at 294 px and overflow the 280 px panel by 14 px under the test font.
  The fields' centres stay inside the panel, so the clicks of the existing tests still land. Guard: the full
  `AxisScalePanelViewTests` run in Task 1. Fallback: none needed for Inter, which `TheScaleTypeRow_FitsThePanelInInter`
  checks; if a headless click misses, the run records which test and a ⚠️ under Task 1.
- ASSUMPTION: the box's string content is shown through one `TextBlock` among the box's visual descendants, so the
  chrome is `UnconstrainedWidthOf(window, box) - UnconstrainedWidthOf(window, text)`, the `HeaderWidthInInter`
  pattern. Guard: the fit test asserts one such `TextBlock` with `Text` equal to `Resources.PenFormLogarithmic` and a
  positive chrome. Fallback: the chrome is the 2026-10-16 English box width, 141 px, less the Inter width of
  "Logarithmic scale", recorded as a ⚠️ under Task 1.
- ASSUMPTION: `WarningGeometry` resolves as a `StaticResource` from inside the flyout, as it does in the pen editor
  window. Guard: `AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` asserts the glyph's `Data` is the
  resource `panel.TryFindResource("WarningGeometry", out _)` returns. Fallback: `DynamicResource` for `Data`, and a ⚠️
  under Task 2.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): the reproduction and the fit guard, the row's AXAML, the glyph style
  and its tests, the key deletion, the gates, the architecture docs.
- **Post-Completion** (no checkboxes): the look in the running viewer and the re-measure of the 2026-10-16 table.

## Implementation Steps

### Task 1: Move the checkbox into the field column under a label, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`

- [x] write the failing `[AvaloniaTheory]` `TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` with
      `[InlineData("en-US")]` and `[InlineData("ru-RU")]`, in the pattern of `:369-394` (`CultureScope`,
      `CreateLoadedViewModel`, `ShowChart`, `OpenPanel`). With `box` = `AxisScaleLogarithmic`, `bounds` =
      `panel.FindControl<Grid>("AxisScaleBounds")` and `row` = `panel.FindControl<Grid>("AxisScaleType")`, it asserts
      in this order: `ViewProbe.LeftOf(box, panel)` equals `LeftOf(AxisScaleMaximum, panel)` and
      `LeftOf(AxisScaleMinimum, panel)` within 0.5 px; `row` exists and is no visual descendant of `bounds`, and
      `box`'s parent is `row`; `row.ColumnDefinitions[0].ActualWidth` equals `bounds.ColumnDefinitions[0].ActualWidth`
      within 0.5 px; `panel.FindControl<TextBlock>("AxisScaleTypeLabel")` has `Text` `Resources.PenFormScaleTypeLabel`,
      parent `row` and `Grid.GetColumn` 0; `box.Content` is `Resources.PenFormLogarithmic`
- [x] run it at HEAD: red in both cultures at the first assertion (the box at 0 px, the fields at the label column
      plus 14 px); note the two actual values under this task
      - red at the first assertion, headless: `en-US` box 0.0 px, fields 112.0 px; `ru-RU` box 0.0 px, fields 126.0 px
- [x] write `[AvaloniaTheory]` `TheScaleTypeRow_FitsThePanelInInter` (`en-US`, `ru-RU`): with `InterTypeface()` and
      `InterFont(typeface, label.FontSize)`, `labelColumn` is computed as Technical Details states over the two
      bounds labels and `AxisScaleTypeLabel`; `labelColumn + 14 + 140` is at most `panel.Width - 24`; `labelColumn +
      14 + chrome + InterWidth(Resources.PenFormLogarithmic)` is at most `panel.Width`, with the chrome measured as the
      chrome ASSUMPTION states. If the fallback runs, it also asserts each label's Inter width is at most the fixed
      column width. The test writes the Inter label widths and the box width to the test output
- [x] change `:329` to `box.Content.Should().Be(Resources.PenFormLogarithmic)`
- [x] change the AXAML row and columns as Technical Details states (the style is Task 2)
- [x] run `AxisScalePanelViewTests` and `AxisScalePanelViewModelTests`: every case passes; note under this task
      whether the shared-size fallback ran, and the Inter widths the fit test reported in each culture
      - 69 passed, 0 failed. The shared-size fallback did not run: the `SharedSizeGroup` column lines up in both
        cultures
      - Inter, `en-US`: labels Maximum 63.7, Minimum 59.8, Scale type 69.6 px; label column 69.6 px; box 101.9 px
        (chrome 24.0 px)
      - Inter, `ru-RU`: labels 72.8, 66.1, 75.0 px; label column 75.0 px; box 149.2 px (chrome 24.0 px)

### Task 2: Show the warning glyph inside an invalid bound, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/AxisScalePanel.axaml`

- [x] write the failing `AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`, modelled on
      `PenFormPaneViewTests.cs:176-215`: under `CultureScope("en-US")`, record `SizesOf(panel, message)` and both
      boxes' `Bounds`; `TypeInto(..., "AxisScaleMaximum", "1x")`; `HeadlessInput.Clear(shown.Window, minimum)`. For
      each box: the `invalid` class is set; `InnerRightContent` is a visible `PathIcon` whose `Data` is the resource
      `panel.TryFindResource("WarningGeometry", out var warning)` returns; the glyph's left, right, top and bottom
      edges lie inside the box (`ViewProbe.LeftOf` and the box's `Bounds`). The two glyphs are different instances;
      both boxes' `Bounds` and `SizesOf(panel, message)` are unchanged (red at HEAD: `InnerRightContent` is null)
      - red at HEAD: `InnerRightContent` was null on `AxisScaleMaximum`
- [x] write `AValidBound_ShowsNoWarningGlyph`: on the opened panel `AxisScaleMaximum` has no `invalid` class and a
      null `InnerRightContent`
- [x] add the `UserControl.Styles` block as Technical Details states
- [x] run `AxisScalePanelViewTests`: every case passes, the Task 1 tests and
      `AnUnreadableEntry_IsMarkedAndEnterWritesNothing` included; note whether the resource fallback ran
      - 71 passed, 0 failed (with `AxisScalePanelViewModelTests`). The resource fallback did not run:
        `StaticResource WarningGeometry` resolves inside the flyout

### Task 3: Remove the key the panel no longer reads

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`

- [x] confirm the guard first: `git grep -nE "Resources\.AxisScaleLogarithmic" -- SemiPlot` prints nothing after
      Task 1
- [x] delete the `AxisScaleLogarithmic` entry from both files (`:168-170` in each)
- [x] run `ResourcesTests` and the full `SemiPlot.Tests.Unit`: every case passes
      - `ResourcesTests` 11 passed; `SemiPlot.Tests.Unit` 2746 passed, 0 failed

### Task 4: Verify acceptance criteria

- [x] run the filtered command under Acceptance Evidence: 0 failed, every named test present (note the pass count)
      - 82 passed, 0 failed; all 14 named tests present and passed, the three theories in both cultures
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
      - 2746 passed, 0 failed
- [x] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the two NU1902 for `MessagePack` 2.5.302 in
      `SemiPlot.AppHost` that the base reports
      - 0 errors; the only warning is NU1902 for `MessagePack` 2.5.302 in `SemiPlot.AppHost`
- [x] set `$BASE` as Acceptance Evidence states
      - `$BASE` is `9c2f74c`: `git merge-base --is-ancestor 9c2f74c HEAD` exits 0
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse` over every
      `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
      - both exit 0; the one `.cs` file is `SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`
- [x] run the HeadlessInput gate, the key gate, the docs gate (after Task 5) and the two scope gates under
      Acceptance Evidence: each prints what it must
      - HeadlessInput gate: only `HeadlessInput.cs` lines; key gate: nothing; both scope gates: nothing
      - ⚠️ docs gate: still prints `docs/architecture/ui-text.md:345`, the resource key in the prose Task 5
        rewrites; Task 5 reruns it

### Task 5: [Final] Update documentation

- [x] `docs/architecture/ui-theme.md:223-225`: the pen editor's form and the axis scale panel carry the glyph, the
      panel through its own `Grid#AxisScaleBounds TextBox.invalid` style; another form adopts it when it is next
      touched. Name the gate `AxisScalePanelLayoutTests.AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`
- [x] `ui-theme.md:365-373`: the rows are the header, the bounds grid, the Scale type row (the label
      `PenFormScaleTypeLabel` in the label column and the Logarithmic `CheckBox` in the field column), the two
      buttons and the message line. Replace "The checkbox spans the panel from the left edge under the labels"
      (`:370`) with: the box starts at the fields' left edge; the bounds grid and the Scale type grid share their
      `Auto` label column through `SharedSizeGroup`, so the form has one label edge and one field edge
      (`desktop-ui` Alignment); the box keeps its own grid outside `AxisScaleBounds`, so focus onto it ends the
      pair's edit (`trend-interaction.md#the-axis-scale-panel`); the box is `HorizontalAlignment="Left"`, so only the
      box and its text toggle the flag, and a click on the label or the empty right part of the row gives the panel
      the keyboard. Name the gate
      `AxisScalePanelLayoutTests.TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` and the Inter guard
      `TheScaleTypeRow_FitsThePanelInInter`. `:372`: "a refused Logarithmic click"
- [x] `ui-theme.md:381-382`: the measured table predates this layout. Mark the field column row and the checkbox row
      as measured before the Scale type row, and add the Inter widths Task 1 noted, labelled as Inter measurements;
      the Skia and HarfBuzz re-measure is a Post-Completion check
- [x] `docs/architecture/ui-text.md:336-337`: the panel also reads `PenFormScaleTypeLabel` and `PenFormLogarithmic`;
      `:345-346`: the Scale type row reads `PenFormScaleTypeLabel` ("Scale type" / "Тип шкалы") and its box
      `PenFormLogarithmic` ("Logarithmic" / "Логарифмическая"), the pen form's pair for the same setting;
      `:236-238`: the axis scale panel reads the pen form's two scale type keys as the pen form reads its bound
      labels
- [x] `docs/architecture/trend-interaction.md:168`, `:385` and `docs/architecture/trend-feature-spec.md:77`: "the
      Logarithmic checkbox" in place of "the Logarithmic scale checkbox"; `:385` keeps its rule (own row, outside
      `AxisScaleBounds`) and adds that the row's label column is shared with the bounds grid
- [x] if the shared-size fallback ran, state the `AxisScaleColumns` resource and its fixed width in `ui-theme.md`
      instead of the shared group
      - the fallback did not run (Task 1), so `ui-theme.md` states the shared group
- [x] ➕ rerun the docs gate `git grep -n AxisScaleLogarithmic -- docs/architecture`: prints nothing
- [x] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** in the running viewer (a press on the active pen's axis), in both languages and both
theme variants:

1. The Logarithmic box starts at the left edge of the Maximum and Minimum fields; "Scale type" / "Тип шкалы" starts
   at the labels' edge.
2. "Логарифмическая" fits on one line in the field column; nothing is trimmed and nothing passes the panel's right
   edge.
3. Tab from Minimum lands on the box and writes the pair; a click on the box switches the axis and the panel keeps
   its size; a click on the "Scale type" label does not toggle the box.
4. "1x" in Maximum shows the red border and the warning glyph inside the box; the panel keeps its size.
5. Re-measure the panel with Skia and HarfBuzz on the Windows machine fonts, the 2026-10-16 method
   (`ui-theme.md:375-376`), and replace the field column and checkbox rows of the table.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: ui-spacing-scale (every remaining
inset onto the 4/8/12/16/24/32 scale, the `12,2` button padding with them). It depends on this slice
(`20261102-visual-chore-roadmap.md:281`).

**Executed by exec:**
- branch: axis-scale-checkbox-column

## Verify it yourself

1. One left edge. Press the active pen's axis: the Logarithmic box starts where the Maximum and Minimum fields
   start, under a "Scale type" / «Тип шкалы» label. At `export-pen-choice-list` the box starts at the labels' edge
   (`AxisScalePanelLayoutTests.TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel`, en-US and ru-RU).
2. Fit. «Логарифмическая» fits on one line and nothing passes the panel's right edge
   (`TheScaleTypeRow_FitsThePanelInInter`).
3. Hit area. A click on the label or beside the box leaves the flag and gives the panel the keyboard; a click on the
   box switches the axis and the panel keeps its size
   (`AClickOnTheScaleTypeLabelOrBesideTheBox_LeavesTheFlagAndGivesThePanelTheKeyboard`,
   `AxisScalePanelViewTests.TheLogarithmicBox_SwitchesTheAxisAndKeepsThePanelOpenAtItsSize`).
4. Glyph. "1x" in Maximum shows the red border and the warning glyph inside the box without resizing; "50" removes
   both (`AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`, `ABoundCorrected_DropsTheWarningGlyph`).
   The pen editor's form keeps its glyph from the same shared style
   (`PenFormPaneViewTests.AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`).
5. The commit rules are unchanged: Tab between the bounds writes nothing until focus leaves the pair
   (`TabbingBetweenTheBounds_WritesNothing_UntilFocusLeavesThePair`).

Run all of it: the Acceptance Evidence command of this plan (84 passed, 0 failed on `c093478`).
