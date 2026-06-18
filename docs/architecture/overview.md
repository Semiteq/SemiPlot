# Overview

## Purpose

SemiPlot is a desktop trend/chart viewer for an industrial installation (semiconductor
plasma process tools — ICP / RIE / PECVD). It is used by operators and process engineers
**alongside the SCADA** (Simple-Scada 2) as a more flexible trend-analysis tool than the
SCADA's built-in trends.

It must handle two classes of data:

- **Real-time** — current tag values, continuously updated.
- **Archive (history)** — selections over a period (shift, day, month), smooth on large ranges.

## Technology stack

| Layer            | Choice                                                                 |
| ---------------- | --------------------------------------------------------------------- |
| Platform         | .NET 10 (`net10.0-windows`), Windows, C# 14                            |
| Desktop shell    | Avalonia 11.3.8 (Win32 backend, SkiaSharp render, FluentTheme light)  |
| Chart renderer   | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.57, MIT, SkiaSharp) — native control |
| MVVM             | ReactiveUI (`ReactiveUI.Avalonia` 11.3.8)                             |
| Backend (in-proc)| .NET data provider abstraction over the data sources                   |
| Realtime source  | OPC UA client to Simple-Scada's built-in UA server                     |
| History source   | Read-only SQL against the Simple-Scada archive DB                      |
| Logging          | Serilog (file, rolling 5 MB / 5 files; mirrors SemiStep conventions)  |

Constraint: **$0 budget** — only free/OSS components.

> Version note: SemiPlot pins **Avalonia 11.3.x** (binding floor 11.3.8, set by
> `ReactiveUI.Avalonia` 11.3.8) because `ScottPlot.Avalonia` 5.1.x has no Avalonia 12 build.
> `Avalonia.HarfBuzz` has no 11.3.x package on NuGet (only 12.x); HarfBuzz text shaping arrives
> transitively via `Avalonia.Skia` 11.3.8 → `HarfBuzzSharp`, so no explicit `UseHarfBuzz()` call.

## Components

```
+-------------------------------------------------------------+
|  SemiPlot.UI (Avalonia 11.3 + ScottPlot 5)                 |
|                                                             |
|   App / MainWindow (Grid: toolbar / chart / legend / status)|
|     ├── TrendChartView ──hosts──► ScottPlot AvaPlot control |
|     ├── TrendToolbarView    (layer, autoscale, sticky, …)   |
|     └── TrendLegendView     (grouped pen rows)              |
|   ViewModels (ReactiveUI) ◄── TrendCoordinator (Rx hub)     |
+----------------------────────────────────────--------------+
              │ IDataProvider (subscribe realtime, query history)
              ▼
+-------------------------------------------------------------+
|  SemiPlot.Core                                              |
|   - IDataProvider abstraction + records (Pen, envelope,     |
|     ArchiveExtent, …)                                       |
|   - renderer-agnostic models (navigation, scale, cursor, …) |
+-------------------------------------------------------------+
              │ implemented by a SemiPlot.DataSource.* project
              ▼
+-------------------------------------------------------------+
|  SemiPlot.DataSource.Stub  (current)                        |
|   - RandomStubDataProvider  (emits random data)             |
|   - MinMaxDecimator + synthetic pen/value generators        |
|  SemiPlot.DataSource.*  (future; OPC UA + SQL)              |
+-------------------------------------------------------------+
```

The UI never talks to a data source directly; it depends only on `IDataProvider`
(see [data-integration.md](./data-integration.md)). This keeps the real Simple-Scada
integration swappable behind the stub. There is **no web bridge**: the chart is a native
ScottPlot control, fed in-process by `TrendCoordinator` over `IObservable`/awaitable seams.

## Data flow

- **Realtime:** the data provider subscribes to a set of tags → `TrendCoordinator` buffers
  the samples on the data scheduler into a coalesced `RealtimeBatch` (≤ 10 Hz / 100 ms),
  crosses to the UI scheduler via `ObserveOn`, and exposes them as `IObservable<RealtimeBatch>`;
  the chart view model subscribes and appends to the per-pen plottables.
- **History:** the chart requests a window → `TrendCoordinator.QueryHistoryAsync` (the single history
  query; the initial load awaits it directly, gesture re-queries go through the debouncer) → provider
  returns one decimated `PenHistoryEnvelope` per pen (ascending `X` + `Min`/`Max`/`Center`) → the view
  model applies the result through one monotonic-sequence path (latest window wins) into the plot.

## Deployment

- Single Windows desktop app, runs on operator PCs next to the SCADA. `net10.0-windows` with
  the Avalonia Win32 backend is the deliberate Windows-only operator-PC target.
- Auto-update of the app itself via Velopack if/when needed.

## Scope status

Current focus: the **UI part** (Avalonia + ScottPlot viewer) backed by a **random data
stub**. Real OPC UA + SQL providers are deferred (see data-integration.md).
