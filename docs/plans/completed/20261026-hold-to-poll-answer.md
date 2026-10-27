# Hold to the poll answer

## Overview

- Issue #152: the SCADA archives changes only, so a constant or rarely changing pen ends at its last change, which
  can be hours old, and the chart shows no current value for it.
- Each pen's last value is drawn as a horizontal segment from its last column to the moment of the last successful
  archive poll. While the archive answers, the segment reaches the current time within one poll interval
  (`poll_interval_ms: 250`), and a following window moves with the answers even when no pen changes.
- While the connection is lost, polls stop answering and the segment stops at the last answer L.
- An outage of 5 s or less is invisible: the answer after it writes no break, and the hold bridges it. A break would
  erase every constant pen's hold until the pen next changes, because a following window issues no history read.
- When the first answer after a failed poll comes more than 5 s after L, the chart writes a break at L into every pen
  and at once re-reads the window. The SCADA re-sends its buffered rows after a reconnect, so the read draws the
  outage from the archive and removes the break, and each pen holds again to the latest answer. A break stays only
  where the archive itself carries one (a project stop row).
- The segment ends at a break: a `NaN` column as the last column draws no hold, and a pending unread-span mark
  (#120) caps the hold at the mark, the end of the span the last read drew.
- The segment is a render mark, not a stored column. The cursor, hover and marker readouts read the held value
  inside it.
- When a new sample arrives, the last point connects to it by the pen's line style; on an Interpolated pen the held
  segment then turns into a slope, and a sample stamped before the hold end redraws the line backwards. Both are
  accepted.
- Known limits:
  - The hold assumes the SCADA project flushes its archive every second (`WriteArchiveToDB(0)`,
    `[SUP:20261023]`). Without the flush script a change reaches the archive minutes late
    (`docs/architecture/scada-archive-observed.md#how-late-a-value-arrives`), and the hold shows the old value over
    it for that long.
  - The poll keeps one `lastSeen` across all pens; a row committed after a row with a later stamp is never
    delivered live, and the hold shows the old value until a history read covers it (backlog).
  - A pen unchanged for longer than the history seed bound (the wider of the window and one day) has no column in a
    window of a day or less, so it draws no hold (backlog).
  - A killed SCADA writes no break while the database keeps answering, so the segment keeps growing until the
    archive shows the next project start.
- PR body carries `Closes #152`.

## Context (from discovery)

Verified on `master` `8115102`, 2026-10-26.

- `ILiveSource` (`SemiPlot/SemiPlot.Core/Data/ILiveSource.cs:6-9`) has one member,
  `IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)`.
- `PostgresLiveSource.PollAsync` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresLiveSource.cs:52-88`) emits only
  when a poll returned samples (`:76-79`). `RealtimePoll.ReadOnceAsync`
  (`SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs:43-70`) logs a failure and returns the empty list
  (`:64-69`), so a failed poll and an answered poll with no change look the same to the caller.
- `TrendCoordinator.BuildRealtimeBatches` (`SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:97-110`) buffers the
  stream over `BatchWindow` (100 ms, `:14`) and drops a window with no timestamps (`:104`).
  `TrendChartViewModel` (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:81`) is its one subscriber.
- `RealtimeBatch(Timestamps, Pens)` (`SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs:17-19`).
- `TrendChartViewModel.ApplyRealtimeBatch` (`TrendChartViewModel.cs:793-812`) forgets a Raw range in hand when it
  folds, hands the batch to `ChartRealtimeApplier.Apply` (`SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs:9-17`)
  and asks for a redraw. `Apply` calls `ChartNavigationController.OnLiveEdge` only when the batch has timestamps
  (`:13-16`); `OnLiveEdge` (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:179-200`) moves a sticky window
  and records `NewestSample`, which the unread mark, the history planner and the minimap read.
- `EnvelopeLine.Render` and `DistanceTo` build the path with
  `EnvelopePath.Build(_columns, first, lastExclusive, _penLineStyle, ...)` (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:94-95`,
  `:133-134`). `Build` (`SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:31-76`) ends a column whose `Min` differs from
  its `Max` on an exit vertex at the `Min` or the `Max` (`:66-74`). The unread mark is `_unreadFromX` (#120).
- The readouts read the history envelope only: `ChartCursorReader.ReadAt` and `ReadPenAt`
  (`SemiPlot/SemiPlot.UI/Chart/ChartCursorReader.cs:11-30`) call `CursorReadoutModel.ReadPenAt`
  (`SemiPlot/SemiPlot.Core/Trends/CursorReadoutModel.cs:19-45`), which returns null past the envelope's last
  timestamp (`:29-32`); `ReadAt` walks only pens that have an envelope (`ChartCursorReader.cs:13-15`).
- The fake live edge for unit tests is `FakeDataProvider.Subscribe` (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:194-230`).
- A hold render path existed in `fc1837d` (#147, reverted by #149): `EnvelopePath.Hold` returning a `HeldSpan`, a
  vertex at the last column's value when the exit vertex differs from it plus a vertex at the hold end, and
  `EnvelopeLine.SetHoldEnd`. Its OPC confirmation (`confirmedX`), `BreakKind` and the ahead-break shade do not
  apply here.

## Development Approach

- **testing approach**: TDD. Each task writes its failing tests first, then the code.
- complete each task fully before moving to the next; all tests pass before the next task starts
- run `dotnet build SemiPlot.slnx` and the affected test project after each task

## Testing Strategy

- unit: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopePathTests.cs`, `EnvelopeLineTests.cs`, `TrendPenStateTests.cs`,
  `ChartRealtimeApplierTests.cs`, `ChartCursorReaderTests.cs` (new, with the `Component`, `Area` and `Category`
  traits), `ChartMarkerReadingsTests.cs`, `TrendChartViewModelTests.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- integration (container): `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionTests.cs` and
  `RealtimePollTests.cs`

## Acceptance Evidence

Automated, the new cases red on `master` and green after the change:

```
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~EnvelopePathTests|FullyQualifiedName~EnvelopeLineTests|FullyQualifiedName~TrendPenStateTests|FullyQualifiedName~ChartRealtimeApplierTests|FullyQualifiedName~ChartCursorReaderTests|FullyQualifiedName~ChartMarkerReadingsTests|FullyQualifiedName~TrendCoordinatorTests|FullyQualifiedName~TrendChartViewModelTests"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~RealtimeSubscriptionTests|FullyQualifiedName~RealtimePollTests"
```

Manual, on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. Hide every fast pen and leave a constant or slow one: the window keeps following, and the pen draws a horizontal
   segment from its last change to the right edge that grows with time.
2. The hover readout over the segment shows the value the sidebar shows.
3. A stepped and an interpolated pen: when the next change arrives, the segment turns into a step or a slope.
4. Stop the bench container for a few seconds or less and start it again: the segments pause and continue with no
   break. Stop it for longer than 5 s: the segments stop growing. Start it again: each pen shows a break from the
   moment the segments stopped, the chart re-reads the window at once, and where the archive holds data for the
   outage the break goes and the segments reach the current answer again.
5. Pan an hour into the past and drag forward past the band: the segment stops at the unread break.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep this plan in sync with the work

## Solution Overview

- The live source reports every answered poll, empty or not, as a `LivePoll`. A failed poll reports nothing. The
  answer time is the poll loop's scheduler clock taken before the statement runs, because the statement reads the
  snapshot of its own start. The first answer after one or more failed polls carries `ResumesAfterFailure`.
- The coordinator keeps a window that holds polls but no samples. `RealtimeBatch` carries the newest answer of the
  window and whether any poll in it resumed after a failure.
- `ChartRealtimeApplier` applies a batch in this order: on a batch that ends a visible outage, each pen writes a
  break at its current hold end; then the samples; then each pen's hold end moves to the answer. One answer serves
  every pen of the subscription, visible or not. A visible outage is a resumed batch whose answer lies more than
  5 s after the answer of the batch before it; the source keeps reporting every resumption as a fact, and the
  chart owns the threshold.
- On a visible outage `TrendChartViewModel` re-reads the window through its existing `RequeryAllPens`, so the read
  comes from `ChartHistoryPlanner.Next` like every other. The covering-read rule then removes the resume break.
- The navigation follows answers: a sticky window moves its live edge to the answer through a member of its own that
  leaves `NewestSample` alone, because `NewestSample` feeds the unread mark, the history planner and the minimap
  and must stay a sample stamp.
- `EnvelopeLine` keeps the hold end under its lock and never moves it backwards. `EnvelopePath.Hold` returns the
  held span when the last column is a value and the effective end lies past it; the effective end is the hold end,
  capped at the unread mark when the last column sits at or before the mark. `Build` appends a vertex at the last
  column's `Center` when the exit vertex differs from it, then a vertex at the effective end at the same value, when
  the range reaches the last column.
- The readouts fall back to the held value: inside the held span `ChartCursorReader` returns
  `TrendPenState.HeldValueAt(time)` where the envelope read returns null, also for a pen that has no envelope. The
  marker readings go through `ReadPenAt` and follow. The delta cursor's Δy inside the held span stays empty
  (backlog).

## Technical Details

- `SemiPlot.Core/Data/LivePoll.cs`:
  `public sealed record LivePoll(DateTime AnsweredAtUtc, IReadOnlyList<Sample> Samples, bool ResumesAfterFailure)`.
  `ILiveSource.Subscribe` returns `IObservable<LivePoll>`.
- `RealtimePoll.ReadOnceAsync` returns `IReadOnlyList<Sample>?`, null for a failed read. `PostgresLiveSource.PollAsync`
  takes `_scheduler.Now.UtcDateTime` before the read, emits a `LivePoll` for every non-null read, and tracks whether
  the previous read failed in a field of the source, so a pen-set switch during an outage keeps the mark.
- `RealtimeBatch(Timestamps, Pens, DateTime AnsweredAtUtc, bool ResumesAfterFailure)`; `TrendCoordinator` keeps a
  window when it holds at least one poll.
- `TrendChartViewModel.ApplyRealtimeBatch` forgets the Raw range in hand only when the batch has timestamps.
- `ChartNavigationController.OnLiveEdge(DateTime answeredAtUtc, DateTime? newestSampleUtc)` moves a sticky
  window's live edge to the later of the two and raises `WindowChanged` once, only when the window moved; it
  writes `NewestSample` from the sample alone. (Review fix: it replaced `OnLiveEdge(now)` plus `OnAnswer`, which
  raised `WindowChanged` twice per batch with samples.)
- `HeldSpan(double FromX, double ToX, double Value)` beside `EnvelopePoint`. `EnvelopePath.Hold(columns, holdEndX,
  unreadFromX)` and `Build(columns, first, lastExclusive, lineStyle, holdEndX, unreadFromX, destination)`.
- `EnvelopeLine`: `_holdEndX` (`NaN` for none) under `_renderStateLock`; `SetHoldEnd(double x)` keeps the larger of
  the old and new value; `BreakAtHoldEnd()` appends a `NaN` column where `Hold` ends, the hold end capped at the
  unread mark, and records it as the resume gap: a value stamped before it goes in ahead of it while it is the
  last column, and a `ReplaceColumns` whose requested end reaches it removes it, as for the unread gap;
  `ReadHeldSpan()` returns `Hold` under the lock.
- `TrendPenState.ApplyHold(DateTime answeredAtUtc)`, `BreakHold()` and `HeldValueAt(DateTime timeUtc)`.
- `ChartRealtimeApplier.ShortestVisibleOutage = TimeSpan.FromSeconds(5)` and a `DateTime? _lastAnsweredAtUtc`.
  `Apply` returns true when `batch.ResumesAfterFailure` and `AnsweredAtUtc - _lastAnsweredAtUtc` exceeds the
  threshold; with no answer before, it returns false. Only then does it call `BreakHold` on every pen.
  `TrendChartViewModel.ApplyRealtimeBatch` calls `RequeryAllPens()` on true once a history read has started,
  after `Apply` moved a sticky window to the answer.
- In the repeated autumn hour `LocalTimeAxis.ToAxis` runs backwards, so the hold end stays put for that hour, as
  live appends are already rejected there.

## What Goes Where

- **Implementation Steps**: code, tests, docs, backlog entries.
- **Post-Completion**: the stand checks.

## Implementation Steps

### Task 1: Report every answered poll from the live source

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/LivePoll.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/ILiveSource.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresLiveSource.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs` (a switch for answered polls with no
  samples, and one for failed polls)
- Modify: the integration tests that read `Subscribe` or `ReadOnceAsync`: `RealtimeSubscriptionTests.cs`,
  `RealtimeSubscriptionDisposalTests.cs`, `RealtimePollTests.cs`, `RealtimePollReadTests.cs`,
  `RealtimeEmptyArchiveTests.cs`, `Journeys/LiveEdgeArchiveJourneyTests.cs`, `Journeys/BreakRenderArchiveJourneyTests.cs`;
  every collector there waits for a poll with samples

- [x] write `RealtimeSubscriptionTests` case: a subscription over a pen with no new rows emits polls with empty
  samples and rising `AnsweredAtUtc`, each taken before its statement
- [x] change `RealtimePollTests.ASubscriptionThatOnlyFailsNeitherCompletesNorFaults` to the `LivePoll` type and keep
  its "emits nothing" assertion
- [x] write a case for `ResumesAfterFailure`: a failing read followed by an answered one marks the answer
- [x] add `LivePoll`, change `ILiveSource.Subscribe`, `RealtimePoll.ReadOnceAsync` and `PostgresLiveSource.PollAsync`
- [x] adapt `FakeDataProvider` and the existing callers
- [x] run both test projects

### Task 2: Carry the poll answer through the coordinator

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`

- [x] write tests: a window of answered polls with no samples yields a batch with the newest answer and no
  timestamps; a window with samples carries the newest answer; a window with a resumed poll carries
  `ResumesAfterFailure`; a window with no poll yields no batch
- [x] add the two fields and keep answered windows in `BuildRealtimeBatches`
- [x] run the unit suite

### Task 3: Draw the hold in the envelope path

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopePathTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`

- [x] write `EnvelopePathTests`: a degenerate last column and a hold end past it add one vertex at the hold end at
  the last value; a last column with `Min` below `Max` adds a vertex at its `Center` and one at the hold end, both at
  the `Center`; a gap as the last column adds none; a hold end at or before the last column adds none; a pending
  unread mark caps the hold at the mark; a range that does not reach the last column adds none
- [x] write `EnvelopeLineTests`: `SetHoldEnd` never moves the end backwards; `BreakAtHoldEnd` appends a gap at the
  hold end and the hold is gone after it; `ReadHeldSpan` matches `Build`
- [x] add `HeldSpan`, `EnvelopePath.Hold`, the `Build` parameters, `_holdEndX`, `SetHoldEnd`, `BreakAtHoldEnd` and
  `ReadHeldSpan`
- [x] run the unit suite

### Task 4: Apply the answer to every pen and to the window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartRealtimeApplierTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] write tests: a batch with an answer and no samples sets every pen's hold end; a batch with samples appends
  first, then holds from the new last column; a resumed batch breaks every pen at its old hold end before its
  samples; `HeldValueAt` returns the last value inside the span and null outside
- [x] write a `TrendChartViewModelTests` case: while sticky, a batch with an answer and no samples moves
  `Navigation.To` and leaves `Navigation.NewestSample` unchanged; detached, it moves neither
- [x] add `ApplyHold`, `BreakHold`, `HeldValueAt`, `ChartNavigationController.OnAnswer`, the applier calls, and the
  timestamps condition on `Forget` in `ApplyRealtimeBatch`
- [x] run the unit suite and adjust the headless view tests that count vertices, frames or picks and now meet the
  hold or the redraw per answer
- [x] run the unit suite

### Task 5: Read the held value at the cursor

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartCursorReader.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartCursorReaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerReadingsTests.cs`

- [x] write tests: `ReadAt` and `ReadPenAt` return the held value inside the held span past the envelope's end, and
  for a pen with no envelope; the envelope value where the envelope covers the cursor; null for a hidden pen
- [x] write a `ChartMarkerReadingsTests` case: a marker inside the held span reads the held value
- [x] fall back to `HeldValueAt` where the envelope read returns null, and walk pens without an envelope in `ReadAt`
- [x] run the unit suite

### Task 6: Verify acceptance criteria

- [x] verify every point of the Overview
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot.slnx`
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files

### Task 7: [Final] Update documentation

- [x] `docs/architecture/data-integration.md`: `ILiveSource` and `LivePoll` in the provider surface (`:126-140`);
  the realtime section from `:955`, including the contract bullet at `:966` ("emits no sample") and the first-tick
  sentence at `:999`
- [x] `docs/architecture/charting.md`: the hold, the break on a resumed answer and the autumn hour in "Realtime
  append / live-edge join"; the readouts; the realtime line of the data contract (`:794`)
- [x] `AGENTS.md:399`: the `ILiveSource` line
- [x] `docs/plans/backlog.md`: per-pen `lastSeen` in the poll; a seed bound wide enough for a pen unchanged longer
  than a day; Δy of the delta cursor inside the held span
- [x] move this plan to `docs/plans/completed/` (left in docs/plans/ - delivery archives it)

### ➕ Task 8: Break and re-read only after a visible outage

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartRealtimeApplierTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `docs/architecture/charting.md`, `docs/architecture/data-integration.md`

- [x] write `ChartRealtimeApplierTests`: a resumed batch no later than 5 s after the last answer breaks nothing and
  holds on; one past 5 s breaks at the old hold end; a resumed first batch breaks nothing
- [x] write `TrendChartViewModelTests`: after a 6 s outage the resumed batch re-reads the window, and the read
  removes the break and holds the constant pen to the latest answer; after a 1 s outage nothing is read or broken
- [x] add `ShortestVisibleOutage`, the `Apply` result and the `RequeryAllPens` call
- [x] state the rule in `charting.md` and `data-integration.md`
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot.slnx`

## Post-Completion

- The stand checks in Acceptance Evidence, steps 1 to 5.
