# Agent Instructions for SemiPlot

SemiPlot is a trend/chart viewer for an industrial installation (semiconductor plasma
process tools: ICP / RIE / PECVD). It reads live tags and historical archives from
Simple-Scada 2 and renders interactive, multi-axis trends.
Platform: .NET 10, Windows, C# 14. UI: Avalonia 11.3.x desktop (Win32 + Skia) with ReactiveUI
for MVVM and ScottPlot.Avalonia (SkiaSharp) for rendering — no WPF, WebView2, or JS frontend.
Solution: `SemiPlot/SemiPlot.slnx`. All commands run from repository root.

## Build

```powershell
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj    # recommended (entry executable)
dotnet build SemiPlot/SemiPlot.slnx                     # all projects
dotnet run   --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj
dotnet format SemiPlot/SemiPlot.slnx                    # pre-commit hook enforces this
```

## Test

Tests are split into two projects:

- `SemiPlot.Core.Tests` — pure Core (no UI reference), uses `xunit.v3`. Builds and runs green
  independently of UI state; hosts the renderer-agnostic model tests (decimation, navigation, scale,
  cursor, delta).
- `SemiPlot.Tests` — UI/Bridge/Di tests; references `SemiPlot.UI`. Runs on the
  `Avalonia.Headless` harness with `xunit` v2 (not v3 — `Avalonia.Headless.XUnit` is xunit-v2-only).
  `TestAppBuilder.cs` carries `[assembly: AvaloniaTestApplication]`. Tests touching
  ReactiveUI/ScottPlot pipelines use `[AvaloniaFact]`/`[AvaloniaTheory]`; pure logic stays plain `[Fact]`.

```powershell
dotnet test SemiPlot/SemiPlot.slnx                                       # full suite
dotnet test SemiPlot/SemiPlot.Core.Tests/SemiPlot.Core.Tests.csproj      # Core models only
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj                # UI / headless
dotnet test SemiPlot/SemiPlot.slnx --filter "Area=Data"
dotnet test SemiPlot/SemiPlot.slnx --filter "Category=Unit"
dotnet test SemiPlot/SemiPlot.slnx --filter "FullyQualifiedName~TestMethodName"
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
- ScottPlot is a thin render target: renderer-agnostic logic (decimation, navigation, scale, cursor)
  lives in unit-tested Core models; only views touch `AvaPlot`. The data hub (`TrendCoordinator`)
  feeds the chart VM via `IObservable`/awaitables (see `docs/architecture/data-integration.md`).

---

This is the project overview file; do not add specifics here. See the machine-readable
architecture docs in `docs/architecture/*` (English). Plans live in `docs/plans/`
(`YYYYMMDD-<name>.md`; completed ones in `docs/plans/completed/`).
