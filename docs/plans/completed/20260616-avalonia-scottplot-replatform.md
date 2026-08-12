# SemiPlot Re-platform: WPF/WebView2/uPlot → Avalonia + ScottPlot 5

## Overview

Replace the SemiPlot presentation stack (WPF host + WebView2 + uPlot JS frontend) with a native
Avalonia 11.3.x / .NET 10 desktop app that renders trends with ScottPlot 5 (SkiaSharp). The web
bridge (JSON message contract, `WebViewTrendChannel`, `Web/` JS modules) is removed. The Core data
layer (`IDataProvider`, DTOs, `RandomStubDataProvider`) is retained. `TrendCoordinator` is refactored
from a JSON channel poster into an observable event-hub feeding a chart ViewModel.

Problem solved: uPlot is too feature-poor and hosting WebView2 just to write JS is not worth it;
native .NET charting gives independent multi-axis, real-time append, and large-archive decimation
without a JS bridge.

Authoritative spec: `docs/architecture/trend-interaction.md` (behavior + Decisions log),
`docs/architecture/charting.md`, `docs/architecture/overview.md`, `docs/architecture/data-integration.md`.

## Context (from discovery)

- **Project:** .NET 10 / C# 14. Solution `SemiPlot/SemiPlot.slnx`. Central package management (CPM) at
  `SemiPlot/Directory.Packages.props`.
- **Current UI (`SemiPlot.UI`, to re-platform):** WPF (`UseWPF`, `net10.0-windows`), WebView2.
  `Program.cs`, `App.xaml(.cs)`, `UiServiceCollectionExtensions.cs`, `MainWindow/*`, `Bridge/*`, `Web/*`.
- **Core (retained):** `Data/` (`IDataProvider`, `RandomStubDataProvider`, `SyntheticPen*`,
  `SyntheticValueWalk`, `DataServiceCollectionExtensions` — registers a bare `IScheduler` =
  `DefaultScheduler.Instance`), `Trends/` (`AggregationLayer`, `Pen`, `Sample`, `Series`).
- **`TrendCoordinator` seam:** depends on `ITrendChannel.Post(json)`, the `TrendMessages` records, and
  inbound JSON dispatch; its data logic (subscribe → `Buffer` → columnar batch; `QueryHistoryAsync`;
  layer switch) is reusable. Because the coordinator references these types directly, they are deleted
  in the same task as the refactor, not earlier. The current "WebView2 serializes WebMessageReceived,
  so no lock is needed" invariant comment becomes false after the refactor and must be removed.
- **Test project (critical for gating):** the single `SemiPlot.Tests` project **references
  `SemiPlot.UI`** (verified `SemiPlot.Tests.csproj`). Therefore, the moment the UI stops compiling
  (Task 1), the **entire** test assembly cannot build and no `--filter` can run. Resolved by splitting
  Core-only tests into a `SemiPlot.Core.Tests` project with no UI reference (Task 2).
- **SemiStep reference (sibling repository):** Avalonia 12.0.4 / net10, `ReactiveUI.Avalonia`,
  MS.DI extension methods, Serilog (file, 5 MB / 5 files), FluentTheme; scheduler-capturing singletons
  initialized in `.AfterSetup(...)` after `UseReactiveUI()`; `MainWindow` `Grid` = menu / content /
  message panel / status bar. **SemiPlot mirrors SemiStep's patterns, NOT its versions** — see below.

## Confirmed external facts (workflow-verified)

- **ScottPlot.Avalonia 5.1.x has no Avalonia 12 build;** it requires Avalonia ≥ 11.3.4. Avalonia 12.0.0
  released 2026-04-07. SemiPlot therefore targets **Avalonia 11.3.x**, deliberately diverging from
  SemiStep's Avalonia 12 (the SemiStep mirror is patterns, not versions).
- **Binding version floor = 11.3.8**, set by `ReactiveUI.Avalonia` 11.3.8 (`Avalonia ≥ 11.3.8`), which is
  higher than ScottPlot's 11.3.4. All `Avalonia.*` packages pin to ONE identical patch ≥ 11.3.8.
- `ReactiveUI.Avalonia` 11.3.8 resolves ReactiveUI 22.2.1 + Splat 17.1.1; the 11.3 UI scheduler symbol is
  `RxApp.MainThreadScheduler` / `AvaloniaScheduler.Instance` (NOT SemiStep's `RxSchedulers.MainThreadScheduler`,
  which is 11.4-beta+ and will not compile on 11.3).
- **MVVM = ReactiveUI** (justified below); CommunityToolkit.Mvvm is an acceptable lower-friction
  alternative (and, if chosen, relaxes the floor to ScottPlot's 11.3.4). System.Reactive stays in the
  data/coordinator layer regardless.

## Development Approach

- **Testing approach: Regular** (implement, then add/update tests within the same task).
- **Green-gate reality:** `SemiPlot.Core.Tests` (no UI ref) builds and runs green from Task 2 onward —
  it is the gate for the architectural-core tasks (decimation, navigation, scale models). The UI test
  assembly (`SemiPlot.Tests`) and `slnx`-wide green only return once the UI compiles again, at the end of
  **Task 6** (coordinator refactor + bridge removal). Tasks 1, 4, 5 carry a "Core builds + `Core.Tests`
  green + `slnx` restore green; UI knowingly red" gate, not a full-suite gate.
- Test command (Core): `dotnet test SemiPlot/SemiPlot.Core.Tests/SemiPlot.Core.Tests.csproj`.
  Full suite (from Task 6): `dotnet test SemiPlot/SemiPlot.slnx`.
- Follow CLAUDE.md style: one class per file, file-scoped namespaces, tabs, ≤120 cols, braces on new
  lines, `var`, constructor injection via extension methods, no abbreviations.

## Testing Strategy

- **Unit tests (backbone, in `SemiPlot.Core.Tests`):** `MinMaxDecimator`, `TrendNavigationModel` (sticky
  state machine), `PenScaleModel` (axis model), `CursorReadoutModel`, `DeltaCursorModel`.
- **Coordinator + ViewModel tests (in `SemiPlot.Tests`, headless):** `Avalonia.Headless.XUnit` harness;
  `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]` building
  `Configure<App>().UseHeadless(...).UseReactiveUI(_ => {})`. Tests touching `ReactiveCommand`/`ReactiveObject`
  pipelines use `[AvaloniaFact]`/`[AvaloniaTheory]`; pure Core models stay plain `[Fact]`.
- **No e2e.** Rendering correctness, FPS feel, gestures → manual verification (Post-Completion).

## Progress Tracking

- Mark `[x]` when done. New tasks `➕`. Blockers `⚠️`. Keep this file in sync.

## Solution Overview

- **In-place conversion** of `SemiPlot.UI` to Avalonia (keep project refs, `InternalsVisibleTo`).
- **Pure-logic Core for behavior:** decimation, view-window/sticky navigation, and the axis/scale model
  are renderer-agnostic, unit-tested classes (Core). ScottPlot is a thin render target driven by them.
- **Decimation defined first (Task 3):** the min/max-per-pixel envelope and its end-to-end representation
  are pinned before the chart/cursor design against it.
- **Observable data hub:** `TrendCoordinator` exposes realtime batches and history results as
  observables/awaitables; the chart VM subscribes on the UI scheduler.

## Technical Details

- **`SemiPlot.UI` packages — add:** `Avalonia`, `Avalonia.Desktop`, `Avalonia.Themes.Fluent`,
  `ReactiveUI.Avalonia` (11.3.8), `Avalonia.Win32`, `Avalonia.HarfBuzz`, `Avalonia.Skia`, `ScottPlot.Avalonia`
  (5.1.57). **Remove (in Task 6, not Task 1):** `Microsoft.Web.WebView2`, `UseWPF`. **`SemiPlot.Tests` adds**
  `Avalonia.Headless`, `Avalonia.Headless.XUnit`. All `Avalonia.*` pinned identical ≥ 11.3.8 in
  `SemiPlot/Directory.Packages.props`.
- **Rx version graph:** confirm `ReactiveUI.Avalonia` 11.3.8 pulls ReactiveUI 22.2.1 + Splat 17.1.1 without
  forcing `System.Reactive` above the pinned 6.1.0. If an NU1605 downgrade appears, bump `System.Reactive`
  **and** `Microsoft.Reactive.Testing` in lockstep to the floor ReactiveUI 22.2.1 demands.
- **Scheduler seam (one mechanism, committed):** Core `AddData()` keeps the bare `IScheduler`
  (`DefaultScheduler.Instance`) for data timing. The UI scheduler is **not** a second container
  registration; capture `AvaloniaScheduler.Instance` (= `RxApp.MainThreadScheduler` after `UseReactiveUI()`)
  in the `AfterSetup` callback and pass it explicitly to a coordinator factory. Constructor:
  `TrendCoordinator(IDataProvider, ILogger, IScheduler dataScheduler, IScheduler uiScheduler)` — `Buffer`
  on `dataScheduler`, `ObserveOn` on `uiScheduler`.
- **Decimation envelope contract (end-to-end, committed):** the history record carries, per pen, ascending
  `X[]` + `Min[]` + `Max[]` + center `Y[]`; `RealtimeBatch` stays single-value `double?[]` (null = gap).
  **Line plottable = `Scatter`** (per-pen `ConnectStyle` = stepped/straight, `OnNaN = Gap`) for every pen in
  both modes, with **`FillY` (X, Top = Max, Bottom = Min)** carrying the min/max band. SignalXY is rejected
  (cannot express per-pen stepping + NaN gaps; its built-in decimation is unused because the data layer
  pre-decimates). Arrays segment at nulls. **Live-edge join:** the realtime single-value tail appends to the
  same `Scatter` center line; the `FillY` band degenerates to `Min == Max == value` at the live edge; when the
  active layer is coarse (minute/hour/day) at zoom-out, realtime points fold into the current decimation
  column rather than drawing raw; cursor/legend read the center channel consistently across the seam.
- **MVVM = ReactiveUI rationale:** the data layer is Rx-native (`IObservable`, `Buffer`/`ObserveOn`/`Publish`/
  `RefCount`) and the VMs are derived-state-heavy (sticky, cursor, active-pen) — a fit for
  `WhenAnyValue`/OAPH/`ReactiveCommand` over one shared `MainThreadScheduler`.
- **Performance:** render throttled to **30 FPS**; realtime input coalesced at ≤ **10 Hz**; ≤ **50 pens**;
  points to ScottPlot ≈ viewport-width × 2–4 (decimated).
- **`net10.0-windows` retained** deliberately: Windows-only operator-PC target with the Win32 backend.

## What Goes Where

- **Implementation Steps (`[ ]`):** all code, tests, architecture-doc updates.
- **Post-Completion (no checkboxes):** manual run/feel verification, FPS under load, IJ theme (north-star),
  real PostgreSQL provider, view persistence/export (LATER).

## Implementation Steps

### Task 1: Convert SemiPlot.UI to Avalonia 11.3.x + confirm restore

**Files:** Modify `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`, `SemiPlot/Directory.Packages.props`;
Delete `SemiPlot/SemiPlot.UI/Web/`.

- [x] In `Directory.Packages.props`: add `PackageVersion`s — all `Avalonia.*` (Avalonia, Avalonia.Desktop, Avalonia.Themes.Fluent, Avalonia.Win32, Avalonia.Skia) at one identical patch ≥ **11.3.8**, `ReactiveUI.Avalonia` **11.3.8**, `ScottPlot.Avalonia` **5.1.57**. Keep the `Microsoft.Web.WebView2` `PackageVersion` (removed in Task 6). **Deviation:** `Avalonia.HarfBuzz` has NO 11.3.x build on NuGet (only 12.x) — it is omitted; HarfBuzz text shaping arrives transitively via `Avalonia.Skia` 11.3.8 → `HarfBuzzSharp` 8.3.1.1, and `UseHarfBuzz()` lives in the `Avalonia.Skia` assembly.
- [x] In the csproj: remove `UseWPF` and the WPF `ApplicationDefinition`/`Page` block and the `Web\**` content include; add the Avalonia + ScottPlot.Avalonia + ReactiveUI.Avalonia package references; keep `net10.0-windows`, `OutputType=WinExe`, `InternalsVisibleTo`, Serilog, `System.Reactive`, MS.DI/Logging, **and (for now) the WebView2 reference**. Delete the `Web/` folder.
- [x] **Restore confirmation (replaces the old blocker gate):** `dotnet restore SemiPlot/SemiPlot.slnx` produces no NU1605/NU1608; all `Avalonia.*` resolve to an identical **11.3.8** and ≥ 11.3.8; `ReactiveUI.Avalonia` 11.3.8 resolves ReactiveUI 22.2.1 + Splat 17.1.1 without forcing `System.Reactive` above 6.1.0 (it stayed at 6.1.0, no lockstep bump needed). Only a transitive NU1903 advisory (Tmds.DBus.Protocol, unused on Win32) appears — not a blocking error.
- [x] No test code. Gate: restore green (UI build knowingly red until App/MainWindow are ported).

### Task 2: Split Core tests into SemiPlot.Core.Tests + headless harness for UI tests

**Files:** Create `SemiPlot/SemiPlot.Core.Tests/SemiPlot.Core.Tests.csproj` and move `SemiPlot/SemiPlot.Tests/Core/**`
into it; Modify `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`; Create `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs`;
Modify `SemiPlot/SemiPlot.slnx`.

- [x] Create `SemiPlot.Core.Tests` referencing **only** `SemiPlot.Core` (xUnit, same traits); move all existing `Core/*` tests there so the architectural-core tests run regardless of UI state.
- [x] `SemiPlot.Tests` keeps the UI/Bridge/Di tests (still references `SemiPlot.UI`); add `Avalonia.Headless` + `Avalonia.Headless.XUnit`; add `TestAppBuilder.cs` with `[assembly: AvaloniaTestApplication]` → `Configure<App>().UseHeadless(...).UseReactiveUI(_ => {})`.
- [x] Add `SemiPlot.Core.Tests` to the solution.
- [x] Gate: `dotnet test SemiPlot/SemiPlot.Core.Tests/...` green (existing Core tests pass); UI test project knowingly red until Task 6.

### Task 3: Data contract + min/max decimation envelope (Core, architectural core)

**Files:** Create `SemiPlot/SemiPlot.Core/Data/MinMaxDecimator.cs`,
`SemiPlot/SemiPlot.Core/Trends/RealtimeBatch.cs` + history-result record; Modify `Trends/Series.cs`,
`Data/RandomStubDataProvider.cs`; Create `SemiPlot/SemiPlot.Core.Tests/Data/MinMaxDecimatorTests.cs`;
Modify `SemiPlot/SemiPlot.Core.Tests/Data/RandomStubDataProviderTests.cs`.

- [x] Define typed in-process records (replacing JSON DTOs) in `Core/Trends`: history record per pen = ascending `X[]` + `Min[]` + `Max[]` + center `Y[]`; `RealtimeBatch` = union timestamps + per-pen `double?[]` (null = gap).
- [x] `MinMaxDecimator`: samples + target column count → min AND max per pixel column (+ center); pass-through when ≤ target; segment at nulls.
- [x] Stub provider returns layer/width-sized envelopes via the decimator; keep the seam so a future server-side SQL aggregate can replace in-process decimation behind `IDataProvider`. Map OPC bad-quality to `null` at the provider boundary (feeds the null=gap path).
- [x] Tests: envelope preserves a single-sample spike that Nth-sampling drops; column count ≤ target × const; monotonic X; pass-through for small inputs; bad-quality → null.
- [x] Update stub-provider tests. Gate: `Core.Tests` green.

### Task 4: Avalonia application bootstrap

**Files:** Create `SemiPlot/SemiPlot.UI/App.axaml(.cs)`; Delete `App.xaml(.cs)`;
Modify `Program.cs`, `UiServiceCollectionExtensions.cs`.

- [x] `App.axaml` with `FluentTheme` (light) + `Application.Styles`; `App.axaml.cs` resolves the main window/VM from DI in `OnFrameworkInitializationCompleted`.
- [x] `Program.Main`: Serilog (file, 5 MB / 5 files, structured) → build DI → `BuildAvaloniaApp().UseWin32().UseSkia().UseReactiveUI()` → `.AfterSetup(_ => InitializeServices(provider))` that captures `AvaloniaScheduler.Instance` and constructs the scheduler-capturing coordinator (via factory) AFTER `UseReactiveUI()` → `StartWithClassicDesktopLifetime`. **Deviation:** explicit `UseHarfBuzz()` omitted — `Avalonia.HarfBuzz` has no 11.3.x build (Task 1 finding); HarfBuzz shaping arrives transitively via `Avalonia.Skia`. `AvaloniaScheduler` / `UseReactiveUI` live in namespace `ReactiveUI.Avalonia` on 11.3.8 (verified by assembly inspection), NOT `Avalonia.ReactiveUI`. The SemiStep error-window/`StartupOutcome`/`ValidateStartup` machinery is out of scope (no failing pre-flight phase).
- [x] `AddUi()`: register the Avalonia main window + VM; provide the coordinator factory the captured UI scheduler. No second `IScheduler` registration. The coordinator factory is `Func<IScheduler, TrendCoordinator>` (UI scheduler as parameter, data scheduler resolved from container); the legacy `AddTrends()` channel factory is dropped from the `Program` composition (its file is deleted in Task 6).
- [x] (runs once UI compiles at Task 6) DI smoke test `Container_ResolvesMainWindowViewModel_UnderHeadlessHarness` added as `[AvaloniaFact]` in `CompositionRootTests`. Cannot execute until `SemiPlot.Tests` compiles (Task 6), per the partial-implementation exception.
- [x] Gate: Core builds + `Core.Tests` green (37 passed) + `slnx` restore green (only transitive NU1903 advisory). UI knowingly red (WPF `MainWindow.xaml.cs` → Task 5; `WebViewTrendChannel.cs` + coordinator refactor → Task 6).

### Task 5: MainWindow shell (Grid layout)

**Files:** Create `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml(.cs)`; Delete `MainWindow.xaml(.cs)`;
Modify `MainWindow/MainWindowViewModel.cs`.

- [x] `MainWindow.axaml`: `Grid` rows = toolbar / chart content / message panel / status bar; chart area placeholder.
- [x] `MainWindowViewModel : ReactiveObject` holds child VMs + `CompositeDisposable`. **Do not eagerly build command-bearing child VMs in the constructor** (so the Task 6 smoke test can stay simple); construct child VMs/commands lazily or on activation. **Note:** no command-bearing child VMs exist yet (toolbar/chart/legend arrive in Tasks 7/9/14); the VM currently holds only the `CompositeDisposable` and a `PenCount` projection over `IDataProvider`, keeping it DI-resolvable and constructor-light.
- [x] Gate: Core builds + `Core.Tests` green (37 passed); `slnx` restore green (only transitive NU1903 advisory). UI knowingly red only in `Bridge/WebViewTrendChannel.cs` (`System.Windows.Threading.Dispatcher` — removed in Task 6); new MainWindow files compile.

### Task 6: Coordinator → observable hub + remove the WebView bridge (UI compiles green here)

**Files:** Modify `Bridge/TrendCoordinator.cs`, `Bridge/TrendServiceCollectionExtensions.cs`,
`SemiPlot.UI.csproj`, `Directory.Packages.props`; Delete `Bridge/ITrendChannel.cs`, `WebViewTrendChannel.cs`,
`TrendMessages.cs`; Delete `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendMessageContractTests.cs`, `FakeTrendChannel.cs`;
Modify `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`, `FakeDataProvider.cs`.

- [x] Replace `ITrendChannel.Post(json)` with `IObservable<RealtimeBatch>` via `ObserveOn(uiScheduler).Publish().RefCount()`, and an awaitable `QueryHistoryAsync` returning the typed history record. Keep `Subscribe → Buffer(batchWindow, dataScheduler) → columnar build`; typed pens-catalog property; layer state + re-query. Replace inbound JSON with typed `RequestHistory(penIds, from, to)` / `SetLayer(layer)`. History results surface via a second `IObservable<TrendHistory>` (new Core record = layer + envelopes); `QueryHistoryAsync(penIds, from, to, layer, targetColumnCount)` is the direct awaitable pass-through.
- [x] Constructor `TrendCoordinator(IDataProvider, ILogger, IScheduler dataScheduler, IScheduler uiScheduler)`. **Deleted the stale "WebView2 serializes WebMessageReceived, no lock needed" comment**; replaced with a note that `RequestHistory`/`SetLayer` and their mutable state are touched only on the UI thread while `Buffer` stays on the data scheduler and crosses via `ObserveOn`.
- [x] Deleted `ITrendChannel`/`WebViewTrendChannel`/`TrendMessages`; removed channel registration (`TrendServiceCollectionExtensions.cs` deleted entirely — fully superseded by the Task 4 `AddUi()` coordinator factory, per the Task 4 note); **removed the `Microsoft.Web.WebView2` reference and `PackageVersion`**. Deleted `TrendMessageContractTests`, `FakeTrendChannel`.
- [x] Updated `TrendCoordinatorTests` to assert on emitted typed `RealtimeBatch`/`TrendHistory` objects (plain `[Fact]` with `TestScheduler` for Buffer timing + `ImmediateScheduler` for the UI seam = deterministic `ObserveOn`); grep clean of WebView2/`ITrendChannel`/`TrendMessages`. Updated `FakeDataProvider` to the current `IDataProvider` (envelope return + `targetColumnCount`).
- [x] Gate: **full `slnx` build + entire suite green** (first full-green point: Core.Tests 37 + SemiPlot.Tests 14 = 51 passed). The DI smoke test (`[AvaloniaFact]`, headless) resolves the main window VM and now runs. **Deviation:** `Avalonia.Headless.XUnit` 11.3.8 is xunit-v2-only (hard dep on `xunit.core` 2.4.0), which clashed (CS0433) with `SemiPlot.Tests`' `xunit.v3` once the UI compiled; resolved by switching only `SemiPlot.Tests` to `xunit` 2.9.3 (`SemiPlot.Core.Tests` stays on `xunit.v3`).

### Task 7: ScottPlot chart integration (Scatter + FillY envelope, realtime append)

**Files:** Create `Chart/TrendChartView.axaml(.cs)`, `Chart/TrendChartViewModel.cs`; Modify `MainWindow/MainWindow.axaml`;
Create `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`.

- [x] Host `ScottPlot.Avalonia.AvaPlot`. Per pen: a `Scatter` center line + a `FillY` min/max band, fed from the Task 3 envelope. Realtime tail appends to the center line with the band degenerate at the live edge. **Deviation:** ScottPlot 5.1.57 `Scatter` has **no `OnNaN`/`Gap` property** — that was a plan assumption. NaN-gap segmentation is automatic: the default `IPathStrategy` (`ScottPlot.PathStrategies.Straight`) skips `float.IsNaN` points and breaks the path, so feeding `double.NaN` (the envelope's gap marker) produces the gap. The committed gap mechanism is therefore "NaN in Center/Min/Max" rather than an explicit enum. `Scatter.ConnectStyle` (Straight/StepHorizontal/StepVertical) carries the per-pen stepping for Task 15. Band fed via `Plot.Add.FillY(double[] xs, double[] ys1, double[] ys2)`; realtime append calls `FillY.SetDataSource(ICollection<(double X, double Top, double Bottom)>)` (it snapshots, so it is re-set after each append), while the center `Scatter` wraps a `List<Coordinates>` by reference (`ScatterSourceCoordinatesList`) so appends are live.
- [x] Runtime add/remove pens + per-pen visibility toggle without rebuilding the plot (`Plot.Add.Scatter`/`Plot.Add.FillY` once per pen; `Plot.Remove(IPlottable)` on removal; `IsVisible` toggle); subscribe to the coordinator's realtime + history observables on the UI scheduler; redraw coalesced via `Sample(33 ms, uiScheduler)` exposed as a `RedrawRequested` observable the view binds to `AvaPlot.Refresh()` (the precise 30 FPS lock is Task 11).
- [x] Tests (`[AvaloniaFact]`, 6): add/remove/visibility mutate VM state; history loads the center current value; realtime batch updates the per-pen current value (rendering = manual). **VM/plottable split:** `TrendPenState` owns one pen's `Scatter`+`FillY` and the backing buffers + `IsVisible`/`CurrentValue`; `TrendChartViewModel` owns a bare `ScottPlot.Plot` (headless-constructable, no `AvaPlot`) plus the pen dictionary and the observable subscriptions — so the VM is unit-tested headless with `ImmediateScheduler`. The view (`TrendChartView`) is the only type touching `AvaPlot`.
- [x] Gate: full suite green (Core.Tests 37 + SemiPlot.Tests 20 = 57 passed).

### Task 8: Pen scale / axis model (Core)

**Files:** Create `SemiPlot/SemiPlot.Core/Trends/PenScaleModel.cs` + scale/mode types;
Create `SemiPlot/SemiPlot.Core.Tests/Trends/PenScaleModelTests.cs`.

- [x] Renderer-agnostic model emitting, per pen/group: `(Min, Max)` + autoscale mode + visibility + axis key. Single **active pen** on the primary axis; non-active autoscale individually; optional **shared group scale**. `PenScaleModel.Compute(settings, envelopes, activePenId, windowStart, windowEnd)` groups `PenScaleSettings` by `AxisKey` and emits one `PenScale` per axis (`AxisKey`, `PenIds`, `Min`, `Max`, `Mode`, `IsActive`, `IsVisible`, `IsLogarithmic`); the axis owning the active pen is `IsActive`, others autoscale on their own keys; same-key members share one range. Consumes the Task 3 `PenHistoryEnvelope` as plain input; touches no renderer.
- [x] Modes: `Auto` (5% padding, ±0.5 for a flat line), `Manual` (fixed `ManualMin`/`ManualMax`, swapped if inverted, data ignored), `AutoscaleToWindow` (fits only envelope columns whose timestamp lies in `[windowStart, windowEnd]`). Log axis **sanitizes ≤ 0** by dropping non-positive Min/Max before ranging and clamping the padded lower bound positive; NaN gap columns are skipped; empty/all-dropped axes fall back (0..1, or 1..10 for log).
- [x] Tests (`Core.Tests`, plain `[Fact]`, 10): active-pen surfaces its range; per-pen autoscale on separate axes; shared-group spans all members; window-autoscale; manual fixed; log sanitize drops ≤ 0 (+ no-positive fallback); hidden pen not visible; NaN gaps ignored.
- [x] Gate: `Core.Tests` green (46 passed). `slnx` build 0 errors.

### Task 9: Wire axis model to chart + axis editing + toolbar + shared-X invariant

**Files:** Modify `Chart/TrendChartViewModel.cs`, `TrendChartView.axaml(.cs)`;
Create `Toolbar/TrendToolbarView.axaml(.cs)`, `Toolbar/TrendToolbarViewModel.cs`;
Modify `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`.

- [x] Axis mechanism: each distinct-unit pen gets its own `IYAxis` via `AddLeftAxis`/`AddRightAxis`; a same-unit group shares one `IYAxis` (assign `plottable.Axes.YAxis`); non-active axes `IsVisible = false`; active-pen switch toggles `IsVisible` (no rebuild). Drive scaling via `SetLimitsY(min, max, IYAxis)`, not global `AutoScale`. **Where it lives:** the axis-application logic was extracted into `Chart/ChartAxisBinder.cs` (the VM was approaching the 300-line limit), which keeps `TrendChartViewModel` at ~290 lines; the VM holds the per-pen `PenScaleSettings`, the active-pen id, and the tracked X window, runs `PenScaleModel.Compute(...)`, and hands the result to the binder. **Deviation:** the parameterless adder overloads on `AxisManager` are `AddLeftAxis()` → `LeftAxis` and `AddRightAxis()` → `RightAxis` (both `: IYAxis`); the binder reuses the plot's built-in `Axes.Left` for the first key and alternates right/left for further keys. `AutoScaleY` is unused because the Core model already produces a padded auto range that is applied with `SetLimitsY`, so scaling stays per-axis and deterministic (no global `AutoScale`). `IsLogarithmic` is honored by the Core range computation; a renderer-side log tick generator is out of scope here.
- [x] **Shared-X invariant:** every plottable is pinned to `plot.Axes.Bottom` explicitly at creation (the plottable adders do not assign axes; the default `Axes.XAxis` is otherwise resolved lazily only at render, so it is set in `AddPen`); per-pen axes are Y-only; `EveryPlottable_UsesTheSharedBottomXAxis` asserts no per-pen X axis is ever created.
- [x] Axis gestures: double-click axis = autoscale (VM `AutoscaleAxis`); value entry = fixed limits (VM `SetAxisLimits`); duplicated in the toolbar. `Toolbar/TrendToolbarViewModel.cs` ReactiveUI commands: autoscale, set limits, layer selector, jump-to-now, sticky toggle (last two wired in Task 11 — exposed now as a no-op command and a local flag flip). `Toolbar/TrendToolbarView.axaml(.cs)` host; `MainWindowViewModel` builds the toolbar VM from the chart VM and `MainWindow.axaml` hosts it in the toolbar row. Clicking a pen sets the active pen via `SetActivePen`.
- [x] Tests (`[AvaloniaFact]`): VM axis-edit commands switch Auto/Manual + update `PenScaleSettings`; manual limits drive the owning `IYAxis`; same-group pens share one Y axis; distinct groups get separate axes; active-pen command updates state; first pen auto-activates; shared-X invariant. Toolbar tests: set-limits/autoscale commands flow to the active pen, sticky toggle flips, jump-to-now is a callable placeholder.
- [x] Gate: full suite green (Core.Tests 46 + SemiPlot.Tests 32 = 78 passed; `slnx` build 0 errors).

### Task 10: Time navigation & sticky state machine (Core)

**Files:** Create `SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs`;
Create `SemiPlot/SemiPlot.Core.Tests/Trends/TrendNavigationModelTests.cs`.

- [x] Pure model owning `[from, to]`, sticky flag, zoom width. `Pan`, `Zoom(factor, anchor)`, `JumpToNow()` (re-attach sticky, now at right edge), `DetachSticky()`, `OnLiveEdge(now)` (advance window when sticky, constant width). Pan past the live edge auto-detaches; clamp pan-back to first sample; zoom clamped 1 s … 1 year. **Representation:** `DateTime` (UTC) for `[From, To]`, `TimeSpan Width = To - From`; `now`/`firstSample` are inputs (no clock read), so the model is deterministic. `Pan(delta, now)` shifts the window keeping width and auto-detaches sticky when `now` leaves `[From, To]`; `Zoom(factor, anchor)` holds the anchor's relative position while clamping width to `[1 s, 1 year]`. Width clamp also enforced in the constructor.
- [x] Tests (`Core.Tests`, plain `[Fact]`, 12): sticky advance keeps width; not-sticky leaves window unchanged; jump-to-now → sticky, now at right edge; pan-past-live detaches; pan-while-inside keeps sticky; pan clamps at first sample; zoom clamps at 1 year and at 1 s; zoom holds anchor; constructor width clamp + invalid-window throw; non-positive zoom factor throws.
- [x] Gate: `Core.Tests` green (58 passed = 46 prior + 12 new). `slnx` build 0 errors (only transitive NU1903 advisory).

### Task 11: Wire navigation + render throttle + layer-by-zoom + live-edge fold

**Files:** Modify `Chart/TrendChartView.axaml.cs`, `TrendChartViewModel.cs`, `Toolbar/TrendToolbarViewModel.cs`.

- [x] Scroll = zoom, drag = pan onto `TrendNavigationModel`; sticky toggle + jump-to-now wired. Render throttled to **30 FPS**; realtime input coalesced at ≤ **10 Hz**; live edge advances the window only when sticky. **Where it lives:** navigation wiring is extracted into `Chart/ChartNavigationController.cs` (owns the `TrendNavigationModel`, the layer-by-zoom mapping, the live-edge advance, and raises `WindowChanged` carrying `[From, To]` + `Layer`), keeping `TrendChartViewModel` at 293 lines. **Interaction surface:** the AvaPlot's built-in `UserInputProcessor` is **disabled** (`Disable()`) and `TrendChartView` (the only AvaPlot-touching type) hooks `PointerWheelChanged`/`PointerPressed`/`PointerMoved`/`PointerReleased`; scroll zooms about the cursor anchor (pixel → OADate via `Plot.GetCoordinates`, OADate → UTC), left-drag pans, and `WindowChanged` drives `Plot.Axes.SetLimitsX`. **Throttle:** redraws stay locked to 30 FPS via the existing `Sample(33 ms, uiScheduler)` on the redraw subject; the ≤ 10 Hz realtime-input coalesce is the coordinator's `Buffer` window, moved to **100 ms** (on the data scheduler, so it never schedules a periodic timer on the UI/Immediate scheduler — the VM subscribes to realtime batches directly). The toolbar `JumpToNow`/`ToggleSticky` now call `Navigation.JumpToNow()`/`SetSticky(...)` and mirror the model's sticky state.
- [x] Layer follows zoom width (raw ≤ 1 h, minute ≤ 2 d, hour ≤ 60 d, day above) via `ChartNavigationController.LayerForWidth`; the VM's `WindowChanged` handler calls `coordinator.SetLayer(layer)` then `coordinator.RequestHistory(penIds, from, to)` to re-query through the decimation seam. At coarse layers (non-Raw) realtime points fold into the current decimation column (`TrendPenState.FoldRealtime` widens the last column's Min/Max band and moves its center) rather than appending raw points. Realtime application + the append/fold rule were extracted into `Chart/ChartRealtimeApplier.cs` to keep the VM within budget. **Deviation:** the ≤ 10 Hz coalesce required moving the realtime `Buffer` window default in `Bridge/TrendCoordinator.cs` from 33 ms to 100 ms (one line, plus a small `Chart/NavigationWindow.cs` event-payload record) — both outside the originally listed three files but the correct home for the realtime-input seam and the window/layer payload.
- [x] Tests (`[AvaloniaFact]`/`[Fact]`, +12): `ChartNavigationControllerTests` (zoom widens window, pan shifts/clamps, pan-past-live detaches sticky, set-sticky/jump-to-now re-anchor, live-edge advances only when sticky, layer follows zoom band); `TrendChartViewModelTests` (zoom drives coarser-layer re-query, pan re-queries shifted window, fold widens current column); `TrendToolbarViewModelTests` (sticky toggle + jump-to-now flow to the navigation controller).
- [x] Gate: full suite green (SemiPlot.Core.Tests 58 + SemiPlot.Tests 46 = 104 passed; `slnx` build 0 errors).

### Task 12: Vertical X-trace cursor

**Files:** Create `Chart/CursorReadoutModel.cs`; Modify `Chart/TrendChartView.axaml.cs`, `TrendChartViewModel.cs`;
Create `SemiPlot/SemiPlot.Core.Tests/Chart/CursorReadoutModelTests.cs` (model is renderer-agnostic).

- [x] Hover vertical line maps cursor X → each visible pen's center-channel value at X (interpolated); expose a per-pen value map for the legend; gap (null) handled. **Where it lives:** the renderer-agnostic math is `Core/Trends/CursorReadoutModel.cs` (`ReadAt(cursorTime, envelopes)` → `IReadOnlyDictionary<long, double?>`): exact column hit returns the column Center, an X between two finite columns is linearly interpolated, a NaN on either bounding column (or an exact hit on a NaN column) yields no value, and an X outside `[first, last]` yields no value (upper-bound found via binary search since timestamps are ascending). The view-side hookup stays thin: `TrendChartView` adds one `VerticalLine` plottable pinned to the bottom X axis, moves it on `PointerMoved` (when not dragging) and hides it on `PointerExited`; the VM exposes `CursorTime`/`CursorValues` (ReactiveUI properties for the Task 14 legend) and `MoveCursor`/`ClearCursor`, delegating visible-pen filtering to `Chart/ChartCursorReader.cs` so the VM stays near budget. **Deviation:** the model lives in `Core/Trends` (not `Chart/`) because `SemiPlot.Core.Tests` cannot reference UI — the Files line's `Chart/CursorReadoutModel.cs` was the UI convention; tests are at `SemiPlot.Core.Tests/Chart/CursorReadoutModelTests.cs` as planned.
- [x] Tests: X → correct per-pen values incl. interpolation + gaps (`Core.Tests`, plain `[Fact]`, 8): exact hit, linear interpolation between columns, cursor inside NaN gap, exact hit on a NaN column, before-first, after-last, empty envelope, multi-pen map (in-range interpolated + out-of-range null + gapped null).
- [x] Gate: `Core.Tests` green (66 passed = 58 prior + 8 new); full `slnx` build 0 errors; full suite green (Core.Tests 66 + SemiPlot.Tests 46 = 112 passed).

### Task 13: Dual Δt / Δy cursors

**Files:** Create `Chart/DeltaCursorModel.cs`; Modify `Chart/TrendChartView.axaml.cs`, `TrendChartViewModel.cs`,
`Toolbar/TrendToolbarViewModel.cs`; Create `SemiPlot/SemiPlot.Core.Tests/Chart/DeltaCursorModelTests.cs`.

- [x] Two placeable cursors; Δt and Δy. **Δy is reported only for the active/selected pen** (pens share X but have independent Y scales, so a global Δy is meaningless); toolbar toggle to enable/clear. **Where it lives:** the renderer-agnostic math is `Core/Trends/DeltaCursorModel.cs` (holds the two cursor times; `Place` cycles first→second→fresh; `Clear` resets; `Compute(activePenEnvelope)` returns the `Core/Trends/DeltaReadout` record = `Δt = |t2 - t1|` + `Δy = value(t2) - value(t1)`, reusing `CursorReadoutModel`'s interpolation/gap rules so Δy is null when either endpoint is a gap or out of range). View-state lives in `Chart/ChartDeltaCursorReader.cs` (owns the model + enable flag, resolves the active pen's envelope). **Deviation:** the model lives in `Core/Trends` (not `Chart/`, the Files line's UI convention) because `SemiPlot.Core.Tests` cannot reference UI — same precedent as Task 12's `CursorReadoutModel`; tests are at `SemiPlot.Core.Tests/Chart/DeltaCursorModelTests.cs` as planned. `TrendToolbarViewModel.ToggleDeltaCursorsCommand` is the enable/clear toggle; `TrendChartView` places cursors on left-click while enabled (instead of starting a pan) and draws two `VerticalLine`s. **Size note:** `TrendChartViewModel` grew from 330 to 369 lines (over the 300 soft limit) — the additions are a thin bindable surface (`DeltaCursorsEnabled`/`DeltaFirstCursor`/`DeltaSecondCursor`/`DeltaReadout`) plus two one-line delegating methods; the delta math is fully in Core and view-state in the reader, so no meaningful logic was added to the VM. Per the constraint the bindable surface was not churned (Task 14's legend will consume `DeltaReadout`).
- [x] Tests: Δt/Δy correct for two positions; clear resets. `DeltaCursorModelTests` (Core, plain `[Fact]`, 8): null before both placed, two-exact-columns Δt+Δy, absolute Δt regardless of order, interpolated endpoint Δy, gap endpoint → null Δy (Δt kept), out-of-range endpoint → null Δy, clear resets, third placement starts fresh. `TrendToolbarViewModelTests.ToggleDeltaCursorsCommand_EnablesAndDisablesOnChart` (`[AvaloniaFact]`) covers the toolbar toggle flowing to the chart.
- [x] Gate: full suite green (SemiPlot.Core.Tests 74 + SemiPlot.Tests 47 = 121 passed; `slnx` build 0 errors).

### Task 14: Grouped mini-legend

**Files:** Create `Legend/TrendLegendView.axaml(.cs)`, `Legend/TrendLegendViewModel.cs`;
Modify `MainWindow/MainWindow.axaml`; Create `SemiPlot/SemiPlot.Tests/UI/Legend/TrendLegendViewModelTests.cs`.

- [x] Grouped by pen group: checkbox (visibility) / color / name / current value; show value-at-cursor (Task 12) and the pen's scale range. Checkbox toggles visibility; row selection sets the active pen. **Where it lives:** all legend logic is in `Legend/TrendLegendViewModel.cs` (groups rows by `Pen.Group`) + per-row `Legend/TrendLegendRowViewModel.cs` (mirrors the chart VM's read surface and drives the two mutators); `TrendLegendGroupViewModel.cs` is the group container. The chart VM was given only a thin read surface (no legend logic): `ActivePenId` now raises change notifications, plus `ScaleRangeForPen(penId)` + a `ScalesRevision` notification fed from the already-computed `PenScale` list in `ApplyAxisModel`. The row binds `CursorValues` (Task 12) for value-at-cursor, `CurrentValue` (Task 7 `TrendPenState`) for the live value, and `ScaleRangeForPen` (Task 8/9 `PenScaleModel` output) for Min..Max. `TrendLegendView.axaml(.cs)` renders the grouped `ItemsControl`; a row's checkbox two-way binds `IsVisible` (propagates via `SetPenVisibility`, mirroring chart-side changes back) and a pointer press calls `SetActivePen`. Two small `IValueConverter`s (`HexColorToBrushConverter` for the color swatch, `ActiveToWeightConverter` to bold the active row) keep Avalonia media types out of the VMs. `MainWindowViewModel` builds the legend VM from the chart VM (like the toolbar) and `MainWindow.axaml` hosts `TrendLegendView` in a right-hand column beside the chart. **Deviation:** the row/group VMs and the two converters are extra files beyond the plan's `TrendLegendViewModel.cs` (one-class-per-file rule); the legend was hosted in a dedicated right-hand column (one of the two coherent-layout options the constraint offered) rather than the message-panel row. The chart VM grew from 369 to 403 lines — purely the thin read surface (no legend logic), still over the 300 soft limit as flagged in Task 13.
- [x] Tests (`[AvaloniaFact]`, 6): grouping by `Pen.Group`, checkbox visibility propagation to the chart, chart→row visibility mirroring, active-pen selection, value-at-cursor binding reflecting `CursorValues`, current-value binding reflecting a history load.
- [x] Gate: full suite green (SemiPlot.Core.Tests 74 + SemiPlot.Tests 53 = 127 passed; `slnx` build 0 errors).

### Task 15: Line styles, gap rendering, local-time axis

**Files:** Modify `Chart/TrendChartViewModel.cs`, `TrendChartView.axaml.cs`,
`SemiPlot/SemiPlot.Core/Trends/Pen.cs` (per-pen line-style flag if absent); Modify relevant tests.

- [x] Per-pen stepped vs interpolated (`Scatter.ConnectStyle`); nulls render as visible gaps (`OnNaN = Gap`, already segmented); time axis in computer local time (UTC→local at the boundary). Bad-quality already mapped to null in Task 3. **Where it lives:** a `PenLineStyle { Interpolated, Stepped }` enum on Core `Pen` (default `Interpolated`, also threaded through `SyntheticPen`/`SyntheticPenCatalog` so the stub can configure per-pen — the Dampers group is `Stepped`); the style→`ConnectStyle` map is the tiny `Chart/PenLineStyleMap` (`Stepped`→`StepHorizontal`, else `Straight`), applied in `TrendPenState`'s constructor (the plottable owner). **Gaps:** both the history path (`TrendPenState.LoadHistory`, envelope NaN) and the realtime path (`AppendRealtime`, `null`→`double.NaN`) feed NaN into the same center buffer, so the default `Straight` path strategy breaks the line at the gap consistently. **Local time:** all UTC↔OADate conversion is funneled through the new `Chart/LocalTimeAxis` (`ToAxis` UTC→local-OADate, `FromAxis` local-OADate→UTC) used at every render boundary — plotted X (`TrendPenState`), axis limits, cursor/delta line X and the cursor anchor (`TrendChartView`) — so plotted X, the nav window, the cursor and the readout share one local domain with no double-convert; a `DateTimeAutomatic` tick generator is assigned to the existing bottom axis (preserving the shared-X axis instance) so labels read local time. The VM is untouched (stays 403 lines; no logic added per the constraint).
- [x] Tests: line-style flag respected; gap segmentation at nulls; UTC→local conversion. (`TrendChartViewModelTests`: stepped→`StepHorizontal`, interpolated→`Straight`, history interior NaN splits into two segments, realtime null→NaN; `LocalTimeAxisTests`: UTC→local OADate, UTC round-trip for the cursor readout, Unspecified-kind treated as UTC.)
- [x] Gate: full suite green (SemiPlot.Core.Tests 74 + SemiPlot.Tests 60 = 134 passed; `slnx` build 0 errors).

### Task 16: Update architecture docs

**Files:** Modify `docs/architecture/overview.md`, `docs/architecture/charting.md`, `docs/architecture/trend-interaction.md`.

- [x] `overview.md`: replace the uPlot/WebView2 stack with Avalonia 11.3.x + ScottPlot.
- [x] `charting.md`: **rewrite** the renderer section to the new **Scatter + FillY** ScottPlot pattern (NOT DataLogger) on Avalonia 11.3.x; rewrite the "Frontend module layout" (JS modules → Avalonia views/VMs) and data-contract sections.
- [x] `trend-interaction.md`: lift DRAFT status; reconcile changed `[OPEN]`/`[LATER]`. Docs only. **Also updated** `data-integration.md` (dead JSON bridge section marked superseded → typed in-process records; Simple-Scada OPC UA/SQL content kept) and `architecture/README.md` (stale uPlot/WebView2/WPF stack table + doc list).

### Task 17: Verify acceptance criteria

- [x] Verify all MVP requirements from `trend-interaction.md` (sticky/jump/pan, active-axis model, autoscale modes, log sanitize, X-trace + Δ cursors, decimation envelope, legend, line styles, gaps, local time, shared-X invariant). **All implemented** with source + test evidence; coverage checklist recorded in the progress log (every spec item maps to a Core model or chart wiring class plus at least one passing test).
- [x] Verify no residual WebView2/uPlot/JS references (grep). Run `dotnet test SemiPlot/SemiPlot.slnx`. Run `dotnet format SemiPlot/SemiPlot.slnx`. **Grep clean:** no `WebView2`/`uPlot`/`ITrendChannel`/`TrendMessages`/`UseWPF` in any `.cs`/`.csproj`/`.props`/`.axaml` under `SemiPlot/`; the only hits are historical mentions in `claude.md`/`readme.md` (the `Web/` JS folder is gone). **Suite green:** SemiPlot.Core.Tests 74 + SemiPlot.Tests 60 = 134 passed, 0 failed. **Build:** `slnx` 0 errors (4 transitive NU1903 advisories only). **Format:** normalized UTF-8 BOM/whitespace across 6 pre-existing-drift files, committed separately (`style: dotnet format normalization`); suite re-confirmed green afterward.
- [x] ⚠️ Watched `TrendChartViewModel` size across Tasks 7/9/11/12/13. **FLAG (for the upcoming review phase):** `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs` is **403 lines**, over the 300-line soft limit. Per the verification constraint, no risky decomposition is attempted here; per-feature logic was already extracted into Core models and `Chart/Chart*` helper classes (binder, navigation controller, realtime applier, cursor/delta readers), leaving the VM a thin orchestration + bindable surface. Decomposition deferred to the review phase.

### Task 18: [Final] Documentation & plan close-out

- [x] Update `CLAUDE.md` only if new conventions emerged (ReactiveUI, headless test harness, two test projects). **Done:** rewrote the stack line (Avalonia 11.3.x + ReactiveUI + ScottPlot.Avalonia, no WPF/WebView2/JS), the Test section (two projects: `SemiPlot.Core.Tests` xunit.v3 / `SemiPlot.Tests` Avalonia.Headless + xunit v2 via `TestAppBuilder`, `[AvaloniaFact]` for ReactiveUI/ScottPlot), the command list, the DI extension-method list (`AddTrends()` dropped) + UI-scheduler capture seam, and replaced the dead "Frontend (Web / uPlot)" section with "UI (Avalonia / ScottPlot)".
- [x] (deferred to end of run — moved after review/finalize phases) Move this plan to `docs/plans/completed/`.

## Post-Completion

*Items requiring manual intervention or external systems — informational only.*

**Manual verification:**
- Run the app: smooth pan/zoom across 1 s … 1 year at the 30 FPS lock with up to 50 stub pens; sticky/
  detach/jump-to-now feel; axis double-click vs value-entry; X-trace and Δ cursors; legend grouping and
  value-at-cursor; spike survives decimation when zoomed out.
- **FPS watch:** with the worst-case all-distinct-unit pen set, watch frame time — ScottPlot
  `RegenerateTicks` runs per distinct Y axis every frame regardless of `IsVisible`; mitigate via
  shared-group axes (realistic distinct-axis count is well below 50 because groups share a scale, e.g. 16
  heaters on one axis).

**External / later (out of MVP):**
- Real `SimpleScadaDataProvider` (OPC UA realtime + PostgreSQL history); confirm whether the DB stores
  pre-trimmed/layered data and move decimation server-side if so.
- IntelliJ/JetBrains-style theming (north-star).
- View persistence (named pen sets / layouts), export / snapshot / print, alarm-event overlays.
