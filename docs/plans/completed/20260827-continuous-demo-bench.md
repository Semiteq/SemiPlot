# A continuous demo bench

## Overview

The demo stand fills the archive to a fixed `--end 2026-08-01T00:00:00` and then appends at the
machine's wall clock. Measured 2026-08-27 against the running bench container, `semiplot_app`:

| Layer | Rows | Span |
| --- | --- | --- |
| 0 | 232166 | `2026-07-31 00:00:00 .. 2026-08-27 18:49:55` |
| 1 | 35599 | `2026-07-31 00:00:00 .. 2026-07-31 23:59:59.269` |
| 2 | 815 | `2026-07-31 00:00:00 .. 2026-07-31 23:59:59.269` |
| 3 | 96 | `2026-07-31 00:00:00 .. 2026-07-31 23:59:59.269` |

Two consequences, both visible on screen:

- **The archive carries a hole nothing marks.** A raw-layer window spanning it draws one straight
  interpolated segment across 26 days, because an absence of rows without a preceding `q = 32` means
  "the value did not change" (`docs/architecture/data-integration.md:420`). No real site produces
  that state, and the stand teaches it to whoever watches.
- **The live edge is invisible on every coarse layer.** `--follow` writes the raw layer only, so a
  coarse seam sits at the fill end. `FreshTail.Start` clamps the tail at `windowEnd - spacing * 4`
  (`SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs:71-73`), and `FreshTail.Merge` appends tail
  rows only for a pen whose own seam reaches that tail start, dropping the rest
  (`FreshTail.cs:110-116`, `:139`). Every pen's seam sits at the fill end, weeks before the clamp,
  so every pen drops out and the chart shows synthetic history alone.

A real installation is continuous: the SCADA writes raw without stopping and flushes the coarse
layers on their own cadence, so a coarse layer trails raw by less than one of its own periods — the
case `FreshTail` treats as ordinary. The stand should imitate that.

**This changes the bench, not the product.** The reader, the fresh tail and every rule about what a
gap means stay exactly as they are; what changes is the archive the stand hands them.

## Context (from discovery)

**The seeding path thins over the trailing partial period.** `Program.SeedAsync` builds
`rawRows.Concat(LayerThinner.ThinAll(rawRows))`
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs:34-35`) over **all** raw rows, including the
minute, hour and day the fill ends part-way through. `LayerThinner.AppendPeriod` takes that period's
first row (`LayerThinner.cs:63`), so a coarse row for the straddling period is already in the
archive before the demo writer starts. Measured: layer 1's newest row is `2026-07-31 23:59:59.269`,
inside the partial minute `23:59`.

**That is a primary-key collision, not a theoretical one.** `ArchiveWriter` appends by binary `COPY`
(`ArchiveWriter.cs:31`), which has no conflict handling, and the archive's key is
`PRIMARY KEY (id, l, t)`. Re-thinning the straddling minute and inserting the result was run against
the bench and answered:

```
ERROR:  duplicate key value violates unique constraint "tp2026m07d31_pkey"
DETAIL:  Key (id, l, t)=(1000, 1, 2026-07-31 23:59:04.702) already exists.
```

Any design that flushes the straddling period through `ArchiveWriter.WriteAsync` therefore fails at
the first period boundary of every run, `FollowAsync` returns 1 (`Program.cs:109`) and the demo
writer dies.

**The clamps are exactly equal to the flush periods, with no margin.**
`AggregationLayer.ToPointSpacing` is a quarter of the period by construction — 1 s raw, 15 s minute,
15 min hour, 6 h day (`SemiPlot/SemiPlot.Core/Trends/AggregationLayer.cs:19-29`) — and `FreshTail`
clamps at `spacing * 4`. So the clamp is 60 s at Minute, 1 h at Hour, 24 h at Day: one period each,
zero slack. A design that writes a coarse row only when a period closes leaves the seam older than
the clamp for the last seconds of every period, and the tail vanishes once a minute at layer 1 — the
layer a watcher stares at.

**The follow loop keeps one piece of state and no test seam.** `FollowAsync` is private
(`Program.cs:80`), sets `lastEmitted = LocalNow()` before the loop (`Program.cs:98`) and reassigns
it at the end of each tick (`Program.cs:113`); `LocalNow()` is `DateTime.Now` with its `Kind`
stripped (`Program.cs:125-128`) and the loop exits only on Ctrl+C. Nothing accepts an injected
instant, so a test over a period boundary would be a 60-second wall-clock test.

**The thinner is calendar-aligned and already public.** `LayerThinner.PeriodStart` truncates to a
whole minute, hour or day and is declared the one place calendar alignment lives
(`LayerThinner.cs:15-32`); `Thin` groups a pen's rows by period (`LayerThinner.cs:34-49`);
`AppendPeriod` takes first, last, min and max with ties resolved to the earliest row
(`LayerThinner.cs:63-66`) plus every row whose `q` is not ordinary (`LayerThinner.cs:71-74`);
`ThinAll` runs all three coarse layers (`LayerThinner.cs:51-54`).

**The generator is a pure function of absolute time.** `LiveTailGenerator` places every row on an
absolute-time lattice and a row belongs to the span its own timestamp falls in
(`LiveTailGenerator.cs:10-12`), emitted at `ArchiveRow.RawLayer` (`LiveTailGenerator.cs:101-104`).
That is what keeps two adjacent spans disjoint under the primary key.

**The bench script.** `scripts/bench-demo.ps1` converges `semiplot_seeded` (`:197-221`) and
recreates `semiplot_app` from it on every run (`:223-237`). `$SeedEnd` is the literal
`'2026-08-01T00:00:00'` (`:60`); the template is built once and never rebuilt.

**The writer's own role can run the statement.** Verified against the bench: `scada_writer` owns
`public.trends` and `has_table_privilege` returns true for SELECT, INSERT, UPDATE and DELETE, so an
`INSERT ... SELECT` is legal for the role the follow loop already connects as. The loop holds no
open connection to reuse: `ArchiveWriter.WriteAsync` opens one per call and disposes it
(`ArchiveWriter.cs:46`), so the flush opens its own from the same connection string.

**Dependencies:** none new.

## Development Approach

- **testing approach**: Regular (code first, then tests), matching this repository's other plans.
- complete each task fully before moving to the next
- make small, focused changes
- **every task includes new/updated tests** for the code it changes
- **all tests pass before starting the next task** — no exceptions
- **update this plan when scope changes during implementation**

## Testing Strategy

- **gated tests only, in `SemiPlot.Tests.Data`.** The flush is a statement the server executes; a
  pure unit test would pin its text without proving it selects what `LayerThinner` selects. The
  equivalence is the property worth asserting, and only a database can assert it.
- **raw xunit `Assert.` exclusively.** `SemiPlot.Tests.Data` references no assertion library and
  must not gain one.
- **every test class carries all three traits**: `[Trait("Component", "Core")]`,
  `[Trait("Area", "Data")]`, `[Trait("Category", "Integration")]`.
- **a test that writes clones its own database.** `SeededArchive`'s contract is that a class leaves
  the database as it found it (`SemiPlot/SemiPlot.Tests.Data/Integration/SeededArchive.cs:5-7`), and
  the flush writes rows. Take `PostgresContainerFixture.CloneProvisionedAsync()` in
  `InitializeAsync`, the way `ArchiveWriterTransactionTests` does
  (`ArchiveWriterTransactionTests.cs:14-16`, `:63`) — xunit constructs the class once per test
  method, so the clone belongs to exactly one test and is dropped with it.
- **the flush is driven by an explicit instant, never by the wall clock.** The public entry point
  takes the previous tick and the current one, so a boundary crossing is two arguments rather than
  sixty seconds of waiting. A hung test executable locks the next build (`CLAUDE.md`, **Test**), so
  no test in this plan sleeps.
- **no UI test**: nothing in `SemiPlot.Tests` or `SemiPlot.Tests.Journeys` changes — the product is
  untouched.
- **the golden digest is untouched.** `RawLayerGeneratorTests` builds its standard slice in memory
  against a fixed end and never reads the bench's parameters, so a floating `--end` on the stand
  changes an input to a deterministic generator, not the generator.

## Acceptance Evidence

Every item is a command with the result it must produce. Docker is available; the gated suites run
with `SEMIPLOT_REQUIRE_DB=1` and must report 0 skipped.

Baselines measured 2026-08-27 at `a31638b`, by running the commands below: `SemiPlot.Tests` 362,
`SemiPlot.Tests.Data` 483, `SemiPlot.Tests.Journeys` 4, all 0 failed and 0 skipped. A full fill is
266372 rows — 229862 raw, 35599 minute, 815 hour, 96 day — and took 5.7 s end to end including the
`dotnet run` build check.

**1. The archive has no unmarked hole.** After `Bench up` and one minute of the demo writer:

```powershell
docker exec -e PGPASSWORD=semibase-container-superuser semiplot-bench `
  psql -U postgres -d semiplot_app -tAc `
  "WITH s AS (
       SELECT id, t,
              lag(t) OVER (PARTITION BY id ORDER BY t) AS previous,
              lag(q) OVER (PARTITION BY id ORDER BY t) AS previous_quality
       FROM public.trends WHERE l = 0)
   SELECT count(*) FROM s
   WHERE t - previous > interval '1 hour' AND previous_quality IS DISTINCT FROM 32;"
```

Must return **0**. Today it returns **8** — one 26-day hole per pen.

Both halves of the criterion are load-bearing, and both were measured against the pristine fill:

| Criterion | Pristine fill | `semiplot_app` today |
| --- | --- | --- |
| `> interval '60 seconds'`, no exclusion | 153 | 161 |
| `> interval '60 seconds'`, excluding `q = 32` | 121 | 129 |
| `> interval '1 hour'`, excluding `q = 32` | **0** | **8** |

The 60-second form can never return 0: the change interval has a mean of 5 s and an exponential
tail, and the pristine fill carries 121 ordinary gaps between 1 m 00.009 s and 1 m 45.869 s. The
`q = 32` exclusion removes the 32 planted breaks (4 breaks x 8 pens), whose gaps run 3 m 03.565 s to
9 m 41.939 s — bounded by `BreakPlan.MinimumDuration` and `MaximumDuration`
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs:10-11`). One hour clears both populations by
more than an order of magnitude and is still four orders below the hole it must catch.

**2. Every layer keeps a seam young enough for the fresh tail.**

The age is measured against the **host's** clock, and the query has to be handed that instant. This
criterion first read `now()::timestamp - max(t)`, and that expectation was wrong: `now()` is
evaluated inside the container, whose time zone is UTC, while the archive carries naive local time
from the machine the writer runs on. The difference is the host's UTC offset, so on this machine
(`Russian Standard Time`, UTC+3) the original form reports `-02:59:56.887938` for a layer that is
3.1 s old — a negative age, on an archive that is correct to within a second of the wall clock. Bind
the host's own instant rather than subtract an offset the query cannot know:

```powershell
$hostNow = (Get-Date).ToString('yyyy-MM-dd HH:mm:ss.fff')
docker exec -e PGPASSWORD=semibase-container-superuser semiplot-bench `
  psql -U postgres -d semiplot_app -tAc `
  "SELECT l, max(t), timestamp '$hostNow' - max(t) AS age FROM public.trends GROUP BY l ORDER BY l;"
```

After the writer has run past a minute boundary:

| Layer | Required age | How it gets there |
| --- | --- | --- |
| 0 | under 2 min | the tick's own raw append |
| 1 | under 2 min | a closed minute is flushed every minute, within a session |
| 2 | under 1 h | the current hour's opening row; its closed content waits for the hour to close |
| 3 | under 24 h | the current day's opening row; its closed content waits for the day to close |

Layers 2 and 3 trail by up to one period by design and do not reach the live edge within a session.
What reaches the edge on those layers is the fresh tail, and the opening row is what keeps them
above the clamp so the tail is read at all. Today layers 1, 2 and 3 all stop at
`2026-07-31 23:59:59.269` whatever the writer does.

**3. The fill ends where the live edge begins.** Immediately after `Bench up`, before the writer
runs, `max(t)` on layer 0 must be within two minutes of the wall clock. Today it is
`2026-07-31 23:59:59.269`, 27 days behind.

**4. The stale-past bench is still one command.**

```powershell
pwsh scripts/bench-demo.ps1 -SeedEnd 2026-08-01T00:00:00
```

`--end` is exclusive and the raw lattice is change-driven, so the archive stops short of that
instant rather than on it: at `a31638b` the fill's `max(t)` is `2026-07-31 23:59:59.269`, 731 ms
earlier. The criterion is therefore that the fill ends inside the last minute before the given end
and stays a day or more behind the wall clock — which is what makes a chart seeding its window from
the extent reach the data while one opening on the wall clock does not:

```powershell
docker exec -e PGPASSWORD=semibase-container-superuser semiplot-bench `
  psql -U postgres -d semiplot_app -tAc `
  "SELECT max(t) < timestamp '2026-08-01 00:00:00'
       AND max(t) >= timestamp '2026-08-01 00:00:00' - interval '1 minute'
       AND max(t) <  now()::timestamp - interval '1 day'
   FROM public.trends WHERE l = 0;"
```

Must return **t**, and must be run before the demo writer starts — the writer's own rows move
`max(t)` to the wall clock, which is the whole point of the other four criteria.

**5. All three suites, unchanged where the product is unchanged.**

```powershell
dotnet build SemiPlot.slnx -c Release
dotnet format SemiPlot.slnx --verify-no-changes
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj
```

0 warnings, 0 errors, exit 0; `SemiPlot.Tests` still **362** and `SemiPlot.Tests.Journeys` still
**4**, both unchanged because no product code is touched. `SemiPlot.Tests.Data` rises from **483**
by the cases this plan adds.

**6. The scope guard, and it fails rather than reports.**

```powershell
$leaked = @(git diff master...HEAD --name-only |
    Where-Object { $_ -match '^SemiPlot/SemiPlot\.(Core|UI|DataSource\.Postgres)/' })
if ($leaked.Count -ne 0) { throw "product code touched:`n$($leaked -join "`n")" }
```

Must print nothing and exit 0. The pattern anchors at the start of the path, so
`SemiPlot/SemiPlot.Tests.Data/` does not match it.

## Progress Tracking

- mark completed items `[x]` immediately when done
- add newly discovered tasks with ➕
- document blockers with ⚠️
- keep this plan in sync with the work actually done

## Solution Overview

**Thin on the server, not in the process.** The follow loop issues an `INSERT ... SELECT` against
`public.trends` on its own connection, selecting per pen the first, last, min and max rows of a
period plus every row whose `q` is not ordinary — the same selection `LayerThinner.Thin` makes,
expressed in SQL — and closes with `ON CONFLICT DO NOTHING`.

**The equivalence holds over every row a bench generator emits, and the qualification is real.** Two
values break it, and each breaks it for the same reason: `ArchiveRow.Value` is a plain `double`
(`ArchiveRow.cs:3`) ordered by `Comparer<double>.Default`, while the column is a nullable `float8`
ordered by PostgreSQL's own rules.

**NULL, closed by a token.** `trends.v` is nullable while `ArchiveRow.Value` is not, so
`LayerThinner` never sees a NULL and `MinBy`/`MaxBy` never select one. Under `ORDER BY v DESC`
PostgreSQL defaults to NULLS FIRST and would pick a NULL-valued raw row as a period's maximum — a
row the thinner cannot produce. The statement therefore spells the ordering
`ORDER BY v DESC NULLS LAST`; the ascending form already places NULLs last by default.
`CoarseFlushTests.ANullValuedRawRowIsNotSelectedAsAPeriodsMaximum` writes such a row over the admin
connection and pins the outcome, and it fails when the token is removed.

**NaN, left open.** PostgreSQL sorts NaN above every other `float8` while `Comparer<double>.Default`
sorts it below every other double. Measured against the bench server, `ORDER BY v DESC NULLS LAST`
over `{NaN, 1, 5, NULL}` returns `NaN, 5, 1, NULL`, so the statement would select NaN as a period's
maximum where `MaxBy` returns 5; ascending returns `1, 5, NaN, NULL`, so the statement would never
select NaN as the minimum where `MinBy` always would. Nothing triggers it — no bench generator
emits a NaN, and none is expected to — so the condition is stated rather than coded around: **the
equivalence holds for every finite, non-null `v`, which is every value this bench writes.**

**`ON CONFLICT DO NOTHING` is what dissolves the collision.** The seeder's row for the straddling
period and the flush's row for the same period are the same event: same pen, same layer, same
timestamp, same value. The primary key recognises them as one and the second write is a no-op. That
also removes the two pieces of machinery a buffer-and-append design needs —
`WHERE t < <period start>` **is** the closed-period test, and the primary key **is** the once-only
guarantee. Nothing reads a period back into the process and nothing tracks what has already been
flushed.

**Verified equivalence, not asserted.** The statement was run against the bench over the seeded day
with the seeder's own coarse rows removed first, and rebuilt them exactly:

| Layer | Seeder's rows | Statement's rows | Missing | Extra |
| --- | --- | --- | --- | --- |
| 1 | 35599 | 35599 | 0 | 0 |
| 2 | 815 | 815 | 0 | 0 |
| 3 | 96 | 96 | 0 | 0 |

Re-running the identical statement as `scada_writer` reported `INSERT 0 0`.

**The seam stutter is killed by writing the period's opening row early.** As soon as a period opens,
the loop inserts that period's first raw row at the coarse layer. It is the row the closed-period
thinning would select anyway (`LayerThinner.cs:63`), written earlier, so it adds nothing to the
period's final content — when the period closes, the flush selects the same row and
`ON CONFLICT DO NOTHING` skips it.

That is what makes the clamp's zero margin harmless. Let `P` be the current period's start and `W`
the period's width. The coarse layer's newest row is at or after `P`. The clamp is `windowEnd - W`
(`FreshTail.cs:71-72`, with `ToPointSpacing` a quarter of `W`). A sticky window sets its end to the
live edge (`SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs:108-118`), which lies inside the
current period, so `windowEnd < P + W` and therefore `windowEnd - W < P <= seam`. The clamp is
cleared strictly, at every instant of the period.

Without the opening row the seam is the last raw row of the *previous* period, which sits before
that period's end. Measured per pen over the 11313 pen-minutes of the seeded day that carry rows,
that distance has a mean of 9.093 s, a minimum of 0.001 s and a maximum of 59.630 s. A minute period
bounds the distance by its own 60 s width, and the fill reaches to within 0.37 s of that bound.
(1 m 45.869 s, quoted in acceptance criterion 1, is a different quantity: the longest ordinary gap
between two consecutive raw rows, which spans a period boundary rather than sitting inside one.)
A mean lag of 9 s against a 60 s clamp puts the seam past the clamp for the last seconds of most
minutes; where the lag runs to its maximum the pen is out for nearly the whole minute. The pen drops
out of the tail and the right edge retreats, once a minute at layer 1.

**Do not flush the whole open period every tick.** The running "last" row moves with each tick, so
`ON CONFLICT DO NOTHING` would accept a new row every second and the coarse layer would grow as
dense as raw. Only the opening row is written early, because only the opening row is already final.

**Delete the seeded template.** With the fill end moving to "now", a reused template hands the
writer an archive as stale as the template, which reintroduces the hole. `scripts/bench-demo.ps1`
clones `semiplot_provisioned` straight into `semiplot_app` and runs the seeder on it every
`Bench up`. A full fill is around 266000 rows in a few seconds, so the template was buying seconds
and now costs correctness. Cloning `semiplot_provisioned` rather than seeding it keeps the rule at
`docs/architecture/bench.md:224-228` intact: the fixture's pristine source stays empty.

**`$SeedEnd` becomes a parameter, not a mode.** It defaults to the script's own wall clock; the
stale-past bench is the same script with an explicit past value. No `-Stale` switch, no second
template, and `.run/Bench up.run.xml` needs no edit — its `SCRIPT_OPTIONS` is empty, which also
keeps the scope guard satisfiable.

## Technical Details

**Two statements per coarse layer, on different cadences.** Both were measured against the bench
container over the 229862-row raw layer.

*The closed-period flush*, run only on a tick that crosses that layer's own boundary:

```sql
INSERT INTO public.trends (id, l, t, v, q)
SELECT id, @layer, t, v, q
FROM (
    SELECT id, t, v, q,
           row_number() OVER (PARTITION BY id ORDER BY t)                    AS first_row,
           row_number() OVER (PARTITION BY id ORDER BY t DESC)               AS last_row,
           row_number() OVER (PARTITION BY id ORDER BY v, t)                 AS min_row,
           row_number() OVER (PARTITION BY id ORDER BY v DESC NULLS LAST, t) AS max_row
    FROM public.trends
    WHERE l = 0 AND t >= @periodStart AND t < @periodEndExclusive
) selected
WHERE first_row = 1 OR last_row = 1 OR min_row = 1 OR max_row = 1 OR q <> 0
ON CONFLICT DO NOTHING;
```

`@periodStart` and `@periodEndExclusive` are bound from `LayerThinner.PeriodStart`, never computed
as `date_trunc` in SQL: calendar alignment stays in the one method that owns it for the whole
repository (`LayerThinner.cs:15-17`). Because the bound span holds exactly one calendar period, the
windows partition by `id` alone and need no period key. `ORDER BY v, t` and
`ORDER BY v DESC NULLS LAST, t` reproduce `AppendPeriod`'s tie resolution to the earliest row
(`LayerThinner.cs:63-66`) and keep a NULL `v` out of the maximum, and `q <> 0` is its marker-row
loop (`LayerThinner.cs:71-74`).

*The opening-row seam*, run every tick:

```sql
INSERT INTO public.trends (id, l, t, v, q)
SELECT pen.id, @layer, opening.t, opening.v, opening.q
FROM unnest(@penIds) AS pen(id)
CROSS JOIN LATERAL (
    SELECT t, v, q FROM public.trends
    WHERE id = pen.id AND l = 0 AND t >= @periodStart
    ORDER BY t LIMIT 1
) AS opening
ON CONFLICT DO NOTHING;
```

`@penIds` comes from `RawLayerGenerator.SelectPens(options.PenCount)`, which the loop already knows.
No upper bound is needed: the raw layer ends at the live edge, which is inside the open period. The
`LATERAL` with `LIMIT 1` is what keeps it cheap — the primary key is `(id, l, t)`, so each pen costs
one index probe.

**Why the closed flush is gated on a boundary crossing rather than issued every tick.** Measured
execution times, PostgreSQL 17-alpine in the bench container:

| Statement | Rows scanned | Time |
| --- | --- | --- |
| Closed flush, minute, unbounded below (`t < @periodStart` only) | 229676 | 286 ms |
| Closed flush, minute, one-period span | 162 mean, 271 max | 8.7 ms |
| Closed flush, hour, one-period span | 9578 mean | 19.7 ms |
| Closed flush, day, one-period span | 229862 | 298 ms |
| Opening-row seam, any layer | 8 index probes | 0.6 ms |

Each figure is the median of 15 runs — 5 for the day and for the unbounded form — of the statement
exactly as written above, the `INSERT` and its `ON CONFLICT DO NOTHING` included, against a clone of
the seeded archive with the coarse rows removed so every run inserts rather than conflicts. The
`SELECT` half alone measures 7.4 ms at the minute, 17.1 ms at the hour and 274 ms at the day, so the
write is a small share of each.

An unbounded closed flush scans the whole raw layer whatever the layer, so all three cost about the
same 286 ms per tick and all three grow with the archive; three of them together spend roughly 0.9 s
of the 1-second cadence `.run/Demo writer.run.xml` uses on day one and overrun it soon after.
Bounding the span to one period fixes the minute and the hour but leaves the day at 298 ms every
second for a period that closes once a day. The gate is one comparison over state the loop already
holds: `LayerThinner.PeriodStart(now, layer) != LayerThinner.PeriodStart(previousTick, layer)`. It
needs no new type and no per-layer dictionary. With it, the minute flush costs 8.7 ms once a minute,
the hour 19.7 ms once an hour and the day 298 ms once a day; the seam statements cost about 2 ms per
tick for all three layers together.

**The public entry point takes both instants:**

```
CoarseFlush.FlushAsync(
    FollowOptions options,
    DateTime previousTickLocal,
    DateTime nowLocal,
    CancellationToken cancellationToken)
        -> Result<long>   // rows inserted by the statements this call issued
```

`FollowOptions` carries the connection string, so the method takes the connection and two explicit
instants and nothing else. A test crosses a boundary by passing two arguments.

**Order within a tick: raw append first, then flush.** At a 1-second cadence the closing period's
rows were all written by earlier ticks, but a longer `--follow` value makes the tick's own append
supply them. Flushing first would write a coarse "last" row for a period whose final raw rows had
not landed yet.

**A tick longer than a period closes every period it leaves, not only the first.** This was first
written up as a state `--follow 1` cannot reach. It is reachable, and by two routes. `FollowOptions`
accepts a cadence up to `MaximumSeconds` = 86400 s, so any `--follow` above 60 s crosses several
minute boundaries on the ordinary path; and a host suspend, or any stall over a period's length,
stretches a single `Task.Delay` at `--follow 1` across many periods. Either way the raw layer
refills across the whole jump — `LiveTailGenerator` is a pure function of absolute time and the
partition DDL is `IF NOT EXISTS` — so a flush closing one period would leave a continuous raw
layer under a coarse layer with an unmarked hole, which is the defect this branch exists to remove,
at a smaller scale. `CoarseFlush.FlushLayerAsync` therefore loops from `PeriodStart(previousTick)`
to `PeriodStart(now)`, one statement per period. Each statement keeps its one-period bound, which is
what lets its windows partition by `id` alone.
`CoarseFlushTests.ACallSpanningSeveralPeriodsClosesEveryOneOfThem` pins it, and it fails against the
single-period gate.

**The seam period keeps a few extra coarse rows, once.** The seeder already wrote up to four coarse
rows for the partial minute, hour and day its fill ends inside. When those periods close, the flush
selects the four the full period deserves; the seeder's stale ones stay. A coarse layer holds
verbatim raw rows, so the union is still a set of real samples — at most four extra rows per pen, in
exactly one minute, one hour and one day of the whole archive.

**Processing flow of one tick, after the change:**

```
tick
  → generate raw rows for [lastEmitted, now)      unchanged
  → append them                                    unchanged
  → for each coarse layer:
        insert the current period's opening row     every tick, ~0.6 ms
        if PeriodStart(now) != PeriodStart(lastEmitted):
            flush the period that just closed       once per period
  → lastEmitted = now
```

**Row counts stay small.** A closed minute at 8 pens with the default 5-second change interval
yields at most 4 rows per pen plus markers, so a minute flush is bounded at 32 rows before markers.
Measured over the 1418 minutes of the seeded day that carry rows: the raw input is a mean of 162.1
rows per minute (min 19, max 271) and the layer-1 output a mean of 25.11 rows per minute (min 13,
max 30). The floor is below 32 because a minute where a pen changes fewer than four times, or not at
all, contributes fewer than four rows for it.

**Statement text lives in the seeder, which is exempt by design.**
`docs/architecture/data-integration.md:90-92` enumerates the SQL the bench seeder holds outside the
one-place rule; these two statements join that list.

## What Goes Where

- **Implementation Steps**: the seeder, the bench script, their tests and the documentation.
- **Post-Completion**: the one check no automated test covers, and one product follow-up this work
  makes visible but must not carry.

## Implementation Steps

### Task 1: Thin a closed period on the server

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/CoarseFlushTests.cs`

- [x] add `CoarseFlush` holding the closed-period statement, one per entry of
      `LayerThinner.CoarseLayers` (`LayerThinner.cs:13`), issued on a connection of its own
- [x] bind `@periodStart` and `@periodEndExclusive` from `LayerThinner.PeriodStart`, never from
      `date_trunc`, so calendar alignment stays in the one method that owns it
- [x] keep the row filter identical to `LayerThinner.AppendPeriod`: first, last, min and max with
      ties resolved to the earliest row (`LayerThinner.cs:63-66`), plus every row whose `q` is not
      `ArchiveRow.OrdinaryQuality` (`LayerThinner.cs:71-74`), and order the maximum
      `v DESC NULLS LAST` so a NULL `v` cannot be selected as one
- [x] close every statement with `ON CONFLICT DO NOTHING` and return the rows actually inserted
- [x] expose the public `CoarseFlush.FlushAsync` whose signature **Technical Details** states,
      gating each layer's closed flush on
      `LayerThinner.PeriodStart(now, layer) != LayerThinner.PeriodStart(previousTick, layer)`

**Every assertion below is scoped to one layer and to one period's own bounds**, never to a count
over the whole table and never to "the call wrote nothing". Task 2 makes the same entry point write
the current period's opening row on every call, so an assertion phrased over the whole table would
go red there; an assertion over the rows inside the closed period stays true, because the opening
row lands inside the *following* period.

- [x] write a gated test that the flush of one closed period produces, inside that period's bounds
      and at that period's layer, exactly the rows `LayerThinner.Thin` produces for it — no row
      missing, no row extra
- [x] write a gated test that running the same flush twice inserts 0 the second time and leaves that
      period's row count unchanged
- [x] write a gated test that a period whose rows include a `q = 32` marker carries that marker into
      all three coarse layers
- [x] write a gated test that a pair of instants inside one minute adds no coarse row inside the
      minute that precedes them — the closed-period gate fires at no layer
- [x] write a gated test that a pair crossing an hour boundary adds the closed minute's rows at
      layer 1 and the closed hour's rows at layer 2, and adds no row at layer 3 inside the day
      preceding the pair
- [x] write a gated test that a flush over a period the seeder already wrote a coarse row for
      succeeds and adds no duplicate — the case that kills a `COPY`-based design
- [x] every test class carries the three traits and clones its own database with
      `CloneProvisionedAsync()`
- [x] run tests — must pass before task 2

### Task 2: Write each period's opening row as the period opens

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/CoarseFlushTests.cs`

- [x] add the opening-row statement: for each coarse layer, the first raw row at or after the
      current period's start, one `LATERAL` probe per pen, `ON CONFLICT DO NOTHING`
- [x] take the pen identifiers from `RawLayerGenerator.SelectPens(options.PenCount)` rather than
      from a catalogue read — `scada_writer` holds no privilege on `semiplot_tags`
- [x] issue it on every call to `FlushAsync`, ahead of the gated closed flush, and fold its row
      count into the returned total
- [x] write a gated test that a call inside a fresh period leaves one coarse row per pen per layer,
      carrying the period's first raw row verbatim
- [x] write a gated test that ten calls inside one period leave that same one row per pen per layer,
      proving the coarse layer does not densify
- [x] write a gated test that closing the period afterwards adds only the rows the opening row is
      not already, so the period's final content equals `LayerThinner.Thin` over it
- [x] write a gated test that a period with no raw rows yet writes nothing and reports no failure
- [x] re-run task 1's `CoarseFlushTests` cases unchanged: each is scoped to a closed period's own
      bounds and layer, so the opening row — which lands in the following period — leaves every one
      of them green. Fix the code, not the assertion, if one goes red
- [x] add to the two gate cases the one thing the opening row does change: a same-minute pair and an
      hour-crossing pair now each leave exactly one opening row per pen at every coarse layer,
      inside the period the pair sits in
- [x] run tests — must pass before task 3

### Task 3: Flush from the follow loop and correct what it advertises

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowOptions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SeederEntryPointTests.cs`

- [x] call `CoarseFlush.FlushAsync(options, lastEmitted, now, ...)` after the tick's raw append has
      committed (`Program.cs:103-113`), and fail the run on its failure the way the append already
      does (`Program.cs:105-110`)
- [x] report the coarse rows written on the tick's own line, so a watcher sees a flush happen
- [x] leave `LiveTailGenerator`, `RawLayerGenerator` and `LayerThinner` untouched
- [x] replace `FollowOptions.Usage`'s "A follow run appends layer 0 only and seeds nothing"
      (`FollowOptions.cs:70-71`) with what a follow run now does: it appends raw rows and thins them
      into the coarse layers, and still seeds nothing
- [x] update the two assertions pinning that wording, `SeederEntryPointTests.cs:49` and
      `SeederEntryPointTests.cs:62`, to the new text
- [x] run tests — must pass before task 4

### Task 4: Fill to the current moment and drop the seeded template

**Files:**
- Modify: `scripts/bench-demo.ps1`

- [x] add `[string] $SeedEnd` to `param()`, defaulting to the script's own wall clock formatted
      naive local as `yyyy-MM-ddTHH:mm:ss`. Truncating to a whole second is the script's own choice,
      not a match to `Program.LocalNow()`, which strips the `Kind` and nothing else
      (`Program.cs:125-128`): the value is echoed in the closing report and in `bench.md`, and a
      whole second reads. The lost sub-second cannot show, because the seeder's `--end` is exclusive
      and the follow writer starts from its own `LocalNow()` — the untouched interval between the
      two is under a second against a mean change interval of 5 s
- [x] delete `$SeededTemplate` (`:47`) and the whole `Seeding semiplot_seeded` block (`:197-221`)
- [x] clone `semiplot_provisioned` directly into `semiplot_app` on every run and run the seeder
      against the clone, with `--admin-connection` so `semiplot_tags` is filled —
      `semiplot_provisioned` carries 0 tag rows, so the clone starts empty
- [x] terminate backends on `semiplot_provisioned` as well as on `semiplot_app` before the clone
      (`:229-232`): `CREATE DATABASE ... TEMPLATE` refuses a source another session holds, and the
      source is now used on every run rather than once
- [x] update the comment header's two-lifetime description (`:11-22`) and the `$SeedEnd` comment
      (`:57-59`), which both describe the template and the fixed past end
- [x] state the chosen fill end and the row count in the closing report (`:259`)
- [x] run the script twice and confirm each run leaves a pristine archive ending inside the last
      minute before that run's own wall clock
- [x] run it once with an explicit `-SeedEnd 2026-08-01T00:00:00` and confirm the stale-past archive
      with the query of acceptance criterion 4
- [x] run the three suites — this task changes PowerShell only, so no test changes with it and all
      three must stay at the counts task 3 left them at. Must pass before task 5

### Task 5: Verify acceptance criteria

**Files:** none — this task runs commands and records their output.

- [x] run every command in `## Acceptance Evidence` and record its actual output against the result
      that section states
- [x] state all three suite counts and reconcile them against the 362 / 483 / 4 baseline
- [x] run the scope guard and confirm it exits 0 with no output

**Measured 2026-08-27 at `1e87bd5`.** Host: Windows 11, time zone `Russian Standard Time` (UTC+3).
Bench: `semiplot-bench`, PostgreSQL 17-alpine, `semiplot_app` on `localhost:55432`.

| Criterion | States | Read | Verdict |
| --- | --- | --- | --- |
| 1 no unmarked hole | `0` | `0` | pass |
| 2 seam age per layer | 0, 1 under 2 min; 2 under 1 h; 3 under 24 h | 0.703 s, 50.703 s, 6 m 02.4 s, 6 m 02.4 s | pass, after the criterion was corrected |
| 3 fill ends at the live edge | layer 0 within 2 min of the wall clock | 14.591 s behind it | pass |
| 4 stale-past bench | `t` | `t` | pass |
| 5 suites | 362 / above 483 / 4, 0 failed, 0 skipped | 362 / 493 / 4, 0 failed, 0 skipped (495 at HEAD) | pass |
| 6 scope guard | no output, exit 0 | no output, exit 0 | pass |

**Criterion 2's stated query was wrong, and the correction is in the criterion itself.** Run
verbatim it returned a *negative* age on every layer — `-02:59:56.887938` at layer 0, `-02:59:16` at
layer 1, `-02:54:05` at layers 2 and 3 — because `now()` is evaluated in the container, whose zone
is UTC, against an archive carrying naive host-local time. The offset is the host's own, so the fix
binds the host instant rather than subtracting a constant the query cannot know; with
`timestamp '$hostNow'` the same four layers read `00:00:00.703`, `00:00:50.703`, `00:06:02.434` and
`00:06:02.434`. The archive was never wrong: layer 0 sat 0.7 s behind the wall clock while the
original query called it three hours ahead of it.

**Criterion 5, command by command.** `dotnet build SemiPlot.slnx -c Release`: 0 warnings, 0 errors,
exit 0. `dotnet format SemiPlot.slnx --verify-no-changes`: no output, exit 0. The three suites, the
gated two with `SEMIPLOT_REQUIRE_DB=1`:

| Suite | Passed | Failed | Skipped | Against the baseline |
| --- | --- | --- | --- | --- |
| `SemiPlot.Tests` | 362 | 0 | 0 | 362, unchanged as stated |
| `SemiPlot.Tests.Data` | 493 | 0 | 0 | 483 plus the 10 cases of `CoarseFlushTests`; **495** at HEAD |
| `SemiPlot.Tests.Journeys` | 4 | 0 | 0 | 4, unchanged as stated |

The data suite's rise reconciles exactly: at this commit `CoarseFlushTests` held 10 `[Fact]` cases
and no other file gained one — `SeederEntryPointTests` changed two assertions and added no case.

**Superseded at HEAD, on the data count only.** The review phase added two cases to
`CoarseFlushTests`, which now holds 12. The progression is 483 baseline, 493 at this task and
**495** at HEAD. `SemiPlot.Tests` 362 and `SemiPlot.Tests.Journeys` 4 are unchanged throughout.

**Criterion 4's fill is the plan's own figure.** `pwsh scripts/bench-demo.ps1 -SeedEnd
2026-08-01T00:00:00` wrote 266372 rows — 229862 / 35599 / 815 / 96 — in 3.1 s, newest
`2026-07-31 23:59:59.269`, 731 ms before the given end. The query returned `t`.

**Criterion 3, immediately after a default `Bench up`.** The fill wrote 266409 rows in 3.0 s, newest
`2026-08-27 20:35:48.269` against a `-SeedEnd` of `2026-08-27T20:35:49`; measured against the host
clock at `20:36:02.860` that is 14.591 s. The coarse counts differ from the fixed-end fill
(35556 / 863 / 128 rather than 35599 / 815 / 96) because a day-long span starting at 20:35:49
straddles two calendar days and one more calendar hour than one starting at midnight.

**The live edge, measured end to end.** The demo writer ran its own configuration
(`--follow 1 --pens 8 --seed 1 --change-seconds 5`) from 20:36:18 to 21:01:10 — 1465 ticks across
25 minute boundaries and one hour boundary. What each layer held, sampled every 2.5 s and again at
250 ms across two boundaries:

| Layer | `max(t)` through the run | Age at 21:01:02.234 | Required |
| --- | --- | --- | --- |
| 0 | advances every 5 s with the change lattice | 2.234 s | under 2 min |
| 1 | steps once a minute: 20:37:00, 20:38:00 … 21:01:00 | 2.234 s | under 2 min |
| 2 | held 20:35:48.269, then stepped to 21:00:00 at 21:00:00.615 | 1 m 02.234 s | under 1 h |
| 3 | held 20:35:48.269 — the day has not closed | 25 m 13.965 s | under 24 h |

Layer 2's step is the hour crossing caught live, and it is the half no earlier task could show. The
21:00:00 tick reported **55 coarse rows** against 26 to 32 on an ordinary minute boundary: minute
20:59's closed flush and its layer-1 opening row, plus hour 20:00's closed flush and its layer-2
opening row, on one tick. Every later tick inside a period reported **0 coarse** — the opening-row
statement runs on all of them and `ON CONFLICT DO NOTHING` absorbs it, so the coarse layers do not
densify. The observable is "every later tick", not "every tick crossing no boundary": the first tick
to give a pen a raw row inside the open period writes that pen's opening row, and a 5-minute rerun
showed exactly that — 291 ticks, 285 of them `0 coarse`, five minute boundaries at 24, 30, 32, 31
and 29, and one in-period tick at 1.
Layer 1 carried 27, 32, 30, 29 and 26 rows for the first five closed minutes and 8 — one
per pen — for the open one. Hour 20:00 ended at 49 rows, the documented one-off union of the
seeder's partial-hour rows with the closed flush's full-hour rows.

**The coarse seam never fell below the fresh tail's clamp.** The measured quantity is the one
`FreshTail` tests: per pen, the live edge (`max(t)` at `l = 0`) minus that pen's own layer-1 seam,
against a clamp of `spacing * 4` = 60 s.

| Window | Samples | Worst per-pen gap this sampling saw |
| --- | --- | --- |
| 20:37:16 .. 20:40:49, every 2.5 s, 4 minute boundaries | 77 | 55 s |
| 20:42:52 .. 20:43:08, every ~260 ms, across one boundary | 60 | 55 s |
| 20:59:54 .. 21:00:12, every ~250 ms, across the hour | 20 | 55 s |

The dense pass shows the whole shape: the gap climbs, **holds across the boundary itself** from
20:43:00.000 to 20:43:00.840 — the seam and the live edge are both still inside minute 20:42, so
the clamp moves with the seam rather than away from it — and resets to 0 the moment the tick
writes minute 20:43's opening row. `min(seam)` equalled `max(seam)` over the 8 pens at every
sample, so no pen ever lagged the others and none could drop out of the tail. Layer 2's gap peaked
at 24 m 39.8 s against its own 1 h clamp before the crossing cleared it.

**Correction, the 55 s figure.** 55 s is what the sampling above saw, not the worst the archive
holds, and there is no margin to report. The table's passes sample at 250 ms to 2.5 s and none of
them landed in the last 100 ms of a minute. Reconstructed from the archive of a later 5-minute run
at the same parameters, and reading the rows rather than sampling a wall clock:

| Quantity, layer 1, 8 pens, 4 complete minutes | Measured |
| --- | --- |
| Seam offset from its own period start | `00:00:00` at every pen and every minute |
| Live edge to seam, both commits landed (`max(t)` at `l = 0` inside the period) | `00:00:59.9` |
| Live edge to seam between a boundary tick's two commits | `00:01:00` |
| `FreshTail` clamp at layer 1 (`spacing * 4`) | `00:01:00` |

Two mechanisms produce those numbers. `--change-seconds 5` divides 60 s, so a change row lands on
every minute boundary and the opening row sits exactly at the period start, while the last raw row
of the minute is the pre-anchor one `RawLayerGenerator.PollInterval` = 100 ms earlier — hence
59.9 s.
And inside a boundary tick the raw `COPY` and the coarse `INSERT` commit on separate connections
(`ArchiveWriter.WriteAsync` opens and commits its own; `CoarseFlush.FlushAsync` opens its own after
it returns), so between the two commits the live edge is already at the boundary while the seam is
still one period behind it — exactly 60 s, equal to the clamp.

So the design holds on `FreshTail.EarliestSeamReachingTheClamp`'s non-strict `seam >= clampedLocal`,
not on a margin. Both figures are ceilings rather than constants: a `--change-seconds` value that
does not divide the period puts the opening row later and shortens the distance. Nothing observed
was wrong in the other direction either — no pen was ever seen dropping out of the tail, at any
sample of any pass.

### Task 6: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/bench.md`
- Modify: `docs/architecture/data-integration.md`
- Move: `docs/plans/20260827-continuous-demo-bench.md` → `docs/plans/completed/` — deferred to the
  delivery step, see the last checkbox

- [x] `bench.md:237-240`: `semiplot_seeded` is gone. `semiplot_app` is a clone of
      `semiplot_provisioned` seeded on every run; the literal `--end 2026-08-01T00:00:00` becomes
      the `-SeedEnd` parameter defaulting to the wall clock
- [x] `bench.md:242-248`: the recreate-every-run paragraph now names `semiplot_provisioned` as the
      source and the seeder rather than a `TEMPLATE` clone as the cost, with the measured 266372
      rows in 5.7 s
- [x] `bench.md:250-253`: "seeded well into the past on purpose" is no longer the default — it is
      what `-SeedEnd` selects, and the paragraph must say which reading each choice buys
- [x] `bench.md:309`: `Bench up` is no longer a no-op on a second press; it re-seeds, and that is
      the point
- [x] `bench.md:282`: the `--follow 1` row now describes a writer that moves every layer's seam, not
      only today's partition
- [x] `data-integration.md:90-92`: add **both** of the seeder's new statements to the list of SQL it
      owns by design — the closed-period `INSERT ... SELECT` and the opening-row `INSERT` with its
      `LATERAL` probe — beside the schema resource, the partition DDL, the `COPY`, the catalogue
      upsert, `CREATE DATABASE` and `DROP DATABASE` already there
- [x] confirm no changed markdown gained a BOM and prose wraps at 100 characters
- [x] ➕ `bench.md:71-105`, **The demo writer**: the tick line now prints two counts and the
      "Layer `0` only" property becomes "every layer, each on its own cadence" — the two
      statements, their cadences, the ticks that report 0 coarse and the measured worst layer-1
      seam against its 60 s clamp. Not in the checkbox list above, and stale without it
- [x] deferred to the delivery step — exec never moves the plan. Moving it to
      `docs/plans/completed/` is delivery work that runs after the operator has reviewed the branch,
      as this repository's other plans record it, so the step is written down here and performed
      there

**Deviation, the fill's cost figure.** The checkbox states "266372 rows in 5.7 s". 5.7 s is the
**Acceptance Evidence** baseline measured through `dotnet run`, whose build check the figure
includes. Task 5 measured the same fill at 3.1 s with the project already built, which is the state
every `Bench up` after the first runs in, so the doc states the cost the reader pays.

**Correction, the row count.** `bench.md` first carried "266372 rows in about three seconds" as if
it described the default path. It does not: 266372 is the `-SeedEnd 2026-08-01T00:00:00` fill's, and
only its raw layer, 229862 rows, is fixed. The coarse layers follow the calendar periods the span
covers, and a day-long span ending at an arbitrary instant straddles two calendar days and one more
calendar hour than one ending at midnight, so no six-digit figure is reproducible under the default:
three measured default runs gave 266409, 266522 and 266408. `bench.md` now hedges the headline at
"around 266000 rows in a few seconds", names each measurement with the `-SeedEnd` it was taken at,
and states why the count moves.

## Post-Completion

**Manual verification**

Raise the stand, start the demo writer, and switch the chart through Raw, Minute, Hour and Day. On
every layer the curve must reach the right edge and the edge must advance.

Read the two halves apart, because they are answered by different mechanisms. On Raw and Minute the
coarse rows themselves advance, once a second and once a minute. On Hour and Day the coarse rows do
not advance within a session and are not supposed to — `max(t)` at `l = 2` sitting up to an hour
back and at `l = 3` up to a day back is the correct state. What must still reach the right edge
there is the drawn curve, which the fresh tail supplies. A curve stopping short on Hour or Day is a
real failure; a coarse `max(t)` one period back is not, and must not be filed as one.

**Known limitations, surfaced by the critical review pass**

**Limitation 1: the seam ceiling depends on `--change-seconds`, not on the design alone.** At the
demo writer's own `--follow 1 --change-seconds 5` the ceiling holds, because 5 s divides the minute:
a change row lands on every boundary, so the opening row sits at the period start and the worst
in-period distance is 59.9 s against the 60 s clamp. At a larger cadence the distance between the
tick's raw commit and its coarse commit can exceed the clamp — measured **64.9 s** at
`--follow 5` with the tick landing in the last 100 ms of a change interval. What an operator sees is
a momentarily short right edge on a wide window, for the few milliseconds between the two commits.
No data is lost and none is corrupted: the next tick's opening row clears it.

**Limitation 2 no longer stands: the fill and the live edge meet exactly.** `$SeedEnd` is captured
at parameter binding, seconds before the fill lands, so an operator reading `max(t)` right after
`Bench up` still sees the fill end a few seconds behind the wall clock. The follow writer starts its
loop at that row rather than at its own `LocalNow()`, so the first tick writes the interval between
the two and the archive carries nothing untouched between the fill and the live edge.
`StaleArchiveGuard` is what keeps that first tick bounded: an archive more than five minutes behind
the clock is refused rather than appended to.

**Named follow-up, not part of this plan**

A staleness readout — the age of the newest archive row against the window edge — would let a person
tell "the fresh tail is clamped away" from "the archive stopped writing". Nothing on screen
distinguishes those two today, which is exactly why the second defect above took a database query to
find. That is product work in `SemiPlot.UI` and the scope guard of criterion 6 would fail the branch
that carried it, so no task here writes it and no task here files it: entering it in
`docs/plans/backlog.md` is the operator's own step after delivery, and that file appears in no Files
block of this plan.

**What stays unproven**

Whether the vendor thins on calendar periods or on flush windows is the first open question of
`docs/architecture/scada-archive.md`, and `LayerThinner.PeriodStart` carries the calendar assumption
for the whole repository. Binding both statements' period bounds from that method rather than from
`date_trunc` keeps the assumption in one place; the experiment that settles it still replaces that
one method and nothing else.

## Verify it yourself

Run these before shipping. Each check names what shows before the change and what shows at HEAD.

**1. Build, format and the three suites.**

```powershell
dotnet build SemiPlot.slnx -c Release
dotnet format SemiPlot.slnx --verify-no-changes
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj
```

0 warnings, 0 errors, exit 0, then 362 / 495 / 4 passed with 0 failed and 0 skipped in each.

**2. The multi-period flush, proved by reverting the code it guards.** `9502bb0` flushes only the
period holding the previous tick; `821f8e1` loops over every period the tick leaves.

```powershell
git checkout 9502bb0 -- SemiPlot/SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~ACallSpanningSeveralPeriodsClosesEveryOneOfThem"
git checkout HEAD -- SemiPlot/SemiPlot.Tools.ArchiveSeeder/CoarseFlush.cs
```

Fails on the reverted file — a minute the tick jumped over stays empty at layer 1 — and
passes at HEAD.

**3. `ORDER BY v DESC NULLS LAST`, proved the same way.** Delete `NULLS LAST` from the descending
`row_number()` window in `CoarseFlush.cs` and run

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~ANullValuedRawRowIsNotSelectedAsAPeriodsMaximum"
```

It fails with a count of 1 where 0 is asserted: PostgreSQL's default `NULLS FIRST` picks the
NULL-valued raw row as the period's maximum, a row `LayerThinner` cannot produce. Restore the token
and it passes.

**4. The hole the branch exists to remove.** On `master`, `pwsh scripts/bench-demo.ps1` leaves
`max(t)` at `2026-07-31 23:59:59.269` whatever the wall clock is, and acceptance criterion 1's query
returns **8** — one hole per pen — once the demo writer has run. At HEAD the same query
returns **0**, and `max(t)` right after `Bench up` sits within a couple of minutes of the wall
clock, which is acceptance criterion 3.

**5. The culture fix (`fd5e95c`).** `$SeedEnd`'s default was rendered with the current culture, so a
non-invariant locale produced a string `SeederOptions.ReadEnd` rejects:

```powershell
$underFinnish = {
    [Threading.Thread]::CurrentThread.CurrentCulture = 'fi-FI'
    & ./scripts/bench-demo.ps1
}
pwsh -NoProfile -Command $underFinnish
```

Before `fd5e95c` the default renders `2026-08-27T21.30.13` and the seeder refuses it. At HEAD the
default renders through `InvariantCulture` and the run completes.

**6. The one check no automated test covers.** Raise the stand, start the demo writer
(`--follow 1 --pens 8 --seed 1 --change-seconds 5`), turn **Sticky** on and switch the chart through
**Raw**, **Minute**, **Hour** and **Day**. On each layer the drawn curve must reach the right edge
and the edge must advance. Watch the writer's own tick line while doing it: `0 coarse` on ordinary
ticks, a jump at each minute boundary, a larger one at the hour.

Read the layers apart. Layer 1 advances by flush once a minute, layer 2 once an hour and layer 3
once a day; between flushes those two reach the right edge through the fresh tail, not through new
coarse rows. A coarse `max(t)` one period old on Hour or Day is therefore the correct state and must
not be filed as a failure. A curve stopping short of the right edge is a real failure on any layer.

**Executed by exec:**

- branch: continuous-demo-bench

**Tasks and the commit each produced**

| Task | Commit |
| --- | --- |
| 1 Thin a closed period on the server | `651bf27` feat(seeder): thin a closed period on the server |
| 2 Write each period's opening row as the period opens | `9502bb0` feat(seeder): open each coarse period with its first row |
| 3 Flush from the follow loop and correct what it advertises | `e1c3be1` feat(seeder): thin into the coarse layers while following |
| 4 Fill to the current moment and drop the seeded template | `1e87bd5` fix(bench): fill the archive up to the current moment |
| 5 Verify acceptance criteria | `02f3b10` test(bench): record the acceptance evidence |
| 6 Update documentation | `5364fcd` docs(bench): describe the continuous demo stand |

**Review phases.** Two ran: a comprehensive pass with two agents, then a critical pass with one
agent. The external `codex` phase did **not** run — `codex` is not installed on this machine.

**What the review changed**, grouped, in five commits:

| Finding | Commit |
| --- | --- |
| A tick spanning several periods flushed only one and left the rest with no coarse row ever, the branch's own defect reintroduced at a smaller scale | `821f8e1` fix(seeder): flush every period a tick leaves |
| The two cases those fixes need, neither of which existed | `0b6ac77` test(seeder): cover skipped periods and a null value |
| `$SeedEnd`'s default was culture-sensitive and broke the script under a non-invariant locale | `fd5e95c` fix(bench): render the seed end culture-invariantly |
| The stated seam margin did not exist, and four documentation statements were false | `fe65fc4` docs(bench): correct the seam margin and the row counts |
| The remaining prose corrections in the seeder and the stand documentation | `6d99288` docs(seeder): sharpen the demo stand prose |

The load-bearing `ORDER BY v DESC NULLS LAST` was asserted by no test before `0b6ac77`. Both added
tests were proven load-bearing by reverting the code they guard:
`ACallSpanningSeveralPeriodsClosesEveryOneOfThem` fails against the pre-`821f8e1` `CoarseFlush`
(minute 23:11 empty), and `ANullValuedRawRowIsNotSelectedAsAPeriodsMaximum` fails when `NULLS LAST`
is dropped (count 1 instead of 0).

**Final measured numbers at HEAD**

| Check | Result |
| --- | --- |
| `dotnet build SemiPlot.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format SemiPlot.slnx --verify-no-changes` | exit 0 |
| `SemiPlot.Tests` | **362** passed, 0 failed, 0 skipped |
| `SemiPlot.Tests.Data`, `SEMIPLOT_REQUIRE_DB=1` | **495** passed, 0 failed, 0 skipped |
| `SemiPlot.Tests.Journeys`, `SEMIPLOT_REQUIRE_DB=1` | **4** passed, 0 failed, 0 skipped |

The critical pass returned **NO CRITICAL FINDINGS**.

**Live-edge evidence, which is what this plan exists for.** Across a recorded run of the demo
writer: layer 1 stepped once a minute; layer 2 stepped at an hour crossing, caught live; the
boundary tick reported **55 coarse rows** against 26 to 32 on an ordinary minute; and every tick
crossing no boundary after a period's opening row had landed reported **0**, because
`ON CONFLICT DO NOTHING` absorbs the opening insert. The coarse layers do not densify toward raw.
