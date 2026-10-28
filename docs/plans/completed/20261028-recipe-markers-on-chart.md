# Recipe markers on the chart

## Overview

- The chart reads two new `semiplot_markers` columns, `kind` and `locked`, which SemiBase v0.7.0 ships
  (Semiteq/SemiBase#21). The SCADA inserts recipe markers (kind 1 start, 2 end, 3 interrupted) with
  `locked = true`; the viewer's own markers are kind 0.
- A locked marker ignores the two gestures that change a marker by accident, the drag and the double
  click that deletes; the marker menu still renames and deletes it and gains `Lock` / `Unlock`. The
  database does not enforce `locked`; the viewer's gestures honour it.
- A recipe marker draws in its own palette colour on the chart; its minimap tick keeps the one marker
  colour (`docs/architecture/ui-theme.md`). A locked line draws solid instead of dashed. A
  checkable `View` item hides the recipe markers in one window.
- The lock guards against gestures in the window that shows it. The move and delete statements do not
  check `locked`: a lock another window stores while this window drags the marker is held off by the
  drag (`MarkerSet.cs:31-34`), and the drop overwrites the moment. The statements stay unguarded,
  because a guarded write that touches no row maps to `MarkerGone` and would remove a live marker.
- SemiBase v0.7.0 is the minimum. Against an older database every marker read fails with `42703`,
  `ShapeUnexpected` (`ArchiveExceptionMapper.cs:116`), not the `TableMissing` that
  `docs/architecture/postgres-instance.md:33-34` describes for a database without the table.
- This is the first of two PRs for #160. The second, picking an export range by markers and the CSV
  file name, is out of scope here.

## Context (from discovery)

- `StoredMarker(long Id, DateTime TimeUtc, string? Label)` is the one marker type
  (`SemiPlot.Core/Data/IMarkerStore.cs:20`); `IMarkerStore` has read, add, move, rename and delete
  (`IMarkerStore.cs:6-17`).
- The read is `SELECT id, t, label FROM semiplot_markers ORDER BY t, id`
  (`SemiPlot.DataSource.Postgres/ArchiveStatements.cs:143`), its column order is `StoredMarkerColumn`
  (`ArchiveStatements.cs:33-38`), and `PostgresMarkerStore.ReadAsync` maps a row
  (`PostgresMarkerStore.cs:47-53`). Move and rename share `UpdateOneRowAsync`, which returns
  `MarkerGone` when no row is touched (`PostgresMarkerStore.cs:150-172`).
- `Chart/IMarkerChannel` mirrors the four writes (`IMarkerChannel.cs:13-19`); `Markers/MarkerSync`
  implements it through one `ArchiveCallQueue` (`MarkerSync.cs:28`, `MarkerSync.cs:65-75`).
- `Chart/ChartMarkers` holds the `MarkerSet`, runs each write with hold and revert
  (`ChartMarkers.cs:107-118`, `ChartMarkers.cs:187-212`) and numbers new labels from every shown
  marker (`ChartMarkers.cs:153-165`). Its `Markers` and `Changed` feed the lines
  (`TrendChartView.axaml.cs:371-385`), the readings (`TrendChartView.axaml.cs:390-392`) and the minimap
  ticks (`MinimapViewModel.cs:53`, `MinimapViewModel.cs:308-325`). The hit test is
  `ChartMarkers.NearestWithin` over `MarkerSet.NearestWithin` (`ChartMarkers.cs:45-48`,
  `MarkerSet.cs:116-150`).
- The press router maps a marker press to `DragMarker` and a double click to `DeleteMarker`
  (`ChartPressRouter.cs:56-60`); the view takes the region from `PressRegionAt`
  (`TrendChartView.axaml.cs:548-562`) and the hover cursor from the same route
  (`TrendChartView.axaml.cs:647-654`).
- The marker menu is a `MenuFlyout` with `Rename` and `Delete` (`TrendChartView.axaml:22-27`);
  `OpenChartMenu` stores the marker under the pointer in `_menuMarker`
  (`TrendChartView.Markers.cs:18-29`).
- `ChartMarkerLines.AddLine` draws every line dashed (`ChartMarkerLines.cs:166`) and paints it from
  `MarkerPalette(Line, Label, ReadingBackground)` (`MarkerPalette.cs:5`, `ChartMarkerLines.cs:154-158`),
  built from `AppMarkerLineBrush` and `AppMarkerLabelBrush` (`ChartPalette.cs:21`, `ChartPalette.cs:47-53`,
  `Styles/Palette.axaml:86-87`, `Styles/Palette.axaml:171-172`).
- The `View` menu's checkable items bind `IsChecked` `Mode=OneWay` and write through a command
  (`MainWindow/AppMenuBar.axaml:40-54`, `MainWindowViewModel.cs:87-88`); `MainWindowViewModel` holds the
  chart as `ChartViewModel` (`MainWindowViewModel.cs:62`).
- `docs/architecture/postgres-instance.md:49` lists the `semiplot` role's privileges; the grant
  `scada_writer` now holds on `semiplot_markers` is not stated anywhere in this repository.

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` for the Core set, the router, `ChartMarkers`, `MarkerSync`, the
  lines, the menus and the view model.
- **integration tests**: `SemiPlot.Tests.Integration/MarkerStoreTests.cs` for the new columns and the
  lock write, against the `ghcr.io/semiteq/semibase:latest` image, which is v0.7.0.
- no e2e suite beyond the headless view tests.

## Acceptance Evidence

The behaviour does not exist today: `StoredMarker` carries no kind or lock, and a double click on any
marker deletes it (`ChartPressRouter.cs:59`). Each item below is a test that is red before the change
and green after it.

1. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartPressRouterTests"`:
   a press on a locked marker routes to `PanOrPickPen` for one click and for two.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartMarkersTests"`:
   `ToggleLockAsync` shows the new state at once and restores it on failure; hiding recipe markers
   leaves only kind 0 in `ShownMarkers` and out of `NearestWithin`; numbering ignores a recipe marker's `#N`
   label.
3. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~MarkerStoreTests"`:
   a row inserted with `kind = 1, locked = true` reads back with both; `SetLockedAsync` writes that
   row only; a lock write to a deleted marker is `MarkerGone`.
4. Manual, on the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), after
   `INSERT INTO semiplot_markers (t, label, kind, locked) VALUES (now(), 'Etch.csv', 1, true)` as
   `postgres`:
   1. The marker draws solid in the recipe colour.
   2. A drag on it pans the chart; a double click on it does nothing.
   3. The marker menu shows `Unlock`; after it the line draws dashed and drags.
   4. `View` -> `Recipe markers` unticked removes the line and its minimap tick; ticked brings them back.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep plan in sync with actual work done

## Solution Overview

- `StoredMarker` gains `Kind` (a Core enum `MarkerKind`: `User = 0`, `RecipeStart = 1`,
  `RecipeEnd = 2`, `RecipeInterrupted = 3`) and `IsLocked`, as trailing parameters with the defaults
  `MarkerKind.User` and `false`, so a marker the viewer adds is what the database defaults store.
- One new write, `SetLockedAsync(StoredMarker marker, bool isLocked)`, runs the same path as rename
  through store statement, `IMarkerStore`, `IMarkerChannel` and `MarkerSync`; `ChartMarkers` exposes it
  as `ToggleLockAsync(StoredMarker seen)`, with hold and revert. No other write changes: add still
  inserts `(t, label)`.
- The lock is a press region, not a new action: `PressRegionAt` returns `ChartPressRegion.LockedMarker`
  for a locked marker, and the router maps it to `PanOrPickPen` for any click count. The hover cursor
  follows from the same route. The menu still finds the marker, because `MarkerAt` is unchanged.
- Hiding is a filter inside `MarkerSet`, which Core owns together with `MarkerKind`. The set keeps every
  marker in `All` and a cached `ShownMarkers` array of the shown ones, rebuilt only when the set changes or
  the flag flips; `NearestWithin` searches the cached array. `ShownMarkers` is read every frame, every pointer
  move and every drag move (`TrendChartView.axaml.cs:392`, `TrendChartView.axaml.cs:575`), so it never
  filters per call. The lines, the readings, the minimap ticks and the hit test follow `ShownMarkers` without
  a filter of their own. The id lookups (`ChartMarkers.FindMarker`, `ChartMarkers.cs:239-242`, behind
  `ReplaceHeld` and `RemoveMarker`), `NextNumber` and the `Contains` in `AddAtAsync` read `All`, so a
  revert or a `MarkerGone` on a marker hidden after its write began still lands.
- The flag lives in `MarkerSet.ShowsRecipeMarkers`; `ChartMarkers` exposes it read-only and flips it
  through `ToggleRecipeMarkers`, which raises `Changed`. `MainWindowViewModel.ToggleRecipeMarkersCommand`
  calls it, and `ShowsRecipeMarkers`, the property the menu item reads, is an
  `ObservableAsPropertyHelper` over `Markers.Changed`: `ChartMarkers` raises no property notification, so
  the view model mirrors it as an adapter.
- `MarkerPalette` gains the recipe line colour from a new `AppRecipeMarkerLineBrush` key in both theme
  variants. `ChartMarkerLines.Sync` sets the colour from the kind and the pattern from the lock on every
  line it touches, since both can change between reads.

## Technical Details

- Statement: `SELECT id, t, label, kind, locked FROM semiplot_markers ORDER BY t, id`;
  `StoredMarkerColumn` gains `Kind = 3` and `Locked = 4`; `kind` reads with `GetInt16` and casts to
  `MarkerKind`. The column's `CHECK (kind BETWEEN 0 AND 3)` bounds it; a value outside the enum is
  `ShapeUnexpected`, not a crash. ASSUMPTION: the mapper has no existing path for an out-of-range
  value; the implementer adds the guard in `ReadAsync` and maps it there.
- Lock statement: `UPDATE semiplot_markers SET locked = @locked WHERE id = @id`, through
  `UpdateOneRowAsync`, so no touched row is `MarkerGone` naming the marker.
- `MarkerSet` keeps one binary search, over the shown array.
- `ChartMarkers.NextNumber` counts only `MarkerKind.User` markers of `All`.
- `ChartMarkerLines.ApplyPalette` recolours every line by its marker's kind, so it keeps the kind of each
  line it drew (`ChartMarkerLines.cs:89-99` iterates the lines alone today).
- Menu: one `MenuItem` named `MarkerMenuLock`; `OpenChartMenu` sets its header to `Lock` or `Unlock`
  from `_menuMarker.IsLocked` before showing the flyout. Two resource keys, `MarkerMenuLock` and
  `MarkerMenuUnlock`, in `Resources.resx` and `Resources.ru.resx` ("Закрепить", "Открепить"). The
  `View` item is `MenuViewRecipeMarkers`, "Recipe markers" / "Метки рецептов", ticked by default.
- The flag is per window and not stored; a new window starts with recipe markers shown.

## What Goes Where

- **Implementation Steps**: code, tests and docs in this repository.
- **Post-Completion**: the stand check and the SCADA script, which live outside it.

## Implementation Steps

### Task 1: Read kind and lock from the store

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IMarkerStore.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresMarkerStore.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/MarkerStoreTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Markers/FakeMarkerWriter.cs`

- [x] confirm the test image carries the columns: run the marker store tests once against `ghcr.io/semiteq/semibase:latest` after `docker pull`, and stop with ⚠️ if `kind` is missing (⚠️ Docker daemon down and starting it was denied: confirmed instead from SemiBase v0.7.0 `sql/semiplot_markers.sql`, which declares `kind smallint NOT NULL DEFAULT 0 CHECK (kind BETWEEN 0 AND 3)` and `locked boolean NOT NULL DEFAULT false`; the container tests ran later with Docker up against v0.7.0 and passed, MarkerStoreTests 25/25)
- [x] add `MarkerKind` beside `StoredMarker` and the two trailing parameters to the record
- [x] select `kind` and `locked` in `StoredMarkers`, extend `StoredMarkerColumn`, map both in `ReadAsync`
- [x] add `SetLockedAsync` to `IMarkerStore` and `PostgresMarkerStore` with the `SetMarkerLocked` statement
- [x] add the lock write to `FakeMarkerWriter`, which `FakeMarkerStore` implements `IMarkerStore` through (`FakeMarkerStore.cs:11`), so the unit project still builds
- [x] write integration tests: a raw row with `kind = 1, locked = true` reads back with both; an added marker reads `User`, unlocked
- [x] write integration tests: `SetLockedAsync` writes that row only; add a `Lock` arm to the deleted-marker theory (`MarkerStoreTests.cs:94`) and the dropped-table theory (`MarkerStoreTests.cs:196`)
- [x] run `dotnet test SemiPlot.slnx` - must pass before task 2 (unit project passed, 2405 tests; the integration project did not run then for lack of a Docker daemon; the container tests ran and passed in task 7, 210/210)

### Task 2: Carry the lock write through the channel

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/IMarkerChannel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Markers/MarkerSync.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartMarkers.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Markers/MarkerSyncTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkersTests.cs`

- [x] add `SetLockedAsync` to `IMarkerChannel` and route it through `MarkerSync`'s queue like rename
- [x] ➕ add the lock write to `SemiPlot.Tests.Integration/Journeys/NoMarkers.cs`, which implements `IMarkerChannel`, so the integration project still builds
- [x] add `ChartMarkers.ToggleLockAsync`: hold, show the new state, revert on failure, remove on `MarkerGone`
- [x] make `NextNumber` count only `MarkerKind.User` markers
- [x] write tests: `MarkerSync` queues the lock write and reads after it
- [x] write tests: `ChartMarkers` shows the lock at once, reverts it on failure, and numbers past a user `#3` but not a recipe `#9`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 3

### Task 3: Make a locked marker ignore drag and double click

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPressRouterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`

- [x] add `ChartPressRegion.LockedMarker`, routed to `PanOrPickPen` for every click count after the axis region, Ctrl, the time axis and the tool
- [x] return it from `PressRegionAt` and use it in `Hover`, so a locked marker shows the hand cursor
- [x] write router tests: one and two clicks on a locked marker; Ctrl and delta mode keep their branches
- [x] write view tests: a double click on a locked marker leaves it stored; a drag on it pans; a hover on it shows the hand cursor and still snaps the cursor to its moment
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 4

### Task 4: Add Lock and Unlock to the marker menu

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.Markers.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`

- [x] add `MarkerMenuLock` to `MarkerMenuFlyout` with a `Click` handler calling `ToggleLockAsync(marker)`
- [x] set its header from `_menuMarker.IsLocked` in `OpenChartMenu`
- [x] add `MarkerMenuLock` and `MarkerMenuUnlock` in both resource files
- [x] write view tests: the menu over an unlocked marker reads `Lock` and locks it; over a locked one reads `Unlock` and unlocks it
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 5

### Task 5: Draw kind and lock on the line

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/MarkerPalette.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartMarkerLines.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`

- [x] add `AppRecipeMarkerLineBrush` to both theme dictionaries and `RecipeLine` to `MarkerPalette`
- [x] in `Sync`, set each line's colour from its kind and its pattern from its lock; `ApplyPalette` recolours by kind
- [x] write tests: a recipe line takes the recipe colour, a user line the marker colour; a locked line is solid, an unlocked one dashed; a lock change between two syncs changes the pattern
- [x] write tests: a theme change keeps the recipe colour on a recipe line
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 6

### Task 6: Hide recipe markers per window

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/MarkerSet.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartMarkers.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MarkerSetTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkersTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`

- [x] give `MarkerSet` `All`, the cached `ShownMarkers` and the flag; `NearestWithin` searches the cached array
- [x] give `ChartMarkers` `ShowsRecipeMarkers` and `ToggleRecipeMarkers`, raising `Changed`; point `FindMarker`, `NextNumber` and `AddAtAsync`'s `Contains` at `All`
- [x] add `ToggleRecipeMarkersCommand` and the `ShowsRecipeMarkers` OAPH to `MainWindowViewModel`, and the `ViewRecipeMarkers` item after `ViewMinimap`
- [x] write tests: hidden recipe markers leave `ShownMarkers` and `NearestWithin`, stay in `All`, raise one `Changed`, and come back on show; `ShownMarkers` returns the same array between two changes
- [x] write tests: a rename of a recipe marker that fails after the marker was hidden restores the label in `All`
- [x] write tests: the menu item starts ticked and its command flips the flag and the tick; add a `ViewRecipeMarkers` arm to the switch at `AppMenuBarTests.cs:103-108`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 7

### Task 7: Verify acceptance criteria

- [x] verify every Overview item is implemented
- [x] run the three Acceptance Evidence commands (ChartPressRouterTests 43/43, ChartMarkersTests 30/30, MarkerStoreTests 25/25; the manual stand check, item 4, is not automatable and stays in Post-Completion)
- [x] run the full suite: `dotnet test SemiPlot.slnx` (unit 2437/2437, integration 210/210 with Docker up)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files (both exit 0, 27 files)

### Task 8: [Final] Update documentation

- [x] `docs/architecture/data-integration.md#the-marker-store`: the two columns, `MarkerKind`, the lock statement, five writes
- [x] `docs/architecture/postgres-instance.md`: the minimum becomes SemiBase v0.7.0 with the `ShapeUnexpected` failure (lines 32-35); the table row at line 98 names `kind` and `locked`; `scada_writer` holds `INSERT` on `semiplot_markers` and nothing else on it
- [x] `AGENTS.md`: the left-button gesture paragraph names the locked-marker region, and "four writes" becomes five wherever the markers are described
- [x] `docs/architecture/trend-interaction.md#the-time-markers`: the lock, the recipe colour, the solid line, the hiding
- [x] `docs/architecture/overview.md#the-live-markers`, `ui-text.md#the-marker-menu-and-label-editors-text`, `ui-theme.md`: the five writes, the new strings, the new palette key
- ⚠️ move this plan to `docs/plans/completed/` (left for the delivery step, not done here)

## Post-Completion

**Manual verification**:
- the stand check in Acceptance Evidence, item 4, in both themes.

**External system updates**:
- the SCADA project script issues `INSERT INTO semiplot_markers (t, label, kind, locked) VALUES
  (clock_timestamp(), NULLIF(left('<file>', 80), ''), <kind>, true)` as `scada_writer`
  (Semiteq/SemiBase#21).

**Remaining work for #160**: the second PR, picking the export range by markers and proposing the CSV
file name from the start marker's label.

**Executed by exec:**
- branch: recipe-markers-on-chart

## Verify it yourself

Automated, each from the repository root on `recipe-markers-on-chart`. The tests do not exist at
`9370cec` (the plan commit), where `StoredMarker` has no `Kind` or `IsLocked`, so there is no red run to
compare; each passes at `fb69271`.

1. Locked-marker routing: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartPressRouterTests"`.
2. Lock write, hiding, numbering, the lock menu, the line colour and pattern:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartMarkersTests|FullyQualifiedName~MarkerSetTests|FullyQualifiedName~ChartMarkerViewTests|FullyQualifiedName~MinimapViewModelTests"`.
3. The store against SemiBase v0.7.0, Docker up:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~MarkerStoreTests"` (26 tests).

Manual, on the stand: `dotnet run --project SemiPlot/SemiPlot.AppHost`, then as `postgres` in the
stand's database run
`INSERT INTO semiplot_markers (t, label, kind, locked) VALUES (now(), 'Etch.csv', 1, true);` and wait up to
5 s for the read.

1. The marker draws as a solid purple line (`#834DF0` light, `#A571E6` dark) labelled `Etch.csv`; a
   marker added by double click draws grey and dashed.
2. A press and drag on the purple line pans the chart and leaves the marker in place; a double click on
   it does nothing, and the pointer shows the hand, not the resize cursor.
3. Right click on it: the menu reads `Unlock` (`Открепить`). After it the line turns dashed, still
   purple, and now drags; the menu then reads `Lock`.
4. `Rename` and `Delete` from the menu work on a locked marker.
5. `View` -> `Recipe markers` unticked removes the purple line and its minimap tick and keeps the grey
   one; ticked brings both back. A second window keeps its own tick state.
6. Switch the theme: the purple line keeps the recipe colour of the new theme.
