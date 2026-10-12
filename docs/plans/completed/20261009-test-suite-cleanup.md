# Test suite cleanup

## Overview

The two test projects hold 39 772 lines and 1568 test methods over about 17 700 lines of production code.
Individual tests are mostly sound: expected values are literals and the documented seams
(`ServedFrameCount`, `DriveOneFrame`, `HeadlessInput`, `HeadlessWait`, `TestScheduler`, `RedStroke`) are used as
documented. The excess is structural:

- one outcome pinned at two or three layers (view test over view-model test, loader over section reader,
  view model over controller and debouncer);
- Facts that differ only by literals where a Theory belongs;
- tests that cannot fail, and tautologies whose expected value is the production expression;
- tests that diagnose a result through error wording, the wall clock, real sockets or template internals;
- production members that exist only for tests, and guards no production caller can trip, with tests reaching
  them through `null!` or impossible arguments;
- helpers, builders and doubles copied across files.

This plan removes that excess. It changes one production contract: `ConnectionFileError` carries field names
instead of English prose, and the operator's detail line comes from resources per problem kind, which fixes an
English sentence inside the Russian window on a fresh install. It writes the two rules that decide most
deletions into `docs/architecture/testing-strategy.md` and corrects six statements there that drifted from the
code.

Expected result: about 180 tests and 3.5k lines removed, about 68 Facts folded into Theories with every case
kept, about 600 lines of helpers consolidated. Every number is an estimate the tasks re-measure.

## Context (from discovery)

Audit of `53ee950`, 2026-10-09. Every `file:line` below was read at that commit; earlier tasks shift later
lines, so each task locates its targets by member name first and uses the line as a hint. Paths are relative to
`SemiPlot/` unless they start with `docs/`. `Unit/` means `SemiPlot.Tests.Unit/`, `Integration/` means
`SemiPlot.Tests.Integration/`.

Measured 2026-10-09 at `53ee950`:

| Area | Production lines | Test lines | Ratio |
| --- | --- | --- | --- |
| UI | 10 983 | 24 676 | 2.2x |
| Core | 2 356 | 3 247 | 1.4x |
| Postgres provider | 2 306 | 2 850 unit + 6 564 integration | 4.1x |
| Seeder | 2 105 | 2 162 | 1.0x |

- Test attributes (`[Fact]`, `[Theory]`, `[AvaloniaFact]`, `[AvaloniaTheory]`): Unit 1436, Integration 132.
- `ArgumentNullException.ThrowIfNull` in production: 13, all in `Core/Trends/PenHistoryEnvelope.cs:12-15`,
  `DataSource.Postgres/ArchiveTimeConverter.cs:13`, `DataSource.Postgres/PostgresDataProvider.cs:86`, `:133`,
  `DataSource.Postgres/RealtimePoll.cs:64-69`.
- `PostgresDataProvider.QueryHistoryAsync` (`:126-148`) checks its arguments three ways: `ValidateArguments`
  (`:505-518`) returns the only two bare-string `Result.Fail` calls in production (inverted window, column count
  below one); `Enum.IsDefined(layer)` throws at `:143-146`; the comment at `:142` orders the two. Production has
  no cast or parse into `AggregationLayer` (`git grep -nE "\(AggregationLayer\)|Enum\.Parse<AggregationLayer>"`
  prints nothing); the layer always comes from `LayerForWidth`. The chart's column count is
  `HistoryColumnTarget.FromDataAreaWidth`'s `Math.Clamp` to `[256, 2048]` times `MarginColumnFactor` 3
  (`ReportDataAreaWidth`, `UI/Chart/TrendChartViewModel.cs:204`, filters non-positive widths before it); the
  minimap passes the constant 250. `TrendNavigationModel` keeps the window at least 1 s wide with `To >= FirstSample`,
  so `HistoryPrefetch.Expand` cannot invert it; the minimap bounds come from `min(t)`/`max(t)`
  (`ArchiveStatements.cs:155-159`). The checks are described in `docs/architecture/data-integration.md:75-81` and
  `:381-384`.
- `HistoryApplied`: declared at `UI/Chart/TrendChartViewModel.cs:119`, raised as `_historyApplied.OnNext` at
  `:591` right after `RequestRedraw()` at `:590`; seven test references (`TrendChartCatalogueTests.cs:397`,
  `TrendChartViewModelTests.cs:1356`, `:1907`, `:2225`, `Integration/Journeys/BreakRenderArchiveJourneyTests.cs:89`,
  `LiveEdgeArchiveJourneyTests.cs:75`, `:162`). `ReleaseAndAwaitResults` (`TrendChartViewModelTests.cs:2220`,
  built on `HistoryApplied.Take(results)` at `:2225`) has 18 call sites.
- `ConnectionFileError` (`Core/Data/Errors/ConnectionFileError.cs:17-25`) carries `Reason`, English prose built in
  `DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:156-159` (Unparseable), `:221-225` (HostNotIPv4)
  and `Invalid` at `:229-241` (with its own singular/plural rule). `UI/Messages/ArchiveFailureMapper.cs:108`
  injects it into `FailureConnectionFileRejectedDetail`, its only reader; `UI/Settings/SettingsSave.cs:163`
  re-wraps it. `LogFileError.Reason` (`UI/Startup/LogFileTarget.cs:37`) carries the OS exception message and is not
  this repository's prose.
- `SeederException.Message` is the seeder's stderr line (`Tools.ArchiveSeeder/Program.cs:22-25` prints it); no
  production code reads anything else of it. Asserting its wording pins operator output, which is legitimate.
- No production code branches on message text: every exception filter dispatches on type or SQLSTATE.
- Unit tests that read the wall clock or open a socket, `git grep -nE "DateTime\.(Utc)?Now|TcpListener|NpgsqlDataSource\.Create" -- SemiPlot/SemiPlot.Tests.Unit`:
  `ConvergeTests.cs:44`, `:67`; `Postgres/PostgresCompositionTests.cs:188`, `:200`, `:215`, `:233-234`;
  `Postgres/RealtimePollTests.cs:32-156` (eight); `UI/Chart/ChartNavigationControllerTests.cs:422`, `:445`;
  `UI/Chart/HistoryDebouncerTestBuilder.cs:45-47`; `UI/Chart/TrendChartViewModelTests.cs:2194-2210`;
  `UI/Navigation/NavigationBarViewModelTests.cs:45`, `:83`; `UI/Startup/AppConfigurationTests.cs:261-263` (the
  documented theme exception, `testing-strategy.md:53-65`).
- `RealtimePoll.LastSeen` (`DataSource.Postgres/RealtimePoll.cs:84`) has no reader outside the class; tests read it
  at `Unit/Postgres/RealtimePollTests.cs:130` and in `Integration/RealtimePollReadTests.cs`,
  `RealtimeEmptyArchiveTests.cs`.
- `Integration/ConvergeTests.cs` and `Integration/RealtimeSubscriptionTests.cs` carry
  `[Collection(ArchiveDatabaseCollection.Name)]` and take `PostgresContainerFixture`.
- `.editorconfig:35` raises `category-Style` to warning and the build treats warnings as errors, so an unused
  parameter (IDE0060) fails the build.

Rules this plan applies: `AGENTS.md` (Test; "Tests over provider errors assert by error type and structured
field, never on message wording"; "No ceremonial `ArgumentNullException.ThrowIfNull`"), base.md ("a flag, enum
arm, or parameter with no production writer is deleted, not tested"; "do not widen visibility or add members
solely so a test can look inside"), `testing-strategy.md:50` (a unit test opens no socket and reads no wall
clock), `:77-83` (process-global culture writers carry `[Collection(ProcessGlobalStateCollection.Name)]`).

Out of scope:

- Extracting the shared per-pen ascent-and-anchor loop of `DataSource.Postgres/HistoryRowFold.cs:30-60` and
  `BucketedRowFold.cs:29-66` (`data-integration.md:463-464` states they share the rule). While two copies exist,
  `Unit/Postgres/BucketedRowFoldTests.cs:65` and `:144` stay: they pin the second copy.
- `EXPLAIN (FORMAT JSON)` in `Integration/ExplainPlanTests.cs`: PostgreSQL is pinned to one major
  (`Integration/PostgresContainerFixture.cs:28`), and the plan text is stable within a major.
- Killing the docker process on timeout in `Integration/DockerCli.cs:57-60`: nothing observes the orphan.
- A kind enum on `SeederException`: no production code would read it.

## Development Approach

- **testing approach**: Regular. Most tasks delete or merge tests; production changes come with their tests in
  the same task.
- every deletion names the test that still covers the claim; a deletion whose covering test cannot be named
  becomes a keep, recorded with ⚠️.
- complete each task fully before moving to the next; the unit suite is green after every task, the
  integration suite after every task that touches `Integration/` or the provider.
- one commit per task while executing; `ship:ship` collapses the branch into the single commit of the one PR.
  The PR title and body name the operator-visible fix of Task 3 (the connection-file detail in the operator's
  language) beside the cleanup, so release notes carry it.
- every touched `.cs` file passes `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse`.

## Testing Strategy

- **unit**: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`. `failSkips` is on, so no test
  may skip.
- **integration**: `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`; needs a
  container runtime.
- A test merged into another keeps every assertion of both, unless the assertion is listed here as redundant.
- A test folded into a Theory keeps every case as a row.

## Acceptance Evidence

Runnable from the repository root in Git Bash after the last task. Each line states what it prints at `53ee950`
and after.

```bash
# guards: four per-file counts summing to 13 today; bare-string failures: 4 lines today; nothing after
git grep -c "ThrowIfNull" -- SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.DataSource.Postgres SemiPlot/SemiPlot.Tools.ArchiveSeeder
git grep -nE 'Result\.Fail[^(]*\(\$?"' -- SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.UI SemiPlot/SemiPlot.DataSource.Postgres SemiPlot/SemiPlot.Tools.ArchiveSeeder SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs SemiPlot/SemiPlot.Tests.Unit/UI/Chart/HistoryDebouncerTestBuilder.cs

# test-only members gone: nothing after
git grep -nE "HistoryApplied|ChangesPenSet|HasBothCursors|ValidateArguments|\bBindWindow\(|\bBindBucketedWindow\(" -- SemiPlot
git grep -n "TimeSpan? batchWindow" -- SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs

# error wording: 15 lines today; after, only the operator-output and leak pins listed in Technical Details
git grep -nE "WithMessage\(|(error|refused|rejected|Which)\.Message\.Should\(\)|entry => entry\.Message" -- SemiPlot/SemiPlot.Tests.Unit SemiPlot/SemiPlot.Tests.Integration

# wall clock and sockets in Unit: the list in Context today; after, only AppConfigurationTests.cs (theme
# exception) and PostgresCompositionTests.cs (two NpgsqlDataSource.Create calls that open nothing)
git grep -nE "DateTime\.(Utc)?Now|TcpListener|NpgsqlDataSource\.Create" -- SemiPlot/SemiPlot.Tests.Unit

# one chart builder: 8 files today; after, ChartTestBuilder.cs, MinimapStand.cs, TrendCoordinatorTests.cs
git grep -nE "new TrendCoordinator\(" -- SemiPlot/SemiPlot.Tests.Unit

# size: test attributes 1436 Unit and 132 Integration, 39772 lines in both today; about 1250 Unit attributes
# and about 35k lines after
git grep -cE '^\s*\[(Fact|Theory|AvaloniaFact|AvaloniaTheory)\b' -- SemiPlot/SemiPlot.Tests.Unit | awk -F: '{s+=$2} END {print s}'
git grep -cE '^\s*\[(Fact|Theory|AvaloniaFact|AvaloniaTheory)\b' -- SemiPlot/SemiPlot.Tests.Integration | awk -F: '{s+=$2} END {print s}'
git ls-files SemiPlot/SemiPlot.Tests.Unit SemiPlot/SemiPlot.Tests.Integration | grep '\.cs$' | xargs cat | wc -l

dotnet build SemiPlot.slnx
dotnet test SemiPlot.slnx
```

A size figure far from the expected one means a task skipped its list or deleted more than it named.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues or blockers with ⚠️ prefix
- update the plan if the work deviates from its scope

## Solution Overview

Order: rules first, so later deletions cite a rule; the contract change and the removal of unreachable checks
next, because they rewrite tests that later tasks would otherwise dedupe twice; then the removal of test-only
surface and nondeterminism; then the per-area dedup; helper consolidation last, over the smaller suite.

Two rules go into `testing-strategy.md`:

- **What a realised-view test owns.** A `*ViewTests` class asserts what only a realised view shows: a binding
  reaching a control, a gesture routed to a command, layout and size, focus and keyboard routing, and any outcome
  the code-behind itself decides (`PenEditorWindow`'s drain and `_textEdit` slot, the `_isReorderingRows` guard,
  `TrendChartView`'s axis repaint). An outcome the view model decides is asserted in the view-model test; the
  view test drives one representative case of the binding.
- **A pass-through layer.** A member whose body is `return inner(args)` keeps one test proving the forward; the
  inner layer owns every case. A forward that adds a layer-owned argument (`ChartNavigationController` passes its
  own `_liveEdge`; a loader names its own section) keeps one test for that argument.

The provider's argument checks go, with their tests: no production caller can pass an undefined layer, an
inverted window or a column count below one, and AGENTS.md forbids guards on APIs only this repository calls.
`MinMaxDecimator.Decimate` keeps its own `ArgumentException` on a bad target (`MinMaxDecimatorTests.cs:300`).

`ConnectionFileError` replaces `Reason` with `IReadOnlyList<string> Fields`; `ArchiveFailureMapper` renders one
resourced detail per `ConnectionFileProblem`.

Two test seams are settled: `TrendChartViewModel.HistoryApplied` is removed (tests wait on the outcome they
assert); `EnvelopeLine.Columns` stays as a documented seam beside `ServedFrameCount` and returns a snapshot taken
under `_renderStateLock`, which the field comment at `UI/Chart/EnvelopeLine.cs:22-23` requires for every access.

## Technical Details

- `ConnectionFileError(string path, ConnectionFileProblem kind, IReadOnlyList<string> fields)`. The base `Error`
  message stays English and names path, kind and fields for the log. New resource keys, en and ru:
  `FailureConnectionFileUnparseableDetail` (`{0}` path), `FailureConnectionFileMissingFieldDetail` and
  `FailureConnectionFileOutOfRangeDetail` (`{0}` path, `{1}` comma-joined fields),
  `FailureConnectionFileHostNotIPv4Detail` (`{0}` path). `FailureConnectionFileRejectedDetail` is deleted. The
  mapper's detail switch ends in `_ => throw new ArgumentOutOfRangeException(...)`, the form at
  `ArchiveFailureMapper.cs:100`.
- Production members deleted: `ValidateArguments` and the `Enum.IsDefined` throw with the comment at `:142`
  (`PostgresDataProvider.cs`), `PenListDelta.ChangesPenSet` (`Core/Trends/PenListDelta.cs:15`),
  `DeltaCursorModel.HasBothCursors` (`Core/Trends/DeltaCursorModel.cs:12`), the `TimeSpan? batchWindow` parameter
  and the `_batchWindow` field of `TrendCoordinator` (`UI/Bridge/TrendCoordinator.cs:19`, `:39`, `:44`, `:113`),
  the `Edge.Right` branch and the `edge` parameter of `ChartAxisRegion.HorizontalBand`
  (`UI/Chart/ChartAxisRegion.cs:57-73`), the throw branch of `HistoryColumnTarget`
  (`UI/Chart/HistoryColumnTarget.cs:12-17`), `ArchiveExtent.ToString` (`Core/Data/ArchiveExtent.cs:17`),
  `PostgresDataProvider.BindWindow` (`:345`) and `BindBucketedWindow` (`:361`), `TrendChartViewModel.HistoryApplied`
  and its subject, the 13 `ThrowIfNull` lines. `RealtimePoll.LastSeen` becomes private.
- `TrendCoordinator.BatchWindow` (`internal static readonly TimeSpan`, 100 ms, the value production runs) replaces
  the parameter; tests advance by it.
- Wording assertions that stay, because the text is operator output or a leak pin:
  `Integration/StaleArchiveGuardTests.cs:36` (`"converge"`, `bench.md:98`), `Integration/ArchiveWriterTransactionTests.cs:90`
  (seeder stderr), `Unit/ConvergeTests.cs:25`, `:38` (seeder stderr), `Unit/RawLayerGeneratorTests.cs:283` (tells the
  two `options` rules apart; both throw `ArgumentOutOfRangeException(nameof(options))` at `RawLayerGenerator.cs:165`
  and `BreakPlan.cs:48`), `Unit/Postgres/PostgresConnectionLoaderTests.cs:332` (`NotContain` the host: a leak pin).
  `SeederCommandTests` stderr fragments name an option or token; System.CommandLine exposes errors only as text.

## What Goes Where

- **Implementation Steps**: every code, test and document change in this repository.
- **Post-Completion**: the fold-loop extraction, recorded as a follow-up.

## Implementation Steps

### Task 1: Write the testing rules and correct doc drift

**Files:**
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `AGENTS.md`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartNavigationControllerTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ClonedArchiveTest.cs`

- [x] add sections "What a realised-view test owns" and "A pass-through layer" to `testing-strategy.md` (after
      "Unit tests"), wording from Solution Overview; add one pointer line to `AGENTS.md` Test
- [x] rewrite `testing-strategy.md:38` and `:110`: the coarse flush reads the finer layer back and thins it with
      `LayerThinner.Thin` (`Tools.ArchiveSeeder/CoarseFlush.cs:7`, `:120`); `CoarseFlushTests` pins period bounds,
      which periods close, the NULL skip, `ON CONFLICT` and the opening-row statement
- [x] rewrite `testing-strategy.md:125`: `ChartGapRenderTests` runs under `[AvaloniaFact]`
- [x] rewrite `testing-strategy.md:283-284` to "no window test builds a window's parts by hand"
- [x] delete the false clause of `Unit/UI/Chart/ChartNavigationControllerTests.cs:400-402` (`FirstSample` is public at
      `UI/Chart/ChartNavigationController.cs:33`) and the leave-nothing-behind clause of `Integration/ClonedArchiveTest.cs:29`
- [x] run `dotnet build SemiPlot.slnx`

### Task 2: Remove the provider's unreachable argument checks

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Delete or modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/HistoryArgumentGuardTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] re-read the invariants stated in Context (window width and order, `LayerForWidth`, the column clamp, the
      minimap's `min(t)`/`max(t)` bounds) at the current HEAD; if one no longer holds, stop and record it with ⚠️
      instead of deleting
- [x] delete `ValidateArguments` (`:505-518`), the `IsFailed` branch, the `Enum.IsDefined` throw (`:143-146`) and the
      ordering comment (`:142`)
- [x] delete the tests that reach them: `HistoryArgumentGuardTests.cs:42-107` (wording asserts at `:49`, `:59`, `:91`,
      `:101`, the undefined-layer test at `:80`, the ordering tests at `:88-107`); delete the class if nothing remains
- [x] `data-integration.md:75-81` (the undefined-layer throw and "a failed `Result` with a plain message", whose
      32-bit range clause is already stale) and `:381-384` ("passes its argument checks"): state the caller-side
      invariants instead; `AggregationLayerTests:57` tests `ToPointSpacing`'s own throw and stays
- [x] run the unit suite

### Task 3: ConnectionFileError carries fields, the detail is a resource

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileError.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsSave.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`, `DeliveredConfigurationTests.cs`,
  `Errors/DataErrorTests.cs`, `UI/Messages/ArchiveFailureMapperTests.cs`, `UI/Messages/FailureSeverityTests.cs`
- Modify: `docs/architecture/ui-text.md`

- [x] `git grep -n "new ConnectionFileError(\|\.Reason\b"` over both test projects before editing (2026-10-09:
      `DeliveredConfigurationTests`, `DataErrorTests`, `PostgresConnectionLoaderTests`, `ArchiveFailureMapperTests`,
      `FailureSeverityTests`; `LogFileTargetTests` reads `LogFileError.Reason`, which stays)
- [x] change `ConnectionFileError` to `(path, kind, IReadOnlyList<string> fields)`; update the summary
- [x] producers: `PostgresConnectionLoader.cs:156-159` (Unparseable, no fields), `:221-225` (HostNotIPv4, `[HostKey]`);
      `Invalid` at `:229-241` passes the field names and loses its singular/plural prose
- [x] `ArchiveFailureMapper.MapConnectionFile` (`:104-112`) picks the detail resource by `Kind` with a throwing default
      arm; add the four keys in both resx files; delete `FailureConnectionFileRejectedDetail`
- [x] `SettingsSave.cs:163` re-wraps with `file.Fields`
- [x] tests: `PostgresConnectionLoaderTests.cs:200`, `:212`, `:255`, `:268`, `:286`, `:331` assert `Kind` and `Fields`;
      `DeliveredConfigurationTests.cs:49`; `ArchiveFailureMapperTests.cs:340-343` becomes a Theory over the kinds
      asserting the exact `Resources.Format…Detail(...)`; `FailureSeverityTests.cs:76`, `:113` constructor calls
- [x] delete here, not later: `DataErrorTests.cs:21` (`ConnectionFileErrorKeepsItsDiscriminator`, constructor echo
      plus wording) and the connection-file remedy table in `FailureSeverityTests` (`:108`, duplicates
      `ArchiveFailureMapperTests.cs:332-345`)
- [x] `ResourcesTests` walks every key in both cultures; confirm it covers the four new keys; update
      `docs/architecture/ui-text.md` with the keys
- [x] run the unit suite

### Task 4: Typed fakes and no wording assertions on errors

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`, `UI/Chart/HistoryDebouncerTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHistoryRequestDebouncerTests.cs`, `UI/Chart/TrendChartViewModelTests.cs`,
  `UI/Di/TrendWindowBuildTests.cs`, `UI/MainWindow/AppStatusBarViewModelTests.cs`, `UI/MainWindow/MainWindowViewModelTests.cs`,
  `UI/Startup/StartupFailureWindowTests.cs`, `UI/Chart/AxisScalePanelViewModelTests.cs`, `UI/Bridge/PenCatalogueSyncTests.cs`,
  `UI/Bridge/TrendCoordinatorTests.cs`, `UI/Messages/UnhandledErrorObserverTests.cs`, `UI/Messages/ArchiveFailureMapperTests.cs`,
  `UI/Messages/ReportingTestDoubles.cs`, `Postgres/ArchiveExceptionMapperTests.cs`, `Errors/DataErrorTests.cs`,
  `UI/PenEditor/PenEditorViewTests.cs`, `SeederCommandTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/PostgresCatalogReadTests.cs`, `LiveCatalogueTests.cs`
- Modify: `docs/architecture/testing-strategy.md`

- [x] `FakeDataProvider.cs:252-254` (`FailHistory`) and `HistoryDebouncerTestBuilder.cs:61-64` (`Unreachable`)
      return `new ArchiveError(ArchiveFault.Unreachable, …)`, matching the fake's own comment at `FakeDataProvider.cs:45-47`
- [x] `ChartHistoryRequestDebouncerTests:149`, `TrendChartViewModelTests:1944`, `TrendWindowBuildTests:62` assert the
      error's `Kind` or compare to `ArchiveFailureMapper.Map(thatError)`
- [x] panel-detail sentinels: `TrendChartViewModelTests:2030`, `AppStatusBarViewModelTests:110-111` (let
      `ThrowingLogger` keep the instance it threw), `MainWindowViewModelTests:44`, `StartupFailureWindowTests:63`, `:234`,
      `AxisScalePanelViewModelTests:229`, `PenCatalogueSyncTests:206` compare the whole view to
      `ArchiveFailureMapper.Map(new ExceptionalError(thrown))`
- [x] `TrendChartViewModelTests:2052-2054` and `AppStatusBarViewModelTests:136` tell two doubles apart by message;
      give each double a private exception type and assert by type
- [x] `TrendCoordinatorTests:212` and `ChartHistoryRequestDebouncerTests:430`: `BeSameAs` on the thrown instance
- [x] delete redundant wording asserts: `ArchiveExceptionMapperTests:113` (Detail at `:112`) and the `Result` wrap at
      `:120-122`; `ArchiveFailureMapperTests:494` (exact match at `:492`); `UnhandledErrorObserverTests:40-41`
      (`:37-38`); `SeederCommandTests:109` `"seeding run"`
- [x] delete `DataErrorTests.cs:38` (constructor echo plus host and database wording); keep
      `EveryPublicErrorTypeIsSealedAndDerivesFromError` (`:61`); update `testing-strategy.md:39` ("Error construction")
- [x] `PenEditorViewTests.cs:43-45`, `:76-77`, `:1196`: compare the refresh label to `Resources.<key>` instead of the
      literal English and Russian copy
- [x] `PostgresCatalogReadTests:289`: compare `Detail` to the `ArchiveStatements.*Relations` constant
- [x] `LiveCatalogueTests:134`: filter the log entries by the structured `FallbackColor` state (`:207-218`) and by
      `PenId`, as `:138` does, instead of asserting the whole category's level sequence
- [x] run the unit and integration suites; the wording grep under Acceptance Evidence prints only the lines listed
      in Technical Details

### Task 5: Remove ceremonial guards and their tests

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenHistoryEnvelope.cs`, `SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs`,
  `PostgresDataProvider.cs`, `RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Trends/PenHistoryEnvelopeTests.cs`, `Postgres/ArchiveTimeConverterTests.cs`,
  `Postgres/PostgresCompositionTests.cs`, `Postgres/RealtimePollTests.cs`

- [x] delete the 13 `ThrowIfNull` lines listed in Context (`ArchiveTimeConverter` and `RealtimePoll` became primary-constructor classes, IDE0290)
- [x] delete the tests that pass `null!`: `PenHistoryEnvelopeTests.cs:94`, `:107`, `:120`, `:133`;
      `ArchiveTimeConverterTests.cs:25`; `PostgresCompositionTests.cs:184`; `RealtimePollTests.cs:151`
      (`HistoryArgumentGuardTests.cs:70` went with Task 2)
- [x] keep the `PenHistoryEnvelope` length and ascent validation (`:17-32`) and its tests
- [x] `docs/architecture/data-integration.md:76` names the `ArgumentNullException` for a null `penIds`; delete it (already gone with Task 2, nothing left to edit)
- [x] run the unit suite

### Task 6: Remove members and branches with no production reader

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenListDelta.cs`, `Core/Trends/DeltaCursorModel.cs`, `Core/Data/ArchiveExtent.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`, `UI/Chart/ChartAxisRegion.cs`, `UI/Chart/HistoryColumnTarget.cs`
- Modify: tests that read them (listed per item)

- [x] delete `PenListDelta.ChangesPenSet`; its 12 assertions in `PenListDeltaTests` and `Integration/LiveCatalogueTests`
      become `Added.Should().BeEmpty()` / `RemovedPenIds.Should().BeEmpty()` where "no add, no remove" matters
- [x] delete `DeltaCursorModel.HasBothCursors` and its asserts at `Unit/Chart/DeltaCursorModelTests.cs:112`, `:127`
- [x] delete the `batchWindow` parameter and the `_batchWindow` field; `Buffer` (`:113`) reads the new
      `TrendCoordinator.BatchWindow` (100 ms); the eight Unit files that pass a window drop the argument (among them
      `TrendChartViewModelTests.cs:2016`, `:2041`, `:2077` and `MinimapStand.cs:37`; `git grep` the constructor calls);
      the journeys pass none
- [x] change the window, run the unit suite, and rewrite only what fails or turns vacuous. The boundary to check is
      the 400 ms `_historyCapInterval` sample where history has started: at `TrendChartViewModelTests.cs:1002`,
      `:1049`, `:2141` an advance of `BatchWindow.Ticks * 3` after the debounce reaches 151 + 300 = 451 ms and
      re-admits the stamped request, which `IsAnsweredByLastSucceeded` suppresses today. `:339`, `:2025` and
      `TrendCoordinatorTests.cs:109` have no pending history request, and the literal 200 ms at `:1973` never depended
      on the batch window (`ChartTestBuilder.cs:25` ticks realtime every 10 ms)
- [x] delete the `Edge.Right` branch and the `edge` parameter of `ChartAxisRegion.HorizontalBand` (`:57-73`) with the
      `axis.Edge` argument at its call; delete `ChartAxisRegionTests.cs:36` (only `TrendChartView.axaml.cs:314` calls
      `TryCreate`, with the left `ActivePenAxis`)
- [x] delete the throw branch of `HistoryColumnTarget` (`:12-17`) and `HistoryColumnTargetTests.cs:18`
- [x] delete `ArchiveExtent.ToString` (`:17`) and `ArchiveExtentTests.cs:73-80` after `git grep` confirms no
      interpolation or log caller
- [x] run the unit and integration suites

### Task 7: Provider binders and the realtime high-water mark

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`, `RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/ArchiveStatementTextTests.cs`, `Postgres/RealtimePollTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ExplainPlanTests.cs`, `PostgresHistoryReadTests.cs`, `RealtimePollReadTests.cs`,
  `RealtimeEmptyArchiveTests.cs`
- Modify: `docs/architecture/testing-strategy.md:91-93`, `docs/architecture/data-integration.md:223`

- [x] delete the UTC wrappers `BindWindow` (`:345`) and `BindBucketedWindow` (`:361`); make the private
      `BindLocalWindow` (`:378`) and `BindLocalBucketedWindow` (`:394`), called at `:438` and `:462`, internal; point
      the three test files and both documents at them
- [x] make `RealtimePoll.LastSeen` private
- [x] `Unit/Postgres/RealtimePollTests.cs:121-131` `AFailingTickLeavesTheBaselineUnread` reads `LastSeen`: delete it
      and carry its claim into the recovery test of `Integration/RealtimePollReadTests.cs` (`:230`): fail three ticks,
      restore the table, append row A, tick and expect no samples (the baseline is taken at A), append row B, tick and
      expect exactly B; the last step replaces `:266`
- [x] `RealtimePollReadTests.cs:77`: delete (the baseline delivery it would become is `:89`'s `ContainSingle`, since
      the appended row sits before `_unsubscribedLast`)
- [x] `RealtimePollReadTests.cs:154` pins that `lastSeen` advances past a null row; delivery of the next row cannot
      show it, because a stuck poll re-reads the null row, drops it and still delivers. Build that poll with a
      `FakeLogger` and assert the next tick's Debug entry (`RealtimePoll.cs:217-224`) carries the structured
      `LastSeen` equal to the appended row's time, or `RowCount == 1`
- [x] `:85`, `:170`, `:212-223`, `RealtimeEmptyArchiveTests.cs:87`: replace the `LastSeen` reads with the delivery the
      test already sets up; `RealtimeEmptyArchiveTests.cs:60`, `:64`: delete the `LastSeen` asserts (`:69` covers the
      claim through delivery)
- [x] delete `RealtimePollReadTests.cs:200` `TheLastSeenNeverMovesBackwardsAcrossTicks` (it never builds a backwards
      case; its sequence restates `:77`, `:114`, `:154`)
- [x] run the unit and integration suites

### Task 8: Settle the chart's test seams

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`, `UI/Chart/EnvelopeLine.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`, `UI/Chart/TrendChartCatalogueTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/Journeys/BreakRenderArchiveJourneyTests.cs`, `Journeys/LiveEdgeArchiveJourneyTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Integration/Journeys/JourneyWait.cs`
- Modify: `docs/architecture/testing-strategy.md`

- [x] delete `HistoryApplied` and its subject (`TrendChartViewModel.cs:119`, raise at `:591`)
- [x] the apply's last effect decides the wait. With the raise at `:591` gone, `RequestRedraw()` at `:590` is the last
      statement of `ApplyHistory` and a production signal; mid-apply state is not: the columns are written at `:568`,
      before `_lastFetch` (`:573`), the re-request (`:582`) and `ApplyAxisModel` (`:587`); the query count rises when the
      query is issued; the apply never writes `ActiveLayer`
- [x] replace `ReleaseAndAwaitResults` (`TrendChartViewModelTests.cs:2220`) with
      `ReleaseAndAwait(Action release, int redraws)` that subscribes to `RedrawRequested` before `release()` and waits
      with `HeadlessWait.Until` for that many pulses; each of the 18 call sites (all release one successful result)
      passes its own count, because a release that also applies a catalogue (`:1466`) or pans (`:1519`) redraws more
      than once; `:958` and `:1323` are the sites a premature return would break silently
- [x] `TrendChartCatalogueTests.cs:397` and `TrendChartViewModelTests.cs:1907` wait the same way
- [x] `TrendChartViewModelTests.cs:1356`: inject the fault through a throwing `RedrawRequested` subscriber, armed
      after `RequestInitialHistory()` and before the debounce advance (inline `LoadInitialHistory` there), because
      `RedrawRequested` also fires from `OnNavigationWindowChanged` (`:502`, `:509`) during `TrackDataExtents`; the
      apply raises it at `:590` and the view subscribes in production (`TrendChartView.axaml.cs:200`); keep both
      assertions (report once, the next pan still queries)
- [x] the journeys (`BreakRenderArchiveJourneyTests.cs:89`, `LiveEdgeArchiveJourneyTests.cs:75`, `:162`) wait through
      `JourneyWait.Until` (same contract as `Unit/UI/HeadlessWait.cs`: poll every 10 ms, pump the dispatcher, throw
      after 30 s) for `ScalesRevision` to pass its value read before the request: it is written at `:587`, with only
      the redraw after it, so the render that follows sees the applied axis. Integration cannot reference the unit
      project, and the copy is the accepted cross-project kind, like `FixedClock`
- [x] `EnvelopeLine.Columns` returns a copy taken under `_renderStateLock`
- [x] document `Columns` in `testing-strategy.md` beside `ServedFrameCount` (`:257-264`) as the chart's data seam
      (internal, no production reader, the alternative is a pixel probe), and `JourneyWait` beside `HeadlessWait`
- [x] run the unit and integration suites

### Task 9: Socket-opening tests move to Integration

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Integration/RealtimePollTests.cs`, `ConvergeReadinessTests.cs`,
  `RealtimeSubscriptionDisposalTests.cs`, `UnreachableArchive.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Unit/Postgres/RealtimePollTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`, `SemiPlot.Tests.Unit/ConvergeTests.cs`,
  `Postgres/ArchiveStatementTextTests.cs`
- Modify: `docs/architecture/testing-strategy.md`, `AGENTS.md`

- [x] the new classes carry no `[Collection]` and take no `PostgresContainerFixture`, so a missing container runtime
      does not fail them (Integration has no assembly fixture, only `ArchiveDatabaseCollection`); they carry
      `[Trait("Category", "Integration")]`. The realtime failure ladder then runs only on the `linux` CI job
- [x] `UnreachableArchive.Settings(TimeSpan pollInterval)` gives the moved tests the settings
      `Unit/Postgres/ConnectionSettingsFactory` gives them today (`127.0.0.1:1`, adjustable poll interval;
      `ArchiveProviderFactory.Build` fixes the poll at 1 s and `:228` needs 10 ms); the copy is the accepted
      cross-project kind
- [x] `Integration/RealtimePollTests.cs` receives the unit class's ticking tests (every tick connects to
      `127.0.0.1:1`); within the move merge `:29` + `:41` and `:51` + `:69` + `:88` (same ticks, assertions split),
      delete `:106` `NoExceptionEscapesAFailingTick` (every other test awaits the same tick unguarded) and the
      restating assert at `:148`
- [x] keep the `CommandTimeout` Theory `ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop`
      (`RealtimePollTests.cs:135-149`) in Unit: it builds an `NpgsqlCommand` and opens no socket; move it to
      `ArchiveStatementTextTests`
- [x] `ConvergeReadinessTests` receives `ConvergeTests.cs:41` `AServerThatAcceptsAndClosesTheSocketIsWaitedFor`
      (real `TcpListener`, 2 s timer; a regression pin from `5012b6f`)
- [x] `RealtimeSubscriptionDisposalTests` receives `PostgresCompositionTests.cs:228`
      `SubscribingAndDisposingAtOnceDeliversNothingAndStopsThePoll`, the only test of that class that opens a
      connection
- [x] `PostgresCompositionTests.cs:200`, `:215` stay in Unit (`NpgsqlDataSource.Create` opens nothing): build their
      provider through `BuildProvider()` + `GetRequiredService<IDataProvider>()`, then delete `NewProvider`, which has
      no caller left (an unused private member fails the build, IDE0051)
- [x] restate the project split: `testing-strategy.md:290-297` and `AGENTS.md` (Test table, "Container tests":
      "Every test in `SemiPlot.Tests.Integration` needs a container runtime and nothing else") change the axis from
      "needs a container" to "crosses a machine boundary: a socket or a container", and say that only the container
      collection fails as a whole on a missing runtime; `testing-strategy.md:41` moves the realtime row to the
      Integration list
- [x] run both suites; the socket part of the wall-clock grep prints only the two `PostgresCompositionTests` lines

### Task 10: Deterministic time and threads in unit tests

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartNavigationControllerTests.cs`, `UI/Navigation/NavigationBarViewModelTests.cs`,
  `UI/Chart/TrendChartViewModelTests.cs`, `UI/Chart/HistoryDebouncerTestBuilder.cs`, `UI/PenEditor/PenEditorViewTests.cs`,
  `UI/PenEditor/PenGroupsViewTests.cs`, `UI/Chart/AxisScalePanelViewModelTests.cs`, `UI/Chart/AxisScalePanelViewTests.cs`,
  culture try/finally sites

- [x] `ChartNavigationControllerTests.cs:419` (reads `DateTime.UtcNow` at `:422`; adds only `IsSticky` over `:404`):
      delete; `:404` asserts `controller.FirstSample` instead of panning 30 days; `:436` (`BeCloseTo(DateTime.UtcNow)`
      at `:445`) pins the constructor's own clock read (`ChartNavigationController.cs:22`): inject the clock through
      a `TimeProvider` constructor parameter only if production has a second reason for it, otherwise move the
      test's claim to a fixed seed and delete the clock assert; record the decision
- [x] `NavigationBarViewModelTests.cs:45`, `:83`: replace `DateTime.UtcNow` with a fixed instant
- [x] fold the polling loops `TrendChartViewModelTests.cs:2192` `AwaitPanelEntries`, `:2206` `AwaitQueryCount` and
      `HistoryDebouncerTestBuilder.cs:43` `AwaitCondition` into `HeadlessWait.Until`
- [x] PenEditor and PenGroups view tests: replace `HeadlessWait` at `PenEditorViewTests.cs:739` and
      `PenGroupsViewTests.cs:280`, `:308`, `:311`, `:317` with `Dispatcher.UIThread.RunJobs()` where no pool thread is
      involved (the fake answers inline; the file already drains that way at `:543`, `:765`); keep a wait that fails
      without it and record it
- [x] add `[Collection(ProcessGlobalStateCollection.Name)]` to `AxisScalePanelViewModelTests` and
      `AxisScalePanelViewTests` (they write the culture through `CultureScope.cs:15-16`)
- [x] replace hand-rolled `CurrentUICulture` try/finally swaps with `CultureScope`; leave the
      `DefaultThreadCurrentUICulture` writes (`StartupSequenceTests:29`, `:82`, `ArchiveFailureMapperTests:80`), which
      `CultureScope` does not cover
- [x] run the unit suite (twice, 1796 passed each); the wall-clock grep prints only `AppConfigurationTests.cs` (Task 9 removed the two
      `PostgresCompositionTests` lines)

### Task 11: Bounded waits in Integration and the fixture's startup timer

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Integration/RealtimeSubscriptionTests.cs`, `RealtimeEmptyArchiveTests.cs`,
  `Journeys/LiveEdgeArchiveJourneyTests.cs`, `Journeys/BreakRenderArchiveJourneyTests.cs`, `StaleArchiveGuardTests.cs`,
  `PostgresContainerFixture.cs`, `ConvergeTests.cs`

- [x] every gate await (`ArmedGate`, `NextBatch`, the inline gate at `RealtimeEmptyArchiveTests.cs:100-117`) takes
      `WaitAsync(bound, TestContext.Current.CancellationToken)`, so a regression fails instead of hanging the
      executable (MSB3027); rewrite the "never a timeout" comments to state the bound
- [x] `PostgresContainerFixture.cs:75`: create the `startup` token after `DockerCli.PullProvisionerAsync` (`:79`)
      returns, so the pull and the build each get their own bound as the comment at `:36-37` states
- [x] `StaleArchiveGuardTests`: replace `DateTime.Now` with fixed instants (`StaleArchiveGuard.CheckAsync` uses only
      its `now` argument, `StaleArchiveGuard.cs:19`, `:38`), as `FollowRestartTests` does; delete `:64` (implied by `:50`)
- [x] `ConvergeTests.cs:38` (Integration): write through `TestContext.Current.TestOutputHelper`, not `Console.WriteLine`
- [x] run the integration suite

### Task 12: Chart view-model tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`, `TrendChartCatalogueTests.cs`,
  `ChartNavigationControllerTests.cs`, `ChartHistoryRequestCancellationTests.cs`, `ChartHistoryRequestDebouncerTests.cs`,
  `HistoryColumnTargetTests.cs`, `TrendChartViewTests.cs`, `ChartTestBuilder.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendPenStateTests.cs`

Abbreviations: VM = `TrendChartViewModelTests.cs`, Cat = `TrendChartCatalogueTests.cs`, Nav =
`ChartNavigationControllerTests.cs`, Canc = `ChartHistoryRequestCancellationTests.cs`, Deb =
`ChartHistoryRequestDebouncerTests.cs`. Lines are hints at `53ee950`; locate by method name.

- [x] move VM tests that only exercise `TrendPenState` (`:68`, `:542`, `:582`, `:603`, `:707`, `:729`, `:744`, `:761`,
      `:778`, `:797`, `:814`, `:833`) to plain `[Fact]`s over `new TrendPenState(pen, new EnvelopeLine())`; delete `:814`
      (`EnvelopeLineTests.cs:101` pins the trim) and `:542` (subset of `:833`); Theory `:744`+`:761` (keep the
      rejection assertion from `4325547`) and `:778`+`:797`
- [x] delete VM `:1601` (inside `:1615`), `:1714` (same assert as `:1679`), `:1671` (default echoed at `:1737`),
      `:1745` (calls `Navigation.PanBy` itself; `ChartPointerInputTests:37` covers the chain), `:458`, `:481`, `:517`,
      `:138` (the `:282` `DrawnPenId` sequence covers them; `DrawnPenId` derives from `ActivePenId` at
      `TrendChartViewModel.cs:107`), `:56` (fold its column count into `:1798`), `:529` (keep its "a gesture ahead of
      the initial request still queries" as one assert in `:1798`), `:616` (fold `LastQueriedPenIds` into `:630`)
- [x] merge VM `:1567` into `:1629`; `:100` with `:1114`; `:1854` into `:1837`; `:1276` with `:1308`; Theory
      `:893`/`:911`/`:931` (regression pin `44bba75`, all three rows) and `:556`/`:569` over `PenLineStyle`
- [x] VM `:1163`, `:1183`: delete (`Deb:29`, `:59`, `HistoryPrefetchTests.cs:23`, VM `:1550` own the claims); `:222`,
      `:238`, `:299`, `:313`, `:253`, `:148`+`:394`: one Theory per scale command over active / named / hidden pen and
      one merged `SetAxisLimits` test; `:691`: fold "pen stays unloaded" into `:1928` and delete; `:863`: assert
      `ScalesRevision` increases (its name's claim) or delete
- [x] Cat `:115` and `:181` duplicate VM `:418` and `:445`: keep the Cat copies, Theory `:181`+`:195` over
      `EnabledOnStart`, delete VM `:418`, `:445`
- [x] Nav: delete `:35` (pure forward, `TrendNavigationModelTests` owns it) and `:220` (rows of `:390`); merge `:57` and
      `:70` (same path, controller `:139`); Theory `:21`+`:354` over the gesture asserting `RequiresHistoryRequery`;
      keep `:46` and `:82` (they pin the controller-owned `_liveEdge`, `ChartNavigationController.cs:125`, `:151-163`)
- [x] Canc `:306`: delete (inside `:238`); Deb `:247`: delete (same state as `:289`)
- [x] `HistoryColumnTargetTests.cs:26`, `:32`, `:38`, `:44`: one Theory
- [x] delete `TrendChartViewTests.cs:42` and `:65` (width-report cases); keep `TrendChartRenderThreadTests.cs:138`,
      `:156`, `:172`, which report from a pool thread as production `RenderFinished` does and cover rebinding to a
      second view model; keep `TrendChartViewTests.cs:82`, because detaching by `DataContext = null` is a different
      branch from the rebind at `:172`
- [x] `ChartTestBuilder.LoadInitialHistory` (`ChartTestBuilder.cs:68-79`) becomes `void`; drop the needless
      `async`/`await` at its call sites
- [x] run the unit suite

### Task 13: Chart view, axis panel and render tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/AxisScalePanelViewTests.cs`, `AxisScalePanelViewModelTests.cs`,
  `ChartAxisBinderTests.cs`, `ChartCursorOverlayTests.cs`, `ChartPaletteTests.cs`, `EnvelopePathTests.cs`,
  `ChartPressRouterTests.cs`, `ChartHoverReadoutTests.cs`, `LogAxisRenderTests.cs`, `ChartTickGeneratorTests.cs`,
  `LocalTimeAxisTests.cs`, `ChartViewTestBuilder.cs`, `TrendChartViewTests.cs`, `ChartPointerInputTests.cs`,
  `ChartDeltaCursorReaderTests.cs`, `ChartGapRenderTests.cs`, `SemiPlot.Tests.Unit/Chart/CursorReadoutModelTests.cs`,
  `Chart/DeltaCursorModelTests.cs`

- [x] add a realised-view test: an axis added after the first paint is painted under the current theme
      (`TrendChartView.axaml.cs:107-125`, driven from `:197`; `ui-theme.md#the-plot`); then delete
      `ChartPaletteTests.cs:52` and `:75`
- [x] `AxisScalePanelViewTests`: delete `:257`, `:280`, `:451`, `:470` (VM `:233`, `:274`, `:600`, `:625` and the size
      gate `:427` own them), `:154`, `:356`, `:490`; trim culture rows and seed asserts at `:29`, `:62`, `:81`, `:400` to
      one row per binding
- [x] `AxisScalePanelViewModelTests`: delete `:85`, `:102`, `:116` (`:131` and `PenValueFormatTests` own them; `:116`
      is a tautology), `:493` (`TrendChartViewModelTests:394`), `:473`; merge `:556` into the Theory at `:651` and
      `:233` with `:255` over `IsInverted`; `:744`, `:384`, `:403`: keep only the panel's own pen and the `closes == 1`
      check; Theory `:673-716` down to the `("abc", false/true)` rows
- [x] `ChartAxisBinderTests`: delete `:89`, `:103` (one `VisibleAxes` assert added to `:41`) and `:180`
      (`LogTickGeneratorTests:41`); replace `LabelOf` (`:161`, `:272`) with literal labels
- [x] Theories: `ChartCursorOverlayTests.cs:17-79` (visible rows, hidden rows), `EnvelopePathTests.cs:15-50`,
      `ChartPressRouterTests.cs:15-35`, `LogAxisRenderTests.cs:52`+`:65` (inside a named gate: rows only)
- [x] delete `ChartHoverReadoutTests.cs:71`, `ChartTickGeneratorTests.cs:26`, `LocalTimeAxisTests.cs:14` (expected
      value is `LocalTimeAxis.cs:12` itself), `DeltaCursorModelTests.cs:57`, `:88`, `CursorReadoutModelTests.cs:55`,
      `:70` (rows of `:103`)
- [x] `ChartPointerInputTests.cs:140-146` (`AnchorAt` mirrors the view) and `ChartDeltaCursorReaderTests.cs:32`
      (`1.5.ToString("0.###")`): literal expected values; add the missing `FormatDeltaTime` h/m/s cases
- [x] `ChartViewTestBuilder.cs:70`, `TrendChartViewTests.cs:137`: find `PlotControl` and `EmptyCatalogueMessage` by
      name; keep the three placeholder `Contain` asserts at `TrendChartViewTests.cs:190-192` unless `ResourcesTests`
      already pins those placeholders
- [x] `ChartGapRenderTests.cs:235` `ArchiveShapedBreak(bool withAnchor)`: drop the always-true parameter
- [x] run the unit suite

### Task 14: Exact-count assertions on fakes

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs` (54 hits), `UI/Bridge/PenCatalogueSyncTests.cs`
  (19), `UI/Chart/ChartHistoryRequestDebouncerTests.cs` (14), `UI/Chart/TrendChartCatalogueTests.cs` (13),
  `UI/MainWindow/PenCatalogueApplierTests.cs` (10), `UI/Chart/ChartNavigationControllerTests.cs` (10)

Counts measured 2026-10-09 with `git grep -cE '(QueryCount|Count|redraws|Calls|Changes)\.Should\(\)\.(Be|HaveCount)\('`.

- [x] classify each exact count as contract or incidental. Contract counts are documented: no double load at
      startup (`ChartNavigationController.cs:108-109`), the 3-failure ladder, live edge once (`data-integration.md:729`),
      debounce 0 then 1, one axis-model computation for a 500-pen delta (`charting.md:602`), the `PenCatalogueSync`
      interval, one `RequestRedraw` per command
- [x] keep contract counts; replace incidental ones with the outcome they stand for (`BeGreaterThan(before)`, the
      state the extra call would corrupt) or delete them where a neighbouring assert already states the outcome
- [x] run the unit suite

### Task 15: Pen editor, legend, settings and startup tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/*`, `UI/Legend/*`, `UI/Settings/*`, `UI/Startup/*`,
  `UI/MainWindow/MainWindowViewTests.cs`

- [x] `PenEditorViewTests`: delete `:134`, `:1026`, `:1056` (same handler and asserts as `:880`), `:618`, `:499`, `:242`;
      keep `:321`, `:212`, `:523`, `:829`, `:752`, `:777`, `:803`, `:162` (code-behind outcomes)
- [x] `PickSwatch` (`PenEditorViewTests.cs:1141`) sets `picker.Color` with the flyout open instead of clicking a
      template `TabItem` by index; `RowGridOf` (`:1169`) and `TrendLegendViewTests.cs:188`, `:585` find row parts by a
      `Name` added to the row template
- [x] `PenGroupsViewTests`: delete `:293`, `:351`; `PenGroupsViewModelTests`: delete `:58`
- [x] `PenFormViewModelTests`: `:405` `TheMaskPreview` compares to literal rendered text (keeps the wiring pin);
      Theories `:444`/`:457`/`:510` and `:137`/`:540`
- [x] `PenEditorViewModelTests`: delete `:345` (`EditorCallQueueTests:49`, `PenFormViewModelTests:265`)
- [x] `TrendLegendViewTests`: merge `:303` into `:345`, `:323` into `:269`, `:72`/`:86`/`:105` into one toggle test;
      delete `:385`; `TrendLegendViewModelTests`: Theory `:216`+`:233`, delete `:365`
- [x] `MainWindowViewTests.cs:238-291`, `:293`, `:314`: keep one realised drag; the resize, floor and fit rules stay
      in `TrendLegendViewModelTests:288`, `:318`, `:342`
- [x] `SettingsSaveTests`: Theory `:35`/`:48`/`:59`/`:70`; delete `:319`; reduce `:81` to the password with spaces and
      `#` (`SettingsViewModelTests:317` runs the eight keys)
- [x] `SettingsViewTests`: delete `:199`, `:108`; merge `:171` into `:224`; `SettingsViewModelTests`: merge `:217` and
      `:232`
- [x] `AppSettingsLoaderTests`: delete `:85`, `:93`, `:107`, `:117` (the loader forwards `section.Errors`,
      `AppSettingsLoader.cs:42-47`); keep `:73` `AnAbsentSection` (it asserts the section name); Theories
      `:150`/`:164`/`:178` and `:128`/`:140`
- [x] `StartupFailureWindowTests`: merge `:238` into `:93`; drop the `UnhandledErrorObserver` part of `:56-63` and the
      disposed-Subject probe at `:71-73`; move `:292` (a settings regression pin from #90/#98) to `SettingsViewTests`
- [x] `EmptyCatalogueStartupTests`: delete `:48`; trim `:33-37`; `StartupProbeTests`: Theory `:67`+`:100`
- [x] run the unit suite

### Task 16: Window, messages, minimap and composition tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/*`, `UI/Di/*`, `UI/MainWindow/*`, `UI/Bridge/*`, `UI/ThemeTests.cs`

- [x] `ArchiveFailureMapperTests`: about 20 Facts with one body shape (title key, detail arguments, remedy key)
      become three Theories (archive faults, app-settings problems, section problems); delete
      `TheRemedyDoesNotDependOnWhichTableIsAbsent`, `EveryProblemCarriesItsOwnRemedy`, `StartupArgumentsUnknown`,
      `DirectoryMissing_TheRemedyDiffersBetweenTheSections`; keep the `57014` SQLSTATE assert (`ui-text.md`); tighten
      `Detail.Contain(<argument>)` to the exact `Resources.Format…`
- [x] `FailureSeverityTests`: delete `ArchiveFaults_SplitIntoWhatRetriesAndWhatNeedsTheOperator` (restates
      `_archiveFaults`) and the section and app-settings tables (production hardcodes `Error`:
      `ConfigurationSectionFailureMapper.cs:15`, `ArchiveFailureMapper.cs:78-101`); keep the `Enum.GetValues` loop that
      gates the throw arm
- [x] `CompositionRootTests`: delete `Container_ResolvesDataProvider` (subsumed by the `BeOfType` test); merge the
      scheduler and logging resolves into one test that calls `GetRequiredService` for each, without `NotBeNull`
- [x] `Di/TrendWindowBuildTests.cs:94-123`: delete (`MainWindowViewModelTests.cs:131` composes through
      `TrendWindow.Build` too)
- [x] `MainWindowViewModelTests.cs:48-84`: delete (`SettingsViewModelTests.ADirectoryPopulatesEveryField`;
      `MainWindowViewTests.cs:83` reads the host off the realised dialog)
- [x] `AppMenuBarTests.cs:92-132`: one Theory; `:206-243` (Autoscale, InitialScale): keep one proof that each menu item
      invokes its command; the behaviour stays in `AxisScalePanelViewModelTests:384-420`
- [x] `TrendCoordinatorTests`: delete `QueryHistoryAsync_PassesThroughToProvider` (`TrendCoordinator.cs:100` is a
      one-line forward); `ThemeTests.TheApplicationVariant_FollowsTheSettings`: delete (`AppConfigurationTests` pins the
      settings-to-variant path)
- [x] `FakeDataProvider`: make `ChartHistoryQueries` private; delete `ArchiveExtentOverride` if only
      `ArchiveExtent.Empty` is ever set (`Pens = []` yields it)
- [x] run the unit suite

### Task 17: Core, Postgres and seeder unit tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/*`, `Postgres/*`, `Data/ArchiveExtentTests.cs`, `PartitionScriptTests.cs`,
  `LiveTailGeneratorTests.cs`, `BreakGenerationTests.cs`, `LayerThinnerTests.cs`, `RawLayerGeneratorTests.cs`,
  `SeederCommandTests.cs`

- [x] `PostgresConnectionLoaderTests`: delete `:91`, `:117`, `:125`, `:139`, `:149`, `:301`, `:421` and the `KindOf`
      helper (the loader forwards `section.Errors`, `PostgresConnectionLoader.cs:72-75`); keep `:104`
      `AnAbsentSection…` as the pass-through proof
- [x] `MinMaxDecimatorTests`: delete `:306` (cannot fail: `PenHistoryEnvelope` validates at `:17-32`), `:84`, `:181`,
      `:44` (entailed by `:63`); reduce `:338` (regression pin `58a0832`) to the `Decimate` call; drop
      `BeInAscendingOrder` at `:164`, `:245`, `:270`
- [x] `PenListDeltaTests`: delete the six per-member Facts `:82`, `:94`, `:108`, `:119`, `:130`, `:140` (the MemberData
      Theory at `:54` covers them)
- [x] `HistoryRowFoldTests`: delete `:214`, `:230`, `:367`, `:389`, `:148`; fold `Select(PenId).Equal([3, 9])` into
      `:162`; drop `:429`. `BucketedRowFoldTests`: delete `:107`; keep `:65`, `:144`
- [x] `AggregationLayerTests`: delete `:26`, `:62`, `:76`; `ArchiveExtentTests`: delete `:17`, `:23`, `:31`, `:49`, keep
      the two `with` tests
- [x] `ConfigurationSectionTests`: delete `:191` and its helper `:347` (`DeliveredConfigurationTests:53` runs the real
      loader over the shipped file), `:283` (`:80` and `:301` own it)
- [x] `PartitionScriptTests`: Theory `:35`/`:45`/`:55`; delete `:129` and its helper `:140`
- [x] `LiveTailGeneratorTests`: delete `:21`, `:31`, `:132`, `:141`, `:151` (`SharedLatticeTests` proves row equality
      with `RawLayerGenerator`); keep the window-bound and anchor tests
- [x] `BreakGenerationTests`: delete `:105`, `:194`, `:82`; merge `:120`, `:159`, `:253`
- [x] `LayerThinnerTests`: delete `:96`, `:108`; `RawLayerGeneratorTests`: delete `:116`, `:128`; reduce the rejection
      Theory at `:267` to its first-run row and its last-run row (the comment on the Theory names the two shapes),
      keeping the wording assert at `:283`
- [x] `ArchiveTimeConverterTests`: delete `:33`, `:72` and the `TimeZoneInfo` asserts at `:101-102`; fold the winter
      case into a `(local, utc)` Theory
- [x] `FreshTailBoundTests`: delete `:51`, `:161`; `PostgresCompositionTests`: delete `:69`, `:90`, `:98`, `:141`
      (`NotBeNull` after `GetRequiredService`), keep `:59` and `:132`; replace `:116`'s computed offset with the literal
      at `:117`
- [x] `PenScaleModelTests`: merge `:16` and `:41`, delete `:164`, trim `:188-189`; `LogAxisTests`: delete `:78` and the
      `(2, 50)` row at `:101`; `PenValueFormatTests`: delete `:47` (asserts .NET's `double.ToString`)
- [x] `ArchiveStatementTextTests`: fold the nine clause pins at `:26-71`, `:129-177` into one Theory over
      `(statement, fragment)`; `:52` stays one row with both fragments; keep the four binder Facts and the moved
      `CommandTimeout` Theory
- [x] `SeederCommandTests`: merge `:272`+`:299` and `:243`+`:256`
- [x] run the unit suite

### Task 18: Integration tests: dedup

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Integration/*`

- [x] delete `PostgresContainerFixtureTests.cs` (whole file: `SELECT 1` after an initialised fixture),
      `ArchiveDatabaseTests.cs:21`, `:36`
- [x] `PostgresHistoryReadTests`: merge `:556`, `:593`, `:609`, `:645` into one read of the tail window with a fixed clock
      (same branch at `PostgresDataProvider.cs:246-247`; `:665` covers the other); merge `:145`, `:318`, `:415` (keep
      `:415`'s premise asserts and `:145`'s pen count); simplify `:286-311` to `AssertMatchesSeededRows` plus one
      gap-count assert and drop `:463-465`; delete `:724` and its helper `:74`; drop the `ReportServerTimeAsync` call at
      `:377`
- [x] `LiveCatalogueTests`: keep `:57` (the only server pin that two reads compare equal: `array_agg(grp.name ORDER BY
      grp.name)` at `DataSource.Postgres/ArchiveStatements.cs:141` has no clause pin); merge `:70` and `:94` into one
      write-read-delta test
- [x] `RealtimeSubscriptionTests.cs:53` `DisposingASubscriptionStopsItsPoll` cannot fail (the collector is detached
      before the count is read). Rewrite: build a provider of its own with a `FakeLogger` and open one subscription
      only, so every per-tick Debug entry ("The realtime poll read…", `RealtimePoll.cs:218`, written on each
      successful non-baseline tick) belongs to it; append a row and wait for its entry; dispose; append another row;
      wait three poll intervals on the wall clock (allowed in Integration); assert no new entry. Delete `:137` (prefix
      of `:94`)
- [x] merge `RealtimeEmptyArchiveTests.cs:37` into `:50` and `RealtimePollReadTests.cs:66` into `:77` (assert "no
      samples and connected" in each) (the `RealtimePollReadTests` half had no target: `:77` went with Task 7)
- [x] `PenCatalogueEditorTests`: Theory `:276`, `:291`, `:306`, `:321`, `:337` (RowGone) and `:235`+`:263` (NameTaken);
      simplify `:110` to its editor-read half
- [x] `SeededArchiveTests`: delete `:131`, `:62`; move `:165` to `TagCatalogWriterTests` and delete `TagsAsync` (`:141`)
- [x] `CoarseFlushTests`: delete `:75`, `:153`; `PostgresCatalogReadTests`: delete `:187`
- [x] `PostgresExtentReadTests`: Theory `:44`+`:62` and `:82`+`:104`; drop the `IsEmpty` assert before
      `Be(ArchiveExtent.Empty)`
- [x] `ExplainPlanTests`: one helper for the seed-walk assertions at `:138-160` and `:206-229`; run `ANALYZE` once per
      class instead of in each test
- [x] `Journeys/LiveEdgeArchiveJourneyTests.cs:39-86`, `:133-169`: one private builder for the shared composition;
      delete the `BeAfter` tautology at `:101`
- [x] run the integration suite

### Task 19: One chart test builder

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartTestBuilder.cs`, `ChartViewTestBuilder.cs`, `ChartGapRenderTests.cs`,
  `TrendChartViewTests.cs`, `TrendChartViewModelTests.cs`, `TrendChartCatalogueTests.cs`, `HistoryDebouncerTestBuilder.cs`,
  `RedStroke.cs`, `RenderedRise.cs`, `UI/Navigation/NavigationBarTestBuilder.cs`, `UI/Bridge/TrendCoordinatorTests.cs`,
  `UI/Bridge/FakeDataProvider.cs`, `UI/Legend/TrendLegendViewModelTests.cs`, `UI/Legend/TrendLegendViewTests.cs`,
  `Chart/CursorReadoutModelTests.cs`, `Chart/DeltaCursorModelTests.cs`, `UI/Chart/ChartTickGeneratorTests.cs`,
  `UI/Chart/LogTickGeneratorTests.cs`, `UI/Chart/ChartAxisBinderTests.cs`, `UI/Chart/TrendChartRenderThreadTests.cs`,
  `UI/Chart/EnvelopeLineTests.cs`

- [x] give `ChartTestBuilder` the parameters the copies need (UI scheduler, realtime interval, returned panel) and
      replace the hand-written setups in `ChartGapRenderTests` (`CreateViewModel`), `TrendChartViewTests`
      (`CreateViewModel`), `ChartViewTestBuilder`, `NavigationBarTestBuilder`, `TrendCoordinatorTests`
      (`CreateCoordinator`) and `TrendChartViewModelTests` (`CreateReportingViewModel`, `CreateChartOn`,
      `CreateViewModelWithPanel` and the two inline constructions)
- [x] one `_from`, `_to`, deadline and realtime-interval constant (copies in `ChartTestBuilder`, `ChartViewTestBuilder`,
      `ChartGapRenderTests`, `TrendChartViewTests`, VM and Cat field blocks, `HistoryDebouncerTestBuilder`); the literal
      `TimeSpan.FromMilliseconds(10)` advances in VM and Cat read the shared realtime interval
- [x] the legend tests' `CreateChart` and `LoadInitialHistory` (`TrendLegendViewModelTests.cs:533-563`,
      `TrendLegendViewTests.cs:631-643`) use the builder; resolve their `Ticks + 2` versus the builder's `Ticks + 1` by
      reading which one the production debounce needs
- [x] `FakeDataProvider` exposes its two default pens; the 93 literal `new Pen(1, "Pen 1", ["Group A"], "#ff0000")` and
      14 second-pen literals read them
- [x] `RedStroke` becomes the only pixel probe: `ChartGapRenderTests` (`ColumnCarriesPenColor`) and `RenderedRise`
      (`Dominates`) call it, with a channel parameter if `RenderedRise` needs a non-red channel
- [x] one `Envelope` helper for `CursorReadoutModelTests` and `DeltaCursorModelTests`; one `Regenerate` helper for
      `ChartTickGeneratorTests`, `LogTickGeneratorTests`, `ChartAxisBinderTests.MajorTicks`; one render-while-mutating
      harness for `TrendChartRenderThreadTests` and `EnvelopeLineTests`
- [x] run the unit suite

### Task 20: Shared unit-test helpers

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Unit/ResultText.cs`, `SemiPlot/SemiPlot.Tests.Unit/TemporaryDirectory.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
- Modify: config, settings, startup, pen-editor, legend and messages test files listed below

- [x] `Describe<T>(Result)` (copies in `Core/Configuration/ConfigurationSectionTests`, `ConfigurationSectionWriterTests`,
      `DeliveredConfigurationTests`, `Postgres/PostgresConnectionLoaderTests`, `UI/Startup/AppSettingsLoaderTests`,
      `UI/Settings/SettingsSaveSandbox`) and `ErrorOf`/`SectionErrorOf` (`ConfigurationSectionTests`,
      `ConfigurationSectionWriterTests`, `PostgresConnectionLoaderTests`) move to `ResultText`
- [x] `TemporaryDirectory` replaces the create-and-delete fixtures (`git grep -l CreateTempSubdirectory` in `Unit/`,
      17 files on 2026-10-09), including the try/finally `Directory.Delete` blocks in `AppConfigurationTests` (seven)
      and `StartupFailureWindowTests` (five); `SettingsSaveSandbox` builds on it
- [x] `ShippedConfiguration.ConnectionBody()` replaces `ShippedConnectionBody()` in `ConfigurationSectionTests` (if
      Task 17 left it) and `ConfigurationSectionWriterTests`, and the path built in `DeliveredConfigurationTests.Shipped`
- [x] one copy each of `Named<T>` (`PenEditorWindowDriver`, `SettingsViewTests`), `LeftOf`/`TopOf`
      (`PenEditorViewTests`, `TrendLegendViewTests`), the combo-pick gesture (`PenEditorViewTests`, `SettingsViewTests`),
      the window show and close helpers, the settings `Build()` (`SettingsViewModelTests`, `SettingsViewTests`) and
      `FailedStartup()` (`AppConfigurationTests`, `StartupFailureWindowTests`)
- [x] `FakePenCatalogueEditor.Refusal` replaces the inline copies (12 on 2026-10-09, among them
      `PenFormViewModelTests.cs:232`, `EditorCallQueueTests.cs:99`)
- [x] `RecordingLogger` (`UI/Messages/ReportingTestDoubles.cs:36-67`) is replaced by `FakeLogger` from
      `Microsoft.Extensions.Diagnostics.Testing` if every use reads only level, category, exception and state; add the
      package reference to `SemiPlot.Tests.Unit.csproj` (only Integration references it; version 10.6.0 at
      `SemiPlot/Directory.Packages.props:21`)
- [x] the fold tests' date and converter helpers (`Postgres/HistoryRowFoldTests.cs:29-43`, `Postgres/BucketedRowFoldTests.cs:180-194`)
      and `NaNColumnCount` (`HistoryRowFoldTests`, `RealArchiveGapTests`) get one home; `LiveTailGeneratorTests.cs:17`
      reads `BenchOptions.ConnectionString`
- [x] run the unit suite

### Task 21: Shared integration-test helpers

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ArchiveDatabase.cs`, `ClonedArchiveTest.cs`, `RealtimeSubscriptionTests.cs`,
  `RealtimePollReadTests.cs`, `RealtimeEmptyArchiveTests.cs`, `Journeys/LiveEdgeArchiveJourneyTests.cs`, `DockerCli.cs`,
  the scalar-reader sites below
- Create: `SemiPlot/SemiPlot.Tests.Integration/BatchCollector.cs`, `ArmedGate.cs`, `RealtimeArchive.cs`

- [x] one generic `BatchCollector<T>` for `Journeys/LiveEdgeArchiveJourneyTests` (`:277`) and
      `RealtimeSubscriptionTests` (`:284`); the counting `ArmedGate` (`RealtimeSubscriptionTests:204`) serves the
      journey (`:250`) and the inline gate in `RealtimeEmptyArchiveTests` through `Armed(1)`
- [x] `NewPoll` (`RealtimeEmptyArchiveTests`, `RealtimePollReadTests`), `AppendAsync` (three shapes:
      `RealtimeEmptyArchiveTests`, `RealtimePollReadTests`, `RealtimeSubscriptionTests`), `SeededRows`
      (`RealtimePollReadTests`, `RealtimeSubscriptionTests`) and the shared constants move to one `RealtimeArchive` helper
- [x] `ArchiveDatabase.ScalarAsync<T>` and `ReadAsync<T>` replace the scalar and row readers in (`ArchiveDatabaseTests` was deleted by Task 18; `FollowRestartTests`, `PenRegistrationTests` and `PenCatalogueEditorTests` joined them, and `CountAsync` went)
      `ConvergeTests`, `SeededArchiveTests`, `ArchiveWriterTransactionTests`, `TagCatalogWriterTests`, `CoarseFlushTests`
- [x] the inline `await using` clones (about 12) and `PostgresHistoryReadTests.WriteTailArchiveAsync` reuse
      `ClonedArchiveTest`'s dispose-on-failure path
- [x] `DockerCli.RunAsync` stops returning the unread `Output`
- [x] `FixedClock` (`Integration/PostgresHistoryReadTests.cs:1207` and `Unit/FixedClock.cs:3`) stays duplicated: the
      projects do not reference each other and the copies differ (the unit one carries a time zone)
- [x] run the integration suite

### Task 22: Verify acceptance criteria

- [x] run every command under Acceptance Evidence; record the before and after numbers in the PR body (recorded in the progress file; no PR yet)
- [x] `dotnet format SemiPlot.slnx --verify-no-changes`
- [x] `dotnet test SemiPlot.slnx` (both projects, container runtime present)
- [x] `git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit` prints only
      `HeadlessInput.cs` lines (AGENTS.md)
- [x] every test class still carries the three traits

### Task 23: [Final] Update documentation

- [x] `testing-strategy.md`: the unit-test table (`:34-44`) and the Avalonia list (`:116-121`) name the files that
      exist after the moves and deletions; the pixel-probe paragraph (`:125-135`) names `RedStroke` as the only probe
- [x] `AGENTS.md`: the Test section's file references still resolve (`git grep` each name)
- [x] move this plan to `docs/plans/completed/` (deferred to the delivery step, which archives it; not moved here)

## Post-Completion

**Follow-up effort** (not this PR): extract the shared per-pen ascent-and-anchor loop of `HistoryRowFold.Fold` and
`BucketedRowFold.Fold` into one place; then `BucketedRowFoldTests.cs:65` and `:144` stop pinning a second copy and
can go. A DST-rule change landing in one fold today leaves both test files green.

**Executed by exec:**
- branch: test-suite-cleanup

**Records the review rounds changed** (the task checkboxes above describe the task as first executed):
- Task 3: the MissingField and OutOfRange remedies no longer refer to a "reason"; the OutOfRange remedy takes
  `PostgresConnectionLoader.LowestPort`, `HighestPort` and `LowestPollIntervalMs`, and the HostNotIPv4 detail takes
  the field name as `{1}`. `ArchiveFailureMapper` reads those constants (the coupling is documented in `ui-text.md`).
- Task 10: plain `[Fact]` tests wait through `HistoryDebouncerTestBuilder.AwaitCondition`, which touches no Avalonia
  type; `HeadlessWait.Until` is for `[AvaloniaFact]` callers only and always pumps. A plain `[Fact]` that reads
  `Dispatcher.UIThread`, even `CheckAccess()`, can take the UI dispatcher during a test's reset and hang the suite.
- Task 15: `MainWindowViewTests` keeps the shrink-then-grow window test (the only test of the `SizeChanged` ->
  `FitPanel` wiring).
- Task 16: `AppMenuBarTests` keeps `AutoscaleItem_RevertsTheActivePensAxisToAuto` and
  `InitialScaleItem_SetsTheActivePensAxisToItsStoredPair`, which execute `MainWindowViewModel`'s commands; the
  severity of every mapper arm is asserted through `FailureCase.Severity`.
- Task 17: `HistoryRowFoldTests.ASeedMarkedAsABreakKeepsItsOwnColumnUnderDecimation` stays; the redundant line at
  `:429` was the deletion meant.
- `RedStroke` is now `StrokeProbe`; `EnvelopeLine.Columns` is now `EnvelopeLine.SnapshotColumns()`;
  `ChartTestBuilder.CreateViewModelOnOneScheduler` builds a view model on one `TestScheduler`.
- Measured at the branch head: Unit test attributes 1188 (from 1436), Integration 112 (from 132), test `.cs` lines
  35 614 (from 39 772); unit suite 1623 passed in 5 of 5 bounded runs, integration suite 134 passed.

## Verify it yourself

1. Build and gates: `dotnet build SemiPlot.slnx` (0 warnings), `dotnet format SemiPlot.slnx --verify-no-changes`.
2. Run the unit suite several times in a row, each bounded, and expect every run green in about 30 s with no hang:
   `for i in 1 2 3 4 5; do timeout 300 dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --no-build || break; done`
   (Git Bash). A run that stalls with near-zero CPU is the dispatcher race described above; before `baf8ed7` it
   showed up as one hung run in a handful.
3. Run the integration suite with Docker running:
   `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`; 134 tests, including the three
   container-free classes `RealtimePollTests`, `ConvergeReadinessTests`, `RealtimeSubscriptionDisposalTests`.
4. Run the Acceptance Evidence block above. Every grep prints its "after" state, with two known differences: the
   wall-clock grep prints only `AppConfigurationTests.cs` (the two `PostgresCompositionTests` lines are gone), and the
   size figures are 1188 / 112 / 35 614.
5. Operator-visible change, connection-file failure (no automated UI check covers the rendered window): copy
   `ConfigFiles/` to a scratch folder, leave `password` empty in `connection/connection.yaml`, start the viewer with
   `--config-dir` pointing at it and `Settings` language set to Russian. Before this branch the detail line carried an
   English sentence ("the required field 'password' is ..."); after it, the detail is Russian and names `password`,
   and the remedy no longer mentions a "reason". The same strings are pinned by
   `ArchiveFailureMapperTests` (connection-file `FailureCase` table) in both cultures.
