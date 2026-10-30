# One catalogue read and the sidebar group fold

## Overview

Two stages over the catalogue read path, closing the database groundwork of Semiteq/SemiPlot#140 (proposal
items 1 and 2) and Semiteq/SemiPlot#139:

- **Stage A — one catalogue read.** The chart, the sidebar and the startup sequence read the
  catalogue through one provider read that returns the stored `PenCatalogue` and the normalised
  `Pen` projection of the same rows. The periodic sync emits a read whenever the whole catalogue
  changed — stored pens and stored groups — so a change the normalisation hides (an unusable mask
  replaced by another unusable mask, a null colour replaced by the fallback colour) and a
  `semiplot_groups` row's own column are changes even when every normalised pen is equal. No
  behaviour change for the operator beyond the pen order noted under Decisions; the stage sets the
  seam Stage B builds on.
- **Stage B — the sidebar group fold (#139).** `semiplot_groups.folded_on_start` (shipped in
  SemiBase, Semiteq/SemiBase#18) reaches the sidebar through the Stage A read; each group header
  gains a fold toggle; the fold the operator sets lives for the session; the pen editor edits the
  stored flag.
- **Stage B2 — the sidebar's top row and the all-pens commands.** Added after the operator's stand
  test of Stage B. The panel button says what it does (`Скрыть значения` / `Показать значения`)
  and moves left; a fold-all button joins it on the right; both show text at full width and icons
  when narrow; the per-group chevron loses its accent fill; `Вид` gains the values check item,
  fold-all, `Показать все перья` and `Скрыть все перья`. Decisions 9-12.

The editor that follows reads (#140 item 3) moves to its own plan,
`docs/plans/20261030-editor-follows-reads.md`. The settings part of #140 is out of scope.

## Decisions (review of 2026-10-30)

The plan and Stage A were reviewed after Tasks 1-5. These decisions replace the earlier text they
contradict.

1. **The sync emits changed reads, not deltas.** `CatalogueDelta` is replaced by
   `PenCatalogueSync.Changes : IObservable<CatalogueRead>` and a pure
   `CatalogueRead.Matches(CatalogueRead other)` in Core. Production read only `IsEmpty`, `Current`
   and `AddedPens.Count`; `RevisedPens`, `AddedGroups`, `RevisedGroups` and `RemovedGroupIds` had
   tests as their only callers, and `RevisedPens` carried normalised `Pen` records where the editor
   needs `StoredPen`. Every consumer reconciles to the whole read: the chart to `Pens`
   (`ChartPenSet`), the sidebar to `Stored.Groups`, the applier derives "pens were added" from
   `PenListDelta.Between(_lastApplied.Pens, read.Pens)`. `PenListDelta` stays for `ChartPenSet`.
2. **The fold map is keyed by header name**, as #139 states, not by group id. The merged
   `Ungrouped` header (a real group named like `Resources.LegendUngroupedHeader` plus the ungrouped
   pens) is then one key with no ownership rule. A header name seen for the first time takes the
   `folded_on_start` of the stored group of that name, or unfolded when no stored group carries the
   name. The cost: a mid-session rename is a new name and takes the stored flag again.
3. **Stage C is a plan of its own.** #139 does not need it, it is the riskiest rework, and its
   inner API changes under decision 1: the editor reconciles to `CatalogueRead.Stored` by id rather
   than to delta arms, and refresh awaits `ReadNowAsync()` on the read loop, a task that completes
   with the first read started after the call, instead of subscribing to a completion signal
   ahead of `ReadNow()`.
4. **One branch.** Both stages land on `catalogue-read-and-group-fold`, by the operator's call;
   the per-stage branches of the earlier text are dropped.
5. **Icon resource names.** The chevron geometries are named `ChevronDownGeometry` and
   `ChevronUpGeometry`. `Semi.Avalonia` 12.0.3 ships no `SemiIcon*` keys, and a local key with that
   prefix would read as the library's.
6. **Pen order.** The pens projection sorts by `StringComparer.CurrentCulture` then id
   (`a22bfdd`); the deleted statement sorted by the database collation. Under a `C` collation the
   two orders differ for mixed-case and Cyrillic names. `data-integration.md` states the rule.
7. **The fold's one writer is `SetFolded`** (review of 2026-10-30, after Task 13). Where the Solution
   Overview and Task 9 say `IsFolded` is written only by its command, the shipped single writer is
   `TrendLegendGroupViewModel.SetFolded`, with two callers: `FoldCommand` and the unfold-on-pick rule.
   `FoldCommand` carries no `canExecute`; the headerless list draws no toggle to run it.
8. **The catalogue pair reads one snapshot.** `CatalogueRowReader.ReadAsync` runs `StoredPens` and
   `StoredGroups` in one `REPEATABLE READ` transaction for both the provider and the editor, so a write
   landing between the two statements never yields a read whose groups and pens disagree.

Stage B2 decisions (design review of 2026-10-30, after the operator's stand test):

9. **The panel button names the values, not a collapse.** `ToggleExpandedCommand` hides the value and
   unit columns and narrows the width slot; nothing collapses. Labels `Скрыть значения` /
   `Показать значения`, tooltips `Скрыть значения и сузить панель` /
   `Показать значения и расширить панель`. It moves to the left of the top row. Below the measured
   text width it shows a « / » icon with the same tooltip. `LegendCollapsePanel` and
   `LegendExpandPanel` are retired. This supersedes #139's "the Collapse button stays as it is".
10. **Fold-all is one plain `Button`** on the right of the top row, label naming the next action:
    `Свернуть все` / `Развернуть все`, tooltip and menu `Свернуть все группы` /
    `Развернуть все группы`. It folds every header unless every header is folded, then unfolds every
    header, the `SwitchGroupCommand` rule. `AreAllFolded` derives from the headers' `IsFolded`
    (zero headers handled); `canExecute` is "any group has a header"; the command calls `SetFolded`
    on each header, so `SetFolded` stays the one writer. Below the measured width it shows a double
    chevron icon. The switch between text and icon follows the panel's real width (an Avalonia
    container query, fallback a bool derived from `PanelWidth`), starting at 272 px; if the text
    clips on a real window the threshold moves to 290 px.
11. **Show and hide all pens live in the `Вид` menu only**, as two fixed items `Показать все перья` and
    `Скрыть все перья`, backed by one chart method `TrendChartViewModel.SetAllPensVisibility(bool)`:
    it sets every flag, keeps `ActivePenId` when that pen ends visible, then settles once
    (`ActivateAVisiblePen`, `SetVisiblePenCount`, one history request, one redraw). A loop over the
    rows would drag the active pen from pen to pen, lose the operator's pick on show-all and replan the
    history per pen. No panel control: it is rare, and one click empties a running display.
12. **The per-group chevron is a ghost.** A local style on `#GroupFoldToggle` sets a transparent
    background and border in both check states, `AppSecondaryForegroundBrush` for the glyph and
    `AppAccentBrush` on pointer-over; accent stays the active pen's colour. No new palette key. A
    view-test probe pins the presenter's background and the glyph's brush in Light and Dark. Group
    header captions trim with an ellipsis and carry the full name as a tooltip.

## Context (from discovery)

- Before Stage A two independent reads existed: `IDataProvider.QueryPensAsync` ran the
  `PenCatalog` statement, normalised in `PostgresDataProvider.ReadPen` with warn-once warnings
  pinned by `AnUnusableMaskAndLineStyle_WarnOncePerStoredValue`
  (`SemiPlot/SemiPlot.Tests.Integration/LiveCatalogueTests.cs`) and name-then-id ordering pinned by
  `SeededCatalogueReadsEveryPenOrderedByName`
  (`SemiPlot/SemiPlot.Tests.Integration/PostgresCatalogReadTests.cs`);
  `IPenCatalogueEditor.ReadAsync` ran `StoredPens` and `StoredGroups`, values as stored.
- After Stage A: `IDataProvider.QueryCatalogueAsync` returns `CatalogueRead(PenCatalogue Stored,
  IReadOnlyList<Pen> Pens)`; `CatalogueRowReader` runs the statement pair into `RawPenRow`s;
  `CatalogueNormaliser.Project` builds both projections. `PenCatalogueSync` snapshots the whole
  read; `PenCatalogueApplier` tracks the last read it applied to both chart and legend and rebases
  the sync on it after a failed apply (`SemiPlot/SemiPlot.UI/MainWindow/PenCatalogueApplier.cs`).
- The sidebar derives headers from the stored groups (`TrendLegendViewModel.BuildGroups`),
  ungrouped pens under the synthetic `Ungrouped` header, sorting last; a real group named like that
  header merges with them into one header, pinned in `TrendLegendViewModelTests`. The header
  template is `GroupHeaderRow` in `TrendLegendView.axaml`, gated by an allowlist test in
  `TrendLegendViewTests` that admits `Border`, `Grid`, `TextBlock`, `CheckBox` and finds exactly one
  `CheckBox`. The chart settles an active pen on its own (`SettleActivePen`,
  `TrendChartViewModel.cs`), separately from the explicit `SetActivePen` a pick goes through
  (`TrendLegendRowViewModel.cs`).
- The groups tab of the pen editor writes through `PenGroupWriter`
  (`SemiPlot/SemiPlot.UI/PenEditor/PenGroupWriter.cs`) with queued renames
  (`PenGroupViewModel.cs`). Every queued editor call ends in `_readCatalogueNow`, which triggers a
  sync `ReadNow()`.
- `Semi.Avalonia` 12.0.3 ships no `SemiIcon*` resources (the installed package was scanned).
- The bench seeder inserts `semiplot_groups (name)` only
  (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs`), so a `NOT NULL DEFAULT false`
  column changes nothing there.
- ASSUMPTION: the published `ghcr.io/semiteq/semibase:latest` image carries
  `semiplot_groups.folded_on_start` (Semiteq/SemiBase#18 is closed). Task 7's integration test is
  the probe.

## Development Approach

- **Testing approach**: regular — code and tests land inside each task, and each task passes its
  tests before the next starts.
- Both stages land on `catalogue-read-and-group-fold` (Decision 4).
- `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over touched files stay
  clean (pre-commit hook `.githooks/pre-commit`).

## Testing Strategy

- Unit tests in `SemiPlot.Tests.Unit` (no container): the read comparison, the legend fold and the
  editor field run headless (`[AvaloniaFact]` where a view is realised; every click through
  `HeadlessInput`). Fakes: `FakeDataProvider` and `FakePenCatalogueEditor`.
- Integration tests in `SemiPlot.Tests.Integration` (container required): the provider read, the
  `folded_on_start` roundtrip, and the live-catalogue journeys.
- Provider errors assert by error type and structured field, never on message wording.
- The scheduler rules of `AGENTS.md` hold: no `ImmediateScheduler` under `PenCatalogueSync` or
  anything built on `ArchiveReadLoop`; `TestScheduler` plus one tick after `Start()`.

## Acceptance Evidence

Stage A (automatable):

- `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~CatalogueReadTests"`:
  equal catalogues match, including the same pens in another order; a stored-only change the
  normalisation hides (an unusable mask replaced by a different unusable mask, a null colour replaced
  by the fallback colour) does not match; a group's own change on a memberless group does not match;
  a group rename and a membership move do not match; an added or removed pen or group does not match.
- The applier test: a group-only apply that throws is retried by the next identical read.
- Existing suites green over the new read:
  `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresCatalogReadTests"` (needs Docker),
  `--filter "FullyQualifiedName~LiveCatalogueTests"`,
  `--filter "FullyQualifiedName~CatalogueReadUnreachableTests"`.

Stage B (automatable plus one manual pass):

- `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegend"` —
  new tests: the toggle hides and shows rows through its command; the chart does not change; a
  fold survives `Rebuild()`; a `folded_on_start = true` group opens folded, including when its pen
  is the first pen the window settles on; a folded group holding the active pen shows the accent bar
  on its header; a pen picked on the chart unfolds every folded group holding it when none of its
  groups is unfolded, and leaves folded groups alone when one is; the window's own initial pen
  settlement unfolds nothing; the headerless list shows no toggle; the merged `Ungrouped` header
  folds as one; the allowlist admits exactly one `ToggleButton` beside the one `CheckBox`.
- `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PenCatalogueEditorTests"` —
  the `folded_on_start` write roundtrips through the container.
- Manual smoke over the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), done by the operator:
  1. a click on the toggle hides the group's rows, a second click shows them; the chart does not change
  2. a fold survives a pen edit saved in the editor
  3. a group set `folded_on_start` in the editor opens folded on a fresh viewer; clearing the flag opens it unfolded
  4. the toggle never sits under the scrollbar at the panel's narrowest width (`TrendLegendViewModel.PanelMinWidth`)

Stage B2 (automatable plus one manual pass):

- `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegend|FullyQualifiedName~AppMenuBarTests|FullyQualifiedName~TrendChartViewModel"` —
  new tests: fold-all folds every header while any is unfolded and unfolds every header when all are
  folded, cannot run on a headerless list, and its label follows `AreAllFolded`; the panel button
  reads `Скрыть значения` / `Показать значения` from resx; the top row shows text at 280 px and
  icons at 168 px and 120 px; the per-group chevron's presenter background is transparent and its
  glyph `AppSecondaryForegroundBrush` in both check states, Light and Dark; a header caption trims
  and carries the full name as a tooltip; `SetAllPensVisibility(false)` then `(true)` keeps the
  operator's `ActivePenId` and replans the history once per call; every new `Вид` leaf carries a
  command, and the values check item reads `IsExpanded` and writes nothing back (its own theory
  arm, not the minimap fallback).
- The row and header allowlists in `TrendLegendViewTests` stay unchanged; `ThemeTests` stays unchanged.
- Manual smoke over the stand, done by the operator:
  1. at the default width the top row reads `Скрыть значения` on the left and `Свернуть все` on the
     right with no clipping, at 100% and 125% display scaling
  2. `Свернуть все` folds every group and turns into `Развернуть все`; that unfolds them all
  3. `Скрыть значения` narrows the panel and both buttons turn into icons with full tooltips
  4. `Вид` -> `Скрыть все перья` empties the chart; `Показать все перья` brings every pen back with
     the previously picked pen still active
  5. folded headers show grey chevrons, and the active pen's 3 px bar is the only accent among them

Full suite at the end of each stage: `dotnet test SemiPlot.slnx`.

## Progress Tracking

- Mark completed items with `[x]` immediately when done; add discovered tasks with a `➕` prefix,
  blockers with a `⚠️` prefix.
- The plan moves to `docs/plans/completed/` after Task 18, in the delivery step.

## Solution Overview

- **One read.** `IDataProvider.QueryCatalogueAsync()` answers `CatalogueRead`. The provider issues
  `StoredPens` and `StoredGroups` on one connection, both under the 5 s catalogue bound, and maps the
  raw rows to both projections in `CatalogueNormaliser`. The pens sort by name then id; the stored
  list keeps id order.
- **Changed reads.** `CatalogueRead.Matches` compares the pens by id regardless of order
  (`PenListDelta.Between(...).IsEmpty`), the stored pens by id with record equality, and the stored
  groups by id field by field (name, membership sequence, and the fold flag from Task 7).
  `PenCatalogueSync` emits a read on `Changes` when it does not match the snapshot.
- **The applier.** `PenCatalogueApplier` hands the chart `read.Pens` and the sidebar
  `read.Stored.Groups`, derives "pens were added" against its last-applied read, advances that read
  only after both apply, and rebases the sync on it when an apply throws.
- **The fold.** `TrendLegendViewModel` owns a session fold map keyed by header name (Decision 2),
  seeded from the stored group of that name on first sight and surviving every `Rebuild()`.
  `TrendLegendGroupViewModel` gains `IsFolded`, written only by its command, and a derived
  accent-on-header flag for a folded group holding the active pen. Unfold-on-pick follows explicit
  `SetActivePen` picks only, not the replayed initial value and not `SettleActivePen`; when every
  group holding the picked pen is folded, all of them unfold.
- **The stored flag.** `StoredGroup` grows `FoldedOnStart`; `IPenCatalogueEditor` grows
  `SetFoldedOnStartAsync(StoredGroup, bool)` beside `RenameGroupAsync`; the groups tab edits it on
  the selected group.

## Technical Details

- `CatalogueRead` and its `Matches` live in `SemiPlot/SemiPlot.Core/Data/PenCatalogue.cs`.
- `StoredGroup` gets `FoldedOnStart` as a positional parameter with default `false`, so the
  editor's construction of a created group keeps compiling.
- The unfold-on-pick subscription lives in `TrendLegendViewModel`, on the chart's explicit pick
  path, disposed with the legend.
- The chevron geometries are `StreamGeometry` resources `ChevronDownGeometry` and
  `ChevronUpGeometry` in a new `SemiPlot.UI/Styles/Icons.axaml`, pulled into `App.axaml` through a
  `ResourceInclude`.
- The toggle reads `IsFolded` `Mode=OneWay` and writes only through its command.

## Implementation Steps

### Stage A — one catalogue read

### Task 1: `CatalogueRead` and the Postgres read behind it

- [x] add `CatalogueRead(PenCatalogue Stored, IReadOnlyList<Pen> Pens)` beside `PenCatalogue`; replace `QueryPensAsync` with `QueryCatalogueAsync(): Task<Result<CatalogueRead>>` on `IDataProvider`
- [x] implement the read: `StoredPens` and `StoredGroups` on one connection, both commands under the catalogue bound, rows kept in an internal raw shape carrying the raw `line_style` smallint
- [x] map through `CatalogueNormaliser`: `StoredPen` and the normalised `Pen` project from the raw row, warn-once included; each pen's `Groups` derived from `StoredGroups.MemberPenIds` in group-name order
- [x] sort the `Pens` projection by name then id; keep the stored list in id order; delete the `PenCatalog` statement and `CreateCatalogueCommand`, keeping `PenCatalogRelations` as the failure detail line
- [x] `PostgresPenCatalogueEditor.ReadAsync` reads through the same statement pair
- [x] switch `FakeDataProvider` and every integration consumer to the new signature
- [x] run `dotnet build SemiPlot.slnx` and the catalogue integration tests

### Task 2: the startup sequence on the one read

- [x] `StartupData` carries `CatalogueRead` in place of `IReadOnlyList<Pen>`
- [x] `StartupProbe.ReadAsync` reads `QueryCatalogueAsync`; `TrendWindow.Build` hands the chart the pens and the sync the whole read
- [x] update the startup tests that build `StartupData`
- [x] run the unit tests

### Task 3: the whole-catalogue delta (superseded by Task 6)

- [x] `CatalogueDelta.Between(CatalogueRead, CatalogueRead)` with pen and group arms
- [x] `PenListDelta` stays for `ChartPenSet.Apply`
- [x] `IsEmpty` is false when any arm is non-empty
- [x] unit tests in `CatalogueDeltaTests`
- [x] run the delta tests

### Task 4: the sync and the applier on the whole catalogue

- [x] `PenCatalogueSync` reads `QueryCatalogueAsync`, snapshots `CatalogueRead`; `Rebase` takes the whole applied read
- [x] `PenCatalogueApplier` tracks the last read it applied to both chart and legend and rebases the sync on it after a throw
- [x] `BuildGroups` derives headers from the stored groups, keeping the merged `Ungrouped` behaviour
- [x] tests over the sync, the applier retry and the stored-group headers
- [x] run both test projects

### Task 5: Stage A documentation

- [x] state the one read in `overview.md`, `data-integration.md` and `AGENTS.md`
- [x] verify the Stage A acceptance commands
- [x] `dotnet test SemiPlot.slnx` green

### ➕ Task 6: changed reads replace the delta (Decision 1)

**Files:**
- Delete: `SemiPlot/SemiPlot.Core/Data/CatalogueDelta.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/PenCatalogue.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/PenCatalogueApplier.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Unit/Core/Data/CatalogueDeltaTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Data/CatalogueReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/PenCatalogueSyncTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/PenCatalogueApplierTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/LiveCatalogueTests.cs`
- Modify: `docs/architecture/overview.md`, `docs/architecture/data-integration.md`, `docs/architecture/charting.md`, `docs/architecture/testing-strategy.md`, `AGENTS.md` (every mention of `CatalogueDelta` and the delta arms)

- [x] `CatalogueRead.Matches(CatalogueRead other)`: pens by id regardless of order through `PenListDelta.Between(...).IsEmpty`; stored pens by id with record equality and the same id set; stored groups by id with the same id set, equal name and equal `MemberPenIds` sequence
- [x] `PenCatalogueSync.Changes : IObservable<CatalogueRead>` replaces `Deltas`; a read that matches the snapshot emits nothing; `Rebase` stays
- [x] `PenCatalogueApplier` subscribes to `Changes`, derives "pens were added" from `PenListDelta.Between(_lastApplied.Pens, read.Pens).Added`, and keeps its apply order, last-applied tracking and rebase
- [x] delete `CatalogueDelta` and `CatalogueDeltaTests`; `CatalogueReadTests` covers every Stage A acceptance case; the sync, applier and live-catalogue tests assert on the emitted read instead of the arms, with no behaviour assertion dropped
- [x] the documents name `Changes` and `Matches`; `git grep -n "CatalogueDelta\|RevisedPens\|RevisedGroups" -- . ':!docs/plans'` prints nothing
- [x] run both test projects — must pass before Task 7

### Stage B — the sidebar group fold (#139)

### Task 7: `folded_on_start` through the read

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/PenCatalogue.cs` (`StoredGroup`, `Matches`)
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs` (`StoredGroups`, `StoredGroupColumn`)
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/CatalogueRowReader.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Data/CatalogueReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresCatalogReadTests.cs`

- [x] `StoredGroup(Id, Name, MemberPenIds, FoldedOnStart = false)`
- [x] `StoredGroups` selects `grp.folded_on_start`; the reader maps it; `Matches` compares it, with a unit test that a fold-flag change alone does not match
- [x] integration test reading the flag back false and true — the probe of the ASSUMPTION; if the column is missing, stop with a `⚠️` blocker naming the image
- [x] run the catalogue integration tests — must pass before Task 8

### Task 8: the fold write path

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IPenCatalogueEditor.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresPenCatalogueEditor.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Data/PenCatalogueEditorSurfaceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/FakePenCatalogueEditor.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PenCatalogueEditorTests.cs`

- [x] `SetFoldedOnStartAsync(StoredGroup, bool)` on the interface; `UPDATE semiplot_groups SET folded_on_start = @value WHERE id = @id` through `UpdateOneRowAsync`, the `RenameGroupAsync` pattern
- [x] surface test over the interface; the fake grows the write; integration roundtrip through the container
- [x] run the surface and editor tests — must pass before Task 9

### Task 9: the sidebar fold state and the group view model

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendGroupViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs` (expose the explicit pick path)
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendFoldTests.cs`

- [x] session fold map in `TrendLegendViewModel` keyed by header name: a name seen first takes the `FoldedOnStart` of the stored group of that name, or unfolded when none; the merged `Ungrouped` header is one key; the map survives every `Rebuild()`
- [x] `TrendLegendGroupViewModel`: `IsFolded` with `FoldCommand` as its single writer; a derived flag for the 3 px accent bar on a folded header holding the active pen
- [x] unfold-on-pick on the chart's explicit `SetActivePen` path only: the replayed initial value and `SettleActivePen` unfold nothing; a pick whose pen has no row under any unfolded header unfolds every folded group holding it; a pen with a row under an unfolded header leaves folded groups alone
- [x] unit tests: seeding; survival across `Rebuild()`; a window whose first pen sits only in a `folded_on_start` group opens folded; unfold-on-pick in all three cases; the merged `Ungrouped` header folding as one and seeding from the real group's flag; the accent flag; a memberless stored group and an all-ungrouped catalogue produce no header and no toggle
- [x] run the legend tests — must pass before Task 10

### Task 10: the header toggle in the view

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Create: `SemiPlot/SemiPlot.UI/Styles/Icons.axaml`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`

- [x] `Icons.axaml` with `ChevronDownGeometry` / `ChevronUpGeometry`, included from `App.axaml`
- [x] `GroupHeaderRow` gains a right column: a `ToggleButton` reading `IsFolded` `Mode=OneWay`, writing through `FoldCommand`, chevron down while folded and up while unfolded, tooltip and automation name from new `LegendFoldGroup` / `LegendUnfoldGroup` keys and their `ru` pair; colours from `Palette.axaml` keys only
- [x] the header's right margin clears the `ScrollViewer` scrollbar; the headerless list draws no toggle
- [x] the allowlist test admits `ToggleButton` in the header and still finds exactly one `CheckBox`; view tests: a click hides and shows the rows through the command, the chart does not change, a folded header holding the active pen shows the bar
- [x] run `TrendLegendViewTests` — must pass before Task 11

### Task 11: the editor's fold field

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupWriter.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`

- [x] the selected group's panel gains a check box: reads the stored flag `Mode=OneWay`, writes through a command routed by `PenGroupWriter` to `SetFoldedOnStartAsync`; a successful write updates the group's record; a refused write shows on the tab's message line
- [x] label from a new `PenGroupsFoldedOnStartLabel` key and its `ru` pair
- [x] VM and view tests: the flag writes on command only, the label key resolves, the box follows the single-writer rule
- [x] run the pen editor tests — must pass before Task 12

### Task 12: Stage B documentation

**Files:**
- Modify: `docs/architecture/overview.md` (The live catalogue; The pen and group editor)
- Modify: `docs/architecture/data-integration.md` (The pen catalogue editor; The catalogue while the viewer runs)
- Modify: `docs/architecture/ui-text.md` (the new keys)
- Modify: `AGENTS.md` (the sidebar rules: the fold toggle and the row allowlist note)

- [x] document the fold: the session rule keyed by header name, the stored default, the unfold-on-pick rule, the editor field
- [x] run the Stage B automatable acceptance commands
- [x] `dotnet test SemiPlot.slnx` green

### Task 13: close out

- [x] verify every Stage A and Stage B acceptance command once more, fresh (unit: `CatalogueReadTests` 11/11, `PenCatalogueApplierTests` 13/13, `TrendLegend` 69/69; integration: `PostgresCatalogReadTests` 13/13, `LiveCatalogueTests` 5/5, `CatalogueReadUnreachableTests` 1/1, `PenCatalogueEditorTests` 34/34)
- [x] `dotnet test SemiPlot.slnx` green end to end (unit 2561/2561, integration 214/214)
- [x] move this plan to `docs/plans/completed/` (moved to Task 18; the delivery step moves it)

### Stage B2 — the sidebar's top row and the all-pens commands

### Task 14: show and hide every pen at once (Decision 11)

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml` (locate the `Вид` menu file by grep first)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: the chart view-model tests and `AppMenuBarTests`

- [x] `TrendChartViewModel.SetAllPensVisibility(bool)`: sets every pen's flag, keeps `ActivePenId` when that pen ends visible, then settles once (`ActivateAVisiblePen`, `SetVisiblePenCount`, one history request, one redraw); `ShowAllPensCommand` and `HideAllPensCommand` call it
- [x] `Вид` gains, after a separator below the legend items, `Показать все перья` and `Скрыть все перья` (`MenuViewShowAllPens`, `MenuViewHideAllPens` and their English pair), bound to `ChartViewModel`'s commands
- [x] tests: hide-all then show-all keeps the picked `ActivePenId`; each call replans the history once; hide-all leaves no visible pen and no drawn axis; every new leaf carries a command
- [x] run the chart and menu tests — must pass before Task 15 (`TrendChartViewModel|AppMenuBarTests` 169/169, unit 2574/2574)

### Task 15: fold-all and the values toggle in the view model (Decisions 9, 10)

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendFoldTests.cs`, `TrendLegendViewModelTests.cs`, `AppMenuBarTests`

- [x] `AreAllFolded` derived from the headers' `IsFolded` across every `Rebuild()`, zero headers handled; `ToggleAllFoldsCommand` with `canExecute` "any group has a header", calling `SetFolded` on each header; `FoldAllText` and `FoldAllGroupsText` (tooltip and menu header, one value per Decision 10) follow `AreAllFolded`
- [x] the panel button's text and tooltip move to the new keys `LegendHideValues`, `LegendShowValues`, `LegendHideValuesTip`, `LegendShowValuesTip`; `LegendCollapsePanel` and `LegendExpandPanel` are deleted; fold-all keys `LegendFoldAll`, `LegendUnfoldAll`, `LegendFoldAllGroups`, `LegendUnfoldAllGroups`; English pairs for every key
- [x] `Вид` gains `Значения в панели` (`MenuViewLegendValues`, checkable, `LegendViewModel.IsExpanded` OneWay, `ToggleExpandedCommand`) and the fold-all item (header from `FoldAllGroupsText`, `ToggleAllFoldsCommand`), after `Условные обозначения` and a separator
- [x] tests: the fold-all rule in both directions, its label, its `canExecute` on a headerless list, fold-all then a pick still unfolds; the values item's own `ToggleItem` theory arm
- [x] run the legend and menu tests — must pass before Task 16 (`TrendLegend|AppMenuBarTests|TrendChartViewModel|MainWindowViewTests` 265/265, unit 2584/2584)

### Task 16: the top row, the ghost chevron and the trimmed caption (Decisions 9, 10, 12)

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Icons.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`

- [x] `Icons.axaml` gains `ChevronDoubleUpGeometry`, `ChevronDoubleDownGeometry`, `ChevronDoubleLeftGeometry`, `ChevronDoubleRightGeometry` as explicit paths
- [x] the top row: `PanelStateToggle` on the left, `FoldAllButton` on the right, both plain `Button`s; text at 272 px and wider, a 12 px `PathIcon` in a 24x20 button below, switched on the panel's real width; tooltips always carry the full phrase; a fixed row height so nothing moves when the width changes; an `AppSubtleLineBrush` separator under the row (a `ContainerQuery` on the view's root `DockPanel`, `min-width:263`: the view is `LegendPanel`'s width less its 4 px padding each side and 1 px border)
- [x] the ghost style on `#GroupFoldToggle` (Decision 12); `GroupHeader` gets `TextTrimming="CharacterEllipsis"` and a tooltip with the full name (Semi paints `PART_ContentPresenter` from triggered theme setters, so the ghost targets the template part in each of rest, `:checked`, `:pointerover` and `:pressed`)
- [x] view tests: the row shows text at 280 px and icons at 168 and 120 px; a click on fold-all folds and unfolds through its command; the chevron palette probe in Light and Dark, folded and unfolded; the caption trims with its tooltip; both allowlists and `ThemeTests` unchanged and green (new `TrendLegendTopRowViewTests`, 14 cases, including the 263/262 boundary and a live resize)
- [x] run the view tests and the full unit suite — must pass before Task 17 (`TrendLegend|ThemeTests` 116/116, unit 2598/2598)

### Task 17: Stage B2 documentation

**Files:**
- Modify: `docs/architecture/overview.md` (`#the-sidebar-fold`: fold-all, the ghost chevron, the renamed panel button)
- Modify: `docs/architecture/ui-text.md` (the new and retired keys)
- Modify: `docs/architecture/ui-theme.md` (the chevron among the secondary-foreground and accent consumers)
- Modify: `docs/architecture/charting.md` (the batch visibility method), `readme.md` (operator feature list), `AGENTS.md` only if a stated rule changes

- [x] document fold-all, the values button, the menu items and the batch visibility rule; replace the "accent-filled toggle" sentence
- [x] run the Stage B2 automatable acceptance commands (`TrendLegend|AppMenuBarTests|TrendChartViewModel` 267/267)
- [x] `dotnet test SemiPlot.slnx` green (unit 2598/2598, integration 214/214)

### Task 18: close out

- [x] verify every Stage A, B and B2 automatable acceptance command once more, fresh (unit: `CatalogueReadTests` 11/11, `PenCatalogueApplierTests` 13/13, `TrendLegend` 95/95, `TrendLegend|AppMenuBarTests|TrendChartViewModel` 267/267; integration: `PostgresCatalogReadTests` 13/13, `LiveCatalogueTests` 5/5, `CatalogueReadUnreachableTests` 1/1, `PenCatalogueEditorTests` 34/34)
- [x] `dotnet test SemiPlot.slnx` green end to end (unit 2598/2598, integration 214/214)
- [x] move this plan to `docs/plans/completed/` (left to the delivery step)

## Post-Completion

**Manual verification (operator):**
- [ ] The Stage B smoke checklist over the AppHost stand (four steps in Acceptance Evidence).
- [ ] Two viewers side by side over the stand: a fold set in one stays local to it.
- [ ] The Stage B2 smoke checklist over the AppHost stand (five steps in Acceptance Evidence).

**External system updates:**
- Semiteq/SemiBase: if Task 7's probe fails, the published `semibase` image lags SemiBase#18 and
  the image must move, not this repository.
- #139 closes on the merge. #140 stays open for the editor follow (its own plan) and the settings part.
- The PR push and creation are the operator's.

**Executed by exec:**
- branch: catalogue-read-and-group-fold

## Verify it yourself

Automated, from the repository root (Docker running for the integration project):

```powershell
dotnet test SemiPlot.slnx   # unit 2603, integration 214 after the stage B2 review fixes
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegendFoldTests"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresCatalogReadTests|FullyQualifiedName~PenCatalogueEditorTests"
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendLegend|FullyQualifiedName~AppMenuBarTests|FullyQualifiedName~TrendChartViewModel|FullyQualifiedName~MainWindowViewTests"   # Stage B2, 284
```

On the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. Click the chevron on a group header. Its rows hide, the chevron turns down, and the chart, pen
   visibility and active pen stay as they were. Click again; the rows show. Before this branch the
   header carried no toggle.
2. Fold a group, open `Edit` -> `Pens and groups`, change any pen field and close the editor. The
   group stays folded after the next read (at most 5 s).
3. In the editor's groups tab, tick "Folded when the viewer opens" on a group. The running sidebar
   does not fold it. Start a second viewer (`File` -> `New window`): that group opens folded.
   Clear the box; the next new window opens it unfolded.
4. Fold the group holding the active pen; its header shows the 3 px accent bar. Click a trace of a
   pen that sits only in a folded group: the group unfolds. Click one that also sits in an
   unfolded group: the folded group stays folded.
5. Drag the sidebar to its narrowest width with enough pens to show the scrollbar: the toggle stays
   clear of it. The 16 px margin was measured headless only (`TheFoldToggle_ClearsTheScrollbarAtThePanelsNarrowestWidth`).

Stage B2 on the same stand:

6. At the default width the top row reads `Скрыть значения` on the left and `Свернуть все` on the
   right with no clipping, at 100% and 125% display scaling.
7. `Свернуть все` folds every group and turns into `Развернуть все`; that unfolds them all.
8. `Скрыть значения` narrows the panel, and both buttons turn into icons with full tooltips.
9. Pick a pen, then `Вид` -> `Скрыть все перья`: the chart empties. `Показать все перья` brings every
   pen back with the picked pen still active.
10. Folded headers show grey chevrons, and the active pen's 3 px bar is the only accent among them.

Stage A (one read, changed reads) changes nothing on screen beyond the pen order note under
Decision 6; `CatalogueReadTests` and `PenCatalogueApplierTests` demonstrate it.
