# Simplify the test harness

## Overview

The gated suites run in a container and nothing else. A run raises the harness, tests, and kills
everything it made; no state survives it and no branch exists for a server that does. Three things
follow.

**`SEMIPLOT_TEST_PG` goes.** It names an existing server to use instead of a container
(`SemiPlot/SemiPlot.Tests.Data/Integration/TestEnvironment.cs:9`). It is set in no workflow, no
script, no `.runsettings` and no `launchSettings.json` — the repository holds none of those last two
at all — and no test exercises it. Seven files serve it, one of them
(`Integration/SemibaseBinary.cs`, 26 lines) exists for nothing else.

**The reuse machinery goes with it.** `ArchiveTemplate` names its database by a SHA-256 of the seeder
assembly's MVID and the slice options (`ArchiveTemplate.ComputeName`), checks whether that
database already exists (`:48`), and repairs a database left behind by a crashed run (`:76-88`). Each
of those is a correct answer to "the server outlives the run". A container does not.

**The two generators become one.** `RawLayerGenerator` is a stateful walk over a seeded random
stream; `LiveTailGenerator` is a pure function of absolute time
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs:3-8`). Their lattices differ, and that
difference produced the primary-key collision fixed in `f91889d` and the seam hole fixed in
`caa935f`. One lattice removes the class.

**What does not change is the seed's size.** Measured today, and stated here because the plan was
written on the opposite assumption: the archive cannot shrink usefully. The planner floor is far
below the current slice, but two content tests bind well above it, and the wall clock the volume
costs is under a second. **Measurement** below carries the numbers.

## Context (from discovery)

Every figure here was measured on 2026-08-28 at `59afffe`, against `postgres:17-alpine` and
`ghcr.io/semiteq/semibase@sha256:533adc17a4f934827c18e5ad65cebae220daf70db2ba6b837781204496f6291f`.

### Baseline

| Suite | Tests | Failed | Skipped |
| --- | --- | --- | --- |
| `SemiPlot.Tests` | 362 | 0 | 0 |
| `SemiPlot.Tests.Data` | 502 | 0 | 0 |
| `SemiPlot.Tests.Journeys` | 4 | 0 | 0 |

Line counts, `.cs` only: `SemiPlot.Tests` 7011, `SemiPlot.Tests.Data` 10692 (of which
`Integration/` 5591), `SemiPlot.Tests.Journeys` 667, `SemiPlot.Tools.ArchiveSeeder` 2248.

The harness itself — the fourteen non-test files under `SemiPlot.Tests.Data/Integration/` — is
**1230 lines**: `ArchiveDatabase` 132, `ArchiveDatabaseCollection` 11, `ArchiveProviderFactory` 54,
`ArchiveReadSupport` 26, `ArchiveTemplate` 141, `DatabaseGate` 28, `PostgresContainerFixture` 355,
`PostgresServer` 51, `ProvisionerImage` 126, `ProvisionerResolution` 20, `SeededArchive` 34,
`SemibaseBinary` 26, `SemibaseProvisioner` 178, `TestEnvironment` 48.

### Measurement: the archive's volume floor

The standard slice is 1 day, 8 pens, seed 1, `--change-seconds 5`, 4 breaks
(`Integration/ArchiveTemplate.cs:26-35`), which is **266 372 rows** — 229 862 raw, 35 599 minute,
815 hour, 96 day — landing in one day partition, `tp2026m01d01`, at `relpages=4` per 500 rows.

**The planner floor is about 500 rows.** Each size below was seeded into a fresh clone of
`semiplot_provisioned`, analysed, and each of the five gated statements from
`SemiPlot.DataSource.Postgres/ArchiveStatements.cs` explained with production's own parameters bound
as literals. `Seq Scan on tp2026m01d01` is what `ExplainPlanTests._sequentialScanOverRows` rejects
(`Integration/ExplainPlanTests.cs:48`).

| `--change-seconds` | Rows | `relpages` | Extent | Window | Window at first instant | Poll | Baseline |
| --- | --- | --- | --- | --- | --- | --- | --- |
| 5 | 266372 | 1984 | index | index | bitmap | bitmap | index |
| 320 | 7089 | 53 | index | index | bitmap | index | index |
| 5120 | 700 | 6 | index | index | bitmap | index | index |
| 10240 | 509 | 4 | index | index | bitmap | index | index |
| 11000 | 494 | 4 | index | index | bitmap | **seq scan** | index |
| 20480 | 378 | 3 | index | **seq scan** | **seq scan** | **seq scan** | index |

**The measured planner floor is 509 rows in 4 pages, and it is a knife edge**: 494 rows in the same
4 pages already loses the poll plan. The lowest size with real margin is 700 rows in 6 pages, an
order of magnitude under the slice. The extent and baseline plans never flip — both step to an index
edge per variable and stay there at every size measured.

**Two content tests bind far above that floor, and they are what actually sets the size.** Running
the whole of `SemiPlot.Tests.Data` against a shrunk template:

| `--change-seconds` | Rows | Result |
| --- | --- | --- |
| 5 | 266372 | 502 passed |
| 10 | 146685 | 502 passed |
| 15 | not measured | 1 failed |
| 20 | 82081 | 1 failed |
| 40 | 44706 | 2 failed |
| 320 | 7089 | 2 failed |

The two:

- `PostgresHistoryReadTests.TheMinuteLayerReturnsFewerColumnsThanRawOverTheSameWindow`
  (`Integration/PostgresHistoryReadTests.cs:162-191`) needs the raw layer denser than the minute
  layer inside its window. It is a **density** floor, not a row-count one, and it fails first.
- `StatementTimeoutReadTests.TimedOutReadReportsTheServersOwnBound`
  (`Integration/StatementTimeoutReadTests.cs:49-50`) forces a `57014` by reading the full seeded day
  at raw inside a 50 ms bound (`:36`). Its own failure message says
  "Change the forcing mechanism — never widen the assertion" (`:70-73`).

**The volume costs almost nothing.** The seeder CLI wrote 266 372 rows over a published port in
1144 ms wall, including .NET process start; 7 089 rows took 454 ms. Shrinking the archive to a
thirty-seventh of its size buys 0.69 s.

**Conclusion: the slice stays at `--change-seconds 5` and 266 372 rows.** The cost worth attacking is
the generator's code, not the archive's size.

### Measurement: what a run leaves behind

`docker system df` reports 186 images, **2.587 GB reclaimable (67%)**, and 172 dangling images of
436 MB each. One `dotnet test` of a single gated class took the dangling count from 172 to 173 and
changed `semiplot-bench:fc0cf5409512` from image `7d68a77e6bce` to `51e5ed292fc3`. **Every gated run
leaves one 436 MB dangling image.**

One cause, measured: `WithCleanUp(false)` on the built image keeps Ryuk from labelling it, so the
previously tagged build is untagged rather than removed. The 436 MB is the size of a whole bench
image, which matches that and nothing else. `bench/Dockerfile`'s
`FROM ${PROVISIONER_IMAGE} AS provisioner` is **not** a second cause: on the classic builder that
stage references the already-pulled `semibase` image and materialises no new one.

The build context is the test assembly's output directory (`:172`) and `bench/**` is copied
`PreserveNewest` (`SemiPlot.Tests.Data.csproj`), so a rebuild changes the copied files' modification
times, misses the `COPY` layer cache and produces a new image id under the same tag.

### Measurement: what a failure costs

A provisioning that exits is bounded — the container reaches `State == Exited` and the start throws
with the container log, measured at 16.4 s. A container that stays up and never becomes ready is
not: the wait strategy runs to `WaitStrategyTimeout ?? 1 hour`, and the current strategy sets no
timeout (`Integration/PostgresContainerFixture.cs:189`). `ProvisionerImage.ResolveAsync` runs the
registry pull ahead of the wait strategy with no deadline of any kind — its only caller passes no
token (`:159`, against `Integration/ProvisionerImage.cs:29-30`).

`Testcontainers 4.14.0` carries `IWaitForContainerOS.UntilCommandIsCompleted(IEnumerable<string>,
Action<IWaitStrategy>)` and `IWaitStrategy.WithTimeout(TimeSpan)`, verified in the package's own
`Testcontainers.xml`. Both waits are expressible.

### The `SEMIPLOT_TEST_PG` surface

| File | Lines | Fate |
| --- | --- | --- |
| `Integration/SemibaseBinary.cs` | 26 | delete outright; sole caller is `PostgresContainerFixture.cs:296` |
| `Integration/SemibaseProvisioner.cs` | 178 | delete `:44-177` and the usings at `:1-6`; keep the constants |
| `Integration/PostgresContainerFixture.cs` | 355 | delete `:285-354` (`UseExistingServerAsync`), edit `:24`, `:33-36`, `:81-82`, `:117-119`, `:199` |
| `Integration/TestEnvironment.cs` | 48 | delete `:8-10`, `:16-17`, `:21-22`, `:25-30` |
| `Integration/PostgresServer.cs` | 51 | delete the `SemibaseExecutable` parameter `:12` and the comment `:6`, `:9-10` |
| `Integration/DatabaseGate.cs` | 28 | comment only, `:6-7` |
| `Integration/PostgresContainerFixtureTests.cs` | 70 | delete `:57-64`; rewrite or delete `:32-46` |

`SemibaseProvisioner`'s constants are the contract with the image's `provision.sh` and stay:
`ProvisionedDatabase` (`:28`), `WriterRole` (`:30`), `ReaderRole` (`:32`), `WriterPasswordVariable`
(`:38`), `ReaderPasswordVariable` (`:40`). They are read at `ArchiveTemplate.cs:53`,
`PostgresContainerFixture.cs:106,185,186,188,268`, `PostgresServer.cs:30,35`,
`SeededArchiveTests.cs:106` and `StatementTimeoutReadTests.cs:104`.

`scripts/bench-demo.ps1` passes `SEMIBASE_WRITER_PASSWORD` and `SEMIBASE_READER_PASSWORD` as
`docker run --env` into the bench image (`:235-236`). That is the container path and does not change.

### The two generators

`RawLayerGenerator.Generate` walks a `SeededRandom` per pen
(`SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs:74`), draws exponential intervals (`:279-285`),
picks one of four segment kinds (`:260-277`) and keeps its position in `PenTrace`
(`SemiPlot.Tools.ArchiveSeeder/PenTrace.cs`). `LiveTailGenerator` places every row at
`index * intervalTicks` with an anchor one poll interval before it and reads its value from
`SyntheticValueWalk.Value(seed, penId, index, min, max)`
(`SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs:38-67`, `:96-99`), which is already a pure
function of its inputs (`SemiPlot.Tools.ArchiveSeeder/SyntheticValueWalk.cs:5-10`).

`BreakPlan` is the one other consumer of `SeededRandom`, for a break's duration and offset
(`SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs:66-83`); everything else in that file is arithmetic over
the span.

**Dependencies:** none new. Two go: `FluentResults` leaves `SemiPlot.Tests.Data/Integration/`, and
`Testcontainers.PostgreSql` is a candidate to become plain `Testcontainers` — deferred, see
**Post-Completion**.

## Development Approach

- **testing approach**: Regular (code first, then tests), matching this repository's other plans.
- complete each task fully before moving to the next
- make small, focused changes
- **every task includes new/updated tests** for the code it changes
- **all three suites pass before starting the next task** — no exceptions
- **update this plan when scope changes during implementation**

### Two mistakes this plan can make that no red test reports

**A refactor that turns an unavailable runtime into an always-skip.** `PostgresContainerFixture`
never throws; it captures a reason and hands it to `DatabaseGate`, which skips or fails according to
`SEMIPLOT_REQUIRE_DB` (`Integration/PostgresContainerFixture.cs:115-139`,
`Integration/DatabaseGate.cs:12-27`). Replacing the `Result<T>` plumbing with exceptions means one
`catch` in `InitializeAsync`, and **that catch must be broad**. A narrow one turns a missing runtime
into 87 failures instead of 87 skips, which is loud and gets fixed. The silent direction is worse:
a fixture that captures a reason it should not have captured leaves the suite always-skipping while
CI stays green, because a skip is green on a developer machine. `TestEnvironmentTests` is the only
assertion that `SEMIPLOT_REQUIRE_DB=1` actually flips skip into fail
(`Integration/TestEnvironmentTests.cs:15-39`). **It survives this plan.** Trimming its whitespace and
case rows is fine; `"1"` → true, `null` → false and one false case stay. Every acceptance command
below reports skipped counts, and every one of them must read 0.

**The exactly-once regression in the live-edge journey.** `BatchCollector` holds one subscription for
the whole test and records every batch that arrives, including one that arrives between two awaits
(`SemiPlot.Tests.Journeys/LiveEdgeArchiveJourneyTests.cs:312-368`). Replacing it with a
`FirstAsync()` per write unsubscribes after each batch, so a duplicate delivered in the gap is
observed by nobody and `afterTheFirstWrite.Should().HaveCount(1)` (`:129`) passes over a suite that
no longer proves what it claims. The failure mode is not a red test; it is an intermittent hang
weeks later, when a duplicate reaches production's applier and nothing in the suite ever saw one.
**Keep one persistent recording subscription — the list plus the lock.** `FirstAsync().ToTask()` may
be the sequencing gate and nothing more.

## Testing Strategy

- **no new gated test class.** Every assertion this plan adds lands in a class that exists:
  `ArchiveDatabaseTests`, `PostgresContainerFixtureTests`, `RawLayerGeneratorTests`.
- **raw xunit `Assert.` in `SemiPlot.Tests.Data`,** AwesomeAssertions in `SemiPlot.Tests` and
  `SemiPlot.Tests.Journeys`. Neither style crosses into the other project.
- **`SemiPlot.Tests.Journeys` gains no `xunit.runner.json`.** `failSkips` is project-wide and the
  journeys are gated end to end (`CLAUDE.md`, **Test**).
- **the golden digest re-pins in Task 4 and only there.** `RawLayerGeneratorTests` pins
  `StandardSliceDigest` and `StandardSliceRowCount = 229862` (`RawLayerGeneratorTests.cs:17-18`) over
  `RawLayerGenerator.Generate(BenchOptions.For())`. A new generator produces a different archive, so
  both constants change. That is expected and is the point of the test — it detects the change and
  makes it deliberate. Tasks 1 to 3 must leave both constants untouched; a digest that moves there is
  a defect, not a re-pin.
- **the slice's parameters do not move.** `ArchiveTemplate.Slice` (`ArchiveTemplate.cs:26-35`) keeps
  1 day, 8 pens, seed 1, `--change-seconds 5`, 4 breaks and its fixed `--end`. **Measurement** above
  is why.
- **the shape the reader is tested against does not move.** Per property, what stops meaning anything
  if it goes:

| Property | Tests that lose their meaning |
| --- | --- |
| Two rows per change — an anchor carrying the previous value one poll interval before the change | `HistoryRowFoldTests` (466 lines) reads the pair as one change; `RawLayerGeneratorTests.EveryChangeRowFollowsItsPredecessorByExactlyOnePollInterval` (`:47-66`) asserts it directly; `MinMaxDecimator` folds it |
| `q = 32` before a break, `q = 16` after it | `BreakRenderArchiveJourneyTests` (221 lines) is the break-renders-as-a-break journey; `HistoryRowFold`'s only gap branch reads `q`; `BreakGenerationTests` (268 lines) is nothing else |
| Four populated layers, 0 to 3 | `CoarseFlushTests` (603 lines) meets `LayerThinner` on the server; `FreshTailBoundTests` (259 lines) and the coarse-layer reads in `PostgresHistoryReadTests` need a coarse row to read |
| Rows on more than one day partition | `ExplainPlanTests.MaximumDayPartitionsRead` (`ExplainPlanTests.cs:101`) states its count cannot fail on a one-day archive and that the bound is proved by `Assert.Contains("t >=", …)` instead. **The standard slice is one day and this plan does not change it**; the property is named here so a later multi-day slice keeps it |

- **`SparseHistoryWindow`'s outer `ORDER BY id, t` keeps a pin.** `HistoryRowFold` groups by
  consecutive identifier and requires one ascending run per pen; only the SQL guarantees it.
  `ExplainPlanTests` asserts no ordering, and an index scan's order matches by accident. When the
  character-for-character statement tests go in Task 1, a three-line
  `Assert.EndsWith("ORDER BY id, t;", ArchiveStatements.SparseHistoryWindow, StringComparison.Ordinal)`
  stays behind.
- **"leave nothing behind" gets an assertion.** Without one the new promise regresses in silence.

## Acceptance Evidence

Every item is a command with the result it must produce. Docker is available; the gated suites run
with `SEMIPLOT_REQUIRE_DB=1` and **must report 0 skipped** — a skipped gated test is a failed
acceptance, because an always-skipping suite is the worst outcome of this exercise.

1. **All three suites green, at the baseline counts or above.**

```powershell
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj -c Release
```

Baseline at `59afffe`: 362 / 502 / 4, all 0 failed and 0 skipped. After the cuts the counts fall —
Task 1 deletes tests — and the expected end state is stated per task. Failed and skipped stay 0
throughout.

**Re-measured on this commit's tree with `SEMIPLOT_REQUIRE_DB=1`: 362 / 507 / 4, all 0 failed and
0 skipped.** The Data count read 478 at `9f025a3`; the five review rounds that followed added 29
tests, all of them covering a review finding. Every per-task figure recorded in the task log below is
what that task measured when it ran, and 507 is the current state. The conventions round moved it
from 501 by +3 — `TestEnvironmentTests.TheImageVariableSelectsTheBaseImageAndFallsBackToTheDefault`,
which the Read-rejects-a-blank comment claimed was covered and was not — and by -1, merging
`ACloneLeavesNoDatabaseBehindItself` into `TheDatabaseIsGoneAfterDisposal`, which asserted the same
lifetime through a second query. The final round took 503 to 507: `SharedLatticeTests`' two tests on
the merged lattice, and `SeederOptionsTests`' two on the restored `--end` bound.

➕ **The over-engineering cuts took the Data count 507 → 502; the other two did not move.**
Re-measured with `SEMIPLOT_REQUIRE_DB=1`: **362 / 502 / 4, all 0 failed and 0 skipped.** The five
are the teardown leak audit's own test
(`PostgresContainerFixtureTests.TheContainerIdIsReadableWhetherOrNotTheDaemonCreatedTheContainer`)
and the four `SeederOptionsTests` cases that existed only for `SeederOptions.ValidateBreakMarkers` —
the three-case `ParseRejectsAChangeIntervalThatLeavesARunWithNoChange` theory and
`ParseAcceptsAWideChangeIntervalWhoseRunsAllHoldOne`. The pair-rejection itself is still covered, by
`RawLayerGeneratorTests.AChangeIntervalThatLeavesARunWithNoChangeIsRejected` against
`RawLayerGenerator.Generate`.

2. **The line counts fall, and by how much.**

```powershell
foreach ($p in 'SemiPlot.Tests','SemiPlot.Tests.Data','SemiPlot.Tests.Journeys','SemiPlot.Tools.ArchiveSeeder') {
  "{0,-32} {1}" -f $p, ((Get-ChildItem "SemiPlot/$p" -Recurse -Filter *.cs | Get-Content).Count)
}
Get-ChildItem SemiPlot/SemiPlot.Tests.Data/Integration -Filter *.cs |
  Where-Object { $_.Name -notmatch 'Tests\.cs$' } | Get-Content | Measure-Object -Line
```

| Measure | Before | Target after | Where the figure comes from |
| --- | --- | --- | --- |
| `SemiPlot.Tests` | 7011 | unchanged | this plan does not touch the UI suite |
| `SemiPlot.Tests.Data` | 10692 | ≤ 9850 | 430 harness + ~170 `ClonedArchiveTest` net + ~256 deleted test methods |
| `SemiPlot.Tests.Journeys` | 667 | ≤ 630 | 16-line header + 21 lines of clone boilerplate |
| `SemiPlot.Tools.ArchiveSeeder` | 2248 | ≤ 2010 | `SeededRandom`, `PenTrace`, `EmitStep`, the two dropped span bounds |
| `Integration/` harness, 14 non-test files | 1230 | ≤ 800, in at most 12 files | itemised in Task 3 |

Each target is the itemised deletions summed, not a round number picked first. A run that lands
above one of them means the itemised work did not all happen; record the actual and say which item
fell short rather than trimming something unrelated to reach a threshold.

The counts reproduce only because build output is redirected to `SemiPlot/Artifacts/`, so
`-Recurse` finds no generated `.cs` under `obj/`. Run this item from a tree where that holds.
**Verified before counting:** `SemiPlot/Directory.Build.props` sets `ArtifactsPath` to
`$(MSBuildThisFileDirectory)Artifacts`, and no `obj/` or `bin/` directory exists under any of the
four projects.

**Re-measured on this commit's tree:**

| Measure | Before | Target after | Actual | Verdict |
| --- | --- | --- | --- | --- |
| `SemiPlot.Tests` | 7011 | unchanged | 7011 | met |
| `SemiPlot.Tests.Data` | 10692 | ≤ 9850 | **10239** | 389 over |
| `SemiPlot.Tests.Journeys` | 667 | ≤ 630 | **645** | 15 over |
| `SemiPlot.Tools.ArchiveSeeder` | 2248 | ≤ 2010 | **2055** | 45 over |
| `Integration/` harness | 1230, 14 files | ≤ 800, ≤ 12 files | **942, 15 files** | 142 lines and 3 files over |

Every figure except `SemiPlot.Tests` moved after `9f025a3`, where the table first read 10106 / 645 /
1962 / 1008. The two review rounds are the whole difference, and no deletion was undone to produce
it.

➕ **The harness and data-suite misses share one cause: the targets summed the deletions and counted
nothing Task 3 adds.** Task 3 adds `ClonedArchiveTest.cs` (39 as added), `CloneSource.cs` (9) and
`BenchNames.cs` (19) as new files in `Integration/`, plus the leave-nothing-behind checks inside
`PostgresContainerFixture.DisposeAsync` and `ArchiveDatabase.ListClonesAsync` — additions against a
target built purely from subtraction. The harness therefore lands at 1075 in 15 files rather than
≤ 800 in ≤ 12, and `SemiPlot.Tests.Data`, which contains it, carries the same overshoot to 10332.
Every itemised deletion in Tasks 1 to 3 was made; nothing was skipped. Per file: `ArchiveDatabase`
144, `ArchiveDatabaseCollection` 11, `ArchiveProviderFactory` 54, `ArchiveReadSupport` 26,
`ArchiveTemplate` 81, `BenchNames` 19, `ClonedArchiveTest` 68, `CloneSource` 9, `DatabaseGate` 23,
`PostgresContainerFixture` 359, `PostgresServer` 50, `ProvisionerImage` 145, `ProvisionerResolution`
20, `SeededArchive` 34, `TestEnvironment` 32.

➕ **The two review rounds moved every one of these figures, and `SemiPlot.Tools.ArchiveSeeder` went
from met to missed.** At `9f025a3` the seeder was 1962 against its ≤ 2010 target; it is now 2051,
41 over. The 89 lines are `RawLayerGenerator.DescribeUnmarkableRun`, `SeederOptions.ValidateBreakMarkers`
and the `Program.Main` catch — each of them a review finding fixed, none of them optional, and the
generator's output is unchanged by all three (`StandardSliceDigest` and the 271 984 row count are
pinned and did not move). The harness rose 1008 → 1075 over the same two rounds:
`ClonedArchiveTest` 39 → 68 for the per-test clone assertions, `PostgresContainerFixture` 327 → 359
for the guarded container-id read and the pull bound, `PostgresServer` 44 → 50. The rule this section
states is to record the actual and name the cause, which is what the table above now does; no code
was trimmed to reach a threshold.

➕ **`SemiPlot.Tests.Journeys` misses by 15 because the target counted a header Task 1 forbade
deleting.** The ≤ 630 figure subtracted a 16-line header block and 21 lines of clone boilerplate. The
boilerplate went — `LiveEdgeArchiveJourneyTests` derives from `ClonedArchiveTest`, 22 lines net — but
Task 1's own item **keeps** that class's header, one of the four named there as stating facts the
code cannot. The two instructions contradict each other; the keep-the-header one was followed, so
645 is the correct outcome and 630 was never reachable.

➕ **The conventions round moved three of these figures, all upward, and all from fixes.**
`SemiPlot.Tools.ArchiveSeeder` went 2051 → 2090: `Program.RunSeedAsync` now narrows the
`ArgumentException` catch to the parse and the in-memory generation, which costs the explicit locals
and a `ReportUsage` helper but stops an Npgsql `ArgumentException` printing the CLI usage block. The
harness went 1075 → 1092 and `SemiPlot.Tests.Data` 10332 → 10397: `BenchNames` took the container's
credential pair in from `PostgresContainerFixture` (which fell 359 → 351), `ClonedArchiveTest` took
in the `Writer()` three classes repeated, and `TestEnvironmentTests` gained the `Image` theory its
own comment claimed existed. Nothing was deleted to reach a threshold and nothing added is optional.

➕ **The harness command's two counting modes differ and both are recorded.** `Measure-Object -Line`
drops empty strings, so the command as written reports **881**. The 1230 baseline was built from
per-file raw line counts, and the comparable raw figure is **1075**. The table above uses the raw
figure so before and after are measured the same way.

➕ **The comment-audit pass moved three of the figures down, by deletion only.** A comment audit found
the fix rounds had re-stated `docs/architecture/bench.md` inline — the "first and last run are the
tight ones" paragraph in four places, the one-lattice rule in four — and 42 comments were cut or
split back to what the code and that document do not already carry. No code changed except one false
statement in `ProvisionerImage.ResolveAsync`'s header, which claimed both catches below it filter
`OperationCanceledException` out while the catch that converts the pull bound catches it.
`SemiPlot.Tests.Data` went 10397 → 10298, `SemiPlot.Tools.ArchiveSeeder` 2090 → 2060, and the
harness 1092 → **1057** in the same 15 files (the `Measure-Object -Line` figure 881 → **860**).
`SemiPlot.Tests` and `SemiPlot.Tests.Journeys` are unchanged at 7011 and 645 — the journey suite lost
one clause inside a header it keeps. Test counts did not move: 362 / 503 / 4, 0 failed and 0 skipped
with `SEMIPLOT_REQUIRE_DB=1`, and `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

➕ **The final review round moved two of them back up, by addition only.** `SemiPlot.Tests.Data`
10298 → **10410** and `SemiPlot.Tools.ArchiveSeeder` 2060 → **2070**, which the table above carries.
The 122 lines are `SharedLatticeTests.cs` (92), the two `--end` option tests (20) and the restored
`End > DateTime.MaxValue.Date` bound with its header (10). The harness is untouched at 1057 in 15
files, and `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` stay at 7011 and 645.

➕ **The over-engineering cuts moved three figures down, by deletion only, and closed part of the
overshoot two earlier notes recorded.** `SemiPlot.Tests.Data` 10410 → **10239** and
`SemiPlot.Tools.ArchiveSeeder` 2070 → **2055**; the harness 1057 → **942** in the same 15 files (the
`Measure-Object -Line` figure 860 → **766**). `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` are
unchanged at 7011 and 645.

The two cuts account for every line. **The teardown leak audit** takes 142 out of
`SemiPlot.Tests.Data`: `PostgresContainerFixture` 334 → 246, which loses `DisposeAsync`'s
failure-collecting body, `ContainerAnswersAsync`, `CreatedContainerId` and the `StartedContainer`
property that existed only for a test; `ArchiveDatabase` 141 → 114, which loses `ListClonesAsync` and
`CloneNamesCommand`; and the two test files, `PostgresContainerFixtureTests` 133 → 117 and
`ArchiveDatabaseTests` 83 → 72. The audit went because the `SEMIPLOT_TEST_PG` path's removal made it
unreachable — the container is destroyed at the end of every run, so every clone dies with it and a
leaked clone survives nothing — and because no test ever proved it fires. Disposability now rests on
`WithCleanUp(true)` and the reaper, with
`TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository` as the tripwire; that test, the
`WithCleanUp(true)` call and the `semiplot.bench` label all stay. **`SeederOptions.ValidateBreakMarkers`**
takes the other 15 from `SemiPlot.Tools.ArchiveSeeder` (`SeederOptions` 252 → 237) and 29 from
`SeederOptionsTests` (293 → 264). `RawLayerGenerator.Generate` already refuses the same option pair
through `DescribeUnmarkableRun`, and `Program.RunSeedAsync` wraps parse and generation in one
`catch (ArgumentException)` that prints the usage block and exits 1, so the validator's only yield
was naming the two flags instead of the parameter — not worth running `BreakPlan.Create` inside
option parsing. `DescribeUnmarkableRun`, its `Generate` throw, the `Program.Main` catch and
`ValidateSpan`'s `End > DateTime.MaxValue.Date` bound are untouched. `StandardSliceDigest` and the
271 984 row count did not move.

3. **The planner floor, re-measured, is unchanged by the refactor.** `ExplainPlanTests`' five tests
   pass against the unchanged slice:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release --filter "FullyQualifiedName~ExplainPlanTests"
```

5 passed, 0 failed, 0 skipped. **Re-measured after the conventions round: 5 passed, 0 failed,
0 skipped.** The floor
itself is 509 rows in 4 `relpages` and the slice is
266 372 rows, so the margin is three orders of magnitude; the number is recorded so a later shrink
knows what it is spending.

4. **`SEMIPLOT_TEST_PG` appears nowhere outside the completed-plan record.**

```powershell
Select-String -Path (Get-ChildItem . -Recurse -File -Exclude *.md |
  Where-Object { $_.FullName -notmatch '\\(obj|bin|Artifacts|\.git)\\' }).FullName `
  -Pattern 'SEMIPLOT_TEST_PG|SEMIBASE_EXE'
```

No matches. In `.md`, matches remain only under `docs/plans/completed/` and
`docs/plans/roadmaps/`, which record decisions as they were made and are not edited.

**Re-measured on this commit's tree: no matches outside `.md`.** The `.md` matches are the six files under
`docs/plans/completed/`, the one under `docs/plans/roadmaps/`, this plan itself — which describes the
removal and moves to `completed/` in Task 6 — and the untracked
`docs/plans/20260828-test-suite-review.md`, which predates this work and is not part of it.

5. **A run leaves no dangling image and no container.**

```powershell
$before = (docker images --filter "dangling=true" --filter "label=semiplot.bench=1" -q).Count
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
$after = (docker images --filter "dangling=true" --filter "label=semiplot.bench=1" -q).Count
"bench dangling before=$before after=$after"
docker ps -a --filter "label=org.testcontainers=true" --format '{{.Names}} {{.Image}}'
```

`after` equals `before`; the container listing is empty.

➕ **Re-measured by hand after the teardown leak audit was cut, because nothing asserts it any
more.** Before the run: 0 dangling bench images, no `org.testcontainers=true` container. After it:
`after` equals `before` at 0, and within ten seconds of the run finishing the reaper container is
gone and the built `semiplot-bench:fc0cf5409512` with it. The unrelated `semiplot-bench:manual` tag,
which the application-bench recipe builds by hand, is present before and after and is not a test
artifact.

Two scoping choices, both load-bearing. The dangling count is filtered to the bench image's own
label — Task 3 adds `.WithLabel("semiplot.bench", "1")` to the image build — because
`ghcr.io/semiteq/semibase:latest` is a moving tag this repository tracks deliberately
(`docs/architecture/testing-strategy.md`), and a run that pulls a new manifest untags the old one
and raises the unfiltered count through no fault of this change. The container listing filters on
the Testcontainers label rather than `ancestor=semiplot-bench`, because `ancestor=` matches by image
reference: once the image is deleted a leaked container's ancestor resolves to a bare id and the
filter returns nothing whether or not anything leaked, which is a check that cannot fail.

Before this plan the bench-scoped count read 1 → 2 per run.

**Re-measured on this commit's tree: `bench dangling before=0 after=0`,** over a full
`SemiPlot.Tests.Data` run of 501 passed, 0 failed, 0 skipped. The conventions round did not touch the
image build or the teardown, and its own full run reports 503 passed, 0 failed, 0 skipped.

➕ **The container listing was not empty, and that is not a leak.** It held one row,
`testcontainers-ryuk-… testcontainers/ryuk:0.14.0`, `Up 37 seconds` — Testcontainers' own reaper,
which carries `org.testcontainers=true` and so matches the filter. The reaper removes itself once its
session closes, and an immediate re-check found the listing empty. No bench container survived the
run. The expectation holds for everything the fixture creates; the filter also catches the reaper for
as long as the reaper is still reaping.

6. **A broken provisioning is a bounded skip, not a hang.** Put `exit 1` on the line above the
   `/semibase bench` call in `SemiPlot/SemiPlot.Tests.Data/bench/provision.sh`, then:

```powershell
Remove-Item Env:\SEMIPLOT_REQUIRE_DB -ErrorAction SilentlyContinue
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release --filter "FullyQualifiedName~PostgresContainerFixtureTests"
```

Every test skipped with a stated reason inside two minutes, no test failed, no process left running.
The `Remove-Item` is not optional: items 1 and 5 set `SEMIPLOT_REQUIRE_DB` in this same session and
PowerShell keeps it, so without clearing it this item produces failures and reads as a broken change
rather than the bounded skip it is testing. Item 7 sets it again. Revert `provision.sh` afterwards. Measured on the current tree: 16.4 s, because the container exits;
the two-minute bound is what covers the container that comes up and never becomes ready.

**Re-measured on this commit's tree: 6 skipped, 0 passed, 0 failed, 14 s wall.** The class holds six
tests rather than the three it held at `9f025a3`; the three the review rounds added skip with the rest.
Each skip states its reason —
`no container runtime started a bench image over postgres:17-alpine: Container … exited with code 1.`
followed by the container log. No `SemiPlot.Tests.Data` or `testhost` process survived the run, and
the only container left labelled `org.testcontainers=true` was Testcontainers' own reaper, which
removes itself with its session. `provision.sh` was reverted with `git checkout --` and `git status`
reports the file clean.

➕ **This item does not measure the second review round's fix, and no item does.** `exit 1` fails the
provisioning at readiness, after the daemon has already created the container, so
`DockerContainer.Id` answers on this path and the unguarded read never threw here: the figures above
are identical before and after that fix. The path the guard covers is a start that fails at CREATE —
a name collision, a refused port allocation, a daemon out of resources — where the property throws,
the throw preceded the disposal, and xunit reported a collection-level fixture-disposal failure
instead of the bounded skip this item requires. That path is reproduced by no acceptance item, so the
guard carries a test of its own instead:
`PostgresContainerFixtureTests.TheContainerIdIsReadableWhetherOrNotTheDaemonCreatedTheContainer`
asserts both halves — a container the daemon never created throws on `Id` and answers `null` through
the guard, and the fixture's own started container still reports an id.

7. **`SEMIPLOT_REQUIRE_DB` still flips skip into fail.** With the Docker daemon stopped:

```powershell
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
Remove-Item Env:\SEMIPLOT_REQUIRE_DB
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
```

First run: the gated tests fail, each naming `SEMIPLOT_REQUIRE_DB`. Second run: the same tests skip
with a stated reason, 0 failed. This is the acceptance item that catches the always-skip mistake.

**Measured at `9f025a3`, daemon stopped with `docker desktop stop`, and not re-run since:** stopping
the daemon is the one item an unattended run cannot repeat safely, so the figures below are the
`9f025a3` suite of 478 and the split has moved with the 19 tests added since. What the item proves —
that the same tests move between failed and skipped and nothing else changes — is a property of
`DatabaseGate`, which neither review round touched.

| Run | `SEMIPLOT_REQUIRE_DB` | Failed | Passed | Skipped |
| --- | --- | --- | --- | --- |
| first | `1` | **84** | 394 | 0 |
| second | cleared | 0 | 394 | **84** |

The same 84 tests move between the two columns and nothing else changes. Each failure reads
`SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails instead of skipping: no container
runtime started a bench image over postgres:17-alpine: no Docker endpoint answered, so the
provisioner image cannot be fetched.` Both runs took 43 s, so the pull bound Task 3 added holds
against a dead daemon rather than waiting on it. Docker was restarted with `docker desktop start` and
confirmed back — server 29.7.2, 0 containers, `PostgresContainerFixtureTests` green again.

8. **`dotnet format` is clean and the pre-commit hook passes.**

```powershell
dotnet format SemiPlot.slnx --verify-no-changes
```

**Re-measured after the conventions round: exit 0, no output.** One caveat for a later run: the
IDE's own post-edit formatter reindents a wrapped string continuation differently from
`dotnet format`, so a file it touches has to be checked against this command before it is committed.

## Progress Tracking

- mark completed items `[x]` immediately when done
- add newly discovered tasks with ➕
- document blockers with ⚠️
- keep this plan in sync with the work actually done

## Solution Overview

**One path, one container, one template, one generator.**

`PostgresContainerFixture.InitializeAsync` starts a container, builds the template inside it, and
records an unavailable reason if either throws. It carries no `Result<T>`, no second branch and no
knowledge of a server it did not start. `ArchiveTemplate.Name` is the constant
`"semiplot_bench"`; nothing checks whether it exists, because a fresh container guarantees it does
not.

**The template is the one thing worth its cost.** Building it takes about a second and every test
class clones it in 0.28 s. That stays exactly as it is — it is not reuse across runs, it is one
seeding inside one run, which is what "raise the harness, test, kill everything" describes.

**Every clone-owning test class states three lines instead of twenty.** A
`ClonedArchiveTest(PostgresContainerFixture fixture, CloneSource source)` base holds the
`ArchiveDatabase?` field, the `?? throw`, `InitializeAsync` and `DisposeAsync` that nine classes
repeat today, at 199 lines of pure boilerplate.

**The seed generator adopts the follow generator's shape.** One lattice, `index * intervalTicks`,
anchored at absolute tick zero; one value function, `SyntheticValueWalk.Value(seed, penId, index,
min, max)`; one anchor rule, the previous value one poll interval before the change. A break plan
filters the lattice and marks the change on each side. What dies: `SeededRandom` (64 lines),
`PenTrace` (52 lines), and the walk inside `RawLayerGenerator` — `SegmentKind`, `NextKind`,
`EmitStep`, `EmitRamp`, `EmitSpike`, `NextInterval`, `ChangeInstant`, `AppendRun`, `AppendPen`,
`MarkRunBoundaries`, roughly 190 of its 291 lines. What survives: `SelectPens`,
`PollInterval` (`:5`), and `Generate`'s shell.

**What the merge buys, precisely.** The seed's lattice and the follow run's lattice become the same
lattice, so a follow run resuming at the archive edge continues it rather than approximating it.
`f91889d` and `caa935f` are the two bugs that came from them differing; a third of the same family
becomes unreachable rather than fixed.

**What the merge costs.** Two `RawLayerGeneratorTests` methods lose their subject —
`AnIdleSegmentEmitsNoRowsAndLeavesTheLevelUntouched` (`:110-131`) and
`ARampWritesOneRowPerTickWithNoPreAnchors` (`:133-154`) — because idle and ramp segments stop
existing. They are deleted, not rewritten. And `--change-seconds` stops being a mean and becomes an
exact interval, which the seeder's usage text has to say.

**Both waits get a two-minute bound**, and the pull gets one for the first time. A broken bench is
then a stated skip inside two minutes on every path, instead of an unbounded wait that hangs
`SemiPlot.Tests.Data.exe` and locks the next build with MSB3027.

**`WithCleanUp(false)` becomes `true`.** Ryuk then reaps the built image with the run. One thing
stays true and is said plainly: the images the fixture *pulls* still persist, because Ryuk labels
what Testcontainers creates and not what the registry served.

➕ **`bench/Dockerfile` is left alone.** An earlier draft of this sentence also removed the named
`FROM ${PROVISIONER_IMAGE} AS provisioner` stage in favour of a direct
`COPY --from=${PROVISIONER_IMAGE}`. Task 3's checklist rejects that on two measured grounds — a
global `ARG` is out of scope inside a build stage, and expanding an `ARG` in a `COPY --from=` flag is
BuildKit behaviour while the fixture builds through the classic builder — and the 436 MB dangling
image is the size of a whole bench image, which points at `WithCleanUp(false)` alone. The
implementation followed Task 3.

## Technical Details

### `PostgresContainerFixture` after the change

```csharp
public async ValueTask InitializeAsync()
{
    try
    {
        _postgresServer = await StartContainerAsync();
        _templateDatabase = await ArchiveTemplate.BuildAsync(_postgresServer, _creationGate);
    }
    catch (Exception exception) when (exception is not OperationCanceledException)
    {
        UnavailableReason =
            $"no container runtime started a bench image over {TestEnvironment.Image}: {exception.Message}";
    }
}
```

The `catch` takes every type but one. Testcontainers reports a missing or unreachable runtime through
several exception types and the distinction does not matter here — which the current comment on
`StartContainerAsync`'s catch already states. `OperationCanceledException` is the one exclusion,
because a cancelled run is the caller's outcome and not an unavailable runtime.

**That filter is only safe because no bound of this plan's own arrives here as a cancellation.** The
readiness bound raises `TimeoutException`, and the pull bound is converted inside `ResolveAsync`
into the failure `Result` that function already returns (below). A bound that escaped as
`OperationCanceledException` would pass straight through this filter and fail the whole collection
on a slow registry, which is the opposite of the bounded skip this task promises.

### The readiness and pull bounds

```csharp
.WithWaitStrategy(
    Wait.ForUnixContainer().UntilCommandIsCompleted(
        ProvisionedWaitCommand(),
        options => options.WithTimeout(_startupBound)))
```

with `private static readonly TimeSpan StartupBound = TimeSpan.FromMinutes(2);`, passed to
`ProvisionerImage.ResolveAsync` as a parameter so both waits read one field and cannot drift apart.

`ResolveAsync` bounds itself rather than being bounded from outside, because both of its catches
filter `when (exception is not OperationCanceledException)` and a bound applied from outside would
therefore escape as the one exception type nothing on the path holds:

```csharp
public static async Task<Result<ProvisionerResolution>> ResolveAsync(
    TimeSpan bound,
    CancellationToken cancellationToken = default)
{
    using var bounded = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);

    bounded.CancelAfter(bound);

    try
    {
        // unchanged body, passing bounded.Token to PullAsync and DescribeAsync
    }
    catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)
    {
        return Result.Fail<ProvisionerResolution>(
            $"'{Reference}' did not resolve within {bound}; the registry or the daemon is not answering.");
    }
}
```

The filter is what separates the two cancellations: the linked source fired on its own timer, so the
caller's token is still unset and the bound becomes a stated reason; a caller who really cancelled
sets that token and the exception propagates as before. The two existing catches keep their filters
untouched.

### `ArchiveTemplate` after the change

`Name` is `public const string Name = "semiplot_bench";`. `BuildAsync` clones
`SemibaseProvisioner.ProvisionedDatabase` into it, seeds it, and returns nothing — a failure is an
exception the fixture's one catch turns into an unavailable reason. `ComputeName`,
`ArchiveIsSeededAsync`, the `ExistsAsync` branch inside `BuildAsync` and the crashed-run repair
comment above it all go, and `System.Globalization`, `System.Security.Cryptography`, `System.Text`
and `FluentResults` leave the file's usings.

`ArchiveDatabase.CopyAsync` **stays a public method with two callers** — `ArchiveDatabase.CloneAsync`
and `ArchiveTemplate.BuildAsync`, which is the clone this task keeps. It is not inlined.

`ArchiveWriter.ArchiveIsSeededCommand` loses this consumer but keeps three others —
`ArchiveWriter.WriteAsync` itself (`ArchiveWriter.cs:60`) and
`ArchiveWriterTransactionTests.cs:85,94` — so the constant stays.

### `ClonedArchiveTest`

```csharp
public abstract class ClonedArchiveTest(PostgresContainerFixture fixture, CloneSource source)
    : IAsyncLifetime
{
    private ArchiveDatabase? _archiveDatabase;

    protected PostgresContainerFixture Fixture => fixture;

    protected ArchiveDatabase Database =>
        _archiveDatabase ?? throw new InvalidOperationException(
            fixture.UnavailableReason ?? "The archive was used before it was cloned.");

    public virtual async ValueTask InitializeAsync()
    {
        if (fixture.IsAvailable)
        {
            _archiveDatabase = source is CloneSource.Template
                ? await fixture.CloneTemplateAsync()
                : await fixture.CloneProvisionedAsync();
        }
    }

    public virtual async ValueTask DisposeAsync()
    {
        if (_archiveDatabase is not null)
        {
            await _archiveDatabase.DisposeAsync();
        }
    }
}
```

`CloneSource` is `Template | Provisioned`. Seven classes in `SemiPlot.Tests.Data` and one in
`SemiPlot.Tests.Journeys` derive from it: `ArchiveWriterTransactionTests` (`:50-72`),
`CoarseFlushTests` (`:74-100`), `FollowRestartTests` (`:44-78`), `RealtimeEmptyArchiveTests`
(`:41-61`), `RealtimePollReadTests` (`:60-92`), `RealtimeSubscriptionTests` (`:48-80`),
`StaleArchiveGuardTests` (`:21-41`), `LiveEdgeArchiveJourneyTests` (`:47-67`). Three of them do
per-class seeding after the clone and override `InitializeAsync`. `SeededArchive` (`:13-33`) is a
class fixture rather than a test class and keeps its own shape.

The base type lives in `SemiPlot.Tests.Data/Integration/`, which `SemiPlot.Tests.Journeys` already
references. **The reference direction stays one-way** — nothing in `SemiPlot.Tests.Data` may point
back at the UI projects.

### The merged generator

```csharp
public static IReadOnlyList<ArchiveRow> Generate(SeederOptions options);
```

keeps its signature and its place in `RawLayerGenerator`. Inside:

- the change lattice is `index * intervalTicks` over `[Start, End)`, with `intervalTicks` derived
  from `ChangeSeconds` the way `LiveTailGenerator.ChangeIntervalTicks` derives it — whole
  milliseconds, so the in-memory uniqueness check means what
  `PRIMARY KEY (id, l, t)` means;
- a change emits its anchor at `changeTicks - PollInterval.Ticks` carrying `ValueAt(index - 1)`,
  then the change itself carrying `ValueAt(index)`, exactly as `LiveTailGenerator.AppendPen` does,
  including its `carriesAnchor` guard for an interval wider than the anchor offset;
- a lattice point inside a break window emits nothing; the last change before a break carries
  `ArchiveRow.LastBeforeBreakQuality`, the first after it `ArchiveRow.FirstAfterBreakQuality`, and
  that first change emits no anchor — the plant moved while archiving was stopped;
- `BreakPlan.BuildWindows` (`BreakPlan.cs:66-83`) draws its duration and offset from
  `SyntheticValueWalk`'s hash over two coordinates below zero instead of from a `SeededRandom` stream,
  which is the last thing keeping that class alive.

`LiveTailGenerator.Generate` then calls the same row builder over `[from, toExclusive)` with no break
plan at all, and the two paths are one lattice by construction rather than by agreement.

**The single-row-run rule.** A run holding exactly one lattice point — a change interval wider than
the run — would need one row to be both the last change before a break and the first change after
one, and `Quality` carries a single code with no combination for the pair. The rule is therefore the
one the stateful walk already used: the single row keeps `FirstAfterBreakQuality`, and a second row
carrying the same value one poll interval later takes `LastBeforeBreakQuality`. The marker sequence
then stays a strict 32, 16 alternation of exactly two markers per break, which is what every reader of
a gap boundary relies on (`BreakGenerationTests.MarkersComeInOrderedStopThenResumePairs`).
`RawLayerGeneratorTests.ASingleRowRunBetweenTwoBreaksGetsASynthesisedStopRow` pins the case at 60
breaks in a day with `--change-seconds 600`, which reaches it 15 times.

Two bounds come with that rule and are stated rather than guarded. A run holding **no** lattice point
carries neither marker and breaks the alternation; it needs a change interval wider than
`BreakPlan.MinimumRun`, five minutes. And the synthesised row sits one poll interval after a lattice
point inside the run, so a point landing in the final 100 ms of a run would place it inside the break.
Neither is reachable at the standard slice's 5 s interval, and `MarkRunBoundaries` returns without
marking an empty run rather than throwing on it.

**The resume row moves onto the lattice.** The stateful walk opened every run with a row at the run's
own start, so the q = 16 row sat exactly on the break's end. A lattice point is not drawn where a break
boundary is, so the resume row is now the first lattice point at or after that end, within one change
interval of it. `BreakGenerationTests.EachMarkerPairBoundsOneBreakWindow` asserts that range in place
of the equality it asserted before.

**The row count rises and that is fine.** A 5 s lattice over a day gives 17 280 changes per pen, two
rows each, eight pens: 276 480 raw rows before the breaks, and **271 984 measured** against today's
229 862 — 314 845 across all four layers against 266 372. `StatementTimeoutReadTests`' 50 ms bound and
the minute-layer density test both sit further from their floors than they do now, and the five
`ExplainPlanTests` pass against the regenerated archive unchanged.

### The seeder's input bounds — a stated decision

`SeederOptions.Validate` runs four checks (`SeederOptions.cs:141`, `:169`, `:190`, `:212`).
Removing them is safe for the database: generation is fully in memory before a connection opens
(`Program.cs:34-40`), the partition DDL and the `COPY` are one transaction, and a non-advancing
span throws before any DDL. What is forfeited is a usage message instead of a stack trace on a
mistyped option.

**Recommendation: split them.** `ValidateSpan`'s two extreme-date branches —
`End > DateTime.MaxValue.Date` (`:145-148`) and `Days > latestDays` (`:158-164`) — defend against
`DateTime.MinValue` underflow,
which an operator reaches only by typing `--days 739617`. They go, and the four tests pinning them
go with them (`SeederOptionsTests.cs:217-229`, `:231-240`, `:242-248`, `:250-272`). `Days < 1`
(`:150-153`), `ValidatePenCount`, `ValidateChangeRate` and `ValidateBreaks` stay: each is reachable
by an ordinary typo — `--pens 60`, `--change-seconds 0`, `--break-count 100` — and each turns one
into a usage message rather than a stack trace out of a generator. The seeder is a command an
operator runs by hand, from `CLAUDE.md` and from `scripts/bench-demo.ps1`, so its usage text is part
of what it delivers.

**Cost of the half that goes:** `--days 739617 --end 2026-01-02T00:00:00` becomes an
`ArgumentOutOfRangeException` out of `SeederOptions.Start` instead of a usage line. Nobody types it.

➕ **Only the `--days` half went, and the `--end` bound came back.** The recommendation above treated
both branches as one decision and both costs as `SeederOptions.Start` underflow. They are not the
same. `--days 739617` does throw out of `Start`, inside `Validate`, which `Program.cs:39` catches and
answers with the usage block — `SeederEntryPointTests.ADayCountTheSpanCannotHoldExitsWithOneAndPrintsTheUsage`
pins it, so that half stays deleted and costs nothing. `--end 9999-12-31T23:59:59` throws nowhere
near the parse: it validates clean, generates clean, prints the plan header, and then dies with an
unhandled `ArgumentOutOfRangeException` out of `PartitionScript.CoveredDays`, called from
`Program.ReportPlan` past the end of that catch — and `ArchiveWriter.WriteAsync` calls
`PartitionScript.CreateStatements` outside its own `try` as a second landing spot. Widening the catch
to cover both would swallow an `ArgumentException` out of the writer, which `Program.cs:14-18` refuses
by name. So `End > DateTime.MaxValue.Date` is restored in `ValidateSpan`, with
`SeederOptionsTests.ParseRejectsAnEndInsideTheLastRepresentableDay` and
`ParseAcceptsTheLatestEndThatCanBePartitioned` going red without it.

### "Leave nothing behind" gets an assertion

`ArchiveDatabaseTests.TheDatabaseIsGoneAfterDisposal` (`ArchiveDatabaseTests.cs:37-49`) already
proves one clone is dropped. The promise this plan adds is the aggregate one, and it is asserted in
`PostgresContainerFixture.DisposeAsync`, before the container dies:

- count `pg_database` rows matching `ArchiveDatabase.ClonePrefix || '%'`. A non-zero count throws,
  naming the leaked databases. xunit reports a fixture disposal failure against the collection, so a
  leak is a red run rather than a quiet one.
- after the container is disposed, inspect the container id through the Docker client reachable via
  `TestcontainersSettings.OS.DockerEndpointAuthConfig` and throw if it still answers.

**The container is disposed in a `finally`, and the clone count is read in the `try` above it.**
Written the other way round the assertion's own failure skips the disposal and leaves the container
running — the exact leak this plan exists to remove, produced by the check that reports it. The two
throws are collected and reported together rather than the first one hiding the second.

Both checks run only when the fixture is available; on a machine with no runtime there is nothing to
leak and nothing to assert.

## What Goes Where

- **Implementation Steps**: the deletions, the `SEMIPLOT_TEST_PG` removal, the harness, the
  generator, their tests and the documentation.
- **Post-Completion**: the one check no automated test covers, and two follow-ups this work makes
  visible but must not carry.

## Implementation Steps

**Line numbers in the tasks below are read at `59afffe` and Task 1 shifts most of them.** Tasks 2, 3
and 4 therefore name symbols, not lines; where a line number survives in those tasks it is a reading
aid, and the symbol is what identifies the target. Verify each citation against the tree before
acting on it — a wrong "only caller" claim in an earlier draft of this plan would have broken the
build, and stale line numbers are this repository's recurring plan defect.

### Task 1: Delete what nothing reads

Pure deletion. No behaviour changes, no signature changes, and the golden digest constants are not
touched.

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SeederOptionsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/RawLayerGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/PartitionScriptTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SeederEntryPointTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/DatabaseGateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/TestEnvironmentTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ProvisionerImage.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabase.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs`
- Modify: the five file-header blocks named below

- [x] delete the six character-for-character SQL literal tests and their private literals in
      `ArchiveStatementTextTests.cs` — `:27-31` + `:33-37`, `:42-49` + `:51-55`, `:66-85` + `:87-91`,
      `:96-101` + `:103-107`, `:113-119` + `:121-125`, `:132-134` + `:136-140`
- [x] keep `TheDefaultPartitionOccupancyStatementReadsTheRelationTheWarningNames` (`:144-148`) and
      the three binder-parameter tests with their helper (`:151-202`)
- [x] add, in their place, one three-line assertion that `ArchiveStatements.SparseHistoryWindow` ends
      with `ORDER BY id, t;` — **Testing Strategy** states why this one survives while the five
      literals do not
- [x] move the "load-bearing" rationale the deleted literals carried onto the constants in
      `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`, where the statement text lives
- [x] add two more assertions beside the `ORDER BY id, t` one, for the same reason: that
      `SparseHistoryWindow`'s seam bound is strict `t < @from` (an inclusive bound returns the
      boundary row on both branches) and that its seed lower bound keeps
      `greatest(@to - @from, interval '1 day')`. Without these three, **every** pin on the shipped
      SQL sits behind Docker — `ExplainPlanTests` and `ArchiveHealthReadTests` are both gated, while
      `ArchiveStatementTextTests` is `Category=Unit` and runs on any machine today
- ➕ **Three more pins, added in the third review round: `RealtimePoll`'s `l = 0` and `ORDER BY t`,
      and `RealtimeBaseline`'s `l = 0`.** Deleting the two realtime literals left those three clauses
      pinned by nothing. No gated test catches their loss either: `RealtimePollReadTests`,
      `RealtimeSubscriptionTests` and `RealtimeEmptyArchiveTests` clone `CloneSource.Provisioned` and
      write raw rows only, so no coarse row exists for an `l = 0` filter to exclude;
      `LiveEdgeArchiveJourneyTests` clones the four-layer template, but every coarse row sits at or
      before `lastSeen`, where `t > @lastSeen` hides the difference; and `ExplainPlanTests` asserts
      plan shape alone. The ordering is unprovable by a read at all — one COPY returns rows in
      physical order, which is already ascending — so a clause pin is the only guarantee available
      for it, and the same form is used for the two filters rather than a gated test seeding a coarse
      row above `lastSeen`
- [x] delete the four extreme-`DateTime` option tests in `SeederOptionsTests.cs` — `:217-229`,
      `:231-240`, `:242-248`, `:250-272` — and the redundant `InlineData` rows at `:180`, `:189`,
      `:192`. ➕ `:181` (`--days 20000000`) goes with `:180`: both are rejected only by the
      `Days > latestDays` bound this task deletes, and without it each throws out of
      `SeederOptions.Start` instead of returning a failed `Result`
- [x] delete the matching bounds in `SeederOptions.ValidateSpan`: `:143-148` and `:155-164`. Keep
      `Days < 1` (`:150-153`) and the other three validators — **Technical Details** states the
      decision and its cost
      ➕ **`End > DateTime.MaxValue.Date` is back.** Deleting it left `--end 9999-12-31T23:59:59`
      dying with an unhandled `ArgumentOutOfRangeException` out of `PartitionScript.CoveredDays`,
      past the end of the `Program.cs:39` catch. Only the `Days > latestDays` half stays deleted —
      **Technical Details** carries the split
- [x] delete `RawLayerGeneratorTests.GenerateAcceptsAnEndAtTheLastRepresentableInstant`,
      `GenerateRejectsZeroDays`, `SelectPensRejectsMoreThanTheCatalogueHolds` and
      `IdenticalSeedsProduceIdenticalRows` — the last is subsumed by the golden digest, which stays
      untouched
      ➕ **Two of the four came back in the first review round (`af5c160`) and are in the tree.**
      `GenerateRejectsZeroDays` and `SelectPensRejectsMoreThanTheCatalogueHolds`
      (`RawLayerGeneratorTests.cs:213` and `:219`) guard `RawLayerGenerator`'s own argument checks,
      which no option test reaches: `SeederOptions.Parse` is not the only caller of the generator,
      and the digest pins output rather than refusal. Only
      `GenerateAcceptsAnEndAtTheLastRepresentableInstant` and `IdenticalSeedsProduceIdenticalRows`
      stay deleted
- [x] **keep `GenerateAcceptsAChangeIntervalAsLongAsTheWholeSpan` through Task 4.** It pins generator
      arithmetic, not option validation, and Task 4 rewrites exactly that arithmetic — an interval
      equal to the whole span is where the anchor either fits ahead of the first change or does not.
      Delete it, if at all, only after Task 4 is green
- [x] delete `PartitionScriptTests.TheStatementPassesThroughADayThatAlreadyHasItsPartition`
      (`:66-75`) and `TheStatementCarriesOnlyItsRenderedNameAndBounds` (`:77-90`) — both restate the
      literals already pinned at `:33-64`
- [x] delete `SeederEntryPointTests.ABreakCountLargerThanTheSpanHoldsIsRejectedWithTheUsage`
      (`:25-38`) and `DatabaseGateTests.TheSkipAndTheFailureAreDifferentOutcomes` (`:42-53`)
- [x] trim `TestEnvironmentTests`' theory rows to `"1"` → true, one other true case, `null` → false,
      one other false case, **and one whitespace-only row** — `TestEnvironment.Read`'s
      `IsNullOrWhiteSpace`/`Trim` also governs `Image`, so dropping every whitespace row leaves a
      blank `SEMIPLOT_PG_IMAGE` silently falling back to the default with nothing asserting it.
      **The test itself must survive** — it is the only assertion that `SEMIPLOT_REQUIRE_DB=1` flips
      skip into fail
- [x] **keep `ProvisionerImage.PullFailureLog` and `PullAsync`'s reading of the progress stream.** A
      pull can report its failure inside the stream instead of throwing; drop the log and that pull
      returns success, `InspectImageAsync` then succeeds against a **stale cached** image,
      `ProvisionerResolution.StalenessReason` is permanently `null`, and `WarnIfStale` becomes dead
      code. `InspectImageAsync` covers only the empty-cache case, which is not the case this exists
      for
- [x] **leave `ArchiveDatabase.CopyAsync` alone.** It has two callers — `ArchiveDatabase.CloneAsync`
      and `ArchiveTemplate.BuildAsync` — and Task 3 keeps the second, so it is not a single-caller
      indirection at any point in this plan
- [x] delete the five file-header comment blocks that only restate `bench.md` and
      `testing-strategy.md`: the ones on `SemibaseProvisioner`, `ProvisionerImage`, `PostgresServer`,
      `TestEnvironment` and `DatabaseGate`
- [x] **keep four header blocks that state facts the code cannot, and that later tasks depend on:**
      `LiveEdgeArchiveJourneyTests` (why nothing there waits on a timeout, and that the losing side
      of that race is a hung xunit v3 executable locking the next build with MSB3027/MSB3021);
      `PostgresContainerFixture` (initialisation never throws — the reason is captured and handed to
      `DatabaseGate`, which is the invariant Task 3's rewrite must not break); `SeededArchive` (why
      cloning is skipped when the runtime is unavailable, which `ClonedArchiveTest` must reproduce);
      `ArchiveTemplate` (that `CREATE DATABASE … TEMPLATE` carries table ownership, `relacl` and
      default privileges but not database `CONNECT`). CLAUDE.md admits comments for genuinely
      non-obvious logic and these four are it
- [x] keep the one-line "why" comments on pool clearing (`ArchiveDatabase.cs:67-68`), the creation
      gate (`PostgresContainerFixture.cs:65-67`) and the TCP wait
      (`PostgresContainerFixture.cs:253-255`) — each states a fact the code cannot
- [x] run all three suites — `SemiPlot.Tests` 362 unchanged, `SemiPlot.Tests.Data` about 475,
      `SemiPlot.Tests.Journeys` 4, 0 failed, 0 skipped. Record the actual `SemiPlot.Tests.Data`
      count; it is the baseline every later task's count is read against.
      **Actual: 362 / 478 / 4, all 0 failed and 0 skipped, with `SEMIPLOT_REQUIRE_DB=1`.**
- [x] confirm `RawLayerGeneratorTests.StandardSliceDigest` and `StandardSliceRowCount` are unchanged

### Task 2: Remove the `SEMIPLOT_TEST_PG` path and its documentation

One PR, code and documentation together, because the documentation is the only place the path is
described.

**Files:**
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinary.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseProvisioner.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresServer.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/TestEnvironment.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/DatabaseGate.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixtureTests.cs`
- Modify: `CLAUDE.md`, `docs/architecture/bench.md`, `docs/architecture/testing-strategy.md`

- [x] delete `SemibaseBinary.cs` outright, all 26 lines
- [x] delete `SemibaseProvisioner.ProvisionAsync`, both `RunAsync` overloads, `ObserveAsync` and
      `Describe` — `:44-177` — rather than simplifying them, plus `BenchCommand` (`:23`),
      `SuperPasswordVariable` (`:36`), `_runTimeout` (`:42`) and the four usings at `:1-6`
- [x] keep `ProvisionedDatabase`, `WriterRole`, `ReaderRole`, `WriterPasswordVariable` and
      `ReaderPasswordVariable` — they are the contract with the image's `provision.sh` and are read
      from six other files
- [x] rename the residual holder `SemibaseProvisioner` to **`BenchNames`**. It defines names, it runs
      nothing. The rename touches `ArchiveTemplate`, `PostgresContainerFixture` (six references),
      `PostgresServer`, `SeededArchiveTests` and `StatementTimeoutReadTests`. **Applied through the
      IDE rename refactoring: six files, ten call sites, no conflicts.** The file was renamed to
      `BenchNames.cs` with it, and the two prose references to the old name — the comment in
      `bench/provision.sh` and `bench.md` — were updated by hand
- [x] delete `PostgresContainerFixture.UseExistingServerAsync` and its comment (`:285-354`); collapse
      `InitializeAsync`'s branch (`:117-119`) to a single `StartContainerAsync()` call; make
      `Provisioner` non-nullable (`:83`) and drop the `null` argument at `:199`. `Provisioner` takes
      the `Server`/`TemplateDatabase` shape — a nullable backing field behind a non-nullable
      `?? throw` property — because an unavailable run assigns it nothing. `using Npgsql;` left the
      file with the deleted branch
- [x] delete `PostgresServer.SemibaseExecutable` (`:12`)
- [x] delete `TestEnvironment.TestServerVariable`, `TestServerConnectionString`,
      `SemibaseExecutableVariable`, `SemibaseExecutable`, `WriterPassword` and `ReaderPassword` —
      `:8-10`, `:16-17`, `:21-22`, `:25-30`
- [x] delete the now-unreachable skip branch in `PostgresContainerFixtureTests` (`:57-64`); rewrite
      `TheServerIsPostgresFourteenOrNewer`'s rationale (`:32-35`) to justify the floor against a
      custom `SEMIPLOT_PG_IMAGE`, or delete the test with it. **Rewritten, not deleted:**
      `SEMIPLOT_PG_IMAGE` still lets the operator choose the PostgreSQL version, so the floor keeps a
      subject and the assertion keeps its meaning
- [x] update `CLAUDE.md`: drop rows `:129`, `:132` and `:133` from the environment table, leaving
      `SEMIPLOT_PG_IMAGE` and `SEMIPLOT_REQUIRE_DB`; delete the `SEMIBASE_SUPER_PASSWORD` note
      (`:135-136`); rewrite `:138-142` for one path
- [x] update `docs/architecture/bench.md`: `:186-189` loses the two-path clause; `:219-225` — the
      definitive statement of the path — is deleted entirely; `:256-257` loses its last sentence;
      `:276-277` needs a new justification for the clone, because the one it gives dies with the path
- [x] update `docs/architecture/testing-strategy.md`: `:215` goes from two accepted exceptions to
      one; `:225-227` — the `SEMIBASE_EXE` exception — is deleted. Note in the commit that removing
      it makes the section's own rule at `:211-213` unconditionally true
- [x] run all three suites — counts unchanged from Task 1 except the fixture tests, 0 failed,
      0 skipped. **Actual: 362 / 478 / 4, all 0 failed and 0 skipped, with `SEMIPLOT_REQUIRE_DB=1`.**
      The fixture count did not move: the deletion took an unreachable branch out of a test, not a
      test out of the class
- [x] run Acceptance Evidence item 4 — no matches outside `.md`; in `.md` the only matches left are
      this plan, `docs/plans/completed/`, `docs/plans/roadmaps/` and `docs/plans/20260828-test-suite-review.md`,
      which is a point-in-time review record of the same kind as a completed plan and is not edited
- [x] item 7 deferred to Task 5 — it needs the Docker daemon stopped, which would break the rest of
      an unattended run

### Task 3: One harness, bounded and self-cleaning

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ProvisionerImage.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ClonedArchiveTest.cs`
- Modify: the eight clone-owning test classes named in **Technical Details**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabaseTests.cs`

- [x] replace `ArchiveTemplate.Name`'s digest with `public const string Name = "semiplot_bench";` and
      delete `ComputeName`, `ArchiveIsSeededAsync`, the `ExistsAsync` branch inside `BuildAsync` and
      the crashed-run repair comment above it
- [x] update `docs/architecture/bench.md`'s "one template per run, named after a hash" bullet and its
      sentence about removing accumulated `semiplot_bench_*` databases by hand — the constant name
      and the self-cleaning disposal both falsify it, and no other task owns this bullet
- [x] make `ArchiveTemplate.BuildAsync` return `Task` and throw on failure; delete every `Result` and
      `Result<T>` from the file and from `PostgresContainerFixture`
- [x] rewrite `PostgresContainerFixture.InitializeAsync` to the shape in **Technical Details**, with
      **one broad catch** filtered only on `OperationCanceledException` — safe only together with
      the self-bounding `ResolveAsync` below, which keeps this plan's own timeout from arriving here
      as a cancellation. A narrow catch turns a
      missing runtime into 87 failures; a wrongly broad capture turns the suite into a silent
      always-skip. Acceptance items 1 and 7 are what prove neither happened
- [x] bound the readiness strategy at two minutes with
      `UntilCommandIsCompleted(command, options => options.WithTimeout(_startupBound))`
- [x] bound `ProvisionerImage.ResolveAsync` at the same two minutes **inside `ResolveAsync`**, on
      the shape in **Technical Details**: a linked source, `CancelAfter(bound)`, and a
      `catch (OperationCanceledException) when (!cancellationToken.IsCancellationRequested)` that
      returns the failure `Result` the function already returns. Bounding it from outside instead
      makes the timeout escape as `OperationCanceledException` — the one type both existing catches
      filter out and the one type the fixture's catch also excludes — turning a slow registry into a
      red collection rather than a stated skip. The pull has no deadline of any kind today
- [x] set `WithCleanUp(true)` on the built image and add `.WithLabel("semiplot.bench", "1")` so
      Acceptance Evidence item 5 can count dangling bench images without counting the moving
      `semibase:latest` tag. If `WithDeleteIfExists(false)` then fights the reaper, drop it and say
      so in the commit. **`WithDeleteIfExists(false)` stays — it does not fight the reaper.** The
      reaper deleted both the tagged image and the exited container of the run
- [x] **accept the rebuild cost and correct the comment that denies it.** The comment on the image
      build currently reads that layers stay cached either way, which stops being true once the
      reaper deletes the tagged image every session — the build context is the test output
      directory copied `PreserveNewest`, so its mtimes change every rebuild and the `COPY` layer
      never caches. Disposability is the requirement; build time is not a constraint here. Measure
      the new per-run build once and record it in the commit. **Measured: 1.6 s for the image build,
      and the whole `SemiPlot.Tests.Data` run stayed at 43 s.** The same correction was applied to
      `bench.md`, which carried the identical claim ("a rebuild is a cache lookup")
- [x] **leave `bench/Dockerfile` alone.** `COPY --from=${PROVISIONER_IMAGE}` does not work here for
      two reasons the file itself states: `ARG PROVISIONER_IMAGE` is declared before the first
      `FROM`, so it is a global ARG and is out of scope inside the build stage unless re-declared;
      and expanding an `ARG` inside a `COPY --from=` flag is BuildKit behaviour, while the fixture
      builds through the Docker Engine API — the classic builder — which the file's own comment
      already carves out `--chmod` for. The measured 436 MB dangling image is the size of a whole
      bench image, which points at `WithCleanUp(false)` alone; the named provisioner stage
      references an already-pulled image and materialises nothing
- [x] add `ClonedArchiveTest` and derive the eight classes from it, removing the repeated field,
      `?? throw`, `InitializeAsync` and `DisposeAsync` — about 170 lines net of the new base type
- [x] **`LiveEdgeArchiveJourneyTests` keeps `BatchCollector`'s single whole-test subscription when it
      moves onto the base type.** Replacing it with a per-write `FirstAsync()` unsubscribes between
      awaits, so nothing observes a duplicate emitted in the gap and the exactly-once ledger stops
      proving anything. The failure mode is not a red test — it is an intermittent hang weeks later,
      in a suite whose executable locks the next build when it hangs
- [x] add the leave-nothing-behind checks to `PostgresContainerFixture.DisposeAsync`: no
      `semiplot_clone_%` database survives, and the container does not answer after disposal.
      **The container disposal goes in a `finally` and the clone count in the `try` above it** — the
      other order lets the assertion's own failure skip the disposal and leave the container
      running, producing the leak it reports
- [x] add a test in `ArchiveDatabaseTests` that a clone taken and disposed inside one test leaves no
      row in `pg_database` matching `ArchiveDatabase.ClonePrefix || '%'`
- [x] run all three suites — **362 / 479 / 4, all 0 failed and 0 skipped, with
      `SEMIPLOT_REQUIRE_DB=1`.** The Data count rose by the one test this task adds. Then Acceptance
      Evidence items 5 and 6: item 5 read bench dangling before=0 after=0 with an empty container
      listing once the reaper finished; item 6 skipped all three fixture tests with a stated reason
      in 18 s wall, 0 failed, nothing left running
- [x] confirm `Integration/`'s fourteen non-test files are now at most 800 lines in at most 12 files.
      **Missed, and recorded rather than forced: 1008 lines in 15 files.** The itemised work all
      happened; the target did not account for what this task adds. `ClonedArchiveTest` (39) and
      `CloneSource` (9) are two new files, `PostgresContainerFixture` grew 280 → 327 for the
      leave-nothing-behind checks and the bounds, and `ProvisionerImage` grew 124 → 145 for the
      self-bound `ResolveAsync`. Nothing unrelated was trimmed to reach the threshold. The two review
      rounds after this task took the harness to 1075; Acceptance Evidence item 2 carries the current
      figures

### Task 4: One generator

Last, because it re-pins the golden digest and changes the archive every generated expectation in the
gated suite is computed against. Doing it before Task 3 would leave a red suite ambiguous between the
two.

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs` (usage text only)
- Delete: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeededRandom.cs`
- Delete: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/PenTrace.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/RawLayerGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/BreakGenerationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticValueWalk.cs` ➕ the uniform `Fraction` draw
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowOptions.cs` ➕ the same usage-text correction
- Modify: `SemiPlot/SemiPlot.Tests.Data/LayerThinnerTests.cs` ➕ the four-layer volume assertion
- `SemiPlot/SemiPlot.Tests.Data/LiveTailGeneratorTests.cs` **unchanged**: every one of its tests pins
  the lattice the merged builder now serves both paths from, and all of them pass as written
- Add: `SemiPlot/SemiPlot.Tests.Data/SharedLatticeTests.cs` ➕ the guard on the merge itself. Every
  test named above reads one generator: `LiveTailGeneratorTests` the follow path, the golden digest
  the seeding path, `FollowRestartTests` the follow path on both sides of a restart. Nothing compared
  the two, so re-splitting the lattices left all three suites green until a seeded archive was
  followed and the `COPY` met a key it already held. The new file generates a seeding slice and a
  follow tick over the same span and asserts they are the same rows, then resumes from the seeded
  edge through `StaleArchiveGuard.StartFrom` and asserts the union carries no duplicate `(id, l, t)`,
  no seam wider than one change interval, and no row off the absolute lattice. Its own parameters
  (3 pens, `--change-seconds 60`, no break) keep it clear of the standard slice

- [x] move the row builder — lattice, anchor, value — into one place both
      `RawLayerGenerator.Generate` and `LiveTailGenerator.Generate` call, on the rules in
      **Technical Details**
- [x] give `BreakPlan.BuildWindows` a deterministic draw that needs no `SeededRandom` stream.
      `BuildWindows` makes **two** draws per index — one for duration, one for offset — so it needs
      two coordinates, and both must be uniform on `[0, 1)`: `SyntheticValueWalk.Hash` is private,
      and its public `Value` is `0.6 sin + 0.25 sin + 0.15 jitter`, concentrated near the middle, so
      durations drawn from it would cluster at about 6.5 minutes instead of spreading over the
      three-to-ten-minute range. Expose the hash as an internal uniform `Fraction(seed, coordinate)`
      and draw `(seed, -1 - 2*index)` and `(seed, -2 - 2*index)`. Keep `MinimumDuration`,
      `MaximumDuration`, `MinimumRun`, `MaximumBreaks`, `BuildRuns` and `Window` as they are.
      **The distribution was measured rather than assumed:** 80 000 drawn durations over 20 000 seeds
      fall in seven one-minute buckets of 11 317 to 11 613 each, mean 6.496 min — flat across the
      stated three-to-ten-minute range
- [x] delete `SeededRandom.cs` and `PenTrace.cs`, and the walk inside `RawLayerGenerator` —
      `SegmentKind`, `NextKind`, `EmitStep`, `EmitRamp`, `EmitSpike`, `NextInterval`,
      `ChangeInstant`, `AppendRun`, `AppendPen`, `MarkRunBoundaries`
- [x] keep `SelectPens` and `PollInterval` unchanged; `SelectPens` decides which
      pens the whole suite reads and its round-robin is load-bearing
- [x] change `--change-seconds`' usage line in `SeederOptions.Usage` (`:56`) from "Mean interval
      between value changes" to the exact interval it now is
- [x] delete `RawLayerGeneratorTests.AnIdleSegmentEmitsNoRowsAndLeavesTheLevelUntouched` (`:110-131`)
      and `ARampWritesOneRowPerTickWithNoPreAnchors` (`:133-154`) — idle and ramp segments stop
      existing, and a rewritten test would assert a shape the generator no longer has
- [x] re-pin `StandardSliceDigest` and `StandardSliceRowCount` (`:17-18`) to the new generator's
      output. **This is the expected outcome of this task and of no other.** Digest
      `b1291512fa0a3e962bcdf79db65bea46ab07d365fc2f528c2bf9e7b719627428`, row count **271 984**
      against 229 862
- [x] **state and verify the single-row-run rule.** `RawLayerGeneratorTests`'
      `ASingleRowRunBetweenTwoBreaksGetsASynthesisedStopRow` covers a run holding exactly one lattice
      point, where one row must be both the last change before a break and the first after one. The
      stateful walk synthesises a stop row for it; a uniform lattice reaches the same case whenever
      `--change-seconds` exceeds a run's width, and the merged generator's break rules as written
      say nothing about it. Decide the rule — one row carrying both markers, or a synthesised
      second — write it into **Technical Details**, and keep this test.
      **Decided: a synthesised second row**, one poll interval later and carrying the same value,
      which keeps the marker sequence a strict 32, 16 alternation. Written into **Technical Details**
      with its two stated bounds. The test keeps its subject at `--change-seconds 600` and 60 breaks,
      where a uniform lattice reaches the case 15 times; 120 s no longer reaches it at all, because
      every run between two breaks is at least 10 minutes wide
- [x] verify the four preserved properties directly, each in the test file that owns it: two rows per
      change, `q = 32` / `q = 16` markers, four populated layers, and the row count at or above
      266 372. `RawLayerGeneratorTests.EveryChangeRowFollowsItsPredecessorByExactlyOnePollInterval`
      and `BreakGenerationTests` cover the first two unchanged; the layers and the volume gained
      `LayerThinnerTests.TheStandardSliceFillsFourLayersAtOrAboveTheMeasuredVolume`, which reads
      **314 845** rows across the four layers
- [x] run all three suites, then Acceptance Evidence item 3 — the five `ExplainPlanTests` must pass
      against the new archive unchanged. **Actual: 362 / 478 / 4, all 0 failed and 0 skipped, with
      `SEMIPLOT_REQUIRE_DB=1`; item 3 read 5 passed, 0 failed, 0 skipped.** The Data count fell by the
      two deleted generator tests and rose by the one volume test this task adds.
      `SemiPlot.Tools.ArchiveSeeder` is 1962 lines against its ≤ 2010 target. The two review rounds
      after this task took it to 2051, 41 over; Acceptance Evidence item 2 records the miss and its
      cause

### Task 5: Verify acceptance criteria

- [x] run every command in **Acceptance Evidence**, in order, and record the output. **All eight
      items were run; the actual result of each is recorded beside its expectation above**
- [x] item 6 requires editing `provision.sh` and reverting it — confirm `git status` is clean
      afterwards. **Reverted with `git checkout --`; `git status` reports the file clean**
- [x] item 7 requires stopping the Docker daemon — confirm both halves, the failure and the skip.
      **Both confirmed: 84 failed and 0 skipped with the variable set, 0 failed and 84 skipped with
      it cleared. Docker was restarted and confirmed back up**
- [x] if any item fails, fix it in the task that owns it and re-run the whole list. **No item found a
      defect. Item 2's three line-count misses are recorded with their cause rather than reached by
      trimming something unrelated, which acceptance item 2 states is the required treatment**
- ➕ **Re-run after the second review round.** The round's fixture change lands on the exact path
      item 6 exercises, so items 1 to 6 and 8 were run again on this commit's tree and every figure
      above was re-measured: 362 / 497 / 4 with 0 failed and 0 skipped, item 2's table at
      7011 / 10332 / 645 / 2051 with the harness at 1075 in 15 files, item 3 at 5 passed, item 4 with
      no match outside `.md`, item 5 at `before=0 after=0` with no bench container left, item 6 at
      5 skipped in 22 s with `provision.sh` reverted clean, item 8 at exit 0. Item 7 was not re-run:
      it needs the daemon stopped, which an unattended run must not do
- ➕ **Re-run after the third review round.** The round adds four tests — three statement-text pins
      and the container-id guard's own test — so items 1, 5 and 6 were measured again: 362 / 501 / 4
      with 0 failed and 0 skipped, item 5 at `before=0 after=0` with only Testcontainers' reaper left
      in the container listing, and item 6 at 6 skipped in 14 s with `provision.sh` reverted clean.
      Item 2's line counts move with the four added tests and were not re-taken; what item 2 measures
      is the size of the harness and of the deletions, which this round does not touch
- ➕ **Re-run after the final review round.** The round adds four tests — the two in
      `SharedLatticeTests` guarding the shared lattice and the two in `SeederOptionsTests` guarding
      the restored `--end` bound — so items 1 and 2 were measured again on this commit's tree with
      `SEMIPLOT_REQUIRE_DB=1`: **362 / 507 / 4**, 0 failed and 0 skipped, and
      `dotnet format SemiPlot.slnx --verify-no-changes` exits 0. Item 2's table moves to
      7011 / **10410** / 645 / **2070**, the harness unchanged at 1057 in 15 files: the data suite
      carries the 92-line `SharedLatticeTests.cs` and 20 lines of option tests, and the seeder the
      10 lines of the restored bound. Items 3 to 8 were not re-run — the round touches no fixture,
      no provisioning path and no generator output, and `StandardSliceDigest` with its 271 984 rows
      is unmoved

### Task 6: [Final] Update documentation

- [x] `CLAUDE.md`, **Gated data tests**: the environment table holds two rows; state that a container
      runtime is the only requirement and that a run leaves nothing behind
- [x] `CLAUDE.md`, **Test**: the three-project split and its rationale are unchanged and stay as they
      are; the paragraph on the hung executable stays, and gains the two-minute bound as the thing
      that now prevents the container half of it
- [x] `docs/architecture/bench.md`: **The test bench** and **Where the provisioning comes from**
      describe one path; add what the run cleans up and what it deliberately does not — pulled images
      persist, because Ryuk labels what Testcontainers creates and not what the registry served
- [x] `docs/architecture/bench.md`, **What the generator emits**: one generator, one lattice, and
      `--change-seconds` as an exact interval
- [x] `docs/architecture/testing-strategy.md`, **What is pinned, and by what**: one accepted
      exception, and the golden digest re-pinned to the merged generator
- [x] record the measured planner floor — 509 rows in 4 `relpages`, knife-edged — beside the standard
      slice in `bench.md`, so the next person considering a smaller slice knows what the margin is
      and which two tests bind above it
- [x] move this plan to `docs/plans/completed/` — ⚠️ **deferred, deliberately.** Archiving is
      delivery work that runs after the operator has tested the branch; moving the file here would
      break the review and stats phases, which read the plan in place. The checkbox is marked because
      Task 6 has nothing else left to do, and the move itself belongs to delivery.

## Post-Completion

**Executed by exec:**
- branch: simplify-the-test-harness

**Manual verification**

Run `scripts/bench-demo.ps1` end to end and switch the chart through Raw, Minute, Hour and Day. The
merged generator changes every value the stand draws, and no automated test looks at the picture. A
curve that reaches the right edge on every layer, with breaks rendered as breaks, is the check.

**Named follow-ups, not part of this plan**

**Microsoft.Testing.Platform belongs in its own plan.** Moving the three suites to the MTP runner
unlocks `Microsoft.Testing.Extensions.HangDump` with a timeout, which addresses the
hung-executable-locks-the-build problem `CLAUDE.md` documents from the other side: this plan bounds
the container waits, and a hang anywhere else stays unbounded. It does not belong here. It changes
the runner for all three projects and all four CI jobs at once, it is orthogonal to every cut above,
and mixing it in makes a red CI leg ambiguous between "the harness change broke it" and "the runner
change broke it". File it separately.

**`Testcontainers.PostgreSql` is used as the generic `Testcontainers`.** The module's wait strategy
is replaced (`PostgresContainerFixture.cs:189`), `GetConnectionString()` is never called, and every
connection string comes from `PostgresServer`. The plain package with a `ContainerBuilder` and three
`POSTGRES_*` environment entries drops one dependency and one false promise to the reader. Small,
and independent of everything above.

**`TrendChartViewModelTests` is 1017 lines in one class.** Not a cut and not this plan's work, but a
signal: split it by behaviour — pens, history, realtime, navigation — and ask whether
`TrendChartViewModel` is carrying too much.

## Verify it yourself

```powershell
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet build SemiPlot.slnx -c Release
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release --no-build
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release --no-build
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj -c Release --no-build
dotnet format SemiPlot.slnx --verify-no-changes
docker images --filter "dangling=true" -q | Measure-Object
```

Three suites green, 0 skipped, the formatter silent, and the dangling count where it was before the
run.

The counts that command must produce: **362 / 507 / 4, 0 failed and 0 skipped**. Anything skipped is
a failed acceptance — an always-skipping suite is the worst outcome of this work, which is why the
run below re-proves the gate itself.

### The four checks the suite cannot make about itself

**1. A broken provisioning is a bounded skip, not a hang.** Put `exit 1` on the line above the
`/semibase bench` call in `SemiPlot/SemiPlot.Tests.Data/bench/provision.sh`, then:

```powershell
Remove-Item Env:\SEMIPLOT_REQUIRE_DB -ErrorAction SilentlyContinue
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release --filter "FullyQualifiedName~PostgresContainerFixtureTests"
```

Every test skipped with a stated reason inside two minutes, nothing left running. Measured on this
branch: 6 skipped, 0 failed, 14 s. Revert `provision.sh` and confirm `git status` is clean.
Clearing the variable first is not optional — the commands above set it, PowerShell keeps it, and
without clearing it this check produces failures instead of the skips it is testing.

**2. The gate still turns a skip into a failure.** With the Docker daemon stopped:

```powershell
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
Remove-Item Env:\SEMIPLOT_REQUIRE_DB
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
```

First run: the gated tests fail, each naming `SEMIPLOT_REQUIRE_DB`. Second: the same tests skip with
a stated reason, 0 failed. Measured at `9f025a3`: 84 failed, then 84 skipped, 43 s for both halves.
Start the daemon again afterwards.

**3. The run leaves nothing behind.** Scope the count to the bench image's own label, because
`ghcr.io/semiteq/semibase:latest` is a moving tag and pulling a new manifest untags the old one:

```powershell
$before = (docker images --filter "dangling=true" --filter "label=semiplot.bench=1" -q).Count
$env:SEMIPLOT_REQUIRE_DB = "1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
$after = (docker images --filter "dangling=true" --filter "label=semiplot.bench=1" -q).Count
"bench dangling before=$before after=$after"
docker ps -a --filter "label=org.testcontainers=true" --format '{{.Names}} {{.Image}}'
```

`after` equals `before`. The container listing may hold the Testcontainers reaper for a few seconds
after the run — that is Ryuk removing itself, not a leak; re-run the listing and it is empty. Before
this branch the same commands read 1 to 2 per run, one 436 MB dangling image every time.

**4. The lattice guard actually fires.** The branch's headline claim is that seeding and following
share one timestamp lattice by construction. `SharedLatticeTests` is what holds it, and a guard
nobody has seen fail is not a guard:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release --filter "FullyQualifiedName~SharedLatticeTests"
```

Two passed. To see it bite, give `LiveTailGenerator` a private window builder that draws the lattice
from the span start instead of absolute tick zero — the historical re-split — and re-run: both tests
fail, the first with `Collections differ ↓ (pos 0)` and the second with `sits off the absolute
lattice`. Revert with `git checkout --`.

### What the automated suite proves and a person does not have to

The seeder's own output is pinned by `RawLayerGeneratorTests.StandardSliceDigest`, unchanged through
five review rounds and independently reproduced twice; the two defects the generator merge could
have brought back — the primary-key collision of `f91889d` and the seam hole of `caa935f` — are each
asserted directly in `SharedLatticeTests`. Neither needs a person.
