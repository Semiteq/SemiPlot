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
- **UI framework:** **Avalonia 12.0.5 / net10**, the same pairing `SemiStep` ships
  (`ReactiveUI.Avalonia` 12.0.3, `ScottPlot.Avalonia` 5.1.59, which itself depends on Avalonia 12.0.0).
  *As-built note:* `SemiPlot.UI` references `Avalonia.HarfBuzz` 12.0.5 and the builder chain calls
  `UseHarfBuzz()` between `UseSkia()` and `UseReactiveUI()`. `App.BuildAvaloniaApp` names the platform
  itself (`UseWin32().UseSkia()`) instead of calling `UsePlatformDetect()`, and Skia carries no text
  shaper, so without that call `AppBuilder.Setup` throws "No text shaping system configured" before any
  window exists. The headless platform registers a shaper of its own, so no headless test reaches that
  path — `SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests` reads the composed builder back instead.
  `AvaloniaScheduler` / `UseReactiveUI` live in namespace `ReactiveUI.Avalonia` (NOT
  `Avalonia.ReactiveUI`), and `UseReactiveUI` takes a mandatory `Action<ReactiveUIBuilder>`.
  Stack: **ReactiveUI** MVVM
  (`ReactiveObject` / `ReactiveCommand` / `AvaloniaScheduler.Instance` = `RxApp.MainThreadScheduler` /
  `CompositeDisposable`; `RxSchedulers.MainThreadScheduler` also ships in 12.0.3 and is not used),
  **Microsoft.Extensions.DependencyInjection** (extension methods, primary constructors), **Serilog**
  (file, rolling 5 MB / 5 files), **FluentTheme** (light). Rationale for ReactiveUI: the data layer is
  Rx-native and the VMs are derived-state-heavy (sticky, cursor, active-pen) — a fit for
  `WhenAnyValue`/OAPH/`ReactiveCommand`; CommunityToolkit.Mvvm is an acceptable lower-friction
  alternative. The Core `IDataProvider` / DTO / stub layer is retained.
- **Visual language:** JetBrains / IntelliJ look is a **north-star, not MVP** — MVP uses the
  stock FluentTheme (as SemiStep does); IJ-style theming is a later, separate effort.
- **Scheduler seam:** Core keeps the bare `IScheduler` (`DefaultScheduler.Instance`) for data timing;
  the UI scheduler (`AvaloniaScheduler.Instance`) is captured in `AfterSetup` and passed explicitly to
  the coordinator — `TrendCoordinator(IDataProvider dataProvider, IReadOnlyList<Pen> pens,
  IScheduler dataScheduler, IScheduler uiScheduler, TimeSpan? batchWindow = null)`,
  `Buffer` on the data scheduler, `ObserveOn` on the UI one. No second `IScheduler` container registration.
  The pen catalogue is passed in because the coordinator needs the pen identifiers in its constructor
  and `QueryPensAsync` cannot be awaited there; the composition root reads it once and hands it over.
- **Decimation envelope contract:** history record per pen = ascending `X[]` + `Min[]` + `Max[]` + center
  `Y[]`; realtime stays single-value `double?[]` (null = gap); rendered as `Scatter` + `FillY` (see Renderer).
- **Δ cursors:** Δy is reported only for the **active/selected pen** (pens share X but have independent Y
  scales, so a global Δy is meaningless).
- **Bad quality → gap:** OPC bad-quality is mapped to `null` at the `IDataProvider` boundary, reusing the
  null = gap path; a distinct value-present-but-bad-quality flag is deferred.
- **Real-time return:** "jump to real-time" re-attaches sticky immediately; now-marker at the
  **right edge** (centering is at most a transient transition animation).
- **Many-axes management:** **single active Y axis + per-pen autoscale** (legacy SCADA model);
  clicking a pen makes it active. A shared common scale for a *group* of pens is supported on top.
- **Axis scaling gestures:** double-click axis = autoscale; entering values = fixed manual
  limits. Basic actions are also **duplicated in a toolbar** (not only axis gestures).
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
  trailing request after the gesture goes quiet, the query runs on the data scheduler, and `Switch`
  drops any still-in-flight query when a newer window arrives (latest-wins, so a stale response never
  overwrites the current window). Per-zoom redraws are coalesced through the 30 FPS `Sample(33 ms)`
  redraw seam, not an inline refresh. The startup `RequestInitialHistory` awaits `QueryHistoryAsync`
  directly (bypassing the debounce, fires once promptly) and applies through the same
  monotonic-sequence counter as every gesture re-query, so the initial load and gestures share one
  latest-wins history path; the first-snap `TrackDataExtents` path stays non-requerying (single initial load).
- **Axis scaling gestures (as-built):** double-click an axis = autoscale (§AY-4); entering min/max =
  fixed manual limits (§AY-3); the same actions are duplicated in a toolbar. Autoscale modes are
  `auto`, `manual`, and `autoscale-to-window` (§AY-3 … §AY-5); the logarithmic axis is an axis
  *type* with values ≤ 0 sanitized before scaling (§AY-6).

## Multi-pen / multi-axis behavior

Requirements: multiple independent Y axes (trend-feature-spec.md §AY-1), the per-pen "each on its
own axis" case (§AY-2), and the shared-X / independent-Y invariant (§TM-1). As-built mechanics:

- Plot up to **50 pens** with either a **shared** axis or **separate** scales.
- **Axis management = single active axis + per-pen autoscale:** the active pen's scale is surfaced
  on the primary axis; non-active pens scale individually with their axes hidden, so many pens do
  not spill many visible axes. The literal "N lines, each on its own axis" case (§AY-2) requires a
  per-pen `AxisKey`, not a hard-wired group key.
- A **shared common scale for a group** of pens (e.g. 16 heaters together; dampers separately) is
  supported on top of the active-axis model.
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
tool is sourced from the toolbar delta-mode toggle (`TrendChartViewModel.ActiveLeftButtonTool`),
so there is one left-button gesture, not overlapping hidden branches.

- **Scroll = zoom about the cursor anchor; left-drag = hand pan.** Press captures the pointer and
  switches the cursor to a grab icon (`StandardCursorType.SizeAll`); each move pans the X window via
  `TrendNavigationModel.Pan`; release ends the drag and restores the hand cursor. The hover readout
  and crosshair (an Avalonia overlay) are suppressed for the duration of the drag.
- **Sticky to real-time by default.** A button detaches sticky (pan into the past); clicking it
  again re-attaches and returns to real-time. `WindowChanged` is the single writer of the toolbar
  `IsSticky` (refreshed from `Navigation.IsSticky`), so auto-detach and `JumpToNow` re-attach stay
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
  the throttled `RedrawRequested` seam (after `Refresh()`) and on `SizeChanged` so it tracks
  pan/zoom/resize/live-edge without per-event re-renders.
- **Delta cursors (Δt / Δy) via an explicit toolbar mode.** A toolbar "Delta" toggle
  (`TrendToolbarViewModel.IsDeltaModeEnabled`) sets the chart into `DeltaPlacement`: two left clicks
  place the cursors and drag does NOT pan; toggling off clears the placed cursors and hides the
  lines. Δt and the **active-pen** Δy (`Core/Trends/DeltaCursorModel` → `DeltaReadout`) are shown in
  an inline toolbar readout next to the toggle. (The legacy `DeltaCursorsEnabled` flag and the hidden
  left-click hijack branch were deleted.)
- **Y-axis click-region range edit.** A press on the active pen's Y-axis panel band
  (`Chart/ChartAxisRegion`, computed from the last render layout) is handled before pan/delta routing:
  a single click in the **upper** half edits MAX, the **lower** half edits MIN (top pixel = max,
  accounting for pixel-Y inversion), opening an inline numeric editor seeded with the clicked value;
  a **double-click** autoscales the axis (`ScaleMode.Auto`). The untouched bound is carried from the
  current computed range (`Chart/ChartAxisEdit.SeedManualLimits`) and committed into `PenScaleModel`
  manual limits. The press never starts a pan or places a delta cursor.
- **Horizontal cursor / crosshair** — not in the MVP; deferred as a NICE item
  (trend-feature-spec.md §CU-5), not permanently dropped.

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
  sub-span of the samples it is given has no data, `MinMaxDecimator` (`SemiPlot.Core.Trends`, shared
  by every provider) anchors a `NaN` column there, so the line segments instead of the chart bridging
  the empty span with a straight line to the live-edge point (the right-side straight-line collapse
  fix). The edge it anchors at is the first and last **row**, not the window bound — the decimator
  never sees the window. The stub reaches the window edge anyway because it synthesises a point per
  tick across the whole window; the archive provider does not, and slice
  `postgres-gap-reconstruction` is where its rows learn to carry the break markers that produce the
  same anchors.
- **Use a min/max-per-pixel envelope, not plain sampling.** Plain decimation aliases away spikes
  (AVEVA warns of exactly this). Retain min AND max per pixel column so spikes survive (M4:
  min/max/first/last per column → visually lossless; MinMaxLTTB for speed; Power Chart "MinMax").
- **Aggregation runs PostgreSQL-side** (§DA-2): per the spec the production layer aggregates in the
  PostgreSQL query rather than streaming raw rows to the client. Both providers fold their samples
  in-process through `MinMaxDecimator`; the data layer is structured so that decimator and a
  server-side SQL aggregate are interchangeable behind `IDataProvider`.
- **Performance budget (§RT-4):** 30 FPS pan/zoom lock; input data ≤ 10 Hz; ≤ 50 simultaneous pens;
  points per pen handed to the chart ≈ viewport width × 2–4.

## Data quality & line rendering

- **Gap / bad-quality rendering** (trend-feature-spec.md §DA-8): nulls and quality (archive `q`
  column) render as visible gaps, not interpolated across.
- **Stepped vs interpolated lines** (§PN-5): configurable per pen (stepped for discrete/digital
  tags like valve on/off; interpolated for analog). Mapped to the renderer via `PenLineStyleMap`.

## Time handling

Local-time display is required by trend-feature-spec.md §TM-1 (the single `LocalTimeAxis`).
As-built: samples are UTC over the wire (data-integration.md). All UTC↔OADate conversion is
funneled through `Chart/LocalTimeAxis` at every render boundary (plotted X, axis limits,
cursor/delta X), with a `DateTimeAutomatic` tick generator on the shared bottom axis so labels
read local time. DST boundary behavior follows the machine's local-time conversion (cosmetic; no
special-casing).

## Legend

Required by trend-feature-spec.md §PN-8. As-built: a grouped mini-legend with checkbox / color /
name / current value (charting.md), plus **value at cursor** and the active pen's **scale range**.

## Archive-overview minimap

Required by trend-feature-spec.md §TM-4. As-built: a thin overview strip beneath the chart shows
the **full archive extent** with the current `[From, To]` view window highlighted, for orientation
and fast navigation across long archives.

- The extent comes from a new `IDataProvider.QueryArchiveExtentAsync()` seam returning an
  `ArchiveExtent(FirstUtc, LastUtc)` (data-integration.md). The stub returns a synthetic depth
  (now − 7 days … now); the real provider will return the true archive bounds.
- `Minimap/MinimapViewModel` reaches the extent through a `TrendCoordinator.QueryArchiveExtentAsync()`
  pass-through (mirroring the `QueryHistoryAsync` seam + UI-scheduler discipline); it never holds the
  `IDataProvider` directly. Extent → strip / window → fractions geometry is pure
  (`Core/Trends/MinimapGeometry`).
- `Minimap/MinimapView` is a Canvas-based strip (not a second `AvaPlot`): a highlight border sized
  from `WindowStartFraction` / `WindowWidthFraction`. Press/drag converts pointer-X to a fraction →
  `NavigateToFraction`, which recenters the window via the **same** `ChartNavigationController` the
  chart navigates with. The highlight tracks every `WindowChanged` (pan / zoom and the sticky
  live-edge advance).

## Out of scope / later

- **Deferred (NICE, not in MVP):** horizontal cursor / crosshair (trend-feature-spec.md §CU-5).
- **Out of scope:** alarm/event overlays from the `messages` log; annotations.
- **`[LATER]`:** save pen sets + scales + layout as named views/templates; snapshot / export /
  print (trend-feature-spec.md §MS-6); touch input (not relevant for target operator PCs).

## Renderer & UI framework

- **ScottPlot 5** (as-built): MIT; SkiaSharp; each distinct-unit pen gets its own `IYAxis`
  (`AddLeftAxis`/`AddRightAxis`), same-unit groups share one axis, non-active axes `IsVisible = false`.
  Trends drawn as `Scatter` + `FillY` band over a data-layer-decimated envelope; NaN segments the
  line at gaps (no `OnNaN` property in ScottPlot 5); `DataLogger` is prior art, not the pattern. Shared-X
  invariant: all pens pinned to `plot.Axes.Bottom`.
- **Avalonia 12.0.5 / net10** (as-built): hosts `ScottPlot.Avalonia` 5.1.59, which depends on Avalonia
  12.0.0. Mirrors SemiStep's patterns (ReactiveUI + MS.DI + Serilog + FluentTheme) and now its versions
  too. `Avalonia.HarfBuzz` 12.0.5 is referenced and `UseHarfBuzz()` is called explicitly; Skia carries
  no text shaper.
- No qualifying open-source .NET SCADA trend-viewer reference repo exists; built from library
  primitives, with ScottPlot's `DataLogger` demo as the nearest realtime pattern.

### Reuse from SemiStep (sibling repository)

Same author, same conventions — reuse directly:

- DI + two-phase startup (validate config → build provider → `App.Run`); `IServiceCollection`
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
