# Live hold to the edge

## Overview

- Two render-only marks at the live edge, the last slice of the OPC UA live edge roadmap.
- The break the chart writes ahead of a pen's first live value (the ahead-break) is shaded grey and labelled
  "No archive data when the window opened". A history read that covers it drops it, as today; nothing fills it
  on its own. A break the source writes (a lost session, a `Bad` status) keeps the ordinary look, so the break
  column carries a kind.
- While the OPC UA session is up, each visible pen that the live stream has confirmed is drawn held from its last
  column's value to the chart's newest sample. A constant or rarely changing pen then reaches the edge instead of
  stopping at a row that can be a day old. The hold stops at a break and is off while the session is down.
- Neither mark is a stored column. The cursor and hover readouts read the pen's `CurrentValue` inside the held
  span; `CurrentValue` stays the one owner of a pen's reading.
- One live-edge rule tightens with it: after a source break, a live value the line rejects re-confirms the hold
  but not the append, so the next value past the last column opens behind an ordinary break and no line crosses
  the outage.
- Thesis: while the OPC UA session is up, every point past a pen's newest archived row comes from OPC UA. The
  archive replaces a live point only when a history read covers it, under the rule `EnvelopeLine.ReplaceColumns`
  applies today.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice live-hold-to-edge

- Verified at `master` `5f8ea2c`. Paths are relative to the repository root. Exec starts from the then-current
  `master`, after `opc-settings` merges; that slice touches both resx files, the settings files and docs, so exec
  rebases the resx entries and re-reads any cited line that moved.
- The slice as the roadmap states it: `docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md:275-293`. Settled
  forks this plan relies on: the grey span carries no promise of a fill (`:337`), `Bad` breaks and `Uncertain`
  draws (`:337-338`), the live edge follows samples and not the machine clock (`:321`), auto-fill of the grey span
  is rejected (`:312-313`).
- A column is `EnvelopeColumn(double X, double Min, double Max, double Center)`
  (`SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs:4`). A gap is a column whose `Min` or `Max` is `NaN`
  (`SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:293-296`). Nothing tells the chart's ahead-break from any other
  `NaN` column today.
- The ahead-break is written in `TrendPenState`: `AppendRealtime` and `FoldRealtime` call
  `Line.AppendAfterBreak(AheadBreak(timestampUtc), sample)` while `_isJoinedToLive` is false
  (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:11`, `:82-111`); `AheadBreak` is a `NaN` column `Sample.BreakStep`
  ahead of the value (`:122-125`). A null from the source appends a `NaN` column and unjoins (`:84-89`); a history
  apply that dropped a live column unjoins (`:138-146`); a value, taken or not, joins (`:113-120`).
- The same `AheadBreak` call also writes the `NaN` column that stands in for a rejected source break: a source
  break at or before the last column is rejected, the line unjoins, and the next value past the last column
  opens behind a chart `NaN` column (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs:247-262`). That
  gap covers an outage and must keep the ordinary look.
- After a source break, a recreated session re-delivers an unchanged pen's value with its original stamp, before
  the break (`docs/architecture/data-integration.md:1352-1357`). For a pen whose break was rejected because the
  archive holds a later row (`:1087-1090`), the line rejects that replay, and `TakeLiveValue(_, false)` rejoins it
  (`TrendPenState.cs:113-120`); its next change then appends through `AppendColumn` with no break, a straight line
  across the outage, the line the unjoin exists to prevent (`docs/architecture/charting.md:150-154`).
- `EnvelopeLine.AppendAfterBreak` writes the gap only past a last column that is not a gap
  (`EnvelopeLine.cs:221-237`); `ReplaceColumns` keeps every column past the old and new history end and drops
  those past the requested end, and reports a drop only for a live column (`:196-218`); the cap trims from the
  oldest end (`:298-313`). A field on the column therefore travels with it through every one of these.
- Render reads the visible columns under `_renderStateLock` and strokes outside it into the reused
  `_pathPoints` (`EnvelopeLine.cs:85-117`); `DistanceTo` reads the same `VisibleRange` and `Build` vertices under
  the same lock (`:120-149`). The colour, style and width sit under that lock and are written by lock-taking
  setters (`:177-192`).
- `EnvelopePath.VisibleRange` keeps one column beyond each edge (`SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:15-29`),
  so a last column left of the window is still handed to `Build` as the range `[Count - 1, Count)`. `Build`
  exits a column with `Min != Max` at whichever edge it did not enter at (`:66-74`), so the path's last Y is not
  the column's `Center` for such a column.
- `FoldIntoLastColumn` keeps the column's X and moves its `Center` (`EnvelopeLine.cs:249-274`); at a coarse layer
  the first live value behind an ahead-break opens a column at its own X and later values fold into it
  (`docs/architecture/charting.md:166-171`, `TrendPenStateTests.cs:286-300`).
- The newest sample: `ChartNavigationController.NewestSample` is "the newest sample the chart has seen, from the
  archive or the live edge" (`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:46-47`), written by
  `TrackDataExtents` (`:114-133`) and `OnLiveEdge` (`:179-200`), monotone (`:202-211`), and announced by
  `NewestSampleMoved` (`:55-59`). `ChartRealtimeApplier.Apply` hands it the batch union's last timestamp
  (`SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs:9-17`); the union holds every sample of the window, breaks
  included (`SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:142-154`).
- The session state: `ILiveSource.States` is hot and replays the current state to each subscriber
  (`SemiPlot/SemiPlot.Core/Data/ILiveSource.cs:11-12`); `LiveSourceState.IsConnected` (`LiveSourceState.cs:18`).
  The source publishes `Connecting` only at start (`data-integration.md:1241`).
  `TrendCoordinator.LiveSourceStates` is `_liveSource.States.ObserveOn(_uiScheduler)` (`TrendCoordinator.cs:43`).
  `TrendChartViewModel`'s constructor already subscribes three coordinator streams into `_disposables`
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:81-86`), and the class is 871 lines.
  `data-integration.md:1270-1271` names this slice as a reader of the same `States`.
- The readouts read the history envelopes only: `ChartCursorReader.ReadAt` and `ReadPenAt` read `envelopesById`
  (`SemiPlot/SemiPlot.UI/Chart/ChartCursorReader.cs:11-29`), which only `ApplyHistory` writes
  (`TrendChartViewModel.cs:690`), and `CursorReadoutModel.ReadPenAt` returns null past the envelope's last
  timestamp (`SemiPlot/SemiPlot.Core/Trends/CursorReadoutModel.cs:29-32`). `MoveCursor` fills `CursorValues`
  from `ReadAt` (`TrendChartViewModel.cs:403-414`), which the hover readout prints
  (`SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs:833`); the marker readings read `ActivePenReadingAt`, which
  is `ReadPenAt` (`TrendChartViewModel.cs:417-420`). The delta readout reads the envelope through Core's
  `DeltaCursorModel` (`SemiPlot/SemiPlot.UI/Chart/ChartDeltaCursorReader.cs:30-36`).
- `CurrentValue` is set from the last non-gap `Center` after a history apply (`TrendPenState.cs:145`) and from
  every taken live value (`:113-120`), so it equals the last column's `Center` whenever the line ends on a value.
- The precedent for a plottable the view adds and feeds per frame: `ChartMarkerReadings`, its own lock over its
  state (`SemiPlot/SemiPlot.UI/Chart/ChartMarkerReadings.cs:17-20`), `Update` from `ServeFrame`
  (`TrendChartView.axaml.cs:301-319`, `:388-394`), added under `lock (Plot.Sync)` and removed on a data-context
  change (`:350-367`), recoloured from `ApplyChartPalette` (`:161-183`). It shows the active pen's readings only,
  "so many pens do not crowd it" (`docs/architecture/trend-interaction.md:544-545`). Its `Update` holds
  `_readingsLock` while it calls `readAt` (`ChartMarkerReadings.cs:61-74`); its `Render` takes no line lock
  (`:95-117`).
- The marker labels hang inside the data area's top edge (`SemiPlot/SemiPlot.UI/Chart/ChartMarkerLines.cs:160-171`)
  and a label that does not fit before the next line is hidden (`:66-84`). The hover readout hangs from the
  crosshair's top end (`trend-interaction.md:307-313`). A plottable is pinned to the shared bottom axis at creation
  (`ChartPenSet.cs:94`, `ChartMarkerLines.cs:163`).
- ScottPlot 5.1.59: `Plot.MoveToBottom(IPlottable)` renders a plottable first, below all others
  (`ScottPlot.xml:3116-3119` in the 5.1.59 package); `LabelStyle.Measure(Paint)` returns the size of its text
  (`ScottPlot.xml:5303-5306`) and needs a `Paint`, which a plottable holds only inside `Render`;
  `FillStyle.Render(RenderPack, PixelRect)` fills a rectangle (present in `ScottPlot.dll` 5.1.59).
- The palette: `ChartPalette.Resolve` copies the brush's ARGB and ignores `Opacity`
  (`SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs:55-63`, `docs/architecture/ui-theme.md:443-447`); the app's own keys
  sit at `SemiPlot/SemiPlot.UI/Styles/Palette.axaml:81-95` (Light) and `:166-180` (Dark); `ui-theme.md:151` counts
  them ("fifteen keys").
- Test seams: `FakeDataProvider` seeds `LiveSourceState.Connected` (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:38`)
  and exposes `PushLiveState` and `PushLiveSamples`; `ChartTestBuilder.CreateCoordinator` gives the coordinator an
  immediate UI scheduler (`SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs:57-60`), so a default-built
  chart sees `Connected` at construction. Live samples land only after the test scheduler passes
  `TrendCoordinator.BatchWindow` (`TrendCoordinator.cs:15`, `:111`). A chart with no realised view never moves the
  plot's X limits, so a render test sets them (`ChartGapRenderTests.cs:128-146`). `StrokeProbe` reads chroma
  (`StrokeProbe.cs:17`), so a grey shade is invisible to it; a shade test diffs against the data background.
- The stand's writer runs `--archive-lag 60` (`SemiPlot/SemiPlot.AppHost/AppHost.cs:25`, `:58-61`).
- The OPC journey asserts the ahead-break column by shape (`SemiPlot/SemiPlot.Tests.Integration/Journeys/OpcUaLiveEdgeJourneyTests.cs:120-135`)
  and runs in `DemoScadaCollection`, with no container.
- The roadmap's close condition reads each slice's `Status`, `Plan`, `PR` and `Branch` (roadmap `:295-298`); the
  slice `opc-settings` is `PENDING` at `5f8ea2c` and also edits both resx files (roadmap `:258-273`).

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task), with one red-first step: in Task 3,
  `ChartLiveHoldRenderTests.AConfirmedSlowPen_IsDrawnHeldToTheNewestSample` is written and confirmed red before the
  wiring lands; it uses only API that exists at `5f8ea2c`.
- Start on a branch off the remote tip (`git fetch origin && git switch -c live-hold-to-edge origin/master`); the
  pre-commit hook refuses `master` (`AGENTS.md`, Build).
- Complete each task fully before moving to the next. Each task leaves `dotnet build SemiPlot.slnx` at 0 errors
  and no warning beyond the AppHost's two `NU1902`.
- Every task includes new or updated tests for the code it changes; all tests pass before the next task.
- `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files pass; a new
  `.cs` file gets one `dotnet format SemiPlot.slnx --include <file>` (`AGENTS.md`, Build).
- An existing test that fails because a session-up chart now draws or reads a held segment is examined, not
  loosened: it is updated only when its scenario has the session up and a confirmed pen, and the plan records which.
- Update this plan when scope changes during implementation.

## Testing Strategy

- **Unit** (`SemiPlot.Tests.Unit`): the break kind and the join rule (`TrendPenStateTests`, `EnvelopeLineTests`);
  the hold and span geometry (`EnvelopePathTests`, `EnvelopeLineTests`); the hold's session and edge wiring and
  the readouts (`TrendChartLiveHoldTests`, new); pixels of the hold (`ChartLiveHoldRenderTests`, new) and of the
  shade (`ChartAheadBreakShadeTests`, new); the palette key (`ChartPaletteTests`); the view wiring
  (`TrendChartViewTests`); the marker reading in the held span (`ChartMarkerReadingsTests`). Pure geometry is plain
  `[Fact]`; anything touching ReactiveUI, ScottPlot or Avalonia is `[AvaloniaFact]`. A test that reads a
  `Resources` value joins `ProcessGlobalStateCollection` (`docs/architecture/ui-text.md:489-493`).
- **Integration**: `OpcUaLiveEdgeJourneyTests` gains the kind assertion; it needs no container.
- Every new test class carries the three traits (`Component`, `Area`, `Category`).
- No e2e suite exists. The stand check is in Post-Completion.

## Acceptance Evidence

The defect at `5f8ea2c`, read from the code: a constant pen's line ends at its last column
(`EnvelopePath.cs:43-75` draws nothing past the last column), so on the stand a pen whose archive row is a day old
stops a day short of the edge; the ahead-break and the `NaN` column standing in for a rejected source break are
the same `NaN` column (`TrendPenState.cs:122-125`, pinned by shape only in `TrendPenStateTests.cs:163-181` and
`:247-262`), so no renderer can shade one and not the other; the hover readout prints a dash for every pen past its
envelope's last timestamp (`CursorReadoutModel.cs:29-32`); and a rejected replay after a rejected source break
rejoins the line (`TrendPenState.cs:113-120`), so the pen's next change appends with no break across the outage.

Automatable, after the slice:

1. Red at `5f8ea2c`, green after Task 3:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ChartLiveHoldRenderTests"`.
   `AConfirmedSlowPen_IsDrawnHeldToTheNewestSample` builds a chart through `ChartTestBuilder` with the session
   `Connected` and two pens, then:
   1. loads history so the slow pen's last column sits at a third of the window;
   2. pushes a live sample for the slow pen at its last column's stamp, which the line rejects and which confirms
      the pen;
   3. pushes a live sample for the second pen at the window's end, which moves `NewestSample` there;
   4. advances the test scheduler past `TrendCoordinator.BatchWindow`;
   5. sets the plot's X limits to the window and renders through `Plot.GetImage`.

   It finds the slow pen's colour (`StrokeProbe.RowsIn`) in the pixel column halfway between its last column and
   the window's end, on the row of its last value. After `PushLiveState` with an `Unreachable` fault and a second
   render, the same probe finds no pen colour there.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendPenStateTests|FullyQualifiedName~EnvelopePathTests|FullyQualifiedName~EnvelopeLineTests|FullyQualifiedName~TrendChartLiveHoldTests|FullyQualifiedName~ChartAheadBreakShadeTests|FullyQualifiedName~ChartPaletteTests|FullyQualifiedName~ChartMarkerReadingsTests|FullyQualifiedName~TrendChartViewTests"`
   passes, including:
   - the ahead-break at window open, on an empty line and after a history apply that dropped a live column carries
     `BreakKind.Ahead`; a source break, and the `NaN` column standing in for a rejected source break, carry
     `BreakKind.Ordinary`;
   - a rejected source break followed by a rejected replay at the original stamp restores the hold, whose value
     equals `CurrentValue`, and the next value past the last column still opens behind an `Ordinary` `NaN` column;
     with no replay no hold is drawn;
   - a value stamp older than every archived row confirms the pen, and the hold stays through a pan read that
     returns fewer rows;
   - at a coarse layer the ahead-break's span ends at the first live column's X after two more values fold into
     that column;
   - a confirmed pen is held to `NewestSample` while the session is `Connected`; no hold for an unconfirmed pen, a
     line ending on a gap, the initial `Connecting` state, a faulted session, a pen added by a later catalogue read
     before its first live value, or a pen confirmed by rejection whose newest archived row a history apply cut;
   - `DistanceTo` a click on the held segment is 0;
   - the cursor inside the held span reads the pen's `CurrentValue`, and past the hold end it reads no value;
   - the shade fills the drawn pen's ahead-break span and nothing outside it, draws its label when the span is
     wider than the label and no label when it is narrower, and draws nothing for an ordinary break.
3. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUaLiveEdgeJourneyTests"`
   passes with no container runtime: a value served over OPC UA reaches the chart behind a `BreakKind.Ahead`
   column.
4. These print what is stated:
   - `git grep -n "_isJoinedToLive" -- SemiPlot docs/architecture` prints nothing;
   - `git grep -n "live-hold-to-edge" -- docs/architecture` prints nothing;
   - `git grep -n "SetHoldEnd(" -- SemiPlot/SemiPlot.UI` prints the declaration in `Chart/EnvelopeLine.cs` and one
     call, in `TrendPenState.ApplyHold` (`Chart/TrendPenState.cs`);
   - `git grep -n "BreakKind.Ahead" -- SemiPlot/SemiPlot.UI` prints lines of `Chart/TrendPenState.cs` (the one
     writer) and `Chart/EnvelopePath.cs` (the one reader) only;
   - `git grep -nE '#[0-9A-Fa-f]{6,8}\b|"(Gray|Black|White)"' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'`
     prints nothing (`ui-theme.md:490-492`).
5. `dotnet build SemiPlot.slnx` at 0 errors and no warning beyond the AppHost's two `NU1902`;
   `dotnet test SemiPlot.slnx` green on both CI legs; `dotnet format SemiPlot.slnx --verify-no-changes` clean.

Manual (the look of the grey, the label in both languages, the hold on the stand): Post-Completion.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix.
- Document issues or blockers with ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

The kind rides on the column, the hold is a few extra vertices the path builder adds past the last column, and
the shade is one plottable under every pen line that reads the drawn pen's ahead-break spans. The session state
and the newest sample reach the lines through one small owner, `ChartLiveHold`.

### Where the break kind lives

**[decision] On `EnvelopeColumn`, as `BreakKind Break = BreakKind.Ordinary`, in `SemiPlot.UI/Chart`.** The
ahead-break is the chart's own column (`data-integration.md:1065-1071`); the source's breaks are all ordinary, so
Core's `Sample` and `RealtimeBatch` stay as they are. On the column the kind travels through `ReplaceColumns`' kept
tail, its drop once a read covers the column, and the cap trim, with no second list to keep in step with the
columns. The default keeps every existing `new EnvelopeColumn(x, min, max, center)` compiling and every column the
history writes ordinary. The kind means something on a gap column only. Cost: the struct grows from 32 to 40
bytes, 0.8 MB per pen at the 100 000-column cap.

**[decision] `TrendPenState` decides the kind from the reason the line is unjoined.** `_isJoinedToLive` becomes
`LiveJoin { Unjoined, UnjoinedBySourceBreak, ReconfirmedAfterSourceBreak, Joined }`. A new state and a history apply
that dropped a live column set `Unjoined`; a null from the source sets `UnjoinedBySourceBreak`. `AheadBreak` writes
`BreakKind.Ordinary` while the line is `UnjoinedBySourceBreak` or `ReconfirmedAfterSourceBreak`, and
`BreakKind.Ahead` otherwise. The reason is the rejected source break: its outage is drawn by the chart's `NaN`
column, and that gap must keep the ordinary look. The latest unjoin wins: a source break followed by a history apply
that dropped live columns gives a grey span, because the live run the outage cut is gone and the span is one the
archive had not reached.

**[decision] A rejected replay after a source break re-confirms the hold; the next append still opens behind an
ordinary break.** A recreated session re-delivers an unchanged pen's value at its original stamp, before the break
(`data-integration.md:1352-1357`); the server is stating the pen's current value, so holding it to the edge is
truthful. So a rejected value moves `UnjoinedBySourceBreak` to `ReconfirmedAfterSourceBreak`, where the line is held
as a joined one. It does not move it to `Joined`: the next value past the last column still goes through
`AppendAfterBreak` with an `Ordinary` `NaN` column ahead of it, so no line crosses the outage, the line master draws
today when the rejected replay rejoins it (`TrendPenState.cs:113-120`, `charting.md:150-154`). A taken value then
sets `Joined`. A session the stack reactivates re-delivers nothing for an unchanged pen
(`data-integration.md:1351-1352`), so that pen stays unjoined and unheld until its next change; a hold there could
draw a stale value, because a change made inside a short cut never arrives
(`docs/plans/completed/20261020-opc-live-source.md:166`). Closing that gap needs a read of current values after a
reactivation, a source change outside this slice; `docs/plans/backlog.md` records it.

**[decision] The ahead-break after a pan into the past and back is grey too.** It marks the span between the
archive's newest row and the pen's next live value (`data-integration.md:1091-1098`), the same kind of span as at
window open. The label keeps the settled wording, which names the common case and reads loosely here.

### The hold

**[decision] The hold end X is `ChartNavigationController.NewestSample`.** It is the newest stamp the chart has
seen, from the archive seed or a live batch (`ChartNavigationController.cs:46-47`, `:179-200`), and the sticky
window's right edge follows the same stamps, so a held pen reaches the right edge of a following window. The
machine clock is rejected (`roadmap:321`); the window's `To` is rejected because a zoom moves it without a sample.
A hold end at or before the pen's last column draws nothing.

**[decision] The hold learns the session state from `TrendCoordinator.LiveSourceStates`, in the chart view
model.** The stream is already observed on the UI scheduler and replays the current state (`TrendCoordinator.cs:43`,
`ILiveSource.cs:11-12`); the status bar reads the same stream and stays the indicator's one writer.
`TrendChartViewModel` subscribes beside its three coordinator streams and hands `IsConnected` to
`ChartLiveHold.SetSessionUp`, then requests a redraw. `ChartLiveHold` (`Chart/ChartLiveHold.cs`, owned and disposed
by the view model) keeps the session flag, follows `Navigation.NewestSampleMoved`, and in `Apply()` hands every pen
`NewestSample` as an axis X while the session is up and a sample exists, `NaN` otherwise. It is the one writer of
the edge X. The view model calls `Apply()` after every catalogue apply, so a pen a later read adds takes the
current edge; a moved newest sample comes from a batch or a history apply, each of which redraws already. The
class keeps the wiring out of the 871-line view model, as `ChartRealtimeApplier` and `ChartCursorReader` do.

**[decision] A pen is held only while the live stream has confirmed its last column.** The hold says the OPC UA
stream confirmed the value up to the edge. For every live value that reaches the line and is allowed to join it,
`TrendPenState` calls `Line.ConfirmLive(valueX)`, which keeps the confirmed X under the column lock as the smaller of
the value's X and the last column's X after the append or fold: a taken append confirms its own column, a fold at a
coarse layer confirms the bucket it widened (whose X is its opening X), and a rejected value confirms the moment it
carries. The pen state hands the line the hold end while the line is `Joined` or `ReconfirmedAfterSourceBreak`,
`NaN` otherwise. The line holds only when its last column is a value whose X is at or past the confirmed X. So:
- an unserved pen (unknown node, non-numeric) never joins, and its archive row is never drawn as live;
- a pen before its first live value is not held over the span it may still break;
- after a source break nothing is held until the server states the pen's value again: a rejected replay from a
  recreated session restores the hold, a reactivated session that re-delivers nothing leaves the pen unheld until
  its next change, and a line that ends on the appended break holds nothing in either case;
- after a history apply that dropped live columns the pen is not held until its next live value, because the chart
  then holds no live confirmation past the archive's newest row;
- a constant pen joins on its initial notification, which the line rejects as inside the archive's span, and is
  held from its archive row. A pan read whose last column stays at or past the confirmed stamp keeps the hold (a
  stamp older than every row keeps it through any read that returns a row after it); a read that cuts back before
  the stamp leaves no hold, so no hold draws an older value across the change, and the hold returns when a read
  brings the rows back.

The rule is read under the column lock at every frame, so a history apply needs no extra write.

**What a constant pen's hold shows.** The held value is the last column's `Center`, whatever the archive wrote
there, the by-time row that sometimes carries the previous value included (roadmap `:306-307`). It is the value
`CurrentValue` holds, so the held line, the readouts inside the held span and the sidebar agree. The demo-SCADA
comparison in Post-Completion checks it against the SCADA's own display.

**[decision] The hold is drawn by `EnvelopePath.Build`.** `Build` gains the confirmed X and the hold end. When the
range reaches the last column and `Hold` gives a span, `Build` adds `(lastX, Center)` when the path's last Y differs
and then `(holdEndX, Center)`: at most two points into the reused list, no allocation, under the column lock as
today. `Center` is the value `CurrentValue` holds, so the held line and the readouts agree; a degenerate last column
(every Raw live column, and a Raw row whose `Min` equals its `Max`) needs only the horizontal point. `DistanceTo` shares
`Build`, so a click on the held segment picks the pen. The hold is no column: `GetAxisLimits`, `ReplaceColumns`,
the readouts' envelope and the autoscale never see it.

**[decision] One write path for the hold end.** `TrendPenState.ApplyHold()` holds the single
`Line.SetHoldEnd(isHeld ? edgeX : NaN)` call; `HoldTo(edgeX)` and a change of `LiveJoin` call it, each
change-guarded so a value that changes nothing takes no extra lock. The confirmed X lives in the line because only
the line knows its last column's X after a fold. `EnvelopeLine.ReadHeldSpan()` returns the span and its value
(`HeldSpan(FromX, ToX, Value)`, null when nothing is held) under the same `Hold` rule; `ChartCursorReader` reads
its bounds, and the pen-state tests compare its `Value` with `CurrentValue`.

**[decision] Constructor order.** `TrendChartViewModel` assigns `_penSet` and `_liveHold` before it subscribes
`LiveSourceStates`: the source's `BehaviorSubject` replays the current state at once, and under the tests'
immediate UI scheduler (`ChartTestBuilder.cs:57-60`) the handler runs inside `Subscribe`.

### The readouts

**[decision] Inside the held span the cursor and hover readouts read `CurrentValue`.** `ChartCursorReader` reads a
visible pen's envelope first, as today; when that gives no value and `x` lies inside `Line.ReadHeldSpan()`, it reads
`state.CurrentValue`. The held span is `[lastX, holdEndX]` under the same `Hold` rule `Build` draws. `ReadAt` and
`ReadPenAt` share the private method, so the marker reading at a marker in the held span equals the hover value
there (`ChartMarkerReadingsTests.cs:65-79` pins that equality). `ReadAt` adds a pen's key when the pen has an
envelope, as today, or a held value at the cursor.

**[decision] Δy in the held span is not built in this slice; it stays a backlog entry, as does the dash over the
live run.** The readouts read the
history envelope only (`ChartCursorReader.cs:11-29`, `TrendChartViewModel.cs:690`), and the delta readout reads it
through Core's `DeltaCursorModel` (`ChartDeltaCursorReader.cs:30-36`). So the live run between the envelope's end and
the held span reads a dash, and a delta cursor in the held span reads no Δy while the hover at the same X prints a
value. Both predate this slice in the same root, a readout source that is not the drawn line, and the fix is one
change: the readouts read the line. `docs/plans/backlog.md` records it with these facts.

**Lock nesting on the UI thread.** `ChartMarkerReadings.Update` holds `_readingsLock` while `readAt` reaches
`ReadHeldSpan()`, which takes the line's lock. That is safe because no path takes a line's lock and then
`_readingsLock`: `ChartMarkerReadings.Render` projects through `PixelOf`, which takes none. The architecture docs
state the order, readings then line, shade then line, so no later change adds the reverse.

### The shade and its label

**[decision, taken on the operator's behalf; the operator may veto it] One shade plottable for the drawn pen,
under every pen line.** The brief shades "a pen's" ahead-break and does not say which pens. Per-pen fills inside
`EnvelopeLine.Render` would paint over the pens drawn before them, and sixteen translucent spans over nearly the
same range would stack darker and want sixteen labels. A union over every visible pen would grey a range where most
pens draw archive rows. The shade follows the drawn pen (the active pen while it is visible), as the axis and the
marker readings do, so picking a pen moves the shade to that pen. `ChartAheadBreakShade` is an `IPlottable` the view
creates with `Axes.XAxis` pinned to `Plot.Axes.Bottom`, adds under `lock (Plot.Sync)` and moves to the bottom with
`Plot.MoveToBottom`, feeds the drawn pen's line from `ServeFrame` and recolours from `ApplyChartPalette`: the
`ChartMarkerReadings` pattern. Every other pen's ahead-break draws as an ordinary gap.

**[decision] A span runs from the last value column before the ahead-break to the first value column after it.**
Adjacent `NaN` columns, such as a history anchor right before a kept ahead-break (`charting.md:192-194`), stay inside
one span. With no value column before it (an empty line), the span opens at the data area's left edge, because the
read held no row for the pen there; with none after it, the span ends at the ahead-break's own X. At a coarse layer
the first value column after the ahead-break is the first live bucket, which keeps its opening X while values fold
into it (`EnvelopeLine.cs:249-274`), so the span ends at the first live bucket's X and does not move as the bucket
widens (`charting.md:166-171`).

**[decision] The label sits centred along the bottom edge of the visible part of the span, and only when it
fits.** The shade measures the text on its first `Render` with `rp.Paint`, under its own lock on the render thread,
and keeps the width; the text never changes, because the culture is fixed for the process (`ui-text.md:23-33`).
Each frame it clips the span to the data area and draws the label 4 px above the data area's bottom, centred in the
clipped part, when that part is at least the label's width plus 4 px on each side; otherwise the band draws alone.
A label wider than its band would sit over rows the archive holds and read as covering them; the marker labels hide
under the same fit rule (`ChartMarkerLines.cs:66-84`). The bottom keeps the label clear of the marker labels at the
top edge and the hover readout at the crosshair's top end. The text is `Resources.ChartAheadBreakLabel` at 12 px in
`AppSecondaryForegroundBrush`, the size of the marker readings (`ChartMarkerReadings.cs:13`).

**[decision] The grey is `AppAheadBreakFillBrush`, translucent through the alpha of its colour.**
`ChartPalette.Resolve` reads ARGB and ignores `Opacity` (`ChartPalette.cs:55-63`), so the key carries its alpha in
the hex: `#33818594` under Light (the secondary grey at 20 %) and `#406F737A` under Dark (25 %). A translucent band
keeps the gridlines readable inside the span. The values are chosen, not measured; the stand check judges them.

**[decision] No allocation per frame on the shade's path.** The shade keeps one `List<AheadBreakSpan>` it clears
and refills, one `FillStyle` it fills through `FillStyle.Render(RenderPack, PixelRect)`, and one `LabelStyle` whose
text is set once. The label is drawn from one `SKTextBlob` shaped on the first frame that meets a span, not through
`LabelStyle.Render`, which allocates and runs LINQ on every call (review fix, below). `EnvelopeLine.CollectAheadBreakSpans` fills that list under the column lock from the visible
range, with no LINQ. The shade projects span X through its own pinned bottom axis. Its `Render` holds its own lock
throughout, as `ChartMarkerReadings.Render` does, and takes the line's lock inside it; no path takes the two in the
other order.

### Review fixes

The first two review iterations of this branch changed these records; the code and `docs/architecture` state them.

- **[decision] A coarse read that replaces the bucket the newest value folded into, and ends before that value,
  unjoins the line.** The line keeps the confirming value's own X beside the capped `_confirmedX`;
  `ReplaceColumns` reports a lost live value when the value folded past its bucket's X (`_confirmingValueX >
  _confirmedX`), the read replaces every column, and the value's X lies past both the requested end and the read's
  last row, as it does for a live column it drops. The bucket is the live bucket or, once a read has covered it,
  the archive bucket a joined pen's values fold into. Holding to "a read that cuts back before the stamp leaves no
  hold" at a coarse layer needs it, because a bucket keeps its opening X while later values fold into it. The
  second iteration replaced the first's "the line ends on a live column" condition, which missed the archive
  bucket, by the fold condition rather than dropping the condition: without it a Raw pen confirmed by a rejected
  value would be unjoined by a read cutting back before the value, and a read bringing the rows back would no
  longer restore its hold. The archive-lag case, a read that reaches past the value while the archive has not
  written it, is a backlog entry.
- **[decision] After a source break a coarse layer rejects a value at or before the last column.** The join table
  below now holds at every layer: while the line is `UnjoinedBySourceBreak` or `ReconfirmedAfterSourceBreak`,
  `FoldRealtime` no longer folds such a value into the bucket the outage cut.
- **[decision] A value that turns non-numeric writes a source break at the later of the notification's stamp and
  the pen's newest delivered stamp.** `DeliveredStamps.NotNumericBreak` places it for a pen with a delivered value
  and never raises it past the newest stamp, so it moves the edge to no moment the server did not state; the break
  ends the pen's hold. The raise to the newest stamp is the second iteration's: the coordinator sorts one pen's
  samples in a batch by stamp, so a break stamped behind the newest value in the same batch reached the chart
  ahead of it, and the value rejoined the line and held it.
- `TryHold` became `EnvelopePath.Hold`, returning `HeldSpan?`, and `EnvelopeLine.HeldSpan()` became
  `ReadHeldSpan()`; the shade draws its label from a cached `SKTextBlob` and is `IDisposable` for it.
- The code-smells review moved the held read to `TrendPenState.HeldValueAt`, which `ChartCursorReader`
  calls after the envelope; made `CursorReadoutModel` static with `ReadPenAt` alone, which `DeltaCursorModel`
  now calls; moved `AheadBreakPalette` to `Chart/AheadBreakPalette.cs`, public like `MarkerPalette`, with a
  public `ChartPalette.AheadBreakColors`; and renamed the view's `CreateMarkerLines` and `PaintMarkerLines` to
  `CreateChartOverlays` and `PaintChartOverlays`, which also build and recolour the shade.

## Technical Details

```csharp
// SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs
/// <summary>What a gap column means: the chart's break ahead of a pen's first live value, or any other.</summary>
public enum BreakKind
{
	Ordinary,
	Ahead
}

public readonly record struct EnvelopeColumn(
	double X, double Min, double Max, double Center, BreakKind Break = BreakKind.Ordinary);

// SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs
public readonly record struct AheadBreakSpan(double FromX, double ToX);   // FromX may be -Infinity

public static void Build(
	IReadOnlyList<EnvelopeColumn> columns,
	int first,
	int lastExclusive,
	PenLineStyle lineStyle,
	double confirmedX,
	double holdEndX,                       // NaN: no hold
	List<EnvelopePoint> destination);

/// <summary>The held span, when the last column is a confirmed value short of the hold end; null otherwise.</summary>
public static HeldSpan? Hold(IReadOnlyList<EnvelopeColumn> columns, double confirmedX, double holdEndX);

public static void AheadBreakSpans(
	IReadOnlyList<EnvelopeColumn> columns, int first, int lastExclusive, List<AheadBreakSpan> destination);

// SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs (all under _renderStateLock)
private double _confirmedX = double.NaN;     // min(value X, last column X) of the newest confirming value
private double _confirmingValueX = double.NaN;   // the same value's own X, uncapped
private double _holdEndX = double.NaN;
internal void ConfirmLive(double valueX);
internal void SetHoldEnd(double holdEndX);
internal HeldSpan? ReadHeldSpan();                                       // [FromX, ToX] and its Value
internal void CollectAheadBreakSpans(List<AheadBreakSpan> destination);  // over its own X axis' visible range

// SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs
public readonly record struct HeldSpan(double FromX, double ToX, double Value);   // Hold's result

// SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs
private enum LiveJoin { Unjoined, UnjoinedBySourceBreak, ReconfirmedAfterSourceBreak, Joined }
private LiveJoin _liveJoin = LiveJoin.Unjoined;
private double _holdEdgeX = double.NaN;
internal void HoldTo(double edgeX);            // NaN: hold off
internal double? HeldValueAt(DateTime time);   // CurrentValue inside Line.ReadHeldSpan(), else null
private void ApplyHold();                      // the one Line.SetHoldEnd call

// SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs
public void HoldLinesTo(double edgeX);         // hands edgeX to every pen state; keeps no copy

// SemiPlot/SemiPlot.UI/Chart/ChartLiveHold.cs (new)
internal sealed class ChartLiveHold : IDisposable   // over ChartPenSet and ChartNavigationController
{
	public void SetSessionUp(bool isUp);       // applies at once
	public void Apply();                       // the one caller of ChartPenSet.HoldLinesTo
	public void Dispose();                     // unsubscribes NewestSampleMoved
}

// SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs
// constructor: _disposables.Add(_coordinator.LiveSourceStates.Subscribe(OnLiveSourceState, ReportFailure));
// OnLiveSourceState: _liveHold.SetSessionUp(state.IsConnected); RequestRedraw();
// ApplyCatalogue: _liveHold.Apply() after the plot block; Dispose: _liveHold.Dispose()

// SemiPlot/SemiPlot.UI/Chart/AheadBreakPalette.cs (new)
public readonly record struct AheadBreakPalette(Color Fill, Color Label);

// SemiPlot/SemiPlot.UI/Chart/ChartAheadBreakShade.cs (new)
internal sealed class ChartAheadBreakShade : IPlottable, IDisposable
{
	public void ShowFor(EnvelopeLine? drawnLine);      // UI thread, once per served frame
	public void ApplyPalette(AheadBreakPalette palette);
	public void Render(RenderPack rp);                  // render thread
	public AxisLimits GetAxisLimits();                  // AxisLimits.NoLimits
}

// SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs
public static AheadBreakPalette AheadBreakColors(IResourceHost resources, ThemeVariant variant);
// AppAheadBreakFillBrush and AppSecondaryForegroundBrush
```

Join rule in `TrendPenState`, per live value, after the append or fold:

| State before | Value taken | Value rejected |
| --- | --- | --- |
| `Unjoined` | past the last column, behind an `Ahead` break; at or before it, folded at a coarse layer; then `Joined` | `Joined` |
| `UnjoinedBySourceBreak` | only past the last column, behind an `Ordinary` break, then `Joined` | `ReconfirmedAfterSourceBreak` |
| `ReconfirmedAfterSourceBreak` | only past the last column, behind an `Ordinary` break, then `Joined` | stays |
| `Joined` | appended or folded, stays | stays |

Every value calls `Line.ConfirmLive(valueX)`. A source null sets `UnjoinedBySourceBreak`; a history apply that lost
a live value (a dropped live column, or a coarse read replacing the bucket the newest value folded into that ends
before that value) sets `Unjoined`. The line is held in `Joined` and `ReconfirmedAfterSourceBreak`. "Behind a
break" is `AppendAfterBreak`, which writes no `NaN` column when the line already ends on a gap. In the two states after a
source break a value at or before the last column is rejected at every layer (review fix).

Hold rule: `Hold` gives a span when the list is not empty, the last column is a value, its X is at or past
`confirmedX`, and `holdEndX` is a number past that X. In `Build`, after the column loop, when
`lastExclusive == columns.Count` and `Hold` gives `(fromX, toX, value)`, add `(fromX, value, false)` if the last
point's Y differs from `value`, then `(toX, value, false)`.

Span rule in `AheadBreakSpans`: for each column in `[first, lastExclusive)` that is a gap with `BreakKind.Ahead`,
walk back over the whole list to the nearest value column (`FromX`, or `-Infinity` when none) and forward to the
nearest value column (`ToX`, or the ahead-break's own X when none). `VisibleRange` keeps one column past each edge,
so a window lying wholly inside a span still meets its ahead-break column.

Resources: `ChartAheadBreakLabel`, "No archive data when the window opened" / "Нет данных архива на момент
открытия окна".

Palette rows, both variants of `Styles/Palette.axaml`:

| Key | Light | Dark |
| --- | --- | --- |
| `AppAheadBreakFillBrush` | `#33818594` | `#406F737A` |

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests and documentation in this repository.
- **Post-Completion** (no checkboxes): the stand check and the roadmap's close.

## Implementation Steps

### Task 1: Give the ahead-break a kind and keep a source break's unjoin

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/OpcUaLiveEdgeJourneyTests.cs`

- [x] add `BreakKind { Ordinary, Ahead }` and the defaulted `Break` member to `EnvelopeColumn`
- [x] replace `_isJoinedToLive` with `LiveJoin` under the join table in Technical Details
- [x] make `AheadBreak` write `BreakKind.Ordinary` while the line is `UnjoinedBySourceBreak` or
      `ReconfirmedAfterSourceBreak`, `BreakKind.Ahead` otherwise
- [x] extend `TrendPenStateTests`: the ahead-break at window open, on an empty line, at a coarse layer and after a
      dropping history apply is `Ahead`; a source break and the `NaN` column after a rejected source break are
      `Ordinary`; a source break then a dropping history apply gives `Ahead`; a rejected source break, then a
      rejected replay at the original stamp, then a value past the last column opens behind an `Ordinary` column
      and appends nothing in between
- [x] add `EnvelopeLineTests.AnAheadBreakKeepsItsKindThroughAReplaceThatKeepsItAndAnAppendPastTheCap`
- [x] assert `BreakKind.Ahead` on the ahead-break column in `OpcUaLiveEdgeJourneyTests.AssertTheLine`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendPenStateTests|FullyQualifiedName~EnvelopeLineTests|FullyQualifiedName~TrendChartViewModelTests"`
      and `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUaLiveEdgeJourneyTests"`;
      both must pass before Task 2

### Task 2: Draw the hold in the path and hit it

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopePathTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`

- [x] add `TryHold` and the `confirmedX` and `holdEndX` parameters of `Build` under the hold rule in Technical
      Details; pass `double.NaN` for both at the two existing test call sites (`EnvelopePathTests.cs:143`, `:210`)
- [x] add `_confirmedX` and `_holdEndX` under `_renderStateLock`, `ConfirmLive`, `SetHoldEnd` and `HeldSpan()` to
      `EnvelopeLine`; `Render` and `DistanceTo` pass both X values to `Build`
- [x] write `EnvelopePathTests` for the hold: a degenerate last column held at its value; a tall last column steps
      from its exit to its `Center` first; a stepped pen; nothing after a gap column, for a last column before
      `confirmedX`, for a hold end at or before the last column, for a `NaN` hold end, or when the range stops short
      of the last column; a last column left of the window is still held across it
- [x] write `EnvelopeLineTests`: `DistanceTo_AClickOnTheHeldSegment_IsZero`, a click past the hold end measures to
      the hold's end point, `HeldSpan()` spans `[lastX, holdEndX]` and is null with no hold, `ConfirmLive`
      keeps the smaller of the value's X and the last column's X (a fold confirms the bucket's opening X), and
      `Render_WhileTheUiThreadRewritesTheColumns_DoesNotThrow` also calls `ConfirmLive` and `SetHoldEnd` in its
      mutation
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~EnvelopePathTests|FullyQualifiedName~EnvelopeLineTests"`;
      must pass before Task 3

### Task 3: Feed the hold from the session state and the newest sample

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartLiveHold.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartLiveHoldRenderTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartLiveHoldTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`

- [x] write `ChartLiveHoldRenderTests.AConfirmedSlowPen_IsDrawnHeldToTheNewestSample` with the five steps of
      Acceptance Evidence 1, and confirm it red before the wiring
- [x] add `HoldTo`, `ApplyHold` and the `ConfirmLive` calls to `TrendPenState` under the join rule; add
      `HoldLinesTo` to `ChartPenSet`
- [x] create `ChartLiveHold`; in `TrendChartViewModel` assign `_penSet` and `_liveHold` before the
      `LiveSourceStates` subscription (the source's `BehaviorSubject` replays at once, and under the tests' immediate
      UI scheduler the handler runs inside `Subscribe`), request a redraw on a state, call `Apply()` after the plot
      block of `ApplyCatalogue`, and dispose it
- [x] write `TrendPenStateTests` for the hold, each asserting `Line.HeldSpan()?.Value == CurrentValue` wherever a
      hold is drawn:
      - none before a live value;
      - a rejected first value confirms a constant pen and holds it from its archive row;
      - a history load that cuts back before the confirmed stamp ends the hold, and a load that brings the rows back
        restores it;
      - a rejected value stamped before every archived row, then a pan read that returns fewer rows, keeps the hold;
      - at a coarse layer a pen keeps its hold while values fold into its last bucket;
      - a source break appended at the end of the line ends the hold;
      - two-row history, a rejected value at the first row's stamp, a rejected `AppendRealtime(stamp + 1 ms, null)`,
        the replay `AppendRealtime(stamp, value)` and `HoldTo(edge)`: the hold is drawn;
      - the same sequence without the replay: no hold is drawn;
      - a dropping history apply ends the hold until the next value
- [x] write `TrendChartLiveHoldTests` over the production path (`PushLiveSamples` through the coordinator and
      `ChartRealtimeApplier`):
      - a confirmed pen holds to `NewestSample` while `Connected`; no hold during an initial `Connecting`;
      - a loss as the source writes it, the `Unreachable` state and a null sample 1 ms past each delivered pen's
        newest stamp, turns the hold off and requests a redraw;
      - `Connected` and a replay at the pen's original stamp restore the hold; `Connected` with no replay leaves the
        pen unheld;
      - a pen a later catalogue read adds takes the current edge once it is confirmed
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`; examine every failure an existing
      test shows under the default `Connected` state and record here which were updated and why; all pass before
      Task 4
      - Result: no existing test failed under the default `Connected` state, so none was updated. The full unit
        suite ran 2550/2550 with the wiring and before the new tests, and 2565/2565 with them.
      - Red-first result before the wiring: `Expected rows {empty} to have an item matching ... because the slow
        pen is held at its last value across pixel column 542`; `NewestSample` already stood at the window's end.

### Task 4: Read the held value at the cursor

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartCursorReader.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartLiveHoldTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartMarkerReadingsTests.cs`

- [x] route `ReadAt` and `ReadPenAt` through one private method: the envelope's value, else `CurrentValue` when the
      cursor's axis X lies inside `Line.HeldSpan()`; `ReadAt` keeps a pen's key when it has an envelope or a
      held value
- [x] write `TrendChartLiveHoldTests`: the cursor inside the held span reads `CurrentValue` in `CursorValues`; past
      the hold end, with the session down, and for a hidden pen it reads no value; a pen with no envelope and a held
      span gets its key
- [x] write `ChartMarkerReadingsTests`: a marker inside the held span reads the active pen's `CurrentValue`, equal to
      the cursor's value there
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TrendChartLiveHoldTests|FullyQualifiedName~ChartMarkerReadingsTests|FullyQualifiedName~TrendChartCatalogueTests|FullyQualifiedName~TrendChartViewModelTests"`;
      must pass before Task 5

### Task 5: Find the ahead-break spans

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopePathTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`

- [x] add `AheadBreakSpans` under the span rule in Technical Details and `EnvelopeLine.CollectAheadBreakSpans`
- [x] write `EnvelopePathTests` for the spans: one span from the last value before to the first value after; an
      anchor `NaN` before a kept ahead-break stays in one span; an empty line opens at `-Infinity`; an ordinary gap
      gives none; a window inside a span still yields it
- [x] write `TrendPenStateTests.FoldRealtime_TheAheadBreakSpanEndsAtTheFirstLiveBucketsX`: after the first value
      opens a column behind the ahead-break and two more fold into it, the span's `ToX` is the first value's X
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~EnvelopePathTests|FullyQualifiedName~TrendPenStateTests"`;
      must pass before Task 6

### Task 6: Shade and label the drawn pen's ahead-break

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartAheadBreakShade.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartAheadBreakShadeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPaletteTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewTests.cs`

- [x] add `AppAheadBreakFillBrush` to both variants of `Palette.axaml`, `ChartPalette.AheadBreakColors`, and
      `ChartAheadBreakLabel` to both resx sets
- [x] create `ChartAheadBreakShade`: the pinned bottom axis, its lock over the drawn line and colours, the reused
      span list, the fill, the lazily measured label and the fit rule
- [x] wire it in `TrendChartView`: create, add and `MoveToBottom` it with the marker layer, remove it on a
      data-context change, `ShowFor` the drawn pen's line in `ServeFrame`, recolour in `ApplyChartPalette`
- [x] write `ChartAheadBreakShadeTests` over `Plot.GetImage`, diffing against the data background: the band inside the
      span and none outside it; a label inside a wide span and none inside a narrow one; nothing for an ordinary
      break; nothing with no drawn line
- [x] write `ChartPaletteTests.AheadBreakColors_ResolveTheFillWithItsAlphaUnderTheVariant` and
      `TrendChartViewTests`: the shade is the plot's first plottable after the view binds, follows the drawn pen after
      an active-pen switch, and is gone after the data context changes
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`; must pass before Task 7

### Task 7: Describe the marks in the architecture docs

**Files:**
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/ui-theme.md`
- Modify: `docs/architecture/ui-text.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/plans/backlog.md`

- [x] `charting.md`: the kind in "Gaps via NaN" (`:64-70`); `LiveJoin`, the kind rule and the join table, with a
      rejected replay after a source break re-confirming the hold while the next append still opens behind an
      ordinary break, in "The break ahead of a first live sample" (`:133-164`); the coarse paragraph in present tense
      with the span ending at the first live bucket (`:166-171`); new subsections "The hold to the live edge" and
      "The ahead-break shade", with the lock order (readings then line, shade then line), and the statement that a
      constant pen's hold extends whatever value the last archive column holds, the by-time row that carries the
      previous value included, and agrees with the sidebar's `CurrentValue`; the hold's two X values
      under the column lock (`:82-97`) and the held segment in "Hit distance" (`:40-47`); module entries for
      `TrendPenState`, `ChartCursorReader`, `ChartLiveHold`, `ChartAheadBreakShade` and `Styles/Palette.axaml`
      (`:451-458`, `:533-537`, `:686-687`)
- [x] `data-integration.md`: present tense for the shade (`:1065-1071`) and for the reader of `States`
      (`:1270-1271`), `LiveJoin` for `_isJoinedToLive` (`:1073-1100`), and at `:1349-1358` the rule: a recreated
      session's replay re-confirms an unchanged pen's hold and its next change still opens behind an ordinary break,
      while a reactivated session leaves the pen unheld until its next change
- [x] `ui-theme.md`: the `AppAheadBreakFillBrush` row and its count (`:151-169`), the shade label among the
      consumers of `AppSecondaryForegroundBrush`, and in "The plot" the shade's two colours and why its key carries
      alpha in its colour (`:443-447`)
- [x] `ui-text.md`: a "The chart's text" section with `ChartAheadBreakLabel` in both languages, and that the label
      reads loosely for the span after a pan into the past and back
- [x] `trend-interaction.md`: the hover readout bullet (`:307-320`) reads `CurrentValue` in the held span
- [x] `backlog.md`: the readouts read the history envelope rather than the drawn line, so the live run between the
      envelope end and the held span reads a dash and a delta cursor in the held span reads no Δy, with
      `ChartCursorReader.cs:11-29`, `ChartDeltaCursorReader.cs:30-36` and `TrendChartViewModel.cs:690`; and the
      reactivation gap: a session the stack reactivates re-delivers nothing for an unchanged pen, so the pen stays
      unheld until its next change, and a read of the current values after a reactivation would close it
      (`data-integration.md:1351-1352`)
- [x] run `git grep -n "_isJoinedToLive" -- SemiPlot docs/architecture` and
      `git grep -n "live-hold-to-edge" -- docs/architecture`; both print nothing

### Task 8: Verify acceptance criteria

- [x] verify every point of the Overview is implemented
      - Read against the code: `BreakKind` on `EnvelopeColumn`, `AheadBreak` writes `Ordinary` after a source break;
        `ChartAheadBreakShade` under every line with `ChartAheadBreakLabel`; `ChartLiveHold` hands `NewestSample` while
        `IsConnected`, `NaN` otherwise; `TrendPenState.ApplyHold` holds in `Joined` and `ReconfirmedAfterSourceBreak`;
        `ChartCursorReader` reads `CurrentValue` inside `HeldSpan()`; a rejected value after a source break moves to
        `ReconfirmedAfterSourceBreak`, not `Joined`. No stored column is written for either mark.
      - Corrected after review: `HeldSpan()` is now `EnvelopeLine.ReadHeldSpan()`, and the held read is
        `TrendPenState.HeldValueAt`, which `ChartCursorReader` calls after the envelope (Review fixes). The line numbers
        and counts below are those of the Task 8 run at `f9c934b`; the run at `af6916c` is recorded under Task 9.
- [x] run every command of Acceptance Evidence 1 to 4 and read each result
      - AE1: `ChartLiveHoldRenderTests` 1/1 passed. The red side at `5f8ea2c` is not re-run here: Task 3 confirmed it red
        before the wiring (recorded under Task 3), and this run checks out no old commit.
      - AE2: the eight-class filter 150/150 passed.
      - AE3: `OpcUaLiveEdgeJourneyTests` 1/1 passed.
      - AE4: `_isJoinedToLive` and `live-hold-to-edge` print nothing; `SetHoldEnd(` prints `Chart/EnvelopeLine.cs:289`
        (declaration) and `Chart/TrendPenState.cs:161` (`ApplyHold`); `BreakKind.Ahead` prints
        `Chart/EnvelopePath.cs:172` and `Chart/TrendPenState.cs:169` only; the AXAML colour-literal grep prints nothing.
- [x] run the full suite: `dotnet test SemiPlot.slnx`
      - Docker Desktop 29.8.2 reachable: unit 2589/2589, integration 205/205 (container collection included), exit 0.
- [x] run `dotnet build SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes`, and `dotnet terse` over
      the touched `.cs` files
      - build: 0 errors, 2 warnings (the AppHost's two `NU1902`); format: exit 0; terse over the branch's 21 `.cs`
        files: exit 0.

### Task 9: [Final] Update documentation

- [x] update `AGENTS.md` only if a rule changed (none is expected: the palette, resx and single-writer rules already
      cover the slice)
      - No AGENTS.md statement turned false: the `ILiveSource`, `new HistoryRequest(` and Chart/Legend port gates
        print what they state. `TrendPenState` alone writes `BreakKind.Ahead` and the hold end, and the two git grep
        gates of Acceptance Evidence 4, which `charting.md` states in prose but not as commands, move into AGENTS.md
        so they do not leave with this plan. Corrected after review: the code-smells review moved them from a clause of
        the live-edge bullet to a bullet of their own in AGENTS.md's UI section.
- [x] move this plan to `docs/plans/completed/` (not moved: exec leaves the plan in place; delivery archives it)
      - Final run at `af6916c`: `dotnet build SemiPlot.slnx` 0 errors, 2 warnings (the AppHost's two `NU1902`);
        `dotnet test SemiPlot.slnx --no-build` unit 2610/2610, integration 205/205; `dotnet format SemiPlot.slnx
        --verify-no-changes` exit 0; `dotnet terse` over the branch's 29 `.cs` files exit 0; the AE4 greps print
        `Chart/EnvelopeLine.cs` and `Chart/TrendPenState.cs` for `SetHoldEnd(`, `Chart/EnvelopePath.cs` and
        `Chart/TrendPenState.cs` for `BreakKind.Ahead`, and nothing for the other three.

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification** on the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`; the writer runs
`--archive-lag 60`, `SemiPlot/SemiPlot.AppHost/AppHost.cs:25`, `:58-61`):

1. At window open the active pen shows a grey span between its newest archived row and its first live value,
   labelled "No archive data when the window opened" when the span is wide enough, under both themes; the
   gridlines stay readable inside it.
2. Switch the locale to Russian, restart, and read the label in Russian; it fits under the same width.
3. Pick another pen; the grey moves to that pen's span.
4. A pen that changes slowly is drawn held from its last value to the right edge, and the hover readout over the
   held segment shows the value the sidebar shows.
5. A gesture more than a minute after open reads history; the grey span closes where the archive now holds rows.
6. Pan more than a window into the past and jump back to now: the active pen shows a new grey span after the
   archive's newest row, under the same label. A pen whose last change was a live value is held again only at
   its next change (`docs/plans/backlog.md`).
7. Stop the writer; within about 5 s the status bar shows OPC UA not answering, and every held segment is gone.
8. On the demo SCADA (`test_project`, roadmap `:20-22`), open the viewer and compare every constant and stepped
   pen's held value with the sidebar reading and with the value the SCADA's own runtime shows. They agree; a pen whose
   last archive column is a by-time row with the previous value shows that value in all three of the viewer's places
   until its next change, and the comparison records whether the SCADA's display differs there.
9. Optional: rerun the frame-cost capture of `docs/architecture/testing-strategy.md#frame-cost` with 8 pens; the
   hold adds at most two vertices per pen and the shade one band per frame.

**Roadmap and merge:**

- The roadmap tier records this slice's `Status`, `Plan`, `PR` and `Branch` in
  `docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md` (`:275-293`), which its close condition reads (`:295-298`).
- `opc-settings` also edits both resx files; whichever slice merges second rebases its resx entries.

Remaining slices: none; the roadmap closes when this slice merges.

**Executed by exec:**
- branch: live-hold-to-edge

## Verify it yourself

Every command runs from the repository root on `live-hold-to-edge`. The unit and OPC UA checks need no container;
`dotnet test SemiPlot.slnx` needs a Docker daemon for the archive collection.

1. The hold to the edge. Run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter
   "FullyQualifiedName~ChartLiveHoldRenderTests"`; it passes. The test lands with its wiring in `9ea537e`. Its red
   run before the wiring is recorded under Task 3 ("Expected rows {empty} ... across pixel column 542"). No commit
   holds the test without the wiring, so the red side is not reproducible by a checkout. On the stand, Post-Completion
   steps 4 and 7 show it: before, a slow pen's line stops at its last archive row; after, it runs to the right edge
   while OPC UA is up and stops at the row once the writer stops.
2. The kind of the break ahead of the first live value. `git grep -n "BreakKind.Ahead" -- SemiPlot/SemiPlot.UI`
   prints only `Chart/TrendPenState.cs` and `Chart/EnvelopePath.cs`. On `master` (`1c67536`) the type does not exist.
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter
   "FullyQualifiedName~OpcUaLiveEdgeJourneyTests"` asserts `BreakKind.Ahead` on the column a served value opens.
3. No line across an outage after a rejected replay. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
   --filter "FullyQualifiedName~Realtime_ARejectedReplayAfterARejectedBreak|FullyQualifiedName~FoldRealtime_AReplayAfterARejectedBreak"`
   passes. On `master` the replay rejoins the line, so its next change appends with no break (Acceptance Evidence,
   the defect at `5f8ea2c`).
4. The grey span and its label. These have no reliable automated check of the look; `ChartAheadBreakShadeTests`
   pins the band, the fit rule and the clipped label by pixels. Post-Completion steps 1 to 3, 5 and 6 are the
   manual check, under both themes and both languages.
5. Readouts in the held span. `--filter "FullyQualifiedName~TrendChartLiveHoldTests|FullyQualifiedName~ChartMarkerReadingsTests"`
   passes. On the stand, Post-Completion step 4: the hover over the held segment shows the sidebar's value; on
   `master` it shows a dash past the envelope's end.
6. Review fixes with no stand repro (the coarse-layer cases need a window wide enough for a coarse layer and an
   archive read that ends before the newest live value): `--filter
   "FullyQualifiedName~Hold_AtACoarseLayer_A|FullyQualifiedName~ABreakAtTheStampOfTheValueAheadOfItInOneBatch|FullyQualifiedName~ANonNumericValueAfterADeliveredOne|FullyQualifiedName~APenTurningNonNumeric_"`
   passes. The fixers recorded the mutation checks that turn each red (progress log, review phase 1 iterations 1
   and 2).
7. The whole gate: `dotnet build SemiPlot.slnx` (0 errors, the AppHost's two `NU1902`), `dotnet test SemiPlot.slnx`,
   `dotnet format SemiPlot.slnx --verify-no-changes`, and `dotnet terse` over
   `git diff --name-only --diff-filter=d master...HEAD -- '*.cs'`.
