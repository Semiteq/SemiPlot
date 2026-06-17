# Trend Interaction (behavior spec — as-built)

Behavioral / interaction requirements for the trend viewer: time navigation, real-time
sticky scrolling, multi-pen axis management, scaling, cursors, decimation, and rendering.
Complements [charting.md](./charting.md) (static charting features) by defining how the
viewer *behaves* under operator interaction.

> Status: **as-built (MVP implemented).** This spec is realized in `SemiPlot.UI` on Avalonia
> 11.3.8 + ScottPlot 5; the Decisions log below records the original rationale and is kept for
> history. Source: desired behavior of the MasterSCADA 3 trend window plus user-requested fixes
> (reference image in the machine docs), cross-checked against mature vendor trend controls
> (Ignition Power Chart / Easy Chart, AVEVA Trend Client, WinCC, Simple-Scada) and decimation
> literature (M4, MinMaxLTTB). Data integration target stays Simple-Scada 2 (see
> data-integration.md); MasterSCADA 3 / Ignition Power Chart are **UX references** only. Non-MVP
> items remain marked `[LATER]`.

## Decisions log (2026-06-16)

- **Renderer:** **ScottPlot 5** (MIT, SkiaSharp; `ScottPlot.Avalonia` 5.1.57 on **Avalonia 11.3.8**).
  Chosen over OxyPlot for built-in independent multi-axis. Trends render as a per-pen **`Scatter`
  center line + `FillY` min/max band** over a data-layer-decimated envelope;
  `DataLogger` is cited prior art only, not the implementation pattern (it cannot carry a
  pre-decimated min/max band). Supersedes the uPlot/WebView2 stack in overview.md / charting.md.
  *As-built reconciliation:* ScottPlot 5.1.57 `Scatter` has **no `OnNaN`/`Gap` property** (a plan
  assumption); gaps are produced by feeding `double.NaN` (the default `Straight` path strategy
  breaks the line at NaN), so the committed gap mechanism is "NaN in Center/Min/Max", not an enum.
- **UI framework:** **Avalonia 11.3.8 / net10** (binding floor 11.3.8, set by `ReactiveUI.Avalonia`
  11.3.8). Deliberately diverges from SemiStep's Avalonia 12 because **ScottPlot.Avalonia has no
  Avalonia 12 build** — SemiStep is mirrored for *patterns*, not versions. *As-built note:*
  `Avalonia.HarfBuzz` has no 11.3.x package (only 12.x), so it is omitted and there is no explicit
  `UseHarfBuzz()`; text shaping arrives transitively via `Avalonia.Skia` 11.3.8 → `HarfBuzzSharp`.
  `AvaloniaScheduler` / `UseReactiveUI` live in namespace `ReactiveUI.Avalonia` on 11.3.8 (NOT
  `Avalonia.ReactiveUI`). Stack: **ReactiveUI** MVVM
  (`ReactiveObject` / `ReactiveCommand` / `AvaloniaScheduler.Instance` = `RxApp.MainThreadScheduler` /
  `CompositeDisposable`; NOT `RxSchedulers.MainThreadScheduler`, which is 11.4-beta+),
  **Microsoft.Extensions.DependencyInjection** (extension methods, primary constructors), **Serilog**
  (file, rolling 5 MB / 5 files), **FluentTheme** (light). Rationale for ReactiveUI: the data layer is
  Rx-native and the VMs are derived-state-heavy (sticky, cursor, active-pen) — a fit for
  `WhenAnyValue`/OAPH/`ReactiveCommand`; CommunityToolkit.Mvvm is an acceptable lower-friction
  alternative. The Core `IDataProvider` / DTO / stub layer is retained.
- **Visual language:** JetBrains / IntelliJ look is a **north-star, not MVP** — MVP uses the
  stock FluentTheme (as SemiStep does); IJ-style theming is a later, separate effort.
- **Scheduler seam:** Core keeps the bare `IScheduler` (`DefaultScheduler.Instance`) for data timing;
  the UI scheduler (`AvaloniaScheduler.Instance`) is captured in `AfterSetup` and passed explicitly to
  the coordinator — `TrendCoordinator(IDataProvider, ILogger, IScheduler dataScheduler, IScheduler uiScheduler)`,
  `Buffer` on the data scheduler, `ObserveOn` on the UI one. No second `IScheduler` container registration.
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
| Cursor / X-trace | On-hover vertical line reading each pen's value at the cursor X.        |

> Terminology fix: the user's draft used "слой графика" for a plotted curve. To avoid clashing
> with the archive-resolution "layer", a plotted curve is always a **pen**.

## Single chart — time behavior

- The **now-marker** moves and always coincides with the pen's latest measured value.
- When the live edge reaches the right edge, the view **sticks** and scrolls with real-time; the
  displayed **time-window width stays constant**.
- The operator can **pan left into the past** with a constant window width.
- **Jump-to-real-time** re-attaches sticky; now-marker at the right edge.
- Panning back is allowed **arbitrarily far, down to the first stored sample** in the DB.
- **Zoom** spans **1 second to 1 year** with no lag at the 30 FPS budget — guaranteed by
  decimation, not by the chart control (see "Decimation & performance").
- **Axis scaling:** double-click an axis = autoscale; entering min/max = fixed manual limits.
  The same actions are available from a toolbar.
- **Autoscale modes:** `auto` (fit data with padding so pens are not flush to top/bottom),
  `manual` (fixed limits), `autoscale-to-window` (fit Y to what is currently visible).
- **Logarithmic Y scale** as an axis *type* (Numeric / Logarithmic). Values ≤ 0 are sanitized
  (dropped) before log scaling.

## Multi-pen / multi-axis behavior

- Plot up to 50 pens with either a **shared** axis or **separate** scales.
- **Axis management = single active axis + per-pen autoscale:** the active pen's scale is on the
  primary axis; non-active pens scale individually with hidden axes; many pens do not spill many
  visible axes.
- A **shared common scale for a group** of pens (e.g. 16 heaters together; dampers separately)
  is supported on top of the active-axis model.
- When panning/zooming time, **all pens move together** — pens are **always time-synchronized**;
  Y scales are independent.

## Navigation & cursors

- **Scroll = zoom; mouse drag = pan.**
- **Sticky to real-time by default.** A button detaches sticky (pan into the past); clicking it
  again re-attaches and returns to real-time.
- **Panning so the live edge scrolls out of the view** auto-detaches sticky.
- **Vertical cursor (X-trace):** reads each visible pen's value at the cursor X — the standard
  multi-pen readout (Ignition Power Chart "X Trace": vertical line, interpolated value per pen).
- **Dual cursors (Δt / Δy)** — **in MVP:** two cursors measuring time/value deltas (step
  duration, ramp rate).
- ~~Horizontal cursor~~ — dropped from scope.

## Decimation & performance (architectural core)

Underwrites "no lag from 1 s to 1 year." A data-layer requirement, not a chart-control feature.

- **Never feed raw millions of points to the chart.** A 1080p-wide plot shows ~one point per
  pixel; a month of 10-second data is ~135 raw samples per pixel (MasterSCADA). The data layer
  returns roughly viewport-width points.
- **Zoom level selects the archive layer** (raw / minute / hour / day): deeper ranges use coarser
  layers — the existing layer design (data-integration.md), validated by industry (MasterSCADA
  layered archive; AVEVA "Cyclic" retrieval).
- **Use a min/max-per-pixel envelope, not plain sampling.** Plain decimation aliases away spikes
  (AVEVA warns of exactly this). Retain min AND max per pixel column so spikes survive (M4:
  min/max/first/last per column → visually lossless; MinMaxLTTB for speed; Power Chart "MinMax").
- **Decimation backend stubbed for now.** Production = PostgreSQL; unknown whether it stores
  pre-trimmed/layered data. Design the data layer so decimation can run either server-side
  (SQL aggregate) or in-process; the current stub provider synthesizes decimated series.
- **Performance budget:** 30 FPS pan/zoom lock; input data ≤ 10 Hz; ≤ 50 simultaneous pens;
  points per pen handed to the chart ≈ viewport width × 2–4.

## Data quality & line rendering

- **Gap / bad-quality rendering:** nulls and OPC quality (archive `q` column) render as visible
  gaps, not interpolated across.
- **Stepped vs interpolated lines:** configurable per pen (stepped for discrete/digital tags like
  valve on/off; interpolated for analog).

## Time handling

- Samples are UTC over the wire (data-integration.md). The time axis displays **computer local
  time**: all UTC↔OADate conversion is funneled through `Chart/LocalTimeAxis` at every render
  boundary (plotted X, axis limits, cursor/delta X), with a `DateTimeAutomatic` tick generator on
  the shared bottom axis so labels read local time. DST boundary behavior follows the machine's
  local-time conversion (cosmetic; no special-casing).

## Legend

- Grouped mini-legend: checkbox / color / name / current value (charting.md). Add **value at
  cursor** and the active pen's **scale range**.

## Out of scope / later

- **Out of scope:** horizontal cursor; alarm/event overlays from the `messages` log; annotations.
- **`[LATER]`:** save pen sets + scales + layout as named views/templates; snapshot / export /
  print; touch input (not relevant for target operator PCs).

## Renderer & UI framework

- **ScottPlot 5** (as-built): MIT; SkiaSharp; each distinct-unit pen gets its own `IYAxis`
  (`AddLeftAxis`/`AddRightAxis`), same-unit groups share one axis, non-active axes `IsVisible = false`.
  Trends drawn as `Scatter` + `FillY` band over a data-layer-decimated envelope; NaN segments the
  line at gaps (no `OnNaN` property in 5.1.57); `DataLogger` is prior art, not the pattern. Shared-X
  invariant: all pens pinned to `plot.Axes.Bottom`.
- **Avalonia 11.3.8 / net10** (as-built): hosts `ScottPlot.Avalonia` 5.1.57 (ScottPlot has no
  Avalonia 12 build). Mirrors SemiStep's patterns (ReactiveUI + MS.DI + Serilog + FluentTheme), not
  its versions. `Avalonia.HarfBuzz` omitted (no 11.3.x build); HarfBuzz arrives via `Avalonia.Skia`.
- No qualifying open-source .NET SCADA trend-viewer reference repo exists; built from library
  primitives, with ScottPlot's `DataLogger` demo as the nearest realtime pattern.

### Reuse from SemiStep (C:\Users\admin\projects\SemiStep)

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
