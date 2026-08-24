# The Simple-Scada 2 archive (archive system v2)

Reference for the vendor's archive as it exists, independent of what SemiPlot does with it.
How SemiPlot reads it is in `data-integration.md`; the database instance we ship is in
`postgres-instance.md`. Claim provenance follows the convention in `sources.md`.

## Scope and ownership

Left to itself, Simple-Scada 2 creates the archive tables, writes them, thins them, creates their
partitions and deletes old data. This document describes that vendor behaviour; on a SemiPlot site
`semibase site` creates `public.trends` ahead of the SCADA, which `postgres-instance.md` states.
SemiPlot is a strict read-only consumer either way `[DEC:read-only-consumer]`.

Consequences that constrain every design decision downstream:

- The schema is a vendor internal, not a sanctioned integration surface. A product upgrade or an
  archive reconfiguration may recreate the tables.
- We never `ALTER` these tables and never create indexes or triggers on them. Anything we need
  additionally lives in our own `semiplot_*` objects `[DEC:additive-objects]`.
- Archive system v1 used a different, per-variable table layout and is not supported here. Only
  v2 is described below.

## Database objects

Verified against a real archive `[MEAS:dump-20260805]`. Engine is PostgreSQL; the vendor also
supports MySQL and MS SQL Server, which SemiPlot does not target.

```sql
CREATE TABLE public.trends (
    id integer  DEFAULT 0 NOT NULL,
    l  smallint DEFAULT 0 NOT NULL,
    t  timestamp(3) without time zone NOT NULL,
    v  double precision,
    q  integer NOT NULL
) PARTITION BY RANGE (t);

ALTER TABLE ONLY public.trends ADD CONSTRAINT tpk PRIMARY KEY (id, l, t);

CREATE TABLE public.messages (
    t   timestamp(3) without time zone NOT NULL,
    gid integer  DEFAULT 0 NOT NULL,
    mid integer  DEFAULT 0 NOT NULL,
    k   smallint DEFAULT 0 NOT NULL,
    n   character varying(255),
    v   character varying(255),
    uid integer  DEFAULT '-1'::integer NOT NULL,
    r   timestamp(3) without time zone,
    c   timestamp(3) without time zone
) PARTITION BY RANGE (t);

ALTER TABLE ONLY public.messages ADD CONSTRAINT mpk PRIMARY KEY (t, gid, mid);
```

Both tables are range-partitioned by time, one partition per calendar day, created automatically
`[FORUM:1388]`:

| Table | Partition name | Bounds | Catch-all |
| --- | --- | --- | --- |
| `trends` | `tpYYYYmMMdDD`, e.g. `tp2026m08d05` | `FROM ('2026-08-05 00:00:00') TO ('2026-08-06 00:00:00')` | `tpdefault` |
| `messages` | `mpYYYYmMMdDD` | same shape | `mpdefault` |

Partitioning exists so that deleting old data is a metadata operation on a whole day rather than a
row-by-row delete, which is why deletion cost does not grow with archive size `[MAN:archsysv2]`.

`PRIMARY KEY (id, l, t)` on `trends` is also the only index available for reads. Its leading column
is `id`, which dictates the shape of every query — see *Reader hazards*.

There is no table mapping a variable number to a variable name. The archive stores numbers only.

## Column glossary

### `trends` — archived variable values

| Column | Type | Full name | Meaning |
| --- | --- | --- | --- |
| `id` | `integer` | identifier | Project variable number. The only identity in the archive; no name is stored anywhere. |
| `l` | `smallint` | layer | Archive layer, i.e. degree of thinning: `0` main, `1` minute, `2` hour, `3` day `[MAN:tablestruct]`. |
| `t` | `timestamp(3)` | time | Instant of the sample, millisecond precision, no time zone. |
| `v` | `double precision` | value | The archived value. Nullable by DDL, but never observed null `[MEAS:dump-20260805]`. |
| `q` | `integer` | quality | OPC UA quality code, with the two low hexadecimal digits reused as break marks `[MAN:tablestruct]`. |

### `messages` — events and alarms

| Column | Type | Full name | Meaning |
| --- | --- | --- | --- |
| `t` | `timestamp(3)` | time | Instant of the event. |
| `gid` | `integer` | group identifier | Message group. Negative values are system groups; `-6` is the project itself, `-5` a connected client. |
| `mid` | `integer` | message identifier | Sequence number within the group. |
| `k` | `smallint` | kind | Message class: alarm, warning, or normal event. |
| `n` | `varchar(255)` | name | Source name, e.g. the project name or a client address. |
| `v` | `varchar(255)` | value | Message text, e.g. «Проект запущен». |
| `uid` | `integer` | user identifier | Originating user, `-1` for system messages. |
| `r`, `c` | `timestamp(3)` | — | Alarm recovery and acknowledgement instants; null for non-alarm events. |

SemiPlot reads `messages` for one purpose only: explaining a gap to the operator. It is not a data
source for trends.

## Time semantics

`t` is naive local wall-clock time of the machine running the SCADA server. The column type carries
no zone, and the database stores the zone nowhere.

The provider converts at its own boundary using a configured source time zone; everything above the
provider works in UTC. See `data-integration.md`.

## Layers

The layer column is not a separate table or a separate concept — it is a label on rows of the same
table. The engine writes each sample into the main layer and additionally writes a thinned selection
into three coarser layers.

What a coarse layer contains:

- **Verbatim copies of raw rows.** Every coarse row reproduces the timestamp, value and quality of
  an existing `l = 0` row; 170 of 170 matched in the measured archive `[MEAS:dump-20260805]`. There
  are no computed aggregates, no averages, and no bucket-aligned synthetic timestamps.
- **Strictly nested sets**: `l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0` `[MEAS:dump-20260805]`.
- **A budget of four points per period**: four per minute in `l=1`, four per hour in `l=2`, four per
  day in `l=3` `[FORUM:1032]`. Fewer when the variable changed less; sixty changes within a minute
  leave two to four minute-layer points `[FORUM:1454]`.
- **Selected by magnitude, not by time**: the maximum deviation of the trend over the interval is
  what gets taken `[FORUM:1974]`.

Taken together — a fixed budget of four, extremes guaranteed, every stored row a real sample — this
is the classic first / last / minimum / maximum selection, deduplicated when those coincide. The
identity of the two non-extreme points is inference rather than vendor statement, `UNVERIFIED`.

**Half of that inference now has evidence.** In one measured minute the coarse layer carries a row
that is neither an extreme nor a marker and that is the last raw row of its minute, so *last of the
period* is one of the two non-extreme points `[MEAS:dump-20260805]`. *First of the period* stays
inferred: in every minute of the measured sample the first raw row is a marker, which is copied into
every layer anyway, so nothing there separates the two readings.

**Which row survives when the extreme value repeats: the later one** `[MEAS:dump-20260805]`. Where a
period's minimum or maximum occurs at two instants, the coarse layer carries the row with the larger
timestamp — the last poll tick still holding that value, which is the corner where the step ends.
The extreme *values* are identical either way, so an envelope read from a coarse layer is unaffected
by the tie-break; only the abscissa of the point moves. A reader that reproduces the selection rule
for its own purposes has to know this: taking the earliest row instead keeps the same envelope and
loses the width of the step.

**Which minimum and maximum survive.** Those of the period, not of the whole archive. A minute-layer
row set for one minute carries that minute's lowest and highest samples. The silhouette of the trend
therefore survives at every zoom level: the amplitude of an excursion is preserved, only its shape
within the period is lost. Five oscillations inside one minute collapse into a single vertical span
between that minute's extremes.

**Point spacing implied by the budget**, which is what decides when a layer is usable for rendering:

| Layer | Period | Points per period | Effective spacing |
| --- | --- | --- | --- |
| `0` | — | every change | the variable's archiving interval |
| `1` | minute | up to 4 | 15 s |
| `2` | hour | up to 4 | 15 min |
| `3` | day | up to 4 | 6 h |

## Quality and gaps

A **gap** is an interval during which archiving did not happen. It is not "value unknown" — it is
"no data was recorded, and none ever will be".

Three states are easy to confuse and must be distinguished by a reader of this archive:

| State | Rows present | Correct rendering |
| --- | --- | --- |
| Value unchanged | none | horizontal line at the last recorded value |
| Gap | none | broken line |
| Bad quality | row present, value present | point discarded |

Absence of rows alone cannot separate the first two, which is why the engine marks the boundaries in
the quality column. The manual states that the quality code follows the OPC UA specification except
for its two low hexadecimal digits, which may carry break marks, and that `0x00000000`, `0x00000010`
and `0x00000020` all mean good quality `[MAN:tablestruct]`.

Measured assignment `[MEAS:dump-20260805]`:

| `q` | Meaning |
| --- | --- |
| `0` | ordinary sample |
| `16` (`0x10`) | first sample after a break |
| `32` (`0x20`) | last sample before a break |

Both marker rows carry a valid value — they are real data points that additionally flag a boundary.
Every marker pair in the measured archive aligned within 30 ms with a `messages` row from group `-6`
reading «Проект остановлен» or «Проект запущен».

Marker rows are copied into every layer unchanged, so gap boundaries survive thinning and a broken
line renders correctly at any zoom level `[MEAS:dump-20260805]`.

A gap is **not** encoded as a null value: `v` was never null anywhere in the measured archive.

## Write behavior

Per-variable archiving settings control admission into the **main** layer only: archiving type (by
time, by change, or combined), archiving interval from 100 ms to one hour, and a deadband expressed
as a percentage of the variable's scale `[MAN:vararchive]`.

With change-based archiving the engine writes **two rows per change** `[MEAS:dump-20260805]`: the
previous value at the last poll tick before the change, then the new value at the change tick. The
observed poll interval was 100 ms.

```
13:50:44.113  v=0     last tick holding 0
13:50:44.213  v=522   the change
13:50:46.337  v=522   last tick holding 522
13:50:46.437  v=313   the next change
```

Two consequences. The archive is explicitly step-shaped, with the corner of each step anchored by a
real sample, so linear interpolation between a pair is exact. And row count scales with the number
of changes rather than with elapsed time — a quiet variable costs almost nothing.

**The poll tick jitters.** Of 34 change rows in the measured archive, 30 sat exactly 100 ms after
their predecessor and 4 sat 104 to 109 ms after it; the four late ones fall at the same two instants
for both variables, so the tick itself ran late rather than one variable being treated differently
`[MEAS:dump-20260805]`. Two rows closer together than the poll interval were never observed. A
reader that keys anything on the pair spacing must allow roughly 10 ms of tolerance rather than
demand an exact interval.

Values accumulate in memory and reach the database on periodic flushes; a rarely changing variable
reaches it rarely, though a write can also land within a millisecond `[FORUM:1847]`. During a
database outage the engine accumulates up to roughly two million records in memory `[MAN:archsysv2]`,
which are written afterwards carrying their original timestamps.

Per-layer flush cadence was one minute for the minute layer, one hour for the hour layer and one day
for the day layer, with the hour and day layers additionally backed up every ten minutes
`[FORUM:345]`. That statement describes archive system v1; whether v2 kept these cadences is
`UNVERIFIED`.

**Freshness lag.** Because coarse layers are flushed on their own cadence, the newest part of the
archive exists only in the main layer. A wide window that reaches "now" has an empty tail in
`l=1/2/3`, and the read path has to patch it from a finer layer.

## Retention

One project-level setting, «Ограничение архива трендов», bounds how long trends are kept; older
trends are deleted from the database `[MAN:trendsset]`. Messages have their own equivalent setting
`[MAN:messet]`.

There is no per-layer retention. Because all four layers live in the same time-partitioned table,
dropping a day removes that day at every layer at once. A coarse layer therefore cannot outlive the
raw data it was thinned from.

The account under which the SCADA connects needs `ALTER`, `CREATE`, `DROP`, `INSERT`, `SELECT` and
`UPDATE`; `DROP` may be withheld only if both archive limits are set to unlimited `[MAN:db-access-rights]`.

**Thinning is neither configurable nor disableable.** No such setting exists in the manual, and the
word does not occur in any resource string of the editor, server or options applications
`[MEAS:install-inspection]`. The coarse layers are always written; their cost has to be accepted as
fixed overhead, at most about 5860 rows per variable per day at the four-per-period budget — 1440
minutes, 24 hours and one day at four points each. A variable that changes rarely produces fewer.

## Reader hazards

Two mistakes produce silently wrong results rather than errors.

**Omitting the layer predicate.** Coarse rows duplicate the timestamps and values of raw rows, so a
query filtered only by `id` and time returns each point up to four times, and the chart draws
overlapping duplicates. Every read must constrain `l`.

**Predicating a query on time alone.** The only index is `PRIMARY KEY (id, l, t)`, whose leading
column is `id`. A query of the form `WHERE t > @lastSeen` cannot use it and degenerates into a
sequential scan of the current day's partition. Every query must carry the variable list.

A third, operational: if the engine ever fails to create the next daily partition, rows fall into
`tpdefault`, which is never pruned and defeats partition elimination. A non-empty `tpdefault` is a
fault signal.

## Not established

Carried deliberately as open, to be settled by a controlled experiment rather than by more reading.

| Question | Why it is open | Impact |
| --- | --- | --- |
| Are the thinning periods aligned to the calendar minute/hour/day, or to the flush window? | Never stated; measured coarse timestamps are raw sample times and reveal nothing about bucket edges. | Affects accuracy at period boundaries only. |
| Which two of the four points are the non-extreme ones? | Inferred as first and last of the period; no vendor statement. *Last* is confirmed on measured rows, *first* is not — every sample minute opens on a marker row, which is copied regardless. | None for us: we need the extremes and the period edges, and both are present. |
| Did archive system v2 keep the v1 flush cadences? | The cadence statement is v1-era. | Sets how stale the coarse layers are near the live edge. |
| Do the hour and day layers behave like the minute layer? | The measured archive spans two hours with twelve restarts, so `l=2` and `l=3` were never exercised across their own periods. | Wide-window fidelity beyond ten days. |

The experiment that settles the first and the last: run one variable changing every 100–200 ms over
a wide value range, archiving by time, for a continuous run long enough to cover many periods, then
compare each calendar period's raw extremes against the rows present in the coarse layer.

```sql
WITH b AS (
    SELECT id, date_trunc('minute', t) AS bucket,
           min(v) AS vmin, max(v) AS vmax, count(*) AS n
    FROM trends WHERE l = 0 AND t >= :from AND t < :to
    GROUP BY 1, 2
)
SELECT b.id, b.bucket, b.n, b.vmin, b.vmax,
       EXISTS (SELECT 1 FROM trends m WHERE m.l = 1 AND m.id = b.id
               AND m.t >= b.bucket AND m.t < b.bucket + interval '1 minute'
               AND m.v = b.vmin) AS min_kept,
       EXISTS (SELECT 1 FROM trends m WHERE m.l = 1 AND m.id = b.id
               AND m.t >= b.bucket AND m.t < b.bucket + interval '1 minute'
               AND m.v = b.vmax) AS max_kept,
       (SELECT count(*) FROM trends m WHERE m.l = 1 AND m.id = b.id
        AND m.t >= b.bucket AND m.t < b.bucket + interval '1 minute') AS kept_rows
FROM b ORDER BY b.bucket;
```

Value equality is a valid test because coarse rows are byte-identical copies. Two to four
`kept_rows` per period with both flags true confirms the selection rule. A single period with
`max_kept = false` refutes it, and the read path must then stop trusting coarse layers for
envelopes. Failures clustered at period edges indicate flush-window rather than calendar buckets.

The unlicensed `DEMO-TIME` build permits one hour of continuous operation per start, which is
sufficient for the minute layer and not for the hour layer.
