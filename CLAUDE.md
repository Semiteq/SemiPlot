# Agent Instructions for SemiPlot

SemiPlot is a trend/chart viewer for an industrial installation (semiconductor plasma
process tools: ICP / RIE / PECVD). It reads live tags and historical archives from
Simple-Scada 2 and renders interactive, multi-axis trends.
Platform: .NET 10, Windows, C# 14. UI: Avalonia 12.0.5 desktop (Win32 + Skia + HarfBuzz) with ReactiveUI
for MVVM and ScottPlot.Avalonia (SkiaSharp) for rendering — no WPF, WebView2, or JS frontend.
Solution: `SemiPlot.slnx`. All commands run from repository root.

## Build

```powershell
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj    # recommended (entry executable)
dotnet build SemiPlot.slnx                     # all projects
dotnet run   --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj
dotnet format SemiPlot.slnx                    # pre-commit hook enforces this
```

The bench seeder fills a `semibase bench`-provisioned database with a generated archive. The
provisioning creates `public.trends`, so the seeder requires the table and refuses a database that
already carries rows or day partitions. It issues no `DROP` anywhere:

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Database=semiplot_dev;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Database=semiplot_dev;Username=postgres;Password=<super>" `
  --end 2026-01-02T00:00:00 --days 1 --pens 8 --seed 1
```

`--connection` and `--end` are required; `--end` carries no time zone, so two runs of the same seed
produce the same archive. `--admin-connection` is optional and only fills `semiplot_tags`, which
`scada_writer` holds no privilege on. Run it with `--help` for the option list.

## Test

Tests live in three projects, split by dependency graph and skip policy rather than by taste.

| Project | Target | Framework | References | Holds |
| --- | --- | --- | --- | --- |
| `SemiPlot.Tests` | `net10.0` | xunit v3 + `Avalonia.Headless.XUnit` | `SemiPlot.UI` | Everything touching the UI, plus the renderer-agnostic Core models |
| `SemiPlot.Tests.Data` | `net10.0` | xunit v3 | `SemiPlot.Core`, `SemiPlot.Tools.ArchiveSeeder`, `SemiPlot.DataSource.Postgres` | Bench and data-source tests, pure and container-gated. Never Avalonia, never the UI |
| `SemiPlot.Tests.Journeys` | `net10.0` | xunit v3 + `Avalonia.Headless.XUnit` | `SemiPlot.UI`, `SemiPlot.Tests.Data` | The end-to-end journeys, which need the UI and a container at once. Gated end to end |

`SemiPlot.Tests` carries `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]`. Pure logic
(decimation, navigation, scale, cursor, delta) uses plain `[Fact]`; tests touching
ReactiveUI/ScottPlot/Avalonia use `[AvaloniaFact]`/`[AvaloniaTheory]`. The Core model tests build
against the UI project, so they do not run independently of the UI build — the accepted cost of
keeping one Avalonia project.

**Why the split exists.** All three are on xunit v3 and all three target plain `net10.0`, so neither
the test framework nor the target framework separates them: `dotnet test` runs each in its own
process only because they are three projects. The reason there are three is the dependency graph and
the skip policy.
`SemiPlot.Tests.Data` references only `SemiPlot.Core`, `SemiPlot.DataSource.Postgres` and
`SemiPlot.Tools.ArchiveSeeder`, so the data suite and its `data-tests` CI job build and run without
Avalonia, ScottPlot and SkiaSharp. An xunit v3 test project is one executable, so keeping them apart
keeps the container lifecycle and the Avalonia dispatcher in separate processes, where a hung
UI test cannot wedge the harness. `SemiPlot.DataSource.Postgres` also names `SemiPlot.Tests.Data` as
the sole assembly in its `InternalsVisibleTo`.

**The reference direction is one-way.** `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` may take a
project reference on `SemiPlot.Tests.Data` and consume its container harness; the reverse reference
would build, and must not exist — it would drag Avalonia, ScottPlot and SkiaSharp into the data suite
and its Linux job.

**A journey fits neither of the other two**, which is the whole reason for the third project. It
needs the UI, which `SemiPlot.Tests.Data` may not reference, and it is gated on a container runtime,
which `SemiPlot.Tests` turns into a failure — `failSkips` is project-wide with no per-test scope, so
one gated test there would either fail every machine without a runtime or cost the project its skip
guard. `SemiPlot.Tests.Journeys` therefore carries no `xunit.runner.json`, and must not gain one.

**An xunit v3 test project is an executable.** A test that hangs leaves `SemiPlot.Tests.exe` running
and locked, and the next build fails with MSB3027/MSB3021 until that process is killed. The
container half of that is bounded: the registry fetch and the readiness wait both stop after two
minutes, so a bench that never comes up is a stated skip rather than a wedged process. Everything
else is not. A plain `[Fact]` body also runs with no `SynchronizationContext` — v3 installs one only
under the Aggressive parallel algorithm — so an `await` on a `TaskCompletionSource` that production
code completes resumes inline on the completing thread. A test gate awaited by the test body and
completed by production code therefore takes `TaskCreationOptions.RunContinuationsAsynchronously`; a
gate awaited by production code and completed by the test body must not, because tests assert on
that inline resumption. `[AvaloniaFact]` bodies are unaffected: `Avalonia.Headless.XUnit` installs
`AvaloniaSynchronizationContext`.

```powershell
dotnet test SemiPlot.slnx                                                 # full suite, all three
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj                 # UI and Core models
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj       # bench and data source
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj  # end-to-end journeys
dotnet test SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot.slnx --filter "Category=Unit"
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
```

**`SemiPlot.Tests` runs on both platforms in CI:** `build-and-test` on `windows-latest` and
`ui-tests-linux` on `ubuntu-latest`. `SemiPlot.Tests.Data` runs on `data-tests` and
`SemiPlot.Tests.Journeys` on `journey-tests`, both `ubuntu-latest` and both with
`SEMIPLOT_REQUIRE_DB=1`. The Linux leg proves two things: `SemiPlot.UI` and
`SemiPlot.Tests` compile there, and every test passes under the headless platform. A Windows-only
API fails that leg only once a test executes the call — the compiler reports it as the `CA1416`
warning and no project turns warnings into errors. A Windows path used as a string does not fail it
at all, which is why `StartupOptions`' `C:\DISTR\` defaults and the tests asserting on them are
green there. The skip-versus-fail policy of each suite is in
`docs/architecture/testing-strategy.md`.

Test traits: `[Trait("Component", "Core|UI")]`, `[Trait("Area", "Data|Bridge|Chart|Di")]`,
`[Trait("Category", "Unit|Integration")]`. Every test class carries all three; `SemiPlot.Tests.Data`
is `Component=Core` throughout, since nothing in it touches the UI, and `SemiPlot.Tests.Journeys` is
`Category=Integration` throughout.

**Assertions split by project, deliberately.** `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` use
AwesomeAssertions (`.Should()`) exclusively. `SemiPlot.Tests.Data` uses raw xunit `Assert.`
exclusively and references no assertion library: its assertions are mostly `Assert.Equal`, `Assert.Contains` and `Assert.All`
over rows and database state, where the fluent form buys no diagnostic the raw form does not already
give. Keep each project on its own style rather than mixing the two inside one file.

**Tests over provider errors assert by error type and structured field, never on exact message
wording** — the message is built in the error's base constructor and stays free to change.

### Gated data tests

The integration tests in `SemiPlot.Tests.Data` and every test in `SemiPlot.Tests.Journeys` need a
container runtime and nothing else. The
provisioner rides in as an image layer: `SemiPlot.Tests.Data/bench/Dockerfile` copies `/semibase`
out of `ghcr.io/semiteq/semibase:latest` onto the base image and runs `semibase bench` from
`/docker-entrypoint-initdb.d/`, over the unix socket, before the published port opens. A missing
container runtime is reported as a skip with a stated reason, never as a pass. Environment carries
the policy:

| Variable | Effect | Unset means |
| --- | --- | --- |
| `SEMIPLOT_PG_IMAGE` | Base image the bench image is built over (`--build-arg BASE_IMAGE`), so it selects the PostgreSQL version | `postgres:17-alpine` |
| `SEMIPLOT_REQUIRE_DB` | `1` or `true` turns an unavailable runtime from a skip into a failure. The CI `data-tests` job sets it; a developer machine must not | skip with a reason |

There is one path and it is the container. The suite never spawns a `semibase` binary and reads no
role password from the environment: the container carries fixed dummy passwords of the fixture's own,
so a developer needs no variable at all.

**A run leaves nothing behind.** It raises the harness, tests, and kills everything it made — the
built image, the container, the template and every clone. That rests on `WithCleanUp(true)` and the
resource reaper: the fixture's teardown only disposes the container, and everything the run created
lives inside it. `TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository` is the tripwire on
the reaper's label. The images the run *pulled* stay, on purpose. `docs/architecture/bench.md` holds
the full statement.

The container provisions one fixed database, `semiplot_provisioned`, before the published port opens;
the fixture passes that name in, so no consumer carries a branch. Everything else the suite needs is
a `CREATE DATABASE ... TEMPLATE` clone made inside the running container, of one of two sources.
The seeded template `semiplot_bench` is itself a clone of the provisioned source, filled once per
run; `CloneSource` names which of the two a test class clones. `SeededArchive` and
`LiveEdgeArchiveJourneyTests` take the template, because they read the seeded rows. The seven
writing classes — `ArchiveWriterTransactionTests`, `CoarseFlushTests`, `FollowRestartTests`,
`RealtimeEmptyArchiveTests`, `RealtimePollReadTests`, `RealtimeSubscriptionTests` and
`StaleArchiveGuardTests` — take `CloneSource.Provisioned` and write their own rows into an empty
`public.trends`. The grain differs with the holder: the `SeededArchive` class fixture gives one clone
to a whole class, while `ClonedArchiveTest` gives one per test method, since xunit constructs a test
class once per test method.

## Code Style

### General

- SOLID, DRY, KISS, YAGNI. Each method does one thing; each class one purpose.
- Prefer better naming over comments.

### File Layout

- One class per file. File-scoped namespaces: `namespace SemiPlot.Core.Trends;`
- `using` directives above the namespace. `System` namespaces first, blank line, then others.
- Never inline full namespace paths — use `using` directives.

### Size Limits

- Class: prefer 300 lines. Method: prefer 50 lines.

### Naming

| Element                           | Convention                     | Example                          |
| --------------------------------- | ------------------------------ | -------------------------------- |
| Public types, methods, properties | PascalCase                     | `TrendViewer`, `QueryAsync()`    |
| Interfaces                        | I-prefix                       | `IDataProvider`                  |
| Private fields                    | `_camelCase`                   | `_dataProvider`                  |
| Class instance fields             | `_className` (no abbreviation) | `_trendViewer`, `_dataProvider`  |
| Constants                         | PascalCase                     | `MaxPenCount`                    |
| Local variables                   | camelCase                      | `penIndex`                       |

No abbreviations in names.

### Formatting

- Tabs, size 4. Max line length 120 characters.
- Braces on new line, even for single-line statements.
- Expression-bodied members only for simple properties and indexers.

### Types and `var`

- Always `var` for local declarations.
- Predefined types: `int`, `string` (not `Int32`, `String`).

### Nullability

- Nullable reference types enabled. Avoid nulls in public APIs.
- Use `?.` and `??`. Do not suppress warnings with `!` without a verified reason.

### Dependency Injection

- Constructor injection only (primary constructors preferred). No property injection, no service locator.
- Register services in extension methods, each named for what it registers: `AddPostgresData()` in
  `SemiPlot.DataSource.Postgres`, `AddUi()` in `SemiPlot.UI`. A data-source project names its own
  source rather than a bare `AddData()`, so a composition root referencing several
  `SemiPlot.DataSource.*` projects names the one it registers. Core registers nothing.
- Avoid mutable static state.
- `AddPostgresData()` registers the bare data `IScheduler` (`DefaultScheduler.Instance`). The UI
  scheduler is not a second container
  registration: capture `AvaloniaScheduler.Instance` (= `RxApp.MainThreadScheduler`) in the
  `.AfterSetup(...)` callback after `UseReactiveUI()` and pass it explicitly to the coordinator
  constructor and the chart/minimap factories.
- Startup work that touches no Avalonia type belongs in `Program`, ahead of `BuildAvaloniaApp()`,
  not in `.AfterSetup(...)`: that callback is synchronous, so a data read inside it either blocks
  Avalonia's setup or throws through it. `StartupProbe` reads there and hands `App.Run` a
  `StartupData` record; the UI scheduler is why the split lands exactly at that boundary
  (`docs/architecture/data-integration.md`).

### Interface Design

- Create an interface when: 2+ implementations exist, the class is mocked in tests, it crosses
  an architectural layer boundary, or it implements Strategy/Factory.
- Do not create an interface for a single concrete class with no extension plans, or for POCOs/DTOs.
- Interfaces belong on the consumer side.

### Comments

- Only for genuinely non-obvious business logic. No process notes (`// TODO`, `// in new version`).
- English only.

### UI (Avalonia / ScottPlot)

- MVVM via ReactiveUI: VMs derive from `ReactiveObject`; use `WhenAnyValue`/OAPH/`ReactiveCommand`
  over the one shared `MainThreadScheduler`. Each view owns a `.axaml` + `.axaml.cs` pair.
- ScottPlot is a thin render target: renderer-agnostic logic (navigation, scale, cursor) lives in
  unit-tested Core models; only views touch `AvaPlot`. The data hub (`TrendCoordinator`) feeds the
  chart VM via `IObservable`/awaitables (see `docs/architecture/data-integration.md`).
- The left-button gesture is one state, never overlapping branches: a `Chart/LeftButtonTool`
  (`Pan | DeltaPlacement`) enum sourced from the toolbar delta toggle decides pan vs delta placement,
  and the axis-region edit is a pre-branch ahead of it. Toolbar `IsSticky` has a single writer (the
  `WindowChanged` handler refreshing from `Navigation.IsSticky`) — do not reintroduce imperative
  `IsSticky =` assignments.

### Data-source projects

- `IDataProvider` + its DTOs stay in `SemiPlot.Core`; every concrete provider lives in its own
  `SemiPlot.DataSource.*` project (`SemiPlot.DataSource.Postgres` is the only one). Core must not
  reference a data-source project; a further provider slots in as a sibling without touching Core.
- `MinMaxDecimator` lives in `SemiPlot.Core/Trends` beside `PenHistoryEnvelope` and is shared by every
  provider; each provider translates its own rows into the decimator's input vocabulary, which
  `docs/architecture/charting.md` states.
- The bench seeder `SemiPlot.Tools.ArchiveSeeder` owns `SyntheticValueWalk`, `SyntheticPenCatalog`
  and `SyntheticPen`. `RawLayerGeneratorTests` pins the generator by determinism and invariants —
  the absolute lattice, the break holes, the row-pair shape — not by a digest, so the waveform may
  change and the shape may not. **One lattice serves both generators.** A change sits at
  `index * intervalTicks` from absolute tick zero and both `RawLayerGenerator` (run by run between
  the breaks) and `LiveTailGenerator` (span by span) emit through `RawLayerGenerator.AppendWindow`.
  Do not split them again: two lattices is what produced the primary-key collision in `f91889d` and
  the seam hole in `caa935f`, and `SemiPlot.Tests.Data/SharedLatticeTests.cs` is the test that goes
  red if they are. `docs/architecture/bench.md` states what the shape guarantees.
  `public.trends`, `semiplot_tags`, the two roles and their grants are SemiBase's and are never
  defined in this repository — the seeder fills the archive table and creates only the day
  partitions its rows land in.
- The provider runs no cold-path reader: a failed read is mapped by `ArchiveExceptionMapper`, which
  stays synchronous, pure and unit-testable, and nothing opens a second connection to enrich the
  error. Each read supplies the one relation its statement touches, which the detail line names.

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
