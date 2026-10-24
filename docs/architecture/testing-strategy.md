# Testing strategy — what each test owns, and who owns each piece of the bench

This document answers two questions that keep getting confused: what kind of test a given file is,
and which party owns each piece of infrastructure the tests stand on. The bench itself — the seeder,
the container fixture, the template-and-clone lifecycle — is described in `bench.md`; this document
says what the tests built on it are *for*.

## The category of a test is decided by boundaries, not by tools

"It starts a container" names where a dependency comes from. It does not name what kind of test it
is. The three categories below are distinguished by how many boundaries the test crosses to reach
something this repository does not build, and by whose wiring is under test.

| | Unit | Integration | End-to-end |
| --- | --- | --- | --- |
| Foreign boundaries crossed | none | exactly one | several |
| Wiring under test | one component's own | one seam's translation | the production composition |
| What a failure names | the defective function | the seam that drifted | nothing in particular |
| How many there should be | many | a moderate number | few, and thin |

The names are conventional labels for three operational questions that always have crisp answers.
When a test is hard to categorise, answer these instead and the label stops mattering:

1. **What must exist on the machine for this test to run?** `PostgresContainerFixture` answers it
   for the container suite: a runtime, and nothing else.
2. **What can make it fail other than a defect in this repository's code?**
3. **What does a failure name?**

## Unit tests

A unit test crosses no boundary it does not own. Everything it touches is deterministic, in-process
and built from the commit under test. Its value is diagnosis.

| Area | Files |
| --- | --- |
| Navigation, scale, cursor geometry | `SemiPlot.Tests.Unit/Core/Trends/TrendNavigationModelTests.cs`, `PenScaleModelTests.cs`, `MinimapGeometryTests.cs`, `Chart/CursorReadoutModelTests.cs`, `DeltaCursorModelTests.cs` |
| The seeder's generation rules | `SemiPlot.Tests.Unit/LayerThinnerTests.cs`, `RawLayerGeneratorTests.cs`, `BreakGenerationTests.cs`, `PartitionScriptTests.cs` |
| The demo writer's own rules and the command line | `SemiPlot.Tests.Unit/LiveTailGeneratorTests.cs`, `SharedLatticeTests.cs`, `SeederCommandTests.cs`, `FollowEdgeTests.cs` (the archive edge's windows under `--archive-lag`). Its coarse flush is not here: it reads the finer layer back from the server and thins it with `LayerThinner.Thin`, so `Integration/CoarseFlushTests.cs` carries it, gated |
| The error types' shape and extent arithmetic | `SemiPlot.Tests.Unit/Errors/DataErrorTests.cs`, `Data/ArchiveExtentTests.cs` |
| The provider's statement text and its binder | `SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs` |
| The bucketed fold: one column per bucket, the gap column, the DST drop | `SemiPlot.Tests.Unit/Postgres/BucketedRowFoldTests.cs`, `Postgres/RealArchiveGapTests.cs` |
| The fresh tail's bound, and the catalogue read's own command timeout | `SemiPlot.Tests.Unit/Postgres/FreshTailBoundTests.cs`, `Postgres/ArchiveStatementTextTests.cs` |
| The vendor's observed row shape | `SemiPlot.Tests.Unit/Fixtures/RealArchiveFixtureTests.cs` over `Fixtures/real-archive-rows.csv` |
| The shipped configuration set | `SemiPlot.Tests.Unit/DeliveredConfigurationTests.cs` over `ConfigFiles/**`, linked into the output directory by `SemiPlot.Tests.Unit.csproj` |
| The live theme's pipeline, its watcher and `App`'s wiring of both | `SemiPlot.Tests.Unit/UI/Settings/AppSectionWatcherTests.cs`, `UI/Startup/AppConfigurationTests.cs`; the wall-clock exception below |
| A pen's buffer and its derived state, and the break ahead of a pen's first live sample | `SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`, `EnvelopeLineTests.cs` (`AppendAfterBreak`, the return of `ReplaceColumns`) |
| The realtime append-or-fold rule per layer, and the live break | `SemiPlot.Tests.Unit/UI/Chart/ChartRealtimeApplierTests.cs` |
| The OPC UA client's pure pieces: the endpoint choice, the project folder, the value mapping, the loss and `Bad` breaks, the subscription plan with its queue size of 1, the client configuration, the session states, the retry waits and the unserved pens | `SemiPlot.Tests.Unit/OpcUa/EndpointChoiceTests.cs`, `ProjectFolderTests.cs`, `LiveValueTests.cs`, `DeliveredStampsTests.cs`, `SubscriptionPlanTests.cs`, `ClientApplicationConfigurationTests.cs`, `LiveSessionStatesTests.cs`, `ConnectBackoffTests.cs`, `UnservedPensTests.cs` |
| One OPC UA subscription with no session: the routing of a `Bad`, a non-numeric and an unstamped notification, which the stack raises from `MonitoredItem.SaveValueInCache`; a closed subscription that emits nothing; a loss queued ahead of a pen-set change that still breaks the kept pens | `SemiPlot.Tests.Unit/OpcUa/PenSubscriptionTests.cs`, `PenSubscriptionSetTests.cs` |
| The `opc/` section, the OPC UA composition and the file `converge` writes | `SemiPlot.Tests.Unit/OpcUa/OpcUaConnectionLoaderTests.cs`, `OpcUa/OpcUaCompositionTests.cs` (resolving and disposing the source with no server opens no socket), `OpcFileWriterTests.cs` |

Shared helpers sit beside the tests that use them, one home each: `ResultText` (a failed result read in an
assertion), `TemporaryDirectory`, `FixedClock`, `HeadlessInput` (the four gestures), `ViewProbe` (find, measure,
show and close a window), `Postgres/FoldTestData` and `UI/Chart/ChartTestBuilder`, with `ChartViewTestBuilder`
for the realised view.

The rows for the vendor's row shape and the shipped configuration set are the ones that mislead. A test
reading a committed CSV, or the tracked YAML the
installation ships, is still a unit test: the file is data, versioned by git, and cannot change
underneath the test. Touching a file is not crossing a boundary.

A unit test must not open a socket, read the wall clock, or depend on anything the machine resolves —
`PATH`, an installed service, a display. It runs everywhere, ungated.

The wall clock has two exceptions: the live theme's watcher and `HeadlessWait`.

The live theme's watcher is the first. Its pipeline runs on a `TestScheduler` in
`UI/Settings/AppSectionWatcherTests.cs`, but what only the operating system shows needs a real
`FileSystemWatcher` over a temporary folder and the real 300 ms quiet period: that a `File.Replace` raises
an event at all, and that `App` wires the watch on both starts. Those tests,
`AReplacedFileInARealFolderYieldsTheNewTheme` and the theme tests of
`UI/Startup/AppConfigurationTests.cs`, wait for an outcome with a bound of 5 s or `HeadlessWait`'s 30 s;
one of them pumps the dispatcher for one second to let a failed reload land before the next file change.
`AnAppFolderDeletedAndRecreatedIsWatchedAgain` waits out one real `ReopenInterval`, about 5 s, bounded at
10 s, and runs its body on Windows alone: Linux raises no event when the watched folder itself is deleted
(`overview.md#the-live-theme`). A temporary folder is a file the test creates, not a machine resource, so
they stay ungated on both CI legs.

`UI/HeadlessWait.cs` is the second, and the approved wait for a hop from a pool thread back to the
dispatcher that an `[AvaloniaFact]` test cannot drive. A `ReactiveCommand` whose task finishes on the pool
reaches the view through an `AvaloniaScheduler` post, and `Dispatcher.UIThread.RunJobs()` can drain the
queue before that post lands. `HeadlessWait.Until` checks the awaited outcome every 10 ms, pumps the
dispatcher between checks, and throws after 30 s. Only `[AvaloniaFact]` and `[AvaloniaTheory]` bodies call
it. A plain `[Fact]` must not touch `Dispatcher.UIThread`, not even `CheckAccess()`: on a thread with no
dispatcher it installs that thread as the UI thread, and a pool thread that does so while the Avalonia
session resets its dispatcher kills the session's dispatch thread and hangs the suite. A plain `[Fact]`
polls through `HistoryDebouncerTestBuilder.AwaitCondition`, which references no Avalonia type. The exception
reaches no further: the clock bounds the wait and no test asserts on elapsed time, and a pipeline that takes
an `IScheduler` runs on a `TestScheduler` instead. Its users besides the theme tests are
`UI/MainWindow/MainWindowViewTests.cs`, `UI/Startup/StartupFailureWindowTests.cs`,
`UI/Settings/SettingsViewModelTests.cs`, `SettingsViewTests.cs`, `UI/Chart/TrendChartViewModelTests.cs`,
`ChartTestBuilder`, `UI/Export/CsvExportViewModelTests.cs` and `CsvExportWindowTests.cs`.

`Journeys/JourneyWait.cs` is its copy for the journeys, with the same contract (10 ms poll, dispatcher pump,
30 s bound), except that it pumps unconditionally, which is safe only because every journey is an
`[AvaloniaFact]`: Integration cannot reference the unit project, and the copy is the accepted cross-project kind,
like `FixedClock`. A journey waits for `TrendChartViewModel.ScalesRevision` to pass the value it read before
the request. `ApplyHistory` writes it through `ApplyAxisModel`, with only the redraw after it, so the render
that follows sees the applied axis. A test over a held history query waits through
`ChartTestBuilder.ReleaseAndAwait`, which counts `RedrawRequested` pulses from before the release: the redraw
is the apply's last statement and a production signal, while the columns, the query count and `ActiveLayer`
move before the apply ends. A release that also applies a catalogue or pans redraws more than once, so each
call site passes its own count.

Two pieces of state are process-global and the project runs its classes in parallel: the UI culture
(`CultureInfo.DefaultThreadCurrentUICulture` / `CurrentUICulture`) and the application's theme
variant. A class that writes either one, **and** a class that reads what either one selects (a
`Resources` accessor, a control's resolved brush), carries
`[Collection(ProcessGlobalStateCollection.Name)]` and restores the previous value in a `finally`. A
write landing between a production read and the test's own read is otherwise an intermittent failure
with no reproduction.

Statement text is pinned clause by clause, in `ArchiveStatementTextTests.cs` against the constants in
`ArchiveStatements.cs`: one assertion per guarantee whose loss nothing else catches without a
container: the bucketed history window's outer `ORDER BY id, t`, its
`GROUP BY id, segment, date_bin(@bucket, t, @from)`, its break flag, its newest non-null timestamp,
`l = @layer` in both branches, its strict seed bound and its one-day seed floor; the export statements'
`l = 0` filter and bounds; the archive days read's `l = 0` filter, its catalogue root and its ordered
`LIMIT 1` probe. Four binders are each pinned against their statement's parameter names:
`PostgresDataProvider.BindLocalBucketedWindow`, `PostgresDataProvider.BindArchiveDays`,
`PostgresArchiveRowReader.BindSeed` and `PostgresArchiveRowReader.BindPensRange`. Two assertions more pin
`PostgresDataProvider.BucketFor`: the bucket is the window divided by the column target, and it floors
at one millisecond. `data-integration.md` names the constants and quotes
no SQL, so there is no second copy to drift.

`TrendCoordinator.BatchWindow` is `internal` so `TrendCoordinatorTests` advance a `TestScheduler` by the
production window instead of repeating the 100 ms literal; the constant is the one test seam the class widens.

### What a realised-view test owns

A `*ViewTests` class asserts what only a realised view shows: a binding reaching a control, a gesture
routed to a command, layout and size, focus and keyboard routing, and any outcome the code-behind itself
decides (`PenEditorWindow`'s drain and `_textEdit` slot, `TrendChartView`'s axis repaint) and any outcome only
a realised control can trigger (the view model's `_isReorderingRows` guard against the grid's echo). An
outcome the view model decides is asserted in the view-model test; the view test drives
one representative case of the binding.

### A pass-through layer

A member whose body is `return inner(args)` keeps one test proving the forward; the inner layer owns every
case. A forward that adds a layer-owned argument (`ChartNavigationController` passes its own `_liveEdge`; a
loader names its own section) keeps one test for that argument.

## Integration tests

An integration test crosses exactly one boundary to a real implementation of something this
repository does not build, to verify the translation across that seam. A fake cannot do this job: it
would encode our own assumption on both sides. The value is contract verification.

There are five families — the same category with different foreign parties.

**Against a real PostgreSQL**, in `SemiPlot.Tests.Integration/`: every class carrying
`[Collection(ArchiveDatabaseCollection.Name)]` outside `Journeys/`, for example `PostgresHistoryReadTests`,
`PostgresCatalogReadTests`,
`CoarseFlushTests`, `ProviderSessionTests`, `ExplainPlanTests`, `ArchiveRowReaderTests`,
`PenCatalogueEditorTests`, `MarkerStoreTests`, and the seeder's `ConvergeTests` and `TagCatalogWriterTests`.
`ArchiveRowReaderTests` writes its own rows through `RealtimeArchive` (the archive day and the append
path). Seams
guarded: statement text, type mapping, the demo writer's coarse
flush (`CoarseFlushTests` pins period bounds, which periods close, the NULL skip, `ON CONFLICT` and the
opening-row statement; the thinning itself is `LayerThinner.Thin`'s, tested in Unit),
the naive-local-to-UTC conversion and, for the export, its absence (`ArchiveRowReaderTests` pins the
wall-clock timestamps across an autumn hour, the tie order by `penIds` and the slice reads), partition pruning, the session startup option (`ProviderSessionTests`:
a pooled session answers `SHOW jit` with `off`, where a plain session on the same database answers `on`),
and the grant chain: reads and the editor's writes run as `semiplot`, so a privilege that never reached the
role fails here instead of at commissioning. The container is the delivery mechanism for a real server,
nothing more.

`ExplainPlanTests` explains the history window at `Raw` only. A coarse read over a long window takes
most day partitions through a bitmap or a sequential scan (`data-integration.md#operation-to-statement`),
so no plan shape at `Minute` or `Hour` holds either way. The seed look-back clause is one text at every
layer: `ArchiveStatementTextTests` pins it, and
`PostgresHistoryReadTests.AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks` runs it.

**Against a socket, with no server**: `CatalogueReadUnreachableTests` (the catalogue read over a refused
socket at `127.0.0.1:1` answers `Unreachable` naming the archive), `ConvergeReadinessTests` (a loopback
listener) and `StartupProbeOpcSectionTests` (a valid `opc/` section passes the start on to the archive
read, which fails on the refused socket, and a loopback listener on the `opc/` port sees no connection:
the start opens no OPC UA socket). They cross the machine boundary and need no container, so they carry
no collection and take no `PostgresContainerFixture`; a missing container runtime does not fail them.
`UnreachableArchive.Settings` gives them the unreachable address.

**Against the OPC Foundation stack**: two groups of classes over the seeder's OPC UA demo server, started
in process. `DemoScadaServerLifecycleTests` and `DemoScadaServerTests`, under
`SemiPlot.Tests.Integration/DemoScada/`, read it through a 2.0.1 client over loopback. Seams guarded:
the endpoints and the anonymous token policy, the namespace at index 3, the address space, the served
values and stamps under `Asia/Tokyo`, the start stamp, the busy-port mapping and the server's lifetime
(`bench.md#the-opc-ua-demo-server`). `OpcUaSessionTests`, `OpcUaLiveSourceTests` and
`OpcUaLiveSourceRestartTests`, under `SemiPlot.Tests.Integration/OpcUa/`, read it through the production
`OpcUaLiveSource` (`data-integration.md#the-opc-ua-live-source`). Seams guarded: the session states
(`Connecting`, `Connected`, the `Unreachable` fault after the grace or a loss, `Connected` again), the
lattice values at true UTC stamps with the server in `Asia/Tokyo`, one `PensUnserved` fault for a set of
unknown pens, no break on a pen-set change, one break per pen at its newest stamp plus 1 ms after a
server restart in place and after a cut transport, a listener that is no OPC UA server, the 10 s
discovery bound against a peer that accepts and never answers, and the bounded close, also under a
`SynchronizationContext` that drops every post. Every class runs in
`DemoScadaCollection`, which carries no fixture and only keeps two servers from starting side by side;
they take no container. Each server starts through `DemoScadaHost` on a free port, never 4840 or 49875.
The live source's helpers sit beside its tests: `LiveSourceHost` builds a source over a `DemoScadaHost`
endpoint and records its states, pen faults and subscriptions; `SampleRecorder` records one
subscription's batches and every completion or fault of its stream; `LoopbackRelay` stands between the
source and the server and cuts the transport with the session alive. `LiveSourceHost` takes the
production 30 s grace, so a slow first connect under a full run writes no `Unreachable` ahead of
`Connected`; only `NoServer_TakesUnreachableAfterTheGrace_ThenConnectedOnceAServerStartsOnThePort`,
`AListenerThatAcceptsAndCloses_KeepsTheSourceInDiscoveryWithNoSetupFault` and
`AListenerThatAcceptsAndNeverAnswers_IsDialledAgainAfterTheDiscoveryBound` pass `ShortGrace` (1 s). A
test that binds a port a source already dials (a server restarted on its port, a listener after a
dispose) goes through `DemoScadaHost.StartOnPortOnceFreeAsync` or `ListenOnPortOnceFreeAsync`, which wait
while another socket still holds the port; a fresh listener comes from `DemoScadaHost.ListenOnFreePort`,
which binds before it answers. The tests run on the wall clock: they assert values, stamps, orderings
and floors on counts, bound every wait, and assert no latency. A dispose bound is the source's own bound
plus room for the pool under a full run, and the synchronous `Dispose`, which returns at its 3 s bound
whatever the close does, is judged by the stream completing after it, not by its duration.

The demo server always answers with a usable endpoint and one constant project folder, and it exposes no
subscription count, so four paths of the source have no test against it, and the session they need has no
seam that opens no socket: a project-ID change that deletes and recreates every subscription (only
`ProjectFolderTests.Next_...` pins the decision), a `CreateSubscriptionAsync` that fails or passes its
10 s bound and is tried again 10 s later, the delete of a subscription closed while the session was down
at the next `Connected`, and a `Closed` the stack raises on its own, which ends the pass and starts
discovery again. Each is read in `data-integration.md#the-opc-ua-live-source`.

**Against a real Avalonia** — `SemiPlot.Tests.Unit/UI/`, under `[AvaloniaFact]` and `[AvaloniaTheory]`,
for example the classes that realise a view: `ChartPointerInputTests`, `MinimapPointerInputTests`,
`MinimapViewTests`, `TrendChartViewTests`, `ChartMarkerViewTests`, `AxisScalePanelViewTests`,
`TimeWindowPanelViewTests`, `CsvExportWindowTests`, `MainWindowViewTests`, `TrendLegendViewTests`,
`PenEditorViewTests`, `ThemeTests` and `StartupFailureWindowTests`. Seams guarded: the dispatcher, layout, hit-testing,
pointer capture and event routing. Real framework, synthetic data. These are what catch a rendering-stack version bump.
The headless platform draws no pixels, so `Minimap/MinimapBand.Render` has no test: `MinimapViewTests`
pins the figures it receives, and the minimap's smoke walk on the demo stand covers what it paints.

**Against a real rasterizer** — `SemiPlot.Tests.Unit/UI/Chart/ChartGapRenderTests.cs`, under `[AvaloniaFact]`:
it renders through SkiaSharp and asserts on pixels that a `NaN` column breaks the
line. `LogAxisRenderTests.cs`, under plain `[Fact]` and `[Theory]` over a bare `Plot`, renders a log axis
through the same rasterizer and asserts on pixels that the 1e-4 plateau
of a 1e-6..1e-2 axis draws at the data area's vertical middle and that a plateau at `0`, or under the
axis minimum, draws 2 px above the data area's bottom row. `EnvelopeLineTests.cs`, under plain `[Fact]`,
uses the same rasterizer twice. As a render thread, not for its pixels: in
`Render_WhileTheUiThreadRewritesTheColumns_DoesNotThrow` a background task renders frames while the test thread
rewrites the same pen's columns, and the pin is that the render task throws nothing and reaches its frame
budget, the run bounded by that budget and a cancellation token. `RenderWhileMutating.RunAsync`
(`UI/Chart/RenderWhileMutating.cs`) holds that loop, and `TrendChartRenderThreadTests` runs it under
`[AvaloniaFact]` while the chart applies a catalogue and switches an axis between linear and log10.
`EnvelopeLineTests` reads pixels in
`Render_ProjectsThroughLog10OnlyUnderALogTickGenerator`: one flat line lands inside a -6..-2 axis only
while the axis carries a `LogTickGenerator`. `StrokeProbe` (`UI/Chart/StrokeProbe.cs`) is the only
pixel probe. It holds the channel-dominance band, the ink measure `InkOf` (a channel's lead over the other
two, summed down one column, which grows with the line's width) and the two ink-ratio bounds, `ThinInkRatio`
for two 1 px lines and `ThickInkRatio` for a 2 px line over a 1 px one. `ChartGapRenderTests`,
`LogAxisRenderTests`, `EnvelopeLineTests`, `TrendChartCatalogueTests`, `TrendChartViewModelTests`,
`ChartActivePenRenderTests`, `ChartPointerInputTests`, `ChartTestBuilder` and `RenderedRise` all read it, so
no test class carries a threshold of its own.

An integration test must not cross a second foreign boundary in the same assertion, and must not
exercise the production composition root. The moment it does either, a failure stops naming a seam.

## End-to-end tests

An end-to-end test crosses several boundaries through the production composition, with the assertion
at the far end. Its value is an existence proof that parts which each pass their seam tests actually
connect. Its cost is that a failure names nothing, so they stay few and thin — the seams carry the
coverage, and the journeys only prove the chain is closed.

`SemiPlot.Tests.Integration/Journeys/` holds them: two tests in two classes, one over a container-backed
archive and one over the OPC UA demo server, each closing a chain whose halves are already proved apart:

| Test | Chain it closes | The seam tests it joins |
| --- | --- | --- |
| `BreakRenderArchiveJourneyTests` | a seeded break reaches the canvas: archive → `AddPostgresData` → `TrendCoordinator` → `TrendChartViewModel` → the pixels the rasteriser leaves blank | `PostgresHistoryReadTests` counts the fold's NaN anchor; `ChartGapRenderTests` measures the blank pixels a NaN column leaves |
| `OpcUaLiveEdgeJourneyTests` | a value the demo server serves over OPC UA reaches the chart: demo server -> `OpcUaLiveSource` -> `TrendCoordinator` -> `TrendChartViewModel`. Each pen's line holds the history an in-file `IDataProvider` answers, then a `NaN` column 1 ms ahead of its first live column, then live columns carrying the lattice's values, none at or before the history's last stamp; the sticky window's end reaches the newest delivered stamp. It runs in `DemoScadaCollection` and needs no container | `OpcUaLiveSourceTests` asserts the values and stamps over the source alone; `TrendPenStateTests` covers the break ahead of a first live sample; `TrendCoordinatorTests` and `TrendChartViewModelTests` cover the batch shape and the live edge above it |

The composed path adds what those seam tests cannot see: the coordinator's buffering and folding,
the chart view model's applier, the navigation controller. Each can lose a sample or replay one without
the provider noticing, and the journey is what fails when one does.

Two things in the repository are adjacent to this category without being in it.
`SemiPlot.Tests.Unit/UI/Startup/AppBuilderCompositionTests.cs` and `UI/Di/CompositionRootTests.cs` test
the production wiring with no real edges; they are composition tests. The application bench in
`bench.md` is a genuine end-to-end procedure whose runner is a person, with its evidence read from
`pg_stat_user_tables` and the log rather than from a screen.

A test that starts a container is an integration test when it interrogates one seam, and an
end-to-end test only when the container feeds the composed application. `PostgresHistoryReadTests`
builds its provider through the real `AddPostgresData` registration, but that is one layer's wiring
and the assertion sits on rows: integration.

## Frame cost

Per-frame cost is measured rather than asserted: nothing in the suite can time a real drag.
`scripts/perf/trace-shares.py` reads a dotnet-trace Speedscope export and prints the chart's frame
cost and cadence.

```powershell
dotnet-trace collect -p (Get-Process SemiPlot.UI).Id --format Speedscope --duration 00:00:45 -o drag.speedscope.json
python scripts/perf/trace-shares.py drag.speedscope.speedscope.json --skip-seconds 10
```

`--skip-seconds` drops every run that starts within that many seconds of the export's start, so a
capture that began before the warm-up ended is read past it. The export is sampled, about one stack
every 1-2 ms, and a run is one span of consecutive samples: a call shorter than the interval shows
only when a sample lands in it. Run counts of short frames are lower bounds; their milliseconds are
the figure to compare. `NumericAutomatic.GenerateTicks` showed 0.2 runs per `RenderOnce` on the
2026-10-08 drag, where ScottPlot called it about 18 times per frame.

The script prints, past the skipped head:

| Output | Read from |
| --- | --- |
| runs, total, mean and max ms of six frames | every thread |
| `RenderOnce` start-to-start spacing p50, p90, max: all frames, and the drag phase, the gaps that hold the start of a `TrendChartView.OnPointerMoved` run | render thread, UI thread |
| compositor frame interval: `ServerCompositor.RenderCore` start spacing, and the rate at its median | render thread |
| pan step to next `RenderOnce`: from the start of a `TrendChartView.OnPointerMoved` run that holds `TrendChartViewModel.OnNavigationWindowChanged` to the next `RenderOnce` start | UI thread, render thread |
| `NumericAutomatic.GenerateTicks` runs and ms per `RenderOnce` | render thread |
| `LabelStyle.Measure` ms per `RenderOnce`, by calling frame | render thread |
| `Monitor.Enter_Slowpath` runs under `TrendChartViewModel.OnNavigationWindowChanged`, count and max | UI thread |

The UI thread is the profile whose stacks hold `Win32DispatcherImpl.RunLoop`, the render thread the one
holding `WinUiCompositorConnection.RunLoop`; the script stops with a message when either is missing.
Every percentile carries its sample count, because a pan step is seen only when a sample lands in it:
19 in 10 s of the 2026-10-08 drag.

Two captures check the chart on the demo stand, Release:

- drag for 45 s: the drag-phase `RenderOnce` spacing p50 sits within 2 ms of the compositor frame
  interval, the pan step to the next `RenderOnce` p50 is under 12 ms, `NumericAutomatic.GenerateTicks`
  takes 0.12 ms or less per `RenderOnce` (below), and no `Monitor.Enter_Slowpath` run sits under
  `TrendChartViewModel.OnNavigationWindowChanged`;
- follow for 20 s with the demo writer running, no pointer in the window and no resize: `RenderOnce` runs
  at least once per writer tick, less a small margin. A plot that stopped redrawing passes every other
  check, the hover one included, so this capture is the lower bound that catches it;
- hover without a button held over a still plot, follow off, for 20 s: `RenderOnce` runs about once per
  writer tick, not once per pointer move (about 52 per second without the plot cache at 8 pens). The count
  holds only when the follow capture passes as well (`charting.md#hover-and-the-plot-cache`).

`OnNavigationWindowChanged` is the frame that carries `ApplyAxisModel`, whose cost now scales with the
pen count rather than the group count: one `PenScale` and one `SetLimitsY` per pen per window change,
50 on the bench catalogue against 5 before the axis became the pen.

A hidden axis used to cost its ticks. Measured on 2026-10-08 on `44bba75`, Release, 8 pens, Raw layer,
one Y axis drawn: ScottPlot ran `NumericAutomatic.GenerateTicks` about 18 times per frame, the eight pen
axes and the default right axis twice each, at 0.351 ms per `RenderOnce`, and `LabelStyle.Measure` took
0.720 ms per `RenderOnce`, 0.281 ms of it under `GenerateTicks`. ScottPlot regenerates every Y axis at
two lengths per frame and reads no `IsVisible` doing so, so a pen axis's generator now skips a hidden axis
and reuses its last two results (`charting.md#ticks-for-the-drawn-axis-only`). The code leaves 4 calls
per drag frame: the drawn Auto pen axis misses its cache at both lengths, because each pan step moves its
range, and the default right axis keeps an uncached `NumericAutomatic`; 4/18 of the before figure is
0.078 ms. The export is sampled, so the check reads the time with a margin for sampling noise: 0.12 ms or
less of `GenerateTicks` per `RenderOnce`, 6/18 of the before figure. One hidden axis that still generated
would add 2 calls per frame; seven would bring back the before figure. The 2026-09-17 measurement that
found hidden axes free was taken on axes whose range was never set.

dotnet-trace names the converted file `<name>.speedscope.json`, so the second argument repeats the
extension. The six frames of the first table are `RenderOnce`, `Polygon.Render` and `SKCanvas.DrawPath` on the
chart's frame path, `OnNavigationWindowChanged`, `ApplyHistory` and `QueryHistoryAsync` on its
history path. A 45 s drag capture passes when `RenderOnce` averages 10 ms or less and
`SKCanvas.DrawPath` totals 1 s or less.

## Frames in a realised view

A realised `TrendChartView` redraws on an animation frame (`charting.md#the-frame-paced-redraw`).
`AvaloniaHeadlessPlatform.ForceRenderTimerTick()` ticks the server compositor and completes the
committed batch; the frame callback runs on the `Render` dispatcher operation that follows. A test
drives one frame as `Dispatcher.UIThread.RunJobs(); AvaloniaHeadlessPlatform.ForceRenderTimerTick();
Dispatcher.UIThread.RunJobs();`, which `ChartViewTestBuilder.DriveOneFrame` holds. The first callback of
an idle view runs on a plain `RunJobs()`. After a frame that invalidated the plot, Avalonia holds the
next pulse until the batch that frame committed is processed, and the tick is what processes it.

A plain `RunJobs()` also fires the real-time 60 Hz headless render timer once 16 ms have passed, and a
callback that invalidated nothing waits on Avalonia's real-time 16 ms animation timer for its next
pulse, not on the tick. A test therefore counts frames through `TrendChartView.ServedFrameCount`, never
per `RunJobs()` call, and reads its baseline after the view settled: an attached view requests a frame
from `OnLoaded`, which runs after the first pulse.

`ServedFrameCount` is internal and nothing in production reads it: the one test-only member of the view,
an exception to the rule against them. It counts the frame callbacks that served a request, so the tests
pin coalescing and the pending mark through it, not the repaint. That the served frame invalidates the
plot has no headless observable. The test application uses headless drawing, where `AvaPlot`'s draw
operation gets no Skia lease and renders nothing, so ScottPlot's render count and `RenderFinished` never
move from the realised view; the renderer's dirty set is internal to Avalonia; and a Skia-backed headless
platform would be a second test application, which one test assembly cannot host beside the first.
`Plot.RenderInMemory` does fire `RenderFinished` headless, which the data-area width tests use.

`TrendChartView.MarkerLines` is the marker seam: the view reads it itself, and it is internal so a test reaches
each marker's `VerticalLine` by id. The alternative is to tell the plot's `VerticalLine`s apart from the two delta
lines by position and label. `TrendChartView.MarkerReadings` and its `SnapshotReadings()` are the same kind of seam
for the active pen's readings at the markers: the alternative is a pixel probe of the dots.
`TrendChartView.AheadBreakShade` and its `ShownLine` are the same kind of seam for the line the ahead-break shade
follows: internal, no production reader of `ShownLine`, and the alternative is a pixel probe of two pens' grey
spans through a realised view, whose headless drawing renders nothing.

`EnvelopeLine.SnapshotColumns()` is the chart's data seam, beside `ServedFrameCount`: internal, no production reader,
and the alternative is a pixel probe. It returns a snapshot taken under the line's lock, because the render
thread reads the list while the UI thread writes it, so a test never loops over it per append.

## The UI scheduler in a realised view

A test that realises `TrendChartView` never passes `ImmediateScheduler.Instance` as the chart's UI
scheduler. The history debouncer delivers each result through `ObserveOn(uiScheduler)`, so over
`ImmediateScheduler` the apply runs on whatever thread completed the query, a pool thread once the query
awaits anything. The apply raises `RedrawRequested`, the view requests an animation frame, and
`RequestAnimationFrame` throws off the UI thread. The hangs belong to `PenCatalogueSync`'s wait loop and
the minimap band's next-read schedule (`AGENTS.md`, Test). Two schedulers work in its place.

| Scheduler | When |
| --- | --- |
| `TestScheduler` | the test drives time itself |
| `AvaloniaScheduler.Instance` | the test needs the production seam, as `ChartPointerInputTests` does |

The same holds for a window that realises the chart indirectly: `ChartTestBuilder.CreateChart`, which the
sidebar tests build on, hands its charts a virtual UI scheduler for this reason alone. Its
`CreateViewModel` takes the immediate UI scheduler and so serves only tests that realise no view.
`MainWindowTestBuilder` composes the window through the production builder, `TrendWindow.Build`, over a
`TestScheduler`, and no window test builds a window's parts by hand.

No test starts a process. `MainWindowTestBuilder` and `TestLaunch.LauncherAt` give every window an
`InstanceLauncher` over a recording seam, its internal four-argument constructor, because the default
launcher would start the test host; `InstanceLauncherTests` pins the start info as a pure builder.

## Where the boundaries between projects fall

Two projects, peers, and the line between them is one question: does the test cross a machine boundary, a
socket or a container? A test that opens none runs in Unit.

| Project | Holds | References |
| --- | --- | --- |
| `SemiPlot.Tests.Unit` | every test that opens no socket and starts no container: the UI, the Core models, the seeder's generators, the providers' pure classes | `SemiPlot.UI`, `SemiPlot.Core`, `SemiPlot.DataSource.Postgres`, `SemiPlot.DataSource.OpcUa`, `SemiPlot.Tools.ArchiveSeeder` |
| `SemiPlot.Tests.Integration` | the container harness, the container tests and the archive journey in one xunit collection, the OPC UA classes and the OPC UA journey in `DemoScadaCollection`, and the socket tests that need no server | the same five |

Neither references the other, and both target plain `net10.0`, so both build on the Linux runner and
the target framework separates nothing. An xunit v3 test project is one executable, so the split also
keeps the container lifecycle out of the process a developer iterates the unit suite in. Core,
`SemiPlot.DataSource.Postgres`, `SemiPlot.DataSource.OpcUa` and `SemiPlot.UI` each name both projects in
`InternalsVisibleTo`.

Nothing skips. `SemiPlot.Tests.Unit` runs on any machine with the SDK, and
`SemiPlot/SemiPlot.Tests.Unit/xunit.runner.json` sets `failSkips` so a skipped test there is a
failure on both CI legs; in `SemiPlot.Tests.Integration` a missing container runtime throws out of
`PostgresContainerFixture.InitializeAsync`, and xunit fails every test of the collection with
`TestPipelineException`. Only the container collection fails as a whole: the socket classes carry no
collection, and the `DemoScadaCollection` classes take no fixture, so both run without a runtime.

CI has two jobs: `unit-windows` runs `SemiPlot.Tests.Unit` on `windows-latest`, the platform the
viewer ships on, and `linux` builds the solution once on `ubuntu-latest` and runs both projects, the
runner's Docker daemon serving the fixture. The Windows runner cannot host a Linux container and runs
`SemiPlot.Tests.Integration` nowhere but the Linux leg. That includes `CatalogueReadUnreachableTests`,
`ConvergeReadinessTests`, `StartupProbeOpcSectionTests`, `DemoScadaServerLifecycleTests`,
`DemoScadaServerTests`, `OpcUaSessionTests`, `OpcUaLiveSourceTests`, `OpcUaLiveSourceRestartTests` and
`OpcUaLiveEdgeJourneyTests`: they need no container, but they live in Integration, so they run only on the
Linux leg. The Linux leg runs in UTC, where a stamp read as local time passes unnoticed, so
`OpcUaLiveSourceTests` runs the demo server in `Asia/Tokyo` and checks every stamp against true UTC.

## Ownership

Each piece lives with the party whose change invalidates it.

| Piece | Owner | Lives in | Why this boundary |
| --- | --- | --- | --- |
| Archive schema, layers, thinning rule | Simple-Scada 2 | the vendor's product; observed in `scada-archive.md` | SemiPlot is a strict read-only consumer. The observation is documented with the consumer because the consumer depends on it, not because anyone here controls it |
| Instance provisioning: database, roles, grants, default privileges, the five configuration tables (`semiplot_tags`, `semiplot_groups`, `semiplot_pen_groups`, `semiplot_markers`, `semiplot_meta`), `public.trends` | SemiBase | `github.com/Semiteq/SemiBase` | the instance is shared by the SCADA, SemiPlot and future readers. The bench must be provisioned by the same implementation a site is, or it stops testing the grant chain. The archive table is in that list because SemiBase creates it: a second definition here would be the one exercised daily while the real one decayed |
| `semibase` artifact formats and versions | SemiBase | its release workflow and its published image | the producer owns its artifacts; SemiPlot only consumes them |
| Synthetic data model, including `LayerThinner` — this project's hypothesis about the vendor's thinning rule | SemiPlot | `SemiPlot.Tools.ArchiveSeeder` | the hypothesis couples to the consumer, not the provisioner: if the rule is refuted, the *read path* changes and SemiBase changes nothing. It must version in lock-step with the code that bets on it, which is why `RawLayerGeneratorTests` lives beside it and why `SyntheticValueWalk`, `SyntheticPenCatalog` and `SyntheticPen` are the seeder's own: the tests pin the generator's shape and later slices develop against its output, so a generator shared with anything evolving for its own reasons would break them |
| Test harness | SemiPlot tests | `SemiPlot.Tests.Integration/` | the harness serves this repository's tests and nothing else; no other party can decide its shape |
| Developer environment | SemiPlot | `dotnet test` and the bench recipe in `bench.md` | it composes the others' artifacts and defines none of them |

Two rules follow from the first two rows and are not negotiable, restating
`data-integration.md`: SemiPlot never writes to vendor objects, and every additive object is prefixed
`semiplot_`.

A third belongs here rather than there: **containers are the bench and CI, never the product.** On a
site PostgreSQL is a Windows service installed by `winget`, `semibase` is an executable run once at
commissioning, and SemiPlot is a desktop application on the operator's PC — no Docker takes part in
any of it. Where this document and the roadmap reach for an image, they are pinning a dependency of
the tests, not describing what is delivered.

## What is pinned, and by what

Three mechanisms pin three kinds of thing, and Docker is one of them rather than the definition of
correctness.

| Kind | Mechanism |
| --- | --- |
| Code in this repository — the seeder, the provider, the models | git: a project reference means the code under test *is* the commit |
| Third-party libraries | NuGet versions in `Directory.Packages.props`, the SDK in `global.json` |
| Dependencies with an independent release cycle — PostgreSQL, `semibase` | a container image |

The delivered configuration set is pinned by the production loaders rather than by a copy of their
rules: `DeliveredConfigurationTests` runs `AppSettingsLoader`, `PostgresConnectionLoader` and
`OpcUaConnectionLoader` over the tracked `ConfigFiles/` tree, so a delivered file that stopped parsing
fails the build.

This repository's own generator output is pinned by properties rather than by a digest:
`RawLayerGeneratorTests` asserts that the same options generate the same rows twice, that every row
sits on the absolute lattice, that a plan with breaks is the continuous lattice with the break
windows cut out, and that every change row follows its anchor by one poll interval. A deliberate
waveform change moves none of them; a change that breaks one is the defect the suite exists for.

A dependency resolved from the machine — an executable found on `PATH`, a service that happens to be
installed — is pinned by nothing, and that is the property to avoid. It is a separate property from
process isolation, and it is the one that matters here.

The rule is that nothing the gated suite stands on may be resolved from the machine. The provisioner
is a layer of the bench image, copied out of `ghcr.io/semiteq/semibase:latest`, so it arrives with
the image and nothing searches `PATH`; `bench.md` describes how.

One exception is accepted, and it is a choice rather than an oversight.

**`latest` is a moving tag.** A delivered installation updates neither service, so the only pair
ever newly deployed is the newest provisioner with the current reader; pinning a digest here would
test a pair nobody ships. A moving tag buys that only if it moves, and rebuilding the bench image
does not move it — the Engine's builder takes the provisioner's `FROM` from the local image cache.
The fixture runs `docker pull` on the tag ahead of the build, so the pair every run exercises is the
newest one. `bench.md` holds the full statement, including how the step degrades where there is no
route to the registry.

The cost of the moving tag is that one unchanged commit can pass today and fail tomorrow. That
failure is legible rather than mysterious, in two ways. A provisioning that fails exits the
container's entrypoint, and Testcontainers' start exception carries the container's own stdout and
stderr — `error: server version 130023 is below the floor 140000` is what a base image below
SemiBase's floor produces — and that exception propagates straight out of
`PostgresContainerFixture.InitializeAsync`, so xunit reports it as the `TestPipelineException` that
fails the collection. And the provisioner a run built over is the one `docker image inspect` names for
the tag on that machine, so a failure that follows a moved tag can be tied to the digest it moved to.
