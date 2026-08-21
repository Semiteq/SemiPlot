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
| Error construction and extent arithmetic | `SemiPlot.Tests.Data/Errors/DataErrorTests.cs`, `Data/ArchiveExtentTests.cs` |
| The vendor's observed row shape | `SemiPlot.Tests.Data/Fixtures/RealArchiveFixtureTests.cs` over `Fixtures/real-archive-rows.csv` |

The last row is the one that misleads. A test reading a committed CSV is still a unit test: the file
is data, versioned by git, and cannot change underneath the test. Touching a file is not crossing a
boundary.

A unit test must not open a socket, read the wall clock, or depend on anything the machine resolves —
`PATH`, an installed service, a display. It runs everywhere, ungated.

## Integration tests

An integration test crosses exactly one boundary to a real implementation of something this
repository does not build, to verify the translation across that seam. A fake cannot do this job: it
would encode our own assumption on both sides. The value is contract verification.

There are three families — the same category with different foreign parties.

**Against a real PostgreSQL** — `SemiPlot.Tests.Data/Integration/`: `PostgresCatalogReadTests`,
`PostgresExtentReadTests`, `PostgresHistoryReadTests`, `StatementTimeoutReadTests`,
`ArchiveWriterTransactionTests`, `ExplainPlanTests`. Seams guarded: statement text, type mapping,
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

`SemiPlot.Tests` holds everything touching the UI plus the renderer-agnostic Core models.
`SemiPlot.Tests.Data` holds the bench and data-source tests and never references the UI.

The durable reason for the split is the dependency graph. `SemiPlot.Tests.Data` references only Core,
the provider and the seeder, so the data suite — the one iterated against a container — builds and
runs without Avalonia, ScottPlot and SkiaSharp. An xunit v3 test project is one executable, so
keeping them apart keeps the container lifecycle and the Avalonia dispatcher in separate processes,
where a hung UI test cannot wedge the harness. Each project keeps its own assertion style
(AwesomeAssertions and raw `Assert.` respectively), and `SemiPlot.DataSource.Postgres` names
`SemiPlot.Tests.Data` alone in `InternalsVisibleTo`.

A second constraint holds while `SemiPlot.Tests` targets `net10.0-windows`: it cannot build on the
Linux runner that is the only one able to start a container. That constraint is removable and the
reasons above are not, so the graph is what the split rests on.

`SemiPlot.Tests` may reference `SemiPlot.Tests.Data` and consume its container harness. The reverse
reference is the one that cannot exist.

## Ownership

Each piece lives with the party whose change invalidates it.

| Piece | Owner | Lives in | Why this boundary |
| --- | --- | --- | --- |
| Archive schema, layers, thinning rule | Simple-Scada 2 | the vendor's product; observed in `scada-archive.md` | SemiPlot is a strict read-only consumer. The observation is documented with the consumer because the consumer depends on it, not because anyone here controls it |
| Instance provisioning: database, roles, grants, default privileges, `semiplot_tags` | SemiBase | `github.com/Semiteq/SemiBase` | the instance is shared by the SCADA, SemiPlot and future readers. The bench must be provisioned by the same implementation a site is, or it stops testing the grant chain |
| `semibase` artifact formats and versions | SemiBase | its release workflow | the producer owns its artifacts; SemiPlot only names versions |
| Synthetic data model, including `LayerThinner` — this project's hypothesis about the vendor's thinning rule | SemiPlot | `SemiPlot.Tools.ArchiveSeeder` | the hypothesis couples to the consumer, not the provisioner: if the rule is refuted, the *read path* changes and SemiBase changes nothing. It must version in lock-step with the code that bets on it, which is why the golden digest lives beside it and why the seeder holds verbatim copies rather than referencing another project |
| Test harness, gate policy | SemiPlot tests | `SemiPlot.Tests.Data/Integration/` | skip-versus-fail is consumer CI policy; no other party can decide it |
| Developer environment | SemiPlot | `dotnet test` and the bench recipe in `bench.md` | it composes the others' artifacts and defines none of them |

Two rules follow from the first two rows and are not negotiable, restating
`data-integration.md`: SemiPlot never writes to vendor objects, and every additive object is prefixed
`semiplot_`.

## What is pinned, and by what

Three mechanisms pin three kinds of thing, and Docker is one of them rather than the definition of
correctness.

| Kind | Mechanism |
| --- | --- |
| Code in this repository — the seeder, the provider, the models | git: a project reference means the code under test *is* the commit |
| Third-party libraries | NuGet versions in `Directory.Packages.props`, the SDK in `global.json` |
| Dependencies with an independent release cycle — PostgreSQL, `semibase` | a container image |

A dependency resolved from the machine — an executable found on `PATH`, a service that happens to be
installed — is pinned by nothing, and that is the property to avoid. It is a separate property from
process isolation, and it is the one that matters here.

One dependency does not yet meet the rule: the `semibase` binary is resolved from `SEMIBASE_EXE` or
`PATH`, so on a developer machine whichever build happens to be installed is the one that
provisions. CI does not share the gap — it downloads a named release. The row above states the
target, not the present.
