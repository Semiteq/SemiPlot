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
| Desktop shell    | Avalonia 12.0.5 (Win32 backend, SkiaSharp render, HarfBuzz shaping, `Semi.Avalonia` 12.0.3 retinted to the JetBrains palette — `ui-theme.md`) |
| Chart renderer   | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — native control |
| MVVM             | ReactiveUI (`ReactiveUI.Avalonia` 12.0.3)                             |
| Backend (in-proc)| .NET data provider abstraction over the data sources                   |
| Data source      | One read-only PostgreSQL connection to the Simple-Scada archive — history, extent and realtime alike (`data-integration.md`) |
| Coarse resolutions | The SCADA's own archive layers; nothing of ours runs in or beside the database (`history-read-path-evaluation.md`) |
| Logging          | Serilog, path and level from the launch keys, rolling 5 MB / 5 files (`data-integration.md`) |
| Configuration    | YAML (YamlDotNet 18.1.0), one folder per section merged by `SemiPlot.Core/Configuration/ConfigurationSection` |

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
|   App / MainWindow (Grid: seven rows, table below)          |
|     +-- AppMenuBar          File / View / Help              |
|     +-- NavigationBarView   jump to now, sticky, delta      |
|     +-- TrendChartView ---hosts--> ScottPlot AvaPlot        |
|     +-- TrendLegendView     grouped pen rows                |
|     +-- MinimapView         archive-overview strip          |
|     +-- MessagePanelView    every failure lands here        |
|     +-- AppStatusBar        connection state, layer         |
|   ViewModels (ReactiveUI) ◄── TrendCoordinator (Rx hub)     |
+----------------------────────────────────────--------------+
              │ IDataProvider (subscribe realtime, query history)
              ▼
+-------------------------------------------------------------+
|  SemiPlot.Core                                              |
|   - IDataProvider abstraction + records (Pen, envelope,     |
|     ArchiveExtent, …)                                       |
|   - renderer-agnostic models (navigation, scale, cursor, …) |
|   - MinMaxDecimator, shared by the coarse-layer reads       |
|   - ConfigurationSection, the section-folder merge          |
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

### The window's rows

`MainWindow.axaml` is one `Grid` of seven rows, six `Auto` and the chart row taking the rest
(`RowDefinitions="Auto,Auto,*,Auto,Auto,Auto,Auto"`). Each row either collapses on a flag or is
always there.

| Row | Content | Collapses |
| --- | --- | --- |
| 0 | Menu bar (`AppMenuBar`) | no |
| 1 | Navigation bar (`NavigationBarView`) | `IsNavigationBarVisible` |
| 2 | Chart and legend | the legend column on `IsLegendVisible`; the handle column on `IsLegendVisible` and a non-null `LegendViewModel` |
| 3 | Minimap | `IsMinimapVisible` |
| 4 | Message panel (`MessagePanelView`) | `MessagePanel.IsVisible` |
| 5 | Status bar (`AppStatusBar`) | `HasStartupFailure` |
| 6 | Startup-failure panel | `HasStartupFailure` |

The `View` menu writes rows 1, 3 and 4; row 2's legend column has its own item. Each flag has one
writer, the command the menu item invokes, and the item reads back that same flag `Mode=OneWay` — a
`MenuItem` whose `IsChecked` were two-way would have the control as a second writer, and one reading
back a property the command does not write would let a click move nothing on screen.

Row 4 is the one whose flag the operator is not the only source of. `MessagePanel.IsVisible` starts
closed, so a session that never fails is never given the row, and `MessagePanelViewModel.Report`
opens it for a failure the list does not already carry — an entry that landed off screen would be a
failure nobody is shown. A repeat of the entry on top does not reopen a panel the operator closed.
The row, the `View` item's check state and the status bar's connection indicator all read the same
flag, so one click always moves the row and the check state always describes it.

The status bar carries current state only: whether the archive answers, and the layer the chart
reads, named from resx rather than from `AggregationLayer.ToString()`. It holds no pen count and no
history; the legend lists the pens and the message panel holds what happened.

### Where a failure goes

One route, and the mapper is on it. `Messages/ArchiveFailureMapper.Map` turns any `IError` into a
title, a detail, a remedy and a `MessageSeverity`, and `Messages/MessagePanelViewModel` is the one
bounded, timestamped list the operator reads them in. A repeated failure is counted against the
newest entry rather than prepended again, so an outage that reissues a history query on every pan
produces one entry with a repeat count. The startup-failure panel is separate on purpose: it renders
before configuration exists and answers a different question.

`Messages/UnhandledErrorObserver` is the last-resort route under it — what ReactiveUI raises through
its own machinery (a `ReactiveCommand`'s unobserved `ThrownExceptions`, a faulting `ToProperty`, a
binding). It reports through `AvaloniaScheduler.Instance`, because ReactiveUI raises on the scheduler
that failed and the panel edits a bound collection, and the log line travels with the report so a
failure repeating per redraw is demoted to `Debug` rather than rolling the capped log files away. A
failure raised before the window exists has no panel to reach and is logged on the spot. The report
runs as a dispatcher job, where a throw is unhandled and would end the process, so it goes through
`ResultReporting.TryReportFailure`. Its reach stops there: an
`async void` handler, a `Dispatcher.UIThread.Post` body and a throw inside a raw `Subscribe`'s
`onNext` are guarded where they occur instead.

**Nothing may construct a ReactiveUI object before `AppBuilder.Setup()`.** The observer is installed
in the builder lambda, `.UseReactiveUI(builder => builder.WithExceptionHandler(...))`, and
`RxState.DefaultExceptionHandler` initialises itself on first read — `InitializeExceptionHandler`
then no-ops. So one `ReactiveCommand`, one `ObservableAsPropertyHelper` or one read of that property
built ahead of `Setup()` turns the install into a silent no-op, with no error and no log line. This
is why `StartupSequence.Run` touches no ReactiveUI type and why `MessagePanelViewModel`, which builds
two `ReactiveCommand`s, is resolved from the container inside `.AfterSetup(...)` and never before it.
`RxApp` itself is gone from the installed ReactiveUI 23.2.28; the schedulers live on `RxSchedulers`
and the handler on `RxState`.

The same ordering forces the one service-locator lookup this tree allows. The handler is taken before
any container exists, so it cannot be given a panel by constructor injection; `App.ResolveMessagePanel`
reaches `Application.Current`'s own service provider on first use and returns null until one exists.
That is the declared exception to the constructor-injection rule in `CLAUDE.md`, and it covers this
one method: nothing else may resolve a service through a static.

## Data flow

- **Realtime:** the provider polls the raw layer for the samples written past the last one it saw →
  `TrendCoordinator` buffers them on the data scheduler into a coalesced `RealtimeBatch`
  (≤ 10 Hz / 100 ms), crosses to the UI scheduler via `ObserveOn`, and exposes them as
  `IObservable<RealtimeBatch>`; the chart view model subscribes and appends to the per-pen
  plottables. The same provider reports its own connection state, which the status bar shows and the
  message panel records.
- **History:** the chart requests a window → `TrendCoordinator.QueryHistoryAsync` (the single history
  query, reached through the debouncer by the initial load and every gesture alike) → provider
  returns one decimated `PenHistoryEnvelope` per pen (ascending `X` + `Min`/`Max`/`Center`) → the view
  model applies the result into the plot; the debouncer runs one query at a time and lets the newest
  window asked for run last.

## Deployment

- Single Windows desktop app, runs on operator PCs next to the SCADA. The projects target plain
  `net10.0`; `OutputType=WinExe` and the Avalonia Win32 backend are what make the operator PC the
  deliberate Windows-only target. The plain TFM exists so the test projects build on the Linux CI
  runners, and changes nothing about where the application ships.
- Auto-update of the app itself via Velopack if/when needed.
- Site paths follow the `C:\DISTR\` convention of the sibling SemiStep installation: configuration
  in `C:\DISTR\Config\SemiPlot`, logs in `C:\DISTR\Logs\SemiPlot\`. Neither sits beside the
  executable and neither is per-user. Nothing in the application knows those paths: the three launch
  keys carry them, and the installer is what fills them in.
- **Configuration is a tree of section folders.** A section is one folder under `--config-dir`, and
  every section folder behaves the same way: the loader reads every `*.yaml` in it, parses each file
  on its own and merges them at the key level. A key carried by two files of one folder stops the
  start and names the key and both files; a key repeated inside one file stops it naming that file.
  Files are read in `StringComparer.Ordinal` order, which decides only which file an error names
  first, because a conflict fails rather than resolves.

  | Section folder | Holds | Read by |
  | --- | --- | --- |
  | `app/` | `locale` (`ru` \| `en`) and `theme` (`light` \| `dark`), both required, no default and no fallback | `Startup/AppSettingsLoader`, first — a broken archive cannot mask a broken configuration |
  | `connection/` | The archive connection | `Startup/StartupProbe`, second — [data-integration.md](./data-integration.md) |

  `SemiPlot.Core/Configuration/ConfigurationSection.Read` is the one merge both loaders call. It
  returns the merged mapping as one YAML text, which the caller hands to its own typed deserializer
  unchanged. An absent folder and a folder holding no `*.yaml` are separate failures with separate
  remedies; every failure opens the startup window rather than escaping as an exception.

  `locale` is in [ui-text.md](./ui-text.md), `theme` in [ui-theme.md](./ui-theme.md). The set that
  ships is tracked at `ConfigFiles/` in the repository — `ConfigFiles/app/app.yaml` and
  `ConfigFiles/connection/connection.yaml` — and `SemiPlot.Tests.Unit/DeliveredConfigurationTests`
  runs the production loaders over it, so a broken delivered file fails the build.
  `connection/connection.yaml` ships with an empty `password`, which is already a named startup
  failure, so the repository carries no credential and a forgotten edit fails loudly.

### Command line

All three keys are required and none carries a default.

| Argument | Effect |
| --- | --- |
| `--config-dir <dir>` | Directory holding the `app/` and `connection/` section folders |
| `--log-file <path>` | Log file, rolling 5 MB / 5 files |
| `--logging-level <level>` | `verbose` \| `debug` \| `info` (or `information`) \| `warning` \| `error` \| `fatal`, case-insensitive |

`StartupOptions.Parse` returns `Result<StartupOptions>`. A missing key, a key the parser does not
know, a valued key given last with nothing after it, and an unusable logging level are each a
`StartupArgumentsError` naming the key. `Program.Main` parses ahead of everything else, because the
logger's own path is an argument, and reports a failure the way a configuration failure is reported:
`ArchiveFailureMapper`, the failure window, exit 1. `SemiPlot.UI` is a `WinExe`, so its standard
error stream reaches nobody and the window is the whole report; that path applies the bootstrap
locale itself, since it never enters `StartupSequence.Run`.

`LogFileTarget.Prepare` runs next, creating the folder and opening the file `--log-file` names
before the logger exists. A path that cannot be opened is a `LogFileError` taking the same route:
the failure window, exit 1. Serilog's file sink reports its own open failure only to
`Serilog.Debugging.SelfLog`, so without this step a mistyped path would start the viewer with no log
and no report.

The file therefore exists from `Prepare` onward and is empty until something writes to it. A
successful parse is followed by one Information line naming the configuration directory and the
level; at `information` or below that line is the only one a healthy run writes, and at `warning`,
`error` or `fatal` the file stays empty until the first failure.

The process exits `0` when the main window opened and closed normally, and `1` when the start failed —
the startup failure and the fatal catch alike — so a launcher can tell one from the other.

## Scope status

The application reads the real archive and nothing else: the composition root registers
`AddPostgresData`, and every member of `IDataProvider` is implemented over it — the pen catalogue,
the archive extent, the windowed history read and the live-edge poll. The chart draws history and
follows the archive as it grows. See [data-integration.md](./data-integration.md) for the contract
and `docs/plans/` for the remaining work.
