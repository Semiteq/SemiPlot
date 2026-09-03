# Testing strategy — what each test owns, and who owns each piece of the bench

This document answers two questions that keep getting confused: what kind of test a given file is,
and which party owns each piece of infrastructure the tests stand on. The bench itself — the seeder,
the container fixture, the template-and-clone lifecycle, the gating environment — is described in
`bench.md`; this document says what the tests built on it are *for*.

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

1. **What must exist on the machine for this test to run?** `TestEnvironment` and `DatabaseGate`
   encode exactly this for the gated suite.
2. **What can make it fail other than a defect in this repository's code?**
3. **What does a failure name?**

## Unit tests

A unit test crosses no boundary it does not own. Everything it touches is deterministic, in-process
and built from the commit under test. Its value is diagnosis.

| Area | Files |
| --- | --- |
| Decimation, navigation, scale, cursor geometry | `SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs`, `Core/Trends/TrendNavigationModelTests.cs`, `PenScaleModelTests.cs`, `MinimapGeometryTests.cs`, `Chart/CursorReadoutModelTests.cs`, `DeltaCursorModelTests.cs` |
| The seeder's generation rules | `SemiPlot.Tests.Data/LayerThinnerTests.cs`, `RawLayerGeneratorTests.cs`, `BreakGenerationTests.cs`, `PartitionScriptTests.cs` |
| The demo writer's own rules and the command line | `SemiPlot.Tests.Data/LiveTailGeneratorTests.cs`, `SharedLatticeTests.cs`, `SeederCommandTests.cs`. Its coarse flush is not here: the selection is a statement the server executes, so `Integration/CoarseFlushTests.cs` carries it, gated |
| Error construction and extent arithmetic | `SemiPlot.Tests.Data/Errors/DataErrorTests.cs`, `Data/ArchiveExtentTests.cs` |
| The provider's statement text and its binder | `SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs` |
| The live edge's own rules, and the fresh tail's bound | `SemiPlot.Tests.Data/Postgres/RealtimePollTests.cs`, `Postgres/FreshTailBoundTests.cs` |
| The vendor's observed row shape | `SemiPlot.Tests.Data/Fixtures/RealArchiveFixtureTests.cs` over `Fixtures/real-archive-rows.csv` |

The last row is the one that misleads. A test reading a committed CSV is still a unit test: the file
is data, versioned by git, and cannot change underneath the test. Touching a file is not crossing a
boundary.

A unit test must not open a socket, read the wall clock, or depend on anything the machine resolves —
`PATH`, an installed service, a display. It runs everywhere, ungated.

Statement text is pinned clause by clause, in `ArchiveStatementTextTests.cs` against the constants in
`ArchiveStatements.cs`: one assertion per guarantee whose loss nothing else catches without a
container — the sparse history window's outer `ORDER BY id, t`, its strict seam bound and its one-day
seed floor; the realtime poll's `l = 0` filter and its `ORDER BY t`; the realtime baseline's `l = 0`
filter and its `DISTINCT unnest(@ids)`. Three statements take parameters, each through a binder of
its own pinned against the statement's parameter names: `PostgresDataProvider.BindWindow`,
`RealtimePoll.BindPoll` and `RealtimePoll.BindBaseline`. `data-integration.md` names the constants
and quotes no SQL, so there is no second copy to drift.

## Integration tests

An integration test crosses exactly one boundary to a real implementation of something this
repository does not build, to verify the translation across that seam. A fake cannot do this job: it
would encode our own assumption on both sides. The value is contract verification.

There are three families — the same category with different foreign parties.

**Against a real PostgreSQL** — `SemiPlot.Tests.Data/Integration/`: `PostgresCatalogReadTests`,
`PostgresExtentReadTests`, `PostgresHistoryReadTests`, `RealtimePollReadTests`,
`RealtimeSubscriptionTests`, `RealtimeEmptyArchiveTests`,
`ArchiveWriterTransactionTests`, `CoarseFlushTests`,
`ExplainPlanTests`. Seams guarded: statement text, type mapping, the demo writer's server-side
thinning against `LayerThinner`'s own selection,
the naive-local-to-UTC conversion, partition pruning, and the grant chain — reads run as
`semiplot_reader`, so a privilege that never reached the reader fails here instead of at
commissioning. The container is the delivery mechanism for a real server, nothing more.

**Against a real Avalonia** — `SemiPlot.Tests/UI/`, under `[AvaloniaFact]`:
`ChartPointerInputTests`, `MinimapPointerInputTests`, `TrendChartViewTests`, `TrendCoordinatorTests`
with `FakeDataProvider`. Seams guarded: the dispatcher, layout, hit-testing, pointer capture and
event routing. Real framework, synthetic data. These are what catch a rendering-stack version bump.

**Against a real rasterizer** — `SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs`, a plain `[Fact]`
with no Avalonia: it renders through SkiaSharp and asserts on pixels that a `NaN` column breaks the
line.

An integration test must not cross a second foreign boundary in the same assertion, and must not
exercise the production composition root. The moment it does either, a failure stops naming a seam.

## End-to-end tests

An end-to-end test crosses several boundaries through the production composition, with the assertion
at the far end. Its value is an existence proof that parts which each pass their seam tests actually
connect. Its cost is that a failure names nothing, so they stay few and thin — the seams carry the
coverage, and the journeys only prove the chain is closed.

`SemiPlot.Tests.Journeys` holds them — four tests in three classes over a container-backed
archive, each closing a chain whose halves are already proved apart:

| Test | Chain it closes | The seam tests it joins |
| --- | --- | --- |
| `ArchiveHarnessSmokeTests` | the seeded template clones across the assembly boundary and answers an extent read through `AddPostgresData` | the harness itself, before a journey depends on it |
| `BreakRenderArchiveJourneyTests` | a seeded break reaches the canvas: archive → `AddPostgresData` → `TrendCoordinator` → `TrendChartViewModel` → the pixels the rasteriser leaves blank | `PostgresHistoryReadTests` counts the fold's NaN anchor; `ChartGapRenderTests` measures the blank pixels a NaN column leaves |
| `LiveEdgeArchiveJourneyTests` | two: a row appended while the application runs reaches the chart's live edge and reaches it once; and rows on a variable of its own reach the chart without breaking a pen that has no sample at that timestamp | `RealtimeSubscriptionTests` asserts the first rule over the provider alone; `TrendCoordinatorTests` and `TrendChartViewModelTests` cover the per-variable batch shape above it |

The composed path adds what those seam tests cannot see: the coordinator's buffering and folding,
the chart view model's applier, the navigation controller. Each can lose a sample or replay one without
the provider noticing, and the journey is what fails when one does.

Two things in the repository are adjacent to this category without being in it.
`SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests.cs` and `UI/Di/CompositionRootTests.cs` test
the production wiring with no real edges; they are composition tests. The application bench in
`bench.md` is a genuine end-to-end procedure whose runner is a person, with its evidence read from
`pg_stat_user_tables` and the log rather than from a screen.

A test that starts a container is an integration test when it interrogates one seam, and an
end-to-end test only when the container feeds the composed application. `PostgresHistoryReadTests`
builds its provider through the real `AddPostgresData` registration, but that is one layer's wiring
and the assertion sits on rows: integration.

## Where the boundaries between projects fall

Three projects, and the line between them is the dependency graph rather than the kind of test.

| Project | Holds | References |
| --- | --- | --- |
| `SemiPlot.Tests` | everything touching the UI, plus the renderer-agnostic Core models | `SemiPlot.UI` |
| `SemiPlot.Tests.Data` | the bench and the data-source tests, and never the UI | `SemiPlot.Core`, `SemiPlot.DataSource.Postgres`, `SemiPlot.Tools.ArchiveSeeder` |
| `SemiPlot.Tests.Journeys` | the end-to-end journeys, which need the UI and a container at once | `SemiPlot.UI`, `SemiPlot.Tests.Data` |

The durable reason for the first line is the dependency graph. `SemiPlot.Tests.Data` references only
Core, the provider and the seeder, so the data suite — the one iterated against a container — builds
and runs without Avalonia, ScottPlot and SkiaSharp. An xunit v3 test project is one executable, so
keeping them apart keeps the container lifecycle and the Avalonia dispatcher in separate processes,
where a hung UI test cannot wedge the harness. Each project keeps its own assertion style
(AwesomeAssertions in `SemiPlot.Tests` and `SemiPlot.Tests.Journeys`, raw `Assert.` in
`SemiPlot.Tests.Data`), and `SemiPlot.DataSource.Postgres` names `SemiPlot.Tests.Data` alone in
`InternalsVisibleTo`.

All three target plain `net10.0`, so all three build on the Linux runner and the target framework
separates nothing.

The reference direction is one-way. `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` may reference
`SemiPlot.Tests.Data` and consume its container harness. The reverse reference would build, and must
not exist: it would put Avalonia, ScottPlot and SkiaSharp into the data suite and its Linux job.

The split also decides skip-versus-fail per project. `SemiPlot.Tests` holds no gated test — every
test in it runs on any machine with the SDK — so a skipped test there is a mistake rather than a
stated absence, and `SemiPlot/SemiPlot.Tests/xunit.runner.json` sets `failSkips` to turn one into a
failure on both CI legs. `SemiPlot.Tests.Data` keeps its skips: `DatabaseGate` states a reason when
a runtime is missing, and `SEMIPLOT_REQUIRE_DB` is what a pipeline sets to make that a failure. It
carries no `xunit.runner.json`, and must not gain one. `SemiPlot.Tests.Journeys` is gated end to end
and follows the same policy, so it carries none either.

**That policy is why the journeys are a project rather than a folder.** A journey needs the UI,
which `SemiPlot.Tests.Data` may not reference, and it is gated on a container runtime, which
`SemiPlot.Tests` turns into a failure. `failSkips` is a project-wide setting with no per-test scope,
so a gated journey inside `SemiPlot.Tests` would either fail every machine without a runtime or cost
that project its skip guard for every ungated test in it. A third project is the shape that keeps
both properties.

CI has two jobs: `unit-windows` runs `SemiPlot.Tests` on `windows-latest`, the platform the viewer
ships on, and `linux` builds the solution once on `ubuntu-latest` and runs all three projects, the
runner's Docker daemon serving the fixture. No job sets `SEMIPLOT_REQUIRE_DB`: the Windows runner
cannot host a Linux container and runs no gated project, and the Linux runner has the daemon.

## Ownership

Each piece lives with the party whose change invalidates it.

| Piece | Owner | Lives in | Why this boundary |
| --- | --- | --- | --- |
| Archive schema, layers, thinning rule | Simple-Scada 2 | the vendor's product; observed in `scada-archive.md` | SemiPlot is a strict read-only consumer. The observation is documented with the consumer because the consumer depends on it, not because anyone here controls it |
| Instance provisioning: database, roles, grants, default privileges, `semiplot_tags`, `public.trends` | SemiBase | `github.com/Semiteq/SemiBase` | the instance is shared by the SCADA, SemiPlot and future readers. The bench must be provisioned by the same implementation a site is, or it stops testing the grant chain. The archive table is in that list because SemiBase creates it: a second definition here would be the one exercised daily while the real one decayed |
| `semibase` artifact formats and versions | SemiBase | its release workflow and its published image | the producer owns its artifacts; SemiPlot only consumes them |
| Synthetic data model, including `LayerThinner` — this project's hypothesis about the vendor's thinning rule | SemiPlot | `SemiPlot.Tools.ArchiveSeeder` | the hypothesis couples to the consumer, not the provisioner: if the rule is refuted, the *read path* changes and SemiBase changes nothing. It must version in lock-step with the code that bets on it, which is why `RawLayerGeneratorTests` lives beside it and why `SyntheticValueWalk`, `SyntheticPenCatalog` and `SyntheticPen` are the seeder's own: the tests pin the generator's shape and later slices develop against its output, so a generator shared with anything evolving for its own reasons would break them |
| Test harness, gate policy | SemiPlot tests | `SemiPlot.Tests.Data/Integration/`, consumed by `SemiPlot.Tests.Journeys` | skip-versus-fail is consumer CI policy; no other party can decide it |
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
SemiBase's floor produces — which the fixture's unavailable reason then names as a container that
exited non-zero. And the provisioner a run built over is the one `docker image inspect` names for
the tag on that machine, so a failure that follows a moved tag can be tied to the digest it moved to.
