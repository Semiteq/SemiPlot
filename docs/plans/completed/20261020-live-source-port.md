# Live-source port

## Overview

- The live edge reads through a port of its own, `ILiveSource` in `SemiPlot.Core/Data`, instead of
  through `IDataProvider.Subscribe`. The port has one member: a cold `IObservable<IReadOnlyList<Sample>>`
  of samples and breaks for a pen set. Its contract states that the stream never completes and never
  faults. `TrendCoordinator` turns an `OnError` into one `RealtimeFailures` entry and a live edge that stays
  empty for the rest of the window's life: the `Catch` swaps in `Observable.Empty`, which completes
  `RealtimeBatches` and drops the subscription to the pen set, and nothing rebuilds it
  (`TrendCoordinator.cs:38,42-44,97-115`). An `OnCompleted` is not caught at all: `Switch` idles on a
  completed inner stream, so the live edge goes silent with no entry until the next `SetPens`.
- The archive poll becomes the port's first implementation, `PostgresLiveSource` in
  `SemiPlot.DataSource.Postgres`. `RealtimePoll` and `ArchiveStatements` do not change, and neither does
  the poll's cadence: one tick, then a wait of `poll_interval_ms` on the data scheduler.
- `IDataProvider` loses `Subscribe` and keeps the history, extent, days and catalogue reads.
  `TrendCoordinator` takes both interfaces; `TrendWindow.Build` resolves both from the container.
- Nothing the operator sees changes. The only output that differs is the source context of the poll's
  own log lines, which moves from `PostgresDataProvider` to `PostgresLiveSource` (Technical Details).
- This prepares the roadmap's `opc-live-source` slice: an `OpcUaLiveSource` in a new
  `SemiPlot.DataSource.OpcUa` project implements the same port, and `PostgresLiveSource` is deleted with
  the poll.

## Context (from discovery)

- Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice live-source-port
- Verified at `85e91eb` (the tip of `archive-state-from-catalogue`, PR #142); `master` at `54f455e` carries
  the same tree (`git diff 85e91eb 54f455e` prints nothing). Line numbers below are at `85e91eb`. Paths are
  under `SemiPlot/` unless they start with `docs/` or name `AGENTS.md`.
- The member that moves: `IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds)` with its
  "Cold per call" comment (`SemiPlot.Core/Data/IDataProvider.cs:9-10`). `IDataProvider` holds nothing else
  of the live edge (`:12-28`).
- `Sample(int PenId, DateTime TimestampUtc, double? Value)`, a null `Value` is a break
  (`SemiPlot.Core/Trends/Sample.cs:3-4`).
- The one production implementation: `PostgresDataProvider.Subscribe` wraps `PollAsync` in
  `Observable.Create` over `_scheduler.ScheduleAsync` (`SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:76-87`).
  `PollAsync` builds one `RealtimePoll` per subscription, emits non-empty tick results, sleeps
  `_settings.PollInterval` on `_scheduler`, and ends silently on cancellation (`:340-377`). It never calls
  `OnError` or `OnCompleted`. `_settings` and `_scheduler` (`:45-46,69-70`) have no other reader
  (`git grep -n -E "_settings|_scheduler"` on the file prints `:45,46,69,70,86,370`). The class summary
  names `Subscribe` in a `cref` (`:21-25`), and the `Subscribe` summary names
  `PostgresConnectionSettings.PollInterval` in a `cref` (`:76-80`). `System.Reactive.Concurrency`,
  `System.Reactive.Linq` (`:4-5`) and `SemiPlot.DataSource.Postgres.Configuration` (`:17`) serve only the
  live edge: `PostgresConnectionSettings` appears only at `:45,60,78`.
- `RealtimePoll(dataSource, timeConverter, exceptionMapper, penIds, ILogger logger)`
  (`SemiPlot.DataSource.Postgres/RealtimePoll.cs:15-20`). `ReadOnceAsync` catches every failure but
  `OperationCanceledException`, logs it with `ConsecutiveFailures` and returns no sample (`:43-70,198-209`).
- The registration: `AddPostgresData` registers `IDataProvider` by factory with `settings` and `IScheduler`
  among its arguments (`SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:37-45`), and
  the other ports beside it (`:46-57`). The bare `IScheduler` is `DefaultScheduler.Instance` (`:29`). Every
  sibling class registered there is public with an explicit `internal` constructor
  (`PostgresDataProvider.cs:56-74`, `PostgresMarkerStore.cs:20-28`), and `ArchiveExceptionMapper` is
  `internal` (`ArchiveExceptionMapper.cs:17`).
- The one consumer: `TrendCoordinator` holds `_dataProvider` (`SemiPlot.UI/Bridge/TrendCoordinator.cs:16`),
  takes it first in its constructor (`:28-34`), and subscribes through
  `_penIds.Select(_dataProvider.Subscribe).Switch()` (`:97-99`). A terminal failure is caught after
  `Switch` and ahead of `Publish`, reported once on `RealtimeFailures`, and ends the batch stream
  (`:104,110-115`). The comment at `:25-27` says "a provider silently drops" unknown ids; the summaries at
  `:42-52` speak of "a provider that ends the stream". The history, extent and days pass-throughs stay on
  `_dataProvider` (`:71-93`).
- `RealtimeFailures` has one reader, `TrendChartViewModel` (`SemiPlot.UI/Chart/TrendChartViewModel.cs:83`).
- The window: `TrendWindow.Build` resolves `IDataProvider` (`SemiPlot.UI/MainWindow/TrendWindow.cs:72`) and
  builds the coordinator over it (`:80`); `PenCatalogueSync` takes the same provider (`:100-105`).
- `new TrendCoordinator(` sites: `TrendWindow.cs:80`; `SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs:97,121,214`;
  `SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs:60`; `SemiPlot.Tests.Unit/UI/Minimap/MinimapStand.cs:37`;
  `SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs:179-180`;
  `SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs:70-71`.
- Direct callers of `IDataProvider.Subscribe` in tests:
  `SemiPlot.Tests.Integration/RealtimeEmptyArchiveTests.cs:76-78`,
  `SemiPlot.Tests.Integration/RealtimeSubscriptionDisposalTests.cs:31-38`,
  `SemiPlot.Tests.Integration/RealtimeSubscriptionTests.cs:44-46,85-97`. Each resolves `IDataProvider` from
  a container that `ArchiveProviderFactory.Build` or `UnreachableArchive.Services` composes with
  `AddPostgresData` (`SemiPlot.Tests.Integration/ArchiveProviderFactory.cs:29-60`,
  `SemiPlot.Tests.Integration/UnreachableArchive.cs:26-34`). The unit project calls it nowhere.
- `SemiPlot.AppHost` references `SemiPlot.UI` (`SemiPlot.AppHost/SemiPlot.AppHost.csproj:13`).
- The test fake: `FakeDataProvider : IDataProvider` (`SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:16-19`).
  Its live half is `OpenLiveSubscriptionCount`, `LiveSubscriptionsOpened`, `LiveSubscriptionChanging`
  (`:141-146`), `StaggerRealtimeTimestamps`, `PoisonRealtimeWindow`, `RealtimeStreamFailure` (`:153-161`),
  `BreakRealtimePenOnce`, `TheBreakIn` (`:164-190`) and `Subscribe` with its helpers (`:192-246`). The tick
  filters the subscribed ids by the fake's own `Pens` (`:217`), and
  `TrendCoordinatorTests.SetPens_WithAnAddedId_ReachesTheOneSubscriberOfTheLiveEdge` grows `provider.Pens`
  and expects the live edge to carry the new pen (`TrendCoordinatorTests.cs:165-170`). Six test files outside
  the fake read its live members.
- Test containers that `TrendWindow.Build` resolves from: `MainWindowTestBuilder.NewArchiveStand`
  creates its own `TestScheduler` and registers the fake as `IDataProvider`
  (`SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs:97-130`); `TrendWindowTests`,
  `TrendWindowBuildTests`, `AppMainWindowTests`, `AppConfigurationTests` and `EmptyCatalogueStartupTests`
  build through it. `StartupProbeTests` builds containers of its own (`StartupProbeTests.cs:149-151,208-213`)
  and never builds a window.
- Composition tests: `PostgresCompositionTests` resolves `IDataProvider` and lists the singleton
  registrations (`SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs:44-52,83-100`);
  `CompositionRootTests` resolves each port from the container `StartupProbe` builds
  (`SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs:27-49`).
- The socket class for the poll: `RealtimePollTests`, no collection, no fixture, against
  `UnreachableArchive` (`SemiPlot.Tests.Integration/RealtimePollTests.cs:13-23`). `ArmedGate` counts the
  highest `ConsecutiveFailures` across every logger of a container and awaits it
  (`SemiPlot.Tests.Integration/ArmedGate.cs:9-13,53-68`).
- Log category: the log template prints `{SourceContext}` (`SemiPlot.UI/Program.cs:100-102`). No test
  filters the poll's lines by category: `ArmedGate` reads every logger (`ArmedGate.cs:70-73`),
  `RealtimeSubscriptionTests.PollEntries` filters on `RowCount` only (`RealtimeSubscriptionTests.cs:120-123`),
  and `PostgresHistoryReadTests.HistoryReadEntries` filters on the provider's category and the `Outcome`
  key, which no poll line carries (`PostgresHistoryReadTests.cs:1041-1048`).
- Docs that state the old seam: `docs/architecture/data-integration.md:28-62` (the surface and its code
  block), `:941-947,963-978` (Realtime), `:1244-1250` (the terminal-failure guard);
  `docs/architecture/overview.md:69-73,77,88-90,97-104,333-338,905-906`;
  `docs/architecture/charting.md:773-775,789-793`; `docs/architecture/postgres-topology.md:104-107,120-121,130-132,139-140`;
  `docs/architecture/trend-interaction.md:61-62`; `docs/architecture/testing-strategy.md:226`;
  `AGENTS.md:396-403`.
- The later slices this port must serve (roadmap `:211-251`, `:270-288`): `OpcUaLiveSource` opens one
  session per process, writes breaks through `Sample`, reports OPC failures to the message panel through
  `ArchiveFailureMapper` with a new error type in `SemiPlot.Core/Data/Errors`, and drives a second
  status-bar indicator; `live-hold-to-edge` needs to know whether the OPC session is up.

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task).
- Complete each task fully before moving to the next. Each task leaves `dotnet build SemiPlot.slnx` green.
- Every task includes new or updated tests for the code it changes.
- All tests pass before the next task starts.
- Update this plan file when scope changes during implementation.
- `dotnet build SemiPlot.slnx` holds at 0 errors and no compiler or analyzer warning (`TreatWarningsAsErrors`,
  which also makes IDE0005 and CS1574 errors). The NuGet audit warnings `NU1900`-`NU1904` are outside the
  gate: `SemiPlot/Directory.Build.props:14` keeps them warnings, and `85e91eb` already prints two `NU1902` for
  `SemiPlot.AppHost` (MessagePack 2.5.302). `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` pass over the touched
  `.cs` files. A new `.cs` file gets one `dotnet format SemiPlot.slnx --include <file>` (`AGENTS.md`, Build).

## Testing Strategy

- Unit tests (`SemiPlot.Tests.Unit`, no container):
  - `PostgresCompositionTests`: `AddPostgresData` resolves `ILiveSource` as `PostgresLiveSource` and
    registers it once, as a singleton.
  - `CompositionRootTests`: the container `StartupProbe` builds resolves `ILiveSource` as
    `PostgresLiveSource`.
  - `TrendCoordinatorTests`: with two distinct fakes, the live edge subscribes the live source and the
    history and extent reads go to the provider; every existing case passes with only its construction
    changed.
  - `TrendWindowBuildTests`: the window's live edge opens on the container's `ILiveSource`, not on the
    `IDataProvider` instance.
  - Every other suite that builds a coordinator or a window passes unchanged in its assertions.
- Integration tests (`SemiPlot.Tests.Integration`):
  - `RealtimePollTests` (socket, no server): a subscription through `ILiveSource` over an unreachable
    archive emits no notification of any kind, `OnCompleted` and `OnError` included, through five failed
    ticks.
  - `RealtimeEmptyArchiveTests`, `RealtimeSubscriptionTests`, `RealtimeSubscriptionDisposalTests`: the same
    assertions, against the resolved `ILiveSource`.
  - `RealtimePollReadTests`, `ArchiveStatementTextTests` (unit project) and both journeys pass with no
    assertion edited: the scope-guard proof that the poll's statements and samples are unchanged.
  - The whole project runs in Task 4, which changes the `PostgresDataProvider` factory every collection
    class resolves.
- Every new test carries the class's `Component`, `Area` and `Category` traits. AwesomeAssertions throughout.

## Acceptance Evidence

This slice fixes no defect; it moves one seam. The evidence is that the guard fires at `85e91eb`, that
every consumer moved, and that the poll behaves as before.

- Guard at `85e91eb`, run as Task 1's first step: delete `IDataProvider.Subscribe` (`IDataProvider.cs:9-10`)
  and run `dotnet build SemiPlot.slnx`. Hypothesis:
  - the build fails with one error, CS1061 at `SemiPlot.UI/Bridge/TrendCoordinator.cs:98`, and
    `SemiPlot.Tests.Unit`, `SemiPlot.Tests.Integration` and `SemiPlot.AppHost` do not compile because they
    reference `SemiPlot.UI`;
  - with `:98` patched in the same probe to `.Select(_ => Observable.Never<IReadOnlyList<Sample>>())`, the
    build fails with CS0411 at `RealtimeEmptyArchiveTests.cs:78`, `RealtimeSubscriptionDisposalTests.cs:34,38`
    and `RealtimeSubscriptionTests.cs:46,87,97`: without the interface member, overload resolution binds
    `provider.Subscribe(ids)` to `System.ObservableExtensions.Subscribe<T>(IObservable<T>, Action<T>)` and
    type inference fails;
  - `FakeDataProvider` and `PostgresDataProvider` keep compiling: a public `Subscribe` method needs no
    interface.
  Record the error list in Task 1; restore both files.
- What the compiler does not catch, and the checks that do:
  - a cast from `IDataProvider` to `ILiveSource` in `TrendWindow.Build`: the new
    `TrendWindowBuildTests` case registers a second fake as `ILiveSource`, so a cast subscribes the wrong
    instance and the case fails;
  - a forgotten registration in a test container: `GetRequiredService<ILiveSource>` throws in every
    `NewWindowStand` test.
- After the change these commands pass:

```powershell
dotnet build SemiPlot.slnx
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj
```

- These print exactly what is stated:
  - `git grep -n "Subscribe(IReadOnlyList<int>" -- SemiPlot` prints `ILiveSource.cs`, `PostgresLiveSource.cs`
    and `FakeDataProvider.cs` lines only;
  - `git grep -n "ILiveSource" -- SemiPlot/SemiPlot.UI` prints `Bridge/TrendCoordinator.cs` and
    `MainWindow/TrendWindow.cs` lines only;
  - `git grep -n -E "[^f]\(ILiveSource\)|as ILiveSource" -- SemiPlot` prints nothing (the `[^f]` keeps
    `typeof(ILiveSource)`, which is no cast, out of the match);
  - `git diff --stat master -- SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
    prints nothing (the statements and the tick are unchanged).
- Integration runs need a container runtime for the collection classes. ASSUMPTION: Docker is available on
  the executing machine. The CI `linux` leg runs the integration project in any case.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix.
- Document issues or blockers with ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **One port, one member.** `ILiveSource` carries `Subscribe(IReadOnlyList<int> penIds)` and nothing else.
  The name and the signature are the ones `IDataProvider` carries today, so every caller changes the object
  it calls and not the call. The port is not `IDisposable`: the container disposes a singleton that owns a
  resource whatever interface it is registered under, and the poll owns nothing past a subscription. It
  carries no state channel: the archive state already comes from `PenCatalogueSync.ConnectionStates`, and
  the OPC session state is the `opc-live-source` slice's to add, as a member of the port or a sibling
  observable, once its consumers (the second indicator, the hold to the edge) exist.
- **The contract lives in `data-integration.md#realtime`.** The interface carries a one-line summary and a
  pointer to it. The contract: cold per call, and disposal stops the work; never completes, never faults,
  so a failure inside a source is the source's to log or report out of band and emits nothing; a break is
  a `Sample` whose `Value` is null, for the moments the source itself observes; timestamps are UTC; an
  identifier the source does not serve emits nothing and leaves the stream running. Ordering is not part of
  the port: the chart appends a pen's sample only past that pen's last column (`DA-7`) and drops any other.
- **The coordinator keeps its guard.** `Catch` and `RealtimeFailures` stay. A source that faults still
  reaches the panel once, though its live edge is gone for the window's life, and the three
  `RealtimeStreamFailure` tests keep pinning that (`TrendCoordinatorTests.cs:119,201`,
  `TrendChartViewModelTests.cs:2610`). The guard covers `OnError` only; a source that completes
  leaves the live edge silent with no entry. Both are contract violations, and the documentation says what
  each one does.
- **Which breaks travel through the port.** Every break a source can decide from its own input: a `q = 32`
  row today, a session loss or a `Bad` status in `opc-live-source`. The port carries no chart state, so a
  source cannot know a pen's last column. The roadmap's break ahead of a pen's first live sample is
  therefore not settled here: a subscription restarts on every `SetPens` (`Switch`), so a per-subscription
  ahead-break would land past the last live column after every pen-set change, and the chart's two filters
  (`AppendAfterLastColumn` rejects at or before the last column only when columns exist,
  `EnvelopeLine.cs:271-286`; `ReplaceColumns` keeps the appended tail past the history end, `:195-214`) would
  draw it. `opc-live-source` designs that rule; this slice's documentation names it as open and states no
  rule for it.
- **A class of its own for the poll.** `PostgresLiveSource` holds the `Observable.Create` and the loop that
  `PostgresDataProvider` holds today, so `opc-live-source` deletes the poll implementation as one file plus
  `RealtimePoll`, and `PostgresDataProvider` (626 lines) drops its two live-only fields.
- **One fake for both interfaces.** `FakeDataProvider` implements `IDataProvider` and `ILiveSource`. Its
  live edge filters by its own `Pens`, which the catalogue tests grow at run time, so a separate live fake
  would have to share that list with the provider fake. Tests that need the two apart build two
  `FakeDataProvider` instances.

## Technical Details

- `SemiPlot.Core/Data/ILiveSource.cs`:

```csharp
using SemiPlot.Core.Trends;

namespace SemiPlot.Core.Data;

/// <summary>The live edge of a pen set (docs/architecture/data-integration.md#realtime).</summary>
public interface ILiveSource
{
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);
}
```

- `SemiPlot.DataSource.Postgres/PostgresLiveSource.cs`: `public sealed class PostgresLiveSource : ILiveSource`
  with an explicit `internal` constructor over `NpgsqlDataSource`, `ArchiveTimeConverter`,
  `ArchiveExceptionMapper`, `IScheduler`, `ILogger<PostgresLiveSource>` and `TimeSpan pollInterval`, in that
  order (the configuration value last, as `PostgresDataProvider`'s `historyReadConcurrency`), assigning one
  `_camelCase` `readonly` field each. A primary constructor takes no access modifier,
  and a public one would expose the `internal` `ArchiveExceptionMapper` (CS0051); the sibling classes have
  the same shape (Context). `Subscribe` and `PollAsync` move from `PostgresDataProvider.cs:76-87,340-377`
  with their bodies unchanged except for the field names; the sleep is
  `_scheduler.Sleep(_pollInterval, cancellationToken)`. The `Subscribe` summary says "the poll interval"
  instead of the `PostgresConnectionSettings.PollInterval` `cref`, because the class takes a `TimeSpan`. The
  registration is a factory, and the composition tests and the integration containers reach the class only
  through `AddPostgresData`.
- `AddPostgresData` registers it beside the provider:
  `services.AddSingleton<ILiveSource>(provider => new PostgresLiveSource(...))`, passing
  `provider.GetRequiredService<PostgresConnectionSettings>().PollInterval`. `PostgresDataProvider`'s factory
  loses its `settings` and `IScheduler` arguments.
- `PostgresDataProvider` after the move: no `Subscribe`, no `PollAsync`, no `_settings` or `_scheduler`
  fields or constructor parameters, no `System.Reactive.*` or `SemiPlot.DataSource.Postgres.Configuration`
  usings; the class summary drops the live edge and its `cref`.
- `TrendCoordinator(IDataProvider dataProvider, ILiveSource liveSource, IReadOnlyList<Pen> pens, IScheduler
  dataScheduler, IScheduler uiScheduler)`; field `_liveSource` after `_dataProvider`;
  `BuildRealtimeBatches` reads `_penIds.Select(_liveSource.Subscribe)`. The comment at `:25-27` says that
  the live source drops the identifiers it does not serve. The summaries at `:42-52` say "a live source that
  faults", because an `OnCompleted` is not reported (Overview).
- `TrendWindow.Build` resolves `var liveSource = serviceProvider.GetRequiredService<ILiveSource>();` beside
  the provider and passes it to the coordinator.
- `MainWindowTestBuilder.NewArchiveStand` gains an optional `ILiveSource? liveSource = null` and registers
  `liveSource ?? provider` as `ILiveSource`. Its one caller with a value is the new `TrendWindowBuildTests`
  case.
- Log output: the poll's lines (`RealtimePoll.cs:116-122,178-185,204-208`) carry the source context
  `SemiPlot.DataSource.Postgres.PostgresLiveSource`. Their text and level are unchanged.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests and documentation in this repository.
- **Post-Completion** (no checkboxes): the manual smoke on the demo stand and the remaining roadmap slices.

## Implementation Steps

### Task 1: Declare the port and serve it from the archive poll

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/ILiveSource.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresLiveSource.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollTests.cs`

- [x] run the guard probe in Acceptance Evidence at `85e91eb` and record the error list here; restore
      `IDataProvider.cs` and `TrendCoordinator.cs`
      - Run on the `live-source-port` working tree at `54f455e` (same tree as `85e91eb`); both files restored
        with `git checkout --`. The observed list matches the hypothesis exactly.
      - `Subscribe` deleted: one error, `SemiPlot.UI/Bridge/TrendCoordinator.cs(98,26): error CS1061`
        (`IDataProvider` has no `Subscribe`); nothing downstream of `SemiPlot.UI` compiled.
      - `:98` patched to `Observable.Never`: six errors, all `CS0411` on
        `ObservableExtensions.Subscribe<T>(IObservable<T>, Action<T>)`: `RealtimeEmptyArchiveTests.cs(78,37)`,
        `RealtimeSubscriptionDisposalTests.cs(34,80)`, `(38,31)`, `RealtimeSubscriptionTests.cs(46,79)`,
        `(87,72)`, `(97,73)`. `FakeDataProvider` and `PostgresDataProvider` compiled.
- [x] create `ILiveSource` as in Technical Details
- [x] create `PostgresLiveSource` with the `Subscribe` and `PollAsync` bodies of
      `PostgresDataProvider.cs:76-87,340-377`; `PostgresDataProvider` keeps its own copy until Task 4
      deletes it
- [x] register `ILiveSource` in `AddPostgresData` after `IDataProvider`
- [x] `PostgresCompositionTests`: add `AddPostgresDataResolvesThePostgresLiveSource` and
      `[InlineData(typeof(ILiveSource))]` to `AddPostgresDataRegistersASingleton`
- [x] `CompositionRootTests`: add `Container_ResolvesTheLiveSource` (`PostgresLiveSource`)
- [x] `RealtimePollTests`: add `ASubscriptionThatOnlyFailsNeitherCompletesNorFaults`: an `ArmedGate` over
      `UnreachableArchive.Services` at a 10 ms poll interval, a `BatchCollector` over
      `ILiveSource.Subscribe([1]).Materialize()`, await `FailuresReached(TickCount)` within
      `ClonedArchiveTest.GateBound`, then `Batches` is empty
- [x] run the unit project and
      `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~RealtimePollTests"`;
      both pass before Task 2 (unit 2300/2300, `RealtimePollTests` 3/3)

### Task 2: Point the poll's integration tests at the port

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeEmptyArchiveTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionDisposalTests.cs`

- [x] `RealtimeEmptyArchiveTests.ASubscriptionOverAnArchiveWithNoRowLogsItsBaseline` (`:70-81`) resolves
      `ILiveSource` into a local named `liveSource`
- [x] `RealtimeSubscriptionTests` (`:44,85`) resolves `ILiveSource` into `liveSource` in both cases
- [x] `RealtimeSubscriptionDisposalTests` (`:31-32`) resolves `ILiveSource` for `dropped` and `running`;
      the comments at `RealtimeSubscriptionDisposalTests.cs:19-20` and `RealtimeSubscriptionTests.cs:32` say
      "live source" where they say "provider"
- [x] no assertion changes in the three classes
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~Realtime"`;
      it passes before Task 3 (17/17, 0 skipped)

### Task 3: Move the coordinator and the window onto the port

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Minimap/MinimapStand.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/TrendWindowBuildTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`

- [x] `TrendCoordinator` takes `ILiveSource` as in Technical Details; reword `:25-27` and `:42-52`
- [x] `TrendWindow.Build` resolves `ILiveSource` and passes it
- [x] `FakeDataProvider` implements `ILiveSource` beside `IDataProvider`; its comments at `:159-160` say
      "live source" for the live edge
- [x] pass the fake as both arguments at `TrendCoordinatorTests.cs:97,121,214`, `ChartTestBuilder.cs:60` and
      `MinimapStand.cs:37`; pass the container's `ILiveSource` at the two journey sites
- [x] `MainWindowTestBuilder.NewArchiveStand` takes `ILiveSource? liveSource = null` and registers
      `liveSource ?? provider`
- [x] `TrendCoordinatorTests`: rename the three cases that say "provider" for the live edge (`:70,174,191`)
      to say "live source", and say "live source" in the comment at `:110-111` and the exception texts at
      `:116,200`; the same for `TrendChartViewModelTests.AProviderThatEndsTheLiveEdge_IsReportedOnce`
      (`:2604`) and its exception text, and the comment at `LiveEdgeArchiveJourneyTests.cs:23`
- [x] `TrendCoordinatorTests`: add `TheLiveEdgeFollowsTheLiveSource_AndTheReadsGoToTheProvider`: two fakes
      over one `TestScheduler`, one subscription to `RealtimeBatches`, one `QueryHistoryAsync` and one
      `QueryArchiveExtentAsync`; the live fake opened one live subscription and saw no history or extent
      query, the archive fake opened none and saw one of each
- [x] `TrendWindowBuildTests`: add `TheWindowFollowsTheContainersLiveSource`: `NewArchiveStand(liveSource: <a
      second FakeDataProvider over its own TestScheduler, never advanced>)`, build the window as
      `TheWindowReadsTheCatalogueAtOnce_OnTheRealScheduler` does (`:70-88`); the second fake's
      `LiveSubscriptionsOpened` is 1 and the stand provider's is 0
- [x] run the unit project and
      `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~JourneyTests"`;
      both pass before Task 4 (unit 2302/2302, journeys 4/4, 0 skipped)

### Task 4: Delete `IDataProvider.Subscribe` and the provider's live half

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`

- [x] delete `IDataProvider.Subscribe` and its comment; `dotnet build SemiPlot.slnx` is green, because Tasks
      2 and 3 moved every consumer
- [x] delete `Subscribe`, `PollAsync`, `_settings`, `_scheduler`, their constructor parameters, the
      `System.Reactive.*` and `SemiPlot.DataSource.Postgres.Configuration` usings and the live-edge `cref`
      from `PostgresDataProvider`; drop the two arguments from its factory in `AddPostgresData`
- [x] run the three `git grep` checks of Acceptance Evidence
      - `Subscribe(IReadOnlyList<int>` prints `ILiveSource.cs:8`, `PostgresLiveSource.cs:44`,
        `FakeDataProvider.cs:192` only; `ILiveSource` in `SemiPlot.UI` prints `Bridge/TrendCoordinator.cs:17,31`
        and `MainWindow/TrendWindow.cs:73` only.
      - ⚠️ the cast check prints one line, `PostgresCompositionTests.cs:95: [InlineData(typeof(ILiveSource))]`
        (added in Task 1): the regex `\(ILiveSource\)` also matches `typeof(ILiveSource)`. It is no cast;
        `git grep -n -E "[^f]\(ILiveSource\)|as ILiveSource" -- SemiPlot` prints nothing.
      - the `git diff --stat master` over `RealtimePoll.cs` and `ArchiveStatements.cs` prints nothing.
- [x] `PostgresCompositionTests` and `CompositionRootTests` pass unchanged: they pin both registrations
- [x] run the unit project and the whole integration project
      (`dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`): every collection
      class resolves the changed `PostgresDataProvider` factory; both pass before Task 5
      - unit: passed 2302, failed 0, skipped 0 (2302 total); integration: passed 197, failed 0, skipped 0
        (197 total), container runtime reached

### Task 5: Verify acceptance criteria

- [x] `dotnet build SemiPlot.slnx` at 0 errors and no warning beyond the two `NU1902` of `SemiPlot.AppHost`
      - run fresh at `4940ea1`, also with `--no-incremental`: 0 errors, 2 warnings, both `NU1902` (MessagePack
        2.5.302) on `SemiPlot.AppHost`
- [x] the three Acceptance Evidence test commands pass; record Task 4's counts here
      - fresh runs at `4940ea1`: build green as above; unit: passed 2302, failed 0, skipped 0 (2302 total);
        integration: passed 197, failed 0, skipped 0 (197 total), container runtime reached
- [x] every `git grep` and `git diff --stat` check of Acceptance Evidence prints what it states
      - `Subscribe(IReadOnlyList<int>` prints `ILiveSource.cs:8`, `PostgresLiveSource.cs:44`,
        `FakeDataProvider.cs:192` only; `ILiveSource` in `SemiPlot.UI` prints `Bridge/TrendCoordinator.cs:17,31`
        and `MainWindow/TrendWindow.cs:73` only.
      - ⚠️ the literal cast check `\(ILiveSource\)|as ILiveSource` prints one line,
        `PostgresCompositionTests.cs:95: [InlineData(typeof(ILiveSource))]`, which is no cast (Task 4); the
        narrowed `[^f]\(ILiveSource\)|as ILiveSource` prints nothing (exit 1).
      - the `git diff --stat master` over `RealtimePoll.cs` and `ArchiveStatements.cs` prints nothing.
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files
      exit 0
      - touched = `git diff --name-only master...HEAD -- '*.cs'`, 22 files; `dotnet format` exit 0,
        `dotnet terse <22 files>` exit 0

### Task 6: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `AGENTS.md`

- [x] `data-integration.md`, The provider surface (`:28-62`): drop `Subscribe` from the `IDataProvider`
      block; add `ILiveSource` and `PostgresLiveSource` as a fourth Core interface beside the reader and the
      two write paths, with its code block
- [x] `data-integration.md`, Realtime (`:941-947`): open with the port's contract (Solution Overview),
      naming what each terminal message does to the live edge (Overview) and which breaks travel through
      the port, with no rule for a break ahead of a first live sample (Solution Overview); the bullet "never completes and never faults" (`:977-978`) and the poll's own points stay,
      stated for `PostgresLiveSource`; `Select(_dataProvider.Subscribe)` at `:963-967` reads
      `Select(_liveSource.Subscribe)`, and `:974` says "pens of the live source's catalogue"; `:1248` says
      "out of the live source"; note that the poll's lines carry the `PostgresLiveSource` source context
      - as built, the pen-set paragraph keeps "pens of the provider's own catalogue" and adds "the live
        source drops any identifier it does not serve": `TrendCoordinator.cs:26-28` states the set is the
        provider's catalogue
- [x] `overview.md`: the diagram's interface list and Core box (`:69-73,77`), the Postgres box (`:88-90`),
      the paragraph at `:97-104`, the Realtime data flow (`:333-338`) and Scope status (`:905-906`) name the
      port
- [x] `charting.md:773-775,789-793`: the live edge comes through `ILiveSource`; `SetPens` switches the
      live-source subscription; `:67` and `trend-interaction.md:72` say "a break the live source reported"
- [x] `postgres-topology.md`: the diagram (`:104-107,120-121`) and the text at `:130-132,139-140` name
      `PostgresLiveSource` for the live-edge poll
- [x] `trend-interaction.md:61-62`: the coordinator signature carries `ILiveSource liveSource`
- [x] `testing-strategy.md:226`: the `LiveEdgeArchiveJourneyTests` row says "over the live source alone"
      where it says "over the provider alone"; `git grep -n "over the provider alone" -- docs` prints nothing
      - ⚠️ over `docs` the grep prints two plan lines: this checkbox, which quotes the phrase, and
        `docs/plans/completed/20260824-postgres-live-edge-and-demo.md:1649`, a completed plan left as
        written; `git grep -n "over the provider alone" -- docs ':!docs/plans'` prints nothing (exit 1).
- [x] `AGENTS.md`, Data-source projects (`:396-403`): one sentence: `ILiveSource` lives in
      `SemiPlot.Core/Data` beside `IDataProvider`, and `AddPostgresData()` registers `PostgresLiveSource`
      over the same `NpgsqlDataSource` (`docs/architecture/data-integration.md#realtime`)

## Post-Completion

**Manual verification:**

1. Start the demo stand: `dotnet run --project SemiPlot/SemiPlot.AppHost`.
2. With the time window at the live edge, the newest point of a drawn pen advances as the demo writer
   appends, as before.
3. Run the viewer at `--logging-level Debug` and open `semiplot.log`: the realtime poll lines carry the
   source context `SemiPlot.DataSource.Postgres.PostgresLiveSource`; the history-read lines still carry
   `PostgresDataProvider`.

**Remaining slices** (docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md):

- opc-demo-server: an OPC UA server in the ArchiveSeeder demo writer with the Simple-Scada address space,
  port 49875.
- opc-live-source (Closes #116): `SemiPlot.DataSource.OpcUa` feeds the live edge; the poll is deleted.
- opc-settings: an OPC block after the DB block in the settings window.
- live-hold-to-edge: a grey pending gap and a render-only hold of the last value to the edge.

**Executed by exec:**

- branch: live-source-port

## Verify it yourself

This slice moves a seam and fixes no defect, so no test fails at `master` and passes on the branch: the
new tests name `ILiveSource`, which does not exist at `master`. The checks below prove the move instead.

1. Build and run both projects at the branch tip (`5e8e59b` or later):

   ```powershell
   dotnet build SemiPlot.slnx
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
   dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj
   ```

   Expected: 0 errors and only the two `NU1902` warnings of `SemiPlot.AppHost`; unit 2303 passed, integration
   200 passed (Docker running). Task 5's 2302 and 197 were taken at `4940ea1`, before review added four tests.
2. Run the four `git grep`/`git diff` checks of Acceptance Evidence. Expected: `Subscribe(IReadOnlyList<int>`
   prints `ILiveSource.cs`, `PostgresLiveSource.cs` and `FakeDataProvider.cs` only; `ILiveSource` under
   `SemiPlot.UI` prints `TrendCoordinator.cs` and `TrendWindow.cs` only; the cast check prints nothing; the
   diff over `RealtimePoll.cs` and `ArchiveStatements.cs` is empty.
3. The guard: delete `IDataProvider.Subscribe` at `master` and build. Expected: CS1061 at
   `TrendCoordinator.cs:98` (Task 1 records the full list). On the branch the member is already gone and
   the build is green.
4. The tests that pin what the compiler cannot:
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TheWindowFollowsTheContainersLiveSource|FullyQualifiedName~TheLiveEdgeFollowsTheLiveSource|FullyQualifiedName~ALiveSourceThatCompletes"`
   and
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~RealtimePollTests"`.
   Replacing `GetRequiredService<ILiveSource>()` in `TrendWindow.Build` with `(ILiveSource)dataProvider`
   turns `TheWindowFollowsTheContainersLiveSource` red.
5. The one output change is manual: Post-Completion step 3 (the poll's log lines under
   `SemiPlot.DataSource.Postgres.PostgresLiveSource`). `RealtimePollTests.ThePollLogsUnderTheLiveSourcesCategory`
   pins the same category without a server.
