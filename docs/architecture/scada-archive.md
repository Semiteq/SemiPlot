# The Simple-Scada 2 archive (archive system v2)

Reference for the vendor's archive as it exists, independent of what SemiPlot does with it.
How SemiPlot reads it is in `data-integration.md`; the database instance we ship is in
`postgres-instance.md`. Claim provenance follows the convention in `sources.md`. The full record of
the 48-hour run behind every `[MEAS:capture-20261006]` claim, with the statements verbatim, the
counts and the reproduction recipe, is `scada-archive-observed.md`.

## Scope and ownership

Left to itself, Simple-Scada 2 creates the archive tables, writes them, thins them, creates their
partitions and deletes old data. This document describes that vendor behaviour; on a SemiPlot site
`semibase site` creates `public.trends` ahead of the SCADA, which `postgres-instance.md` states,
and the SCADA uses that table as found `[MEAS:capture-20261006]`. SemiPlot is a strict read-only
consumer either way `[DEC:read-only-consumer]`.

Consequences that constrain every design decision downstream:

- The schema is a vendor internal, not a sanctioned integration surface. A product upgrade or an
  archive reconfiguration may recreate the tables.
- We never `ALTER` these tables and never create indexes or triggers on them. Anything we need
  additionally lives in our own `semiplot_*` objects `[DEC:additive-objects]`.
- Archive system v1 used a different, per-variable table layout and is not supported here. Only
  v2 is described below.
- Behaviour differs between builds: the day layer is the known case (see *Layers*). Statements
  marked `[MEAS:capture-20261006]` were measured on 2.7.6.0 (`demo-64`); `[MEAS:dump-20260805]`
  comes from a customer installation of unknown build.

## Database objects

Verified against a real archive `[MEAS:dump-20260805]` and against the statements the server issues
`[MEAS:capture-20261006]`. Engine is PostgreSQL; the vendor also supports MySQL and MS SQL Server,
which SemiPlot does not target.

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

Both tables are range-partitioned by time, one partition per calendar day of local wall-clock time
`[FORUM:1388]` `[MEAS:capture-20261006]`:

| Table | Partition name | Bounds | Catch-all |
| --- | --- | --- | --- |
| `trends` | `tpYYYYmMMdDD`, e.g. `tp2026m08d05` | `FROM ('2026-08-05 00:00:00') TO ('2026-08-06 00:00:00')` | `tpdefault` |
| `messages` | `mpYYYYmMMdDD` | same shape | `mpdefault` |

The server creates them itself `[MEAS:capture-20261006]`:

- **At every project start** it runs `CREATE TABLE IF NOT EXISTS trends (...)`, which skips an
  existing table, then `DROP TABLE IF EXISTS tpdefault`, recreates `tpdefault`, creates the
  partitions for yesterday, today and tomorrow, and runs `TRUNCATE TABLE tpdefault`. The same for
  `messages`.
- **Every night at 02:00** it runs `TRUNCATE TABLE tpdefault` and creates tomorrow's partition, the
  same for `messages`.

Partitioning exists so that deleting old data is a metadata operation on a whole day rather than a
row-by-row delete, which is why deletion cost does not grow with archive size `[MAN:archsysv2]`;
the server deletes with `DROP TABLE IF EXISTS tp<day>` (see *Retention*).

`PRIMARY KEY (id, l, t)` on `trends` is also the only index available for reads. Its leading column
is `id`, which dictates the shape of every query; see *Reader hazards*.

The archive tables store variable numbers only. An optional table `variables (ID integer, Name
varchar(512), Description varchar(1024), CONSTRAINT vpk PRIMARY KEY (ID))` maps numbers to names;
the editor's database action «создать таблицу переменных» ("create the variables table") creates
it with `TRUNCATE` and `INSERT`. It is a snapshot: adding a variable, saving the project and
restarting it leave it unchanged `[MEAS:capture-20261006]`. The manual calls it `variables_data`,
the name of the v1 table.

## Column glossary

### `trends`: archived variable values

| Column | Type | Full name | Meaning |
| --- | --- | --- | --- |
| `id` | `integer` | identifier | Project variable number, assigned by the editor. The only identity in the archive. |
| `l` | `smallint` | layer | Archive layer, i.e. degree of thinning: `0` main, `1` minute, `2` hour, `3` day `[MAN:tablestruct]`. |
| `t` | `timestamp(3)` | time | Instant of the sample, millisecond precision, no time zone. |
| `v` | `double precision` | value | The archived value. Nullable by DDL, but never observed null `[MEAS:dump-20260805]` `[MEAS:capture-20261006]`. |
| `q` | `integer` | quality | OPC UA quality code, with the two low hexadecimal digits reused as break marks `[MAN:tablestruct]`. |

### `messages`: events and alarms

| Column | Type | Full name | Meaning |
| --- | --- | --- | --- |
| `t` | `timestamp(3)` | time | Instant of the event. |
| `gid` | `integer` | group identifier | Message group. Negative values are system groups; `-6` is the project itself, `-5` a connected client. |
| `mid` | `integer` | message identifier | Sequence number within the group. |
| `k` | `smallint` | kind | Message class: alarm, warning, or normal event. |
| `n` | `varchar(255)` | name | Source name, e.g. the project name or a client address. |
| `v` | `varchar(255)` | value | Message text, e.g. «Проект запущен». |
| `uid` | `integer` | user identifier | Originating user, `-1` for system messages. |
| `r`, `c` | `timestamp(3)` | | Alarm recovery and acknowledgement instants; null for non-alarm events. |

SemiPlot reads `messages` for one purpose only: explaining a gap to the operator. It is not a data
source for trends. A project start writes «Проект запущен» and a stop «Проект остановлен»
(`gid = -6`, `k = 2`); a database outage writes nothing there `[MEAS:capture-20261006]`.

## Time semantics

`t` is naive local wall-clock time of the machine running the SCADA server: the SCADA stamps it on its
own process clock `[MEAS:dump-20260805]`, and that clock is taken to be in the machine's Windows zone
`[DEC:machine-time-zone]`. The column type carries no zone, and the database stores the zone nowhere.
The partition bounds are in the same local time `[MEAS:capture-20261006]`.

The SCADA, its archive and the viewer share one machine, so the provider converts at its own boundary
in that machine's zone; everything above the provider works in UTC. See
`data-integration.md#time-boundary`.

## Layers

The layer column is not a separate table or a separate concept: it is a label on rows of the same
table. The engine writes each sample into the main layer and additionally writes a thinned selection
into the coarser layers.

What a coarse layer contains `[MEAS:capture-20261006]`:

- **Verbatim copies of raw rows.** Every coarse row reproduces the timestamp, value and quality of
  an existing `l = 0` row; no computed aggregates, no averages, no bucket-aligned synthetic
  timestamps `[MEAS:dump-20260805]`.
- **The minimum and the maximum of each calendar period.** For each variable and each calendar
  minute (`l = 1`) or calendar hour (`l = 2`), the layer holds the raw row with the smallest value
  and the raw row with the largest value, one row when they are the same row. Checked over 10 340
  variable-minutes and 193 variable-hours; the only misses were periods whose copy had not arrived
  yet.
- **Not the first and the last.** The first and the last row of a period appear only when they are
  also an extreme. The forum's "four points per period" `[FORUM:1032]` does not hold for this
  build: a period holds at most two points apart from markers.
- **Periods are calendar-aligned** (`date_trunc` boundaries). A project restart splits a period,
  and each run segment gets its own minimum and maximum.
- **Markers** (`q = 16`, `q = 32`) are copied into layers 1 and 2 in addition to the extremes, so
  gap boundaries survive thinning `[MEAS:dump-20260805]`.
- **One known exception**: a stepped integer variable kept a third, non-extreme row in 4 of its
  minutes on one day. The rule for it is not established.

**Which row survives when the extreme value repeats** `[MEAS:capture-20261006]`. A repeated maximum
keeps its latest occurrence (111 of 111 hours, 498 of 498 minutes); a repeated minimum keeps its
earliest (115 of 115 hours, 663 of 663 minutes). The extreme *values* are identical either way, so
an envelope read from a coarse layer is unaffected; only the abscissa of the point moves. A reader
that reproduces the selection for its own purposes has to apply the same asymmetric tie-break.

**Which minimum and maximum survive.** Those of the period, not of the whole archive. The silhouette
of the trend therefore survives at every zoom level: the amplitude of an excursion is preserved,
only its shape within the period is lost. Five oscillations inside one minute collapse into a
single vertical span between that minute's extremes.

**When coarse rows arrive.** Not at the end of the period: a period's rows ride along with the
variable's next batch after the period has closed. For the hour 23:00-24:00 the hour-layer rows of
fast variables arrived within 3 minutes after midnight, those of slow variables up to 47 minutes
later `[MEAS:capture-20261006]`. A variable's coarse rows are as late as its main-layer rows (see
*Write behavior*).

**The day layer depends on the build.** The customer archive holds `l = 3` rows within the same day,
markers included `[MEAS:dump-20260805]`. Build 2.7.6.0 (`demo-64`) wrote no `l = 3` row at all in
48 hours, including a full calendar day without a restart `[MEAS:capture-20261006]`. A reader cannot
assume layer 3 is populated.

**Point spacing implied by the selection**, which is what decides when a layer is usable for
rendering:

| Layer | Period | Points per period | Effective spacing |
| --- | --- | --- | --- |
| `0` | | every archived change | the variable's archiving interval |
| `1` | minute | up to 2 | 30 s |
| `2` | hour | up to 2 | 30 min |
| `3` | day | up to 2 where written | 12 h |

## Quality and gaps

A **gap** is an interval during which archiving did not happen. It is not "value unknown": it is
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

Measured assignment `[MEAS:dump-20260805]` `[MEAS:capture-20261006]`:

| `q` | Meaning |
| --- | --- |
| `0` | ordinary sample |
| `16` (`0x10`) | first sample after a break, written at the project start instant |
| `32` (`0x20`) | last sample before a break, written at the project stop instant |

Both marker rows carry a valid value: they are real data points that additionally flag a boundary.
They are written for a project stop and start, aligned with the `messages` rows «Проект остановлен»
and «Проект запущен». They are copied into layers 1 and 2 `[MEAS:capture-20261006]`, and into
layer 3 where that layer is written `[MEAS:dump-20260805]`.

**A database outage is not a break.** The server keeps collecting, sends the backlog with the
original timestamps after it reconnects, and writes no marker `[MEAS:capture-20261006]`. If it loses
a batch on the broken connection (see *Write behavior*), the hole carries no mark at all and renders
as a straight line, indistinguishable from an unchanged value.

A gap is **not** encoded as a null value: `v` was never null anywhere.

## Write behavior

Per-variable archiving settings control admission into the **main** layer only: archiving type (by
time, by change, or combined), archiving interval from 100 ms to one hour, and a deadband expressed
as a percentage of the variable's scale `[MAN:vararchive]`. "By time" admits a new value when "more
time has passed than the interval"; with a 1 s interval and a value changing every second, 37 % of
seconds produced no row `[MEAS:capture-20261006]`.

**The statement** `[MEAS:capture-20261006]`:

```sql
INSERT INTO trends (id,t,v,q,l) VALUES (5,'2026-10-06 13:00:01.583',1.91,0,0),(5,...), ... ON CONFLICT DO NOTHING;
```

A multi-row `INSERT` with literal values and `ON CONFLICT DO NOTHING`, normally one variable per
statement, median 49 rows. A statement carries main-layer rows together with any coarse rows that
became due for that variable. The server writes through four insert threads over a pool of
connections. It issues no `UPDATE` or `DELETE` on `trends`.

**Per-variable buffering.** Values accumulate in a buffer per variable in the server and reach the
database when the buffer holds about 50 rows or after about 8 minutes `[MEAS:capture-20261006]`. No
setting controls either. The resulting delay between a sample's instant and its arrival in the
table:

| Kind of variable | Typical delay | Longest observed |
| --- | --- | --- |
| changes every second | under 1 min | 3 min |
| writes every 10 s to every minute | ~8 min | 9 min |
| changes a few times an hour | 5 to 8 min | 2 h |
| never changes | | 12 h; one main-layer row per day, at a fixed time of day |

So the newest row of a variable in the archive lags the plant by minutes, and by hours for a
rarely changing variable `[FORUM:1847]` `[MEAS:capture-20261006]`. The archive is not a live source.

**Two rows around a change.** With change-based archiving the engine writes the previous value as a
separate row ahead of a change, an *anchor*. In the customer archive, polled every 100 ms, the
anchor sits one poll tick before every change `[MEAS:dump-20260805]`:

```
13:50:44.113  v=0     last tick holding 0
13:50:44.213  v=522   the change
13:50:46.337  v=522   last tick holding 522
13:50:46.437  v=313   the next change
```

In build 2.7.6.0 the anchor appeared only for a change that came more than 600 s after the previous
row, dated exactly 600 s before the change `[MEAS:capture-20261006]`. Either way the corner of each
step is anchored by a real sample, so linear interpolation between a pair is exact, and row count
scales with the number of changes rather than with elapsed time.

**The poll tick jitters.** Of 34 change rows in the customer archive, 30 sat exactly 100 ms after
their predecessor and 4 sat 104 to 109 ms after it `[MEAS:dump-20260805]`. A reader that keys
anything on the pair spacing must allow roughly 10 ms of tolerance.

**Project stop.** The server flushes every buffer in one statement and writes the stop markers.
A stop is the only moment the archive is complete up to the present `[MEAS:capture-20261006]`.

**Database outage.** During an outage the engine accumulates up to roughly two million records in
memory `[MAN:archsysv2]` and writes them afterwards with their original timestamps
`[MEAS:capture-20261006]`. When the connection breaks, the batch an insert thread is holding can be
lost: in two of three forced outages that broke connections, one batch of about 50 rows of one
variable never reached the database, with no error in the project log and no marker. A paused
database (connection open, nothing answering) lost nothing. The server's own log
(`Logs\Server-log.txt` in the user's Simple-Scada folder) shows `Insert thread N - con. lost 1`.

**Freshness of coarse layers.** Because coarse rows travel with the variable's batches, a wide
window that reaches "now" has an empty tail in `l = 1/2/3`, and the read path has to patch it from a
finer layer. The v1-era statement that layers flush every minute, hour and day `[FORUM:345]` does not
describe v2.

## Retention

One project-level setting, «Ограничение архива трендов», bounds how long trends are kept; older
trends are deleted from the database `[MAN:trendsset]`. The choices are days, months, a year, or
«Без ограничений» (no limit). Messages have their own equivalent setting `[MAN:messet]`.

How the server applies it `[MEAS:capture-20261006]`:

- Every 93.6 s it lists the `trends` partitions whose names sort at or below the cut-off and runs
  `DROP TABLE IF EXISTS tp<day>` for each. The cut-off moves at 02:00. The first check after a
  project start comes about 95 s after it, so an expired day is dropped then too.
- Between 23:58 and 02:00 the check does not run.
- With a 1-day limit, only the current calendar day survives 02:00.

There is no per-layer retention. Because all four layers live in the same time-partitioned table,
dropping a day removes that day at every layer at once. A coarse layer therefore cannot outlive the
raw data it was thinned from.

A row that arrives after its day was dropped lands in `tpdefault` and is erased at the next 02:00 or
the next start. With a limit set, the late rows of slow and constant variables are lost this way.

The account under which the SCADA connects needs `ALTER`, `CREATE`, `DROP`, `INSERT`, `SELECT` and
`UPDATE`; `DROP` may be withheld only if both archive limits are set to unlimited `[MAN:db-access-rights]`.

**Thinning is neither configurable nor disableable.** No such setting exists in the manual, and the
word does not occur in any resource string of the editor, server or options applications
`[MEAS:install-inspection]`. The coarse layers are always written; their cost is fixed overhead, at
most about 2 900 rows per variable per day at two points per period: 1 440 minutes and 24 hours at
two each. A variable that changes rarely produces fewer.

## Reader hazards

Three mistakes produce silently wrong results rather than errors.

**Omitting the layer predicate.** Coarse rows duplicate the timestamps and values of raw rows, so a
query filtered only by `id` and time returns a point up to three or four times, and the chart draws
overlapping duplicates. Every read must constrain `l`.

**Predicating a query on time alone.** The only index is `PRIMARY KEY (id, l, t)`, whose leading
column is `id`. A query of the form `WHERE t > @lastSeen` cannot use it and degenerates into a
sequential scan of the current day's partition. Every query must carry the variable list.

**Reading a coarse layer without a time bound.** The server writes junk rows dated `1899-12-30
00:00:00` (Delphi's zero date, `v = 0`, `q = 0`) into layers 1 and 2, one per variable and layer in
each project run. They land in `tpdefault` and stay there until 02:00 or the next start, so
`min(t)` over a coarse layer without a time predicate returns 1899-12-30 `[MEAS:capture-20261006]`.

**`tpdefault` is not a fault signal by itself.** It always holds the junk rows above and, with a
limit set, late rows of dropped days. Only rows dated today or later mean that a day partition was
missing at write time; the server erases those too within a day, so a check for them has to run
before 02:00.

## Not established

Carried deliberately as open, to be settled by a controlled experiment rather than by more reading.

| Question | Why it is open | Impact |
| --- | --- | --- |
| Which builds or licences write the day layer? | The customer archive has it, 2.7.6.0 `demo-64` does not. | Wide windows read `l = 3`; an empty layer draws nothing. |
| What sets the anchor offset: one poll tick, or 600 s? | The customer archive and 2.7.6.0 differ. | None for rendering: either way the step corner is a real sample. |
| Why does a stepped integer sometimes keep a third row in a minute? | Seen in 4 minutes of one variable. | None for envelopes: the extremes are still present. |
| Which broken-connection case loses the in-flight batch? | Two of three outages that broke connections lost one, the third none. | Silent holes; restart PostgreSQL only with the project stopped. |
| Why do rarely changing variables wait far longer than the ~8 min flush? | Not visible from the database side. | Freshness of the newest point. |

The selection-rule check, the tie-break check and the anchor check are given as SQL in
`scada-archive-observed.md#reproduction`; they run against any archive.

The unlicensed `DEMO-TIME` build permits one hour of continuous operation per start; the `demo-64`
build (64 tags, no time limit) runs indefinitely and is the one to use for any further experiment.
