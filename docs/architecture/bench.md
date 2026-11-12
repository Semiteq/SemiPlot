# The seeded bench

The data-source slices need a PostgreSQL archive that looks like a Simple-Scada 2 archive and holds
the same rows on every machine. This document is that bench as it exists. What the vendor's archive
is remains in `scada-archive.md`; what SemiPlot reads from it remains in `data-integration.md`.

## Ownership

| Piece | Owner |
| --- | --- |
| Database, roles, grants, default-privileges chain, the configuration tables, `public.trends` with `tpdefault` | `semibase bench` (`github.com/Semiteq/SemiBase`), carried in the bench image |
| The daily partitions and the rows | `SemiPlot.Tools.ArchiveSeeder`, connected as `scada_writer` |
| The catalogue rows in `semiplot_tags`, `semiplot_groups` and `semiplot_pen_groups` | `SemiPlot.Tools.ArchiveSeeder` over the admin connection, written once when the stand comes up |
| The recipe and operator markers in `semiplot_markers` of a `shifts` seed | `SemiPlot.Tools.ArchiveSeeder`, connected as `scada_writer` (`#the-shift-schedule`) |
| The moving live edge of a demo archive | `SemiPlot.Tools.ArchiveSeeder --follow`, connected as `scada_writer` |
| Template build, the clones, teardown | `SemiPlot.Tests.Integration` |

The archive table is the provisioner's, not this repository's: nothing here transcribes the vendor's
DDL. The seeding run creates one day partition per calendar day of its span and no other relation,
and issues no `DROP`. An absent `public.trends` is a failure naming the provisioning that did not run; a table already
carrying rows or day partitions is refused. Two things drop databases: the test fixture, only ones it
created itself, and `converge`, which recreates the `semiplot_` database `--connection` names with
`DROP DATABASE IF EXISTS ... WITH (FORCE)` (`#the-converge-verb`).

## The standard slice

One day, 10 pens, seed 1, change interval 5 s, 4 breaks, exclusive end `2026-01-02T00:00:00`.
The end is fixed rather than floating, so two runs of the same seed produce the same archive. Its
shape is pinned by `RawLayerGeneratorTests` — determinism, the absolute lattice, the break holes and
the row-pair shape — and the waveform itself is not. The raw layer is about 307 000 rows and all
three layers about 355 000, landing in one day partition. Pens are taken round-robin across the
catalogue's groups, so a slice spans more than one group and more than one value range.

The size has a floor: `ExplainPlanTests` loses the poll statement's index plan to a sequential scan
under about 500 rows, and
`PostgresHistoryReadTests.TheMinuteLayerReturnsFewerColumnsThanRawOverTheSameWindow` needs the raw
layer denser than the minute layer inside its window.

## What the generator emits

Layer `0` only; the coarse layers are derived from it.

- **One lattice, written by the seeding run and by the demo writer alike.** A change sits at
  `index * intervalTicks` from absolute tick zero, where the interval is `--change-seconds` rounded
  to whole milliseconds, and its value is `SyntheticValueWalk.Value(seed, penId, index, min, max)`,
  a pure function of its inputs. A pen whose `LogScaleOnStart` is set takes
  `SyntheticValueWalk.LogValue`, which walks the same function between `log10(min)` and `log10(max)`
  and takes `10^v`, clamped to `min..max`, so the walk moves in decades rather than in value; the
  lattice is the same. `RawLayerGenerator` (run by run between the breaks) and
  `LiveTailGenerator` (window by window) both emit through `RawLayerGenerator.WindowRows`, so a
  follow run resuming at the archive edge continues the lattice the seeding wrote.
  `SharedLatticeTests` goes red if the two are split again.
- A change carries its pre-anchor: the previous value one poll interval (100 ms) earlier, then the
  new value at the change tick — the vendor's two-rows-per-change shape. A change interval no wider
  than the poll interval carries no anchor.
- **A slow pen changes on every k-th lattice point and writes nothing between.** A pen whose
  `ChangePeriod` is set (the `Setpoints` group: 5 and 30 minutes) has the stride
  `k = max(1, ChangePeriod / intervalTicks)`, whole intervals only: at 0.5 s that is 600 and 3 600, at
  the standard slice's 5 s 60 and 360, and a period the interval does not divide changes slightly more
  often than stated. A change sits on an index that is a multiple of `k` and carries the walk's value
  at that index; its anchor carries the value of the previous change, `k` indices back. The pen writes
  no row between two changes, so the reader sees an unchanged value, never a gap. Both generators
  apply the stride inside `WindowRows`, so a follow run continues a slow pen exactly where the
  seeding left it.
- Timestamps carry whole milliseconds only, matching `timestamp(3)`.
- The lattice carries no per-pen phase, so every pen changes at the same instants. A defect that only
  shows when two pens carry distinct timestamps needs a test that builds its own rows.
- Three quality codes and no others: `0`, `16`, `32`.

## Breaks

A break is the SCADA project stopped: no rows anywhere in the interval, the last row before it
marked `32` and the first row after it `16`. Breaks hit every pen at the same instants. The resume
row is the first lattice point at or after the break's end and carries no pre-anchor.

Each break takes an equal slot of the span, lasts 3 to 10 minutes and leaves at least 5 minutes of
archiving on either side, so every break empties at least one whole calendar minute — the empty
period the thinner has to survive. A span holds at most one break per 20 minutes: 72 in a day, and
`BreakPlan` refuses a `SeederOptions.BreakCount` above that. The seeder states no option for it:
`SeederOptions.DefaultBreakCount` breaks fit any span the command line can ask for. These bounds bind
the `continuous` schedule only; under `shifts` the off periods between sessions are the breaks
(`#the-shift-schedule`).

Both markers land on lattice rows, so with breaks every archiving run must hold at least two lattice
points. The tight run is the first or the last, which `BreakPlan` guarantees only `MinimumRun`;
`RawLayerGenerator.Generate` refuses a `--change-seconds` that leaves a run shorter and names the
run. For an ordinary pen every lattice point is a change, so the markers go on change rows. A slow pen
may hold one value across a whole run, so it writes its held value at the run's last lattice point
before a break and at the first one after it, and the markers go there: the vendor writes every tag at
the project stop and start instants (`scada-archive.md#quality-and-gaps`). Those two rows repeat a
value, so they carry no anchor.

## The shift schedule

`--schedule shifts`, a `converge` key (`#the-converge-verb`), gives the span a plasma tool's working
pattern. `ShiftSchedule` is pure in the seed and the day, and draws every bound as a lattice index, so a
session and a run start and end on lattice points at any `--change-seconds`.

- A day is on when it is a weekday outside the maintenance week and outside the random off days. The
  maintenance week recurs every 26 calendar weeks at a phase the seed draws, so a 183-day span holds
  one. A further 4% of weekdays are off at random.
- An on day holds one session. It starts between 07:00 and 08:30 and lasts 6 to 10 hours.
- A session holds recipe runs of 6 to 18 minutes with pauses of 4 to 20 minutes between them, and one
  pause in ten is 40 minutes longer. About one run in 15 is interrupted. A run that would pass the
  session's end is not drawn.

**The schedule clips the span.** `ShiftSchedule.Clip(Start, End)` opens the generated span at the first
session start at or after `Start`, and leaves out a session that cannot hold two changes before `End`.
The last session runs to `End` whatever the clock, so the demo writer's `StaleArchiveGuard` accepts a
stand converged at any hour. The off periods between sessions are the `BreakPlan`'s breaks: nights,
weekends, the maintenance week and the off days. The sessions are its runs. A session after the
first therefore opens with `q = 16`, and a session before the last closes with `q = 32` (`#breaks`).

**Values.** Inside a recipe run a pen walks the lattice as under `continuous`. Between runs it holds an
idle level, `min + 0.05 * (max - min)` of its stored pair. `LatticeValues` decides both at the held
index, so a slow pen's anchors and strides stay on the lattice. Every lattice point inside a session
writes a row, idle ones included.

**Partitions.** `PartitionScript.CoveredDays` takes the calendar days of `[Start, End)`, not the days
that hold rows. Under `shifts` the weekends, the off days and the day before the first session carry
empty partitions: the six-month stand has 184 day partitions and rows in 122 of them
(`#the-six-month-stand`).

**Recipe markers.** After the archive `COPY` commits, `ShiftMarkerWriter.RecipePlan` plans two markers
for every run that opens inside the span and closes before `End`: `RecipeStart` at its start, and
`RecipeEnd`, or `RecipeInterrupted` for an interrupted run, at its end. Both are locked and carry the same
label, one of five recipe file names. A run still open at `End` walks in the rows and has no marker.

**Operator markers.** `ShiftMarkerWriter.OperatorPlan` plans the markers an operator would have left:
`User` kind, unlocked. A calendar week draws two or three, each on an on day of its own and at a whole
second inside that day's session; a week with fewer on days takes one per on day, and the maintenance week
takes none. A marker before the generated span opens, or at `End` and later, is dropped. The draws depend
on the seed and the calendar alone, so a later `End` adds markers and moves none. A marker carries no label
(`NULL`) at a 15% draw; the others take one of seven Russian labels from `OperatorMarkerLabels.txt`, a
resource of the seeder, because the C# source stays ASCII (`ui-text.md`).

`ShiftMarkerWriter` writes both plans in one binary `COPY`. It connects as `scada_writer`, the role the
SCADA writes its markers as, and binds `t` as `timestamptz` from the archive's wall-clock time through the
zone it is given, `TimeZoneInfo.Local` in a seeding run and in `converge`. A `continuous` seed writes no
marker of either kind, and a seeding run, which has no `--schedule`, is always `continuous`.

`ShiftScheduleTests` pins the schedule, `ShiftsGenerationTests` the rows and quality marks it generates,
and `ShiftMarkerWriterTests` both marker plans, the operator plan over a 183-day span. `ShiftSeedTests`
seeds 14 days into a provisioned clone and reads both kinds back through `PostgresMarkerStore`; one more
marker goes through a writer given `Europe/Berlin`, so a host in UTC still checks the conversion.

## The demo writer

`--follow <seconds>` runs the seeder as a demo writer: it appends to an archive somebody else seeded,
so it plants no break and fills no tag catalogue. `--end`, `--days` and `--admin-connection` are
rejected in this mode. `--pens`, `--seed` and `--change-seconds` mean what they mean in a seeding run.
`--follow` and `--change-seconds` each take a finite number of seconds above 0 and at most one day,
`FollowOptions.MaximumSeconds` (86 400), in every run.

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=127.0.0.1;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --follow 1 --change-seconds 0.5
```

Every tick appends the raw rows of the window since the previous tick, thins them into the coarse
layers and prints both counts; `Ctrl+C` stops the loop where it waits, never inside an append.

To see a live break on the stand, insert one raw row for a pen in view as `scada_writer`, with `l = 0`,
`q = 32` and `t` past the newest row: the line ends at its value and resumes at the writer's next row
(`data-integration.md#realtime`).

- **The window is `after < t <= to`.** `LiveTailGenerator.Generate(options, after, to)` takes the
  archive's edge on the first tick and the previous tick's own instant after that. Both are instants
  the archive already accounts for, so consecutive windows partition the lattice: a restart writes no
  row twice — a `COPY` has no conflict handling, and the edge sits on a lattice point — and leaves
  no hole, since the next lattice point is inside one change interval. `FollowRestartTests` performs
  the restart against a database; `SharedLatticeTests` performs it in memory.
- **`StaleArchiveGuard` bounds the first tick.** It reads `max(t)` once and refuses an archive more
  than `MaximumAge` (five minutes) behind the clock, naming `converge` as the refill:
  the first tick writes everything between the edge and now, and against an archive filled weeks ago
  that would be weeks of rows and a partition per day. An empty archive is accepted and the loop
  starts at the clock.
- **Every layer moves, each on its own cadence.** `CoarseFlush` works one layer at a time on a
  connection of its own: for every period the tick leaves behind it reads the finer layer's rows,
  runs `LayerThinner.Thin` over them and inserts the result, and an opening-row `INSERT` writes the
  open period's first raw row. Both inserts end with `ON CONFLICT DO NOTHING`, so a period's opening row is
  written once and later ticks inside it report `0 coarse`. The opening row is what keeps every
  layer's seam inside the open period, which `FreshTail`'s clamp requires (`data-integration.md`,
  Layer ladder): with `--change-seconds` dividing the period the seam sits exactly at the period
  start and the distance to the live edge peaks at exactly one period, which the non-strict
  comparison keeps.
- **The clock is the machine's local wall clock with its `Kind` stripped**, because the archive
  column holds the SCADA host's naive local time.

## Thinning into the coarse layers

The seeder writes the minute and hour layers, `1` and `2`, and no day layer. Both hold verbatim copies
of raw rows: first, last, minimum and maximum of the period, deduplicated when they coincide, plus
every marker row. The SCADA keeps only the minimum and the maximum (`scada-archive.md#layers`); the
bench keeps the first and the last as well, because the opening-row write of `CoarseFlush` relies on
the closed flush selecting the first row. `LayerThinner.Thin` is the only implementation, and
`l=2 ⊆ l=1 ⊆ l=0` holds by construction.

A seeding run thins every layer from the raw rows it generated, one calendar day at a time.
`SeedFiller` yields each day's raw rows and then that day's thinned rows into one
`ArchiveWriter.WriteAsync`: one `COPY` in one transaction, so the seeded-archive refusal holds. A minute
or an hour period never crosses midnight, so thinning per day equals thinning over the span, and the
run holds about one day of rows rather than the span. The demo writer thins the minute
layer from the raw rows and the hour layer from the minute layer, which is exact: a period's minute
rows already carry that period's first, last, minimum and maximum, and every marker. The invariant it
rests on is the order of `CoarseFlush.FlushAsync`, minute, then hour inside one call, so the minute
layer of a closing period is complete before the hour layer reads it.

Ties on value resolve to the **earliest** row; the vendor keeps the **later** one
(`[MEAS:dump-20260805]`). The difference moves only the abscissa of a point. Flipping it waits on the
open calendar-versus-flush-window question; `LayerThinner.PeriodStart` is the one place period
alignment lives.

## The test bench

`SemiPlot.Tests.Integration` runs on a Linux CI runner.

- One server per run: a container start costs seconds while a `CREATE DATABASE` costs under one.
- One provisioned source database per server, `semiplot_provisioned`. Everything else is a
  `CREATE DATABASE ... TEMPLATE` clone of it, which copies table ownership, `relacl` and
  `pg_default_acl`.
- One seeded template per run, `semiplot_bench`, itself a clone of the provisioned source.
- One clone per consumer. `CloneSource` names the source: a class that reads the seeded rows clones
  `semiplot_bench`, a class that writes its own rows clones `semiplot_provisioned`. The `SeededArchive`
  class fixture gives a whole class one clone; `ClonedArchiveTest` gives one per test method.
- Every read in the container tests connects as `semiplot`, so a grant that never reached the role
  fails here instead of on commissioning day.

**The container is the only path, and it is disposable.** The resource reaper deletes the built
image `semiplot-bench:test` with the session; the container, its volume and every database die with
it. The images the run *pulled* — `ghcr.io/semiteq/semibase:latest` and the base image — stay,
because re-pulling every run would put the registry on the critical path.

## Where the provisioning comes from

The provisioner is a layer of the bench image, not a binary on the machine.
`SemiPlot/bench/Dockerfile` copies `/semibase` out of `ghcr.io/semiteq/semibase:latest`
onto the base image `PostgresContainerFixture.BaseImage` names and places `provision.sh` in
`/docker-entrypoint-initdb.d/` with mode 0755. The entrypoint runs it while `initdb` is still in
progress, so `semibase bench --host /var/run/postgresql --database "$SEMIPLOT_PROVISIONED_DATABASE"`
goes over the unix socket before the published port opens. `set -e` in the script and in the
entrypoint make a failed provisioning and a dead container one event; `docker logs` carries the
reason.

The fixture builds that image in-process from the `bench/` directory copied to the output
directory, under the fixed tag `semiplot-bench:test`. Readiness is the wait strategy's `psql`
against `public.trends` over TCP — the entrypoint's temporary server listens on the unix socket only
— so a container whose provisioning did not complete never becomes ready. The build, the start and
the readiness wait share one two-minute bound, `PostgresContainerFixture._startupBound`, so a bench that
never comes up fails rather than hanging `SemiPlot.Tests.Integration.exe`. The pull ahead of them runs
under a two-minute bound of its own, and a pull that runs out of it is reported like any failed pull.

`PostgresContainerFixture` maps the runtime's `localhost` host to `127.0.0.1` and passes any other host
through, so a remote `DOCKER_HOST` that reports a hostname is not a supported setup: the connection
loader accepts only an IPv4 host.

A missing container runtime is never a pass and never a skip: `InitializeAsync` lets the exception
through, and xunit fails every test of the collection with `TestPipelineException`.

**`latest` is a moving tag on purpose.** Delivered installations update only the provisioner, so the
pair worth testing is the newest `semibase` with the current reader. The image build pulls a `FROM`
image it lacks and never re-pulls one it has, so the fixture runs
`docker pull ghcr.io/semiteq/semibase:latest` ahead of the build (`DockerCli.PullProvisionerAsync`),
which is the one step that moves the tag. A failed pull — no registry route, no `docker` CLI on
`PATH` — is one `[bench]` line on standard error and the build goes on with the cached image;
only a machine with neither route nor image fails. When an unchanged commit fails after the tag moved,
`docker image inspect ghcr.io/semiteq/semibase:latest` names the digest the run built over.

## The application bench

The container tests exercise the provider and the journeys exercise the composed application; the
application bench is where a person looks at the chart. `SemiPlot.AppHost` owns the whole stand: the
bench container, the converge job, the demo writer and the viewer start in dependency order and stop
together. A viewer window started from `File` -> `New window` or `Restart` is not among them
(`#the-demos-directories`).

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost
```

or the `Live demo (Two days)` run configuration, which runs the `http` launch profile (the profile carries
`ASPIRE_ALLOW_UNSECURED_TRANSPORT`, without which the AppHost refuses its http dashboard address). Either
way, stopping the AppHost stops the container: it runs under the AppHost's default
`ContainerLifetime.Session`, and DCP watches its parent process, so a Ctrl+C and a hard kill of
`SemiPlot.AppHost.exe` (Rider's Stop in Debug) both remove the container, the writer and the viewer the
stand launched within seconds. A window the viewer started itself, through `File` -> `New window` or
`Restart`, is a plain child process of that viewer: it keeps running against the removed container and
the removed configuration copy, and the operator closes it by hand. Its theme watcher reports one outage
when the stand deletes `%TEMP%\SemiPlot\ConfigFiles` and then tries to open the watch again every 5 s
against the missing folder, which is harmless (`overview.md#the-live-theme`). The JetBrains Aspire plugin
(`me.rafaelldi.aspire`) is optional; it adds per-resource debugging (attaching to the converge job or the
writer individually) on top of what the Aspire dashboard already shows. The AppHost injects the standard
OpenTelemetry and console-formatter environment variables into every project resource; neither the seeder
nor the viewer carries an OpenTelemetry SDK or the `Microsoft.Extensions.Logging` console provider, so
the variables are inert.

The `six-months` launch profile runs the same stand over the six-month archive
(`#the-six-month-stand`); the `.run/` configuration and the `.zed/tasks.json` task named `Live demo (Six months)` start
it, and so does

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost --launch-profile six-months
```

The profile differs from `http` in one variable, `SEMIPLOT_DEMO_DATASET=six-months`.
`DemoDataset.FromName` (`SemiPlot/SemiPlot.AppHost/`) reads it from the AppHost's configuration and
picks the keys `converge` and the writer take:

| Dataset | `converge` keys | Writer keys |
| --- | --- | --- |
| `one-day`, the variable unset | `--change-seconds 0.5` | `--follow 1 --change-seconds 0.5` |
| `six-months` | `--schedule shifts --days 183 --pens 50 --change-seconds 30` | `--follow 1 --pens 50 --change-seconds 30` |

The writer repeats the seed's pens and interval, so the live tail continues the seeded lattice on every
seeded pen (`#the-demo-writer`). Any other value stops the AppHost before a resource starts, naming the
two accepted ones.

There is no volume: `converge` recreates the archive on every start regardless of what a previous
session left, so a volume would carry nothing across runs, and a saved archive would sit behind the
clock past `StaleArchiveGuard`'s five minutes. Every stand start pays `initdb`, `semibase bench` and the
seed: the day slice, or about 50 s for the six-month archive.

### The converge verb

`converge` is the seeder's own bench-only verb, and it is what the AppHost runs to bring the stand's
archive up before the writer and the viewer start. Run by hand it needs a `--config-dir` that already
holds a copy of the tracked set, because it writes the connection section and nothing else. The copy
goes to the developer's own directory, not to the stand's `%TEMP%\SemiPlot\ConfigFiles`, which the
next stand start sweeps:

```powershell
New-Item -ItemType Directory -Force SemiPlot\Artifacts\dev-config | Out-Null
Copy-Item ConfigFiles\* SemiPlot\Artifacts\dev-config -Recurse -Force

dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- converge `
  --connection "Host=127.0.0.1;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Password=<super>" `
  --config-dir SemiPlot\Artifacts\dev-config
```

It first refuses a database `--connection` names unless the name starts with `semiplot_` and is not
`semiplot_provisioned` itself (`Converge.RequireBenchDatabase`), before it opens any connection. It then
waits for the admin connection up to 60 s, runs `DROP DATABASE IF EXISTS ... WITH (FORCE)` and
`CREATE DATABASE ... TEMPLATE semiplot_provisioned` against that database, seeds it up to `--end` or
this machine's clock with the `SeederOptions` its keys build (`ConvergeOptions.ToSeederOptions`), fills
the catalogue through `--admin-connection` re-pointed at the stand database, and writes
`connection/connection.yaml` under `--config-dir` with the bench `semiplot` role's fixed password and a
`poll_interval_ms` of 250.

| Key | Default | Values |
| --- | --- | --- |
| `--schedule` | `continuous` | `continuous`, `shifts` (`#the-shift-schedule`), by name, case-insensitive |
| `--days` | 1 (`SeederOptions.DefaultDays`) | whole days, at least 1 |
| `--pens` | 10 (`SeederOptions.DefaultPenCount`) | 1 to the catalogue's 52 |
| `--change-seconds` | 5 (`SeederOptions.DefaultChangeSeconds`) | as in a seeding run; the AppHost passes the writer's interval, 0.5 s for the one-day dataset and 30 s for the six-month one, so the seed and the live tail share one density |

Seed 1 and `SeederOptions.DefaultBreakCount` are fixed. `converge` without the three newer keys builds
the `SeederOptions` it built before them, which `SeederCommandTests` pins. It prints the plan before the
counts: the generated span, the schedule, the pens, `sessions N` under `shifts` in place of `breaks N`,
the partitions, the rows of each layer, `recipe markers N` and `operator markers N`.

The catalogue is written once, here. `TagCatalogWriter` upserts `semiplot_tags`, inserts the group
names into `semiplot_groups` and replaces each pen's rows in `semiplot_pen_groups`, all three in one
transaction: a half-written catalogue would draw pens into groups they left. The replacement runs both
ways — a membership the catalogue stopped stating goes, and a group left with no member goes with it,
so a rerun after a rename leaves no header with nothing under it. The admin connection is
what carries it, because `scada_writer` holds no privilege on those tables. Nothing rewrites the
catalogue afterwards — the demo writer fills none of it, and every test database is a clone of a
template that was already filled.

`SyntheticPenCatalog.Build` holds 52 pens over seven group names, all but one with a unit, a format mask and the
stored scale pair its waveform walks between. What reaches the table is the slice, not the whole
catalogue: `SeedFiller` hands the writer `RawLayerGenerator.SelectPens(options.PenCount)`, the
round-robin slice that is ten pens wide by default. Ten carries `Setpoint 01`, the 5-minute pen, to the
stand and the test bench, and still splits into the provider's four pen groups, which nine does not;
`Setpoint 02`, the 30-minute pen, enters at `--pens 13`. Four pens carry the states that slice would
otherwise never reach — `TwoGroupPenId` sits in `Heaters` and `Watchlist` at once, `HiddenOnStartPenId`
has `enabled_on_start = false`, `LogScalePenId` (4000, `Pressure 01`) spans `1e-6..1e-1` mbar under the
mask `0.0E+0` with `log_scale_on_start = true`, so the demo stand opens it on a log10 axis, and
`UncommissionedPenId` has no group, no unit, no mask and no stored pair, so it opens autoscaled.
`TagCatalogWriter` writes `log_scale_on_start` with the other settings columns.

That one file is all it writes. It overwrites the delivered `connection/connection.yaml` by name
rather than adding a second file beside it, because two files of one section folder carrying the
same keys is exactly what the section rule rejects (`overview.md`). The `app/` section it leaves
alone: `locale: ru` and `theme: light` come from the tracked set the AppHost copied in.

`BenchRoles` in the seeder is the one place the bench's role names and
passwords live; the container fixture reads them from there, and the AppHost repeats the same fixed
values as environment variables for the container, because an Aspire AppHost project cannot compile
against a project resource's own assembly.

### The six-month stand

The minimap and the marker navigation need six months of working days with recipe and operator markers.
The `six-months` launch profile builds that archive on every stand start
(`#the-application-bench`). By hand, `converge` builds it against a bench container on port 55432 and a
`--config-dir` copy as above:

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- converge `
  --connection "Host=127.0.0.1;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=127.0.0.1;Port=55432;Database=postgres;Username=postgres;Password=<super>" `
  --config-dir SemiPlot\Artifacts\dev-config `
  --schedule shifts --days 183 --pens 50 --change-seconds 30
```

Measured on 2026-11-10 on `033576d`. The machine: Intel Core i7-1355U, 10 logical processors, 31.5 GiB
RAM, Windows 11 Pro 10.0.26300; Docker Desktop 4.98.0, Engine 29.9.2, WSL 2 kernel 6.18.54.1, the VM
with 10 CPUs and 15.4 GiB; the image built from `SemiPlot/bench/Dockerfile` over `postgres:17-alpine`
(PostgreSQL 17.11) and `ghcr.io/semiteq/semibase@sha256:02ebe9b5`. The seeder ran as the Debug build's
executable with the arguments above, so the times exclude the build.

| Measure | Run 1 | Run 2 |
| --- | --- | --- |
| Wall time | 62.7 s | 58.8 s |
| Peak working set | 89.5 MB | 83.7 MB |
| Span | 2026-05-12 07:10:30 to 15:15:27 on 2026-11-10 | 2026-05-12 07:10:30 to 15:18:58 on 2026-11-10 |
| Layer 0 rows | 11 361 570 | 11 362 242 |
| Layer 1 rows | 6 942 738 | 6 943 074 |
| Layer 2 rows | 186 208 | 186 208 |
| Sessions | 122 | 122 |
| Recipe markers | 4 120 | 4 120 |

The runs differ only in the end, which follows the clock. The SQL checks and the size below were taken
on run 1's archive. `count(*)` grouped by `l` equals the printed counts. The raw rows fall on Monday to Friday only, on 122 days, and `max(t)` is
15:15:00, the last lattice point before the end. The markers are 2 060 `RecipeStart`, 1 928 `RecipeEnd`
and 132 `RecipeInterrupted`, all locked. The database holds 184 day partitions and `tpdefault`, and
`pg_database_size` is 1 786 230 451 bytes (1 703 MB). From 2026-05-12 to 2026-11-10 there are 131
weekdays, so 9 weekdays hold no session.

The viewer read run 2's archive at `--logging-level debug`, Release, with the demo writer running at
`--follow 1 --change-seconds 0.5` and its default 10 pens; the psql figures also read the writer's rows up
to 15:21:26. The server logged every statement
(`log_min_duration_statement = 0`); the psql figures connect as `semiplot` with `jit = off`, three runs
each, the first then the two warm ones.

| Read | Viewer | psql planning | psql execution |
| --- | --- | --- | --- |
| Archive extent, at start | two reads: bind 45 and 50 ms, execute 176 and 169 ms | 50.5, 10.7, 9.3 ms | 134.3, 84.6, 79.6 ms |
| Archive days, 2026-05-11 to 2026-11-10 | not read at start; only the time window panel and the export read it | 4.7, 4.4, 3.9 ms | 15.4, 15.0, 15.2 ms, 122 days, an index-only scan per day partition |
| Markers, 4 120 rows | execute 1.7 ms at start, 1.6 and 2.1 ms on the 5 s re-reads | 0.3 ms, then under 0.1 ms | 0.83, 0.79, 0.78 ms |
| Minimap band, `Hour` over the whole extent at 250 columns | 85 ms in `MinimapBandFeed`, 43 ms of it in the provider; 50 ms on the re-read a minute later | | |
| Chart, first `Raw` read, 14:20 to 16:20 local at 4 096 columns, 49 pens in 4 groups | 64 ms in the provider | | |

The chart's frame time comes from a follow capture with no pointer in the window. The chart's first read
started at 14:20 local and the newest marker sits at 14:23:30, so at most a few markers were in range:
the figure is the cost of holding 4 120 markers, not of drawing them. `dotnet-trace` ran for 30 s and
`scripts/perf/trace-shares.py --skip-seconds 10` read the last 20 s: 79 `RenderOnce` runs, mean 3.0 ms,
max 5.9 ms, spacing p50 286 ms. A later comparison repeats the same capture: follow, the writer at 10
pens and 0.5 s, 30 s, the first 10 s skipped. No drag capture was taken, because nothing on the bench drives a drag
without synthesised desktop input (`testing-strategy.md#frame-cost`).

#### The stand after the two-track minimap

Measured again on 2026-11-10 on `fb8739e`, the same machine and Docker, an image built again from
`SemiPlot/bench` over the same two base images, and the same `converge` command run as the Debug
executable: 32.4 s, 86.7 MB peak working set, a span from 2026-05-12 07:10:30 to 21:20:30 on
2026-11-10, 11 431 916, 6 977 996 and 186 808 rows in layers 0, 1 and 2, 122 sessions and 4 120
recipe markers. The two baseline `converge` runs reported the database ready in 0.3 and 0.2 s and this
one in 0.1 s,
so no server wait explains the shorter wall time; the gap was not investigated. The viewer, the writer
and the statement log ran as above, and the viewer column was read on the fresh seed, before any
`ANALYZE`. The psql figures were taken after `ANALYZE trends, semiplot_tags`, as for
`data-integration.md#read-cost`, with the shipped statement text and its parameters written in as
literals. The extent statement is unchanged since the baseline, and its 80-84 ms confirms the server
was in the same state as for the baseline's warm runs.
A pass before the `ANALYZE` read the extent at 108-154 ms and the full day spans at 220-313 ms, with
one first run of 3 112 ms; those figures are left out.

| Read | Viewer | psql planning | psql execution |
| --- | --- | --- | --- |
| Archive extent, at start | two reads: bind 49 and 52 ms, execute 151 and 153 ms | 6.7, 7.0, 6.8 ms | 79.7, 80.2, 83.8 ms |
| Archive days, 2026-05-12 to 2026-11-10 | not read at start | 3.4, 3.3, 3.3 ms | 13.9, 13.5, 14.2 ms |
| Day spans, 2026-05-12 to 2026-11-10, at start | bind 93 ms, execute 288 ms; 474 ms in the minimap's log line | 42.9, 14.3, 12.1 ms | 184.1, 169.3, 164.1 ms, 122 days |
| Day spans, 2026-11-10 alone | not observed: no midnight passed during the run | 12.2, 12.9, 16.3 ms | 4.8, 5.0, 5.8 ms |
| Day spans, 2026-11-09 to 2026-11-10, the range a rollover reads | not observed | 11.2, 13.1, 10.3 ms | 5.4, 7.0, 7.2 ms |
| Markers, 4 120 rows | execute 1.3 ms at start, 1.4 to 1.8 ms on the 5 s re-reads | | |
| Band, Day page, at start | the calendar day at `Minute`, 1 000 columns: 91 ms in `MinimapBandFeed`, 17 ms in the provider; then 07:00 to 22:00 at `Minute`, 1 000 columns: 9 ms, 7 ms in the provider; re-read every 54 s at 5 to 12 ms | 4.2, 0.3, 0.4 ms | 3.0, 2.4, 2.2 ms |
| Band, Fri 6 Nov over today's 07:00 to 22:00 bounds, `Minute`, 1 000 columns | | 0.9, 0.3, 0.4 ms | 1.2, 1.4, 1.8 ms |
| Band, Week page 9 to 16 Nov, `Minute`, 1 000 columns | not driven | 2.2, 0.5, 0.5 ms | 3.3, 3.0, 3.5 ms |
| Band, Week page 2 to 9 Nov, `Minute`, 1 000 columns | not driven | 2.2, 0.7, 0.7 ms | 6.0, 6.4, 5.8 ms |
| Band, Week page 2 to 9 Nov, `Hour`, 1 000 columns | | 0.7, 0.9, 0.6 ms | 0.8, 0.7, 0.7 ms |
| Chart, first `Raw` read, 2 h at 4 096 columns, 49 pens in 4 groups | 73 ms in the provider | | |

The strip was 1 000 columns wide. The Day page took 07:00 to 22:00, because the seed's last session
runs to `--end`, 21:20:30, and the writer continues it, so today's rows widen the page past the derived
hours. This run drove no window control, so the Week page was timed in psql only, as the pen the
viewer drew (2000) over Monday to Monday with the 604.8 s bucket of 1 000 columns.
`ChartNavigationController.LayerForWidth` picks `Minute` there: with one pen the `Minute` ceiling is
15 min x 1 000 = 250 h, above the 168 h week. The psql rows time the coarse statement only; on the page that holds the newest sample the viewer also issues the `Raw` fresh-tail
statement over the minutes past the newest `Minute` row.

The frame time comes from follow captures taken the same way as the baseline's, on the first seed and
on a second one (33.3 s, 90.1 MB, ready in 2.2 s, 122 sessions and 4 120 markers again). The uptime is
the window the script read, counted from the viewer's first log line:

| Capture | Uptime read | `RenderOnce` runs | Mean | Max | Spacing p50 | `EnvelopeLine.Render` |
| --- | --- | --- | --- | --- | --- | --- |
| Baseline, run 2 above | 32 to 52 s | 79 | 3.0 ms | 5.9 ms | 286 ms | 81.0 ms in 20 s |
| Second seed, early, `--skip-seconds 30` of 52 s | 31 to 53 s | 84 | 2.8 ms | 6.3 ms | 298 ms | 80.5 ms in 22 s |
| First seed | 4 min 6 s to 4 min 26 s | 80 | 3.8 ms | 8.2 ms | 295 ms | 147.1 ms in 20 s |
| Second seed, late | 8 min 40 s to 9 min | 79 | 4.1 ms | 6.0 ms | 298 ms | 202.8 ms in 20 s |
| First seed | 9 min 35 s to 9 min 55 s | 79 | 5.3 ms | 8.6 ms | 297 ms | 261.5 ms in 20 s |
| First seed | 10 min 7 s to 10 min 27 s | 78 | 5.0 ms | 8.5 ms | 296 ms | 239.2 ms in 20 s |

At the baseline's uptime the frame is unchanged: 2.8 against 3.0 ms on the mean. The frame then grows
with the viewer's uptime on both seeds, and the growth sits in `EnvelopeLine.Render`, which draws the
pens. That growth is independent of the minimap and navigation work:
`git log 033576d..fb8739e -- SemiPlot/SemiPlot.UI/Chart/` prints nothing, and the baseline has no capture past its first minute. The live tail filling the
window is the likely cause; it is not confirmed. The minimap's per-sample work runs on the UI thread,
not inside `RenderOnce`: `MinimapViewModel.OnNewestSampleMoved`, which calls `RefreshShadedRuns`, took
2.0 to 4.4 ms in each capture's read window. `MarkerNavigation.Runs`, which both the minimap and the
navigation bar call on every newest sample, took 1.9 ms in the early capture's read window, 10.3 ms
over its 5 to 52 s, and none in the four later 30 s captures. Recomputing the runs on every newest
sample costs no measurable frame time with 4 120 markers in the table.

#### The stand from the launcher

Measured on 2026-11-11 on the same machine and Docker, through
`dotnet run --project SemiPlot/SemiPlot.AppHost --launch-profile six-months`, Debug build.
The figures come from the converge resource's output and from `psql` against the stand database while
the writer and the viewer ran. The `converge` time is not a timed process, unlike the Debug-executable
runs above: it runs from the creation of the resource's output files in the DCP session directory to the
timestamp DCP put on its last line, so it includes the process start. The writer's first tick appended 96
rows, and every 30 s after it about as many.

| Measure | Value |
| --- | --- |
| `converge`, process start to its last line | 52.4 s, the database ready in 0.7 s |
| `dotnet run` start, build included, to the viewer's first log line | 70 s |
| Span | 2026-05-13 08:18:30 to 15:54:01 on 2026-11-11 |
| Rows, layers 0, 1 and 2 | 11 363 784, 6 943 656 and 186 240, 18 493 680 in all |
| Sessions, day partitions | 122, 184 and `tpdefault` |
| Recipe markers | 4 124: 2 062 `RecipeStart`, 1 929 `RecipeEnd`, 133 `RecipeInterrupted`, all locked |
| Operator markers | 63, all `User` and unlocked, 9 without a label |
| `pg_database_size` | 1 783 633 587 bytes (1 701 MB) |

The operator markers carry all seven labels: `Чистка камеры` 12, `Утечка He` 11, `Проверка вакуума` and
`Замена кольца` 8 each, `Новая партия` and `Калибровка MFC` 6 each, `Смена мишени` 3.

### The demo's directories

The stand consumes a copy of the tracked configuration set, never the original. `DemoDirectories`
(`SemiPlot/SemiPlot.AppHost/`) owns two fixed paths under the temporary directory, so a crashed run
is cleaned by the next start and no per-run suffix is needed — one stand runs at a time by
construction, on a fixed host port with `isProxied: false` and one database name.

| Path | Holds | Lifecycle |
| --- | --- | --- |
| `%TEMP%\SemiPlot\ConfigFiles` | A copy of the repository's `ConfigFiles/`, `converge`'s rewritten `connection/connection.yaml` included | Swept and recreated at start, removed on `ApplicationStopping` |
| `%TEMP%\SemiPlot\Logs` | `semiplot.log`, and the `semiplot_NNN.log` files it rolls into at 5 MB | Swept and recreated at start, kept after the stand stops |

`AppHost.cs` resolves the tracked set two levels above `builder.AppHostDirectory`, and
`DemoDirectories.Prepare` throws `DirectoryNotFoundException` when it is absent, so a missing source fails the AppHost at once
rather than producing an empty copy the viewer reports later. The AppHost passes
`%TEMP%\SemiPlot\ConfigFiles` as `--config-dir` to both `converge` and the viewer, and the log path
plus `--logging-level information` to the viewer, which needs all three keys.

A value saved through Edit -> Settings during a stand run does not survive it: `converge` rewrites
`connection/connection.yaml` on every run (`SemiPlot.Tools.ArchiveSeeder/Converge.cs`) and the stand
removes the configuration copy when it stops (`DemoDirectories.cs`).

`Logs` is swept at the next start rather than on stop: the viewer holds `semiplot.log` open through a
Serilog file sink declared `shared: true`, so deleting the directory during shutdown would race that
handle, and the log of a run that failed is the one thing worth keeping after the stand is gone. The
sweep deletes file by file and tolerates a file still held open, so a viewer that outlived its stand
cannot stop the next one from starting. The configuration sweep tolerates no survivor:
`DemoDirectories.RequireSwept` fails the AppHost start with an `IOException` naming every file the sweep
left, because such a file would merge with the fresh copy. `ApplicationStopping` does not fire when the AppHost is
killed rather than stopped; the sweep at the next start is the backstop for that path. A window started
from the viewer's `File` menu holds `semiplot.log` open the same way and can outlive the stand.

What the server can be asked afterwards, which needs no screen:

| Question | Where the answer is |
| --- | --- |
| Did the application reach the archive? | `pg_stat_activity` carries `semiplot` connections while it runs |
| Did it read the catalogue? | `pg_stat_user_tables.idx_scan` on `semiplot_tags` |
| Did it read history from the seeded span? | `idx_tup_fetch` on the partitions the fill landed in, `tp<YYYY>m<MM>d<DD>` |
| Did any read fall back to a sequential scan? | `seq_scan` on the same partitions, which `ExplainPlanTests` forbids |
| Which failure did the operator get? | `%TEMP%\SemiPlot\Logs\semiplot.log` on the stand, `C:\DISTR\Logs\SemiPlot\semiplot.log` on an installation. A startup failure writes its error and a `[FTL]` line, except the two that happen before the logger exists: a bad launch argument and an unusable `--log-file` are reported in the failure window alone (`SemiPlot/SemiPlot.UI/Program.cs`) |
| Did the live edge reach the chart? | Run `--follow 1` against the same database and watch the chart with **Sticky** on; at `--logging-level debug` the log carries one realtime line per tick |

The failure states are forced from outside the application: stop the container for an unreachable
server, rename `semiplot_tags` for an unfinished provisioning, change the password in the connection
file for a refused login, delete the catalogue rows for an empty catalogue.

Nothing machine-dependent lives in `AppHost.cs`: the role passwords are `BenchRoles`' public
constants, the port is 55432, the configuration and log paths come from `DemoDirectories`, and the
seeder and the viewer both take the machine's own time zone. The
`Live demo (Two days)` run configuration at the repository root's `.run/` is an `AspireHostConfiguration` over
`SemiPlot.AppHost`'s `http` launch profile, and `Live demo (Six months)` one over the `six-months`
launch profile (`#the-six-month-stand`). A launch profile name carries no space or parenthesis, because a
`.zed/` task hands its arguments to `pwsh -C`, which reads `(...)` as a subexpression.

The launchers that start the viewer alone - `.run/Debug.run.xml` and the `.zed/` run and debug
entries - point `--config-dir` at `SemiPlot/Artifacts/dev-config` through `$PROJECT_DIR$` and
`$ZED_WORKTREE_ROOT`, never at the stand's copy: `%TEMP%\SemiPlot\ConfigFiles` exists only while the
stand runs, and the stand has already started its own viewer against it. The developer creates that
directory once from `ConfigFiles/`, as `AGENTS.md` spells out; `SemiPlot/Artifacts/`
is gitignored, so the password filled in there stays out of the repository.

## The headless render and input guards

Six classes in `SemiPlot.Tests.Unit` pin what a rendering-stack version bump can change without
announcing it: how a gap is drawn, where a log axis puts a value, how a pointer reaches a handler, and
where a realised control sits in the strip, at what size and with which figures.

| Class | Drives | Asserts |
| --- | --- | --- |
| `UI/Chart/ChartGapRenderTests` | ScottPlot's rasteriser, no Avalonia | a `NaN` column leaves the rendered line broken, and a continuous series leaves no such break |
| `UI/Chart/LogAxisRenderTests` | ScottPlot's rasteriser, no Avalonia, through `ChartAxisBinder.Apply` | the 1e-4 plateau of a 1e-6..1e-2 log axis draws within 2 px of the data area's middle, and a plateau at `0` or under the minimum draws 2 px above its bottom row; it pins two ScottPlot behaviours, the bottom axis frame drawn over the data area's bottom row and a linear `YAxisBase.GetPixel` |
| `UI/Chart/ChartPointerInputTests` | headless pointer events into `TrendChartView` | a drag pans the navigation window, a wheel zooms it, a capture loss ends the drag, a click within the slop picks the nearest pen and never pans, a click on empty space or below the data area outside the time axis band keeps the active pen and drops its emphasis, a drag through empty space keeps it; Shift + wheel pans by a tenth of the window, Ctrl + wheel and Ctrl + drag shift only the active pen, the wheel over the active axis scales it about the cursor, a horizontal wheel leaves the window, a Ctrl + click changes nothing, and the press fixes the drag's action and its cursor; a click on the time axis opens the time window panel, picks no pen and keeps the emphasis, in delta mode too without placing a cursor, Escape in that panel closes the chart's flyout and writes nothing, a drag on the time axis pans and opens nothing, a Ctrl drag from the time axis shifts the active pen, and closing the window with a picker's calendar open throws nothing and writes the pending pair |
| `UI/Minimap/MinimapPointerInputTests` | headless pointer events into `MinimapView` | a drag on the detail track moves the chart's window to each pointer fraction of the page, a move after release reaches the strip and moves nothing, a window past the extent keeps its box inside the strip of its own page; a click on a weekend cell of the day calendar lands on the nearer working day's data, a click under the Week unit shows the week holding the day, a click past the last cell moves nothing, and a drag along the cells lands on each day under the pointer and a move after release on none |
| `UI/Minimap/MinimapHoverTests` | headless pointer events into `MinimapView` | a hover over the detail track shows a line at the pointer and its time centred under it in the tick row, inside the row at either end, where the time hides only the tick labels it covers; the line stays inside the strip at its right edge and keeps its fraction through a resize; a page turn relabels the time; a drag keeps the line under the pointer, and a move off the strip or an exit hides the line and the time; a hover over a day calendar cell rings it and names the day and its first and last sample in the tooltip, a day without rows says so, and leaving the row or passing the last cell clears both |
| `UI/Minimap/MinimapViewTests` | a realised `MinimapView` with a published band | the strip holds the band below the window box and no baseline, with the page's hour labels in a row under it; the band is sized to the strip, read over the page at the strip's own width, with figures in the pen's colour that a pan leaves as they are; the strip ticks each operator marker on the page; a click on either chevron steps the chart a working day; the strip spans the panel between the two chevrons, 8 px from each, directly under the chart, with its tick row under it, an 8 px gap and the day calendar across the strip's width under that, in a 97 px row that keeps its height when the extent fills its labels; the calendar takes one cell per day, fills the days with rows right-aligned at a 20 px pitch, draws two 19 px cells at the row's right end and no slab for a two-day archive, rings the page and follows a page step, stops a week's outline at the last day, regrades the newest cell only across a coverage step through a live sample, ticks the days of the operator's markers only, keeps the outline without day spans, and labels the months of a short archive with a Monday dropped where it would touch a month, or the months of a six-month archive with a separator before each start |

**The render guard.** `Plot.GetImage(width, height)` rasterises through SkiaSharp with no Avalonia in
the loop. `LogAxisRenderTests` builds a bare `Plot` and is a plain `[Fact]`; `ChartGapRenderTests` builds
its plot through `TrendChartViewModel`, a ReactiveUI object, and runs under `[AvaloniaFact]`.
`Plot.RenderManager.LastRender.Layout.DataRect`
describes that same render and `Plot.GetPixel(Coordinates)` maps a time to its pixel column.
Detection is by colour band — a dominance test across the channels — never by exact byte, so
antialiasing and theme changes move bytes without moving the answer. A golden image would fail for
benign reasons at every bump and verify nothing about the transition.

**The pointer guards.** `Avalonia.Headless.HeadlessWindowExtensions` (`MouseDown`, `MouseMove`,
`MouseUp`, `MouseWheel` on a `TopLevel`) post raw input and pump the dispatcher, so hit testing,
capture and routing run for real; points are window-client coordinates, translated out of the
control's space with `TranslatePoint`. Before any coordinate means anything the window must be shown
and laid out, and the plot must have rendered once through `Plot.RenderInMemory` at the control's
own size — the headless platform draws nothing. Capture loss is driven through
`IPointer.Capture(null)`, the path `Pointer.PlatformCaptureLost` routes into; a version that reroutes
`PlatformCaptureLost` leaves the guard green. A pointer test that measures a line's width calls
`ChartTestBuilder.InkRatio`, which renders the plot again at the size of `LastRender` and asserts that the
data area did not move, so the pointer maths after the measurement reads the same `DataRect` as before it.

**A test that builds `TrendChartView` passes `AvaloniaScheduler.Instance` or a `TestScheduler` as the
UI scheduler.** Over `ImmediateScheduler.Instance` a history result can land off the UI thread, and the
view's animation-frame request throws there (`testing-strategy.md#the-ui-scheduler-in-a-realised-view`).

**What the guards do not cover.** The Win32 backend and the desktop `AppBuilder` chain are exercised
by nothing headless; a desktop-only registration the chain is missing fails at `AppBuilder.Setup` in
the running application, which is the application bench's to catch. `ChartGapRenderTests` guards the
ScottPlot half of a bump alone and the pointer guards the Avalonia half: the ScottPlot 5.1.59 bump
marked every wheel event handled, killed wheel zoom, moved no pixel, and `ChartPointerInputTests` is
what failed. The headless platform also does not reproduce how Win32 reports a Shift-modified wheel or a
touchpad pinch; both are checked by hand (`trend-interaction.md#navigation--cursors`, the wheel map).
