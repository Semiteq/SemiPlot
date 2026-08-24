# The PostgreSQL instance SemiPlot reads

SemiPlot neither installs, configures nor provisions the database server. SemiBase
(`github.com/Semiteq/SemiBase`) owns the instance: the engine, the configuration deltas, the archive
database, both roles, the grants, the default-privileges chain and the `semiplot_tags` DDL. This
document records only what constrains SemiPlot as a consumer of that instance.

| Question | Where it is answered |
| --- | --- |
| Which engine, which version, how it is installed | `Semiteq/SemiBase`: `docs/architecture/overview.md` |
| Every configuration setting that differs from the PostgreSQL default | `Semiteq/SemiBase`: `docs/architecture/configuration.md` |
| The provisioning order, and what `semibase site` and `semibase bench` each do | `Semiteq/SemiBase`: `docs/architecture/overview.md` and `docs/architecture/provisioning.md` |
| The role definitions, the grants, the default-privileges chain and the `trends` DDL | `Semiteq/SemiBase`: `docs/architecture/provisioning.md` |
| The `semiplot_tags` DDL | `Semiteq/SemiBase`: `sql/semiplot_tags.sql` |
| The archive schema itself | `scada-archive.md` |
| The queries SemiPlot issues | `data-integration.md` |

None of that is restated here. A second copy of a provisioning order is the copy that gets read on
commissioning day and the copy that has drifted.

## What SemiPlot may assume about the server

- Vanilla PostgreSQL with the major version pinned. Production and the test bench both run 17;
  **14 is the declared floor**, which is the constraint on the SQL SemiPlot may write. It is a
  deliberate margin rather than a requirement any shipped statement makes: the features the bench
  executes bottom out at 13 (`DROP DATABASE ... WITH (FORCE)`), and no statement in
  `ArchiveStatements.cs` needs more. The bucketing query `data-integration.md` quotes would read
  `date_bin`, which arrives in 14, if the slice that ships it is ever revived.
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

Both timeouts are set on the role by `semibase` as session defaults, so they apply to every
session SemiPlot opens. They are defaults, not enforcement: PostgreSQL classes `statement_timeout` as
`USERSET`, and a startup option or a plain `SET` overrides a role default from the client side.
SemiPlot's contract is that it never sends `statement_timeout` in any form, so the value the reader
role carries is the value every SemiPlot session runs under; the client reads the effective value
only after a read has failed, from a fresh session of the same role, to report which bound that read
hit. The number is stable while the role default is: role and database defaults bind at backend
start and a pooled physical connection keeps its startup value, so an administrative change to the
default mid-run can leave one report one increment stale.

The reader credential is what makes the plaintext password in SemiPlot's configuration file an
acceptable risk: it grants reading process history and nothing more.

## `semiplot_tags`

The archive has no mapping from a variable number to a name, so we supply one `[DEC:semiplot-tags]`.
The table is created by `semibase` and filled by hand during commissioning. SemiPlot never
writes to it.

| Column | Read by SemiPlot | Use |
| --- | --- | --- |
| `id` | yes | Joins the pen to `trends.id` |
| `name` | yes | Pen label |
| `group_name` | yes | Pen grouping and catalogue ordering |
| `color` | yes | Pen colour |
| `line_style` | yes | Mapped onto the domain line-style enum |
| `unit` | no | Present in the table; no query reads it yet |

The catalogue query is in `data-integration.md`. An absent table and an empty one are both normal
states with their own message, and neither is ever a crash — but they travel in different channels.
An empty table is a successful read of zero rows, because the database answered correctly and
nothing is broken. An absent table is a typed failure carrying the table name, because provisioning
has not finished. Keeping the two apart is what lets the operator be sent to the
provisioner in one case and to commissioning in the other.

If several client versions ever have to coexist against one database, a `semiplot_meta` table
carrying a schema version is the intended mechanism. It is not needed while a single client version
is deployed.

## Four states SemiPlot must survive

Provisioning is a sequence and the client can be started at any point in it. Each state below is
normal, carries its own message, and is never a crash:

1. no database — nothing answers at the configured address;
2. database without `trends` — provisioning stopped part-way, or the table was removed after it;
3. `trends` without `semiplot_tags` — provisioning is not finished;
4. `semiplot_tags` present but empty — commissioning is not finished.

The behaviour for each is specified in `data-integration.md`.

**The second state has moved and the code has not.** SemiBase creates `public.trends` in both
`semibase site` and `semibase bench`, so on a commissioned site the archive table arrives with the
database and a missing `trends` is no longer *the SCADA has not run yet*. SemiPlot's own mapping
still says it is — `StartupFailureMapper`, `MissingRelationProbe` and `ArchiveNotInitialisedError`
carry the older model, and the slice `missing-relation-probe-removal` is what corrects them. Read
the list above as the states the client distinguishes, not as what a site's provisioning leaves
behind.

One operational state belongs beside them: a non-empty `tpdefault` means a daily partition was
missing at write time. The partition itself arrives with the provisioning and is empty by
construction, so anything in it is a row the SCADA wrote with no daily partition to take it. It is
read straight from the database at commissioning, and SemiPlot treats it as the fault signal
`scada-archive.md` describes.

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
