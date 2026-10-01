# Charting / Trend Viewer

## Renderer: ScottPlot 5

The chart is rendered with **ScottPlot 5** (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — a
native Avalonia control (`AvaPlot`), no web view. It was chosen over OxyPlot for built-in
independent multi-axis; the prior uPlot/WebView2 stack is removed. The surrounding UI (legend,
navigation bar, axes UX, theming) is ours; ScottPlot is only the plotting core.

### Per-pen plottable: `EnvelopeLine`

Each pen is drawn as **one plottable of our own** over a data-layer-decimated min/max envelope
(`PenHistoryEnvelope`: ascending `Timestamps` + `Min` + `Max` + `Center`). `Chart/EnvelopeLine` is an
`IPlottable` owning a `List<EnvelopeColumn>` (`X`, `Min`, `Max`, `Center`) and strokes one polyline
through every visible column's `Min` and `Max`: no fill, no band, no markers.

`SignalXY` was rejected: it cannot express per-pen stepping plus NaN gaps, and its built-in
decimation is unused because the data layer pre-decimates. `DataLogger` is cited prior art only
(it cannot carry a pre-decimated min/max band), **not** the implementation pattern.

**Why one polyline instead of `Scatter` + `FillY`.** The pair it replaced walked every coordinate twice
per frame: `FillY` is a `Polygon` building an `SKPath` of `2 x columns + 1` vertices, and `Scatter` walked
all points again through `Drawing.DrawMarkers` even at `MarkerStyle.None`. Neither culled to the viewport,
and the prefetch margin (`HistoryPrefetch.MarginColumnFactor = 3`) makes every pen's buffer three visible
windows wide, so a frame cost three times what it drew. Measured on the bench stand on 2026-09-07 with
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

**Gaps via NaN.** A column carrying `NaN` in `Min`/`Max` is the envelope's gap marker (and what
`TrendPenState` encodes a null append as): `Build` breaks the path there and the next real column opens a
new segment with a `MoveTo`. Every NaN a chart draws comes from the history path: `RealtimeBatch` carries
`double` values, so the live edge has no null to encode.

**Per-pen stepping.** The line carries the Core `PenLineStyle` — `Stepped` for discrete/digital tags,
`Interpolated` for analog. `Chart/TrendPenState` sets it and the colour from the pen through
`EnvelopeLine.Restyle`, when it is built and on every `Revise`. Neither is readable back: tests prove the
style and the colour through rendered pixels (`RenderedRise`).
On a change-based archive the two styles draw almost the same line. Simple-Scada writes two rows per
change, the previous value at the last poll tick and the new value about 100 ms later
(`scada-archive.md#write-behavior`), so an `Interpolated` line over such rows already draws a step;
`Stepped` differs only over those 100 ms. The setting shows only on a variable archived without the
pair.

**Column buffer and its two threads.** The plottable owns the column list and the `Lock` that guards it,
both private. Every mutation goes through `ReplaceColumns`, `ClearColumns`, `AppendColumn` or
`FoldIntoLastColumn`, each of which takes the lock, so no caller can reach the list without it.
`Chart/TrendPenState` is the only caller of those four and calls them on the UI thread; `Render` and
`GetAxisLimits` read on Avalonia's render thread under the same lock. `Render` holds it for the
visible-column read alone (`EnvelopePath.VisibleRange` plus `EnvelopePath.Build`) and strokes the resulting
points outside it, so a frame that meets a history load waits for the one `AddRange` that swaps the buffer,
not for the conversion ahead of it: `LoadHistory` converts into a list local to the call and hands it over
in one `ReplaceColumns`. The colour and the line style sit under the same lock: a catalogue read can
change both after the first frame, so `Restyle` writes both on the UI thread and `Render` reads both
under the lock.

**The plot's own lists and the render thread.** ScottPlot's `Plot.Render` holds `Plot.Sync` for the
whole frame, and the plottable and axis lists it walks are ScottPlot's own, so the column lock cannot
guard them. A catalogue read adds and removes plottables and adds Y axes after the window has shown,
so `TrendChartViewModel.ApplyCatalogue` runs its plot and axis edits under `lock (Plot.Sync)`, one
block per call, and moves the live edge, queries history and asks for the redraw after it. The lock
guards the plot's lists alone: the live-edge switch cancels the old subscription's tick synchronously,
and under the lock a frame would wait for it. The block also runs under `DelayChangeNotifications()`, so `ActivePenId`, `DrawnPenId` and `ScalesRevision`
raise after the lock is released: a subscriber that closes the axis panel destroys a popup window, which waits
for the render thread, and the render thread waits for `Plot.Sync`. Without the lock a frame that
meets the edit throws "Collection was modified" on the render thread, the crash class of #59
(`TrendChartRenderThreadTests`).

**Realtime append / live-edge join.** `TrendPenState` holds one pen's `EnvelopeLine` and appends into the
plottable's column list, which every render re-reads, so appends are live and nothing is re-set. The realtime
tail appends one degenerate column (`Min == Max == Center == value`) at the live edge. At coarse layers
(minute/hour/day) a realtime sample does **not** append — it folds into the current decimation column
(`FoldRealtime` widens that column's `Min`/`Max` and moves its `Center`). Cursor and legend read the
`Center` channel consistently across the seam.

**Axes / shared-X invariant.** Each pen gets its own `IYAxis`, keyed on the pen id; no two pens ever
share one, whatever their unit or group. Every axis is a left axis — `Axes.Left` for the first pen,
`AddLeftAxis()` for the rest — because only the active pen's axis is drawn and alternating sides
would move it across the plot as the active pen changes. Non-active axes are
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
- **Aggregation layer** ("Слой": raw / minute / hour / day) — the resolution the history read
  asks for; maps to the archive `l` column (see data-integration.md). The legacy window lets the
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

Canonical use cases (acceptance fixtures): 16 dampers + 16 heat sources (dampers viewed separately,
the 16 heaters reading against the same bounds, which is the same `scale_min`/`scale_max` pair stored
on each of them rather than a shared axis) and 10 gas lines with different min..max ranges (all on
one chart, each with its own scale — §AY-2).

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
  dictionary on every catalogue applied, so the two never hold different pens.
- `Chart/EnvelopeLine` + `EnvelopePath` — the per-pen `IPlottable`, the private column buffer with the
  lock over it and the four mutators that take it, and the pure geometry it strokes: the visible-column
  search, the min/max point order, the gap break and the step shape.
- `Chart/TrendPenState` — one pen's `EnvelopeLine`, `IsVisible`, `CurrentValue`, and the
  history-load / realtime-append / fold logic that drives those mutators. `Revise(Pen)` replaces the
  pen, which raises `Pen` like `IsVisible` and `CurrentValue`, and restyles the line; `IsVisible` and the
  loaded history stay. Nothing binds to `Pen`: the hover readout reads it per pointer move and the
  sidebar reads it when it builds a row.
- `Chart/ChartAxisBinder` — applies the `PenScaleModel` output to ScottPlot Y axes (one left axis per
  pen, `SetLimitsY`, shared-X pinning).
- `Chart/ChartNavigationController` — owns the `TrendNavigationModel`, the layer ladder, the live-edge
  advance; raises `WindowChanged` (`NavigationWindow` = `[From, To]` + `Layer` +
  `RequiresHistoryRequery`). A ceiling is derived, not constant:
  `nextCoarser(layer).ToPointSpacing() × TargetColumnCount`, guarded by a 10% hysteresis band.
  `TargetColumnCount` is the canvas width in columns, clamped to 256…2048 and quantised to a power of
  two with its own 10% deadband; it selects the layer only, never the query resolution. **Single
  writer:** `TrendChartViewModel.ReportDataAreaWidth`, called from `TrendChartView`'s
  `Plot.RenderManager.RenderFinished` handler — do not call `SetTargetColumnCount` from anywhere else,
  in the same style as the navigation bar's `IsSticky`. That seam carries the `DataRect` of the frame just
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
  Every window change that asks for a re-query passes the prefetch gate below before a query leaves:
  the flag says the data may be stale, `HistoryPrefetch.Covers` says whether it is.
- `Chart/HistoryColumnTarget` — pixel width → column count (one per pixel, clamped to 256…2048; a
  non-positive width has no canvas behind it and is rejected). The unquantised value is what every
  history query asks the provider to decimate to, times the three windows the prefetch below fetches;
  `TrendChartViewModel` keeps the last reported one and stands on `MaxColumns` until the first render
  reports.
- `Chart/ChartHistoryRequestDebouncer` — the one history path, for the initial load and every gesture:
  `Throttle` (one trailing request 150 ms after the gesture goes quiet) merged with `Sample` at a
  400 ms cap interval (one request per cap while the gesture keeps moving), duplicates dropped by
  window, layer and column target → one query at a time on the data scheduler, the newest request
  that arrived while it ran running when it lands → apply, or report the failure, on the UI
  scheduler. The cap is what fills the strip a long drag exposes while it is still moving; the single
  slot is what lets a read slower than the cap complete at all. The first-snap path stays
  non-requerying.
- `Chart/HistoryPrefetch` — visible window → the window to fetch: one window width of margin on each
  side at three times the column target, so the fetched range stays at one column per pixel, with the
  left edge clamped to the archive's first sample. `TrendChartViewModel` keeps the last `FetchRange`
  and asks `Covers` before every request, so a pan that stays in the inner band, half a window width
  in from each fetched edge, issues no query, while a zoom, a layer change and a column-target change
  always issue one. Envelopes therefore span three windows, and the auto scale reads the visible one
  only, through `PenScaleModel`'s window bound.
- `Chart/ChartRealtimeApplier` — the append-vs-fold rule per layer for incoming `RealtimeBatch`es.
  It walks each `PenRealtimeValues` on that pen's own timestamps, never on the batch's union, and
  hands the union's last timestamp to `ChartNavigationController.OnLiveEdge`.
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
  from the navigation bar's delta toggle.
- `Chart/ChartAxisRegion` — Y-axis click-region hit-test (the axis panel band beside the data area).
- `Chart/AxisScalePanel` + `AxisScalePanelViewModel` — the flyout that edits the drawn pen's two bounds
  (`trend-interaction.md#the-axis-scale-panel`); `TrendChartViewModel.AxisScale` owns the view model.
- `Chart/LocalTimeAxis` — UTC↔local-OADate conversion at every render boundary.
- `Navigation/NavigationBarView` + `NavigationBarViewModel` — time navigation only: jump-to-now,
  sticky toggle, delta-mode toggle + inline Δt/Δy readout (ReactiveUI commands). Autoscale, the two
  limit boxes and set-limits left with the axis scale panel taking them over; the layer label left
  for the status bar.
- `Legend/TrendLegendView` + `TrendLegendViewModel` (+ group / row VMs and the converters) — the
  grouped sidebar. A row carries the on/off box, a round colour dot, the name, the current value in
  the pen's own mask and the unit; nothing in it is editable but the box, which an allowlist test over
  the built row template holds. A header is a section caption, not a bold name: smaller text in
  `AppSecondaryForegroundBrush`, set in capitals by `LegendConverters.ToCapitals`, a one-pixel
  `AppSubtleLineBrush` line above every header but the first (a `:nth-child(1)` style hides the first),
  and the rows under a header indented so their box starts where the caption starts. The active row
  keeps the regular weight and is marked by an `AppAccentFillBrush` background and a 3 px `AppAccentBrush`
  bar on its left edge, both following `IsActive`. A pen draws once and is listed under every group it belongs to, so one
  row view model appears under several headers and `Dispose` walks the distinct list. Every drawn header,
  the ungrouped one included, carries a switch box: `TrendLegendGroupViewModel.SwitchState` is derived
  from its rows (all on true, all off false, mixed null) and stored nowhere, the box reads it
  `Mode=OneWay`, and `SwitchGroupCommand`, its one writer, switches every row on unless all are on, then
  off. The rows route to the chart one by one, so every other header a shared pen sits under
  re-derives. `BuildGroups` keys every row by its groups, an ungrouped row by the ungrouped header, in
  one ordinal `GroupBy`, so a group named like the ungrouped header takes the ungrouped rows; headers
  sort alphabetically in the current culture, the ungrouped one last. It constructs every group view
  model once per build, because a replaced group would keep its subscriptions on the rows. `Rebuild()`,
  called after every catalogue read that changed something, builds new rows and groups from the chart's
  `Pens`, assigns `Groups`, which raises `PropertyChanged`, and only then disposes the rows and groups
  it replaced, so no binding reads a disposed row; a replaced row's visibility write reaches no chart.
  A row reads its pen's name, colour, unit, mask and visibility when it is built, so a rebuilt row shows
  the stored change and every pen keeps its visibility. `IsExpanded` is the
  panel's one state flag: collapsed, the row drops the value and the unit. The panel keeps two session
  width slots, expanded (starts at 280) and collapsed (starts at 168), and `PanelWidth` shows the slot
  of the current state, clamped to at least `PanelMinWidth` (120) and to the room that leaves the chart
  at least `ChartMinWidth` (320). `FitPanel` is the one writer of that room: `MainWindow.axaml.cs` calls
  it in `OnLoaded` and on every `SizeChanged` of the content grid, so the
  chart floor holds from the first frame, and a shrink narrows the shown width without writing a slot,
  so growing the window back restores it. The widths and `IsExpanded` are the panel's own fields, so a
  `Rebuild()` keeps them. A `Thumb` between the chart and the panel is the drag handle:
  its `DragDelta` handler calls `ResizePanel`, the only writer of the shown slot, which applies the
  same clamp. Semi ships no `Thumb` theme, so the handle carries its own template, a `Border` painting
  its `Background`; without one it draws nothing and a press never reaches it. The handle shows only
  while `IsLegendVisible` holds. Nothing persists the state or the widths —
  every start opens expanded at 280.
- `Minimap/MinimapView` + `MinimapViewModel` — Canvas-based archive-overview strip; navigates via the
  shared `ChartNavigationController` (see trend-interaction.md).
- `MainWindow/TrendWindow` — builds the window's parts once, in dependency order, and disposes them in
  reverse (`overview.md#one-window-per-process`).
- `MainWindow/MainWindow` + `MainWindowViewModel` — the six-row window grid, the flags its View
  menu writes and the menu's two axis commands under View -> Pen scale, which call `AutoscaleActivePen`
  and `RestoreInitialScale` on the chart, and the submenu header that names the drawn pen; the window's
  code-behind owns the three view-side requests (close, About dialog, settings dialog).
- `MainWindow/AppMenuBar` — the File / Edit / View / Help menu. Each checkable item reads its flag
  `Mode=OneWay` and writes it only through the command it invokes (`CLAUDE.md`, UI).
- `MainWindow/AppStatusBar` + `AppStatusBarViewModel` — current connection state and the active
  aggregation layer, named from resx. Built by `TrendWindow` over the coordinator's connection stream and
  the chart's navigation, and writes the fault and recovery entries (`data-integration.md`).
- `MainWindow/AboutDialog` + `AboutInfo` — the modal naming the product, the assembly version and the
  configuration directory this run read.
- `Settings/SettingsDialog` + `SettingsViewModel` — the modal that edits the `app/` and `connection/`
  section folders, opened from Edit -> Settings (`overview.md#the-settings-window`).
- `PenEditor/PenEditorWindow` + `PenEditorViewModel` (+ `PenFormViewModel`, `PenGroupsViewModel`, the
  row and membership VMs and `EditorCallQueue`) - the modal that edits the pen catalogue through
  `IPenCatalogueEditor`, opened from Edit -> Pens and groups (`overview.md#the-pen-and-group-editor`).
  Nothing under `Chart/` or `Legend/` references it.
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
- `Styles/Palette.axaml` + `Chart/ChartPalette` - the two theme variants, and the four ScottPlot
  surfaces painted from them (`ui-theme.md`).

**Core models (`SemiPlot.Core.Trends`, renderer-agnostic, unit-tested):**

- `PenScaleModel` — one `PenScale` per pen: `(Min, Max)` + autoscale mode + the active flag (Auto over
  the columns inside `[windowStart, windowEnd]` / Manual; log sanitize). A pen whose `scale_min`/`scale_max` are stored
  opens `Manual` on exactly those bounds; a pen without them opens `Auto`. What the operator then does
  to the axis rewrites the settings for the session and reaches no database. The only writer of
  `semiplot_tags` is the pen editor (`Edit` -> `Pens and groups`, `PenEditor/`,
  `data-integration.md#the-pen-catalogue-editor`). The stored pair is the pen's initial scale: it builds the
  settings when the pen enters the chart and is the target of `RestoreInitialScale`.
  `PenScaleSettings.InitialFor` builds the settings from the pair a pen record holds, `Manual` on both bounds
  or `Auto` when it has none; `TrendChartViewModel.RestoreInitialScale` applies them to the active pen.
  Both scale commands leave an active pen that is switched off alone, since its axis is not drawn. A changed
  pair reaches the running chart through the next catalogue read and leaves every shown pen alone, whatever
  session axis it holds (Applying a catalogue read, below).
- `PenValueFormat` — the `0.###` fallback mask, the character rule that accepts a stored mask, and the
  render under `CultureInfo.CurrentCulture`. The rule runs once, in `PostgresDataProvider.ReadPen`,
  which is where the logger is; a rejected mask reaches the record as `null`, so `Pen.Format` in the
  row is always usable and the row neither validates nor logs. The rule is a character set rather than
  a `try`/`catch` because .NET throws on almost no bad mask: `qqq` prints literally and `%0.0`
  multiplies the reading by 100.
- `TrendNavigationModel` — `[from, to]` window, sticky flag, zoom width; pan / zoom / jump-to-now /
  live-edge advance, clamped 1 s … 1 year, zoom width quantized onto a 1.25 ladder, `From ≥ FirstSample`.
- `MinMaxDecimator` — samples + target column count → min AND max per column (+ center); NaN-gap anchor
  at empty leading/trailing edge sub-spans. Shared by the coarse-layer read path of every provider:
  each translates its own rows into the parallel `(timestamp, value?)` vocabulary where a null marks
  a gap. The Postgres Raw path runs no decimator; the server reduces the rows instead.
- `MinimapGeometry` — extent + window → strip start/width fractions, and fraction → timestamp.
- `CursorReadoutModel` — cursor X → per-pen interpolated `Center` value (gaps → no value).
- `DeltaCursorModel` — two cursor times → `DeltaReadout` (Δt + Δy for the active pen).

## Applying a catalogue read

A running chart follows the stored catalogue (`overview.md#the-live-catalogue`).
`Core/Trends/PenListDelta.Between` compares two reads: `Added`, `RemovedPenIds`, `Revised` (the new record of
each pen whose stored settings differ) and `Current`, the new read. `IsEmpty` is true when nothing was
added, removed or revised, so a new order alone is no change; `ChangesPenSet` is true when a pen was
added or removed.

`TrendChartViewModel.ApplyCatalogue(catalogue)` takes the read itself, `delta.Current`, and compares it
with the pens the chart shows, not with the read before it. `ChartPenSet.Apply` computes that
comparison with `PenListDelta.Between` and applies it, so the chart ends up showing exactly the read
whatever baseline the loop compared it with (`overview.md#what-a-read-changes`). In this order, under
`lock (Plot.Sync)` (`#per-pen-plottable-envelopeline`, the plot's own lists):

1. Every pen the read does not name leaves the dictionaries, the plot and its envelope, and its axis is
   hidden. The axis stays keyed on the pen id, so a pen that comes back takes it again.
2. Every pen the chart holds whose stored settings differ is revised: `TrendPenState.Revise` takes the
   new pen and restyles its line in place. A revision never touches the pen's scale settings, so a changed
   stored pair leaves the session axis alone, and an `EnabledOnStart` revision keeps the visibility.
3. Every pen the chart lacks joins, with the visibility its `EnabledOnStart` gives it.
4. `Pens` is rebuilt in the order of the read.
5. The active pen settles: a removed active pen's slot goes to the first visible pen in catalogue order.
6. `ApplyAxisModel` once, so a read that adds 500 pens computes the axis model once. A read that
   removes every pen computes it over no pen, which clears the stored scales and moves
   `ScalesRevision` on like any other.

After the lock, the live edge follows the pens shown: when their id set differs from
`TrendCoordinator.PenIds`, the set the live edge follows (`data-integration.md#realtime`), the chart
hands the new one to `SetPens`. Compared with that set rather than with the read before, an apply that
threw before this point is caught up by the next one, and the start sequence, which applies the pens
the coordinator was built with, opens no second live subscription. Once the first history request has gone out
(`RequestInitialHistory`), a set change also drops the last fetch and issues one history query for
every pen: a pen added after the first fetch otherwise gets no history from a window already read, and
the live-edge switch leaves a hole in the pens that stay. Before it, the first request covers every pen,
so the start sequence seeds the chart through the same `ApplyCatalogue` and queries nothing twice.
`Pens` and `HasNoPens` are raised, and `RequestRedraw` follows. No production code adds or removes a pen
but a catalogue applied.

A history result that lands after a delta removed a pen carries an envelope for it, because the query
was issued before the removal. `ApplyHistory` skips every envelope whose pen the chart no longer
holds, so a removed pen's rows never outlive it and a pen that comes back reads its history afresh.

A chart that started empty takes its first pens through the same path. `PenCatalogueApplier` reads the
archive extent first and seeds `Navigation.SeedFromArchiveExtent` with it before the delta applies, as a
start with pens does. A delta that adds a pen to a chart that already had some reloads the minimap's
extent after it, and `Navigation.WidenToArchiveExtent` moves the pan floor back to an earlier first
sample that extent carries, without moving the window (`overview.md#what-a-read-changes`).

## Data contract (UI ↔ provider)

The viewer consumes data only through `IDataProvider` (see data-integration.md), in-process and
strongly typed — **no JSON message bridge**. `TrendCoordinator` is the Rx hub between provider and
view model:

- **History:** `QueryHistoryAsync(penIds, from, to, layer, targetColumnCount)` is the single history
  query, returning one `PenHistoryEnvelope` per pen (ascending `Timestamps` + `Min` + `Max` +
  `Center`; NaN = gap). The initial load and every gesture re-query go through
  `ChartHistoryRequestDebouncer`, which runs one query at a time and lets the newest window asked
  for run last.
- **Realtime:** `IObservable<RealtimeBatch>` — an ascending union timeline the live edge advances
  from, plus one `PenRealtimeValues` per pen carrying that pen's **own** timestamps and `double`
  values; buffered on the data scheduler and observed on the UI scheduler. The values are per pen
  rather than a column over the union because the archive is per-variable and change-based with a
  deadband, so one buffer window routinely spans timestamps only one pen sampled. A pen carries no
  entry at a timestamp it did not sample, and a consumer leaves it alone there. The type is `double`
  and not `double?`: `Sample` carries a non-nullable value, so the live edge has no representation
  for a break, and the gap a chart draws is the history path's reconstruction from `q = 32`. The pen
  set the stream carries follows the catalogue: `TrendCoordinator.SetPens` switches the provider
  subscription and `RealtimeBatches` stays one published stream (`data-integration.md#realtime`).
- **Archive extent:** `QueryArchiveExtentAsync()` returns an `ArchiveExtent(FirstUtc, LastUtc)` —
  the full stored time span. `TrendCoordinator.QueryArchiveExtentAsync()` is a pass-through to the
  provider (mirroring `QueryHistoryAsync`); the minimap consumes it (see trend-interaction.md).
- **Connection state:** `TrendCoordinator.ConnectionFaults` republishes the provider's own
  `IObservable<ArchiveConnectionState>` on the UI scheduler. `MainWindow/AppStatusBarViewModel` binds
  it and shows it in the status bar, over a chart that keeps its history; every fault it carries
  becomes a message-panel entry (see data-integration.md). `TrendCoordinator.Dispose` completes the
  republished stream, while the provider's own stream never terminates (`data-integration.md`).

These records are the plottables' input shape after the view model maps them onto `EnvelopeColumn`
buffers; there is no serialization step.
