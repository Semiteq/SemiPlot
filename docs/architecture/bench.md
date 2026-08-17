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
