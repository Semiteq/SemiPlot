# The seeded bench

The data-source slices need a PostgreSQL archive that looks like a Simple-Scada 2 archive and holds
the same rows on every machine. This document is that bench as it exists. What the vendor's archive
is remains in `scada-archive.md`; what SemiPlot reads from it remains in `data-integration.md`.

## Ownership

| Piece | Owner |
| --- | --- |
| Database, roles, grants, default-privileges chain, `semiplot_tags` | `semibase create` (`github.com/Semiteq/SemiBase`, pinned `v0.1.0`) |
| `public.trends`, its daily partitions, the rows | `SemiPlot.Tools.ArchiveSeeder`, connected as `scada_writer` |
| Template build, per-class clones, teardown | `SemiPlot.Tests.Data/Integration` |

The seeder creates the archive tables itself rather than having them restored, because that is what
the SCADA does on a site: it puts SemiBase's default-privileges chain on a path exercised by every
run instead of leaving it to commissioning day. The schema it applies is `sql/semiplot_dev.sql`,
carried as an embedded resource — a console binary and a test assembly have no path to the
repository at runtime.

The seeder never destroys. It refuses a database that already holds `public.trends` and issues no
`DROP` anywhere. Only the test fixture drops databases, and only ones it created itself.

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

`SemiPlot.Tests.Data` is plain `net10.0` on xunit v3, so it runs on a Linux CI runner. It never
references the UI.

- One server per run. A container start costs seconds while a `CREATE DATABASE` costs under one, so
  the container is the wrong isolation unit and the database is the right one.
- One template per run, named after a hash of the seeder's module version, the schema script and the
  slice parameters. A persistent server therefore cannot serve last week's seed to this week's code.
- One clone of that template per test class. Cloning skips the schema apply and the `COPY` entirely.
- Every read in the gated tests connects as `semiplot_reader`, not as the superuser: a grant that
  never reached the reader fails a test here instead of on commissioning day.

Both runtimes the bench needs — a container runtime and the `semibase` binary — are optional on a
developer machine. Their absence is captured as a stated reason and turned into a skip, never into a
pass. `SEMIPLOT_REQUIRE_DB` turns that skip into a failure; the CI `data-tests` job sets it. The
full variable list is in the root `CLAUDE.md`, section *Gated data tests*.

## The application bench

The gated tests exercise the provider. They cannot exercise the composed application: that needs
Avalonia and a container at once, and no CI runner provides both — `build-and-test` runs on
`windows-latest`, which cannot start a Linux container, and `data-tests` on `ubuntu-latest`, which
cannot build against `SemiPlot.UI`. The application bench fills that gap on a developer machine, and
its checks are read from the server and the log rather than from a screen, so they run unattended.

```powershell
docker run -d --name semiplot-bench -e POSTGRES_PASSWORD=<super> -p 55432:5432 postgres:17-alpine
semibase create -host localhost -port 55432 -database semiplot_bench `
  -super-password <super> -writer-password <writer> -reader-password <reader>
dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
  --connection "Host=localhost;Port=55432;Database=semiplot_bench;Username=scada_writer;Password=<writer>" `
  --admin-connection "Host=localhost;Port=55432;Database=semiplot_bench;Username=postgres;Password=<super>" `
  --end 2026-08-01T00:00:00 --days 1 --pens 8 --seed 1
```

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

What this bench cannot answer is what the curve looks like: whether a break renders as a break,
whether the ladder's chosen layer is the right one by eye, and whether the window is legible. Those
wait for the demo stand.
