# Time markers stored in the archive

## Overview

- An operator places a vertical line at a moment (the start of a process, an event worth returning to),
  drags it to another time, renames or deletes it from a right-click menu, and finds it again after a
  restart and in every other open window (Semiteq/SemiPlot#77).
- A marker is one row of a new SemiBase table `semiplot_markers`: an identity id, a `timestamptz` moment and
  an optional text label of at most 80 characters. The viewer reads and writes it as the one `semiplot` role
  over the one `NpgsqlDataSource`, like the pen catalogue editor
  (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:32,46-49`).
- Every running window re-reads the markers every 5 s and at once after its own write lands, the way
  `Bridge/PenCatalogueSync` keeps the pen catalogue live (`SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs:27,50-53`).
- Pen values at a marker: every marker inside the window shows the active pen's value at its moment, a dot on the pen's
  line and the number beside it; the other pens show none there. With the pointer on a marker, the cursor snaps to the
  marker's moment and the existing hover readout lists every visible pen's value there
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:559-603`).
- Count: no cap. Every window reads every marker every 5 s, draws every marker inside the time window, and hides a
  label that would overlap its neighbour's.
- A double click in the data area, or "Add marker here", adds a marker labelled with the next number; a double click on
  a marker deletes it. `Rename` changes the label, and an empty label leaves the marker unlabelled. The line's label
  shows the label and the marker's local time.
- Every marker draws in one theme colour, the same for all markers; a marker carries no colour of its own.
- A marker is an annotation. A marker list and bookmark navigation (CU-4 previous/next with centring,
  `docs/architecture/trend-feature-spec.md:152-154`) are one separate issue, not part of this plan: here a marker is
  found on the chart and as a tick on the minimap strip.

## Context (from discovery)

Line numbers are read at `127e3e4` (`feat(chart): add a time window panel on the time axis (#71) (#129)`). Each task
re-reads the files it changes before editing; a member name in this plan outranks a line number that has drifted.

- SemiBase (`C:\Users\admin\projects\SemiBase`, Go) provisions every `semiplot_*` table. `semiplotSchemaFiles`
  lists the DDL files in apply order (`internal/provision/schema.go:12-20`), `semiplotTables` the grant each
  table gets (`schema.go:42-50`), and `plotWriteGrants` is `SELECT, INSERT, UPDATE, DELETE` (`schema.go:36`).
  `TestSemiplotGrantStatements` compares the statements by index (`internal/provision/schema_test.go:152-175`).
  The write probes run as `semiplot` inside a transaction that rolls back (`internal/provision/check.go:225-244`),
  from the list `plotWriteProbes` (`check.go:26-45`), through `assertPlotWritesItsTables` (`check.go:284-293`).
  The DDL files are embedded through `embed.go`.
- The schema version stays `1`: no site runs a provisioned database, so a schema change edits the `CREATE TABLE`
  alone (`SemiBase/docs/architecture/provisioning.md:321-323`). The sentence at `provisioning.md:325-326` that
  promises a bump to 2 for `semiplot_markers` contradicts that rule and is rewritten. SemiPlot reads no
  `schema_version` (a grep over `SemiPlot/` for `schema_version|SchemaVersion` is empty).
- SemiBase counts its tables or lists the `semiplot` write surface in code and docs alike:
  `git grep -nE "\bfour\b|четыре" -- "*.md" "*.go"` prints 20 lines outside `docs/plans`, among them the `--help` text
  `cmd/semibase/main.go:28`, `README.md:33`, `CLAUDE.md:9,99,118,162`, `docs/architecture/README.md:11`,
  `docs/architecture/overview.md:22,26,37,56`, `docs/architecture/provisioning.md:92,173,243` and
  `docs/deployment.md:34,96,160`; `provisioning.md:226,275` count registrar columns and `deployment.md:59` counts roles.
  The grants are listed in `CLAUDE.md:119-127`, `docs/architecture/README.md:24,27`, `overview.md:19`,
  `provisioning.md:10-12,176,230-241,428-429` and `deployment.md:65,97,105`. The risk statement `overview.md:29-31` says
  a viewer bug can corrupt only the pen catalogue.
- The bench image copies `/semibase` out of `ghcr.io/semiteq/semibase:latest` (`SemiPlot/bench/Dockerfile:3`), so
  SemiPlot's container tests see the new table only after a SemiBase release tag moves `:latest`.
- Write path precedent: `PostgresPenCatalogueEditor` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresPenCatalogueEditor.cs`).
  An update or delete touching no row returns `ArchiveFault.RowGone` (`:194-214`).
- Error contract gaps the markers hit:
  - `ArchiveExceptionMapper.MapWrite` sends every non-constraint failure to
    `Map(exception, ArchiveStatements.PenCatalogRelations)` (`ArchiveExceptionMapper.cs:37-46`). A marker write
    against a missing table would name the pen tables. Besides the editor (`PostgresPenCatalogueEditor.cs:282-285`),
    `PenCatalogueEditorTests.cs:330` and the helper `ArchiveExceptionMapperTests.cs:244-246` call it.
  - The `RowGone` wording is catalogue-specific: "Read the catalogue again..."
    (`SemiPlot/SemiPlot.UI/Localization/Resources.resx:384-392`). It is mapped in
    `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs:212-216`, the message text is in
    `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveError.cs:42`, and the enum arm is at `ArchiveFault.cs:37`.
  - `FailureSeverityTests.ArchiveFaultTable_CoversEveryMemberOfTheEnum`
    (`SemiPlot/SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs:116-119`) fails on a new enum member that is
    missing from its table (`:21-34`).
- Sync precedent: `PenCatalogueSync` runs a loop on the UI scheduler (`PenCatalogueSync.cs:44-47,67-101`), counts
  consecutive failures, and reports from the third (`:119,129-144`). Its warning names the pen catalogue (`:136`).
  Twelve plain `[Fact]`s pin its timing, because a plain fact has no `SynchronizationContext` and the loop resumes
  inline (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/PenCatalogueSyncTests.cs:14-17,45-237`).
- Serialised writes precedent: `PenEditor/EditorCallQueue` runs calls one after another in the order issued, on the UI
  thread with no lock, and calls a success callback inside the queued call, before the awaiting caller resumes
  (`SemiPlot/SemiPlot.UI/PenEditor/EditorCallQueue.cs:9-64`). `EditorCallQueueTests` pin it with plain facts.
  `git grep -n EditorCallQueue` outside `docs/plans` prints the type, its tests, `PenFormViewModelTests.cs:35`, the four
  consumers with their `_editorCallQueue` fields and `editorCallQueue` parameters (`PenEditorViewModel.cs:42,61`,
  `PenFormViewModel.cs:25,34`, `PenGroupWriter.cs:17,23`, `PenGroupsViewModel.cs:36`), a `cref` at
  `PenEditorViewModel.cs:129` (a stale `cref` fails the build: `GenerateDocumentationFile` with `TreatWarningsAsErrors`),
  and three doc lines (`docs/architecture/charting.md:564`, `docs/architecture/overview.md:507,596`).
- Window composition: `MainWindow/TrendWindow.Build` builds every part once, the chart at `:77` and the catalogue
  sync at `:98`, starts the sync at `:129`, and `Dispose` tears them down in reverse
  (`SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs:60-148`). Every `TrendWindow.Build` caller takes its container from
  `MainWindowTestBuilder`'s stand or the startup sequence, and the stand registers the fakes
  (`SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs:91-105`).
- `new TrendChartViewModel(` has five call sites: `TrendWindow.cs:77`, `SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs:47,77`,
  `SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs:72`, and `LiveEdgeArchiveJourneyTests.cs:148`.
  `SemiPlot.Tests.Integration` references Core, DataSource.Postgres, ArchiveSeeder and UI only
  (`SemiPlot.Tests.Integration.csproj:30-33`), never the Unit project.
- Namespace direction: `Chart/` already uses `Bridge/` (`TrendChartViewModel` takes `TrendCoordinator`), and no `Bridge/`
  file uses `SemiPlot.UI.Chart` (a grep for `using SemiPlot.UI.Chart` in `Bridge/` is empty). `MainWindow/` wires the
  two (`PenCatalogueApplier.cs:5-6`).
- Chart input (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`, 625 lines):
  - `ChartPressRouter.Route(ChartPressRegion region, KeyModifiers modifiers, LeftButtonTool activeTool)`
    (`SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs:26-46`) routes the left press: the active pen axis edits its scale,
    then Ctrl shifts the active pen, then the time axis pans or edits the time window, then the tool. `ChartPressRegion`
    is `DataArea | ActivePenAxis | TimeAxis` (`:15-20`), the click slop is 4 px (`:24`), and the view computes the region
    in `PressRegionAt` (`TrendChartView.axaml.cs:397-408`).
  - The view ignores every non-left press (`:357`). It handles release in a tunnel handler (`:73`) because `AvaPlot`
    releases the capture first. The release handler (`:474-497`) returns unless the press was the left button (`:476`),
    and clears the capture (`:482`), which raises `PointerCaptureLost` and reaches `EndDrag` (`:501-516`), the one place
    every drag ends. A drag starts past the slop (`:446-449`); `MoveCursor` takes the pointer's moment (`:457`); a resize
    repositions the overlay (`:76`).
  - Two flyouts open at the pointer through `ShowAt(PlotControl, true)` (`:414,421`): the axis scale panel, attached in
    XAML with `Placement="Pointer"` (`TrendChartView.axaml:26-30`), and the time window panel, a keyed resource
    (`TrendChartView.axaml:15-17`).
- Chart lines: the two delta `VerticalLine`s are created on the VM-owned plot and pinned to `plot.Axes.Bottom`
  (`TrendChartView.axaml.cs:524-533`). `OnDataContextChanged` (`:170-234`) creates them again on every bind without
  removing the old ones.
- Hover readout: `MoveCursor` returns early while dragging (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:395-402`).
  The overlay is hidden while dragging or in delta mode (`TrendChartView.axaml.cs:568-569`).
- Chart child VM precedent: `AxisScale`, created by `TrendChartViewModel` (`TrendChartViewModel.cs:76`) and disposed by it
  (`:182`). `TrendChartViewModel.cs` is 817 lines, so marker state goes in classes of its own.
- Palette: `ChartPalette.Apply` paints five ScottPlot surfaces from palette keys
  (`SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs:14-41`), and the view runs it from `ApplyChartPalette`
  (`TrendChartView.axaml.cs:141-150`) on load and on every theme variant change. Both theme dictionaries live in
  `SemiPlot/SemiPlot.UI/Styles/Palette.axaml:11-179`.
- Headless input: `HeadlessInput.Click` presses the left button only (`SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs:28-35`).
  AGENTS.md requires every click in a view test to go through that file.
- The empty-catalogue message is hit-test visible over the plot (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml:33-47`):
  with no pens a right-click in the centre does not reach the plot. Accepted: markers without pens have no use.

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task)
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
  - write unit tests for new functions/methods and for modified ones
  - add new test cases for new code paths, update existing ones if behaviour changes
  - tests cover both success and error scenarios
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- every task gate runs `dotnet build SemiPlot.slnx` first, so a change that breaks the Integration project fails there
- maintain backward compatibility: `PenCatalogueSync`'s public surface and its 12 tests stay as they are, except the
  `ReadInterval` alias: the interval lives on `ArchiveReadLoop.ReadInterval`, which the tests read

## Testing Strategy

- **unit tests** (`SemiPlot.Tests.Unit`): Core models, the read loop, the write queue and `MarkerSync` with plain `[Fact]`
  over a `TestScheduler`, as `PenCatalogueSyncTests` and `EditorCallQueueTests` do; VMs and views with `[AvaloniaFact]`.
  Every test class carries the `Component`, `Area` and `Category` traits: `Area=Bridge` for `MarkerSyncTests`,
  `Area=Data` for the store, `Area=Chart` for the chart and view tests.
- A test that builds `MarkerSync` uses a `TestScheduler` and advances one tick after `Start()`, never
  `ImmediateScheduler.Instance`: the loop waits through `Schedule(ReadInterval)` and would sleep the calling thread,
  the hang AGENTS.md records for `PenCatalogueSync`.
- View tests drive frames with `ChartViewTestBuilder.DriveOneFrame` and every click through `HeadlessInput`.
- **integration tests** (`SemiPlot.Tests.Integration`): `PostgresMarkerStore` runs every statement as `semiplot`
  against a `ClonedArchiveTest(..., CloneSource.Provisioned)` clone, which also proves the grant. They need the
  SemiBase release of Task 1 published as `:latest`, so they are the last code task.
- SemiBase: `go test ./...` plus its CI provisioning job.
- No Playwright-style e2e suite exists; the two-window behaviour is a manual smoke check (Acceptance Evidence).

## Acceptance Evidence

There is no defect to reproduce: this is a new feature. Today a grep for `semiplot_markers` over `SemiPlot/` and
SemiBase `internal/` prints nothing. Done means:

Automated:
1. `go test ./...` in SemiBase passes, including the new `semiplotTables`, schema-file and write-probe expectations.
2. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~MarkerStoreTests"`
   passes. An add returns an id. A read returns markers in `t` order. A move and a rename touch one row. A move of a
   deleted marker returns `ArchiveFault.MarkerGone`. A second delete succeeds. A `''` or 81-character label returns
   `ArchiveFault.ValueRejected`. A moment written as UTC reads back equal, including one in the autumn repeated hour.
3. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~Marker"` passes:
   `MarkerSetTests`, `MarkerSyncTests`, `ChartMarkersTests`, `ChartMarkerViewTests`.
4. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenCatalogueSyncTests|FullyQualifiedName~ArchiveCallQueueTests"`
   passes, the twelve sync tests unchanged after the loop extraction.
5. `git grep -l "IMarkerStore" -- SemiPlot/SemiPlot.UI/Chart SemiPlot/SemiPlot.UI/Legend` prints nothing, and
   `git grep -n EditorCallQueue -- . ':!docs/plans'` prints nothing.
6. `dotnet build SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` exit 0; `dotnet terse` over the
   touched files exits 0.

Manual smoke, on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):
1. Double-click an empty point of the chart: a line labelled "#1" and its time appears at that moment at once, with a dot
   and the active pen's value on the pen's line, and a tick on the minimap strip.
2. `File` -> `New window`: the second window shows the marker within 5 s of opening.
3. In window one, drag the marker 10 minutes to the right: it follows the pointer and stays where it was dropped.
   Window two moves it within 5 s.
4. In window two, rename the marker to "Etch start": the label shows in window two at once and in window one within 5 s.
5. Hover the marker in window one: the cursor snaps to it, and the readout lists every visible pen's value at its moment.
6. In window one, drag the marker more than 4 px and hold the button. In window two, delete it. Wait 5 s, then drop it in
   window one: the line disappears and the panel shows a Warning saying the marker no longer exists.
7. Pan until the marker leaves the window, then back: the line leaves and returns with the time axis.
8. Press on the time axis right under a marker: the time window panel opens, the marker does not move.
9. Switch the theme (`Edit` -> `Settings`), then add a marker: the old and the new line both draw in the new variant.
10. Restart the viewer: the markers come back.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

```
semiplot_markers  <--  PostgresMarkerStore : IMarkerStore (Core/Data)
                                 ^
                       MarkerSync : IMarkerChannel (Markers/)      ArchiveReadLoop (Bridge/): 5 s + ReadNow
                                 ^                                  ArchiveCallQueue (Bridge/): one write at a time
                                 |  Reads (UI thread) / Add, Move, Rename, Delete -> Result
                       ChartMarkers (Chart/)               owns MarkerSet (Core/Trends)
                                 |  Changed
                       TrendChartView + ChartMarkerLines (Chart/)   lines, labels, hit test, gestures, menu
```

- **One error contract for every write.** `MapWrite` takes the relation its statement touches. A move or rename of a
  gone marker is its own fault kind, `ArchiveFault.MarkerGone`, worded for the chart rather than the catalogue.
- **One polling loop, one write queue.** `PenCatalogueSync`'s loop, wake and failure counting move into
  `Bridge/ArchiveReadLoop<T>`. `PenEditor/EditorCallQueue` moves to `Bridge/ArchiveCallQueue`, since a second
  consumer now exists. `PenCatalogueSync` and `MarkerSync` keep only their read and what they do with it.
- **The chart never knows the database.** `Chart/` names no `Core/Data` interface. The chart talks to its own port
  `Chart/IMarkerChannel`, defined on the consumer side. The port carries the reads and the four writes, and each write
  returns a `Result`: the new id, or the failure to revert on. `Markers/MarkerSync` implements it over `IMarkerStore`
  and reports every failure to the message panel. `Markers/` uses `Chart/` and `Bridge/`; neither uses `Markers/`.
  The AGENTS.md rule "the chart never writes" becomes "the chart writes markers only, through `IMarkerChannel`", and a
  third grep pins `IMarkerStore` out of `Chart/` and `Legend/`.
- **Local state first, reads reconcile.** A write's own result updates the window at once: `RETURNING id` places an
  add, one touched row confirms a move or rename, and a delete removes the marker. A marker with a write in flight, or
  under the pointer during a drag, is held: reads do not move, relabel or remove it. A read that began before the
  latest successful write landed is discarded, and the `ReadNow()` after that write brings the fresh state. The writes
  of one window run one at a time, so two quick drags of one marker cannot commit out of order.
- **No startup read.** `MarkerSync.Start()` is followed by `ReadNow()`, so markers appear right after the window opens
  and a missing table never blocks the start. A failed read follows the loop's rule: two are logged, the third reaches
  the panel as an Error. Against a bench provisioned before Task 1's release, every window shows that Error about 10 s
  after start. That is accepted: the bench is recreated by `converge`.
- **Pointer routing.** A marker is a press region, `ChartPressRegion.Marker`, beside the two axis regions. The router
  keeps its order and adds the marker after the delta tool: active pen axis, Ctrl, time axis, delta tool, marker, pan or
  pick. Ctrl keeps the pen shift, the time axis keeps its panel under a marker, and the delta tool keeps its placement on
  top of a marker. A press on a marker drags it only past the 4 px slop; a release inside the slop picks a pen.

## Technical Details

### Table (SemiBase `sql/semiplot_markers.sql`)

```sql
-- docs/architecture/provisioning.md#the-semiplot-configuration-schema

CREATE TABLE IF NOT EXISTS semiplot_markers (
	id    bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY,
	t     timestamptz NOT NULL,
	label text CHECK (char_length(label) BETWEEN 1 AND 80)
);
```

- `timestamptz`, not the naive local time of `trends.t`: the viewer knows the exact UTC moment it places, and a naive
  local value is ambiguous in the autumn repeated hour (`docs/architecture/data-integration.md:796-833`, Time boundary).
  This is the second exception to "the archive stores naive local wall-clock time" (`:798`), next to the export.
- An empty label is stored as `NULL`. The check refuses `''`, the rule the pen form already applies
  (`data-integration.md:193`). `ChartMarkers` trims the text the label editor hands it, and the editor has
  `MaxLength = 80`, so the UI never reaches the
  check. A direct write that does gets `ValueRejected` with the pen-worded detail; accepted.
- No index: every read takes the whole table. Not partitioned and outside any retention: a marker outlives the day
  partition it points into.
- Grant `plotWriteGrants` to `semiplot`. The identity column needs no sequence grant (SemiBase `schema_test.go:89-95`).

### Core types (`SemiPlot.Core/Data`)

```csharp
public sealed record StoredMarker(long Id, DateTime TimeUtc, string? Label);

public interface IMarkerStore
{
	Task<Result<IReadOnlyList<StoredMarker>>> ReadAsync(CancellationToken cancellationToken = default);
	Task<Result<long>> AddAsync(DateTime timeUtc, string? label);
	Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc);
	Task<Result> RenameAsync(StoredMarker marker, string? label);
	Task<Result> DeleteAsync(StoredMarker marker);
}
```

- Statements in `ArchiveStatements`: `StoredMarkers` (`SELECT id, t, label FROM semiplot_markers ORDER BY t, id;`),
  `AddMarker` (`INSERT ... RETURNING id`), `MoveMarker`, `RenameMarker`, `DeleteMarker` (one row by id), and
  `MarkersRelation = "semiplot_markers"` for every detail line.
- A move or rename touching no row returns `MarkerGone`. A delete touching no row succeeds: the operator wanted the
  marker gone, the way `RemoveMembership` succeeds (`data-integration.md:198`).
- `t` binds as `NpgsqlDbType.TimestampTz` with `DateTimeKind.Utc` and reads back as UTC.
  ASSUMPTION: Npgsql refuses a non-UTC `DateTime` for `timestamptz` and returns `Kind = Utc` on read (no
  `LegacyTimestampBehavior` switch is set: `git grep LegacyTimestamp` is empty). The integration round-trip test pins it.
- The subject a fault names is the label, or the moment as local `yyyy-MM-dd HH:mm:ss` when there is none.

### Error contract

- `ArchiveExceptionMapper.MapWrite(Exception exception, string subject, string relation)`: the fallback passes
  `relation` instead of `PenCatalogRelations`. The three existing callers pass `PenCatalogRelations`, so their behaviour
  does not change.
- `ArchiveFault.MarkerGone`: `ArchiveError.cs` words it "The {archive} no longer holds the marker '{detail}'.", and
  `ArchiveFailureMapper` maps it to `Warning` with `FailureArchiveMarkerGoneTitle`, `...Detail` and `...Remedy` in both
  `.resx` files. Remedy: another window or an administrator removed it, and the chart drops it.

### Read loop and write queue (`Bridge/`)

- `ArchiveReadLoop<T>`: `Start()`, `ReadNow()`, `Dispose()`, the wait (`ReadInterval` 5 s), and the failure counting
  against `ArchiveConnectionState.ConsecutiveFailuresBeforeFault`. It takes the read
  (`Func<CancellationToken, Task<Result<T>>>`), the apply (`Action<T>`), the name of what it reads for the warning
  ("pen catalogue", "marker"), the message panel, the UI scheduler and the consumer's logger. Code moved from
  `PenCatalogueSync.cs:44-53,63,67-101,112-119,129-144`; the warning template at `:136` becomes
  "The {Read} read failed, ...". The delta comparison (`:120-126`), `Rebase`, `Deltas` and `_deltas.OnCompleted()` stay
  in `PenCatalogueSync`.
- `ArchiveCallQueue`: `PenEditor/EditorCallQueue` moved and renamed, behaviour unchanged. Every reference follows:
  the fields become `_archiveCallQueue`, the parameters `archiveCallQueue`, the `cref` names the new type, and the
  three doc lines name it too.

### MarkerSync (`Markers/`)

- `MarkerSync : IMarkerChannel, IDisposable` runs an `ArchiveReadLoop<GenerationRead>`, a private
  `record struct GenerationRead(int Generation, IReadOnlyList<StoredMarker> Markers)`. Its read captures `_writeGeneration` before
  the statement starts. Its apply emits on `Reads` only when the captured value still equals `_writeGeneration`. A
  discarded read counts as a success for the failure counter. It emits every read that is not discarded, changed or
  not: `MarkerSet.Apply` is the one diff.
- The four writes go through one `ArchiveCallQueue(() => _writeGeneration++)`. The queue runs the callback inside the
  queued call, so the bump lands before the caller resumes. Every write ends with `ReadNow()`, whatever its outcome. A
  failed write is reported through `MessagePanelViewModel.ReportFailure` and returned to the caller.
- A write that fails on the client after the server committed does not bump, so a read already in flight emits the old
  state once; the `ReadNow()` after the failed write brings the committed state. Accepted.

### Chart side

- `Core/Trends/MarkerSet`: markers sorted by time, then id, in an array.
  - `Apply(read)` replaces every marker that is not held, and returns whether anything changed.
  - `Hold(id)` and `Release(id)` count, so two overlapping writes on one marker keep it held until both land.
    `Release` returns whether the count reached zero. A held marker keeps its local moment and label, and it stays when
    a read omits it.
  - `Place(marker)` is an upsert by id, so a read that already brought the new id leaves one marker; a relabel is a
    `Place`. `Remove`.
  - `NearestWithin(DateTime time, TimeSpan tolerance)` is a binary search, no LINQ: it runs on every pointer move.
- `Chart/IMarkerChannel`: `Reads` (`IObservable<IReadOnlyList<StoredMarker>>`) plus `AddAsync`, `MoveAsync`, `RenameAsync`
  and `DeleteAsync`, the shapes of `IMarkerStore` minus the read.
- `Chart/ChartMarkers`, created and disposed by `TrendChartViewModel` like `AxisScale`, from the port it
  receives in its constructor:
  - it subscribes `Reads` synchronously, with `onError` to the chart's `ReportFailure`, and calls `MarkerSet.Apply`;
  - it exposes `Markers` (the current set) and `Changed` (`IObservable<Unit>`, a private subject through `AsObservable()`);
  - its gesture methods `AddAtAsync(time, label)`, `BeginMove`, `Move`, `EndMoveAsync`, `RenameAsync` and `DeleteAsync`
    return `Task`. `BeginMove`, `RenameAsync` and `DeleteAsync` hold the marker and release it in `finally`.
  - a failed move or rename restores the stored moment or label, only when its `Release` brings the count to zero; a
    still-held marker keeps the newer local state. `MarkerGone` removes the marker. A failed delete keeps it.
  - a pending add is not in `MarkerSet` and draws nothing until its id lands.

### View

- The view calls the gesture methods from `async void` event handlers whose body is wrapped in `try/catch` into
  `TrendChartViewModel.ReportFailure` (`TrendChartViewModel.cs:803-806`), the form AGENTS.md and the Avalonia rules allow.
- `Chart/ChartMarkerLines` owns one `VerticalLine` per marker on `plot.Axes.Bottom`, with `EnableAutoscale = false`.
  - `Sync(markers)` is the one diff on the view side: it adds a line for a new id, removes the line of a gone id, and
    sets `X` and the label text of every surviving line from the set, so a move or a rename shows. The view calls it once
    when it creates the layer, then on every `Changed`, which it subscribes with `ReportFailureOf`, then `RequestFrame()`.
  - `Project(window, dataRectWidth)` hides lines outside the window. It hides a label whose estimated width (label
    length times font size) does not fit before the next visible marker, a pre-render estimate like
    `ChartCursorOverlay.Project`. The view calls it on `WindowChanged` (`OnNavigationWindowChanged`,
    `TrendChartView.axaml.cs:285-288`), on `SizeChanged` (`:76`) and after every `Sync`.
  - `ApplyPalette(lineColor, labelColor)` keeps both colours, recolours every line, and every later line takes them.
    The view calls it from `ApplyChartPalette` (`TrendChartView.axaml.cs:141-150`) with the colours
    `ChartPalette.MarkerColors(resources, variant)` resolves from the new keys `AppMarkerLineBrush` and
    `AppMarkerLabelBrush`, which both dictionaries of `Palette.axaml` carry.
  - The line is a neutral grey, dashed, 1.5 px wide, so it does not compete with the pens' colours. The values reuse
    tones the palette already has:

    | Key | Light | Dark |
    | --- | --- | --- |
    | `AppMarkerLineBrush` | `#6F737A` | `#A8ADBD` |
    | `AppMarkerLabelBrush` | `#000000` | `#DFE1E5` |
  - `RemoveLines` removes every line it added. The view creates the layer in `OnDataContextChanged` and adds
    `Disposable.Create(layer.RemoveLines)` to `_disposables`.
- ASSUMPTION: `VerticalLine.LabelText` in ScottPlot 5.1.59 draws on the axis edge, over the bottom tick labels, and with
  `LabelOppositeAxis = true` it draws in the top strip. Task 8 starts by rendering one labelled line in a headless test
  and reading where the text lands. If the top strip is not readable, the label is a `Text` plottable at the top of the
  data area, pinned to `plot.Axes.Bottom`.
- Hit tolerance: `AnchorAt(x + 4) - AnchorAt(x)` at the pointer, so it follows the same pixel space as every other
  anchor in the view. No hit test runs while `Plot.LastRender.DataRect.Width` is 0.
- Press region: `PressRegionAt` (`TrendChartView.axaml.cs:397-408`) returns `ChartPressRegion.Marker` when the press is
  in the data area within tolerance of a marker, after its two axis checks. `Route` (`ChartPressRouter.cs:26-46`) returns
  `ChartPressAction.DragMarker` for that region after the Ctrl, time-axis and delta-tool branches.
- Hover within tolerance: the pointer shows `SizeWestEast` and goes back to `_handCursor` outside it, and `MoveCursor`
  receives the marker's moment, so the existing readout is the crossing readout. While a marker drags, no readout follows
  it (`MoveCursor` returns under `IsDragging`). In delta mode the overlay stays hidden. Both are accepted.
- A marker drag commits in `EndDrag` and nowhere else, at the last `Move` moment. A release reaches `EndDrag` through the
  capture loss it raises, and a capture lost for another reason ends there too, as a pan keeps the window it reached.
- Right button: the tunnel release handler checks `InitialPressMouseButton == MouseButton.Right` ahead of its
  left-button guard (`TrendChartView.axaml.cs:476`). It opens a `MenuFlyout` with `Placement = Pointer` through
  `ShowAt(PlotControl, true)`, the way the two existing flyouts open (`:414,421`). It does not declare a `ContextFlyout`,
  whose `ContextRequested` may never fire on `AvaPlot`. Near a marker the menu is `Rename`, `Delete`. Elsewhere in the
  data area it is `Add marker here`, which adds a numbered marker at the clicked moment.
- A double click adds a numbered marker or deletes the marker under the pointer: `ChartPressRouter.Route` takes the
  press's `ClickCount` and returns `ChartPressAction.AddMarker` for a second click where a single one would return
  `PanOrPickPen`, and `DeleteMarker` where it would return `DragMarker`.
- The label editor serves `Rename` only, a third flyout declared as a keyed resource like the time window panel
  (`TrendChartView.axaml:15-17`): one focused `TextBox` (`MaxLength = 80`), OK and Cancel, at the two panels' 360 px. The
  text is trimmed, and an empty result means no label. It opens with the current label: OK or Enter stores the text,
  Cancel or Esc changes nothing.

## What Goes Where

- **Implementation Steps**: the SemiBase table and release, the SemiPlot error contract, the Core types and model, the
  read loop and write queue, the sync, the store and its registration, the chart VM and window wiring, the view, the
  gestures, the menu, the container tests, the docs.
- **Post-Completion**: the GitHub issue texts, the delta lines' colour, the manual smoke run.

## Implementation Steps

### Task 1: Provision semiplot_markers in SemiBase

Work in `C:\Users\admin\projects\SemiBase`, on its own branch and PR.

**Files:**
- Create: `sql/semiplot_markers.sql`
- Modify: `embed.go`
- Modify: `internal/provision/schema.go`
- Modify: `internal/provision/check.go`
- Modify: `internal/provision/schema_test.go`
- Modify: `internal/provision/check_test.go`
- Modify: `cmd/semibase/main.go`
- Modify: `README.md`
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/README.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/provisioning.md`
- Modify: `docs/deployment.md`

- [x] add `sql/semiplot_markers.sql` (Technical Details) and embed it as `SemiplotMarkersSQL` in `embed.go`
- [x] add `{"semiplot_markers.sql", semibase.SemiplotMarkersSQL}` to `semiplotSchemaFiles` and
      `{"semiplot_markers", plotWriteGrants}` to `semiplotTables`, both right before `semiplot_meta`
      (`schema.go:12-20,42-50`)
- [x] add INSERT, UPDATE and DELETE probes on `semiplot_markers` to `plotWriteProbes` (`check.go:26-45`): the INSERT binds
      `t = now()` and `label = writeProbeName`, the UPDATE and DELETE find the row by that label. Extend the `ok(...)` line
      of `assertPlotWritesItsTables` (`check.go:284-293`)
- [x] sweep the docs and the `--help` text: every line `git grep -nE "\bfour\b|четыре|semiplot_pen_groups" -- "*.md" "*.go"`
      prints outside `docs/plans` either names the new table and its grant or is shown not to need it. Also rewrite the
      risk statement (`overview.md:29-31`: a viewer bug can now corrupt markers as well), the apply-order paragraph
      (`provisioning.md:230-234`), the tail-check list (`:428-429`), and the bump-to-2 sentence (`:325-326`)
- [x] write tests: insert the new statement at its index in `TestSemiplotGrantStatements` (`schema_test.go:152-175`) and
      extend the schema-file, table-name and write-probe expectations
- [x] run `go test ./...` and the CI provisioning job - must pass
- [x] approval gate: with the user's approval, merge to SemiBase master and push a release tag, so
      `ghcr.io/semiteq/semibase:latest` carries the table (`.github/workflows/release.yml`)
      (approved in advance: Semiteq/SemiBase#16 squash-merged as `4499b27`, tag `v0.6.0`; `:latest` and `:v0.6.0` both
      resolve to `sha256:885a65fb866b674288547e6c4c3a7d0c658d06bed38bebff1e8771f418dd4159` and print `v0.6.0`)

### Task 2: Give every write its own relation and add MarkerGone

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresPenCatalogueEditor.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveFault.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PenCatalogueEditorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`

- [x] add the `relation` parameter to `MapWrite` (`ArchiveExceptionMapper.cs:37-46`) and a `MarkerGone(string subject)`
      factory beside `RowGone` (`:49-52`)
- [x] pass `ArchiveStatements.PenCatalogRelations` from all three callers: `PostgresPenCatalogueEditor.MapWrite`
      (`:282-285`), `PenCatalogueEditorTests.cs:330` and the helper `ArchiveExceptionMapperTests.cs:244-246`
- [x] add `ArchiveFault.MarkerGone`, its `ArchiveError` message, its `Warning` arm in `ArchiveFailureMapper`
      (next to `:212-216`) and the three resources in both `.resx` files
- [x] write tests: `MapWrite` names the given relation on `42P01`; the pen editor path still names the pen tables
      (the editor path is pinned by the container test
      `PenCatalogueEditorTests.AWriteToADroppedCatalogueTableNamesEveryCatalogueRelation`)
- [x] write tests: `ArchiveFailureMapper` maps `MarkerGone` to `Warning` with its own title; add
      `[ArchiveFault.MarkerGone] = MessageSeverity.Warning` to the table in `FailureSeverityTests.cs:21-34`
- [x] run `dotnet build SemiPlot.slnx`, then `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` -
      must pass before Task 3

### Task 3: Add the marker Core types and the MarkerSet model

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/IMarkerStore.cs` (with `StoredMarker`)
- Create: `SemiPlot/SemiPlot.Core/Trends/MarkerSet.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MarkerSetTests.cs`

- [x] add `IMarkerStore` and `StoredMarker` (Technical Details, Core types)
- [x] implement `MarkerSet` (Technical Details, Chart side)
- [x] write tests: `Apply` of an unchanged read returns false; of a moved, relabelled, added or removed marker returns
      true with the new set
- [x] write tests: a held marker keeps its local moment and label through `Apply` and stays when a read omits it; two
      `Hold`s and one `Release` keep it held, and only the second `Release` reports zero
- [x] write tests: `Place` of an id `Apply` already added leaves one marker
- [x] write tests: `NearestWithin` returns the closer of two neighbours, nothing outside the tolerance, and the lower id
      on a tie
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 4

### Task 4: Extract ArchiveReadLoop and move the write queue to Bridge

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Bridge/ArchiveReadLoop.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs`
- Move: `SemiPlot/SemiPlot.UI/PenEditor/EditorCallQueue.cs` -> `SemiPlot/SemiPlot.UI/Bridge/ArchiveCallQueue.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`, `PenFormViewModel.cs`, `PenGroupWriter.cs`,
  `PenGroupsViewModel.cs`
- Move: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/EditorCallQueueTests.cs` ->
  `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/ArchiveCallQueueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenFormViewModelTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/overview.md`

- [x] move the loop members into `ArchiveReadLoop<T>` with the neutral warning (Technical Details, Read loop and write queue)
- [x] keep `PenCatalogueSync`'s public members (`Deltas`, `Start`, `ReadNow`, `Rebase`, `Dispose`) and let it delegate
      the loop with the read name "pen catalogue"; the interval is `ArchiveReadLoop.ReadInterval`
- [x] move and rename `EditorCallQueue` to `Bridge/ArchiveCallQueue` with `git mv`, then follow every reference of the
      Context list: types, `_archiveCallQueue` fields, `archiveCallQueue` parameters, the `cref` at
      `PenEditorViewModel.cs:129`, `PenFormViewModelTests.cs:35`, and the doc lines `charting.md:564` and
      `overview.md:507,596`
- [x] tests: `PenCatalogueSyncTests` pin the loop through the production surface (`:45-237`), and the moved
      `ArchiveCallQueueTests` pin the queue; run both unchanged apart from the type name
- [x] check `git grep -n EditorCallQueue -- . ':!docs/plans'` prints nothing
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 5

### Task 5: Add the marker port and MarkerSync

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/IMarkerChannel.cs`
- Create: `SemiPlot/SemiPlot.UI/Markers/MarkerSync.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Markers/FakeMarkerStore.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Markers/MarkerSyncTests.cs`

- [x] add `IMarkerChannel` (Technical Details, Chart side)
- [x] implement `MarkerSync` (Technical Details, MarkerSync)
- [x] add `FakeMarkerStore`, an `IMarkerStore` whose reads and writes a test completes by hand; it has two consumers,
      these tests and `MainWindowTestBuilder` (Task 7)
- [x] write tests (plain `[Fact]`, `TestScheduler`): `ReadNow` right after `Start` reads at once; an unchanged read emits
- [x] write tests: a read held across a successful write is discarded and does not count as a failure; the `ReadNow`
      after the write emits; a read held across a failed write emits
- [x] write tests: a second write starts only after the first lands; a failed write reaches the panel once and returns
      its failure; two failed reads log with "marker" in the warning, the third reports once
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 6

### Task 6: Add PostgresMarkerStore and register it

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresMarkerStore.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`

- [x] add the five statements and `MarkersRelation` to `ArchiveStatements` (pattern `:113-117`)
- [x] implement `PostgresMarkerStore` with an internal constructor over `NpgsqlDataSource`, `ArchiveExceptionMapper`
      and a logger (pattern `PostgresPenCatalogueEditor.cs:24-32,194-214`)
- [x] register it by factory in `AddPostgresData()` beside the editor (`PostgresDataServiceCollectionExtensions.cs:46-49`)
- [x] write the composition test: the container resolves `IMarkerStore` as `PostgresMarkerStore`
      (pattern `CompositionRootTests.cs:40`). The statements run against a database in Task 11
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 7

### Task 7: Add the chart's markers VM and wire MarkerSync into the window

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartMarkers.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/FakeMarkerChannel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/TrendWindowTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/Journeys/NoMarkers.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkersTests.cs`

- [x] implement `ChartMarkers` (Technical Details, Chart side)
- [x] give `TrendChartViewModel` an `IMarkerChannel` constructor parameter; it creates the VM next to `AxisScale`
      (`TrendChartViewModel.cs:76`), exposes it as `Markers`, and disposes it next to `AxisScale` (`:182`)
- [x] in `TrendWindow.Build`, resolve `IMarkerStore`, build `MarkerSync` ahead of the chart (`:75-77`) and pass it as the
      channel. Call `Start()` then `ReadNow()` next to `catalogueSync.Start()` (`:129`), and dispose it after the chart in
      `Dispose` (`:137-148`)
- [x] pass a channel at the other four call sites: `ChartTestBuilder.cs:47,77` take an optional `IMarkerChannel`
      defaulting to a new `FakeMarkerChannel`, which tests use to push reads and inspect writes; the two journey tests pass
      `NoMarkers`, an Integration-side channel whose `Reads` never emits
- [x] register `FakeMarkerStore` in `MainWindowTestBuilder`'s stand (`:91-105`)
- [x] write VM tests: an add places the marker with the returned id; a failed add places nothing; a read landing before
      the add's id leaves one marker
- [x] write VM tests: a failed move restores the stored moment; a failed move during a second drag of the same marker
      keeps the second drag's moment; `MarkerGone` removes the marker; a failed rename restores the label; a delete
      removes it and a failed delete keeps it; a read during a drag does not move the dragged marker; `Changed` fires once
      per change
- [x] write the window test: disposing the window stops the marker read (pattern
      `TrendWindowTests.DisposingTheWindowStopsTheCatalogueRead`, `TrendWindowTests.cs:50`)
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 8

### Task 8: Draw the markers

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartMarkerLines.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPaletteTests.cs`

- [x] render one labelled `VerticalLine` in a headless test and settle the label placement (Technical Details, View ASSUMPTION)
      (the default label covers the bottom tick labels and `LabelOppositeAxis` alone clips it in the 15 px top strip;
      the `VerticalLine` label serves with `LabelOppositeAxis`, `ManualLabelAlignment = UpperLeft`, offsets 4 and 12 px
      and a transparent background: it hangs inside the data area under its top edge, right of the line. No `Text`
      plottable. The probe test was removed after the render was read)
- [x] implement `ChartMarkerLines` (Technical Details, View) (not `IDisposable`: the view keeps it in a property, which
      CA1001 refuses for a disposable; `RemoveLines()` takes the lines off the plot and the view's `_disposables` call it)
- [x] add the two keys to both dictionaries of `Palette.axaml` and `ChartPalette.MarkerColors`
- [x] in the view, create the layer in `OnDataContextChanged` (`TrendChartView.axaml.cs:170-234`) and `Sync` it once,
      subscribe `Changed`, call `ApplyPalette` from `ApplyChartPalette` (`:141-150`), and `Project` on `WindowChanged`
      (`:285-288`) and `SizeChanged` (`:76`)
- [x] write view tests: a read with two markers shows two marker lines at their moments; after a `Changed` raised by a
      move, that marker's line has `X == LocalTimeAxis.ToAxis(newMoment)`; after a rename, its line shows the new label
- [x] write view tests: a rebind to the same VM leaves two marker lines (count the layer's lines, not every
      `VerticalLine`: the delta lines duplicate on rebind); a marker outside the window is invisible; the label of a marker
      too close to its neighbour is hidden, and shows again after a resize that makes room
- [x] write view tests: after a theme variant switch, a marker added later carries the new variant's line colour; both
      keys resolve in both variants
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 9

### Task 9: Drag, hover and snap

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPressRouterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`

- [x] add `ChartPressRegion.Marker` (`ChartPressRouter.cs:15-20`) and `ChartPressAction.DragMarker`; `Route` returns it
      for that region after the Ctrl, time-axis and delta-tool branches (`:26-46`)
- [x] `PressRegionAt` (`TrendChartView.axaml.cs:397-408`) returns `Marker` for a data-area press within tolerance of a
      marker, after its two axis checks (the press hit-tests once and hands the marker to `PressRegionAt` and the drag;
      the hit test reads `ChartMarkers.NearestWithin`, a pass-through added to the VM)
- [x] in the view: past the slop, a `DragMarker` drag calls `BeginMove` once and `Move` per pointer move; `EndDrag`
      (`:511-516`) calls `EndMoveAsync` with the last moment, the one commit point; a release inside the slop picks a pen
      (`EndDrag` became `EndDragAsync`, awaited by the now `async void` capture-lost handler, which reports a failure)
- [x] on hover within tolerance, show `SizeWestEast` and pass the marker's moment to `MoveCursor` (`:457`); outside it,
      restore `_handCursor`; skip the hit test while the data rect width is 0 (the whole hover is skipped: the
      existing `AnchorAt` already threw before the first render; `SizeWestEast` shows only when a press would drag)
- [x] write router tests: Ctrl on a marker shifts the pen; the time axis returns `PanOrEditTimeWindow` whatever lies
      under it; the delta tool on a marker places the delta cursor; the pan tool on a marker drags it; the data area pans
- [x] write view tests: one drag past the slop issues exactly one move, with the dropped moment; a click inside the slop
      issues none and picks; hovering a marker sets `CursorTime` to its moment; a pointer move before the first frame
      does not throw
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 10

### Task 10: Right-click menu and label editor

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerViewTests.cs`

- [x] add a `MouseButton` overload of `Click` to `HeadlessInput` (`HeadlessInput.cs:28-35`); no test class carries its own
- [x] open the `MenuFlyout` from the tunnel release handler for the right button (Technical Details, View)
      (two keyed `MenuFlyout`s, `ChartMenuFlyout` and `MarkerMenuFlyout`, whose items run `Click` handlers in the view)
- [x] add the label editor flyout for add and rename as a keyed resource (Technical Details, View), with the reserved
      message line of `docs/architecture/ui-theme.md#a-form-never-resizes-on-validation`
      (640 px wide; nothing in the editor can be invalid, so the reserved line carries the non-error hint
      `MarkerLabelHint` and never changes; a click outside closes it like Cancel)
- [x] add every menu and editor string to both `.resx` files
- [x] write view tests: `Add marker here` then "Etch start" and Enter adds a marker labelled "Etch start" at the clicked
      moment; an empty or whitespace-only Enter adds an unlabelled one; Esc adds nothing
- [x] write view tests: on a marker, `Delete` removes it; `Rename` with "Etch end" relabels it; an empty rename stores
      `null`; Esc keeps the old label
- [x] run `dotnet build SemiPlot.slnx` and the unit tests - must pass before Task 11

### Task 11: Run the marker store against a database

Start only after `docker pull ghcr.io/semiteq/semibase:latest` shows the Task 1 release.

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Integration/MarkerStoreTests.cs`

- [x] write integration tests (`ClonedArchiveTest`, `CloneSource.Provisioned`): the cases of Acceptance Evidence item 2
- [x] write the error test: a dropped table gives `TableMissing` naming `semiplot_markers`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` - must pass before Task 12

### Task 12: Verify acceptance criteria

- [x] verify every requirement in the Overview is implemented
- [x] run every automated command in Acceptance Evidence
      (SemiBase `go test -count=1 ./...` ok at `4499b27`; `MarkerStoreTests` 19 passed; the unit `Marker` filter 96
      passed; `PenCatalogueSyncTests|ArchiveWriteQueueTests` 17 passed, the 12 sync facts unchanged since `127e3e4`;
      both greps print nothing)
- [x] run `dotnet test SemiPlot.slnx` (Unit 2191 passed, Integration 193 passed, none skipped)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
      (both exit 0; terse over the 51 `.cs` files the branch adds or changes)
- [x] manual smoke (skipped - not automatable, left for the operator)

### Task 13: [Final] Update documentation

- [x] `AGENTS.md`: reword "the chart never writes" to markers through `IMarkerChannel` only and add the
      `git grep -l "IMarkerStore" -- SemiPlot/SemiPlot.UI/Chart SemiPlot/SemiPlot.UI/Legend` check; add the marker branch
      to the left-button gesture bullet; add `semiplot_markers` to the bullet on the relations each read supplies; add the
      `MarkerSync` `TestScheduler` rule next to the `PenCatalogueSync` one
- [x] `docs/architecture/data-integration.md`: a marker store section beside the pen catalogue editor, the `timestamptz`
      exception in Time boundary (`:796-833`), and `MapWrite`'s relation and `MarkerGone` in Error semantics
- [x] `docs/architecture/overview.md`: the live markers next to `#the-live-catalogue`, `ArchiveReadLoop` and
      `ArchiveCallQueue`
- [x] `docs/architecture/trend-interaction.md`: the marker gestures, the press order, the hover readout at a marker, no
      cap on the count; remove "annotations" from the two out-of-scope lists (`:98,693`)
- [x] `docs/architecture/trend-feature-spec.md`: a note under CU-4 (`:152-154`) that stored markers cover the placement
      and that the list and navigation are still open
- [x] `docs/architecture/ui-theme.md`: the two marker palette keys (and a `The marker label editor` subsection
      under the form rule)
- [x] ➕ `docs/architecture/ui-text.md#the-pen-editors-failure-text`: it lists three write kinds; add
      `FailureArchiveMarkerGone*` (Task 2) and the Russian term "метка" for a marker
- [x] ➕ the table and grant lists in `postgres-instance.md`, `postgres-topology.md` and `testing-strategy.md`
      name `semiplot_markers` (five configuration tables)
- [x] move this plan to `docs/plans/completed/` (left in place: archiving the plan is the delivery step)

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**External system updates:**
- Rewrite Semiteq/SemiBase#10: role `semiplot`, `timestamptz`, the label check, no `created_at`, schema version 1.
- Update Semiteq/SemiPlot#77: one role, the hover-only crossing values, no cap on the count, one theme colour, the
  optional label, a double click adds a numbered marker or deletes one, the active pen's value at each marker, the
  minimap ticks. Open one new issue for the marker list and CU-4 previous/next navigation: both find a marker outside
  the current window.

**Known gap outside this plan:**
- The delta lines are created without a colour (`TrendChartView.axaml.cs:524-533`) and draw in ScottPlot's default in
  both themes; a separate backlog item.

**Manual verification:**
- The smoke checklist in Acceptance Evidence, two windows on the demo stand.

**Executed by exec:**
- branch: worktree-time-markers

## Verify it yourself

Before the branch (`master`, `127e3e4`) the chart has no markers: a right-click on the plot opens nothing, and
`git grep semiplot_markers` prints nothing. After it (`worktree-time-markers`) every check below holds.

Automated, from the worktree root (Docker running, `ghcr.io/semiteq/semibase:latest` at v0.6.0):

1. `dotnet test SemiPlot.slnx` passes: 2210 unit and 193 integration tests, none skipped.
2. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~MarkerStoreTests"`
   passes 19 tests: the store round trip as `semiplot`, including both UTC halves of the autumn repeated hour.
3. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ASecondTouchDuringAMarkerDrag"`
   fails at `7d58057` (a second finger replaced the drag and left the marker held) and passes at `07ea81d`.
4. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~DisposingDuringAHeldRead"`
   passes from `90de8ef`: closing a window during a marker read reports nothing.
5. `git grep -l "IMarkerStore" -- SemiPlot/SemiPlot.UI/Chart SemiPlot/SemiPlot.UI/Legend` prints nothing.

Manual, on the demo stand `dotnet run --project SemiPlot/SemiPlot.AppHost` (no test covers two real windows):

1. Double-click an empty point of the plot: a grey dashed line labelled "#1" and its time appears at the clicked moment,
   the active pen's value shows on it, and the minimap strip shows a tick there. Right-click it, pick `Rename`, type
   "Etch start", press Enter: the label shows, and the editor fits inside its frame with both buttons whole.
2. `File` -> `New window`: the second window shows the same marker within 5 s.
3. In window one, drag the marker to another time and release: it stays there; window two moves it within 5 s.
4. In window two, right-click the marker, `Rename` to "Etch end": window one shows the new label within 5 s.
5. Hover the marker: the pointer turns into a left-right arrow, the cursor line snaps to it, and the readout lists every
   visible pen's value at the marker's moment.
6. In window one, drag the marker and hold; in window two, `Delete` it; wait 5 s and release in window one: the line
   disappears and the panel shows a Warning that the marker no longer exists.
7. Press on the time axis right under a marker: the time window panel opens, the marker does not move.
8. Switch the theme in `Edit` -> `Settings`: the line and label repaint in the new variant.
9. Restart the viewer: the markers come back.
10. Click another pen's line: every marker's value moves to that pen. Double-click a marker: it disappears in both
    windows.
