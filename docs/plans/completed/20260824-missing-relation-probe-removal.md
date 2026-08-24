# Remove the missing-relation probe and the model it feeds

## Overview

A `42P01` from the archive is answered today by a second network round trip.
`MissingRelationProbe` (`SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs:17-81`)
opens a fresh connection to the server that has just failed and runs a `to_regclass` lookup over
both relations, so the provider can tell the operator which one is absent.

**That round trip exists for one reason, and the reason is gone.** Its own comment states it
(`MissingRelationProbe.cs:68-70`): the two relations had *different remedies* — `semiplot_tags`
was SemiBase's and `trends` was the SCADA's, so naming the wrong one sent the operator to start a
SCADA against a database that did not carry SemiBase's object yet. SemiBase v0.3.0 creates
`public.trends` in both `semibase site` and `semibase bench`, which
`docs/architecture/postgres-instance.md:96-97` already records. Both tables now arrive in one
provisioning run, both are absent for exactly one reason, and both are fixed by exactly one
command. There is nothing left for the probe to tell apart.

The model built on that distinction goes with it:

- `ArchiveNotInitialisedError`'s summary (`ArchiveNotInitialisedError.cs:8,13,17,21`) says the
  remedy follows the absent object, documents `trends` as "the table is the SCADA's: it has never
  run against this database" and points twice at `semibase create`, a command v0.3.0 removed.
- `StartupFailureMapper.DescribeMissingObjectRemedy`
  (`SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs:121-139`) branches on the table name to
  send the operator to two different places, and names `semibase create` at `:125` and `:135`.
- `docs/architecture/postgres-topology.md:132-133,140` models `NoTrends` and `NoTags` as
  consecutive states with a transition between them, which no sequence of events can now produce.

The reader currently tells an operator standing at a machine that will not start to run a command
that does not exist, or to start a SCADA that will not fix anything. That is the defect; the probe
removal is what falls out of fixing it.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice missing-relation-probe-removal

**Files that carry the probe:**

- `SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs` — the whole type, 81 lines
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:111-118` — `RelationProbe` and its
  summary, no other caller
- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:22-23,31,35,40,47,54,300-311` —
  the class summary's probe sentence, the field, the constructor comment counting three internal
  parameters, the constructor parameter, its null guard, the assignment, the `MapAsync` comment and
  the probe call inside `MapAsync`
- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:25,29-31,36` —
  the singleton registration, the comment counting three internal parameters, and the injection
  into the provider
- `SemiPlot/SemiPlot.DataSource.Postgres/StatementTimeoutReader.cs:14` — a `<see cref>`
  cross-reference in its own summary, which must survive as prose
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:39-44,53,75-80,82,97,116` — the
  `missingRelation` parameter, whose name says a probe resolved it

**Files that carry the model:**

- `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs:5-24` — the summary's
  three-row table and its "the remedy follows the absent object" claim
- `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs:119-139` — the comment and the
  per-table branch
- `docs/architecture/postgres-instance.md:96-102` and
  `docs/architecture/data-integration.md:468-471` — both already name this slice as what corrects
  them
- `docs/architecture/data-integration.md:515-516` — "the remedy follows the table — `trends` is the
  SCADA's, `semiplot_tags` is SemiBase's", the ownership split itself
- `docs/architecture/data-integration.md:541` — "the missing-relation probe", lowercase and
  unbackticked
- `docs/architecture/data-integration.md:674` and `docs/architecture/testing-strategy.md:52` —
  both name `RelationProbe` as a cold-path diagnostic carrying no pinned literal
- `docs/architecture/postgres-topology.md:126-161` — the state machine and the note under it
- `CLAUDE.md:228-233` — the cold-path-reader rule, with `MissingRelationProbe` as one of its two
  canonical examples
- ➕ `docs/architecture/scada-archive.md` — the survey missed it. Its ownership sentences and its
  ownership diagram edge outlived the rewrite and the review round corrected them.

**Tests that pin either:**

- `SemiPlot/SemiPlot.Tests.Data/Postgres/MissingRelationProbeTests.cs` — the whole file, three
  `[InlineData]` rows and one `[Fact]` over `Resolve`, four cases in total
- `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs:34,93-99,139` — a
  construction argument, `AddPostgresDataResolvesTheMissingRelationProbe`, and an `[InlineData]` in
  the singleton-lifetime sweep
- `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveExceptionMapperTests.cs:193,197` — the helper's own
  `missingRelation` parameter, passed positionally
- `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs:14` — names `RelationProbe`
  as a cold-path statement carrying no literal
- `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresExtentReadTests.cs:83-107` —
  `ADroppedCatalogueFailsNamingSemiplotTagsNotTheFallback`, the one gated test the probe is what
  satisfies
- `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs:96,103,107,113-114,118,124` —
  three test names encoding the old remedy and three assertions on `semibase create`

**Dependency identified:** `PostgresDataProvider.MapAsync` (`:305-332`) takes a
`fallbackRelation` from each of its three call sites — `:88` passes `TagCatalogRelation`, `:160`
and `:187` pass `TrendsRelation` — and today that value is used only when the probe returns null.
Removing the probe promotes the fallback from a backstop to the answer, which is the whole
mechanism of this change: each read already knows which relations its own statement touches, and
`ArchiveExceptionMapper` stays synchronous and pure.

## Development Approach

- **testing approach**: Regular (code first, then tests) — this is a deletion against tests that
  already exist, so the tests are edited to match the narrowed surface rather than written ahead
  of it.
- complete each task fully before moving to the next
- make small, focused changes
- **every task includes new/updated tests** for the code it changes
- **all tests pass before starting the next task** — no exceptions
- **update this plan file when scope changes during implementation**
- run tests after each change

## Testing Strategy

- **unit tests**: required for every task. The mapper, the error type and the startup mapper are
  all pure, so every behavioural claim in this plan is a plain `[Fact]` or `[Theory]`.
- **gated tests**: the `42P01` read path is covered by four gated integration tests, one per read
  plus the extent read's second relation. They are the only proof that the relation a read reports
  is the one its statement touches.
- **e2e tests**: none — this project has no UI e2e suite. The startup failure text is covered by
  `SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs` under plain facts.
- **the two suites split by dependency graph**: `MissingRelationProbeTests`,
  `PostgresCompositionTests`, `ArchiveExceptionMapperTests`, `ArchiveStatementTextTests` and the
  gated integration tests live in `SemiPlot.Tests.Data`; `StartupFailureMapperTests` lives in
  `SemiPlot.Tests`. Both are touched, and neither may gain a reference to the other.

## Acceptance Evidence

Every item below is a command with the result it must produce. Both greps cover source, the
architecture documents and `CLAUDE.md`. They exclude `docs/plans/` entirely: completed plans and
the roadmap record shipped history and are not rewritten, and this plan file names the strings it
is about.

1. **The removed command is named nowhere the operator can reach.** Today:

   ```powershell
   git grep -n "semibase create" -- SemiPlot/ docs/architecture/ CLAUDE.md
   ```

   returns **nine hits across four files**: `ArchiveNotInitialisedError.cs:13,21`,
   `MissingRelationProbe.cs:69`, `StartupFailureMapperTests.cs:103,114,124`,
   `StartupFailureMapper.cs:120,125,135`. After the change the same command returns **nothing**.

2. **The probe is gone with everything that served only it.**

   ```powershell
   git grep -n "MissingRelationProbe\|RelationProbe\|missingRelation" -- SemiPlot/ docs/architecture/ CLAUDE.md
   ```

   returns **39 hits across 14 files** today and **nothing** after.

3. **Every gated `42P01` test still names a relation, and each names its own statement's.**

   ```powershell
   $env:SEMIPLOT_REQUIRE_DB=1
   dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj --filter "FullyQualifiedName~ADropped"
   ```

   selects **four tests** before and after the change:
   `PostgresCatalogReadTests.ADroppedCatalogueFailsNamingSemiplotTags` (`:129`),
   `PostgresExtentReadTests.ADroppedTrendsTableFailsNamingTrends` (`:110`),
   `PostgresHistoryReadTests.ADroppedTrendsTableFailsNamingTrends` (`:300`), and the extent read's
   dropped-catalogue case (`PostgresExtentReadTests.cs:86`), which task 1 inverts and renames.
   All four must report **4 passed, 0 skipped**.

4. **Both suites green.**

   ```powershell
   dotnet build SemiPlot.slnx -c Release
   dotnet format SemiPlot.slnx --verify-no-changes
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj
   $env:SEMIPLOT_REQUIRE_DB=1; dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj
   ```

   0 warnings, 0 errors, exit 0, and both suites pass with **0 skipped**. Baseline measured
   2026-08-24 at `31a784b`: `SemiPlot.Tests` 370 passed, `SemiPlot.Tests.Data` 369 passed. The
   expected end state:

   | Suite | Baseline | Change | Expected |
   | --- | --- | --- | --- |
   | `SemiPlot.Tests.Data` | 369 | −4 `MissingRelationProbeTests` cases, −1 `AddPostgresDataResolvesTheMissingRelationProbe`, −1 lifetime `[InlineData]` | **363** |
   | `SemiPlot.Tests` | 370 | +1 fact pinning that no remedy sends the operator to the SCADA | **371** |

   A count that moves any other way is unexplained and blocks.

   The review round changed the `SemiPlot.Tests.Data` figure once more: `73b7848` deleted the
   three-row `AnUndefinedTableWithNoResolvedRelationIsACallerDefect` theory together with the
   `RequireRelation` guard it pinned, so the suite ends at **360** rather than 363. `SemiPlot.Tests`
   stays at **371** — the added fact was replaced, not removed.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- keep this plan in sync with the work actually done

## Solution Overview

**One statement, one relation, no probe.** Each read already knows which relations its own
statement touches, and `PostgresDataProvider` already passes that knowledge into `MapAsync` as
`fallbackRelation`. The change promotes it from a backstop to the answer and deletes the round
trip that second-guessed it. The three call sites keep the values they pass today
(`:88` `TagCatalogRelation`, `:160` and `:187` `TrendsRelation`) — those are already correct on
every path the application takes, which is why the probe's own null path already relied on them.

**The extent read names `trends` for either absent relation, and that is accepted.**
`QueryArchiveExtentAsync` (`PostgresDataProvider.cs:172-191`) issues the one statement that touches
both relations, so its `42P01` is ambiguous in principle. At startup it never misreports:
`StartupProbe` reads the catalogue first (`StartupProbe.cs:114`) and the extent second (`:122`), so
a missing `semiplot_tags` fails the catalogue read and never reaches the extent read. The minimap
re-queries the extent at run time, where that ordering does not hold, so a `semiplot_tags` dropped
under a live session is reported as a missing `trends`. That wrong name reaches a log line and
nothing further: `ArchiveNotInitialisedError.Table` is consumed only by `StartupFailureMapper`, and
the minimap logs a warning on a failed extent and opens no error window
(`MinimapViewModel.cs:112-119`). Under the new model both tables carry the same remedy, so even the
log line sends the operator to the right command.

**The remedy collapses to one branch because the ownership collapsed to one owner.**
`DescribeMissingObjectRemedy` currently answers three ways: `semibase create` for a missing
database, "run the SCADA" for `trends`, `semibase create` for `semiplot_tags`. Under v0.3.0 all
three become one instruction — run `semibase site` against this server — differing only in
whether the detail names a database or a table. One owner carries one remedy, so the remedy text
carries no table name.

`ArchiveNotInitialisedError` keeps its shape: seven error types, `MissingObject` and `Table` both
survive, because `Table` still names which object is absent in the detail line and consumers still
route on `MissingObject`. Only the summary's claims about *ownership* change.

**Scope guard.** The seven public error types in `SemiPlot.Core/Data/Errors/`, the `ArchiveObject`
enum, `ArchiveQueryTimedOutError` and `StatementTimeoutReader` are unchanged by this slice;
`StatementTimeoutReader.cs` differs only in the summary prose that named a deleted type, and
`PostgresDataProvider` keeps its explicit constructor. Run `git diff master...HEAD --stat` from the
feature branch to read the whole surface the slice touched — run from `master` it compares nothing.

## Technical Details

**`ArchiveExceptionMapper` keeps its shape.** `Map` reads
`Map(Exception exception, string? relation, TimeSpan? effectiveBound)`: three parameters, the
relation nullable, and only its name changes. Nullable is what the file needs — the `3D000` arm,
the single arm covering the three credential states, the `57014` arm and the fall-through all pass
no relation, as do eleven of the fifteen `Map(...)` call sites in `ArchiveExceptionMapperTests.cs`
(`:35,46,54,79,100,110,122,135,147,155,167`).

The parameter rename and the `<param>` prose (`:39-44`) state that the relation is the one the
calling statement touches rather than the one a probe resolved. Two named arguments in
`PostgresDataProvider` carry the old name — `PostgresDataProvider.cs:318` and `:321` — and change
with it, or the build fails with CS1739.

**Corrected by review:** the plan asked to keep `RequireRelation` (`:75-80`) on the grounds that
another `SemiPlot.DataSource.*` provider could construct `ArchiveNotInitialisedError` through this
mapper. That justification is false — `ArchiveExceptionMapper` is `internal sealed`, its
`InternalsVisibleTo` names only `SemiPlot.Tests.Data`, and the only production caller is
`PostgresDataProvider.MapAsync`, whose three call sites pass compile-time constants. The
non-blank guarantee the guard duplicated already lives in `ArchiveNotInitialisedError`'s base
initialiser (`ArgumentException.ThrowIfNullOrWhiteSpace(table)`), which is the public type's own
contract. `73b7848` removed the guard and the three-row theory that pinned it.

**`ArchiveStatements` loses `RelationProbe`** (`:111-118`). The plan asked
`ArchiveStatementTextTests.cs:14`'s header to name the class of statements rather than the members;
`73b7848` restored the member name, because with one cold-path statement left the unnamed form
told the reader nothing about which statement is exempt. The header now names
`EffectiveStatementTimeout` and states why it carries no literal.

**Processing flow after the change**, `42P01` only:

```
read fails 42P01
  → MapAsync(exception, relation)          // relation supplied by the calling read
  → _exceptionMapper.Map(exception, relation, effectiveBound: null)
  → ArchiveNotInitialisedError(host, port, database, ArchiveObject.Table, relation)
  → StartupFailureMapper → "Run 'semibase site' against this server…"
```

No connection is opened on the error path for `42P01`. The `42P01` arm in `MapAsync` stays: it
passes the relation and the fall-through passes null, so the two produce different errors.

The `57014` path keeps `StatementTimeoutReader` exactly as it is — it meets `CLAUDE.md`'s bar,
because the two arms of the timeout mapping carry different operator remedies.

**`MapAsync` keeps its `async` modifier and its `Task<Error>` return.** It still awaits
`StatementTimeoutReader` on `57014`, which this slice leaves exactly as it is.

## What Goes Where

- **Implementation Steps**: every code, test and documentation change in this repository.
- **Post-Completion**: nothing requires an external system. The one item worth stating is the
  remaining roadmap slice.

## Implementation Steps

### Task 1: Remove the probe and let each read answer for its own statement

**Files:**
- Delete: `SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Data/Postgres/MissingRelationProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/StatementTimeoutReader.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresExtentReadTests.cs`

- [x] delete `MissingRelationProbe.cs` and `ArchiveStatements.RelationProbe` (`:111-118`)
- [x] in `PostgresDataProvider`, drop the field (`:31`), the constructor parameter (`:40`), its
      guard (`:47`), the assignment (`:54`) and the probe call (`:309`); rename `MapAsync`'s
      `fallbackRelation` to `relation` and pass it straight to the mapper
- [x] correct the constructor comment (`:35`) — two of its parameters are internal types, not three
- [x] rewrite the `MapAsync` comment (`:300-304`) and the class summary's probe sentence
      (`:22-23`) to state that each read names the relations its own statement touches
- [x] keep the `QueryArchiveExtentAsync` catch comment (`:184-186`) and restate its reason: the
      startup-ordering argument survives, the ownership argument does not
- [x] in `ArchiveExceptionMapper`, rename every `missingRelation` occurrence to `relation` — all
      ten, at `:39,53,62,75,77,79,82,86,97,116`, including `RequireRelation`'s own parameter — and
      rewrite the `<param>` prose (`:39-44`) to say the relation is the one the calling statement
      touches; keep the parameter nullable. `RequireRelation` (`:75-80`) was kept here and removed
      by the review in `73b7848` — see **Corrected by review** under `## Technical Details`
- [x] update the two named arguments in `PostgresDataProvider.cs:318` and `:321` to the new
      parameter name
- [x] drop the singleton registration (`:25`) and the injection (`:36`) from
      `PostgresDataServiceCollectionExtensions`, and correct its comment (`:29-31`) to two internal
      parameters
- [x] replace the `<see cref="MissingRelationProbe"/>` in `StatementTimeoutReader.cs:14` with
      prose that states the rule without naming a deleted type
- [x] in `PostgresCompositionTests`, drop the construction argument (`:34`),
      `AddPostgresDataResolvesTheMissingRelationProbe` (`:93-99`) and the `[InlineData]` (`:139`)
- [x] restate `ArchiveStatementTextTests.cs:14`'s header so it names the class of cold-path
      statements rather than listing members — reverted by the review in `73b7848`, which put
      `EffectiveStatementTimeout` back in the header
- [x] rename the helper parameter at `ArchiveExceptionMapperTests.cs:193,197` to `relation`; the
      eleven call sites passing no relation are unaffected.
      `AnUndefinedTableWithNoResolvedRelationIsACallerDefect` (`:85-92`) was kept here and deleted
      by the review in `73b7848` with the `RequireRelation` guard it pinned — three rows, which is
      the whole difference between 363 and 360
- [x] invert `PostgresExtentReadTests.ADroppedCatalogueFailsNamingSemiplotTagsNotTheFallback`
      (`:83-107`): rename it `ADroppedCatalogueFailsNamingTheStatementsOwnRelation`, assert
      `error.Table` is `"trends"` (`:105`), and replace its comment (`:83-84`) with one naming the
      accepted anomaly — the extent statement touches both relations and reports its own, so a
      dropped catalogue is reported as a missing `trends`. It is the only coverage of the extent
      read's `42P01` path.
- [x] run tests — must pass before task 2

### Task 2: Correct what the operator is told when the archive is not provisioned

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveNotInitialisedError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`

- [x] rewrite `ArchiveNotInitialisedError`'s summary (`:5-24`): drop the claim that the remedy
      follows the absent object (`:8`) and collapse the three-row table's two table rows into one —
      both tables are SemiBase's and both are absent for one reason, provisioning did not complete
- [x] collapse `DescribeMissingObjectRemedy` (`:121-139`) to the database sentence naming
      `semibase site` and keeping its second clause about correcting the connection file, plus one
      table sentence naming whichever table the error carries. No arm may switch on a table *name* —
      that is the ownership knowledge this slice removes. The plan also asked for a default arm for
      a null or blank `Table`; the review removed it in `73b7848` as unreachable by construction —
      the only route to `ArchiveObject.Table` runs through `ArchiveNotInitialisedError`'s base
      initialiser, which rejects a blank table, so the arm could not be reached and no test could
      cover it
- [x] replace the comment at `:119-120` — the remedy no longer follows the absent object, so state
      what it does follow
- [x] rename the three `StartupFailureMapperTests` methods that encode the old remedy:
      `ArchiveNotInitialised_MissingDatabase_SendsTheOperatorToSemibaseCreate` (`:96`),
      `ArchiveNotInitialised_MissingTrends_SendsTheOperatorToTheScada` (`:107`) and
      `ArchiveNotInitialised_MissingTagTable_SendsTheOperatorToSemibaseCreate` (`:118`) all name
      `semibase site` instead
- [x] update their assertions (`:103,113-114,124`): all three now assert `semibase site`, and the
      `trends` case that asserted `Simple-Scada` and `NotContain("semibase create")` is inverted
- [x] add one `[Fact]` pinning that no remedy string tells the operator to run the SCADA to create
      a table — replaced by the review in `73b7848` with
      `ArchiveNotInitialised_TheRemedyDoesNotDependOnWhichTableIsAbsent`, which elides the table
      name from each remedy and asserts the two strings equal. That pins the property the slice
      claims; the original asserted string absence over three literal instances, and its
      `NotContainEquivalentOf("scada")` matched the `scada-host` fixture value by accident
- [x] run tests — must pass before task 3

### Task 3: Update documentation

**Files:**
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `docs/architecture/postgres-instance.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `CLAUDE.md`
- ➕ Modify: `docs/architecture/scada-archive.md` — found by the review, corrected in `9e6a6f5`


- [x] `docs/architecture/postgres-topology.md:126-161`: collapse `NoTrends` (`:132`) and `NoTags`
      (`:133`) into one state — provisioning did not complete — remove the transition between them
      (`:140`), and rewrite the note at `:158-161` that records the client's lag
- [x] `docs/architecture/postgres-instance.md:96-102`: the lag is closed, so the paragraph naming
      `StartupFailureMapper`, `MissingRelationProbe`, `ArchiveNotInitialisedError` and this slice
      goes, and the surrounding text states the current model
- [x] `docs/architecture/data-integration.md:468-471`: the `trends` row's lag is closed, so the
      pointer at this slice goes and the text states the current model
- [x] `docs/architecture/data-integration.md:515-516`: "the remedy follows the table — `trends` is
      the SCADA's, `semiplot_tags` is SemiBase's" states an ownership split that no longer exists;
      the type still carries the table name, and the reason is now that `42P01` is table-agnostic
      and the name is what the detail line reports
- [x] `docs/architecture/data-integration.md:541`: "the missing-relation probe" names a deleted
      type in lowercase prose; the `57014` bound is now read on a failure path that runs nothing
      else
- [x] `docs/architecture/data-integration.md:674` and `docs/architecture/testing-strategy.md:52`:
      both list `EffectiveStatementTimeout` and `RelationProbe` as the cold-path statements
      carrying no pinned literal; only `EffectiveStatementTimeout` remains
- [x] `CLAUDE.md:228-233`: the cold-path-reader passage keeps its bar and loses the example that no
      longer exists. Target text:

      > - A diagnostic question the exception itself cannot answer is resolved by a cold-path
      >   reader: an internal sealed type beside the provider that opens a fresh connection on the
      >   failure path (`StatementTimeoutReader` for `57014`). It runs from
      >   `PostgresDataProvider.MapAsync`, never from `ArchiveExceptionMapper`, which stays
      >   synchronous, pure and unit-testable. Add one only when a distinct operator remedy depends
      >   on the answer — an extra round trip against a server that has just failed buys nothing
      >   otherwise, and a `42P01` needs none, because each read names the relations its own
      >   statement touches.
- [x] ➕ `docs/architecture/data-integration.md` field triage, step 2: it read "the SCADA project has
      never run against this database", the same removed model in an operator-facing checklist. It
      now states that provisioning did not complete and names `semibase site`.

### Task 4: Verify acceptance criteria

**Files:** none — this task runs commands and records their output.

- [x] run every command in `## Acceptance Evidence` and record its actual output against the result
      that section states
- [x] state both suite counts against the expected 363 / 371, explaining any difference
- [x] deferred to the delivery step — exec never moves the plan. Archiving to
      `docs/plans/completed/` runs after the operator has tested the branch.

**Measured 2026-08-24 at `d9156c5`, branch `missing-relation-probe-removal`, Docker 29.7.2.**

| Evidence | Stated result | Actual |
| --- | --- | --- |
| 1. `git grep -n "semibase create" -- SemiPlot/ docs/architecture/ CLAUDE.md` | nothing | no output, exit 1. Same grep against `master` returns 9 hits, matching the stated baseline |
| 2. `git grep -n "MissingRelationProbe\|RelationProbe\|missingRelation" -- …` | nothing | no output, exit 1. Against `master`: 39 hits across 14 files, matching the stated baseline |
| 3. `dotnet test …Tests.Data… --filter "FullyQualifiedName~ADropped"` with `SEMIPLOT_REQUIRE_DB=1` | 4 passed, 0 skipped | **4 passed, 0 failed, 0 skipped, total 4**, exit 0 |
| 4a. `dotnet build SemiPlot.slnx -c Release` | 0 warnings, 0 errors | 0 warnings, 0 errors, exit 0 |
| 4b. `dotnet format SemiPlot.slnx --verify-no-changes` | exit 0 | exit 0, no output |
| 4c. `dotnet test …SemiPlot.Tests…` | 371 passed, 0 skipped | **371 passed, 0 failed, 0 skipped**, exit 0 |
| 4d. `SEMIPLOT_REQUIRE_DB=1; dotnet test …SemiPlot.Tests.Data…` | 363 passed, 0 skipped | **363 passed, 0 failed, 0 skipped**, exit 0 |

Both suite counts land exactly on the expected 363 / 371 at `d9156c5`. Nothing to explain there.

**`SemiPlot.Tests.Data` stands at 360 at HEAD, not 363.** The review commit `73b7848` deleted the
three-row `AnUndefinedTableWithNoResolvedRelationIsACallerDefect` theory together with the
`RequireRelation` guard it pinned: 363 − 3 = 360. That single deletion is the whole difference.
`SemiPlot.Tests` is 371 at both commits — the review replaced the added fact, it did not remove it.
The table above is the measurement at `d9156c5` and is kept as measured; the HEAD figures are in the
exec run record at the end of this file.

The `~ADropped` filter selects exactly four tests, confirmed twice: the run reports `всего 4`, and
`grep -rn "ADropped" SemiPlot/SemiPlot.Tests.Data/ --include=*.cs` returns four declarations —
`PostgresCatalogReadTests.ADroppedCatalogueFailsNamingSemiplotTags` (`:129`),
`PostgresExtentReadTests.ADroppedCatalogueFailsNamingTheStatementsOwnRelation` (`:87`),
`PostgresExtentReadTests.ADroppedTrendsTableFailsNamingTrends` (`:111`) and
`PostgresHistoryReadTests.ADroppedTrendsTableFailsNamingTrends` (`:300`). The evidence section
predicted `:86` and `:110` for the two extent-read cases; both sit one line lower after the edit.
Names and count are as stated.

**Scope guard checked.** `git diff master...HEAD -- …/StatementTimeoutReader.cs` is a single hunk of
seven lines inside the `<para>` of the class summary: the clause "for the same reason
`<see cref="MissingRelationProbe"/>` does" is dropped and the surrounding sentence rewrapped. No
code line, signature or attribute changes. The claim holds.

**Error vocabulary still counts seven types.**
`grep -rn "^public \(sealed \)\?\(class\|record\|enum\)" SemiPlot/SemiPlot.Core/Data/Errors/`
returns nine declarations over nine files: seven error classes — `ArchiveAccessDeniedError`,
`ArchiveNotInitialisedError`, `ArchiveQueryTimedOutError`, `ArchiveReadFailedError`,
`ArchiveUnreachableError`, `ConnectionFileInvalidError`, `ConnectionFileNotFoundError` — plus the
two supporting enums `ArchiveObject` and `ConnectionFileProblem`, which the scope guard names
separately. `ArchiveNotInitialisedError.cs` is the one file of the seven the diff touches, and at
`d9156c5` its 18 changed lines were entirely inside the `<summary>` block that task 2 rewrote. At
HEAD `git diff master...HEAD -- SemiPlot/SemiPlot.Core/Data/Errors/` reports 22 changed lines in
that one file: the same `<summary>` block plus the two-line comment above
`ArgumentException.ThrowIfNullOrWhiteSpace(table)` in `Describe`, which `73b7848` restated once the
guard it justified moved out of `ArchiveExceptionMapper`. The constructor, the properties and the
base initialiser's executable lines are untouched, so the scope guard still holds.

## Post-Completion

**Manual verification:**

The failure text is what a human reads at a machine that will not start, and no automated test
proves it *reads well*. Point a connection file at a database with no `public.trends` and start
the application once; the remedy must name `semibase site` and must not mention Simple-Scada. The
commands and the exact expected text are step 6 of `## Verify it yourself`.

**Remaining slices**

`postgres-live-edge-and-demo` — the realtime poll, the fresh tail, the `--follow` writer and the
stub's retirement.

## Verify it yourself

Run these before shipping. Each states what to run and what separates the branch from `master`.

1. **The removed command reaches no operator.**

   ```powershell
   git grep -n "semibase create" -- SemiPlot/ docs/architecture/ CLAUDE.md
   ```

   Nothing at HEAD, exit 1. Nine hits over four files on `master` — `ArchiveNotInitialisedError.cs`,
   `MissingRelationProbe.cs`, `StartupFailureMapper.cs`, `StartupFailureMapperTests.cs`.

2. **The probe and everything named after it are gone.**

   ```powershell
   git grep -n "MissingRelationProbe\|RelationProbe\|missingRelation" -- SemiPlot/ docs/architecture/ CLAUDE.md
   ```

   Nothing at HEAD, exit 1. 39 hits over 14 files on `master`.

3. **The remedy no longer branches on the table name.** This is the behaviour change, and one test
   pins it:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj `
     --filter "FullyQualifiedName~TheRemedyDoesNotDependOnWhichTableIsAbsent"
   ```

   Passes at HEAD. To watch it fail, put `master`'s mapper back under the current test and restore
   it after:

   ```powershell
   git checkout master -- SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj `
     --filter "FullyQualifiedName~TheRemedyDoesNotDependOnWhichTableIsAbsent"
   git checkout HEAD -- SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs
   ```

   It fails: `master`'s `DescribeMissingObjectRemedy` switches on `error.Table` and answers
   "Table 'trends' belongs to Simple-Scada. Run the SCADA against this database once…" for one table
   and "Run 'semibase create' against this database to finish provisioning." for the other. Eliding
   the table name leaves two different strings.

4. **Every `42P01` read still names a relation, and each names its own statement's.** Needs a
   container runtime:

   ```powershell
   $env:SEMIPLOT_REQUIRE_DB=1
   dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj --filter "FullyQualifiedName~ADropped"
   ```

   4 passed, 0 skipped, total 4. The four:

   - `PostgresCatalogReadTests.ADroppedCatalogueFailsNamingSemiplotTags` (`:129`)
   - `PostgresExtentReadTests.ADroppedCatalogueFailsNamingTheStatementsOwnRelation` (`:87`)
   - `PostgresExtentReadTests.ADroppedTrendsTableFailsNamingTrends` (`:111`)
   - `PostgresHistoryReadTests.ADroppedTrendsTableFailsNamingTrends` (`:300`)

   The second is the inverted one: it asserts `trends` for a dropped catalogue, which is the
   accepted anomaly the slice documents.

5. **Both suites and the gates.**

   ```powershell
   dotnet build SemiPlot.slnx -c Release
   dotnet format SemiPlot.slnx --verify-no-changes
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj
   $env:SEMIPLOT_REQUIRE_DB=1; dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj
   ```

   0 warnings, 0 errors; exit 0; 371 passed, 0 skipped; 360 passed, 0 skipped.

6. **The one manual check: does the failure text read well to a person?** No test proves that.
   Provision a database, drop the table the SCADA writes, and start the application against it:

   ```powershell
   psql -c "DROP TABLE public.trends CASCADE" -d <provisioned-database>
   dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir <dir>
   ```

   `<dir>` holds `archive-connection.yaml` pointing at that database
   (`StartupProbe.ConnectionFileName`, default directory `C:\DISTR\Config\SemiPlot`). Dropping
   `trends` rather than the whole database is what reaches the table arm: `StartupProbe` reads the
   catalogue first (`:114`) and the extent second (`:122`), so `semiplot_tags` must survive.

   The startup failure window must read:

   - title: **The archive is not provisioned**
   - detail: **The archive '<database>' at <host>:<port> holds no table 'trends'.**
   - remedy: **Table 'trends' is created by provisioning. Run 'semibase site' against this database
     to finish provisioning it.**

   It must not say Simple-Scada, must not say `semibase create`, and must not tell the operator to
   run a SCADA. Dropping `semiplot_tags` instead must produce the same remedy with the other table
   name — that is the property step 3 pins, seen by eye.

**Executed by exec:**

- branch: missing-relation-probe-removal

**Tasks and commits**

| Task | Commit | Subject |
| --- | --- | --- |
| 1. Remove the probe | `fc4b58d` | `refactor(postgres): remove the missing-relation probe` |
| 2. Correct the operator text | `9cb1f6b` | `fix(startup): name the command that provisions the archive` |
| 3. Update documentation | `d9156c5` | `docs: state one provisioning run for both archive tables` |
| 4. Verify acceptance criteria | `74b205b` | `test: record the acceptance evidence` |

**Review phases**

Two ran: a three-agent comprehensive pass and a single-agent critical pass. The external `codex`
phase did NOT run — `codex` is not installed on this machine.

**What review changed** — two commits, `73b7848` for code and `9e6a6f5` for prose.

- The removed ownership model survived the rewrite in four documents and one source comment, two of
  them contradicting their own file.
- The branch's own rewrite left three files claiming consumers route on
  `ArchiveNotInitialisedError.Table`, while a fourth said the opposite. They now all say the
  consumer routes on `MissingObject` and `Table` names the absent object in the detail line.
- A remedy arm the plan asked for — the null-or-blank `Table` default in
  `DescribeMissingObjectRemedy` — is unreachable by construction and went.
- `RequireRelation` was kept on a justification that is false: `ArchiveExceptionMapper` is
  `internal sealed` and reachable only from `PostgresDataProvider`, so no other provider could hit
  it. Its protection already lives in `ArchiveNotInitialisedError`'s base initialiser. It went, and
  with it the three-row theory that pinned it.
- The `[Fact]` added by task 2 was replaced by one that pins the property — remedies equal after the
  table name is elided — rather than asserting string absence over three literal instances.

**Declined, with the reason**

- `ADroppedCatalogueFailsNamingTheStatementsOwnRelation` stays inverted. Its residual value is
  narrow and understood: it is the only coverage of the extent read's `42P01` path. The reason sits
  in its own comment above the test (`PostgresExtentReadTests.cs:83-85`).
- `StatementTimeoutReader`, `ArchiveQueryTimedOutError` and the seven-type error vocabulary stayed
  out of scope, as the plan's scope guard states.

**Measured at HEAD**

| Check | Result |
| --- | --- |
| `dotnet build SemiPlot.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format SemiPlot.slnx --verify-no-changes` | exit 0 |
| `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` | **371 passed** |
| `SEMIPLOT_REQUIRE_DB=1` `dotnet test …SemiPlot.Tests.Data…` | **360 passed, 0 skipped** |
| `git grep -n "semibase create" -- SemiPlot/ docs/architecture/ CLAUDE.md` | nothing |
| `git grep -n "MissingRelationProbe\|RelationProbe\|missingRelation" -- …` | nothing |

The critical pass returned NO CRITICAL FINDINGS. It also checked the slice's premise against ground
truth by running `ghcr.io/semiteq/semibase:latest --help`, which lists `site`, `bench` and `version`
and no `create`.
