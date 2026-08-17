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
and nothing else — no new project, no Npgsql, no error types. Bundling it with the provider scaffold
would put an unrelated UI refactor inside a slice whose whole value is being additive.

Behaviour does not change anywhere. The stub cannot fail, so no failure path is exercised yet; the
application starts the same way and draws the same chart.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice provider-pen-query-seam

**The member being replaced**

- `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:9` — `IReadOnlyList<Pen> Pens { get; }`. The two
  query methods at `:14-21` already return `Task<Result<...>>`; this one has no failure channel.

**Implementers — two**

- `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`

**Consumers**

- `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:41` — `public IReadOnlyList<Pen> Pens =>
  _dataProvider.Pens;`, a passthrough. Its only reader anywhere is
  `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs:28`; **no production code reads it**.
- `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:86` — `_dataProvider.Pens.Select(pen => pen.PenId)`
  inside `BuildRealtimeBatches()`, which the constructor calls at `:38`. This is why the change is
  more than a rename: an awaitable read cannot happen in a constructor.
- `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:26` — `public int PenCount =>
  _dataProvider.Pens.Count;`, bound in XAML at `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml:67`
  and asserted at `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs:46`. The view model is
  registered by plain constructor injection at
  `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs:18` and resolved from the container at
  `SemiPlot/SemiPlot.UI/App.axaml.cs:40` and `:91`.
- `SemiPlot/SemiPlot.UI/App.axaml.cs:86` — the loop that calls `chartViewModel.AddPen(pen)`.

**Six test files construct `TrendCoordinator` directly**, positionally:
`TrendCoordinatorTests.cs`, `TrendChartViewModelTests.cs:741`, `ChartAxisRegionEditTests.cs:125`,
`MinimapViewModelTests.cs:115`, `TrendLegendViewModelTests.cs:139`,
`TrendToolbarViewModelTests.cs:167`. A defaulted trailing parameter would not save them semantically:
an empty catalogue kills the realtime batches those tests exercise.

**The startup call is synchronous.** `SemiPlot/SemiPlot.UI/App.axaml.cs:53` is
`.AfterSetup(_ => InitializeServices(serviceProvider))`, and `AfterSetup` takes a synchronous
delegate. Nothing inside it can be awaited.

**What replaces `PenCount`'s source.** `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:108` already
exposes `public IReadOnlyCollection<TrendPenState> Pens => _pensById.Values;`, and
`App.axaml.cs:86-89` fills it. So the count the window shows is available without the provider at
all.

**Two architecture documents embed the shapes being changed**

- `docs/architecture/data-integration.md:35-49` reproduces the `IDataProvider` source verbatim,
  including the `Pens` property at `:37`.
- `docs/architecture/trend-interaction.md:51` spells out the `TrendCoordinator` constructor signature.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- **Every task compiles and every test passes before the next begins.** A seam change done in one
  step never compiles mid-way, so this plan uses the safe refactor path: add the new member alongside
  the old, move consumers one at a time, and delete the old member last. Each task is a working tree.
- Complete each task fully before moving to the next.
- Update this plan when scope changes during implementation.

## Testing Strategy

**This slice adds almost no tests, and that is correct.** It changes the shape of a seam without
changing behaviour, so the existing suite is the guard: `SemiPlot.Tests` must keep passing at the same
count throughout, because every test that exercised the catalogue through the old member must still
exercise it through the new one.

**One new test is warranted** — that the stub's `QueryPensAsync` returns a successful `Result`
carrying the same catalogue the property used to return. It pins the equivalence the whole refactor
rests on.

**No test asserts a failed catalogue read**, because no implementer can fail one yet. The stub and the
fake both succeed unconditionally. The first failure test belongs to
`postgres-catalog-and-extent`, which is the first slice with an implementation that can fail.

**Nothing is added to `SemiPlot.Tests.Data`.** This slice touches no data-source code.

## Acceptance Evidence

The change is a refactor, so the evidence is that behaviour is identical and the old member is gone.

1. **The old member no longer exists.**
   `grep -rn "\.Pens\b" --include=*.cs SemiPlot/ | grep -v "/obj/"` returns no hit against an
   `IDataProvider`, a `TrendCoordinator` or a `RandomStubDataProvider`. Hits against
   `TrendChartViewModel.Pens` and `RealtimeBatch.Pens` are unrelated and stay.

2. **The suite passes at the same count.**
   `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` reports its branch-point count plus exactly the one
   new test, zero failures. A count that fell means a test was deleted rather than migrated.

3. **The application still starts and still shows the pen count.**
   `dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` — the window opens, the chart draws
   the stub's pens, and the status line reads the same "Pens: N" it read before. This is the one
   check no test covers, because `PenCount` moves from one source to another and its XAML binding at
   `MainWindow.axaml:67` is not exercised headlessly.

4. **No data-source project is touched.**
   `git diff --name-only master...HEAD` lists nothing under `SemiPlot/SemiPlot.DataSource.Postgres/`
   or `SemiPlot/SemiPlot.Tests.Data/` — those belong to the slice after this one.

5. **The architecture documents match the code.**
   The `IDataProvider` block at `docs/architecture/data-integration.md:35-49` and the coordinator
   signature at `docs/architecture/trend-interaction.md:51` reproduce what the source now says.

## Progress Tracking

- Mark completed items `[x]` immediately when done.
- Add newly discovered tasks with `+`.
- Record blockers with a `BLOCKED` note and the reason.
- Keep this file in sync with the work actually done.

## Solution Overview

**Add first, delete last.** `QueryPensAsync` is added to the interface alongside `Pens`, both
implementers gain it, and consumers move one at a time. `Pens` is deleted in the final code task,
when nothing reads it. Every intermediate state compiles and every test gate is real, which a
one-shot interface change cannot offer.

**`TrendCoordinator.Pens` is deleted, not threaded.** It is a passthrough with zero production
consumers — one test reads it. The coordinator genuinely needs the pen *identifiers* to build its
realtime subscription; it does not need to re-expose the catalogue it was handed. Deleting the
property is smaller than plumbing it through the constructor and the DI factory, and it removes the
one thing that made the coordinator look like a catalogue source.

**`MainWindowViewModel` stops depending on `IDataProvider` entirely.** `PenCount` becomes
`ChartViewModel?.Pens.Count ?? 0`, reading the chart view model that
`SemiPlot/SemiPlot.UI/App.axaml.cs:86-89` already populates. This removes a dependency rather than
complicating one: the registration at `UiServiceCollectionExtensions.cs:18` stays a plain
`AddSingleton`, and both `GetRequiredService<MainWindowViewModel>()` sites keep working. The count is
what the chart holds, which is what the window is describing.

**The startup read blocks, and that is acceptable exactly once.** `AfterSetup` takes a synchronous
delegate, so the composition root calls `QueryPensAsync().GetAwaiter().GetResult()`. Against the stub
this completes immediately. Blocking on a real database at startup is a question for
`postgres-startup-and-composition`, which owns the operator-visible states and can restructure the
call; noting it here stops that slice rediscovering it.

**A failed read throws at startup, for now.** The stub cannot fail, so the branch is unreachable
today. Making it a visible operator state rather than a crash is the composition slice's job, and
inventing a half-answer here would be work that slice discards.

## Technical Details

**The interface after the change:**

```csharp
public interface IDataProvider
{
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

Three of four members now report failure the same way, and `Subscribe` remains the one that cannot —
a cold observable signals its own errors through the stream.

**`TrendCoordinator`'s constructor gains the pen list as its first parameter**, before the schedulers.
Six test files construct it positionally, so the position is stated here rather than left to the
implementer: putting it first groups the data argument with the provider it accompanies and makes the
compiler reject any call site that was not updated, instead of silently binding a scheduler to it.

**`MainWindowViewModel.PenCount` must notify.** It derives from `ChartViewModel`, which is a settable
property, so the setter raises a change notification for `PenCount` as well — otherwise the XAML
binding at `MainWindow.axaml:67` shows the value from before the chart was assigned, which is zero.

**`CompositionRootTests.cs:46` changes what it asserts.** It resolves `MainWindowViewModel` straight
from the container and asserts `PenCount > 0`. With the count sourced from `ChartViewModel`, a
freshly resolved view model legitimately reports zero. The test sets a chart view model with pens and
asserts the count follows it — which tests the wiring rather than the stub's pen count.

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

- [ ] add `Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()` to the interface, leaving `Pens` in
      place — nothing calls the new member yet and nothing breaks
- [ ] both implementers return their existing catalogue as a successful `Result`; neither can fail
- [ ] write a test that the stub's `QueryPensAsync` succeeds and carries exactly the same catalogue
      its `Pens` property returns — this pins the equivalence every later task relies on
- [ ] run tests — the whole suite must pass before Task 2

### Task 2: Move the coordinator off the property

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

- [ ] the constructor takes `IReadOnlyList<Pen>` as its **first** parameter and uses it in
      `BuildRealtimeBatches()` (`TrendCoordinator.cs:86`) instead of reading the provider
- [ ] delete the passthrough `Pens` property (`TrendCoordinator.cs:41`) and the single test that
      reads it (`TrendCoordinatorTests.cs:28`) — no production code reads either
- [ ] the coordinator factory registration in `UiServiceCollectionExtensions` carries the new
      parameter, and `App.axaml.cs` loads the catalogue with
      `QueryPensAsync().GetAwaiter().GetResult()` before constructing the coordinator, throwing on a
      failed `Result` for now
- [ ] update all six direct construction sites in the test project; each passes the same catalogue its
      fake provider holds, so the realtime batches those tests exercise are unchanged
- [ ] run tests — the whole suite must pass before Task 3

### Task 3: Move the window's pen count off the provider

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`

- [ ] `PenCount` becomes `ChartViewModel?.Pens.Count ?? 0`, and the `IDataProvider` constructor
      dependency is removed — the registration at `UiServiceCollectionExtensions.cs:18` stays a plain
      `AddSingleton` and both resolution sites keep working
- [ ] the `ChartViewModel` setter raises a change notification for `PenCount` too, or the XAML binding
      at `MainWindow.axaml:67` keeps showing the pre-assignment value
- [ ] `CompositionRootTests.cs:46` sets a chart view model carrying pens and asserts `PenCount`
      follows it, instead of asserting a resolved-but-unwired view model reports a positive count
- [ ] run tests — the whole suite must pass before Task 4

### Task 4: Delete the property

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`

- [ ] remove `Pens` from the interface and from both implementers; the compiler is the check that
      nothing still reads it
- [ ] migrate the remaining `RandomStubDataProviderTests` assertions that used `Pens` onto
      `QueryPensAsync`, keeping what each one asserted — uniqueness of identifiers, non-emptiness, and
      the identifiers the later tests reuse
- [ ] the equivalence test written in Task 1 is now redundant and is removed with the property it
      compared against
- [ ] run tests — the whole suite must pass before Task 5

### Task 5: Verify acceptance criteria

- [ ] every check in Acceptance Evidence produces its stated result
- [ ] `dotnet test SemiPlot.slnx` — zero failures, and `SemiPlot.Tests` reports its branch-point count
- [ ] `git diff --name-only master...HEAD` lists nothing under
      `SemiPlot/SemiPlot.DataSource.Postgres/` or `SemiPlot/SemiPlot.Tests.Data/`
- [ ] `dotnet format SemiPlot.slnx` reports no changes
- [ ] start the application and confirm the chart draws and the status line still reads the pen count

### Task 6: Update the architecture documents

- [ ] update the `IDataProvider` block at `docs/architecture/data-integration.md:35-49` so the
      reproduced source matches the interface
- [ ] update the `TrendCoordinator` constructor signature at
      `docs/architecture/trend-interaction.md:51`
- [ ] record in `data-integration.md` that a catalogue read can fail and that the failure travels as a
      `Result`, without naming error types — those arrive with `postgres-provider-scaffold`
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** Start the application and confirm the window opens, the chart draws the
stub's pens, and the status line reads the same pen count as before. `PenCount` changes source in this
slice and its XAML binding is not exercised by any headless test, so this is the one check that
covers it.

**What the next slices assume.** `postgres-provider-scaffold` implements the post-change interface and
does not start before this lands. `postgres-catalog-and-extent` is the first slice whose
`QueryPensAsync` can actually fail, and it owns the first test of a failed catalogue read — and the
unresolved question of whether an empty or missing `semiplot_tags` is a typed failure or an empty
success, on which the roadmap and `data-integration.md` currently disagree.

**Blocking at startup is inherited, not endorsed.** The composition root calls
`GetAwaiter().GetResult()` because `AfterSetup` is synchronous. Against the stub this is free.
`postgres-startup-and-composition` owns whether a real database read belongs there at all, and what
the operator sees when it fails.
