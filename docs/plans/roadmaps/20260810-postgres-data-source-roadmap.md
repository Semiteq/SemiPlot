# PostgreSQL data source roadmap

**Issues:** none declared — the repository has no issue tracker in use. This roadmap covers the
whole span from "the viewer runs on synthetic data" to "the viewer reads a real Simple-Scada
archive", sliced into nine independently shippable pull requests.

## Summary

SemiPlot renders trends correctly but has never read a real archive: the only implementation of
`IDataProvider` emits random walks. The architecture for reading the Simple-Scada 2 PostgreSQL
archive is settled and documented, and one piece of already-shipped code — the aggregation-layer
thresholds — is wrong by a factor of four against that architecture. Nine slices deliver a
production provider plus the local test bench it is developed against. The roadmap closes when the
application, pointed at a populated database, draws real history, follows the live edge, and selects
archive layers by window width.

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
| Production data source | `RandomStubDataProvider` | `PostgresDataProvider`, chosen by configuration, stub retained for tests |
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
- **`EXPLAIN` assertions.** Gated integration tests assert that the windowed history query and the
  realtime poll use the `tpk` primary key. This turns the two documented hazards — a missing layer
  predicate and a missing variable list — into enforced invariants.
- **Gated integration suite.** Database-touching tests skip cleanly when no server answers, so the
  default suite stays green on a machine without one.
- **Fixture rows from a real archive.** Envelope assembly and gap reconstruction are tested against
  rows extracted from a real Simple-Scada dump, not against rows we imagined.

## Slices

### Slice layer-ladder-spacing — Status: PENDING
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

### Slice archive-populator — Status: PENDING
- **Scope:** Build the local test bench. Extract the verified archive DDL from the customer's dump
  into a schema script held in the repository — daily range partitions, the default partition, the
  `(id, l, t)` primary key, `timestamp(3) without time zone` — so the bench reproduces the structure
  a real SCADA creates rather than a hand-written approximation. Add a deterministic populator, a
  standalone script outside the solution, that creates the daily partitions itself and writes
  archive-shaped data: anchor pairs on change at a 100 ms grid, long steady stretches with no rows,
  steps at recipe transitions, noise around setpoints, occasional spikes, and real breaks marked
  `q = 32` then `q = 16` with no rows in between. It fills the coarse layers by the vendor's
  documented rule — first, last, minimum and maximum of each period, deduplicated, copied verbatim
  with the same timestamps, values and quality, with break markers replicated into every layer.
  Variable count, day count and change rate are parameters; randomness is seeded so runs reproduce.
  It writes into a dedicated database, never into the leftovers from earlier experiments. Also
  extract a small fixture of real rows from the dump for the database-free tests that later slices
  need.
- **Issue:** none
- **Blast radius:** additive only — a script and a schema file. No solution project, no application
  code.
- **Risk:** medium, concentrated in fidelity: data that does not reproduce the archive's shape would
  make every later slice's tests pass against conditions that never occur.
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no provider code; no alternative layer-selection rule; nothing that writes to a
  production archive.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-provider-scaffold — Status: PENDING
- **Scope:** Stand up the provider project with everything that needs no query. A
  `SemiPlot.DataSource.Postgres` project referencing Core only, Npgsql added through central package
  management, a DI extension registering the provider, and the provider itself implementing
  `IDataProvider` with unimplemented bodies. The connection settings loader: a YAML file in a
  configuration directory, a version field checked on load, a settings record, and a connection
  string built through the Npgsql builder rather than by concatenation. The time boundary
  converter: naive local to UTC on the way out, UTC to naive local for query bounds, with the zone
  resolved once from configuration. All of it is pure logic and testable without a database.
- **Issue:** none
- **Blast radius:** additive — one new project and its registration. The composition root is not
  switched over in this slice.
- **Risk:** low
- **Depends on:** independent
- **Stacking base:** master
- **Scope guard:** no SQL, no queries, no change to which provider the application uses.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-catalog-and-extent — Status: PENDING
- **Scope:** The first two operations that touch the database. Create `semiplot_tags` and load the
  pen catalogue from it, mapping the stored line style onto the domain enum and treating an empty or
  absent table as an empty pen list rather than as a failure. Implement the archive extent using
  per-variable bounded subqueries, because an unbounded minimum over the whole table cannot use the
  primary key and scans the entire archive. Establish the gated integration test pattern here: a
  disposable database, clean skipping when no server answers, and the trait scheme the rest of the
  slices reuse.
- **Issue:** none
- **Blast radius:** the provider only; the application still runs on the stub.
- **Risk:** medium, concentrated in the integration test harness — if skipping is not clean, the
  default suite stops being trustworthy on a machine without a database.
- **Depends on:** archive-populator, postgres-provider-scaffold
- **Stacking base:** master
- **Scope guard:** no history queries, no realtime, no composition changes.
- **Plan:** —
- **PR:** —
- **Branch:** —

### Slice postgres-history-read — Status: PENDING
- **Scope:** History from a chosen layer by direct read. Introduce the single class that owns every
  SQL statement in the solution and the discipline that no SQL exists anywhere else. Implement the
  windowed read constrained on the variable list, the layer and the time bounds, ordered for
  per-pen assembly, with timestamps converted at the boundary. Fold the returned rows into one
  envelope per pen through the existing decimator, preserving the strictly ascending contract. Pin
  the statement text and parameter names in unit tests, and assert through `EXPLAIN` that the query
  uses the primary key.
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
  tick. Disposal stops the poll; a query error logs and drops that tick without throwing on the UI
  thread and without terminating the observable; and the provider never emits a timestamp at or
  before the last one already delivered, which is what keeps the history-to-realtime seam monotonic.
  An integration test appends rows and asserts they arrive once, in order, without duplicates, and an
  `EXPLAIN` assertion pins the index usage.
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
- **Scope:** Make the application actually use the provider. A startup probe verifies the shape of
  the archive table against the catalogue and distinguishes the states the operator must be able to
  tell apart: no connection, no archive table, an unexpected table shape, an empty pen catalogue, and
  a non-empty default partition. The composition root selects the PostgreSQL provider when a valid
  connection file is present and falls back to the stub otherwise, reporting a malformed file loudly
  instead of crashing. DI tests cover both branches. This is the slice after which the application,
  pointed at a populated database, draws real data.
- **Issue:** none
- **Blast radius:** the composition root and startup path — the only slice that changes what the
  running application does by default.
- **Risk:** medium, concentrated in the fallback behaviour: a misconfigured installation must degrade
  visibly rather than silently showing synthetic data as if it were real.
- **Depends on:** postgres-gap-reconstruction, postgres-realtime-poll
- **Stacking base:** master
- **Scope guard:** no new queries; no UI redesign of the error states beyond surfacing them.
- **Plan:** —
- **PR:** —
- **Branch:** —

## Close condition

Every slice not marked DROPPED has a MERGED PR. No slice owns an issue, so no issue closes
automatically; there is no tracking issue to close by hand. The functional close condition is that
the application, pointed at a database populated by the bench, draws real history, follows the live
edge, selects layers by window width, and breaks the line only where the archive says a break
occurred.

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

## Open forks for the operator

**The vendor's layer selection rule is documented but not measured by us.** The manual and two
vendor forum answers state that a coarse layer holds up to four points per period chosen by
magnitude, and the measured dump is consistent with it, but we have never watched the SCADA thin a
period with our own instrument. Confirming it needs a running installation, which does not exist
yet. The design stands regardless: if the rule turns out to differ, the read path stops trusting
coarse layers for envelopes and the lazy-materialisation alternative above becomes the answer, which
changes the provider's layer strategy and nothing else. The experiment and its query are recorded at
the end of `docs/architecture/scada-archive.md` and run when a stand becomes available.

**Retention depth and disk size are unset.** Both need a measured write rate from a working
installation, and both are recorded as undecided in `docs/architecture/postgres-instance.md`. The
design stands regardless — no slice depends on the number.

**Backup method for the supplied instance is unset.** Recorded as undecided in the same document.
It is an operations decision, not a code decision, and no slice depends on it.
