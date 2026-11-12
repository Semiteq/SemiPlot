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
  and `QueryCatalogueAsync` cannot be awaited there; the composition root hands over the start sequence's
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
  `log_scale_on_start` stores, the panel's Logarithmic checkbox switches it for the session of the
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
- The Logarithmic checkbox sits in its own row under the bounds, outside `AxisScaleBounds`, so a
  press on it ends the pair's edit; the row's label column is shared with the bounds grid
  (`ui-theme.md#the-axis-scale-panel`). It reads the pen's session flag, `IsLogarithmic`, `Mode=OneWay`,
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
The SCADA adds recipe markers of its own, locked, at the start, the end and the interruption of a recipe. The
navigation bar and the `Go` menu step between recipe runs and between operator markers, and the marker list
centres any operator marker (`#recipe-run-and-marker-steps`, `trend-feature-spec.md` §CU-4). The minimap's day
calendar ticks every day of the archive that holds an operator marker, its Day page shades every recipe run,
and a press on a day's cell brings that day into the window (`#archive-overview-minimap`).

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
  marker leaves `ShownMarkers`, so it loses its line, its reading and its hit, the minimap's Day page drops
  its run's shading, and it stays in `MarkerSet.All`, so a revert or a `MarkerGone` of a write that began
  before the hiding still lands.

### Recipe-run and marker steps

`Navigation/NavigationBarViewModel` steps the window between recipe runs and between operator markers. Each
step writes the window through `ChartNavigationController.SetWindow` and nothing else, so a step drops sticky
(`TrendNavigationModel.SetWindow`), and the chart, the minimap and the bar follow as for any window change.

- **Targets.** The steps read `ChartMarkers.All`, hidden markers included. The runs are
  `MarkerNavigation.Runs(All, NewestSample)`, paired as the minimap pairs them (`#archive-overview-minimap`),
  so a run still steps while `View` -> `Recipe markers` hides its markers and the Day page drops its
  shading. The marker steps take `ChartMarkers.OperatorMarkers`, the `MarkerKind.User` markers shown or
  hidden, which `Core/Trends/MarkerSet` keeps beside `All`, so no recipe marker reaches them. The runs are paired again on `ChartMarkers.Changed` and `NewestSampleMoved`; a window change only
  runs `MarkerNavigation`'s binary searches.
- **The reference.** A step measures from the window centre, or from `FirstSample` when the centre lies
  before it. `PreviousRunCommand` frames the last run that ends before the reference and `NextRunCommand` the
  first run that starts after it, so a run that holds the reference is neither. `PreviousMarkerCommand` and
  `NextMarkerCommand` centre the last operator marker before the reference and the first after it, keeping
  the width. `LatestRunCommand` frames the newest run from any window.
- **The frame.** `MarkerNavigation.Frame` is the run plus a tenth of its length on each side.
- **The first-sample floor.** A run that ends at or before `FirstSample` is no target, and a run that starts
  before it is framed from it. An operator marker before `FirstSample` is no step target, and neither is one
  whose centred window the floor clamps back onto the window shown, so an enabled marker step always moves
  the window.
- **Enabled state.** Each step command's `canExecute` follows its target, recomputed on a marker change, on a
  newest sample and on every window change, and each command body recomputes the target from the live state.
  `LatestRunCommand` stays enabled while the newest run is framed already: an open last run grows with the
  newest sample, and a second press frames the grown run. `HasRecipeRuns` is true while the newest run is a
  target.

The navigation bar's run group stands between `Sticky` and `Delta`, a `Border.groupSeparator` on each side:
the `RecipeRunsButton` `DropDownButton` captioned "Recipe run", which opens the run list
(`#the-marker-and-run-lists`), the chevron steps `PreviousRunButton` and `NextRunButton` (32 x 32), the text
button `LatestRunButton` and the `MarkersButton` `DropDownButton`. A run control's tooltip names its step and
gesture while enabled. A disabled step shows "No earlier recipe run" or "No later recipe run" while another
run exists, and every run step shows "No recipe runs" while `HasRecipeRuns` is false, through the
`noRuns` class. `RecipeRunsButton` is disabled while the run list is empty (`HasRecipeRunEntries`) and then
shows "No recipe runs", and `MarkersButton` is disabled while the marker list is empty and then shows "No
operator markers". The Russian bar measures 1239 px in Inter
(`ui-text.md#the-navigation-bars-text`). The
bar carries no marker steps; they are `Go` items (`#the-go-menu-and-the-gestures`). The texts are under
`ui-text.md#the-navigation-bars-text`.

### The marker and run lists

One list control serves two lists under one policy: `MarkersButton.Flyout`, the operator marker list, and
`RecipeRunsButton.Flyout`, the recipe run list. Each is a plain `Flyout`, `BottomEdgeAlignedLeft`, whose
content is a `Navigation/NavigationListView` over a `Navigation/NavigationListViewModel` the bar owns:
`NavigationBarViewModel.MarkerList` over `OperatorMarkers`, the operator's markers newest first, shown or
hidden, those before `FirstSample` included, and `RecipeRunList` over `RecipeRunEntries`, every run of
`MarkerNavigation.Runs(ChartMarkers.All, NewestSample)` newest first, the source the run steps read. Recipe
markers never reach the marker list, and neither list carries an edit or delete action. Both lists cover the
whole archive with no count cap.

- **The entries.** `Navigation/NavigationListEntry` is one entry: its key (a marker's id, a run's start in
  ticks), its moment, its name, and for a run its duration in whole minutes and whether it ended in an
  interruption. `NavigationListEntry.Of(StoredMarker)` takes the marker's label; `Of(RecipeRun)` takes the
  run's `Label`, the `RecipeStart` marker's label (`MarkerNavigation.Runs`). A blank label is no name. The
  bar republishes `RecipeRunEntries` only when the entries differ, so an open run's growing end changes the
  list once a minute, when its rounded duration does.
- **The content.** 400 px wide: `ListFilter`, a 32 px `TextBox` with the list's watermark, then 8 px below
  `ListFrame`, a 1 px `AppBorderBrush` frame with 4 px padding around `ListScroller`, a fixed 280 px viewport
  of ten entry rows (`NavigationListView.ViewportHeight`), and its `ItemsControl` `ListItems` over a
  `VirtualizingStackPanel`. The rows are `NavigationListViewModel.Rows`, two typed templates over one list,
  their heights the view's `HeaderHeight` and `RowHeight` constants: a 24 px `MonthHeader` (the month in
  capitals and, at the right, the count of its entries the filter keeps, both 11 px in
  `AppSecondaryForegroundBrush`, the sidebar group header's style) and a 28 px `EntryRow`. A row holds
  `EntryName` at the left, trimmed with an ellipsis and its full text as the tooltip, then at the right, 8 px
  apart, the 16 px `EntryFlag` slot, the 108 px `EntryDate` column (`d MMM HH:mm`) and the 88 px
  right-aligned `EntryDuration` column, the two text columns in `AppSecondaryForegroundBrush`. A marker row
  shows no flag slot and no duration. A run row's flag slot holds `InterruptedGlyph`, the 12 px
  `WarningGeometry` in `AppRecipeMarkerLineBrush` with the tooltip "Interrupted", when the run ended in an
  interruption. The fixed columns line up the virtualised rows; the date and duration widths hold the widest
  Russian forms in Inter, 103.6 px and 82.8 px with every digit as the widest one. An entry with no name shows
  its date alone, and the year stands only in the header.
- **The rows.** `Navigation/NavigationListRows` builds them, pure: each entry in the machine's zone
  (`TimeZoneInfo.Local`), its date as `d MMM HH:mm` and its month header as `MMMM yyyy` in capitals, both in
  `CultureInfo.CurrentCulture`, and a run's duration as "35 min" under an hour and "1 h 05 min" from it
  (`ui-text.md#the-navigation-bars-text`). The filter splits its text on whitespace and keeps an entry for
  which every token is found in its name, its shown date or its month header, ignoring case through the
  culture's `CompareInfo`, so "нояб etch" keeps a November entry whose name holds "etch", and "2026" every
  entry of that year. A list with no row shows `NoMatchLine`, "No markers match the filter" or "No recipe
  runs match the filter", in the list area: a filter that keeps nothing, or the last entry gone through a
  read while the list is open.
- **The active row.** `NavigationListViewModel` marks one `NavigationListItem` active, the one writer of its
  `IsActive`. `Open` clears the filter and activates the entry nearest the window centre
  (`NavigationListRows.Nearest`): a marker by its moment, a run by its span, at no distance while the centre
  lies inside it; the newer on a tie. A rebuilt list keeps its active entry, by key, while the filter keeps
  it, else activates the nearest kept one. The active row wears the sidebar's active-row look: an
  `AppAccentFillBrush` fill and a 3 px `AppAccentBrush` bar at its left.
- **The view.** On the flyout's `Opening`, `NavigationListView.Prepare` runs `Open` ahead of the first
  layout pass. On `Opened`, `FocusFilter` focuses the filter and scrolls to the active row after the layout
  pass. Every rebuild of `Rows`, a filter typed or a read while the list is open, scrolls to the active row the
  same way, at `Background` priority, so the view model has moved it and the list has its layout. The fixed
  viewport never resizes the open list. Focus stays in the filter: a tunnelling `KeyDown` handler takes Down
  and Up, which move the active row over the entry rows and hold at the ends, Enter, which goes to the active
  entry and closes, and Esc, which closes. Every other key reaches the box, so Ctrl+Left moves its caret and
  steps no run (`#the-go-menu-and-the-gestures`). A click on an entry row goes to its entry and closes. A hover
  moves nothing. The bar hands the marker list a go-to that centres the marker and keeps the width, and the
  run list its private `GoToRun`, which frames the run with the start it names as it stands now
  (`MarkerNavigation.Frame`); a target before `FirstSample` lands on the window the floor places.

`NavigationBarView` sets each content's `DataContext` to the bar's list itself, so a list keeps its model at
any anchor. `MainWindowViewModel.ShowMarkerListCommand`, enabled while `NavigationBarViewModel.HasOperatorMarkers`
holds, the one flag that also enables `MarkersButton`, raises `MarkerListRequests`, and `MainWindow` answers with
`NavigationBarView.ShowMarkerList(AppMenuBar.GoMenuHeader)`. `ShowRecipeRunListCommand`, enabled while
`HasRecipeRunEntries` holds, raises `RecipeRunListRequests`, answered by `ShowRecipeRunList`. A list opens under
its button while that button is effectively visible, and under the `Go` menu's header while the navigation bar
is hidden. `NavigationListRowsTests` and `NavigationListViewModelTests` pin the rows of both kinds, the token
filter, the month grouping, the durations, the interrupted flag and the active row; `NavigationListViewTests`
drives both realised lists through `HeadlessInput`, keeps the active row in view when a filter moves it fifteen
rows up, shows `NoMatchLine` once the last marker goes or no run holds every token, and measures the
watermarks, the widest month header and the date and duration columns in Inter; `WindowGestureTests` opens
each list from either anchor.

### The Go menu and the gestures

`AppMenuBar`'s `GoMenu` stands between `View` and `Help` and carries every step, so each one works with the
navigation bar and the minimap hidden. Its items run in three blocks split by separators: the page steps, the
run steps with the run list, the marker steps with the marker list. Every item carries a command and the `InputGesture` of
`Navigation/WindowGestures`, the one gesture table:

| Item | Command | Gesture |
| --- | --- | --- |
| Previous day, week or month, Next day, week or month | `MinimapViewModel.Pager.PreviousPageCommand`, `NextPageCommand` | Alt+Left, Alt+Right |
| Previous recipe run, Next recipe run | `NavigationBarViewModel.PreviousRunCommand`, `NextRunCommand` | Ctrl+Left, Ctrl+Right |
| Newest recipe run | `NavigationBarViewModel.LatestRunCommand` | Ctrl+End |
| Recipe runs... | `MainWindowViewModel.ShowRecipeRunListCommand` | Ctrl+R |
| Previous operator marker, Next operator marker | `NavigationBarViewModel.PreviousMarkerCommand`, `NextMarkerCommand` | Ctrl+Shift+Left, Ctrl+Shift+Right |
| Operator markers... | `MainWindowViewModel.ShowMarkerListCommand` | Ctrl+M |

- **The gesture rule.** `MainWindow.OnKeyDown` runs the `Go` menu: `AppMenuBar.GoItemFor` finds the item
  whose `InputGesture` matches the key, and the window runs the item's command and marks the key handled
  when the command can execute, so the pairing of key and command is written once, on the item. A key a control already handled, and a key whose source is a `TextBox`, run nothing, and a
  disabled command leaves the key unhandled. So a gesture works with the keyboard on the chart, on a bar
  button or on a sidebar check box, and Ctrl+Left in the marker label editor or the time window panel moves
  the caret and steps no run, and so does Ctrl+Left in the marker list's filter box (`#the-marker-and-run-lists`). No
  window `KeyBinding` carries a gesture: a `KeyBinding` walks the visual
  parents ahead of `KeyDown` (`#the-time-window-panel`), so it would take the key from the focused `TextBox`.
  The `TextBox` source rule matters for Alt+Left, Alt+Right, Ctrl+M and Ctrl+R, the keys a `TextBox` leaves
  unhandled. Ctrl+R is free elsewhere: no control of the tree and no other gesture of `WindowGestures` reads it.
- **The gesture text.** The open menu prints a gesture with Avalonia's key names, "Alt+Left Arrow" for
  Alt+Left, and every tooltip that names a gesture prints the same text through `WindowGestures`' private `TextOf`
  (`ui-text.md#the-gesture-text`).
- **The tests.** `AppMenuBarTests` pins the item order, the commands and the gestures. `WindowGestureTests`
  presses each gesture with the bar and the minimap hidden, opens the marker list from the menu and from
  Ctrl+M and the run list from the menu and from Ctrl+R with the bar shown and hidden, and types the gestures
  into the marker label editor, the time window panel and the marker list's filter box. The headless platform hosts a flyout inside the window and sends an Alt-modified arrow with no
  Alt key-down, so no headless test covers the label editor as a Win32 popup or the menu bar's Alt handling.

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

Required by trend-feature-spec.md §PN-8. As-built: a grouped sidebar whose row carries the on/off box, a
round colour dot, the name, the current value in the pen's own mask and the unit (charting.md). A pen in
several groups is listed under each of them and switching it off under one header switches it off under
all. Every drawn header carries a switch whose state is derived from its pens: on when all are on, off
when all are off, indeterminate when mixed. A click on a mixed or off header switches every pen of the
group on, on an all-on header off, and every other header a shared pen sits under re-derives. A header
reads as a section caption, small grey capitals under a thin line, with its rows indented beneath it; the
active pen's row is marked by an accent background and an accent bar on its left edge, not by bold text.
A toggle at a header's right edge folds the group's rows away for the session, and a pick of a pen folded
away unfolds it (`overview.md#the-sidebar-fold`). The panel's top row holds a values button, which hides
the value and unit columns and narrows the panel, and a fold-all button (`overview.md#the-sidebar-fold`);
with the values hidden, a row leaves the box, the dot and the name. `View` -> Show all pens and Hide all
pens switch every pen at once (`charting.md#switching-every-pen-at-once`). A handle on the panel's left
edge drags its width with the values shown or hidden; each state remembers its own width for the session,
and a restart returns both to their defaults. The value at the cursor is the chart's own hover readout
(§CU-2), not a legend cell. The row shows no scale range: the padded axis bound would read as the pen's
measured extremes.

## Archive-overview minimap

Required by trend-feature-spec.md §TM-4. As-built: a 97 px row beneath the chart holds the **detail track**
between the two page steps and its tick row, and under them the **day calendar** and its label row. The day
calendar shows the whole archive extent as one cell per archive wall-clock day: which days
hold rows and how much of the working hours each covers, the days that hold an operator marker, and an
outline around the detail page. The detail track spans one page, a working day, a Monday-to-Monday week or a
calendar month, the smallest that holds the chart window, at the strip's full width: the drawn pen's min/max
band, the chart window's box, the recipe runs of a Day page and the operator's markers. The day calendar
answers which days hold data, the detail track what one day, week or month looked like, and the chart is the
third level.

- The extent comes from `IDataProvider.QueryArchiveExtentAsync()`, which returns an
  `ArchiveExtent(FirstUtc, LastUtc)` (data-integration.md); `PostgresDataProvider` answers it with the
  true bounds of the configured variables, and an archive with no rows answers `ArchiveExtent.Empty`.
  The days that hold rows come from `IDataProvider.QueryArchiveDaySpansAsync`, one
  `ArchiveDaySpan(Day, FirstUtc, LastUtc)` per archive wall-clock day with a raw row
  (`data-integration.md#the-provider-surface`).
- `Minimap/MinimapViewModel` and its parts reach the archive through `TrendCoordinator.QueryArchiveExtentAsync()`,
  `QueryArchiveDaySpansAsync` and `QueryHistoryAsync` on the UI scheduler; it never holds the
  `IDataProvider` directly. It takes the chart (`TrendChartViewModel`) and reads its `Navigation`,
  `DrawnPenId`, `Pens`, `FindPen` and `Markers`, and it takes `archiveZone` from `TrendWindow.Build`, the
  zone every wall-clock day and label of the minimap is read in. It builds, exposes and disposes three
  parts: `PresenceFeed` (`Minimap/ArchivePresenceFeed`, the day spans), `Pager` (`Minimap/MinimapPager`,
  the page and its steps) and `BandFeed` (`Minimap/MinimapBandFeed`, the band). The geometry is pure and
  lives in Core: `Core/Trends/MinimapPage` (pages, the page that holds a window, working hours, steps,
  landing), `DayCalendar` (the calendar's pitch, cells, coverage, page outline, marker ticks and counts,
  month separators and hit test),
  `DayCalendarLabels` (the calendar's label row), `MinimapTrackGeometry` (the window box, the run shades,
  the page ticks), `MinimapGeometry` (fractions, the right bound, the band's figures) and
  `MarkerNavigation` (the recipe runs).
- The minimap reads the extent at start (`TrendWindow.StartExtentLoad`), before a catalogue read gives
  an empty chart its first pens, and after a read adds a pen to a chart that already has some
  (`overview.md#what-a-read-changes`). `LoadExtentAsync` returns the read once the minimap has applied
  it. Both callers widen the navigation's first sample to an earlier one in that read
  (`TrendChartViewModel.WidenToArchiveExtent`), so the chart pans back as far as the day calendar reaches
  and reads the rows a moved floor brings into the window in view; the empty-chart caller seeds the
  navigation from the same read first. A read that leaves the minimap without an extent, failed or
  empty, is read once more on the next `NewestSampleMoved`: a sample exists, so the archive has rows. One
  such read runs at a time, only a read that again leaves no extent arms the next one, and the next one
  waits for a sample at least 60 s after the previous retry started, so an extent read that keeps
  failing while the live edge works costs one read a minute. The extent reads share the band's outage
  policy (see Failures).

**The layout.** `Minimap/MinimapView.axaml` is a `Border` with a 1 px `AppBorderBrush` top line and
`8,4` padding around `TrackGrid`, rows `36,14,8,16,14`, columns `24,*,24` and a `ColumnSpacing` of 8;
1 + 4 + 36 + 14 + 8 + 16 + 14 + 4 makes the 97 px. The first row, directly under the chart, holds
`PreviousPageButton`, `StripCanvas` (the detail track, with `HintLabel` centred over it) and
`NextPageButton`, each step 8 px from the strip; the second holds `PageTickRow` (tick labels and the hover
time) in the strip's column, so the tick labels, the hover time and the window box share the strip's x. The
third row is the 8 px gap between the tick row and the calendar. The last two rows hold
`Minimap/DayCalendarView` in the strip's column only, so the calendar and the strip share their left and
right edges, and the steps stand beside the strip alone. It is a control of its own with rows `16,14` and
one column: `CalendarCellRow` (the cells' pointer area) in the 16 px row, `CalendarLabels` (the day and month
labels) under it, and `CalendarCells` (the drawing) over both rows: marker ticks at y 0 to 2, cells at y 4
to 14 and the page outline at y 2 to 16 of the cell row. No page indicator names the page: the outline
around its cells marks it on the calendar, and only the hint over a page without rows names it.
`TrackGrid` sets every label row's text (`Canvas > TextBlock`) and `HintLabel` to 10 px in
`AppSecondaryForegroundBrush`. `MainWindow.axaml` sets no height on the minimap row, so the row takes the
view's own height. `MinimapView.axaml.cs` lays out the band, `MinimapView.DetailTrack.cs` the detail track
and its pointer, and `DayCalendarView.axaml.cs` the calendar, its labels, its hover and its pointer.
`Minimap/TrackLabels` measures the labels of both label rows and places and hides those of the tick row.

**The bounds.** The left bound (`ExtentFirst`) is the extent's first sample. The archive is never
pruned, so it moves only when a reloaded extent starts earlier, and no periodic extent read exists.
The right bound (`ExtentLast`) is `MinimapGeometry.RightBound`: the later of the extent's last sample
and `ChartNavigationController.NewestSample`, the newest sample the chart has seen. `TrackDataExtents`
writes it once, from the archive seed or the chart's first history, and afterwards only the live edge
(`OnLiveEdge`) moves it. `NewestSample` is null until one of the two writes it, so the controller's
wall-clock start never becomes a bound, and it only moves forward. `NewestSampleMoved` fires on every
forward move, sticky or not; when the window also moves, it fires after the move and ahead of
`WindowChanged`. On it the minimap pairs the recipe runs again, moves the right bound and hands the
sample to the presence feed while it has an extent, and then hands it to the pager
(`MinimapPager.FollowNewestSample`), in that order, so the page never judges a sample the day spans do not
hold yet (see The page follows the zoom). An extent applied after a newer sample takes that sample as its
right bound.

**The day spans.** `ArchivePresenceFeed.Spans` holds the days of the extent that hold rows, ascending,
each with its first and last raw sample. `MinimapViewModel.ApplyExtent` calls `ReadExtent` on every applied non-empty
extent, which
reads every day from the extent's first wall-clock day to its last, the days computed through
`ArchiveClock.DayOf` in `archiveZone`. `MinimapViewModel` calls `FollowNewestSample` on every
`NewestSampleMoved` while it has an extent:

- The newest span's `LastUtc` moves to `Navigation.NewestSample` when that sample lies on the same
  wall-clock day, with no read.
- A sample on a day past the last day of the last successful read appends that day at once, its first and
  last sample both the newest sample, so the day calendar and the hint show today before its read lands
  and while a failed read waits for its retry. The landed read replaces it.
- A sample on a new day reads from the last day of the last successful read to the newest day, and the
  answer replaces the held spans from its first day on. A midnight rollover therefore reads two days and
  refreshes the finished day's true last sample, and a jump over a weekend or an outage reads the days in
  between. A read of a whole extent also drops every held span before the extent's first day, so days
  whose partitions were dropped leave the list.
- One read runs at a time: a sample while a read is in flight requests nothing, so the live poll does
  not cancel and restart the read on every tick of a new day. A newer extent cancels the read in flight
  through its `CancellationToken`.
- A failed read keeps the spans the feed held. The next sample reads again, at most one retry a minute:
  a retry waits for a sample at least 60 s after the previous retry started. A retry reads from the last
  complete day, or from the extent's first day when no read has landed. The feed sets no timer: it reads
  `uiScheduler.Now` only, so on an archive that no longer grows a failed read is retried only by the next
  applied extent, as the extent read is.
- `IsExtentReadPending` is true from `ReadExtent` until a read lands or fails.

Each read logs its day range and duration at Debug. Every live sample on the newest day republishes
`Spans` with a new `LastUtc`, so a consumer that depends on the days alone compares the day lists and
rebuilds only when they change. Over six months a full read executes in 167 to 176 ms and the newest
day's read in 4.5 to 4.9 ms (`data-integration.md#read-cost`).

**The pages.** A `MinimapPage` carries its `Unit` (`MinimapPageUnit.Day`, `Week` or `Month`), `FirstDay`, the
common `Hours`, and `FromUtc` and `ToUtc`; `EndDay` is the day after its last. Every page function works
in the archive's wall clock and converts to UTC only at the edges; a wall-clock time the spring change
skips takes the zone's base offset through `Core/Data/ArchiveClock.ToUtc`, which `ArchiveTimeConverter.ToUtc`
calls too.

- The common working hours are `MinimapPage.WorkingHours(spans, zone)`: the 5th percentile of each day's
  first sample to the 95th percentile of each day's last sample, as archive wall-clock times of day,
  floored and ceiled to whole hours. The percentile is the nearest rank, so below 20 days it is the
  earliest first and the latest last sample. The start stays at or before 23:00 and the page is at least
  one hour wide. No spans give 00:00-24:00 (`DayHours.FullDay`), and so does an archive that runs around
  the clock.
- A Day page is its day's common hours. A day whose own span starts earlier or ends later widens its
  page in whole hours, so no sample of the day is hidden and the live day rescales at most once an hour.
  Common hours keep one clock time at one x on every day's page, so runs line up from day to day.
- A Week page runs from Monday 00:00 to the next Monday 00:00 and ignores the hours.
- A Month page runs from the 1st 00:00 to the next month's 1st 00:00 and ignores the hours.

**The page follows the zoom.** No control picks the unit, and `Page` has no writer but its derivation. `Page`
is `MinimapPage.Holding(Navigation.From, Navigation.To, NewestSample, WorkingHours(Spans), Spans,
archiveZone)`, the smallest page that holds the chart window against the page's own extent
(`MinimapPage.Holds`): the Day page of the window's start, its working hours widened by its own span, while
the window lies inside it; else the Monday-to-Sunday Week page that holds it; else the calendar Month page
that holds it. A window wider than a month, or one that crosses a month's end, keeps the Month page of its
start, and the box is clipped there. A Day page is its working hours, so a 12 h window on 07:00-17:00 hours,
an hour before 07:00 or a window across midnight shows its week. The shown unit is `Page.Unit`, with no
property of its own.

The hold is judged against the window clipped to the newest sample: the end `Holding` tests is
`min(Navigation.To, NewestSample)` while a newest sample exists, and the window's start is unchanged. A sticky
window's right end is the latest poll time, which passes the live day's widened end at an hour boundary and
in the evening after the shift; no sample lies past that end, so the Day page keeps holding the window and no
band read runs. A window that lies wholly past the newest sample, such as a sticky evening window after the
last sample at 17:00, also keeps the Day page of its start, and its box is hidden there (see The detail
track). A historical window ends before the newest sample, so the clip changes nothing for it. With no newest
sample the whole window is judged.

`Window` mirrors `[From, To]` for the detail track's box. `Page` is a `CombineLatest` over `WindowChanged`,
the spans and `MinimapPager.NewestSample`, ending in `ToProperty`, so one window event yields one `Page` and
the working hours are computed once per spans republish, never per window move. `NewestSample` is the
navigation's newest sample as of the last `FollowNewestSample`, which `MinimapViewModel` calls on every
`NewestSampleMoved` after the presence feed has extended the spans to that sample: the live day is widened
before the page reads the sample past its end, so the page never shows the week in between. `MinimapPage` is a
record struct and `ToProperty` raises only on a change, so a sample or a spans republish that leaves the page
equal raises nothing and reads no band.
`MinimapPagerTests.AStickyWindowPastTheLiveDaysEnd_KeepsTheDayPage_AndReadsTheBandOnlyWhenTheDayWidens` drives
a poll past the day's end, then a sample past it, and fails on a Week page in between or a band read while the
page stays; the `Holding_*` cases of `MinimapPageTests` pin the clip.

**Steps and landing.** `PreviousPage` and `NextPage` are the step targets, and `PreviousPageCommand` and
`NextPageCommand` turn to them, each enabled while its target is not null.

- A target is `MinimapPage.Step`: the nearest page in the direction whose days hold a span, at the
  shown unit and hours, so a step skips a weekend, a maintenance week or a month without rows. Past the
  first or the last span it is null.
- A step lands through `MinimapPage.Land(From, To, page, Spans, zone)`. The window keeps its width and
  its clock offset: from its own day's midnight on a Day page, from its own Monday on a Week page, from its
  own month's 1st on a Month page. When no day span of the target page overlaps the window at that offset,
  the window moves to the first sample of the page's first day with rows, width kept. A page with no span
  keeps the offset. The page then follows the landed window, so a landing that moves the window onto its
  page's first sample can show a smaller page than the one stepped from.
- The landing calls `Navigation.SetWindow(from, to)` and nothing else, so Sticky drops there
  (`TrendNavigationModel`) with no second writer. A step depends on the spans only, never on a band read.
- With no spans held and the extent's read still pending (`IsExtentReadPending`), both steps are disabled
  and a press on the day calendar moves nothing, because a landing without spans could pick a day with no
  rows.
- With no spans held after that read answered, every page is a whole calendar day, week or month, so a step goes
  to the calendar neighbour while that neighbour meets `[ExtentFirst, ExtentLast]`. Without an extent both
  steps are disabled.

**The day calendar.** `MinimapViewModel.Calendar(width)` builds `DayCalendar.Over(first day, last day,
width)` over the archive wall-clock days of `ExtentFirst` and `ExtentLast` (`ArchiveClock.DayOf` in
`archiveZone`), at the width of `CalendarCellRow`; it is null without an extent or when the width leaves no
room right of the inset. The last day follows the right bound, so on a live archive it is today.

- The cells are anchored at the row's right end: the last day's cell ends at `DayCalendar.Right`, the row's
  width, which is the strip's right edge, and the earlier days fill leftwards. A short archive leaves the
  row empty left of its cells: two days are two 19 px cells at the right end, under the newest part of the
  strip. The row starts at the strip's left edge, so the calendar and the strip share both edges.
- The pitch is `min(20 px, floor(available / days))`, `available` being the row less a 2 px inset at its left
  end (`DayCalendar.OutlineStroke`, the room the page outline's stroke takes left of the first cell). At the
  right end the outline of the last day draws its stroke 2 px past the row, into the 8 px gap before the step
  column, so neither `CalendarCells` nor `DayCalendarView` clips (`ClipToBounds="False"` on the view, which
  clips by default). A pitch of 4 px or more leaves a 1 px gap before each cell. An
  archive with more days than pixels takes the fractional pitch `available / days` with no gap. Each day
  then falls in the whole pixel column its offset from the right end floors to, the last ending at the row's
  right end, and `Cells` draws one 1 px cell per column at the highest grade of its days, so two short days
  never composite into a darker cell.
- `Cells(spans, hours, zone)` gives one `CalendarCell` per day span on the calendar, or per pixel column below
  a 1 px pitch, graded by `CoverageOf`:
  the share of the common working hours (`Page.Hours`, the `MinimapPage.WorkingHours` of the held spans)
  that the span's first-to-last sample covers, in the archive's wall clock. Under 25 % is `Low`, under 75 %
  `Medium`, the rest `Full`, so a short day and a full one read apart. A day with rows is never empty, even
  one whose rows fall wholly outside the working hours. On an archive that runs around the clock the hours
  are 00:00-24:00, so the share is of the whole day. Today is graded by its span so far, which the presence
  feed extends to the newest sample. A day without rows has no cell and draws nothing.
- `PageOutline(page)` rings the page's cells, a day, a Monday-to-Sunday week or a month, with the 2 px stroke
  outside them. A page running past the last day stops at that day's cell, and a page off the calendar has
  none.
- `MarkerTicks(markerTimes, zone)` gives one cell-wide tick per day that holds at least one of
  `MinimapViewModel.OperatorMarkerTimes`, the times of `ChartMarkers.OperatorMarkers`; recipe markers never
  reach the calendar.
- `MarkerCountsByDay(markerTimes, zone)` counts the same markers per archive wall-clock day, for a cell's
  tooltip.
- `MonthSeparators()` gives the x of a 1 px line in the gap before each month's first cell on an archive of
  more than 42 days, whose label row names months only, and none while it names Mondays
  (`ShowsMondayLabels`).
- `DayAt(x)` is the day whose pitch holds x, its leading gap included; x before the first cell's pitch or
  past the row's right end is no day.

`Minimap/MinimapDayCalendar`, a `Control` with a `Render` override that reads nothing, draws over the cell
row and the label row, bottom to top: the month separators through both rows in `AppSubtleLineBrush`; each
cell, 10 px tall at y 4 to 14 of the 16 px row, in `AppMinimapPresenceBrush` at opacity 0.35, 0.65 or 1 by
its grade; the page outline, y 2 to 16, a 2 px `AppAccentBrush` stroke with no fill; the hovered cell, a 1 px
`AppSecondaryForegroundBrush` ring inside it; and the marker ticks, 2 px tall at y 0 to 2 in
`AppMarkerLineBrush`. A tick stands above the outline, which starts at y 2, so neither covers the other, and
off the cell, because the marker brush on the presence fill reads at almost no contrast in the light variant.
The control is not hit-testable.

The view recomputes the cells on every spans republish and assigns them only when their sequence changes, so
a live sample redraws the calendar only when today crosses a coverage step. The calendar itself changes with
the extent's days and the row's width, and each change lays out the separators, the labels and the hover
again. The outline follows `Pager.Page`, and
`OperatorMarkerTimes` is reassigned only when its sequence changes. With no spans held the calendar fills no
cell, and the outline, the marker ticks and the labels still draw, because they come from the page, the
markers and the extent. A day span holds only its first and last sample, so a stop in the middle of a day
does not lower its grade; the detail track's band shows it as a break.

`CalendarLabels` sits under the cells and always shows. The archive's length picks the label kinds, never the
row's width: `DayCalendarLabels.Candidates` names the month at the first cell and at each month's first day,
and on an archive of 42 days or less (`DayCalendar.ShowsMondayLabels`) each Monday as well. A Monday label
stands at its cell's left edge and a month label 2 px past it, clear of the separator. The labels are `ddd d`
and `MMM` (`MinimapLabels.Calendar`), so a Russian Monday reads `пн 9` and a month `нояб.`, and no label names
an hour. The view measures each candidate on the row, and `DayCalendarLabels.Fitted` drops only the labels
that would collide: it places the first cell's label, then each month, then each Monday; a label that would
run past the row's right end is moved left to end there, and it is kept when it starts inside the row 4 px
clear of every label kept before it. So the first cell's label always shows, a month label beats a Monday it
would touch, a narrow row shows fewer labels of the same kinds, and a two-day archive shows its month at its
first cell.

A hover over `CalendarCellRow` goes through `Minimap/TrackPointer` and `DayAt`. The view holds the hovered x
as a distance from the row's right end, where the cells hang, so a resize keeps the ring on the cell under the
pointer. The cell under the pointer takes the hover ring, and the row's tooltip names the day and its rows
through `MinimapLabels.DayTip`, `Wed 11 Nov 2026 · rows 07:02-16:40`, or `Wed 11 Nov 2026 · no rows` for a day
without rows, and adds the count of the day's operator markers when it holds any,
`Wed 11 Nov 2026 · rows 07:02-16:40 · 2 markers` (`ui-text.md#the-minimaps-text`). The view keeps the counts
of `DayCalendar.MarkerCountsByDay`, recomputed on each `OperatorMarkerTimes` change, so a hover only looks its
day up. The row keeps the default arrow cursor. A press, and every move of its
drag, calls `MinimapPager.NavigateToArchiveDay` for the day under the pointer. It snaps the day's wall-clock
noon through `MinimapPage.SnapToData`, which picks the day itself when it holds rows and otherwise the
neighbour whose span lies nearer, the earlier on a tie, and lands the chart through `MinimapPage.Land` on that
day's Day page whatever the shown unit, the window's width and clock offset kept. A click on a Saturday
therefore lands on Friday and one on a Sunday on Monday. The page then shows the smallest page that holds the
landed window, so a window wider than the day shows the week or the month that holds it. A press left of the
first cell or past the last moves nothing.

**The detail track.** `StripCanvas` draws, bottom to top, `MinimapBand`, `MinimapRunShading`,
`WindowHighlight`, `TimeMarkerTicks` and `HoverLine`, all over `Pager.Page`. No baseline runs through it,
because a line through the middle of the band is no zero and no value.

- The window box is `MinimapTrackGeometry.WindowBox(page, Window, width, 6 px)`:
  it clips the window's start and end to fractions of the page and gives a width of
  `min(strip, max(6 px, widthFraction × strip))` and a left edge `MinimapGeometry.SpanLeftWithin` keeps
  inside the strip. It fills with
  `AppAccentFillBrush` inside a 2 px `AppAccentBrush` border, and an edge the page cuts draws no border.
  A window that misses the page hides the box: a window wholly outside a day's widened hours, such as an
  evening window on a day whose rows end at 17:00, or on an archive that runs around the clock the first
  window width after midnight, while the live window still starts on the day before. The box follows
  every `WindowChanged` and every page.
- The recipe runs are `MinimapViewModel.ShadedRuns`, `MarkerNavigation.Runs(ChartMarkers.All,
  NewestSample)` while `ChartMarkers.ShowsRecipeMarkers` holds and empty while `View` -> `Recipe markers`
  hides them. A run is a `RecipeStart` up to the next `RecipeEnd` or `RecipeInterrupted`. A start
  followed by another start runs to it, the last open start runs to the newest sample, a close with no
  start is skipped, and a run of no length is dropped. The list is paired again on every
  `ChartMarkers.Changed` and every `NewestSampleMoved` and reassigned only when it changes.
  `MinimapTrackGeometry.RunShades` clips each run that meets a Day page through `WindowBox` at a 2 px
  minimum, and `Minimap/MinimapRunShading` fills it full height with `AppRecipeRunFillBrush`. An
  interrupted run whose end lies on the page draws a 2 px `AppRecipeMarkerLineBrush` edge there. A Week or
  Month page shades no run.
- `TimeMarkerTicks` is one `Path` of full-height 1 px lines, one per operator marker on the page
  (`MinimapGeometry.FractionAt`), in `AppMarkerLineBrush`. A recipe marker draws no tick at either unit;
  the Day page shades its run instead.
- `PageTickRow` holds the tick labels of `MinimapTrackGeometry.PageTicks`. A Day page ticks whole
  wall-clock hours whose hour divides by a stride of 1, 2, 3, 4, 6 or 12, the smallest whose spacing
  reaches the widest label plus 8 px, stepped in UTC and kept at least one stride of real hours apart
  across a daylight-saving change. A Week or Month page ticks midnights at a stride of 1, 2 or 7 days. The labels
  are `HH:mm` and `ddd d` (`MinimapLabels.Tick`), and a label that would overflow the right end is
  dropped.

**The detail hover.** The code-behind takes the pointer on `StripCanvas` through `Minimap/TrackPointer`,
which the day calendar shares: a left press and every move of its drag navigate at the pointer's x, and
a move over the track hovers there; a strip
with no width hovers and navigates nothing, and without an extent nothing shows. `HoverLine` is a 1 px
`Border` above the box in the chart crosshair's `AppSecondaryForegroundBrush`, at the pointer's x and kept
inside the strip by `MinimapGeometry.SpanLeftWithin`. `HoverTimeLabel` shows the archive wall-clock time
under it, `HH:mm` on a Day page and `ddd d HH:mm` on a Week or Month page (`MinimapLabels.HoverTime`), centred
under the line and kept inside the row; a tick label it overlaps takes `Opacity` 0, so its layout stays
put and it shows again once the time moves away. The view holds the pointer's fraction, so a resize keeps
the line at its fraction and a page turn relabels the time. A press and a drag call
`MinimapPager.NavigateToPageFraction` with the pointer's fraction, which `MinimapGeometry.TimeAtFraction`
clamps to `[0, 1]`, and the pager centres the window there,
width kept, its start held at or after the page's start, through `Navigation.SetWindow`. The line follows
the pointer during a drag. A captured drag keeps delivering moves past the strip's edges, so a move off
the strip clears the hover; capturing the pointer raises `PointerExited` while the pointer is still over
the strip, so `PointerExited` is ignored while a drag holds.

**The controls.** The minimap's only controls are the two page steps.

- `PreviousPageButton` and `NextPageButton` stand at the strip's two ends, 8 px from it, the strip's 36 px
  tall, as `Button.iconStep.narrow` with Semi's `Tertiary` class (`ui-theme.md#the-applications-own-surfaces`),
  each with a 12 px chevron `PathIcon` (`ChevronLeftGeometry`, `ChevronRightGeometry`). Their tooltips come from styles: the
  Day text, the Week text under the `week` class and the Month text under the `month` class, both following
  `Page.Unit`, and "No earlier rows in the archive" or "No later rows in the archive" on `:disabled`, shown
  while disabled (`ToolTip.ShowOnDisabled`). The texts are under `ui-text.md#the-minimaps-text`.
- `MinimapViewTests.TheStrip_AndTheDayCalendarUnderIt_StackInANinetySevenPixelRow` pins the 97 px, the row
  order, the 8 px gap and the calendar's two edges on the strip's;
  `TheLastDaysCell_EndsAtTheStripsRightEdge_AndItsOutlineIsNotClipped` and
  `ATwoDayArchive_DrawsTwoNineteenPixelCells_AtTheRightEndOfTheRow` pin the right anchor.

**The states.** `MinimapViewModel.Hint` is `MinimapHint.For` over `IsArchiveEmpty`, `HasExtent`, the
page, the spans and `BandFeed.HasReadFailed`; `HintLabel` shows it centred over the strip and takes no
pointer. The hint over a page without rows names the page through `MinimapLabels.Page` in
`CultureInfo.CurrentCulture`, with the year of the page's last day: a Day page as `Fri 14 Aug 2026`, a Week
page as its days inclusive, `11-17 May 2026`, `27 Apr - 3 May 2026` across a month and
`29 Dec 2025 - 4 Jan 2026` across a year, and a Month page as its month, `November 2026`. A day number always
stands beside a month, so a Russian day or week takes the genitive (`пн 9 нояб. 2026`), and a Month page's
`MMMM` stands alone and takes the nominative (`ноябрь 2026`).

| State | Day calendar | Detail track | Controls |
| --- | --- | --- | --- |
| Empty archive: an extent read answered with no rows and no extent is held (`IsArchiveEmpty`) | blank | hint "No rows in the archive" | steps disabled |
| No extent after a failed extent read | blank | no hint, unless an earlier read answered empty; the message panel holds the failure | steps disabled |
| One-day archive | one cell at the row's right end inside the outline, its month label ending there | the day | both steps disabled, each with its "No earlier/later rows" tooltip |
| Page with no rows, while spans are held | the cells, the outline around a day with no cell | hint "No rows on {page}" | each step enabled while its side holds rows |
| Failed band read | unchanged | the band it held, hint "Not read, see messages" until the next read lands or the request empties | enabled |
| Day-spans read pending with no spans held | no cell; the outline, the marker ticks and the labels | the band | steps disabled, a calendar press moves nothing |
| Failed day-spans read with no spans held | no cell; the outline, the marker ticks and the labels | the band; no page is declared empty | calendar steps |
| No recipe runs, or recipe markers hidden | unchanged | no shading | unchanged |

**The band read.** `MinimapBandFeed` runs one read pipeline on the UI scheduler. Five things request a
read: an applied non-empty extent, a change of `DrawnPenId` (`WhenAnyValue` drops a raise with the same
value, and the value at construction is skipped), a change of `Pager.Page` (the value at construction is
skipped), a change of the column count, and the next-read schedule. Each request snapshots the drawn pen,
the page's bounds, the layer and the column count on the UI thread, drops the scheduled read, and
`Switch` cancels the read in flight through its `CancellationToken`. A request for a pen other than the
one `Band` holds turns `Band` null at once, so the band never shows one pen's shape in another pen's
colour, even when the new pen's read fails. An answer that lands after a newer request, already queued
on the UI scheduler when `Switch` dropped it, is discarded and schedules nothing. A read asks
`QueryHistoryAsync` for `[DrawnPenId]` over `[Page.FromUtc, Page.ToUtc)` at the request's column count
and logs its layer, column count and duration at Debug. `Band` holds the drawn pen's envelope from the
answer, null when the answer has none. With no drawn pen or no extent the request is empty: `Band` turns
null, `HasReadFailed` clears, and no read runs or stays scheduled, so a chart that draws no pen issues no
band reads until one is drawn again. At start the first read covers the calendar day, because no spans
have landed and the hours are 00:00-24:00; the landed spans change the page to the working hours, and
that page change reads again, cancelling the first read if it is still in flight.

**The column count.** The view reports `StripCanvas`'s width on every bounds change and on a new
`DataContext`, to `MinimapBandFeed.SetStripWidth`.
`MinimapBandFeed.ColumnsForWidth` rounds the width to the nearest multiple of `ColumnStep` (250), half
away from zero, and clamps it to `[250, MaximumColumns]`, `MaximumColumns` being 2 500, so a strip up to
2 500 px reads about one column per pixel. A width that keeps the count reads nothing, so a resize issues
no read per pixel, and every new count reads once. The count does not set the band's reads apart from the
chart's: a chart read asks for the data area's width clamped to 256..2 048
(`HistoryColumnTarget.FromDataAreaWidth`) times a `FetchRange.ColumnFactor` of 1 to 3, and that product
can be a multiple of 250. The provider needs no split. The test fake tells the band's reads from the
chart's by the request, judged as it is issued: a band read names the drawn pen alone over exactly the
page in force, at a multiple of 250 (`FakeDataProvider.RecognizeBandReadsOf`, installed by the fixtures
that build a minimap). A chart read that shows only the drawn pen over a fetch range equal to the page at
such a count would pass for the band's; no test builds one.

**The overview layer** is `ChartNavigationController.LayerForWidth(page span, the band's previous
layer, the column count, 1 visible pen)`: the chart's own ladder at the band's column count, so no
second ladder exists, and the chart's pen count never shrinks the band's ladder. The ladder reads `Raw`
up to 15 s times the column count, `Minute` up to 15 min times the column count and `Hour` beyond, and
the hysteresis keeps a span at a boundary on the layer it already has. A 10 h Day page reads `Minute` at
1 500 columns (a `Raw` ceiling of 6 h 15 min) and `Raw` at 2 500 (10 h 25 min); a week reads `Hour` at
250 columns and `Minute` at 1 500. A 10 h page at 1 500 columns is a 24 s bucket. Every coarse read,
`Minute` or `Hour`, includes the provider's raw fresh tail, up to four point spacings of one pen
(`data-integration.md#layer-ladder`).

**The next-read schedule.** When a read lands, success or failure, the next read is scheduled
`MinimapBandFeed.NextReadDelay(span)` later: `clamp(span / 1000, 2 s, 60 s)` over the page span that read
covered, 36 s on a 10 h Day page and 60 s on a week. A requested read drops the scheduled one, so a read
slower than that delay is never cut off by its own successor; the next read follows it one delay after
it lands. While the newest sample lies on the page, the band's right end trails it by up to one delay:
a thousandth of the strip on a page from 33 min to 16.7 h, 1.5 px on a 1 500 px strip, and 0.15 px on a
week. The schedule keeps reading while the operator hides the minimap row, at the same cost as when the
strip shows.

**The band's shape** is pure geometry. `MinimapGeometry.BandFigures(band, first, last, width,
height)`, called with the page's `FromUtc` and `ToUtc`, maps the envelope to `BandFigure(Outline,
CenterLine)` values in control coordinates, each point a `BandPoint(X, Y)` (`Core/Trends/BandFigure.cs`).
A column is drawn when it lies in `[first, last]` and its min, max and centre are all finite; a NaN or an
infinite value is a break. Each run of drawn columns is one figure: its outline runs along the max line
forward and the min line back, and its centre line follows `Center`, so a break in the archive is a gap
in the band. A run of one column is drawn 2 px wide, kept inside the strip, so a short island between two
breaks stays visible. The y range is the pen's own, the min and max of the drawn columns, widened through
`Core/Trends/ValueRange.Widen`, the NaN-skipping rule `PenScaleModel` uses for its auto scale. The maximum
maps to the top and the band is never logarithmic. A flat range is padded by ±1, so a flat pen draws a
band at mid-height. A zero span, width or height, or an envelope with no drawn column, gives no figure.

**The band control.** `Minimap/MinimapBand` is a `Control` with a `Render` override (avalonia.md,
custom drawing) and reads nothing. `Figures` and `BandColor` are styled properties registered with
`AffectsRender`; `Render` fills each outline with `BandColor` at 35 % opacity and strokes each centre
line 1 px wide at full colour. It sits at the bottom of the strip with `IsHitTestVisible="False"`, so a
press on the band reaches the strip. `MinimapView.LayoutBand` sizes it to the strip and computes its
figures from `Band`, the page and the strip's size; it runs on a resize of the strip and on a change of
`HasExtent`, `Pager.Page` or `Band`. The window box is placed on a change of `HasExtent`, the page or
`Pager.Window`, so a pan or a drag inside the page moves the box and leaves the figures as they are.

**The band's colour** follows the drawn pen's stored colour live. `BandColor` is a `WhenAnyValue`
over `DrawnPenId` and `Pens`, switched to that pen state's `Pen.Color` and ending in `ToProperty`, so a
recolour in the pen editor reaches the strip with the chart and needs no read. It is the stored hex
string, null with no drawn pen, and the view converts it with the legend's
`LegendConverters.HexToBrush`.

**Failures.** A failed band, day-spans or extent read reaches the message panel through
`ResultReporting.TryReportFailure`, which maps it through `ArchiveFailureMapper.Map` like every
other read. `Minimap/OutageReport` holds the outage policy, one instance for the band reads in
`MinimapBandFeed`, one for the day-spans reads in `ArchivePresenceFeed` and one for the extent reads in
`MinimapViewModel`: it reports the first failed read of an outage and logs every further one at Warning,
with the exception when the read threw, and the first successful read ends the outage and arms the
report again. The report starts armed. The panel coalesces a repeat of its newest entry only, and during
an outage other entries land between two band reads, the status bar's `ConnectionLost` and the chart's
failed history reads among them, so a band entry per read would not coalesce; one entry per outage keeps
the panel readable. The catalogue sync reports no lost connection itself: it logs each one, and three in
a row turn the status bar to the fault with one entry (`overview.md#a-failed-read`). Each band and
day-spans read turns a throw into a failed `Result`, so a throw never ends its pipeline and the next band
read is still scheduled; a throw while a read is applied, the next read's scheduling included, goes to
`TryReportFailure` as well.

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
