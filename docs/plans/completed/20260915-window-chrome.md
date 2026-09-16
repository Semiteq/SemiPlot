# Window chrome: a menu, a navigation bar, a status bar, and one route for every failure

## Overview

The window has no frame and no voice. It opens straight into a toolbar that mixes unrelated
controls, it has no menu at all, and the things that go wrong reach the operator through two
stacked full-width banners that push the chart up — or not at all, because most failures are
written to the log and dropped.

This change gives the window its chrome and gives every failure one route to the operator:

- **A menu bar** — File, View, Help. The home for commands that do not belong on a bar.
- **A navigation bar** — what the toolbar becomes: time navigation only. Jump to Now, Sticky and
  Delta stay; Autoscale, the min and max boxes and Set Limits leave, because they act on one pen's
  Y axis and the axis click editor already sets those two numbers at the axis itself.
- **A status bar** — connection state and the active aggregation layer. Read-only, current state
  only: no counts, no history.
- **A message panel** — one bounded, timestamped list where every failure lands, replacing the two
  banners and giving the failures that are logged and dropped today somewhere to go.
- **A handler under it** — the ReactiveUI exception observer, which is not installed today, so an
  exception raised through ReactiveUI's own machinery reaches the panel instead of the process.

The handler's reach is narrower than "nothing can crash": it covers what ReactiveUI routes through
`IHandleObservableErrors` — a `ReactiveCommand`'s unobserved `ThrownExceptions`, a faulting
`ToProperty`, a binding. It does not cover an `async void` handler, a `Dispatcher.UIThread.Post`
body, or a throw inside a raw `Subscribe`'s `onNext`. Those are guarded where they occur, and this
plan guards the three that exist.

The startup-failure panel stays as it is: it renders before configuration exists and answers a
different question.

## Context (from discovery)

- `MainWindow.axaml` is a seven-row `Grid` (`:21`): toolbar, chart and legend, minimap, then four
  stacked `Border` rows — the empty-catalogue banner (`:58-68`), the archive-connection banner
  (`:70-81`), a status line holding only a pen count (`:83-92`), and the startup-failure panel
  (`:94-115`).
- `MainWindowViewModel.PenCount` is `ChartViewModel?.Pens.Count ?? 0` and is never re-notified
  (`MainWindowViewModel.cs:20-21`). `Pens` is `_pensById.Values` (`Chart/TrendChartViewModel.cs:103`),
  a dictionary view with no change event, so there is no signal to notify from.
- `IsCatalogueEmpty` is `ChartViewModel is not null && PenCount == 0`
  (`MainWindowViewModel.cs:23-26`), documented as "unfinished provisioning shown as a state, not an
  error". It depends on `PenCount` and is raised at `:89`.
- `ArchiveConnectionMessage` is an OAPH over the coordinator's republished state stream, bound once
  and guarded against a second writer (`MainWindowViewModel.cs:44-72`). Its only consumer is the
  banner this change deletes. `App.InitializeServices` binds it at `App.axaml.cs:167`.
- `App.CreateMainWindow` constructs `new MainWindowViewModel { StartupFailure = _startupFailure }`
  on the failure path (`App.axaml.cs:53-56`), which runs before any service provider exists —
  `Configure` returns early on a failed startup (`App.axaml.cs:94-102`).
- `ArchiveConnectionState` is a nullable `Fault` plus `IsConnected`
  (`Core/Data/ArchiveConnectionState.cs:10-15`), and its own documentation states that **every
  subscription's first tick reports `Connected`** (`:8-9`), which `RealtimePoll.Succeed` implements
  (`:240-253`).
- **Faults do not travel as `OnError` on the connection stream.** `IDataProvider` reports connection
  state through `ConnectionFaults` (`Core/Data/IDataProvider.cs:11-15`); the subject only ever
  receives `OnNext` and, on dispose, `OnCompleted` (`PostgresDataProvider.cs:34-38,56,197`);
  `RealtimePoll.ReadOnceAsync` catches everything (`:112-115`) and `Fail` latches `_faultRaised`
  (`:255-280`), so an outage emits one fault and stays silent until success.
- **`RealtimeBatches` is a different story.** `TrendCoordinator.BuildRealtimeBatches` is
  `.Select(BuildRealtimeBatch).Where(...)` (`TrendCoordinator.cs:100-106`); Rx turns a throwing
  projection into `OnError`, which reaches two handler-less subscriptions —
  `TrendCoordinator.Start` (`:73`) and `TrendChartViewModel.cs:76-77`.
- `TrendCoordinator` forwards the connection stream with a bare `Subscribe(_connectionFaults.OnNext)`
  (`:44-46`).
- Two failures are logged and dropped: `Chart/TrendChartViewModel.cs:477-479` (a failed history
  query; the same method then calls `ApplyAxisModel()` and `RequestRedraw()` at `:484-485`, which is
  deliberate recovery documented at `:475-476`) and `Minimap/MinimapViewModel.cs:105-109`.
- `App.axaml.cs:172` is `_ = minimapViewModel.LoadExtentAsync()` — a fire-and-forget with no
  exception continuation, against the project's own rule.
- `ChartHistoryRequestDebouncer.Deliver` carries the shape for a throw inside a consumer: try,
  report, keep the subscription alive (`:159-166`). `ApplyRealtimeBatch`
  (`Chart/TrendChartViewModel.cs:554-559`) has no such guard.
- **No ReactiveUI exception handler is installed.** `App.BuildAvaloniaApp` calls
  `.UseReactiveUI(_ => { })` (`App.axaml.cs:123`) and the lambda is where it goes:
  `UseReactiveUI(AppBuilder, Action<ReactiveUIBuilder>)` and
  `ReactiveUIBuilder.WithExceptionHandler(IObserver<Exception>)`, both present in the installed
  ReactiveUI 23.2.28 and ReactiveUI.Avalonia 12.0.3.
- `ArchiveFailureMapper.Map` is a nine-arm switch (`:28-39`) over `AppSettingsError`,
  `ConfigurationSectionError`, `ConnectionFileError`, `StartupArgumentsError`, `LogFileError`,
  `ArchiveError`, `StartupReadTimedOutError`, `IExceptionalError` and `_`.
  `ConfigurationSectionFailureMapper.Map` is a second entry point (`:11-15`). `Map` yields title,
  detail and remedy; `Describe` concatenates detail and remedy and drops the title.
- `ArchiveFault` has eight members: `Unreachable`, `AccessDenied`, `DatabaseMissing`, `TableMissing`,
  `ShapeUnexpected`, `QueryTimedOut`, `ConnectionLost`, `ReadFailed`
  (`Core/Data/Errors/ArchiveFault.cs`).
- **The layer has no localized name.** `Resources.ToolbarLayerFormat` is the only resx key matching
  `layer` in either set, and its `{0}` is `AggregationLayer.ToString()`, so the Russian window reads
  `Слой: Minute`. `AggregationLayer` has four members (`Core/Trends/AggregationLayer.cs:5-11`).
- `TrendToolbarView.axaml` is one flat `StackPanel` of nine controls (`:13-49`). The min and max
  boxes bind to `double` with no converter and no validation. Its five commands have no
  `canExecute` (`TrendToolbarViewModel.cs:27-35`), and the view model is constructed only when
  `ChartViewModel is not null` (`MainWindowViewModel.cs:92`).
- `Styles/Palette.axaml` carries 33 distinct keys across `Light` and `Dark`. A colour literal in
  AXAML is a defect.
- `SemiPlot.Tests.Unit/TestAppBuilder.cs:20` builds its own Avalonia app with `.UseReactiveUI(_ => { })`
  and never calls `App.BuildAvaloniaApp()`. No test in either project exercises the real install.
- The sibling SemiStep carries the precedents: `MainWindow/RecipeMenuBar.axaml` for the menu (a
  `Separator` between groups inside one menu; a checkable item as `Command` plus
  `IsChecked="{Binding ..., Mode=OneWay}"`; `Help > About` shipped `IsEnabled="False"`),
  `MessageService/MessagePanelViewModel.cs:79-82` for `ShowPanel` as `HasEntries && IsVisible`, and
  `MessageService/ResultReportingExtensions.cs:16-34` for the reporting seam. SemiStep injects its
  panel into every view model that reports; its entries carry no timestamp and no bound.

## Development Approach

- **testing approach**: Regular - code first, tests in the same task.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change

Five project rules bind every task:

- `SemiPlot.Tests.Unit` sets `failSkips`, so no gated test may live there. Tests touching
  `CultureInfo.DefaultThreadCurrentUICulture` or the application theme variant join the
  `process-global-state` collection.
- The build runs under `TreatWarningsAsErrors` with the style analyzers on;
  `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` must both exit 0.
- Every operator-visible string is a resx key in both sets with identical placeholders. Every colour
  resolves to a key in `Styles/Palette.axaml`, in both variants. A key whose last reader is deleted
  is deleted with it.
- Each task leaves the whole solution compiling, which means each task's file list names every file
  that reads what the task changes — including test files.
- **No ReactiveUI object may be constructed before `AppBuilder.Setup()`.** This is a hard ordering
  constraint, not a style preference: `RxState.DefaultExceptionHandler` auto-initialises on first
  read and `InitializeExceptionHandler` then no-ops, so one `ReactiveCommand`, one
  `ObservableAsPropertyHelper` or one read of that property before `Setup()` makes
  `WithExceptionHandler` a silent no-op with no error and no log line. Today the ordering holds:
  `StartupSequence.Run` touches no ReactiveUI type and everything else runs inside
  `.AfterSetup(...)` (`App.axaml.cs:77`). This plan registers `MessagePanelViewModel`, which builds
  two `ReactiveCommand`s, as a DI singleton — so resolution timing becomes load-bearing and is
  stated at the install site and in `overview.md`.

**The anti-fake rule.** A feature is not done when the control is on screen; it is done when a
consumer that is not the declaring code observes the behaviour, and when the test guarding it can
fail. A test that restates a type's cardinality, or asserts a non-nullable field is non-null, looks
like a defence and is not one. Every test named below is specified so that a stated implementation
mistake makes it red; where the only honest check is a grep, it is a grep in Acceptance Evidence
rather than a test pretending to be more.

## Testing Strategy

- **unit tests**: required for every task.
- The message panel, the coalescing, the severity table and the status bar view model are pure
  logic. Plain `[Fact]`.
- Anything needing a realised control — the menu's wiring, the panel's visibility — is an
  `[AvaloniaFact]` that shows the control and reads the resolved value back.
- The exception observer is tested through its own `OnNext`, not through a deliberate crash. That
  the Avalonia builder calls it **cannot be tested in either project**: `TestAppBuilder.cs:20`
  builds its own app and never calls `App.BuildAvaloniaApp()`, and `AppBuilderCompositionTests`
  calls it without `Setup()`. The install is confirmed once, by hand, in the manual list.
- Removal is tested by what survives: the axis limits remain reachable and effective through the
  chart's own editor.

## Acceptance Evidence

Each item is a command with the result it must produce, against the measured before-state.

1. **Failures reach one place, and the panel's visibility is one flag.**
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SemiPlot.Tests.Unit.UI.Messages"`
   passes, covering: `IsVisible` starts closed, the toggle moves it in both directions, and a failure
   the list does not already carry opens it; a failed `Result` handed to the reporting seam produces
   an entry whose title, detail, remedy and severity are the mapper's. The history and extent
   failures each produce an entry too, but their tests live with their view models, so they run as
   `--filter "FullyQualifiedName~AFailedHistoryQueryReachesTheMessagePanelAndStillRedraws"` and
   `--filter "FullyQualifiedName~AFailedExtentQueryReachesTheMessagePanel"`.
   Today (2026-09-15): `Chart/TrendChartViewModel.cs:477-479` and `Minimap/MinimapViewModel.cs:105-109`
   log a warning and return.
   The filter is the namespace, not `MessagePanel`: `ResultReportingTests` carries no `MessagePanel`
   in its fully qualified name.

2. **A repeated failure does not flood the panel.**
   The same filter covers a test issuing the same failure twenty times and asserting one entry with
   a repeat count of twenty and a moving last-seen time; a test issuing two different failures and
   asserting two entries; and a test issuing A, B, A and asserting three entries, because coalescing
   is against the newest entry only. The case is real: during an outage every pan reissues a history
   query (debounce 150 ms, cap 400 ms), so a ten-second drag produces roughly twenty-five identical
   failures.

3. **Every mapper arm assigns a severity, and adding an arm breaks the test.**
   `dotnet test ... --filter "FullyQualifiedName~FailureSeverity"` passes. The test holds an expected
   table — one row per `ArchiveFault` member (eight), one per non-`ArchiveError` arm of
   `ArchiveFailureMapper.Map` (eight), one per `SectionProblem` of `ConfigurationSectionFailureMapper`
   — and asserts the table covers `Enum.GetValues` for each enum it keys on. A new `ArchiveFault`,
   `SectionProblem` or `AppSettingsProblem` member makes it red because the table no longer covers
   the enum. This is the test that bites; asserting a non-nullable `Severity` is non-null would not.

4. **Every severity arm has a production writer.**
   `git grep -n "MessageSeverity\." -- 'SemiPlot/SemiPlot.UI'` shows a non-test writer for each of
   `Error`, `Warning` and `Info`. `Info` is written by the connection-restored entry and by nothing
   else; an arm with no writer is deleted rather than kept.

5. **A connection that was never lost is not "restored".**
   `dotnet test ... --filter "FullyQualifiedName~StatusBar"` covers a test driving `Connected` as the
   first tick and asserting no entry is written, then a fault, then `Connected` and asserting exactly
   one `Info` entry. Without this the panel says "connection restored" at every launch, because the
   first tick of every subscription reports `Connected` (`ArchiveConnectionState.cs:8-9`).

6. **A throw in the connection handler does not silence the stream.**
   The same filter covers a test whose status-bar handler throws once and then succeeds, asserting
   the next state still arrives and the throw was reported. The handler now maps, writes an entry and
   may write the recovery entry; a throw in it propagates into the coordinator's forwarding
   subscription (`TrendCoordinator.cs:44-46`), which has no `onError`, and would kill the connection
   stream for the session.

7. **A throw inside the realtime apply does not kill the chart.**
   `dotnet test ... --filter "FullyQualifiedName~RealtimeApply"` covers a batch that throws once then
   succeeds, asserting the subscription survived and the failure was reported.

8. **A throw inside the ReactiveUI observer's input reaches the panel.**
   `dotnet test ... --filter "FullyQualifiedName~UnhandledErrorObserver"` hands the observer an
   exception and asserts an entry with `Error` severity and a log line. That the builder calls it is
   manual — see item 14.

9. **The status bar reads the layer in the window's language.**
   `dotnet test ... --filter "FullyQualifiedName~StatusBar"` covers a test setting each
   `AggregationLayer` member and asserting the bar's text is the resx value for that member, not
   `ToString()`. Today `Слой: Minute` is what a Russian window shows.

10. **No property survives its only consumer.**
    `git grep -n "ArchiveConnectionMessage\|HasArchiveConnectionMessage\|PenCount\|IsCatalogueEmpty\|ObserveArchiveConnection" -- 'SemiPlot/SemiPlot.UI'`
    returns nothing, and neither does
    `git grep -n "StatusPenCountFormat\|ToolbarAutoscale\|ToolbarMinPlaceholder\|ToolbarMaxPlaceholder\|ToolbarSetLimits\|ToolbarLayerFormat" -- 'SemiPlot/'`.
    Today all of them have exactly one reader, the rows this change deletes.
    The first grep is scoped to `SemiPlot/SemiPlot.UI`: over `SemiPlot/` it also matches
    `ArchiveTemplate.Slice.PenCount` and `RawLayerGenerator.SelectPens(options.PenCount)`, which are
    the seeder's own and stay.

11. **Every menu item does something, and the toggles have one writer.**
    `dotnet test ... --filter "FullyQualifiedName~MenuBar"` passes, walking the `Menu`'s declared
    `Items` — not the visual tree, which does not materialise a submenu until it opens — and
    asserting every leaf has a non-null `Command` or non-empty `Items`. No item is exempt: an item
    with nothing behind it is not added. For each checkable item a second test invokes the command
    and asserts `IsChecked` followed, then writes `IsChecked` on the control and asserts the view
    model's flag did **not** move. The negative half is the one that proves the one-way binding; the
    binding mode itself is not readable from a realised control.

12. **The axis limits survive the bar losing them.**
    `dotnet test ... --filter "FullyQualifiedName~AxisEditorPath_PutsBothBoundsOnTheRenderedAxis"`
    sets a pen's limits through the chart's axis editor path and reads the resulting axis back. It
    lives in `UI/Chart/ChartAxisRegionEditTests.cs`, not in the menu suite.

13. **No colour literal appears.**
    `git grep -nE '#[0-9A-Fa-f]{6,8}\b' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'`
    returns nothing. It returns nothing today too: this is a no-regression guard over the new views,
    not evidence of work done. `git grep -n 'Style Selector="Button.connection-' --
    'SemiPlot/SemiPlot.UI/MainWindow/AppStatusBar.axaml'` returns exactly two, matching the two the
    view model can produce; a bare `connection-` grep over the same file returns four, the two
    selectors plus the two `Classes.` bindings that drive them.

14. **The window works by hand.** Manual, in order:
    1. `dotnet run --project SemiPlot/SemiPlot.AppHost`
    2. the menu bar shows File, View and Help; each opens and every item it contains acts
    3. View toggles the navigation bar, the legend, the minimap and the message panel, and each
       toggle's check state matches what is on screen
    4. the status bar shows the layer in Russian, and it changes when the chart is zoomed across a
       layer boundary
    5. no "connection restored" entry appears at launch
    6. stop the bench container: the indicator turns to the fault state and one entry appears,
       without the chart moving
    7. drag the chart for ten seconds while it is down: the entry's repeat count rises and no second
       entry appears
    8. restart the container: the indicator returns and one `Info` entry says so
    9. with a temporary throw injected into a `ReactiveCommand` body, the exception appears in the
       panel rather than terminating the process. This is the only way to confirm the builder
       installed the observer, because no test in either project exercises `App.BuildAvaloniaApp`

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with the plus prefix
- document blockers with the warning prefix
- keep the plan in sync with the work actually done

## Solution Overview

**One route, and the mapper is on it.** `ArchiveFailureMapper` already turns any `IError` into a
title, a detail and a remedy in both languages, and it is the one place a remedy is written. The
panel takes its text from `Map`, not from `Describe`: the title is the row's text and the coalescing
key, and `Describe` drops it. The mapper, `ArchiveFailureView` and `ConfigurationSectionFailureMapper`
move into `Messages/`, because they now serve two windows rather than one.

A `Result` carrying several errors becomes one entry built from `Errors[0]`, the same choice
`App.Configure` already makes for the startup window (`App.axaml.cs:96`). One entry per error would
defeat the coalescing.

The cost, accepted rather than hidden: `MapUnknown` and `MapThrown` surface `error.Message` and
`exception.Message` verbatim, which for a programming error is English developer text in a Russian
window. That is the right trade for an error class the operator cannot act on, and `ui-text.md`
records it.

**Severity is decided in the mapper, not at the call site,** so a future error type gets one from
the `_ => MapUnknown` arm without anyone remembering. The axis is whether the operator must act.
For `ArchiveFault`: `AccessDenied`, `TableMissing`, `DatabaseMissing` and `ShapeUnexpected` are
`Error`; `Unreachable`, `ConnectionLost`, `QueryTimedOut` and `ReadFailed` are `Warning`, because
the poll loop retries by itself. Configuration and argument failures are `Error`; they are only
reachable at startup, where the failure window shows them anyway. `Info` exists because the
connection-restored entry writes it, and for no other reason.

**Coalescing is against the most recent entry only.** Two identical failures in a row become one
entry with a repeat count and a moving last-seen time; a different failure in between starts a new
one. The key is the structural equality of the mapped view, which needs no new field on any error
type. The flood this exists for is not the connection — `RealtimePoll.Fail` latches `_faultRaised`
and emits one fault per outage — it is the history query, reissued on every pan.

**The panel is a singleton, injected.** Registered in `AddUi`, taken as a constructor parameter by
`MainWindowViewModel`, and passed by hand into the chart and minimap view models in
`App.InitializeServices` — the pattern the UI scheduler already follows. The startup-failure path
has no container, so it constructs its own panel: `MessagePanelViewModel` has no dependencies, and
that window never writes to it.

**The global observer holds a factory and marshals to the UI thread.** It is installed inside
`UseReactiveUI(builder => builder.WithExceptionHandler(...))` in `App.BuildAvaloniaApp`, which runs
before any service provider exists, so it resolves the panel on first use. ReactiveUI raises on the
failing scheduler and the panel mutates an `ObservableCollection`, so the observer posts through
`AvaloniaScheduler.Instance`; an off-thread collection edit does not reliably throw, it silently
drops. It logs whether or not a panel has been resolved, so a failure before the window exists is
still recorded.

**Rx faults: the claim is about the connection stream only.** A provider reports its connection
through `ConnectionFaults`, which never errors, and its poll loop retries on its own. Three other
paths do need guarding and get it: a throw inside the connection-state *handler*, which now maps and
writes entries and would kill the coordinator's forwarding subscription; a throw inside
`ApplyRealtimeBatch`; and the unobserved `LoadExtentAsync` task at `App.axaml.cs:172`. Each gets the
try-report-continue shape the history debouncer already uses.

**The status bar holds current state and nothing else.** Connection and layer, the layer named from
resx rather than from `ToString()`. The pen count leaves: it is stale by construction, `Pens` has no
change signal to fix it with, and the legend lists the pens. `IsCatalogueEmpty` leaves with it — the
empty catalogue is a state, not a clearable log entry, so it becomes the chart area's empty state,
which is the thing that is empty.

**The menu ships only what acts.** No disabled placeholders: an item that renders and does nothing
is the shape this plan is written to avoid, and a walking test that exempts one is worse than no
test. Help carries About because About is implemented here. There is no Edit menu: nothing it would
hold exists yet, and Settings arrives with the settings window. There are no accelerators either —
the only candidate was `Alt+F4` on Exit, which the window manager already handles, so no
`Window.KeyBindings` entry is added and there is no two-halves-in-step problem to guard.

**No `canExecute` is added.** The three surviving commands have no state in which they are invalid:
the view model exists only when a chart exists (`MainWindowViewModel.cs:92`), and Jump to Now,
Sticky and Delta are meaningful whenever it does. A guard with no production consumer is surface,
not safety.

**No icons.** The bar reads with text captions today and no icon source is in the repository.
Grouping and the separator carry the structure; icons arrive when someone brings the assets.

## Technical Details

### The window's rows

`MainWindow.axaml` is `RowDefinitions="Auto,*,Auto,Auto,Auto,Auto"` after task 4, and ends as
`RowDefinitions="Auto,Auto,*,Auto,Auto,Auto,Auto"` once task 8 inserts the menu bar at row 0 and
moves every row below it down one:

| Row | Content | Collapses |
| --- | --- | --- |
| 0 | Menu bar | no |
| 1 | Navigation bar | `IsNavigationBarVisible` |
| 2 | Chart and legend | the legend column on `IsLegendVisible` |
| 3 | Minimap | `IsMinimapVisible` |
| 4 | Message panel | `MessagePanel.IsVisible` |
| 5 | Status bar | `HasStartupFailure` |
| 6 | Startup-failure panel | `HasStartupFailure` |

Every task that changes this grid keeps this table true.

### Messages

```
SemiPlot.UI/Messages/MessageSeverity.cs        Error | Warning | Info
SemiPlot.UI/Messages/MessageEntry.cs           view, first seen, last seen, repeat count
SemiPlot.UI/Messages/MessagePanelViewModel.cs
SemiPlot.UI/Messages/MessagePanelView.axaml
SemiPlot.UI/Messages/ResultReporting.cs
SemiPlot.UI/Messages/ArchiveFailureMapper.cs                 moved from MainWindow/
SemiPlot.UI/Messages/ArchiveFailureView.cs                   moved, gains Severity
SemiPlot.UI/Messages/ConfigurationSectionFailureMapper.cs    moved from MainWindow/
SemiPlot.UI/Messages/UnhandledErrorObserver.cs
```

`MessagePanelViewModel` holds an `ObservableCollection<MessageEntry>` and exposes:

```csharp
public bool IsVisible { get; }                 // the row, the View item and the status bar all read it
public void Report(ArchiveFailureView view);   // a failure not already on top opens the panel
public ReactiveCommand<Unit, Unit> ClearCommand { get; }
public ReactiveCommand<Unit, Unit> ToggleCommand { get; }   // the operator's writer of IsVisible
```

The row starts closed and every reader binds `IsVisible` one way, so the check state beside the View
item and the row on screen cannot disagree, and one click always moves both. The panel view model is
the only writer, through two transitions: the toggle, and a `Report` whose view is not already the
newest entry. A repeat of the entry on top does not reopen a panel the operator closed, or an outage
reissuing one history query per pan would reopen it twenty-five times over a ten-second drag.

`Report` compares the incoming view with the newest entry; equal means increment and restamp,
different means prepend. The collection is capped at 200 entries, oldest dropped — this viewer runs
for weeks, and SemiStep's unbounded untimestamped list is the one part of its shape that does not
transfer.

`ResultReporting` is two extension methods over the panel, each writing the entry and one log line:

```csharp
public static void ReportFailure(this MessagePanelViewModel panel, IResultBase result);
public static void ReportFailure(this MessagePanelViewModel panel, IError error);
```

Both are extensions over the panel, which the caller holds. That is SemiStep's shape and it is not a
seam that hides the panel from the caller; calling it one would be a claim the code does not support.

### The exception observer

An `IObserver<Exception>` holding a `Func<MessagePanelViewModel?>` and a scheduler. `OnNext` logs at
`Error`, then schedules the report onto the UI scheduler if a panel has been resolved. It lives as a
`private static readonly` field on `App` with its process lifetime stated in a comment, which is the
declared-lifetime exception the Avalonia rules allow for a process-wide hook; nothing else in the
tree may hold mutable static state.

Installed at `App.axaml.cs:123`:

```csharp
.UseReactiveUI(builder => builder.WithExceptionHandler(_unhandledErrors))
```

The ordering constraint in Development Approach is repeated as a comment at this line, because a
`ReactiveCommand` built before `Setup()` disables this call with no signal.

### The status bar

One row of `Auto` columns separated by hairlines: the connection indicator, then the active layer.
The indicator is a `Button` carrying one of two classes, `connection-ok` and `connection-fault`,
each backed by a palette key in both variants. Clicking it invokes the panel's `ToggleCommand` — the
same command the View menu invokes, one command, two callers, one writer.

`AppStatusBarViewModel` takes over the bind-once subscription to the coordinator's connection
stream, with the single-writer guard moving with it, and its handler is wrapped so a throw is
reported and the stream survives. It writes the fault entry, and the `Info` recovery entry only when
a fault was seen since the last recovery — the first tick of every subscription is `Connected`.

The layer name comes from a private `LayerNameOf` switch on the bar itself, mapping each
`AggregationLayer` member onto one of four new resx keys in both sets.

### The navigation bar

`Toolbar/` becomes `Navigation/`. Three controls plus the delta readout, grouped with a 1 px
separator between the time group and the tools group.

`AutoscaleActiveAxisCommand`, `SetActiveAxisLimitsCommand`, `ManualMin` and `ManualMax` are deleted
with the four controls that used them; the layer label goes to the status bar; and the five resx
keys that lose their last reader go with them.

### The menu

| Menu | Items |
| --- | --- |
| File | Exit |
| View | Navigation bar; Legend; Minimap; `Separator`; Message panel — each `ToggleType="CheckBox"` |
| Help | About |

Every caption is a resx key in both sets. About is a small modal: application name, assembly version,
and the configuration directory the run read — the same string `Program.LogStart` writes.

## Implementation Steps

### Task 1: Move the failure mappers into Messages and give them a severity

**Files:**
- Move: `SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`, `ArchiveFailureView.cs`, `ConfigurationSectionFailureMapper.cs` to `Messages/`
- Create: `SemiPlot/SemiPlot.UI/Messages/MessageSeverity.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`, `MainWindow/MainWindowViewModel.cs`
- Move and modify: `SemiPlot.Tests.Unit/UI/MainWindow/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot.Tests.Unit/UI/MainWindow/ArchiveStatusBannerTests.cs`, `MainWindowViewModelTests.cs`, `UI/Localization/ResourcesTests.cs`
- Create: `SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`

- [x] move the three files and their namespace, updating every reference including the test files above
- [x] add `MessageSeverity` and a non-nullable `Severity` on `ArchiveFailureView`
- [x] assign the severity in every arm of both mappers, `MapUnknown` supplying the default
- [x] write the severity table test: one row per `ArchiveFault`, per non-`ArchiveError` `Map` arm and per `SectionProblem`, asserting the table covers `Enum.GetValues` for each enum, so a new member makes it red
- [x] write a test asserting the recoverable faults are `Warning` and the ones needing the operator are `Error`
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 2

### Task 2: Add the message panel with coalescing

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Messages/MessageEntry.cs`, `MessagePanelViewModel.cs`, `ResultReporting.cs`
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`
- Create: `SemiPlot.Tests.Unit/UI/Messages/MessagePanelViewModelTests.cs`, `ResultReportingTests.cs`
- Modify: `SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`

- [x] add `MessageEntry` with the view, first seen, last seen and repeat count
- [x] add the panel view model: the collection, `IsVisible` closed at launch with the toggle and a new entry as its two transitions, the 200-entry cap, `ClearCommand`
- [x] implement `Report` coalescing against the newest entry by structural equality of the view
- [x] add `ResultReporting`, mapping `Errors[0]` for a multi-error result and writing one log line
- [x] register the panel as a singleton in `AddUi`
- [x] write tests for coalescing (twenty identical, two different, A-B-A), the cap, and `IsVisible` over the toggle and over a new entry
- [x] write tests for both reporting overloads, asserting title, detail, remedy and severity come from the mapper
- [x] run tests - must pass before task 3

### Task 3: Install the ReactiveUI exception observer

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Messages/UnhandledErrorObserver.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Create: `SemiPlot.Tests.Unit/UI/Messages/UnhandledErrorObserverTests.cs`

- [x] add the observer with a panel factory and a scheduler, logging at `Error` and scheduling the report when a panel exists
- [x] hold it as a `private static readonly` field on `App` with its process lifetime stated, and install it in the builder lambda at `App.axaml.cs:123`
- [x] repeat the no-ReactiveUI-before-`Setup()` constraint as a comment at the install site
- [x] write a test handing the observer an exception and asserting the report reaches a stub panel on the scheduler
- [x] write a test asserting it logs when no panel has been resolved yet
- [x] run tests - must pass before task 4

### Task 4: Show the panel, retire the two banners, move the empty state to the chart

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Messages/MessagePanelView.axaml` and `.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, both resx sets
- Modify: `SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`, `ArchiveStatusBannerTests.cs`, `UI/Startup/EmptyCatalogueStartupTests.cs`

- [x] add the panel view: a scrollable list, a severity dot, wrapped title and detail, the repeat count when above one, a capped height, its own empty-state line, and the row hidden when `IsVisible` is false
- [x] delete the empty-catalogue and archive-connection borders (`MainWindow.axaml:58-81`) and keep the row table above true
- [x] delete `IsCatalogueEmpty` and its `RaisePropertyChanged` (`MainWindowViewModel.cs:23-26,89`); the chart area gains the empty state instead, driven by its own pen collection (`TrendChartViewModel.HasNoPens`)
- [x] take the panel as a constructor parameter on `MainWindowViewModel`, and construct one directly on the startup-failure path at `App.axaml.cs:53-56`, which has no container
- [x] add the severity-dot colours to both palette variants and the new strings to both resx sets
- [x] write an `[AvaloniaFact]` asserting the row is hidden at launch, opens on the first failure and closes on the toggle
- [x] write a test asserting the chart's empty state appears with zero pens and not with one
- [x] run tests - must pass before task 5

### Task 5: Route the dropped failures and guard the three unguarded paths

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`, `Minimap/MinimapViewModel.cs`, `App.axaml.cs`
- Modify: `SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`, `UI/Minimap/MinimapViewModelTests.cs`
- Modify, for the two constructor signatures: `SemiPlot.Tests.Unit/UI/Chart/ChartAxisRegionEditTests.cs`,
  `ChartGapRenderTests.cs`, `ChartPointerInputTests.cs`, `TrendChartViewTests.cs`,
  `UI/Legend/TrendLegendViewModelTests.cs`, `UI/MainWindow/MainWindowViewModelTests.cs`,
  `UI/Minimap/MinimapPointerInputTests.cs`, `UI/Toolbar/ToolbarTestBuilder.cs`,
  `SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`, `LiveEdgeArchiveJourneyTests.cs`

- [x] report the failed history query (`TrendChartViewModel.cs:477-479`) through the panel, keeping the log line and the `ApplyAxisModel`/`RequestRedraw` recovery at `:484-485`
- [x] report the failed extent query (`MinimapViewModel.cs:105-109`) the same way
- [x] wrap `ApplyRealtimeBatch` in the try-report-continue shape `ChartHistoryRequestDebouncer.Deliver:159-166` uses
- [x] give `_ = minimapViewModel.LoadExtentAsync()` (`App.axaml.cs:172`) a continuation that logs and reports
- [x] write a test whose history query fails, asserting one entry with the mapper's text and that the redraw still happened
- [x] write a test whose realtime batch throws once then succeeds, asserting the subscription survived and the failure was reported
- [x] run tests - must pass before task 6

### Task 6: Add the status bar

**Files:**
- Create: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBar.axaml` and `.axaml.cs`, `AppStatusBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`, `App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, both resx sets
- Modify: `SemiPlot.Tests.Unit/UI/MainWindow/ArchiveStatusBannerTests.cs`, `MainWindowViewModelTests.cs`, `UI/Startup/EmptyCatalogueStartupTests.cs`, `UI/Localization/ResourcesTests.cs`
- Create: `SemiPlot.Tests.Unit/UI/MainWindow/AppStatusBarViewModelTests.cs`, `AppStatusBarViewTests.cs`

- [x] add the bar's private `LayerNameOf` switch mapping each `AggregationLayer` to a resx key, with four new keys in both sets
- [x] add the status bar view model holding the connection state and the layer name, taking over the bind-once subscription with its guard, and wrapping its handler so a throw is reported and the stream survives
- [x] write the fault entry, and the `Info` recovery entry only when a fault was seen since the last recovery
- [x] delete `ObserveArchiveConnection`, the `ArchiveConnectionMessage` OAPH, `HasArchiveConnectionMessage` and `PenCount`, update `App.axaml.cs:167`, and delete `StatusPenCountFormat` from both resx sets (the bind-once entry point is now `AppStatusBarViewModel.TrackArchiveConnection`, renamed so the item-10 grep stays clean)
- [x] add the bar view with the two connection classes and their palette keys in both variants
- [x] write tests for the first-tick suppression, the single recovery entry, the handler guard, and the layer text being the resx value for each member
- [x] run tests - must pass before task 7

### Task 7: Turn the toolbar into the navigation bar

**Files:**
- Rename: `Toolbar/TrendToolbarView.axaml` and `TrendToolbarViewModel.cs` to `Navigation/NavigationBarView.axaml` and `NavigationBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`, `MainWindowViewModel.cs`, both resx sets
- Rename: `SemiPlot.Tests.Unit/UI/Toolbar/` to `UI/Navigation/`, its three files to `NavigationBarViewModelTests.cs`, `NavigationBarTestBuilder.cs`, `NavigationBarViewTests.cs`
- Modify: `SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`, `UI/MainWindow/MainWindowViewModelTests.cs`, `UI/Startup/EmptyCatalogueStartupTests.cs`, `UI/Chart/ChartAxisRegionEditTests.cs`

- [x] delete the two axis commands, `ManualMin`, `ManualMax` and the four controls that used them
- [x] delete the layer label, now owned by the status bar
- [x] delete `ToolbarAutoscale`, `ToolbarMinPlaceholder`, `ToolbarMaxPlaceholder`, `ToolbarSetLimits` and `ToolbarLayerFormat` from both resx sets and from `ResourcesTests.cs`
- [x] group the remaining controls with a 1 px separator
- [x] write a test asserting the axis limits are still reachable and effective through the chart's editor path (`ChartAxisRegionEditTests.AxisEditorPath_PutsBothBoundsOnTheRenderedAxis`)
- [x] run tests - must pass before task 8

### Task 8: Add the menu bar and the About dialog

**Files:**
- Create: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml` and `.axaml.cs`, `AboutDialog.axaml` and `.axaml.cs`, `AboutInfo.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`, `MainWindow.axaml.cs`, `MainWindowViewModel.cs`, both resx sets
- Create: `SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`
- Modify: `SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`

- [x] add the menu with File, View and Help and only the items that act
- [x] add the panel-visibility flags and their commands, each command the single writer of its flag, the message panel's toggle being the panel's own `ToggleCommand`
- [x] add the About dialog showing the name, the assembly version and the configuration directory
- [x] write the walking test over the declared `Items`: every leaf has a command or children, no exemption
- [x] write a test per toggle: invoke the command and assert the check state followed, then write `IsChecked` on the control and assert the flag did not move
- [x] run tests - must pass before task 9

### Task 9: Verify acceptance criteria

- [x] run every command in Acceptance Evidence and record the result
- [x] run the manual list end to end, including the launch check, the outage, the ten-second drag, the recovery and the injected command throw
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files; both exit 0
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot.slnx`

Measured on 2026-09-15. Items 1 to 13 pass as written, after the three corrections folded into the
text above. `dotnet build SemiPlot.slnx` and `dotnet format SemiPlot.slnx --verify-no-changes` exit
0; `dotnet terse` exits 0 over the 47 touched `.cs` files; `dotnet test SemiPlot.slnx` is 927 unit
and 85 container tests, none failed, none skipped.

The manual list ran against `dotnet run --project SemiPlot/SemiPlot.AppHost` with the workstation
locked, so the window was driven through UI Automation and posted window messages rather than by
hand, and read back through the automation tree rather than by eye. Every step of item 14 was
driven that way and held:

| Step | Measured |
| --- | --- |
| 14.2 | `Файл`, `Вид` and `Справка` open; `Выход` closes the viewer, `О программе` opens the dialog naming SemiPlot, version `0.0.0` and `%TEMP%\SemiPlot\ConfigFiles`, the same directory the log's first line names |
| 14.3 | each of the four `Вид` items hides and shows its row. The message-panel row was re-shaped after this run; the review follow-up below states what shipped, and `AppMenuBarTests` and `MainWindowViewModelTests` pin it |
| 14.4 | `Слой: сырой`, and `Слой: минутный` after the chart zooms out |
| 14.5 | the panel row is absent at launch |
| 14.6 | `docker stop`: the indicator reads `Архив не отвечает` and one entry appears, the layer unchanged |
| 14.7 | repeated history failures during the outage become one entry reading `повторов: 2`, not two entries |
| 14.8 | `docker start`: the indicator returns to `Архив на связи` and one `Info` entry says so |
| 14.9 | a throw injected into `JumpToNowCommand` reached the panel; the process stayed up and the log carries one `[ERR] UnhandledErrorObserver` line with the stack |

One half of 14.3 is attested by test rather than by eye: Avalonia's `MenuItem` automation peer
exposes no `TogglePattern`, so the check mark beside each `Вид` item was not read from the running
window. `AppMenuBarTests` pins it, one test per toggle, including the negative half.

Stopping the stand with a console Ctrl+C removed `%TEMP%\SemiPlot\ConfigFiles` and left
`%TEMP%\SemiPlot\Logs` in place, so `ApplicationStopping` ran.

- [+] the panel stamped each entry in UTC while the window's clock is local, three hours apart on
  this machine. `MessagePanelViewModel.Report` now stamps with `TimeProvider.GetLocalNow()`, so the
  entry's `DateTimeOffset` carries the operator's zone and `MessagePanelView.axaml` formats
  `LastSeen` as it stands. The zone comes from the injected clock rather than `TimeZoneInfo.Local`,
  so `MessagePanelViewTests` pins the rendered row on a UTC runner too.
- [!] `ArchiveFailureMapper.MapThrown` titles a runtime ReactiveUI exception
  `Запуск прервался неожиданно`, which reads as a startup failure now that the mapper serves the
  message panel too. Task 10 owns the `ui-text.md` wording.

### Task 10: Update documentation

**Files:**
- Modify: `docs/architecture/trend-interaction.md`, `charting.md`, `overview.md`, `ui-text.md`, `data-integration.md`, `CLAUDE.md`
- Modify, carried from task 9: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify, found false while writing the six above: `docs/architecture/ui-theme.md`, `testing-strategy.md`, `postgres-topology.md`, `trend-feature-spec.md`

- [x] `trend-interaction.md:76` and `:127`: the duplication between the axis editor and the toolbar is gone, not deliberate
- [x] `charting.md:94` and `:197`: the layer indicator is read-only state in the status bar, not a selector
- [x] `overview.md`: the window's row table, the exception observer as the last-resort route, and the no-ReactiveUI-before-`Setup()` ordering constraint
- [x] `ui-text.md`: the panel takes its text from `ArchiveFailureMapper.Map`; `MapUnknown` and `MapThrown` surface developer text for programming errors; the layer is named from resx
- [x] `data-integration.md`: failures no longer stop at the log; name the panel as their destination
- [x] `CLAUDE.md`: the single-writer rule for checkable menu items, the rule that a menu item without a command is not added, and the correction that `RxApp` no longer exists in the installed ReactiveUI
- [x] move this plan to `docs/plans/completed/` — not done here: archiving belongs to the delivery step, which moves the file once the branch ships. The plan file is left in place.

The `[!]` item above is resolved by rewording rather than by a second route: `FailureThrownTitle` is
now "Unexpected failure" / "Непредвиденный сбой" and `FailureThrownDetail` names the exception instead of the
startup sequence, so the same arm is true in the startup window and in the message panel. No test
asserted the old wording; `ui-text.md` records the rule that no mapper title may name a phase.

Four documents outside the list carried statements the branch made false and were corrected with it:
`ui-theme.md` (the palette rows naming the deleted limit boxes and the toolbar), `testing-strategy.md`
(`TrendToolbarViewTests`, renamed), `postgres-topology.md` (the banner row) and `trend-feature-spec.md`
(the layer's location).

## Post-Completion

*No checkboxes: these need action outside this codebase.*

**What this plan deliberately leaves out**

- The settings window and the palette editor. The Edit menu arrives with them.
- The pen bar — visibility toggles, per-pen colour, the rest of the pen surface.
- Time-width presets and an absolute window start (#71), and the marker mode (#77). Both land on
  the navigation bar once they exist; its grouping leaves room.
- Icons on the navigation bar, until an icon source exists in the repository.

**Known consequences to watch**

- Removing the toolbar's axis group makes the axis click editor the only way to set a pen's limits
  until #65 stores a range per pen. #86 asks whether that editor is the right mechanism; this plan
  does not answer it, it only stops there being three answers.
- The panel is a process-wide singleton holding operator-visible text. Anything reporting into it
  from a background thread must go through the UI scheduler, as the exception observer does.
- The ReactiveUI ordering constraint is held by a comment and a document, not by a gate. The one
  thing that would break it — resolving a ReactiveUI-bearing service before `Setup()` — is exactly
  what a future composition-root change might do without noticing.

**Review follow-up (2026-09-15)**

The panel's visibility went through two wrong shapes before the one that shipped, and the text above
now describes the third.

`ShowPanel` (`HasEntries && IsVisible`) made the View item write one property and read another back,
so the first click on an empty panel disarmed it with nothing on screen moving and the item still
unticked. Deleting `ShowPanel` fixed that and left `IsVisible` defaulting to true, which made the row
permanent chrome: about 54 px of "Messages / no messages" on every launch of a session that never
fails, on the startup-failure window as well, and a status-bar indicator whose documented job is to
open the panel closing it on the first click instead.

What shipped: the row, the View item's check state and the indicator all bind `IsVisible`; it starts
closed; the toggle and a `Report` carrying a view that is not already the newest entry are its two
transitions. One click always moves the row, the check state always matches it, a session that never
fails never sees it, and a failure never lands off screen. The startup-failure window needs no guard
of its own — nothing reports into that window's panel, so the row stays closed —
and `MainWindowViewModelTests.MainWindow_WithAStartupFailure_ShowsTheFailureAndNothingElseBelowTheChart`
reads every row of it rather than only the status bar.

Item 1's `HasEntries && IsVisible` and item 14.3's "with no entry the item is unticked and the row is
absent" are superseded by that, and the row table, the API block and the task 2 and 4 checkboxes are
rewritten to match.

Four defects around the same change were fixed with it:

- `TrendChartView`'s empty-catalogue message bound `HasNoPens` against a null chart view model on the
  startup-failure path, where an unset binding falls back to `IsVisible` true. The binding carries
  `FallbackValue=False`, which the same test reads off the realised window.
- `TrendCoordinator.RealtimeBatches` handed a terminal provider failure to both its keep-alive and
  the chart, which reported it twice into one coalesced entry reading as two occurrences. The
  pipeline now `Catch`es a terminal failure into `RealtimeFailures` and completes, so the batch
  stream never faults and the failure is reported once. `_realtimeFailures` is a
  `Subject.Synchronize`d subject, written from the data scheduler and completed from the UI thread.
- `ResultReporting.ReportFailure` ran the panel edit and the log line as one statement pair, so
  whichever sink threw cost the operator the other. The panel edit runs first and the log line runs
  from a `finally`, so each sink gets the entry whether or not the other one threw. A handler that
  runs detached, where an escape would end an Rx stream or a dispatcher job, carries a catch-all over
  its whole body ending in `TryReportFailure`: the chart's realtime and history handlers, the status
  bar, the minimap's extent apply, the `LoadExtentAsync` continuation and the ReactiveUI observer.
- `UnhandledErrorObserver` reported straight into the panel from a dispatcher job, where a throw is
  unhandled and ends the process, and bypassed the `Coalesces` demotion that keeps a per-redraw
  failure from rolling the five capped log files away. It reports through `TryReportFailure` on the
  UI scheduler and logs directly only when no panel exists yet.

## Verify it yourself

**Executed by exec:**

- branch: window-chrome
- commits: 19, 88 files changed

Measured on 2026-09-15 from a clean tree, each command run from the repository root:

```powershell
dotnet build SemiPlot.slnx                          # 0 warnings, 0 errors
dotnet test SemiPlot.slnx                           # 960 unit, 85 container, 0 failed, 0 skipped
dotnet format SemiPlot.slnx --verify-no-changes     # exit 0
dotnet terse $(git diff --name-only --diff-filter=d master...HEAD -- '*.cs')   # exit 0, 59 files
```

Acceptance items 1 to 13 are the runnable set above; each one's filter is in `## Acceptance
Evidence` and each passes. Three checks worth repeating by hand, because they are the ones a
green suite can hide:

1. **The stamp test is host-independent.** Change `Messages/MessagePanelView.axaml:92` from
   `{Binding LastSeen, ...}` to `{Binding LastSeen.UtcDateTime, ...}` and run
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TheEntryRow_ShowsTheLocalTime"`.
   It fails with `Expected ... to contain "04:30:15"` and the row reading `21:30:15`. Revert and it
   passes. The expected value comes from the clock's fixed +07 zone, not from `TimeZoneInfo.Local`,
   so a UTC runner sees the same red.
2. **Every palette key a view names is defined.** The `App*` keys used across
   `SemiPlot.UI/**/*.axaml` and the keys declared in `Styles/Palette.axaml` are the same set of 12,
   with none unused on either side.
3. **The resource sets agree.** `Resources.resx` and `Resources.ru.resx` carry the same 112 keys,
   and every one of them has a reader in `SemiPlot.UI` or `SemiPlot.Tests.Unit`.

Item 14, the by-hand run of `dotnet run --project SemiPlot/SemiPlot.AppHost`, is the one gate no
command replaces: step 9 in particular, injecting a throw into a `ReactiveCommand` body, is the only
evidence that `BuildAvaloniaApp` installed the exception observer, because neither test project
exercises `App.BuildAvaloniaApp`.
