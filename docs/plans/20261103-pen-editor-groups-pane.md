# Pen editor: one window with the groups pane beside the pen pane

## Overview

- `PenEditor/PenEditorWindow` is a `TabControl` of two tabs over a bottom bar
  (`PenEditorWindow.axaml:95-707`). The `Pens` tab shows the pen list and the pen pane; the `Groups` tab
  shows the group list, one rename field, a fold checkbox and a delete button driven by the list's
  selection, and beside them every pen as a checkbox for the selected group (`:489-686`). The two tabs show
  one many-to-many relation from two sides in opposite orientations, and a group row is both selectable and
  the target of the rename block: two states per row.
- The tabs go. The window is one row of three parts under a top bar: pen list | pen pane (420 px) |
  groups pane (340 px). The top bar carries the added count and `Refresh pen list`, right-aligned; its left
  side stays empty for the filter of the next slice.
- The groups pane: the title "Группы", the one-line subtitle "Отмечены группы выбранного пера", a new-group
  row (name box and "Создать"), then a read-only `DataGrid` of three columns: the membership checkbox
  (empty header), Группа with the pen count as right-aligned secondary text inside the cell and the tooltip
  "Перьев в группе", and Действия with a "..." button that opens a menu of Переименовать,
  Свёрнута при открытии (checkable) and Удалить. A row shows no selection highlight and no cell outline: its
  one state is the checkbox. Under the list a reserved block, present in every state, shows the rename box
  or the delete confirmation, which names the group and its pen count; its Удалить button takes destructive
  brushes from new `Palette.axaml` keys. The pane's reserved message line closes it.
- Membership is set for the pen selected in the pen list, through the existing
  `PenMembershipViewModel` -> `PenGroupWriter.RunAsync(SetMembershipAsync)` -> `PenGroupViewModel.SetMembership`
  path. With no pen selected every membership box is disabled.
- The window opens 1440 px wide where the owner's screen working area allows, else at that area's width,
  never below its 1272 px minimum, and has a minimum height derived from its fixed rows.
- The write path does not change: no change to `IPenCatalogueEditor`, `PenGroupWriter`, the membership
  toggle or the queue.
- Out of scope: the editor following catalogue reads (#140, `docs/plans/20261030-editor-follows-reads.md`),
  the top bar's filter (slice pen-list-filter) and saved column widths.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice pen-editor-groups-pane

The slice has no issue (`20261102-visual-chore-roadmap.md:211`), so the PR body closes none.

Stacking: this branch, `pen-editor-groups-pane`, starts at `8ecbe9a`, the tip of `pen-editor-pens-pane`
(verified 2026-11-03: `git rev-parse pen-editor-pens-pane HEAD` prints the same hash), which stacks on
`hex-color-input`; none is merged.

- Window (`SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`):
  - `Width="1272"`, `MinWidth="1260"`, `Height="720"`, `SizeToContent="Manual"`, `CanResize="True"`,
    `WindowStartupLocation="CenterOwner"` (`:12-18`).
  - Resources: `IdColumnWidth` (`:21`), `PenListIdColumnWidth` (`:22`), `DataGridCellMinHeight` 28 (`:23`),
    `MembershipColumns` (`:24-28`), `PenFormColumns` (`:29-33`), `LineStyleItem` (`:34-37`).
  - Styles: the header font `DataGrid#PenList DataGridColumnHeader` (`:41-44`); the cell-outline rule
    `DataGrid#PenList DataGridCell:current /template/ Rectangle#CurrencyVisual, DataGrid#PenList
    DataGridCell:focus /template/ Panel#FocusVisual` (`:45-47`); `TextBlock.grid-cell` (`:48-51`);
    `TextBlock.cell` with `Margin="6,0"` (`:52-56`); `ListBox#GroupList ListBoxItem` (`:89-92`).
  - The root grid `Margin="12"`, `RowDefinitions="*,Auto"`, `RowSpacing="12"` (`:95-97`); `TabControl`
    `PenEditorTabs` (`:99-100`); `TabItem` `PensTab` (`:102-103`) with the grid `*,16,Auto` (`:105`), the
    bordered `DataGrid` `PenList` (`:107-230`; the name column `Width="220"`, `:143-146`; the fill column
    Groups, `:210-214`) and the `Border` `PenFormPanel`, 420 px, `IsEnabled="{Binding !IsRefreshing}"`
    (`:232-239`).
  - `TabItem` `GroupsTab` (`:489-686`): `NewGroupName` and `CreateGroupButton` (`:509-523`); the `ListBox`
    `GroupList` with `SelectedItem="{Binding SelectedGroup}"`, rows `GroupName` and `GroupMemberCount` in
    `TextBlock.cell` (`:525-552`); `GroupRename`, `GroupFoldedOnStartBox` and `DeleteGroupButton` under
    the selection (`:554-592`); the `ItemsControl` `MembershipList` of `PenMembershipViewModel` checkboxes
    `MembershipBox` (`:596-637`); the 32 px `GroupDeletionRow` with `GroupDeletionText`,
    `ConfirmDeleteButton`, `CancelDeleteButton` (`:641-673`); `GroupsMessage`, `TextBlock.form-message
    error` (`:675-678`).
  - The bottom bar `PenEditorBottomBar`: `PenEditorAddedCount` and `PenEditorRefreshButton` with
    `Padding="12,2"` in a right-aligned `Auto,Auto` grid 12 apart (`:690-707`).
- Code-behind (`PenEditorWindow.axaml.cs`): `RowHeight = 28` (`:20`); `OnLoaded`/`OnUnloaded` pair
  (`:55-83`); the tunnel press handler that clears the focus on a press outside any focusable control
  (`:128-141`); `OnFieldGotFocus` records `group.EndRenameAsync` when `GroupRename` takes the focus with a
  `PenGroupViewModel` data context (`:144-154`); `OnPenListSelectionChanged` writes every selection change
  to `SelectedRow` (`:215-221`); `EndsItsEditOnItsOwn` names `GroupRename` (`:318-321`).
- View models:
  - `PenGroupsViewModel.cs`: `PendingGroupDeletion(Group, Text)` (`:17`); the constructor takes the rows and
    builds `PenGroupWriter` with `ShowGroupsOn` (`:33-44`); `Memberships` from `SelectedGroup` through
    `_membershipsOfGroup` (`:46-49`, `:139-153`); `Message` from the selected group's message, else the
    writer's refusal (`:51-56`); `DeleteGroupCommand` on the selection (`:60-66`); `SelectedGroup` with the
    `_isReplacingGroups` guard (`:81-91`, `:222-237`); `CreateGroupAsync` selects the new group (`:155-175`);
    `AskToDelete` builds the confirmation from `Resources.FormatPenGroupsDeleteConfirmation` (`:177-186`);
    `ConfirmDeleteAsync` (`:188-220`).
  - `PenGroupViewModel.cs`: internal constructor `(StoredGroup, PenGroupWriter)` (`:20-27`); `Group` raises
    `MemberCount` and `FoldedOnStart` (`:29-42`); `ToggleFoldedOnStartCommand` (`:45`, `:135-152`);
    `RenameDraft`, `IsNameValid`, `Message`, `EndRenameAsync` (`:47-120`); `SetMembership` (`:128-133`).
  - `PenMembershipViewModel.cs`: internal constructor `(PenRowViewModel, PenGroupViewModel, PenGroupWriter)`
    (`:13-20`); `IsMember` (`:24`); `ToggleMembershipCommand` built with `CreateFromTask` and no
    `canExecute` (`:20`, `:27`); the toggle calls `SetMembershipAsync(Row.Pen, _group.Group, isMember)`, then
    `SetMembership` and `ShowGroupsOn` on success, or `RefuseWrite` and a re-raise of `IsMember` on failure
    (`:34-52`).
  - `PenGroupWriter.cs`: the tab's one route to the editor (`:16-21`), `RunAsync` (`:37-48`), `RefuseWrite`
    (`:56-60`), `ShowGroupsOn` (`:63-66`).
  - `PenEditorViewModel.cs`: `Groups = GroupsOf(rows, catalogue.Groups)` in the constructor (`:52`);
    `SelectedRow` (`:71-75`); the refresh keeps the selected group by id (`:154-158`) and disposes the
    replaced groups (`:159-169`); `GroupsOf` (`:191-194`).
- `SemiPlot/SemiPlot.UI/Tables/HeaderMeasuredColumns.cs`: `HeadersMeasured` latches on the first successful
  measure (`:95-98`); `TryMeasureHeaders` waits until every visible column has a header (`:106-121`);
  `FitFillColumn` sums every visible non-fill column wherever the fill column stands (`:140-159`).
- Opening the editor: `MainWindow.axaml.cs:38-40` hands `DialogOpener.ShowOwnedDialog` a factory
  `() => new PenEditorWindow { DataContext = penEditor }`; `DialogOpener.cs:27-41` awaits
  `ShowDialog(owner)` and disposes the view model in `finally`.
- Theme: `App.axaml:14-19` (one `StyleInclude` of `Styles/Forms.axaml`, `:18`); `Palette.axaml` radii at the
  top level (`:5-11`), the five tab keys in each variant (`:44-48`, `:135-139`), the `ListBoxItem` keys
  (`:49-53`), the DataGrid keys (`:83-86`), the App keys (`:88-103`, `:179-194`); `Icons.axaml:4-18`;
  `Forms.axaml:17-24` (`TextBlock.form-message`, two 20 px lines, 40 px).
- The only other `ListBox` is the export window's pen list (`CsvExportWindow.axaml:149`); no other AXAML
  holds a `TabControl` or `TabItem` (verified with `git grep -n "TabControl\|TabItem" -- 'SemiPlot/SemiPlot.UI/*.axaml'`).
- The Semi DataGrid assembly carries the strings `DataGridRowBackground`, `DataGridRowPointeroverBackground`,
  `DataGridRowSelectedBackground`, `BackgroundBorder` and `CurrencyVisual` (verified 2026-11-03 by a UTF-16
  string search of `semi.avalonia.datagrid/12.0.0/lib/net10.0/Semi.Avalonia.DataGrid.dll`).
- `Avalonia.Fonts.Inter` 12.0.5 ships `Inter-SemiBold.ttf` beside `Inter-Regular.ttf` (verified by a string
  search of its assembly); `InterMeasure.cs:10-21` loads Regular only.
- `Avalonia.Headless` 12.0.5 carries a `HeadlessScreensStub` (string search of its assembly); its working
  area is not read here.
- Tests:
  - `PenGroupsViewTests.cs`: 13 view tests (`:41-408`), all opening the `Groups` tab, reading `GroupList`
    as a `ListBox` and `MembershipList` as an `ItemsControl` (`:410-415`).
  - `PenGroupsViewModelTests.cs`: 22 tests, most over `SelectedGroup`, `Memberships` and
    `DeleteGroupCommand` (`:43-492`).
  - `PenEditorViewTests.cs`: `TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart`
    (`:50-84`), `ARefreshThatChangesTheCatalogue_RebindsTheTableAndTheGroupsTab` (`:599-627`).
  - `PenFormPaneViewTests.cs`: `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum`
    asserts `Width < 1280` and every column inside the viewport at `MinWidth` (`:48-69`);
    `ThePane_Stays420PxWideWhenTheWindowWidens` (`:205-226`); `AtTheOpeningSize_ThePaneShowsTheWholeForm`
    (`:289-305`); `EveryPenFormLabel_FitsTheLabelColumnInInter` (`:308-332`).
  - `PenEditorWindowDriver.cs`: `RowAt` (`:44-59`), `HeaderOf` (`:61-64`), `OpenGroupsTab` (`:66-69`),
    `GroupAt` over the `ListBox` (`:71-75`), `TextOf` (`:77-80`), `RealisedRows` (`:97-100`).
  - `PenEditorFixture.cs`: `Chamber` `[3, 7]`, `Gas` `[3]`, `Spare` `[]` (`:19-23`); `ViewCatalogue` =
    `[Argon, Pressure, Power]` with `[Chamber, Spare]` (`:26`); `RowsOf` (`:41-44`).
  - `ThemeTests.cs`: `EveryTabAndListSurface_PaintsItselfFromThePalette` (`:207-254`), the only reader of
    the tab keys; `EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette` (`:346-386`);
    `PointAt` (`:500-508`).
  - `ChartMarkerViewTests.cs:905-914`: an open `MenuFlyout`'s item found through `menu.Popup.Child`'s visual
    descendants and clicked on its own `TopLevel`.
  - `MainWindowViewTests.cs:44-82`: opens the editor from the realised main window and reads
    `OwnedWindows`.
  - `ViewProbe.cs`: `HeaderOf` (`:47-51`), `ViewportOf` (`:54-61`), `UnconstrainedWidthOf` (`:91-99`).
  - `HeadlessInput.cs:16-52`: every click runs the jobs it queued.
- Resources (`Localization/Resources.resx`, `Resources.ru.resx`): `PenEditorPensTab` (`:225`),
  `PenEditorGroupsTab` (`:228`), `PenGroupsDeleteConfirmation` "Delete group '{0}'? Pens in the group: {1}.
  The pens themselves stay." / «Удалить группу «{0}»? Перьев в группе: {1}. Сами перья останутся.» (`:693`),
  `PenGroupsNameLabel` (`:702`), `PenGroupsFoldedOnStartLabel` "Folded when the viewer opens" /
  "Свёрнута при открытии" (`:711`), `PenGroupsMembersHeader` (`:714`).
- Docs: `overview.md:514-557` (the editor table and its rows), `:586-589`, `:595-597`, `:621-625`,
  `:647-648`; `ui-theme.md` key table (`:35-87`), the retint paragraph (`:101-108`), the tests paragraph
  (`:120-131`), the App surfaces (`:165-186`), "A resizable window keeps its fixed parts" (`:391-526`);
  `ui-text.md:223-269`; `charting.md:693-695` ("the row and membership VMs").
- Rules applied: `desktop-ui` Tables (`SKILL.md:45-79`), Spacing (`:88-112`), Resize (`:158-180`), Control
  choice (`:190-199`), Text (`:222-230`), Destructive actions (`:232-237`).
- Visual reference: the roadmap's Target end state (`20261102-visual-chore-roadmap.md:47-51`, `:79-85`).
- `BorderlessButton` is a theme key of Semi 12.0.3 (verified 2026-11-03 by a UTF-16 string search of
  `semi.avalonia/12.0.3/lib/net10.0/Semi.Avalonia.dll`).

## Development Approach

- **Testing approach**: TDD. The view-model tests are written first against the new members; the view
  tests are written first against the named controls and driven through `HeadlessInput`.
- A test written before its type or member exists fails to compile; that is its red state, and the task's
  implementation step turns it green. A view test that reads a control by name compiles at HEAD and fails
  there at run time.
- Complete each task fully before the next; every task ends with its tests passing.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every edit-semantics assertion that passes at HEAD keeps passing in what it asserts about writes: a field
  writes on edit end, a rename writes once, the scale pair writes once, the drain on close, the refresh
  order, a refused toggle puts the box back.

## Testing Strategy

- **Unit tests**: `PenGroupsViewModelTests` rewritten for the pen-side membership and the row actions;
  `ThemeTests` gains the destructive button and drops the tab half of its list test.
- **View tests**: `PenGroupsViewTests` rewritten over the `DataGrid` `GroupList`, the row menu and the
  reserved block; a new `PenEditorLayoutViewTests` class for the window's composition and sizes;
  `PenEditorViewTests`, `PenFormPaneViewTests`, `PenListViewTests` and `MainWindowViewTests` updated where
  they read the tabs, the bottom bar or the window size.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- The headless shaper measures text about twice as wide as Inter (`ui-theme.md:497-500`), so no headless
  test asserts a width that depends on text; those widths are gated in Inter through Skia, in `en` and `ru`.
- Every new test class carries `[Trait("Component", "UI")]`, `[Trait("Area", "Chart")]`,
  `[Trait("Category", "Unit")]` (the pen editor classes' traits, `PenGroupsViewTests.cs:26-29`), and joins
  `ProcessGlobalStateCollection` when it reads a `Resources` value (`ui-text.md`, Adding a key).
- Every click, clear, typed text and key press goes through `HeadlessInput`; a hover or a held button in
  `ThemeTests` keeps its own `PointAt`/`MouseDown` (`AGENTS.md`, Test).

## Acceptance Evidence

The defect is automatable. At HEAD the window holds the `TabControl` `PenEditorTabs`, the groups sit on
their own tab, and no control named `GroupsPanel` exists. The new
`PenEditorLayoutViewTests.TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide` reads only named
controls, so it compiles at HEAD and fails there: `Named<Border>(window, "GroupsPanel")` throws, and the
window has a `TabControl` descendant.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenEditorLayoutViewTests|FullyQualifiedName~PenListViewTests|FullyQualifiedName~PenFormPaneViewTests|FullyQualifiedName~PenGroupsViewTests|FullyQualifiedName~PenGroupsViewModelTests|FullyQualifiedName~PenEditorViewModelTests|FullyQualifiedName~PenFormViewModelTests|FullyQualifiedName~PenRowOrderTests|FullyQualifiedName~HeaderMeasuredColumnsTests|FullyQualifiedName~ThemeTests|FullyQualifiedName~ResourcesTests|FullyQualifiedName~MainWindowViewTests|FullyQualifiedName~MainWindowViewModelTests"
```

It must report 0 failed. These named tests must be present and pass:

- `ThemeTests.TheDestructiveButton_PaintsItselfFromThePalette` (theory: `Light`, `Dark`) — Task 1
- `PenGroupsViewModelTests.EachGroup_ShowsTheSelectedPensMembership` (replaces `TheMemberships_ListEveryPenInCatalogueOrderForTheSelectedGroup`) — Task 2
- `PenGroupsViewModelTests.AMembershipToggle_PutsTheSelectedPenIntoTheGroupOnceAndNamesTheGroupInTheRow` — Task 2
- `PenGroupsViewModelTests.TogglingOffAMember_TakesThePenOutAndClearsTheGroupFromTheRow` — Task 2
- `PenGroupsViewModelTests.AFailedToggle_ReRaisesIsMemberUnchangedAndReportsOnce` (rewritten) — Task 2
- `PenGroupsViewModelTests.AToggleLandingAfterThePenWasSelectedAgain_ShowsOnTheEntryThenShownAndItsNextClickUndoesIt` — Task 2
- `PenGroupsViewModelTests.RenameFromARow_OpensTheRenameBlockOnThatGroupAndClosesAPendingDeletion` — Task 2
- `PenGroupsViewModelTests.DeleteFromARow_AsksNamingTheGroupAndItsCountClosesTheRenameAndWritesNothing` — Task 2
- `PenGroupsViewModelTests.ConfirmingTheDeletion_DeletesOnceAndUpdatesTheAffectedRowsGroupText` (rewritten) — Task 2
- `PenGroupsViewModelTests.ABlankRename_NeverReachesTheEditorAndRevertsMarksAndSaysWhy` (rewritten) — Task 2
- `PenGroupsViewModelTests.ABlankRenameThenCloseRename_KeepsTheBlockOpenOnTheGroup` — Task 2
- `PenGroupsViewModelTests.AFailedRename_RevertsMarksSaysWhyAndReportsOnce` (asserts the pane's `Message` with no block open) — Task 2
- `PenGroupsViewModelTests.AFailedDeletion_KeepsTheGroupFillsTheMessageAndReportsOnce` (rewritten) — Task 2
- `PenGroupsViewModelTests.CreateGroup_WritesTheNameAndAppendsTheNewEmptyGroup` — Task 2
- `PenGroupsViewModelTests.Refresh_RebuildsTheGroupsFollowsTheSelectedPenAndClosesTheActionBlock` — Task 2
- `PenGroupsViewModelTests.GroupCalls_WaitInTheQueueBehindAHeldPenCommit` (rewritten) — Task 2
- `PenGroupsViewTests.WithNoPenSelected_EveryMembershipBoxIsDisabledAndUnchecked` — Task 2
- `PenGroupsViewTests.AMembershipBoxClicked_WritesThePenIntoTheGroupAndThePenListNamesIt` — Task 2
- `PenGroupsViewTests.AMembershipTheArchiveRefuses_PutsTheBoxBackAndReportsOnce` (rewritten) — Task 2
- `PenGroupsViewTests.AnotherPenSelected_TheBoxesShowItsGroups` — Task 2
- `PenGroupsViewTests.AGroupRow_ShowsTheNameAndTheCountWithItsTooltip` — Task 2
- `PenGroupsViewTests.ARowClick_ShowsNoSelectionHighlightAndNoCellOutline` — Task 2
- `PenGroupsViewTests.TheGroupGrid_SortsNothingOnAHeaderClick` — Task 2
- `PenGroupsViewTests.EveryGroupColumn_OpensNoNarrowerThanItsHeader` (theory: 0 and 2 groups) — Task 2
- `PenGroupsViewTests.TheRowMenu_HoldsRenameTheFoldFlagAndDelete` — Task 2
- `PenGroupsViewTests.TheFoldItemClicked_WritesOnceAndTheItemFollowsTheFlag` — Task 2
- `PenGroupsViewTests.AFoldTheArchiveRefuses_LeavesTheItemUncheckedAndReportsOnce` — Task 2
- `PenGroupsViewTests.TheFoldItemSetInCode_WritesNothingAndTheItemFollowsTheCommand` — Task 2
- `PenGroupsViewTests.RenameChosenOnTheSecondRow_OpensTheBlockOnThatGroupWithItsNameFocusedAndSelected` — Task 2
- `PenGroupsViewTests.ARenameTypedThenDoneClicked_RenamesOnceAndClosesTheBlock` — Task 2
- `PenGroupsViewTests.ABlankRenameThenDoneClicked_KeepsTheBlockOpenMarkedWithTheRule` — Task 2
- `PenGroupsViewTests.ARenameTheArchiveRefusesThenDoneClicked_ClosesTheBlockAndTheMessageLineNamesTheRefusal` — Task 2
- `PenGroupsViewTests.ARenameTypedThenAnotherRowsMenuOpened_RenamesTheFirstGroupOnce` — Task 2
- `PenGroupsViewTests.ARenameTypedThenThePanesEmptyAreaClicked_RenamesOnce` — Task 2
- `PenGroupsViewTests.ARenameEnteredWhileItsWriteIsHeldThenDoneClicked_RenamesOnce` — Task 2
- `PenGroupsViewTests.ARenameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing` (rewritten) — Task 2
- `PenGroupsViewTests.DeleteChosen_AsksNamingTheGroupAndItsCountAndCancelWritesNothing` — Task 2
- `PenGroupsViewTests.ADeleteConfirmed_DeletesThatGroupAndItsRowGoes` — Task 2
- `PenGroupsViewTests.TheActionBlock_KeepsItsBoundsEmptyRenamingAndAskingToDelete` — Task 2
- `PenGroupsViewTests.TheConfirmButton_IsDestructiveFromThePaletteNotSemisDanger` — Task 2
- `PenGroupsViewTests.ANewNameCreated_AppendsTheGroupAndClearsTheField` — Task 2
- `PenGroupsViewTests.AFullCatalogue_RealisesOnlyTheGroupRowsOnScreen` — Task 2
- `PenGroupsViewTests.TheGroupColumns_LeaveTheNameAtLeast100PxInInter` (theory: `en`, `ru`) — Task 2
- `PenGroupsViewTests.ThePaneTexts_FitTheirLinesInInter` (theory: `en`, `ru`) — Task 2
- `PenEditorViewTests.ARefreshThatChangesTheCatalogue_RebindsThePenListAndTheGroupsPane` (replaces `ARefreshThatChangesTheCatalogue_RebindsTheTableAndTheGroupsTab`) — Task 2
- `PenEditorLayoutViewTests.TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide` (the reproduction) — Task 3
- `PenEditorLayoutViewTests.TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart` (replaces `PenEditorViewTests.TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart`) — Task 3
- `PenEditorLayoutViewTests.AtTheMinimumWidth_BothPanesKeepTheirWidthAndThePenListScrolls` (replaces `PenFormPaneViewTests.TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum`) — Task 3
- `PenEditorLayoutViewTests.BothPanes_StayFixedWhenTheWindowWidens` (replaces `PenFormPaneViewTests.ThePane_Stays420PxWideWhenTheWindowWidens`) — Task 3
- `PenFormPaneViewTests.AtTheOpeningSize_ThePaneShowsTheWholeForm` (unchanged, now at 1440 x 720) — Task 3
- `ThemeTests.EveryListSurface_PaintsItselfFromThePalette` (replaces `EveryTabAndListSurface_PaintsItselfFromThePalette`) — Task 3
- `PenEditorLayoutViewTests.TheOpeningWidth_TakesTheDesignedWidthWhereTheWorkingAreaAllowsAndNeverLessThanTheMinimum` (theory) — Task 4
- `PenEditorLayoutViewTests.AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows` — Task 4
- `PenListViewTests.AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter` (theory: `en`, `ru`) — Task 4
- `MainWindowViewTests.EditPensAndGroups_ClickedOnTheRealisedWindow_OpensTheEditorOverIt` (asserts the opening width) — Task 4
- `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries` (passes with the new keys) — Task 2

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the two NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -n "SelectedGroup\|DeleteGroupCommand\|_membershipsOfGroup\|_isReplacingGroups\|PenEditorTabs\|PensTab\|GroupsTab\|PenEditorBottomBar\|OpenGroupsTab\|GroupAt(\|MembershipList\|MembershipColumns\|\bIdColumnWidth\b\|PenGroupsMembersHeader\|PenGroupsDeleteConfirmation\|TabItemLine\|TabControlSeparator" -- SemiPlot docs/architecture   # prints nothing
git grep -n "\.Memberships\b" -- SemiPlot/SemiPlot.UI/PenEditor SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor   # prints nothing
git grep -n "Classes=\"Danger\"\|\.Danger" -- SemiPlot/SemiPlot.UI   # prints nothing
```

The first frame of the opened window and the menu's look cannot be seen headlessly; the manual checks
under Post-Completion cover them.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The membership entry keeps its write path; only who caches it changes.** `PenMembershipViewModel` is
  already one pen and one group with its own toggle (`PenMembershipViewModel.cs:13-52`), so it stays as it
  is, and so does `PenGroupWriter`. What inverts is the cache: `PenGroupsViewModel` keys entries by the
  pair (row, group) instead of one array of every pen per selected group (`PenGroupsViewModel.cs:139-153`),
  and hands each `PenGroupViewModel` the entry of the selected pen through a new `Membership` property, null
  with no pen selected. The entry of a pen and a group lives as long as the groups pane, so the box shown
  when a pen is selected again is the entry whose toggle may still run: it stays disabled until the toggle
  lands and then shows it, the invariant `AToggleLandingAfterTheGroupWasSelectedAgain_...` pins today with the
  sides swapped. A delete disposes the entries of its group.
- **The selected pen reaches the groups pane as an observable.** `PenGroupsViewModel` takes an
  `IObservable<PenRowViewModel?>`, which `PenEditorViewModel` passes as
  `this.WhenAnyValue(editor => editor.SelectedRow)`. One pipeline, the incoming observable combined with
  `this.WhenAnyValue(pane => pane.Groups)` through `CombineLatest`, assigns every group's `Membership` in
  its `Subscribe`; no property mirrors the selected pen, and the pane is `Membership`'s one writer. A
  non-public property read through `WhenAnyValue` would rest on ReactiveUI's reflection reaching it, so
  there is none. The subscription is disposed with the pane, whose lifetime
  `PenEditorViewModel` owns: it disposes the replaced pane on refresh and the last one in `Dispose`
  (`PenEditorViewModel.cs:114-118`, `:159-169`). The refresh builds the new pane after it sets
  `SelectedRow`, so the new pane's first membership is the restored pen's.
- **No row selection, so no selection state.** `SelectedGroup`, its `_isReplacingGroups` guard and
  `ReplaceGroups` (`PenGroupsViewModel.cs:81-91`, `:222-237`) existed for the `ListBox`'s two-way selection;
  the grid binds no selection, so all three go, `Groups` is assigned directly, and `Memberships`,
  `_membershipsOfGroup` and `DeleteGroupCommand` go with them.
- **Row actions are commands on the row's view model.** `PenGroupViewModel` gains `AskRenameCommand` and
  `AskDeleteCommand` (`ReactiveCommand.Create`), each calling a callback the pane hands in at construction,
  the shape of the writer's `showGroupsOn` callback (`PenGroupWriter.cs:16-21`). The menu's fold item is the
  existing `ToggleFoldedOnStartCommand` on a `MenuItem ToggleType="CheckBox"` that reads `FoldedOnStart`
  `Mode=OneWay` (`AGENTS.md`, the checkable menu item rule). ASSUMPTION: a `MenuFlyout` attached to a button
  inside a cell `DataTemplate` gives its items the row's `DataContext`; the menu tests act on the second row
  and assert the second group, and if they fail the fallback is a `Click` handler per item in the
  code-behind that reads the group from the flyout's target button (`MenuFlyout.Target.DataContext`) and
  invokes the same command, recorded under Task 2.
- **One reserved block, one action at a time.** `PenGroupsViewModel` holds `PendingRename`
  (`PenGroupViewModel?`) beside the existing `PendingDeletion`; asking one clears the other, and a refresh
  builds a pane with neither. The block shows the rename box while `PendingRename` is set and the
  confirmation while `PendingDeletion` is set. `GroupRename` keeps its name and binds
  `DataContext="{Binding PendingRename}"`, so the code-behind's rename route (`PenEditorWindow.axaml.cs:150`,
  `:320`) does not change. `Message` reads `PendingRename`'s message, else the writer's refusal.
- **The rename block closes on Done, not on Cancel.** An edit writes when it ends, and a press on any button
  ends it first (`PenEditorWindow.axaml.cs:128-154`), so a Cancel button could not stop the write it
  suggests it stops. The block offers Готово: its press ends the edit, which writes as every field does, and
  its command clears `PendingRename` only while `PendingRename.IsNameValid` holds. A blank draft is refused
  synchronously when the edit ends, before the click lands, so it stays in the open block, reverted and
  marked, with the rule on the message line. A rename the archive refuses lands after the block has closed:
  `PenGroupViewModel.EndRenameAsync` reports it through `PenGroupWriter.RefuseWrite`, so the pane's message
  line names the refusal with the block open or closed, the panel gets one entry, and the next
  Переименовать on that group opens the box reverted and marked. The block also closes when another row
  action replaces it and on refresh.
- **The delete confirmation keeps the count visible.** The single confirmation key becomes three lines, each
  20 px: `PenGroupsDeleteQuestion` ("Delete group '{0}'?", trimmed with the full text as a tooltip),
  `PenGroupsDeleteCount` ("Pens in the group: {0}") and `PenGroupsDeleteKeepsPens` ("The pens themselves
  stay."). A long group name then trims its own line and never pushes the count or the note past the reserved
  height. `PendingGroupDeletion` carries `Question` and `Count` instead of `Text`; the note is a static key.
- **The groups grid sorts nothing.** `CanUserSortColumns="False"`: groups stay in the order the catalogue
  reads them, and a created group appends at the end until the next refresh. A short list read in name order
  needs no sort, and a sort would need a collection view that survives the list's replacement on create and
  delete, which the pen list already pays for (`overview.md:538-549`).
- **Column widths.** The checkbox column opens 36 px and Действия at `max(40, header)`; neither resizes,
  because each holds one fixed-size control (a checkbox, a 28 px button at the 12 px cell inset) and a wider
  column shows nothing more. Группа is the `HeaderMeasuredColumns.IsFill` column and resizes. It stands in
  the middle, not last as the desktop-ui rule prefers (`SKILL.md:163-164`), because the roadmap fixes the order
  (`20261102-visual-chore-roadmap.md:80-82`): the tick a membership list is read by stands first and the
  row's menu last; `FitFillColumn` supports a fill column anywhere (`HeaderMeasuredColumns.cs:140-159`), and
  Действия keeps the last grab edge.
- **No HeadersMeasured latch case.** `HeadersMeasured` latches after the first measure
  (`HeaderMeasuredColumns.cs:95-98`), so a column added or shown later would keep no header-derived minimum.
  The groups grid declares its three columns statically, binds no column `IsVisible`, and disables rather
  than hides the membership boxes. Inside the `Groups` tab of Task 2 the grid attaches when the tab opens and
  measures then; from Task 3 it is in the window from the first layout. An empty catalogue of groups still
  realises the headers. `EveryGroupColumn_OpensNoNarrowerThanItsHeader` gates both counts, 0 and 2 groups.
  `HeaderMeasuredColumns.cs` does not change.
- **No selection highlight, no cell outline.** The cell-outline rule and the header font become window-wide
  (`DataGridCell:current ...`, `DataGridColumnHeader`), covering both grids. Two window styles on Semi's own
  `:selected` trigger repaint a selected group row as an unselected one:
  `DataGrid#GroupList DataGridRow:selected /template/ Border#BackgroundBorder` takes
  `{DynamicResource DataGridRowBackground}`, and the same selector with `:selected:pointerover` takes
  `{DynamicResource DataGridRowPointeroverBackground}`; a selector without the pseudo-class loses to Semi's
  trigger, as the pen list found (`ui-theme.md:101-108`). ASSUMPTION: Semi paints a row at rest and hovered
  from those two keys on `Border#BackgroundBorder`; `ARowClick_ShowsNoSelectionHighlightAndNoCellOutline`
  compares a clicked row with an unclicked one at rest and hovered, and if it fails the marker probe of Task 1
  finds the part and key, recorded under Task 2.
- **The membership box.** `IsChecked="{Binding Membership.IsMember, Mode=OneWay, FallbackValue=False}"`,
  `Command="{Binding Membership.ToggleMembershipCommand}"`,
  `IsEnabled="{Binding Membership, Converter={x:Static ObjectConverters.IsNotNull}}"`: with no pen the box is
  disabled and unchecked, never indeterminate, and while a write runs the command's `IsExecuting` disables
  it. ASSUMPTION: a compiled binding through a null `Membership` yields the fallback without a throw;
  `WithNoPenSelected_EveryMembershipBoxIsDisabledAndUnchecked` checks it, and if the box reads null the
  binding adds `TargetNullValue=False`, recorded under Task 2. ASSUMPTION: a click on a `CheckBox` inside a
  read-only template cell reaches the box before the row's selection handling;
  `AMembershipBoxClicked_WritesThePenIntoTheGroupAndThePenListNamesIt` checks it; if the box does not
  toggle, the run records the observed event order as a ⚠️ under Task 2 and implements the toggle through a
  PointerPressed handler that invokes the row's toggle command, recorded as a deviation.
- **The destructive button is a class with palette keys.** `Styles/Buttons.axaml`, included from `App.axaml`,
  styles `Button.destructive` at rest, `:pointerover` and `:pressed` from four new App keys; the state arms
  need Semi's pseudo-classes to win over its triggers. Task 1's marker probe decides whether the setters
  target the `Button` or its `ContentPresenter#PART_ContentPresenter`, and the theme test reads the painted
  surface. The button is visible only while `PendingDeletion` is set, which is also its `canExecute`, and
  `ConfirmDeleteAsync` clears `PendingDeletion` before it awaits (`PenGroupsViewModel.cs:188-196`), so the
  button is disabled only while a refresh disables the pane with a confirmation pending; the class's rest arm
  carries `:not(:disabled)` (Task 1's probe: without it the arm beats Semi's `:disabled` trigger), so it then
  shows Semi's disabled brushes and needs no arm of its own. Semi's `Danger` class is not used.
- **The window's size.** `PenEditorWindow` declares `DesignedWidth = 1440` and `MinimumWidth = 1272` and a
  pure `OpeningWidthFor(double workingAreaWidth)` that returns
  `Math.Max(MinimumWidth, Math.Min(DesignedWidth, workingAreaWidth))`. The opener sets the width before it
  shows the window: `MainWindow`'s factory passes the owner screen's working area in device-independent
  pixels (`Screens.ScreenFromWindow(this)`, `WorkingArea.Width / Scaling`), because `CenterOwner` centres the
  width the window has when it is shown, and a width changed after `Show` would move the window's right edge
  only. With no screen reported the window opens at `DesignedWidth`. The working area counts the frame, so
  on a screen narrower than 1440 px the client width equals the area's width and the frame overhangs by its
  border. `Screens.ScreenFromWindow` is present in Avalonia 12.0.5 (verified 2026-11-03 by a string search
  of `avalonia/12.0.5/lib/net10.0/Avalonia.Controls.dll`, beside `ScreenFromVisual` and
  `ScreenFromTopLevel`); if the build rejects the call, `Screens.ScreenFromTopLevel(this)` replaces it.
- **MinHeight is the fixed rows plus four list rows.** Window 16 + top bar 32 + 16 + groups pane + 16. The
  groups pane: 2 border + 32 padding + title 24 + 4 + subtitle 20 + 8 + new-group row 32 + 8 + list + 8 +
  block 116 + 8 + message 40 = 302 + list; the list's border, padding, 32 px header and four 28 px rows are
  2 + 8 + 32 + 112 = 154. That is 80 + 456 = 536 px, so `MinHeight="536"`. The block is as tall as its
  tallest state, the confirmation: question 20 + 8 + count 20 + 8 + note 20 + 8 + buttons 32 = 116. Below
  702 px the pen form scrolls inside its pane, which it was built to do (`ui-theme.md:483-486`); a
  `MinHeight` of 702 would not fit a 1366 x 768 screen's working area less its title bar. The opening `Height` stays 720: 16 + 32 + 16 + 622 +
  16 = 702 fits, so `AtTheOpeningSize_ThePaneShowsTheWholeForm` keeps passing.
- **MinWidth and the opening widths.** At 1272 px the pen list's border box is 1272 - 4 x 16 - 420 - 340 =
  448 px, its grid 438 px, which holds Id, Name and Unit at their opening widths (84 + 200 + 96 = 380 px): the
  first columns, as the rule asks (`SKILL.md:167-171`). The rest scrolls horizontally, and the panes keep
  their widths. At 1440 px the grid is 1440 - 64 - 760 - 10 = 606 px. The pen list's Name column opens at
  200 px instead of 220, so the four fixed columns (84 + 200 + 96 + 120 = 500 px) leave the Groups fill
  column about 96 px with a vertical scroll bar shown, above its header minimum, and the list opens with no
  horizontal scroll at the designed width (`SKILL.md:59`). `AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter`
  gates it.
- **The groups pane's arithmetic.** 340 - 2 x 16 padding = 308 px of content; the list's 1 px border and 4 px
  padding on each side leave the grid 298 px, less the vertical scroll bar while it shows. The checkbox column
  is 36, Действия its realised header (Semi's 12 px left padding, the text at 12 px SemiBold, the 16 px arrow
  slot and its 8 px margin, `ui-theme.md:425-428`), and Группа the rest. Expected in Inter: Действия about
  92 px in `ru` and 84 px in `en`, so Группа about 160 px in `ru`; its name text gets Группа - 12 - 8 (the
  cell insets) - 8 (the gap) - the count's width, about 110 px. `TheGroupColumns_LeaveTheNameAtLeast100PxInInter`
  gates the floor of 100 px and Task 6 records the measured values.
- **Window margin 16.** This slice rebuilds the window's composition, so the root margin moves from 12 to
  the scale's 16 (`SKILL.md:95`), and the top bar's two controls stand 8 apart (`SKILL.md:98`). The refresh
  button keeps `Padding="12,2"`, which the `ui-spacing-scale` slice owns.
- **Dead keys go.** No control reads the five tab keys once the `TabControl` goes, so they leave both
  variants and the key table (`ui-theme.md:29-33`: a key belongs in the palette when a control reads it).
  The `ListBoxItem` keys stay for the export window's list (`CsvExportWindow.axaml:149`).

## Technical Details

### Palette and the destructive style

`Styles/Palette.axaml`, both variants, beside the other App keys:

| Key | Light | Dark |
| --- | --- | --- |
| `AppDestructiveBackgroundBrush` | `#C7303F` | `#C7303F` |
| `AppDestructivePointeroverBackgroundBrush` | `#B02A38` | `#B02A38` |
| `AppDestructivePressedBackgroundBrush` | `#992431` | `#992431` |
| `AppDestructiveForegroundBrush` | `#FFFFFF` | `#FFFFFF` |

White text on the three backgrounds measures about 5.4:1, 6.5:1 and 7.9:1 (WCAG relative luminance), above
the 4.5:1 a 14 px caption needs; `#DB3B4B`, the error brush, gives 4.4:1 and is not used for a filled
button. The values are equal across the variants, as the accent is, because a filled button carries its
own ground. The theme test asserts the ratio, so a later edit of a value keeps it.

`Styles/Buttons.axaml` (new), included in `App.axaml` after `Forms.axaml`:

```xml
<Styles xmlns="https://github.com/avaloniaui"
        xmlns:x="http://schemas.microsoft.com/winfx/2006/xaml">
    <Style Selector="Button.destructive">  <!-- Background, Foreground; the probe decides Button or /template/ ContentPresenter#PART_ContentPresenter -->
    <Style Selector="Button.destructive:pointerover"> ... AppDestructivePointeroverBackgroundBrush, AppDestructiveForegroundBrush
    <Style Selector="Button.destructive:pressed"> ... AppDestructivePressedBackgroundBrush, AppDestructiveForegroundBrush
</Styles>
```

### Resources

| Key | English | Russian | Use |
| --- | --- | --- | --- |
| `PenGroupsTitle` | Groups | Группы | the pane title; renamed from `PenEditorGroupsTab`, value unchanged |
| `PenGroupsSubtitle` | The selected pen's groups are ticked | Отмечены группы выбранного пера | the subtitle, one line |
| `PenGroupsColumnGroup` | Group | Группа | the name column's header |
| `PenGroupsColumnActions` | Actions | Действия | the action column's header |
| `PenGroupsMemberCountTip` | Pens in the group | Перьев в группе | the count's tooltip |
| `PenGroupsActionsTip` | Group actions | Действия с группой | the "..." button's tooltip |
| `PenGroupsRename` | Rename | Переименовать | the menu item |
| `PenGroupsRenameDone` | Done | Готово | the rename block's button |
| `PenGroupsDeleteQuestion` | Delete group '{0}'? | Удалить группу «{0}»? | the confirmation's first line |
| `PenGroupsDeleteCount` | Pens in the group: {0} | Перьев в группе: {0} | the confirmation's second line |
| `PenGroupsDeleteKeepsPens` | The pens themselves stay. | Сами перья останутся. | the confirmation's third line |

- Reused: `PenGroupsNewNamePlaceholder`, `PenGroupsCreate`, `PenGroupsNameLabel` (the rename block's label;
  the box shows the group's name, so the label need not repeat it), `PenGroupsFoldedOnStartLabel` (the menu's
  checkable item), `PenGroupsDelete` (the menu item and the confirm button: one term), `PenGroupsCancel`,
  `PenGroupsNameRequired`.
- Deleted, every consumer gone: `PenGroupsDeleteConfirmation` and `PenGroupsMembersHeader` (Task 2),
  `PenEditorPensTab` (Task 3).
- Both resx files are hand-edited, no byte order mark (`ui-text.md`, Adding a key).

### Glyph

`Styles/Icons.axaml` gains a 16 x 16 geometry; it is not text, so it is not a resource string:

```xml
<StreamGeometry x:Key="MoreGeometry">M3,6.5 A1.5,1.5 0 1 1 3,9.5 A1.5,1.5 0 1 1 3,6.5 Z M8,6.5 A1.5,1.5 0 1 1 8,9.5 A1.5,1.5 0 1 1 8,6.5 Z M13,6.5 A1.5,1.5 0 1 1 13,9.5 A1.5,1.5 0 1 1 13,6.5 Z</StreamGeometry>
```

### View models

```csharp
public sealed record PendingGroupDeletion(PenGroupViewModel Group, string Question, string Count);

public sealed class PenGroupsViewModel : ReactiveObject, IDisposable
{
	public PenGroupsViewModel(
		IReadOnlyList<PenRowViewModel> rows,
		IReadOnlyList<StoredGroup> groups,
		IObservable<PenRowViewModel?> selectedRow,
		IPenCatalogueEditor penCatalogueEditor,
		ArchiveCallQueue archiveCallQueue,
		MessagePanelViewModel messagePanel,
		ILogger logger);

	public IReadOnlyList<PenGroupViewModel> Groups { get; private set; }   // replaced on create and delete
	public string NewGroupName { get; set; }
	public PenGroupViewModel? PendingRename { get; private set; }
	public PendingGroupDeletion? PendingDeletion { get; private set; }
	public string Message { get; }                                          // PendingRename's message, else the refusal
	public ReactiveCommand<Unit, Unit> CreateGroupCommand { get; }
	public ReactiveCommand<Unit, Unit> CloseRenameCommand { get; }          // canExecute: PendingRename is not null; clears it only while its name is valid
	public ReactiveCommand<Unit, Unit> ConfirmDeleteCommand { get; }
	public ReactiveCommand<Unit, Unit> CancelDeleteCommand { get; }
	// private: Dictionary<(PenRowViewModel, PenGroupViewModel), PenMembershipViewModel>, AskToRename(group),
	// AskToDelete(group), the Membership pipeline: selectedRow.CombineLatest(WhenAnyValue(Groups)).Subscribe
}
```

- `PenGroupViewModel`: the internal constructor becomes
  `(StoredGroup group, PenGroupWriter penGroupWriter, Action<PenGroupViewModel> askToRename,
  Action<PenGroupViewModel> askToDelete)`; new members `PenMembershipViewModel? Membership { get; internal
  set; }` (raises), `AskRenameCommand`, `AskDeleteCommand`; `Dispose` disposes the three commands;
  `EndRenameAsync` reports an archive refusal through `_penGroupWriter.RefuseWrite` instead of
  `ReportFailure` (`PenGroupViewModel.cs:106`). Nothing else changes, `SetMembership` and the rename queue
  included.
- `AskToDelete(group)` sets `PendingRename = null` and `PendingDeletion` from
  `Resources.FormatPenGroupsDeleteQuestion(group.Group.Name)` and
  `Resources.FormatPenGroupsDeleteCount(group.MemberCount)`. `AskToRename(group)` sets
  `PendingDeletion = null` and `PendingRename = group`.
- `ConfirmDeleteAsync` keeps its order (`PenGroupsViewModel.cs:188-220`) less the selection lines, and
  disposes the deleted group's membership entries.
- `CreateGroupAsync` appends the group and selects nothing.
- `PenEditorViewModel.RefreshAsync`: refill the rows, set `SelectedRow`, then build the pane with
  `GroupsOf(rows, read.Value.Groups)`, assign `Groups`, dispose the replaced pane. `GroupsOf` passes
  `this.WhenAnyValue(editor => editor.SelectedRow)`. The selected-group lines (`:155`, `:158`) go.
- `PenGroupsViewModel` stays under the 300-line cap; if it crosses, `PendingGroupDeletion` and the membership
  cache move into a file of their own.
- `PenMembershipViewModel.cs` and `PenGroupWriter.cs` change in their `<summary>` lines only (each names "the
  groups tab" or "the selected group").

### The groups pane

```xml
<!-- IsEnabled reads the editor and DataContext switches to the pane, so they sit on two elements,
     as today's tab content does (PenEditorWindow.axaml:492-497) -->
<Border Name="GroupsPanel" Width="340" Padding="16"
        BorderBrush="{DynamicResource AppBorderBrush}" BorderThickness="1" CornerRadius="4"
        IsEnabled="{Binding !IsRefreshing}">
  <Panel DataContext="{Binding Groups}">
    <Grid x:DataType="local:PenGroupsViewModel" RowDefinitions="Auto,Auto,*,Auto,Auto" RowSpacing="8">
        <StackPanel Grid.Row="0" Spacing="4">
            <TextBlock Name="GroupsTitle" Classes="form-title" Height="24" Text="{x:Static text:Resources.PenGroupsTitle}" />
            <TextBlock Name="GroupsSubtitle" Height="20" TextTrimming="CharacterEllipsis"
                       Foreground="{DynamicResource AppSecondaryForegroundBrush}"
                       Text="{x:Static text:Resources.PenGroupsSubtitle}" />
        </StackPanel>
        <Grid Grid.Row="1" ColumnDefinitions="*,Auto" ColumnSpacing="8">  <!-- NewGroupName, CreateGroupButton, as today -->
        <Border Grid.Row="2" BorderBrush="{DynamicResource AppBorderBrush}" BorderThickness="1" CornerRadius="4" Padding="4">
            <DataGrid Name="GroupList" ItemsSource="{Binding Groups}" IsReadOnly="True" SelectionMode="Single"
                      HeadersVisibility="Column" GridLinesVisibility="None" CanUserSortColumns="False"
                      CanUserResizeColumns="True" CanUserReorderColumns="False"
                      RowHeight="{x:Static local:PenEditorWindow.RowHeight}"
                      tables:HeaderMeasuredColumns.FitsColumns="True">  <!-- three DataGridTemplateColumns, below -->
        </Border>
        <Panel Grid.Row="3" Name="GroupActionBlock" Height="116">  <!-- GroupRenameBlock | GroupDeletionBlock -->
        <TextBlock Grid.Row="4" Name="GroupsMessage" Classes="form-message error" Text="{Binding Message}" />
    </Grid>
  </Panel>
</Border>
```

| Column | Header | Width | Resize | Cell |
| --- | --- | --- | --- | --- |
| Membership | none | 36 | no | `CheckBox` `MembershipBox`, centred, the three bindings of Solution Overview |
| Group | `PenGroupsColumnGroup` | 120, then the fill rule (`IsFill`) | yes | `Grid` `*,Auto`, `ColumnSpacing="8"`, `Margin="12,0,8,0"`: `TextBlock.grid-cell` `GroupName` (`Group.Name`, `ToolTip.Tip="{Binding $self.Text}"`), `TextBlock` `GroupMemberCount` (`MemberCount`, `AppSecondaryForegroundBrush`, `HorizontalAlignment="Right"`, `VerticalAlignment="Center"`, `ToolTip.Tip` `PenGroupsMemberCountTip`) |
| Actions | `PenGroupsColumnActions` | 40, raised to its header | no | `Button` `GroupActionsButton`, `Theme="{DynamicResource BorderlessButton}"`, 28 x 24, `Padding="0"`, `Margin="12,0,0,0"`, `HorizontalAlignment="Left"`, `ToolTip.Tip` `PenGroupsActionsTip`, content `PathIcon` 16 x 16 `MoreGeometry` in `AppSecondaryForegroundBrush`; `Button.Flyout` a `MenuFlyout` `Placement="BottomEdgeAlignedLeft"` |

- Every column sets `CanUserSort="False"`; the membership and action columns `CanUserResize="False"`.
- The menu, in order: `MenuItem` `GroupRenameItem` (`PenGroupsRename`, `Command="{Binding AskRenameCommand}"`,
  `Click="OnRenameItemClick"`); `MenuItem` `GroupFoldItem` (`PenGroupsFoldedOnStartLabel`,
  `ToggleType="CheckBox"`, `IsChecked="{Binding FoldedOnStart, Mode=OneWay}"`,
  `Command="{Binding ToggleFoldedOnStartCommand}"`); `Separator`; `MenuItem` `GroupDeleteItem`
  (`PenGroupsDelete`, `Command="{Binding AskDeleteCommand}"`).
- (Task 2 took the fallback below: the template's `Click` never runs.) `OnRenameItemClick` posts `GroupRename.Focus()` and `GroupRename.SelectAll()` at
  `DispatcherPriority.Background`, after the item's command has set `PendingRename` and the block has been
  laid out (`~/.claude/rules/avalonia.md`, UI thread). The handler is input interop only; the command decides.
  ASSUMPTION: a handler named inside a cell template's `MenuFlyout` hooks to the window's code-behind, and
  the posted focus lands after the flyout restores the focus on close;
  `RenameChosenOnTheSecondRow_OpensTheBlockOnThatGroupWithItsNameFocusedAndSelected` checks both. If either
  fails, `GroupRenameItem` loses its `Click`, and the code-behind focuses and selects `GroupRename` at
  `DispatcherPriority.Background` when `GroupRename.DataContext` changes to a `PenGroupViewModel`, recorded
  under Task 2.
- `GroupRenameBlock`, a `StackPanel` `Spacing="8"` on the pane's data context, visible while
  `PendingRename` is not null: the label `TextBlock.form-label` `PenGroupsNameLabel`, `Height="20"`; a
  `Grid` `*,Auto` 8 apart: in column 0 a `Panel` with `DataContext="{Binding PendingRename}"` holding the
  `TextBox` `GroupRename` (`x:DataType="local:PenGroupViewModel"`, `Text="{Binding RenameDraft}"`,
  `Classes.invalid="{Binding !IsNameValid}"`), the wrapper-then-box shape of today's
  `PenEditorWindow.axaml:568-576`, so the code-behind's `GroupRename.DataContext is PenGroupViewModel` check
  holds; in column 1 the `Button` `GroupRenameDoneButton` (`PenGroupsRenameDone`,
  `Command="{Binding CloseRenameCommand}"`).
- `GroupDeletionBlock`, a `StackPanel` `Spacing="8"`, visible while `PendingDeletion` is not null:
  `TextBlock` `GroupDeletionQuestion` (`PendingDeletion.Question`, 20 px, trimmed, tooltip), `TextBlock`
  `GroupDeletionCount` (`PendingDeletion.Count`, 20 px, trimmed), `TextBlock` `GroupDeletionNote`
  (`PenGroupsDeleteKeepsPens`, 20 px, trimmed, `AppSecondaryForegroundBrush`), and a right-aligned horizontal
  `StackPanel` `Spacing="8"` with `Button` `ConfirmDeleteButton` (`Classes="destructive"`, `PenGroupsDelete`,
  `ConfirmDeleteCommand`) and `Button` `CancelDeleteButton` (`PenGroupsCancel`, `CancelDeleteCommand`).
- Window styles added: the two `GroupList` selection styles of Solution Overview. Window styles changed: the
  header font becomes `DataGridColumnHeader`, the outline rule `DataGridCell:current /template/
  Rectangle#CurrencyVisual, DataGridCell:focus /template/ Panel#FocusVisual`. Removed: `TextBlock.cell`,
  `ListBox#GroupList ListBoxItem`, `IdColumnWidth`, `MembershipColumns`.

### The window (Task 3 and Task 4)

```xml
<Window ... Width="{x:Static local:PenEditorWindow.DesignedWidth}"
            MinWidth="{x:Static local:PenEditorWindow.MinimumWidth}" Height="720" MinHeight="536">
    <Grid Margin="16" RowDefinitions="Auto,*" RowSpacing="16">
        <Grid Grid.Row="0" Name="PenEditorTopBar" ColumnDefinitions="*,Auto,Auto" ColumnSpacing="8">
            <!-- column 0 stays empty for the filter; PenEditorAddedCount in 1, PenEditorRefreshButton in 2 -->
        </Grid>
        <Grid Grid.Row="1" ColumnDefinitions="*,16,Auto,16,Auto">
            <!-- the bordered PenList | PenFormPanel (420) | GroupsPanel (340) -->
        </Grid>
    </Grid>
</Window>
```

`MinHeight="536"` and the `OpeningWidthFor` call arrive in Task 4; Task 3 carries the composition and the two
width constants.

### Test helpers

- `PenEditorWindowDriver.GroupListOf(window)` returns `Named<DataGrid>(window, "GroupList")`.
- `PenEditorWindowDriver.GroupRowAt(window, index)` returns the realised `DataGridRow` of `GroupList` whose
  `Index` is `index`, scrolling it into view first; `RowAt` and `GroupRowAt` share one private
  `RowOf(DataGrid, int)`. `RowOf` scrolls to `ItemsSource.Cast<object>().ElementAt(index)`, since `PenList`
  binds a `DataGridCollectionView` and `GroupList` a plain list.
- `PenEditorWindowDriver.PickGroupMenuItem(window, index, itemName)` clicks the row's `GroupActionsButton`,
  asserts its `MenuFlyout` is open, finds the `MenuItem` by name among `menu.Popup.Child`'s visual
  descendants and clicks it on its own `TopLevel`, the shape of `ChartMarkerViewTests.cs:905-914`.
- `OpenGroupsTab` and `GroupAt` are deleted in Task 3.
- `InterMeasure.InterTypeface` gains a parameter, `InterTypeface(string face = "Regular")`, which loads
  `Inter-{face}.ttf`, so today's callers stay unchanged; `InterMeasure` also gains
  `HeaderWidthInInter(Window window, DataGridColumnHeader header, SKTypeface semiBold)`: the header's
  unconstrained width less its text's unconstrained headless width (both through
  `ViewProbe.UnconstrainedWidthOf`), plus the header text measured in Inter SemiBold at the header's
  `FontSize`. Both Inter width tests call it.

## What Goes Where

- **Implementation Steps**: the palette keys and the destructive style, the view-model reshape, the groups
  pane, the window's composition and sizes, their tests, and the `overview.md`, `ui-theme.md`, `ui-text.md`
  and `charting.md` updates.
- **Post-Completion**: the manual checks in the running viewer, the remaining roadmap slices.

## Implementation Steps

### Task 1: Add the destructive button style and its palette keys

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Create: `SemiPlot/SemiPlot.UI/Styles/Buttons.axaml`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] run a scratch marker probe: a `Button` with `Classes="destructive"` and a window style that sets each
      candidate (`Button.Background`, `Button.Foreground`, and the same on
      `/template/ ContentPresenter#PART_ContentPresenter`) to a distinct marker colour, at rest, hovered
      (`PointAt`) and pressed (`MouseDown`); read back which element's brush paints the surface in each
      state; delete the scratch test and note the finding as a ⚠️ in this task
- ⚠️ probe finding: Semi paints every button state on `ContentPresenter#PART_ContentPresenter`. A setter on the
  `Button` paints the rest state only; Semi's hover and press triggers repaint the presenter's background over
  it (`#2E3238` at 0.09 and 0.13). Setters on the presenter win in every state, but a rest arm without a
  pseudo-class also beats Semi's `:disabled` trigger, so the rest arm is
  `Button.destructive:not(:disabled) /template/ ContentPresenter#PART_ContentPresenter`, and a disabled
  destructive button then paints what a disabled default button paints (`#2E3238` at 0.05, caption
  `#A8ADBD` in Light)
- [x] write the failing theory `TheDestructiveButton_PaintsItselfFromThePalette` (`Light`, `Dark`): a
      `Button { Classes = { "destructive" }, Content = "Delete" }` in a shown `Window`; at rest, hovered and
      pressed the painted surface the probe named reads `#C7303F`, `#B02A38`, `#992431` and the caption
      `#FFFFFF`; for each state the WCAG contrast of caption to background is at least 4.5; and a disabled
      `destructive` button paints what a disabled default button paints
- [x] add the four `AppDestructive*` keys to both variants of `Palette.axaml`
- [x] create `Styles/Buttons.axaml` with the three styles, setters on the element the probe named; add
      `<StyleInclude Source="avares://SemiPlot.UI/Styles/Buttons.axaml" />` to `App.axaml` after `Forms.axaml`
- [x] run `ThemeTests`: every case passes

### Task 2: Replace the groups tab's content with the groups pane

The pane is built inside the existing `Groups` tab, so every task ends green; Task 3 moves it beside the pen
pane.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenMembershipViewModel.cs` (summary only)
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupWriter.cs` (summary only)
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Icons.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorWindowDriver.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/InterMeasure.cs`

- [x] rewrite `PenGroupsViewModelTests` against the members of Technical Details (red: they do not compile).
      New or rewritten, over `_catalogue` `[Argon, Pressure, Power]` with `[Chamber, Gas]`
      (`PenGroupsViewModelTests.cs:32`) and `editor.SelectedRow = RowsOf(editor)[i]`:
      `EachGroup_ShowsTheSelectedPensMembership` (no pen: every `Membership` null; Argon: Chamber and Gas
      members; Power: neither; Pressure: Chamber only);
      `AMembershipToggle_PutsTheSelectedPenIntoTheGroupOnceAndNamesTheGroupInTheRow` (Power, Gas:
      `SetMembership(Power, Gas, true)`, `IsMember`, `MemberCount` 2, `RowsOf(editor)[2].GroupsText` "Gas");
      `TogglingOffAMember_TakesThePenOutAndClearsTheGroupFromTheRow` (Pressure, Chamber);
      `AFailedToggle_ReRaisesIsMemberUnchangedAndReportsOnce`;
      `AToggleLandingAfterThePenWasSelectedAgain_ShowsOnTheEntryThenShownAndItsNextClickUndoesIt` (Power,
      Gas held; select Argon, then Power again: the entry shown is the same instance, raises `IsMember` when
      the toggle lands, and its next toggle writes `false`);
      `RenameFromARow_OpensTheRenameBlockOnThatGroupAndClosesAPendingDeletion`;
      `DeleteFromARow_AsksNamingTheGroupAndItsCountClosesTheRenameAndWritesNothing` (`Question` and `Count`
      from the two format keys, Chamber, 2);
      `ConfirmingTheDeletion_DeletesOnceAndUpdatesTheAffectedRowsGroupText` (through `AskDeleteCommand`;
      `PendingDeletion` null, the group gone, its membership entries disposed);
      `AFailedDeletion_KeepsTheGroupFillsTheMessageAndReportsOnce`;
      `ABlankRename_NeverReachesTheEditorAndRevertsMarksAndSaysWhy` (through `AskRenameCommand`; the pane's
      `Message` reads the rule); `ABlankRenameThenCloseRename_KeepsTheBlockOpenOnTheGroup` (a blank draft
      ended, then `CloseRenameCommand`: `PendingRename` is still the group);
      `AFailedRename_RevertsMarksSaysWhyAndReportsOnce` (kept, and the pane's `Message` equals the refusal
      title with no block open); `CreateGroup_WritesTheNameAndAppendsTheNewEmptyGroup` (no selection; with
      Power selected the new group's `Membership.IsMember` is false);
      `Refresh_RebuildsTheGroupsFollowsTheSelectedPenAndClosesTheActionBlock` (a rename open before;
      after: a new pane, `PendingRename` null, each group's membership read for the restored pen);
      `GroupCalls_WaitInTheQueueBehindAHeldPenCommit` (the toggle through `Groups.Groups[1].Membership`);
      delete `Delete_SetsAPendingConfirmationCarryingTheMemberCountAndWritesNothing` and
      `TheMemberships_ListEveryPenInCatalogueOrderForTheSelectedGroup`; replace
      `AMembershipToggle_CallsSetMembershipOnceAndNamesTheGroupInTheRow`,
      `TogglingOffAMember_TakesItOutAndClearsTheGroupFromTheRow`,
      `AToggleLandingAfterTheGroupWasSelectedAgain_ShowsOnTheEntryThenShownAndItsNextClickUndoesIt`,
      `CreateGroup_WritesTheNameAndSelectsTheNewEmptyGroup` and
      `Refresh_RebuildsTheGroupsKeepingTheSelectedGroupById` with the new tests above; keep the other rename,
      fold, create and dispose tests unchanged in what they assert, moving `SelectedGroup = x` to
      `x.AskRenameCommand` where a test reads the pane's `Message`
- [x] `EndRenameAsync` reports an archive refusal through `RefuseWrite` (Technical Details, View models)
- [x] implement the view models as Technical Details states; delete `SelectedGroup`, `_isReplacingGroups`,
      `ReplaceGroups`, `Memberships`, `_membershipsOfGroup`, `MembershipsOf` and `DeleteGroupCommand`; adjust
      `PenEditorViewModel` (`GroupsOf` takes the observable; the refresh order of Technical Details)
- [x] edit both resx files: rename `PenEditorGroupsTab` to `PenGroupsTitle` (the `GroupsTab` header reads it
      until Task 3), add the ten keys of the Resources table, delete `PenGroupsDeleteConfirmation` and
      `PenGroupsMembersHeader`
- [x] add `MoreGeometry` to `Icons.axaml`
- [x] update the driver (`GroupListOf`, `GroupRowAt`, `PickGroupMenuItem`, the shared `RowOf`) and
      `InterMeasure` (`InterTypeface(face = "Regular")`, `HeaderWidthInInter`)
- [x] rewrite `PenGroupsViewTests` (red at run time: the window holds no `DataGrid` `GroupList`). Each test
      opens the `Groups` tab with `OpenGroupsTab`; a test that needs a pen first clicks `RowAt(window, i)` on
      the `Pens` tab; if a tab switch clears `SelectedRow`, those tests set `viewModel.SelectedRow` after the
      click and Task 3 removes the line, recorded under Task 2. The tests named under Acceptance Evidence for
      Task 2, each asserting:
  - `WithNoPenSelected_EveryMembershipBoxIsDisabledAndUnchecked`: every realised `MembershipBox` is
    `IsEffectivelyEnabled == false` and `IsChecked == false` (not null)
  - `AMembershipBoxClicked_WritesThePenIntoTheGroupAndThePenListNamesIt`: Power selected, the box on the
    `Chamber` row clicked: `_editor.Calls` equals `SetMembership(Power, Chamber, true)`, the box checked; on
    the `Pens` tab `TextOf(RowAt(window, 2), "RowGroups")` is `Chamber`
  - `AMembershipTheArchiveRefuses_PutsTheBoxBackAndReportsOnce`: one call, the box unchecked, one panel
    entry, `GroupsMessage` not empty
  - `AnotherPenSelected_TheBoxesShowItsGroups`: Argon shows Chamber ticked and Spare not; Power neither
  - `AGroupRow_ShowsTheNameAndTheCountWithItsTooltip`: `GroupName` "Chamber", `GroupMemberCount` "2" right of
    the name and inside the name column's cell, its tooltip `Resources.PenGroupsMemberCountTip`; a 28 px row
  - `ARowClick_ShowsNoSelectionHighlightAndNoCellOutline`: after a click on row 0's name cell, its
    `Border#BackgroundBorder` paints what row 1's does at rest, and hovered what row 1's does hovered; no
    cell shows a visible `Rectangle#CurrencyVisual` or `Panel#FocusVisual`; the hover is a `window.MouseMove`
    to the row's centre (a hover is outside the `HeadlessInput` rule)
  - `TheGroupGrid_SortsNothingOnAHeaderClick`: a click on the Group header leaves the row order and sets no
    `:sortascending`
  - `EveryGroupColumn_OpensNoNarrowerThanItsHeader` (theory: a catalogue with no group, `ViewCatalogue`):
    every column's `MinWidth` lies between its header's unconstrained width and 1 px more
    (`ui-theme.md:444-446`), `ActualWidth >= MinWidth`; the membership and action columns are not
    `CanUserResize`, the group column is
  - `TheRowMenu_HoldsRenameTheFoldFlagAndDelete`: the open flyout's items in order are
    `PenGroupsRename`, `PenGroupsFoldedOnStartLabel` (`ToggleType` `CheckBox`), a `Separator`,
    `PenGroupsDelete`, each with a command; the button's tooltip is `PenGroupsActionsTip`
  - `TheFoldItemClicked_WritesOnceAndTheItemFollowsTheFlag`: the fold item on row 1 writes
    `SetFoldedOnStart(Spare, true)` once, and reopened shows checked; row 0's item stays unchecked
  - `AFoldTheArchiveRefuses_LeavesTheItemUncheckedAndReportsOnce`
  - `TheFoldItemSetInCode_WritesNothingAndTheItemFollowsTheCommand` (the item's `IsChecked` set in code
    writes nothing; the command then writes and the item follows)
  - `RenameChosenOnTheSecondRow_OpensTheBlockOnThatGroupWithItsNameFocusedAndSelected`: `GroupRename` shows
    "Spare", is focused, its selection covers the text; `viewModel.Groups.PendingRename` is the Spare group
  - `ARenameTypedThenDoneClicked_RenamesOnceAndClosesTheBlock`: one `RenameGroup(Chamber, "Chamber A")`,
    `GroupRenameBlock` hidden, row 0's `GroupName` "Chamber A"
  - `ABlankRenameThenDoneClicked_KeepsTheBlockOpenMarkedWithTheRule`: `GroupRename` shows "Chamber" with
    `invalid`, `GroupRenameBlock` is visible, `GroupsMessage` reads `Resources.PenGroupsNameRequired`,
    `_editor.Calls` is empty
  - `ARenameTheArchiveRefusesThenDoneClicked_ClosesTheBlockAndTheMessageLineNamesTheRefusal`: one
    `RenameGroup` call, the block hidden, `GroupsMessage` shows the refusal title, one panel entry, row 0's
    `GroupName` unchanged
  - `ARenameTypedThenAnotherRowsMenuOpened_RenamesTheFirstGroupOnce`
  - `ARenameTypedThenThePanesEmptyAreaClicked_RenamesOnce` (two clicks on the empty `GroupsMessage`, as
    `PenGroupsViewTests.cs:317-318` does)
  - `ARenameEnteredWhileItsWriteIsHeldThenDoneClicked_RenamesOnce`
  - `ARenameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing`
  - `DeleteChosen_AsksNamingTheGroupAndItsCountAndCancelWritesNothing`: `GroupDeletionQuestion` and
    `GroupDeletionCount` read the two format keys for Chamber and 2, `GroupDeletionNote` reads
    `Resources.PenGroupsDeleteKeepsPens`; Cancel writes nothing and hides the block
  - `ADeleteConfirmed_DeletesThatGroupAndItsRowGoes`: asked on row 0, confirmed: `DeleteGroup(Chamber)`,
    the grid holds Spare only, the pen list's Groups cells lose "Chamber"
  - `TheActionBlock_KeepsItsBoundsEmptyRenamingAndAskingToDelete`: `GroupActionBlock`'s and `GroupsPanel`'s
    `Bounds` are equal in the three states, and in the renaming and the asking state every visible
    descendant of `GroupActionBlock`, `ConfirmDeleteButton` and `CancelDeleteButton` included, lies inside
    its bounds
  - `TheConfirmButton_IsDestructiveFromThePaletteNotSemisDanger`: `ConfirmDeleteButton.Classes` contains
    `destructive` and not `Danger`
  - `ANewNameCreated_AppendsTheGroupAndClearsTheField`: `CreateGroup("Gas")`, the last row "Gas" with count
    "0", `NewGroupName` empty, nothing in the block
  - `AFullCatalogue_RealisesOnlyTheGroupRowsOnScreen`: 500 groups realise fewer than 500 `DataGridRow`s
  - `TheGroupColumns_LeaveTheNameAtLeast100PxInInter` (theory `en`, `ru` through `CultureScope`; 40 groups,
    so the vertical scroll bar shows): with `ViewportOf(GroupListOf(window))`, the membership column's
    `ActualWidth` and `HeaderWidthInInter` of the action header, the name text width
    `viewport - membership - max(40, actionsInter) - 12 - 8 - 8 - Inter("999" at the cell's FontSize)` is at
    least 100, and the group column's own Inter header width fits what is left; the test's output records
    the two widths for Task 6
  - `ThePaneTexts_FitTheirLinesInInter` (theory `en`, `ru`): the subtitle at its `FontSize` in Inter Regular
    is at most 308 px, and `Resources.FormatPenGroupsDeleteCount(999)` and `Resources.PenGroupsDeleteKeepsPens`
    each at most 308 px. If a text fails, the
    failing language's value is shortened until it passes (a subtitle stays one line, `SKILL.md:227`) and
    the new value is recorded here and in the Resources table
- [x] rewrite `ARefreshThatChangesTheCatalogue_RebindsTheTableAndTheGroupsTab` as
      `ARefreshThatChangesTheCatalogue_RebindsThePenListAndTheGroupsPane`: after the refresh
      `GroupListOf(window).ItemsSource` is `viewModel.Groups.Groups` and `GroupRowAt(window, 2)`'s `GroupName`
      is "Vacuum"
- [x] replace the `GroupsTab` content (`PenEditorWindow.axaml:492-684`) with the `GroupsPanel` of Technical
      Details, left-aligned in the tab; update the window styles as listed; add `OnRenameItemClick` to the
      code-behind
- [x] run `PenGroupsViewModelTests`, `PenGroupsViewTests`, `PenEditorViewTests`, `PenEditorViewModelTests`,
      `PenListViewTests`, `PenFormPaneViewTests`, `MainWindowViewTests`, `MainWindowViewModelTests`,
      `ThemeTests` and `ResourcesTests`: every case passes
- ⚠️ the `Click` handler named on `GroupRenameItem` inside the cell template's `MenuFlyout` never runs (the item's
  command does); the plan's fallback is in place: `GroupRenameItem` carries no `Click`, and the code-behind
  subscribes `GroupRename.DataContextChanged` in `OnLoaded` (removed in `OnUnloaded`) and posts the focus and
  `SelectAll` at `DispatcherPriority.Background` when the data context becomes a `PenGroupViewModel`. The flyout
  hands the focus back to `GroupActionsButton` as it closes, and the posted focus lands after it
- ⚠️ the other three ASSUMPTIONs hold: the menu items take the row's `DataContext` (the second-row tests act on
  Spare), the compiled binding through a null `Membership` reads `False` with `FallbackValue` alone, and a click
  on `MembershipBox` toggles it; a tab switch keeps `SelectedRow`, so no view test sets it in code
- ⚠️ a disposed `ReactiveCommand` still executes and reports `CanExecute` true (probe), so "its membership entries
  disposed" has no observation on the production surface; `ConfirmingTheDeletion_...` asserts the rest, and the
  pane still disposes the deleted group's entries
- ⚠️ `ARenameTheArchiveRefusesThenDoneClicked_...` holds the rename call across the Done click: the fake and the
  queue answer an unheld call inside the press, which would refuse the draft before the click lands, while the
  archive answers later
- ⚠️ `GroupCalls_WaitInTheQueueBehindAHeldPenCommit` toggles Pressure into Gas through the selected pen's entry;
  the toggle reads `Row.Pen` when the queue runs it, after the held name commit, so the call carries the new name
- ⚠️ `ThePaneTexts_FitTheirLinesInInter` measures against the realised `GroupsSubtitle` width (the pane's 1 px
  border leaves 306 px, not 308); `ARowClick_...` moves the pointer to `GroupsTitle` before the rest comparison,
  since the clicked row is hovered after the click
- ⚠️ measured in Inter (`TheGroupColumns_LeaveTheNameAtLeast100PxInInter`, 40 groups): `en` Действия 81.5 px,
  Группа 169.5 px, name text 115.3 px; `ru` 95.2 px, 155.8 px, 101.6 px

### Task 3: Remove the tabs and stand the three parts side by side under a top bar

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorLayoutViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenFormPaneViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorWindowDriver.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [ ] create `PenEditorLayoutViewTests` (the three traits, `ProcessGlobalStateCollection`) and write the
      failing `TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide`: no `TabControl` among the
      window's visual descendants; the pen list's border, `PenFormPanel` and `GroupsPanel` share one top,
      stand left to right 16 px apart, the panes 420 and 340 px wide; the list's border starts 16 px from the
      window's left edge, `GroupsPanel` ends 16 px from its right, and `PenEditorTopBar` sits 16 px above the
      three
- [ ] move and rewrite `TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart` as
      `TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart`: after a refresh with two
      added pens, the count text, the button caption, the button's right edge on `GroupsPanel`'s right edge,
      the count left of the button by 8 px, both above the panes, and the On start column header
- [ ] replace `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum` with
      `AtTheMinimumWidth_BothPanesKeepTheirWidthAndThePenListScrolls` (opens at `DesignedWidth`; at
      `MinWidth` 1272 the panes are 420 and 340 px, every pen column's `ActualWidth >= MinWidth`, Id and Name
      lie inside the viewport, and the headers presenter's extent exceeds its bounds) and
      `ThePane_Stays420PxWideWhenTheWindowWidens` with `BothPanes_StayFixedWhenTheWindowWidens` (+300 px:
      both panes unchanged, the list 300 px wider); both move into `PenEditorLayoutViewTests` and the
      `PaneWidth` constant they read goes with them
- [ ] in `PenEditorWindow.axaml` replace the `TabControl` and the bottom bar with the root grid of Technical
      Details: the top bar, then `*,16,Auto,16,Auto` holding the bordered `PenList`, `PenFormPanel` and
      `GroupsPanel`; `Width` and `MinWidth` from the two constants added to `PenEditorWindow.axaml.cs`
      (`DesignedWidth = 1440`, `MinimumWidth = 1272`); the root margin 16
- [ ] delete `PenEditorPensTab` from both resx files; delete the five tab keys from both variants of
      `Palette.axaml`; rewrite `EveryTabAndListSurface_PaintsItselfFromThePalette` as
      `EveryListSurface_PaintsItselfFromThePalette` over the `ListBox` alone
- [ ] delete `OpenGroupsTab` and `GroupAt` from the driver and every `OpenGroupsTab(window)` call; drop the
      `Pens` tab switch from the view tests that made one
- [ ] sweep the `<summary>` lines of the `PenEditor` test files and view models for "tab" wording
      (`PenEditorViewTests.cs:28-31` "its pens tab", `PenGroupsViewTests`' "the groups tab", the driver's
      "both tabs", `PenEditorViewModel`'s "the groups tab"): each names the pane it now covers;
      `git grep -n -i "tab\b" -- SemiPlot/SemiPlot.UI/PenEditor SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor`
      prints no line that means an editor tab
- [ ] run `PenEditorLayoutViewTests`, `PenEditorViewTests`, `PenListViewTests`, `PenFormPaneViewTests`
      (`AtTheOpeningSize_ThePaneShowsTheWholeForm` passes at 1440 x 720), `PenGroupsViewTests`, `ThemeTests`,
      `ResourcesTests`: every case passes

### Task 4: Open at the designed width where the screen allows, and fix the minimum height

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorLayoutViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenListViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`

- [ ] write the failing theory `TheOpeningWidth_TakesTheDesignedWidthWhereTheWorkingAreaAllowsAndNeverLessThanTheMinimum`
      (`[Theory]`: `OpeningWidthFor` is pure): working area 1920 -> 1440, 1440 -> 1440, 1366 -> 1366, 1280 -> 1280,
      1024 -> 1272, `double.PositiveInfinity` -> 1440 (red: `OpeningWidthFor` does not exist)
- [ ] extend `EditPensAndGroups_ClickedOnTheRealisedWindow_OpensTheEditorOverIt`: `editorWindow.Width` equals
      `PenEditorWindow.OpeningWidthFor(area)`, where `area` is the main window's
      `Screens.ScreenFromWindow(window)` working area width over its scaling, or infinity with no screen
- [ ] write the failing `AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows`: at
      `Height = MinHeight` the window's height is 536; with a deletion asked, `ConfirmDeleteButton` ends inside
      `GroupActionBlock` and `GroupsMessage` ends inside `GroupsPanel` above its padding; `GroupList`'s and `PenList`'s `PART_RowsPresenter` are each at least four `RowHeight` tall
- [ ] write the failing theory `AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter` (`en`, `ru`;
      100 pens, so the vertical scroll bar shows): each fixed column's width is
      `max(declared Width, HeaderWidthInInter)`, the Groups fill column's minimum its `HeaderWidthInInter`,
      and their sum is at most `ViewportOf(PenList)` at the designed width
- [ ] add `OpeningWidthFor` to `PenEditorWindow.axaml.cs`; set `MinHeight="536"` in the AXAML; the pen list's
      Name column opens at 200 px; `MainWindow`'s factory sets `Width = PenEditorWindow.OpeningWidthFor(...)`
      from its screen, through one private method `WorkingAreaWidth()` in `MainWindow.axaml.cs`
- [ ] if `AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter` fails at 200 px, lower the Name width to
      the largest multiple of 4 that passes and record the value here for Task 6
- [ ] run `PenEditorLayoutViewTests`, `PenListViewTests`, `PenFormPaneViewTests`, `MainWindowViewTests`:
      every case passes

### Task 5: Verify acceptance criteria

- [ ] run the filtered command under Acceptance Evidence: 0 failed, every named test present
- [ ] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
- [ ] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the two NU1902 for `MessagePack`
      2.5.302 in `SemiPlot.AppHost` that the base already reports
- [ ] set `$BASE` to the stacking base: `pen-editor-pens-pane` while it is unmerged, `master` once it merges
      (`git merge-base --is-ancestor pen-editor-pens-pane master`, exit 1 means unmerged)
- [ ] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse`
      over every `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
- [ ] run the HeadlessInput gate, the key gate with `-- SemiPlot` only, the `.Memberships` gate and the
      `Danger` gate under Acceptance Evidence: each prints what it must; the key gate over `docs/architecture`
      runs in Task 6
- [ ] confirm the scope guard: `git diff $BASE...HEAD --stat` lists no `IPenCatalogueEditor`, `ArchiveCallQueue`,
      `PostgresPenCatalogueEditor`, `PenFormViewModel.cs` or `HeaderMeasuredColumns.cs`, and
      `git diff $BASE...HEAD -- SemiPlot/SemiPlot.UI/PenEditor/PenGroupWriter.cs SemiPlot/SemiPlot.UI/PenEditor/PenMembershipViewModel.cs`
      changes only `///` lines
- [ ] confirm no filter box and no saved widths: `git diff $BASE...HEAD -- SemiPlot/SemiPlot.UI` adds no
      `TextBox` to `PenEditorTopBar` and no settings key

### Task 6: [Final] Update documentation

- [ ] `docs/architecture/overview.md:521-527`: the window is one row under a top bar, pen list | pen pane |
      groups pane, no tabs; the table's rows become the pen list and pane (the existing `Pens` row text), the
      groups pane (title, one-line subtitle, the new-group row, the three-column grid, the "..." menu of
      rename, the fold flag and delete, the reserved block, the message line; membership is edited from the
      pen side, for the pen selected in the list, disabled with none) and the top bar; the `Groups` tab row
      goes. Rewrite `:548-549` (the pane takes the rows and follows `SelectedRow`), `:586-589` and `:595-597`
      ("the groups pane"), `:621-625` (the membership entry per pen and group, cached for the pane's life,
      handed to the group row for the selected pen), `:626-632` (the fold flag is a checkable menu item), and
      `:647-648` (the group grid is a focusable `DataGrid`, so a press on it ends an edit as the pen list
      does); add the rename block's Done rule and the delete confirmation's three lines
- [ ] `docs/architecture/ui-theme.md`: the key table loses the five tab rows (`:60-63`) and names the groups
      pane for `TextBoxPlaceholderForeground` (`:43`), `ListBoxItem*` (`:64-65`, the export list now) and the
      membership box rows (`:82-84`); the retint paragraph (`:101-108`) states the window-wide outline rule
      and the two `GroupList` selection styles; the tests paragraph (`:120-131`) names
      `EveryListSurface_PaintsItselfFromThePalette` and `TheDestructiveButton_PaintsItselfFromThePalette`; the
      App surfaces table (`:165-186`) gains the four `AppDestructive*` rows and counts twenty keys, and the
      consumers of `AppBorderBrush` and `AppSecondaryForegroundBrush` name the groups pane, its list border,
      the count, the subtitle and the "..." glyph; rewrite "A resizable window keeps its fixed parts"
      (`:391-526`): 1440 x 720 opening, `OpeningWidthFor` and where it is called, `MinWidth` 1272 and
      `MinHeight` 536 with their arithmetic, the margin 16, the top bar, the three parts, the Name column at
      its Task 4 width, the groups pane's rows, column widths, insets, the reserved 116 px block, and the gates
      table with every test this plan adds or replaces; record Task 2's measured Inter widths
- [ ] `docs/architecture/ui-text.md:223-269`: no tab headers; the top bar; the groups pane's keys of the
      Resources table, the reused ones, the three confirmation lines (the count noun first in both languages
      still holds), the menu items, `PenGroupsNameRequired` on the pane's message line
- [ ] `docs/architecture/charting.md:693-695`: name `PenGroupViewModel` and `PenMembershipViewModel` instead
      of "the row and membership VMs" if that wording no longer covers them
- [ ] run the key gate under Acceptance Evidence over `-- SemiPlot docs/architecture`: it prints nothing
- [ ] move this plan to `docs/plans/completed/` (left to the delivery step)

## Post-Completion

**Manual verification** in the running viewer (`Edit` -> `Pens and groups`), in both languages:

1. On a 1920 px screen the window opens 1440 px wide, centred on the main window; the pen list shows its five
   columns with no horizontal scroll bar.
2. The groups pane shows "Группы", the subtitle on one line, the new-group row, the grid and nothing in the
   reserved block.
3. With no pen selected every membership box is greyed. Select a pen: its groups are ticked; tick another
   group, and the pen list's Группы cell names it.
4. A click on a group row shows no highlight and no cell outline.
5. "..." opens Переименовать, Свёрнута при открытии and Удалить. Переименовать puts the caret in the box with
   the name selected; type, press Готово: the row shows the new name and the block empties.
6. Удалить shows the question, the count and «Сами перья останутся.» on three lines, plus a red Удалить button that darkens on hover and
   press; Отмена empties the block; nothing in the pane moves in any state.
7. Narrow the window to its minimum: both panes keep their widths and the pen list scrolls horizontally.
   Lower it to its minimum height: the groups pane's rows all show and the pen form scrolls.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: pen-list-filter (the top
bar's filter box over id, name and group, case-insensitive, keeping the selection while it matches);
export-pen-choice-list (the export window's pens as checkboxes only, no row selection);
axis-scale-checkbox-column (the axis scale panel's log checkbox in the field column, with a label in the
label column); ui-spacing-scale (every remaining inset onto the 4/8/12/16/24/32 scale, the `12,2` button
padding with them, the refresh button's included).
