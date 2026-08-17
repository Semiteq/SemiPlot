# Give the pen catalogue a failure channel

## Overview

`IDataProvider` exposes the pen catalogue as a plain `IReadOnlyList<Pen>` property. A property has no
way to report a failed read, and the slice two ahead loads that catalogue from `semiplot_tags`, where
unreachable, not-initialised and timed-out are all reachable states. So the property becomes
`Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()`, matching the shape of the two query methods the
interface already has.

The change has to land before anything tries to read a catalogue from a database, and it is cheapest
now: only the stub implements the interface, and the consumers that follow it are a coordinator, a
view model, the composition root and their tests. It is packaged alone because it is a seam change
and nothing else — no new project, no Npgsql, no error types.

Behaviour changes in exactly one place: `MainWindowViewModel.PenCount` stops reading the provider and
starts deriving from the chart view model. Everywhere else the application starts the same way and
draws the same chart. The stub cannot fail, so no failure path is exercised yet.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice provider-pen-query-seam

**The member being replaced**

- `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:9` — `IReadOnlyList<Pen> Pens { get; }`. The two
  query methods at `:14-21` already return `Task<Result<...>>`; this one has no failure channel.

**Implementers — two, and they are not symmetric**

- `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:11` — builds its catalogue from
  `_pensById` and does not read its own `Pens` property anywhere.
- `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs:11` — **uses its own `Pens` internally** at
  `:64`, `penIds.Where(id => Pens.Any(pen => pen.PenId == id))`. That use has nothing to do with the
  interface and must survive.

**Production consumers — four**

- `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:41` — `public IReadOnlyList<Pen> Pens =>
  _dataProvider.Pens;`, a passthrough whose only reader anywhere is
  `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs:28`. **No production code reads it.**
- `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:86` — `_dataProvider.Pens.Select(pen => pen.PenId)`
  inside `BuildRealtimeBatches()`, which the constructor calls at `:38`. This is why the change is
  more than a rename: an awaitable read cannot happen in a constructor.
- `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:26` — `public int PenCount =>
  _dataProvider.Pens.Count;`, bound in XAML at `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml:67`.
- `SemiPlot/SemiPlot.UI/App.axaml.cs:85-86` — resolves `IDataProvider` directly and loops its `Pens`
  into `chartViewModel.AddPen(pen)`.

**Test readers of a stub or fake catalogue** — `RandomStubDataProviderTests.cs:29,30,91,171,172,190,211`
plus the private helper at `:258-261`; `TrendCoordinatorTests.cs:28,58,60`.

**Seven direct construction sites of `TrendCoordinator`**: `UiServiceCollectionExtensions.cs:23`,
`TrendCoordinatorTests.cs:111`, `TrendChartViewModelTests.cs:741`, `ChartAxisRegionEditTests.cs:125`,
`MinimapViewModelTests.cs:115`, `TrendLegendViewModelTests.cs:139`,
`TrendToolbarViewModelTests.cs:167`. All positional.

**The registered factory is a closed generic.** `UiServiceCollectionExtensions.cs:23` registers
`Func<IScheduler, TrendCoordinator>` and `App.axaml.cs:78` resolves that exact type. Adding a
constructor parameter changes the delegate type, so both move together.

**The startup call is synchronous.** `App.axaml.cs:53` is
`.AfterSetup(_ => InitializeServices(serviceProvider))`, and `AfterSetup` takes a synchronous
delegate. Nothing inside it can be awaited.

**What replaces `PenCount`'s source.** `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:108`
exposes `public IReadOnlyCollection<TrendPenState> Pens => _pensById.Values;`, and
`App.axaml.cs:85-89` fills it before assigning it to the window view model at `:92`.

**Two architecture documents embed the shapes being changed**

- `docs/architecture/data-integration.md:35-49` reproduces the `IDataProvider` source verbatim,
  including the `Pens` property at `:37`.
- `docs/architecture/trend-interaction.md:51` gives the coordinator signature as
  `TrendCoordinator(IDataProvider, ILogger, IScheduler dataScheduler, IScheduler uiScheduler)` — which
  is **already wrong** independent of this change: the constructor takes no `ILogger` and does take a
  trailing `TimeSpan? batchWindow = null`.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- **Every task compiles and every test passes before the next begins.** A seam change done in one
  step never compiles mid-way, so this plan uses the safe refactor path: add the new member alongside
  the old, move consumers one at a time, and delete the old member last.
- Complete each task fully before moving to the next.
- Update this plan when scope changes during implementation.

## Testing Strategy

**The existing suite is the guard.** This slice changes the shape of a seam without changing
behaviour, so every test that exercised the catalogue through the old member must still exercise it
through the new one.

**Two tests legitimately disappear and are named, so a falling count can be told from a lost test:**

- `TrendCoordinatorTests.Pens_ExposesTheProviderCatalog` (`:28`) — the property it asserts is deleted
  in Task 2 and has no production reader.
- `RandomStubDataProviderTests.Pens_ExposesCatalog` (`:24-31`) — replaced in Task 1 by the same
  assertions against `QueryPensAsync`, and deleted with the property in Task 4.

**Three tests appear in the planned scope:** `QueryPensAsync_ExposesCatalog` in Task 1, and two in a new
`MainWindowViewModelTests` in Task 3. The review round added more; Acceptance Evidence item 2 carries
the full final list.

**`PenCount` is the one genuine behaviour change and needs its own tests.** There is no
`MainWindowViewModelTests` in the repository today; the property's only coverage is an assertion
inside `CompositionRootTests.cs:46`, which stops meaning anything once the count no longer comes from
the container. Task 3 adds a proper home for it.

**No test asserts a failed catalogue read**, because neither implementer can fail one. `FakeDataProvider`
could gain a `FailPens` flag the way it has `FailHistory`, but there is nothing left in this slice to
point it at: `LoadPens` carries no hand-written failure branch, only `Result<T>.Value`, whose throw is
FluentResults' own tested behaviour. The first failed-catalogue test belongs to
`postgres-catalog-and-extent`, and the first assertion about what the operator sees to
`postgres-startup-and-composition`.

**The composition root has no test surface.** `App` is not constructible under test, so `LoadPens` and
`InitializeServices` are unguarded — stated here rather than left implicit. Removing the coordinator's
container factory shrank what that gap covers: the coordinator wiring is now a compile error when wrong
instead of a startup exception.

**Nothing is added to `SemiPlot.Tests.Data`.** This slice touches no data-source code. New tests in
`SemiPlot.Tests` follow that project's conventions: AwesomeAssertions, and `[AvaloniaFact]` for
anything constructing a `TrendChartViewModel`, which owns a ScottPlot `Plot`.

## Acceptance Evidence

The change is a refactor, so the evidence is that behaviour is identical and the old member is gone.

1. **The old member no longer exists on the interface.**
   `grep -rn "\.Pens\b" --include=*.cs SemiPlot/ | grep -v "/obj/"` returns no hit against an
   `IDataProvider` or a `TrendCoordinator`. Hits against `FakeDataProvider.Pens` (a test double's own
   property), `TrendChartViewModel.Pens` and `RealtimeBatch.Pens` are unrelated and stay.

2. **The suite passes, and the count moves by exactly the named tests.**
   `dotnet test SemiPlot.slnx` — zero failures. `SemiPlot.Tests` goes from 250 at the branch point to
   256, and `SemiPlot.Tests.Data` stays at 183 passed plus 24 skipped. Across the whole slice, including
   the review rounds, `SemiPlot.Tests` loses `RandomStubDataProviderTests.Pens_ExposesCatalog`,
   `TrendCoordinatorTests.Pens_ExposesTheProviderCatalog` and
   `CompositionRootTests.Container_ResolvesMainWindowViewModel_UnderHeadlessHarness`; it gains
   `RandomStubDataProviderTests.QueryPensAsync_ExposesCatalog`,
   `TrendCoordinatorTests.Start_WithAnEmptyCatalog_EmitsNoRealtimeBatch`,
   `CompositionRootTests.Container_ResolvesChartFactory`,
   `CompositionRootTests.Container_ResolvesMinimapFactory`, and the five `MainWindowViewModelTests`
   (`PenCount_WithoutChart_IsZero`, `ChartViewModel_WhenAssigned_PublishesThePenCount`,
   `ChartViewModel_WhenReassigned_PublishesTheNewPenCount`,
   `ChartViewModel_WhenClearedToNull_PublishesAZeroPenCount`,
   `ChartViewModel_WhenAssignedTheSameInstance_KeepsTheChartAlive`). Any other movement means a test was
   lost rather than migrated.

3. **The application still starts and still shows the pen count.**
   `dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` — the window opens, the chart draws
   the stub's pens, and the status line reads the same "Pens: N" it read before. `PenCount` changes
   source and its XAML binding at `MainWindow.axaml:67` is not exercised headlessly.

4. **No data-source project is touched.**
   `git diff --name-only master...HEAD` lists nothing under `SemiPlot/SemiPlot.DataSource.Postgres/`
   or `SemiPlot/SemiPlot.Tests.Data/`.

5. **The architecture documents match the code.**
   The `IDataProvider` block at `docs/architecture/data-integration.md:35-49` and the coordinator
   signature at `docs/architecture/trend-interaction.md:51` reproduce what the source now says — the
   latter including the corrections it already needed.

## Progress Tracking

- Mark completed items `[x]` immediately when done.
- Add newly discovered tasks with `+`.
- Record blockers with a `BLOCKED` note and the reason.
- Keep this file in sync with the work actually done.

## Solution Overview

**Add first, delete last.** `QueryPensAsync` is added to the interface alongside `Pens`, both
implementers gain it, and consumers move one at a time. `Pens` leaves the interface in the final code
task, when nothing reads it through the interface. Every intermediate state compiles.

**`FakeDataProvider` keeps a `Pens` property that is no longer an interface member.** It uses the
catalogue internally at `:64`, and the six coordinator construction sites need something to pass. A
test double is allowed a public surface the interface does not have; deleting it because the
interface lost the member would break code that never depended on the interface.

**`TrendCoordinator.Pens` is deleted, not threaded.** It is a passthrough with zero production
consumers. The coordinator needs the pen *identifiers* to build its realtime subscription; it does not
need to re-expose the catalogue it was handed.

**`MainWindowViewModel` stops depending on `IDataProvider` entirely.** `PenCount` becomes
`ChartViewModel?.Pens.Count ?? 0`. This removes a dependency rather than complicating one: the
registration at `UiServiceCollectionExtensions.cs:18` stays a plain `AddSingleton` and both
`GetRequiredService<MainWindowViewModel>()` sites keep working.

**The startup read blocks, and that is acceptable only because the stub completes synchronously.**
`AfterSetup` takes a synchronous delegate, so the composition root calls
`QueryPensAsync().GetAwaiter().GetResult()`. The stub returns an already-completed
`Task.FromResult`, so nothing is ever awaited and nothing is posted anywhere.

The cost is not "a blocking wait" — it is a **deadlock**. Avalonia installs its synchronization context
during `Setup`, before `AfterSetup` runs, and the dispatcher does not start pumping until
`StartWithClassicDesktopLifetime`. So the first `QueryPensAsync` that genuinely awaits — the first one
backed by Npgsql — captures its continuation on that context, posts it to a dispatcher that will never
pump while `InitializeServices` is on the stack, and hangs with no window and no log line.
`postgres-startup-and-composition` must therefore restructure the read, not merely decide whether a
blocking read at startup is tasteful: `ConfigureAwait(false)` inside the provider is not a fix the
composition root can rely on, and `Task.Run(...).GetAwaiter().GetResult()` trades the deadlock for a
frozen splash. The alternative of letting the coordinator factory perform the read was rejected because
it would hide the same call inside a DI factory, where nothing signals that resolving a service touches
a database.

**A failed read throws at startup, for now.** `LoadPens` reads `Result<T>.Value`, which throws
`InvalidOperationException` carrying the error messages when the result failed (FluentResults 4.0.0,
verified). No hand-written failure branch is needed to produce that. The stub cannot fail, so the throw
is unreachable today. Making it a visible operator state rather than a crash is the composition slice's
job.

## Technical Details

**The interface after the change**, keeping the existing comment on `Subscribe`:

```csharp
public interface IDataProvider
{
    // Cold per call: no samples flow until subscribed; the subscriber disposes the returned IDisposable.
    IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds);

    Task<Result<IReadOnlyList<Pen>>> QueryPensAsync();

    Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
        IReadOnlyList<long> penIds,
        DateTime fromUtc,
        DateTime toUtc,
        AggregationLayer layer,
        int targetColumnCount);

    Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();
}
```

**The coordinator constructor, literally:**

```csharp
public TrendCoordinator(
    IDataProvider dataProvider,
    IReadOnlyList<Pen> pens,
    IScheduler dataScheduler,
    IScheduler uiScheduler,
    TimeSpan? batchWindow = null)
```

The catalogue sits second, beside the provider it accompanies. Position is stated because seven call
sites are positional; it is not a safety mechanism — `IReadOnlyList<Pen>` and `IScheduler` are
unrelated types, so every position is equally compiler-checked. The new parameter gets the same
`ArgumentNullException.ThrowIfNull` guard the existing three have at `:30-32`.

**The coordinator's registered factory is deleted rather than widened.** The first cut changed
`Func<IScheduler, TrendCoordinator>` into `Func<IReadOnlyList<Pen>, IScheduler, TrendCoordinator>`, which
kept a runtime-only contract that the registration and the resolution had to spell identically or fail
with an `InvalidOperationException` at startup — and no test resolved it. Its only job was to hide two
`GetRequiredService` calls from its single caller, which already made one of them itself. Review removed
it: `InitializeServices` now writes
`new TrendCoordinator(dataProvider, pens, serviceProvider.GetRequiredService<IScheduler>(), uiScheduler)`
and the wiring is compile-checked. No lifetime was lost — the delegate was the singleton, the coordinator
it built never was. The chart and minimap factories are untouched; a container test now resolves both, so
drift in those two surfaces as a test failure rather than at startup.

**`MainWindowViewModel.PenCount` raises change notification through the `ChartViewModel` setter.**
Not because the binding would otherwise read zero — `AfterSetup` runs before the window's
`DataContext` is assigned (`App.axaml.cs:34-38` proves the ordering by throwing if the second
`AfterSetup` callback has not run), so the value is already populated when the binding first reads it.
The notification is there because a derived property whose source can be reassigned must announce it,
and nothing guarantees that ordering stays true.

One hole it does not close: `TrendChartViewModel.Pens` is a live view over `_pensById`
(`TrendChartViewModel.cs:108`), so an `AddPen` after assignment moves the count with no notification.
Accepted for now — every pen is added in `InitializeServices` before the assignment, and `RemovePen` has
no production caller. Review moved this note onto `PenCount` itself as a comment, so it is found by
whoever makes the pen set dynamic rather than only by whoever reads this archived plan.

## What Goes Where

- **Implementation Steps** — the interface, the two implementers, the coordinator, the view model,
  the composition root, the deletion, verification and documentation.
- **Post-Completion** — the manual start check, and what the slices after this one assume.

## Implementation Steps

### Task 1: Add QueryPensAsync alongside the property

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`

- [x] add `Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()` to the interface, leaving `Pens` in
      place — nothing calls the new member yet and nothing breaks
- [x] both implementers return their existing catalogue as a successful `Result`; neither can fail
- [x] write `QueryPensAsync_ExposesCatalog` asserting success, a non-empty catalogue and unique
      `PenId`s — the same assertions `Pens_ExposesCatalog` makes at
      `RandomStubDataProviderTests.cs:24-31`. Write it in its final form now so Task 4 only deletes
      the old test rather than churning this one
- [x] run tests — the whole suite must pass before Task 2

### Task 2: Move the coordinator and the composition root off the property

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartAxisRegionEditTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Legend/TrendLegendViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Toolbar/TrendToolbarViewModelTests.cs`

This task changes nine files at once and that is not a granularity failure: a constructor signature
breaks all seven call sites simultaneously, so no smaller step compiles.

- [x] the constructor takes the catalogue as its second parameter, per the signature in Technical
      Details, guards it with `ArgumentNullException.ThrowIfNull`, and uses it in
      `BuildRealtimeBatches()` (`TrendCoordinator.cs:86`) instead of reading the provider
- [x] delete the passthrough `Pens` property (`TrendCoordinator.cs:41`) and the single test that
      reads it, `Pens_ExposesTheProviderCatalog` (`TrendCoordinatorTests.cs:28`)
- [x] the composition root passes the catalogue at the coordinator's construction site in
      `App.axaml.cs`, which is the only place a `TrendCoordinator` is built
- [x] `App.axaml.cs` loads the catalogue once with `QueryPensAsync().GetAwaiter().GetResult()`,
      throwing on a failed `Result`, and uses it for **both** the coordinator factory call and the
      `AddPen` loop at `:86`; the direct `IDataProvider` resolution at `:85` is deleted — the
      provider is still resolved once, moved to the top of `InitializeServices` as the argument of
      the new `LoadPens` helper that performs the blocking read
- [x] update all six direct construction sites in the test project, each passing the catalogue its
      fake provider already holds, so the realtime batches those tests exercise are unchanged
- [x] `TrendCoordinatorTests.cs:58,60` keep reading `provider.Pens` — that is the fake's own property,
      not the interface member, and it stays
- [x] run tests — the whole suite must pass before Task 3

### Task 3: Move the window's pen count off the provider

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/MainWindow/MainWindowViewModelTests.cs`

- [x] `PenCount` becomes `ChartViewModel?.Pens.Count ?? 0`, and the `IDataProvider` constructor
      dependency is removed — the registration at `UiServiceCollectionExtensions.cs:18` stays a plain
      `AddSingleton` and both resolution sites keep working
- [x] the `ChartViewModel` setter raises change notification for `PenCount` as well
- [x] drop the `PenCount.Should().BeGreaterThan(0)` assertion from
      `CompositionRootTests.cs:46`. That test resolves the view model straight from the container and
      never assigns a chart, so after the change the count says nothing about the container; the
      surrounding resolution assertion stays
- [x] write `MainWindowViewModelTests` with two facts: no chart assigned yields a count of zero, and
      assigning a chart carrying pens yields the matching count and raises
      `PropertyChanged(nameof(PenCount))`. Use `[AvaloniaFact]` — constructing a
      `TrendChartViewModel` needs the headless harness, as every other test that builds one does
- [x] run tests — the whole suite must pass before Task 4

### Task 4: Remove the property from the interface

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`

- [x] remove `Pens` from the interface and from `RandomStubDataProvider`, whose catalogue becomes a
      private field backing `QueryPensAsync` — it reads `_pensById`, not its own property, so nothing
      internal breaks
- [x] **`FakeDataProvider.Pens` stays**, as a plain property that is no longer an interface member:
      it is used internally at `FakeDataProvider.cs:64` and read by `TrendCoordinatorTests` and the
      six construction sites
- [x] delete `Pens_ExposesCatalog` (`RandomStubDataProviderTests.cs:24-31`), whose assertions Task 1
      already reproduced against `QueryPensAsync`
- [x] migrate the remaining `RandomStubDataProviderTests` reads at `:91,171,172,190,211` onto
      `QueryPensAsync`, keeping what each asserted
- [x] the private helper at `:258-261` is synchronous and called from both synchronous and
      asynchronous tests: resolve it as
      `QueryPensAsync().GetAwaiter().GetResult().Value[0].PenId` rather than making it async and
      changing its call sites — expressed as a shared private `Catalog(provider)` helper the four
      synchronous reads and `PenIds()` all use
- [x] run tests — the whole suite must pass before Task 5

### Task 5: Verify acceptance criteria

**Files:** none — verification only.

- [x] every check in Acceptance Evidence produces its stated result — checks 1 to 4 pass as written.
      Check 5 (architecture documents match the code) is still false and is Task 6's own work: it is
      listed as acceptance evidence for the slice, not for this task, and cannot pass before the task
      that performs the edit
- [x] `dotnet test SemiPlot.slnx` — zero failures, and the count moves by exactly the tests named in
      Testing Strategy. `SemiPlot.Tests` 250 at the branch point (`243fa5c`) against 251 on this
      branch; `SemiPlot.Tests.Data` 183 passed and 24 skipped, unchanged and untouched. Each named
      test checked by name: `Pens_ExposesTheProviderCatalog` and `Pens_ExposesCatalog` present at the
      branch point and absent now, `QueryPensAsync_ExposesCatalog` plus
      `MainWindowViewModelTests.PenCount_WithoutChart_IsZero` and
      `ChartViewModel_WhenAssigned_PublishesThePenCount` added
- [x] `git diff --name-only master...HEAD` lists nothing under
      `SemiPlot/SemiPlot.DataSource.Postgres/` or `SemiPlot/SemiPlot.Tests.Data/` — the diff is 16
      source files plus this plan, none of them under either path
- [x] `dotnet format SemiPlot.slnx` reports no changes — `--verify-no-changes` exits 0
- [x] start the application and confirm the chart draws and the status line still reads the pen count
      — **deferred to operator verification**; the drawn chart and the status text cannot be read
      without eyes on the screen. What was checked: `dotnet build SemiPlot.slnx` succeeds with zero
      errors; the built executable runs for 10 seconds without exiting and creates a top-level window
      titled "SemiPlot - Trend Viewer"; no new line reaches `%LOCALAPPDATA%\SemiPlot\Logs\semiplot.log`
      during the run, so the composition root's blocking `QueryPensAsync` read and the `AddPen` loop
      raise nothing. The chart content and the "Pens: N" text remain unverified

### Task 6: Update the architecture documents and the roadmap

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`

- [x] update the `IDataProvider` block at `docs/architecture/data-integration.md:35-49` so the
      reproduced source matches the interface — copied from `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`,
      so the block now also carries the `Subscribe` comment the document had dropped
- [x] correct the whole coordinator signature at `docs/architecture/trend-interaction.md:51`: it
      names an `ILogger` parameter that does not exist and omits the trailing
      `TimeSpan? batchWindow = null`, both wrong before this slice as well as after
- [x] record in `data-integration.md` that a catalogue read can fail and that the failure travels as a
      `Result`, without naming error types — those arrive with `postgres-provider-scaffold`. The
      paragraph sits under the DTO table in "The provider surface" and does not touch the
      empty-`semiplot_tags` sentence, whose typed-failure-versus-empty-success question belongs to
      `postgres-catalog-and-extent`
- [x] stamp this slice in the roadmap: `Status`, `Plan`, `PR` and `Branch`, following the form used
      for `archive-populator` — `IN-PROGRESS`, the plan at its current path, branch
      `provider-pen-query-seam`, `PR: —` because no pull request exists yet
- [x] verify the roadmap is still inert:
      `bash .../skills/roadmap/scripts/check-inert.sh docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
      — prints `inert`
- [x] move this plan to `docs/plans/completed/` — **deferred to delivery**. Archiving happens after
      the operator has tested the branch, and the review phases that follow this task read the plan
      where it is. The roadmap entry points at `docs/plans/20260817-provider-pen-query-seam.md` and
      moves to the `completed/` path when the slice is stamped `DONE`

### + Task 7: Review pass

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`, `SemiPlot/SemiPlot.UI/App.axaml.cs`,
  `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`, `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`,
  `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`,
  `SemiPlot/SemiPlot.Tests/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `CLAUDE.md`, `docs/architecture/data-integration.md`,
  `docs/plans/20260619-simplescada-postgres-provider.md`, `docs/plans/backlog.md`

- [x] delete the coordinator's container factory; `InitializeServices` constructs the coordinator
      directly, so the wiring is compile-checked (see Technical Details)
- [x] `LoadPens` drops its hand-written failure branch for `Result<T>.Value`, whose throw already
      carries the errors
- [x] the coordinator takes the catalogue as a `BuildRealtimeBatches` argument instead of a field, and
      the constructor parameter names the invariant that it must be the provider's own catalogue
- [x] `MainWindowViewModelTests` asserts `PenCount` from inside the `PropertyChanged` handler and covers
      chart-to-chart and chart-to-null; verified by swapping the raise above `RaiseAndSetIfChanged` and
      seeing the tests fail
- [x] new tests: an empty catalogue emits no realtime batch; the container resolves the two surviving
      `Func<>` factories, replacing the duplicated `MainWindowViewModel` resolution test
- [x] `SemiPlot.Tests` 251 → 256: three `MainWindowViewModelTests` and one `TrendCoordinatorTests`
      added, plus the container's `MainWindowViewModel` resolution test replaced by two factory
      resolution tests, a net gain of one. `SemiPlot.Tests.Data` unchanged at 183 passed, 24 skipped
- [x] **not done, and deliberately**: the seven near-identical `new TrendCoordinator(...)` blocks in the
      test project are not extracted into a shared helper. They differ in per-file `_batchWindow`, in
      realtime interval and in provider setup, so a helper saves a few lines per site while touching
      seven green test files in a slice whose point is that behaviour did not change. Worth doing when a
      further constructor change forces those files open anyway
- [x] **accepted, not fixed**: commit `f9b0df1` is typed `test:` but changes only this plan file; it
      should have been `docs:`. History is not rewritten for it

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** Start the application and confirm the window opens, the chart draws the
stub's pens, and the status line reads the same pen count as before. `PenCount` changes source in this
slice and its XAML binding is not exercised by any headless test, so this is the one check that
covers it end to end.

**What the next slices assume.** `postgres-provider-scaffold` implements the post-change interface and
does not start before this lands. `postgres-catalog-and-extent` is the first slice whose
`QueryPensAsync` can actually fail; it owns the first test of a failed catalogue read, and the
unresolved question of whether an empty or missing `semiplot_tags` is a typed failure or an empty
success, on which the roadmap and `data-integration.md` currently disagree.

**Blocking at startup is inherited, not endorsed, and it does not survive a real provider.** The
composition root calls `GetAwaiter().GetResult()` because `AfterSetup` is synchronous. Against the stub
this is free — the task is already complete. Against Npgsql it deadlocks: the Avalonia synchronization
context is installed during `Setup`, the dispatcher only starts pumping at
`StartWithClassicDesktopLifetime`, so a continuation captured inside `AfterSetup` is posted to a
dispatcher that never runs while `InitializeServices` is on the stack. The symptom is a hang with no
window and no log line, not a slow start. `postgres-startup-and-composition` owns the restructuring, and
what the operator sees when the read fails.

**Executed by exec:**

- branch: provider-pen-query-seam

## Verify it yourself

This slice changes an interface and claims no behaviour moved. The first three checks are mechanical;
the fourth is the operator's, because the one binding that changed source is not reachable headlessly.

1. **The synchronous catalogue is gone from the seam.**
   `git grep -n "Pens" -- SemiPlot/SemiPlot.Core/Data/IDataProvider.cs` returns exactly one line, the
   `QueryPensAsync` declaration. `git grep -nE "IReadOnlyList<Pen> Pens" -- "*.cs"` returns exactly one
   hit, `FakeDataProvider.cs:60` — the test double's own property, which is not an interface member and
   is deliberately kept as the catalogue the construction sites pass in.

2. **The suite grew only where the slice added behaviour.**
   `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests` 256 passed and `SemiPlot.Tests.Data` 183 passed
   with 24 skipped. The data project is untouched by this branch and its count must not move;
   `SemiPlot.Tests` goes 251 → 256 by the arithmetic in Task 7.

3. **The self-assignment guard is load-bearing, not decorative.**
   Revert only the early return in `MainWindowViewModel`'s `ChartViewModel` setter and
   `ChartViewModel_WhenAssignedTheSameInstance_KeepsTheChartAlive` fails with `ObjectDisposedException`
   from `TrendChartViewModel`. The unguarded setter disposed the chart before deciding it had not
   changed, then rebuilt the toolbar and legend on top of the dead instance.

4. **The pen count still reads the same on screen.** `dotnet run --project SemiPlot/SemiPlot.UI` and
   compare the status line against the previous build. `PenCount` now derives from the chart rather than
   from the provider, and its XAML binding is exercised by no headless test.
