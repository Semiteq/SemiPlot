# PostgreSQL data source roadmap

**Issues:** none declared — the repository has no issue tracker in use. This roadmap covers the
whole span from "the viewer runs on synthetic data" to "the viewer reads a real Simple-Scada
archive", sliced into twelve independently shippable pull requests.

**Amended 2026-08-14** after the archive-populator sanity review: the bench is two solution
projects provisioned by SemiBase (verified: `v0.1.0` at `aa037a4`, all commands cross-platform,
its CI provisions a Linux container by running `all` twice, and the `v0.1.0` release ships
`linux_amd64`/`windows_amd64` binaries, so no consumer needs a Go toolchain); the seeder populates
`semiplot_tags`;
failure handling adopts the SemiStep typed-results discipline; the stub fallback is removed from
the composition slice; and a final slice replaces the stub with a live demo bench.

**Amended 2026-08-19** after provider-simplification was found to be scoped from statements this
document made before any code existed: the statement-pinning guard now records the mechanism that
shipped, and that slice keeps `MissingRelationProbe` and the shipped pinning. A shipped decision
that supersedes something written here amends this document in the same pull request — leaving the
two to disagree is what let a regression be scoped as a return to plan.

## Summary

SemiPlot renders trends on synthetic data: the running application resolves `IDataProvider` to the
stub, which emits random walks. `PostgresDataProvider` stands beside it and reads the pen catalogue
and the archive extent from a real database, but its history and realtime members are still
unimplemented and no composition selects it. The architecture for reading the Simple-Scada 2 PostgreSQL
archive is settled and documented, and the aggregation-layer ladder already picks a resolution by
the rule that architecture states. Twelve slices deliver a production provider, the local test bench
it is developed against, and a live demo bench that retires the synthetic stub. The roadmap closes
when the application, pointed at a populated database, draws real history, follows the live edge,
selects archive layers by window width, and the stub project is gone.

**Thesis:** every resolution the trend canvas needs already exists in the vendor's archive, so the
provider only has to choose a layer, reduce it to the canvas width, and reconstruct gaps — it never
has to maintain data of its own.

**Verified against code on 2026-08-10 (`bef4823`). Baseline at that ref: solution builds, 250 tests
pass, zero failures. Trust rule: prefer the shapes over the numbers if they have drifted.**

## Root cause

The provider seam was designed early and honoured — the UI depends only on `IDataProvider`, and the
stub is swappable. What followed late is a real implementation, and it is still half-built: the
PostgreSQL provider reads the catalogue and the extent, the application reads neither. Two
consequences compound.

First, everything downstream of the seam has been validated only against synthetic data whose shape
does not match the archive: the stub emits evenly spaced samples, while the archive writes anchor
pairs on change, leaves long stretches with no rows at all when a value is steady, and marks breaks
in a quality column the stub does not model.

Second, the layer machinery is right on paper and unconfirmed in the field. It follows the vendor's
writing rule — up to four points per period, so a layer's point spacing is a quarter of its period,
15 s, 15 min and 6 h — and `ChartNavigationController.LayerForWidth` derives every ceiling from that
spacing and the live canvas column count. Against the stub a wrong choice draws the same curve as a
right one, so only a run against a real archive can tell the two apart.

| Area | State today |
| --- | --- |
| `IDataProvider` implementations | Two: the stub the application resolves, and `PostgresDataProvider`, which reads the catalogue and the extent but neither history nor the live edge |
| Layer selection | Ceilings derived from each layer's point spacing and the live canvas column count; the choice is unconfirmed against a real archive |
| Gap rendering | Modelled synthetically; the archive's quality marks are not read at all |
| Time handling | `ArchiveTimeConverter` owns the naive-local-to-UTC boundary at the provider edge |
| Tag identity | `semiplot_tags` maps numbers to names, read through `PenLineStyleReader`; the application still lists synthetic pens |
| Test bench | A seeded local database, its gated harness and DB-free fixture rows exist |

## Target end state

| Concern | Today | Target |
| --- | --- | --- |
| Production data source | `RandomStubDataProvider` | `PostgresDataProvider`, selected by configuration; a missing or invalid configuration is a visible error state, never a silent stub |
| Synthetic stub | composition-root default | project deleted; manual "see something in the UI" runs on a seeded live demo database through the real provider |
| Failure reporting | two decoupled error planes (SemiStep pattern): nine sealed public types with structured fields in `SemiPlot/SemiPlot.Core/Data/Errors`, internal detail riding `CausedBy` into the log | the same two planes, over a narrower vocabulary, with every public type mapped to a UI state by one exhaustive `switch` the compiler checks |
| Layer spacing | period ÷ 4 (15 s / 15 min / 6 h) | unchanged; confirmed by eye against a real archive in the live-demo slice |
| Layer thresholds | derived from `window / targetColumnCount ≥ spacing`, hysteresis retained | unchanged |
| Wide-window reduction | client-side only | server-side pixel buckets when the layer is denser than the canvas |
| Gaps | synthetic | reconstructed from `q = 32` / `q = 16`, distinguished from unchanged values |
| Timestamps | `ArchiveTimeConverter` converts both ways at the provider boundary; no application path reaches it, so the running viewer is UTC throughout | converted from naive local at the provider boundary, UTC above it |
| Pen catalogue | the provider reads `semiplot_tags`; the application lists the stub's synthetic pens | `semiplot_tags`, filled by hand |
| Test bench | a populated local database with archive-shaped data, plus DB-free tests over fixture rows | unchanged; later slices develop against it |

Every architectural choice behind this table is already recorded: `docs/architecture/scada-archive.md`
for the archive, `data-integration.md` for the contract and the read path, `postgres-instance.md`
for the server, `history-read-path-evaluation.md` for why nothing of ours runs inside the database.

## Why it is safe

The blast radius is bounded by the provider seam, which was built for exactly this substitution.

`IDataProvider` is referenced from 14 files: its own definition, the stub and its DI extension, the
PostgreSQL provider and its DI extension, `App.axaml.cs`, `TrendCoordinator`, `MinimapViewModel`,
and six test files including `FakeDataProvider`. Adding a second implementation touches none of them
except the composition root: the PostgreSQL provider, its DI extension and its tests arrived beside
the stub across the merged slices, and the composition root still resolves the stub — the startup
slice is where it switches.

`AggregationLayer` is referenced from 20 files and no remaining slice changes it: the enum, its
ordering, its use as a request field and the point spacing it exposes are settled. What the slices
below change is which provider answers a request and how the answer is read, never how a layer is
chosen.

The database side is additive only, and the customer's production archive is read-only throughout:
no slice inserts a row into its `trends` or `messages`, creates an index on them, or attaches a
trigger. Writing belongs to the bench alone, in a development database of our own —
`sql/semiplot_dev.sql` creates `public.trends` and its `tpdefault` catch-all there, the seeder fills
them, and the live-demo slice keeps appending. This repository creates no object in an archive at
all: `semiplot_tags` is created by `semibase create`, which owns every role, grant and table on that
side.

## Guard strategy

Each guard below is a hypothesis the owning slice plan must confirm fires at HEAD before relying on
it.

- **The existing suite.** Both test projects pass with zero failures, the only skips being the
  database-gated ones, and every slice below inherits that state: a failure in it is a regression,
  not a pending update. The total is evidence, not the guard — it moves with every slice that adds a
  test, so it is measured rather than tracked: at `58181ec`, `SemiPlot.Tests` passes 286 and
  `SemiPlot.Tests.Data` 330 of 365, the 35 skipped for want of a database.
  `AggregationLayerTests`, `ChartNavigationControllerTests` and `RandomStubDataProviderTests` hold
  the ladder's numbers — each layer's point spacing, the ceilings derived from it and the hysteresis
  band — so a slice that moves a rung by accident fails there first.
- **Statement-text pinning.** Every operational statement lives in one class,
  `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`, and
  `docs/architecture/data-integration.md` quotes each one in a fenced block under a stable heading.
  `ArchiveStatementTextTests` reads those fences at run time and asserts constant-equals-fence, so an
  edit to either side alone fails; binders are pinned against their statement's own parameter names.
  A literal held in the test file would catch the code half only, and it is the weaker guard rather
  than the cheaper one: the document is the artifact each slice's brief is assembled from, so a fence
  that silently stops describing the shipped statement corrupts the next slice's plan while every
  test stays green. The document also quotes statements no slice has built yet, which is what makes
  it lead the code rather than trail it.
- **`EXPLAIN` assertions.** Gated integration tests assert the plan's shape for the extent statement,
  the windowed history query and the realtime poll: an index scan under each bounded subquery, and no
  sequential scan of a `trends` partition holding rows. The plan cannot name `tpk` — it is the parent
  partitioned index of a `PARTITION BY RANGE (t)` table and is never scanned, so `EXPLAIN` prints
  each partition's own cloned `<partition>_pkey`. The shape assertion survives partition renaming and
  turns the documented hazards — a missing layer predicate, a missing variable list and an unbounded
  extent minimum — into enforced invariants.
- **Gated integration suite.** Database-touching tests skip cleanly when no server answers, so the
  default suite stays green on a machine without one.
- **Fixture rows from a real archive.** Envelope assembly and gap reconstruction are tested against
  rows extracted from a real Simple-Scada dump, not against rows we imagined.
- **Typed failure assertions.** Every operator-visible failure is a sealed public error class
  carrying structured fields; tests assert `result.HasError<T>()` on type and fields, never on
  message text and never on log output. Internal errors and log strings stay free to change — only
  the public plane is pinned.
- **Exhaustive error-to-state mapping.** The UI maps public error types to states in one `switch`
  over the error vocabulary, written without a catch-all arm so the compiler reports an unhandled
  type (added in the composition slice). A new public error type cannot silently leak past the
  operator, and an internal error cannot silently become public. SemiStep's
  `CoreErrorLocalizationCoverageTests` gets the same guarantee by reflection at run time; the
  compiler-checked form costs no test, no reflection, and nothing that grows with the vocabulary.

## Slices

### Slice layer-ladder-spacing — Status: DONE
- **Scope:** Correct the aggregation-layer arithmetic. `AggregationLayer` exposes each layer's point
  spacing — a quarter of its period, so 15 s, 15 min and 6 h — instead of returning the period
  itself. `ChartNavigationController.LayerForWidth` derives its ceilings from that spacing and the
  canvas column count rather than from fixed window widths, keeping the existing hysteresis
  behaviour. The stub provider's synthesis follows the same spacing so its output stays plausible.
  Update the tests that assert the old numbers; their new values are the specification. No database
  and no new project.
- **Issue:** none
- **Blast radius:** mechanism — the value returned by one extension method and the ceiling
  computation in one controller. Surface — the enum, its ordering and its use as a request field are
  untouched, so consumers that only carry a layer value are unaffected.
- **Risk:** low, concentrated in whether the canvas column count is available where the ceilings are
  computed; if it is not, the slice must thread it through or the thresholds stay parameterised by a
  documented default.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no database, no changes to the layer enum's members or to `IDataProvider`, and no
  work on the PostgreSQL provider. The stub provider's synthesis step is in scope, because it is a
  call site of the method whose meaning changes.
- **Plan:** docs/plans/completed/20260810-layer-ladder-spacing.md
- **PR:** #5 (merged)
- **Branch:** layer-ladder-spacing

### Slice archive-populator — Status: DONE
- **Scope:** Build the local test bench. Extract the verified archive DDL from the customer's dump
  into a schema script held in the repository — daily range partitions, the default partition, the
  `(id, l, t)` primary key, `timestamp(3) without time zone` — so the bench reproduces the structure
  a real SCADA creates rather than a hand-written approximation. Two solution projects: a
  deterministic seeder console (`SemiPlot.Tools.ArchiveSeeder`, event-driven segments, anchor pairs
  on change, long steady stretches with no rows, breaks marked `q = 32` then `q = 16`, coarse layers
  filled by the vendor's rule in testable C#) and a `net10.0` test project
  (`SemiPlot.Tests.Data`, xunit v3) owning the gated harness the later slices reuse: one
  Testcontainers PostgreSQL per run (`postgres:17-alpine`, overridable through `SEMIPLOT_PG_IMAGE`,
  with `SEMIPLOT_TEST_PG` as the escape hatch to an existing provisioned server), provisioned by
  `semibase create` pinned at `v0.1.0` — the same
  command that provisions a site — a seeded template database cloned per test class, skip-with-reason
  by default and failure under `SEMIPLOT_REQUIRE_DB=1`. The seeder writes as `scada_writer`, gated
  reads run as `semiplot_reader`, and `--admin-connection` fills `semiplot_tags` from the seeder's
  pen catalogue. The synthetic value walk and pen catalogue are copied from the stub, not
  referenced — the seeder owns its copies. A `data-tests` CI job on `ubuntu-latest` enforces the
  availability policy. Also extract a small anonymised fixture of real rows from the dump for the
  database-free tests that later slices need.
- **Issue:** none
- **Blast radius:** additive — two solution projects, `sql/`, the CI workflow,
  `SemiPlot/Directory.Packages.props`, `SemiPlot.slnx`. No application code, and
  `SemiPlot/Directory.Build.props` stays untouched: the new projects inherit its `TargetFramework`
  and `IsPackable` instead of redeclaring them.
- **Risk:** medium, concentrated in fidelity: data that does not reproduce the archive's shape would
  make every later slice's tests pass against conditions that never occur.
- **Depends on:** independent (external: SemiBase `v0.1.0`)
- **Stacking base:** master
- **Scope guard:** no provider code; no alternative layer-selection rule; nothing that writes to a
  production archive; no live/follow mode — that is the final slice's.
- **Plan:** docs/plans/completed/20260810-archive-populator.md
- **PR:** #1 (merged)
- **Branch:** archive-populator (deleted after merge)

### Slice provider-pen-query-seam — Status: DONE
- **Scope:** Give the pen catalogue a failure channel before anything tries to load it from a
  database. `IDataProvider` exposes the catalogue as a plain `IReadOnlyList<Pen>` property, which
  cannot report a failed read; the next-but-one slice reads it from `semiplot_tags`, where
  unreachable, not-initialised and timed-out are all reachable. The property becomes
  `Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()`, matching the two query methods already on the
  interface, and every implementer and consumer follows: the stub, `FakeDataProvider`,
  `TrendCoordinator` and its DI factory, the composition root, and `MainWindowViewModel`, whose
  `PenCount` is bound in XAML and cannot be computed from an injected provider once the read is
  awaitable. Six test files construct `TrendCoordinator` directly and follow the constructor change.
  Behaviour is unchanged throughout — the stub cannot fail, so no failure path is exercised yet.
- **Issue:** none
- **Blast radius:** mechanism — one Core interface member and the shape of one view model's
  dependency. Surface — no user-visible behaviour changes; the application still starts on the stub
  and draws the same chart.
- **Risk:** medium, concentrated in `MainWindowViewModel`: it is registered by plain constructor
  injection and resolved twice from the container, so making its `PenCount` depend on an awaited read
  is a DI-shape question rather than a call-site edit.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no new project, no Npgsql, no error types, no configuration. This slice changes a
  seam and nothing else — the provider scaffold that uses the new shape is the slice after it.
- **Plan:** docs/plans/completed/20260817-provider-pen-query-seam.md
- **PR:** #2 (merged)
- **Branch:** provider-pen-query-seam

### Slice postgres-provider-scaffold — Status: DONE
- **Scope:** Stand up the provider project with everything that needs no query. A
  `SemiPlot.DataSource.Postgres` project referencing Core only, Npgsql added through central package
  management, a DI extension registering the provider, and the provider itself implementing
  `IDataProvider` with unimplemented bodies. The connection settings loader: a YAML file in a
  configuration directory, a version field checked on load, a settings record, and a connection
  string built through the Npgsql builder rather than by concatenation. The time boundary
  converter: naive local to UTC on the way out, UTC to naive local for query bounds, with the zone
  resolved once from configuration. All of it is pure logic and testable without a database.
  This slice also establishes the error discipline the rest of the roadmap follows — the SemiStep
  pattern (`SemiStep/Docs/architecture/error-reporting.md`), two decoupled planes:
  - **Public plane** — a finite set of sealed FluentResults error types in Core beside
    `IDataProvider`, one per operator-visible **failure** (malformed connection file, version
    mismatch, unreachable database, uninitialised archive, query timeout, ...). An operator-visible state
    that is not a failure — an empty catalogue among them — travels in the success channel and gets
    no error type; postgres-catalog-and-extent settles that split. Each carries
    structured fields via a primary constructor and builds its message in the base constructor.
    This surface is the stable contract: the UI maps it to states, tests assert on it, and it grows
    only when a new operator-visible state exists — SemiStep's published rule, "a public error type
    exists iff a distinct operator sentence exists", enforced there by a build-time reflection
    coverage test; the composition slice enforces the same rule here through an exhaustive `switch`
    the compiler checks.
  - **Internal plane** — provider-internal failures (Npgsql exceptions, SQLSTATE codes, parse
    details) are free to change and never leak raw across the boundary: they cross only mapped
    into a public type, with the raw detail riding `.CausedBy(...)` into the log — SemiStep's
    envelope shape (`RecipeLoadFailedError`, `PlcCommandFailedError`).

  Tests assert by public error type and fields, never by message text.
- **Issue:** none
- **Blast radius:** additive — one new project and its registration. The composition root is not
  switched over in this slice.
- **Risk:** low
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no SQL, no queries, no change to which provider the application uses.
- **Plan:** docs/plans/completed/20260817-postgres-provider-scaffold.md
- **PR:** #3 (merged)
- **Branch:** postgres-provider-scaffold

### Slice postgres-catalog-and-extent — Status: DONE
- **Scope:** The first two operations that touch the database. Load the pen catalogue from
  `semiplot_tags` — the table itself is created by `semibase create` and populated by the bench
  seeder — mapping the stored line style onto the domain enum. The empty-versus-missing question is
  settled here and the answer splits it: an empty table is a successful read of zero rows, an absent
  one is a failed `Result` carrying `ArchiveNotInitialisedError` with `Table` naming `semiplot_tags`.
  No `EmptyTagCatalogError` is added — an empty catalogue is an operator-visible state and not a
  failure sentence — and `docs/architecture/data-integration.md` carries the settled split.
  Implement the archive extent using per-variable bounded subqueries, because an unbounded minimum
  over the whole table cannot use the primary key and scans the entire archive; `ArchiveExtent` gains
  an explicit empty form, because a fresh archive returns nulls and mapping them onto
  `default(DateTime)` would hand the minimap an extent beginning in year 0001. A SQLSTATE the mapper
  does not recognise gets a public type of its own, `ArchiveReadFailedError`, carrying the code, so
  no internal exception reaches the operator raw. This slice also
  introduces the single class that owns every SQL statement on the application and provider path,
  and the discipline that no SQL exists anywhere else on that path. The gated harness — container,
  provisioning, template cloning, skip policy, traits — is owned by archive-populator and reused here
  unchanged.
- **Issue:** none
- **Blast radius:** the provider, plus four files outside it — `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs`,
  the added `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveReadFailedError.cs` and
  `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs`
  in Core, and `SemiPlot/SemiPlot.UI/Minimap/MinimapViewModel.cs`, which follows the extent's new
  empty form. The application still runs on the stub.
- **Risk:** low-medium — the harness risk moved to archive-populator; what remains is the extent
  query shape.
- **Depends on:** archive-populator, postgres-provider-scaffold
- **Stacking base:** master
- **Scope guard:** no history queries, no realtime, and no change to the application's composition
  root, which stays untouched and still selects the stub. `AddPostgresData` itself does change: it
  takes `PostgresConnectionSettings` and gains registrations for the data source, the time converter,
  the exception mapper and the missing-relation probe.
- **Plan:** docs/plans/completed/20260818-postgres-catalog-and-extent.md
- **PR:** #4 (merged)
- **Branch:** postgres-catalog-and-extent

### Slice postgres-history-read — Status: DONE
- **Scope:** History from a chosen layer by direct read. Inherit the single statement class from
  postgres-catalog-and-extent and add the windowed statement to it. Implement the windowed read
  constrained on the variable list, the layer and the time bounds, ordered for per-pen assembly,
  with timestamps converted at the boundary. Move `MinMaxDecimator` verbatim from
  `SemiPlot.DataSource.Stub` to `SemiPlot.Core/Trends` — both providers fold through it and neither
  data-source project may reference the other — then fold the returned rows into one envelope per
  pen through it, dropping any row whose converted timestamp does not strictly ascend, which is the
  daylight-saving artefact `data-integration.md` assigns to the envelope assembler. Pin the
  statement text character for character against the fenced block
  `docs/architecture/data-integration.md` carries, the way the two shipped statements are pinned,
  and pin the binder against that statement's own parameter names in a unit test of its own. Assert
  through `EXPLAIN` that the query reaches its rows through an index, or a bitmap driven by one, and
  scans no row-holding `trends` partition sequentially — the plan cannot name `tpk`, for the reason
  in Guard strategy. The typed timeout path is inherited, not built: `ArchiveExceptionMapper`
  already maps SQLSTATE `57014` onto `ArchiveQueryTimedOutError`, and the windowed read travels it
  unchanged; which bound that error reports is provider-simplification's to settle.
  `QueryHistoryAsync` is the last body returning the temporary not-implemented error the scaffold
  gave its unimplemented members, so this slice also **owns the deletion of that type** together
  with the tests that assert on it. A pen with no rows in the window gets no envelope, an interim
  rule postgres-gap-reconstruction revises.
- **Issue:** none
- **Blast radius:** the provider only.
- **Risk:** medium, concentrated in envelope assembly against archive-shaped input — anchor pairs and
  steady stretches behave differently from the evenly spaced synthetic data the decimator has seen so
  far.
- **Depends on:** postgres-catalog-and-extent
- **Stacking base:** master
- **Scope guard:** no server-side bucketing, no gap reconstruction beyond what the decimator already
  does, no realtime.
- **Plan:** docs/plans/completed/20260819-postgres-history-read.md
- **PR:** #6 (merged)
- **Branch:** postgres-history-read

### Slice provider-simplification — Status: PENDING
- **Scope:** Take back the one thing the shipped provider carries for no reader, and correct two
  comments that misdescribe what is kept. `ArchiveDataSource` carries the most intricate code in the
  provider — a dual physical-connection initializer, an interlocked tick cache, a `pg_settings`
  parse and a warn-on-change arm — and it serves two purposes that the slice plan must weigh
  separately, because they are coupled through one field and a simplification can keep only one
  cleanly. The first is truthful reporting: `ArchiveQueryTimedOutError` carries the bound the server
  actually applied, which matters because `statement_timeout` is `USERSET`, so the effective value
  genuinely varies per site and is knowable no other way. The second is an unambiguous client
  timeout: the command bound is set one margin above the server's, so a `TimeoutException` can only
  mean the server stopped answering, which is why it maps to `ArchiveUnreachableError` and never to
  `ArchiveQueryTimedOutError`. A fixed generous bound drops both — the server's own default still
  fires `57014` and the error then reports a number the server never applied, which is worse than
  none. Reading `pg_settings` lazily on the `57014` path instead keeps the number true and deletes
  the whole initializer apparatus, at the cost of choosing the client bound without knowing the
  server's. The plan settles which purpose survives and states the cost; `ArchiveQueryTimedOutError`
  keeps its public shape and fields either way, and if the number cannot be kept true it reports
  unknown rather than a wrong one. The two comment corrections are
  `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`, whose header claims the code
  half needs no pinning when the code half is the production half, and
  `SemiPlot/SemiPlot.DataSource.Postgres/MissingRelationProbe.cs`, whose class doc says a non-English
  `lc_messages` would change the table name in the message text — localisation changes the wording
  and the quote glyphs, not the interpolated identifier, and the real reason not to read it is that
  the project routes on structured fields rather than on message text.
- **Issue:** none
- **Blast radius:** `ArchiveDataSource`, the exception mapper's timeout source, the DI extension and
  the tests covering the three, plus two comment-only edits named in the scope. Whatever the plan
  settles about the reported number also lands in the error-semantics section of
  `docs/architecture/data-integration.md`, in `postgres-instance.md`'s read-back rationale and in
  `ArchiveQueryTimedOutError`'s own contract — those three describe the current behaviour and must
  not be left describing it once it changes. No other project and no application code.
- **Risk:** medium, concentrated in the client-bound choice rather than in the deletion: the bound
  decides whether a slow-but-alive server reads as unreachable, and the dead-server detection time
  moves with it.
- **Depends on:** postgres-history-read
- **Stacking base:** master
- **Scope guard:** `MissingRelationProbe` stays — its deletion was scoped from a premise the code
  disproves, and postgres-startup-and-composition deepens the reliance on the distinction it keeps.
  Statement pinning stays as shipped. No new queries and no edit to the text of an existing one; no
  error-type merges — those belong to postgres-startup-and-composition.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-gap-reconstruction — Status: PENDING
- **Scope:** Make breaks render correctly on the direct read path. A sample marked as the last before
  a break is followed by a gap anchor; the first sample after a break resumes the line; and a long
  run with no rows that is not preceded by a break marker renders as a horizontal continuation rather
  than as a break. The windowed statement already selects `q`, so the statement does not change; the
  read does. `ReadHistoryRow` projects the first three columns and `HistoryRowFold.Row` carries no
  `q` member, so this slice extends the row struct and the reader as well as the fold. The left edge
  needs one addition: a pre-window seed lookup for the pen's last row at or before the window start,
  without which a pen last written before the window opens returns no rows and gets no envelope at
  all. Tests run against the fixture rows extracted from a real archive as well as against
  the populated database. This precedes bucketing because a misdrawn break is operator-visible
  incorrectness while bucketing is a transfer optimisation, and correctness does not wait on an
  optimisation: reconstruction on the direct read path needs nothing that bucketing provides.
- **Issue:** none
- **Blast radius:** the provider's envelope assembly; the rendering path above it already understands
  gap anchors.
- **Risk:** high relative to the rest — this is the behaviour most likely to be subtly wrong, and
  wrong in a way that looks plausible on screen. A break drawn as a straight line across hours is
  the failure mode that misleads an operator.
- **Depends on:** postgres-history-read
- **Stacking base:** master
- **Scope guard:** no changes to the statements themselves beyond what gap data requires; no
  server-side bucketing; no realtime.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-bucketed-read — Status: PENDING
- **Scope:** Server-side reduction to pixel columns for windows where the chosen layer is still
  denser than the canvas. A bucketing statement returning at most one row per column per pen with
  the minimum, maximum, first and last values, the edge timestamps, the edge quality codes and a
  break count, with buckets aligned to the window start so the leftmost column is not clipped. The
  provider chooses between this path and the direct read by the expected row count. The gap
  reconstruction already shipped on the direct path is fed from the bucketed path's edge quality
  codes and break count, covering a break that spans several buckets. Statement text and parameter
  names pinned character for character against a literal in the test; an integration test compares
  bucketed output against the same window read directly.
- **Issue:** none
- **Blast radius:** the provider only; adds a second read path alongside the first.
- **Risk:** medium, concentrated in bucket alignment and in the choice threshold between the two
  paths.
- **Depends on:** postgres-gap-reconstruction
- **Stacking base:** master
- **Scope guard:** no gap semantics beyond feeding the shipped reconstruction from bucket edges, no
  realtime, no layer-selection changes.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-realtime-poll — Status: PENDING
- **Scope:** The live edge. A cold observable that polls the raw layer for samples newer than the
  last one seen, on the injected data scheduler, carrying the variable list in every query because a
  time-only predicate cannot use the primary key and would scan the current day's partition on every
  tick. Disposal stops the poll; a query error drops that tick without throwing on the UI thread and
  without terminating the observable, and repeated consecutive failures surface as a typed
  connection-state change the UI can show, not only as log lines; the provider never emits a
  timestamp at or before the last one already delivered, which is what keeps the
  history-to-realtime seam monotonic.
  An integration test appends rows and asserts they arrive once, in order, without duplicates, and an
  `EXPLAIN` assertion pins the index usage.
  `Subscribe` returns an empty observable today, which is a silent live edge rather than a failure;
  this slice replaces that body with the poll.
- **Issue:** none
- **Blast radius:** the provider only; the batching and scheduler hand-off above it are unchanged.
- **Risk:** medium, concentrated in the seam invariant and in poll error handling.
- **Depends on:** postgres-catalog-and-extent
- **Stacking base:** master
- **Scope guard:** no changes to coordinator batching; no composition changes.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-startup-and-composition — Status: PENDING
- **Scope:** Make the application actually use the provider. A startup probe returns a `Result`
  whose **public-plane** typed errors distinguish the states the operator must be able to tell
  apart. Most of that vocabulary is already shipped: the sealed types in
  `SemiPlot/SemiPlot.Core/Data/Errors` cover the connection file — absent, malformed, wrong
  version — and the database — unreachable, missing, access denied, not initialised, query timed
  out, read failed on an unmapped SQLSTATE — and each of them, after the two merges below, needs its
  own visible state here. Two operator states have no type yet and are genuinely new work here: an
  unexpected table shape, and a non-empty default partition. The two merges narrow the vocabulary
  while it is being mapped, each pair costing a state without buying the operator a distinction:
  `ConnectionFileVersionMismatchError` folds into `ConnectionFileInvalidError` as a
  `ConnectionFileProblem` value, the file format having had one version ever; and
  `ArchiveDatabaseMissingError` folds into `ArchiveNotInitialisedError`, both being "this server
  carries no archive to read yet", discriminated by which object is absent — the database, the
  SCADA's `trends`, or SemiBase's `semiplot_tags` — which is already how the surviving type routes
  its remedy. The mapping is written once and against a settled vocabulary, which is why
  provider-simplification lands before this slice rather than after it: it changes what
  `ArchiveQueryTimedOutError` carries, and mapping a vocabulary that then moves underneath means
  mapping it twice. An empty pen catalogue is not among the
  new types: postgres-catalog-and-extent settles it as a success-channel state, so this slice
  surfaces it as a UI state reached through a successful `Result` and pins it with a named test of
  its own, separate from the mapping guard, which covers error types only. That named test is what
  keeps `postgres-instance.md`'s "normal states with their own message" a state something can force
  and something can see. The UI maps each public error type onto a distinct visible state — the
  application stays alive, draws nothing, and says why —
  in one exhaustive `switch` the compiler checks (see Guard strategy), so the mapping is total: every
  public error type in Core has a UI state, and internal errors reach the UI only wrapped in a
  public envelope. Whether an unhandled type fails the build or only warns turns on
  `TreatWarningsAsErrors`, which `SemiPlot/Directory.Build.props` does not set; making that gate hard
  is this slice's plan to settle. **There is no stub fallback**:
  the database is part of the service, and an unreachable database is an error, never silently
  substituted synthetic data. The stub remains selectable only by an explicit development flag until
  the final slice deletes it. DI tests cover the selection; a thin end-to-end suite (5–7 journeys on
  `Avalonia.Headless` against a bench-seeded database, gated like the data tests) proves the
  composed application: pens listed from `semiplot_tags`, history drawn with counts consistent with
  the seed, a break rendered as a broken line, layer switch on zoom, a live insert arriving once,
  and one test per startup error state asserting the UI state — never log text. On Windows CI the
  suite reaches PostgreSQL through `SEMIPLOT_TEST_PG` (the runner image ships a stopped PostgreSQL
  service — verify the image at slice time). This automates the roadmap's close condition.
  **The consumer side of the no-envelope rule is inherited here.** postgres-history-read ships an
  interim rule where a pen with no rows in the window gets no envelope, and
  `TrendChartViewModel.ApplyHistory` writes the pens a result carries while removing none — so a
  pen the provider omits keeps the previous window's envelope on screen. Wiring the provider to
  the chart is what makes that reachable at all, so this slice either drops the entry for a
  requested pen the result omits, or takes the seeded envelope postgres-gap-reconstruction starts
  sending. `docs/architecture/data-integration.md` carries the rule and its revision.
- **Issue:** none
- **Blast radius:** the composition root and startup path — the first slice that changes what the
  running application does by default. It also touches the chart view model, for the inherited
  no-envelope rule above.
- **Risk:** medium, concentrated in the error-state mapping and in keeping the E2E suite thin: the
  layer boundaries are already covered by contract, so E2E asserts composition, not behaviour
  matrices.
- **Depends on:** postgres-bucketed-read, postgres-realtime-poll, provider-simplification
- **Stacking base:** master
- **Scope guard:** no new queries; no UI redesign of the error states beyond surfacing them; no
  stub deletion — that is the final slice's. The two error-type merges are in scope only as far as
  the types themselves and their call sites; no other error type is reshaped.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice live-demo-and-stub-retirement — Status: PENDING
- **Scope:** Replace the synthetic stub with a live demo bench and delete it. The seeder gains a
  `--follow` mode: after seeding history up to "now" it keeps walking the same segment sequence in
  real time as `scada_writer` — raw rows continuously, coarse layers flushed when their period
  closes (which makes the freshness lag of `l=1/2/3` visible in the running application), the next
  day's partition created ahead of midnight, and `q = 32`/`q = 16` markers across a graceful stop
  and restart, so stopping and restarting the demo writer produces real breaks. `--follow` only
  appends; the only destruction lives in `scripts/seed-demo.ps1`, which wraps drop-and-recreate of
  the demo database (`semibase create` → seeder with a current-time `--end` → printed connection
  file). The demo writer plays the role of the SCADA, not of SemiPlot — the application remains a
  strict read-only consumer. Then delete `SemiPlot.DataSource.Stub`: the project, its solution
  entry, its DI registration and the development flag; check nothing else references its classes
  (`MinMaxDecimator` in particular) and relocate anything that is still needed. `FakeDataProvider`
  in the test project stays — it is a test double, not the stub.
  This slice also owns the eyes-on confirmation of the layer ladder, which layer-ladder-spacing
  deliberately deferred: against `RandomStubDataProvider` a wrong layer has no observable
  consequence, because the stub synthesises points at whatever spacing `ToPointSpacing` hands it and
  then decimates to the canvas column count, so the drawn curve is identical whether the ladder is
  right or wrong. On a real archive the difference is large — too fine a layer reads an order of
  magnitude more rows, too coarse loses detail. With the application drawing real data, maximise and
  restore the window at a fixed time span and confirm the toolbar's layer readout follows the canvas
  width and does not oscillate at a rung boundary. This is the first point at which the check can
  tell a correct ladder from a broken one.
- **Issue:** none
- **Blast radius:** the seeder tool, `scripts/`, the composition root, and one deleted project.
- **Risk:** low — the demo path exercises code every earlier slice already tests; the deletion is
  mechanical.
- **Depends on:** postgres-startup-and-composition
- **Stacking base:** master
- **Scope guard:** no in-database procedures or scheduled jobs for the demo writer — rejected
  below; no changes to provider queries.
- **Plan:** —
- **PR:** —
- **Branch:** —

## Close condition

Every one of the twelve slices not marked DROPPED has a MERGED PR. No slice owns an issue, so no
issue closes automatically; there is no tracking issue to close by hand. The functional close
condition is that the application, pointed at a database seeded by the bench, draws real history,
follows a live edge moved by the `--follow` demo writer, selects layers by window width, breaks the
line only where the archive says a break occurred, and `SemiPlot.DataSource.Stub` no longer exists. The
end-to-end suite of the composition slice asserts this automatically; the demo stand confirms it
by eye.

## Rejected alternatives

Settled during design — do not relitigate without new facts. The full reasoning is in
`docs/architecture/history-read-path-evaluation.md`.

- Summary tables of our own maintained by a background service — the vendor already writes up to
  four points per period selected by magnitude, and a common retention depth removes the only reason
  to own a second copy.
- Lazy on-demand materialisation of summaries — the strongest alternative, and recorded as the
  fallback if the vendor's selection rule is ever refuted, but redundant while the layers hold.
- TimescaleDB — continuous aggregates read from hypertables, and the archive table is created by the
  SCADA as a declaratively partitioned table that cannot be converted.
- A scheduler inside the database — no released version of the usual extension supports the
  platform, and every alternative scheduler is one more process to keep alive for no benefit.
- Reading the vendor's binaries to settle the thinning rule — the server image is protected and
  carries no readable strings, and defeating that protection is out of scope.
- A second implementation of the layer-selection rule in the populator, to see how badly the picture
  degrades if the vendor's rule differs — dropped as scope; the risk stays recorded as unverified in
  the architecture docs.
- The demo writer as an in-database procedure with a scheduler — pg_cron has no Windows release and
  the stand's PostgreSQL runs on Windows, so an external caller is needed anyway; it would be a
  second implementation of the generation rule, in SQL, untestable without a database; and
  `postgres-instance.md` deliberately keeps the instance free of our functions, triggers and jobs.
- A stub fallback in the composition root — removed 2026-08-14: synthetic data silently standing in
  for process data is the worst failure mode for an operator tool. An unreachable database is a
  visible error state.

## Open forks for the operator

**The vendor's layer selection rule is documented but not measured by us.** The manual and two
vendor forum answers state that a coarse layer holds up to four points per period chosen by
magnitude, and the measured dump is consistent with it, but we have never watched the SCADA thin a
period with our own instrument. Confirming it needs a running installation, which does not exist
yet. Partly narrowed in slice archive-populator: the bench's thinner was confronted with the dump's
real `l = 1` rows and every period's extreme *values* agreed, so envelopes read from a coarse layer
are safe either way. Two details went into `docs/architecture/scada-archive.md` with it — when an
extreme value repeats the vendor keeps the later row while the bench keeps the earlier one, which
moves the abscissa and not the envelope, and one selected point is confirmed to be the last row of
its period. The dump still spans two hours with twelve restarts, so the hour and day layers remain
untested across their own periods. The design stands regardless: if the rule turns out to differ, the read path stops trusting
coarse layers for envelopes and the lazy-materialisation alternative above becomes the answer, which
changes the provider's layer strategy and nothing else. The experiment and its query are recorded at
the end of `docs/architecture/scada-archive.md` and run when a stand becomes available.

**Retention depth and disk size are unset.** Both need a measured write rate from a working
installation, and both are recorded as undecided in `docs/architecture/postgres-instance.md`. The
design stands regardless — no slice depends on the number.

**Backup method for the supplied instance is unset.** Recorded as undecided in the same document.
It is an operations decision, not a code decision, and no slice depends on it.
