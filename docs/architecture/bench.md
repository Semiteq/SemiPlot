# The seeded bench

The data-source slices need a PostgreSQL archive that looks like a Simple-Scada 2 archive and holds
the same rows on every machine. This document is that bench as it exists. What the vendor's archive
is remains in `scada-archive.md`; what SemiPlot reads from it remains in `data-integration.md`.

## Ownership

| Piece | Owner |
| --- | --- |
| Database, roles, grants, default-privileges chain, `semiplot_tags`, `public.trends` with `tpdefault` | `semibase bench` (`github.com/Semiteq/SemiBase`), carried in the bench image |
| The daily partitions and the rows | `SemiPlot.Tools.ArchiveSeeder`, connected as `scada_writer` |
| The moving live edge of a demo archive | `SemiPlot.Tools.ArchiveSeeder --follow`, connected as `scada_writer` |
| Template build, the clones, teardown | `SemiPlot.Tests.Data/Integration` |

The archive table is the provisioner's, not this repository's. `semibase bench` creates it over
`SET ROLE scada_writer`, so it lands owned exactly as a site's does and SemiBase's
default-privileges chain sits on a path every seeded run exercises. Nothing here transcribes the
vendor's DDL: a second definition would be the one exercised daily while the real one decayed.

The seeder writes into that table the way the SCADA fills its own. It creates the day partitions
its rows land in and nothing else, so what it applies is a consequence of the slice, not a schema.

The seeder never destroys. An absent `public.trends` is a failure naming the provisioning that did
not run; a table already carrying rows or day partitions is refused, because those are what a
previous run leaves. Provisioning creates the table empty and `tpdefault` with it, so neither of
those is evidence of a seeding and neither counts. The seeder issues no `DROP` anywhere. Only the
test fixture drops databases, and only ones it created itself.

## The standard slice

One day, 8 pens, seed 1, change interval 5 s, 4 breaks, exclusive end `2026-01-02T00:00:00`.
The end is fixed rather than floating, so two runs of the same seed produce the same archive. Its
shape is pinned by `RawLayerGeneratorTests` — determinism, the absolute lattice, the break holes and
the row-pair shape — and the waveform itself is not, so a deliberate change to the value walk moves
no constant. The raw layer is about 272 000 rows and all four layers about 315 000, landing in one
day partition.

Pens are taken round-robin across the catalogue's groups rather than as the first N, so a slice
spans more than one group and more than one value range.

**What a smaller slice would buy, and what it would cost.** The planner floor is **509 rows in 4
`relpages`, and it is a knife edge**: 494 rows in the same 4 pages already loses the poll statement's
index plan to the sequential scan `ExplainPlanTests` rejects. One content test binds far above that
floor and is what actually sets the size —
`PostgresHistoryReadTests.TheMinuteLayerReturnsFewerColumnsThanRawOverTheSameWindow`, which needs the
raw layer denser than the minute layer inside its window. The saving is small either way: the seeder wrote a
slice of this order over a published port in about a second, and a slice a thirty-seventh of its size
took 0.45 s. Both floors were measured before the generator merge, which only widened the margin —
the merged lattice is denser than the walk it replaced.

## What the generator emits

Layer `0` only; the coarse layers are derived from it.

- **One lattice, written by the seeding run and by the demo writer alike.** A change sits at
  `index * intervalTicks` measured from absolute tick zero, where the interval is `--change-seconds`
  rounded to whole milliseconds — an exact interval, not a mean — and its value is
  `SyntheticValueWalk.Value(seed, penId, index, min, max)`, a pure function of its inputs. A row's
  value therefore depends on no row before it, and a follow run resuming at the archive edge
  continues the lattice the seeding wrote instead of approximating it. `RawLayerGenerator` and
  `LiveTailGenerator` both emit through `RawLayerGenerator.AppendWindow`; a seeding run walks the
  lattice run by run between the breaks, a follow run span by span.
- A change carries its pre-anchor: the previous value one poll interval earlier, then the new value
  at the change tick. That is the vendor's two-rows-per-change shape, so linear interpolation
  between a pair is exact. A change interval no wider than the poll interval leaves no room for the
  anchor and carries none.
- Timestamps carry whole milliseconds only, matching `timestamp(3)`, so an in-memory uniqueness
  check means what `PRIMARY KEY (id, l, t)` means.
- **The lattice carries no per-pen phase**, so every pen changes at the same instants. That is a
  consequence of one lattice for both generators, and it is kept deliberately: a per-pen offset would
  move every expectation computed against the seeded archive. A defect
  that only shows when two pens carry distinct timestamps is therefore not exercised by this bench
  and needs a test that builds its own rows.
- Three quality codes and no others: `0`, `16`, `32`. No bad-quality code was observed in the
  measured dump, so inventing one would be fiction.

## Breaks

A break is the SCADA project stopped: no rows anywhere in the interval, the last row before it
marked `32` and the first row after it `16`. Breaks hit every pen at the same instants.

The lattice is absolute, so a break boundary is not a point the lattice is drawn at. The resume row
is the first lattice point at or after the break's end — within one change interval of it, which is
the range `BreakGenerationTests.EachMarkerPairBoundsOneBreakWindow` asserts — and it carries no
pre-anchor, because the plant moved while archiving was stopped.

Each break takes an equal slot of the span, lasts 3 to 10 minutes and leaves at least 5 minutes of
archiving on either side — so two breaks never meet, and every break empties at least one whole
calendar minute, which is the empty period the thinner has to survive. A span therefore holds at
most one break per 20 minutes: 72 in a day. `SeederCommand` rejects a larger `--break-count` with
that number rather than letting `BreakPlan.Create` throw.

Both markers land on real change rows, so with breaks every archiving run holds at least two
changes: a run holding one would have to carry `32` and `16` at once, and the archive has no code
for both. **The tight run is the first or the last**, not one between two breaks: those two are
guaranteed only `BreakPlan.MinimumRun`, five minutes, while a run between two breaks is at least
twice that. So the bound is not a flat rule on `--change-seconds`: 600 s is refused at 20 breaks and
beyond under the default seed because a run at one end falls under it, and the refusal names the
run. `RawLayerGenerator.Generate` throws rather than inventing a row, and `Program` prints the
reason. At the standard slice's 5 s interval no run comes anywhere near.

## The demo writer

`--follow` runs the seeder as a demo writer instead of as a seeder: it appends to an archive somebody
else seeded, so it plants no break and fills no tag catalogue, and it refuses nothing for the rows
already there. What it creates is the same thing a seeding run creates and nothing more — the day
partition each tick's rows land in, through
`CREATE TABLE IF NOT EXISTS … PARTITION OF public.trends`.
`public.trends` itself is the provisioner's and a follow run never creates it. What it moves is the
live edge the viewer's poll follows. Every tick appends the raw rows of the span since the previous
tick — the first tick's span opens one millisecond past the archive's own `max(t)` — thins them into
the coarse layers and prints both counts; `Ctrl+C` stops the loop where it waits, never inside an
append.

The command below writes into `semiplot_app` on port 55432 — the container and the clone that
**The application bench** below creates. Run that recipe first, or point `--connection` at an
archive of your own that a seeding run has already filled up to about now — a fill ending further
back than five minutes is refused rather than written into.

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --follow 1 --pens 8 --seed 1 --change-seconds 0.5
```

`--follow` takes the seconds between ticks and is the switch that selects this mode. `--pens`,
`--seed` and `--change-seconds` mean what they mean in a seeding run — pens taken round-robin from
the catalogue, the generator seed, and the exact interval between value changes — and default to 8,
1 and 5.

Four properties decide what it is good for:

- **Every layer, each on its own cadence.** Two statements do the thinning, both issued by the loop
  on a connection of its own. A closed-period `INSERT ... SELECT` reproduces `LayerThinner`'s
  selection in SQL and runs once for every period of that layer the tick leaves behind, so at
  `--follow 1` layer 1 steps once a minute, layer 2 at the hour crossing and layer 3 at the day
  crossing — and a tick that spans several periods, after a stall or at a cadence above the
  period, closes each of them rather than only the first. An opening-row
  `INSERT` runs every tick and writes the open period's first raw row, which the closed flush would
  select anyway, so it adds nothing to the period's final content and keeps every layer's seam
  inside the open period, which is what keeps a wide window's tail readable. Both close with
  `ON CONFLICT DO NOTHING`, so a period's opening row is written once and every later tick inside
  that period reports 0 coarse rows — that is what stops the coarse layers densifying toward raw,
  and the writer's own `appended … coarse` line is where a reader checks it. Over a 5-minute run
  at `--follow 1` the console showed 291 ticks, 285 of them `0 coarse`; the five minute boundaries
  reported 24, 30, 32, 31 and 29 coarse rows, and one tick inside a period reported 1, being the
  first tick to give one pen a raw row in that minute.
- **The seam clears `FreshTail`'s clamp with no margin, by design rather than by luck.** Whenever
  `--change-seconds` divides the period — 0.5 and 5 both divide a minute — a change row lands on
  every minute boundary, so a layer-1 period's opening row sits exactly at the period start.
  Measured at `--change-seconds 5` over the same run, every pen's layer-1 seam offset from its
  period start was 00:00:00, and the worst distance between the live edge and that seam was
  **59.9 s**, for all 8 pens in each of the four complete minutes, because the last raw row of a
  minute is the pre-anchor one `RawLayerGenerator.PollInterval` before the boundary. Inside a
  boundary tick the raw `COPY` and the coarse `INSERT` commit on
  separate connections, and between those two commits the live edge has already advanced to the
  boundary while the seam has not moved: measured, **exactly 60 s**, equal to the clamp. The pen
  keeps its tail because `FreshTail` compares `seam >= clamped`, non-strictly. Both figures are
  ceilings rather than constants — a `--change-seconds` value that does not divide the period puts
  the opening row later and shortens the distance — and no pen was observed dropping out of the
  tail.
- **The machine's local wall clock, with its `Kind` stripped.** The archive column holds the SCADA
  host's naive local time, so `DateTime.UtcNow` would place the demo's live edge one zone offset from
  where the viewer, converting through `source_time_zone`, looks for it.
- **It starts one millisecond past the archive's own `max(t)`, which a refusal keeps close to "now".**
  The first tick therefore continues the fill instead of standing apart from it behind a hole nothing in
  the archive marks — the absence a raw window draws as one straight interpolated segment across its
  whole width. What that start costs is the span of the first tick, and against a bench seeded weeks into
  the past it would be those weeks of rows and a day partition for each. `StaleArchiveGuard` is what
  bounds it: it reads `max(t)` once, before the first tick, refuses the run when the newest row is more
  than `StaleArchiveGuard.MaximumAge` — five minutes — behind the clock, naming `scripts/bench-demo.ps1`
  as the refill, and hands the accepted timestamp back to the loop, so nothing reads `max(t)` twice.
  An archive holding no rows is accepted and reports no timestamp: there is no edge to continue, and
  the loop starts at "now". A database provisioning never finished answers the same way and is
  `ArchiveWriter`'s to report, not this guard's.
- **The window is open at the edge, which is what makes a restart of the writer work.** The follow
  lattice is absolute, so an archive whose newest row a previous follow run wrote carries that row on a
  point the lattice produces again, and a window that included its start would regenerate that row
  into a `COPY` that has no conflict handling: the run would die on its first tick with `23505:
  duplicate key value violates unique constraint`. `LiveTailGenerator.Generate(options, after, to)`
  therefore emits the rows with `after < t <= to`. Both bounds are instants the archive already
  accounts for — the edge a restart hands in, or the previous tick's own instant, whose rows that
  tick wrote — so consecutive windows partition the lattice, and a restart continues it with no row
  twice and no hole: the next lattice point is inside one change interval of the edge.
  `FollowRestartTests` performs the restart against a database and `SharedLatticeTests` the same
  sequence in memory.

`--end`, `--days`, `--break-count` and `--admin-connection` belong to a seeding run and are rejected
here, with a message saying what a follow run does rather than "Unknown option".

## Thinning into the coarse layers

Layers `1`, `2` and `3` hold verbatim copies of raw rows — first, last, minimum and maximum of the
period, deduplicated when they coincide, plus every marker row regardless of selection. Every layer
is computed against the raw rows rather than against the layer below, which is what makes
`l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0` fall out on its own.

Ties on value resolve to the **earliest** row. The vendor keeps the **later** one
(`[MEAS:dump-20260805]`). The difference moves only the abscissa of a point, never the envelope, and
flipping it is deferred until the open calendar-versus-flush-window question is settled — the two
have to be answered together, since both change which rows a period selects.
`LayerThinner.PeriodStart` is the one place period alignment lives, so the experiment that settles
it replaces that method and nothing else.

## The test bench

`SemiPlot.Tests.Data` runs on a Linux CI runner. It never references the UI.

- One server per run. A container start costs seconds while a `CREATE DATABASE` costs under one, so
  the container is the wrong isolation unit and the database is the right one.
- One provisioned source database per server, `semiplot_provisioned`. Everything else is a
  `CREATE DATABASE ... TEMPLATE` clone of it, which copies table ownership, `relacl` and
  `pg_default_acl`; the database-level `CONNECT` is not copied, and `PUBLIC`'s default covers it.
- One template per run, `semiplot_bench`. The name is a constant rather than a digest because a fresh
  container guarantees the database does not already exist, so nothing checks and nothing repairs.
  Nothing drops the template afterwards either: the server dies with the run. Every
  `semiplot_clone_*` is dropped by the test that made it, and any that is not dies with the
  container along with the template.
- One clone per consumer, of one of the two sources, and the consumer decides both. `CloneSource`
  names the source: a class that reads the seeded rows clones `semiplot_bench` and so skips the
  `COPY` entirely, while a class that writes its own rows clones `semiplot_provisioned` and starts
  from an empty `public.trends`. Seven of the eight clone-owning classes take the provisioned source;
  `SeededArchive` and `LiveEdgeArchiveJourneyTests` take the template. The holder decides the grain:
  the `SeededArchive` class fixture gives a whole class one clone, while a class deriving from
  `ClonedArchiveTest` gets one per test method, because xunit constructs a test class once per test
  method.
- Every read in the gated tests connects as `semiplot_reader`, not as the superuser: a grant that
  never reached the reader fails a test here instead of on commissioning day.

**The container is the only path, and it is disposable.** There is no branch for a server the run
did not start, nothing is reused between runs, and nothing the run creates survives it: the built
image carries the label `semiplot.bench=1` and is built with clean-up on, the container and its
volume die with the session, and the databases die with the container.
`PostgresContainerFixture.DisposeAsync` disposes the container and does nothing else. Disposability
rests on `WithCleanUp(true)` and the resource reaper, not on a teardown assertion: with the container
destroyed at the end of every run, a clone that outlived its own test dies with the server it lives
in, so there is nothing left for a teardown check to find.
`PostgresContainerFixtureTests.TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository` is the
tripwire on that — a revert to `WithCleanUp(false)` drops the reaper's label and fails it while
passing every other test in the suite.

**What a run deliberately does not clean up: the images it pulled.**
`ghcr.io/semiteq/semibase:latest` and the base image stay on the machine, because the resource reaper
labels what Testcontainers creates and not what the registry served. That is the intended split — a
pulled image is a cache and re-pulling it every run would put the registry on the critical path,
while a built image is this run's own and goes with it.

## Where the provisioning comes from

The provisioner is a layer of the bench image, not a binary on the machine.
`SemiPlot.Tests.Data/bench/Dockerfile` copies `/semibase` out of `ghcr.io/semiteq/semibase:latest`
onto the base image `SEMIPLOT_PG_IMAGE` names, and places `provision.sh` in
`/docker-entrypoint-initdb.d/` with mode 0755. The entrypoint runs it — a mode bit is what
separates *run* from *source* there — while `initdb` is still in progress, so
`semibase bench --host /var/run/postgresql --database "$SEMIPLOT_PROVISIONED_DATABASE"` goes over
the unix socket under local `trust`, before the published port opens. The fixture passes that
database name in, so `BenchNames.ProvisionedDatabase` is the one place it is written.
`set -e` exits the script on a failed provisioning and the entrypoint, itself under `set -e`,
aborts with it: the container exits non-zero and no port ever serves an unprovisioned database.

The fixture builds that image in-process from the `bench/` directory copied to the output directory
— a test assembly has no path to the source tree. The tag carries a digest of the base image, so a
run under a changed `SEMIPLOT_PG_IMAGE` is never served the build made over the previous base. The
image is built with clean-up on and carries the label `semiplot.bench=1`, so the resource reaper
deletes it with the session and a run leaves no dangling image behind. Every run therefore rebuilds,
for the plain reason that the previous run deleted what it would have been served;
`PostgresContainerFixtureTests.TheBuiltBenchImageIsLabelledForTheReaperAndForThisRepository` asserts
both labels on the built image, which is the only visible trace of the clean-up call. Disposability
is the requirement here; build time is not.

Readiness is asserted rather than observed once and trusted: the wait strategy runs `psql` inside
the container against `public.trends` over **TCP**, and the entrypoint's temporary server listens on
the unix socket only. A container whose provisioning did not complete therefore never becomes ready.

**Both waits are bounded at two minutes**, from one field, `PostgresContainerFixture._startupBound`,
so they cannot drift apart: the registry pull and the readiness wait, whose default would otherwise
be Testcontainers' own one hour. A bench that never comes up is then a stated skip inside two
minutes rather than a hung `SemiPlot.Tests.Data.exe` that locks the next build. Readiness raises
`TimeoutException`; a pull that runs out of its bound is a failed pull, which the next paragraph
covers. Neither escapes as `OperationCanceledException`, which the fixture's one catch excludes and
which would fail the whole collection instead of skipping it.

Init scripts run only on an empty `PGDATA`, so this shape provisions a fresh cluster and never a
reused volume.

The one runtime the bench needs is a container runtime, and it is optional on a developer machine.
Its absence is captured as a stated reason and turned into a skip, never into a pass.
`SEMIPLOT_REQUIRE_DB` turns that skip into a failure; the CI `data-tests` job sets it. The full
variable list is in the root `CLAUDE.md`, section *Gated data tests*.

What the bench bets on is that SemiBase's `latest` stays compatible with this reader. Delivered
installations update neither service, so the only pair ever newly deployed is the newest provisioner
with the current reader — which is the pair every run exercises.

That last clause is only true because something fetches the tag, and building the bench image is
not it. The Engine's builder resolves the provisioner's `FROM` from the local image cache, so a
rebuild on its own would copy whatever provisioner the machine last happened to hold — for good, on
a machine that pulled once. The fixture therefore runs `docker pull ghcr.io/semiteq/semibase:latest`
ahead of the build (`DockerCli.PullProvisionerAsync`), so the cache the builder reads holds the
newest tag.

It is a separate step rather than `pull` on the build request because the Engine fails a build
outright when `pull` is set and the registry cannot be reached, even with a usable image already
cached. Pulled on its own, that case stays recoverable: a machine with no route to the registry, or
with no `docker` CLI on `PATH`, runs against the image it has, and only a machine with neither route
nor image is an unavailable reason — the build reports the missing `FROM`. A run that fell back that
way writes one `[bench]` line to standard error saying why — standard error rather than the test
output, because a passing test's output is what a console logger drops.

The cost of a moving tag is that one unchanged commit can pass today and fail tomorrow. When it
does, `docker image inspect ghcr.io/semiteq/semibase:latest` names the digest the run built over,
and `docker run --rm ghcr.io/semiteq/semibase:latest --version` its version.

## The application bench

The gated tests exercise the provider, and the `journey-tests` job exercises the composed
application: `ubuntu-latest` hosts Avalonia and a container at once, so `SemiPlot.Tests.Journeys`
drives `AddPostgresData`, `TrendCoordinator` and `TrendChartViewModel` over a container-backed
archive and asserts on rendered pixels and on a delivered live sample
(`testing-strategy.md`, **End-to-end tests**). What those journeys cannot answer is what the chart
looks like to a person. The application bench is where a human answers that, and its own checks are
read from the server and the log rather than from a screen, so they run unattended.

It runs the same bench image the gated suite does, so it needs no `semibase` binary either. The
image provisions `semiplot_provisioned` before the published port opens; the recipe clones that
database and seeds the clone.

**The clone is not a formality.** The seeder refuses a database that already carries rows or day
partitions, and every recreate of the stand's database uses `semiplot_provisioned` as its `TEMPLATE`
source. Seeding that source by hand would leave rows in it and make the stand a one-shot: the next
convergence, and every convergence at a different slice, would then need the container rebuilt from
the image. Seeding a clone keeps the source pristine, so a reseed is a drop and a recreate.

```powershell
pwsh scripts/bench-demo.ps1          # converge the stand
pwsh scripts/bench-demo.ps1 -Down    # remove it
```

**`scripts/bench-demo.ps1` is the recipe, not a copy of one**, and every piece of the stand it owns
is converged. What differs between the pieces is the test each convergence applies.

The image and the container are converged **on existence**: built once, reused after, so the slow
half of the recipe is paid once per boot rather than once per session. That is the deliberate
opposite of the test bench above, and for the opposite reason: the gated suite is a batch that must
leave a machine as it found it, while the stand is something a person comes back to between
sessions. `-Down` is how it goes away.

`semiplot_app` is converged **on freshness**: the script reads `max(t)` from `public.trends` and
decides between two paths. When the newest row is within five minutes of the wall clock the archive is live — a demo
writer is appending to it — and the script keeps it and rewrites only the connection file. When it is
further back than that, when the archive holds no rows, or when the database is absent, the archive
is recreated: its backends and the source's are terminated, the database is dropped, cloned from
`semiplot_provisioned` and filled by the seeder with `--days 1 --pens 8 --seed 1` up to `-SeedEnd`.
That parameter defaults to the script's own wall clock, and the seeder's `--end` is exclusive, so the
newest row lands just under the value given. **Stating `-SeedEnd` recreates whatever the archive
holds.** No `-Reseed` switch stands beside it: both intents that need a recreate — the stale-past
stand and a pristine reset — are a statement of where the fill ends, and a switch meaning "recreate
up to the default end" would be a second spelling of `-SeedEnd` with the current instant.

**Converging the archive is not the cross-session drift an unconditional recreate removed**, because
stale and live are exactly the two states that argument separates. A stale archive is the previous
session's, and it is recreated, so the extent still starts where the seed puts it rather than
stretching a little further from it each time. A live archive is one a demo writer is appending to at
this moment, and keeping it is the loop working: recreating it would terminate that writer's backend
and drop the database out from under it.

**Five minutes is the bound**, and `StaleArchiveGuard.MaximumAge` applies the same one from the demo
writer's side. Its floor is the writer's tick cadence — at `--follow 1` a running writer keeps
`max(t)` a second or two behind the clock, so five minutes is three hundred ticks of margin and no
running writer is read as a stopped one — and it must still cover the latency between one instance's
fill ending and the next instance's read of `max(t)`, which is a `dotnet run` of the seeder. Its
ceiling is the demo writer's first tick, which starts at the archive's `max(t)`: whatever a kept
archive is behind the clock is what that tick writes, and five minutes of rows across at most one day
boundary is what the bound admits. The unchecked recreate it replaces measured a 793.7-minute hole.
A keep costs about 4 s end to end and writes nothing but the connection file.

**A named mutex, `Global\semiplot-bench`, spans the whole convergence** and is released in a
`finally`, so a failed run does not wedge the next one. Two instances started at once serialise: the
loser waits and then finds the image, the container and the archive already converged. That is what
makes the script safe as a before-launch task of two configurations at once, which is how the stand
became one button. Measured from a dropped database, before the generator merge, two instances
started together: the winner filled a whole day up to its own wall clock — 266520 rows at the
density of the time, around 315000 at today's — the loser printed its wait, then read that fill end
12.4 s old and kept it. Both exited 0, and every one of the winner's `(id, l, t)` keys was
distinct — one fill, not two.

Seeding a clone on a recreate rather than cloning a filled template costs the fill — around
315000 rows in a few seconds against a `TEMPLATE` clone's file copy — and two things buy that. The
demo writer appends to the archive, so a template kept across sessions would carry the previous
session's live rows: its extent would stand a little further from its seed each time, the minimap
would widen, and the window the chart opens on would differ from the one the last session saw. A
bench that drifts is a bench whose reading cannot be trusted. And the fill end moves with the wall
clock, so a template built once freezes that end at the moment it was built and hands the demo writer
an archive as stale as itself, which `StaleArchiveGuard` refuses once it is more than five minutes
back: the stand would come up carrying history and never grow a live row.

The row count is not a constant, so no six-digit figure is reproducible under the default. The raw
layer holds about 272 000 rows for a one-day span at these parameters, while the coarse layers follow
the calendar periods the span covers, and a day-long span ending at an arbitrary instant straddles
two calendar days and one more calendar hour than one ending at midnight. A midnight end is about
315 000 rows across the four layers — the standard slice's own figure — and a default run lands a
few hundred either side of it, in a few seconds.

**Where the fill ends is a choice `-SeedEnd` makes**, and each choice buys a different reading. The
default ends the fill at the script's own wall clock, so the archive meets the demo writer's live
rows without a gap and the stand reads as a continuous installation. An explicit past value —
`pwsh scripts/bench-demo.ps1 -SeedEnd 2026-08-01T00:00:00` — is the stale-past bench: an archive
whose last sample predates the opening window is what distinguishes a chart that seeds its window
from the extent from one that opens on the wall clock and never reaches the data. A fill up to now
hides that distinction, and the demo writer refuses a past fill outright rather than appending the
hole into it, so the stale-past reading is a viewer-only one. It is taken from a command line rather
than from a Rider button, because both buttons converge the stand first and a stale archive is what
that convergence recreates:

```powershell
pwsh scripts/bench-demo.ps1 -SeedEnd 2026-08-01T00:00:00
dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir SemiPlot/Artifacts/bench-config
```

**The connection file is rewritten on every run**, into `SemiPlot/Artifacts/bench-config/`, which
git ignores. Its `source_time_zone` carries the identifier of the machine the script ran on, because
the demo writer writes that machine's local wall clock: a zone naming anywhere else puts the live
edge one offset from where the viewer looks, and that shows as a chart which never advances while
the log reads rows normally. Regenerating the field is what makes that state unreachable rather than
merely documented. The identifier goes in as Windows names it —
`TimeZoneInfo.FindSystemTimeZoneById` resolves a machine's own identifier on that machine, so no
IANA conversion stands between the two.

`docker logs semiplot-bench` carries the provisioning: a container that reached a serving port ran
`semibase bench` to completion, because the init script's `set -e` and the entrypoint's own make a
failed provisioning and a dead container one event.

A site's own installation is the other shape this bench wears: `StartupOptions` defaults the
connection file and the log to the `C:\DISTR\` tree, which is what a default-paths rehearsal
exercises. The script stays clear of both, so a rehearsal and a demo share one machine without
colliding.

What the server can be asked afterwards, which needs no screen:

| Question | Where the answer is |
| --- | --- |
| Did the application reach the archive at all? | `pg_stat_activity` carries `semiplot_reader` connections while it runs |
| Did it read the catalogue? | `pg_stat_user_tables.idx_scan` on `semiplot_tags` |
| Did it read real history, and from the seeded span? | `idx_tup_fetch` on the partitions the fill landed in, `tp<YYYY>m<MM>d<DD>` — two of them unless `-SeedEnd` is midnight, since a day-long fill ending at an arbitrary instant straddles two calendar days. Under the stale-past bench a window left on the wall clock fetches nothing, because no partition holds those hours; under the default fill that window lands inside the newest partition and fetches plenty |
| Did any read fall back to a sequential scan? | `seq_scan` on the same partitions, which the `EXPLAIN` guard forbids |
| Which failure did the operator get? | `C:\DISTR\Logs\SemiPlot\semiplot.log`. A clean start writes nothing at the default `Warning` floor; every startup failure writes its error and a `[FTL]` line |
| Did the live edge reach the chart? | Run `--follow 1` against the same database and watch the chart with **Sticky** on. The writer moves every layer's seam, not only the raw layer's, so the question is answerable on a wide window too. `pg_stat_user_tables.idx_tup_fetch` on the partitions the window covers rises every poll interval; the log at `--logging-level debug` carries one realtime line per tick with the row and sample counts, and the writer's own line reports the raw and coarse rows it wrote |

A startup failure opens a window and waits, so a run under a timeout returns that timeout's own exit
code rather than the application's. The log line, not the exit code, is what says which failure it
was.

The failure states are forced from outside the application: stop the container for an unreachable
server, rename `semiplot_tags` for an unfinished provisioning, change the password in the connection
file for a refused login, and delete the catalogue rows for an empty catalogue — which is a normal
start, not a failure, and writes no error at all.

What this bench cannot answer is what the curve looks like: whether the ladder's chosen layer is the
right one by eye, and whether the window is legible. Those wait for the demo stand. Whether a break
renders as a break is answered instead by the render guard below, at the rasteriser rather than on a
screen, and whether the archive's own break survives the whole path to that rasteriser is answered by
`BreakRenderArchiveJourneyTests`.

### Running it from Rider

`.run/` at the repository root tracks the buttons over the recipe — the root is the directory the
solution opens at, so it is where Rider looks. Nothing machine-dependent lives in them — the
container's role passwords are the fixture's own public constants and the port is the documented
55432, while the one machine-dependent input, the time zone, lives only in the generated connection
file.

| Configuration | What it does |
| --- | --- |
| `Bench up` | Runs the script on its own. Both children below run it first, so this button is for converging the stand without starting anything |
| `Bench down` | Removes the container and the generated connection file |
| `Demo writer` | The seeder in `--follow 1` against `semiplot_app`, with `Bench up` as a before-launch task |
| `Viewer (bench)` | `SemiPlot.UI` with `--config-dir` on the generated file and `--logging-level debug`, with `Bench up` as a before-launch task |
| `Live demo` | A compound of the last two: the stand is this one button |

**`Bench up` is a before-launch task of both compound children, not of the compound.** A compound
carries no before-launch list of its own, and it starts its children in parallel, so both run the
script at once. The mutex serialises the two and the freshness check makes the second one find its
work already done. What that buys is a precondition nothing has to remember: a cold `Live demo`
converges the stand before either child starts, and restarting only the viewer against a running
writer finds a live archive, keeps it and returns in seconds.

**Stopping the viewer leaves the writer running**, because a compound stops its children
independently. That is the shape the repeated check wants: restarting the viewer against an edge
that never stopped moving is the loop, and the writer's `Ctrl+C` path ends between appends rather
than inside one.

## The headless render and input guards

Three classes in `SemiPlot.Tests` pin the two things a rendering-stack version bump can change
without announcing it: how a gap is drawn, and how a pointer reaches a handler. They are written to
survive a version bump unchanged, so a stack that behaves differently after one surfaces as a
failing test rather than as a screenshot nobody took.

| Class | Drives | Asserts |
| --- | --- | --- |
| `UI/Chart/ChartGapRenderTests` | ScottPlot's rasteriser, no Avalonia | a `NaN` column leaves the rendered line broken, and a continuous series leaves no such break |
| `UI/Chart/ChartPointerInputTests` | headless pointer events into `TrendChartView` | a drag pans the navigation window, a wheel zooms it, a capture loss ends the drag |
| `UI/Minimap/MinimapPointerInputTests` | headless pointer events into `MinimapView` | a drag on the strip moves the chart's window to each pointer fraction, a move after release moves nothing |

### What the render guard sees

`Plot.GetImage(width, height)` returns a `ScottPlot.Image` whose `GetArrayRGB()` is a
`byte[row, column, channel]`, channel 0 red, 1 green, 2 blue. `Plot.RenderInMemory(width, height)`
is that same call with the image dropped, so `Plot.RenderManager.LastRender.Layout.DataRect`
describes exactly the render the pixels came from — one rasterise, one coordinate system, no second
render to disagree with it. `Plot.GetPixel(Coordinates)` maps a time to its pixel column. SkiaSharp
does the drawing with no Avalonia in the loop, so the render guard is a plain `[Fact]` and needs no
headless application.

Detection is by colour band, never by byte: a column of the data area either carries pen-coloured
pixels or it does not, and "pen-coloured" is a dominance test across the channels rather than an
exact RGB match. Antialiasing, font metrics and theme changes move bytes without moving that answer,
which is what lets the assertion survive a version bump that legitimately moves pixels. A golden
image does not survive one: it fails for benign reasons and gets regenerated at the bump, at which
point it has verified nothing about the transition.

### What the pointer guards drive

Headless input is `Avalonia.Headless.HeadlessWindowExtensions` on a `TopLevel`:
`MouseDown(TopLevel, Point, MouseButton)`, `MouseMove(TopLevel, Point)`,
`MouseUp(TopLevel, Point, MouseButton)` and `MouseWheel(TopLevel, Point, Vector)`, each with an
optional trailing `RawInputModifiers`. Each posts a raw input event into the headless window and
pumps the dispatcher, so hit testing, pointer capture and event routing all run for real. Their
points are window-client coordinates, so a position computed inside a control is translated out of
that control's space with `TranslatePoint`.

The pump is a loop, not a fixed sequence: `HeadlessWindowExtensions.RunJobsOnImpl` runs jobs and
forces a render timer tick up to ten times, until nothing is left at
`DispatcherPriority.MinimumActiveValue`. It does that twice — once before the raw input event and
once after it — so the phase following an input event renders too, and a single helper call can
drive up to twenty render ticks. A headless timing difference that appears with no source change
starts there.

Two things must hold before any coordinate means anything:

1. The window is shown and laid out, so the view has bounds.
2. The plot has rendered once, so `LastRender.Layout.DataRect` is populated — the view's
   pixel-to-time maths reads it through `Plot.GetCoordinates`.

The headless platform draws nothing (`UseHeadlessDrawing`), so that first render is forced through
ScottPlot with `Plot.RenderInMemory` at the plot control's own size, never through Avalonia.

Capture loss has no headless entry point of its own. `IPointer.Capture(null)` on the pointer taken
from the pressed event is the path `Pointer.PlatformCaptureLost` itself routes into, and it leaves
the pointer free, so a follow-up move still hit-tests and can show the drag is gone rather than the
event swallowed. The guard therefore covers `Capture(null)`, not `PlatformCaptureLost`: a version
that reroutes `PlatformCaptureLost` away from `Capture(null)` leaves the guard green while the
platform's own deactivation path regresses.

### A test that builds `TrendChartView` needs a UI scheduler that can defer

`TrendChartViewModel` builds `RedrawRequested` as
`Sample(33 ms, uiScheduler).ObserveOn(uiScheduler)`, and `TrendChartView` subscribes to it the
moment a `DataContext` arrives. `Sample` on `ImmediateScheduler.Instance` calls `SchedulePeriodic`,
which blocks the calling thread forever. The UI scheduler for a test that constructs the view is
therefore anything except `ImmediateScheduler.Instance`. Two choices work:
`AvaloniaScheduler.Instance`, which is what `App.InitializeServices` passes in production and what
the pointer guards pass; and `Microsoft.Reactive.Testing.TestScheduler`, which is what
`UI/Chart/TrendChartViewTests` passes for both of its schedulers.

**The symptom is a silent hang, not a failure.** The test host prints nothing at all and never
exits; a hang dump shows the thread parked in `Scheduler.SchedulePeriodicStopwatch.Start` under
`TrendChartView.OnDataContextChanged`.

Every view model a test builds with `AvaloniaScheduler.Instance` holds a 33 ms periodic dispatcher
timer until it is disposed, on the dispatcher the whole headless run shares, so the tests dispose
theirs inside the test body.

### What the guards do not cover

The Win32 backend — windowing, DPI, real cursor changes, the render-thread interplay — is exercised
by nothing headless. Nor is the desktop `AppBuilder` chain: the headless platform supplies its own
text shaper and its own drawing, so a desktop-only registration the chain is missing passes every
headless test and fails at `AppBuilder.Setup` in the running application. That failure mode is the
application bench's, not a guard's. Visual legibility is not a machine question at all, and waits
for the demo stand.

`ChartGapRenderTests` reaches SkiaSharp through ScottPlot with no Avalonia in the loop, so across a
rendering-stack bump it guards the ScottPlot half alone; the Avalonia half rests on the two pointer
guards. The halves are not interchangeable, and the ScottPlot 5.1.59 bump shows why: the plot
control began marking every wheel event handled, which killed wheel zoom in the application and
moved no pixel the render guard reads. `ChartPointerInputTests` is what failed — its wheel test —
while both render assertions stayed green.
