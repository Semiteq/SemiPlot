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
`Sample(33 ms)` redraw seam driving `AvaPlot.Refresh()`. Only data/window/visibility/gesture/
delta-toggle changes drive ScottPlot redraws (through that throttled seam); hover and pointer-exit do
**not** call `AvaPlot.Refresh()`. A pointer-move updates only the cheap Avalonia cursor overlay, which
is repositioned from the `RedrawRequested` seam (after `Refresh()`) and on `SizeChanged`. Keeping the
re-rasterization off the per-pointer-event path is the point of the overlay.

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
- **Aggregation layer selector** ("Слой": raw / minute / hour / day) — switches the resolution
  of historical data; maps to the archive `l` column (see data-integration.md). Behavior in
  trend-feature-spec.md §DA-2, §DA-4.
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
- **Y axes / scaling** — per-pen independent min/max, multiple Y axes, shared group scale,
  active-pen scale on the primary axis: trend-feature-spec.md §AY-1, §AY-2.
- **Cursor / inspection** — vertical cursor reading every visible pen at the cursor X:
  trend-feature-spec.md §CU-1, §CU-2.
- **History performance** — smooth zoom/pan over long archives via aggregation layers and
  decimation: trend-feature-spec.md §DA-2, §DA-3, §DA-5.
- **Grouping / layout** — view pen groups separately or together: trend-feature-spec.md §MS-2.

Canonical use cases (acceptance fixtures): 16 dampers + 16 heat sources (all 16 heaters together
on one shared scale, dampers viewed separately) and 10 gas lines with different min..max ranges
(all on one chart, each with its own scale — §AY-2).

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
- `Chart/ChartNavigationController` — owns the `TrendNavigationModel`, the layer ladder, the live-edge
  advance; raises `WindowChanged` (`NavigationWindow` = `[From, To]` + `Layer` +
  `RequiresHistoryRequery` + `IsColumnCountChange`). A ceiling is derived, not constant:
  `nextCoarser(layer).ToPointSpacing() × TargetColumnCount`, guarded by a 10% hysteresis band.
  `TargetColumnCount` is the canvas width in columns, clamped to 256…2048 and quantised to a power of
  two with its own 10% deadband; it selects the layer only, never the query resolution. **Single
  writer:** `TrendChartViewModel.ReportDataAreaWidth`, called from `TrendChartView`'s
  `Plot.RenderManager.RenderFinished` handler — do not call `SetTargetColumnCount` from anywhere else,
  in the same style as the toolbar's `IsSticky`. That seam carries the `DataRect` of the frame just
  rasterised; `Plot.LastRender` read after `Refresh()` would still describe the previous frame, so a
  resize could leave the layer computed for the old canvas with nothing scheduled to correct it.
  `RenderFinished` fires on Avalonia's render thread, so the report is posted to the UI thread, and only
  a changed width is posted.
  A changed *quantised* count re-queries the window even when the layer survives, because it also
  invalidates the decimation width the visible data was fetched at. The re-query keys on the quantised
  count while the query resolution follows the unquantised width, so a resize inside the deadband
  (with 1024 in force: any width from 659 to 1592 px) changes the requested resolution without
  re-querying. The drawn resolution then lags the canvas by up to one deadband span, `2 × 1.1² ≈ 2.42`,
  until the next navigation gesture re-queries at the current width.
- `Chart/HistoryColumnTarget` — pixel width → column count (one per pixel, clamped to 256…2048; a
  non-positive width has no canvas behind it and is rejected). The unquantised value is what every
  history query asks the provider to decimate to; `TrendChartViewModel` keeps the last reported one
  and stands on `MaxColumns` until the first render reports.
- `Chart/ChartHistoryRequestDebouncer` — the single chokepoint for gesture-driven history re-queries:
  `Throttle` (one trailing request after the gesture goes quiet) → query on the data scheduler →
  `Switch` (latest-wins, drops stale in-flight responses) → apply on the UI scheduler. The startup
  initial load bypasses it; the first-snap path stays non-requerying.
- `Chart/ChartRealtimeApplier` — the append-vs-fold rule per layer for incoming `RealtimeBatch`es.
- `Chart/ChartCursorReader` / `ChartDeltaCursorReader` — view-side state wrapping the Core cursor
  models, resolving the visible / active pens (`ChartDeltaCursorReader.FormatReadout` formats Δt/Δy).
- `Chart/ChartHoverReadout` — pure static `BuildContent`: builds the readout string (local timestamp +
  every visible pen's value at the cursor X; gap or missing pen → dash) that feeds the Avalonia overlay
  `TextBlock`. No plottable; unit-tested as plain `[Fact]`.
- `Chart/ChartCursorOverlay` — pure projection (no Avalonia/`AvaPlot` deps): cursor pixel X + `DataRect`
  + render scale → crosshair endpoints + readout anchor in DIP space, clamped to the data rect; unit-tested.
  The view (`TrendChartView`) renders the result onto a transparent overlay `Canvas` (crosshair `Line` +
  readout `Border`), suppressed during drag / delta mode.
- `Chart/LeftButtonTool` (enum `Pan | DeltaPlacement`) — the single left-button gesture state, sourced
  from the toolbar delta toggle.
- `Chart/ChartAxisRegion` + `ChartAxisEdit` — Y-axis click-region hit-test (panel band, upper/lower
  split, pixel→value with Y inversion) and the seed-untouched-bound helper for inline range edits.
- `Chart/LocalTimeAxis` + `PenLineStyleMap` — UTC↔local-OADate conversion at every render boundary;
  `PenLineStyle` → `Scatter.ConnectStyle`.
- `Toolbar/TrendToolbarView` + `TrendToolbarViewModel` — autoscale, set-limits, layer selector,
  jump-to-now, sticky toggle, delta-mode toggle + inline Δt/Δy readout (ReactiveUI commands).
- `Legend/TrendLegendView` + `TrendLegendViewModel` (+ group / row VMs and two converters) — the
  grouped mini-legend: checkbox visibility, color, name, current value, value-at-cursor, scale range.
- `Minimap/MinimapView` + `MinimapViewModel` — Canvas-based archive-overview strip; navigates via the
  shared `ChartNavigationController` (see trend-interaction.md).

**Core models (`SemiPlot.Core.Trends`, renderer-agnostic, unit-tested):**

- `PenScaleModel` — per-axis `(Min, Max)` + autoscale mode + visibility + axis key (active pen on
  the primary axis; per-pen or shared-group scaling; Auto / Manual / AutoscaleToWindow; log sanitize).
- `TrendNavigationModel` — `[from, to]` window, sticky flag, zoom width; pan / zoom / jump-to-now /
  live-edge advance, clamped 1 s … 1 year, zoom width quantized onto a 1.25 ladder, `From ≥ FirstSample`.
- `MinMaxDecimator` — samples + target column count → min AND max per column (+ center); NaN-gap anchor
  at empty leading/trailing edge sub-spans. **Lives in `SemiPlot.DataSource.Stub`** (stub-only caller).
- `MinimapGeometry` — extent + window → strip start/width fractions, and fraction → timestamp.
- `CursorReadoutModel` — cursor X → per-pen interpolated `Center` value (gaps → no value).
- `DeltaCursorModel` — two cursor times → `DeltaReadout` (Δt + Δy for the active pen).

## Data contract (UI ↔ provider)

The viewer consumes data only through `IDataProvider` (see data-integration.md), in-process and
strongly typed — **no JSON message bridge**. `TrendCoordinator` is the Rx hub between provider and
view model:

- **History:** `QueryHistoryAsync(penIds, from, to, layer, targetColumnCount)` is the single history
  query, returning one `PenHistoryEnvelope` per pen (ascending `Timestamps` + `Min` + `Max` +
  `Center`; NaN = gap). The view model awaits it directly for the initial load and routes gesture
  re-queries through `ChartHistoryRequestDebouncer`; both apply via one monotonic-sequence path so the
  latest window wins.
- **Realtime:** `IObservable<RealtimeBatch>` — a union timeline plus per-pen `double?[]` (`null` =
  gap), buffered on the data scheduler and observed on the UI scheduler.
- **Archive extent:** `QueryArchiveExtentAsync()` returns an `ArchiveExtent(FirstUtc, LastUtc)` —
  the full stored time span. `TrendCoordinator.QueryArchiveExtentAsync()` is a pass-through to the
  provider (mirroring `QueryHistoryAsync`); the minimap consumes it (see trend-interaction.md).

These records are ScottPlot's input shape after the view model maps them onto `Coordinates` /
`FillY` data sources; there is no serialization step.
