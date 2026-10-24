# Charting / Trend Viewer

## Renderer: ScottPlot 5

The chart is rendered with **ScottPlot 5** (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — a
native Avalonia control (`AvaPlot`), no web view. It was chosen over OxyPlot for built-in
independent multi-axis; the prior uPlot/WebView2 stack is removed. The surrounding UI (legend,
navigation bar, axes UX, theming) is ours; ScottPlot is only the plotting core.

### Per-pen plottable: `EnvelopeLine`

Each pen is drawn as **one plottable of our own** over a data-layer-decimated min/max envelope
(`PenHistoryEnvelope`: ascending `Timestamps` + `Min` + `Max` + `Center`). `Chart/EnvelopeLine` is an
`IPlottable` owning a `List<EnvelopeColumn>` (`X`, `Min`, `Max`, `Center`, `Break`) and strokes one polyline
through every visible column's `Min` and `Max`: no fill, no band, no markers.

`SignalXY` was rejected: it cannot express per-pen stepping plus NaN gaps, and its built-in
decimation is unused because the data layer pre-decimates. `DataLogger` is cited prior art only
(it cannot carry a pre-decimated min/max band), **not** the implementation pattern.

**Why one polyline instead of `Scatter` + `FillY`.** The pair it replaced walked every coordinate twice
per frame: `FillY` is a `Polygon` building an `SKPath` of `2 x columns + 1` vertices, and `Scatter` walked
all points again through `Drawing.DrawMarkers` even at `MarkerStyle.None`. Neither culled to the viewport,
and the prefetch margin (`HistoryPrefetch.Expand`, one window width on each side) makes every pen's buffer
three visible windows wide, so a frame cost three times what it drew. Measured on the bench stand on 2026-09-07 with
8 pens and 2048 columns, `RenderOnce` averaged 62 ms (max 386 ms) with the fill anti-aliased and 4.8 ms
(max 34 ms) with it off. Drawing the min/max as a line rather than a fill also keeps a spike inside a
column visible, which a centre-only line hides.

**Viewport culling.** `EnvelopePath.VisibleRange` binary-searches the ascending `X` for the columns inside
the `Axes.XAxis` range plus one column beyond each edge, so a segment entering the viewport is drawn and
the buffer outside it is never walked.

**Path shape.** `EnvelopePath.Build` turns the visible columns into the point sequence `Render` strokes: a
column is entered at whichever of `Min`/`Max` is nearer the previous point's Y and left at the other, which
keeps the crossing between two columns short; a column whose `Min` equals its `Max` is one point; a stepped
pen holds the previous Y to the new column's X before the vertical move. It touches no Skia, so the culling
window, the gap break and the step shape are unit-tested (`EnvelopePathTests`).

**Hit distance.** `EnvelopeLine.DistanceTo(click, dataRect)` is the pixel distance from a point to the
nearest stroked segment. It reads the same `VisibleRange` and `Build` vertices as `Render`, under the same
lock, and projects them through the same `ProjectVertex`, log10 floor included. ScottPlot clips every
plottable to the data area, so a click outside `dataRect` measures infinity and each segment is clipped to
`dataRect` before it is measured (`EnvelopePath.ClippedSegmentDistance`): a line the chart does not draw
under the click is never picked. A `StartsSegment` vertex opens a new segment, so no distance is taken
across a gap. The held segment is part of the `Build` vertices (`#the-hold-to-the-live-edge`), so a click
on it measures 0 and picks its pen, and a click past the hold end measures to the hold's end point
(`EnvelopeLineTests.DistanceTo_AClickOnTheHeldSegment_IsZero`). `Chart/ChartPenPick.Choose` applies the
6 px tolerance and the tie rule to the distances of all visible pens (`trend-interaction.md`, "Navigation &
cursors").

**Active stroke.** The active pen's line is drawn above the others. It is 2 px wide while the view model's
emphasis flag is set and 1 px like the others while it is clear; the width follows the flag and the order
does not, so the active pen wins the shared pixels either way.
`ChartPenSet.RestyleActiveLine(activePenId, isEmphasized)` sets the widths through `EnvelopeLine.SetLineWidth`
(written under the column lock, read by `Render` with the colour) and calls `Plot.MoveToTop` on the active
line. `TrendChartViewModel.RestyleActivePen` passes the flag to it under `lock (Plot.Sync)` on every change of
the active pen or the flag: `SetActivePen`, a miss in `PickPenAt`, a visibility change and a catalogue apply
(inside its own `Plot.Sync` block). The call raises no notification inside the lock. The flag starts clear, so
a new window draws the active pen 1 px wide until the operator picks a pen. `SetActivePen` sets the flag for
every pen it accepts, and `DropEmphasis` clears it on a miss in `PickPenAt` (`trend-interaction.md`,
"Navigation & cursors"). `SettleActivePen` and `ActivateAVisiblePen` move the active slot without an operator
choice and keep the flag, so a thin line stays thin on the pen that takes the slot and a thick one stays thick,
except after a catalogue read that leaves no pen: `ApplyCatalogue` then clears the flag, so the first pen a
later read seats starts thin, as in a new window.

**Gaps via NaN.** A column carrying `NaN` in `Min`/`Max` is the envelope's gap marker: `Build` breaks the
path there and the next real column opens a new segment with a `MoveTo`. A `NaN` column comes from one of
three routes. The history path writes one after a bucket whose `breaks` flag a `q = 32` marker or a null
`v` sets (`data-integration.md#quality-and-gaps`). The live edge writes one for a break the live source
reported: `TrendPenState.AppendRealtime` encodes a null value as a `NaN` column and keeps `CurrentValue` at
the last real value. The chart writes one itself 1 ms ahead of a pen's first live value when that value
lies past the pen's last column (Realtime append / live-edge join, below).
Every column carries a `BreakKind Break`, `Ordinary` by default, which means something on a gap column
only. The chart's ahead-break is `BreakKind.Ahead`; every gap the history or the source writes, and the
chart's own gap after a source break, is `Ordinary` (The break ahead of a first live sample, below).
`Build` breaks the path at both kinds alike, and only the shade reads the kind (`#the-ahead-break-shade`).
The kind rides on the column, so it travels through `ReplaceColumns`' kept tail, its drop once a read
covers the column, and the cap trim, with no second list to keep in step with the columns
(`EnvelopeLineTests.AnAheadBreakKeepsItsKindThroughAReplaceThatKeepsItAndAnAppendPastTheCap`). The
column is 40 bytes rather than 32, 0.8 MB more per pen at the 100 000-column cap.

**Per-pen stepping.** The line carries the Core `PenLineStyle` — `Stepped` for discrete/digital tags,
`Interpolated` for analog. `Chart/TrendPenState` sets it and the colour from the pen through
`EnvelopeLine.Restyle`, when it is built and on every `Revise`. Neither is readable back: tests prove the
style and the colour through rendered pixels (`RenderedRise`).
On a change-based archive the two styles draw almost the same line. Simple-Scada writes two rows per
change, the previous value at the last poll tick and the new value about 100 ms later
(`scada-archive.md#write-behavior`), so an `Interpolated` line over such rows already draws a step;
`Stepped` differs only over those 100 ms. The setting shows only on a variable archived without the
pair.

**Column buffer and its two threads.** The plottable owns the column list and the `Lock` that guards it, both
private. Every mutation goes through `ReplaceColumns`, `AppendColumn`, `AppendAfterBreak` or
`FoldIntoLastColumn`, each of which takes the lock, so no caller can reach the list without it.
`Chart/TrendPenState` is the only caller of those four and calls them on the UI thread; `Render` and
`GetAxisLimits` read on Avalonia's render thread under the same lock. `Render` holds it for the visible-column
read alone (`EnvelopePath.VisibleRange` plus `EnvelopePath.Build`) and strokes the resulting points outside
it, so a frame that meets a history load waits for the one merge that swaps the buffer, not for the conversion
ahead of it: `LoadHistory` converts into a list local to the call and hands it over in one `ReplaceColumns`,
which merges it with the live tail the list already holds. The last X the previous replace wrote, the hold's
three X values, the colour, the line style and the line width sit under the same lock. `TrendPenState` writes
the confirmed and the confirming X through `ConfirmLive` and the hold end through `SetHoldEnd` on the UI thread
(`#the-hold-to-the-live-edge`). `DistanceTo` and `ReadHeldSpan` read under the lock on the UI thread, and
`CollectAheadBreakSpans` on the render thread (`#the-ahead-break-shade`). A catalogue read can change the
colour and the line style after the first frame, so `Restyle` writes them on the UI thread and `Render` reads
them under the lock. The list is capped at 100 000 columns, and the cap trims in chunks of one tenth: the
append that passes it removes the oldest 10 000 columns in one `RemoveRange`. The list then lives between
90 000 and 100 000 columns, and the shift under the lock runs once per 10 000 appends instead of on every append.

**The plot's own lists and the render thread.** ScottPlot's `Plot.Render` holds `Plot.Sync` for the
whole frame, and the plottable and axis lists it walks are ScottPlot's own, so the column lock cannot
guard them. A catalogue read adds and removes plottables and adds Y axes after the window has shown,
so `TrendChartViewModel.ApplyCatalogue` runs its plot and axis edits under `lock (Plot.Sync)`, one
block per call, and moves the live edge, queries history and asks for the redraw after it. The lock
guards the plot's lists alone: the live-edge switch disposes the old subscription and opens a new one,
and a frame has no reason to wait for either. The block also runs under `DelayChangeNotifications()`, so `ActivePenId`, `DrawnPenId` and `ScalesRevision`
raise after the lock is released: a subscriber that closes the axis panel destroys a popup window, which waits
for the render thread, and the render thread waits for `Plot.Sync`. Without the lock a frame that
meets the edit throws "Collection was modified" on the render thread, the crash class of #59
(`TrendChartRenderThreadTests`).

**Realtime append / live-edge join.** The live edge comes from the SCADA's OPC UA server through
`ILiveSource` (`data-integration.md#realtime`); history stays on the archive `[DEC:opc-live-edge]`.
`TrendPenState` holds one pen's `EnvelopeLine` and appends into the
plottable's column list, which every render re-reads, so appends are live and nothing is re-set. The realtime
tail appends one degenerate column (`Min == Max == Center == value`) at the live edge. At coarse layers
(minute/hour) a realtime sample does **not** append; it folds into the current decimation column
(`FoldRealtime` widens that column's `Min`/`Max` and moves its `Center`). A live break is always appended,
at every layer: `ChartRealtimeApplier` hands a null value to `AppendRealtime` ahead of the fold-or-append
choice, because `Math.Min`/`Math.Max` with a `NaN` would turn the folded column into a gap and erase its
extremes. A gap column takes no fold: when the last column is a `NaN` gap, as after a history envelope
that ends on nulls or after a live break, the sample opens a column at its own timestamp past the gap, and
the samples after it fold into that one. The gap still draws as a break, and the line and `CurrentValue`
resume at the first value instead of waiting for the next read. A sample at or before the gap's X is
rejected, like any append at or before the last X. An empty line takes the first live value behind its
ahead-break at every layer (below), so a pen with no history draws its live run before any history read
lands; the next read replaces that span up to its new last column, and the live columns past it stay
under the kept-tail rule below. Cursor and legend read the `Center` channel consistently across the
seam. The fold follows the
layer in view, not the layer the columns were read at: until a coarse read lands, the columns are still
Raw, and the fold over them opens the history gate so the return to Raw reads the folded span again
(`data-integration.md#what-one-history-query-covers`).

**The break ahead of a first live sample.** The chart decides it, not the source
(`data-integration.md#the-break-ahead-of-a-first-live-sample`): the source carries no chart state, and
`TrendCoordinator.SetPens` restarts its subscription on every pen-set change, so a break the source wrote
ahead of each subscription's first sample would draw a gap after every catalogue change. `TrendPenState`
keeps the line's join to the live stream in one field of a private enum,
`LiveJoin { Unjoined, UnjoinedBySourceBreak, ReconfirmedAfterSourceBreak, Joined }`. A new
`TrendPenState` starts `Unjoined`, so the window's first live value of every pen, and the first value of a
pen a catalogue read adds, are first values. A live value that reaches a line that is not `Joined` takes
one of two routes:

- At or before the line's last column the line already covers that moment, as when the archive already
  holds a slow pen's last change. `AppendRealtime` rejects the value at `Raw`, and `FoldRealtime` folds it
  into the last column at a coarse layer, unless that column is a gap or the line is unjoined by a source
  break (`UnjoinedBySourceBreak` or `ReconfirmedAfterSourceBreak`); either rejects it.
- Past the last column, or on an empty line, `EnvelopeLine.AppendAfterBreak` appends a `NaN` column 1 ms
  ahead of the value, then the value. It writes no `NaN` column when the line already ends on a gap, or
  when 1 ms ahead of the value is not past the last column. 1 ms is the axis' smallest step:
  `LocalTimeAxis.ToAxis` is `ToOADate`, so a column one tick from the value would map onto its X.

After each live value the field moves under this table:

| State before | Value taken | Value rejected |
| --- | --- | --- |
| `Unjoined` | `Joined`; past the last column it opens behind an `Ahead` break, at or before it a coarse layer folds it | `Joined` |
| `UnjoinedBySourceBreak` | `Joined`; only past the last column, behind an `Ordinary` break | `ReconfirmedAfterSourceBreak` |
| `ReconfirmedAfterSourceBreak` | `Joined`; only past the last column, behind an `Ordinary` break | stays |
| `Joined` | stays; appended at `Raw` or folded at a coarse layer, with no break | stays |

Two events unjoin the line. A null from the source sets `UnjoinedBySourceBreak`, whether the line takes
the `NaN` column or rejects it: a pen whose live values all lay inside the archive's span would otherwise
draw a straight line across the outage at the next session's first value. A rejected value then moves it
to `ReconfirmedAfterSourceBreak` and not to `Joined`, at every layer: in these two states a coarse layer
rejects a value at or before the last column rather than fold it into the bucket the outage cut, which
would join the line and fold the next change into that bucket across the outage. A recreated session
re-delivers an unchanged pen's
value at its original stamp, before the break (`data-integration.md#the-breaks-the-source-writes`), and
the line rejects that replay. The server is stating the pen's current value, so the line is held again
(`#the-hold-to-the-live-edge`), but the next value past the last column still opens behind an `Ordinary`
`NaN` column, so no line crosses the outage. A history apply whose `ReplaceColumns` lost a live value past
the read's requested end sets `Unjoined`, because the live run it held is gone and the read does not reach
past its end; `ReplaceColumns` returns whether it lost one. It loses one when it drops a live column past
that end, and at a coarse layer when the read replaces the bucket the newest live value folded into, a
live bucket or an archive bucket, while that value's own stamp lies past both the read's requested end
and its last row (`#the-hold-to-the-live-edge`).
The latest unjoin wins: a source
break followed by a dropping apply gives `Unjoined`, because the live run the outage cut is gone and the
span ahead of the next value is one the archive had not reached. A sticky window's read keeps its right
margin past the live edge, so following the edge never takes this branch. A pen-set change touches no
`TrendPenState`: a pen the catalogue keeps stays joined, and its new subscription's first value, which
carries the stamp of its last change, lies at or before the line's last column and is rejected as any
such append. `CurrentValue` moves only when the value was taken.

**The kind of a chart-written break.** `TrendPenState.AheadBreak` writes `BreakKind.Ordinary` while the
line is `UnjoinedBySourceBreak` or `ReconfirmedAfterSourceBreak`, and `BreakKind.Ahead` otherwise. A
`NaN` column written in either of the first two states stands in for a source break the line rejected:
it covers an outage, so it keeps the ordinary look. Every other chart-written break opens the span
between the archive's newest row and the pen's first live value, at window open, on an empty line or after
a dropping apply such as a pan into the past and back, and that span is shaded grey on the drawn pen
(`#the-ahead-break-shade`).

`TrendPenStateTests` pins the rule (`Realtime_AFirstValuePastTheLastColumn_OpensOneMillisecondBehindANaNColumn`,
`Realtime_AFirstValueOnALineEndingOnAGap_TakesNoSecondNaNColumn`,
`Realtime_ARejectedBreak_StillOpensTheNextValuePastTheLastColumnBehindABreak`,
`Realtime_AfterAHistoryApply_TheNextValueOpensBehindABreakOnlyWhenTheApplyDroppedALiveColumn`,
`Realtime_ARejectedReplayAfterARejectedBreak_StillOpensTheNextValueBehindAnOrdinaryBreak`,
`FoldRealtime_AReplayAfterARejectedBreak_IsRejectedAndTheNextValueOpensBehindAnOrdinaryBreak`,
`Realtime_ASourceBreakThenADroppingHistoryApply_OpensTheNextValueBehindAnAheadBreak`), and
`TrendChartViewModelTests.ACatalogueThatAddsAPen_BreaksOnlyTheAddedPensFirstLiveValue` pins the pen-set
change.

At a coarse layer the first live value behind the ahead-break opens a column at its own X, and the values
after it fold into that column. `EnvelopeLine.FoldIntoLastColumn` widens `Min` and `Max` and moves `Center`
but keeps the column's X, so the live line resumes from the start of its first live bucket, not at the
newest value (`FoldRealtime_AFirstValueOpensAColumnBehindTheBreakAndTheNextOneWidensIt`). The grey span
ends at that bucket's X and does not move as the bucket widens
(`FoldRealtime_TheAheadBreakSpanEndsAtTheFirstLiveBucketsX`).

The source writes breaks of its own, and the chart appends each as any null
(`data-integration.md#the-breaks-the-source-writes`). When the session leaves `Connected`, every open
subscription emits one break per pen it has delivered a value for, 1 ms past that pen's newest delivered
`SourceTimestamp`, and none for a pen whose last emission is already a break. A notification whose status is
`Bad` emits one break; `Uncertain` and `Good` draw the value.

A history apply keeps the live tail past its snapshot. `EnvelopeLine.ReplaceColumns` takes the new
columns and the right edge of the range the read asked for. It keeps every appended column whose X is
after the new last column, all of them when the new list is empty, and drops those past the requested
edge; then it trims the merge to the cap from the oldest end. An appended column is one past the last X
the previous replace wrote, so only live appends survive and a previous band's history never does,
except where `EnvelopeSplice.Over` laid a margin-less read over the envelope in hand before the load
(`data-integration.md#what-one-history-query-covers`). A
pan into the past asks for a range that ends before the follow phase's tail, so the tail goes and the
band just read keeps the whole buffer. `TrendPenState.ClearHistory` is a replace with no columns, so a
requested pen absent from a result keeps its tail too. A live break column is a live append and follows the
same rule, the ahead-break included: `ReplaceColumns` keeps it while it lies past the new history end and
drops it once a read covers it. The gap ahead of a first live value, and its grey span with it, therefore
closes only when a history read reaches that value, and nothing closes it on its own; no read is issued
when the archive catches up.
A read that ends at a `q = 32` marker writes its anchor `NaN` one tick past the marker
(`data-integration.md#quality-and-gaps`); when a kept ahead-break follows it, the pen holds two adjacent
`NaN` columns, and `Build` draws one break across them. `LoadHistory` and `ClearHistory` both set
`CurrentValue` from the merged columns. An append at or before the last kept column is rejected,
as before any other last column; at a coarse layer the next fold widens the last kept column
(`data-integration.md#what-one-history-query-covers`). Only appended columns survive: a sample folded into
the last coarse column after the next coarse read's snapshot is lost with that column, and an extreme it
carried shows again only when a later read covers that span.

**Axes / shared-X invariant.** Each pen gets its own `IYAxis`, keyed on the pen id; no two pens ever
share one, whatever their unit or group. Every axis is a left axis — `Axes.Left` for the first pen,
`AddLeftAxis()` for the rest — because only the active pen's axis is drawn and alternating sides
would move it across the plot as the active pen changes. Non-active axes are
`IsVisible = false`, and the active-pen switch toggles visibility without rebuilding. `ChartAxisBinder`
fixes the drawn axis's panel at 56 px (`AxisPanelWidth`, as both `MinimumSize` and `MaximumSize`) and a
hidden axis's at 0, so the data area keeps its place when the active pen changes; a wider tick label
overflows the panel. Scaling is
driven per-axis via `SetLimitsY(min, max, axis)` from the Core `PenScaleModel` output (no global
`AutoScale`). Every plottable is pinned to `plot.Axes.Bottom` explicitly at creation, so all pens
share one X axis and per-pen axes are Y-only.

### The hold to the live edge

While the OPC UA session is up, each visible pen the live stream has confirmed is drawn held from its last
column's value to the chart's newest sample. A constant or rarely changing pen then reaches the edge
instead of stopping at a row that can be a day old. The hold stops at a break and is off while the session
is down. The hold is no column: `GetAxisLimits`, `ReplaceColumns`, the readouts' envelopes and the
autoscale never see it.

**The geometry.** `EnvelopePath.Hold(columns, confirmedX, holdEndX)` returns
`HeldSpan(FromX, ToX, Value)`, the last column's X, the hold end and the column's `Center`, when the last
column is a value (`Min`, `Max` and `Center` all numbers), its X is at or past `confirmedX`, and
`holdEndX` is a number past that X; otherwise it returns null. `Build` takes both X values and, when the
range reaches the last column, has drawn a point and `Hold` gives a span, adds `(lastX, Center)` when the
path's last Y differs and then `(holdEndX, Center)`: at most two points into the reused list, no
allocation, under the column lock. A tall last column steps from its exit to its `Center` first; a
degenerate one, every Raw live column and a Raw row whose `Min` equals its `Max`, needs only the horizontal
point, and a coarse live bucket a fold widened steps like a tall one. `VisibleRange` keeps one column
beyond each edge, so a last column left of the window is still held across it.
`EnvelopeLine.ReadHeldSpan()` returns the same span under the column lock. `EnvelopePathTests`
(`Build_AHeld...`, `Build_..._HoldsNothing`, `Hold_...`) and `EnvelopeLineTests` (`HeldSpan_...`,
`DistanceTo_...`) pin the shape, the span and the hit.

**The confirmed X.** For every live value that reaches the line, taken or not, `TrendPenState` calls
`EnvelopeLine.ConfirmLive(valueX)`, which keeps the smaller of the value's X and the last column's X after
the append or fold. A taken append confirms its own column, a fold at a coarse layer confirms the bucket
it widened, whose X is its opening X, and a rejected value confirms the moment it carries. The line holds
only while its last column is a value at or past the confirmed X. The rule is read under the column lock
at every frame, so a history apply writes nothing extra. The line also keeps the confirming value's own X,
uncapped: at a coarse layer a bucket keeps its opening X while later values fold into it, so the confirming
X lies past the confirmed X exactly when the newest value folded into a bucket that opens before it. That
bucket is the live bucket, or, once a read has covered the live bucket, the archive bucket a joined pen's
values fold into. A read can replace that bucket and still end before the value's stamp: a pan off the
edge reads with no right margin. When the value's X lies past both the read's requested end and its last
row, `ReplaceColumns` reports the value lost, as for a live column it drops past the requested end, and the
pen is unjoined and unheld until its next value
(`Hold_AtACoarseLayer_AReadCoveringTheLiveBucketButEndingBeforeItsNewestValue_EndsItUntilTheNextValue`,
`Hold_AtACoarseLayer_AReadEndingBeforeAValueFoldedIntoAnArchiveBucket_EndsItUntilTheNextValue`). A Raw
append or a rejected value confirms at its own X, so a read cutting back before it reports nothing: the
hold rule above ends the hold, and a read that brings the rows back restores it
(`Hold_AHistoryLoadCuttingBackBeforeTheConfirmedStamp_EndsItAndALoadBringingTheRowsBackRestoresIt`). So:

- an unserved pen, an unknown node or a non-numeric value, receives no live value, and its archive row is
  never drawn as live. A pen whose value turns non-numeric mid-session gets one break from the source, at
  that notification's stamp or the pen's newest delivered stamp, whichever is later
  (`data-integration.md#the-breaks-the-source-writes`), which ends its hold;
- a pen before its first live value is not held over the span it may still break;
- a constant pen joins on its initial notification, which the line rejects as inside the archive's span,
  and is held from its archive row. A pan read whose last column stays at or past the confirmed stamp
  keeps the hold, and a stamp older than every row keeps it through any read that returns a row after it.
  A read that cuts back before the stamp leaves no hold, so no hold draws an older value across a change,
  and the hold returns when a read brings the rows back;
- after a source break nothing is held until the server states the pen's value again: a rejected replay
  from a recreated session restores the hold (`ReconfirmedAfterSourceBreak`), a reactivated session that
  re-delivers nothing leaves the pen unheld until its next change
  (`data-integration.md#the-breaks-the-source-writes`), and a line that ends on the appended break holds
  nothing in either case;
- after a history apply that lost a live value, a dropped live column or a coarse read that ended before
  the newest folded value, the pen is not held until its next live value, because the chart then holds no
  live confirmation past the archive's newest row. A pen confirmed by a rejected value keeps its hold
  through the same cut, because its confirmation lies inside the archive's span; the asymmetry is a
  backlog entry (`docs/plans/backlog.md`).

**At a coarse layer** the hold starts at the last bucket's X, its opening X, and holds the newest value
folded into it. While the window follows the edge the live bucket never closes, so on a wide window the
newest value is drawn flat from the first live bucket's X to the edge, across the live values that differed
inside the bucket; the bucket's `Min` and `Max` still show their range at its X
(`Hold_AtACoarseLayer_StaysWhileValuesFoldIntoTheLastBucket`).

**The hold end.** `TrendPenState.ApplyHold` holds the one `Line.SetHoldEnd` call: the edge X while the
line is `Joined` or `ReconfirmedAfterSourceBreak`, `NaN` otherwise. `HoldTo(edgeX)` and a change of
`LiveJoin` call it, each change-guarded, so a value that changes nothing takes no lock. `Chart/ChartLiveHold`,
which `TrendChartViewModel` owns and disposes, is the one writer of the edge X. It keeps the session flag,
follows `Navigation.NewestSampleMoved`, and in `Apply()` hands every pen, through
`ChartPenSet.HoldLinesTo`, `ChartNavigationController.NewestSample` as an axis X while the session is up
and a sample exists, `NaN` otherwise. The newest sample is the newest stamp the chart has seen, from the
archive seed or a live batch (`trend-interaction.md#archive-overview-minimap`), and the sticky window's
right edge follows the same stamps, so a held pen reaches the right edge of a following window. The edge
follows samples and never the machine clock, and not the window's `To`, which a zoom moves without a
sample. A hold end at or before the pen's last column draws nothing.

The session flag comes from `TrendCoordinator.LiveSourceStates`, the source's `States` observed on the UI
scheduler, which replays the current state to each subscriber
(`data-integration.md#the-session-state-the-live-source-reports`). `TrendChartViewModel` subscribes it beside
its other coordinator streams; `OnLiveSourceState` hands `IsConnected` to `ChartLiveHold.SetSessionUp`, which
applies at once when the flag changes, and requests a redraw. `Connecting` and every fault hold nothing. The
constructor builds `_penSet` and `_liveHold` before that subscription, because under an immediate UI scheduler
the replay runs the handler inside `Subscribe`. `ApplyCatalogue` calls `Apply()` after its plot block, so a
pen a later read adds takes the current edge; a moved newest sample comes from a batch or a history apply,
each of which redraws already. `TrendChartLiveHoldTests` pins the wiring over the coordinator, and
`ChartLiveHoldRenderTests.AConfirmedSlowPen_IsDrawnHeldToTheNewestSample` the pixels.

**What a constant pen's hold shows.** The held value is the last column's `Center`, whatever the archive
wrote there, a row that repeats the previous value included (`scada-archive.md#write-behavior`). It is
the value `CurrentValue` holds whenever the line ends on a value, so the held line, the readouts inside the
held span and the sidebar agree; every `Hold_` case of `TrendPenStateTests` that draws a hold asserts
`ReadHeldSpan().Value == CurrentValue`.

**The readouts.** Inside the held span the cursor and hover readouts read `CurrentValue`.
`ChartCursorReader` reads a visible pen's history envelope first, and when that gives no value it reads
`TrendPenState.HeldValueAt`: the pen's `CurrentValue` when the cursor's axis X lies inside
`Line.ReadHeldSpan()`, else none. `ReadAt` and `ReadPenAt` share that read, so a marker reading in the
held span equals the hover value there. The live run between the envelope's end and the held span still
reads a dash, and a delta cursor in the held span reads no Δy, because both read the history envelope only
(`docs/plans/backlog.md`).

**The lock order.** `ChartMarkerReadings.Update` holds its readings lock while its `readAt`,
`ChartCursorReader.ReadPenAt`, reaches `ReadHeldSpan()` through `TrendPenState.HeldValueAt`, which takes
the line's lock: readings, then line. `ChartAheadBreakShade.Render` holds the shade's lock while
`CollectAheadBreakSpans` takes the line's: shade, then line. No path takes a line's lock and then either of
the two: `EnvelopeLine.Render` takes only its own, and `ChartMarkerReadings.Render` projects through
`PixelOf`, which takes none. A change that adds the reverse order can deadlock the UI thread against the
render thread.

### The ahead-break shade

The drawn pen's ahead-break spans are shaded grey under every pen line and labelled "No archive data when
the window opened" where the label fits (`ui-text.md#the-charts-text`). The grey promises no fill: the
span closes only where a history read covers it, as the ahead-break column does
(`#per-pen-plottable-envelopeline`).

**One plottable for the drawn pen.** `Chart/ChartAheadBreakShade` is one `IPlottable` per bound plot.
`TrendChartView.CreateChartOverlays` creates it beside `ChartMarkerReadings`, pins its `Axes.XAxis` to
`Plot.Axes.Bottom`, adds it under `lock (Plot.Sync)` and moves it to the bottom with `Plot.MoveToBottom`,
so every pen line draws over it; a data-context change removes it with the marker layer. `ServeFrame`
hands it the drawn pen's line on every frame (`ShowFor`): the line of `TrendChartViewModel.DrawnPenId`,
the active pen while it is shown, and none otherwise. Picking a pen moves the shade to that pen, as it
moves the axis and the marker readings, and every other pen's ahead-break draws as an ordinary gap.
Per-pen fills inside `EnvelopeLine.Render` would paint over the pens drawn before them, and many
translucent spans over nearly the same range would stack darker and want one label each; a union over
every visible pen would grey a range where most pens draw archive rows.

**The span.** `EnvelopePath.AheadBreakSpans` gives one `AheadBreakSpan(FromX, ToX)` per run of adjacent
gap columns that meets the range and holds at least one `BreakKind.Ahead` column. `FromX` is the last
value column before the run, `-Infinity` when none, as on an empty line, where the read held no row for
the pen; `ToX` is the first value column after it, or the run's last ahead-break X when none. A history
anchor right before a kept ahead-break therefore stays inside one span, and a window lying wholly inside
a run still yields it, because `VisibleRange` keeps one column past each edge and the run is walked over
the whole list. At a coarse layer the first value column after the ahead-break is the first live bucket,
which keeps its opening X, so the span ends there. `EnvelopeLine.CollectAheadBreakSpans` runs it under the
column lock over its own X axis' visible range and refills the caller's list. `EnvelopePathTests`
(`AheadBreakSpans_...`) pins the rule. A run that meets the range is walked to both ends on every frame,
under the column lock, so the cost grows with the run's length; a run longer than two columns comes only
from adjacent null rows.

A line whose history ends on a gap, such as a `q = 32` anchor, takes no ahead-break, because
`EnvelopeLine.AppendAfterBreak` writes no `NaN` column after a gap; the span from that gap to the first
live value keeps the ordinary look and carries no label
(`TrendPenStateTests.Realtime_AFirstValueOnALineEndingOnAGap_TakesNoSecondNaNColumn`). The live stream is
not gated on history, so a value that reaches an empty line before the pen's first history read lands
opens a span at the data area's left edge, labelled, until that read replaces it; the race lasts one read
and is accepted.

**The band and the label.** `Render` holds the shade's own lock throughout, refills one reused span list,
and fills each span, clipped to the data area, through one `FillStyle`. The label is one `LabelStyle`
whose text, `Resources.ChartAheadBreakLabel` at 12 px, is read once when the view builds the shade. The
style sets its font and colour on `rp.Paint`, and on the first `Render` that meets a span the shade measures
the text and shapes it into one `SKTextBlob` it keeps; the text never changes, because the culture is fixed
for the process (`ui-text.md#two-sets-one-selector`). Each frame draws that blob with one
`SKCanvas.DrawText`, not through `LabelStyle.Render`, which in ScottPlot 5.1.59 splits the text, measures
it through LINQ and allocates on every call. The shade is `IDisposable` for the blob, and the view disposes
it when it removes the shade with the marker layer. The label is centred in the clipped
band, its bottom 4 px above the data area's bottom, and drawn only when the band is at least the label's
width plus 4 px on each side; otherwise the band draws alone, because a label wider than its band would
sit over rows the archive holds. The marker labels hide under the same fit rule. The bottom edge keeps the
label clear of the marker labels at the top edge and of the hover readout at the crosshair's top end. The
shade allocates nothing per frame and runs no LINQ. `ChartMarkerReadings` still draws its numbers through
`LabelStyle.Render`, one allocation per reading per frame (`docs/plans/backlog.md`). The shade's colours are
`AppAheadBreakFillBrush` and `AppSecondaryForegroundBrush` (`ui-theme.md#the-plot`).
`ChartAheadBreakShadeTests` pins the band, the label's fit in the span and in its visible part, the span
of an empty line, the ordinary break, the missing line and a render against the UI thread's rewrites over
`Plot.GetImage`, and `TrendChartViewTests` (`TheAheadBreakShade_...`) the wiring.

### Ticks for the drawn axis only

ScottPlot 5.1.59 regenerates every Y axis twice per frame, at the figure height in
`Layouts.Automatic.GetLayout` and at the data-area height in `RenderActions.RegenerateTicks`, and neither
reads `IsVisible`; only `YAxisBase.Measure` and `Render` skip a hidden axis. So every pen axis carries an
`IDrawnTickGenerator`, a `LinearTickGenerator` or a `LogTickGenerator`, whose `Regenerate` returns at once
while `IsDrawn` is false. `LinearTickGenerator` wraps ScottPlot's `NumericAutomatic` and forwards
`MaxTickCount` to it, which nothing in the application writes. Both keep their last two results in a
`TickCache`: the linear one keyed on the limits, the length and the label font size, the log one on the
limits, the length and the mask. The second length of a frame and every frame of a still axis reuse the
published array. A drawn Auto axis still generates on every pan step, because `PenScaleModel` moves its
range; a Manual axis hits the cache.

`ChartAxisBinder` installs a `LinearTickGenerator` on every axis it creates, `Axes.Left` included, and on
every switch back from log, each built with the axis's current `IsVisible` as its `IsDrawn`. It writes
`IsDrawn` beside every `IsVisible` write, `HideAxis` included. ScottPlot reads the two at different points
of one render pass under `Plot.Sync`: `IsDrawn` when it regenerates the ticks, `IsVisible` when it measures
and draws the panel. So the binder changes both inside one `lock (Plot.Sync)`, and no frame measures or
draws an axis whose generator skipped its ticks. It takes that lock only when a flag changes, on an
active-pen switch or a pen shown or hidden. The `Apply` of a pan step finds both flags already at their
values, returns before the lock and never waits on a render in progress. A hidden log axis keeps its
`LogTickGenerator`, because the generator type is the axis's log flag (`#log10-y-axis`). The bottom
`DateTimeAutomatic` axis and ScottPlot's default right axis keep their stock generators.

A hidden generator publishes no ticks. The horizontal gridlines come from `Grid.YAxis`, so the plot draws
none while no pen axis is drawn, as with the active pen switched off. `Grid.YAxis` stays on the last drawn
axis until the binder moves it to the newly drawn one, so a frame that lands between hiding the old active
axis and that move has no gridlines; it is the same frame that has no Y axis.

### The frame-paced redraw

The chart redraws at most once per display frame. `TrendChartViewModel.RequestRedraw` raises
`RedrawRequested` at once, on the calling thread, when a view subscribes, and does nothing otherwise;
`Dispose` completes the signal. The view coalesces: when no frame is pending it calls
`TopLevel.RequestAnimationFrame`, and it marks a frame pending only after that call returned, so a view
with no top level requests nothing and is free to request once attached. Avalonia runs the callback on
the UI thread inside the next render pulse (`MediaContext` -> `Clock.Pulse`, a `Render` dispatcher
operation), in the same job that records the visuals and commits the batch, so the plot's
`InvalidateVisual()` lands in that frame. Every request until then joins the pending frame.

The callback clears the pending mark. Only when the view still has a view model and a top level does it
invalidate the plot, reposition the cursor overlay and count the frame in `ServedFrameCount`, which the
tests read. `OnDataContextChanged`, `OnUnloaded` and the signal's completion clear the pending mark, so a
callback that outlives its view, its view model or its plot does nothing. `OnLoaded`, the theme repaint,
delta-cursor placement and the start of a pan request a frame through the same path; nothing in the view
calls `AvaPlot.Refresh()`.

`RequestAnimationFrame` verifies the UI thread, so `RequestRedraw` with a view subscribed is UI-thread
only, and Avalonia enforces it; every caller runs on the UI scheduler. A request off the UI thread throws
to its caller, and Rx then ends the view's subscription to the signal: the chart repaints afterwards only
on the view's own requests, which the pending mark, set only after a request succeeded, still serves.
Under Win32 the pulse follows the compositor's commits, one per display frame, so the redraw follows the
display with no timer and no timer resolution in between.

Only data, window, visibility, gesture and delta-toggle changes request a ScottPlot redraw; hover and
pointer-exit do **not**. A pointer move updates only the Avalonia cursor overlay, which the frame
callback repositions after the invalidation and `SizeChanged` repositions on a resize.

### Hover and the plot cache

The overlay does not keep the plot from re-rendering on its own: the crosshair's and the readout's dirty
rects lie inside the plot, so the compositor replays `AvaPlot`'s draw operation, which is ScottPlot's whole
`RenderOnce`, on every pointer move. Measured 2026-10-08 at 8 pens: about 52 renders per second while
hovering, against about 33 inside a drag, ~2.4 ms each. A drag, a zoom and every follow tick change the plot
itself, so they re-render in any design; only a hover over a still plot is wasted work.

The plot is therefore cached, on its wrapper: `TrendChartView.axaml` puts `PlotControl` alone inside the
`PlotLayer` decorator and sets `CacheMode` to a `BitmapCache` there, not on `AvaPlot`. The compositor keeps
the plot's raster in that layer and blits it while only the overlay changes; the frame callback's
`InvalidateVisual()` reaches the layer as a child's change and ScottPlot renders it once. Measured
2026-10-09 at 8 pens: about one `RenderOnce` per second while hovering a still plot, from the data the writer
appends, against about 52 without the cache; follow and drag render as often as without it.

The cache sits on the wrapper because a `BitmapCache` on `AvaPlot` itself freezes the plot on its first
frame: Avalonia 12 (12.0.5 through 12.1.3) adds a self-drawing visual's own dirty rect to the window's
region before it switches to the visual's cache collector (`ServerCompositionVisual.Update.cs`, `PreSubgraph`),
so the cache never sees the plot change and blits its first layer until a resize recreates it (no `RenderOnce`
in 15 s of live follow, 2026-10-09). `ThePlotIsCachedThroughItsOnlyAncestor` pins the layout: the decorator
holds the cache and `PlotControl` alone, `PlotControl` holds none. The headless platform returns a stub layer,
so no headless test sees the freeze; the follow capture in `testing-strategy.md#frame-cost` does, and it reruns
after every Avalonia upgrade.

The layer costs one chart-sized texture per window, about 8 MB at 1920x1080 and 100% scaling and about 19 MB
at 150%, and its text is grayscale-antialiased, because `EnableClearType` stays false.

### Log10 Y axis

The logarithmic scale is base 10, the one base the chart builds. The operator texts call it the
logarithmic scale and leave the base out (`ui-text.md`), as Excel, MasterSCADA 4D and Simple-Scada 2 do.

ScottPlot 5.1.59 has no log axis: `YAxisBase.GetPixel` is linear and `IAxis` exposes no transform. The
transform lives at the render edge instead, and everything before it stays in the pen's own units:
`PenScale.Min`/`Max`, the axis scale panel's bounds, `ScaleRangeForPen` and the cursor, hover and delta
readouts, which read stored values. Two places convert:

- `ChartAxisBinder.Apply` sets a log axis's limits to `log10(Min)` and `log10(Max)`, so the axis runs in
  decades.
- `EnvelopeLine.Render` projects every Y through `LogAxis.Project` while its axis is logarithmic.

**The axis type lives in the tick generator.** An axis is logarithmic while its `TickGenerator` is a
`LogTickGenerator`, and nothing else on the render side holds the type. `Render` reads that mode once per
frame and projects through the axis's own `GetPixel`, and the line keeps no flag of its own to drift from
the axis. `GetAxisLimits` reports no Y limits under a log axis, because its columns are in the pen's
units and the axis runs in decades; nothing autoscales a pen's axis through ScottPlot today. The binder
switches a type under one `lock (Plot.Sync)`: the generator (a new `LogTickGenerator`, or a new
`LinearTickGenerator` on the way back), the limits and the minor gridline width, so no frame draws decades
under linear ticks or the reverse. A pan or an autoscale that keeps the type sets the limits as a linear
axis does.

**Non-positive samples are pinned.** A value `<= 0` has no logarithm. `LogAxis.Project` puts it on a
floor 2 px above the data area's bottom (`EnvelopeLine.PinnedFloorPixels`), because the bottom axis frame
is drawn over the data area's bottom row and would hide a line there. A positive value under that floor,
such as a dip below a manual minimum, lands on the same floor, so the line never draws `0` above a
smaller positive value. The line stays continuous through it, and the readouts show the stored value,
`0`. A NaN stays NaN, so a gap stays a gap.

**The range is padded in decades.** The auto range skips values `<= 0` (`ValueRange.Widen`), and
`PenScaleModel` pads an auto log range by 5 % of the decades it spans on each side, a flat range by half
a decade, and keeps both padded ends between decades -307 and 308, so a `+Infinity` sample or a value
near the end of the double range never gives the axis a limit of 0 or infinity. A manual log range with a
minimum `<= 0` can only come from a row written by hand, since the axis scale panel and the pen form both
refuse it; `SanitizeManualRange` replaces it with a positive range. A manual pair with an infinite or NaN
bound, which also only a hand-written row can hold, opens on the default range instead, 0..1 on a linear
axis and 1..10 on a log one. The panel, the chart's `SetLogarithmic`, the pen form and
`SanitizeManualRange` read one rule, `LogAxis.AdmitsMinimum`.

**Tick choice is Core.** `LogAxis.Ticks(minLog, maxLog, pixelLength)` returns ascending `LogTick`s, all
inside the range, and nothing for a non-finite bound, `minLog >= maxLog` or a length `<= 0`. Majors come
from one of three families:

1. linear: one value step, the smallest 1-2-5 step whose narrowest gap, at the top of the range, is at
   least 40 px;
2. mantissas 1, 2 and 5 in every decade;
3. decades, at the smallest stride of 1, 2, 3, 5, 10, 20, ... that leaves 40 px between majors.

Of the families with at least two majors and no gap under 40 px, the one with the most majors wins, ties
going to the earlier one in that order. The first readable family would let the linear one win a range of
one to three decades with its majors in the top decade only: over 2..50 at 400 px it gives 20 and 40,
where 1-2-5 gives 2, 5, 10, 20 and 50. With no readable family, the two range ends are the majors. Minors
fill what the majors skip: mantissas 2..9 under a decade stride of 1, the skipped decades under a larger
stride, mantissas 3, 4 and 6..9 under 1-2-5, four even steps per interval under the linear family, the
partial intervals at both ends included, and none under the range ends. One call builds at most
`pixelLength / 40 + 2` majors plus their minors. Skipped decades closer than 1 px get no minors, and the
linear family gets none once its minor multiples pass 2^53, where a double no longer tells neighbours
apart: a range of float noise, such as 1 to 1.0000000000000007, then still returns its ticks.

**Labels go through the pen's mask.** `Chart/LogTickGenerator` adapts `LogAxis.Ticks` to ScottPlot's
`ITickGenerator` and labels a major `PenValueFormat.Format(10^position, Mask)`. A major turns into a
minor when its label equals `PenValueFormat.Format(0, Mask)`, a positive value the mask cannot show, when
it does not parse back under the current culture to within 1 % of its value, or when it repeats the last
label kept, so no gridline carries a label that misreads it. Over 0.02..0.5 under `0.0` the 0.05 gridline
would read "0.1"; it stays a minor one and the 0.1 gridline keeps the label. A label whose literals no
number parse reads, such as parentheses, is kept. A pen spanning decades needs an exponent mask such as
`0.0E+0`; under a fixed-point mask or the `0.###` fallback the decades below the mask's precision keep
their gridlines and lose their labels. The binder writes `Mask` from `Pen.Format` on every `Apply` on the
UI thread, and `Regenerate` reads it on the render thread under `Plot.Sync`, so a revised mask reaches
the labels at the next catalogue read. ScottPlot regenerates each Y panel at two lengths per frame, so
the generator caches the last two results, keyed on the limits, the length and the mask
(`#ticks-for-the-drawn-axis-only`); a published `Ticks` array is never changed. `MaxTickCount` is
unused, because `LogAxis.Ticks` bounds the count itself.

**Minor gridlines show on a log axis only.** The binder sets `Grid.YAxisStyle.MinorLineStyle.Width` to 1
when it draws a log axis and to 0 otherwise, in the same step that assigns `Grid.YAxis`, and
`ChartPalette` paints them `AppMinorGridLineBrush` (`ui-theme.md`).

The minimap band stays linear (`trend-interaction.md#archive-overview-minimap`), and the delta readout
stays a linear difference.

## Reference: legacy SCADA trend window

The legacy Simple-Scada trend window ("Параметры (Графики)") is a functional reference —
SemiPlot must match its capabilities and improve usability. The authoritative feature set is
trend-feature-spec.md (MasterSCADA-derived); this section only records the visual elements to
reproduce.

The capture itself is not kept here: the only one available is from a live installation and
discloses the customer's tag list. The elements below are what SemiPlot reproduces.

Elements to reproduce and improve:

- **Left Y axis = scale of the selected pen** (legacy shows the active pen's scale), on top of
  the simultaneous multi-axis capability (trend-feature-spec.md §AY-1, §AY-2).
- **Mini-legend** with columns: checkbox / color / name / current value
  (trend-feature-spec.md §PN-8). Pens are logically grouped (ICP, RIE, pressures, gases,
  temperatures).
- **Aggregation layer** ("Слой": raw / minute / hour): the resolution the history read
  asks for; maps to the archive `l` column (see data-integration.md). At 256 columns Raw reaches
  64 min and Minute 64 h; at 2048 columns Raw reaches 8 h 32 min and Minute 512 h; Hour reads every
  wider window, to the 365-day cap. The legacy window lets the
  operator pick it. SemiPlot does not: the zoom width picks the layer and the status bar shows the
  name read-only, named from resx rather than from `AggregationLayer.ToString()`. The manual pin is
  trend-feature-spec.md §DA-4 and is not built; the automatic selection is §DA-2.
- **Cursor** with value readout at a point (trend-feature-spec.md §CU-1, §CU-2).
- **Time navigation:** zoom/pan, jump to start/end, range selection
  (trend-feature-spec.md §TM-1 … §TM-4).
- Toolbar (snapshot, save, print, refresh, favorite, help) and tabs (Trends / Values /
  Legend / Settings).

## Required charting features

The full prioritized requirement set lives in trend-feature-spec.md and is not duplicated here.
The mapping below routes the major capability groups to their feature IDs:

- **Pens (series)** — runtime add/remove, per-pen visibility, identity/color/group/value:
  trend-feature-spec.md §PN-1, §PN-4.
- **Y axes / scaling** — one axis per pen with its own min/max, the active pen's axis on the left:
  trend-feature-spec.md §AY-1, §AY-2.
- **Cursor / inspection** — vertical cursor reading every visible pen at the cursor X:
  trend-feature-spec.md §CU-1, §CU-2.
- **History performance** — smooth zoom/pan over long archives via aggregation layers and
  decimation: trend-feature-spec.md §DA-2, §DA-3, §DA-5.
- **Grouping / layout** — view pen groups separately or together: trend-feature-spec.md §MS-2.

Canonical use cases (acceptance fixtures): 16 dampers + 16 heat sources (dampers viewed separately, the
16 heaters reading against the same bounds, which is the same `scale_min_on_start`/`scale_max_on_start`
pair stored on each of them rather than a shared axis) and 10 gas lines with different min..max ranges
(all on one chart, each with its own scale — §AY-2).

## Module layout (Avalonia views / view models / Core models)

There is no JavaScript and no web bridge. The viewer is Avalonia views with ReactiveUI view
models, backed by renderer-agnostic models in `SemiPlot.Core`. Responsibilities:

**Views + view models (`SemiPlot.UI`):**

- `Chart/TrendChartView` + `TrendChartViewModel` — the chart. The view is the only type touching
  `AvaPlot`; the view model owns a bare `ScottPlot.Plot` (headless-constructable), the pen set and the
  coordinator subscriptions — so it is unit-tested headless. `ApplyCatalogue` takes a catalogue read
  (Applying a catalogue read, below).
- `Chart/ChartPenSet` — the pens the chart shows: one `TrendPenState`, one plottable and one
  `PenScaleSettings` per pen id, and `Ordered`, the same states in catalogue order, rebuilt from the
  dictionary on every catalogue applied, so the two never hold different pens. It finds the pen nearest
  a click (`PenNearest`) and sets the active pen's width and draw order (`RestyleActiveLine`).
- `Chart/ChartPenPick` — the pure pen choice rule: nearest distance within the 6 px tolerance, the active
  pen on a tie, otherwise the first listed.
- `Chart/EnvelopeLine` + `EnvelopePath` — the per-pen `IPlottable`, the private column buffer with the
  lock over it and the mutators that take it, and the pure geometry it strokes: the visible-column
  search, the min/max point order, the gap break, the step shape, the hold past the last column
  (`#the-hold-to-the-live-edge`) and the ahead-break spans (`#the-ahead-break-shade`).
- `Chart/TrendPenState` — one pen's `EnvelopeLine`, `IsVisible`, `CurrentValue`, and the
  history-load / realtime-append / fold logic that drives those mutators, with the break ahead of a first
  live value and its kind, the `LiveJoin` that says how the line is joined to the live stream
  (`#per-pen-plottable-envelopeline`), the one `SetHoldEnd` call and `HeldValueAt`, the held value at a
  moment (`#the-hold-to-the-live-edge`).
  `Revise(Pen)` replaces the pen, which raises `Pen` like `IsVisible` and `CurrentValue`, and restyles the
  line; `IsVisible` and the loaded history stay. `IsVisible` is seeded from `Pen.EnabledOnStart` at
  construction and has a private setter; the internal `SetVisibility`, which
  `TrendChartViewModel.SetPenVisibility` alone calls, is its
  one writer, and a method is what no binding can reach. The hover readout reads `Pen` per pointer move;
  the sidebar row binds through it.
- `Chart/ChartAxisBinder` — applies the `PenScaleModel` output to ScottPlot Y axes (one left axis per
  pen, `SetLimitsY`, shared-X pinning) and switches an axis between linear and log10: its limits in
  decades, its tick generator and the minor gridline width (`#log10-y-axis`); writes each generator's
  `IsDrawn` beside the axis's `IsVisible` (`#ticks-for-the-drawn-axis-only`).
- `Chart/IDrawnTickGenerator` - the `ITickGenerator` of a pen axis, which generates nothing while its
  axis is not drawn (`#ticks-for-the-drawn-axis-only`).
- `Chart/LinearTickGenerator` - the `IDrawnTickGenerator` of a linear axis: ScottPlot's `NumericAutomatic`
  behind the two-entry cache (`#ticks-for-the-drawn-axis-only`).
- `Chart/LogTickGenerator` - the `IDrawnTickGenerator` of a log axis: `LogAxis.Ticks` with majors labelled
  under the pen's mask, and the two-entry cache (`#log10-y-axis`).
- `Chart/TickCache` - the two-entry cache both generators keep their last results in.
- `Chart/ChartNavigationController` — owns the `TrendNavigationModel`, the layer ladder, the live-edge
  advance; raises `WindowChanged` (`NavigationWindow` = `[From, To]` + `Layer` +
  `RequiresHistoryRequery`). A ceiling is derived, not constant:
  `nextCoarser(layer).ToPointSpacing() × TargetColumnCount × min(1, ReferencePenCount / max(1, visiblePens))`,
  guarded by a 10% hysteresis band; `ReferencePenCount` is 16 (`data-integration.md#layer-ladder`).
  `SetVisiblePenCount`, which `TrendChartViewModel` calls after every catalogue apply and every
  visibility change, stores the count and raises `WindowChanged` when the layer moves.
  `TargetColumnCount` is the canvas width in columns, clamped to 256…2048 and quantised to a power of
  two with its own 10% deadband; it selects the layer only, never the query resolution. **Single
  writer:** `TrendChartViewModel.ReportDataAreaWidth`, called from `TrendChartView`'s
  `Plot.RenderManager.RenderFinished` handler — do not call `SetTargetColumnCount` from anywhere else,
  in the same style as the navigation bar's `IsSticky`. That seam carries the `DataRect` of the frame just
  rasterised; `Plot.LastRender` read when a frame is requested would still describe the previous frame, so a
  resize could leave the layer computed for the old canvas with nothing scheduled to correct it.
  `RenderFinished` fires on Avalonia's render thread, so the handler posts the width to the UI thread,
  and only when it differs from the last width it posted. The last width lives in the handler's closure
  and is read and written on the render thread only. A repeated width would change nothing anyway:
  `SetTargetColumnCount` drops an unchanged quantised count.
  A changed *quantised* count re-queries the window even when the layer survives, because it also
  invalidates the decimation width the visible data was fetched at. The re-query keys on the quantised
  count while the query resolution follows the unquantised width, so a resize inside the deadband
  (with 1024 in force: any width from 659 to 1592 px) changes the requested resolution without
  re-querying. The drawn resolution then lags the canvas by up to one deadband span, `2 × 1.1² ≈ 2.42`,
  until the next navigation gesture re-queries at the current width.
  Every window change that asks for a re-query passes the prefetch gate below before a query leaves:
  the flag says the data may be stale, `HistoryPrefetch.Covers` says whether it is.
- `Chart/HistoryColumnTarget` — pixel width → column count (one per pixel, clamped to 256…2048). A
  non-positive or NaN width has no canvas behind it, so `TrendChartViewModel.ReportDataAreaWidth` drops it and
  keeps the last width. The unquantised value is what every
  history query asks the provider to decimate to, times the windows the prefetch below fetches;
  `ChartHistoryPlanner.ReportedColumnTarget` keeps the last reported one and stands on `MaxColumns` until
  the first render reports.
- `Chart/ChartHistoryRequestDebouncer` — the one history path, for the initial load and every gesture:
  `Throttle` (one trailing request 150 ms after the gesture goes quiet) merged with `Sample` at a
  400 ms cap interval (one request per cap while the gesture keeps moving), a request for the window
  the last successful read brings dropped → one query at a time on the data scheduler, the newest request
  that arrived while it ran running when it lands → apply, or report the failure, on the UI
  scheduler. The cap is what fills the strip a long drag exposes while it is still moving; the single
  slot is what lets a read slower than the cap complete at all. The trailing request cancels a read
  still running for another window, also when the gesture ends back on the window already drawn
  (`data-integration.md#what-one-history-query-covers`). The first-snap path stays non-requerying.
- `Chart/HistoryPrefetch` — visible window → the range to fetch. `Visible` is the window alone, with
  its right margin at the live edge, and `Expand` adds one window width of margin on each side; each asks
  for the column target times the windows it spans, so the fetched range stays at one column per pixel,
  and each clamps the left edge to the archive's first sample. `ChartHistoryPlanner` keeps the last
  `FetchRange` and asks `Covers` before every request, so a pan that stays in the inner band, half of
  each side's margin in from that fetched edge, issues no query, and a pan past it, a zoom, a layer
  change and a column-target change issue a `Visible` read first
  (`data-integration.md#what-one-history-query-covers`). A range
  reaching the first sample holds its whole left side, so a window starting before the first sample is
  read once. Envelopes therefore span up to three windows, and the auto scale reads the visible one
  only, through `PenScaleModel`'s window bound.
- `Chart/ChartHistoryPlanner` — the one history request decision: the range in hand, the pens read
  for it, the column count it was read at and the last reported width. `Next` answers what the window in
  view still needs read, `Land` takes an applied read and `Forget` drops the range in hand
  (`data-integration.md#what-one-history-query-covers`). `TrendChartViewModel` owns one and is its only
  caller.
- `Chart/ChartRealtimeApplier` — the append-vs-fold rule per layer for incoming `RealtimeBatch`es.
  It walks each `PenRealtimeValues` on that pen's own timestamps, never on the batch's union, and
  hands the union's last timestamp to `ChartNavigationController.OnLiveEdge`. A null value is a break:
  it goes to `TrendPenState.AppendRealtime` ahead of the fold-or-append choice, so it appends a `NaN`
  column at every layer and never folds. A value goes to `AppendRealtime` at `Raw` and `FoldRealtime` at a
  coarse layer, and `TrendPenState` decides there whether it opens behind an ahead-break.
- `Chart/ChartLiveHold` - the one writer of the edge X every pen is held to: `NewestSample` as an axis X
  while the OPC UA session is up, `NaN` otherwise, re-applied on `NewestSampleMoved`, on a change of the
  session flag and after every catalogue apply (`#the-hold-to-the-live-edge`).
- `Chart/ChartCursorReader` - the cursor's reading of every visible pen (`ReadAt`) and of one pen
  (`ReadPenAt`) through one private read: the history envelope's interpolated `Center`
  (`CursorReadoutModel.ReadPenAt`), else the pen's `CurrentValue` inside its held span
  (`TrendPenState.HeldValueAt`, `#the-hold-to-the-live-edge`).
- `Chart/ChartDeltaCursorReader` - view-side state wrapping the Core `DeltaCursorModel` over the active
  pen's history envelope (`FormatReadout` formats Δt/Δy).
- `Chart/ChartHoverReadout` — pure static `BuildContent`: builds the readout string (local timestamp +
  every visible pen's value at the cursor X; gap or missing pen → dash) that feeds the Avalonia overlay
  `TextBlock`. No plottable; unit-tested as plain `[Fact]`.
- `Chart/ChartCursorOverlay` — pure projection (no Avalonia/`AvaPlot` deps): cursor pixel X + `DataRect`
  + render scale → crosshair endpoints in DIP space, or hidden when the cursor X falls outside the data
  rect's X range; unit-tested.
  The view (`TrendChartView`) renders the result onto a transparent overlay `Canvas` (crosshair `Line` +
  readout `Border` at the line's top end), suppressed during drag / delta mode.
- `Chart/LeftButtonTool` (enum `Pan | DeltaPlacement`) — the single left-button gesture state, sourced
  from the navigation bar's delta toggle.
- `Chart/ChartAxisRegion` — axis band hit-tests from the last render layout: `TryCreate`, the active pen's
  Y-axis panel band beside the data area, for the press (`EditAxisScale`) and the wheel
  (`ScaleActivePen`); `TryCreateTimeAxis`, the bottom axis band under the data area, for the press
  (`PanOrEditTimeWindow`).
- `Chart/ChartPressRouter` — the pure routing of a left press (`EditAxisScale`, `ShiftActivePen`,
  `PanOrEditTimeWindow`, `PlaceDeltaCursor`, `DragMarker`, `DeleteMarker`, `PanOrPickPen`, `AddMarker`)
  and the 4 px click slop that
  separates a click from a pan.
- `Chart/ChartWheelRouter` — the pure routing of a wheel event (`ScaleActivePen`, `ShiftActivePen`,
  `PanTime`, `ZoomTime`, `None`) and its notch sign (`trend-interaction.md`, "The wheel map").
- `Chart/AxisScalePanel` + `AxisScalePanelViewModel` — the flyout that edits the drawn pen's two bounds
  and its axis type (`trend-interaction.md#the-axis-scale-panel`); `TrendChartViewModel.AxisScale` owns
  the view model.
- `Chart/TimeWindowPanel` + `TimeWindowPanelViewModel` — the flyout a time-axis click opens: the window's
  local start and end, its width and the width presets, and the calendars' blackout days from one archive
  days read per open (`trend-interaction.md#the-time-window-panel`); `TrendChartViewModel.TimeWindow` owns
  the view model.
- `Chart/IMarkerChannel`, `Chart/ChartMarkers` and `Chart/ChartMarkerLines` — the time markers.
  The channel is the chart's port to the stored markers, which `TrendChartViewModel` takes in its
  constructor and `Markers/MarkerSync` implements; `TrendChartViewModel.Markers` owns `ChartMarkers`, and
  the view owns one line layer per bound plot (`trend-interaction.md#the-time-markers`,
  `overview.md#the-live-markers`). The view's right-click menus and label editor are the partial
  `Chart/TrendChartView.Markers.cs`.
- `Chart/ChartAheadBreakShade` - the grey band and its label over the drawn pen's ahead-break spans, one
  plottable under every pen line per bound plot, created and removed with the marker layer
  (`#the-ahead-break-shade`).
- `Chart/LocalTimeAxis` — UTC↔local-OADate conversion at every render boundary.
- `Navigation/NavigationBarView` + `NavigationBarViewModel` — time navigation only: jump-to-now,
  sticky toggle, delta-mode toggle + inline Δt/Δy readout (ReactiveUI commands). The axis scale panel
  holds autoscale, the two limit boxes and set-limits, and the status bar shows the layer.
- `Navigation/WallClockRangeForm` — the date-plus-`HH:mm:ss` range fields and the five range rules that
  `Export/ExportRangeForm` and `Navigation/TimeWindowForm` derive from; `TimeWindowForm` adds the width
  field, the skipped-time and span rules and the per-edge comparison with its seed.
  `Navigation/WindowWidthText` formats and parses the width as `h:mm:ss` with unbounded hours.
- `Navigation/TimeFieldInput` — the attached `Layout` that keeps only the digits a text box receives and
  inserts the separators: `ClockTime` and `Date` fill the slots of `WallClockRangeForm.TimeFieldFormat` and
  `Resources.ExportDateFormat` from the left, `Duration` groups seconds and minutes from the right and leaves
  the hours unbounded. Selection, Backspace and paste stay the `TextBox`'s own. The time window panel and the
  export window set it on their time and width boxes, and a style sets `Date` on each
  `CalendarDatePicker`'s `PART_TextBox`.
- `Navigation/ArchiveDayCalendar` — the days a pair of range calendars offers and the read days without a
  row among them, for the time window panel and the export window; `Navigation/CalendarBlackout` writes the
  set into the pickers' `BlackoutDates`.
- `Legend/TrendLegendView` + `TrendLegendViewModel` (+ group / row VMs and the converters) — the grouped
  sidebar. A row carries the on/off box, a round colour dot, the name, the current value in the pen's own
  mask and the unit; nothing in it is editable but the box, which an allowlist test over the built row
  template holds. A header is a section caption, not a bold name: smaller text in
  `AppSecondaryForegroundBrush`, set in capitals by `LegendConverters.ToCapitals`, a one-pixel
  `AppSubtleLineBrush` line above every header but the first (a `:nth-child(1)` style hides the first),
  and the rows under a header indented so their box starts where the caption starts. The active row keeps
  the regular weight and is marked by an `AppAccentFillBrush` background and a 3 px `AppAccentBrush` bar
  on its left edge, both following `IsActive`. A pen draws once and is listed under every group it
  belongs to, so one row view model appears under several headers and `Dispose` walks the distinct list.
  Every drawn header, the ungrouped one included, carries a switch box:
  `TrendLegendGroupViewModel.SwitchState` is derived from its rows' pen states (all on true, all off
  false, mixed null) and stored nowhere, the box reads it `Mode=OneWay`, and `SwitchGroupCommand`, its
  one writer, switches every row on unless all are on, then off. A pen's visibility is
  `TrendPenState.IsVisible` alone, and the chart's `SetPenVisibility` is its one writer: the row keeps no
  flag, exposes the chart's state as `PenState`, and its box reads `PenState.IsVisible` `Mode=OneWay` and
  writes through `ToggleVisibilityCommand`, the pattern of a checkable menu item. The box command and the
  header switch both reach the chart through the row's `SetVisibility`, one row at a time, so every other
  header a shared pen sits under re-derives. `BuildGroups` keys every row by its groups, an ungrouped row
  by the ungrouped header, in one ordinal `GroupBy`, so a group named like the ungrouped header takes the
  ungrouped rows; headers sort alphabetically in the current culture, the ungrouped one last. It
  constructs every group view model once per build, because a replaced group would keep its subscriptions
  on the rows. `Rebuild()`, called after every catalogue read that changed something, builds new rows and
  groups from the chart's `Pens`, assigns `Groups`, which raises `PropertyChanged`, and only then
  disposes the rows and groups it replaced, so no binding reads a disposed row. A row binds its pen's
  name, colour and unit through `PenState.Pen`, which a revision replaces, so they follow a revision
  without a rebuild; it formats each reading under the mask `PenState.Pen` carries when that reading
  arrives, so a revised mask shows from the next reading or the next rebuild. Its box reads the pen
  state the chart keeps, so every pen keeps its visibility. `IsExpanded` is the panel's one state flag: collapsed, the row drops
  the value and the unit. The panel keeps two session width slots, expanded (starts at 280) and collapsed
  (starts at 168), and `PanelWidth` shows the slot of the current state, clamped to at least
  `PanelMinWidth` (120) and to the room that leaves the chart at least `ChartMinWidth` (320). `FitPanel`
  is the one writer of that room: `MainWindow.axaml.cs` calls it in `OnLoaded` and on every `SizeChanged`
  of the content grid, so the chart floor holds from the first frame, and a shrink narrows the shown
  width without writing a slot, so growing the window back restores it. The widths and `IsExpanded` are
  the panel's own fields, so a `Rebuild()` keeps them. A `Thumb` between the chart and the panel is the
  drag handle: its `DragDelta` handler calls `ResizePanel`, the only writer of the shown slot, which
  applies the same clamp. Semi ships no `Thumb` theme, so the handle carries its own template, a `Border`
  painting its `Background`; without one it draws nothing and a press never reaches it. The handle shows
  only while `IsLegendVisible` holds. Nothing persists the state or the widths — every start opens
  expanded at 280.
- `Minimap/MinimapView` + `MinimapViewModel` — Canvas-based archive-overview strip with no baseline,
  and a label row under it that holds the two end labels and, under the pointer, the time at the strip's
  1 px hover line (`HoverAt`, `ClearHover`, `HoverFraction`, `HoverLabel`); navigates via the shared
  `ChartNavigationController`, ends at the later of the extent's last sample and the navigation's
  `NewestSample`; its `Minimap/MinimapBandFeed` reads the drawn pen's band over the whole strip
  (`trend-interaction.md#archive-overview-minimap`).
- `Minimap/MinimapBand` — the strip's band, a `Control` with a `Render` override that fills and strokes
  the figures `MinimapGeometry.BandFigures` shapes; it reads nothing.
- `MainWindow/TrendWindow` — builds the window's parts once, in dependency order, and disposes them in
  reverse (`overview.md#one-window-per-process`).
- `MainWindow/MainWindow` + `MainWindowViewModel` — the six-row window grid, the flags its View
  menu writes and the menu's two axis commands under View -> Pen scale, which call `AutoscaleActivePen`
  and `RestoreInitialScale` on the chart, and the submenu header that names the drawn pen; the window's
  code-behind owns the five view-side requests (close, About dialog, settings dialog, pen editor, export
  window).
- `MainWindow/AppMenuBar` — the File / Edit / View / Help menu. Each checkable item reads its flag
  `Mode=OneWay` and writes it only through the command it invokes (`AGENTS.md`, UI).
- `MainWindow/AppStatusBar` + `AppStatusBarViewModel` — current connection state and the active
  aggregation layer, named from resx. Built by `TrendWindow` over the catalogue sync's `ConnectionStates`
  and the chart's navigation, and writes the fault and recovery entries (`data-integration.md`).
- `MainWindow/AboutDialog` + `AboutInfo` — the modal naming the product, the assembly version and the
  configuration directory this run read.
- `Settings/SettingsDialog` + `SettingsViewModel` - the modal that edits the `app/`, `connection/` and
  `opc/` section folders, opened from Edit -> Settings (`overview.md#the-settings-window`).
- `PenEditor/PenEditorWindow` + `PenEditorViewModel` (+ `PenFormViewModel`, `PenGroupsViewModel`, the
  row and membership VMs, over `Bridge/ArchiveCallQueue`) - the modal that edits the pen catalogue through
  `IPenCatalogueEditor`, opened from Edit -> Pens and groups (`overview.md#the-pen-and-group-editor`).
  Nothing under `Chart/` or `Legend/` references it.
- `Export/CsvExportWindow` + `CsvExportViewModel` (+ `ExportRangeForm`, `ExportRangeProblemText`, `ExportMessageLine`, `ExportPenChoice`, `CsvFileExporter`)
  is the modal that writes the archive's stored rows of a range and a set of pens into one CSV table ordered by time, opened
  from File -> Export to CSV... (`overview.md#the-export-window`). Nothing under `Chart/` or `Legend/`
  references it.
- `Messages/MessagePanelView` + `MessagePanelViewModel` — the bounded, newest-first list every failure
  lands in, capped at `MessagePanelViewModel.MaximumEntries` (200) with the oldest dropped.
  `IsVisible` decides the row, an empty list included, and the empty list carries its own line;
  `ToggleCommand` is the single writer of `IsVisible` and has two callers, the View menu and the
  status bar's connection indicator, both of which read that same flag back.
- `Messages/MessageEntry` + `MessageSeverity` — one row: the mapped failure, the UTC last-seen stamp
  the view renders as local time, and the repeat count coalescing produces.
- `Messages/ArchiveFailureMapper` + `ArchiveFailureView` + `ConfigurationSectionFailureMapper` — the
  one place an `IError` becomes a title, a detail, a remedy and a severity (`ui-text.md`).
- `Messages/ResultReporting` — the two extension methods over the panel that write the entry and one
  log line; the log line carries the exception object when the error has one.
- `Messages/UnhandledErrorObserver` — the `IObserver<Exception>` ReactiveUI's builder takes, holding a
  panel factory and the UI scheduler (`overview.md`).
- `Localization/Resources.resx` + `Resources.ru.resx` and the generated
  `SemiPlot.UI.Localization.Resources` - every string the operator reads, the startup failure window
  included, in two sets the `locale` key of the `app/` section selects between.
  `Microsoft.CodeAnalysis.ResxSourceGenerator` writes the accessor at compile time; C# reads
  `Resources.Key` and AXAML `{x:Static text:Resources.Key}`. The delta labels and the no-value
  placeholder live there because the source is ASCII. What stays a literal, and why, is in
  `ui-text.md`.
- `Startup/StartupFailureWindow` + `StartupFailureViewModel` - the window a failed start shows instead of
  `MainWindow`: the failure text, a panel of its own and the Settings, Restart, About and Exit buttons
  (`overview.md#one-window-per-process`).
- `Startup/InstanceLauncher` - starts a copy of the process with the same launch keys for `File` -> `New window`,
  `Restart` and `Restart now` (`overview.md#another-instance`).
- `Startup/AppSettings` + `AppSettingsLoader` + `StartupSequence` - the required
  `<config-dir>/app` section, its two keys and the ordered startup steps that read it before the
  connection section (`data-integration.md`, Startup).
- `Styles/Palette.axaml` + `Chart/ChartPalette` - the two theme variants, the five ScottPlot surfaces
  painted from them, and the colours of the time markers and the ahead-break shade (`ui-theme.md#the-plot`).

**Export (`SemiPlot.Core.Export`):** `CsvDialect` (delimiter, number culture, timestamp format and a
`CsvHeader` of the three header strings; the one factory `CsvDialect.For(CultureInfo regional, CsvHeader header)`, which takes the delimiter and
decimal sign from the regional culture and falls back for a list separator other than `;`, `,`, tab or `|`, or equal to the decimal sign, `ui-text.md#the-export-windows-text`) and `ArchiveCsvWriter` (`WriteHeader`, `PenFields`, which maps a pen id to its
quoted `name, unit` field and appends ` (id)` only to pens that share a name, and `WriteRows`; RFC 4180
quoting). `SemiPlot.Core.Export.ArchiveRowSlices` holds the 15 minute slice width and the tiling of a range.

**Core models (`SemiPlot.Core.Trends`, renderer-agnostic, unit-tested):**

- `PenScaleModel` — one `PenScale` per pen: `(Min, Max)` + autoscale mode + the active flag (Auto over
  the columns inside `[windowStart, windowEnd]` / Manual; log padding and sanitize). A pen whose
  `scale_min_on_start`/`scale_max_on_start` are stored opens `Manual` on exactly those bounds; a pen
  without them opens `Auto`. A pen whose `log_scale_on_start` is set opens on a log10 axis. What the
  operator then does to the axis rewrites the settings for the session and reaches no database. The only
  writer of `semiplot_tags` is the pen editor (`Edit` -> `Pens and groups`, `PenEditor/`,
  `data-integration.md#the-pen-catalogue-editor`). The stored pair and the stored flag are the pen's
  initial scale: they build the settings when the pen enters the chart and are the target of
  `RestoreInitialScale`. `PenScaleSettings.InitialFor` builds the settings from the pair and the flag a
  pen record holds, `Manual` on both bounds or `Auto` when it has none, logarithmic when
  `Pen.LogScaleOnStart` is set; `TrendChartViewModel.RestoreInitialScale` applies them to the active pen.
  The session flag has three writers: `ChartPenSet.Add` and `RestoreInitialScale` from the stored flag,
  and `TrendChartViewModel.SetLogarithmic`, which the axis scale panel calls. Both scale commands leave
  an active pen that is switched off alone, since its axis is not drawn. A changed pair or flag reaches
  the running chart through the next catalogue read and leaves every shown pen alone, whatever session
  axis it holds (Applying a catalogue read, below).
- `LogAxis` - the log10 projection with the pinned floor, the tick choice and the log minimum rule
  (`#log10-y-axis`).
- `PenRangeGesture` — `Shift` and `Scale` of a pen's `(min, max)` in axis space (decades on a log10 axis);
  null keeps the range for a non-finite result, a span not above zero or a log minimum
  `LogAxis.AdmitsMinimum` refuses (`trend-interaction.md`, "Mouse gestures on the active pen's scale").
- `PenValueFormat` — the `0.###` fallback mask, the character rule that accepts a stored mask, and the
  render under `CultureInfo.CurrentCulture`, for the sidebar row, the hover readout and the major labels
  of a log axis. The delta readout's Δy goes through `FormatDifference`, which keeps only the mask's
  positive section without its sign literals: a difference is often negative and often zero, a reading's
  negative or zero section (a sign left out, a dash for no reading) would misstate it, and a literal `+`
  or parenthesis would print after the minus .NET puts ahead of a negative value. The rule runs once, in `PostgresDataProvider.ReadPen`,
  which is where the logger is; a rejected mask reaches the record as `null`, so `Pen.Format` in the
  row is always usable and the row neither validates nor logs. The rule is a character set rather than
  a `try`/`catch` because .NET throws on almost no bad mask: `qqq` prints literally and `%0.0`
  multiplies the reading by 100.
- `TrendNavigationModel` — `[from, to]` window, sticky flag, zoom width; pan / zoom / jump-to-now /
  live-edge advance, clamped 1 s … 1 year, zoom width quantized onto a 1.25 ladder, `From ≥ FirstSample`.
- `MinimapGeometry` — extent + window → strip start/width fractions, and fraction → timestamp;
  `MarkerSpan` → the marker's left edge and width, clamped inside the strip; `RightBound` → the later
  of the extent's last sample and the newest sample; `BandFigures` → one outline and centre line per
  run of finite envelope columns, scaled to the pen's own range. The band's next-read delay is the
  read schedule's own, `Minimap/MinimapBandFeed.NextReadDelay`.
- `CursorReadoutModel` — cursor X → per-pen interpolated `Center` value (gaps → no value).
- `DeltaCursorModel` — two cursor times → `DeltaReadout` (Δt + Δy for the active pen).

## Applying a catalogue read

A running chart follows the stored catalogue (`overview.md#the-live-catalogue`).
`Core/Trends/PenListDelta.Between` compares two reads: `Added`, `RemovedPenIds`, `Revised` (the new record of
each pen whose stored settings differ) and `Current`, the new read. `IsEmpty` is true when nothing was
added, removed or revised, so a new order alone is no change.

`TrendChartViewModel.ApplyCatalogue(catalogue)` takes the read itself, `delta.Current`, and compares it
with the pens the chart shows, not with the read before it. `ChartPenSet.Apply` computes that
comparison with `PenListDelta.Between` and applies it, so the chart ends up showing exactly the read
whatever baseline the loop compared it with (`overview.md#what-a-read-changes`). In this order, under
`lock (Plot.Sync)` (`#per-pen-plottable-envelopeline`, the plot's own lists):

1. Every pen the read does not name leaves the dictionaries, the plot and its envelope, and its axis is
   hidden. The axis stays keyed on the pen id, so a pen that comes back takes it again.
2. Every pen the chart holds whose stored settings differ is revised: `TrendPenState.Revise` takes the
   new pen and restyles its line in place. A revision never touches the pen's scale settings, so a changed
   stored pair or `log_scale_on_start` leaves the session axis alone, and an `EnabledOnStart` revision
   keeps the visibility.
3. Every pen the chart lacks joins, with the visibility its `EnabledOnStart` gives it.
4. `Pens` is rebuilt in the order of the read.
5. The active pen settles: a removed active pen's slot goes to the first visible pen in catalogue order.
6. `ApplyAxisModel` once, so a read that adds 500 pens computes the axis model once. A read that
   removes every pen computes it over no pen, which clears the stored scales and moves
   `ScalesRevision` on like any other.

After the lock, the visible pen count goes to `Navigation.SetVisiblePenCount`, which may move the
layer (`data-integration.md#layer-ladder`). Then the live edge follows the pens shown, hidden ones
included: when their id set differs from `TrendCoordinator.PenIds`, the set the live edge follows
(`data-integration.md#realtime`), the chart hands the new one to `SetPens`. Compared with that set
rather than with the read before, an apply that threw before this point is caught up by the next one,
and the start sequence, which applies the pens the coordinator was built with, opens no second live
subscription. Once the first history request has gone out (`RequestInitialHistory`), a set change also
drops the last fetch and issues one history query for every visible pen. A pen added after the first
fetch otherwise gets no history from a window already read. The switch itself writes no break into the
pens that stay: their `TrendPenState` stays joined (`#per-pen-plottable-envelopeline`). Before the first history request, that request covers every visible pen, so the
start sequence seeds the chart through the same `ApplyCatalogue` and reads nothing of its own. One read
can leave before it: a visible pen count that moves the layer raises `WindowChanged`, which reads the
window as any window change does, and the debouncer's 150 ms throttle merges that read with the first
request, which `TrendWindow.Build` pushes in the same synchronous step. `Pens` and `HasNoPens` are
raised, and `RequestRedraw` follows. No production code adds or removes a pen but a catalogue applied.

A pen removed and named again before the removal's read applied is read again too. Its request names
the same pens and window as the read applied before the removal, but the chart pushes it after that
read was delivered, and the debouncer reads every such push. A result for a read issued before the
change sets no fetch either, since it holds no row of an added pen, and the window in view is asked for
again (`data-integration.md#what-one-history-query-covers`).

A history result that lands after a delta removed a pen carries an envelope for it, because the query
was issued before the removal. `ApplyHistory` skips every envelope whose pen the chart no longer
holds, so a removed pen's rows never outlive it and a pen that comes back reads its history afresh.

A hidden pen is never read. Every history request names the visible pens alone
(`data-integration.md#what-one-history-query-covers`), so a pen joining hidden, and a pen switched off,
keeps its last envelope without drawing it; the cursor and hover readouts skip it. That envelope can
belong to a window the chart has since left.

`TrendChartViewModel.SetPenVisibility` switches a pen on in this order:

1. A pen missing from the pens read for the range in hand is cleared, also when no range is in hand:
   its envelope is dropped and its columns are cleared with the right edge of the range in hand as the
   end, or the window end when none is, so the live tail the source appended inside the range survives and
   no curve from another window is drawn or read by the cursor.
2. The active pen settles on a visible pen.
3. The new visible count goes to `Navigation.SetVisiblePenCount`. A count that moves the layer raises
   `WindowChanged`, whose handler reads the window in view at the new layer, applies the axis model and
   redraws, and the switch ends there.
4. Otherwise the request decision runs. While the range in hand covers the window in view, the pen is
   read alone over that range; otherwise the window in view is read for every visible pen. The axis model
   is applied once and a redraw follows.

A pen switched off and on again while the range in hand still holds it is not cleared and reads
nothing. Switching a pen off reads nothing either, unless the smaller count moves the layer. A failed
read of a pen switched on leaves it cleared and unread for the range in hand, so the next window
change asks for it again.

A chart that started empty takes its first pens through the same path. `PenCatalogueApplier` reads the
archive extent first and seeds `Navigation.SeedFromArchiveExtent` with it before the delta applies, as a
start with pens does, then hands it to `WidenToArchiveExtent`: a chart that lost every pen latched its
first sample at start, so only the widen moves it. A delta that adds a pen to a chart that already had
some reloads the minimap's extent after it, and `WidenToArchiveExtent` moves the pan floor back to an
earlier first sample that extent carries, without moving the window. When the floor moved and history
has started, the chart asks for the window in force through the same `Covers` gate as any request: a
range clamped at the old floor no longer reaches the first sample, so a window starting before the old
floor is read again, while a window far from either floor reads nothing (`overview.md#what-a-read-changes`).

## Data contract (UI ↔ provider)

The viewer consumes data only through `IDataProvider` and, for the live edge, `ILiveSource` (see
data-integration.md), in-process and strongly typed — **no JSON message bridge**. `TrendCoordinator` is
the Rx hub between the two and the view model:

- **History:** `QueryHistoryAsync(penIds, from, to, layer, targetColumnCount)` is the single history
  query, returning one `PenHistoryEnvelope` per pen (ascending `Timestamps` + `Min` + `Max` +
  `Center`; NaN = gap). The initial load and every gesture re-query go through
  `ChartHistoryRequestDebouncer`, which runs one query at a time and lets the newest window asked
  for run last. The minimap band is a second reader outside the debouncer: one pen at 250 columns
  every 2-60 s (`trend-interaction.md#archive-overview-minimap`).
- **Realtime:** the live edge comes through `ILiveSource` as lists of `Sample`, and the coordinator
  republishes it as `IObservable<RealtimeBatch>` — an ascending union timeline the live edge advances
  from, plus one `PenRealtimeValues` per pen carrying that pen's **own** timestamps and `double?`
  values; buffered on the data scheduler and observed on the UI scheduler. The values are per pen
  rather than a column over the union because the OPC UA server notifies per variable and on change,
  so one buffer window routinely spans timestamps only one pen sampled. A pen carries no
  entry, null or real, at a timestamp it did not report, and a consumer leaves it alone there. A null
  is a break the live source reported, a `Sample` whose `Value` is null: the pen has no data from that
  moment until its next sample, and the chart appends a `NaN` column there. `OpcUaLiveSource` writes one
  per delivered pen when its session is lost and one for a `Bad` notification
  (`data-integration.md#the-breaks-the-source-writes`). The pen
  set the stream carries follows the catalogue: `TrendCoordinator.SetPens` switches the live-source
  subscription and `RealtimeBatches` stays one published stream (`data-integration.md#realtime`).
- **Archive extent:** `QueryArchiveExtentAsync()` returns an `ArchiveExtent(FirstUtc, LastUtc)` —
  the full stored time span. `TrendCoordinator.QueryArchiveExtentAsync()` is a pass-through to the
  provider (mirroring `QueryHistoryAsync`); the minimap consumes it (see trend-interaction.md).
- **Archive days:** `QueryArchiveDaysAsync(firstDay, lastDay, ct)` returns the local days of the range
  that hold a layer-0 row of a catalogue pen. `TrendCoordinator.QueryArchiveDaysAsync` is a pass-through
  to the provider; the time window panel reads it once per open and the export window once per window for
  their calendars' blackout days (`trend-interaction.md#the-time-window-panel`).
- **Connection state:** `Bridge/PenCatalogueSync.ConnectionStates` carries the
  `ArchiveConnectionState` its catalogue reads show, on the UI thread; the provider carries none.
  `MainWindow/AppStatusBarViewModel` binds it and shows it in the status bar,
  over a chart that keeps its history; a fault becomes one message-panel entry
  (`data-integration.md#the-connection-state-the-catalogue-read-reports`). `PenCatalogueSync.Dispose`
  completes the stream. The live source's session state reaches the bar's second indicator through
  `TrendCoordinator.LiveSourceStates`, and its pen faults reach the panel through
  `TrendCoordinator.LivePenFaults`, both the source's streams observed on the UI scheduler
  (`data-integration.md#the-session-state-the-live-source-reports`).

These records are the plottables' input shape after the view model maps them onto `EnvelopeColumn`
buffers; there is no serialization step.
