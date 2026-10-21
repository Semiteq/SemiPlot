# OPC UA live source

## Overview

- The chart's live edge moves from the archive poll to the SCADA's built-in OPC UA server. A new project,
  `SemiPlot.DataSource.OpcUa`, implements `ILiveSource` with `OpcUaLiveSource`: one session per process to
  `opc.tcp://<host>:<port>`, read from a new `opc/` section folder, one OPC UA subscription per pen set at a
  100 ms publishing interval, a monitored item per pen with a queue of 1.
- While the session is up, every point past a pen's newest archived row comes from OPC UA. The archive
  replaces a live point only when a history read covers it, under the rule `EnvelopeLine.ReplaceColumns`
  applies today. Nothing re-reads history when the archive catches up.
- Breaks: the source writes one for every pen it delivered when the session is lost and one for a pen whose
  notification is `Bad`; the chart writes one ahead of a pen's first live sample when that sample lies past
  the pen's last column. `Uncertain` draws as a value.
- OPC down is a second status-bar indicator and one `Warning` in the message panel through
  `ArchiveFailureMapper`, with a new public error type in `SemiPlot.Core/Data/Errors`. The source reconnects
  in the background and survives a server restart that destroys its session. An `opc/` folder that does not
  read, parse or validate is a startup failure; an unreachable server is not.
- Deleted: `PostgresLiveSource`, `RealtimePoll`, the `RealtimePoll` and `RealtimeBaseline` statements,
  `poll_interval_ms` with its settings field and resx keys, and the tests that only exercise them.
- The demo stand serves its OPC UA on 55875: `converge` writes `opc/opc.yaml` with that port, as it writes
  `connection/connection.yaml` with 55432, and the stand's writer holds its archive back by 60 s.
- Closes #116. The 1 s figure is measured by hand on the demo SCADA from a debug line; it is not asserted.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice opc-live-source (#116)

- Verified at `master` `a92c12f` (the demo server merged as #144). Paths are under `SemiPlot/` unless they start with `docs/`, `ConfigFiles/`,
  `Installer/`, `AGENTS.md` or `SemiPlot.slnx`.
- The port: `ILiveSource` has one member, `Subscribe(IReadOnlyList<int> penIds)`
  (`SemiPlot.Core/Data/ILiveSource.cs:6-9`). `Sample(int PenId, DateTime TimestampUtc, double? Value)`, a null
  value is a break (`SemiPlot.Core/Trends/Sample.cs:3-4`). Its contract (cold per call, never completes, never
  faults, a break is a null sample, UTC, an unserved id emits nothing) is in
  `docs/architecture/data-integration.md:955-971`, and the open question this slice settles, the break ahead
  of a first live sample, is stated at `:982-988`.
- The coordinator: `_penIds.Select(_liveSource.Subscribe).Switch().Buffer(BatchWindow, _dataScheduler)`
  (`UI/Bridge/TrendCoordinator.cs:97-110`); `SetPens` pushes a new set (`:68-71`). The chart calls `SetPens`
  with every pen it shows, hidden ones included, only when the id set changes
  (`UI/Chart/TrendChartViewModel.cs:529-542`), that is when a catalogue read adds or removes a pen. A new pen
  gets a new `TrendPenState` (`UI/Chart/ChartPenSet.cs:97`); a kept pen keeps its state and its line.
- The chart's live path: `ChartRealtimeApplier.ApplyBatch` sends a null to `TrendPenState.AppendRealtime` and a
  value to `FoldRealtime` at a coarse layer or `AppendRealtime` at Raw (`UI/Chart/ChartRealtimeApplier.cs:19-48`),
  then hands the union's last timestamp to `ChartNavigationController.OnLiveEdge`
  (`ChartRealtimeApplier.cs:9-17`, `ChartNavigationController.cs:179-191`). `TrendPenState.AppendRealtime`
  appends a `NaN` column for a null and sets `CurrentValue` only for a value (`UI/Chart/TrendPenState.cs:77-83`).
- `EnvelopeLine`: `AppendAfterLastColumn` rejects a column at or before the last X (DA-7)
  (`UI/Chart/EnvelopeLine.cs:271-286`); `FoldIntoLastColumn` appends past a gap and folds otherwise
  (`:226-252`); `ReplaceColumns` keeps the live tail past the old and the new history end and drops every
  column past the read's requested end (`:195-214`). A `NaN` column ends a segment, and a stepped line is
  not held to it (`UI/Chart/EnvelopePath.cs:40-50`), so a gap is drawn between the two real columns around a
  break wherever the break sits between them. The axis keeps milliseconds: `LocalTimeAxis.ToAxis` is
  `ToOADate` (`UI/Chart/LocalTimeAxis.cs:8-12`), so a break one tick from a sample maps onto the sample's X
  and is rejected; 1 ms is the smallest step (`docs/architecture/data-integration.md:1035-1044`).
- The archive poll to delete: `PostgresLiveSource` (`DataSource.Postgres/PostgresLiveSource.cs:14-88`),
  `RealtimePoll` (`DataSource.Postgres/RealtimePoll.cs:15-209`), the statements
  `ArchiveStatements.RealtimePoll` (`ArchiveStatements.cs:207-216`) and `RealtimeBaseline` (`:218-229`), its
  registration (`PostgresDataServiceCollectionExtensions.cs:44-50`), `PostgresConnectionSettings.PollInterval`
  (`Configuration/PostgresConnectionSettings.cs:17`, `:62`), the loader's `PollIntervalMs`, `PollIntervalKey`,
  `LowestPollIntervalMs` and their checks (`Configuration/PostgresConnectionLoader.cs:15,29,51,57,142,190-193,209-212`),
  the mapper's poll bound (`UI/Messages/ArchiveFailureMapper.cs:125-132`), the settings field
  (`UI/Settings/SettingsViewModel.cs:64-65,79-80,89,112,119,121,204-213,225,369`;
  `UI/Settings/SettingsDialog.axaml:31,134-149`), the resx keys `SettingsPollIntervalLabel` and
  `SettingsPollIntervalInvalid` and the poll half of `FailureConnectionFileOutOfRangeRemedy`
  (`UI/Localization/Resources.resx:282-284,582-584,606-608`, same lines in `Resources.ru.resx`), the
  `converge` writer's `poll_interval_ms` (`Tools.ArchiveSeeder/ConnectionFileWriter.cs:15-37`,
  `Tools.ArchiveSeeder/Converge.cs:17,53-63`) and `ConfigFiles/connection/connection.yaml:6`.
- Tests that only exercise the poll: `Tests.Integration/RealtimePollTests.cs`, `RealtimePollReadTests.cs`,
  `RealtimeSubscriptionTests.cs`, `RealtimeSubscriptionDisposalTests.cs`, `RealtimeEmptyArchiveTests.cs`,
  `ArmedGate.cs`, `BatchCollector.cs` (every user is a poll test or the poll journey),
  `Journeys/LiveEdgeArchiveJourneyTests.cs` (it waits on the poll's baseline log line, `:209`). Mixed:
  `ExplainPlanTests.cs:79-81,174-218,335-340`, `Tests.Unit/Postgres/ArchiveStatementTextTests.cs:28-31,107-118,199-233`,
  `RealtimeArchive.cs:22,32,34-56` (`Day`, `NextDay`, `AppendAsync` stay for `ArchiveRowReaderTests`),
  `UnreachableArchive.cs:15,24`, `ArchiveProviderFactory.cs:22,86`, `Tests.Unit/Postgres/ConnectionSettingsFactory.cs:13,27`,
  `PostgresCompositionTests.cs:54-62,95`, `UI/Di/CompositionRootTests.cs:35-41`,
  `PostgresConnectionLoaderTests.cs:32,56,135,206,220-221`, `SettingsViewModelTests.cs:55,66,82,191,313,327,396-427,436,497-512`,
  `SettingsViewTests.cs:72-73`, `ArchiveFailureMapperTests.cs:262-274`, `ConnectionFileWriterTests.cs:33,45,54`,
  `DeliveredConfigurationTests.cs:72`, `Core/Configuration/ConfigurationSectionWriterTests.cs:135`. Every test
  builds `PostgresConnectionSettings` with named arguments; the one positional construction is the loader's
  (`PostgresConnectionLoader.cs:135-143`).
- Configuration: a section folder is read whole, top-level `*.yaml` only (`Core/Configuration/ConfigurationSection.cs:40-67`),
  so a subfolder of `opc/` is never parsed. `ConfigurationSectionName` is `App | Connection`
  (`Core/Configuration/ConfigurationSectionError.cs:6-10`); `ConfigurationSectionFailureMapper` switches on it
  for the title (`UI/Messages/ConfigurationSectionFailureMapper.cs:18-26`) and treats every non-`App` section
  as the connection for two remedies (`:67-80`). `FailureSeverityTests.EveryEnumMapperArm_AnswersForEveryMemberOfItsEnum`
  iterates the enum (`Tests.Unit/UI/Messages/FailureSeverityTests.cs:76,108-113`).
- `ConnectionFileError(path, kind, fields)` with `ConnectionFileProblem` `Unparseable | MissingField |
  OutOfRange | HostNotIPv4` (`Core/Data/Errors/ConnectionFileError.cs:5-31`); its texts name "the connection
  folder" and "the archive connection settings" (`Resources.resx:261-287`). `docs/architecture/ui-text.md:207-208`
  already says a second provider with bounds of its own moves them onto `ConnectionFileError`. The IPv4 rule
  and the port bounds live in `PostgresConnectionLoader` (`:53-55,113-131`) and are read by
  `ArchiveFailureMapper.cs:129-130` and `SettingsViewModel.cs:60,115,117,215`.
- Startup: `StartupProbe.Run` loads `connection/`, builds the container with `AddPostgresData(settings).AddUi()`
  and reads the catalogue and the extent (`UI/Startup/StartupProbe.cs:20,26-51`). The container is disposed
  synchronously when `Main` returns (`UI/Program.cs:48-49`, `[STAThread]` at `:16`) and asynchronously on a
  failed start (`StartupProbe.cs:114,125`). Serilog has one global minimum level and no per-category override
  (`UI/Program.cs:97-122`).
- `FailureSeverityTests.MapArmTable_CoversEveryErrorTypeThisRepositoryDeclares` scans exactly the Core and
  UI assemblies for `IError` types (`FailureSeverityTests.cs:125-134`), and `ArchiveFaultTable_CoversEveryMemberOfTheEnum`
  and `EveryArchiveFault_CarriesTheSeverityTheTableNames` pin one severity per `ArchiveFault` (`:117,137`), so an error type declared in a
  data-source project would fall into `MapUnknown` as an `Error` with every test green; a type in
  `SemiPlot.Core/Data/Errors` is caught.
- `DeliveredConfigurationTests` names its loaders one by one and checks that `converge` overwrites the shipped
  connection file (`Tests.Unit/DeliveredConfigurationTests.cs:29-85`); `ConfigFiles/**` reaches the test
  output recursively (`SemiPlot.Tests.Unit.csproj:31-36`).
- The status bar: `AppStatusBarViewModel` follows one `IObservable<ArchiveConnectionState>`, writes the fault
  to the panel and an `Info` entry on the first `Connected` after a fault (`UI/MainWindow/AppStatusBarViewModel.cs:34-47,86-120,133-140`);
  the view has one indicator (`UI/MainWindow/AppStatusBar.axaml:21-42`). `TrendWindow.Build` hands it the
  catalogue sync's states (`UI/MainWindow/TrendWindow.cs:109-114`). The panel coalesces only an entry equal to
  its newest one (`UI/Messages/MessagePanelViewModel.cs:86-89`).
- The demo server: `DemoScadaServer.StartAsync(FollowOptions, TimeZoneInfo, string certificateDirectory,
  ILoggerFactory)` binds `opc.tcp://127.0.0.1:<OpcPort>` (`Tools.ArchiveSeeder/DemoScadaServer.cs:44-48`),
  advertises the stock anonymous token policy (`:208`), and takes its start instant, the stamp of every first
  notification, at `:66`. `DemoScadaHost` starts it on a free port that is never 4840 or 49875, retries a busy
  one, fixes `Asia/Tokyo` as the archive zone and carries the 8-pen `Options`
  (`Tests.Integration/DemoScada/DemoScadaHost.cs:19,35,38-46,52-75,77-90`); `StartOnPortAsync` restarts one on a
  given port (`:72-75`). `SimpleScadaNodeIds` spells the demo's namespace, folders and pen nodes
  (`DemoScada/SimpleScadaNodeIds.cs:6-25`). `OpcTestClient` picks the `None` endpoint from `GetEndpoints` and
  opens a classic session with no `ValidateAsync` (`DemoScada/OpcTestClient.cs:51-69,153-164`). Every demo test
  joins `DemoScadaCollection` (`DemoScada/DemoScadaCollection.cs:5-10`).
- The stand: the AppHost runs `converge --config-dir <copy>` and the writer `--follow 1 --change-seconds 0.5
  --opc-port 55875` (`AppHost/AppHost.cs:23,43-58`). `converge` writes only `connection/connection.yaml`
  (`ConnectionFileWriter.cs:15-40`, `SeederCommand.cs:108-112,172-188,303-318`, `ConvergeOptions.cs:6-11`).
- The installer copies `ConfigFiles\*` recursively with `onlyifdoesntexist`
  (`Installer/SemiPlot.iss:70-71`), so an upgraded site gains `opc/opc.yaml` and keeps its own
  `connection.yaml`, whose `poll_interval_ms` the loader then ignores (`PostgresConnectionLoader.cs:63-66`).
- Measured on the real server and by the 2.0.1 spike, 2026-10-19/20 (roadmap `:41-54`; the demo-server
  plan's tables): see "Measured" below.

### Measured

Earlier measurements this plan relies on (roadmap `:41-54`, the demo-server plan's tables, 2026-10-19/20): the
real server's endpoint is `opc.tcp://WORKING-PC:49875`, `None` + Anonymous among seven endpoints, every one
carrying `PolicyId = Anonymous` on `#None`; the namespace array is `[UA, urn:WORKING-PC:SimpleScada:UAServer,
http://www.simple-scada.com/UA/Namespace, http://simple-scada.com/UA]`; `Objects` holds `Server`,
`FD34514A35<system folder>_1`, `FD34514A35root_2`, `FD34514A35group1_3`; variables are
`ns=3;s=<projectId><trends.id>`; `SourceTimestamp` is true UTC; the initial value comes at once with the last
change's stamp; after a restart every first notification carries the server's start; a queue above 1 makes
the real server send an empty first value; an unknown node gives `BadNodeIdUnknown` with `Created = false`. A
2.0.1 server needs an application certificate, binds 4840 for port 0, binds 127.0.0.1 only, dials a discovery
server without `MaxRegistrationInterval = 0`, fails a busy port with `BadNoCommunication`, starts in 1.5-4.3 s.

Measured for this plan on 2026-10-20 by a scratch probe (not committed): a client of the planned shape
(validated configuration with directory stores, `GetEndpoints`, the `None` endpoint, its URL overwritten
with the configured one, `ManagedSession`, publishing 100 ms, sampling 100 ms, queue 1) against the demo
server of `6c84be6` in process, in a child process, and behind a loopback TCP relay, on Windows; and
read-only against the real Simple-Scada on 49875.

| Question | Answer |
| --- | --- |
| Does the stack keep the configured address? | Not by default. `ConfiguredEndpoint.UpdateBeforeConnect` defaults to true: `ConnectAsync` re-reads the endpoints and the session ends on the server's own advertised URL (behind a relay on port N+1 the session's `Endpoint.EndpointUrl` read port N, and a cut of the relay never reached it). With `UpdateBeforeConnect = false` the session keeps the configured URL and every reconnect dials it |
| What does `GetEndpoints` return from the real server, dialled on 127.0.0.1? | Seven endpoints with `EndpointUrl = opc.tcp://127.0.0.1:49875/` (the 2.0.1 discovery client puts the dialled host into an advertised URL whose scheme and port match; the literal advertisement is read in the roadmap as `WORKING-PC`), each with one Anonymous token `PolicyId = Anonymous` on `#None`; the demo server's three carry `PolicyId '1'` on `#None` at `None` and `'2'` on `''` at `Sign` and `SignAndEncrypt` |
| Does the planned client connect to both? | Yes, with the token policies as advertised: real server `ConnectAsync` 928 ms, demo 146-233 ms. `KeepAliveInterval` 5000 ms, `SessionTimeout` 60000 ms |
| Real server's values | 17 variables under `root`, 21 notifications in 20 s for each changing one; `Double` for the analog pens, `Int64` for the stepped ones, `Float` with status `Good [0x00000010]` for `am_i_seen`; `SourceTimestamp` converts to `DateTime` with `Kind = Utc` |
| Server restarted in place (dispose, new server on the port after 0 s, 5 s, 70 s) | The `ManagedSession` object, its `Subscription` object and its `MonitoredItem` objects stay the same; the session id and the subscription id change (the session is recreated); the same `Notification` handlers fire again with no call by the client; no pen ever gets a stamp at or before its newest one before the outage; the unknown node stays `BadNodeIdUnknown`. The old server's dispose takes 5.1 s with a session attached; the state leaves `Connected` 3.9-4.3 s after it ends (keep-alive `BadConnectionClosed`), returns 0.4-4.7 s after the new server is up, and the first notification follows 1.0-6.1 s after the server is up |
| Server process killed (`Kill(entireProcessTree)`), restarted after 5 s | The state leaves `Connected` 4957 ms after the kill (keep-alive `BadConnectionClosed`), retries with `BadServerHalted`, and returns 1.5 s after the new process is ready; first notification 4.5 s after; no stamp regresses |
| Transport cut with the server alive (relay closes its sockets), pinned endpoint | 5 s cut: the state leaves `Connected` 4.7 s after the cut, returns on the same session id, and no notification for the cut span arrives; 70 s cut (beyond the 60 s session timeout): the session is recreated, values resume with current stamps; a relay that stalls with its sockets open is noticed after 14.7 s (`BadNoCommunication`, keep-alive read timed out) |
| Server absent at start | `GetEndpointsAsync` fails after 2.1 s (`SocketException`); `ManagedSession.ConnectAsync` on a hand-built endpoint keeps retrying inside the call and returns 7.1 s after a server appears; cancelling a pending `ConnectAsync` returns within 5 ms |
| Port held by a non-OPC listener | A listener that accepts and stays silent holds `GetEndpointsAsync` for 90 s (`BadRequestInterrupted`); one that accepts and closes fails it in 135 ms (`BadConnectionClosed`); `ConnectAsync` retries against either until cancelled |
| `ReconnectPolicyOptions.MaxTotalReconnectTime` | At 20 s the session goes `Reconnecting` to `Failover` to `Disconnected` (`BadNothingToDo`) 20 s into the outage and re-enters `Reconnecting` 30 s later; at `Timeout.InfiniteTimeSpan` it stays `Reconnecting` and recovers when the server returns |
| Close | `CloseAsync` 11-134 ms when connected and 11 ms while reconnecting; nothing reconnects afterwards |
| A pen-set change on a live session | `RemoveSubscriptionAsync` 33 ms, a new subscription over the same nodes created in 2 ms; its first notifications carry current stamps |
| Queue size on the demo server | 1 and 10 both deliver a real first value (the real server's empty first value is not reproduced) |
| Files | `ValidateAsync` and every session created no file or folder under the configured store root |
| What a restart re-delivers | On a 30 s lattice the first notification after a restart carries the new server's start instant and the value held before the outage; on a pen-set change the new subscription's first notification of every pen carries a stamp equal to the newest one already received, the value delivered again |
| What a surviving session re-delivers | After a 5 s cut the session is reactivated and an unchanged pen gets no notification until its next change; a change made inside the cut never arrives |
| What a recreated session re-delivers, the server kept running (70 s pinned cut) | No pen changed during the cut (600 s lattice): every pen's first notification after the recreation carries the value and the `SourceTimestamp` it had before the cut, and a `ServerTimestamp` equal to the recreation instant; it arrived 0.3 s after the recreation. Two changes inside the cut (30 s lattice): the first notification carries the newest change and its own stamp, 4.9 s after the recreation |
| From `Connected` again to the first notification | 0.6-3.0 s on Windows, 4.4 s on Linux after a kill; with a queue of 1 the changes inside that window arrive as the newest one only |
| The event that reports a loss | `ConnectionStateChanged` `Connected` to `Reconnecting` carries `Error = null`; the status comes on the `KeepAlive` event (`BadConnectionClosed`, `BadNoCommunication`) and on the later `Reconnecting` to `Reconnecting` events (`BadNotConnected`, `BadServerHalted`). Before `ConnectAsync` returns no session object exists, so no event reports the retries inside it |
| Linux (`mcr.microsoft.com/dotnet/sdk:10.0`), restart in place and kill | The same shape: the state leaves `Connected` 3.8-4.8 s after the loss and returns 3.9-4.8 s after the server is up; a refused loopback connect costs about 1 ms there against 2.0 s on Windows |
| Demo server start | 1.5-3.5 s warm; 16.9 s and 14.3 s for the first two runs on this machine |
| Stack log lines | The client logs one `Error` from `Opc.Ua.Bindings.UaSCUaBinaryClientChannel` ("CLIENTCHANNEL CONNECT FAILED") per failed connect attempt, 3-33 s apart while a server is down; one `Warning` "A smaller minLifetimeInterval 10000ms than session timeout 60000ms" per subscription at the stack's default lifetime count. The `Error` "The domain ... is not listed in the server certificate" comes from the demo server's own stack, not the client. After a reactivated session the client logs one `Error` from `Opc.Ua.Client.MonitoredItem`: "RepublishAsync is only supported when the session uses the classic subscription engine" |

## Development Approach

- **testing approach**: Regular (code first, then tests in the same task), with two red-first steps: the
  `FailureSeverityTests` guard in Task 3 and the `DeliveredConfigurationTests` guard in Task 5 are each
  confirmed red against the new type or folder before its arm or loader lands.
- Complete each task fully before moving to the next. Each task leaves `dotnet build SemiPlot.slnx` at 0
  errors and no warning beyond the AppHost's two `NU1902`.
- Every task includes new or updated tests for the code it changes; all tests pass before the next task.
- `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files pass; a new
  `.cs` file gets one `dotnet format SemiPlot.slnx --include <file>` (`AGENTS.md`, Build).
- Update this plan when scope changes during implementation.
- Compatibility: an installed `connection.yaml` that still carries `poll_interval_ms` keeps loading (unknown
  keys are ignored). A configuration directory without `opc/` fails at start with the section's
  `DirectoryMissing` entry; the installer adds the folder on upgrade.

## Testing Strategy

- **Unit** (`SemiPlot.Tests.Unit`, no socket): the `opc/` loader and the shipped set; the pure OPC pieces
  (endpoint choice, project folder, value mapping, loss breaks, the subscription plan with its queue size of
  1); the chart's ahead-break; the mapper arms and severities; the status bar's second indicator; the
  coordinator's republishing; the composition; `converge`'s option and writer.
- **Integration** (`SemiPlot.Tests.Integration/OpcUa/`, no container): `OpcUaLiveSource` against the
  in-process demo server through `DemoScadaHost` on a free port, in `DemoScadaCollection` beside the demo
  server's own tests, outside `ArchiveDatabaseCollection`. No test names 49875: every endpoint comes from
  `DemoScadaHost.FreePort`, and `git grep -n "49875" -- SemiPlot/SemiPlot.Tests.Integration/OpcUa` prints
  nothing. The values class runs the demo server in `Asia/Tokyo` (`DemoScadaHost.ArchiveZone`) and checks
  every `Sample.TimestampUtc` against true UTC; the CI Linux leg runs in UTC, where a stamp treated as local
  passes unnoticed, so the source itself takes no zone and touches no zone API (a `git grep` in Acceptance
  Evidence pins that).
- **Real time**: the integration tests run on the wall clock, assert values, stamps, orderings and floors on
  counts, and bound every wait; none asserts latency.
- **Server logs**: every integration assertion message carries `DemoScadaHost.ServerLog()` and the source's
  own `FakeLogCollector` records of `Warning` and above.
- No e2e suite exists. The stand and the real SCADA are checked by hand (Post-Completion).

## Acceptance Evidence

The defect: on the demo SCADA a change reaches the chart minutes to a day late (roadmap `:31-36`), because
the live edge reads the archive. It is reproduced today by the stand: with the writer run with
`--archive-lag 60`, the chart's newest live point trails the clock by at least 60 s.

Automatable, after the slice:

1. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUa"`
   passes with no container runtime:
   - every pen of the demo's 8 delivers samples whose value is the lattice value and whose `TimestampUtc`
     has `Kind = Utc` and lies within 60 s of `DateTime.UtcNow` at receipt, with the server in `Asia/Tokyo`;
   - a server restarted in place yields exactly one break per delivered pen, stamped 1 ms after that pen's
     newest delivered stamp, before any sample of the new session, then samples stamped at or after the new
     server's start; the states read `Connected`, a `LiveSourceFault.Unreachable` fault, `Connected`;
   - a source started with no server reports `Connecting` and then the `Unreachable` fault after its grace,
     and reaches `Connected` once a server starts on that port;
   - a subscription with two unknown pens yields one `PensUnserved` fault naming both, and the other pens keep
     delivering;
   - `Dispose()` on a connected source returns within its bound under a `SynchronizationContext` that drops
     every posted continuation;
   - a pen-set change on a live session writes no break;
   - the journey: a value served over OPC UA reaches `TrendChartViewModel` as a live column with a `NaN`
     column 1 ms ahead of the pen's first one, and the navigation's live edge moves to the newest stamp.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` passes, including
   `SubscriptionPlanTests` (queue size 1), `EndpointChoiceTests` (both servers' advertisements),
   `TrendPenStateTests` (the ahead-break), `DeliveredConfigurationTests` (the OPC loader over the shipped
   `opc/`), `FailureSeverityTests` and `AppStatusBarViewModelTests`.
3. These print what is stated:
   - `git grep -n -E "RealtimePoll|RealtimeBaseline|PostgresLiveSource|poll_interval_ms|PollInterval" -- SemiPlot ConfigFiles ':!SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs'`
     prints only lines about the SCADA's own 100 ms archiving tick: `RealArchiveFixture.PollInterval`, the
     seeder's `RawLayerGenerator.PollInterval`, their users, and test names and comments that speak of that tick;
     and `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs:60,62`, Task 11's
     `ALeftoverPollIntervalKeyIsIgnored`, whose upgraded file carries the retired `poll_interval_ms` key;
   - `git grep -n -E "ArchiveTimeConverter|TimeZoneInfo|ToLocalTime|ToUniversalTime" -- SemiPlot/SemiPlot.DataSource.OpcUa`
     prints nothing;
   - `git grep -n "ILiveSource" -- SemiPlot/SemiPlot.UI` prints `Bridge/TrendCoordinator.cs` and
     `MainWindow/TrendWindow.cs` lines only;
   - `git grep -n "49875" -- SemiPlot/SemiPlot.Tests.Integration/OpcUa` prints nothing.
4. `dotnet build SemiPlot.slnx` at 0 errors and no warning beyond the AppHost's two `NU1902`;
   `dotnet test SemiPlot.slnx` green on both CI legs.

Manual (the 1 s figure, the archive catching up, a stopped project, the start-up timings): Post-Completion.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with ➕ prefix.
- Document issues or blockers with ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

The slice adds one data-source project and changes three seams: the port gains the session's state and the
per-pen faults, the chart decides the break ahead of a pen's first live sample, and the status bar gains a
second indicator. The poll and its key go.

### Where the break ahead of a first live sample is decided

**[decision] The chart writes it, in `TrendPenState`, and only the chart.** The source cannot: it carries no
chart state, and `Switch` restarts its subscription on every pen-set change, so a break it wrote ahead of each
subscription's first sample would land past every kept pen's last live column and draw a gap after each
catalogue change (`data-integration.md:982-988`). The chart knows each pen's last column and whether the
pen's line is already joined to the live stream. A chart-side break also carries its own meaning for the
`live-hold-to-edge` slice, which shades exactly this break grey and leaves a source's break (a lost session,
a `Bad` status) with the ordinary look.

The rule, per pen:

- A pen's line is **joined** to the live stream once a live value has reached it, appended or not. A new
  `TrendPenState` starts unjoined, so the window's first live sample of every pen, and the first sample of a
  pen a catalogue read adds, are first samples.
- A live value that reaches an unjoined line:
  - at or before the line's last column (the line already covers that moment, as when the archive already
    holds a slow pen's last change): rejected at Raw and folded at a coarse layer, as today; the line joins;
  - past the last column, or on an empty line: the chart appends a `NaN` column 1 ms ahead of the value, then
    the value, and the line joins. No break is written when the line already ends on a gap, or when 1 ms
    ahead of the value is not past the last column. At a coarse layer the value opens a new column past the
    break, as `FoldIntoLastColumn` already does after a gap (`EnvelopeLine.cs:237-241`).
- A break from the source unjoins the line, whether the line takes the `NaN` column or rejects it. A rejected
  break is the case of a pen whose live samples all lay inside the archive's span (a constant pen whose
  archive holds a later row): without the unjoin, the first sample of the next session would draw a straight
  line across the outage.
- A history apply that drops live columns past the read's requested end (`ReplaceColumns`' second removal,
  `EnvelopeLine.cs:201-203`) unjoins the line, because the live run it held is gone and the read does not reach
  past its end. `ReplaceColumns` returns whether it dropped any. A sticky window's read keeps its right margin
  past the live edge (`data-integration.md:424-427`), so following the edge never takes this branch.
- A pen-set change touches no `TrendPenState`, so a kept pen stays joined. Its new subscription's first
  notification carries the stamp of its last change (Measured), which the line already holds, and is dropped
  by DA-7 as today; a change made in the switch's own gap is appended with no break, which is right: the value it
  carries is the newest one and its stamp is its true change moment.

The return value of `ReplaceColumns` and the unjoin it drives are a live-edge rule that a history apply
triggers; what a read loads, which columns it replaces and which live tail it keeps stay as they are, so the
slice's guard "history untouched" holds.

The ahead-break is a live append like any other: `ReplaceColumns` keeps it while it lies past the new history
end and drops it once a read covers it, so the gap closes only when a history read reaches the first live
sample, and nothing closes it on its own (the roadmap's thesis and its settled fork).

### Breaks the source writes

- **[decision] A lost session writes one break per pen the source has delivered, at once.** When the session
  leaves `Connected`, every open subscription emits, for each of its pens with a delivered value, a break 1 ms
  after that pen's newest delivered `SourceTimestamp`. The stamp is the server's clock, so a skew between the
  SCADA's machine and the viewer's cannot push the break past the next session's samples, and 1 ms is the
  axis' smallest step. The newest stamp per pen is kept for the process, across subscriptions, so a session
  lost in the moment after a pen-set change still breaks the kept pens. A pen with no delivered value gets
  none: its line is unjoined or its last column is not live. A pen whose last emission is already a break gets
  no second one.
- Every first notification of a restarted server carries the start instant (roadmap `:54`, and the demo
  server on a slow lattice, Measured), later than any break, so after a project stop, a restart or a kill the
  break never lands at or after the next session's first sample. A server that kept running is the one case
  where it lands after. A session the stack reactivates (an outage shorter than the 60 s session timeout)
  delivers nothing for an unchanged pen. A session it recreates (a 70 s outage, Measured) re-delivers an
  unchanged pen's value with its original `SourceTimestamp`, equal to the newest one delivered before the
  outage, and stamps only the `ServerTimestamp` with the recreation instant; a pen that changed during the
  outage arrives with the stamp of its newest change. The re-delivered unchanged value lies before the break
  and the chart drops it (DA-7). Consequence: an unchanged pen keeps its gap until its next change, which is
  honest about the outage and costs a constant pen its line past the gap until a history read covers it.
  Waiting for the next session to place the break exactly would leave the outage without a break while it
  lasts.
- **[decision] `Bad` writes a break; `Uncertain` and `Good` (with or without info bits, such as the measured
  `0x00000010`) draw the value.** The break's stamp is the notification's `SourceTimestamp`, else its
  `ServerTimestamp`, clamped to at least 1 ms past the pen's newest delivered stamp; a `Bad` with neither stamp
  and no delivered value writes nothing.

### The source

- **[decision] One session per process, started by the first `Subscribe`.** The container builds
  `OpcUaLiveSource` when `TrendWindow.Build` resolves it; its constructor opens nothing. The first
  subscription starts the connect loop, which lives until the container disposes the source.
- **[decision] The connect loop owns discovery; `ManagedSession` owns reconnection.** Each pass:
  `GetEndpointsAsync` on the configured URL under a 10 s bound (a listener that never answers holds it 90 s,
  Measured); pick the endpoint with `SecurityMode None` and policy `#None`; overwrite its `EndpointUrl` with the
  configured `opc.tcp://<host>:<port>` and build the `ConfiguredEndpoint` with `UpdateBeforeConnect = false`,
  so neither the first connect nor a reconnect dials the address the server advertises (with the default
  `true` the session moved to the server's own URL, Measured); keep its `UserIdentityTokens` as advertised
  (below); connect a `ManagedSession` over that endpoint with an anonymous identity and
  `ReconnectPolicyOptions.MaxTotalReconnectTime = Timeout.InfiniteTimeSpan` (at a finite bound the session
  passes through `Failover` to `Disconnected`, Measured); browse `Objects` for the project folder; create the
  open subscriptions. A failure before the first session waits 1 s, doubling to a cap of 5 s, and tries again,
  so a viewer started while the project is stopped sees the server within about 5 s of its start, plus the 2 s
  a refused attempt costs on Windows (Measured). Once
  connected, the session recreates itself and its subscriptions in place after a restart, a kill or a long
  transport outage, and the same `MonitoredItem` handlers fire again (Measured). A session that reaches
  `Closed` without the source closing it is disposed and the loop starts again at discovery.
- **[decision] A close the source issues is not a loss.** The source lets go of a session before every
  `CloseAsync` of its own (a setup-fault retry, a failed browse), and the state handler drops every event
  whose sender is not the session the source holds, so its `Closing` and `Closed` publish no `Unreachable`
  and write no loss breaks; disposal cancels both loops first, and `Publish` is gated on the stopping token.
  The state logic is a pure class, `LiveSessionStates`, fed the stack's transitions, the connect loop's
  outcomes and the grace timer. (As built, Task 7 bracketed the close with a closing flag as well; the
  review removed it, because the stale-session filter already decided every case it covered.)
- **[decision] The token policy is the `None` endpoint's own.** Both servers put an anonymous
  token on `#None` at their `None` endpoint, under different `PolicyId`s (`Anonymous` on Simple-Scada, `1` on
  the stock 2.0.1 server); the real server repeats it on its six secured endpoints, the stock one carries `''`
  there (Measured). The client therefore picks the endpoint with mode `None` and policy `#None` and never builds
  a token policy of its own, so the `PolicyId` it presents is always the one that server issued. Discovery
  stays for that reason even though the URL is pinned. `EndpointChoiceTests` holds both advertisements.
- **Session and reconnect behaviour, measured against the demo server.** A restart in place, a killed server process
  and a cut transport each take the state out of `Connected` within 4-5 s (the 5 s keep-alive; 14.7 s for a
  peer that stalls with its socket open) and back within 0.4-4.7 s of the server answering again, with no
  stamp at or before a pen's newest one. A busy port held by something that is not an OPC UA server keeps the
  loop in discovery, with the 10 s bound, and never reaches a setup fault.
- **[decision] The configuration is validated, and its stores stay empty.** The client builds an
  `ApplicationConfiguration` of type `Client` whose four directory stores sit under
  `<config-dir>/opc/pki/{own,issuer,trusted,rejected}`, runs `ValidateAsync`, and loads no application
  certificate. `ValidateAsync` creates no file or folder (Measured), and a section folder is read top-level
  only, so the store paths never reach the `opc/` loader. An unvalidated configuration makes
  `ManagedSession.ConnectAsync` retry forever (roadmap `:53`).
- **The project ID comes from browsing.** The project folder is the one child of `Objects` whose browse name
  is `root` in the namespace `http://simple-scada.com/UA` and whose string identifier matches
  `<projectId>root_<n>`; a pen's node is `ns=<that namespace's index>;s=<projectId><penId>`.
- **[decision] The project ID is checked on every new session.** On each `Connected` whose session id differs
  from the last one (a first connect or a recreation; a reactivation keeps the id, Measured) the source
  browses `Objects` again. A changed project ID deletes and recreates every open subscription under the new
  ID, because the recreated items still name the old nodes; a missing project folder is `ProjectMissing`.
  After every recreation the source reads each monitored item's status, and an item that is now
  `BadNodeIdUnknown` joins that subscription's unserved pens. A project-ID change is pinned by unit tests only:
  the decision to browse lives in `LiveSessionStates`, the keep, recreate or missing outcome in
  `ProjectFolder.Next(current, browsed)`, and the status read in `UnservedPens`, because the demo server's
  project ID is a constant and no production caller sets another. The restart test on the demo server covers
  the recreation path with the same ID.
- **[decision] Setup faults are reported once and retried at the 30 s cap.** A server with no `None` +
  Anonymous endpoint and a server with no project folder are setup errors the operator fixes in the SCADA or in
  `opc/`. The source reports the fault and keeps trying every 30 s, so a fix in the SCADA, or a project folder
  that appears after the server first answers, reaches the chart without a viewer restart; whether
  Simple-Scada can answer before its project folder exists is not measured. A server without a project folder
  closes the session before the next attempt.
- **[decision] The subscription plan, and the queue size of 1.** Every subscription: publishing
  interval 100 ms, `TimestampsToReturn.Both`; every monitored item: sampling interval 100 ms (the spike's and
  the demo test client's measured value), `QueueSize = 1`. Simple-Scada answers a queue above 1 with an empty
  `Good` first value stamped `MinValue`, so a static variable never gets its value (roadmap `:53`); the demo
  server does not reproduce it (Measured), so `SubscriptionPlanTests` pins the 1 on the items the plan builds,
  and its test name carries the reason.
- **Values.** A scalar of a numeric built-in type (`SByte`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`,
  `Int64`, `UInt64`, `Float`, `Double`) converts to `double`. Anything else, `Boolean`, `String`, `DateTime`,
  an array or a null, is non-numeric: the pen joins its subscription's unserved pens and emits nothing. The
  measured SCADA serves a bool as `Byte` (roadmap `:46`). `SourceTimestamp`, true UTC on the SCADA (roadmap
  `:47`), becomes `Sample.TimestampUtc` with `Kind = Utc` and no zone conversion; the source references no
  zone API. A value with no `SourceTimestamp` takes its `ServerTimestamp`; with neither it is dropped and
  logged at debug.
- **[decision] One entry per subscription for the pens the source cannot serve.** Each subscription collects
  its unserved pens, the ids whose item is `BadNodeIdUnknown` and the ids whose value is non-numeric, and
  reports them as one `LiveSourceError` of kind `PensUnserved` that lists both groups. It reports once every
  created item has delivered its first notification or 5 s after the subscription is created, whichever comes
  first, and again when the set grows later (a value that turns non-numeric, an item unknown after a
  recreation). A set the process has already reported is not reported again, so a pen-set change or a
  recreated session that meets the same set writes nothing. A site with retired variables would otherwise get
  one entry per pen at every start, since the panel merges only an entry equal to its newest one.
- **The port gains two members.** `ILiveSource.States`, an `IObservable<LiveSourceState>` that replays the
  current state to each subscriber (`Connecting` at first), and `ILiveSource.PenFaults`, an
  `IObservable<LiveSourceError>`. Both are hot, never complete before the source is disposed and never fault.
  The `live-hold-to-edge` slice reads `States` for "the session is up".
- **[decision] A 30 s grace before the first fault.** A source that has never connected reports `Connecting`
  and turns to the `Unreachable` fault only when 30 s have passed since its loop started with no session. The
  stand starts the viewer and the OPC UA server side by side, and the demo server takes 1.5-3.5 s to start
  warm and 14-17 s cold (Measured), so without the grace a stand start would write a fault entry and a
  recovery entry. `Connecting` is visible and neutral on the bar, so the longer grace hides nothing; a viewer
  beside a stopped project shows it for those 30 s and then the fault. A
  session that is lost reports the fault as soon as the stack leaves `Connected`, 4-5 s after the loss
  (Measured).
- **[decision] Logging.** The stack logs through the container's `ILoggerFactory` (Serilog). While the server
  is down the client's channel logs one `Error` per failed connect attempt, 3-33 s apart (Measured), which
  would fill the log of a viewer beside a stopped project with errors that say nothing the source's own
  state line does not. `AddOpcUaLiveSource` adds a logging filter that raises
  `Opc.Ua.Bindings.UaSCUaBinaryClientChannel` to `Critical`; nothing else of the stack is filtered. The
  host-name `Error` seen in the demo runs comes from the demo server's stack, not the client (Measured), so the
  viewer filters nothing for it. The source's own lines: one `Information` per state change, carrying the
  stack's status code when the event or the keep-alive carries one (the event that leaves `Connected`
  carries none, Measured), one `Warning` per pen fault, one `Debug` per break it writes. The one `Error`
  the stack writes after a reactivated session ("RepublishAsync is only supported ...", Measured) stays
  unfiltered: it is rare and names a stack defect the log should keep.

### Startup and configuration

- **[decision] `opc/` is a third section folder, loaded after `connection/`.** `StartupProbe.Run` loads
  `connection/`, then `opc/`, then builds the container with `AddPostgresData(settings)`,
  `AddOpcUaLiveSource(opcSettings)` and `AddUi()`. A folder that is missing, unreadable, unparseable or carries
  a bad value fails the start with the section's own entry; nothing connects to the OPC UA server at start.
- Keys: `host` (an IPv4 address under the `connection/` rule) and `port` (1-65535), both required. Tracked as
  `host: 127.0.0.1`, `port: 49875`.
- **[decision] The IPv4 rule and the port bounds move to Core.** `NetworkAddress.IsIPv4`, `LowestPort` and
  `HighestPort` in `SemiPlot.Core/Configuration` serve both loaders, the settings window and the mapper, so
  the rule lives in one place and the OPC project needs no reference to the Postgres one.
- **[decision] `ConnectionFileError` names its section.** It gains a `ConfigurationSectionName Section`, and the
  mapper reads the title from the section; the four detail texts drop "the connection folder" for "the
  folder", which the path already names. No second file error type. The port bounds the mapper's remedy reads
  move to `NetworkAddress` rather than onto the error, as `ui-text.md:207-208` sketches, because they are the
  same for both sections.
  With `poll_interval_ms` gone the `OutOfRange` remedy names the port range only.

### Reporting

- **[decision] The OPC error type lives in Core:** `LiveSourceError(LiveSourceFault Kind, string Endpoint)`
  in `SemiPlot.Core/Data/Errors`, so `FailureSeverityTests` covers it (the review deleted a `Detail` that no
  production code wrote; the stack's status goes on the source's own state line). Kinds and
  severities: `Unreachable` (`Warning`: the chart keeps the archive and the source reconnects),
  `EndpointRefused` (`Error`: no `None` + Anonymous endpoint), `ProjectMissing` (`Error`: no project folder),
  `PensUnserved` (`Warning`: the detail lists the unknown pen ids and the non-numeric pen ids, each group
  named, from the error's `UnknownPenIds` and `NotNumericPenIds`).
- **[decision] The second indicator mirrors the first.** `AppStatusBarViewModel` follows
  `TrendCoordinator.LiveSourceStates` beside the archive's states: `IsLiveConnected`, `IsLiveFaulted`, a text
  per state (connecting, connected, not answering), one panel entry per fault through `ArchiveFailureMapper`, and one
  `Info` entry on the first `Connected` after a fault. `Connecting` carries neither palette class, so the
  indicator draws in the bar's ordinary text colour during the grace instead of the fault colour. The bar
  stays the single writer of both indicators.
- Pen faults reach the panel from `TrendCoordinator.LivePenFaults` through
  `ResultReporting.TryReportFailure(MessagePanelViewModel, IError, ILogger)` (`UI/Messages/ResultReporting.cs:67`),
  which maps them with `ArchiveFailureMapper` and so takes the `Warning` of `MapLiveSource`; the
  `ReportFailure(Exception)` path would wrap them as an `ExceptionalError` and show them as `Error`. The coordinator republishes both streams on the UI scheduler and stays the
  one holder of `ILiveSource` in the UI besides `TrendWindow`.
- **The latency line.** `TrendChartViewModel.ApplyRealtimeBatch` logs at `Debug`, per batch, the batch's
  value count and `LagMilliseconds`, the apply moment minus the newest value's `TimestampUtc`. Breaks are left
  out of the newest stamp. The 1 s acceptance reads this line on the demo SCADA.

### The demo stand

- **[decision] `converge` writes `opc/opc.yaml`.** It gains a required `--opc-port`, and after the connection
  file it writes `opc/opc.yaml` with `host: 127.0.0.1` and that port through `OpcFileWriter`, overwriting the
  tracked file by name as it overwrites `connection/connection.yaml`. `converge` already owns the stand's
  configuration copy, the AppHost starts the viewer only after `converge` completes, and a hand run
  (`AGENTS.md`'s `converge` example) gets the file the same way. The follow run cannot write it: the viewer
  does not wait for the writer. The AppHost passes its one `OpcPort` constant to both `converge` and the
  writer. The installer needs no edit: its recursive `onlyifdoesntexist` copy installs `opc/opc.yaml` on a new
  site and on an upgrade.
- **[decision] The stand's writer runs `--archive-lag 60`.** The archive then trails the OPC UA stream by a
  minute, inside the 1-3 min a fast pen lags on the real SCADA (roadmap `:33`), so the stand shows the span the
  archive has not reached at the window's open, and a gesture a minute later shows a history read covering
  it. `converge` drops the database at every stand start, so the lagged restart guard never meets an old
  archive.

### Tests

- **[decision] The OPC tests live in `SemiPlot.Tests.Integration/OpcUa/`, in `DemoScadaCollection`.** They
  start the demo server through `DemoScadaHost` on a free port, never 4840 or 49875, need no container and run
  on the Linux leg. A server restart is reproduced by disposing the demo server and starting a new one on the
  same port (`DemoScadaHost.StartOnPortAsync`); a lost transport with the session alive by a loopback TCP relay
  the test cuts. A killed server process is not reproduced in a test: the probe measured it against a demo
  server in a child process (Measured), and Post-Completion repeats it on the real SCADA.
- The pure pieces (endpoint choice, project folder, value mapping, loss breaks, subscription plan, loader) have
  unit tests that open no socket.

## Technical Details

### Core

```csharp
// SemiPlot.Core/Data/ILiveSource.cs
/// <summary>The live edge of a pen set and its session (docs/architecture/data-integration.md#realtime).</summary>
public interface ILiveSource
{
	IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

	IObservable<LiveSourceState> States { get; }

	IObservable<LiveSourceError> PenFaults { get; }
}

// SemiPlot.Core/Data/LiveSourceState.cs
public sealed record LiveSourceState    // private constructor: only the three states below exist
{
	public static readonly LiveSourceState Connecting;    // not connected, no fault

	public static readonly LiveSourceState Connected;

	public bool IsConnected { get; }

	public LiveSourceError? Fault { get; }

	public static LiveSourceState FaultedBy(LiveSourceError fault);

	public override string ToString();    // Connected, Connecting or the fault's kind, for the log line
}

// SemiPlot.Core/Data/Errors/LiveSourceError.cs
public enum LiveSourceFault { Unreachable, EndpointRefused, ProjectMissing, PensUnserved }

public sealed class LiveSourceError(
	LiveSourceFault kind,
	string endpoint,
	IReadOnlyList<int>? unknownPenIds = null,
	IReadOnlyList<int>? notNumericPenIds = null)
	: Error(Describe(kind, endpoint, unknownPenIds, notNumericPenIds))
{
	public LiveSourceFault Kind { get; } = kind;

	public string Endpoint { get; } = endpoint;

	public IReadOnlyList<int> UnknownPenIds { get; } = unknownPenIds ?? [];

	public IReadOnlyList<int> NotNumericPenIds { get; } = notNumericPenIds ?? [];
}

// SemiPlot.Core/Configuration/NetworkAddress.cs
public static class NetworkAddress
{
	public const int LowestPort = 1;

	public const int HighestPort = 65535;

	public static bool IsIPv4(string? text);
}
```

`ConfigurationSectionName` gains `Opc`. `ConnectionFileError(ConfigurationSectionName section, string path,
ConnectionFileProblem kind, IReadOnlyList<string> fields)` gains `Section`.

### The project

`SemiPlot/SemiPlot.DataSource.OpcUa/SemiPlot.DataSource.OpcUa.csproj`, the Postgres project's shape
(`SemiPlot.DataSource.Postgres.csproj:1-21`): `FluentResults`, `Microsoft.Extensions.DependencyInjection.Abstractions`,
`Microsoft.Extensions.Logging.Abstractions`, `OPCFoundation.NetStandard.Opc.Ua.Client` (2.0.1, pinned at
`Directory.Packages.props:28`), `System.Reactive`, `YamlDotNet`; a reference to Core; `InternalsVisibleTo` both
test projects. `SemiPlot.UI`, `SemiPlot.Tests.Unit` and `SemiPlot.Tests.Integration` reference it.

| File | Holds |
| --- | --- |
| `Configuration/OpcUaConnectionSettings.cs` | `record OpcUaConnectionSettings(string Host, int Port, string CertificateStoreRoot)` with `EndpointUrl => $"opc.tcp://{Host}:{Port}"` |
| `Configuration/OpcUaConnectionLoader.cs` | `HostKey`, `PortKey`, `Load(string sectionDirectory)`: `ConfigurationSection.Read(dir, Opc)`, a nullable DTO under the underscored convention with unmatched keys ignored, missing fields, port range, IPv4 host, each a `ConnectionFileError` of section `Opc`, all through `SemiPlot.Core/Configuration/ConnectionFile`, which `PostgresConnectionLoader` shares; the store root is `Path.Combine(sectionDirectory, "pki")` |
| `OpcUaDataServiceCollectionExtensions.cs` | `AddOpcUaLiveSource(this IServiceCollection, OpcUaConnectionSettings)` |
| `OpcUaLiveSource.cs` | `public sealed class OpcUaLiveSource : ILiveSource, IAsyncDisposable, IDisposable`; `internal` constructor over the settings, `ILoggerFactory` and the grace (30 s in production); `Subscribe`, `States`, `PenFaults`, the registrations |
| `OpcUaSession.cs` | the connect loop, the `ManagedSession`, the browse on each new session id, the close |
| `LiveSessionStates.cs` | the pure state logic: the stack's transitions, the connect loop's setup faults and unanswered passes, and the grace in; the `LiveSourceState` to publish and the loss trigger out |
| `UnservedPens.cs` | one subscription's unknown and non-numeric pen ids and the settle rule |
| `ReportedUnservedSets.cs` | the process-wide set of reported unserved sets |
| `PenSubscription.cs` | one OPC UA subscription per `Subscribe` call and its notification handler |
| `PenSubscriptionContext.cs` | what every pen subscription of one source shares |
| `EndpointChoice.cs` | `static EndpointDescription? Pick(IEnumerable<EndpointDescription>, string configuredUrl)` |
| `ProjectFolder.cs` | `static string? ProjectIdOf(IEnumerable<ReferenceDescription> objectsChildren, NamespaceTable)`, `static NodeId NodeOf(string projectId, ushort namespaceIndex, int penId)`, and `Next(string? current, string? browsed)`: keep, recreate or missing |
| `LiveValue.cs` | `static LiveReading Read(DataValue)` over a private `ToDouble(Variant)`: the mapping of one `DataValue` to a value sample, a break, a non-numeric outcome or nothing |
| `DeliveredStamps.cs` | the newest delivered stamp per pen for the process, and the breaks of a lost session |
| `SubscriptionPlan.cs` | the constants and the factories of the `Subscription` and its `MonitoredItem`s |
| `ClientApplicationConfiguration.cs` | the validated `ApplicationConfiguration`: `ApplicationType.Client`, application name `SemiPlot` and URI `urn:localhost:SemiPlot`, the four directory stores under the store root (`own` with subject `CN=SemiPlot`), `TransportQuotas.OperationTimeout = 15000`, `ClientConfiguration.DefaultSessionTimeout = 60000`: every field the probe's `ProbeClient.CreateConfigurationAsync` set |

Each internal type that does not touch a session is unit-testable without a socket. `OpcUaLiveSource` and
`OpcUaSession` stay under 300 lines each.

### The connect loop

```text
start (first Subscribe):
  configuration = await ClientApplicationConfiguration.CreateValidatedAsync(settings)  // no file written
  loop until disposed:
    endpoints = await DiscoveryClient.GetEndpointsAsync(settings.EndpointUrl)  // 10 s bound; failure -> retry
    endpoint  = EndpointChoice.Pick(endpoints, settings.EndpointUrl)           // none -> EndpointRefused, retry at 30 s
    configured = new ConfiguredEndpoint(null, endpoint, EndpointConfiguration.Create(configuration))
                   { UpdateBeforeConnect = false }
    session   = await new ManagedSessionBuilder(configuration, telemetry)
                  .UseEndpoint(configured)
                  .WithSessionName("SemiPlot")
                  .WithSessionTimeout(TimeSpan.FromSeconds(60))
                  .WithUserIdentity(new UserIdentity())
                  .WithReconnectPolicy(policy => policy with { MaxTotalReconnectTime = Timeout.InfiniteTimeSpan })
                  .ConnectAsync(token)                                         // retries inside until a server answers
    projectId = ProjectFolder.ProjectIdOf(browse Objects)                      // none -> ProjectMissing, close, retry at 30 s
    create every open PenSubscription; state -> Connected
    await Closed (not closed by the source) or disposal; dispose the session; retry
  retry before the first session: wait 1 s, doubling to 5 s; a setup fault waits 30 s
  grace: 30 s after the loop starts with no session yet -> FaultedBy(Unreachable), once
session.ConnectionStateChanged (ConnectionStateChangedEventArgs PreviousState, NewState, Error), through LiveSessionStates:
  leaves Connected, not self-closing -> queue "lost": every PenSubscription emits its loss breaks; state -> FaultedBy(Unreachable)
  any event of a session the source has let go of -> nothing
  enters Connected   -> queue "back": state -> Connected; a new SessionId -> browse again: a new project ID
                        recreates every open subscription; then read every item's status into its unserved pens
session.KeepAlive: a failed keep-alive's status is kept for the next "lost" log line
```

The API names are the ones the probe compiled and ran against 2.0.1: `DiscoveryClient.CreateAsync(configuration,
new Uri(url), DiagnosticsMasks.None, token)` and `GetEndpointsAsync(default, token)`; `ManagedSession.SessionId` (a
`NodeId` struct), `Connected`, `Reconnecting`, `Disposed`; `session.AddSubscription`, `Subscription.CreateAsync`,
`session.RemoveSubscriptionAsync(subscription, token)`; `CloseAsync(token)` then `DisposeAsync()`;
`DataValue.SourceTimestamp.ToDateTime()` (a `DateTime` of `Kind = Utc`); `StatusCode.IsGood`, `IsUncertain`,
`IsBad`; `Variant.TypeInfo.BuiltInType`. A file that sees both the client and the server package (the
integration project) aliases `Opc.Ua.Client.MonitoredItem` and `Opc.Ua.Client.Subscription`.

The stack raises `ConnectionStateChanged` on its own threads; the handler only queues the change onto the
source's loop, which serializes every state change, every subscription creation and every emission per
observer. `States` is a `BehaviorSubject` seeded `Connecting` behind `Synchronize`; `PenFaults` a `Subject`
behind `Synchronize`, both exposed through `AsObservable()`.

Close: `DisposeAsync` cancels both loops, so nothing publishes, then closes the session with a 2 s token (the
demo test client's `_closeTimeout`, `OpcTestClient.cs:22`), then completes both subjects. The token bounds the
close, not the 15 s `OperationTimeout`: the measured close took 11-134 ms, and a `CloseSession` against a peer
that never answers would otherwise wait the operation timeout. `Dispose` runs the whole close on the pool,
`Task.Run(DisposeAsyncCore).Wait(TimeSpan.FromSeconds(3))`, so no continuation needs `Main`'s thread and
none can wait on a `SynchronizationContext` that `Main` still carries: the container is disposed
synchronously when `Main` returns (`Program.cs:48-49`), the compat-forced entry point the sync-over-async
rule allows. The 3 s wait covers the 2 s token with room; an abandoned close finishes on the pool.

### A subscription

- `Subscribe(penIds)` is `Observable.Create`: it registers a `PenSubscription` with the source (starting the
  loop on the first call) and returns a disposable that unregisters it. Registered while `Connected`, its OPC
  UA subscription is created at once; otherwise at the next `Connected`. Disposal deletes the OPC UA
  subscription when the session is connected and removes it from the session otherwise; a failure of that
  delete is logged at `Warning` from its continuation.
- `SubscriptionPlan`: `PublishingInterval = 100`, `KeepAliveCount = 10`, `LifetimeCount = 600` (the spike's;
  the probe's default of 100 logged "A smaller minLifetimeInterval 10000ms than session timeout 60000ms"),
  `TimestampsToReturn.Both`; each item `SamplingInterval = 100`, `QueueSize = 1`, `StartNodeId =
  ProjectFolder.NodeOf(projectId, index, penId)`, its `Handle` the pen id.
- After `CreateAsync`, and after every recreation, each item with a `BadNodeIdUnknown` status (the real
  server's unknown node, with `Created == false`) joins the subscription's `UnservedPens` as unknown; any
  other creation error is logged at `Warning`.
- A notification goes through `LiveValue`: a value sample emits `Sample(penId, stamp, value)` and records the
  stamp in `DeliveredStamps`; a `Bad` emits the break of Solution Overview; a non-numeric value joins
  `UnservedPens` as non-numeric and emits nothing. `UnservedPens` raises one `PensUnserved` error under the
  settle rule of Solution Overview. Each notification emits one list; the coordinator
  buffers them.
- On "lost", `DeliveredStamps.LossBreaks(penIds)` yields one `Sample(penId, newest + 1 ms, null)` per pen with a
  delivered value whose last emission is not already a break; the subscription emits them as one list.

### The chart

```csharp
// EnvelopeLine
internal bool ReplaceColumns(IReadOnlyList<EnvelopeColumn> columns, double requestedEndX); // true: dropped a live column past requestedEndX
internal bool AppendAfterBreak(EnvelopeColumn gap, EnvelopeColumn sample);

// TrendPenState
private bool _isJoinedToLive;    // the break step is Core's Sample.BreakStep, 1 ms, which DeliveredStamps reads too
```

`TrendPenState.AppendRealtime(t, v)` with `v` set and the line unjoined calls
`Line.AppendAfterBreak(LiveColumn(t - Sample.BreakStep, NaN), LiveColumn(t, v))`; `FoldRealtime` does the same
when unjoined and falls back to `FoldIntoLastColumn` when `AppendAfterBreak` returns false. Both set
`_isJoinedToLive = true` and set `CurrentValue` only when the value was taken.

### Startup

```csharp
// StartupProbe
public const string OpcDirectoryName = "opc";

var settings = PostgresConnectionLoader.Load(<config-dir>/connection);   // fail -> start fails
var opc = OpcUaConnectionLoader.Load(<config-dir>/opc);                  // fail -> start fails
var serviceProvider = BuildArchiveServiceProvider(settings.Value, opc.Value);
// AddPostgresData(settings).AddOpcUaLiveSource(opc).AddUi()
```

`ConfigFiles/opc/opc.yaml`:

```yaml
host: 127.0.0.1
port: 49875
```

### UI

- `TrendCoordinator.LiveSourceStates => _liveSource.States.ObserveOn(_uiScheduler)` and
  `LivePenFaults => _liveSource.PenFaults.ObserveOn(_uiScheduler)`, built once in the constructor.
- `AppStatusBarViewModel(MessagePanelViewModel, IObservable<ArchiveConnectionState>, IObservable<LiveSourceState>,
  ChartNavigationController, ILogger<AppStatusBarViewModel>)`.
- The latency line: `_logger.LogDebug("Live batch applied: {ValueCount} values, {LagMilliseconds} ms after the
  newest", count, (DateTime.UtcNow - newestValueUtc).TotalMilliseconds)`, skipped for a batch of breaks only.
- New resx keys (both languages): `StatusLiveConnecting`, `StatusLiveConnectionOk`, `StatusLiveConnectionFault`,
  `StatusLiveConnectionRestoredTitle`, `StatusLiveConnectionRestoredDetail`, a title, detail and remedy per
  `LiveSourceFault`, `FailureOpcFileRejectedTitle`, `FailureConfigurationSectionOpcTitle`,
  `FailureConfigurationSectionOpcDirectoryMissingRemedy`, `FailureConfigurationSectionOpcNoFilesRemedy`.
  Removed: `SettingsPollIntervalLabel`, `SettingsPollIntervalInvalid`.

### The stand

```csharp
// AppHost.cs
const string OpcPort = "55875";
// The archive trails the OPC UA stream as the SCADA's buffered archive does, so the stand shows that span.
const string ArchiveLagSeconds = "60";
// converge: ..., "--opc-port", OpcPort
// writer:   ..., "--opc-port", OpcPort, "--archive-lag", ArchiveLagSeconds
```

`converge` writes `opc/opc.yaml` as `host: 127.0.0.1` and `port: <--opc-port>` through
`OpcFileWriter.WriteAsync(configDirectory, "127.0.0.1", port)`; `DirectoryName = "opc"`,
`FileName = "opc.yaml"`.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests and documentation in this repository.
- **Post-Completion** (no checkboxes): the stand check, the manual acceptance on the real SCADA and the
  remaining roadmap slices.

## Implementation Steps

### Task 1: Move the IPv4 rule and the port bounds into Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Configuration/NetworkAddress.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Configuration/NetworkAddressTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`

- [x] create `NetworkAddress` with `IsIPv4(string?)`, `LowestPort = 1`, `HighestPort = 65535`, moving the body of
      `PostgresConnectionLoader.IsIPv4Address` (`:113-131`) unchanged
- [x] `PostgresConnectionLoader` drops `IsIPv4Address`, `LowestPort`, `HighestPort` (`:53-55`) and calls
      `NetworkAddress`; `ArchiveFailureMapper.cs:129-130` and `SettingsViewModel.cs:60,115,117,215` read
      `NetworkAddress`
- [x] move the two IPv4 theories (`PostgresConnectionLoaderTests.cs:160-190`) into `NetworkAddressTests`
      unchanged; the loader keeps its `HostNotIPv4` case
- [x] run the unit project - must pass before Task 2

### Task 2: Name the section on `ConnectionFileError` and add the `opc/` section

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSectionError.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileError.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ConfigurationSectionFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsSave.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`,
  `FailureSeverityTests.cs`, `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsSaveTests.cs`

- [x] add `Opc` to `ConfigurationSectionName`; run `FailureSeverityTests` and confirm
      `EveryEnumMapperArm_AnswersForEveryMemberOfItsEnum` fails on `ConfigurationSectionName.Opc` (the guard
      fires), then add the mapper's `Opc` title arm and turn `SectionRemedy`'s `app` flag into a switch over the
      three sections, with `FailureConfigurationSectionOpcTitle`, `...OpcDirectoryMissingRemedy` and
      `...OpcNoFilesRemedy` (they name the `host` and `port` keys)
- [x] `ConnectionFileError(ConfigurationSectionName section, string path, ConnectionFileProblem kind,
      IReadOnlyList<string> fields)`; its message names the section; the Postgres loader passes `Connection`,
      `SettingsSave.cs:163` passes the section it rebuilds for
- [x] `ArchiveFailureMapper.MapConnectionFile` takes the title from the section
      (`FailureConnectionFileRejectedTitle` for `Connection`, new `FailureOpcFileRejectedTitle` for `Opc`); the
      four `FailureConnectionFile*Detail` texts say "the folder '{0}'" instead of "the connection folder '{0}'",
      in both languages
- [x] tests: the mapper's `ConnectionFileError` cases run for both sections and assert the title per section;
      the section-failure cases gain `Opc` rows for `DirectoryMissing` and `NoFiles`
- [x] run the unit project - must pass before Task 3

### Task 3: The live source's state and errors in Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/LiveSourceState.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/LiveSourceError.cs` (the class and its `LiveSourceFault` enum)
- Modify: `SemiPlot/SemiPlot.Core/Data/ILiveSource.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresLiveSource.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`, `FailureSeverityTests.cs`

- [x] create `LiveSourceError` and `LiveSourceFault` as in Technical Details; run `FailureSeverityTests` and
      confirm `MapArmTable_CoversEveryErrorTypeThisRepositoryDeclares` fails naming `LiveSourceError` (the
      guard fires at HEAD), then add the `MapLiveSource` arm with one title, detail and remedy per kind and the
      severities of Solution Overview
- [x] create `LiveSourceState`; add `States` and `PenFaults` to `ILiveSource` with the contract in a one-line
      summary and a pointer to `data-integration.md#realtime`
- [x] `PostgresLiveSource` answers `States` with `Connected` replayed and `PenFaults` with an empty stream
      that never completes (it is deleted in Task 11); `FakeDataProvider` holds a `BehaviorSubject` seeded
      `Connected` with `PushLiveState(LiveSourceState)` and a `Subject` with `RaisePenFault(LiveSourceError)`
- [x] tests: `LiveSourceError` follows `ArchiveError`'s pattern in `FailureSeverityTests`: the declared-types
      check exempts it by name as covered by its own fault table (`:132-133`), and a `LiveSourceFault` table with
      a coverage test and a severity test sits beside the `ArchiveFault` one (`:117,137`); the `PensUnserved`
      detail names both id groups and leaves out an empty one; `ArchiveFailureMapperTests` per kind, asserting the endpoint and the pen id in the detail
- [x] run the unit project - must pass before Task 4

### Task 4: The chart writes the break ahead of a pen's first live sample

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/EnvelopeLineTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartRealtimeApplierTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- Modify: every other unit test whose column counts change (see the last checkbox)
- Delete: `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] `EnvelopeLine.ReplaceColumns` returns whether it dropped a column past the requested end
- [x] `EnvelopeLine.AppendAfterBreak(EnvelopeColumn gap, EnvelopeColumn sample)`, under the lock: false when the
      sample is at or before the last X; otherwise appends the gap when the line is empty or does not end on a
      gap and the gap's X is past the last X, then the sample, and returns true
- [x] `TrendPenState`: an `_isJoinedToLive` flag, false at construction; `AppendRealtime` and `FoldRealtime` take
      the unjoined branch of Solution Overview; a null unjoins; `ReplaceHistory` unjoins when `ReplaceColumns`
      returns true; `CurrentValue` stays the last real value
- [x] tests in `TrendPenStateTests`: a first value past the last history column appends a `NaN` 1 ms ahead and
      then the value; a first value at or before the last column is rejected and the next value past it
      appends with no break; an empty line takes the `NaN` and the value, and a history load that lands after
      keeps both past its end; a line that ends on a gap takes no second `NaN`; a value exactly 1 ms past the
      last column takes no break; a rejected break still makes the next value past the last column open with
      a break; a history apply that drops live columns past its requested end makes the next value open with a
      break, and one that drops none does not; the coarse-layer fold opens a column past the ahead-break
- [x] tests in `TrendChartViewModelTests`: a catalogue read that adds a pen moves the live edge (one more
      subscription) and the kept pen's next live sample appends with no `NaN` column; the added pen's first
      sample opens with one
- [x] run the unit project and update the existing tests whose pen now takes the ahead-break before its first
      live column. The candidates are the direct callers of `AppendRealtime`/`FoldRealtime` and the users of
      `FakeDataProvider`'s live ticks, which start past `ArchiveLastUtc`: `TrendPenStateTests` (the `Realtime_*`
      cases at `:56-160`), `ChartRealtimeApplierTests` (`:20-58`, each starts on a fresh state),
      `TrendChartViewModelTests`, `TrendChartCatalogueTests` and `Minimap/MinimapBandFeedTests`. A count or an
      index of live columns shifts by the one `NaN`; an assertion that the first live column follows the last
      history column directly inverts to one `NaN` column between them; an assertion on `CurrentValue` or on
      the live edge does not change. List the touched tests here: `TrendPenStateTests` (`Realtime_BreakOneTickAfterTheValue_...`,
      `Realtime_LiveEdgeColumnDegenerates...`, `Realtime_SampleAfterTheLastPoint_...` row 2.0, both
      `Realtime_After...EndingBeforeALiveColumn_...`, and `FoldRealtime_WidensTheMinMaxOfTheCurrentColumn`, now
      `FoldRealtime_AFirstValueOpensAColumnBehindTheBreakAndTheNextOneWidensIt`); `ChartRealtimeApplierTests`
      (all three); `TrendChartViewModelTests` (`Coordinator_CoarseLayerRealtime_FoldsInsteadOfGrowingColumns`,
      `ABreakAtACoarseLayerAppendsANaNColumnAndTheNextFoldOpensAColumnPastIt`,
      `ABreakAtRawAppendsANaNColumnOneMillisecondPastItsValue`,
      `Coordinator_RealtimeTimestampsOnlyOnePenSampled_BreakNeitherPen`); `EnvelopeLineTests` gained the
      `ReplaceColumns` return and three `AppendAfterBreak` cases. `TrendChartCatalogueTests` and
      `MinimapBandFeedTests` needed no change
- [x] delete `LiveEdgeArchiveJourneyTests`: it drives the poll, which Task 11 deletes, and with the ahead-break
      its `RowsOnAVariableOfTheirOwnReachTheChartWithoutBreakingAnyPen` (`:131-158`, no `NaN` column in any pen)
      turns red, because the poll's first row lands past the pen's last history column; Task 10's journey
      replaces it
- [x] run the unit project and the integration filter `FullyQualifiedName~Journeys` - must pass before Task 5

### Task 5: The `SemiPlot.DataSource.OpcUa` project and the `opc/` section

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/SemiPlot.DataSource.OpcUa.csproj`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/Configuration/OpcUaConnectionSettings.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/Configuration/OpcUaConnectionLoader.cs`
- Create: `ConfigFiles/opc/opc.yaml`
- Modify: `SemiPlot.slnx`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`, `SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`,
  `SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/OpcUa/OpcUaConnectionLoaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/DeliveredConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs` (`:90` calls
  `BuildArchiveServiceProvider` with one argument)

- [x] create the project as in Technical Details and add it to `SemiPlot.slnx`, the UI and both test projects
- [x] `OpcUaConnectionLoader.Load(sectionDirectory)` as in Technical Details
- [x] add `DeliveredConfigurationTests.TheShippedOpcSectionLoads` and run it before `ConfigFiles/opc/opc.yaml`
      exists: it fails with `ConfigurationSectionError` `DirectoryMissing` for `Opc` (the guard fires); then add
      the file (`host: 127.0.0.1`, `port: 49875`) and it passes, asserting host, port and the store root
      `<root>/opc/pki`
- [x] `StartupProbe`: `OpcDirectoryName = "opc"`; `Run` loads `opc/` after `connection/` and fails the start on
      its errors; `BuildArchiveServiceProvider` takes both settings (the registration of the source itself lands
      in Task 7; until then `BuildArchiveServiceProvider` registers the settings as a singleton, because an
      unused parameter fails the build on IDE0060)
- [x] tests `OpcUaConnectionLoaderTests`: the tracked shape loads; `host` or `port` absent or blank
      (`MissingField`, both named when both are absent); port 0 and 65536 (`OutOfRange`); host `localhost`,
      `WORKING-PC`, `127.0.0.01` (`HostNotIPv4`); a non-numeric port (`Unparseable`); an unknown key is
      ignored; every error carries `ConfigurationSectionName.Opc`; a `pki` folder with a `.yaml` inside under
      the section is not read
- [x] tests `StartupProbeTests`: an `opc/` folder that is missing, or carries `port: 0`, fails the start with
      that section's error and builds no container; with a valid `opc/` pointing at a closed port the start's
      result carries no error of the `Opc` section (it fails on the unreachable archive instead; the probe
      opens no OPC socket). The two failing cases live in `StartupProbeTests`; the valid case connects to the
      archive's closed port, so it moved to `SemiPlot.Tests.Integration/StartupProbeOpcSectionTests` beside the
      other socket tests that need no server (AGENTS.md: the unit project opens no socket), and it also holds a
      loopback listener on the `opc/` port and asserts no connection reached it
- [x] run the unit project - must pass before Task 6

### Task 6: The pure pieces of the OPC client

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/EndpointChoice.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/ProjectFolder.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/LiveValue.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/DeliveredStamps.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/SubscriptionPlan.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/ClientApplicationConfiguration.cs` (named `ClientConfiguration.cs`
  until the review renamed it off the stack's `Opc.Ua.ClientConfiguration`)
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/LiveSessionStates.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/UnservedPens.cs`
- ➕ Create: `SemiPlot/SemiPlot.DataSource.OpcUa/StackTelemetry.cs` (the `TelemetryContextBase` over the
  container's `ILoggerFactory` that `ClientApplicationConfiguration` and `SubscriptionPlan` take; Task 7 reuses it)
- Create: `SemiPlot/SemiPlot.Tests.Unit/OpcUa/EndpointChoiceTests.cs`, `ProjectFolderTests.cs`,
  `LiveValueTests.cs`, `DeliveredStampsTests.cs`, `SubscriptionPlanTests.cs`, `ClientApplicationConfigurationTests.cs`,
  `LiveSessionStatesTests.cs`, `UnservedPensTests.cs`

- [x] implement the eight types as in Technical Details
- [x] `EndpointChoiceTests`: the real SCADA's seven endpoints (all advertising `opc.tcp://WORKING-PC:49875`,
      one token `PolicyId = Anonymous` on `#None` each) yield the `None` endpoint with the configured URL and the
      advertised token kept; the stock 2.0.1 server's three (`#None` token, `PolicyId '1'`, on `None`; `''`
      on the other two) yield the `None` one; no `None` endpoint, and a `None` endpoint without an anonymous
      token, yield nothing
- [x] `ProjectFolderTests`: the real `Objects` order (`Server`, `FD34514A35<system>_1`, `FD34514A35root_2`,
      `FD34514A35group1_3`) yields `FD34514A35`; `Next(current, browsed)` keeps an equal ID, recreates on a
      different one and reports `ProjectMissing` on none; the demo's order (`Server`, `Aliases`, `Locations`,
      `5E4D0C1A0Broot_2`, group folders) yields `5E4D0C1A0B`; `root` in another namespace, a `root` whose
      identifier does not end `root_<n>`, two `root` folders and none yield nothing; the node of pen 1005 is
      `ns=3;s=FD34514A351005`
- [x] `LiveValueTests`: each numeric built-in type reads as its `double`; `Boolean`, `String`, `DateTime`, an
      array and a null are non-numeric; `Good`, `Good` with info bits and `Uncertain` yield a value sample;
      `Bad` yields a break; the stamp is the `SourceTimestamp` with `Kind = Utc` and identical ticks, else the
      `ServerTimestamp`, else no sample
- [x] `DeliveredStampsTests`: a session loss yields one break per pen with a delivered value at newest + 1 ms,
      none for a pen without one, none twice in a row; a `Bad` break is clamped 1 ms past the newest; the
      newest stamp survives a new subscription of the same pen
- [x] `SubscriptionPlanTests`: `AMonitoredItemsQueueIsOne_BecauseSimpleScadaSendsAnEmptyFirstValueAboveIt`
      asserts `QueueSize == 1` on the items the plan builds; publishing interval 100 ms, sampling interval
      100 ms, `TimestampsToReturn.Both`
- [x] `ClientApplicationConfigurationTests`: the configuration validates; `OperationTimeout` is 15000, `DefaultSessionTimeout`
      is 60000, the application name, URI and type and the four store paths under the settings' store root
      are the ones in Technical Details; after validation no file or folder exists under the root
- [x] `LiveSessionStatesTests`: `Connecting` at start; `Connected` on the first connect; leaving `Connected`
      yields the fault and one loss trigger; `Closing` and `Closed` after the closing flag is set yield neither;
      `Connected` after a fault yields `Connected`; the grace yields the fault once and only when no session
      came first; a reconnect with a new session id asks for a browse and one with the same id does not
      (the review deleted the closing flag and its two cases, and the public `Current` the `Connecting` case
      read; it added the grace after a setup fault, a transition before any connect, a change of setup-fault
      kind and the unanswered pass)
- [x] `UnservedPensTests`: unknown and non-numeric ids gather into one error per subscription once every item
      has answered or the 5 s settle has passed; a later non-numeric pen reports the grown set; item statuses
      read after a recreation add their `BadNodeIdUnknown` pens and report the grown set; a set the process
      already reported is not reported again; an empty set reports nothing
- [x] run the unit project - must pass before Task 7

### Task 7: The session: connect, browse, report state

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/OpcUaLiveSource.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/OpcUaSession.cs`
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/OpcUaDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/OpcUa/OpcUaCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/OpcUaSessionTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/LiveSourceHost.cs`
- ➕ Create: `SemiPlot/SemiPlot.DataSource.OpcUa/ConnectBackoff.cs` (the pure retry waits, 1 s doubling to 5 s and 30 s
  after a setup fault), `SerialLoop.cs` (the one reader every state input runs on) and `ServerCalls.cs` (discovery,
  connect, browse, close), so `OpcUaSession` stays under 300 lines
- ➕ Create: `SemiPlot/SemiPlot.Tests.Unit/OpcUa/ConnectBackoffTests.cs` (the setup-fault path's unit test)

- [x] `OpcUaSession`: the connect loop with its 5 s pre-session cap, the state stream through `LiveSessionStates`
      with its 30 s grace and the closing flag, the setup faults and their 30 s retry, the
      browse, the stack's logging filter, and the bounded close, as in Technical Details (the review removed
      the closing flag, see the Solution Overview [decision])
- [x] `OpcUaLiveSource`: `States` and `PenFaults`, the lazy start on the first `Subscribe` (whose
      subscriptions Task 8 builds; here `Subscribe` returns a stream that only starts the session), `Dispose`
      and `DisposeAsync`
- [x] `AddOpcUaLiveSource(settings)` registers the source as the singleton `ILiveSource` and the logging
      filter; `StartupProbe.BuildArchiveServiceProvider`
      calls it after `AddPostgresData`, so the window resolves the OPC source (the last registration wins).
      `AddPostgresData` keeps its own `ILiveSource` registration until Task 11, because the integration
      containers built from it (`ArchiveProviderFactory`, `UnreachableArchive`, the poll classes and
      `BreakRenderArchiveJourneyTests.cs:60`) resolve it until then
- [x] unit tests: `OpcUaCompositionTests` resolves `ILiveSource` as `OpcUaLiveSource`, once, as a singleton, and
      resolving and disposing it with no server opens no socket and returns at once; `CompositionRootTests`
      resolves `OpcUaLiveSource` (`:35-41`)
- [x] `LiveSourceHost` (test helper): builds a source over a `DemoScadaHost` endpoint with a 1 s grace and a
      `FakeLogCollector`, and records `States` with receipt times (Task 13 moved it to the production 30 s
      grace; only the no-server and the listener tests pass `ShortGrace`, 1 s)
- [x] integration tests `OpcUaSessionTests` (`DemoScadaCollection`): a running server takes the states
      `Connecting`, `Connected`; no server takes `Connecting`, then `Unreachable` after the grace, then
      `Connected` once a server starts on that port; a server disposed and restarted on its port takes
      `Connected`, `Unreachable`, `Connected` within 60 s (dispose 5 s, detection 4-5 s, return up to 5 s,
      Measured); a `TcpListener` that accepts and closes on the port keeps the source in discovery with no
      setup fault; disposing the source while it reconnects returns within 3 s, reports no `Unreachable` and
      writes no break, and no connection reaches the port after; `Dispose()` on a connected source, called
      under a `SynchronizationContext` whose `Post` drops the work, returns within its 3 s bound. The guard for
      `UpdateBeforeConnect = false` is Task 8's relay test: against the plain demo server the configured and
      the advertised URL are the same, so no test here can tell them apart
- [x] the demo server always has a `None` endpoint and a project folder, so `EndpointRefused` and
      `ProjectMissing` are covered by `EndpointChoiceTests` and `ProjectFolderTests` and by one unit test of the
      session's setup-fault path over those two results (reported once, retried at the 30 s cap); the test is
      `ConnectBackoffTests.ASetupFault_IsReportedOnce_AndRetriedAfterThirtySeconds`, over `LiveSessionStates` and
      the pure `ConnectBackoff`, since the session itself has no seam that opens no socket
- [x] run the unit project and
      `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUa"` -
      must pass before Task 8

### Task 8: The subscriptions: samples, breaks, pen faults

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.OpcUa/PenSubscription.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.OpcUa/OpcUaLiveSource.cs`, `OpcUaSession.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/OpcUaLiveSourceTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/OpcUaLiveSourceRestartTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/LoopbackRelay.cs`
- ➕ Create: `SemiPlot/SemiPlot.DataSource.OpcUa/PenSubscriptionSet.cs` (the open subscriptions, the project they are
  built under and the session they live on, so `OpcUaSession` stays under 300 lines)
- ➕ Modify: `SemiPlot/SemiPlot.DataSource.OpcUa/ServerCalls.cs` (subscription create and delete),
  `UnservedPens.cs` (a `Bad` notification counts as an answer)
- ➕ Create: `SemiPlot/SemiPlot.DataSource.OpcUa/PenSubscriptionContext.cs` and `ReportedUnservedSets.cs` (split
  out of `PenSubscription.cs` and `UnservedPens.cs` once each type had a second consumer)
- ➕ Create: `SemiPlot/SemiPlot.Tests.Integration/OpcUa/SampleRecorder.cs`; modify `LiveSourceHost.cs` (subscribes the
  demo's pens, records pen faults and every subscription) and `OpcUaSessionTests.cs` (the dispose-while-reconnecting
  case asserts one loss break per pen before the dispose and no batch after it)

- [x] `PenSubscription` and the registration path of Technical Details: create on `Connected`, delete on
      disposal, loss breaks on leaving `Connected`, value mapping, unserved pens per subscription. A subscription
      closed while the session is down is deleted at the next `Connected`, because `ManagedSession`'s delete waits
      for the session to return and would hold the loop
- [x] `OpcUaLiveSourceTests` (one server per class, 8 pens, change 0.25 s, `Asia/Tokyo`): every pen delivers
      at least 3 samples in 3 s; each value equals `LiveTailGenerator.ChangesAt(options,
      converter.ToArchiveLocal(stamp))` and each stamp has `Kind = Utc`, lies within 60 s of `DateTime.UtcNow` at
      receipt and strictly increases per pen; two unknown pen ids raise one `PensUnserved` fault naming both, and
      a second subscription of the same set raises none; a second subscription of the same set on the live
      session writes no break and each pen's first sample is at or after its newest one before the switch;
      the stream neither completes nor faults through all of it (`Materialize`)
- [x] `OpcUaLiveSourceRestartTests`: a server disposed and restarted on its port: exactly one break per pen,
      each at that pen's newest delivered stamp + 1 ms, all before the first sample of the new session; every
      first sample after it is stamped at or after the new server's start; an 8 s cut of a `LoopbackRelay` between
      the source and the server (longer than the 4-5 s detection, shorter than the 60 s session timeout): one
      break per pen at its newest stamp + 1 ms, the session id unchanged, samples resume with stamps past the
      breaks (the session id read through the session's internal `SessionId`); disposing a subscription while
      the session is down delivers nothing afterwards (the review deleted `SessionId`, a member only the test
      read, and with it the session-id claim, which the measurement holds; what a closed subscription emits
      moved to the socket-free `PenSubscriptionTests`)
- [x] run the unit project and the `OpcUa` filter - must pass before Task 9

### Task 9: The status bar's second indicator and the panel entries

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBarViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppStatusBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/TrendWindow.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppStatusBarViewModelTests.cs`,
  `AppStatusBarViewTests.cs`, `MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`
- ➕ Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs` (`PushLiveSamples`: a list stamped by the
  test, so the lag test can put a break minutes past the newest value)

- [x] `TrendCoordinator.LiveSourceStates` and `LivePenFaults`: the source's streams on the UI scheduler
- [x] `AppStatusBarViewModel` takes the live states beside the archive's: `IsLiveConnected`, `IsLiveFaulted`,
      `LiveConnectionText`
      over `StatusLiveConnecting`, `StatusLiveConnectionOk`, `StatusLiveConnectionFault`, the fault entry, and the
      `Info` entry `StatusLiveConnectionRestoredTitle`/`Detail` on the first `Connected` after a fault; the handler
      is wrapped as the archive's is (`AppStatusBarViewModel.cs:86-120`)
- [x] `AppStatusBar.axaml`: a `LiveConnectionIndicator` button after the archive indicator and a separator,
      `connection-ok` bound to `IsLiveConnected` and `connection-fault` to `IsLiveFaulted`, the same command
- [x] `TrendChartViewModel` reports `LivePenFaults` through
      `ResultReporting.TryReportFailure(MessagePanelViewModel, IError, ILogger)`, and `ApplyRealtimeBatch` writes the
      `Debug` latency line of Technical Details
- [x] `TrendWindow.Build` hands `coordinator.LiveSourceStates` to the bar
- [x] tests: `AppStatusBarViewModelTests` mirror the archive cases for the live indicator (`Connecting` writes no
      entry and reads not connected; a fault writes one `Warning` and sets the fault text; `Connected` after a
      fault writes one `Info`; two `Connected` after one fault write one; a throw in the handler is reported and
      the stream survives; disposal stops following); `AppStatusBarViewTests`: the live indicator carries the state
      class, both states paint from the palette in both themes, `Connecting` carries neither class, and a click
      opens the panel;
      `TrendCoordinatorTests`: both streams arrive on the UI scheduler; `TrendChartViewModelTests`: a `PensUnserved`
      fault lands in the panel once, with severity `Warning` and the `PensUnserved` title, and one batch writes one `Debug` entry carrying `LagMilliseconds` computed from
      the newest value, not a break
- [x] run the unit project - must pass before Task 10

### Task 10: An OPC UA journey to the chart

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Integration/Journeys/OpcUaLiveEdgeJourneyTests.cs` (named
  `OpcLiveEdgeJourneyTests` until Task 13 renamed it, so the `FullyQualifiedName~OpcUa` filter of Acceptance
  Evidence 1 runs it)

- [x] one class in `DemoScadaCollection`, its tests `[AvaloniaFact]` over the integration project's
      `TestAppBuilder` as `BreakRenderArchiveJourneyTests` runs (`:55`): the demo server, an `OpcUaLiveSource` over it, a `TrendCoordinator` over
      that source and a private in-file `IDataProvider` that answers one history read with columns ending one
      minute before the server's start, and a `TrendChartViewModel`, built as
      `BreakRenderArchiveJourneyTests` builds its coordinator and chart (`:71-80`)
- [x] tests: each pen's line holds the history, then a `NaN` column 1 ms ahead of its first live column, then
      live columns whose values are the lattice's; `Navigation.To` reaches the newest delivered stamp under a
      sticky window; no live column carries a stamp at or before the history's last
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUaLiveEdgeJourneyTests"` -
      must pass before Task 11

### Task 11: Delete the archive poll and `poll_interval_ms`

**Files:**
- Delete: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresLiveSource.cs`, `RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`,
  `PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs`,
  `PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`, `SettingsDialog.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs`, `Converge.cs`
- Modify: `ConfigFiles/connection/connection.yaml`
- Delete: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollTests.cs`, `RealtimePollReadTests.cs`,
  `RealtimeSubscriptionTests.cs`, `RealtimeSubscriptionDisposalTests.cs`, `RealtimeEmptyArchiveTests.cs`,
  `ArmedGate.cs`, `BatchCollector.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeArchive.cs`, `UnreachableArchive.cs`,
  `ArchiveProviderFactory.cs`, `ExplainPlanTests.cs`, `CatalogueReadUnreachableTests.cs`, `ConvergeTests.cs`
- Modify: the unit tests listed in Context (Mixed)

- [x] delete the two classes, their registration in `AddPostgresData` (`:44-50`), the two statements,
      `PollInterval` (record, `ToString`, the `CommandTimeoutSeconds`
      comment), the loader's key, constant, DTO member and checks, and the `{2}` of
      `FailureConnectionFileOutOfRangeRemedy` (both languages; `ResourcesTests` checks the placeholders)
- [x] the settings window: the poll field, its row (`SettingsDialog.axaml:31` drops one `Auto`), its rule, its
      key in `ConnectionKeys` and both resx keys
- [x] `ConnectionFileWriter.WriteAsync` loses `pollInterval`; `Converge` loses `_pollInterval`;
      `ConfigFiles/connection/connection.yaml` loses `poll_interval_ms`
- [x] delete the poll-only test classes and helpers; trim the mixed ones to what is not the poll
      (`RealtimeArchive` keeps `Day`, `NextDay`, `AppendAsync` for `ArchiveRowReaderTests`; the poll
      `InlineData`, binders and the tick-bound test leave `ArchiveStatementTextTests`; the two poll plans leave
      `ExplainPlanTests`; `UnreachableArchive.Settings` takes no interval)
- [x] `PostgresCompositionTests` drops the live-source cases (`:54-62,95`); `BreakRenderArchiveJourneyTests`
      (`:60,71-72`), a history-render journey, builds its coordinator over a private in-file live source whose
      subscriptions emit nothing and whose state is `Connected`
- [x] add a test to `PostgresConnectionLoaderTests`: a section that still carries `poll_interval_ms` loads (an
      upgraded site's installed file)
- [x] the grep of Acceptance Evidence 3 prints only the unrelated `PollInterval` users (and the two lines of
      `PostgresConnectionLoaderTests.ALeftoverPollIntervalKeyIsIgnored`, the upgrade test this task adds)
- [x] run the unit project and the whole integration project (container required) - must pass before Task 12

### Task 12: The stand serves its own `opc/` and lags its archive

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/OpcFileWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`, `ConvergeOptions.cs`, `Converge.cs`,
  `Program.cs`
- Modify: `SemiPlot/SemiPlot.AppHost/AppHost.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SeederCommandTests.cs`, `DeliveredConfigurationTests.cs`,
  `ConvergeTests.cs` (`:18,30` build `ConvergeOptions` positionally)
- Create: `SemiPlot/SemiPlot.Tests.Unit/OpcFileWriterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ConvergeTests.cs`, `ConvergeReadinessTests.cs`

- [x] `OpcFileWriter.WriteAsync(configDirectory, host, port)` writes `opc/opc.yaml`, creating the folder;
      `PathFor(configDirectory)` names it
- [x] `converge` takes a required `--opc-port` (1-65535, the follow run's parser) into `ConvergeOptions.OpcPort`
      and writes the file after the connection file; `ConvergeAsync` prints its path beside the connection
      file's
- [x] `AppHost.cs`: `converge` gets `--opc-port OpcPort`; the writer gets `--archive-lag 60` from a new
      `ArchiveLagSeconds` constant with a one-line reason
- [x] tests: `SeederCommandTests` - `converge` with `--opc-port 55875` carries it, without it is refused naming
      it, with `0` or `65536` is refused naming the value (the existing case that refuses `--opc-port` on `converge`
      now expects it accepted); `OpcFileWriterTests` - the written file loads through `OpcUaConnectionLoader` with that host and
      port; `DeliveredConfigurationTests.TheSeederWritesOverTheShippedOpcFile` (the writer's folder is
      `StartupProbe.OpcDirectoryName` and its path exists in the shipped set); `ConvergeTests` loads the written
      `opc/` section; `ConvergeReadinessTests` passes a port
- [x] `dotnet build SemiPlot/SemiPlot.AppHost/SemiPlot.AppHost.csproj` at 0 errors
- [x] run the unit project and the integration filter `FullyQualifiedName~Converge` - must pass before Task 13

### Task 13: Verify acceptance criteria

- [x] every Overview item and every `[decision]` is in the code
- [x] Acceptance Evidence 1-4, each fresh (the `OpcUa` filter ran 12 tests, the journey among them after its
      rename; the Linux leg of Evidence 4 runs in CI only)
- [x] `dotnet test SemiPlot.slnx` with the container runtime up (unit 2481, integration 204). The first run
      failed two load-dependent tests, both green alone: a served-server state sequence met the host's 1 s grace
      before its first `Connected`, so `LiveSourceHost` now takes the production grace and only the two grace tests
      pass `ShortGrace`; `OpcUaCompositionTests` waited on a starved pool through the synchronous `Dispose`, so it
      disposes the container asynchronously
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files exit 0

### Task 14: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/sources.md`, `docs/architecture/README.md`, `docs/architecture/data-integration.md`,
  `docs/architecture/trend-feature-spec.md`, `docs/architecture/charting.md`, `docs/architecture/overview.md`,
  `docs/architecture/ui-text.md`, `docs/architecture/ui-theme.md`, `docs/architecture/scada-archive-observed.md`,
  `docs/architecture/testing-strategy.md`, `docs/architecture/postgres-topology.md`,
  `docs/architecture/trend-interaction.md`, `docs/architecture/bench.md`, `docs/plans/backlog.md`, `AGENTS.md`,
  `readme.md`

- [x] `sources.md`: `[DEC:opc-live-edge]` in the decisions table (`:73-82`): the live edge reads the SCADA's
      OPC UA server, history stays on the archive, the archive replaces a live point only when a history read
      covers it; and `[MEAS:opc-probe-20261020]` in the measurements table (`:65-71`) for the OPC rows of
      `scada-archive-observed.md`, which also covers the unmarked "measured 2026-10-20" lines of `bench.md`
      (`:205,228,244`)
- [x] `README.md` locked decisions (`:81`): "history, extent and realtime alike" becomes "history and extent";
      "no OPC UA client" becomes an OPC UA client for the live edge only, citing `[DEC:opc-live-edge]`; a
      description line for the OPC part of `data-integration.md` (`:21-25`); the shipped set names
      `opc/opc.yaml` (`:51-53`)
- [x] `data-integration.md`: the title and `:8-9` (the integration surface is the archive plus the live
      source's server); Responsibility zones `:22`; the provider surface `:30-36,126-138` (the port's three
      members, `OpcUaLiveSource`, `LiveSourceState`, `LiveSourceError`); Operation to statement drops the two
      poll rows and binders (`:322-327,335-336,351-353`); `What one history query covers` and the realtime
      clauses that name the poll (`:411-412,424-427,455-456,491-493,529-555,633-634`); Time boundary `:878`
      gains a fourth path, the live source, whose stamps arrive as UTC and pass no `ArchiveTimeConverter`;
      Quality and gaps `:925,941-942` (a `q = 32` row reaches the live edge only through a history read now);
      Realtime `:953-1044` rewritten: the port's contract with `States` and `PenFaults`, the chart-side
      ahead-break rule, the source's breaks, the session and its states, the subscription plan, the pen-set
      change, the source's own pins; the connection state section `:1048,1086-1099` (two readers of the
      `NpgsqlDataSource` repeat on their own, and the poll's log-only cases go); Error semantics gains the OPC
      rows and loses the realtime tick (`:1115`); Two error planes `:1145-1146,1153-1154,1161,1206-1213`; No
      failure stops at the log `:1225-1227,1235-1249,1295-1303`; Configuration `:1343-1382` gains the `opc/`
      section and loses `poll_interval_ms` and the tick's bound; Startup `:1404-1429` loads `opc/`; Field triage
      `:1462-1485` gains an OPC step (is the indicator up; the latency debug line)
- [x] `trend-feature-spec.md`: `RT-1` (`:214-216`) reads "Realtime stream from the SCADA's OPC UA server";
      `:9`, `DA-1` (`:170-172`) and the MUST summary (`:266,272`) keep PostgreSQL for history and extent; DA-8
      `:202` drops the poll
- [x] `charting.md` `:64-69,98-148,475-479,711-724,744-747,773-807`: the live edge from OPC UA, the ahead-break
      and the join rule, the dropped-tail unjoin, the source's breaks; and the coarse-layer consequence: at a
      coarse layer the ahead-break's first live sample opens a column at its own X and the samples after it
      fold into that column, which keeps the opening X (`EnvelopeLine.cs:243-247`), so the live line resumes
      from the start of its first live bucket. The behaviour stays; the `live-hold-to-edge` slice owns the grey
      look of the span
- [x] `overview.md`: Purpose `:12`, the stack table `:23-27` (an OPC UA row), the component diagram `:46-97`
      (a `SemiPlot.DataSource.OpcUa` box, the status bar's two indicators), `:99-106,127-133,158-164`, Data flow
      `:335-341`, Deployment `:355-399` (the `opc/` row of the section table, the shipped set), the settings window
      `:403-490` (no poll field; the window does not edit `opc/` yet), A failed read `:708-731` (drop the "1 to
      2 s" claim), Command line `:864`, Scope status `:907-911`
- [x] `ui-text.md`: the connection-file texts per section and the dropped poll bound (`:198-211`), the
      validation order without `SettingsPollIntervalInvalid` (`:180-184`), the status bar's two connection texts
      (`:132-138`), the `LiveSourceError` texts
- [x] `ui-theme.md` `:166-167`: both indicators read `AppConnectionOkBrush` and `AppConnectionFaultBrush`
- [x] `scada-archive-observed.md`: a section "The OPC UA server" after "Database outages" with the roadmap's
      table (`:41-54`), the endpoint and namespace reads, the queue-size quirk, and this plan's Measured table,
      marked `[MEAS:opc-probe-20261020]`; Consequences item 1 (`:586-589`) says the live edge now takes that
      channel; `:62` keeps "no OPC server" for the capture run
- [x] `testing-strategy.md` `:42,47,110-122,148-190,230-240,389-395,400-410,442-448`: the OPC family (the demo
      server, `DemoScadaCollection`), the new unit rows, the journey row, the dropped poll classes, the fifth
      project reference
- [x] `postgres-topology.md` `:102-149`: the diagram and text lose `PostgresLiveSource`
- [x] `trend-interaction.md` `:67-85,620-626`: OPC `Bad` maps to a break at `ILiveSource`; the poll lines go
- [x] `bench.md`: the demo writer and the stand (`:78-124,377-384,488`) - the stand runs `--archive-lag 60`
      and the viewer's live edge reads the stand's OPC UA server; the converge verb (`:392-416,437-467`) writes
      `opc/opc.yaml` from its required `--opc-port`; the demo's directories table names it
- [x] `backlog.md` `:9-14,102-104`: the live-edge poll is gone
- [x] `AGENTS.md`: the section folders (`:24-37`) become three; the `converge` paragraph and example
      (`:74-89`) gain `--opc-port`; the test table and `InternalsVisibleTo` (`:117-132`) name
      `SemiPlot.DataSource.OpcUa`; the socket-class list (`:191-195`) loses the poll classes and names the
      `OpcUa` classes in `DemoScadaCollection`; DI (`:267-282`) adds `AddOpcUaLiveSource()`; the failure rule
      (`:386-390`) loses the poll and names the OPC indicator; Data-source projects (`:399-451`) gain the OPC
      project, the port's members, the ahead-break rule's home, and the time-boundary count
- [x] ➕ `AGENTS.md:385` and `overview.md:844` name `AppStatusBarViewModel.ConnectionRestored` as the one
      message with no error behind it; `LiveConnectionRestored` (Task 9) is the second, the live indicator's
      recovery entry
- [x] `readme.md` (Russian) `:73,88-94`: the configuration row names `opc/`, the data-source row names the
      OPC UA server for the live edge
- [x] ➕ doc lines the checkboxes did not name but the code required: `data-integration.md` says
      `MessageSeverity.Info` has two writers and `ArchiveFailureMapper.Map` eleven further arms, the live
      source's among them; `AGENTS.md` and `testing-strategy.md` name five project references; the binder
      count in `data-integration.md` and `testing-strategy.md` drops the two poll binders
- [x] move this plan to `docs/plans/completed/` (left in place for the delivery step)

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual acceptance on the real SCADA** (this machine's Simple-Scada 2.7.6.0, project running, OPC UA on
49875; the viewer started with `--logging-level debug` over a configuration copy whose `opc/opc.yaml` is the
tracked one):

1. The status bar reads both indicators connected within a few seconds of the window opening, and the panel
   holds no OPC entry.
2. Change a variable on the SCADA (or watch a fast pen). The latency line in `semiplot.log`
   (`LagMilliseconds`) stays under 1000 for every batch over a few minutes; record its median and maximum.
3. Leave the window sticky for longer than the archive's lag of a fast pen (3 min). Nothing on the chart
   changes when the archive's rows arrive: no line moves, no gap closes, no history read is logged.
4. Make a gesture that re-reads the window. The ahead-break closes only where the archive now holds rows.
5. Stop the project. Within seconds the OPC indicator reads not answering, the panel holds one `Warning`,
   every pen's line ends with a gap, and the chart keeps panning and zooming over the archive. Start the
   project again: the indicator returns, one `Info` entry lands, and every pen resumes past its gap.
6. Optional, because a kill loses the archive rows the SCADA has not flushed: kill `Server.exe` of
   the SCADA instead of stopping the project (Task Manager), then start the project again: the same as step 5, and the span the archive lost stays drawn from the live points until a history
   read covers it.

7. Start the viewer while the project is stopped, then start the project: record the time from the
   project's start to the OPC indicator reading connected (expected within about 5 s of the server's start, the
   pre-session retry cap).

**The stand** (`dotnet run --project SemiPlot/SemiPlot.AppHost`):

1. `%TEMP%\SemiPlot\ConfigFiles\opc\opc.yaml` carries `port: 55875`, and the viewer's OPC indicator reads
   connected with no panel entry at start.
2. A cold stand start (first run after a reboot, or after deleting `%TEMP%\SemiPlot\OpcDemoServer`): record the
   time from the writer's start to the OPC indicator reading connected, and whether the 30 s grace held the
   fault entry back.
3. The writer prints `archive lag     60 s`; at open each pen draws history, a gap of about a minute, then the
   live line; a gesture after a minute closes the gap.

**Remaining slices** of `docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md`:

- opc-settings: the settings window's OPC block after the database block, editing the opc/ keys under the
  existing rules.
- live-hold-to-edge: the grey ahead-break span and the held line to the live edge, render only.

**PR:** the body carries `Closes #116`.

**Executed by exec:**
- branch: opc-live-source

## Verify it yourself

Pre-fix is `master` `a92c12f` (the live edge reads the archive poll); post-fix is the branch tip. The OPC tests
below do not exist at `a92c12f`, so the pre-fix side of each check is the stand, not a red test.

1. The defect on the stand. At `a92c12f`, run the AppHost with the demo writer's `--archive-lag 60`: the chart's
   newest live point trails the clock by at least 60 s. At the branch tip, `dotnet run --project
   SemiPlot/SemiPlot.AppHost` (the AppHost now passes `--archive-lag 60` itself): at open each pen draws
   history, a gap of about a minute, then a live line that follows the clock; the OPC UA indicator reads
   connected. Post-Completion "The stand" lists what to record.
2. The live edge over OPC UA, no container needed:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~OpcUa"`.
   It covers the 8 pens' lattice values with UTC stamps (server in `Asia/Tokyo`), one break per pen 1 ms past
   its newest stamp on a server restart, the grace and the late server, two unknown pens as one
   `PensUnserved` fault, the bounded `Dispose()` under a dropping `SynchronizationContext`, a pen-set change
   that writes no break, and the journey `OpcUaLiveEdgeJourneyTests` (history, a `NaN` column 1 ms ahead of the
   first live column, the live edge at the newest stamp).
3. The unit side: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`, with
   `SubscriptionPlanTests` (queue size 1), `EndpointChoiceTests`, `TrendPenStateTests` (the ahead-break),
   `DeliveredConfigurationTests` (the shipped `opc/`), `FailureSeverityTests` and `AppStatusBarViewModelTests`.
4. The deletions and boundaries, each printing what Acceptance Evidence 3 states:
   `git grep -n -E "RealtimePoll|RealtimeBaseline|PostgresLiveSource|poll_interval_ms|PollInterval" -- SemiPlot ConfigFiles ':!SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs'`,
   `git grep -n -E "ArchiveTimeConverter|TimeZoneInfo|ToLocalTime|ToUniversalTime" -- SemiPlot/SemiPlot.DataSource.OpcUa`,
   `git grep -n "ILiveSource" -- SemiPlot/SemiPlot.UI`, `git grep -n "49875" -- SemiPlot/SemiPlot.Tests.Integration/OpcUa`.
5. The 1 s figure, the archive catching up, a stopped project and the start-up timings have no automated
   check: Post-Completion "Manual acceptance on the real SCADA" steps 1-7.
