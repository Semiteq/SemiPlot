# Read history from a chosen layer

## Overview

`QueryHistoryAsync` is the last member of `PostgresDataProvider` that still fails with
`ProviderNotImplementedError`, the temporary type
`SemiPlot/SemiPlot.Core/Data/Errors/ProviderNotImplementedError.cs` holds at the branch point.
This slice implements it: run the windowed statement over `trends`, fold the rows into one
`PenHistoryEnvelope` per pen, and delete the temporary error type together with the tests that
assert on it.

The fold reuses `MinMaxDecimator`, which today lives in `SemiPlot.DataSource.Stub` and is described
by `CLAUDE.md` as stub-only. Neither data-source project may reference the other and Core may not
reference either, so the decimator moves to `SemiPlot.Core/Trends` beside `PenHistoryEnvelope`
first — verbatim, no behaviour change. Its input contract is parallel `(DateTime, double?)` lists
where null marks a gap, which is exactly the vocabulary the archive path needs; what differs between
the two providers is the translation into that vocabulary, and that stays provider-side.

The application still runs on the stub and the composition root is untouched.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-history-read

**What ships already and must be reused, not rebuilt**

- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs` — every statement, pinned by
  `ArchiveStatementTextTests`, which reads the fenced SQL out of `docs/architecture/data-integration.md`
  at run time and compares it to the constant, one `[InlineData]` row per heading. The windowed
  statement joins it.
- `ArchiveDataSource` — `OpenConnectionAsync(CancellationToken)` and
  `CreateCommand(statementText, connection)`, the command already carrying the resolved timeout.
  Parameters are the caller's to bind.
- `ArchiveExceptionMapper` — maps SQLSTATE `57014` onto `ArchiveQueryTimedOutError` already. The
  windowed read travels that path unchanged; which bound the error reports is
  `provider-simplification`'s to settle.
- `MissingRelationProbe` — a `42P01` here can only mean `trends`, since the statement touches one
  relation, so the read passes `ArchiveStatements.TrendsRelation` as its fallback.
- `ArchiveTimeConverter` — `ToArchiveLocal` returns `DateTimeKind.Unspecified`, which is what Npgsql
  requires for a `timestamp without time zone` parameter; `ToUtc` converts rows on the way out.

**The statement**, verbatim from `docs/architecture/data-integration.md`, the "History, chosen layer
already sparse enough" block. It is the first statement in this repository that takes parameters:
`@ids`, `@layer`, `@from`, `@to`.

**What the fold must produce**

- `SemiPlot/SemiPlot.Core/Trends/PenHistoryEnvelope.cs` — parallel `Timestamps`, `Min`, `Max`,
  `Center` of equal length, and **strictly ascending** timestamps enforced in the constructor
  (`:25-33`), which throws otherwise.
- `SemiPlot/SemiPlot.DataSource.Stub/MinMaxDecimator.cs` — `Decimate(penId, timestamps, values,
  targetColumnCount)` over `(DateTime, double?)` where null marks a gap. Below the target it passes
  rows through one column each; above it, it buckets by index and takes min, max and a centre
  sample. Its only caller today is `RandomStubDataProvider.cs:121`, and it is pinned by
  `SemiPlot/SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs`.

**Ordering is guaranteed by the schema, with one exception.** `trends` carries
`PRIMARY KEY (id, l, t)` (`sql/semiplot_dev.sql:22`) and the statement filters a single `l`, so
`(id, t)` is unique within one query and `ORDER BY id, t` ascends strictly in naive local time. The
exception is the conversion: an autumn fall-back repeats an hour and a spring gap can invert a pair,
which `docs/architecture/data-integration.md` accepts as cosmetic. The fold drops any row whose
converted timestamp does not exceed the previous kept one — one comparison per row, firing at most
an hour a year.

**Archive-shaped input is not stub-shaped.** The vendor writes anchor pairs on change and nothing
during a steady stretch, so a window can return far fewer rows than the canvas has columns, or none
at all. `[MEAS:dump-20260805]` shows a real window where one minute holds twenty-five raw rows and
the next holds none.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- Complete each task fully before the next; all tests pass before the next task starts.
- Every task that changes code carries its own tests as separate checklist items.

## Testing Strategy

Pure logic — the statement text, the row fold, the monotonic filter — is `Category=Unit` in
`SemiPlot.Tests.Data`, raw xunit `Assert.`, all three traits. Anything opening a connection is
`Category=Integration` behind `DatabaseGate`, which skips with a stated reason when no runtime
answers and fails instead under `SEMIPLOT_REQUIRE_DB=1`.

`MinMaxDecimatorTests` stays in `SemiPlot.Tests` and keeps its AwesomeAssertions style; the move
changes its `using` and nothing else. That the file is untouched apart from the namespace is the
evidence the move carried no behaviour, and it is why the file stays at `Core/Data/` for a type now
in `Core/Trends` — the path mismatch is accepted here and left to whoever next edits the file.

**No gated test can be observed passing on the development machine** — no container runtime — so they
first execute on the pull request's `data-tests` job.

## Acceptance Evidence

1. **The decimator moved without changing behaviour.**
   `git diff master...HEAD -- SemiPlot/SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs` shows a
   `using` change only, and
   `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj --filter "FullyQualifiedName~MinMaxDecimatorTests"`
   reports the same count as at the branch point — the pinned set that runs unchanged against the
   moved type. The suite-wide count is not the proof it once was: later tasks on this branch add
   tests for their own reasons, so only the decimator's own file carries the invariant.

2. **The statement is what the architecture document says.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StatementText"` — the windowed statement
   matches the document's fence character for character, and its four parameter names are asserted
   separately.

3. **History reads a seeded archive.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresHistoryRead"`
   Against the bench the returned envelopes carry the seeded pens' rows for the requested window and
   layer, timestamps ascending, one envelope per requested pen that has rows.

4. **A window with no rows is a success, not a failure.**
   The same run covers it: an envelope list that omits pens with nothing in the window, and a
   successful `Result` rather than an error.

5. **The query reaches its rows through an index.**
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ExplainPlan"` — the plan reaches its rows
   through an index scan or a bitmap heap scan driven by one, and reads no row-holding `trends`
   partition sequentially. Which of the two the planner picks is its own decision, so the assertion
   accepts either. It cannot name `tpk`: `trends` is
   `PARTITION BY RANGE (t)`, so that is the parent index and never scanned.

6. **The temporary error type is gone.**
   After Task 7's documentation edits, `git grep -n ProviderNotImplementedError` returns nothing
   outside `docs/plans/completed/`. All four provider members are implemented, or deliberately empty
   in `Subscribe`'s case, which `postgres-realtime-poll` fills.

7. **The suite and the gates.**
   `dotnet test SemiPlot.slnx` — zero failures. `dotnet format SemiPlot.slnx --verify-no-changes`
   exits 0. Every `.cs` starts `ef bb bf`; no `.md` does.

## Progress Tracking

- Mark completed items `[x]` immediately. Add discovered tasks with `+`. Record blockers with
  `BLOCKED` and the reason. Keep this file in sync with the work actually done.

## Solution Overview

**The decimator moves, it does not fork.** The two providers differ in what they read, not in how an
envelope is built from `(timestamps, values)`. Duplicating the algorithm would duplicate its pinned
test suite as well. Core is the sanctioned shared home: `CLAUDE.md` already places renderer-agnostic
logic there, and the decimator references only Core types.

**Reduction still happens client-side in this slice.** `postgres-bucketed-read` has not shipped, so a
raw-layer window denser than the canvas is reduced by the decimator exactly as the stub's output is.
That slice later bypasses the decimator for its own path; nothing built here has to be undone.

**Gaps are not reconstructed here.** `postgres-gap-reconstruction` reads the `q` markers and inserts
the `NaN` anchors. This slice passes values through and lets the decimator segment on nulls where a
null already exists, which is the seam that slice extends. On the bench it never fires: `ArchiveRow.Value`
is a non-nullable `double` and a break is row absence marked by `q = 32` and `q = 16` on rows carrying
real values, so the seeded archive holds no null `v`.

**A pen with no rows gets no envelope.** The alternative — an empty envelope per requested pen —
would force every consumer to distinguish "no data" from "not asked for". The rule is **interim**,
and not because the consumer side is ready for it: `TrendChartViewModel.ApplyHistory` writes the pens
a result carries and removes none, so a pen omitted from one window keeps the previous window's
envelope. It is interim because this slice's scope guard is the provider, and both halves of the
revision live elsewhere — the seed lookup in `postgres-gap-reconstruction`, the consumer side in
`postgres-startup-and-composition`, which is where Postgres is wired to the chart at all. Until then
the application still runs on the stub, where the rule never fires.

## Technical Details

**The row fold**, per pen, in the provider:

| Step | Rule |
| --- | --- |
| Read | `ORDER BY id, t` groups rows per pen without sorting client-side |
| Convert | `ArchiveTimeConverter.ToUtc` on each `t` |
| Filter | drop a row whose converted timestamp does not exceed the previous kept one, per pen |
| Value | `v` is `double precision` and nullable in the schema; a null becomes a gap |
| Fold | `MinMaxDecimator.Decimate(penId, timestamps, values, targetColumnCount)` |

**The filter drops more than a mis-ordered pair.** At the spring gap it drops the one or two rows the
conversion put out of order. At the autumn fall-back both passes over the repeated hour convert to the
same instants, so the first pass occupies them and every second-pass row that does not advance past
them is dropped — for an archive written at a steady cadence, the whole repeated hour, for every pen,
once a year, and the surviving hour is stamped an hour late. Nothing stateless does better: the
archive records no offset to tell the passes apart and `PenHistoryEnvelope` admits no repeat. Stated
in `data-integration.md`'s Time boundary section and pinned by
`HistoryRowFoldTests.TheSecondPassOverTheRepeatedHourIsDropped`.

**Parameters.** `@ids` binds the pen identifiers as an array, `@layer` the `smallint` layer, `@from`
and `@to` the window bounds converted through `ToArchiveLocal` so they arrive as
`DateTimeKind.Unspecified`. The variable list is mandatory: without it the query cannot use
`PRIMARY KEY (id, l, t)`, whose leading column is `id`, and reads every partition.

**The window bounds keep the fall-back collapse.** `ToArchiveLocal` is not injective, so a UTC window
spanning the autumn transition converts to a narrower local one, and a one-hour window over the
transition itself to a zero-width one selecting no rows. Accepted as cosmetic, the way
`data-integration.md` already accepts the duplicated hour on the row side; nothing compensates for it.

**`q` is selected but never read.** The statement returns it because `postgres-gap-reconstruction`
needs it and the statement is pinned. It travels no further than the wire: `ReadHistoryRow` projects
columns 0 to 2 and `HistoryRowFold.Row` carries no `q` member, so that slice extends the row struct
and the reader as well as the fold.

## What Goes Where

- **Implementation Steps** — the move, the statement, the read, the gated tests, the deletion,
  verification, documentation.
- **Post-Completion** — what the following slices inherit, and the remaining slices.

## Implementation Steps

### Task 1: Move the decimator to Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/MinMaxDecimator.cs`
- Delete: `SemiPlot/SemiPlot.DataSource.Stub/MinMaxDecimator.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs`
- Modify: `CLAUDE.md`

- [x] move the file verbatim into `SemiPlot.Core.Trends`, private `EnvelopeBuilder` unchanged
- [x] the stub provider and the test file change their `using` and nothing else — the test file drops
      `using SemiPlot.DataSource.Stub;`; `RandomStubDataProvider` needed no edit in this task, it
      already imported `SemiPlot.Core.Trends` for `PenHistoryEnvelope`. A later task on this branch
      did edit it: `BuildEnvelope`'s loop bound became `timestamp < toUtc` so the stub's window is
      half-open like the archive read's, which drops one sample at each window's right edge. That is
      a behaviour change in the provider the application runs on — see Post-Completion
- [x] `CLAUDE.md`'s data-source bullet drops "and owns the stub-only `MinMaxDecimator`" and states
      that the decimator lives in Core beside `PenHistoryEnvelope`, providers translating their input
      into its null-marks-gap vocabulary
- [x] run tests — `SemiPlot.Tests` must report the same count as at the branch point, which is what
      proves the move carried no behaviour — 286 passed, unchanged; `SemiPlot.Tests.Data` 330 passed /
      35 skipped, unchanged

### Task 2: Add the windowed statement

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`

- [x] add the statement verbatim from `docs/architecture/data-integration.md`, the "History, chosen
      layer already sparse enough" block — the constant is `ArchiveStatements.SparseHistoryWindow`
- [x] pin the text by adding an `[InlineData]` row for the `### History, chosen layer already sparse
      enough` heading to `EachDocumentedStatementMatchesTheConstantCharacterForCharacter`
- [x] pin the binder against the statement instead of the statement against itself — the fence
      extractor pins the whole block, and asserting the four names against the same constant catches no
      drift that matters. `TheWindowBinderNamesExactlyTheStatementsOwnParameters` binds through
      `PostgresDataProvider.BindWindow`, which is `internal` for that reason, and compares the names the
      command carries with the statement's own `@` tokens
- [x] run tests — must pass before Task 3 — `SemiPlot.Tests` 286 passed, unchanged;
      `SemiPlot.Tests.Data` 332 passed / 35 skipped, two tests more and the same skips

### Task 3: Implement the windowed read

**Files:**
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryRowFoldTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ConnectionSettingsFactory.cs`

- [x] the fold is its own `internal` type, not a private method, so its tests reach it through the
      existing `InternalsVisibleTo("SemiPlot.Tests.Data")` with no connection open — `HistoryRowFold`
      takes materialised `HistoryRowFold.Row` values, a nested record struct carrying the pen, the naive
      archive-local timestamp and the nullable value
- [x] `QueryHistoryAsync` opens a connection, binds the four parameters, reads rows in order and
      hands them to the fold, which folds per pen through `MinMaxDecimator`
- [x] window bounds convert through `ToArchiveLocal`; row timestamps convert through `ToUtc`
- [x] a row whose converted timestamp does not exceed the previous kept one is dropped, "previous
      kept" resetting at each pen — the comparand is the tail of the pen's own timestamp list, which is
      rebuilt per pen, so no global comparand exists to carry across
- [x] a null `v` becomes a gap; a pen with no rows gets no envelope; zero rows overall is a
      successful empty list
- [x] failures travel `MapAsync` with `TrendsRelation` as the fallback relation
- [x] + caller-argument faults answer in the `Result` channel ahead of the connection, so they never
      reach `MapAsync`: an inverted window, a target column count below one and a pen identifier outside
      the archive's 32-bit `trends.id` range each return a failed `Result` carrying a plain message, the
      first two worded exactly as `RandomStubDataProvider` words them, so the two implementations are
      indistinguishable to a consumer. A null `penIds` is the one precondition both assert with
      `ArgumentNullException.ThrowIfNull`. `HistoryArgumentGuardTests` pins all four without a server,
      resolving the provider over an address nothing answers
- [x] write unit tests for the fold's own three things — group by pen, convert, drop non-ascending —
      and nothing the decimator already pins: six tests in `HistoryRowFoldTests`. No rows at all;
      ascending rows below the target, which also checks `PenId` and `DateTimeKind` and so is the
      wiring test; only the pens carrying rows get an envelope; the per-pen comparand reset; the
      spring-forward gap; and the autumn fall-back's dropped second pass
- [x] delete `QueryHistoryAsyncFailsWithTheNotImplementedError`: the implemented member issues a real
      read against `127.0.0.1:1`, so this `Category=Unit` class would fail with
      `ArchiveUnreachableError`. Correct `ConnectionSettingsFactory`'s "no caller issues a read" comment
- [x] run tests — must pass before Task 4 — `SemiPlot.Tests` 286 passed, unchanged; `SemiPlot.Tests.Data`
      337 passed / 35 skipped, the six new fold tests less the deleted composition test and the same
      skips

### Task 4: Assert the read against a seeded archive

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ExplainPlanTests.cs`

- [x] read a window from the seeded archive as `semiplot_reader` through `ArchiveProviderFactory`
      and assert the envelopes against the seeder's own rows for that window and layer. The window is
      narrow enough that its row count stays below `targetColumnCount`, so the decimator passes rows
      through one column each and the comparison is against raw rows, not a second decimator run
- [x] assert a window before the archive's first row returns a successful empty list
- [x] assert a window straddling one of the seeder's four breaks carries no column inside it: the
      break writes no rows, so the envelope steps across the span with no interior sample. The exact
      sequence comparison against the seeder's rows is what says so — a separate "nothing inside the
      break" assertion would re-state a property of the seeder rather than of the read
- [x] vary the two parameters no other test varies. One test binds a strict subset of the seeded pens
      and asserts exactly those come back; one reads `AggregationLayer.Minute`, which is
      `LayerThinner.MinuteLayer = 1` in the bench, and asserts every pen returns fewer columns than
      from `Raw` over the same window. Without them a provider ignoring `penIds`, or binding layer 0
      always, passes the whole branch
- [x] cover the failure path both sibling reads cover: a history read against a provisioned but
      unseeded database fails with `ArchiveNotInitialisedError` naming `trends`
- [x] give `ExplainAsync` an overload that binds parameters — the windowed statement is the first
      explained one taking any — and bind `@ids`, `@layer`, `@from`, `@to`
- [x] `EXPLAIN` it over a narrow window and a strict subset of the seeded pens: a wide window over
      all eight selects most of the day partition, where a sequential scan is the planner's right
      answer. Assert an index scan and no sequential scan of a row-holding `trends` partition
- [x] every new test carries all three traits, `[Collection(ArchiveDatabaseCollection.Name)]` and
      `IClassFixture<SeededArchive>`, and skips through `DatabaseGate`
- [x] run tests — must pass before Task 5 — `SemiPlot.Tests` 286 passed, unchanged; `SemiPlot.Tests.Data`
      337 passed / 42 skipped, the seven new gated tests raising the skips by exactly seven

### Task 5: Delete the temporary error type

**Files:**
- Delete: `SemiPlot/SemiPlot.Core/Data/Errors/ProviderNotImplementedError.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs`

- [x] delete the type and `ProviderNotImplementedErrorCarriesTheMemberName`, the last test asserting
      on it once Task 3 removed the composition test's case — `DataErrorTests` also drops its now
      unused `using SemiPlot.Core.Data;`, which only the deleted test's `nameof(IDataProvider...)`
      needed. `EveryPublicErrorTypeIsSealedAndDerivesFromError` asserts `NotEmpty`, not a count, so it
      picked the removal up with no edit
- [x] run tests — must pass before Task 6 — `SemiPlot.Tests` 286 passed, unchanged;
      `SemiPlot.Tests.Data` 336 passed / 42 skipped, one test fewer and the same skips

### Task 6: Verify acceptance criteria

**Files:** none.

- [x] every check in Acceptance Evidence runs and produces its stated result, except the grep in
      Evidence 6, which Task 7 runs after the documentation edits — Evidence 1:
      `~MinMaxDecimatorTests` matched 16 tests, all passed, the branch-point count, and the
      `MinMaxDecimatorTests` diff is the single dropped `using SemiPlot.DataSource.Stub;` line;
      Evidence 2: `~StatementText` matched 8 tests, all passed; Evidence 3, 4 and the parameter
      variation: `~PostgresHistoryRead` matched 6; Evidence 5: `~ExplainPlan` matched 2 — each of the
      eight gated ones skipped, since no container runtime answers here; Evidence 7:
      `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests` 290 passed / 0 skipped and
      `SemiPlot.Tests.Data` 347 passed / 42 skipped, zero failures. `git diff --name-status -M
      master...HEAD` records the decimator as `R099`, so the move carried one line of change
- [x] with no runtime the gated tests skip with a stated reason and none passes; with
      `SEMIPLOT_REQUIRE_DB=1` the same tests fail instead — `--filter "Category=Integration"` reports
      0 passed / 42 skipped, the reason being "semibase was not found on PATH: download the v0.1.0
      release binary from github.com/Semiteq/SemiBase and point SEMIBASE_EXE at it". The inversion
      was executed, not assumed: `SEMIPLOT_REQUIRE_DB=1 dotnet test` on `SemiPlot.Tests.Data` reports
      42 failed / 347 passed / 0 skipped, each failure an `InvalidOperationException` carrying
      "SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails instead of skipping" ahead of the
      same reason. The 42 are exactly the skipped set
- [x] `git diff --name-only master...HEAD` lists nothing under `SemiPlot/SemiPlot.UI/` — 26 paths:
      `CLAUDE.md`, this plan, the roadmap, five under `docs/architecture/`, four under
      `SemiPlot.DataSource.Postgres/`, three under `SemiPlot.Core/`, one under
      `SemiPlot.DataSource.Stub/` and ten under the two test projects, of which one is
      `SemiPlot.Tests/Core/Data/MinMaxDecimatorTests.cs`
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` exits 0 and the BOM rule holds — exit 0, and
      all 186 tracked `.cs` files start `ef bb bf` while none of the 29 `.md` files does

### Task 7: Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveTimeConverter.cs`
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
- Move: `docs/plans/20260819-postgres-history-read.md` → `docs/plans/completed/`

- [x] `data-integration.md` — the history read folds through the decimator in Core, and a pen with no
      rows in the window yields no envelope. Mark that rule interim on both its counts: a pen last
      changed before the window has nothing to draw where the quality table's "absence of rows without
      a preceding `q = 32`" row promises a horizontal continuation, which
      `postgres-gap-reconstruction`'s seed lookup revises; and no consumer drops a pen a result omits,
      so `TrendChartViewModel.ApplyHistory` leaves the previous window's envelope in place and the
      line draws straight across a zoomed-in window, which `postgres-startup-and-composition` owns.
      The same section states that the decimator's `NaN` anchors do not fire on an archive break at
      all, because a break is row absence marked by `q` and `q` is not read yet. The document names
      the slice; the roadmap owns what each slice does. The fenced statement itself is untouched,
      which `ArchiveStatementTextTests` confirms
- [x] `data-integration.md`'s Time boundary section — it ended by leaving the non-ascending decision to
      the component assembling envelopes. That decision is made and stated, with its cost: the drop
      also discards the whole second pass over the autumn repeated hour
- [x] `data-integration.md:393-394` — this slice hands no token down; the slice that gives
      `IDataProvider` tokens owns splitting `57014` from a client-issued cancel
- [x] `data-integration.md:396-399` — the `ProviderNotImplementedError` paragraph goes with the type.
      What survives it: `Subscribe` is deliberately empty and `postgres-realtime-poll` fills it —
      that survivor sits in the Realtime section, beside the poll contract it qualifies, rather than
      in Error semantics, which names error types only. The rationale for the empty sequence is that
      `Subscribe` returns an observable and has no `Result` channel to fail through
- [x] the decimator's home — drop `charting.md:168`'s bold "Lives in `SemiPlot.DataSource.Stub`
      (stub-only caller)", and move it out of the stub box into the Core box in `overview.md:64`
- [x] `postgres-topology.md` — three of the four provider members are implemented now, not two, and
      the error-type node counts the nine sealed types `ProviderNotImplementedError`'s deletion leaves
- [x] + `trend-interaction.md:205,212-214` — the same home, missed by this Files block: the decimation
      section names `SemiPlot.Core.Trends` at the first mention and states that both providers fold
      in-process through the decimator, in place of "the current stub synthesizes decimated series
      in-process". DA-5 is corrected with it: the decimator anchors at the first and last row it is
      handed, not at the window edge, which it never sees. The stub reaches the edge only because it
      synthesises a point per tick across the whole window
- [x] `ArchiveTimeConverter.ToArchiveLocal`'s xmldoc (`:59-60`) — replace "the slice that builds
      history queries owns what to do about it" with the decision above: cosmetic, uncompensated
- [x] the roadmap's `postgres-history-read` entry — correct what it describes as pending, its "against
      a literal" phrasing included, and note in `postgres-gap-reconstruction` that `q` already arrives
      — the pinning sentence now names the fenced block the test reads and the separate parameter-name
      test; the failure-reporting row counts nine public error types; the gap slice records both the
      unused `q` and the pre-window seed lookup. `check-inert.sh` prints `inert`. The "State today"
      table is left as it stands: the stamping commit after the merge owns it, as it did for the
      catalogue slice
- [x] `git grep -n ProviderNotImplementedError` now returns nothing outside `docs/plans/completed/`;
      before this task it still hits `data-integration.md:396` and the roadmap at `:67` and `:320` —
      the three source hits are gone; what the grep still returns is this plan, which the delivery
      step moves under `docs/plans/completed/`
- [x] move this plan to `docs/plans/completed/` — not done here: archiving is delivery work and runs
      after the operator tests the branch

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification.** Needed, on one point. The application still runs on the stub, and the stub's
history window became half-open on this branch: `RandomStubDataProvider.BuildEnvelope` walks
`timestamp < toUtc` where it walked `timestamp <= toUtc`, so every window now yields one sample fewer
at its right edge. The chart therefore stops one point spacing short of the window's end — at the
minute layer's 15-second spacing, 240 columns over an hour instead of 241. Check that the trend still
draws to the right edge of the plot and that the cursor reads the last column. Everything else on the
branch is provider-side and unreachable from the running application. The gated tests first execute on
the pull request's `data-tests` job.

**Inherited: a pen omitted from a result keeps its stale envelope.** The provider drops a pen with no
rows in the window; `TrendChartViewModel.ApplyHistory` writes only the pens a result carries and
removes none, so the omitted pen keeps the previous window's entry in `_envelopesById` and its
previous series in `TrendPenState`. The stale envelope keeps feeding the cursor readers and the scale
model, and on a zoom-in its columns bracket the new window, so the line draws straight across it
rather than disappearing. Not fixed here: this slice's scope guard is the provider, and the
application still runs on the stub, where no pen is ever omitted. `postgres-startup-and-composition`
is the slice that wires this provider to the chart and therefore owns the consumer-side fix — either
removing the entry for a requested pen a result omits, or taking the empty envelope this provider
declines to send.

**What the following slices inherit.** `postgres-gap-reconstruction` extends the read and the fold:
the `q` column arrives on the wire but is not projected, so the row struct and `ReadHistoryRow` grow
with the fold. The decimator already segments on nulls, so that slice inserts the anchors rather than
restructuring the envelope path. `postgres-bucketed-read` adds a second statement and
constructs envelopes directly, bypassing the decimator for its own path. `provider-simplification`
still owns which bound `ArchiveQueryTimedOutError` reports.

**Cancellation stays unsettled.** `QueryHistoryAsync` takes no `CancellationToken`, so the mapper's
`OperationCanceledException` rethrow remains unreachable. The slice that gives the interface tokens
owns the `57014`-versus-own-token split.

**Remaining slices**

After this slice the roadmap continues with: provider-simplification, postgres-gap-reconstruction,
postgres-bucketed-read, postgres-realtime-poll, postgres-startup-and-composition,
live-demo-and-stub-retirement.

**Executed by exec:**

- branch: postgres-history-read

## Verify it yourself

**The whole suite.** `dotnet test SemiPlot.slnx` reports `SemiPlot.Tests` 290 passed / 0 skipped and
`SemiPlot.Tests.Data` 347 passed / 42 skipped, zero failures. `dotnet format SemiPlot.slnx
--verify-no-changes` exits 0.

**The windowed read reaches the archive.** The read itself has no manual repro — the composition root
still resolves the stub, so nothing in the running application calls it. The demonstrating tests are
gated on a container runtime and a `semibase` binary, and skip with a stated reason without them:

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresHistoryRead"
```

Six tests, all `[SKIP]` on a machine with no runtime. They first execute on the pull request's
`data-tests` job, which sets `SEMIPLOT_REQUIRE_DB=1` and turns an unavailable runtime into a failure
rather than a skip. To run them locally, start a container runtime and put `semibase` v0.1.0 on
`PATH` (or point `SEMIBASE_EXE` at it).

**The statement text cannot drift from the document.** `dotnet test SemiPlot.slnx --filter
"FullyQualifiedName~StatementText"` — eight tests, all pass ungated. They read the fenced block in
`docs/architecture/data-integration.md` at run time and compare it to the shipped constant, and pin
the binder's parameter names against that statement's own tokens.

**The decimator moved verbatim.** `git show b524b3a --stat` reports the rename as `R099`, and
`git diff master...HEAD -- SemiPlot/SemiPlot.Tests/Core/Trends/MinMaxDecimatorTests.cs` is a single
dropped `using` line. That file's own 16 tests are unchanged. The suite-wide count is not the proof —
later commits on this branch legitimately added tests elsewhere.

**Client-side reduction is pinned.** `MoreRowsThanTheTargetColumnCountAreReducedToIt` in
`SemiPlot.Tests.Data/Postgres/HistoryRowFoldTests.cs` puts ten rows through the fold at a target of
two. It is absent at `3fe4a12` and present from `48a0d9a`. Replace `targetColumnCount` with
`int.MaxValue` in `HistoryRowFold.Fold` and it fails with a collection mismatch; that is the only
guard that the fold still forwards the target to the decimator.

**The two providers answer alike.** `HistoryArgumentGuardTests` (added `48a0d9a`, extended `c659f64`)
and `RandomStubDataProviderTests` assert the same contract from opposite sides: a null pen list
throws, an undefined `AggregationLayer` throws, an inverted window and a target column count below
one return a failed `Result` carrying identical message text, and when a caller supplies two bad
arguments at once both providers report the same one. At `816aa4d` the Postgres provider threw where
the stub returned a failed `Result`; from `c659f64` they agree.

**The one manual check.** The stub's history window became half-open on this branch, so the chart now
stops one point spacing short of the window's end — 240 columns over an hour at the minute layer
instead of 241. Run the application, and confirm the trend still draws to the right edge of the plot
and that the cursor reads the last column. Everything else on the branch is provider-side and
unreachable from the running application.
