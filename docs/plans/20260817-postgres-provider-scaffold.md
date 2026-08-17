# PostgreSQL provider scaffold and the error contract

## Overview

Stand up `SemiPlot.DataSource.Postgres` with everything that needs no query, and settle the error
discipline the five remaining data-source slices will follow.

Three things ship. The project itself: referencing Core only, registering through a DI extension,
implementing `IDataProvider` with bodies that fail rather than pretend. The connection settings: a
YAML file with a version field, loaded into a record, turned into a connection string through the
Npgsql builder rather than string concatenation. And the time boundary: the archive stores naive
local timestamps and everything above the provider works in UTC, so one converter owns that
translation with the zone resolved once from configuration.

The error contract is the part that outlives this slice. Every later slice adds failure modes, and
without a settled shape each one invents its own. The rule adopted here is SemiStep's, re-anchored:
a public error type exists if and only if a distinct operator-visible **failure** sentence exists.
Everything else stays internal and crosses the boundary only mapped, with the raw detail riding into
the log.

Nothing here runs a query and nothing here changes what the application does. One new project, its
registration, and a set of types — the composition root still selects the stub, and no file outside
the new project and the error folder is edited.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-provider-scaffold

**The seam being implemented**

- `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs` — four members. The preceding slice
  `provider-pen-query-seam` replaces the `Pens` property with
  `Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()`, so by the time this slice runs **three** of the
  four return `Task<Result<...>>` and only `Subscribe` does not. This plan assumes that shape; if the
  seam slice has not landed, this one does not start.
- `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs` — the extent DTO, alongside the interface.

**The project shape to copy**

- `SemiPlot/SemiPlot.DataSource.Stub/SemiPlot.DataSource.Stub.csproj` — references `FluentResults`,
  `Microsoft.Extensions.DependencyInjection.Abstractions` and `System.Reactive`, plus a
  `ProjectReference` to Core. No `TargetFramework`, no `IsPackable`.
- `SemiPlot/SemiPlot.DataSource.Stub/DataServiceCollectionExtensions.cs:11-17` — `AddData(this
  IServiceCollection)` registering `IScheduler` and `IDataProvider` as singletons and returning the
  collection. The Postgres equivalent must not collide with this name.
- `SemiPlot/Directory.Build.props:5,9` — `TargetFramework` `net10.0` and `IsPackable` false apply to
  every project; a new csproj redeclares neither.

**Dependencies**

`SemiPlot/Directory.Packages.props` already carries `Npgsql` 10.0.3 (added by the bench slice) and
`FluentResults` 4.0.0. It carries no YAML library. `YamlDotNet` 18.1.0 is added, matching the version
the sibling SemiStep repository pins, so the two projects parse configuration with one library.

`SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj` references `Microsoft.NET.Test.Sdk`,
`Npgsql`, `Testcontainers.PostgreSql`, `xunit.v3` and `xunit.runner.visualstudio`. It does **not**
reference `Microsoft.Extensions.DependencyInjection` — Core brings only the abstractions — so the
composition test needs that package added.

**The error pattern being adopted**

Documented in the sibling repository at `Docs/architecture/error-reporting.md` (SemiStep is public;
SemiPlot is private and may cite it, not the reverse). Its ownership rule: Core owns the error text,
the UI only routes. The concrete shape, from
`SemiStep/SemiStep.Core/Recipes/Import/Errors/RecipeLoadFailedError.cs:5-9`:

```csharp
public sealed class RecipeLoadFailedError(string filePath)
    : Error($"Failed to load recipe from '{filePath}'")
{
    public string FilePath { get; } = filePath;
}
```

Sealed, primary constructor carrying the structured fields, message built in the base constructor,
fields exposed as get-only properties, one type per file. Where a state has no fields the type is
still its own class — `SemiStep/SemiStep.Core/Plc/Sync/PlcCommandFailedError.cs:5-8`.

**The states these types must cover**

`docs/architecture/data-integration.md:247-254` lists the failure rows this slice's types cover. An
archive with no rows in the window is not among them: that is **success with empty envelopes**.

**Two documents disagree about the empty catalogue, and this slice does not settle it.**
`data-integration.md:253` makes an empty or missing `semiplot_tags` an empty pen list with a
successful `Result`. The roadmap's `postgres-catalog-and-extent` entry at `:222-224` says the
opposite — "a distinct typed state (`EmptyTagCatalogError`-shaped) ... **not a silent empty list**".
A third constraint bears on it: SemiBase requires *missing* and *empty* to be distinguishable
(provisioning skipped versus commissioning unfinished), which a bare empty list cannot express.

This slice implements no catalogue read, so it adds no type either way and pre-empts nothing.
`postgres-catalog-and-extent` owns the decision, and whichever document loses is amended there. The
conflict is recorded here so that slice settles it deliberately rather than discovering it.

**Reader constraints that shape the timeout error**

`docs/architecture/postgres-instance.md` records the reader contract: SELECT-only, with
`statement_timeout` 30 s and `idle_in_transaction_session_timeout` 60 s set by SemiBase, so a slow
query fails with SQLSTATE `57014`. The timeout is a configured bound, not an accident, and the error
type says so.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- Complete each task fully before moving to the next.
- Every task that changes code carries its own tests, listed as separate checklist items.
- All tests pass before the next task starts.
- Update this plan when scope changes during implementation.

## Testing Strategy

**Everything in this slice is pure logic and needs no database.** All tests go in
`SemiPlot.Tests.Data` (plain `net10.0`, xunit v3). Per `CLAUDE.md` every test class carries all three
traits: `[Trait("Component","Core")]`, `[Trait("Area","Data")]` and `[Trait("Category","Unit")]` —
never `Category=Integration`, no container, no gate. The container fixture the bench slice built is
not used here and must not be.

**`SemiPlot.Tests.Data` uses raw xunit `Assert.` and no assertion library**, as `CLAUDE.md` records.
The four new test files follow that, not the `.Should()` style of `SemiPlot.Tests`.

**Tests assert by error type and by field, never by message text.** That is what makes the message
free to change and the contract stable. A test that greps a sentence pins the wrong thing.

**The loader is tested against real files**, written to a temp directory by the test, not against a
mocked file system: the failure modes that matter are a missing file, unreadable YAML, a version
that does not match, and a field that is absent or blank.

**No existing project is edited.** `SemiPlot.Tests`, `SemiPlot.UI` and `SemiPlot.DataSource.Stub` are
untouched — that is the guard in Acceptance Evidence 6, and it is what makes this slice cheap to
verify.

## Acceptance Evidence

There is no defect to reproduce — this slice adds a project that does not exist. The evidence is
therefore that each piece exists and behaves, by runnable command.

1. **The provider resolves from the container.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresComposition"`
   A test builds a `ServiceCollection`, calls the new extension, resolves `IDataProvider`, and
   asserts the concrete type is the Postgres provider. This is what proves the registration, which a
   compile cannot.

2. **Unimplemented members fail rather than lie.**
   Calling `QueryPensAsync`, `QueryHistoryAsync` or `QueryArchiveExtentAsync` on the scaffold returns
   a failed `Result` carrying the not-implemented error type — not `null`, not an empty success, not
   a throw. An empty success here would silently render a blank chart in a later slice.

3. **The loader accepts a valid file and rejects each invalid one by type.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresConnectionLoader"`
   One test per state: valid file loads with every field populated; missing file, malformed YAML,
   wrong version, missing required field, and unknown time-zone identifier each produce their own
   error type. Assertions are on the type and its fields.

4. **The connection string is built, not concatenated.**
   A test asserts that a password containing `;` and `'` round-trips through the settings into a
   connection string that `NpgsqlConnectionStringBuilder` parses back to the same password. String
   concatenation fails this; the builder passes it.

5. **The time boundary round-trips a known instant.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ArchiveTimeConverter"`
   A naive local timestamp converts to a `DateTime` with `Kind = Utc` and back to the identical naive
   value, for a fixed zone and a fixed instant. Daylight-saving behaviour at a transition is asserted
   explicitly, whatever it is, so the choice is recorded rather than discovered later.

6. **Nothing outside the new project and the error folder is touched.**
   `dotnet test SemiPlot.slnx` — zero failures, and `SemiPlot.Tests` reports the same passing count
   as at the branch point.
   `git diff --name-only master...HEAD` lists only files under
   `SemiPlot/SemiPlot.DataSource.Postgres/`, `SemiPlot/SemiPlot.Core/Data/Errors/`,
   `SemiPlot/SemiPlot.Tests.Data/`, `SemiPlot.slnx`, `SemiPlot/Directory.Packages.props`, `docs/` and
   this plan. `SemiPlot/SemiPlot.UI/`, `SemiPlot/SemiPlot.DataSource.Stub/` and
   `SemiPlot/SemiPlot.Tests/` appear nowhere.

## Progress Tracking

- Mark completed items `[x]` immediately when done.
- Add newly discovered tasks with `+`.
- Record blockers with a `BLOCKED` note and the reason.
- Keep this file in sync with the work actually done.

## Solution Overview

**Two error planes, and the boundary is the provider's public surface.** Inside the provider,
failures are whatever they are — `NpgsqlException`, a SQLSTATE string, a YAML parse exception. None
of that crosses. At the boundary each is mapped to one of a small set of sealed public types living
in Core beside `IDataProvider`, and the original rides `.CausedBy(...)` so the log keeps it. A later
slice that needs a new distinction adds a type; a later slice that changes how Npgsql reports
something changes nothing public.

**The public set is small on purpose.** Six types, derived from the failure rows of the table at
`docs/architecture/data-integration.md:247-254`. The rule, stated precisely, is:

> A public error type exists if and only if a distinct operator-visible **failure** sentence exists.
> Operator-visible states that are not failures travel in the success channel.

The qualifier matters and is not in SemiStep's original wording. "No variables configured" is a
distinct sentence an operator reads, and it is not a failure — SemiStep never hit this because its
rule was anchored to localization coverage, which SemiPlot drops. Re-anchoring the rule is exactly
where the wording has to be re-derived rather than copied.

**The enforcement mechanism is not yet proven.** SemiStep's coverage test works because every public
reason type needs a resource pair, which a reflection test can check. SemiPlot has no localization,
so the equivalent test has to assert something else — that every public error type maps to a UI
state. Whether that is mechanically checkable is for `postgres-startup-and-composition` to establish;
this plan does not assume it, and `ProviderNotImplementedError`'s removal is owned by a named slice
rather than left to that test.

**Where this slice stops.** The roadmap requires that a missing or invalid configuration become a
visible error state rather than a silent fall back to the stub. This slice defines the types that
state needs and the loader that produces them; it does **not** wire them into the composition root,
which stays on the stub until `postgres-startup-and-composition`. The boundary is therefore: the
loader returns a failed `Result` carrying a typed error, and nothing yet consumes it. That is a
deliberate half, and the composition slice is where the other half lands.

**The provider is a scaffold, and says so in its behaviour.** All three `Result`-returning members
return a failed `Result` with a not-implemented error rather than throwing or returning an empty
success. A later slice replaces each body one at a time, and until it does, a mis-wired composition
fails loudly instead of drawing an empty chart. `Subscribe` returns an observable that completes
immediately — it has no `Result` to fail through, and an empty stream is the honest answer for a
provider with nothing to stream.

## Technical Details

**The public error types**, in `SemiPlot.Core/Data/Errors/`, one file each:

| Type | Fields | Operator sentence |
| --- | --- | --- |
| `ConnectionFileNotFoundError` | path | The connection file is not where it was expected |
| `ConnectionFileInvalidError` | path, reason | The file exists but cannot be read as configuration |
| `ConnectionFileVersionMismatchError` | path, found, expected | The file is a version this build does not accept |
| `ArchiveUnreachableError` | host, port, database | No connection to the archive |
| `ArchiveNotInitialisedError` | database | The database is there but the SCADA has never run |
| `ArchiveQueryTimedOutError` | timeout | The read exceeded its configured bound |

`ArchiveUnreachableError` and `ArchiveNotInitialisedError` are separate because the operator's next
action differs: check the network or the server, versus start the SCADA once. The same distinction
separates a socket failure from SQLSTATE `3D000`, and `trends` being absent from the database being
absent — a later slice needs all three and this type set must not collapse them.

A seventh type, `ProviderNotImplementedError`, exists only while the scaffold does. Tracing the four
members to the slices that implement them — `QueryPensAsync` and the extent to
`postgres-catalog-and-extent`, `QueryHistoryAsync` to `postgres-history-read`, `Subscribe` to
`postgres-realtime-poll` — makes **`postgres-realtime-poll` the owner of its deletion**, and Task 7
amends the roadmap to say so. A duty with no owner migrates to whoever notices. The type is marked
temporary in its own summary so it is not mistaken for a permanent part of the contract.

**An unknown time-zone identifier is validated by the loader, not by the converter.** It is a
malformed configuration value, and `ConnectionFileInvalidError` carries a path — which the loader has
and the converter does not. Validating it at load time also matches `data-integration.md:260`: a
malformed file is reported at startup rather than at first query. The converter is therefore
constructed only from an identifier already known to resolve.

**Connection settings.** A record with host, port, database, username, password, source time zone,
poll interval, schema, statement timeout, and the file-version field. The YAML DTO is a separate
type with underscored member names, mapped into the record by the loader — the DTO is what the file
looks like, the record is what the code wants, and they are allowed to differ. The connection string
is built by the settings record itself, through `NpgsqlConnectionStringBuilder`.

**The connection string goes through `NpgsqlConnectionStringBuilder`.** A password may contain `;`
or `'`, which concatenation corrupts silently: the connection then fails with an authentication
error that points at the wrong cause.

**The time boundary.** `t` in the archive is naive local wall-clock time of the SCADA machine, with
no zone stored anywhere. The converter resolves `TimeZoneInfo` once from the configured identifier
and exposes two directions: a naive value read from the archive becomes a `DateTime` with
`Kind = Utc`, and a UTC window bound becomes the naive value a query parameter needs. .NET 10 accepts
IANA identifiers on Windows, so the configured zone need not be a Windows name. Daylight-saving
transitions are the one place this is not a bijection, and the plan requires the behaviour to be
asserted rather than assumed.

**The scaffold takes no constructor dependencies.** It is registered as a singleton and resolves
without a settings object; wiring settings into it belongs to the slice that first opens a
connection.

## What Goes Where

- **Implementation Steps** — the error types, the project, the loader, the converter, verification
  and documentation.
- **Post-Completion** — what the composition slice must pick up, and the remaining slices.

## Implementation Steps

### Task 1: The public error surface in Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileNotFoundError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileInvalidError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileVersionMismatchError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveUnreachableError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveQueryTimedOutError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ProviderNotImplementedError.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs`

- [ ] one sealed class per file, each deriving from `FluentResults.Error`, with a primary
      constructor carrying the fields from the Technical Details table and the message built in the
      base constructor, following
      `SemiStep/SemiStep.Core/Recipes/Import/Errors/RecipeLoadFailedError.cs:5-9`
- [ ] fields are get-only properties assigned from the primary constructor parameters
- [ ] `ProviderNotImplementedError` carries the member name and its summary states that it is
      removed when the last member is implemented
- [ ] no error type is added for an empty or missing `semiplot_tags`. Not because the answer is
      settled — two documents disagree, as Context records — but because this slice reads no
      catalogue and must not pre-empt the slice that does. An empty query window stays a successful
      `Result` per `docs/architecture/data-integration.md:254`, which nothing contradicts
- [ ] write tests: each type's fields survive construction, and each message contains the field
      values that identify the case — asserted on the fields, with the message checked only for
      containing the field values, never for exact wording
- [ ] run tests — must pass before Task 2

### Task 2: The provider project and its registration

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot.slnx`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [ ] project referencing `SemiPlot.Core` only — never the UI, never the Stub — with `Npgsql`,
      `FluentResults`, `Microsoft.Extensions.DependencyInjection.Abstractions` and `System.Reactive`,
      mirroring `SemiPlot/SemiPlot.DataSource.Stub/SemiPlot.DataSource.Stub.csproj`; declare neither
      `TargetFramework` nor `IsPackable`
- [ ] `PostgresDataProvider` takes no constructor dependencies and implements all four members; the
      three `Result`-returning methods return a failed `Result` carrying `ProviderNotImplementedError`
      and `Subscribe` returns an observable that completes immediately
- [ ] the DI extension is named so it cannot collide with the stub's `AddData`
      (`SemiPlot/SemiPlot.DataSource.Stub/DataServiceCollectionExtensions.cs:11`) — both may be
      referenced from the same composition root in a later slice
- [ ] register the project in `SemiPlot.slnx`, reference it from `SemiPlot.Tests.Data`, and add
      `Microsoft.Extensions.DependencyInjection` to that test project — it currently has only the
      abstractions, and `BuildServiceProvider()` lives in the implementation package
- [ ] write tests: the extension registers `IDataProvider`, resolving it yields
      `PostgresDataProvider`, and the registration is a singleton
- [ ] write tests: each of the three `Result`-returning members returns a failed `Result` carrying
      `ProviderNotImplementedError` — not a throw, not an empty success
- [ ] run tests — must pass before Task 3

### Task 3: Connection settings and their loader

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionDto.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresConnectionLoaderTests.cs`

- [ ] add `YamlDotNet` 18.1.0 to central package management, matching the version the sibling
      SemiStep repository pins, and reference it from the provider project
- [ ] `PostgresConnectionSettings` record with the fields listed under Technical Details, exposing
      the connection string built through `NpgsqlConnectionStringBuilder`
- [ ] `PostgresConnectionDto` with underscored member names matching the YAML, mapped into the
      record by the loader
- [ ] the loader returns `Result<PostgresConnectionSettings>`, never throws, and maps each failure to
      the matching Task 1 type: absent file, unreadable YAML, version mismatch, missing or blank
      required field, and an unknown time-zone identifier
- [ ] the loader resolves the configured zone with `TimeZoneInfo` so an unknown identifier is a
      `ConnectionFileInvalidError` at load time, carrying the path the converter does not have
- [ ] write tests against real files in a temp directory: a valid file populates every field; a
      missing file, malformed YAML, a wrong version, a blank required field and an unknown zone each
      yield their own error type, asserted by type and fields
- [ ] write a test that a password containing `;` and `'` round-trips through the builder and parses
      back to the same value
- [ ] run tests — must pass before Task 4

### Task 4: The time boundary converter

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveTimeConverterTests.cs`

- [ ] the converter is constructed from a `TimeZoneInfo` the loader already resolved, so it has no
      failure mode of its own and needs no factory
- [ ] naive local from the archive to `DateTime` with `Kind = Utc`; UTC window bound to the naive
      local value a query parameter needs
- [ ] write tests: a known instant round-trips both directions and `Kind` is correct on the way out
- [ ] write a test asserting the behaviour at a daylight-saving transition — an ambiguous local time
      and a skipped one — pinning whatever the chosen behaviour is. Choose a zone and instant whose
      transition is identical under Windows registry data and Linux tzdata, because
      `SemiPlot.Tests.Data` runs on both (`.github/workflows/ci.yml`, the `data-tests` job); a
      historical date or an unusual zone makes this a cross-platform flake
- [ ] run tests — must pass before Task 5

### Task 5: Verify acceptance criteria

- [ ] every check in Acceptance Evidence runs and produces its stated result
- [ ] `dotnet test SemiPlot.slnx` — zero failures across both test projects, and `SemiPlot.Tests`
      reports the same passing count as at the branch point
- [ ] `git diff --name-only master...HEAD` lists no file under `SemiPlot/SemiPlot.UI/`,
      `SemiPlot/SemiPlot.DataSource.Stub/` or `SemiPlot/SemiPlot.Tests/`
- [ ] no test added by this slice carries `[Trait("Category","Integration")]` and none needs a
      container: everything here is pure logic
- [ ] every new test class carries all three traits and uses raw xunit `Assert.`, per `CLAUDE.md`
- [ ] `dotnet format SemiPlot.slnx` reports no changes

### Task 6: Update documentation

- [ ] record the two-plane error contract in `docs/architecture/data-integration.md` beside the
      error-semantics table: the public set, the rule as worded in Solution Overview, and that
      internal failures cross only mapped with the original on `CausedBy`
- [ ] note in `CLAUDE.md` that provider failures are typed errors in `SemiPlot.Core/Data/Errors/`
      and that tests assert by type and field, never by message text
- [ ] amend the roadmap entry for `postgres-realtime-poll` to name it the owner of
      `ProviderNotImplementedError`'s deletion
- [ ] leave the empty-catalogue conflict between the roadmap and `data-integration.md` unresolved and
      recorded — `postgres-catalog-and-extent` owns it, and amending either document here would
      pre-empt the slice that has to live with the answer
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**This slice depends on `provider-pen-query-seam` having landed.** That slice changes
`IDataProvider.Pens` into `QueryPensAsync()` and follows the change through the stub, the
coordinator, the composition root, the main-window view model and their tests. This plan's provider
implements the post-change shape and does not start before it.

**What the composition slice must pick up.** This slice deliberately produces a loader whose failed
`Result` nothing consumes. `postgres-startup-and-composition` is where a missing or invalid
configuration becomes a visible operator state rather than a silent fall back to the stub, and where
the coverage test is added — if a mechanical form of it can be built, which this plan does not
assume.

**What the composition slice must additionally define, and this slice deliberately does not.** The
startup probe distinguishes states no code here can produce: an unexpected `trends` shape and a
non-empty default partition. Their error types belong to the slice that can raise them — the slice
that can produce a failure defines its type — so they are absent here by design rather than by
oversight.

**`ProviderNotImplementedError` is deleted by `postgres-realtime-poll`**, which implements
`Subscribe`, the last of the four members to be filled in.

**Remaining slices**

After this slice the roadmap continues with: postgres-catalog-and-extent, postgres-history-read,
postgres-bucketed-read, postgres-gap-reconstruction, postgres-realtime-poll,
postgres-startup-and-composition, live-demo-and-stub-retirement.
