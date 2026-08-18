# The PostgreSQL instance SemiPlot reads

SemiPlot neither installs, configures nor provisions the database server. SemiBase
(`github.com/Semiteq/SemiBase`) owns the instance: the engine, the configuration deltas, the archive
database, both roles, the grants, the default-privileges chain and the `semiplot_tags` DDL. This
document records only what constrains SemiPlot as a consumer of that instance.

| Question | Where it is answered |
| --- | --- |
| Which engine, which version, how it is installed | `Semiteq/SemiBase`: `docs/architecture/overview.md` |
| Every configuration setting that differs from the PostgreSQL default | `Semiteq/SemiBase`: `docs/architecture/configuration.md` |
| The provisioning order, and what `semibase config` / `create` / `verify` each do | `Semiteq/SemiBase`: `docs/architecture/overview.md` and `docs/architecture/provisioning.md` |
| The role definitions, the grants and the default-privileges chain | `Semiteq/SemiBase`: `docs/architecture/provisioning.md` |
| The `semiplot_tags` DDL | `Semiteq/SemiBase`: `sql/semiplot_tags.sql` |
| The archive schema itself | `scada-archive.md` |
| The queries SemiPlot issues | `data-integration.md` |

None of that is restated here. A second copy of a provisioning order is the copy that gets read on
commissioning day and the copy that has drifted.

## What SemiPlot may assume about the server

- Vanilla PostgreSQL with the major version pinned. Production and the test bench both run 17;
  **14 is the declared floor**, which is the constraint on the SQL SemiPlot may write — the
  bucketing query uses `date_bin`, added in 14.
- Reachable on the loopback interface plus the operator network only.
- The archive database holds the SCADA's `trends` and `messages` plus the one object we add,
  `semiplot_tags`. Nothing of ours runs inside the database: no summary tables, triggers, functions,
  scheduled jobs or extensions `[DEC:vendor-layers]`. The reasoning is in
  `history-read-path-evaluation.md`.

## The reader role

SemiPlot connects as `semiplot_reader` and as nothing else.

| Property | Value | What it means for the client |
| --- | --- | --- |
| Privileges | `SELECT` on `trends`, `messages`, `semiplot_tags`. Nothing else | Any write, `ALTER` or `CREATE` issued by SemiPlot is a defect; the server answers `42501` |
| `statement_timeout` | 30 s | A read that exceeds it fails with SQLSTATE `57014`. That is a bug in layer selection, not a slow disk — surface it as a typed error instead of retrying |
| `idle_in_transaction_session_timeout` | 60 s | A transaction held open is killed rather than blocking vacuum on the partitions |

Both timeouts are set on the role by `semibase create` as session defaults, so they apply to every
session SemiPlot opens. They are defaults, not enforcement: PostgreSQL classes `statement_timeout` as
`USERSET`, and a startup option or a plain `SET` overrides a role default from the client side.
SemiPlot's contract is that it never sends `statement_timeout` in any form, so the value the reader
role carries is the value every SemiPlot session runs under; the client reads the effective value
back to report which bound a failed read hit.

The reader credential is what makes the plaintext password in SemiPlot's configuration file an
acceptable risk: it grants reading process history and nothing more.

## `semiplot_tags`

The archive has no mapping from a variable number to a name, so we supply one `[DEC:semiplot-tags]`.
The table is created by `semibase create` and filled by hand during commissioning. SemiPlot never
writes to it.

| Column | Read by SemiPlot | Use |
| --- | --- | --- |
| `id` | yes | Joins the pen to `trends.id` |
| `name` | yes | Pen label |
| `group_name` | yes | Pen grouping and catalogue ordering |
| `color` | yes | Pen colour |
| `line_style` | yes | Mapped onto the domain line-style enum |
| `unit` | no | Present in the table; no query reads it yet |

The catalogue query is in `data-integration.md`. An absent or empty table is a normal state with its
own message, not a failure.

If several client versions ever have to coexist against one database, a `semiplot_meta` table
carrying a schema version is the intended mechanism. It is not needed while a single client version
is deployed.

## Four states SemiPlot must survive

Provisioning is a sequence and the client can be started at any point in it. Each state below is
normal, carries its own message, and is never a crash:

1. no database — nothing answers at the configured address;
2. database without `trends` — the SCADA has not run yet;
3. `trends` without `semiplot_tags` — provisioning is not finished;
4. `semiplot_tags` present but empty — commissioning is not finished.

The behaviour for each is specified in `data-integration.md`.

One operational state belongs beside them: a non-empty `tpdefault` means a daily partition was
missing at write time. `semibase verify` reports it during commissioning, and SemiPlot treats it as
the fault signal `scada-archive.md` describes.

## Retention and capacity

Retention depth is one number applying to all archived data `[DEC:common-retention]`. The setting
lives in the SCADA project, not in the database — «Ограничение архива трендов» `[MAN:trendsset]`.
Coarse layers cannot be kept longer than raw data, so the span SemiPlot can chart is bounded by that
one number.

Sizing follows from the write rate rather than from the number of variables. A row occupies roughly
90 bytes all-in — about 56–60 bytes of heap tuple plus about 30 bytes in the primary key. Two
multipliers apply:

- change-based archiving writes two rows per change `[MEAS:dump-20260805]`;
- the coarse layers add at most about 5860 rows per variable per day — 1440 minutes, 24 hours and
  one day at four points each — and cannot be disabled. A variable that changes rarely produces
  fewer.

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

## Backup

An instance decision, so it belongs to whoever administers the instance rather than to the client.
`UNDECIDED` and recorded nowhere else yet: method and schedule. Two properties shape the choice —
the database is large and almost entirely append-only, and losing recent process history is worse
than losing old history. A physical base backup plus write-ahead log archiving fits that shape far
better than a nightly `pg_dump` of a year-sized archive, and whatever is chosen must be paired with
a rehearsed restore, because an untested backup is not a backup.

## Schema drift

Simple-Scada upgrades can change the archive schema without notice — it is a vendor internal, not a
published interface. SemiPlot probes the shape of `trends` at startup against `information_schema`
and reports a clear incompatibility rather than producing wrong charts. That probe belongs to the
reader, not to the provisioning tool. A supported version range is recorded here once a second SCADA
version has been observed.
