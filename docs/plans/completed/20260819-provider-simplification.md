# Provider simplification: move the statement-timeout read onto the failure path

## Overview

`ArchiveDataSource` carries the most intricate code in the provider project. A physical-connection
initializer runs on every physical connection open, reads `pg_settings.statement_timeout`, parses it,
stores it in an interlocked tick field and warns when the value changes
(`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs:50,125-149`). Registering the
initializer also forces a synchronous counterpart that exists only to throw
(`ArchiveDataSource.cs:110-115`), because Npgsql requires both versions.

That apparatus serves two purposes, and they are coupled through one field:

1. **Truthful reporting.** `ArchiveQueryTimedOutError.Timeout` carries the bound the server actually
   applied. `ArchiveExceptionMapper` fills it from the cached field through a `Func<TimeSpan?>`
   (`ArchiveExceptionMapper.cs:29,107-111`). The number matters because `statement_timeout` is a
   `USERSET` GUC and SemiPlot never sends one, so the effective value is the reader role's and varies
   per site (`docs/architecture/postgres-instance.md:43-46`).
2. **An unambiguous client timeout.** `ResolveCommandTimeoutSeconds` sets each command's bound one
   30 s margin above the server's (`ArchiveDataSource.cs:30,154-164`), so a client `TimeoutException`
   can only mean the server stopped answering — which is why it maps to `ArchiveUnreachableError` and
   never to `ArchiveQueryTimedOutError` (`ArchiveExceptionMapper.cs:17,22-23,62,83`).

This slice keeps the first purpose and drops the second, replacing the derived bound with a fixed
one. The bound is read lazily, on the `57014` path only, so the reported number stays true while the
initializer, the cache, the parse, the warn-on-change arm and the synchronous throw-stub all go.

The slice also corrects the shipped comments that state something false once the mechanism moves, and
two that state something false today.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice provider-simplification

Files involved:

- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs` (165 lines) — the class summary at
  `:11-23`, `InitializerCommandTimeoutSeconds` at `:26-28`, the margin and fallback constants at
  `:30-31`, the `_logger` field at `:34`, the interlocked field at `:39`, the constructor at
  `:41-53` and the initializer registration it makes at `:50`, the
  `EffectiveStatementTimeout` property at `:60-68`, the `OpenConnectionAsync` doc at `:70-74`,
  `CreateCommand` at `:84-95`, `ThrowOnSynchronousOpen` at `:110-115`, `ParseMilliseconds` at
  `:117-123`, `CacheEffectiveStatementTimeoutAsync` at `:125-133`, `CacheEffectiveStatementTimeout`
  at `:137-149`, and `ResolveCommandTimeoutSeconds` at `:154-164` with its fallback branch at
  `:158-161`
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs` — the summary at `:17`, the
  `Func<TimeSpan?>` field and constructor at `:29,31-37`, and the `57014` arm at `:107-111`
- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs` — the `EffectiveStatementTimeout`
  statement, doc at `:52-58` and constant at `:59-61`
- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:34-52` — the internal constructor
- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:25,31,36-41` —
  the probe registration, the `Func<TimeSpan?>` wiring and the provider factory
- `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveQueryTimedOutError.cs` — its XML contract says the value
  is "read back from that session"; `Describe` at `:29-35` renders the number into the operator
  sentence
- `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveDataSourceTests.cs:21-85` — four tests, all over the
  cache-and-derive behaviour this slice removes; the settings strings are `InlineData` at `:22-25`
  and `:44-47`
- `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs:28-36,162-170` — constructs the
  mapper directly at `:33` and asserts the unset bound at `:169`
- `docs/architecture/data-integration.md:437,477-479` and `docs/architecture/postgres-instance.md:46`
  — the three places that describe the eager read-back

Related patterns found:

- **The provider already has an async mapping seam on the failure path.** `PostgresDataProvider`
  calls `await MapAsync(exception, <fallback relation>)`, which is where `MissingRelationProbe` runs
  its own cold-path round trip (`MissingRelationProbe.cs:16,27`). A lazy timeout read is the same
  shape and belongs in the same place, for the same stated reason: keeping the round trip out of
  `ArchiveExceptionMapper` preserves the mapper as a synchronous, unit-testable pure mapping
  (`MissingRelationProbe.cs:10-14`).
- `ArchiveDataSourceTests.cs:11-13` records that the resolved `CommandTimeout` is readable without a
  server, because constructing an `NpgsqlDataSource` opens nothing and `CreateCommand` works on a
  closed connection. The replacement bound stays testable the same way.
- `PostgresCompositionTests.cs:162-164` records that an unset bound and a zero bound are deliberately
  distinguishable, "because only the second means the server bounds nothing". This slice preserves
  that distinction inside the reader and collapses it only in the operator sentence, where neither
  case has a number to report.

Dependencies identified: none outside `SemiPlot.DataSource.Postgres` and its tests. No application
code reads any of it — `PostgresDataProvider` is registered but not resolved by the composition root.

## Development Approach

- **testing approach**: Regular (code first, then tests), matching the repository's existing slices.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** — no exceptions. Tasks 1 to 3 change
  constructors that tests construct directly, so each task's Files block names every call site it
  breaks; a task that leaves the test project uncompilable has not met this bar.
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- **compatibility**: no public Core API changes. `ArchiveQueryTimedOutError` keeps its four fields and
  its constructor. The internal constructors of `ArchiveExceptionMapper` and `PostgresDataProvider`
  change deliberately, and every call site moves with them. `ArchiveDataSource` is public and its
  constructor changes too, losing the `ILogger<ArchiveDataSource>` parameter once the type logs
  nothing; the container resolves it by type, so the call sites that move are inside
  `SemiPlot.Tests.Data`.

## Testing Strategy

- **unit tests**: required for every task. `SemiPlot.Tests.Data` uses raw xunit `Assert.` and
  references no assertion library; every test class carries `[Trait("Component", …)]`,
  `[Trait("Area", …)]` and `[Trait("Category", …)]`.
- **e2e tests**: the project has none, and this slice touches no UI. Not applicable.
- **gated integration tests**: Evidence 1 needs a container runtime and the `semibase` binary, or an
  existing server through `SEMIPLOT_TEST_PG`. Without one it skips with a stated reason. It first
  executed locally in Task 7, on a Docker daemon holding `postgres:17-alpine` with `semibase` v0.1.0
  on `PATH`; the pull request's `data-tests` job, which sets `SEMIPLOT_REQUIRE_DB=1`, runs it again.

## Acceptance Evidence

The defect this slice addresses is complexity, not a behavioural fault, so Evidence 1 is an
**invariant-preservation** test: it passes before and after, and its job is to prove the operator-visible
guarantee survives the deletion. The differential proof that the mechanism actually moved is
Evidence 2 and Evidence 4, which cannot pass before the change.

**Evidence 1 — the reported bound is the server's own, measured against a real server.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TimedOutReadReportsTheServersOwnBound"
```

It clones the template, sets a bound on the reader role scoped to that clone only, runs a read that
exceeds it, and asserts `ArchiveQueryTimedOutError.Timeout` equals the bound set — not the client
backstop, and not zero.

**The bound has to sit inside a bracket**, because the lazy reader opens a session of the same reader
role and therefore runs under the same bound: above the reader's own `pg_settings` read, and below
the forced read. `SELECT setting FROM pg_settings WHERE name = 'statement_timeout'` materialises
`pg_show_all_settings()`, a few hundred rows, so the floor is not as low as it looks. The forced read
is therefore `QueryHistoryAsync` over the full seeded day at the raw layer for every pen, which
crosses 229 862 rows (`docs/plans/completed/20260810-archive-populator.md:958`), not the extent read.
Set the bound at 50 ms. Measured in Task 7, that is about twelve times above the settings read
(4.246 ms cold, 1.594 to 1.811 ms warm) and about six times below the full day (548.4 ms cold,
327.2 to 343.2 ms warm): the bracket opens, with a narrower upper end than an order-of-magnitude
claim would suggest.

Fact, not assumption: `ALTER ROLE <reader> IN DATABASE <clone> SET statement_timeout` is permitted —
the fixture's admin connection is the superuser
(`SemiPlot/SemiPlot.Tests.Data/Integration/PostgresServer.cs:12,22`) and `statement_timeout` is a
`USERSET` GUC. A database-scoped `ALTER DATABASE … SET` is **not** an alternative: `semibase create`
sets the 30 s bound on the role (`docs/architecture/postgres-instance.md:42`), and PostgreSQL applies
database settings before role settings, so the role default would win and the read would never trip.
`SET statement_timeout` on the reader's own session is also unavailable — SemiPlot sending no
`statement_timeout` in any form is a contract (`postgres-instance.md:45`,
`data-integration.md:476-477`), and the test drives SemiPlot's own code.

ASSUMPTION: the full-day raw read exceeds 50 ms and the settings read stays under it. Measure both
against the template at implementation time and record the two numbers in Task 4. If the bracket does
not open, the forcing mechanism changes — never the assertion.

**Evidence 2 — the machinery is gone.** Both return no hit. They are scoped to `.cs` because
completed plans legitimately record the shipped mechanism in prose:

```powershell
git grep -n "UsePhysicalConnectionInitializer" -- "*.cs"
git grep -n "EffectiveStatementTimeout" -- "SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs"
```

**Evidence 3 — the client backstop is fixed and still applied.** `ArchiveDataSourceTests` asserts,
without a server, that a command built by `CreateCommand` carries the fixed bound in seconds.

**Evidence 4 — the mapper no longer depends on the data source's state.**
`ArchiveExceptionMapper`'s constructor takes no `Func<TimeSpan?>`; the compiler enforces it.

**Evidence 5 — the suite does not regress.** `dotnet test SemiPlot.slnx` reports zero failures.
Measured at `2a51fbf`, the branch point: `SemiPlot.Tests` 290 passed / 0 skipped,
`SemiPlot.Tests.Data` 347 passed / 42 skipped. The totals move with the tests this slice adds and
removes; zero failures is the invariant, not the count.

**Evidence 6 — format and encoding.** `dotnet format SemiPlot.slnx --verify-no-changes` exits 0, and
every tracked `.cs` file still begins `ef bb bf`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

**The bound is read when it is needed, which is only when a read has already failed.** `57014`
arrives at the provider's failure path, which is already asynchronous and already performs one
cold-path round trip for `MissingRelationProbe`. A second reader of the same shape fills the timeout
number there. Nothing is read on any successful path.

**The client backstop becomes a fixed five minutes.** This is a behaviour change to the common case,
not a generalisation of an existing one: on a provisioned site the current fallback branch
(`ArchiveDataSource.cs:158-161`) never runs, because the reader role carries 30 s and every command is
built after the initializer has read it. The fixed bound is the value that branch already names, and
the slice makes it universal.

**What that trades away.**

- **A hung server takes five minutes to surface instead of ten seconds on the first read.** The
  initializer command carries its own 10 s bound (`ArchiveDataSource.cs:26-28`), so a server that
  accepts TCP but never answers a query fails the first physical open in about ten seconds today.
  After this slice the first read waits for the fixed backstop. A refused or unroutable host is
  unaffected: Npgsql's connect `Timeout` stays at its 15 s default.
- **Dead-server detection after the first read stretches from about 60 s to five minutes**, for the
  case of a server that goes silent mid-statement.
- **`postgres-startup-and-composition` inherits that worst case.** Its startup probe is a read, so a
  hung-but-accepting server leaves the application with nothing to show for up to five minutes. That
  slice needs its own bound or cancellation token for the probe; this slice hands it the constraint
  rather than solving it.
- **The client bound is no longer guaranteed above the server's.** On a site whose role bound exceeds
  five minutes the two cancels race. Npgsql answers a `CommandTimeout` by sending a cancellation
  request to the backend, so that path could surface as a `PostgresException` carrying `57014` rather
  than as a `TimeoutException` — and the error would then report the server's bound for a read the
  client killed. It does not: verified against Npgsql 10.0.3 (`SemiPlot/Directory.Packages.props:23`)
  and `postgres:17-alpine`, a client `CommandTimeout` surfaces as an `NpgsqlException` wrapping a
  `TimeoutException`, with the message "Exception while reading from stream", and never as a
  `PostgresException` carrying `57014`. `ArchiveExceptionMapper.IsConnectionFailure` therefore routes
  it to `ArchiveUnreachableError`, so the raced read is reported as unreachable rather than with a
  bound the client did not apply.
- **Every `57014` now costs an extra connection and query** against a server that has just proved
  slow, delaying the error by up to the reader's own short bound. Same discipline as
  `MissingRelationProbe`, on a path already headed for an error screen.

**Why the number is kept at all.** The honest competitor is reporting no number and deleting the
reader outright, which is simpler than this plan by about a hundred lines. The number survives
because `statement_timeout` is `USERSET` and SemiPlot sends none, so the effective bound is per-site
and knowable no other way; an operator reading a screenshot from a site they cannot reach learns
which bound was applied without asking anyone to run a query. The remedy is the same either way —
check the reader role's bound — so this is a diagnosis-at-a-distance argument, not a correctness one.

**Why not a fixed bound with no read-back but the number still reported.** The server's own default
still fires `57014`, and the error would then report a number the server never applied: "exceeded its
configured bound of 300 s" when the server cancelled at 30 s. A wrong number is worse than none.

**What `TimeSpan.Zero` means, and what the operator sees.** The reader keeps three states: a parsed
bound, `TimeSpan.Zero` when the server answers `0` and bounds nothing, and null when the read could
not run. The first two are distinguished in the log. In the operator sentence they collapse, because
neither has a bound to report — a `57014` on a server that bounds nothing is a cancel, not an
exceeded bound. `ArchiveQueryTimedOutError.Describe` therefore gains a second wording for a zero
timeout, naming the SQLSTATE and no number, instead of the current "exceeded its configured bound of
0 s". That wording names no mechanism either, and `statement_timeout` least of all: the two zero
states cannot be told apart here, and on the second of them the setting reads `0` and is working as
configured, so blaming it sends the operator after a setting that is doing what it was asked to.

**Why the number stays true when read from a different session.** SemiPlot sends no
`statement_timeout` in any form (`postgres-instance.md:45`), so the effective value is the reader
role's default and every session of that role runs under it. This holds while that default is static.
Role and database defaults bind at backend start and a pooled physical connection keeps its startup
value, so an administrative change to the role default mid-run can leave the reported number one
increment stale. The shipped eager cache has the same window — it holds whatever the most recently
opened physical connection read — so this is not a regression, but the documents must carry the
caveat rather than promise more than holds.

## Technical Details

**The reader.** `StatementTimeoutReader`, an internal sealed type beside `MissingRelationProbe`,
taking `ArchiveDataSource` and an `ILogger`, exposing one method returning `Task<TimeSpan?>`. It opens
a fresh connection with `CancellationToken.None` and a short command timeout of its own, runs
`ArchiveStatements.EffectiveStatementTimeout`, parses the milliseconds, and swallows its own failures
to a warning — the same discipline `MissingRelationProbe.cs:28-64` follows, and for the same reasons:
the failed command's connection may sit in an aborted transaction answering `25P02`, and a caller's
token is frequently already cancelled by the time its read fails.

`ParseMilliseconds` (`ArchiveDataSource.cs:117-123`) is copied here and made internal so its tests can
reach it; the original is deleted in Task 3, not Task 1, because
`CacheEffectiveStatementTimeout` still calls it at `:139` until then.
`InitializerCommandTimeoutSeconds` (`ArchiveDataSource.cs:26-28`) moves here as the reader's own
bound, keeping its ten seconds under a name and a comment that state what it now bounds: one read on
a server that has just proved slow, where an error path that hangs is worse than one that reports no
number.

**The mapping seam.** `ArchiveExceptionMapper.Map` stays synchronous and pure, and its constructor
loses the `Func<TimeSpan?>` and its `ArgumentNullException.ThrowIfNull`. It gains the bound the same
way it already gains the relation name: from its caller. `PostgresDataProvider.MapAsync` resolves the
bound through `StatementTimeoutReader` only when the exception carries SQLSTATE `57014`, and passes
null on every other path, so nothing else pays for it.

**`ArchiveQueryTimedOutError` keeps its public shape**: same four fields, same constructor. Only
`Describe` changes, gaining the no-number wording for a zero timeout.

**Deleted from `ArchiveDataSource`**: the `UsePhysicalConnectionInitializer` registration (`:50`),
`ThrowOnSynchronousOpen` (`:110-115`), `CacheEffectiveStatementTimeoutAsync` (`:125-133`),
`CacheEffectiveStatementTimeout` (`:137-149`), `ParseMilliseconds` (`:117-123`),
`InitializerCommandTimeoutSeconds` (`:26-28`), the `_effectiveStatementTimeoutTicks` field (`:39`),
the `EffectiveStatementTimeout` property (`:60-68`), the `_commandTimeoutMargin` constant (`:30`), and
the derivation in `ResolveCommandTimeoutSeconds` (`:154-164`). `_unboundedServerFallback` (`:31`)
survives as the fixed backstop under a name that no longer says "fallback". The warn-on-change arm is
the type's only log entry, so the constructor's `ILogger<ArchiveDataSource>` parameter (`:41`), its
`ThrowIfNull` (`:44`) and the `_logger` field it fills (`:34`) go with it. `ArchiveDataSource` is
public, so that is a public constructor signature change: the container registers the type and
resolves the constructor itself, so the call sites that move are the direct constructions in
`SemiPlot.Tests.Data`.

**Kept, with the argument restated.** `CreateCommand` continues to stamp the bound per command rather
than the connection string carrying `Command Timeout=300`. The current justification — that an
explicit bound beats inheriting Npgsql's implicit 30 s — does not distinguish the two, since a
connection-string value inherits nothing either. The real reasons are that the constant stays named,
commented and asserted by a test that parses no connection string, and that
`PostgresConnectionSettings` builds the connection string from a YAML file whose keys are fixed.
`Command Timeout=0` in the connection string is unchanged (`data-integration.md:477`).

**Comments that become false and are corrected here**: the `ArchiveDataSource` class summary
(`:11-23`), whose two-reason justification for the open-then-create surface loses its second reason;
the `OpenConnectionAsync` doc (`:70-74`), which promises the initializer has run; the
`ArchiveExceptionMapper` summary (`:17`), which says the bound "lives only in the data source's cached
field"; and the `ArchiveStatements.EffectiveStatementTimeout` doc (`:52-58`), which says "read once
per physical connection". The statement text itself is untouched, so the scope guard holds.

**`EffectiveStatementTimeout` stays unpinned, deliberately.** It is now the statement the whole
reported number rests on, and it is quoted in no fenced block in `data-integration.md` and asserted
by no case in `ArchiveStatementTextTests`, which pin the three read statements only — `RelationProbe`,
the other failure-path statement, is unpinned the same way. It is left that way because the
project's direction is to stop growing the run-time document-pinning mechanism: a future statement is
pinned with a plain literal in the test instead, so adding a fourth fence here would extend the
mechanism this slice is not extending.

## What Goes Where

- **Implementation Steps**: the code, its tests, and the documents that describe the changed
  behaviour.
- **Post-Completion**: nothing needs manual verification — the composition root still resolves the
  stub, so no path in the running application reaches this code.

## Implementation Steps

### Task 1: Add the lazy statement-timeout reader

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/StatementTimeoutReader.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/StatementTimeoutReaderTests.cs`

- [x] create `StatementTimeoutReader`, an internal sealed type taking `ArchiveDataSource` and
      `ILogger<StatementTimeoutReader>`, with one public `Task<TimeSpan?> ReadEffectiveBoundAsync()`
- [x] open a fresh connection with `CancellationToken.None`, run
      `ArchiveStatements.EffectiveStatementTimeout` under its own ten-second command bound carried as
      a named constant, and return the parsed value; a server answering `0` returns `TimeSpan.Zero`
- [x] swallow every exception to a logged warning and return null, so the error path can never
      re-enter the mapper for the reader's own failure; the warning distinguishes "could not read"
      from the server's own zero
- [x] copy `ParseMilliseconds` from `ArchiveDataSource.cs:117-123` unchanged and make it `internal
      static` so the tests reach it — do not delete the original here, `CacheEffectiveStatementTimeout`
      still calls it at `ArchiveDataSource.cs:139`
- [x] write tests over the settings strings `ArchiveDataSourceTests.cs:22-25,44-47` already covers — a
      parsed value, `"0"`, an unparsable string, and null
- [x] run tests — must pass before task 2

### Task 2: Fill the timeout from the failure path

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveQueryTimedOutError.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] drop the `Func<TimeSpan?>` constructor parameter, its field and its `ThrowIfNull` from
      `ArchiveExceptionMapper` (`:29,31-37`), and take the bound as a `TimeSpan?` argument on the
      mapping call instead, beside the relation name
- [x] fill `ArchiveQueryTimedOutError` from that argument, using `TimeSpan.Zero` when it is null
      (`ArchiveExceptionMapper.cs:107-111`), and correct the mapper's summary at `:17`
- [x] give `ArchiveQueryTimedOutError.Describe` (`:29-35`) a second wording for a zero timeout that
      names the SQLSTATE and no number, and names no mechanism — `statement_timeout` above all,
      because a server that bounds nothing reads `0` there and is working as configured
- [x] add a sixth parameter to `PostgresDataProvider`'s internal constructor (`:34-52`) for
      `StatementTimeoutReader`, and resolve the bound in `MapAsync` only when the exception carries
      SQLSTATE `57014`
- [x] register `StatementTimeoutReader` as a singleton beside `MissingRelationProbe`
      (`PostgresDataServiceCollectionExtensions.cs:25`), pass it into the provider factory (`:36-41`),
      and delete the `Func<TimeSpan?>` wiring (`:31`)
- [x] update `PostgresCompositionTests.NewProvider` (`:28-36`) for both new signatures
- [x] update the mapper's tests for the new signature, keeping their assertions on error type and
      structured fields rather than on message text
- [x] write a test that a `57014` with an unreadable bound reports `TimeSpan.Zero` and the no-number
      wording rather than throwing
- [x] run tests — must pass before task 3

### Task 3: Delete the initializer apparatus

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveDataSourceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] remove the initializer registration (`:50`), `ThrowOnSynchronousOpen` (`:110-115`),
      `CacheEffectiveStatementTimeoutAsync` (`:125-133`), `CacheEffectiveStatementTimeout`
      (`:137-149`), `ParseMilliseconds` (`:117-123`), `InitializerCommandTimeoutSeconds` (`:26-28`),
      the `_effectiveStatementTimeoutTicks` field (`:39`), the `EffectiveStatementTimeout` property
      (`:60-68`) and the `_commandTimeoutMargin` constant (`:30`) — none of these raise an unused
      warning, so check each one off by grep rather than by build
- [x] make `ResolveCommandTimeoutSeconds` (`:154-164`) return the fixed backstop unconditionally,
      renaming `_unboundedServerFallback` (`:31`) to a name that states what it now is
- [x] rewrite the class summary (`:11-23`), which justifies the open-then-create surface with two
      reasons and keeps only the first, and the `OpenConnectionAsync` doc (`:70-74`), which promises
      the initializer has run
- [x] correct the `ArchiveStatements.EffectiveStatementTimeout` doc (`:52-58`), which says the
      statement is read once per physical connection; the statement text at `:59-61` does not change
- [x] replace the four cache-and-derive tests (`ArchiveDataSourceTests.cs:21-85`) with one asserting
      that a command built by `CreateCommand` carries the fixed bound, keeping the file's
      no-server construction note (`:11-13`)
- [x] delete `TheEffectiveBoundIsUnsetUntilAPhysicalConnectionOpens`
      (`PostgresCompositionTests.cs:162-170`), whose subject no longer exists
- [x] run tests — must pass before task 4

### Task 4: Assert the reported bound against a real server

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/StatementTimeoutReadTests.cs`

- [x] **this task needs a database.** A container runtime plus `semibase`, or `SEMIPLOT_TEST_PG`. On a
      machine without one the new test skips and this task's checkbox cannot be discharged — say so
      and stop rather than marking it done on a skip
      — **not discharged at Task 4 time, discharged in Task 7.** At Task 4 the machine carried no
      Docker or podman daemon, no `semibase` on `PATH`, and `SEMIBASE_EXE` and `SEMIPLOT_TEST_PG` both
      unset, so the test skipped with the reason "semibase was not found on PATH". `semibase` v0.1.0
      and a Docker daemon holding `postgres:17-alpine` were installed before Task 7, and the test now
      runs here: `dotnet test SemiPlot.slnx --filter
      "FullyQualifiedName~TimedOutReadReportsTheServersOwnBound"` reports 1 passed / 0 skipped in
      425 ms.
- [x] measure both statements against the template and record the two numbers here: the
      `pg_settings` read the reader issues, and `QueryHistoryAsync` over the full seeded day at the
      raw layer for every pen
      — **discharged in Task 7**, measured against the template on a `postgres:17-alpine` container by
      a throwaway harness that was deleted before commit rather than added to the suite. The reader's
      `SELECT setting FROM pg_settings WHERE name = 'statement_timeout'` takes **4.246 ms cold and
      1.594 to 1.811 ms warm** over six consecutive runs on one connection. `QueryHistoryAsync` over
      the full seeded day at the raw layer for all eight pens, target 4096 columns, takes **548.4 ms
      cold and 327.2 to 343.2 ms warm** over three runs and returns 8 envelopes. The 50 ms bound
      therefore clears the settings read by about twelve times at its worst and sits about six times
      under the forced read: the bracket opens, but its upper end is narrower than the plan's "orders
      below the full day" claimed. `StatementTimeoutReadTests.cs` now carries these numbers in place of
      that claim. The diagnostic failure messages stay — a bracket is a property of the machine it runs
      on, and both ends have to stay nameable from a CI log alone.
- [x] add a gated test `TimedOutReadReportsTheServersOwnBound` that clones the template, sets a 50 ms
      `statement_timeout` on the reader role scoped to the clone, and runs the full-day raw read
- [x] assert the result fails with `ArchiveQueryTimedOutError` and that its `Timeout` equals the bound
      set, proving the number is the server's rather than the client backstop
- [x] if the bracket does not open, change the forcing mechanism and record why — never widen the
      assertion, and never lower the bound below the settings read
      — not reached: the bracket opens on the measured numbers above, so no forcing mechanism changed
      and no assertion widened. The two failure messages stay for a machine where it does not open.
- [x] verify the test skips with a stated reason on a machine with no container runtime
- [x] run tests — must pass before task 5
      — `dotnet build SemiPlot.slnx` clean; `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests`
      290 passed / 0 skipped and `SemiPlot.Tests.Data` 350 passed / 43 skipped, zero failures — the
      skip count rises by the one test this task adds. `dotnet format SemiPlot.slnx
      --verify-no-changes` exits 0.

### Task 5: Correct two rationale comments

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs`

- [x] rewrite `ArchiveStatementTextTests.cs:12-14`, whose claim that a literal "would only catch an
      edit to the code, which is the half nobody needs pinning" is false — the code half is the
      production half. Adopt the reasoning the roadmap's Guard strategy already carries
      (`docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:128-132`): reading the document
      pins both halves, so a literal is the weaker guard rather than the cheaper one, and it matters
      because the document is what each slice's brief is assembled from
- [x] rewrite the localisation clause in `MissingRelationProbe.cs:6-9`: a non-English `lc_messages`
      changes the wording and the quote glyphs, not the interpolated identifier. The reason not to
      read the message is that the project routes on structured fields, and `42P01` leaves the
      structured table-name field empty
- [x] run tests — must pass before task 6
      — `dotnet build SemiPlot.slnx` clean; `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests`
      290 passed / 0 skipped and `SemiPlot.Tests.Data` 350 passed / 43 skipped, zero failures —
      identical to Task 4, as a comment-only change must be. `dotnet format SemiPlot.slnx
      --verify-no-changes` exits 0.

### Task 6: Update the documents

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/postgres-instance.md`

- [x] correct `data-integration.md:437` — the bound is read from a session of the same reader role on
      the failure path, not from the failing session; state that zero means there is no bound to
      report, either because it could not be read or because the server bounds nothing
- [x] correct `data-integration.md:477-479` — the per-command backstop is a fixed bound, no longer
      derived from a value read at connection time; `Command Timeout=0` and the reason for it are
      unchanged
- [x] correct `postgres-instance.md:46` — the client reads the effective value when a read has failed,
      and the value is stable except across an administrative change of the role default, which can
      leave one report stale
- [x] state in `data-integration.md` that the fixed backstop is no longer guaranteed above the
      server's bound, and what that means for a site whose bound exceeds it
- [x] run tests — the statement-text pinning tests read these documents, so a fence edit fails here
      — `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests` 290 passed / 0 skipped and
      `SemiPlot.Tests.Data` 350 passed / 43 skipped, zero failures — identical to Task 5, as a
      documentation-only change must be. `dotnet format SemiPlot.slnx --verify-no-changes` exits 0,
      and neither `.md` file carries a BOM. No fenced block, heading or fence marker was touched; the
      three places the plan names were the only ones describing the eager read-back.

### Task 7: Verify acceptance criteria

- [x] verify every requirement in Overview is implemented — the lazy read is the only reader of the
      bound (`StatementTimeoutReader.cs`), it runs from `PostgresDataProvider.MapAsync` only on
      SQLSTATE `57014` and passes null on every other path, and the client bound is a fixed
      `_commandTimeoutBackstop` of five minutes returned unconditionally by
      `ResolveCommandTimeoutSeconds`. **`ArchiveDataSource.cs` no longer opens a connection to read
      anything**: it sets `CommandText` once, inside `CreateCommand`, on a connection the caller owns,
      and executes no command of its own. **The reported number is still the server's own** — the
      gated `TimedOutReadReportsTheServersOwnBound` asserts `ArchiveQueryTimedOutError.Timeout` equals
      the 50 ms the reader role was given for that clone, which neither the five-minute backstop nor an
      unreadable bound's `TimeSpan.Zero` can produce, and it passes here in 425 ms
- [x] run Evidence 2's two `git grep` commands and confirm no hit — both exit 1 with no output.
      `ThrowOnSynchronousOpen`, `CacheEffectiveStatementTimeout`, `_effectiveStatementTimeoutTicks`,
      `InitializerCommandTimeoutSeconds`, `_commandTimeoutMargin`, `_unboundedServerFallback` and
      `Func<TimeSpan?>` are likewise absent from every `.cs` file
- [x] run the full suite: `dotnet test SemiPlot.slnx` — zero failures. **`SemiPlot.Tests` 290 passed /
      0 skipped / 0 failed in 3 s, `SemiPlot.Tests.Data` 393 passed / 0 skipped / 0 failed in 11 s.**
      Nothing skips: `semibase` v0.1.0 is on `PATH`, the Docker daemon runs and `postgres:17-alpine` is
      pulled locally, so every gated test executes. The per-task counts recorded in Tasks 4 to 6
      (350 passed / 43 skipped) were taken before that runtime existed and are skip-inflated; these are
      the real numbers
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0 — exit 0
- [x] confirm every tracked `.cs` file still begins `ef bb bf` — 189 of 189 tracked `.cs` files carry
      the BOM, none missing
- [x] record the measured counts and the Task 4 bracket numbers here — bracket lower end, the reader's
      own `pg_settings` read: **4.246 ms cold, 1.594 to 1.811 ms warm**. Bracket upper end,
      `QueryHistoryAsync` over the full seeded day at the raw layer for all eight pens: **548.4 ms
      cold, 327.2 to 343.2 ms warm**. The 50 ms bound sits about twelve times above the worst settings
      read and about six times below the fastest forced read. Both came from a throwaway harness run
      against a cloned template on `postgres:17-alpine`, deleted before commit; Task 4's two notes are
      corrected to state them

### Task 8: [Final] Update documentation

- [x] update `CLAUDE.md` if a new pattern was established — recorded. This slice produced the second
      instance of one shape: a cold-path reader that opens a fresh connection on the failure path to
      answer a diagnostic question the exception itself cannot (`MissingRelationProbe` for `42P01`,
      `StatementTimeoutReader` for `57014`), run from `PostgresDataProvider.MapAsync` so
      `ArchiveExceptionMapper` stays synchronous and unit-testable. Two instances make it a pattern
      rather than a one-off, four provider slices remain that will map further SQLSTATEs, and the
      constraint it carries — the round trip never enters the mapper — is not derivable from the
      code without reading both types. Three sentences added to the "Data-source projects" section
      of `CLAUDE.md`, stating the shape and the constraint that a cold-path reader is justified only
      when a distinct operator remedy depends on the answer
- [x] confirm no architecture document still describes the eager read-back — re-searched
      `docs/architecture/*.md` independently of Task 6, over the initializer, over a bound read at
      connection time, and over a bound cached per connection. `initializ` matches nothing in any
      architecture document. Every `statement_timeout`, `57014`, `Command Timeout`, `Npgsql`,
      `backstop` and `five minutes` hit falls inside four regions of `data-integration.md` — the
      error-table row (`:437`), the failure-path paragraph (`:457-465`), the shipped limitation the
      read-back carries (`:467-476`) and the configuration paragraph (`:495-507`) — plus
      `postgres-instance.md:39-51`. All describe the
      lazy read; the surviving "connection time" mentions are the ones stating what the change is
      *not* (`data-integration.md:472-475`, `data-integration.md:501`) and the staleness caveat about
      a pooled physical connection keeping its startup value (`data-integration.md:462`,
      `postgres-instance.md:49`), both of which are true of the lazy reader
- [x] move this plan to `docs/plans/completed/` — **not performed here, and deliberately.** Archiving
      the plan is delivery work: it belongs after the operator has tested the branch, alongside the
      push. The plan file stays at `docs/plans/20260819-provider-simplification.md`

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** None. The composition root still resolves the stub, so no path in the
running application reaches any code this slice touches, and nothing user-visible changes.

**Handed to `postgres-startup-and-composition`.** Its startup probe is a read, and after this slice a
hung-but-accepting server leaves it waiting for the fixed five-minute backstop before
`ArchiveUnreachableError` exists to map to a UI state. That slice needs its own bound or cancellation
token for the probe.

**External system updates.** None. No consuming project, no deployment configuration, no third-party
service.

**Remaining slices.** After this slice the roadmap continues with: postgres-gap-reconstruction,
postgres-bucketed-read, postgres-realtime-poll, postgres-startup-and-composition,
live-demo-and-stub-retirement.

**Executed by exec:**

- branch: provider-simplification

## Verify it yourself

**The whole suite, with the database live.** This machine now carries `semibase.exe` v0.1.0 on `PATH`
and a running Docker with `postgres:17-alpine` local, so nothing skips:

```powershell
dotnet test SemiPlot.slnx
```

`SemiPlot.Tests` 290 passed / 0 skipped, `SemiPlot.Tests.Data` 393 passed / 0 skipped, zero failures.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**The reported bound is still the server's own — the one guarantee this slice had to keep.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~TimedOutReadReportsTheServersOwnBound"
```

The test clones the seeded template, sets `statement_timeout = '50ms'` on the reader role scoped to
that clone, runs the full seeded day at the raw layer for all eight pens, and asserts
`ArchiveQueryTimedOutError.Timeout` equals 50 ms. Neither the five-minute client backstop nor an
unreadable bound's `TimeSpan.Zero` can produce that number, so the assertion can only pass if the
lazy read-back reached the server and read the real value. It is absent at `2a51fbf` and present from
`d1a365f`.

**The apparatus is gone.** Both return no hit:

```powershell
git grep -n "UsePhysicalConnectionInitializer" -- "*.cs"
git grep -n "EffectiveStatementTimeout" -- "SemiPlot/SemiPlot.DataSource.Postgres/ArchiveDataSource.cs"
```

`ArchiveDataSource.cs` went from 165 lines to 78 and no longer opens a connection to read anything:
the physical-connection initializer, its synchronous throw-stub, the interlocked cache, the
millisecond parse and the warn-on-change arm are all deleted, and the class no longer takes a logger.

**The client backstop is fixed and still stamped per command.** `EveryCommandCarriesTheFixedBackstop`
in `SemiPlot.Tests.Data/Postgres/ArchiveDataSourceTests.cs` builds a command on a closed connection
and asserts 300 seconds, with no server involved.

**The mapper no longer reads the data source's state.** `ArchiveExceptionMapper`'s constructor takes
no `Func<TimeSpan?>`, and both of `Map`'s remaining arguments are required, so the compiler rejects a
`57014` call site that forgets to resolve the bound.

**The bracket this rests on, measured 2026-08-19 against `postgres:17-alpine`.** The reader's own
`pg_settings` read takes 4.246 ms cold and 1.6–1.8 ms warm; the forced full-day raw read takes
548 ms cold and 327–343 ms warm. The 50 ms bound therefore sits about twelve times above the floor
and six times below the ceiling. The upper margin is narrower than a bound chosen by eye would
suggest, which is why the test names which end of the bracket it accuses on every failure path.

**No manual repro.** The composition root still resolves the stub, so no path in the running
application reaches any code this slice touches, and nothing user-visible changes.
