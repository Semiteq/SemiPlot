# Avalonia 12 and xunit v3

## Overview

`SemiPlot.UI` runs on Avalonia 11.3.8 and `ScottPlot.Avalonia` 5.1.57; `SemiPlot.Tests` runs on
xunit 2.9.3 because `Avalonia.Headless.XUnit` 11.3.8 depends on `xunit.core`. `CLAUDE.md` already
names the end state — both test projects on xunit v3 — and this slice reaches it.

The move is forced as one step. Verified against the shipped nuspec: `ScottPlot.Avalonia` 5.1.59
depends on `Avalonia` 12.0.0 and `Avalonia.Skia` 12.0.0, so there is no intermediate pairing where
the plotting control and the framework disagree. The framework bump and the ScottPlot bump are the
same bump.

Nothing about the application's behaviour changes. This is a version move and the compile and test
fixes it forces.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice avalonia-12-bump

Current pins, `SemiPlot/Directory.Packages.props`:

| Package | Now | Target |
| --- | --- | --- |
| `Avalonia`, `.Desktop`, `.Headless`, `.Headless.XUnit`, `.Themes.Fluent`, `.Win32`, `.Skia` | 11.3.8 | 12.0.5 |
| `ReactiveUI.Avalonia` | 11.3.8 | 12.0.3 |
| `ScottPlot.Avalonia` | 5.1.57 | 5.1.59 |
| `xunit` (2.9.3) in `SemiPlot.Tests` | 2.9.3 | replaced by `xunit.v3` 3.2.2 |

The targets are not the newest available — `Avalonia.Headless.XUnit` is at 12.1.1 and
`ReactiveUI.Avalonia` at 12.1.1. They are the versions a sibling repository of this author's,
`C:/Users/admin/projects/SemiStep`, already ships: `SemiStep/Directory.Packages.props` pins Avalonia
12.0.5, `Avalonia.Headless.XUnit` 12.0.5, `ReactiveUI.Avalonia` 12.0.3 and `xunit.v3` 3.2.2, and
`SemiStep/SemiStep.Tests/SemiStep.Tests.csproj` carries `xunit.v3` and `Avalonia.Headless.XUnit`
together in one project referencing its Core and its UI. A proven pairing beats a newer one for a
move nobody can watch.

Scale:

- 102 `[AvaloniaFact]` / `[AvaloniaTheory]` attributes across 12 files
- 231 plain `[Fact]` / `[Theory]` across 28 files in the same project
- `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs` carries `[assembly: AvaloniaTestApplication]`
- `SemiPlot.Tests.Data` is already xunit v3 and is not touched by this slice

**The two test projects do not merge.** `SemiPlot.Tests.Data` stays plain `net10.0` because the
`data-tests` CI job runs on `ubuntu-latest`, the only runner that can start a container, and a
project referencing `SemiPlot.UI` (`net10.0-windows`, `UseWin32`) cannot build there. What dissolves
is the xunit-major mismatch, after which `SemiPlot.Tests` may take a project reference on
`SemiPlot.Tests.Data` and consume the container harness — which is what makes the end-to-end journeys
cheap in `postgres-live-edge-and-demo`. Taking that reference is not this slice's work.

## Development Approach

- **testing approach**: not applicable in the usual sense — this slice adds no behaviour and writes
  no new test. Its evidence is that the existing suite passes unchanged.
- **CRITICAL: a test the bump forces to change is a finding, not a diff to appease.** A headless
  dispatcher whose semantics moved surfaces first as a test that needs an edit to stay green. Every
  such edit is recorded with what it revealed, in the plan and in the progress file. An edit that
  cannot be explained is a blocker, not a fix.
- **One commit per unit of work inside one pull request**, so each fix is isolated and readable:
  packages and compile fixes; the xunit v2 → v3 conversion; each regression the bump surfaced. Six
  landed. What that ordering does *not* buy is an automated bisect — see "What the history supports"
  under Task 3.
- No behaviour changes, no new features, no provider work.

## Acceptance Evidence

**Evidence 1 — the guards pass unchanged.** This is the point of the slice's ordering.
`ui-render-and-input-guards` shipped six tests that were green on Avalonia 11: two reading rendered
pixels through ScottPlot's rasteriser, four driving pointer events through Avalonia's input pipeline.
They must pass on Avalonia 12 **with no edit to their assertions**:

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ChartGapRender|FullyQualifiedName~PointerInput"
```

`git diff master...HEAD -- SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs SemiPlot/SemiPlot.Tests/UI/Chart/ChartPointerInputTests.cs SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapPointerInputTests.cs`
must show nothing beyond what the xunit v3 conversion mechanically requires. An assertion edited to
stay green means the two stacks do **not** behave alike, and that is the finding this slice exists to
surface.

**Evidence 2 — the whole suite passes.** `dotnet test SemiPlot.slnx` reports zero failures.
Measured at `506cf83`, the branch point: `SemiPlot.Tests` 368 passed / 0 skipped,
`SemiPlot.Tests.Data` 397 passed / 0 skipped, with Docker running and `semibase` on `PATH`. The
counts must not fall. A test that cannot be made to pass is reported, not deleted.

**Evidence 3 — the versions moved.** `SemiPlot/Directory.Packages.props` carries the targets above,
and `dotnet list SemiPlot.slnx package` shows no Avalonia 11 or `xunit` 2 anywhere.

**Evidence 4 — the application still starts against a real archive.** Raise the application bench
from `docs/architecture/bench.md`, point the connection file at it, run the application, and confirm
from the server that it read history: `idx_tup_fetch` on the seeded day's partition is non-zero and
`seq_scan` is zero. This is the only check that exercises the Win32 backend, which nothing headless
reaches. Record the numbers.

**Evidence 5 — format and encoding.** `dotnet format SemiPlot.slnx --verify-no-changes` exits 0 and
every tracked `.cs` file still begins `ef bb bf`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix, blockers with ⚠️

## Solution Overview

**The packages move together because they must.** `ScottPlot.Avalonia` 5.1.59's nuspec pins
`Avalonia` 12.0.0, so a configuration with ScottPlot 5.1.57 on Avalonia 12, or 5.1.59 on Avalonia 11,
is not a supported pairing. There is no smaller step.

**The conversion is mechanical, and where it is not, that is information.** xunit v3 keeps `[Fact]`,
`[Theory]` and `[InlineData]`; `Avalonia.Headless.XUnit` 12 keeps `[AvaloniaFact]` and
`[AvaloniaTheory]`. What changes is the project shape — an xunit v3 test project is an executable —
and `IAsyncLifetime`, whose methods return `ValueTask` in v3 rather than `Task`. `SemiPlot.Tests.Data`
is already v3 and is the in-repo reference for both.

**What the guards are for.** They were written on Avalonia 11 precisely so this slice could be
measured. Carried across unchanged and still green, they say the rasteriser still breaks a line on
`NaN` and Avalonia still routes a drag, a wheel and a capture loss the way the view expects. That is
the whole argument for doing the bump in this order.

**What they do not cover, restated so the slice does not overclaim.** `ChartGapRenderTests` reaches
SkiaSharp with no Avalonia in the loop, so it guards the ScottPlot half alone. The Win32 backend —
windowing, DPI, real cursor changes, the render-thread interplay — is exercised by nothing headless,
which is why Evidence 4 runs the real application. Visual legibility is not a machine question and
waits for the demo stand.

## Implementation Steps

### Task 1: Move the packages and make it compile

**Files:**
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: whatever production files the compiler names

- [x] set the seven Avalonia packages to 12.0.5, `ReactiveUI.Avalonia` to 12.0.3 and
      `ScottPlot.Avalonia` to 5.1.59; leave `xunit` alone in this task
- [x] restore and build, and fix what the compiler reports in `SemiPlot.UI` — record each fix with
      what changed in the framework to require it
- [x] read `C:/Users/admin/projects/SemiStep/SemiStep/SemiStep.UI` when an API's replacement is not
      obvious; it is the same author's application already on Avalonia 12
- [x] do not touch a test file in this task; `SemiPlot.Tests` will not build until Task 2 and that is
      expected
- [x] confirm `SemiPlot.UI`, `SemiPlot.Core`, both data-source projects, the seeder and
      `SemiPlot.Tests.Data` all build
- [x] commit this task alone, so a bisect can separate the framework move from the test conversion

### Task 2: Convert `SemiPlot.Tests` to xunit v3

**Files:**
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`
- Modify: test files only where the conversion forces it

- [x] replace the `xunit` 2.9.3 package reference with `xunit.v3`, matching what
      `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj` already does, and set
      `Avalonia.Headless.XUnit` to 12.0.5
- [x] make the project shape what xunit v3 needs — `SemiPlot.Tests.Data` is the in-repo reference for
      every property involved
- [x] fix what the conversion forces: `IAsyncLifetime` returning `ValueTask`, any `Assert` overload
      that moved, any runner configuration
- [x] confirm `[AvaloniaFact]` and `[AvaloniaTheory]` still resolve and still run
- [x] remove the `xunit` 2.9.3 pin from `Directory.Packages.props` once nothing references it
- [x] commit this task alone

### Task 3: Read what the bump forced

- [x] list every test whose body or assertions the bump forced to change, and for each one say what
      it revealed about the framework — this is the slice's real output
- [x] confirm the six guard tests from `ui-render-and-input-guards` pass with no assertion edited;
      if one needed an edit, stop and report it as a finding rather than committing the edit
- [x] run the full suite and record the counts against the branch point
- [x] commit anything this task changed as the third commit

#### What the bump forced

Whole branch through `af22e9e`, `git diff master..af22e9e`: nine files besides this plan. Two are
production (`App.axaml.cs`, `TrendChartView.axaml.cs`), two are project files
(`SemiPlot.Tests.csproj`, `SemiPlot.UI.csproj`), one is the package manifest
(`Directory.Packages.props`), one is a view (`TrendToolbarView.axaml`), two are documentation
(`CLAUDE.md`, `docs/architecture/bench.md`), and **exactly one is a test file**:

| Test file | What changed | What it revealed |
| --- | --- | --- |
| `SemiPlot.Tests/UI/Chart/ChartHistoryRequestDebouncerTests.cs` | `firstQueryStarted` and `secondApplied` take `TaskCreationOptions.RunContinuationsAsynchronously`; `Task.Delay(50)` takes `TestContext.Current.CancellationToken` | Finding 2 below (harness semantics), plus one line of pure analyzer churn (xUnit1051, which `xunit.analyzers` ships for both majors and gates at runtime on `HasV3References`) |

No other test file on the branch is touched at all — not a body, not an assertion, not a `using`.
The 102 `[AvaloniaFact]`/`[AvaloniaTheory]` methods and the 231 plain `[Fact]`/`[Theory]` methods
carried across two major versions of Avalonia and one of xunit with no source edit.

The production edits are recorded here because a reader looking for the bump's cost will look here:

- `App.axaml.cs`: `UseReactiveUI()` no longer has a parameterless overload in `ReactiveUI.Avalonia`
  12; the `Action<ReactiveUIBuilder>` callback is mandatory. Compile error, fixed as
  `.UseReactiveUI(_ => { })`.
- `TrendToolbarView.axaml`: `TextBox.Watermark` is `[Obsolete]` in Avalonia 12, renamed to
  `PlaceholderText`. Warning only; same property, same behaviour.
- `TrendChartView.axaml.cs`: finding 1 below. This one is a shipped regression, not a rename.
- `App.axaml.cs` and `SemiPlot.UI.csproj`: `.UseHarfBuzz()` in the builder chain and an
  `Avalonia.HarfBuzz` package reference beside it — finding 3 below, the second shipped regression.

##### What the history supports

Each fix sits in its own commit and reads on its own, which is what makes the branch reviewable. An
automated bisect is **not** among the things it buys, and nothing here should be read as claiming it:

- `609201d` and `9a276d0` do not build. `Directory.Packages.props` still pins `xunit` 2.9.3 there
  while `Avalonia.Headless.XUnit` is already 12.0.5, so `SemiPlot.Tests` fails with CS0433 on the
  duplicated xunit types. The pin only leaves at `0f48c07`. `git bisect run dotnet test` cannot
  classify either commit.
- The wheel fix `9a276d0` lands *before* the conversion `0f48c07`, so no commit on this branch ever
  exhibits the wheel regression as a runnable red test.

#### Finding 1 — ScottPlot 5.1.59 silently killed wheel zoom in the running application

**This is a production regression the guard suite caught, not a test problem.** Every claim below
was re-verified for this task by decompiling the packages out of `~/.nuget/packages`, independently
of what Task 2 reported.

1. **The property is new.** `ScottPlot.Avalonia.AvaPlot` in 5.1.57 has no `HandleMouseWheelEvent`.
   5.1.59 adds `public bool HandleMouseWheelEvent { get; set; } = true;`, doc-commented "Prevent
   mouse wheel events from bubbling up to parent controls (e.g., ScrollViewer)".
2. **The handler now marks every wheel event handled.** 5.1.57's `OnPointerWheelChanged` ends after
   `UserInputProcessor.ProcessMouseWheel(pixel, num)`. 5.1.59's ends with
   `e.Handled = HandleMouseWheelEvent;` — **outside** the `if (num != 0f)` guard, so it runs on every
   wheel event, and not conditioned on `UserInputProcessor` being enabled. `TrendChartView` calls
   `UserInputProcessor.Disable()`, which therefore does not cover it.
3. **Avalonia runs class handlers before instance handlers on the same element.**
   `Interactive.BuildEventRoute` walks from the source up the parent chain and, for each element,
   calls `eventRoute.AddClassHandler(interactive)` **before** `interactive.AddToEventRoute(...)`.
   `EventRoute.RaiseEventImpl` invokes the class handler through `_event.InvokeRaised(target, e)`
   and gates the instance handler on `(!e.Handled || routeItem.HandledEventsToo)`. The class handler
   is not ungated — `RoutedEvent.AddClassHandler` puts the same
   `(!e.Handled || handledEventsToo)` test inside its own subscription, one level down, so a
   *parent's* class handler is skipped once `Handled` is set. What matters here is the same element:
   `InvokeRaised` for a given target runs before that target's instance handlers, so `Handled` is
   still false when the class handler reads it. `AvaPlot.OnPointerWheelChanged` is reached through
   the class handler `InputElement` registers in its static constructor
   (`PointerWheelChangedEvent.AddClassHandler((InputElement x, PointerWheelEventArgs e) => x.OnPointerWheelChanged(e))`),
   and `TrendChartView` subscribes with `_plotControl.PointerWheelChanged += OnPointerWheelChanged`,
   which is `AddHandler(..., handledEventsToo: false)`. Same element, class handler first, sets
   `Handled`, instance handler skipped. This routing code is identical between Avalonia 11.3.8 and
   12.0.5.
4. **Avalonia's own wheel routing did not move.** `HeadlessWindowExtensions.MouseWheel` and
   `HeadlessWindowImpl.MouseWheel` decompile identically in `Avalonia.Headless` 11.3.8 and 12.0.5.

So the failure of `ChartPointerInputTests.WheelUpThenWheelDown_NarrowThenWidenTheNavigationWindow`
(`widthAfterZoomIn == widthBefore`, assertions untouched) was reporting a real application defect:
on ScottPlot 5.1.59 the chart stops zooming on the wheel, in the running application, silently.
Fixed production-side with one line in `TrendChartView.InitializeComponent`:
`_plotControl.HandleMouseWheelEvent = false;` — commit `9a276d0`, filed as `fix(chart)` rather than
`test:` so a bisect lands on it.

**Correction to what Task 2 recorded.** Task 2 wrote "Verified identical
`HeadlessWindowExtensions.MouseWheel` and `HeadlessWindowImpl.MouseWheel` IL". The two `MouseWheel`
methods are indeed identical, but the private helper they call,
`HeadlessWindowExtensions.RunJobsOnImpl`, is **not**: 11.3.8 does a fixed
`RunJobs / ForceRenderTimerTick / RunJobs / action / RunJobs`, while 12.0.5 runs a local
`RunJobsAndRender()` — `RunJobs` plus `ForceRenderTimerTick`, up to ten times until
`HasJobsWithPriority(DispatcherPriority.MinimumActiveValue)` is false — both before and after the
action. The phase *after* the input event is where the two differ most: on 11.3.8 it is a bare
`RunJobs` with no render tick at all, so it gained rendering it never had, not merely more of it.
That does not change how a wheel event is *routed*, so the conclusion holds, but every headless
input helper now pumps the dispatcher harder than on Avalonia 11. Anyone chasing a future headless timing difference should
start there rather than assume the harness is unchanged.

#### Finding 2 — xunit v3 removed the per-test SynchronizationContext

**Mechanism, verified statically and empirically.**

- xunit v2 `TestInvoker<T>.InvokeTestMethodAsync` unconditionally does
  `SetSynchronizationContext(new AsyncTestSyncContext(oldSyncContext))` around the test body, and
  `AsyncTestSyncContext.Post` forwards to `innerContext.Post` when it holds an inner context and
  otherwise hands the continuation to `XunitWorkerThread.QueueUserWorkItem`. Under a plain v2 runner
  the captured outer context is null, so in this suite it never resumed inline.
- xunit v3 has no `AsyncTestSyncContext` at all.
  `XunitTestAssemblyRunnerBaseContext.SetupParallelism` installs a `SynchronizationContext` **only**
  when `ParallelAlgorithm == Aggressive`, and then it is a `MaxConcurrencySyncContext`. The default
  is Conservative, which installs a `SemaphoreSlim` and no context; the per-test `runTest` then
  calls `SynchronizationContext.SetSynchronizationContext(syncContext)` with whatever was ambient,
  i.e. `null`.
- Measured with a throwaway probe run against this branch: a plain `[Fact]` observes
  `SynchronizationContext.Current == null`, and an `await` on a bare `TaskCompletionSource` resumes
  on the same managed thread that called `SetResult`.

That is what hung
`ChartHistoryRequestDebouncerTests.StaleResponse_IsDroppedWhenANewerWindowSupersedesAnInFlightQuery`
— and it hung the whole suite, not one test. The test body awaits `secondApplied.Task`; the
debouncer's apply callback completes it; with no context the test body resumed inline on the
callback's thread and re-entered the Rx pipeline from inside its own notification. Fixed at gate
construction only, assertions and logic untouched:
`new TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`.

**Refinement Task 2 did not have.** The same probe shows an `[AvaloniaFact]` runs with
`SynchronizationContext.Current == Avalonia.Threading.AvaloniaSynchronizationContext`, installed by
`Avalonia.Headless.XUnit`. The hazard is confined to plain `[Fact]`/`[Theory]` bodies; the 102
Avalonia-attributed tests still have a posting context. That is why exactly one test was hit.

**Latent-hazard sweep, done for this task.** The hang shape needs a *test body* to await a
`TaskCompletionSource` that *production or callback code* completes. Across both test projects
there are exactly three `await <tcs>.Task` sites, all in `ChartHistoryRequestDebouncerTests`;
`SemiPlot.Tests.Data` contains no `TaskCompletionSource` at all. Two of the three are the hazardous
direction and now carry `RunContinuationsAsynchronously`. The third, `firstQueryGate`, is awaited
**inside** the production query lambda and completed **by** the test body, which is the safe
direction. The three gates on `FakeDataProvider` (`PensGate`, `ExtentGate`, `HistoryGate`) are
likewise awaited by production code and completed by the test body — and several tests in
`TrendChartViewModelTests` assert immediately after `HistoryGate.SetResult(...)` with no scheduler
advance, so inline continuation there is load-bearing. **Do not add
`RunContinuationsAsynchronously` to those gates.** No latent hang remains.

#### Evidence recorded by this task

- Guard diff:
  `git diff master...HEAD -- SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs SemiPlot/SemiPlot.Tests/UI/Chart/ChartPointerInputTests.cs SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapPointerInputTests.cs`
  produces **no output**. All six guards pass with zero edits, including zero conversion churn.
- `dotnet test SemiPlot.slnx`: `SemiPlot.Tests` 368 passed / 0 failed / 0 skipped;
  `SemiPlot.Tests.Data` 397 passed / 0 failed / 0 skipped. Both exactly the branch point
  (`506cf83`).
- `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

#### Operational note for anyone running this suite

An xunit v3 test that hangs keeps `SemiPlot.Tests.exe` running and locked, and the next build fails
with MSB3027/MSB3021 until that process is killed. This is new with the executable project shape v3
requires, and it is how a single deadlocking test blocks the whole loop rather than just failing.

### Task 4: Verify acceptance criteria

- [x] run Evidence 1 and confirm the guard diff is conversion-mechanical only
- [x] run Evidence 2 and record both counts
- [x] run Evidence 3 and confirm no Avalonia 11 or xunit 2 package remains
- [x] run Evidence 4: raise the bench, run the application, record `idx_tup_fetch` and `seq_scan`
- [x] run Evidence 5

#### What the evidence measured

| Evidence | Result |
| --- | --- |
| 1 — guards unchanged | `git diff master...HEAD` over the three guard files produces no output. The filtered run reports 7 passed / 0 failed (2 render, 5 pointer — the plan's "six" undercounts `ChartPointerInputTests`, which holds three) |
| 2 — whole suite | `SemiPlot.Tests` 368 passed / 0 failed / 0 skipped; `SemiPlot.Tests.Data` 397 passed / 0 failed / 0 skipped. Both exactly the branch point |
| 3 — versions moved | Avalonia and its satellites 12.0.5, `ReactiveUI.Avalonia` 12.0.3, `ScottPlot.Avalonia` 5.1.59, `xunit.v3` 3.2.2. No xunit 2 anywhere, direct or transitive. The one 11.x string in the transitive graph is `Avalonia.BuildServices` 11.3.2, which `Avalonia` 12.0.5's own nuspec pins — a build-time helper on its own version stream, not a framework leftover |
| 4 — real application | `tp2026m07d31`: `idx_scan` **48**, `idx_tup_fetch` **19 808**, `seq_scan` **0**. `semiplot_tags`: 3 sequential scans, which is the planner's choice on an 8-row table. Two idle `semiplot_reader` connections in `pg_stat_activity` while the application ran; `semiplot.log` never created, which is a clean start at the `Warning` floor. Both stacks were then measured against the same freshly seeded bench, each left untouched and sampled once the startup burst settled: **Avalonia 11 and Avalonia 12 read exactly the same 48 / 19 808 / 0**. The 19 840 recorded earlier was not a controlled measurement — that run was left going on a machine whose screen was locked. See the two properties of this number below |
| 5 — format and encoding | `dotnet format SemiPlot.slnx --verify-no-changes` exits 0; all 203 tracked `.cs` files begin `ef bb bf` |

#### What the bench number means

Two properties make the Evidence 4 reading usable as a baseline rather than an anecdote:

- **The startup burst is deterministic.** The 48 index scans decompose exactly: 2 extent queries ×
  2 lateral subqueries × 8 pens = 32, plus 2 history queries × 8 pens = 16.
- **Interaction inflates it, heavily.** A sample taken while a person scrolled the chart read 120
  scans and 121 175 rows — the debouncer re-querying on every window change. Any bench measurement
  must be taken on an untouched window, or it measures the operator instead of the application.

#### Finding 3 — Avalonia 12 needs `UseHarfBuzz()` and no test can say so, commit `90f3c63`

**Evidence 4 caught a second production regression, and it is the one that justifies the evidence
item.** The first launch of the bumped application died before its window existed:

```
System.InvalidOperationException: No text shaping system configured. Consider calling UseHarfBuzz().
   at Avalonia.AppBuilder.Setup()
```

`App.BuildAvaloniaApp` composes the platform explicitly — `.UseWin32().UseSkia()` — rather than
through `UsePlatformDetect()`. On Avalonia 11 `UseSkia()` brought a text shaper with it; on 12 it
does not, and `Avalonia.HarfBuzz` must be registered by name.

**Both escape hatches were there and neither was reached.** `Avalonia.Desktop` 12.0.5's
`UsePlatformDetect()` calls `LoadHarfBuzz(builder)` as its very first statement, before any
per-OS branch — an application composing the platform that way never meets this. And
`Avalonia.Headless` 12.0.5's `UseHeadless` ends with
`HarfBuzzApplicationExtensions.UseHarfBuzz(...)` wrapping everything it registers, which is exactly
*why* no test saw the failure: the headless platform always has a shaper, so all 368 tests, the
seven guards included, passed against an application that could not start. That also names the
alternative fix that was never weighed — switching `BuildAvaloniaApp` to `UsePlatformDetect()`.
It was not taken: naming the backend keeps this Windows-only application off platform detection at
startup, and `SemiStep`, the sibling repository already on Avalonia 12, carries
`.UseWin32().UseSkia().UseHarfBuzz()`.

Fixed production-side: `Avalonia.HarfBuzz` 12.0.5 pinned in `Directory.Packages.props` and
referenced by `SemiPlot.UI`, and `.UseHarfBuzz()` added to the builder chain between `UseSkia()` and
`UseReactiveUI()`. The reference resolves nothing new — `Avalonia.Desktop` 12.0.5 already lists
`Avalonia.HarfBuzz` 12.0.5 among its own dependencies, so the package was in the graph either way;
it is kept as an explicit pin because the code names the assembly. **Only the `.UseHarfBuzz()` call
was load-bearing.** Evidence 4 then read history on the first try.

**The gap that let this ship is closed.** `SemiPlot.Tests/TestAppBuilder` composes
`UseHeadless().UseReactiveUI()` and shares nothing with `App.BuildAvaloniaApp`, so no test reached
any desktop-only registration. `App.BuildAvaloniaApp` is now `internal` (`SemiPlot.UI.csproj`
already carries `InternalsVisibleTo("SemiPlot.Tests")`) and
`SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests` asserts that
`RenderingSubsystemInitializer`, `WindowingSubsystemInitializer` and
`TextShapingSubsystemInitializer` are all non-null on the composed builder. `AppBuilder.Configure`
only constructs the builder and each `Use*` call only stores a delegate, so the test initialises no
platform; `LogToTrace()` is the one call with an immediate effect — it writes `Logger.Sink` — and
the test saves and restores the sink around the call so the process is left as found. Verified by
commenting out `.UseHarfBuzz()`: the test fails with *"Expected
builder.TextShapingSubsystemInitializer not to be <null>."*

### Task 5: [Final] Update documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/bench.md` if a guard's mechanics changed

- [x] rewrite `CLAUDE.md`'s test-split section: the exit path is taken, both projects are on xunit
      v3, and the split now exists only because `data-tests` runs on the one runner that can start a
      container. Delete the version evidence that no longer describes anything
- [x] record what the bump forced, if anything, where a reader of the guards would need it
- [x] ➕ bring the architecture docs onto the shipped stack: `docs/architecture/overview.md`,
      `README.md`, `charting.md` and `trend-interaction.md` all pinned Avalonia 11.3.8 /
      `ScottPlot.Avalonia` 5.1.57, and two of them stated that no explicit `UseHarfBuzz()` call
      exists. `trend-interaction.md` also carries the wheel caveat now, beside the
      `UserInputProcessor.Disable()` rule it qualifies
- [x] move this plan to `docs/plans/completed/` — **not done here, by instruction.** Archiving is
      delivery work and belongs to whoever ships the branch; the plan stays at
      `docs/plans/20260820-avalonia-12-bump.md`

## Post-Completion

*Items requiring manual intervention — no checkboxes, informational only*

**Manual verification.** Evidence 4 is as close as this slice gets: the real application, the real
Win32 backend, against a real archive, checked from the server rather than from a screen. Whether the
chart *looks* right on Avalonia 12 — fonts, theme, cursor, DPI — is not a machine question and waits
for the demo stand.

**What the next slices inherit.** `SemiPlot.Tests` may now take a project reference on
`SemiPlot.Tests.Data` and consume the container harness directly, which is what makes the end-to-end
journeys in `postgres-live-edge-and-demo` affordable. Taking that reference belongs to the slice that
needs it.

**Remaining slices.** After this slice: postgres-gap-reconstruction, postgres-live-edge-and-demo.

**Executed by exec:**

- branch: avalonia-12-bump

## Verify it yourself

**The suite.** `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` 369 passed / 0 skipped,
`SemiPlot.Tests.Data` 397 passed / 0 skipped, zero failures, with Docker running and `semibase` on
`PATH`. `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**The guards crossed the bump untouched.** This is the argument for the slice ordering, and it holds:

```powershell
git diff master...HEAD -- SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs SemiPlot/SemiPlot.Tests/UI/Chart/ChartPointerInputTests.cs SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapPointerInputTests.cs
```

Empty. Seven tests written on Avalonia 11 pass on Avalonia 12 with no assertion edited, which is what
says the two stacks route a drag, a wheel and a capture loss the same way and still break a line on
`NaN`.

**Two regressions this slice found, and how to see each one.**

The wheel guard is the one that caught the first. `ScottPlot.Avalonia` 5.1.59 added
`AvaPlot.HandleMouseWheelEvent`, defaulted true, and its class handler sets `e.Handled` outside the
delta guard; Avalonia runs class handlers before instance handlers on the same element, so
`TrendChartView`'s subscription stopped firing and wheel zoom was dead in the running application.
Revert `9a276d0` alone and `WheelUpThenWheelDown_NarrowThenWidenTheNavigationWindow` fails with the
window width unchanged.

The second was invisible to every test. On Avalonia 11 `UseSkia()` bound a text shaper; on 12 it does
not, and `App.BuildAvaloniaApp` composes the platform explicitly rather than through
`UsePlatformDetect()`. The application threw `No text shaping system configured` before its window
existed — while all 368 tests passed, because `Avalonia.Headless`'s own `UseHeadless` wraps
`UseHarfBuzz`. Revert `90f3c63` alone and `AppBuilderCompositionTests` fails with
`Expected builder.TextShapingSubsystemInitializer not to be <null>`. That test did not exist when the
regression was found; the application bench found it, and the test now closes the gap.

**The application still reads a real archive.** Raise the bench from `docs/architecture/bench.md`,
launch the application, leave the window untouched, and ask the server:

```powershell
docker exec <container> psql -U postgres -d semiplot_bench -c "select idx_scan, idx_tup_fetch, seq_scan from pg_stat_user_tables where relname like 'tp2026%';"
```

48 index scans, 19 808 rows fetched, 0 sequential scans — measured identically on both stacks, which
is what says Avalonia 12 changed nothing about how the archive is read. The count is deterministic
only on an untouched window: scrolling requeries through the debouncer, and a sample taken during
interaction read 120 scans and 121 175 rows.

**What no check here covers.** Whether the chart *looks* right on Avalonia 12 — fonts, theme, cursor,
DPI — is not a machine question and waits for the demo stand. `docs/architecture/bench.md` records
which guard covers which half of the stack.
