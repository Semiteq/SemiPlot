# CSV export rework: one time-ordered table, a date and time form

## Overview

- The CSV export of issue #73 writes one table of three columns, pen, time and value, ordered by time across
  every ticked pen, with rows of different pens interleaved. The `id` and `q` columns go; no marker of any kind
  replaces `q`.
- The header is operator text in the interface language: `Перо;Время;Значение` in Russian, `Pen,Time,Value` in
  English. The pen field is `name, unit`, or the name alone for a pen without a unit; an id is added only when
  two ticked pens share a name.
- The read path reads the range in bounded time slices, one statement per slice, so neither one statement nor
  the process holds a whole day of every pen.
- The export window takes the start and the end as a date picker plus a typed `HH:mm:ss` time each, shows a
  bordered pen list with a live "3 of 8" header, puts the message on a full-width row above the buttons, and is
  narrower.
- Timestamp format, decimal separator, BOM, CRLF, the `.partial` file and the Excel row-limit notice keep their
  current behaviour.

## Context (from discovery)

What exists on branch `csv-export` (`docs/plans/20261013-csv-export.md` describes the current implementation):

- The reader contract is `CountRowsAsync(penIds, from, to)` and `ReadPenRowsAsync(penId, from, to)`
  (`SemiPlot/SemiPlot.Core/Data/IArchiveRowReader.cs:8-14`); a row is
  `StoredArchiveRow(DateTime Time, double? Value, int Quality)` with no pen id
  (`SemiPlot/SemiPlot.Core/Data/StoredArchiveRow.cs:4`).
- `PostgresArchiveRowReader.ReadPenRowsAsync` runs `ExportSeedRow` then `ExportPenRows` for one pen on one
  connection and materialises the pen's rows in one list (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresArchiveRowReader.cs:61-97`).
  The statements select `t, v, q` (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:258-271`);
  `ExportRowCount` counts `id = ANY(@ids)` over the whole range plus one lateral seed probe per pen in one
  statement (`ArchiveStatements.cs:277-292`). Three binders, `BindSeed`, `BindWindow` and `BindCount`
  (`PostgresArchiveRowReader.cs:99-120`); `BindCount` binds `ids`, `from`, `to`.
- `CsvFileExporter` writes the header, then pen after pen in checklist order, reporting the running total after
  each pen (`SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs:79-98`), into a BOM-carrying `<path>.partial`
  (`:74-77`) moved over `<path>` on success (`:34-38`).
- `ArchiveCsvWriter` writes the literal header `id`, `name`, `unit`, `t`, `v`, `q`
  (`SemiPlot/SemiPlot.Core/Export/ArchiveCsvWriter.cs:12-16`) and one line per row with the pen's id, name and
  unit repeated (`:18-33`); `Quote` applies RFC 4180 quoting against the dialect's delimiter (`:55-60`).
- `CsvDialect(char Delimiter, CultureInfo NumberCulture, string TimestampFormat)` has two static instances
  (`SemiPlot/SemiPlot.Core/Export/CsvDialect.cs:5-9`). `SettingsVocabulary.Languages` stores them per language
  (`SemiPlot/SemiPlot.UI/Startup/AppSettings.cs:23-28`, `:37-51`), `App.CsvDialectFor` returns the stored
  instance (`SemiPlot/SemiPlot.UI/App.axaml.cs:242-245`) and `ConfigureStarted` passes it to `TrendWindow.Build`
  (`App.axaml.cs:134-139`). `AppConfigurationTests` asserts `BeSameAs(CsvDialect.Russian)` and
  `BeSameAs(CsvDialect.English)` (`SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs:61-66`).
  Six test sites pass `CsvDialect.English`: `TrendWindowBuildTests.cs:56`, `:86`, `MainWindowTestBuilder.cs:142`,
  `TrendWindowTests.cs:72`, `EmptyCatalogueStartupTests.cs:39`, `CsvExportStand.cs:31`.
- `SemiPlot.Core` cannot read `SemiPlot.UI` resources. The generated accessor exposes `Resources.ResourceManager`,
  which tests already read with an explicit culture (`SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs:135`).
- `docs/architecture/ui-text.md:88-92` lists the header names among the literals that belong to the file, together
  with the range fields' format `HH:mm:ss dd.MM.yyyy`.
- Pen names are not unique: `semiplot_tags.name text NOT NULL` carries no `UNIQUE`
  (`docs/plans/completed/20260917-pen-catalogue-and-groups.md:106-108`), and the catalogue orders shared names by
  `name, id` (`docs/architecture/data-integration.md:212`, `:232`). `Pen(int PenId, string Name, ..., string? Unit, ...)`
  (`SemiPlot/SemiPlot.Core/Trends/Pen.cs:3-14`).
- The ticked pens keep the checklist's catalogue order (`SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs:138-141`).
- The `semiplot` role runs under `statement_timeout = 30 s` (`docs/architecture/postgres-instance.md:46`; the bench
  pins it, `SemiPlot/SemiPlot.Tests.Integration/SeededArchiveTests.cs:149`). ASSUMPTION: a statement stays open,
  and its timeout keeps counting, until its last row is sent, so a single streamed `ORDER BY t` statement would tie
  its duration to how fast the file is written.
- Density floor: the main layer's archiving interval goes down to 100 ms (`docs/architecture/scada-archive.md:247-249`),
  and the anchor row sits one poll tick before a change (`scada-archive.md:279-281`), so a pen writes at most 10
  rows per second, 864 000 a day. The observed project writes about 670 000 rows a day for 16 variables
  (`docs/architecture/scada-archive-observed.md:546`).
- Nothing in the repository times `ExportRowCount`: its `ExplainPlanTests` case reads the plan shape only
  (`SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs:282-303`).
- `ExplainPlanTests` forbids a sequential scan of a day partition (`ExplainPlanTests.cs:40`), requires an index or a
  bitmap reach (`:51-52`), and only the bucketed case's regex admits a `Sort` node (`:61-63`). The export window
  case (`:260-280`) explains `ExportPenRows`.
- The bench seeder generates 50 pens (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticPenCatalog.cs:38-78`, groups
  of 16, 16, 10, 4 and 4) at any whole-millisecond interval (`--change-seconds`,
  `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs:55-59`, `RawLayerGenerator.cs:100-105`) into a
  `semibase bench`-provisioned database that carries no rows (`AGENTS.md:57-65`). It materialises every row as an
  `ArchiveRow` record class (`ArchiveRow.cs:3`), raw rows plus `LayerThinner.ThinAll` of them, in one array
  (`SeedFiller.cs:13-14`), and writes them with one `COPY` inside one transaction (`ArchiveWriter.cs:61-71`, `:102`).
  At 43.2 million rows a day for 50 pens at 100 ms that array alone is several gigabytes of objects, so the
  measurement fills its archive in SQL instead. The day partition DDL is `PartitionScript.CreateStatement`
  (`PartitionScript.cs:29-30`); the archive's columns are `(id, l, t, v, q)` (`ArchiveWriter.cs:27`).
- The demo stand is no measurement bench: `SemiPlot.AppHost` blocks in `stand.Run()` (`AppHost.cs:67`), runs
  `converge`, the follow writer and the viewer (`:42-60`), and stopping it removes the container
  (`docs/architecture/bench.md:200-202`). A standalone bench container is built and run as in
  `docs/architecture/scada-archive-observed.md:624-629`, with the superuser password `semibase-container-superuser`,
  the plot role password `semibase-container-plot` and `SEMIPLOT_PROVISIONED_DATABASE=semiplot_provisioned`.
- Range form: two text fields in `HH:mm:ss dd.MM.yyyy` parsed under the invariant culture
  (`SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs:24`, `:106-112`), prefilled from the UTC edges in the archive's
  zone (`:28-32`); problems in form order `StartUnreadable`, `EndUnreadable`, `EndNotAfterStart`, `SpanOverLimit`,
  `NoPenTicked` (`:8-16`). `ExportRangeProblemText` passes `FieldFormat` into the two unreadable messages
  (`SemiPlot/SemiPlot.UI/Export/ExportRangeProblemText.cs:11-14`).
- Culture: startup writes `CultureInfo.DefaultThreadCurrentUICulture` and nothing else
  (`SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs:47-50`), so `CurrentCulture` is the machine's; numbers and
  timestamps the operator reads follow `CurrentCulture` by rule (`docs/architecture/ui-text.md:39-45`), and the axis
  panel and the pen editor parse bounds under it (`ui-text.md:266-269`).
- `CalendarDatePicker` in Avalonia 12.0.5, read from `ilspycmd -t Avalonia.Controls.CalendarDatePicker` over
  `Avalonia.Controls.dll` (line numbers of that decompile):
  - `SelectedDate` is `DateTime?`, default null, two-way by default (`:515`); `PART_TextBox` is a required
    template part (`:24`).
  - Typed text commits on lost focus (`:821`) or Return (`:1119-1121`); empty text commits a null date
    (`:1149-1152`); text that does not parse restores the previous date and its text (`:1177-1182`). So no gesture
    leaves an unreadable date: a date is present or missing.
  - With `SelectedDateFormat="Custom"` and a `CustomDateFormatString`, it parses with
    `DateTime.ParseExact(text, CustomDateFormatString, ...)` (`:1079`) and displays with the same string
    (`:1110`); otherwise `DateTime.Parse` and the short date pattern. Both read
    `DateTimeHelper.GetCurrentDateFormat()`, which returns `CultureInfo.CurrentCulture.DateTimeFormat` for a
    Gregorian culture (`ilspycmd -t Avalonia.Controls.DateTimeHelper`, `:77-82`). `FirstDayOfWeek` is taken from the
    same culture (`:535`). A parse failure raises `DateValidationError`, whose `ThrowException` defaults to false.
- Window: 800 px wide (`SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml:12`), two `TextBox`es `ExportStart` and
  `ExportEnd` (`:33-48`), a bold `ExportPensHeader` (`:50-54`), a 180 px virtualising `ListBox` (`:56-73`), the
  message cell left of the three buttons (`:83-123`). `Styles/Forms.axaml` paints `invalid` on `TextBox` and
  `NumericUpDown` only (`SemiPlot/SemiPlot.UI/Styles/Forms.axaml:5-11`); `AppBorderBrush` is a palette key
  (`SemiPlot/SemiPlot.UI/Styles/Palette.axaml:83`).
- The success line carries the full path (`CsvExportViewModel.cs:233`; `Resources.resx:687-688`). Measured buttons
  in Russian are 145, 82 and 88 px, the Russian Excel notice 702 px on one line
  (`docs/architecture/ui-theme.md:241`, `:244`). The axis scale panel puts its message line on its own row above
  its buttons (`ui-theme.md:259-262`).
- Tests that drive the current shape: `CsvExportWindowTests.cs:54-55`, `:81-82`, `:106-107`, `:111-112` type into the
  two text fields; `CsvExportViewModelTests.cs:217-223` asserts the six-column file and the full path in the
  success line, `:270` the same path; `FakeArchiveRowReader.cs:16-17`, `:29-35` serves `ReadPenRowsAsync`, and
  `MainWindowViewTests.cs:88-97` sets its `OnRead`; `ArchiveRowReaderTests.cs:104-113` reads `Quality`;
  `ArchiveStatementTextTests.cs:42-105` pins `ExportPenRows` and the three binders.

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
- the file format changes on purpose; no reader of the old six-column file exists in this repository

## Testing Strategy

- **unit tests** (`SemiPlot.Tests.Unit`): the writer, the pen field and its collision rule, the dialect wiring, the
  statement text and binders, the range form, the export view model over the fake reader, a `TestScheduler` and a
  temporary directory, the realised export window headless.
- **integration tests** (`SemiPlot.Tests.Integration`, container): the seed and slice reads against a cloned
  archive, the tie order, the plan of the slice statement in `ExplainPlanTests`.
- No e2e suite exists beyond the journeys; the export adds no journey.
- Every test class carries the three traits (`AGENTS.md`, Test traits). Headless gestures go through
  `HeadlessInput` (`AGENTS.md`, Test); the date picker is driven through its `PART_TextBox` with `Clear`, `Type`
  and `Press`, so no new gesture helper appears. A test that reads a `Resources` value under the thread culture
  joins `ProcessGlobalStateCollection` (`docs/architecture/ui-text.md:327-331`).

## Acceptance Evidence

The defect is the current file shape and form shape: the new tests below fail at `c4ba462` because the file has
six columns in per-pen blocks and the form has no date picker. Each command must pass.

Header per language, pen field with unit, id only on a name collision, no `id` or `q`:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ArchiveCsvWriterTests|FullyQualifiedName~AppConfigurationTests"
```

Seeds first, then slices, contiguous slice bounds, progress per slice, no file after cancel or failure:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~CsvExportViewModelTests|FullyQualifiedName~MainWindowViewTests"
```

Statement text and binders:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ArchiveStatementTextTests"
```

Interleaved order, tie order by checklist, seeds by time, autumn hour, count equals reads, and the slice plan
(container required):

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReaderTests|FullyQualifiedName~ExplainPlanTests"
```

Date and time fields, missing date, unreadable time, 24 h rule, invalid borders, fixed size, list, header count,
tooltip:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ExportRangeFormTests|FullyQualifiedName~CsvExportWindowTests"
```

Everything:

```powershell
dotnet test SemiPlot.slnx
```

The assertions that carry the change:

- English header `Pen,Time,Value`, Russian `Перо;Время;Значение`, read from the set of the dialect's language even
  while the thread's UI culture is the other one;
- `"Damper 01, %"` (English, quoted) and `Damper 01, %` (Russian); `Damper 01 (17)` and `Damper 01, % (17)` only when
  two ticked pens are named `Damper 01`; an unticked namesake adds no id;
- rows of two pens with equal `t` come out in the checklist order, and reversing the checklist reverses them;
- every slice call spans at most `ArchiveRowSlices.Width`, the slices tile `[from, to)` with no gap and no
  overlap, and the seeds come before the first slice;
- a cleared start date shows the missing-date message and marks the start date picker; `25:00:00` in the end time
  shows the time-format message and marks the end time box; neither changes the window's `Bounds`;
- the pen list's inner `ScrollViewer` has `AllowAutoHide` false, and the header reads `Resources.FormatExportPensHeader`
  of the ticked and total counts after each click.

Manual Excel checklist (Post-Completion, Excel cannot run in CI):

1. With the viewer in Russian and Windows in ru-RU, export 1 h of three pens, one with a unit, and open the file by
   double click: the header reads `Перо`, `Время`, `Значение` in three columns.
2. The rows of the three pens are interleaved by time, not grouped by pen.
3. A pen with a unit reads `name, unit` in one cell, for example `Damper 01, %`.
4. The value column is numeric with a decimal comma.
5. The time column is a date-time Excel can sort and format, not text.
6. Repeat steps 1-5 with the viewer in English and Windows in en-US: `Pen`, `Time`, `Value`, the pen cell holds its
   comma inside quotes, decimal point.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

```
CsvExportWindow (Width ~520, SizeToContent=Height)
  Start  [13.10.2026 v] [08:00:00]          ExportRangeForm: StartDate, StartTimeText, EndDate, EndTimeText
  End    [13.10.2026 v] [20:00:00]
  Pens (2 of 3)                              MultiBinding: Form.TickedPenCount, Pens.Count
  +------------------------------+
  | [x] Chamber pressure         |           bordered ListBox, MaxHeight, scroll bar never auto-hidden
  | [ ] Argon flow               |
  +------------------------------+
  ~412 000 rows
  [message line, full width, two reserved lines]
                       [Save as...] [Cancel] [Close]

CsvExportViewModel.ExportCommand(path)
  CsvFileExporter, off the UI thread, into <path>.partial
    header (dialect.Header)
    ReadSeedRowsAsync(ids, from)              per-pen probe, sorted by (t, checklist index)  -> write, report
    for each slice of ArchiveRowSlices.Tile(from, to), 15 min each:
      ReadSliceAsync(ids, a, b)               one statement, ORDER BY t, array_position(@ids, id) -> write, report
  move over <path>; message line: "N rows written to <file name>", tooltip: full path
```

Key decisions:

- **One table by time.** Seeds first, each pen's last `l = 0` row before `from` within one day, ordered by `t` with
  the checklist order breaking ties; every seed is older than `from`, so it precedes every slice row. Then the
  slices in ascending order, each ordered by `t` and the checklist order. Consecutive slices are disjoint and
  ascending, so the whole file is ordered by `(t, checklist index)`.
- **Bounded slices, fully materialised.** Each slice is one statement that returns all its rows before the
  exporter writes any of them, so a statement's duration depends on the server alone and never on the disk the
  file goes to, and the process holds one slice at a time. The count is one statement over the whole range unless
  Task 3 measures it over 10 s; then it is one statement per slice (Task 3, the count branch).
- **Slice width 15 minutes.** At the density floor a pen writes 10 rows per second, 9 000 rows per 15 minutes. One
  slice is 144 000 rows for 16 pens and 450 000 rows for 50 pens. At about 32 bytes per `StoredArchiveRow`
  (ASSUMPTION: CLR sequential layout of `int`, `DateTime`, `double?`; not measured) that is 4.6 MB and 14.4 MB of
  list content, at most twice that while `List<T>` grows: under 30 MB for 50 pens. One hour would be 58 MB of
  content for 50 pens. A 24 h range is 96 slice statements plus one seed probe per pen. ASSUMPTION: a 450 000-row
  slice, an index range read plus a sort by `(t, array_position)`, finishes in a few seconds, far below 30 s;
  Task 3 measures it. ASSUMPTION: `ExportRowCount` over 24 h of 50 pens at the density floor, 43.2 million rows
  counted in one statement, completes under `statement_timeout`; Task 3 measures it too.
- **Two reader members replace `ReadPenRowsAsync`.** The seed probe stays one statement per pen
  (`ExportSeedRow`), so it gets its own member; the slice is one statement over every ticked id, and the reader
  runs one statement per call. The slice width and the tiling of a range live in one place,
  `SemiPlot.Core/Export/ArchiveRowSlices`, which the exporter reads and which the count branch of Task 3 reuses.
- **No `q`.** The file carries no quality and no marker, so `StoredArchiveRow` loses `Quality` and the statements
  stop selecting `q`; the row gains `PenId` because a slice mixes pens.
- **Header is operator text.** `CsvDialect` carries a `CsvHeader(Pen, Time, Value)`. Core keeps the delimiter, number
  culture and timestamp rule per language as two factories, `CsvDialect.Russian(CsvHeader)` and
  `CsvDialect.English(CsvHeader)`; `App.CsvDialectFor` fills the header from the resx set of the dialect's language
  through `Resources.ResourceManager.GetString(key, entry.UiCulture)`, so the header and the delimiter always come
  from one language whatever the thread's UI culture is.
- **Pen field.** `name, unit`, or `name` when `Pen.Unit` is null or empty. When two or more pens of the export's
  ticked set share a name under ordinal comparison, each of those pens' field gets ` (id)` appended:
  `Damper 01, % (17)`, or `Damper 01 (17)` without a unit. Pens whose name is unique among the ticked set carry no
  id. The field is quoted by the dialect's rule, so the English file writes `"Damper 01, %"`.
- **Date picker plus typed time.** Each bound is a `CalendarDatePicker` and a `TextBox` in `HH:mm:ss`. The picker
  runs with `SelectedDateFormat="Custom"` and `CustomDateFormatString` from the resx key `ExportDateFormat`:
  `dd.MM.yyyy` in Russian, `yyyy-MM-dd` in English, the date half of the English file timestamp. Both patterns are
  digits and literal separators (`.` and `-` are literals in a .NET custom format, `/` would not be), so the
  machine's culture decides neither the shape nor the parse of the text, and the shape follows the interface
  language. `CurrentCulture` stays the machine's: setting it from `locale` would move the decimal separator of
  every pen reading and of the axis panel's parse (`ui-text.md:39-45`, `:266-269`). The picker's popup month
  names and first day of week follow the machine's culture, as every other date and number on screen does.
- **Problems.** A date is present or missing; a time is readable or not. Form order: `StartDateMissing`,
  `StartUnreadable` (time), `EndDateMissing`, `EndUnreadable` (time), `EndNotAfterStart`, `SpanOverLimit`,
  `NoPenTicked`. `EndNotAfterStart` and `SpanOverLimit` mark both end controls, because either one can be the
  cause.
- **Layout.** The message line moves to its own full-width row above a right-aligned button row, as in the axis
  scale panel. The success line names the file; the full path is the message line's tooltip while the success
  line shows. The pen list is bordered with `AppBorderBrush`, sizes to its rows up to a `MaxHeight` of eight 28 px
  rows, and never auto-hides its scroll bar. Its header reads "Pens (2 of 3)" from a `MultiBinding`, pure view logic
  that nothing in the view model consumes.

## Technical Details

### Core

```csharp
namespace SemiPlot.Core.Data;

/// <summary>One stored archive row; Time is the archive's own wall-clock value, never converted.</summary>
public readonly record struct StoredArchiveRow(int PenId, DateTime Time, double? Value);

/// <summary>Stored main-layer rows, bounded and returned in the archive's wall-clock time.</summary>
public interface IArchiveRowReader
{
	// penIds holds distinct catalogue ids (semiplot_tags.id is the primary key). ExportRowCount counts seeds over
	// DISTINCT unnest(@ids) while ReadSeedRowsAsync probes once per element, so a repeated id would make the
	// count and the file differ.

	// Rows of every pen inside [from, to) at l = 0 plus one seed row per pen that has one.
	Task<Result<long>> CountRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default);

	// Per pen its last l = 0 row before from, not older than one day; ordered by time, ties in penIds order.
	Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSeedRowsAsync(
		IReadOnlyList<int> penIds, DateTime from, CancellationToken cancellationToken = default);

	// Every l = 0 row of the pens inside [from, to) in one statement; ordered by time, ties in penIds order.
	Task<Result<IReadOnlyList<StoredArchiveRow>>> ReadSliceAsync(
		IReadOnlyList<int> penIds, DateTime from, DateTime to, CancellationToken cancellationToken = default);
}
```

The id precondition rests on `semiplot_tags.id integer PRIMARY KEY`
(`docs/plans/completed/20260917-pen-catalogue-and-groups.md:106-107`) and on `DISTINCT unnest(@ids)` in
`ExportRowCount` (`ArchiveStatements.cs:283`); the ticked pens are distinct catalogue pens
(`CsvExportViewModel.cs:138-141`).

`SemiPlot.Core/Export/ArchiveRowSlices.cs`:

```csharp
public static class ArchiveRowSlices
{
	public static readonly TimeSpan Width = TimeSpan.FromMinutes(15);

	// [from, to) as consecutive [a, min(a + Width, to)) slices, no gap, no overlap; empty when to <= from.
	public static IEnumerable<(DateTime From, DateTime To)> Tile(DateTime from, DateTime to);
}
```

`SemiPlot.Core/Export/CsvDialect.cs`:

```csharp
public sealed record CsvHeader(string Pen, string Time, string Value);

public sealed record CsvDialect(char Delimiter, CultureInfo NumberCulture, string TimestampFormat, CsvHeader Header)
{
	public static CsvDialect Russian(CsvHeader header);  // ';', decimal comma, "yyyy-MM-dd HH:mm:ss,fff"
	public static CsvDialect English(CsvHeader header);  // ',', invariant, "yyyy-MM-dd HH:mm:ss.fff"
}
```

`SemiPlot.Core/Export/ArchiveCsvWriter.cs`:

- `WriteHeader(TextWriter writer, CsvDialect dialect)`: the three header strings, each quoted by the dialect's rule.
- `PenFields(IReadOnlyList<Pen> pens, CsvDialect dialect)`: `IReadOnlyDictionary<int, string>`, pen id to the quoted
  pen field, the collision rule applied over `pens`.
- `WriteRows(TextWriter writer, CsvDialect dialect, IReadOnlyDictionary<int, string> penFields,
  IReadOnlyList<StoredArchiveRow> rows)`: one line per row, `penField<d>time<d>value`, CRLF. Time and value keep
  their current rules (`ArchiveCsvWriter.cs:26`, `:35-53`).

### Postgres

- `ExportSeedRow` selects `t, v` (no `q`); the rest unchanged.
- `ExportSliceRows`, new:

  ```sql
  SELECT id, t, v
  FROM trends
  WHERE id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to
  ORDER BY t, array_position(@ids, id);
  ```

- `ExportPenRows` and `BindWindow` are deleted. `BindCount` becomes `BindPensRange(NpgsqlCommand, int[] penIds,
  DateTime from, DateTime to)`, the binder of both `ExportRowCount` and `ExportSliceRows`, which take the same three
  parameters.
- `ReadSeedRowsAsync`: one connection, one `ExportSeedRow` probe per id in `penIds` order, then a stable
  `OrderBy(row => row.Time)` over the collected rows, so equal times keep the `penIds` order.
- `ReadSliceAsync`: one command over `_dataSource`, read whole into a `List<StoredArchiveRow>`, returned after the
  reader closes.
- Both keep the existing failure shape: `OperationCanceledException` rethrown, any other exception mapped with the
  relation `trends` (`PostgresArchiveRowReader.cs:89-96`, `:138-144`).

### UI

- `CsvFileExporter`: `ExportAsync(path, from, to, pens, reportRowsWritten, cancellationToken)` keeps its signature and
  its `.partial` handling; the body writes the header, computes `PenFields` once, reads and writes the seeds, then
  each slice of `ArchiveRowSlices.Tile(from, to)`, checking the token and reporting the running total after the
  seeds and after each slice. Its summary (`CsvFileExporter.cs:17`, "the total after each pen") says the total
  after the seeds and after each slice.
- `ExportRangeForm`: `DateTime? StartDate`, `string StartTimeText`, `DateTime? EndDate`, `string EndTimeText`,
  `int TickedPenCount`; flags `IsStartDateInvalid`, `IsStartTimeInvalid`, `IsEndDateInvalid`, `IsEndTimeInvalid`,
  `IsValid`; `Problem`; `From`, `To`. `internal const string TimeFieldFormat = "HH:mm:ss"` replaces `FieldFormat`;
  a time parses through `TimeOnly.TryParseExact(text.Trim(), TimeFieldFormat, CultureInfo.InvariantCulture,
  DateTimeStyles.None, out var time)`, and a bound is `date.Date + time.ToTimeSpan()`. The prefill sets each date
  to the wall-clock value's `.Date` and each time to its `HH:mm:ss`, after the floor and the ceiling to the second.
- `ExportRangeProblem`: `None`, `StartDateMissing`, `StartUnreadable`, `EndDateMissing`, `EndUnreadable`,
  `EndNotAfterStart`, `SpanOverLimit`, `NoPenTicked`.
- `CsvExportViewModel`: `MessageToolTip` (`string?`), the full path while the line shows a success and no form
  problem, null otherwise; the success text takes `Path.GetFileName(path)`.
- `App.CsvDialectFor(UiLanguage locale)`: `var entry = SettingsVocabulary.Of(locale)`, the three header keys read
  with `entry.UiCulture`, `return entry.CsvDialect(header)`. `UiLanguageEntry.CreateCsvDialect` becomes
  `Func<CsvHeader, CsvDialect>`, filled with the method groups `CsvDialect.Russian` and `CsvDialect.English`.

### Text

| Key | English | Russian |
| --- | --- | --- |
| `ExportCsvHeaderPen` (new) | Pen | Перо |
| `ExportCsvHeaderTime` (new) | Time | Время |
| `ExportCsvHeaderValue` (new) | Value | Значение |
| `ExportDateFormat` (new) | yyyy-MM-dd | dd.MM.yyyy |
| `ExportMessageStartDateMissing` (new) | Start: pick a date. | Начало: выберите дату. |
| `ExportMessageEndDateMissing` (new) | End: pick a date. | Конец: выберите дату. |
| `ExportMessageStartUnreadable` (reworded) | Start time: write it as {0}. | Время начала: запишите как {0}. |
| `ExportMessageEndUnreadable` (reworded) | End time: write it as {0}. | Время конца: запишите как {0}. |
| `ExportPensHeader` (now a format) | Pens ({0} of {1}) | Перья ({0} из {1}) |
| `ExportSuccess` (`{1}` is a file name) | {0} rows written to {1} | Записано строк: {0}, файл {1} |

The final wording is settled in the resx; `ResourcesTests` gates the placeholder indices.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests, the bench measurement and documentation in this
  repository.
- **Post-Completion** (no checkboxes): the Excel checks and the timing on the customer archive.

## Implementation Steps

### Task 1: File format pen, time, value under a localized header

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/StoredArchiveRow.cs`
- Modify: `SemiPlot/SemiPlot.Core/Export/CsvDialect.cs`
- Modify: `SemiPlot/SemiPlot.Core/Export/ArchiveCsvWriter.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresArchiveRowReader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/AppSettings.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Export/ArchiveCsvWriterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportStand.cs`, `CsvExportViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/TrendWindowBuildTests.cs`,
  `UI/MainWindow/MainWindowTestBuilder.cs`, `UI/MainWindow/TrendWindowTests.cs`,
  `UI/Startup/EmptyCatalogueStartupTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ArchiveRowReaderTests.cs`

- [x] `StoredArchiveRow(int PenId, DateTime Time, double? Value)`; `PostgresArchiveRowReader` fills `PenId` from the
      pen it reads and stops reading `q` (the statements change in Task 2)
- [x] `CsvHeader`, `CsvDialect.Header`, the `Russian(CsvHeader)` and `English(CsvHeader)` factories; the two static
      instances go
- [x] `ArchiveCsvWriter.WriteHeader`, `PenFields` with the collision rule, `WriteRows` over the pen-field map;
      `CsvFileExporter` passes `PenFields` of its ticked pens to every `WriteRows` call
- [x] resx keys `ExportCsvHeaderPen`, `ExportCsvHeaderTime`, `ExportCsvHeaderValue` in both sets;
      `UiLanguageEntry.CreateCsvDialect` as `Func<CsvHeader, CsvDialect>`; `App.CsvDialectFor` reads the header with
      `Resources.ResourceManager.GetString(key, entry.UiCulture)`
- [x] test sites that never read a written file pass `App.CsvDialectFor(UiLanguage.En)`; `CsvExportStand` builds
      `CsvDialect.English(new CsvHeader("Pen", "Time", "Value"))` so file assertions do not follow resx wording
- [x] rewrite `ArchiveCsvWriterTests`: header per dialect from a literal `CsvHeader`, quoted when a header string
      holds the delimiter; `Pressure, bar;2026-10-13 08:00:00,123;1,91` (Russian) and
      `"Pressure, bar",2026-10-13 08:00:00.123,1.91` (English); a pen without a unit writes its name alone, an empty
      unit too; `PenFields_TwoTickedPensNamedDamper01_GetTheirIds` pins `Damper 01, % (17)` and `Damper 01 (18)`
      while a third, unique name gets none; ordinal comparison (`Damper 01` and `damper 01` do not collide); a name
      holding the delimiter, a quote and a line break; every `Split(...)[4]` and `[5]` index becomes `[2]` for the
      value, and the quality test is deleted
- [x] `CsvExportViewModelTests.RowAt` (`:57-60`) becomes `RowAt(penId, second, value)` and every `SetRows` and
      `OnRead` serves rows carrying the pen they are served for, because `WriteRows` looks each row's `PenId` up in
      the pen-field map
- [x] `CsvExportViewModelTests.cs:217-221` expects `Pen,Time,Value` and the three-column lines;
      `ArchiveRowReaderTests.ABreakMarkersQualityComesOutAsStored` (`:104-113`) is deleted, since the marker rows
      themselves stay in the time assertions of `:52-61`
- [x] `AppConfigurationTests.CsvDialectFor_*`: `Ru` gives `;` and the Russian header, `En` gives `,` and the English
      header, the bootstrap locale gives the Russian one, and `CsvDialectFor(UiLanguage.En)` under a `ru` thread UI
      culture still gives the English header (the class is in `ProcessGlobalStateCollection`,
      `AppConfigurationTests.cs:28`); `ResourcesTests` asserts the Russian header values
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` and
      `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReader"`
      - must pass before task 2

### Task 2: Seed and slice reads, written as one time-ordered table

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IArchiveRowReader.cs`
- Create: `SemiPlot/SemiPlot.Core/Export/ArchiveRowSlices.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Export/ArchiveRowSlicesTests.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresArchiveRowReader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvFileExporter.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/FakeArchiveRowReader.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportViewModelTests.cs`, `CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ArchiveRowReaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`

- [x] `IArchiveRowReader` as in Technical Details: `ReadSeedRowsAsync` and `ReadSliceAsync` replace
      `ReadPenRowsAsync`
- [x] `ExportSeedRow` selects `t, v`; add `ExportSliceRows`; delete `ExportPenRows` and `BindWindow`; rename
      `BindCount` to `BindPensRange` for the count and the slice. The `ExportRowCount` summary names the deleted
      constant in `<see cref="ExportPenRows"/>` (`ArchiveStatements.cs:274`), which fails the build as CS1574 under
      `GenerateDocumentationFile` and `TreatWarningsAsErrors` (`SemiPlot/Directory.Build.props:10`, `:19`); it now
      reads "the rows `<see cref="ExportSliceRows"/>` returns over the range"
- [x] `ArchiveRowSlices.Width` and `Tile` as in Technical Details; tests: a 1 h range is four 15 min slices, a 20 min
      range ends its second slice at `to`, a range shorter than a slice is one slice, `to <= from` is none, the
      slices of a 24 h range tile it with no gap or overlap
- [x] `PostgresArchiveRowReader.ReadSeedRowsAsync` and `ReadSliceAsync` as in Technical Details
- [x] `CsvFileExporter`: seeds, then each slice of `ArchiveRowSlices.Tile(from, to)`, the token checked and the total
      reported after the seeds and after each slice; its `<summary>` at `CsvFileExporter.cs:17` names the seeds and
      the slices instead of "after each pen"
- [x] `FakeArchiveRowReader`: `OnSeeds`/`SeedCalls` and `OnSlice`/`SliceCalls` replace `OnRead`/`ReadPenIds`, each
      call recording its ids, bounds and token; `MainWindowViewTests.cs:88-97` blocks in `OnSeeds`
- [x] statement-text tests: the slice carries `l = 0`, `id = ANY(@ids)`, `t >= @from AND t < @to` and ends with
      `ORDER BY t, array_position(@ids, id);`; no export statement selects `q`; `BindPensRange` names exactly the
      parameters of both statements it binds; `BindSeed` unchanged
- [x] view-model tests: a 1 h range issues one seed call, then four slice calls whose bounds tile `[from, to)` and
      none wider than `ArchiveRowSlices.Width`, all with ids `7, 12` in checklist order; a 20 min range ends its second slice at
      `to`; the file holds the seed rows, then the slice rows, in call order; progress reaches the bound state per
      slice only after the scheduler advances; cancel during the second slice, a failed second slice, a reader that
      throws, `Dispose` during a slice: no file, no `.partial`, one panel entry where the existing tests assert one
- [x] integration tests over the provisioned clone: seeds of `[Steady, SeedOnly, Stale, Boundary]` come out
      `Boundary`, `SeedOnly`, `Steady` by time and `Stale` not at all; two pens with a seed at the same `t` come out
      in `penIds` order and reversed for reversed ids; a slice interleaves two pens by `t`, and two rows at one `t`
      follow `penIds` order, reversed for reversed ids; `t = @to` excluded; coarse rows excluded; a null value; every
      row of the autumn repeated hour through a slice typed across it; the count equals the seed rows plus the rows
      of slices tiling the range; a dropped `trends` fails both reads naming `trends`; a cancelled seed read and a
      cancelled slice read throw `OperationCanceledException`; the lock-held cancel test runs on `ReadSliceAsync`
- [x] `ExplainPlanTests`: delete `TheExportWindowPlanReachesItsRowsThroughAnIndex` with its statement; add
      `TheExportSlicePlanReachesItsRowsThroughAnIndexWithinItsDay` over `ExplainedPenIds()` and one slice of
      `ArchiveRowSlices.Width` inside one day: no sequential scan of a day partition, an index or bitmap reach,
      exactly one day partition read. The `Sort` above the scan is the statement's own `ORDER BY` and the case does not assert its absence;
      the existing regexes and cases stay as they are; the count case calls `BindPensRange`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` and
      `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReader|FullyQualifiedName~ExplainPlan"`
      - must pass before tasks 3 and 4

### Task 3: Measure the count and a slice at the density floor

Depends on Task 2 only. Tasks 4-6 do not wait for it; Task 7 does. The run never stops here and asks no one: each
threshold below selects a branch that is written out in this task.

**Files:**
- Modify: `docs/plans/20261014-csv-export-rework.md` (this plan, the measured numbers for Task 8)
- Count branch only: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`, `PostgresArchiveRowReader.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`,
  `SemiPlot/SemiPlot.Tests.Integration/ArchiveRowReaderTests.cs`, `ExplainPlanTests.cs`
- Slice branch only: `SemiPlot/SemiPlot.Core/Export/ArchiveRowSlices.cs` and the tests that name its width

- [x] build and start a standalone bench container from the recipe of `scada-archive-observed.md:624-629`, without
      its `-c` logging options (`:49-52`; `log_statement=all` would log every statement), without the named volume
      and without a published port, because every statement runs through `docker exec`:

      ```powershell
      docker build -t semiplot-bench:capture SemiPlot/bench
      docker run -d --name semiplot-measure `
        -e POSTGRES_PASSWORD=semibase-container-superuser -e SEMIBASE_WRITER_PASSWORD=semibase-container-writer `
        -e SEMIBASE_PLOT_PASSWORD=semibase-container-plot -e SEMIPLOT_PROVISIONED_DATABASE=semiplot_provisioned `
        semiplot-bench:capture
      ```

      It is ready when `docker exec semiplot-measure psql -U postgres -d semiplot_provisioned -c "SELECT 1"` succeeds
- [x] as `postgres`, `CREATE DATABASE semiplot_measure TEMPLATE semiplot_provisioned;`, then in `semiplot_measure`
      create the day partition with the DDL of `PartitionScript.cs:29-30` and fill it in SQL, ten pens per statement
      for pens 1-10, 11-20, 21-30, 31-40 and 41-50:

      ```sql
      CREATE TABLE IF NOT EXISTS public.tp2026m01d01 PARTITION OF public.trends
          FOR VALUES FROM ('2026-01-01 00:00:00') TO ('2026-01-02 00:00:00');
      INSERT INTO public.trends (id, l, t, v, q)
      SELECT pen, 0, timestamp '2026-01-01 00:00:00' + tick * interval '100 milliseconds', random() * 100, 0
      FROM generate_series(1, 10) AS pen, generate_series(0, 863999) AS tick;
      ```

      That is 864 000 rows per pen, the 100 ms floor, 43.2 million in all. ASSUMPTION: about 4 GB on disk, scaled
      from 63 MB per 670 000 rows (`scada-archive-observed.md:546`). The seeder is not used: it holds every row of the
      run as an object in one array before one `COPY` (`SeedFiller.cs:13-14`, `ArchiveWriter.cs:61-71`)
- [x] as `postgres`, `VACUUM ANALYZE public.trends;`, so the fresh rows have a visibility map and statistics before
      the first plan
- [x] as `postgres`, run `EXPLAIN (ANALYZE, BUFFERS)` of `ExportRowCount` over `[2026-01-01, 2026-01-02)` for pens
      1-16 and 1-50, and of `ExportSliceRows` over `[2026-01-01 12:00, 2026-01-01 12:15)` for pens 1-16 and 1-50, with
      `@ids` written as `ARRAY(SELECT generate_series(1, N))` and the bounds as `timestamp` literals. Each runs cold,
      right after `docker restart semiplot-measure` (PostgreSQL's buffers empty; the Docker VM's page cache is not
      cleared, which the record states), and warm, a second run. Record execution time, rows, shared hits and reads,
      and whether the sort wrote to disk. `postgres` runs without the role's 30 s timeout, so a slow statement yields a
      number instead of `57014`
- [x] as `semiplot` (`docker exec -e PGPASSWORD=semibase-container-plot semiplot-measure psql -h 127.0.0.1 -U semiplot
      -d semiplot_measure`), run the same four statements without `EXPLAIN` and record pass or `57014` under the role's
      `statement_timeout`
- [x] record the numbers in this plan under this task with the date
- [x] (not selected: 24 h count of 50 pens took 5.5 s cold, 5.1 s warm, under 10 s; the count stays one statement)
      **count branch**, selected when the 24 h count of 50 pens takes over 10 s, a third of `statement_timeout`, cold or
      warm: `CountRowsAsync` counts per slice of `ArchiveRowSlices.Tile(from, to)` on one connection and sums. The
      first slice runs `ExportRowCount` bound by `BindPensRange` to that slice, so the seeds, which depend only on
      `from`, are counted once; every later slice runs a new `ExportSliceRowCount`, `SELECT count(*) FROM trends WHERE
      id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to;`, bound by `BindPensRange`. Tests: its statement text and
      binder; the existing count-equals-reads integration test over a range of several slices; an `ExplainPlanTests`
      case for `ExportSliceRowCount` with the slice case's assertions. Re-measure one slice count and record
      it. Under 10 s, the count stays one statement and nothing changes
- [x] (not selected: one 50-pen slice took 0.36 s cold, 0.63 s warm, under 10 s; the width stays 15 minutes)
      **slice branch**, selected when one `ExportSliceRows` slice of 50 pens takes over 10 s: halve
      `ArchiveRowSlices.Width`, update the tests that name the width, re-run the slice measurement, and repeat until a
      slice is under 10 s; record each width and time. Under 10 s, the width stays 15 minutes
- [x] `docker rm -f -v semiplot-measure`, which removes the container and its anonymous data volume; nothing measured
      enters the test suite

Measured 2026-10-14, PostgreSQL 17 in the bench image on Docker Desktop, `semiplot_measure` with one day partition of
43 200 000 rows (50 pens x 864 000, 100 ms floor): 2482 MB heap, 3782 MB with indexes (the ASSUMPTION of about 4 GB
held). `VACUUM ANALYZE` ran before the first plan. Cold = right after `docker restart semiplot-measure`, the Docker
VM's page cache not cleared; statements ran in the order below, so a later cold statement may reuse pages an earlier
one read. The seed lateral of the count found no prior-day rows (the archive holds one day), so the count measures
the range half; the seed half is 16 or 50 index probes.

| Statement | Cold ms | Warm ms | Rows | Shared hit / read (cold) | Sort |
| --- | --- | --- | --- | --- | --- |
| `ExportRowCount`, 24 h, 16 pens | 2065 | 1576 | 1 (13.8 M counted) | 23 / 52 982 | none (parallel aggregate) |
| `ExportRowCount`, 24 h, 50 pens | 5456 | 5107 | 1 (43.2 M counted) | 40 / 165 548 | none (parallel aggregate) |
| `ExportSliceRows`, 15 min, 16 pens | 121 | 150 | 144 000 | 39 / 1657 | external merge, 5.4 MB on disk |
| `ExportSliceRows`, 15 min, 50 pens | 361 | 626 | 450 000 | 1869 / 3412 | external merge, 16.7 MB on disk |

Run as `semiplot` under `statement_timeout = 30 s`, cold and warm, all four statements passed with no `57014`; the
slice statements returned 144 000 and 450 000 lines. The slice plan is an index scan on `tp2026m01d01_pkey` under a
`Sort`. The sort spills to temporary files at the default `work_mem`, and the 50-pen slice stays far under the
timeout. Both thresholds stayed unmet, so neither the count branch nor the slice branch applies and no code changed.

### Task 4: Date picker and typed time fields

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/ExportRangeProblemText.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`, `CsvExportWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Forms.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/ExportRangeFormTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`

- [x] `ExportRangeForm` and `ExportRangeProblem` as in Technical Details; the setters raise the derived properties
      as today (`ExportRangeForm.cs:136-144`)
- [x] `ExportRangeProblemText`: the two missing-date arms; the unreadable arms take `TimeFieldFormat`
- [x] resx: `ExportDateFormat`, the two missing-date keys, the two reworded unreadable keys
- [x] AXAML: a 2x2 grid, labels `Start` and `End` in the first column, `CalendarDatePicker` `ExportStartDate` and
      `ExportEndDate` (`SelectedDate` bound to the form, `SelectedDateFormat="Custom"`,
      `CustomDateFormatString="{x:Static text:Resources.ExportDateFormat}"`), and fixed-width `TextBox`
      `ExportStartTime` and `ExportEndTime`; each binds `Classes.invalid` to its own flag; `SuggestedFileName`
      moved from the window's code-behind to `CsvFileExporter`
- [x] `Forms.axaml`: the `invalid` selector for `CalendarDatePicker`. ASSUMPTION: Semi 12.0.3 draws the picker's
      visible border on `PART_TextBox`'s `Border#PART_ContentPresenterBorder`, so the selector nests two `/template/`
      steps like `NumericUpDown` (`Forms.axaml:9`); the implementer walks the realised template, and the rendered
      brush test below decides. Shipped: the assumption failed; the selector is
      `CalendarDatePicker.invalid /template/ Border#Background`, one step, because the realised Semi template draws
      the visible border there and `PART_TextBox`'s border is transparent
- [x] rewrite `ExportRangeFormTests` over dates and time texts: prefill floor and ceiling under a pinned zone,
      including an end ceiled across midnight onto the next date; 12 h valid; exactly 24 h valid; 24 h plus one
      second marks both end controls with the span problem; end before start marks both end controls; a missing
      start date; a missing end date; `25:00:00`, `8:00` and `08:00:00 13.10.2026` in a time box are unreadable;
      messages in form order across all seven arms; no pen ticked marks no control; the parsed values carry no
      conversion; values absent while invalid; an edit raises the derived properties
- [x] adjust `CsvExportWindowTests` (`:54-55`, `:81-82`, `:106-107`, `:111-112`): the size test types `tomorrow` into
      `ExportStartTime`; the span tests set the end date to the next day through `ExportEndDate`'s `PART_TextBox`
      (`Clear`, `Type` of the date in `Resources.ExportDateFormat`, `Press` Return) and type `09:00:01`; the
      `CsvExportViewModelTests` range edits (`EndText = ...`) become `EndTimeText` edits
- [x] new window tests: clearing the start date and pressing Return shows the missing-date message, marks
      `ExportStartDate`, disables `Save as...` and keeps the window's and the button's `Bounds`; typing an
      unparseable date and pressing Return restores the prefilled date and leaves the form valid; the invalid picker
      paints its border with `AppSeverityErrorBrush` focused and at rest, read from the rendered part as
      `SettingsViewTests.AnInvalidField_PaintsItsBorderWithTheErrorBrushFocusedOrNot` does
      (`SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs:269`); the prefilled picker text is the date in
      `Resources.ExportDateFormat`
- [x] `ResourcesTests`: the reworded keys keep `{0}`
- [x] run tests - must pass before task 5

### Task 5: Success line names the file, the tooltip carries the path

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`

- [x] the success status takes `Path.GetFileName(path)`; `MessageToolTip` from `WhenAnyValue` over the form problem and
      the last success path, through `ToProperty`; `ToolTip.Tip` of `ExportMessage` binds it
- [x] update `CsvExportViewModelTests.cs:223` and `:270` to the file name
- [x] tests: after a success `MessageToolTip` is the full path; a range edit clears it; a failed export and a
      cancelled one leave it null; a form problem over a success shows no tooltip
- [x] window test: the realised `ExportMessage`'s `ToolTip.GetTip` is the path after a success
- [x] run tests - must pass before task 6

### Task 6: Pen list, message row and width

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`
- Modify: `docs/plans/20261014-csv-export-rework.md` (the measured numbers for Task 8)

- [x] `ExportPensHeader` becomes "Pens ({0} of {1})" / "Перья ({0} из {1})", shown through a `MultiBinding` with
      `StringFormat="{x:Static text:Resources.ExportPensHeader}"` over `Form.TickedPenCount` and `Pens.Count`
- [x] the `ListBox`: `BorderThickness="1"`, `BorderBrush="{DynamicResource AppBorderBrush}"`, no fixed `Height`,
      `MaxHeight` of eight 28 px rows plus the border, `ScrollViewer.VerticalScrollBarVisibility="Auto"`,
      `ScrollViewer.AllowAutoHide="False"`; the virtualising panel stays, and a `ListBoxItem` style scoped to
      `ListBox#ExportPens` (`Padding="0"`, `MinHeight="0"`, as in `PenEditorWindow.axaml`) holds the row at 28 px
- [x] the message `TextBlock.form-message` on its own full-width row; the buttons on a right-aligned row below it
- [x] `Width="520"` as the candidate (kept: the measurement below fits it). Floor: the Russian buttons 145 + 82 + 88 px plus two 8 px gaps are 331 px, plus
      the 40 px margin, 371 px. At 520 px the message row is 480 px, two lines of 960 px capacity for the 702 px
      Russian Excel notice. ASSUMPTION: the Russian success line with the suggested 44-character file name is about
      600 px on one line, scaled from 766 px for the 70-character path (`ui-theme.md:245`), so it fits two lines.
      ASSUMPTION: the date picker, the time box and the Russian label fit 480 px
- [x] measure with real fonts, the way the 2026-10-13 numbers were taken (Skia and HarfBuzz, the real Win32
      platform, the Semi theme, Inter 14 px, a scratch program deleted after; `docs/plans/20261013-csv-export.md:410-411`).
      The headless test font is about twice as wide (`ui-theme.md:251-252`), so no headless test can stand in.
      The scratch program:
      1. a console project in the session scratchpad
         (`C:\Users\admin\AppData\Local\Temp\claude\C--Users-admin-projects-SemiPlot\aeb1b9cb-3eb6-4468-95db-0e1a8214449d\scratchpad`),
         never in the repository, with a `ProjectReference` to `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` by absolute
         path; `CsvExportWindow`, `CsvExportViewModel`, `ExportPenChoice` and `MessagePanelViewModel` are public
         (`CsvExportViewModel.cs:20`, `ExportPenChoice.cs:8`, `MessagePanelViewModel.cs:13`)
      2. one process per language: set `CultureInfo.DefaultThreadCurrentUICulture` to `ru` or `en` first, as
         `StartupSequence.cs:47-50` does
      3. `AppBuilder.Configure<App>().UseWin32().UseSkia().UseHarfBuzz().UseReactiveUI(_ => { }).SetupWithoutStarting()`,
         the chain of `App.BuildAvaloniaApp` (`App.axaml.cs:257-267`, which is internal) without its exception handler;
         `UseHarfBuzz` is required in Avalonia 12 (`App.axaml.cs:262-264`), and no classic lifetime is started because
         `App.CreateMainWindow` needs a built trend window (`App.axaml.cs:91-95`); then
         `SemiTheme.OverrideLocaleResources(app, ru-RU or en-US)` as `App.axaml.cs:238-239` does
      4. a `CsvExportWindow` over a `CsvExportViewModel` built with a stub `IArchiveRowReader`, the
         `AvaloniaScheduler.Instance` UI scheduler, 50 pens named as the seeder names them (`Damper 01`, ...), then
         `Show()` and a `Dispatcher.UIThread.MainLoop` that the program ends after its last reading
      5. read `Bounds` of the window, the buttons, the message row and the date and time grid after `Show()`; drive
         each state through the view model (each problem in turn, a count above the Excel limit from the stub, a
         success into a file in the scratchpad under the suggested name) and read the window's `Bounds` again; measure
         each text on one line with a `TextLayout` in the message `TextBlock`'s typeface at 14 px
      6. delete the scratch project
      Record in both languages: the window size; the button widths; the message row width; the Excel notice, the
      success line with the suggested file name and the longest form message on one line and in the row; the date
      and time grid's natural width; the pens header at "50 of 50"; the window size with each control invalid in turn.
      Move the width inside 480-560 px if a measurement demands it, and write the numbers into this plan for Task 8
- [x] tests: with 12 pens the list's height equals its `MaxHeight`, and with 2 pens it is below it; the inner
      `ScrollViewer`'s `AllowAutoHide` is false and so is its vertical `ScrollBar`'s. ASSUMPTION: Semi's `ListBox`
      template passes the attached `ScrollViewer.AllowAutoHide` to its `ScrollViewer`; the test reads the realised
      value and decides; the header text equals `Resources.FormatExportPensHeader(1, 2)` and changes after a click
      on a box; the message row sits above `ExportSaveAs` and spans the content width; the size test still holds
- [x] `ResourcesTests`: `ExportPensHeader` carries `{0}` and `{1}` in both sets
- [x] run tests - must pass before task 7

Measured 2026-10-14 with Skia and HarfBuzz, the real Win32 platform, the Semi theme, Inter at 14 px, a scratch
program deleted after (a `CsvExportWindow` over a stub reader, 50 pens `Damper 01`..`Damper 50`, `Width="520"`):

| | English | Russian |
| --- | --- | --- |
| Window size, every state below | 520 x 515 px | 520 x 515 px |
| `Save as...`, `Cancel` and `Close` buttons | 90, 73 and 64 px | 145, 82 and 88 px |
| Message row | 480 px | 480 px |
| Date and time grid: label, picker, time box; right edge of the time box | 32, 160, 110; 330 px | 52, 160, 110; 350 px |
| Pens header at "1 of 50" and "50 of 50" | 97 and 106 px | 112 and 121 px |
| Excel notice on one line, lines in the row | 577 px, 2 | 702 px, 2 |
| Success line with the 44-character suggested file name, one line, lines in the row | 455 px, 1 | 509 px, 2 |
| Longest form message on one line (lines in the row) | 253 px, end not after start (1) | 275 px, start time unreadable (1) |

The window kept 520 x 515 px (50 pens, the list at its `MaxHeight`) through the valid state, the Excel notice
(a count of 2 000 000), each of the seven problems in turn and the success line. Every message fits two lines
of the 480 px row, and every form message fits one. The 520 px candidate stays; the three ASSUMPTIONs (the
Russian success line about 600 px, the picker row fitting 480 px, the notice in two lines) held with room: 509 px,
350 px and 702 px of 960 px.

### Task 7: Verify acceptance criteria

Requires Task 3 complete, with its measurements recorded and any selected branch done.

- [x] verify every point in Overview is implemented
- [x] verify the edge cases: a pen with only a seed, two pens with one name, one ticked of two namesakes, a range
      shorter than one slice, a range ending inside a slice, equal timestamps across pens, a cleared date, a cancel
      mid-slice
- [x] run every command in Acceptance Evidence
- [x] run full test suite: `dotnet test SemiPlot.slnx`
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files
- [x] `git grep -n -E "ReadPenRowsAsync|ExportPenRows|BindWindow|BindCount" -- SemiPlot docs AGENTS.md readme.md` (verified: only the test helper `BindWindowParametersAt` in ExplainPlanTests, a different identifier, and docs/architecture lines owned by Task 8)
      prints only lines under `docs/plans/`
- [x] `git grep -n -i -E "each pen|per pen|one pen at a time|pen after pen|pen's rows" -- SemiPlot/SemiPlot.Core
      SemiPlot/SemiPlot.DataSource.Postgres SemiPlot/SemiPlot.UI/Export SemiPlot/SemiPlot.Tests.Unit/UI/Export
      SemiPlot/SemiPlot.Tests.Integration/ArchiveRowReaderTests.cs`: every hit in a comment or a summary still
      describes the code under it; one that describes the per-pen read is reworded. The seed probe is legitimately
      per pen
- [x] `git grep -l "IArchiveRowReader" -- SemiPlot/SemiPlot.UI/Chart SemiPlot/SemiPlot.UI/Legend` prints nothing

### Task 8: [Final] Update documentation

- [x] `docs/architecture/data-integration.md`: the reader table (`:87-101`) with the two new members, rows with
      `PenId`, the slice width and why; the export rows of "Operation to statement" (`:238-245`) with
      `ExportSliceRows`, `t, v` columns, the measured times of Task 3 and, when its count branch was selected,
      `ExportSliceRowCount` and the per-slice count; the binder list (`:247-251`) with
      `BindPensRange`; the cancellation sentence (`:775-778`) naming the export's seed, slice and count reads; the
      time-boundary paragraph (`:478-484`) with the date picker and the typed time
- [x] `docs/architecture/overview.md` "The export window" (`:686-725`): three columns, one table by time, seeds
      first, ties by checklist order, the pen field and its collision rule, the date and time form, the list, the
      message row, the file name with its tooltip, slices instead of one pen at a time
- [x] `docs/architecture/ui-text.md`: take the header and the range format out of "What stays a literal" (`:88-92`),
      keep the non-finite spellings and `HH:mm:ss` there; "The export window's text" (`:276-306`) gets the header
      keys, `ExportDateFormat` and why the picker's popup follows the machine's culture, the new problem keys, the
      pens header format and the file-name success line
- [x] `docs/architecture/ui-theme.md`: replace the export window's paragraph and table (`:233-255`) with the
      measurements of Task 6, and add the picker's `invalid` selector beside the `NumericUpDown` one (`:191-198`)
- [x] `docs/architecture/charting.md` (`:508-511`, `:543-544`): `CsvHeader`, the factories, `PenFields`
- [x] `docs/architecture/testing-strategy.md:156`: `ArchiveRowReaderTests` pins the tie order and the slices too
- [x] `docs/architecture/trend-feature-spec.md:247-249` and `docs/architecture/trend-interaction.md:98-99`: the value
      export's columns are pen, time and value
- [x] `AGENTS.md:371-375`: the export reads seeds and slices; `readme.md:53-54`: one table by time with a header in
      the interface language
- [x] move `docs/plans/20261013-csv-export.md` and this plan to `docs/plans/completed/` (left for the delivery step)

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification**

- The Excel checklist in Acceptance Evidence, on a Windows machine with Excel, in ru-RU and en-US.
- ASSUMPTION: Excel in ru-RU reads `2026-10-13 08:00:00,123` as a date-time and `2026-10-13 08:00:00.123` as text.
  The Russian `TimestampFormat` is `yyyy-MM-dd HH:mm:ss,fff`, the English one `yyyy-MM-dd HH:mm:ss.fff`, and step 5
  of the checklist confirms or corrects it; a correction goes into `CsvDialect.Russian` and its tests.

**Customer archive**

- One export of a full 24 h of every pen on the customer archive, timed, with the process's peak working set, and
  the live count over the same range timed; both go to `docs/architecture/scada-archive-observed.md` and replace the
  row-size and statement-time ASSUMPTIONs with measurements.

**Executed by exec:**
- branch: csv-export

## Verify it yourself

The new tests fail at `c4ba462` (six columns in per-pen blocks, masked text range fields) and pass at `8239a5f`.

Automated, from the repository root (the integration command needs Docker):

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ArchiveRowReaderTests|FullyQualifiedName~ExplainPlanTests"
```

| Behaviour | Test |
| --- | --- |
| Header in the dialect's language whatever the thread UI culture | `AppConfigurationTests.CsvDialectFor_English_KeepsItsHeaderWhileTheThreadUiCultureIsRussian`, `ArchiveCsvWriterTests.WriteHeader_WritesThePenTimeAndValueNamesOfTheDialect` |
| ` (id)` only on a shared name or a shared `name, unit` field among ticked pens | `ArchiveCsvWriterTests.PenFields_TwoTickedPensNamedDamper01_GetTheirIds`, `PenFields_DifferentNamesComposingOneField_GetTheirIds`, `CsvExportViewModelTests.ANamesakeThatIsNotTicked_AddsNoIdToTheTickedPensFileField`, `TwoTickedNamesakes_CarryTheirIdsInTheFile` |
| Header, then seeds, then slice rows | `CsvExportViewModelTests.TheExport_WritesTheHeader_ThenTheSeedsThenTheSliceRowsInCallOrder` |
| Slices tile the range with no gap or overlap | `ArchiveRowSlicesTests.TheSlicesOfADay_TileItWithNoGapOrOverlap` |
| Cancel stops before the next slice and leaves no file | `CsvExportViewModelTests.ACancelAfterTheFirstSlice_StopsBeforeTheNextSliceEvenWhenTheReaderIgnoresTheToken` |
| The slice statement reaches its rows through an index within its day | `ExplainPlanTests.TheExportSlicePlanReachesItsRowsThroughAnIndexWithinItsDay` |
| A cancel during a running seed or slice statement throws instead of failing | `ArchiveRowReaderTests.ACancelDuringARunningStatement_ThrowsOnceTheLockedTableHoldsItBack` |
| A cleared date shows the missing-date message without resizing the window, ru and en | `CsvExportWindowTests.AClearedStartDate_ShowsTheMissingDateMessage_MarksThePicker_AndKeepsTheBounds`, `AClearedEndDate_ShowsTheMissingDateMessage_AndMarksTheEndPicker` |
| Pen list: eight 28 px rows, then a visible scroll bar; header counts ticks | `CsvExportWindowTests.ThePenList_MaxHeight_ShowsExactlyEightRowsOf28Pixels`, `ThePensHeader_ReadsTheTickedAndTotalCounts_AndFollowsAClick` |
| Namesakes are told apart in the checklist | `CsvExportWindowTests.TwoPensSharingAName_ShowTheirIdsInTheChecklist` |

By hand, on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`, or the `Live demo` run configuration):

1. `File` -> `Export to CSV...`: the window is 520 px wide; Start and End each show a date picker and a
   `HH:mm:ss` box prefilled from the chart's visible edges.
2. The pen list has a border, shows at most eight rows, and its scroll bar stays visible when it scrolls; the
   header reads `Перья (N из M)` and follows each tick.
3. Clear the start date: the missing-date message shows and the window does not resize. Type `25:00:00` into the
   end time: the time-format message shows.
4. Save: the message row names the file only; hovering it shows the full path.
5. Run the Excel checklist in Acceptance Evidence (steps 1-6); step 5 settles the Russian timestamp ASSUMPTION.
