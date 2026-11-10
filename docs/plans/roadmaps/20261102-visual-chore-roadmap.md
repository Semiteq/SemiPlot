# Visual chore roadmap

**Issues:** #111 (closed by slice `pen-editor-pens-pane`). The other slices have no issue: they
bring the remaining windows to the `desktop-ui` rules (`dev:desktop-ui` skill, confs-cc
`d682062`).

## Summary

The pen editor shows every pen setting twice, in a read-only table and in a form. The table has
no room for the next setting, and its two inert checkboxes are #111. Seven slices rebuild the
editor as one three-pane window with a pen filter and bring the export window, the axis scale panel and every
remaining off-scale inset to the same rules. The roadmap closes when every slice below has a
merged PR.

**Thesis:** every window shows each value once, edits it in one place, and takes its sizes and
insets from the `desktop-ui` tables, so a new pen setting costs one form row and no column.

**Verified against code on 2026-11-02 (`df129f7`). Trust rule: prefer the shapes over the
numbers if they have drifted.**

## Root cause

The pen editor was built table-first: each setting became a table column and a form field. The
table's fixed columns sum to 1136 px, and the one star column (groups) gets 100 px at the
1260 px minimum, so the next setting has no column to go into. The log-scale flag (`823c687`)
cost 100 AXAML lines, 64 view-model lines across six files, and a 160 px column measured in two
languages. The two tabs show one many-to-many relation from two sides, in opposite
orientations, under a bottom bar whose refresh button serves only one of them.

| Area | Today | Cost |
| --- | --- | --- |
| Pen table | 11 columns, `ListBox` with hand-made header buttons, no sort arrow, read-only checkboxes | a new setting = a measured column, a sort arm, a header width; #111 |
| Pen form | 5-column grid under the table, `*` fields, three checkbox conventions, 240 px disabled block with no selection | uneven edges, no empty state |
| Groups tab | list left, members right, rename/fold/delete 400 px below the list | two orientations of one relation |
| Colour field | free text box, `#` deletable | a typed colour can lose its prefix |
| Export pen list | checkbox inside a selectable `ListBox` row | two states per row |
| Axis scale panel | log checkbox at the label edge | two left edges in one form |
| Insets | 22 off-scale `Margin`/`Padding`/`Spacing` values (20, 14, 10, 6) in 7 AXAML files, 19 outside the editor; plus `Padding="12,2"` on 15 buttons in 7 more files | no scale, insets do not nest |

Size of the editor today: `PenEditorWindow.axaml` 691 lines, its code-behind 315, the editor view
models about 1160, `PenEditorViewTests` 1005, `PenGroupsViewTests` 418.

## Target end state

### Behaviour

**The editor window.** One window, no tabs: pen list | pen pane (420 px) | groups pane
(340 px). Top bar: filter on the left; added count and `Refresh pen list` on the right. Window
margin and gaps 16. The window opens at 1440 px where the screen's working area allows, and its
minimum width is the fixed panes, the gaps and the list's first columns at their minimums. Below
the opening width the list scrolls horizontally.

**The pen list** is a read-only `DataGrid`: Номер | Имя (colour dot inside the cell) | Ед. изм. |
При запуске (a check glyph, not a control) | Группы. A header click sorts ascending, a second
click descending; the arrow shows the direction and never clips the header text. Every column
resizes. Its minimum is the realised header's width; there is no maximum; Группы takes the spare
width. Every truncated cell ends in an ellipsis and carries the full text as a tooltip. The cell
focus outline is hidden. Widths are not saved between opens.

**The pen pane.** With no pen selected: one hint line, "Выберите перо в списке слева". With a pen
selected: a one-line title (id, then name), then three sections with bold sentence-case headers
on one grid, a 112 px label column and one field edge:

| Section | Rows |
| --- | --- |
| Подпись | Имя (240), Ед. изм. (120), Маска (120), Пример (derived, own row) |
| Вид | Цвет (120 box + square swatch picker), Линия (240) |
| При запуске | Видимость (checkbox "Показывать на графике"), Минимум (120), Максимум (120), Тип шкалы (checkbox "Логарифмическая") |

One reserved message line closes the form. An invalid field shows the `invalid` border and an
icon inside the field. Edit semantics stay as they are: a field writes when its edit ends, the
scale pair writes when focus leaves both boxes, every call runs through the one queue.

**The colour field** normalises as the operator types: hex digits only, upper-cased, `#`
maintained by the behaviour, the caret on the digit it followed, a paste with or without `#`
accepted, placeholder `#RRGGBB`. It is the time and date fields' input behaviour with one more
layout, not a second class.

**The groups pane.** Title "Группы", subtitle "Отмечены группы выбранного пера", a new-name box
with "Создать". Then a `DataGrid` with three columns: the membership checkbox (empty header) |
Группа, with the pen count as right-aligned secondary text inside the cell | Действия, with a
"..." button. Row selection has no highlight. The "..." menu holds Переименовать, Свёрнута при
открытии (checkable) and Удалить. Rename and the delete confirmation appear in a reserved block
under the list that exists in every state; delete names the group and its pen count. With no pen
selected the membership column is disabled.

**The export pen list** is a list of checkboxes with no row selection: one state per row.

**The axis scale panel's** log-scale checkbox sits in the field column, with a label in the label
column.

**Every window's insets** read off the 4/8/12/16/24/32 scale; `ui-theme.md` records the measured
values.

### Today | Target

| | Today | Target |
| --- | --- | --- |
| Pen editor layout | two tabs, table over form | three panes, no tabs |
| Pen settings shown | twice (column + field) | once (form) |
| A new pen setting | column + field + sort arm + width | one form row |
| Pen list control | `ListBox` + header buttons | read-only `DataGrid` |
| Flags in the list | inert checkboxes | glyph |
| Group actions | rename/delete block driven by selection | per-row "..." menu |
| Membership | groups tab only, group to pens | groups pane, pen to groups |
| Colour input | free text | normalised `#RRGGBB` |
| Export pen list | selectable rows with checkboxes | checkboxes only |
| Off-scale insets | 22 in 7 files, plus the `12,2` button padding | 0 |
| Pen list filter | none | id, name or group, case-insensitive |

## Why it is safe

- The editor is reached from one place: `MainWindowViewModel.ShowPenEditorCommand` emits a
  `PenEditorViewModel` on `PenEditorRequests`, and `MainWindow` shows `PenEditorWindow` with
  `ShowDialog` (`git grep -l PenEditorWindow -- SemiPlot/SemiPlot.UI` prints `MainWindow.axaml.cs`
  and the editor's own files). Nothing else constructs the editor's view models.
- The write path does not change: every editor call still runs through `ArchiveCallQueue` and
  `IPenCatalogueEditor`, and `RegisterNewPensAsync` keeps its one caller, `PenEditorViewModel`.
  The new layout only moves where the existing view models are shown.
- Membership already writes through one path, `PenGroupWriter.RunAsync(SetMembershipAsync)` with
  `PenGroupViewModel.SetMembership`; the groups pane binds the same path from the pen side.
- `TimeFieldInput` has two consumers, `TimeWindowPanel.axaml` and `CsvExportWindow.axaml`, plus
  `TimeFieldInputTests`. A new layout arm leaves the three existing ones untouched.
- The export pen list is read by `CsvExportViewModel` through `ExportPenChoice.IsTicked`; the
  selection of the `ListBox` is never read.
- `DataGrid` is new to the project: `Avalonia.Controls.DataGrid` 12.0.1 (depends on Avalonia
  12.0.5, the pinned version) and `Semi.Avalonia.DataGrid` 12.0.0. Both are on nuget.org, the only
  source `nuget.config` keeps.

## Guard strategy

- `TreatWarningsAsErrors` and `EnforceCodeStyleInBuild` fail the build on a style or analyzer
  regression; the pre-commit hook runs `dotnet format --verify-no-changes` and `terse`.
- `PenEditorViewTests` and `PenGroupsViewTests` realise the window headlessly; each slice rewrites
  the tests of the part it rebuilds and keeps the write-path assertions (a field writes on edit
  end, the scale pair writes once, the drain on close).
- `CsvExportWindowTests` reads `ExportPens` as a `ListBox` (nine reads between lines 407 and
  722); the export slice changes those tests with the control.
- `TimeFieldInputTests` pins the three existing layouts; the colour slice adds the hex layout's
  cases, including the caret right after `#`.
- The settings dialog tests assert equal bounds before and after a field turns invalid; the
  spacing slice keeps them green.
- `AGENTS.md` greps stay empty: `RegisterNewPensAsync` has one caller, the chart and legend
  folders reference no `IPenCatalogueEditor`.

Each guard is a hypothesis; the slice plan confirms it fires at HEAD.

## Slices

### Slice hex-color-input — Status: DONE
- **Scope:** Give the fixed-format input behaviour now in `Navigation/TimeFieldInput` a hex
  colour layout: keep hex digits, upper-case them, maintain the leading `#`, keep the caret on the
  digit it followed (right after `#` when no digit precedes it), accept a paste with or without
  `#`. Use it on the pen form's colour field with the placeholder `#RRGGBB`. The behaviour stays
  one class with one algorithm; renaming it to a format-neutral name is in scope if the planner
  finds the time-specific name misleading. The pen form's commit semantics do not change.
- **Issue:** none
- **Blast radius:** mechanism: one attached behaviour gains a layout and a format-neutral name;
  surface: the colour field, the two AXAML consumers' attribute name, the behaviour's test class
  and its `charting.md` entry.
- **Risk:** low; the caret mapping for a prefixed format is the one new rule.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no layout change of the pen editor; no change to the time and date layouts'
  behaviour.
- **Plan:** `docs/plans/completed/20261102-hex-color-input.md`
- **PR:** #164 (merged)
- **Branch:** pen-editor-redesign

### Slice pen-editor-pens-pane — Status: DONE
- **Scope:** Rebuild the pens side of the editor as Target end state describes: the pen list as a
  read-only `DataGrid` (packages `Avalonia.Controls.DataGrid` 12.0.1 and
  `Semi.Avalonia.DataGrid` 12.0.0) with sort, header-measured resizable columns, a fill column, an
  ellipsis and tooltip on every cell, the colour dot in the name cell and the start flag as a
  glyph; the pen form in a 420 px pane with the empty-state hint, the one-line title, the three
  sections on one grid, the field widths, the square swatch and the closing message line. An
  invalid field shows the `invalid` border and a warning glyph in the box's `InnerRightContent`;
  rewrite `ui-theme.md`'s "A form never resizes on validation" bullet "marked by its border alone"
  to name the glyph, for the pen editor first and as the rule for every form touched afterwards.
  Header and cell text inset 12 on the left, 8 on the right; the skill's cell inset row says so
  for `DataGrid` cells. Confirm both `DataGrid` package versions resolve against Avalonia 12.0.5
  with a restore before relying on them. The header-measured column sizing is one reusable behaviour, applied before the grid is first shown,
  reserving the sort arrow's slot in the header template, growing and shrinking the fill column
  from the grid's real viewport, and subscribing once. The groups tab stays as it is in this
  slice; the window keeps its tabs until `pen-editor-groups-pane`. Rewrite `ui-theme.md`
  "A resizable window keeps its fixed parts" and the Pens row of `overview.md`'s editor table.
- **Issue:** #111
- **Blast radius:** mechanism: a new control family and a column-sizing behaviour; surface: the
  Pens tab and its view tests.
- **Risk:** high; it concentrates in the headless view tests, which allowlist the realised tree,
  and in the first-frame widths.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no groups-pane change, no membership from the pen side, no change to the edit
  semantics or the queue, no saved widths.
- **Plan:** `docs/plans/completed/20261103-pen-editor-pens-pane.md`
- **PR:** #164 (merged)
- **Branch:** pen-editor-redesign

### Slice pen-editor-groups-pane — Status: DONE
- **Scope:** Remove the editor's tabs and add the groups pane as the third column: the new-group
  row, a `DataGrid` of membership checkbox | group name with the count as secondary text | the
  "..." action menu (rename, fold flag, delete), no selection highlight, rename and delete
  confirmation in a reserved block under the list. Membership is set for the selected pen through
  the existing `PenGroupWriter` path; with no pen selected the membership column is disabled. The
  refresh button moves to the top bar. The window opens 1440 px wide where the screen's working
  area allows, else at the working area's width, and never below its 1272 px minimum. The
  confirm Удалить button takes its destructive brushes from new `Palette.axaml` keys, recorded in
  `ui-theme.md`'s key table; Semi's `Danger` class is not used. Update `overview.md`: membership
  is edited from the pen side, and the groups tab row of the editor table goes.
- **Issue:** none
- **Blast radius:** mechanism: the window's composition and the group actions' entry point;
  surface: the groups tab, `PenGroupsViewTests`, the editor's documented layout.
- **Risk:** medium; the documented rule "membership is edited on the Groups tab only" reverses,
  and a group menu's commands need the row's view model through the flyout.
- **Depends on:** pen-editor-pens-pane
- **Stacking base:** master
- **Scope guard:** no change to `IPenCatalogueEditor` or the group write path; no #140 sync.
- **Plan:** `docs/plans/completed/20261103-pen-editor-groups-pane.md`
- **PR:** #164 (merged)
- **Branch:** pen-editor-redesign

### Slice pen-list-filter — Status: DONE
- **Scope:** Add the top bar's filter box, 280 px, placeholder "Фильтр: номер, имя или группа": the
  pen list shows the rows whose id, name or group text contains the typed text, case-insensitive,
  and keeps the selection when the selected row still matches. An empty box shows every row. The
  groups pane is not filtered.
- **Issue:** none
- **Blast radius:** mechanism: one derived row list in `PenEditorViewModel`; surface: the pen list
  and `PenEditorViewTests`.
- **Risk:** low.
- **Depends on:** pen-editor-groups-pane
- **Stacking base:** master
- **Scope guard:** no sort change, no filter of the groups pane, no saved filter text.
- **Plan:** `docs/plans/completed/20261103-pen-list-filter.md`
- **PR:** #164 (merged)
- **Branch:** pen-editor-redesign

### Slice export-pen-choice-list — Status: DONE
- **Scope:** Make the export window's pen list a list of checkboxes with no row selection, so a
  row carries one state, the tick. Keep its height cap, its scroll behaviour and its tooltip.
  Update `ui-theme.md`'s export paragraph, which describes the list as a `ListBox` with a
  window-scoped `ListBoxItem` style (`ui-theme.md:250-256`).
- **Issue:** none
- **Blast radius:** mechanism: the item container of one list; surface: the export window and
  `CsvExportWindowTests`.
- **Risk:** low.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no change to the export range, the markers or the file writer.
- **Plan:** `docs/plans/completed/20261103-export-pen-choice-list.md`
- **PR:** #165 (merged)
- **Branch:** export-pen-choice-list

### Slice axis-scale-checkbox-column — Status: DONE
- **Scope:** Put the axis scale panel's log-scale checkbox in the field column with a label in the
  label column, so the panel's form has one left edge. Update the `ui-theme.md` paragraph that
  places it under the labels.
- **Issue:** none
- **Blast radius:** mechanism: one row of one flyout; surface: the axis scale panel and its view
  test.
- **Risk:** low.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no change to the scale rules or the panel's commit behaviour.
- **Plan:** `docs/plans/completed/20261103-axis-scale-checkbox-column.md`
- **PR:** #166 (merged)
- **Branch:** axis-scale-checkbox-column

### Slice ui-spacing-scale — Status: DONE
- **Scope:** Replace every off-scale `Margin`, `Padding` and `Spacing` value left in the UI's AXAML
  (settings dialog, export window, time window panel, axis scale panel, chart view, legend) with a
  value from the 4/8/12/16/24/32 scale, by the level each inset belongs to, and update the
  measured values `ui-theme.md` records. The `Padding="12,2"` button padding (15 buttons in 7
  files) moves onto the scale with them. The reserved message line keeps two 20 px lines.
- **Issue:** none
- **Blast radius:** mechanism: none; surface: every remaining window's geometry and any test that
  measures bounds.
- **Risk:** medium; widths derived from these insets (dialog widths, label columns measured in two
  languages) may need re-measuring.
- **Depends on:** export-pen-choice-list, axis-scale-checkbox-column
- **Stacking base:** master
- **Scope guard:** no control or behaviour change; no palette change.
- **Plan:** `docs/plans/completed/20261103-ui-spacing-scale.md`
- **PR:** #167 (merged)
- **Branch:** ui-spacing-scale

## Close condition

Every slice not marked DROPPED has a MERGED PR. Slices that own an Issue carry `Closes #N` in
their PR, so those issues close on merge.

## Rejected alternatives

Settled during design. Do not relitigate without new facts.

- Inline editing in the pen table: the code-behind already routes eight edit-end cases for one
  form; per-row editing multiplies them, and 100 pens do not need it.
- A property grid or a descriptor-generated form: the scale pair writes as one statement and ends
  its edit when focus leaves both boxes; a generic row per property cannot express that.
- Keeping two tabs: they show one relation from two sides in opposite orientations.
- A fixed non-editable `#` prefix in the colour box: the owner found it odd; the normalising
  behaviour replaces it.
- Star-sized table columns: a star column ends a resize drag and leaves the last column with no
  grab edge.
- A separate count column in the groups pane: at 340 px the measured header minimums leave the
  group name 70 px.
- Saving column widths: the opening widths and the resize rule cover the cases; the app has no UI
  state store and needs none for this.
- A splitter between the panes: nothing persists its position, so the operator would repair the
  layout on every open.

## Open forks for the operator

- #140 (open editors follow values stored elsewhere) is open and outside this roadmap. The new
  layout does not block it; it may want the pen form as one long-lived view model that updates
  fields in place. The design stands regardless.
