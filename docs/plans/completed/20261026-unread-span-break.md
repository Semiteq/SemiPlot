# Unread span break

## Overview

- Issue #120: a pen draws one straight segment across a span the chart never read. With follow off and the window
  in the past, a history read keeps only the columns up to the right edge of the span it drew. The next live sample
  then joins the band's last column, and the line runs across the unread span until a later read covers it.
- Fix: a history apply whose drawn span ends before the chart's newest sample marks that span's end as the start of
  an unread span. The first live sample past the mark opens behind a gap column, both when it appends a column and
  when it folds into the last one at a coarse layer. The span draws as a break until a read fills it.
- In follow mode a read's span ends one window width past the window end, so no mark is set as long as the read
  lands within one window width of being issued. A narrow Raw window whose read takes longer than its width gets a
  short, honest break at the live edge: the kept-tail rule already dropped the live columns past the span, and the
  next read refills it within half a window.
- The mark also gives #152 (hold a pen's last value to the time of the last successful poll) its boundary: the
  hold may run up to the span end, which the read covered, and stops at the gap.
- PR body carries `Closes #120`.

## Context (from discovery)

Verified on `master` `dae7954`, 2026-10-26.

- `EnvelopeLine.ReplaceColumns(columns, requestedEndX)` (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:195-214`)
  keeps the appended columns after the new history end and drops those past `requestedEndX`
  (`:200-203`). Nothing records that the span past `requestedEndX` is unread.
- `AppendColumn` (`:217-223`) and the fold path's `AppendAfterLastColumn` (`:271-286`) accept any X past the last
  column. `FoldIntoLastColumn` (`:226-252`) widens the last column unless it is a gap (`:237-241`), so at a coarse
  layer a live sample past the unread span folds into the band's last column and moves its `Center`.
- `EnvelopePath.Build` (`SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:31-76`) joins consecutive non-gap columns; a
  gap column resets the segment (`:46-50`).
- `TrendPenState.LoadHistory` and `ClearHistory` (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:56-75`) pass the
  read's `requestedToUtc` to `ReplaceHistory` (`:105-109`). `AppendRealtime` and `FoldRealtime` (`:77-92`) are the
  live paths; `ChartRealtimeApplier.ApplyBatch` (`SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs:19-48`)
  chooses between them per layer.
- The callers are `TrendChartViewModel` at `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:331` (ClearHistory),
  `:695` (LoadHistory) and `:751` (ClearHistory).
- At `:680` `EnvelopeSplice.Over` returns the drawn envelope and its `ReadSpan`. A margin-less read inside the span
  in hand keeps the in-hand columns after it and extends the span to `spanInHand.ToUtc`
  (`SemiPlot/SemiPlot.UI/Chart/EnvelopeSplice.cs:12-32`), so the drawn envelope can run past `request.ToUtc`. The
  span is the edge the mark uses; `request.ToUtc` would put the mark inside the drawn band.
- `ChartNavigationController.NewestSample` (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:46-47`) is
  the newest sample the chart has seen, from the archive or the live edge, null until one arrives.
- The behaviour is described in `docs/architecture/charting.md:110-148` ("Realtime append / live-edge join" and the
  kept-tail rule).

## Development Approach

- **testing approach**: code first. Tasks 1 and 2 carry no tests; the stand check in Task 3 gates them because the
  defect does not reproduce reliably on the stand. Task 4 adds the tests once the stand confirms the fix.
- complete each task fully before moving to the next
- run `dotnet build SemiPlot.slnx` and the unit suite after each code task; the existing tests must stay green

## Testing Strategy

- unit tests in `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`, `TrendPenStateTests.cs` and
  `TrendChartViewModelTests.cs` (Task 4)
- no UI e2e suite exists; the stand check is manual

## Acceptance Evidence

Manual, on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. Open the viewer; the window follows the live edge. At windows wider than one read's latency no break appears at
   the live edge while it follows.
2. Turn follow off and pan the window an hour into the past; wait for the history read.
3. Drag forward in steps of half a window to one window past the band's right edge. Before the next read lands, no
   pen draws a horizontal or diagonal segment from the band's last value to the live samples; the span shows a
   break.
4. After the read lands, the span fills with archive data.
5. Repeat steps 2 to 4 at a window wide enough for the minute layer: the band's last column keeps its value; no
   live value folds into it.
6. Jump back to now and follow again: once the read lands, the live line runs without breaks.

Automated, Task 4, red on `master` and green after the fix:

```
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~EnvelopeLineTests|FullyQualifiedName~TrendPenStateTests|FullyQualifiedName~TrendChartViewModelTests"
```

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep this plan in sync with the work

## Solution Overview

- `EnvelopeLine` holds two more values under its lock: the X where the unread span starts, or none, and the X of
  the gap it inserted for that span, or none. `ReplaceColumns` sets the first from its caller's decision.
- The decision belongs to `TrendPenState`, which already converts the read's right edge: the span past the drawn
  span's end is unread when that end is earlier than the chart's newest sample, which the caller passes from
  `ChartNavigationController.NewestSample`. A null newest sample sets no mark.
- `TrendChartViewModel` passes the `ReadSpan.ToUtc` that `EnvelopeSplice.Over` returns as the edge, at the history
  apply. The two `ClearHistory` sites keep their own edge. The kept-tail cutoff moves to the span end with it;
  nothing changes in practice, because the replace already drops every live column after the spliced end.
- The first live column past the mark gets a gap column ahead of it, and the mark clears. The gap sits at the mark
  when the mark lies after the last column; when the last column sits exactly on the mark, it sits halfway between
  the last column and the new one. A line with no columns, or one whose last column is already a gap, gets no extra
  gap.
- The fold path applies the same rule: a sample past the mark does not fold into the last column but opens a new
  column behind the gap, as it does after a gap today.
- A later replace whose edge reaches the inserted gap removes it: that read covered the span, so a held line there
  is correct. A live `NaN` break cannot be told apart from the inserted gap by its values, so the line keeps the
  inserted gap's X.

## Technical Details

- `EnvelopeLine`: fields `_unreadFromX` and `_unreadGapX` (`double`, `NaN` for none) under `_renderStateLock`.
- `ReplaceColumns(columns, requestedEndX, bool isUnreadPastEnd)`: when `_unreadGapX` is set and at or before
  `requestedEndX`, it removes that column from the kept tail and clears `_unreadGapX`; then it sets
  `_unreadFromX = isUnreadPastEnd ? requestedEndX : NaN`.
- `AppendAfterLastColumn(column)`: before the add, when `_unreadFromX` is set, the line has a last column that is
  not a gap, the last column's X is at or before `_unreadFromX` and `column.X > _unreadFromX`, it adds
  `new EnvelopeColumn(gapX, NaN, NaN, NaN)` and records `_unreadGapX = gapX`; any accepted column past the mark
  clears `_unreadFromX`.
- `FoldIntoLastColumn(sample)`: the same condition routes to `AppendAfterLastColumn`, as the gap case does.
- `TrendPenState.LoadHistory(envelope, requestedToUtc, newestSampleUtc)` and `ClearHistory(requestedToUtc,
  newestSampleUtc)`; `ReplaceHistory` computes `isUnreadPastEnd = newestSampleUtc is { } newest && requestedToUtc <
  newest`.
- `TrendChartViewModel`: `:695` passes `span.ToUtc` and `Navigation.NewestSample`; `:331` and `:751` pass their
  current edge and `Navigation.NewestSample`.

## What Goes Where

- **Implementation Steps**: the code, the stand gate, the tests and the docs.
- **Post-Completion**: #152 builds on the mark.

## Implementation Steps

### Task 1: Mark the unread span in EnvelopeLine

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs` (24 `ReplaceColumns` call sites take the new
  argument)

- [x] add `_unreadFromX`, `_unreadGapX` and the `isUnreadPastEnd` parameter to `ReplaceColumns`
- [x] remove a covered inserted gap in `ReplaceColumns`
- [x] insert the gap column in `AppendAfterLastColumn` under the rule in Technical Details, and clear the mark
- [x] route a fold past the mark through `AppendAfterLastColumn` in `FoldIntoLastColumn`
- [x] build and run the unit suite; existing tests stay green

### Task 2: Pass the drawn span's end and the newest sample to the history apply

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify, the call sites the new parameter breaks, all under `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/`:
  `TrendPenStateTests.cs` (7), `ChartViewTestBuilder.cs` (2), `ChartGapRenderTests.cs`,
  `ChartRealtimeApplierTests.cs`, `ChartTestBuilder.cs`, `LogAxisRenderTests.cs`, `RenderedRise.cs`,
  `TrendChartRenderThreadTests.cs`, `TrendChartViewModelTests.cs` (1 each)

- [x] add `newestSampleUtc` to `LoadHistory`, `ClearHistory` and `ReplaceHistory`, and compute `isUnreadPastEnd`
- [x] pass `span.ToUtc` at `TrendChartViewModel.cs:695`, and `Navigation.NewestSample` there and at `:331` and `:751`
- [x] build and run the unit suite; existing tests stay green

### Task 3: Operator checks the fix on the stand

- [x] STOP: hand the branch to the operator for Acceptance Evidence steps 1 to 6; continue only when the stand shows
  the break and no false break while following

### Task 4: Cover the unread span with tests

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] `EnvelopeLineTests`: a replace marked unread followed by an append past the edge; `EnvelopePath.Build` starts a
  new segment at the live column and draws nothing across the span
- [x] `EnvelopeLineTests`: the same with a fold past the edge; the band's last column keeps its `Center`, `Min` and
  `Max`
- [x] `EnvelopeLineTests`: a replace not marked unread followed by appends past the edge inserts no gap
- [x] `EnvelopeLineTests`: last column exactly on the edge; the gap lands between it and the new column
- [x] `EnvelopeLineTests`: an inserted gap, then a replace whose edge passes it and whose last column lies before it;
  the gap is gone and the kept live column joins the band
- [x] `TrendPenStateTests`: `LoadHistory` with an edge before the newest sample marks the span; after it, or with a
  null newest sample, does not
- [x] `TrendChartViewModelTests`: an `Expand` read in the past, then a margin-less read inside its span, then a live
  append; the drawn line breaks after the spliced end and does not join it to the live column
- [x] confirm the new cases fail with Tasks 1 and 2 stashed, then pass with them
- [x] run the unit suite

### Task 5: Verify acceptance criteria

- [x] verify every point of the Overview
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot.slnx`
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files

### Task 6: [Final] Update documentation

- [x] `docs/architecture/charting.md`: describe the unread mark, its removal by a covering read, and the span end as
  the kept-tail cutoff in "Realtime append / live-edge join" and the kept-tail rule (`:110-148`)
- [x] move this plan to `docs/plans/completed/`

## Post-Completion

- #152 holds a pen's last value up to the time of the last successful poll; the hold stops at the gap this plan
  writes and may run up to the drawn span's end.
