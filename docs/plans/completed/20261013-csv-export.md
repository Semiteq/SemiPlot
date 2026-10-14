# CSV export of an archive range

## Overview

- The operator exports archive rows to a CSV file: `File` -> `Export to CSV...` opens a modal export window,
  the operator picks a range of at most 24 hours and a set of pens, chooses a file, and the window writes the
  archive's own rows into it (issue #73).
- The file carries the archive 1:1: one CSV line per main-layer (`l = 0`) row, the stored timestamp verbatim,
  the stored value and the stored quality code. No union timeline, no fill, no interpolation, no decimation.
- Each pen also exports its seed row, the last row before the range start within one day, so a pen whose value
  did not change inside the range still appears in the file.
- The export reads the archive through a new read interface of its own that works in the archive's wall-clock
  time end to end; `IDataProvider` and the chart are untouched.

## Context (from discovery)

- No export of any kind exists: `git grep -il csv -- SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.Core` prints nothing
  (verified 2026-10-13).
- `IDataProvider.QueryHistoryAsync` only offers decimated envelopes, Raw included: it takes a
  `targetColumnCount` (`SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:19-25`), and the Raw branch binds a
  `date_bin` bucket (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:212-224`, `:345-362`). A
  full-resolution read needs a new call shape.
- The chart seeds every pen with the last row before the window start, bounded to the wider of the window and
  one day (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:195-204`); a steady pen draws from that
  row alone.
- The archive stores naive local wall-clock time; the provider converts to UTC at its edge
  (`docs/architecture/data-integration.md:444-464`,
  `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs:14-37`). The conversion is not injective at the
  autumn fall-back: `ToUtc` resolves an ambiguous time to standard time (`ArchiveTimeConverter.cs:23`) and the
  UTC side drops the repeated hour (`docs/architecture/data-integration.md:459-463`). The SCADA, the archive and
  the viewer share one machine and one zone (`docs/architecture/data-integration.md:454-456`).
- `q = 32` marks the last sample before a break, `q = 16` the first after; absence of rows alone cannot
  separate "unchanged" from "gap" (`docs/architecture/scada-archive.md:212-236`).
- Volume: one day of a 16-variable project is ~670 000 rows
  (`docs/architecture/scada-archive-observed.md:546`); the archiving interval goes down to 100 ms
  (`docs/architecture/scada-archive.md:247-249`), so one pen can write up to 864 000 rows a day. Rows arrive
  minutes to hours after their instant (`docs/architecture/scada-archive.md:264-277`).
- `docs/architecture/data-integration.md:228-230` records the planner sorting window rows of a `UNION ALL`
  statement rather than reading them in index order.
- The write path already sits behind a segregated interface registered beside the provider over one
  `NpgsqlDataSource` (`SemiPlot/SemiPlot.Core/Data/IPenCatalogueEditor.cs:9-24`,
  `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:36-39`).
- Window parts are built in `TrendWindow.Build` (`SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs:58-119`),
  which resolves services from the container (`:63-68`).
- Dialog route: `MainWindowViewModel` exposes a request observable over a private subject
  (`SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:27-31`, `:106-110`), and `MainWindow` code-behind
  subscribes in `OnLoaded` and opens the window with `ShowDialog`, disposing the view model in `finally`
  (`SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs:23-39`, `:69-83`).
- The menu is `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml:15-23`; every leaf carries a command
  (`AGENTS.md`, menu rule; `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`).
- The visible window is `ChartNavigationController.From`/`To` in UTC
  (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:29-31`); a pen is switched on through
  `TrendPenState.IsVisible` (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:27-35`); pens in catalogue order are
  `TrendChartViewModel.Pens` (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:116`), replaced whole per
  catalogue (`:242`); an empty chart is `HasNoPens` (`:124`), whose change is raised by hand (`:244`).
- Form rule: fixed width, `SizeToContent="Height"`, invalid field marked by its border alone, one reserved
  two-line message cell left of the buttons that also carries non-error notices
  (`docs/architecture/ui-theme.md:174-189`; the settings dialog is the model,
  `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml:12-14`).
- Failures reach the operator through `ArchiveFailureMapper.Map`
  (`SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs:17-31`); an unmapped exception falls to
  `MapThrown` as untranslated runtime text (`docs/architecture/ui-text.md:95-104`). The pen editor shows a
  failure's title in its own message line beside the panel entry (`docs/architecture/ui-text.md:113-115`).
- The interface language is `UiLanguage` (`SemiPlot/SemiPlot.UI/Startup/AppSettings.cs:8-12`) from
  `AppSettings.Locale` (`:70`), applied at start only; `StartupSequence` sets `DefaultThreadCurrentUICulture`
  and nothing else (`SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs:47-49`), so `CurrentCulture` is the OS
  culture. `App.ConfigureStarted` holds the settings when it builds the window
  (`SemiPlot/SemiPlot.UI/App.axaml.cs:123-134`).

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task)
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
  - tests are not optional - they are a required part of the checklist
  - write unit tests for new functions/methods
  - write unit tests for modified functions/methods
  - add new test cases for new code paths
  - update existing test cases if behavior changes
  - tests cover both success and error scenarios
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- maintain backward compatibility

## Testing Strategy

- **unit tests** (`SemiPlot.Tests.Unit`): the CSV writer, the range form, the export view model over a fake
  reader, a `TestScheduler` and a temporary directory, the statement text, the mapper arm, the dialect choice,
  the realised export window headless.
- **integration tests** (`SemiPlot.Tests.Integration`, container): the reader against a cloned archive, and the
  plan shape of the new statements in `ExplainPlanTests`.
- No e2e suite exists beyond the journeys; the export adds no journey.
- Every test class carries the three traits (`AGENTS.md`, Test traits). Headless gestures go through
  `HeadlessInput` (`AGENTS.md`, Test). No view-model test uses `ImmediateScheduler` (`AGENTS.md`, Test).

## Acceptance Evidence

The defect is the absence of the feature, so the evidence is the feature's tests going green plus one manual
check that the file opens in Excel, which no test can drive.

Automated, each must pass:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~Export"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReader|FullyQualifiedName~ExplainPlan"
dotnet test SemiPlot.slnx
```

The assertions that carry the feature:

- a pen with no row inside the range exports exactly its seed row;
- rows stored inside the autumn repeated hour all come out with their stored `t`, for a range typed across it;
- `q = 32` and `q = 16` rows come out with their `q`;
- a cancelled and a failed export leave no file at the chosen path and no temporary file beside it;
- progress reaches the bound state only on the UI scheduler;
- the export window keeps its `Bounds` when a field turns invalid;
- the Russian dialect writes `;` and `1,91`, the English dialect `,` and `1.91`, both write `NaN` and
  `Infinity` invariantly.

Manual smoke checklist (Post-Completion, Excel cannot run in CI):

1. Run the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), `File` -> `Export to CSV...`; the window
   opens with the visible window's edges and the switched-on pens ticked, and a row count appears.
2. Widen the end past 24 h from the start; the end field turns red, the message line reads the 24 h rule, `Save
   as...` is disabled, the window does not change size.
3. Untick a pen; the row count drops within a second.
4. Export 1 h of 8 pens to a file; the message line reports the written row count and the path.
5. Open the file by double click in Excel with the viewer's language set to Russian and Windows in ru-RU:
   columns split, `v` is numeric, `t` is a date-time, Cyrillic pen names read correctly.
6. Repeat step 5 with the viewer's language set to English and Windows in en-US.
7. Start an export of 24 h and press `Cancel`; no file is left at the chosen path.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

```
File -> Export to CSV...            MainWindowViewModel.ExportCommand
        |                             snapshot: Navigation.From/To, Pens (catalogue order, IsVisible)
        v                             -> ExportRequests
CsvExportWindow (ShowDialog)        CsvExportViewModel
  Start  [08:00:00 13.10.2026]        ExportRangeForm: parse wall-clock, 24 h rule, prefill
  End    [20:00:00 13.10.2026]        pen checklist
  Pens   [x] ... [ ] ...              live row count: IArchiveRowReader.CountRowsAsync (throttled, switched)
  ~412 000 rows
  [message line] [Save as...] [Cancel] [Close]
        |  code-behind: SaveFilePickerAsync -> path
        v
  CsvExportViewModel.ExportCommand(path)
    per pen, off the UI thread: ReadPenRowsAsync -> ArchiveCsvWriter -> <path>.partial
    progress -> uiScheduler
    success: move over <path>; cancel / failure: delete <path>.partial
```

Key decisions:

- **Source.** A new Core interface `IArchiveRowReader` returns stored rows, implemented by
  `PostgresArchiveRowReader` over the same `NpgsqlDataSource` and registered in `AddPostgresData`. It is a
  second read interface because its consumer is the export window alone and its contract differs from
  `IDataProvider`'s: rows, not envelopes, and archive wall-clock time, not UTC.
- **Wall-clock end to end.** The range bounds the operator types and the row timestamps written are the
  archive's own naive wall-clock values. Nothing on the export path converts through UTC, so the repeated
  autumn hour loses nothing: a typed bound means the same wall-clock value the archive stores. The one
  conversion is the prefill, UTC navigation edges to wall-clock in `PostgresConnectionSettings.SourceTimeZone`,
  the zone `ArchiveTimeConverter` reads, which is a function and loses nothing. This is the one declared exception to "everything above the provider is UTC".
- **Two statements per pen on one connection.** A seed probe, `WHERE id = @id AND l = 0 AND t < @from AND t >=
  @from - interval '1 day' ORDER BY t DESC LIMIT 1`, then the window, `WHERE id = @id AND l = 0 AND t >= @from AND
  t < @to ORDER BY t`. Each walks `PRIMARY KEY (id, l, t)` on its own; no `UNION ALL` gives the planner a reason
  to sort. Pens are read in the checklist's order, which gives the file its order (by pen, then by time) and the
  progress its steps. One pen's rows are materialised as `StoredArchiveRow` structs and released before the next pen.
- **Seed bound is one day.** The range is at most a day, so the chart's bound formula,
  `greatest(@to - @from, interval '1 day')`, is always one day here. The file and the chart agree on a pen's
  presence when the chart's window is within a day; a wider chart window seeds from further back, and a pen
  last written more than a day before the export's start is absent from the file.
- **24 h is a time bound.** It bounds the read and the memory. It does not guarantee Excel's 1 048 575 data
  rows. The live count shows the number before the file is chosen, and a count above that limit shows a notice
  in the message line without blocking the export.
- **The count is live.** It follows the range and the ticked pens 300 ms after the last edit; the edit itself
  cancels a count still running, so a result for the old range never shows. A failed count shows "count unavailable" and reports to the
  message panel through the mapper.
- **Dialect follows the interface language.** Russian: delimiter `;`, decimal comma. English: delimiter `,`,
  decimal point. Header names are invariant literals in both: `id`, `name`, `unit`, `t`, `v`, `q`. A
  non-finite `v` is written `NaN`, `Infinity` or `-Infinity`, as PostgreSQL spells it, in both. UTF-8 with
  BOM, CRLF line ends, RFC 4180 quoting of a field holding the delimiter, a quote or a line break.
- **Off the UI thread, progress on it.** The read and the write run with `ConfigureAwait(false)` in a helper
  the command awaits; each progress step is posted to the UI scheduler, the only thread that writes a bound
  property.
- **Progress and outcome stay in the export window.** The message line shows progress ("about" the counted
  total, since rows arrive during the export), the written row count and path on success, and the failure title
  on failure. Failures also go to the message panel; success adds no panel entry, so
  `AppStatusBarViewModel.ConnectionRestored` stays the one message with no error behind it.
- **No partial file.** The writer writes `<path>.partial`; success moves it over `<path>`, cancel and failure
  delete it. Closing the window during an export cancels it.
- **The window never touches navigation.** It is modal and snapshots `From`, `To` and the pens when the command
  runs, so it is no second writer of the navigation window.

## Technical Details

### Core

```csharp
namespace SemiPlot.Core.Data;

/// <summary>One stored archive row; Time is the archive's own wall-clock value, never converted.</summary>
public readonly record struct StoredArchiveRow(DateTime Time, double? Value, int Quality);

/// <summary>Stored main-layer rows, bounded and returned in the archive's wall-clock time.</summary>
public interface IArchiveRowReader
{
    // Rows of every pen inside [from, to) at l = 0 plus one seed row per pen that has one.
    Task<Result<long>> CountRowsAsync(
        IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default);

    // The pen's seed row first, if any, then its rows inside [from, to) at l = 0, ascending.
    Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadPenRowsAsync(
        int penId, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
```

`SemiPlot.Core/Export/CsvDialect.cs`: `sealed record CsvDialect(char Delimiter, CultureInfo NumberCulture, string
TimestampFormat)` with two static instances, `Russian` and `English`.

`SemiPlot.Core/Export/ArchiveCsvWriter.cs`: `WriteHeader(TextWriter, CsvDialect)` and `WriteRows(TextWriter,
CsvDialect, Pen, IReadOnlyList<StoredArchiveRow>)`. A line is `id;name;unit;t;v;q`: `id` from `Pen.PenId`, invariant;
`name` and `unit` from the `Pen` (unit empty when null); `t` by the dialect's `TimestampFormat` under
`CultureInfo.InvariantCulture`; a finite `v` by `double.ToString("R", NumberCulture)`, a non-finite one as
`NaN`/`Infinity`/`-Infinity`, a null one empty; `q` invariant.

ASSUMPTION: Excel in ru-RU reads `2026-10-13 08:00:00,123` as a date-time and `2026-10-13 08:00:00.123` as text.
The Russian `TimestampFormat` is `yyyy-MM-dd HH:mm:ss,fff`, the English one `yyyy-MM-dd HH:mm:ss.fff`, and
manual step 5 confirms or corrects it before the plan completes.

ASSUMPTION: one pen's day at the 100 ms floor, 864 000 `StoredArchiveRow` structs of about 24 bytes, holds about 20 MB
in the list; not measured.

### Postgres

Three constants in `ArchiveStatements.cs`, each with its own binder pinned against its parameter names, as the
existing binders are (`docs/architecture/data-integration.md:223-226`):

- `ExportSeedRow`: the seed probe above, columns `t, v, q`.
- `ExportPenRows`: the window read above, columns `t, v, q`.
- `ExportRowCount`: `count(*)` of `id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to`, plus the number of pens
  with a seed row under the one-day bound (a lateral `LIMIT 1` probe per id, counted).

`PostgresArchiveRowReader` (internal constructor, factory registration as the editor's): opens one connection
per call, binds `from` and `to` as `Timestamp` with no conversion, reads `t` with `GetDateTime`
(`DateTimeKind.Unspecified`), maps any exception through `ArchiveExceptionMapper` with relation `trends`, and
rethrows `OperationCanceledException` ahead of the mapper as `QueryHistoryAsync` does
(`PostgresDataProvider.cs:146-151`). It takes no `ArchiveTimeConverter`.

### UI

- `SemiPlot.UI/Export/ExportRangeForm.cs`: two text fields in the literal format `HH:mm:ss dd.MM.yyyy` (both
  languages), prefilled from `From`/`To` converted to wall-clock in the archive zone, the start
  floored and the end ceiled to the whole second so the range covers the view. Validity flags per field and one
  message in form order: start unreadable; end unreadable; end not after start; span over 24 h (exactly 24 h is
  valid); no pen ticked. Exposes `From`/`To` as wall-clock values while valid.
- `SemiPlot.UI/Export/CsvExportViewModel.cs`: the form, the pen checklist (every pen of the snapshot, ticked
  when `IsVisible`), the count (`WhenAnyValue` over the form's range and ticked count, `Select` into
  `Observable.Timer(300 ms, uiScheduler)` then a cancellable `CountRowsAsync` and `ObserveOn(uiScheduler)`,
  `Switch`, a `Subscribe` with an `onError` that reports, setting the count or "count unavailable"), `ExportCommand`
  (`CreateFromTask<string>`, `canExecute` = form valid and not executing), `CancelCommand` (`canExecute` =
  `IsExecuting`), the message line state, `ReportFailure`. The command body awaits a helper that reads and
  writes with `ConfigureAwait(false)` and posts each progress step through `uiScheduler`. Owns its
  `CancellationTokenSource`; `Dispose` cancels a running export and the count subscription.
- `SemiPlot.UI/Export/CsvExportWindow.axaml(.cs)`: fixed width, `SizeToContent="Height"`, `CanResize="False"`;
  the pen list is a virtualising list of fixed height; the code-behind holds the `SaveFilePickerAsync` call with
  a suggested name `semiplot_yyyyMMdd_HHmmss-yyyyMMdd_HHmmss.csv` (invariant digits) and hands
  `file.Path.LocalPath` to `ExportCommand`; a throw reaches the handler's catch.
- `SemiPlot.UI/Export/ExportFileError.cs`: an `IError` for a file the export could not write, carrying the path,
  the runtime's reason and `IsPartialFile`; a resourced arm in `ArchiveFailureMapper.Map`.
- `MainWindowViewModel`: `ExportCommand` with `canExecute` =
  `ChartViewModel.WhenAnyValue(chart => chart.HasNoPens).Select(none => !none)`, and `ExportRequests`; it builds
  each `CsvExportViewModel` through a factory `TrendWindow.Build` closes over the reader, the dialect, the UI
  scheduler and the archive zone. `DialogOpener.ShowOwnedDialog` opens the export, the pen editor and the
  settings dialog alike. `App.ConfigureStarted` selects the dialect through `CsvDialectFor(UiLanguage)`, which
  reads `SettingsVocabulary`, from `settings?.Locale ?? StartupSequence.BootstrapLocale`.

### Text

New keys in `Resources.resx` and `Resources.ru.resx`: menu item, window title, field labels, the five form
messages, the Excel notice, count unavailable, progress (`{0}` of about `{1}` rows), success (`{0}` rows written
to `{1}`), cancelled, button captions, the file-type filter name, the mapper arm's title, detail and remedy. The
CSV header names, the non-finite spellings and the field format are literals
(`docs/architecture/ui-text.md`, "What stays a literal", gets them).

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests and documentation in this repository.
- **Post-Completion** (no checkboxes): the Excel checks on a real Windows machine.

## Implementation Steps

### Task 1: Core row contract and CSV writer

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/IArchiveRowReader.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/StoredArchiveRow.cs`
- Create: `SemiPlot/SemiPlot.Core/Export/CsvDialect.cs`
- Create: `SemiPlot/SemiPlot.Core/Export/ArchiveCsvWriter.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Export/ArchiveCsvWriterTests.cs`

- [x] add `StoredArchiveRow` and `IArchiveRowReader` as in Technical Details
- [x] add `CsvDialect` with `Russian` and `English`
- [x] add `ArchiveCsvWriter.WriteHeader` and `WriteRows`, RFC 4180 quoting
- [x] write tests: header per dialect; a row per dialect (`1,91` vs `1.91`, `;` vs `,`); null value and null unit
      are empty fields; `NaN`, `Infinity`, `-Infinity` written invariantly under both dialects; a name holding
      the delimiter, a quote and a line break is quoted; `q` 0, 16, 32 written as stored; `t` written verbatim
      with milliseconds, `Kind` ignored
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ArchiveCsvWriter"`
      - must pass before task 2

### Task 2: Postgres row reader

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresArchiveRowReader.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/ArchiveRowReaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`

- [x] add `ExportSeedRow`, `ExportPenRows` and `ExportRowCount` with their binders
- [x] add `PostgresArchiveRowReader` and register it in `AddPostgresData` beside the editor
- [x] write statement-text tests: `l = 0` everywhere; strict `<` seed bound and the one-day lower bound;
      `t >= @from AND t < @to`; `ORDER BY t` and `ORDER BY t DESC LIMIT 1`; binder parameter names
- [x] write integration tests over a `ClonedArchiveTest` with rows written by the test
      (`docs/architecture/bench.md`, `CloneSource`): seed row first then window rows ascending; a pen with no
      row in the window yields its seed alone; a seed older than one day yields nothing; `t = @to` excluded;
      coarse-layer rows excluded; `q` 16/32 rows returned; rows at every wall-clock value of an autumn repeated
      hour returned with their stored `t` for a range typed across it; the count equals the rows the reads return
      on a table nothing writes to
- [x] write failure tests: a dropped `trends` fails naming `trends`; a cancelled read throws
      `OperationCanceledException` rather than failing
- [x] add `ExplainPlanTests` cases for all three statements: no sequential scan over a row-holding partition,
      the scan an index or a bitmap over one, as the existing cases allow (`ExplainPlanTests.cs:50-52`)
- [x] run the unit filter and `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj
      --filter "FullyQualifiedName~ArchiveRowReader|FullyQualifiedName~ExplainPlan"` - must pass before task 3

### Task 3: Export range form

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/ExportRangeFormTests.cs`

- [x] add `ExportRangeForm` as in Technical Details, time zone injected for the prefill only
- [x] write tests: prefill floors the start and ceils the end under a pinned zone; a 12 h range is valid;
      exactly 24 h is valid; 24 h plus one second is invalid with the 24 h message on the end field; end before
      start; unreadable text in either field; messages come in form order; the parsed values carry no conversion
- [x] run tests - must pass before task 4

### Task 4: Export view model and file error

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Create: `SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs`
- Create: `SemiPlot/SemiPlot.UI/Export/ExportPenChoice.cs`
- Create: `SemiPlot/SemiPlot.UI/Export/ExportFileError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportViewModelTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/FakeArchiveRowReader.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`

- [x] add `CsvExportViewModel`: checklist, live count, `ExportCommand`, `CancelCommand`, message line,
      `<path>.partial` then move, delete on cancel and failure, `Dispose` cancels
- [x] add `ExportFileError` and its mapper arm with resourced text in both languages
- [x] write tests over a fake `IArchiveRowReader`, a `TestScheduler` and `TemporaryDirectory`: pens ticked from
      `IsVisible`; the count follows a range edit after the throttle and a superseded count is cancelled; a count
      above 1 048 575 shows the notice; export writes the header and every pen's rows in checklist order and
      reports the written row count and path; progress changes only after the scheduler advances
- [x] write tests for the error paths: a failed count shows "count unavailable" and adds one panel entry; cancel
      mid-export leaves neither the file nor `.partial`; a reader failure leaves neither, shows the title in the
      message line and adds one panel entry; an unwritable path maps to `ExportFileError`; `Dispose` during an
      export cancels it; a pre-existing file at the path survives a failed export
- [x] run tests - must pass before task 5

### Task 5: Export window

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Create: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`

- [x] lay out the window under the form rule; compiled bindings, `x:DataType` on the root and the pen template;
      every colour from `Palette.axaml`
- [x] code-behind: `Save as...` runs `SaveFilePickerAsync` with the suggested name and passes the local path to
      `ExportCommand`; the `async void` handler is wrapped in try/catch and reports through
      `CsvExportViewModel.ReportFailure`
- [x] measure the width in both languages with Skia and HarfBuzz as `docs/architecture/ui-theme.md:200-205`
      records for the settings dialog, and write the chosen width into this plan for Task 8
- [x] write headless tests: `TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` (window and `Save as...`
      `Bounds` equal before and after); the invalid field carries the `invalid` class; `Save as...` is disabled
      while invalid; typing a valid range through `HeadlessInput` re-enables it; the suggested file name under
      `ru` and `en` UI cultures
- [x] run tests - must pass before task 6

**Measured width, for Task 8 (`ui-theme.md`):** the export window is 800 px wide, `SizeToContent="Height"`,
433 px high, 20 px margin, 10 px between rows, a 180 px pen list. Measured on 2026-10-13 with Skia and
HarfBuzz (real Win32 platform, Semi theme, Inter 14 px, a scratch program, deleted after), every field valid:

| | English | Russian |
| --- | --- | --- |
| `Save as...` / `Cancel` / `Close` buttons | 90 / 73 / 64 px | 145 / 82 / 88 px |
| Message cell beside the three buttons at 800 px | 505 px | 417 px |
| Longest form message on one line | 446 px (start unreadable) | 524 px (start unreadable) |
| Excel notice on one line | 575 px | 702 px |
| Success line with a 70-character path on one line | 712 px | 766 px |

At 560 px the English cell was 265 px and the Excel notice and the success line did not fit two lines; at 720 px
the Russian cell was 337 px and the Excel notice (702 px) and the success line (766 px) did not fit two lines
(capacity under 674 px). At 800 px every message fits one or two lines in both languages; the form messages stay
two lines at most. With the start field unreadable and with a 49 h span, the realised window kept 800 x 433 px
in both runs. Headless tests use the test font (about twice as wide), so no test asserts the wrapping; the
size gate is `CsvExportWindowTests.TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`.

### Task 6: Menu entry and wiring

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/TrendWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/TrendWindowBuildTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/EmptyCatalogueStartupTests.cs`

- [x] add `File` -> `Export to CSV...` between `New window` and `Exit`, bound to `ExportCommand`
- [x] add `ExportCommand` and `ExportRequests` to `MainWindowViewModel`; `DialogOpener.ShowOwnedDialog` opens
      the window with `ShowDialog` and disposes the view model in `finally`
- [x] `TrendWindow.Build` resolves `IArchiveRowReader` and takes the dialect; `App.ConfigureStarted` selects it
      through `CsvDialectFor`
- [x] write tests: the request carries `From`/`To` and the pens with their visibility at the moment of the
      command; the command is disabled while `HasNoPens` and enabled after a catalogue with a pen;
      `CsvDialectFor` maps `Ru` and `En` and the missing-settings default; `AppMenuBarTests` passes with the new
      leaf
- [x] run tests - must pass before task 7

### Task 7: Verify acceptance criteria

- [x] verify every requirement in Overview is implemented
- [x] verify the edge cases: no row in range, seed only, repeated autumn hour, cancel, failure, failed count,
      zero pens ticked
- [x] run full test suite: `dotnet test SemiPlot.slnx`
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
- [x] `git grep -l "IArchiveRowReader" -- SemiPlot/SemiPlot.UI/Chart SemiPlot/SemiPlot.UI/Legend` prints nothing

### Task 8: [Final] Update documentation

- [x] `docs/architecture/data-integration.md`: the reader in "The provider surface", the three statements in
      "Operation to statement", and in "Time boundary" the export path as the one place that stays in archive
      wall-clock time end to end
- [x] `docs/architecture/overview.md`: a section for the export window (snapshot, modal, live count, outcome in
      the window, no partial file)
- [x] `docs/architecture/ui-text.md`: the CSV header, the non-finite spellings and the field format under "What
      stays a literal", the export window's text section
- [x] `docs/architecture/ui-theme.md`: the export window's measured width beside the settings dialog's
- [x] `docs/architecture/trend-feature-spec.md` MS-6 and `docs/architecture/trend-interaction.md:98`: CSV value
      export is shipped; snapshot and print stay `[LATER]`
- [x] `AGENTS.md`: `IArchiveRowReader` beside `IPenCatalogueEditor` in "Data-source projects"
- [ ] ⚠️ move this plan to `docs/plans/completed/` (not moved; left for the delivery step)

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification**

- The smoke checklist in Acceptance Evidence, on a Windows machine with Excel, in ru-RU and en-US. Step 5
  confirms or corrects the Russian timestamp format ASSUMPTION; a correction goes back into `CsvDialect` and its
  tests before the plan moves to `completed/`.
- One export of a full 24 h on the customer archive, timed, with the process's peak working set, to replace the
  memory ASSUMPTION with a measurement in `docs/architecture/scada-archive-observed.md`.

**Executed by exec:**
- branch: csv-export

## Verify it yourself

The feature did not exist before `5b6e556`; every check below fails on `master` because there is no export.

Automated, from the repository root of the branch (the integration line needs Docker):

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~Export"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReader|FullyQualifiedName~ExplainPlan"
dotnet test SemiPlot.slnx
```

At `3b74352` these gave 1711/1711 unit and 150/150 integration in one full run. Two unrelated tests time out
under machine load and pass alone: `ChartHistoryRequestDebouncerTests.RequestReturnsWhileAPacedAdmissionIsStartingItsQuery`
and `AppSectionWatcherTests.AnAppFolderDeletedAndRecreatedIsWatchedAgain`.

What each behaviour rests on:

- A steady pen still appears: `ArchiveRowReaderTests.APenWithNoRowInTheWindowYieldsItsSeedAlone`.
- The autumn repeated hour loses nothing: `ArchiveRowReaderTests.EveryRowOfTheRepeatedAutumnHourComesOutWithItsStoredTime`.
  It proves no UTC conversion happens on the export path; it would pass for any range, by construction.
- No file is left after cancel or failure, and a file at the path survives a failed export:
  `CsvExportViewModelTests.Cancelling_MidExport_LeavesNeitherTheFileNorThePartial` and its siblings.
- Closing the window cancels a running export:
  `MainWindowViewTests.ExportToCsv_ClickedOnTheRealisedWindow_OpensTheExportOverIt_AndClosingItCancelsTheExport`.
- The window never resizes on validation: `CsvExportWindowTests.TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`.

Manual, on Windows with Excel; no test can drive these:

1. `dotnet run --project SemiPlot/SemiPlot.AppHost`, then `File` -> `Export to CSV...`: the fields hold the
   visible window's edges as `HH:mm:ss dd.MM.yyyy`, the switched-on pens are ticked, a row count appears.
2. Set the end more than 24 h after the start: the end field turns red, the message line states the 24 h rule,
   `Save as...` is disabled, the window keeps its size.
3. Untick a pen: the count drops within a second.
4. Export 1 h of a few pens: the message line reports the written row count and the path; no `.partial` is left.
5. With the viewer's language Russian and Windows in ru-RU, open the file by double click: columns split on `;`,
   `v` is a number, `t` is a date-time, Cyrillic names read correctly. This step decides the Russian
   `TimestampFormat` ASSUMPTION (`yyyy-MM-dd HH:mm:ss,fff`); if `t` lands as text, correct `CsvDialect.Russian`.
6. Repeat step 5 with English and en-US: delimiter `,`, decimal point.
7. Start a 24 h export and press `Cancel`: no file at the chosen path.
