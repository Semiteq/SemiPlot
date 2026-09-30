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
| Desktop shell    | Avalonia 12.0.5 (Win32 backend, SkiaSharp render, HarfBuzz shaping, `Semi.Avalonia` 12.0.3 retinted to the JetBrains palette — `ui-theme.md`); the pen editor's picker from `Avalonia.Controls.ColorPicker` 12.0.5 with `Semi.Avalonia.ColorPicker` 12.0.3 |
| Chart renderer   | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — native control |
| MVVM             | ReactiveUI (`ReactiveUI.Avalonia` 12.0.3)                             |
| Backend (in-proc)| .NET data provider abstraction over the data sources                   |
| Data source      | One PostgreSQL role on the Simple-Scada archive: history, extent and realtime read it, and the pen editor alone writes the catalogue tables (`data-integration.md`) |
| Coarse resolutions | The SCADA's own archive layers; nothing of ours runs in or beside the database (`history-read-path-evaluation.md`) |
| Logging          | Serilog, path and level from the launch keys, rolling 5 MB / 5 files (`data-integration.md`) |
| Configuration    | YAML (YamlDotNet 18.1.0), one folder per section merged by `SemiPlot.Core/Configuration/ConfigurationSection` |

Constraint: **$0 budget** — only free/OSS components.

> Version note: SemiPlot pins **Avalonia 12.0.5** with `ScottPlot.Avalonia` 5.1.59 (which depends on
> Avalonia 12.0.0) and `ReactiveUI.Avalonia` 12.0.3 — the pairing the sibling repository `SemiStep`
> already ships. `Avalonia.Controls.ColorPicker` is part of Avalonia and moves in lock-step with it. Both test projects sit on `xunit.v3` 3.2.2 and both target plain
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
|   App / MainWindow (Grid: six rows, table below)            |
|     built and owned by TrendWindow                          |
|     +-- AppMenuBar          File / Edit / View / Help       |
|     +-- NavigationBarView   jump to now, sticky, delta      |
|     +-- TrendChartView ---hosts--> ScottPlot AvaPlot        |
|     +-- TrendLegendView     grouped pen rows                |
|     +-- MinimapView         archive-overview strip          |
|     +-- MessagePanelView    every failure lands here        |
|     +-- AppStatusBar        connection state, layer         |
|     +-- PenEditorWindow     pens and groups dialog          |
|   ViewModels (ReactiveUI) ◄── TrendCoordinator (Rx hub)     |
|                           ◄── PenCatalogueSync (5 s reads)  |
|   StartupFailureWindow (+ view model): shown instead of     |
|     MainWindow when the start fails                         |
+----------------------────────────────────────--------------+
              │ IDataProvider (subscribe realtime, query history,
              │                re-read the catalogue)
              │ IPenCatalogueEditor (the pen editor's writes)
              ▼
+-------------------------------------------------------------+
|  SemiPlot.Core                                              |
|   - IDataProvider abstraction + records (Pen, envelope,     |
|     ArchiveExtent, …)                                       |
|   - IPenCatalogueEditor + records, the catalogue writes     |
|   - renderer-agnostic models (navigation, scale, cursor, …) |
|   - MinMaxDecimator, shared by the coarse-layer reads       |
|   - ConfigurationSection, the section-folder merge          |
+-------------------------------------------------------------+
              │ implemented by a SemiPlot.DataSource.* project
              ▼
+-------------------------------------------------------------+
|  SemiPlot.DataSource.Postgres  (the only one)               |
|   - PostgresDataProvider over the Simple-Scada archive      |
|   - history, extent, catalogue and the live-edge poll       |
|   - PostgresPenCatalogueEditor, the catalogue writes        |
+-------------------------------------------------------------+
```

The UI never talks to a data source directly. It reads through `IDataProvider`, and the pen editor
alone writes, through `IPenCatalogueEditor` (see [data-integration.md](./data-integration.md)). The composition root resolves the PostgreSQL
provider, which is the only one the application ships; an archive that does not answer shows the
startup failure in its own window rather than falling back to invented data. There is **no web
bridge**: the chart is a native ScottPlot control, fed in-process by `TrendCoordinator` over
`IObservable`/awaitable seams.

### One window per process

SemiPlot runs one trend window per process. An operator opens several windows, each its own process, on
one machine. The windows share the configuration directory and the one PostgreSQL archive, whose
connection never changes while a process runs.

| Shared by every window | Kept by one window |
| --- | --- |
| The pen catalogue: name, colour, style, unit, mask, groups, initial scale (the 5 s catalogue loop) | The group shown, the time window, the live-edge mode |
| The theme | The current scale of each pen, and its visibility |
| The language, read at each window's start | The message panel and its entries |

`MainWindow/TrendWindow` is the window's composition and its owner. `TrendWindow.Build` takes the start
sequence's `StartupData`, the configuration directory and the UI scheduler, and constructs in order:

1. the coordinator;
2. the chart, seeded from the start sequence's pens and extent, whose constructor opens the live edge;
3. the status bar, over the coordinator's connection stream and the chart's navigation;
4. the minimap;
5. the catalogue sync;
6. the navigation bar and the legend;
7. the catalogue applier;
8. `MainWindowViewModel`, over the chart, the status bar, the minimap, the navigation bar, the legend
   and the sync's `ReadNow`.

The coordinator republishes the provider's connection stream through `ObserveOn` on the UI scheduler.
`AvaloniaScheduler` runs a zero-delay action at once when the caller is already on the dispatcher thread,
so `ObserveOn` alone defers nothing for a state emitted on the UI thread. The bar is bound in time for the
first state because the provider emits off the UI thread; the tests' `TestScheduler` defers it.

`Build` then starts the sync, requests the initial history and starts the minimap's extent load. A part
is built once and never replaced, so every part of the window view model is non-null.
`TrendWindow.Dispose` disposes the same objects in reverse order, the coordinator last, and
`MainWindowViewModel` disposes only its own commands and request subjects. A throw out of `Build` ends
the start and the process exits; nothing built before the throw is disposed. `Build` resolves every
container service first, so a missing registration throws before any part exists. `App` builds one
`TrendWindow`, shows its view model, and disposes the `TrendWindow` from the main window's `Closed` event
on the UI thread.

The container keeps the process services: the data source, the provider, the editor, the message panel
and the logger factory. It registers no window part, the status bar included. The UI scheduler is not a
container registration; `App` passes `AvaloniaScheduler.Instance` to `TrendWindow.Build`, while the data
scheduler comes from the container.

A failed start opens `Startup/StartupFailureWindow` instead, over its own `StartupFailureViewModel`: the
failure's title, detail and remedy, a message panel of its own, and the Settings, About and Exit
buttons. The view model builds and disposes that panel; the window disposes the view model when it
closes. Settings is present, and its command executable, when a configuration directory is known.
`App` stores the panel of the window the process shows, the container's in `Configure` on a start and
the failure window's in `CreateMainWindow` on a failed start, and `App.ResolveMessagePanel` returns that
field, null before either has run and again after the failure window closes. `UnhandledErrorObserver`
reaches either window's panel through it. The window has no chart, legend or minimap, and no service
provider.

### The window's rows

`MainWindow.axaml` is one `Grid` of six rows, five `Auto` and the chart row taking the rest
(`RowDefinitions="Auto,Auto,*,Auto,Auto,Auto"`). Each row either collapses on a flag or is
always there.

| Row | Content | Collapses |
| --- | --- | --- |
| 0 | Menu bar (`AppMenuBar`) | no |
| 1 | Navigation bar (`NavigationBarView`) | `IsNavigationBarVisible` |
| 2 | Chart and legend | the legend column and the handle column on `IsLegendVisible` |
| 3 | Minimap | `IsMinimapVisible` |
| 4 | Message panel (`MessagePanelView`) | `MessagePanel.IsVisible` |
| 5 | Status bar (`AppStatusBar`) | no |

The `View` menu writes rows 1, 3 and 4; row 2's legend column has its own item. One submenu, Pen scale, holds
two plain commands, Autoscale and Restore initial scale. They act on the active pen's axis and write no flag.
Each flag has one writer, the command the menu item invokes, and the item reads back that same flag
`Mode=OneWay` — a `MenuItem` whose `IsChecked` were two-way would have the control as a second writer, and one
reading back a property the command does not write would let a click move nothing on screen.

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
produces one entry with a repeat count. The startup-failure text is separate on purpose: it renders
before configuration exists, in a window of its own, and answers a different question.

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
two `ReactiveCommand`s, is resolved from the container inside `.AfterSetup(...)` and never before it. On
a failed start no container exists, and `StartupFailureViewModel` builds its panel when
`App.CreateMainWindow` runs, after `Setup()`.
`RxApp` itself is gone from the installed ReactiveUI 23.2.28; the schedulers live on `RxSchedulers`
and the handler on `RxState`.

The same ordering forces the one static read this tree allows. The handler is taken before any
window exists, so it cannot be given a panel by constructor injection; `App.ResolveMessagePanel`
reads that field of `Application.Current`.
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

- Single Windows desktop app that runs on the SCADA machine itself, beside the SCADA and its archive
  (`[DEC:machine-time-zone]` in `sources.md`). The projects target plain `net10.0`;
  `OutputType=WinExe` and the Avalonia Win32 backend are what make that machine the deliberate
  Windows-only target. The plain TFM exists so the test projects build on the Linux CI
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

### The settings window

`Edit` -> `Settings` opens `Settings/SettingsDialog`, the viewer's only writer of the section folders.
It edits `locale` and `theme` in `app/`, and `host`, `port`, `database`, `user`, `password` and
`poll_interval_ms` in `connection/`. A key the window does not show, such as `schema`, stays in its file
and survives every rewrite. Nothing applies live: the
dialog shows a restart notice after a save, and the change takes effect at the next start.

Each connection field takes only what its loader accepts. `host` is a text box checked by
`PostgresConnectionLoader.IsIPv4Address`, the loader's own rule. `port` and `poll_interval_ms` are
`NumericUpDown` controls held to whole numbers, `port` from `LowestPort` to `HighestPort` and the poll
interval from `LowestPollIntervalMs`, the loader's constants. `database`, `user` and `password` must not be
blank. A field that breaks its rule takes the error border, the message line names the first such
field, and the save stays disabled (`ui-theme.md#a-form-never-resizes-on-validation`). A file value the
rule refuses opens as an invalid field: a host name stays as text in a field with the error border, and
a port or poll interval that is not a whole number in range opens empty.

The window reads the files, not the typed settings. `SettingsSave.ReadOwned` runs
`ConfigurationSection.ReadOwned` over both section folders, which returns each section's scalar values
as text and the file that owns each key, and the view model fills its fields from that. The typed
loaders fail on exactly the values the window is there to fix, such as the shipped empty password, so a
typed read would open the window empty on the startup-failure path.

The failure window offers Settings only when both sections read and carry every key the dialog edits:
`StartupFailureViewModel.OffersSettings` is true when `SettingsSave.ReadOwned` succeeds for both and
`SettingsSave.FirstAbsentKey` finds nothing absent when the window opens. A missing configuration
directory, a section folder with no `*.yaml` file, an unreadable file, and a required key that no file of
its section carries leave the button hidden, and the
failure text, which names the path and the keys to create, alone instructs the operator; the window
creates no file, so a dialog over a section it cannot read, or over an absent key, has nothing to save into.
`Edit` -> `Settings` over such a folder opens with Save disabled and the reason on the message line. An empty password
still offers Settings, and a save writes it into the existing `connection.yaml`. `App.Run` hands the
configuration directory to the window it shows on both paths. When the argument parse failed, or
`LogFileTarget.Prepare` did, the directory is null and the failure window shows no Settings button.

A save goes through `Settings/SettingsSave.Save`:

1. The view model sends only the keys whose text differs from what it loaded. A key the file spells in
   another case, such as `Host:`, counts as changed: the window reads keys ignoring case and the loaders
   do not, so the save rewrites it under the loader's spelling.
2. `ReadOwned` re-reads both section folders at save time.
3. `SettingsSave` creates `<config-dir>/.settings-staging-<random>/` with a folder per section; one
   that cannot be created fails the save with `Unwritable` naming `<config-dir>` under the `App`
   title, because that folder belongs to no section.
   `ConfigurationSectionWriter.Stage` copies every file of a section into it, and rewrites each file
   that owns an edited key. A key goes into the file that already carries it. A file with no edited
   key is copied byte for byte and never rewritten.
4. `AppSettingsLoader.Load` and `PostgresConnectionLoader.Load` run over the staged folders. A refusal
   is returned with the real section directory in place of the staging one.
5. Every rewritten target is opened for writing and closed. One that refuses fails the save with
   `SectionProblem.Unwritable`, and nothing moves.
6. Each rewritten file replaces its target with `File.Replace`, which keeps the target's ACL and
   attributes on Windows. On Unix it is a rename, so the target's mode is copied onto the staged file
   first, and a `0600` `connection.yaml` stays `0600`. The replaced target goes to
   `<name>.replaced` in the staging folder: `ReplaceFile` can fail after it has moved the target
   away, and the backup is then the only copy.
7. The staging directory is removed on every path but one: a failed save that leaves a rewritten
   target missing keeps it, and the log names it and the missing target. A removal that fails after
   promotion goes to the log and the save still succeeds. The loaders read only `app/` and `connection/`, so a staging
   directory left by a killed process is inert.

A failed save reaches the message panel through `ArchiveFailureMapper`, and the dialog stays open. A
save with no edit writes nothing and shows no restart notice. A section that fails to read is reported
each time the dialog opens; the panel counts a repeat against its newest entry rather than adding one.
The dialog also states the first such section on its message line, ahead of the field rules, with Save
disabled (`SettingsViewModel.ValidationMessage`); a disabled Save always carries a reason.

The staging folder inherits the protection of `<config-dir>`, not that of the section folders, and it
holds the password in plain text while a save runs. One that cannot be removed stays until the operator
deletes it; the log names it.

The window edits only keys that already exist and creates no file or folder. Every key it writes is
required by its loader, so a folder copied from the shipped set has an owner for each one. An edited key no file carries fails
with `SectionProblem.KeyAbsent`; the writer never picks a file for it.

A rewrite parses the owning file into a mapping, sets the keys and serializes the whole mapping back
through the serializer `ConfigurationSection` reads with. A key the loaders do not model survives.
Comments in a rewritten file do not: YamlDotNet does not round-trip them. Scalars round-trip as
strings, so an untouched `port` or `poll_interval_ms` in a rewritten file may come back as
`port: "5432"`, and it still loads.

Several viewers may share one configuration directory, and the last write wins per key. A save
applies only its own changed keys over a read taken at save time, so a key another instance saved
survives a save of a different key. The span between that read and the move is not locked: a save
from another instance landing there, on the same file, is overwritten. The cost is one value typed
again, and a lock file would outlive a killed process. A move can also fail after the write check,
when another process opens the target between the two; the files moved before it stay promoted, the
panel reports the failure, and the next save writes again.

### The pen and group editor

`Edit` -> `Pens and groups` opens `PenEditor/PenEditorWindow`, the viewer's only writer of the pen
catalogue. It writes `semiplot_tags`, `semiplot_groups` and `semiplot_pen_groups` through
`IPenCatalogueEditor` and nothing else (`data-integration.md#the-pen-catalogue-editor`). It never adds a
pen by hand, deletes one or changes an `id`, because the key is the SCADA variable number.

The window is a `TabControl` of two tabs over a bottom bar that both tabs share:

| Part | Holds |
| --- | --- |
| `Pens` tab | A read-only table of every pen: a click on a column header sorts by that column, a second click reverses. Under it, the form of the selected pen: name, unit, mask with a live preview, colour as a `#RRGGBB` text box beside a `ColorPicker`, line style, "on start", and the scale pair. A pen's groups show in the table as text and are not edited there. |
| `Groups` tab | The group list with a new-name field, a create button, one rename field and a delete button. Beside it, every pen of the catalogue as a checkbox, checked where the pen is a member of the selected group. Membership is edited here only. A delete asks once, in a row the tab always reserves, naming the group and how many pens it holds. |
| Bottom bar | The number of pens the last refresh added and `Refresh pen list`, right-aligned. `Refresh pen list` registers the keys the SCADA writes as hidden pens and reads the catalogue again. |

The editor reads fresh from the tables when it opens and after each refresh, and shows the values as
stored: a pen with no colour opens with an empty, invalid colour field
(`data-integration.md#the-stored-read`). It keeps that snapshot while it is open and reads again only on
`Refresh pen list`, so no periodic read rebuilds a form under the operator's hands. The visibility column and
field read "on start": the flag is the visibility a pen starts with, and a change of it switches nothing
on a running chart. What the editor writes reaches the running chart as The live catalogue, below,
states. Several viewers may run on one machine, and the last write wins per column. The layout is in
`ui-theme.md#a-resizable-window-keeps-its-fixed-parts`, the text in `ui-text.md#the-pen-editors-text`.

#### Where the view model is built

The editor copies the settings request path. `MainWindowViewModel` takes the container's
`IPenCatalogueEditor`, which `TrendWindow.Build` resolves; the failure window has no editor, because a
failed start disposes the container and leaves no data source to write through. `ShowPenEditorCommand` is
built with `CreateFromTask` and is always executable. A connection lost after start does not
disable it: the read fails, `ArchiveFailureMapper` reports it as an `Unreachable` warning, and no
window opens.

On a successful read the command emits a `PenEditorViewModel` over the catalogue on
`PenEditorRequests`. `MainWindow` shows `PenEditorWindow` with `ShowDialog` and disposes the view model
in `finally`, as it does for the settings dialog. No startup code calls the editor
(`data-integration.md#startup`).

#### One queue for every call

`PenEditorViewModel` owns one `EditorCallQueue`, and every call to `IPenCatalogueEditor` runs through
it: pen commits, the scale pair, membership toggles, group create, rename and delete, and refresh. The
queue is a task chain, first in, first out. Each call starts after the one before it has finished, a
commit issued while another is in flight waits instead of being dropped, and a refresh reads only after
every commit issued before it. A refresh reads again while a call waits behind its read, because a write
queued there would land on a row the rebuild replaces. A call that throws reaches only its own awaiter
and never stops the next. `WhenIdleAsync` also waits for a call queued while it waits. Every caller runs
on the UI thread, so the queue takes no lock.

While a refresh runs, the pen form and the whole groups tab are disabled, because the rebuild replaces
the view models they edit: text typed then would end its edit on a form or group nobody shows any more.
A click on `Refresh pen list` takes the focus first, so the edit in progress ends, and is queued, before
the registration.

#### A field writes when its edit ends

There is no save button.

- A text field's edit ends on Tab, on Enter, on a click on another control, on a click on empty space
  (a label, the empty area of a form or of the groups tab) and on the window's close, and the field
  writes then. The edit ends on the form that owned the field when focus entered it, so a click on another row writes to the pen the
  field was showing. A field compares its draft with the value it holds once every write of it still
  queued succeeds, so a second end of an unchanged edit writes nothing and an edit back to the stored
  value while a write is in flight is written.
- The queued writes belong to the row, not to the form: a new form is built on every selection, so a
  form built while a write of its row is in flight seeds from the pen as queued. The selected form
  follows the row: a draft still showing the queued value takes the new one when a write is queued,
  lands or fails, and a draft the operator changed stays as typed.
- The group rename keeps its own list of queued names on `PenGroupViewModel` instead of sharing the
  row's. The row's list holds changes of seven settings, folds them over the stored pen and counts a
  later write only when it changes the same setting; the rename list holds one field, its queued value
  is the last name, and any queued name is a later write. The two share only the append and the removal
  of the settled entry, and a common type would take both differences as parameters.
- The scale pair writes only when focus leaves the pair, so tabbing from the minimum to the maximum
  writes no half pair; both bounds go in one statement.
- The line-style combo box and the "on start" checkbox write only a value that differs from the draft,
  so a binding that pushes a newly selected pen's values into them writes nothing. The colour picker
  writes when its flyout closes on a colour other than the one it opened with.
- A value the form refuses, or one the write fails on, reverts to the value the field holds once its
  other queued writes land, marks the field with the `invalid` class and fills the form's message
  line: the first rule a draft breaks, in form order, otherwise the last refusal. A failed write also
  adds one entry to the message panel.
- A membership checkbox reads `IsMember` one way and writes only through its own
  `ToggleMembershipCommand`. A failed toggle raises `PropertyChanged(nameof(IsMember))` with the value
  unchanged, and the binding puts the box back. A group keeps one entry per pen for the life of the
  groups tab, so the box shown after the group is selected again is the entry whose toggle may still
  run: it stays disabled until the toggle lands and then shows it.

The routing is input interop in `PenEditorWindow.axaml.cs`; each decision stays in a view model.
An edit ends when its field loses the focus, and a label or an empty area takes no focus on a click.
So the window handles every pointer press in the tunnel phase, handled ones too: a press on an element
with no focusable, enabled ancestor inside the window clears the focus through the window's
`FocusManager`, and the field's `LostFocus`, or the scale pair losing the focus within, ends the edit as
a click on another control does. A press on a field, a button, a row of the pen table or the group list,
or any other focusable control passes untouched, because that control takes the focus itself. The two
lists themselves, their scroll bars and their empty area below the rows take no focus in Avalonia 12.0.5,
so a press there ends the edit as a press on a label does. `FocusManager` moves the focus only on a left press, while the handler clears it on any
button, so a right or middle press on a label or an empty area ends the edit too. Closing the window drains: `Closing` is cancelled until the recorded edits have ended and the queue is
idle, and then the window closes itself. `MainWindow` disposes the view model only after `ShowDialog`
returns, so a name typed just before the close is written once and nothing is disposed under a write
in flight.

### The live catalogue

Every viewer re-reads the pen catalogue while it runs, so a change the pen editor stores reaches the
chart and the sidebar of every running viewer without a restart. `Bridge/PenCatalogueSync` is the one
route a stored change takes into the running chart. The settings window does not take this route: the
connection it edits is the one every read runs over (`#the-settings-window`).

#### The read

`PenCatalogueSync` reads through `IDataProvider.QueryPensAsync`, the statement the start sequence
reads, so the read carries the provider's normalisation and nothing repeats it. Its loop waits for
`ReadInterval`, 5 s, or for a `ReadNow()`, whichever comes first, and then reads:

- the first read comes one interval after `Start`, because the start sequence has just read the same
  catalogue;
- reads never overlap;
- `ReadNow()` during the wait starts a read at once; during a read it makes exactly one more read
  follow it; the wait after any read is a full interval.

The loop runs on the UI scheduler through `ScheduleAsync`. The read is asynchronous I/O and holds no
thread, and the loop, `ReadNow`, the snapshot and `Dispose` all run on the UI thread, so nothing is
locked. `TrendWindow.Build` builds the sync from the pens the start sequence read, after the
minimap, hands its `ReadNow` to `MainWindowViewModel`, and starts it once the window view model exists.
`Dispose` cancels the wait, and a read that lands after it emits nothing.

The editor asks for a read after every write it lands. `EditorCallQueue` invokes its success callback
on the UI thread after a call whose result succeeded, and a failed or thrown call invokes nothing.
`MainWindowViewModel` builds the editor's view model with a callback that calls the sync's `ReadNow()`,
so the read that follows an edit reaches the instance that made it within a second, while the editor
is still open, and every other instance within 5 s. A scale-pair edit changes no pen already shown
(`#what-a-read-changes`). A burst of writes asks for many reads, and `ReadNow` keeps
them to the read in flight and one after it. Refresh's own read succeeds too and asks for one more
read, which costs one statement. The startup-failure window has no editor and no sync.

#### What a read changes

A read is compared with the previous successful read through `PenListDelta.Between`, never with the
chart's state, so a read carries only what someone stored. The one exception follows an apply that
threw, below. `Pen` compares its `Groups` element by
element, so two reads of one stored pen are equal. A read that changes nothing emits no delta.

| The read carries | The running chart |
| --- | --- |
| The same pens as the previous read, in any order | Nothing changes: chart, axes and sidebar stay untouched. |
| A changed name, unit, mask, colour, line style or group list | That pen changes in place. |
| A changed stored scale pair | Nothing shown changes: the pair is the pen's initial scale, applied when the pen enters the chart, and the restore target of `RestoreInitialScale`, which reads the pair the latest read stored. |
| A changed `enabled_on_start` | Nothing changes on screen: the flag is the visibility a pen starts with. |
| A new pen | It joins with the visibility its `enabled_on_start` gives it, with its history and its live edge. |
| A pen gone from the catalogue | It leaves the chart. When it was the active pen, the first visible pen in catalogue order takes the slot. |

A delta also puts the chart's pens in the order of the read, and any change rebuilds the sidebar from
the chart's pens. The sidebar keeps its width and its expanded state, and every pen keeps its
visibility.

`TrendWindow.Build` hands the sync, the chart, the minimap and the legend to
`MainWindow/PenCatalogueApplier`. The applier applies the sync's `Deltas` one at a time, in order,
through `Select(delta => Observable.FromAsync(..., uiScheduler))` and `Concat()`. The scheduler is
required: `FromAsync` without one completes an apply that awaited on the thread pool, and the delta
queued behind it would start there. Each apply takes four steps:

1. When the chart has no pens and the delta adds some, it reads the extent through
   `MinimapViewModel.LoadExtentAsync` and seeds `Navigation.SeedFromArchiveExtent` with a successful
   one before anything else. The navigation latches on the first data it sees, and a history envelope
   would otherwise latch the first sample of a one-hour window, so the operator could not reach the
   archive's first day.
2. `TrendChartViewModel.ApplyCatalogue(delta.Current)` (`charting.md#applying-a-catalogue-read`).
3. `TrendLegendViewModel.Rebuild()`.
4. When the delta added a pen to a chart that already had some, `LoadExtentAsync` again, because the
   extent read at start does not know the new pen. A successful read whose first sample is earlier
   than the navigation's goes to `Navigation.WidenToArchiveExtent`, which moves the pan floor back and
   leaves the window: the navigation latches its first sample once, so the minimap would otherwise draw
   rows the chart cannot pan to.

The body is one `try/catch`, and a throw goes to the message panel through
`TrendChartViewModel.ReportFailure`, so the next delta still applies. A throw ahead of the end of step 3
also rebases the sync on the pens the chart showed before the delta (`PenCatalogueSync.Rebase`): the next
read is compared with those, so it emits again and the chart and the sidebar catch up instead of staying
diverged for the session. The chart takes a read whatever baseline it was compared with, because
`ApplyCatalogue` compares the read with the pens the chart shows: a pen it holds is revised when the read
differs, a pen it lacks joins, and a pen the read does not name leaves. So the repeat is safe, and so is
a delta queued behind the one that threw, which was read against a catalogue the chart never took. A
throw out of step 4 comes after the chart and the sidebar took the delta and rebases nothing. The
subscription's `onError` reports the same way. `Dispose` disposes the applier, which cancels an apply
still waiting for the extent in step 1, and the sync ahead of the chart; the cancelled apply returns
without touching the chart.

#### A failed read

A failed read keeps the snapshot, so the next success carries everything since. The first two failures
in a row write a warning to the log. The third failure in a row and every one after it go to the
message panel, and a success resets the count. Three is the live edge's threshold, one constant for
both (`ArchiveConnectionState.ConsecutiveFailuresBeforeFault`), so one reconnect after a server restart
opens no panel. A read that throws instead of answering is a defect, not an
outage: it adds one panel entry at once and leaves the count as it was. A subscriber that throws adds
one panel entry, and the loop reads on; the read it threw on is the baseline all the same.

#### Why nothing is pushed and nothing is rebuilt whole

Nothing is pushed between instances. `LISTEN/NOTIFY` loses what is sent while an instance is
disconnected, so the periodic read would stay as the fallback in that design too. One statement of a
few hundred rows every 5 s costs less than the live edge's statement per second, and the live edge
itself arrives 1 to 2 s after the SCADA writes, so 5 s between stations reads as immediate.

The chart applies the delta to the pens it has and is never rebuilt whole. A window's parts are built
once by `TrendWindow.Build` and never replaced. The status bar takes the coordinator's connection
stream and the chart's navigation in its constructor, the navigation controller has no way to take a
window back, and a gesture started on a replaced chart would end on a disposed one.

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
level, and a loaded connection section by one naming the time zone the provider reads the archive in
(`data-integration.md#time-boundary`). At `information` or below those two lines are the only ones a
healthy run writes, and at `warning`, `error` or `fatal` the file stays empty until the first failure.

The process exits `0` when the main window opened and closed normally, and `1` when the start failed —
the startup failure and the fatal catch alike — so a launcher can tell one from the other.

## Scope status

The application reads the real archive and nothing else: the composition root registers
`AddPostgresData`, and every member of `IDataProvider` is implemented over it — the pen catalogue,
the archive extent, the windowed history read and the live-edge poll. The chart draws history and
follows the archive as it grows. The pen editor writes the catalogue through `IPenCatalogueEditor`,
and every running chart follows what it writes within 5 s (`#the-live-catalogue`). See
[data-integration.md](./data-integration.md)
for the contract and `docs/plans/` for the remaining work.
