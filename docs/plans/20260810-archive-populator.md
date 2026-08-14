# Archive populator — a local bench shaped like the vendor archive

## Overview

Later slices of the PostgreSQL data source have nothing to develop against. This slice builds the
bench: the archive schema as a script in the repository, a deterministic generator that fills it with
data shaped like a real Simple-Scada archive, and the test harness the rest of the slices reuse.

The problem it solves is fidelity. A hand-written table and evenly spaced synthetic samples would let
every later test pass against conditions that never occur in production. The archive writes anchor
pairs on change, leaves long stretches with no rows at all, marks breaks in a quality column, and
carries thinned copies of the same rows in three coarser layers. A bench that reproduces those
properties is what makes the provider's tests worth anything.

It integrates as an additive tool. One new console project holds the generator, and one new test
project calls the same code the console does, so the rule that fills the coarse layers is written
once and exercised without a database.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice archive-populator

**Files and components involved**

- `SemiPlot.slnx:2-5` — the solution currently holds four projects: Core, DataSource.Stub, Tests, UI.
  Two are added: the seeder console and a test project for it.
- `SemiPlot/Directory.Build.props:5` sets `TargetFramework` to `net10.0` for every project, and
  `:9` already sets `IsPackable=false` globally — neither new project may redeclare either.
- `SemiPlot/Directory.Packages.props` uses central package management and currently has no `Npgsql`
  entry.
- `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj:4` targets `net10.0-windows` and `:26` references
  `SemiPlot.UI`. That project is not modified by this slice at all.

**Copied rather than referenced**

- `SemiPlot/SemiPlot.DataSource.Stub/SyntheticValueWalk.cs:5` (a pure deterministic function built
  from two decorrelated sine waves plus SplitMix64 hash jitter),
  `SemiPlot/SemiPlot.DataSource.Stub/SyntheticPenCatalog.cs:13` (five groups with identifier bases
  1000–5000 and per-group value ranges) and the `SyntheticPen` record they use are **copied
  verbatim into the seeder project, which becomes their owner**. The seeder must not reference
  `SemiPlot.DataSource.Stub`: the stub evolves for UI reasons (the layer-ladder-spacing slice edits
  its synthesis) while the bench must stay frozen — eight later slices develop against its output.
  The stub keeps its own copies until the live-demo-and-stub-retirement slice deletes the project.
- `SemiPlot/SemiPlot.DataSource.Stub/SyntheticQuality.cs:8` is not copied: a bad sample is a
  different concept from a break marker, and the bench emits only `q ∈ {0, 16, 32}`.

**The archive shape being reproduced** — all from `docs/architecture/scada-archive.md`:

- `:27-35` the `trends` DDL: `id integer`, `l smallint`, `t timestamp(3) without time zone`,
  `v double precision`, `q integer`, `PARTITION BY RANGE (t)`, `PRIMARY KEY (id, l, t)` named `tpk`.
- `:57` partitions are named `tpYYYYmMMdDD` with day bounds, plus a `tpdefault` catch-all.
- `:112-120` coarse layers hold verbatim copies of raw rows — same timestamp, value and quality — up
  to four per period, selected by magnitude, strictly nested `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0`.
- `:161-165` quality codes: `0` ordinary, `16` first sample after a break, `32` last before a break.
- `:171` marker rows are copied into every layer unchanged.
- `:186-191` change-based archiving writes two rows per change — the previous value at the last poll
  tick before the change, then the new value at the change tick, one poll interval apart.
- `:194-195` row count scales with the number of changes, not with elapsed time.
- `:233-243` reader hazards, and the rule that a non-empty `tpdefault` is a fault signal.
- `:254` the measured dump spans two hours with twelve restarts, so `l=2` and `l=3` were never
  exercised across their own periods.

**Dependencies identified**

New entries in `SemiPlot/Directory.Packages.props`, versions verified 2026-08-10:

- `Npgsql` 10.0.3 — the only new runtime dependency.
- `Testcontainers.PostgreSql` 4.13.0 — starts the PostgreSQL the gated tests talk to.
- `xunit.v3` 3.2.2 — for the new test project.

One dependency is not a package. `semibase` (`github.com/Semiteq/SemiBase`, pinned `v0.1.0`)
provisions the container the gated tests use. It is a Go binary acquired as a release asset, found
through `SEMIBASE_EXE` or on `PATH`, and treated like the container runtime: absent means the gated
tests skip with a reason. As of that tag (`aa037a4`) every command compiles and runs on any
platform — the Win32 dependencies were removed for exactly this consumer, and the SemiBase CI
already provisions a `postgres:17-alpine` service container on an Ubuntu runner by running `all`
twice. The `v0.1.0` GitHub release ships binaries (`semibase_0.1.0_linux_amd64`,
`semibase_0.1.0_windows_amd64.exe`); acquisition is downloading the pinned binary and pointing
`SEMIBASE_EXE` at it — no Go toolchain, the repository is public.

**Measured on this machine, 2026-08-10**

- PostgreSQL 14 is running: service `postgresql-x64-14`, and
  `C:/Program Files/PostgreSQL/14/data/postgresql.conf` sets `port = 15432`. PostgreSQL 17 is also
  installed but configured for port 5432 with no running service.
- `pg_restore.exe` exists at `C:/Program Files/PostgreSQL/14/bin/` and is not on `PATH`. Neither is
  `psql`. Anything the application code does must go through `Npgsql`.
- The customer archive dump, kept outside the repository, is `PostgreSQL custom database dump
  - v1.14-0`, 18185 bytes. It is not readable as text; extracting anything from it requires
  `pg_restore`. Its path is a local detail and is not recorded here.
- Docker Desktop 4.86.0 is installed per-user, so its `docker.exe` is under the user profile rather
  than in `Program Files` and may be absent from a shell's `PATH`.
- `CREATE DATABASE` costs 630 ms and `DROP DATABASE` 180–250 ms against the running server, because
  the template copy on NTFS is a physical file copy rather than copy-on-write. `initdb` for a
  throwaway cluster takes 14.2 s, which is why no test starts its own.

## Development Approach

- **testing approach**: Regular — implement, then add or update tests in the same task.
- Complete each task fully before moving to the next.
- Every task that changes code carries its own tests, listed as separate checklist items.
- All non-gated tests pass before the next task starts.
- Update this plan when scope changes during implementation.

## Testing Strategy

**One new test project holds everything this slice produces.** `SemiPlot.Tests.Data` targets plain
`net10.0`, uses xunit v3, and references `SemiPlot.Core` and `SemiPlot.Tools.ArchiveSeeder` — never
the UI and never an Avalonia package. Both the pure tests and the container-gated ones live there.

The reason for a second project is the target framework, not the test framework.
`SemiPlot.Tests` is `net10.0-windows` and references `SemiPlot.UI`, so it structurally cannot run on
a Linux CI runner; a project that touches neither Avalonia nor the UI has no reason to inherit that.
xunit v3 follows from the split rather than justifying it — for a greenfield project it is the
current version, and the constraint pinning `SemiPlot.Tests` to v2 (`Avalonia.Headless.XUnit`) does
not exist here. `SemiPlot.Tests` is not modified by this slice.

**Pure, no container.** The generator is a function: given a seed, a pen list, a day span and a
change rate, it returns rows. Every property the bench claims is asserted directly on those rows —
anchor pairs sit one poll interval apart, break markers appear as `32` then `16` with nothing
between, coarse layers are subsets of the raw layer, no period holds more than four non-marker rows,
and each period's extremes survive. This is where the fidelity risk is actually controlled.

**Gated, with a container.** Traits `[Trait("Area","Data")]` and `[Trait("Category","Integration")]`.
One PostgreSQL container per test run through Testcontainers, provisioned by `semibase create`, a
template database seeded once, and a clone of it per test class.

**The bench exercises the production role separation.** SemiBase provisions the container exactly as
it provisions a site — `scada_writer`, `semiplot_reader`, the grants and the default-privileges
chain — and the tests then use those roles rather than the container's superuser. The seeder writes
as `scada_writer` and creates the archive tables itself, the way the SCADA does, which is what makes
the default-privileges chain a path exercised on every run instead of a commissioning-day surprise.
Every gated read connects as `semiplot_reader`, so a broken grant fails a test today.

Nothing in this repository defines those roles or that DDL. A copy here would become the thing
exercised daily while `semibase` decayed into a tool run once, on site, on the day it matters —
which is the failure the two repositories are arranged to avoid.

**Environment carries the policy.** `SEMIPLOT_TEST_PG`, when set, points the fixture at an existing
server instead of starting a container, so a machine without Docker still runs the suite — that
server must itself be semibase-provisioned, and the fixture re-runs `create` against it, which is
idempotent. `SEMIPLOT_REQUIRE_DB=1`, set by the CI job, turns an unreachable runtime from a skip
into a failure; skipping is correct on a developer machine and wrong in a pipeline.
`SEMIPLOT_PG_IMAGE` overrides the image tag. `SEMIBASE_EXE` locates the provisioning binary when it
is not on `PATH`. Role passwords split by path: on the container path the fixture supplies **fixed
dummy passwords itself** — the container is ephemeral and holds no secrets, and a developer must
not need environment variables to run the suite; only on the `SEMIPLOT_TEST_PG` path do the
`SEMIBASE_*_PASSWORD` variables carry the real passwords into `create`.

**Two runtimes gate the same way.** A missing container runtime and a missing `semibase` binary are
both reported as an unavailable reason with a stated cause, never as a pass. `semibase` is acquired
by downloading the `v0.1.0` release binary; the pin is a tagged release rather than a moving
`latest`, so a change in that repository cannot fail this suite without a version to blame.

## Acceptance Evidence

The defect this slice addresses is the absence of a bench, so the evidence is that the bench exists
and produces data with the archive's properties. Every check below is a runnable command.

1. **Self-check on generated rows, no container.**
   `dotnet test SemiPlot/SemiPlot.Tests.Data --filter "Category!=Integration"`
   All tests pass. These assert the anchor-pair, marker, nesting and four-per-period properties
   listed under Testing Strategy.

2. **The thinning rule is confronted with a real archive.**
   Task 10's assertions on real rows pass: every real `l = 1` row equals some real `l = 0` row, both
   extremes of the minute survive, markers appear in every layer. Its comparison of `LayerThinner`'s
   output against the real `l = 1` set is recorded as a finding, not asserted — the identity of the
   non-extreme points is `UNVERIFIED` and a marker-bearing minute can exceed the four-per-period
   budget, so exact equality is not a safe gate.

3. **An unreachable runtime is never a pass, and never a silent skip in CI.**
   With Docker stopped and `SEMIPLOT_TEST_PG` unset,
   `dotnet test SemiPlot/SemiPlot.Tests.Data` reports every gated test as **skipped** with a stated
   reason — none passed, none failed. The same command with `SEMIPLOT_REQUIRE_DB=1` **fails** instead.
   Removing `semibase` from `PATH` with `SEMIBASE_EXE` unset produces the same two behaviours, with
   its own reason.

4. **The existing suite is untouched.**
   `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` reports the same count it reports at
   the branch point, with zero failures, and `git diff` shows no change under `SemiPlot.Tests/`.

5. **A seeded database matches the generator, read through the production role.**
   `semibase create` against the target, then
   `dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- --connection "<scada_writer string>" --days 1 --pens 8 --seed 1 --end 2026-01-02T00:00:00`,
   then the gated tests. Row counts per layer read back **as `semiplot_reader`** equal the counts the
   generator reported, `semiplot_tags` holds one row per seeded pen, and `tpdefault` is empty.
   Reading as the superuser would not prove the grant chain, which is the point of using the role.

6. **The `trends` DDL is the vendor's, not ours.**
   The table definition in `sql/semiplot_dev.sql` is identical, modulo whitespace and ownership
   statements, to `pg_restore --schema-only` output from the customer dump. Task 1 records the command
   that produced it. `messages` is deliberately excluded, since no slice in this roadmap reads it.

7. **CI enforces the availability policy.**
   The `data-tests` job runs on `ubuntu-latest` with `SEMIPLOT_REQUIRE_DB=1` and is green on the
   pull request; the Windows job is unchanged.

## Progress Tracking

- Mark completed items `[x]` immediately when done.
- Add newly discovered tasks with ➕.
- Document blockers with ⚠️.
- Keep this file in sync with the work actually done.

## Solution Overview

**Event-driven generation, not tick scanning.** The archive's row count follows the number of changes
(`docs/architecture/scada-archive.md:194-195`). Scanning a 100 ms grid to look for changes would cost
864 000 iterations per pen per day and produce almost nothing at each one. Instead each pen is a
sequence of segments — idle run, ramp, step, spike, break — whose durations are drawn from a seeded
generator. Only segment boundaries produce rows, so the cost is proportional to the output. Values at
chosen ticks come from `SyntheticValueWalk`.

**One generator, two callers.** `SemiPlot.Tools.ArchiveSeeder` is a console project holding the
generation and write logic. The console parses arguments and writes to a database; the test project
references the same project and calls the generator directly. An executable project reference behaves
identically to a library reference in .NET, and `Program.cs` is the only console-specific file, so a
separate class library would add a project that earns nothing until a third consumer exists.

**Thinning in C#, not in SQL.** The coarse layers could be filled with an `INSERT ... SELECT` using
window functions, which would be shorter. They are filled in C# instead, because the vendor's
selection rule is the thing most likely to be wrong and a rule written in SQL cannot be tested
without a database — nor confronted with the real rows extracted in Task 10. The generator produces
all four layers as ordinary row collections; the write path is indifferent to which layer a row
belongs to.

**The seeder never destroys.** It connects to a database that already exists, refuses to proceed if
`public.trends` is already present, and issues no `DROP DATABASE` anywhere. The only place a database
is dropped is the test fixture, which drops only databases it created.

**One container per run, one database per test class.** A container start costs seconds; a
`CREATE DATABASE` costs 630 ms measured. So the container is the wrong isolation unit and the
database is the right one. The fixture starts the container once, provisions it, seeds a template,
and each test class clones that template. Cloning skips the schema apply entirely, and the later
slices all want the same seeded shape.

**The container is provisioned by SemiBase, not by this repository.** Between container start and
seeding the fixture runs `semibase create`, the same command that provisions a site. That is what
makes the role separation, the grants and the default-privileges chain a path exercised on every
test run rather than a commissioning-day surprise — and it keeps one definition of provisioning
instead of a copy here that would drift, and would be the copy actually proven.

**Testcontainers rather than a server on the machine.** The version under test is pinned in code
instead of being whatever the machine happens to have, state is fresh by construction, and the same
fixture runs unchanged on a Linux CI runner. Two costs accepted knowingly: gated tests need a
container runtime — Docker Desktop's licence requires a paid subscription above 250 employees or
$10M revenue, and Podman and Rancher Desktop are free alternatives Testcontainers supports — and
they need a binary built from another repository, pinned at `v0.1.0`, which can now fail this suite.
That coupling is the point rather than a side effect. `SEMIPLOT_TEST_PG` is the escape hatch for a
machine without a container runtime, provided the server it names was itself provisioned by
`semibase`.

## Technical Details

**Row model.** One record type carries `(int Id, short Layer, DateTime Timestamp, double Value,
int Quality)` — the five columns of `trends`. `Timestamp` is naive local, matching
`timestamp(3) without time zone`, and is never treated as UTC inside the seeder. It is truncated to
whole milliseconds in the constructor: .NET carries 100 ns ticks and the column carries three
decimal places, so two rows distinct in memory can collide on `(id, l, t)` once PostgreSQL rounds
them — an in-memory uniqueness test would pass while the `COPY` five tasks later fails on `tpk`.

**Value generation: the walk sets the level, the segment shapes the path.** `SyntheticValueWalk` is
a clamped sum of two sines plus hash jitter; it never holds a constant and never rises monotonically,
so it cannot by itself produce an idle run or a ramp. It is called once per **segment**, with the
segment index in place of the tick index, and supplies that segment's target level within the pen's
range. The segment kind then decides the trajectory to that level:

| Segment | Trajectory | Rows emitted |
| --- | --- | --- |
| Idle | holds the previous level | none |
| Step | jumps to the new level at one instant | one change |
| Ramp | moves to the new level across its duration | one row per tick, no pre-anchors |
| Spike | leaves the level and returns within a few ticks | one row per tick of the excursion |

This keeps the copy honest: the waveform stays the copied walk's, deterministic in `(seed, penId,
segmentIndex)`, and the archive's step-shaped character comes from the segment vocabulary rather
than from sampling a sine.

**The poll interval is local, not a global lattice.** The measured sample at
`docs/architecture/scada-archive.md:187-191` reads `13:50:44.113 → 44.213 → 46.337 → 46.437`. Each
pair is exactly 100 ms apart, but the two pairs are 2224 ms apart — not a multiple of the poll
interval. No single 100 ms lattice contains all four timestamps, so the archive's timestamps are not
globally aligned and the generator must not pretend they are. The invariant to reproduce and to test
is pair-local: **a change row is preceded by a row exactly one poll interval earlier carrying the
previous value** — with three exceptions, each of which the generator itself creates:

1. the run's first row, which has no predecessor;
2. the `q = 16` row resuming after a break, where a pre-anchor would fall inside the gap Task 4
   forbids;
3. a change whose predecessor is already less than one poll interval old, where the pre-anchor is
   suppressed to avoid a duplicate key — the ramp and spike case.

Stated with them: *every change row that is not the run's first row and not a `q = 16` row is
preceded by a row exactly one poll interval earlier.* Change instants sit on a per-pen local grid
whose step is at least one poll interval, which is what a 100 ms poll physically implies. Segment
boundaries otherwise fall at arbitrary millisecond offsets, and no global lattice is imposed.

**Anchor pairs, conditionally.** A change emits the previous value one poll interval before it and
the new value at the change instant (`:186-191`). The pre-anchor is emitted **only when the pen's
last written row is older than one poll interval**. Without that condition a ramp or spike — where
the value changes every tick — makes the pre-anchor of one change collide with the change row of the
previous one, producing a duplicate `(id, l, t)` that `tpk` rejects. The vendor's sample shows pairs
because the value was steady between changes; during a ramp the archive writes one row per tick.

**Breaks.** A break emits the last sample before it with `q = 32` and the first sample after it with
`q = 16`, with no rows in the interval between (`:161-165`). Break duration is a generator parameter.
Both marker rows carry real values.

**Coarse layers.** For each layer period — minute, hour, day — group the raw rows by calendar-aligned
period and take first, last, minimum and maximum, deduplicated when they coincide. Rows are copied
verbatim: same timestamp, same value, same quality (`:112-120`). Every marker row is copied into
every layer regardless of selection (`:171`). Nesting follows automatically, since a day's extremum
is also its hour's. Calendar alignment is a documented assumption, not a vendor statement —
`docs/architecture/scada-archive.md:251` records the question as open — so the period boundary is
computed in one place that the alternative could later replace.

**Partitions.** The seeder creates `tpYYYYmMMdDD` partitions for every day the run covers before
writing (`:57`), because a missing partition sends rows to `tpdefault`, which the later slices treat
as a fault signal (`:241-243`). `COPY` into the partitioned parent routes rows to the right partition
on PostgreSQL 11 and later, so the write path targets `public.trends`.

**Parameters.** `--connection`, `--admin-connection`, `--days`, `--pens`, `--seed`,
`--change-seconds` (mean interval between changes), `--break-count`, `--end`. `--end` has no
default: a floating "now" would make two runs of the same seed produce different data and break the
reproducibility the bench exists for. The test fixture passes a fixed instant.

**Tag catalogue.** When `--admin-connection` is set, the seeder fills `semiplot_tags` from its pen
catalogue — one row per seeded pen with matching `id`, name, group, color and line style — so the
catalog-reading slices develop against named pens and the demo database shows names, not numbers.
The write goes through the admin connection because `scada_writer` holds no privilege on
`semiplot_tags`, which is correct production parity: on a site that table is filled by hand during
commissioning. Without the flag the seeder writes `trends` only.

**`--end` is exclusive**, so the run covers `[end - days, end)` and the newest row falls strictly
before it. An inclusive bound would put the last instant of `--days 1 --end 2026-01-02T00:00:00` in
the *next* day's partition range, which the writer never creates, sending one row to `tpdefault` —
which the same acceptance check requires to be empty.

**Pen selection.** `--pens N` takes N pens round-robin across the five groups of
`SyntheticPenCatalog`, never the first N. First-N gives eight Heaters: one group, one value range,
20–850 throughout. Every later slice would then develop envelope assembly and layer selection
against a pen set with no multi-axis case and no heterogeneity, and gas lines — the deliberately
varied group — would never appear.

**Volume.** At a change every 5 s a pen produces about 17 280 changes per day, which is roughly
34 500 raw rows counting anchor pairs. Coarse rows are bounded by four per period, so at most
5 860 per pen per day — `docs/architecture/scada-archive.md:227` quotes 1465, which is 1440 + 24 + 1,
one point per period rather than four, and is inconsistent with the four-per-period budget stated
everywhere else in that document. The fixture's standard slice — 1 day, 8 pens, seed 1 — is
therefore about 276 000 raw rows and up to 47 000 coarse.

**What the bench cannot reproduce.** Stated here so no later slice mistakes bench coverage for real
coverage. The customer dump spans two hours with twelve restarts (`:254`), so hour- and day-layer
thinning across their own periods is confirmed by no real data. Calendar versus flush-window
alignment stays open. And the bench emits only `q ∈ {0, 16, 32}`: no bad-quality code was observed in
the dump, so inventing one would be its own fiction — the "row present, point discarded" state in the
three-state table at `:148-152` is therefore unbenched, and a slice that needs it must say so.

## What Goes Where

- **Implementation Steps** — the schema script, the seeder project, the test project, the generator,
  the write path, the container fixture, the real-row fixture, the CI job, and their tests.
- **Post-Completion** — pointing the running application at a seeded database by hand, the live
  demo mode, and the remaining roadmap slices.

## Implementation Steps

### Task 1: Establish the baseline and extract the archive schema

**Files:**
- Create: `sql/semiplot_dev.sql`
- Create: `sql/README.md`

- [ ] record the baseline at the branch point: run `dotnet test SemiPlot.slnx`, write the passing
      count and the commit into this task as a dated measurement
- [ ] run `pg_restore --schema-only` from `C:/Program Files/PostgreSQL/14/bin/` against the customer
      dump into a scratch file, and record the exact command in `sql/README.md`
- [ ] write `sql/semiplot_dev.sql` from that output: the `trends` table with its five columns and
      `PARTITION BY RANGE (t)`, the `tpk` primary key, and the `tpdefault` catch-all partition;
      strip ownership, tablespace and role statements
- [ ] confirm the result matches `docs/architecture/scada-archive.md:27-35` column for column,
      including `timestamp(3) without time zone` and the `smallint` layer
- [ ] exclude `messages` — no slice in this roadmap reads it for data
- [ ] no tests in this task: it produces a data file, and Task 7 is what executes it

### Task 2: Create the seeder and its test project

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticValueWalk.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticPenCatalog.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticPen.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Data/SeederOptionsTests.cs`
- Modify: `SemiPlot.slnx`
- Modify: `SemiPlot/Directory.Packages.props`

- [ ] console project referencing `SemiPlot.Core` only — no reference to `SemiPlot.DataSource.Stub`;
      declare neither `TargetFramework` nor `IsPackable`, both of which
      `SemiPlot/Directory.Build.props:5,9` already set
- [ ] copy `SyntheticValueWalk`, `SyntheticPenCatalog` and `SyntheticPen` verbatim from the stub
      into the seeder namespace, per *Copied rather than referenced* — the seeder owns them from
      here on; the stub's copies are untouched
- [ ] test project on plain `net10.0` with `xunit.v3` 3.2.2, `Microsoft.NET.Test.Sdk` and
      `xunit.runner.visualstudio`, referencing `SemiPlot.Core` and the seeder — never the UI, never
      an Avalonia package
- [ ] add `Npgsql` 10.0.3 and `xunit.v3` 3.2.2 to `SemiPlot/Directory.Packages.props`
- [ ] `SeederOptions` record with the parameters listed under Technical Details (including the
      optional `--admin-connection`), plus a parser that returns a `Result` rather than throwing,
      and defaults for every optional parameter except `--end`
- [ ] `Program` parses arguments, prints usage on a parse failure, and exits non-zero
- [ ] register both projects in `SemiPlot.slnx`
- [ ] write tests for the parser: defaults applied, every parameter accepted, unknown argument
      rejected, non-numeric value rejected, missing connection rejected, missing `--end` rejected
- [ ] run tests — must pass before Task 3

### Task 3: Generate raw-layer rows with anchor pairs

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveRow.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/RawLayerGeneratorTests.cs`

- [ ] `ArchiveRow` record: identifier, layer, naive-local timestamp truncated to whole milliseconds,
      value, quality
- [ ] `RawLayerGenerator` walks each pen as a sequence of segments — idle, step, ramp, spike — with
      durations drawn from the seed
- [ ] the level for each segment comes from the seeder's own `SyntheticValueWalk.Value` called with
      the **segment index**, scaled to the pen's range from the seeder's own `SyntheticPenCatalog`;
      the segment kind supplies the trajectory to that level, per the table in Technical Details
- [ ] `--pens N` selects round-robin across the catalogue's five groups, not the first N
- [ ] emit the pre-anchor exactly one poll interval before a change, and only when the pen's last row
      is older than one poll interval — a ramp writes one row per tick with no pre-anchors
- [ ] change instants sit on a per-pen local grid of at least one poll interval; segment boundaries
      otherwise fall at arbitrary millisecond offsets, and no global lattice is imposed
- [ ] write tests: identical seeds produce identical rows; every change row that is not the run's
      first is preceded by a row exactly one poll interval earlier carrying the prior value;
      timestamps are strictly ascending per pen with no duplicate `(id, l, t)` after millisecond
      truncation; a low change rate leaves stretches longer than a minute with no rows
- [ ] write a golden test pinning a hash of the standard slice (1 day, 8 pens, seed 1), so an
      accidental edit anywhere in the seeder's generation code cannot silently change the bench for
      all later slices — a deliberate waveform change updates the hash in the same commit
- [ ] write a test that the standard slice spans more than one group and more than one value range
- [ ] write tests for the edge cases: zero days rejected, a single pen, an idle segment emitting no
      rows, and a ramp changing every tick — one row per tick, no duplicates, no pre-anchors
- [ ] run tests — must pass before Task 4

### Task 4: Insert breaks marked with the vendor's quality codes

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/BreakGenerationTests.cs`

- [ ] `BreakPlan` places the requested number of breaks across the run from the seed, each with a
      duration long enough to span more than one minute period
- [ ] the last row before a break carries `q = 32`, the first row after carries `q = 16`, and no rows
      exist in between, per `docs/architecture/scada-archive.md:161-165`
- [ ] breaks apply to all pens at the same instants, matching a project stop and start
- [ ] both marker rows carry real values, not sentinels
- [ ] write tests: markers come in ordered `32` then `16` pairs; no row falls inside a break; a break
      spanning several minutes leaves those periods empty; a run with zero breaks has no marker rows
- [ ] run tests — must pass before Task 5

### Task 5: Fill the coarse layers by the vendor's rule

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LayerThinner.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/LayerThinnerTests.cs`

- [ ] group raw rows per pen by calendar-aligned minute, hour and day
- [ ] take first, last, minimum and maximum of each period, deduplicated when they coincide, copying
      timestamp, value and quality verbatim per `docs/architecture/scada-archive.md:112-120`
- [ ] copy every marker row into every layer regardless of selection, per `:171`
- [ ] keep the period-boundary computation in one method, since calendar alignment is an assumption
      recorded as open at `:251`
- [ ] write tests: no period holds more than four non-marker rows; each period's minimum and maximum
      are present; layers nest as `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0`; every coarse row matches a raw row exactly;
      a period with one raw row yields one coarse row, not four
- [ ] write tests for the edge cases: an empty period, a period holding only marker rows, a period
      whose first row is also its minimum
- [ ] run tests — must pass before Task 6

### Task 6: Write rows to PostgreSQL

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/PartitionScriptTests.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj`

- [ ] `PartitionScript` builds the `CREATE TABLE tpYYYYmMMdDD PARTITION OF trends FOR VALUES FROM ... TO ...`
      statement for a given day, named per `docs/architecture/scada-archive.md:57`
- [ ] `ArchiveWriter` **connects as `scada_writer`** and applies `sql/semiplot_dev.sql` itself, the
      way the SCADA creates its own tables. This is what puts SemiBase's default-privileges chain on
      a daily-tested path rather than leaving it to commissioning day
- [ ] the schema script is an `EmbeddedResource` of the seeder project, not a file read from the
      repository root — a console binary running out of `Artifacts/bin/` and a test assembly running
      out of its own output directory have no path to `sql/` at runtime
- [ ] creates a partition per covered day, then writes every row through `COPY` into the partitioned
      parent `public.trends`
- [ ] the writer refuses to run when `public.trends` already exists, and issues no `DROP DATABASE`
- [ ] `TagCatalogWriter`, active only when `--admin-connection` is set, upserts one `semiplot_tags`
      row per seeded pen from the pen catalogue — id, name, group, color, line style — per the
      *Tag catalogue* paragraph; the table itself is created by `semibase create`, never here
- [ ] `Program` wires options to generator to writer and reports per-layer row counts and the tag
      count on success
- [ ] write tests for `PartitionScript` without a database: the name for a known date, the bounds for
      a known date, a day at a month boundary, a day at a year boundary, and a run whose exclusive
      `--end` falls exactly on midnight — which must not create a partition for the following day
- [ ] run tests — must pass before Task 7

### Task 7: Container fixture and availability policy

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Modify: `SemiPlot/Directory.Packages.props`

- [ ] add `Testcontainers.PostgreSql` 4.13.0 to central package management and reference it
- [ ] `PostgresContainerFixture` as a collection fixture, one per test run: when `SEMIPLOT_TEST_PG`
      is set it uses that server, otherwise it starts `postgres:17-alpine`; the image tag comes from
      `SEMIPLOT_PG_IMAGE` with that default
- [ ] two runtimes are required and each is discovered the same way: a container runtime, and the
      `semibase` binary found through `SEMIBASE_EXE` or on `PATH` — acquired as the `v0.1.0`
      release binary from `github.com/Semiteq/SemiBase`
- [ ] either being absent is captured as an unavailable reason rather than thrown; gated tests call
      a guard that issues `Assert.Skip` with that reason, except under `SEMIPLOT_REQUIRE_DB=1` where
      it fails instead
- [ ] write tests: the guard skips with a stated reason when a runtime is missing, and throws under
      `SEMIPLOT_REQUIRE_DB=1`
- [ ] run the project with Docker stopped and `SEMIPLOT_TEST_PG` unset — every gated test reports as
      skipped with a reason, none passed and none failed; repeat with `SEMIPLOT_REQUIRE_DB=1` and
      confirm it fails instead
- [ ] run tests — must pass before Task 8

### Task 8: Provision the template and clone it per test class

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabase.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`

- [ ] between container start and seeding, run `semibase create` against it with the superuser the
      container already has. On the container path the fixture passes **fixed dummy role passwords
      of its own** — the container is ephemeral, and a developer must not need environment
      variables to run the suite; `SEMIBASE_*_PASSWORD` is read only on the `SEMIPLOT_TEST_PG`
      path. This creates the archive database, `scada_writer`, `semiplot_reader`, the grants, the
      default-privileges chain and `semiplot_tags` — the same code path production takes
- [ ] the fixture never defines those roles or that DDL itself. A second definition here would become
      the thing exercised daily while `semibase` decayed into a commissioning-day tool, which is the
      failure both repositories are arranged to avoid
- [ ] seed the template through `ArchiveWriter` as `scada_writer` at the standard slice — 1 day,
      8 pens, seed 1, fixed `--end` — so the default-privileges chain is exercised on every run;
      pass the superuser connection as `--admin-connection` so `semiplot_tags` carries the seeded
      pens and the catalog-reading slices develop against named pens
- [ ] `ArchiveDatabase` clones the template per test class with `CREATE DATABASE ... TEMPLATE ...`,
      and offers an empty database from `template0` with explicit `ENCODING 'UTF8'` for tests that
      need their own shape — the server's own locale must not leak into a test database
- [ ] the template name carries a discriminator over the schema and the generator version; a stale
      template is dropped and rebuilt, since on the `SEMIPLOT_TEST_PG` path a persistent server would
      otherwise serve last week's seed to this week's code
- [ ] on the `SEMIPLOT_TEST_PG` path the target must be a semibase-provisioned server and the fixture
      re-runs `create` against it, which is idempotent; document that this needs a superuser password
- [ ] serialise database creation behind a semaphore, since xunit v3 runs collections in parallel and
      concurrent `CREATE DATABASE ... TEMPLATE` can fail a connection check
- [ ] disposal calls `NpgsqlConnection.ClearPool` for its connection string before
      `DROP DATABASE ... WITH (FORCE)`, since a pooled connection otherwise refuses the drop
- [ ] write tests: a cloned database carries the seeded rows; an empty database carries none; the
      database is gone after disposal
- [ ] run tests — must pass before Task 9

### Task 9: Gated assertions against a seeded archive

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SeededArchiveTests.cs`

- [ ] every read in this task connects **as `semiplot_reader`**, never as the superuser, so a broken
      grant fails a test today rather than on commissioning day
- [ ] per-layer row counts read back equal the generator's counts
- [ ] `tpdefault` is empty after a seed — a non-empty default partition is the documented fault
      signal for a missing daily partition
- [ ] the primary key rejects a duplicate `(id, l, t)`, inside a transaction rolled back in `finally`
      so the shared cloned database stays clean
- [ ] `semiplot_reader` holds `SELECT` and does not hold `INSERT` on `trends`
- [ ] `semiplot_tags` read back as `semiplot_reader` holds one row per seeded pen, ids and names
      matching the seeder's pen catalogue
- [ ] the seeder refuses a second run against a database that already has `public.trends`, exits
      non-zero, and leaves row counts unchanged — the "never destroys" guarantee is asserted three
      times in this plan and verified nowhere else
- [ ] note in this task that `semibase create` sets `statement_timeout = 30 s` and
      `idle_in_transaction_session_timeout = 60 s` on `semiplot_reader`, so a slow query here fails
      with `57014` rather than hanging. That is production parity, not a test defect
- [ ] run tests — must pass before Task 10

### Task 10: Confront the thinning rule with real rows

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Fixtures/real-archive-rows.csv`
- Create: `SemiPlot/SemiPlot.Tests.Data/Fixtures/RealArchiveFixtureTests.cs`
- Modify: `sql/README.md`

- [ ] restore the customer dump into a scratch database with `pg_restore`, select a representative
      row set, write it to CSV, and drop the scratch database
- [ ] the set covers: an anchor pair, a steady stretch, a `32`/`16` marker pair, and all four layers
      for one identifier over at least one full minute
- [ ] choose that minute from inside a continuous run and away from the dump's newest edge — the
      coarse layers lag the raw layer, so the last minutes hold no `l = 1` rows at all
- [ ] **anonymise before committing**: replace the archive identifier with a synthetic one and shift
      every timestamp by a fixed offset onto an epoch-relative base, keeping all intervals exact.
      Values and quality codes are kept as they are. What the fixture tests is which rows survive
      each period and how far apart they sit, and both survive this transformation untouched
- [ ] record the extraction and anonymisation commands in `sql/README.md`, and record the offset, so
      the fixture can be regenerated and a future reader knows the timestamps are shifted
- [ ] the anonymised CSV is committed and read by tests with no database; the raw extract never is
- [ ] **assert** what the archive document records as measured: every real `l = 1` row equals some
      real `l = 0` row byte for byte, both extremes of the minute are present in `l = 1`, and marker
      rows appear in every layer
- [ ] **report, do not assert**, the full comparison of `LayerThinner`'s output against the real
      `l = 1` set. Exact set equality is not a safe gate: the identity of the two non-extreme points
      is recorded `UNVERIFIED` at `docs/architecture/scada-archive.md:124`, calendar versus
      flush-window alignment is open at `:251`, and a minute holding a marker pair can carry six
      `l = 1` rows against a stated budget of four. Write the diff into this task as a finding
- [ ] assert the pair-local timing invariant on the real raw rows, with its three exceptions
- [ ] record in this task what the fixture cannot cover: hour and day layers across their own
      periods, calendar versus flush-window alignment, and bad-quality rows
- [ ] run tests — must pass before Task 11

### Task 11: Add the data-tests CI job

**Files:**
- Modify: `.github/workflows/ci.yml`

- [ ] second job `data-tests` on `ubuntu-latest` alongside the existing Windows job: checkout,
      .NET from `global.json`, download the `semibase_0.1.0_linux_amd64` release binary
      (`gh release download v0.1.0 --repo Semiteq/SemiBase`), mark it executable and expose it via
      `SEMIBASE_EXE` — no Go toolchain on the runner — then
      `dotnet test SemiPlot/SemiPlot.Tests.Data`
- [ ] job env: `SEMIPLOT_REQUIRE_DB=1` — on CI an unavailable runtime is a failure, never a silent
      skip — plus fixed dummy `SEMIBASE_*_PASSWORD` values
- [ ] the Windows job is unchanged and does not set `SEMIPLOT_REQUIRE_DB`: Windows runners cannot
      run Linux containers, so gated tests skip there by design
- [ ] acceptance for this task is the `data-tests` job green on the pull request

### Task 12: Verify acceptance criteria

- [ ] every check in Acceptance Evidence runs and produces the stated result
- [ ] `dotnet test SemiPlot.slnx` — zero failures across both test projects
- [ ] `git diff` shows no change under `SemiPlot/SemiPlot.Tests/`
- [ ] gated tests against a stopped Docker report skips only; with `SEMIPLOT_REQUIRE_DB=1` they fail
- [ ] seed a database by hand and confirm `tpdefault` is empty and per-layer counts match
- [ ] `dotnet format SemiPlot.slnx` reports no changes
- [ ] confirm no file outside `SemiPlot.Tools.ArchiveSeeder`, `SemiPlot.Tests.Data`, `sql/`,
      `SemiPlot.slnx`, `.github/workflows/ci.yml` and the two shared props files was modified

### Task 13: Update documentation

- [ ] reduce `docs/architecture/postgres-instance.md` to SemiPlot's consumer contract and
      cross-reference SemiBase for the rest — the configuration deltas, the provisioning order and
      the role definitions are owned there now, and the local provisioning order is not merely
      duplicated but wrong: it creates the reader after the writer's first run
- [ ] the roadmap was amended ahead of execution (2026-08-14): two solution projects instead of a
      standalone script, gated-harness ownership here, `semiplot_tags` populated by the seeder, the
      no-stub-fallback composition slice, and the live-demo-and-stub-retirement slice — confirm the
      amended text still matches what was actually built, and update it where reality diverged
- [ ] fix the coarse-row arithmetic in `docs/architecture/scada-archive.md:227` and
      `docs/architecture/postgres-instance.md`: "roughly 1465 rows per variable per day" is one
      point per period, inconsistent with the four-per-period budget stated everywhere else — the
      four-per-period ceiling is about 5860
- [ ] replace the one-test-project rule in `CLAUDE.md` with the two-project rule, phrased as the
      current state plus its exit path: a Windows Avalonia project on xunit v2, and a `net10.0`
      project on xunit v3 for everything below the UI, unified when the Avalonia 12 bump happens
- [ ] verify against the NuGet nuspecs, then correct the stale premise in `CLAUDE.md` that the
      xunit-v3 unification is blocked by ScottPlot.Avalonia lacking an Avalonia 12 build — do not
      write the version numbers in without checking them at execution time
- [ ] document `SEMIPLOT_TEST_PG`, `SEMIPLOT_REQUIRE_DB`, `SEMIPLOT_PG_IMAGE`, `SEMIBASE_EXE` and
      the `SEMIBASE_*_PASSWORD` variables in `CLAUDE.md`
- [ ] add both new projects to `readme.md` if it lists the solution's projects
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Manual verification**

- Seed a database, point the running application at it once the composition slice lands, and confirm
  the trends look like process data rather than noise: visible steps, steady stretches, and breaks
  that render as broken lines.
- Tune the waveform parameters by eye if the generated signal does not resemble the customer dump.
  This is the one judgement no test makes.

**External systems**

- Gated tests need a container runtime and the `semibase` binary. A machine missing either runs the
  suite with those tests skipped. Podman and Rancher Desktop are supported alternatives to Docker
  Desktop if its licence becomes a problem.
- The SemiBase pin is `v0.1.0`. Bump it deliberately: a moving `@latest` would let a change in
  another repository fail this suite with no version to blame.

**The tests run what a site runs.** SemiBase installs vanilla PostgreSQL 17 and declares 14 the
minimum supported, so pinning `postgres:17-alpine` matches production exactly rather than
approximating it. One gap is accepted knowingly: a regression that breaks only a floor-14
installation would not be caught. Adding `14-alpine` as a second value of `SEMIPLOT_PG_IMAGE` is the
remedy if the floor stays supported and any site is left on it. PostgreSQL 14 reaches end of life in
November 2026, which is the argument for raising the floor in SemiBase rather than testing it here.

**Live demo.** The bench this slice builds is static by design — template plus clones. The live
demo writer (`--follow`: raw rows in real time, coarse layers flushed at period close, next-day
partition pre-created, `q = 32`/`q = 16` across stop and start) and the `seed-demo.ps1` script
belong to the roadmap's live-demo-and-stub-retirement slice and reuse this slice's generator and
thinner unchanged.

**SemiBase version bumps.** The pin is the `v0.1.0` release with published binaries. Bump it
deliberately, updating the binary name in the CI job and the fixture in one commit.

**Confirming the thinning rule**

Calendar-aligned periods are an assumption. The experiment that settles it is recorded at the end of
`docs/architecture/scada-archive.md` and runs when a stand becomes available. If it refutes the rule,
`LayerThinner`'s period-boundary method is the single place that changes. Until then, the comparison
recorded in Task 10 against real `l = 1` rows is the strongest evidence available.

**Remaining slices**

After this slice the roadmap continues with: postgres-provider-scaffold, postgres-catalog-and-extent,
postgres-history-read, postgres-bucketed-read, postgres-gap-reconstruction, postgres-realtime-poll,
postgres-startup-and-composition, live-demo-and-stub-retirement.
