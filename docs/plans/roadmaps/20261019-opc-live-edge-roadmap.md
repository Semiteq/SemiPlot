# OPC UA live edge roadmap

**Issues:** #116 (Live data: show a value within 1 s of its change). One issue, sliced into seven pull
requests; the slice that switches the live edge to OPC UA closes it.

## Summary

The chart's live edge reads the archive, which the SCADA fills minutes to a day late, so a change on
the plant cannot reach the chart within 1 s. The live edge moves to the SCADA's built-in OPC UA
server, history stays on the archive, and the archive poll is deleted. Seven slices: a break the live
edge can carry, the archive connection state moved off the poll, a live-source port in Core, a
Simple-Scada-shaped OPC UA server for tests and the demo stand, the OPC live source itself (closes
#116), the settings block, and the two render marks at the live edge. The roadmap closes when all
seven are merged.

**Thesis:** while the OPC UA session is up, every point past a pen's newest archived row comes from
OPC UA; the archive replaces a live point only when a history read covers it, under the rule
`EnvelopeLine.ReplaceColumns` applies today.

**Verified against code on 2026-10-19 (`38de80f`) and against the live demo SCADA (2.7.6.0
`demo-64`, `test_project`) the same day. Trust rule: prefer the shapes over the numbers if they have
drifted.**

## Root cause

The archive cannot carry a live edge. Simple-Scada buffers each variable in memory and writes it in
batches of about 50 rows or every ~8 minutes; nothing configures it (`[SUP:20261009]`,
`scada-archive-observed.md#how-late-a-value-arrives`). `RealtimePoll` reads `trends` every second
and can only show what has been flushed.

| Kind of pen | Newest archived row lags the plant by | Source |
| --- | --- | --- |
| changes every second | 1 to 3 min | `[MEAS:capture-20261006]` |
| slow (by time 10 s, 1 min; combined) | ~8 min | same |
| rarely changing (stepped) | up to 2 h | same |
| constant | up to a day | same |

Measured on the demo SCADA on 2026-10-19 with a Python `asyncua` client (scratch probes, not
committed; they move into `scada-archive-observed.md` with `opc-live-source`):

| Behaviour | Observation |
| --- | --- |
| Endpoint | `opc.tcp://WORKING-PC:49875`; the server advertises its host name, not the address dialled. `None` + Anonymous offered beside Sign and SignAndEncrypt. 49875 is the port the SCADA offers by default |
| Address space | `Objects` -> folder `ns=3;s=<project ID>root_<n>` (browse name `root`) -> one variable per SCADA variable, browse name = variable name, node `ns=3;s=<project ID><trends.id>`. Namespace 3 is `http://simple-scada.com/UA`. A sibling folder holds the `Sys*` variables |
| Groups | A SCADA group is a folder beside `root` at the top of `Objects`, `ns=3;s=<project ID><name>_<n>`; a variable inside keeps the address `ns=3;s=<project ID><trends.id>` |
| Value types | `Double`, `Float`, `Int64` (a stepped integer whose DataType attribute reads `Int32`), `Byte` for a bool and for a fresh `Int64` array variable |
| Timestamps | `SourceTimestamp` is true UTC: a constant pen's stamp 11:57:36.718 UTC equals the server log's local `UA server started` at 14:57:36.718 MSK, and source-to-client latency computed against the real UTC clock has a median of 3.1 ms. A `DateTime` variable's value (`SysProjectStartTimestamp`) is local time labelled UTC; no pen reads one |
| Initial value | Delivered at once on subscribe; `SourceTimestamp` is the moment of the last change (a constant pen: the project start) |
| Rate | Every change, whatever the archive mode: `Time1h` sent 32 notifications in 30 s while it archives once an hour |
| Deadband filter | An absolute `DataChangeFilter` was accepted and apparently ignored. Not confirmed |
| Several clients | 8 concurrent sessions over all 17 variables, all `Good`, no refusal |
| Unknown node | `BadNodeIdUnknown` when the monitored item is created |
| .NET client, both `1.5.378.182` and `2.0.1` (spike of 2026-10-19) | Connects with `None` + Anonymous and no application certificate; `ApplicationConfiguration.ValidateAsync` demands store paths but creates no file or folder anywhere. Dials the address given, never the advertised `WORKING-PC`. `SourceTimestamp` arrives as UTC (`DateTimeUtc` in 2.0). An unknown node yields a monitored item with `Created = false` and `BadNodeIdUnknown`; the others work. Server quirk: with a monitored-item `QueueSize` above 1 the first notification is an empty `Good` value with `MinValue` stamps, so a static variable never gets its value; `QueueSize` 0 or 1 delivers the real one. 2.0.1's `ManagedSession` recreates a destroyed session in place after a server restart, recreates the subscriptions with fresh initial values, backs off 1-30 s, and closes in 31 ms; 1.5's `SessionReconnectHandler` builds a new session with cloned subscriptions the caller must swap in, and its classic close waits 5 s. An unvalidated configuration makes `ManagedSession.ConnectAsync` retry forever |
| Project stop, restart, server kill | The UA server lives with the project: each closes the port; no per-variable bad status precedes it. A graceful stop flushes every buffer to the database before the UA server stops; a kill flushes nothing, so the archive loses the unflushed span the OPC client already received. After a start every variable's first notification carries the UA server's start instant. The client reconnected 1-2 s after `UA server started` |

## Target end state

| Concern | Today | Target |
| --- | --- | --- |
| Live-edge source | `PostgresDataProvider.Subscribe`, `RealtimePoll` every `poll_interval_ms` | `OpcUaLiveSource` in a new `SemiPlot.DataSource.OpcUa`, behind a live-source port in Core; one session per process, one subscription per pen set |
| Live edge while OPC is down | n/a | none: the chart shows the archive history, a second status-bar indicator shows OPC down, the message panel carries a Warning, the session reconnects in the background |
| A break at the live edge | `TrendPenState.AppendRealtime(DateTime, double?)` takes a null, but `Sample.Value` is `double`, so nothing writes one; a `q = 32` row draws as a held line (`backlog.md`) | a break travels through `Sample`/`RealtimeBatch`; written on OPC session loss, on a `Bad` status, and ahead of a pen's first live sample past its last column |
| A history read over live points | `ReplaceColumns` keeps appended columns past the new history end | unchanged: a read replaces the live points the archive already holds |
| Archive connection indicator | three failed poll ticks, each bounded at 10 s | three failed catalogue reads of `PenCatalogueSync`, each bounded, connection-class faults only |
| Configuration | `connection/` holds `poll_interval_ms` | `poll_interval_ms` gone; a new `opc/` section folder with `host: 127.0.0.1` and `port: 49875` tracked; the project ID is found by browsing |
| Settings window | database block | database block, then an OPC block |
| Span between a pen's newest archived row and its first live sample | n/a | a break shaded grey, labelled as "no archive data when the window opened"; it fills only when a history read covers it |
| Constant pen at the edge | line stops at its last column | while the OPC session is up, drawn held to the live edge from the last column's value (render only, never a stored column) |
| Demo stand and integration tests | the seeder's `--follow` writer moves the archive; the poll shows it | the same writer serves its values over a Simple-Scada-shaped OPC UA address space and can hold its archive writes back by `--archive-lag` |
| Locked decision in `docs/architecture/README.md` | "No application server, no OPC UA client" | OPC UA client for the live edge only; history stays on the archive `[DEC:opc-live-edge]` |

## Why it is safe

Consumers of the touched surfaces at `38de80f` (non-test):

- `IDataProvider.Subscribe`: `TrendCoordinator.BuildRealtimeBatches` alone. Implemented by
  `PostgresDataProvider` alone; tests implement it in `UI/Bridge/FakeDataProvider.cs`.
- `IDataProvider.ConnectionFaults`: `TrendCoordinator` republishes it, `TrendWindow.Build` hands it
  to `AppStatusBarViewModel`, which is its single writer.
- `Sample`, `RealtimeBatch`, `PenRealtimeValues`: `RealtimePoll`, `TrendCoordinator`,
  `ChartRealtimeApplier`, `TrendChartViewModel`. (`ChartHistoryRequestDebouncer` uses the Rx `Sample`
  operator, not the record.)
- `poll_interval_ms`: `PostgresConnectionSettings.PollInterval`, `PostgresConnectionLoader`,
  `ConnectionFileWriter` (the `converge` verb), `SettingsViewModel`, `SettingsDialog.axaml`, three
  keys of `Resources.resx` / `Resources.ru.resx`, `ArchiveFailureMapper` (its range message),
  `ConfigFiles/connection`. An unknown key is ignored by the loader, so an installed file that still
  carries it keeps loading.
- `PenCatalogueSync` runs on `Bridge/ArchiveReadLoop`, which `MarkerSync` shares; the loop reports to
  the panel from its third failed read in a row. `QueryPensAsync` takes no token and no command
  timeout, so it inherits the connection string's 300 s backstop.
- Tests that exercise the poll: `RealtimePollTests`, `RealtimePollReadTests`,
  `RealtimeSubscriptionTests`, `RealtimeSubscriptionDisposalTests`, `RealtimeEmptyArchiveTests`,
  `LiveEdgeArchiveJourneyTests`, `ExplainPlanTests` (the poll statement), `ArchiveStatementTextTests`,
  `PostgresCompositionTests`, plus `RealtimeArchive`, `BatchCollector`, `ArmedGate`.
- The chart's own invariants stay: one `EnvelopeLine` column list per pen, DA-7 in
  `AppendAfterLastColumn`, the coarse fold, the 100 000-column cap, `ReplaceColumns`. A sticky
  advance raises `WindowChanged` with `RequiresHistoryRequery: false`, so following the edge issues
  no history read; a gesture, a resize or a pen-set change does.
- The installer (`Installer/SemiPlot.iss`) installs each tracked configuration file
  `onlyifdoesntexist`, so an upgraded site gains `opc/opc.yaml` with the tracked port.

## Guard strategy

- The compiler: removing `IDataProvider.Subscribe` and `PollInterval` breaks every consumer above
  at build, `TreatWarningsAsErrors` included.
- `FailureSeverityTests` covers only error types declared in Core and UI
  (`MapArmTable_CoversEveryErrorTypeThisRepositoryDeclares`), so the OPC error type lives in
  `SemiPlot.Core/Data/Errors`; a type in the data-source project would fall into `MapUnknown` as an
  `Error` with every test green.
- `DeliveredConfigurationTests` names its loaders one by one; `opc-live-source` adds the OPC loader
  there, or the tracked `opc/` set is unguarded.
- `AppMenuBarTests`, `TrendLegendViewTests` and the settings window tests pin the UI surfaces the
  slices extend.
- OPC tests live in `SemiPlot.Tests.Integration` outside `ArchiveDatabaseCollection`, as
  `ConvergeReadinessTests` does, against the in-process demo server bound to a free port (a 2.0.1
  server given port 0 binds 4840, measured while planning `opc-demo-server`); they run on the
  Linux leg only, in UTC, so one class pins the timestamp path under a fixed non-UTC zone.
- The 1 s acceptance is not assertable under `TestScheduler`: a debug line in `ApplyRealtimeBatch`
  logs receipt minus `SourceTimestamp`, and the figure is measured by hand on the demo SCADA. Each
  guard is a hypothesis the slice plan confirms fires at HEAD.

## Slices

### Slice live-edge-break — Status: DONE
- **Scope:** Give the realtime seam a way to carry an absence, and draw it. `Sample` (or a signal
  beside it) carries a break for a pen at a moment; `TrendCoordinator` keeps it in the batch;
  `ChartRealtimeApplier` and `TrendPenState` append a `NaN` column at that moment at every layer.
  A break is always appended, never folded: `FoldIntoLastColumn` with a `NaN` sample would turn the
  last real column into `NaN`. The coarse fold after a break opens a column past the gap, as it does
  today. `CurrentValue` stays the last real value. The production writer in this slice is the
  existing poll: a `q = 32` row becomes its value followed by a break, which closes the `backlog.md`
  item "A break that opens at the live edge draws as a held line".
- **Issue:** none (backlog item)
- **Blast radius:** mechanism: the `Sample` / `RealtimeBatch` vocabulary in Core; surface: the
  realtime path from `RealtimePoll` to `EnvelopeLine`, the docs that state "the live edge has no
  null" (`charting.md`, `trend-interaction.md`, `data-integration.md#realtime`).
- **Risk:** medium, concentrated in the fold at coarse layers and in DA-7 at the break column.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no OPC code, no change to the history path or `BucketedRowFold`.
- **Plan:** docs/plans/completed/20261019-live-edge-break.md
- **PR:** #141
- **Branch:** live-edge-break

### Slice archive-state-from-catalogue — Status: DONE
- **Scope:** Move the archive connection state off the poll. The catalogue instance of the read
  loop, not `MarkerSync`'s, becomes the source of `ArchiveConnectionState`: `Connected` on the first
  success and on the first success after a fault, `ArchiveFault.ConnectionLost` after
  `ConsecutiveFailuresBeforeFault` failed reads in a row of a connection-class fault (`Unreachable`,
  a timeout), never for `TableMissing`, `AccessDenied` or a shape fault, which keep their own
  entries. The catalogue read gets a bound of its own like the poll tick's 10 s, so a stalled server
  raises the fault in about 30 s rather than 15 minutes. One outage makes one panel entry: the loop's
  own third-failure report and the status bar's fault report become one. The first `Connected`
  arrives with the first catalogue read, so the window issues one at start rather than waiting 5 s.
  The poll stops reporting state and still feeds the live edge.
- **Issue:** none
- **Blast radius:** mechanism: who produces `ArchiveConnectionState`; surface: `IDataProvider`'s
  `ConnectionFaults` member (moves or goes), `QueryPensAsync`'s bound, `ArchiveReadLoop`,
  `TrendCoordinator`, `TrendWindow.Build`, `AppStatusBarViewModel` tests,
  `overview.md#a-failed-read`, `data-integration.md#the-connection-state-the-poll-reports`.
- **Risk:** medium: the loop is shared with `MarkerSync`, and the panel's coalescing merges only an
  identical newest entry.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no OPC code; the poll's samples and statements stay.
- **Plan:** docs/plans/completed/20261019-archive-state-from-catalogue.md
- **PR:** #142
- **Branch:** archive-state-from-catalogue

### Slice live-source-port — Status: DONE
- **Scope:** A behaviour-preserving seam. A live-source port in `SemiPlot.Core/Data` emits samples
  and breaks for a pen set; its contract states that the stream never completes and never faults,
  because `TrendCoordinator` turns any `OnError` into a permanently empty live edge. The archive poll
  becomes its first implementation, `IDataProvider.Subscribe` leaves `IDataProvider`, and
  `TrendCoordinator` and `TrendWindow.Build` take the port. Nothing the operator sees changes.
- **Issue:** none
- **Blast radius:** mechanism: one interface member moves to its own port; surface:
  `PostgresDataServiceCollectionExtensions`, `TrendCoordinator`, `FakeDataProvider` and the
  coordinator tests.
- **Risk:** low.
- **Depends on:** live-edge-break, archive-state-from-catalogue
- **Stacking base:** master
- **Scope guard:** no OPC code; the poll's statements and cadence unchanged.
- **Plan:** docs/plans/completed/20261020-live-source-port.md
- **PR:** #143
- **Branch:** live-source-port

### Slice opc-demo-server — Status: DONE
- **Scope:** A Simple-Scada-shaped OPC UA server built on `OPCFoundation.NetStandard.Opc.Ua.Server`,
  owned by `SemiPlot.Tools.ArchiveSeeder` and hosted by its demo writer: the `--follow` run serves
  every value it generates at the moment it generates it, under the address space measured above
  (namespace `http://simple-scada.com/UA`, folder `<project ID>root_<n>`, node
  `ns=3;s=<project ID><id>`, browse name = the catalogue name, `None` + Anonymous, the first
  notification after a start stamped with the start). A new `--archive-lag` holds the writer's
  archive inserts back by the given span, so the stand shows the span the archive has not reached
  yet. The server listens on the port its required `--opc-port` names; `SemiPlot.AppHost` passes
  55875, because a developer machine runs the real SCADA on 49875 (the stand's `opc/` copy carries
  55875, written by `opc-live-source` the way `converge` writes 55432), and the integration project
  starts the same server in process on a free port. This slice pins the stack's version line and raises the central
  `Microsoft.Extensions.*` pins the stack requires.
- **Issue:** none
- **Blast radius:** mechanism: a new dependency line and a server inside the seeder; surface:
  `FollowOptions`, `AppHost.cs`, `bench.md`, `Directory.Packages.props`.
- **Risk:** medium: a new package family with transitive pins; the seeder's lattice stays the one
  source of values (`SharedLatticeTests`).
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** nothing in the viewer; no OPC client code.
- **Plan:** docs/plans/completed/20261020-opc-demo-server.md
- **PR:** #144
- **Branch:** opc-demo-server

### Slice opc-live-source — Status: DONE
- **Scope:** The live edge moves to OPC UA and the archive poll is deleted. A new
  `SemiPlot.DataSource.OpcUa` project implements the live-source port with `OpcUaLiveSource`. It
  opens one session per process to `opc.tcp://<host>:<port>` from the new `opc/` section folder
  (`host` IPv4 under the `connection/` rule, `port`; tracked as `127.0.0.1` and `49875`), keeps the
  configured address instead of the host name the server advertises, finds the project ID by
  browsing `Objects` for the `root` folder, subscribes every pen of the set at a 100 ms publishing
  interval with a monitored-item queue of 1 (a larger queue makes this server send an empty first
  value), runs on `ManagedSession` over a validated configuration whose certificate store paths sit
  under `--config-dir` and stay empty for `None`, converts any numeric variant to `double`, reports a non-numeric variable or an unknown
  node once per pen, and maps `SourceTimestamp`, which is true UTC, to `Sample.TimestampUtc`. It
  writes a break for every subscribed pen when the session is lost, stamped so it never lands at or
  after the first notification of the next session; a break for a pen whose notification is `Bad`
  (an `Uncertain` value draws like any other, as history draws every quality); and a break ahead of
  a pen's first live sample when that sample lies past the pen's last column. The port carries no
  chart state and `SetPens` restarts the subscription, so a break written per subscription would draw
  a gap after every pen-set change; this slice's plan settles where the ahead-break is decided (found
  while planning `live-source-port`). It reconnects in the
  background and survives the server's restart, which destroys the old session. OPC down is a
  second status-bar indicator and a message-panel Warning through `ArchiveFailureMapper`, with a new
  public error type in `SemiPlot.Core/Data/Errors`; an `opc/` file that does not parse or carries a
  bad value is a startup failure, an unreachable server is not. Deleted: the poll implementation of
  the port, `RealtimePoll`, the `RealtimePoll` and `RealtimeBaseline` statements, `poll_interval_ms`
  with its settings field and resx keys, and the tests that only exercise them. Docs:
  `[DEC:opc-live-edge]` in `sources.md`, the README locked decision, `data-integration.md#realtime`
  and its time boundary (a fourth path past `ArchiveTimeConverter`), `RT-1` in
  `trend-feature-spec.md`, `charting.md`, `overview.md` components, `ui-text.md`, the measurements
  above in `scada-archive-observed.md`. Acceptance: on the demo SCADA a change appears on the chart
  within 1 s; nothing on the chart changes when the archive catches up; with the project stopped the
  chart works from the archive and says so.
- **Issue:** #116
- **Blast radius:** mechanism: the live-edge source and a new data-source project; surface:
  startup and configuration, `TrendWindow.Build`, status bar, message mapping and resx, the realtime
  test classes, `LiveEdgeArchiveJourneyTests`, `ConfigFiles/`, the installer's file list.
- **Risk:** high, concentrated in the stack's session and reconnect behaviour across a project
  restart and a server kill, which the spike read from source but did not drive; the plan re-checks
  it against the demo SCADA.
- **Depends on:** live-source-port, opc-demo-server
- **Stacking base:** master
- **Scope guard:** no settings-window block, no render marks; history, extent, catalogue and markers
  untouched.
- **Plan:** docs/plans/completed/20261020-opc-live-source.md
- **PR:** #145
- **Branch:** opc-live-source

### Slice opc-settings — Status: DONE
- **Scope:** The settings window gains an OPC block after the database block, editing the `opc/`
  keys under the existing rules: only keys that already exist, each in the file that owns it, only
  the keys the operator changed, validated by the production loader on a staged copy, effective at
  the next start. The startup-failure window offers the settings window under the same condition,
  extended to the third section.
- **Issue:** none
- **Blast radius:** mechanism: `SettingsSave` gains a section; surface: `SettingsDialog`,
  `SettingsViewModel`, `StartupFailureViewModel`, `overview.md#the-settings-window`, resx strings.
- **Risk:** low.
- **Depends on:** opc-live-source
- **Stacking base:** master
- **Scope guard:** no change to which keys exist or what they mean.
- **Plan:** docs/plans/completed/20261021-opc-settings.md
- **PR:** #146
- **Branch:** opc-settings

### Slice live-hold-to-edge — Status: DONE
- **Scope:** Two render-only marks at the live edge. First, the break ahead of a pen's first live
  sample is shaded grey and labelled as "no archive data when the window opened"; it fills only when
  a history read covers it, and a break for a stopped project or a `Bad` status keeps the ordinary
  look. Second, while the OPC session is up, each visible pen's last column value is drawn as a
  horizontal segment from that column to the live edge, so a constant or rarely changing pen reaches
  the edge instead of stopping at a point that can be a day old; the hold stops at a break and is off
  while the session is down. Neither mark is a stored column. The cursor and hover readouts read the
  held value inside the held span. `CurrentValue` stays the one owner of a pen's reading.
- **Issue:** none
- **Blast radius:** mechanism: `EnvelopeLine.Render` and `DistanceTo`, a kind on the break; surface:
  the readouts, `charting.md`, `ui-theme.md` (the grey is a `Palette.axaml` key), resx.
- **Risk:** medium: the render path runs on the render thread under the column lock.
- **Depends on:** opc-live-source
- **Stacking base:** master
- **Scope guard:** no stored column, no change to the live-edge clock (the edge follows samples).
- **Plan:** docs/plans/completed/20261021-live-hold-to-edge.md
- **PR:** #147
- **Branch:** live-hold-to-edge

## Close condition

Every slice not marked DROPPED has a MERGED PR. `opc-live-source` carries `Closes #116`, so the
issue closes on its merge.

## Rejected alternatives

Settled during design — do not relitigate without new facts.

- Replace live points with archive rows when the archive catches up (the issue's watermark
  proposal) — the archive is not a lossless copy of the live stream (117 live notifications against
  about 12 archive rows for `Time10s`; a by-time row sits on the archiver's tick, sometimes with the
  previous value; a deadband pen's noise is never archived), so every flush would draw a density
  seam, and the buffer would grow with the archive's lag (a day for a constant pen).
- Keep live points through a history read (tagged columns, a store of live points) — the same
  density change happens today on any re-read; keeping them needs mid-list inserts and a second
  store.
- Auto-fill the grey span when the archive catches up — needs a per-pen archive watermark, and the
  fill is the density seam above.
- Keep the archive poll as a fallback live source while OPC is down — OPC is down mostly because the
  project is stopped, when the archive stands still too; the remaining case (OPC disabled or
  misconfigured) is a setup error the panel names, and a lagged edge would still miss 1 s.
- Mirror the archive deadband with a `DataChangeFilter` — the archive settings live in the SCADA
  project, not in any table we read, and the server appeared to ignore the filter.
- A project ID configuration key — browsing finds it, and a key would break every address when the
  SCADA's project ID changes.
- A live edge driven by the machine clock — the edge keeps following samples, as today.
- OPC UA unreachable at start as a startup failure — the chart runs from the archive and says so.
- Keep the points the archive loses on a SCADA server kill — they stay on screen until a re-read;
  storing them is a store of our own.

## Open forks for the operator

None open.

Settled by the author on the spike's evidence, 2026-10-19, open to the operator's veto: the package
line is `2.0.1` (`ManagedSession` owns reconnect and in-place session recreation; the cost is about
40 transitive packages, Polly and `Microsoft.Extensions.Http.Resilience` among them, and the central
`Microsoft.Extensions.*` pins move to 10.0.12); the viewer's client writes no certificate store and
needs no server certificate trust rule for `None` + Anonymous (the demo server does keep a
certificate, under `%TEMP%\SemiPlot\OpcDemoServer`, because a 2.0.1 server cannot run without one).

Settled by the operator on 2026-10-19: the grey span carries no promise of a fill; `Uncertain` draws
as a value and `Bad` breaks; the archive fault may take about 30 s; a release may be cut between any
two slices while the version is below 1.0.0.
