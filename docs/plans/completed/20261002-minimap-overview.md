# Minimap overview

## Overview

The minimap under the chart is a navigation strip with no data on it, and its window marker leaves the
strip once the chart follows live data past the extent read at start. This plan turns the strip into an
archive overview of the pen the chart draws, keeps the marker inside the strip at every zoom, and lets the
strip's right bound follow the newest sample.

- **The marker stays in the strip.** At any zoom and any window position the marker lies inside the strip:
  it keeps its minimum width and its left edge stops at `strip width - marker width`.
- **The right bound follows the newest sample.** The strip ends at the later of the archive extent's last
  sample and the newest sample the chart has seen, and its end label moves with it, sticky or not.
- **The strip draws the drawn pen.** The pen whose axis the chart draws (`DrawnPenId`) is drawn as a
  min/max band over the whole extent, scaled to its own range, in its own colour, from the coarsest layer
  the strip's column count allows. A break in the archive is a gap in the band. No drawn pen, no band.
- **The strip carries no meaningless line, and its labels stay readable.** The baseline drawn through
  the middle of the strip goes: it is no zero and no value. The two end labels move out of the strip into a
  label row under it, because the chart's own time labels sit directly above the strip.
- **Hovering reads the time.** Under the pointer the strip shows a thin vertical line, and the label row
  shows that point's date and time to the minute; leaving the strip hides both.
- **The band is re-read** at start, after a catalogue change that reloads the extent, when the drawn pen
  changes, and on a schedule that follows the strip's span, so the band never drifts visibly from the
  strip's right bound.

The strip keeps its 36 px height; the label row under it adds one line of 10 px text. Closes #91: the marker's left edge is not clamped, and the strip's right
bound is the startup extent.

## Context (from discovery)

- `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml` - a `Canvas` with a baseline, two end labels and the
  `WindowHighlight` border; nothing draws data. `MainWindow.axaml:78-80` gives it `Height="36"`.
- `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs:51-75` - `UpdateStrip` sizes the baseline and the
  marker by hand; `:72-73` set `Left = start * width` and `Width = Math.Max(MinimumHighlightWidth,
  widthFraction * width)` with `MinimumHighlightWidth = 6.0` (`:14`); the left edge has no upper clamp.
- `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs:6-22` - `WindowFraction` clamps both window ends to
  `[0, 1]`, so a window past `extentLast` yields start `1.0`.
- `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs:82-90` - `LoadExtentAsync` reads the extent and
  applies it (`ApplyExtent`, `:106-136`); `ExtentLast` changes nowhere else (`:128`).
- Callers of `LoadExtentAsync`: `MainWindow/TrendWindow.cs:141` (start) and
  `MainWindow/PenCatalogueApplier.cs:58`, `:78` (catalogue deltas that add pens).
- `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:16`, `:22-24` - `_liveEdge` starts at the wall
  clock, not at a sample; `:95` `TrackDataExtents` sets it to the archive's last sample; `:139-157`
  `OnLiveEdge` keeps the later timestamp and raises `WindowChanged` only while sticky (`:146-149`).
  `ChartRealtimeApplier.cs:15` calls `OnLiveEdge` with each realtime batch's last timestamp.
- `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:172-199` - `LayerForWidth(width, currentLayer,
  targetColumnCount)`: the finest layer whose next-coarser spacing times the column count covers the
  width, with hysteresis on the current layer.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:91-108` - `ActivePenId` and `DrawnPenId`;
  `:250`, `:281` raise `DrawnPenId` without checking that it changed.
- `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:92-100` - `QueryHistoryAsync(penIds, fromUtc, toUtc,
  layer, targetColumnCount, cancellationToken)`.
- `SemiPlot/SemiPlot.Core/Trends/AggregationLayer.cs` - `Raw`, `Minute`, `Hour`, `Day` with point
  spacings 1 s, 15 s, 15 min, 6 h.
- A break in an envelope is a NaN column in `Min`, `Max` and `Center` (`MinMaxDecimator.cs:225`,
  `PostgresDataProvider.cs:457-460`); `PenScaleModel.cs:113-124` (`Widen`) holds the NaN-skipping range
  rule. The stand seeds breaks every day (`bench.md:25`).
- `SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs:41-62` - a coarse read reaching "now" adds raw rows
  from the pen's newest coarse timestamp, at most four point spacings back from the window end
  (`data-integration.md:382-390`).
- `SemiPlot/SemiPlot.UI/Messages/MessagePanelViewModel.cs:54-64`, `:86-89` - the panel coalesces a
  repeat of its newest entry only. `Bridge/PenCatalogueSync.cs:46`, `:95` - the window's one periodic
  reader runs on the UI scheduler.
- Tests: `SemiPlot.Tests.Unit/Core/Trends/MinimapGeometryTests.cs`, `UI/Minimap/MinimapViewModelTests.cs`
  (builders at `:151`, `:158` pass `ImmediateScheduler.Instance`), `UI/Minimap/MinimapPointerInputTests.cs`
  (`:124`, `:146` likewise), `UI/Chart/ChartNavigationControllerTests.cs`, `UI/Bridge/FakeDataProvider.cs`
  (one `HistoryQueryCount` and one `LastQueriedPenIds` for every reader), and the window builders
  `UI/MainWindow/MainWindowTestBuilder.cs`, `UI/Di/TrendWindowBuildTests.cs`,
  `UI/Di/CompositionRootTests.cs`, `UI/MainWindow/TrendWindowTests.cs`,
  `UI/Startup/EmptyCatalogueStartupTests.cs`, `UI/MainWindow/PenCatalogueApplierTests.cs`.
- Docs: `docs/architecture/trend-interaction.md:306-331` (Archive-overview minimap, with the stale
  `App.StartExtentLoad` at `:320`), `charting.md:291` and `:369`, `trend-feature-spec.md:29-31` (TM-4).

## Development Approach

- **testing approach**: TDD - each defect's test is written first and fails on `master` by assertion, not
  by a missing member.
- complete each task fully before moving to the next; small, focused changes.
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Every new or touched test class runs alone with `--filter`. A test that builds `MinimapViewModel` passes a
  `TestScheduler` as its UI scheduler and never calls `TestScheduler.Start()` once the band's next read is
  scheduled; `ImmediateScheduler` sleeps through a delayed schedule on the calling thread (CLAUDE.md,
  Test).

## Testing Strategy

- **unit tests**: the marker span, the band's figures and the right bound as plain `[Fact]`s in
  `Core/Trends`; the view model's reads, triggers, cancellation and failure reporting as `[AvaloniaFact]`s
  over a `TestScheduler` and `FakeDataProvider`; the marker placement and the band's size as headless view
  tests.
- **container tests**: none; the stand's span always picks `Minute` (Technical Details), which no fresh
  tail makes expensive.
- **e2e tests**: none; the demo stand smoke checklist covers what the eye sees.

## Acceptance Evidence

Today (verified 2026-10-02 on the demo stand): the strip shows no data, and after a few minutes of live
data with a 10-minute window the marker sits past the strip's right edge while the end label still reads
the start time.

Automated, from the repository root:

1. `dotnet build SemiPlot.slnx` - 0 warnings, 0 errors.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - no failures.
3. These named tests exist and pass (`--filter "FullyQualifiedName~<name>"`); the first two fail on
   `master` by assertion:
   - `MinimapPointerInputTests.AWindowPastTheExtent_KeepsItsMarkerInsideTheStrip` (headless);
   - `MinimapViewModelTests.ANewerSampleThanTheExtent_MovesTheRightBoundAndItsLabel`;
   - `MinimapGeometryTests.ABandWithABreak_DrawsTwoFigures`, `AFlatPen_DrawsAMidLineBand`,
     `AnAllNaNBand_DrawsNothing`;
   - `MinimapBandFeedTests.ASevenDayExtent_ReadsTheDrawnPenAtTheHourLayer`;
   - `MinimapBandFeedTests.ASupersededBandRead_IsCancelled`;
   - `MinimapBandFeedTests.AnOutage_ReportsOnceAndLogsEveryRepeat`.
4. `git grep -n "QueryHistoryAsync" -- SemiPlot/SemiPlot.UI/Minimap/MinimapBand.cs` prints nothing: the
   band control draws and never reads.
5. `MinimapViewTests.TheStrip_DrawsNoBaselineAndLabelsItsEndsUnderTheStrip` and
   `MinimapHoverTests.HoveringTheStrip_ShowsALineAndItsTimeAndLeavingHidesThem` exist and pass.

Manual smoke checklist on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. The strip shows a band in the drawn pen's colour across the whole archive within 2 s of start, with a
   gap at each seeded break.
2. Click another pen in the sidebar so its axis is drawn: the band switches to that pen within 2 s.
3. Recolour the drawn pen in the pen editor: the band takes the new colour within 5 s, with the chart.
4. Hide the drawn pen with nothing else shown: the band disappears; show a pen again: it returns.
5. Follow live data with a 15 s window for 5 minutes: the marker stays at the strip's right edge, fully
   visible, and the end label advances with the live edge.
6. Zoom in to a few seconds and pan to both ends of the archive: the marker never leaves the strip.
7. Click and drag on the strip: the chart window recentres as before.
8. Watch the strip for 3 minutes: at each re-read (every 60 s on the stand's day) the band grows at its
   right end and its existing shape does not visibly jump.
9. No line runs through the middle of the strip; the two end labels sit in a row under it, readable, and
   do not touch the chart's time labels above.
10. Move the pointer over the strip: a thin vertical line follows it and the row under the strip shows its
    date and time to the minute under the pointer; move the pointer off the strip: both disappear. Press
    and drag still recentre the chart window.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview

The strip has three jobs and each gets one owner.

- **Marker placement** is pure geometry in `Core/Trends/MinimapGeometry`: from the window fractions, the
  strip width and the minimum marker width it returns the marker's left edge and width, both inside
  `[0, strip width]`. The view only applies them.
- **The bounds.** The left bound is the extent's first sample. The right bound is the later of the
  extent's last sample and the navigation's newest sample. The navigation already owns that timestamp
  (`_liveEdge`); it exposes it as `NewestSample`, absent until a sample sets it, so the constructor's wall
  clock never becomes a bound. It raises `NewestSampleMoved` on every write, sticky or not, and the
  minimap recomputes its right bound, end label and marker on that event. The archive is never pruned, so
  the left bound moves only when a catalogue delta adds a pen with older rows, which already reloads the
  extent; no periodic extent read exists.
- **The band.** `MinimapViewModel` owns one read pipeline on the UI scheduler. Its requests come from the
  extent apply, a distinct `DrawnPenId` change and the next-read schedule; each request snapshots the pen
  id and the bounds on the UI thread, and `Switch` cancels the read in flight through its
  `CancellationToken`. Each read asks `TrendCoordinator.QueryHistoryAsync` for the drawn pen over
  `[left bound, right bound]` and publishes the envelope. When a read lands, success or failure, the next
  read is scheduled `clamp(span / 1000, 2 s, 60 s)` later, where `span` is the strip's span at that
  moment; a read slower than that delay is therefore never cut off by its own successor. Between reads the
  right bound grows and the band's right end trails it by one delay. Three cases follow on a 1500 px
  strip: an archive under 33 minutes reads every 2 s and trails by up to about 5 px; from 33 minutes to
  16.7 hours the delay is a thousandth of the span and the trail is 1.5 px; past 16.7 hours the band is
  read once a minute and the trail shrinks with the span, about 1 px on the stand's day and below a pixel
  from a week on. A `Hour` or `Day` read includes the provider's raw fresh tail, up to four point
  spacings of one pen; ASSUMPTION: once a minute that tail is cheap on an installation, which the
  months-long archive check in Post-Completion measures. ASSUMPTION: the provider's bucketed `Raw` read
  places bucket edges so that a read a few seconds later draws the same shape; if the band visibly
  shimmers on a young archive, the 2 s floor rises.
- **The overview layer** is `ChartNavigationController.LayerForWidth(span, previousLayer,
  MinimapColumns)` with `MinimapColumns = 250`: the existing ladder with the strip's own column count,
  so no second ladder exists. 250 columns read `Raw` up to about 62 min, `Minute` up to about 62 h,
  `Hour` up to about 62 days and `Day` beyond, and the hysteresis keeps a span at a boundary on the layer
  it already has. 250 columns are about 6 px each on a 1500 px strip, which a 36 px band still reads as a
  shape, and they bound a read at about 15 000 `Minute` or 6 000 `Hour` rows per pen at the top of each
  layer. 250 is below the chart's 256-column floor (`HistoryColumnTarget.cs:7`), so it never equals a
  chart read's column count, and a test fake tells the band's reads from the chart's by that count.
- **The band's shape** is pure geometry in Core: the envelope maps to figures, one per run of non-NaN
  columns, each a polygon of the max line forward and the min line back plus its centre line, over a
  value range that skips NaN (the `PenScaleModel.Widen` rule, shared rather than copied) and pads a flat
  range to a centred band. `Minimap/MinimapBand`, a `Control` with a `Render` override (avalonia.md,
  custom drawing), only draws those figures.
- **The band's colour** follows the drawn pen's stored colour live: a `WhenAnyValue` over `DrawnPenId` and
  that pen state's `Pen`, ending in `ToProperty`, so a recolour in the pen editor reaches the strip with
  the chart and needs no read.
- **Failures.** A failed band read reports through `ResultReporting.TryReportFailure`, which maps it
  through `ArchiveFailureMapper.Map` like every other read. During an outage the catalogue sync reports
  every 5 s with its own detail, so the panel's newest-entry coalescing never matches the band's repeat;
  the band therefore reports the first failure of an outage and logs every further one at Warning, and the
  first successful read arms the report again. The report starts armed. `Minimap/OutageReport` holds that
  policy once, and the extent re-read of a strip left blank at start uses it too, re-reading at most once a
  minute. Each read catches its own exception into a failed `Result`, so a throw never ends the pipeline.
  The catalogue sync's policy (suppress the first failures, then report each) answers a 5 s loop where one
  slow read is noise; the band's first failure is already worth a line.

## Technical Details

- `MinimapGeometry.MarkerSpan(double startFraction, double widthFraction, double stripWidth, double
  minimumWidth) -> (double Left, double Width)`: `Width = min(stripWidth, max(minimumWidth, widthFraction
  * stripWidth))`, `Left = clamp(startFraction * stripWidth, 0, stripWidth - Width)`. A zero or negative
  strip width returns `(0, 0)`.
- `MinimapGeometry.RightBound(DateTime extentLast, DateTime? newestSample)`: the later of the two.
- `MinimapGeometry.BandFigures(PenHistoryEnvelope band, DateTime first, DateTime last, double width, double
  height) -> IReadOnlyList<BandFigure>`, where `BandFigure` holds the polygon's points and the centre
  line's points in control coordinates. Columns outside `[first, last]` are dropped; a flat range
  `max == min` is padded to `±1` around the value before mapping (the band then sits at mid-height); an
  envelope with no finite value gives no figure. The NaN-skipping widen moves from a private
  `PenScaleModel.Widen` to one internal Core helper both call.
- `ChartNavigationController`: `public DateTime? NewestSample` (null until `OnLiveEdge` or
  `TrackDataExtents` writes it) and `public event EventHandler<DateTime>? NewestSampleMoved`, raised on
  every write. `_liveEdge` keeps its wall-clock start for `Pan` and `JumpToNow`.
- `MinimapViewModel(TrendCoordinator coordinator, TrendChartViewModel chart, IScheduler uiScheduler,
  MessagePanelViewModel messagePanel, ILogger<MinimapViewModel> logger)`: it reads `chart.Navigation`,
  `chart.DrawnPenId` and `chart.FindPen`. `ExtentLast` becomes the right bound. `Band`
  (`PenHistoryEnvelope?`) and `BandColor` are the published band; `Band` is null with no drawn pen or no
  extent. `MinimapColumns = 250`. The next-read delay is `MinimapGeometry.NextReadDelay(TimeSpan span)` =
  `clamp(span / 1000, 2 s, 60 s)`, a pure Core function. Each band read logs its layer, column count and duration at Debug.
- `FakeDataProvider` logs every history query (`penIds`, `fromUtc`, `toUtc`, `layer`,
  `targetColumnCount`); the chart-side counts and pen-id assertions read only queries whose column count
  is not 250.
- `MinimapBand : Control`: `Figures` and `BandColor` as styled properties registered with `AffectsRender`;
  the view model's figures are recomputed from `Band`, the bounds and the control's size. `Render` fills
  each polygon with `BandColor` at 35 % opacity and strokes its centre line at full colour.
  `MinimapView.LayoutBand` sizes it to the strip, and it sits below `WindowHighlight` and `HoverLine`,
  `IsHitTestVisible="False"`.
- The schedule keeps reading while the operator hides the minimap row; its cost is the same as when the
  strip is shown.

- The label row: `MinimapView` becomes a two-row grid, the 36 px strip on top and a label row of one
  text line under it (about 14 px); `MainWindow.axaml` sets no height on the view, so the minimap row
  takes the view's own height and grows by the label line. `ExtentFirstLabel` sits at the row's left edge and `ExtentLastLabel` at its right edge.
  The labels use the strip's existing foreground key from `Palette.axaml`; no new colour.
- Hover: the code-behind takes `PointerMoved` and `PointerExited` on `StripCanvas` (avalonia.md: input in
  code-behind, the decision in the view model) and hands the pointer's fraction to
  `MinimapViewModel.HoverAt(double fraction)` and `ClearHover()`. The view model publishes `HoverFraction`
  (`double?`) and `HoverLabel`, the time `MinimapGeometry.TimeAtFraction` gives, formatted as the end labels
  are. The view draws a 1 px line at that fraction over the band and centres the time under it in the label
  row, clamped inside the row; while it shows, an end label it would overlap is hidden. Nothing is shown
  without an extent. A drag keeps navigating as before, and the hover line follows the pointer during it.

## What Goes Where

- **Implementation Steps**: code, tests and docs in this repository.
- **Post-Completion**: the operator's walk on the stand and on a months-long archive.

## Implementation Steps

### Task 1: Keep the marker inside the strip

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MinimapGeometryTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapPointerInputTests.cs`

- [x] write `AWindowPastTheExtent_KeepsItsMarkerInsideTheStrip` on the existing headless setup: a window
      past the extent gives `Canvas.GetLeft(WindowHighlight) + WindowHighlight.Width <=
      StripCanvas.Bounds.Width`; it fails on `master` by assertion
- [x] add `MinimapGeometry.MarkerSpan` as Technical Details states; `UpdateStrip` places `WindowHighlight`
      from it
- [x] write geometry tests: start 1.0 and 0.999 keep `Left + Width <= stripWidth`; a window wider than
      the strip fills it exactly; a zero strip width gives `(0, 0)`; a window at the left edge keeps `Left
      == 0`
- [x] run the unit tests - must pass before task 2

### Task 2: Let the right bound follow the newest sample

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartNavigationControllerTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapPointerInputTests.cs`

- [x] write `ANewerSampleThanTheExtent_MovesTheRightBoundAndItsLabel`: an extent ending at T and a realtime
      sample at T + 5 min move `ExtentLast` and `ExtentLastLabel`, sticky and not sticky; it fails on
      `master` by assertion
- [x] add `NewestSample` and `NewestSampleMoved`; add `MinimapGeometry.RightBound`
- [x] `MinimapViewModel` takes the chart in place of the bare navigation (Technical Details) and the right
      bound on `ApplyExtent` and on `NewestSampleMoved`; `TrendWindow.Build` passes the chart; both
      minimap test builders move to a `TestScheduler` UI scheduler and a chart built over one
- [x] write tests: `NewestSample` is null on a fresh controller and set by `TrackDataExtents` and
      `OnLiveEdge`; the event fires on each write; an extent ending later than the newest sample wins; no
      extent applied leaves the strip blank; the existing extent and label tests keep their expectations
      (➕ as built: `NewestSample` only moves forward, so an older sample is no write and raises nothing; a
      sample newer than the extent seen before the extent lands is the right bound when it lands;
      `MinimapGeometryTests.RightBound_IsTheLaterOfTheExtentAndTheNewestSample` pins the rule)
- [x] run the unit tests - must pass before task 3

### Task 3: Shape the band in Core

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenScaleModel.cs`
- Create: the shared NaN-skipping range helper in `SemiPlot/SemiPlot.Core/Trends/`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MinimapGeometryTests.cs`

- [x] move `PenScaleModel.Widen`'s rule into one internal helper both call; `PenScaleModel`'s tests stay
      green unchanged (➕ as built: `Core/Trends/ValueRange.Widen`)
- [x] add `BandFigures`, `BandFigure` and `NextReadDelay` as Technical Details states (➕ as built:
      `BandFigure(Outline, CenterLine)` and its `BandPoint(X, Y)` live in `Core/Trends/BandFigure.cs`; a
      column is drawn when its min, max and centre are all finite, and the range comes from drawn columns
      only; a run of one column is drawn 2 px wide; a zero span, width or height gives no figure; the band
      is never logarithmic)
- [x] write `ABandWithABreak_DrawsTwoFigures`, `AFlatPen_DrawsAMidLineBand`, `AnAllNaNBand_DrawsNothing`,
      and a test that columns outside `[first, last]` are dropped and the y axis maps max to the top;
      `NextReadDelay` gives 2 s for 10 min, 3.6 s for 1 h, 60 s for 1 day and 60 s for 1 year
- [x] run the unit tests - must pass before task 4

### Task 4: Read the drawn pen's band

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/PenCatalogueApplierTests.cs`
- Modify: the window builders Context lists, where they construct the minimap or assert on history reads

- [x] write `ASevenDayExtent_ReadsTheDrawnPenAtTheHourLayer`: a seven-day extent issues one history read
      for `[DrawnPenId]` over `[ExtentFirst, ExtentLast]` at `Hour` and 250 columns, and `Band` holds its
      envelope
- [x] add the read pipeline, the next-read schedule, `BandColor` and the failure policy as Solution
      Overview states (➕ as built: the band, its reads, schedule, outage policy and colour live in
      `Minimap/MinimapBandFeed`, which `MinimapViewModel` builds and exposes as `BandFeed`; each read is an
      `Observable.Create` over a read that answers a throw with a failed `Result`; the `DrawnPenId` trigger
      skips the value it holds at construction; `BandColor` is the stored hex string, null with no drawn
      pen; a request for another pen clears `Band` at once, a requested read drops the scheduled one, and
      an answer landing after a newer request is discarded; a failed or empty extent is read once more on
      the next `NewestSampleMoved`)
- [x] `FakeDataProvider` logs every query; `PenCatalogueApplierTests` and the window builders assert on the
      chart's reads only (column count not 250), and their idle-chart checks stay as strict as on `master`
      (➕ as built: `HistoryQueries`, `ChartHistoryQueries` and `BandHistoryQueries`, with `HistoryQueryCount`
      and `LastQueried*` read from the log; `HistoryReadException` makes a read throw; of the window builders
      only `PenCatalogueApplierTests` asserts on history reads)
- [x] write `ASupersededBandRead_IsCancelled`: a `DrawnPenId` change while a read is held cancels its token
      and only the new pen's envelope lands
- [x] write `AnOutage_ReportsOnceAndLogsEveryRepeat`: the first failed read adds one panel entry, the next
      ones add none and log at Warning, a success then a failure adds one more (➕ as built: a catalogue-sync
      entry tops the panel before each repeat, so the panel's newest-entry coalescing cannot hide a repeat)
- [x] write tests: no drawn pen gives `Band == null` and no read; a `DrawnPenId` raise with the same id
      issues no read; the next read lands `NextReadDelay(span)` after the last one landed; a throwing read reports
      and the next scheduled read still runs; `BandColor` follows a recolour without a read; dispose
      cancels the scheduled read (➕ as built: also `AReloadedExtent_ReadsTheBandAgain` and
      `HidingTheDrawnPen_ClearsTheBandAndStopsTheReads`)
- [x] run the unit tests - must pass before task 5

### Task 5: Draw the band

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Minimap/MinimapBand.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewTests.cs`

- [x] add `MinimapBand` as Technical Details states; `UpdateStrip` sizes it to the strip (➕ as built:
      `BandColor` is an `IBrush?` bound through `LegendConverters.HexToBrush`, the legend's own converter;
      `MinimapView.LayoutBand` computes the figures from `Band`, `ExtentFirst`, `ExtentLast` and the strip's
      size and runs only on those and a resize, while `PlaceMarker` follows the window fractions; the band
      sits below the end labels as well as the marker)
- [x] write `MinimapViewTests`: the realised strip holds one `MinimapBand` above the baseline and below the
      marker, its `Bounds.Size` equals `StripCanvas.Bounds.Size`, a published band gives it figures, a null
      band leaves it none; pointer navigation still recentres the window (➕ as built: also the band's brush
      is the drawn pen's colour, and hiding every pen takes the figures away)
- [x] run the unit tests - must pass before task 6

### Task 6: Verify acceptance criteria

- [x] every automated item of Acceptance Evidence passes (➕ as built: build 0 warnings, 0 errors; unit
      suite 1632 passed, 0 failed, 0 skipped; each named test passes alone, the right-bound theory as 2
      cases; the `MinimapBand.cs` grep prints nothing)
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [x] `dotnet terse` over every touched `.cs` file exits 0 (➕ as built: the 18 files of
      `git diff --name-only master...HEAD -- '*.cs'`)
- [x] the manual smoke checklist: the operator's walk before delivery (skipped - not automatable)

### Task 7: [Final] Update documentation

- [x] `trend-interaction.md` "Archive-overview minimap": the band and its shape, the overview layer, the
      read triggers and the next-read schedule, the right bound at the newest sample, the marker clamp, the
      failure policy; replace the stale `App.StartExtentLoad` with `TrendWindow.StartExtentLoad`
      (➕ as built: `data-integration.md`'s "No failure stops at the log" names the band's outage policy
      with a pointer to the section, which `OutageReport` also points at)
- [x] `charting.md:291` and `:369`: `MinimapBand`, `MinimapGeometry.MarkerSpan`, `RightBound`,
      `BandFigures` (➕ as built: also `NextReadDelay`)
- [x] `trend-feature-spec.md` TM-4 acceptance: the strip draws the drawn pen and the marker stays inside it
      (➕ as built: `bench.md`'s headless guard table lists `MinimapViewTests` and the marker case of
      `MinimapPointerInputTests`; `testing-strategy.md` lists `MinimapViewTests` among the real-Avalonia
      tests)
- [x] the plan moves to `docs/plans/completed/` in the delivery commit (skipped - the delivery commit
      moves it; this pass leaves the file in place)

### Task 8: Clear the strip and label its ends underneath

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`

- [x] write `TheStrip_DrawsNoBaselineAndLabelsItsEndsUnderTheStrip` (headless): the realised strip holds no
      baseline, both end labels lie below the strip's bottom edge, inside the view, at its left and right
      edges; it fails before the change by assertion (➕ as built: the strip's children are exactly
      `BandLayer`, `WindowHighlight` and `HoverLine` in that order, and the two labels do not overlap;
      `MainWindowViewTests.TheMinimapRow_ShowsTheWholeLabelRowUnderTheStrip` pins the row's height)
- [x] delete `Baseline` and its layout; move the end labels into the label row under the strip as Technical
      Details states; size the view in `MainWindow.axaml` (➕ as built: the view is a `Grid` of rows `36,Auto`,
      the label row a `Panel` named `LabelRow` with a 4 px side margin, the labels aligned left and right;
      `MainWindow.axaml` sets no height, so the minimap row takes the view's own height, the same with
      empty labels as with filled ones: about 51 px on a real font, 48 px under the headless text stub)
- [x] keep the band, the marker and the pointer navigation tests green; update tests that looked up the
      baseline or the labels' canvas positions (➕ as built: `TheStrip_HoldsOneBandBelowTheMarker` replaces the
      band-above-the-baseline test; `MinimapViewStand` exposes `View` and sizes its window to the view's
      height, as the minimap row does)
- [x] run the unit tests - must pass before task 9

### Task 9: Show the time under the pointer

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/MinimapGeometry.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/MinimapGeometryTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapViewStand.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapHoverTests.cs`

- [x] write `HoveringTheStrip_ShowsALineAndItsTimeAndLeavingHidesThem` (headless): a pointer move over the
      strip shows the hover line at the pointer's x and the time `TimeAtFraction` gives, centred under it;
      a pointer exit hides both; it fails before the change by assertion
- [x] add `HoverAt`, `ClearHover`, `HoverFraction` and `HoverLabel` to `MinimapViewModel`; wire the pointer
      events in the code-behind and draw the line and the label as Technical Details states (➕ as built:
      the line is a 1 px `Border` named `HoverLine` above the marker in `AppSecondaryForegroundBrush`, the
      chart crosshair's key; the time is `HoverTimeLabel` on a `Canvas` over `LabelRow`; a covered end label
      takes `Opacity` 0, so its bounds stay laid out, and the view's `EndLabelCoverDistance` of 6 px, passed
      to `MinimapGeometry.PlaceHoverTime`, counts as covering; a move off the strip during a captured drag
      clears the hover, and `PointerExited` is ignored while the drag holds, because the capture itself
      raises it)
- [x] write tests: no extent shows nothing on hover; a hover at either end keeps the label inside the row
      and hides the end label it would overlap; a press and drag still recentre the window and the line
      follows the pointer (➕ as built: `MinimapHoverTests` holds the headless hover cases; also a newer
      sample relabels a hover at the right end)
- [x] run the unit tests - must pass before task 10

### Task 10: Verify and document the label row and the hover

- [x] every automated item of Acceptance Evidence passes; `dotnet format SemiPlot.slnx --verify-no-changes`
      and `dotnet terse` over every touched `.cs` file exit 0 (➕ as built: build 0 warnings, 0 errors; unit
      suite 1654 passed, 0 failed, 0 skipped; each of the ten named tests passes alone, the right-bound
      theory as 2 cases; the `MinimapBand.cs` grep prints nothing; terse over the 22 files of
      `git diff --name-only master...HEAD -- '*.cs'`)
- [x] `trend-interaction.md` "Archive-overview minimap" and `charting.md`'s `MinimapView` entry: the label
      row under the strip, the hover line and time, no baseline (➕ as built: "The label row" and "The
      hover" paragraphs follow "The marker"; the band control sits below the marker and the hover line)
- [x] ➕ `ui-theme.md:158` drops "Minimap baseline" from `AppSubtleLineBrush`'s users; `bench.md:338`'s
      `MinimapViewTests` row states the band below the marker, no baseline and the label row (➕ as built:
      `AppSecondaryForegroundBrush` lists the minimap's end labels, hover line and hover time; TM-4's
      acceptance in `trend-feature-spec.md` names the label row and the hover)
- [x] the manual smoke checklist steps 9-10: the operator's walk before delivery (skipped - not
      automatable)

## Post-Completion

**Manual verification**
- Walk the smoke checklist on the demo stand, including a 5-minute live follow at a 15 s window.
- On an installation with a months-long archive, check that the band appears within 2 s of start and that
  the band trails the live edge by no more than a pixel or two, and that one band read, fresh tail
  included, takes well under a second in the log at `--logging-level debug`.

**Executed by exec:**
- branch: minimap-overview

## Verify it yourself

1. Build and tests, from the repository root:
   - `dotnet build SemiPlot.slnx` - 0 warnings, 0 errors.
   - `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - 1668 passed. Master's
     `BreakGenerationTests.NoRowFallsInsideABreak` alone takes about 2 minutes; that is not a hang.
   - `git grep -n "QueryHistoryAsync" -- SemiPlot/SemiPlot.UI/Minimap/MinimapBand.cs` prints nothing.
2. The two defects of #91, by test (`--filter "FullyQualifiedName~<name>"`); each failed by assertion against
   the code before its fix and passes from `02caa09` and `7de132c` on:
   - `MinimapPointerInputTests.AWindowPastTheExtent_KeepsItsMarkerInsideTheStrip` - the marker's right edge
     was 906 px on a 900 px strip;
   - `MinimapViewModelTests.ANewerSampleThanTheExtent_MovesTheRightBoundAndItsLabel` - the right bound
     stayed at the startup extent, sticky or not.
3. The band, by test: `MinimapGeometryTests` (gaps, a flat pen, all-NaN, an infinite column, a lone column,
   the hover time's placement), `MinimapBandFeedTests` (the layer, the read schedule, cancellation, a stale
   answer after a pen change, the outage policy), `MinimapViewTests` (the strip holds exactly the band, the
   marker and the hover line; the end labels sit in the row under the strip).
4. The label row and the hover, by test: `MainWindowViewTests.TheMinimapRow_ShowsTheWholeLabelRowUnderTheStrip`
   fails with the old `Height="36"` on the minimap in `MainWindow.axaml`; `MinimapHoverTests` (the line and
   its time follow the pointer and hide on exit, the time stays in the row and hides only the end label it
   covers, a drag keeps the line under the pointer until it leaves the strip).
5. On the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), the manual smoke checklist of
   Acceptance Evidence, steps 1-10. Before the change the strip showed only labels and a marker over a line
   through its middle; after it the strip shows the drawn pen's band with a gap at each seeded break, the
   end labels sit readable in a row under it, and hovering shows a line and its time.
