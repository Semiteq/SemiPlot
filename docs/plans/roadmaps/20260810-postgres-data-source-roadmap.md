# PostgreSQL data source roadmap

**Issues:** none declared — the repository has no issue tracker in use. This roadmap covers the
whole span from "the viewer runs on synthetic data" to "the viewer reads a real Simple-Scada
archive", sliced into eleven independently shippable pull requests.

**Amended 2026-08-14** after the archive-populator sanity review: the bench is two solution
projects provisioned by SemiBase (verified: `v0.1.0` at `aa037a4`, all commands cross-platform,
its CI provisions a Linux container by running `all` twice, and the `v0.1.0` release ships
`linux_amd64`/`windows_amd64` binaries, so no consumer needs a Go toolchain); the seeder populates
`semiplot_tags`;
failure handling adopts the SemiStep typed-results discipline; the stub fallback is removed from
the composition slice; and a final slice replaces the stub with a live demo bench.

## Summary

SemiPlot renders trends correctly but has never read a real archive: the only implementation of
`IDataProvider` emits random walks. The architecture for reading the Simple-Scada 2 PostgreSQL
archive is settled and documented, and one piece of already-shipped code — the aggregation-layer
thresholds — is wrong by a factor of four against that architecture. Eleven slices deliver a
production provider, the local test bench it is developed against, and a live demo bench that
retires the synthetic stub. The roadmap closes when the application, pointed at a populated
database, draws real history, follows the live edge, selects archive layers by window width, and
the stub project is gone.

**Thesis:** every resolution the trend canvas needs already exists in the vendor's archive, so the
provider only has to choose a layer, reduce it to the canvas width, and reconstruct gaps — it never
has to maintain data of its own.

**Verified against code on 2026-08-10 (`bef4823`). Baseline at that ref: solution builds, 250 tests
pass, zero failures. Trust rule: prefer the shapes over the numbers if they have drifted.**

## Root cause

The provider seam was designed early and honoured — the UI depends only on `IDataProvider`, and the
stub is swappable. What never followed is a real implementation. Two consequences compound.

First, everything downstream of the seam has been validated only against synthetic data whose shape
does not match the archive: the stub emits evenly spaced samples, while the archive writes anchor
pairs on change, leaves long stretches with no rows at all when a value is steady, and marks breaks
in a quality column the stub does not model.

Second, the layer machinery was written against an assumption that has since been disproved.
`AggregationLayerExtensions.ToSampleInterval` returns the layer's period — one minute, one hour, one
day. The vendor writes up to four points per period, so the real point spacing is a quarter of that.
Every threshold in `ChartNavigationController.LayerForWidth` is therefore four times too
conservative, and the viewer would read raw data across windows a coarse layer serves comfortably.

| Area | Cost of the gap today |
| --- | --- |
| `IDataProvider` implementations | One synthetic implementation; nothing reads the archive |
| Layer selection | Thresholds four times too conservative; raw reads where a layer would do |
| Gap rendering | Modelled synthetically; the archive's quality marks are not read at all |
| Time handling | The archive's naive local timestamps have no conversion boundary |
| Tag identity | The archive stores numbers; no name mapping exists anywhere |
| Test bench | No database to develop against, and no data shaped like the archive |

## Target end state

| Concern | Today | Target |
| --- | --- | --- |
| Production data source | `RandomStubDataProvider` | `PostgresDataProvider`, selected by configuration; a missing or invalid configuration is a visible error state, never a silent stub |
| Synthetic stub | composition-root default | project deleted; manual "see something in the UI" runs on a seeded live demo database through the real provider |
| Failure reporting | generic `Result` errors, log strings | two decoupled error planes (SemiStep pattern): a finite, stable public surface in Core — one sealed error type with structured fields per operator-visible state — and freely changing internal errors that cross the boundary only mapped into a public type, detail riding `CausedBy` into the log |
| Layer spacing | period (1 min / 1 h / 1 d) | period ÷ 4 (15 s / 15 min / 6 h) |
| Layer thresholds | fixed ceilings on window width | derived from `window / targetColumnCount ≥ spacing`, hysteresis retained |
| Wide-window reduction | client-side only | server-side pixel buckets when the layer is denser than the canvas |
| Gaps | synthetic | reconstructed from `q = 32` / `q = 16`, distinguished from unchanged values |
| Timestamps | UTC throughout | converted from naive local at the provider boundary, UTC above it |
| Pen catalogue | synthetic list | `semiplot_tags`, filled by hand |
| Test bench | none | a populated local database with archive-shaped data, plus DB-free tests over fixture rows |

Every architectural choice behind this table is already recorded: `docs/architecture/scada-archive.md`
for the archive, `data-integration.md` for the contract and the exact SQL, `postgres-instance.md`
for the server, `history-read-path-evaluation.md` for why nothing of ours runs inside the database.

## Why it is safe

The blast radius is bounded by the provider seam, which was built for exactly this substitution.

`IDataProvider` is referenced from ten files: its own definition, the stub and its DI extension, the
composition root and `App.axaml.cs`, `TrendCoordinator`, two view models, and two test files
including `FakeDataProvider`. Adding a second implementation touches none of them except the
composition root.

`AggregationLayer` is referenced from eighteen files, but the change is confined to what
`ToSampleInterval` returns and how `LayerForWidth` derives its ceilings. The enum itself, its
ordering and its use as a request field are unchanged, so every consumer that merely carries a layer
value is unaffected by construction. The consumers that would notice are the stub provider, which
uses the interval to synthesize history, and the navigation controller's thresholds — both are
inside the first slice.

The database side is additive only. Nothing in this roadmap writes to `trends` or `messages`,
creates an index on them, or attaches a trigger. The only object we create is `semiplot_tags`.

## Guard strategy

Each guard below is a hypothesis the owning slice plan must confirm fires at HEAD before relying on
it.

- **The existing 250 tests.** The layer-spacing slice changes numbers that `AggregationLayerTests`,
  `ChartNavigationControllerTests` and `RandomStubDataProviderTests` assert directly; those tests
  failing is the intended signal, and their updated values are the specification.
- **Statement-text pinning.** Every SQL statement is asserted character for character together with
  its parameter names, so a change in the code that the architecture docs do not describe surfaces as
  a failing diff rather than as an opinion.
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
- **Public-surface coverage test.** A build-time reflection test (added in the composition slice)
  enumerates every public, non-abstract error type in Core and fails when one lacks a mapped UI
  state — a new public error type cannot silently leak past the operator, and an internal error
  cannot silently become public. SemiStep's `CoreErrorLocalizationCoverageTests` is the model.

## Slices

### Slice layer-ladder-spacing — Status: IN-PROGRESS
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
- **Plan:** —
- **PR:** —
- **Branch:** —

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
    mismatch, unreachable database, schema mismatch, query timeout, ...). An operator-visible state
    that is not a failure — an empty catalogue among them — travels in the success channel and gets
    no error type; postgres-catalog-and-extent settles that split. Each carries
    structured fields via a primary constructor and builds its message in the base constructor.
    This surface is the stable contract: the UI maps it to states, tests assert on it, and it grows
    only when a new operator-visible state exists — SemiStep's published rule, "a public error type
    exists iff a distinct operator sentence exists", enforced there by a build-time reflection
    coverage test; the composition slice adds the same enforcement here.
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

### Slice postgres-catalog-and-extent — Status: PENDING
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
  `default(DateTime)` would hand the minimap an extent beginning in year 0001. This slice also
  introduces the single class that owns every SQL statement on the application and provider path,
  and the discipline that no SQL exists anywhere else on that path. The gated harness — container,
  provisioning, template cloning, skip policy, traits — is owned by archive-populator and reused here
  unchanged.
- **Issue:** none
- **Blast radius:** the provider, plus three files outside it —
  `SemiPlot/SemiPlot.Core/Data/ArchiveExtent.cs` and `SemiPlot/SemiPlot.Core/Trends/PenLineStyle.cs`
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
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-history-read — Status: PENDING
- **Scope:** History from a chosen layer by direct read. Inherit the single statement class from
  postgres-catalog-and-extent and add the windowed statement to it. Implement the windowed read
  constrained on the variable list, the layer and the time bounds, ordered for
  per-pen assembly, with timestamps converted at the boundary. Fold the returned rows into one
  envelope per pen through the existing decimator, preserving the strictly ascending contract. Pin
  the statement text and parameter names in unit tests, and assert through `EXPLAIN` that the query
  reaches its rows through an index and scans no row-holding `trends` partition sequentially — the
  plan cannot name `tpk`, for the reason in Guard strategy. A read exceeding the reader role's
  `statement_timeout` (SQLSTATE `57014`) surfaces as a typed error, not a bare exception.
- **Issue:** none
- **Blast radius:** the provider only.
- **Risk:** medium, concentrated in envelope assembly against archive-shaped input — anchor pairs and
  steady stretches behave differently from the evenly spaced synthetic data the decimator has seen so
  far.
- **Depends on:** postgres-catalog-and-extent
- **Stacking base:** master
- **Scope guard:** no server-side bucketing, no gap reconstruction beyond what the decimator already
  does, no realtime.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-bucketed-read — Status: PENDING
- **Scope:** Server-side reduction to pixel columns for windows where the chosen layer is still
  denser than the canvas. A bucketing statement returning at most one row per column per pen with
  the minimum, maximum, first and last values, the edge timestamps, the edge quality codes and a
  break count, with buckets aligned to the window start so the leftmost column is not clipped. The
  provider chooses between this path and the direct read by the expected row count. Statement text
  pinned; an integration test compares bucketed output against the same window read directly.
- **Issue:** none
- **Blast radius:** the provider only; adds a second read path alongside the first.
- **Risk:** medium, concentrated in bucket alignment and in the choice threshold between the two
  paths.
- **Depends on:** postgres-history-read
- **Stacking base:** master
- **Scope guard:** no gap reconstruction changes, no realtime, no layer-selection changes.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-gap-reconstruction — Status: PENDING
- **Scope:** Make breaks render correctly on both read paths. A sample marked as the last before a
  break is followed by a gap anchor; the first sample after a break resumes the line; and a long run
  with no rows that is not preceded by a break marker renders as a horizontal continuation rather
  than as a break. The same reconstruction is driven from the bucketed path's edge quality codes and
  break count. Tests cover both paths, including a break spanning several buckets, and run against
  the fixture rows extracted from a real archive as well as against the populated database.
- **Issue:** none
- **Blast radius:** the provider's envelope assembly; the rendering path above it already understands
  gap anchors.
- **Risk:** high relative to the rest — this is the behaviour most likely to be subtly wrong, and
  wrong in a way that looks plausible on screen. A break drawn as a straight line across hours is
  the failure mode that misleads an operator.
- **Depends on:** postgres-bucketed-read
- **Stacking base:** master
- **Scope guard:** no changes to the statements themselves beyond what gap data requires; no realtime.
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
  `Subscribe` is the last member of the scaffold left unimplemented, so this slice also **owns the
  deletion of `ProviderNotImplementedError`** — the temporary error type the scaffold's members
  return — together with the last body that returns it.
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
  apart: no connection file, a malformed file, no connection, no archive table, an unexpected table
  shape, and a non-empty default partition. An empty pen catalogue is not among them:
  postgres-catalog-and-extent settles it as a success-channel state, so this slice surfaces it as a
  UI state reached through a successful `Result` and pins it with a named test of its own, outside
  the reflection coverage guard, which enumerates error types only. That named test is what keeps
  `postgres-instance.md`'s "normal states with their own message" a state something can force and
  something can see. The UI maps each public error type onto a distinct visible state — the
  application stays alive, draws nothing, and says why —
  and a build-time reflection coverage test (see Guard strategy) makes the mapping total: every
  public error type in Core has a UI state, and internal errors reach the UI only wrapped in a
  public envelope. **There is no stub fallback**:
  the database is part of the service, and an unreachable database is an error, never silently
  substituted synthetic data. The stub remains selectable only by an explicit development flag until
  the final slice deletes it. DI tests cover the selection; a thin end-to-end suite (5–7 journeys on
  `Avalonia.Headless` against a bench-seeded database, gated like the data tests) proves the
  composed application: pens listed from `semiplot_tags`, history drawn with counts consistent with
  the seed, a break rendered as a broken line, layer switch on zoom, a live insert arriving once,
  and one test per startup error state asserting the UI state — never log text. On Windows CI the
  suite reaches PostgreSQL through `SEMIPLOT_TEST_PG` (the runner image ships a stopped PostgreSQL
  service — verify the image at slice time). This automates the roadmap's close condition.
- **Issue:** none
- **Blast radius:** the composition root and startup path — the first slice that changes what the
  running application does by default.
- **Risk:** medium, concentrated in the error-state mapping and in keeping the E2E suite thin: the
  layer boundaries are already covered by contract, so E2E asserts composition, not behaviour
  matrices.
- **Depends on:** postgres-gap-reconstruction, postgres-realtime-poll
- **Stacking base:** master
- **Scope guard:** no new queries; no UI redesign of the error states beyond surfacing them; no
  stub deletion — that is the final slice's.
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

Every slice not marked DROPPED has a MERGED PR. No slice owns an issue, so no issue closes
automatically; there is no tracking issue to close by hand. The functional close condition is that
the application, pointed at a database seeded by the bench, draws real history, follows a live
edge moved by the `--follow` demo writer, selects layers by window width, breaks the line only
where the archive says a break occurred, and `SemiPlot.DataSource.Stub` no longer exists. The
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
