# Live edge break

## Overview

- The realtime seam carries an absence: a `Sample` with a null `Value` is a break for that pen at that
  moment. `TrendCoordinator` keeps it in the batch, and the chart appends a `NaN` column there at every
  layer.
- The production writer is the existing poll: a `q = 32` row becomes its value followed by a break 1 ms
  later. This closes the backlog item "A break that opens at the live edge draws as a held line"
  (`docs/plans/backlog.md:16-21`). Today the line holds at the last value until the next history read
  covers the span.
- Later slices write breaks on OPC session loss, on a `Bad` status and ahead of a pen's first live sample.
  A break that carries its own timestamp and no value serves all three. None of them is built here.

## Context (from discovery)

- Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice live-edge-break
- Verified at `38de80f`.
- Vocabulary:
  - `Sample(int PenId, DateTime TimestampUtc, double Value)` (`SemiPlot/SemiPlot.Core/Trends/Sample.cs:3`).
  - `PenRealtimeValues.Values` is `IReadOnlyList<double>`, and its summary states that a null "would draw
    a break the archive never recorded" (`SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs:3-10`).
  - `IDataProvider.Subscribe` returns `IObservable<IReadOnlyList<Sample>>`
    (`SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:10`).
- Producer `RealtimePoll`:
  - Counts `q = 32` rows for the debug line only (`SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs:34-36`,
    `:166-169`).
  - Drops a null-`v` row (`:171-178`) and emits every other row as an ordinary `Sample` (`:180`).
  - The debug line (`:185-192`) carries the structured placeholder `{RowCount}`, the number of rows read.
    `RealtimePollReadTests.cs:148` and `RealtimeSubscriptionTests.cs:120` select entries by it.
- Hub `TrendCoordinator`:
  - `BuildRealtimeBatch` takes the ascending, distinct union of timestamps (`SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:149-161`).
  - `BuildPenValues` orders each pen's samples by timestamp and copies `Value` with `Array.ConvertAll`
    (`:163-172`). Under a `double?` `Value` it infers `double?[]` and compiles unchanged.
  - The buffer window is 100 ms (`TrendCoordinator.BatchWindow`, `:14`). The unit tests run the fake at
    `ChartTestBuilder.RealtimeInterval` = 10 ms (`ChartTestBuilder.cs:27`), so one window holds about ten ticks.
- Chart:
  - `ChartRealtimeApplier.ApplyBatch` folds at coarse layers and appends at `Raw`, on each pen's own
    timestamps (`SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs:19-42`).
  - The fold flag comes from `TrendChartViewModel.ApplyRealtimeBatch` (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:797`).
  - `TrendPenState.AppendRealtime(DateTime, double?)` already appends `NaN` for a null and keeps
    `CurrentValue` (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:77-83`). Only `Realtime_NullSample_AppendsNaNGap`
    calls it with a null (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs:56-65`).
  - `FoldRealtime(DateTime, double)` takes no null (`TrendPenState.cs:86-92`).
- `EnvelopeLine` invariants this slice keeps:
  - `AppendAfterLastColumn` rejects `X <= last X` (DA-7), accepts any column on an empty list, and trims
    10 000 columns past the 100 000 cap (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:14-15`, `:271-286`).
  - `FoldIntoLastColumn` returns false on an empty list, appends past a gap, and otherwise takes
    `Math.Min`/`Math.Max` over the last column (`:226-252`). So a `NaN` sample folded into a real column turns
    it into `NaN`.
  - `ReplaceColumns` keeps the live columns past the new history end and drops those past the requested
    end (`:195-214`). It reads no `Min` or `Max`, so a `NaN` column follows the same rule as any live column.
  - `EnvelopePath.Build` starts a new segment after a `NaN` column (`SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:40-52`).
- Axis resolution: `LocalTimeAxis.ToAxis` is `ToLocalTime().ToOADate()` (`SemiPlot/SemiPlot.UI/Chart/LocalTimeAxis.cs:8-13`).
  Verified 2026-10-19 in PowerShell: `ToOADate` maps `t`, `t + 1 tick` and `t + 9999 ticks` to the same
  double, and `t + 1 ms` to a different one.
- The history path anchors its `NaN` one tick after a break-closing bucket
  (`SemiPlot/SemiPlot.DataSource.Postgres/BucketedRowFold.cs:51-58`). That works only because
  `ReplaceColumns` inserts without an X check. This slice does not touch the history path.
- Consumers of `Sample` / `RealtimeBatch` / `PenRealtimeValues`:
  - Production: `RealtimePoll`, `PostgresDataProvider.Subscribe` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:94-98`),
    `TrendCoordinator`, `ChartRealtimeApplier`, `TrendChartViewModel`.
  - Tests: `FakeDataProvider.BuildLiveEdge` (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:202-222`),
    `TrendCoordinatorTests`, `TrendChartViewModelTests`, `TrendChartCatalogueTests`,
    `LiveEdgeArchiveJourneyTests`, `RealtimeSubscriptionTests`, `RealtimeSubscriptionDisposalTests`.
  - No test constructs `PenRealtimeValues` or `RealtimeBatch`. The test reads of `Value` and `Values`
    (`RealtimePollReadTests.cs:72,176`, `LiveEdgeArchiveJourneyTests.cs:238`) compile under `double?`.
  - `ChartHistoryRequestDebouncer.cs:56` uses the Rx `Sample` operator, not the record.
- Test seams:
  - `RealtimeArchive.AppendAsync(database, penId, archiveLocal, double? value, int quality, ...)`
    (`SemiPlot/SemiPlot.Tests.Integration/RealtimeArchive.cs:82-88`). `RealtimePollReadTests` imports it with
    `using static`. `LiveEdgeArchiveJourneyTests` does not, and it has a private `AppendAsync` of its own
    (`Journeys/LiveEdgeArchiveJourneyTests.cs:195`) that writes `OrdinaryQuality` for every pen.
  - `ArchiveRow.LastBeforeBreakQuality = 32` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveRow.cs:11`).
  - The current pin of the defect is `RealtimePollReadTests.ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample`
    (`SemiPlot/SemiPlot.Tests.Integration/RealtimePollReadTests.cs:154-178`). It asserts a single ordinary sample.
  - A coarse history column at the fake's live timestamps: `TrendChartViewModelTests.AFoldIntoACoarseEnvelopeKeepsThePrefetchedBand`
    (`TrendChartViewModelTests.cs:1014-1037`) lands `EnvelopeEndingAtTheRealtimeEpoch()` (`:2659-2670`) at a
    coarse layer. `Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns` (`:975-990`) lands no history,
    so its line is empty. `CreateViewModel`'s default window (June 2026) lies after every fake live
    timestamp (`FakeDataProvider.RealtimeEpoch` = 2026-01-01).
- The demo writer writes no live break (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs:27-28`).
  The AppHost stand therefore shows a live break only after a hand-inserted `q = 32` row.

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task). Task 3 runs its new tests once
  before the poll change to confirm that they fail.
- Complete each task fully before moving to the next. Make small, focused changes.
- Every task includes new or updated tests for the code it changes.
- All tests pass before the next task starts.
- Update this plan file when scope changes during implementation.
- `dotnet build SemiPlot.slnx` must hold at zero warnings (`TreatWarningsAsErrors`). `dotnet format
  SemiPlot.slnx --verify-no-changes` and `dotnet terse` must pass over the touched `.cs` files.

## Testing Strategy

- Unit tests (`SemiPlot.Tests.Unit`, no container):
  - `TrendCoordinatorTests`: the break survives the buffer window and the batch build.
  - New `ChartRealtimeApplierTests`: the append-never-fold rule, at `Raw`, at a coarse layer, and on an
    empty coarse line.
  - `TrendPenStateTests`: the axis resolution behind the 1 ms step.
  - `TrendChartViewModelTests`: the coarse-layer path end to end over `FakeDataProvider`.
- Integration tests (`SemiPlot.Tests.Integration`, container required):
  - `RealtimePollReadTests`: a `q = 32` row produces its value and a break.
  - `LiveEdgeArchiveJourneyTests`: the break reaches the chart's column list.
- Every new test class carries the `Component`, `Area` and `Category` traits. AwesomeAssertions throughout.
- No UI e2e suite exists beyond the headless tests above.

## Acceptance Evidence

- Defect at HEAD: `RealtimePollReadTests.ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample` passes.
  It asserts that a `q = 32` row yields exactly one sample (`RealtimePollReadTests.cs:174`). No column of
  `NaN` reaches the live edge.
- Guard: after Task 1 and before the poll change, the renamed poll test and the new journey test fail
  (Task 3, first step).
- After the change, these commands pass:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendCoordinatorTests|FullyQualifiedName~ChartRealtimeApplierTests|FullyQualifiedName~TrendPenStateTests|FullyQualifiedName~TrendChartViewModelTests"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~RealtimePollReadTests|FullyQualifiedName~LiveEdgeArchiveJourneyTests|FullyQualifiedName~RealtimeSubscriptionTests"
```

- What the integration run must show:
  - `RealtimePollReadTests.ARowMarkingTheLastSampleBeforeABreakEmitsItsValueAndThenABreak` reads exactly
    two samples for the `q = 32` row: `(pen, ToUtc(t), 11.25)` and `(pen, ToUtc(t) + 1 ms, null)`, in that order.
  - The new journey test finds, in that pen's `SnapshotColumns()`, a real column at `ToAxis(ToUtc(t))`
    followed by a `NaN` column at `ToAxis(ToUtc(t) + 1 ms)`, and `CurrentValue` equals the row's value.
  - ⚠️ A history read that lands before the snapshot puts its anchor `NaN` at `ToAxis(ToUtc(t))` between the
    two, so the journey asserts the last real column and the last column rather than the last two (Task 4).
- Integration runs need a container runtime. ASSUMPTION: Docker is available on the executing machine.
  The CI `linux` leg runs the integration project in any case.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix.
- Document issues or blockers with ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **A break is a sample without a value.** `Sample.Value` becomes `double?`, and null means "no data from
  this moment until the pen's next sample". `PenRealtimeValues.Values` becomes `IReadOnlyList<double?>`.
  The later OPC writers have no value to attach a flag to: a session loss, and the break ahead of a pen's
  first live sample. A break with a timestamp of its own serves them. The grey "no archive data" kind
  belongs to slice live-hold-to-edge and is not added here.
- **The poll stamps the break 1 ms after the marker.**
  - The archive's `t` is `timestamp(3)`, and the axis keeps milliseconds only (`LocalTimeAxis.cs:12`, verified
    above). So 1 ms is the smallest step that both sides tell apart.
  - At `+1 tick`, the history path's anchor, the break maps to the marker's X. `AppendAfterLastColumn`
    then rejects it, and the defect stays with every poll-level test green.
  - DA-7 in `AppendAfterLastColumn` stays unchanged.
  - A real row at exactly the marker's archive-local millisecond plus one would share the break's
    timestamp, and the chart would reject it. The archive writes none there: a change pair is about 100 ms
    apart (`docs/architecture/charting.md:73-75`), and a project stop and start are seconds apart.
- **A break is always appended, never folded.** `ChartRealtimeApplier` sends a null to
  `TrendPenState.AppendRealtime(ts, null)` ahead of the append-or-fold choice. The rule lives in that one
  place, and `FoldRealtime` keeps its non-null `double`.
  - At a coarse layer the folded column keeps its bucket X, which lies before the break. So the `NaN`
    column lands past it, and the next sample's fold opens a new column past the gap
    (`EnvelopeLine.cs:237-241`), as it does today after a history gap.
  - `CurrentValue` stays the last real value, because `AppendRealtime` sets it only when `value.HasValue`
    (`TrendPenState.cs:79-82`).
  - On a coarse line with no column yet (a pen seated before its first history read lands), the break
    still appends its `NaN` column, and the folds after it open a live column past it. Today such a line
    folds nothing until history lands. The plan accepts this: the next history read replaces the span under
    `ReplaceColumns`' kept-tail rule, and an exception for the empty line would be a second rule in the
    applier. `charting.md` states it (Task 5), and `ChartRealtimeApplierTests` pins it (Task 1).
- **The coordinator needs no new rule and no code change.** Its union and per-pen ordering already sort the
  break after its value, and `Array.ConvertAll` yields `double?[]`. The break's timestamp joins the union,
  so `OnLiveEdge` advances 1 ms past the marker.
- **Asymmetry kept on purpose.** A null-`v` row is still dropped by the poll. The history path flags
  such a bucket as a gap (`docs/architecture/data-integration.md:934-941`), and `v` was never null in the
  measured archive (`docs/architecture/scada-archive.md:245`). Only a `q = 32` row writes a live break in
  this slice. A `q = 32` row writes its break even when its `v` is null; only the value sample is skipped.
  That row shape was never measured either; it is pinned because it is the natural branch of the code.

## Technical Details

- `Sample.cs`: `public sealed record Sample(int PenId, DateTime TimestampUtc, double? Value);` with a one-line
  summary: a null value is a break for the pen from this moment.
- `RealtimeBatch.cs`: `PenRealtimeValues(int PenId, IReadOnlyList<DateTime> TimestampsUtc, IReadOnlyList<double?> Values)`.
  The summary states: a null is a break the provider reported; a pen carries no entry, null or real, at
  a union timestamp it did not report.
- `TrendCoordinator.BuildPenValues`: no code change. The comment at `:163` is reworded: no filler at another
  pen's timestamp; a null comes only from the provider.
- `ChartRealtimeApplier.ApplyBatch`, per index:
  - `value is null` → `state.AppendRealtime(ts, null)`.
  - else `foldIntoColumn` → `state.FoldRealtime(ts, value.Value)`.
  - else `state.AppendRealtime(ts, value)`.
  - The comment at `:28-29` is reworded the same way.
- `RealtimePoll.ReadSamplesAsync`, per row:
  - `isBreakMark = reader.GetInt32(3) == LastBeforeBreakQuality`.
  - When `v` is not null, add `Sample(id, utc, v)`.
  - When `isBreakMark`, add `Sample(id, utc + BreakAnchorStep, null)`. The step is added on the UTC side,
    as `BucketedRowFold` adds its tick.
  - A named constant holds the step: `BreakAnchorStep = TimeSpan.FromMilliseconds(1)`, with a one-line
    comment pointing at `docs/architecture/data-integration.md#realtime`.
  - ⚠️ As built the step is `private static readonly TimeSpan _breakAnchorStep` (`RealtimePoll.cs:42`): a
    `TimeSpan` cannot be `const`, and `.editorconfig` requires `_camelCase` for private static readonly
    fields. `data-integration.md#realtime` cites that name; `BreakAnchorStep` below means this field.
  - The comment at `:34-35` ("read for the debug line only") and the one at `:171-172` are rewritten.
  - The debug line keeps the `{RowCount}` placeholder with its name and meaning (rows read), because two
    tests select by it. Its other counts name what the tick emitted: value samples, rows dropped for a null
    value, and breaks.
- `FakeDataProvider`: a one-shot knob, `BreakRealtimePenOnce(int penId)`. It stores the pen in a private
  nullable field. The next tick emits that pen's value and then `Sample(id, t + 1 ms, null)`, as the poll
  does, and clears the field; later ticks are ordinary. The existing knobs (`StaggerRealtimeTimestamps`,
  `PoisonRealtimeWindow`) stay as they are.

## What Goes Where

- **Implementation Steps**: the code, tests and documentation in this repository.
- **Post-Completion**: the manual check on the AppHost stand, and the remaining roadmap slices.

## Implementation Steps

### Task 1: Carry a break from `Sample` through the batch to the applier

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/Sample.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartRealtimeApplierTests.cs`

- [x] Make `Sample.Value` `double?` and `PenRealtimeValues.Values` `IReadOnlyList<double?>`, and rewrite both
      summaries (Technical Details). Confirm that the build fails at exactly `ChartRealtimeApplier.cs:34`:
      `FoldRealtime` takes a `double`. That is the compiler guard. `TrendCoordinator.cs:171`
      (`ConvertAll` now yields `double?[]`), `ChartRealtimeApplier.cs:38` (`AppendRealtime` takes `double?`),
      `RealtimePoll.cs:180` and `FakeDataProvider.cs:216` (both convert to `double?`) still compile.
- [x] Reword the comment at `TrendCoordinator.cs:163`. No code there changes.
- [x] Put the null branch in `ChartRealtimeApplier.ApplyBatch` ahead of the fold-or-append choice, and
      reword its comment.
- [x] Add `BreakRealtimePenOnce` to `FakeDataProvider` (Technical Details).
- [x] Test in `TrendCoordinatorTests`: call `BreakRealtimePenOnce(FirstPen.PenId)`, then advance one buffer
      window. The first pen's `PenRealtimeValues` holds exactly one null. Its timestamp is 1 ms after the
      entry before it, and that entry is a real value. `batch.Timestamps` holds the null's timestamp, and
      `Values.Count == TimestampsUtc.Count` still holds. The second pen carries no null. A later window
      carries no null for either pen.
- [x] Test in `ChartRealtimeApplierTests` (`Component=UI`, `Area=Chart`, `Category=Unit`, plain `[Fact]`, as
      `TrendPenStateTests` builds `TrendPenState` under `[Fact]`), built over `new ChartNavigationController()`
      and `TrendPenState` instances:
      - At `foldIntoColumn: false`, a `[value, null]` pair on `[t, t + 1 ms]` adds a real column and then a
        `NaN` column.
      - At `foldIntoColumn: true`, after a history column, the value folds into it, and the null adds a
        `NaN` column past it. The last real column keeps its `Min`/`Max`, not `NaN`.
      - At `foldIntoColumn: true` on a pen with no column, `[value, null, value2]` on `[t, t + 1 ms, t + 1 s]`
        leaves two columns: `NaN`, then a real column at `t + 1 s`. `CurrentValue` equals `value2`. This
        pins the accepted empty-line behaviour (Solution Overview).
      - In the first two cases `CurrentValue` equals the value.
- [x] Run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`.
      Both must pass before Task 2.

### Task 2: Pin the break column through the chart

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

No production change is expected in this task. A red `TrendPenStateTests` case means the axis resolution
differs from the Context; a red view-model test after a correct applier test means the test's history setup
is wrong. Fix neither by changing `EnvelopeLine`.

- [x] `TrendPenStateTests`: after `AppendRealtime(t, 5.0)`, `AppendRealtime(t + 1 tick, null)` is rejected
      (one column) and `AppendRealtime(t + 1 ms, null)` adds a `NaN` column. `CurrentValue` stays `5.0`.
      This pins the reason for `BreakAnchorStep` without a container.
- [x] `TrendChartViewModelTests`: model the setup on `AFoldIntoACoarseEnvelopeKeepsThePrefetchedBand`
      (`TrendChartViewModelTests.cs:1014-1037`): `AddPen`, `TrackDataExtents`, `ZoomOutAndIssueTheCoarseRead`,
      `ReleaseAndAwait(... EnvelopeEndingAtTheRealtimeEpoch())` and `LandTheMarginReadFromTheReleasedGate`.
      Do not use the default June 2026 window: history there lies past every fake live timestamp, and DA-7
      rejects the break. Then call `BreakRealtimePenOnce(FirstPen.PenId)` and advance one buffer window.
      The pen's columns grow by exactly two: the column at `RealtimeEpoch` is widened and not `NaN`, a `NaN`
      column follows it, and a real column follows that. `CurrentValue` equals the last column's `Center`.
- [x] Run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`. It must pass before Task 3.

### Task 3: Make the poll write a break after a `q = 32` row

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] Write the tests first and confirm the guard:
      - Rename `ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample` to
        `ARowMarkingTheLastSampleBeforeABreakEmitsItsValueAndThenABreak`. It asserts exactly two samples in
        order: `11.25` at `ToUtc(appended)`, and `null` at `ToUtc(appended) + 1 ms`.
      - Add `ARowMarkingTheLastSampleBeforeABreakWithANullValueEmitsOnlyTheBreak`: a `q = 32` row with `v`
        null yields one sample, `null` at `ToUtc(appended) + 1 ms`.
      - Add a journey to `LiveEdgeArchiveJourneyTests`, next to
        `ARowWrittenAfterStartupReachesTheChartOnceAndMovesItsLiveEdge` (`:33`) and using its stand. Take the
        batch gate, then write one row with the qualified call
        `RealtimeArchive.AppendAsync(Database, pen.PenId, seededLastLocal.AddSeconds(1), value, ArchiveRow.LastBeforeBreakQuality, TestContext.Current.CancellationToken)`;
        the class's own `AppendAsync` hides the unqualified name. Its partition step is `IF NOT EXISTS`, so the
        template clone accepts it (`RealtimeArchive.cs:94-96`). Await the batch. The pen's last two columns
        must be a real column at `LocalTimeAxis.ToAxis(ToUtc(t))` and a `NaN` column at
        `LocalTimeAxis.ToAxis(ToUtc(t) + 1 ms)`. `CurrentValue` must equal the row's value.
      - Run the integration filter below. The three new or renamed tests must fail: one sample for the
        `q = 32` row, none for the null-`v` row, and no `NaN` column on the chart.
- [x] Change `ReadSamplesAsync` as given in Technical Details: add `BreakAnchorStep`, emit the break
      after the value, emit it even when `v` is null, rewrite the two comments and the debug line, keep
      `{RowCount}`.
- [x] Keep `ARowCarryingANullValueEmitsNoSampleReportsNoFaultAndIsNotReadAgain` (`:122-152`) unchanged
      and green. It pins the null-`v` drop of a `q = 0` row, and it reads `{RowCount}`.
      `RowsOnAVariableOfTheirOwnReachTheChartWithoutBreakingAnyPen` (`LiveEdgeArchiveJourneyTests.cs:93-121`)
      must stay green: its rows are `q = 0`.
- [x] Run `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter
      "FullyQualifiedName~RealtimePollReadTests|FullyQualifiedName~LiveEdgeArchiveJourneyTests|FullyQualifiedName~RealtimeSubscriptionTests"`
      and the unit project. Both must pass before Task 4.

### Task 4: Verify acceptance criteria

- [x] Verify the Overview: a `q = 32` row reaches the chart as its value plus a `NaN` column at `Raw`
      and at a coarse layer, and `CurrentValue` stays the last real value. Raw: the journey test (real poll)
      and `ChartRealtimeApplierTests`; coarse: `ChartRealtimeApplierTests` and `TrendChartViewModelTests`.
      ➕ The journey test failed 2 of 3 isolated runs: a history read landing before the snapshot puts its
      anchor `NaN` at the marker's X. The test now asserts the last real column and the last column; 6 of 6
      isolated runs pass.
- [x] Verify the scope guard: `git diff origin/master --name-only` and `git status --porcelain --untracked-files=all`
      list only the files named in the Files blocks of Tasks 1, 2, 3 and 5 and this plan. No file of the
      history path (`BucketedRowFold.cs`, `ArchiveStatements.cs`, `EnvelopeLine.cs`, `EnvelopeSplice.cs`,
      `ChartHistoryPlanner.cs`, `TrendPenState.cs`) appears. `git diff origin/master -- SemiPlot | grep -i -c "opc"`
      prints `0`. (The untracked roadmap file belongs to the controller.)
- [x] Run `dotnet build SemiPlot.slnx` (0 warnings) and `dotnet format SemiPlot.slnx --verify-no-changes`.
      Only NU1902 (MessagePack, `SemiPlot.AppHost`, present on master) remains.
- [x] Run `dotnet test SemiPlot.slnx`. Unit 2284/2284, Integration 195/195 at Task 4; after the review
      fixes (one headless Raw test added) Unit 2285/2285, Integration 195/195.
- [x] Run `dotnet terse` over every touched `.cs` file. It must exit 0.

### Task 5: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/plans/backlog.md`

- [x] `charting.md:64-67` ("Gaps via NaN"): a `NaN` comes from the history path or from a live break,
      which `TrendPenState` appends for a null value.
- [x] `charting.md:108-119` (realtime append and live-edge join): a live break is always appended, at
      every layer, and the fold after it opens a column past the gap. Replace "A pen with no column at all
      folds nothing" (`:116-117`): a pen with no column folds no value, but a break appends its `NaN` column,
      and the folds after it open a live column before any history read.
- [x] `charting.md:122-128` (a history apply keeps the live tail): a live break column follows the
      kept-tail rule like any live column. After a read that ends at the marker, the pen can hold two
      adjacent `NaN` columns, the history anchor at the marker's X and the live break 1 ms past it;
      `Build` draws one break.
- [x] `charting.md:463-465` (the `ChartRealtimeApplier` entry): the null branch ahead of the fold-or-append choice.
- [x] `charting.md:769-778` (data contract, realtime): `double?` values, null = a break the provider
      reported, and still no entry at a timestamp the pen did not report.
- [x] `trend-interaction.md:70-72` and `:77-79`: rewrite both as-built notes. Realtime is `double?`, and
      a `q = 32` row maps to a live break 1 ms after the marker.
- [x] `data-integration.md:70` (the `Sample` row of the DTO table): a null `Value` is a break the provider
      reported.
- [x] `data-integration.md:979-985`:
      - Replace the `q = 32` bullet: the poll emits the value and then a break 1 ms later. Give the reason
        for 1 ms: the axis keeps milliseconds, and DA-7 rejects an equal X.
      - Reword the null-`v` bullet: the drop stays, the history path differs, and the reason is no longer
        that `Sample.Value` is non-nullable.
- [x] `backlog.md:16-21`: remove the item "A break that opens at the live edge draws as a held line".
- [x] ➕ Review fixes: `docs/architecture/trend-feature-spec.md` (DA-8, RT-3), `docs/architecture/testing-strategy.md`
      (the unit area table, the journey table) and `docs/architecture/bench.md` (a live break on the stand) name
      the live break as well; none was in this task's Files block.

## Post-Completion

**Manual verification:**

1. Start the demo stand: `dotnet run --project SemiPlot/SemiPlot.AppHost`.
2. With a pen in view at `Raw` and sticky, insert one row for that pen through `psql` as `scada_writer`:
   `l = 0`, `q = 32`, `t` one second past the newest row.
3. The pen's line ends at that row's value and draws no segment to the demo writer's next row.
   The line resumes at the next row.
4. Zoom out to a `Minute` layer and repeat. The break shows, and the line resumes in a column past it.
5. The legend's current value shows the marker's value, not a dash, until the next row arrives.

**Remaining slices** (docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md):

- archive-state-from-catalogue: the archive connection state from the catalogue reads, bounded,
  connection-class faults only.
- live-source-port: a Core live-source port, with the poll as its first implementation, behaviour-preserving.
- opc-demo-server: a Simple-Scada-shaped OPC UA server in the seeder's demo writer, `--archive-lag`,
  package line 2.0.1.
- opc-live-source: the OPC UA live edge, the poll deleted, closes #116.
- opc-settings: the OPC block in the settings window.
- live-hold-to-edge: the grey no-archive-data break and the held line to the edge.

**Executed by exec:**

- branch: live-edge-break

## Verify it yourself

1. The defect on master. At `38de80f`, `RealtimePollReadTests.ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample`
   passes: a `q = 32` row yields one sample and no break.
2. The guard. On `live-edge-break`, put the master poll back and run the poll and journey filter: 4 of 11
   fail (checked 2026-10-19). They are the two `ARowMarkingTheLastSampleBeforeABreak...` poll tests, the break
   journey, and `ARowCarryingANullValueEmitsNoSampleReportsNoFaultAndIsNotReadAgain`, which now reads the new
   debug counts. The poll change and its tests landed together in `43c5868`, so no commit on the branch
   carries the red state by itself.

   ```powershell
   git show master:SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs > SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs
   dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~RealtimePollReadTests|FullyQualifiedName~LiveEdgeArchiveJourneyTests"
   git checkout -- SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs
   ```

3. The fix. On `live-edge-break` both Acceptance Evidence commands pass (container required for the second):
   - the poll test reads `(pen, ToUtc(t), 11.25)` then `(pen, ToUtc(t) + 1 ms, null)`, and the debug entry
     counts one row, one value, no drop, one break;
   - the journey sees one batch `[t, t + 1 ms]` with values `[value, null]`, `Navigation.To` at `t + 1 ms`,
     the last real column at `ToAxis(t)`, the last column `NaN` at `ToAxis(t + 1 ms)`, and `CurrentValue` equal
     to the value.
4. Without a container, `TrendChartViewModelTests.ABreakAtRawAppendsANaNColumnOneMillisecondPastItsValue` and
   `ABreakAtACoarseLayerAppendsANaNColumnAndTheNextFoldOpensAColumnPastIt` pin the same column shape at `Raw`
   and at a coarse layer over `FakeDataProvider.BreakRealtimePenOnce`.
5. On screen: the Manual verification steps above. The demo writer plants no break, so the `q = 32` row is
   inserted by hand (`docs/architecture/bench.md`, The demo writer). Before the fix the line holds at the
   marker's value and joins the writer's next row; after it the line ends at the marker and resumes at the next
   row, and the legend keeps the marker's value.
