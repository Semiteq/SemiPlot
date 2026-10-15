# Time window panel

Issues: #71, #79.

## Overview

- A click on the chart's time axis opens a time window panel, a flyout at the pointer modelled on the axis scale
  panel. A drag on the time axis still pans.
- The panel sets the view window three ways:

  | Control | Effect | Sticky |
  | --- | --- | --- |
  | Start: date + `HH:mm:ss` | sets the left edge exactly, the right edge stays | dropped |
  | End: date + `HH:mm:ss` | sets the right edge exactly, the left edge stays | dropped |
  | Width: typed `[h]:mm:ss` | sets the width exactly, the right edge stays | kept |
  | Width presets dropdown: 1 min, 2 min, 3 min, 5 min, 10 min, 30 min, 1 h | the same as a typed width, at once | kept |

- Typed values apply on Enter and when the panel closes by a click outside. Escape closes and applies nothing.
- Each date is a `CalendarDatePicker`. A day between the first and the newest archive sample that holds no
  archive row is a blackout date: drawn struck through and not selectable. "Holds a row" means at least one
  layer-0 row of any catalogue pen dated that local day.
- A preset or typed width is set exactly, off the 1.25 zoom ladder. The next wheel notch snaps the width back onto
  the ladder. The in-and-out round trip of TM-2 holds from a width on the ladder.
- Every change goes through two new navigation calls, `SetWindow` and `SetWidth`, and raises `WindowChanged`
  like every other gesture, so the history re-query, the axis, the minimap and the navigation bar follow.
- Covers TM-6 (`docs/architecture/trend-feature-spec.md:37-39`) except the quick interval "ending at now": a
  width keeps the right edge, which under sticky is now.

## Context (from discovery)

Navigation:
- `SemiPlot.Core/Trends/TrendNavigationModel.cs`: width limits 1 s and 365 days (`:11-12`); `Pan` keeps the width
  and clamps `From` at `FirstSample` (`:36-52`); `Zoom` snaps through `QuantizeWidth` (`:54-87`); `ClampWidth`
  and `ClampWidthSpan` (`:114-134`). No member sets the window or the width directly. `To` is never clamped
  against the live edge.
- `SemiPlot.UI/Chart/ChartNavigationController.cs`: `PanBy`, `JumpToNow`, `SetSticky` (`:141-165`) each end in
  `ApplyWindowChange` (`:201-205`), which recomputes the layer and raises `WindowChanged` with a re-query.
  "Now" is `_liveEdge`, the newest realtime timestamp, never a clock read (`:167-172`); `NewestSample` (`:47`)
  is the newest sample seen. Edges are UTC.
- `WindowChanged` subscribers: `MinimapViewModel.cs:178`, `AppStatusBarViewModel.cs:45`,
  `NavigationBarViewModel.cs:30`, `TrendChartView.axaml.cs:184`, `TrendChartViewModel.cs:73`.
- `MinimapViewModel.NavigateToFraction` (`:145-158`) expresses "go to T" as `PanBy(target - currentCenter)`.
- Round trip: `TrendNavigationModelTests.Zoom_InThenOut_ReturnsToOriginWindowWithinTolerance` (`:147-175`);
  `docs/architecture/trend-interaction.md:123-127`; `trend-feature-spec.md:23`.

Chart input:
- `TrendChartView.axaml.cs:330-363`: `OnPointerPressed` routes through
  `ChartPressRouter.Route(IsOverActiveAxis(position), KeyModifiers, ActiveLeftButtonTool)`; `EditAxisScale` opens
  the axis panel, `PlaceDeltaCursor` places a cursor, anything else `BeginDrag` (`:380-389`).
- `ContinueDrag` (`:417-428`) shifts the pen for `ShiftActivePen` and pans for every other action.
  `OnPointerReleased` (`:430-444`) picks a pen only for `!IsStarted && PanOrPickPen`.
- A press on the time axis was `PanOrPickPen` at the plan's base (697bbf6): a drag panned, and a click ran a
  pick that selected nothing, because `SemiPlot.UI/Chart/EnvelopeLine.cs:120-125` returns an infinite distance
  outside the data rect. Master moved on: since c94a37d (#128) a pick that misses, a click outside the data
  area included, drops the active pen's emphasis, so on master a time-axis click is no longer a no-op. This
  branch routes the time axis to `PanOrEditTimeWindow`, which never reaches `PickPenAt`; the reconciliation
  is under Post-Completion.
- `ChartPressRouter.cs:18-33`: axis hit, then Ctrl, then the tool. `ClickSlopPixels = 4.0` (`:16`).
- `ChartAxisRegion.cs:25-65`: Y-axis band from `plot.RenderManager.LastRender.Layout` (`DataRect`,
  `PanelSizes[axis]`, `PanelOffsets[axis]`), bounded vertically by the data rect. Nothing reads the bottom axis
  panel today.

Axis scale panel, the model for the new panel:
- `TrendChartView.axaml:19-25`: `<Flyout Placement="Pointer">` attached to `PlotControl`;
  `TrendChartView.axaml.cs:37`, `:53-54` read it through `FlyoutBase.GetAttachedFlyout`; `OpenAxisScalePanel`
  (`:372-378`) shows it when `Seed()` is true; `CloseRequests` hides it (`:164-167`). The view tests find the
  flyout the same way (`AxisScalePanelViewTests.cs:369-377`).
- `AxisScalePanelViewModel.cs`: `Seed()` (`:141-162`), `CommitBounds()` writes and re-seeds and the panel stays
  open (`:165-171`), `CancelCommand`, `CloseRequests` (`:41`), `ReportFailure` (`:182-185`).
- `AxisScalePanel.axaml:64-67`: Enter is a `KeyBinding` to `CommitBoundsCommand`; Escape to `CancelCommand`
  (`:16-19`). `AxisScalePanel.axaml.cs:16-48` commits when keyboard focus leaves the bound group, paired in
  `OnLoaded`/`OnUnloaded`. The host handles no flyout `Closed` (`trend-interaction.md:318-319`).
- `TrendChartViewModel.cs:74` builds `AxisScale = new AxisScalePanelViewModel(this)`. The chart view model keeps
  `_coordinator`, `_messagePanel` and `_logger` private (`:26-28`), takes `uiScheduler` as a constructor parameter
  (`:47-52`) and exposes only `ReportFailure(Exception)` (`:773-776`). It is constructed at `TrendWindow.cs:77`,
  `ChartTestBuilder.cs:45` and `:73`, `BreakRenderArchiveJourneyTests.cs:72` and `LiveEdgeArchiveJourneyTests.cs:148`.
- `TrendWindow.Build` receives `TimeZoneInfo archiveZone` (`TrendWindow.cs:65`) and hands it to the export
  (`:122`). An Rx subscriber on the UI scheduler reports a failed result with `TryReportFailure`
  (`MinimapViewModel.cs:248-251`, `Messages/ResultReporting.cs:67-93`).

Date and time entry:
- `SemiPlot.UI/Export/ExportRangeForm.cs`: the four range fields (`:25-63`), `TimeFieldFormat = "HH:mm:ss"`
  (`:10`), private `ParseTime` (`:135-141`, `TimeOnly.TryParseExact`, invariant culture) and `FormatTime`
  (`:143-146`), validation flags (`:75-83`), and `Problem` (`:85-121`), whose first five arms are a generic
  date-time range check and the last two (`SpanOverLimit`, `NoPenTicked`) are the export's own.
- `CsvExportWindow.axaml:48-55`: `CalendarDatePicker` with `SelectedDateFormat="Custom"` and
  `CustomDateFormatString={x:Static text:Resources.ExportDateFormat}`.
- `Styles/Forms.axaml:5-28`: the `invalid` classes and the `form-message` line.
- `LocalTimeAxis.cs:8-20` draws the axis in the machine zone (`ToLocalTime`).

Data:
- `SemiPlot.Core/Data/IDataProvider.cs`: five members; `QueryArchiveExtentAsync` (`:27`) returns
  `Task<Result<ArchiveExtent>>`. Implementations: `PostgresDataProvider.cs:27` and the test fake
  `SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:19`. `TrendCoordinator.QueryArchiveExtentAsync`
  (`:100-103`) passes through.
- `PostgresDataProvider.QueryArchiveExtentAsync` (`:196-210`) runs `ArchiveStatements.ArchiveExtent`, rooted at
  `semiplot_tags` with one `CROSS JOIN LATERAL ... LIMIT 1` probe per `id` at `l = 0`
  (`ArchiveStatements.cs:150-161`; the lateral form keeps the index-edge plan), and maps failures with
  `ArchiveStatements.ArchiveExtentRelations` (`"semiplot_tags, trends"`, `:57`).
- Cancellation pattern: `OpenConnectionAsync(ct)` (`:422`), `ExecuteReaderAsync(ct)` (`:427`),
  `catch (OperationCanceledException) { throw; }` (`:166-171`).
- `ExplainPlanTests.cs:41` fails any `Seq Scan on tpYYYYmMMdDD`; a statement joins with one `[Fact]` calling
  `ExplainAsync` (`:336-360`) on `AnalysedArchive`, a one-day clone (`ArchiveTemplate.cs:13-21`).
  `ArchiveStatementTextTests` pins each statement and binder (`data-integration.md:264-268`).
- `PostgresExtentReadTests.cs:45-74` runs the dropped-table and empty-catalogue cases on per-test clones, because
  `SeededArchive` is one clone per class (`docs/architecture/bench.md:143-145`).
- Partitions are local-day ranges `[00:00, next 00:00)` (`scada-archive-observed.md:223-224`); the viewer
  converts in the machine zone, which is the archive's zone (`data-integration.md:776-779`).
  `ArchiveTimeConverter.ToUtc` resolves a skipped local time through `BaseUtcOffset` (`:18-21`).
- `docs/architecture/scada-archive-observed.md`: a running server writes at least one row per variable per day,
  a constant one included (`:338-345`), and start and stop markers (`:206-212`). Partition existence does not
  mean rows: tomorrow's partition is created ahead (`:131`).

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task)
- complete each task fully before moving to the next
- every task includes new or updated tests for the code it changes; all tests pass before the next task
- update this plan when scope changes during implementation

## Testing Strategy

- **unit tests**: `[Fact]` for the Core model, the width text and the forms; `[AvaloniaFact]` for the view
  model, the realised panel and the chart pointer input. Clicks, clears, typing and keys go through
  `SemiPlot.Tests.Unit/UI/HeadlessInput.cs`. View model tests run over a `TestScheduler`, never an immediate one.
- **integration tests**: the day read against seeded clones in `ArchiveDatabaseCollection`, and one
  `ExplainPlanTests` fact.
- **e2e tests**: the project has no browser e2e suite; the headless view tests carry the gesture coverage.
- Every new test class carries the `Component`, `Area` and `Category` traits.

## Acceptance Evidence

Today there is no way to type a window or a width: no `SetWindow` or `SetWidth` exists
(`ChartNavigationController.cs:135-165`), and a click on the time axis opens nothing: at the plan's base it
selects nothing, and on master since #128 it drops the active pen's emphasis as any missed pick does.

Automated, unit project:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj `
  --filter "FullyQualifiedName~TrendNavigationModelTests|FullyQualifiedName~ChartNavigationControllerTests|FullyQualifiedName~WindowWidthTextTests|FullyQualifiedName~WallClockRangeFormTests|FullyQualifiedName~TimeWindowFormTests|FullyQualifiedName~TimeWindowPanelViewModelTests|FullyQualifiedName~TimeWindowPanelViewTests|FullyQualifiedName~ChartPressRouterTests|FullyQualifiedName~ChartAxisRegionTests|FullyQualifiedName~ChartPointerInputTests|FullyQualifiedName~ExportRangeFormTests|FullyQualifiedName~ArchiveStatementTextTests"
```

It must pass with these tests present:

- `TrendNavigationModelTests.SetWidth_FiveMinutes_KeepsTheRightEdgeAndSetsExactlyFiveMinutes`: `To` unchanged,
  `Width == 00:05:00`.
- `TrendNavigationModelTests.SetWidth_ThenOneWheelNotch_LandsOnTheLadder`: after `SetWidth(5 min)` and
  `Zoom(0.8)` the width is `1.25^25 s` (264.7 s) within 1 ms.
- `TrendNavigationModelTests.SetWindow_DropsStickyAndSetsBothEdgesExactly`.
- `ChartPointerInputTests.AClickOnTheTimeAxis_OpensTheTimeWindowPanel` and
  `ADragOnTheTimeAxis_PansAndOpensNothing`.
- `TimeWindowPanelViewTests.TypingAStartTimeAndEnter_MovesTheLeftEdgeOnly`.
- `TimeWindowPanelViewTests.OpeningTheStartCalendar_WritesNothing`.
- `TimeWindowPanelViewTests.PickingTheFiveMinutePreset_SetsTheWidthAndKeepsSticky`.
- `TimeWindowPanelViewTests.ADayWithoutRows_IsABlackoutDateOfThePicker`.
- `TimeWindowPanelViewTests.AWindowStartingOnAnEmptyDay_SeedsWithoutThrowing` and
  `AReopenOntoABlackoutDay_SeedsWithoutThrowing`.
- `TimeWindowPanelViewTests.TypingADateAndEnter_MovesTheLeftEdgeToThatDate`.

Automated, integration project (needs Docker):

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj `
  --filter "FullyQualifiedName~PostgresArchiveDaysReadTests|FullyQualifiedName~ExplainPlanTests"
```

- `PostgresArchiveDaysReadTests` returns exactly the seeded days, and no day for a range outside the seed.
- `ExplainPlanTests.ArchiveDays_ReadsThroughTheIndex`: `trends` is reached by an index scan under the
  per-pen probe, and no `Seq Scan` on a day partition.

Measured once, recorded in `data-integration.md#read-cost`: the day read over a 31-day seed
(`ArchiveSeeder --days 31`), `EXPLAIN (ANALYZE)` showing the partitions outside each probed day never executed,
and the wall time of the read.

Manual, on Windows with the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. A click on the time axis labels opens the panel at the pointer; a drag there pans the chart.
2. The fields show the window as the axis shows it, in local time.
3. Typing `0:05:00` in Width and Enter gives a 5-minute window with the right edge in place; sticky stays on.
4. The presets dropdown sets 1 min to 1 h the same way.
5. Typing a start time and Enter moves the left edge only and switches "Следовать за текущим временем" off.
6. Picking a date in the calendar moves nothing until Enter or a click outside the panel.
7. The calendar shows days before the first sample as unselectable; a day with no rows is struck through.
8. One wheel notch after a preset changes the width to the neighbouring ladder rung.
9. Escape closes the panel and changes nothing; a click outside applies what was typed.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep the plan in sync with the work done

## Solution Overview

- **Two navigation calls.** `TrendNavigationModel.SetWindow(from, to)` and `SetWidth(width)` hold the rules;
  `ChartNavigationController` wraps each in `ApplyWindowChange`. Nothing downstream changes, because every
  consumer already follows `WindowChanged`. The minimap's pan-delta workaround stays; it is outside this change.
- **Exact widths.** A typed or preset width bypasses `QuantizeWidth`. The ladder still serves the wheel, and the
  first notch re-enters it. Snapping a preset would make "5 min" show 4:51, which the operator reads as a defect.
- **Commit on Enter and on close, not on focus loss.** The axis panel commits when focus leaves its field group.
  That rule fails here: a `CalendarDatePicker` moves focus into its own popup root when its calendar opens, which
  clears the group's `IsKeyboardFocusWithin` and would write a half-edited pair. So Enter commits, a preset
  writes at once, and the panel's unload commits what is pending unless Escape cancelled it. Each commit re-seeds
  every field from the navigation, so the panel always shows the window in force.
- **Opening.** The time axis band is a press target routed to a new `ChartPressAction.PanOrEditTimeWindow`. A drag
  pans exactly as today; a release inside the 4 px slop opens the panel. It comes after the Y-axis edit and Ctrl
  and ahead of the tool, so Ctrl keeps its meaning everywhere and delta mode does not change a time-axis click.
- **Day coverage, one read per open.** A new read-only member of `IDataProvider` returns the local days of a date
  range that hold a layer-0 row of a catalogue pen. The panel reads `[first sample day, newest sample day]` once
  when it opens. One statement, rooted at `semiplot_tags` with the lateral `LIMIT 1` probe the extent read uses,
  stops at the first row of each day. Partition metadata is not used: a partition exists ahead of its day and
  outlives a stopped server.
- **One range form.** The date-time range fields, their parse and the five range checks move out of
  `ExportRangeForm` into `WallClockRangeForm`. The export and the time panel each derive and add their own checks.
- **Time frame.** The panel shows and parses local time in a `TimeZoneInfo` handed to `TrendChartViewModel`,
  production's `archiveZone`, which is the machine zone the axis draws in. The day read works in archive
  wall-clock days, the same local days the partitions cut, so it skips `ArchiveTimeConverter` like the export
  read.

## Technical Details

Core:
- `SetWindow(DateTime from, DateTime to)`: throws `ArgumentException` when `to <= from`, as the constructor does
  (`TrendNavigationModel.cs:16-19`). When `from` is before `FirstSample`, the window moves right to start there
  and keeps its width, the rule `Pan` uses (`:40-43`). `IsSticky = false`. The form refuses a span over 365 days,
  so the width clamp never changes a typed pair silently.
- `SetWidth(TimeSpan width)`: clamps the width to 1 s .. 365 days, keeps `To`, sets `From = To - width`; when that
  falls before `FirstSample`, `From = FirstSample` and `To = From + width`, the rule `Zoom` uses (`:67-73`).
  `IsSticky` unchanged.

Forms:
- `WallClockRangeForm` (abstract, `ReactiveObject`) owns the four fields `StartDate`, `StartTimeText`, `EndDate`,
  `EndTimeText`, the `HH:mm:ss` parse and format, `protected WallClockRangeProblem RangeProblem` (`None`,
  `StartDateMissing`, `StartUnreadable`, `EndDateMissing`, `EndUnreadable`, `EndNotAfterStart`),
  `protected (DateTime From, DateTime To)? RangeBounds`, and `protected virtual void RaiseDerived()`, which each
  field setter calls. Extracted from `ExportRangeForm.cs:10`, `:25-63`, `:130-146`, `:165-175`.
- Each derived form owns its problem enum, `Problem` (the six range arms mapped from `RangeProblem`, then its own
  arms), `IsValid`, `From`, `To`, the four `Is...Invalid` flags, and its `RaiseDerived` override. The flags stay
  per form because the export's end flags include its own `SpanOverLimit` (`ExportRangeForm.cs:127-128`, pinned at
  `ExportRangeFormTests.cs:85-94`).
- `ExportRangeForm : WallClockRangeForm` keeps its constructor (floor the start, ceil the end), `TimeFieldFormat`
  and `MaxSpan` names, `SpanOverLimit`, `NoPenTicked` and its public surface; `CsvExportWindow.axaml`,
  `CsvExportViewModel.cs:58-75` and `ExportRangeProblemText` stay unchanged.
- `TimeWindowForm : WallClockRangeForm`: seeded from the UTC edges and a `TimeZoneInfo`; adds `WidthText`,
  `IsWidthInvalid`, and, after the five range problems and in this order, `TimeSkipped` (an edge that
  `zone.IsInvalidTime` reports, the spring-forward hour), `SpanOverLimit` (over 365 days between the UTC
  edges), `WidthUnreadable` and `WidthOutOfRange` (under `0:00:01` or over `8760:00:00`). `TimeSkipped` comes
  first because a skipped time has no UTC value to measure the span from. Edges convert with
  `TimeZoneInfo.ConvertTimeToUtc` only after `TimeSkipped` has passed; an ambiguous autumn hour takes the
  standard offset, the cosmetic DST rule of `data-integration.md#time-boundary`. The width limits are read
  from `TrendNavigationModel.MinimumWidth` and `MaximumWidth`, which became public for it, so the 1 s and
  365-day limits have one home. The form compares each edge with its own seed (`StartDiffersFromSeed`,
  `EndDiffersFromSeed`) besides the pair (`EdgesDifferFromSeed`). `LocalDay(DateTime utc)` gives the panel the
  local day of a UTC instant in the form's zone, so `WallClockRangeForm.ToWallClock` stays `protected`.
- `WindowWidthText` (static): `Format(TimeSpan)` writes total hours unbounded, `h:mm:ss` (`0:05:00`,
  `168:00:00`); `TryParse(string, out TimeSpan)` reads `^\s*(\d+):([0-5]\d):([0-5]\d)\s*$`.

Panel:
- `TrendChartViewModel` gains a `TimeZoneInfo timeZone` constructor parameter (`TrendWindow.cs:77` passes
  `archiveZone`; the test builders and the two journeys pass a fixed zone) and
  `ReportFailure(IReadOnlyList<IError> errors)`, delegating to `_messagePanel.TryReportFailure(errors, _logger)`;
  the panel passes a failed day read's `result.Errors`.
- `TimeWindowPanelViewModel(TrendChartViewModel chart, TrendCoordinator coordinator, IScheduler uiScheduler,
  TimeZoneInfo timeZone)`, built in `TrendChartViewModel` beside `AxisScale` from its constructor parameters.
  - `Seed()`: in this order, sets `BlackoutDays` empty, fills the form from `Navigation.From/To` (both floored to
    the second), sets `FirstDay` (the earlier of the first sample's local day and the selected start day, since
    `TrackDataExtents` can open a window that starts before `FirstSample`), `LastDay` (the later of the newest
    sample's day and the selected end day), then recomputes `BlackoutDays` from the last day read. The host
    calls it before `ShowAt`, as for the axis panel.
  - `Opened()`: starts the day read. Called from the code-behind's `OnLoaded`, so a read starts only for a panel
    that `Close()` will also reach.
  - "Differs from the seed" compares the form's text and dates with the values `Seed()` wrote, never the parsed
    pair with the navigation's ticks, so an untouched Enter writes nothing and keeps sticky.
  - `CommitCommand` (`Commit()`), one for Enter in every field and for the close: when the pair is valid and
    differs from the seed, `Navigation.SetWindow`, with an edge left as seeded taken from the navigation at
    the commit (the live edge moves it under sticky), falling back to the typed pair when that resolved pair is
    out of order or over 365 days; then, when the width is valid and differs, `Navigation.SetWidth`; then one
    re-seed after a write. Review merged the planned `CommitEdgesCommand` and `CommitWidthCommand`, because
    each re-seed dropped the other's typed but unapplied value, which the close then never wrote.
  - `SetWidthCommand` (parameter `TimeSpan`) for the presets: `Navigation.SetWidth`, then re-seed.
  - `CancelCommand`: marks the open cancelled and raises `CloseRequests`.
  - `Close()`: unless cancelled, runs `Commit()`; disposes the day read; clears the
    read result. Called from the code-behind's `OnUnloaded`, which every close reaches: Escape, a light dismiss,
    and the host's `Hide`.
  - Day read: `Observable.FromAsync(ct => coordinator.QueryArchiveDaysAsync(FirstDay, NewestDay, ct))`,
    `ObserveOn(uiScheduler)`. A failed result goes to `chart.ReportFailure(result)`; the calendar then shows no
    blackout days.
  - `BlackoutDays`: each day in `[FirstDay, NewestDay]` without a row, minus the two selected dates. `NewestDay` is
    the local day of `Navigation.NewestSample`, never a clock read.
- `Presets`: 1, 2, 3, 5, 10, 30 min and 1 h as `TimeSpan` values with their labels from `Resources`.
- `TimeWindowPanel.axaml`: fixed width, `x:DataType`, rows `Start`, `End`, `Width`, one reserved message line.
  An Enter `KeyBinding` to `CommitCommand` sits on each of the two time `TextBox`es and on the width `TextBox`,
  never on a picker or the panel root: a `KeyBinding` marks the key handled before the
  picker's own Return parse runs (`KeyBinding` walks the visual parents ahead of `KeyDown`), so Enter on a picker
  would commit the date before the typed one. Escape binds `CancelCommand` on the panel root. The presets are a
  `DropDownButton` whose `MenuFlyout` items invoke `SetWidthCommand`; no selection state, so the width field is
  the width's only display. `DisplayDateStart` binds `FirstDay`, `DisplayDateEnd` binds `LastDay`; the calendar
  clamps rather than throws when a selected date lies outside them.
- `TimeWindowPanel.axaml.cs` holds the interop the view model cannot:
  - `OnLoaded` calls `Opened()`; `OnUnloaded` calls `Close()`, each inside a `try`/`catch` to `ReportFailure`.
    Children detach before the panel, so a focused picker has parsed its text and every `TextBox` has written its
    text (default `PropertyChanged` trigger) when `Close()` runs.
  - A `KeyUp` handler for Return on each picker calls `Commit()` after the picker's own parse.
  - The sync of `BlackoutDays` into each picker's `BlackoutDates`, a collection that is not bindable and is `null`
    until the picker applies its template. The sync subscribes on `DataContextChanged`, replays the current value
    on attach, and lives as long as the view model, not one open: `Seed()` runs before `ShowAt` while the panel is
    unloaded, and its empty set has to reach the pickers then. Each picker's `TemplateApplied` is subscribed in the
    constructor, because the first `ShowAt` applies the templates before `Loaded`.
- Blackout order: `CalendarBlackoutDatesCollection` throws when a range covers a selected date of its calendar, and
  the calendar throws when a selected date is set to a blackout date. `Seed()` empties `BlackoutDays` before it
  writes the dates and refills it after; the sync and the `SelectedDate` binding chain (form, picker, the
  template's calendar) all run synchronously on the property change, so no write meets a conflicting set on any
  open.

Chart input:
- `ChartAxisRegion.TryCreateTimeAxis(Plot plot)`: the band under the data rect from `PanelOffsets` and
  `PanelSizes` of `plot.Axes.Bottom`, bounded horizontally by the data rect.
- `ChartPressRouter.Route(ChartPressRegion region, modifiers, activeTool)`: Y-axis edit, then Ctrl, then
  `PanOrEditTimeWindow`, then the tool. `ChartPressRegion` is `DataArea | ActivePenAxis | TimeAxis`, computed
  once by the view's `PressRegionAt`, so the two bands cannot both be hit.
- `TrendChartView`: the time panel's `Flyout Placement="Pointer"` lives in `UserControl.Resources` under the key
  `TimeWindowFlyout`, held in a private field (tests read it through `ShownChart.TimeWindowFlyout`, the
  resource lookup), and is shown at `PlotControl` from `OnPointerReleased` for
  `!IsStarted && PanOrEditTimeWindow`. `CloseRequests` hides it, as for the axis panel. `ContinueDrag` needs no
  change: every action but `ShiftActivePen` pans.

Data:
- `IDataProvider.QueryArchiveDaysAsync(DateOnly firstDay, DateOnly lastDay, CancellationToken ct = default)`
  returns `Task<Result<IReadOnlyList<DateOnly>>>`, the days in `[firstDay, lastDay]` that hold a row, ascending.
- `ArchiveStatements.ArchiveDays`, the lateral probe shape of `ArchiveExtent`:

  ```sql
  SELECT day.start::date
  FROM generate_series(@firstDay, @lastDay, interval '1 day') AS day(start)
  WHERE EXISTS (
      SELECT 1
      FROM semiplot_tags tag
      CROSS JOIN LATERAL (
          SELECT 1
          FROM trends
          WHERE id = tag.id AND l = 0 AND t >= day.start AND t < day.start + interval '1 day'
          ORDER BY t
          LIMIT 1) hit)
  ORDER BY 1
  ```

  The probe's `ORDER BY t` was added in task 4: with a bare `LIMIT 1` the planner reads the pen's day as a
  `Bitmap Heap Scan` (33 998 index entries for one row on the one-day seed) instead of one index edge.
  The binder is `PostgresDataProvider.BindArchiveDays`, beside the read, as every other binder sits beside
  its reader.

  `@firstDay` and `@lastDay` are `DateTime` values of kind `Unspecified` at midnight, bound as
  `NpgsqlDbType.Timestamp` with no conversion; failures map with `TagCatalogAndTrendsRelations`.
- `TrendCoordinator.QueryArchiveDaysAsync` passes through, like `QueryArchiveExtentAsync`.

Resources, English and Russian: `TimeWindowStartLabel`, `TimeWindowEndLabel`, `TimeWindowWidthLabel`,
`TimeWindowPresets`, one label per preset, and one message per time-window problem. The date format reuses
`ExportDateFormat`.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests, the measurement and the architecture documents in this
  repository.
- **Post-Completion** (no checkboxes): the manual checklist on the stand, and the issue texts.

## Implementation Steps

### Task 1: Exact window and width in the navigation model

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/TrendNavigationModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartNavigationControllerTests.cs`

- [x] add `SetWindow(from, to)` and `SetWidth(width)` to `TrendNavigationModel` with the rules in Technical Details
- [x] add `SetWindow` and `SetWidth` to `ChartNavigationController`, each ending in `ApplyWindowChange`
- [x] write model tests: exact edges, sticky dropped by `SetWindow` and kept by `SetWidth`, width clamps at 1 s and
  365 days, `From` before `FirstSample` keeps the width, `to <= from` throws
- [x] write `SetWidth_ThenOneWheelNotch_LandsOnTheLadder`; keep `Zoom_InThenOut_ReturnsToOriginWindowWithinTolerance`
  green
- [x] write controller tests: each call raises `WindowChanged` with a re-query and the layer for the new width
- [x] run tests - must pass before task 2

### Task 2: One date-time range form and the width text

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Navigation/WallClockRangeForm.cs`
- Create: `SemiPlot/SemiPlot.UI/Navigation/WindowWidthText.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/ExportRangeForm.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/WallClockRangeFormTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/WindowWidthTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/ExportRangeFormTests.cs`

- [x] extract `WallClockRangeForm` from `ExportRangeForm` with the virtual `RaiseDerived` hook; the export form
  derives and keeps its public surface, flags and notifications
- [x] add `WindowWidthText.Format` and `TryParse`
- [x] write range tests on a minimal derived form: each range problem in order, surrounding blanks, bounds
- [x] write width tests: `0:05:00`, `168:00:00`, surrounding blanks, `0:60:00` and `abc` refused, round trip of
  `Format` through `TryParse`
- [x] keep every `ExportRangeFormTests` and `CsvExportViewModel` test green unchanged
- [x] run tests - must pass before task 3

### Task 3: The time window form

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Navigation/TimeWindowForm.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/TimeWindowFormTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`

- [x] create `TimeWindowForm` with the seed, the width field, its problems and the UTC edges
- [x] add the problem messages and the labels to both `.resx` files
- [x] write tests in a fixed zone (`Europe/Berlin`): the seed shows the local edges, a valid pair returns UTC
  edges, each problem in order, width bounds at `0:00:01` and `8760:00:00`, a span over 365 days refused
- [x] write `ASkippedSpringForwardTime_IsAFormProblem`: `2026-03-29 02:30:00` is `TimeSkipped`, not an exception
- [x] run tests - must pass before task 4

### Task 4: The archive days read

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/PostgresArchiveDaysReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`

- [x] first, on the analysed clone: `EXPLAIN` the statement with both days bound and confirm `trends` is reached by
  an index scan under the per-pen probe; adjust the statement and this plan if it is not
- [x] add `QueryArchiveDaysAsync` to `IDataProvider`, the statement and its binder to `ArchiveStatements`, and
  the read to `PostgresDataProvider` with the cancellation pattern of the history read
- [x] add the pass-through to `TrendCoordinator` and a settable, gateable, failable fake to `FakeDataProvider`
- [x] pin the statement text and its binder's parameter names in `ArchiveStatementTextTests`
- [x] write `PostgresArchiveDaysReadTests`: on `SeededArchive`, the seeded days come back and a range before the
  seed returns none; on per-test `CloneAsync` clones (the `PostgresExtentReadTests.cs:45-74` pattern), an empty
  catalogue returns none and a dropped `trends` maps to `TableMissing` naming `semiplot_tags, trends`
- [x] add `ExplainPlanTests.ArchiveDays_ReadsThroughTheIndex`
- [x] seed a 31-day archive on the bench (`ArchiveSeeder --days 31`), run `EXPLAIN (ANALYZE)` and time the read
  over all 31 days; record both in `data-integration.md#read-cost`
- [x] run tests - must pass before task 5

### Task 5: The panel view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanelViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TimeWindowPanelViewModelTests.cs`

- [x] add the `timeZone` parameter and `ReportFailure(IResultBase)` to `TrendChartViewModel`; update its five
  construction sites
- [x] create `TimeWindowPanelViewModel` with `Seed`, `Opened`, the three commit commands, `CancelCommand`,
  `CloseRequests`, `Close`, `FirstDay`, `LastDay` and `BlackoutDays`; build and dispose it in `TrendChartViewModel`
- [x] write tests: a commit writes only when the value differs from the seed; an untouched Enter over a window
  with a sub-second `From` writes nothing and keeps sticky; an invalid form writes nothing;
  the edges drop sticky and the width keeps it; each preset sets its exact width; `Close` commits pending
  edges then width, and after `CancelCommand` commits nothing
- [x] write day tests over a `TestScheduler` and `FakeDataProvider`: `BlackoutDays` excludes days with rows,
  days before `FirstDay`, days after `NewestDay` and the selected dates; one read per open; a failed read reaches
  the message panel and leaves `BlackoutDays` empty; `Close` cancels a pending read and the next open reads again;
  `Seed` raises `BlackoutDays` empty before it writes the dates
- [x] run tests - must pass before task 6

### Task 6: The panel view

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanel.axaml`
- Create: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanel.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TimeWindowPanelViewTests.cs`

- [x] create the panel AXAML: fixed width, `invalid` classes, one reserved message line, the Enter and Escape
  bindings, the presets `DropDownButton`, every colour from `Palette.axaml` keys
- [x] create the code-behind: `OnLoaded` with `Opened()`, `OnUnloaded` with `Close()`, the pickers' Return
  handler, the `BlackoutDates` sync bound to the view model's lifetime, `TemplateApplied` in the constructor
- [x] write view tests: typing a start time and Enter moves only the left edge; typing a date into a picker and
  Enter moves the left edge to that date; opening the start calendar (`IsDropDownOpen = true`) writes nothing;
  picking a date keeps the panel open; the five-minute preset sets the width and keeps sticky; Escape writes
  nothing; a click outside writes the pending pair once; a day without rows is a blackout date after the first
  open; a window starting on an empty day seeds without throwing; the message line keeps the panel's size
- [x] write `AReopenOntoABlackoutDay_SeedsWithoutThrowing`: open, let the day read land, close, pan `From` onto a
  day without rows, click the time axis again
- [x] every view test runs `Dispatcher.UIThread.RunJobs()` after `ShowAt`, because a hide before the posted
  `Loaded` skips both `Loaded` and `Unloaded`
- [x] run tests - must pass before task 7

### Task 7: Open the panel from the time axis

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartAxisRegion.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartAxisRegionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPressRouterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartViewTestBuilder.cs`

- [x] add `ChartAxisRegion.TryCreateTimeAxis` and `ChartPressAction.PanOrEditTimeWindow` with the router order
  in Technical Details
- [x] host the time panel flyout in `TrendChartView` with the `TimeWindowFlyout` seam, open it on a release inside
  the slop, hide it on `CloseRequests`
- [x] add `TimeAxisPoint(vm)` to `ChartViewTestBuilder` beside `AxisRegionPoint`
- [x] write tests: region band from a rendered plot; router order with the time axis hit under Ctrl and in delta
  mode; `AClickOnTheTimeAxis_OpensTheTimeWindowPanel`; `ADragOnTheTimeAxis_PansAndOpensNothing`; a Ctrl drag
  from the time axis shifts the active pen; a click in the data area still picks a pen
- [x] ➕ write a headless test that opens the panel from the time axis, opens a picker's calendar
  (`IsDropDownOpen = true`) and closes the chart window; if Avalonia 12.0.5 throws
  `ArgumentOutOfRangeException` in `StyledElement.OnDetachedFromLogicalTreeCore`, fix it in production code and keep
  the commit-on-close semantics. It threw; `TrendChartView` now hides the time window flyout on its window's
  `Closing`, so the panel applies what was typed before the window detaches
- [x] run tests - must pass before task 8

### Task 8: Verify acceptance criteria

- [x] verify every row of the table in Overview has a test or a manual step. The End row had neither; added
  `TimeWindowPanelViewModelTests.Commit_WithANewEnd_MovesTheRightEdgeOnlyAndDropsSticky`
- [x] run both Acceptance Evidence commands; add any new test class to their filters (every new class was already
  in them): unit 302 passed, integration 13 passed
- [x] run the full suite: `dotnet test SemiPlot.slnx` (unit 2084 passed, integration 172 passed)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files (both exit 0,
  terse over the 36 `.cs` files the branch touches)
- [x] `git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit` prints only
  `HeadlessInput.cs` lines

### Task 9: [Final] Update documentation

- [x] `docs/architecture/trend-interaction.md`: a section "The time window panel" beside "The axis scale panel"
  (`:277-352`), including why it commits on Enter and close rather than on focus loss; the router paragraph
  (`:188-201`) and the left-button bullets (`:221-246`, `:269-273`) name the time-axis branch; the ladder text
  (`:123-127`) states that a typed or preset width is exact and the round trip holds from a ladder width
- [x] `docs/architecture/trend-feature-spec.md`: TM-2 acceptance (`:23`) from a ladder width; TM-6 acceptance
  (`:39`) a width keeps the right edge
- [x] `docs/architecture/data-integration.md`: the interface copy (`:37-58`), a row in "Operation to statement"
  (`:245-254`), and "Time boundary" (`:766`) naming two reads that skip `ArchiveTimeConverter`
- [x] `docs/architecture/ui-theme.md`: the panel under "A form never resizes on validation" (`:174-317`). No
  Skia/HarfBuzz measurement exists for this panel; the section names the headless gate and states the
  real-font measurement as outstanding
- [x] `AGENTS.md`: the left-button bullet (`:322-329`) names the time-axis branch after Ctrl; the export bullet
  (`:380`) stops calling the export the one read that skips `ArchiveTimeConverter`; the code-behind list of
  `ReportFailure` routes names `TimeWindowPanelViewModel`
- [x] `readme.md`: one line under "Тренды" for the panel
- [ ] move this plan to `docs/plans/completed/`: deferred to delivery, which archives the plan; the plan stays
  in `docs/plans/` on this branch

## Post-Completion

**Manual verification:**
- the nine steps of the manual checklist in Acceptance Evidence on Windows with the stand

**Rebase onto master (c94a37d, #128):**

The branch's base is 697bbf6. Master's #128 makes a missed pick, "a click outside the data area included",
drop the active pen's emphasis, and pins it with `ChartPointerInputTests.AClickBelowTheDataArea_DropsTheEmphasis`
(master `:295`), which clicks at `dataRect.Bottom + 8`, inside this branch's time axis band. On the merged
tree that test fails. The resolution taken: a time-axis click opens the panel and keeps the emphasis, as the
axis-region press does; #128's rule narrows to clicks outside the data area and outside both axis bands. The
other resolution, a time-axis click that also drops the emphasis, was not taken: it would thin the active pen
as a side effect of opening a panel, and #128's own list already exempts the axis-region press.

The delivery rebase makes these edits:
- `SemiPlot.Tests.Unit/UI/Chart/ChartPointerInputTests.cs`: master's `AClickBelowTheDataArea_DropsTheEmphasis`
  becomes `AClickBelowTheDataAreaOutsideTheTimeAxis_DropsTheEmphasis`, clicking at
  `(dataRect.Left - PickToleranceClearancePixels, dataRect.Bottom + PickToleranceClearancePixels)`, below the
  left axis and outside the time axis band (`TryCreateTimeAxis` bounds it horizontally by the data rect),
  asserting that point is outside the band and that `shown.TimeWindowFlyout` stays closed. A new
  `AClickOnTheTimeAxis_KeepsTheEmphasis` emphasizes pen 1 (`EmphasizeTheFirstPen`), clicks `TimeAxisPoint`,
  and asserts the flyout open, `ActivePenId` 1 and `InkRatio(Red, Green) > StrokeProbe.ThickInkRatio`.
- `docs/architecture/trend-interaction.md`: master `:232` "a click outside the data area included" reads
  "a click outside the data area and outside both axis bands included"; master `:238` "the axis-region
  press never reach `PickPenAt`" adds "and the time-axis press". This branch's time-axis bullet already says
  the press never reaches `PickPenAt`; it gains "and leaves the emphasis as it is".
- `docs/architecture/bench.md:345`: a textual conflict, both sides edit the `ChartPointerInputTests` row. The
  merged row keeps master's text with "or below the data area" narrowed to "or below the data area outside
  the time axis band", adds "a click on the time axis keeps the emphasis", and keeps this branch's time-axis
  facts.
- `docs/architecture/trend-feature-spec.md:115` (PN-6 "As built", master): "A click farther than 6 px from
  every line" adds "outside the axis bands".

Checked 2026-10-15 on `git merge-tree --write-tree master <branch with these fixes>` extracted outside the
worktree, with the two test edits above applied: the full unit project passed, 2117 tests, both edited
tests among them. The merge itself conflicts only in `docs/architecture/bench.md`.

**Issues:**
- #71 and #79 close with the PR; the PR body states that a width keeps the right edge, that presets are exact
  and off the zoom ladder, and that a blackout day means no layer-0 row of any catalogue pen

**Executed by exec:**
- branch: time-window-panel

## Verify it yourself

None of this exists at the base 697bbf6: there is no `SetWindow`, `SetWidth` or `QueryArchiveDaysAsync`, and a
click on the time axis selects nothing. The tests below are absent there and pass at c51fa02.

1. Unit acceptance filter, reports 316 passed:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj `
     --filter "FullyQualifiedName~TrendNavigationModelTests|FullyQualifiedName~ChartNavigationControllerTests|FullyQualifiedName~WindowWidthTextTests|FullyQualifiedName~WallClockRangeFormTests|FullyQualifiedName~TimeWindowFormTests|FullyQualifiedName~TimeWindowPanelViewModelTests|FullyQualifiedName~TimeWindowPanelViewTests|FullyQualifiedName~ChartPressRouterTests|FullyQualifiedName~ChartAxisRegionTests|FullyQualifiedName~ChartPointerInputTests|FullyQualifiedName~ExportRangeFormTests|FullyQualifiedName~ArchiveStatementTextTests"
   ```

2. With Docker running, the integration filter reports 14 passed; `ExplainPlanTests.ArchiveDays_ReadsThroughTheIndex`
   fails if `ORDER BY t` is removed from `ArchiveStatements.ArchiveDays`:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj `
     --filter "FullyQualifiedName~PostgresArchiveDaysReadTests|FullyQualifiedName~ExplainPlanTests"
   ```

3. Two behaviours a test pins and the stand cannot show by eye:
   - a start-only commit under sticky keeps the live right edge:
     `--filter "FullyQualifiedName~Commit_WithANewStartAfterTheLiveEdgeMoved_KeepsTheLiveRightEdge"`;
   - closing the chart window with a picker's calendar open throws nothing (Avalonia 12.0.5 does without the
     `Closing` hide): `--filter "FullyQualifiedName~ClosingTheWindowWithAPickersCalendarOpen"`.

4. On the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), walk the nine manual steps in Acceptance
   Evidence. Before this branch a click on the time axis does nothing; after it:
   - a click on the axis labels opens the panel at the pointer, a drag there still pans;
   - `0:05:00` + Enter in Width gives exactly 5:00 with the right edge in place and follow-now still on;
   - a typed start + Enter moves the left edge only and switches follow-now off;
   - days with no archive rows are struck through in both calendars;
   - one wheel notch after a preset moves the width to a ladder rung (5:00 -> 4:24.7 inward).
   Also try Enter inside the open calendar on a day (it picks the day and commits at once) and whether the
   calendar's last navigable day follows the newest sample after the first open; reviewers flagged both as
   unverified on real input.

5. Before delivery, rebase onto master and apply the #128 reconciliation in Post-Completion; the merged unit
   project then reports 2117 passed in the reviewers' scratch check.
