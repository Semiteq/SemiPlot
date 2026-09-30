# Window per process

## Overview

SemiPlot runs as one trend window per process. An operator opens several windows, each its own process, on
one machine; four is the expected number, not an enforced cap. The windows share the configuration
directory and the one PostgreSQL archive on localhost, whose connection never changes while a process
runs.

What the windows share and what each keeps:

| Shared by every window | Kept by one window |
| --- | --- |
| The pen catalogue: name, colour, style, unit, mask, groups, initial scale (the 5 s catalogue loop) | The group shown, the time window, the live-edge mode |
| The theme, applied live in every open window | The current scale of each pen, and its visibility |
| The language, applied at each window's next start | The message panel and its entries |

This plan brings the code to that model and closes the defects that stand on the chart's hot and failure
paths:

- **Lifetime.** The window's object graph is built once, in one place, and disposed by the one owner that
  built it, in reverse order. The dispose-and-replace machinery built for swapping a chart at runtime goes,
  because no window part is ever replaced. The startup-failure window gets a view model of its own.
- **Initial scale.** The stored `ScaleMin`/`ScaleMax` is the scale a pen starts with in a window. It never
  changes a pen already shown. The double-click autoscale leaves the axis; the View menu carries
  "Pen scale" with "Autoscale" and "Restore initial scale" for the active pen.
- **Another instance.** One helper starts a copy of the running process with the same launch keys. It
  serves "New window", "Restart" in the startup-failure window, and "Restart now" after a settings save
  that needs one.
- **Live theme.** Every process watches the `app/` section folder and applies a changed theme. The process
  that saved applies it through the same route.
- **Defects.**
  - The read left running when a gesture ends is cancelled.
  - A window whose apply threw can be requested again.
  - The column cap trims in chunks.
  - The render thread shares no field with the UI thread.
  - The legend visibility has one writer.
  - The coordinator completes its connection stream.
  - The redraw schedules nothing while idle.
  - Every log line names its process.

## Context (from discovery)

Every line number below is on `master` at 68fe549.

**Composition.** The graph is built in four places:

- the container: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs:17-24` registers
  `MessagePanelViewModel`, `AppStatusBarViewModel` and a `MainWindowViewModel` factory;
- `App.InitializeServices` (`SemiPlot/SemiPlot.UI/App.axaml.cs:166-203`) builds the coordinator (`:172`),
  the chart (`:178`, `:213`), the minimap (`:179`, `:235`) and the catalogue sync (`:181`). It hands them
  to the window view model through `SetChart`, `SetMinimap`, `SetCatalogueSync` (`:189-191`), binds the
  status bar (`:195`), starts the coordinator and the sync (`:197-198`), requests the initial history
  (`:200`) and starts the minimap's extent load (`:202`, `:243-258`);
- `MainWindowViewModel` builds the navigation bar and the legend (`MainWindow/MainWindowViewModel.cs:186-187`),
  the catalogue applier (`:220`), the settings view model (`:238`) and the pen editor view model (`:258`);
- `App.CreateMainWindow` builds a second `MainWindowViewModel` for a failed start (`App.axaml.cs:61-83`).

`PenCatalogueApplier` takes the whole window view model and reads the chart, the minimap and the legend
through it (`MainWindow/PenCatalogueApplier.cs:19-27`, `:36-87`). The container comment
`UiServiceCollectionExtensions.cs:12-13` says the chart and minimap cannot be registered because the UI
scheduler exists only after `UseReactiveUI()`. The container is resolved inside `AfterSetup`
(`App.axaml.cs:104`, `:170`), where `AvaloniaScheduler.Instance` is already readable.

**Replacement machinery with one production caller.** `SetChart`, `SetMinimap`, `SetCatalogueSync`
(`MainWindowViewModel.cs:169-221`) dispose the part they replace. Each has one production caller
(`App.axaml.cs:189-191`). Consequences:

- every window part is nullable (`:129-151`), and so are `_catalogueSync` and `_catalogueApplier` (`:37-38`);
- `AppStatusBarViewModel.TrackLayer` keeps a `SerialDisposable` to follow a replaced chart
  (`MainWindow/AppStatusBarViewModel.cs:26`, `:76-90`), while `TrackArchiveConnection` refuses a second
  bind (`:65-74`);
- four tests pin the replacement (`SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs:41-95`),
  and `AppStatusBarViewModelTests.cs:215-216` pins `TrackLayer(null)`;
- `docs/architecture/overview.md:413` and `:445` describe `SetCatalogueSync`, and `:493-496` states that
  the chart is never rebuilt whole and that the bar binds once.

**Ownership defect.** `TrendChartViewModel.cs:88` adds the injected coordinator to its own disposables,
while `App` created it (`App.axaml.cs:172`) and shares it with the minimap (`:235-240`) and the status bar
(`:195`). `TrendCoordinator.Dispose` (`Bridge/TrendCoordinator.cs:78-94`) completes `_realtimeFailures`
and `_penIds` but leaves `_connectionFaults` (`:26`) open. `_isDisposed` (`:21`) is a plain `bool` read by
`QueryHistoryAsync` (`:118`), which the debouncer calls off the UI thread
(`Chart/ChartHistoryRequestDebouncer.cs:56`). `TrendCoordinator.Start` holds a keep-alive subscription
"across a chart being replaced" (`:100-102`), described in `docs/architecture/data-integration.md:397`;
the chart subscribes to the same stream in its constructor (`TrendChartViewModel.cs:83-84`).

**The window's disposal.** `Program.cs:49` disposes the container, and with it `MainWindowViewModel`,
after `App.Run` returns, which is after the dispatcher loop has ended.

**The startup-failure window.** It is a `MainWindowViewModel` with no chart (`App.axaml.cs:71-82`), plus
the startup-failure row in `MainWindow/MainWindow.axaml:110-131`. It has a panel of its own
(`App.axaml.cs:68`) that `UnhandledErrorObserver` cannot reach, because `ResolveMessagePanel` reads the
container (`App.axaml.cs:161-164`, `Messages/UnhandledErrorObserver.cs:17-28`). For this window's sake,
`MainWindowViewModel` gates the settings and editor commands on nullable arguments
(`MainWindowViewModel.cs:70-75`).

**Initial scale.** `ChartPenSet.Revise` replaces a shown pen's scale settings when the stored pair
changed (`Chart/ChartPenSet.cs:85-95`). `PenRevision.ScaleChanged` (`SemiPlot.Core/Trends/PenListDelta.cs:49`)
has no other production consumer. `ChartPenSet.Add` builds the settings from the stored pair
(`ChartPenSet.cs:65-74`, `:97-107`). The replacement is documented in `docs/architecture/overview.md:436`
and `docs/architecture/charting.md:321`, `:356-359`.

A double-click on the axis autoscales (`Chart/ChartPressRouter.cs:18`, `Chart/TrendChartView.axaml.cs:247-252`),
as documented in `docs/architecture/trend-interaction.md:79`, `:132`, `:201` and
`docs/architecture/trend-feature-spec.md:67-68`. `TrendChartViewModel.AutoscaleAxis` (`:345-350`) sets
`ScaleMode.Auto`.

**Menu.** `MainWindow/AppMenuBar.axaml:13-64` holds File (Exit), Edit (Settings, Pens and groups), View
(four toggles) and Help (About). `AppMenuBarTests` requires every leaf to carry a command or children
(`CLAUDE.md`, UI section).

**Launch keys.** `StartupOptions` (`SemiPlot/SemiPlot.UI/StartupOptions.cs:7-20`) holds `--config-dir`,
`--log-file` and `--logging-level`. `Program.Main` parses them (`Program.cs:19`) and passes only `ConfigDir`
on (`:43`, `:51`). A failed parse opens the failure window with no options (`Program.cs:68-75`).

**Settings.** `SettingsViewModel.SaveAsync` writes through `SettingsSave.Save` on the pool
(`Settings/SettingsViewModel.cs:209-237`) and sets `IsRestartPending` for any edit (`:233-236`). The keys
are `locale` and `theme` (`Startup/AppSettingsLoader.cs:25-27`).

`SettingsSave` stages outside `app/` (`Settings/SettingsSave.cs:61`) and moves each rewritten file in with
`File.Replace` (`:182-215`). `ConfigurationSection` reads a section file with `File.ReadAllText`
(`SemiPlot.Core/Configuration/ConfigurationSection.cs:131`).

`App.Configure` applies the theme once (`App.axaml.cs:115-118`). The chart repaints on
`ActualThemeVariantChanged` (`Chart/TrendChartView.axaml.cs:77`, `:93-96`).

**History pipeline.** `IDataProvider.QueryHistoryAsync` takes no token (`SemiPlot.Core/Data/IDataProvider.cs:19-24`).
The debouncer merges a trailing `Throttle` with a paced `Sample` (`ChartHistoryRequestDebouncer.cs:46-52`),
runs one query at a time and keeps only the newest request waiting (`:92-134`). It starts the query
through the token-less `FromAsync` (`:56`).

`docs/architecture/data-integration.md:248-259` states the reason: a read slower than the cap interval
still completes during a gesture, and the query in flight is never abandoned. `:598` states that no member
takes a `CancellationToken`. Once a gesture has stopped, the read in flight is for a window the gesture
has left, and the final window waits behind it for up to the 300 s command timeout
(`DataSource.Postgres/Configuration/PostgresConnectionSettings.cs:28`).

`ArchiveExceptionMapper.Map` rethrows `OperationCanceledException` (`DataSource.Postgres/ArchiveExceptionMapper.cs:26-29`).

`CompleteQuery` records `_lastApplied` in a `Do` ahead of delivery (`:63`, `:115`) and drops a pending
request that equals it (`:120-125`). A throw out of `applyHistory` is caught in `Deliver` (`:159-166`)
after the window already counts as applied, and the head filter (`:51`, `:174-180`) then drops a repeat of
it. `TrendChartViewModel.ApplyHistory` sets `_lastFetch` before applying (`:488`).

**Render path.**

- `EnvelopeLine.AppendColumn` runs `RemoveRange(0, overflow)` under `_renderStateLock` on every append
  past `MaxColumns = 100_000` (`Chart/EnvelopeLine.cs:14`, `:124-143`).
- `_lastRenderedDataAreaWidth` is written on the render thread (`TrendChartView.axaml.cs:131-136`) and on
  the UI thread (`:154`).
- `RedrawRequested` samples at 33 ms on the UI scheduler (`TrendChartViewModel.cs:23`, `:79-81`), a
  periodic timer that fires while the chart is idle; the trailing `ObserveOn` is redundant.
- The same `Sample` is why a view test must not pass `ImmediateScheduler`. This is documented in
  `CLAUDE.md` (Test), `docs/architecture/bench.md:346-352`, `docs/architecture/testing-strategy.md:166-169`,
  `docs/architecture/charting.md:92` and `docs/architecture/trend-interaction.md:128`.
- `ChartTestBuilder` passes `ImmediateScheduler.Instance` as the UI scheduler
  (`SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs:45`, `:60`).

**Legend.** `TrendLegendRowViewModel.IsVisible` writes the chart and mirrors it back under
`_isSettingVisibilityFromChart` (`Legend/TrendLegendRowViewModel.cs:22`, `:67-78`, `:96-107`).
`TrendPenState` is a `ReactiveObject` with a notifying `IsVisible` (`Chart/TrendPenState.cs:9`, `:26-34`).
The group derives its switch from the rows and writes `row.IsVisible` as a second writer
(`Legend/TrendLegendGroupViewModel.cs:21-25`, `:60-68`).

**Logs.** `Program.CreateLogger` writes one shared file, rolled on size, with no process id in the template
(`Program.cs:97-121`).

**Backlog.** `docs/plans/backlog.md` has no audit-leftovers section; its bullets at `:81-90` name
`scripts/bench-demo.ps1` and `$LiveWithin`, which no longer exist.

## Development Approach

- **testing approach**: Regular. Code first, then the tests in the same task.
- The plan ships as seven pull requests, one per group below, each on its own branch off
  `origin/master`, in the order listed. A group's tasks land together, and the next group starts from the
  merged `master`.
- Complete each task fully before the next. Every task adds or updates tests for the code it touches,
  covering the success path and the failure path.
- All tests pass before the next task starts.
- `dotnet build SemiPlot.slnx` runs with `TreatWarningsAsErrors`. `dotnet format SemiPlot.slnx
  --verify-no-changes` and `dotnet terse` over the touched files exit 0 before every commit.
- Every new test class carries the `Component`, `Area` and `Category` traits. A headless view test drives
  input only through `SemiPlot.Tests.Unit/UI/HeadlessInput.cs`.
- Every string the operator reads goes into `SemiPlot.UI/Localization/Resources.resx` and
  `Resources.ru.resx`.
- A task that changes behaviour updates the `docs/architecture` statements it contradicts in the same task.
- This plan is updated when the scope moves.

## Testing Strategy

- **Unit tests** go in `SemiPlot.Tests.Unit`: `[Fact]` for pure logic, `[AvaloniaFact]` for anything
  touching ReactiveUI, ScottPlot or Avalonia.
- **The composition is tested through the production builder.** `MainWindowTestBuilder` builds its stands
  with the same `TrendWindow.Build` that `App` uses, so a test covers the production wiring rather than a
  copy of it.
- **The theme watcher** is a pure pipeline function over an event stream plus a thin `FileSystemWatcher`
  adapter. The pipeline is tested with plain `[Fact]`s on a `TestScheduler`. One `[Fact]` drives a real
  watcher over a temporary folder and waits at most 5 s for the reload; it runs on both CI legs.
- **The launcher** is tested as a pure `ProcessStartInfo` builder. The process start is a one-line seam
  that no test executes.
- No e2e suite exists; the manual smoke checklist below covers what only a real multi-process run shows.

## Acceptance Evidence

Automated, run from the repository root:

1. `dotnet build SemiPlot.slnx` reports 0 warnings, 0 errors.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` passes with no failures.
3. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` passes with no
   failures.
4. `git grep -nE "SetChart|SetMinimap|SetCatalogueSync|TrackLayer\(null|_disposables.Add\(_coordinator\)|coordinator\.Start\(" -- SemiPlot`
   prints nothing.
5. `git grep -nE "ScaleChanged|AutoscaleAxis" -- SemiPlot` prints nothing.
6. `git grep -nE "_isSettingVisibilityFromChart|_lastRenderedDataAreaWidth|Sample\(_redrawThrottle" -- SemiPlot`
   prints nothing.
7. These named tests exist and pass (`dotnet test ... --filter "FullyQualifiedName~<name>"`):
   - `AFailedStartShowsTheFailureWindowWithItsOwnPanel` (startup-failure window);
   - `DisposingTheWindowClosesTheLiveEdgeBeforeTheConnectionStream` (window composition);
   - `ARevisedStoredScale_LeavesTheShownPenAlone`, `InitialScale_RestoresTheStoredPair` (chart);
   - `TheStartInfoCarriesTheLaunchKeys` (launcher);
   - `RestartExitsOnlyAfterTheCopyStarted` (window view model);
   - `AThemeChangeOnDiskAppliesTheTheme`, `ALanguageChangeOnDiskAppliesNothing`,
     `AWatcherErrorReloadsOnce` (theme pipeline);
   - `APacedRequestNeverCancelsTheQueryInFlight`, `AGestureEndCancelsTheLeftBehindQuery`,
     `ACancelledQueryReportsNothing` (debouncer);
   - `AFailedApplyCanBeRequestedAgain` (debouncer);
   - `AppendingPastTheCapTrimsOneChunk` (`EnvelopeLineTests`);
   - `AnIdleChartSchedulesNoRedraw` (`TrendChartViewModelTests`).

Manual smoke checklist on the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), one
observable outcome per step:

1. File -> New window opens a second window; both show live values.
2. In window 1, set a manual scale on the active pen. In window 2's pen editor, change that pen's colour
   and its initial scale, then save. Window 1 recolours within 5 s and keeps its manual scale. Window 2
   recolours within 1 s and keeps its scale.
3. In window 1, View -> Pen scale -> Restore initial scale sets the active pen's axis to the saved pair;
   View -> Pen scale -> Autoscale fits it to the window.
4. A click on the axis opens the axis scale panel with the pen's name, unit and bounds; typing a bound and
   pressing Enter applies it, a double-click does the same as a single click, and Autoscale in the panel
   reverts the axis to Auto.
5. In window 2, Edit -> Settings, switch the theme, save. Both windows switch theme within 2 s; no restart
   notice appears.
6. Switch the language and save. The dialog shows the restart notice and "Restart now"; the other window
   does not change. "Restart now" closes window 2 and opens a new one in the new language.
7. Drag the chart across a Raw window for 3 s and release. The strip fills during the drag, and the final
   window appears within one read of the release.
8. Stop the bench database and start a third instance: the startup-failure window opens. Start the
   database, press "Restart": a working window replaces it.
9. The log folder holds lines from three process ids, each line carrying its id. The shared file rolls on
   size per process, so the lines may span `semiplot.log` and `semiplot_001.log`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues and blockers with ⚠️ prefix
- update the plan if implementation deviates from the scope

## Solution Overview

**The startup-failure window is its own window.** `Startup/StartupFailureWindow` over a
`StartupFailureViewModel` shows the failure, the message panel, and buttons: Settings, About, Exit, and
Restart. Settings is present when a configuration directory is known; Restart is present when launch
options parsed. `App` keeps the panel of the window it shows, and `ResolveMessagePanel` returns it, so the
ReactiveUI handler reaches either window's panel. With this window gone from `MainWindowViewModel`, the
main window view model has one shape: a chart, a configuration directory and an editor, always.

**One owner per window.** A new `MainWindow/TrendWindow` class is the window's composition and its
owner. `TrendWindow.Build` constructs, in order:

1. the coordinator;
2. the status bar's binding to the coordinator's connection stream;
3. the chart, seeded from the start sequence's pens and extent, whose constructor opens the live edge;
4. the status bar's binding to the chart's layer;
5. the minimap;
6. the catalogue sync;
7. the navigation bar and the legend;
8. the catalogue applier, over the chart, the minimap, the legend and the panel;
9. `MainWindowViewModel`, over the chart, the minimap, the navigation bar, the legend and the sync's
   `ReadNow`.

⚠️ As built: the status bar is not a container service. `Build` constructs it after the chart, over the
coordinator's connection stream and the chart's navigation, and `TrendWindow` disposes it, so steps 2 and 4
collapse into one step after step 3. The coordinator republishes through `ObserveOn` on the UI scheduler,
so no state arrives before `Build` returns. The catalogue applier is built without the panel, which it
never used. `AddUi` lost its `configDirectory` parameter, and `Build` takes the directory from Task 2. `InitializeServicesTests` is `TrendWindowBuildTests`.

Then it starts the sync, requests the initial history and starts the minimap's extent load.
`TrendWindow.Dispose` disposes the same objects in reverse order, the coordinator last.

`MainWindowViewModel` disposes only what it creates: its commands and request subjects. `App` builds one
`TrendWindow`, shows its view model, and disposes the `TrendWindow` from the main window's `Closed` event
on the UI thread. ASSUMPTION: Avalonia renders no further frame for a window after `Closed`, so
`Plot.Dispose` there meets no render in flight. A headless test closes a realised window and then disposes
its `TrendWindow` without an exception.

⚠️ As built: `AppMainWindowTests` drives `App.Configure` and `App.CreateMainWindow` over a stand's container,
closes the window and reads the provider's `OpenLiveSubscriptionCount` and `ConnectionFaultsObserverCount`
back at zero, which the `Closed` handler alone can cause. `CreateMainWindow` and `ResolveMessagePanel` are
`internal` for those tests; the tests restore the private `App` fields `Configure` writes through one
`AppStateScope`. The disposal-order test asserts the provider's observer counts, not completion of
`ConnectionFaults`. A throw out of `Build` ends the start and the process exits; nothing is disposed.

The container keeps the process services: the data source, the provider, the editor, the message panel,
the status bar and the logger factory. It no longer registers `MainWindowViewModel`. `MainWindowTestBuilder`
builds its stands through `TrendWindow.Build`.

**Initial scale.** A pen's scale settings are built from its stored pair when the pen enters the chart, at
the start or when the catalogue adds it, and never again from the catalogue. Two chart methods act on the
active pen and do nothing when there is none or when it is switched off (➕ deviation: both are `void`, and
the hidden-pen no-op is new):

- `AutoscaleActivePen` replaces `AutoscaleAxis` and sets `Auto`;
- `RestoreInitialScale` rebuilds the settings from the pen's current stored pair, `Auto` when it has none.

The catalogue loop keeps the stored pair fresh on the pen record, so the restore reads what the database
held at most 5 s ago. The two View menu commands are always executable, like the other View items.

**Another instance.** `Startup/InstanceLauncher` builds a `ProcessStartInfo` from `Environment.ProcessPath`
and the parsed `StartupOptions`. The three keys go into `ArgumentList`, never a joined string. When the
host is the `dotnet` muxer, the entry assembly path goes first. `App.Run` receives the parsed options
instead of the bare configuration directory.

A window view model's `RestartApplication` starts a copy through the launcher. On success it pushes
`ExitRequests`, which the window already turns into `Close` (`MainWindow.axaml.cs:34`); on failure it
reports to the panel. The settings dialog's "Restart now" calls the `RestartApplication` its owning window
view model hands it.

**Live theme.** `App` owns one `Settings/AppSectionWatcher` for the process, whichever window it shows. The
pipeline is a pure function:

```
ThemeChanges(IObservable<Unit> fileEvents, Func<Result<AppSettings>> load, IScheduler data, IScheduler ui)
    -> IObservable<Result<AppThemeVariant>>
```

It throttles the events for 300 ms on the data scheduler, runs `load` on the data scheduler, emits on the
UI scheduler, and passes a theme on only when it differs from the last one applied. `App` writes the
emitted theme to `RequestedThemeVariant`, the one writer of the variant after start, and reports a failed
load through the mapper. A changed locale is ignored.

The adapter merges the `FileSystemWatcher` events `Changed`, `Created`, `Renamed` and `Deleted` for `*.yaml`
in `<config-dir>/app`, with `NotifyFilter = FileName | LastWrite`. `File.Replace` surfaces as `Created` or
`Renamed` on Windows and as `Renamed` on Linux. The watcher's `Error` event joins the stream as one more
event, so an overflowed buffer ends in one reload. `ConfigurationSection` opens section files with
`FileShare.ReadWrite | FileShare.Delete`, so a watcher's read never blocks another process's
`File.Replace`.

`SettingsViewModel` sets `IsRestartPending` only when a key other than `theme` changed. The saving process
applies its theme through the watcher like every other process.

## Technical Details

**`TrendWindow`** holds its parts as non-null fields, in the order `Build` assigns them. Its constructor
is private. `Build` takes the start sequence's data and the UI scheduler, and resolves the logger factory
and the panel from `StartupData.ServiceProvider`. `Build` gains the configuration directory and the
launch options in the tasks that first consume them. The status bar's binding ahead of the first poll tick
was a comment at `App.axaml.cs:193-194`; it becomes a statement order in one method.

**`TrendCoordinator`.** `Start` and its keep-alive go. The chart's constructor subscription holds the
`RefCount` for the window's whole life, because the chart is never replaced and is disposed only with the
window. `Dispose` completes `_connectionFaults`. The `ObjectDisposedException` guards on
`QueryHistoryAsync` and `QueryArchiveExtentAsync` go: `TrendWindow` disposes the coordinator after every
caller, and the provider the call reaches outlives it. `_isDisposed` is then read only on the UI thread.

**Debouncer cancellation.** A paced admission never cancels: during a gesture the read in flight
completes and lands, which `data-integration.md:248-259` requires. A trailing admission is the gesture's
end. When it finds a query running for a different request, it stores itself as `_pending` and cancels the
running query. `Dispose` cancels the running query.

Each query runs under its own `CancellationTokenSource`, created, cancelled and disposed only under
`_gate`. A query that ends in `OperationCanceledException` completes as `Cancelled`: `CompleteQuery`
leaves `_lastApplied` as it was and starts the pending request, and `Deliver` reports nothing.

The token is the trailing `CancellationToken cancellationToken = default` of
`IDataProvider.QueryHistoryAsync`, `TrendCoordinator.QueryHistoryAsync` and the chart's adapter.
`PostgresDataProvider` passes it to `OpenConnectionAsync`, `ExecuteReaderAsync` and `ReadAsync` in
`ReadWindowAsync`, `ReadBucketedWindowAsync` and `FillFreshTailAsync`. The mapper's rethrow
(`ArchiveExceptionMapper.cs:26-29`) keeps a cancelled read out of the failure `Result`.

A cancelled command costs at most one connection for two seconds. Npgsql first sends PostgreSQL a cancel
request. If no answer arrives within `CancellationTimeout`, it breaks the socket itself: 2000 ms by default
(`Npgsql.xml` of package 10.0.3, lines 3684-3688), and the connection string leaves it at the default
(`PostgresConnectionSettings.cs:34-44`). A broken connection leaves the pool, and the pool opens a new one.

**Applied mark.** `CompleteQuery` keeps writing `_lastApplied`. `Deliver`'s catch clears it under `_gate`
when it still equals the request whose apply threw, so the same window passes the head filter again.
`TrendChartViewModel.ApplyHistory` sets `_lastFetch` after the envelopes are loaded.

**Column cap.** `EnvelopeLine` trims when the count passes `MaxColumns`, removing `MaxColumns / 10`
columns in one `RemoveRange`. The list lives between 90 000 and 100 000 columns, and the shift runs once
per 10 000 appends.

**Data-area width.** `OnPlotRenderFinished` only posts the width, and
`TrendChartViewModel.ReportDataAreaWidth` ignores a width equal to the last one it applied. The view keeps
no width field.

**Idle redraw.** `RequestRedraw` schedules one emission 33 ms ahead on the UI scheduler unless one is
already scheduled; requests inside that span join it. The flag and the scheduled handle are UI-thread
state, and `Dispose` disposes the handle. With no request nothing is scheduled. `ImmediateScheduler` runs
the one delayed action inline after its wait and returns, because the schedule is one-shot, not periodic.
The periodic-schedule hang documented for `Sample` therefore leaves the chart's redraw.

**Legend visibility.** `TrendPenState.IsVisible` is the one source, and the chart's `SetPenVisibility` is
its one writer.

- The row exposes the pen state and a `ToggleVisibilityCommand` that calls `SetPenVisibility`.
- The row's `CheckBox` binds `IsChecked="{Binding PenState.IsVisible, Mode=OneWay}"` plus the command,
  the pattern `CLAUDE.md` sets for checkable menu items.
- The group derives its switch from the pen states and switches through a row method that calls
  `SetPenVisibility`.

**Log line.** The template gains `[{ProcessId}]`, and the logger enriches with
`Enrich.WithProperty("ProcessId", Environment.ProcessId)`. No package is added.

## What Goes Where

- **Implementation Steps**: code, tests and documentation in this repository.
- **Post-Completion**: the multi-process run on a workstation.

## Implementation Steps

### Group A: the window's lifetime (PR 1)

### Task 1: Give the startup-failure window its own view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupFailureViewModel.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupFailureWindow.axaml`
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupFailureWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupFailureWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`

- [x] create `StartupFailureViewModel` with the failure view, the panel, and Settings, About and Exit
      commands
- [x] create `StartupFailureWindow` showing the failure's title, detail and remedy, the panel and the
      buttons
- [x] `App.CreateMainWindow` opens `StartupFailureWindow` on a failed start; `App` keeps the panel of the
      window it shows, and `ResolveMessagePanel` returns it
- [x] delete `StartupFailure`, `HasStartupFailure`, the startup-failure row (`MainWindow.axaml:110-131`),
      the status bar's `!HasStartupFailure` gate (`:106`) and the nullable gates on the settings and
      editor commands (`MainWindowViewModel.cs:70-75`); the constructor takes a non-null configuration
      directory and editor
- [x] move the startup-failure tests out of `MainWindowViewTests.cs:45-187`, splitting the theory at
      `:138-187` by window, and `MainWindowViewModelTests.cs:98-111` into `StartupFailureWindowTests`;
      update the null-argument cases at `MainWindowViewModelTests.cs:130-183` and the builder calls in
      `AppMenuBarTests.cs:32-165`
- [x] write `AFailedStartShowsTheFailureWindowWithItsOwnPanel` and a test that `UnhandledErrorObserver`
      reaches that panel
- [x] run the unit tests - must pass before task 2

### Task 2: Build the window in one place and own it there

**Files:**
- Create: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/PenCatalogueApplier.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppStatusBarViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartCatalogueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/InitializeServicesTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/EmptyCatalogueStartupTests.cs`

- [x] create `TrendWindow` with `Build` and `Dispose` in the order Solution Overview states
- [x] `PenCatalogueApplier` takes the chart, the minimap, the legend and the panel instead of the window
      view model
- [x] `MainWindowViewModel` takes the chart, minimap, navigation bar, legend and `ReadNow` by constructor
      as non-null. Delete `SetChart`, `SetMinimap`, `SetCatalogueSync`, the applier and sync fields, and
      the part disposal in `Dispose` (`MainWindowViewModel.cs:169-221`, `:266-275`)
- [x] `AppStatusBarViewModel.TrackLayer` takes a non-null navigation once and drops the `SerialDisposable`
- [x] remove `_disposables.Add(_coordinator)` (`TrendChartViewModel.cs:88`). Remove `TrendCoordinator.Start`,
      its keep-alive and the two query guards, and complete `_connectionFaults` in `Dispose`
- [x] `App.InitializeServices` becomes one `TrendWindow.Build` call, and `App` disposes the `TrendWindow`
      from the main window's `Closed` event. The container stops registering `MainWindowViewModel`, and
      the `UiServiceCollectionExtensions.cs:12-13` comment goes with it
- [x] `MainWindowTestBuilder` builds through `TrendWindow.Build`. Delete the four replacement tests
      (`MainWindowViewModelTests.cs:41-95`) and the `TrackLayer(null)` test
      (`AppStatusBarViewModelTests.cs:215-216`), and rewrite
      `LegendPanelWidth_ReadsTheFallbackAndThenFollowsThePanelState` (`MainWindowViewTests.cs:249-276`) for a
      window that always has a legend
- [x] delete the `Start`-pinning tests (`TrendCoordinatorTests.cs:92-118`) and every `coordinator.Start()`
      call (`TrendCoordinatorTests.cs`, `TrendChartViewModelTests.cs:85`, `:604`, `:619`, `:638`, `:1320`,
      `TrendChartCatalogueTests.cs:252`). Port `InitializeServicesTests.cs:51-101` and
      `EmptyCatalogueStartupTests.cs:46-69` to `TrendWindow.Build`, and replace
      `Container_ResolvesMainWindowViewModel` (`CompositionRootTests.cs:68-72`) with a test that the
      container resolves the panel and the status bar
- [x] write `DisposingTheWindowClosesTheLiveEdgeBeforeTheConnectionStream`:
      `FakeDataProvider.OpenLiveSubscriptionCount` (`FakeDataProvider.cs:92`) reaches 0 before
      `ConnectionFaults` completes
- [x] write a test that the chart's disposal leaves `ConnectionFaults` forwarding, because the coordinator
      is not disposed with it, and a headless test that closing a realised window and then disposing its
      `TrendWindow` throws nothing
- [x] write a `TrendCoordinatorTests` case that disposal completes `ConnectionFaults`
- [x] run the unit tests - must pass before task 3

### Task 3: Document the window's lifetime

**Files:**
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `CLAUDE.md`

- [x] `overview.md`: one window per process, the shared and per-window table from this plan's Overview,
      `TrendWindow` as the window's composition and owner, the failure window
- [x] `overview.md:413`, `:445`: `TrendWindow.Build` builds and starts the sync; `:493-496`: a window's parts
      are built once and never replaced
- [x] `data-integration.md`: the startup section names `TrendWindow.Build` instead of `InitializeServices`;
      `:397` drops the keep-alive
- [x] `CLAUDE.md`: the Dependency Injection bullet names `TrendWindow.Build` where it names
      `MainWindowViewModel.SetCatalogueSync`
- [x] run `dotnet build SemiPlot.slnx` - clean before group B

### Task 3a: ➕ Offer Settings only where it can save

A missing configuration directory opens the startup-failure window, whose Settings button opens a
dialog that cannot save: `SettingsViewModel.cs:64` enables Save only when both sections read, and
`SettingsSave.StageSection` fails on any section error (`Settings/SettingsSave.cs:119-124`). The
operator fills every field and Save stays disabled with no reason given.

A missing or unreadable configuration is an installation fault, and the remedy text of the failure
already names the path and the keys to create. The settings window keeps its rule: it edits only keys
that already exist, each in the file that owns it, and creates no file.

The rule after this task:

- The startup-failure window shows Settings only when both sections read
  (`SettingsSave.ReadOwned` succeeds for both when the window opens) and every key the dialog edits is
  present in its section (`SettingsSave.FirstAbsentKey`). Otherwise the failure text alone
  instructs the operator.
- A dialog that cannot save states why on its reserved message line: the first section that fails to
  read, then the first absent key, then the field rules. A disabled Save always carries a reason.

The file-creating writer of commit 4966df7 is reverted; the message-line reason is kept.

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSectionWriter.cs` (revert)
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsSave.cs` (revert)
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureViewModel.cs`
- Modify: the `ConfigurationSectionWriter`, `SettingsSave`, `SettingsViewModel` and `StartupFailureWindow`
  test classes
- Modify: `docs/architecture/overview.md`, `CLAUDE.md`, `docs/architecture/ui-text.md`,
  `docs/architecture/data-integration.md`

- [x] revert the file-creating writer of 4966df7: `ConfigurationSectionWriter`, `OwnedSection.Empty`,
      the `SettingsSave` create, move and directory-cleanup paths, their tests and their documentation
- [x] keep the dialog's stated reason: a section that fails to read shows its error on the message line
      ahead of the field rules
- [x] `StartupFailureViewModel` shows Settings only when both sections read and carry every key the dialog edits
- [x] write tests: a failure window over a missing configuration directory, over a section with no files
      and over an unreadable file shows no Settings; over an empty password it shows Settings, and a save
      writes the password into the existing `connection.yaml`; a dialog over an unreadable section states
      the section's error with Save disabled
- [x] `overview.md#the-settings-window`, `CLAUDE.md`, `ui-text.md`, `data-integration.md`: the settings
      window creates no file, and the failure window offers it only when both sections read and carry every edited key
- [x] run the unit and integration tests - must pass before group B

### Group B: the initial scale (PR 2)

### Task 4: Apply the stored scale only when a pen enters the chart

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPenSet.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenListDelta.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/PenListDeltaTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/LiveCatalogueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartCatalogueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/charting.md`

- [x] `ChartPenSet.Revise` stops replacing scale settings. Delete `PenRevision.ScaleChanged` and its
      assertions (`PenListDeltaTests.cs:92`, `:101-107`, `:143`; `LiveCatalogueTests.cs:90`)
- [x] replace `TrendChartViewModel.AutoscaleAxis` (`:345-350`) with `AutoscaleActivePen` and add
      `RestoreInitialScale`, which reads `PenScaleSettings.InitialFor` (➕ deviation: no
      `ChartPenSet.RestoreInitialScale` exists, both methods are `void`). Both act on the active pen, do
      nothing without one or while it is switched off, and apply the axis model and a redraw
- [x] write `ARevisedStoredScale_LeavesTheShownPenAlone`, keeping both a manual and an auto session scale
- [x] write `InitialScale_RestoresTheStoredPair`, a case where no stored pair gives `Auto`, and a test that a
      pen the catalogue adds takes its stored pair; port `TrendChartViewModelTests.cs:143-149`
- [x] `overview.md:436` and `charting.md:321`, `:356-359`: the stored pair is the initial scale and the
      restore target, and a changed pair leaves every shown pen alone
- [x] run both test projects - must pass before task 5

### Task 5: Move autoscale from the axis to the View menu

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPressRouterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartAxisRegionEditTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/architecture/trend-feature-spec.md`

- [x] delete `ChartPressAction.AutoscaleAxis`, the click-count branch (`ChartPressRouter.cs:18`) and its
      view case (`TrendChartView.axaml.cs:247-252`); `Route` loses its `clickCount` parameter
- [x] add `AutoscaleCommand` and `InitialScaleCommand` to `MainWindowViewModel`, always executable, and
      two View menu items bound to them, with labels in both resource files
- [x] update `ChartPressRouterTests`; replace `DoubleClickOnAxisRegion_RevertsToAutoscale`
      (`ChartAxisRegionEditTests.cs:66-72`) with a test that a double-click opens the bound editor; write
      menu tests that each item invokes its chart method on the active pen
- [x] `trend-interaction.md:79`, `:132`, `:201` and `trend-feature-spec.md:67-68`: autoscale and initial
      scale are View menu commands on the active pen
- [x] run the unit tests - must pass before task 6

### Task 5a: ➕ Edit the active pen's scale in a panel at its axis

A click on the axis opens a bare `TextBox` placed over the ScottPlot canvas by its margin
(`Chart/TrendChartView.axaml:46`, `Chart/TrendChartView.axaml.cs:283-334`). It shows the axis value
at the click point rather than a bound, and the half of the axis clicked silently decides whether the
minimum or the maximum is edited (`Chart/ChartAxisRegion.cs:77`, `Chart/ChartAxisEdit.cs:5`). The two
View menu commands sit among the panel toggles and do not say which pen they act on. Issue #86 asks
for the native mechanism.

The rule after this task:

- A click on the active pen's axis opens an Avalonia `Flyout` anchored at the axis. Its header names
  the pen and its unit. It holds Maximum and Minimum as plain `TextBox` fields (➕ deviation: not
  `NumericUpDown`, whose text is re-parsed on every keystroke and keeps the last value that parsed)
  seeded with the axis's current bounds, formatted and parsed by the pen editor's rule
  (`PenFormRules.FormatBound`, `PenFormRules.TryReadBound`), a reserved message line, and three buttons:
  Apply, Autoscale, Restore initial scale ("Вернуть начальную"). Escape and a click outside close it
  without writing. Enter in either field applies (➕ deviation: a key binding on the two fields, not
  `IsDefault` on Apply, because Avalonia's default-button handler listens on the visual root and the
  overlay popup a headless test hosts never routes key events to it).
- Apply sets both bounds as a manual scale on the pen the panel was opened for. A field that is not a
  number in the current culture, an empty field, or a minimum not below the maximum is refused on the
  message line, marks the field `invalid` and disables Apply; nothing is written and the panel never
  resizes. Autoscale and Restore initial scale act on that same pen (➕ deviation: `AutoscalePen(int)` and
  `RestoreInitialScale(int)` join the two active-pen methods).
- The panel closes without writing when its pen leaves the chart, is hidden or stops being the active
  pen while the panel is open (➕ deviation: `TrendChartViewModel.DrawnPenId`, the active pen while it is
  visible, carries the rule).
- The View menu carries one submenu whose header names the pen whose axis is drawn ("Шкала пера «Damper 01»",
  "Pen scale: Damper 01"; while no axis is drawn, that is with every pen hidden or no pen, "Шкала пера" /
  "Pen scale"), holding Autoscale and "Вернуть начальную шкалу" / "Restore initial scale". The header
  follows visibility changes as well as the active pen and the pen names.
- The inline `TextBox`, its margin placement, the half-of-axis choice and `ChartAxisEdit.SeedManualLimits`
  go; the axis hit test stays.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`, `TrendChartView.axaml.cs`
- Create: the axis panel's view model and view under `SemiPlot/SemiPlot.UI/Chart`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartAxisRegion.cs`, delete `ChartAxisEdit.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`, `MainWindowViewModel.cs`
- Modify: `Resources.resx`, `Resources.ru.resx`
- Modify: the chart pointer, axis-region, menu bar and chart view model test classes
- Modify: `docs/architecture/trend-interaction.md`, `trend-feature-spec.md`, `charting.md`,
  `ui-text.md`, `ui-theme.md`, `readme.md`

- [x] replace the inline editor with the axis `Flyout` and its view model as the rule states
- [x] Apply validates both bounds and writes a manual scale on the active pen; Autoscale and Initial
      scale call the existing chart methods and close the panel
- [x] the View menu submenu with the drawn pen's name in its header, bound to `DrawnPenId` and the pen
      names
- [x] delete `ChartAxisEdit`, the half-of-axis choice and the inline `TextBox`
- [x] write tests: a click on the axis opens the panel seeded with the current bounds and the pen's
      name; Apply writes both bounds; an inverted or empty pair is refused with the panel's size
      unchanged; Escape and a click outside write nothing; an unreadable entry, including a fraction in
      the other culture's separator, is refused and never applied, Enter included; Enter on the focused
      Autoscale button autoscales; Autoscale and Restore initial scale act on the pen the panel was opened
      for; the panel closes when that pen is hidden, deactivated or removed; the submenu header follows
      the drawn pen and reads "Pen scale" without one
- [x] measure the panel and the submenu header in the real theme in both languages and record the
      figures in `ui-theme.md`; update the listed docs
- [x] run the unit tests - must pass before group C


### Task 5b: ➕ Show the axis panel's bounds in the pen's format

The axis scale panel seeds its two fields with the round-trip text of the bounds, so an autoscaled
axis shows values such as 43,50529834. A pen's reading is rendered through
`SemiPlot.Core.Trends.PenValueFormat` and nowhere else (`CLAUDE.md`, UI), with the stored mask and the
`0.###` fallback; the panel's header already takes the pen's unit from the same settings.

The rule after this task:

- The panel seeds Maximum and Minimum through `PenValueFormat` with the pen's stored mask.
- A field the operator leaves unchanged keeps the exact bound it was seeded from, so a mask that
  rounds never moves the scale on Apply. Only a field whose text changed is parsed and written.
- Parsing of typed text is unchanged (`PenFormRules.TryReadBound`).

- [x] seed the two fields through `PenValueFormat` with the pen's mask
- [x] keep the exact seeded bound for an unchanged field on Apply
- [x] write tests: a masked pen seeds masked text; the `0.###` fallback without a mask; Apply with both
      fields untouched leaves the exact bounds (a mask that rounds, e.g. "0" over 0.4..9.6); Apply with
      one field edited writes the typed value and keeps the other exact
- [x] update `ui-text.md` and `trend-interaction.md#the-axis-scale-panel`
- [x] run the unit tests - must pass before group C


### Group C: another instance (PR 3)

### Task 6: Start another instance with the same launch keys

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/InstanceLauncher.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/InstanceLauncherTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `CLAUDE.md`

- [ ] create `InstanceLauncher` as Solution Overview states. A failed start returns a failed `Result`,
      which the caller reports through the mapper
- [ ] `Program` passes the parsed `StartupOptions` to `App.Run` and `App.Configure`, and a failed parse
      passes none; update `AppConfigurationTests.cs:67` and the `App.Run` signature quoted in `CLAUDE.md`
- [ ] `TrendWindow.Build` takes the options. Add `RestartApplication` to `MainWindowViewModel` and
      `StartupFailureViewModel`. File -> New window starts a copy. The failure window's Restart calls
      `RestartApplication` and is present only with options
- [ ] add the labels to both resource files
- [ ] write `TheStartInfoCarriesTheLaunchKeys`, a case for the `dotnet` muxer host and a case for a path
      with spaces
- [ ] write `RestartExitsOnlyAfterTheCopyStarted`, a failed start that reports and keeps the window, and a
      test that New window starts a copy and keeps the window
- [ ] run the unit tests - must pass before task 7

### Task 7: Offer a restart only for what needs one

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelTests.cs`
- Modify: `docs/architecture/overview.md`

- [ ] `IsRestartPending` is set when an edit touches a key other than `theme` (`SettingsViewModel.cs:233-236`)
- [ ] `SettingsViewModel` takes the owning window view model's `RestartApplication` and exposes
      `RestartNowCommand`, shown with the notice
- [ ] write tests: a theme-only save sets no notice; a locale or connection save sets it; "Restart now"
      calls the handed restart
- [ ] `overview.md#the-settings-window`: the restart notice covers every key but the theme, and "Restart
      now" starts a copy with the same keys
- [ ] run the unit tests - must pass before task 8

### Group D: the live theme (PR 4)

### Task 8: Apply a theme saved by any process

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Settings/AppSectionWatcher.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSection.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/AppSectionWatcherTests.cs`
- Modify: the `ConfigurationSection` test class in `SemiPlot/SemiPlot.Tests.Unit/Core`
- Modify: `docs/architecture/overview.md`
- Modify: `CLAUDE.md`

- [ ] create `AppSectionWatcher`: the `ThemeChanges` pipeline and the `FileSystemWatcher` adapter,
      as Solution Overview states
- [ ] `App` builds one watcher for the window it shows, writes the theme it emits, reports a failed load,
      and disposes the watcher at exit
- [ ] `ConfigurationSection` opens section files with `FileShare.ReadWrite | FileShare.Delete`
      (`ConfigurationSection.cs:131`)
- [ ] write `AThemeChangeOnDiskAppliesTheTheme`, `ALanguageChangeOnDiskAppliesNothing` and
      `AWatcherErrorReloadsOnce`, plus tests that a burst collapses to one load and that a failed load
      reaches the report
- [ ] write the real-folder `[Fact]`: a `File.Replace` into a temporary `app/` folder yields the new theme
      within 5 s; and a `ConfigurationSection` test that reads a file another handle holds open for writing
- [ ] `overview.md#the-settings-window` and `:279-285`: the theme applies live in every process;
      `CLAUDE.md`: "every change takes effect at the next start" names the theme as the exception
- [ ] run the unit tests - must pass before task 9

### Group E: the history pipeline (PR 5)

### Task 9: Cancel the read a finished gesture left behind

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresHistoryReadTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [ ] thread the token as Technical Details states, from the interface to `ReadAsync`
- [ ] implement the debouncer's cancellation rule and the `Cancelled` completion
- [ ] `FakeDataProvider` records the last token and honours it
- [ ] write `APacedRequestNeverCancelsTheQueryInFlight`: a continuous drag over a 600 ms query delivers
      results during the drag
- [ ] write `AGestureEndCancelsTheLeftBehindQuery`, `ACancelledQueryReportsNothing` and a test that
      disposal cancels the running read
- [ ] write an integration fact in `PostgresHistoryReadTests`: a history read with a cancelled token throws
      `OperationCanceledException` and returns no failed `Result`
- [ ] `data-integration.md:248-259`, `:598`: the query in flight completes during a gesture and is
      cancelled when the gesture ends on another window
- [ ] run both test projects - must pass before task 10

### Task 10: Let a window whose apply threw be requested again

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs`

- [ ] `Deliver`'s catch clears `_lastApplied` under `_gate` when it still equals the failed request;
      `ApplyHistory` sets `_lastFetch` after the load
- [ ] write `AFailedApplyCanBeRequestedAgain`: `applyHistory` throws once, and the same request issued
      again is applied
- [ ] run the unit tests - must pass before task 11

### Group F: the render path and the legend (PR 6)

### Task 11: Trim the column buffer in chunks

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `docs/architecture/charting.md`

- [ ] trim `MaxColumns / 10` once the count passes `MaxColumns`
- [ ] write `AppendingPastTheCapTrimsOneChunk`: the count after the trim, the oldest surviving X, and no
      count above `MaxColumns + 1`
- [ ] `charting.md`: the cap trims in chunks of one tenth
- [ ] run the unit tests - must pass before task 12

### Task 12: Keep the data-area width on the UI thread

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartRenderThreadTests.cs`

- [ ] delete `_lastRenderedDataAreaWidth`; `OnPlotRenderFinished` posts every width
- [ ] `ReportDataAreaWidth` ignores a width equal to the last applied one
- [ ] write tests: a repeated width changes nothing; a new width re-targets the column count
- [ ] run the unit tests - must pass before task 13

### Task 13: Let an idle chart schedule nothing

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/architecture/bench.md`
- Modify: `docs/architecture/testing-strategy.md`

- [ ] replace `Sample` and the trailing `ObserveOn` (`TrendChartViewModel.cs:79-81`) with the one-shot
      schedule Technical Details states
- [ ] write `AnIdleChartSchedulesNoRedraw` on a `TestScheduler`, and a burst test: requests over 100 ms give
      one redraw per 33 ms span and one after the last request
- [ ] rewrite the redraw and hang statements in `CLAUDE.md` (Test), `charting.md:92`,
      `trend-interaction.md:128`, `bench.md:346-352` and `testing-strategy.md:166-169`: the redraw is a
      one-shot schedule, and the periodic-schedule hang remains for `PenCatalogueSync`'s wait only
- [ ] run the unit tests - must pass before task 14

### Task 14: Give pen visibility one writer

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendRowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendGroupViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Legend/TrendLegendView.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Legend/TrendLegendViewTests.cs`

- [ ] the row exposes the pen state and `ToggleVisibilityCommand`; delete the row's `IsVisible` setter and
      `_isSettingVisibilityFromChart`
- [ ] the group derives its switch from the pen states and switches through the rows' command path
- [ ] the row's `CheckBox` binds through the pen state one way plus the command; the allowlist in
      `TrendLegendViewTests` still holds
- [ ] write tests: a row click, a group switch and a chart-side change each leave the checkbox and the pen
      in agreement
- [ ] run the unit tests - must pass before task 15

### Group G: logs and backlog (PR 7)

### Task 15: Name the process in every log line

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `docs/architecture/overview.md`

- [ ] add the `ProcessId` property and `[{ProcessId}]` to the template (`Program.cs:99-107`)
- [ ] `overview.md`: several processes share the log file, each line naming its process, each process
      rolling it on size on its own
- [ ] run the demo stand once and confirm a log line carries the id

### Task 16: Move the audit's leftovers to the backlog

**Files:**
- Modify: `docs/plans/backlog.md`

- [ ] add "Platform audit leftovers", one line each:
      - the dead logarithmic scale (`PenScaleSettings.cs:6`, `PenScaleModel.cs`);
      - the `ObjectDisposedException` guards on no-op members (`TrendChartViewModel` x12, `MinimapViewModel` x2);
      - `ChartCursorReader` with one caller;
      - the hand-rolled log-level switch (`StartupOptions.cs:105-117`);
      - `ThrowIfNull` on repository-internal constructors;
      - the two `FormatReading` wrappers;
      - `TrendChartViewModel.ScaleSettings`, read only by tests;
      - the pluralisation in `PostgresConnectionLoader`;
      - `throw exception;` in `ArchiveExceptionMapper.cs:28`;
      - the undisposed bounds subscription in `MinimapView.axaml.cs:29`;
      - the double UI hop in `MinimapViewModel.LoadExtentAsync` (`:86-87`);
      - the per-pointer-move allocation in the cursor read
- [ ] delete the two stale bullets at `backlog.md:81-90`
- [ ] run `git grep -n "bench-demo.ps1\|LiveWithin" -- docs/plans/backlog.md` - prints nothing

### Task 17: Verify acceptance criteria

- [ ] every item of Acceptance Evidence's automated list passes
- [ ] `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [ ] `dotnet terse` over every touched `.cs` file exits 0
- [ ] the manual smoke checklist passes on the demo stand

### Task 18: [Final] Update documentation

- [ ] run `git grep -nE "SetChart|SetCatalogueSync|InitializeServices|coordinator\.Start|keep-alive|ScaleChanged|double-click|Sample\(33|takes no .CancellationToken|next start" -- CLAUDE.md docs/architecture`
      and resolve every statement the groups left behind
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification**
- Run four instances on a multi-monitor workstation for a shift and watch the log for reports from the
  watcher and the launcher.
- Walk the operator workflow with the owner: open windows through File -> New window, compare a live
  window with a historical one, recolour a pen from any window.

**Executed by exec:**
- branch: window-lifetime
- branch: initial-scale

## Verify it yourself

Group B (Tasks 4-5b) only; Group A shipped as #97 and #98, Groups C-G are later branches.

1. Build and tests, from the repository root:
   - `dotnet build SemiPlot.slnx` - 0 warnings, 0 errors.
   - `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - 1491 passed.
   - `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj` - 136 passed
     (needs Docker; do not run it at the same time as the unit suite).
2. `git grep -nE "ScaleChanged|AutoscaleAxis" -- SemiPlot` prints nothing; on `master` it prints the
   catalogue's scale replacement and the double-click autoscale.
3. `TrendChartCatalogueTests.ARevisedStoredScale_LeavesTheShownPenAlone` fails on `master`, where a
   revised stored pair replaced a shown pen's axis; `TrendChartViewModelTests.InitialScale_RestoresTheStoredPair`
   pins View -> Pen scale -> Restore initial scale.
4. On the demo stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`):
   - set a manual scale on the active pen by clicking its axis; in Edit -> Pens and groups change that
     pen's "Initial scale, min/max" and save: the shown axis stays as you set it;
   - View -> Pen scale -> Restore initial scale sets the axis to the saved pair; View -> Pen scale ->
     Autoscale fits it to the window and keeps fitting as the window moves; both act only on the active
     pen, and the submenu header names it;
   - a click on the axis opens the axis scale panel with the pen's name, unit and bounds; a value that is
     not a number in the OS regional format is refused on the message line and never applied;
   - the pen editor opens at 1180 px wide, its two scale headers wrap onto two lines, and it cannot be
     made narrower than 1100 px.
