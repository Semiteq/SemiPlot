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

## Test

All tests live in one project, `SemiPlot.Tests`, on `xunit` v2 + `Avalonia.Headless.XUnit`. It
references `SemiPlot.UI`. `TestAppBuilder.cs` carries `[assembly: AvaloniaTestApplication]` and provides
the headless harness. Pure logic (renderer-agnostic Core models: decimation, navigation, scale, cursor,
delta) uses plain `[Fact]`; tests touching ReactiveUI/ScottPlot/Avalonia use `[AvaloniaFact]`/`[AvaloniaTheory]`.

Accepted trade-off: the Core model tests now build against the UI project, so they no longer run
independently of the UI build. This was chosen deliberately — `Avalonia.Headless.XUnit 11.3.x` is
xUnit-v2-only and `ScottPlot.Avalonia` has no released Avalonia-12 build, so staying on Avalonia 11 +
xUnit v2 in a single project is simpler than maintaining a two-project split across xUnit majors. The
xUnit-v3 / Avalonia-12 unification is deferred until `ScottPlot.Avalonia` ships an Avalonia-12 release;
revisit then.

```powershell
dotnet test SemiPlot.slnx                                       # full suite
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj                # the single test project
dotnet test SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot.slnx --filter "Category=Unit"
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
```

Test traits: `[Trait("Component", "Core|UI")]`, `[Trait("Area", "Data|Bridge|Di")]`,
`[Trait("Category", "Unit|Integration")]`.

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

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
