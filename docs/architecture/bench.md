# The seeded bench

The data-source slices need a PostgreSQL archive that looks like a Simple-Scada 2 archive and holds
the same rows on every machine. This document is that bench as it exists. What the vendor's archive
is remains in `scada-archive.md`; what SemiPlot reads from it remains in `data-integration.md`.

## Ownership

| Piece | Owner |
| --- | --- |
| Database, roles, grants, default-privileges chain, `semiplot_tags`, `public.trends` with `tpdefault` | `semibase bench` (`github.com/Semiteq/SemiBase`), carried in the bench image |
| The daily partitions and the rows | `SemiPlot.Tools.ArchiveSeeder`, connected as `scada_writer` |
| Template build, per-class clones, teardown | `SemiPlot.Tests.Data/Integration` |

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

One day, 8 pens, seed 1, mean change interval 5 s, 4 breaks, exclusive end `2026-01-02T00:00:00`.
The end is fixed rather than floating, so two runs of the same seed produce the same archive. Its
raw layer is 229 862 rows, pinned by a golden digest in `RawLayerGeneratorTests`; a deliberate
waveform change updates that constant in the same commit.

Pens are taken round-robin across the catalogue's groups rather than as the first N, so a slice
spans more than one group and more than one value range.

## What the generator emits

Layer `0` only; the coarse layers are derived from it.

- Each pen walks a sequence of segments — idle, step, ramp, spike. Only a segment produces rows, so
  cost follows output instead of the 100 ms poll grid.
- A change carries its pre-anchor: the previous value one poll interval earlier, then the new value
  at the change tick. That is the vendor's two-rows-per-change shape, so linear interpolation
  between a pair is exact.
- Timestamps carry whole milliseconds only, matching `timestamp(3)`, so an in-memory uniqueness
  check means what `PRIMARY KEY (id, l, t)` means.
- Three quality codes and no others: `0`, `16`, `32`. No bad-quality code was observed in the
  measured dump, so inventing one would be fiction.

## Breaks

A break is the SCADA project stopped: no rows anywhere in the interval, the last row before it
marked `32` and the first row after it `16`. Breaks hit every pen at the same instants.

Each break takes an equal slot of the span, lasts 3 to 10 minutes and leaves at least 5 minutes of
archiving on either side — so two breaks never meet, and every break empties at least one whole
calendar minute, which is the empty period the thinner has to survive. A span therefore holds at
most one break per 20 minutes: 72 in a day. `SeederOptions` rejects a larger `--break-count` with
that number rather than letting `BreakPlan.Create` throw.

A run holding a single row between two breaks would have to carry `32` and `16` at once, and the
archive has no code for both. The resume row keeps `16`, and the poll tick 100 ms after it — which
the SCADA certainly also recorded — is appended and marked `32`. It is the one row in the archive
that did not come from the value walk, and it is reachable at ordinary parameters: 60 breaks in a
day at a mean change interval of 120 s produce it three times.

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
- One template per run, named after a hash of the seeder's module version and the slice parameters.
  A persistent server therefore cannot serve last week's seed to this week's code. Nothing drops a
  template afterwards: on the container path the server dies with the run; on the `SEMIPLOT_TEST_PG`
  path a developer removes accumulated `semiplot_bench_*` databases by hand, and `semiplot_clone_*`
  with them: a run killed between `ArchiveDatabase.CloneAsync` and its disposal leaves a clone
  behind, and nothing sweeps those either.
- One clone of that template per test class. Cloning skips the `COPY` entirely.
- Every read in the gated tests connects as `semiplot_reader`, not as the superuser: a grant that
  never reached the reader fails a test here instead of on commissioning day.

## Where the provisioning comes from

The provisioner is a layer of the bench image, not a binary on the machine.
`SemiPlot.Tests.Data/bench/Dockerfile` copies `/semibase` out of `ghcr.io/semiteq/semibase:latest`
onto the base image `SEMIPLOT_PG_IMAGE` names, and places `provision.sh` in
`/docker-entrypoint-initdb.d/` with mode 0755. The entrypoint runs it — a mode bit is what
separates *run* from *source* there — while `initdb` is still in progress, so
`semibase bench --host /var/run/postgresql --database "$SEMIPLOT_PROVISIONED_DATABASE"` goes over
the unix socket under local `trust`, before the published port opens. The fixture passes that
database name in, so `SemibaseProvisioner.ProvisionedDatabase` is the one place it is written.
`set -e` exits the script on a failed provisioning and the entrypoint, itself under `set -e`,
aborts with it: the container exits non-zero and no port ever serves an unprovisioned database.

The fixture builds that image in-process from the `bench/` directory copied to the output directory
— a test assembly has no path to the source tree. The tag carries a digest of the base image, so a
run under a changed `SEMIPLOT_PG_IMAGE` is never served the build made over the previous base. The
context is two files and every layer is content-addressed, so a rebuild is a cache lookup.

Readiness is asserted rather than observed once and trusted: the wait strategy runs `psql` inside
the container against `public.trends` over **TCP**, and the entrypoint's temporary server listens on
the unix socket only. A container whose provisioning did not complete therefore never becomes ready.

Init scripts run only on an empty `PGDATA`, so this shape provisions a fresh cluster and never a
reused volume.

The one path that spawns a `semibase` binary is `SEMIPLOT_TEST_PG`, which names a server the
fixture did not create and cannot put an init script into. It runs the same `bench` command against
the same `semiplot_provisioned`, so both paths reach one state and no consumer branches on which one
ran. `SEMIBASE_EXE` is how that binary is named, and it is read on that path alone; nothing searches
`PATH`. `bench` ends with a real reader `SELECT` and, off a socket host, a real TCP login as
`semiplot_reader` — so an external server whose `pg_hba.conf` does not admit the reader fails the
fixture rather than the tests.

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
a machine that pulled once. `ProvisionerImage` therefore fetches `ghcr.io/semiteq/semibase:latest`
itself, ahead of the build, and hands the build the digest that fetch resolved —
`ghcr.io/semiteq/semibase@sha256:…`, as the `PROVISIONER_IMAGE` build argument. The image built is
then provably the image pulled, rather than two literals that can drift.

It is a separate step rather than `pull` on the build request because the Engine fails a build
outright when `pull` is set and the registry cannot be reached, even with a usable image already
cached. Pulled on its own, that case stays recoverable: a machine with no route to the registry runs
against the image it has, and only a machine with neither route nor image is an unavailable reason.
A run that fell back that way writes one `[bench]` line to standard error naming the digest it kept
and why — standard error rather than the test output, because a passing test's output is what a
console logger drops.

A container run names the provisioner it ran. The fixture asks the started container for
`/semibase --version` — the bench image carries the binary at that path — and pairs the answer with
the digest the pull resolved; `TheContainerPathReportsTheProvisionerItResolved` writes the pair into
the test output, and the digest alone when the executable declines to report a version. The
`SEMIPLOT_TEST_PG` path writes nothing, because the operator named the binary there and there is
nothing to resolve. The cost of a moving tag is that one unchanged commit can pass today and fail
tomorrow, and that report separates *SemiBase moved* from *this repository broke*.

## The application bench

The gated tests exercise the provider. They do not exercise the composed application: that needs
Avalonia and a container at once, and no CI job does both yet. `ubuntu-latest` hosts both, and
`postgres-live-edge-and-demo` owns the job. Until that job exists the application bench fills the
gap on a developer machine, and its checks are read from the server and the log rather than from a
screen, so they run unattended.

It runs the same bench image the gated suite does, so it needs no `semibase` binary either. The
image provisions `semiplot_provisioned` before the published port opens; the recipe clones that
database and seeds the clone.

**The clone is not a formality.** `semiplot_provisioned` is the fixed name the fixture treats as its
pristine source, and every database the gated suite reads is a `TEMPLATE` copy of it. Seeding it by
hand would leave rows in that source, so pointing `SEMIPLOT_TEST_PG` at this server afterwards would
hand every gated test a template that already carries rows. Seeding a clone keeps the two uses of
one server apart.

```powershell
docker build -t semiplot-bench:manual SemiPlot/SemiPlot.Tests.Data/bench
docker run -d --name semiplot-bench -p 55432:5432 `
  -e POSTGRES_PASSWORD=<super> `
  -e SEMIBASE_WRITER_PASSWORD=<writer> -e SEMIBASE_READER_PASSWORD=<reader> `
  -e SEMIPLOT_PROVISIONED_DATABASE=semiplot_provisioned `
  semiplot-bench:manual
docker exec semiplot-bench psql --username postgres --dbname postgres `
  --command "CREATE DATABASE semiplot_app TEMPLATE semiplot_provisioned;"
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Port=55432;Database=semiplot_app;Username=postgres;Password=<super>" `
  --end 2026-08-01T00:00:00 --days 1 --pens 8 --seed 1
```

`docker logs semiplot-bench` carries the provisioning: a container that reached a serving port ran
`semibase bench` to completion, because the init script's `set -e` and the entrypoint's own make a
failed provisioning and a dead container one event.

The connection file goes to `C:\DISTR\Config\SemiPlot\archive-connection.yaml`, or anywhere
`--config-dir` names. **Seed the archive to an `--end` well in the past**: an archive whose last
sample predates the opening window is what distinguishes a chart that seeds its window from the
extent from one that opens on the wall clock and never reaches the data.

What the server can be asked afterwards, which needs no screen:

| Question | Where the answer is |
| --- | --- |
| Did the application reach the archive at all? | `pg_stat_activity` carries `semiplot_reader` connections while it runs |
| Did it read the catalogue? | `pg_stat_user_tables.idx_scan` on `semiplot_tags` |
| Did it read real history, and from the seeded span? | `idx_tup_fetch` on the seeded day's partition, `tp<YYYY>m<MM>d<DD>`. A window left on the wall clock fetches nothing, because no partition holds those hours |
| Did any read fall back to a sequential scan? | `seq_scan` on the same partition, which the `EXPLAIN` guard forbids |
| Which failure did the operator get? | `C:\DISTR\Logs\SemiPlot\semiplot.log`. A clean start writes nothing at the default `Warning` floor; every startup failure writes its error and a `[FTL]` line |

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
screen.

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
