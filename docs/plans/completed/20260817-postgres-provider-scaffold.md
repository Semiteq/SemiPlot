# PostgreSQL provider scaffold and the error contract

## Overview

Stand up `SemiPlot.DataSource.Postgres` with everything that needs no query, and settle the error
discipline the seven remaining data-source slices will follow.

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
registration, and a set of types — the composition root still selects the stub. `SemiPlot.UI`,
`SemiPlot.DataSource.Stub` and `SemiPlot.Tests` are untouched. What changes outside the new project
is the error folder `SemiPlot/SemiPlot.Core/Data/Errors/`, the solution, central package management,
`SemiPlot.Tests.Data` which exercises the scaffold, and documentation.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-provider-scaffold

**The seam this slice implements against**

- `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs` — four members, of which **three** return
  `Task<Result<...>>`: `QueryPensAsync` (`:12`), `QueryHistoryAsync` and `QueryArchiveExtentAsync`.
  Only `Subscribe` does not. The pen catalogue is a query rather than a property because reading it
  can fail (`docs/architecture/data-integration.md:61-64`).
- `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs` — the extent DTO, alongside the interface.

**The project shape to copy**

- `SemiPlot/SemiPlot.DataSource.Stub/SemiPlot.DataSource.Stub.csproj` — references `FluentResults`,
  `Microsoft.Extensions.DependencyInjection.Abstractions` and `System.Reactive`, plus a
  `ProjectReference` to Core. No `TargetFramework`, no `IsPackable`.
- `SemiPlot/SemiPlot.DataSource.Stub/DataServiceCollectionExtensions.cs:11-17` — `AddData(this
  IServiceCollection)` registering `IScheduler` and `IDataProvider` as singletons (`:13-14`) and
  returning the collection. The Postgres equivalent is `AddPostgresData` and registers the same
  two services.
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

`docs/architecture/data-integration.md:267-276` is the error-semantics table. Its failure rows are
`:269` connection refused, `:270` connection lost, `:271` query timeout, `:272` the database missing,
`:273` credentials refused and `:274` `trends` missing, and five of this slice's types cover them,
the two connection rows sharing one type. Three more types come from the configuration contract at
`:326-350`, which requires a malformed file to be reported at startup rather than at first query. Two
of the rows are ones this slice adds together with their types — `ArchiveDatabaseMissingError`
(SQLSTATE `3D000`) and `ArchiveAccessDeniedError` (SQLSTATE `28P01`, `28000`, `42501`) — so the
shipped set and the document agree.

An archive with no rows in the window is not a failure at all: that is **success with empty
envelopes** (`:276`).

**Two documents disagree about the empty catalogue, and this slice does not settle it.**
`data-integration.md:275` makes an empty or missing `semiplot_tags` an empty pen list with a
successful `Result`. The roadmap's `postgres-catalog-and-extent` entry at `:249-250` says the
opposite — "a distinct typed state (`EmptyTagCatalogError`-shaped) ... **not a silent empty list**".
A third constraint bears on it: SemiBase requires *missing* and *empty* to be distinguishable
(provisioning skipped versus commissioning unfinished), which a bare empty list cannot express.

This slice implements no catalogue read, so it adds no type either way and pre-empts nothing.
`postgres-catalog-and-extent` owns the decision, and whichever document loses is amended there. The
conflict is recorded here so that slice settles it deliberately rather than discovering it.

**Reader constraints that shape the timeout error**

`docs/architecture/postgres-instance.md` records the reader contract: SELECT-only, with
`statement_timeout` 30 s and `idle_in_transaction_session_timeout` 60 s set by SemiBase as role
session defaults, so a slow query fails with SQLSTATE `57014`. The bound is the server's and SemiPlot
sends no `statement_timeout` in any form, so the error type reports the effective value the failing
session ran under rather than a value SemiPlot chose.

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

**Tests assert by error type and by structured field, never on exact message wording.** That is what
makes the message free to change and the contract stable. Checking that a message contains a field
value is allowed; pinning a whole sentence by equality is not.

**The loader is tested against real files**, written to a temp directory by the test, not against a
mocked file system: the failure modes that matter are a missing file, unreadable YAML, a version
that does not match, a field that is absent or blank, and an unknown time-zone identifier.

**`SemiPlot.Tests`, `SemiPlot.UI` and `SemiPlot.DataSource.Stub` are untouched.** `SemiPlot.Core` and
`SemiPlot.Tests.Data` do change — the error folder, the project references and the four new test
files. That split is the guard in Acceptance Evidence 6, and it is what makes this slice cheap to
verify.

## Acceptance Evidence

There is no defect to reproduce — this slice adds a project that does not exist. The evidence is
therefore that each piece exists and behaves, by runnable command.

1. **The provider resolves from the container.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresComposition"`
   A test builds a `ServiceCollection`, calls `AddPostgresData`, resolves `IDataProvider`, and
   asserts the concrete type is the Postgres provider. A second resolves `IScheduler`, which the
   extension registers as well. This is what proves the registration, which a compile cannot.

2. **Unimplemented members fail rather than lie.**
   Calling `QueryPensAsync`, `QueryHistoryAsync` or `QueryArchiveExtentAsync` on the scaffold returns
   a failed `Result` carrying the not-implemented error type — not `null`, not an empty success, not
   a throw. An empty success here would silently render a blank chart in a later slice.

3. **The loader accepts a valid file and rejects each invalid one, distinguishably.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresConnectionLoader"`
   One test per state, eight failing states over three error types: a missing file and a blank path
   yield `ConnectionFileNotFoundError`; a wrong version yields
   `ConnectionFileVersionMismatchError`; a path that cannot be opened, malformed YAML, a blank
   required field, a value outside its range and an unknown time-zone identifier all yield
   `ConnectionFileInvalidError`, separated by its discriminator — `Unreadable`, `Unparseable`,
   `MissingField`, `OutOfRange`, `UnknownTimeZone`. Assertions are on the type and its structured
   fields, the discriminator included. A valid file loads with every field populated. A failed
   result carries its causing exception on `CausedBy` while neither the message nor the reason
   repeats the parser's text, which would embed the password.

4. **The connection string is built, not concatenated.**
   A test asserts that a password containing `;` and `'` round-trips through the settings into a
   connection string that `NpgsqlConnectionStringBuilder` parses back to the same password. String
   concatenation fails this; the builder passes it. One more asserts what the string carries: no
   `Options` key and no `statement_timeout` in any form, and `Command Timeout=0` — asserted on the
   emitted string as well as on the parsed value, because the builder answers its own default for a
   key the string never carried. A further test asserts that formatting the settings never prints the
   password.

5. **The time boundary round-trips a known instant.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ArchiveTimeConverter"`
   A naive local timestamp converts to a `DateTime` with `Kind = Utc` and back to the identical naive
   value, for a fixed zone and a fixed instant. Both irregular local times are asserted explicitly:
   an ambiguous one resolves to the standard-time instant, a skipped one resolves deterministically
   instead of throwing. So are the consequences — a naive sequence across the autumn fall-back
   converts to a sequence that repeats an hour, the cosmetic duplicate
   `docs/architecture/data-integration.md:216` accepts; an ascending naive sequence across the
   spring-forward gap converts to a descending one; and a UTC window over the fall-back converts to
   a zero-width local window.

6. **The UI, the stub and their tests are untouched.**
   `dotnet test SemiPlot.slnx` — zero failures, and `SemiPlot.Tests` reports the same passing count
   as at the branch point.
   `git diff --name-only master...HEAD` lists only files under
   `SemiPlot/SemiPlot.DataSource.Postgres/`, `SemiPlot/SemiPlot.Core/Data/Errors/`,
   `SemiPlot/SemiPlot.Tests.Data/`, `SemiPlot.slnx`, `SemiPlot/Directory.Packages.props`, `CLAUDE.md`,
   `readme.md`, `docs/` and this plan. `SemiPlot/SemiPlot.UI/`,
   `SemiPlot/SemiPlot.DataSource.Stub/` and `SemiPlot/SemiPlot.Tests/` appear nowhere.
   `readme.md` is on the list for one line: it named a `SimpleScadaDataProvider` that this slice
   supersedes with `PostgresDataProvider`.

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

**The public set is small on purpose.** Eight types: three from the failure rows of the
error-semantics table at `docs/architecture/data-integration.md:267-276`, three from the
configuration contract at `:326-350`, and `ArchiveDatabaseMissingError` and
`ArchiveAccessDeniedError`, which this slice adds and Task 6 writes into that table. The rule, stated
precisely, is:

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
| `ConnectionFileInvalidError` | path, kind (`Unreadable` \| `Unparseable` \| `MissingField` \| `OutOfRange` \| `UnknownTimeZone`), reason | The file exists but cannot be read as configuration |
| `ConnectionFileVersionMismatchError` | path, foundVersion, expectedVersion | The file is a version this build does not accept |
| `ArchiveUnreachableError` | host, port, database | No connection to the archive |
| `ArchiveDatabaseMissingError` | host, port, database | The server answers but the database does not exist |
| `ArchiveAccessDeniedError` | host, port, database, username | The credentials or the grants are wrong |
| `ArchiveNotInitialisedError` | host, port, database, table | The database is there but a table the read needs is not |
| `ArchiveQueryTimedOutError` | host, port, database, timeout (`TimeSpan`, the effective server bound the session ran under) | The read exceeded its configured bound |

`ConnectionFileInvalidError` carries a discriminator enum beside its path and reason because one type
covers five loader states that share one operator sentence — the file is at the path given and cannot
be used as configuration. The remedy is not shared: `Unreadable` sends the operator to the permissions
or the path, the other four to the file's contents, and telling those apart is exactly what the
discriminator is for. Without it a test can assert only prose, and an operator message cannot say
which of the five happened. `ConnectionFileNotFoundError` stays a separate type rather than a sixth
discriminator value because even its sentence is a different one: the file is not there at all, so
create it or fix the path.

Every `Archive*` type carries host, port and database, so an operator reading any of them knows which
archive answered. `ArchiveQueryTimedOutError`'s `timeout` is the **effective** `statement_timeout` the
failing session ran under, read back from that session: the bound belongs to the reader role
(`docs/architecture/postgres-instance.md:39-40` records its 30 s) and SemiPlot sends no
`statement_timeout` in any form, so a configured value is not something this error could carry.
SQLSTATE `57014` is what the server answers when the bound fires.
`ArchiveNotInitialisedError` carries the table name rather than assuming `trends`, because `42P01` is
table-agnostic and the remedy follows the table.

**Four states, four types, because four operator actions.** A socket failure means check the network
or the server. SQLSTATE `3D000` — the server answers, the database is absent — means run `semibase
create`. SQLSTATE `28P01`, `28000` or `42501` means fix the user, the password or the grants.
SQLSTATE `42P01` — the database is there, a table is not — means start the SCADA once for `trends` or
run `semibase create` for `semiplot_tags`. Collapsing any pair sends the operator to the wrong
remedy, so the set carries all four even though no slice can raise them until
`postgres-catalog-and-extent` opens the first connection.

**Five of the eight types are anticipatory, three are not.** `ArchiveUnreachableError`,
`ArchiveDatabaseMissingError`, `ArchiveAccessDeniedError`, `ArchiveNotInitialisedError` and
`ArchiveQueryTimedOutError` are vocabulary for the slices that can raise them, first
`postgres-catalog-and-extent`. The three connection-file types are live here: Task 3's loader raises
all three and Acceptance Evidence 3 tests them. The five anticipatory types' **fields** are
provisional — `postgres-catalog-and-extent` writes the real SQLSTATE mapping and may add, rename or
drop a field on any of them as that mapping demands. Revising them there is in scope, not scope
creep; only the existence of the five types and their one-per-operator-remedy split is settled here.

A ninth type, `ProviderNotImplementedError`, exists only while the scaffold does. Tracing the four
members to the slices that implement them — `QueryPensAsync` and the extent to
`postgres-catalog-and-extent`, `QueryHistoryAsync` to `postgres-history-read`, `Subscribe` to
`postgres-realtime-poll` — makes **`postgres-realtime-poll` the owner of its deletion**, and Task 6
amends the roadmap to say so. A duty with no owner migrates to whoever notices. The type is marked
temporary in its own summary so it is not mistaken for a permanent part of the contract.

**An unknown time-zone identifier is validated by the loader, not by the converter.** It is a
malformed configuration value, and `ConnectionFileInvalidError` carries a path — which the loader has
and the converter does not. Validating it at load time also matches `data-integration.md:347-350`: a
malformed file is reported at startup rather than at first query. The converter is therefore
constructed from a `TimeZoneInfo` that already resolved, never from an identifier string.

**The loader validates ranges, not only presence.** A `port` of 0 and a negative `poll_interval_ms`
both parse as integers, so presence checks pass them through. A port outside 1..65535 then throws
`ArgumentOutOfRangeException` inside `NpgsqlConnectionStringBuilder` on the first read of
`ConnectionString` — an untyped throw long after the load the operator was told succeeded — and a
non-positive interval becomes a `TimeSpan` nothing downstream checks. They are file faults like any
other, so they travel as `ConnectionFileInvalidError` with the discriminator `OutOfRange`.

**Connection settings.** A record with host, port, database, username, password, the source time zone
as a resolved `TimeZoneInfo`, poll interval, schema, and the file-version field. It carries no query
bound: that one belongs to the server. The zone is resolved once, by the loader, where the error
path for an unknown identifier exists; nothing downstream carries the raw identifier string. The
YAML DTO is a separate type whose member names map onto the file's underscored keys through the
deserializer's naming convention — the DTO is what the file looks like, the record is what the code
wants, and they are allowed to differ. The record overrides `ToString` so the compiler-generated
form cannot print the password, which it would otherwise do twice: once as the member and once
inside the connection string.

**The connection string goes through `NpgsqlConnectionStringBuilder`.** A password may contain `;`
or `'`, which concatenation corrupts silently: the connection then fails with an authentication
error that points at the wrong cause.

**The server owns the query bound, and the connection string states that in two ways.** It carries no
`Options` key and no `statement_timeout` in any form: a startup-packet `-c` switch is GUC source
`PGC_S_CLIENT` and outranks the `PGC_S_USER` value `semibase create` installs on the role, so sending
one replaces the DBA's bound with SemiPlot's. It carries `Command Timeout=0` — infinite — explicitly
rather than omitting the key, because an omitted key leaves Npgsql's implicit 30 s client bound in
force, which fires ahead of any server bound and raises `NpgsqlException` with an inner
`TimeoutException` instead of the SQLSTATE `57014` the read path maps. The per-command backstop and
the read-back of the effective bound belong to `postgres-catalog-and-extent`, which opens the first
connection; Post-Completion names them.

**The time boundary.** `t` in the archive is naive local wall-clock time of the SCADA machine, with
no zone stored anywhere. The converter takes the `TimeZoneInfo` the loader resolved and exposes two
directions: a naive value read from the archive becomes a `DateTime` with `Kind = Utc`, and a UTC
window bound becomes the naive value a query parameter needs. .NET 10 accepts IANA identifiers on
Windows, so the configured zone need not be a Windows name. Daylight-saving transitions are the one
place this is not a bijection, and `data-integration.md:215-216` already settles what that costs:
they "shift or duplicate an hour of history; this is accepted as cosmetic".

**The naive-to-UTC direction must be total, and the obvious implementation is not.**
`TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException` on a local time that does not exist —
the hour skipped at the spring-forward transition. That is not a test-only curiosity: a
`source_time_zone` that was misconfigured or changed puts real archive rows inside the gap, and
`data-integration.md` already records that rows written before such a change stay in the old zone
and are not correctable. A throwing converter would then detonate in the middle of a query, where no
public error type fits — it is not a connection-file fault, and it happens long after load time.

So the converter resolves both irregular cases rather than propagating them:

| Local input | Resolution |
| --- | --- |
| Ambiguous (repeated hour, autumn) | the **standard-time** offset — what `TimeZoneInfo` itself resolves an ambiguous local time to, so the converter needs no branch for this case at all |
| Invalid (skipped hour, spring) | `TimeZoneInfo.BaseUtcOffset` applied to the wall-clock value, which places it deterministically just past the gap |

`BaseUtcOffset` is the standard-time offset for every zone whose daylight saving is positive, which
is every zone in use here. A zone modelled with negative daylight saving — `Europe/Dublin`, whose
`BaseUtcOffset` is +01:00 under tzdata and 00:00 under the Windows registry — would resolve a skipped
hour to different instants on a developer machine and on the Linux `data-tests` runner. The summary
on `ToUtc` names that assumption rather than leaving the reader to find it.

Neither case throws, and the converter's only explicit branch is `TimeZoneInfo.IsInvalidTime`; the
ambiguous case falls out of the default. Every naive input therefore maps to exactly one instant,
deterministically. The UTC-to-naive direction is total by construction and needs no rule, but it is
not injective: across the autumn fall-back it maps two instants an hour apart onto one naive value,
so a UTC window over the transition becomes a zero-width local window that selects no rows. Both
directions are pinned by test at the transition, the collapse included.

What this does **not** buy is ordering, in either direction of the year. At the autumn fall-back both
passes over the repeated hour carry identical naive values; any fixed offset maps them to the same
instants and the converted sequence repeats an hour. At the spring-forward gap the collision is
sharper: a value inside the gap takes `BaseUtcOffset` while the value just after it takes the
daylight offset, so an ascending naive sequence converts to a **descending** one — for
`Europe/Berlin` on 2026-03-29, local 02:30 lands on 01:30Z while the later local 03:00 lands on
01:00Z. No stateless converter can recover the pass a row belonged to or invent an ordering the
archive did not store, and this converter is stateless by design. The repeated hour is the "duplicate
an hour of history" `data-integration.md:216` accepts as cosmetic; the descent is the same cause read
the other way round and is recorded beside it at `data-integration.md:218-224`. Both are asserted by
test, so each is pinned as behaviour rather than inherited from whichever overload the implementer
reached for.

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
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileProblem.cs` (+ the discriminator enum, its
  own file per the repository's one-type-per-file convention)
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileVersionMismatchError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveUnreachableError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveDatabaseMissingError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveAccessDeniedError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveQueryTimedOutError.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ProviderNotImplementedError.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs`

- [x] one sealed class per file, each deriving from `FluentResults.Error`, with a primary
      constructor carrying the fields from the Technical Details table and the message built in the
      base constructor, following
      `SemiStep/SemiStep.Core/Recipes/Import/Errors/RecipeLoadFailedError.cs:5-9`
- [x] fields are get-only properties assigned from the primary constructor parameters
- [x] `ConnectionFileInvalidError` carries a discriminator enum beside its path and reason, with the
      values `Unreadable`, `Unparseable`, `MissingField`, `OutOfRange` and `UnknownTimeZone`, so the
      loader states that share this type stay tellable apart by a structural assertion
- [x] `ProviderNotImplementedError` carries the member name and its summary states that it is
      removed when the last member is implemented
- [x] no error type is added for an empty or missing `semiplot_tags`. Not because the answer is
      settled — two documents disagree, as Context records — but because this slice reads no
      catalogue and must not pre-empt the slice that does. An empty query window stays a successful
      `Result` per `docs/architecture/data-integration.md:276`, which nothing contradicts
- [x] `ArchiveUnreachableError`, `ArchiveDatabaseMissingError`, `ArchiveAccessDeniedError` and
      `ArchiveNotInitialisedError` are four distinct types, one per operator remedy — check the
      network, run `semibase create`, fix the credentials or the grants, start the SCADA once. Each
      carries host, port and database so the operator knows which archive answered, and
      `ArchiveNotInitialisedError` carries the missing table because SQLSTATE `42P01` is
      table-agnostic. No slice can raise them yet; they are the vocabulary
      `postgres-catalog-and-extent` maps its first SQLSTATEs onto
- [x] `ArchiveQueryTimedOutError`'s summary records that SQLSTATE `57014` also answers a client-issued
      cancel, so the slice that maps it checks its own cancellation token first rather than reporting
      a user's pan as an exceeded bound
- [x] write tests: each type's fields survive construction, asserted by type and structured field;
      the message is checked only for containing those field values, never against exact wording
- [x] run tests — must pass before Task 2

### Task 2: The provider project and its registration

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot.slnx`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] project referencing `SemiPlot.Core` only — never the UI, never the Stub — with `Npgsql`,
      `FluentResults`, `Microsoft.Extensions.DependencyInjection.Abstractions` and `System.Reactive`,
      mirroring `SemiPlot/SemiPlot.DataSource.Stub/SemiPlot.DataSource.Stub.csproj`; declare neither
      `TargetFramework` nor `IsPackable`
- [x] `PostgresDataProvider` takes no constructor dependencies and implements all four members; the
      three `Result`-returning methods return a failed `Result` carrying `ProviderNotImplementedError`
      and `Subscribe` returns an observable that completes immediately
- [x] the DI extension is `AddPostgresData`, distinct from the stub's `AddData`
      (`SemiPlot/SemiPlot.DataSource.Stub/DataServiceCollectionExtensions.cs:11`) because both may be
      referenced from the same composition root in a later slice. It registers `IScheduler` **and**
      `IDataProvider` as singletons and returns the collection, mirroring the stub at `:13-14`, so
      either extension on its own yields a working data layer and a composition root that picks one
      is never left without a scheduler
- [x] register the project in `SemiPlot.slnx`, reference it from `SemiPlot.Tests.Data`, and add
      `Microsoft.Extensions.DependencyInjection` to that test project — it currently has only the
      abstractions, and `BuildServiceProvider()` lives in the implementation package
- [x] write tests: the extension registers `IDataProvider`, resolving it yields
      `PostgresDataProvider`, and the registration is a singleton
- [x] write a test that `IScheduler` resolves from the same container after `AddPostgresData` — the
      provider test alone would not catch a missing scheduler registration
- [x] write tests: each of the three `Result`-returning members returns a failed `Result` carrying
      `ProviderNotImplementedError` — not a throw, not an empty success
- [x] run tests — must pass before Task 3

### Task 3: Connection settings and their loader

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionDto.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresConnectionLoaderTests.cs`

- [x] add `YamlDotNet` 18.1.0 to central package management, matching the version the sibling
      SemiStep repository pins, and reference it from the provider project
- [x] `PostgresConnectionSettings` record with the fields listed under Technical Details, holding the
      source zone as a resolved `TimeZoneInfo` rather than an identifier string, exposing the
      connection string built through `NpgsqlConnectionStringBuilder` carrying no `Options` key and an
      explicit `CommandTimeout` of 0, and overriding `ToString` so the password is never printed
- [x] `PostgresConnectionDto` whose member names map onto the file's underscored keys through the
      deserializer's naming convention, mapped into the record by the loader
- [x] the loader returns `Result<PostgresConnectionSettings>` and never throws, for any input
      including a blank path, and maps its failing states onto the Task 1 types: an absent file and a
      blank path to `ConnectionFileNotFoundError`, a version mismatch to
      `ConnectionFileVersionMismatchError`, and a path that cannot be opened, unparseable YAML, a
      missing or blank required field, a value outside its range and an unknown time-zone identifier
      to `ConnectionFileInvalidError` with the discriminator `Unreadable`, `Unparseable`,
      `MissingField`, `OutOfRange` and `UnknownTimeZone` respectively
- [x] existence is probed by opening the file rather than by `File.Exists`, which answers false for a
      path that is there and cannot be reached, and would send the operator after a missing file
- [x] no operator-visible `Reason` repeats a parser or file-system message: those embed the offending
      scalar, and the password is a scalar. The exception rides `CausedBy`, where only the log reads
      it
- [x] every absent field is named in one error rather than one per run, and `port` and
      `poll_interval_ms` are range-checked, not only presence-checked
- [x] the loader resolves the configured zone with `TimeZoneInfo` so an unknown identifier is a
      `ConnectionFileInvalidError` at load time, carrying the path the converter does not have, and
      the resolved zone reaches the settings record
- [x] write tests against real files in a temp directory: a valid file populates every field; the
      failing states are asserted by error type and structured field, the `ConnectionFileInvalidError`
      cases separated by their discriminator value; a failed result carries its causing exception
- [x] write a test that a password containing `;` and `'` round-trips through the builder and parses
      back to the same value, one that the connection string carries no `Options` key, no
      `statement_timeout` in any form and `Command Timeout=0` — asserted on the emitted string, since
      the builder answers its own default for a key the string never carried — and one that
      formatting the settings never prints the password
- [x] run tests — must pass before Task 4

### Task 4: The time boundary converter

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveTimeConverterTests.cs`

- [x] the converter is constructed from the `TimeZoneInfo` the loader already resolved, so it needs
      no factory and reports no error of its own — which holds only because both directions are
      total, per the "neither direction throws" requirement below
- [x] naive local from the archive to `DateTime` with `Kind = Utc`; UTC window bound to the naive
      local value a query parameter needs
- [x] **neither direction throws.** A bare `TimeZoneInfo.ConvertTimeToUtc` throws `ArgumentException`
      on a skipped local time, so the naive-to-UTC path branches on `TimeZoneInfo.IsInvalidTime` and
      applies `TimeZoneInfo.BaseUtcOffset` there; the ambiguous case needs no branch, since standard
      time is what `TimeZoneInfo` already resolves it to. The summary names `BaseUtcOffset` and the
      assumption it rests on — that daylight saving is positive, which a zone such as `Europe/Dublin`
      breaks differently on Windows and on Linux
- [x] write tests: a known instant round-trips both directions and `Kind` is correct on the way out,
      and each direction ignores the `Kind` of its input
- [x] write a test that an ambiguous local time resolves to the standard-time instant, and one that a
      skipped local time resolves to a stated value rather than throwing
- [x] write a test that a naive sequence spanning the autumn fall-back converts to a sequence that
      **repeats an hour** — that is the behaviour, not a defect: the archive stores naive wall-clock
      time, both passes over the repeated hour carry identical values, and
      `docs/architecture/data-integration.md:216` accepts the duplicate as cosmetic
- [x] write a test that an **ascending** naive sequence spanning the spring-forward gap converts to a
      **descending** one, and one that a UTC window over the autumn fall-back converts to a
      zero-width local window. Both are unavoidable for any stateless offset choice, so they are
      pinned as behaviour and named in `data-integration.md:218-224`; the slice that assembles
      envelopes owns what to do about them
- [x] choose a zone and instant whose transition is identical under Windows registry data and Linux
      tzdata: `SemiPlot.Tests.Data` targets plain `net10.0` and developers run it on Windows while the
      `data-tests` job (`.github/workflows/ci.yml`) runs it only on `ubuntu-latest`, so a historical
      date or an unusual zone becomes a defect that appears on one platform and not the other
- [x] run tests — must pass before Task 5

### Task 5: Verify acceptance criteria

- [x] every check in Acceptance Evidence runs and produces its stated result
- [x] `dotnet test SemiPlot.slnx` — zero failures across both test projects, and `SemiPlot.Tests`
      reports the same passing count as at the branch point
- [x] `git diff --name-only master...HEAD` lists no file under `SemiPlot/SemiPlot.UI/`,
      `SemiPlot/SemiPlot.DataSource.Stub/` or `SemiPlot/SemiPlot.Tests/`
- [x] no test added by this slice carries `[Trait("Category","Integration")]` and none needs a
      container: everything here is pure logic
- [x] every new test class carries all three traits and uses raw xunit `Assert.`, per `CLAUDE.md`
- [x] `dotnet format SemiPlot.slnx` reports no changes

### Task 6: Update documentation

- [x] record the two-plane error contract in `docs/architecture/data-integration.md` beside the
      error-semantics table: the public set, the rule as worded in Solution Overview, and that
      internal failures cross only mapped with the original on `CausedBy`
- [x] add two rows to the error-semantics table in `docs/architecture/data-integration.md`, between
      the query-timeout row (`:271`) and the `trends`-missing row (`:274`): the database not existing
      — SQLSTATE `3D000`, failed `Result`, operator remedy "run `semibase create`" — and the
      credentials being refused — SQLSTATE `28P01`, `28000` or `42501`, failed `Result`, operator
      remedy the user, password or grants. Without them `ArchiveDatabaseMissingError` and
      `ArchiveAccessDeniedError` ship with no row behind them
- [x] record in the `Configuration` section the nine YAML keys as a copyable example, and in the
      `Time boundary` section the two ordering consequences of naive storage — the repeated hour at
      the fall-back and the descending sequence at the spring-forward gap
- [x] add one line to `CLAUDE.md`'s test section: tests over provider errors assert by error type and
      structured field, never on exact message wording. That is a convention an agent needs before it
      writes a test, so it belongs where agents read first. The two-plane contract itself does
      **not** go there — `data-integration.md` carries it, and `CLAUDE.md`'s own footer says not to
      add specifics
- [x] extend the `References` cell for `SemiPlot.Tests.Data` in `CLAUDE.md`'s test-project table with
      `SemiPlot.DataSource.Postgres`; Task 2 adds that reference and the cell goes stale without it
- [x] amend the roadmap entry for `postgres-realtime-poll` to name it the owner of
      `ProviderNotImplementedError`'s deletion
- [x] leave the empty-catalogue conflict between the roadmap and `data-integration.md` unresolved and
      recorded on **both** sides — `postgres-catalog-and-extent` owns it, and amending either
      document towards an answer here would pre-empt the slice that has to live with it
- [x] correct `readme.md`'s architecture bullet, which named a `SimpleScadaDataProvider` this slice
      supersedes with `PostgresDataProvider`
- [x] move this plan to `docs/plans/completed/` — not done here: archiving the plan belongs to the
      delivery step, which runs after the branch is tested. Nothing is moved by this task

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**The strictly-ascending envelope contract needs an owner, and three separate hazards break it.**
`data-integration.md:57` requires `PenHistoryEnvelope` timestamps to be strictly ascending. Naive
archive data cannot satisfy that across either transition:

| Where | What the converter produces | Why no converter fixes it |
| --- | --- | --- |
| Autumn fall-back | duplicate timestamps — the repeated hour arrives as identical naive values | no stateless conversion separates the two passes |
| Spring-forward gap | **descending** timestamps — local 02:30 lands on 01:30Z, the later local 03:00 on 01:00Z | any fixed offset for the gap collides with the offset outside it |
| A UTC window over the fall-back | a zero-width local window, so the query selects no rows and reports no error | two instants an hour apart share one naive reading |

Whichever slice assembles envelopes owns the first two — drop, merge, nudge, or amend the contract —
and whichever slice builds history windows owns the third. All three are pinned by test in
`ArchiveTimeConverterTests`, so they are named here rather than discovered in a failing assertion.

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

**The query time bound is read back, not sent, and `postgres-catalog-and-extent` owns the mechanism.**
This slice pins only the negative half: the connection string sends no `statement_timeout` and pins
`Command Timeout=0`. The slice that opens the first connection adds the rest.

| Piece | What it does |
| --- | --- |
| `NpgsqlDataSourceBuilder.UsePhysicalConnectionInitializer` | runs `SELECT setting FROM pg_settings WHERE name = 'statement_timeout'` once per physical connection and caches the parsed value, which is what fills `ArchiveQueryTimedOutError.Timeout`. That one command sets `CommandTimeout = 10` s of its own |
| Per-command backstop on the read commands | `NpgsqlCommand.CommandTimeout = effective + 30 s`, a dead-connection guard rather than a query bound |
| An effective bound of `0` | the server has no bound, so the instance is not provisioned per contract: the read commands take a fixed 5 minute backstop plus a log line, not a failure |
| TCP keepalive for idle pooled connections | that slice's decision as well |

`pg_settings.setting` is a `text` column carrying the value in the parameter's own base unit, which
for `statement_timeout` is milliseconds — `pg_settings.unit` is `ms`, fixed by the parameter's
definition rather than by the server's configuration, so the read needs no unit branch. The reader
role's 30 s bound comes back as the plain string `30000`, and an unbounded server as `0`. Parse it
with `int.Parse(value, CultureInfo.InvariantCulture)` and wrap it in `TimeSpan.FromMilliseconds`.
`SHOW statement_timeout` and `current_setting('statement_timeout')` are the wrong shape here: both
answer a unit-suffixed display string — `30s` for that same role, which
`SeededArchiveTests.TheReaderCarriesTheProductionTimeouts` asserts verbatim — that no integer parse
accepts.

`Command Timeout=0` leaves every command unbounded on the client by default, the read-back
included, and the `effective + 30 s` rule governs the read commands only — it cannot bound the one
command that *produces* `effective`. So the initializer's own query carries `CommandTimeout = 10`
seconds. It is a single-row lookup against `pg_settings` over a connection that has already
authenticated; a server that cannot answer it in ten seconds is not healthy, and the bounded failure
maps to `ArchiveUnreachableError` exactly as a dead connection does. Without that bound a server
which completes authentication and then stops answering hangs the physical-connection initializer
with no failure to map.

When the read-back comes back as `0` there is no effective bound to add 30 s to, so the read
commands take a fixed backstop of 5 minutes alongside the log line naming the unprovisioned
instance.

The backstop is a dead-connection guard because the server never legitimately stays silent past its
own bound during a statement, so its `TimeoutException` means the server is not answering. That fixes
the mapping and stops the two timeout sources being confusable: `PostgresException` `57014` with
SemiPlot's own cancellation token not triggered maps to `ArchiveQueryTimedOutError`, while
`NpgsqlException` with an inner `TimeoutException` maps to `ArchiveUnreachableError`.

**`ProviderNotImplementedError` is deleted by `postgres-realtime-poll`**, which implements
`Subscribe`, the last of the four members to be filled in.

**Remaining slices**

After this slice the roadmap continues with: postgres-catalog-and-extent, postgres-history-read,
postgres-bucketed-read, postgres-gap-reconstruction, postgres-realtime-poll,
postgres-startup-and-composition, live-demo-and-stub-retirement.

**Executed by exec:**

- branch: postgres-provider-scaffold

## Verify it yourself

This slice adds a project and a vocabulary; it changes nothing an operator can see, because the
composition root still selects the stub. So there is no click-path to check, and every claim below is
a command.

1. **The application is untouched.**
   `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` reports 256 passed, identical to
   `master`, and `git diff --name-only master...HEAD` lists no file under `SemiPlot/SemiPlot.UI/`,
   `SemiPlot/SemiPlot.DataSource.Stub/` or `SemiPlot/SemiPlot.Tests/`. That pair is the whole safety
   argument for this slice: nothing that runs today went near it.

2. **The scaffold fails loudly rather than quietly.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresComposition"` — 9 pass. The three
   `Result`-returning members each return a failed `Result` carrying `ProviderNotImplementedError`
   with its member name. An empty success here would render a blank chart in a later slice and look
   like missing data rather than missing code.

3. **The loader is total, and its failures are told apart by a field.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresConnectionLoader"` — 20 pass. Eight
   failing states resolve onto three error types, the five `ConnectionFileInvalidError` cases separated
   by the `Kind` discriminator rather than by message text. A password containing `;` and `'`
   round-trips through `NpgsqlConnectionStringBuilder`.

4. **SemiPlot sends no query time bound.**
   The same run covers `TheConnectionStringSendsNoStatementTimeoutAndPinsAnInfiniteCommandTimeout`,
   which asserts on the emitted string as well as the parsed builder — Npgsql answers its own default
   for an unset key, so the parsed value alone would prove neither absence nor presence. Restore an
   `Options=-c statement_timeout=` assignment and it fails; drop `CommandTimeout = 0` and it fails.

5. **The converter never throws, in either direction.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ArchiveTimeConverter"` — 16 pass. Delete the
   `TimeZoneInfo.IsInvalidTime` branch in `ArchiveTimeConverter.ToUtc` and two tests fail with
   `ArgumentException`, because the same test asserts that a bare `TimeZoneInfo.ConvertTimeToUtc`
   does throw on the skipped hour. The daylight-saving consequences are asserted rather than assumed:
   the autumn fall-back repeats an hour, the spring gap yields a descending pair at its boundary, and
   a one-hour UTC window across the fall-back collapses to zero width.

6. **The whole suite and the format gate.**
   `dotnet test SemiPlot.slnx` — 256 and 263 passed, 24 skipped. The 24 are the bench's
   container-gated integration tests and skip with a stated reason when no runtime answers.
   `dotnet format SemiPlot.slnx --verify-no-changes` exits 0. Note that the format gate does not check
   the UTF-8 BOM `.editorconfig` requires, so a scripted edit can drift encoding past it:
   `head -c 3 <file> | od -An -tx1` must show `ef bb bf`.
