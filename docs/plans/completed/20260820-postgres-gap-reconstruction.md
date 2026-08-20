# Gap reconstruction on the direct read path

## Overview

The provider selects `q` and never reads it. `ReadHistoryRow`
(`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:253-257`) projects the first three
columns of `ArchiveStatements.SparseHistoryWindow`, and `HistoryRowFold.Row` carries no quality
member. So a vendor break — an absence of rows bounded by `q = 32` and `q = 16` marker rows — reaches
the chart as one wide step between two real samples.

That is the failure this roadmap names as its worst: a break drawn as a straight line across hours
looks entirely plausible to an operator watching a plasma process, and it is wrong.

The archive states three things that a row absence alone cannot separate
(`docs/architecture/scada-archive.md:160-181`):

| State | Rows present | Correct rendering |
| --- | --- | --- |
| Value unchanged | none | horizontal line at the last recorded value |
| Gap | none | broken line |
| Bad quality | row present, value present | point discarded |

The marks exist to separate the first two. Treating every row absence as a break would shred a steady
signal into fragments; ignoring the marks draws a line across hours of missing data.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-gap-reconstruction

- `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs` — `SparseHistoryWindow` already selects
  `id, t, v, q`, so **the statement does not change for gap data**. The read does.
- `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:253-257` — `ReadHistoryRow` reads
  three columns and drops `q`.
- `SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs` — `Row(long PenId, DateTime ArchiveLocal,
  double? Value)`; the fold walks each pen's consecutive run, converts the timestamp, drops any row
  that does not strictly ascend, and hands parallel `(timestamps, values)` lists to the decimator.
- `SemiPlot/SemiPlot.Core/Trends/MinMaxDecimator.cs:3-4,59,76,94-100` — **the gap machinery already
  exists.** A null value splits the series into segments, and `AppendGap(timestamps[lastNullIndexBeforeSegment])`
  emits the `NaN` column between them. Nothing new has to be built to render a break; the fold has to
  produce the null.
- `docs/architecture/data-integration.md:325-338` — the contract this slice implements, marker by
  marker.
- `SemiPlot/SemiPlot.Tests.Data/Fixtures/RealArchiveFixture.cs` — rows lifted from the customer dump
  and anonymised: intervals, values and quality codes are the vendor's own, and the chosen minute
  holds a `32`/`16` marker pair. The fixture needs no database.
- `SemiPlot/SemiPlot.Tools.ArchiveSeeder/BreakPlan.cs` — the bench seeder writes the same marker pair,
  so the gated tests have breaks to read.
- `SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs` — shipped in `ui-render-and-input-guards`:
  it renders an envelope carrying a `NaN` column and asserts the gap's centre column holds no
  pen-coloured pixel. It is the check that a break *looks* like a break, and it already passes on
  synthetic input. This slice makes it meaningful for archive input.

## Development Approach

- **testing approach**: Regular. Every task ends with tests and they must pass before the next starts.
- **This is the highest-risk slice in the roadmap**, and the risk is not that it crashes — it is that
  it draws something plausible and wrong. Prefer a test that would fail on a plausible-but-wrong
  rendering over a test that only proves no exception was thrown.
- Provider and Core tests live in `SemiPlot.Tests.Data` (xunit v3, raw `Assert.`, all three traits).
  UI tests live in `SemiPlot.Tests` (xunit v3 now, AwesomeAssertions, all three traits).
- **compatibility**: `IDataProvider` is unchanged. `PenHistoryEnvelope` is unchanged — a gap is
  already a `NaN` column in it.

## Acceptance Evidence

Two plausible-but-wrong implementations must fail here, because both satisfy a test that only asks
whether a gap exists: one that **replaces** the marker row's value with a null instead of appending an
anchor after it, losing a real sample and stopping the line one poll early on every break; and one
that anchors after `q = 16` as well, re-breaking every resumption so the line never restarts. The
assertions below are shaped to kill both, and the instrument that does it is counting columns rather
than finding one.

**Evidence 1 — a break renders as a break, and only once.** A `32`/`16` pair over a run of rows yields
an envelope holding **exactly one** `NaN` column, with the `q = 32` row's own value present as a real
column immediately before it and the `q = 16` row's value present as a real column after it with no
anchor following. Fails before the change, because `q` is not read.

**Evidence 2 — a steady signal does not shred.** A long row absence with **no** preceding `q = 32`
yields no `NaN` column at all. An implementation treating every absence as a break passes Evidence 1
and fails this.

**Evidence 3 — the vendor's own rows, not this repository's idea of them.** Both assertions run over
`RealArchiveFixture`. Evidence 2's subject is pen `9002` on the raw layer between `13:50:46.437`
(`q = 0`) and `13:55:04.814` — a **4 min 18 s absence with no marker** in an archive polled every
100 ms, so roughly 2 600 missing polls that are not a break. The fixture also holds an unpaired
`q = 32` as its last marker, which is Evidence 1's trailing-break case for free.

**Evidence 4 — the break survives to the screen.** `ChartGapRenderTests` renders an envelope built in
the shape the fold produces — marker value, anchor, resumption — and asserts the gap's centre column
holds no pen-coloured pixel. The window is rendered at a ratio that makes the gap **several pixel
columns wide**: a real archive break is seconds long, and at a multi-minute window over 800 px a
three-second gap is under one column, where the probe becomes flaky or trivially satisfied. Removing
the anchor from the input must fail it.

**Evidence 5 — a pen written before the window still draws.** A gated test reads a window opening
after a pen's last sample and asserts the pen gets an envelope carrying that sample rather than being
omitted. Fails before the change.

A second gated test carries the same claim for a pen quiet for longer than one partition width, which
is the case a fixed one-day look-back cannot answer: it opens a window one day past the archive's last
row and three days wide, and asserts every pen still comes back with its seed. Only a look-back scaled
to the requested window reaches one; against a bound fixed at the floor the read returns an empty list
and the consumer drops every pen from the chart.

**Evidence 6 — against a real server.** Gated tests over the seeded bench assert a window straddling a
seeded break carries exactly one `NaN` column, and a window inside a steady stretch carries none.

**Evidence 7 — one envelope per pen.** A pen carrying both a seed row and window rows yields exactly
one envelope, with the seed as its first column. This is the assertion that would catch the failure
`HistoryRowFold`'s own contract warns about, and which the `UNION ALL` shape is chosen to make
unreachable.

**Evidence 8 — nothing regressed.** `dotnet test SemiPlot.slnx` reports zero failures. Measured at
`daef170`, the branch point: `SemiPlot.Tests` 369 passed / 0 skipped, `SemiPlot.Tests.Data` 397 passed
/ 0 skipped, with Docker running and `semibase` on `PATH`. The slice adds tests, so the current figure
is higher: `SemiPlot.Tests` 370 passed / 0 skipped and `SemiPlot.Tests.Data` 422 passed / 0 skipped.
`dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**Two existing gated tests change, and that is expected.**
`PostgresHistoryReadTests.AWindowStraddlingTheFirstBreakCarriesNoColumnInsideIt` — renamed
`AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn`, its assertion replaced — opens five minutes
into the archive, so it gains a seed row, and it straddles a `q = 32`, so it gains a `NaN` column; it
asserts exact list equality through `AssertMatchesSeededRows` and fails twice over. Its leading comment
describes the stepping behaviour this slice abolishes. The tests using `QuietWindow()` — which opens at
`ArchiveTemplate.Slice.Start`, so no seed row exists — are unaffected, as is
`AWindowBeforeTheArchiveStartsIsASuccessfulEmptyList`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix, blockers with ⚠️

## Solution Overview

**The fold emits a null; the decimator does the rest.** `MinMaxDecimator` already splits on nulls and
anchors a `NaN` column between segments. So gap reconstruction is not new rendering machinery — it is
the fold learning to translate a marker into the vocabulary the decimator already speaks. That keeps
the change inside the provider, which is what the scope guard requires, and it means the rendering
path is the one already covered by shipped tests.

**Where the null goes.** `docs/architecture/data-integration.md:332` already specifies it: the point
is kept, and a `NaN` anchor is inserted *after* it. The fold appends one entry carrying a null value
one tick after the marker's **converted** timestamp, appended straight onto the pen's timestamp list
so the tick is added on the UTC side and cannot interact with a daylight-saving boundary. One tick is
four orders of magnitude below the archive's `timestamp(3)` resolution, so no real row can land inside
it, and it keeps the series strictly ascending as the envelope requires.

The anchor is its own column no matter how close the tick is: `MinMaxDecimator` slices by **index**
within a non-null segment and appends the gap column outside any bucket, so a null one tick after the
marker cannot be absorbed into the marker's column.

**`q = 16` takes no branch.** The contract says the point is kept and the line resumes there. A
resumption is what the decimator already produces on the far side of a null segment, so `16` needs no
handling: the fold reads the whole `q` column and simply does nothing on that value. That it emits no
anchor is asserted rather than assumed, because an implementation that anchors after `16` too breaks
every resumption immediately and still satisfies a test that only asks whether a gap exists.

**Bad quality is out of scope and stays that way.** `docs/architecture/scada-archive.md:166` says a
bad-quality row is discarded. The archive's own manual states `0x00000000`, `0x00000010` and
`0x00000020` all mean good quality, so the three codes this slice reads are all good-quality rows that
additionally carry a boundary mark. Discarding on other codes is a separate rule with no measured
vocabulary behind it; this slice does not invent one.

**The left edge needs a seed, and it arrives in the same statement.** A pen whose last sample predates
the window start returns no rows, and `postgres-history-read` shipped the interim rule that such a pen
gets no envelope at all. Its consumer half landed in `postgres-wire-up`, which drops the pen from the
chart — correct when the pen has no data, wrong when it has data the window simply does not reach.

The seed is **not** a second statement feeding the same fold. `HistoryRowFold`'s own contract warns
what that costs: a pen appearing in two runs yields two envelopes for one pen, which no consumer
rejects — `TrendChartViewModel.ApplyHistory` keys by `PenId` and one envelope silently overwrites the
other. Instead the windowed statement becomes a `UNION ALL` of two branches under one outer
`ORDER BY id, t`: the per-pen seed row, and the window rows as today. One round trip, no client-side
merge, and the fold's stated precondition — one consecutive ascending run per pen — holds literally
rather than by argument.

The seed row is a real archive row and carries its own `q`. A seed whose `q` is `32` means the window
opens inside a gap, and the anchor the fold appends after it is the first thing in the series — which
`MinMaxDecimator` handles, because a null at index 1 leaves a leading segment boundary it already
anchors.

**The seek backwards is bounded, and the bound scales with the window.** `trends` is partitioned by
day and the primary key is its only index, so an unbounded `t < @from ORDER BY t DESC LIMIT 1` plans
as a `Limit` over a `Merge Append` of every unpruned partition. That node opens and pulls a first
tuple from all of its children before it can emit one, so the cost is one index descent per older
partition, per pen, on **every** window change rather than once at startup — and it is paid whether or
not an older row exists. The seed branch therefore carries a lower bound as well:
`prior.t >= @from - greatest(@to - @from, interval '1 day')`.

The bound is the wider of the requested window and one partition width rather than a fixed day,
because the archive's value-unchanged state is what the seed exists to draw. A recipe setpoint written
once at process start writes nothing for as long as it does not change; under a fixed one-day bound
such a pen has neither a seed nor a window row, and `TrendChartViewModel.ApplyHistory` drops it from
the chart altogether. A pen drawn as nothing is a worse answer than a pen drawn as a flat line, and
the flat line is what `docs/architecture/scada-archive.md`'s three-state table says that state means.
Scaling with the window puts the reach where the operator already asked for it: zoomed out to a week,
the seed reaches back a week; a two-minute window still costs the one-day floor and no more. A pen
with no row inside that look-back gets no seed and, having no window rows either, no envelope — the
same answer as today, reached in bounded time.

Two alternatives were weighed and dropped. A second probe for pens that came back empty needs two
round trips and re-introduces the two-runs-per-pen hazard `HistoryRowFold`'s contract warns about. A
geometric ladder of nested laterals reaches the same answer and is unreadable.

## Technical Details

**`HistoryRowFold.Row`** gains `int Quality`. `ReadHistoryRow` reads column 3.

**The fold's per-pen loop** keeps its existing shape — consecutive run, `ToUtc`, strict ascent — and
gains one branch: after appending a row whose quality is `32`, append `(timestamp + 1 tick, null)`.
The strict-ascent guard already protects against a following row landing on the same tick.

**The seed branch** is a rewrite of `ArchiveStatements.SparseHistoryWindow` in place rather than a
constant beside it, returning at most one row per requested pen: the row with the greatest `t`
strictly before the window start, at the same layer. It is a lateral join over the requested
identifiers so the primary key's leading column is used, in the shape
`ArchiveStatements.ArchiveExtent` already establishes for per-pen subqueries.

**The statement carries both pins.** `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
Guard strategy prescribes a plain literal for statement text written from here on and records that the
document-fence mechanism stays as shipped and does not grow. The literal is therefore added
(`ArchiveStatementTextTests.SeededWindowStatement`), and the existing fence in
`docs/architecture/data-integration.md` is kept as well, because the fence is not new: this statement
was already quoted there for a reader's sake, and a fence that silently stops describing the shipped
text corrupts the next slice's brief while every test stays green. The cost is two failures with two
remedies whenever the text changes; the three copies must end byte-identical.

**Seed rows join the fold ahead of the window rows** for their pen, so the fold's consecutive-run
grouping and strict-ascent guard both hold unchanged.

## Implementation Steps

### Task 1: Read the quality column

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryRowFoldTests.cs`

- [x] add `int Quality` to `HistoryRowFold.Row` and read column 3 in `ReadHistoryRow`
- [x] leave the fold's behaviour unchanged — quality is carried, not yet acted on, so a red test in
      Task 2 has exactly one cause
- [x] correct `ArchiveStatements.cs:43`, which says the fold ignores `q` for now
- [x] update the existing fold tests for the new positional member, asserting nothing about quality
- [x] run tests — must pass before task 2

### Task 2: Turn a break marker into a gap

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryRowFoldTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`

- [x] after appending a row whose quality is `32`, append one entry carrying a null value one tick
      after that row's **converted** timestamp, added directly to the pen's timestamp list
- [x] write the test that a `32`/`16` pair yields **exactly one** `NaN` column (Evidence 1)
- [x] write the test that the `q = 32` row's own value survives as a real column immediately before
      the anchor — this kills an implementation that nulls the marker instead of appending after it
- [x] write the test that the `q = 16` row's value is a real column with **no** anchor after it —
      this kills an implementation that anchors on both markers
- [x] write the test that a long absence with no preceding `32` yields no `NaN` column (Evidence 2)
- [x] write the test that two breaks in one window yield two anchors, and that a `32` as the last row
      still anchors so a break running past the right edge is not dropped
- [x] write the test that a `32` row dropped by the strict-ascent guard emits no anchor — the autumn
      fall-back case `TheSecondPassOverTheRepeatedHourIsDropped` already covers the drop itself
- [x] run tests — must pass before task 3

### Task 3: Assert against the vendor's own rows

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/RealArchiveGapTests.cs`

- [x] drive the fold with `RealArchiveFixture`'s marker pair and assert exactly one `NaN` column
- [x] assert the 4 min 18 s markerless absence for pen `9002` between `13:50:46.437` and
      `13:55:04.814` yields no anchor — roughly 2 600 missing polls that are not a break
- [x] assert the fixture's unpaired trailing `q = 32` anchors
- [x] state in the file header that intervals, values and quality codes are the vendor's own, so the
      test measures the archive rather than this repository's model of it
- [x] run tests — must pass before task 4

### Task 4: Add the seed branch to the statement

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ExplainPlanTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`
- Modify: `docs/architecture/data-integration.md`

- [x] rewrite `SparseHistoryWindow` as a `UNION ALL` of two branches under one outer `ORDER BY id, t`:
      a per-pen lateral returning the row with the greatest `t` **strictly** before the window start —
      strictly, because the window branch already uses `t >= @from`, so an inclusive bound would
      duplicate a boundary row and lean on the strict-ascent guard to swallow it — and the window rows
      as today
- [x] bound the seed's backwards seek with a lower predicate, so a pen with no prior rows costs a
      bounded probe rather than one index descent per older daily partition on every pan. The bound is
      `prior.t >= @from - greatest(@to - @from, interval '1 day')`: the wider of the requested window
      and one partition width, so a pen quiet for longer than a day still seeds a window wide enough
      to reach it. `greatest` const-folds at plan time, so partition pruning is unchanged — read off
      the plan, a one-minute window bounds the seed at `t >= 2026-07-30 01:00:00` and a three-day
      window at `t >= 2026-07-28 01:00:00`
- [x] pin the statement text and its parameter names with a plain literal in the test, which the
      roadmap's Guard strategy records as the rule for statements added from here on. **The fenced
      block in `docs/architecture/data-integration.md` is kept beside it rather than replaced.** The
      Guard strategy's rule is that the fence mechanism does not grow to cover new statements; this
      statement is not new to the document, which already quoted it for a reader's sake, and a fence
      that silently stops describing the shipped text corrupts the next slice's brief while every test
      stays green. Both pins ship, and `ArchiveStatementTextTests` reads the fence at run time, so the
      constant, the fence and the literal must end byte-identical
- [x] add an `EXPLAIN` assertion for the rewritten statement bound through its own shipped binder,
      covering both the pen-with-a-seed and the pen-with-no-prior-rows cases
- [x] run tests — must pass before task 5

### Task 5: Seed the left edge

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryRowFoldTests.cs`

- [x] confirm the fold needs no change to accept the seed row: one statement, one outer ordering, so
      each pen is still one consecutive ascending run. If it does need one, that is a finding — say
      what the outer ordering failed to guarantee
- [x] rewrite `HistoryRowFold`'s XML doc, whose stated precondition names `SparseHistoryWindow`'s
      `ORDER BY id, t` and whose warning about a second statement no longer describes the design
- [x] write the test that a pen carrying both a seed row and window rows yields **exactly one**
      envelope, with the seed as its first column (Evidence 7)
- [x] write the test that a seed carrying `q = 32` opens the window inside a gap — the anchor lands at
      index 1 and the decimator's inter-segment `AppendGap` emits it. Not the leading-edge branch: the
      seed's own value is non-null at index 0, so `segments[0].Start == 0` and that branch never fires
- [x] correct `PostgresDataProvider.cs:98-101`, which calls the no-envelope rule interim and points at
      this slice to revise it
- [x] run tests — must pass before task 6

### Task 6: Assert against a real server

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`

- [x] make `AssertMatchesSeededRows` aware of the gap anchor and the seed row; it asserts exact list
      equality against window-bounded seeder rows today and cannot survive either addition
- [x] rewrite `AWindowStraddlingTheFirstBreakCarriesNoColumnInsideIt` — its name and its leading
      comment both describe the stepping behaviour this slice abolishes — so that it asserts exactly
      one `NaN` column instead (Evidence 6). It replaces rather than sits beside the new assertion
- [x] add a gated test that a window inside a steady stretch carries no anchor
- [x] add a gated test that a window opening after a pen's last sample returns that pen with its seed
      (Evidence 5)
- [x] confirm the `QuietWindow()` tests and `AWindowBeforeTheArchiveStartsIsASuccessfulEmptyList` pass
      unchanged — they open at the slice start, so no seed row exists
- [x] run tests — must pass before task 7

### Task 7: Prove it reaches the screen

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs`

- [x] build the parallel timestamp and value lists in the shape the fold produces — marker value, null
      one tick later, resumption — and hand them to `MinMaxDecimator` directly. **Do not reach for
      `HistoryRowFold`**: it is internal to `SemiPlot.DataSource.Postgres`, whose `InternalsVisibleTo`
      names only `SemiPlot.Tests.Data`, and widening that plus adding a project reference is a
      structure change this slice has no mandate for
- [x] render at a window-to-break ratio wide enough that the gap spans several pixel columns, and
      state the ratio in the test — a three-second break over a multi-minute window is under one pixel
      column, and the probe stops meaning anything
- [x] verify by removing the anchor from the input and confirming the test fails; record what it said
- [x] run tests — must pass before task 8

### Task 8: Verify acceptance criteria

- [x] run every Evidence item and record what each reported
- [x] run the full suite and record both counts against the branch point
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0
- [x] confirm every tracked `.cs` file still begins `ef bb bf`
- [x] raise the application bench and confirm the running application still reads history: non-zero
      `idx_tup_fetch` on the seeded day's partition, zero `seq_scan`, **window untouched** — the count
      is deterministic only when nothing interacts with the chart

**Evidence, as run.**

| Evidence | Tests | Result |
| --- | --- | --- |
| 1 — a break renders as a break, and only once | `ABreakMarkerPairYieldsExactlyOneGapColumn`, `TheBreakMarkersOwnValueSurvivesAsTheColumnBeforeTheAnchor`, `TheResumptionMarkerIsARealColumnWithNoAnchorAfterIt`, `TwoBreaksInOneWindowYieldTwoAnchors`, `ABreakMarkerAsTheLastRowStillAnchors`, `TheAnchorSurvivesDecimationAsItsOwnColumn` | 6 passed / 0 skipped |
| 2 — a steady signal does not shred | `ALongAbsenceWithNoBreakMarkerYieldsNoGapColumn`, `ABreakMarkerDroppedByTheStrictAscentGuardEmitsNoAnchor` | 2 passed |
| 3 — the vendor's own rows | `RealArchiveGapTests` | 7 passed |
| 4 — the break survives to the screen | `ChartGapRenderTests` | 3 passed |
| 5 — a pen written before the window still draws | `AWindowOpeningAfterEveryPensLastSampleStillReturnsThePens` | 1 passed, gated, against the container |
| 6 — against a real server | `AWindowStraddlingTheFirstBreakCarriesExactlyOneGapColumn`, `AWindowInsideASteadyStretchCarriesNoGapColumn` | 2 passed, gated |
| 7 — one envelope per pen | `APenCarryingASeedRowAndWindowRowsYieldsExactlyOneEnvelope`, `EachSeededPenGetsOneEnvelopeWithItsOwnSeedFirst`, `ASeedMarkedAsABreakOpensTheWindowInsideAGap`, `ASeedMarkedAsABreakKeepsItsOwnColumnUnderDecimation` | 4 passed |
| 8 — nothing regressed | `dotnet test SemiPlot.slnx` | `SemiPlot.Tests` 370 passed / 0 skipped, `SemiPlot.Tests.Data` 422 passed / 0 skipped, zero failures |

Branch point `daef170` for comparison: 369 and 397. `dotnet format SemiPlot.slnx --verify-no-changes`
exits 0. BOM: 205 tracked `.cs` files checked, 0 without `ef bb bf`. Zero skips with Docker running and
`semibase` on `PATH`, so the gated tests ran rather than reporting a reason.

The window-scaled bound carries two tests of its own.
`AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks` opens a window one day past the archive's last
row and three days wide, so only a look-back scaled to the window reaches a seed at all;
`TheExpectedSeedLookBackIsTheStatementsOwn` pins the expectation `SeedBefore` computes to the clause
the shipped statement carries, so widening one without the other fails rather than passing stale.

**Mutations, re-run rather than taken on the record.**

| Mutation | What failed |
| --- | --- |
| anchor after `q = 16` as well | 9 of 26 failures across `HistoryRowFoldTests` and `RealArchiveGapTests`, including `ABreakMarkerPairYieldsExactlyOneGapColumn`, `TheResumptionMarkerIsARealColumnWithNoAnchorAfterIt`, `TwoBreaksInOneWindowYieldTwoAnchors` and both `TheRawRunYieldsOneAnchorPerBreakMarkerAndNothingMore` theory cases |
| null replaces the marker's value instead of an appended anchor | 14 of 26 failures, including `TheMarkerlessAbsenceOfPen9002YieldsNoAnchor` and both `TheUnpairedTrailingBreakMarkerAnchors` cases |
| render input rebuilt with `withAnchor: false` | `ArchiveShapedBreak_WithTheFoldsNullAnchor_LeavesEveryBreakColumnWithoutPenColor`: "Expected breakColumns.Where(column => ColumnCarriesPenColor(pixels, dataRect, column)) to be empty … but found at least one item {413}" |
| seed bound narrowed back to a fixed `interval '1 day'` | 4 failures. `AWindowWiderThanTheLookBackFloorSeeksBackAsFarAsItAsks` reports `Assert.Equal() Failure: Collections differ … Expected: [1000, 1001, 2000, 2001, 3000, ···] Actual: []` — every pen dropped from the read — plus `TheExpectedSeedLookBackIsTheStatementsOwn` and both statement-text pins |

**The application bench.** Container `semiplot-bench` (`postgres:17-alpine`, port 55432),
`semibase create`, seeder `--end 2026-08-01T00:00:00 --days 1 --pens 8 --seed 1` writing 229 862 raw
rows into one partition, `tp2026m07d31`. Connection file at `source_time_zone: UTC`. `pg_stat_reset()`
before the run, window untouched, two idle `semiplot_reader` connections in `pg_stat_activity` while it
ran, no log file written under the log directory — a clean start at the `Warning` floor.

| Relation | `idx_scan` | `idx_tup_fetch` | `seq_scan` |
| --- | --- | --- | --- |
| `tp2026m07d31` | 64 | 19 856 | 0 |
| `tpdefault` | 48 | 0 | 0 |
| `semiplot_tags` | 0 | 0 | 3 |

Unchanged by the widened bound: the application's window is narrower than the one-day floor, so
`greatest(@to - @from, interval '1 day')` folds to the same day the fixed bound named. The three
sequential scans of `semiplot_tags` are the planner's choice on an 8-row unpredicated read and sit
outside the `EXPLAIN` guard, which forbids a sequential scan on a `trends` partition only.

**The counters were settled, and settling takes longer than it looks.** An earlier run of this bench
read a stable 32 / 9 928 across twelve consecutive polls and then rose to 64 / 19 856: the application
issues its extent and history reads twice, once per connection, and the second pair arrives well after
the first. Counter stability alone is not the proof — the figure above was taken after both pairs had
landed and then held across a further twelve polls.

**What the seed branch costs, measured rather than inferred.** Each row is one execution under its own
`pg_stat_reset()`, 8 pens, layer 0, a one-minute window:

| Executed | `tp2026m07d31` `idx_scan` / `idx_tup_fetch` | `tpdefault` `idx_scan` / `idx_tup_fetch` |
| --- | --- | --- |
| the shipped statement | 16 / 144 | 8 / 0 |
| its window branch alone | 8 / 136 | 0 / 0 |
| its seed branch alone | 8 / 8 | 8 / 0 |

The two branches add up exactly. The seed costs one index scan and one tuple per pen on the
row-holding partition, plus one index scan and no tuple per pen on `tpdefault` — the `Merge Append`
opening its second child. `EXPLAIN (ANALYZE)` of the shipped statement shows why:
`Limit → Merge Append` over `Index Scan Backward using tp2026m07d31_pkey` (`rows=1 loops=8`) and
`Index Scan Backward using tpdefault_pkey` (`rows=0 loops=8`).

**The bench run decomposes on those figures.** Two extent reads at 16 / 16 on `tp2026m07d31` and 16 / 0
on `tpdefault` each, plus two history reads at 16 / 9 912 and 8 / 0 each, give exactly 64 / 19 856 and
48 / 0. Each history read's 9 912 tuples are 8 seed rows and 9 904 window rows.

**One figure is not attributed, and is recorded rather than explained away.** Against the pre-slice
bench recorded in `docs/plans/completed/20260820-avalonia-12-bump.md` — `idx_tup_fetch` 19 808 — the
delta is 48. The seed accounts for 16 of it, measured above. The remaining **32 sit in the window
branch**: it fetched 16 rows more per read than that run did. Nothing measured here says why a run at a
different commit read 16 rows fewer, and the seed is not the cause.

### Task 9: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/trend-interaction.md`
- Modify: `CLAUDE.md`

- [x] record that the direct read path reconstructs gaps, and how a marker becomes a `NaN` column
- [x] record the seed branch and what it fixes
- [x] correct the passages describing `q` as selected but never read, and the ones assigning gap
      reconstruction to a future slice
- [x] correct `CLAUDE.md`'s test-split section, which still describes `SemiPlot.Tests` as xunit v2
      — already true at HEAD: commit `3b261ef` "build(ui): move to Avalonia 12 and xunit v3 (#19)"
      made the correction, so `CLAUDE.md` is left unchanged
- [x] correct `docs/architecture/trend-interaction.md:221`, which assigned break-marker carriage to
      this slice as future work — the same class of falsehood, in a line pointing at the section this
      task rewrites
- [x] move this plan to `docs/plans/completed/` — not done, and deliberately: archiving is delivery
      work and belongs to the ship step, so the file stays where it is

## Post-Completion

*Items requiring manual intervention — no checkboxes, informational only*

**Manual verification.** Whether a reconstructed break *looks* right — where the line stops, where it
resumes, whether the eye reads it as a break rather than as missing data — is not a machine question.
`ChartGapRenderTests` proves a gap column holds no line; it does not prove the result is legible. That
waits for the demo stand.

One thing to point the stand at specifically: min/max decimation places a segment last column at its
final bucket centre sample rather than at the marker row, so a rendered break is wider than the
recorded one by up to one bucket on each side. That is inherent to the shipped decimator rather than
to this slice, and whether it reads as wrong to an operator is exactly the judgement a machine cannot
make.

**A consumer-side interaction worth recording rather than re-deriving.** A seed row extends an
envelope first timestamp to before the window start, which reaches
`ChartNavigationController.TrackDataExtents`. It is benign: the `_hasData` latch means only the first
envelope ever applied can move `FirstSample`, and `postgres-wire-up` already seeds the window from the
archive extent before any history arrives.

**What the bucketed path would need.** `postgres-bucketed-read` is dropped pending a measurement. If
it is ever re-added, `docs/architecture/data-integration.md:339-343` records that the same information
survives as `q_first`, `q_last` and `breaks`, and that the client walks buckets holding one of two
states. Nothing in this slice forecloses that.

**Remaining slices.** After this slice: postgres-live-edge-and-demo.

**Executed by exec:**

- branch: postgres-gap-reconstruction

## Verify it yourself

**The suite.** `dotnet test SemiPlot.slnx` — `SemiPlot.Tests` 370 passed / 0 skipped,
`SemiPlot.Tests.Data` 422 passed / 0 skipped, zero failures, with Docker running and `semibase` on
`PATH`. `dotnet format SemiPlot.slnx --verify-no-changes` exits 0.

**A break becomes a break, and only once.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~HistoryRowFold"
```

The instrument is counting, not finding. `ABreakMarkerPairYieldsExactlyOneGapColumn` asserts the
column total, the marker's own value surviving as the column before the anchor, and the resumption
carrying no anchor after it. Two implementations that a find-a-gap test would accept are killed by
those counts, and both were run rather than reasoned about: nulling the marker's value instead of
appending after it fails 14 of 26 fold tests; anchoring after `q = 16` as well fails 9 of 26.

**A steady signal does not shred, proved against the vendor's own rows.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~RealArchiveGap"
```

`TheMarkerlessAbsenceOfPen9002YieldsNoAnchor` covers a **4 minute 18 second absence with no marker**
in an archive polled every 100 ms — roughly 2 600 missing polls that are not a break, taken from the
customer dump rather than invented here. A fold treating any long absence as a break fails 5 of these
7 tests.

**It reaches the screen.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ChartGapRender"
```

`ArchiveShapedBreak_WithTheFoldsNullAnchor_LeavesEveryBreakColumnWithoutPenColor` renders a 3.5 s
break — the shortest of the vendor fixture's three — over a 120 s window, which measures out at
20 pixel columns. The width is asserted at a floor of 12, so the probe cannot quietly degenerate to a
single antialiased column. Removing the anchor fails it at pixel column 413.

**Against a real server.**

```powershell
dotnet test SemiPlot.slnx --filter "FullyQualifiedName~PostgresHistoryRead"
```

A window straddling a seeded break carries exactly one `NaN` column with the marker's value before it
and the resumption after; a window inside a steady stretch carries none across all eight pens; a
window opening after every pen's last sample returns eight envelopes of one column each — the seed.

**The left edge.** `SparseHistoryWindow` is one statement: a `UNION ALL` of a per-pen seed lateral and
the window branch under one outer `ORDER BY id, t`. One round trip, and the fold's precondition — one
consecutive ascending run per pen — holds literally rather than by argument, which is what makes the
two-envelopes-per-pen failure its own contract warns about unreachable.

The seed reaches back `greatest(@to - @from, interval '1 day')`, so a pen quiet for days still draws
when the operator zooms out far enough to ask for those days. Narrowing it back to a fixed day fails
four tests, the gated one reporting every pen dropped.

**Bench figures, measured on an untouched window.** `tp2026m07d31`: 64 index scans, 19 856 rows
fetched, 0 sequential. The seed costs one index scan and one tuple per pen per history read; the
branches decompose exactly when measured separately. Against the 19 808 recorded before this slice the
delta is 48, of which the seed accounts for 16 — the residual 32 sit in the window branch and are
recorded without an explanation rather than attributed.

**A trap for anyone repeating that measurement**: counter stability is not proof the bench has
settled. One run held 32 scans and 9 928 rows across twelve consecutive polls before rising to the
figures above, because the application issues its extent and history reads once per connection and the
second pair lands well behind the first.

**What no check here covers.** Whether a reconstructed break *looks* right — where the line stops,
where it resumes, whether an operator reads it as a break rather than as missing data — is not a
machine question. `ChartGapRenderTests` proves a gap column carries no line; it does not prove the
result is legible. Min/max decimation also places a segment's last column at its final bucket's centre
sample, so a rendered break is wider than the recorded one by up to one bucket on each side. Both wait
for the demo stand.
