# Harness debloat

## Overview

The bench, the test harness, the seeder and the viewer's startup carry machinery that exists for its
own tests, for one IDE, or for a claim about a library that turns out to be false. This plan removes
it and leaves one home for each rule:

- the container fixture throws when no runtime is present; no skip gate, no environment variables
- two test projects split on one axis: needs a container or not
- the thinning rule and the freshness rule each live in C# once
- the seeder exposes no entry point or flag that only a test uses
- a .NET Aspire AppHost owns the demo stand: the container, the converge job, the writer and the
  viewer start in dependency order and stop together from any IDE or a console
- a failed startup opens the main window with the message panel filled and the chart empty, instead
  of a second window and a second Avalonia start path

Every task is one pull request, merged after green CI before the next task starts. The base is
`master` at f083f55; the `bench-compose` branch (PR #53) is closed unmerged, because Task 5 replaces
the compose file and the run configurations it introduced.

## Context (from discovery)

Measured 2026-09-03 on `master` at f083f55:

| Area | Fact |
| --- | --- |
| Skip gate | `RequireAvailable` / `IsAvailable` / `UnavailableReason` referenced 94 times across 20 files under `SemiPlot/`; 18 files call `RequireAvailable()` |
| Test projects | `SemiPlot.Tests` 39 files, 6642 lines; `SemiPlot.Tests.Data` 57 files, 8967 lines; `SemiPlot.Tests.Journeys` 5 files, 653 lines |
| Thinning | `SemiPlot.Tools.ArchiveSeeder/LayerThinner.cs:31-46` in C#; `SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs:17-33` repeats it as SQL `row_number()`; `SemiPlot.Tests.Data/Integration/CoarseFlushTests.cs` is 519 lines, 12 cases, comparing the SQL's output with `LayerThinner.Thin` |
| Freshness | `scripts/bench-demo.ps1:106` `$LiveWithin` and `SemiPlot.Tools.ArchiveSeeder/StaleArchiveGuard.cs:12` `MaximumAge`, each commented "keep the two equal" |
| CI timing, run 33485810826 of 2026-09-01 | `data-tests` test step 37 s, covering the image build, the provisioning, the template seed and 85 tests; `journey-tests` test step 29 s |
| Script | `scripts/bench-demo.ps1` 466 lines: runtime check, image build, container run, port wait, freshness, clone, seed, connection file, mutex, `-Down` |

Claims refuted by measurement (against `postgres:17-alpine` 17.11, decompiled Testcontainers 4.14.0
and xunit.v3 3.2.2):

- `StaleArchiveGuard.cs:14-21` and `bench-demo.ps1:170-176`: `to_regclass` inside a `CASE` does not
  keep a missing `public.trends` out of the read. PostgreSQL resolves the relation at parse time and
  answers `42P01` on the untaken branch.
- `PostgresContainerFixture.cs:41-44`, `_creationGate`: two concurrent `CREATE DATABASE ... TEMPLATE`
  from one source succeed, and a `CREATE` beside a `DROP ... WITH (FORCE)` succeeds. The only rule
  is that no session may be connected to the source, and the template is built inside the fixture's
  `InitializeAsync` before any clone exists.
- `ArchiveDatabase.cs:74-76`: `ClearPool` ahead of `DROP DATABASE ... WITH (FORCE)` changes nothing;
  `FORCE` terminates the sessions itself.
- `PostgresContainerFixture.cs:116-117`: `.WithDeleteIfExists(false)` and `.WithCleanUp(true)` are
  the builder's defaults. The reaper label the tripwire test asserts is applied by `Init()` with or
  without the call.
- `PostgresContainerFixture.cs:38-39`: `_startupBound` reaches the CLI pull and the wait strategy
  only; `image.CreateAsync()` at line 120 and `container.StartAsync()` at line 140 run unbounded.
- `SemiPlot.UI/App.axaml.cs:101`: `AvaloniaScheduler.Instance` is a static field and needs no
  `UseReactiveUI()` ahead of it; the comment is wrong and the split of `Program` and `App` stands
  on `AfterSetup` being synchronous alone.
- `TestEnvironment.cs:15-24` with `DatabaseGate.cs:7-22`: when a collection fixture's
  `InitializeAsync` throws, xunit.v3 3.2.2 fails every test of the collection with
  `TestPipelineException` and exits non-zero. `SEMIPLOT_REQUIRE_DB` adds no failure CI would miss.

## Development Approach

- **testing approach**: Regular. Most tasks delete; the tests that remain are the evidence
- one task is one pull request against `master`; merge after green CI, then start the next
- the build stays at 0 warnings and `dotnet format SemiPlot.slnx` stays clean in every task
- every task that changes behaviour updates or adds the test that pins it; every task that deletes
  a test names the surviving test that covers the same rule, or states that the rule was the
  library's
- **CRITICAL: all tests must pass before starting the next task**
- **CRITICAL: update this plan file when scope changes during implementation**

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit`, runs anywhere with the SDK, on both CI legs
- **integration and journeys**: `SemiPlot.Tests.Integration`, needs Docker, runs on the Linux leg
- the demo stand has no automated test; Task 5 carries a smoke checklist

## Acceptance Evidence

Each task states its own evidence. The plan-level checks, run from the repository root after Task 8:

```powershell
git grep -n 'SEMIPLOT_REQUIRE_DB\|SEMIPLOT_PG_IMAGE\|RequireAvailable\|DatabaseGate' -- ':!docs/plans' ; # prints nothing
git grep -n 'row_number' -- 'SemiPlot/SemiPlot.Tools.ArchiveSeeder' ; # prints nothing
git grep -n 'LiveWithin\|keep the two equal' ; # prints nothing
ls -d SemiPlot/SemiPlot.* ; # AppHost, Core, DataSource.Postgres, Tests.Integration, Tests.Unit, Tools.ArchiveSeeder, UI
dotnet build SemiPlot.slnx ; # 0 warnings
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj ; # green without Docker
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj ; # green with Docker
```

With Docker Desktop stopped, the integration run must fail, not skip:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj 2>&1 | Select-String 'TestPipelineException|Skipped'
# expected: TestPipelineException lines naming PostgresContainerFixture; no "Skipped"
```

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview

- **One fixture, no gate.** `PostgresContainerFixture.InitializeAsync` builds the image, starts the
  container and seeds the template, and lets any exception through. xunit reports the cause on
  every test of the collection. `TestEnvironment`, `DatabaseGate` and the `IsAvailable` plumbing go.
- **Two test projects, peers.** `SemiPlot.Tests.Unit` holds every test that needs no container,
  including the seeder's generators and the provider's pure classes. `SemiPlot.Tests.Integration`
  holds the harness, the container tests and the journeys, all in one xunit collection.
- **One home per rule.** `LayerThinner.Thin` thins the follow loop's closed periods over rows read
  back from the finer layer; `CoarseFlush` keeps the read, the opening-row write and the insert, and
  loses the SQL `row_number()` copy. `StaleArchiveGuard.MaximumAge` is the only freshness bound; the
  converge job recreates the archive unconditionally and the follow run refuses a stale one.
- **Seeder verbs.** `seed`, `follow` and the new `converge` share one `RootCommand`. `converge` is
  bench-only: it waits for the server, recreates the stand database from `semiplot_provisioned`,
  seeds it up to now, fills the tag catalogue and writes `archive-connection.yaml` with the bench
  reader's fixed password. It is what the AppHost runs and what a developer runs by hand.
- **AppHost.** `SemiPlot.AppHost` declares the bench container from `SemiPlot/bench/Dockerfile`,
  the converge job, the writer and the viewer. `scripts/`, the mutex and the `.run` compound go in
  the same task, so the script never lives in a half-converted state.
- **Startup failure in the main window.** `Program` keeps the probe ahead of Avalonia. On failure
  `App.Run` opens `MainWindow` with `MainWindowViewModel.StartupFailure` set and no chart; the
  message panel already renders title, detail and remedy for the live-edge fault, and now for the
  startup fault too. `ErrorWindow`, `RunErrorWindow` and `EnsureSingleStart` go.
- **One assertion style**, last, as its own mechanical pull request.

## Technical Details

### Hierarchical thinning is exact

`LayerThinner.AppendPeriod` (`LayerThinner.cs:55-75`) selects a period's first row, last row,
minimum, maximum and every row with a non-ordinary quality, verbatim. The minute layer of an hour
therefore contains that hour's first raw row (the first minute's first), its last, its minimum (the
minimum of the minute minima, ties to the earliest because `MinBy` walks in timestamp order) and
every marker. Thinning the hour layer from the minute layer yields the same rows as thinning it from
raw, and the day layer from the hour layer likewise. A superset of a period's rows preserves first,
last, minimum and maximum, so the seed's own coarse rows in the same period do no harm.

The invariant this rests on: `CoarseFlush.FlushAsync` (`CoarseFlush.cs:70-76`) walks
`LayerThinner.CoarseLayers` in the order minute, hour, day inside one call, so the finer layer of a
closing period is complete before the coarser layer reads it. The kept case
`APairCrossingAnHourClosesTheMinuteAndTheHourAndNotTheDay` fails if that order changes.

`trends.v` is nullable and `ArchiveRow.Value` is not (`CoarseFlush.cs:14-16`). The read-back skips
a row whose `v` is NULL (`reader.IsDBNull`): the seeder never writes one, and a NULL is neither a
minimum nor a maximum. `ANullValuedRawRowIsNotSelectedAsAPeriodsMaximum` stays and pins it.

### Converge

```
converge --admin-connection <postgres on the maintenance db> --connection <scada_writer on the stand db>
         --config-dir <directory for archive-connection.yaml> [--end <yyyy-MM-ddTHH:mm:ss>]
```

The stand database's name is read from `--connection` (`NpgsqlConnectionStringBuilder.Database`);
the admin connection names `postgres`. The steps:

1. open the admin connection with retries until 60 s pass; a refused connection is retried, any
   other failure is reported. The bound only decides how long a broken container takes to report.
   The AppHost starts the job once the container is `Running`, so the wait covers initdb and
   `semibase bench` alone; `ConvergeTests` logs the measured wait so the bound can be revisited
2. `DROP DATABASE IF EXISTS <db> WITH (FORCE)`, then `CREATE DATABASE <db> TEMPLATE
   semiplot_provisioned` (the clone at `bench-demo.ps1:352`, without the `pg_terminate_backend` at
   line 348). On a fresh container the database does not exist and the drop is a no-op;
   a converge rerun against a running stand drops it under the writer and the viewer, which `FORCE`
   disconnects: the writer exits with the connection error, the viewer's live-edge poll shows the
   connection banner and resumes on the recreated archive, whose catalogue is the same seed's.
   Nothing connects to `semiplot_provisioned`, so no session has to be terminated
3. seed with `SeederOptions` at the defaults, `End` = `--end` or the local wall clock
4. `TagCatalogWriter` with the admin connection re-pointed at the stand database
5. write `archive-connection.yaml` with the bench reader role, `TimeZoneInfo.Local.Id`,
   `poll_interval_ms: 1000` (the file at `bench-demo.ps1:406-414`). The zone is the writer's
   machine's, because the archive column holds the SCADA host's naive local time
   (`docs/architecture/scada-archive.md#time-semantics`) and the demo writer is this machine

The bench role names and their fixed passwords (`BenchNames.cs:5-23`) move to the seeder as
`BenchRoles`, because the seeder is the lowest project that needs them; the fixture reads them from
there.

### The AppHost

`SemiPlot/SemiPlot.AppHost/SemiPlot.AppHost.csproj`, Aspire 13.5.3, no `aspire` CLI:

```xml
<Project Sdk="Aspire.AppHost.Sdk/13.5.3">
  <PropertyGroup>
    <OutputType>Exe</OutputType>
    <TargetFramework>net10.0</TargetFramework>
    <IsAspireHost>true</IsAspireHost>
  </PropertyGroup>
  <ItemGroup>
    <ProjectReference Include="..\SemiPlot.Tools.ArchiveSeeder\SemiPlot.Tools.ArchiveSeeder.csproj" />
    <ProjectReference Include="..\SemiPlot.UI\SemiPlot.UI.csproj" />
  </ItemGroup>
</Project>
```

ASSUMPTION: the single-SDK form above is what `dotnet new aspire-apphost` emits for 13.5.3; the
spike in Task 5 runs that template first and adopts its csproj verbatim, including whatever it does
about `AspireUseCliBundle` (the template's `true` expects the CLI; `false` pulls DCP and the
dashboard from NuGet). ASSUMPTION: central package management in `SemiPlot/Directory.Packages.props`
accepts the SDK's implicit `Aspire.Hosting.AppHost` reference without NU1008; if it does not, the
version goes into `Directory.Packages.props` and the reference is made explicit.

No volume: `converge` recreates the archive on every start, so a volume would carry nothing across
runs. Every stand start pays initdb, `semibase bench` and the day-slice seed, which the CI figure
bounds at well under a minute.

The AppHost injects OpenTelemetry and console-formatter variables into every project resource. Both
projects lack an OpenTelemetry SDK and the `Microsoft.Extensions.Logging` console provider, so the
variables are inert.

### Startup failure in the main window

`MainWindowViewModel` gains `StartupFailure` (`ArchiveFailureView?`) and the message panel binds
`Title`, `Detail`, `Remedy` under `IsVisible="{Binding HasStartupFailure}"`. `App.Run` takes
`Result<StartupData>`: on success it runs as today; on failure it sets `_startupFailure` and
`CreateMainWindow` constructs `new MainWindowViewModel { StartupFailure = failure }` without a
service provider. `TrendChartView.OnDataContextChanged` and `MinimapView` already pattern-match
their data context, and `IsCatalogueEmpty` is false while `ChartViewModel` is null, so the chart
area renders empty. `Program.Main` returns 1 after the window closes on the failure path, as today.

## What Goes Where

- Implementation Steps: code, tests, CI, docs in this repository
- Post-Completion: the Rider run on the developer machine, the plant deployment's config file

## Implementation Steps

### Task 1: Remove the skip gate and the no-op harness calls

**Files:**
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/DatabaseGate.cs`, `TestEnvironment.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`, `ArchiveDatabase.cs`,
  `ArchiveTemplate.cs`, `SeededArchive.cs`, `ClonedArchiveTest.cs`, `DockerCli.cs`,
  `PostgresContainerFixtureTests.cs`, `ArchiveDatabaseTests.cs`, `ArchiveDatabaseCollection.cs`, and
  the 18 files calling `RequireAvailable`
- Modify: `CLAUDE.md`, `docs/architecture/testing-strategy.md`, `docs/architecture/bench.md`,
  `docs/architecture/postgres-topology.md`

- [ ] `PostgresContainerFixture`: delete the `try/catch` at lines 82-94 of `InitializeAsync`,
      `UnavailableReason`, `IsAvailable`, `RequireAvailable`, `_creationGate`, `BenchLabel`,
      `BenchLabelValue`, `BenchImageFor`; the image name becomes the constant `semiplot-bench:test`
      and the base image the constant `postgres:17-alpine`; drop `.WithDeleteIfExists(false)` and
      `.WithCleanUp(true)`; pass one `CancellationTokenSource(_startupBound)` token into
      `image.CreateAsync` and `container.StartAsync`; one comment states that `WithWaitStrategy`
      replaces the module's `pg_isready` wait
- [ ] `ArchiveDatabase`: drop the `SemaphoreSlim` parameter from `CloneAsync`, `CopyAsync`,
      `CreateAsync` and the constructor; delete the three `ClearPool` calls in `DisposeAsync` and
      their comment; `ClonePrefix` private; `CountDatabasesCommand` moves into `ArchiveDatabaseTests`;
      `ArchiveDatabaseCollection.cs:5-6` states that the collection shares one server and that clones
      are independent databases, not that classes never race
- [ ] `ArchiveTemplate`: keep `ClearPool` for the admin and writer strings only (line 45 clears a
      reader pool nothing filled)
- [ ] `SeededArchive` and `ClonedArchiveTest`: delete the `IsAvailable` branches and the
      `UnavailableReason ?? ...` throws; `ClonedArchiveTest.DisposeAsync` loses `GC.SuppressFinalize`
- [ ] delete every `RequireAvailable()` call (18 files)
- [ ] `DockerCli`: delete `InspectImageLabelsAsync`; `PostgresContainerFixtureTests` keeps
      `TheServerAnswersAQueryOnTheAdminConnection` only
- [ ] docs: `CLAUDE.md` loses the environment-variable table under "Gated data tests" and the
      sentence naming the tripwire; `testing-strategy.md:147-166` and
      `bench.md#where-the-provisioning-comes-from` (lines 148 and 157 name `SEMIPLOT_PG_IMAGE`) and
      `bench.md#the-test-bench` replace the skip policy with one sentence: a missing runtime fails
      the integration project; `postgres-topology.md:189` drops the `DatabaseGate` node from its
      diagram
- [ ] evidence: `git grep -n 'RequireAvailable\|IsAvailable\|UnavailableReason\|SEMIPLOT_REQUIRE_DB\|SEMIPLOT_PG_IMAGE\|DatabaseGate' -- ':!docs/plans'`
      prints nothing; with Docker stopped the data project fails with `TestPipelineException` and
      reports no skip; with Docker running it is green
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 2

### Task 2: Two test projects, peers on one axis

**Files:**
- Rename: `SemiPlot/SemiPlot.Tests/` to `SemiPlot/SemiPlot.Tests.Unit/`, namespaces
  `SemiPlot.Tests.*` to `SemiPlot.Tests.Unit.*`
- Rename: `SemiPlot/SemiPlot.Tests.Data/` to `SemiPlot/SemiPlot.Tests.Integration/`
- Delete: `SemiPlot/SemiPlot.Tests.Journeys/` (files move to `SemiPlot.Tests.Integration/Journeys/`)
- Move to `SemiPlot/SemiPlot.Tests.Unit/`: `ArchiveRowTests.cs`, `BreakGenerationTests.cs`,
  `LayerThinnerTests.cs`, `LiveTailGeneratorTests.cs`, `PartitionScriptTests.cs`,
  `RawLayerGeneratorTests.cs`, `SeederCommandTests.cs`, `SeederEntryPointTests.cs`,
  `SharedLatticeTests.cs`, `WriterConnectionFailureTests.cs`, `ProcessStateCollection.cs`,
  `BenchOptions.cs`, `BenchRows.cs`, `Data/ArchiveExtentTests.cs`, `Errors/DataErrorTests.cs`,
  `Fixtures/*` (the CSV included), `Postgres/*`
- Modify: `SemiPlot.slnx`, the three `.csproj` files, `SemiPlot.DataSource.Postgres.csproj:17`,
  `SemiPlot.Core.csproj:9` and `SemiPlot.UI.csproj:10-11` (`InternalsVisibleTo`),
  `.github/workflows/ci.yml`, `CLAUDE.md`, `docs/architecture/testing-strategy.md`

- [ ] `git mv` the two project directories and the moved files; namespaces follow the new folders
- [ ] `LayerThinnerTests.cs:125` builds its options with `BenchOptions.For()`, which is
      `ArchiveTemplate.Slice` with the test connection string (`BenchOptions.cs:8-27`), so
      `ArchiveTemplate` stays in the integration project and the unit project references it nowhere
- [ ] `SemiPlot.Tests.Unit.csproj` gains references to `SemiPlot.DataSource.Postgres`,
      `SemiPlot.Tools.ArchiveSeeder`, the `Npgsql` package and the CSV copy item from
      `SemiPlot.Tests.Data.csproj:22`; `xunit.runner.json` with `failSkips` stays with it
- [ ] `SemiPlot.Tests.Integration.csproj` gains `AwesomeAssertions`, `Avalonia.Headless`,
      `Avalonia.Headless.XUnit` and the `SemiPlot.UI` reference; the journeys join
      `ArchiveDatabaseCollection` and `ArchiveJourneyCollection.cs` is deleted; the journeys'
      `TestAppBuilder.cs` moves with them; `ArchiveHarnessSmokeTests.cs` is deleted
      (`PostgresExtentReadTests` covers the same read)
- [ ] `InternalsVisibleTo`: Core, Postgres and UI each name `SemiPlot.Tests.Unit` and
      `SemiPlot.Tests.Integration`
- [ ] `ci.yml`: the test steps of both jobs name `SemiPlot.Tests.Unit`, and the Linux job's data
      and journey steps become one `integration tests` step over `SemiPlot.Tests.Integration`; the
      bench path in the Linux job's comment follows the project
- [ ] `CLAUDE.md` test table and `testing-strategy.md#where-the-boundaries-between-projects-fall`:
      two rows, the one-way-reference rule and the "why the journeys are a project" paragraph
      deleted; `CLAUDE.md:78` states the assertion split as it stands until Task 7
- [ ] evidence: `ls -d SemiPlot/SemiPlot.Tests*` lists `SemiPlot.Tests.Unit` and
      `SemiPlot.Tests.Integration` only; the unit project is green with Docker stopped; the
      integration project is green with Docker running; CI shows two jobs
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 3

### Task 3: One home for thinning, no test-only seeder surface

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs`, `StaleArchiveGuard.cs`,
  `SeederCommand.cs`, `SeederOptions.cs`, `FollowOptions.cs`, `ArchiveWriter.cs`, `Program.cs`
- Delete: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederRun.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/SeederEntryPointTests.cs`, `ProcessStateCollection.cs`,
  `WriterConnectionFailureTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SeederCommandTests.cs`,
  `SemiPlot/SemiPlot.Tests.Integration/Integration/CoarseFlushTests.cs`, `FollowRestartTests.cs`,
  `ArchiveWriterTransactionTests.cs`, `.run/Demo writer.run.xml`, `scripts/bench-demo.ps1`,
  `docs/architecture/bench.md`

- [ ] `CoarseFlush.FlushPeriodAsync`: `SELECT id, l, t, v, q FROM public.trends WHERE l = @finer AND
      t >= @periodStart AND t < @periodEndExclusive` where `@finer` is raw for the minute layer and
      the previous coarse layer otherwise, skipping a NULL `v`; `LayerThinner.Thin(rows, layer)`;
      one `INSERT INTO public.trends (id, l, t, v, q) SELECT * FROM unnest(@ids, @layers, @ts, @vs,
      @qs) ON CONFLICT DO NOTHING`; `_openingRowCommand` and the layer order of `FlushAsync` stay,
      and a one-line comment names the order as the invariant the read-back rests on
- [ ] `StaleArchiveGuard`: `NewestCommand` becomes `SELECT max(t) FROM public.trends;`; delete the
      `to_regclass` comment and the "keep the two equal" comment; a follow run against a database
      without the table now reports Npgsql's `42P01` message through `Program.ReportingAsync` and
      exits 1
- [ ] `SeederCommand`: `Parse` and `SeederRun` go; `Interpret` returns the options through the
      `seed`/`follow` delegates of `RunAsync` only; `--break-count` goes with its ceiling check at
      `SeederCommand.cs:303-307` (four breaks fit any span of a day or more), and
      `SeederOptions.DefaultBreakCount` is the value; `FollowOptions.MaximumSeconds` comment states
      that it bounds both `--follow` and `--change-seconds` at one day
- [ ] `ArchiveWriter`: `ArchiveExistsCommand` and `ArchiveIsSeededCommand` private
- [ ] `SeederCommandTests`: keep the cases exercising `Interpret` (mode exclusivity, `--end`
      required, change-interval ceiling) and the custom parsers, through `RunAsync` with capturing
      delegates; drop the cases exercising System.CommandLine's own parser (repeated option, missing
      value, unknown token) and the break-count ceiling case
- [ ] `SeederEntryPointTests` and `ProcessStateCollection` go: both cases duplicate
      `SeederCommandTests.AnUnknownTokenIsRejectedByName` and `AFollowRunRejectsASeedingOption`;
      `WriterConnectionFailureTests` goes: it asserts which exception type Npgsql throws, a rule
      that is the library's
- [ ] `CoarseFlushTests`: delete the SQL-versus-C# comparison helper (`ExpectedThin` and what serves
      it), `AMarkerRowReachesEveryCoarseLayer` (`LayerThinnerTests` pins markers in memory) and
      `AFlushOverAPeriodTheSeederAlreadyThinnedAddsNoDuplicate` (the second-flush case pins the same
      `ON CONFLICT`); the other ten cases stay, asserting the table's coarse rows against
      `LayerThinner.Thin` of the raw rows the test wrote: the closed minute, the second flush, the
      pair inside one minute, the pair crossing an hour, the call spanning several periods, the
      three opening-row cases, the NULL `v` row and the empty period
- [ ] `FollowRestartTests` collapses to one gated case: the restart's first append hits no `23505`
      and the seam holds one row per key; the in-memory half is `SharedLatticeTests:58-93`
- [ ] `ArchiveWriterTransactionTests`: keep `ACopyThatFailsPartWayLeavesNoArchiveBehind` (the day
      partitions are created inside the COPY's transaction, `ArchiveWriter.cs:61-67`, which the
      restart path relies on), `TheAppendingRunWritesWhereTheSeedingRunIsRefused` and
      `TheAppendingRunCreatesOnlyTheDaysItsRowsNeed`; drop
      `AnAppendingCopyThatFailsPartWayLeavesTheArchiveAsItWas`, the same rollback under the other
      flag
- [ ] `.run/Demo writer.run.xml:4` drops `--pens 8 --seed 1` and `bench-demo.ps1:370` drops `--days
      $SeedDays --pens $SeedPens --seed $SeedSeed`, the defaults say it;
      `bench.md#thinning-into-the-coarse-layers` describes the read-back and the layer order
- [ ] evidence: `git grep -n 'row_number\|Parse(' -- SemiPlot/SemiPlot.Tools.ArchiveSeeder` prints
      nothing; `SemiPlot.Tests.Integration` green; the demo writer appends and thins for five minutes
      against the stand and `SELECT l, count(*) FROM public.trends GROUP BY l` shows all four layers
      growing
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 4

### Task 4: The converge verb

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ConvergeOptions.cs`, `Converge.cs`,
  `ConnectionFileWriter.cs`, `BenchRoles.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`, `Program.cs`,
  `StaleArchiveGuard.cs`, `SemiPlot/SemiPlot.Tests.Integration/Integration/BenchNames.cs`,
  `StaleArchiveGuardTests.cs`, `docs/architecture/bench.md`, `CLAUDE.md`
- Create: `SemiPlot/SemiPlot.Tests.Integration/Integration/ConvergeTests.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/ConnectionFileWriterTests.cs`

- [ ] `BenchRoles` in the seeder holds the role names and the fixed bench passwords now in
      `BenchNames.cs:5-23`; `BenchNames` is deleted and the fixture reads `BenchRoles`
- [ ] `converge` verb per Technical Details: wait for the admin connection up to 60 s, `DROP
      DATABASE IF EXISTS ... WITH (FORCE)`, clone from `semiplot_provisioned`, seed with
      `SeederOptions` defaults up to `--end` or now, fill tags, write the connection file;
      `StaleArchiveGuard.Describe` names `converge` instead of `pwsh scripts/bench-demo.ps1`, and
      `StaleArchiveGuardTests.cs:32` asserts the new remedy
- [ ] `ConnectionFileWriter` writes the YAML the loader reads, with `TimeZoneInfo.Local.Id` and
      `schema: public` (Task 6 makes the key optional and drops the line)
- [ ] `ConvergeTests` (gated): converge against the fixture's server with a fresh database name in
      `--connection`, then assert the database exists, `public.trends` holds the slice's row count,
      `semiplot_tags` holds the pens, and the file parses through `PostgresConnectionLoader.Load`;
      a second converge while a reader connection is held open recreates (row count equal,
      `pg_database.oid` different, the held connection broken); the test writes the measured
      readiness wait to the output
- [ ] `ConnectionFileWriterTests`: the written file round-trips through the loader with the host,
      port, database, user, password and zone given
- [ ] `bench.md#the-application-bench` and the `CLAUDE.md` seeder recipe describe `converge`;
      `scripts/bench-demo.ps1` is untouched and goes whole in Task 5
- [ ] evidence: `dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- converge ...`
      against the bench container `pwsh scripts/bench-demo.ps1` started prints the seeding summary and leaves
      `SemiPlot/Artifacts/bench-config/archive-connection.yaml`; the viewer opens on it
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 5

### Task 5: The AppHost owns the stand

**Files:**
- Create: `SemiPlot/SemiPlot.AppHost/SemiPlot.AppHost.csproj`, `AppHost.cs`,
  `Properties/launchSettings.json`
- Move: `SemiPlot/SemiPlot.Tests.Integration/bench/` to `SemiPlot/bench/` (inside the CI `paths:`
  filter); the integration csproj copies it from there
- Delete: `scripts/`, `.run/Bench container.run.xml`, `Bench up.run.xml`, `Bench down.run.xml`,
  `Demo writer.run.xml`, `Viewer (bench).run.xml`
- Modify: `.run/Live demo.run.xml` (a `DotNetProject` configuration for the AppHost, in the shape of
  `Debug.run.xml`), `SemiPlot.slnx`, `Directory.Packages.props`,
  `SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`,
  `SemiPlot/SemiPlot.Tests.Integration/Integration/PostgresContainerFixture.cs`,
  `docs/architecture/bench.md`, `CLAUDE.md`

- [ ] spike first, before anything else in this task: `dotnet new aspire-apphost` into a scratch
      directory, adopt its csproj shape, add only `AddProject<Projects.SemiPlot_UI>("viewer")`, and
      `dotnet run`. The viewer is a `WinExe`; whether its window shows and its stdout reaches the
      dashboard when DCP launches it is not documented. If the window does not show, the viewer
      stays outside the AppHost as the `Viewer (bench)` run configuration, and the AppHost owns the
      container, converge and the writer only; the remaining checkboxes are read with that
      substitution. The spike also settles the two csproj ASSUMPTIONs in Technical Details
- [ ] `SemiPlot.AppHost.csproj` per Technical Details; `Directory.Packages.props` carries whatever
      version entry central package management demands
- [ ] `AppHost.cs`: `AddDockerfile("bench", "../bench")`, `WithEnvironment` for the four variables
      of `scripts/bench-demo.ps1:240-243` (`POSTGRES_PASSWORD`, `SEMIBASE_WRITER_PASSWORD`,
      `SEMIBASE_READER_PASSWORD`, `SEMIPLOT_PROVISIONED_DATABASE`), `WithEndpoint(port: 55432,
      targetPort: 5432, scheme: "tcp", isProxied: false)`, default session lifetime, no volume;
      `AddProject<Projects.SemiPlot_Tools_ArchiveSeeder>("converge").WithArgs("converge", ...)
      .WaitFor(bench)`; `AddProject<...>("writer").WithArgs("--follow", "1", "--change-seconds",
      "0.5", ...).WaitForCompletion(converge)`; `AddProject<Projects.SemiPlot_UI>("viewer")
      .WithArgs("--config-dir", ..., "--log-file", ...).WaitForCompletion(converge)`
- [ ] no health check on the container: `WaitFor` waits for the `Running` state only, and the
      converge verb's own connection retry (Task 4) is the readiness wait; the AppHost references no
      health-check package
- [ ] the fixture's Dockerfile directory constant points at the copied `bench/`; the csproj copy
      item changes from `bench\**` to `..\bench\**` with a `Link`
- [ ] `bench.md#the-application-bench` and `#running-it-from-rider` become one section: `dotnet run
      --project SemiPlot/SemiPlot.AppHost` or the `Live demo` configuration; stopping either stops
      the container; the JetBrains Aspire plugin (`me.rafaelldi.aspire`) is optional and adds
      child-resource debugging; one sentence says the injected OTEL variables are inert;
      `CLAUDE.md` recipe updated
- [ ] evidence, smoke checklist on the developer machine:
      1. `dotnet run --project SemiPlot/SemiPlot.AppHost` - the dashboard lists bench, converge,
         writer, viewer; converge finishes; writer appends; the viewer window opens on live data
      2. Ctrl+C in that console - `docker ps -a --filter name=bench` prints no container within 15 s
      3. `Live demo` in Rider, Run, wait for the viewer, Stop once - same `docker ps -a` result
      4. `Live demo` in Rider, Debug, Stop - same result
      A container left behind in step 3 or 4 is the Aspire shutdown path not running to the end
      (aspire#19250 was that regression in 13.5.0); the fallback is `ContainerLifetime.Persistent`
      plus a documented `docker rm`, and the plan records which shipped
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 6

### Task 6: Startup failure in the main window

**Files:**
- Delete: `SemiPlot/SemiPlot.UI/Startup/ErrorWindow.axaml`, `ErrorWindow.axaml.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/ErrorWindowTests.cs`
- Rename: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs` to
  `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs` and `StartupFailureView.cs` to
  `MainWindow/ArchiveFailureView.cs`; `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupFailureMapperTests.cs`
  to `UI/MainWindow/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`, `App.axaml.cs`, `Startup/StartupProbe.cs`,
  `MainWindow/MainWindowViewModel.cs`, `MainWindow/MainWindow.axaml`,
  `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`,
  `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupProbeTests.cs`,
  `UI/MainWindow/MainWindowViewModelTests.cs`, `Postgres/PostgresConnectionLoaderTests.cs`,
  `ConnectionFileWriterTests.cs`, `CLAUDE.md`, `docs/architecture/data-integration.md`,
  `docs/architecture/postgres-instance.md`

- [ ] `MainWindowViewModel.StartupFailure` (`ArchiveFailureView?`) and `HasStartupFailure`;
      `MainWindow.axaml` message panel shows `Title`, `Detail`, `Remedy` when set; the chart, legend
      and minimap bind to null view models and render empty
- [ ] `App.Run(Result<StartupData>)` replaces `Run` and `RunErrorWindow`; `_started`,
      `EnsureSingleStart` and `_startupFailure` go; `Program.Main` calls `App.Run` once and returns 1
      when the probe failed; `FailedExitCode` private
- [ ] `StartupProbe.Run` loses the `readBound` parameter and the `Task.Run` hop (`ReadAsync(...)
      .GetAwaiter().GetResult()` under no synchronization context); `StartupProbeTests:204-206` pass
      no bound; `Run_WithNoConnectionFile_EndsStartup` covers `Run`'s early return and the
      fake-provider cases at lines 114-139 cover `ReadAsync`, which is all `Run` composes
- [ ] `ArchiveFailureMapper`: `GenericTitle` is a private literal; the two tests asserting the
      constant go; the `source_time_zone` remedy names "an identifier this machine knows: IANA, or
      the id `tzutil /g` prints"
- [ ] `PostgresConnectionLoader`: `schema` optional with default `public` (line 123 leaves the
      required list); `PostgresConnectionLoaderTests` gains the default case;
      `ConnectionFileWriter` stops emitting the `schema` line and its round-trip test follows
- [ ] tests: `MainWindowViewModelTests` case for `StartupFailure` set (panel visible, chart null);
      an `[AvaloniaFact]` opening `MainWindow` with a failed view model and asserting the three texts
- [ ] docs: `data-integration.md#startup` describes the failure path as the main window with the
      message panel, not `ErrorWindow`; `App.axaml.cs:101` loses the `UseReactiveUI` comment
- [ ] evidence: `ls SemiPlot/SemiPlot.UI/Startup` lists no `ErrorWindow*`; starting the viewer with
      `--config-dir` pointing at an empty directory opens the main window with the message panel
      naming the missing file and an empty chart, and the process exits 1 when it is closed
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 7

### Task 7: One assertion style

**Files:**
- Modify: every file under `SemiPlot/SemiPlot.Tests.Unit` and `SemiPlot/SemiPlot.Tests.Integration`
  that uses `Assert.`

- [ ] rewrite `Assert.Equal/True/False/Null/NotNull/Throws/IsType/Single/Empty/Contains` to
      AwesomeAssertions `.Should()` forms; `Assert.Skip` no longer exists after Task 1
- [ ] `CLAUDE.md:78`: replace the "Assertions split by project" sentence with "AwesomeAssertions
      everywhere"
- [ ] evidence: `git grep -n 'Assert\.' -- 'SemiPlot/SemiPlot.Tests*'` prints nothing; both test
      projects green
- [ ] run `dotnet test SemiPlot.slnx` - must pass before Task 8

### Task 8: Verify acceptance criteria

- [ ] run the plan-level checks under Acceptance Evidence
- [ ] run `dotnet format SemiPlot.slnx --verify-no-changes`
- [ ] run `lint-comments SemiPlot` and fix what it reports
- [ ] verify the two test projects carry the three traits on every class

### Task 9: Update documentation

- [ ] `docs/architecture/README.md` and `overview.md`: project list and the stand's one command
- [ ] `CLAUDE.md`: build, test and bench sections read against the final tree
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification**

- the plant deployment's `archive-connection.yaml` keeps working without a `schema` line and with a
  Windows zone id
- a developer machine without Docker: `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
  is the command to run; the integration project fails there by design

**External**

- the Rider Docker plugin and `remote-servers.xml` are no longer needed for the stand
