# Wire the application to the real archive

## Overview

The composition root resolves the synthetic stub. `Program.BuildServiceProvider`
(`SemiPlot/SemiPlot.UI/Program.cs:37-47`) calls `AddData()` from `SemiPlot.DataSource.Stub`, and
nothing in the running application has ever touched PostgreSQL. Every layer above the provider seam —
the ladder, the decimator, the cursor, the minimap — has been validated only against evenly spaced
synthetic samples, which is the roadmap's stated central risk.

This slice points the application at the real archive and makes every failure visible. It also takes
on two pieces of work that become reachable only here: seeding the chart window from the archive
extent, and dropping a pen the provider omits.

The startup path cannot survive a real provider as written. `App.LoadPens`
(`SemiPlot/SemiPlot.UI/App.axaml.cs:112-115`) reads `.GetAwaiter().GetResult().Value` with no failure
check, inside the synchronous `AfterSetup` callback `InitializeServices` (`:76-109`) runs from —
against the stub, which cannot fail; against a server that is not answering, an unhandled exception
during Avalonia setup.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-wire-up

Files involved:

- `SemiPlot/SemiPlot.UI/Program.cs` — `Main()` takes no arguments (`:17`), builds the container with
  `.AddData().AddUi()` (`:40-42`), resolves the log path from `LocalApplicationData` (`:49-54`)
- `SemiPlot/SemiPlot.UI/App.axaml.cs` — `Run` at `:49`, `InitializeServices` at `:76-109`, `LoadPens`
  at `:112-115`, `EnsureSingleStart` at `:117-126`
- `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs:18-19` — records why the UI scheduler is not
  a registration: it exists only after `UseReactiveUI()` has run
- `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:33` —
  `Load(string filePath)` returning `Result<PostgresConnectionSettings>`; `:156` constructs
  `ConnectionFileVersionMismatchError`
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:104` — constructs
  `ArchiveDatabaseMissingError` from SQLSTATE `3D000`, where no table exists
- `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs` — `Table` is non-nullable and is
  interpolated into the base message; its XML doc routes the remedy on exactly two values
- `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs` — the constructor at `:25` opens on
  `now - 1h .. now`; `TrackDataExtents` at `:66-86` snaps the window on its first call only, gated by
  `_hasData`; the controller exposes `From`, `To`, `IsSticky`, `ActiveLayer`, `TargetColumnCount` and
  no first-sample accessor — `FirstSample` lives on `TrendNavigationModel`
  (`SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs:34`) behind a private field
- `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs:41-42` — projects
  `(new TrendHistory(request.Layer, result.Value), request.Sequence)`, discarding `request.PenIds`
- `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:502` — `ApplyHistory(TrendHistory, long)`
- `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs` — exposes `LoadHistory`, `AppendRealtime`,
  `FoldRealtime`; nothing clears a loaded curve
- `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs` — `QueryPensAsync` (`:78-81`) and
  `QueryArchiveExtentAsync` (`:123-126`) always succeed; only `FailHistory` exists
- `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs` — the existing composition tests
- `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml:67` — binds `PenCount`, which renders an empty
  catalogue as `Pens: 0`

Patterns taken from `C:/Users/admin/projects/SemiStep`, a sibling application by the same author with
the same error-plane design:

- `SemiStep/SemiStep.UI/StartupOptions.cs` — a `record` parsed from `args` with `--config-dir`,
  `--log-file` and `--logging-level`, defaults under `C:\DISTR\`. The site convention is `C:\DISTR\`,
  not a path beside the executable and not a per-user profile directory.
- `SemiStep/SemiStep.UI/Program.cs:30-56` — `Parse(args)`, create the logger, `ValidateStartup(options)`,
  then a single branch to the error window or to `App.Run`. Its `ValidateStartup` builds only the DI
  container; it constructs no Avalonia or ReactiveUI object before launch. That ordering is what makes
  the branch possible, and this plan copies it.
- `SemiStep/SemiStep.UI/Program.cs:47-48` — a defect already paid for once: **the failure branch must
  not re-launch**, because once `App.Run` has initialised Avalonia a second `BuildAvaloniaApp()`
  throws "Application has already been initialized".
- Its error window is hardcoded English and culture-independent, because it runs before configuration
  is loaded.

What is not taken from SemiStep: its error reporting folds `IError` into a message panel with
run-time localisation. This roadmap maps each public type to a distinct state instead.

ASSUMPTION: the configuration directory is `C:\DISTR\Config\SemiPlot` and the connection file is
`archive-connection.yaml` directly inside it. SemiStep's equivalent carries a per-installation leaf
(`MBE`, `MOCVD`, `RIE`) and nests the file one level deeper, in `connection\connection.yaml`.
SemiPlot has no per-installation equivalent, and `archive-connection.yaml` is the name its own
fixtures already use (`SemiPlot.Tests.Data/Postgres/PostgresConnectionLoaderTests.cs:72`).
`--config-dir` makes the directory correctable without a code change; it does not make the file name
or a nested subdirectory correctable, so both are settled here and both are open to the operator.

## Development Approach

- **testing approach**: Regular (code first, then tests), matching the repository's merged slices.
- complete each task fully before moving to the next
- **CRITICAL: every task MUST include new/updated tests**, and all tests must pass before the next
  task starts
- **CRITICAL: update this plan file when scope changes during implementation**
- UI tests live in `SemiPlot.Tests` (`net10.0-windows`, xunit v2, AwesomeAssertions,
  `[AvaloniaFact]` for anything touching Avalonia or ReactiveUI). Provider and Core tests live in
  `SemiPlot.Tests.Data` (xunit v3, raw `Assert.`). Do not move a test between them.
- **compatibility**: `IDataProvider` gains nothing — no member, no `CancellationToken`. The roadmap
  defers tokens to the slice that owns splitting a server cancel from a caller's, and this is not it.
  The two error-type merges remove two public types from `SemiPlot.Core`; every construction site,
  test and document reference moves with them.

## Testing Strategy

- **unit tests**: required per task, in the project that owns the code under test.
- **no new gated tests.** Everything this slice can automate runs without a database, driven by
  `FakeDataProvider` or by resolving the container graph. The existing 43 gated tests must keep
  passing.
- **manual protocol**: the checks no automated test in this slice can reach are a numbered protocol
  under Acceptance Evidence.
- **no headless end-to-end suite.** It needs Avalonia and a container at once and no CI runner
  provides both; it belongs to `postgres-live-edge-and-demo`, after `avalonia-12-bump`.

## Acceptance Evidence

**Evidence 1 — the composed graph resolves the real provider.** A test builds the container exactly
as `Program` builds it, over `AddPostgresData` plus `AddUi`, and asserts `IDataProvider` resolves to
`PostgresDataProvider`; a second asserts `--use-stub` resolves the stub:

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~CompositionRoot"
```

**Evidence 2 — no public error type can reach the operator unmapped.** A coverage test enumerates
the public types assignable to `IError` in `SemiPlot.Core.Data.Errors` and in `SemiPlot.UI.Startup` by
reflection, asserts each one maps to a state, and pins the enumeration at eight — Core's seven plus
the UI-local `StartupReadTimedOutError` — so it cannot pass over an empty set:

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StartupFailureMapper"
```

The compiler cannot do this job. `CS8509` fires on any switch expression it cannot prove exhaustive,
and over an interface it can never prove it, so a switch handling every type without a catch-all warns
anyway and `WarningsAsErrors` would stop the build outright. The test is the gate, not a fallback for
one. Prove it works by deleting one arm and confirming the test fails; restore it.

**Evidence 3 — a failed startup read does not crash.** A test drives the probe with a provider whose
`QueryPensAsync` returns a failed `Result` and asserts a failed `Result` out, not a thrown exception.
This fails before the change: `App.LoadPens` reads `.Value` on a failed result.

**Evidence 4 — the window opens on the archive, not on the wall clock.** A test seeds navigation from
an extent lying wholly in the past and asserts the window covers that extent and that panning left
reaches its first sample. This fails before the change, where panning clamps at startup minus one
hour.

**Evidence 5 — a pen the provider omits leaves the chart.** A view-model test applies a history
result that omits one requested pen and asserts that pen's curve is gone rather than showing the
previous window's envelope.

**Evidence 6 — nothing regressed.** `dotnet test SemiPlot.slnx` reports zero failures. Measured at
`20e3c16`, the branch point: `SemiPlot.Tests` 290 passed / 0 skipped, `SemiPlot.Tests.Data` 393
passed / 0 skipped, with Docker running and `semibase` on `PATH`.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**Manual protocol** — against a bench seeded by `SemiPlot.Tools.ArchiveSeeder`, in order:

1. With a valid connection file, the application opens and the pen list matches `semiplot_tags`.
2. The chart draws history, and the visible span is inside the seeded archive rather than at the
   current wall clock.
3. Zooming out past a rung boundary switches layer; the column count falls and the curve keeps its
   shape.
4. Point the connection file at a stopped server: an error window names the host and port, and no
   empty chart is drawn.
5. Point it at a database with no `semiplot_tags`: the error window says provisioning is unfinished
   and names that table, not `trends`.
6. Give a wrong password: the error window says access was denied.
7. Empty the catalogue: the application opens normally, draws no pens, and reports an empty
   catalogue — a state, not an error window.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues or blockers with ⚠️ prefix
- update the plan if implementation deviates from the stated scope

## Solution Overview

**The startup decision splits at the Avalonia boundary, because the schedulers do.** The UI scheduler
exists only after `UseReactiveUI()` has run inside `AfterSetup`, which is why the view-model factories
take it as a parameter rather than resolving it. So the work divides in two:

- `StartupProbe` runs in `Program`, **before** Avalonia. It branches on `--use-stub` first, loads the
  connection file on the archive path only, builds the container, reads the pen catalogue and the
  archive extent, and returns `Result<StartupData>`. It touches no Avalonia and no ReactiveUI type, so
  it is testable outright.
- `App.InitializeServices` stays inside `AfterSetup` and consumes that data. It builds the
  coordinator, the chart and the minimap, seeds navigation from the extent, and awaits nothing.

`Program` branches once on the probe's result — the error window or `App.Run` — and never both, because
re-entering `BuildAvaloniaApp` after Avalonia is initialised throws. `App.RunErrorWindow` goes through
the same `EnsureSingleStart` guard `App.Run` uses.

**The startup reads are bounded by the caller, not by a token.** `IDataProvider` takes no
`CancellationToken` and does not gain one here. The probe wraps each read in `Task.WaitAsync(TimeSpan)`
with a short bound, so a server that accepts TCP and answers nothing shows an error instead of holding
startup for the provider's five-minute backstop. Stated plainly: this abandons the wait, not the
query — the read keeps running on its pooled connection until the backstop, and the process exits or
proceeds without it.

**Two error types merge, narrowing the vocabulary before it is mapped.**
`ConnectionFileVersionMismatchError` becomes a `ConnectionFileProblem.VersionMismatch` value on
`ConnectionFileInvalidError` — the file format has had one version ever and both send the operator to
the same file. `ArchiveDatabaseMissingError` folds into `ArchiveNotInitialisedError`, which needs a
real change to receive it: `3D000` carries no table, while `Table` is non-nullable today and is
interpolated into the base message. The surviving type gains an `ArchiveObject` discriminator with
`Database` and `Table` values, makes `Table` nullable, and branches its message on the discriminator.
Its XML doc's two-value remedy table gains the third case.

**The mapping is total by test, not by compiler.** A reflection coverage test over
`SemiPlot.Core.Data.Errors` and `SemiPlot.UI.Startup` fails when a public type in either namespace has
no mapping, and a second test pins the enumeration at eight so it cannot pass vacuously. This is the
roadmap's corrected guard, and it is the form SemiStep already runs.

**The window is seeded from the extent.** `TrackDataExtents` snaps the window on its first call only,
gated by `_hasData`, and is reached only from an envelope that has rows. An archive whose last sample
is older than the opening window therefore never snaps, and panning clamps to a point after the data —
the minimap shows an extent the chart cannot reach. Seeding goes through the same `TrackDataExtents`
path and sets `_hasData`, so the first history envelope does not re-snap and undo it. An empty extent
seeds nothing and leaves the window on the wall clock, which is the only sensible view of an empty
archive.

**A pen the provider omits is dropped.** The requested identifiers do not currently reach
`ApplyHistory`: `ChartHistoryRequestDebouncer` discards `request.PenIds` at its projection. They are
threaded through so the view model can distinguish "asked for and not returned" from "not asked for" —
falling back to the pen dictionary instead would drop a pen added between request and apply.
`TrendPenState` gains a way to clear its curve.

**The stub stays selectable, never as a fallback.** `AddPostgresData` is the default; `--use-stub`
selects the stub for development. An unreachable database is an error state, because substituting
synthetic data silently would let an operator read invented numbers as process data. The stub project
is deleted by the closing slice.

## Technical Details

**`StartupOptions`** — a `sealed record` in `SemiPlot.UI` with `ConfigDir`, `LogFilePath`,
`LoggingLevel` and `UseStub`, and a static `Parse(string[] args)`. `--config-dir`, `--log-file` and
`--logging-level` each consume the following element when one exists; `--use-stub` is a valueless
switch. Unknown arguments are ignored. Defaults: `C:\DISTR\Config\SemiPlot`,
`C:\DISTR\Logs\SemiPlot\semiplot.log`, `Warning`, and the stub off.

**`StartupProbe`** — takes the options; returns `Result<StartupData>` carrying the settings, the built
`ServiceProvider`, the pen list and the archive extent. `--use-stub` is checked first and reads no
connection file at all, so the flag works on a machine holding none; `StartupData.Settings` is null on
that path. Otherwise the sequence is: load `<ConfigDir>/archive-connection.yaml`, build the container
over `AddPostgresData(settings)`, resolve `IDataProvider`, read the catalogue, read the extent. The two
containers are built by two named methods, `BuildArchiveServiceProvider` and
`BuildStubServiceProvider`, so no lost-settings bug can silently produce the stub. Any failed `Result`
short-circuits and carries its error out.

**`StartupData`** earns a file because it carries four values across the Avalonia boundary and is what
`App.Run` is handed.

**`StartupFailureMapper`** — maps `IError` to a `StartupFailureView` record of title, detail and
remedy, all English, with a catch-all arm producing a generic entry. The coverage test, not the arm,
is what forbids a missing mapping.

**`ErrorWindow`** — a minimal Avalonia window bound to `StartupFailureView`, launched by
`App.RunErrorWindow`. It resolves no services and reads no configuration.

**The minimap keeps its own extent read.** `MinimapViewModel.LoadExtentAsync` already calls
`QueryArchiveExtentAsync`; the probe's read is a second one per launch. Passing the probe's extent
into the minimap would couple its lifetime to startup for one saved query on a cold path, so both stay.

## Implementation Steps

### Task 1: Parse startup options

**Files:**
- Create: `SemiPlot/SemiPlot.UI/StartupOptions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/StartupOptionsTests.cs`

- [x] create `StartupOptions` and `Parse` as described in Technical Details
- [x] change `Program.Main` to take `string[] args`, parse the options, and pass the log path and
      level into `CreateLogger` instead of the current `LocalApplicationData` resolution
- [x] write tests for each valued argument, for `--use-stub` as a valueless switch, for a valued
      argument given last with no value, for an unknown argument, and for the defaults on empty args
- [x] run tests — must pass before task 2

### Task 2: Merge the two error types

**Files:**
- Delete: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileVersionMismatchError.cs`
- Delete: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveDatabaseMissingError.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileProblem.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveObject.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `docs/architecture/data-integration.md`
- Modify: the tests covering both types in `SemiPlot.Tests.Data`

- [x] add `ConnectionFileProblem.VersionMismatch`, delete `ConnectionFileVersionMismatchError`, and
      move its construction site (`PostgresConnectionLoader.cs:156`) onto `ConnectionFileInvalidError`
      with that value and the same reason text
- [x] add an `ArchiveObject` enum with `Database` and `Table`; give `ArchiveNotInitialisedError` that
      discriminator, make `Table` nullable, branch its base message on the discriminator, and extend
      its XML doc's remedy table to the third case
- [x] delete `ArchiveDatabaseMissingError` and move its construction site
      (`ArchiveExceptionMapper.cs:104`) onto `ArchiveNotInitialisedError` with `ArchiveObject.Database`
- [x] update the two table rows in `docs/architecture/data-integration.md:432,434` that name the
      deleted types
- [x] update every test asserting on either deleted type, and add one asserting a `3D000` maps to the
      database discriminator while a `42P01` maps to the table one; assertions stay on error type and
      structured field, never on message text
- [x] run tests — must pass before task 3

### Task 3: Extract the pre-Avalonia startup probe

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupReadTimedOutError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`

- [x] create `StartupProbe` and `StartupData` as described; the probe touches no Avalonia type
- [x] bound each read with `Task.WaitAsync(TimeSpan)`; do not add a `CancellationToken` to
      `IDataProvider`, and record in a comment that the wait is abandoned while the query is not
- [x] change `Program` to run the probe and, on failure, log the error and exit without launching
      Avalonia — the error window arrives in Task 4
- [x] change `App.Run` to take `StartupData`; `InitializeServices` consumes the pens it carries and
      `LoadPens` goes, so `.GetAwaiter().GetResult().Value` survives nowhere on the startup path
- [x] add `FailPens` and `FailExtent` switches to `FakeDataProvider`, each returning a typed error
- [x] write tests: a successful probe carries pens and extent; a failed catalogue read returns the
      error and builds nothing; a failed extent read likewise; a read that exceeds its bound returns
      a failure rather than hanging
- [x] run tests — must pass before task 4

⚠️ **Deviation — the connection-file load moved to Task 5.** The probe's stated sequence opens on
`PostgresConnectionLoader.Load(<ConfigDir>/archive-connection.yaml)`, but that loader and
`PostgresConnectionSettings` live in `SemiPlot.DataSource.Postgres`, which `SemiPlot.UI` does not
reference — and adding that reference is Task 5's first checkbox. Doing it here would have emptied
that checkbox, so Task 3 keeps the stub registration under either value of `--use-stub` and reads no
configuration. `StartupData` therefore carries three values, not four; the settings join it in Task 5.

➕ **`StartupReadTimedOutError`** — `Task.WaitAsync` throws `TimeoutException`, and the probe must
return a failed `Result` rather than throw. No surviving `SemiPlot.Core.Data.Errors` type fits: the
probe knows no host, port or database yet, and `ArchiveQueryTimedOutError` means the *server* ended
the read, which would send the operator after a `statement_timeout` that is working as configured. The
type is `SemiPlot.UI.Startup`-local, so Task 4's reflection coverage test over the Core error namespace
is unaffected — but Task 4 must still give it an explicit arm rather than let it fall to the catch-all.

### Task 4: Map every error to a visible state

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupFailureView.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/ErrorWindow.axaml` and `ErrorWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`

- [x] create `StartupFailureView` and `StartupFailureMapper`, one arm per surviving public error type
      plus a generic catch-all, every string English and naming the operator's remedy
- [x] add `ErrorWindow` and `App.RunErrorWindow`, routed through `EnsureSingleStart`, and change
      `Program`'s failure branch to call it instead of exiting — the branch must not reach `App.Run`
- [x] write the reflection coverage test: enumerate public types in `SemiPlot.Core.Data.Errors`
      assignable to `IError` and assert each maps to something other than the catch-all
- [x] write one test per public error type asserting its mapped title and remedy
- [x] delete one arm, confirm the coverage test fails, restore it, and record that the gate fires
- [x] run tests — must pass before task 5

➕ **The coverage enumeration widened to the UI-local namespace.** It walks
`SemiPlot.Core.Data.Errors` **and** `SemiPlot.UI.Startup`, so `StartupReadTimedOutError` — and every
UI-local error type after it — is inside the gate rather than outside it. Asserting that one arm
separately was the alternative and would have left the next UI-local type unguarded. A second test
pins the enumeration at 8 types, because a coverage test over an empty set passes vacuously.

➕ **`ErrorWindowTests`** — two `[AvaloniaFact]`s that the window carries the view's three strings and
resolves no service. The mapper alone proves the state, not that anything shows it.

**Gate exercised.** Deleting the `ArchiveReadFailedError` arm failed `EveryPublicErrorType_MapsToItsOwnState`
with `Expected unmapped to be empty ..., but found at least one item {"SemiPlot.Core.Data.Errors.ArchiveReadFailedError"}`,
plus the two per-type tests for that error. The arm was restored.

### Task 5: Select the provider

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`

- [x] add the project reference from `SemiPlot.UI` to `SemiPlot.DataSource.Postgres`
- [x] ➕ load `<ConfigDir>/archive-connection.yaml` through `PostgresConnectionLoader.Load` at the head
      of `StartupProbe.Run`, short-circuiting on its error, and carry the settings on `StartupData` —
      deferred here from Task 3, which could not reference the loader's project
- [x] register `AddPostgresData(settings)` by default and `AddData()` only under `--use-stub`, with
      no fallback from one to the other
- [x] write tests that the default graph resolves `PostgresDataProvider`, that `--use-stub` resolves
      the stub, and that every service the probe and `InitializeServices` need is resolvable
- [x] run tests — must pass before task 6

➕ **`--use-stub` reads no connection file.** The stated sequence loads the file ahead of the branch,
but a development machine holding no `archive-connection.yaml` could then not reach the stub at all,
and `StartupProbeTests.Run_OverTheStubContainer_CarriesPensAndExtent` could not run. So the flag is
checked first and the load happens only on the archive path. `StartupData.Settings` is therefore
nullable and is null on the stub path only — it is a record of what was loaded, not a switch: the two
containers are built by two separate named methods (`BuildArchiveServiceProvider`,
`BuildStubServiceProvider`), so no lost-settings bug can silently produce the stub.

➕ **`StartupProbeTests` changed too.** `Run_OverTheComposedContainer_CarriesPensAndExtent` drove
`StartupOptions.Parse([])`, which now means the archive; it became
`Run_OverTheStubContainer_CarriesPensAndExtent` over `["--use-stub"]`. A second test was added —
`Run_WithNoConnectionFile_FailsInsteadOfFallingBackToTheStub` — pinning the no-fallback rule: a
`--config-dir` holding no file yields `ConnectionFileNotFoundError`, never a stub container.

➕ **The composition tests build the probe's own containers.** `StartupProbe.BuildArchiveServiceProvider`
and `BuildStubServiceProvider` are `internal` (`InternalsVisibleTo SemiPlot.Tests` already exists), so
the test resolves the graph `Program` builds rather than a look-alike assembled in the test. The five
existing resolution facts became `[Theory]`s over both containers. The archive settings are a local
copy of `SemiPlot.Tests.Data/Postgres/ConnectionSettingsFactory` pointing at `127.0.0.1:1`; resolving
the graph constructs an `NpgsqlDataSource` and opens nothing, so no server is needed.

### Task 6: Seed the window from the archive extent

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartNavigationControllerTests.cs`

- [x] seed the window from `StartupData`'s extent through `TrackDataExtents`, so `_hasData` is set
      and the first history envelope does not re-snap and undo the seed
- [x] leave an empty extent unseeded, opening on the wall clock
- [x] expose whatever accessor the test needs to assert the seeded first sample, or assert it through
      pan clamping — the controller has no first-sample property today
- [x] write tests for an extent wholly in the past, an extent covering now, and an empty extent
- [x] confirm the existing navigation tests pass unchanged; the ladder's numbers are pinned there and
      this slice must not move a rung
- [x] run tests — must pass before task 7

➕ **The seed is one method, `ChartNavigationController.SeedFromArchiveExtent(ArchiveExtent)`.** It owns
the empty-extent rule and delegates to `TrackDataExtents`, so the latch is set by the same path the
first envelope uses. `App.InitializeServices` calls it right after the chart view-model is built —
before `RequestInitialHistory`, which queries whatever window is in force, and before the minimap,
whose `ApplyExtent` reads the window back when its own extent arrives.

➕ **No first-sample accessor was added.** The four new tests assert the seed through pan clamping — a
30-day pan left stops at the extent's first sample rather than at startup minus one hour — which is
the behaviour the missing seed broke, and adds no public surface. The empty-extent test also asserts
that no `WindowChanged` is raised and that a later `TrackDataExtents` still snaps, proving the latch
stayed clear. `SemiPlot.Tests` 345 → 349 passed; the ladder and column-count tests are untouched.

⚠️ **The seeded window is sticky, and `postgres-live-edge-and-demo` inherits that.** `TrackDataExtents`
builds its model with `isSticky: true`, which the seed path keeps — the same value the first history
envelope produced before the seed existed, so this slice changes nothing. It becomes wrong once a live
edge exists: `ChartRealtimeApplier.Apply` calls `OnLiveEdge` on the first batch, and a sticky window
seeded on a stale archive would jump to the wall clock and undo the seed. It is unreachable here,
because `PostgresDataProvider.Subscribe` returns `Observable.Empty`. Choosing the right rule needs a
staleness threshold and a live edge to test against, both of which belong to that slice; it owns the
fix, along with whether a seeded window is sticky at all.

### Task 7: Drop a pen the provider omits

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartHistoryRequestDebouncer.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`

- [x] thread the requested identifiers from `HistoryRequest` through the debouncer's projection
      (`:41-42`) to `ApplyHistory`, which discards them today
- [x] give `TrendPenState` a way to clear its loaded curve
- [x] in `ApplyHistory`, clear the curve of a requested pen the result does not carry
- [x] write a test that a pen omitted from a later result leaves the chart rather than keeping the
      previous window's envelope
- [x] write a test that a pen added between request and apply, and therefore not in the requested
      list, is unaffected
- [x] run tests — must pass before task 8

➕ **The identifiers travel beside `TrendHistory`, not on it.** The debouncer's apply callback became
`Action<TrendHistory, IReadOnlyList<long>, long>` and `ApplyHistory` took a second parameter, so
`SemiPlot.Core`'s `TrendHistory` is untouched — a UI concern does not widen a Core record. Both apply
paths carry them: the debounced one from `HistoryRequest.PenIds`, and `RequestInitialHistory` from a
list captured once, before the await, and passed to both the query and the apply.

➕ **The dropped pen loses its envelope too, not only its curve.** `DropPensMissingFromHistory` removes
the `_envelopesById` entry as well as calling `TrendPenState.ClearHistory`. Keeping the envelope would
leave the cursor readout and `PenScaleModel` computing from the previous window's rows for a pen that
draws nothing. `ChartCursorReader` and `ChartDeltaCursorReader` both read that dictionary defensively,
so removal changes no other behaviour.

➕ **`FakeDataProvider` gained `OmittedPenIds`** — a set of identifiers the history read answers with no
envelope, which is the only way to reach the new branch. The file was not in this task's stated list.

**Both new tests were proved able to fail.** Deleting the `DropPensMissingFromHistory` call failed
`History_OmittingARequestedPen_ClearsThatPensCurve`; replacing `requestedPenIds` with `_pensById.Keys`
— the shortcut the plan forbids — failed `History_PenAddedWhileTheQueryWasInFlight_KeepsItsCurve` with
`Expected lateState.CenterPoints to contain 2 item(s), but found 0`. Both were restored. The stale-window
guard is unchanged: its early return precedes the drop, and
`StaleInitialHistory_DoesNotOverwriteANewerDebouncedGestureWindow` still passes.
`SemiPlot.Tests` 349 → 352 passed.

### Task 8: Pin the empty catalogue as a state

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs` if a sentence is added

- [x] write the named test the roadmap requires: an empty pen catalogue is a successful start that
      builds the view-models and produces no failure
- [x] decide whether `Pens: 0` (`MainWindow.axaml:67`) is the operator-visible state or whether a
      sentence is added, and record which; manual protocol step 7 reads whichever ships
- [x] run tests — must pass before task 9

**Decision — a sentence ships, not `Pens: 0` alone.** `MainWindowViewModel` gains
`IsCatalogueEmpty` (`ChartViewModel is not null && PenCount == 0`), and `MainWindow.axaml`'s already
present but always-hidden `MessagePanel` row binds its `IsVisible` to it and carries one line: *Pen
catalogue is empty: the archive answered and semiplot_tags holds no pens. Ask whoever commissions this
tool to fill it.* `Pens: 0` in the status bar stays. The reason: an operator reading `Pens: 0` beside a
blank plot cannot tell an unfinished commissioning from a broken chart, and that distinction is the
whole reason `postgres-catalog-and-extent` settled the empty catalogue as a success rather than an
error. The roadmap's scope guard forbids "no UI redesign beyond surfacing states" — a line shown only
when the count is zero, inside a panel and a grid row that already existed, is surfacing a state and
adds no layout. Manual protocol step 7 therefore reads that sentence.

➕ **The named test is its own file, `SemiPlot.Tests/UI/Startup/EmptyCatalogueStartupTests.cs`.**
The roadmap asks for the state "pinned by a named test of its own", and the pin has to reach the
view-models, which needs `[AvaloniaFact]` — `StartupProbeTests` is plain `[Fact]` by its own stated
premise that the probe touches no Avalonia, so mixing one in would have contradicted the file. The
probe-level half (an empty catalogue is a success and disposes no container) stays in
`StartupProbeTests` where the probe is tested.

➕ **The test drives `App.InitializeServices` itself.** It was `private static`; it is now `internal
static` (`InternalsVisibleTo SemiPlot.Tests` already exists), so the test covers the startup body the
running application executes rather than a look-alike rebuilt in the test — the same reasoning Task 5
applied to the probe's container builders. Its `IScheduler` is a `TestScheduler`, not
`CurrentThreadScheduler`: `InitializeServices` calls `TrendCoordinator.Start`, and a recurring realtime
subscription on the current thread's trampoline never returns control — the first run of the test hung
the whole suite.

➕ **`FakeDataProvider` gained an optional `pens` constructor argument** and
`MainWindowViewModelTests` gained one `[AvaloniaFact]` that `IsCatalogueEmpty` publishes on assignment,
since the sentence is bound and appears only if the property raises. Neither file was in this task's
stated list.

**Gate exercised.** Replacing `IsCatalogueEmpty` with `=> false` failed
`EmptyCatalogue_StartsNormallyAndReportsTheState` and
`ChartViewModel_WhenAssignedWithNoPens_PublishesTheEmptyCatalogueState` with
`Expected viewModel.IsCatalogueEmpty to be True, but found False.`; the property was restored.
`SemiPlot.Tests` 352 → 356 passed, `SemiPlot.Tests.Data` 394 passed, `dotnet format
SemiPlot.slnx --verify-no-changes` exit 0.

### Task 9: Verify acceptance criteria

- [x] run the full suite: `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` 356 passed / 0 failed / 0
      skipped, `SemiPlot.Tests.Data` 394 passed / 0 failed / 0 skipped, 750 total, exit 0. Docker was
      running and `semibase` was on `PATH`, so the 43 gated tests ran rather than skipped. Against the
      branch point (290 / 393) the slice added 66 tests to `SemiPlot.Tests` and 1 to `SemiPlot.Tests.Data`
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0 — exit 0
- [x] confirm every tracked `.cs` file still begins `ef bb bf` — 200 tracked `.cs` files, 0 missing
- [x] confirm `git grep` finds no surviving reference to the two deleted error types outside
      `docs/plans/completed/` — `git grep -E "ConnectionFileVersionMismatchError|ArchiveDatabaseMissingError"
      -- ':!docs/plans/'` returns nothing. Every surviving match is planning prose describing the merge:
      this file, `docs/plans/completed/20260817-postgres-provider-scaffold.md`,
      `docs/plans/completed/20260818-postgres-catalog-and-extent.md` and
      `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`. No code, no `docs/architecture/`
- [x] record that the coverage-test gate was exercised in Task 4 — recorded under **Gate exercised**
      there: deleting the `ArchiveReadFailedError` arm failed `EveryPublicErrorType_MapsToItsOwnState`
      and the two per-type tests; the arm was restored

**Acceptance Evidence, measured.**

| # | Check | Result |
| --- | --- | --- |
| 1 | `--filter FullyQualifiedName~CompositionRoot` | `DefaultContainer_ResolvesThePostgresProvider` asserts `BeOfType<PostgresDataProvider>()` over `StartupProbe.BuildArchiveServiceProvider`; `StubContainer_ResolvesTheStubProvider` asserts `RandomStubDataProvider` over `BuildStubServiceProvider`. Both are the builders `Run` calls, not look-alikes |
| 2 | `--filter FullyQualifiedName~StartupFailureMapper` | passes, and `ErrorTypeEnumeration_CoversBothNamespaces` pins the set at 8 — so the coverage test cannot pass vacuously. 8 = the 7 non-abstract public `IError` classes in `SemiPlot.Core.Data.Errors` plus `StartupReadTimedOutError` |
| 3 | failed `QueryPensAsync` | `ReadAsync_FailedCatalogue_CarriesTheErrorAndDisposesTheContainer` gets `IsFailed` carrying `ArchiveUnreachableError`, no throw, and the container disposed |
| 4 | window opens on a past extent | `SeedFromArchiveExtent_OpensTheWindowOnAnArchiveWhollyInThePast` seeds `2026-06-01T00:00Z .. 10:00Z`, asserts `To == last`, `From == last - 1h`, and that panning left clamps at `first` |
| 5 | omitted pen leaves the chart | `History_OmittingARequestedPen_ClearsThatPensCurve` — the pen's `CenterPoints` empty and `CurrentValue` null, its neighbour untouched |
| 6 | full suite, format, BOM | as recorded in the checkboxes above |

**The startup path carries no unchecked `Result`.** `App.LoadPens` is gone — `grep -rn "LoadPens" SemiPlot/`
finds nothing. Every `.Value` read on the startup path is behind an `IsFailed` guard:
`StartupProbe.cs:66` behind `:61`, `:117` behind `:104` and `:112`, `Program.cs:35,37` behind `:25`.

**`Program` cannot reach `App.Run` after `App.RunErrorWindow`,** by structure and twice over. The
failure branch (`Program.cs:25-33`) ends in an unconditional `return` inside the `if`, and `App.Run`
sits at `:37` after it — no fall-through exists. Independently, `App.Run` and `App.RunErrorWindow` both
open on `EnsureSingleStart()`, whose static `_started` latch throws on a second call, so even a caller
that ignored the return would get an `InvalidOperationException` rather than a second
`BuildAvaloniaApp()`.

➕ **One `.GetAwaiter().GetResult()` on a `Result` survives in `SemiPlot.UI`, deliberately.**
`StartupProbe.Read` (`:125`) blocks on `Task.Run(() => ReadAsync(...))` to hand a synchronous
`Result<StartupData>` to a synchronous `[STAThread] Main`. It is not the defect this slice removes: it
reads no `.Value`, throws no provider error, and both reads inside it are already bounded by
`WaitAsync`, so it returns within twice the read bound. Removing it needs an `async Main`, which
`[STAThread]` and Avalonia's builder do not take — out of this slice. The one
`.GetAwaiter().GetResult().Value` left in the repository is
`RandomStubDataProviderTests.cs:324`, a test helper over the stub, which cannot fail.

➕ **`StartupProbe.ObserveAbandoned` landed after this task signed off, and ships untested.** It attaches
a fault-only continuation touching `Task.Exception` to the read `WaitAsync` gave up on, so the task
nobody awaits is marked observed before the caller disposes the data source under it. It is defensive
hygiene, not a defect fix: nothing in this repository sets `ThrowUnobservedTaskExceptions`, so an
unobserved fault is raised on `TaskScheduler.UnobservedTaskException` and swallowed, and
`PostgresDataProvider.QueryPensAsync` catches `Exception` and answers `Result.Fail`, so the abandoned
task faults in a narrow case only. It carries no test because the state it changes — a task's observed
flag, read by the finalizer — is not observable from a test without a forced collection and a global
event handler, which would make the suite order-dependent. Both CRITICAL rules of this plan are broken
knowingly and recorded here rather than silently.

### Task 10: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/overview.md`
- Modify: `CLAUDE.md`

- [x] record the startup path, the configuration file location and the argument overrides —
      `data-integration.md` gains a **Startup** section (the pre-Avalonia probe, the sequence, the
      error window, the caller-side 30 s bound that abandons the wait and not the query, the empty
      catalogue as a success) and names `archive-connection.yaml` in **Configuration**;
      `overview.md` gains the `C:\DISTR\` paths and the command-line table under **Deployment**
- [x] record the narrowed error vocabulary and the coverage-test guard — `data-integration.md`
      states seven public types, all mapped by `StartupFailureMapper`, guarded by the reflection
      test pinned at eight (Core's seven plus `StartupReadTimedOutError`), and why the compiler
      cannot be that gate; `postgres-topology.md` reads **seven sealed types**, not nine
- [x] update the scope statement in `overview.md`, which says the composition root resolves the stub
      — corrected in three places (the component box, the `IDataProvider` paragraph, **Scope
      status**) and in `postgres-topology.md`, whose diagram and prose said the same
- [x] move this plan to `docs/plans/completed/` — **not done here, and deliberately.** Archiving is
      delivery work: the plan moves after the operator has run the manual protocol against a
      bench-seeded database. Nothing was moved or renamed

➕ **Two documents beyond the stated three carried the stale claim.**
`docs/architecture/postgres-topology.md` said the composition root still resolves the stub, labelled
`RandomStubDataProvider` "selected today" and counted nine sealed error types.
`docs/architecture/README.md`'s index entry for `data-integration.md` did not list the startup path
the new section adds. Both were corrected.

**Decision — `CLAUDE.md` gains one bullet, not two.** The pre-Avalonia probe split is a rule a
contributor can break by writing a plausible line (a read inside `.AfterSetup(...)`), and the DI
section already governs that boundary, so one bullet states it with the scheduler as the reason and
delegates the detail to `docs/architecture/data-integration.md`. The coverage-test guard over the
error vocabulary was not added: it is error-plane specifics, `CLAUDE.md` says not to add specifics,
and `data-integration.md` now holds it in full.

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** Required and substantial: this is the first slice that changes what the
running application does. The numbered protocol under Acceptance Evidence is the check, and it needs a
bench-seeded database.

**External system updates.** The application expects a connection file at
`C:\DISTR\Config\SemiPlot\archive-connection.yaml`, or wherever `--config-dir` points. A deployment
without one opens an error window rather than a chart.

**Remaining slices.** After this slice the roadmap continues with: avalonia-12-bump,
postgres-gap-reconstruction, postgres-live-edge-and-demo.

**Executed by exec:**

- branch: postgres-wire-up

## Verify it yourself

**The suite, with the database live.** `semibase.exe` v0.1.0 on `PATH`, Docker running,
`postgres:17-alpine` local, so nothing skips:

```powershell
dotnet test SemiPlot.slnx
```

`SemiPlot.Tests` 361 passed / 0 skipped, `SemiPlot.Tests.Data` 397 passed / 0 skipped, zero failures.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**The application reads the real archive by default.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~CompositionRoot"
```

`DefaultContainer_ResolvesThePostgresProvider` resolves through `StartupProbe.BuildArchiveServiceProvider`
— the builder `Run` itself calls — and asserts `PostgresDataProvider`. `StubContainer_ResolvesTheStubProvider`
asserts the stub is reachable only through `BuildStubServiceProvider`, which only `--use-stub` selects.
At `master` the composition root resolved the stub unconditionally.

**No public error type can reach the operator unmapped.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StartupFailureMapper"
```

The coverage test enumerates the public `IError` types in `SemiPlot.Core.Data.Errors` and
`SemiPlot.UI.Startup` and asserts each maps to something other than the catch-all;
`ErrorTypeEnumeration_CoversBothNamespaces` pins the count at 8 so the coverage test cannot pass over
an empty set. Delete one arm and the coverage test names the unmapped type.

**The startup read bound sits above the connect timeout, and that is now pinned.**
`DefaultReadBound_StaysAboveTheConnectTimeout` asserts 30 s against the `Timeout` the shipped
connection string yields (15 s, written explicitly rather than inherited). The two were equal before,
and an unreachable host lost the race by about 10 ms — measured — so it reported as a read that timed
out, whose remedy tells the operator the connection was accepted and the host and port are right.

**A failed startup opens a window instead of crashing.** `StartupProbeTests` drives a failed catalogue
read, a failed extent read, a read that exceeds its bound, a container whose `IDataProvider`
resolution throws, and a read faulting with `OperationCanceledException`. Each returns a failed
`Result` with the container disposed; none throws. At `master` `App.LoadPens` read
`.GetAwaiter().GetResult().Value` on a `Result` inside Avalonia's synchronous setup callback.

**The window opens on the archive.** `SeedFromArchiveExtent_OpensTheWindowOnAnArchiveWhollyInThePast`
seeds an extent months in the past and pans left to its first sample. Before the change the window
opened on `now - 1h`, `TrackDataExtents` never fired because no envelope had rows, and panning
clamped after the data — the minimap showed an extent the chart could not reach.

**A pen the provider omits leaves the chart.** `History_OmittingARequestedPen_ClearsThatPensCurve`,
with `History_PenAddedWhileTheQueryWasInFlight_KeepsItsCurve` proving the requested list is used
rather than the pen dictionary. Substituting `_pensById.Keys` fails the second.

**The manual protocol is not done, and no automated test replaces it.** This slice cannot be verified
end to end by any test in this repository: an end-to-end suite needs Avalonia and a container at once
and no CI runner provides both. The seven numbered steps under `## Acceptance Evidence` are the only
check that an operator reads the right sentence, and they need a bench-seeded database and a
connection file at `C:\DISTR\Config\SemiPlot\archive-connection.yaml`. Steps 4 and 6 exercise the two
remedies a review corrected.
