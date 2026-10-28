# Trend Interaction (behavior spec — as-built)

The authoritative, prioritized requirement set for the trend canvas is
[trend-feature-spec.md](./trend-feature-spec.md) (MasterSCADA-derived); where it and this
document differ, the spec wins. This document keeps the **as-built implementation mechanics**
(gesture routing, overlay/seam wiring, scheduler discipline) and the **Decisions log** below,
and cross-references the spec for the requirements themselves rather than restating them.

Behavioral / interaction subjects covered: time navigation, real-time sticky scrolling,
multi-pen axis management, scaling, cursors, decimation, and rendering. Complements
[charting.md](./charting.md) (renderer details) by defining how the viewer *behaves* under
operator interaction.

> Status: **as-built (MVP implemented).** This spec is realized in `SemiPlot.UI` on Avalonia
> 12.0.5 + ScottPlot 5; the Decisions log below records the original rationale and is kept for
> history. Source: desired behavior of the MasterSCADA 3 trend window plus user-requested fixes
> (reference image in the machine docs), cross-checked against mature vendor trend controls
> (Ignition Power Chart / Easy Chart, AVEVA Trend Client, WinCC, Simple-Scada) and decimation
> literature (M4, MinMaxLTTB). Data integration target stays Simple-Scada 2 (see
> data-integration.md); MasterSCADA 3 / Ignition Power Chart are **UX references** only. Non-MVP
> items remain marked `[LATER]`.

## Decisions log (2026-06-16)

- **Renderer:** **ScottPlot 5** (MIT, SkiaSharp; `ScottPlot.Avalonia` 5.1.59 on **Avalonia 12.0.5**).
  Chosen over OxyPlot for built-in independent multi-axis. Trends render as a per-pen **`Scatter`
  center line + `FillY` min/max band** over a data-layer-decimated envelope;
  `DataLogger` is cited prior art only, not the implementation pattern (it cannot carry a
  pre-decimated min/max band). Supersedes the uPlot/WebView2 stack in overview.md / charting.md.
  *As-built reconciliation:* `Scatter` has **no `OnNaN`/`Gap` property** (a plan
  assumption); gaps are produced by feeding `double.NaN` (the default `Straight` path strategy
  breaks the line at NaN), so the committed gap mechanism is "NaN in Center/Min/Max", not an enum.
  The `Scatter` + `FillY` pair was later replaced by one plottable of our own, `Chart/EnvelopeLine`,
  drawing each pen as a viewport-culled min/max polyline with no fill (charting.md).
- **UI framework:** **Avalonia 12.0.5 / net10**, the same pairing `SemiStep` ships
  (`ReactiveUI.Avalonia` 12.0.3, `ScottPlot.Avalonia` 5.1.59, which itself depends on Avalonia 12.0.0).
  *As-built note:* `SemiPlot.UI` references `Avalonia.HarfBuzz` 12.0.5 and the builder chain calls
  `UseHarfBuzz()` between `UseSkia()` and `UseReactiveUI()`. `App.BuildAvaloniaApp` names the platform
  itself (`UseWin32().UseSkia()`) instead of calling `UsePlatformDetect()`, and Skia carries no text
  shaper, so without that call `AppBuilder.Setup` throws "No text shaping system configured" before any
  window exists. The headless platform registers a shaper of its own, so no headless test reaches that
  path — `SemiPlot.Tests.Unit/UI/Startup/AppBuilderCompositionTests` reads the composed builder back instead.
  `AvaloniaScheduler` / `UseReactiveUI` live in namespace `ReactiveUI.Avalonia` (NOT
  `Avalonia.ReactiveUI`), and `UseReactiveUI` takes a mandatory `Action<ReactiveUIBuilder>`.
  Stack: **ReactiveUI** MVVM
  (`ReactiveObject` / `ReactiveCommand` / `AvaloniaScheduler.Instance` / `CompositeDisposable`),
  **Microsoft.Extensions.DependencyInjection** (extension methods, primary constructors), **Serilog**
  (file, rolling 5 MB / 5 files), **Semi.Avalonia** 12.0.3 retinted to the JetBrains palette
  (`ui-theme.md`). `RxApp` is gone from the installed ReactiveUI 23.2.28: its schedulers are
  `RxSchedulers.MainThreadScheduler` and `RxSchedulers.TaskpoolScheduler`, its exception handler is
  `RxState.DefaultExceptionHandler`, and this repository reads neither — the UI scheduler it passes
  around is `AvaloniaScheduler.Instance`. Rationale for ReactiveUI: the data layer is
  Rx-native and the VMs are derived-state-heavy (sticky, cursor, active-pen) — a fit for
  `WhenAnyValue`/OAPH/`ReactiveCommand`; CommunityToolkit.Mvvm is an acceptable lower-friction
  alternative. The Core `IDataProvider` / DTO / stub layer is retained.
- **Visual language:** superseded. The JetBrains / IntelliJ look was recorded as a north-star and
  `FluentTheme` as the MVP; `Semi.Avalonia` retinted to the JetBrains palette shipped instead, and
  `ui-theme.md` is the record of what it does and how far it reaches.
- **Scheduler seam:** Core keeps the bare `IScheduler` (`DefaultScheduler.Instance`) for data timing;
  the UI scheduler (`AvaloniaScheduler.Instance`) is captured in `AfterSetup` and passed explicitly to
  the coordinator — `TrendCoordinator(IDataProvider dataProvider, ILiveSource liveSource,
  IReadOnlyList<Pen> pens, IScheduler dataScheduler, IScheduler uiScheduler)`,
  `Buffer` (the 100 ms `TrendCoordinator.BatchWindow`) on the data scheduler, `ObserveOn` on the UI one. No second `IScheduler` container registration.
  The pen catalogue is passed in because the coordinator needs the pen identifiers in its constructor
  and `QueryPensAsync` cannot be awaited there; the composition root hands over the start sequence's
  read, and a later pen set arrives through `SetPens` (`data-integration.md#realtime`).
- **Decimation envelope contract:** history record per pen = ascending `X[]` + `Min[]` + `Max[]` + center
  `Y[]`; realtime stays single-value `double?[]` (null = gap); rendered as one `EnvelopeLine` polyline
  (see Renderer).
  *As-built note:* realtime is `double?` on each pen's own timestamps, not on a shared column:
  `PenRealtimeValues` carries no entry at a timestamp the pen did not report, and a null is a break the
  live source reported. The poll writes one 1 ms after a `q = 32` row (data-integration.md#realtime), and
  the first poll answer after an outage longer than 5 s writes one at each pen's hold end; every other gap
  the chart draws comes from the history path (charting.md, "Gaps via NaN").
- **Δ cursors:** Δy is reported only for the **active/selected pen** (pens share X but have independent Y
  scales, so a global Δy is meaningless).
- **Bad quality → gap:** OPC bad-quality is mapped to `null` at the `IDataProvider` boundary, reusing the
  null = gap path; a distinct value-present-but-bad-quality flag is deferred.
  *As-built note:* no OPC quality exists yet, so no bad-quality status maps to `null`. The live edge
  crosses `ILiveSource`, not `IDataProvider`, so that port is the boundary an OPC status maps at. The
  archive marks the last sample before a break with `q = 32`, and that mark maps to a break. On the
  history path the server closes the bucket there, and `BucketedRowFold` writes a `NaN` column one tick
  after it (data-integration.md#quality-and-gaps). At the live edge the poll emits the row's value and
  then a null `Sample` 1 ms after it, which the chart appends as a `NaN` column
  (data-integration.md#realtime).
  `q = 16` takes no branch: the next bucket or sample resumes the line.
- **Real-time return:** "jump to real-time" re-attaches sticky immediately; now-marker at the
  **right edge** (centering is at most a transient transition animation).
- **Many-axes management:** **single active Y axis + per-pen autoscale** (legacy SCADA model);
  clicking a pen makes it active. Pens that must read against one range carry the same stored
  `scale_min_on_start`/`scale_max_on_start`; no two pens share an axis.
- **Axis scaling gestures:** entering values = fixed manual limits. Autoscale and the initial scale
  are commands on the active pen: buttons of the axis scale panel and items of the View -> Pen scale
  submenu. The original **toolbar duplicate** is superseded: the autoscale button, the two limit
  boxes and Set Limits left the bar with the navigation-bar change, and the axis scale panel is the
  only way to type a pen's limits.
- **Log axis:** values ≤ 0 are **sanitized** (dropped) before log scaling.
  *As-built note:* only the auto range drops them. The line draws a value ≤ 0, and any value under the
  axis minimum, on a floor 2 px above the data area's bottom, and the cursor and hover readouts show the
  stored value (charting.md#log10-y-axis).
- **Time display:** **computer local time** (machine local), not UTC.
- **Line style:** both stepped and interpolated, **configurable per pen**.
- **Performance:** **FPS locked at 30**; data updates no faster than **10 Hz (100 ms)**; up to
  **50 displayed pens**.
  *As-built note:* the chart redraws at most once per display frame (§RT-4,
  charting.md#the-frame-paced-redraw). The 50 pens are a sizing budget: no code caps the pen count.
- **Decimation backend:** kept behind the data-provider **stub** for now; production backend
  will be **PostgreSQL**, but whether it stores pre-trimmed/layered data is **unknown** — the
  data layer must support either server-side aggregation or in-process decimation.
  *As-built note:* the backend is PostgreSQL, and the archive stores the layers in its `l` column
  (`AggregationLayer`: `Raw`, `Minute`, `Hour`; `scada-archive.md#layers`). The server buckets every
  layer in the query (`BucketedHistoryWindow`), and no decimator runs on the client (see "Decimation &
  performance").
- **Dropped / out of scope:** horizontal cursor (dropped); alarm/event overlays (out of scope).
  Time markers on the chart are in scope and built (`#the-time-markers`).
  **`[LATER]`:** view persistence, snapshot/print. CSV value export ships
  as one table of pen, time and value (overview.md#the-export-window).

## Terminology

| Term            | Meaning                                                                 |
| --------------- | ----------------------------------------------------------------------- |
| Pen             | One plotted series (one tag): color, name, scale, current value.        |
| Archive layer   | Archive resolution / decimation level: raw / minute / hour (data-integration.md, `l` column). NOT a plotted curve. |
| View window     | Visible time range `[from, to]`; width = zoom level.                    |
| Now-marker      | Marker at the latest measured sample = current moment / live edge.      |
| Sticky          | View auto-scrolls to keep the live edge at the right edge (real-time follow). |
| Active pen      | The pen whose scale is shown on the primary axis; selected by click.    |
| Cursor / X-trace | On-hover Avalonia overlay vertical line reading each pen's value at the cursor X. |
| Time marker     | A stored vertical line at a moment, with an optional label, that every window shows (`#the-time-markers`). Not the now-marker, the T1/T2 delta cursors or the archive's `q = 32` break marker. |

> Terminology fix: the user's draft used "слой графика" for a plotted curve. To avoid clashing
> with the archive-resolution "layer", a plotted curve is always a **pen**.

## Single chart — time behavior

Requirements: the now-marker / sticky live-edge follow (trend-feature-spec.md §RT-2), pan with a
constant window width down to the first stored sample (§TM-3), wheel zoom from 1 second to 1 year
about the cursor anchor (§TM-2), and the autoscale / manual modes and the log10 axis type (§AY-3 … §AY-6). The
as-built mechanics that realize them:

- **Zoom width is quantized onto a 1.25 geometric ladder** (`TrendNavigationModel.Zoom`); the
  reciprocal wheel factors (in 0.8 = 1/1.25, out 1.25) share grid points so an in→out cycle
  round-trips to the origin width instead of drifting through accumulated float error (§TM-2
  acceptance). No-lag from 1 s to 1 year is guaranteed by decimation, not the chart control (see
  "Decimation & performance"). A width typed or picked in the time window panel is set exactly, off
  the ladder: `TrendNavigationModel.SetWidth` and `SetWindow` bypass the quantization, so a 5-minute
  preset shows 0:05:00 and not the nearest rung. The next wheel notch snaps the width back onto the
  ladder (after `SetWidth(5 min)` one notch in gives `1.25^25 s`, 264.7 s), and the round trip holds
  from a width on the ladder.
- **`From` is clamped to `≥ FirstSample`** so a wide window never reaches back past the first
  stored sample and renders the missing left span as data (§TM-3).
- **Zoom history is debounced off the UI thread** (§DA-9). Gesture-driven re-queries flow through a
  single chokepoint (`Chart/ChartHistoryRequestDebouncer`): `Throttle` collapses rapid notches to one
  trailing request after the gesture goes quiet, the query runs on the data scheduler, one at a time, and
  the newest window that arrived while it ran runs when it lands (so a read slower than the cap still
  completes, and the newest window is the last applied). Per-zoom redraws go through the redraw seam
  and land on the next display frame with every request made before it, not an inline refresh. The startup
  `RequestInitialHistory` is an ordinary request on that path, so the initial load and gestures share one
  latest-wins history path; the first-snap `TrackDataExtents` path stays non-requerying (single initial
  load).
- **Axis scaling gestures (as-built):** entering min/max = fixed manual limits (§AY-3); View -> Pen
  scale -> Autoscale (§AY-4) and View -> Pen scale -> Restore initial scale act on the active pen, and
  the panel's two buttons act on the pen the panel was opened for. The axis scale panel is the only
  place that types limits: the navigation bar carries time navigation only. The scale modes are
  `auto`, which ranges over the columns inside the visible window, and `manual` (§AY-3, §AY-4); the
  log10 axis is an axis *type* beside either mode (§AY-6). A pen opens on the type its
  `log_scale_on_start` stores, the panel's Logarithmic scale checkbox switches it for the session of the
  window, and Restore initial scale brings the stored type back with the stored bounds. A log axis
  needs a minimum above zero: the auto range skips values ≤ 0, the panel refuses the type over a manual
  minimum ≤ 0, and a sample ≤ 0 draws 2 px above the bottom of the data area while every readout
  shows its true value (`charting.md#log10-y-axis`).
- **Mouse gestures on the active pen's scale (as-built):** Ctrl + wheel and Ctrl + drag over the data
  area shift the active pen (`TrendChartViewModel.ShiftActivePen`), and the wheel over its axis scales it
  about the cursor (`TrendChartViewModel.ScaleActivePen`, §PN-2). A shift moves both limits by one amount.
  A Ctrl + wheel notch moves them by 10% of the span; wheel up lowers both limits, so the line rises. A
  Ctrl + drag moves them by the pointer's vertical move as a share of the data area's height, so the line
  follows the pointer. The axis wheel multiplies the span by 0.8 per notch up and by 1.25 per notch down
  and keeps the value under the cursor at its pixel. The pen scale has no quantized ladder, so an in and
  out pair restores the span only up to floating-point error. Both methods take the limits the pen shows
  now, apply `Core/Trends/PenRangeGesture` and write the result through `SetAxisLimits`, the write the
  axis scale panel uses. An `Auto` pen therefore takes the limits it shows, turns `Manual` and then moves;
  View -> Pen scale -> Autoscale brings tracking back. The limits live in the window's session like typed
  ones, and nothing reaches the database or the configuration. On a log10 axis the shift and the scale
  run in decades, so the minimum stays above zero. `PenRangeGesture` returns null, and the pen keeps its
  limits, for a result that is not finite, a span not above zero, or a log minimum that
  `LogAxis.AdmitsMinimum` refuses. A hidden active pen draws no axis, and no gesture changes its limits.
  No other pen's limits change.

## Multi-pen / multi-axis behavior

Requirements: multiple independent Y axes (trend-feature-spec.md §AY-1), the per-pen "each on its
own axis" case (§AY-2), and the shared-X / independent-Y invariant (§TM-1). As-built mechanics:

- The chart is sized for **50 pens**, each with a scale of its own; no code caps the count.
- **Axis management = single active axis + per-pen autoscale:** the active pen's scale is surfaced
  on the primary axis; non-active pens scale individually with their axes hidden, so many pens do
  not spill many visible axes. The literal "N lines, each on its own axis" case (§AY-2) is the
  as-built shape: `PenScaleSettings` carries no axis key, and the axis is the pen.
- 16 heaters reading against one range (dampers separately) is the same
  `scale_min_on_start`/`scale_max_on_start` pair stored on each of the sixteen, not one axis carrying all of them.
- When panning/zooming time, **all pens move together** — pens are **always time-synchronized**;
  Y scales are independent (§TM-1).

## Navigation & cursors

Requirements: pan/zoom (trend-feature-spec.md §TM-2, §TM-3), sticky live-edge (§RT-2), the
hover readout/crosshair (§CU-1, §CU-2), T1/T2 delta measurement (§CU-3), and manual axis
limits via axis-region edit (§AY-3). The mechanics below are the as-built realization.

ScottPlot's built-in mouse processing is disabled (`UserInputProcessor.Disable()`); all gestures
are routed through `Chart/TrendChartView` onto the navigation controller. `Disable()` does not cover
the wheel: `AvaPlot.OnPointerWheelChanged` ends with `e.Handled = HandleMouseWheelEvent`, outside the
delta guard and unconditioned on the input processor, and Avalonia runs that class handler before the
view's instance handler on the same element — so a wheel event arrives already handled and the view
never sees it. The `TrendChartView` constructor therefore also sets
`PlotControl.HandleMouseWheelEvent = false`, which is what keeps wheel zoom working; the view's own
handler is then the only writer of `Handled` for the wheel. Each event kind has one pure decision
point, and the handlers carry no modifier test of their own: the wheel goes through
`Chart/ChartWheelRouter.Route(isAxisRegionHit, modifiers, delta)`, the left press through
`Chart/ChartPressRouter.Route(region, modifiers, activeTool, clickCount)`. The view names the one
`ChartPressRegion` the press lands on (`ActivePenAxis`, `TimeAxis`, `LockedMarker`, `Marker` or
`DataArea`; the two bands do not overlap, `LockedMarker` is a press inside the data rect within 4 px of a
locked time marker, and `Marker` the same press near an unlocked one), and the press router checks the
active pen's axis first, then Ctrl, then the time axis band, then the tool, then a locked marker (always
`PanOrPickPen`), then any other marker, then the click count (`#the-time-markers`, Add and delete). The left
button is a single tool with an explicit state (`Chart/LeftButtonTool` = `Pan` | `DeltaPlacement`); the active
tool is sourced from the navigation bar's delta-mode toggle (`TrendChartViewModel.ActiveLeftButtonTool`),
so there is one left-button gesture, not overlapping hidden branches. Ctrl is a held key, not a tool.
A drag belongs to the pointer that pressed it (`ChartDrag.Pointer`). A second mouse button pressed during
a drag never reaches the press router: Avalonia's `MouseDevice` raises a press or a release with another
button still held as `PointerMoved` of the one mouse pointer, so the drag under way keeps going and ends
on the release of the last button held. Touch is the other case: Avalonia's `TouchDevice` gives every
finger its own pointer and raises its `PointerPressed` with the left button down. The view routes no
press while a drag is under way, and it ignores the moves, the release and the capture loss of every other
pointer, so a second finger neither moves, pans nor ends the first finger's drag.

- **The wheel map.** `ChartWheelRouter` returns a `ChartWheelAction` and a notch sign, and checks the
  axis region first, then Ctrl, then Shift:
  - over the active pen's axis region (`Chart/ChartAxisRegion`), with any modifiers: `ScaleActivePen`;
  - Ctrl over the data area: `ShiftActivePen`, Ctrl + Shift included;
  - Shift over the data area: `PanTime`, 10% of the window width per notch; wheel down pans toward
    newer data through `ChartNavigationController.PanBy`;
  - no modifier: `ZoomTime`, the zoom about the cursor anchor.

  The notch is the sign of `Delta.Y`. With Shift held and `Delta.Y` zero it is the sign of `Delta.X`,
  so a Shift + wheel that arrives on the horizontal component still pans. A notch of 0 routes to
  `None`: nothing moves and the view leaves the event unhandled, so a horizontal wheel without Shift
  changes nothing. The view sets `Handled` for every other action.

  The notch is a sign, not a magnitude: a fractional delta from a precision touchpad or a smooth-scroll
  wheel counts as a whole notch, as it did for the zoom before the map, so one swipe takes many 10% steps.
  Windows reports a precision touchpad pinch as a wheel event with Ctrl held (checked on hardware
  2026-10-15), so a pinch shifts the active pen and pins it `Manual` instead of zooming time. This is a
  known trade-off of the agreed map, which keeps Ctrl + wheel for the shift.
- **Scroll = zoom about the cursor anchor; left-drag = hand pan; left-click = pick a pen.** In `Pan`
  tool a press outside the axis region and the time axis band, off every time marker, with Ctrl not
  held routes to `ChartPressAction.PanOrPickPen`; on a marker it drags the marker (`#the-time-markers`).
  The press captures the pointer and switches the cursor to a grab icon (`StandardCursorType.SizeAll`).
  A move pans the X window via `TrendNavigationModel.Pan` only once the pointer is more than the 4 px
  click slop (`ChartPressRouter.IsPastClickSlop`) from the press point; from then on every move pans and
  the release picks nothing. The slop exists because a pan of one pixel into the past detaches sticky,
  so the jitter of a hand on a click would drop follow mode. A release inside the slop is a click:
  `TrendChartViewModel.PickPenAt` measures the press point against every visible pen's line
  (`ChartPenSet.PenNearest`) and `ChartPenPick.Choose` takes the nearest line within 6 px, which then goes
  through `SetActivePen`, the same path the sidebar uses. At an equal distance the active pen keeps the
  choice, otherwise the first pen in catalogue order wins. A click farther than 6 px from every line, a
  click outside the data area and outside both axis bands included, is a miss: the pen stays active and loses its emphasis
  (`TrendChartViewModel.DropEmphasis`), so its line draws 1 px wide like the others while its sidebar
  row, its axis, Δy and the shift and scale gestures stay on it. A click on any line, the active pen's
  own included, or on any sidebar row goes through `SetActivePen` and emphasizes that pen again. The
  window opens with the first visible pen active and not emphasized: its row is marked and its axis
  drawn, and its line is 1 px wide until the first pick. A drag past the
  slop, a Ctrl + click, delta mode, the axis-region press and the time-axis press never reach `PickPenAt`,
  so they pick no pen
  and leave the emphasis as it is. The view registers `OnPointerReleased` on the tunnel route: `AvaPlot`'s
  own class handler calls `Capture(null)` on release, which ends the drag through `PointerCaptureLost`
  before a bubbling handler would run. Release ends the drag and restores the hand cursor. The hover
  readout and crosshair (an Avalonia overlay) are suppressed for the duration of the drag.
- **Ctrl + left button shifts the active pen.** `ChartPressRouter` routes a press with Ctrl held to
  `ChartPressAction.ShiftActivePen` after the axis-region check and ahead of the time axis and delta
  mode, so the held key wins over the navigation bar's toggle and over the time axis band. The press
  starts a drag the way a pan does: it captures the pointer and suppresses the hover readout and crosshair, and the cursor is
  `StandardCursorType.SizeNorthSouth`. Past the 4 px slop every move calls
  `TrendChartViewModel.ShiftActivePen` with the vertical part of the move; the horizontal part is ignored
  and the time window and sticky stay as they are. A release inside the slop is a Ctrl + click and does
  nothing: it picks no pen and places no delta cursor. The press fixes the drag's action in the view's
  `ChartDrag` record, so a Ctrl released mid-drag keeps shifting and a Ctrl pressed mid-pan keeps
  panning. Release and a capture loss end this drag as they end a pan.
- **Left button on the time axis: drag pans, click opens the time window panel.** A press in the band of
  the bottom axis (`ChartAxisRegion.TryCreateTimeAxis`: under the data rect, from the bottom axis panel's
  offset and size, bounded horizontally by the data rect) with Ctrl not held routes to
  `ChartPressAction.PanOrEditTimeWindow`. The press starts a drag the way a pan does, and a move past the
  4 px slop pans. A release inside the slop opens the time window panel (below) at the pointer and picks
  no pen: like the axis-region press, it never reaches `TrendChartViewModel.PickPenAt`, so the active pen
  stays as it is and keeps its emphasis. The branch sits ahead of the tool, so delta mode does not change a time-axis click and
  places no cursor there. A Ctrl press on the band shifts the active pen, as anywhere else.
- **Sticky to real-time by default.** A button detaches sticky (pan into the past); clicking it
  again re-attaches and returns to real-time. The constructor seeds the navigation bar's `IsSticky` from
  `Navigation.IsSticky`, and after that the `WindowChanged` handler is its only writer, refreshing it from
  the same flag, so auto-detach and `JumpToNow` re-attach stay in sync with the button.
- **Panning so the live edge scrolls out of the view** auto-detaches sticky.
- **Hover readout + crosshair (X-trace) live in an Avalonia overlay, not on the plot.** Moving the
  pointer does NOT trigger a ScottPlot re-render. The crosshair is an Avalonia `Line` and the readout
  an Avalonia `Border`/`TextBlock` laid out on a transparent `Canvas` over the `AvaPlot`
  (`Chart/TrendChartView`). On hover the view positions them by projecting the view model's cursor X
  through `Plot.GetPixel` and `Chart/ChartCursorOverlay` (cursor pixel X + `DataRect` + render scale →
  crosshair endpoints in DIP space; the readout hangs from the line's top end). A cursor X outside the
  data rect's X range hides the crosshair and the readout. The readout text is the pure
  `Chart/ChartHoverReadout.BuildContent` string: the local timestamp plus every *visible* pen's value
  at the cursor X (one line per pen; gap or missing pen → dash). A `Stepped` pen reads the value of the last
  point at or before the cursor X, the level its line holds there, and an `Interpolated` pen interpolates
  between its two neighbouring points (`Core/Trends/CursorReadoutModel`). With the pointer on a time marker the
  cursor X is the marker's moment, so the readout lists every visible pen's value there
  (`#the-time-markers`). The overlay is suppressed while a drag
  is in progress or delta mode is active (`IsDragging || IsDeltaModeEnabled`) and is repositioned from
  the chart's frame callback (after the plot's invalidation) and on `SizeChanged` so it tracks
  pan/zoom/resize/live-edge without per-event re-renders.
- **Delta cursors (Δt / Δy) via an explicit navigation-bar mode.** The bar's "Delta" toggle
  (`NavigationBarViewModel.IsDeltaModeEnabled`) sets the chart into `DeltaPlacement`: two left clicks
  place the cursors and drag does NOT pan; toggling off clears the placed cursors and hides the
  lines. Δt and the **active-pen** Δy (`Core/Trends/DeltaCursorModel` → `DeltaReadout`, each end read under the
  pen's line style as the hover readout reads it, Δy under the
  positive section of the pen's mask, `charting.md`) are shown in an inline readout next to the toggle.
- **Y-axis click-region scale edit.** A press on the active pen's Y-axis panel band
  (`Chart/ChartAxisRegion`, computed from the last render layout) is handled before Ctrl, the time axis,
  pan and delta routing and opens the axis scale panel (below). The same region routes the wheel to `ScaleActivePen`.
  The router reads no click count and no half of the axis. The press never starts a pan or places a
  delta cursor.
- **Horizontal cursor / crosshair** — not in the MVP; deferred as a NICE item
  (trend-feature-spec.md §CU-5), not permanently dropped.

### The axis scale panel

A press on the active pen's axis opens an Avalonia `Flyout` (`Chart/AxisScalePanel`, attached to the plot
control in `TrendChartView.axaml` and shown at the pointer) over `Chart/AxisScalePanelViewModel`, which
`TrendChartViewModel.AxisScale` owns. A press while no pen's axis is drawn opens nothing.

- The panel is opened for one pen, the pen whose axis is drawn (`TrendChartViewModel.DrawnPenId`: the
  active pen while it is visible). The header names that pen and shows its unit. Every action of the panel
  acts on that pen. The panel closes without writing when the pen leaves the chart, is hidden or stops being
  the active pen while the panel is open.
- Maximum and Minimum are `TextBox` fields seeded with the bounds the axis shows, rendered through
  `PenValueFormat.Format` with the pen's stored mask and the `0.###` fallback, as the legend row and the
  hover readout render a reading. A field left as seeded keeps the exact bound it was seeded from, so a mask
  that rounds (0.4 shown as "0") never moves the scale; only a field whose text changed is parsed.
  Typed text is read by `PenFormRules.TryReadBound`, the pen editor's rule, in the
  current culture: an entry that is not a number in that culture, such as "150.5" under `ru-RU`, is
  invalid, and the last value that parsed is never applied in its place.
  A field the keyboard reaches, the Maximum the opening flyout focuses or a field Tab lands on, selects its
  text, so the first key replaces the seeded bound; a click places the caret (`AxisScalePanel.OnGotFocus`).
- The panel has no apply button. The two fields are one edit, as the pen editor's scale pair is
  (`overview.md#the-pen-and-group-editor`), and the edit ends when keyboard focus leaves both: the view
  watches `IsKeyboardFocusWithin` of the grid that holds them, `AxisScaleBounds`, and calls
  `CommitBounds` when it turns false, so a Tab from one field to the other writes no half pair. Enter in
  either field runs `CommitBoundsCommand`, the same method; the two buttons and the panel take no Enter
  binding, so Enter on the focused Autoscale button presses Autoscale. The panel itself is focusable and
  no tab stop, so a press on its empty area, a label or the pen name gives the panel the keyboard through
  Avalonia's own press-to-focus, which ends the edit too. Keys then still route through the panel, so
  Escape keeps closing it. The panel's background is `Transparent` so that press reaches it; a press on
  the flyout presenter's padding around the panel does not end the edit.
- `CommitBounds` writes only a real change: a pair that reads, with the minimum below the maximum and
  admitted by the log rule, and that differs from the seeded pair's values. It writes both bounds as a
  manual scale on the pen through `SetAxisLimits`, then re-seeds from the chart, and the panel stays open.
  Focus passing through untouched fields, or a value retyped in another notation, writes nothing, so
  Enter on an untouched autoscaled pen keeps it `Auto` rather than freezing the shown range into a manual
  one.
- A mouse gesture that rescales the panel's pen (the wheel over its axis, Ctrl + wheel) closes the panel
  through `AxisScalePanelViewModel.CloseWhenOpenFor`, which clears the pen before the flyout hides, so
  nothing is written. A light dismiss reacts to a press, not to a wheel, so the panel would otherwise stay
  open on the old seed, and a commit of one typed bound would write the other seeded bound back over the
  gesture.
- A dismiss from outside (a press outside the panel, a click on the window frame, the window losing focus
  or activation) writes a pending valid pair once and closes the panel. Avalonia raises the flyout's
  `Closing`, then detaches the panel, which takes focus off the field and so ends the edit through the
  same `CommitBounds`; the panel has no close-time write of its own, and the host does not handle the
  flyout's `Closed`. Escape runs `CancelCommand`, and the panel's own close requests run `RequestClose`;
  both clear the pen before the flyout hides, so the detach finds no pen and writes nothing. Escape thus
  discards the pending pair and keeps a pair already written. A field that is invalid when the panel
  closes is discarded, and the axis keeps its bounds.
- An unreadable field, an empty field or a minimum not below the maximum is invalid on every edit: the
  field takes the `invalid` border, the message line names the rule, and no commit writes the pair. While
  the axis is logarithmic, a minimum ≤ 0 is invalid the same way, under `ScaleLogMinimumPositive`. The
  panel keeps its size (`ui-theme.md#the-axis-scale-panel`).
- The Logarithmic scale checkbox sits in its own row under the bounds, outside `AxisScaleBounds`, so a
  press on it ends the pair's edit. It reads the pen's session flag, `IsLogarithmic`, `Mode=OneWay`,
  seeded from the chart's settings at every seed, and writes it only through `ToggleLogarithmicCommand`,
  which orders its two writes by direction. Switching on, the pending pair is in the chart before
  `TrendChartViewModel.SetLogarithmic(penId, true)` checks the log rule: in the view the press or Tab onto
  the box has already moved focus off the pair, which wrote it, and the command writes a pending valid
  pair itself first, so the view model keeps the order without the view. Switching off, it calls
  `SetLogarithmic(penId, false)` first and then writes the pending pair under the linear rule, so a
  minimum of 0 typed on a log axis lands. Either way the panel re-seeds and stays open: the fields show
  the bounds of the new axis.
- The command refuses in two cases, and the box then stays as it was. A pending pair that does not read as
  a pair (a field empty or not a number, or the minimum not below the maximum) stops it before any write:
  the typed text stays and the message line names the rule the pair breaks. Switching on, the pending pair
  is written first; the chart then refuses the flag over a manual minimum ≤ 0, so the axis keeps the
  written bounds on a linear scale (a linear 5..100 with 0 typed ends linear at 0..100), and the panel
  keeps the box clear and shows `ScaleLogMinimumPositive` on the message line until the operator edits the
  minimum or the panel seeds again. That refusal marks no field invalid, because the minimum is valid on a
  linear axis; the way out is to type a positive minimum and tick the box again. On an autoscaled pen with
  untouched fields the switch always lands. The box reaches only this window's chart; the stored flag is
  the pen editor's (`overview.md#the-pen-and-group-editor`).
- Autoscale is a `ToggleButton` that shows whether the panel's pen is in `Auto` mode: it reads
  `IsAutoscaled`, `Mode=OneWay`, seeded from the pen's `ScaleMode` at every seed, and writes only through
  `ToggleAutoscaleCommand`. Switching on calls `AutoscalePen`; switching off calls `SetAxisLimits` with the
  range `Auto` shows, so the axis stays where it is and becomes manual. Either way the panel re-seeds and
  stays open, so the fields show the new bounds and the button its new state. Restore initial scale calls
  `RestoreInitialScale(penId)` and closes the panel. A press on either button takes focus off a typed
  pair, which is written first and turns the pen manual; the button's own result then replaces it.
  Autoscale keeps the axis type; Restore initial scale sets the stored one. The View -> Pen scale submenu items call `AutoscaleActivePen` and
  `RestoreInitialScale()`. The submenu header names the drawn pen and follows `DrawnPenId` and the
  catalogue's pen names; while no pen's axis is drawn it reads "Pen scale".

### The time window panel

A click on the time axis opens an Avalonia `Flyout` (`Chart/TimeWindowPanel`, `Placement="Pointer"`) over
`Chart/TimeWindowPanelViewModel`, which `TrendChartViewModel.TimeWindow` owns and builds from the chart's
own coordinator, UI scheduler and time zone. The flyout lives in `TrendChartView`'s `UserControl.Resources`
under the key `TimeWindowFlyout`. A flyout in resources inherits no
`DataContext`, so `TrendChartView.OnDataContextChanged` hands the panel its view model, as it does for the
axis scale panel. The view calls `Seed()` and then `ShowAt(PlotControl)`; the panel's `CloseRequests` hide
the flyout. It covers §TM-6 except the quick interval "ending at now": a width keeps the right edge, which
under sticky is now.

- Three rows: Start and End, each a 110 px `HH:mm:ss` `TextBox` and a `CalendarDatePicker`, and Width, a
  `TextBox` in `h:mm:ss` with unbounded hours (`Navigation/WindowWidthText`: `0:05:00`, `168:00:00`) and a
  presets `DropDownButton`. The fields show local time in the zone handed to `TrendChartViewModel`,
  production's `archiveZone`, the machine zone the axis draws in. `Navigation/TimeWindowForm` holds the
  fields; `Seed()` fills them from `Navigation.From` and `To`, each floored to the second.
- Three writes, each through a navigation call that raises `WindowChanged`, so the re-query, the axis, the
  minimap and the navigation bar follow as for any gesture. The edges go through
  `ChartNavigationController.SetWindow`: both edges exactly, sticky dropped. The width and the presets go
  through `SetWidth`: the right edge stays, the left edge moves, sticky stays as it was. Both clamp the width
  to 1 s .. 365 days, and a left edge before `FirstSample` moves the window right to start there with its
  width kept, the rule `Pan` and `Zoom` use. The presets are 1, 2, 3, 5, 10 and 30 min and 1 h; each item
  of the `MenuFlyout` runs `SetWidthCommand` with its width at once. The menu keeps no selection, so the
  Width field is the width's only display.
- A write happens only for a real change. "Differs from the seed" compares the fields' text and dates with
  what `Seed()` wrote, never the parsed pair with the navigation's ticks, so Enter on untouched fields over a
  window whose `From` carries a fraction of a second writes nothing and keeps sticky. The form compares
  each edge with its own seed (`StartDiffersFromSeed`, `EndDiffersFromSeed`), and an edge left as seeded
  is written as the navigation holds it at the commit, not as it was seeded: under sticky the live edge
  moves both edges while the panel is open, so a typed start keeps the newest data in view and a typed end
  keeps the left edge where the live edge moved it. When the edge in force and the typed one are out of
  order, or more than 365 days apart, the pair as typed is written instead. After a write the panel
  re-seeds every field from the navigation and stays open, so it always shows the window in force. Invalid
  edges write no edge and a valid changed width still lands; an invalid field keeps its typed text until a
  write re-seeds the form, so a form with nothing valid to write keeps all of it.
- The edges and the width validate apart, so a bad width never blocks the edges. The edge rules, in the
  order the message line names them: the start date missing, the start time unreadable, the end date
  missing, the end time unreadable, the end not after the start, a time the spring-forward change skips
  (`zone.IsInvalidTime`), and a span over 365 days measured between the UTC edges. The skipped time comes before the span because a skipped time has no UTC
  value; an ambiguous autumn hour takes the standard offset (`data-integration.md#time-boundary`). The width
  rules: unreadable, or outside `0:00:01` .. `8760:00:00`.
- The panel commits on Enter, on a preset and on close, never on focus loss. The axis scale panel commits when
  focus leaves its field group, and that rule fails here: a `CalendarDatePicker` moves focus into its own
  popup root when its calendar opens, which clears the group's `IsKeyboardFocusWithin` and would write a
  half-edited pair. Opening a calendar and picking a day in it write nothing and keep the panel open.
- Enter and a close write the same thing: `Commit()` writes valid pending edges, then a valid pending
  width, and re-seeds once after a write. A width typed beside a typed start therefore applies on Enter in
  any field, not only on Enter in the Width box, and the width written last keeps the right edge.
- Enter is a `KeyBinding` to `CommitCommand` on the two time boxes and on the width box, never on a picker
  or the panel root. A `KeyBinding` walks the visual parents ahead of `KeyDown` and marks the key handled
  before the picker's own Return parse runs, so Enter bound above a picker would commit the date it held
  before the typed one. Each picker instead has a `KeyUp` handler for Return that calls `Commit` after its
  own parse. Escape is a `KeyBinding` on the panel root to `CancelCommand`, which marks the open cancelled
  and raises `CloseRequests`.
- `OnUnloaded` calls `Close()`, and every close reaches it: Escape, a light dismiss and the host's `Hide`.
  Unless the open was cancelled, `Close()` runs `Commit()`. Children detach before the panel, so a focused
  picker has parsed its text and every
  `TextBox` has written its text when `Close()` runs. The host handles no flyout `Closed`. `OnLoaded` calls
  `Opened()`, so a day read starts only for a panel that `Close()` will also reach; a hide before the
  posted `Loaded` skips both, and every view test runs `Dispatcher.UIThread.RunJobs()` after `ShowAt`.
- The days live in `Navigation/ArchiveDayCalendar`, the panel's `Days`; the export window holds its own
  instance with the same rules (`overview.md#the-export-window`). The calendars offer `[FirstDay, LastDay]`
  (`DisplayDateStart`, `DisplayDateEnd`). `FirstDay` is the
  earlier of the first sample's local day and the selected start day, because `TrackDataExtents` can open
  a window that starts before `FirstSample` and the calendar would clamp a selected date outside its range.
  `LastDay` is the later of the newest sample's local day and the selected end day. The newest sample's
  day is the local day of `Navigation.NewestSample`, never a clock read; with no newest sample yet, the
  panel reads no days.
- `Opened()` reads which days of `[FirstDay, newest sample day]` hold a row, once per open, through
  `TrendCoordinator.QueryArchiveDaysAsync` (`data-integration.md#operation-to-statement`), observed on the
  UI scheduler. `Close()` cancels a read in flight and forgets the result, and the next open reads again. A
  failed read hands its errors to `TrendChartViewModel.ReportFailure(IReadOnlyList<IError>)`, which reaches
  the message panel, and the calendars then show no blackout day. `BlackoutDays` is each read day without a row, inside
  `[FirstDay, LastDay]`, minus the two selected dates: a picker draws it struck through and does not let
  it be picked.
- The blackout order. `CalendarBlackoutDatesCollection` throws when a range covers a selected date of its
  calendar, and the calendar throws when a selected date is set to a blackout date. `Seed()` therefore
  empties `BlackoutDays`, writes the dates, then refills it. The sync into each picker's `BlackoutDates`
  and the `SelectedDate` binding chain (form, picker, the template's calendar) run synchronously on the
  property change, so no write meets a conflicting set. `BlackoutDates` is not bindable and is `null`
  until the picker applies its template, so the code-behind syncs it by hand: the subscription is made on
  `DataContextChanged` and lives as long as the view model, not one open, because `Seed()` runs before
  `ShowAt` while the panel is unloaded and its empty set has to reach the pickers then. Each picker's
  `TemplateApplied`, subscribed in the constructor because the first `ShowAt` applies the templates before
  `Loaded`, replays the current set. `TimeWindowPanelViewTests.AReopenOntoABlackoutDay_SeedsWithoutThrowing`
  fails under a sync scoped to one open.
- The chart hides the flyout on its window's `Closing`. Avalonia 12.0.5 throws `ArgumentOutOfRangeException`
  in `StyledElement.OnDetachedFromLogicalTreeCore`, from `TopLevel.HandleClosed`, when a window closes while
  a picker's calendar popup is open inside the flyout. `Closing` runs ahead of that detach, so
  `TrendChartView` subscribes to it in `OnLoaded`, removes it in `OnUnloaded`, and hides
  the time window flyout there; the panel's `Close()` then writes what was typed, as on any close but Escape.
  `ChartPointerInputTests.ClosingTheWindowWithAPickersCalendarOpen_ThrowsNothingAndWritesThePendingPair`
  gates it.
- The panel keeps its size under validation (`ui-theme.md#the-time-window-panel`).

### The time markers

A time marker is a stored vertical line at a moment, with an optional label of at most 80 characters: the
operator marks an event worth returning to, the start of a process for one, and finds it again after a
restart and in every other window (`overview.md#the-live-markers`, `data-integration.md#the-marker-store`).
The count has no cap: every window reads every marker and draws every shown one inside its time window.
The SCADA adds recipe markers of its own, locked, at the start, the end and the interruption of a recipe. A marker
list and bookmark navigation are not built (`trend-feature-spec.md` §CU-4); the minimap strip marks every
marker of the archive with a tick (`#archive-overview-minimap`), and a press there brings it into the
window.

- **Drawing.** `Chart/ChartMarkerLines` owns one ScottPlot `VerticalLine` per marker, pinned to
  `plot.Axes.Bottom` with `EnableAutoscale = false`, 1.5 px wide. A locked line is solid and an unlocked
  one dashed; an operator's marker (`MarkerKind.User`) draws in the marker colour and a recipe marker in
  the recipe colour (`ui-theme.md#the-plot`). The label is the line's own label with `LabelOppositeAxis`,
  `ManualLabelAlignment = UpperLeft`, offsets of 4 px and 12 px and a transparent background, so it hangs
  inside the data area under its top edge, right of the line, and draws no box. The default label covers
  the time axis ticks, and the strip above the data area is too narrow for it. The label text is the
  marker's label, a middle dot and the marker's local time as `HH:mm:ss`, or the time alone for an
  unlabelled marker. The time carries no date, which the hover readout shows.
  `Sync(markers)` is the one diff on the view side: it adds the line of a new id, removes the line of a
  gone one, and sets the moment, the label text, the pattern and the colour of every other line, since a
  read can change the lock and the kind. The view calls it when it binds a view
  model and on every `Changed`, then requests a frame. `RemoveLines()` takes every line off the plot when
  the view binds again, so a rebind leaves one line per marker.
- **Labels that do not fit.** `Project(from, to, dataAreaWidth)` hides a line outside the window, and a
  label whose estimated width, 4 px plus its length times 0.6 of the font size, does not fit before the
  next visible marker. The estimate needs no render and runs wider than the drawn text: "Etch start"
  draws at about 62 px against an estimate of 88 px, so a label hides before it would touch the next one.
  The view projects on `WindowChanged`, on `SizeChanged`, where the width is the new plot width minus the
  last render's margins, after every `Sync`, and after a render whose data area width changed, the one
  case a pen axis that appears or goes narrows or widens the data area without a resize. `Sync` builds
  its id set only when a marker went.
- **The active pen's readings.** At every marker inside the window, `Chart/ChartMarkerReadings` draws a
  dot on the active pen's line, in the pen's colour, and the pen's value there beside it, formatted by
  `PenValueFormat` with no unit, as the hover readout shows it. The other pens show no value at a marker,
  so many pens do not crowd it; picking another pen moves every reading to that pen, and the hover readout
  still lists every visible pen. The value is the one the cursor reads (`ChartCursorReader.ReadPenAt`), so
  a hidden active pen, a gap or a marker outside the pen's data draws no reading, and a marker inside the
  held span past the last column reads the held value (`charting.md#per-pen-plottable-envelopeline`). The
  layer is one plottable with no axis limits; the view refreshes it in `ServeFrame` before every redraw,
  because a history result, a live batch with or without a sample, a pick, a visibility change, a marker
  change and a pan all reach the chart as a frame request. It projects through the pen's own
  `EnvelopeLine.PixelOf`, so a log10 axis places the dot on the drawn line.
- **The hit test.** A pointer inside the data rect is on a marker when `MarkerSet.NearestWithin` finds one
  within `AnchorAt(x + 4) - AnchorAt(x)`, 4 px at the pointer in the time scale of the window shown. The
  bound is inclusive, and the lower id wins a tie. Before the first render the data rect has no width:
  no hit test runs, and the hover is skipped whole. The pointer's moment comes through
  `LocalTimeAxis.FromAxis`, which gives one instant for a pixel of the repeated autumn hour, so a marker
  another client stored in the other half of that hour draws at its X but is never hit, and an add or a
  drop there lands in the one half (`data-integration.md#time-boundary`).
- **Hover.** On a marker the cursor snaps to the marker's moment: `MoveCursor` receives that moment, and
  the hover readout lists every visible pen's value there. The pointer shows `SizeWestEast` only when a
  press would drag the marker, so not with Ctrl held, in delta mode or on a locked marker, and the hand
  elsewhere. While a
  marker drags no readout follows it, and in delta mode the overlay stays hidden, as for every drag.
- **Drag.** A press on an unlocked marker routes to `ChartPressAction.DragMarker` after the axis region, Ctrl, the
  time axis band and the tool. Ctrl therefore shifts the active pen over a marker, the time axis band
  opens its panel under one and never moves it, and delta mode places its cursor on top of one. The press
  captures the pointer and shows `SizeWestEast`. Past the 4 px slop the first move calls
  `ChartMarkers.BeginMove`, which holds the marker against reads, and every move calls `Move`
  with the pointer's moment: the marker jumps to the pointer rather than keeping an offset, a jump of at
  most 4 px. The drag commits in `TrendChartView.EndDragAsync`, the one place every drag ends, on the view
  model the press began on, so a `DataContext` change mid-drag still ends the first one's move: a release
  reaches it through the capture loss it raises, and a capture lost for another reason ends there too,
  as a pan keeps the window it reached. `EndMoveAsync` writes the moment of the last move, once. A release
  inside the slop is a click: it picks a pen, as a pan press does, and writes nothing.
- **The lock.** A locked marker ignores the two gestures that change a marker by accident, the drag and
  the double click that deletes. `PressRegionAt` returns `ChartPressRegion.LockedMarker` for it, and the
  router reads that region after the tool and maps it to `PanOrPickPen` for any click count: a drag on it
  pans, and a double click on it neither deletes it nor adds a marker. The menu still finds it, so
  `Rename`, `Delete` and `Unlock` act on it. The lock guards the gestures of the window that shows it,
  not the stored row (`data-integration.md#the-marker-store`).
- **Add and delete.** A double click adds a marker at the pointer's moment, and a double click on a marker
  deletes it. A new marker is labelled `#N`, N one past the highest number among the labels of the form
  `#` and decimal digits that the operator's markers carry, shown or hidden, or `#1` when there is none;
  a recipe marker's label does not count; the label is stored and `Rename` changes it
  like any other. Two windows that add at once, or two quick double clicks in one window before the first
  add's id lands, can store the same number. The router reads the press's `ClickCount`: a second click routes to
  `ChartPressAction.AddMarker` where a single one would be `PanOrPickPen`, and to `DeleteMarker` where a
  single one would be `DragMarker`, so only in the pan tool, without Ctrl and off a locked marker. In
  delta mode a double click places the delta cursors, and on an axis it opens that axis's panel. A third click is a single one again,
  so a quick second double click on a new marker drags it rather than deleting it. The first click of the
  pair is an ordinary click and picks a pen. The marker shows once the add's id lands, and a failed add
  places nothing. The delete asks for no confirmation: a double click puts a lost marker back, and
  `Rename` its label.
- **The right-click menu.** The tunnel release handler checks for the right button ahead of its
  left-button guard. A right release inside the data rect with no drag under way opens a `MenuFlyout` at
  the pointer through `ShowAt(PlotControl, true)`, as the two panels open: `MarkerMenuFlyout` (`Rename`,
  `Lock` or `Unlock`, `Delete`) on a marker, `ChartMenuFlyout` (`Add marker here`) elsewhere. The view
  keeps the clicked moment and the marker hit. `Add marker here` adds a numbered marker at once, as a
  double click does. No `ContextFlyout` is declared, because `ContextRequested` may never fire on
  `AvaPlot`. With no pens `EmptyCatalogueMessage` covers the centre of the plot and takes the click there;
  a marker without pens has no use.
- **The label editor.** One keyed flyout, `MarkerLabelFlyout`, opened by `Rename` only: a focused
  `TextBox` with `MaxLength = 80`, a message line, and OK and Cancel, at a fixed 360 px
  (`ui-theme.md#the-marker-label-editor`). It opens on the current label. Enter or OK trims the text,
  closes the editor and writes, and an empty result is no label. Esc, Cancel or a click outside closes it
  and writes nothing. A renamed label shows at once; a failed write reverts it to the label the menu
  opened on (`overview.md#local-state-first`) and the editor stays closed. If another window renamed the
  marker in between, the read every write ends with brings that label back at once. The read after an add
  or a move carries the moment rounded to the microseconds `timestamptz` keeps, so the marker set changes
  once more by under a microsecond, which redraws nothing visible.
- **Delete.** `Delete` removes the marker once the store answers, and a failed delete keeps it.
- **Lock and Unlock.** One item, `MarkerMenuLock`, whose header `OpenChartMenu` sets from the marker's
  lock before the flyout shows. It writes the opposite of that header state through
  `ChartMarkers.ToggleLockAsync`, which starts from the marker as the set holds it at the click: the line
  changes its pattern at once, and a failed write restores the lock that read stored.
- **Hiding the recipe markers.** `View` -> `Recipe markers` hides the recipe markers in one window. The
  flag is `MarkerSet.ShowsRecipeMarkers`, flipped through `ChartMarkers.ToggleRecipeMarkers`, which
  raises `Changed`. It is not stored, and a new window starts with the recipe markers shown. A hidden
  marker leaves `ShownMarkers`, so it loses its line, its reading, its hit and its minimap tick, and stays in
  `MarkerSet.All`, so a revert or a `MarkerGone` of a write that began before the hiding still lands.

## Decimation & performance (architectural core)

Underwrites "no lag from 1 s to 1 year." A data-layer requirement, not a chart-control feature.
Requirements: archive aggregation layers (trend-feature-spec.md §DA-2), auto layer-by-width with
hysteresis (§DA-3), decimation to the canvas width with surviving spikes and gap anchors (§DA-5),
and the render budget (§RT-4). The as-built rationale and mechanics:

- **Never feed raw millions of points to the chart.** A 1080p-wide plot shows ~one point per
  pixel; a month of 10-second data is ~135 raw samples per pixel (MasterSCADA). The data layer
  returns roughly viewport-width points (§DA-5).
- **Window width, canvas width and visible pen count together select the archive layer** (raw /
  minute / hour): deeper ranges use coarser layers — the layer design in data-integration.md, validated
  by industry (MasterSCADA layered archive; AVEVA "Cyclic" retrieval). No ceiling is a constant: it is
  `nextCoarser(layer).ToPointSpacing() × TargetColumnCount × min(1, ReferencePenCount / max(1, visiblePens))`,
  so it moves with the canvas and shrinks past 16 visible pens (`data-integration.md#layer-ladder`). At
  256 columns and 16 visible pens or fewer Raw reaches 64 min and Minute 64 h; at 2048 columns Raw
  reaches 8 h 32 min and Minute 512 h. Hour has no ceiling and reads every wider window, to the 365-day cap.
  `ChartNavigationController.LayerForWidth` applies a **10% hysteresis band** at each ceiling so a
  notch-by-notch zoom hovering on a boundary does not flip-flop the layer every notch (§DA-3) —
  which, at the Raw side, appended a far-right raw point that straight-lined across the wide span.
  The column count carries **its own deadband** on the same grounds: one quantisation step doubles or
  halves every ceiling, so a pixel of jitter across a boundary must not move it.
- **Gaps come from the break markers, not from empty spans** (§DA-5). The archive writes a row only
  when a value changes, so a span with no row is no gap by itself. A history read anchors a `NaN` column
  by one route at every layer: the server closes a bucket at a `q = 32` marker, and `BucketedRowFold`
  writes the `NaN` column one tick after that bucket, so the line breaks there instead of bridging the
  missing span with a straight line (see `data-integration.md`, Quality and gaps). At the live edge the
  poll emits a `q = 32` row's value and then a null sample 1 ms later, which the chart appends as a `NaN`
  column at every layer (`data-integration.md#realtime`). No decimator runs on the client.
- **Use a min/max-per-pixel envelope, not plain sampling.** Plain decimation aliases away spikes
  (AVEVA warns of exactly this). Retain min AND max per pixel column so spikes survive (M4:
  min/max/first/last per column → visually lossless; MinMaxLTTB for speed; Power Chart "MinMax").
- **Every layer aggregates in the query** (§DA-2): `BucketedHistoryWindow` (`GROUP BY date_bin`)
  returns one row per column per pen at `Raw`, `Minute` and `Hour`, so neither the raw stream nor a
  whole coarse layer reaches the client, and no decimator runs there. `BucketedRowFold` maps the rows
  onto the one envelope vocabulary the chart reads, behind `IDataProvider`.
- **Performance budget (§RT-4):** at most one redraw per display frame on pan/zoom; input data ≤ 10 Hz;
  sized for 50 simultaneous pens, with no cap in code;
  points per pen handed to the chart ≈ viewport width × 2–4.

## Data quality & line rendering

- **Gap / bad-quality rendering** (trend-feature-spec.md §DA-8): nulls and quality (archive `q`
  column) render as visible gaps, not interpolated across.
- **Stepped vs interpolated lines** (§PN-5): configurable per pen (stepped for discrete/digital
  tags like valve on/off; interpolated for analog). Mapped to the renderer in `TrendPenState`.

## Time handling

Local-time display is required by trend-feature-spec.md §TM-1 (the single `LocalTimeAxis`).
As-built: samples are UTC over the wire (data-integration.md). All UTC↔OADate conversion is
funneled through `Chart/LocalTimeAxis` at every render boundary (plotted X, axis limits,
cursor/delta X), with a `DateTimeAutomatic` tick generator on the shared bottom axis so labels
read local time. Across a daylight-saving change the axis follows the machine's local-time conversion,
and `LocalTimeAxis.FromAxis` gives one instant for a pixel of the repeated autumn hour. The time window
panel refuses a time the spring change skips and gives an ambiguous autumn time the standard offset
(`#the-time-window-panel`). The archive's naive times follow `data-integration.md#time-boundary`.

## Legend

Required by trend-feature-spec.md §PN-8. As-built: a grouped sidebar whose row carries the on/off
box, a round colour dot, the name, the current value in the pen's own mask and the unit
(charting.md). A pen in several groups is listed under each of them and switching it off under one
header switches it off under all. Every drawn header carries a switch whose state is derived from its
pens: on when all are on, off when all are off, indeterminate when mixed. A click on a mixed or off
header switches every pen of the group on, on an all-on header off, and every other header a shared
pen sits under re-derives. A header reads as a section caption, small grey capitals under a thin line,
with its rows indented beneath it; the active pen's row is marked by an accent background and an accent
bar on its left edge, not by bold text. The panel has two states: collapsed, it leaves the box, the dot and the
name. A handle on the panel's left edge drags its width in either state; each state remembers its own
width for the session, and a restart returns both to their defaults. The value at the cursor is the
chart's own hover readout (§CU-2), not a legend cell. The row shows no scale range: the padded axis
bound would read as the pen's measured extremes.

## Archive-overview minimap

Required by trend-feature-spec.md §TM-4. As-built: a 36 px overview strip beneath the chart shows
the **full archive extent** with the current `[From, To]` view window highlighted over a min/max band
of the drawn pen, for orientation and fast navigation across long archives. A label row under the
strip names its two ends and, while the pointer is over the strip, the time under it.

- The extent comes from a new `IDataProvider.QueryArchiveExtentAsync()` seam returning an
  `ArchiveExtent(FirstUtc, LastUtc)` (data-integration.md), which `PostgresDataProvider` answers
  with the true bounds of the configured variables; an archive with no rows answers
  `ArchiveExtent.Empty`, and the strip stays blank.
- `Minimap/MinimapViewModel` reaches the archive through `TrendCoordinator.QueryArchiveExtentAsync()`
  and `QueryHistoryAsync` on the UI scheduler; it never holds the `IDataProvider` directly. It takes
  the chart (`TrendChartViewModel`) and reads its `Navigation`, `DrawnPenId`, `Pens` and `FindPen`.
  The band is its own part, `Minimap/MinimapBandFeed`, which the view model builds, exposes as
  `BandFeed` and disposes. The strip's geometry is pure (`Core/Trends/MinimapGeometry`).
- The strip reads the extent at start (`TrendWindow.StartExtentLoad`), before a catalogue delta gives
  an empty chart its first pens, and after a delta adds a pen to a chart that already has some
  (`overview.md#what-a-read-changes`). `LoadExtentAsync` returns the read once the strip has applied
  it. Both callers widen the navigation's first sample to an earlier one in that read
  (`TrendChartViewModel.WidenToArchiveExtent`), so the chart pans back as far as the strip draws and
  reads the rows a moved floor brings into the window in view; the empty-chart caller seeds the
  navigation from the same read first. A read that leaves the strip blank, failed or empty, is read
  once more on the next `NewestSampleMoved`: a sample exists, so the archive has rows. One such read
  runs at a time, only a read that again leaves the strip blank arms the next one, and the next one
  waits for a sample at least 60 s after the previous retry started, so an extent read that keeps
  failing while the live edge works costs one read a minute. The extent reads share the band's
  outage policy (see Failures).

**The bounds.** The left bound (`ExtentFirst`) is the extent's first sample. The archive is never
pruned, so it moves only when a reloaded extent starts earlier, and no periodic extent read exists.
The right bound (`ExtentLast`) is `MinimapGeometry.RightBound`: the later of the extent's last sample
and `ChartNavigationController.NewestSample`, the newest sample the chart has seen. `TrackDataExtents`
writes it once, from the archive seed or the chart's first history, and afterwards only the live edge
(`OnLiveEdge`) moves it. `NewestSample` is null until one of the two writes it, so the controller's
wall-clock start never becomes a bound, and it only moves forward. `NewestSampleMoved` fires on every
forward move, sticky or not; when the window also moves, it fires after the move and ahead of
`WindowChanged`. The minimap recomputes the right bound, `ExtentLastLabel` and the window fractions
on it. An extent applied after a newer sample takes that sample as its right bound.

**The marker.** `Minimap/MinimapView` draws the strip on a Canvas, `StripCanvas` (not a second
`AvaPlot`). A highlight border marks the window: `MinimapGeometry.MarkerSpan` turns
`WindowStartFraction` and `WindowWidthFraction` into a width of
`min(strip, max(6 px, widthFraction × strip))` and a left edge clamped to `[0, strip − width]`, so the
marker lies whole inside the strip at any zoom and window position. Press/drag converts pointer-X to a
fraction → `NavigateToFraction`, which recenters the window via the **same**
`ChartNavigationController` the chart navigates with. The highlight tracks every `WindowChanged` (pan /
zoom and the sticky live-edge advance) and every move of the right bound.

**The time markers.** The strip draws one full-height 1 px tick per shown time marker inside the extent,
recipe markers included, in `AppMarkerLineBrush`, over the highlight and under the hover line
(`#the-time-markers`). The ticks show where the markers outside the window are; a press on one navigates as a press anywhere on the strip does,
so the ticks take no pointer. `MinimapViewModel.TimeMarkerFractions` comes from
`MinimapGeometry.FractionAt` over the chart's `ShownMarkers`, which drops a marker outside the extent, and is
recomputed on every `ChartMarkers.Changed`, on the extent read and on every move of the right bound.
The view draws the fractions as one `Path` of `LineGeometry` children, `TimeMarkerTicks`.

**The label row.** `MinimapView` is a two-row grid: the 36 px strip (`StripCanvas`) on top and
`LabelRow`, one line of 10 px text with a 4 px side margin, under it. The strip draws the band, the
marker and the hover line and nothing else; no baseline runs through it, because a line through the
middle of the band is no zero and no value. The chart's own time labels sit directly above the strip,
so the end labels sit under it: `ExtentFirstLabel` at the row's left edge and `ExtentLastLabel` at its
right edge, both `MMM d HH:mm` in local time and in `AppSecondaryForegroundBrush`. `MainWindow.axaml`
sets no height on the minimap row, so the row takes the view's own height, the same with empty labels
as with filled ones.

**The hover.** The code-behind takes `PointerMoved` and `PointerExited` on `StripCanvas` and hands the
pointer's strip fraction to `MinimapViewModel.HoverAt`, or calls `ClearHover`; a strip with no width
hovers and navigates nothing. `HoverAt` does nothing without an extent. The view model publishes
`HoverFraction` (`double?`, null while no hover shows) and `HoverLabel`, the time
`MinimapGeometry.TimeAtFraction` gives at that fraction, formatted as the end labels are; a move of
either bound relabels it. The view draws `HoverLine`, a 1 px `Border` above the marker in the chart
crosshair's `AppSecondaryForegroundBrush`, at the pointer's x, kept inside the strip by
`MinimapGeometry.SpanLeftWithin`. `MinimapGeometry.PlaceHoverTime` centres `HoverTimeLabel` under the
line in the label row, clamped inside the row, and decides which end label it covers from the widths of
the time and of the two end labels. The view measures the three texts as it places them, because a new
extent or a newer sample relabels an end label ahead of the layout pass. An end label the time covers,
or comes within the view's `EndLabelCoverDistance` of 6 px, takes `Opacity` 0, so its layout stays put
and it shows again once the time moves away. A press and drag navigate as before, and the line follows the pointer. A
captured drag keeps delivering moves past the strip's edges, so a move off the strip clears the hover;
capturing the pointer raises `PointerExited` while the pointer is still over the strip, so
`PointerExited` is ignored while a drag holds.

**The band read.** `MinimapBandFeed` runs one read pipeline on the UI scheduler. Three things request
a read: an applied non-empty extent, a change of `DrawnPenId` (`WhenAnyValue` drops a raise with the
same value, and the value at construction is skipped), and the next-read schedule. Each request
snapshots the drawn pen, the bounds and the layer on the UI thread, drops the scheduled read, and
`Switch` cancels the read in flight through its `CancellationToken`. A request for a pen other than the
one `Band` holds turns `Band` null at once, so the band never shows one pen's shape in another pen's
colour, even when the new pen's read fails. An answer that lands after a newer request, already queued
on the UI scheduler when `Switch` dropped it, is discarded and schedules nothing. A read asks
`QueryHistoryAsync` for `[DrawnPenId]` over `[ExtentFirst, ExtentLast]` at
`MinimapBandFeed.MinimapColumns` (250) columns and logs its layer, column count and duration at Debug.
`Band` holds the drawn pen's envelope from the answer, null when the answer has none. With no drawn pen
or no extent the request is empty: `Band` turns null and no read runs or stays scheduled, so a chart
that draws no pen issues no band reads until one is drawn again.

**The overview layer** is `ChartNavigationController.LayerForWidth(span, the band's previous layer,
250, 1 visible pen)`: the chart's own ladder at the strip's column count, so no second ladder exists, and
the chart's pen count never shrinks the strip's ladder. 250 columns
read `Raw` up to about 62 min, `Minute` up to about 62 h and `Hour` beyond, and the hysteresis keeps
a span at a boundary on the layer it already has. Every coarse read, `Minute` or `Hour`, includes the provider's raw fresh tail, up to four point spacings of one pen
(`data-integration.md#layer-ladder`). 250 columns are about 6 px each on a 1500 px strip. 250 is below
the chart's 256-column floor (`Chart/HistoryColumnTarget.MinColumns`), so no chart read asks for it,
and the test fake tells the band's reads from the chart's by that count
(`FakeDataProvider.BandHistoryQueries`).

**The next-read schedule.** When a read lands, success or failure, the next read is scheduled
`MinimapBandFeed.NextReadDelay(span)` later: `clamp(span / 1000, 2 s, 60 s)` over the span that read
covered. A requested read drops the scheduled one, so a read slower than that delay is never cut off by
its own successor; the next read follows it one delay after it lands. Between reads the right bound
grows and the band's right end trails it by up to one delay; on a 1500 px strip that is 5 px at a
10-minute span, 1.5 px from 33 minutes to 16.7 hours, and past 16.7 hours a read a minute whose trail
shrinks with the span, about 1 px on a day and 0.15 px on a week. The schedule keeps reading while the
operator hides the minimap row, at the same cost as when the strip shows.

**The band's shape** is pure geometry. `MinimapGeometry.BandFigures(band, first, last, width,
height)` maps the envelope to `BandFigure(Outline, CenterLine)` values in control coordinates, each
point a `BandPoint(X, Y)` (`Core/Trends/BandFigure.cs`). A column is drawn when it lies in `[first,
last]` and its min, max and centre are all finite; a NaN or an infinite value is a break. Each run of
drawn columns is one figure: its outline runs along the max line forward and the min line back, and its
centre line follows `Center`, so a break in the archive is a gap in the band. A run of one column is
drawn 2 px wide, kept inside the strip, so a short island between two breaks stays visible. The y range
is the pen's own, the min and max of the drawn columns, widened through `Core/Trends/ValueRange.Widen`,
the NaN-skipping rule `PenScaleModel` uses for its auto scale. The maximum maps to the top and the band
is never logarithmic. A flat range is padded by ±1, so a flat pen draws a band at mid-height. A zero
span, width or height, or an envelope with no drawn column, gives no figure.

**The band control.** `Minimap/MinimapBand` is a `Control` with a `Render` override (avalonia.md,
custom drawing) and reads nothing. `Figures` and `BandColor` are styled properties registered with
`AffectsRender`; `Render` fills each outline with `BandColor` at 35 % opacity and strokes each centre
line 1 px wide at full colour. It sits below the marker and the hover line, with
`IsHitTestVisible="False"`, so a press on the band reaches the strip. `MinimapView.LayoutBand`
sizes it to the strip and computes its figures from `Band`, `ExtentFirst`, `ExtentLast` and the strip's
size; it runs on a resize of the strip and on a change of `HasExtent`, `ExtentFirst`, `ExtentLast` or
`Band`. `PlaceMarker` runs on a resize and on a change of the window fractions or `HasExtent`, so a pan
or a drag moves the marker and leaves the figures as they are.

**The band's colour** follows the drawn pen's stored colour live. `BandColor` is a `WhenAnyValue`
over `DrawnPenId` and `Pens`, switched to that pen state's `Pen.Color` and ending in `ToProperty`, so a
recolour in the pen editor reaches the strip with the chart and needs no read. It is the stored hex
string, null with no drawn pen, and the view converts it with the legend's
`LegendConverters.HexToBrush`.

**Failures.** A failed band read reaches the message panel through
`ResultReporting.TryReportFailure`, which maps it through `ArchiveFailureMapper.Map` like every
other read. `Minimap/OutageReport` holds the outage policy, one instance for the band reads in
`MinimapBandFeed` and one for the extent reads in `MinimapViewModel`: it reports the first failed read
of an outage and logs every further one at Warning, with the exception when the read threw, and the
first successful read ends the outage and arms the report again. The report starts armed. The panel coalesces a repeat
of its newest entry only, and during an outage other entries land between two band reads, the status
bar's `ConnectionLost` and the chart's failed history reads among them, so a band entry per read would
not coalesce; one entry per outage keeps the panel readable. The catalogue sync reports no lost
connection itself: it logs each one, and three in a row turn the status bar to the fault with one entry
(`overview.md#a-failed-read`). Each read turns a throw into a failed `Result`, so a
throw never ends the pipeline and the next read is still scheduled; a throw while a read is applied,
the next read's scheduling included, goes to `TryReportFailure` as well.

## Out of scope / later

- **Deferred (NICE, not in MVP):** horizontal cursor / crosshair (trend-feature-spec.md §CU-5).
- **Out of scope:** alarm/event overlays from the `messages` log.
- **`[LATER]`:** save pen sets + scales + layout as named views/templates; snapshot / print
  (trend-feature-spec.md §MS-6); touch gestures such as pinch and multi-finger pan (not relevant for
  target operator PCs). A second finger already never breaks a drag (`#navigation--cursors`).

## Renderer & UI framework

- **ScottPlot 5** (as-built): MIT; SkiaSharp; each pen gets its own `IYAxis` and every one of them is
  a left axis (`Axes.Left`, then `AddLeftAxis`), non-active axes `IsVisible = false`.
  Trends drawn as one per-pen `EnvelopeLine` — our own `IPlottable` — over a data-layer-decimated
  envelope; a NaN column segments the line at gaps; `DataLogger` is prior art, not the pattern. Shared-X
  invariant: all pens pinned to `plot.Axes.Bottom`.
- **Avalonia 12.0.5 / net10** (as-built): hosts `ScottPlot.Avalonia` 5.1.59, which depends on Avalonia
  12.0.0. Mirrors SemiStep's patterns (ReactiveUI + MS.DI + Serilog + Semi.Avalonia) and its
  versions. `Avalonia.HarfBuzz` 12.0.5 is referenced and `UseHarfBuzz()` is called explicitly; Skia carries
  no text shaper.
- No qualifying open-source .NET SCADA trend-viewer reference repo exists; built from library
  primitives, with ScottPlot's `DataLogger` demo as the nearest realtime pattern.

### Reuse from SemiStep (sibling repository)

Same author, same conventions — reuse directly:

- DI + two-phase startup (validate config → build provider → `App.RunStarted`); `IServiceCollection`
  extension methods.
- Coordinator-as-event-hub: expose realtime as `IObservable<T>`, `ObserveOn(uiScheduler)
  .Publish().RefCount()` at the source, `uiScheduler` being the `AvaloniaScheduler.Instance` the
  composition root passes in; subscribers `DisposeWith(_disposables)`. This is the
  realtime sample → chart pipeline.
- Serilog config; `MainWindow` `Grid` layout (menu / content / message panel / status bar) with
  the chart in the content row; ReactiveUI VM base; code style / naming.
- Gaps vs SemiPlot needs: no charting (new), no window-state persistence (matches `[LATER]`),
  no IJ theme (north-star only).

## Architecture note

All of the above is **renderer-agnostic behavior**. The `IDataProvider` + DTO (Pen/Sample/Series)
+ coordinator layer is retained; only the presentation under the bridge changes (WebView2/Web/JS
removed, native ScottPlot control added). "No lag" is delivered by the data layer returning
decimated data per zoom level, not by the chart library.

## Sources

- Ignition Easy Chart axes / Power Chart — https://www.docs.inductiveautomation.com/docs/8.1/ignition-modules/vision/historian-in-vision/using-the-vision-easy-chart/easy-chart-axes ; https://www.docs.inductiveautomation.com/docs/8.1/appendix/components/perspective-components/perspective-chart-palette/perspective-power-chart
- M4 decimation — https://dl.acm.org/doi/10.14778/2732951.2732953 ; MinMaxLTTB — https://arxiv.org/pdf/2305.00332
- AVEVA Trend Client (Full vs Cyclic) — https://cdn.logic-control.com/docs/aveva/hmi-scada/application-server/aaTrendClient.pdf
- MasterSCADA archive layers — https://www.owenkomplekt.ru/assets/files/SCADA/MasterSCADA_3.%D0%A5/arkhivy-v-masterscada.pdf
- Simple-Scada trends — https://simple-scada.com/help/manual/trendviewweb.html ; archive v2 — https://simple-scada.com/help/manual/archsysv2.html
- ScottPlot — https://github.com/ScottPlot/ScottPlot ; ScottPlot.Avalonia — https://www.nuget.org/packages/ScottPlot.Avalonia ; DataLogger demo — https://github.com/ScottPlot/ScottPlot/blob/main/src/ScottPlot5/ScottPlot5%20Demos/ScottPlot5%20WinForms%20Demo/Demos/DataLogger.cs
- OxyPlot perf issues — https://github.com/oxyplot/oxyplot/issues/1865 ; https://github.com/oxyplot/oxyplot/issues/1602 ; https://github.com/oxyplot/oxyplot/issues/1748
