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
dotnet format SemiPlot.slnx                    # pre-commit hook enforces this
```

`dotnet run` on the viewer needs all three launch keys — `--config-dir`, `--log-file` and
`--logging-level` — and none has a default; a missing or bad one opens the failure window and exits
1. The run reads a copy of the tracked set, never the tracked set itself: `readme.md` holds the
copy-and-fill-the-password recipe that produces `SemiPlot/Artifacts/dev-config`, which is also where
the `.zed/` and `.run/` launchers point `--config-dir`.

Configuration is a tree of section folders under `--config-dir`: `app/` and `connection/`. Every
section folder is read whole — each `*.yaml` in it parsed on its own and merged at the key level —
and a key carried by two files of one folder is a startup failure naming both. The set that ships is
tracked at `ConfigFiles/` and gated by the production loaders in
`SemiPlot.Tests.Unit/DeliveredConfigurationTests`; `ConfigFiles/connection/connection.yaml` carries
an empty `password`, which is a named startup failure. The password goes into the copy, never into
the tracked file (`docs/architecture/overview.md`).

`.editorconfig`'s style and quality analyzer rules fail `dotnet build` (`TreatWarningsAsErrors`,
`EnforceCodeStyleInBuild`) and `dotnet format SemiPlot.slnx --verify-no-changes` alike, so a
regression stops both the build and the pre-commit hook. The hook is `.githooks/pre-commit`; wire it
once per clone with `git config core.hooksPath .githooks`. Over the staged `.cs` files it runs
`dotnet format --verify-no-changes` and the comment linter
[terse](https://github.com/mrcsin/terse), pinned in `.config/dotnet-tools.json` and restored by the
hook itself: ASCII-only source, a `<summary>` of at most three lines, no `//` essays, banners or
`#region`. Both gates hold at zero with no baseline and no suppression marker; a comment that
cannot pass moves its knowledge into the code or `docs/architecture` with a one-line pointer.

`[*.cs]` is `charset = utf-8` with no byte order mark, and a file `dotnet new` writes passes the hook
only after one `dotnet format SemiPlot.slnx --include <file>`. `docs/architecture/ui-text.md` holds
what the operator reads, what stays a literal in code, and how CI gates both.

The repository's `nuget.config` clears every inherited package source, so a `dotnet tool install` run
from inside the repository sees only `nuget.org`; install a tool from another source outside the
repository directory or with `--add-source`.

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

`converge` is a separate, bench-only subcommand: unlike the seeding run above, it does issue `DROP
DATABASE ... WITH (FORCE)`. It waits for `--admin-connection` up to 60 s, recreates the database
`--connection` names from `semiplot_provisioned`, seeds it up to `--end` or this machine's clock,
fills the tag catalogue and writes `connection/connection.yaml` into `--config-dir` with the bench
reader role's fixed password. That one file is all it writes, so run it against a directory that
already holds a copy of the tracked set (`docs/architecture/bench.md#the-converge-verb`):

```powershell
New-Item -ItemType Directory -Force SemiPlot\Artifacts\dev-config | Out-Null
Copy-Item ConfigFiles\* SemiPlot\Artifacts\dev-config -Recurse -Force

dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- converge `
  --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=<super>" `
  --config-dir SemiPlot\Artifacts\dev-config
```

The demo writer is the same seeder run with `--follow <seconds>` instead of `--end`: it appends to
the archive `converge` created and thins it into the coarse layers on every tick
(`docs/architecture/bench.md#the-demo-writer`).

`SemiPlot.AppHost` runs the whole demo stand — the bench container, `converge`, the demo writer and
the viewer — in dependency order and stops them together:

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost
```

The stand copies `ConfigFiles/` into `%TEMP%\SemiPlot\ConfigFiles`, points the log at
`%TEMP%\SemiPlot\Logs`, hands both to the processes it launches, and removes the configuration copy
when it stops; the logs are swept at the next start instead, because the viewer may still hold
`semiplot.log` open (`docs/architecture/bench.md#the-demos-directories`). Nothing writes into the
tracked set.

## Test

Two projects, split on one axis: needs a container or not.

| Project | Framework | References | Holds |
| --- | --- | --- | --- |
| `SemiPlot.Tests.Unit` | xunit v3 + `Avalonia.Headless.XUnit` | `SemiPlot.UI`, `SemiPlot.Core`, `SemiPlot.DataSource.Postgres`, `SemiPlot.Tools.ArchiveSeeder` | Every test that needs no container: the UI, the Core models, the seeder's generators and the provider's pure classes |
| `SemiPlot.Tests.Integration` | xunit v3 + `Avalonia.Headless.XUnit` | the same four | The container harness, the container tests and the journeys, all in one xunit collection |

- Each project carries its own `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]`. Pure
  logic uses plain `[Fact]`; tests touching ReactiveUI/ScottPlot/Avalonia use
  `[AvaloniaFact]`/`[AvaloniaTheory]`.
- Neither project references the other. Core, `SemiPlot.DataSource.Postgres` and `SemiPlot.UI` each
  name both in `InternalsVisibleTo`.
- `SemiPlot.Tests.Unit` sets `failSkips` in `xunit.runner.json`, so no gated test may live there.
  `SemiPlot.Tests.Integration` carries no `xunit.runner.json`.
- An xunit v3 test project is an executable: a hung test leaves `SemiPlot.Tests.Unit.exe` locked and
  the next build fails with MSB3027 until it is killed. The container half is bounded at two minutes.
- A plain `[Fact]` body runs with no `SynchronizationContext`, so an `await` on a
  `TaskCompletionSource` completed by production code resumes inline on the completing thread. A gate
  awaited by the test and completed by production code takes
  `TaskCreationOptions.RunContinuationsAsynchronously`; one awaited by production code and completed
  by the test must not, because tests assert on the inline resumption. `[AvaloniaFact]` bodies are
  unaffected.

```powershell
dotnet test SemiPlot.slnx                                                        # both projects
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj              # no container needed
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj  # container required
dotnet test SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
```

CI has two jobs: `unit-windows` runs `SemiPlot.Tests.Unit` on `windows-latest`; `linux` builds the
solution once on `ubuntu-latest` and runs both test projects, the Docker daemon of the runner serving
the container fixture. A Windows-only API fails
the Linux leg only once a test executes the call (`CA1416` is a warning); a Windows path used as a
string does not fail it at all. The fail-only behavior is in `docs/architecture/testing-strategy.md`.

Test traits: `[Trait("Component", "Core|UI")]`,
`[Trait("Area", "Data|Bridge|Chart|Di|Messages")]`,
`[Trait("Category", "Unit|Integration")]`. Every test class carries all three.

AwesomeAssertions everywhere. Tests over provider errors assert by error type and structured field,
never on message wording.

Frame cost during a drag is measured, not asserted: `docs/architecture/testing-strategy.md`, Frame cost.

### Container tests

Every test in `SemiPlot.Tests.Integration` needs a container runtime and nothing else:
`SemiPlot/bench/Dockerfile` copies `/semibase` out of
`ghcr.io/semiteq/semibase:latest`, which the fixture pulls ahead of the build, and runs
`semibase bench` from `/docker-entrypoint-initdb.d/` before the published port opens. The container
carries fixed dummy passwords of the fixture's own, and the base image is the constant
`postgres:17-alpine`. A missing runtime fails every test of the collection with
`TestPipelineException`; nothing skips.

A run leaves nothing behind but the images it pulled: the resource reaper removes the built image,
the container and every database.

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

- A file holds one concept, named for its primary type: that type and the small types only it uses
  (an options record, an enum it switches on, a private helper). Group related small types together
  rather than giving each a three-line file; split when a grouped type gains a second consumer or the
  file passes the size limit. Never group by size alone (`Types.cs`).
- File-scoped namespaces: `namespace SemiPlot.Core.Trends;`
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
| Constants                         | PascalCase, including local `const` | `MaxPenCount`                |
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
  scheduler is not a second container registration: `App` reads the static
  `AvaloniaScheduler.Instance` and passes it explicitly to the coordinator constructor and the
  chart/minimap factories. `RxApp` does not exist in the installed ReactiveUI 23.2.28 — its
  schedulers moved to `RxSchedulers` and its exception handler to `RxState`; this repository reads
  neither.
- **Nothing may construct a ReactiveUI object before `AppBuilder.Setup()`.** `RxState.DefaultExceptionHandler`
  initialises itself on first read and `InitializeExceptionHandler` then no-ops, so one
  `ReactiveCommand` or one `ObservableAsPropertyHelper` built ahead of `Setup()` turns
  `App.BuildAvaloniaApp`'s `.UseReactiveUI(builder => builder.WithExceptionHandler(...))` into a
  silent no-op, with no error and no log line. `StartupSequence.Run` touches no ReactiveUI type, and
  `MessagePanelViewModel` is resolved from the container inside `.AfterSetup(...)`, never before it
  (`docs/architecture/overview.md`).
- `.AfterSetup(...)` is synchronous, so no blocking call belongs in it. `StartupSequence.Run` holds
  the ordered blocking steps and `Program.Main` calls it ahead of `BuildAvaloniaApp()`, handing
  `App.Run(AppSettings?, Result<StartupData>)` both results; the reads `InitializeServices` starts
  inside the callback are asynchronous and return through the schedulers
  (`docs/architecture/data-integration.md`).

### Interface Design

- Create an interface when: 2+ implementations exist, the class is mocked in tests, it crosses
  an architectural layer boundary, or it implements Strategy/Factory.
- Do not create an interface for a single concrete class with no extension plans, or for POCOs/DTOs.
- Interfaces belong on the consumer side.

### Comments

- Only for genuinely non-obvious business logic, one or two lines. English only, ASCII only.
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
- Every string the operator reads lives in `SemiPlot.UI/Localization/Resources.resx` and
  `Resources.ru.resx`; C# reads `Resources.Key` or `Resources.FormatKey(...)`, AXAML
  `{x:Static text:Resources.Key}` (`docs/architecture/ui-text.md`).
- Every colour this tree's AXAML paints resolves to a key in `SemiPlot.UI/Styles/Palette.axaml`; a
  literal in AXAML is a defect. Semi's own surfaces outside that key set keep Semi's variant-aware
  stock brushes (`docs/architecture/ui-theme.md`).
- The left-button gesture is one state, never overlapping branches: a `Chart/LeftButtonTool`
  (`Pan | DeltaPlacement`) enum sourced from the navigation bar's delta toggle decides pan vs delta
  placement, and the axis-region edit is a pre-branch ahead of it. The bar's `IsSticky` has a single
  writer (the `WindowChanged` handler refreshing from `Navigation.IsSticky`) — do not reintroduce
  imperative `IsSticky =` assignments.
- A checkable menu item reads its flag `Mode=OneWay` and writes it only through the command it
  invokes, so the command is the flag's single writer. A two-way `IsChecked` would make the control a
  second writer and the two halves would drift.
- A menu item without a command, or without children, is not added. A disabled placeholder renders
  and does nothing, and it also forces an exemption into `AppMenuBarTests`, which walks the declared
  `Items` and requires every leaf to carry one or the other.
- Every failure the operator should see goes to `Messages/MessagePanelViewModel` through
  `Messages/ArchiveFailureMapper.Map`, which assigns the severity in its own per-kind switch — never
  at the call site. The one message with no error behind it is
  `AppStatusBarViewModel.ConnectionRestored`, which builds its own `Info` view because the mapper maps
  errors. A `catch`, or an Rx `onError`, that only logs is a defect
  (`docs/architecture/data-integration.md#no-failure-stops-at-the-log`). Code-behind reaches the
  panel through its view model (`TrendChartViewModel.ReportFailure`, `MainWindowViewModel.ReportFailure`).

### Data-source projects

- `IDataProvider` + its DTOs stay in `SemiPlot.Core`; every concrete provider lives in its own
  `SemiPlot.DataSource.*` project (`SemiPlot.DataSource.Postgres` is the only one). Core must not
  reference a data-source project; a further provider slots in as a sibling without touching Core.
- `MinMaxDecimator` lives in `SemiPlot.Core/Trends` beside `PenHistoryEnvelope` and is shared by the
  coarse-layer read path of every provider; each provider translates its own rows into the
  decimator's input vocabulary, which `docs/architecture/charting.md` states.
- The bench seeder `SemiPlot.Tools.ArchiveSeeder` owns `SyntheticValueWalk`, `SyntheticPenCatalog`
  and `SyntheticPen`. `RawLayerGeneratorTests` pins the generator by determinism and invariants —
  the absolute lattice, the break holes, the row-pair shape — not by a digest. **One lattice serves
  both generators**: a change sits at `index * intervalTicks` from absolute tick zero, and
  `RawLayerGenerator` and `LiveTailGenerator` both emit through `RawLayerGenerator.AppendWindow`.
  `SemiPlot.Tests.Unit/SharedLatticeTests.cs` goes red if they are split. `public.trends`,
  `semiplot_tags`, the two roles and their grants are SemiBase's; the seeder fills the archive table
  and creates only the day partitions its rows land in (`docs/architecture/bench.md`).
- The provider runs no cold-path reader: a failed read is mapped by `ArchiveExceptionMapper`, which
  stays synchronous, pure and unit-testable, and nothing opens a second connection to enrich the
  error. Each read supplies the one relation its statement touches, which the detail line names.

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
