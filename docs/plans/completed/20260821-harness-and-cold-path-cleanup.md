# Cut the apparatus that accreted around the read path

## Overview

Twelve shipped slices, each through several review rounds, left roughly 750 lines of machinery whose
only consumer is its own tests. None of it is in the read path: the statements, the fold, the time
converter, the provider, the seven error types, `ExplainPlanTests` and the real-archive fixture stay.

**This slice changes no production behaviour and no operator-visible message.** That is what lets it
be reviewed as one deletion. The cold-path readers are not here: `StatementTimeoutReader` stays
because it meets the project's own bar, and `MissingRelationProbe` leaves in a slice of its own.

Four clusters go. One is a substitution rather than a deletion and carries the slice's only real
risk: withdrawing the document-fence guard leaves two read-path statements unpinned unless each first
gains the plain literal `SparseHistoryWindow` already carries. That substitution lands before the
machinery it replaces is removed.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice harness-and-cold-path-cleanup

### Cluster 1 — the stale-template sweep

- `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs`, 238 lines. The sweep is `StaleAfter`
  (`:34`), `MarkerPrefix` (`:36`), `StaleTemplatesCommand` (`:43`), `ServerEpochCommand` (`:54`, read
  only by `StampAsync` at `:149`), the `DropStaleAsync` call site (`:66`), the `StampAsync` call site
  (`:76`), `StampAsync` (`:143`), the `COMMENT ON DATABASE` write (`:157`), `DropStaleAsync` (`:163`)
  and `IsStale` (`:201-213`).
- `ArchiveTemplateTests.cs` (162 lines) is **two tests, both sweep tests**:
  `TheStaleSweepDropsIdleDatedTemplatesAndNothingElse` (`:34`) and
  `TheSweepLeavesTheTemplateThisRunIsUsing` (`:65`), plus private helpers serving only them. Nothing
  in the file tests template naming or reuse, so the file goes whole.
- `StaleTemplateRuleTests.cs` (56 lines) tests the stamp arithmetic alone.
- `PostgresContainerFixtureTests.cs:32-35` names `starts_with` first among the features that justify
  the PostgreSQL 14 floor. **That justification is already wrong**: `starts_with` is PostgreSQL 11,
  and `DROP DATABASE ... WITH (FORCE)` is 13. Removing `starts_with` from the tree makes a wrong
  comment wrong about a function that no longer exists, so the comment is re-anchored — the floor and
  the assertion at `:37` stay at 14.
  ⚠️ This bullet first re-anchored the floor on `date_bin`, calling it "the real 14 requirement". That
  is wrong too: `grep -c date_bin SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs` returns
  0. `date_bin` appears only in the bucketing statement quoted at
  `docs/architecture/data-integration.md:204`, whose slice `postgres-bucketed-read` is DROPPED. The
  features the bench executes bottom out at 13. The comment as shipped therefore states the floor as a
  **deliberate margin** over that 13 and nothing else, and the assertion stays at 14. It names
  `date_bin` nowhere: the conditional reading — what the bucketing query *would* read if that slice
  were ever revived — is carried by `postgres-instance.md:24-28`, the document that declares the
  floor, and is not repeated in a test comment.

### Cluster 2 — the harness's tests of itself

- Deleted: `SemibaseBinaryTests.cs` (148 lines), `SemibaseProvisionerTests.cs` (53).
- **`DatabaseGateTests.cs` (70 lines) and `TestEnvironmentTests.cs` (40 lines) both stay.** They are
  two halves of one policy, and `TestEnvironmentTests.cs:5-8` says so: `SEMIPLOT_REQUIRE_DB` decides
  whether an unavailable runtime is a skip or a failure, `DatabaseGateTests` passes the flag as a
  literal, and the variable-to-bool mapping in `TestEnvironment.cs:31-40` is asserted nowhere else.
  Were that getter to read false by accident, CI would report the gated tests as skipped and stay
  green — a silent pass that reports itself nowhere, which is exactly what this plan's Evidence 3
  exists to prevent. Keeping one and deleting the other would be incoherent.
- `PostgresContainerFixtureTests.cs` (61 lines) stays whole. It is three facts, one of which —
  `TheResolvedBinaryReportsThePinnedVersion` (`:48-60`) — keeps `SemibaseProvisioner.RunAsync` and
  `SemibaseBinary.PinnedVersion` exercised after their own tests go.
- **Accepted regression, stated rather than hidden.** No production or harness code changes here:
  `SemibaseBinary.cs` is not in this slice's file list, and `PathDirectories()` at
  `SemibaseBinary.cs:60-67` still unquotes and filters `PATH` entries, so a malformed entry still
  yields a stated skip rather than a `Path.Combine` throw. What is lost is the verification, not the
  behaviour: seven behaviours keep working and nothing asserts them any more. Each is named here
  rather than left to be discovered.

  | Behaviour losing its only test | Where it still lives |
  | --- | --- |
  | A configured `SEMIBASE_EXE` is never repaired from `PATH` (`AConfiguredPathIsNotRepairedFromTheSearchDirectories`) | `SemibaseBinary.Resolve` |
  | A configured path that does not exist is a stated reason (`AConfiguredPathThatDoesNotExistIsAStatedReason`) | `SemibaseBinary.Resolve` |
  | An absent binary is a reason naming both ways to supply it (`AnAbsentBinaryIsAReasonNamingBothWaysToSupplyIt`) | `SemibaseBinary.Resolve` |
  | A search directory that does not exist is stepped over rather than thrown (`AnUnusableSearchEntryDoesNotThrow`) | `SemibaseBinary.Resolve`'s search loop, where `File.Exists` on a candidate under a missing directory returns false |
  | `PATH` entries are unquoted and sanitised before they are probed, both halves of `PathEntriesAreUnquotedAndSanitisedBeforeTheyAreProbed` | `SemibaseBinary.PathDirectories` |
  | A resolved file that is not runnable is a stated reason (`AFileThatIsNotRunnableIsAStatedReason`) | `SemibaseProvisioner.RunAsync` |
  | An absent executable is a stated reason (`AnAbsentExecutableIsAStatedReason`) | `SemibaseProvisioner.RunAsync` |

  The two deleted files held nine `[Fact]`s and seven appear above. The two that do not are
  `SemibaseBinaryTests`'s happy paths, `AConfiguredPathResolvesToTheBinaryItNames` and
  `ABinaryOnTheSearchPathIsFound`, and they keep a successor:
  `PostgresContainerFixtureTests.TheResolvedBinaryReportsThePinnedVersion` runs the binary the
  fixture resolved, on every gated run. Both of `SemibaseProvisionerTests`'s two are above.

  Nothing succeeds them. The wager is the one the **Ordering** section states — a harness fault
  reports itself as the first gated test's skip reason — and `SEMIBASE_EXE` is set directly on CI, so
  the `PATH` half never runs there. `SemibaseBinary` and `SemibaseProvisioner` are both deleted
  outright by `semibase-container-provisioning`, so investing a test in either now would be spent
  twice.
- **The wager rests on one `catch`, recorded here beside it.** "A failed provisioner reports itself as
  the first gated test's skip reason" is true only because
  `SemibaseProvisioner.RunAsync` (`SemibaseProvisioner.cs:90`) catches
  `Win32Exception or InvalidOperationException or IOException` and returns `Result.Fail`.
  `ArchiveTemplate.BuildAsync` (`:53`) catches only `NpgsqlException or InvalidOperationException`, so
  a narrowing of the provisioner's catch would let a `Win32Exception` escape the collection fixture as
  a crash rather than becoming a stated skip. `SemibaseProvisionerTests` was that catch's only test
  and goes with this slice, so the argument and the code it rests on are recorded together instead.

### Cluster 3 — the document-fence machinery

- `ArchiveStatementTextTests.cs`, 221 lines.
- **Only two statements are fence-only.** The `[Theory]` at `:31` covers three headings, but
  `SparseHistoryWindow` already carries a plain literal — `SeededWindowStatement` at `:53-72` with
  its test at `:74-78`. `PenCatalog` and `ArchiveExtent` have no second pin.
- `TheWindowBinderNamesExactlyTheStatementsOwnParameters` (`:82-107`) reads only
  `ArchiveStatements.SparseHistoryWindow`, `_parameterTokenPattern` (`:25`) and
  `PostgresDataProvider.BindWindow`. No dependency on the extractor; it stays. `Normalise` (`:217`)
  was kept beside it at first and dropped later — see Task 2.
- Going: the `[Theory]` (`:31`), the four extractor self-tests (`:110`, `:116`, `:124`, `:132`),
  `DocumentPath` (`:22`), `ReadDocument` (`:139`), `FindRepositoryRoot` (`:151`), `ExtractFencedSql`
  (`:169`) and `FindOpeningFence` (`:199`).
- Comments that go stale: the class header (`:12-16`) and the `SeededWindowStatement` comment
  (`:38-42`), which both describe the fence as a live second pin.

**Why the withdrawal is total rather than partial.** A containment assertion over the raw document —
keeping the file read while dropping the parser — looks like a cheap way to save doc-drift detection.
It is not: it keeps the repository-root walk and it still breaks the build when someone edits the
fenced block, which are two of the three objections the withdrawal rests on, and it is *weaker* than
what it replaces. Containment passes when a line is added inside the fence, and passes when the SQL
sits anywhere in the file rather than under its heading. Paying the full cost of the document-reading
path for a weaker guard is the worst of the three options, so the document keeps quoting SQL for a
reader and no test reads it back. That is the roadmap's Guard strategy verbatim and the standing rule
since `postgres-gap-reconstruction`.

**What is lost, recorded rather than hidden.** Nothing detects `data-integration.md` drifting from the
shipped SQL. Three slices remain that assemble a brief from that document, and whoever writes them
re-reads the quoted SQL against `ArchiveStatements.cs` by hand.

### Cluster 4 — constructor-assignment assertions and dead carriers

- `DataErrorTests.cs`, 179 lines. Constructor-assignment assertions: `:20`, `:46`, `:56`, `:71`,
  `:95`, `:107`, `:120`, `:133`, plus `EachArchiveStateStaysTellableApartThroughAFailedResult`
  (`:141`), which asserts that instances of different types have different types.
- Staying: `ConnectionFileInvalidErrorKeepsItsDiscriminator` (`:34`), the `ArgumentException` guard
  (`:88`), `EveryPublicErrorTypeIsSealedAndDerivesFromError` (`:166`).
- Deleting `:95` is safe: the `ArchiveObject.Database`-with-null-table arm is still constructed at
  `StartupFailureMapperTests.cs:99` and `ArchiveExceptionMapperTests.cs:57`.
- `StartupData.Settings` has no production consumer — settings reach the provider through the DI
  singleton at `PostgresDataServiceCollectionExtensions.cs:22`. Its XML doc is at `StartupData.cs:18`,
  and the `settings` value threads through `StartupProbe.Run` → `Read` → `ReadAsync` (`:63`, `:73`,
  `:111`, `:133`, `:146`). One assertion reads it: `StartupProbeTests.cs:204`, inside
  `Run_OverTheStubContainer_CarriesPensAndExtent`, which also asserts pens, extent and provider
  resolution — the assertion goes, the test stays.
- **`PostgresConnectionSettings.FileVersion` is a positional record parameter** (`:13`), so every
  construction site is a compile break: `PostgresConnectionLoader.cs:85` (production),
  `CompositionRootTests.cs:122`, `StartupProbeTests.cs:230`, `ArchiveProviderFactory.cs:47` (with its
  then-dead const at `:22`), `ConnectionSettingsFactory.cs:19`. It is read at
  `PostgresConnectionLoaderTests.cs:48` and printed at `PostgresConnectionSettings.cs:65`. That
  `ToString` has no caller in the tree, so trimming it changes no operator-visible text. The loader
  writes the value and never reads it back; `SupportedFileVersion` and `ValidateVersion` stay, so the
  file's version is still validated on load — it simply stops being carried afterwards.

### Not touched

- `StatementTimeoutReader`, `ArchiveQueryTimedOutError` and all seven error types with all their
  fields. `StartupFailureMapper.cs:150-155` gives the timeout's two arms different remedies, which is
  the bar `CLAUDE.md` sets, so the reader stays.
- `MissingRelationProbe` — slice `missing-relation-probe-removal`.
- `poll_interval_ms` and `PostgresConnectionSettings.PollInterval` — `postgres-live-edge-and-demo`
  reads them.
- The guard-ordering parity with the stub — dies with the stub in the closing slice.

### Ordering

The roadmap places this slice after `semibase-container-provisioning` by choice rather than
necessity: the harness's self-tests hold the provisioning swap steady and are deleted once it has
held. That slice is blocked on SemiBase publishing its image, so this one runs first.

ASSUMPTION: the consequence is acceptable. When the container slice later swaps provisioning, the
binary-resolution and provisioner self-tests will be gone, and what catches a fault is the gated
suite — a broken provisioner reports itself as the stated skip reason of the first gated test in any
run, and `SEMIPLOT_REQUIRE_DB` turns that skip into a failure on CI. Keeping `DatabaseGateTests` and
`TestEnvironmentTests` keeps that mechanism itself under test.

### Baseline

`SemiPlot.Tests` is 370 passed, 0 skipped, 0 failed
(`docs/plans/completed/20260821-linux-test-target.md:364`). The `SemiPlot.Tests.Data` count is
measured in Task 1 — it is gated, and depends on Docker and the `semibase` binary being present.

Measured at `fac6dfc` with `SEMIPLOT_REQUIRE_DB=1`, Docker 29.7.2 and `semibase` on `PATH`:
`SemiPlot.Tests.Data` is **422 passed, 0 skipped, 0 failed**. Zero skips is what proves the gated
tests ran rather than being waved through.

## Development Approach

- **Testing approach**: Regular. This slice deletes; the existing suites are the guard.
- Complete each task fully before moving to the next.
- **All tests must pass before starting the next task.**
- Deletion order matters once: Task 1 re-pins before Task 2 removes the pin it replaces.
- Line numbers here are read at `fac6dfc`. Task 1 edits `ArchiveStatementTextTests.cs`, so every later
  reference into that file names a **symbol**; find it by name, not by line.

## Testing Strategy

- **Unit tests**: several are deleted. Each deletion names what still covers the behaviour, or states
  plainly that nothing does — Cluster 2 carries a table of the seven harness behaviours that keep
  working with nothing asserting them.
- **Integration tests**: untouched. Every gated test in the suite is a guard for this slice.
- **Tests added**: two literal pins in Task 1, replacing an existing guard rather than adding
  coverage.

## Acceptance Evidence

**Evidence 1 — the solution builds.**
```powershell
dotnet build SemiPlot.slnx -c Release
```
Exit 0, zero warnings introduced.

**Evidence 2 — the UI suite is unchanged.**
```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
```
370 passed, 0 skipped, 0 failed. Task 5 edits two UI-project files (`StartupData.cs`,
`StartupProbe.cs`) and two files in `SemiPlot.Tests` (`StartupProbeTests.cs`,
`CompositionRootTests.cs`), and every one of those changes is behaviour-neutral, so the count must
not move. It moving is a finding.

**Evidence 3 — the data suite, with the gated tests actually running.**
```powershell
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
```
Zero failures, zero skips. `SEMIPLOT_REQUIRE_DB` is what turns an unavailable runtime from a skip
into a failure; without it a missing Docker daemon would let this slice pass while proving nothing.

**Evidence 4 — the replacement pins bite.** After Task 1, mutate one character of
`ArchiveStatements.PenCatalog` and run only that test class:
```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~ArchiveStatementTextTests" -c Release
```
**Two failures are expected**, not one: the still-live fence theory's `### Pen catalog` case and the
new literal pin. Both must appear, and the new pin's presence is what the evidence is for. The filter
excludes the gated catalogue reads, which would otherwise fail for a third and unrelated reason.
Revert afterwards and record the failure text here.

**Evidence 5 — the deletions are exactly the intended ones.** Capture test names at the Task 1
baseline and again at the end, then diff:
```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj --list-tests > $env:TEMP/tests-before.txt
# ... after the slice ...
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj --list-tests > $env:TEMP/tests-after.txt
git diff --no-index $env:TEMP/tests-before.txt $env:TEMP/tests-after.txt
```
Every removed name belongs to a cluster this plan names, and the only additions are the two literal
pins. A name diff rather than arithmetic, because xunit counts each `[InlineData]` separately and
hand-counting theory cases across the touched files is where a reconciliation fails silently. The
captures go to `$env:TEMP`, never inside the repository.

**Evidence 6 — formatting and encoding.**
```powershell
dotnet format SemiPlot.slnx --verify-no-changes
```
Exit 0. Every tracked `.cs` file still begins `ef bb bf`; no tracked `.md` gains a BOM.

**Evidence 7 — nothing references what was deleted.** Derive the symbol list from the diff rather
than by hand, then grep the tree for each survivor:
```bash
git diff master...HEAD -U0 | grep '^-' \
  | grep -oE '\b(StaleAfter|MarkerPrefix|StaleTemplatesCommand|ServerEpochCommand|StampAsync|DropStaleAsync|IsStale|DocumentPath|ReadDocument|FindRepositoryRoot|ExtractFencedSql|FindOpeningFence|FileVersion)\b' \
  | sort -u > deleted-symbols.txt
git grep -nF -f deleted-symbols.txt -- ':!docs/plans'
```
Any hit is either a survivor the plan missed or a dangling `<see cref="..."/>`. No project sets
`GenerateDocumentationFile`, so a dangling doc reference compiles silently and this grep is the only
thing that catches one. Delete `deleted-symbols.txt` afterwards.

**What Evidence 7 structurally cannot catch.** Its symbol list is derived from **removed** lines, so a
definition that survives while its only *use* is deleted is invisible to it: the surviving definition
never appears in the diff's `-` lines and so never enters the grep. Reviewing the deleted test files
for the members they were the last consumer of is the manual half. `ArchiveDatabase.CountDatabasesCommand`
was checked on that ground and keeps a consumer — `ArchiveDatabaseTests.CountDatabasesAsync` — so no
orphan was found; the blind spot is recorded because the evidence cannot close it, not because it bit.

**Evidence 8 — the change stayed inside its file list.**
```powershell
git diff master...HEAD --name-only
```
Every path appears in a task's **Files** block, and no path outside them.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update this plan if implementation deviates from the original scope

## Solution Overview

Task 1 adds the two replacement literals. Task 2 removes the fence machinery whole. Task 3 cuts the
sweep, Task 4 the two harness self-test files that have no policy behind them, Task 5 the constructor
assertions and the two dead carriers. Task 6 verifies, Task 7 corrects the documents the deletions
make wrong.

## Technical Details

**Why the sweep protects nothing on the path that matters.** `PostgresContainerFixture` starts a
container per run and disposes it at the end; template databases die with it. The sweep's subject —
templates outliving a run — exists only when `SEMIPLOT_TEST_PG` names a persistent server the fixture
did not create. This slice accepts manual cleanup there and says so in `bench.md`.

**Why two harness self-tests go and two stay.** Every gated test reaches the database through the
harness, so a broken binary resolution or a failed provisioner reports itself as the first gated
test's skip reason — or, under `SEMIPLOT_REQUIRE_DB`, as its failure. That argument covers
`SemibaseBinaryTests` and `SemibaseProvisionerTests`. It does not cover `DatabaseGateTests` or
`TestEnvironmentTests`, because those two *are* the mechanism the argument rests on: if
`TestEnvironment.DatabaseRequired` silently read false, the run would skip and pass, and no test in
the suite would say so.

**The shape of the replacement pin.** One plain literal per fence-only statement, in the shape
`SeededWindowStatement` already uses: the constant is compared against a copy held in the test file,
so an edit to the shipped SQL fails. It catches the code half and nothing else, which is the whole
of what survives the withdrawal.

## What Goes Where

- **Implementation Steps** — the deletions, the replacement literals, and every construction site
  that must move with a removed record parameter.
- **Post-Completion** — nothing manual; every acceptance item is a command.

## Implementation Steps

### Task 1: Replace the fence guard before removing it

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`

- [x] capture the baseline: run Evidence 3, and Evidence 5's `--list-tests` capture. Record the count
      in this plan — **422 passed, 0 skipped, 0 failed**, recorded under **Baseline** above. The name
      capture is at `$env:TEMP/tests-before.txt` (430 lines), outside the repository
- [x] give `ArchiveStatements.PenCatalog` and `ArchiveStatements.ArchiveExtent` a plain-literal pin
      each, in the shape `SeededWindowStatement` and its test already use — `PenCatalogStatement` with
      `ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter`, and `ArchiveExtentStatement` with
      `TheArchiveExtentStatementMatchesItsLiteralCharacterForCharacter`
- [x] add **no** literal for `SparseHistoryWindow` — it already has one, and a second is a duplicate
- [x] leave the fence theory in place — this task only adds
- [x] run Evidence 4 and record both expected failures here
- [x] run Evidence 3 — nothing fails — **424 passed, 0 skipped, 0 failed**, the baseline plus the two
      new pins

**Evidence 4 as run.** Mutation: `ArchiveStatements.PenCatalog` line 26, `color` → `colar`, one
character. `dotnet test ... --filter "FullyQualifiedName~ArchiveStatementTextTests" -c Release`
reported **2 failed, 9 passed, 11 total** — both expected failures, with the same diff:

```
EachDocumentedStatementMatchesTheConstantCharacterForCharacter(heading: "### Pen catalog", ...) [FAIL]
  Assert.Equal() Failure: Strings differ
                                      ↓ (pos 32)
  Expected: ···"id, name, group_name, color, line_style\nFROM semip"···
  Actual:   ···"id, name, group_name, colar, line_style\nFROM semip"···

ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter [FAIL]
  Assert.Equal() Failure: Strings differ
                                      ↓ (pos 32)
  Expected: ···"id, name, group_name, color, line_style\nFROM semip"···
  Actual:   ···"id, name, group_name, colar, line_style\nFROM semip"···
```

The new pin reports the drift on its own, which is what the evidence is for. The mutation was
reverted with `git checkout --`, and the class ran 11 passed, 0 failed again.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

### Task 2: Remove the fence machinery

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`

- [x] delete `EachDocumentedStatementMatchesTheConstantCharacterForCharacter`, the four extractor
      self-tests (`AMissingDocumentFails…`, `AMissingHeadingFails…`, `AHeadingWithNoFenceFails…`,
      `AnUnclosedFenceFails…`), `DocumentPath`, `ReadDocument`, `FindRepositoryRoot`,
      `ExtractFencedSql` and `FindOpeningFence`. Nothing in the class reads a file afterwards — no
      `File.`, `Directory.` or path constant survives in it
- [x] keep `TheWindowBinderNamesExactlyTheStatementsOwnParameters` and `_parameterTokenPattern`.
      ⚠️ `Normalise` was kept here and dropped in a later review round: with the fence gone every
      pin compares a literal in this file against a constant in the same repository, so normalising
      line endings only weakened the comparison. The pins now compare raw, which is stricter
- [x] rewrite the class header comment and the `SeededWindowStatement` comment, both of which
      describe the fence as a live second pin. The `PenCatalogStatement` and
      `ArchiveExtentStatement` comments added in Task 1 also said "beside the fence above", so both
      lost that clause too — three words each, same staleness, same file.
      ⚠️ The rewritten header first also stated what the withdrawal costs — nothing detects
      `data-integration.md` drifting from the shipped SQL. That sentence is gone from the header:
      the cost is a property of the repository rather than of this class, and it is recorded where a
      reader meets it, in `testing-strategy.md`, in `data-integration.md`'s "Keeping this document
      honest" opening and in the roadmap slice. The header as shipped states only the standing
      rule — a plain literal per operational statement compared against the constant, the two
      cold-path diagnostics excluded, and a new operational statement gaining a literal here
- [x] run Evidence 3 — nothing fails — **417 passed, 0 skipped, 0 failed**, the 424 of Task 1 less
      the seven deleted tests (the fence theory's three `[InlineData]` cases plus the four extractor
      self-tests). `dotnet format SemiPlot.slnx --verify-no-changes` exits 0

### Task 3: Remove the stale-template sweep

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/StaleTemplateRuleTests.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplateTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixtureTests.cs`
- Modify: `docs/architecture/bench.md`

- [x] delete `StaleAfter`, `MarkerPrefix`, `StaleTemplatesCommand`, `ServerEpochCommand`,
      `StampAsync`, `DropStaleAsync` and `IsStale` from `ArchiveTemplate`, with both call sites
- [x] delete both test files outright — every test in each is a sweep test
- [x] re-anchor the PostgreSQL-floor comment at `PostgresContainerFixtureTests.cs:32-35` on what the
      bench actually executes — `DROP DATABASE ... WITH (FORCE)` (13) and COPY routing into a
      partitioned parent (10) — with 14 stated as a deliberate margin over them. **Keep the floor and
      the assertion at 14** and keep the test's name: `starts_with` never justified 14 — it is
      PostgreSQL 11 — so removing it corrects a wrong reason rather than lowering a real requirement.
      ⚠️ The first re-anchoring named `date_bin` as a requirement a shipped statement makes. It is not:
      `date_bin` occurs in no `.cs` file, only in the bucketing statement of the DROPPED
      `postgres-bucketed-read`. A later round demoted it to conditional and the round after that cut
      it: the comment as shipped names `date_bin` nowhere and gives one reason for the floor, a
      deliberate margin over the 13 the bench executes. `postgres-instance.md:24-28` keeps the
      conditional reading, being the document that declares the floor
- [x] record in `bench.md` that a developer using `SEMIPLOT_TEST_PG` drops `semiplot_bench_*` by
      hand, and that the container path needs no cleanup because the server dies with the run.
      ⚠️ A later review round added `semiplot_clone_*` to that note: a run killed between
      `ArchiveDatabase.CloneAsync` and its disposal leaves a clone behind, and the note replaces the
      only sweep either prefix ever had
- [x] run Evidence 3 — nothing fails — **401 passed, 0 skipped, 0 failed**, the 417 of Task 2 less the
      sixteen deleted tests (ArchiveTemplateTests: six `[InlineData]` cases plus one `[Fact]`;
      StaleTemplateRuleTests: three `[Fact]`s plus six `[InlineData]` cases).
      `dotnet format SemiPlot.slnx --verify-no-changes` exits 0

### Task 4: Remove the two harness self-tests with no policy behind them

**Files:**
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinaryTests.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseProvisionerTests.cs`

- [x] delete those two files
- [x] **keep `DatabaseGateTests.cs` and `TestEnvironmentTests.cs`** — together they are the
      skip-versus-fail policy every acceptance run in this repository depends on, and neither half
      asserts the other's subject
- [x] keep `PostgresContainerFixtureTests.cs` whole
- [x] confirm `SemibaseBinary`, `SemibaseProvisioner` and `TestEnvironment` themselves are untouched.
      `SemibaseBinary.Resolve`'s two-argument overload loses its last external caller and becomes
      decomposition rather than a test seam; leave its visibility alone, since
      `semibase-container-provisioning` deletes the type. `SemibaseBinary.WindowsFileName` and
      `UnixFileName` likewise lose their last external callers but stay read by the private
      `FileNames()`; their visibility is left alone for the same reason.
      `ProcessStateCollection` keeps two consumers: `TestEnvironmentTests` and
      `SeederEntryPointTests`. `ArchiveTemplate.NamePrefix` is the same situation one task earlier:
      `ArchiveTemplateTests` was its last external consumer, and `ComputeName` in its own file now
      reads it alone. Its visibility is left alone too — this slice narrows no modifier anywhere,
      and the constant names the `semiplot_bench_` prefix `bench.md` tells a `SEMIPLOT_TEST_PG`
      developer to drop by hand
- [x] run Evidence 3 — nothing fails — **392 passed, 0 skipped, 0 failed**, the 401 of Task 3
      less the nine deleted tests (`SemibaseBinaryTests`: seven `[Fact]`s;
      `SemibaseProvisionerTests`: two). `dotnet format SemiPlot.slnx --verify-no-changes` exits 0

### Task 5: Remove the constructor assertions and the dead carriers

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Errors/DataErrorTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveProviderFactory.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ConnectionSettingsFactory.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresConnectionLoaderTests.cs`

- [x] delete the constructor-assignment assertions (`:20`, `:46`, `:56`, `:71`, `:95`, `:107`,
      `:120`, `:133`) and `EachArchiveStateStaysTellableApartThroughAFailedResult` (`:141`)
- [x] keep `ConnectionFileInvalidErrorKeepsItsDiscriminator`, the `ArgumentException` guard and
      `EveryPublicErrorTypeIsSealedAndDerivesFromError`
- [x] remove `StartupData.Settings`, its XML doc, and the `settings` value threaded through
      `StartupProbe.Run` → `Read` → `ReadAsync`
- [x] remove the assertion at `StartupProbeTests.cs:204`, keeping the rest of
      `Run_OverTheStubContainer_CarriesPensAndExtent` — the test stays, one line goes
- [x] remove `PostgresConnectionSettings.FileVersion` and its `ToString` segment, then fix **every**
      construction site: `PostgresConnectionLoader.cs:85`, `CompositionRootTests.cs:122`,
      `StartupProbeTests.cs:230`, `ArchiveProviderFactory.cs:47` with its then-dead const at `:22`,
      and `ConnectionSettingsFactory.cs:19`
- [x] remove the read at `PostgresConnectionLoaderTests.cs:48`, keeping the rest of
      `AValidFilePopulatesEveryField`
- [x] keep `SupportedFileVersion` and `ValidateVersion` — the version is still validated on load.
      ⚠️ A later review round narrowed the const to `private`: `CompositionRootTests.cs:122` and
      `StartupProbeTests.cs:230` were its only readers outside the class and both went with
      `FileVersion` here, leaving `ValidateVersion` as the sole reader
- [x] run Evidence 1, 2 and 3 — Evidence 1 built with **0 warnings, 0 errors**; Evidence 2 reported
      **370 passed, 0 skipped, 0 failed**, unchanged as required; Evidence 3 reported
      **382 passed, 0 skipped, 0 failed**, the 392 of Task 4 less the ten deleted `DataErrorTests`
      cases (eight `[Fact]`s plus the two `[InlineData]` cases of
      `ArchiveNotInitialisedErrorCarriesTheMissingTable`).
      `dotnet format SemiPlot.slnx --verify-no-changes` exits 0

⚠️ The plan says `PostgresConnectionSettings.ToString()` "has no caller in the tree". It has one:
`PostgresConnectionLoaderTests.TheSettingsNeverPrintThePassword` reads it. That test asserts only that
the password is absent and `scada-01` present, so trimming the `FileVersion` segment left it green and
the claim's substance — no operator-visible text moves — still holds. Five construction sites, exactly
as listed; no sixth.

### Task 6: Verify acceptance criteria

- [x] run every Evidence item and record what each reported
- [x] run Evidence 5's name diff and confirm every removed test belongs to a cluster this plan names,
      and that the only additions are the two literal pins
- [x] confirm `SemiPlot.Tests` is still 370 passed, 0 skipped, 0 failed — Task 5's UI edits are
      behaviour-neutral, so a moved count is a finding rather than an expected drop
- [x] run Evidence 7 and confirm no reference to a deleted symbol survives
- [x] run Evidence 8 and confirm the changed-file list matches the tasks' **Files** blocks exactly
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0
- [x] confirm every tracked `.cs` file still begins `ef bb bf` and no tracked `.md` gained one
- [x] confirm no capture file or symbol list was left inside the repository

**Every item was run at `e5af5df`**, the branch tip when this task ran, rather than trusted from
what earlier tasks recorded. Docker 29.7.2 running, `semibase` on `PATH` at
`/c/Users/admin/bin/semibase`.

**Why the figures below still hold at the branch tip.** Everything committed after that run is a
review round, and every one of them has changed comments, Markdown, a visibility modifier or a
helper private to a test class — never a test's existence or name, never a shipped statement or
symbol. Evidence 1, 2, 3 and 6 are cheap and are re-run in each such round; each round has reported
the figures below unchanged. Evidence 4, 5 and 7 read test names, pin behaviour and shipped symbols,
which a change of that shape cannot move, so they are not re-run. The one round that touched pin
behaviour dropped `Normalise`, tightening each pin from a newline-normalised comparison to a raw
one — strictly narrower, and Evidence 3 green after it. A round that adds, removes or renames a
test, or edits a shipped statement, falls outside this rule: it re-runs all eight items and rewrites
this paragraph.

| Evidence | Expected | Reported |
| --- | --- | --- |
| 1 — build | exit 0, no new warnings | exit 0, **0 warnings, 0 errors** |
| 2 — UI suite | 370 / 0 / 0 | **370 passed, 0 skipped, 0 failed** |
| 3 — data suite, gated | zero failures, zero skips | **382 passed, 0 skipped, 0 failed** |
| 4 — the pins bite | see note below | **1 failed, 3 passed, 4 total** |
| 5 — name diff | removals all in a named cluster, two additions | 42 removed, 2 added |
| 6 — format | exit 0 | exit 0 |
| 7 — no surviving reference | no hit | 5 substring hits, all false — see below |
| 8 — file list | inside the tasks' **Files** blocks | 22 of 23 named; the 23rd is this plan |

**Evidence 3 reconciles with the baseline.** 422 − 42 + 2 = 382, and the `--list-tests` line counts
agree: 430 − 42 + 2 = 390.

**Evidence 4 re-run — one failure now, not two, and by design.** Mutation repeated exactly:
`ArchiveStatements.PenCatalog`, `color` → `colar`, one character.
`dotnet test ... --filter "FullyQualifiedName~ArchiveStatementTextTests" -c Release` reported
**1 failed, 3 passed, 4 total**:

```
SemiPlot.Tests.Data.Postgres.ArchiveStatementTextTests.ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter [FAIL]
  Assert.Equal() Failure: Strings differ
                                      ↓ (pos 32)
  Expected: ···"id, name, group_name, color, line_style\nFROM semip"···
  Actual:   ···"id, name, group_name, colar, line_style\nFROM semip"···
```

The Evidence 4 text expects two failures because it is scoped to "**After Task 1**", when the fence
theory was still live and contributed the `### Pen catalog` case. Task 2 deleted that theory, which is
the whole point of the slice, so at the branch tip the surviving pin is the only thing that can fail —
and it does. That is the pin carrying the guard alone, which is what the evidence exists to show. Both
failures are recorded verbatim under Task 1, at the state the evidence names. Mutation reverted with
`git checkout --`; the class runs 4 passed, 0 failed and the working tree is clean.

**Evidence 7 — five hits, none of them a surviving reference.** The command as the plan writes it uses
`git grep -nF`, a fixed-**substring** match, so the symbol `FileVersion` matches inside two names the
plan keeps on purpose:

```
SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionDto.cs:10:    public string? ConnectionFileVersion { get; set; }
SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:18:  public const string SupportedFileVersion = "1.0";
SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:51:  var version = ValidateVersion(filePath, dto.ConnectionFileVersion);
SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:152: if (!string.Equals(foundVersion, SupportedFileVersion, StringComparison.Ordinal))
SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:158:     $"the file is version '{foundVersion}', not the supported '{SupportedFileVersion}'"));
```

`SupportedFileVersion` and `ValidateVersion` are the two Task 5 states plainly that it keeps, and
`ConnectionFileVersion` is the DTO field the loader still reads off the file. Re-run word-anchored,
`git grep -nE '\b(StaleAfter|…|FileVersion)\b' -- ':!docs/plans'` returns **no match** — no reference
to the deleted `PostgresConnectionSettings.FileVersion` survives. The deleted test types and members
(`ArchiveTemplateTests`, `StaleTemplateRuleTests`, `SemibaseBinaryTests`, `SemibaseProvisionerTests`,
`EachDocumentedStatementMatchesTheConstantCharacterForCharacter`,
`EachArchiveStateStaysTellableApartThroughAFailedResult`, `StartupData.Settings`) likewise return no
match outside `docs/plans`.

**The dangling-cref half, checked stronger than a grep.** No project sets `GenerateDocumentationFile`,
so `dotnet build SemiPlot.slnx -c Release -p:GenerateDocumentationFile=true -p:NoWarn=CS1591` was run
once to make the compiler resolve all 57 `cref=` targets in the tree. It reported **0 warnings,
0 errors** — no `CS1574`, so no XML doc reference dangles. Build outputs only; nothing tracked changed.

**Evidence 5 — the 42 removed names, every one inside a named cluster.** Additions are exactly
`ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter` and
`TheArchiveExtentStatementMatchesItsLiteralCharacterForCharacter`, the two literal pins.

| Cluster | Removed | Count |
| --- | --- | --- |
| 1 — stale-template sweep | `ArchiveTemplateTests` (6 `[InlineData]` + 1 `[Fact]`), `StaleTemplateRuleTests` (3 `[Fact]` + 6 `[InlineData]`) | 16 |
| 2 — harness self-tests | `SemibaseBinaryTests` (7), `SemibaseProvisionerTests` (2) | 9 |
| 3 — document fence | `EachDocumentedStatementMatchesTheConstantCharacterForCharacter` (3 cases) + 4 extractor self-tests | 7 |
| 4 — constructor assertions | `DataErrorTests`: 8 `[Fact]`s, the 2 `ArchiveNotInitialisedErrorCarriesTheMissingTable` cases | 10 |

No removed name falls outside those four. `EachArchiveStateStaysTellableApartThroughAFailedResult` is
counted in cluster 4 with the constructor assertions, where the plan names it.

**Evidence 8 — 23 paths, 22 of them named by a task.** Re-measured at the tip of this review round;
the count grew as Task 7 ran and as the rounds after it corrected the documents Task 7 touched. The
one path no task's **Files** block names is `docs/plans/20260821-harness-and-cold-path-cleanup.md`,
this plan itself, which every task's own checkboxes require editing. A plan never lists itself, so
this is not scope leak. `data-integration.md` and `postgres-instance.md` rode in unrecorded until
this round and are now named by Task 7, the documentation task that owns them. No path a task names
is missing from the diff.

**Encoding.** All 201 tracked `.cs` files begin `ef bb bf`. One of 37 tracked `.md` files carries a
BOM, `docs/plans/completed/20260819-postgres-wire-up.md` — pre-existing: `git show master:` on it
returns the same three bytes and `git diff master...HEAD` on it is empty. This branch adds no BOM to a
Markdown file.

**Nothing left in the repository.** `git status --porcelain` is empty; the `--list-tests` captures and
`deleted-symbols.txt` all live under `$env:TEMP`, never inside the tree.

### Task 7: [Final] Update documentation

**Files:**
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/postgres-instance.md`

- [x] the roadmap's slice scope says the fence withdrawal keeps "one containment assertion per
      statement over the raw document". This slice withdraws fully instead, for the reason recorded
      in Cluster 3, so correct that sentence. The scope sentence now says the machinery goes whole
      and names why a containment assertion is not the cheaper half: it keeps the repository-root
      walk, still breaks the build on a documentation edit, and passes when a line is added inside
      the fence or the SQL sits anywhere in the file.
      ⚠️ This checkbox first recorded the **Guard strategy** section as already correct and left
      alone, quoting it at roadmap `:182-183`. That held only until the review round after it
      rewrote the section — it described the deleted fence machinery in the present tense — so the
      sentence this checkbox quoted no longer exists. The ⚠️ closing this task records that rewrite
- [x] correct the roadmap slice's line figure, which still says roughly 1,300 from before the
      cold-path cluster moved out — now **roughly 750**. The roadmap carries the rounded shape on
      purpose: a review round that moves a comment by four lines moves an exact count and leaves it
      wrong, while the shape survives. Measured at `72c5784` with
      `git diff master...HEAD --shortstat -- ':!docs'`: 17 files, 54 insertions, 801 deletions, a
      net 747 lines out of the tree. That agrees with this plan's own **Overview**
- [x] run `bash C:/Users/admin/.claude/plugins/cache/confs-cc/planning/3.18.0/skills/roadmap/scripts/check-inert.sh docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
      and confirm `inert`; keep the document's prose wrapped at 100 characters — the script printed
      **inert**, and no line the edit introduced exceeds 100 characters
- [x] `testing-strategy.md:58` describes how statement text is pinned; state that it is now a plain
      literal per statement and that nothing compares the document to the code. ⚠️ The cited line does
      not say that: line 58 lists statement text among the seams the gated PostgreSQL integration
      tests guard, which is about executing the statements and stays true. The document described the
      pinning mechanism nowhere, so the passage was added rather than corrected — a row in the unit
      test table naming `ArchiveStatementTextTests.cs`, and a paragraph after the ungated-unit rule
      stating one plain literal per **operational** statement, the one binder that takes parameters
      pinned against its own statement's parameter names, and that nothing compares the shipped SQL
      to `data-integration.md`.
      ⚠️ The paragraph first said "per statement", which claims a completeness that does not hold:
      `ArchiveStatements` holds five statements and three carry a literal. `EffectiveStatementTimeout`
      and `RelationProbe` are cold-path diagnostics and carry none — the fence never covered them
      either, so this is a wording correction, not a regression. The paragraph names the three the
      read path issues. The `ArchiveStatementTextTests` class header states the rule instead — a
      literal per operational statement, the two diagnostics excluded — and names no statement, so a
      fourth operational statement cannot leave it stale.
      ⚠️ The round after that found the same over-claim still live in two further documents:
      `data-integration.md`'s "Keeping this document honest" list and the roadmap's **Guard
      strategy** bullet both read "each shipped statement" and "each binder". Both now name the
      three operational statements, the two cold-path diagnostics that carry no literal, and
      `PostgresDataProvider.BindWindow` as the only binder — `SparseHistoryWindow` is the only
      statement taking parameters. `testing-strategy.md`'s own binder clause was narrowed the same
      way
- ➕ [x] `postgres-instance.md:25` justified the declared 14 floor with "the bucketing query uses
      `date_bin`, added in 14" — present tense about a query no shipped statement contains, whose
      slice `postgres-bucketed-read` is dropped. The bullet now states 14 as a deliberate margin
      over the 13 the bench executes (`DROP DATABASE ... WITH (FORCE)`) and names `date_bin` as
      what the documented bucketing query would read if that slice were revived. It rides along
      rather than waiting for a slice of its own because Task 3 rewrote the same floor's
      justification in `PostgresContainerFixtureTests`: one slice touching one of two statements of
      one floor and leaving the other on a reason it had just rejected is a diff that reads as an
      oversight.
      ⚠️ That coupling was tighter when this item was added than it is now. The fixture comment
      then named `date_bin` as conditional and this document named it as current, a plain
      contradiction; a later round cut `date_bin` from the fixture comment altogether, so the two no
      longer disagree — `postgres-instance.md:24-28` simply says more, being the document that
      declares the floor. The ride-along still stands on the shared reason, not on a contradiction
- [x] deferred to the delivery step — exec never moves the plan; `ship` archives it once the
      operator has tested the branch

⚠️ One roadmap inaccuracy was found in this task and left alone at the time, then corrected in the
review round that followed: the slice scope said "`DatabaseGateTests` is the exception that stays",
where two files stayed — Task 4 kept `TestEnvironmentTests` as well, and Cluster 2 explains why the
two halves of the skip-versus-fail policy cannot be split. The scope now names both. The **Guard
strategy** section was corrected in the same round: it described the deleted fence machinery in the
present tense, which stops being true the moment this slice lands, so it now states the plain literal
as the pin and the fence comparison as what preceded it.

**Build after the edits.** `dotnet build SemiPlot.slnx -c Release` reports **0 warnings, 0 errors**.
This task changes Markdown only, so no test observes it; the guards are `check-inert.sh`, which
prints `inert`, and the 100-character wrap, which no introduced line exceeds. Neither document
carries a BOM.

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Nothing requires manual verification.** Every acceptance item is a command. The gated suite needs
Docker and the `semibase` binary; with `SEMIPLOT_REQUIRE_DB=1` their absence is a failure rather than
a silent pass, which is what makes Evidence 3 meaningful.

**What this slice bets on.** Two things, both stated rather than hidden. A developer using
`SEMIPLOT_TEST_PG` drops `semiplot_bench_*` and `semiplot_clone_*` databases by hand when they
accumulate, with a note in `bench.md` as the only reminder. And whoever assembles the remaining
slice briefs from `data-integration.md` re-reads by hand the three blocks that have a constant —
the pen catalogue, the archive extent and the sparse history window — against
`ArchiveStatements.cs`, because no test does it any more. The document's other three SQL blocks
quote slices that have not shipped and name no constant, so nothing can be read back against them
yet.

**Remaining slices**

- `semibase-container-provisioning` — the bench provisions from a container instead of a binary
  resolved from `PATH`; blocked until SemiBase publishes its image.
- `missing-relation-probe-removal` — the `42P01` probe goes; its static fallbacks already answer.
- `archive-schema-ownership` — the archive DDL moves to SemiBase behind a flag; blocked until SemiBase
  ships the flag and the `verify` column check.
- `postgres-live-edge-and-demo` — the realtime poll, the fresh tail, the `--follow` writer and the
  stub's retirement.

**Executed by exec:**

- branch: harness-and-cold-path-cleanup

## Verify it yourself

This slice deletes. Nothing it removes has an observable behaviour, so the verification is not "does
the feature work" but "did the deletions take only what they were meant to". Four checks, in order.

**1. Both suites are unchanged.** The UI suite is untouched by this slice and must not move. The data
suite drops by exactly the tests deleted.

```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
```

370 passed / 0 skipped / 0 failed, and 382 passed / 0 skipped / 0 failed. `SEMIPLOT_REQUIRE_DB=1` is
what makes the second number mean anything: without it a missing Docker daemon turns every gated test
into a skip and the run still exits zero. Zero skips is the part to read.

**2. The arithmetic closes.** `master` reports 422. This branch removes 42 tests and adds 2, and
422 − 42 + 2 = 382. Every removed name belongs to one of the four clusters the plan lists; the two
added are the literal pins for the pen catalogue and the archive extent.

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj --list-tests | Measure-Object -Line
```

390 lines against `master`'s 430.

**3. The replacement pins bite.** The document-fence comparison is gone; what replaces it is a plain
literal per operational statement. To see one fire, change any character inside
`ArchiveStatements.PenCatalog` and run the class alone:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~ArchiveStatementTextTests" -c Release
```

One failure, `ThePenCatalogStatementMatchesItsLiteralCharacterForCharacter`. Revert afterwards.

**4. Nothing references what was deleted.** No project sets `GenerateDocumentationFile`, so a
`<see cref="..."/>` pointing at a deleted member compiles silently. Force the check:

```powershell
dotnet build SemiPlot.slnx -c Release -p:GenerateDocumentationFile=true -p:NoWarn=CS1591
```

Zero warnings; all 57 `cref` targets resolve.

**What this slice gives up, so you can weigh it rather than discover it.** Nothing detects
`data-integration.md` drifting from the shipped SQL any more — that document quotes six SQL blocks, of
which three have a constant behind them and three belong to slices not yet shipped, and a reader is
now the only thing checking any of them. Seven harness behaviours keep working with nothing asserting
them; the plan's Cluster 2 names each one. And a developer using `SEMIPLOT_TEST_PG` accumulates
`semiplot_bench_*` and `semiplot_clone_*` databases with only a note in `bench.md` as the reminder —
on the container path the server dies with the run and there is nothing to accumulate.
