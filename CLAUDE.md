# Agent Instructions for SemiPlot

SemiPlot is a trend/chart viewer for an industrial installation (semiconductor plasma
process tools: ICP / RIE / PECVD). It reads live tags and historical archives from
Simple-Scada 2 and renders interactive, multi-axis trends.
Platform: .NET 10, Windows, C# 14. UI: Avalonia 11.3.x desktop (Win32 + Skia) with ReactiveUI
for MVVM and ScottPlot.Avalonia (SkiaSharp) for rendering — no WPF, WebView2, or JS frontend.
Solution: `SemiPlot.slnx`. All commands run from repository root.

## Build

```powershell
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj    # recommended (entry executable)
dotnet build SemiPlot.slnx                     # all projects
dotnet run   --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj
dotnet format SemiPlot.slnx                    # pre-commit hook enforces this
```

The bench seeder fills an empty, `semibase create`-provisioned database with a generated archive. It
refuses a database that already holds `public.trends` and issues no `DROP` anywhere:

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Database=semiplot_dev;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Database=semiplot_dev;Username=postgres;Password=<super>" `
  --end 2026-01-02T00:00:00 --days 1 --pens 8 --seed 1
```

`--connection` and `--end` are required; `--end` carries no time zone, so two runs of the same seed
produce the same archive. `--admin-connection` is optional and only fills `semiplot_tags`, which
`scada_writer` holds no privilege on. Run it with no arguments for the full option list.

## Test

Tests live in two projects, split by target framework rather than by taste.

| Project | Target | Framework | References | Holds |
| --- | --- | --- | --- | --- |
| `SemiPlot.Tests` | `net10.0-windows` | xunit v2 + `Avalonia.Headless.XUnit` | `SemiPlot.UI` | Everything touching the UI, plus the renderer-agnostic Core models |
| `SemiPlot.Tests.Data` | `net10.0` | xunit v3 | `SemiPlot.Core`, `SemiPlot.Tools.ArchiveSeeder` | Bench and data-source tests, pure and container-gated. Never Avalonia, never the UI |

`SemiPlot.Tests` carries `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]`. Pure logic
(decimation, navigation, scale, cursor, delta) uses plain `[Fact]`; tests touching
ReactiveUI/ScottPlot/Avalonia use `[AvaloniaFact]`/`[AvaloniaTheory]`. The Core model tests build
against the UI project, so they do not run independently of the UI build — the accepted cost of
keeping one Avalonia project. `SemiPlot.Tests.Data` stays plain `net10.0` so it runs on a Linux CI
runner, which a project referencing `SemiPlot.UI` structurally cannot. The two xunit majors coexist
with no runner setting: `dotnet test` runs each project in its own process.

**Exit path for the split.** It ends with the Avalonia 11 → 12 bump of the UI, after which
`SemiPlot.Tests` takes `Avalonia.Headless.XUnit` 12.x and both projects sit on xunit v3. Nothing
external blocks that any more. Verified against the NuGet nuspecs on 2026-08-14:
`Avalonia.Headless.XUnit` 11.3.8 (pinned here) depends on `xunit.core` 2.4.0 while 12.0.0 and later
depend on `xunit.v3.extensibility.core` 3.2.2; `ScottPlot.Avalonia` 5.1.57 (pinned here) and 5.1.58
depend on `Avalonia` 11.3.4 while 5.1.59 depends on `Avalonia` 12.0.0; `ReactiveUI.Avalonia`
publishes 12.1.1. What remains is the UI bump itself, which is its own piece of work.

```powershell
dotnet test SemiPlot.slnx                                                 # full suite, both projects
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj                 # UI and Core models
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj       # bench and data source
dotnet test SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot.slnx --filter "Category=Unit"
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
```

Test traits: `[Trait("Component", "Core|UI")]`, `[Trait("Area", "Data|Bridge|Di")]`,
`[Trait("Category", "Unit|Integration")]`. Every test class carries all three; `SemiPlot.Tests.Data`
is `Component=Core` throughout, since nothing in it touches the UI.

**Assertions split by project, deliberately.** `SemiPlot.Tests` uses AwesomeAssertions
(`.Should()`) exclusively. `SemiPlot.Tests.Data` uses raw xunit `Assert.` exclusively and references
no assertion library: its assertions are mostly `Assert.Equal`, `Assert.Contains` and `Assert.All`
over rows and database state, where the fluent form buys no diagnostic the raw form does not already
give. Keep each project on its own style rather than mixing the two inside one file.

### Gated data tests

The integration tests in `SemiPlot.Tests.Data` need a container runtime and the `semibase` binary
(`github.com/Semiteq/SemiBase`, pinned `v0.1.0`, taken as a release asset — no Go toolchain). Either
one missing is reported as a skip with a stated reason, never as a pass. Environment carries the
policy:

| Variable | Effect | Unset means |
| --- | --- | --- |
| `SEMIPLOT_TEST_PG` | Connection string of an existing semibase-provisioned server to use instead of a container; the fixture re-runs `semibase create` against it, which is idempotent | start a container |
| `SEMIPLOT_PG_IMAGE` | Image tag for that container | `postgres:17-alpine` |
| `SEMIPLOT_REQUIRE_DB` | `1` or `true` turns an unavailable runtime from a skip into a failure. The CI `data-tests` job sets it; a developer machine must not | skip with a reason |
| `SEMIBASE_EXE` | Path to the `semibase` binary | search `PATH` |
| `SEMIBASE_WRITER_PASSWORD`, `SEMIBASE_READER_PASSWORD` | Role passwords for `semibase create`, read **only** on the `SEMIPLOT_TEST_PG` path — a real server's roles already have passwords. The container path uses fixed dummy passwords of the fixture's own, so a developer needs no variable at all | required on the `SEMIPLOT_TEST_PG` path, unused otherwise |

`SEMIBASE_SUPER_PASSWORD` is passed to `semibase` by the fixture, taken from the container or from
the `SEMIPLOT_TEST_PG` connection string; setting it in the shell changes nothing.

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
- Register services in extension methods: `AddData()`, `AddUi()`.
- Avoid mutable static state.
- Core `AddData()` keeps the bare data `IScheduler`. The UI scheduler is not a second container
  registration: capture `AvaloniaScheduler.Instance` (= `RxApp.MainThreadScheduler`) in the
  `.AfterSetup(...)` callback after `UseReactiveUI()` and pass it explicitly via the coordinator factory.

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
  `SemiPlot.DataSource.*` project (`SemiPlot.DataSource.Stub` is the current stub, and owns the
  stub-only `MinMaxDecimator`). Core must not reference a data-source project; real providers slot in
  as siblings without touching Core.
- The bench seeder `SemiPlot.Tools.ArchiveSeeder` owns verbatim copies of `SyntheticValueWalk`,
  `SyntheticPenCatalog` and `SyntheticPen` and must not reference `SemiPlot.DataSource.Stub`: the
  stub evolves for UI reasons while the bench stays frozen, a golden-digest test pins its output, and
  later slices develop against that output. `sql/semiplot_dev.sql` is an `EmbeddedResource` of that
  project. Roles, grants and `semiplot_tags` are SemiBase's and are never defined in this repository.

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
