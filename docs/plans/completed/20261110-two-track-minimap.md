# Two-track minimap, recipe-run navigation and a six-month working-day stand

## Overview

- The minimap gets two tracks:
  - an **archive track** over the whole extent: the days that hold rows, the operator's markers, and an
    underline under the detail page;
  - a **detail track**: one page, a working day or a week, drawn at the strip's width, with recipe
    runs shaded on the Day page.
- Page steps `‹` and `›` turn to the previous or next page that holds rows. The chart follows and lands
  on data.
- The navigation bar gets a recipe-run group: previous and next run, latest run, and a list of the
  operator's markers.
- A `Go` menu and window-level gestures carry every step, so the actions survive hiding the bar or the
  minimap.
- The problem:
  - With six months in the archive, the strip reads 250 columns over 183 days, so one column covers
    about 17.6 h (`SemiPlot.UI/Minimap/MinimapBandFeed.cs:27`, `:146-149`). No decimation shows a
    working day's shape at that width.
  - Reaching a given run means hand-panning, even though the SCADA stores every recipe start, end and
    interruption as a locked marker (`docs/architecture/data-integration.md:296-297`,
    `SemiPlot.Core/Data/IMarkerStore.cs:30-36`).
- The seeder produces only a continuous archive (`SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs:10-14`).
  A plasma tool runs 6 to 10 hours on weekdays and is off at nights and weekends. A `shifts` schedule
  with recipe markers, plus three `converge` keys, produces a six-month stand of that shape.
- The work ships as three PRs, one per phase. Each phase ends with its own documentation and a full
  gate:
  - Phase A: the working-day stand (Tasks 1-5);
  - Phase B: the two-track minimap (Tasks 6-15);
  - Phase C: marker navigation (Tasks 16-21).
- ➕ The branch holds the three phases as one line of commits; delivery splits it at the phase ends
  (Post-Completion, Delivery).

## Context (from discovery)

- Minimap today:
  - `SemiPlot.UI/Minimap/MinimapView.axaml:16-45`: one 36 px `StripCanvas` with the band, the
    window highlight, the marker ticks and the hover line, plus a label row.
  - `MinimapView.axaml.cs` is 266 lines.
  - `MinimapViewModel` is 326 lines. It takes its band bounds from `ReadBounds`
    (`MinimapViewModel.cs:50`, `:196-199`). `NavigateToFraction` pans the shared navigation by the
    distance to the target (`:153-166`). `FormatTime` uses `ToLocalTime()` (`:262-265`).
  - `MinimapBandFeed` reads the drawn pen over the extent at 250 columns (`MinimapBandFeed.cs:27`,
    `:139-150`, `:168-169`) and reports failures through `OutageReport` (`:62`, `:213-224`).
  - The pure geometry lives in `SemiPlot.Core/Trends/MinimapGeometry.cs`.
  - The minimap is built in `SemiPlot.UI/MainWindow/TrendWindow.cs:100-101`. `TrendWindow.Build` holds
    `archiveZone` (`TrendWindow.cs:69`) and passes it to the chart only.
- Archive days and extent:
  - `IDataProvider.QueryArchiveDaysAsync` (`SemiPlot.Core/Data/IDataProvider.cs:22`) runs
    `ArchiveStatements.ArchiveDays` (`SemiPlot.DataSource.Postgres/ArchiveStatements.cs:180`,
    `PostgresDataProvider.cs:160-170`, `:319`).
  - It works in archive wall-clock days and skips `ArchiveTimeConverter`
    (`data-integration.md:922`, `:932-934`).
  - Measured: 2.5 ms execution and 8 ms planning over 31 days (`data-integration.md:895-911`).
  - Callers: `TimeWindowPanelViewModel.cs:98`, and the export through `TrendWindow.cs:131`.
  - `ArchiveExtent` uses a lateral min/max pair that the code comments call "load-bearing"
    (`ArchiveStatements.cs:163-174`).
  - Gates: `Postgres/ArchiveStatementTextTests.cs`, `ExplainPlanTests.ArchiveDays_ReadsThroughTheIndex`
    (`ExplainPlanTests.cs:292`), `PostgresArchiveDaysReadTests.cs`.
  - The provider and the viewer share one time zone: `SourceTimeZone = TimeZoneInfo.Local`
    (`data-integration.md:950-952`).
- Markers:
  - `StoredMarker(Id, TimeUtc, Label, Kind, IsLocked)` and
    `MarkerKind { User, RecipeStart, RecipeEnd, RecipeInterrupted }` (`IMarkerStore.cs:22-36`).
  - `semiplot_markers.t` is `timestamptz` (`data-integration.md:294`).
  - `scada_writer` holds `INSERT` on the table, and the SCADA writes recipe markers as that role
    (`docs/architecture/postgres-instance.md:106-107`).
  - `ChartMarkers.All` holds every marker of the window, shown or hidden (`SemiPlot.UI/Chart/ChartMarkers.cs:41`).
  - `MarkerSync` reads the whole table every 5 s (`data-integration.md:339`).
- Navigation:
  - `ChartNavigationController.SetWindow` drops Sticky (`TrendNavigationModel.cs:76-86`).
  - `PlaceFrom` clamps `From` to `FirstSample` (`TrendNavigationModel.cs:94-98`).
  - The bar's `IsSticky` has one writer (`NavigationBarViewModel.cs:68-71`).
  - `NavigationBarView.axaml:17-31` holds `Now`, `Sticky`, one `GroupSeparator` border (`:24`) and
    `Delta`.
  - `NavigationBarViewTests` pins exactly three `ContentControl`s and a single `GroupSeparator`
    (`SemiPlot.Tests.Unit/UI/Navigation/NavigationBarViewTests.cs:45-48`, `:56-58`, `:71`).
- Menu: `SemiPlot.UI/MainWindow/AppMenuBar.axaml:16-97` holds File, Edit, View (elements `ViewMinimap`,
  `ViewRecipeMarkers`) and Help. `AppMenuBarTests.EveryMenuLeaf_CarriesACommand` walks the declared
  `Items` (`AppMenuBarTests.cs:33-60`). No window-level gesture exists today.
- Test seams:
  - `FakeDataProvider` tells band reads from chart reads by `TargetColumnCount == MinimapColumns`
    (`SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:105-106`, `:123-124`).
  - Chart counts are the data-area width clamped to 256..2048 times a `FetchRange.ColumnFactor` of 1 to
    3 (`HistoryColumnTarget.cs:7-17`, `ChartHistoryPlanner.cs:99`); only the layer ladder quantizes the
    width to a power of two (`ChartNavigationController.cs:269-284`). Corrected in Task 20.
  - `MinimapBandFeedTests.cs:363` pins the band count below `MinColumns`.
  - The provider does not clamp the column count (`PostgresDataProvider.cs:334-336`).
- Palette:
  - `AppSecondaryForegroundBrush` is `#818594` light and `#6F737A` dark (`Palette.axaml:87`, `:173`).
  - A key that carries opacity joins `ThemeTests.APaletteKeyCarryingOpacity_KeepsItUnderBothVariants`
    (`ui-theme.md:135`).
  - A Semi surface new to the tree has its brushes read back in `ThemeTests` (`ui-theme.md:30-33`).
- Seeder:
  - `RawLayerGenerator.Generate` writes each pen run by run over `BreakPlan.Runs`
    (`RawLayerGenerator.cs:7-21`).
  - `BuildRuns` opens a run at `Start` and closes one at `End` (`BreakPlan.cs:87-101`), where
    `Start = End - Days` (`SeederOptions.cs:19`). `converge`'s `End` defaults to `DateTime.Now`
    (`Converge.cs:43`).
  - Break markers: q=16 goes on runs with `runIndex > 0`, q=32 on runs a break follows
    (`RawLayerGenerator.cs:125-126`, `MarkRunBoundaries` `:144-155`).
  - Values come from the private `HeldValue` and `ValueAt` (`:190-200`).
  - `RequireTwoChangesPerRun` throws on a short run (`:159-178`).
  - `ArchiveWriter.WriteAsync` refuses an archive that already holds rows or partitions
    (`ArchiveWriter.cs:17-25`, `:54-59`). It creates the partitions and the COPY in one transaction
    (`:61-71`) and streams an `IEnumerable<ArchiveRow>` (`:34-40`, `:97-120`).
  - `SeedFiller` materialises every row (`SeedFiller.cs:5`, `:13-17`), and `Program.SeedAsync` hands
    `fill.Rows` to `ReportPlan`, which prints the break count (`Program.cs:35`, `:142-155`).
  - `converge` takes five keys (`SeederCommand.cs:145-162`) and hard-codes days, pens and breaks
    (`Converge.cs:41-48`, `ConvergeOptions.cs:6-11`).
  - Tests at the unit project root: `RawLayerGeneratorTests.cs` (determinism at `:27`),
    `SharedLatticeTests.cs` (`breaks: 0` only, `:28-29`), `SeederCommandTests.cs`.
  - The AppHost runs `converge` at `--change-seconds 0.5` (`SemiPlot.AppHost/AppHost.cs:21`, `:41-47`).
- Conventions:
  - Operator text lives in `Resources.resx` and `Resources.ru.resx`.
  - Colours resolve to `Palette.axaml` keys.
  - Insets are on the 4/8/12/16/24/32 scale.
  - Headless input goes through `HeadlessInput`.
  - A checkable control reads `OneWay` and writes through its command.
  - Every menu leaf carries a command or children.
  - Every test class carries the Component, Area and Category traits.
  - `dotnet format` and `terse` gate commits (AGENTS.md).

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next; each phase ends green, documented and shippable
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- the `continuous` schedule and a default `converge` run produce today's archive row for row; Task 2 gates it

## Testing Strategy

- **unit tests** (`SemiPlot.Tests.Unit`, no container):
  - the shift schedule, the paging model, the marker navigation and the geometry as plain `[Fact]`s;
  - the feeds and the pager over `FakeDataProvider` and a `TestScheduler`, never started after a landed
    band read (AGENTS.md);
  - the realised minimap, navigation bar, menu and window gestures through their stands and
    `HeadlessInput`.
- **integration tests** (`SemiPlot.Tests.Integration`):
  - a `shifts` seed into a `CloneSource.Provisioned` clone;
  - the day-spans statement against the seeded template;
  - its index plan.
- No browser e2e suite exists. The realised-view tests are the UI gate.

## Acceptance Evidence

New tests that fail on today's code. Run each with `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~<Name>"`:

1. `MinimapBandFeedTests.ADayPageReadsTheWorkingHoursAtTheStripWidth`
   - Setup: a six-month extent, working hours 07:00-17:00, a 1500 px strip.
   - It asserts one band request over 07:00-17:00 of that day at 1500 columns, a bucket of 24 s.
   - Today the request spans the whole extent at 250 columns, a bucket of about 63 000 s.
2. `MinimapPageTests`
   - `›` from a Friday lands on Monday when the weekend holds no rows.
   - A window at 02:00 lands at that day's first sample.
   - The working hours are P5 and P95 rounded out to whole hours.
   - A day with data outside the hours widens in whole hours.
   - A 24/7 archive yields 00:00-24:00.
3. `MarkerNavigationTests`
   - A start through its end, or through its interruption, is one run.
   - A start with no close runs to the next start, or to the newest sample.
   - An orphan close is skipped.
   - Steps measure from the window centre.
4. `MinimapViewTests`, `MinimapPointerInputTests` and `NavigationBarViewTests`
   - A `HeadlessInput.Click` on `›` moves the chart a working day (`MinimapViewTests`).
   - A click on the archive track over a weekend lands on the nearest working day's data
     (`MinimapPointerInputTests`).
   - A click on next run frames the run with a 10% margin on each side.
5. `ShiftsGenerationTests` and `RecipeMarkerWriterTests` (➕ split out of `ShiftScheduleTests`, which keeps the
   schedule model; filter `FullyQualifiedName~ShiftsGenerationTests|FullyQualifiedName~RecipeMarkerWriterTests`)
   - 14 days of `shifts` hold no raw row on Saturday or Sunday.
   - Every session after the first opens with `q = 16`, every session before the last closes with
     `q = 32`, and the last session runs to `--end`.
   - Each recipe run has a start marker and one closing marker.
   - A continuous demo writer resumes a `shifts` seed with no repeated row and no hole.
6. Integration: `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~ShiftSeedTests|FullyQualifiedName~PostgresArchiveDaySpansReadTests"`.

Guards that already pass and must stay green:

7. `--filter "FullyQualifiedName~RawLayerGeneratorTests|FullyQualifiedName~SharedLatticeTests|FullyQualifiedName~SeederCommandTests"`.
8. Task 2's new invariant: every `continuous` row's value equals the walk at its held index
   (`RawLayerGeneratorTests.EveryContinuousRowCarriesTheWalkAtItsHeldIndex`, inside guard 7's filter).

Measured on the six-month stand and recorded in `docs/architecture/bench.md`. These are numbers,
not assertions:

9. The `converge --schedule shifts --days 183 --pens 50 --change-seconds 30` run: wall time, peak
   working set, raw and coarse row counts, recipe marker count, `pg_database_size`.
10. The viewer at Debug:
    - the extent, archive days, day-spans and marker reads;
    - the band read at Day and Week (`MinimapBandFeed.cs:224-228` logs layer, columns and time);
    - the chart's frame time with about 5 000 recipe markers in the table.
    - Taken in Task 5 as the baseline and again in Task 19.

Manual smoke checklist on the six-month stand:

1. The archive track shows working days as blocks, with gaps for weekends and the maintenance week.
2. The Day page spans the derived working hours, and the day's runs show as shaded blocks.
3. `›` pressed five times from a Monday reaches the next Monday, and the chart never shows an empty day.
4. A click on a Saturday in the archive track lands on Friday's or Monday's data, whichever is nearer.
5. Ctrl+Right steps through the runs of a day, each framed. Ctrl+End frames the newest run.
6. With the minimap and the navigation bar hidden, every step works from `Go` and its gesture.
7. On Win32, Ctrl+Left typed in the marker label editor, which opens as its own popup window, moves the
   caret and steps no run.
8. On a real keyboard, Alt+Left and Alt+Right step the page; a lone Alt press may focus the menu bar.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview

**Two tracks, two scales.**
- The archive track answers "where is data in the whole archive".
- The detail page answers "what did this day or week look like".
- The chart is the third level.
- One linear strip cannot do both: at about 24 px per day it holds about 40 days, and Plaisant, Carr
  & Shneiderman (1995) put a useful overview-to-detail ratio below 20:1.

**Two units, chosen by the operator.**
- Day and Week, a two-state toggle. The wheel over the detail track does nothing.
- The chosen unit has one writer, `SetPageUnitCommand`, and lives for the window's lifetime.
- ➕ As built the writer is the parameterless `TogglePageUnitCommand`: the Week toggle's one caller always
  passed the other unit, so the command flips it and the `OtherUnit` converter is gone.
- The shown unit and the page are derived: Week while the chart window is wider than a day, otherwise
  the chosen unit.

**The Day page is the working hours, not the calendar day.**
- The hours are the 5th percentile of each day's first sample to the 95th percentile of each day's last
  sample, as archive-local times of day, rounded out to whole hours.
- A common window keeps one run position at one x across days.
- A day whose data falls outside the hours widens its own page, in whole hours, so no sample is hidden
  and the live day rescales at most once an hour.
- A 24/7 archive gets 00:00-24:00. Time past the newest sample stays empty.

**Steps skip empty days and land on data.**
- Page steps go to the nearest page that has a day span.
- The chart window keeps its clock offset. If that offset holds no sample of the new day, it moves to
  the day's first sample, keeping its width.
- A click on the archive track snaps to the nearest day with rows and lands the same way.
- The day spans arrive before any band is read, so a step never waits for a band or jumps twice.
- Landing and run framing call `Navigation.SetWindow(from, to)` and nothing else. Sticky drops there
  (`TrendNavigationModel.cs:76-86`), with no second writer.

**Recipe runs are a target of their own.**
- A run is a start marker up to its end or its interruption. A start with no close runs to the next
  start, or to the newest sample. A close with no start is skipped.
- Run steps frame the run, plus 10% of its length on each side.
- Operator markers are sparse bookmarks. A marker step centres on the marker and keeps the width, and
  the marker list picks one by label.
- Runs read `ChartMarkers.All`, so hiding recipe markers through View hides the shading, not the runs.
- The run controls live in the navigation bar, because they move the chart window, not the page.
- The Day page's run shading replaces the roughly 40 recipe ticks per day. Week draws neither.

**The shift schedule clips the span and becomes a break plan.**
- Under `--schedule shifts`, the generated span is `[first session start at or after Start, End)`.
- Off periods inside it (nights, weekends, a maintenance week, random off days) are the `BreakPlan`'s
  breaks. Sessions are its runs.
- The last session extends to `End` whatever the clock, so the demo writer's five-minute
  `StaleArchiveGuard` accepts the stand at any hour.
- `MarkRunBoundaries` writes q=16 at the start of every session after the first and q=32 at the end of
  every session before the last.
- Inside a session, a pen walks during a recipe run and holds an idle level between runs. Both go
  through the index-based value lookup, so anchors and slow-pen strides stay on the lattice.
- After the archive COPY commits, the seeder writes each run's start marker and its closing marker,
  locked, as `scada_writer`, the role the SCADA uses.
- The demo writer stays continuous.

## Technical Details

### Shift schedule (seeder)

- `ShiftSchedule` is pure in `(seed, day)`:
  - `IsOn`: weekdays, minus a maintenance week placed by the seed, minus about 4% random off days;
  - `Session`: start at 07:00 plus 0-90 min, length 6-10 h, both on lattice points;
  - `RecipeRuns`: 6-18 min runs, 4-20 min apart, sometimes 40 min more; about 1 run in 15 is
    interrupted.
- `ShiftSchedule.Clip(start, end)` returns the generated span and the off periods inside it.
  `MaximumBreaks` and the 3-10 min bounds (`BreakPlan.cs:10-14`, `:46-53`) bind `continuous` only.
- Values: a value seam replaces the private `HeldValue` and `ValueAt` (`RawLayerGenerator.cs:190-200`).
  `continuous` returns the walk. `shifts` returns the walk inside a run and
  `min + 0.05 * (max - min)` of the stored pair between runs.
- Recipe markers: `t` is bound as `timestamptz`, from the local wall-clock time through
  `TimeZoneInfo.Local`.

### Keys

| Key | Command | Default | Values |
| --- | --- | --- | --- |
| `--schedule` | `converge` | `continuous` | `continuous`, `shifts` |
| `--days` | `converge` (the root already has it) | 1 (`SeederOptions.DefaultDays`) | whole days, at least 1 |
| `--pens` | `converge` (the root already has it) | 10 (`SeederOptions.DefaultPenCount`) | 1 to the catalogue size |

- `ReportPlan` prints the schedule. Under `shifts` it prints the session count, not the break count.
- The six-month stand command:

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- converge `
  --connection "Host=127.0.0.1;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Password=<super>" `
  --config-dir SemiPlot\Artifacts\dev-config `
  --schedule shifts --days 183 --pens 50 --change-seconds 30
```

- Estimate, measured in Task 5: about 12.5 M raw rows and about 5 000 recipe markers. Measured on
  2026-11-10: 11 361 570 raw rows and 4 120 recipe markers (`bench.md#the-six-month-stand`).

### Streamed seeding

- `SeedFiller` yields a lazy sequence, one calendar day at a time: the day's raw rows, then that day's
  thinned rows.
- One `ArchiveWriter.WriteAsync` call consumes it: one COPY, one transaction, and the seeded-archive
  refusal stays intact.
- A minute or hour period never crosses midnight, so per-day thinning equals thinning over the span.
- `SeedFillResult.Rows` becomes per-layer counts, and `Program.ReportPlan` reads the counts.

### Archive day spans (Core and provider)

- `IDataProvider.QueryArchiveDaySpansAsync(firstDay, lastDay)` returns
  `ArchiveDaySpan(DateOnly Day, DateTime FirstUtc, DateTime LastUtc)` for each day with rows:
  - `Day` is the archive wall-clock day the statement groups on;
  - `FirstUtc` and `LastUtc` are the earliest and latest raw sample across the catalogue's pens,
    through `ArchiveTimeConverter.ToUtc`.
- The statement copies `ArchiveExtent`'s lateral min/max pair per day partition
  (`ArchiveStatements.cs:163-174`).
- It supplies `semiplot_tags, trends` to `ArchiveExceptionMapper`.
- One definition of "a day with rows":
  - Task 6 measures `ArchiveDays` and the spans read over six months.
  - If the spans read executes in 100 ms or less, the calendar callers move to it (`.Select(s => s.Day)`)
    and `ArchiveDays` and `BindArchiveDays` are deleted.
  - Otherwise the measured gap is recorded in `data-integration.md` as the reason both stay.
- Fallback if the spans read itself is slow: read the extent once at start, then re-read only the
  newest day.
- Measured in Task 6 on the six-month stand: the spans read executes in 167-176 ms over 184 days against
  15-20 ms for `ArchiveDays`, so both stay and the calendars keep `ArchiveDays`
  (`data-integration.md#read-cost`). The newest day alone executes in 4.5-4.9 ms.

### Paging model (Core)

- `SemiPlot.Core/Trends/MinimapPage.cs` holds `MinimapPageUnit { Day, Week }` and pure functions that
  take the archive zone:
  - `WorkingHours(spans)`: P5 and P95 as archive-local times of day, rounded out to whole hours;
  - `Snap(unit, time, hours, spans)`: a Day is the hours, widened in whole hours by that day's own
    span; a Week is Monday 00:00 to the next Monday;
  - `Step(page, direction, spans)`: the nearest page with a span, or none;
  - `Land(window, page, spans)`: the same offset if it holds a sample, else the day's first sample,
    keeping the width;
  - `SnapToData(time, spans)`: the nearest day with a span.

### Marker navigation (Core)

- `SemiPlot.Core/Trends/MarkerNavigation.cs` holds two concepts:
  - `RecipeRun(Start, End, Interrupted)` with the pairing rule;
  - plain operator-marker stepping.
- Functions: `Runs(markers, newestSample)`, `NextRun` / `PreviousRun` from a time, `LatestRun`,
  `Frame(run)` (10% margin each side), `NextMarker` / `PreviousMarker` over kind `User`.

### Geometry (Core)

- `MinimapGeometry` gains:
  - the archive track's day-snapped scale;
  - the day-gap rule (1 px gaps only at 4 px per day or more);
  - the page underline, at least 8 px wide;
  - the window box clipped at page edges, with a flag per cut edge;
  - tick placement: hours on a Day page, `Mon 11` on a Week page, month starts on the archive track.

### Pager, band and presence (UI)

- `Minimap/MinimapPager` is built and disposed by `MinimapViewModel`, the way `BandFeed` is. It holds:
  - the chosen unit (one writer);
  - the derived shown unit and page (`WhenAnyValue` / `FromEventPattern` -> `ToProperty`);
  - `PreviousPage` and `NextPage`, with `canExecute` from the spans and the page;
  - archive-track navigation through `SnapToData`.
- `MinimapViewModel` takes `archiveZone` from `TrendWindow.Build`.
- `ArchivePresenceFeed` (built by `MinimapViewModel`) reads the day spans:
  - on an applied extent, and when the newest sample enters a new day;
  - after a failure, on the next `NewestSampleMoved` at most once a minute, like the extent read
    (`trend-interaction.md:726-731`).
  - The newest day's `LastUtc` follows `Navigation.NewestSample`.
  - Failures go through `OutageReport`.
  - With no spans, page steps fall back to plain calendar steps.
- `MinimapBandFeed` takes `(from, to, columns)` from the pager:
  - The columns are `min(2500, max(250, round(stripWidth / 250) * 250))`.
  - A multiple of 250 can also be a chart count, so `FakeDataProvider` tells a band read by its request,
    judged as it is issued: the drawn pen alone over the page in force, at a multiple of 250 (Task 20).
  - The 250 px step stops a resize from issuing a read per pixel.
  - Strip width route: code-behind -> `MinimapViewModel.SetStripWidth` -> pager.
  - ➕ As built: code-behind -> `MinimapBandFeed.SetStripWidth`; the column count is the band read's own
    rule, so it lives in the feed and the pager needs no width.

### Layout and encoding

```
Navigation bar (40 px, unchanged height)
[Now] [Sticky] | Recipe run [<] [>] [Latest run] [Markers v] | [Delta]  dt 00:12:30

Minimap (89 px; 51 px today)
1   ------------------------------------------------------------------- AppBorderBrush
4
16  Fri 14 Aug 2026       |  '        '                '         '       |  operator markers 4
                          |  ||||| |||||  ||||| ||#|| |||||  |||||       |  presence 10
                          |                       ==                     |  page underline 2
14                        |  May       Jun       Jul     Aug      Sep    |  months / hover day
36  [<] [Day|Week] [>]    |  ..::::::..:::::..::::::[#####]::..::::..    |  band, runs, window box
14                        |  07:00    09:00    11:00    13:00    15:00   |  hour ticks
4
    |<------- 176 ------->|8|<------------------ fills ----------------->|
```

- **Archive track:**
  - Presence bars use a new key, `AppMinimapPresenceBrush`. It is a semantic key with the same values
    as `AppSecondaryForegroundBrush`. Inside the page the bars use `AppAccentBrush`.
  - The 2 px `AppAccentBrush` underline marks the page. There is no window tick.
  - The marker lane shows operator markers only, in `AppMarkerLineBrush`.
- **Detail track:**
  - The filled box (`AppAccentFillBrush`, 2 px `AppAccentBrush` border) always means the chart window.
    A cut edge draws no border.
  - ➕ As built a window that misses the page hides the box: one wholly outside a day's widened hours, or
    the first window width after midnight on a 24/7 archive (`trend-interaction.md#archive-overview-minimap`).
  - Recipe runs on a Day page use a new key, `AppRecipeRunFillBrush`: the `AppRecipeMarkerLineBrush`
    hue at about 0.15 opacity. It joins the opacity `ThemeTests` case. An interrupted run gets a 2 px
    edge at its end.
  - Operator marker ticks stay at both units.
- **Controls:**
  - A fixed 176 px left column, an 8 px gap, row padding `8,4`.
  - 32x32 Semi buttons with chevron `PathIcon`s.
  - The Day/Week toggle reads `OneWay` and writes through `SetPageUnitCommand`.
  - ➕ As built: one `Week` `ToggleButton`, checked on a chosen Week, in place of the `[Day|Week]` pair
    the layout above draws (the Russian toggle alone takes 93.5 of the 96 px); it writes through
    `TogglePageUnitCommand`.
  - Tooltips name the unit and the gesture.
- **Labels:**
  - Day: `Fri 14 Aug 2026`. Week: `11-17 May 2026`, inclusive.
  - Both use `CultureInfo.CurrentCulture` patterns, with genitive months in Russian.
- **Navigation bar group:**
  - It goes after `Sticky` and before `Delta`.
  - The separators become a style class, so no two elements share the name `GroupSeparator`.
  - The `Markers` `DropDownButton` lists operator markers newest first:
    `label · ddd d MMM yyyy HH:mm`. Its brushes are read back in `ThemeTests`.
  - Russian caption widths are measured before the widths are fixed.
- **`Go` menu:**
  - It holds previous and next page, previous and next run, latest run, and previous and next marker.
  - It has `Markers...`, a command that opens the marker list as a flyout. The flyout anchors to the
    `Markers` button, or to the menu bar when the navigation bar is hidden.
  - ➕ As built the item reads "Operator markers..." ("Метки оператора..."), and the page steps read
    "Previous/Next day or week".
- **Gestures**, run by `MainWindow.OnKeyDown` over the table in `Navigation/WindowGestures.cs` (➕ as built
  it runs the `Go` item whose `InputGesture` matches, `AppMenuBar.GoItemFor`) on a key no control handled and never on one typed into a `TextBox` (a window `KeyBinding` runs ahead of the focused
  `TextBox` and took Ctrl+Left from it, measured in Task 18):

| Action | Gesture |
| --- | --- |
| Previous / next page | Alt+Left / Alt+Right |
| Previous / next recipe run | Ctrl+Left / Ctrl+Right |
| Latest run | Ctrl+End |
| Previous / next operator marker | Ctrl+Shift+Left / Ctrl+Shift+Right |
| Marker list | Ctrl+M |

### States

| State | Archive track | Detail track | Controls |
| --- | --- | --- | --- |
| Empty archive | blank | hint "No rows in the archive" | disabled |
| One-day archive | one block, full underline | the day | page steps disabled with a tooltip |
| Page with no data | underline only | hint "No rows on Sat 15 Aug" (➕ as built the full page label with the year, "No rows on Sat 27 Dec 2025" or the week range) | enabled |
| Failed band read | unchanged | hint "Not read, see messages" | enabled |
| Failed day-spans read | blank | band | page steps take plain calendar steps |
| ➕ Day-spans read pending, no spans held | blank | band | steps disabled, an archive press moves nothing |
| No recipe markers | unchanged | no shading | run steps disabled, tooltip "No recipe runs" |

## What Goes Where

- **Implementation Steps**:
  - seeder, Core, provider and UI code and tests;
  - the six-month measurements taken in this repository;
  - each phase's `docs/architecture` and AGENTS.md updates.
- **Post-Completion**:
  - the decision on issues #75, #76 and #78;
  - a look on a plant archive;
  - a settings key for the working hours, if a plant archive shows the derived hours are wrong.

## Implementation Steps

### Phase A: the working-day stand

### Task 1: Shift schedule model

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ShiftSchedule.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/ShiftScheduleTests.cs`
- ➕ Later split: the generation tests moved to `ShiftsGenerationTests.cs`, the marker-plan tests to
  `RecipeMarkerWriterTests.cs`, and the shifts options to `BenchOptions.Shifts`

- [x] create `ShiftSchedule` with `IsOn`, `Session`, `RecipeRuns` and `Clip`, pure in `(seed, day)`
- [x] the last session extends to `End`; the span starts at the first session start at or after `Start`
- [x] write tests: determinism; no session on a weekend or in the maintenance week; sessions 6-10 h from 07:00-08:30; runs inside their session
- [x] write tests for edges: a start on a Saturday, a start at 02:00, an `End` on a Sunday, an `End` at 03:00, a one-day span
- [x] run tests - must pass before task 2

### Task 2: Generate rows under a schedule

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs`, `BreakPlan.cs`, `RawLayerGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/ShiftScheduleTests.cs`, `RawLayerGeneratorTests.cs`
- ➕ Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LatticeValues.cs` (the value seam)
- ➕ Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs`, `SemiPlot/SemiPlot.Tests.Unit/BenchRows.cs`

- [x] add `Schedule` (`Continuous | Shifts`) to `SeederOptions`, defaulting to `Continuous`
- [x] `BreakPlan.Create` takes the clipped span and the off periods under `Shifts`; `Continuous` is unchanged
- [x] replace `HeldValue` / `ValueAt` with a value seam keyed on the lattice index; `Shifts` holds the idle level between runs
- [x] write the invariant test: every `Continuous` row's value equals `SyntheticValueWalk.Value/LogValue` at `index - index % stride`
- [x] write tests: 14 days of `Shifts` with no weekend row, q=16 and q=32 on every inner session edge, idle values between runs
- [x] write a `Shifts` case of the follow-resume test: a continuous follow run resuming a `Shifts` edge repeats no row and leaves no hole
- [x] generate the standard slice before and after the change and diff it; record the empty diff in the PR description (empty: four SHA-256 digests identical, see the progress log)
- [x] run tests - must pass before task 3

### Task 3: `converge` keys

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`, `ConvergeOptions.cs`, `Converge.cs`, `Program.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SeederCommandTests.cs`

- [x] add `--schedule`, `--days` and `--pens` to `converge`; `ConvergeOptions` carries them; `Converge.cs:41-48` passes them on
- [x] `ReportPlan` prints the schedule, and the session count under `shifts`
- [x] write tests: each key parses; a bad schedule name, `--days 0` and too many `--pens` fail with the option named
- [x] write tests: `converge` without the new keys builds today's `SeederOptions`
- [x] run tests - must pass before task 4

### Task 4: Streamed seeding and recipe markers

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeedFiller.cs`, `RawLayerGenerator.cs`, `Program.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RecipeMarkerWriter.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/StreamedSeedTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/ShiftSeedTests.cs`
- ➕ Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Converge.cs`, `LiveTailGenerator.cs` (`AppendWindow` became the lazy `WindowRows`)
- ➕ Modify: `SemiPlot/SemiPlot.Tests.Unit/ShiftScheduleTests.cs` (the marker plan, Acceptance Evidence 5)
- ➕ Modify: `AGENTS.md`, `docs/architecture/bench.md` (the `WindowRows` name only)

- [x] `SeedFiller` yields rows lazily, one calendar day at a time (raw, then thinned), into one `WriteAsync`; `SeedFillResult` carries per-layer counts
- [x] after the COPY commits, write each run's start marker and its closing marker, locked, as `scada_writer`, with `t` bound as `timestamptz` through `TimeZoneInfo.Local`
- [x] write tests: the streamed rows of a three-day run equal one materialised pass, for both schedules
- [x] write the integration test on a `CloneSource.Provisioned` clone: no Saturday row; break markers at inner session edges; one start and one closing marker per run; a marker reads back at its instant through `PostgresMarkerStore`
- [x] run tests - must pass before task 5

### Task 5: Phase A documentation, baseline and gate

**Files:**
- Modify: `docs/architecture/bench.md`, `AGENTS.md`
- ➕ Modify: `docs/architecture/postgres-topology.md` (the partition sentence)

- [x] document the schedule, the clipping, the `converge` keys, the recipe markers and the six-month stand command in `bench.md`; update the AGENTS.md seeder lines
- [x] run the six-month `converge`; record wall time, peak working set, row counts per layer, marker count and `pg_database_size`, with the date and the machine (62.7 s, 89.5 MB, 11.36 M raw rows, 4 120 markers, 1 703 MB)
- [x] start the viewer at `--logging-level debug`; record the extent, archive days and marker reads, today's band read, and the chart's frame time with the markers loaded (frame time from a follow capture; no drag capture, see `bench.md#the-six-month-stand`)
- [x] if the seed passes 10 minutes or 1 GB peak working set, mark ⚠️, lower `--pens`, and record the density used (not triggered: 62.7 s and 89.5 MB at `--pens 50`)
- [x] run `dotnet build SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet test SemiPlot.slnx` - all must pass before task 6
- ➕ `postgres-topology.md` carried the same stale partition claim and is corrected with `bench.md`

### Phase B: the two-track minimap

### Task 6: Archive day-spans read

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`, `PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`, `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/PostgresArchiveDaySpansReadTests.cs`
- ➕ Create: `SemiPlot/SemiPlot.Core/Data/ArchiveDaySpan.cs`
- ➕ Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs` (the pass-through reaches the provider)
- ➕ Modify: `docs/architecture/data-integration.md` (the interface block and the measured gap under Read cost)

- [x] add `ArchiveDaySpan` and `QueryArchiveDaySpansAsync`; the statement uses the lateral min/max pair per day partition; times through `ArchiveTimeConverter.ToUtc`
- [x] write the integration test on the seeded template: spans equal min and max raw `t` per day; an empty day is absent; failures map with `semiplot_tags, trends`
- [x] add the statement-text row and an `ExplainPlanTests` case beside `ArchiveDays_ReadsThroughTheIndex`
- [x] measure both reads over six months with `EXPLAIN (ANALYZE)`; at 100 ms or less move `TimeWindowPanelViewModel`, the export calendar and `FakeDataProvider` to the spans and delete `ArchiveDays` / `BindArchiveDays`; otherwise record the gap (gap recorded: spans 167-176 ms, days 15-20 ms; both stay)
- [x] run tests - must pass before task 7
- ⚠️ for Task 10: the spans read over six months (167-176 ms execution, 13-44 ms planning) costs more than the extent read (80-134 ms, Task 5); the newest day alone executes in 4.5-4.9 ms, so the plan's fallback (one full read, then the newest day only) is the cheap path for re-reads

### Task 7: Paging model

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/MinimapPage.cs`, `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MinimapPageTests.cs`

- [x] create `MinimapPageUnit` and `WorkingHours`, `Snap`, `Step`, `Land`, `SnapToData`, each taking the archive zone
- [x] write tests: P5/P95 rounding; widening in whole hours; 24/7 gives 00:00-24:00; Week is Monday to Monday
- [x] write tests: `Step` skips a weekend and a maintenance week and returns none past the extent; `Land` keeps the offset or moves to the first sample; `SnapToData` picks the nearer day
- [x] run tests - must pass before task 8

### Task 8: Marker navigation model

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/MarkerNavigation.cs`, `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MarkerNavigationTests.cs`

- [x] create `RecipeRun`, `Runs`, `NextRun`, `PreviousRun`, `LatestRun`, `Frame`, `NextMarker`, `PreviousMarker`
- [x] write tests: start with end; start with interruption; open start to the next start or to the newest sample; orphan close skipped
- [x] write tests: steps from the window centre; none at either end; the frame margin; operator stepping ignores recipe kinds
- [x] run tests - must pass before task 9
- ⚠️ for Task 16: a run or marker before `FirstSample` frames into a window that `PlaceFrom` clamps, so its centre can leave the run and `PreviousRun` returns the same run again; Task 16 owns the filter or the `canExecute` rule

### Task 9: Minimap geometry

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs`, `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MinimapGeometryTests.cs`
- ➕ Create instead (`MinimapGeometry.cs` is 230 lines): `SemiPlot/SemiPlot.Core/Trends/ArchiveTrackScale.cs`, `MinimapTrackGeometry.cs`, `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/ArchiveTrackScaleTests.cs`, `MinimapTrackGeometryTests.cs`
- ➕ Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapPage.cs` (`Midnight`, `WallClock` and `UtcOfWallClock` become internal for the tracks)

- [x] add the day-snapped scale, the day-gap rule, the underline, the clipped window box with cut-edge flags, and the tick placement
- [x] write tests for each function, including a window straddling a page edge and a page narrower than the 8 px underline
- [x] run tests - must pass before task 10

### Task 10: Archive presence feed

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Minimap/ArchivePresenceFeed.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/ArchivePresenceFeedTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`

- ➕ Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs` (`archiveZone` reaches `MinimapViewModel` now, because the read takes wall-clock days; Task 11 keeps the pager half), `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapStand.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- [x] read the spans on an applied extent and on a new day; after a failure retry on `NewestSampleMoved` at most once a minute; extend the newest day from `Navigation.NewestSample`
- [x] build and dispose the feed in `MinimapViewModel`; route failures through `OutageReport`
- [x] write tests: spans after an extent; one more read on a day rollover, none within a day; the retry cadence; the live day's extension; a failure reaches the message panel
- [x] run tests - must pass before task 11
- ⚠️ for Tasks 11 and 13: every live sample on the newest day republishes `Spans` with a new `LastUtc`, about once a second; rebuild what depends on `Day` alone (presence bars, step targets) only when the day list changes, and the page only when `WorkingHours` or the page's own widening changes

### Task 11: Minimap pager

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Minimap/MinimapPager.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapPagerTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`, `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`

- [x] create the pager: the chosen unit written only by `SetPageUnitCommand`; the shown unit and the page derived with `ToProperty`; `PreviousPage` / `NextPage` landing through `Land` and `SetWindow`
- [x] archive-track navigation through `SnapToData`; detail-track navigation in page fractions; the calendar fallback without spans
- [x] pass `archiveZone` from `TrendWindow.Build` into `MinimapViewModel` and the pager (`TrendWindow.Build` already passes it since Task 10; `MinimapViewModel` hands it to the pager, so `TrendWindow.cs` is unchanged)
- [x] write tests: Friday to Monday at the same clock time; an empty offset lands at the first sample; a pan past the page turns it; zooming out and back returns to the chosen unit; a weekend click lands on data; the fallback steps
- [x] run tests - must pass before task 12
- ⚠️ for Task 12-14: the page follows the chart window's start (`Navigation.From`), because `Land` places the window's start on the target page; `MinimapViewModel` keeps its extent-wide strip members until Tasks 12-14 replace them

### Task 12: Detail band over the page

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs`, `MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapBandFeedTests.cs`, `MinimapViewModelTests.cs`

- ➕ Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs` (reports the strip width, draws the band over the page), `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapStand.cs`, `MinimapViewStand.cs`, `MinimapViewTests.cs`; `MinimapViewModelTests.cs` needed no change
- [x] the feed takes `(from, to, columns)` from the pager; `SetStripWidth` feeds the columns rule with the 2500 cap; a width change inside a 250 step issues no read (the page comes from the pager; the column count lives in the feed, which `MinimapViewModel.SetStripWidth` reaches)
- [x] `FakeDataProvider` splits band reads by "a multiple of 250"; `MinimapBandFeedTests.cs:363` becomes "never a power of two in 256..2048"
- [x] rewrite the existing tests that assert extent-wide bounds at 250 columns
- [x] write `ADayPageReadsTheWorkingHoursAtTheStripWidth` and tests for a page change, a width change and the cap
- [x] run tests - must pass before task 13
- ⚠️ for Task 15: `MinimapBandFeed.MinimapColumns` is gone (now `ColumnStep` and `MaximumColumns`); `trend-interaction.md:796-806`, `charting.md:895` and `data-integration.md:868` still name it or the fixed 250 columns
- ⚠️ for Task 14: the band reads at start over the calendar day, then again over the working hours once the spans land; the old strip draws the band over `Pager.Page` while its window box, ticks and hover stay extent-wide until Tasks 13-14

### Task 13: Archive track view

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`, `MinimapView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewTests.cs`, `MinimapPointerInputTests.cs`, `MinimapHoverTests.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- ➕ Create: `SemiPlot/SemiPlot.UI/Minimap/MinimapArchiveTrack.cs` (the lanes' `Render` control), `MinimapView.ArchiveTrack.cs` (the track's layout and pointer half of the view; later its own control, `ArchiveTrackView.axaml` and `.axaml.cs`, with the label helpers in `TrackLabels.cs`), `MinimapLabels.cs` (the culture date labels Task 14 extends)
- ➕ Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs` (`ArchiveScale(width)`, `OperatorMarkerTimes`), `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapStand.cs`, `MinimapViewStand.cs`; `Resources.resx` and `Resources.ru.resx` unchanged (the track carries dates only)
- [x] add the archive track and the left column's label row; draw presence, the underline and the operator lane from the Task 9 geometry; month labels and the hover day (the 176 px column stays empty for Task 14's controls; the detail strip and its label row moved into the right column)
- [x] add `AppMinimapPresenceBrush` to both variants
- [x] a click or drag on the archive track goes to the pager's archive navigation
- [x] write tests with `HeadlessInput`: the track realises; a weekend click lands on data; the hover names the day; `ThemeTests` cover the new key
- [x] run tests - must pass before task 14
- ⚠️ for Task 14: the strip's window box, ticks, hover and end labels are still extent-wide; the left column is an empty 176 px grid column (`MinimapView.axaml`, `ColumnDefinitions="176,8,*"`)

### Task 14: Detail track and controls

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`, `MinimapView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewTests.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- ➕ Create: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.DetailTrack.cs` (the detail track's layout and pointer half of the view), `MinimapRunShading.cs` (the runs' `Render` control), `MinimapConverters.cs` (page label, `IsWeek`, `OtherUnit`), `MinimapHint.cs`; `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapDetailTrackTests.cs`, `MinimapLabelsTests.cs`
- ➕ Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapTrackGeometry.cs` (`RunShades`), `MinimapGeometry.cs` (`PlaceHoverTime` deleted with the end labels), `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs` (extent-wide strip members deleted; `IsArchiveEmpty`, `ShadedRuns`, `Hint`, `ArchiveZone`), `MinimapPager.cs` (`Window`), `MinimapBandFeed.cs` (`HasReadFailed`), `MinimapLabels.cs`, `MinimapView.ArchiveTrack.cs`, `SemiPlot/SemiPlot.UI/Styles/Icons.axaml` (single chevrons), `docs/architecture/ui-text.md` (the minimap's keys); tests `MinimapGeometryTests.cs`, `MinimapTrackGeometryTests.cs`, `MinimapViewModelTests.cs`, `MinimapHoverTests.cs`, `MinimapPointerInputTests.cs`, `MinimapViewStand.cs`, `MainWindowViewTests.cs` (the label row is `PageTickRow`)
- [x] draw the band, the clipped window box, run shading on the Day page only, and hour or day ticks; add `AppRecipeRunFillBrush` with its opacity case
- [x] add the page steps, the Day/Week toggle (`OneWay`, `SetPageUnitCommand`), the page label, the tooltips and the states table's hints (one `Week` toggle, checked on Week pages, in place of a two-segment control)
- [x] write tests with `HeadlessInput`: `›` moves the chart a working day; the toggle switches the page; each state's hint shows; a run shades at Day and not at Week
- [x] confirm `MainWindowViewTests` and `AppMenuBarTests` pass
- [x] run tests - must pass before task 15
- ⚠️ for Task 15: the strip's end labels are gone (the row under the strip holds the page's tick labels), recipe markers no longer tick the strip at either unit, and `MinimapViewModel` lost `NavigateToFraction`, `HoverAt`, the window and marker fractions and the end labels; `ui-theme.md`'s consumer columns for `AppSecondaryForegroundBrush` ("Minimap end labels") and `AppMarkerLineBrush` ("every shown marker on the minimap strip") need the new wording, and the two new keys their rows

### Task 15: Phase B documentation and gate

**Files:**
- Modify: `docs/architecture/trend-interaction.md`, `data-integration.md`, `ui-theme.md`, `AGENTS.md`

- ➕ Modify: `docs/architecture/charting.md`, `overview.md`, `bench.md` (the three minimap guard rows), `trend-feature-spec.md` (TM-4), `testing-strategy.md` (stale minimap statements)
- [x] rewrite `trend-interaction.md#archive-overview-minimap` for two tracks, pages, working hours, landing and states
- [x] add the day-spans read and its measurement to `data-integration.md`, including the time-boundary section's list of paths (the measurement sat under Read cost since Task 6)
- [x] add the two palette keys to `ui-theme.md`; update the AGENTS.md minimap lines
- [x] run `dotnet build SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet test SemiPlot.slnx` - all must pass before task 16
- ⚠️ for Task 20: a chart read asks for `ReportedColumnTarget * ColumnFactor`, the unquantized data-area width (256..2048) times 1 to 3, not a power of two, so a 1500 px data area asks for a multiple of 250; `FakeDataProvider.IsBandQuery` would count it as a band read, and the `MinimapBandFeed.ColumnStep` comment and `NoBandColumnCount_IsAChartColumnCount` rest on the power-of-two premise (Context, Test seams); the docs state the real rule
- ⚠️ for Task 18: the page steps' tooltips name Alt+Left and Alt+Right (`ui-text.md#the-minimaps-text`), but no window-level `KeyBinding` exists yet

### Phase C: marker navigation

### Task 16: Run and marker commands

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Navigation/NavigationBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/NavigationBarViewModelTests.cs`

- ➕ Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/NavigationBarTestBuilder.cs` (takes a `FakeMarkerChannel`)
- [x] add `PreviousRun`, `NextRun`, `LatestRun`, `PreviousMarker`, `NextMarker` over `ChartMarkers.All`; each calls `Navigation.SetWindow` and nothing else
- [x] add the operator-marker list, newest first, derived from `ChartMarkers`; `canExecute` follows the markers and the window centre
- [x] write tests: each command moves the window as specified; disabled with no target; a hidden recipe marker still steps; a run near the first sample is framed from `FirstSample` (`TrendNavigationModel.cs:94-98`)
- [x] run tests - must pass before task 17
- ➕ the first-sample floor: a run that ends at or before `FirstSample` is no target, one that starts before it is framed from it, and a marker step whose centred window the floor clamps back onto the current one is none; steps measure from the window centre, or from `FirstSample` when the centre lies before it
- ➕ `GoToMarkerCommand` centres a given marker for Task 17's list items

### Task 17: Navigation bar group and marker list

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Navigation/NavigationBarView.axaml`, `NavigationBarView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/NavigationBarViewTests.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- ➕ Create: `SemiPlot/SemiPlot.UI/Navigation/NavigationConverters.cs` (the list item text)
- ➕ Modify: `SemiPlot/SemiPlot.UI/Navigation/NavigationBarViewModel.cs` (`HasRecipeRuns` for the "No recipe runs" tooltip), `docs/architecture/ui-text.md` (the bar's keys), `docs/architecture/ui-theme.md` (the separator class, the renamed inset test, the `DropDownButton` read-back)
- [x] turn the separators into a style class; add the run group between `Sticky` and `Delta` with chevron steps, `Latest run` and the `Markers` `DropDownButton`
- [x] measure the Russian captions and fix the widths (text buttons keep Semi's padding and size to their caption; the whole Russian bar with a sample delta readout (8759h 59m 59s, -99999.999) measures 1251 px in Inter against the 1264 px the default 1280 px window leaves, pinned by a test)
- [x] rewrite `TheBar_CarriesNoAxisEditor` as an allowlist of the new controls that still refuses a `TextBox`, and the separator test for the style class
- [x] write tests with `HeadlessInput`: next run frames the run; latest run frames the newest; a list item centres its marker; the `DropDownButton` brushes read back in both variants
- [x] run tests - must pass before task 18
- ⚠️ for Task 18: a list item's command binds through `$parent[navigation:NavigationBarView]`, so the same `MenuFlyout` shown from the menu bar finds no bar view model; the `Go` menu's `Markers...` needs its own item binding or a flyout whose `DataContext` is the bar view model
- ⚠️ for Task 18 and later: the Russian bar leaves 13 px inside the default window with the sample readout, and a pen mask wider than five digits overflows it; a further bar control needs a shorter caption somewhere

### Task 18: `Go` menu and gestures

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`, `AppMenuBar.axaml.cs`, `MainWindow.axaml`, `MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`, `MainWindowViewTests.cs`

- ➕ Create: `SemiPlot/SemiPlot.UI/MainWindow/WindowGestures.cs`, later moved to `Navigation/` (the gesture table: the `Go` items' `InputGesture` and the window's key handler read it), `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/WindowGestureTests.cs` (the realised window's gestures and marker list, in place of `MainWindowViewTests.cs`)
- ➕ Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs` (`ShowMarkerListCommand`, `MarkerListRequests`), `SemiPlot/SemiPlot.UI/Navigation/NavigationBarView.axaml`, `NavigationBarView.axaml.cs` (one list, item command bound through `#MarkersButton`, `ShowMarkerList`), `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, `docs/architecture/ui-theme.md`, `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs` (`MenuItemInputGestureTextForeground`, the gesture text read back)
- [x] add the `Go` menu with every step and `Markers...` as a command that opens the list flyout, anchored to the menu bar when the navigation bar is hidden
- [x] bind the gestures table as window-level `KeyBinding`s; every menu item shows its gesture (a window `OnKeyDown` over `WindowGestures` in place of `KeyBinding`s: a `KeyBinding` runs ahead of the focused `TextBox`)
- [x] write tests: every `Go` leaf carries a command; each gesture invokes its command; the actions and `Markers...` work with the minimap and the navigation bar hidden
- [x] write a test: Ctrl+Left typed in a focused `TextBox` (marker label editor, time window panel) moves the caret and steps no run
- [x] run tests - must pass before task 19
- ⚠️ for Task 20/21: the open `Go` menu prints Avalonia's key names (`Alt+Left Arrow`), while the bar's and the minimap's tooltips say `Alt+Left`; `trend-interaction.md` gets the gesture rule (a key no control took, never from a `TextBox`)

### Task 19: Six-month stand after

**Files:**
- Modify: `docs/architecture/bench.md`

- [x] repeat the Task 5 viewer measurements, plus the day-spans read, the band at Day and Week, and the frame time (`bench.md#the-stand-after-the-two-track-minimap`; the Week band and the new-day spans re-read timed in psql only)
- [ ] walk the manual smoke checklist and record each outcome
- ⚠️ handoff: mapped to tests, walk pending. Nobody walked the checklist; each row maps to its covering tests in Post-Completion, and rows 1, 2, 7 and 8 still need a person. Item 10 is weaker than its wording: the frame time comes from follow captures, not a drag, and the Week band and the new-day spans re-read were timed in psql only
- [x] run tests - must pass before task 20
- ➕ the checklist gains rows 7 and 8 from Task 18's findings

### Task 20: Verify acceptance criteria

- [x] verify every requirement in Overview is implemented (the three-PR split is a delivery step for `ship`, not code)
- [x] run `dotnet build SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`
- [x] run full test suite: `dotnet test SemiPlot.slnx` (unit 3039/3039, integration 224/224)
- [x] run every Acceptance Evidence filter, unit and integration, and confirm each passes (item 4 names `MinimapPointerInputTests` for the weekend click, item 8 names its test)
- ➕ `FakeDataProvider` tells a band read by its request, judged as it is issued: the drawn pen alone over the page in force, at a multiple of 250 (`RecognizeBandReadsOf`, installed by `MinimapStand` and the window stand); every other read is the chart's. This supersedes Task 12's "a multiple of 250" split and its power-of-two test, which `AChartReadAtAMultipleOfTheBandStep_IsNoBandRead` replaces. A chart read of only the drawn pen over a fetch range equal to the page at such a count would still pass for the band's

### Task 21: [Final] Phase C documentation

- ➕ Modify: `SemiPlot/SemiPlot.UI/MainWindow/WindowGestures.cs` (`TextOf` and the eight gesture tooltips), `SemiPlot/SemiPlot.UI/Navigation/NavigationBarView.axaml`, `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`, `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx` (the gesture tooltips take `{0}`), `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`, `UI/Minimap/MinimapDetailTrackTests.cs`, `UI/Navigation/NavigationBarViewTests.cs`, `docs/architecture/ui-text.md`, `charting.md`, `overview.md`, `trend-feature-spec.md`
- [x] add the recipe-run navigation, the marker list, the `Go` menu and the gestures to `trend-interaction.md`
- [x] update the AGENTS.md menu rule for the `Go` items and the marker-route lines
- [ ] move this plan to `docs/plans/completed/` (left for delivery)
- ➕ the gesture text: the tooltips print a gesture as the open `Go` menu does ("Alt+Left Arrow"), through `WindowGestures.TextOf`, and `TheOpenGoMenu_PrintsEachItemsGestureBesideItsCaption` compares the two (`ui-text.md#the-gesture-text`)

## Post-Completion

**Manual verification:**
- the two tracks and the run steps on a customer dump or a plant archive, where the day pattern is real
- the manual smoke checklist (Acceptance Evidence), walked by a person on the six-month stand. Task 19
  could not click a desktop window unattended. The headless tests below cover each row as far as they
  reach; a partial row names what only a person can check:

| Row | Covering tests | Coverage |
| --- | --- | --- |
| 1. Working days as blocks, weekend and maintenance gaps | `MinimapViewTests.TheArchiveTrack_DrawsOneBarPerDayWithRows_AndUnderlinesThePage`, `MinimapTrackGeometryTests.PresenceBars_MarkTheDaysOnThePage`, `ArchiveTrackScaleTests.DayGap_IsOnePixel_OnlyAtFourPixelsPerDayOrMore`, `ArchiveTrackScaleTests.DayGap_OfSixMonthsOnNineHundredPixels_IsOnePixel`, `ShiftScheduleTests.NoSessionFallsOnAWeekend`, `ShiftScheduleTests.TheMaintenanceWeekRecursOncePerCycle` | covered; the look of six months of bars is left to a person |
| 2. Day page over the working hours, runs shaded | `MinimapPageTests.WorkingHours_ArePercentilesOfFirstAndLastSamples_RoundedOutToWholeHours`, `MinimapPageTests.Snap_ADayWithSamplesOutsideTheHours_WidensItsOwnPageInWholeHours`, `MinimapBandFeedTests.ADayPageReadsTheWorkingHoursAtTheStripWidth`, `MinimapDetailTrackTests.ARun_ShadesTheDayPage_AndNeitherTheWeekPageNorAHiddenRecipeMarker` | covered; on the stand the viewer read the band over 07:00-22:00 (`bench.md#the-stand-after-the-two-track-minimap`) |
| 3. `›` five times from Monday reaches the next Monday, no empty day | `MinimapViewTests.FiveNextPageClicksFromAMonday_ReachTheNextMonday_EachOnADayWithRows`, `MinimapViewTests.ANextPageClick_MovesTheChartAWorkingDay`, `MinimapPagerTests.NextPage_FromAFriday_LandsOnMondayAtTheSameClockTime_AndPreviousPageReturns`, `MinimapPagerTests.AStepWhoseClockOffsetHoldsNoSample_LandsAtTheDaysFirstSample_KeepingTheWidth`, `MinimapPagerTests.BeforeTheFirstSpansReadAnswers_NothingSteps_AndAFailedReadTurnsToCalendarSteps`, `MinimapPageTests.Step_NextFromFriday_SkipsTheWeekend`, `MinimapPageTests.Step_SkipsAMaintenanceWeek_InBothDirections` | covered |
| 4. A Saturday click lands on the nearer working day | `MinimapPointerInputTests.AClickOnTheArchiveTrackOverAWeekend_LandsOnTheNearerWorkingDaysData`, `MinimapPagerTests.AnArchiveClickOnAWeekend_LandsOnTheNearerWorkingDaysData`, `MinimapPageTests.SnapToData_OnASaturday_TakesTheNearerFriday` | covered |
| 5. Ctrl+Right frames each run, Ctrl+End the newest | `WindowGestureTests.EachStepGesture_RunsItsCommand_WithTheBarAndTheMinimapHidden`, `NavigationBarViewModelTests.RunSteps_FrameTheRunBeforeAndAfterTheWindowCentre_WithATenthOfItsLengthOnEachSide`, `NavigationBarViewModelTests.LatestRun_FramesTheNewestRun_FromAnyWindow`, `NavigationBarViewTests.AClickOnNextRun_FramesTheRunWithATenthOfItsLengthOnEachSide`, `NavigationBarViewTests.AClickOnLatestRun_FramesTheNewestRun` | covered |
| 6. Every step from `Go` and its gesture with the bar and the minimap hidden | `WindowGestureTests.EachStepGesture_RunsItsCommand_WithTheBarAndTheMinimapHidden`, `WindowGestureTests.EachGoStepItem_RunsItsCommandWhenClicked_WithTheBarAndTheMinimapHidden`, `WindowGestureTests.TheMarkerList_OpensFromTheGoMenuOrItsGesture_AndAnItemCentresItsMarker`, `AppMenuBarTests.GoMenu_HoldsThePageTheRunAndTheMarkerStepsEachBoundToItsCommand`, `AppMenuBarTests.EveryGoItem_ShowsItsGesture_AndThatKeyFindsTheItem` | covered |
| 7. Ctrl+Left in the marker label editor on Win32 | `WindowGestureTests.ACaretGestureInAFocusedTextBox_MovesTheCaretAndStepsNothing`, `WindowGestureTests.AGestureTypedIntoATextBox_ThatTheBoxDoesNotUse_StepsNothing` | partial: the headless platform hosts the editor as an overlay in the window, not as a Win32 popup window |
| 8. Alt+Left and Alt+Right on a real keyboard | `WindowGestureTests.EachStepGesture_RunsItsCommand_WithTheBarAndTheMinimapHidden` | partial: `HeadlessInput.Press` sends the arrow with an Alt flag and no Alt key-down, so the menu bar's Alt handling is not exercised |

- `docs/img/main-window.png` (2026-10-05) and its caption in `readme.md` show the old single-strip
  minimap. Retake the screenshot on the six-month stand after merge; an unattended run cannot take it.

**Delivery:**
- The Overview asks for three PRs, one per phase, and the branch holds all three phases in one line of
  commits. Delivery splits it at the phase ends rather than rewriting history: `241aefe` closes Phase A,
  `1c763f7` closes Phase B, and the commits after `1c763f7` form the third: Phase C and the review fixes,
  which also touch Phase A and B files and so ride with the last PR. `ship` collapses a branch to one
  commit, so each PR is cut from its own range.

**Decisions outside this plan:**
- issues #75, #76 and #78: #78 is largely this plan, #75's coarse cache is rejected in
  `docs/plans/backlog.md`, and #76's remaining measurements are Tasks 5, 6 and 19
- a settings key for the working hours, if the derived hours prove wrong on plant data

**Executed by exec:**
- branch: two-track-minimap

## Verify it yourself

Every test named below is new on this branch, so on `master` (`d74605b`) each filter matches nothing;
on the branch tip each passes.

1. Gates: `dotnet build SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes`,
   `dotnet test SemiPlot.slnx`. Expect 0 errors, exit 0, unit 3050/3050 and integration 226/226.
2. Band resolution: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ADayPageReadsTheWorkingHoursAtTheStripWidth"`.
   It asserts one band request over 07:00-17:00 at 1500 columns, a 24 s bucket; `master` reads the
   whole extent at 250 columns.
3. Paging, runs and the views: the same command with `--filter "FullyQualifiedName~MinimapPageTests|FullyQualifiedName~MarkerNavigationTests|FullyQualifiedName~MinimapViewTests|FullyQualifiedName~MinimapPointerInputTests|FullyQualifiedName~NavigationBarViewTests|FullyQualifiedName~WindowGestureTests"`.
4. The schedule: `--filter "FullyQualifiedName~ShiftScheduleTests|FullyQualifiedName~ShiftsGenerationTests|FullyQualifiedName~RecipeMarkerWriterTests"`
   in the unit project, and `--filter "FullyQualifiedName~ShiftSeedTests|FullyQualifiedName~PostgresArchiveDaySpansReadTests"`
   in `SemiPlot.Tests.Integration` (needs Docker).
5. The six-month stand, by hand:
   1. Start the bench container and copy `ConfigFiles` to `SemiPlot\Artifacts\dev-config` as
      `docs/architecture/bench.md#the-six-month-stand` shows.
   2. Run the converge command from Technical Details > Keys (`--schedule shifts --days 183 --pens 50
      --change-seconds 30`). Expect about 60 s and `markers written` near 4 120.
   3. Start the viewer on that config. On `master` the strip is one solid band; on the branch the
      archive track shows weekday blocks with weekend gaps, and the Day page shows separate run pulses.
   4. Walk the manual checklist rows in Post-Completion. Rows 7 and 8 have no headless equivalent:
      Ctrl+Left inside the marker label editor must move the caret, not the chart, and Alt+Left /
      Alt+Right on a real keyboard must turn the page.
