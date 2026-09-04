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
| Template build, the clones, teardown | `SemiPlot.Tests.Integration` |

The archive table is the provisioner's, not this repository's: nothing here transcribes the vendor's
DDL. The seeder creates the day partitions its rows land in and nothing else, and never destroys.
An absent `public.trends` is a failure naming the provisioning that did not run; a table already
carrying rows or day partitions is refused. Only the test fixture drops databases, and only ones it
created itself.

## The standard slice

One day, 8 pens, seed 1, change interval 5 s, 4 breaks, exclusive end `2026-01-02T00:00:00`.
The end is fixed rather than floating, so two runs of the same seed produce the same archive. Its
shape is pinned by `RawLayerGeneratorTests` — determinism, the absolute lattice, the break holes and
the row-pair shape — and the waveform itself is not. The raw layer is about 272 000 rows and all
four layers about 315 000, landing in one day partition. Pens are taken round-robin across the
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
  a pure function of its inputs. `RawLayerGenerator` (run by run between the breaks) and
  `LiveTailGenerator` (window by window) both emit through `RawLayerGenerator.AppendWindow`, so a
  follow run resuming at the archive edge continues the lattice the seeding wrote.
  `SharedLatticeTests` goes red if the two are split again.
- A change carries its pre-anchor: the previous value one poll interval (100 ms) earlier, then the
  new value at the change tick — the vendor's two-rows-per-change shape. A change interval no wider
  than the poll interval carries no anchor.
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
`SeederOptions.DefaultBreakCount` breaks fit any span the command line can ask for.

Both markers land on real change rows, so with breaks every archiving run must hold at least two
changes. The tight run is the first or the last, which `BreakPlan` guarantees only `MinimumRun`;
`RawLayerGenerator.Generate` refuses a `--change-seconds` that leaves a run shorter and names the
run.

## The demo writer

`--follow <seconds>` runs the seeder as a demo writer: it appends to an archive somebody else seeded,
so it plants no break and fills no tag catalogue. `--end`, `--days` and `--admin-connection` are
rejected in this mode. `--pens`, `--seed` and `--change-seconds` mean what they mean in a seeding run.

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --follow 1 --change-seconds 0.5
```

Every tick appends the raw rows of the window since the previous tick, thins them into the coarse
layers and prints both counts; `Ctrl+C` stops the loop where it waits, never inside an append.

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

Layers `1`, `2` and `3` hold verbatim copies of raw rows — first, last, minimum and maximum of the
period, deduplicated when they coincide, plus every marker row. `LayerThinner.Thin` is the only
implementation, and `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0` holds by construction.

A seeding run thins every layer from the raw rows it generated. The demo writer thins the minute
layer from the raw rows and each coarser layer from the layer below it, which is exact: a period's
minute rows already carry that period's first, last, minimum and maximum, and every marker. The
invariant it rests on is the order of `CoarseFlush.FlushAsync` — minute, then hour, then day inside
one call — so the finer layer of a closing period is complete before the coarser layer reads it.

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
- Every read in the container tests connects as `semiplot_reader`, so a grant that never reached the
  reader fails here instead of on commissioning day.

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
— so a container whose provisioning did not complete never becomes ready. The pull, the build, the
start and the readiness wait share one two-minute bound,
`PostgresContainerFixture._startupBound`, so a bench that never comes up fails rather than hanging
`SemiPlot.Tests.Integration.exe`.

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
together.

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost
```

or the `Live demo` run configuration, which runs the `http` launch profile (the profile carries
`ASPIRE_ALLOW_UNSECURED_TRANSPORT`, without which the AppHost refuses its http dashboard address).
Either way, stopping the AppHost stops the container: it runs under the AppHost's default
`ContainerLifetime.Session`, and DCP watches its parent process, so a Ctrl+C and a hard kill of
`SemiPlot.AppHost.exe` (Rider's Stop in Debug) both remove the container, the writer and the viewer
within seconds. The JetBrains Aspire plugin (`me.rafaelldi.aspire`) is optional; it adds
per-resource debugging (attaching to the converge job or the writer individually) on top of what the
Aspire dashboard already shows. The AppHost injects the standard OpenTelemetry and console-formatter
environment variables into every project resource; neither the seeder nor the viewer carries an
OpenTelemetry SDK or the `Microsoft.Extensions.Logging` console provider, so the variables are inert.

There is no volume: `converge` recreates the archive on every start regardless of what a previous
session left, so a volume would carry nothing across runs. Every stand start pays `initdb`,
`semibase bench` and the day-slice seed.

### The converge verb

`converge` is the seeder's own bench-only verb, and it is what the AppHost runs to bring the stand's
archive up before the writer and the viewer start.

```powershell
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder -- converge `
  --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Port=55432;Database=postgres;Username=postgres;Password=<super>" `
  --config-dir SemiPlot/Artifacts/bench-config
```

It waits for the admin connection up to 60 s, then unconditionally `DROP DATABASE IF EXISTS ...
WITH (FORCE)` and `CREATE DATABASE ... TEMPLATE semiplot_provisioned` against the database
`--connection` names, seeds it with `SeederOptions` at the defaults (`--change-seconds` may override
the change interval; the AppHost passes the writer's 0.5 s so the seeded day and the live tail share
one density) up to `--end` or this machine's
clock, fills `semiplot_tags` through `--admin-connection` re-pointed at the stand database, and
writes `archive-connection.yaml` with the bench reader role's fixed password and
`TimeZoneInfo.Local.Id`. `BenchRoles` in the seeder is the one place the bench's role names and
passwords live; the container fixture reads them from there, and the AppHost repeats the same fixed
values as environment variables for the container, because an Aspire AppHost project cannot compile
against a project resource's own assembly.

What the server can be asked afterwards, which needs no screen:

| Question | Where the answer is |
| --- | --- |
| Did the application reach the archive? | `pg_stat_activity` carries `semiplot_reader` connections while it runs |
| Did it read the catalogue? | `pg_stat_user_tables.idx_scan` on `semiplot_tags` |
| Did it read history from the seeded span? | `idx_tup_fetch` on the partitions the fill landed in, `tp<YYYY>m<MM>d<DD>` |
| Did any read fall back to a sequential scan? | `seq_scan` on the same partitions, which `ExplainPlanTests` forbids |
| Which failure did the operator get? | `C:\DISTR\Logs\SemiPlot\semiplot.log`; every startup failure writes its error and a `[FTL]` line |
| Did the live edge reach the chart? | Run `--follow 1` against the same database and watch the chart with **Sticky** on; at `--logging-level debug` the log carries one realtime line per tick |

The failure states are forced from outside the application: stop the container for an unreachable
server, rename `semiplot_tags` for an unfinished provisioning, change the password in the connection
file for a refused login, delete the catalogue rows for an empty catalogue.

Nothing machine-dependent lives in `AppHost.cs`: the role passwords are `BenchRoles`' public
constants, the port is 55432, and the time zone lives only in the generated connection file. The
`Live demo` run configuration at the repository root's `.run/` is a `DotNetProject` configuration
over `SemiPlot.AppHost`, in the shape of `Debug.run.xml`.

## The headless render and input guards

Three classes in `SemiPlot.Tests.Unit` pin what a rendering-stack version bump can change without
announcing it: how a gap is drawn, and how a pointer reaches a handler.

| Class | Drives | Asserts |
| --- | --- | --- |
| `UI/Chart/ChartGapRenderTests` | ScottPlot's rasteriser, no Avalonia | a `NaN` column leaves the rendered line broken, and a continuous series leaves no such break |
| `UI/Chart/ChartPointerInputTests` | headless pointer events into `TrendChartView` | a drag pans the navigation window, a wheel zooms it, a capture loss ends the drag |
| `UI/Minimap/MinimapPointerInputTests` | headless pointer events into `MinimapView` | a drag on the strip moves the chart's window to each pointer fraction, a move after release moves nothing |

**The render guard.** `Plot.RenderInMemory(width, height)` rasterises through SkiaSharp with no
Avalonia in the loop, so it is a plain `[Fact]`; `Plot.RenderManager.LastRender.Layout.DataRect`
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
`PlatformCaptureLost` leaves the guard green.

**A test that builds `TrendChartView` needs a UI scheduler that can defer.** `TrendChartViewModel`
builds `RedrawRequested` as `Sample(33 ms, uiScheduler)`, and `Sample` on
`ImmediateScheduler.Instance` blocks the calling thread forever — the symptom is a silent hang under
`TrendChartView.OnDataContextChanged`. Pass `AvaloniaScheduler.Instance` or a `TestScheduler`, and
dispose every view model built on the Avalonia scheduler inside the test body, since each holds a
33 ms dispatcher timer.

**What the guards do not cover.** The Win32 backend and the desktop `AppBuilder` chain are exercised
by nothing headless; a desktop-only registration the chain is missing fails at `AppBuilder.Setup` in
the running application, which is the application bench's to catch. `ChartGapRenderTests` guards the
ScottPlot half of a bump alone and the pointer guards the Avalonia half: the ScottPlot 5.1.59 bump
marked every wheel event handled, killed wheel zoom, moved no pixel, and `ChartPointerInputTests` is
what failed.
