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
| Platform         | .NET 10 (`net10.0`), ships on Windows, C# 14                           |
| Desktop shell    | Avalonia 12.0.5 (Win32 backend, SkiaSharp render, HarfBuzz shaping, FluentTheme light) |
| Chart renderer   | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — native control |
| MVVM             | ReactiveUI (`ReactiveUI.Avalonia` 12.0.3)                             |
| Backend (in-proc)| .NET data provider abstraction over the data sources                   |
| Data source      | One read-only PostgreSQL connection to the Simple-Scada archive — history, extent and realtime alike (`data-integration.md`) |
| Coarse resolutions | The SCADA's own archive layers; nothing of ours runs in or beside the database (`history-read-path-evaluation.md`) |
| Logging          | Serilog (`C:\DISTR\Logs\SemiPlot\`, rolling 5 MB / 5 files, `Warning` by default) |

Constraint: **$0 budget** — only free/OSS components.

> Version note: SemiPlot pins **Avalonia 12.0.5** with `ScottPlot.Avalonia` 5.1.59 (which depends on
> Avalonia 12.0.0) and `ReactiveUI.Avalonia` 12.0.3 — the pairing the sibling repository `SemiStep`
> already ships. Both test projects sit on `xunit.v3` 3.2.2 and both target plain
> `net10.0`; what keeps them separate is the dependency graph, not the target framework
> (`testing-strategy.md`).
> `SemiPlot.UI` references `Avalonia.HarfBuzz` 12.0.5 and `App.BuildAvaloniaApp` calls `UseHarfBuzz()`
> between `UseSkia()` and `UseReactiveUI()`. The chain names the platform itself
> (`UseWin32().UseSkia()`) rather than calling `UsePlatformDetect()`, and Skia brings no text shaper, so
> without that call `AppBuilder.Setup` throws "No text shaping system configured" before any window
> exists. The headless platform registers a shaper of its own, which is why no headless test reaches
> that path; `SemiPlot.Tests.Unit/UI/Startup/AppBuilderCompositionTests` reads the composed builder back and
> pins all three subsystems instead.

## Components

```
+-------------------------------------------------------------+
|  SemiPlot.UI (Avalonia 12.0 + ScottPlot 5)                 |
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
|   - MinMaxDecimator, shared by every provider               |
+-------------------------------------------------------------+
              │ implemented by a SemiPlot.DataSource.* project
              ▼
+-------------------------------------------------------------+
|  SemiPlot.DataSource.Postgres  (the only one, read-only)    |
|   - PostgresDataProvider over the Simple-Scada archive      |
|   - history, extent, catalogue and the live-edge poll       |
+-------------------------------------------------------------+
```

The UI never talks to a data source directly; it depends only on `IDataProvider`
(see [data-integration.md](./data-integration.md)). The composition root resolves the PostgreSQL
provider, which is the only one the application ships; an archive that does not answer shows the
startup failure in the main window rather than falling back to invented data. There is **no web
bridge**: the chart is a native ScottPlot control, fed in-process by `TrendCoordinator` over
`IObservable`/awaitable seams.

## Data flow

- **Realtime:** the provider polls the raw layer for the samples written past the last one it saw →
  `TrendCoordinator` buffers them on the data scheduler into a coalesced `RealtimeBatch`
  (≤ 10 Hz / 100 ms), crosses to the UI scheduler via `ObserveOn`, and exposes them as
  `IObservable<RealtimeBatch>`; the chart view model subscribes and appends to the per-pen
  plottables. The same provider reports its own connection state, which the main window draws as a
  banner row over the chart.
- **History:** the chart requests a window → `TrendCoordinator.QueryHistoryAsync` (the single history
  query, reached through the debouncer by the initial load and every gesture alike) → provider
  returns one decimated `PenHistoryEnvelope` per pen (ascending `X` + `Min`/`Max`/`Center`) → the view
  model applies the result into the plot; `Switch` in the debouncer is what makes the latest window win.

## Deployment

- Single Windows desktop app, runs on operator PCs next to the SCADA. The projects target plain
  `net10.0`; `OutputType=WinExe` and the Avalonia Win32 backend are what make the operator PC the
  deliberate Windows-only target. The plain TFM exists so the test projects build on the Linux CI
  runners, and changes nothing about where the application ships.
- Auto-update of the app itself via Velopack if/when needed.
- Site paths follow the `C:\DISTR\` convention of the sibling SemiStep installation: configuration
  in `C:\DISTR\Config\SemiPlot`, logs in `C:\DISTR\Logs\SemiPlot\`. Neither sits beside the
  executable and neither is per-user.
- The connection file `C:\DISTR\Config\SemiPlot\archive-connection.yaml` is required. An
  installation without one shows the startup failure in the main window instead of a chart — the
  startup path is in [data-integration.md](./data-integration.md).

### Command line

| Argument | Effect | Default |
| --- | --- | --- |
| `--config-dir <dir>` | Directory holding `archive-connection.yaml` | `C:\DISTR\Config\SemiPlot` |
| `--log-file <path>` | Log file, rolling 5 MB / 5 files | `C:\DISTR\Logs\SemiPlot\semiplot.log` |
| `--logging-level <level>` | `verbose` \| `debug` \| `info` (or `information`) \| `warning` \| `error` \| `fatal`, case-insensitive | `warning` |

An argument the table does not name is ignored, and so is a valued argument given last with nothing
after it; the default stands in both cases. An unrecognised logging level reads as `warning` and says
so on the standard error stream — parsing runs before the logger exists, so it has no other route. A log
directory that cannot be created disables file logging and leaves the console sink, rather than
failing the start; that is the file sink's own fallback.

The process exits `0` when the main window opened and closed normally, and `1` when the start failed —
the startup failure and the fatal catch alike — so a launcher can tell one from the other.

## Scope status

The application reads the real archive and nothing else: the composition root registers
`AddPostgresData`, and every member of `IDataProvider` is implemented over it — the pen catalogue,
the archive extent, the windowed history read and the live-edge poll. The chart draws history and
follows the archive as it grows. See [data-integration.md](./data-integration.md) for the contract
and `docs/plans/` for the remaining work.
