# The PostgreSQL instance SemiPlot reads

SemiPlot neither installs, configures nor provisions the database server. SemiBase
(`github.com/Semiteq/SemiBase`) owns the instance: the engine, the configuration deltas, the archive
database, both roles, the grants, the default-privileges chain and the configuration-schema DDL. This
document records only what constrains SemiPlot as a consumer of that instance.

| Question | Where it is answered |
| --- | --- |
| Which engine, which version, how it is installed | `Semiteq/SemiBase`: `docs/architecture/overview.md` |
| Every configuration setting that differs from the PostgreSQL default | `Semiteq/SemiBase`: `docs/architecture/configuration.md` |
| The provisioning order, and what `semibase site` and `semibase bench` each do | `Semiteq/SemiBase`: `docs/architecture/overview.md` and `docs/architecture/provisioning.md` |
| The role definitions, the grants, the default-privileges chain and the `trends` DDL | `Semiteq/SemiBase`: `docs/architecture/provisioning.md` |
| The configuration-schema DDL and the pen registration function | `Semiteq/SemiBase`: `sql/semiplot_tags.sql`, `sql/semiplot_register.sql`, `sql/semiplot_groups.sql`, `sql/semiplot_meta.sql` |
| The archive schema itself | `scada-archive.md` |
| The queries SemiPlot issues | `data-integration.md` |

None of that is restated here. A second copy of a provisioning order is the copy that gets read on
commissioning day and the copy that has drifted.

## What SemiPlot may assume about the server

- Vanilla PostgreSQL with the major version pinned. Production and the test bench both run 17;
  **14 is the declared floor**, which is the constraint on the SQL SemiPlot may write, and a shipped
  statement now needs it: `BucketedRawWindow` groups by `date_bin`, added in 14
  (`data-integration.md`, History, Raw). The bench's own SQL bottoms out at 13
  (`DROP DATABASE ... WITH (FORCE)`); the client's floor is 14.
- Reachable on the loopback interface plus the operator network only.
- The archive database holds `trends`, which the SCADA writes and SemiBase creates, the four
  configuration tables `semiplot_tags`, `semiplot_groups`, `semiplot_pen_groups` and `semiplot_meta`,
  which are SemiBase's outright, and `messages`, which is the SCADA's outright. Nothing of ours runs
  inside the database: no summary tables, triggers, functions, scheduled jobs or extensions
  `[DEC:vendor-layers]`. The reasoning is in `history-read-path-evaluation.md`.

## The `semiplot` role

SemiPlot connects as `semiplot` and as nothing else. One login covers both directions: it reads the
archive and it reads and writes the configuration tables.

| Property | Value | What it means for the client |
| --- | --- | --- |
| Privileges | `SELECT` on `trends` and `messages`; `SELECT` and a column-level `UPDATE` of the eight settings columns on `semiplot_tags`, never `INSERT` or `DELETE`; `SELECT, INSERT, UPDATE, DELETE` on `semiplot_groups` and `semiplot_pen_groups`; `SELECT` on `semiplot_meta`; `EXECUTE` on `semiplot_register_new_pens()`. Nothing else | Any write to the archive, and any `ALTER` or `CREATE`, is a defect; the server answers `42501` |
| `statement_timeout` | 30 s | A read that exceeds it fails with SQLSTATE `57014`. That is a bug in layer selection, not a slow disk — surface it as a typed error instead of retrying |
| `idle_in_transaction_session_timeout` | 60 s | A transaction held open is killed rather than blocking vacuum on the partitions |

Both timeouts are set on the role by `semibase` as session defaults, so they apply to every
session SemiPlot opens. They are defaults, not enforcement: PostgreSQL classes `statement_timeout` as
`USERSET`, and a startup option or a plain `SET` overrides a role default from the client side.
SemiPlot's contract is that it never sends `statement_timeout` in any form, so the value the role
carries is the value every SemiPlot session runs under; the client reads the effective value
only after a read has failed, from a fresh session of the same role, to report which bound that read
hit. The number is stable while the role default is: role and database defaults bind at backend
start and a pooled physical connection keeps its startup value, so an administrative change to the
default mid-run can leave one report one increment stale.

The credential is what makes the plaintext password in SemiPlot's configuration file an acceptable
risk: it grants reading process history and editing the pen catalogue, and no write to the archive.

## The configuration tables

The archive has no mapping from a variable number to a name, so we supply one `[DEC:semiplot-tags]`.
`semibase` creates the tables and commissioning fills them. The viewer as built only reads them; the
pen editor that writes them is `Semiteq/SemiPlot#67` and does not exist yet.

`semiplot_tags` is one row per pen, and the catalogue read projects every column of it:

| Column | Read by SemiPlot | Use |
| --- | --- | --- |
| `id` | yes | Joins the pen to `trends.id` |
| `name` | yes | Pen label, and the catalogue ordering |
| `unit` | yes | Drawn beside the value in the sidebar row |
| `format` | yes | The .NET numeric mask the value renders through; no server-side check can parse one |
| `color` | yes | Pen colour; `NULL` draws in the one fallback colour |
| `line_style` | yes | Mapped onto the domain line-style enum |
| `enabled_on_start` | yes | Whether the pen is drawn when the viewer opens |
| `scale_min`, `scale_max` | yes | The pen's own Y range, set together or not at all; absent means autoscale |

Group membership is many-to-many. `semiplot_groups` holds one row per group name,
`semiplot_pen_groups` one row per membership, and a pen may sit in several groups or in none. The
catalogue read joins all three tables (`data-integration.md`).

An absent table and an empty catalogue are both normal states with their own message, and neither is
ever a crash — but they travel in different channels. An empty catalogue is a successful read of zero
rows, because the database answered correctly and nothing is broken. An absent table is a typed
failure carrying the relations the statement reads, because provisioning has not finished. Keeping
the two apart is what lets the operator be sent to the provisioner in one case and to commissioning
in the other.

`semiplot_meta` carries the schema version and SemiBase maintains it. SemiPlot reads it nowhere: a
version gate earns its cost once a delivered installation can be older than the viewer, and while one
client version is deployed it gates nothing.

## Three states SemiPlot must survive

Provisioning is a sequence and the client can be started at any point in it. Each state below is
normal, carries its own message, and is never a crash:

1. no database — the server answers, but holds no database of that name (`3D000`);
2. database without the archive tables — provisioning stopped part-way, or a table was removed
   after it;
3. `semiplot_tags` present but empty — no key has been registered as a pen yet.

The behaviour for each is specified in `data-integration.md`.

SemiBase creates `public.trends` and the configuration tables in one run, in both `semibase site` and
`semibase bench`, so they all arrive with the database. Any one of them absent is the same state —
provisioning did not complete — and one command restores the set, which is why the second state
covers the set rather than ordering it.

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
published interface. SemiPlot runs no shape probe: a read that fails on a missing column is mapped
to `ShapeUnexpected` (`ArchiveExceptionMapper`) and reported rather than producing wrong charts.
That check belongs to the reader, not to the provisioning tool. A supported version range is recorded here once a second SCADA
version has been observed.
