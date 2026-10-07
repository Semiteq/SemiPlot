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
  the coordinator — `TrendCoordinator(IDataProvider dataProvider, IReadOnlyList<Pen> pens,
  IScheduler dataScheduler, IScheduler uiScheduler, TimeSpan? batchWindow = null)`,
  `Buffer` on the data scheduler, `ObserveOn` on the UI one. No second `IScheduler` container registration.
  The pen catalogue is passed in because the coordinator needs the pen identifiers in its constructor
  and `QueryPensAsync` cannot be awaited there; the composition root hands over the start sequence's
  read, and a later pen set arrives through `SetPens` (`data-integration.md#realtime`).
- **Decimation envelope contract:** history record per pen = ascending `X[]` + `Min[]` + `Max[]` + center
  `Y[]`; realtime stays single-value `double?[]` (null = gap); rendered as one `EnvelopeLine` polyline
  (see Renderer).
- **Δ cursors:** Δy is reported only for the **active/selected pen** (pens share X but have independent Y
  scales, so a global Δy is meaningless).
- **Bad quality → gap:** OPC bad-quality is mapped to `null` at the `IDataProvider` boundary, reusing the
  null = gap path; a distinct value-present-but-bad-quality flag is deferred.
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
- **Time display:** **computer local time** (machine local), not UTC.
- **Line style:** both stepped and interpolated, **configurable per pen**.
- **Performance:** **FPS locked at 30**; data updates no faster than **10 Hz (100 ms)**; up to
  **50 displayed pens**.
- **Decimation backend:** kept behind the data-provider **stub** for now; production backend
  will be **PostgreSQL**, but whether it stores pre-trimmed/layered data is **unknown** — the
  data layer must support either server-side aggregation or in-process decimation.
- **Dropped / out of scope:** horizontal cursor (dropped); alarm/event overlays (out of scope);
  annotations. **`[LATER]`:** view persistence, export/snapshot/print.

## Terminology

| Term            | Meaning                                                                 |
| --------------- | ----------------------------------------------------------------------- |
| Pen             | One plotted series (one tag): color, name, scale, current value.        |
| Archive layer   | Archive resolution / decimation level: raw / minute / hour / day (data-integration.md, `l` column). NOT a plotted curve. |
| View window     | Visible time range `[from, to]`; width = zoom level.                    |
| Now-marker      | Marker at the latest measured sample = current moment / live edge.      |
| Sticky          | View auto-scrolls to keep the live edge at the right edge (real-time follow). |
| Active pen      | The pen whose scale is shown on the primary axis; selected by click.    |
| Cursor / X-trace | On-hover Avalonia overlay vertical line reading each pen's value at the cursor X. |

> Terminology fix: the user's draft used "слой графика" for a plotted curve. To avoid clashing
> with the archive-resolution "layer", a plotted curve is always a **pen**.

## Single chart — time behavior

Requirements: the now-marker / sticky live-edge follow (trend-feature-spec.md §RT-2), pan with a
constant window width down to the first stored sample (§TM-3), wheel zoom from 1 second to 1 year
about the cursor anchor (§TM-2), and the autoscale / manual / log axis modes (§AY-3 … §AY-6). The
as-built mechanics that realize them:

- **Zoom width is quantized onto a 1.25 geometric ladder** (`TrendNavigationModel.Zoom`); the
  reciprocal wheel factors (in 0.8 = 1/1.25, out 1.25) share grid points so an in→out cycle
  round-trips to the origin width instead of drifting through accumulated float error (§TM-2
  acceptance). No-lag from 1 s to 1 year is guaranteed by decimation, not the chart control (see
  "Decimation & performance").
- **`From` is clamped to `≥ FirstSample`** so a wide window never reaches back past the first
  stored sample and renders the missing left span as data (§TM-3).
- **Zoom history is debounced off the UI thread** (§DA-9). Gesture-driven re-queries flow through a
  single chokepoint (`Chart/ChartHistoryRequestDebouncer`): `Throttle` collapses rapid notches to one
  trailing request after the gesture goes quiet, the query runs on the data scheduler, one at a time, and
  the newest window that arrived while it ran runs when it lands (so a read slower than the cap still
  completes, and the newest window is the last applied). Per-zoom redraws are coalesced through the
  redraw seam, one emission 33 ms after the first request of a span, not an inline refresh. The startup
  `RequestInitialHistory` is an ordinary request on that path, so the initial load and gestures share one
  latest-wins history path; the first-snap `TrackDataExtents` path stays non-requerying (single initial
  load).
- **Axis scaling gestures (as-built):** entering min/max = fixed manual limits (§AY-3); View -> Pen
  scale -> Autoscale (§AY-4) and View -> Pen scale -> Restore initial scale act on the active pen, and
  the panel's two buttons act on the pen the panel was opened for. The axis scale panel is the only
  place that types limits: the navigation bar carries time navigation only. The scale modes are
  `auto`, which ranges over the columns inside the visible window, and `manual` (§AY-3, §AY-4); the
  logarithmic axis is an axis *type* with values ≤ 0 sanitized before scaling (§AY-6).

## Multi-pen / multi-axis behavior

Requirements: multiple independent Y axes (trend-feature-spec.md §AY-1), the per-pen "each on its
own axis" case (§AY-2), and the shared-X / independent-Y invariant (§TM-1). As-built mechanics:

- Plot up to **50 pens**, each with a scale of its own.
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
never sees it. `TrendChartView.InitializeComponent` therefore also sets
`_plotControl.HandleMouseWheelEvent = false`, which is what keeps wheel zoom working; the view's own
handler is then the only writer of `Handled` for the wheel. The left button is a
single tool with an explicit state (`Chart/LeftButtonTool` = `Pan` | `DeltaPlacement`); the active
tool is sourced from the navigation bar's delta-mode toggle (`TrendChartViewModel.ActiveLeftButtonTool`),
so there is one left-button gesture, not overlapping hidden branches.

- **Scroll = zoom about the cursor anchor; left-drag = hand pan.** Press captures the pointer and
  switches the cursor to a grab icon (`StandardCursorType.SizeAll`); each move pans the X window via
  `TrendNavigationModel.Pan`; release ends the drag and restores the hand cursor. The hover readout
  and crosshair (an Avalonia overlay) are suppressed for the duration of the drag.
- **Sticky to real-time by default.** A button detaches sticky (pan into the past); clicking it
  again re-attaches and returns to real-time. `WindowChanged` is the single writer of the navigation
  bar's `IsSticky` (refreshed from `Navigation.IsSticky`), so auto-detach and `JumpToNow` re-attach stay
  in sync with the button — no double write path.
- **Panning so the live edge scrolls out of the view** auto-detaches sticky.
- **Hover readout + crosshair (X-trace) live in an Avalonia overlay, not on the plot.** Moving the
  pointer does NOT trigger a ScottPlot re-render. The crosshair is an Avalonia `Line` and the readout
  an Avalonia `Border`/`TextBlock` laid out on a transparent `Canvas` over the `AvaPlot`
  (`Chart/TrendChartView`). On hover the view positions them by projecting the view model's cursor X
  through `Plot.GetPixel` and `Chart/ChartCursorOverlay` (cursor pixel X + `DataRect` + render scale →
  crosshair endpoints + readout anchor in DIP space, clamped). The readout text is the pure
  `Chart/ChartHoverReadout.BuildContent` string: the local timestamp plus every *visible* pen's value
  at the cursor X (one line per pen; gap or missing pen → dash). The overlay is suppressed while a drag
  is in progress or delta mode is active (`IsDragging || IsDeltaModeEnabled`) and is repositioned from
  the coalesced `RedrawRequested` seam (after `Refresh()`) and on `SizeChanged` so it tracks
  pan/zoom/resize/live-edge without per-event re-renders.
- **Delta cursors (Δt / Δy) via an explicit navigation-bar mode.** The bar's "Delta" toggle
  (`NavigationBarViewModel.IsDeltaModeEnabled`) sets the chart into `DeltaPlacement`: two left clicks
  place the cursors and drag does NOT pan; toggling off clears the placed cursors and hides the
  lines. Δt and the **active-pen** Δy (`Core/Trends/DeltaCursorModel` → `DeltaReadout`) are shown in
  an inline readout next to the toggle. (The legacy `DeltaCursorsEnabled` flag and the hidden
  left-click hijack branch were deleted.)
- **Y-axis click-region scale edit.** A press on the active pen's Y-axis panel band
  (`Chart/ChartAxisRegion`, computed from the last render layout) is handled before pan/delta routing and
  opens the axis scale panel (below). The router reads no click count and no half of the axis. The press
  never starts a pan or places a delta cursor.
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
  that rounds (0.4 shown as "0") never moves the scale on Apply; only a field whose text changed is parsed.
  Typed text is read by `PenFormRules.TryReadBound`, the pen editor's rule, in the
  current culture: an entry that is not a number in that culture, such as "150.5" under `ru-RU`, is
  invalid, and the last value that parsed is never applied in its place.
- Apply writes both bounds as a manual scale on the pen through `SetAxisLimits`. Enter in either field
  applies; the two buttons and the panel take no Enter binding, so Enter on the focused Autoscale button
  presses Autoscale. Escape and a click outside close the panel and write nothing.
- An unreadable field, an empty field or a minimum not below the maximum is invalid on every edit: the
  field takes the `invalid` border, the message line names the rule and Apply is disabled. The panel
  keeps its size (`ui-theme.md#the-axis-scale-panel`).
- Autoscale and Restore initial scale call `AutoscalePen` and `RestoreInitialScale(penId)` for the
  panel's pen and close the panel. The View -> Pen scale submenu items call `AutoscaleActivePen` and
  `RestoreInitialScale()`. The submenu header names the drawn pen and follows `DrawnPenId` and the
  catalogue's pen names; while no pen's axis is drawn it reads "Pen scale".

## Decimation & performance (architectural core)

Underwrites "no lag from 1 s to 1 year." A data-layer requirement, not a chart-control feature.
Requirements: archive aggregation layers (trend-feature-spec.md §DA-2), auto layer-by-width with
hysteresis (§DA-3), decimation to the canvas width with surviving spikes and gap anchors (§DA-5),
and the render budget (§RT-4). The as-built rationale and mechanics:

- **Never feed raw millions of points to the chart.** A 1080p-wide plot shows ~one point per
  pixel; a month of 10-second data is ~135 raw samples per pixel (MasterSCADA). The data layer
  returns roughly viewport-width points (§DA-5).
- **Window width and canvas width together select the archive layer** (raw / minute / hour / day):
  deeper ranges use coarser layers — the layer design in data-integration.md, validated by industry
  (MasterSCADA layered archive; AVEVA "Cyclic" retrieval). No ceiling is a constant: it is
  `nextCoarser(layer).ToPointSpacing() × TargetColumnCount`, so it moves with the canvas.
  `ChartNavigationController.LayerForWidth` applies a **10% hysteresis band** at each ceiling so a
  notch-by-notch zoom hovering on a boundary does not flip-flop the layer every notch (§DA-3) —
  which, at the Raw side, appended a far-right raw point that straight-lined across the wide span.
  The column count carries **its own deadband** on the same grounds: one quantisation step doubles or
  halves every ceiling, so a pixel of jitter across a boundary must not move it.
- **Empty edge sub-spans render as gaps, not straight lines** (§DA-5). When the leading or trailing
  sub-span of the samples it is given has no data, `MinMaxDecimator` (`SemiPlot.Core.Trends`, on the
  coarse-layer read path) anchors a `NaN` column there, so the line segments instead of the chart
  bridging the empty span with a straight line to the live-edge point (the right-side straight-line
  collapse fix). The edge it anchors at is the first and last **row**, not the window bound — the
  decimator never sees the window. The archive provider does not reach the window edge, because the
  archive writes a row only when a value changes; it reaches the same anchors from the break markers
  instead, by a different route per layer. On the coarse layers `HistoryRowFold` appends a null one
  tick after a `q = 32` row and the decimator splits on it. On Raw no decimator runs: the server
  closes a bucket at the marker, and `BucketedRowFold` writes the `NaN` column itself one tick after
  that bucket (see `data-integration.md`, Quality and gaps).
- **Use a min/max-per-pixel envelope, not plain sampling.** Plain decimation aliases away spikes
  (AVEVA warns of exactly this). Retain min AND max per pixel column so spikes survive (M4:
  min/max/first/last per column → visually lossless; MinMaxLTTB for speed; Power Chart "MinMax").
- **Aggregation is placed per layer** (§DA-2): Raw aggregates in the query
  (`BucketedRawWindow`, `GROUP BY date_bin`), one row per column, so no raw stream reaches the
  client. The coarse layers fold in-process through `MinMaxDecimator`, over rows the SCADA already
  thinned. Both sit behind `IDataProvider` and reach the chart in one envelope vocabulary.
- **Performance budget (§RT-4):** 30 FPS pan/zoom lock; input data ≤ 10 Hz; ≤ 50 simultaneous pens;
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
read local time. DST boundary behavior follows the machine's local-time conversion (cosmetic; no
special-casing).

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
chart's own hover readout (§CU-2) and no longer a legend cell; the grey scale range is gone with it —
it read as the pen's measured extremes and was the padded axis bound.

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
250)`: the chart's own ladder at the strip's column count, so no second ladder exists. 250 columns
read `Raw` up to about 62 min, `Minute` up to about 62 h, `Hour` up to about 62 days and `Day`
beyond, and the hysteresis keeps a span at a boundary on the layer it already has. Every coarse read,
`Minute`, `Hour` or `Day`, includes the provider's raw fresh tail, up to four point spacings of one pen
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
first successful read ends the outage and arms the report again. The report starts armed. During an outage the catalogue
sync reports every 5 s with its own detail, and the panel coalesces a repeat of its newest entry only,
so a band entry per read would never coalesce; one entry per outage keeps the panel readable. The
catalogue sync answers its 5 s loop the other way: it logs its first two failures in a row and reports
from the third (`overview.md#a-failed-read`). Each read turns a throw into a failed `Result`, so a
throw never ends the pipeline and the next read is still scheduled; a throw while a read is applied,
the next read's scheduling included, goes to `TryReportFailure` as well.

## Out of scope / later

- **Deferred (NICE, not in MVP):** horizontal cursor / crosshair (trend-feature-spec.md §CU-5).
- **Out of scope:** alarm/event overlays from the `messages` log; annotations.
- **`[LATER]`:** save pen sets + scales + layout as named views/templates; snapshot / export /
  print (trend-feature-spec.md §MS-6); touch input (not relevant for target operator PCs).

## Renderer & UI framework

- **ScottPlot 5** (as-built): MIT; SkiaSharp; each pen gets its own `IYAxis` and every one of them is
  a left axis (`Axes.Left`, then `AddLeftAxis`), non-active axes `IsVisible = false`.
  Trends drawn as one per-pen `EnvelopeLine` — our own `IPlottable` — over a data-layer-decimated
  envelope; a NaN column segments the line at gaps; `DataLogger` is prior art, not the pattern. Shared-X
  invariant: all pens pinned to `plot.Axes.Bottom`.
- **Avalonia 12.0.5 / net10** (as-built): hosts `ScottPlot.Avalonia` 5.1.59, which depends on Avalonia
  12.0.0. Mirrors SemiStep's patterns (ReactiveUI + MS.DI + Serilog + Semi.Avalonia) and now its
  versions too. `Avalonia.HarfBuzz` 12.0.5 is referenced and `UseHarfBuzz()` is called explicitly; Skia carries
  no text shaper.
- No qualifying open-source .NET SCADA trend-viewer reference repo exists; built from library
  primitives, with ScottPlot's `DataLogger` demo as the nearest realtime pattern.

### Reuse from SemiStep (sibling repository)

Same author, same conventions — reuse directly:

- DI + two-phase startup (validate config → build provider → `App.RunStarted`); `IServiceCollection`
  extension methods.
- Coordinator-as-event-hub: expose realtime as `IObservable<T>`, `ObserveOn(MainThreadScheduler)
  .Publish().RefCount()` at the source; subscribers `DisposeWith(_disposables)`. This is the
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
