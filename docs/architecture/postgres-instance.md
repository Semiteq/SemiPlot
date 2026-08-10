# The PostgreSQL instance we supply

What we install, what we change in it and why, what we add to it, and who is responsible for what
afterwards. The archive schema itself belongs to the SCADA and is described in `scada-archive.md`;
the queries SemiPlot issues are in `data-integration.md`.

## Ownership

We supply and administer the database server. Simple-Scada is a client of it: the SCADA creates and
writes its own tables inside a database we provisioned, and it deletes its own old partitions
according to its own retention setting.

| Area | Responsible |
| --- | --- |
| Installing PostgreSQL, its configuration, service account, port | us |
| Creating the archive database and the roles | us |
| Creating `trends` / `messages`, their partitions, and writing them | the SCADA |
| Executing retention on those tables | the SCADA |
| Choosing the retention depth | us `[DEC:common-retention]` |
| `semiplot_*` objects | us |
| Backup and restore of the instance | us |
| Upgrading the instance | us |
| Disk capacity planning | us |

## Installation

- PostgreSQL installed through `winget`, with the **major version pinned**. Automatic upgrade is
  disabled: a major-version jump requires a data migration and must never happen unattended.
- Minimum major version **14**, because the bucketing query uses `date_bin`.
- Runs as a Windows service under a dedicated account, listening on the loopback interface plus the
  operator network only.
- The instance configuration and provisioning scripts live in the repository, so that a rebuilt
  machine reproduces the same server. `UNDECIDED`: exact directory layout under `deploy/`.

## Configuration deltas

Only settings we change from the PostgreSQL default are listed. The workload is a sustained
append-only insert stream from one writer plus a small number of read-heavy analytical queries.

| Setting | Default | Ours | Why |
| --- | --- | --- | --- |
| `shared_buffers` | 128 MB | 25% of RAM | The archive working set is far larger than the default cache; this is the standard starting point for a dedicated server. |
| `effective_cache_size` | 4 GB | 50–75% of RAM | Planner hint only. Too low a value pushes the planner away from index scans, which are exactly what our queries depend on. |
| `work_mem` | 4 MB | 64 MB | The pixel-bucket query groups and sorts. At the default it spills to disk on wide windows. |
| `maintenance_work_mem` | 64 MB | 512 MB | Index builds and vacuum on daily partitions. |
| `max_wal_size` | 1 GB | 8 GB | Under a sustained insert stream the default forces frequent checkpoints, each a write burst that stalls queries. |
| `checkpoint_timeout` | 5 min | 30 min | Same reason: fewer, larger, smoother checkpoints. |
| `checkpoint_completion_target` | 0.9 | 0.9 | Already the default in 14+; stated because it matters and must not be lowered. |
| `wal_compression` | off | on | Cuts write-ahead log volume on a partitioned insert workload for a small CPU cost. |
| `random_page_cost` | 4.0 | 1.1 | The default assumes a spinning disk and discourages index scans. The machine has a solid-state drive. |
| `log_min_duration_statement` | off | 1000 ms | A slow query must leave a trace; this is the only diagnostic that survives an operator restarting the client. |
| `track_io_timing` | off | on | Makes `EXPLAIN (ANALYZE, BUFFERS)` usable when a query is slow in the field. |

Set on the reading role rather than globally, so that a badly framed chart query can never stall the
SCADA's own writes:

| Setting | Value | Why |
| --- | --- | --- |
| `statement_timeout` | 30 s | A read that exceeds this is a bug in layer selection, not a slow disk. Fail it and report. |
| `idle_in_transaction_session_timeout` | 60 s | A stuck client must not hold back vacuum on the partitions. |

Left at defaults deliberately: `max_connections` (a handful of clients), autovacuum (PostgreSQL 13+
already triggers vacuum on insert-only tables, which is what sets the visibility map on closed
partitions), and `fillfactor` (the archive is append-only, never updated).

## Databases and roles

| Role | Used by | Privileges |
| --- | --- | --- |
| `scada_writer` | the Simple-Scada project | `ALTER`, `CREATE`, `DROP`, `INSERT`, `SELECT`, `UPDATE` on the archive database. `DROP` is required for retention and may be withheld only if both archive limits are set to unlimited `[MAN:db-access-rights]`. |
| `semiplot_reader` | the SemiPlot desktop client | `SELECT` on `trends`, `messages`, `semiplot_tags`. Nothing else. |
| `semiplot_admin` | commissioning only | Owner of the `semiplot_*` objects. Not used at runtime. |

The client connects as `semiplot_reader`, which is what makes the plaintext password in its
configuration file an acceptable risk: the credential grants reading process history and nothing
more.

## What we add to the database

The archive has no mapping from a variable number to a name, so we supply one. Populated by hand
during commissioning `[DEC:semiplot-tags]`.

```sql
CREATE TABLE semiplot_tags (
    id         integer PRIMARY KEY,   -- matches trends.id
    name       text    NOT NULL,
    group_name text,
    unit       text,
    color      text,
    line_style smallint NOT NULL DEFAULT 0
);
```

Nothing else is added. Specifically, and deliberately: no summary tables, no triggers, no functions,
no scheduled jobs, no extensions. The reasoning is recorded in `history-read-path-evaluation.md`;
the short version is that the SCADA's own archive layers already provide the coarse resolutions, so
maintaining our own would duplicate them and add a process that can silently stop.

If several client versions ever have to coexist against one database, a `semiplot_meta` table
carrying a schema version is the intended mechanism. It is not needed while a single client version
is deployed.

## Retention and capacity

Retention depth is one number applying to all archived data `[DEC:common-retention]`. The setting
lives in the SCADA project, not in the database — it is «Ограничение архива трендов»
`[MAN:trendsset]`. There is no way to keep coarse layers longer than raw data.

Sizing follows from the write rate rather than from the number of variables. A row occupies roughly
90 bytes all-in — about 56–60 bytes of heap tuple plus about 30 bytes in the primary key. Two
multipliers apply:

- change-based archiving writes two rows per change `[MEAS:dump-20260805]`;
- the coarse layers add up to about 1465 rows per variable per day and cannot be disabled.

`UNDECIDED`: the retention depth in days and the resulting disk size, both of which need a measured
write rate from a working installation. The measurement is one query:

```sql
SELECT count(*) / 86400.0 AS rows_per_second
FROM trends
WHERE l = 0
  AND t >= date_trunc('day', now()) - interval '1 day'
  AND t <  date_trunc('day', now());
```

Run per variable as well, grouped by `id`, to find which few variables dominate the stream. Reducing
their archiving interval or widening their deadband is a cheaper lever than any storage decision.

## Provisioning order

The order matters, because the archive tables do not exist until the SCADA has run once.

1. Install PostgreSQL, apply the configuration, restart the service.
2. Create the archive database and the `scada_writer` role.
3. Point the Simple-Scada project at the database and start it once. It creates `trends`,
   `messages` and the first daily partitions.
4. Create `semiplot_tags` and the `semiplot_reader` role.
5. Fill `semiplot_tags` with the variables to be trended.
6. Write the SemiPlot connection file, including the source time zone.

SemiPlot must survive every intermediate state of this sequence: no database, database without
`trends`, `trends` without `semiplot_tags`, and `semiplot_tags` without data. Each is a normal
condition with its own message, not a crash. The behaviour is specified in `data-integration.md`.

## Backup

Ours by default, since we supply the instance. Two properties make the choice unusual: the database
is large and almost entirely append-only, and losing recent process history is worse than losing old
history.

`UNDECIDED`: method and schedule. `pg_dump` of a year-sized archive is impractical as a nightly job;
a physical base backup plus write-ahead log archiving fits the shape of the data far better. Whatever
is chosen must be paired with a rehearsed restore, because an untested backup is not a backup.

## Upgrades

- **PostgreSQL minor versions** apply in place with a service restart and are safe.
- **PostgreSQL major versions** require `pg_upgrade` and a planned outage. Never automatic.
- **Simple-Scada upgrades** can change the archive schema without notice — it is a vendor internal,
  not a published interface. SemiPlot probes the shape of `trends` at startup against
  `information_schema` and reports a clear incompatibility rather than producing wrong charts. A
  supported version range is recorded once a second SCADA version has been observed.
