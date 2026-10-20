# OPC UA demo server in the demo writer

## Overview

- The seeder's demo writer (`--follow`) gains a Simple-Scada-shaped OPC UA server. Every pen the writer
  archives is also a variable of that server, and each lattice change is served at its own instant, with
  the `SourceTimestamp` the vendor's server would give it. Nothing new produces values: the server reads
  the same lattice `LiveTailGenerator` writes, so every served value is an archive row; outside the two DST
  transitions the stream and the archive's change rows agree row for row.
- A new `--archive-lag <seconds>` holds the archive inserts back by that span, so the archive trails the
  OPC stream the way the SCADA's buffered archive trails the plant (roadmap, "Root cause").
- A new `--opc-port <port>` names the listening port. A follow run requires it: the real Simple-Scada
  listens on 49875 on the operator's machine, and a writer that took a default port would either fail
  beside it or take the port from it. `SemiPlot.AppHost` passes the stand's port, 55875.
- The integration project starts the same server in process on a free port, so the next slice
  (`opc-live-source`) can test its client without the real SCADA.
- The package line `OPCFoundation.NetStandard.Opc.Ua.*` 2.0.1 enters `Directory.Packages.props`, the four
  central `Microsoft.Extensions.*` pins move from 10.0.8 to 10.0.12, which 2.0.1 requires, and
  `Microsoft.Extensions.Logging.Console` 10.0.12 joins them for the server's warnings.
- No viewer code changes. The shipped viewer does carry `Microsoft.Extensions.DependencyInjection` and
  `.Logging` 10.0.12 instead of 10.0.8, because `SemiPlot.UI` references both through the central pins. No
  OPC client code enters a production project; the test-side client lives in `SemiPlot.Tests.Integration`.

## Context (from discovery)

- Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice opc-demo-server
- Verified against `master` at `54f455e`, read through `git show master:<path>`. Line numbers are at that
  commit. Scratch probes ran outside the repository on 2026-10-20: `srvprobe` (a 2.0.1 server and client
  in one process, run on Windows and in the `mcr.microsoft.com/dotnet/sdk:10.0` Linux container) and
  `pinprobe` (a `git archive master` copy with this slice's package changes, built and unit-tested).
- The demo writer: `Program.FollowAsync` reads the archive edge once through `StaleArchiveGuard`, then each
  tick appends `LiveTailGenerator.Generate(options, edge, now)` and runs `CoarseFlush.FlushAsync(options,
  edge, now)`, then sets `edge = now` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs:46-89`). Ctrl+C
  stops the loop where it waits (`:50-52`, `:84-88`). The clock is `DateTime.Now` with its `Kind` stripped
  (`:93-96`). `ReportingAsync` prints `SeederException`, `NpgsqlException`, `ArgumentException` and
  `FormatException` as one line and lets anything else through with its stack trace (`:16-29`).
- `FollowOptions(ConnectionString, Interval, PenCount, Seed, ChangeSeconds)` and `MaximumSeconds = 86400`
  (`FollowOptions.cs:4-14`). Constructed by `SeederCommand.Interpret` (`SeederCommand.cs:322`) and in four
  tests: `CoarseFlushTests.cs:244-252`, `FollowRestartTests.cs:80-83`, `LiveTailGeneratorTests.cs:142-145`,
  `SharedLatticeTests.cs:28-33`; `SeederCommandTests.cs:65-96` asserts its fields.
- The command line: `--follow` selects the follow run (`SeederCommand.cs:62-68`); `_seedingOnly` lists the
  options a follow run rejects (`:70-75`, checked at `:310-318`); there is no mirror list of options a
  seeding run rejects. `ParseSeconds` refuses NaN, infinity, `<= 0` and `> MaximumSeconds` (`:217-238`).
  `ParseWholeNumber` takes a minimum and maximum (`:175-199`). `converge` registers its own options
  (`:146-162`), so an option not added to it is refused by System.CommandLine.
- The lattice: `RawLayerGenerator.AppendWindow` is the one emitter (`RawLayerGenerator.cs:53-96`), a change
  sits at `index * intervalTicks` (`:74`), `FirstIndex` is the private ceiling (`:176-179`),
  `ValueAt` is private (`:181-186`), `ChangeIntervalTicks` rounds to whole milliseconds (`:100-105`).
  `LiveTailGenerator.Generate(options, after, to)` emits `after < t <= to` through `AppendWindow`
  (`LiveTailGenerator.cs:7-32`). `SharedLatticeTests` pins one lattice for the seeding run and the follow
  run (`SharedLatticeTests.cs:9-91`; `bench.md:42-51`).
- The pens: `RawLayerGenerator.SelectPens(count)` takes them round-robin across the groups of
  `PrimaryGroup` (`RawLayerGenerator.cs:23-51`). `SyntheticPen.Name` is the catalogue name and
  `PrimaryGroup` the first group, the empty string for an ungrouped pen (`SyntheticPen.cs:5-20`).
  `UncommissionedPenId` 5001 has no group (`SyntheticPenCatalog.cs:102`), so `""` is a sixth round-robin
  group and the default 8-pen slice is 1000, 2000, 3000, 4000, 5000, 5001, 1001, 2001. `TwoGroupPenId` 1000
  also sits in `Watchlist` (`SyntheticPenCatalog.cs:92`). `converge` seeds `SeederOptions` defaults (8 pens)
  and the AppHost's writer passes no `--pens`, so the stand's writer and catalogue hold the same 8 pens
  (`bench.md:249-251`, `AppHost.cs:50-52`).
- The time boundary: the archive holds naive local time (`Program.cs:91-96`); `ArchiveTimeConverter` in
  `SemiPlot.DataSource.Postgres` is the viewer's one conversion, `ToUtc` resolving a skipped local time by
  the base offset and `ToArchiveLocal` the reverse (`ArchiveTimeConverter.cs:7-37`).
  `ArchiveTimeConverterTests` fixes `Europe/Berlin`, whose 2026 rule matches on Windows and on ubuntu
  (`SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveTimeConverterTests.cs:9-17`). The seeder references `SemiPlot.Core` only
  (`SemiPlot.Tools.ArchiveSeeder.csproj:7-14`).
- The guard on a restart: `StaleArchiveGuard.CheckAsync(connection, now)` refuses an archive more than
  five minutes behind `now` (`StaleArchiveGuard.cs:11`, `:19-44`).
- The pins: `Microsoft.Extensions.DependencyInjection`, `.DependencyInjection.Abstractions`, `.Logging`,
  `.Logging.Abstractions` at 10.0.8, `Microsoft.Extensions.Diagnostics.Testing` at 10.6.0
  (`SemiPlot/Directory.Packages.props:19-23`). `SemiPlot.Tests.Unit`, `SemiPlot.Tests.Integration` and
  `SemiPlot.UI` reference `DependencyInjection` and `Logging` directly; `SemiPlot.DataSource.Postgres`
  references the two `Abstractions`. `TreatWarningsAsErrors` and `AnalysisMode Recommended` hold for every
  project (`SemiPlot/Directory.Build.props:10-16`).
- The AppHost: the writer runs `--connection ... --follow 1 --change-seconds 0.5` after `converge`
  (`AppHost.cs:50-52`); the stand's Postgres sits on host port 55432 so it never meets a local server on
  5432 (`AppHost.cs:18`, `:34`); argument constants are strings (`AppHost.cs:21`); the AppHost cannot
  compile against the seeder, so bench constants repeat there (`AppHost.cs:8-9`, `bench.md:280-283`). The
  AppHost injects console-formatter environment variables that the seeder ignores (`bench.md:219-222`).
- The integration project: no `[Collection]` and no fixture means no container, as
  `ConvergeReadinessTests` runs (`ConvergeReadinessTests.cs:12-16`). It references the seeder, the provider,
  Core and the UI (`SemiPlot.Tests.Integration.csproj:29-34`), carries no `xunit.runner.json`, and runs on
  the Linux CI leg only (AGENTS.md, Test).
- Configuration: `ConfigFiles/` holds `app/` and `connection/` only. `DeliveredConfigurationTests` names
  its loaders one by one (`DeliveredConfigurationTests.cs:29-85`). The installer copies `ConfigFiles\*`
  recursively with `onlyifdoesntexist` (`Installer/SemiPlot.iss:71`).
- The real server, measured 2026-10-19 (roadmap table, lines 41-54): namespace 3 is
  `http://simple-scada.com/UA`; `Objects` holds folder `ns=3;s=<project ID>root_<n>` with browse name
  `root`; a variable is `ns=3;s=<project ID><trends.id>` with browse name = the variable name; a SCADA group
  is a folder beside `root`, `ns=3;s=<project ID><name>_<n>`, and a variable inside keeps its address;
  `None` + Anonymous is offered beside Sign and SignAndEncrypt; the initial value arrives at once with the
  stamp of the last change; after a start every first notification carries the start instant; a
  monitored-item queue above 1 makes the first notification an empty `Good` value. Support states
  `ns=3;s=<project ID><variable ID>` is stable (`sources.md:63`).
- The 2.0.1 client spike connected to the real server with an anonymous token policy whose
  `SecurityPolicyUri` is null (`dotnet-spike/v20/Program.cs`, `SPIKE_TOKENPOLICY` unset, runs in
  `dotnet-spike/run20/`). The client accepts the server's policy only when token type and
  `SecurityPolicyUri` match, null equal to empty (`Session20.cs:6180-6195` of the stack source).
- `GetEndpoints` of the real server, read 2026-10-20 with asyncua (`scratchpad/opc_assumptions.py`):
  seven endpoints, all advertising `opc.tcp://WORKING-PC:49875`: `None`, and `Sign` plus `SignAndEncrypt`
  under each of `Aes128_Sha256_RsaOaep`, `Basic256Sha256` and `Aes256_Sha256_RsaPss`. Every endpoint
  carries one token policy, `PolicyId = Anonymous`, type Anonymous, `SecurityPolicyUri =
  http://opcfoundation.org/UA/SecurityPolicy#None`. So the real server advertises `#None`, not an empty
  URI, and the spike's null-policy client still connected to it.
- The same read of `Objects`: `Server`, then `FD34514A35<system folder>_1` (the system-variables folder),
  `FD34514A35root_2` (browse name `root`), then `FD34514A35group1_3` (a user group). Every browse name,
  the folders' and the variables' (`Fast1`, `Fast2`, ...), carries namespace index 3. Namespace array:
  `[http://opcfoundation.org/UA/, urn:WORKING-PC:SimpleScada:UAServer,
  http://www.simple-scada.com/UA/Namespace, http://simple-scada.com/UA]`.

### Measured on 2026-10-20 (scratch probes, not committed)

Every row holds on Windows and in the Linux container unless it names one platform.

| Question | Answer |
| --- | --- |
| Does a 2.0.1 server bind port 0 to a free port? | No. `BaseAddresses = opc.tcp://127.0.0.1:0` listened on 4840, the OPC UA default |
| Which interface does it bind? | `127.0.0.1` only: a connect to `::1` and to every non-loopback address of the host fails (Windows: `netstat` shows `127.0.0.1:<port> LISTENING`) |
| Does a `None`-only server run without an application certificate? | No. `ValidateAsync(ApplicationType.Server)` throws "ApplicationCertificate must be specified", and every `CreateSession` fails with a `NullReferenceException` inside the server |
| What does the certificate cost? | `ApplicationInstance.CheckApplicationInstanceCertificatesAsync(true, null, ct)` over directory stores writes three files (`own/certs`, `own/private`, `trusted/certs`) in 124-304 ms |
| Can the stores live in memory? | Not on the non-DI path: store type `InMemory` throws "Invalid store type name" at validation; the provider registers only through `CertificateManagerOptions.StoreProviders` |
| What does the server advertise for Anonymous? | `SecurityPolicyUri = ...#None` on the `None` endpoint, the same as the real server there; a client asking with a null policy then fails `BadSecurityChecksFailed`. The plan keeps `#None` and has the client take the advertised policy instead (Solution Overview, Security) |
| Sign and SignAndEncrypt beside None? | Two more `ServerSecurityPolicy` entries (`Basic256Sha256`) add the two endpoints; the null-policy anonymous client still connects over `None` |
| Which index does the custom namespace get? | 2, with `http://opcfoundation.org/UA/Diagnostics` at 3. `server.NamespaceUris.GetIndexOrAppend(<Diagnostics URI>)` inside `INodeManagerFactory.Create`, ahead of the manager's own registration, puts Diagnostics at 2 and `http://simple-scada.com/UA` at 3 |
| What else does a default server do? | It dials a discovery server at `opc.tcp://localhost:4840` (`RegisterServer2`) until `ServerConfiguration.MaxRegistrationInterval = 0` |
| A busy port? | `StartAsync` throws `ServiceResultException` `BadNoCommunication`, "Failed to establish tcp listener sockets on port N" |
| Start and stop cost | `StartAsync` 1.5-4.3 s. `StopAsync` 141-318 ms with no session, 5.0-5.1 s with a session attached. A second server on the same port starts 84 ms (Windows) and 97 ms (Linux) after the first stopped with a session attached |
| Two servers in one process | Start and serve side by side on two ports |
| The initial value and changes | Delivered at subscribe with the `Timestamp` the variable was created with; with `SamplingInterval = 0`, `ClearChangeMasks` after a value and timestamp write raised one notification per write every 0.5 s; an unknown node fails its monitored item with `BadNodeIdUnknown` |
| The pins | With the two OPC packages at 2.0.1 and the pins at 10.0.8, restore fails `NU1605` (package downgrade) in `SemiPlot.Tests.Unit` and `SemiPlot.Tests.Integration` for `DependencyInjection` and `Logging`. With the four pins at 10.0.12 the solution builds with 0 errors (the two known `NU1902` of the AppHost remain) and the unit suite passes 2297 of 2297 |
| `Diagnostics.Testing` | Stays 10.6.0: the client pulls `Microsoft.Extensions.Telemetry.Abstractions` 10.10.0 into `SemiPlot.Tests.Integration`, and a `FakeLogger` 10.6.0 logs and reads back over it |
| Analyzers in the packages | None: `Opc.Ua.Server` and `Opc.Ua.Core` 2.0.1 ship no `analyzers/` folder |

## Development Approach

- **testing approach**: Regular (code first, then tests), with one exception: the `SharedLatticeTests` fact
  of Task 3 is confirmed red against a deliberately wrong publisher before it is kept.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- compatibility: a follow run now requires `--opc-port` and starts a server; a hand command that omits the
  option fails before anything connects, naming it. Seeding and `converge` runs are unchanged.

## Testing Strategy

- **unit tests** (`SemiPlot.Tests.Unit`, plain `[Fact]`, traits `Component=Core`, `Area=Data`,
  `Category=Unit`): the command line, the lattice lookup, the follow edge, the shared lattice.
- **integration tests** (`SemiPlot.Tests.Integration`, plain `[Fact]`, traits `Component=Core`,
  `Area=Data`, `Category=Integration`): the server against a test-side 2.0.1 client over loopback. They
  share one named collection, `DemoScadaCollection`, with no collection fixture, so they never run beside each
  other
  and need no container; they stay outside `ArchiveDatabaseCollection` and never dial 49875.
- **time zone**: the CI Linux leg runs in UTC, where a local-as-UTC stamp passes unnoticed. The value test
  runs the server under `Asia/Tokyo` (+09:00, no DST, the same IANA id on Windows and on ubuntu) and checks
  every stamp against true UTC; a zone without DST keeps the test clear of the fall-back hour, and a
  local-as-UTC stamp misses by nine hours.
- **real time**: the server publishes on the wall clock. The tests use short change intervals and assert
  values, stamps and a floor on the notification count, never latency.
- **server logs in tests**: every server gets a logger factory over a `FakeLogCollector` owned by its host
  (the class fixture or the test), and every assertion message carries the records of `Warning` and above
  added since that test began, so a failing test shows the server's side.
- no e2e suite exists; the stand is checked by hand (Post-Completion).

## Acceptance Evidence

Today the stand serves no OPC UA: `Test-NetConnection 127.0.0.1 -Port 55875` reports
`TcpTestSucceeded : False` while the stand runs, and the archive's newest row follows the clock within one
tick.

Automatable, after the slice:

1. `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~DemoScada"`
   passes with no container runtime: three endpoints with `None` among them and one anonymous token per
   endpoint, `#None` on the `None` endpoint (the stock stack leaves the secured endpoints' URI empty; Task 4
   ⚠️); the namespace `http://simple-scada.com/UA` at index 3;
   `root_2` ahead of the group folders numbered from 3 under `Objects`; every one of the stand's 8 pens served as `ns=3;s=<project ID><id>` with its catalogue
   name; every notification after the first carries the lattice value at a true-UTC lattice instant under
   `Asia/Tokyo`; the first notification after a start carries the start instant; an unknown node fails
   `BadNodeIdUnknown`; a busy port fails with a `SeederException` naming it; a stopped server starts again
   on its port; a second start reuses the certificate.
2. `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SharedLatticeTests|FullyQualifiedName~SeederCommandTests|FullyQualifiedName~LiveTailGeneratorTests|FullyQualifiedName~FollowEdgeTests"`
   passes; `SharedLatticeTests` holds three facts, the new one red against a publisher that reads the next
   lattice index.
3. `dotnet build SemiPlot.slnx` at 0 errors and no warning beyond the AppHost's two `NU1902`;
   `dotnet test SemiPlot.slnx` green on both legs of CI.

Manual, on the stand: see Post-Completion.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope

## Solution Overview

Two types own the server in `SemiPlot.Tools.ArchiveSeeder`: `DemoScadaServer` (configuration, start,
stop, the publishing loop) and `DemoScadaNodeManager` (the address space and the variable writes). The
server starts from the follow run's options, builds the address space for
`RawLayerGenerator.SelectPens(options.PenCount)`, and runs one publishing loop. The loop wakes at each
lattice change instant, asks `LiveTailGenerator` for the change rows at that instant, and writes them into
the variables. The archive writer keeps its statements; its window end moves back by `--archive-lag`,
through a new `FollowEdge` type that owns the edge.

Decisions:

- **[decision] One source of values.** The server publishes only rows `LiveTailGenerator` emits. A new
  `LiveTailGenerator.ChangesAt(options, instant)` returns the change rows at the newest lattice instant at
  or before `instant`, through `Generate(options, change.AddTicks(-1), change)`, so `AppendWindow` stays the
  one emitter; a window one tick wide holds the change row and never its anchor, whose instant lies 100 ms
  earlier. The newest instant comes from `RawLayerGenerator.LatestChangeTicks`, which sits beside
  `FirstIndex` so the lattice arithmetic stays in one file. `SharedLatticeTests` gains the third path.
- **[decision] The server publishes changes, not anchors.** The anchor row repeats the previous value one
  poll interval early; it is the archive's two-rows-per-change shape, not a change the plant made.
- **[decision] One value per pen per wake.** A variable holds one current value and the client's monitored
  item keeps a queue of 1. A process stall that skips lattice instants publishes the newest one only; the
  archive still receives every row.
- **[decision] The start stamp.** At start each variable takes the lattice value at the newest change at or
  before the start and the start instant as its `SourceTimestamp`; the loop's first publish is the next
  change. The loop's last served instant is seeded with `LatestChangeTicks` of the start, so its first write
  is the next change and no stamp ever moves back past the start. A pen that has not changed since the
  start keeps the start stamp, as on the real server. The start instant is read ahead of the stack's
  1.5-4.3 s start, so a change inside the start is written as soon as the loop runs: a client sees the
  start stamp only when it subscribes before the first change after the start, never at the stand's 0.5 s
  lattice. Seeding the loop from the clock after the start instead would skip such a change until the
  next one, a day of stale value at an 86400 s lattice.
- **[decision] The time boundary reuses the viewer's converter.** The seeder gains a project reference to
  `SemiPlot.DataSource.Postgres` and the server takes a `TimeZoneInfo`, wrapped in `ArchiveTimeConverter`.
  `SourceTimestamp = converter.ToUtc(row.Timestamp)` and the loop's clock is
  `converter.ToArchiveLocal(DateTime.UtcNow)`. A live point and the archive row that later replaces it then
  map to one UTC instant by construction, through the code the viewer runs. Production passes
  `TimeZoneInfo.Local`; the integration tests pass `Asia/Tokyo`. A second copy of the rule inside the
  seeder would be a duplicate the next DST bug splits. The cost is the autumn fall-back hour: the local
  clock repeats 02:00-03:00, `LatestChangeTicks` falls behind the last served instant, and the server
  publishes nothing new for that hour, while its first-pass stamps resolve to standard time, an hour ahead
  of true UTC. The archive rows of that hour take the same path, so the two agree with each other; the real
  server does not stall there. At the spring jump the archive tick spans the skipped hour and writes lattice
  rows at local instants that never existed (today's behaviour), while the server publishes only the
  instant after the jump.
- **[decision] Address space.** Namespace `http://simple-scada.com/UA` is pinned at index 3 by registering
  the Diagnostics URI first (measured above): the real server has it there and the vendor calls
  `ns=3;s=<project ID><variable ID>` stable, so this is the real contract, not a demo convenience. The
  project ID is a private constant `5E4D0C1A0B`, ten hex characters like the measured `FD34514A35` and
  different from it, so a client that hard-codes the real ID fails against the demo. `Objects` organizes,
  in this order, the folder `<ProjectId>root_2` (browse name `root`) and then one folder per distinct
  non-empty `PrimaryGroup` of the served pens in `SelectPens` order, `<ProjectId><group>_<n>` with `n` from
  3, as the real server numbers them (measured above; the real `_1` is its system folder, not reproduced).
  Every browse name carries namespace index 3 (measured above). A client finds `root` by browse name, never
  by position or by a fixed node id. `root` organizes every served pen; each group folder organizes the same
  variable nodes of its pens. An ungrouped pen (5001) sits under `root` only, and a pen's later groups
  (`Watchlist`) get no folder: the catalogue groups stand in for SCADA groups, and only the primary one
  is placed. Variables: `<ProjectId><PenId>`, browse and display name `SyntheticPen.Name`, `DataType` Double,
  scalar, current-read access, status `Good`.
- **[decision] Not reproduced.** The `Sys*` sibling folder (its node and browse name were not recorded);
  `Float`/`Int64`/`Byte` variables (the lattice yields doubles and the archive stores them; a rounded integer
  variable would differ from its archive row); `Bad` statuses; the advertised host name differing from the
  dialled address (the demo binds and advertises `127.0.0.1`); the empty first notification under a
  monitored-item queue above 1 (the stock stack delivers the real value at any queue size); an abrupt
  server kill (the server offers `DisposeAsync`, a graceful `StopAsync`, only). `opc-live-source` covers
  numeric conversion, `Bad`, the queue size of 1 and keeping the configured address over the advertised
  one in unit tests of its own. It reproduces a project restart, which destroys the session, by disposing
  the demo server and starting a new one on the same port (measured at under 100 ms); a loopback TCP
  relay its test cuts is the second case, a lost transport with the session still alive on the server.
- **[decision] Security.** Three endpoints, `None`, `Sign` and `SignAndEncrypt`, the last two under
  `Basic256Sha256`, one of the three secured policies the real server offers (measured above); the endpoint
  list exists so a client that picks a secured endpoint fails here as it would on the real server. One
  stock anonymous token policy and no `GetUserTokenPolicies` override: the stack advertises it on `#None` at
  the `None` endpoint, as the real server does, and with an empty `SecurityPolicyUri` at `Sign` and
  `SignAndEncrypt`, where the real server shows `#None` (measured, Task 4 ⚠️). A test client takes the anonymous token policy from
  the `None` endpoint's own `UserIdentityTokens` rather than building a null-policy one, which works against
  both servers; if a 2.0.1 client still fails against the demo, record the error and the client
  configuration in the plan and stop that task rather than diverging from the real server's
  advertisement. Client certificates are never trusted, so only `None` admits a session.
  `MaxRegistrationInterval = 0` stops the discovery-server registration.
- **[decision] The application certificate lives in a directory the caller names.** The server cannot run
  without one (measured), and the in-memory store needs the DI hosting path. The follow run passes the
  fixed `%TEMP%\SemiPlot\OpcDemoServer` (`Path.Combine(Path.GetTempPath(), "SemiPlot", "OpcDemoServer")`),
  so every run, killed or stopped, reuses one certificate. The first run creates the directory and nothing
  removes it: it holds the server's certificate and private key, and `rejected/certs` collects the
  certificate of every client refused on a secured endpoint. `DemoDirectories` sweeps only `ConfigFiles`
  and `Logs` beside it. Tests pass a directory of their own and delete it.
  ASSUMPTION: `CheckApplicationInstanceCertificatesAsync` replaces a certificate that has expired; the
  stand check of Post-Completion does not reach that case.
- **[decision] Server logging.** The stack logs through a `LoggerFactory` over the console provider,
  `Warning` and above, single line, all of it to standard error (`LogToStandardErrorThreshold =
  Warning`), so a rejected session leaves a line on the stand. The factory is built with
  `LoggerFactory.Create` and no configuration source, so the console-formatter variables the AppHost
  injects stay inert. Each session over the demo's `127.0.0.1` certificate logs one host-name line,
  `Opc.Ua.CertificateValidationCore` "The domain 'opc.tcp://127.0.0.1:<port>/' is not listed in the server
  certificate", at `Error`, not `Warning` (measured 2026-10-20, Task 8 ⚠️); the `Warning`+ factory prints it,
  and nothing suppresses or filters it. The line is expected.
- **[decision] The loop never ends on its own.** `DemoScadaServer.Completion` completes successfully only
  when `DisposeAsync` cancels it, and a fault in the loop faults it; the loop catches its own token's cancellation and returns, so a graceful stop
  leaves `Completion` completed successfully. `FollowAsync` checks it on every tick: a fault is rethrown, and a completion
  without a fault is an `InvalidOperationException` ("the OPC UA publishing loop ended"), so a writer never
  keeps appending beside a dead server. `DisposeAsync` attempts the loop cancellation, `StopAsync` and the
  stack's disposal each, and rethrows only a real fault, aggregated when more than one failed.
- **[decision] Ports.** `--opc-port` is a whole number 1-65535 and required for a follow run, with no
  default. Port 0 is refused at the command line, because the stack would bind 4840 instead of a free
  port; `StartAsync` carries no guard of its own, since only the command line and the tests call it. A busy
  port is a `SeederException` that names the port and says a running Simple-Scada holds 49875 by default.
- **[decision] The stand uses port 55875.** The operator's machine runs Simple-Scada on 49875; a stand
  writer there would refuse to start whenever the SCADA runs, stopping the archive tail with it. The stand
  already moves Postgres to 55432 for the same reason. The AppHost passes `--opc-port 55875`. This
  machine's dynamic port range is 1024-65535 (`netsh int ipv4 show dynamicport tcp`, 2026-10-20), so no
  port is clear of an outbound socket by range; 55875 carries the same small exposure 55432 does, and a
  collision fails the writer at once with the busy-port line rather than serving nothing quietly.
- **[decision] Tests pick a free port.** A `TcpListener` on `127.0.0.1` port 0 reads a free port and
  closes; the test helper starts the server there and retries with a fresh port, up to three attempts, when
  the start fails with `SeederException` for a busy port, which covers another process taking the port in
  between. The busy-port test holds its port with a `TcpListener` on `127.0.0.1`, the address the stack
  binds.
- **[decision] `--archive-lag`.** A finite number of seconds, 0 to `FollowOptions.MaximumSeconds`, default
  0 (today's behaviour). `FollowEdge` owns the archive's edge: each tick it returns the window
  `(edge, now - lag]` and moves the edge to its end, or returns nothing and keeps the edge while the
  lagged clock has not passed it, so the edge never moves back (moving it back would regenerate rows the
  archive holds and fail the `COPY` on the primary key). An empty archive seeds the edge at the lagged
  clock. `StaleArchiveGuard` reads the lagged clock, so a restart compares the archive's edge with the
  instant the archive should have reached, and the lag needs no bound below its five minutes. A restart
  that drops a lag the previous run used meets an archive behind the unlagged clock; the guard's refusal
  therefore names passing the previous run's `--archive-lag` beside `converge`, which drops the database.
  `CoarseFlush` runs over the same window, because its opening-row insert reads the raw rows just written.
  The OPC server is not lagged.
- **[decision] A graceful stop writes the held-back span.** On Ctrl+C the run appends and thins the
  window `FollowEdge.Advance(LocalNow())` returns once before it exits, as the SCADA flushes every buffer on a
  graceful project stop; a killed writer loses that span, as a killed SCADA does (roadmap line 54).
- **[decision] The AppHost passes no `--archive-lag` in this slice.** The stand's live edge still comes
  from the archive poll until `opc-live-source`, and a lagged archive would put the stand's chart minutes
  behind the clock with nothing yet to fill the span. `opc-live-source` adds the lag to the AppHost.
- **[decision] `ConfigFiles/opc/opc.yaml` arrives with `opc-live-source`, not here.** The loader that
  reads it arrives there, and `DeliveredConfigurationTests` names its loaders one by one; a tracked file
  created here would ship in any release cut between the slices with nothing to read or guard it, and a
  guard written here would parse the viewer's file a second way. The tracked file carries 49875, the
  plant's port; the stand's copy must carry 55875, which `opc-live-source` writes the way `converge`
  writes 55432 into `connection/connection.yaml`.

## Technical Details

### Command line

| Option | Run | Value | Default |
| --- | --- | --- | --- |
| `--archive-lag` | follow only | seconds, finite, `0 <= x <= 86400` | `0` |
| `--opc-port` | follow only, required there | whole number `1..65535` | none |

A seeding run that names either one fails with `Option '<name>' belongs to a follow run: ...` and exit 1,
mirroring the existing `_seedingOnly` rejection. A follow run without `--opc-port` fails with `Option
'--opc-port' is required for a follow run: ...`, naming 49875 as the port a running Simple-Scada holds.
`converge` does not register either option, so System.CommandLine refuses them there. `FollowOptions`
becomes:

```csharp
public sealed record FollowOptions(
	string ConnectionString,
	TimeSpan Interval,
	int PenCount,
	long Seed,
	double ChangeSeconds,
	TimeSpan ArchiveLag,
	int OpcPort)
```

No default values on the record: every construction site states both.

### The server

```csharp
public sealed class DemoScadaServer : IAsyncDisposable
{
	public static Task<DemoScadaServer> StartAsync(
		FollowOptions options,
		TimeZoneInfo archiveZone,
		string certificateDirectory,
		ILoggerFactory loggerFactory);

	public string EndpointUrl { get; }   // opc.tcp://127.0.0.1:<port>, printed by the follow run
	public Task Completion { get; }      // the publishing loop; see "The loop never ends on its own"
	public ValueTask DisposeAsync();
}
```

Review phase 1 removed `VariableCount`, a pass-through of `options.PenCount` that the follow run now prints
itself, and the `CancellationToken`, which no production caller passed (Task 6 gives the start no Ctrl+C
token).

Files: `DemoScadaServer.cs` holds the server class and the publishing loop; it uses the stock
`StandardServer` unless the node-manager registration needs a subclass, and overrides no token policy. `DemoScadaNodeManager.cs` holds the `CustomNodeManager2`
subclass and its private `INodeManagerFactory`, which registers the Diagnostics URI ahead of the manager.
The namespace URI and the project ID are private constants of the node manager; the tests repeat both
literals, which pins the vendor-shaped values against an edit.

`StartAsync` order: build the `ApplicationConfiguration` (server type, application name
`SemiPlotDemoScada`, URI `urn:localhost:SemiPlotDemoScada`, base address `opc.tcp://127.0.0.1:<port>`, the
three security policies, one anonymous `UserTokenPolicy`, `MaxRegistrationInterval = 0`, directory stores
`own`, `issuer`, `trusted` and `rejected` under `certificateDirectory`), `ValidateAsync`,
`CheckApplicationInstanceCertificatesAsync(silent: true, ...)`, `AddNodeManager`, `StartAsync`, start the
loop. A `ServiceResultException` with `BadNoCommunication` becomes the `SeederException`. Every failure
from the stack's start up to the node manager's lookup disposes the stack before it propagates.

Publishing loop, each wake:

1. `now = converter.ToArchiveLocal(DateTime.UtcNow)`
2. `change = LatestChangeTicks(now)`; if `change` is not newer than the last served instant, skip to 4
3. write `ChangesAt(options, change)` into the variables under the manager's `Lock`: value, `Timestamp =
   converter.ToUtc(row.Timestamp)`, then `ClearChangeMasks`
4. wait until `change + interval`, recomputed from the clock on every wake

### The follow run

```csharp
public sealed class FollowEdge(DateTime edge)
{
	// The window (edge, end], moving the edge to end; null, edge kept, when end is not past the edge.
	public (DateTime After, DateTime To)? Advance(DateTime end);
}
```

The archive's clock is `LocalNow() - options.ArchiveLag`. `FollowAsync` order: `using var` the console
logger factory, so its queue flushes after the server is gone; `StaleArchiveGuard.CheckAsync(connection,
archive clock)`; `new FollowEdge(newest ?? archive clock)`; start the server over
`TimeZoneInfo.Local`, the fixed certificate directory and that factory; report the plan,
including `archive lag  <s> s` and `opc  <EndpointUrl>, <PenCount> variables`; then the tick loop.
Each tick: the `Completion` check; `Advance(archive clock)`; on a window, append and flush over it as
today. After Ctrl+C: `Advance(LocalNow())`, the held-back span, appended and flushed without the token,
then the server and the logger factory are disposed in that order, and `stopped` prints between the two.
The server is disposed explicitly rather than by `await using`: when the tick loop throws, the run disposes
the server and rethrows the loop's exception, and a dispose fault beside it is only logged through the
factory, so the archive failure is the one the operator reads.

### Project and package changes

| File | Change |
| --- | --- |
| `SemiPlot/Directory.Packages.props` | add `OPCFoundation.NetStandard.Opc.Ua.Client` and `.Server` at 2.0.1 and `Microsoft.Extensions.Logging.Console` at 10.0.12; the four pins at `:19-20`, `:22-23` to 10.0.12; `Diagnostics.Testing` stays 10.6.0 |
| `SemiPlot.Tools.ArchiveSeeder.csproj` | `PackageReference` `OPCFoundation.NetStandard.Opc.Ua.Server` and `Microsoft.Extensions.Logging.Console`; `ProjectReference` `SemiPlot.DataSource.Postgres` |
| `SemiPlot.Tests.Integration.csproj` | `PackageReference` `OPCFoundation.NetStandard.Opc.Ua.Client` |

The Server package brings `Opc.Ua.Configuration` (`ApplicationInstance`) transitively. Test files that use
the client alias `Opc.Ua.Client.Subscription` and `Opc.Ua.Client.MonitoredItem`, which collide with the
server's types of the same names once both assemblies are referenced (measured).

## What Goes Where

- **Implementation Steps**: code, tests and docs inside this repository.
- **Post-Completion**: the stand smoke check and the hand-offs to later slices.

## Implementation Steps

### Task 1: Pin the 2.0.1 package line

**Files:**
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`

- [x] add both OPC packages at 2.0.1 and the seeder's and the integration project's references, leave the
      pins at 10.0.8, run `dotnet restore SemiPlot.slnx` and confirm the `NU1605` downgrade errors in both
      test projects (the guard fires at HEAD)
- [x] raise the four `Microsoft.Extensions.*` pins to 10.0.12; leave `Diagnostics.Testing` at 10.6.0
- [x] add `Microsoft.Extensions.Logging.Console` 10.0.12 centrally and to the seeder, and the seeder's
      `ProjectReference` to `SemiPlot.DataSource.Postgres`
- [x] `dotnet build SemiPlot.slnx`: 0 errors, no warning beyond the AppHost's two `NU1902`
- [x] run tests: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before
      task 2 (no new test: the build and the unchanged suite are this task's evidence)

### Task 2: The follow-only options

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowOptions.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SeederCommandTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/LiveTailGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SharedLatticeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/CoarseFlushTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/FollowRestartTests.cs`

- [x] add `ArchiveLag` and `OpcPort` to `FollowOptions`
- [x] add `--archive-lag` (a seconds parser that accepts 0, sharing `ParseSeconds`' NaN and infinity rule)
      and `--opc-port` (`ParseWholeNumber`, 1 to 65535) to the root command, both follow-only, `--opc-port`
      required in `Interpret`'s follow branch
- [x] add the mirror of `_seedingOnly`: a seeding run naming either option fails naming it
- [x] update the four test construction sites with `TimeSpan.Zero` and a port literal (the port is never
      bound by these tests)
- [x] tests: a follow run with `--opc-port 55875` carries lag 0 and that port; a follow run accepts
      `--archive-lag 120`; `--archive-lag 0` is accepted
- [x] tests: a follow run without `--opc-port` is refused naming the option; `--archive-lag -1`, `NaN`,
      `86401`, `x` and `--opc-port 0`, `65536`, `8.5` are refused naming the value; a seeding run with
      either option is refused naming the option; a `converge` run with its required set
      (`SeederCommandTests.cs:226-227`) plus `--opc-port 1` is refused naming `--opc-port`
- [x] update the follow cases of `SeederCommandTests.cs:65-109` that run without `--opc-port` to pass it
- [x] run tests - must pass before task 3

### Task 3: The lattice point the server reads

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/LiveTailGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SharedLatticeTests.cs`

- [x] add `internal static long LatestChangeTicks(long atTicks, long intervalTicks)` beside `FirstIndex`:
      the newest change instant at or before `atTicks`, never below index 1
- [x] add `LiveTailGenerator.ChangesAt(FollowOptions options, DateTime instant)`: one change row per
      selected pen at `LatestChangeTicks`, through `Generate(options, change.AddTicks(-1), change)`
- [x] tests: `ChangesAt` at an exact lattice instant returns that instant; between instants returns the
      earlier one; one row per pen in `SelectPens` order; no anchor row at intervals above and below the
      100 ms poll interval (1 s and 0.05 s)
- [x] `SharedLatticeTests`: a third fact - over one hour of a 0.5 s lattice, `ChangesAt` at every lattice
      instant equals the follow window's change row at that timestamp, value and pen; confirm it fails
      against a `ChangesAt` that reads index + 1, then restore
- [x] run tests - must pass before task 4

### Task 4: Start and stop the demo server

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/DemoScadaServer.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/DemoScadaNodeManager.cs` (the factory and an empty address
  space; Task 5 fills it)
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/DemoScadaCollection.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/DemoScadaHost.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/OpcTestClient.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/DemoScadaServerLifecycleTests.cs`

- [x] implement `DemoScadaServer.StartAsync` and `DisposeAsync` as in Technical Details: configuration,
      certificate directory, the three policies, the stock anonymous token policy, the namespace order, the
      busy-port mapping, the loop start, `Completion`
- [x] test helpers: `DemoScadaCollection` (a named collection with no collection fixture); `DemoScadaHost.StartAsync`
      picks a free port, retries a busy one up to three times, and owns a temporary certificate directory and
      a `FakeLogCollector`; `OpcTestClient` opens a `None` + Anonymous session with the anonymous token policy
      the `None` endpoint advertises and no client certificate, reads the endpoints, browses, and subscribes with publishing interval 100 ms,
      sampling interval 100 ms and queue size 1, collecting notifications into a channel
- [x] `DemoScadaServerLifecycleTests`: three endpoints, `None`, `Sign` and `SignAndEncrypt`, each with one
      anonymous token whose policy URI is `http://opcfoundation.org/UA/SecurityPolicy#None`; a session opens
      over `None`; the namespace URI sits at index
      3; a port held by a `TcpListener` on `127.0.0.1` fails `StartAsync` with a `SeederException` naming the
      port; a server stopped with a session attached starts again on the same port; a second start in the
      same certificate directory leaves one certificate in `own/certs`; `Completion` is still running
      before `DisposeAsync` and has completed successfully after it, `DisposeAsync` throwing nothing
- [x] run tests: `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~DemoScada"` -
      must pass before task 5
- ⚠️ the stock stack advertises the anonymous token on `#None` at the `None` endpoint only; the `Sign` and
      `SignAndEncrypt` endpoints carry it with an empty `SecurityPolicyUri` (measured 2026-10-20 by the test
      client: `None #None: Anonymous:1:#None | Sign #Basic256Sha256: Anonymous:2:'' | SignAndEncrypt
      #Basic256Sha256: Anonymous:2:''`), while the real server advertises `#None` at all seven. No override
      was added (run limit); the tests assert one anonymous token per endpoint and `#None` at `None` only. A
      session opens over `None` with the token policy taken from that endpoint's `UserIdentityTokens`.
- ➕ `StartAsync` takes no `archiveZone` yet and the server has no `VariableCount`: nothing reads either
      before the address space and the loop exist, and an unused parameter fails the build (`IDE0060`).
      Task 5 adds both, and `DemoScadaHost` passes the zone through.

### Task 5: The address space and the publishing loop

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/DemoScadaNodeManager.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/DemoScadaServer.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/DemoScadaServerTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/SimpleScadaNodeIds.cs` (the namespace, the folder
  numbers and the node ids both test classes build; `DemoScadaHost.Options` is the one base `FollowOptions`)
- Modify: `SemiPlot/SemiPlot.Tests.Integration/DemoScada/DemoScadaServerLifecycleTests.cs`

- [x] build the address space: `root_2`, then the group folders from `_3`, the variables, each initialised to
      `ChangesAt(options, start)` with the start instant as stamp
- [x] implement the publishing loop of Technical Details
- [x] `DemoScadaServerTests` (one server per class through `IClassFixture`, 8 pens - the stand's slice,
      ungrouped pen 5001 and two-group pen 1000 included - change 0.25 s, `Asia/Tokyo`): `Objects` lists
      exactly one `root` of the namespace, node id `5E4D0C1A0Broot_2`, ahead of the group folders numbered
      from 3; `root` organizes every pen as `5E4D0C1A0B<PenId>` with its catalogue name and
      `DataType` Double; each group folder organizes its primary-group pens under the same node ids; pen
      5001 sits under `root` only and no `Watchlist` folder exists; over 3 s every notification after each
      pen's first has the value of `ChangesAt` at `converter.ToArchiveLocal(SourceTimestamp)`, that local
      instant is a lattice instant, the stamp lies within 60 s of `DateTime.UtcNow` at receipt (a
      local-as-UTC stamp under Tokyo is off by nine hours), and each pen's stamps strictly increase; each
      pen gets at least 3 notifications; a
      monitored item on `5E4D0C1A0B99999` fails `BadNodeIdUnknown` while the others are created
- [x] `DemoScadaServerLifecycleTests`: under `Asia/Tokyo` with change 86400 s, every first notification
      carries a stamp between the instants taken before and after `StartAsync` and the value of
      `ChangesAt(options, converter.ToArchiveLocal(stamp))`
- [x] run tests: `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~DemoScada"` -
      must pass before task 6
- ➕ `SeederException` takes an optional inner exception, and the busy-port refusal carries the stack's
      `BadNoCommunication` `ServiceResultException` there; `DemoScadaHost` retries only on that inner status,
      so a certificate failure surfaces on the first attempt. `DemoScadaHost.ArchiveZone` fixes `Asia/Tokyo`
      for every test server. The start instant is read once in `StartAsync`, ahead of the stack's start.

### Task 6: Host the server in the follow run, with the lag

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowEdge.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/StaleArchiveGuard.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/StaleArchiveGuardTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/FollowEdgeTests.cs`

- [x] implement `FollowEdge` as in Technical Details
- [x] `FollowAsync`: the logger factory, the lagged guard read, the edge seed, the server start, the report
      lines after it, the tick loop over `Advance` at the archive clock, the `Completion` check per tick,
      `Advance(LocalNow())` after Ctrl+C
- [x] `StaleArchiveGuard`'s refusal names passing the previous run's `--archive-lag` beside `converge`
      (`StaleArchiveGuardTests.cs:34-36` asserts only that the message contains `converge`, which it keeps;
      add `.And.Contain("--archive-lag")` there, a container test run on the Linux leg)
- [x] tests: an end past the edge returns `(edge, end]`; an end at or behind the edge returns null, and the
      next window still starts at the old edge; a sequence of `Advance` calls over a lagged clock, some
      returning null, yields strictly increasing, non-overlapping windows whose union is `(seed, last
      end]`; a final `Advance` at the unlagged clock covers the held-back span with no overlap
- [x] run tests - must pass before task 7
- ➕ `ReportFollowPlan` now prints after the server start, so a busy port exits with its one line only;
      the two new report lines keep the 16-column label alignment (`archive lag     `, `opc             `).
      `DemoScadaServer.StartAsync` takes no Ctrl+C token: a stop pressed during the start lets it finish, then
      the run writes the held-back span and stops gracefully.

### Task 7: The stand's writer serves OPC UA

**Files:**
- Modify: `SemiPlot/SemiPlot.AppHost/AppHost.cs`

- [x] add `const string OpcPort = "55875";` beside the other argument constants and pass `--opc-port` to the
      writer
- [x] `dotnet build SemiPlot/SemiPlot.AppHost/SemiPlot.AppHost.csproj` at 0 errors (the AppHost has no
      test project; the stand check is Post-Completion)

### Task 8: Verify acceptance criteria

- [x] verify every Overview item and every `[decision]` above is in the code
- [x] run the Acceptance Evidence commands 1 and 2
- [x] run the full suite: `dotnet test SemiPlot.slnx`
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files exit 0
- [x] `git grep -n "Opc.Ua" -- SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.DataSource.Postgres`
      prints nothing (no viewer code)
- ⚠️ `EveryFirstNotificationAfterAStart_CarriesTheStartInstantAndTheLatticeValueThere` (change 86400 s under
      `Asia/Tokyo`) failed if the Tokyo-midnight lattice instant, 15:00 UTC, fell between `StartAsync` and the
      subscribe: a window of a few seconds a day, no production defect. Review phase 1 fixed it: the test
      waits past a change due within 30 s before the start and asserts that the clock is still short of the
      next change once every first notification has arrived. The stack logs the per-session
      host-name line at `Error`, not `Warning`; the `Warning+` console factory still prints it, so Task 9
      words it as an error line.

### Task 9: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/bench.md`
- Modify: `AGENTS.md`

- [x] `bench.md` Ownership: the demo writer also owns the OPC UA demo server
- [x] `bench.md` The demo writer: the run command gains `--opc-port 55875`; a subsection "The OPC UA demo
      server" states the address space, the start stamp, the publishing rule, the endpoints and the
      anonymous token policy (`#None` on the `None` endpoint, as on the real server), the certificate directory, the console warnings, the required port and why it
      has no default, the busy-port refusal, port 0 binding 4840, and the not-reproduced list;
      `--archive-lag` with the no-rewind rule, the lagged guard read, the edge seed and the graceful-stop
      write
- [x] `bench.md` The application bench: the writer's `--opc-port 55875`, why it is not 49875, that the stand
      passes no lag yet, and that the seeder now carries the console provider while the AppHost's formatter
      variables stay inert; the question table's live-edge row names the OPC port
- [x] `bench.md` The test bench: the OPC classes run in `DemoScadaCollection`, outside
      `ArchiveDatabaseCollection`, need no container, and bind a free port because port 0 binds 4840
- [x] `AGENTS.md`: the demo-writer paragraph gains one clause, "and serves every value over OPC UA on the
      port `--opc-port` names (`docs/architecture/bench.md#the-opc-ua-demo-server`)", and its
      `--follow <seconds>` mention gains `--opc-port <port>`; the Container tests paragraph names `DemoScadaCollection` beside the socket classes that
      need no runtime

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification** (on this machine, with Simple-Scada's own server possibly on 49875). Steps 4 and 5
are the only check of `FollowAsync`'s final window and the busy-port line; the unit tests cover
`FollowEdge` alone.

1. `dotnet run --project SemiPlot/SemiPlot.AppHost`; the writer resource prints
   `opc  opc.tcp://127.0.0.1:55875, 8 variables` and keeps appending.
2. `netstat -ano | findstr 55875` shows one `127.0.0.1:55875 LISTENING` line and no `0.0.0.0` line; no
   firewall prompt appears.
3. An `asyncua` browse of `opc.tcp://127.0.0.1:55875` (the scratch probe `opc_browse.py` walks `Objects`
   and prints node id, browse name and value; `uaclient`'s `uals` serves the same) lists the five group folders and `root` under
   `ns=3`, eight variables under `root` named like the sidebar's pens, values moving every 0.5 s.
4. Stop the stand; run `converge` and then the writer by hand, both with `--change-seconds 0.5`, the
   writer with `--opc-port 55875 --archive-lag 60`. After more than a minute, `SELECT max(t) FROM
   public.trends` trails the clock by 60 s plus at most one tick and one change interval. Ctrl+C prints
   `stopped`, and the same query then returns the last lattice instant before the stop.
5. With a process already listening on 55875, the writer exits 1 with one line naming the port.

**Remaining slices** of `docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md`:

- opc-live-source (Closes #116): `SemiPlot.DataSource.OpcUa` feeds the live edge; the poll is deleted. It
  also adds `ConfigFiles/opc/opc.yaml` (port 49875), writes 55875 into the stand's copy the way `converge`
  writes 55432, adds `--archive-lag` to the AppHost's writer, pins the monitored-item queue size of 1 in a
  unit test (the demo does not reproduce the empty first value), keeps the configured address over the
  advertised one in a unit test, reproduces a project restart by restarting the demo server on its port
  and a lost transport through a loopback relay, and takes the anonymous token policy from the endpoint
  it connects to (both servers advertise `#None` on the `None` endpoint, measured 2026-10-20). Its restart
  test sees the start stamp only with a change interval longer than the server's start and the subscribe
  together (`bench.md#the-opc-ua-demo-server`).
- opc-settings: an OPC block after the DB block in the settings window.
- live-hold-to-edge: a grey pending gap and a render-only hold of the last value to the edge.

**Executed by exec:**

- branch: opc-demo-server

## Verify it yourself

Each check names its command. A check that needs no container runs anywhere; the container tests need
Docker. None of them binds or dials 49875.

1. The demo server's contract (Acceptance Evidence 1), no container:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --filter "FullyQualifiedName~DemoScada"`
   passes 16 of 16 at `7b67cbd`. The endpoints, the `#None` token on the `None` endpoint, namespace index 3,
   `root_2` ahead of the group folders from `_3`, the 8 pens, the true-UTC lattice stamps under
   `Asia/Tokyo`, the start stamp, `BadNodeIdUnknown`, the busy-port `SeederException`, the restart on the
   same port, the reused certificate (same bytes) and the faulted publishing loop each have a test there.
   On `master` the `DemoScada` classes do not exist.
2. The lattice, the command line and the follow edge (Acceptance Evidence 2):
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SharedLatticeTests|FullyQualifiedName~SeederCommandTests|FullyQualifiedName~LiveTailGeneratorTests|FullyQualifiedName~FollowEdgeTests"`
   passes. To see the third `SharedLatticeTests` fact go red, make `LiveTailGenerator.ChangesAt` read the
   next lattice index (`change + interval`): `TheServedChangeAtEveryLatticeInstantIsTheFollowWindowsChangeRow`
   fails with "differs at index 0" (measured in Task 3), and passes again once restored.
3. The whole branch (Acceptance Evidence 3, local half): `dotnet build SemiPlot.slnx` gives 0 errors and
   only the AppHost's two `NU1902`; `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
   passes 2326 of 2326 and `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`
   216 of 216 at `7b67cbd`. The CI half is unobserved: the branch has not been pushed.
4. No viewer code: `git grep -n "Opc.Ua" -- SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.DataSource.Postgres`
   prints nothing.
5. The command line, by hand, without a database: `dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- --connection "Host=127.0.0.1" --follow 1`
   exits 1 naming `--opc-port` and 49875; adding `--opc-port 0` exits 1 naming the value; a seeding run
   with `--end 2026-01-02T00:00:00 --archive-lag 5` exits 1 naming `--archive-lag` (all three checked at
   `7b67cbd` with `dotnet run --no-build`). On `master` the first command passes the parser and goes on to
   connect.
6. The first-notification flake fixed in review has no reliable manual repro: it failed only when the
   Tokyo-midnight change (15:00 UTC) landed between `StartAsync` and the subscribe. The guard is
   `WaitUntilNoChangeIsNearAsync` in `DemoScada/DemoScadaServerLifecycleTests.cs` (commit `83442f0`); run
   `--filter "FullyQualifiedName~EveryFirstNotificationAfterAStart"` within 30 s before 15:00 UTC and the
   test waits past the change instead of failing.
7. The stand itself, the server under the live writer, the archive lag and the busy-port line: the
   Post-Completion manual steps 1-5 above.
