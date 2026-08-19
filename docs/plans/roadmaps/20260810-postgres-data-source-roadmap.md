# PostgreSQL data source roadmap

**Issues:** none declared — the repository has no issue tracker in use. This roadmap covers the
whole span from "the viewer runs on synthetic data" to "the viewer reads a real Simple-Scada
archive", sliced into independently shippable pull requests.

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

**Corrected 2026-08-19** on a compiler fact: the exhaustive-`switch` guard this document
prescribed cannot exist for an open hierarchy, so the mapping is guarded by a reflection coverage
test — the form this document had rejected, rejected on reasoning that assumed the compiler form was
available.

**Re-sliced 2026-08-19** after provider-simplification shipped. The remaining work is four slices
rather than six: `postgres-startup-and-composition` is split so the application reads the real
archive one slice from here instead of four, because everything downstream has only ever been
validated against synthetic data whose shape does not match the archive, and the slice most likely
to be subtly wrong — gap reconstruction — was being built with nothing on screen to check it
against. Realtime, the demo bench and the stub's retirement merge into one slice, which is what they
always were. `postgres-bucketed-read` is dropped pending a measurement.

**The planning apparatus scales to the risk the slice declares.** A low-risk slice takes a
one-page plan and one review round; the full apparatus — a long plan, several review rounds — is for
slices rated medium or higher. Five review rounds over a slice that corrects two comments is a cost
with no defect behind it, and the test suite, not the rounds, is what catches behaviour.

## Summary

SemiPlot renders trends on synthetic data: the running application resolves `IDataProvider` to the
stub, which emits random walks. `PostgresDataProvider` stands beside it and reads the pen catalogue
and the archive extent from a real database, but its history and realtime members are still
unimplemented and no composition selects it. The architecture for reading the Simple-Scada 2 PostgreSQL
archive is settled and documented, and the aggregation-layer ladder already picks a resolution by
the rule that architecture states. The slices below deliver a production provider, the local test bench
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
| Failure reporting | two decoupled error planes (SemiStep pattern): nine sealed public types with structured fields in `SemiPlot/SemiPlot.Core/Data/Errors`, internal detail riding `CausedBy` into the log | the same two planes, over a narrower vocabulary, with every public type mapped to a UI state, and a coverage test that fails when one is not |
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
the stub across the merged slices, and the composition root resolves the archive provider — the startup
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
  test stays green.

  That is the mechanism as shipped, and it stays. It does not grow: two operational statements
  (`RelationProbe`, `EffectiveStatementTimeout`) are already unpinned, both are one line used from
  one call site, and both are covered behaviourally by gated tests that fail if the statement stops
  working. A statement a future slice adds is pinned with a plain literal in its test unless the
  document quotes it for a reader's sake anyway. Reading markdown at run time earns its place for
  the three statements that carry the read path and buys progressively less for each one after.
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
- **Total error-to-state mapping.** The UI maps every public error type to a state, and a coverage
  test enumerates the public types in `SemiPlot.Core/Data/Errors` and in `SemiPlot.UI.Startup` by
  reflection and fails when one has no mapping, with a second test pinning the count so it cannot pass
  over an empty set (added in postgres-wire-up). A new public error type cannot silently leak past the
  operator, and an internal error cannot silently become public.

  A compiler-checked `switch` was the intended form and is not available. `CS8509` fires on any
  switch expression the compiler cannot prove exhaustive, and over an open hierarchy — `IError` is
  an interface, and C# has no closed hierarchies — it can never prove it, so a switch handling every
  type without a catch-all warns anyway and `WarningsAsErrors` would stop the build outright. The
  contrast is exact over an enum, where a missing member gives `CS8509` and a handled set without a
  `_` arm gives `CS8524`; over a type hierarchy there is no such pair. SemiStep's
  `CoreErrorLocalizationCoverageTests` is the shape that works, and this repository adopts it.

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
    exists iff a distinct operator sentence exists", enforced there by a reflection coverage test;
    postgres-wire-up enforces the same rule here the same way.
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

### Slice provider-simplification — Status: DONE
- **Scope:** Take back the one thing the shipped provider carries for no reader, and correct two
  comments that misdescribe what is kept. `ArchiveDataSource` carried the most intricate code in the
  provider — a dual physical-connection initializer, an interlocked tick cache, a `pg_settings` parse
  and a warn-on-change arm — serving two purposes coupled through one field: reporting the bound the
  server actually applied, and setting each command's bound one margin above it so a client timeout
  could only mean the server had stopped answering. The first survives, read lazily on the `57014`
  path; the second is replaced by a fixed backstop, and the consequences are documented rather than
  buried.
- **Issue:** none
- **Blast radius:** `ArchiveDataSource`, the exception mapper's timeout source, the DI extension and
  their tests, plus the error-semantics regions of `data-integration.md` and `postgres-instance.md`.
- **Risk:** medium, concentrated in the client-bound choice rather than in the deletion.
- **Depends on:** postgres-history-read
- **Stacking base:** master
- **Scope guard:** `MissingRelationProbe` stays; statement pinning stays as shipped; no new queries,
  no error-type merges.
- **Plan:** docs/plans/completed/20260819-provider-simplification.md
- **PR:** #10 (merged)
- **Branch:** provider-simplification

### Slice postgres-wire-up — Status: DONE
- **Scope:** Make the application read the real archive, with every failure visible. `Program` and
  `App` gain configuration-directory handling and load `PostgresConnectionSettings` at startup; the
  composition root registers `AddPostgresData` by default, with the stub selectable only by an
  explicit development flag. **There is no stub fallback** — an unreachable database is an error
  state, never silently substituted synthetic data. A startup probe returns a `Result`, and the two
  error-type merges land here: `ConnectionFileVersionMismatchError` folds into
  `ConnectionFileInvalidError` as a `ConnectionFileProblem` value, the file format having had one
  version ever; `ArchiveDatabaseMissingError` folds into `ArchiveNotInitialisedError`, both being
  "this server carries no archive to read yet", discriminated by which object is absent. The UI maps
  every remaining public error type onto a distinct visible state, and a reflection coverage test
  over `SemiPlot.Core/Data/Errors` fails when a public type has no mapping. The compiler cannot do
  this job over an open hierarchy — see Guard strategy — so the test is the gate rather than a
  fallback for one. An empty pen catalogue is not an error:
  postgres-catalog-and-extent settles it as a success-channel state, so it surfaces as a UI state
  reached through a successful `Result`, pinned by a named test of its own.

  Two pieces of work no slice previously owned land here because this slice is what makes them
  reachable. **Startup seeds the window from the archive extent.** `ChartNavigationController` opens
  on `now - 1h .. now` and `TrackDataExtents` moves `FirstSample` only from an envelope that has
  rows, so an archive whose last sample is older than the opening window returns nothing, the window
  never snaps onto the data, and panning clamps to a point after it — the minimap shows the extent
  the chart cannot reach. **And `TrendChartViewModel.ApplyHistory` drops the entry for a requested
  pen a result omits**, closing the consumer half of the interim no-envelope rule
  postgres-history-read shipped; that path stays correct after gap reconstruction adds its seed
  lookup, because a pen with no data at all still gets no envelope.

  The startup probe needs its own bound or cancellation token: provider-simplification replaced the
  derived command timeout with a fixed backstop, so a hung-but-accepting server otherwise leaves
  startup waiting the full backstop before `ArchiveUnreachableError` exists to map to a state.

  The composition is proved without a database. `App`'s private startup wiring is extracted into a
  testable orchestrator returning a `Result` — today `LoadPens` reads `.GetAwaiter().GetResult().Value`
  with no failure check inside a synchronous `AfterSetup`, which against a real server is an
  unhandled crash on the first unreachable one. Composition tests resolve the whole graph over
  `AddPostgresData` plus `AddUi`; the error-state mapping gets a test per public type driven by
  `FakeDataProvider`; window seeding and pen dropping are view-model logic tested the same way. The
  plan carries a manual protocol against the seeded bench for what those cannot reach: pens from
  `semiplot_tags`, history counts, layer switch on zoom, and each startup error state forced by hand.

  A headless end-to-end suite is deliberately **not** here. It needs Avalonia and a container at
  once, and no CI runner provides both: `build-and-test` runs on `windows-latest`, which cannot run
  Linux containers, and `data-tests` runs on `ubuntu-latest`, which cannot build a project
  referencing `SemiPlot.UI`. Such a suite would be a developer-machine check that skips in CI
  forever, so it waits for `avalonia-12-bump` to make it cheap and lands with the journeys in
  `postgres-live-edge-and-demo`.
- **Issue:** none
- **Blast radius:** the composition root, the startup path, the chart view model and the navigation
  controller — the first slice that changes what the running application does by default.
- **Risk:** medium, concentrated in the error-state mapping and in the extent seeding, which has no
  synthetic equivalent to have been exercised against.
- **Depends on:** provider-simplification
- **Stacking base:** master
- **Scope guard:** no new SQL, no statement changes, and no provider behaviour change beyond the two
  error-type merges, which necessarily edit `ArchiveExceptionMapper` and `PostgresConnectionLoader`
  where those two types are constructed. The guard's "no new error types" clause fences the provider's
  Core SQLSTATE vocabulary; `StartupReadTimedOutError` sits in `SemiPlot.UI.Startup`, is raised by no
  provider, and answers the bound this slice's own scope requires. No gap semantics — a `q = 32` break still draws as a step
  until postgres-gap-reconstruction. No realtime; `Subscribe` stays empty and the live edge is
  static. No new error types beyond the two merges — the unexpected table shape and the non-empty
  default partition arrive with the closing slice, and the coverage test forces their mapping then. No stub deletion; the flag stays until the closing slice. No framework version changes;
  the Avalonia bump is its own slice.
- **Plan:** docs/plans/completed/20260819-postgres-wire-up.md
- **PR:** #14 (merged)
- **Branch:** postgres-wire-up

### Slice avalonia-12-bump — Status: PENDING
- **Scope:** Take the UI to Avalonia 12 and both test projects to xunit v3, which `CLAUDE.md`
  already names as the intended end state. Seven Avalonia packages move from 11.3.8 to 12.0.x,
  `ReactiveUI.Avalonia` follows, `ScottPlot.Avalonia` moves 5.1.57 to 5.1.59 — the release that
  depends on Avalonia 12 — and `SemiPlot.Tests` converts from xunit 2.9.3 to xunit v3, carrying its
  roughly ninety `[AvaloniaFact]` and `[AvaloniaTheory]` tests across. The stack is not speculative:
  a sibling repository of this operator's already ships `Avalonia.Headless.XUnit` 12.0.5 and
  `xunit.v3` 3.2.2 in one project against Avalonia 12.0.5.

  **The two test projects do not merge**, and `CLAUDE.md`'s exit path never said they would.
  `SemiPlot.Tests.Data` stays plain `net10.0` because the `data-tests` job runs on `ubuntu-latest`,
  the only runner that can start a container; a project referencing `SemiPlot.UI` cannot build
  there. What dissolves is the xunit-major mismatch, after which `SemiPlot.Tests` may take a project
  reference on `SemiPlot.Tests.Data` and consume the container harness directly — which is what
  makes the end-to-end journeys cheap in the slice that owns them.

  This slice lands after postgres-wire-up on purpose: by then the application draws real archive
  data, so the bump has something to be judged against. Against the stub a rendering regression
  draws the same curve as correct behaviour.
- **Issue:** none
- **Blast radius:** every Avalonia and ScottPlot reference, `SemiPlot.Tests`' framework and test
  attributes, and the view code that touches input routing.
- **Risk:** medium, concentrated in `ScottPlot.Avalonia` 5.1.59's `AvaPlot` on Avalonia 12 — the one
  piece the sibling repository does not de-risk, since it carries no ScottPlot — and in the
  pointer handling of `TrendChartView` and `MinimapView`.
- **Depends on:** postgres-wire-up
- **Stacking base:** master
- **Scope guard:** no behaviour changes and no new features; a test may change only where the
  framework forces it. No provider work.
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
  all. Tests run against the fixture rows extracted from a real archive as well as against the
  populated database.
- **Issue:** none
- **Blast radius:** the provider's envelope assembly; the rendering path above it already understands
  gap anchors.
- **Risk:** high relative to the rest — this is the behaviour most likely to be subtly wrong, and
  wrong in a way that looks plausible on screen. A break drawn as a straight line across hours is
  the failure mode that misleads an operator. This slice earns the full planning and review
  apparatus; most do not.
- **Depends on:** postgres-history-read
- **Stacking base:** master
- **Scope guard:** no changes to the statements themselves beyond what gap data requires; no
  server-side bucketing; no realtime; no composition changes.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-live-edge-and-demo — Status: PENDING
- **Scope:** The live edge, the demo bench that exercises it, and the stub's retirement — one piece
  of work rather than three, because the poll is verified by watching a live archive grow and the
  bench that grows it is what replaces the stub.

  A cold observable polls the raw layer for samples newer than the last one seen, on the injected
  data scheduler, carrying the variable list in every query because a time-only predicate cannot use
  the primary key and would scan the current day's partition on every tick. Disposal stops the poll;
  a query error drops that tick without throwing on the UI thread and without terminating the
  observable, and repeated consecutive failures surface as a typed connection-state change the UI can
  show, not only as log lines. The provider never emits a timestamp at or before the last one already
  delivered, which is what keeps the history-to-realtime seam monotonic.

  **The fresh tail lands here**, which no slice previously owned: `data-integration.md` records that
  a coarse-layer window ending at the live edge is missing up to one period at its right edge until
  the tail is filled from `l = 0` and concatenated. A live archive makes that visible; a static bench
  does not.

  The demo bench appends to a seeded database on a wall-clock cadence so the live edge has something
  to follow, the stub project is deleted, and the aggregation ladder gets its eyes-on confirmation
  against real data — the check the roadmap has deferred since the beginning. The thin end-to-end
  journeys that need a live archive land here too: a break rendered as a broken line, and a live
  insert arriving once.
- **Issue:** none
- **Blast radius:** the provider's realtime member, the composition root's provider selection, the
  deleted stub project and every reference to it, plus the new demo tool.
- **Risk:** medium, concentrated in the seam invariant and in poll error handling.
- **Depends on:** postgres-wire-up, postgres-gap-reconstruction, avalonia-12-bump
- **Stacking base:** master
- **Scope guard:** no coordinator batching changes; no bucketing; no new error types beyond the
  connection-state change the poll needs.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-bucketed-read — Status: DROPPED — no measurement justifies it yet
- **Scope:** Server-side reduction to pixel columns for windows where the chosen layer is still
  denser than the canvas.
- **Why dropped:** it is a transfer optimisation with no measured problem behind it. The worst case
  the ladder permits is a window at a rung boundary reading on the order of 60 000 rows per pen,
  which the client decimator already folds and which hysteresis makes transient. Building it would
  add a second read path, a threshold between the two, its own statement pin, its own `EXPLAIN`
  assertions and a direct-versus-bucketed comparison suite — all to speed up a case nobody has
  observed being slow.
- **Re-add condition:** a measurement from the demo bench or a site showing a wide-window read that
  is slow enough to notice. Re-add it then, with the number in hand; slugs survive reordering, so
  nothing else has to move.
- **Depends on:** postgres-gap-reconstruction
- **Plan:** —
- **PR:** —
- **Branch:** —

## Close condition

Every slice not marked DROPPED has a MERGED PR. No slice owns an issue, so no
issue closes automatically; there is no tracking issue to close by hand. The functional close
condition is that the application, pointed at a database seeded by the bench, draws real history,
follows a live edge moved by the `--follow` demo writer, selects layers by window width, breaks the
line only where the archive says a break occurred, and `SemiPlot.DataSource.Stub` no longer exists. The
end-to-end journeys in postgres-live-edge-and-demo assert this on a developer machine; the demo
stand confirms it by eye.

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
