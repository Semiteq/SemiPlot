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

**Three tests appear:** `QueryPensAsync_ExposesCatalog` in Task 1, and two in a new
`MainWindowViewModelTests` in Task 3.

**`PenCount` is the one genuine behaviour change and needs its own tests.** There is no
`MainWindowViewModelTests` in the repository today; the property's only coverage is an assertion
inside `CompositionRootTests.cs:46`, which stops meaning anything once the count no longer comes from
the container. Task 3 adds a proper home for it.

**No test asserts a failed catalogue read**, because no implementer can fail one. The first belongs to
`postgres-catalog-and-extent`.

**The composition root's new throw on a failed `Result` is untested.** `App` has no test surface and
no implementer can produce the failure, so it is a new production branch with no guard — stated here
rather than left implicit.

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
   `dotnet test SemiPlot.slnx` — zero failures. Against the branch point, `SemiPlot.Tests` loses
   `Pens_ExposesTheProviderCatalog` and `Pens_ExposesCatalog`, and gains `QueryPensAsync_ExposesCatalog`
   plus two `MainWindowViewModelTests` — a net of plus one. Any other movement means a test was lost
   rather than migrated.

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

**The startup read blocks, and that is acceptable exactly once.** `AfterSetup` takes a synchronous
delegate, so the composition root calls `QueryPensAsync().GetAwaiter().GetResult()`. Against the stub
this completes immediately. Whether a real database read belongs at startup at all is
`postgres-startup-and-composition`'s question; noting it here stops that slice rediscovering it. The
alternative — letting the coordinator factory perform the read — was rejected because it would hide a
blocking call inside a DI factory, where nothing signals that resolving a service touches a database.

**A failed read throws at startup, for now.** The stub cannot fail, so the branch is unreachable
today. Making it a visible operator state rather than a crash is the composition slice's job.

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

**The registered factory type changes with it**, from `Func<IScheduler, TrendCoordinator>` to
`Func<IReadOnlyList<Pen>, IScheduler, TrendCoordinator>`, so the registration at
`UiServiceCollectionExtensions.cs:23` and the resolution at `App.axaml.cs:78` move together.

**`MainWindowViewModel.PenCount` raises change notification through the `ChartViewModel` setter.**
Not because the binding would otherwise read zero — `AfterSetup` runs before the window's
`DataContext` is assigned (`App.axaml.cs:34-38` proves the ordering by throwing if the second
`AfterSetup` callback has not run), so the value is already populated when the binding first reads it.
The notification is there because a derived property whose source can be reassigned must announce it,
and nothing guarantees that ordering stays true.

One hole it does not close: `TrendChartViewModel.Pens` is a live view over `_pensById`
(`TrendChartViewModel.cs:108`), so an `AddPen` after assignment moves the count with no notification.
Accepted for now — every pen is added at `App.axaml.cs:85-89` before the assignment at `:92`.

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
- [ ] write `QueryPensAsync_ExposesCatalog` asserting success, a non-empty catalogue and unique
      `PenId`s — the same assertions `Pens_ExposesCatalog` makes at
      `RandomStubDataProviderTests.cs:24-31`. Write it in its final form now so Task 4 only deletes
      the old test rather than churning this one
- [ ] run tests — the whole suite must pass before Task 2

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

- [ ] the constructor takes the catalogue as its second parameter, per the signature in Technical
      Details, guards it with `ArgumentNullException.ThrowIfNull`, and uses it in
      `BuildRealtimeBatches()` (`TrendCoordinator.cs:86`) instead of reading the provider
- [ ] delete the passthrough `Pens` property (`TrendCoordinator.cs:41`) and the single test that
      reads it, `Pens_ExposesTheProviderCatalog` (`TrendCoordinatorTests.cs:28`)
- [ ] the factory registration at `UiServiceCollectionExtensions.cs:23` becomes
      `Func<IReadOnlyList<Pen>, IScheduler, TrendCoordinator>`, and the resolution at
      `App.axaml.cs:78` changes to the same closed type
- [ ] `App.axaml.cs` loads the catalogue once with `QueryPensAsync().GetAwaiter().GetResult()`,
      throwing on a failed `Result`, and uses it for **both** the coordinator factory call and the
      `AddPen` loop at `:86`; the direct `IDataProvider` resolution at `:85` is deleted
- [ ] update all six direct construction sites in the test project, each passing the catalogue its
      fake provider already holds, so the realtime batches those tests exercise are unchanged
- [ ] `TrendCoordinatorTests.cs:58,60` keep reading `provider.Pens` — that is the fake's own property,
      not the interface member, and it stays
- [ ] run tests — the whole suite must pass before Task 3

### Task 3: Move the window's pen count off the provider

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/MainWindow/MainWindowViewModelTests.cs`

- [ ] `PenCount` becomes `ChartViewModel?.Pens.Count ?? 0`, and the `IDataProvider` constructor
      dependency is removed — the registration at `UiServiceCollectionExtensions.cs:18` stays a plain
      `AddSingleton` and both resolution sites keep working
- [ ] the `ChartViewModel` setter raises change notification for `PenCount` as well
- [ ] drop the `PenCount.Should().BeGreaterThan(0)` assertion from
      `CompositionRootTests.cs:46`. That test resolves the view model straight from the container and
      never assigns a chart, so after the change the count says nothing about the container; the
      surrounding resolution assertion stays
- [ ] write `MainWindowViewModelTests` with two facts: no chart assigned yields a count of zero, and
      assigning a chart carrying pens yields the matching count and raises
      `PropertyChanged(nameof(PenCount))`. Use `[AvaloniaFact]` — constructing a
      `TrendChartViewModel` needs the headless harness, as every other test that builds one does
- [ ] run tests — the whole suite must pass before Task 4

### Task 4: Remove the property from the interface

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`

- [ ] remove `Pens` from the interface and from `RandomStubDataProvider`, whose catalogue becomes a
      private field backing `QueryPensAsync` — it reads `_pensById`, not its own property, so nothing
      internal breaks
- [ ] **`FakeDataProvider.Pens` stays**, as a plain property that is no longer an interface member:
      it is used internally at `FakeDataProvider.cs:64` and read by `TrendCoordinatorTests` and the
      six construction sites
- [ ] delete `Pens_ExposesCatalog` (`RandomStubDataProviderTests.cs:24-31`), whose assertions Task 1
      already reproduced against `QueryPensAsync`
- [ ] migrate the remaining `RandomStubDataProviderTests` reads at `:91,171,172,190,211` onto
      `QueryPensAsync`, keeping what each asserted
- [ ] the private helper at `:258-261` is synchronous and called from both synchronous and
      asynchronous tests: resolve it as
      `QueryPensAsync().GetAwaiter().GetResult().Value[0].PenId` rather than making it async and
      changing its call sites
- [ ] run tests — the whole suite must pass before Task 5

### Task 5: Verify acceptance criteria

**Files:** none — verification only.

- [ ] every check in Acceptance Evidence produces its stated result
- [ ] `dotnet test SemiPlot.slnx` — zero failures, and the count moves by exactly the tests named in
      Testing Strategy
- [ ] `git diff --name-only master...HEAD` lists nothing under
      `SemiPlot/SemiPlot.DataSource.Postgres/` or `SemiPlot/SemiPlot.Tests.Data/`
- [ ] `dotnet format SemiPlot.slnx` reports no changes
- [ ] start the application and confirm the chart draws and the status line still reads the pen count

### Task 6: Update the architecture documents and the roadmap

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`

- [ ] update the `IDataProvider` block at `docs/architecture/data-integration.md:35-49` so the
      reproduced source matches the interface
- [ ] correct the whole coordinator signature at `docs/architecture/trend-interaction.md:51`: it
      names an `ILogger` parameter that does not exist and omits the trailing
      `TimeSpan? batchWindow = null`, both wrong before this slice as well as after
- [ ] record in `data-integration.md` that a catalogue read can fail and that the failure travels as a
      `Result`, without naming error types — those arrive with `postgres-provider-scaffold`
- [ ] stamp this slice in the roadmap: `Status`, `Plan`, `PR` and `Branch`, following the form used
      for `archive-populator`
- [ ] verify the roadmap is still inert:
      `bash .../skills/roadmap/scripts/check-inert.sh docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
- [ ] move this plan to `docs/plans/completed/`

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

**Blocking at startup is inherited, not endorsed.** The composition root calls
`GetAwaiter().GetResult()` because `AfterSetup` is synchronous. Against the stub this is free.
`postgres-startup-and-composition` owns whether a real database read belongs there at all, and what
the operator sees when it fails.
