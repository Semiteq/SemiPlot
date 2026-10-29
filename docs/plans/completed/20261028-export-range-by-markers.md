# Export range by markers

## Overview

- The export window lets the operator fill a range edge from a stored time marker instead of typing
  the time. Each edge is two lines: the date, then the time box and a marker box that lists "No marker"
  and the markers on the date that edge's calendar holds. The start box lists kinds 0 (user) and 1
  (recipe start); the end box lists kinds 0, 2 (recipe end) and 3 (recipe interrupted), and only markers
  after the start.
- Choosing a marker fills the edge's date and time. A hand edit of that edge's time or date returns the
  box to "No marker". The form keeps one range and one set of rules (end after start, at most 24 h); the
  time stays editable, and the edges are independent. The viewer pairs nothing: it does not check that a
  start has an end.
- When the start box holds a labelled marker, the save dialog proposes a file name from that label: the
  label without its extension, then the start as `yyyy-MM-dd_HH-mm`, then `.csv` (`RecipeName.csv`
  started at 14:03 gives `RecipeName_2026-10-27_14-03.csv`). Otherwise the name stays
  `semiplot_<start>-<end>.csv`.
- This is the second of two PRs for #160; the first (#161) added `kind` and `locked` to the chart.
  The PR body carries `Closes #160`.

## Context (from discovery)

- `CsvExportViewModel` takes the snapshot edges, the zone, the first and newest sample and the pen
  checklist (`SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs:37-49`) and builds `ExportRangeForm`
  from them (`CsvExportViewModel.cs:59`). `TrendWindow.Build` passes the factory
  (`SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs:128-140`), and `MainWindowViewModel.RequestExport`
  snapshots the window and the pens into it (`SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:220-226`,
  factory type at `MainWindowViewModel.cs:38`).
- `WallClockRangeForm` holds `StartDate`, `StartTimeText`, `EndDate`, `EndTimeText` as archive
  wall-clock values and raises its derived properties from each setter
  (`SemiPlot/SemiPlot.UI/Navigation/WallClockRangeForm.cs:23-61`); `StartBound`/`EndBound` are
  `protected` (`WallClockRangeForm.cs:94-96`), times are `HH:mm:ss` (`WallClockRangeForm.cs:21`), and
  `ToWallClock` converts UTC through the zone (`WallClockRangeForm.cs:116-119`). `ExportRangeForm` floors
  the start and ceils the end to the second when it prefills (`SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs:12-21`)
  and exposes `From`/`To` only when valid (`ExportRangeForm.cs:54-56`).
- The window is 390 px wide and does not resize (`SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml:13-15`);
  each range row is a time box of 110 px then a stretching `CalendarDatePicker`
  (`CsvExportWindow.axaml:33-86`), and a form never resizes on validation
  (`docs/architecture/ui-theme.md:177`).
- The save dialog's name comes from `CsvFileExporter.SuggestedFileName(from, to)`
  (`SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs:79-83`, `SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs:19-25`).
- Every stored marker of the window, shown or hidden, is `MarkerSet.All`
  (`SemiPlot/SemiPlot.Core/Trends/MarkerSet.cs:17-18`); `ChartMarkers` exposes only `ShownMarkers`
  (`SemiPlot/SemiPlot.UI/Chart/ChartMarkers.cs:38`), and the chart view model owns it as `Markers`
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:93`). `StoredMarker.TimeUtc` is UTC and carries
  `Kind` and `Label` (`SemiPlot/SemiPlot.Core/Data/IMarkerStore.cs`).

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task**
- **CRITICAL: update this plan file when scope changes during implementation**

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` for the marker choice list, the choices and their release, the
  file name, the view model and the realised window (`CsvExportWindowTests`).
- no store change, so no container test.

## Acceptance Evidence

The behaviour does not exist today: the export window has no marker control and the suggested name is
always `semiplot_<start>-<end>.csv` (`CsvFileExporter.cs:19-25`). Each item is a test red before the
change and green after it.

1. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ExportMarkerChoice"`:
   for a date, the start list holds kinds 0 and 1 of that wall-clock day in time order; the end list
   holds kinds 0, 2 and 3 past the start bound; a marker of the neighbouring day is absent.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~CsvExportWindowTests"`:
   choosing a start and an end marker fills both edges, the form is valid, and the count and the
   export read that span; a hand edit or a date change returns the box to "No marker"; the dates and the
   marker boxes end 20 px from the window edge in both languages.
3. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ExportFileNameTests|FullyQualifiedName~CsvExportViewModelTests"`:
   a start box on a labelled marker proposes `RecipeName_2026-10-27_14-03.csv`; a refused character
   becomes `_`; "No marker", an unlabelled marker or an edited start falls back to
   `semiplot_<start>-<end>.csv`.
4. Manual, on the stand, after inserting a `kind = 1` marker labelled `Etch.csv` and a `kind = 2` marker
   ten minutes later: `File` -> `Export to CSV...`, the start box lists `Etch.csv`, the end box lists
   the end marker, the file dialog proposes `Etch_<date>_<HH-mm>.csv`; the window's height is measured
   for `ui-theme.md`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview

- The export snapshots every stored marker of the window, `ChartMarkers.All` (a new read-only
  pass-through of `MarkerSet.All`), when it opens, like it snapshots the pens. The `View` toggle does not
  filter it. No marker read is issued.
- `ExportMarkerChoice.ForStart` and `ForEnd` (new, `SemiPlot.UI/Export`) are the pure rule: given the snapshot, the zone, a
  wall-clock date and, for the end, the start bound, it returns the matching markers in time order as
  `ExportMarkerChoice` rows (the marker, its wall-clock time, the display text). The view model exposes
  `StartMarkers` and `EndMarkers`: the start list recomputed when the start date changes, the end list when
  the end date or the start bound changes.
- Each edge's choice is view-model state, `SelectedStartMarker` and `SelectedEndMarker`, bound to its
  box's `SelectedItem`. A marker chosen there writes the edge's date and time through
  `WallClockRangeForm.PlaceStart` and `PlaceEnd`, which assign both fields before they raise, so no
  observer reads a half-written edge. The start is the marker's wall-clock time floored to the second.
  The end is that time floored to the second plus 1 s, so the export's `t < @to`
  (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:290`) keeps every row of the marker's own
  second, a marker on a whole second included. Both write the date from the rounded value, so an end at
  23:59:59.5 moves to the next day rather than to 00:00:00 of the marker's day.
- A choice holds while its edge reads the value it wrote and its edge's list still offers it; otherwise
  the box returns to "No marker" and the typed time stays. So a hand edit of the time, a date change,
  a start moved past the chosen end and an end marker in the day's last second (its next date does not
  list it) all release the box. The release runs ahead of the lists' recomputation, so the box never
  holds a choice its new list lacks.
- Each edge is two lines in the form grid `Auto,*`: the label, centred on the date, and the
  `CalendarDatePicker` over the field column; under it a nested grid `110,*` at 8 px with the time box
  and the `ComboBox`, which ends at the date's right edge. The box lists `StartChoices` / `EndChoices`:
  "No marker" / "Без метки", then `StartMarkers` / `EndMarkers`. A marker item reads `HH:mm:ss` and the
  stored label, or "Unnamed" / "Без имени"; "No marker" reads in `AppSecondaryForegroundBrush`. The box
  is disabled while its list holds no marker, and the window stays 390 px.
- The marker lists are a snapshot taken when the window opens, like the pens: a marker stored while the
  window is open is absent until it is opened again.
- The end list compares wall-clock times, so in the repeated autumn hour an end marker of the second
  pass, earlier on the wall clock than a start of the first pass, is not listed, and a choice there exports
  rows of both passes (`docs/architecture/data-integration.md#time-boundary`). This holds one hour a
  year and is documented rather than handled.
- `ExportFileName.Suggest` (moved out of `CsvFileExporter`) takes the start box's marker label: a label
  names the file `<stem>_<yyyy-MM-dd_HH-mm>.csv` from the start. The stem is the label trimmed, with one
  trailing `.csv` removed (ordinal, ignoring case), so `Run 3.5` stays `Run 3.5`; then
  `\ / : * ? " < > |` and control characters become `_`, and the stem is cut to 200 characters (never
  inside a surrogate pair), so a long label keeps the name with its 21-character suffix under 255
  characters. Neither `Path.GetFileNameWithoutExtension` nor `Path.GetInvalidFileNameChars()` is used: the
  first cuts at any dot and strips a directory part, and the second differs on the Linux CI leg. "No
  marker", or a label the cut leaves blank (no label, only spaces, `.csv` alone), keeps the range name.

## Technical Details

- `ExportMarkerChoice(StoredMarker? Marker, DateTime WallClock, string Text)`; the "No marker" item has
  no marker (`IsNoMarker`), one per view model, its text read when the window opens.
- Day bucketing goes through the archive zone (`WallClockRangeForm.WallClockDay`), so a marker shown at
  00:30 local belongs to that local day.
- The end list keeps a marker whose floored second is after the start bound (Task 8; it was the end value,
  floored plus 1 s, which offered the start's own marker); without a readable start it holds every end-kind
  marker of the end date.
- The end filter and the release read the bounds, never `From`: `From` is null whenever the form is
  invalid (`SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs:54`), including an end not after the start,
  which is when the filter matters. `WallClockRangeForm.StartBound` and `EndBound` are
  `protected internal` so the view model reads them. The name reads `Form.From`, because the save dialog
  opens only on a valid range.
- `StartMarkers` and `EndMarkers` are `ObservableAsPropertyHelper`s, added to the view model's
  disposables: the start list over `Form.WhenAnyValue(form => form.StartDate)`, the end list over
  `Form.WhenAnyValue(form => form.EndDate, form => form.StartDate, form => form.StartTimeText)`.
  `StartChoices` and `EndChoices` are one `ReadOnlyObservableCollection` each, "No marker" at index 0,
  brought to the same lists in place (Task 9). `StartBound` raises no notification of its own, so nothing observes it directly.
  The release watches the four edge fields and is subscribed before the lists.
- New strings in `Resources.resx` / `Resources.ru.resx`: `ExportNoMarker` ("No marker" / "Без метки")
  and `ExportMarkerUnnamed` ("Unnamed" / "Без имени"). A disabled box needs no empty-list text.

## What Goes Where

- **Implementation Steps**: code, tests and docs in this repository.
- **Post-Completion**: the stand check.

## Implementation Steps

### Task 1: Snapshot the markers into the export

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartMarkers.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Modify: the tests that build `CsvExportViewModel` or the export factory

- [x] add `ChartMarkers.All`, a read-only pass-through of `MarkerSet.All`
- [x] pass a marker snapshot from `RequestExport` through the factory into `CsvExportViewModel`
- [x] write tests: the view model receives every stored marker, hidden recipe markers included
  - ⚠️ the view model keeps no `Markers` property, which had no production reader; the test reads the hidden recipe markers through `StartMarkers` and `EndMarkers`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 2

### Task 2: List a row's markers

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Export/ExportMarkerChoice.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/ExportMarkerChoiceTests.cs`

- [x] create `ExportMarkerChoice` and its `ForStart` and `ForEnd` with the start and end rules
- [x] expose `StartMarkers` and `EndMarkers` as OAPHs over the start date, start time and end date
- [x] add `PickStartCommand` and `PickEndCommand` that write date and time from the rounded value
- [x] add the kind words and the button tooltip in both resource files
- [x] write tests: kinds per row, day bucketing through the zone, time order, the end list past the start, an unreadable start, an end not after the start
- [x] write tests: a pick fills date and time; an end at 23:59:59.5 moves to the next day; an end on a whole second gets +1 s; an edit of the start date refreshes both lists
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 3

### Task 3: Add the marker buttons to the window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs` (only if a pick needs code-behind)
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`

- [x] add a button with a `MenuFlyout` bound to the row's list and pick command, in the picker's cell, disabled on an empty list
- [x] write tests: picking through the realised window fills both rows and the form is valid; picking the same marker again after a manual edit refills it; an empty day disables the button; the window width stays 390 px
- [x] write a test under the `ru` culture that the start picker's `PART_TextBox` shows `dd.MM.yyyy` whole (its extent fits its viewport)
  - ⚠️ the headless shaper advances every glyph by the font size (the date measures 140 px), so no layout passes an extent check; `UnderRussian_TheWidestDateInInter_FitsTheStartPickersTextViewportOnTheStand` measures the date masks and the labels in the shipped Inter font through Skia and corrects the realised layout by the label column's real width
  - ⚠️ that measurement showed the widest Russian date (79.6 px) over the 76 px the picker's text had, so the window style sets the picker's text box padding to 12, 0, 4, 0 px, which gives the text 84 px (`ui-theme.md#a-form-never-resizes-on-validation`)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 4

### Task 4: Propose the file name from the start marker

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/ExportFileNameTests.cs` (the current name rule is tested only through `CsvExportWindowTests`)

- [x] find the labelled kind 0 or 1 marker whose floored wall-clock moment equals the start bound
- [x] build `<stem>_<yyyy-MM-dd_HH-mm>.csv` with the fixed refused-character set, else keep the current name
- [x] move the existing `SuggestedFileName` test out of `CsvExportWindowTests` (`CsvExportWindowTests.cs:754`) into `ExportFileNameTests`
- [x] write tests: recipe and user marker labels, `RecipeName.CSV`, `Run 3.5`, `Lot/Etch.csv`, a refused character, an edited start, an unlabelled marker, a kind 1 and a kind 0 marker in one second
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 5

### Task 5: Verify acceptance criteria

- [x] verify every Overview item is implemented (marker buttons and lists, picks, kinds per row, end past the start, file name from a labelled start marker; item 4 of Acceptance Evidence is the manual stand check, left for Post-Completion)
- [x] run the three Acceptance Evidence commands (13, 39 and 56 tests passed)
- [x] run the full suite: `dotnet test SemiPlot.slnx` (Unit 2483 passed, Integration 211 passed, 0 failed)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files (both exit 0, 15 `.cs` files)

### Task 6: [Final] Update documentation

- [x] `docs/architecture/overview.md#the-export-window`: the marker buttons, the snapshot, the lists, the end rounding, the file name rule, the repeated-hour limit, and the tab-order sentence (`overview.md:831`)
- [x] `docs/architecture/ui-text.md#the-export-windows-text` and `ui-theme.md`: the new strings and the row layout
- [x] `readme.md`: the export by markers
- ⚠️ moving this plan to `docs/plans/completed/` is left for the delivery step

### ➕ Task 7: Rework each range edge into a date line and a time-and-marker line

The chevron beside the calendar icon reads as nothing and the row is cramped. Each edge becomes a
two-line block in the 390 px window:

```
  Начало  [ 28.10.2026                       📅 ]
          [ 18:16:01 ]  [ 18:16:01  Etch.csv     ▾ ]

  Конец   [ 28.10.2026                       📅 ]
          [ 18:17:26 ]  [ Без метки              ▾ ]
```

- Form grid `ColumnDefinitions="Auto,*"`, `ColumnSpacing="14"`. Line one: the edge label, centred on the
  date, and a `CalendarDatePicker` stretched over the field column. Line two: a nested grid
  `ColumnDefinitions="110,*"`, `ColumnSpacing="8"`, the time box and a `ComboBox` ending at the date's
  right edge (370 px, with the pen list and `Закрыть`). `RowSpacing="10"` inside an edge; the end label
  and date get `Margin="0,10,0,0"`, 20 px between edges. No separator. Controls keep Semi's 32 px.
- The `ComboBox` lists a first item "Без метки" / "No marker", then the row date's markers in time order.
  Every marker item reads `HH:mm:ss` and the stored label as is; an unlabelled marker reads `HH:mm:ss`
  and "Без имени" / "Unnamed". No kind word. Closed, the box shows the same text; "Без метки" in
  `AppSecondaryForegroundBrush`. With no marker on the date the box is disabled on "Без метки".
- The selection is explicit view-model state bound with `SelectedItem`: choosing a marker writes date
  and time as the pick commands do now (start floored, end floored plus 1 s); a hand edit of that edge's
  time or a change of its date resets it to "Без метки". The edges are independent: a marker on one edge
  and a typed time on the other is valid, with no message.
- The suggested file name follows the start box's selection: a selected labelled start marker names the
  file; "Без метки" or an unlabelled marker keeps the range name.
- Remove the `DropDownButton.marker` style, the `MenuFlyout`s and their `$parent[...]` bindings, the
  `ExportPickMarker` tooltip, the kind words if nothing else reads them, and the `PART_TextBox`
  `Padding="12,0,4,0"` setter (keep `TimeFieldInput.Layout="Date"`).

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`, `CsvExportViewModel.cs`, `ExportMarkerChoice.cs`, `ExportFileName.cs`, `ExportRangeForm.cs`
- Modify: `SemiPlot/SemiPlot.UI/Navigation/WallClockRangeForm.cs` (`PlaceStart` and `PlaceEnd` write an edge as one edit)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: the export tests (`CsvExportWindowTests`, `ExportMarkerChoiceTests`, `ExportFileNameTests`, `CsvExportViewModelTests`)
- Modify: `docs/architecture/overview.md#the-export-window`, `ui-text.md`, `ui-theme.md`, `readme.md`

- [x] rebuild the range grid as two lines per edge with the numbers above, and drop the chevron, its style and the date padding override
- [x] replace the pick menus with two `ComboBox`es over the start and end lists with a leading "no marker" item and explicit selection state that writes date and time and resets on a hand edit or a date change
- [x] drive the suggested file name from the start selection
- [x] item text: `HH:mm:ss` plus the stored label, or plus "Без имени" / "Unnamed"; update both resource files and drop the unused strings
- [x] write tests: selecting a marker fills its edge; a hand time edit and a date change reset the box; the empty day disables it; one edge on a marker and one typed is valid; the file name follows the start selection; the item text for labelled and unlabelled markers
- [x] write tests: the date and the marker box end at the same right edge as the pen list under ru and en; the Russian date-fit test checks the widened date cell
- [x] update the docs and the size table in `ui-theme.md` (the window height is an estimate until the stand measures it)
- [x] run `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` - must pass

### ➕ Task 8: Fix the marker boxes found on the stand

Three defects seen on the stand after Task 7:

- The end list offers the marker chosen as the start. It keeps a marker whose end value (floored second
  plus 1 s) is after the start bound, so the start marker itself passes and yields a one-second range.
  Only kind 0 shows it, because the start and end lists share no other kind. The end list keeps a marker
  only when its floored second is strictly after the start bound; the start's own marker and any marker
  of the same second leave the end list, with no reference to the other box's selection.
- Choosing in one box blanks the other box instead of showing "Без метки". The end list depends on the
  start time, so a start choice rebuilds it; the `ComboBox` writes `null` on the new `ItemsSource`, the
  setter maps `null` to "no marker", sees no change and returns without a notification, and the box stays
  empty. Raising the property on that `null` does not reach the box: the binding compares the raised
  value with the one it last delivered and drops it. Task 9 removes the replacement instead.
- The closed box repeats the time the time box beside it already shows. The closed box shows the label
  only (or "Без имени" / "Unnamed"; "Без метки" in the secondary brush) through
  `SelectionBoxItemTemplate`; the open list keeps `HH:mm:ss` and the label.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/ExportMarkerChoice.cs`, `CsvExportViewModel.cs`, `CsvExportWindow.axaml`
- Modify: the export tests
- Modify: `docs/architecture/overview.md#the-export-window`, `ui-text.md`, `ui-theme.md` where they describe the end list or the box text

- [x] keep an end marker only when its floored second is after the start bound
- [x] raise the selection property on a `null` from the box (superseded by Task 9: a `null` keeps the choice)
- [x] show the label alone in the closed box through `SelectionBoxItemTemplate`, keep time and label in the open list
- [x] write tests: a kind 0 start marker is absent from the end list; in the realised window a choice in one box leaves the other on "Без метки" (both directions); the closed box shows the label alone and the open list the time and label
  - ⚠️ the realised-window tests pass before the fix as well: headless Avalonia 12.0.5 restores the selection through `InternalSelectionModel.SetSource`, which re-selects the items of `WritableSelectedItems`; a selection that never reached that list (the init path) is not restored, which is the likely Win32 split. Task 9 removes the replacement that needs it
- [x] update the docs
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` - must pass

### ➕ Task 9: Keep the marker box selection across list updates

A box keeps its selection when its list changes, and only a stale edge releases a choice.

- `StartChoices` and `EndChoices` are one `ReadOnlyObservableCollection` each, created with the view
  model with "No marker" at index 0 and brought to `StartMarkers` / `EndMarkers` in place by `RemoveAt`
  and `Insert` on the UI thread; nothing clears or replaces them, so the `ComboBox` never sees a new
  `ItemsSource` or a Reset.
- A `null` from a box keeps the choice. `ReleaseStaleChoices` is the one path that drops a choice, and it
  runs ahead of the lists, so a chosen item never leaves its list while chosen.
- An end marker of its day's last second (`ExportMarkerChoice.EndLeavesItsDate`) is placed one tick later
  on the UI scheduler, after the box's pick returns: a release raised inside the pick leaves the closed
  box on the marker's label, and `AvaloniaScheduler` runs a zero due time inline.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`, `ExportMarkerChoice.cs`
- Modify: `SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`, `ExportMarkerChoiceTests.cs`
- Modify: `docs/architecture/overview.md#the-export-window`

- [x] keep one collection per box, updated in place with no Reset, "No marker" fixed at index 0
- [x] keep the choice on a `null` from the box; leave the release to `ReleaseStaleChoices`
- [x] place a last-second end marker after the box's pick returns
- [x] write tests: a `null` keeps either edge's choice; an end marker held while a start marker is chosen stays chosen in the view model and in the realised box; the lists are the same instances after date, time and marker changes and change only by Add and Remove; both boxes read "No marker" in `SelectedItem`, `SelectionBoxItem` and the first container after a choice in the other box; the last-second end marker leaves the closed box on "No marker" under the `TestScheduler` and under `AvaloniaScheduler.Instance`
  - ⚠️ no realised setup reproduces the stand's empty box on the Task 8 code (a choice before the window binds, the data context set after realisation, an end marker held across a start pick): headless restores the selection each time. The view-model tests carry the rule and fail before the fix
- [x] update the docs
- [x] run `dotnet build SemiPlot.slnx`, `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` - must pass

## Post-Completion

**Manual verification**:
- the stand check in Acceptance Evidence, item 4.

**Closes #160** together with #161.

**Executed by exec:**
- branch: export-range-by-markers

## Verify it yourself

Automated, from the repository root on `export-range-by-markers` (the tests do not exist at `f3a0772`,
the plan commit; each passes at `4737a15`):

1. Lists, rounding, the end rule and the box state:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ExportMarkerChoice|FullyQualifiedName~CsvExportViewModelTests"`.
2. The realised window (layout, choices, both-direction "Без метки", the last-second end):
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~CsvExportWindowTests"`.
3. The file name: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ExportFileNameTests"`.

Manual, on the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`). As `postgres` insert
`INSERT INTO semiplot_markers (t, label, kind, locked) VALUES (now() - interval '10 minutes', 'Etch.csv', 1, true), (now(), NULL, 2, true);`
and add one user marker by double click; then open `Файл` -> `Экспорт в CSV...` with the chart on today.

1. Each edge is two lines: the date on its own line, the time box and the marker box under it; nothing
   is clipped, the Russian date reads whole.
2. The start box opens with "Без метки", then `HH:mm:ss Etch.csv` and the user marker; the end box offers
   the unnamed end marker as `HH:mm:ss Без имени` and the user marker, never the start's own marker.
3. Closed, a box shows the label alone (`Etch.csv`, `Без имени`, `#1`); "Без метки" is grey.
4. Choose the start, then the end, then the start again: neither box goes blank, the box not touched keeps
   its marker or "Без метки". This is the defect the headless tests could not reproduce.
5. Type a time by hand: that box returns to "Без метки"; change a date: that edge's box returns to it.
6. `Сохранить как...` with the start on `Etch.csv` proposes `Etch_<yyyy-MM-dd>_<HH-mm>.csv`; with the start
   on "Без метки" it proposes `semiplot_<start>-<end>.csv`.
7. Measure the window height and write it into the size table in `docs/architecture/ui-theme.md` (now an
   estimate, 609 px).
