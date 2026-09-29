# Pen and group editor

## Overview

The viewer reads a full pen catalogue (unit, mask, colour, line style, start visibility, stored scale,
several groups per pen) and nothing can change it but `psql`. New SCADA variables never become pens on
their own. `Semiteq/SemiPlot#67` adds both halves:

- a pen and group editor window, opened from `Edit` -> `Pens and groups`, with two tabs. The `Pens` tab is
  a read-only, sortable table of every pen and an edit form for the selected one. The `Groups` tab lists
  the groups with create, rename and delete, and edits the membership of the selected group;
- pen registration from the editor's refresh button: the viewer asks SemiBase's
  `semiplot_register_new_pens()` to add a hidden, default pen for every key SCADA writes that has none.

Constraints:

- SemiPlot edits the settings of pens that exist. It never adds a pen by hand, deletes one, or changes an
  `id`: the key is the SCADA variable number and belongs to SCADA. The database enforces this: the
  `semiplot` role holds a column-level `UPDATE` on the eight settings columns and no `INSERT` or `DELETE`
  on `semiplot_tags`.
- Every write the viewer issues follows an operator action. No startup code calls `IPenCatalogueEditor`.
  The container constructs it with `MainWindowViewModel`, which `App.InitializeServices` resolves
  (`App.axaml.cs:181`, reached from `.AfterSetup` through `:104` and `:133`), and its constructor issues no
  statement. `RegisterNewPensAsync` has one caller, `PenEditorViewModel`.
- Live catalogue. The editor writes the tables; every running instance re-reads the catalogue every 5 s
  and applies what changed to its chart and sidebar, and the instance whose editor writes reads again right
  after each write.
  No restart is needed for anything the editor writes. The settings window keeps its restart model.
- One machine, parallel viewer instances possible, last write wins per column.
- A field writes when its edit ends. There is no save button. A value the form refuses, or one the write
  fails on, reverts and says why, as Revert, mark, say why states.
- Membership is edited from the group side only: the `Groups` tab shows the selected group's pens as
  checkboxes. A pen's groups appear in the `Pens` table as text and are not editable there.
- Deleting a group asks once, naming how many pens it holds. `ON DELETE CASCADE` on
  `semiplot_pen_groups.group_id` then removes the memberships; a pen left without a group falls under the
  Ungrouped header at the next catalogue read.

## Context (from discovery)

Prerequisites, all met on `master` at `44811da`:

- SemiBase `v0.4.0` (`5498b49`, plan `docs/plans/completed/20260916-semiplot-role-and-pen-schema.md`
  Tasks 8 and 9 in the SemiBase repository) ships the column-level grant `SELECT, UPDATE (name, unit,
  format, color, line_style, enabled_on_start, scale_min, scale_max) ON semiplot_tags`, full DML on
  `semiplot_groups` and `semiplot_pen_groups`, and `semiplot_register_new_pens()` (`sql/semiplot_register.sql`:
  `SECURITY DEFINER`, `EXECUTE` for `semiplot`, none for `PUBLIC`; a new pen is named by its number, takes
  one of twelve colours by `id % 12`, starts with `enabled_on_start = false`, no unit, no mask, no scale).
- The constraints the writes meet: `semiplot_tags_scale_paired` (both bounds or neither, `scale_min <
  scale_max`), `semiplot_tags_color_hex` (`NULL` or `^#[0-9A-Fa-f]{6}$`), `semiplot_groups.name NOT NULL
  UNIQUE`, `PRIMARY KEY (pen_id, group_id)` on `semiplot_pen_groups`, and both of its foreign keys
  `ON DELETE CASCADE` (SemiBase `sql/semiplot_tags.sql`, `sql/semiplot_groups.sql`).
- `docs/plans/completed/20260917-pen-catalogue-and-groups.md` (the catalogue read),
  `docs/plans/completed/20260916-settings-window.md` (the settings window, the first form),
  `docs/plans/completed/20260924-sidebar-group-switch-and-splitter.md` and
  `docs/plans/completed/20260925-drop-source-time-zone.md` have landed.
- The container image carries v0.4.0 or later. `SemiPlot/bench/Dockerfile:3` names
  `ghcr.io/semiteq/semibase:latest` as the provisioner. `ArchiveStatements.PenCatalog`
  (`ArchiveStatements.cs:50-60`) selects `format`, `enabled_on_start` and `scale_min` and joins
  `semiplot_groups`; SemiBase's first commit carrying any of them is `5498b49` (`v0.4.0`), and
  `PostgresCatalogReadTests` runs that statement against the pulled image in the `linux` CI job, green
  for `44811da` (run `36148794568`).

The registration function, as SemiBase measures and bounds it:

- `semiplot_register_new_pens()` finds the keys by a loose index scan whose cost grows with keys times
  partitions (SemiBase `docs/architecture/provisioning.md:294-314`): 119.0 ms for 50 keys over 91
  partitions (`:303`), and a linear extrapolation, not a measurement, of 2.6 s to 4.8 s for 500 keys over
  365 partitions (`:309-314`). The `semiplot` role runs under `statement_timeout = '30s'` and
  `idle_in_transaction_session_timeout = '60s'` (SemiBase `internal/provision/create.go:78-79`).
- Its recursive CTE yields the keys in ascending order, and the insert is `ON CONFLICT (id) DO NOTHING`.
- `provisioning.md:256-257` says the viewer calls it at start and from the refresh button, and `:312`
  counts the cost "at every viewer start and refresh"; Post-Completion carries both lines.

Files and components involved:

- `SemiPlot.Core/Data/IDataProvider.cs:7-27` - `Subscribe`, `ConnectionFaults` and three `Query*` members;
  no write. The two greps of acceptance item 13 return 0 on `44811da`; the write path lives in a separate
  interface so both keep holding.
- `SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:15-35` - `AddPostgresData`
  registers the data `IScheduler`, the settings, one `NpgsqlDataSource` singleton, `ArchiveTimeConverter`,
  `ArchiveExceptionMapper` and `IDataProvider` (`:26-32`, a factory because the constructor is internal).
  The editor's implementation shares that data source: one role, one connection string.
- `SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:85-106` - `QueryPensAsync`, the catalogue read over
  `ArchiveStatements.PenCatalog` (`ArchiveStatements.cs:50`), failure detail `PenCatalogRelations`
  (`:40`). `ReadPen` (`:438-454`) normalises what it reads: a `NULL` colour becomes `#808080`
  (`:469-482`), a mask `PenValueFormat.IsAcceptable` refuses becomes `null` (`:486-507`), and groups are
  names. The editor cannot show values through that normalisation, so it reads its own shape.
  `ReadLineStyle` (`:509-524`) is a private instance method that logs through `_logger`. `Map`
  (`:538-549`) logs an empty-detail `ReadFailed` as a fault in this code.
- `SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:50-65` - maps what a read throws. Its default arm
  (`:63`) turns every unlisted SQLSTATE into `ArchiveFault.ReadFailed`; 42883 is unlisted. `Map`'s
  `relation` parameter is read on 42P01 only (`:22`, `:55`).
- `SemiPlot.Core/Data/Errors/ArchiveFault.cs:7-32` - eight kinds, one remedy each; `ArchiveError.cs:10`
  carries kind, host, port, database and detail, and its `Describe` switch (`:27-42`) writes the English
  log line per kind. `:36` and `:39-41` name a read.
- `SemiPlot.UI/Messages/ArchiveFailureMapper.cs:16`, archive arms at `:126-175`, `MapReadFailed` at
  `:179-195` - the per-kind switch that assigns title, detail, remedy and severity. CLAUDE.md requires the
  severity to be decided there, never at a call site. `:147-148` states why `TableMissing`'s remedy never
  depends on which relation is absent.
- `SemiPlot.UI/Localization/Resources.resx` - the reused kinds read as read-only operations:
  `FailureArchiveAccessDenied*` (`:186-194`), `FailureArchiveTableMissing*` (`:204-209`),
  `FailureArchiveQueryTimedOut*` (`:228-236`), `FailureArchiveReadFailed*` and `FailureArchiveReadUnnamed*`
  (`:237-251`). `ArchiveFailureMapperTests.cs:402` asserts the timed-out detail contains `57014`.
  `EmptyCatalogueMessage` (`:123-125`) says the catalogue "is filled when the tool is commissioned".
  `ui-text.md#what-the-mappers-two-consumers-read` already forbids a title that names a phase.
- `SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs` - `_archiveFaults` (`:21-32`) and
  `ArchiveFaults_SplitIntoWhatRetriesAndWhatNeedsTheOperator` (`:215-238`), two buckets that must cover
  the enum. `SemiPlot.Tests.Unit/Errors/DataErrorTests.cs:38-56` - one `InlineData` row per kind.
- `SemiPlot.UI/Chart/TrendChartView.axaml:16-17` shows `EmptyCatalogueMessage` through `{x:Static}`;
  `TrendChartViewTests.cs:164` asserts `message.Text == Resources.EmptyCatalogueMessage`, which passes after
  any rewording. `ui-text.md:59-69` states that a formatted label in AXAML keeps `StringFormat` rather than
  growing a view-model property.
- `SemiPlot.UI/MainWindow/MainWindowViewModel.cs` - the settings window's request path, which this plan
  copies: a private `Subject<SettingsViewModel>` (`:26`), `ShowSettingsCommand` built with
  `CreateFromTask` and the availability `Observable.Return(configDirectory is not null)` (`:58-60`), the
  `SettingsRequests` observable (`:74-75`), and `RequestSettingsAsync` (`:190-201`), which reads first and
  then emits a view model the listener owns.
- `SemiPlot.UI/MainWindow/MainWindow.axaml.cs:35` - subscribes `SettingsRequests` in `OnLoaded`;
  `ShowSettings` (`:82-96`) opens `SettingsDialog` with `ShowDialog(this)` from a guarded `async void`
  handler and disposes the view model in `finally`.
- `SemiPlot.UI/App.axaml.cs:61-94` - `CreateMainWindow`. The startup-failure path (`:63-83`) builds its
  own `MainWindowViewModel` with no container (`:73`); the container path (`:91`) resolves the one
  `UiServiceCollectionExtensions.cs:18-22` registers.
- `SemiPlot.UI/MainWindow/AppMenuBar.axaml:22-27` - `EditMenu` holds `EditSettings`.
  `AppMenuBarTests.EveryMenuLeaf_CarriesACommand` (`:29-51`) requires every leaf to carry a command, and
  `EditMenu_HoldsTheSettingsItemBoundToItsCommand` (`:53-65`) asserts with `ContainSingle` that `EditMenu`
  holds one item. `MainWindowViewTests.TheStartupFailureWindowWithNoDirectory_CarriesTheSettingsItemDisabled`
  (`:65-78`) is the exists-but-cannot-execute gate this plan copies; `:116-119` proves a disposed view
  model by a command that stops following its inputs.
- `SemiPlot.UI/Settings/SettingsDialog.axaml` - the first form: `Width="528"`, `SizeToContent="Height"`,
  fields binding `Classes.invalid` to `Is*Valid`, one `TextBlock.form-message` line (`:159-173`).
  `SettingsViewModel.cs:193` and `:239-267` - `ValidationMessage` is the first broken rule in form order.
- `SemiPlot.UI/Styles/Forms.axaml:5-11` styles the `invalid` border on `TextBox` and `NumericUpDown` only;
  `:13-24` is the two-line `form-message`. `docs/architecture/ui-theme.md#a-form-never-resizes-on-validation`
  (`:128-159`) states the norm for a fixed-width, `SizeToContent="Height"` window whose notice shares the
  message line.
- `SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs:228-249` - the size gate
  (`TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`); `:297-304` realises a window; its input
  helpers and their two copies are what Task 6 extracts.
- `SemiPlot.Core/Trends/PenValueFormat.cs:25` - `IsAcceptable` is the mask rule; `:61` - `Format` is the
  only renderer of a reading (CLAUDE.md), and the mask preview goes through it.
- `SemiPlot.Core/Trends/PenLineStyle.cs` - `Interpolated = 0`, `Stepped = 1`, the wire values.
- `SemiPlot.UI/SemiPlot.UI.csproj:19-37` - the package references; neither `Avalonia.Controls.DataGrid`
  nor any colour picker is referenced, and the table is built without a grid package.
- `UI/Startup/StartupProbeTests.cs:161-168` and `:221-232` build an `AddUi` container and never resolve
  `MainWindowViewModel`, so they need no fake editor; `UI/Di/CompositionRootTests.cs:78-81` builds the
  production container through `StartupProbe.BuildArchiveServiceProvider`.
- Integration harness: `ClonedArchiveTest(fixture, CloneSource.Provisioned)` (`ClonedArchiveTest.cs`)
  hands each test a clone of `semiplot_provisioned` and a `SeedAsync` hook; `ArchiveDatabase` exposes
  `AdminConnectionString` and `PlotConnectionString` (`ArchiveDatabase.cs:11`, `:15`);
  `ArchiveProviderFactory.Build(connectionString)` returns a `ServiceProvider` over `AddPostgresData`;
  `TagCatalogWriter` (`SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs:8`) writes pens and memberships
  through the admin connection.

## Development Approach

- Testing approach: regular - code first, then tests, within the same task.
- Complete each task fully before the next; every task ends with `dotnet test SemiPlot.slnx` green.
- Every task that changes code adds or updates tests covering the success and the failure path.
- Update this plan when the scope changes during implementation.

## Testing Strategy

- Integration tests for everything that touches the database: registration, every write, every refusal.
  They live in `SemiPlot.Tests.Integration` and derive from
  `ClonedArchiveTest(fixture, CloneSource.Provisioned)`, because they write their own rows
  (`docs/architecture/bench.md`). `SeedAsync` writes the pens and groups through
  `new TagCatalogWriter(Database.AdminConnectionString)`, and the editor under test is resolved from
  `ArchiveProviderFactory.Build(Database.PlotConnectionString)`, so every write runs as `semiplot` and
  proves the grant.
- Unit tests for the editor view models run over `FakePenCatalogueEditor`.
- Headless view tests with `[AvaloniaFact]` over the realised window for everything bound: a view-model
  assertion proves nothing about what the operator sees. Every click, clear, type and key goes through
  `SemiPlot.Tests.Unit/UI/HeadlessInput.cs`, and an edit ends the way the operator ends it, with Tab, Enter
  or a click elsewhere. The editor window realises no chart, so no scheduler rule applies
  (`docs/architecture/testing-strategy.md#the-ui-scheduler-in-a-realised-view`).
- The queue and the editor view models await without `ConfigureAwait(false)`, so every continuation returns
  through the dispatcher. A gate the fake holds is a `TaskCompletionSource` that production code awaits and
  the test completes, created without `TaskCreationOptions.RunContinuationsAsynchronously` (CLAUDE.md,
  Test); completing it resumes the held call inline, and the calls queued behind it run as dispatcher jobs.
  A gated test therefore asserts only after `await WhenIdleAsync()` or `Dispatcher.UIThread.RunJobs()`.
- `PenFormViewModelTests`, `PenEditorViewTests` and `PenGroupsViewTests` join
  `[Collection(ProcessGlobalStateCollection.Name)]`: the first sets `CultureInfo.CurrentCulture`, the view
  tests read the resource set the UI culture selects.
  A test that sets the culture restores it in `finally`, as `PenValueFormatTests.cs:92-106` does.
- Tests assert by error kind and structured field, never on message wording.
- Traits, one `Area` per class. The `Area` set (CLAUDE.md) has no editor member; the editor's UI classes
  take `Chart`, as `SettingsViewTests` and `MainWindowViewTests` do.

  | Class | Component | Area | Category |
  | --- | --- | --- | --- |
  | `PenCatalogueEditorTests`, `PenRegistrationTests` | `Core` | `Data` | `Integration` |
  | `PenCatalogueEditorSurfaceTests` | `Core` | `Data` | `Unit` |
  | `ArchiveExceptionMapperTests`, `DataErrorTests`, `PostgresCompositionTests` (existing) | `Core` | `Data` | `Unit` |
  | `ArchiveFailureMapperTests`, `FailureSeverityTests` (existing) | `UI` | `Messages` | `Unit` |
  | `CompositionRootTests` (existing) | `UI` | `Di` | `Unit` |
  | `PenFormViewModelTests`, `PenEditorViewModelTests`, `PenGroupsViewModelTests`, `PenEditorViewTests`, `PenGroupsViewTests` | `UI` | `Chart` | `Unit` |
  | `PenListDeltaTests` | `Core` | `Data` | `Unit` |
  | `PenCatalogueSyncTests`, `TrendCoordinatorTests` (existing) | `UI` | `Bridge` | `Unit` |
  | `LiveCatalogueTests` | `Core` | `Data` | `Integration` |
  | `TrendChartCatalogueTests` (split out of `TrendChartViewModelTests` in review) | `UI` | `Chart` | `Unit` |

- A `--filter` that matches nothing exits 0; every acceptance item states its minimum passed count.

## Acceptance Evidence

Unit filters run on `SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` and integration filters on
`SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`, as `dotnet test <project>
--filter "<filter>"`. Each reports zero failed (`Failed: 0`, or `не пройдено 0` under a Russian SDK
locale) and at least the passed count the item names; Tasks 10 and 18 record the actual count.

1. **A key SCADA writes becomes a hidden default pen, and a second refresh waits for the first.**
   Integration, `FullyQualifiedName~PenRegistrationTests`, at least 4 passed:
   1. a `trends` row for an id with no `semiplot_tags` row gains one named by its number,
      `enabled_on_start = false`, one of the twelve colours, no unit, no mask, no scale;
   2. a second call adds nothing and returns 0;
   3. over N unregistered keys, a separate connection on `Database.PlotConnectionString` runs `BEGIN;
      SELECT semiplot_register_new_pens();` and holds the transaction open; `RegisterNewPensAsync` started
      next has not completed after 500 ms; after `COMMIT` it returns 0 with no fault, the held call
      returned N, and `semiplot_tags` holds N rows for those keys. The hold stays far inside the role's
      60 s `idle_in_transaction_session_timeout`;
   4. after `DROP FUNCTION semiplot_register_new_pens()` through the admin connection the call fails with
      `ArchiveFault.TableMissing` and `Detail == "semiplot_register_new_pens()"`.
2. **Registration runs only behind the refresh button.**
   `git grep -l "RegisterNewPensAsync" -- SemiPlot/SemiPlot.UI` prints exactly
   `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`, and
   `git grep -n "IPenCatalogueEditor" -- SemiPlot/SemiPlot.UI/Startup SemiPlot/SemiPlot.UI/App.axaml.cs`
   prints nothing. Unit,
   `FullyQualifiedName~ArchiveExceptionMapperTests.AnUndefinedFunctionMapsToTableMissingCarryingTheFunction`,
   1 passed: 42883 with the relation `semiplot_register_new_pens()` maps to `TableMissing` carrying it.
3. **Each setting writes its own column and nothing else.** Integration,
   `FullyQualifiedName~PenCatalogueEditorTests`, at least 12 passed for items 3 to 6 together: two writes
   of two different columns to one pen, issued as two instances would, both survive; the scale pair
   writes both bounds in one statement; `ReadAsync` returns a stored `NULL` colour as null and a stored
   unusable mask as it is stored.
4. **The server's refusals arrive as write faults.** Same filter: a half-set scale pair and an inverted
   pair give `ValueRejected` with `Detail` equal to the pen's name; a duplicate group name gives `NameTaken`
   with `Detail` equal to the name asked for; an update of a pen and a rename of a group that no longer
   exist give `RowGone` (zero rows affected is a failure, not a success); `SetMembershipAsync` for a
   deleted group gives `RowGone` (23503); none of them maps to `ReadFailed`.
5. **SemiPlot cannot manage SCADA's keys.** Same filter: an `INSERT` into, a `DELETE` from and an `UPDATE
   ... SET id` on `semiplot_tags` issued over the resolved `NpgsqlDataSource` fail and `MapWrite` maps each
   to `AccessDenied`. Unit, `FullyQualifiedName~PenCatalogueEditorSurfaceTests`, 1 passed: the method names
   `IPenCatalogueEditor` declares are exactly `ReadAsync`, `RegisterNewPensAsync`, `ChangeAsync`,
   `CreateGroupAsync`, `RenameGroupAsync`, `DeleteGroupAsync` and `SetMembershipAsync`.
6. **Deleting a group removes its memberships.** Same filter: after deleting a group with three pens, none
   of the three has a membership row, the pens remain, and the catalogue read shows them ungrouped.
7. **The form commits, refuses and reverts, one write at a time.** Unit,
   `FullyQualifiedName~PenFormViewModelTests`, at least 12 passed:
   1. a valid edit calls the one change it names; an unchanged field writes nothing;
   2. an empty name, a non-empty mask `PenValueFormat.IsAcceptable` refuses, an empty colour, a colour
      not `#RRGGBB`, and a half-set or inverted scale pair never reach the editor, revert, mark the field
      and fill `Message`;
   3. an empty mask writes `Format(null)`;
   4. a stored `NULL` colour opens as an empty, invalid colour field;
   5. with `CultureInfo.CurrentCulture` set to `ru-RU` inside the test, the scale text `1,5` commits 1.5;
      the culture's `NaNSymbol`, `PositiveInfinitySymbol` and `1e999` are refused as scale bounds;
   6. a failed write reverts, marks, fills `Message` and adds one message-panel entry;
   7. two commits issued while the fake holds the first both reach the editor, in the order issued, as
      read after `WhenIdleAsync()`;
   8. the mask preview is `PenValueFormat.Format` of the sample under the draft.
8. **Groups: create, rename, delete, membership.** Unit, `FullyQualifiedName~PenGroupsViewModelTests`, at
   least 6 passed: delete first sets a pending confirmation carrying the member count and writes nothing;
   cancelling writes nothing; confirming deletes once and updates the affected rows' group text; a
   rename commit on one `PenGroupViewModel` writes that group only; a membership toggle calls
   `SetMembershipAsync` once; a failed one raises `PropertyChanged(nameof(IsMember))` with `IsMember`
   unchanged and adds one message-panel entry.
9. **Refresh finds new pens; the table sorts.** Unit, `FullyQualifiedName~PenEditorViewModelTests`, at
   least 5 passed: refresh calls registration, re-reads the catalogue, shows the new rows and the added
   count, and keeps the selected pen when it still exists; a refresh issued while the fake holds a commit
   calls registration only after that commit completes; a sort command orders the rows, a second one on
   the same column reverses them, `SelectedRow` and the `SelectedForm` instance stay the same, and nothing
   is written; `WhenIdleAsync` completes only after every queued call.
10. **The editor opens from the menu, and only where it can work.** Unit,
    `FullyQualifiedName~AppMenuBarTests|FullyQualifiedName~MainWindowView|FullyQualifiedName~CompositionRootTests`,
    at least 39 passed (34 on `44811da`, plus 5): `EditMenu` holds `EditSettings` then `EditPensAndGroups`,
    each bound to its command; with an editor, the command reads the catalogue and emits one
    `PenEditorViewModel`, and a failed read reports once and emits nothing; the startup-failure window
    carries `EditPensAndGroups` bound to `ShowPenEditorCommand` with `IsEffectivelyEnabled == false`;
    clicking the item on a realised window opens `PenEditorWindow` over it, and after the window closes,
    setting `Groups.NewGroupName` to a non-blank name leaves `CreateGroupCommand.CanExecute(null)` false,
    the disposed-command check `MainWindowViewTests.cs:116-119` uses; the production container resolves
    `IPenCatalogueEditor`.
11. **The empty chart names the way in.** Unit,
    `FullyQualifiedName~TrendChartViewTests.EmptyCatalogueMessage_ShowsWithNoPensAndWithdrawsWhenOneArrives`,
    1 passed: the realised chart with no pens shows the `TextBlock` whose text equals
    `Resources.FormatEmptyCatalogueMessage(Resources.MenuEdit, Resources.MenuEditPensAndGroups,
    Resources.PenEditorRefresh)` and contains each of those three values.
12. **The window works through its real controls.** Unit,
    `FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenGroupsViewTests`, at least
    19 passed, over the realised window with a fake editor:
    1. the bottom bar shows the refresh button and the added count on both tabs, and the visibility column
       header reads `Resources.PenEditorColumnOnStart`;
    2. clicking a row fills the form with that pen; with no row selected the form's fields are disabled;
       clicking through three rows, one of them with the other line style and the other on-start state,
       leaves the fake with no recorded change;
    3. typing a new name and pressing Tab writes it once and the row's name cell shows it;
    4. typing `%0.0` into the mask and pressing Tab writes nothing, shows the committed mask again, gives
       the field the `invalid` class and fills the message line;
    5. a name the fake refuses with `NameTaken` reverts, marks the field and adds one message-panel entry;
    6. the window's and the form panel's `Bounds` are equal before and after step 4;
    7. typing a scale minimum and tabbing into the maximum writes nothing; typing the maximum and tabbing
       out of the pair writes one `Scale` change carrying both bounds;
    8. typing a name and clicking another row writes the name to the first pen;
    9. clicking a column header reorders the rows, the same pen stays `SelectedRow` and the `ListBox`'s
       `SelectedItem`, and nothing is written;
    10. with 500 pens the table realises fewer than 500 row containers;
    11. on the `Groups` tab, selecting a group and clicking an unchecked pen's checkbox calls
        `SetMembershipAsync(pen, group, true)` once, and the `Pens` tab's groups cell of that pen names the
        group;
    12. with the fake refusing the toggle, the realised checkbox's `IsChecked` is back to false after the
        click, and the message panel holds one entry;
    13. clicking delete shows the confirmation naming the member count; cancel writes nothing;
    14. typing a new name into the rename field and clicking another group renames the first group once
        and leaves the second untouched;
    15. typing a name and closing the window writes it once, before the window is gone;
    16. clicking the picker, clicking a palette swatch inside its flyout and pressing Escape, all through
        `HeadlessInput` on the flyout's `TopLevel`, writes one `Color` change carrying that swatch's colour
        as `#RRGGBB`;
    17. typing `red` into the colour field and pressing Tab writes nothing and marks the field;
    18. on a pen with no colour, opening the picker's flyout and pressing Escape writes nothing;
    19. typing both scale bounds and clicking another row writes one `Scale` change to the first pen.
13. **The chart still never persists anything.** `grep -rl "IPenCatalogueEditor" SemiPlot/SemiPlot.UI/Chart
    SemiPlot/SemiPlot.UI/Legend` prints nothing, and `grep -c "IDataProvider"
    SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs` and `grep -cE "Insert|Update|Write|Save"
    SemiPlot/SemiPlot.Core/Data/IDataProvider.cs` still print 0.
14. **Both suites green.** `dotnet test SemiPlot.slnx` reports zero failed for both projects.
15. **Manual smoke on the stand** (`dotnet run --project SemiPlot/SemiPlot.AppHost`):
    1. Open `Edit` -> `Pens and groups`: every bench pen is listed with its settings.
    2. Click the name header, then the id header: the rows reorder by each.
    3. Select a visible pen and choose a new colour in the picker: within a second, with the editor still
       open, the chart line and the sidebar dot behind it draw the new colour, and the pen keeps its
       visibility.
    4. Close the editor, drag that pen's axis to a range of your own, reopen the editor, set its scale to 0
       and 100 and tab out of the pair: within a second the axis shows 0 to 100. Close the editor, drag the
       axis again and wait 10 s: the dragged range stays.
    5. Enter the mask `%0.0` and press Tab: the field reverts and turns red, and the message line says why.
    6. Enter the colour `red` and press Tab: the same, and the window keeps its size.
    7. Enter only a scale minimum and tab out of the pair: the same.
    8. Type the mask `0.00`: the preview shows the sample with two decimals.
    9. On `Groups`, create a group, check two pens, rename it, press delete: the confirmation names two
       pens.
    10. As `scada_writer` on `127.0.0.1:55432`, database `semiplot_app`, run
        `INSERT INTO trends (id, l, t, v, q) VALUES (4242, 0, localtimestamp, 1, 0);`, then press Refresh:
        a hidden pen named `4242` appears in the table and the added count reads 1.
    11. Within a second of the refresh `4242` is in the sidebar behind the editor, switched off. Close the
        editor and switch it on: its row is drawn.
    12. Start a second viewer beside the stand's, from the repository root in PowerShell:
        `dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir
        $env:TEMP\SemiPlot\ConfigFiles --log-file $env:TEMP\SemiPlot\Logs\second.log --logging-level
        information`. Edit one pen's unit in the first viewer and its name in the second: each viewer shows
        its own change within a second and the other's within 5 s. Delete a group in the first: within 5 s
        the second shows its pens under the Ungrouped header.
    13. As `postgres` on the same database run `DROP FUNCTION semiplot_register_new_pens();`, then press
        Refresh: the message panel shows "The archive is not provisioned" naming
        `semiplot_register_new_pens()`, and the table is unchanged.
    14. Start with a broken `connection/` section: the failure window's `Edit` -> `Pens and groups` is
        disabled.
    15. With the stand's viewer running, open `Edit` -> `Pens and groups`, select a visible pen, type a scale
        minimum and maximum, and click the empty area of the form below the fields: within a second the
        pen's axis on the chart behind the editor shows that range.
16. **First start on an empty catalogue** (manual, stand running):
    1. As `postgres` on `127.0.0.1:55432`, database `semiplot_app`, run `DELETE FROM semiplot_tags;
       DELETE FROM semiplot_groups;`: both statements report a non-zero row count.
    2. From the repository root, in PowerShell, run:

       ```powershell
       New-Item -ItemType Directory -Force SemiPlot\Artifacts\empty-config | Out-Null
       Copy-Item ConfigFiles\* SemiPlot\Artifacts\empty-config -Recurse -Force
       dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- `
         --config-dir SemiPlot\Artifacts\empty-config `
         --log-file SemiPlot\Artifacts\empty-logs\semiplot.log --logging-level information
       ```

       The startup-failure window names the empty password.
    3. In `Edit` -> `Settings`, set port `55432`, database `semiplot_app` and password
       `semibase-container-plot`, then save: the restart notice appears.
    4. Close the viewer and run the `dotnet run` line of step 2 again: the chart area shows the
       empty-catalogue text naming `Edit`, `Pens and groups` and `Refresh`.
    5. Open `Edit` -> `Pens and groups` and press Refresh: the table lists one hidden pen per key in `trends`,
       each named by its number, and the added count equals the row count.
    6. Rename one pen, tick its "on start", create a group on `Groups` and check the pen: each field keeps
       its value after Tab.
    7. Close the editor: the empty-catalogue text is gone, the sidebar lists the pens with the renamed one
       under its new name inside the new group, switched off, because "on start" is the visibility a pen
       starts with; the minimap shows the archive's extent. Switch the pen on: it is drawn. Click the left
       end of the minimap: the chart moves to the archive's first day.
17. **A catalogue read is compared by value, and a real read proves it.** Unit,
    `FullyQualifiedName~PenListDeltaTests`, at least 8 passed: two reads of one catalogue whose `Groups` are
    equal but distinct lists give an empty delta; a colour change gives one revision and no set change; a
    stored scale change gives `ScaleChanged`; a groups change and a rename each give a revision; a new pen
    is in `Added` and a gone pen in `RemovedPenIds`, each with `ChangesPenSet`; an `EnabledOnStart` change
    gives a revision; `Current` keeps the order of the read; an empty previous list gives every pen added;
    a change to any one member of `Pen` gives one revision and a different hash, and every member of `Pen`
    has such a row. Integration, `FullyQualifiedName~LiveCatalogueTests`, at least 4 passed: two `QueryPensAsync` reads
    with no write between give an empty delta; a colour written through `PostgresPenCatalogueEditor` gives
    one revision carrying it; a membership written through it gives a revision whose `Groups` differ; two
    reads of a pen stored with a `NULL` colour log one warning, not two; an unusable mask and an unknown
    line style each warn once, log the repeat at `Debug`, and warn again after the stored value changes.
18. **The live edge follows the pen set.** Unit, `FullyQualifiedName~TrendCoordinatorTests`, all passed, at
    least 3 tests more than the 13 that `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
    --list-tests` lists for the class at `7099271`: after `SetPens` with an added id, the one subscriber of
    `RealtimeBatches` receives batches carrying it; the provider holds exactly one live subscription after
    a switch; a provider failure after a switch reaches `RealtimeFailures` once.
19. **The chart applies a delta and keeps the session.** Unit,
    `FullyQualifiedName~TrendChartViewModelTests|FullyQualifiedName~TrendChartCatalogueTests`, all passed,
    at least 11 tests more than the 80 listed in `TrendChartViewModelTests` at `7099271`: a colour and
    line-style revision redraws the line in the new colour and step shape, read back as pixels, and keeps
    `IsVisible`, `ActivePenId` and the loaded history;
    a stored scale change replaces an axis set through `SetAxisLimits` on that pen only; a revision without a
    scale change keeps that axis; an `EnabledOnStart` revision keeps visibility; an added pen with
    `EnabledOnStart = false` joins hidden and one with `true` joins visible; an addition and a removal each
    hand the coordinator the new set and issue one history query carrying every pen; a revision-only delta
    hands the coordinator nothing and issues no history query; a removed active pen hands the active slot to
    the first visible pen in catalogue order; `Pens` follows the order of `delta.Current`; an empty chart
    that receives pens withdraws `HasNoPens`; a delta adding 500 pens computes the axis model once; the
    start catalogue, the coordinator's own pens, opens one live subscription.
20. **The sidebar follows the chart.** Unit,
    `FullyQualifiedName~TrendLegendViewModelTests|FullyQualifiedName~TrendLegendViewTests`, all passed, at
    least 4 tests more than the 44 listed at `7099271`: `Rebuild` shows a revised name, colour, unit and
    mask; it keeps `PanelWidth`, `IsExpanded` (tested collapsed) and every row's visibility; it assigns the
    new `Groups` before it disposes the replaced rows, and a visibility write on a replaced row reaches no
    chart; the realised sidebar shows a renamed pen's new name. The existing row allowlist of
    `TrendLegendViewTests` still passes.
21. **The read loop.** Unit, `FullyQualifiedName~PenCatalogueSyncTests`, at least 9 passed, over
    `TestScheduler`: the first read comes one interval after `Start`, then one every 5 s; no read starts
    while one is held; `ReadNow` during a held read gives exactly one read after it; `ReadNow` while waiting
    reads at once, and the next read comes a full interval after that one; an unchanged read emits no
    delta; one and two failed reads in a row report nothing, the third reports one panel entry, and the
    next success emits the whole change since the last success; a subscriber that throws adds one panel
    entry and the next read still runs, and the read it threw on is the baseline; a read that throws adds
    one panel entry at once and leaves the failure count; `Dispose` during a held third failure in a row
    reports nothing when the read lands; `Dispose` stops the reads.
22. **The main window applies what the loop emits.** Unit, `FullyQualifiedName~MainWindowView`, all passed,
    at least 4 tests more than the 26 listed at `7099271`: a pen write that lands in an editor the main
    window opened grows the provider's catalogue read count by one before the interval passes, and a write
    the fake refuses does not; a delta that adds a pen to a
    chart with pens reaches the chart, rebuilds the sidebar and reads the extent once; a delta that adds a
    pen whose rows predate the navigation's first sample lets the chart pan back to them; a delta that gives an
    empty chart its first pens seeds `Navigation.FirstSample` from the extent's `FirstUtc` before the
    history query goes out, and a failed extent read still lets the pens apply; a delta that adds no pen
    reads no extent; a delta whose extent reload throws adds one panel entry and the next delta still
    applies; a delta whose apply throws adds one panel entry and the next read carries it again; a delta
    queued behind an apply that throws reaches the chart whole, and a later revision of its pen applies;
    an apply still waiting for the extent at disposal applies and reports nothing.
23. **Nothing states the restart model for the editor.**
    `git grep -n -i -e "next start" -e "restart model" -e "PenEditorRestartNotice" -e "then restart" -e
    "restart notice" -e "restarted" -- CLAUDE.md docs/architecture readme.md SemiPlot` prints exactly 10
    lines: five about the settings window (`CLAUDE.md`, `docs/architecture/overview.md` twice,
    `docs/architecture/ui-theme.md`, `SettingsViewModel.cs`) and five about the stand's directory sweep
    (`CLAUDE.md`, `docs/architecture/bench.md` three times, `DemoDirectories.cs`).

24. **The refresh button says what it refreshes.** Unit, `FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~TrendChartViewTests.EmptyCatalogueMessage`, all passed: the realised button's content equals `Resources.PenEditorRefresh`, whose values are "Refresh pen list" and «Обновить список перьев»; the empty-catalogue text names it through the same key.
25. **A click on empty space ends the edit.** Unit, `FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenGroupsViewTests`, at least 4 tests more than at `dc0fc4d`, all passed, each through `HeadlessInput`: a name typed and then a click on a form label writes it once; both scale bounds typed and then a click on the form's empty area write one `Scale` change; a rename typed and then a click on the groups tab's empty area renames once; a click on empty space with no edit in progress writes nothing.

## Progress Tracking

- Mark completed items `[x]` when done.
- New tasks discovered during the work get a `+` prefix; blockers get `!`.
- Update this plan if the work deviates from the scope above.

## Solution Overview

**The write path is its own interface.** `SemiPlot.Core/Data/IPenCatalogueEditor.cs` declares everything
the editor may do and nothing more (Technical Details). The Postgres implementation shares the
`NpgsqlDataSource` singleton and the one role. `IDataProvider` stays read-only and neither `Chart/` nor
`Legend/` references the new interface, so the chart provably never writes (acceptance item 13).

**Registration lives only behind the refresh button.** A registered pen is hidden and named by its number,
so the chart has nothing new to draw after it, and the operator opens the editor to name and enable it
either way. Registration at start would put the key scan, timed in Context, on every start, and a
database whose provisioning lacks the function would fail every start with 42883 instead of failing the
one Refresh an operator pressed. `PenEditorViewModel.RefreshCommand` is the one caller of
`RegisterNewPensAsync`, and acceptance item 2 gates it. A second call started while the first is
uncommitted waits on the first call's keys, then skips them under `ON CONFLICT (id) DO NOTHING`.
ASSUMPTION: the waiting call returns 0 with no deadlock and no fault once the first commits. Settled by
acceptance item 1.3. Fallback: the implementation runs the call inside a transaction that first takes
`pg_advisory_xact_lock` on one constant key, which PostgreSQL grants to `PUBLIC`; item 1.3 then holds the
lock instead of the function call, and no other item changes.

**One setting, one column, one statement.** A change carries exactly one column, so two instances editing
different settings of one pen never overwrite each other. The scale pair is the exception and one unit:
`semiplot_tags_scale_paired` rejects a half-set pair, so the two bounds are edited together and written in
one `UPDATE` of both columns. Every `UPDATE`, rename and delete checks that it touched exactly one row;
zero rows means the pen or group is gone and is reported as `RowGone`, never as success.

**Validate before writing, map what the server still refuses.** The form refuses what the viewer itself
would not use: an empty name; a non-empty mask `PenValueFormat.IsAcceptable` refuses (an empty mask is
written as `NULL`); an empty colour or one not `#RRGGBB`; a scale bound whose text does not parse under
`CultureInfo.CurrentCulture` or parses to a non-finite value; a half-set or inverted pair. The server's
constraints stay the last word, mapped as Faults states. No statement the editor issues can raise 23502
or 22P02: every `NOT NULL` column it writes takes a non-null bound value, and every parameter is bound
with its type.

**The reused kinds speak of no operation.** `AccessDenied`, `Unreachable`, `QueryTimedOut`, `TableMissing`
and `ReadFailed` serve reads and writes alike, so their operator text and their `Describe` lines stop
naming a read. `ReadFailed` stays the default arm of both mappings and keeps its name, which the operator
never reads; no `WriteFailed` kind is added, because its title, detail and remedy would repeat the
reworded `ReadFailed` ones. A missing registration function is a missing relation to the operator, so
42883 takes the `TableMissing` remedy, "run semibase site".

**The editor reads the catalogue as stored.** It reads fresh from the tables when it opens and after
refresh, never from the startup catalogue, or it would revert fields to values another instance already
replaced. It reads the stored values, not `ReadPen`'s normalised ones: a pen with no colour opens with an
empty, invalid colour field, and a stored mask the rule refuses opens as written and invalid, the way the
settings dialog opens a file value its rule refuses (`docs/architecture/overview.md#the-settings-window`).

**One window, two tabs.** `PenEditorWindow` holds a `TabControl` with `Pens` and `Groups`, and below it a
bottom bar present on both tabs: the added count and the refresh button, right-aligned. The visibility
column and field are labelled "on start": the flag is the visibility a pen starts with, and switching it
changes nothing on the running chart.

- The `Pens` tab is a table above an edit form. The table is read-only: a header row of sort buttons over
  a `ListBox` with a `VirtualizingStackPanel` and a fixed row height, each row a `Grid` with the same fixed
  column widths as the header, both read from one unshared `ColumnDefinitions` resource. Columns: id, "on start" (a read-only tick), colour swatch, name, unit,
  mask, line style, scale min, scale max, groups. A header click sorts by that column; a second click
  reverses. No grid package is added.
- Selecting a row shows the form for that pen below the table, laid out as Window layout and size states:
  an invalid field is marked by the `invalid` class alone, and one `TextBlock.form-message` line is
  reserved. With no row selected the fields are empty and disabled. Fields: name, unit, mask with a live
  preview, colour, line style, visibility on start, and the scale pair as two `TextBox` fields.
- The `Groups` tab puts the group list on the left, with a new-name field and a create button above it,
  one rename field bound to `SelectedGroup.RenameDraft`, and a delete button. On the right are the pens of
  the catalogue as checkboxes, checked where the pen is a member of the selected group. A delete first
  shows a confirmation bar in a row the tab always reserves, naming the group and its member count, with
  confirm and cancel. The tab has its own reserved message line.

**One queue for every editor call.** `PenEditorViewModel` owns one `EditorCallQueue`, and every call to
`IPenCatalogueEditor` runs through it: pen commits, the scale pair, membership toggles, group create,
rename and delete, and refresh. The queue is a task chain, FIFO by construction: each call starts after
the one before it has finished. The committed values follow the order of the writes, a commit issued
while another is in flight waits instead of being dropped, and a refresh reads only after every commit
issued before it. No text or choice edit goes through a command whose `CanExecute` could refuse it:
code-behind calls `PenFormViewModel.EndEditAsync(PenField)` and `PenGroupViewModel.EndRenameAsync()`
directly. While a refresh runs, the pen form and the whole groups tab are disabled, because the rebuild
replaces the form, the rows and the groups they edit: an edit ended on a form or group nobody shows any
more would write through an orphaned row. Disabling is the smaller of the two
options; ending the recorded edits in code-behind and rebuilding only after the queue drains would move
refresh sequencing into the window. A click on `Refresh` takes the focus, so the edit in progress ends and
queues before the registration.

**Commit at the end of an edit.** The text fields bind their drafts with the default `PropertyChanged`
trigger, so the draft is already in the view model when the edit ends. The edit ends on the view model
that owned the field when focus entered it, whatever `DataContext` holds by then, so a click on another
row commits to the pen the field was showing. The scale pair ends its edit only when focus leaves the
pair, so tabbing from the minimum to the maximum commits no half pair. The line-style combo box and the
"on start" checkbox read their drafts one way and write only a value that differs from the committed one,
so a binding pushing a newly selected pen's values into them writes nothing. The routing is input interop
the avalonia rules allow in code-behind; the decision stays in the view model.

**The queued writes belong to the row.** A new form is built on every selection, so the writes queued for
a pen live on its `PenRowViewModel`, which exposes the pen as queued. Every form of the row seeds its
drafts from that pen and compares with it, and the selected form follows it: a draft still showing the
queued value takes the new one when a write is queued, lands or fails; a draft the operator changed stays.
A form built while a write is in flight therefore shows the value being written, and a Tab through an
untouched field writes nothing.

**Revert, mark, say why.** A refused or failed commit sets the draft back to the value the field holds
once every write of it still queued succeeds, and then marks the field, in that order, so the mark
survives its own revert. A failed write with a later write of the same field still queued only marks:
reverting would undo the later edit. The mark clears when the operator
next changes that field's draft or when a later commit of it succeeds. `Forms.axaml:5-11` styles
`invalid` for `TextBox` and `NumericUpDown` only, so the line-style combo and the "on start" checkbox,
which refuse no value, show a failed write on the message line and in the panel only. The message line
shows the first rule a draft breaks, in form order; otherwise the last refusal. For a failed write that
text is the title `ArchiveFailureMapper.Map` gives the error, so the mapper stays the only owner of the
wording, and the full entry goes to the message panel through `ResultReporting.ReportFailure`.

**A membership checkbox writes through its command.** It reads `PenMembershipViewModel.IsMember` one way
and writes only through that view model's `ToggleMembershipCommand`, so only its own box disables while
its write runs. The control flips itself on the click, so a failed toggle raises
`PropertyChanged(nameof(IsMember))` with the value unchanged and the binding puts the box back.
ASSUMPTION: Avalonia 12.0.5 re-pushes a one-way binding's value on a `PropertyChanged` whose value did not
change, and `ToggleButton` flips `IsChecked` without removing the binding. Settled by acceptance item
12.12. Fallback: the toggle sets `IsMember` to the clicked state before the write and back after a
failure, two real changes the binding always follows; item 8's last clause then reads "a failed one
leaves `IsMember` at its value before the click", and 12.12 is unchanged. Outcome (Task 9): the assumption
holds. After a refused toggle the realised box reads unchecked again, and without the re-raise it stays
checked, so no fallback was built. A group keeps one entry per pen for the life of the groups tab, so the
box shown after the group is selected again is the entry whose toggle may still run, and it shows the
toggle when it lands; an entry rebuilt on reselection would show the value from before the toggle, and
the next click would write the opposite of what the operator clicked.

**A sort keeps the selection.** `SortCommand` replaces `Rows` with a new list of the same
`PenRowViewModel` instances, so `SelectedRow` still points at a row in it and `SelectedForm`, derived from
`SelectedRow` through `WhenAnyValue` -> `DistinctUntilChanged` -> `Select` -> `Switch` -> `ToProperty`,
keeps its instance and its drafts. ASSUMPTION: replacing a `ListBox`'s `ItemsSource` with a list that holds the selected instance
keeps `SelectedItem` and writes no null back through the two-way binding. Settled by acceptance item
12.9. Fallback: `PenEditorViewModel` brackets the replacement with a flag that ignores the selection
write-back, the one bracket `csharp.md` allows for a programmatic mutation of a two-way-bound selection,
and raises `PropertyChanged(nameof(SelectedRow))` after the swap so the binding pushes the row back into
the `ListBox`; items 9 and 12.9 are unchanged. Outcome (Task 7): the assumption does not hold. The
`ListBox` ends on the same row, but it writes a null selection back first, so `SelectedForm` was rebuilt;
the fallback is built in `PenEditorViewModel.Sort`. The group list does the same when create or delete
replaces `Groups` (Task 9): a delete confirmed while another group is selected rebuilt that group's
`Memberships`, so `PenGroupsViewModel.ReplaceGroups` carries the same bracket.

**Closing the window drains the queue.** The window lets no close request through until the recorded
edits have ended and the queue is idle (The window's code-behind). `MainWindow` disposes the view model
after `ShowDialog` returns, so nothing is disposed under a write in flight, and a name typed just before
the close is written once.

**The colour field.** A `TextBox` takes `#RRGGBB` and commits like every text field. Beside it an Avalonia
`ColorPicker` (`Avalonia.Controls.ColorPicker` 12.0.5 with Semi's theme `Semi.Avalonia.ColorPicker`
12.0.3, `<semi:ColorPickerSemiTheme />` in `App.axaml`), alpha off, its palette and hex input shown, reads
the draft one way through `PenColorConverters.ToColor`, which gives `Colors.Transparent` for an empty or
malformed draft. The code-behind records the picker's `Color` when its drop-down flyout opens and, when it
closes, hands the colour to `PenFormViewModel.PickColorAsync` only if it differs from the recorded one, so
opening and dismissing the flyout on a pen with no colour writes no `#000000`. The picker never writes the
draft through a binding, so the form stays the draft's one writer. Both package versions are in the local
NuGet cache, and `Semi.Avalonia.ColorPicker` 12.0.3 depends on `Avalonia.Controls.ColorPicker` 12.0.3 or
later and on `Irihi.Avalonia.Shared` 0.4.0, which `Semi.Avalonia` 12.0.3 already brings.
ASSUMPTION: the Semi theme's `ColorPicker` template, a `DropDownButton` with a `Flyout`, lets the
code-behind reach that flyout and observe its `Opened` and `Closed`; the picker renders under the headless
platform; and its inner `ColorView` writes `ColorPicker.Color` without removing the one-way binding, so
after a pick in row A, selecting row B shows B's colour. Settled by Task 8's first probe. Fallback: the
picker is dropped and the field is the `#RRGGBB` text box plus a row of swatch buttons, one per distinct
colour the catalogue already carries, each committing on click; no package is added and no colour list is
copied from SemiBase. Item 12.16 then clicks a swatch button, 12.18 is dropped and item 12's count becomes
18, and Task 8 probes `Button` instead of the picker in `ThemeTests`. Outcome (Task 8): the assumption
holds, the picker is kept, and item 12's count stays 19.

**Where the editor's view model is built, and when the command can run.** The settings request path of
Context, copied. `MainWindowViewModel` takes a trailing `IPenCatalogueEditor? penCatalogueEditor = null`.
`AddUi` passes the container's editor; the startup-failure path (`App.axaml.cs:73`) and the direct
constructions in tests leave it out, because a failed startup disposes the container and there is no data
source to write through. `ShowPenEditorCommand` is built with `CreateFromTask` and the availability
`Observable.Return(penCatalogueEditor is not null)`, decided once at construction, so the item exists on
both windows, carries its command, and is disabled on the startup-failure window. A connection lost after
start does not disable it: the read fails, `ArchiveFailureMapper` maps it (`Unreachable`, a warning), and
no window opens. On success the command emits a `PenEditorViewModel` over the catalogue on
`PenEditorRequests`, and `MainWindow.axaml.cs` shows `PenEditorWindow` and disposes the view model as
`ShowSettings` does.

**The empty chart names the way in.** `EmptyCatalogueMessage` becomes a format over the three labels the
operator clicks, `{0}` `Resources.MenuEdit`, `{1}` `Resources.MenuEditPensAndGroups` and `{2}`
`Resources.PenEditorRefresh`, and says to name and switch on the pens. `TrendChartView.axaml`
reads it through a `MultiBinding` whose `StringFormat` is `{x:Static text:Resources.EmptyCatalogueMessage}`
over three `x:Static` sources, the AXAML form `ui-text.md:59-69` prescribes, so a relabelled menu item or
button changes the sentence with it.

**Menu.** `Edit` -> `Pens and groups`, the second child of `EditMenu`, named `EditPensAndGroups`, label key
`MenuEditPensAndGroups`. The label carries no ellipsis, as `MenuEditSettings` carries none.

**The running chart follows the catalogue.** Every viewer re-reads the pen catalogue every 5 s through the
statement it reads at start, `IDataProvider.QueryPensAsync` (`PostgresDataProvider.cs:84`), so the read
carries the provider's normalisation and nothing repeats it. Every write the editor lands asks for one read
at once: an edit reaches the chart of the instance that made it within a second, while the editor is still
open, and every other instance within 5 s. The live edge itself arrives 1 to 2 s after SCADA writes, so 5 s between stations reads as immediate.
Nothing is pushed between instances. `LISTEN/NOTIFY` loses what is sent while an instance is disconnected,
so the periodic read would stay as the fallback in that design too; one statement of a few hundred rows
every 5 s costs less than the live edge's statement per second.

A read is compared with the previous read, never with the chart's state, so a read can only carry what
someone stored:

- a read equal to the previous one changes nothing: chart, axes and sidebar are untouched;
- a changed name, unit, mask, colour or line style is applied to that pen in place;
- a changed stored scale pair replaces that pen's axis, including an axis the operator set in this
  session; a read with the pair unchanged leaves a session axis alone;
- a changed `enabled_on_start` changes nothing on screen: it is the visibility a pen starts with;
- a new pen joins with the visibility its `enabled_on_start` gives it, with its history and its live edge;
  a pen gone from the catalogue leaves the chart;
- any change rebuilds the sidebar from the chart's pens; the sidebar keeps its width and its expanded
  state, and every pen keeps its visibility;
- the first pens of a chart that started empty seed the navigation from the archive's extent, as a start
  with pens does, so the operator can reach the archive's first day.

A failed read is reported from the third failure in a row, the threshold the live edge uses, so one
reconnect after a server restart opens no panel.

The whole chart is not rebuilt on a change: the status bar binds to one coordinator's connection stream
once (`AppStatusBarViewModel.TrackArchiveConnection`), the navigation controller has no way to take a
window back, and a gesture started on the old chart would end on a disposed one.

The editor keeps its own snapshot while it is open and re-reads only on Refresh, so no read rebuilds a
form under the operator's hands; the last write per column still wins. The settings window keeps its
restart model: the connection it edits is the one every read runs over.

## Technical Details

### `IPenCatalogueEditor`

```csharp
public interface IPenCatalogueEditor
{
	Task<Result<PenCatalogue>> ReadAsync();
	Task<Result<int>> RegisterNewPensAsync();
	Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change);
	Task<Result<int>> CreateGroupAsync(string name);
	Task<Result> RenameGroupAsync(StoredGroup group, string name);
	Task<Result> DeleteGroupAsync(StoredGroup group);
	Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember);
}
```

The records carry the id the statement binds and the name a fault names, so `MapWrite` gets its `subject`
without a second read. The implementation reads only `Id` and `Name` from a parameter record; no
statement compares old values in its `WHERE`.

`SemiPlot.Core/Data/PenCatalogue.cs` holds `PenCatalogue(IReadOnlyList<StoredPen> Pens,
IReadOnlyList<StoredGroup> Groups)`, `StoredPen(int Id, string Name, string? Unit, string? Format, string?
Color, PenLineStyle LineStyle, bool EnabledOnStart, double? ScaleMin, double? ScaleMax)`, the row as
stored, and `StoredGroup(int Id, string Name, IReadOnlyList<int> MemberPenIds)`, whose member count is
`MemberPenIds.Count`. `line_style` goes through `StoredLineStyle.Read`, which logs an unrecognised value
at the level its caller's function gives; the editor gives `Warning` on every read.

`SemiPlot.Core/Data/PenSettingChange.cs` holds the closed family, one arm per column plus the pair:
`Name`, `Unit`, `Format`, `Color`, `LineStyle`, `EnabledOnStart`, `Scale(double? Min, double? Max)`. The
UI view models are its second consumer. The form turns an empty unit or mask into null, which the
implementation binds as `NULL`. The implementation
maps each arm to one fixed statement; no column name is ever interpolated.

Subjects: a pen change names `pen.Name` (the committed name, also for a `Name` change); a rename names the
new name on 23505 and `group.Name` on zero rows; a delete names `group.Name`; a membership names
`pen.Name` when the 23503 names the key `semiplot_pen_groups_pen_id_fkey` and `group.Name` otherwise.

`SetMembershipAsync` is idempotent: adding an existing membership is `ON CONFLICT (pen_id, group_id) DO
NOTHING`, removing an absent one touches zero rows and succeeds. A membership for a pen or group that no
longer exists fails with 23503 and maps to `RowGone`.

### Statements

All in `ArchiveStatements.cs`, bound, never interpolated:

- `StoredPens`: `SELECT id, name, unit, format, color, line_style, enabled_on_start, scale_min, scale_max
  FROM semiplot_tags ORDER BY id;`
- `StoredGroups`: one row per group, `grp.id, grp.name` and `coalesce(array_agg(membership.pen_id ORDER BY
  membership.pen_id) FILTER (WHERE membership.pen_id IS NOT NULL), '{}')` over `semiplot_groups grp LEFT
  JOIN semiplot_pen_groups membership`, `GROUP BY grp.id ORDER BY grp.name`.
- `ReadAsync` runs the two on one connection, with `PenCatalogRelations` as the failure detail.
- `RegisterNewPens`: `SELECT semiplot_register_new_pens();`, relation constant
  `RegisterNewPensFunction = "semiplot_register_new_pens()"`.
- One `UPDATE semiplot_tags SET <column> = @value WHERE id = @id;` per arm; `Scale` sets both bounds.
- `CreateGroup` (`INSERT ... RETURNING id`), `RenameGroup`, `DeleteGroup`, `AddMembership` (`ON CONFLICT
  (pen_id, group_id) DO NOTHING`), `RemoveMembership`.

### Faults

`ArchiveFault` gains `ValueRejected` (23514; detail: the pen's name), `NameTaken` (23505; detail: the name
asked for) and `RowGone` (23503 or zero rows; detail: the pen or group name). Each new kind gets an English
`Describe` line, a title, detail and remedy in both resource sets whose text wraps `{0}` (the archive) and
`{1}` (the subject), so no word of either language lives in `Detail`, and a `Warning` arm in
`ArchiveFailureMapper`: the operator's own edit was refused, and the next edit is the remedy.
`FailureSeverityTests.ArchiveFaults_SplitIntoWhatRetriesAndWhatNeedsTheOperator` gains that third bucket.

`ArchiveExceptionMapper` gains `MapWrite(Exception exception, string subject)`, which maps 23514, 23505 and
23503 to the three new kinds carrying `subject` and sends every other exception to the existing
classification with `ArchiveStatements.PenCatalogRelations` as its relation: each write touches one of
those three relations, one provisioning run creates all three, and `ArchiveFailureMapper.cs:147-148`
already makes the remedy independent of which one is absent. It also gains `RowGone(string subject)` for
a zero-row update, and its `MapSqlState` gains `PostgresErrorCodes.UndefinedFunction` on the
`TableMissing` arm, reading `relation` as 42P01 does.

The empty-detail `ReadFailed` log of `PostgresDataProvider.Map` (`PostgresDataProvider.cs:538-549`) moves
into `internal static class ArchiveFailureLog` (`ArchiveFailureLog.cs`) as
`Error LogIfUnexpected(Error error, Exception exception, ILogger logger)`, with the message "An archive
statement failed with an exception this code did not expect."; `PostgresDataProvider.Map` and every
failure path of `PostgresPenCatalogueEditor` call it, and the editor carries no copy.

Operator text reworded to name no operation. The placeholders stay as they are, which
`ResourcesTests.EveryRussianValue_UsesThePlaceholderIndicesOfTheNeutralOne` gates, and the timed-out
detail keeps `(SQLSTATE 57014)` for `ArchiveFailureMapperTests.cs:402`.

| Key | English after | Russian after |
| --- | --- | --- |
| `FailureArchiveAccessDeniedTitle` | The archive refused access | База данных отказала в доступе |
| `FailureArchiveAccessDeniedRemedy` | Check the user name and password in the connection file. Check the grants SemiBase gives the role. | Проверьте имя пользователя и пароль в файле подключения. Проверьте права, которые SemiBase выдаёт роли. |
| `FailureArchiveTableMissingDetail` | The archive {0} holds no '{1}'. | В базе данных {0} отсутствует «{1}». |
| `FailureArchiveTableMissingRemedy` | '{0}' is created by provisioning. Run 'semibase site' against this database to finish provisioning it. | «{0}» создаётся при первоначальной настройке. Выполните команду «semibase site» для этой базы данных, чтобы завершить её настройку. |
| `FailureArchiveQueryTimedOutTitle` | The archive ended the statement | База данных прервала запрос |
| `FailureArchiveQueryTimedOutDetail` | The server ended a statement against {0} (SQLSTATE 57014). | Сервер прервал запрос к {0} (SQLSTATE 57014). |
| `FailureArchiveQueryTimedOutRemedy` | Check statement_timeout for the role on the server. If the chart was reading, narrow its time window. Check whether an administrator cancelled the statement. | Проверьте настройку statement_timeout для роли на сервере. Если данные читал график, сократите его интервал времени. Проверьте, не отменил ли запрос администратор. |
| `FailureArchiveReadFailedTitle` | The archive rejected the statement | База данных отклонила запрос |
| `FailureArchiveReadFailedDetail` | The archive {0} rejected a statement (SQLSTATE {1}). | База данных {0} отклонила запрос (SQLSTATE {1}). |
| `FailureArchiveReadUnnamedDetail` | A statement against {0} failed before the server answered. | Запрос к {0} завершился ошибкой, прежде чем сервер ответил. |
| `EmptyCatalogueMessage` | The pen catalogue is empty. Open {0} > {1}, press {2}, then name and switch on the pens you need. | Каталог перьев пуст. Откройте {0} > {1}, нажмите «{2}», затем задайте имена нужным перьям и включите их. |

`ArchiveError.Describe` changes the same way: `:33` reads "The {archive} holds no '{detail}'.", `:36`
"The server ended a statement against the {archive} (SQLSTATE 57014).", and `:39-41` "The {archive}
rejected a statement for an unrecognised reason" with and without the SQLSTATE. `ArchiveFault`'s doc
comments on `TableMissing`, `QueryTimedOut` and `ReadFailed`, and `ArchiveExceptionMapper`'s summary and
`relation` doc, follow.

### The editor view models

All in `SemiPlot.UI/PenEditor/`, all `ReactiveObject` but the queue, none referencing a view type.

- `EditorCallQueue` (`EditorCallQueue.cs`): a `Task` tail, `Task<T> RunAsync<T>(Func<Task<T>> call)` and
  `Task WhenIdleAsync()`. `RunAsync` starts the call after the current tail, awaiting it with
  `ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing` so a thrown
  call reaches only its own awaiter and never stops the next, and makes the new call the tail.
  `IsLast(Task)` says whether nothing was queued after a call. `WhenIdleAsync` awaits the tail the same
  way and awaits again while the tail it awaited is no longer the last, so a call queued while it waits,
  such as refresh's read after registration, is waited for too: a close during registration otherwise
  disposes the view model before the read lands. Every caller runs on the UI thread, so the tail needs no
  lock. The queue owns nothing to dispose. `PenEditorViewModel` constructs it and hands it to every form
  and to the groups view model.
- `PenEditorViewModel`, the window's root, `IDisposable`. Built from an `IPenCatalogueEditor`, the
  `PenCatalogue` the menu command read, the `MessagePanelViewModel` and a logger.
  - `Pens` tab: `Rows` (`IReadOnlyList<PenRowViewModel>`, replaced whole on sort and on refresh),
    `SortCommand` (`ReactiveCommand<PenColumn, Unit>`, `PenColumn` declared in this file; the sort column
    and direction are private state), `SelectedRow` (two-way from the `ListBox`), and `SelectedForm`, a
    `PenFormViewModel` for the selected row or null, derived as the sort paragraph states.
  - `Groups`, the `PenGroupsViewModel`, replaced on refresh.
  - `RefreshCommand` (`CreateFromTask`, body through the queue): registration, which sets `AddedCountText`
    once it succeeds, then a fresh read, repeated while a call was queued behind it, because a write
    queued behind the read would land on a row the rebuild replaces; rebuilds rows and groups and keeps the
    selected pen and group by id when they still exist. A failure of either call reports to the panel and
    leaves the tables as they were. `IsRefreshing`, the command's `IsExecuting` through `ToProperty`,
    disables the pen form (a `MultiBinding` with `BoolConverters.And` over it and `SelectedForm`) and the
    groups tab while the refresh runs.
  - `WhenIdleAsync()` delegates to the queue; `ReportFailure(Exception)` serves the window's code-behind.
  - `Dispose` disposes its commands and `Groups`.
- `PenRowViewModel`, one table row. It holds the pen's `StoredPen`, the pen as queued (`QueuedPen`, the
  record with every change still queued applied) and its groups text, and the view reads them one way.
  `WriteAsync(PenSettingChange, Func<Task<Result>>)` counts the change as queued until the write settles
  and, on success, applies it to the row's current record with a `with` expression, not to a form's seed;
  `IsQueued(PenSettingChange)` says whether a write of that setting is still queued. The only other writer
  is the groups view model, of the groups text, after a membership change, rename or delete.
- `PenFormViewModel`, the edit form of one row, built when `SelectedRow` changes.
  - One draft per field (`Name`, `Unit`, `Mask`, `Color`, `LineStyle`, `EnabledOnStart`, `ScaleMin`,
    `ScaleMax`, the last two as text under `CultureInfo.CurrentCulture`) seeded from the row; `Is*Valid`
    per field, false while its draft breaks its rule or while the field carries the last refusal;
    `Message`, the form's reserved line; `MaskPreview`, `PenValueFormat.Format(MaskPreviewSample, Mask)`,
    so an unusable draft previews as the fallback the chart would draw.
  - `EndEditAsync(PenField field)`: validates the field's draft, returns when it equals the row's
    `QueuedPen`, otherwise writes the one change it names through `Row.WriteAsync`, the call taking the
    row's current record when it starts; on failure it reverts and marks as Revert, mark, say why states.
    The scale pair is one `PenField.Scale`. Comparing with the queued pen rather than the stored record
    keeps a second edit that returns a queued field to its stored value, and keeps a failed write from
    reverting the draft behind a later queued write of the same field.
    `PickColorAsync(Color)`, `ChooseLineStyleAsync(PenLineStyle)` and `ChooseEnabledOnStartAsync(bool)`
    set their draft and run the same path.
  - `PenField`, the field enum, lives in its file, and the rules a draft must keep live in
    `PenFormRules`. The form's one subscription is to its row's
    `QueuedPen`, which its untouched drafts follow; the form is `IDisposable` and `SelectedForm` is derived
    through `Observable.Using` inside `Switch`, so the form is disposed once the selection moves on. A
    commit started as focus left a field still completes against the pen it started for after that.
  - A form built for a row whose write is still queued seeds from the row's `QueuedPen`, so it shows the
    value being written. Seeded from the stored record, a Tab through the untouched field would write the
    older value back once the queued write landed.
- `PenGroupsViewModel`, the `Groups` tab.
  - `Groups` (`PenGroupViewModel` in its own file: its `StoredGroup`, member count, `RenameDraft`,
    `IsNameValid` and `EndRenameAsync()`, one per group, which compares with the last name it queued and
    follows the same revert, mark and message rules as the pen form), `SelectedGroup`.
  - `NewGroupName` and `CreateGroupCommand`, which cannot execute while the name is blank; a server
    `NameTaken` reverts nothing and fills `Message`; a success clears the field only while it still holds
    the name created.
  - `DeleteGroupCommand` sets `PendingDeletion` (group name and member count) and writes nothing;
    `ConfirmDeleteCommand` deletes through the queue and clears it; `CancelDeleteCommand` clears it. The
    confirmation is state the tab shows in its reserved row, with no dialog and no view seam.
  - `Memberships`, one `PenMembershipViewModel` per pen for the selected group, derived from
    `SelectedGroup` through `WhenAnyValue` -> `Select` -> `ToProperty`. The list of a group is built on its
    first selection and kept for the tab's life, so the one entry of a pen in a group is the only writer of
    that membership and raises `IsMember` when its toggle lands, whichever group was selected meanwhile.
    Each `PenMembershipViewModel`, in its own file, carries `IsMember` and its own
    `ToggleMembershipCommand`, whose body runs through the queue. The tab disposes every list it built,
    and a deleted group's list when the delete lands.
  - `PenGroupWriter`, the tab's one route to the editor, handed to the tab's groups and membership
    entries in place of the tab itself: it runs each call through the queue, reports failures, keeps the
    last refusal the tab's `Message` shows, and calls back into the tab to refresh the rows' groups text.
  - `Message`, the tab's reserved line.
- `PenColorConverters` (`PenColorConverters.cs`): `ToColor` (`FuncValueConverter<string?, Color>`, the
  picker, `static readonly` and consumed through `{x:Static}`), `TryParse`, the one `#RRGGBB` rule the
  form's colour field also applies, and `Format`, which writes a picked colour in that form. The row swatch binds `LegendConverters.HexToBrush`, the sidebar
  dot's converter, which draws a transparent swatch for a pen with no colour.

### The window's code-behind

`PenEditorWindow.axaml.cs` holds only edit-end routing and reaches the view models by pattern matching on
`DataContext` or on a recorded instance. It keeps two records: the text-field record and the scale record.

- A text field's `GotFocus` writes the text-field record: its form or group view model and its
  `PenField`, read from the box's `PenEditorWindow.Field` attached property. `LostFocus` takes the record and ends the edit on it; Enter ends the edit and keeps the
  record, so typing on after Enter still commits when focus leaves. A second end with no new typing
  writes nothing, because the draft equals the value queued. The rename field records `SelectedGroup`.
- The two scale boxes route no `LostFocus` and write no text-field record. The pair's container writes the
  scale record, the form its `DataContext` holds, when its `IsKeyboardFocusWithin` turns true, and ends
  `PenField.Scale` on that record when it turns false; Enter in either box ends it too. The subscription is
  made in `OnLoaded` and disposed in `OnUnloaded`.
- The combo box's `SelectionChanged` and the checkbox's `IsCheckedChanged` hand the new value to the form
  `DataContext` holds.
- No handler routes the refresh: the pen form and the groups tab bind their `IsEnabled` to
  `IsRefreshing`, and the refresh button takes the focus on its click, which ends the recorded edit.
- The picker flyout's `Opened` and `Closed` handlers, attached in `OnLoaded` and detached in `OnUnloaded`,
  record the colour and call `PickColorAsync` as The colour field states.
- `Closing` sets `e.Cancel` until the drain is done. One `Task?` field holds the drain: null until the
  first request, which starts it; a request while it runs is cancelled and starts nothing. The drain ends
  the text-field record's edit and, while the scale pair holds focus, the scale record's, awaits
  `WhenIdleAsync()`, and calls `Dispatcher.UIThread.Post(Close)`; the posted `Close` finds the drain
  completed and passes.
- Every handler is `async void` with its body in `try/catch`, and the `catch` calls
  `PenEditorViewModel.ReportFailure`.

### Window layout and size

The window opens at a fixed `Width` and `Height`, `SizeToContent="Manual"`, `CanResize="True"`: the
operator may widen it for the table, and nothing inside it sizes the window. Resizing grows the table and
the membership list only; the form panel, the groups tab's side panel and the bottom bar keep fixed
heights made of rows that exist in every state. The opening size, the column widths and the label column are measured with
every field valid in both languages, and at the opening size the window holds its size in both languages
with each field invalid in turn, as `ui-theme.md#a-form-never-resizes-on-validation` describes for the
settings dialog. The measured numbers go into `ui-theme.md`.

Every colour the window paints resolves to a `Styles/Palette.axaml` key; a pen's colour swatch is the pen's
own colour through `LegendConverters.HexToBrush`, not a theme key. Every string lives in both resource sets.

### The live catalogue

- `Pen` (`SemiPlot.Core/Trends/Pen.cs:3-13`) keeps its positional shape and gains `Equals(Pen?)` and
  `GetHashCode` that compare `Groups` element by element, ordinal, and every other member as the record
  does. `QueryPensAsync` builds a new groups list per row, so the synthesized equality, which compares
  `Groups` by reference, would call every pen changed on every read.
- `PenListDelta` (`SemiPlot.Core/Trends/PenListDelta.cs`), pure. It compares two lists of the chart's `Pen`
  and is unrelated to the editor's `PenCatalogue` record in `Core/Data`. `static PenListDelta
  Between(IReadOnlyList<Pen> previous, IReadOnlyList<Pen> current)` returns `PenListDelta(IReadOnlyList<Pen>
  Current, IReadOnlyList<Pen> Added, IReadOnlyList<int> RemovedPenIds, IReadOnlyList<PenRevision> Revised)`
  with `IsEmpty` (nothing added, removed or revised; order alone is no change) and `ChangesPenSet`
  (something added or removed). `PenRevision(Pen Previous, Pen Current)` carries `ScaleChanged`, true when
  either stored bound differs.
- `ArchiveStatements.PenCatalog` orders by `tag.name, tag.id` (`ArchiveStatements.cs:138` orders by name
  only), so two pens sharing a name keep one order across reads.
- `PostgresDataProvider` warns about a stored value it normalises (`ReadColor`, `:468`; `ReadFormat`,
  `:485`; the `StoredLineStyle.Read` call, `:452`) once per process for each pen, column and stored value,
  and logs a repeat at `Debug`. The set of warned triples lives in the provider. A hand-edited row would
  otherwise write a warning every 5 s.
- `TrendCoordinator` (`Bridge/TrendCoordinator.cs:117-131`): the id set becomes a
  `BehaviorSubject<IReadOnlyList<int>>` seeded from the constructor's pens; `BuildRealtimeBatches` reads it through
  `Select(_dataProvider.Subscribe)` and `Switch()`, ahead of the existing `Buffer`. `SetPens(IReadOnlyList<int>
  penIds)` pushes a set. The chart calls it only when a delta `ChangesPenSet`, so the coordinator carries no
  second change check. As built after the second review, the chart calls it when the id set it shows
  differs from the set it last handed over, so an apply that threw before the call is caught up. As built
  after the fourth review, it compares with the coordinator's `PenIds` instead, which starts at the
  constructor's pens, so the start opens one live subscription, not two. `RealtimeBatches` stays one published stream, so the chart's one subscription
  (`TrendChartViewModel.cs:80-81`) and the keep-alive (`TrendCoordinator.cs:95`) are untouched. A switch
  starts a new `RealtimePoll`, whose first tick reads the baseline and emits nothing (`RealtimePoll.cs:89-93`,
  `:146-171`): rows written between the old subscription's last tick and the new baseline, at most one poll
  interval, are not delivered live, and the history query every set change issues covers them. `Dispose`
  completes the subject. The constructor's rule (`:33-34`) holds for every set: the chart pushes only ids of
  the provider's own catalogue.
- `EnvelopeLine` (`Chart/EnvelopeLine.cs`): `Color` and `PenLineStyle` (`:30-42`) are read by `Render` on the
  render thread and from now on written on the UI thread after the first frame. `Restyle(Color color,
  PenLineStyle lineStyle)` writes both under `_columnsLock` (`:22`, `_renderStateLock` after the second review), `Render` reads both under that lock,
  and the lock's comment names both threads (`csharp.md`). As built (Task 14 deviation), the `Color` and
  `PenLineStyle` setters and the unused `LineWidth` property are gone, so `Restyle` is the one writer.
  The second review deleted the two getters, which only tests read; the tests read the colour and the
  step shape back from a rendered frame (`RenderedRise`).
- The plot's own plottable and axis lists: ScottPlot's `Plot.Render` holds `Plot.Sync` for a frame, and a
  delta adds and removes plottables and adds axes after the window has shown, so `ApplyCatalogue` and
  `AddPen` run their plot and axis edits under `lock (Plot.Sync)`, one block per call, and redraw after it
  (`charting.md#per-pen-plottable-envelopeline`; review fix, `TrendChartRenderThreadTests`). As built after
  the second review, `AddPen` is gone and the live-edge switch and the history query run after the lock,
  which guards the plot's lists alone.
- `TrendPenState` (`Chart/TrendPenState.cs:16`): `Pen` gets a private setter; `Revise(Pen pen)` sets it and
  calls `Line.Restyle`. `IsVisible` and the history stay. Nothing binds to `Pen`: the hover readout reads it
  per pointer move (`TrendChartView.axaml.cs:488`) and the sidebar reads it when it builds a row. As built
  after the second review, the setter raises `Pen` like the other two properties.
- `TrendChartViewModel`:
  - `Pens` (`:118`) becomes `IReadOnlyList<TrendPenState>` in catalogue order, a list rebuilt when pens are
    added, removed or reordered, never per read of the property. `ActivateAVisiblePen` (`:416-432`) and the
    active-pen fallback of `RemovePen` (`:268`) walk that list, so the pen that takes the active slot is the
    first visible one in catalogue order.
  - `AddPen` and `RemovePen` keep their contract and move their bookkeeping into private cores that do not
    apply the axis model; each public member calls its core, then `ApplyAxisModel` and `RequestRedraw`.
    As built, `RemovePen` had no production caller once `ApplyCatalogue` existed, and the review pass
    deleted it; its tests go through a removal delta.
  - As built, `ApplyHistory` skips an envelope whose pen a delta removed while the query was in flight,
    and `ApplyAxisModel` runs over no pen too, so a delta that removes every pen clears the stored scales.
  - `ApplyCatalogue(PenListDelta delta)`: the remove core per removed id; `Revise` per revision, and for a
    revision with `ScaleChanged`, `_settingsById[id] = BuildScaleSettings(revision.Current)` (`:402-412`);
    the add core per added pen, which seeds visibility from `EnabledOnStart` (`:399`); the list reordered
    to `delta.Current`; when `ChangesPenSet`, `_coordinator.SetPens` over the new ids and then
    `_lastFetch = null; RequestInitialHistory();` (`:48`, `:209-225`), because a pen added after the first
    fetch otherwise gets no history (`IsWindowFetched`, `:466-470`) and a switch leaves a hole in the pens
    that stay; then `ApplyAxisModel` and `RequestRedraw` once, so a registration of 500 pens computes the
    axis model once.
  - The class is past the 300-line preference before this plan (646 lines). `ApplyCatalogue` adds about 40
    lines; splitting the class is not part of this plan.
  - As built after the second review (`charting.md#applying-a-catalogue-read`): the pen dictionary, the
    scale settings and the ordered list moved to `Chart/ChartPenSet`, which rebuilds the list from the
    dictionary on every apply; `ApplyCatalogue(IReadOnlyList<Pen> catalogue)` takes the read and compares
    it with the pens shown, so a delta read against a baseline the chart never took still converges;
    `Pens` and `HasNoPens` raise after every apply; the live edge follows the set shown; a set change
    queries history only after `RequestInitialHistory`, and the start sequence seeds the chart through
    `ApplyCatalogue`, so `AddPen` is deleted and the tests add pens through `ChartTestBuilder.AddPen`,
    which applies a catalogue. The class is 635 lines.
- `TrendLegendViewModel` (`Legend/TrendLegendViewModel.cs:25-40`): `_rows` and `Groups` become replaceable
  and `Groups` raises `PropertyChanged`. `Rebuild()` builds rows and groups from the chart's `Pens` through
  the existing `BuildGroups` (`:106-128`), assigns `Groups`, and only then disposes the rows and groups it
  replaced, so no binding reads a disposed row. Widths and `IsExpanded` are its own fields and stay. A row
  reads its pen when it is built (`TrendLegendRowViewModel.cs:53-59`), and `CurrentValueText` (`:36-39`)
  reads `Pen.Format` of its pen state, so a rebuilt row shows a new mask. As built, `TrendLegendRowViewModel`
  did change (Task 15): its visibility setter stops reaching the chart once the row is disposed.
- `PenCatalogueSync` (`SemiPlot.UI/Bridge/PenCatalogueSync.cs`), `IDisposable`, built from the
  `IDataProvider`, the start catalogue, the `MessagePanelViewModel`, the UI scheduler and a logger; it
  references no chart, sidebar or minimap type. `ReadInterval` is a 5 s constant. `Start()` runs one loop on
  the UI scheduler through `ScheduleAsync`, as the provider's poll loop runs on its own scheduler: wait for
  `ReadInterval` or a `ReadNow()`, whichever comes first, then read. The read is asynchronous I/O and holds
  no thread; the loop, `ReadNow`, the snapshot and `Dispose` all run on the UI thread, so nothing is locked.
  The contract:
  - the first read comes one interval after `Start`: the start sequence has just read the same catalogue;
  - reads never overlap;
  - `ReadNow()` during the wait starts a read at once; during a read it makes exactly one more read follow
    it; the wait after any read is a full interval. As built after review, one non-null
    `TaskCompletionSource` carries the pending wake-up, replaced after each wait;
  - a read that throws, rather than answering with a failed `Result`, goes to the panel at once and leaves
    the failure count as it was;
  - a success computes `PenListDelta.Between(snapshot, read)`, emits a non-empty delta on `Deltas` (a
    private subject exposed through `AsObservable()`), and makes the read the snapshot. The emission sits in
    `try/catch`, and a subscriber's throw goes to the panel through `TryReportFailure`, so the loop
    survives it;
  - a failure keeps the snapshot, so the next success carries everything since. The third failure in a
    row and every one after it go to the panel through `ResultReporting.ReportFailure`, and a success
    resets the count. Three is the live edge's threshold (`RealtimePoll.cs:28`): one reconnect after a
    server restart fails one read and must not open the panel. As built after the second review, both read
    `ArchiveConnectionState.ConsecutiveFailuresBeforeFault`;
  - `Dispose` cancels the wait; a read landing after it emits nothing.
- `MainWindowViewModel` (`MainWindow/MainWindowViewModel.cs`):
  - `SetCatalogueSync(PenCatalogueSync sync)`, called once, owns the sync and applies its `Deltas` in order,
    one at a time, through `Select(delta => Observable.FromAsync(() => ApplyCatalogueAsync(delta)))` and
    `Concat()`. `ApplyCatalogueAsync` wraps its whole body in `try/catch` and reports a throw through
    `ReportFailure`, as `TrendChartViewModel.ApplyRealtimeBatch` (`TrendChartViewModel.cs:622-634`) does:
    1. when the chart has no pens and the delta adds some, it reads the extent through
       `MinimapViewModel.LoadExtentAsync` (`Minimap/MinimapViewModel.cs:80-86`), which now returns the
       result after applying it, and seeds `ChartViewModel.Navigation.SeedFromArchiveExtent` with a
       successful one before anything else: the navigation latches on the first data it sees
       (`ChartNavigationController.cs:65-93`), and a history envelope would otherwise latch the first
       sample of a one-hour window;
    2. `ChartViewModel.ApplyCatalogue(delta)`;
    3. `LegendViewModel.Rebuild()`;
    4. when the delta added a pen to a chart that already had some, `MinimapViewModel.LoadExtentAsync()`,
       because the extent read at start does not know the new pen. As built after the fourth review, a
       successful read also goes to `Navigation.WidenToArchiveExtent`, which moves the pan floor back to an
       earlier first sample and leaves the window.
  - As built after review, a throw ahead of the end of step 3 also rebases the sync on the pens the chart
    showed before the delta (`PenCatalogueSync.Rebase`), so the next read carries the delta again; a throw
    out of step 4 rebases nothing. `FromAsync` takes the cancellable overload, and an apply resumed after
    `Dispose` returns before step 2.
  - The subscription's `onError` reports through `ReportFailure`. `Dispose` (`:240-247`) disposes the
    subscription and the sync ahead of the chart.
  - As built after the second review: `SetCatalogueSync(PenCatalogueSync sync, IScheduler uiScheduler)`
    takes `SetChart`'s shape, a same-instance guard and the replaced sync disposed, and the apply above
    moved to `MainWindow/PenCatalogueApplier`, which step 2 calls as `ApplyCatalogue(delta.Current)`.
    `FromAsync` also takes the UI scheduler: without it an apply that awaited completed on the thread
    pool, and the delta queued behind it started there, off the UI thread.
  - `RequestPenEditorAsync` (`:220-238`) hands the `PenEditorViewModel` it builds a callback that calls the
    sync's `ReadNow()`, and does nothing on the startup-failure window, which has no sync.
- `App.InitializeServices` (`App.axaml.cs:166-194`) builds the sync from `startupData.Pens` after the
  minimap, hands it to `SetCatalogueSync`, and starts it after `coordinator.Start()`.
- The editor asks for a read after every write it lands. There is no save step: a field writes when its
  edit ends, so the write itself is the trigger, and the chart behind the open editor follows within a
  second. Every editor call already runs through `EditorCallQueue` (`PenEditor/EditorCallQueue.cs:15-21`),
  so the queue is the one place the trigger lives: it takes an `Action callSucceeded`, constrains
  `RunAsync<T>` to `T : IResultBase` (every `IPenCatalogueEditor` member returns a `Result`), and invokes
  the action on the UI thread after a call whose result succeeded; a failed or thrown call invokes nothing.
  `PenEditorViewModel` takes the callback as its last constructor parameter and builds its queue with it
  (`PenEditorViewModel.cs:41`). Refresh's own read also succeeds and asks for one more read, which costs
  one statement. A burst of writes asks for many reads, and `ReadNow` coalesces them: at most the read in
  flight and one after it.
- The editor: `PenEditorWindow.axaml:611-615` loses `PenEditorRestartNotice`, and the bottom bar
  (`:606-628`) keeps the added count and Refresh, right-aligned. The key leaves both resource sets.
- Test doubles: `FakeDataProvider` (`SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`) gets a settable
  `Pens` (`:84`) and counts catalogue reads, extent reads and live subscriptions. `MainWindowTestBuilder`
  builds the sync over it with `TestScheduler`, never `ImmediateScheduler`, which runs a periodic schedule
  by sleeping on the calling thread (CLAUDE.md, Test).

### Ending an edit with the mouse

- A field writes when its edit ends, and an edit ends when the field loses focus. A click on a label or on
  the empty area of the form takes no focus, so the edit stayed open and the operator had to press Enter or
  Tab. `PenEditorWindow.axaml.cs` adds one window-level `PointerPressed` handler, in the tunnel phase and
  with `handledEventsToo`, attached in `OnLoaded` and removed in `OnUnloaded`: when the pressed element has
  no focusable, enabled ancestor inside the window, it clears the focus, so the focused field's
  `LostFocus` (and the scale pair's `IsKeyboardFocusWithin` turning false) ends the edit through the
  existing routing. A press on a field, a button, the table or any other focusable control changes
  nothing: that control takes the focus itself. ASSUMPTION: Avalonia 12.0.5's `TopLevel.FocusManager`
  clears focus and raises `LostFocus` on the focused `TextBox`. Settled by acceptance item 25. Fallback: the
  form panel and the groups tab's side panel become `Focusable` with no visual, and the handler focuses the
  panel under the pointer instead.
- The refresh button reads "Refresh pen list" / «Обновить список перьев» (`PenEditorRefresh`); the bottom
  bar widens and its measured width in `ui-theme.md` is taken again.
- The line style stays. Simple-Scada's change-based archive writes two rows per change, the previous value
  at the last poll tick and the new value about 100 ms later (`scada-archive.md:200-202`), so an
  interpolated line over such rows already draws a step; `Stepped` differs only over those 100 ms. The
  setting shows only on a variable archived without the pair. `charting.md` (Per-pen stepping) and
  `readme.md` say so.

## What Goes Where

- **Implementation Steps**: Tasks 1 to 21 below.
- **Post-Completion**: the issue text that differs from this plan, and the SemiBase lines that still say
  the viewer registers at start.

## Implementation Steps

### Task 1: Fault kinds, the write mapping and operation-neutral wording

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveFault.cs`, `ArchiveError.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Errors/DataErrorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs`

- [x] add `ValueRejected`, `NameTaken` and `RowGone` to `ArchiveFault` with their `Describe` lines
- [x] add `MapWrite` and `RowGone` to `ArchiveExceptionMapper` and put 42883 on the `TableMissing` arm, as
      Faults states
- [x] add the three `Warning` arms to `ArchiveFailureMapper` and their nine keys
      (`FailureArchiveValueRejected*`, `FailureArchiveNameTaken*`, `FailureArchiveRowGone*`, each `Title`,
      `Detail`, `Remedy`) to both resource sets, following
      `docs/architecture/ui-text.md#what-the-mappers-two-consumers-read`
- [x] reword the eleven keys of the Faults table and the three `Describe` lines; add `MenuEditPensAndGroups`
      ("Pens and groups", "Параметры и группы") and `PenEditorRefresh` ("Refresh", "Обновить") to both sets
- [x] read `EmptyCatalogueMessage` in `TrendChartView.axaml:16-17` through the `MultiBinding` The empty
      chart names the way in describes
- [x] tests: `ArchiveExceptionMapperTests` for 23514, 23505, 23503,
      `AnUndefinedFunctionMapsToTableMissingCarryingTheFunction`, `RowGone` carrying the subject and the
      endpoint, and an unlisted SQLSTATE through `MapWrite` giving `ReadFailed`; `ArchiveFailureMapperTests`
      for the three arms; `FailureSeverityTests` rows for the three kinds and the third bucket "the
      operator's own edit refused" at `Warning`; `DataErrorTests` one `InlineData` per new kind;
      `TrendChartViewTests.cs:164` asserts acceptance item 11
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 2: The write interface and its Postgres implementation

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/IPenCatalogueEditor.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/PenCatalogue.cs` (`PenCatalogue`, `StoredPen`, `StoredGroup`)
- Create: `SemiPlot/SemiPlot.Core/Data/PenSettingChange.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresPenCatalogueEditor.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/StoredLineStyle.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveFailureLog.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/PenCatalogueEditorTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/PenRegistrationTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Data/PenCatalogueEditorSurfaceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`

- [x] declare `IPenCatalogueEditor`, `PenCatalogue.cs` and `PenSettingChange.cs` as Technical Details states
- [x] move `PostgresDataProvider.ReadLineStyle` (`:509-524`, with its comment) to
      `internal static class StoredLineStyle` as `Read(short storedValue, int penId, ILogger logger)`;
      `ReadPen` (`:453`) calls it with `_logger`
- [x] move the empty-detail log of `PostgresDataProvider.Map` (`:538-549`) into `ArchiveFailureLog`
- [x] add the Statements section's constants to `ArchiveStatements`
- [x] implement `PostgresPenCatalogueEditor` (internal constructor over `NpgsqlDataSource`,
      `ArchiveExceptionMapper` and `ILogger<PostgresPenCatalogueEditor>`): `ReadAsync` over the two stored
      statements, registration mapped through `Map` with `RegisterNewPensFunction`, one fixed statement per
      change arm, rows-affected checked on every update, rename and delete, every write failure through
      `MapWrite` with its subject, every failure through `ArchiveFailureLog.LogIfUnexpected`
- [x] register it in `AddPostgresData()` beside `IDataProvider`, by factory; add
      `typeof(IPenCatalogueEditor)` to `PostgresCompositionTests.AddPostgresDataRegistersASingleton` and a
      `CompositionRootTests.Container_ResolvesThePenCatalogueEditor`
- [x] write `PenRegistrationTests` (acceptance item 1), `PenCatalogueEditorTests` (items 3 to 6) and
      `PenCatalogueEditorSurfaceTests` (item 5) on the harness the Testing Strategy names; item 1.3 settles
      the registration ASSUMPTION: the waiting call returns 0 with no fault, so no advisory lock is needed
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 3: The pen form view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/EditorCallQueue.cs`
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenRowViewModel.cs`
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenFormViewModel.cs` (with `PenField`)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/FakePenCatalogueEditor.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenFormViewModelTests.cs`

- [x] build `EditorCallQueue`, `PenRowViewModel` and `PenFormViewModel` as Technical Details describes
- [x] write `FakePenCatalogueEditor`: records every call in order, answers each with a configurable result,
      and holds any call on a gate the test completes
- [x] add the form's validation messages to both resource sets
- [x] write the tests of acceptance item 7, in `ProcessGlobalStateCollection`
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 4: The pen table view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs` (with `PenColumn`)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewModelTests.cs`

- [x] build `PenEditorViewModel` without the groups tab: the queue it owns, rows, sort that keeps the row
      instances, selection, the derived form, refresh through the queue with the added count,
      `WhenIdleAsync`, `ReportFailure`, `Dispose`
- [x] add the added-count text to both resource sets
- [x] write the tests of acceptance item 9
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 5: The groups view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupsViewModel.cs` (with `PenGroupViewModel` and
  `PenMembershipViewModel`)
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewModelTests.cs`

- [x] build `PenGroupsViewModel` as Technical Details describes, every call through the queue
- [x] update the affected rows' groups text after every successful membership change, rename and delete
- [x] hang it off `PenEditorViewModel.Groups`, rebuild it on refresh keeping the selected group by id, and
      dispose it from `PenEditorViewModel.Dispose`
- [x] add the confirmation text (takes the group name and the member count) and the group messages to both
      resource sets
- [x] write the tests of acceptance item 8
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 6: One headless input helper

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`

- [x] extract `SettingsViewTests`' `Click`, `Clear` and `Type` (`:338-365`) into
      `internal static class HeadlessInput` over `TopLevel`, with `Press(TopLevel, PhysicalKey)` replacing
      `Commit` (`:331-336`) and serving Tab, Enter and Escape
- [x] point every caller in `SettingsViewTests`, `MainWindowViewTests` (`:365-373`) and
      `TrendLegendViewTests` (`:419-427`) at it and delete the three copies;
      `git grep -nE "static void (Click|Clear|Type)\(" -- SemiPlot/SemiPlot.Tests.Unit` prints only
      `HeadlessInput.cs` lines
- [x] run `dotnet test SemiPlot.slnx` - green before the next task, with the unit passed count unchanged
      (unit 1264 passed before and after, integration 130)

### Task 7: The window shell, the table and the menu entry

**Files:**
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`, `.axaml.cs`
- Create: `SemiPlot/SemiPlot.UI/PenEditor/PenColorConverters.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`, `MainWindow.axaml.cs`,
  `AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`, `MainWindowViewTests.cs`,
  `MainWindowViewModelTests.cs`, `MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/InitializeServicesTests.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/EmptyCatalogueStartupTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] give `MainWindowViewModel` the trailing `IPenCatalogueEditor? penCatalogueEditor = null`,
      `ShowPenEditorCommand` and `PenEditorRequests` as Solution Overview states; `AddUi`
      (`UiServiceCollectionExtensions.cs:18-22`) passes `GetRequiredService<IPenCatalogueEditor>()`;
      `MainWindow.axaml.cs` wires `PenEditorRequests` as it wires `SettingsRequests` (`:35`, `:82-96`)
- [x] add `EditPensAndGroups` as the second child of `EditMenu`, bound to `ShowPenEditorCommand`
- [x] build the window shell: the `TabControl` with `Pens` and `Groups`, the bottom bar with the restart
      notice, the refresh button and the added count; the `Pens` tab's header row and virtualised table
      with the "on start" column and the colour swatch through `PenColorConverters.ToBrush`; the form area
      laid out at its fixed height, empty until Task 8 fills it (the line-style cell reads
      `PenLineStyleConverters.ToLabel`; `PenColorConverters` holds `ToBrush` only, Task 8 adds `ToColor`)
- [x] run the marker probe for each control the tree gains (`TabControl`, `ListBox`) and add them to the
      `ThemeTests` probes (`ui-theme.md:66-79`, `:104`): the probe found ten keys whose Semi value is off
      the palette, now in `Palette.axaml` under both variants (`TabItemLineHeader*Foreground`,
      `TabItemLinePipeSelectedBackground`, `TabControlSeparatorBorderBrush`, `ListBoxItem*Foreground`,
      `ListBoxItemSelected*Background` at 0.25) and gated by
      `ThemeTests.EveryTabAndListSurface_PaintsItselfFromThePalette`; Task 11 adds them to `ui-theme.md`
- [x] update the tests the new parameter and item touch: `AppMenuBarTests` `:29-51` (the leaf list) and
      `:53-65` (`ContainSingle` becomes two items in order); `MainWindowTestBuilder.NewViewModel`
      (`:28-37`) takes an optional editor; `InitializeServicesTests.cs:94-105` and
      `EmptyCatalogueStartupTests.cs:81-92` register `FakePenCatalogueEditor`;
      `CompositionRootTests.cs:59-65` now constructs the Postgres editor through `MainWindowViewModel` and
      needs no change; the direct constructions at `MainWindowViewTests.cs:134`,
      `MainWindowViewModelTests.cs:72` and `:104` and `App.axaml.cs:73` leave the parameter out
- [x] add `MainWindowViewModelTests` for the command's availability and its emitted view model, and
      `MainWindowViewTests` for the disabled item on the startup-failure window and the opening click
      (acceptance item 10)
- [x] write `PenEditorViewTests` in `ProcessGlobalStateCollection` for acceptance items 12.1, 12.2
      (selection half), 12.9 and 12.10; 12.9 settled the `ListBox` ASSUMPTION against it, so the
      fallback bracket is built
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 8: The pen form in the window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`, `.axaml.cs`
- Modify: `SemiPlot/Directory.Packages.props`, `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`,
  `SemiPlot/SemiPlot.UI/App.axaml` (only if the picker probe passes)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] probe the colour-picker ASSUMPTION first: add both packages, realise a `ColorPicker` headless, reach
      its flyout's `Opened` and `Closed`, pick in row A, select row B, and read B's colour off the picker;
      keep the picker if all hold, otherwise build the swatch-row fallback and take the packages out
      again; record the outcome in this plan. Outcome: all three hold, the picker is kept. The Semi
      template is a `DropDownButton` whose `Flyout` is a `Flyout` the code-behind reaches through the
      visual tree; it opens in the window's overlay layer headless, so its `TopLevel` is the window; its
      content is a `TabControl` of spectrum, palette and components; a swatch click sets `Color` and
      leaves the one-way binding in place (`AfterAPickInOneRow_ThePickerShowsTheNextRowsColour`)
- [x] lay out the form: label column, name, unit, mask with its preview, colour text box and picker, line
      style combo, "on start" checkbox, the two scale `TextBox` fields in one container; every text field
      binds `Classes.invalid` to its `Is*Valid`; one `TextBlock.form-message` bound to `Message`
- [x] route every edit end and the `Closing` drain from code-behind as The window's code-behind states
- [x] measure the opening size and the column widths in both languages as Window layout and size states:
      Skia and HarfBuzz, 2026-09-28. The widest headers are "При запуске" 101 px and "Шкала до" 82 px, the
      widest line style "Ступенчатая" 69 px; each fixed column is at least 1.1 times its widest header or
      fixed-vocabulary cell plus the 12 px cell margin, so the widths are 72, 112, 68, 220, 96, 96, 104,
      104, 104 and the groups column takes the rest (180 px at the opening width). At 1180 x 720 the
      table shows 12 rows, the form's natural size is 597 x 195 px in English and 635 x 195 px in Russian
      inside its 1156 x 232 px area, the longest form message is 379 px on a 1156 px line, and the bottom
      bar needs 394 and 544 px; the opening size stays 1180 x 720
- [x] add the picker (or the swatch row) to the `ThemeTests` probes: the marker probe over the realised
      form (disabled, enabled, the line-style list open and hovered, the picker flyout on each tab) found
      nineteen keys off the palette, now in `Palette.axaml` under both variants and gated by
      `ThemeTests.EveryComboBoxSurface_PaintsItselfFromThePalette` and
      `EveryColourPickerSurface_PaintsItselfFromThePalette`; Task 11 adds them to `ui-theme.md`
- [x] write `PenEditorViewTests` for acceptance items 12.2 (form half), 12.3 to 12.8 and 12.15 to 12.19
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 9: The groups tab in the window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`, `.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenGroupsViewModel.cs` (the selection bracket)
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] lay out the tab: the new-name field and create button, the group list, the one rename field bound to
      `SelectedGroup.RenameDraft` with `Classes.invalid`, the delete button, the reserved confirmation row,
      the reserved message line, and the virtualised membership checkbox list reading `IsMember` one way
      with each row's `ToggleMembershipCommand`: a 320 px side panel left of the membership list, the
      confirmation row (32 px) and the message line across the tab's foot; the membership list is an
      `ItemsControl` in a `ScrollViewer`, so a click on a pen selects no row. Six new keys
      (`PenGroupsNewNamePlaceholder`, `PenGroupsCreate`, `PenGroupsNameLabel`, `PenGroupsDelete`, the
      confirm button's caption too, `PenGroupsCancel`, `PenGroupsMembersHeader`). The marker probe over the
      tab's controls found seven `CheckBox` keys off the palette, now under both variants and gated by
      `ThemeTests.EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette`: the accent for
      `CheckBoxCheckedPointerover*`, `CheckBoxCheckedPressed*` and `CheckBoxPressedBorderBrush`, the
      disabled grey for `CheckBoxCheckedDisabled*` (a box whose write runs); Task 11 adds them to
      `ui-theme.md`, and `TextBoxPlaceholderForeground` now has a consumer, the new-name field
- [x] route the rename field's edit end to the recorded `PenGroupViewModel.EndRenameAsync()`
- [x] write `PenEditorViewTests` for acceptance items 12.11 to 12.14, plus a create through the view and a
      delete confirmed after another group was selected, which found the group list's null write-back
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 10: Verify acceptance criteria

- [x] run acceptance items 1 to 14 and record each passed count
      ! measured 2026-09-28 after the review fixes, every filter with 0 failed: item 1
      `PenRegistrationTests` 4 passed (minimum 4); item 2 both greps as stated (only
      `PenEditor/PenEditorViewModel.cs`, then nothing) and
      `AnUndefinedFunctionMapsToTableMissingCarryingTheFunction` 1 passed; items 3 to 6
      `PenCatalogueEditorTests` 30 passed (minimum 12); item 5 `PenCatalogueEditorSurfaceTests` 1 passed;
      item 7 `PenFormViewModelTests` 45 passed (minimum 12); item 8 `PenGroupsViewModelTests` 21 passed
      (minimum 6); item 9 `PenEditorViewModelTests` 27 passed (minimum 5); item 10 the three-class filter
      40 passed (minimum 39); item 11 1 passed; item 12 `PenEditorViewTests|PenGroupsViewTests` 39 passed (minimum 19);
      item 13 the `Chart`/`Legend` grep prints nothing and both counts print 0; item 14
      `dotnet test SemiPlot.slnx` unit 1338 passed, integration 131 passed, 0 failed in both (counts
      after the second review round, which moved the queued writes to the row; unchanged after the smells
      round, which split the view tests by tab)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
      ! measured 2026-09-28: `dotnet format` exit 0 with no changes; `dotnet terse` over the 49 `.cs` files
      `git diff --name-only --diff-filter=d 44811da..HEAD` lists exit 0; after the review fixes, again
      exit 0 for both, `dotnet terse` over the 22 `.cs` files the fixes touched
- [x] run the colour-literal grep in `ui-theme.md#a-colour-literal-in-axaml-is-a-defect`; it prints nothing
      ! measured 2026-09-28: prints nothing, exit 1

### Task 11: Update documentation

- [x] `docs/architecture/data-integration.md`: the write interface, its statements, its faults and
      `MapWrite`, the stored read, the operation-neutral wording, registration from the refresh button only
      with its reasons, and why the read interface stays read-only
- [x] `docs/architecture/postgres-instance.md:62-63` and `postgres-topology.md:39` (the `SELECT only` edge
      to `config`) and `:49-55`: the column-level grant, registration from the refresh button, and the
      editor that now exists
- [x] `docs/architecture/overview.md`: the editor window beside `#the-settings-window`: the two tabs, where
      its view model is built, when its command can run, the serial queue, the restart model
- [x] `docs/architecture/ui-theme.md`: under `#a-form-never-resizes-on-validation`, the resizable-window
      rule Window layout and size states, the editor's measured size, and the controls it adds
      ! the groups side panel (320 px) and the confirmation row (32 px) are recorded as chosen, not
      measured, as Task 9 left them
- [x] `docs/architecture/ui-text.md`: the editor's text, the three new failure messages, the reworded
      keys, and the empty-catalogue format
- [x] `docs/architecture/charting.md:281-282`: the pen editor now writes `semiplot_tags`
- [x] `readme.md`: the editor in the feature list, and that new SCADA variables appear as hidden pens after
      Refresh in the editor
- [x] `CLAUDE.md`: the write path's home, the grep that keeps it out of `Chart/` and `Legend/`, the grep
      that keeps `RegisterNewPensAsync` in `PenEditor/`, `HeadlessInput` as the one input helper, and the
      sidebar bullet's pointer to the editor. The startup statement copies the Constraints wording: the
      container constructs the editor with `MainWindowViewModel`, and no startup code calls it

### Task 12: Pen value equality, the list delta and a real read

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/Pen.cs`
- Create: `SemiPlot/SemiPlot.Core/Trends/PenListDelta.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/PenListDeltaTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/LiveCatalogueTests.cs`

- [x] give `Pen` the value equality The live catalogue states, with a `GetHashCode` that agrees with it
- [x] create `PenListDelta.Between` with `PenRevision`, `IsEmpty` and `ChangesPenSet`
- [x] order `PenCatalog` by `tag.name, tag.id`
- [x] warn once per pen, column and stored value in the provider's normalisation, a repeat at `Debug`
- [x] write `PenListDeltaTests` for acceptance item 17, plus an empty current list (every pen removed)
- [x] write `LiveCatalogueTests` for acceptance item 17 on `ClonedArchiveTest(fixture,
      CloneSource.Provisioned)`, seeding through `TagCatalogWriter` as `PenCatalogueEditorTests` does
- [x] run `dotnet test SemiPlot.slnx` - green before the next task
- [x] + second review fix: `StoredLineStyle.Read` takes the log level as a function and is the one read of
      `line_style` for both callers; the warned set keys on the column ordinal; `LiveCatalogueTests` builds
      its statements from the id constants and records the log through `FakeLoggerProvider`
      (`Microsoft.Extensions.Diagnostics.Testing`, nuget.org)

### Task 13: The coordinator follows the pen set

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`

- [x] replace the fixed id array with the `BehaviorSubject<int[]>` and `Switch` chain and add `SetPens`;
      `Dispose` completes the subject
- [x] make `FakeDataProvider.Pens` settable and count catalogue reads, extent reads and live subscriptions
- [x] write the `TrendCoordinatorTests` of acceptance item 18 over `TestScheduler`
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 14: The chart applies a delta

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Create (second review): `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartCatalogueTests.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/RenderedRise.cs`; move `UI/Legend/LegendChartBuilder.cs` to
  `UI/Chart/ChartTestBuilder.cs`

- [x] add `EnvelopeLine.Restyle` with both reads in `Render` under `_columnsLock`, and `TrendPenState.Revise`
- [x] make `Pens` the ordered list and walk it for the active-pen fallback; split `AddPen` and `RemovePen`
      into cores; add `ApplyCatalogue` as The live catalogue states
- [x] write the `TrendChartViewModelTests` of acceptance item 19
- [x] run `dotnet test SemiPlot.slnx` - green before the next task
- [x] + deviation: `EnvelopeLine` lost its `Color` and `PenLineStyle` setters and the unused `LineWidth`,
      so `Restyle` is the one writer of both
- [x] + review fix: the plot and axis edits of `ApplyCatalogue` and `AddPen` run under `lock (Plot.Sync)`
      (`TrendChartRenderThreadTests`); `RemovePen` is deleted, having no production caller; `ApplyHistory`
      skips a removed pen's envelope; a delta removing every pen clears the scales
- [x] + second review fix: `ChartPenSet` holds the pens; `ApplyCatalogue` takes the read and compares it
      with the pens shown; the live-edge switch and the history query leave the lock; `AddPen` is deleted
      and the start seeds through `ApplyCatalogue`; `TrendPenState.Pen` raises; `EnvelopeLine`'s test-only
      getters are deleted; the catalogue tests moved to `TrendChartCatalogueTests`
- [x] + fourth review fix: the chart compares the pens shown with `TrendCoordinator.PenIds`, so the start
      opens one live subscription

### Task 15: The sidebar rebuilds on demand

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`

- [x] add `Rebuild()`: new rows and groups from the chart's `Pens`, `Groups` assigned and raised, then the
      replaced ones disposed
- [x] write the tests of acceptance item 20; the realised one applies a delta to the chart the shown
      sidebar was built from and calls `Rebuild()`
- [x] run `dotnet test SemiPlot.slnx` - green before the next task
- [x] + `TrendLegendRowViewModel`'s visibility setter stops reaching the chart once the row is disposed:
      without it a write on a replaced row still switched the pen, which item 20 forbids

### Task 16: The catalogue read loop and its wiring

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/EditorCallQueue.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/PenCatalogueSyncTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/EditorCallQueueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Create (second review): `SemiPlot/SemiPlot.UI/MainWindow/PenCatalogueApplier.cs`

- [x] create `PenCatalogueSync` with the contract The live catalogue states
- [x] make `MinimapViewModel.LoadExtentAsync` return the result it applied
- [x] add `SetCatalogueSync` and `ApplyCatalogueAsync` to `MainWindowViewModel`, and hand the editor it
      builds the read callback
- [x] give `EditorCallQueue` the success callback and `PenEditorViewModel` the constructor parameter that
      feeds it
- [x] build and start the sync in `App.InitializeServices`; build it in `MainWindowTestBuilder` over
      `TestScheduler`
- [x] write `PenCatalogueSyncTests` for acceptance item 21, the `MainWindowViewModelTests` of item 22, and in
      `EditorCallQueueTests` the callback's three cases: once after a successful call, never after a failed
      one, never after a thrown one
- [x] run `dotnet test SemiPlot.slnx` - green before the next task
- [x] + deviation: the item 22 tests went into `MainWindowViewModelTests`, not `MainWindowViewTests`, which
      this task left untouched
- [x] + review fix: an apply that throws before the sidebar rebuilt rebases the sync
      (`PenCatalogueSync.Rebase`); an apply resumed after `Dispose` returns; one `TaskCompletionSource`
      carries the loop's wake-up; `InitializeServicesTests` pins the wiring of the loop
- [x] + second review fix: the apply moved to `PenCatalogueApplier`, completing on the UI scheduler;
      `SetCatalogueSync` follows `SetChart`'s shape; the failure threshold is one Core constant
- [x] + fourth review fix: the extent reload after a delta that adds a pen widens the navigation's first
      sample (`ChartNavigationController.WidenToArchiveExtent`)

### Task 17: The editor and the empty chart drop the restart

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`

- [x] remove `PenEditorRestartNotice` from the bottom bar and from both resource sets; right-align the
      added count and Refresh, and measure the bottom bar's width in both languages for Task 19
      (measured headless with a three-digit count: 346 px in English, 210 px count + 12 + 124 px Refresh;
      444 px in Russian, 294 + 12 + 138 px)
- [x] reword `EmptyCatalogueMessage` in both sets as the wording table states
- [x] rewrite `TheRestartNotice_StandsOnBothTabsAndTheVisibilityColumnReadsOnStart` to assert acceptance
      item 12.1 (now `TheBottomBar_StandsOnBothTabsRightAlignedAndTheVisibilityColumnReadsOnStart`)
- [x] run `dotnet test SemiPlot.slnx` - green before the next task

### Task 18: Verify acceptance criteria

- [x] run acceptance items 1 to 14 and 17 to 22 and record each passed count
      ! measured 2026-09-28 on `9fc83e9`, every filter with 0 failed: item 1 `PenRegistrationTests` 4 passed
      (minimum 4); item 2 both greps as stated (only `PenEditor/PenEditorViewModel.cs`, then nothing) and
      `AnUndefinedFunctionMapsToTableMissingCarryingTheFunction` 1 passed; items 3 to 6
      `PenCatalogueEditorTests` 30 passed (minimum 12); item 5 `PenCatalogueEditorSurfaceTests` 1 passed;
      item 7 `PenFormViewModelTests` 45 passed (minimum 12); item 8 `PenGroupsViewModelTests` 21 passed
      (minimum 6); item 9 `PenEditorViewModelTests` 27 passed (minimum 5); item 10 the three-class filter
      45 passed (minimum 39); item 11 1 passed; item 12 `PenEditorViewTests|PenGroupsViewTests` 39 passed
      (minimum 19); item 13 the `Chart`/`Legend` grep prints nothing and both counts print 0; item 14
      `dotnet test SemiPlot.slnx` unit 1392 passed, integration 135 passed, 0 failed in both; item 17
      `PenListDeltaTests` 13 passed (minimum 8) and `LiveCatalogueTests` 4 passed (minimum 4); item 18
      `TrendCoordinatorTests` 16 passed (13 listed at `7099271`, minimum 16); item 19
      `TrendChartViewModelTests` 93 passed (80 at `7099271`, minimum 91); item 20
      `TrendLegendViewModelTests|TrendLegendViewTests` 50 passed (44 at `7099271`, minimum 48); item 21
      `PenCatalogueSyncTests` 11 passed (minimum 9); item 22 `MainWindowView` 31 passed (26 at `7099271`,
      minimum 30). The `7099271` baselines were re-listed with `--list-tests` on a build of that commit
- [x] walk the manual items 15 and 16 on the stand: item 16 after item 15's steps 1 to 12 and 14, and step
      15.13 last, because it drops the function item 16's Refresh calls
      (not automatable: left to the operator's stand walk, it needs a person at the window)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
      ! measured 2026-09-28: `dotnet format` exit 0 with no changes; `dotnet terse` over the 35 `.cs` files
      `git diff --name-only --diff-filter=d 7099271..HEAD` lists exit 0
- [x] run the colour-literal grep in `ui-theme.md#a-colour-literal-in-axaml-is-a-defect`; it prints nothing
      ! measured 2026-09-28: prints nothing, exit 1
- [x] update the counts in Verify it yourself

### Task 19: Update documentation

- [x] `docs/architecture/overview.md`: `#### The restart model`, the bottom-bar row of the editor's layout
      table and the line that says what the editor writes reaches the chart at the next start become the
      live catalogue: the 5 s read, the read after each editor write, what a read changes and what it
      leaves, the failure threshold, the editor's own snapshot, why nothing is pushed and why the chart is
      not rebuilt whole
- [x] `docs/architecture/data-integration.md`: `### The restart model`, the `DeleteGroupAsync` row and the
      line that says a registered pen draws once the viewer restarted; the coordinator's switch over the
      pen set and the rows a switch leaves to the history query; the once-per-value normalisation warning
- [x] `docs/architecture/charting.md`: a stored scale change replaces the session axis of that pen, the
      chart's update path for a pen, and navigation seeded from the extent when an empty chart gets pens
- [x] `docs/architecture/ui-theme.md`: the bottom bar without the notice and its measured width;
      `docs/architecture/postgres-topology.md` and `docs/architecture/ui-text.md`: the catalogue read, the
      removed notice and the empty-catalogue text
- [x] `CLAUDE.md`: one UI bullet naming `Bridge/PenCatalogueSync` as the one route a stored change takes
      into the running chart, with a pointer to `overview.md`; `readme.md`: edits reach every running
      viewer without a restart
- [x] run acceptance item 23's grep; it prints exactly the lines it names
      ! measured 2026-09-28: 10 lines, the five settings-window lines (`CLAUDE.md:35`, `overview.md:214`
      and `:260`, `ui-theme.md:185`, `SettingsViewModel.cs:195`) and the five directory-sweep lines
      (`CLAUDE.md:101`, `bench.md:267`, `:285`, `:290`, `DemoDirectories.cs:58`)

### Task 20: Name the refresh button and record the line style's effect

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/ui-theme.md`, `docs/architecture/ui-text.md`,
  `readme.md`

- [x] set `PenEditorRefresh` to "Refresh pen list" and «Обновить список перьев»
- [x] measure the bottom bar again in both languages and record it in `ui-theme.md`; `ui-text.md` names
      the new label
      ! measured headless 2026-09-29 with a three-digit count: 472 px in English (210 + 12 + 250 px) and
      640 px in Russian (294 + 12 + 334 px); `readme.md` names the button by its new label too
- [x] state in `charting.md` (Per-pen stepping) and `readme.md` what the line style changes on a
      change-based archive, as Ending an edit with the mouse states
- [x] extend the bottom-bar view test for acceptance item 24
      ! item 24's filter 29 passed, 0 failed
- [x] run `dotnet test SemiPlot.slnx` - green before the next task
      ! unit 1420 passed, integration 136 passed, 0 failed in both

### Task 21: A click on empty space ends the edit

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenGroupsViewTests.cs`
- Modify: `docs/architecture/overview.md` (the editor's edit-end routing)

- [x] add the window-level press handler as Ending an edit with the mouse states, or its fallback
      ! ASSUMPTION probed 2026-09-29 and holds: with no handler the four item-25 tests failed (the field
      kept the focus); `FocusManager.Focus(null)` on the window clears the focus and raises the text box's
      `LostFocus` and the scale pair's focus-within change, so all four pass and no fallback was built
- [x] write the tests of acceptance item 25
      ! three in `PenEditorViewTests` (a label click, the empty message line, no edit in progress), one in
      `PenGroupsViewTests` (the tab's empty message line)
- [x] add to manual item 15 a step: type a scale minimum and maximum, click the empty area of the form,
      and the axis changes within a second
      ! step 15.15; `overview.md`, `data-integration.md`, `postgres-instance.md` and `postgres-topology.md`
      now name the button `Refresh pen list`
- [x] record in `overview.md` how an edit ends: Tab, Enter, a click on another control or on empty space
- [x] run acceptance items 24 and 25, `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx
      --verify-no-changes` and `dotnet terse` over the touched files
      ! item 24's filter 32 passed; item 25's filter 43 passed (39 at `dc0fc4d`); unit 1424 passed,
      integration 136 passed, 0 failed in both; format and terse exit 0

## Post-Completion

- Ship moves this plan to `docs/plans/completed/` after the operator has walked the stand.
- `Semiteq/SemiPlot#67` still describes opening the editor from a sidebar row and saving over a second
  `semiplot_writer` connection. This plan replaces both: the menu entry and the one `semiplot` role. The
  PR body names both so the issue closes with the difference on record.
- Logarithmic pen scale, as its own patch after this delivery: a SemiBase release adds the stored flag and
  its column grant, the catalogue read carries it into `PenScaleSettings.IsLogarithmic`, the pen form gains
  the checkbox, and the chart draws a logarithmic axis. The live catalogue carries it with no change of its
  own.
- SemiBase follow-up in the same delivery: `docs/architecture/provisioning.md:256-257` changes "The viewer
  calls it at start and from its pen editor's refresh button" to "The viewer calls it from its pen
  editor's refresh button", and `:312` changes "at every viewer start and refresh" to "at every refresh".

**Executed by exec:**
- branch: pen-and-group-editor

## Verify it yourself

Automated, from the repository root:

```powershell
dotnet test SemiPlot.slnx
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenFormViewModelTests|FullyQualifiedName~PenGroupsViewModelTests|FullyQualifiedName~PenEditorViewModelTests|FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~PenGroupsViewTests"
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenListDeltaTests|FullyQualifiedName~TrendCoordinatorTests|FullyQualifiedName~TrendChartCatalogueTests|FullyQualifiedName~TrendChartRenderThreadTests|FullyQualifiedName~TrendLegendViewModelTests|FullyQualifiedName~PenCatalogueSyncTests|FullyQualifiedName~MainWindowViewModelTests"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~LiveCatalogueTests"
```

The first prints 1424 unit and 136 integration; the second 136 (45 + 21 + 27 + 31 + 12); the fourth 5. The
queued-edit regressions of the editor (a field toggled back while its write is held, a failed write followed
by a queued write of the same field, a form rebuilt while its row's write is held, a membership toggle landing
after the group was selected again) each have a test that failed on the code before `5678c23`/`b6e9f03`.

The live catalogue's review regressions each have a test that failed on the code before its fix:

- `TrendChartRenderThreadTests`: rasterising on a second thread while deltas add and remove pens threw
  "Collection was modified" at frame 1 before `7ca9144` (`lock (Plot.Sync)`), and the live-edge switch ran
  with the lock held before `88516f5`.
- `PenListDeltaTests`: the one-row-per-member theory failed on the `Unit` row with `Unit` dropped from
  `Pen.Equals`.
- `MainWindowViewModelTests.AnApplyThatThrowsWithTheNextDeltaQueued_StillTakesALaterRevisionOfThatDeltasPen`
  lost pen 1 and the later revision before `88516f5`.
- `MainWindowViewModelTests.ADeltaAddingAPenWithOlderRows_LetsTheChartPanBackToThem` and
  `TrendChartCatalogueTests.TheStartCatalogue_OpensOneLiveSubscription` failed before `98b9066`.

What cannot be shown by a test is the operator's view of it, on the stand: acceptance items 15 (steps 1-15)
and 16 (steps 1-7) above, in the order Task 18 names. Before this branch, a colour picked in the editor
showed only after a restart; after it, the chart line behind the open editor changes within a second
(15.3), and a second viewer shows it within 5 s (15.12).
