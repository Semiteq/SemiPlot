# Simple-Scada 2 archive: observed behaviour

This document records what a running Simple-Scada 2 server did to a PostgreSQL archive over 48
hours, statement by statement. It answers the questions the vendor manual leaves open: which SQL the
server issues and when, how values travel from the server's memory to the table, how the coarse
layers are selected and when they arrive, what retention does, what a database outage costs, and
how much disk a year takes. Every fact below was read from the PostgreSQL statement log, from the
server's own log file, or from the archive table itself during that run, marker
`[MEAS:capture-20261006]` in `sources.md`. Where a sentence is an inference rather than an
observation, it says so.

`scada-archive.md` is the reference built earlier from the manual, the forum and a two-hour customer
dump. Where the two disagree, this document is the measured one; the differences are listed in
[Manual and earlier reference versus observation](#manual-and-earlier-reference-versus-observation).
Everything here holds for the build and licence named below. A different build may behave
differently, and [Open questions](#open-questions) says where that is already known to happen.

Terms used throughout:

- **Layer** is the `l` column of `trends`: 0 holds every archived value, 1 (minute), 2 (hour) and
  3 (day) hold thinned copies.
- **Batch** is the set of rows the server sends in one `INSERT` statement.
- **Marker** is a row whose quality `q` is 16 (first value after a break) or 32 (last value before a
  break).
- **Anchor** is a row that repeats a variable's previous value, written together with a change and
  dated ahead of it.

## The run

### Software

| Part | Version and setting |
| --- | --- |
| Simple-Scada 2 | 2.7.6.0, `demo-64` licence (64 tags, no time limit), installed in `C:\Program Files (x86)\Simple-Scada 2 (demo64)` |
| Archive system | v2 (`Проект -> Настройки -> База данных`) |
| Database | PostgreSQL 17.11 (`postgres:17-alpine`) in the container `scada-capture`, built from `SemiPlot/bench` and provisioned by `semibase bench` |
| Database name | `semiplot_provisioned`; the SCADA connects as `scada_writer` |
| Trends archive limit («Ограничение архива трендов») | 1 day |
| Messages archive limit | unlimited |
| Time zone | Windows `Russian Standard Time` (UTC+3); the server log prints `UTC offset = 10800`; the container runs `Europe/Moscow`, so log times and `t` agree |

`semibase bench` created `public.trends` with `PRIMARY KEY (id, l, t)` and the `tpdefault` partition
before the SCADA ever connected, as `semibase site` does on a plant. The SCADA created `messages`
and every day partition.

The PostgreSQL side logged every statement with millisecond timestamps:

```
-c log_statement=all -c logging_collector=on -c log_directory=log
-c log_filename=postgresql-%Y-%m-%d_%H.log -c log_rotation_age=60 -c log_rotation_size=0
-c "log_line_prefix=%m [%p] %u@%d %a xid=%x " -c log_connections=on -c log_disconnections=on
-c log_lock_waits=on -c log_timezone=Europe/Moscow -c timezone=Europe/Moscow
```

Besides the SCADA server, the log holds three other sources, all separable by user and
application name: the provisioning by `postgres` at 12:47, the editor's "create the variables
table" action at 13:04:39 (three `scada_writer` connections of its own), and the observer's
read-only queries through `psql`. The counts below exclude all three.

### The project

One project, `test_project`, with internal variables only (no OPC server). A timer script runs every
second, the shortest interval the script engine allows, and moves each variable by a random step
inside 0..100:

```pascal
procedure Walk(AVar: TM_Variable; AStep: Double);
var
   x: Double;
begin
   x := AVar.AsFloat + (Random(2001) - 1000) / 1000 * AStep;
   if x < 0 then x := 0;
   if x > 100 then x := 100;
   AVar.Value := x;
end;

begin
   Walk(Fast1, 2);   Walk(Fast2, 2);   Walk(Fast3, 5);   Walk(Fast4, 5);
   Walk(Band1, 2);   Walk(Band2, 2);   Walk(Band3, 5);   Walk(Band4, 5);
   Walk(Time1s, 2);  Walk(Time10s, 2); Walk(Time1m, 2);  Walk(Time1h, 2);
   Walk(Comb1, 1);   Walk(Comb2, 1);
   if Random(120) = 0 then Step1.Value := Random(4);
   if Random(600) = 0 then Step2.Value := Random(4);
end.
```

| `id` | Variable | Type | Archiving | Layer-0 rows written |
| --- | --- | --- | --- | --- |
| 2-5 | `Fast1`-`Fast4` | Double | by change, deadband 0 | ~0.98 per second |
| 6-7 | `Band1`, `Band2` | Double | by change, deadband 1 % | ~0.49 per second |
| 8-9 | `Band3`, `Band4` | Double | by change, deadband 1 % | ~0.78 per second |
| 10 | `Time1s` | Double | by time, 1 s | ~0.74 per second |
| 11 | `Time10s` | Double | by time, 10 s | ~0.094 per second |
| 12 | `Time1m` | Double | by time, 1 min | ~1 per minute |
| 13 | `Time1h` | Double | by time, 1 h | 1 per hour |
| 14-15 | `Comb1`, `Comb2` | Double | combined, deadband 5 %, 1 min | ~1.2 per minute |
| 16 | `Step1` | Integer, stepped | by change | ~22 per hour, anchors included |
| 17 | `Step2` | Integer, stepped | by change | ~6 to 7 per hour, about 30 % of them anchors |
| 18 | `am_i_seen` | Single | by time, 5 s; initial value 1, never written by the script | 1 per day |

"By time" writes fewer rows than its interval suggests. `Time1s` and `Fast1` move by the same step
every second; on 2026-10-08 `Fast1` had 52 710 one-second gaps and 104 two-second gaps between
layer-0 rows, `Time1s` had 24 522 and 14 268. The value sat on the 0 or 100 clamp in only 1.8 % of
`Time1s` rows, so the clamp does not explain the skipped seconds. The manual's wording, a new value
is archived when "more time has passed than the interval", together with a script tick that is not
exactly 1.000 s, would explain it; that explanation is not verified.

### Timeline

| Time (MSK) | Event |
| --- | --- |
| 2026-10-06 12:47 | container started, `semibase bench` provisioned it |
| 13:00:00 | project start 1 |
| 13:04:39 | editor action "create the variables table" |
| 13:09:30 / 13:09:34 | project stop and start 2 |
| 13:47:09 / 13:47:11 | project restart 3 (with `am_i_seen` saved); no project restart after this |
| 14:33:05 to 14:35:07 | database outage 1: `docker stop`, 2 min |
| 18:13:15 to 18:13:48 | database outage 2: `docker stop`, 30 s |
| 18:33:17 to 18:33:49 | database outage 3: `docker kill`, 30 s |
| 18:53:12 to 18:54:13 | database outage 4: `docker pause`, 60 s |
| 2026-10-07 02:00 | first nightly maintenance |
| 2026-10-08 02:00 | second nightly maintenance |
| 2026-10-08 13:25 | end of observation |

## Answers at a glance

| Question | Answer | Section |
| --- | --- | --- |
| Does the SCADA accept a `trends` table someone else created? | Yes. It skips its `CREATE TABLE IF NOT EXISTS` and keeps our primary key. | [Project start](#project-start) |
| Does it ever alter or recreate `trends`? | No. It drops and recreates only `tpdefault`. | [Project start](#project-start) |
| When are day partitions created? | Yesterday, today and tomorrow at every project start; tomorrow again at 02:00 every night. | [Partitions](#partitions-and-retention) |
| When does retention delete data? | At 02:00 every night, and about 95 s after every project start. | [Partitions](#partitions-and-retention) |
| What does a 1-day limit keep? | Only the current calendar day, from 02:00 on. | [Partitions](#partitions-and-retention) |
| How are values inserted? | `INSERT ... VALUES (...),(...) ON CONFLICT DO NOTHING`, one variable per statement, about 50 rows. | [Writing](#how-values-are-written) |
| How late does a value reach the table? | 1 to 3 min for a fast pen, about 8 min for a slow one, up to 2 hours for a rarely changing one, up to a day for a constant one. | [Latency](#how-late-a-value-arrives) |
| Can the buffering be configured? | No setting exists in the editor, the manual or the server options. | [Latency](#how-late-a-value-arrives) |
| What does a change after a long steady stretch look like? | An anchor row with the old value 600 s before the change, then the change. | [Anchor rows](#anchor-rows) |
| What do layers 1 and 2 hold? | Copies of the raw rows holding the minimum and the maximum of each calendar minute or hour. | [Coarse layers](#coarse-layers) |
| What does layer 3 hold? | Nothing. This build wrote no day-layer row in 48 h. | [The day layer](#the-day-layer) |
| What is in `tpdefault`? | Junk rows dated 1899-12-30 and late rows of deleted days; emptied at 02:00 and at every start. | [`tpdefault`](#what-tpdefault-holds) |
| What does a database outage cost? | Usually nothing; a broken connection can silently lose one batch of about 50 rows of one variable. | [Outages](#database-outages) |
| Is there a table of variable names? | Optionally `variables`, filled by an editor button, a snapshot that never updates itself. | [Variables table](#the-variables-table) |
| How much disk? | 93 bytes per row with the primary key; about 3.1 GB per year for a pen that changes every second. | [Disk](#disk-and-volume) |

## Connection and session

- The server first connects to the maintenance database `postgres` and checks that the archive
  database exists: `SELECT datname FROM pg_database WHERE datname = 'semiplot_provisioned';`. Then
  it opens a pool on the archive database. When the database is missing, the server creates it
  with `CREATE DATABASE "<name>" WITH ENCODING = 'UTF8' TABLESPACE = pg_default CONNECTION
  LIMIT = -1;` (the template is in `Editor.exe`; this path was not exercised).
- Every new connection sends `SELECT VERSION()`, `show integer_datetimes`, `show bytea_output` and
  `SET search_path TO public`. No `application_name` is set; it logs as `[unknown]`.
- Catalogue reads go through server-side prepared statements, logged as `execute PRSTMT.../PORTALST...:`
  rather than `statement:`. Each is followed by a query of the database access library (UniDAC) on
  `pg_attribute` that describes the tables it read. A search of the log for `statement:` alone
  misses them.
- The pool held 15 connections until the first database outage and 5 afterwards. Writing kept the
  same pace with 5.
- Over the run the server issued 26 847 `INSERT`, about 3 500 `SELECT`, 134 `SHOW` and 67 `SET`
  statements, plus the `CREATE`, `TRUNCATE` and `DROP` statements described below. It issued no
  `UPDATE`, `DELETE` or `ALTER` against any table.

## Project start

Every start (13:00:00, 13:09:34, 13:47:11) issued the same two batches for `trends`, then the same
two for `messages`:

```sql
CREATE TABLE IF NOT EXISTS trends (id integer NOT NULL DEFAULT 0,l smallint NOT NULL DEFAULT 0,
  t timestamp(3) without time zone NOT NULL,v double precision,q integer NOT NULL,
  CONSTRAINT tpk PRIMARY KEY (id,l,t)) PARTITION BY RANGE (t) ;
DROP TABLE IF EXISTS tpdefault;
CREATE TABLE IF NOT EXISTS tpdefault PARTITION OF trends DEFAULT;
CREATE TABLE IF NOT EXISTS tp2026m10d05 PARTITION OF trends FOR VALUES FROM ('2026-10-05 00:00:00') TO ('2026-10-06 00:00:00');
CREATE TABLE IF NOT EXISTS tp2026m10d06 PARTITION OF trends FOR VALUES FROM ('2026-10-06 00:00:00') TO ('2026-10-07 00:00:00');
CREATE TABLE IF NOT EXISTS tp2026m10d07 PARTITION OF trends FOR VALUES FROM ('2026-10-07 00:00:00') TO ('2026-10-08 00:00:00');

TRUNCATE TABLE tpdefault;
CREATE TABLE IF NOT EXISTS tp2026m10d07 PARTITION OF trends FOR VALUES FROM ('2026-10-07 00:00:00') TO ('2026-10-08 00:00:00');
```

Between the two batches the server checks each partition it created with the catalogue query shown
in [The retention check](#the-retention-check).

What follows from it:

1. A `trends` table that already exists is used as found. The SemiBase-created table kept its
   owner, grants and `tpk` for the whole run.
2. `tpdefault` is dropped and recreated by `scada_writer` at every start. Whatever it held is gone,
   and the new `tpdefault` belongs to `scada_writer`.
3. The SCADA creates `messages` and its `mp...` partitions the same way at the first start.
4. A database outage repeats none of this; only a project start does.

The start writes one row to `messages`, and a stop writes one:

```sql
INSERT INTO messages (t,gid,mid,k,n,v,uid) VALUES ('2026-10-06 13:00:00.862',-6,0,2,'test_project','Проект запущен',-1)ON CONFLICT DO NOTHING;
```

The stop text is «Проект остановлен» ("project stopped"). A database outage writes no row to
`messages`.

### Stop and restart

At a stop the server flushes every buffer in one statement (428 rows at 13:09:30 and 405 rows at
13:47:09, each covering all 16 variables) and closes all connections. For every variable it writes
a stop marker, `q = 32`, at the stop instant into layers 0, 1 and 2. The next start writes a start
marker, `q = 16`, at the start instant into layers 0, 1 and 2. A start marker waits in the buffer
like any row and reaches the table with the variable's first batch. Neither marker is written into
layer 3. The marker rows carry the value the variable had: 0 at a start in this project, because
the script's variables start at 0.

The server's own log for a stop reads `Записываем буферы переменных в БД...` ("writing variable
buffers to the database") and `Буферы переменных успешно записаны в БД!` ("variable buffers written
to the database").

A variable added in the editor reaches the server only through a saved project and a project
restart; saving alone changes nothing on the running server.

## Partitions and retention

Day partitions are `tpYYYYmMMdDD` for `trends` and `mpYYYYmMMdDD` for `messages`, bounded
`[00:00, next 00:00)` in local wall-clock time, plus the catch-all `tpdefault` / `mpdefault`.

### The retention check

Every 93.6 s the server runs one prepared catalogue query (logged as `execute`) followed by the
UniDAC describe query on `pg_attribute`. The condition of the first names the cut-off of the trends
retention:

```sql
SELECT nmsp_parent.nspname AS parent_schema, parent.relname AS parent,
       nmsp_child.nspname AS child_schema, child.relname AS child
FROM pg_inherits
JOIN pg_class parent ON pg_inherits.inhparent = parent.oid
JOIN pg_class child ON pg_inherits.inhrelid = child.oid
JOIN pg_namespace nmsp_parent ON nmsp_parent.oid = parent.relnamespace
JOIN pg_namespace nmsp_child ON nmsp_child.oid = child.relnamespace
WHERE (parent.relname='trends') AND (child.relname<='tp2026m10d05') ORDER BY (child.relname);
```

The comparison is on the partition name as text, which sorts correctly for this naming scheme and
never matches `tpdefault`. Every partition it returns is dropped:

```sql
DROP TABLE IF EXISTS tp2026m10d05;
```

The cut-off was `tp2026m10d05` on 2026-10-06, `tp2026m10d06` from 2026-10-07 02:00:04 and
`tp2026m10d07` from 2026-10-08 02:00:18. The first check after each project start came 95 s after
the start, so the drop of an expired day runs then too: `tp2026m10d05` was dropped at 13:01:36,
13:11:09 and 13:48:46, each time right after the start had recreated it empty.

**Between 23:58 and 02:00 the check does not run.** The last check before midnight was at 23:58:30
and 23:58:44; the next one at 02:00:00, both nights.

The same query for `messages` ran only at each start and at 02:00, 11 times in all. The messages
limit was unlimited, so it never dropped anything.

### The nightly maintenance

At 02:00:00 both nights, in this order:

```sql
TRUNCATE TABLE tpdefault;CREATE TABLE IF NOT EXISTS tp2026m10d08 PARTITION OF trends FOR VALUES FROM ('2026-10-08 00:00:00') TO ('2026-10-09 00:00:00');
TRUNCATE TABLE mpdefault;CREATE TABLE IF NOT EXISTS mp2026m10d08 PARTITION OF messages FOR VALUES FROM ('2026-10-08 00:00:00') TO ('2026-10-09 00:00:00');
DROP TABLE IF EXISTS tp2026m10d06;
```

Before the `TRUNCATE` the server checked whether the partition for the day after tomorrow
(`tp2026m10d09` on the first night) existed. The `DROP` came 4 s (first night) and 18 s (second
night) after the `TRUNCATE`, through the retention check with the moved cut-off. No lock waits were
logged.

### What the limit means in practice

- With a 1-day limit the archive holds only the current calendar day after 02:00, so between 2 and
  26 hours of data. Between 01:05 and 02:05 the database shrank from 38 MB to 13 MB on the first
  night and from 70 MB to 13 MB on the second, when a full day was dropped.
- A row dated a day whose partition is already gone lands in `tpdefault` and is erased at the next
  02:00 or the next start. With a limit this is a guaranteed loss; see
  [How late a value arrives](#how-late-a-value-arrives).
- Each `TRUNCATE`, `CREATE ... PARTITION OF` and `DROP` takes an `ACCESS EXCLUSIVE` lock on
  `trends` for its duration (PostgreSQL semantics), so a reader running at 02:00 or at a project
  start waits for it. The duration was not measured; no lock wait was logged.

## How values are written

```sql
INSERT INTO trends (id,t,v,q,l) VALUES (5,'2026-10-06 13:00:00.862',0,16,0),(5,'2026-10-06 13:00:00.862',0,16,1),(5,'2026-10-06 13:00:00.862',0,16,2),(5,'2026-10-06 13:00:01.583',1.91,0,0), ... ON CONFLICT DO NOTHING;
```

- A plain multi-row `INSERT`, literal values, `ON CONFLICT DO NOTHING`. A repeated row is skipped
  silently.
- One statement carries one variable, occasionally two. Median 49 rows, maximum 166 in normal
  operation, 310 and 695 when an outage backlog was sent.
- A statement carries layer-0 rows together with the layer-1 and layer-2 rows that became due for
  that variable.
- Statements run in parallel over the pool, about 545 per hour for this project.
- Because each statement holds one variable, one variable's rows are expected to sit in the heap in
  runs of about 50 consecutive rows, so that a reader of one variable touches about one heap page
  per 50 rows. The physical placement was not measured.

### When a buffer is flushed

Each variable has its own buffer in the server. It is written when either:

1. it holds about 50 rows, or
2. about 8 minutes have passed (`Time1m` went out as 8 rows every ~8 min; `Time10s` as 46 rows
   every ~8 min).

Two cases fit neither trigger:

- `Time1h`, archived by time with a 1-hour interval, was written within a second of each new
  value, in a statement of 3 rows: the new layer-0 row and the previous hour's row as layers 1 and
  2. The write time drifted from 14:47:11.667 to 13:47:28.314 two days later, about 0.35 s per hour,
  keeping the minute and second of the 13:47:11 project start.
- Rarely changing variables waited far longer than 8 minutes (below).

### How late a value arrives

Measured as the age of the oldest layer-0 row in each statement at the moment PostgreSQL logged it,
from 13:47 on 2026-10-06 to 08:07 on 2026-10-07. For `Step1` and `Step2` anchor rows are excluded,
because an anchor is dated 600 s before the change it precedes.

| Variable | Statements | Median age | Longest |
| --- | --- | --- | --- |
| `Fast1`-`Fast4` (one row per second) | ~1 350 each | 0.8 min | 2.8 min |
| `Band1`-`Band4`, `Time1s` | 670 to 1 110 each | 1.0 to 1.6 min | 2.5 min |
| `Time10s` | 136 | 7.9 min | 8.1 min |
| `Time1m`, `Comb1`, `Comb2` | 130 to 136 each | 7.1 to 7.6 min | 8.7 min |
| `Step1` | 100 | 8.2 min | 22 min |
| `Step2` | 47 | 5.2 min | 115 min |
| `Time1h` | 18 | under 1 s | under 1 s (its start marker waited 60 min) |
| `am_i_seen` (constant) | 1 | | 12 h 22 min |

The constant variable is the extreme case. Its start marker from 13:47:11 waited until 02:09:37 the
next night. In that statement the server also wrote a layer-0 row with `t = 02:09:37.065` and the
unchanged value 1. At 02:09:37 the following night it wrote another such row, together with the
layer-1 and layer-2 copies of the previous night's row, one day late. In the same statement of the
second night `Step2` received a layer-0 row `t = 02:09:37.037` with its unchanged value 1, while the
script's tick that second fell at .554. Over the two nights, then, the constant variable produced
one point per day at 02:09:37, and its coarse copies trailed it by a day. Which variables the
02:09:37 write covers, and whether it follows the clock or the project start, is not established.

No setting controls this buffering: neither the manual, nor the editor's resource strings
(`Libraries/Editor_*.ini`), nor the server options mention a buffer size or a flush period. The
manual's page on the server's "Базы данных" tab shows the write queue («Очередь на запись») only as
a counter, capped at 2 000 000 requests.

### Anchor rows

When a variable archived by change changes more than 600 s after its previous archived row, the
server writes two rows: the previous value dated exactly 600.000 s before the change, then the
change. Example, `Step2` on 2026-10-08:

| `t` | `v` | Row |
| --- | --- | --- |
| 00:43:20.538 | 2 | change |
| 00:43:37.556 | 2 | anchor |
| 00:53:37.556 | 0 | change, 600.000 s after the anchor |

On 2026-10-08, every change of `Step1` (8) and `Step2` (30) that came more than 600 s after the
previous row had its anchor, and no anchor appeared without such a change. The Double variables
never showed one, because they change every few seconds. The customer dump shows the same pattern
one poll tick (100 ms) before each change, which is the interval the earlier reference describes.
What sets 600 s in this build is not established.

## Coarse layers

### What they contain

- **Verbatim copies.** Every layer-1 and layer-2 row matched a layer-0 row in `t`, `v` and `q`. The
  only exceptions were copies whose layer-0 row had already been deleted by retention.
- **Minimum and maximum of a calendar period.** For each variable and each calendar minute
  (layer 1) or calendar hour (layer 2), the layer holds the raw row with the smallest value and the
  raw row with the largest value; one row when they are the same row. For all 17 variables on
  2026-10-08 00:00 to 12:50 the minimum and the maximum were present in 10 339 of 10 340 minutes
  and in 192 of 193 hours. Each miss is a period whose copy had not arrived yet (the late copy of
  the constant variable at 02:09, and the last, still open period).
- **Not first and last.** On 2026-10-06 from 13:48 up to, not including, 14:07 (244
  variable-minutes) the first raw row of
  a minute was present in 97 and the last in 87, exactly when it was also the minimum or the
  maximum.
- **Tie-break.** When the maximum occurs more than once in a period, the layer keeps the latest
  occurrence (111 of 111 hours, 498 of 498 minutes). When the minimum occurs more than once, it
  keeps the earliest (115 of 115 hours, 663 of 663 minutes). Almost all ties in this run came from
  the 0 and 100 clamps of the random walk.
- **Markers** (`q = 16`, `q = 32`) are copied into layers 1 and 2 in addition to the minimum and
  the maximum.
- **Calendar alignment.** Periods are the calendar minute and hour (`date_trunc` boundaries).
- **A restart splits the period.** The hour 13:00-14:00 on 2026-10-06, with restarts at 13:09 and
  13:47, holds a maximum for each of the three run segments (13:06:21 = 15.1, 13:29:55 = 40.9,
  13:59:52 = 85.2 for `Time10s`) plus the markers. The segment minimums were the start markers
  themselves (`v = 0`).

Point spacing that follows: at most 2 points per minute in layer 1 (30 s) and 2 per hour in
layer 2 (30 min).

One exception to the min-and-max rule: `Step1`, a stepped integer, had four minutes on 2026-10-08
with three non-marker layer-1 rows. At 01:35 (raw 3, 1, 2) all three were copied, including
`01:35:37.556 v=2`, which is neither the minimum nor the maximum; at 05:56 (raw 0, 3, 1, 0) both
occurrences of the minimum were copied; at 11:30 and 14:01 (raw 0, x, 0) all three. Seven other
three-row minutes of `Step1` followed the rule, and the hour layer had no exception. The rule for
these cases is not established.

An example, `Time10s` (`id = 11`), 13:52 to 13:54 on 2026-10-06:

| `t` | `v` | Copied to layer 1 |
| --- | --- | --- |
| 13:52:08 | 18.5 | |
| 13:52:18 | 25.2 | yes, maximum of 13:52 |
| 13:52:29 | 23.3 | |
| 13:52:40 | 15.2 | yes, minimum of 13:52 |
| 13:52:50 | 15.9 | |
| 13:53:01 | 19.2 | |
| 13:53:11 | 19.5 | |
| 13:53:22 | 20.8 | yes, maximum of 13:53 |
| 13:53:32 | 20.0 | |
| 13:53:42 | 16.1 | |
| 13:53:53 | 11.8 | yes, minimum of 13:53 |
| 13:54:04 | 13.1 | |
| 13:54:14 | 16.1 | yes, maximum of 13:54 |
| 13:54:24 | 14.0 | |
| 13:54:34 | 7.1 | |
| 13:54:44 | 5.9 | yes, minimum of 13:54 |
| 13:54:55 | 13.4 | |

### When they arrive

A period's coarse rows are not written at the period's end. They ride along with the variable's
next batch after the period has closed. For the hour 23:00-24:00 on 2026-10-06, the layer-2 rows of
the 14 Double variables arrived between 00:00:00.054 and 00:02:48, `Step2`'s at 00:06:40 and
`Time1h`'s at 00:47:12. A variable's coarse rows wait as long as its layer-0 rows do.

### The day layer

**Layer 3 received no row in 48 hours.** That includes 2026-10-07, a full calendar day without a
restart, checked after the second midnight and after the 02:00 maintenance. Start and stop markers
were not copied into layer 3 either.

The customer dump (`SemiPlot.Tests.Unit/Fixtures/real-archive-rows.csv`, build unknown) has
22 layer-3 rows within two hours of one day, markers included. Whether layer 3 is written may
therefore depend on the build or the licence; it cannot be assumed on a plant.

## What tpdefault holds

`tpdefault` is never empty for long on a live archive, and its content is not a fault signal by
itself:

| Content | Where from | Removed |
| --- | --- | --- |
| Junk rows, `t = '1899-12-30 00:00:00.000'`, `v = 0`, `q = 0`, layers 1 and 2 | see below | 02:00 `TRUNCATE`, next start `DROP` |
| Late rows of a day retention already dropped | slow or constant variables | the same |
| Rows dated today or later | a day partition missing at write time | the same, so they are lost |

1899-12-30 is the zero of Delphi's `TDateTime`. The server wrote one such row per variable and
layer in each project run, with the first batch that carried that layer (the first closed minute,
the first closed hour, or the stop flush when the run ended first): three runs of 16 variables gave
48 per layer. The constant variable added one per layer with its first write on 2026-10-07, 49 per
layer in total. While they are in `tpdefault`, an unbounded read of layer 1 or 2 (`min(t)` without a
time predicate) returns 1899-12-30.

Only the third kind means a fault, and the server erases it within a day. A check that wants to
detect a missing partition has to look for rows with `t >= current_date` in `tpdefault`, and has to
look before 02:00.

## Database outages

Method: the container was stopped, killed or paused while the project ran. Holes were looked for in
the variables that write about once per second (a gap over 15 s in `Fast1`-`Fast4` and `Time1s`)
and in `Band1`-`Band4` (a gap over 30 s), then checked against the values on both sides and the
statement log. This method cannot see a lost batch of a slow variable, whose normal gaps are longer.
For those, the largest gaps around every outage were checked by hand and were normal (`Time10s` at
most 13 s, `Time1m` at most 63 s).

| Outage | Insert threads noticed | Project log «Потеряно соединение» | Reconnected | Lost |
| --- | --- | --- | --- | --- |
| 1: `docker stop`, 2 min, 14:33:05 | 14:33:05, :10, :16, :35 | 14:34:25 to 14:35:08, every 2 s | 14:35:08, 1 s after ready | `Band1`, 14:31:30 to 14:33:12 (102 s, about 50 rows) |
| 2: `docker stop`, 30 s, 18:13:15 | 18:13:20, :23, :35, :41 | 18:14:16 | 4 connections within 2 s of ready, the 5th and the log line «База данных подключена» at 18:14:16 | none |
| 3: `docker kill`, 30 s, 18:33:17 | 18:33:36, :38, :42, 18:34:16 | 18:33:42 to 18:33:50 | 4 connections at 18:33:50, the 5th at 18:34:16 right after its thread noticed | `Fast4`, 18:32:50 to 18:33:37 (47 s); `Band4`, 18:32:41 to 18:33:37 (56 s); about 45 rows each |
| 4: `docker pause`, 60 s, 18:53:12 | not noticed | none | not disconnected | none |

What happened in every outage:

1. The server buffered in memory and sent the backlog with the original timestamps once it could.
   Outage 1 sent it in one 695-row statement over 10 variables, then normal batches. After the
   pause (outage 4), the waiting inserts completed and one 310-row statement over 6 variables
   followed at 18:54:13.079.
2. No start DDL ran again, no markers (`q = 16/32`) were written, no `messages` row was written.

The loss:

- Each hole is exactly one batch of one variable, ending at the moment an insert thread hit the
  dead connection (outage 3: `Insert thread 4 - con. lost 1` at 18:33:36.8, the hole ends at
  18:33:37.8). The batch never reached PostgreSQL: no statement, no `ERROR` for it.
- It happened in two of the three outages that broke connections. In outage 2 all four threads hit
  the dead connection and nothing was lost. What decides which case loses a batch is not known.
- Nothing marks the hole: no quality flag, no `messages` row, no error in the project log. On a
  chart it is a straight line between the points around it.

### The server log

The server keeps its own log in `Documents\Simple-Scada 2 (demo64)\Logs\Server-log.txt` (UTF-8,
millisecond timestamps). It carries lines the web interface (`http://localhost:8758/`) does not
show:

```
14:33:05.672 | Insert thread 4 - con. lost 1
14:33:10.702 | Insert thread 2 - con. lost 1
14:33:16.701 | Insert thread 1 - con. lost 1
14:33:35.657 | Insert thread 3 - con. lost 1
14:34:25.179 | test_project | Потеряно соединение с базой данных (execute)
14:35:08.808 | test_project | База данных подключена
```

The server writes through four insert threads.

## The variables table

The editor's database action «создать таблицу переменных» ("create the variables table") issues:

```sql
CREATE TABLE IF NOT EXISTS variables (ID integer NOT NULL DEFAULT 0,Name varchar(512) NULL,
  Description varchar(1024) NULL,CONSTRAINT vpk PRIMARY KEY (ID)) WITH (OIDS = FALSE) TABLESPACE pg_default;
TRUNCATE variables;
INSERT INTO variables (ID,Name,Description) VALUES (2,'Fast1',''),(3,'Fast2',''), ... ,(17,'Step2','');
```

- `variables.ID` is `trends.id`.
- The table is a snapshot. Adding a variable, saving the project and restarting it left the table
  unchanged; only the button rewrites it.
- The manual calls it `variables_data`, the name of the archive system v1 table; v2 creates
  `variables`.

Variable IDs come from the editor. A CSV import with an `ID` column of 1..16 produced IDs 2..17
while a hand-made variable still held ID 1; whether the import ever takes the column as given was
not established.

## Disk and volume

| Measure | Value |
| --- | --- |
| Bytes per row, heap plus primary key | 93 (heap ~60, index ~33) |
| One day partition of this project (16 variables) | ~63 MB, ~670 000 rows |
| Layer-1 rows per layer-0 rows | ~5 % |
| Layer-2 rows | about 2 per variable per hour |
| This project per year | ~23 GB |

Per pen and year, from the rate at which the pen writes layer 0:

| Pen writes | Rows per year | Disk per year |
| --- | --- | --- |
| once per second | ~32 million plus up to 1 million in layer 1 | ~3.1 GB |
| once per 2 s | ~16 million plus layer 1 | ~1.6 GB |
| once per 10 s | ~3.2 million plus layer 1 | ~0.4 GB |
| once per minute or less | ~0.5 million | ~0.05 GB |

WAL (about 1 GB on top) and vacuum headroom are not included. The deadband decides the rate: a
plant sensor with a well-chosen deadband writes far less often than this random walk.

## Manual and earlier reference versus observation

| Topic | Manual, forum or `scada-archive.md` | Observed in this run |
| --- | --- | --- |
| Coarse selection | up to 4 points per period: first, last, min, max (`[FORUM:1032]`, partly inferred) | min and max of the calendar period; first and last only when extreme |
| Tie-break | the later row for a repeated extreme | the later row for a repeated maximum, the earlier row for a repeated minimum |
| Layer spacing | 15 s, 15 min, 6 h | 30 s, 30 min; layer 3 empty |
| Period alignment | open question | calendar minute and hour |
| Coarse flush cadence | v1: minute layer every minute, hour layer every hour (`[FORUM:345]`) | with the variable's next batch after the period closes |
| Day layer | written like the others | not written by 2.7.6.0 demo-64 |
| Anchor row before a change | one poll tick before the change | 600 s before a change that follows more than 600 s of no change |
| Deletion | «скорость удаления не зависит от объёма БД» (`[MAN:archsysv2]`) | `DROP TABLE` of whole day partitions, at 02:00 and after each start |
| "1 day" limit | «старые тренды будут удаляться» (`[MAN:trendsset]`) | only the current day survives 02:00 |
| Outage buffering | up to ~2 million records kept and written later (`[MAN:archsysv2]`) | sent later with original timestamps; one in-flight batch can be lost silently |
| Freshness | rarely changing values reach the database rarely (`[FORUM:1847]`) | 1 to 3 min, ~8 min, up to 2 h, up to a day, by kind of pen |
| Name table | none in v2 | optional `variables`, a snapshot |
| `tpdefault` | non-empty means a missing partition | always holds junk; emptied nightly and at every start |
| Existing `trends` | unverified assumption in SemiBase `provisioning.md` | accepted unchanged |

## Consequences

For SemiPlot:

1. **Live data cannot come from the archive.** The newest point of a pen lags the plant by
   minutes, by hours for a rarely changing pen. A live edge needs a channel that bypasses the
   archive buffer: the server's built-in OPC UA server (manual, section 6.13.6) or a project script
   that writes current values to a table of ours (scripts manual, section 2.10.6).
2. **Layer 3 can be empty.** A window wide enough to select the day layer draws nothing on such a
   build; the hour layer has to stand in.
3. **The layer spacings are half the density assumed.** `AggregationLayer.ToPointSpacing` assumes
   four points per period.
4. **Unbounded reads of layers 1 and 2 meet 1899-12-30.** Every read is bounded by time today; it
   has to stay so.
5. **A pen that does not change has one point per day, and its newest point can be a day old.**
   Holding the last value as a step is what draws it correctly.
6. **A silent hole looks like a straight line.** Nothing in the archive distinguishes it from a
   steady value.

For SemiBase and the plant:

1. The `tpdefault` check has to count rows with `t >= current_date`, not rows at all.
2. PostgreSQL must not be restarted under a running project: a broken connection can cost a batch.
   Restart it with the project stopped, which flushes every buffer and writes markers.
3. The SCADA accepts the SemiBase-created `trends`, and replaces `tpdefault` at every start.

## Open questions

| Question | Why it is open | How to settle it |
| --- | --- | --- |
| Which builds or licences write layer 3? | The customer dump has it, 2.7.6.0 demo-64 does not | `select count(*) from trends where l = 3` on the plant |
| What sets the 600 s anchor offset? | The customer dump shows one poll tick instead | a variable with a different poll rate, or an OPC variable |
| Why do rarely changing variables wait longer than the ~8 min flush? | Not visible from the database side | none without the server code |
| Which variables does the 02:09:37 write cover, and is it tied to the clock or the start? | Seen for the constant variable both nights and for `Step2` once | a run started at a different time of day |
| Which broken-connection case loses the in-flight batch? | 2 of 3 outages that broke connections lost one; the third lost none | more outages, correlated with which thread held which variable |
| What decides the 02:00 maintenance time and the 23:58-02:00 quiet window? | Identical on both nights, no setting found | a run with a different project start time |
| Why does a stepped integer sometimes keep a third row in a minute? | Four minutes of `Step1` on 2026-10-08 | more stepped variables with frequent changes |
| Why does "by time, 1 s" skip 37 % of seconds? | Not the clamp; the strict "more than the interval" reading is unverified | a variable written at a fixed, slower tick |

## Reproduction

1. Build and start the container (the passwords are the bench's fixed dummies):

   ```powershell
   docker build -t semiplot-bench:capture SemiPlot/bench
   docker run -d --name scada-capture --restart unless-stopped -p 55433:5432 -e TZ=Europe/Moscow `
     -e POSTGRES_PASSWORD=semibase-container-superuser -e SEMIBASE_WRITER_PASSWORD=semibase-container-writer `
     -e SEMIBASE_PLOT_PASSWORD=semibase-container-plot -e SEMIPLOT_PROVISIONED_DATABASE=semiplot_provisioned `
     -v scada-capture-data:/var/lib/postgresql/data semiplot-bench:capture <the -c options above>
   ```

2. In the editor: a new project; `Проект -> Настройки -> База данных`: PostgreSQL, archive system
   v2, database `semiplot_provisioned`, host `127.0.0.1`, port `55433`, user `scada_writer`,
   password `semibase-container-writer`.
3. Variables: create one internal variable by hand, export it to CSV (`Documents\Simple-Scada 2
   (demo64)\Import`), add rows by copying its format, and import with «пропустить существующие»
   ("skip existing"). The archive interval column takes the export's own spelling, `1 sec`; the
   editor's display spelling `1 сек.` is rejected («Недопустимая частота записи тренда!»). `10 sec`,
   `1 min` and `1 hour` were accepted.
4. The timer script above: event type «Таймер», interval 1 s, «Выполнить сразу после запуска
   проекта» ("run right after the project starts").
5. Save the project (`Ctrl+S`) and start it from the server interface.
6. Read the statement log from `/var/lib/postgresql/data/log` in the container and the server log
   from `Documents\Simple-Scada 2 (demo64)\Logs\Server-log.txt`.

The selection-rule check, per variable and calendar minute (replace `l = 1` and `minute` with
`l = 2` and `hour` for the hour layer):

```sql
WITH b AS (
    SELECT id, date_trunc('minute', t) AS bucket, min(v) AS vmin, max(v) AS vmax
    FROM trends WHERE l = 0 AND q = 0 AND t >= :from AND t < :to GROUP BY 1, 2
)
SELECT count(*) AS periods,
       sum((EXISTS (SELECT 1 FROM trends m WHERE m.l = 1 AND m.id = b.id
            AND m.t >= b.bucket AND m.t < b.bucket + interval '1 minute' AND m.v = b.vmin))::int) AS min_kept,
       sum((EXISTS (SELECT 1 FROM trends m WHERE m.l = 1 AND m.id = b.id
            AND m.t >= b.bucket AND m.t < b.bucket + interval '1 minute' AND m.v = b.vmax))::int) AS max_kept
FROM b;
```

The tie-break check takes, per period, the first and the last raw row holding the maximum and asks
which of the two the coarse layer carries. Replace `max(v)` with `min(v)` for the minimum:

```sql
WITH b AS (SELECT id, date_trunc('hour', t) AS bucket, max(v) AS vext
           FROM trends WHERE l = 0 AND q = 0 AND t >= :from AND t < :to GROUP BY 1, 2),
r AS (SELECT b.id, min(x.t) AS first_t, max(x.t) AS last_t
      FROM b JOIN trends x ON x.l = 0 AND x.q = 0 AND x.id = b.id AND x.t >= b.bucket
           AND x.t < b.bucket + interval '1 hour' AND x.v = b.vext
      GROUP BY b.id, b.bucket HAVING count(*) > 1)
SELECT count(*),
       sum((EXISTS (SELECT 1 FROM trends m WHERE m.l = 2 AND m.id = r.id AND m.t = r.first_t))::int) AS earliest,
       sum((EXISTS (SELECT 1 FROM trends m WHERE m.l = 2 AND m.id = r.id AND m.t = r.last_t))::int) AS latest
FROM r;
```

The anchor check, per variable, counts changes that follow exactly 600 s after the previous row and
rows that repeat the previous value:

```sql
WITH s AS (
    SELECT id, t, v, lag(t) OVER w AS pt, lag(v) OVER w AS pv
    FROM trends WHERE l = 0 AND id IN (16, 17) AND t >= :from
    WINDOW w AS (PARTITION BY id ORDER BY t)
)
SELECT id,
       count(*) FILTER (WHERE extract(epoch FROM t - pt) = 600) AS after_600_s,
       count(*) FILTER (WHERE v = pv) AS repeats
FROM s GROUP BY id;
```

## The SQL templates in Editor.exe

`Server.exe` is packed (one encrypted section, the original Delphi sections empty) and yields no
strings. `Editor.exe` is a plain Delphi image whose string table carries the DDL templates for all
three supported database engines. The PostgreSQL ones, in the order they appear:

```
DROP TABLE IF EXISTS
CREATE TABLE IF NOT EXISTS ... PARTITION OF ... FOR VALUES FROM ( ... ) TO ( ... )
... pdefault
CREATE TABLE IF NOT EXISTS ... PARTITION OF ... DEFAULT;
CREATE DATABASE "..." WITH ENCODING = 'UTF8' TABLESPACE = pg_default CONNECTION LIMIT = -1;
CREATE TABLE IF NOT EXISTS ... integer NOT NULL DEFAULT 0, ... smallint NOT NULL DEFAULT 0,
  ... timestamp(3) without time zone NOT NULL, ... double precision, ... integer NOT NULL,
  CONSTRAINT tpk PRIMARY KEY ( ... ) PARTITION BY RANGE ( ... )
... varchar(255), ... integer NOT NULL DEFAULT '-1'::integer, ... timestamp(3) without time zone,
  CONSTRAINT mpk PRIMARY KEY ( ... ))
ID integer NOT NULL DEFAULT 0, Name varchar(512) NULL, Description varchar(1024) NULL,
  CONSTRAINT vpk PRIMARY KEY (ID)) WITH (OIDS = FALSE) TABLESPACE pg_default;
```

The string `Invalid DB_PARTITIONS_TYPE!` shows an internal partitioning switch; no editor setting
exposes it, and PostgreSQL partitions were daily throughout the run.
