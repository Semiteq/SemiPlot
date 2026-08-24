<!--
Provenance: decision record. Frozen once written — it captures the reasoning at the time of the
decision, not the current design. Current design lives in data-integration.md and postgres-instance.md.
Decision: read the SCADA's own archive layers; build no summary tables, no aggregator service,
no scheduler and no extensions of our own.
-->

# Where the history read path reduces data

## The question

A trend canvas shows about one thousand pixel columns. The archive can hold hundreds of millions of
rows. Something has to reduce one to the other. The choice is where that reduction happens and who
maintains the reduced form.

## Constraints at the time of the decision

- Simple-Scada owns `trends`; we may not modify it, index it, or attach triggers to it.
- The SCADA additionally writes three thinned layers automatically, and they cannot be disabled.
- One retention depth applies to everything `[DEC:common-retention]`, so a summary can never outlive
  the raw data it came from.
- The PostgreSQL instance is ours and fully configurable. It runs on Windows.
- The client is a desktop application connecting directly to the database. There is no application
  server, and introducing one is a significant change.
- The sustained write rate was unmeasured. The stated ceiling was one sample per 100 ms across up to
  a hundred variables, which is a ceiling and not an expectation.

## Options considered

**A. Our own summary tables maintained by a background service.** Tiers at one second, ten seconds,
one minute, one hour and one day, each holding minimum, maximum, first and last per bucket, refreshed
every thirty seconds by a Windows service.

**B. TimescaleDB.** Hypertables with continuous aggregates and compression.

**C. Lazy summaries.** The same tables as A, but filled on demand: the first request for a period
computes and stores it, later requests read it. No scheduler at all.

**D. Read the vendor's own layers.** Raw for narrow windows, `l = 1/2/3` for wide ones, with
PostgreSQL reducing to pixel columns by `GROUP BY` when the chosen layer is still denser than the
canvas. **Chosen.**

**E. Nothing but on-the-fly aggregation over raw.** No coarse source at all.

## What decided it

**The layers already do the work.** The vendor writes up to four points per minute, per hour and per
day, and selects them by magnitude so the period's extremes are among them `[FORUM:1032]`,
`[FORUM:1974]`. That is the same selection our own decimator would compute. Building A or C would
have produced a second copy of data the SCADA already maintains.

**Common retention removed the only argument for owning the summaries.** The original motivation for
A was to keep summaries for a year while dropping raw after two weeks. Once one depth applies to
everything, our summaries would occupy disk without extending history by a single day.

**No scheduler exists that is worth its cost on this platform.** `pg_cron` has no tagged release
supporting Windows. `pgAgent` and `pg_timetable` work, but each is one more process to keep alive.
A trigger on `trends` was excluded twice over: it modifies a vendor object, and a fault in it would
abort the SCADA's own inserts. Option C avoided the scheduler entirely and was the strongest
alternative, which is why it is recorded here as the fallback if D is ever refuted.

**Option E fails at the wide end.** No amount of tuning aggregates a year of raw samples inside an
interactive gesture.

**Option B buys nothing here.** Continuous aggregates read from hypertables, and `trends` is a
declaratively partitioned table SemiBase creates and the SCADA writes into. It cannot be
converted. Timescale would only pay
off if we owned ingestion, which we do not.

## Questions that recurred during the evaluation

These are recorded because they will recur.

**Is a lookup by time not constant-time?** Finding the first row of a range is effectively instant —
a B-tree descent. Reading the range is proportional to the number of rows in it, and computing a
minimum requires examining every value in it. An index accelerates the search, never the reading.
This is why precomputation is about shrinking the number of rows examined, not about finding them
faster. Two refinements: daily partitioning excludes whole days before any reading happens, and on a
cold cache the cost is measured in heap pages, which interleave all variables in write order — so
reading five pens over a window costs nearly what fifty cost. Window width dominates, pen count does
not.

**Were the proposed tiers ring buffers?** No. A ring buffer has a fixed capacity in records and
overwrites the oldest in place. The proposal was append at the head plus dropping whole old
partitions at the tail: depth fixed in time, row count varying with the write rate, deletion a
metadata operation, overflow impossible.

**Why would a maintenance service lag behind live data?** Because the SCADA can write samples with
older timestamps after a database outage, so a bucket computed immediately might miss rows that
arrive later. The lag and the overlapping recomputation existed to absorb that. It never affected
what the operator saw: the newest part of a window is always read from raw. The same seam exists in
the chosen design, where the fresh tail of a wide window is patched from a finer layer.

## What would reopen this

- The pending experiment showing that a coarse layer omits a period's extreme. The read path would
  then have to stop trusting layers for envelopes, and option C becomes the answer.
- A measured write rate near the stated ceiling combined with a requirement for full resolution over
  a year. That combination cannot be served by layers alone.
- Acquiring ownership of ingestion, for example by subscribing to the SCADA's OPC UA server and
  writing our own store. That would reopen option B on its merits.
