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

Three projects, split by dependency graph and skip policy.

| Project | Framework | References | Holds |
| --- | --- | --- | --- |
| `SemiPlot.Tests` | xunit v3 + `Avalonia.Headless.XUnit` | `SemiPlot.UI` | Everything touching the UI, plus the renderer-agnostic Core models |
| `SemiPlot.Tests.Data` | xunit v3 | `SemiPlot.Core`, `SemiPlot.Tools.ArchiveSeeder`, `SemiPlot.DataSource.Postgres` | Bench and data-source tests, pure and container-gated. Never Avalonia |
| `SemiPlot.Tests.Journeys` | xunit v3 + `Avalonia.Headless.XUnit` | `SemiPlot.UI`, `SemiPlot.Tests.Data` | End-to-end journeys, which need the UI and a container at once. Gated end to end |

- `SemiPlot.Tests` carries `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]`. Pure logic
  uses plain `[Fact]`; tests touching ReactiveUI/ScottPlot/Avalonia use `[AvaloniaFact]`/`[AvaloniaTheory]`.
- `SemiPlot.Tests.Data` never references Avalonia, ScottPlot or SkiaSharp, so its `data-tests` CI job
  builds without them. `SemiPlot.DataSource.Postgres` names it as the sole `InternalsVisibleTo` assembly.
- The reference direction is one-way: `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` may reference
  `SemiPlot.Tests.Data`; the reverse would build and must not exist.
- `SemiPlot.Tests` sets `failSkips` in `xunit.runner.json`, so no gated test may live there. The
  journeys are a third project for that reason; it and `SemiPlot.Tests.Data` carry no `xunit.runner.json`.
- An xunit v3 test project is an executable: a hung test leaves `SemiPlot.Tests.exe` locked and the
  next build fails with MSB3027 until it is killed. The container half is bounded at two minutes.
- A plain `[Fact]` body runs with no `SynchronizationContext`, so an `await` on a
  `TaskCompletionSource` completed by production code resumes inline on the completing thread. A gate
  awaited by the test and completed by production code takes
  `TaskCreationOptions.RunContinuationsAsynchronously`; one awaited by production code and completed
  by the test must not, because tests assert on the inline resumption. `[AvaloniaFact]` bodies are
  unaffected.

```powershell
dotnet test SemiPlot.slnx                                                 # full suite, all three
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj                 # UI and Core models
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj       # bench and data source
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj  # end-to-end journeys
dotnet test SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
```

CI runs `SemiPlot.Tests` on `windows-latest` and `ubuntu-latest`, `SemiPlot.Tests.Data` and
`SemiPlot.Tests.Journeys` on `ubuntu-latest` with `SEMIPLOT_REQUIRE_DB=1`. A Windows-only API fails
the Linux leg only once a test executes the call (`CA1416` is a warning); a Windows path used as a
string does not fail it at all. The skip-versus-fail policy is in `docs/architecture/testing-strategy.md`.

Test traits: `[Trait("Component", "Core|UI")]`, `[Trait("Area", "Data|Bridge|Chart|Di")]`,
`[Trait("Category", "Unit|Integration")]`. Every test class carries all three.

Assertions split by project: `SemiPlot.Tests` and `SemiPlot.Tests.Journeys` use AwesomeAssertions
(`.Should()`) exclusively; `SemiPlot.Tests.Data` uses raw xunit `Assert.` exclusively. Tests over
provider errors assert by error type and structured field, never on message wording.

### Gated data tests

The integration tests in `SemiPlot.Tests.Data` and every test in `SemiPlot.Tests.Journeys` need a
container runtime and nothing else: `SemiPlot.Tests.Data/bench/Dockerfile` copies `/semibase` out of
`ghcr.io/semiteq/semibase:latest`, which the fixture pulls ahead of the build, and runs
`semibase bench` from `/docker-entrypoint-initdb.d/` before the published port opens. The container
carries fixed dummy passwords of the fixture's own. A missing runtime is a skip with a stated reason,
never a pass.

| Variable | Effect | Unset means |
| --- | --- | --- |
| `SEMIPLOT_PG_IMAGE` | Base image the bench image is built over, so it selects the PostgreSQL version | `postgres:17-alpine` |
| `SEMIPLOT_REQUIRE_DB` | `1` or `true` turns an unavailable runtime from a skip into a failure. CI sets it; a developer machine must not | skip with a reason |

A run leaves nothing behind but the images it pulled: `WithCleanUp(true)` and the resource reaper
remove the built image, the container and every database.
`TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository` is the tripwire.

The container provisions `semiplot_provisioned`; the seeded template `semiplot_bench` is a clone of
it filled once per run, and every test database is a `CREATE DATABASE ... TEMPLATE` clone of one of
the two. `CloneSource` names which: a class that reads the seeded rows takes the template, a class
that writes its own rows takes the provisioned source. `SeededArchive` gives one clone to a class,
`ClonedArchiveTest` one per test method. `docs/architecture/bench.md` holds the full statement.

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
- No ceremonial `ArgumentNullException.ThrowIfNull` on APIs only this repository calls; the nullable
  annotations are the contract. Guard only inputs that cross a process or file boundary.

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

- Only for genuinely non-obvious business logic, one or two lines. English only.
- Never restate what a `docs/architecture/*` document, a test, or a neighbouring member already says;
  where a document holds the reasoning, leave a bare `docs/architecture/<file>.md#<anchor>` pointer.
- No process notes (`// TODO`, `// in new version`), no test names in production code, no changelog
  phrases ("the fix", "was moved", commit hashes), no CAPS for stress, no narration of alternatives
  rejected. Rationale for a value goes in the commit message.
- Internal and private members get at most a one-line `<summary>`; multi-paragraph XML documentation
  belongs to nothing in this repository.

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
  the absolute lattice, the break holes, the row-pair shape — not by a digest. **One lattice serves
  both generators**: a change sits at `index * intervalTicks` from absolute tick zero, and
  `RawLayerGenerator` and `LiveTailGenerator` both emit through `RawLayerGenerator.AppendWindow`.
  `SemiPlot.Tests.Data/SharedLatticeTests.cs` goes red if they are split. `public.trends`,
  `semiplot_tags`, the two roles and their grants are SemiBase's; the seeder fills the archive table
  and creates only the day partitions its rows land in (`docs/architecture/bench.md`).
- The provider runs no cold-path reader: a failed read is mapped by `ArchiveExceptionMapper`, which
  stays synchronous, pure and unit-testable, and nothing opens a second connection to enrich the
  error. Each read supplies the one relation its statement touches, which the detail line names.

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
