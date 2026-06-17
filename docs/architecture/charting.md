# Charting / Trend Viewer

## Renderer: ScottPlot 5

The chart is rendered with **ScottPlot 5** (`ScottPlot.Avalonia` 5.1.57, MIT, SkiaSharp) — a
native Avalonia control (`AvaPlot`), no web view. It was chosen over OxyPlot for built-in
independent multi-axis; the prior uPlot/WebView2 stack is removed. The surrounding UI (legend,
toolbar, axes UX, theming) is ours; ScottPlot is only the plotting core.

### Per-pen plottables: `Scatter` (center) + `FillY` (band)

Each pen is drawn as **two plottables over a data-layer-decimated min/max envelope**
(`PenHistoryEnvelope`: ascending `Timestamps` + `Min` + `Max` + `Center`):

- a **`Scatter`** center line over the envelope's `Center` channel, and
- a **`FillY`** min/max band (`X`, `Top = Max`, `Bottom = Min`) carrying the decimation envelope.

`SignalXY` was rejected: it cannot express per-pen stepping plus NaN gaps, and its built-in
decimation is unused because the data layer pre-decimates. `DataLogger` is cited prior art only
(it cannot carry a pre-decimated min/max band), **not** the implementation pattern.

**Gaps via NaN, not an enum.** ScottPlot 5.1.57 `Scatter` has **no `OnNaN`/`Gap` property** (an
earlier plan assumption). Gap segmentation is automatic: the default path strategy
(`ScottPlot.PathStrategies.Straight`) skips `float.IsNaN` points and breaks the path, so feeding
`double.NaN` (the envelope's gap marker, also used for `null` realtime samples) produces the gap
in the center line and the band at the same X.

**Per-pen stepping.** `Scatter.ConnectStyle` carries the per-pen line style — `StepHorizontal`
for stepped (discrete/digital tags), `Straight` for interpolated (analog). Mapped from the Core
`PenLineStyle` enum by `Chart/PenLineStyleMap`.

**Realtime append / live-edge join.** `TrendPenState` owns one pen's `Scatter` + `FillY` plus the
backing buffers. The center `Scatter` wraps a `List<Coordinates>` by reference so appends are live;
the `FillY` snapshots, so it is re-set (`SetDataSource`) after each append. The realtime tail
appends one point to the center line with the band degenerate (`Min == Max == value`) at the live
edge. At coarse layers (minute/hour/day) a realtime sample does **not** draw a raw point — it folds
into the current decimation column (`FoldRealtime` widens that column's Min/Max band and moves its
center). Cursor and legend read the `Center` channel consistently across the seam.

**Axes / shared-X invariant.** Each distinct-unit pen gets its own `IYAxis`
(`AddLeftAxis`/`AddRightAxis`); a same-unit group shares one `IYAxis`; non-active axes are
`IsVisible = false`, and the active-pen switch toggles visibility without rebuilding. Scaling is
driven per-axis via `SetLimitsY(min, max, axis)` from the Core `PenScaleModel` output (no global
`AutoScale`). Every plottable is pinned to `plot.Axes.Bottom` explicitly at creation, so all pens
share one X axis and per-pen axes are Y-only. Redraws are coalesced to 30 FPS via a
`Sample(33 ms)` redraw seam driving `AvaPlot.Refresh()`.

## Reference: legacy SCADA trend window

The legacy Simple-Scada trend window ("Параметры (Графики)") is the functional reference —
SemiPlot must match its capabilities and improve usability.

The capture itself is not kept here: the only one available is from a live installation and
discloses the customer's tag list. The elements below are what SemiPlot reproduces.

Elements to reproduce and improve:

- **Left Y axis = scale of the selected pen** (legacy shows the active pen's scale). SemiPlot
  must additionally support several independent scales **simultaneously**, not only the
  single selected pen's scale.
- **Mini-legend** with columns: checkbox / color / name / current value. Pens are logically
  grouped (ICP, RIE, pressures, gases, temperatures).
- **Aggregation layer selector** ("Слой": minute / second / hour / day) — switches the
  resolution of historical data; maps to the archive `l` column (see data-integration.md).
- **Cursor** with value readout at a point (timestamp + every pen's value).
- **Time navigation:** zoom/pan, jump to start/end, range selection.
- Toolbar (snapshot, save, print, refresh, favorite, help) and tabs (Trends / Values /
  Legend / Settings).

## Required charting features

1. **Pens (series)**
   - Add/remove pens at runtime; toggle individual pen visibility without rebuilding the chart.
   - Each pen has identity (tag id + name), color, group, and current value.

2. **Y axes / scaling**
   - **Per-pen independent min/max** (multiple Y axes on one chart). Example: 10 gas lines with
     different ranges shown together, each scaled individually.
   - **Shared common scale** for a group of pens (e.g. all 16 heater sources in one scale).
   - Switching the active pen surfaces its scale on the primary axis (legacy behavior), on top
     of the simultaneous multi-axis capability.

3. **Cursor / inspection**
   - Vertical cursor / crosshair reading the value of **every** visible pen at the cursor X.

4. **History performance**
   - Smooth zoom/pan/scroll over long archives without lag. Resolution is controlled by
     aggregation layers (raw / minute / hour / day); deeper ranges use coarser layers.

5. **Grouping / layout**
   - View pen groups separately (e.g. dampers separately from heaters), or together.

## Canonical use cases

- 16 dampers + 16 heat sources: view all 16 heaters together on one shared scale, separately
  view all dampers.
- 10 gas lines with different min..max ranges: all visible on one chart, each with its own scale.

## Module layout (Avalonia views / view models / Core models)

There is no JavaScript and no web bridge. The viewer is Avalonia views with ReactiveUI view
models, backed by renderer-agnostic models in `SemiPlot.Core`. Responsibilities:

**Views + view models (`SemiPlot.UI`):**

- `Chart/TrendChartView` + `TrendChartViewModel` — the chart. The view is the only type touching
  `AvaPlot`; the view model owns a bare `ScottPlot.Plot` (headless-constructable), the per-pen
  `TrendPenState` dictionary, and the coordinator subscriptions — so it is unit-tested headless.
- `Chart/TrendPenState` — one pen's `Scatter` + `FillY`, its backing buffers, `IsVisible`,
  `CurrentValue`, and the history-load / realtime-append / fold logic.
- `Chart/ChartAxisBinder` — applies the `PenScaleModel` output to ScottPlot Y axes
  (`AddLeftAxis`/`AddRightAxis`, shared-group axis assignment, `SetLimitsY`, shared-X pinning).
- `Chart/ChartNavigationController` — owns the `TrendNavigationModel`, the layer-by-zoom mapping,
  the live-edge advance; raises `WindowChanged` (`NavigationWindow` = `[From, To]` + `Layer`).
- `Chart/ChartRealtimeApplier` — the append-vs-fold rule per layer for incoming `RealtimeBatch`es.
- `Chart/ChartCursorReader` / `ChartDeltaCursorReader` — view-side state wrapping the Core cursor
  models, resolving the visible / active pens.
- `Chart/LocalTimeAxis` + `PenLineStyleMap` — UTC↔local-OADate conversion at every render boundary;
  `PenLineStyle` → `Scatter.ConnectStyle`.
- `Toolbar/TrendToolbarView` + `TrendToolbarViewModel` — autoscale, set-limits, layer selector,
  jump-to-now, sticky toggle, Δ-cursor toggle (ReactiveUI commands).
- `Legend/TrendLegendView` + `TrendLegendViewModel` (+ group / row VMs and two converters) — the
  grouped mini-legend: checkbox visibility, color, name, current value, value-at-cursor, scale range.

**Core models (`SemiPlot.Core.Trends`, renderer-agnostic, unit-tested):**

- `PenScaleModel` — per-axis `(Min, Max)` + autoscale mode + visibility + axis key (active pen on
  the primary axis; per-pen or shared-group scaling; Auto / Manual / AutoscaleToWindow; log sanitize).
- `TrendNavigationModel` — `[from, to]` window, sticky flag, zoom width; pan / zoom / jump-to-now /
  live-edge advance, clamped 1 s … 1 year.
- `MinMaxDecimator` — samples + target column count → min AND max per column (+ center).
- `CursorReadoutModel` — cursor X → per-pen interpolated `Center` value (gaps → no value).
- `DeltaCursorModel` — two cursor times → `DeltaReadout` (Δt + Δy for the active pen).

## Data contract (UI ↔ provider)

The viewer consumes data only through `IDataProvider` (see data-integration.md), in-process and
strongly typed — **no JSON message bridge**. `TrendCoordinator` is the Rx hub between provider and
view model:

- **History:** `QueryHistoryAsync(penIds, from, to, layer, targetColumnCount)` returns one
  `PenHistoryEnvelope` per pen (ascending `Timestamps` + `Min` + `Max` + `Center`; NaN = gap);
  `RequestHistory(...)` + `SetLayer(...)` re-query through the decimation seam and surface results
  via `IObservable<TrendHistory>` (layer + envelopes).
- **Realtime:** `IObservable<RealtimeBatch>` — a union timeline plus per-pen `double?[]` (`null` =
  gap), buffered on the data scheduler and observed on the UI scheduler.

These records are ScottPlot's input shape after the view model maps them onto `Coordinates` /
`FillY` data sources; there is no serialization step.
