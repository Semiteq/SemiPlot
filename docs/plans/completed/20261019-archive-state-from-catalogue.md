# Archive state from the catalogue read

## Overview

- The status bar's archive indicator and its `ConnectionLost` panel entry come from the pen catalogue
  read loop, not from the live-edge poll. `PenCatalogueSync` publishes `ArchiveConnectionState`;
  `IDataProvider.ConnectionFaults`, its republish in `TrendCoordinator` and the poll's state machine are
  deleted. The poll still feeds the live edge with the same statements and samples.
- Only a connection-class failure raises `ConnectionLost`, after three of them in a row. Connection class
  is `ArchiveFault.Unreachable`, which this slice widens to the server-side SQLSTATEs that mean "no
  connection": class `08`, `57P01`, `57P02`, `57P03` and `53300`. A fired client bound is already
  `Unreachable`. `QueryTimedOut` (`57014`) is not connection class: the server answered and ended the
  statement, so it keeps its own entry, as do `TableMissing`, `AccessDenied`, `ShapeUnexpected`,
  `DatabaseMissing` and `ReadFailed`. At HEAD the poll turns any failure, a missing `trends` table
  included, into `ConnectionLost` (`RealtimePoll.cs:225-250`).
- One rule covers the three readers of the shared `NpgsqlDataSource`: the catalogue loop, the marker loop
  and the poll. A connection-class failure reaches the operator through the catalogue's state and the one
  `ConnectionLost` entry the bar writes; each reader logs its own. Every other kind reaches the panel
  through the two loops' own route. The poll has no route of its own any more: its other kinds surface
  through the minimap's band reads and the chart's history reads over the same `trends` relation, which run
  only while the minimap has an extent and a drawn pen, or on a gesture. So an outage makes one entry from
  the read loops and the bar; the minimap's first failed band read of the outage adds its own
  `Unreachable` entry, as at HEAD.
- The catalogue read gets its own 5 s statement bound. At HEAD it inherits the connection string's 300 s
  backstop, so a stalled server would raise the fault after about 15 minutes.
- The window issues one catalogue read at start, so the failure run, and the first `Connected`, start at
  window open instead of 5 s later.
- This prepares the roadmap's later slices: the poll can leave `IDataProvider` and then be deleted without
  taking the archive indicator with it.

## Context (from discovery)

- Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice archive-state-from-catalogue
- Verified at `36d5d85`. `master` at `cf21d86` carries the same `SemiPlot/`, `docs/` and `AGENTS.md`
  trees (`git diff --stat 36d5d85 cf21d86` prints nothing for them). Line numbers below are at `36d5d85`.
- The state type: `ArchiveConnectionState(ArchiveError? Fault)`, `ConsecutiveFailuresBeforeFault = 3`,
  `Connected`, `IsConnected` (`SemiPlot/SemiPlot.Core/Data/ArchiveConnectionState.cs:10-18`). Its doc speaks
  of "a provider", "every subscription's first tick" and `DistinctUntilChanged` (`:5-9`).
- The member that goes: `IObservable<ArchiveConnectionState> ConnectionFaults { get; }`
  (`SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:12-15`).
- Its producer: `PostgresDataProvider` holds `_connectionFaultsSource` and a synchronized
  `_connectionFaults` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:49-53,80,87`),
  `PollAsync` builds a `RealtimePoll` with `_settings` (`:374-380`) and pushes `tick.StateChange`
  (`:400-403`), and `Dispose` only completes the subject (`:246-260`). `RealtimePoll` builds the state:
  `RealtimeTick(Samples, StateChange)` (`RealtimePoll.cs:20`), `Succeed` (`:210-223`), `Fail` (`:225-250`),
  which counts every failure kind and builds `ConnectionLost` from `settings.Host/Port/Database`
  (`:244-249`), the only read of the `settings` parameter (`:30`).
- Its consumers: `TrendCoordinator` republishes it through `ObserveOn(_uiScheduler)`
  (`SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:21-22,28,45-48,63-67,74,80`), and the
  `RealtimeFailures` summary names it in a `cref` (`:59`); `TrendWindow.Build` hands
  `coordinator.ConnectionFaults` to `AppStatusBarViewModel` (`SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs:98-102`).
- The bar: `AppStatusBarViewModel.ApplyConnectionState` sets `IsConnected`, reports a fault through
  `_messagePanel.ReportFailure`, and writes the `Info` recovery entry only after a fault (`_hasSeenFault`)
  (`SemiPlot/SemiPlot.UI/MainWindow/AppStatusBarViewModel.cs:86-120`). `IsConnected` starts `true` (`:60`).
  Its comments name `TrendCoordinator`'s bare `Subscribe` (`:84-85`) and "every subscription's first tick"
  (`:100-101`).
- The loop: `ArchiveReadLoop<T>(read, apply, readName, messagePanel, uiScheduler, logger)`
  (`SemiPlot/SemiPlot.UI/Bridge/ArchiveReadLoop.cs:24-30`). One counter `_consecutiveFailures` (`:41`);
  `ReadNow()` completes the wake before the loop runs (`:50-53`); `ReadAsync` resets the count and applies
  on success (`:100-118`); `ReportFailedRead` logs the first two failures and sends the third and every
  later one to the panel whatever the kind (`:120-136`); a thrown read goes to `TryReportFailure` and leaves
  the count (`:81-84`).
- The loop's two users: `PenCatalogueSync` (`SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs:32-33`) and
  `MarkerSync` (`SemiPlot/SemiPlot.UI/Markers/MarkerSync.cs:40-41`).
- The window: the bar is built at `TrendWindow.cs:98-102`, the catalogue sync at `:106-111`;
  `catalogueSync.Start()` at `:140`, `markerSync.Start()` and `markerSync.ReadNow()` at `:141-142`.
  `Dispose` disposes the sync at `:165` and the bar at `:167`; the class doc promises reverse build order
  (`:22-25`); fields and constructor parameters follow the build order (`:28-60`).
- The catalogue read: `QueryPensAsync` opens a pooled connection and runs `ArchiveStatements.PenCatalog`
  on a command with no `CommandTimeout` (`PostgresDataProvider.cs:105-126`), so it takes the connection
  string's `CommandTimeout = 300` (`SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs:26-28,42`).
  The connect bound is `Timeout = 15` (`:20-23,43`). The statement is one `GROUP BY` over the three catalogue
  tables (`ArchiveStatements.cs:160-171`). The start sequence reads it too (`SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs:63`),
  under a 30 s caller bound (`:22-24`).
- The poll's bound, the precedent: `TickCommandTimeoutSeconds = 10` and `CreateTickCommand`
  (`RealtimePoll.cs:37-39,85-88`), pinned by
  `ArchiveStatementTextTests.ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop`
  (`SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs:219-233`).
- The fault classes: `ArchiveExceptionMapper.IsConnectionFailure` maps `NpgsqlException`, `SocketException`
  and `TimeoutException` to `Unreachable`; the comment states that a fired client bound is one of them
  (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:72-87`). `MapSqlState` sends every
  unlisted SQLSTATE to `ReadFailed` (`:89-104`), so `57P01` (admin shutdown), `57P03` (starting up) and
  `53300` (too many connections) are `ReadFailed` at HEAD. `MapWrite` falls through to `Map` (`:38-47`).
  `ArchiveExceptionMapperTests` pins the socket and the client bound at `Unreachable`
  (`SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveExceptionMapperTests.cs:29-44`). Every `ArchiveError`
  carries `Host`, `Port`, `Database` (`SemiPlot/SemiPlot.Core/Data/Errors/ArchiveError.cs:10-21`).
- The panel coalesces a report only into an identical newest entry (`SemiPlot/SemiPlot.UI/Messages/MessagePanelViewModel.cs:54-79,86-89`).
- `ConnectionLost` maps to a `Warning` titled "The archive stopped answering"
  (`SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs:180-186`), under a comment about "a lost live
  edge" (`:180-181`); the resx texts name no poll (`Resources.resx:324-331`). `ArchiveError.Describe` says
  "The live edge of the archive stopped answering" (`ArchiveError.cs:37-38`), and `ArchiveFault.ConnectionLost`'s
  doc says "poll ticks" (`ArchiveFault.cs:27`).
- The minimap: `MinimapBandFeed` schedules its next band read before it applies a result, so its reads keep
  running through failures while a pen is drawn (`SemiPlot/SemiPlot.UI/Minimap/MinimapBandFeed.cs:186-198`),
  and `OutageReport` writes the first failure of each outage and logs the rest (`SemiPlot/SemiPlot.UI/Minimap/OutageReport.cs:10-33`).
  The band reads `trends` through `QueryHistoryAsync` (`MinimapBandFeed.cs:161-170`).
- Test drivers that read the provider's stream or count catalogue reads:
  - unit: `FakeDataProvider.ConnectionFaults`, `ConnectionFaultsObserverCount`, `ReportConnectionState`
    (`SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs:33-35,167-185`), used by `TrendCoordinatorTests`
    (five cases, `:116,133,151,168,214`), `TrendChartViewModelTests` (`:75-85`), `TrendWindowTests`
    (`:32-50,92-111`), `TrendWindowBuildTests` (`:27-31,74-100`) and `AppMainWindowTests` (`:43,51`).
    `FakeDataProvider.FailPens` answers `Unreachable` (`:52,282-286`). `MainWindowViewModelTests` asserts
    absolute `PensQueryCount` values after `NewWindowStand` (`:105,128,147`), whose `AdvanceBy(1)`
    (`UI/MainWindow/MainWindowTestBuilder.cs:149`) runs a read that `ReadNow()` asked for.
  - unit tests over the loop that fail with `Unreachable`: `PenCatalogueSyncTests` (`:141-174,195-215`) and
    `MarkerSyncTests.ADiscardedRead_CountsAsNoFailure` (`:92-110`) and `TwoFailedReads_LogTheMarkerRead_TheThirdReportsOnce`
    (`:178-196`).
  - integration: `ArmedGate` counts `Connected` states as armed subscriptions
    (`SemiPlot/SemiPlot.Tests.Integration/ArmedGate.cs:5-75`) for `RealtimeSubscriptionTests` (`:43,84`),
    `RealtimeEmptyArchiveTests` (`:80`) and `LiveEdgeArchiveJourneyTests` (`:184,211`);
    `RealtimeSubscriptionTests.DisposingASubscriptionStopsItsPoll` already passes a `FakeLoggerProvider` as
    the one logger provider (`:38-39,118-121`); `RealtimeSubscriptionDisposalTests` calibrates its wait on the
    running provider's `ConnectionLost` and asserts the dropped provider raised no state (`:30-47`);
    `RealtimePollTests` (`:26-49`), `RealtimePollReadTests` (`:39-47,77,94,144,207,214-278`) and
    `RealtimeEmptyArchiveTests` (`:33-41,56-65`) read `RealtimeTick`; `RealtimeArchive.TickAsync` and a local
    `ConsecutiveFailuresBeforeFault` serve them (`RealtimeArchive.cs:25-27,53-63`); `PostgresCompositionTests`
    asserts that `Dispose` completes the stream (`SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs:135-160`).
- The poll's log lines that stay: the baseline lines (`RealtimePoll.cs:129-134`), the per-tick count line
  with `RowCount` (`:190-197`), the failure warning with `ConsecutiveFailures` (`:231-235`), written inside
  `ReadOnceAsync` before `PollAsync` checks for cancellation (`PostgresDataProvider.cs:388-393`).
  `ArchiveProviderFactory.Build` takes one optional `ILoggerProvider` and sets `Debug` with it
  (`SemiPlot/SemiPlot.Tests.Integration/ArchiveProviderFactory.cs:29-45`); `UnreachableArchive.Services`
  takes none and sets no level (`SemiPlot/SemiPlot.Tests.Integration/UnreachableArchive.cs:25-31`).

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task). Task 4 runs its new
  `TrendWindowTests` cases once before rewiring `TrendWindow.Build`, to see them fail.
- Complete each task fully before moving to the next. Each task leaves `dotnet build SemiPlot.slnx` green.
- Every task includes new or updated tests for the code it changes, success and failure paths both.
- All tests pass before the next task starts.
- Update this plan file when scope changes during implementation.
- `dotnet build SemiPlot.slnx` holds at zero warnings (`TreatWarningsAsErrors`, which also makes CS9113 and
  IDE0005 errors). `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` pass over the
  touched `.cs` files.

## Testing Strategy

- Unit tests (`SemiPlot.Tests.Unit`, no container):
  - `PenCatalogueSyncTests`: the state sequence over a `TestScheduler`; the connection-class run against
    other kinds; the state lands before the apply.
  - `MarkerSyncTests`: a connection-class failure logs only; another kind still reports from the third.
  - `ArchiveExceptionMapperTests`: the widened `Unreachable` SQLSTATEs.
  - `ArchiveStatementTextTests`: the catalogue command carries its own 5 s bound.
  - `TrendWindowTests`: three failed catalogue reads turn the bar to fault with one panel entry; the first
    catalogue read happens at start.
  - `AppStatusBarViewModelTests`, `AppStatusBarViewTests`: unchanged inputs (a `Subject`), kept green.
- Integration tests (`SemiPlot.Tests.Integration`):
  - a socket class without a collection: `QueryPensAsync` over `UnreachableArchive.Services` answers
    `ArchiveError { Kind: Unreachable }`, the real-socket proof of the route.
  - `ArmedGate` turns into a forwarding logger provider that counts the poll's baseline lines.
  - `RealtimeSubscriptionDisposalTests` calibrates on the poll's third failure warning.
  - The poll's state assertions go; its sample assertions stay.
- Every new test class carries the `Component`, `Area` and `Category` traits. AwesomeAssertions throughout.
  Errors are asserted by type and structured field, never by message wording.
- No UI e2e suite exists beyond the headless tests above.

## Acceptance Evidence

- Defect at HEAD, by reading:
  - `ArchiveReadLoop.ReportFailedRead` sends the third `Unreachable` catalogue failure to the panel and emits
    no state (`ArchiveReadLoop.cs:120-136`); the poll sends its own `ConnectionLost` beside it
    (`RealtimePoll.cs:237-249`). Two entries per outage, from two counters.
  - `RealtimePollReadTests.TheFirstSuccessAfterARaisedFaultReportsConnectedExactlyOnce` passes at HEAD and
    asserts `ConnectionLost` for a renamed `trends` table, which is `TableMissing` (`RealtimePollReadTests.cs:214-278`).
  - The catalogue command carries no `CommandTimeout` (`PostgresDataProvider.cs:110`).
  - `57P03` maps to `ReadFailed` (`ArchiveExceptionMapper.cs:103`), so a reconnect during a restart's recovery
    would bypass a rule keyed on `Unreachable`.
- Defect by test: the new `TrendWindowTests` case
  `ThreeUnreachableCatalogueReads_TurnTheBarToFault_WithOneConnectionLostEntry` runs red against the
  unchanged `TrendWindow.Build` (Task 4, first step): `IsConnected` stays `true`, because the bar still
  follows the coordinator, and the panel holds no entry, because Task 1 already stopped the loop from
  reporting `Unreachable`.
- Guards:
  - deleting `TrendCoordinator.ConnectionFaults` (Task 4) fails the build with one error per remaining
    consumer, and that build lists them;
  - by Task 5 no consumer of `IDataProvider.ConnectionFaults` is left, so its guard is
    `git grep -n "ConnectionFaults" -- SemiPlot`, run before the deletion (only the interface and its two
    implementations) and after it (nothing);
  - no new error type is declared, so `FailureSeverityTests` is not engaged by this slice.
- After the change, these commands pass:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PenCatalogueSyncTests|FullyQualifiedName~MarkerSyncTests|FullyQualifiedName~TrendWindowTests|FullyQualifiedName~TrendWindowBuildTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~ArchiveStatementTextTests|FullyQualifiedName~ArchiveExceptionMapperTests|FullyQualifiedName~AppStatusBar|FullyQualifiedName~TrendCoordinatorTests"
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~Realtime|FullyQualifiedName~LiveEdgeArchiveJourneyTests|FullyQualifiedName~CatalogueReadUnreachableTests"
```

- What the unit run must show:
  - `PenCatalogueSyncTests`: over `Start(); ReadNow()` and `FailPens` from the second read, the states are
    exactly `[Connected, ConnectionLost(detail "3")]` after the fourth read, and `[.., Connected]` after the
    first success that follows. The panel stays empty throughout.
  - With `PensFailureKind = TableMissing`, no state follows the first `Connected`, and the panel holds one
    `TableMissing` entry from the third failure on. `[TableMissing x3, Unreachable]` emits no state.
  - `TrendWindowTests`: after three failed reads, `StatusBar.IsConnected` is `false` and the panel holds one
    entry whose view equals `ArchiveFailureMapper.Map` of a `ConnectionLost` error. A fourth failure adds no
    entry and does not restamp it. The next success sets `IsConnected` and adds the `Info` recovery entry.
  - `ArchiveStatementTextTests`: the catalogue command's `CommandTimeout` is `5`.
  - `ArchiveExceptionMapperTests`: `08006`, `57P01`, `57P02`, `57P03` and `53300` map to `Unreachable`;
    `57014` still maps to `QueryTimedOut`.
- The integration run must show the poll's batches unchanged: every `RealtimePollReadTests` sample
  assertion and both `RealtimeSubscriptionTests` cases pass on the log gate.
- Integration runs need a container runtime for the collection classes. ASSUMPTION: Docker is available on
  the executing machine. The CI `linux` leg runs the integration project in any case.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix.
- Document issues or blockers with ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **The catalogue loop decides the state from the read.** `ArchiveReadLoop<T>` takes an optional
  `Action<ArchiveConnectionState>? reportConnectionState`. `PenCatalogueSync` passes its own subject's
  `OnNext` and exposes the subject as `ConnectionStates`; `MarkerSync` passes `null`. With a sink, the loop
  emits `Connected` on the first success and on the first success after a raised fault, and
  `ConnectionLost` once per outage, after three connection-class failures in a row.
- **One rule for every reader of the data source.** A connection-class failure is logged by the reader
  that met it and reaches the operator through the catalogue's state and the bar's one entry. Any other
  kind reaches the panel through the two loops' own route, from the third failure in a row, coalescing
  repeats into the newest entry, as today. The poll has no route of its own: a `TableMissing`, an
  `AccessDenied` or a role `statement_timeout` on `trends` surfaces through the minimap's band reads (every
  2 to 60 s, the first failure of each outage, `MinimapBandFeed.cs:30-31,206-211`) and through the chart's
  history read on the next gesture. The band reads run only while the minimap has an extent and a drawn pen
  (`MinimapBandFeed.cs:141-157`).
- **The residual cases, each logged at every failure:**
  - the marker loop: a marker statement stalled to the 300 s backstop while the catalogue answers; the marker
    read has no bound of its own (`PostgresMarkerStore.cs:42-43`), so after a stall the marker loop can sit
    on a dead pooled connection for up to 300 s after the bar has written the recovery entry;
  - the poll: a tick whose incremental read exceeds its 10 s bound and repeats every second, because a failed
    tick does not advance `_lastSeen`; HEAD does not recover from it either, it raises a permanent
    `ConnectionLost` over a frozen edge;
  - the poll: a non-connection failure on `trends` while no band read runs (an archive with no extent, or no
    drawn pen) and the operator makes no gesture; HEAD showed it as a misworded `ConnectionLost`.
  - The poll is deleted with the `opc-live-source` slice, which gives the live source a failure route of its
    own.
- **The mapper decides connection class.** `ArchiveExceptionMapper` maps SQLSTATE class `08` and `57P01`,
  `57P02`, `57P03`, `53300` to `Unreachable`. The fired client bound stays `Unreachable` and `57014` stays
  `QueryTimedOut`. The change applies to every read and, through `MapWrite`'s fall-through, to a write that
  meets a restart.
- **`PenCatalogueSync` is the source; `TrendWindow.Build` wires it.** `TrendWindow.Build` builds the
  catalogue sync ahead of the status bar, hands its `ConnectionStates` to the bar, and calls
  `catalogueSync.ReadNow()` after `Start()`, as it does for the markers.
- **The poll and the coordinator stop carrying state.** `IDataProvider.ConnectionFaults`,
  `TrendCoordinator.ConnectionFaults`, `PostgresDataProvider`'s subject and `Dispose`, and the state half of
  `RealtimePoll` go. The poll keeps its statements, its 10 s tick bound, its samples, its failure warning
  and its count.
- **The catalogue read has a bound of its own.** `PostgresDataProvider` builds the catalogue command with
  `CommandTimeout = 5`, through an internal factory pinned by a unit test, the way `RealtimePoll.CreateTickCommand`
  is pinned.

## Technical Details

- `ArchiveReadLoop<T>` state, all touched on the UI thread only:
  - `_consecutiveFailures` (exists) counts every failed read and drives the panel route;
    `_consecutiveConnectionFailures` counts connection-class failures in a row and resets on a success and
    on any other kind; `_lastReportedState`, the last state the sink got or null, serves the sink.
  - On success: reset both counts; with a sink, when `_lastReportedState` is not `Connected`, record and
    emit `Connected`; then call `_apply`. The state goes out
    before the apply, so an apply that throws leaves the state right.
  - On a failure whose error is `ArchiveError { Kind: Unreachable }`: increment both counts, reset nothing,
    log a warning with the run under the key `ConsecutiveConnectionFailures`. With a sink, when the run reaches `ConsecutiveFailuresBeforeFault` and no
    fault is raised, record and emit a `ConnectionLost` state built from the failing error's
    `Host`, `Port` and `Database`, its detail `ConsecutiveFailuresBeforeFault.ToString(CultureInfo.InvariantCulture)`.
    No connection-class failure goes to the panel from either loop.
  - On any other failure: increment `_consecutiveFailures`, reset `_consecutiveConnectionFailures`; below the
    threshold log the warning as today, at or past it `ReportFailure` as today. A run
    `[Unreachable, Unreachable, TableMissing]` writes one `TableMissing` entry and raises nothing;
    `[TableMissing x3, Unreachable]` writes one `TableMissing` entry and raises nothing.
  - An error that is not an `ArchiveError` is not connection class.
  - A thrown read stays as it is: one `TryReportFailure` entry at once, counts unchanged.
- `ArchiveConnectionState` keeps its shape. Its doc says what a reader of the archive reports about the
  connection, names no UI class, and drops the `DistinctUntilChanged` warning and the per-subscription
  sentence. The classification and the `ConnectionLost` construction stay private to the loop, their one
  caller.
- `ArchiveFault.ConnectionLost` doc: "A run of consecutive reads failed for lack of a connection. Detail is
  the number of failures that raised it." `ArchiveFault.Unreachable` doc gains "or a server that refuses the
  session (SQLSTATE class 08, 57P01-57P03, 53300)". `ArchiveError.Describe`'s arm: "The {archive} stopped
  answering after {detail} consecutive failed reads." The `ConnectionLost` resx texts stay.
- The `Unreachable` resx texts widen to the causes the kind now carries, a fired read bound and a server out
  of connection slots among them (`Resources.resx:292-298`, `Resources.ru.resx:292-298`):
  - `FailureArchiveUnreachableDetail`: "SemiPlot could not open a connection to {0}, or the connection did
    not answer in time." / "Не удалось подключиться к {0}, или подключение не ответило вовремя."
  - `FailureArchiveUnreachableRemedy` gains a last sentence: "Check that the server has a free connection
    slot (max_connections)." / "Проверьте, что у сервера есть свободные подключения (max_connections)."
  - The title stays.
- `ArchiveExceptionMapper.MapSqlState`: `PostgresErrorCodes.AdminShutdown`, `CrashShutdown`,
  `CannotConnectNow`, `TooManyConnections` and any SQLSTATE starting with `08` map to `Unreachable`.
- `PostgresDataProvider`:
  - `private const int CatalogueCommandTimeoutSeconds = 5;` and
    `internal static NpgsqlCommand CreateCatalogueCommand(NpgsqlConnection connection)`, used by
    `QueryPensAsync`.
  - `PostgresConnectionSettings.CommandTimeoutSeconds`' summary names both overrides: the poll's tick and
    the catalogue read.
  - The class drops `IDisposable`: `Dispose` had no job but completing the subject.
- Time to the fault after an outage starts, as estimates for the Post-Completion measurement:
  - a refused socket: each read fails at once, so the third failed read lands 10 to 15 s in, depending on
    where in the 5 s wait the outage begins;
  - a stalled server on a pooled connection: the first read waits the 5 s bound and then Npgsql's
    cancellation (ASSUMPTION: Npgsql sends a cancel request when `CommandTimeout` fires, waits at most its
    2 s `CancellationTimeout`, and then breaks the connection). The next reads open a new physical connection
    unless an idle pooled one is free, and against a paused server that open waits the 15 s connect bound.
    So the range is 7 + 5 + 7 + 5 + 7 = 31 s with spare idle connections, up to 7 + 5 + 15 + 5 + 15 = 47 s
    without;
  - dropped packets with no pooled connection: each read waits the 15 s connect bound, so about
    3 x 15 s + 2 x 5 s = 55 s. The poll at HEAD takes about 3 x (15 s + 1 s) = 48 s here.
  - The documentation states the mechanism (three reads 5 s apart, each under the 5 s statement bound and the
    15 s connect bound), not a total.
- The 5 s bound also covers the start sequence's catalogue read (`StartupProbe.cs:63`). A server that
  answers the catalogue more slowly than 5 s at start, a lock held by a provisioning run for one, now fails
  the start as `Unreachable` ahead of the probe's 30 s `StartupReadTimedOut` bound. The failure window's
  retry is the remedy; one statement keeps one bound.
- `TrendWindow.Build` order: coordinator, marker sync, chart, minimap, catalogue sync, status bar (over
  `catalogueSync.ConnectionStates` and the chart's navigation), navigation bar, legend, applier,
  `MainWindowViewModel`. Then `catalogueSync.Start(); catalogueSync.ReadNow(); markerSync.Start();
  markerSync.ReadNow();`. `Dispose` runs the reverse, so the bar is disposed before the sync. Fields and
  constructor parameters follow the new order. The bar subscribes in its constructor, before `Start()`, so
  no state precedes it.
- `AppStatusBarViewModel` keeps its handler and its `try/catch`. The comment at `:84-85` names the new
  reason: the sync raises the state from inside the read loop, ahead of the read's apply, so a throw out of
  the handler would skip that read's apply. The comment at `:100-101` names the first catalogue read as the
  first `Connected`.
- `RealtimePoll`: `ReadOnceAsync` returns `IReadOnlyList<Sample>`, and `RealtimeTick` goes. A failure
  returns no sample and logs the warning with its running count, as at `:231-235`. `_faultRaised`,
  `_reportedConnected`, the state half of `Succeed` and `Fail`, and the `settings` parameter go; the
  constructor call in `PostgresDataProvider.PollAsync` and `RealtimeArchive.NewPoll` drop it. The comment on
  `TickCommandTimeoutSeconds` names its remaining job: a stalled tick holds the live edge for at most the
  bound.
- The baseline log lines carry the structured key `SubscribedPenCount` in place of `PenCount`, so a test
  can tell them from the history read's `PenCount`. Both lines keep their wording otherwise.
- Integration arming: `ArmedGate` implements `ILoggerProvider` over an optional inner provider, to which
  it forwards every entry; it counts entries whose structured state carries `SubscribedPenCount`, and
  `Armed(n)` completes at the nth such line. Over an archive with no row for the subscribed pens, the
  "no archived row yet" line repeats every tick, so the gate counts lines, not subscriptions; every caller
  arms its subscriptions one at a time over a seeded archive, or awaits only the first line. The gate also
  records the highest `ConsecutiveFailures` it saw, and `RealtimeSubscriptionTests` asserts it stayed below
  `3`, the same tolerance HEAD's "no fault seen" had. `FailuresReached(n)` completes when an entry carries
  `ConsecutiveFailures >= n`; `RealtimeSubscriptionDisposalTests` awaits it on the running provider. `UnreachableArchive.Services` takes an optional
  `ILoggerProvider` and sets `Debug` with it, as `ArchiveProviderFactory.Build` does.
- `FakeDataProvider` gains `ArchiveFault PensFailureKind { get; set; } = ArchiveFault.Unreachable`, read
  when `FailPens` is set, through `BenchArchiveError`. It loses `ConnectionFaults`,
  `ConnectionFaultsObserverCount` and `ReportConnectionState` in Task 5.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests and documentation in this repository.
- **Post-Completion** (no checkboxes): the timed outage on the demo stand and the remaining slices.

## Implementation Steps

### Task 1: Make the catalogue read loop the source of the connection state

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/ArchiveReadLoop.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/PenCatalogueSync.cs`
- Modify: `SemiPlot/SemiPlot.UI/Markers/MarkerSync.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/ArchiveConnectionState.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveFault.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveError.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/PenCatalogueSyncTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Markers/MarkerSyncTests.cs`

- [x] (review 84aafa5 merged the two flags into one `_lastReportedState`)
      add the `reportConnectionState` sink, the second counter, `_faultRaised` and `_reportedConnected` to
      `ArchiveReadLoop<T>`; emit on success before `_apply`; route a failure by kind as Technical Details
      states; update the class summary (`ArchiveReadLoop.cs:19-23`): without a sink, a connection-class
      failure only logs, because the catalogue loop's sink carries it to the bar
- [x] give `PenCatalogueSync` a `Subject<ArchiveConnectionState>` exposed as `ConnectionStates`, passed to
      the loop and completed in `Dispose`; `MarkerSync` passes `null` (`MarkerSync.cs:40-41`), and its class
      summary (`MarkerSync.cs:16-19`) says a connection-class read failure reaches the panel through the
      catalogue read's state, every other failure directly
- [x] update the docs of `ArchiveConnectionState`, `ArchiveFault.ConnectionLost` and the `ConnectionLost`
      arm of `ArchiveError.Describe`
- [x] add `FakeDataProvider.PensFailureKind`
- [x] rewrite `PenCatalogueSyncTests.TwoFailedReads_ReportNothing_TheThirdReportsOnce_AndTheNextSuccessCarriesEverythingSince`
      (`:141-158`) and `AReadThatThrows_AddsOnePanelEntryAtOnce_AndTheNextReadComesOneIntervalLater`
      (`:195-215`) for the new route: the third `Unreachable` emits `ConnectionLost` and adds no entry;
      `ASuccess_ResetsTheFailureCount` (`:161-174`) asserts that no `ConnectionLost` state is emitted
- [x] write `PenCatalogueSyncTests` cases: the first success emits `Connected` once and later successes emit
      nothing; a fourth `Unreachable` emits nothing; the first success after the fault emits `Connected` and
      still carries everything since; `TableMissing` three times emits no state and one entry;
      `[Unreachable, Unreachable, TableMissing]` and `[TableMissing x3, Unreachable]` each give one
      `TableMissing` entry and no state; a deltas subscriber that throws still gets `Connected` out first;
      `Dispose` completes `ConnectionStates`; a held read that fails after `Dispose` emits nothing
- [x] `MarkerSyncTests.ADiscardedRead_CountsAsNoFailure` (`:92-110`) and
      `TwoFailedReads_LogTheMarkerRead_TheThirdReportsOnce` (`:178-196`) fail with
      `BenchArchiveError(ArchiveFault.TableMissing, "semiplot_markers")`; add a case: three `Unreachable`
      marker reads add no entry
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before Task 2

### Task 2: Bound the catalogue read and widen the connection class

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveFault.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Integration/CatalogueReadUnreachableTests.cs`

- [x] add `CatalogueCommandTimeoutSeconds = 5` (private) and `CreateCatalogueCommand`; build the command in
      `QueryPensAsync` (`:110`) through it
- [x] update the summary of `PostgresConnectionSettings.CommandTimeoutSeconds` (`:26-28`)
- [x] map class `08`, `57P01`, `57P02`, `57P03` and `53300` to `Unreachable` in `MapSqlState` (`:89-104`);
      update `ArchiveFault.Unreachable`'s doc (verified 2026-10-19: no test pins `57P0x`, `53300` or a class
      `08` SQLSTATE, `git grep -n -E "57P0|53300" -- SemiPlot` prints only unrelated lines)
- [x] widen the `Unreachable` detail and remedy in both resx files as Technical Details states; any
      `ArchiveFailureMapperTests` case that compares the rendered text follows
- [x] write `ArchiveStatementTextTests.TheCatalogueCommandCarriesItsOwnBoundAndNotTheDataSourceBackstop`,
      restating `5` rather than reading the constant, beside the tick test (`:219-233`)
- [x] write `ArchiveExceptionMapperTests` theory cases for `08006`, `57P01`, `57P02`, `57P03`, `53300` to
      `Unreachable`, `57014` to `QueryTimedOut`, and one write through `MapWrite` with `57P01` to `Unreachable`
- [x] write `CatalogueReadUnreachableTests` (no collection, no fixture, like `RealtimePollTests`):
      `QueryPensAsync` over `UnreachableArchive.Services` answers `ArchiveError { Kind: Unreachable }` naming
      the configured host, port and database
- [x] run the unit project and `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~PostgresCatalogReadTests|FullyQualifiedName~LiveCatalogueTests|FullyQualifiedName~CatalogueReadUnreachableTests"`
      - must pass before Task 3

### Task 3: Arm the integration tests on the poll's own log lines

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ArmedGate.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/UnreachableArchive.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeEmptyArchiveTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionDisposalTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] rename the baseline lines' key `PenCount` to `SubscribedPenCount` (`RealtimePoll.cs:129-134`)
- [x] rewrite `ArmedGate` as a forwarding `ILoggerProvider` over the baseline lines, recording the highest
      `ConsecutiveFailures`; its summary says it counts lines; give `UnreachableArchive.Services` an optional
      `ILoggerProvider` that sets `Debug`
- [x] pass the gate to `ArchiveProviderFactory.Build` in `RealtimeSubscriptionTests` (`:39-46,80-97`; in
      `DisposingASubscriptionStopsItsPoll` the gate wraps the existing `FakeLoggerProvider`),
      `RealtimeEmptyArchiveTests.ASubscriptionOverAnArchiveWithNoRowReportsConnected` (renamed
      `...LogsItsBaseline`) and `LiveEdgeArchiveJourneyTests.OpenStandAsync` (`:165,184`); the
      `NoFaultWasSeen` assertion (`RealtimeSubscriptionTests.cs:107-109`) reads the highest
      `ConsecutiveFailures` below `3`; the class comment (`RealtimeSubscriptionTests.cs:15-17`) names the
      baseline log line as the awaited emission in place of `Connected`
- [x] rewrite `RealtimeSubscriptionDisposalTests`: the running provider's gate awaits `FailuresReached(3)`, and after the same margin as HEAD the dropped provider's highest
      `ConsecutiveFailures` is below `3` and it delivered no batch
- [x] run the integration filter `FullyQualifiedName~Realtime|FullyQualifiedName~LiveEdgeArchiveJourneyTests`
      - must pass before Task 4

### Task 4: Feed the status bar from the catalogue sync

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/TrendWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/TrendWindowBuildTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppMainWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`

- [x] write `TrendWindowTests.ThreeUnreachableCatalogueReads_TurnTheBarToFault_WithOneConnectionLostEntry`
      and `TheWindowReadsTheCatalogueAtOnce`; run them against the unchanged `Build` and record that both fail
- [x] reorder `TrendWindow.Build`, `Dispose`, the fields and the constructor as Technical Details states;
      hand `catalogueSync.ConnectionStates` to the bar; add `catalogueSync.ReadNow()` after `Start()`
- [x] delete `TrendCoordinator.ConnectionFaults`, its subject, its subscription and their disposal
      (`TrendCoordinator.cs:21-22,28,45-48,63-67,74,80`), and edit the `RealtimeFailures` summary's `cref`
      (`:59`) rather than deleting it; build and confirm that the compiler names every remaining consumer,
      then fix each
- [x] rewrite the comments in `AppStatusBarViewModel` (`:84-85`, `:100-101`) and `ArchiveFailureMapper`
      (`:180-181`)
- [x] ➕ rewrite the two `AppStatusBarViewModelTests` comments (`:101-102`, `:125-126`) that named the
      coordinator's bare forwarding subscription
- [x] update the tests that read the coordinator's stream: delete `TrendCoordinatorTests`' five connection
      cases (`:116,133,151,168,214`) and `TrendChartViewModelTests.DisposingTheChart_LeavesTheConnectionFaultsForwarding`
      (`:75-85`); `TrendWindowTests.DisposingTheWindowClosesTheLiveEdgeBeforeTheConnectionStream` becomes
      `DisposingTheWindowClosesTheLiveEdge` over `OpenLiveSubscriptionCount`; `AFaultReportedAsTheLiveEdgeOpens_ReachesTheStatusBar`
      (`:92-111`) is replaced by the new fault test; `AppMainWindowTests` drops its two
      `ConnectionFaultsObserverCount` assertions (`:43,51`)
- [x] `TrendWindowBuildTests.TheStatusBar_FollowsTheCoordinatorsConnectionState` (`:74-100`) becomes
      `TheWindowReadsTheCatalogueAtOnce_OnTheRealScheduler`: it drops the pushed fault and asserts only that
      `PensQueryCount` rose by one after `Dispatcher.UIThread.RunJobs()`; the class summary (`:27-31`) names
      the catalogue read as the bar's source
- [x] `MainWindowViewModelTests` (`:105,128,147`) asserts the rise of `PensQueryCount` against a count taken
      after the stand is built, not an absolute value; check `TrendWindowTests.DisposingTheWindowStopsTheCatalogueRead`
      and `TheChart_ReadsAndWritesThroughTheMarkerSyncTheWindowStarts` against the extra read at start;
      the comment at `MainWindowTestBuilder.cs:134` says the catalogue loop has made its first read
- [x] remove the usings the deletions leave dead (IDE0005 fails the build): in `TrendCoordinatorTests.cs`
      `SemiPlot.Core.Data.Errors` and `SemiPlot.Tests.Unit.UI.PenEditor`; in `TrendWindowBuildTests.cs`
      (`:12,13,15`) `SemiPlot.Core.Data`, `.Errors` and `UI.PenEditor`; in `TrendWindowTests.cs` (`:8`) unless
      the new fault test uses `ArchiveFault`; whichever the build names
- [x] run the unit project - must pass before Task 5

### Task 5: Delete the poll's connection state

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeArchive.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeEmptyArchiveTests.cs`

- [x] run `git grep -n "ConnectionFaults" -- SemiPlot` and record the hits: after Tasks 3 and 4 only the
      interface, `PostgresDataProvider` and `FakeDataProvider` remain; delete `IDataProvider.ConnectionFaults`
      and its comment (`IDataProvider.cs:12-15`) (recorded: those three plus `PostgresCompositionTests.cs:142`,
      which this task deletes; nothing after)
- [x] delete the subject pair, `ConnectionFaults`, `Dispose`, `_disposed` and `IDisposable` from
      `PostgresDataProvider` (`:49-53,60,80,83-87,246-260`) and the `StateChange` push from `PollAsync`
      (`:400-403`); fix the class summary
- [x] reduce `RealtimePoll` as Technical Details states, the `settings` parameter included, and drop it from
      `PostgresDataProvider.PollAsync` (`:374-380`) and `RealtimeArchive.NewPoll` (`RealtimeArchive.cs:42-51`);
      rewrite the comment at `RealtimePoll.cs:37-38`
- [x] remove the usings the deletions leave dead (IDE0005 fails the build): `System.Globalization`, the
      `SemiPlot.Core.Data`/`.Errors` usings and `SemiPlot.DataSource.Postgres.Configuration` (`:12`) in
      `RealtimePoll.cs`; `System.Reactive.Subjects` in `PostgresDataProvider.cs`; `SemiPlot.Core.Data`,
      `.Configuration` (`:11`) and `Xunit` (`:14`) in `RealtimeArchive.cs`; `RealtimePollReadTests.cs:5-6`;
      `SemiPlot.Core.Data.Errors` in `RealtimePollTests.cs`; `System.Reactive.Disposables` and
      `System.Reactive.Subjects` in `FakeDataProvider.cs` (`:3,5`); whichever the build names
- [x] delete `FakeDataProvider.ConnectionFaults`, `ConnectionFaultsObserverCount` and `ReportConnectionState`
- [x] delete `PostgresCompositionTests.DisposingTheProviderCompletesTheConnectionFaultStream` and
      `DisposingTwiceDoesNotThrow` (`:135-160`)
- [x] integration: `RealtimeArchive.TickAsync` and its local `ConsecutiveFailuresBeforeFault` go
      (`RealtimeArchive.cs:25-27,53-63`); every `.Samples` read becomes the returned list;
      `RealtimePollTests` keeps `ASingleFailure...` as `AFailedTickEmitsNoSample` and replaces the ladder case
      with one that reads the warning's `ConsecutiveFailures` as 1 to 5 over five ticks;
      `RealtimePollReadTests` drops every `StateChange` assertion (`:46,77,94,144,207,251-277`), renames
      `TheFirstTickEmitsNoSampleAndReportsTheSubscriptionArmed` to `TheFirstTickEmitsNoSample` and
      `TheFirstSuccessAfterARaisedFaultReportsConnectedExactlyOnce` to
      `TheFirstTickAfterTheTableReturnsTakesTheBaseline`, keeping its sample assertions and ticking three
      times without `TickAsync`; `RealtimeEmptyArchiveTests` drops its `StateChange` assertions (`:36,41`)
- [x] run `dotnet test SemiPlot.slnx` - must pass before Task 6

### Task 6: Verify acceptance criteria

- [x] verify the Overview against the code: `git grep -n "ConnectionFaults" -- SemiPlot` prints nothing;
      `git grep -n "ArchiveConnectionState" -- SemiPlot/SemiPlot.DataSource.Postgres` prints nothing
      (both print nothing)
- [x] verify the scope guard: `git diff origin/master --name-only` lists only the files of Tasks 1 to 5 and
      7 and this plan; `git diff origin/master -- SemiPlot | grep -i -c "opc"` prints `0`;
      `ArchiveStatements.cs` is not in the list (41 files of Tasks 1-5 plus this plan, Task 7 not run yet;
      `opc` count 0; no `ArchiveStatements.cs`)
- [x] run both Acceptance Evidence commands and check each "must show" item (unit filter 159/159,
      integration filter 20/20; every item matched to an assertion)
- [x] run `dotnet build SemiPlot.slnx` (0 warnings) and `dotnet format SemiPlot.slnx --verify-no-changes`
      (`--no-incremental`: 0 errors, only the 2 pre-existing NU1902; format exit 0)
- [x] run `dotnet test SemiPlot.slnx` (as two foreground project runs: unit 2296/2296 on the second run,
      integration 196/196)
- [x] run `dotnet terse` over every touched `.cs` file; it exits 0 (39 files, exit 0)
- ⚠️ the first full unit run failed one test outside this slice,
  `PenEditorViewTests.ANameTypedWhileARefreshRuns_ReachesNoFieldAndWritesNothing` ("Expected
  name.IsEffectivelyEnabled to be True because the rebuild finished, but found False", run 1 m 35 s); the
  class passed 5 of 5 runs alone and the full rerun passed 2296/2296 in 50 s. No PenEditor file is in this
  branch; `docs/plans/completed/20260929-window-per-process.md:1183` names the same test as timing-sensitive

### Task 7: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`
- Modify: `AGENTS.md`

- [x] `data-integration.md:45`: drop `ConnectionFaults` from the interface listing
- [x] `data-integration.md:998-1019`: rename "The connection state the poll reports" to "The connection
      state the catalogue read reports" and rewrite it: the producer, the connection class and its SQLSTATEs,
      the one rule for the three readers and its three residual cases, the first `Connected` at window open,
      and the fault mechanism without a total
- [x] `data-integration.md:1027`, `:1033`, `:1034`, `:1069`, `:1075`, `:1110`, `:1140-1142`: the
      `ConnectionLost` row names the catalogue reads; the catalogue and marker rows say a connection-class
      failure goes to the bar's entry; the `Unreachable` row lists the widened SQLSTATEs; "because the poll
      loop retries" becomes "because the reads retry"; `#no-failure-stops-at-the-log` names the rule and the
      three residual cases
- [x] `data-integration.md:1268-1270`: two reads override the backstop, the poll's tick (10 s) and the
      catalogue read (5 s); `:1323-1327`: the start sequence's catalogue read fails as `Unreachable` past 5 s,
      ahead of the 30 s caller bound
- [x] `overview.md:124-146`: the new build order, the bar over the catalogue sync's stream, the
      coordinator paragraph replaced by the subscribe-before-`Start()` rule, and the start sentence (`:146`)
      naming the catalogue sync's first read; `:337-338`: the provider no longer reports a connection state
- [x] `overview.md:611-628` (#the-read): one read at window open, the 5 s bound; `:699-704`
      (#a-failed-read): the two routes and the state; `:713-716`: the bar takes the sync's stream;
      `:731` and `:741-749` (the live markers): a connection-class marker failure goes to the bar's entry
- [x] `charting.md:801-805`: the connection state bullet names `PenCatalogueSync`, not the coordinator
- [x] `trend-interaction.md:818-823`: the catalogue sync no longer reports every 5 s during an outage
- [x] `ArchiveStatementTextTests.cs:219-220`: the tick test's comment no longer names the fault threshold
- [x] `AGENTS.md:360-364`: `PenCatalogueSync` reads once at start too and is the source of the archive
      connection state the status bar shows
- [x] `AGENTS.md:376-382`: the failure rule gains the one rule for connection-class failures (they reach the
      panel through the bar's `ConnectionLost`) and a pointer to the residual cases in
      `data-integration.md#no-failure-stops-at-the-log`
- [x] update every `#the-connection-state-the-poll-reports` link the rename breaks
      (`git grep -n "connection-state-the-poll-reports"` prints nothing afterwards) (no tracked doc linked
      it; the grep prints only these two plan lines, and the untracked roadmap's link at its line 159 stays
      by operator order)

## Post-Completion

**Manual verification:**

1. Start the demo stand: `dotnet run --project SemiPlot/SemiPlot.AppHost`, with a pen drawn in the
   minimap. The status bar reads connected and the panel is empty.
2. Stop the stand's bench PostgreSQL container with `docker stop <the stand's bench container>`. Never touch
   `scada-capture`.
3. Within 10 to 15 s the indicator reads the fault. The panel holds one `Warning` entry, "The archive stopped
   answering", and at most one `Unreachable` entry from the minimap's band read.
4. Wait 30 s. Neither entry gains a repeat or a new timestamp: the loops add nothing, and the minimap logs
   its repeats.
5. `docker start` the container. Within 5 s of the server answering the indicator reads connected and one
   `Info` entry, "The connection to the archive is restored", appears.
6. Time a stalled server: `docker pause` the bench container and record the seconds to the fault. Write the
   measured figure into `data-integration.md` only as a measurement with its date. The roadmap's accepted
   figure is about 30 s; a measurement above 40 s goes to the operator for a decision on the bound before it
   enters the documentation.

**Remaining slices** (docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md):

- live-source-port: a Core live-source port with the poll as its first implementation;
  `IDataProvider.Subscribe` leaves `IDataProvider`.
- opc-demo-server: an OPC UA server in the ArchiveSeeder demo writer with the Simple-Scada address space,
  port 49875.
- opc-live-source (Closes #116): `SemiPlot.DataSource.OpcUa` feeds the live edge; the poll is deleted.
- opc-settings: an OPC block after the DB block in the settings window.
- live-hold-to-edge: a grey pending gap and a render-only hold of the last value to the edge.

**Executed by exec:**

- branch: archive-state-from-catalogue

Final state at `83301f5`, after the review rounds (Task 6's figures above are from `1de694f`): unit
2297/2297, integration 196/196, `dotnet terse` over 40 touched `.cs` files exit 0, 49 files in
`git diff master...HEAD`.

## Verify it yourself

Run from the repository root on `archive-state-from-catalogue`. The integration run needs Docker.

1. Run the two Acceptance Evidence commands. Expect 160/160 for the unit filter and 20/20 for the
   integration filter.
2. Run the guards. Each prints nothing:
   `git grep -n "ConnectionFaults" -- SemiPlot` and
   `git grep -n "ArchiveConnectionState" -- SemiPlot/SemiPlot.DataSource.Postgres`.
3. One outage, one entry. At `cf21d86` the catalogue loop wrote its third `Unreachable` failure to the
   panel and the poll wrote its own `ConnectionLost` beside it. Now
   `TrendWindowTests.ThreeUnreachableCatalogueReads_TurnTheBarToFault_WithOneConnectionLostEntry` asserts
   one `ConnectionLost` entry, no restamp on a fourth failure, and the `Info` entry on recovery. Task 4 ran
   it against the unchanged `TrendWindow.Build` at `338820a`: it failed with `IsConnected` still `true`.
   That run is recorded, not repeated here; the two test cases do not exist on `master`.
4. The read at window open. `TrendWindowTests.TheWindowReadsTheCatalogueAtOnce` failed at `338820a` with
   `PensQueryCount` 0 and passes now.
5. The catalogue bound. At `cf21d86` `QueryPensAsync` builds its command with no `CommandTimeout`. Now
   `ArchiveStatementTextTests.TheCatalogueCommandCarriesItsOwnBoundAndNotTheDataSourceBackstop` pins 5.
6. The connection class. `ArchiveExceptionMapperTests` maps `08006`, `57P01`, `57P02`, `57P03` and `53300`
   to `Unreachable` and keeps `57014` as `QueryTimedOut`. At `cf21d86` all five fell through `MapSqlState`
   to `ReadFailed`.
7. A missing table is no lost connection. At `cf21d86`
   `RealtimePollReadTests.TheFirstSuccessAfterARaisedFaultReportsConnectedExactlyOnce` asserted
   `ConnectionLost` for a renamed `trends` table. Now the `PenCatalogueSyncTests` `TableMissing` cases
   assert one `TableMissing` entry and no state.
8. On the stand, follow the Manual verification steps above. Expect one behaviour the plan specifies: if the
   server returns but answers the catalogue with another kind (`TableMissing`, `AccessDenied`), the
   indicator keeps the fault until a read succeeds, beside that kind's own entry
   (`ATableMissingReadAfterTheFault_LeavesTheFault_AndReachesThePanelAtOnce`).
