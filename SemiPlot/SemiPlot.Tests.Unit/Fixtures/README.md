# `Fixtures/`

Provenance for `real-archive-rows.csv`, so a later reader can repeat the extraction. The file is
data, versioned by git, and the tests over it need no database.

The rows are the vendor's own output, anonymised, and they are the only vendor rows this repository
holds: `RealArchiveFixtureTests` confronts the thinning hypothesis with them, and
`RealArchiveGapTests` reads gaps out of them. Nothing here is generated and nothing here is a
schema — who owns `public.trends` is in `docs/architecture/bench.md`.

## The customer dump

The customer archive dump is kept outside the repository — a PostgreSQL custom-format dump
(`pg_dump -Fc`), not readable as text. Its path is a local detail and is not recorded here;
substitute it for `<path-to-dump>` below.

`pg_restore` is not on `PATH` on a default PostgreSQL install for Windows; call it by its full path,
for example `C:/Program Files/PostgreSQL/14/bin/pg_restore.exe`.

## Where `real-archive-rows.csv` came from

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
