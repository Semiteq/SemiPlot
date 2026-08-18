# Read the pen catalogue and the archive extent

## Overview

The first two operations that touch a real database. `PostgresDataProvider` fails three of its four
members with `ProviderNotImplementedError` (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:23-44`);
`Subscribe` (`:18-21`) returns `Observable.Empty` deliberately and is not one of them. This slice
replaces two of those three bodies with real reads and builds the connection machinery both need.

Three things ship. The Npgsql data source, which owns the connection and the per-command time bound
the scaffold deferred. The pen catalogue read, mapping `semiplot_tags` rows onto `Pen`
(`SemiPlot/SemiPlot.Core/Trends/Pen.cs`). The archive extent read, using per-variable bounded
subqueries: an unbounded minimum over `trends` cannot use `PRIMARY KEY (id, l, t)`, whose leading
column is `id`, and reads every row of every partition. Bounded per `id`, each subquery becomes an
index scan per partition under a `MergeAppend` — the cost scales with the partition count, not with
the archive's row count.

**The empty-versus-missing catalogue question is settled here, and the answer splits it.** A missing
`semiplot_tags` raises SQLSTATE `42P01` and is a typed failure — `ArchiveNotInitialisedError` already
carries a `Table` field for exactly this routing. An empty `semiplot_tags` raises nothing: it is a
successful read of zero rows. Both states stay distinguishable, which is what SemiBase requires, and
that split needs no new error type. `docs/architecture/data-integration.md:292-293` carries the two
states as two rows, and `:312-320` states the split and why it needs no type of its own. One type is added for a different reason: an unmapped
SQLSTATE has no named output today, and Task 4 gives it `ArchiveReadFailedError`.

`ArchiveExtent` (`SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs:3`) cannot say "no data" — it is a
two-`DateTime` record with no empty representation. A fresh archive returns nulls from the extent
query, and mapping those onto `default(DateTime)` would hand the minimap an extent beginning in year
0001. The type gains an explicit empty form.

The application still runs on the stub. The composition root is untouched.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-catalog-and-extent

**What the scaffold left to implement**

- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:16-45` — takes no constructor
  dependencies and fails three of its four members. `QueryPensAsync` (`:23`) and
  `QueryArchiveExtentAsync` (`:40`) are this slice's; `QueryHistoryAsync` (`:29`) stays failing, and
  `Subscribe` (`:18-21`) already returns `Observable.Empty` and stays as it is.
- `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs` — carries the
  nine connection fields, including `SourceTimeZone` (`:19`), and builds the connection string with
  `Command Timeout=0`. Nothing constructs a connection from it yet.
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs:9-18` — constructed from a
  `TimeZoneInfo`, total in both directions, no error path. It exists and is unit-tested, and nothing
  registers or constructs it yet.
- `SemiPlot/SemiPlot.Core/Data/Errors/` holds nine public error types across ten files; the tenth
  file is the `ConnectionFileProblem` enum, which is not an error type. Five of the nine have never
  been raised by any code and the completed scaffold plan declares their fields provisional, which
  puts a revision in scope here. None is needed: every `Archive*` type carries `Host`, `Port` and
  `Database`, `ArchiveAccessDeniedError` adds a username and `ArchiveQueryTimedOutError` a `TimeSpan`
  bound, and the mapping in Technical Details fills every one of them. None of those five values is on
  a `PostgresException`, which is why the mapper is constructed rather than static.
  `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs` constructs all five by exact signature
  (`:58`, `:68`, `:80`, `:93`, `:106`), so any field that does change takes that file with it in the
  same task.

**The types the catalogue maps onto**

- `SemiPlot/SemiPlot.Core/Trends/Pen.cs` — `record Pen(long PenId, string Name, string Group,
  string Color, PenLineStyle LineStyle = PenLineStyle.Interpolated)`.
- `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs:3-7` — two members, `Interpolated` and `Stepped`,
  with no explicit values.
- **What `line_style` holds is already decided by the seeder.**
  `SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs:62` writes
  `command.Parameters.AddWithValue("line_style", (short)pen.LineStyle)`, and
  `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticPen.cs:12` types that property as Core's
  `PenLineStyle`. The bench archive therefore stores a `smallint` carrying the C# enum ordinal, `0`
  for `Interpolated` and `1` for `Stepped`. Only the column's declared type is SemiBase's, and it
  already accepts a `smallint`. Task 5 maps from that evidence; Task 7 confirms it against the
  database rather than discovering it.
- **The hazard that follows: the wire format is an unnamed enum ordinal.** The database
  representation of `line_style` is the declaration order of an enum in Core, and the seeder holds a
  deliberately frozen verbatim copy of the pen catalogue that would not move with it. Reordering
  `PenLineStyle` or inserting a member ahead of `Stepped` silently reinterprets every commissioned
  site's catalogue, with no compiler error and no failing test outside the gated suite. Task 5 pins
  the ordinals and reads them through an explicit `short` switch.

**The extent's consumers, and why the type must change**

- `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs:3` — `public sealed record ArchiveExtent(DateTime
  FirstUtc, DateTime LastUtc);`. No empty form.
- `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs:105-130` — `ApplyExtent` sets `HasExtent = true`
  on any success. `:54` declares `HasExtent`, `:95` no-ops `NavigateToFraction` when it is false, and
  `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml.cs:82` hides the strip highlight on the same flag.
  So the UI already has the state an empty extent needs; only `ApplyExtent` must stop asserting it.
- Other constructors of the type: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:97`,
  `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs:123`.
- Existing assertions that must keep passing:
  `SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapViewModelTests.cs:36,46` and
  `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs:245-250`.

**The statements this slice implements**, from `docs/architecture/data-integration.md:87-89` and
`:103-108`. All statement text on the application and provider path lives in one place in
`SemiPlot.DataSource.Postgres`, and parameters are always bound, never interpolated. The bench seeder
and the gated harness own SQL of their own by design — the schema resource, the partition DDL, the
`COPY`, the catalogue upsert, `CREATE DATABASE`, `DROP DATABASE` — and are outside that rule.

**Who creates which table.** `semibase create` provisions the database, both roles, the grants, the
default-privileges chain and `semiplot_tags`, and nothing else
(`SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseProvisioner.cs:9-11`). `trends` is the SCADA's, and
in the bench it is created by the seeder from the embedded `sql/semiplot_dev.sql`
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs:61`). The harness proves the split:
`ArchiveTemplate.SeedAsync` finds `to_regclass('public.trends')` null immediately after
`SemibaseProvisioner.CreateAsync` and then creates it. So a provisioned-but-unseeded database has
`semiplot_tags` and no `trends`, and the extent statement over it raises `42P01` at parse analysis —
an empty outer table does not suppress an undefined relation. That state is a typed failure, not an
empty extent, and Task 7 asserts it as one.

**The catalogue's real DDL is looser than `Pen`.** SemiBase v0.1.0's `sql/semiplot_tags.sql` declares
`id integer PRIMARY KEY`, `name text NOT NULL`, `group_name text`, `unit text`, `color text` and
`line_style smallint NOT NULL DEFAULT 0`. So `group_name` and `color` are nullable while
`SemiPlot/SemiPlot.Core/Trends/Pen.cs` requires non-null `Group` and `Color`, and `id` is 32-bit while
`Pen.PenId` is `long`. The bench seeder always writes both nullable columns
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs:60-61`), so only a commissioned site
reaches the null. Task 5 coalesces and widens; Task 7 pins it with a row the seeder would never write.

**The gated harness types are owned by `archive-populator` and are used unchanged.**
`SemiPlot/SemiPlot.Tests.Data/Integration/` carries `PostgresContainerFixture`,
`ArchiveDatabaseCollection`, `PostgresServer`, `ArchiveDatabase`, `SemibaseBinary`,
`SemibaseProvisioner`, `ArchiveTemplate`, `SeededArchive`, `DatabaseGate` and `TestEnvironment`. This
slice edits none of them; it adds test classes and their own test-local setup on top. The members
Task 7 needs:

| Member | What it gives |
| --- | --- |
| `PostgresContainerFixture.CloneTemplateAsync` (`:58`) | a private `ArchiveDatabase` cloned from the seeded template, disposable per test |
| `PostgresContainerFixture.CreateEmptyDatabaseAsync` (`:63`) | an `ArchiveDatabase` created from `template0` with no schema at all — not a provisioned database |
| `SemibaseProvisioner.CreateAsync` (`:33`) | runs the `semibase create` subprocess against a database, giving the provisioned-but-unseeded state |
| `ArchiveDatabase.ReaderConnectionString` (`:20`), `.AdminConnectionString` (`:16`) | the two roles the tests read and set up under |
| `ArchiveDatabase.ExecuteAsync` (`:67`) | one statement on a named connection string, used to empty or drop `semiplot_tags` |
| `SeededArchive` (`:11-33`) | one clone shared by a whole test class |

`SeededArchive`'s header (`:5-10`) states the contract every test in the class is bound by: the counts
asserted are the template's, so each test must leave the database as it found it. A test that drops
or empties a table cannot honour that and needs a clone of its own.
`SemiPlot/SemiPlot.Tests.Data/Integration/SeededArchiveTests.cs:237` asserts the reader role carries
`statement_timeout` `30s`, and `:153` already reads `semiplot_tags` over
`ArchiveDatabase.ReaderConnectionString`, so `semiplot_reader`'s `SELECT` on the catalogue is
established.

**The provisioning states this slice must survive**, from `docs/architecture/postgres-instance.md:83-86`:
no database; database without `trends`; `trends` without `semiplot_tags`; `semiplot_tags` present but
empty. Each is normal and never a crash. States 1 to 3 travel as typed failures, state 4 as a
successful empty catalogue. A sixth sits beside them, reachable only through the missing-relation
probe: a database holding NEITHER relation. Provisioning precedes commissioning, so the probe answers
`semiplot_tags` there and the operator is sent to `semibase create` rather than to starting a SCADA
against a database SemiBase has not touched. States 2, 3 and 4 each get a gated test in Task 7; state 1 is covered by
the mapper's unit test alone, for the reason in Testing Strategy. A fifth state sits outside that list and is this slice's too: a fully
provisioned and seeded archive holding no rows in `trends`, which `data-integration.md:119` maps to an
empty extent on a successful `Result`. It is the only state that exercises the null-from-no-rows path,
since state 4 produces its null from having no configured variables to join against.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- Complete each task fully before moving to the next.
- Every task that changes code carries its own tests, listed as separate checklist items.
- All tests pass before the next task starts.
- Update this plan when scope changes during implementation.

## Testing Strategy

**Pure tests and gated tests, split by what they need.** Statement-text pinning, the exception
mapper, the `line_style` conversion and the extent's empty representation are pure logic and carry
`[Trait("Category","Unit")]`. The mapper stays in that list because it is constructed from a settings
instance and an accessor for the cached bound, and takes an already-resolved table name rather than
issuing the probe itself — no test of it opens a connection. Reading a row out of an
`NpgsqlDataReader` is not pure logic — the type is not constructible outside a database — so the row
read is covered by the gated tests in Task 7, not by a unit test. Anything opening a connection
carries `[Trait("Category","Integration")]` and goes through `DatabaseGate`, which skips with a
stated reason when no container runtime or `semibase` binary answers, and fails instead of skipping
when `SEMIPLOT_REQUIRE_DB` is set.

**Provisioning state 1 has no end-to-end coverage, and the mapper unit test is deliberately the whole
of it.** `postgres-instance.md:83` covers both nothing answering at the configured address and the
database not existing; the first surfaces as `ArchiveUnreachableError` and the second as SQLSTATE
`3D000`. Both are asserted in `ArchiveExceptionMapperTests` over fabricated exceptions, and no gated
test reaches either — the harness hands out a reachable server and an existing database, and driving
the unreachable case would mean pointing an integration test at a dead address and waiting out a
connect timeout inside the gated suite. States 2, 3 and 4 do get gated tests, in Task 7. The mapper
is the single place both of state 1's shapes are translated, so its unit test pins the whole of what
this slice decides about them.

**No `Category=Unit` test constructs a provider that can open a connection.** Constructing
`ArchiveDataSource` from settings opens nothing — `NpgsqlDataSource` connects lazily — so the unit
tests in `PostgresCompositionTests` may build a provider over a settings instance pointing at an
address nothing answers, as long as they call only members that return before touching the data
source. A unit test calling a real read would attempt a TCP connection and stall for the connect
timeout before failing with the wrong error.

**The provider project's internals are visible to its test project.** `SemiPlot.DataSource.Postgres`
carries `<InternalsVisibleTo Include="SemiPlot.Tests.Data" />`, matching what `SemiPlot.Core` and
`SemiPlot.UI` already declare for `SemiPlot.Tests`. The statement holder, the mapper, the
missing-relation probe and the line-style reader therefore stay `internal` instead of being made
public to be testable.

**`SemiPlot.Tests.Data` uses raw xunit `Assert.` and references no assertion library**, per
`CLAUDE.md`. Every test class carries all three traits.

**Assertions are on the error type and its structured fields, never on exact message wording.**

**Statement text is pinned against the architecture document itself, not against a literal in the
test.** The test reads the fenced `sql` block out of `docs/architecture/data-integration.md` at run
time and compares it to the constant. `SemiPlot.Tests.Data` targets plain `net10.0` and its test
process can walk up from `AppContext.BaseDirectory` to the repository root, so this costs roughly
twenty lines and puts the document in the loop. A comparison against a literal copied into the test
file would only fail when someone edits the code and not the test, which is the check nobody needs.
Parameter binding is asserted by `postgres-history-read`: neither statement in this slice takes a
parameter.

**`EXPLAIN` assertions are gated tests, not unit tests** — they need a real planner over real
statistics. They assert the plan's SHAPE, not an index name: an `Index Scan` or `Index Only Scan`
under each per-variable subquery, and no `Seq Scan` over a `trends` partition that holds rows. The
qualifier is not slack: `tpdefault` is empty by design (`sql/semiplot_dev.sql:9-10`), and the planner
may legitimately pick a sequential scan of an empty analysed partition, so asserting over every
partition would fail on a correct plan. The day partitions `tpYYYYmMMdDD`
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs:13`) are the ones holding rows and the ones
the assertion covers. The index name is not assertable either. `trends` is declared `PARTITION BY RANGE (t)` with `CONSTRAINT tpk PRIMARY KEY
(id, l, t)` (`sql/semiplot_dev.sql:12-24`), so `tpk` is the parent partitioned index and is never
scanned; each partition carries its own cloned index that PostgreSQL names `<partition>_pkey`, and
`EXPLAIN` prints the leaf. The shape assertion survives partition renaming and still fails the
moment the per-variable bounds are dropped, which is the invariant worth pinning.

## Acceptance Evidence

There is no defect to reproduce: this slice adds two reads that do not exist. The evidence is that
each behaves, by runnable command.

1. **The catalogue reads a seeded archive.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresCatalogRead"`
   Against the bench-seeded database the provider returns the seeded pens with their names, groups,
   colours and line styles, ordered by group then name. Gated; skips with a stated reason when no
   runtime answers.

2. **An empty catalogue is a success, a missing one is a typed failure.**
   The same run covers both: with `semiplot_tags` emptied the result is `IsSuccess` with zero pens;
   with the table dropped the result is `IsFailed` carrying `ArchiveNotInitialisedError` whose
   `Table` is `semiplot_tags`. That pair is the whole point of the slice's settled decision, and
   asserting only one of them would leave the other free to regress.

3. **The extent reads a seeded archive, an emptied one and an unseeded one.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresExtentRead"`
   Against seeded data the extent matches the seeder's own first and last raw timestamps. Against a
   clone whose `trends` has been truncated the result is `IsSuccess` carrying `ArchiveExtent.Empty`,
   not a failure and not an extent at `DateTime.MinValue` — that is the no-rows state
   `data-integration.md:119` describes. Against a full archive whose `semiplot_tags` has been emptied
   the result is also `ArchiveExtent.Empty`, by a different route: the extent is the span of the
   configured variables, not of the archive. Against a provisioned but unseeded database, where
   `semibase create` has made `semiplot_tags` and nothing has made `trends`, the result is `IsFailed`
   carrying `ArchiveNotInitialisedError` whose `Table` is `trends`. Against a full archive whose
   `semiplot_tags` has been DROPPED the same read reports `Table` as `semiplot_tags` — the one case in
   the slice where `MissingRelationProbe` and the extent statement's `trends` fallback disagree, and
   therefore the only case that fails if the probe is dropped for a constant.

4. **The extent query reaches its rows through an index, not a scan.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ExplainPlan"`
   After `ANALYZE`, `EXPLAIN` output for the extent statement shows an `Index Scan` or `Index Only
   Scan` under each per-variable subquery and no `Seq Scan` over a `trends` partition holding rows.
   This is the enforced form of the hazard `data-integration.md:111-117` describes in prose.

5. **The statements are what the architecture document says.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StatementText"`
   Each statement is compared character for character against the fenced `sql` block read out of
   `docs/architecture/data-integration.md` at run time. Editing either side alone fails here — on a
   developer machine and in any local `dotnet test` run. CI does not add to that: `.github/workflows/ci.yml`
   filters its paths to `SemiPlot/**`, `SemiPlot.slnx`, `sql/**`, `.editorconfig`, `global.json` and the
   workflow file, with `!**.md` on top, so a doc-only edit to a pinned statement triggers no run at all.
   The guarantee is that the pair cannot drift unnoticed by anyone who runs the suite, not that a push
   touching only the document is blocked.

6. **The whole suite and the format gate.**
   `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` must report the same passing count as at the branch
   point plus only the tests this slice adds for the `ArchiveExtent` change. `SemiPlot.Tests.Data` must
   report zero failures, and when no runtime answers its skip count must grow by exactly the number of
   gated tests this slice adds — the eleven Tasks 7 and 8 name method by method — with none of them
   passing. With `SEMIPLOT_REQUIRE_DB=1` those same eleven fail instead of skipping; that inversion is
   what keeps the CI `data-tests` job from reporting a green run over a suite that ran nothing.
   `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.
   Note that the format gate does not check the UTF-8 BOM `.editorconfig` requires, so a scripted edit
   can drift encoding past it: `head -c 3 <file> | od -An -tx1` must show `ef bb bf`.

## Progress Tracking

- Mark completed items `[x]` immediately when done.
- Add newly discovered tasks with `+`.
- Record blockers with a `BLOCKED` note and the reason.
- Keep this file in sync with the work actually done.

## Solution Overview

**Missing and empty are different states and travel in different channels.** `postgres-instance.md:83-86`
already lists them separately — `trends` without `semiplot_tags` is provisioning unfinished, while
`semiplot_tags` present but empty is commissioning unfinished. A missing table raises `42P01`, which
maps to `ArchiveNotInitialisedError` with `Table` naming `semiplot_tags`; the type's shipped summary
already specifies that routing. An empty table is a successful read of zero rows. The two remain
distinguishable, which is what SemiBase requires, and that split creates no new error type.

`EmptyTagCatalogError` is not added. The rule the scaffold adopted is that a public error type exists
if and only if a distinct operator-visible **failure** sentence exists, and that operator-visible
states which are not failures travel in the success channel. "No variables configured" is a state
sentence: the database answered correctly and nothing is broken. Routing it as a failure would also
force an exclusion in every generic failure handler — `MinimapViewModel.ApplyExtent:105-119` logs any
failed `Result` as a warning, so a fresh installation would log one on every start.

**The extent gains an explicit empty form rather than a sentinel.** `ArchiveExtent` becomes a record
with a static `Empty` and an `IsEmpty` property computed from its two bounds. Mapping the null row
onto `default(DateTime)` and reading the result as a real span would produce an extent starting in
year 0001 that the minimap would render as real, which is the silent-wrong-data failure this roadmap
exists to prevent; deriving `IsEmpty` from the bounds names that span as the empty one wherever it
comes from.

The real competitor is `Task<Result<ArchiveExtent?>>` on `IDataProvider`, where null is the empty
case: the type keeps one meaning, no static instance is needed, and `MinimapViewModel.ApplyExtent`
gains one null check. It is a defensible design and it loses here on two counts. The interface
signature stays stable for the six slices that follow, none of which would otherwise touch
`IDataProvider`. And a nullable `Result<T?>` doubles the states a consumer must destructure at every
call site — failed, successful-null, successful-value — where `Result<ArchiveExtent>` keeps two and
pushes the third onto a flag that only the consumer that cares reads.

**The data source owns the time bound the scaffold deferred.** `Command Timeout=0` leaves every
command unbounded, so the provider sets bounds explicitly: the physical-connection initializer's own
read-back query carries `CommandTimeout = 10` seconds, and every read command carries the effective
`statement_timeout` plus 30 seconds. Because the server never legitimately stays silent longer than
its own bound during a statement, that backstop fires only when the server is not answering — so its
`TimeoutException` maps to `ArchiveUnreachableError` and never to `ArchiveQueryTimedOutError`, and the
two timeout sources stop being confusable.

The effective bound only exists once a physical connection has opened, and
`NpgsqlDataSource.CreateCommand(...)` sets `CommandTimeout` before that — so on the first read of a
process the cached value would be unset. The wrapper therefore opens the connection explicitly first
and builds the command against that open connection, which is why its surface hands out both rather
than one call taking a statement string.

**A `42P01` from the extent query is ambiguous and is resolved by probing, not by ordering.** That
statement touches both `semiplot_tags` and `trends`. PostgreSQL does not populate the table-name field
for `undefined_table`; the relation name appears only in the localizable message text, so parsing it
breaks under a non-English `lc_messages`. A `to_regclass` probe over both relations answers which one
is absent, for either query and for calls made in any order, where an ordering assumption would not.

**The probe lives in the read path, not in the mapper.** `ArchiveExceptionMapper` is a sealed class
constructed with the connection settings and an accessor for the cached effective bound, exposing one
synchronous method over an exception and an already-resolved missing-table name, which the `42P01`
path requires and every other path ignores. It has to be constructed rather than static because the public error types demand
values no exception carries: `Host`, `Port` and `Database` on every `Archive*` type, the username on
`ArchiveAccessDeniedError`, and the effective `statement_timeout` on `ArchiveQueryTimedOutError`,
which lives only in the data source's cached field. It stays synchronous, opens no connection and
returns no `Task`, so a unit test constructs it from a settings instance and a fixed bound and needs
no database. Making it issue the probe would make it async, give it a data-source dependency and put a
network round trip inside the exception path, and would end the unit-testability that keeps its
`[Trait("Category","Unit")]` honest. So the provider catches `42P01`, runs the probe through a small
separate type, and hands the answer to the mapper. The probe's own rules are stated in Technical
Details, because an error-path query left under-specified is how an error path hangs.

**An unmapped SQLSTATE gets a named type rather than a bare string.** `ArchiveReadFailedError` is
added to `SemiPlot.Core/Data/Errors/`, carrying host, port, database and the SQLSTATE. It earns its
place under the same rule the rest of the vocabulary lives by: the operator sentence is distinct and
real — the archive rejected the read for a reason this build does not recognise — and the SQLSTATE
plus the log entry is exactly what an engineer needs to name the cause. Without it the mapper's
fallback would either invent an untyped `Result.Fail(string)` that no consumer can route on, or let
the exception escape, which is the unmapped-internal-plane leak `data-integration.md` forbids.

**No connection-level keepalive is set, and that is the decision rather than a deferral.** Npgsql's
`Keepalive` defaults to 0, meaning disabled, and the data source leaves it there. The pool already
closes an idle physical connection after `Connection Idle Lifetime`, 300 seconds by default, checked
every `Connection Pruning Interval`, 10 seconds by default, and the connection string sets no
`MinPoolSize`, so nothing is held open past that. A five-minute idle ceiling sits well inside any
plant firewall or NAT idle timeout, so the case a keepalive would cover — a pooled connection killed
by a middlebox while idle — is one the pool has already discarded the connection for. A keepalive
would instead add a round trip per idle connection per interval and mask a genuinely dead link that
the next read reports as `ArchiveUnreachableError` anyway. `postgres-realtime-poll` is the slice that
reopens this: it is the first to hold a connection across a poll interval, and if its interval ever
exceeds the idle lifetime the trade-off changes.

## Technical Details

**`ArchiveExtent`**, in `SemiPlot.Core`:

| Member | Meaning |
| --- | --- |
| `ArchiveExtent.Empty` | static, the configured variables span no time — no rows, or no configured variables |
| `IsEmpty` | computed: true when both bounds are `default`, so for `Empty` and for any extent equal to it |
| `FirstUtc`, `LastUtc` | meaningful only when `IsEmpty` is false |

**`IsEmpty` is a computed property over the two bounds — empty when both are `default` — and the type
carries no state beyond them.** `ArchiveExtent` is a positional record, so `with` is available on it
whatever a copy constructor's accessibility, and a record's copy constructor runs BEFORE `with`
applies its initializers: it cannot know what the result will hold. Any stored flag is therefore
wrong for one of the two directions — carried across, `Empty with { FirstUtc = x, LastUtc = y }`
reports itself empty and a consumer routing on `IsEmpty` drops it; cleared, `Empty with { }` reports
a populated extent spanning year 0001 to year 0001, which is the minimap misreading this type exists
to prevent. Deriving the flag from the bounds makes every copy report the state its own values
describe.

`ArchiveExtent.Empty` equals `new ArchiveExtent(default, default)` by consequence, and that is
correct rather than a defect: an extent from year 0001 to year 0001 is meaningless, so reporting it
as empty is the honest answer. Nothing routes on the two being tellable apart —
`MinimapViewModel.ApplyExtent` branches on `IsEmpty` alone, and the gated extent assertions in Task 7
compare real seeded timestamps, which are never `default`. The protection is preserved in a different
form: instead of the two values differing, both are empty.

`ToString` is still declared rather than synthesized, so `Empty` logs as `IsEmpty = true` instead of
as two year-0001 timestamps. It reads only the two bounds through an invariant-culture format and
cannot throw.

The two-argument constructor is kept, which is what leaves
`SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:97` untouched. `FakeDataProvider.cs:123`
does change, for a separate reason: it returns a populated extent unconditionally, and
`MinimapViewModel.ApplyExtent` is private, so the only way a view-model test can drive the empty case
is through the fake. It gains a settable extent whose empty setting makes `QueryArchiveExtentAsync`
answer `ArchiveExtent.Empty`; its `ArchiveFirstUtc` and `ArchiveLastUtc` defaults stay as they are, so
the existing assertions at `MinimapViewModelTests.cs:36,46` keep passing untouched.

**The catalogue row read is total against SemiBase's DDL, not against the seeder's output.**
`group_name` and `color` are nullable columns mapped onto non-null `Pen.Group` and `Pen.Color`, so the
row read coalesces a null in either to the empty string. Reading them straight would throw
`SqlNullValueException` down the unmapped-exception path at a commissioned site with a NULL group, and
no test in this plan would see it because the bench seeder always writes both. `semiplot_tags.id` is
`integer` while `Pen.PenId` is `long`, so the read takes `GetInt32` and widens; `GetInt64` throws
`InvalidCastException` on an `int4` column. The rule that already governs the line-style fallback
governs this too: one malformed row must not hide every other pen.

**The statements**, verbatim from `docs/architecture/data-integration.md:87-89` and `:103-108`, held in
one class in `SemiPlot.DataSource.Postgres`. The catalogue statement selects `id`, `name`,
`group_name`, `color` and `line_style` from `semiplot_tags` ordered by `group_name` then `name`. The
extent statement takes `min(lo)` and `max(hi)` over a `CROSS JOIN LATERAL` on `semiplot_tags`, whose
lateral subquery carries one bounded `min(t)` and one bounded `max(t)` over `trends` per variable at
`l = 0`. The probe statement resolves both relation names through `to_regclass`.

**The extent is the span of the configured variables, not of the archive.** The statement is rooted
at `semiplot_tags`, so a present-but-empty catalogue over an archive holding months of rows yields
`min(lo) = null` and therefore `ArchiveExtent.Empty`. That is the intended behaviour — with no
configured variables there is nothing to draw, and a minimap strip spanning data no pen can render
would be a lie — but it is a consequence of the statement's shape rather than of an explicit rule, so
Task 7 asserts it and Task 10 writes it beside the statement in the architecture document.

**Because `trends` is partitioned on `t` and the extent carries no `t` predicate, no partition is
pruned.** Each bounded `min(t)` / `max(t)` becomes a `MergeAppend` over one index scan per partition,
so the cost scales with the partition count. That is far cheaper than the unbounded form, which reads
every row of every partition, but it is not a single walk to one index edge.

**The `to_regclass` probe**, run by the read path on `42P01`:

| Rule | Why |
| --- | --- |
| a fresh connection from the data source, never the failed command's | the failed command may sit in an aborted transaction, where any further statement answers `25P02` |
| `CancellationToken.None` | neither read in this slice carries a token, and from `postgres-history-read` on the caller's token is frequently already cancelled by the time a read fails, which would leave the probe unable to run at all. Passing `None` makes the rule permanent rather than a property of which members happen to take a token |
| `CommandTimeout = 10` seconds | `Command Timeout=0` would otherwise let the error path hang without bound |
| the probe throwing is not propagated | the mapper is called with no table name and still produces a usable error; the mapper is never re-entered for the probe's own failure, and that recursion is structurally forbidden |
| the probe answering nothing still fills `Table` | `ArchiveNotInitialisedError.Table` is non-nullable and the type's own summary tells consumers to route on it, so an empty string there would break every consumer. The read passes its statement's fallback relation, which the read knows and the mapper does not |
| the fallback relation per statement | `semiplot_tags` for the catalogue read, which touches no other relation. `trends` for the extent read, which touches both: if `semiplot_tags` were the absent one the catalogue read would be failing too, while `trends` absent with `semiplot_tags` present is the state only the extent read discovers, and it is the earlier provisioning state (`postgres-instance.md:84` precedes `:85`) |

**The data source's public surface**, fixed here because `postgres-history-read` binds `@ids`,
`@layer`, `@from` and `@to` against it:

| Member | Contract |
| --- | --- |
| open a connection | returns an open `NpgsqlConnection` from the pool, by which point the physical-connection initializer has run |
| create a command | takes the statement text and an open connection, returns an `NpgsqlCommand` already carrying the bound `CommandTimeout`; the caller adds parameters |
| the effective bound | the cached parsed `statement_timeout`, which the exception mapper reads to fill `ArchiveQueryTimedOutError.Timeout` |
| `Dispose` and `DisposeAsync` | both implemented, each forwarding to the wrapped `NpgsqlDataSource` |

The wrapper implements both disposal interfaces because it is registered as a DI singleton and
`Microsoft.Extensions.DependencyInjection`'s synchronous `ServiceProvider.Dispose()` throws
`InvalidOperationException` for an instantiated singleton that implements only `IAsyncDisposable`.
`PostgresCompositionTests` disposes its provider synchronously (`using var services = BuildProvider();`),
so an async-only wrapper fails that suite the moment the data source is resolved once.

A wrapper exposing only `ExecuteReaderAsync(statementText)` gets widened on the next slice's first
day, so the seam is a connection plus a command from the start.

**SQLSTATE mapping**, at the provider boundary:

| SQLSTATE or exception | Public type |
| --- | --- |
| socket failure, `NpgsqlException` with inner `TimeoutException` | `ArchiveUnreachableError` |
| `3D000` | `ArchiveDatabaseMissingError` |
| `42P01` | `ArchiveNotInitialisedError`, `Table` from the probe result the read path passes in, or the statement's fallback relation |
| `28P01`, `28000`, `42501` | `ArchiveAccessDeniedError` |
| `57014` | `ArchiveQueryTimedOutError`, `Timeout` from the data source's cached effective bound |
| anything else, including an unmapped SQLSTATE | `ArchiveReadFailedError`, carrying host, port, database and the SQLSTATE |

`OperationCanceledException` is the one thing the mapper does not map: it is rethrown. A cancelled
operation is not a failed `Result` in .NET, and a self-cancelled read is not an error at all. Neither
`IDataProvider.QueryPensAsync()` nor `QueryArchiveExtentAsync()` takes a `CancellationToken`, so
nothing in this slice can reach it. Telling a server-side `57014` from one this client asked for
needs a caller holding the token and belongs to `postgres-history-read`, the first slice to hand one
down.

**Timestamps cross the boundary through `ArchiveTimeConverter`.** The archive stores naive local
`timestamp(3)`; everything above the provider works in UTC. Extent values are converted on the way out
with `ToUtc`, once. `ToArchiveLocal` is not called by either read here — neither statement takes a
parameter — so Npgsql's requirement of `DateTimeKind.Unspecified` for `timestamp without time zone`
parameters is `postgres-history-read`'s constraint, not this slice's.

## What Goes Where

- **Implementation Steps** — the extent type, the statements, the data source, the mapper, the two
  reads, the gated tests, the `EXPLAIN` assertions, verification and documentation.
- **Post-Completion** — what the following slices inherit, and the remaining slices.

## Implementation Steps

### Task 1: Give ArchiveExtent an empty form

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Minimap/MinimapViewModelTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Data/ArchiveExtentTests.cs`

- [x] `ArchiveExtent` gains a static `Empty` and a computed `IsEmpty` property, keeping the two-argument
      constructor so `RandomStubDataProvider.cs:97` needs no change. `IsEmpty` is computed from the
      two bounds and the type stores nothing else, for the `with`-safety reason in Technical Details
- [x] the new test class goes in a folder rather than at the project root: the root of
      `SemiPlot.Tests.Data` holds the bench and seeder tests, while tests of Core types live in
      `Errors/` and provider tests in `Postgres/`. `Data/ArchiveExtentTests.cs`, namespace
      `SemiPlot.Tests.Data.Data`, sits beside the `Errors/DataErrorTests.cs` sibling that already
      tests Core types and follows its precedent. It carries all
      three traits — `Component=Core`, `Area=Data`, `Category=Unit`
- [x] `MinimapViewModel.ApplyExtent` (`:105-130`) returns without setting `HasExtent` when the value
      is empty, leaving the strip in the state `MinimapView.axaml.cs:82` already handles
- [x] `FakeDataProvider` gains a settable extent so a view-model test can drive the empty case.
      `QueryArchiveExtentAsync` (`:123`) returns a populated extent unconditionally today and
      `ApplyExtent` is private, so there is no other route into the branch. The defaults of
      `ArchiveFirstUtc` and `ArchiveLastUtc` are unchanged, which is what keeps
      `MinimapViewModelTests.cs:36,46` and the other existing cases passing
- [x] write tests: `Empty.IsEmpty` is true, a constructed extent's is false, a default-valued extent
      is empty and equal to `Empty`, the two-argument constructor still round-trips both timestamps,
      and both `with` directions hold — `Empty with { FirstUtc = x, LastUtc = y }` reports non-empty
      while `Empty with { }` stays empty
- [x] write a test that an empty extent leaves `HasExtent` false, driving it through
      `MinimapViewModel.LoadExtentAsync` over the fake's empty setting, alongside the existing
      `MinimapViewModelTests.cs:36,46` cases
- [x] run tests — must pass before Task 2

### Task 2: Hold the statement text in one place

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`

- [x] add `<InternalsVisibleTo Include="SemiPlot.Tests.Data" />` to the provider csproj, matching what
      `SemiPlot.Core.csproj:11` and `SemiPlot.UI.csproj:11` already declare for `SemiPlot.Tests`. Every
      internal type this slice creates — the statement holder here, the mapper and probe in Task 4, the
      line-style reader in Task 5 — is referenced from `SemiPlot.Tests.Data`, and without this the tests
      fail to compile with CS0122 rather than being made public to be testable
- [x] one internal static class carrying the catalogue and extent statements verbatim from
      `docs/architecture/data-integration.md:87-89` and `:103-108`, and the `to_regclass` probe
- [x] no SQL string exists anywhere else on the application and provider path; parameters are bound,
      never interpolated. The bench seeder and the gated harness own SQL of their own by design and are
      outside the rule
- [x] write a test that locates the repository root by walking up from `AppContext.BaseDirectory`,
      reads the fenced `sql` block under each of the two named headings in
      `docs/architecture/data-integration.md`, and compares it character for character to the
      constant. Both directions of a one-sided edit fail here, which a literal copied into the test
      file would not catch. The statements the document does not quote — the `to_regclass` probe and
      the `statement_timeout` read-back — are pinned by nothing: a literal copied beside the constant
      would fail only when someone edits the code and not the test, which is the mechanism this test
      exists to replace. The extractor's own guards do earn a test each, because an extractor that
      silently finds nothing would defeat the whole arrangement
- [x] `ArchiveStatementTextTests` carries all three traits — `Component=Core`, `Area=Data`,
      `Category=Unit`
- [x] run tests — must pass before Task 3

### Task 3: Build the data source and bound its commands

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ConnectionSettingsFactory.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveDataSourceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] add `<PackageReference Include="Microsoft.Extensions.Logging.Abstractions"/>` to the provider
      csproj and `<PackageReference Include="Microsoft.Extensions.Logging"/>` to the test csproj. Both
      are already pinned in `SemiPlot/Directory.Packages.props` at 10.0.8, so central package management
      needs no edit. `ArchiveDataSource` takes `ILogger<ArchiveDataSource>` by constructor injection;
      without the test-side package `AddLogging()` does not resolve
- [x] build an `NpgsqlDataSource` from `PostgresConnectionSettings`, with
      `UsePhysicalConnectionInitializer` reading the effective `statement_timeout` from `pg_settings`
      once per physical connection and caching the parsed value. `pg_settings.setting` is `text`
      carrying the value in the parameter's base unit, which for this parameter is milliseconds by its
      own definition — so `30s` on the reader role reads back as `30000` and an unbounded server as
      `0`. `SHOW statement_timeout` returns the unit-suffixed display string instead and is the wrong
      query; `SeededArchiveTests.cs:237` asserts it answers `30s`
- [x] supply BOTH delegates to `UsePhysicalConnectionInitializer`. Its signature is
      `(Action<NpgsqlConnection>, Func<NpgsqlConnection, Task>)` and Npgsql's own remark states that if
      an initializer is registered both versions must be provided. The synchronous one is never used
      here — every read opens asynchronously — so it throws `NotSupportedException`, which is what
      Npgsql documents for exactly this case and which also catches an accidental synchronous open
- [x] the initializer's own read-back command carries `CommandTimeout = 10` seconds, since
      `Command Timeout=0` leaves it otherwise unbounded and it is the one command that cannot use the
      effective bound it produces
- [x] cache the parsed bound in a thread-safe field: it is written from an initializer callback on
      whichever thread opened the physical connection and read from command construction on others
- [x] read commands carry `CommandTimeout` of the effective bound plus 30 seconds, or a fixed 5
      minutes when the effective bound reads back as `0`. The zero case logs a warning through the
      injected `ILogger<ArchiveDataSource>`, but only when the cached value changes: the initializer
      runs on every physical open, and an unconditional line repeats for the life of the process
- [x] the millisecond parse and the resolved bound are covered by `ArchiveDataSourceTests`, a
      `Category=Unit` class needing no database: `NpgsqlConnection.CreateCommand()` works on a closed
      connection, so `CommandTimeout` is readable off the command the data source builds. It pins a
      parsed bound producing bound-plus-30 s, a zero or unparsable bound producing the 5-minute
      fallback, and the not-yet-known state producing the same fallback without claiming the server
      bounds nothing. Without them a unit slip in the parse would leave every read on the fallback and
      every `ArchiveQueryTimedOutError.Timeout` at zero with the whole suite green
- [x] expose the cached effective bound so the exception mapper can fill
      `ArchiveQueryTimedOutError.Timeout`, which lives in no exception
- [x] bootstrap the bound by opening the connection before building the command.
      `NpgsqlDataSource.CreateCommand(...)` sets `CommandTimeout` before any physical connection
      opens, so on the first read of a process the cached value would still be unset. The wrapper
      calls `OpenConnectionAsync` first — by which point the initializer has run — and builds the
      command against that open connection
- [x] the public surface is an open connection plus a command carrying the bound timeout, with the
      caller adding parameters, as the table in Technical Details states. `postgres-history-read`
      binds `@ids`, `@layer`, `@from` and `@to` through this seam
- [x] the wrapper implements BOTH `IDisposable` and `IAsyncDisposable` and forwards each to the
      wrapped `NpgsqlDataSource`, which implements both and is held as a DI singleton. Async-only is
      not enough: `ServiceProvider.Dispose()` throws `InvalidOperationException` for an instantiated
      singleton that implements only `IAsyncDisposable`, and `PostgresCompositionTests` disposes
      synchronously through `using var services = BuildProvider();`. Forwarding also matters to the
      harness — a leaked data source keeps pooled connections open, a pooled connection makes
      `DROP DATABASE` refuse, and that is why `ArchiveDatabase.DisposeAsync`
      (`SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabase.cs:81-99`) clears the pools first
- [x] set no `Keepalive` on the connection string, and no `TcpKeepAliveTime`. The reasoning is in
      Technical Details and the decision belongs to this slice: the pool discards an idle physical
      connection after `Connection Idle Lifetime`, which no configuration here raises, so the idle-drop
      case a keepalive covers is already handled and a keepalive would only add traffic
- [x] `AddPostgresData` takes `PostgresConnectionSettings` and registers the data source, the provider
      and an `ArchiveTimeConverter` built from `settings.SourceTimeZone`; the composition root is not
      touched and still selects the stub
- [x] `PostgresDataProvider` is not touched by this task. It keeps its parameterless constructor and
      all four of its current bodies, which is all `AddPostgresData` needs to resolve `IDataProvider`.
      The provider takes its dependencies in one edit in Task 5, where the first of them is first used
- [x] update `PostgresCompositionTests` for the new registration signature: every `AddPostgresData()`
      call passes a settings instance built in the test file, and the collection carries `AddLogging()`
      so `ILogger<T>` resolves. The settings point at an address nothing answers, which is safe because
      no `Category=Unit` test issues a read — constructing `NpgsqlDataSource` opens no connection
- [x] the nine-argument `PostgresConnectionSettings` construction those tests need lives once, in
      `ConnectionSettingsFactory`. Three `Category=Unit` classes build settings — `ArchiveDataSourceTests`
      and `PostgresCompositionTests` here, `ArchiveExceptionMapperTests` in Task 4 — and a record of nine
      positional fields spelled out three times drifts field by field. The factory pins host and port at
      an address nothing answers and takes the source time zone as a parameter, which is the only field
      any caller varies
- [x] write tests: the container resolves the provider, the data source and the converter, and each
      registration is a singleton, extending the existing `PostgresCompositionTests`. The three
      not-implemented assertions at `:71`, `:83` and `:100` and the `Subscribe` assertion at `:112`
      keep both their shape and their parameterless `new PostgresDataProvider()`; all four call members
      that return before touching the data source, so none of them opens a connection
- [x] assert the converter through its behaviour, not through a zone member: `ArchiveTimeConverter`
      exposes none, so the test registers a settings instance with a known non-UTC zone and asserts
      that the resolved converter's `ToUtc` shifts by that zone's offset
- [x] run tests — must pass before Task 4

### Task 4: Map PostgreSQL failures onto the public error types

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveReadFailedError.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveExceptionMapperTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/MissingRelationProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs`

- [x] `ArchiveExceptionMapper` is a sealed class constructed with the `PostgresConnectionSettings` and
      an accessor for the data source's cached effective bound, exposing one synchronous method over
      the exception and an already-resolved missing-table name, required on the `42P01` path and
      ignored on every other. Constructed rather than static because the public types demand values no `PostgresException`
      carries: `Host`, `Port` and `Database` on every `Archive*` type, the username on
      `ArchiveAccessDeniedError`, and the `TimeSpan` bound on `ArchiveQueryTimedOutError`. It opens no
      connection, issues no query and returns no `Task`, which is what keeps its
      `[Trait("Category","Unit")]` honest — a unit test constructs it from a settings instance and a
      fixed bound
- [x] register the mapper and the probe in `AddPostgresData` as singletons beside the data source, and
      extend the composition test accordingly
- [x] map each SQLSTATE in the Technical Details table onto its public type, with the original
      riding `.CausedBy(...)` so the log keeps it and nothing internal crosses unmapped
- [x] `MissingRelationProbe` is a separate type owning the `to_regclass` query, and lives in the read
      path rather than in the mapper. It takes a fresh connection from the data source, passes
      `CancellationToken.None`, sets `CommandTimeout = 10` seconds, and returns which relation is
      absent, or `semiplot_tags` when neither does. Each of those three is load-bearing: the failed command's connection may be in an
      aborted transaction (`25P02`); from `postgres-history-read` on, the caller's token is often
      already cancelled when a read fails, and passing `None` makes that rule permanent rather than a
      property of which members happen to take a token; and `Command Timeout=0` would let the error
      path hang without bound
- [x] the probe never throws out: a failure inside it yields no name, the read supplies its own
      statement's relation instead, and the mapper is not re-entered for the probe's own exception
- [x] the probe answers `semiplot_tags` when neither relation resolves. Provisioning precedes
      commissioning, so the remedy there is `semibase create`; naming `trends` would send the operator
      to start a SCADA against a database SemiBase has not touched
- [x] when the probe answers nothing — both relations resolve, or the probe could not run — the read
      passes its statement's fallback relation, `semiplot_tags` for the catalogue read and `trends`
      for the extent read, because the read knows which relations its statement touches and the mapper
      does not. `ArchiveNotInitialisedError.Table` is non-nullable and its own summary tells consumers
      to route on it, so the mapper REQUIRES a relation on the `42P01` path and raises rather than
      guessing: every read supplies one, so an unnamed relation there is a caller defect
- [x] add `ArchiveReadFailedError` to `SemiPlot.Core/Data/Errors/`, carrying host, port, database and
      the SQLSTATE, as the fallback for anything the table does not name. Its operator sentence is
      distinct: the archive rejected the read for a reason this build does not recognise, and the
      SQLSTATE plus the log is what an engineer needs to name the cause. Nothing escapes as an
      exception and nothing crosses as an untyped `Result.Fail(string)`
- [x] an `OperationCanceledException` is rethrown rather than mapped: a cancelled operation is not a
      failed `Result` in .NET. Neither member this slice implements takes a `CancellationToken`, so
      nothing here can reach it, and telling a server-side `57014` from one this client asked for is
      `postgres-history-read`'s, the first slice to hand a token down
- [x] the five shipped `Archive*` types keep the fields they have; no revision is expected. The
      mapping in the Technical Details table fills every one of them — host, port and database from the
      settings on all five, the username on `ArchiveAccessDeniedError`, and the `TimeSpan` on
      `ArchiveQueryTimedOutError` from the data source's cached bound — which is precisely why the
      mapper is constructed rather than static. Should one field still prove unfillable, the scaffold
      plan declares these fields provisional so changing it is in scope, and
      `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs` moves with it in the same task: that file
      constructs all five by exact signature at `:58`, `:68`, `:80`, `:93` and `:106`
- [x] extend `EachArchiveStateStaysTellableApartThroughAFailedResult`
      (`SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs:145-161`) to `ArchiveReadFailedError`,
      so the new type is not the one `Archive*` type that test leaves out. `DataErrorTests` also
      carries `EveryPublicErrorTypeIsSealedAndDerivesFromError` (`:164-176`), which enumerates the
      namespace by reflection and picks the new type up with no edit — provided it is sealed,
      implements `IError` and its name ends in `Error`
- [x] write tests over constructed `PostgresException` instances for each mapped SQLSTATE, asserting
      by error type and structured field, including `42P01` with a supplied name and `42P01` with
      none, which is the caller defect that raises. `ArchiveExceptionMapperTests` carries all three
      traits — `Component=Core`, `Area=Data`, `Category=Unit`
- [x] write a test that an unmapped SQLSTATE produces a failed `Result` carrying
      `ArchiveReadFailedError` with that SQLSTATE, rather than escaping as an exception
- [x] `MissingRelationProbe.Resolve` is `internal` and `MissingRelationProbeTests` covers its four
      combinations, two of which no gated test can reach: both relations present, and neither. Running
      the probe needs a database and stays in Task 7; deciding what its two booleans mean is pure logic
- [x] a read that fails with no server answer behind it — a null reference or a bad cast inside the
      row read — is logged at Error with the full exception. It still crosses typed, because nothing
      may escape the boundary, but `ArchiveReadFailedError` alone would dress a fault in this code as
      a server state
- [x] run tests — must pass before Task 5

### Task 5: Read the pen catalogue

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/PenLineStyleReader.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/PenLineStyleReaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] `PostgresDataProvider` takes all five of its dependencies here, in one edit: the
      `ArchiveDataSource`, the `ArchiveTimeConverter`, the `ArchiveExceptionMapper`, the
      `MissingRelationProbe` and an `ILogger<PostgresDataProvider>`. The converter is Task 6's to use
      but is taken now, so the constructor is widened once rather than twice. That constructor is
      `internal`: `ArchiveExceptionMapper` and `MissingRelationProbe` are internal types by the
      Testing Strategy, and a public constructor taking an internal parameter is CS0051. The
      registration follows it — `AddPostgresData` registers `IDataProvider` through a factory lambda
      resolving the five services already registered by Tasks 3 and 4, not through type activation,
      because the container's constructor lookup finds only public constructors
- [x] update the three direct constructions the widening breaks in
      `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs` —
      `QueryHistoryAsyncFailsWithTheNotImplementedError` (`:83`),
      `QueryArchiveExtentAsyncFailsWithTheNotImplementedError` (`:100`) and
      `SubscribeCompletesImmediately` (`:112`). Each builds the five arguments over the same
      nothing-answers settings instance the rest of the class uses, and each still calls a member that
      returns before touching the data source, so none opens a connection. Task 6 deletes the extent
      one, leaving two
- [x] delete `QueryPensAsyncFailsWithTheNotImplementedError` (`:71`) from
      `PostgresCompositionTests`: this task implements the member, so the behaviour that test asserts no
      longer exists. `QueryHistoryAsyncFailsWithTheNotImplementedError` stays — `QueryHistoryAsync` is
      still `postgres-history-read`'s. No `Category=Unit` test is left calling `QueryPensAsync` on a
      provider that can open a connection; a survivor would attempt a real TCP connection and stall for
      the connect timeout before failing with the wrong error
- [x] `QueryPensAsync` runs the catalogue statement through the data source and reads rows onto `Pen`
      inline. Five columns onto a record is a private static method beside the read, not a type of
      its own; the gated test in Task 7 covers it against a real reader, which is the only place an
      `NpgsqlDataReader` can be had
- [x] the row read is total against SemiBase's DDL, not against the seeder's output: `id` is read with
      `GetInt32` and widened to `long`, because the column is `integer` and `GetInt64` throws
      `InvalidCastException` on it; `group_name` and `color` are nullable columns mapped onto non-null
      `Pen.Group` and `Pen.Color`, so a null in either coalesces to the empty string instead of throwing
      `SqlNullValueException` down the unmapped-exception path. Only `name` and `line_style` are
      `NOT NULL` in SemiBase v0.1.0's `sql/semiplot_tags.sql`
- [x] pin the enum ordinals: `Interpolated = 0, Stepped = 1` in
      `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs`, with a comment naming `semiplot_tags.line_style`
      as the reason. The stored value is the ordinal
      (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs:62`), the seeder's copy of the pen
      catalogue is deliberately frozen, and an unpinned declaration order would silently reinterpret
      every commissioned site's catalogue on a reorder
- [x] `PenLineStyleReader` converts `short` to `PenLineStyle` through an explicit switch, never a
      cast, so an added enum member forces a decision here instead of widening the accepted value set
      by accident
- [x] an unrecognised value takes the `Interpolated` default rather than failing the whole catalogue,
      because one malformed row must not hide every other pen — and is logged with the pen id, so
      the default is a recorded decision rather than a silent swallow. The reader takes the `ILogger`
      as a parameter rather than resolving one, which is what keeps its unit test free of a container
- [x] zero rows is a successful empty list, never a failure and never a crash
- [x] write tests for the conversion: `0` gives `Interpolated`, `1` gives `Stepped`, an out-of-range
      value gives `Interpolated`, and the ordinals are asserted numerically so a reorder in Core
      fails here. `PenLineStyleReaderTests` carries all three traits — `Component=Core`, `Area=Data`,
      `Category=Unit`
- [x] run tests — must pass before Task 6

### Task 6: Read the archive extent

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] delete `QueryArchiveExtentAsyncFailsWithTheNotImplementedError` from
      `PostgresCompositionTests`: this task implements the member. The `QueryHistoryAsync` and
      `Subscribe` cases stay with the five constructor arguments Task 5 gave them, and no
      `Category=Unit` test is left calling a member that opens a connection
- [x] `QueryArchiveExtentAsync` runs the extent statement and converts both bounds out of naive local
      time through the injected `ArchiveTimeConverter` Task 3 registered
- [x] a null row maps to `ArchiveExtent.Empty` and a successful `Result`, per
      `docs/architecture/data-integration.md:107`. This is also what an empty `semiplot_tags` over a
      full archive produces, since the statement is rooted at the catalogue
- [x] rewrite `PostgresDataProvider`'s class summary at `:11-15`, which describes a scaffold: every
      `Result`-returning member failing with `ProviderNotImplementedError`, and later slices replacing
      one body at a time. Both halves are false once this task lands — two of the three members read
      real data. The summary names what the type does and leaves `QueryHistoryAsync` as the one member
      `postgres-history-read` still owns
- [x] this task adds no unit test, and that is the decision rather than a gap. The outward conversion
      is already pinned directly over `ArchiveTimeConverter` by
      `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveTimeConverterTests.cs:31-40` (Europe/Berlin summer,
      noon local to 10:00 UTC) and `:43-52` (winter, noon local to 11:00 UTC), so a further copy of
      that assertion would add no coverage. What is left to prove is that the provider applies the
      converter exactly once and in the right direction, which is only observable from outside the
      provider — for the same reason the row read has none, that `NpgsqlDataReader` is not
      constructible outside a database. Task 7's extent-versus-seeder-timestamps assertion under a
      non-UTC zone is this task's coverage
- [x] run tests — must pass before Task 7

### Task 7: Assert both reads against real archive states

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveProviderFactory.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveReadSupport.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresCatalogReadTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresExtentReadTests.cs`

This task adds ten of the slice's eleven gated tests. Named, so the skip count in Task 9 can be
checked rather than inferred:

| Class | Test |
| --- | --- |
| `PostgresCatalogReadTests` | `SeededCatalogueReadsEveryPenOrderedByGroupThenName` |
| `PostgresCatalogReadTests` | `SeededCatalogueLineStylesReadBackAsTheStoredOrdinals` |
| `PostgresCatalogReadTests` | `ANullGroupNameAndColourReadAsEmptyStrings` |
| `PostgresCatalogReadTests` | `AnEmptiedCatalogueIsASuccessfulEmptyList` |
| `PostgresCatalogReadTests` | `ADroppedCatalogueFailsNamingSemiplotTags` |
| `PostgresExtentReadTests` | `TheSeededExtentMatchesTheSeedersFirstAndLastTimestamps` |
| `PostgresExtentReadTests` | `AnEmptiedCatalogueYieldsAnEmptyExtent` |
| `PostgresExtentReadTests` | `AnEmptyTrendsTableYieldsAnEmptyExtent` |
| `PostgresExtentReadTests` | `ADroppedCatalogueFailsNamingSemiplotTagsNotTheFallback` |
| `PostgresExtentReadTests` | `AProvisionedButUnseededDatabaseFailsNamingTrends` |

- [x] use the harness types in `SemiPlot/SemiPlot.Tests.Data/Integration/` unchanged — container,
      provisioning, template cloning, skip policy and traits are `archive-populator`'s and none of
      those files is edited. The setup each state needs is test-local and lives in the two new classes
- [x] non-destructive reads share the class's `SeededArchive` clone. Destructive states take a
      private clone from `PostgresContainerFixture.CloneTemplateAsync`, disposed at the end of the
      test: `SeededArchive` is one clone per test class and its header (`:5-7`) binds every test in
      the class to leaving the database as it found it, which a `DROP TABLE semiplot_tags` cannot
      honour and which would destroy the object the sibling tests read
- [x] one shared helper builds every test's provider, in `ArchiveProviderFactory`. It parses an
      `ArchiveDatabase` connection string through `NpgsqlConnectionStringBuilder` for host, port,
      database, username and password, and fills the remaining `PostgresConnectionSettings` fields
      itself. `Schema` is pinned to `public` and is the reason the helper exists rather than each test
      building its own nine-field record: `Schema` becomes `SearchPath` on the connection string
      (`PostgresConnectionSettings.cs:40`), the harness's own connection strings set no `SearchPath`
      (`PostgresServer.ConnectionStringFor:35-47`), and a wrong value makes `semiplot_tags`
      unresolvable — turning every catalogue test into a `42P01` that reads exactly like a correctly
      detected missing table. One wrong constant in one place would then green the drop tests while
      silently breaking the read tests
- [x] the helper builds the provider through `AddPostgresData` over those settings rather than calling
      the five-argument constructor, so every gated test runs the real registration and no test file
      repeats the argument list. The collection carries `AddLogging()`, from the
      `Microsoft.Extensions.Logging` reference Task 3 adds to the test project. It hands back the
      `ServiceProvider` for the test to dispose, which
      returns the pooled connections before `ArchiveDatabase.DisposeAsync` drops the database — a
      pooled connection makes `DROP DATABASE` refuse
- [x] what the two read-test classes share beyond the provider lives in `ArchiveReadSupport`: the two
      statements that drive the catalogue into its emptied and its dropped state, and a helper that
      renders a failed `Result`'s messages so an assertion failure names the archive state rather than
      only the expectation it broke. Both classes reach both states, so the statements belong there
      rather than in whichever file happened to need them first
- [x] configure every test's provider with a NON-UTC `SourceTimeZone`, which the helper takes as a
      parameter. Under UTC the time boundary is invisible: an unconverted, doubly converted and
      correctly converted extent all read the same, and the extent-versus-seeder-timestamps assertion
      below is what proves the conversion is applied exactly once. The seeder writes naive local
      values, so the expected UTC bounds are the seeder's raw timestamps put through
      `ArchiveTimeConverter.ToUtc` under that same zone
- [x] `SeededCatalogueReadsEveryPenOrderedByGroupThenName` reads the catalogue as `semiplot_reader`
      against the seeded database and asserts the seeded pens with their names, groups and colours,
      ordered by group then name. Reading as the reader role also proves this slice's own connection
      wiring: `semiplot_reader` holds `SELECT` on `semiplot_tags` and `SeededArchiveTests.cs:153`
      already exercises it, so a `42501` here is a role or connection-string fault in this slice, never
      a mapper bug and never a missing grant
- [x] `SeededCatalogueLineStylesReadBackAsTheStoredOrdinals` is a test of its own rather than an
      assertion inside the one above: it confirms `line_style` reads back as the `smallint` ordinals
      `0` and `1` that `TagCatalogWriter.cs:62` writes, which is the evidence Task 5 built the mapping
      from. Keeping it separate means a reordered `PenLineStyle` fails one named test with an
      unambiguous cause rather than one clause inside a five-column comparison
- [x] `ANullGroupNameAndColourReadAsEmptyStrings` asserts a row with NULL `group_name` and NULL
      `color` still reads, on a private clone: insert it over `ArchiveDatabase.AdminConnectionString`
      and assert the catalogue returns every pen,
      including that one with empty-string `Group` and `Color`. Both columns are nullable in SemiBase
      v0.1.0 and the bench seeder always fills them, so no other test in this plan reaches the null —
      and one malformed row must not hide every other pen, which is the same rule the line-style
      fallback lives by
- [x] `AnEmptiedCatalogueIsASuccessfulEmptyList` asserts an emptied `semiplot_tags` yields a successful
      empty catalogue, on a private clone
- [x] `ADroppedCatalogueFailsNamingSemiplotTags` asserts a dropped `semiplot_tags` yields
      `ArchiveNotInitialisedError` with `Table` naming `semiplot_tags`, on a private clone of its own
- [x] `TheSeededExtentMatchesTheSeedersFirstAndLastTimestamps` asserts the extent matches the seeder's
      own first and last raw timestamps, converted through the configured non-UTC zone
- [x] `ADroppedCatalogueFailsNamingSemiplotTagsNotTheFallback` runs the EXTENT read against a private
      clone whose `semiplot_tags` has been dropped and asserts `ArchiveNotInitialisedError` with
      `Table` naming `semiplot_tags`. This is the one state in which `MissingRelationProbe` and a
      hardcoded constant give different answers, and therefore the only test that can fail when the
      probe is removed: the extent statement's fallback relation is `trends`, so a build that skipped
      the probe would report `trends` here. Every other gated case agrees with its statement's
      fallback — the unseeded database answers `trends`, which is the extent read's fallback, and the
      dropped-catalogue case above goes through the catalogue read, whose fallback is `semiplot_tags`.
      It is also provisioning state 3 from `postgres-instance.md:85`, `trends` without
      `semiplot_tags`, which the Context claims this slice survives
- [x] `AnEmptiedCatalogueYieldsAnEmptyExtent` asserts that an emptied `semiplot_tags` over the
      otherwise full archive yields
      `ArchiveExtent.Empty` with a successful `Result`: the extent is the span of the configured
      variables, not of the archive. This takes a private clone of its own rather than sharing the
      catalogue test's, because a private clone is per-test and the two assertions live in different
      test classes, which no clone can span
- [x] `AnEmptyTrendsTableYieldsAnEmptyExtent` asserts that `trends` present with zero rows yields
      `ArchiveExtent.Empty` with a successful `Result`.
      This is the null-from-no-rows state `data-integration.md:119` describes, and it is
      distinct from the emptied-catalogue case above, which is null-from-no-configured-variables. Build
      it on a private clone with `TRUNCATE public.trends` over `ArchiveDatabase.AdminConnectionString`:
      cheap, no subprocess, and `semiplot_tags` stays populated so the statement's outer relation still
      has rows to laterally join against
- [x] `AProvisionedButUnseededDatabaseFailsNamingTrends` asserts a provisioned but unseeded database
      yields a FAILED `Result` carrying `ArchiveNotInitialisedError` with `Table` naming `trends`.
      `PostgresContainerFixture.CreateEmptyDatabaseAsync` gives a `template0` database with no schema
      at all, followed by `SemibaseProvisioner.CreateAsync`, which provisions the roles, the grants and
      `semiplot_tags` and nothing else — `trends` is the SCADA's and in the bench is created by the
      seeder. So the extent statement raises `42P01` at parse analysis, the probe resolves `trends` as
      the absent relation, and the read reports it. The `semibase` subprocess is the
      slowest step in this task and is why it costs more than the clone-based cases
- [x] both classes carry all three traits — `Component=Core`, `Area=Data`, `Category=Integration` —
      and every test skips through `DatabaseGate` with a stated reason when no runtime answers
- [x] run tests — must pass before Task 8

### Task 8: Assert the extent query reaches its rows through an index

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ExplainPlanTests.cs`

This task adds the eleventh and last gated test:
`ExplainPlanTests.TheExtentPlanReachesEveryRowHoldingPartitionThroughAnIndex`.

- [x] run `ANALYZE` over `ArchiveDatabase.AdminConnectionString` before the `EXPLAIN`. This test can
      share the class's `SeededArchive` clone, since `ANALYZE` and `EXPLAIN` leak no rows and honour
      the leave-it-as-you-found-it contract that binds every test in the class.
      The template is built by `COPY` and never analysed
      (`SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs:99-125`), and autovacuum has not
      run seconds after `CREATE DATABASE … TEMPLATE`; with no statistics the planner may pick a
      sequential scan over a one-day partition and the test fails for a reason unrelated to query
      shape. `ANALYZE` needs table ownership or `MAINTAIN`, neither of which `semiplot_reader` holds,
      so the admin connection is not a convenience here but the only role that can run it
- [x] run `EXPLAIN` over the extent statement — taken from `ArchiveStatements`, which Task 2's
      `InternalsVisibleTo` makes reachable — and assert the plan SHAPE: an `Index Scan` or `Index Only
      Scan` under each per-variable subquery, and no `Seq Scan` over a `trends` partition that holds
      rows, meaning the day partitions `tpYYYYmMMdDD`. `tpdefault` is excluded from that assertion:
      it is empty by design (`sql/semiplot_dev.sql:9-10`) and the planner may legitimately pick a
      sequential scan of an empty analysed partition, so covering it would fail a correct plan
- [x] do not assert an index name. `trends` is `PARTITION BY RANGE (t)` and `tpk` is the parent
      partitioned index (`sql/semiplot_dev.sql:12-24`), which is never scanned; each partition carries
      a cloned index named `<partition>_pkey`, and `EXPLAIN` prints the leaf. The shape assertion
      survives partition renaming and still fails the moment the per-variable bounds are dropped
- [x] state in the test what the failure means: the per-variable bounded subqueries have been lost and
      the query now scans the whole archive
- [x] gated and skipping like the other integration tests, with all three traits —
      `Component=Core`, `Area=Data`, `Category=Integration`
- [x] run tests — must pass before Task 9

### Task 9: Verify acceptance criteria

**Files:** none. This task changes no file; it runs commands and reads their output.

- [x] every check in Acceptance Evidence runs and produces its stated result
- [x] `dotnet test SemiPlot.slnx` — zero failures across both projects
- [x] with no container runtime the eleven gated tests named in Tasks 7 and 8 skip with a stated reason
      and none passes, so `SemiPlot.Tests.Data`'s skip count grows by exactly eleven; with
      `SEMIPLOT_REQUIRE_DB=1` the same eleven fail instead. Check the count against the names, not
      against the number: ten in `PostgresCatalogReadTests` and `PostgresExtentReadTests`, one in
      `ExplainPlanTests`
- [x] `git diff --name-only master...HEAD` lists no file under `SemiPlot/SemiPlot.UI/` other than
      `Minimap/MinimapViewModel.cs`, and none under `SemiPlot/SemiPlot.DataSource.Stub/`
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` reports no changes, and every new `.cs` file
      starts `ef bb bf`

### Task 10: Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/postgres-instance.md`
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
- Modify: `docs/plans/20260619-simplescada-postgres-provider.md`
- Move: `docs/plans/20260818-postgres-catalog-and-extent.md` to `docs/plans/completed/`

- [x] `docs/architecture/data-integration.md` — split the `semiplot_tags` row of the error-semantics
      table into two (`:292-293`): a missing table is a failed `Result` carrying
      `ArchiveNotInitialisedError` with `Table`, and an empty table is a success with an empty pen
      list. Delete the open-question paragraph that followed and state the settled split in its
      place (`:312-320`). Record `ArchiveExtent.Empty` in the DTO
      table and beside the extent statement
- [x] `docs/architecture/data-integration.md:322-332` — add `ArchiveReadFailedError` to the error-type
      field table as the fallback row: fields host, port, database, SQLSTATE; operator sentence "the
      archive rejected the read for a reason this build does not recognise". Update the paragraph below
      it (`:334-342`) that counts "the five `Archive*` types" — there are six once this row lands. The
      other five rows keep the fields they list, since Task 4 changes none; should Task 4 have changed
      one after all, it moves here in the same edit, because that table is the only place host, port
      and database are documented per type
- [x] `docs/architecture/data-integration.md:78-79` and `:388-390` — scope the no-SQL-anywhere-else
      claim in BOTH places it is made, in one edit. `:78-79` says "No SQL exists anywhere else in the
      solution" and `:388-390`, in the drift-prevention list, says "Nothing else in the solution issues
      SQL". Neither is true as written: the bench seeder owns the schema resource, the partition DDL,
      the `COPY` and the catalogue upsert, and the gated harness owns `CREATE DATABASE` and
      `DROP DATABASE`. The rule is about the application and provider path, and saying so is what makes
      it enforceable. Scoping only one leaves the other contradicting it on the same page
- [x] `docs/architecture/data-integration.md:111-117` — correct the cost claim. `trends` is
      partitioned on `t` and the extent statement carries no `t` predicate, so no partition is pruned
      and each bounded subquery becomes a `MergeAppend` over one index scan per partition. It does
      not "walk the index to its edge" once; it walks one edge per partition. Still far cheaper than
      the unbounded form, which reads every row of every partition
- [x] `docs/architecture/data-integration.md:393-401` — correct the enforced invariant. `tpk` is the
      parent partitioned index of a `PARTITION BY RANGE (t)` table and is never scanned, so `EXPLAIN`
      never names it; the gated tests assert the plan shape instead — an index scan under the bounded
      subqueries and no sequential scan of a `trends` partition holding rows
- [x] `docs/architecture/data-integration.md` — beside the extent statement, state that the extent is
      the span of the CONFIGURED VARIABLES and not of the archive: the statement is rooted at
      `semiplot_tags`, so an empty catalogue over a full archive yields an empty extent, which is the
      intended behaviour because with no configured variables there is nothing to draw
- [x] `docs/architecture/postgres-instance.md` — the `semiplot_tags` section says an empty table is a
      successful read and an absent one a typed error carrying the table name, both normal states,
      never a crash (`:67-72`). The four-states list at `:83-86` is already correct and does not change
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:245-256` — correct this slice's
      entry to the settled split
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:258` — this slice's **Blast
      radius** is not "the provider only". It also changes `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs`
      and `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs`, and `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`
      follows the extent's new empty form. State those three beside "the application still runs on the
      stub", which remains true
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:263` — narrow the **Scope guard**
      rather than delete it. "no composition changes" is falsified as written: `AddPostgresData` takes
      `PostgresConnectionSettings` and gains registrations for the data source, the converter, the
      mapper and the probe. What the guard means here is that the APPLICATION's composition root is not
      touched and still selects the stub, and that is what it should say. "no history queries, no
      realtime" stays as it is
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:269-270` — the single class owning
      every SQL statement in the solution is introduced by this slice, not by `postgres-history-read`.
      Amend that entry to inherit the class and add the windowed statement to it
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:274-275` — `postgres-history-read`
      asserts through `EXPLAIN` that the query reaches its rows through an index and scans no
      row-holding `trends` partition sequentially, not that the plan names the primary key. Same reason
      as the `data-integration.md` correction above
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:107-109` — the **Guard strategy**
      `EXPLAIN` bullet promises that the gated tests assert the windowed history query and the realtime
      poll "use the `tpk` primary key". `tpk` is the parent partitioned index and is never scanned, so
      no `EXPLAIN` can name it. Correct it to the plan-shape assertion the same way, or the roadmap
      contradicts itself in two places once this slice merges
- [x] `docs/plans/20260619-simplescada-postgres-provider.md` — the same correction wherever that plan
      claims `EXPLAIN` names `tpk`: its integration-test summary and its realtime-poll task bullet both
      state the plan-shape assertion instead, and its own known-wrong list records why. `tpk` is the
      parent partitioned index of a `PARTITION BY RANGE (t)` table and is never scanned, so the plan
      names each partition's cloned `<partition>_pkey` and no `EXPLAIN` can produce the claimed name.
      That document is live rather than archived — `docs/plans/backlog.md:12` points at it as the
      provider's plan — so leaving it would have the repository still asserting an invariant no
      `EXPLAIN` can produce, in the place a reader arriving from the backlog looks first
- [x] `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:352-360` — strike the empty pen
      catalogue from the states `postgres-startup-and-composition`'s probe distinguishes by public
      error type, and name what replaces it in the same edit: that slice surfaces the empty catalogue
      as a SUCCESS-CHANNEL UI state with a named test of its own, outside the reflection coverage
      guard. Without the replacement, striking the state leaves it with no error type, no mention and
      no rule requiring a screen, which converts `postgres-instance.md:67-72`'s "normal states with
      their own message" into a state nothing can force and nothing can see
- [x] run tests — must pass. This task edits `docs/architecture/data-integration.md`, which
      `ArchiveStatementTextTests` reads at run time and compares character for character, including the
      `ArchiveExtent.Empty` note this task writes beside the extent statement. Task 9's green run is
      before those edits, so without a run here a reflowed fenced `sql` block, or a second `sql` fence
      under either heading, would break the suite after the last verification
- [x] move this plan to `docs/plans/completed/` — deferred to the delivery step. Archiving records a
      completion the operator has not confirmed yet, and moving the file mid-run breaks every review
      phase that reads it by path, so the move happens after the branch is tested and shipped

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** None specific to this slice: the application still runs on the stub and
nothing user-visible changes. The `ArchiveExtent` change touches `MinimapViewModel`, so a start of the
application confirming the minimap still draws its strip and labels is worth one minute.

**What the following slices inherit.** `postgres-history-read` gets the data source and its
connection-plus-command seam, the statement holder, the exception mapper, the missing-relation probe
and the timestamp conversion, and adds the windowed history statement to the same class, binding
`@ids`, `@layer`, `@from` and `@to` through that seam. It also inherits the statement-pinning test,
which reads the fenced block out of `data-integration.md`, and extends it to the new statement and its
parameter names. It is also the first slice to hand a `CancellationToken` down, so the `57014`-versus-own-token
distinction and the probe's `CancellationToken.None` rule first get a
real referent there, and it is where the mapper's rethrow of `OperationCanceledException` first
meets a token. `postgres-startup-and-composition` inherits the obligation to give the empty pen
catalogue a success-channel UI state with a named test, which Task 10 writes into its roadmap entry,
and inherits `ArchiveReadFailedError` as one more public type its reflection coverage guard must map
to a UI state.
The keepalive question the scaffold handed to this slice is answered in Task 3 — no `Keepalive` is
set, because the pool discards an idle physical connection well before any plant middlebox would.
`postgres-realtime-poll` is the slice that reopens it, being the first to hold a connection across a
poll interval.

**Cancellation belongs to `postgres-history-read`.** Neither read in this slice takes a
`CancellationToken`, so the mapper carries no cancellation branch: an `OperationCanceledException`
is rethrown rather than mapped, because a cancelled operation is not a failed `Result` in .NET, and
a self-cancelled read is not an error at all. What that leaves open is the `57014` split — the server
answers the same SQLSTATE for its own `statement_timeout` and for a cancel this client asked for, and
only a caller holding the token can tell them apart. The first slice whose members take a token owns
that distinction, along with the probe's `CancellationToken.None` rule.

**`App.axaml.cs`'s `LoadPens` does not check `IsFailed`.** It reads the catalogue once at startup and
lets a failed `Result` throw, so the process fails to start instead of showing the "no connection to
the archive" state. Real, and outside this slice: the composition root belongs to
`postgres-startup-and-composition`, and this slice's scope guard forbids touching it.

**The anticipatory error types have now met real SQLSTATEs.** Their fields are the real contract
rather than a guess. The remaining unraised types are `ProviderNotImplementedError`, deleted by
`postgres-realtime-poll`, and any type no query in this slice can produce.

**Remaining slices**

After this slice the roadmap continues with: postgres-history-read, postgres-bucketed-read,
postgres-gap-reconstruction, postgres-realtime-poll, postgres-startup-and-composition,
live-demo-and-stub-retirement.

**Executed by exec:**

- branch: postgres-catalog-and-extent

## Verify it yourself

The two reads are the slice, and neither can be exercised without a database. That shapes every check
below: the first four are the ones that matter and they run only where a container runtime answers,
which on this branch means the pull request's `data-tests` job. The rest are runnable anywhere.

1. **The gated tests have never executed. Watch the CI job, not a local run.**
   `.github/workflows/ci.yml`'s `data-tests` job runs on `pull_request` with `SEMIPLOT_REQUIRE_DB=1`
   on Ubuntu, and this branch touches `SemiPlot/**`, so that run is the first time the catalogue read,
   the extent read, the exception mapping and the `EXPLAIN` shape meet a real PostgreSQL. Locally they
   skip with a stated reason; under `SEMIPLOT_REQUIRE_DB=1` they fail for want of a runtime, which is
   the gate working rather than the tests failing.

2. **The gate cannot report green over a suite that ran nothing.**
   `dotnet test SemiPlot/SemiPlot.Tests.Data` reports 330 passed and 35 skipped, the skips carrying a
   stated reason. The same command with `SEMIPLOT_REQUIRE_DB=1` fails 35 instead. Filtered to this
   slice's three new classes, that pair is 11 skipped against 11 failed.

3. **The pinned statements match the architecture document, and the check is real.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StatementText"` reads the fenced `sql`
   blocks out of `docs/architecture/data-integration.md` at run time and compares them character for
   character. Change `ORDER BY coalesce(group_name, ''), name` in the constant alone and it fails;
   change it in the document alone and it fails. A test comparing the constant to a literal in the
   test file could do neither, which is why it reads the document.

4. **The empty extent cannot be mistaken for a real one.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ArchiveExtent"` — `IsEmpty` is derived from
   the two bounds rather than stored, so `ArchiveExtent.Empty with { }` stays empty. A stored flag
   rides a record's copy constructor, which runs before `with` applies its initializers, and produced
   an extent spanning year 0001 that the minimap would have drawn as real.

5. **The application is untouched.**
   `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` reports 257, one more than `master`,
   and that one is the minimap's empty-extent case. `git diff --name-only master...HEAD` lists exactly
   one file under `SemiPlot/SemiPlot.UI/` — `Minimap/MinimapViewModel.cs` — and nothing under
   `SemiPlot/SemiPlot.DataSource.Stub/`. The composition root still selects the stub.

6. **The format gate, and the one thing it does not check.**
   `dotnet format SemiPlot.slnx --verify-no-changes` exits 0. It does not check the UTF-8 BOM
   `.editorconfig` requires on `.cs` files, so a scripted edit can drift encoding past it:
   `head -c 3 <file> | od -An -tx1` must show `ef bb bf` for a `.cs` file and must not for a `.md`.
