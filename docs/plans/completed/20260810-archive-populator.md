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

- `#database-objects` the `trends` DDL: `id integer`, `l smallint`, `t timestamp(3) without time zone`,
  `v double precision`, `q integer`, `PARTITION BY RANGE (t)`, `PRIMARY KEY (id, l, t)` named `tpk`.
- `#database-objects` partitions are named `tpYYYYmMMdDD` with day bounds, plus a `tpdefault` catch-all.
- `#layers` coarse layers hold verbatim copies of raw rows — same timestamp, value and quality — up
  to four per period, selected by magnitude, strictly nested `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0`.
- `#quality-and-gaps` quality codes: `0` ordinary, `16` first sample after a break, `32` last before a break.
- `#quality-and-gaps` marker rows are copied into every layer unchanged.
- `#write-behavior` change-based archiving writes two rows per change — the previous value at the last poll
  tick before the change, then the new value at the change tick, one poll interval apart.
- `#write-behavior` row count scales with the number of changes, not with elapsed time.
- `#reader-hazards` reader hazards, and the rule that a non-empty `tpdefault` is a fault signal.
- `#not-established` the measured dump spans two hours with twelve restarts, so `l=2` and `l=3` were never
  exercised across their own periods.

**Dependencies identified**

New entries in `SemiPlot/Directory.Packages.props`, versions verified 2026-08-10:

- `Npgsql` 10.0.3 — the only new runtime dependency.
- `Testcontainers.PostgreSql` 4.14.0 — starts the PostgreSQL the gated tests talk to.
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
(`docs/architecture/scada-archive.md#write-behavior`). Scanning a 100 ms grid to look for changes would cost
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
`docs/architecture/scada-archive.md#write-behavior` reads `13:50:44.113 → 44.213 → 46.337 → 46.437`. Each
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
the new value at the change instant (`#write-behavior`). The pre-anchor is emitted **only when the pen's
last written row is older than one poll interval**. Without that condition a ramp or spike — where
the value changes every tick — makes the pre-anchor of one change collide with the change row of the
previous one, producing a duplicate `(id, l, t)` that `tpk` rejects. The vendor's sample shows pairs
because the value was steady between changes; during a ramp the archive writes one row per tick.

**Breaks.** A break emits the last sample before it with `q = 32` and the first sample after it with
`q = 16`, with no rows in the interval between (`#quality-and-gaps`). Break duration is a generator parameter.
Both marker rows carry real values.

**Coarse layers.** For each layer period — minute, hour, day — group the raw rows by calendar-aligned
period and take first, last, minimum and maximum, deduplicated when they coincide. Rows are copied
verbatim: same timestamp, same value, same quality (`#layers`). Every marker row is copied into
every layer regardless of selection (`#quality-and-gaps`). Nesting follows automatically, since a day's extremum
is also its hour's. Calendar alignment is a documented assumption, not a vendor statement —
`docs/architecture/scada-archive.md#not-established` records the question as open — so the period boundary is
computed in one place that the alternative could later replace.

**Partitions.** The seeder creates `tpYYYYmMMdDD` partitions for every day the run covers before
writing (`#database-objects`), because a missing partition sends rows to `tpdefault`, which the later slices treat
as a fault signal (`#reader-hazards`). `COPY` into the partitioned parent routes rows to the right partition
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
5 860 per pen per day — `docs/architecture/scada-archive.md#retention` quotes 1465, which is 1440 + 24 + 1,
one point per period rather than four, and is inconsistent with the four-per-period budget stated
everywhere else in that document. The fixture's standard slice — 1 day, 8 pens, seed 1 — is
therefore about 276 000 raw rows and up to 47 000 coarse.

**What the bench cannot reproduce.** Stated here so no later slice mistakes bench coverage for real
coverage. The customer dump spans two hours with twelve restarts (`#not-established`), so hour- and day-layer
thinning across their own periods is confirmed by no real data. Calendar versus flush-window
alignment stays open. And the bench emits only `q ∈ {0, 16, 32}`: no bad-quality code was observed in
the dump, so inventing one would be its own fiction — the "row present, point discarded" state in the
three-state table at `#quality-and-gaps` is therefore unbenched, and a slice that needs it must say so.

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

- [x] record the baseline at the branch point: run `dotnet test SemiPlot.slnx`, write the passing
      count and the commit into this task as a dated measurement
- [x] run `pg_restore --schema-only` from `C:/Program Files/PostgreSQL/14/bin/` against the customer
      dump into a scratch file, and record the exact command in `sql/README.md`
- [x] write `sql/semiplot_dev.sql` from that output: the `trends` table with its five columns and
      `PARTITION BY RANGE (t)`, the `tpk` primary key, and the `tpdefault` catch-all partition;
      strip ownership, tablespace and role statements
- [x] confirm the result matches `docs/architecture/scada-archive.md#database-objects` column for column,
      including `timestamp(3) without time zone` and the `smallint` layer
- [x] exclude `messages` — no slice in this roadmap reads it for data
- [x] no tests in this task: it produces a data file, and Task 7 is what executes it

**Baseline, measured 2026-08-14** at commit `93ccd10` on branch `archive-populator`:
`dotnet test SemiPlot.slnx` reports **250 passed, 0 failed, 0 skipped** in 960 ms, from the one
existing test project `SemiPlot.Tests`. Task 12 compares against this count.

**What the dump held beyond `trends`.** The schema-only output also carries `public.messages` with
partitions `mp2026m08d04..06` and `mpdefault`, the matching `trends` partitions `tp2026m08d04..06`,
and `public.realtest_withtimer` — a table from the customer's own testing, not part of the archive.
All are excluded; `sql/README.md` tabulates every removal. The `trends` definition matches
`docs/architecture/scada-archive.md#database-objects` column for column, `DEFAULT` clauses included.

**Partitions are written as `PARTITION OF`, not `CREATE` plus `ATTACH`.** `pg_dump` renders every
partition as a standalone table with its own `_pkey`, then attaches both table and index. The
script uses `CREATE TABLE public.tpdefault PARTITION OF public.trends DEFAULT`, which is the same
object in one statement and inherits `tpk` on its own — and is the form Task 6's `PartitionScript`
builds for day partitions.

⚠️ The script was not executed against a server in this task. The local PostgreSQL 14 on port 15432
refused password authentication for `postgres` and no credentials are on this machine. Task 7 is
what executes it, as this task's last item already states.

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

- [x] console project referencing `SemiPlot.Core` only — no reference to `SemiPlot.DataSource.Stub`;
      declare neither `TargetFramework` nor `IsPackable`, both of which
      `SemiPlot/Directory.Build.props:5,9` already set
- [x] copy `SyntheticValueWalk`, `SyntheticPenCatalog` and `SyntheticPen` verbatim from the stub
      into the seeder namespace, per *Copied rather than referenced* — the seeder owns them from
      here on; the stub's copies are untouched
- [x] test project on plain `net10.0` with `xunit.v3` 3.2.2, `Microsoft.NET.Test.Sdk` and
      `xunit.runner.visualstudio`, referencing `SemiPlot.Core` and the seeder — never the UI, never
      an Avalonia package
- [x] add `Npgsql` 10.0.3 and `xunit.v3` 3.2.2 to `SemiPlot/Directory.Packages.props`
- [x] `SeederOptions` record with the parameters listed under Technical Details (including the
      optional `--admin-connection`), plus a parser that returns a `Result` rather than throwing,
      and defaults for every optional parameter except `--end`
- [x] `Program` parses arguments, prints usage on a parse failure, and exits non-zero
- [x] register both projects in `SemiPlot.slnx`
- [x] write tests for the parser: defaults applied, every parameter accepted, unknown argument
      rejected, non-numeric value rejected, missing connection rejected, missing `--end` rejected
- [x] run tests — must pass before Task 3

**Defaults chosen, 2026-08-14.** The plan names the parameters but not their values, so:
`--days 1`, `--pens 8` and `--seed 1` come from the standard slice; `--change-seconds 5` from the
Volume paragraph; `--break-count 4` is new — a day of process work with a few restarts, enough that
several minute periods carry a `32`/`16` pair for Task 5 to thin. `--admin-connection` defaults to
unset, so a plain run writes `trends` only.

**The parser rejects more than the six listed cases.** A repeated option, a positional argument, an
option with no value, `--days`/`--pens` below 1, `--pens` above the catalogue's 50, non-positive
`--change-seconds` and a negative `--break-count` all fail. `--end` is also rejected when it carries
a time zone: `DateTime.TryParse` turns `2026-01-02T00:00:00Z` into a `Local` value, and the column is
`timestamp(3) without time zone`, so an offset-bearing bound would be silently reinterpreted rather
than refused. Numeric failures are merged, so one run reports all of them.

**Npgsql is versioned but not yet referenced.** The `PackageVersion` entry is in
`Directory.Packages.props` as this task requires; the `PackageReference` lands in Task 6, which is
where the seeder first opens a connection. `Program.Report` currently prints the parsed options and
exits 0 — Task 6 replaces that body with the generator-to-writer wiring.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds, `dotnet test SemiPlot.slnx` reports
**26 passed** in the new `SemiPlot.Tests.Data` and **250 passed, 0 failed** in `SemiPlot.Tests`,
matching the Task 1 baseline. `dotnet format SemiPlot.slnx --verify-no-changes` is clean.
xunit v3 and xunit v2 coexist in one solution without any runner setting: `dotnet test` runs each
project in its own process.

### Task 3: Generate raw-layer rows with anchor pairs

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveRow.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeededRandom.cs` ➕
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/PenTrace.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/RawLayerGeneratorTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/ArchiveRowTests.cs` ➕

- [x] `ArchiveRow` record: identifier, layer, naive-local timestamp truncated to whole milliseconds,
      value, quality
- [x] `RawLayerGenerator` walks each pen as a sequence of segments — idle, step, ramp, spike — with
      durations drawn from the seed
- [x] the level for each segment comes from the seeder's own `SyntheticValueWalk.Value` called with
      the **segment index**, scaled to the pen's range from the seeder's own `SyntheticPenCatalog`;
      the segment kind supplies the trajectory to that level, per the table in Technical Details
- [x] `--pens N` selects round-robin across the catalogue's five groups, not the first N
- [x] emit the pre-anchor exactly one poll interval before a change, and only when the pen's last row
      is older than one poll interval — a ramp writes one row per tick with no pre-anchors
- [x] change instants sit on a per-pen local grid of at least one poll interval; segment boundaries
      otherwise fall at arbitrary millisecond offsets, and no global lattice is imposed
- [x] write tests: identical seeds produce identical rows; every change row that is not the run's
      first is preceded by a row exactly one poll interval earlier carrying the prior value;
      timestamps are strictly ascending per pen with no duplicate `(id, l, t)` after millisecond
      truncation; a low change rate leaves stretches longer than a minute with no rows
- [x] write a golden test pinning a hash of the standard slice (1 day, 8 pens, seed 1), so an
      accidental edit anywhere in the seeder's generation code cannot silently change the bench for
      all later slices — a deliberate waveform change updates the hash in the same commit
- [x] write a test that the standard slice spans more than one group and more than one value range
- [x] write tests for the edge cases: zero days rejected, a single pen, an idle segment emitting no
      rows, and a ramp changing every tick — one row per tick, no duplicates, no pre-anchors
- [x] run tests — must pass before Task 4

**Two files beyond the list, 2026-08-14.** `SeededRandom.cs` holds a SplitMix64 draw written out
rather than taken from `System.Random`: the slice is pinned by a golden hash, so a runtime that
changed its own generator would change the archive eight later slices develop against.
`PenTrace.cs` holds one pen's write cursor and is the single place the anchor-pair rule lives, which
keeps `RawLayerGenerator` inside the 300-line and 50-line limits of `CLAUDE.md`. Both are `internal`
or seeder-local; nothing outside the seeder sees them. `ArchiveRowTests.cs` is a fourth extra file —
millisecond truncation is the first checkbox here and deserved its own assertions, including that
`with { Timestamp = … }` truncates too.

**A change row is identified by its value, not by a flag.** The row after a pre-anchor carries a
different value; the pre-anchor carries the same value as the row before it. So the pair-local
invariant is testable without the generator marking anything: *every row whose value differs from its
predecessor sits exactly one poll interval after it*. That statement covers the ramp and spike cases
without an exception — during a ramp the predecessor is already the anchor — and leaves only the
run's first row exempt in this task. The `q = 16` exception arrives with Task 4.

**Segment mix chosen to hit the Volume paragraph.** Idle 0.47, step 0.40, ramp 0.05, spike 0.08, with
waits drawn exponentially around `--change-seconds` and capped at eight times it. Ramps run 0.4 to
1.5 s and spikes 2 to 4 excursion ticks plus a return, which is what keeps ramps from dominating the
row count. Measured for the standard slice: **234 149 raw rows**, about 29 300 per pen per day
against the paragraph's estimate of 34 500 — the same order, low because ramps are deliberately rare.

**The golden digest hashes values at six decimals.** `Math.Sin` may differ by one unit in the last
place between a Windows and a Linux runner, and Task 11 runs this project on `ubuntu-latest`. Six
decimals survive that and still fail on any real waveform change. Digest for 1 day, 8 pens, seed 1,
`--end 2026-01-02T00:00:00`: `cd5cdd6d3975411ff520cb63aec188774e385c9fefbc1d2d0bd09aae5a1166b0`.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings,
`dotnet test SemiPlot.slnx` reports **48 passed** in `SemiPlot.Tests.Data` and **250 passed,
0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean.

### Task 4: Insert breaks marked with the vendor's quality codes

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/BreakGenerationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveRow.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/RawLayerGeneratorTests.cs` ➕

- [x] `BreakPlan` places the requested number of breaks across the run from the seed, each with a
      duration long enough to span more than one minute period
- [x] the last row before a break carries `q = 32`, the first row after carries `q = 16`, and no rows
      exist in between, per `docs/architecture/scada-archive.md#quality-and-gaps`
- [x] breaks apply to all pens at the same instants, matching a project stop and start
- [x] both marker rows carry real values, not sentinels
- [x] write tests: markers come in ordered `32` then `16` pairs; no row falls inside a break; a break
      spanning several minutes leaves those periods empty; a run with zero breaks has no marker rows
- [x] run tests — must pass before Task 5

**Placement, 2026-08-14.** One break per equal slot of the span, so `--break-count` breaks are spread
across it and two of them can never meet. Inside its slot a break is drawn 3 to 10 minutes long — three
minutes is the shortest that always leaves a whole calendar minute with no rows, which is the empty
period Task 5's thinner has to survive — and offset so at least 5 minutes of archiving remains on
either side. A span with no room for the requested count is rejected: at 1 day the ceiling is 72
breaks. `BreakPlan.Runs` is the complement, one more window than there are breaks, and is what the
generator walks.

**The resume row is a change row.** A run that resumes after a break opens on a level of its own, drawn
from the walk with the next segment index, because the plant kept moving while archiving was stopped.
That is what makes the `q = 16` row differ in value from the `q = 32` row before it — and therefore
what makes it the second exception to the pair-local invariant, since its pre-anchor would fall inside
the gap this task forbids. `RawLayerGeneratorTests.EveryChangeRowFollowsItsPredecessorByExactlyOnePollInterval`
was widened to that exception, not weakened: it still demands the 100 ms predecessor of every other
change row.

**A single-row run between two breaks gets a second row.** Such a run would have to carry `32` and
`16` on one row, and the archive has no code for both — the bench emits only `q ∈ {0, 16, 32}`. The
resume row keeps `16` and the poll tick 100 ms after it, which the SCADA certainly also recorded, is
appended and marked `32`. It is what keeps the marker sequence a strict `32`, `16` alternation for
every pen, and it is the one row in the archive that did not come from the value walk.

**Correction.** This was first written up as unreachable at any realistic parameter. It is not: many
short runs and a slow change rate reach it easily, and `--change-seconds 120 --break-count 60` over
one day produces it three times. `RawLayerGeneratorTests.ASingleRowRunBetweenTwoBreaksGetsASynthesisedStopRow`
pins that count the way the golden digest pins the waveform.

**The golden digest changed, as expected.** Breaks remove rows and shift the segment stream, so the
standard slice is now **229 862 raw rows** against 234 149 before, digest
`59a88008953845d205ed7d61da1c543833d9bf7666a85d8d2774905986e25f78`. Both constants were updated in the
same commit as the change that moved them, which is the rule the golden test exists to enforce.
`EveryRowCarriesTheRawLayerAndOrdinaryQuality` became
`EveryRowCarriesTheRawLayerAndOneOfTheThreeQualityCodes` for the same reason.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **62 passed** in `SemiPlot.Tests.Data` (14 of them new) and **250 passed,
0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean.

### Task 5: Fill the coarse layers by the vendor's rule

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LayerThinner.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/LayerThinnerTests.cs`

- [x] group raw rows per pen by calendar-aligned minute, hour and day
- [x] take first, last, minimum and maximum of each period, deduplicated when they coincide, copying
      timestamp, value and quality verbatim per `docs/architecture/scada-archive.md#layers`
- [x] copy every marker row into every layer regardless of selection, per `#quality-and-gaps`
- [x] keep the period-boundary computation in one method, since calendar alignment is an assumption
      recorded as open at `#not-established`
- [x] write tests: no period holds more than four non-marker rows; each period's minimum and maximum
      are present; layers nest as `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0`; every coarse row matches a raw row exactly;
      a period with one raw row yields one coarse row, not four
- [x] write tests for the edge cases: an empty period, a period holding only marker rows, a period
      whose first row is also its minimum
- [x] run tests — must pass before Task 6

**Every layer is computed against the raw rows, never against the layer below it, 2026-08-14.** That
is what makes nesting fall out instead of being forced: a day's extremum is also the extremum of the
hour and the minute holding it, and the day's first and last rows are the first and last rows of their
own minute. Nothing in `LayerThinner` special-cases nesting; `LayersNestFromTheDayDownToTheRawRows`
asserts `Assert.ProperSubset` three times over the generated slice.

**Ties on value resolve to the earliest row, which is what keeps nesting exact.** `MinBy`/`MaxBy` keep
the first minimum of an ascending-ordered period, so a value repeated inside a day selects the same
row at the day, hour and minute layers. Had the tie broken differently per layer, the day's minimum
row could be absent from its minute. `RepeatedExtremesResolveToTheEarliestRow` pins it.

**Markers are additional to the four, not part of them.** A period bounding a break can therefore hold
six rows against a stated budget of four, which is why the budget assertion counts rows with
`q = 0` only — the same reason Task 10 records exact set equality against real `l = 1` rows as unsafe
to gate on. `MarkerRowsAreKeptOnTopOfTheFourSelectedOnes` builds that six-row period by hand.

**`PeriodStart` is the single place calendar alignment lives.** It switches minute/hour/day to a tick
count and floors by modulo — `DateTime` ticks start at midnight of 0001-01-01, so all three align. The
helper that previously held the switch was folded into it: the experiment at
`docs/architecture/scada-archive.md#not-established` must have one method to replace, not two. Layer `0` and layer
`4` are rejected rather than silently treated as a period.

**The golden digest is untouched.** Task 3's hash covers raw rows only and this task adds no raw row;
`TheStandardSliceMatchesItsGoldenDigest` still reads
`59a88008953845d205ed7d61da1c543833d9bf7666a85d8d2774905986e25f78` at 229 862 rows.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **88 passed** in `SemiPlot.Tests.Data` (26 of them new) and **250 passed,
0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean.

### Task 6: Write rows to PostgreSQL

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/PartitionScriptTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/SchemaResourceTests.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj`

- [x] `PartitionScript` builds the `CREATE TABLE tpYYYYmMMdDD PARTITION OF trends FOR VALUES FROM ... TO ...`
      statement for a given day, named per `docs/architecture/scada-archive.md#database-objects`
- [x] `ArchiveWriter` **connects as `scada_writer`** and applies `sql/semiplot_dev.sql` itself, the
      way the SCADA creates its own tables. This is what puts SemiBase's default-privileges chain on
      a daily-tested path rather than leaving it to commissioning day
- [x] the schema script is an `EmbeddedResource` of the seeder project, not a file read from the
      repository root — a console binary running out of `Artifacts/bin/` and a test assembly running
      out of its own output directory have no path to `sql/` at runtime
- [x] creates a partition per covered day, then writes every row through `COPY` into the partitioned
      parent `public.trends`
- [x] the writer refuses to run when `public.trends` already exists, and issues no `DROP DATABASE`
- [x] `TagCatalogWriter`, active only when `--admin-connection` is set, upserts one `semiplot_tags`
      row per seeded pen from the pen catalogue — id, name, group, color, line style — per the
      *Tag catalogue* paragraph; the table itself is created by `semibase create`, never here
- [x] `Program` wires options to generator to writer and reports per-layer row counts and the tag
      count on success
- [x] write tests for `PartitionScript` without a database: the name for a known date, the bounds for
      a known date, a day at a month boundary, a day at a year boundary, and a run whose exclusive
      `--end` falls exactly on midnight — which must not create a partition for the following day
- [x] run tests — must pass before Task 7

**The embedded resource is asserted, not assumed, 2026-08-14.** `SchemaResourceTests.cs` is a file
beyond the list: it reads the resource stream through `ArchiveWriter.ReadSchemaScript()` and checks the
vendor's five columns, `PARTITION BY RANGE (t)`, the `tpk` constraint and the `tpdefault` partition are
in it, plus that the script creates those two tables and nothing else. A csproj item that stopped
matching would fail there rather than at the first database connection, which is the whole point of
having a test for a build item. The item is
`<EmbeddedResource Include="..\..\sql\semiplot_dev.sql" LogicalName="SemiPlot.Tools.ArchiveSeeder.semiplot_dev.sql"/>` —
the file lives outside the project directory, so the logical name has to be stated.

**Nothing in this task talked to a server.** The local PostgreSQL 14 on port 15432 refuses password
authentication and no credentials exist on this machine, so `ArchiveWriter` and `TagCatalogWriter` are
built and reviewed but first executed in Tasks 7 and 8. `PartitionScript` is the pure part and carries
the tests. One runtime check was possible and was run: the seeder against `127.0.0.1:1` reports the
per-layer counts, prints `Failed to connect to 127.0.0.1:1` and exits 1 — the connection failure
surfaces as a `Result` error rather than an unhandled exception.

**Date rendering is invariant everywhere.** The partition name comes from
`ToString("'tp'yyyy'm'MM'd'dd", CultureInfo.InvariantCulture)` and the range bounds from the same
culture. A machine running a non-Gregorian calendar would otherwise name the partition after a year the
archive never uses, and the bug would only appear on the one site that had it.

**`CoveredDays` is `[start.Date, endExclusive)`.** A day is covered when its midnight falls before the
exclusive end, so `--days 1 --end 2026-01-02T00:00:00` creates exactly `tp2026m01d01`, and an end one
millisecond later creates the second partition too. An end at or before the start throws rather than
returning nothing — a caller asking for an empty span is a bug, not a run with no partitions.

**`--admin-connection` is a separate connection, not a second role on the same one.** `TagCatalogWriter`
opens the admin string and upserts `id, name, group_name, color, line_style` — the columns
`docs/architecture/data-integration.md:72-74` reads back — leaving `unit` alone, since `SyntheticPen`
carries none. `ON CONFLICT (id) DO UPDATE` makes a re-run against a provisioned server idempotent,
which is what Task 8's template rebuild needs.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **115 passed** in `SemiPlot.Tests.Data` (27 of them new) and **250 passed,
0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean, and Rider's analyser reports no problem in any of the six files.

### Task 7: Container fixture and availability policy

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/TestEnvironment.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinary.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/DatabaseGate.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresServer.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabaseCollection.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/DatabaseGateTests.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseBinaryTests.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixtureTests.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj`
- Modify: `SemiPlot/Directory.Packages.props`

- [x] add `Testcontainers.PostgreSql` 4.14.0 to central package management and reference it
- [x] `PostgresContainerFixture` as a collection fixture, one per test run: when `SEMIPLOT_TEST_PG`
      is set it uses that server, otherwise it starts `postgres:17-alpine`; the image tag comes from
      `SEMIPLOT_PG_IMAGE` with that default
- [x] two runtimes are required and each is discovered the same way: a container runtime, and the
      `semibase` binary found through `SEMIBASE_EXE` or on `PATH` — acquired as the `v0.1.0`
      release binary from `github.com/Semiteq/SemiBase`
- [x] either being absent is captured as an unavailable reason rather than thrown; gated tests call
      a guard that issues `Assert.Skip` with that reason, except under `SEMIPLOT_REQUIRE_DB=1` where
      it fails instead
- [x] write tests: the guard skips with a stated reason when a runtime is missing, and throws under
      `SEMIPLOT_REQUIRE_DB=1`
- [x] run the project with Docker stopped and `SEMIPLOT_TEST_PG` unset — every gated test reports as
      skipped with a reason, none passed and none failed; repeat with `SEMIPLOT_REQUIRE_DB=1` and
      confirm it fails instead
- [x] run tests — must pass before Task 8

**The policy is four small classes, not one fixture, 2026-08-14.** `TestEnvironment` reads the five
variables and nothing else; `SemibaseBinary` resolves the binary through `SEMIBASE_EXE` or `PATH` and
returns a `Result<string>` like the rest of the seeder code; `DatabaseGate` is the guard every gated
test opens with; `PostgresServer` carries host, port and superuser apart from the connection string,
because `semibase create` takes them as separate flags. That split is what let the whole policy be
tested without a container: only three of the run's 129 tests need one.

**The fixture provisions nothing.** It starts or finds a server and resolves the binary; `semibase
create`, the template and the clones are Task 8. What it hands forward is
`Server.AdminConnectionString`, `Server.ConnectionStringFor(database, user, password)` and
`Server.SemibaseExecutable`.

**`InitializeAsync` never throws.** A collection fixture that threw would fail every test in the
collection with a stack trace rather than a reason, and under a developer's `SEMIPLOT_REQUIRE_DB`-free
run the correct outcome is a skip. Both runtimes are probed into `UnavailableReason`, and
`RequireAvailable()` — one line at the head of each gated test — turns it into a skip or a failure.
semibase is probed first because it is the cheap check: no container is started when it is missing.

**Verified by running, not by reasoning, 2026-08-14.** Four branches, `dotnet test
SemiPlot/SemiPlot.Tests.Data --filter "Category=Integration"` each time, 3 gated tests in total:

| Condition | Result |
| --- | --- |
| `SEMIBASE_EXE` unset, semibase not on `PATH` | 3 skipped, 0 passed, 0 failed — reason: `semibase was not found on PATH: download the v0.1.0 release binary…` |
| the same with `SEMIPLOT_REQUIRE_DB=1` | 3 failed, 0 passed, 0 skipped — `InvalidOperationException: SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails instead of skipping: …` |
| semibase present, `SEMIPLOT_PG_IMAGE` naming an unpullable image | 3 skipped — reason: `no container runtime started …: Docker API responded with status code='InternalServerError'…` |
| the same with `SEMIPLOT_REQUIRE_DB=1` | 3 failed, 0 passed, 0 skipped |
| `SEMIPLOT_TEST_PG=Host=127.0.0.1;Port=1;…` | 3 skipped — reason: `SEMIPLOT_TEST_PG names a server that refused a connection: Failed to connect to 127.0.0.1:1` |
| both runtimes present | 3 passed against a real container, with and without `SEMIPLOT_REQUIRE_DB=1` |

**A dead `DOCKER_HOST` is not a way to hide the runtime.** Testcontainers walks its endpoint providers
and takes the first one that answers, so `DOCKER_HOST=tcp://127.0.0.1:1` silently falls back to the
working named pipe and the tests pass. The container-start failure was therefore forced through
`SEMIPLOT_PG_IMAGE`, which reaches the identical catch. Stopping the Docker service is the only way to
exercise the literal wording of the acceptance check, and Task 12 is where that is done.

**The container path really started a container.** Setting `SEMIPLOT_PG_IMAGE=postgres:14-alpine`, an
image not present on the machine, left it in `docker images` after the run and the three gated tests
passed — so the fixture pulled, started and connected, and `SEMIPLOT_PG_IMAGE` is honoured.

**`Record.Exception` rethrows a dynamic skip.** xunit v3 marks `Assert.Skip` with a token in the
exception message and `Record.Exception` passes it through, so the first version of the gate's own
tests reported themselves as skipped instead of asserting. They catch the exception plainly now;
`TheSkipAndTheFailureAreDifferentOutcomes` pins that the two outcomes stay distinct types.

**`Testcontainers.PostgreSql` was first taken at 4.13.0, which brought `SSH.NET` 2025.1.0 and its
NU1903 restore warning.** The pin is 4.14.0, and restore emits no NU1903 for `SemiPlot.Tests.Data`
(measured 2026-08-17). The remaining NU1903 warnings are Avalonia's `Tmds.DBus.Protocol`, on
`SemiPlot.UI` and `SemiPlot.Tests` only.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **129 passed** in `SemiPlot.Tests.Data` (14 of them new, 3 of those gated) and
**250 passed, 0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format
SemiPlot.slnx --verify-no-changes` is clean, and Rider's analyser reports no problem in any of the
nine new files.

### Task 8: Provision the template and clone it per test class

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabase.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SemibaseProvisioner.cs` ➕
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveDatabaseTests.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresServer.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/TestEnvironment.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixtureTests.cs` ➕

- [x] between container start and seeding, run `semibase create` against it with the superuser the
      container already has. On the container path the fixture passes **fixed dummy role passwords
      of its own** — the container is ephemeral, and a developer must not need environment
      variables to run the suite; `SEMIBASE_*_PASSWORD` is read only on the `SEMIPLOT_TEST_PG`
      path. This creates the archive database, `scada_writer`, `semiplot_reader`, the grants, the
      default-privileges chain and `semiplot_tags` — the same code path production takes
- [x] the fixture never defines those roles or that DDL itself. A second definition here would become
      the thing exercised daily while `semibase` decayed into a commissioning-day tool, which is the
      failure both repositories are arranged to avoid
- [x] seed the template through `ArchiveWriter` as `scada_writer` at the standard slice — 1 day,
      8 pens, seed 1, fixed `--end` — so the default-privileges chain is exercised on every run;
      pass the superuser connection as `--admin-connection` so `semiplot_tags` carries the seeded
      pens and the catalog-reading slices develop against named pens
- [x] `ArchiveDatabase` clones the template per test class with `CREATE DATABASE ... TEMPLATE ...`,
      and offers an empty database from `template0` with explicit `ENCODING 'UTF8'` for tests that
      need their own shape — the server's own locale must not leak into a test database
- [x] the template name carries a discriminator over the schema and the generator version; a stale
      template is dropped and rebuilt, since on the `SEMIPLOT_TEST_PG` path a persistent server would
      otherwise serve last week's seed to this week's code
- [x] on the `SEMIPLOT_TEST_PG` path the target must be a semibase-provisioned server and the fixture
      re-runs `create` against it, which is idempotent; document that this needs a superuser password
- [x] serialise database creation behind a semaphore, since xunit v3 runs collections in parallel and
      concurrent `CREATE DATABASE ... TEMPLATE` can fail a connection check
- [x] disposal calls `NpgsqlConnection.ClearPool` for its connection string before
      `DROP DATABASE ... WITH (FORCE)`, since a pooled connection otherwise refuses the drop
- [x] write tests: a cloned database carries the seeded rows; an empty database carries none; the
      database is gone after disposal
- [x] run tests — must pass before Task 9

**`semibase create` v0.1.0 needed nothing hand-written, 2026-08-14.** Its flags are `--host`,
`--port`, `--database`, `--superuser` and `--expected-major`; the three role passwords come from
`SEMIBASE_SUPER_PASSWORD`, `SEMIBASE_WRITER_PASSWORD` and `SEMIBASE_READER_PASSWORD`. There is no
`--admin` role in this tag. The fixture passes the passwords through the child process environment
rather than through flags, so they never reach a process listing. `--database` is what makes the
template a normal `create` target: the archive database is simply named after the discriminator
instead of `scada_archive`. Nothing in this repository defines a role, a grant or a default privilege.

**Three classes rather than one fixture, matching Task 7's split.** `SemibaseProvisioner` runs the
binary and owns the role names and the password variable names; `ArchiveTemplate` owns the template's
name, its `semibase create` call and its seeding; `ArchiveDatabase` owns one database's lifetime.
`PostgresContainerFixture` keeps only the run-level state: the server, the template name and the
creation semaphore, and hands out `CloneTemplateAsync` / `CreateEmptyDatabaseAsync`.

**The discriminator is the seeder assembly's module version plus the schema script plus the slice.**
Deterministic builds make the module version a function of the seeder's own sources, and the schema
script is an embedded resource of that assembly, so either one moving moves the name — the script is
hashed as well, since the name is the only thing standing between a stale template and a false pass.
The sweep drops every `semiplot_bench_%` database whose name is not the current one. Clones are not
swept: a clone belongs to the run that created it and is dropped on disposal.

**The template is reused when it already carries `public.trends`.** That is what the discriminator
buys on the `SEMIPLOT_TEST_PG` path. `semibase create` still runs on every start — it is idempotent by
design and it is the check that the server really is provisioned — and only the seeding is skipped. A
database that exists but holds no archive is a crashed earlier run, and seeding it is the repair.

**A failed provisioning is an unavailable reason, not a throw.** `InitializeAsync` still never throws,
so `semibase create` failing reads as a skip with a stated cause on a developer machine and as a
failure under `SEMIPLOT_REQUIRE_DB=1`. One mechanism carries every reason a gated test cannot run.

**The `SEMIPLOT_TEST_PG` path now demands the two role passwords.** That server is real and its roles
already have passwords; inventing dummies would change them. Missing either variable is reported as an
unavailable reason naming both. The superuser password keeps coming from the connection string.

**Verified by running, not by reasoning, 2026-08-14.** Both runtimes present, `SEMIBASE_EXE` set:

| Path | Result |
| --- | --- |
| container, `postgres:17-alpine` | 6 gated tests passed; the clone holds **266 372** archive rows (229 862 raw + 36 510 coarse) and **8** `semiplot_tags` rows |
| `SEMIPLOT_TEST_PG` against a hand-started container, first run | 6 passed, template `semiplot_bench_1a4a232626bb3e15` created and seeded, 3.7 s wall |
| the same server, second run | 6 passed, 2.3 s wall — the template was reused and `create` re-ran without complaint |
| a planted `semiplot_bench_deadbeefdeadbeef` | dropped by the sweep; only the current template survived, and no `semiplot_clone_%` database was left behind |
| `SEMIPLOT_TEST_PG` set with no `SEMIBASE_WRITER_PASSWORD` | 6 skipped with the stated reason |
| `SEMIBASE_EXE` unset, semibase not on `PATH` | 6 skipped, 0 passed, 0 failed |

**Not exercised: an empty database that the writer can write to.** `EmptyAsync` creates the database
from `template0` and nothing else, so on PostgreSQL 15 and later `scada_writer` cannot create a table
in its `public` schema. A later task that needs that shape runs `semibase create` against the empty
database, the same way the template is built — it does not grow hand-written grants here.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **132 passed** in `SemiPlot.Tests.Data` (3 of them new, 6 gated in total) and
**250 passed, 0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format
SemiPlot.slnx --verify-no-changes` is clean, and Rider's analyser reports no problem in any of the new
or modified files.

### Task 9: Gated assertions against a seeded archive

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SeededArchiveTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/SeededArchive.cs` ➕

- [x] every read in this task connects **as `semiplot_reader`**, never as the superuser, so a broken
      grant fails a test today rather than on commissioning day
- [x] per-layer row counts read back equal the generator's counts
- [x] `tpdefault` is empty after a seed — a non-empty default partition is the documented fault
      signal for a missing daily partition
- [x] the primary key rejects a duplicate `(id, l, t)`, inside a transaction rolled back in `finally`
      so the shared cloned database stays clean
- [x] `semiplot_reader` holds `SELECT` and does not hold `INSERT` on `trends`
- [x] `semiplot_tags` read back as `semiplot_reader` holds one row per seeded pen, ids and names
      matching the seeder's pen catalogue
- [x] the seeder refuses a second run against a database that already has `public.trends`, exits
      non-zero, and leaves row counts unchanged — the "never destroys" guarantee is asserted three
      times in this plan and verified nowhere else
- [x] note in this task that `semibase create` sets `statement_timeout = 30 s` and
      `idle_in_transaction_session_timeout = 60 s` on `semiplot_reader`, so a slow query here fails
      with `57014` rather than hanging. That is production parity, not a test defect
- [x] run tests — must pass before Task 10

**Nothing in the grant chain was missing, 2026-08-14.** Every assertion in this class reads through
`semiplot_reader`, and each one passed on the first run against a `postgres:17-alpine` container
provisioned by `semibase create` — `trends`, the `tpdefault` partition and `semiplot_tags` are all
readable by the role, so SemiBase's default-privileges chain reaches the tables `scada_writer`
creates after provisioning. No privilege had to be added here, and none would have been: a missing
grant is a finding about the provisioning chain, not something this repository patches.

**The role is asserted, not assumed.** `TheReaderHoldsSelectAndNotInsert` reads `current_user` and
demands `semiplot_reader` before it checks anything, then uses the two-argument
`has_table_privilege`, which asks about the session's own role. A test that quietly connected as the
superuser would pass every count and prove nothing, so the identity of the reader is checked before
the privileges are.

**Two ways of asking the same question, both kept.** `has_table_privilege` is the grant as
PostgreSQL records it; `TheReaderIsRefusedAWrite` is the grant as a write attempt meets it, and the
`INSERT` fails with `42501`. A privilege can be recorded and then shadowed — by ownership, by a
row-level policy — so the catalogue answer alone is not the production behaviour.

**Both write attempts roll back in `finally`.** The clone is shared by every test in the class, so a
leaked row would corrupt the counts the other tests assert. The duplicate-key insert runs as
`scada_writer` — `semiplot_reader` would fail on the privilege before ever reaching the key, which
would test the wrong thing — and `ThePrimaryKeyRejectsADuplicateRow` re-reads the total afterwards,
so the rollback is asserted rather than trusted. Only the `SqlState` `23505` is pinned, not the
constraint name: a `PARTITION OF` child carries its own index name (`tp2026m01d01_pkey`), and `tpk`
is the parent's.

**One clone for the class, in `SeededArchive`.** A class fixture rather than `IAsyncLifetime` on the
test class, which xunit builds per test — eight clones at roughly 600 ms each, for a database no
test modifies. It takes `PostgresContainerFixture` as a constructor argument, which xunit v3 allows a
class fixture to do, and it skips cloning when the fixture reports an unavailable runtime so the
gate still turns that into a skip or a failure inside each test rather than a stack trace.

**The seeder's refusal is exercised through `Program.Main`.** The checkbox says "exits non-zero", and
`ArchiveWriter` returning a failed `Result` is not the same claim, so the test calls the entry point
with the clone's `scada_writer` connection string and asserts the exit code is 1 and the row count is
unchanged. `--pens 1` keeps the generation the refusal happens after short.

**The timeouts are what the checkbox says they are.** Measured on the container path through the
reader's own session: `SHOW statement_timeout` is `30s` and `SHOW idle_in_transaction_session_timeout`
is `1min` — PostgreSQL's own rendering of 60 s. `TheReaderCarriesTheProductionTimeouts` pins both, so
a `57014` in a later slice reads as production parity rather than as a broken test.

**Per-layer counts, generated and read back, 2026-08-14:** `l=0` 229 862, `l=1` 35 599, `l=2` 815,
`l=3` 96 — 266 372 rows, matching Task 8's total. The comparison regenerates the slice in the test
process, so it covers the generator, the `COPY` and the partition routing in one assertion.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, and with
`SEMIBASE_EXE` set and Docker running `dotnet test SemiPlot.slnx` reports **140 passed** in
`SemiPlot.Tests.Data` (8 of them new, 14 gated in total) and **250 passed, 0 failed** in
`SemiPlot.Tests`, matching the Task 1 baseline. With `SEMIBASE_EXE` unset and semibase not on `PATH`
the same 14 gated tests report **skipped, 0 passed, 0 failed**. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean, and Rider's analyser reports no problem in either new file.

### Task 10: Confront the thinning rule with real rows

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Fixtures/real-archive-rows.csv`
- Create: `SemiPlot/SemiPlot.Tests.Data/Fixtures/RealArchiveFixtureTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Fixtures/RealArchiveFixture.cs` ➕
- Modify: `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj` ➕
- Modify: `sql/README.md`

- [x] restore the customer dump into a scratch database with `pg_restore`, select a representative
      row set, write it to CSV, and drop the scratch database
- [x] the set covers: an anchor pair, a steady stretch, a `32`/`16` marker pair, and all four layers
      for one identifier over at least one full minute
- [x] choose that minute from inside a continuous run and away from the dump's newest edge — the
      coarse layers lag the raw layer, so the last minutes hold no `l = 1` rows at all
- [x] **anonymise before committing**: replace the archive identifier with a synthetic one and shift
      every timestamp by a fixed offset onto an epoch-relative base, keeping all intervals exact.
      Values and quality codes are kept as they are. What the fixture tests is which rows survive
      each period and how far apart they sit, and both survive this transformation untouched
- [x] record the extraction and anonymisation commands in `sql/README.md`, and record the offset, so
      the fixture can be regenerated and a future reader knows the timestamps are shifted
- [x] the anonymised CSV is committed and read by tests with no database; the raw extract never is
- [x] **assert** what the archive document records as measured: every real `l = 1` row equals some
      real `l = 0` row byte for byte, both extremes of the minute are present in `l = 1`, and marker
      rows appear in every layer
- [x] **report, do not assert**, the full comparison of `LayerThinner`'s output against the real
      `l = 1` set. Exact set equality is not a safe gate: the identity of the two non-extreme points
      is recorded `UNVERIFIED` at `docs/architecture/scada-archive.md#layers`, calendar versus
      flush-window alignment is open at `#not-established`, and a minute holding a marker pair can carry six
      `l = 1` rows against a stated budget of four. Write the diff into this task as a finding
- [x] assert the pair-local timing invariant on the real raw rows, with its three exceptions
- [x] record in this task what the fixture cannot cover: hour and day layers across their own
      periods, calendar versus flush-window alignment, and bad-quality rows
- [x] run tests — must pass before Task 11

**The extract, 2026-08-14.** The dump was restored into a throwaway `postgres:17-alpine` container —
the local PostgreSQL 14 still refuses password authentication — and the container was removed
afterwards. `sql/README.md` records the restore, the extraction query and the anonymisation. The
whole dump holds 106 raw rows and 170 coarse rows over two identifiers, `13:02:56.475` to
`15:06:34.653` of one day. The verbatim-copy claim was re-checked against all of it before extracting:
**170 of 170 coarse rows have a raw row with the same `(id, t, v, q)`**, which is the number
`docs/architecture/scada-archive.md#layers` quotes.

The committed slice is `13:48:00`–`13:56:00`, both identifiers, all four layers, 140 rows. It carries
the anchor pair quoted at `#write-behavior`, a steady stretch of 4 min 18 s with no rows, three `32`/`16`
marker pairs, and minute `13:55` in full: a marker pair, a burst of changes one poll interval apart,
and rows in all four layers. Identifiers `0` and `1` became `9001` and `9002`; every timestamp moved
back exactly **9713 days**, mapping `2026-08-05` onto `2000-01-01`. A whole-day offset keeps the
calendar minute, hour and day each row falls in, which is what the period grouping depends on.

**Finding: the vendor's tie-break runs the other way.** Reported, not asserted. Over the extract's
four non-empty minutes, `LayerThinner` produced 20 minute-layer rows against the vendor's 24, with 16
agreed:

| Side | Rows | Which |
| --- | --- | --- |
| both | 16 | all seven marker rows of each pen, plus `13:50:46.437`, the last row of its minute |
| only `LayerThinner` | 4 | `9001 13:50:44.213 v=522`, `9001 13:55:13.018 v=929`, `9002 13:50:44.213 v=975`, `9002 13:55:11.801 v=993` |
| only the vendor | 8 | `9001 13:50:44.113 v=0`, `9001 13:50:46.337 v=522`, `9001 13:55:10.764 v=0`, `9001 13:55:42.546 v=929`, and the same four instants for `9002` with `v=0`, `975`, `0`, `993` |

Every one of the twelve differing rows has the same cause, and it is not the selection rule but the
tie-break inside it. When a value repeats, `LayerThinner` keeps the **earliest** row carrying it
(`RepeatedExtremesResolveToTheEarliestRow`, Task 5) and the vendor keeps the **latest**. Minute
`13:55` of `9001` is the whole finding in one line: the maximum `929` sits at `13:55:13.018` and again
at `13:55:42.546`, we take the first, the vendor took the second. Same for the minimum `0` at
`13:55:08.369` and `13:55:10.764`, and same for both extremes in minute `13:50` of both pens.

Three consequences worth carrying forward:

1. **The rule itself is confirmed.** Both selections keep the same extreme *values* in every period —
   only the row carrying them differs — so `EveryMinuteKeepsItsExtremesInTheMinuteLayer` passes over
   real rows, and the envelope a later slice reads from `l = 1` is the right envelope either way.
2. **Half of the `UNVERIFIED` inference in *Layers* now has evidence.** The vendor kept
   `13:50:46.437 v=313`, the last row of its minute and neither an extreme nor a marker. So *last of
   the period* really is one of the two non-extreme points. *First of the period* remains untestable
   here: in every minute of the extract the first raw row is a marker and would be copied anyway.
3. **The vendor's choice is the physically better one on a step-shaped archive.** The later of two
   equal rows is the last poll tick holding that value — the corner where the step ends. Taking the
   earlier one loses the width of the step. This is an argument for changing `LayerThinner`, not a
   reason: the plan's instruction is that a disagreement is a discovery about the vendor's rule and
   not a defect. Nesting is **not** the reason to keep the earliest-wins tie-break: latest-wins nests
   identically, because if value `V` is the hour's extreme and its last occurrence in the hour is at
   `T`, then `T` is also the last occurrence of `V` inside its own minute. The real reason to defer is
   the open calendar-versus-flush-window question — which rows a period holds is undecided, so
   deciding which of them a tie keeps is premature. Both belong to whoever runs the controlled
   experiment in the *Not established* section of `docs/architecture/scada-archive.md`.

**Finding: a real 100 ms poll jitters, a generated one does not.** The pair-local invariant holds on
the real rows with its stated exceptions, but not to the millisecond. Of **34 change rows** — every
raw row whose value differs from its predecessor, minus each pen's first row and the `q = 16`
resume rows — **30 sit exactly 100 ms after their predecessor and 4 sit 104 to 109 ms after it**.
The four late ones are the same two instants in both pens, so the poll tick itself ran late rather
than one variable being handled differently. `EveryChangeRowFollowsItsPredecessorByOnePollInterval`
therefore asserts the span is at least one poll interval and at most one interval plus 10 ms of
jitter. The third exception in Technical Details — a change whose predecessor is younger than one
poll interval — never occurs in the extract: the vendor never wrote two rows closer together than its
own interval. The bench's generator is exact by construction, which is a fidelity gap a later slice
should know about before it builds anything on sub-poll timing.

**What this fixture cannot cover.** The hour and day layers across their own periods: the extract
spans eight minutes, and the whole dump spans two hours with twelve restarts (`#not-established`), so `l = 2` and
`l = 3` are identical to `l = 1` there and prove nothing about their own thinning. Calendar versus
flush-window alignment (`#not-established`): both readings agree on this sample, because every minute of the
extract is bounded by a break or by a stretch with no rows, so no period edge is under tension — the
controlled run at the *Not established* section is still the only thing that settles it. Bad-quality rows: the dump holds
only `q ∈ {0, 16, 32}`, so the "row present, point discarded" state at `#quality-and-gaps` stays unbenched.

**One file beyond the list.** `RealArchiveFixture.cs` reads and parses the CSV, keeping the test class
to assertions; the CSV is copied to the output directory by a `None Update` item in the test csproj,
so the tests find it next to the assembly rather than by walking up to the repository root.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **135 passed, 14 skipped** in `SemiPlot.Tests.Data` (9 of the passing ones new,
the 14 skipped being the gated set with no `SEMIBASE_EXE`) and **250 passed, 0 failed** in
`SemiPlot.Tests`, matching the Task 1 baseline. The new tests need no database. `dotnet format
SemiPlot.slnx` makes no change.

### Task 11: Add the data-tests CI job

**Files:**
- Modify: `.github/workflows/ci.yml`

- [x] second job `data-tests` on `ubuntu-latest` alongside the existing Windows job: checkout,
      .NET from `global.json`, download the `semibase_0.1.0_linux_amd64` release binary
      (`gh release download v0.1.0 --repo Semiteq/SemiBase`), mark it executable and expose it via
      `SEMIBASE_EXE` — no Go toolchain on the runner — then
      `dotnet test SemiPlot/SemiPlot.Tests.Data`
- [x] job env: `SEMIPLOT_REQUIRE_DB=1` — on CI an unavailable runtime is a failure, never a silent
      skip. The `SEMIBASE_*_PASSWORD` variables this item originally called for were removed again
      during review: the container path supplies its own fixed passwords and injects them into the
      child process, and those variables are read only on the `SEMIPLOT_TEST_PG` path, so setting
      them in the job read as required wiring while changing nothing
- [x] the Windows job is unchanged and does not set `SEMIPLOT_REQUIRE_DB`: Windows runners cannot
      run Linux containers, so gated tests skip there by design
- [x] acceptance for this task is the `data-tests` job green on the pull request — ⚠️ **deferred to
      delivery**: the branch is unpushed and no pull request exists, so no run can be observed from
      here. Everything checkable without a runner was checked instead (below); confirm the job on the
      pull request when the branch is delivered

➕ **`sql/**` was added to both `paths:` filters.** The schema script is an `EmbeddedResource` of the
seeder (Task 6), so a change to `sql/semiplot_dev.sql` changes what `data-tests` runs — and under the
filter as it stood, that change alone triggered no run at all. `!**.md` still keeps `sql/README.md`
out, which is right: it is documentation of the extraction, not an input to a build.

**Verified without a pull request, 2026-08-14.** Four things were checked by running them:

| Check | Result |
| --- | --- |
| the workflow parses and holds two jobs | `yaml.safe_load` gives `build-and-test` on `windows-latest` and `data-tests` on `ubuntu-latest`; the Windows job has no `env:` at all |
| the release asset name is real | `gh release view v0.1.0 --repo Semiteq/SemiBase` lists `semibase_0.1.0_linux_amd64` and `semibase_0.1.0_windows_amd64.exe` |
| the download step's exact command works | `gh release download v0.1.0 --repo Semiteq/SemiBase --pattern semibase_0.1.0_linux_amd64` fetched a 9 343 138-byte statically linked x86-64 ELF |
| the job's three commands run | `dotnet restore` / `build -c Release --no-restore` / `test -c Release --no-build` against `SemiPlot.Tests.Data.csproj` alone: 149 tests, 135 passed and 14 skipped with no `SEMIBASE_EXE` |

The job's environment was then reproduced locally — `SEMIPLOT_REQUIRE_DB=1` plus the three dummy
`SEMIBASE_*_PASSWORD` values plus a downloaded `v0.1.0` binary in `SEMIBASE_EXE`, Docker running:
**149 passed, 0 skipped, 0 failed**. That is the one risk the dummy passwords carry — the fixture
overrides them with its own constants on the container path (`PostgresContainerFixture:25-29`, passed
through the child environment in `SemibaseProvisioner.CreateAsync`), and this confirms the override
holds rather than inferring it from the code. What stays unproven until the pull request: the
`ubuntu-latest` runner itself and its Docker daemon.

**The job builds one project, not the solution.** `SemiPlot.Tests` is `net10.0-windows` and
references the UI, so a Linux runner has no reason to restore or build it; the data project and its
two references (`SemiPlot.Core`, `SemiPlot.Tools.ArchiveSeeder`) are plain `net10.0`. The three steps
name `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj` rather than the directory the
checkbox writes, which is the form the Windows job already uses.

**`GH_TOKEN` is set on the download step.** The checkout runs with `persist-credentials: false`, so
`gh` has no token to inherit and would fail on an unauthenticated call even against a public
repository. `${{ github.token }}` with the job's `contents: read` is enough to read a release asset.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **135 passed, 14 skipped** in `SemiPlot.Tests.Data` and **250 passed,
0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline. `dotnet format SemiPlot.slnx
--verify-no-changes` is clean. No file outside `.github/workflows/ci.yml` and this plan was touched.

### Task 12: Verify acceptance criteria

- [x] every check in Acceptance Evidence runs and produces the stated result — five of the seven were
      run here; check 6 is not re-runnable on this machine and check 7 waits for the pull request,
      both recorded below
- [x] `dotnet test SemiPlot.slnx` — zero failures across both test projects
- [x] `git diff` shows no change under `SemiPlot/SemiPlot.Tests/`
- [x] gated tests against a stopped Docker report skips only; with `SEMIPLOT_REQUIRE_DB=1` they fail
- [x] seed a database by hand and confirm `tpdefault` is empty and per-layer counts match
- [x] `dotnet format SemiPlot.slnx` reports no changes
- [x] confirm no file outside `SemiPlot.Tools.ArchiveSeeder`, `SemiPlot.Tests.Data`, `sql/`,
      `SemiPlot.slnx`, `.github/workflows/ci.yml` and the two shared props files was modified

**Acceptance Evidence, item by item, 2026-08-14** at commit `b9867fb` on branch `archive-populator`:

| # | Check | Observed |
| --- | --- | --- |
| 1 | `dotnet test SemiPlot/SemiPlot.Tests.Data --filter "Category!=Integration"` | **135 passed, 0 failed, 0 skipped** in 1 s |
| 2 | Task 10's assertions on real rows | `RealArchiveFixtureTests`: **9 passed** in 89 ms; the `LayerThinner` comparison stays a finding in Task 10, not a gate |
| 3 | unreachable runtime never a pass | four branches run with the Docker engine genuinely stopped — table below |
| 4 | the existing suite untouched | `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`: **250 passed, 0 failed**, matching the Task 1 baseline; `git diff master...HEAD -- SemiPlot/SemiPlot.Tests/` is empty |
| 5 | a hand-seeded database read as `semiplot_reader` | counts match the generator exactly, `tpdefault` empty — table below |
| 6 | the `trends` DDL is the vendor's | ⚠️ **not re-runnable here**, see below |
| 7 | CI enforces the availability policy | ⚠️ **deferred to delivery**: the branch has no upstream and no pull request exists, so no `data-tests` run can be observed. Task 11 checked everything checkable without a runner |

**The stopped-engine check was run for real, and Task 7 was right that it had to be.** `docker
desktop stop` removed the engine — `npipe:////./pipe/dockerDesktopLinuxEngine` no longer exists — and
Testcontainers then reported the absence instead of falling back the way a dead `DOCKER_HOST` let it.
`docker desktop start` afterwards returned the engine to `running`, server `29.7.2` on Linux
containers, confirmed by a `hello-world` pull and run.

| Condition, engine stopped | Result |
| --- | --- |
| `SEMIBASE_EXE` set, no `SEMIPLOT_REQUIRE_DB` | **135 passed, 14 skipped, 0 failed** — reason: `no container runtime started postgres:17-alpine: Docker is either not running or misconfigured…` |
| the same with `SEMIPLOT_REQUIRE_DB=1` | **14 failed, 0 skipped** — `InvalidOperationException: SEMIPLOT_REQUIRE_DB is set, so an unavailable runtime fails instead of skipping: no container runtime started…` |
| `SEMIBASE_EXE` unset, semibase not on `PATH` | **135 passed, 14 skipped, 0 failed** — reason: `semibase was not found on PATH: download the v0.1.0 release binary…` |
| the same with `SEMIPLOT_REQUIRE_DB=1` | **14 failed** with the semibase reason — semibase is probed first, so no container start is even attempted |

**The hand seed, outside the test fixture.** A `postgres:17-alpine` container started by hand on port
55450, provisioned by `semibase create --host 127.0.0.1 --port 55450 --database semiplot_manual
--superuser postgres` (PostgreSQL 17.11; every phase reported `OK`, including the default-privileges
chain and `semiplot_tags`), then the console binary against it:

```
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- \
  --connection "…Username=scada_writer…" --admin-connection "…Username=postgres…" \
  --days 1 --pens 8 --seed 1 --end 2026-01-02T00:00:00
```

The seeder reported `l=0` 229 862, `l=1` 35 599, `l=2` 815, `l=3` 96, 266 372 rows written and 8 tags,
and exited 0. Read back through a `psql` session **as `semiplot_reader`** (`current_user` checked
first): the same four counts, the same 266 372 total, `public.tpdefault` **0 rows**, `semiplot_tags`
**8 rows** with ids 1000, 1001, 2000, 2001, 3000, 3001, 4000, 5000 — round-robin across all five
catalogue groups, not the first eight of one. `trends` carries exactly two partitions,
`tp2026m01d01` and `tpdefault`. Every number matches Task 9's, which is what the two paths agreeing
is worth. The container was removed afterwards; nothing is left on the machine.

⚠️ **Check 6 is not re-runnable on this machine, and that is by design.** The comparison needs the
customer dump, whose path `sql/README.md:14-16` deliberately does not record — and a filesystem
search for it is outside what this task may do. What was verified instead: `sql/semiplot_dev.sql`
matches `docs/architecture/scada-archive.md#database-objects` column for column, `DEFAULT` clauses, `timestamp(3)
without time zone`, the `smallint` layer, `PARTITION BY RANGE (t)` and the `tpk` constraint included;
`messages` is absent; and `SchemaResourceTests` — among the 135 passing — asserts the same content on
the embedded resource the seeder actually applies. The `pg_restore` provenance rests on Task 1's
record and its table of removals.

**Nothing outside the stated set was touched.** At the end of the task loop `git diff --name-only
master...HEAD` listed 47 files. Five review rounds then followed, and the final count is 67 —
the additions being new test files, the architecture documents the review pass corrected, and
`readme.md`. Of the two shared props files only `Directory.Packages.props` moved;
`Directory.Build.props` is unchanged, as Task 2 required. The working tree is clean.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings (NU1903 restore
warnings remain, from transitive packages of `Testcontainers.PostgreSql` 4.13.0 and Avalonia; the
`Testcontainers.PostgreSql` one is gone at the 4.14.0 pin).
With Docker running and `SEMIBASE_EXE` set, `dotnet test SemiPlot.slnx` reports **250 passed** in
`SemiPlot.Tests` and **149 passed, 0 skipped, 0 failed** in `SemiPlot.Tests.Data` — all 14 gated tests
ran against a real container rather than skipping. `dotnet format SemiPlot.slnx --verify-no-changes`
exits 0.

### Task 13: Update documentation

- [x] reduce `docs/architecture/postgres-instance.md` to SemiPlot's consumer contract and
      cross-reference SemiBase for the rest — the configuration deltas, the provisioning order and
      the role definitions are owned there now, and the local provisioning order is not merely
      duplicated but wrong: it creates the reader after the writer's first run
- [x] the roadmap was amended ahead of execution (2026-08-14): two solution projects instead of a
      standalone script, gated-harness ownership here, `semiplot_tags` populated by the seeder, the
      no-stub-fallback composition slice, and the live-demo-and-stub-retirement slice — confirm the
      amended text still matches what was actually built, and update it where reality diverged
- [x] fix the coarse-row arithmetic in `docs/architecture/scada-archive.md#retention` and
      `docs/architecture/postgres-instance.md`: "roughly 1465 rows per variable per day" is one
      point per period, inconsistent with the four-per-period budget stated everywhere else — the
      four-per-period ceiling is about 5860
- [x] replace the one-test-project rule in `CLAUDE.md` with the two-project rule, phrased as the
      current state plus its exit path: a Windows Avalonia project on xunit v2, and a `net10.0`
      project on xunit v3 for everything below the UI, unified when the Avalonia 12 bump happens
- [x] verify against the NuGet nuspecs, then correct the stale premise in `CLAUDE.md` that the
      xunit-v3 unification is blocked by ScottPlot.Avalonia lacking an Avalonia 12 build — do not
      write the version numbers in without checking them at execution time
- [x] document `SEMIPLOT_TEST_PG`, `SEMIPLOT_REQUIRE_DB`, `SEMIPLOT_PG_IMAGE`, `SEMIBASE_EXE` and
      the `SEMIBASE_*_PASSWORD` variables in `CLAUDE.md`
- [x] add both new projects to `readme.md` if it lists the solution's projects — it does not, so
      this task left it untouched. A later review round did amend it for a different reason: the
      test line silently skips the gated tests on a machine without a container runtime and
      `semibase`, which the requirements table did not mention
- [x] move this plan to `docs/plans/completed/` — ⚠️ **deferred to delivery**: archiving runs after
      the operator has tested the branch, and the review and stats phases read this file where it
      is. Move it when the branch is delivered

**`postgres-instance.md` lost two thirds of its length and all of its duplication, 2026-08-14.**
Deleted: the ownership table, the installation section, the configuration-deltas table, the roles
table, the provisioning order, the `semiplot_tags` DDL block, and the PostgreSQL upgrade lines —
every one of them owned by SemiBase (`docs/architecture/overview.md`, `configuration.md`,
`provisioning.md`, `sql/semiplot_tags.sql`), which the new cross-reference table names one by one.
The provisioning order was worse than duplicated: its step 4 created `semiplot_reader` *after* the
SCADA's first run at step 3, which is exactly the state SemiBase's `ALTER DEFAULT PRIVILEGES` chain
exists to prevent and which `semibase verify` reports as a fault. Kept, because each constrains the
client: the reader role's contract (`SELECT` on three tables, `statement_timeout` 30 s,
`idle_in_transaction_session_timeout` 60 s, the plaintext-password rationale), the `semiplot_tags`
columns SemiPlot reads, the four provisioning states the client must survive, retention and
capacity, backup, and the schema-drift probe SemiBase's overview explicitly assigns to the reader.
`semiplot_admin` went with the roles table — the role was removed in SemiBase `v0.1.0` and this was
the last mention of it in either repository.

**The engine line was stale too.** The document said "PostgreSQL installed through `winget`, with the
major version pinned. Minimum major version 14" without saying which major, in a repository whose
bench and CI both pin `postgres:17-alpine`. It now states vanilla PostgreSQL 17 with 14 as the
declared floor, and gives the floor its consumer meaning: `date_bin` is what SemiPlot's SQL may not
outrun.

**The `CLAUDE.md` premise was stale on both halves, verified against nuget.org 2026-08-14.**

| Package | Version pinned here | Its dependency | Later version | Its dependency |
| --- | --- | --- | --- | --- |
| `Avalonia.Headless.XUnit` | 11.3.8 | `xunit.core` 2.4.0 | 12.0.0 through 12.1.1 | `xunit.v3.extensibility.core` 3.2.2 |
| `ScottPlot.Avalonia` | 5.1.57 | `Avalonia` 11.3.4 | 5.1.59 | `Avalonia` 12.0.0 |

So `ScottPlot.Avalonia` does have a released Avalonia 12 build (5.1.59, and 5.1.58 does not — the
change landed in the last release), and `Avalonia.Headless.XUnit` is no longer xunit-v2-only from
12.0.0 on. `ReactiveUI.Avalonia` publishes 12.1.1 as well, so the whole 11 → 12 set exists. Nothing
external blocks the xunit-v3 unification any more; what blocks it is that nobody has done the
Avalonia 11 → 12 bump of the UI, which is its own piece of work and not this slice's. `CLAUDE.md`
now says exactly that, with the nuspec evidence.

**Three files beyond the checkboxes.** `docs/architecture/README.md` describes
`postgres-instance.md` in one bullet and that bullet listed the sections just deleted.
`data-integration.md` had SemiPlot owning "PostgreSQL instance: installation, configuration, roles,
backup, upgrade" in its responsibility table, which contradicts the reduced document; the cell now
reads "client of it — provisioned by SemiBase". And `sources.md` is where `[MEAS:dump-20260805]`
resolves, so the three new establishments from Task 10 are listed there beside the old ones, with
the committed CSV named as the artifact slice.

**Task 10's findings became measured claims in `scada-archive.md`.** That is the document holding
what is known about the archive, and all three came from the same dump, so they carry the same
`[MEAS:dump-20260805]` marker: the repeated-extreme tie-break keeps the later row; *last of the
period* is confirmed as one selected point while *first* stays inferred (the "Not established" row
was narrowed rather than removed); and the 100 ms poll jitters, 30 of 34 change rows exact and 4 at
104 to 109 ms, so a reader must allow about 10 ms of tolerance. The tie-break paragraph states the
consequence a later slice needs — the envelope is the same either way, only the abscissa moves.

**The roadmap needed three corrections, not a rewrite.** Its amended `archive-populator` text
matched what was built almost line for line. Corrected: the blast radius said "the two shared props
files" where only `Directory.Packages.props` moved (`Directory.Build.props` is untouched by design,
and Task 12 verified it); the scope did not mention the container image default or the
`SEMIPLOT_TEST_PG` escape hatch, both of which the later slices inherit; and the status was
`PENDING` for a slice whose plan is finished, so it is `IN-PROGRESS` with the branch filled in —
`DONE` is a merge-time stamp and is not ours to write. The open fork on the vendor's selection rule
gained the narrowing from Task 10. The file still passes `check-inert.sh`.

**Measured 2026-08-14:** `dotnet build SemiPlot.slnx` succeeds with no code warnings, `dotnet test
SemiPlot.slnx` reports **135 passed, 14 skipped** in `SemiPlot.Tests.Data` (no `SEMIBASE_EXE` in
this shell) and **250 passed, 0 failed** in `SemiPlot.Tests`, matching the Task 1 baseline.
`dotnet format SemiPlot.slnx --verify-no-changes` is clean, and
`check-inert.sh docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md` prints `inert`. No
file outside `docs/` and `CLAUDE.md` was touched — this task changes no code.

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

**Executed by exec:**

- branch: archive-populator

## Verify it yourself

Three of these need nothing but the repository. The fourth needs Docker and the pinned `semibase`
binary, and is the one that proves the two repositories work together.

1. **The bench is deterministic and unchanged.**
   `dotnet test SemiPlot/SemiPlot.Tests.Data --filter "Category!=Integration"`
   183 pass. `TheStandardSliceMatchesItsGoldenDigest` is the one that matters: it pins the standard
   slice at 229 862 raw rows and digest `59a88008953845d205ed7d61da1c543833d9bf7666a85d8d2774905986e25f78`.
   Five review rounds edited the generator's surroundings and none moved a row.

2. **An unavailable runtime never reports as a pass.**
   With Docker stopped and `SEMIBASE_EXE` unset, `dotnet test SemiPlot/SemiPlot.Tests.Data` reports
   24 skipped with a stated reason, none passed. The same command with `SEMIPLOT_REQUIRE_DB=1`
   reports 24 failed. That pair is what keeps the CI job honest.

3. **The existing suite is untouched.**
   `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` reports 250 passed, and
   `git diff master...HEAD -- SemiPlot/SemiPlot.Tests/` is empty.

4. **The two repositories provision a database together.** Download the pinned binary from the
   `v0.1.0` release of `github.com/Semiteq/SemiBase`, point `SEMIBASE_EXE` at it, start Docker, then
   `dotnet test SemiPlot/SemiPlot.Tests.Data`. All 207 pass with 0 skipped, 24 of them against a
   real `postgres:17-alpine` that `semibase create` provisioned, seeded by the writer as
   `scada_writer` and read back as `semiplot_reader`. A broken grant fails here rather than on
   commissioning day, which is the whole reason the bench uses the production roles.

**What no check here covers.** Acceptance item 7 — the `data-tests` job green on a pull request —
cannot be observed until the branch is pushed, and item 6 needs the customer dump, which is
deliberately outside the repository. Both are recorded as deferred in Tasks 11 and 12.
