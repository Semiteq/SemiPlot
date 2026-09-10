# Envelope render lock (issue #59)

## Overview

Dragging the timeline crashes the viewer with `ArgumentOutOfRangeException` from
`EnvelopePath.Build` (issue #59). `EnvelopeLine` renders on Avalonia's render thread while
`TrendPenState` rewrites the same `List<EnvelopeColumn>` on the UI thread, and nothing synchronises
the two. The fix is one `Lock` per pen, owned by the plottable together with the list it guards,
taken by every mutator and by both render-thread readers. No allocation profile changes on the hot
path. `Render` holds the lock only for the visible-column read; `GetAxisLimits` holds it over the
whole buffer but runs only for an axis without limits.

## Context (from discovery)

- `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:12-14` takes an `IReadOnlyList<EnvelopeColumn>` and
  keeps the reference. `Render` (`:67-70`) calls `EnvelopePath.VisibleRange` and then
  `EnvelopePath.Build` against that list; `GetAxisLimits` (`:38-65`) walks it with `foreach` and
  indexes `[0]` and `[^1]`. The `SKPath` construction (`:77-91`) reads only `_pathPoints`, the
  plottable's own buffer.
- `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:13-26` clamps `lastExclusive` to `columns.Count` at
  the moment of the search; `Build` (`:29-74`) indexes `columns[index]` at `:43`, the line in the
  stack trace. On a stable list an out-of-range index is unreachable. `Render` is `EnvelopePath`'s
  only production caller.
- `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs` holds the writers: `LoadHistory` (`:45-59`,
  `Clear` then `Add` per column), `ClearHistory` (`:61-65`), `AppendRealtime` (`:68-85`) with
  `TrimToCap` (`:87-96`, `RemoveRange` past the 100k cap), and `FoldRealtime` (`:100-122`, indexer
  write). The constructor (`:15-21`) receives the list as a separate argument, and the comment at
  `:13-14` is the only thing tying it to the line's list. `LastNonGapCenter` (`:124-136`) and the
  `Columns` property (`:27`) read on the UI thread.
- Every mutator caller runs on the UI thread: history lands through
  `ChartHistoryRequestDebouncer.cs:64` (`ObserveOn(uiScheduler)` ahead of `Deliver`, then
  `TrendChartViewModel.ApplyHistory` at `:448` and `:501`), realtime through
  `TrendCoordinator.cs:105` (`ObserveOn(_uiScheduler)`) into `ChartRealtimeApplier.cs:34,38`.
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:365-375` (`BuildPenState`) creates the list,
  the line and the state, and adds the line to the plot. `AddPen` is reached only from
  `App.axaml.cs:123` at startup, before the window is shown, so ScottPlot's own `PlottableList` is
  never mutated during a frame.
- `ScottPlot.Avalonia` 5.1.59 draws through a `CustomDrawOp : ICustomDrawOperation`, so
  `EnvelopeLine.Render` runs on the render thread. `GetAxisLimits` is reached from the render
  pipeline only through `AutoscaleUnsetAxesToData`, for an axis without limits;
  `ChartAxisBinder.cs:30` sets Y limits and the view sets X, so after the opening frames it is not a
  per-frame reader.
- Test builders construct the same triple: `ChartHoverReadoutTests.cs:104-107` (inside `CreatePen`,
  `:100-108`), `TrendLegendViewModelTests.cs:129-130`. `ChartGapRenderTests.cs:145` renders a
  headless `Plot` through `Plot.GetImage`, SkiaSharp only, no Avalonia.
- `docs/architecture/charting.md:139` already records that `RenderFinished` fires on Avalonia's
  render thread; `:10-16` and `:48-49` describe the live-reference design without naming the threads.
- Precedent for the house rule on shared mutable state: `ChartHistoryRequestDebouncer.cs:26`
  (`private readonly Lock _gate = new();`), described in
  `docs/plans/completed/20260907-chart-drag-responsiveness.md:329`.

## Development Approach

- **testing approach**: Regular, except that the stress test lands first because it is the
  acceptance evidence and must be seen red on the unfixed code
- complete each task fully before moving to the next
- every task carries its tests or its gate; all tests pass before the next task starts
- update this plan when scope changes

## Testing Strategy

- **unit tests**: a two-thread stress test over `EnvelopeLine` + `TrendPenState` in
  `SemiPlot.Tests.Unit`, plain `[Fact]`, bounded by frame count and a cancellation token
- the existing `EnvelopePathTests`, `TrendChartViewModelTests` and `ChartGapRenderTests` pin that
  geometry, mutation semantics and pixels are unchanged
- no e2e tests exist for this path; the manual drag is in Post-Completion

## Acceptance Evidence

Reproduction today: the stress test below, run on the tree before Task 2, fails with
`ArgumentOutOfRangeException` from `Build` on the render task. The test also calls
`GetAxisLimits` directly as added pressure; an `InvalidOperationException` (collection modified)
from that call is the same race, not the production path. Task 1 records the failing output in this
file. A stress test that stays green on the unfixed code is not evidence; Task 1 widens the window
once, and if it cannot be made red within the stated bounds the plan stops there.

Red run recorded 2026-09-09 (Task 1), on the unfixed tree, three consecutive runs of
`dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter
"FullyQualifiedName~EnvelopeLineTests"`. Every run failed identically, at rendered frame 0 (the
first frame of the render task), with no widening needed:

```
System.ArgumentOutOfRangeException: Index was out of range. Must be non-negative and less than
the size of the collection. (Parameter 'index')
   at System.Collections.Generic.List`1.get_Item(Int32 index)
   at SemiPlot.UI.Chart.EnvelopePath.Build(...) in SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:line 43
   at SemiPlot.UI.Chart.EnvelopeLine.Render(RenderPack rp) in SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:line 70
   at ScottPlot.Rendering.RenderActions.RenderPlottables.Render(RenderPack rp)
   at ScottPlot.Plot.GetImage(Int32 width, Int32 height)
```

`EnvelopePath.cs:43` is the `columns[index]` read named in issue #59, reached from `Render` and not
from `GetAxisLimits`, so the failure is the production path.

Negative control recorded 2026-09-09 (Task 2). With the lock removed from `Render` only and left in
place everywhere else, `EnvelopeLineTests` failed at rendered frame 11 with the same
`ArgumentOutOfRangeException` from `EnvelopePath.Build` at `EnvelopePath.cs:43`, reached from
`EnvelopeLine.Render`. Restoring the lock turned the same test green again, and it then passed three
consecutive runs. The test therefore still detects the race it was written for; the intermediate
state was never committed.

Both negative controls re-run 2026-09-09 after the review fixes, on the strengthened test (the frame
budget is now asserted, the mutation loop also calls `FoldRealtime` and the envelopes carry NaN gap
columns). With the lock removed from `Render` only, the test failed at rendered frame 22 with
`ArgumentOutOfRangeException` from `EnvelopePath.Build` at `EnvelopePath.cs:43`. With the lock removed
from `GetAxisLimits` only and `Render` left locked, it failed at rendered frame 130 with
`ArgumentOutOfRangeException` from `EnvelopeLine.GetAxisLimits` itself, at the `Columns[0]` /
`Columns[^1]` read. Each lock was restored immediately; neither intermediate state was committed.

Green run recorded 2026-09-09 (Task 4), on the fixed tree: `dotnet build SemiPlot.slnx` clean (0
warnings, 0 errors) and `dotnet format SemiPlot.slnx --verify-no-changes` exit 0.
`SemiPlot.Tests.Unit` passed 778 of 778 (777 before this plan, plus `EnvelopeLineTests`);
`SemiPlot.Tests.Integration` passed 85 of 85 against a reachable Docker daemon (29.7.2). No test
skipped in either project. `EnvelopeLineTests` alone passes three consecutive runs, reaching its full
frame budget each time. The manual smoke, a two-minute drag near the Raw/Minute boundary against the
bench, is in Post-Completion because nothing in the suite can drive Avalonia's real render loop.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with `+` prefix
- document issues/blockers with `!` prefix

## Solution Overview

`EnvelopeLine` creates and owns the column list and the lock, both private:

```csharp
private readonly List<EnvelopeColumn> _columns = [];
private readonly Lock _columnsLock = new();
```

Every mutation is a method on the plottable that takes that lock: `ReplaceColumns`, `ClearColumns`,
`AppendColumn` and `FoldIntoLastColumn`. `TrendPenState` takes `(Pen pen, EnvelopeLine line)` and calls
them; it holds neither list nor lock. Tests read the buffer through `internal IReadOnlyList<EnvelopeColumn>
Columns` on the plottable. `EnvelopeLine.Render` takes the lock across `VisibleRange` plus `Build`, and
`GetAxisLimits` takes it for its whole walk. The `SKPath` loop and `Drawing.DrawLines` stay outside
the lock: they read `_pathPoints`, which only the render thread touches.

Why the plottable owns both: a lock and the list it guards must be unseparable. With the list as a
separate constructor argument, a mismatched pair (a list the writers lock but the line never reads)
would fail silently, with the render thread reading unguarded. Owning both in one place makes the
mismatch unstateable; the constructor loses a parameter, the `MUST be the exact instance` comment is
deleted, and the three construction sites shrink. The list stays private for the same reason: a writer
outside the plottable cannot reach it, so an unguarded write does not compile.

Why every buffer access takes the lock: one lock per method, from the first read of the list to the
last write, is one rule instead of a per-method argument about which read is safe. `AppendColumn`'s
last-X check and `FoldIntoLastColumn`'s last-column read therefore run inside the lock with the write they
precede. `LastNonGapCenter` walks the pen state's local list, which the render thread never sees, so
it needs no lock; `CurrentValue` stays outside every lock, and so does the conversion `LoadHistory`
runs into that local list.

Why `Build` gets no clamp: with the lock in place a range wider than the list is unreachable, and a
clamp would turn a future locking mistake into a silently truncated line instead of a throw.

## Technical Details

Lock scope per mutator, all in `EnvelopeLine`:

| Mutator | Under the lock | Left to the caller, outside it |
| --- | --- | --- |
| `ReplaceColumns` | `Clear` + `AddRange` of the converted columns | the conversion loop, `LastNonGapCenter`, `CurrentValue` |
| `ClearColumns` | `Clear` | `CurrentValue = null` |
| `AppendColumn` | the last-X check, `Add`, the cap trim | `CurrentValue` |
| `FoldIntoLastColumn` | the last-column read, the NaN check, the indexer write | `CurrentValue` |

`LoadHistory` converts first and swaps under the lock so the render thread waits for one
`AddRange`, not for the per-column `LocalTimeAxis.ToAxis` loop. It converts into a list local to the call,
sized to the envelope: one allocation per load, on the debounced history path and not on a hot one.
`CurrentValue` stays outside every lock because `RaiseAndSetIfChanged` runs binding code of arbitrary
cost.

Render thread, in `EnvelopeLine`:

```csharp
lock (_columnsLock)
{
	var (first, lastExclusive) = EnvelopePath.VisibleRange(_columns, Axes.XAxis.Min, Axes.XAxis.Max);
	EnvelopePath.Build(_columns, first, lastExclusive, PenLineStyle, _pathPoints);
}
```

`GetAxisLimits` wraps its existing body. The only code that runs under the lock is `EnvelopePath`'s
pure index math, `List<T>` mutation and ScottPlot axis-limit reads; none takes a lock or raises an
event, so no lock is ever nested and no deadlock is possible. Uncontended cost is tens of nanoseconds
per frame; contention happens only when a history load lands during a frame, and then the frame waits
for one `AddRange` of at most a few thousand structs.

The comment naming the threads sits on the list: written on the UI thread only, through
`TrendPenState`'s calls into the mutators; read on the render thread in `Render` and `GetAxisLimits`;
every access under `_columnsLock`.

## Implementation Steps

### Task 1: Stress test that fails on the unfixed code

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`

- [x] create `EnvelopeLineTests` with the three traits (`Component=UI`, `Area=Chart`,
      `Category=Unit`); build a pen state through the same triple `ChartHoverReadoutTests.cs:104-107`
      uses, a headless `Plot` with the line added and `line.Axes.XAxis = plot.Axes.Bottom`, X limits
      set over the data
- [x] `Render_WhileTheUiThreadRewritesTheColumns_DoesNotThrow`: one `Task` renders through
      `plot.GetImage(400, 300)` in a loop and calls `line.GetAxisLimits()` each iteration, up to a
      frame budget of 300 frames (about one second on the green path), which the test asserts it
      reached; the test thread alternates `LoadHistory` with a long envelope (6000 columns, every
      500th a NaN gap), `LoadHistory` with a short one (2000), `AppendRealtime`, `FoldRealtime` and
      `ClearHistory`
- [x] termination: one `CancellationTokenSource` bounding the run, `[Fact(Timeout = 120_000)]`
      bounding the test and `WaitAsync` bounding the join, so a thread stuck inside `lock` cannot hang
      the executable; both loops check the token; the mutation loop also exits when
      `renderTask.IsCompleted`; the test cancels and joins the render task in a `finally`, so a throw
      at frame five on the unfixed code ends the test instead of hanging it
- [x] the render task's exception is the assertion: awaiting it must not throw
- [x] run the test on the unfixed tree three times; it must fail with `ArgumentOutOfRangeException`
      or `InvalidOperationException`. If it stays green, double the long envelope size and the frame
      budget once, re-run three times; if it is still green, stop and record it here as `!` before
      touching Task 2
- [x] record the failing output (exception type, frame at which it threw) in this file under
      Acceptance Evidence

### Task 2: Lock the column buffer between the two threads

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHoverReadoutTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] `EnvelopeLine`: drop the constructor parameter; add a private `List<EnvelopeColumn>` and a
      private `Lock` with the one-line comment naming the two threads and the single-writer rule, the
      four guarded mutators (`ReplaceColumns`, `ClearColumns`, `AppendColumn`, `FoldIntoLastColumn`)
      and `internal IReadOnlyList<EnvelopeColumn> Columns` as the read surface
- [x] take the lock in `Render` across `VisibleRange` plus `Build`, and in `GetAxisLimits` around
      the whole body; leave the `SKPath` loop outside
- [x] `TrendPenState`: constructor becomes `(Pen pen, EnvelopeLine line)`, `_columns` field goes,
      the writers call the plottable's mutators per the scope table in Technical Details; `LoadHistory`
      converts into a local list first and hands it to `ReplaceColumns`; the `Columns` pass-through
      goes and the tests read `state.Line.Columns`; delete the constructor comment at `:13-14`
- [x] update `BuildPenState` (`TrendChartViewModel.cs:365-375`) and the two test builders to the
      new pair; `EnvelopeLineTests` builds its state the same way
- [x] run `EnvelopeLineTests` three times: green each time; run `EnvelopePathTests`,
      `TrendChartViewModelTests`, `ChartGapRenderTests`, `ChartHoverReadoutTests`,
      `TrendLegendViewModelTests`: green
- [x] negative control: remove the `lock` from `Render` only, run `EnvelopeLineTests`, see it red,
      restore the lock, see it green; record both outcomes here
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over every touched
      `.cs` file exit 0

### Task 3: Record the threading contract in the architecture docs

**Files:**
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/testing-strategy.md`

- [x] `charting.md`, section "Per-pen plottable: `EnvelopeLine`" (`:10-16`): one paragraph stating
      that the plottable owns the column list and its lock, both private, `TrendPenState` writes on
      the UI thread only through the guarded mutators, `Render` and `GetAxisLimits` read on the render
      thread under the same lock, and `Render` holds it only for the visible-column read
- [x] `charting.md:48-49` ("owns one pen's `EnvelopeLine` plus the column buffer", "re-reads that
      list on every render"): reword so ownership and the lock agree with the new paragraph
- [x] `charting.md:122-125` file list: `EnvelopeLine` owns the buffer, `TrendPenState` the
      history-load / realtime-append / fold logic
- [x] `testing-strategy.md`: one sentence for `EnvelopeLineTests` next to `ChartGapRenderTests`
      (`:87`), stating what it pins: two threads, no throw, bounded by a token
- [x] gate: `grep -n "column buffer\|re-reads that list\|the lock" docs/architecture/charting.md`
      shows every mention agreeing with the code; no sentence restates the code line by line

### Task 4: Verify acceptance criteria

**Files:**
- Modify: `docs/plans/20260909-envelope-render-lock.md`

- [x] `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: all green, count
      recorded here
- [x] `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` when
      Docker is up (85 tests before this plan); record skipped if it is not
- [x] `dotnet build SemiPlot.slnx` clean, `dotnet format SemiPlot.slnx --verify-no-changes` exit 0
- [x] the Acceptance Evidence section carries the red output from Task 1, the negative control from
      Task 2 and the green run

### Task 5: Update documentation and close out

**Files:**
- Modify: `README.md` (expected: no change)
- Modify: `CLAUDE.md` (expected: no change)
- Move: `docs/plans/20260909-envelope-render-lock.md` to `docs/plans/completed/`

- [x] `README.md` and `CLAUDE.md`: no change needed. Neither names `EnvelopeLine`, `TrendPenState`,
      the column buffer or the render thread, so the constructor change and the buffer ownership make
      no sentence false; the threading contract belongs in `docs/architecture/charting.md:49-50`,
      which Task 3 wrote, and `CLAUDE.md` states that specifics do not go in it
- [x] ! PR body ends with `Closes #59` above the session URL: recorded here, not yet written; the PR
      itself is delivery work outside this run and needs no further code change
- [x] ! move this plan to `docs/plans/completed/`: deferred to the ship step, delivery work outside
      this run

## Post-Completion

**Manual verification:**

1. Start the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`).
2. Drag the timeline back and forth across the Raw/Minute boundary for two minutes. Before the fix
   the viewer died within about a minute (issue #59, reproduced 2026-09-09).
3. Expected: no crash, the line redraws after each history load, the realtime edge keeps appending.
4. Zoom to a day and back to seconds while dragging, so the buffer cap trim and the fold path run.

**Follow-ups found in review, out of this plan's scope:**

1. `SemiPlot.UI/Chart/ChartAxisBinder.cs:70` writes `state.Line.Axes.YAxis` on the UI thread while
   `Render` reads `Axes` on the render thread under no lock. A reference swap cannot throw, but a frame
   can stroke against a half-swapped axis pair.
2. `SemiPlot.UI/Chart/TrendChartViewModel.cs:248` calls `Plot.Remove(state.Line)`, mutating ScottPlot's
   `PlottableList` on the UI thread. Latent only because `RemovePen` has no production caller.

Both want an issue of their own; neither is touched here.

**External system updates:** none.

**Executed by exec:**
- branch: envelope-render-lock

## Verify it yourself

1. The race, reproduced by the test. On `master` (any commit up to `9ce10bd`) the stress test does not
   exist; on `20ca5e9` (the test alone, no lock) it fails at rendered frame 0 with
   `ArgumentOutOfRangeException` from `EnvelopePath.cs:43`; from `8096db6` on it passes. Run on the
   branch head:

   ```
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~EnvelopeLineTests"
   ```

   Expected: 1 passed, about one second, 300 frames rendered against a mutating buffer.

2. The test detects a missing lock. Delete the `lock (_columnsLock)` line and its braces around
   `VisibleRange` plus `Build` in `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs` `Render`, run the
   command above: red within a few dozen frames with the same exception. Restore with
   `git checkout -- SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`. The same experiment on the
   `GetAxisLimits` lock alone went red at frame 130 on 2026-09-09.

3. Nothing else moved. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` is
   778 passed (777 before the branch plus this test); with Docker up,
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` is 85 passed.
   `dotnet build SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` are clean.

4. The crash itself, by hand. Start the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), drag
   the timeline back and forth across the Raw/Minute boundary for two minutes. On `master` the viewer
   died within about a minute on 2026-09-09; on the branch it keeps drawing. Nothing in the suite drives
   Avalonia's real render loop, so this step is the only check of the shipped thread pairing.
