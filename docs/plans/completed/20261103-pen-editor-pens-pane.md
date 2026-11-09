# Pen editor: a DataGrid pen list and a sectioned pen pane

## Overview

- The `Pens` tab of `PenEditor/PenEditorWindow` shows every pen setting twice: an eleven-column
  read-only table (`PenEditorWindow.axaml:90-264`) and a five-column form under it
  (`PenEditorWindow.axaml:266-466`). Two of the table's columns are inert checkboxes,
  `RowEnabledOnStart` and `RowLogScaleOnStart`, with `IsHitTestVisible="False"`
  (`PenEditorWindow.axaml:193-199`, `:248-254`): they look editable and ignore the click (#111).
- The table becomes a read-only `DataGrid` of five columns, Номер | Имя | Ед. изм. | При запуске |
  Группы. A header click sorts ascending, the next descending, and the arrow shows the direction. Every
  column resizes, opens no narrower than its realised header, and Группы takes the spare width. Every
  text cell trims with an ellipsis and carries its full text as a tooltip. The colour dot sits inside
  the name cell, and the start flag is a check glyph, not a control.
- The form moves into a 420 px pane beside the list: an empty-state hint with no pen selected, and with a
  pen selected a one-line title (id, then name), three sections on one label column and one field edge,
  and the reserved message line last. An invalid field shows the `invalid` border and a warning glyph
  inside the box.
- The header-measured column sizing is one reusable attached behaviour, `Tables/HeaderMeasuredColumns`,
  which the groups pane of the next slice reuses.
- Edit semantics do not change: a field writes when its edit ends, the scale pair writes when focus
  leaves both boxes, every call runs through the one `ArchiveCallQueue`, and the window drains on close.
  The `Groups` tab and the window's tabs stay as they are.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice pen-editor-pens-pane (#111)

The PR body carries `Closes #111`.

- Window and code-behind:
  - `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`: `Width="1272"`, `MinWidth="1260"`,
    `Height="720"` (`:11-13`); the `PenTableColumns` and `MembershipColumns` resources and the shared
    `IdColumnWidth` (`:19-39`); the `Button.column-header`, `TextBlock.cell`, `TextBlock.form-label`,
    `Grid#FormScale TextBox` and `ListBox#PenTable ListBoxItem, ListBox#GroupList ListBoxItem` styles
    (`:46-75`); the header grid of eleven sort buttons bound to `SortCommand` with a `PenColumn`
    parameter (`:90-171`); the `ListBox` `PenTable` with a `VirtualizingStackPanel` and the row
    template (`:173-264`); the form panel `PenFormPanel`, 240 px high, enabled while a form exists and
    no refresh runs (`:266-277`); the form grid `Auto,*,Auto,*,Auto` (`:282-286`); the colour box with
    `input:FormattedFieldInput.Layout="HexColor"` and the `ColorPicker` (`:341-365`); `FormScale`, the
    scale pair's focus scope, and the log box outside it under a shared `ScaleLabel` column
    (`:392-453`); `FormMessage` (`:455-460`); the `Groups` tab (`:472-669`); the bottom bar
    (`:673-690`).
  - `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`: `RowHeight = 28` (`:20`), the `Field`
    attached property (`:25-26`). `OnLoaded` subscribes the tunnel press handler, `FormScale`'s
    `IsKeyboardFocusWithin`, and finds the picker's flyout with
    `FormColorPicker.GetVisualDescendants().OfType<DropDownButton>()` (`:55-74`); `OnUnloaded` removes
    all three (`:76-91`). The drain on close (`:94-134`), the press outside a focusable control
    (`:136-149`), the edit-end routing (`:151-221`), the line-style and checkbox handlers naming
    `FormLineStyle`, `FormEnabledOnStart` and `FormLogScaleOnStart` (`:223-261`), the picker flyout
    handlers (`:263-287`).
- View models:
  - `PenEditorViewModel.cs`: the `PenColumn` enum of eleven columns (`:18-32`); `_sortColumn`,
    `_isSortDescending`, `_isReorderingRows` (`:47-49`); `SortCommand` (`:75`, `:119-120`); `Rows`
    replaced whole on sort and on refresh (`:82-87`); `SelectedRow` ignores the write-back while a sort
    reorders (`:89-100`); `Sort` and `Sorted` (`:161-192`); the refresh keeps the selected pen and group
    and re-sorts (`:194-226`); `ComparisonOf` and `ByText`, each column then `Pen.Id`,
    `StringComparer.CurrentCultureIgnoreCase` for text (`:252-277`).
  - `PenRowViewModel.cs`: `Pen` as last written, `QueuedPen`, `GroupsText` joined with `", "`
    (`:13-37`); public constructor `(StoredPen, IEnumerable<StoredGroup>)` (`:13`).
  - `PenFormViewModel.cs`: `Row` (`:56`), the drafts (`:58-111`), the validity flags (`:113-132`),
    `Message` (`:134`), `MaskPreview` (`:137`).
- Tests:
  - `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`: 36 view tests
    (`:54-1028`); helpers `FormLabel` (reads `TextBlock.form-label` under `Panel#PenForm`,
    `:1035-1040`), `ColorFlyoutOf` (`:1047-1053`), `PickColor` (`:1055-1068`), `RealisedPens(ListBox)`
    (`:1070-1076`). Tests that name controls this slice removes: `SortByEnabledOnStart` (`:76`),
    `RowLineStyle`, `RowScaleMaxOnStart`, `RowEnabledOnStart`, `PenTableHeader`, `RowGrid` (`:98-122`),
    `SortByName` (`:142`, `:149`), `PenTable` (`:170`, `:200`), `PenTableHeader` (`:190`),
    `RowLineStyle` and `RowEnabledOnStart` (`:490-491`), `SortByLogScaleOnStart` and
    `RowLogScaleOnStart` (`:886-891`, `:923`).
  - `PenEditorWindowDriver.cs`: `RowAt` reads `ListBox` `PenTable` (`:35-39`), `TextOf` (`:52-55`).
  - `PenGroupsViewTests.cs:60-62` switches to the pens tab and reads `RowGroups` through `RowAt`.
  - `PenEditorViewModelTests.cs`: `Refresh_KeepsTheSortOrder` (`:104-115`), the sort theory
    `ASort_OrdersTheRowsByTheColumnThenById` (`:293-313`),
    `ASecondSortOnTheSameColumn_ReversesTheRowsAndKeepsTheSelectionAndItsForm` (`:315-332`),
    `ASortOnAnotherColumn_StartsAscending` (`:334-343`).
  - `PenGroupsViewModelTests.cs:214` calls `SortCommand.Execute(PenColumn.Name)`.
  - `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`: `EveryCornerRadius_ComesFromThePalette`
    (`:80-107`), `EveryTabAndListSurface_PaintsItselfFromThePalette` (`:203-246`),
    `APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` (`:394-399`); traits `UI`, `Chart`, `Unit`
    (`:26-28`).
  - `CsvExportWindowTests.cs:1175-1235`: the Inter-through-Skia measurement pattern
    (`InterTypeface`, `InterFont`), with `CultureScope`.
  - `HeadlessInput.cs:16-91`: `Click` runs the jobs a click queues (`:51`); `Type`, `Clear`, `Press`.
  - `ViewProbe.cs:26-44`: `Named` reads the window's name scope; `Descendant` walks the visual tree
    because a template carries its own name scope.
- Theme and resources:
  - `SemiPlot/SemiPlot.UI/App.axaml:14-18`: `SemiTheme`, `ColorPickerSemiTheme`, one `StyleInclude` of
    `Styles/Forms.axaml`.
  - `Styles/Forms.axaml:5-7`: `TextBox.invalid /template/ Border#PART_ContentPresenterBorder` takes
    `AppSeverityErrorBrush`; `:17-28`: `TextBlock.form-message`, 40 px, two 20 px lines.
  - `Styles/Icons.axaml:4-14`: `StreamGeometry` keys, the house form of a glyph.
  - `Styles/Palette.axaml:5-9`: five corner radii at 4; `:13-95`: the `Light` variant's brushes,
    `ListBoxItemSelectedBackground` `#3574F0` at 0.25 (`:50`), `AppBorderBrush` (`:83`),
    `AppSecondaryForegroundBrush` (`:89`), `AppSeverityErrorBrush` (`:92`).
  - `Localization/Resources.resx` and `Resources.ru.resx`: `PenEditorColumn*` at `:231-263`,
    `PenFormMaskPreviewLabel` at `:672`, `AxisScaleMaximumLabel` and `AxisScaleMinimumLabel` at
    `:150-155`. `PenEditorColumnScaleMinOnStart`, `PenEditorColumnScaleMaxOnStart` and
    `PenEditorColumnLogScaleOnStart` (`:252-260`) have consumers only in `PenEditorWindow.axaml` and
    `PenEditorViewTests.cs` (verified with `grep -rn PenEditorColumn` over `SemiPlot.UI` and
    `SemiPlot.Tests.Unit`).
- Packages: `SemiPlot/Directory.Packages.props` pins Avalonia 12.0.5 and `Semi.Avalonia` 12.0.3
  (`:8`, `:29`); `SemiPlot.UI.csproj:20-38` references by name only. `nuget.config` keeps only
  `nuget.org`. `Avalonia.Controls.DataGrid` 12.0.1 depends on `Avalonia` 12.0.5
  (`~/.nuget/packages/avalonia.controls.datagrid/12.0.1/avalonia.controls.datagrid.nuspec:19`) and
  `Semi.Avalonia.DataGrid` 12.0.0 on `Avalonia.Controls.DataGrid` 12.0.0
  (`semi.avalonia.datagrid/12.0.0/semi.avalonia.datagrid.nuspec:18`). Task 1's restore confirms the
  three versions resolve together.
- Upstream sources, by repository and tag:
  - `irihitech/Semi.Avalonia@v12.0.0 src/Semi.Avalonia.DataGrid/DataGrid.axaml`:
    `DataGridCell` is `Focusable=False` (`:33`); its `CurrencyVisual` rectangle, stroked with
    `DataGridCellCurrentBorderBrush`, shows on `:current` (`:43-51`, `:96-98`); its `FocusVisual` shows
    on `:focus` only (`:52-65`, `:99-101`). The column header pads with `DataGridColumnHeaderPadding`
    (`:114`), lays out `ContentPresenter` then `PathIcon#SortIcon` with `Margin="0,0,8,0"` and the
    `InnerPathIcon` theme (`:130-144`), and the sorted styles set `IsVisible` and `Data` (`:187-195`); no
    style sets the icon invisible. A selected row paints `Border#BackgroundBorder` with
    `DataGridRowSelectedBackground` (`:288-294`, `:338-351`). The grid template: the headers presenter
    spans columns 1 and 2 (`:491-495`), the rows presenter all three (`:505-510`), and the vertical
    scroll bar overlays column 2 (`:525-530`) with `ScrollViewer.AllowAutoHide="True"` (`:469`).
  - `irihitech/Semi.Avalonia@v12.0.0 src/Semi.Avalonia.DataGrid/Shared.axaml`:
    `DataGridCellTextBlockDefaultMargin` `12 0` (`:2`), `DataGridCellMinHeight` =
    `SemiHeightControlDefault` (`:3`), `DataGridColumnHeaderPadding` `12 0 0 0` (`:7`),
    `DataGridColumnHeaderMinHeight` (`:8`), `DataGridColumnHeaderCornerRadius` and
    `DataGridRowCornerRadius` = `SemiBorderRadiusSmall` (`:9`, `:14`).
  - `irihitech/Semi.Avalonia@v12.0.0 src/Semi.Avalonia.DataGrid/Light.axaml`: the nineteen DataGrid
    brush keys, among them `DataGridColumnHeaderForeground` = `SemiColorText2`, `DataGridLineBrush` =
    `SemiColorBorder`, `DataGridRowSelectedBackground` = `SemiColorPrimaryLight`.
  - `irihitech/Semi.Avalonia@v12.0.3 src/Semi.Avalonia/Controls/PathIcon.axaml:26-28`: `InnerPathIcon`
    has a fixed `SemiWidthIconMedium` width and height.
  - `irihitech/Semi.Avalonia@v12.0.3 src/Semi.Avalonia.ColorPicker/Controls/ColorPicker.axaml`:
    `Padding 0 0 10 0` (`:19`); the swatch content is a `<Template>` setter, one control per target
    (`:23-36`); the template is a `DropDownButton` with `ClipToBounds="True"` (`:40-55`).
    `irihitech/Semi.Avalonia@v12.0.3 src/Semi.Avalonia/Controls/DropDownButton.axaml:115` names the arrow
    `DropDownGlyph`.
  - `AvaloniaUI/Avalonia.Controls.DataGrid@12.0.1 src/Avalonia.Controls.DataGrid/DataGridColumnHeader.cs`:
    `OwningColumn` is internal (`:122`); `UpdatePseudoClasses` sets `:sortascending`/`:sortdescending`
    from `OwningColumn.GetSortDescription()` (`:172-190`); a click posts `ProcessSort` to the dispatcher
    (`:224-235`), which sorts only when the column's `CanUserSort` holds (`:244-249`) and replaces a
    column's description with its `SwitchSortDirection()` or adds
    `DataGridSortDescription.FromComparer(CustomSortComparer)` (`:238-300`).
  - `AvaloniaUI/Avalonia.Controls.DataGrid@12.0.1 src/Avalonia.Controls.DataGrid/DataGridColumn.cs`:
    `ActualWidth` (`:267`); `CanUserSort` without an explicit value derives from the sort member's type
    (`:329-354`); `MinWidth` (`:559`); `Width` a styled property (`:600-604`);
    `GetColumnContainingElement(Control)` is public static and walks to the header's owning column
    (`:681`); `CustomSortComparer` an `IComparer` (`:1147`); `GetSortDescription` matches a comparer
    description by `SourceComparer == CustomSortComparer` (`:1157-1178`).
  - `AvaloniaUI/Avalonia.Controls.DataGrid@12.0.1 src/Avalonia.Controls.DataGrid/DataGridDataConnection.cs`:
    `CollectionView` is the source itself when it is an `IDataGridCollectionView` (`:168-172`); a plain
    list gets a fresh `DataGridCollectionView` (`:463-479`).
  - `AvaloniaUI/Avalonia.Controls.DataGrid@12.0.1 src/Avalonia.Controls.DataGrid/DataGrid.cs`: the grid
    is focusable (`:761`); `InitializeElements` caches and restores the selected items across a reset
    (`:2623-2675`). `DataGridRow.cs:449`: `GetIndex()` is public.
  - `AvaloniaUI/Avalonia@12.0.5 src/Avalonia.Base/Layout/LayoutManager.cs`: `ExecuteLayoutPass` raises
    `LayoutUpdated` after its passes (`:116-179`); `ExecuteInitialLayoutPass`, which `Show` runs, ends
    with `ExecuteLayoutPass` (`:182-207`); an invalidation queues the next pass through
    `MediaContext.BeginInvokeOnRender` (`:348-355`).
  - `AvaloniaUI/Avalonia@12.0.5 src/Avalonia.Base/Media/MediaContext.cs`: `FireInvokeOnRenderCallbacks`
    reruns queued layout passes until none is left (`:187-222`), before the frame is committed
    (`:134-161`).
- Related patterns: attached behaviour configured from AXAML (`Input/FormattedFieldInput`); a
  `FuncValueConverter` consumed through `{x:Static}` (`PenLineStyleConverters.ToLabel`,
  `PenEditorWindow.axaml:42`); `LegendConverters.HexToBrush` for a pen swatch
  (`PenEditorWindow.axaml:208-211`).

## Development Approach

- **Testing approach**: TDD. The comparers and the column-sizing behaviour are written test first
  against a bare grid; the window's list and pane are driven test first through `HeadlessInput` on the
  realised window.
- Complete each task fully before moving to the next; every task ends with its tests passing.
- Make small, focused changes.
- **CRITICAL: every task MUST include new/updated tests** for the code changes in that task.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- A test written before its type exists fails to compile; that is its red state, and the task's
  implementation step turns it green.
- Every edit-semantics test that passes at HEAD keeps passing unchanged in what it asserts about writes:
  a field writes on edit end, the scale pair writes once, the drain on close, the refresh order.

## Testing Strategy

- **Unit tests**: `PenRowOrderTests` (`[AvaloniaFact]`/`[AvaloniaTheory]`, because a row is a `ReactiveObject`), `HeaderMeasuredColumnsTests`
  (`[AvaloniaFact]`, a bare `DataGrid` in a `Window`), `PenEditorViewModelTests` and
  `PenGroupsViewModelTests` updated for the grid-owned sort, `ThemeTests` and `ResourcesTests`
  extended.
- **View tests**: `PenEditorViewTests` rewrites the tests of the list and the pane and keeps every
  write-path assertion; `PenGroupsViewTests` keeps passing through the new `RowAt`.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- Every new test class carries the three traits `Component`, `Area`, `Category` (`AGENTS.md`), and a
  class reading a `Resources` value joins `ProcessGlobalStateCollection` (`ui-text.md:478-482`).
- Every click, clear, typed text and key press goes through `HeadlessInput`; no test class adds a helper
  of its own for those four gestures (`AGENTS.md`, Test).

## Acceptance Evidence

The defect is automatable. At HEAD the realised `PenTable` holds two `CheckBox`es per row,
`RowEnabledOnStart` and `RowLogScaleOnStart`, both `IsHitTestVisible="False"`
(`PenEditorWindow.axaml:193-199`, `:248-254`), so the new test `ThePenList_RealisesNoCheckBox` fails
at HEAD: the window has no `PenList`, and the equivalent read of `PenTable` finds six checkboxes over
the three fixture pens.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenListViewTests|FullyQualifiedName~PenFormPaneViewTests|FullyQualifiedName~PenGroupsViewTests|FullyQualifiedName~PenEditorViewModelTests|FullyQualifiedName~PenGroupsViewModelTests|FullyQualifiedName~PenFormViewModelTests|FullyQualifiedName~PenRowOrderTests|FullyQualifiedName~HeaderMeasuredColumnsTests|FullyQualifiedName~ThemeTests|FullyQualifiedName~ResourcesTests|FullyQualifiedName~MainWindowViewTests|FullyQualifiedName~MainWindowViewModelTests"
```

It must report 0 failed. These named tests must be present and pass:

- `ThemeTests.EveryDataGridSurface_PaintsItselfFromThePalette` (theory: `Light`, `Dark`) — Task 1
- `ThemeTests.EveryCornerRadius_ComesFromThePalette` (extended with a `DataGrid` row and header) — Task 1
- `ThemeTests.APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` (gains
  `DataGridRowSelectedBackground`, `DataGridRowSelectedPointeroverBackground`) — Task 1
- `HeaderMeasuredColumnsTests.AColumnNarrowerThanItsHeader_OpensAtTheHeadersUnconstrainedWidth` — Task 2
- `HeaderMeasuredColumnsTests.AColumnDeclaredWiderThanItsHeader_KeepsItsDeclaredWidth` — Task 2
- `HeaderMeasuredColumnsTests.TheWidths_AreFittedInTheLayoutPassThatRealisesTheHeaders` — Task 2
- `HeaderMeasuredColumnsTests.AnUnsortedHeader_ReservesTheSortArrowSlot` — Task 2
- `HeaderMeasuredColumnsTests.TheFillColumn_TakesTheViewportTheOtherColumnsLeave` — Task 2
- `HeaderMeasuredColumnsTests.TheFillColumn_ShrinksWithTheGridDownToItsMinimumAndThenTheGridScrolls` — Task 2
- `HeaderMeasuredColumnsTests.TheFillColumn_GrowsBackWhenTheGridWidens` — Task 2
- `HeaderMeasuredColumnsTests.AResizedColumn_GivesItsDifferenceToTheFillColumn` — Task 2
- `HeaderMeasuredColumnsTests.AVisibleVerticalScrollBar_IsLeftOutOfTheViewport` — Task 2
- `HeaderMeasuredColumnsTests.AGridDetachedAndAttachedAgain_KeepsFittingTheFillColumn` — Task 2
- `PenRowOrderTests.EachOrder_SortsByItsKeyThenById` (theory: Id, Name, Unit, EnabledOnStart, Groups) — Task 3
- `PenRowOrderTests.TheTextOrders_IgnoreCase` — Task 3
- `PenEditorViewModelTests.PenRows_ListsEveryRowInCatalogueOrderUntilSorted` — Task 4
- `PenEditorViewModelTests.Refresh_KeepsTheSortOrder` (rewritten over `PenRows.SortDescriptions`) — Task 4
- `PenGroupsViewModelTests.TheMemberships_ListEveryPenInCatalogueOrderForTheSelectedGroup` (sorts through `PenRows`) — Task 4
- `PenListViewTests.ThePenList_RealisesNoCheckBox` (the #111 reproduction) — Task 4
- `PenListViewTests.ARow_ShowsThePenAsStoredUnderItsHeader` (rewritten) — Task 4
- `PenListViewTests.EveryTextCell_TrimsWithAnEllipsisAndCarriesItsTextAsATooltip` — Task 4
- `PenListViewTests.AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm` — Task 4
- `PenListViewTests.EachHeaderClick_OrdersTheListByItsColumn` (theory over the five headers) — Task 4
- `PenListViewTests.ARefresh_KeepsTheSortAndItsArrow` — Task 4
- `PenListViewTests.AFullCatalogue_RealisesOnlyTheRowsOnScreen` (rewritten over `DataGridRow`) — Task 4
- `PenListViewTests.EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader` — Task 4
- `PenListViewTests.AClickedRow_ShowsTheRowHighlightAndNoCellOutline` — Task 4
- `PenEditorViewTests.ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce` — Task 4
- `PenEditorViewTests.ANameTypedThenAColumnHeaderClicked_WritesOnce` — Task 4
- `PenFormPaneViewTests.TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum` (replaces `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthClipsNoFixedColumn`) — Task 5
- `PenFormPaneViewTests.WithNoPenSelected_ThePaneShowsOnlyTheHint` (replaces `TheForm_IsEmptyAndDisabledWithNoRowAndShowsTheClickedPen`) — Task 5
- `PenFormPaneViewTests.AClickedPen_FillsTheFormUnderAOneLineTitle` — Task 5
- `PenFormPaneViewTests.TheForm_HasThreeSectionsOnOneLabelColumnAndOneFieldEdge` — Task 5
- `PenFormPaneViewTests.EveryField_HasTheWidthOfItsContentType` — Task 5
- `PenFormPaneViewTests.TheColourSwatchPicker_IsSquare` — Task 5
- `PenFormPaneViewTests.AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` — Task 5
- `PenFormPaneViewTests.AValidField_ShowsNoWarningGlyph` — Task 5
- `PenFormPaneViewTests.ThePane_Stays420PxWideWhenTheWindowWidens` — Task 5
- `PenFormPaneViewTests.AtTheOpeningSize_ThePaneShowsTheWholeForm` — Task 5
- `PenFormPaneViewTests.EveryPenFormLabel_FitsTheLabelColumnInInter` (theory: `en`, `ru`) — Task 5
- `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries` (passes with the new keys) — Task 5

Every other test of `PenEditorViewTests`, `PenListViewTests` and `PenFormPaneViewTests` passes with
its write assertions unchanged; those whose row or
header reads change are listed in Tasks 4 and 5. Two of them guard the picker's flyout lookup behind
the empty-state hint: `ASwatchPickedInThePicker_WritesOneColourChange` and
`APenWithNoColour_OpeningAndDismissingThePicker_WritesNothing`.

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no new warning
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -n "\bPenColumn\b\|SortCommand\|PenTableColumns\|PenEditorColumnScale\|PenEditorColumnLogScaleOnStart\|PenEditorColumnColor\|PenEditorColumnMask\|PenEditorColumnLineStyle" -- SemiPlot docs/architecture   # prints nothing
```

The first frame of the opened window cannot be seen headlessly; the manual check under Post-Completion
covers it (no visible width jump on open).

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The grid owns the sort; the view model owns the collection view it sorts.** The header arrow is
  the header's `:sortascending`/`:sortdescending` pseudo-class, which the header reads only from a sort
  description in the grid's collection view (`DataGridColumnHeader.cs:172-190`,
  `DataGridColumn.cs:1157-1178`); a sort the window performs itself in the view model would never show
  the arrow. A plain list as `ItemsSource` gets a new view on every replacement
  (`DataGridDataConnection.cs:463-479`), so a refreshed list would lose the sort. Therefore
  `PenEditorViewModel` builds one `DataGridCollectionView` (`Avalonia.Collections`) over one
  `ObservableCollection<PenRowViewModel>` for its lifetime and exposes it as `PenRows`; the grid binds
  it, the grid writes its `SortDescriptions` on a header click, and a refresh replaces the collection's
  contents, so the view, its sort and the arrow survive. ASSUMPTION: `DataGridCollectionView` keeps its
  `SortDescriptions` across a `Clear` and re-sorts the replaced contents when the deferral ends;
  `Refresh_KeepsTheSortOrder` and `ARefresh_KeepsTheSortAndItsArrow` check it. `SortCommand`, `PenColumn`,
  `_isReorderingRows`, `Sort`, `Sorted`, `ComparisonOf` and `ByText` are deleted; the grid keeps the
  selection across the view's reset (`DataGrid.cs:2633-2662`). ASSUMPTION: the restore writes no
  transient null back to `SelectedRow`;
  `AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm` checks it and Task 4 names
  the fallback. ASSUMPTION: the grid selects no row on open and none on a refresh that keeps no
  selection, although the collection view tracks a current item; `WithNoPenSelected_ThePaneShowsOnlyTheHint`
  and `TheForm_IsEmptyAndDisabledWithNoRowAndShowsTheClickedPen` (until Task 5 replaces it) check it. If
  it fails, the view model calls `PenRows.MoveCurrentToPosition(-1)` after building the view and after
  each refresh, and Task 7 records it.
- **A press on a row ends the edit.** The `DataGrid` is focusable (`DataGrid.cs:761`). ASSUMPTION:
  `FocusManager` gives it the focus on a left press anywhere inside it, on a row, a header or the empty
  area, so the window's tunnel handler passes the press (`PenEditorWindow.axaml.cs:144-149`) and the
  field's `LostFocus` ends the edit; `ANameTypedThenAnotherRowClicked_WritesToThePenTheFieldShowed` checks
  a row, `ANameTypedThenAColumnHeaderClicked_WritesOnce` a header and
  `ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce` the empty area.
- **One comparer per sortable column, in one place.** `PenEditor/PenRowOrder` holds five static
  `IComparer` instances, `ById`, `ByName`, `ByUnit`, `ByEnabledOnStart` and `ByGroups`, each by its key
  and then by `Pen.Id`, text by `StringComparer.CurrentCultureIgnoreCase`, the rule
  `ComparisonOf` holds today (`PenEditorViewModel.cs:252-277`). Each column sets
  `CustomSortComparer="{x:Static local:PenRowOrder.ByName}"`, so the grid's description matches its column
  by comparer reference (`DataGridColumn.cs:1163-1168`). A descending sort reverses the whole order, the
  tie-break included, as `Sorted` does today (`PenEditorViewModel.cs:191`).
- **One row collection, one view.** The rows live in one private `ObservableCollection<PenRowViewModel>`, never replaced; `PenRows` is the only public surface over it. `PenGroupsViewModel` keeps receiving the new row array a refresh builds (`PenEditorViewModel.cs:217-218`), so the memberships stay in catalogue order whatever the list's sort.
- **Header-measured columns are one attached behaviour.** `Tables/HeaderMeasuredColumns` has two
  attached properties: `FitsColumns` on a `DataGrid` and `IsFill` on one `DataGridColumn`. While the grid
  is attached to the visual tree it handles `LayoutUpdated`:
  - The first time every visible column's header is realised, it measures each header unconstrained,
    sets the column's `MinWidth` to that width, and raises the column's `Width` to it when the declared
    width is smaller. The header is matched to its column through
    `DataGridColumn.GetColumnContainingElement` (`DataGridColumn.cs:681`), never through its text.
  - On every `LayoutUpdated` it computes the viewport, the headers presenter's width less the vertical
    scroll bar's width while that bar is visible, and the sum of the other visible columns'
    `ActualWidth`. When either changed since the last fit, the fill column's `Width` becomes
    `max(fill.MinWidth, viewport - others)`. The fill column therefore grows and shrinks with the grid,
    takes back what an operator's resize of another column gives up, and a column set wider than the
    viewport leaves the grid to scroll horizontally.
  - `LayoutUpdated` fires at the end of the layout pass that realised the headers, and an invalidation
    it causes is laid out again before the frame is committed (`LayoutManager.cs:116-179, :348-355`,
    `MediaContext.cs:187-222`), so the first frame shows the fitted widths. Nothing is posted to the
    dispatcher.
  - The fill column has a pixel width, never `Width="*"`: a star column ends a resize drag and leaves
    the last column no grab edge (`desktop-ui`, Tables).
  - The class handler of `FitsColumns` subscribes the grid's `AttachedToVisualTree` and
    `DetachedFromVisualTree` once and removes them when it turns false; `LayoutUpdated` is added on
    attach and removed on detach (`~/.claude/rules/avalonia.md`, lifecycle pairs).
- **The arrow slot is the theme's.** Semi's header lays the `SortIcon` out with a fixed
  `SemiWidthIconMedium` size and an 8 px right margin whether sorted or not (`DataGrid.axaml:130-144`,
  `PathIcon.axaml:26-28`), so an unsorted header measures as wide as a sorted one and the arrow never
  pushes the text into an ellipsis. `AnUnsortedHeader_ReservesTheSortArrowSlot` guards it. Semi's
  `12 0 0 0` padding and the icon's 8 px margin give the header its 12 px left and 8 px right insets.
- **The cell outline is the currency rectangle.** Semi's cell is not focusable (`DataGrid.axaml:33`), so
  its `FocusVisual` never shows; the outline the operator sees after a click is `Rectangle#CurrencyVisual`
  on `:current` (`:43-51`, `:96-98`). One window style hides it: `DataGrid#PenList DataGridCell
  /template/ Rectangle#CurrencyVisual` sets `IsVisible="False"`. The row keeps Semi's highlight, painted
  from the palette.
- **The empty state and the picker.** With no pen selected the pane shows only the hint; the form is
  `IsVisible="False"`. ASSUMPTION: an invisible control is not measured, so the `ColorPicker`'s template is
  not applied and the load-time lookup at `PenEditorWindow.axaml.cs:68` finds no flyout. The window therefore resolves the
  flyout whenever the picker applies its template: `OnLoaded` subscribes `FormColorPicker.TemplateApplied`
  and resolves once at load, `OnUnloaded` removes the subscription and the flyout handlers, and a
  resolve first detaches the flyout it held.
- **The invalid glyph is one window-scoped style.** `Panel#PenForm TextBox.invalid` sets
  `InnerRightContent` through a `<Template>` setter, one `PathIcon` per box (the form Semi's own
  picker theme uses for `Content`, `ColorPicker.axaml:23-36`; ASSUMPTION: the setter builds the template
  per target for `InnerRightContent` as it does for `Content`, which
  `AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` checks over two boxes): `WarningGeometry` from `Styles/Icons.axaml`, 16 px,
  `AppSeverityErrorBrush`, `Margin="0,0,8,0"`. The pen editor carries the rule first; another form adopts
  it when it is next touched, so `Forms.axaml` keeps its border rule unchanged.
- **Window size for this slice.** The tabs and the bottom bar stay, so the window keeps `Width="1272"`,
  `MinWidth="1260"` and `Height="720"`; the groups-pane slice sets the 1440 px opening width. The tab
  content becomes `*,16,420`: the bordered list, a 16 px gap, the pane. At `MinWidth` the list keeps about
  790 px, more than the five opening widths.
- **The form scrolls rather than clips.** The form sits in a vertical `ScrollViewer`
  (`Focusable="False"`, horizontal scrolling disabled) inside the pane, every form row is 32 px and every
  text row has an explicit height, so the form's height is fixed by the AXAML. `AtTheOpeningSize_ThePaneShowsTheWholeForm`
  asserts the extent fits the viewport at 1272 x 720; when it does not, `Height` becomes the smallest
  multiple of 8 above 720 that passes, recorded in `ui-theme.md`.

## Technical Details

### Packages and theme

- `SemiPlot/Directory.Packages.props`: `<PackageVersion Include="Avalonia.Controls.DataGrid" Version="12.0.1"/>`
  and `<PackageVersion Include="Semi.Avalonia.DataGrid" Version="12.0.0"/>`, in alphabetical order.
- `SemiPlot.UI.csproj`: `<PackageReference Include="Avalonia.Controls.DataGrid"/>` and
  `<PackageReference Include="Semi.Avalonia.DataGrid"/>`. The explicit 12.0.1 reference lifts Semi's
  12.0.0 dependency to the build that depends on Avalonia 12.0.5.
- `App.axaml`: `<semi:DataGridSemiTheme />` after `<semi:ColorPickerSemiTheme />`.
- `Styles/Palette.axaml`, both variants:

  | Key | Light | Dark |
  | --- | --- | --- |
  | `DataGridColumnHeaderForeground` | `#818594` | `#6F737A` |
  | `DataGridLineBrush` | `#EBECF0` | `#393B40` |
  | `DataGridRowSelectedBackground` | `#3574F0` at 0.25 | `#3574F0` at 0.25 |
  | `DataGridRowSelectedPointeroverBackground` | `#3574F0` at 0.25 | `#3574F0` at 0.25 |

  Top level, beside the five radii: `DataGridRowCornerRadius` and `DataGridColumnHeaderCornerRadius`,
  both 4. The hover overlay (`DataGridRowPointeroverBackground`) and the header's hover and press fills
  stay Semi's translucent overlays, as the list rows' do (`ui-theme.md:93-96`). The marker probe of Task 1
  decides whether any further key the pen list reaches joins the table.

### Resources

| Key | English | Russian | Use |
| --- | --- | --- | --- |
| `PenFormEmptyHint` | Select a pen in the list on the left | Выберите перо в списке слева | the pane with no pen selected |
| `PenFormSectionLabel` | Label | Подпись | section header |
| `PenFormSectionAppearance` | Appearance | Вид | section header |
| `PenFormSectionOnStart` | On start | При запуске | section header |
| `PenFormVisibilityLabel` | Visibility | Видимость | row label |
| `PenFormShowOnChart` | Show on the chart | Показывать на графике | the visibility checkbox's text |
| `PenFormScaleTypeLabel` | Scale type | Тип шкалы | row label |
| `PenFormLogarithmic` | Logarithmic | Логарифмическая | the log checkbox's text |

- Renamed, value unchanged, because no column reads them any more: `PenEditorColumnColor` to
  `PenFormColorLabel`, `PenEditorColumnMask` to `PenFormMaskLabel`, `PenEditorColumnLineStyle` to
  `PenFormLineStyleLabel`.
- Deleted, every consumer gone: `PenEditorColumnScaleMinOnStart`, `PenEditorColumnScaleMaxOnStart`,
  `PenEditorColumnLogScaleOnStart`.
- Reused: the headers `PenEditorColumnId`, `PenEditorColumnName`, `PenEditorColumnUnit`,
  `PenEditorColumnOnStart`, `PenEditorColumnGroups`; the labels `PenEditorColumnName` and
  `PenEditorColumnUnit` (one term per concept), `PenFormMaskPreviewLabel`, and the axis scale panel's
  `AxisScaleMinimumLabel` and `AxisScaleMaximumLabel` ("Minimum" / "Минимум", "Maximum" / "Максимум").
- The title is two `TextBlock`s, the id and the name, with no format key, as the axis scale panel joins
  name and unit (`ui-text.md:313-314`).
- The scale messages keep their "Initial scale:" / "Начальная шкала:" opening: they name the stored
  pair, which the section "On start" holds.

### Glyphs

`Styles/Icons.axaml` gains two 16 x 16 geometries; neither is text, so neither is a resource string and
the `.cs` sources stay ASCII (`ui-text.md:3-10, :484-486`):

```xml
<StreamGeometry x:Key="CheckGeometry">M6.2,11.6 L2.6,8 L1.5,9.1 L6.2,13.8 L14.5,5.5 L13.4,4.4 Z</StreamGeometry>
<StreamGeometry x:Key="WarningGeometry">F0 M8,1.5 L15,14.5 L1,14.5 Z M7.25,6 L8.75,6 L8.75,10.5 L7.25,10.5 Z M7.25,11.75 L8.75,11.75 L8.75,13.25 L7.25,13.25 Z</StreamGeometry>
```

### The pen list

```xml
<Border Grid.Column="0" BorderBrush="{DynamicResource AppBorderBrush}" BorderThickness="1"
        CornerRadius="4" Padding="4">
    <DataGrid Name="PenList"
              ItemsSource="{Binding PenRows}"
              SelectedItem="{Binding SelectedRow, Mode=TwoWay}"
              IsReadOnly="True" SelectionMode="Single" HeadersVisibility="Column"
              GridLinesVisibility="None" CanUserSortColumns="True" CanUserResizeColumns="True"
              CanUserReorderColumns="False"
              RowHeight="{x:Static local:PenEditorWindow.RowHeight}"
              tables:HeaderMeasuredColumns.FitsColumns="True">
        <DataGrid.Columns>
            <!-- one DataGridTemplateColumn per column; CellTemplate DataTemplate x:DataType="local:PenRowViewModel" -->
        </DataGrid.Columns>
    </DataGrid>
</Border>
```

| Column | Header | Opening width | Comparer | Cell |
| --- | --- | --- | --- | --- |
| Id | `PenEditorColumnId` | 84 | `ById` | `TextBlock` `RowId`, `Pen.Id` |
| Name | `PenEditorColumnName` | 220 | `ByName` | `Grid` `Auto,*` 8 apart: `Border` `RowColor` 12 x 12, `CornerRadius="6"`, `Background` through `LegendConverters.HexToBrush`, `BorderBrush` `AppBorderBrush`, `BorderThickness="1"`; `TextBlock` `RowName`, `Pen.Name` |
| Unit | `PenEditorColumnUnit` | 96 | `ByUnit` | `TextBlock` `RowUnit`, `Pen.Unit` |
| On start | `PenEditorColumnOnStart` | 120 | `ByEnabledOnStart` | `PathIcon` `RowEnabledOnStart`, `CheckGeometry`, 12 x 12, centred, `IsVisible="{Binding Pen.EnabledOnStart}"` |
| Groups | `PenEditorColumnGroups` | 96, then the fill rule | `ByGroups` | `TextBlock` `RowGroups`, `GroupsText`, `AppSecondaryForegroundBrush`, `tables:HeaderMeasuredColumns.IsFill="True"` |

- Every text cell is `TextBlock.grid-cell`: `Margin="12,0,8,0"`, `VerticalAlignment="Center"`,
  `TextTrimming="CharacterEllipsis"`, `ToolTip.Tip` bound to the same value as `Text`. The fill
  column's cell is `Margin="12,0,12,0"`, so its text ends 12 px before the vertical scroll bar
  (`desktop-ui`, Tables). The name cell's grid carries the 12/8 margin; its `TextBlock` `RowName` is a `grid-cell` with `Margin="0"`, so it trims and carries its tooltip at one inset.
  The glyph cell cannot truncate: its column opens no narrower than its header, wider than the 12 px
  glyph, so it carries no tooltip.
- Every column is a `DataGridTemplateColumn` with `CanUserSort="True"`, `CanUserResize="True"` and its `CustomSortComparer`. A template column with no `SortMemberPath` otherwise derives `CanUserSort` from the row type, which is not `IComparable`, and the header click does nothing (`DataGridColumn.cs:329-354`, `DataGridColumnHeader.cs:244-249`).
- Header text renders at `FontSize="12"` and `FontWeight="SemiBold"`: a window style on
  `DataGrid#PenList DataGridColumnHeader`.
- `Window.Resources` gains `<x:Double x:Key="DataGridCellMinHeight">28</x:Double>`, so Semi's 32 px
  cell minimum (`Shared.axaml:3`) does not raise the 28 px `RowHeight`. ASSUMPTION: the cell theme reads
  the key through `DynamicResource` (`DataGrid.axaml:32`), so a window-scoped key reaches it; the 28 px
  row assertion of `ARow_ShowsThePenAsStoredUnderItsHeader` checks it.
- The window style that hides `Rectangle#CurrencyVisual` is the only cell-outline rule.

### HeaderMeasuredColumns

```csharp
namespace SemiPlot.UI.Tables;

public static class HeaderMeasuredColumns
{
	public static readonly AttachedProperty<bool> FitsColumnsProperty =
		AvaloniaProperty.RegisterAttached<DataGrid, bool>("FitsColumns", typeof(HeaderMeasuredColumns));

	public static readonly AttachedProperty<bool> IsFillProperty =
		AvaloniaProperty.RegisterAttached<DataGridColumn, bool>("IsFill", typeof(HeaderMeasuredColumns));
	// Get/Set pairs; a class handler on FitsColumnsProperty subscribes attach/detach once and adds LayoutUpdated at once when the grid is already attached.
}
```

- Per-grid state (whether the headers were measured, the last viewport and the last sum of the other
  columns) lives in one private attached property holding a small private `Fit` class; no static mutable
  state.
- Headers: `grid.GetVisualDescendants().OfType<DataGridColumnHeader>()` mapped through
  `DataGridColumn.GetColumnContainingElement(header)` and kept when `grid.Columns` contains the column,
  which leaves out the top-left header and the filler header. Measuring waits until every visible
  column has a header.
- Minimum: `header.Measure(Size.Infinity)`, `Math.Ceiling(header.DesiredSize.Width)`, then
  `header.InvalidateMeasure()` so the next pass measures it in its column again.
- Viewport: the `DataGridColumnHeadersPresenter` named `PART_ColumnHeadersPresenter`, less the `ScrollBar`
  named `PART_VerticalScrollbar` when it `IsVisible`, both found by name among the grid's visual
  descendants.
- The fill rule writes only when the target differs from `fill.Width.Value` by more than 0.5 px, so the
  pass it triggers computes the same target and writes nothing.

### The pen pane

Inside the tab, `Grid ColumnDefinitions="*,16,420"`; the pane is `Border Name="PenFormPanel"` in column
2, `Width="420"`, `Padding="16"`, `BorderBrush` `AppBorderBrush`, `BorderThickness="1"`,
`CornerRadius="4"`, `IsEnabled="{Binding !IsRefreshing}"`. It holds a `Panel`:

- `TextBlock Name="PenFormEmptyHint"`, `PenFormEmptyHint`, centred both ways,
  `AppSecondaryForegroundBrush`, visible while `SelectedForm` is null.
- `ScrollViewer Name="PenFormScroller"`, `Focusable="False"`, `HorizontalScrollBarVisibility="Disabled"`,
  visible while `SelectedForm` is not null, holding `Panel Name="PenForm"`
  (`DataContext="{Binding SelectedForm}"`) and in it a `StackPanel x:DataType="local:PenFormViewModel"`:
  1. Title, `Grid` `Auto,*` 8 apart, `Height="24"`: `TextBlock PenFormTitleId` (`Row.Pen.Id`,
     `AppSecondaryForegroundBrush`) and `TextBlock PenFormTitleName` (`Row.Pen.Name`, trimmed, tooltip),
     both `FontSize="16"`, `FontWeight="SemiBold"`.
  2. Section header `TextBlock.form-section` `PenFormSectionLabel`: `FontWeight="SemiBold"`,
     `Height="20"`, `Margin="0,16,0,8"`. Then a grid with the window resource `PenFormColumns`
     (`x:Shared="False"`: 112, 240), `ColumnSpacing="12"`, `RowSpacing="8"`, rows `32,32,32,32`:
     Name (`FormName`, 240), Unit (`FormUnit`, 120), Mask (`FormMask`, 120), Preview
     (`FormMaskPreview`, a trimmed `TextBlock` with tooltip, `AppSecondaryForegroundBrush`).
  3. `PenFormSectionAppearance`, then rows `32,32`: Colour (a horizontal `StackPanel` 8 apart:
     `FormColor`, 120, and `FormColorPicker`, 32 x 32, `Padding="0"`), Line (`FormLineStyle`, 240).
  4. `PenFormSectionOnStart`, then rows `32,Auto,32`: Visibility (`FormEnabledOnStart`, content
     `PenFormShowOnChart`); `FormScale` in row 1 spanning both columns, its own `PenFormColumns` grid with
     the same spacing and rows `32,32`: Minimum (`FormScaleMinOnStart`, 120) and Maximum
     (`FormScaleMaxOnStart`, 120); Scale type (`FormLogScaleOnStart`, content `PenFormLogarithmic`).
  5. `FormMessage`, `TextBlock.form-message.error`, `Margin="0,16,0,0"`.
- Every label is `TextBlock.form-label` in column 0; every field `HorizontalAlignment="Left"` in column 1
  with its width set on itself. The `x:Name`s and `local:PenEditorWindow.Field` attachments of today's
  fields stay, so the code-behind's routing and the edit tests read the same controls.
- Window styles: `ColorPicker#FormColorPicker /template/ DropDownButton /template/ :is(Control)#DropDownGlyph`
  sets `IsVisible="False"` (the glyph's templated parent is the `DropDownButton`, so the selector nests two
  `/template/` steps, as `Forms.axaml:9` does for `NumericUpDown`), so the 32 px picker is the swatch alone; `Panel#PenForm TextBox.invalid` sets the
  warning `InnerRightContent` (Solution Overview).
- Removed: the `PenTableColumns` resource, the `Button.column-header` and `Grid#FormScale TextBox`
  styles, `ListBox#PenTable ListBoxItem` from the list-item style, the `ScaleLabel` wrapper grid. Kept for
  the `Groups` tab: `IdColumnWidth`, `MembershipColumns`, `TextBlock.cell`, `ListBox#GroupList
  ListBoxItem`, `LineStyleItem`.

Height of the form, from the AXAML: title 24; three section headers at 16 + 20 + 8; rows 4 x 32 + 3 x 8,
2 x 32 + 8, 32 + (2 x 32 + 8) + 32 + 2 x 8; message 16 + 40. That is 24 + 132 + 152 + 72 + 152 + 56 =
588 px, plus 32 px of padding and 2 px of border.

### Test helpers

- `PenEditorWindowDriver.PenListOf(window)` returns `Named<DataGrid>(window, "PenList")`.
- `PenEditorWindowDriver.RowAt(window, index)` returns the realised `DataGridRow` whose `GetIndex()` is
  `index`, scrolling it into view first with `ScrollIntoView(item, null)` and `RunJobs()` when it is not
  realised.
- `PenEditorWindowDriver.HeaderOf(window, header)` returns the realised `DataGridColumnHeader` whose
  `Content` equals the header text.
- `RealisedPens(DataGrid)` reads the realised rows ordered by `GetIndex()`.

## What Goes Where

- **Implementation Steps**: packages, palette, the behaviour, the comparers, the list, the pane, their
  tests, and the `ui-theme.md`, `ui-text.md` and `overview.md` updates.
- **Post-Completion**: the manual check in the running viewer, the remaining roadmap slices.

## Implementation Steps

### Task 1: Add the DataGrid packages, its Semi theme and its palette keys

**Files:**
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] add both `PackageVersion` entries and both `PackageReference` items (Technical Details); run
      `dotnet restore SemiPlot.slnx` and confirm `SemiPlot/SemiPlot.UI/obj/project.assets.json` lists
      `Avalonia.Controls.DataGrid/12.0.1`, `Semi.Avalonia.DataGrid/12.0.0` and `Avalonia/12.0.5`. A 401
      from a private feed means a source outside the repository answered: rerun with
      `--source https://api.nuget.org/v3/index.json`
- [x] add `<semi:DataGridSemiTheme />` to `App.axaml` after `ColorPickerSemiTheme`
- [x] write the failing theory `EveryDataGridSurface_PaintsItselfFromThePalette` (`Light`: text
      `#000000`, header `#818594`, line `#EBECF0`; `Dark`: `#DFE1E5`, `#6F737A`, `#393B40`): a `DataGrid`
      of two rows and one text column, row 0 selected, in a shown `Window`; read the header's
      `Foreground`, `Rectangle#PART_ColumnHeadersAndRowsSeparator`'s `Fill`, the selected row's
      `Border#BackgroundBorder` `Background` (`#3574F0` at 0.25), the same after `PointAt` hovers the
      selected row, and the cell text's foreground
- [x] run the marker probe the palette was built by (`ui-theme.md:27-31`): give each of the nineteen
      keys of Semi's DataGrid `Light.axaml` a distinct marker colour in a scratch test, read back every
      brush in the visual tree of a grid at rest, hovered, pressed, selected and with a sorted header;
      a key that a surface reaches and whose Semi value is not the JetBrains one joins the palette with
      a row in the test; delete the scratch test. A reached text key maps to the palette's
      `#000000`/`#DFE1E5`, a secondary-text key to `#818594`/`#6F737A`, a line key to
      `#EBECF0`/`#393B40`, and a selection key to the accent at 0.25; a reached key outside these four
      roles is a ⚠️ in this plan, not a silent addition
- [x] add the four brush keys to both variants and the two radii at the top level of `Palette.axaml`
- [x] extend `EveryCornerRadius_ComesFromThePalette` with a `DataGrid`: its row's
      `Border#BackgroundBorder` and a header's `Border#HeaderBackground` read `CornerRadius(4)`; add
      `DataGridRowSelectedBackground` and `DataGridRowSelectedPointeroverBackground` to
      `APaletteKeyCarryingOpacity_KeepsItUnderBothVariants`
- [x] run `ThemeTests`: every case passes
- ⚠️ the marker probe found two cell keys a surface reaches outside the table, both left Semi's:
  `DataGridCellCurrentBorderBrush` paints `Rectangle#CurrencyVisual`, visible at rest on the current
  cell, which Task 4's window style hides; `DataGridCellFocusBorderBrush` (Semi `#C6CACD`, outside the
  four roles) paints the unnamed `Rectangle` in `Panel#FocusVisual`
  (`DataGridCell /template/ Grid#PART_CellRoot`), effectively visible from the first press on a row
  onward. The Solution Overview's claim that the cell's `FocusVisual` never shows does not hold
  headlessly: Task 4 also hides `DataGrid#PenList DataGridCell /template/ Panel#FocusVisual`, or
  `AClickedRow_ShowsTheRowHighlightAndNoCellOutline` fails. The hover and press overlays and the
  three transparent backgrounds need no key
- ⚠️ `DataGridRow.GetIndex()` is obsolete in 12.0.1 (CS0618, an error under `TreatWarningsAsErrors`);
  the tests read `DataGridRow.Index`, and Task 4's `RowAt` and `RealisedPens` do the same
- ⚠️ the restore writes `project.assets.json` to `SemiPlot/Artifacts/obj/SemiPlot.UI/`

### Task 2: Add the header-measured column sizing behaviour, tests first

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Tables/HeaderMeasuredColumns.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Tables/HeaderMeasuredColumnsTests.cs`

- [x] create the test class with the three traits (`UI`, `Chart`, `Unit`); its stand is a `Window` of
      a given width holding a `DataGrid` over a list of small records, three text columns with string
      headers and `FitsColumns` set, the last column `IsFill`; it shows the window and drives
      `Dispatcher.UIThread.RunJobs()`, except where a test says otherwise
- [x] write the failing tests `AColumnNarrowerThanItsHeader_OpensAtTheHeadersUnconstrainedWidth` (a
      column declared `Width="10"`: `MinWidth` and `ActualWidth` equal the ceiling of that header's
      `DesiredSize.Width` after `Measure(Size.Infinity)`) and
      `AColumnDeclaredWiderThanItsHeader_KeepsItsDeclaredWidth` (a 300 px column stays 300, its
      `MinWidth` is its header's width)
- [x] write the failing test `TheWidths_AreFittedInTheLayoutPassThatRealisesTheHeaders`: `Show()` and
      `window.UpdateLayout()` with no `RunJobs()`; the narrow column's `Width.Value` already equals its
      header's width. A fit posted to the dispatcher fails it
- [x] write `AnUnsortedHeader_ReservesTheSortArrowSlot`: the header's unconstrained width is equal
      before and after a click sorts its column (`HeadlessInput.Click` on the header), and
      `PathIcon#SortIcon` is visible with a positive width while unsorted. If it fails, add
      `Styles/Tables.axaml` with `DataGridColumnHeader /template/ PathIcon#SortIcon` `IsVisible="True"`,
      include it from `App.axaml`, and record the fact under Solution Overview
- [x] write the failing tests `TheFillColumn_TakesTheViewportTheOtherColumnsLeave`,
      `TheFillColumn_ShrinksWithTheGridDownToItsMinimumAndThenTheGridScrolls` (narrow the window: the fill
      column shrinks, stops at its `MinWidth`, and the headers presenter's extent exceeds its width),
      `TheFillColumn_GrowsBackWhenTheGridWidens`, `AResizedColumn_GivesItsDifferenceToTheFillColumn` (set
      the first column's `Width` 40 px wider: the fill column is 40 px narrower),
      `AVisibleVerticalScrollBar_IsLeftOutOfTheViewport` (200 rows: the columns sum to the headers
      presenter's width less the visible vertical bar's width) and
      `AGridDetachedAndAttachedAgain_KeepsFittingTheFillColumn` (remove the grid from the window, add it
      back, widen the window: the fill column takes the new width)
- [x] implement `HeaderMeasuredColumns` as Technical Details states: the two attached properties, the
      class handler that subscribes attach and detach once, `LayoutUpdated` added on attach and removed
      on detach, the one-time header measure, the fill rule; no `Dispatcher.Post`
- [x] run `HeaderMeasuredColumnsTests`: every case passes
- ⚠️ `AnUnsortedHeader_ReservesTheSortArrowSlot` passed before the behaviour existed: Semi's header
  reserves the arrow slot itself, so no `Styles/Tables.axaml` was added
- ⚠️ the behaviour caches the headers presenter and the vertical scroll bar and finds them again only
  when the cached presenter is no longer inside the grid, so a layout pass walks no row's visuals

### Task 3: Move the sort comparisons into PenRowOrder, tests first

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenRowOrder.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenRowOrderTests.cs`

- [x] write the failing `[AvaloniaTheory]` `EachOrder_SortsByItsKeyThenById` over the fixture pens `Argon`,
      `Pressure`, `Power` with the groups `Chamber` and `Gas` (`PenEditorFixture.cs:9-22`): `ById`
      3, 7, 12; `ByName` 3, 12, 7; `ByUnit` 7, 3, 12; `ByEnabledOnStart` 3, 7, 12; `ByGroups` 12, 7, 3
      (the orders `PenEditorViewModelTests.cs:294-304` pins today)
- [x] write the failing test `TheTextOrders_IgnoreCase`: two pens named `alpha` and `Beta` order alpha
      first, and two pens differing only in case order by id
- [x] create `PenRowOrder` with the five `public static readonly IComparer` comparers, each
      `Comparer<PenRowViewModel>.Create` over the key then `Pen.Id`; text keys through
      `StringComparer.CurrentCultureIgnoreCase`
- [x] run `PenRowOrderTests`: every case passes

### Task 4: Replace the pen table with the DataGrid pen list

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Icons.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorWindowDriver.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`

- [x] update `PenEditorWindowDriver` (`PenListOf`, `RowAt`, `HeaderOf`) and `RealisedPens`; update
      `TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart` to read the On start
      column's `Header` and `PenList`'s visibility; drop the `RowLineStyle` and `RowEnabledOnStart`
      checkbox reads of `ALineStyleChosenAndTheStartBoxClicked_WriteOneChangeEach` (read the glyph
      instead) and the `RowLogScaleOnStart` read of `TheLogBoxClicked_WritesTheFlagAndTheRowShowsIt`
      (rename it `TheLogBoxClicked_WritesTheFlag`); delete
      `TheLogColumn_ShowsTheFlagReadOnlyAndSortsOnAHeaderClick`, whose column no longer exists, and
      `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthClipsNoFixedColumn`, which reads the deleted
      `PenTableHeader` and `PenTable` (`PenEditorViewTests.cs:190`, `:200`), and the `FixedColumnCount`
      constant it alone reads (`PenEditorViewTests.cs:39`); Task 5 writes its replacement
- [x] write the failing view test `ThePenList_RealisesNoCheckBox`: no `CheckBox` among the visual
      descendants of `PenList`; every row's `RowEnabledOnStart` is a `PathIcon` visible for `Pressure`
      and `Power` and hidden for `Argon`
- [x] rewrite `ARow_ShowsThePenAsStoredUnderItsHeader`: `RowId`, `RowName`, `RowUnit`, `RowGroups` as
      stored; `RowColor`'s brush is `Argon`'s colour and `Transparent` for `Power`; `RowColor` lies inside
      the bounds of the name column's cell; a row is 28 px high
- [x] write `EveryTextCell_TrimsWithAnEllipsisAndCarriesItsTextAsATooltip`: every `TextBlock.grid-cell`
      in a realised row has `TextTrimming.CharacterEllipsis` and `ToolTip.GetTip` equal to its `Text`
- [x] rewrite `AHeaderClick_ReordersTheRowsAndKeepsTheSelectedPen` as
      `AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm`: click row 1, click the
      name header: `Argon, Power, Pressure`, the header carries `:sortascending`, the grid's selected item
      and `viewModel.SelectedForm` are the same instances; click again: reversed, `:sortdescending`,
      same selection and form; `_editor.Calls` empty. If the selection or the form changes across the
      sort, bind `SelectedItem` `Mode=OneWay` and write `SelectedRow` from the grid's `SelectionChanged`
      only for a non-null `SelectedItem`, and record it under Solution Overview
- [x] write the theory `EachHeaderClick_OrdersTheListByItsColumn` over the five header keys and the view
      catalogue `[Argon, Pressure, Power]` with `[Chamber, Spare]` (`PenEditorViewTests.cs:43`): Id 3, 7,
      12; Name 3, 12, 7; Unit 7, 3, 12; On start 3, 7, 12; Groups 12, 3, 7 (`GroupsText` is "Chamber",
      "Chamber" and empty), and `ARefresh_KeepsTheSortAndItsArrow` (sort Id
      descending, refresh to four pens: `4242, 12, 7, 3`, the Id header still `:sortdescending`)
- [x] rewrite `AFullCatalogue_RealisesOnlyTheRowsOnScreen` to count realised `DataGridRow`s
- [x] write `EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader` (`CanUserResize` on every column;
      `ActualWidth >= MinWidth` and `MinWidth` equal to the header's unconstrained width) and
      `AClickedRow_ShowsTheRowHighlightAndNoCellOutline` (after a click on row 1, its
      `Border#BackgroundBorder` paints the selection brush and no cell of the grid shows a visible
      `Rectangle#CurrencyVisual` or `Panel#FocusVisual`)
- [x] write `ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce`: retype the name, click the list
      below its last row: one name change, the field no longer focused. If it fails, the window's tunnel
      press handler (`PenEditorWindow.axaml.cs:136-149`) also clears the focus for a press inside
      `PART_RowsPresenter` with no `DataGridRow` among its ancestors, and Task 7 records that route
- [x] write `ANameTypedThenAColumnHeaderClicked_WritesOnce`: retype the name, click the Id header: one
      name change, the field no longer focused. If it or the empty-area test fails, the fallback widens
      to "a press inside `PenList` focuses `PenList`" in the window's tunnel handler, and Task 7 records
      that route
- [x] in the view model: delete `PenColumn`, `SortCommand`, `_sortColumn`, `_isSortDescending`,
      `_isReorderingRows`, `Sort`, `Sorted`, `ComparisonOf` and `ByText`; hold the rows in one private `ObservableCollection<PenRowViewModel>`; delete `Rows`; tests read
      `PenRows.Cast<PenRowViewModel>()`; expose `PenRows`, a `DataGridCollectionView` over the collection
      built once in the constructor; the refresh replaces the collection's contents inside
      `using (PenRows.DeferRefresh())`, so the view re-sorts once on dispose, and then sets
      `SelectedRow`; `SelectedRow` becomes a plain
      `RaiseAndSetIfChanged` property
- [x] rewrite `Refresh_KeepsTheSortOrder` to add
      `DataGridSortDescription.FromComparer(PenRowOrder.ById, ListSortDirection.Descending)` to
      `PenRows.SortDescriptions` and read `PenRows`; write `PenRows_ListsEveryRowInCatalogueOrderUntilSorted`; rewrite the failed-registration assertion (`PenEditorViewModelTests.cs:121-128`) as
      `PenRows.Cast<PenRowViewModel>().Should().Equal(rows)` over a snapshot
      `rows = [.. editor.PenRows.Cast<PenRowViewModel>()]`, and every other read of `editor.Rows` in
      `PenEditorViewModelTests`, `MainWindowViewModelTests.cs:69, :123` and `MainWindowViewTests.cs:69`
      as a read of `PenRows`; delete `ASort_OrdersTheRowsByTheColumnThenById`,
      `ASecondSortOnTheSameColumn_ReversesTheRowsAndKeepsTheSelectionAndItsForm` and
      `ASortOnAnotherColumn_StartsAscending` (`PenRowOrderTests` and the header-click view tests own those orders); in
      `PenGroupsViewModelTests.cs:214` sort through `PenRows.SortDescriptions` instead of `SortCommand`
- [x] add `CheckGeometry` to `Icons.axaml`; add `xmlns:tables="clr-namespace:SemiPlot.UI.Tables"` to the
      window root; in `PenEditorWindow.axaml` replace the header grid and the `ListBox` (`:90-264`) with
      the bordered `DataGrid` of Technical Details, and change the tab's grid from `Auto,*,Auto`
      (`:87-88`) to `*,Auto`: the list in row 0, today's form panel unchanged in row 1 at full width. The
      old form grid's `*` boxes need its 742 to 877 px natural width (`ui-theme.md:418-419`), so the
      `*,16,420` columns arrive with the new pane in Task 5. Add the `DataGridCellMinHeight` resource, the
      header font style and the `CurrencyVisual` style; delete `PenTableColumns`, `Button.column-header`
      and the `ListBox#PenTable ListBoxItem` selector
- [x] run `PenEditorViewTests`, `PenGroupsViewTests`, `PenEditorViewModelTests`,
      `PenGroupsViewModelTests`, `MainWindowViewTests` and `MainWindowViewModelTests`: every case passes
- ⚠️ `DataGridCollectionView.DeferRefresh()` throws `InvalidOperationException` ("Cannot change or check the
  contents ... while Refresh is being deferred") on the first `Add` inside it, so the refresh clears and
  refills the collection with no deferral; the view inserts each added row at its sorted place, and
  `Refresh_KeepsTheSortOrder` and `ARefresh_KeepsTheSortAndItsArrow` pass
- ⚠️ the header click wrote a transient null back through the two-way `SelectedItem` binding, which rebuilt
  the form, so the plan's fallback is in: `SelectedItem` binds `Mode=OneWay` and the window's
  `OnPenListSelectionChanged` writes `SelectedRow` only for a non-null `SelectedItem`.
  `ARefresh_KeepsTheSortAndItsArrow` also checks that the list follows a selection the view model writes
  after a click
- ⚠️ the outline rule needs the theme's own triggers: a plain `DataGridCell /template/ Rectangle#CurrencyVisual`
  setter loses to Semi's `:current` trigger, so the one window style is
  `DataGridCell:current /template/ Rectangle#CurrencyVisual, DataGridCell:focus /template/ Panel#FocusVisual`
- ⚠️ the fill column's header is measured while the filler header still stands right of it, so its
  `VerticalSeparator` (1 px) is shown then and hidden once the fill closes the gap: the Groups minimum is
  1 px over the header's later unconstrained width. `EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader`
  asserts the minimum between the header's width and 1 px more
- ⚠️ a cell's tooltip binds `{Binding $self.Text}`: bound to `Pen.Id` it carries the boxed `int`, not the text
- ⚠️ `PenEditorFixture.RowsOf(editor)` reads `PenRows.Cast<PenRowViewModel>()` for the view-model tests, and
  `RowAt` scrolls through `DataGridRow.Index`, not the obsolete `GetIndex()`; the failed-read test compares
  the rows by `Equal`, since `PenRows` is no longer a replaced list
- ⚠️ the new view tests were written with the implementation, not run red first: at HEAD they cannot
  compile, because `PenList`, `PenRows` and `DataGridRow` reads do not exist there

### Task 5: Rebuild the pen form as the sectioned 420 px pane

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Icons.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/InterMeasure.cs` (internal static `InterTypeface`, `InterFont`)

- [x] replace `TheForm_IsEmptyAndDisabledWithNoRowAndShowsTheClickedPen` with the failing tests
      `WithNoPenSelected_ThePaneShowsOnlyTheHint` (`PenFormEmptyHint` on screen with
      `Resources.PenFormEmptyHint`, `PenFormScroller` not visible) and
      `AClickedPen_FillsTheFormUnderAOneLineTitle` (the old test's field reads after `RowAt(window, 1)`,
      plus `PenFormTitleId` `7` left of `PenFormTitleName` `Chamber pressure` on one line, the hint gone)
- [x] write the failing tests `TheForm_HasThreeSectionsOnOneLabelColumnAndOneFieldEdge` (the three
      section headers in order; every `form-label` shares one left edge; every field, the two
      `CheckBox` controls included, starts 124 px right of it, each read as `LeftOf(control, window)` as
      `PenEditorViewTests.cs:951-952` reads the log box), `EveryField_HasTheWidthOfItsContentType`
      (Name and Line 240; Unit, Mask, Colour, Minimum, Maximum 120), `TheColourSwatchPicker_IsSquare`
      (`FormColorPicker` 32 x 32, 8 px right of `FormColor`, its `DropDownGlyph` not visible) and
      `ThePane_Stays420PxWideWhenTheWindowWidens`
- [x] write the failing tests `AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` (an
      unusable mask and an empty name: `FormMask` and `FormName` each carry `invalid`, and each one's
      `InnerRightContent` is a distinct visible `PathIcon` whose `Data` is `WarningGeometry` and whose
      bounds lie inside its box; the boxes', the pane's and the window's `Bounds` are unchanged) and `AValidField_ShowsNoWarningGlyph`
- [x] write `AtTheOpeningSize_ThePaneShowsTheWholeForm` (at 1272 x 720 the scroller's
      `Extent.Height <= Viewport.Height`; when it fails, set `Height` to the smallest multiple of 8 above
      720 that passes and record the value for Task 7) and
      `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum`, the
      replacement of the test Task 4 deletes (the window opens below
      1280 px; at `MinWidth` the pane is 420 px, every column's `ActualWidth >= MinWidth`, the columns
      sum to no more than the list's viewport)
- [x] write the theory `EveryPenFormLabel_FitsTheLabelColumnInInter` (`en`, `ru` through
      `CultureScope`): each of the ten label texts, measured with Inter at the label's `FontSize` through
      Skia (the `CsvExportWindowTests.cs:1214-1223` helpers, moved to `UI/InterMeasure.cs`, which both classes
      call), is at most 112 px
- [x] add the eight keys of Technical Details to both resx files, rename the three keys, delete the
      three keys; both files hand-edited, no byte order mark
- [x] add `WarningGeometry` to `Icons.axaml`; rebuild the pane in `PenEditorWindow.axaml` as Technical
      Details states (hint, scroller, title, three sections, `FormScale` as the pair's grid, message
      line), with the picker style and the invalid-glyph style; delete `Grid#FormScale TextBox` and the
      `ScaleLabel` wrapper
- [x] in `PenEditorWindow.axaml.cs`, resolve the picker's flyout on `FormColorPicker.TemplateApplied`
      as well as at load, detaching the previous flyout first, and remove the subscription in
      `OnUnloaded`
- [x] update `FormLabel` callers to the renamed keys; confirm the unchanged guards pass:
      `ASwatchPickedInThePicker_WritesOneColourChange`, `AfterAPickInOneRow_ThePickerShowsTheNextRowsColour`,
      `APenWithNoColour_OpeningAndDismissingThePicker_WritesNothing`,
      `AnUnusableMask_WritesNothingRevertsMarksAndSaysWhyWithoutResizing` (panel `Bounds` unchanged),
      `TheLogBoxOverAStoredMinimumOfZero_PutsTheBoxBackAndSaysWhyWithoutResizing` (the box lines up with
      the minimum, above the message line), `BothBoundsTypedThenTheFormsEmptyAreaClicked_WritesOneScaleChange`
- [x] run `PenEditorViewTests`, `PenGroupsViewTests`, `PenFormViewModelTests` and `ResourcesTests`:
      every case passes
- ⚠️ the four section grids carry `HorizontalAlignment="Left"`: stretched across the 378 px scroller
  viewport, the `112,240` grid widened its two pixel columns by a pixel each, and every field stood 125 px
  right of the label edge instead of 124
- ⚠️ `AtTheOpeningSize_ThePaneShowsTheWholeForm` passes at `Height="720"`, so the window height stays and
  Task 7 records no new value
- ⚠️ the empty-state ASSUMPTION holds: with the subscription to `FormColorPicker.TemplateApplied` removed,
  `ASwatchPickedInThePicker_WritesOneColourChange` and `AfterAPickInOneRow_ThePickerShowsTheNextRowsColour`
  fail, so the load-time lookup alone finds no flyout
- ⚠️ no `FormLabel` caller read a renamed key: `ANameTypedThenAFormLabelClicked_WritesOnce` reads
  `PenEditorColumnName` and `PenEditorColumnUnit`, which the form reuses
- ⚠️ the title's two blocks share a window style `TextBlock.form-title` (16 px, SemiBold, centred) rather
  than repeating the two setters

### Task 6: Verify acceptance criteria

- [x] run the filtered command under Acceptance Evidence: 0 failed, every named test present (242
      passed, all 41 named tests found in the trx)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed (2660 passed)
- [x] run `dotnet build SemiPlot.slnx`: 0 errors and no warning beyond the ones `master` already reports
      (2 NU1902 for `MessagePack` 2.5.302 in `SemiPlot.AppHost`, which predate this branch and
      `hex-color-input`; no AppHost file in the diff, no package version changed)
- [x] set `$BASE` to the stacking base: `hex-color-input` while that branch is unmerged, `master` after
      it merges (`git merge-base --is-ancestor hex-color-input master`) (exit 1: `$BASE` is
      `hex-color-input`)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and
      `dotnet terse` over every `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
- [x] run the HeadlessInput gate under Acceptance Evidence, and the key gate with `-- SemiPlot` only:
      both print what they must (only `HeadlessInput.cs` lines; the key gate prints nothing)
- [x] confirm the scope guard with `git diff $BASE...HEAD --stat`: no change to `PenGroupsViewModel`,
      `PenGroupViewModel`, `PenMembershipViewModel`, `PenGroupWriter`, `PenFormViewModel`,
      `ArchiveCallQueue` or `IPenCatalogueEditor`, and
      `git diff $BASE...HEAD -- SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml` shows no hunk inside
      the `GroupsTab` `TabItem` (25 files, none of the seven named; the last new-side hunk ends at
      line 489, `GroupsTab` opens at 499)

### Task 7: [Final] Update documentation

- [x] `docs/architecture/ui-theme.md:10-15`: the manifest names `<semi:DataGridSemiTheme/>`
      (`Semi.Avalonia.DataGrid` 12.0.0 over `Avalonia.Controls.DataGrid` 12.0.1)
- [x] `ui-theme.md` key table (`:33-82`): rows for the four DataGrid keys and any the probe added;
      `:108-116`: name `EveryDataGridSurface_PaintsItselfFromThePalette` and the two new opacity keys;
      `:133-147`: the two DataGrid radii and the extended corner test; the application-surfaces table
      (`:153-170`): `AppBorderBrush` gains the pen list's border, the pane's border and the colour dot's
      ring, `AppSecondaryForegroundBrush` the empty hint, the title's id, the mask preview and the groups
      cell, `AppSeverityErrorBrush` the warning glyph
- [x] `ui-theme.md:185-186`, "A form never resizes on validation": an invalid field shows the `invalid`
      border and a warning glyph inside the box (`InnerRightContent`, `WarningGeometry`,
      `AppSeverityErrorBrush`); the pen editor's form carries the glyph through its window-scoped
      style, and a form adopts it when it is next touched; until then a form marks by the border alone
- [x] rewrite `ui-theme.md:367-431`, "A resizable window keeps its fixed parts": the window size
      (Task 5's value); the list is the one part that takes the width and the height; the pane is 420 px,
      its 16 px padding, the 112 px label column, the 12 px gap, the field widths and the 22 px slack after the widest field (the 1 px border takes 2 of the skill's
      24); the
      list's 4 px border padding and 28 px rows; the five columns, their opening widths, their minimum as
      the realised header (`Tables/HeaderMeasuredColumns`), the fill column and the viewport rule; the
      cell insets 12 and 8, 12 on the fill column; the hidden currency rectangle; the form's fixed 588 px
      height and its scroller; the checkboxes in the field column; the tests that gate each part
- [x] `docs/architecture/ui-text.md:223-243`: five column headers; the form's labels, the renamed
      `PenForm*Label` keys and the eight new keys; Minimum and Maximum reuse the axis scale panel's keys;
      the "On start" section holds the visibility flag and the initial scale, whose messages keep their
      "Initial scale:" opening; the scale pair and the log flag have no column and no header key of their
      own
- [x] `docs/architecture/overview.md:525`, the `Pens` row: a read-only `DataGrid` of id, name with the
      colour dot, unit, the start flag as a check glyph and groups, sorted by a header click and reversed
      by the next, every column resizable from its header's width, groups taking the spare width; beside
      it the 420 px pane: the hint with no pen selected, otherwise the title and the three sections
      (Label: name, unit, mask, preview; Appearance: colour box and square swatch, line; On start:
      visibility, minimum, maximum, scale type) and the message line. Update `:532-533` (the visibility
      field reads "Show on the chart" under "On start") and `:618-621`: a press on the pen list's empty
      area ends the edit, as `ANameTypedThenThePenListsEmptyAreaClicked_WritesOnce` shows, through the
      grid taking the focus or, when Task 4 added it, through the handler's rows-presenter branch
- [x] run the key gate under Acceptance Evidence over `-- SemiPlot docs/architecture`: it prints
      nothing; no rewritten paragraph names a deleted or renamed key
- [x] move this plan to `docs/plans/completed/`
- ⚠️ the docs describe the selection route as built: `SelectedItem` binds one way and
  `OnPenListSelectionChanged` writes every selection change to `SelectedRow`, null included (review fix:
  a sort raises no selection change in 12.0.1, and a Ctrl+click deselect now shows the hint); the refresh refills the
  collection with no `DeferRefresh`; the grid takes the focus on any press inside it, so the window's
  tunnel handler has no rows-presenter branch; the window height stays 720
- ⚠️ `testing-strategy.md#what-a-realised-view-test-owns` named the deleted `_isReorderingRows`; it names
  `OnPenListSelectionChanged` instead. The table rows of `ui-theme.md` that read "while no pen is selected"
  read "while a refresh runs", because the form is hidden with no pen selected
- ⚠️ `PenEditorViewModel.SelectedRow`'s summary read "Two-way from the pen list", which the one-way
  binding contradicts; the review fix rewrote it

## Post-Completion

**Manual verification** in the running viewer (`Edit` -> `Pens and groups`), in both languages:

1. The window opens with the list's columns already at their final widths: no width jump on the first
   frame.
2. No header is cut and none ends in an ellipsis, sorted or not; the arrow sits right of the text.
3. A header click sorts ascending, the next descending; the selected pen and its form stay.
4. Dragging a column edge resizes it; the Groups column gives and takes the difference; widening the
   window widens only the Groups column.
5. A clicked row shows the row highlight and no cell outline.
6. With no pen selected the pane shows only "Выберите перо в списке слева".
7. Type `%0.0` into the mask and press Tab: the red border and the warning glyph inside the box, the
   message line filled, nothing moves.
8. The colour swatch is square and opens the picker; a picked colour writes once.
9. Every label fits its 112 px column; every checkbox's box lines up with the text boxes.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`: pen-editor-groups-pane
(the tabs go, the groups become the third pane with a row menu, the window opens 1440 px wide, the
delete button takes palette-key destructive brushes); pen-list-filter (the top bar's filter over id,
name and group); export-pen-choice-list (the export pens become checkboxes only); axis-scale-checkbox-column
(the log checkbox moves to the field column); ui-spacing-scale (every remaining inset onto the
4/8/12/16/24/32 scale, the `12,2` button padding with them).

**Executed by exec:**
- branch: pen-editor-pens-pane

## Verify it yourself

1. #111, inert boxes gone. Open `Edit` -> `Pens and groups`. At `hex-color-input` the pen table shows
   two checkbox columns ("При запуске", "Начальная шкала, логарифмическая") that ignore a click. On
   this branch the list has five columns (Номер, Имя, Ед. изм., При запуске, Группы); the start flag is
   a check glyph and the log flag is gone from the list. Proof: `PenListViewTests.ThePenList_RealisesNoCheckBox`.
2. Sort. Click each header: the list sorts ascending with an arrow, a second click descending; the
   selected pen and its form stay (`PenListViewTests.EachHeaderClick_OrdersTheListByItsColumn`,
   `AHeaderClick_SortsAscendingThenDescendingAndKeepsTheSelectedPenAndItsForm`).
3. Resize. Drag any column edge, the last one included: no column goes narrower than its header,
   wider ones scroll; Группы takes the spare width when the window widens
   (`EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader`, `HeaderMeasuredColumnsTests`).
4. Long names end in an ellipsis with the full text as a tooltip.
5. The pane. With no pen selected it shows only "Выберите перо в списке слева"; select a pen: a
   one-line title (id, name) and three sections Подпись / Вид / При запуске on one grid; Ctrl+click
   the selected row and the hint returns (`PenFormPaneViewTests`, `PenEditorViewTests`).
6. Validation. Clear the name: the box gets the red border plus a warning glyph inside, the window
   does not resize (`AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`).
7. The Groups tab is unchanged.

Run all of it: the Acceptance Evidence command of this plan (246 passed, 0 failed on `291ceec`).
