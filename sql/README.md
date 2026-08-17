# `sql/`

Data files, not code. `semiplot_dev.sql` is the archive schema the bench writes into; the seeder
(`SemiPlot.Tools.ArchiveSeeder`) carries it as an embedded resource and applies it as `scada_writer`,
the way Simple-Scada 2 creates its own tables on a site.

Everything SemiPlot adds of its own — the `scada_writer` and `semiplot_reader` roles, the grants, the
default-privileges chain and `semiplot_tags` — is created by `semibase create`
(`github.com/Semiteq/SemiBase`), never here. A second definition in this repository would be the one
exercised daily while the real one decayed.

## Where `semiplot_dev.sql` came from

The customer archive dump is kept outside the repository — a PostgreSQL custom-format dump
(`pg_dump -Fc`), not readable as text. Its path is a local detail and is not recorded here;
substitute it for `<path-to-dump>` below.

`pg_restore` is not on `PATH` on a default PostgreSQL install for Windows; call it by its full path,
for example `C:/Program Files/PostgreSQL/14/bin/pg_restore.exe`. Version 14 rejects the command
without `-d` or `-f`, so the output file is named explicitly rather than piped:

```sh
pg_restore --schema-only -f schema-only.sql <path-to-dump>
```

`schema-only.sql` is a scratch file and is not committed. `semiplot_dev.sql` was written from its
`public.trends` section with these edits, all of them removals:

| Removed | Reason |
| --- | --- |
| `ALTER TABLE ... OWNER TO postgres` | the owner is `scada_writer` here, set by who runs the script |
| `SET` preamble, `SELECT pg_catalog.set_config`, `\restrict` / `\unrestrict` | `pg_dump` scaffolding, and the backslash forms are `psql` commands that `Npgsql` cannot execute |
| `public.messages`, its partitions, `mpk` | no slice of the PostgreSQL data source reads messages |
| `public.realtest_withtimer` | a table from the customer's own testing, not part of the archive |
| the dated `tp2026m08dNN` partitions and their `ATTACH` statements | day partitions belong to the run being seeded, and the seeder creates them |
| the per-partition `_pkey` constraints and `ALTER INDEX tpk ATTACH PARTITION` | `pg_dump` renders inherited indexes explicitly; `CREATE TABLE ... PARTITION OF` inherits `tpk` on its own |

The `trends` definition itself is unchanged, column for column, down to `timestamp(3) without time
zone` and the `smallint` layer. It matches the DDL in `docs/architecture/scada-archive.md`, section
*Database objects*.

## Where `SemiPlot.Tests.Data/Fixtures/real-archive-rows.csv` came from

That file is 140 rows of the customer archive, anonymised, committed so the thinning rule can be
confronted with real vendor output by tests that need no database. The raw extract is never
committed.

Restoring the dump needs a server. A throwaway container is the least invasive one, and the
`pg_restore` of a local PostgreSQL install reaches it over TCP:

```sh
docker run -d --name dumpscratch -e POSTGRES_PASSWORD=<scratch> -p 55433:5432 postgres:17-alpine
docker exec dumpscratch psql -U postgres -c "CREATE DATABASE dumpscratch;"
pg_restore --no-owner --no-privileges -h 127.0.0.1 -p 55433 -U postgres -d dumpscratch <path-to-dump>
```

The extraction and the anonymisation are one query. It takes the eight minutes
`13:48:00`–`13:56:00` of the dump's continuous middle — far enough from the newest edge that the
coarse layers are flushed — for both archived variables and all four layers:

```sh
docker exec dumpscratch psql -U postgres -d dumpscratch -t -A -F',' -c "
SELECT 9001 + id, l,
       to_char(t - interval '9713 days', 'YYYY-MM-DD\"T\"HH24:MI:SS.MS'),
       v, q
FROM public.trends
WHERE t >= timestamp '2026-08-05 13:48:00' AND t < timestamp '2026-08-05 13:56:00'
ORDER BY id, l, t;"
docker rm -f dumpscratch
```

The header line `id,l,t,v,q` is prepended by hand.

| Anonymisation | Rule |
| --- | --- |
| identifier | the archive's `0` and `1` become the synthetic `9001` and `9002` |
| timestamp | shifted back by exactly **9713 days**, which maps `2026-08-05` onto the epoch-relative base `2000-01-01` |
| value, quality | kept exactly as the vendor wrote them |

The offset is a whole number of days, so the calendar minute, hour and day a row falls in are the
ones it had — which is what the fixture's period grouping depends on. Intervals between rows are
untouched, so the poll interval and the anchor pairs survive. What the fixture tests is which rows
survive each period and how far apart they sit, and neither is changed by the shift.
