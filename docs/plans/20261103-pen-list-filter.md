# Pen editor: the top bar's filter box over the pen list

## Overview

- The pen editor's top bar holds the added count and `Refresh pen list`, right-aligned; its left side is
  empty (`PenEditorWindow.axaml:96-112`, `overview.md:525`). With a few hundred pens the operator finds a pen
  only by sorting and scrolling.
- The top bar gains a filter box on its left, 280 px wide, with the placeholder "Filter: id, name or group" /
  «Фильтр: номер, имя или группа». The pen list shows the rows whose id, name or group text contains the
  typed text, case-insensitive. An empty box shows every row.
- The selection stays while the selected row still matches. When a filter change hides the selected row,
  the selection clears and the pen pane shows its hint.
- The groups pane is not filtered: it lists every group, and its membership boxes follow the selected pen.
- Scope guard: no sort change, no filter of the groups pane, no saved filter text. The filter text lives for
  the window's life and is not written anywhere.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice pen-list-filter

The slice has no issue (`20261102-visual-chore-roadmap.md:228`), so the PR body closes none. The roadmap's
target end state puts the filter on the left of the top bar (`:48`); the slice's scope and guard are
`:224-234`.

Stacking: this branch, `pen-list-filter`, starts at `538a79d`, the tip of `pen-editor-groups-pane`
(verified 2026-11-03: `git rev-parse HEAD pen-editor-groups-pane` prints the same hash), which stacks on
`pen-editor-pens-pane` and `hex-color-input`; none is merged.

- View model (`SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`):
  - The rows live in one private `ObservableCollection<PenRowViewModel> _rows` (`:32`), and `PenRows` is a
    `DataGridCollectionView` over it, built once in the constructor (`:49-51`, `:67-68`).
  - `Groups = GroupsOf(rows, catalogue.Groups)` takes the row array, not `PenRows` (`:52`, `:188-198`), so
    the groups pane already sees every row whatever the view shows.
  - `SelectedRow` is a plain notifying property (`:70-75`); `SelectedForm` is built from it through
    `DistinctUntilChanged` and `Switch`, which disposes the old form (`:54-59`, `:120-131`).
  - `RefreshAsync` clears and refills `_rows` with no `DeferRefresh` (`:158-162`), then restores the
    selection by pen id (`:154`, `:165`) and builds the new groups pane (`:166`).
- `SemiPlot/SemiPlot.UI/PenEditor/PenRowOrder.cs`: the static comparers the grid sorts by; text compares
  through `StringComparer.CurrentCultureIgnoreCase` (`:36`).
- `SemiPlot/SemiPlot.UI/PenEditor/PenRowViewModel.cs`: `Pen` (`:19-23`), `GroupsText` (`:32-37`) joined with
  ", " (`:15`) in the groups' order (`:97-102`), rewritten by `ShowGroupsOf` after a group write (`:69-73`);
  `Pen` changes after a successful write (`:48-51`).
- `StoredPen` has `int Id` and a non-null `string Name` (`SemiPlot.Core/Data/PenCatalogue.cs:57-60`).
- Window (`SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`):
  - The root grid `Margin="16"`, `RowDefinitions="Auto,*"`, `RowSpacing="16"` (`:92-94`); the top bar
    `PenEditorTopBar`, `ColumnDefinitions="*,Auto,Auto"`, `ColumnSpacing="8"`, with `PenEditorAddedCount` in
    column 1 and `PenEditorRefreshButton` in column 2 (`:96-112`).
  - The pen list `PenList`: `ItemsSource="{Binding PenRows}"`, `SelectedItem="{Binding SelectedRow,
    Mode=OneWay}"`, `SelectionChanged="OnPenListSelectionChanged"` (`:123-126`). Its cells show `Pen.Id`
    (`:144-148`), `Pen.Name` (`:177-181`) and `GroupsText` (`:227-232`).
  - The pen pane's hint `PenFormEmptyHint` shows while `SelectedForm` is null (`:253-258`).
  - `NewGroupName` is a `TextBox` with a plain `Text` binding and no `Field` (`:529-532`): the window's one
    existing text box outside the edit-end routing.
- Code-behind (`PenEditorWindow.axaml.cs`):
  - The width constants `PenPaneWidth`, `GroupsPaneWidth`, `DesignedWidth`, `MinimumWidth` (`:22-29`) and the
    height constants (`:31-34`).
  - The edit-end routing: `OnFieldGotFocus` records an edit only for a `TextBox` whose `DataContext` is a
    `PenFormViewModel` and that carries a `Field`, or for `GroupRename` (`:168-178`); `OnFieldLostFocus` and
    `OnFieldKeyDown` act only on a box for which `EndsItsEditOnItsOwn` is true, which is a box with a `Field`
    or `GroupRename` (`:180-217`, `:358-362`); the Enter branch for the scale pair checks that the box sits
    inside `FormScale` (`:208`). A box with neither takes no part.
  - The tunnel press handler clears the focus only on a press outside every focusable control (`:152-165`);
    a `TextBox` is focusable.
  - `OnPenListSelectionChanged` writes every selection change, null included, to `SelectedRow` (`:239-245`).
- `Avalonia.Controls.DataGrid` 12.0.1 (`SemiPlot/Directory.Packages.props:10`, referenced at `SemiPlot.UI.csproj:23`): its XML
  documentation describes `DataGridCollectionView.Filter` as the consumer's callback that decides whether an
  item is in the view (`Avalonia.Controls.DataGrid.xml:3441-3452`), `Count` as the number of records after
  filtering and sorting (`:3407-3410`), `PassesFilter` (`:3798-3801`), and `Refresh()` as "Re-create the view,
  using any SortDescriptions and/or Filters" (`:3809-3812`). `ProcessAddEvent` exists (`:4050`).
  Decompiled 12.0.1 (verified 2026-11-03): an added row enters the view only if it passes the filter, at its
  sorted place (`ProcessCollectionChanged`, `ProcessInsertToCollection`), and the view subscribes to no row's
  `PropertyChanged`.
- The sort is a view refresh that keeps the selection: `PenListViewTests.AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm`
  asserts the same `SelectedItem`, `SelectedRow` and `SelectedForm` instances after each header click
  (`PenListViewTests.cs:123-157`), with the code-behind writing every change null included;
  `overview.md:553-554` states "A sort raises no selection change in Avalonia 12.0.1".
- Resources: `PenEditorColumnId` is "Id" / "Номер" (`Resources.resx:228-229`, `Resources.ru.resx:228-229`);
  `PenEditorRefresh` (`Resources.resx:216-218`), `PenGroupsNewNamePlaceholder` (`:720-722`), the precedent for
  a `TextBox` placeholder key.
- Layout facts: the top bar is 32 px in the `MinHeight` arithmetic, and the groups pane's new-group row,
  which holds the `TextBox` `NewGroupName`, is 32 px in the same sum (`ui-theme.md:445-446`). At `MinWidth` the
  top bar has 1240 px; the count needs at most 294 px and the button 334 px in Russian (`ui-theme.md:601-604`).
  `TextBoxPlaceholderForeground` names the new-group field as its only consumer (`ui-theme.md:43`).
- Tests:
  - `PenEditorLayoutViewTests.cs`: `TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide` (`:42-69`)
    and `TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart` (`:71-98`), which
    asserts the button's right edge on the groups pane's and the count 8 px left of it.
  - `PenEditorViewModelTests.cs`: the catalogue `[Argon, Pressure, Power]` with `[Chamber, Gas]` (`:35`);
    `TheTable_ShowsThePensAsReadWithTheNamesOfTheirGroups` reads the group texts "Chamber, Gas", "Chamber"
    and "" (`:46-56`); `Refresh_KeepsTheSortOrder` adds a `DataGridSortDescription` by hand (`:106-117`).
  - `PenGroupsViewModelTests.cs:75`: a membership toggle is `gas.Membership!.ToggleMembershipCommand.Execute()`;
    `PenGroupViewModel.Membership` is the selected pen's entry (`PenGroupViewModel.cs:63-68`).
  - `PenEditorFixture.cs`: `Argon` (3, "Argon flow", "sccm"), `Pressure` (7, "Chamber pressure"), `Power`
    (12, "Bias power") (`:10-17`); `Chamber` `[3, 7]`, `Gas` `[3]`, `Spare` `[]` (`:19-23`); `ViewCatalogue`
    (`:26`); `RowsOf` enumerates `PenRows` (`:46-49`).
  - `PenRowOrderTests.cs`: pure comparer tests as `[AvaloniaTheory]`/`[AvaloniaFact]`, because a
    `PenRowViewModel` is a `ReactiveObject` (`:16-42`).
  - `PenEditorViewTests.cs`: the edit-end tests `ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce` (`:50`)
    and `ANameTypedThenTheSelectedRowCtrlClicked_WritesOnceAndShowsTheHint` (`:99`).
  - `HeadlessInput.cs`: `Type` clicks the box, then sends the text (`:66-71`); `Clear` clicks, selects all and
    deletes (`:54-64`); `Press` (`:81-91`).
  - `InterMeasure.cs`: `InterTypeface`, `InterFont` (`:11-22`); `PenGroupsViewTests.ThePaneTexts_FitTheirLinesInInter`
    (`PenGroupsViewTests.cs:804-827`) is the model for a text-fit theory.
- Rules applied: `desktop-ui` Field widths ("A new width needs a stated reason", `SKILL.md:134-136`), Empty,
  disabled, hidden (`:201-207`), Text in the UI (`:222-230`); `csharp.md` (a change-guarded call to one named
  private method from a setter); `AGENTS.md` UI and Test sections.

## Development Approach

- **Testing approach**: TDD. The predicate's tests are written first against `PenRowFilter`; the view-model
  tests first against `FilterText`; the view tests first against the named control `PenListFilter`.
- A test written before its type or member exists fails to compile; that is its red state, and the task's
  implementation step turns it green. A view test that reads a control by name compiles at HEAD and fails
  there at run time.
- Complete each task fully before the next; every task ends with its tests passing.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every edit-semantics assertion that passes at HEAD keeps passing: a field writes on edit end, the refresh
  order, the sort keeping the selection.

## Testing Strategy

- **Unit tests**: a new `PenRowFilterTests` for the predicate; `PenEditorViewModelTests` gains the filter
  cases.
- **View tests**: `PenEditorLayoutViewTests` gains the box's place and the placeholder's fit in Inter;
  `PenListViewTests` gains the operator's filter cases; `PenEditorViewTests` gains the edit-end case.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- The headless shaper measures text about twice as wide as Inter (`ui-theme.md:597-599`), so the placeholder's
  fit is gated in Inter through Skia, in `en` and `ru`, against the realised box's inner width.
- Every new test class carries `[Trait("Component", "UI")]`, `[Trait("Area", "Chart")]`,
  `[Trait("Category", "Unit")]` (the pen editor classes' traits, `PenRowOrderTests.cs:16-18`). A class that
  reads a `Resources` value or compares under the current culture joins `ProcessGlobalStateCollection`
  (`ui-text.md`, Adding a key): `PenRowFilterTests` joins it, because the match compares under the current
  culture.
- Every click, clear, typed text and key press goes through `HeadlessInput` (`AGENTS.md`, Test).

## Acceptance Evidence

The defect is automatable. At HEAD the top bar holds no `TextBox` and no control named `PenListFilter`
exists. The new `PenEditorLayoutViewTests.TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder`
reads `PenEditorWindow.FilterBoxWidth` and `Resources.PenEditorFilterPlaceholder`, so Task 3 adds the constant
and the key first; with both in place and no box, the test compiles and fails at run time:
`Named<TextBox>(window, "PenListFilter")` throws.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenRowFilterTests|FullyQualifiedName~PenRowOrderTests|FullyQualifiedName~PenEditorViewModelTests|FullyQualifiedName~PenEditorLayoutViewTests|FullyQualifiedName~PenListViewTests|FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenFormPaneViewTests|FullyQualifiedName~PenGroupsViewTests|FullyQualifiedName~PenGroupsViewModelTests|FullyQualifiedName~ResourcesTests"
```

It must report 0 failed. These named tests must be present and pass:

- `PenRowFilterTests.ABlankText_MatchesEveryRow` (theory: `""`, `"   "`) — Task 1
- `PenRowFilterTests.TheText_MatchesTheIdTheNameOrTheGroupsAnywhereInside` (theory) — Task 1
- `PenRowFilterTests.TheMatch_IgnoresCaseInLatinAndCyrillic` — Task 1
- `PenRowFilterTests.TheText_IsTrimmedAtBothEnds` — Task 1
- `PenRowFilterTests.TheUnit_IsNotMatched` — Task 1
- `PenEditorViewModelTests.AFilterText_ShowsOnlyTheMatchingRowsAndAClearedTextShowsEveryRow` — Task 2
- `PenEditorViewModelTests.AFilter_KeepsTheSortOrder` — Task 2
- `PenEditorViewModelTests.AFilterTheSelectedRowStillMatches_KeepsTheSelectionAndItsForm` — Task 2
- `PenEditorViewModelTests.AFilterTheSelectedRowNoLongerMatches_ClearsTheSelectionAndAClearedTextRestoresNone` — Task 2
- `PenEditorViewModelTests.ARefreshWhileFiltered_ShowsOnlyTheMatchingRowsAndKeepsTheText` — Task 2
- `PenEditorViewModelTests.ARefreshThatMakesTheSelectedPenStopMatching_ClearsTheSelection` — Task 2
- `PenEditorViewModelTests.AMembershipChangeThatStopsTheSelectedRowMatching_KeepsTheRowShownAndSelected` — Task 2
- `PenEditorViewModelTests.AFilter_LeavesTheGroupsPaneWhole` — Task 2
- `PenEditorLayoutViewTests.TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` (the reproduction) — Task 3
- `PenEditorLayoutViewTests.ThePlaceholder_FitsTheFilterBoxInInter` (theory: `en`, `ru`) — Task 3
- `PenListViewTests.TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow` — Task 3
- `PenListViewTests.AFilterAfterACtrlClickClearedTheSelection_SelectsNoRow` (➕, the no-selection guard) — Task 3
- `PenListViewTests.AFilterTheSelectedRowMatches_KeepsTheRowSelectedAndItsForm` — Task 3
- `PenListViewTests.AFilterTheSelectedRowFailsToMatch_ClearsTheListSelectionAndShowsTheHint` — Task 3
- `PenListViewTests.AFilterUnderASort_KeepsTheOrderAndItsArrow` — Task 3
- `PenListViewTests.TheGroupsPane_ListsEveryGroupWhileThePenListIsFiltered` — Task 3
- `PenEditorViewTests.ANameTypedThenTheFilterBoxTypedAndEnterPressed_WritesOnceAndTheFilterWritesNothing` — Task 3
- `PenEditorLayoutViewTests.TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart` (unchanged) — Task 3
- `PenEditorLayoutViewTests.AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows` (unchanged) — Task 3
- `PenListViewTests.AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm` (unchanged) — Task 3
- `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries` (passes with the new key) — Task 3

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the two NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git diff $BASE...HEAD -- SemiPlot/SemiPlot.UI/PenEditor/PenRowOrder.cs SemiPlot/SemiPlot.UI/PenEditor/PenGroupsViewModel.cs SemiPlot/SemiPlot.UI/PenEditor/PenGroupViewModel.cs   # prints nothing
git diff $BASE...HEAD -G "SortDescriptions" --stat -- SemiPlot/SemiPlot.UI   # prints nothing
git diff $BASE...HEAD --stat -- ConfigFiles SemiPlot/SemiPlot.UI/Settings   # prints nothing
```

The look of the placeholder and the typing feel cannot be seen headlessly; the manual checks under
Post-Completion cover them.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The view model filters the one view the grid already sorts.** `PenRows` takes a `Filter` once, in the
  constructor, as a predicate over the row and the current `FilterText`; a `FilterText` change calls
  `PenRows.Refresh()`, which re-creates the view with its sort descriptions and its filter
  (`Avalonia.Controls.DataGrid.xml:3809-3812`). The sort descriptions are never touched, so the grid's sort
  and its arrow stay. No second collection and no copied row list exist.
- **The match rule lives in one pure static class**, `PenEditor/PenRowFilter`, beside `PenRowOrder`:
  `Matches(PenRowViewModel row, string? filterText)`. The view model and its tests call it; the view never
  does.
- **The filter box is the one writer of `FilterText`**, through a plain two-way `Text` binding. The refresh
  does not touch it.

Design decisions, each with its reason:

- **A filter change that hides the selected row clears the selection.** The view model sets `SelectedRow` to
  null when the selected row no longer matches, so `SelectedForm` drops through `Switch` and the pane shows
  `PenFormEmptyHint`; the groups pane's boxes disable as they do with no pen selected. Reason: a selected row
  the operator cannot see, whose form stays open and editable in the pane, is a surprise, and the membership
  boxes would edit a pen the list does not show. This mirrors the refresh, which clears the selection when
  the selected pen is gone (`PenEditorViewModel.cs:165`, `Refresh_ClearsTheSelectionWhenTheSelectedPenIsGone`).
  The rule is the view model's, not the grid's, so a unit test pins it without a window.
- **The clear runs before `PenRows.Refresh()`.** The one-way `SelectedItem` binding hands the grid the null while the row is still in its view, and the grid then moves the view's current item before the first row (`DataGrid.OnSelectedItemChanged` calls `MoveCurrentTo(null)`), so the refresh keeps no current item and the grid selects nothing. In the reverse order the refresh would make the first matching row current and the grid would select it. The grid's echo through `OnPenListSelectionChanged` writes the same null and changes nothing.
- **A cleared filter restores no selection.** The row comes back unselected. Reason: restoring would keep a
  remembered hidden selection, a second state behind `SelectedRow`, and `SelectedRow` keeps its writers: the
  list, the refresh and now the filter.
- **The refresh restores the selected pen only if it still matches.** `RefreshAsync` looks the pen up by id
  among the new rows that pass the filter. Reason: the re-read row may carry a new name or new groups, and
  the rule above holds for every change of the view, not only for a typed one.
- **The filter does not re-evaluate when a shown row's name or groups change.** It applies at the next
  filter change and at the next refresh. Reason: with "Chamber" typed and a pen selected, unticking Chamber in
  the groups pane, or renaming the pen in its form, would make the row vanish and drop the form under the
  operator's hands. The rows stay as filtered until the operator types again.
- **The match**: the text is trimmed at both ends; an empty or whitespace-only text matches every row.
  Otherwise a row matches when its id as invariant decimal text, `Pen.Name` or `GroupsText` contains the text
  under `StringComparison.CurrentCultureIgnoreCase`. Reasons: the trim makes a stray leading or trailing
  space no filter of its own; the comparison is the one `PenRowOrder` sorts by (`PenRowOrder.cs:36`), one case
  rule for the list, and it folds Cyrillic case as the operators type Russian; "contains" over the id means
  "1" matches 1, 12 and 100, which the roadmap's "contains the typed text" asks for. The unit is not matched:
  the roadmap names id, name and group only. `Pen.Name`, not `QueuedPen.Name`, because the list's cell shows
  `Pen.Name` (`PenEditorWindow.axaml:177-181`).
- **No throttle.** Every keystroke refreshes the view at once. Reason: the rows are in memory, a refresh
  realises only the rows on screen (`PenListViewTests.AFullCatalogue_RealisesOnlyTheRowsOnScreen`), and a
  `Throttle` would bring a second scheduler and an `ObserveOn` into a synchronous property.
- **The filter call is a change-guarded call from the setter.** `FilterText`'s setter assigns and raises
  through `RaiseAndSetIfChanged` and, when the value changed, calls the one private method
  `ShowRowsMatchingFilter()` (`csharp.md`, a change-guarded call to one named private method). Reason: it is
  synchronous, so the view-model tests need no scheduler.
- **The box stays enabled during a refresh.** The pen pane disables because the rebuild replaces its target
  (`PenEditorViewModel.cs:97-100`); the filter's target is the view, which the refill keeps.
- **The box is 280 px wide**, not the 240 px of free text in `desktop-ui` Field widths (`SKILL.md:138-140`):
  the roadmap fixes the width (`20261102-visual-chore-roadmap.md:224`), and the box carries a sentence-long
  placeholder rather than one value, with room left for typed text past it. The width is the
  constant `PenEditorWindow.FilterBoxWidth`, beside the pane widths, so the layout test reads it. The fit is
  gated in Inter in both languages.
- **The box takes no part in the edit-end routing.** It carries no `PenEditorWindow.Field` and its
  `DataContext` is the `PenEditorViewModel`, so `OnFieldGotFocus`, `OnFieldLostFocus` and `OnFieldKeyDown`
  ignore it (`PenEditorWindow.axaml.cs:168-217`, `:358-362`), as they ignore `NewGroupName`. The code-behind
  does not change. A click into the box ends a pending form edit through the form field's own `LostFocus`, as
  a click on any other focusable control does.
- **No empty-result text and no clear button.** A filter that matches nothing shows an empty list under its
  headers; the typed text in the box says why. Neither is in the slice's scope.

## Technical Details

### `PenEditor/PenRowFilter.cs`

```csharp
using System.Globalization;

namespace SemiPlot.UI.PenEditor;

/// <summary>The pen list's filter: a row matches when its id, name or groups contain the trimmed text.</summary>
public static class PenRowFilter
{
	public static bool Matches(PenRowViewModel row, string? filterText)
	{
		if (string.IsNullOrWhiteSpace(filterText))
		{
			return true;
		}

		var text = filterText.Trim();

		return Contains(row.Pen.Id.ToString(CultureInfo.InvariantCulture), text)
			|| Contains(row.Pen.Name, text)
			|| Contains(row.GroupsText, text);
	}

	private static bool Contains(string field, string text)
	{
		return field.Contains(text, StringComparison.CurrentCultureIgnoreCase);
	}
}
```

The parameter is `string?` because the `TextBox` binding may write a null when the box empties (ASSUMPTION
below).

### `PenEditorViewModel`

- Constructor: `PenRows = new DataGridCollectionView(_rows) { Filter = row => row is PenRowViewModel penRow && PenRowFilter.Matches(penRow, FilterText) };`
  The compiler checks the delegate type of `Filter`; the pattern match keeps the cast soft.
- New property, ordered after `PenRows`:

  ```csharp
  /// <summary>The pen list shows the rows <see cref="PenRowFilter"/> matches against it; the filter box writes it.</summary>
  public string FilterText
  {
  	get;
  	set
  	{
  		if (field != value)
  		{
  			this.RaiseAndSetIfChanged(ref field, value);
  			ShowRowsMatchingFilter();
  		}
  	}
  } = string.Empty;
  ```

- `ShowRowsMatchingFilter()`: if `SelectedRow` is set and `PenRowFilter.Matches(SelectedRow, FilterText)` is
  false, set `SelectedRow = null`; then `PenRows.Refresh()`.
- `RefreshAsync` (`:165`): `SelectedRow = rows.FirstOrDefault(row => row.Pen.Id == selectedPenId && PenRowFilter.Matches(row, FilterText));`
  The refill loop (`:158-162`) does not change.

### The window

- `PenEditorWindow.axaml.cs`: `public const double FilterBoxWidth = 280;` beside `GroupsPaneWidth` (`:24`).
- `PenEditorWindow.axaml`, the top bar (`:96-112`) keeps `*,Auto,Auto`. Column 0 gets:

  ```xml
  <TextBox Grid.Column="0"
           Name="PenListFilter"
           Width="{x:Static local:PenEditorWindow.FilterBoxWidth}"
           HorizontalAlignment="Left"
           PlaceholderText="{x:Static text:Resources.PenEditorFilterPlaceholder}"
           Text="{Binding FilterText}" />
  ```

  The count and the button stay right-aligned in columns 1 and 2, so the assertions of
  `TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart` hold unchanged. The box's
  left edge is the root margin, 16 px, which is the pen list border's left edge.
- Width at `MinWidth`: 280 + 8 + 294 + 8 + 334 = 924 px in Russian, inside the bar's 1240 px.

### Resources

| Key | `en` | `ru` |
| --- | --- | --- |
| `PenEditorFilterPlaceholder` | Filter: id, name or group | Фильтр: номер, имя или группа |

"id" and «номер» are the Id column's own terms (`PenEditorColumnId`, "Id" / "Номер"), lower-cased inside the
sentence, so the box and the header use one term per concept.

### ASSUMPTIONs, each with its guard and fallback

- ASSUMPTION: a `PenRows.Refresh()` whose result still holds the selected row keeps the grid's `SelectedItem`
  and raises no `SelectionChanged`, as the sort's refresh does (Context). Guard:
  `PenListViewTests.AFilterTheSelectedRowMatches_KeepsTheRowSelectedAndItsForm` asserts the same
  `SelectedItem`, `SelectedRow` and `SelectedForm` instances. Fallback: `ShowRowsMatchingFilter` captures the
  matching `SelectedRow` before `Refresh()` and writes it back after; the test then asserts the same
  `SelectedRow` and a form for the same pen, and the deviation is recorded as a ⚠️ under Task 3. The fallback
  loses no edit: typing in the filter box means the focus left the form field, whose `LostFocus` wrote it.
- ASSUMPTION: a `PenRows.Refresh()` with no row selected selects nothing. The view's current item can be a row while nothing is selected: the view opens with its first row current, and a Ctrl+click that clears the selection leaves the row current. When the filter hides that row, `DataGridCollectionView.ResetCurrencyValues` makes the first matching row current and the grid's `UpdateStateOnCurrentChanged` selects it (decompiled `Avalonia.Controls.DataGrid` 12.0.1, verified 2026-11-03), so this assumption is expected to fail. Guard: `PenListViewTests.TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow`, which types "bias" with no row clicked and so hides Argon, the view's current row. Fallback: `ShowRowsMatchingFilter` calls `PenRows.MoveCurrentTo(SelectedRow)` after the clear and before `PenRows.Refresh()`. With a selected row the call changes nothing, because the grid already made that row current; with none it puts the current item before the first row, so the refresh selects nothing. The deviation is recorded as a ⚠️ under Task 3, and Task 5 states it in `overview.md`.
- ASSUMPTION: while `Filter` is set, a row added to `_rows` enters the view only if it passes the filter, at
  its sorted place. Guard: `PenEditorViewModelTests.ARefreshWhileFiltered_ShowsOnlyTheMatchingRowsAndKeepsTheText`
  under a descending id sort. Fallback: `RefreshAsync` calls `PenRows.Refresh()` once after the refill loop;
  `overview.md`'s "a refresh raises N inserts" sentence then changes in Task 5.
- ASSUMPTION: `DataGridCollectionView` does not re-filter a row when the row raises `PropertyChanged`. Guard:
  `PenEditorViewModelTests.AMembershipChangeThatStopsTheSelectedRowMatching_KeepsTheRowShownAndSelected`.
  Fallback: `Filter` reads a `HashSet<PenRowViewModel>` of the matching rows that `ShowRowsMatchingFilter`
  and the refresh's refill recompute, so a row's own change cannot move it out.
- ASSUMPTION: the `TextBox`'s `Text` binding writes `FilterText` on every keystroke. Guard:
  `PenListViewTests.TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow` reads the rows
  after `HeadlessInput.Type` with the focus still in the box. Fallback: the binding takes
  `UpdateSourceTrigger=PropertyChanged`.
- ASSUMPTION: the emptied box writes `""` or null, never another value; `PenRowFilter` treats both as empty.
  Guard: the same test's `HeadlessInput.Clear` step. Fallback: none needed beyond the `string?` parameter of
  `Matches`.
- ASSUMPTION: Semi's `TextBox` realises 32 px tall, the height of the bar's button, so the bar stays 32 px and
  `MinimumHeight` 536 holds (`ui-theme.md:445`). Guard:
  `TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` asserts the box's and the bar's heights
  equal to the button's, and `AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows` stays
  green. Fallback: `Height="32"` on the box.
- ASSUMPTION: the Russian placeholder fits the box's inner width in Inter. Guard: `ThePlaceholder_FitsTheFilterBoxInInter`. Fallback: `FilterBoxWidth` rises to the smallest multiple of 8 that holds the longer of the two measured placeholders plus the box's `Padding` and `BorderThickness` on both sides; both placeholder texts stay. The test `TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` takes the new width in its name in every place this plan names it, the run records both measured widths and the new width as a ⚠️ under Task 3, and Task 5 states in `ui-theme.md` the width the code carries and why it departs from the roadmap's 280 px. The top bar's sum stays far inside its 1240 px.

### Test helpers

- `PenEditorWindowDriver.cs`: `FilterBoxOf(window)` returns `Named<TextBox>(window, "PenListFilter")`, read
  by the layout, list and edit tests.
- No new input helper: typing, clearing and Enter go through `HeadlessInput.Type`, `Clear` and `Press`.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): the predicate, the view-model filter, the box, the key, the
  tests and the architecture docs.
- **Post-Completion** (no checkboxes): the manual look of the box in the running viewer.

## Implementation Steps

### Task 1: Add the pen list's match rule, tests first

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenRowFilter.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenRowFilterTests.cs`

- [x] create `PenRowFilterTests` (the three traits, `ProcessGlobalStateCollection`; `[AvaloniaFact]` and
      `[AvaloniaTheory]`, since each row is a `ReactiveObject`) over rows built as
      `new PenRowViewModel(pen, [Chamber, Gas])` for `Argon`, `Pressure` and `Power` (group texts
      "Chamber, Gas", "Chamber", "")
- [x] write the failing `ABlankText_MatchesEveryRow` (theory: `""`, `"   "`) and
      `TheText_MatchesTheIdTheNameOrTheGroupsAnywhereInside` (theory of text and matching ids: `"1"` -> 12,
      `"7"` -> 7, `"power"` -> 12, `"flow"` -> 3, `"gas"` -> 3, `"chamber"` -> 3 and 7 (Argon by its group only,
      Pressure by its name and its group), `"ber, Ga"` -> 3) (red: `PenRowFilter` does not exist)
- [x] write the failing `TheMatch_IgnoresCaseInLatinAndCyrillic` (`"ARGON"` matches Argon; a pen named «Давление камеры» matches «ДАВЛЕНИЕ» and «КАМЕРЫ», each written in the test as `\u` escapes because terse refuses non-ASCII source, as `UI/Legend/LegendConvertersTests.cs:46` does: `"Давление камеры"`, `"ДАВЛЕНИЕ"`, `"КАМЕРЫ"`), `TheText_IsTrimmedAtBothEnds` (`"  argon  "` matches Argon only) and `TheUnit_IsNotMatched` (`"sccm"` matches no row)
- [x] create `PenRowFilter` as Technical Details states
- [x] run `PenRowFilterTests` and `PenRowOrderTests`: every case passes

### Task 2: Filter the pen list's view in the view model, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewModelTests.cs`

- [x] write the failing `AFilterText_ShowsOnlyTheMatchingRowsAndAClearedTextShowsEveryRow` (`"chamber"` ->
      Argon and Pressure; `""` -> all three in catalogue order; `PenRows.Count` equals the rows shown;
      `_editor.Calls` stays empty) and `AFilter_KeepsTheSortOrder` (a descending `PenRowOrder.ById` sort, then
      `"chamber"` -> ids 7, 3) (red: `FilterText` does not exist)
- [x] write the failing `AFilterTheSelectedRowStillMatches_KeepsTheSelectionAndItsForm` (Pressure selected,
      `"press"`: the same `SelectedRow` and `SelectedForm` instances) and
      `AFilterTheSelectedRowNoLongerMatches_ClearsTheSelectionAndAClearedTextRestoresNone` (Power selected,
      `"chamber"`: `SelectedRow` and `SelectedForm` null; then `""`: Power shown, `SelectedRow` still null)
- [x] write the failing `ARefreshWhileFiltered_ShowsOnlyTheMatchingRowsAndKeepsTheText` (a descending id sort,
      `"4"`, a read adding `NumberedPen(4242)` and `NumberedPen(14)`: shown ids 4242, 14; `FilterText` still
      `"4"`) and `ARefreshThatMakesTheSelectedPenStopMatching_ClearsTheSelection` (`"chamber"`, Pressure
      selected, a read where Pressure is named "Gauge" and no group holds it: `SelectedRow` null)
- [x] write the failing `AMembershipChangeThatStopsTheSelectedRowMatching_KeepsTheRowShownAndSelected`
      (`"gas"`, Argon selected, `await gas.Membership!.ToggleMembershipCommand.Execute()` on Gas (`editor.Groups.Groups[1]`) succeeds: Argon's
      `GroupsText` is "Chamber", it is still the one row shown and still `SelectedRow`) and
      `AFilter_LeavesTheGroupsPaneWhole` (Power selected, `"power"`: `Groups` is the same instance, it lists
      Chamber and Gas, and each group's `Membership.Row` is Power's row)
- [x] add `FilterText`, the constructor's `Filter`, `ShowRowsMatchingFilter` and the refresh's restore rule as
      Technical Details states
- [x] run `PenEditorViewModelTests`, `PenGroupsViewModelTests` and `PenRowFilterTests`: every case passes

### Task 3: Add the filter box to the top bar, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorWindowDriver.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorLayoutViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenListViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`

- [x] add `FilterBoxOf` to the driver, and `PenEditorWindow.FilterBoxWidth` and the `PenEditorFilterPlaceholder`
      key to both resx files, so the view tests compile; the box itself is not added yet
- [x] write the failing `TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder`: the box is inside
      `PenEditorTopBar`, `Bounds.Width` equals `FilterBoxWidth`, its left edge equals the pen list border's
      (16 px), its height and the bar's each equal `PenEditorRefreshButton`'s (32 px in the `MinHeight` sum,
      `ui-theme.md:445`), `PlaceholderText` is
      `Resources.PenEditorFilterPlaceholder`, and the box ends left of `PenEditorAddedCount`
- [x] write the failing theory `ThePlaceholder_FitsTheFilterBoxInInter` (`en`, `ru`; `CultureScope` as
      `ThePaneTexts_FitTheirLinesInInter`): the placeholder measured in Inter at the box's `FontSize` is at most
      the box's width less its `Padding` and `BorderThickness` on both sides
- [x] write the failing `PenListViewTests` cases over `ViewCatalogue`:
      `TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow` (no row clicked;
      `HeadlessInput.Type` "bias": `RealisedPens` is Power alone, `PenList.SelectedItem` and `SelectedRow` null,
      `PenFormEmptyHint` on screen; `HeadlessInput.Clear`: all three, still no selection and the hint on screen);
      `AFilterTheSelectedRowMatches_KeepsTheRowSelectedAndItsForm` (click Pressure, type "press": the same
      `SelectedItem`, `SelectedRow`, `SelectedForm`; `PenFormEmptyHint` hidden);
      `AFilterTheSelectedRowFailsToMatch_ClearsTheListSelectionAndShowsTheHint` (click Power, type "chamber":
      `PenList.SelectedItem` null, `PenFormEmptyHint` visible, every membership box disabled);
      `AFilterUnderASort_KeepsTheOrderAndItsArrow` (two clicks on the Id header, type "chamber": `RealisedPens`
      ids 7, 3 and the header keeps `:sortdescending`);
      `TheGroupsPane_ListsEveryGroupWhileThePenListIsFiltered` (type "zzz": no pen row realised, the group grid
      still realises Chamber and Spare)
- [x] write the failing `PenEditorViewTests.ANameTypedThenTheFilterBoxTypedAndEnterPressed_WritesOnceAndTheFilterWritesNothing`:
      click Pressure, retype its name as "Gauge", type "7" (its id, which the new name leaves matching) into
      the filter box and press Enter: `_editor.Calls` holds exactly one name write, "Gauge" for pen 7, and the
      row still shows
- [x] add the `TextBox` `PenListFilter` to column 0 of `PenEditorTopBar` as Technical Details states; the
      code-behind gains only the constant
- [x] run `PenEditorLayoutViewTests` (`TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart`
      and `AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows` unchanged), `PenListViewTests`,
      `PenEditorViewTests`, `PenFormPaneViewTests`, `PenGroupsViewTests`, `ResourcesTests`: every case passes;
      note under this task the placeholder's two measured Inter widths and the box's inner width, which Task 5
      carries into `ui-theme.md`
- [x] ➕ write the failing `PenListViewTests.AFilterAfterACtrlClickClearedTheSelection_SelectsNoRow` (click Argon,
      Ctrl+click it, type "bias": `RealisedPens` is Power alone, `PenList.SelectedItem` and `SelectedRow` null,
      `PenFormEmptyHint` on screen), the guard for the path the no-selection ASSUMPTION fails on; red before the
      fallback with Power selected
- [x] apply the no-selection fallback: `ShowRowsMatchingFilter` calls `PenRows.MoveCurrentTo(SelectedRow)` after the
      clear and before `PenRows.Refresh()`

Measured in Inter at the box's `FontSize` (`ThePlaceholder_FitsTheFilterBoxInInter` output): the placeholder is
157.6 px in `en` and 217.3 px in `ru`; the box's inner width (280 px less `Padding` and `BorderThickness` on both
sides) is 254 px. The width ASSUMPTION held, `FilterBoxWidth` stays 280. The height ASSUMPTION held: the box and
the bar realise at the button's height with no `Height` set.

⚠️ The no-selection ASSUMPTION failed, but not where the plan predicted. The view opens with no current item
(`PenRows.CurrentPosition` is -1 before any click), so the named guard
`TypingInTheFilterBox_ShowsOnlyTheMatchingRowsAndClearingItShowsEveryRow` passed without the fallback. After a
click on Argon and a Ctrl+click on it, the selection is null and Argon (id 3) stays current; typing "bias" then
made the grid select Power (id 12) and `SelectedRow` followed. The fallback above fixes it, the new
`AFilterAfterACtrlClickClearedTheSelection_SelectsNoRow` pins it, and with a row selected the call changes
nothing: `AFilterTheSelectedRowMatches_KeepsTheRowSelectedAndItsForm` and
`AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm` keep asserting the same instances.
Task 5 states the built behaviour in `overview.md`.


### Task 4: Verify acceptance criteria

- [ ] run the filtered command under Acceptance Evidence: 0 failed, every named test present
- [ ] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
- [ ] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the two NU1902 for `MessagePack`
      2.5.302 in `SemiPlot.AppHost` that the base already reports
- [ ] set `$BASE` to the stacking base: `pen-editor-groups-pane` while it is unmerged, `master` once it merges
      (`git merge-base --is-ancestor pen-editor-groups-pane master`, exit 1 means unmerged)
- [ ] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse`
      over every `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
- [ ] run the HeadlessInput gate and the three scope gates under Acceptance Evidence: each prints what it must
      (no change to `PenRowOrder.cs`, `PenGroupsViewModel.cs`, `PenGroupViewModel.cs`; no `SortDescriptions`
      line; nothing under `ConfigFiles` or `Settings`)

### Task 5: [Final] Update documentation

- [ ] `docs/architecture/overview.md:525`: the top bar's row names the filter box on the left (id, name or
      group text, case-insensitive, trimmed; an empty box shows every row; the groups pane unfiltered)
- [ ] `docs/architecture/overview.md`, after the selection paragraph (`:552-557`): a paragraph on the filter:
      `PenRows.Filter` set once over `PenRowFilter.Matches` and `FilterText`, `Refresh()` on each change with
      the sort descriptions untouched; the selection kept while it matches and cleared before the refresh when
      it does not, with no restore; the refresh restoring the selected pen only if it matches; no
      re-evaluation on a row's own change, and why; each with its gating test name
- [ ] `docs/architecture/ui-theme.md:431-435`: the top bar's column 0 holds `PenListFilter`, 280 px
      (`FilterBoxWidth`), left-aligned on the margin, and why 280 rather than 240; `:43` names the filter box
      beside the new-group field as a `TextBoxPlaceholderForeground` consumer; `:601-604` adds the box to the
      top bar's width sum (924 px of 1240 px in Russian) and the placeholder's measured Inter widths from
      Task 3; the gates table gains `TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` and
      `ThePlaceholder_FitsTheFilterBoxInInter`
- [ ] `docs/architecture/ui-text.md:255-260`: the top bar reads `PenEditorFilterPlaceholder`, "Filter: id,
      name or group" / «Фильтр: номер, имя или группа», which reuses the Id column's term in each language
- [ ] if a Task 2 or Task 3 fallback ran, state the built behaviour in the matching `overview.md` sentence
- [ ] move this plan to `docs/plans/completed/` (left to the delivery step)

## Post-Completion

**Manual verification** in the running viewer (`Edit` -> `Pens and groups`), in both languages:

1. The top bar shows the filter box on the left with the placeholder in secondary text on one line, and the
   added count and `Refresh pen list` on the right.
2. Type part of a pen name, then a group name, then digits of an id: the list narrows as each character lands,
   in either letter case and in Cyrillic.
3. Select a pen and type text it matches: it stays highlighted and its form stays. Type text it does not
   match: the pane shows "Select a pen in the list on the left" and the membership boxes grey out.
4. Sort by a column, then filter: the arrow and the order stay.
5. Empty the box: every pen shows; the groups pane listed every group throughout.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: export-pen-choice-list (the
export window's pens as checkboxes only, no row selection); axis-scale-checkbox-column (the axis scale panel's
log checkbox in the field column, with a label in the label column); ui-spacing-scale (every remaining inset
onto the 4/8/12/16/24/32 scale, the `12,2` button padding with them).
