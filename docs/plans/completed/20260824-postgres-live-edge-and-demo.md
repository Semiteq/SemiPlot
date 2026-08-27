# PostgreSQL live edge, demo bench and the stub's retirement

## Overview

The application draws real archive history but its live edge is static:
`PostgresDataProvider.Subscribe` returns `Observable.Empty`
(`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:55-60`), so `TrendCoordinator` never
receives a batch and the chart never advances on its own. This slice gives the provider a real poll,
fills the coarse layers' fresh tail so a window ending at the live edge is not short at its right
edge, adds a `--follow` writer that moves that edge on a wall-clock cadence, deletes
`SemiPlot.DataSource.Stub`, and lands two end-to-end journeys that read a real archive through the
whole composed chart.

What it solves, in the order the failures matter:

- **No live edge.** A viewer for a running process that never updates is not a viewer. The poll
  closes it.
- **A short right edge.** `docs/architecture/data-integration.md:318-320` records the fresh tail as
  outstanding and the provider's job; at a coarse layer the newest rows are up to one point spacing
  old, so the curve stops short of "now" and the operator reads a stale value as the current one.
- **Synthetic data still shipping.** `SemiPlot.DataSource.Stub` is reachable through `--use-stub`
  (`SemiPlot/SemiPlot.UI/StartupOptions.cs:45-47`,
  `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs:59-64`). Its retirement is what closes the roadmap.
- **Nothing proves the composed application against a real archive.** The gated suite exercises the
  provider; no test drives `TrendChartViewModel` over a database. Two journeys close that.

It integrates at exactly one seam. `IDataProvider`
(`SemiPlot/SemiPlot.Core/Data/IDataProvider.cs:7-22`) gains one member for connection state;
everything else is provider-internal, chart-model-internal, or the bench.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice postgres-live-edge-and-demo

Verified against code on 2026-08-24 at `f34567c`. Every file:line below was opened while writing
this plan.

### The live edge today, and what a real `Subscribe` must satisfy

`PostgresDataProvider.Subscribe` validates `penIds` and returns `Observable.Empty`
(`PostgresDataProvider.cs:55-60`). Its consumers:

| Consumer | What it does | What the real poll must therefore satisfy |
| --- | --- | --- |
| `TrendCoordinator.BuildRealtimeBatches` (`SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs:85-99`) | `.Buffer(_batchWindow, _dataScheduler)`, drops empty windows, folds to a `RealtimeBatch`, `.ObserveOn(_uiScheduler)`, `.Publish().RefCount()` | Cold per call, as `IDataProvider.cs:9` states. `RefCount` disposes the upstream when the last subscriber goes, so disposal must stop the poll |
| `TrendCoordinator.BuildRealtimeBatch` (`TrendCoordinator.cs:101-120`) and `BuildColumn` (`:122-134`) | builds `rowOfTimestamp` from the batch's distinct timestamps and writes `column[rowOfTimestamp[sample.TimestampUtc]]` | One sample per (pen, timestamp) per batch. A repeat overwrites silently at `TrendCoordinator.cs:130` |
| `TrendChartViewModel` constructor (`SemiPlot/SemiPlot.UI/Chart/TrendChartViewModel.cs:91-92`) | subscribes `ApplyRealtimeBatch` for the view model's lifetime | Never terminates, and never pushes an error: the subscription has no `onError` handler, so `OnError` would go unhandled on the UI scheduler |
| `ChartRealtimeApplier.Apply` (`SemiPlot/SemiPlot.UI/Chart/ChartRealtimeApplier.cs:12-20`) | appends or folds each value, then `Navigation.OnLiveEdge(batch.Timestamps[^1])` | Timestamps ascending inside a batch |
| `PostgresCompositionTests.SubscribeCompletesImmediately` (`SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs:174-184`) | `await provider.Subscribe([1L]).ToArray()` | **This test hangs forever against a poll that never completes.** It must be rewritten in the same task that lands the poll. `CLAUDE.md` states a hung xunit v3 executable locks the next build with MSB3027/MSB3021 |
| `PostgresCompositionTests.SubscribeRejectsANullPenList` (`:164-172`) | `Assert.Throws<ArgumentNullException>` | The null guard stays ahead of everything |
| `FakeDataProvider.Subscribe` (`SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs:93-105`) | `Observable.Interval` on an injected scheduler | Follows any interface change |
| `RandomStubDataProvider.Subscribe` (`SemiPlot/SemiPlot.DataSource.Stub/RandomStubDataProvider.cs:38-54`) | same shape, anchored to the wall clock at subscription | Deleted in this slice |

The empty observable satisfies every one of those trivially because it emits nothing and completes
at once.

> ⚠️ **Superseded by the review pass (`6e1056c`).** The `BuildRealtimeBatch`/`BuildColumn` row above
> records the shape at `f34567c` and is kept as the discovery record. That shape is gone: a column
> over the batch's shared timestamp list wrote a `null` at every timestamp the pen did not sample,
> and a `null` is how the chart encodes a break, so on a per-variable change-based archive every pen
> gained a fabricated break at every other pen's timestamp. `PenRealtimeValues` now carries the
> pen's own `TimestampsUtc` beside a non-nullable `IReadOnlyList<double>`, so the batch has no
> representation for a break at all. See the row in **New and changed types**.

### The seam invariant, and where it breaks

`TrendPenState.AppendRealtime` (`SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs:93-108`) appends the
incoming point to `_centerPoints` and `_bandPoints` with no comparison against the last point.
ScottPlot's `Scatter` holds a live reference to that list (`TrendPenState.cs:20-22`), so a timestamp
at or before the previous one draws a segment running backwards across the plot. That is the join of
history and realtime: `LoadHistory` (`TrendPenState.cs:63-80`) clears both lists and fills them from
the envelope, and every later realtime sample is appended after them.

`PenHistoryEnvelope` rejects a non-ascending series in its constructor
(`SemiPlot/SemiPlot.Core/Trends/PenHistoryEnvelope.cs:25-33`) and `HistoryRowFold` drops a row that
does not advance (`SemiPlot/SemiPlot.DataSource.Postgres/HistoryRowFold.cs:71`), so history alone is
always ascending. Nothing guards the realtime append.

`ChartNavigationController.OnLiveEdge`
(`SemiPlot/SemiPlot.UI/Chart/ChartNavigationController.cs:144-149`) does guard its own field with
`if (now > _liveEdge)`, so the axis never walks backwards — which is why a violated seam shows as a
stray line on the curve rather than as a jumping window.

There is a second path no provider can see. `TrendChartViewModel.ApplyHistory`
(`TrendChartViewModel.cs:508-538`) reloads every envelope on a navigation gesture, so history's last
point can move forward past samples the poll already delivered, and the next emission — which is
only required to be newer than what the poll itself last sent — can then land before it. The
invariant therefore needs both halves: the provider never emits a timestamp at or before its own
last, and `AppendRealtime` ignores a point that does not advance the pen's own series.

### What the archive can put on the wire, and what `Sample` can carry

`Sample` is `public sealed record Sample(long PenId, DateTime TimestampUtc, double Value)`
(`SemiPlot/SemiPlot.Core/Trends/Sample.cs:3`) — `Value` is non-nullable, so the realtime seam has no
null channel. Two archive states meet that limit.

**A null `v`.** The column is `double precision` and nullable by DDL, and no null was ever observed
(`docs/architecture/scada-archive.md:79`, `:190`). `PostgresDataProvider.ReadHistoryRow` still guards
it (`PostgresDataProvider.cs:255-262`, `reader.IsDBNull(2) ? null : reader.GetDouble(2)`), because a
`GetDouble` on a null throws. Inside a poll tick that throw would be caught by the tick's own catch
and counted as a connection failure, and three of them would raise a lost-connection banner over a
healthy archive. The poll therefore reads `v` through `IsDBNull` as well.

**A `q = 32` row.** That is the archive's break mark — the last sample before a break — and it
carries a real value (`docs/architecture/scada-archive.md:180-181`, `:190`: a gap is not encoded as a
null value). `HistoryRowFold` keeps that row and appends a synthetic null one tick later
(`HistoryRowFold.cs:81-85`), which `MinMaxDecimator` turns into the NaN column that draws the gap.
The poll cannot do the same, because the null it would have to append has no representation in
`Sample`.

### The fresh tail

`docs/architecture/data-integration.md:318-320` states it verbatim:

> **Fresh tail** — outstanding, and the provider's job. Coarse layers are flushed on their own
> cadence, so
> a window reaching "now" has an empty tail in `l=1/2/3`. The provider fills the tail from `l=0` and
> concatenates. The seam is the newest timestamp present in the coarse layer.

Nothing of it exists. `QueryHistoryAsync` (`PostgresDataProvider.cs:105-160`) issues
`ArchiveStatements.SparseHistoryWindow` once with the caller's `@layer`
(`PostgresDataProvider.cs:138-140`), reads the rows and hands them to `HistoryRowFold.Fold`
(`PostgresDataProvider.cs:151`). There is one statement, one round trip and no concatenation
anywhere.

`SparseHistoryWindow` (`SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs:78-97`) takes
`@ids`, `@layer`, `@from` and `@to`, so it already reads the raw layer when `@layer` is bound to `0`
— the tail needs no statement of its own. Its seed branch returns one row strictly before `@from`
per pen (`ArchiveStatements.cs:81-90`), which the fold drops when the tail rows are concatenated
after the coarse rows, because `HistoryRowFold.Fold` keeps a row only when `utc > timestamps[^1]`
(`HistoryRowFold.cs:71`). The per-pen seam therefore falls out of the fold rather than needing a
per-pen query bound.

**One read cannot carry a per-pen lower bound.** `SparseHistoryWindow` binds one `@from` for every
pen, so a single tail read starts at one instant for all of them. A pen whose coarse seam sits
earlier than that instant would get coarse rows, then a range no row covers, then tail rows — and a
range with no null in it is not a gap: `HistoryRowFold` emits a gap only from a null value
(`HistoryRowFold.cs:71-86`), and `MinMaxDecimator` turns only that null into the NaN column. It would
draw as one straight interpolated segment across missing time, which is the failure this repository
keeps a render guard against (`SemiPlot/SemiPlot.Tests/UI/Chart/ChartGapRenderTests.cs:113-126`). The
tail therefore drops the rows of any pen whose own seam falls before the tail's start, rather than
concatenating across the hole.

**One period is not a fault threshold.** `AggregationLayerExtensions.ToPointSpacing`
(`SemiPlot/SemiPlot.Core/Trends/AggregationLayer.cs:19-29`) makes a layer's spacing a quarter of its
period, so at `Day` one period is 24 h and at `Hour` one hour. A coarse layer trailing the raw layer
by up to a period is ordinary, not a fault, so the clamp below is a cost bound and nothing else: it
caps how much raw data one history query may pull, and a layer further behind than that keeps its
short right edge instead of paying for a full-resolution read of days.

### The stub's removal

| Reference | What it is | Cost of deleting it |
| --- | --- | --- |
| `SemiPlot/SemiPlot.UI/StartupOptions.cs:9`, `:20`, `:45-47` | the `UseStub` record member, its `DefaultUseStub = false`, and the `--use-stub` parse arm | The record loses a member; three tests lose assertions |
| `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs:8`, `:56-64`, `:87-93` | the `using`, the branch that registers `AddData()` and reads no connection file, and `BuildStubServiceProvider` | Nothing else calls either |
| `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:33-34`, `:36` | the comment naming the switch and the stub project reference. **`:35` is the Postgres reference and stays** | Comment goes with it |
| `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj:29` | the test project's reference | — |
| `SemiPlot.slnx:4` | the solution entry | — |
| `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs:11`, `:30-115` | the `using`, 2 `[Fact]` plus 6 `[Theory]` at two `InlineData` each — 14 cases, of which 7 are the stub's | `StubContainer_ResolvesTheStubProvider` goes; the six theories collapse to facts over the archive container |
| `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs` | 18 `[Fact]` plus one `[Theory]` at four `InlineData` — 22 cases | All go |
| `SemiPlot/SemiPlot.Tests/Core/Data/SyntheticPenCatalogTests.cs` | 5 `[Fact]` | Four go; `Build_EveryColorIsSixDigitHex` (`:56-62`) moves — see below |
| `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs:193-205` | `Run_OverTheStubContainer_CarriesPensAndExtent` | Goes. `Run_WithNoConnectionFile_FailsInsteadOfFallingBackToTheStub` (`:207-222`) stays; only its wording mentions the stub |
| `SemiPlot/SemiPlot.Tests/UI/StartupOptionsTests.cs:70-85` | `Parse_UseStub_IsValuelessSwitch`, `Parse_UseStubLast_IsHonoured` | Both go; `:24`, `:103` and `:122` lose their `UseStub` assertions |
| `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs:124-127`, `:215-218` | two production comments explaining a guard ordering by naming the other implementation | Reworded as this provider's own contract |
| `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:13` | the comment stating `AddPostgresData` is "named apart from the stub's `AddData`" | Reworded |
| `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryArgumentGuardTests.cs:94`, `:103`, `:106`; `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs:162` | four comments describing a cross-implementation contract | Reworded |
| `docs/architecture/data-integration.md:75-77`, `:437-442`, `:606-607`; `overview.md:68-69`, `:76`, `:112`, `:126`; `postgres-topology.md:88`, `:116`; `CLAUDE.md:217`, `:222-223`; `readme.md:37`; `docs/plans/backlog.md:9` | documentation naming the stub as shipping | Task 16 |

**37 test cases leave `SemiPlot.Tests`**: 22 + 5 + 7 + 1 + 2. One of them arrives in
`SemiPlot.Tests.Data`, so the data suite gains a case.

**`SyntheticPenCatalogTests.Build_EveryColorIsSixDigitHex` moves rather than dies.**
`SemiPlot/SemiPlot.Tools.ArchiveSeeder/SyntheticPenCatalog.cs` is byte-identical to the stub's copy
apart from its namespace (verified by `diff` on 2026-08-24), and that copy's colours are what
`TagCatalogWriter` writes into `semiplot_tags`
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/TagCatalogWriter.cs:61`) and what
`ArchiveStatements.PenCatalog` (`ArchiveStatements.cs:25-29`) reads back into `Pen`. The seeder's
golden digest is over `ArchiveRow`, which carries `(Id, Layer, Timestamp, Value, Quality)` and no
colour (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveRow.cs:3`), so nothing else pins the colour
format. The other four cases — determinism, unique identifiers, group counts, min-not-above-max —
are the stub catalogue's own shape and go with it.

Who sets `--use-stub`: nothing in the repository. `StartupOptions.Parse` reads it from the process
arguments (`StartupOptions.cs:22-56`), `Program.Main` calls `Parse(args)`
(`SemiPlot/SemiPlot.UI/Program.cs:23`), and the only other callers are the two `StartupOptionsTests`
cases and `StartupProbeTests.Run_OverTheStubContainer_CarriesPensAndExtent`, which passes the
literal `"--use-stub"`. It is a developer's hand-typed switch and nothing else.

**No test asserts behaviour only the stub provides.** `RandomStubDataProviderTests` asserts the
shared `IDataProvider` contract against the synthetic implementation; the ladder's own numbers —
each layer's point spacing and the quarter-period rule — are held independently by
`SemiPlot/SemiPlot.Tests/Core/Trends/AggregationLayerTests.cs:14-87`, which tests `ToPointSpacing`
directly. Deleting the stub tests loses no rung.

### The error vocabulary and the test that forces it

The coverage test is `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`. It forces
work through three mechanisms:

1. `CollectErrorTypes` (`:221-236`) enumerates, by reflection, every exported non-abstract class
   assignable to `IError` in the namespace of `ArchiveUnreachableError` and in the namespace of
   `StartupReadTimedOutError`.
2. `ErrorTypeEnumeration_CoversBothNamespaces` (`:38-45`) asserts `HaveCount(8)` at `:43`, over a
   comment at `:41-42` reading "Core's seven types and the UI-local one". Three new types make it
   `11`, and both the constant and the comment move with it.
3. `EveryPublicErrorType_MapsToItsOwnState` (`:26-36`) instantiates each type through `Instantiate`
   (`:240-249`) — `type.GetConstructors().Single()`, so exactly one public constructor — and fails
   when `StartupFailureMapper.Map` returns `StartupFailureMapper.GenericTitle`
   (`SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs:26`). Each new type therefore needs an arm
   in the switch at `StartupFailureMapper.cs:32-44`.

**A constructor-parameter constraint follows from the same test.** `SampleValue` (`:251-275`)
supplies a value for `string`, `int`, `TimeSpan` and any enum, and throws `NotSupportedException`
for anything else. A new error type carrying a collection, a `DateTime` or a nullable value type
breaks the coverage test at construction. Every new type below uses only those four kinds.

**Where two of the three types were deferred.**
`docs/plans/completed/20260817-postgres-provider-scaffold.md:637-641`:

> **What the composition slice must additionally define, and this slice deliberately does not.** The
> startup probe distinguishes states no code here can produce: an unexpected `trends` shape and a
> non-empty default partition. Their error types belong to the slice that can raise them — the slice
> that
> can produce a failure defines its type — so they are absent here by design rather than by
> oversight.

That reasoning is inherited unchanged: this slice is the first that can raise either state, so it
defines both types.

The non-empty default partition is a fault named in `docs/architecture/scada-archive.md:265-267`:
rows fall into `tpdefault` when the engine fails to create the next daily partition, `tpdefault` is
never pruned, and it defeats partition elimination. It has a probe of its own and an operator remedy
of its own.

**The unexpected shape has no probe, and must not gain one.** The shipped scope guard for
`semibase-container-provisioning`
(`docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:665-666`) reads "no change to what the
provisioning does, no second transcription of the vendor DDL on this side, and no shape assertion
written to replace the retiring one", with the reasoning at `:637-638`: "a tool that creates the
table has nothing to verify it against, and a second transcription in this repository would be the
drift the move exists to kill." A reader comparing `information_schema.columns` against an expected
shape held here is exactly that transcription. The state is reached instead from a real read: a
`trends` whose columns have moved answers `42703` (`undefined_column`), which today falls to
`ArchiveExceptionMapper`'s catch-all arm (`ArchiveExceptionMapper.cs:118`) and reaches the operator
as `ArchiveReadFailedError`, "a reason this build does not recognise". `ArchiveShapeUnexpectedError`
is what names it. `Npgsql.PostgresErrorCodes.UndefinedColumn` exists in Npgsql 10.0.3 (verified in
the restored assembly), and `ArchiveExceptionMapperTests` uses `42P07` as its unmapped example
(`:151`, `:169`), so no existing case moves.

`ArchiveExceptionMapper` (`ArchiveExceptionMapper.cs:95-119`) is therefore extended by one arm and
is not extended by `ArchiveDefaultPartitionNotEmptyError`, which is not SQLSTATE-derived.

### The bench and the writer

`ArchiveWriter.WriteAsync` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs:33-81`) today:

1. requires `public.trends` to exist — `ArchiveExistsCommand` (`:16`), checked at `:47-52`, failing
   with a message naming `semibase bench`;
2. **refuses an archive that already carries rows or day partitions** — `ArchiveIsSeededCommand`
   (`:21-29`), checked at `:54-59`;
3. creates every covered day partition and runs one binary `COPY`, both inside one transaction
   (`:64-73`).

Consequence for `--follow`: the second call against a seeded database is refused by the guard at
`ArchiveWriter.cs:54-59`. The two behaviours differ in exactly that one check, so appending is a
parameter of the existing write path rather than a second method beside it.

`PartitionScript.CreateStatement` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs:23-32`)
emits a plain `CREATE TABLE`, which fails on a day an earlier run already created. `IF NOT EXISTS`
on that one statement serves both callers, and is safe on the seed path because the seeded refusal
runs first and rejects any non-`tpdefault` partition (`ArchiveWriter.cs:21-29`, checked at `:54-59`,
before the partition statements execute at `:66-69`) — a seed run therefore never meets an existing
day partition, so `IF NOT EXISTS` can never mask one there.

`SeederOptions` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs:8-16`) is a positional
record whose `End` is a non-nullable `DateTime` (`:10`), required unconditionally (`:119-123`), and
whose `Validate` runs an ordered chain with `ValidateSpan` first (`:156-172`, `:158-159`). Every
later check reads `Start`, which is `End - Days` (`:66`). A follow run has no `End`, so it cannot
pass through that record at all: with `End` left at `default(DateTime)`, `ValidateSpan` computes
`latestDays = 0` (`:193`) and rejects the default `--days 1` (`:195-199`) before any follow check
could run.

`SeederOptions` is also hashed into the bench template's database name.
`SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs:25-33` constructs `Slice` positionally
with eight arguments and `ComputeName` (`:120-140`) digests six of its members plus the seeder
assembly's module version. Adding a member without a default breaks that construction; adding one
with a default leaves the digest silently unchanged. Follow mode therefore gets its **own** options
type, and `SeederOptions` is not touched at all: `End` stays required and non-nullable for a seed
run, the ordered chain stays as it is, and the template's name provably cannot move.

`RawLayerGenerator.Generate` (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/RawLayerGenerator.cs:25-39`)
walks a whole span from `options.Start` and asserts `Days >= 1` (`:27`), so it cannot generate the
seconds-wide slice a follow tick needs. `--follow` gets a small generator of its own over
`SyntheticValueWalk` and `RawLayerGenerator.SelectPens` (`RawLayerGenerator.cs:43-70`).

`docs/architecture/bench.md` carries the recipes `--follow` joins: the ownership table (`:9-13`)
stating the seeder owns the daily partitions and the rows, the never-destroys rule (`:23-27`), the
standard slice (`:29-37`), the two-rows-per-change shape (`:43-47`), and the application bench
(`:169-233`) whose container recipe (`:187-200`) clones `semiplot_provisioned`, seeds the clone to
`--end 2026-08-01T00:00:00` (`:199`) and requires an end well in the past (`:207-209`).

### The two journeys, and the project they run in

`SemiPlot/SemiPlot.Tests/xunit.runner.json` sets `"failSkips": true`, and
`docs/architecture/testing-strategy.md:125-130` states the policy it serves:

> The split also decides skip-versus-fail per project. `SemiPlot.Tests` holds no gated test — every
> test in it runs on any machine with the SDK — so a skipped test there is a mistake rather than a
> stated absence, and `SemiPlot/SemiPlot.Tests/xunit.runner.json` sets `failSkips` to turn one into a
> failure on both CI legs. `SemiPlot.Tests.Data` keeps its skips: `DatabaseGate` states a reason when
> a runtime is missing, and `SEMIPLOT_REQUIRE_DB` is what a pipeline sets to make that a failure. It
> carries no `xunit.runner.json`, and must not gain one.

`failSkips` is an assembly-wide setting with no per-test scope, so one assembly cannot hold both
properties. Putting a gated journey into `SemiPlot.Tests` turns every planned skip into a failure:
the Windows `build-and-test` leg cannot host a Linux container and would go red every run, as would
every developer machine without one. Setting `SEMIPLOT_REQUIRE_DB` on both CI legs does not rescue
it — the Windows leg would then fail rather than skip, the same red for the same reason. Removing
`failSkips` rescues it only by deleting a guard over 371 tests in order to admit 2.

**The layer already exists on paper.** `docs/architecture/testing-strategy.md:14-19` defines three
categories and populates two: the end-to-end row — several foreign boundaries, the production
composition under test, "few, and thin" — holds no automated test at all, and its only occupant is
the manual application bench in `docs/architecture/bench.md`, whose runner is a person. These two
journeys are that row's first inhabitants, and the three axes that justify the existing split all
point the same way for this one: skip policy, dependency closure (the UI plus the container harness,
a union neither existing project is permitted), and process isolation.

**The journeys therefore get a project of their own, `SemiPlot.Tests.Journeys`.** Its skips are
stated absences, so it carries no `xunit.runner.json` and `SEMIPLOT_REQUIRE_DB` is what a pipeline
sets — the policy `SemiPlot.Tests.Data` already runs under. `SemiPlot.Tests` keeps `failSkips` and
keeps the property that makes it meaningful. `SemiPlot.Tests.Data` cannot host the journeys instead:
`CLAUDE.md` forbids it a reference to the UI, because that would put Avalonia, ScottPlot and
SkiaSharp into the data suite and its Linux job. The same document's reason for the existing split
argues for this one — "an xunit v3 test project is one executable, so keeping the two apart keeps the
container lifecycle and the Avalonia dispatcher in separate processes, where a hung UI test cannot
wedge the harness" — and a journey is the first thing that needs both at once.

References: `SemiPlot.UI` for the composed chart, `SemiPlot.Tests.Data` for the container harness,
`Avalonia.Headless.XUnit` for the test attribute. Every type it needs is public —
`TrendCoordinator`, `TrendChartViewModel`, `ChartNavigationController` and `TrendPenState` are
`public sealed` in `SemiPlot.UI`, and `PostgresContainerFixture`, `SeededArchive`, `ArchiveTemplate`,
`ArchiveDatabase` and `ArchiveProviderFactory` are public in `SemiPlot.Tests.Data` — so no
`InternalsVisibleTo` entry changes.

Measured on 2026-08-24 by giving `SemiPlot.Tests` a reference to `SemiPlot.Tests.Data`, building and
reverting:

- `dotnet build` succeeds with 0 warnings and 0 errors, so the reference direction compiles.
- `SemiPlot/Artifacts/bin/SemiPlot.Tests/debug/bench` and `.../Fixtures` are present, so the
  `None Update="bench\**" CopyToOutputDirectory` item at
  `SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj:24` flows across a project reference into
  the referencing project's output directory. `PostgresContainerFixture` builds the image from
  `Path.Combine(AppContext.BaseDirectory, "bench")`
  (`SemiPlot/SemiPlot.Tests.Data/Integration/PostgresContainerFixture.cs:170-177`), so it resolves in
  a referencing project too.
- `dotnet test` on `SemiPlot.Tests` still reports 371 tests, so referencing the other test project
  adds no discovered tests.

The harness a journey needs:

| Type | What it gives | Where |
| --- | --- | --- |
| `PostgresContainerFixture` | one server per run; `IsAvailable`/`UnavailableReason`; `CloneTemplateAsync`, `CloneProvisionedAsync`; `RequireAvailable()` | `Integration/PostgresContainerFixture.cs:76-113` |
| `ArchiveTemplate` | the seeded template's name and `Slice`, the `SeederOptions` every gated expectation is generated from | `Integration/ArchiveTemplate.cs:19-39` |
| `ArchiveDatabase` | a clone with admin, writer and reader connection strings, dropped on disposal | `Integration/ArchiveDatabase.cs:7-20`, `:90-108` |
| `SeededArchive` | the `IClassFixture` that clones the template, skipping the clone when the fixture is unavailable | `Integration/SeededArchive.cs:11-33` |
| `ArchiveProviderFactory` | the provider built through the real `AddPostgresData`, on a non-UTC source zone, at a 1 s poll interval (`:26`) | `Integration/ArchiveProviderFactory.cs:18-54` |
| `DatabaseGate` | skip-with-reason, or failure under `SEMIPLOT_REQUIRE_DB` | `Integration/DatabaseGate.cs:10-28` |

**`SeededArchive` is read-only by contract.** Its comment (`SeededArchive.cs:5-7`) states that the
counts the tests assert are the template's, so every test in the class must see the same database and
must leave it as it found it. No class that appends rows may take it. A class that appends implements
`IAsyncLifetime` itself, calls `postgresContainerFixture.CloneTemplateAsync()` in `InitializeAsync`
and disposes the clone in `DisposeAsync`; xunit constructs the test class once per test method, so
each appending test gets a database of its own and leaves nothing behind.

**The existing gating mechanism, end to end.** A gated class takes `PostgresContainerFixture` from
the collection and calls `postgresContainerFixture.RequireAvailable()` as the first line of each
test — the shape `ExplainPlanTests` uses (`Integration/ExplainPlanTests.cs:107`, `:135`, `:179`).
That call reaches `DatabaseGate.Require(UnavailableReason, TestEnvironment.DatabaseRequired)`
(`PostgresContainerFixture.cs:110-113`), which returns when a runtime answered, calls `Assert.Skip`
with the stated reason when it did not, and throws when `SEMIPLOT_REQUIRE_DB` is set
(`DatabaseGate.cs:12-27`). No new mechanism is needed, which is what the roadmap says.

One piece is not reusable across the assembly boundary: `ArchiveDatabaseCollection`
(`Integration/ArchiveDatabaseCollection.cs:7-11`) is a `[CollectionDefinition]`, and a collection
definition is discovered per test assembly.

> ASSUMPTION: xunit v3 discovers `[CollectionDefinition]` only within the test assembly being run,
> so
> `[Collection(ArchiveDatabaseCollection.Name)]` written in `SemiPlot.Tests.Journeys` would not bind
> to the definition declared in `SemiPlot.Tests.Data`. The plan therefore declares a local
> `[CollectionDefinition]` over the same `ICollectionFixture<PostgresContainerFixture>` inside
> `SemiPlot.Tests.Journeys`, which is correct whether or not the assumption holds.

**Two containers per solution-wide run.** Once `SemiPlot.Tests.Journeys` holds a container fixture,
`dotnet test SemiPlot.slnx` raises one server for it and one for `SemiPlot.Tests.Data`, because each
xunit v3 project is its own executable with its own collection fixtures. That is the cost of the
split, and it is stated wherever this plan tells the operator to run the whole solution.

**The journeys' Avalonia shape.** Both construct `TrendChartViewModel`, so both take `[AvaloniaFact]`
and the project needs its own `TestAppBuilder` carrying `[assembly: AvaloniaTestApplication]` — the
one in `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs:10-21` is per-assembly and does not travel. The
scheduler pair is the one `ChartGapRenderTests.CreateViewModel` already uses under `[AvaloniaFact]`
(`ChartGapRenderTests.cs:168-181`): `ImmediateScheduler.Instance` as the UI scheduler for both
`TrendCoordinator` and `TrendChartViewModel`, so no batch waits on a dispatcher pump. The data
scheduler is the `IScheduler` the provider's own container registers — `DefaultScheduler.Instance`
(`PostgresDataServiceCollectionExtensions.cs:21`) — resolved from the `ServiceProvider`
`ArchiveProviderFactory.Build` returns, so the poll runs at its real cadence on a real thread.

That puts every gate on the thread-pool side and the test body on the test's own thread, which is
what `CLAUDE.md`'s rule addresses: a gate awaited by the test body and completed by production code
takes `TaskCreationOptions.RunContinuationsAsynchronously`, or the poll thread resumes the test body
inline and the rest of the test — ScottPlot rendering, Avalonia types — runs off the test thread.
Both journeys build their gates as
`TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)` completed from an
`IObservable` subscription, the shape `ChartHistoryRequestDebouncerTests.cs:69` and `:71` already use. No
gate in either journey is completed by the test body, so no gate omits the flag.

**A CI fact the roadmap's blast radius did not name.** `.github/workflows/ci.yml:91-95` sets
`SEMIPLOT_REQUIRE_DB: "1"` on `data-tests` only, and the comment at `:92-94` states that the two
other jobs omit it deliberately because "`SemiPlot.Tests` has no gated test to require one". That
sentence stays true — the journeys are not in `SemiPlot.Tests` — but it is no longer the whole list,
so it is reworded and a third job appears beside it.

### Test baselines measured for this plan

Measured 2026-08-24 at `f34567c`, Docker available on the machine:

| Command | Result |
| --- | --- |
| `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` | 371 total, 371 passed, 0 failed, 0 skipped |
| `SEMIPLOT_REQUIRE_DB=1 dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj` | 360 total, 360 passed, 0 failed, 0 skipped |

## Development Approach

- **testing approach**: Regular — code first, then tests, matching every other plan in this
  repository.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
  - tests are not optional - they are a required part of the checklist
  - write unit tests for new functions/methods
  - write unit tests for modified functions/methods
  - add new test cases for new code paths
  - update existing test cases if behavior changes
  - tests cover both success and error scenarios
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- **Every task leaves the tree building and all suites green.** The stub deletion removes 37 test
  cases from `SemiPlot.Tests` and is Task 1 precisely so no later task carries a dangling reference:
  nothing added afterwards has to be implemented twice, once for the archive provider and once for a
  stub about to be deleted.

## Testing Strategy

- **unit tests**: required for every task (see Development Approach above)
- **provider unit tests** live in `SemiPlot.Tests.Data`, which holds the `InternalsVisibleTo` for
  `SemiPlot.DataSource.Postgres`
  (`SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj:17`), and use raw
  `Assert.` exclusively
- **UI and Core-model tests** live in `SemiPlot.Tests` and use AwesomeAssertions exclusively
- **end-to-end journeys** live in `SemiPlot.Tests.Journeys` and use AwesomeAssertions, matching the
  UI project they drive. That project carries no `xunit.runner.json`: its skips are stated absences,
  and `SEMIPLOT_REQUIRE_DB` is what turns one into a failure
- **`SemiPlot.Tests` keeps `failSkips`.** No test this slice adds to it may skip on any machine
- **gated integration tests** carry `[Trait("Category", "Integration")]`, join the archive-database
  collection of their own assembly and open with `RequireAvailable()`
- **a gated class that appends rows clones per test** through `IAsyncLifetime`, and never takes
  `SeededArchive`, whose contract is that the class leaves the database as it found it
- **no test waits on a timeout.** Every synchronisation point is an awaited emission or an awaited
  write, never a delay, a deadline or a poll loop. The reason is stated in Technical Details
- there are no Playwright/Cypress-style e2e tests in this repository; the two journeys are its
  equivalent

## Acceptance Evidence

Baseline measured 2026-08-24 at `f34567c` on a machine with Docker available: `SemiPlot.Tests` 371
passed / 0 failed / 0 skipped; `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` 360 passed / 0
failed / 0 skipped. The totals move with this slice, so the assertions below are on failures, skips
and named tests rather than on a total.

Run from the repository root. Each numbered item records what it returns at `f34567c`, so the
difference is read rather than assumed.

1. **The solution builds clean.**
   ```powershell
   dotnet build SemiPlot.slnx -c Release
   ```
   Must report `0 Warning(s)` and `0 Error(s)`.

2. **Formatting is unchanged.**
   ```powershell
   dotnet format SemiPlot.slnx --verify-no-changes
   ```
   Must exit 0.

3. **The data suite passes with no skip.**
   ```powershell
   $env:SEMIPLOT_REQUIRE_DB="1"
   dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release
   ```
   Must report `failed 0` and `skipped 0`. A container runtime that does not answer fails this run
   rather than skipping it, which is the whole point of the variable.

4. **The UI suite passes with no skip, on any machine.**
   ```powershell
   Remove-Item Env:\SEMIPLOT_REQUIRE_DB -ErrorAction SilentlyContinue
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
   ```
   Must report `failed 0` and `skipped 0` with no container runtime running. `failSkips` is still
   set there, so a skip is reported as a failure.

5. **Both journeys actually ran.**
   ```powershell
   $env:SEMIPLOT_REQUIRE_DB="1"
   dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj -c Release `
     --logger "console;verbosity=normal"
   ```
   Must list `BreakRenderArchiveJourneyTests` and `LiveEdgeArchiveJourneyTests` as passed and report
   `failed 0`, `skipped 0`. A run reporting 0 total tests is a failure of this step.

6. **The journeys skip cleanly with no runtime.** With `SEMIPLOT_REQUIRE_DB` unset and the container
   runtime stopped:
   ```powershell
   Remove-Item Env:\SEMIPLOT_REQUIRE_DB -ErrorAction SilentlyContinue
   dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj -c Release
   ```
   Must report `failed 0` with both journeys skipped and a stated reason naming the absent runtime.

   `dotnet test SemiPlot.slnx` raises two containers — one for `SemiPlot.Tests.Data` and one for
   `SemiPlot.Tests.Journeys` — because each xunit v3 project is its own executable. Run the projects
   separately when that matters.

7. **The stub is gone.**
   ```powershell
   git ls-files | Select-String -Pattern "SemiPlot.DataSource.Stub" | Measure-Object -Line
   ```
   Must report `Lines : 0`. At `f34567c` it reports `Lines : 7`.
   ```powershell
   git grep -n -E "use-stub|UseStub|RandomStubDataProvider|DataSource\.Stub" `
     -- ":!docs/plans/completed" ":!docs/plans/roadmaps"
   ```
   Must return nothing. At `f34567c` it returns 63 lines. Completed plans and roadmaps are excluded
   because they are the record of what was done and keep the old names on purpose. `git grep` rather
   than `Select-String` over a glob: PowerShell rejects a second `-Path` argument outright, and
   `SemiPlot\**\*.cs` matches one directory level rather than recursing — at `f34567c` that glob
   finds 5 of the 63.

8. **The error vocabulary is eleven and all of it maps.**
   ```powershell
   Select-String -Pattern "HaveCount\(11\)" `
     -Path SemiPlot\SemiPlot.Tests\UI\Startup\StartupFailureMapperTests.cs
   ```
   Must return exactly one line. At `f34567c` the file reads `_errorTypes.Should().HaveCount(8);`, so
   it returns nothing.
   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release `
     --filter "FullyQualifiedName~StartupFailureMapperTests" --logger "console;verbosity=normal"
   ```
   Must pass with `failed 0`.

9. **The poll and the baseline reach their rows through an index.**
   ```powershell
   Select-String `
     -Pattern "ThePollPlanReachesItsRowsThroughAnIndex|TheBaselinePlanReachesEachBoundByAnIndexEdge" `
     -Path SemiPlot\SemiPlot.Tests.Data\Integration\ExplainPlanTests.cs
   ```
   Must return exactly two lines. At `f34567c` it returns nothing.
   ```powershell
   $env:SEMIPLOT_REQUIRE_DB="1"
   dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj -c Release `
     --filter "FullyQualifiedName~ExplainPlanTests" --logger "console;verbosity=normal"
   ```
   Must pass with `failed 0` and `skipped 0`, listing both added cases.

10. **The demo writer moves the live edge, read from the server without a screen.** Raise the bench
    exactly as `docs/architecture/bench.md:187-200` does, then:
    ```powershell
    $writerConnection =
      "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>"
    dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
      --connection $writerConnection --pens 8 --seed 1 --follow 1
    ```
    The process must print one line per tick naming the rows appended and must not exit. In a second
    shell, twice, ten seconds apart:
    ```powershell
    docker exec semiplot-bench psql --username postgres --dbname semiplot_app `
      --tuples-only --no-align --command "SELECT max(t) FROM public.trends WHERE l = 0;"
    ```
    The first reading must already be near the machine's local wall clock rather than near the
    seeded `2026-08-01`, which is what proves the writer starts at "now" instead of at the archive's
    own maximum. The second must be later than the first by roughly the elapsed wall-clock time.
    ```powershell
    docker exec semiplot-bench psql --username postgres --dbname semiplot_app `
      --tuples-only --no-align --command `
      "SELECT count(*) FROM pg_inherits WHERE inhparent = to_regclass('public.trends');"
    ```
    Must report the seeded day's partitions plus `tpdefault` plus at most one more — the day the
    follow run is writing into. Stop the writer with Ctrl+C; it must exit 0 and leave the archive
    readable — a third reading of `max(t)` must succeed and must not have moved further.

11. **The application follows that edge.** With the writer from step 10 still running and the
    connection file pointing at `semiplot_app`:
    ```powershell
    dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- `
      --config-dir <dir> --logging-level information
    ```
    The chart's curve must advance to the right on its own with the sticky toolbar state on, and the
    log at the configured path must carry no `[ERR]` or `[FTL]` line. This is the one item on this
    list that needs a screen; everything above it does not.

## Progress Tracking
- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

Five pieces, in the order they depend on each other.

**The stub goes first.** Deleting `SemiPlot.DataSource.Stub` before anything else means the
interface change later in the slice has one fewer implementation to carry, and the 37 removed test
cases leave the tree at a known-green state that every later task builds on.

**The poll is an awaitable engine with a thin Rx wrapper.** `RealtimePoll` is an internal sealed
type beside the provider holding the subscription's state — the baseline, `lastSeen`, the
consecutive failure count and the raised-fault flag — and exposing one awaitable `ReadOnceAsync`.
Every rule the roadmap states is a property of that method and is asserted by awaiting it, with no
scheduler in the test at all. `PostgresDataProvider.Subscribe` wraps it in `Observable.Create` over
the injected data `IScheduler`, so the cadence is the operator's `poll_interval_ms` and disposal
cancels the loop.

**The fresh tail is a second bind of the statement already there.** After the coarse read,
`QueryHistoryAsync` issues `SparseHistoryWindow` a second time with `@layer` bound to `0` over the
tail span, merges the two row lists per pen, and lets `HistoryRowFold` drop everything at or before
each pen's own seam. No new statement, no new pin, and the existing `EXPLAIN` guard already covers
the shape.

**The connection state travels on the seam, not around it.** `IDataProvider` gains one member,
`IObservable<ArchiveConnectionState> ConnectionFaults`; `TrendCoordinator` republishes it on the UI
scheduler; `MainWindowViewModel` turns it into a banner row built exactly like the existing
empty-catalogue banner (`SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:21-27`,
`SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml:55-65`).

**The bench appends and follows.** Appending is a parameter of `ArchiveWriter`'s existing write path,
`PartitionScript.CreateStatement` gains `IF NOT EXISTS`, and `--follow <seconds>` drives it from a
small tick generator behind its own options type. Both journeys append from the test body, so no
second process exists to race with.

### Key design decisions

**Poll shape.** `Subscribe(penIds)` returns a cold observable, one poll per subscription, on the
`IScheduler` that `AddPostgresData` already registers
(`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:21`). The cadence
is `PostgresConnectionSettings.PollInterval`
(`SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionSettings.cs:19`), which
`PostgresConnectionLoader` already fills from the required `poll_interval_ms` field
(`.../Configuration/PostgresConnectionLoader.cs:26`, `:91`, `:203`) and which nothing has read until
now.

The query is the statement `docs/architecture/data-integration.md:227-232` already documents, added
to `ArchiveStatements` verbatim:

```sql
SELECT id, t, v, q
FROM trends
WHERE id = ANY(@ids) AND l = 0 AND t > @lastSeen
ORDER BY t;
```

The variable list is in every query because a time-only predicate cannot use `PRIMARY KEY (id, l,
t)`, whose leading column is `id`, and degenerates into a sequential scan of the current day's
partition (`docs/architecture/scada-archive.md:261-263`). `@ids` binds an array, as
`PostgresDataProvider.BindWindow` already does for the windowed read
(`PostgresDataProvider.cs:200-203`). This shape is a bounded range per identifier on the primary key
and keeps its index plan through `id = ANY(...)`.

**"Newer than the last seen" is one field on the subscription.** `RealtimePoll` holds `lastSeen` as
a naive archive-local `DateTime`, the same side of the boundary the statement binds on, and advances
it to the maximum `t` the tick returned — never to the wall clock, and never backwards. The samples
it emits are converted to UTC through `ArchiveTimeConverter` before leaving, which is where every
other read converts (`PostgresDataProvider.cs:293-294`, `HistoryRowFold.cs:63`).

**A tick reads `v` through `IsDBNull` and drops a null row.** `Sample.Value` is non-nullable, so a
null cannot be emitted; `lastSeen` still advances past the row, and the tick logs at debug how many
it dropped. No null has ever been observed in this column (`scada-archive.md:79`, `:190`), so the
guard costs nothing in practice. What it buys is that an unexpected null is a dropped sample rather
than an exception the tick's own catch would count as a connection failure — three of which raise a
lost-connection banner over a healthy archive.

**A `q = 32` row is emitted as an ordinary sample, and the poll opens no gap.** The break's gap is
the history path's reconstruction: `HistoryRowFold` appends a synthetic null one tick after the
marked row (`HistoryRowFold.cs:81-85`) and `MinMaxDecimator` turns that into the NaN column. The
realtime seam has no equivalent, because `Sample` carries no null. A break that opens at the live
edge therefore shows as a line held at the last archived value until the next history read redraws
the window, and a sticky live-edge advance does not re-query
(`ChartNavigationController.cs:158-162`, `RequiresHistoryRequery: false`). Closing that needs a null
channel on `Sample` and a decision about `CurrentValue`, which changes the Core realtime vocabulary
and belongs to a slice of its own. Task 16 records it in the backlog.

**The first tick establishes the baseline and reports Connected.** Nothing has been seen, so there is
no `@lastSeen` to bind. The alternatives are both wrong: binding `null` returns no rows and leaves
the subscription permanently blind, and emitting every row since the archive began would dump the
whole archive into the chart on the first tick. So the first tick issues a baseline read instead,
sets `lastSeen` to its answer, emits no sample — **and returns `ArchiveConnectionState.Connected`**.

That state is the subscription's only observable armed point, and both the disposal proof and the
live-edge journey stand on it. Without it a subscription can never emit a row that already existed
when it subscribed, so a test that subscribes and then appends is racing the baseline read: if the
append commits before the baseline `SELECT` runs, the baseline swallows the row and no batch ever
arrives. No test here may use a timeout, so the losing side of that race is an infinite await in an
xunit v3 executable, which locks the next build with MSB3027/MSB3021. Every test therefore sequences
subscribe → await `Connected` → append → await batch.

An archive with no rows for the subscribed pens returns `NULL`; `lastSeen` then stays unset and the
next tick repeats the baseline read, which costs one index-edge probe per pen and is the correct
behaviour for an archive nothing has written to yet. That tick still reports `Connected`, because the
server answered. The baseline is read from the archive rather than taken from the local wall clock,
because the archive stores the SCADA host's naive local time and a clock difference between the two
machines would silently drop or repeat the first seconds of realtime.

**The baseline statement takes the extent's shape, not `max(t)` over `id = ANY(...)`.**
`SELECT max(t) FROM trends WHERE id = ANY(@ids) AND l = 0` does not get PostgreSQL's min/max
index-edge transform through an array membership test, and `ExplainPlanTests` holds an index-edge
assertion (`_indexEdgeReachedRows`, `ExplainPlanTests.cs:55-59`) that such a plan fails.
`ArchiveStatements.ArchiveExtent` (`ArchiveStatements.cs:31-38`) already solved this with a per-pen
`LATERAL` scalar subquery, and its literal in `ArchiveStatementTextTests.cs:39-49` records why:
"each one is an index probe on `PRIMARY KEY (id, l, t)` per pen, so the bounds come from two descents
per pen rather than from a scan of `trends`." `RealtimeBaseline` is that shape over the requested
identifiers instead of over `semiplot_tags`:

```sql
SELECT max(hi) AS last
FROM (SELECT DISTINCT unnest(@ids) AS id) requested
CROSS JOIN LATERAL (
    SELECT (SELECT max(t) FROM trends WHERE id = requested.id AND l = 0) AS hi
) bounds;
```

`unnest(@ids)` is the same de-duplicating source `SparseHistoryWindow`'s seed branch uses
(`ArchiveStatements.cs:82`), so a caller repeating an identifier costs one probe rather than two.

**Error handling.** Every failure inside `ReadOnceAsync` is caught, mapped through the same
`ArchiveExceptionMapper` the reads use (`ArchiveExceptionMapper.cs:54-64`), logged, and returned as
part of the tick's result. The Rx wrapper never calls `OnError` and never completes on its own, so
nothing crosses to the UI thread: `TrendChartViewModel` subscribes with an `onNext` handler alone
(`TrendChartViewModel.cs:91-92`) and an `OnError` there would go unhandled on the UI scheduler. A
failing tick emits no samples, so `TrendCoordinator`'s `Where(window => window.Count > 0)`
(`TrendCoordinator.cs:92`) drops it and the chart keeps the data it has.
`OperationCanceledException` from the loop's own cancellation is caught before the mapper, which
rethrows it by design (`ArchiveExceptionMapper.cs:58-61`), and ends the loop quietly.

**"Repeated" is three consecutive failures, and the flag is explicit.** A counter increments on each
failed tick and resets to zero on the first successful one. On the tick that takes it to three, and
not again until a success has reset it, the provider pushes an `ArchiveConnectionState` carrying
`ArchiveConnectionLostError`; the first success after that pushes `ArchiveConnectionState.Connected`.
Three rather than one, because Npgsql opens a fresh physical connection after a reset and a single
dropped packet or recycled pool connection produces exactly one failed tick — a banner raised on one
failure would flap on a healthy archive. Three rather than ten, because the count multiplies the
operator's own `poll_interval_ms`: at the 1 s cadence a bench uses, three failures is a banner within
about three seconds, and at a 5 s cadence within fifteen — both inside the time an operator would
otherwise spend wondering whether the curve had gone flat or gone dead.

`DistinctUntilChanged` on the stream cannot replace that flag, in either direction.
`ArchiveConnectionState` is a record over `IError?`, and `Error` carries reference equality, so two
lost-connection states from two subscriptions compare unequal and would both pass a distinct filter.
In the other direction the filter would swallow what the tests stand on: `Connected` is reported by
every subscription's first successful tick, so a second subscription's armed signal is equal to the
first's and a distinct filter would drop it.

**Disposal.** `Subscribe` builds the observable with `Observable.Create`, and the disposable it
returns is the one from `IScheduler.ScheduleAsync`; disposing it cancels the token the loop's query
and its sleep both run under, so no further query is issued after the disposal returns.
`TrendCoordinator` disposes its own subscription (`TrendCoordinator.cs:54`) and `RefCount`
(`TrendCoordinator.cs:98`) disposes the upstream when the last subscriber goes, so the chart closing
stops the poll.

What a test asserts to prove it, without a timeout, on a clone the test class creates for itself:

1. Subscribe A, collecting its batches. Await the first `Connected` — A's baseline tick has run.
2. Append one row per pen. Await A's first batch — A's loop is live and delivering.
3. Dispose A. Record how many batches A collected.
4. Subscribe B. Await the next `Connected` — B's baseline tick has run.
5. Append a second row per pen. Await B's first batch. B's emission proves at least one poll interval
   elapsed after A was disposed.
6. Assert A's batch count did not move across steps 4 and 5. A leaked loop would have delivered the
   second row to A.

**Why the journeys use an awaited emission rather than a virtual scheduler.** A poll whose tick
awaits an Npgsql round trip cannot be driven deterministically by `TestScheduler`: `AdvanceBy`
starts the query, the await completes on a thread-pool thread, and the continuation is only then
posted back to the scheduler, so the test has no way to know when the next `AdvanceBy` would be
observed. Every synchronisation point in the journeys is therefore an `await` on something that
completes exactly when the thing being asserted has happened — a write that has landed, a
`Connected` that says the subscription is armed, or a batch that has been emitted. No `Task.Delay`,
no `WaitAsync`, no deadline, no retry loop. `RealtimePoll`'s own rules are asserted by awaiting
`ReadOnceAsync` directly, where no scheduler is involved at all.

**The fresh tail's bound.** After the coarse read, each pen's seam is its last returned timestamp, or
the window start when it returned none. Let `earliestSeam` be the smallest of those,
`spacing = layer.ToPointSpacing()` and `onePeriod = spacing * 4` — the period the layer's spacing is
a quarter of (`docs/architecture/data-integration.md:253-261`).

- The tail is never read at `AggregationLayer.Raw`, where there is nothing coarser to be short of.
- The read is skipped entirely when `to - earliestSeam` does not exceed `spacing`, so a coarse layer
  fresh within one of its own points costs no round trip.
- Otherwise one read of `SparseHistoryWindow` at `@layer = 0` over `[tailStart, to)`, where
  `tailStart = max(earliestSeam, to - onePeriod)`. The clamp is a cost bound: it caps a single
  history query at one period of raw rows however far behind a coarse layer has fallen.
- **A pen whose own seam falls before `tailStart` contributes no tail rows.** Its coarse rows stop
  before the tail's own start, so appending tail rows would leave a range no row covers, and a range
  with no null in it draws as one straight interpolated segment rather than as a gap. Such a pen
  keeps the short right edge it already had.
- Every remaining pen's tail rows are appended after its coarse rows, and `HistoryRowFold.Fold` drops
  those at or before that pen's own seam through its ascending check (`HistoryRowFold.cs:71`).

> ⚠️ **`earliestSeam` was corrected by the review pass (`7d1e081`).** The global minimum above is
> wrong: one pen the coarse read answered nothing for seams at the window start, which is at or
> before the clamp, so it pulled `tailStart` down to `to - onePeriod` — a full layer period of raw
> rows, 24 h of them at `Day` — on every coarse query, for rows the per-pen exclusion then threw
> away. `FreshTail.Start` now takes the minimum over **only the seams that reach the clamp**
> (`seam >= windowEnd - spacing * 4`), which are exactly the pens `Merge` keeps a tail row for. No
> such candidate means no tail read at all. The clamp still bounds the read by construction, and the
> skip rule is unchanged.

## Technical Details

### New and changed types

| Type | Where | What it is |
| --- | --- | --- |
| `ArchiveConnectionState` | `SemiPlot/SemiPlot.Core/Data/ArchiveConnectionState.cs` | `sealed record ArchiveConnectionState(IError? Fault)` with a static `Connected` and `IsConnected => Fault is null`. Not an error type; it carries one |
| `ArchiveConnectionLostError` | `SemiPlot/SemiPlot.Core/Data/Errors/` | `sealed class (string host, int port, string database, int consecutiveFailures)` — four parameters over `string`, `int`, `string`, `int`, all kinds `SampleValue` (`StartupFailureMapperTests.cs:251-275`) supplies. Message built in the base constructor like `ArchiveQueryTimedOutError` (`.../ArchiveQueryTimedOutError.cs:22-31`). ⚠️ **the fourth parameter shipped as `int failureThreshold`, not a running count** (`999074a`) — see the note below the table |
| `ArchiveShapeUnexpectedError` | `SemiPlot/SemiPlot.Core/Data/Errors/` | `sealed class (string host, int port, string database, string detail)` — `detail` is the server's own message text, not a collection, because `SampleValue` supports no collection type |
| `ArchiveDefaultPartitionNotEmptyError` | `SemiPlot/SemiPlot.Core/Data/Errors/` | `sealed class (string host, int port, string database, string partition)` |
| `RealtimePoll` | `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs` | internal sealed; one instance per subscription; `Task<RealtimeTick> ReadOnceAsync(CancellationToken)`, plus internal static `BindPoll` and `BindBaseline` so a binder pin and an `EXPLAIN` test bind through the shipped path |
| `RealtimeTick` | same file | internal readonly record struct `(IReadOnlyList<Sample> Samples, ArchiveConnectionState? StateChange)` |
| `ArchiveHealthReader` | `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveHealthReader.cs` | public sealed; registered by `AddPostgresData`; `Task<IReadOnlyList<IError>> ReadAsync()` returning zero or one warning |
| `OptionTokens` | `SemiPlot/SemiPlot.Tools.ArchiveSeeder/OptionTokens.cs` | the `--name value` tokeniser lifted out of `SeederOptions.Parse` (`:68-110`) so both option types share one, keeping the unknown-option and missing-value rules (`:83-99`) in one place |
| `FollowOptions` | `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowOptions.cs` | `sealed record (string ConnectionString, TimeSpan Interval, int PenCount, long Seed, double ChangeSeconds)` with its own `Parse` and `Usage`. Separate from `SeederOptions` so `End` stays required and non-nullable there, the ordered validation chain is untouched, and the bench template's digest cannot move |
| `LiveTailGenerator` | `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs` | rows for one follow tick over `SyntheticValueWalk`, in the archive's pre-anchor-plus-change shape |
| `PenRealtimeValues` | `SemiPlot/SemiPlot.Core/Trends/PenRealtimeValues.cs` | ⚠️ **changed by the review pass (`6e1056c`), not by a task.** `(long PenId, IReadOnlyList<double?> Values)` — a column over the batch's shared timestamp list — became `(long PenId, IReadOnlyList<DateTime> TimestampsUtc, IReadOnlyList<double> Values)`, the pen's own instants and nothing else. `RealtimeBatch.Timestamps` stays, as the ascending union the live edge advances from |

⚠️ **`ArchiveConnectionLostError` carries the threshold, not a running count** (`999074a`).
Reporting the running total means re-raising a state on every failed tick, a banner rewrite per poll
interval — exactly what the raise-once flag exists to prevent, and what
`RealtimePollTests.AFourthAndFifthConsecutiveFailureRaiseNothingFurther` pins. The property is
`FailureThreshold`, fixed at the moment the fault was raised, and the message is past tense:
*"…stopped answering after 3 consecutive failed reads."*

### New statements in `ArchiveStatements`

All three are operational, so all three take a plain literal in `ArchiveStatementTextTests` — the
file states the rule at `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs:12-15`.

| Constant | Text | Parameters |
| --- | --- | --- |
| `RealtimePoll` | the poll above, verbatim from `docs/architecture/data-integration.md:227-232` | `@ids`, `@lastSeen` |
| `RealtimeBaseline` | the lateral `max(t)` above | `@ids` |
| `DefaultPartitionOccupancy` | `SELECT EXISTS (SELECT 1 FROM ONLY public.tpdefault);` | none |

There is no column-shape statement. An unexpected `trends` shape is reached from a real read as
`42703` and named by `ArchiveShapeUnexpectedError`; a reader comparing `information_schema.columns`
against an expected shape held here is the second transcription the roadmap's scope guard forbids
(`docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md:665-666`).

The binder pins follow the existing shape at `ArchiveStatementTextTests.cs:94-120`, which compares
the parameter names the binder produces against the `@`-tokens in the statement itself.

### The provider's constructor

`PostgresDataProvider`'s internal constructor (`PostgresDataProvider.cs:35-53`) gains two
parameters, the `IScheduler` the poll runs on and the `PostgresConnectionSettings` the cadence comes
from. The DI factory follows (`PostgresDataServiceCollectionExtensions.cs:31-36`), as does the
direct construction in `PostgresCompositionTests.NewProvider`
(`SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs:28-36`), whose settings come from
`ConnectionSettingsFactory.Create` (`:24`). Both new dependencies are already registered: the
scheduler at `PostgresDataServiceCollectionExtensions.cs:21` and the settings at `:22`.

### Processing flow of one history query with a tail

1. `QueryHistoryAsync` validates and binds as today (`PostgresDataProvider.cs:112-140`).
2. It reads the coarse rows into `List<HistoryRowFold.Row>` (`:144-149`).
3. It computes each pen's seam and the bound described above. When the layer is `Raw`, or the deficit
   does not exceed `layer.ToPointSpacing()`, it stops here.
4. Otherwise it binds the same statement a second time on the same connection with `@layer = 0`,
   `@from = tailStart`, `@to = toUtc`, and reads the tail rows.
5. It drops the tail rows of every pen whose seam falls before `tailStart`, then merges the two lists
   into one ordered by pen identifier, each pen's coarse rows followed by its tail rows — the
   ordering `HistoryRowFold.Fold` requires (`HistoryRowFold.cs:24-27`).
6. `Fold` drops every remaining tail row at or before that pen's own seam through its ascending check
   (`HistoryRowFold.cs:71`), so the seam is per pen without a per-pen query bound, and decimates as
   before (`HistoryRowFold.cs:91`).

### Processing flow of one poll tick

1. If `lastSeen` is unset, issue `RealtimeBaseline`, set `lastSeen` from a non-null answer, reset the
   failure counter, and return an empty sample list with `ArchiveConnectionState.Connected`.
2. Otherwise issue `RealtimePoll` with `@lastSeen` and read the rows. For each row: read `v` through
   `IsDBNull` and skip the row when it is null, otherwise convert `t` to UTC and build a `Sample`
   (`SemiPlot/SemiPlot.Core/Trends/Sample.cs:3`). `q` is read for the debug log and takes no branch.
3. Advance `lastSeen` to the maximum naive `t` read, null rows included. Rows arrive ordered by `t`,
   so that is the last one.
4. Reset the failure counter, and return `ArchiveConnectionState.Connected` when this is the
   subscription's first successful tick or the first after a raised fault, otherwise no state change.
5. On an exception: map it, log it, increment the counter, and return an empty sample list plus — on
   the third consecutive failure only, and not again until a success — a state carrying
   `ArchiveConnectionLostError`.

⚠️ **Added by the review pass (`263b58d`).** Steps 1 and 2 build their commands through
`RealtimePoll.CreateTickCommand`, which sets `CommandTimeout` to `TickCommandTimeoutSeconds = 10`.
Without it a tick inherited `ArchiveDataSource`'s five-minute client backstop, which is sized for a
wide history read. A server that accepts connections and then stops answering would hold each tick
for minutes and reach the three-failure threshold only after fifteen of them — a frozen chart and no
banner in between. Ten seconds is the bound `ArchiveHealthReader` already carries and ten times the
1 s cadence a bench runs at, so the same stall raises the banner inside half a minute. The connect
attempt keeps its own `PostgresConnectionSettings.ConnectTimeoutSeconds`.

### The archive-status banner has two sources and one writer each

`MainWindowViewModel` gains two independent properties rather than one shared string, because a
single message written by both the connection-state stream and the startup health warnings has no
defined precedence, and `CLAUDE.md` names exactly that pattern as forbidden for `IsSticky`.

| Property | Its only writer | Lifetime |
| --- | --- | --- |
| `ArchiveHealthMessage` (string?) | `App.InitializeServices`, once, from the warnings `StartupProbe` carried out | set at startup, never changed |
| `ArchiveConnectionMessage` (string?) | an OAPH over the coordinator's republished `ConnectionFaults` | changes with the stream |

`MainWindow.axaml` carries one `Border`/`TextBlock` row per property, each with its own `IsVisible`,
modelled on the empty-catalogue row at `:55-65`. Both messages can therefore be on screen at once,
which is the honest rendering of two independent facts.

⚠️ **Both rows render `StartupFailureMapper.Describe(fault)`, not `IError.Message`** (`b903229`).
The first implementation put the error's own message on screen, which states the fault and stops
there: the operator reads that the archive stopped answering and is told nothing to do about it,
while the same error already carries a remedy that only the startup path was showing.
`StartupFailureMapper.Describe` is the mapper's detail plus its remedy in one line, and
`MainWindowViewModel.ObserveArchiveConnection` and `App.DescribeHealthWarnings` both route through
it. Each row still has exactly one writer.

### Seeder `--follow`

`--follow <seconds>` takes the cadence as its value, which fits the existing tokeniser without a
valueless branch. `Program.Main` routes on the presence of `--follow` in the raw argument list: with
it, `FollowOptions.Parse` runs; without it, `SeederOptions.Parse` runs on exactly the path it does
today. `FollowOptions` accepts `--connection`, `--follow`, `--pens`, `--seed` and `--change-seconds`,
and rejects `--end`, `--days`, `--break-count` and `--admin-connection` with a message stating that a
follow run seeds nothing and fills no catalogue. It requires a finite, positive interval.

`SeederOptions` is unchanged: `--end` stays unconditionally required there (`:119-123`), the ordered
validation chain keeps `ValidateSpan` first (`:156-172`), and `ArchiveTemplate.Slice`
(`ArchiveTemplate.cs:25-33`) and `ComputeName` (`:120-140`) keep constructing and digesting the same
eight members, so the bench template's database name cannot move.

**The follow writer writes the machine's local wall clock.** The archive column is
`timestamp(3) without time zone` and holds the SCADA host's naive local time
(`docs/architecture/scada-archive.md:32`, `:79`), so a tick's bounds come from `DateTime.Now` with
its `Kind` stripped to `Unspecified`. `DateTime.UtcNow` would place the demo's live edge one zone
offset away from where the application, converting through `source_time_zone`, looks for it.

> ASSUMPTION: on the demo bench the writer and the viewer run on the same machine, so
> `archive-connection.yaml`'s `source_time_zone` names that machine's zone and the two agree. A
> writer on a different host in a different zone would need the zone stated on the command line,
> which this slice does not add.

`lastEmitted` starts at the wall clock the loop starts at, never at the archive's `max(t)`. Each tick
generates raw rows for `[lastEmitted, now)`, calls the appending write path, and sets
`lastEmitted = now`, so the first tick writes one interval of rows and touches one day partition.
Starting from `max(t)` would, against the bench's own recipe seeded to `--end 2026-08-01T00:00:00`
(`docs/architecture/bench.md:199`), generate weeks of rows and a partition per day on the very first
tick.

**Follow appends layer `0` only.** The coarse layers stay as the seed left them, because thinning a
period that is still open would write rows the next tick would have to write again, and
`PRIMARY KEY (id, l, t)` refuses the second one. The live edge the poll follows is `l = 0`, so the
demo loses nothing by it; a window at a coarse layer sees the appended rows through the fresh tail.

**A follow run over an archive seeded into the future would collide** on `(id, l, t)` and fail the
`COPY`. Nothing guards it, and nothing needs to: `docs/architecture/bench.md:207-209` already
requires the seed's `--end` to be well in the past, for an unrelated reason.

### Scope guard, honoured throughout

No coordinator batching changes: `TrendCoordinator.BuildRealtimeBatches`
(`TrendCoordinator.cs:85-99`) keeps its `Buffer` window, its filter and its `Publish().RefCount()`
exactly as they are, and gains only the republished connection-state stream.

> ⚠️ **Narrowed by the review pass (`6e1056c`).** The guard holds for the batching *stream* —
> `_batchWindow`, `Buffer`, the empty-window filter and `Publish().RefCount()` are byte-identical at
> HEAD. It does **not** hold for the fold: `BuildColumn` is gone and `BuildRealtimeBatch` now builds
> one `PenRealtimeValues` per pen from that pen's own samples. The guard was written to stop a
> re-architecture of the batching window; the fold carried a defect the poll made reachable, and
> fixing it is inside the slice's own subject. See the note under **New and changed types**.

No bucketing:
`SparseHistoryWindow` is unchanged and no server-side reduction is added. No change to `Sample`, so
the realtime seam gains no null channel. No shape assertion and no second transcription of the
vendor DDL. No change to `SeederOptions`' shape, so the bench template's digest holds. No compose
file and no second orchestration mechanism: the developer environment stays the container
`dotnet test` raises plus the hand recipes in `docs/architecture/bench.md`, with `--follow` added as
one more command. No error type beyond the three named.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): everything achievable in this repository — provider
  code, Core types, chart-model changes, seeder changes, tests, CI, and documentation.
- **Post-Completion** (no checkboxes): the demo-stand judgments the roadmap names as acceptance
  items for the operator, and the manual application-bench run.

## Implementation Steps

### Task 1: Retire `SemiPlot.DataSource.Stub`

**Files:**
- Delete: `SemiPlot/SemiPlot.DataSource.Stub/` (whole project: `RandomStubDataProvider.cs`,
  `DataServiceCollectionExtensions.cs`, `SyntheticPen.cs`, `SyntheticPenCatalog.cs`,
  `SyntheticQuality.cs`, `SyntheticValueWalk.cs`, `SemiPlot.DataSource.Stub.csproj`)
- Delete: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`
- Delete: `SemiPlot/SemiPlot.Tests/Core/Data/SyntheticPenCatalogTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/SyntheticPenCatalogColorTests.cs`
- Modify: `SemiPlot.slnx`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`
- Modify: `SemiPlot/SemiPlot.UI/StartupOptions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/StartupOptionsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/HistoryArgumentGuardTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`

- [x] remove the `UseStub` member, `DefaultUseStub` and the `--use-stub` arm from
      `StartupOptions.cs:9`, `:20`, `:45-47`, and drop `useStub` from `Parse` (`:22-56`)
- [x] remove the `using` (`StartupProbe.cs:8`), the stub branch (`:56-64`) and
      `BuildStubServiceProvider` (`:87-93`), leaving `Run` with one path: load the connection file,
      then `Read(BuildArchiveServiceProvider(...))`
- [x] delete the project directory and its entries in `SemiPlot.slnx:4`,
      `SemiPlot.Tests.csproj:29`, and `SemiPlot.UI.csproj:33-34` (the comment) and `:36` (the
      reference) — **`:35` is the Postgres reference and must stay**
- [x] reword the three production comments that explain a rule by naming the other implementation:
      `PostgresDataProvider.cs:124-127` and `:215-218`, and
      `PostgresDataServiceCollectionExtensions.cs:13`, so each states this provider's own contract
- [x] rewrite `CompositionRootTests` over the archive container alone: drop the `using` (`:11`),
      delete `StubContainer_ResolvesTheStubProvider` (`:38-44`) and collapse the six `[Theory]` cases
      (`:46-108`) to `[Fact]`s using
      `StartupProbe.BuildArchiveServiceProvider(UnreachableSettings())`
- [x] delete `Parse_UseStub_IsValuelessSwitch` and `Parse_UseStubLast_IsHonoured`
      (`StartupOptionsTests.cs:70-85`) and drop the `UseStub` assertions at `:24`, `:103` and `:122`
- [x] delete `Run_OverTheStubContainer_CarriesPensAndExtent` (`StartupProbeTests.cs:193-205`) and
      reword `Run_WithNoConnectionFile_FailsInsteadOfFallingBackToTheStub` (`:207-222`) so its name
      and comment state the rule — a missing connection file ends startup — without naming a project
      that no longer exists
- [x] move `Build_EveryColorIsSixDigitHex` (`SyntheticPenCatalogTests.cs:56-62`) into
      `SemiPlot.Tests.Data` against the seeder's own `SyntheticPenCatalog`, converted to raw
      `Assert.` and carrying the three traits, with a comment stating why it survives: the seeder's
      catalogue is a verbatim copy whose colours reach `semiplot_tags` through
      `TagCatalogWriter.cs:61` and come back through `ArchiveStatements.PenCatalog`, and the golden
      digest is over `ArchiveRow` (`ArchiveRow.cs:3`), which carries no colour
- [x] correct the cross-implementation comments at `HistoryArgumentGuardTests.cs:94`, `:103`, `:106`
      and `PostgresCompositionTests.cs:162` to state the guard ordering as this provider's own
      contract
- [x] run both suites — `SemiPlot.Tests` must be 371 minus 37 cases, all passing, and
      `SemiPlot.Tests.Data` 360 plus 1

### Task 2: `RealtimePoll` — statements, engine and their pins

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/RealtimePollTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/RealtimePollReadTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ExplainPlanTests.cs`

- [x] add `RealtimePoll` to `ArchiveStatements`, character for character as
      `docs/architecture/data-integration.md:227-232` carries it, with a doc comment stating why the
      variable list is mandatory
- [x] add `RealtimeBaseline` in the lateral shape `ArchiveExtent` (`ArchiveStatements.cs:31-38`)
      uses, with a doc comment stating that `max(t)` under `id = ANY(...)` loses the index-edge
      transform, that each lateral subquery is one index probe on `PRIMARY KEY (id, l, t)` per pen,
      and that a `NULL` answer means the subscribed pens have no rows yet
- [x] implement `RealtimePoll` holding `lastSeen`, the consecutive failure count and the
      raised-fault flag, with internal static `BindPoll` and `BindBaseline` modelled on
      `PostgresDataProvider.BindWindow` (`:192-213`); `ReadOnceAsync` issues the baseline when
      `lastSeen` is unset and the poll otherwise, converting through `ArchiveTimeConverter` on the
      way out
- [x] read `v` through `IsDBNull` and drop a null row from the emitted samples while still advancing
      `lastSeen` past it, with a comment naming why: `Sample.Value` is non-nullable (`Sample.cs:3`),
      and a `GetDouble` on a null inside the tick would be counted as a connection failure
- [x] map every caught exception through `ArchiveExceptionMapper`, log it, and let cancellation out
      untouched ahead of the mapper, which rethrows `OperationCanceledException`
      (`ArchiveExceptionMapper.cs:58-61`)
- [x] return the armed signal from the subscription's first successful tick and from the first
      success after a raised fault, and the lost-connection signal on the third consecutive failure
      and not again until a success. `ArchiveConnectionState` lands in Task 4; until then
      `RealtimeTick` carries the two signals as a small enum, which Task 4 replaces with the state
- [x] add both literals and their equality assertions to `ArchiveStatementTextTests`, beside the
      three already there (`:33-37`, `:51-55`, `:87-91`), and a binder pin for each modelled on
      `TheWindowBinderNamesExactlyTheStatementsOwnParameters` (`:94-120`)
- [x] add `ThePollPlanReachesItsRowsThroughAnIndex` to `ExplainPlanTests`, using `_indexReachedRows`
      (`:65-66`) and `_sequentialScanOverRows` (`:48`), binding through `RealtimePoll.BindPoll`
- [x] add `TheBaselinePlanReachesEachBoundByAnIndexEdge`, using `_indexEdgeReachedRows` (`:59`) the
      way the extent case does (`:104-130`), binding through `RealtimePoll.BindBaseline`. Both open
      with `postgresContainerFixture.RequireAvailable()` and reuse `ExplainAsync` (`:245-269`) and
      `AnalyseAsync` (`:235-240`)
- [x] write unit tests against unreachable settings (`ConnectionSettingsFactory.Create`, the helper
      `PostgresCompositionTests` uses at `:24`) for: one failure emits no lost signal; three
      consecutive failures emit exactly one; a fourth and fifth emit none; no exception escapes
      `ReadOnceAsync`
- [x] write gated tests, on a clone this class creates per test through `IAsyncLifetime` rather than
      through `SeededArchive`, for: the first tick emits no sample and reports armed; a tick after an
      appended row emits exactly that row; a tick with nothing new emits nothing; a row written with
      a null `v` emits no sample, reports no failure and still advances `lastSeen`; a row written
      with `q = 32` emits exactly one sample; `lastSeen` never moves backwards across ticks
- [x] run `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped

### Task 3: `PostgresDataProvider.Subscribe` over the engine

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/PostgresCompositionTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/RealtimeSubscriptionTests.cs`

- [x] add the `IScheduler` and `PostgresConnectionSettings` parameters to the internal constructor
      (`PostgresDataProvider.cs:35-53`) and the DI factory
      (`PostgresDataServiceCollectionExtensions.cs:31-36`)
- [x] replace the body of `Subscribe` (`PostgresDataProvider.cs:55-60`) with `Observable.Create`
      over `IScheduler.ScheduleAsync`, keeping the null guard first, looping `ReadOnceAsync` then
      sleeping `settings.PollInterval`, pushing only non-empty sample lists, and never calling
      `OnError` or `OnCompleted`
- [x] follow the constructor change in `PostgresCompositionTests.NewProvider` (`:28-36`)
- [x] **replace `SubscribeCompletesImmediately` (`PostgresCompositionTests.cs:174-184`)**: it awaits
      `.ToArray()` on the sequence and would hang forever against a poll that never completes. The
      replacement asserts, with no database, that subscribing and immediately disposing issues
      nothing and returns
- [x] write a gated test proving disposal stops the poll, by the six-step construction in Solution
      Overview: subscribe → await armed → append → await batch → dispose → subscribe → await armed →
      append → await batch, then assert the disposed subscription's batch list did not grow. No
      timeout anywhere. The class appends, so it clones per test through `IAsyncLifetime`
- [x] write a gated test proving two independent subscriptions each keep their own `lastSeen`,
      sequenced the same way
- [x] run both suites under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped

### Task 4: `ArchiveConnectionState` on the `IDataProvider` seam

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/ArchiveConnectionState.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveConnectionLostError.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/RealtimePoll.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/FakeDataProvider.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/RealtimeSubscriptionTests.cs`

- [x] add `ArchiveConnectionState` and `ArchiveConnectionLostError`, the error's message built in
      the base constructor and its four parameters typed `string`, `int`, `string`, `int` — all
      kinds `SampleValue` (`StartupFailureMapperTests.cs:251-275`) supplies
- [x] add `IObservable<ArchiveConnectionState> ConnectionFaults { get; }` to `IDataProvider`
      (`IDataProvider.cs:7-22`) with a comment stating that it is hot, shared and never terminating,
      and that every subscription's first successful tick reports `Connected` on it — the only
      observable point at which a subscription is known to be armed
- [x] implement it in `PostgresDataProvider` over a subject the poll loop pushes `RealtimeTick`'s
      state change into, and in `FakeDataProvider` (`FakeDataProvider.cs:93-105`) over a subject a
      test can push into
- [x] republish it in `TrendCoordinator` on `_uiScheduler`, beside `RealtimeBatches`
      (`TrendCoordinator.cs:41`, `:85-99`), and dispose the subscription in `Dispose` (`:46-56`)
- [x] add the `ArchiveConnectionLostError` arm to `StartupFailureMapper.Map`
      (`StartupFailureMapper.cs:32-44`) with a title, a detail naming the host and the failure
      count, and a remedy
      - ⚠️ superseded by the review pass (`999074a`): the detail names the failure **threshold**,
        not a running count. The fault is raised once per outage and never re-raised, so a running
        total would be stale the tick after it was built. See **New and changed types**
- [x] raise `ErrorTypeEnumeration_CoversBothNamespaces` (`StartupFailureMapperTests.cs:43`) from 8
      to 9, correct its comment at `:41-42`, and add a per-type case for the new arm
- [x] rewrite the Task 3 gated tests to await `ArchiveConnectionState.Connected` in place of the
      interim signal, keeping the sequencing unchanged
- [x] write tests: the coordinator forwards a fault on the UI scheduler and stops after disposal; a
      fault does not disturb `RealtimeBatches`; a gated test that the provider reports `Connected`
      on each subscription's first successful tick, not only on the first subscription's
- [x] run all suites under `SEMIPLOT_REQUIRE_DB=1` — must pass

### Task 5: The archive-status banner

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests/UI/MainWindow/ArchiveStatusBannerTests.cs`

- [x] add `ArchiveConnectionMessage` (string?) and `HasArchiveConnectionMessage` to
      `MainWindowViewModel` beside `IsCatalogueEmpty` (`MainWindowViewModel.cs:21-27`), written only
      by the coordinator's stream
- [x] add `ArchiveHealthMessage` (string?) and `HasArchiveHealthMessage`, written only by
      `App.InitializeServices`. Task 7 fills it; here it is set by nothing and its row stays hidden
- [x] add one `Border`/`TextBlock` row per property to `MainWindow.axaml`, modelled on the
      empty-catalogue row at `:55-65`, each with its own `IsVisible`, and shift the status bar's
      `Grid.Row` (`:67-74`) accordingly
- [x] wire the coordinator's stream into the view model in `App.InitializeServices`, before
      `coordinator.Start()` (`App.axaml.cs:150`)
- [x] write tests: the view model raises and clears the connection banner as states arrive; the two
      messages are independent, and neither writer can clear the other's row
- [x] run `SemiPlot.Tests` — must pass with 0 skipped

### Task 6: `ArchiveShapeUnexpectedError` on the mapper's `42703` arm

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveShapeUnexpectedError.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveExceptionMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`

- [x] add `ArchiveShapeUnexpectedError(string host, int port, string database, string detail)` with
      its message built in the base constructor, and a class comment stating that no prober exists
      and none may be added: the state is reached from a real read, and the type is what names it
      rather than what detects it — a column-shape reader would be the second transcription of the
      vendor DDL the roadmap's scope guard forbids (`:665-666`)
- [x] add the `PostgresErrorCodes.UndefinedColumn` arm to the switch at
      `ArchiveExceptionMapper.cs:95-119`, carrying the server's own `MessageText` as `detail`
- [x] add the arm to `StartupFailureMapper.Map` (`StartupFailureMapper.cs:32-44`) with a remedy
      naming the provisioning that owns `public.trends`, and raise the count at
      `StartupFailureMapperTests.cs:43` from 9 to 10
- [x] write tests: `42703` maps to the new type carrying the server's message; `42P07` still maps to
      `ArchiveReadFailedError` (`ArchiveExceptionMapperTests.cs:144-151`); a per-type mapper case
- [x] run both affected suites — must pass

### Task 7: Archive health, the default-partition warning and its route to the banner

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/Errors/ArchiveDefaultPartitionNotEmptyError.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveHealthReader.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/ArchiveStatements.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Postgres/ArchiveStatementTextTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveHealthReadTests.cs`
- ➕ Create: `SemiPlot/SemiPlot.Tests/UI/Startup/ArchiveHealthBannerStartupTests.cs` — the warning's
  last leg, driving `App.InitializeServices` itself so the banner wiring is covered by the body the
  running application executes

- [x] add `ArchiveDefaultPartitionNotEmptyError(string host, int port, string database, string
      partition)`, its remedy naming the fault `docs/architecture/scada-archive.md:265-267` states
- [x] add `DefaultPartitionOccupancy` to `ArchiveStatements` with its literal and its pin in
      `ArchiveStatementTextTests`
- [x] implement `ArchiveHealthReader` returning zero or one warning on one connection, register it
      in `AddPostgresData`, and make an unreadable health check a logged nothing rather than a
      warning — a degraded probe must not become a second failure plane
- [x] carry the warnings out of `StartupProbe.ReadAsync` (`StartupProbe.cs:108-136`) in
      `StartupData`, resolved with `GetService` so a container holding a test double and no reader
      is unaffected, and **do not fail startup on it**: rows in `tpdefault` are still returned by
      every read, so refusing to start would hide a working archive from the operator over a
      planning fault
- [x] set `MainWindowViewModel.ArchiveHealthMessage` from those warnings in `App.InitializeServices`
      (`App.axaml.cs:116-154`) — the only writer of that property
- [x] add the arm to `StartupFailureMapper.Map` (`StartupFailureMapper.cs:32-44`) — the coverage
      test enumerates by namespace, not by reachability — and raise the count at
      `StartupFailureMapperTests.cs:43` from 10 to 11
- [x] write tests: a per-type mapper case; a probe test carrying warnings through without failing;
      gated tests reading a healthy archive (no warning) and a `tpdefault` seeded with one row (one
      warning), the second on a clone the class creates per test through `IAsyncLifetime`
      - ⚠️ deviation: both gated tests clone inside the test body with
        `await using var database = await postgresContainerFixture.CloneTemplateAsync(...)`, the shape
        `ArchiveWriterTransactionTests` already uses, rather than through `IAsyncLifetime`. xunit
        constructs the class once per test method either way, so each test still gets a database of its
        own and leaves nothing behind — the stated reason for `IAsyncLifetime` holds with less machinery
- [x] run all suites under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped

### Task 8: The seam guard in `TrendPenState.AppendRealtime`

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendPenState.cs`
- Modify: `SemiPlot/SemiPlot.Tests/UI/Chart/TrendChartViewModelTests.cs`

- [x] make `AppendRealtime` (`TrendPenState.cs:93-108`) ignore a timestamp at or before the last
      point already held, with a comment naming the case the provider cannot see: a history re-query
      through `ApplyHistory` (`TrendChartViewModel.cs:508-538`) moves history's last point past
      samples the poll has already delivered
      - ⚠️ decision: the guard compares the incoming axis X against `_centerPoints[^1].X`
        rather than keeping a `_lastAppended` field of its own. The stored X is what ScottPlot
        renders, so it is the value the backwards-segment invariant is about, and both resets fall
        out of it: `LoadHistory` refills the list and `ClearHistory` empties it, leaving no stale
        watermark to clear
- [x] leave `FoldRealtime` (`TrendPenState.cs:124-146`) alone — it writes the last column in place
      and appends nothing
- [x] write tests: an out-of-order append changes neither `CenterPoints` nor `BandPoints` nor
      `CurrentValue`; an ascending append still lands; the guard resets after `LoadHistory`
      (`:63-80`) and after `ClearHistory` (`:84-91`)
- [x] run `SemiPlot.Tests` — must pass

### Task 9: The fresh tail

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`
- Create: `SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/PostgresHistoryReadTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/Postgres/FreshTailBoundTests.cs`

- [x] extract the tail bound into a testable static — `tailStart = max(earliestSeam, to -
      onePeriod)`, `onePeriod = layer.ToPointSpacing() * 4`, skipped when `to - earliestSeam` does
      not exceed `layer.ToPointSpacing()` and always skipped at `AggregationLayer.Raw` — with a
      comment stating that the clamp is a cost bound and not a fault threshold, because at `Day` one
      period is 24 h (`AggregationLayer.cs:19-29`) and a coarse layer trailing by less than a period
      is the ordinary case
      - ⚠️ superseded by the review pass (`7d1e081`): `earliestSeam` is the minimum over **only the
        seams at or after the clamp**, not the global minimum. As written, a single pen the coarse
        read answered nothing for seamed at the window start and forced `tailStart` to the clamp —
        a full layer period of raw rows on every coarse query — for rows the per-pen exclusion then
        discarded. No seam reaching the clamp now means no tail read at all
- [x] issue the second bind of `ArchiveStatements.SparseHistoryWindow` with `@layer = 0` on the same
      connection, after the coarse read (`PostgresDataProvider.cs:142-151`)
- [x] drop the tail rows of every pen whose own seam falls before `tailStart`, with a comment naming
      the failure that prevents: a range no row covers and no null marks draws as one straight
      interpolated segment, because `HistoryRowFold` emits a gap only from a null (`:71-86`)
- [x] merge the remaining rows per pen, coarse then tail, preserving the identifier ordering
      `HistoryRowFold.Fold` requires (`HistoryRowFold.cs:24-27`)
- [x] write unit tests for the bound: no tail at `Raw`; no tail when the coarse layer is fresh; the
      clamp caps the span at one period; the skip threshold is `ToPointSpacing`; a pen whose seam
      precedes `tailStart` is excluded from the merge
- [x] write gated tests: a `Minute`-layer window whose right edge is past the coarse layer's newest
      row returns rows up to the raw layer's newest; the same window at `Raw` issues one read and
      returns the same rows as before; a pen whose coarse rows already reach the window end gains no
      duplicate; a pen whose coarse rows stop before `tailStart` gains no row and no interpolated
      span
- [x] run `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped
      - ⚠️ deviation: the bound and the merge live in a new internal static
        `SemiPlot/SemiPlot.DataSource.Postgres/FreshTail.cs` rather than inside
        `PostgresDataProvider`, which is already past the 300-line preference in `CLAUDE.md`.
        This matches the file's own neighbourhood — `HistoryRowFold`, `RealtimePoll`,
        `StatementTimeoutReader` are each a small internal type beside the provider — and it is
        what the test file `FreshTailBoundTests.cs` the plan names already implies
      - ⚠️ decision: the bound is computed on the archive's naive local wall clock, the side the
        statement binds and the side `HistoryRowFold.Row.ArchiveLocal` carries. `BindWindow` keeps
        its signature and its binder pin and now delegates to a private `BindLocalWindow`, which the
        tail binds through directly: converting the tail start out to UTC and back would cross a
        conversion that is neither order-preserving nor injective at a daylight-saving boundary
      - ⚠️ decision: the `AggregationLayer.Raw` short-circuit sits ahead of the seam computation
        rather than inside `FreshTail.Start`, because the seams walk the whole result set and the
        raw read is the one that must pay nothing for the tail
      - ⚠️ decision: the four gated cases clone the provisioned source inside the test body and
        write their own archive, the shape `ADroppedTrendsTableFailsNamingTrends` already uses in
        this class. The bench template carries raw rows alone (`RawLayerGenerator`), so it can state
        no coarse seam, and `SeededArchive`'s read-only contract stays intact

### Task 10: Appending in `ArchiveWriter` and idempotent partition creation

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ArchiveWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/PartitionScript.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/PartitionScriptTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveWriterTransactionTests.cs`

- [x] add `IF NOT EXISTS` to `PartitionScript.CreateStatement` (`:23-32`), with a comment stating
      why one form serves both callers: the seeded refusal (`ArchiveWriter.cs:21-29`, checked at
      `:54-59`) runs before the partition statements execute (`:66-69`), so a seed run can never meet
      an existing day partition and the clause can never mask one there
- [x] add an `allowExistingRows` parameter to `WriteAsync` (`ArchiveWriter.cs:33-81`), defaulting to
      `false`, which skips the `ArchiveIsSeededCommand` check at `:54-59` and nothing else. The
      `ArchiveExistsCommand` precondition (`:47-52`), the single transaction and the `COPY` stay
      shared rather than duplicated into a second method
- [x] keep the default path's refusal exactly as it is — the seeding run's guarantee is what keeps a
      half-filled archive from being read as a whole one — and leave every existing call site on the
      default
- [x] write tests for the partition statement's new form, including that the bounds still come from
      a `DateTime` and that nothing a caller typed reaches the statement
- [x] write gated tests, on a clone the class creates per test through `IAsyncLifetime`: the
      appending call succeeds against a seeded archive where the default is refused; it creates only
      the days its rows need; a failed `COPY` leaves neither rows nor new partitions
- [x] run `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped
      - ⚠️ decision: `allowExistingRows` sits ahead of `cancellationToken` in `WriteAsync`, keeping
        the token last as every other method in this repository does. The four call sites that
        passed the token positionally now name it (`ArchiveTemplate.cs:94`,
        `PostgresHistoryReadTests.cs:630-635`, `WriterConnectionFailureTests.cs:31`, and the
        rewritten `ArchiveWriterTransactionTests`); all of them stay on the default `false`
      - ⚠️ deviation: `ArchiveWriterTransactionTests` moved its clone out of the test body into
        `IAsyncLifetime`, which the plan's checkbox requires, so the existing rollback case now runs
        over the per-test clone as well. The class grew from 1 case to 4: the rollback case, an
        appending run that writes where the seeding run is refused and re-issues the existing day's
        partition statement, a case pinning that only the days its rows need are created, and an
        appending `COPY` that fails part-way and leaves the seeded archive as it was
      - ⚠️ deviation: `PartitionScriptTests.StatementNames` selected the third space-separated token,
        which `IF NOT EXISTS` moved. It now selects the token starting with `public.tp`, which no
        later change to the clause moves
      - ⚠️ decision recorded rather than fixed: `ArchiveTemplate.Name` moved from
        `semiplot_bench_9ec4f494420d6cf3` to `semiplot_bench_ae49012b91b22e8f`, because `ComputeName`
        (`ArchiveTemplate.cs:120-140`) digests the seeder assembly's module version and this task
        edits two seeder source files. That is the discriminator working as its comment states
        (`:35-38`) — a persistent server must not serve last week's seed to this week's code — and it
        is orthogonal to the option material, which is unchanged because `SeederOptions` is untouched.
        `RawLayerGeneratorTests.StandardSliceDigest` is unchanged and its test passes: no row this
        task writes differs from the row the same options wrote before it

### Task 11: The `--follow` demo writer

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/OptionTokens.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/FollowOptions.cs`
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/LiveTailGenerator.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederOptions.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Program.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/FollowOptionsTests.cs`
- Create: `SemiPlot/SemiPlot.Tests.Data/LiveTailGeneratorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SeederOptionsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Data/SeederEntryPointTests.cs`

- [x] extract the tokeniser from `SeederOptions.Parse` (`:68-110`) into `OptionTokens`, keeping the
      unknown-option and missing-value rules (`:83-99`) unchanged, and have `SeederOptions.Parse`
      call it. `SeederOptions`' record header (`:8-16`), its validation chain (`:156-172`) and its
      `--end` requirement (`:119-123`) are otherwise untouched
- [x] add `FollowOptions` with `Parse`, `Usage` and validation of its own: a finite positive
      `--follow` interval, `--pens` within the catalogue, a finite positive `--change-seconds`, and
      a stated rejection of `--end`, `--days`, `--break-count` and `--admin-connection`
- [x] route in `Program.Main` (`Program.cs:7-21`) on the presence of `--follow` in the raw arguments, so a
      seed run reaches `SeederOptions.Parse` on exactly the path it does today, and add `--follow` to
      `SeederOptions.Usage` (`:48-64`) as a pointer to `FollowOptions.Usage`
- [x] implement `LiveTailGenerator`: raw rows for `[from, to)` for the pens
      `RawLayerGenerator.SelectPens` chooses (`RawLayerGenerator.cs:43-70`), values from
      `SyntheticValueWalk`, in the pre-anchor-plus-change shape `docs/architecture/bench.md:43-47`
      describes, timestamps on whole milliseconds, layer `0` only
- [x] add the follow loop to `Program`: `lastEmitted` starts at the wall clock the loop starts at —
      never at the archive's `max(t)`, which against a bench seeded to `--end 2026-08-01`
      (`bench.md:199`) would write weeks of rows and a partition per day on the first tick. Each tick
      generates `[lastEmitted, now)`, calls the appending write path, prints one line naming the rows
      appended, sets `lastEmitted = now`, and exits 0 on Ctrl+C after the in-flight append completes
- [x] take `now` from `DateTime.Now` with its `Kind` stripped to `Unspecified`, with a comment
      stating why: the column is naive local time (`scada-archive.md:32`, `:79`) and a UTC clock
      would place the live edge one zone offset from where the viewer, converting through
      `source_time_zone`, looks for it
- [x] write tests: the generator emits strictly ascending whole-millisecond timestamps per pen and
      only layer `0`; two consecutive spans never share an `(id, l, t)`; `FollowOptions` accepts
      `--follow 1`, rejects `--follow` combined with `--end`, with `--days`, with `--break-count` and
      with `--admin-connection`, and rejects a non-positive or non-finite interval; every existing
      `SeederOptionsTests` case still passes unchanged
- [x] confirm `ArchiveTemplate.Slice` (`ArchiveTemplate.cs:25-33`) and `ComputeName` (`:120-140`)
      needed no edit, by comparing `ArchiveTemplate.Name` before and after the task — it must be the
      same string, or the whole bench template rebuilds for no reason
      - ⚠️ corrected in Task 10: `ComputeName` digests the seeder assembly's module version as well as
        the options, so `ArchiveTemplate.Name` moves on **any** edit to a `SemiPlot.Tools.ArchiveSeeder`
        source file and did move in Task 10. What this checkbox can assert is the half that is under
        the task's control: `Slice` and `ComputeName` need no edit, and the option material
        `Days/PenCount/Seed/ChangeSeconds/BreakCount/End` is unchanged because `SeederOptions` is
        untouched
- [x] run `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped
      - ⚠️ decision: the tokeniser extraction took `ReadNumber` with it. `OptionTokens` now holds
        `Read`, the generic `ReadNumber` and the two expectation strings (`WholeNumber`,
        `PlainNumber`); `SeederOptions` calls both and keeps every message byte-identical, which is
        what leaves its 40 existing cases passing unchanged. Duplicating the generic reader into
        `FollowOptions` was the alternative and is a plain DRY violation
      - ⚠️ decision: `FollowOptions` states an upper bound of 86400 s on both `--follow` and
        `--change-seconds`, which the plan's checkbox did not name. `SeederOptions` takes that
        ceiling from its own span (`ValidateChangeRate`); a follow run states no span, so the bound
        is a literal there. Without one, `1e18` survives `double.IsFinite` and overflows the tick
        arithmetic behind the lattice
      - ⚠️ decision: `LiveTailGenerator` puts every row on a lattice fixed to absolute time — a
        change every `--change-seconds` measured from `DateTime.MinValue`, its pre-anchor one
        `RawLayerGenerator.PollInterval` earlier, the value read from `SyntheticValueWalk` at the
        lattice index. A row belongs to the span its own timestamp falls in. That is what makes two
        adjacent spans disjoint without the generator carrying state between ticks, which is what
        `PRIMARY KEY (id, l, t)` requires of a writer that starts each tick from nothing. A change
        interval at or below the poll interval carries no anchor, because there is no room for one
      - ⚠️ deviation: a tick whose span holds no lattice change writes nothing and skips the write
        path entirely, printing `appended 0 rows`. At the default `--change-seconds 5` with
        `--follow 1` that is four ticks in five — the archive genuinely has no row between two
        changes. `--change-seconds 1` makes every tick write
      - ⚠️ measured, in place of the plan's original "`ArchiveTemplate.Name` must be the same string":
        `SemiPlot/SemiPlot.Tests.Data/Integration/ArchiveTemplate.cs` is untouched by this task
        (`git status`), so `Slice` and `ComputeName` needed no edit. The digest's option half —
        `Days/PenCount/Seed/ChangeSeconds/BreakCount/End` — is byte-identical at
        `1/8/1/5/4/2026-01-02T00:00:00.0000000`, because `SeederOptions`' record header, its five
        `Default*` constants and its `_knownOptions` list are all unchanged. The digest's other half
        moved as Task 10 said it must: the seeder assembly's module version went from
        `aee459341bf441e3995a1e10a4e03873` to `9236632e3c6f4ddebc63c7b145b5b38a` in Release, taking
        the template name from `semiplot_bench_00c2ab96c0bd87c8` to `semiplot_bench_fbd89b5b61516ce8`
      - ⚠️ measured: `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` reports 467 passed, 0
        failed, 0 skipped, up from 419. The 48 are 34 in `FollowOptionsTests`, 11 in
        `LiveTailGeneratorTests`, 1 in `SeederOptionsTests` and 2 in `SeederEntryPointTests`.
        `SemiPlot.Tests` is unchanged at 360 passed, 0 skipped

### Task 12: The journey project and its CI job

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Journeys/TestAppBuilder.cs`
- Create: `SemiPlot/SemiPlot.Tests.Journeys/ArchiveJourneyCollection.cs`
- Create: `SemiPlot/SemiPlot.Tests.Journeys/ArchiveHarnessSmokeTests.cs`
- Modify: `SemiPlot.slnx`
- Modify: `.github/workflows/ci.yml`

- [x] create the project referencing `SemiPlot.UI` and `SemiPlot.Tests.Data` — the direction
      `CLAUDE.md` permits — with xunit v3, `Avalonia.Headless.XUnit` and AwesomeAssertions, and
      **no `xunit.runner.json`**: this project's skips are stated absences, which is the whole reason
      the journeys are not in `SemiPlot.Tests`, whose `failSkips` would turn each planned skip into a
      failure on the Windows leg and on every machine without a container runtime
      - ⚠️ decision: the package list is the union of what the two halves need and nothing more —
        `Microsoft.NET.Test.Sdk`, `xunit.v3`, `xunit.runner.visualstudio`, `AwesomeAssertions`,
        `Avalonia.Headless`, `Avalonia.Headless.XUnit`, `Microsoft.Extensions.DependencyInjection`,
        all versionless under central package management. `SemiPlot.Core`, `Npgsql`, `Avalonia` and
        `ReactiveUI.Avalonia` are not named: they arrive over the two project references, which is
        how `SemiPlot.Tests` already reaches `Avalonia` and `ReactiveUI.Avalonia`
      - ⚠️ decision: the reason for the absent `xunit.runner.json` is written into the `.csproj` as
        an XML comment where the file would otherwise sit, so a later reader adding one has to delete
        the reason first
- [x] add a `TestAppBuilder` carrying `[assembly: AvaloniaTestApplication]`, modelled on
      `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs:10-21`, because that attribute is per-assembly
- [x] add a local `[CollectionDefinition]` over `ICollectionFixture<PostgresContainerFixture>`,
      because a collection definition is discovered per test assembly
      - ⚠️ decision: named `ArchiveJourneyCollection` with `Name = "archive-journey"` rather than
        reusing `ArchiveDatabaseCollection.Name`. The two definitions live in different assemblies
        and never meet, and a distinct string keeps a `[Collection]` attribute in this project from
        reading as if it bound to the data suite's definition
- [x] add the project to `SemiPlot.slnx`
- [x] add a `journey-tests` job to `.github/workflows/ci.yml` on `ubuntu-latest` with
      `SEMIPLOT_REQUIRE_DB: "1"`, modelled on `data-tests` (`:85-95`) for the variable and on
      `ui-tests-linux` (`:55-83`) for the `libfontconfig` note (`:78-81`), and reword the comment at
      `:92-94` so it names all three jobs correctly: `build-and-test` runs on a Windows runner that
      cannot host a Linux container, `SemiPlot.Tests` still has no gated test, and `journey-tests`
      requires one
      - ⚠️ deviation: the reworded comment names four jobs, not three — `ui-tests-linux` is the
        second job that omits the variable, and the original sentence covered it only implicitly by
        naming `SemiPlot.Tests`. The `libfontconfig` note is carried as a cross-reference to
        `ui-tests-linux` rather than a second copy of the same three lines
- [x] verify the copied `bench/` context lands in this project's output directory, which is what
      `PostgresContainerFixture` builds the image from (`PostgresContainerFixture.cs:170-177`)
      - ⚠️ measured: `SemiPlot/Artifacts/bin/SemiPlot.Tests.Journeys/release/bench` holds
        `Dockerfile` and `provision.sh`, and the same directory holds no `xunit.runner.json`
- [x] write one gated smoke test that clones the seeded template and reads one row, proving the
      harness works across the assembly boundary before a journey depends on it
      - ⚠️ decision: `ArchiveHarnessSmokeTests` reads the archive extent through
        `ArchiveProviderFactory.Build` and `IDataProvider.QueryArchiveExtentAsync`, so one test proves
        the whole chain the journeys stand on: this assembly's collection definition starts the
        server, the template seeded in `SemiPlot.Tests.Data` clones, and the clone answers through
        the real `AddPostgresData` registration. It is a plain `[Fact]` at
        `Component=Core, Area=Data, Category=Integration` — it constructs no UI type, so labelling it
        `Component=UI` would misname what it covers; Tasks 13 and 14 bring `[AvaloniaFact]` and
        `Component=UI` with them
- [x] run all three suites under `SEMIPLOT_REQUIRE_DB=1`, then run `SemiPlot.Tests.Journeys` again
      with the variable unset and no runtime, and confirm it skips with a stated reason instead of
      failing
      - ⚠️ measured: `SemiPlot.Tests` 360 passed / 0 failed / 0 skipped; `SemiPlot.Tests.Data`
        under `SEMIPLOT_REQUIRE_DB=1` 467 passed / 0 failed / 0 skipped; `SemiPlot.Tests.Journeys`
        under `SEMIPLOT_REQUIRE_DB=1` 1 passed / 0 failed / 0 skipped. `dotnet build SemiPlot.slnx -c
        Release` reports 0 warnings and 0 errors, `dotnet format SemiPlot.slnx --verify-no-changes`
        exits 0
      - ⚠️ deviation: the no-runtime leg was reached by pointing `SEMIPLOT_TEST_PG` at a server
        with `SEMIBASE_EXE` unset rather than by stopping the container runtime this machine runs.
        `DOCKER_HOST` set to an unreachable endpoint does not make Testcontainers unavailable here —
        both an `npipe://` and a `tcp://` override were ignored and the container still started — so
        the unavailable state was produced on the other branch of the same `InitializeAsync`. The
        gate is the identical code path: with the variable unset the single test reports `[SKIP]`
        with the reason "SEMIBASE_EXE is not set: SEMIPLOT_TEST_PG names a server this suite
        provisions by spawning semibase, so the variable must point at the binary." and the run ends
        0 failed; with `SEMIPLOT_REQUIRE_DB=1` the same state fails through `DatabaseGate.Require`,
        which proves the project carries no `failSkips`

### Task 13: Journey — a seeded break renders as a broken line

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Journeys/BreakRenderArchiveJourneyTests.cs`

- [x] `[AvaloniaFact]`, because the test constructs `TrendChartViewModel`. Take `SeededArchive` as a
      class fixture: this journey writes nothing, so it honours that fixture's leave-it-as-you-found-it
      contract
- [x] build the provider through `ArchiveProviderFactory.Build` (`ArchiveProviderFactory.cs:30-38`)
      and drive a real `TrendCoordinator` and `TrendChartViewModel` over it, with the container's own
      `IScheduler` (`DefaultScheduler.Instance`, registered at
      `PostgresDataServiceCollectionExtensions.cs:21`) as the data scheduler and
      `ImmediateScheduler.Instance` as the UI scheduler — the pair
      `ChartGapRenderTests.CreateViewModel` (`ChartGapRenderTests.cs:168-181`) already uses under
      `[AvaloniaFact]`
      - the journey mirrors `App.InitializeServices` (`App.axaml.cs:118-165`): it reads the catalogue
        and the extent through the provider, adds every catalogued pen, opens the window, calls
        `coordinator.Start()` and then awaits `RequestInitialHistory`
- [x] open the window on the first seeded break, taken from `BreakPlan.Create(ArchiveTemplate.Slice)`
      the way `PostgresHistoryReadTests` does (`PostgresHistoryReadTests.cs:67`), at a
      window-to-break ratio wide enough to leave the break several pixel columns — the guard
      `ChartGapRenderTests` asserts at `:113-116` against its `MinimumBreakColumns` constant (`:68`)
      - ⚠️ deviation: the window is opened through `ChartNavigationController.TrackDataExtents` — the
        entry point `SeedFromArchiveExtent` itself calls — with the break's own centre as the last
        sample, keeping the controller's opening width of one hour. No zoom and no pan: a navigation
        gesture raises `WindowChanged` with `RequiresHistoryRequery: true`, which re-queries through
        `ChartHistoryRequestDebouncer` on the data scheduler, and that result would land on the pen
        states while the test thread is inside `Plot.GetImage`. The awaited `RequestInitialHistory` is
        therefore the only history read in the test. Measured: the first break spans 72 pixel columns
        of the data area at a plot width of 800 px, against the guard's minimum of 12
      - ⚠️ deviation: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` gains
        `<InternalsVisibleTo Include="SemiPlot.Tests.Journeys"/>`, against the Context section's claim
        that no `InternalsVisibleTo` entry changes. `LocalTimeAxis` is `internal` and is "the single
        conversion boundary between the UTC time domain and the chart's local-time axis"
        (`LocalTimeAxis.cs:3-4`); the journey stands in for `TrendChartView.ApplyWindow`
        (`TrendChartView.axaml.cs:171-174`), the one line of the view it needs, and transcribing that
        conversion into the test would be exactly the drift `LocalTimeAxis` exists to prevent
- [x] await `RequestInitialHistory` (`TrendChartViewModel.cs:194-229`), render through
      `Plot.GetImage` and read `RenderManager.LastRender.Layout.DataRect`, the technique
      `ChartGapRenderTests.cs:149-166` uses
- [x] assert with the `ColumnCarriesPenColor` band sampling (`ChartGapRenderTests.cs:190-207`):
      every column inside the break carries no pen colour, and the columns either side do
      - ⚠️ deviation: the band probe reads chroma — `max(R,G,B) - min(R,G,B)` against the same
        threshold of 24 — rather than `ChartGapRenderTests`' red dominance. The pen colours here come
        out of the archive's own catalogue rather than being chosen by the test, and every catalogue
        colour is a saturated hue while the plot's frame, grid and labels are greyscale. The test
        guards that reading by asserting every catalogued pen's own colour clears the threshold
      - ⚠️ deviation: one pixel column is dropped from each end of the break before probing
        (`BoundaryInsetColumns`), for the same reason `ColumnCarriesPenColor` insets the rows it reads
        from the data rectangle. Measured without it: the break spans columns 377 to 448 and column
        447 alone carries chroma — the antialiased leading edge of the segment resuming at the q = 16
        row, which falls between two columns rather than on one. 70 of the 72 columns are still probed
- [x] open with `RequireAvailable()` so the journey skips with a reason on a machine with no runtime
- [x] run the journey suite under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped, and must skip
      cleanly with the runtime stopped and the variable unset
      - ⚠️ measured: `SemiPlot.Tests.Journeys` under `SEMIPLOT_REQUIRE_DB=1` is 2 passed / 0 failed /
        0 skipped, green on three consecutive runs. `SemiPlot.Tests` 360 passed / 0 failed / 0
        skipped; `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` 467 passed / 0 failed / 0
        skipped. `dotnet build SemiPlot.slnx -c Release` reports 0 warnings and 0 errors, and
        `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
      - ⚠️ deviation: the no-runtime leg was reached the way Task 12 records, by pointing
        `SEMIPLOT_TEST_PG` at a server with `SEMIBASE_EXE` unset rather than by stopping the container
        runtime this machine runs. Both journey-project tests report `[SKIP]` with the stated reason
        and the run ends 0 failed

### Task 14: Journey — a live insert arrives exactly once

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] `[AvaloniaFact]` with the same scheduler pair as Task 13. This journey appends, so it clones
      per test through `IAsyncLifetime` and never takes `SeededArchive`
      - the clone is `CloneTemplateAsync`, not `CloneProvisionedAsync`: the journey joins history to
        the live edge, so it needs the seeded rows `RequestInitialHistory` draws and the appended rows
        that follow them
- [x] build the provider and subscribe through `TrendCoordinator` so the assertion runs over the
      composed path rather than over the provider alone
      - the window is opened by `SeedFromArchiveExtent`, the production entry point, which leaves the
        archive's last hour and therefore `AggregationLayer.Raw`. The test asserts that layer before
        it appends: only the raw layer appends a realtime sample as a point of its own, and a coarse
        layer would fold it into the last column where no live-edge assertion could read it
- [x] sequence every step on an awaited gate, in this order and no other: subscribe; **await the
      first `ArchiveConnectionState.Connected`**, which is the subscription's only observable armed
      point and is what stops the append that follows from being swallowed by the baseline read;
      append exactly one row per pen through the appending write path from the test body — no second
      process, so nothing races; await the next batch
      - the `ArmedGate` is constructed ahead of `TrendChartViewModel`, because that constructor takes
        the first `RefCount` subscription on `RealtimeBatches` and that is what starts the poll. A
        gate opened after the first tick would have nothing left to complete it
      - the appending write path is `ArchiveWriter.WriteAsync(..., allowExistingRows: true)`, one row
        per catalogued variable at the archive's last timestamp plus one second
- [x] build every gate as `TaskCompletionSource(TaskCreationOptions.RunContinuationsAsynchronously)`
      completed from an observable subscription — the shape
      `ChartHistoryRequestDebouncerTests.cs:69` and `:71` use — because production code on the poll thread
      completes what the test body awaits, and an inline resumption would run the rest of the test,
      ScottPlot rendering included, off the test's own thread
      - both gates take the flag. `BatchCollector.Collect` completes its gate while holding the lock
        that guards the recorded batches, so an inline continuation would run the rest of the test
        inside that lock as well as off the Avalonia dispatcher
- [x] assert the awaited batch carries exactly the appended timestamps and values, converted to UTC,
      and that its timestamps are strictly after the archive's extent before the write
      - the value is `penId + tick * 0.25`, distinct per variable and per write, so a batch carrying
        the right timestamp with another variable's value still fails
- [x] append a second row, await the next batch, and assert no timestamp repeats — the monotonic
      `lastSeen` rule, observed from the consumer side
      - `BatchCollector` holds one subscription for the whole test rather than one per await, so a
        repeat arriving between two awaits is still recorded. The exactly-once assertion is therefore
        over everything that arrived: two batches in total, and their timestamps equal to the two
        appended instants in order
- [x] assert `ChartNavigationController.OnLiveEdge` moved the chart's live edge, so the journey
      covers the whole path down to the axis (`ChartRealtimeApplier.cs:16-19`,
      `ChartNavigationController.cs:144-163`)
      - ⚠️ deviation: `_liveEdge` has no public accessor, so the assertion reads `Navigation.To`,
        which a sticky window sets to the live edge (`TrendNavigationModel.cs:108-118`). The test
        pins `IsSticky` first, so a window that stopped following the edge fails on its own line
        rather than silently making the live-edge assertion vacuous. `TrendPenState.CurrentValue` is
        asserted beside it, which is the pen-state half of the same path
- [x] use no `Task.Delay`, no `WaitAsync`, no deadline and no retry loop anywhere in the file
- [x] run the journey suite under `SEMIPLOT_REQUIRE_DB=1` — must pass with 0 skipped, and must skip
      cleanly with the runtime stopped and the variable unset
      - ⚠️ measured: `SemiPlot.Tests.Journeys` under `SEMIPLOT_REQUIRE_DB=1` is 3 passed / 0 failed /
        0 skipped, green on five consecutive runs. `SemiPlot.Tests` 360 passed / 0 failed / 0
        skipped; `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` 467 passed / 0 failed / 0
        skipped. `dotnet build SemiPlot.slnx -c Release` reports 0 warnings and 0 errors, and
        `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
      - ⚠️ deviation: the no-runtime leg was reached the way Tasks 12 and 13 record, by pointing
        `SEMIPLOT_TEST_PG` at a server with `SEMIBASE_EXE` unset. All three journey-project tests
        report `[SKIP]` with the stated reason and the run ends 0 failed

### Task 15: Verify acceptance criteria

**Files:**
- Modify: `docs/plans/20260824-postgres-live-edge-and-demo.md`

- [x] verify all requirements from Overview are implemented
      - ⚠️ measured: all five hold. The live edge is `RealtimePoll` behind
        `PostgresDataProvider.Subscribe` (`PostgresDataProvider.cs:88-96`, `:229-274`), a sequential
        `ReadOnceAsync` then `_scheduler.Sleep(PollInterval)` loop. The fresh tail is `FreshTail.cs`.
        `--follow` is `FollowOptions` plus `LiveTailGenerator` plus `Program.FollowAsync`. The stub is
        gone from every code and build file. The journeys are `BreakRenderArchiveJourneyTests` and
        `LiveEdgeArchiveJourneyTests`. `IDataProvider` gained exactly one member —
        `IObservable<ArchiveConnectionState> ConnectionFaults`; the diff against `origin/master` is
        that one property and its comment
- [x] verify the edge cases are handled: an archive with no rows for the subscribed pens; a poll
      interval shorter than a query; a row with a null `v`; a row with `q = 32` at the live edge; a
      window at `Raw` (no tail); a coarse window already fresh (no tail read); a pen whose seam
      precedes the tail start; a disposal during an in-flight query
      - ⚠️ measured, one by one:
        - no rows for the subscribed pens — `RealtimePollReadTests` clones the *provisioned* (empty)
          database, so `TheFirstTickEmitsNoSampleAndReportsTheSubscriptionArmed` is that case;
          `RealtimePoll.cs:182-185` leaves `lastSeen` unset and the next tick repeats the probe
        - poll interval shorter than a query — the loop is sequential, so ticks cannot overlap: the
          interval is a floor applied *after* each tick (`PostgresDataProvider.cs:244-266`). Measured
          live at 1000 ms against the demo bench, consecutive tick log stamps are 14:21:24.679,
          14:21:25.690, 14:21:26.708, 14:21:27.726 — 1.01 to 1.02 s apart, the query time added
        - null `v` — `RealtimePollReadTests.ARowCarryingANullValueEmitsNoSampleReportsNoFaultAndStillAdvancesTheLastSeen`
        - `q = 32` at the live edge — `RealtimePollReadTests.ARowMarkingTheLastSampleBeforeABreakEmitsAnOrdinarySample`
        - a window at `Raw` — `FreshTailBoundTests.NoTailIsReadAtTheRawLayer`
        - a coarse window already fresh — `FreshTailBoundTests.NoTailIsReadWhenEveryPenReachesWithinOnePointSpacingOfTheWindowEnd`
        - a pen whose seam precedes the tail start — `FreshTailBoundTests.APenWhoseSeamPrecedesTheTailStartContributesNoTailRow`
          and `PostgresHistoryReadTests.APenWhoseCoarseRowsStopBeforeTheTailStartGainsNoRowAndNoInterpolatedSpan`
        - disposal during an in-flight query — `RealtimeSubscriptionTests.DisposingASubscriptionStopsItsPoll`,
          with the post-await re-check at `PostgresDataProvider.cs:250-253`
- [x] run every numbered command in the **Acceptance Evidence** section above and record its observed
      result beside the `f34567c` figure already stated there
      - ⚠️ measured on Windows 11 with Docker 29.7.2, at `faafad5`:
        1. `dotnet build SemiPlot.slnx -c Release` — 0 warnings, 0 errors. Holds
        2. `dotnet format SemiPlot.slnx --verify-no-changes` — exit 0. Holds
        3. `SemiPlot.Tests.Data` under `SEMIPLOT_REQUIRE_DB=1` — 467 passed, 0 failed, 0 skipped.
           Holds; the section's `f34567c` figure of 360 is the pre-slice number
        4. `SemiPlot.Tests` with the variable unset — 360 passed, 0 failed, 0 skipped. Holds
        5. `SemiPlot.Tests.Journeys` under `SEMIPLOT_REQUIRE_DB=1` — 3 passed, 0 failed, 0 skipped.
           Both named journeys are listed as passed:
           `BreakRenderArchiveJourneyTests.TheFirstSeededBreakLeavesTheRenderedCurvesBroken` and
           `LiveEdgeArchiveJourneyTests.ARowWrittenAfterStartupReachesTheChartOnceAndMovesItsLiveEdge`
        6. journeys with no runtime — 0 failed, 3 skipped, each with a stated reason
        7. `git ls-files | Select-String "SemiPlot.DataSource.Stub" | Measure-Object -Line` reports
           `Lines : 0`. Holds. The `git grep` returns **37 lines, not nothing** — see the deviation
           below
        8. `Select-String "HaveCount\(11\)"` returns exactly one line
           (`StartupFailureMapperTests.cs:43`). The filtered run reports 25 passed, 0 failed. Holds
        9. `Select-String` for the two plan names returns exactly two lines — `ExplainPlanTests.cs:219`
           and `:250`. The filtered run under `SEMIPLOT_REQUIRE_DB=1` reports 5 passed, 0 failed,
           0 skipped and lists both added cases. Holds
        10. the demo writer — holds in full; the readings are below
        11. the application follows the edge — the machine-readable half holds, the on-screen half
            was not observed; see the deviation below
      - ⚠️ **the three counts above are anchored at `faafad5` and were moved by the review pass.**
        They are kept as measured; the figures at HEAD are:

        | Suite | at `faafad5` | at HEAD | What moved it |
        | --- | --- | --- | --- |
        | `SemiPlot.Tests` | 360 | **362** | the sparse-timestamp regression test and `RealtimeBatch_KeepsEachPenOnItsOwnTimestamps` |
        | `SemiPlot.Tests.Data` | 467 | **483** | 9 `FreshTail` theory rows, 2 tick-bound rows, the fault-clearing crossing, the 4-test empty-archive class |
        | `SemiPlot.Tests.Journeys` | 3 | **4** | the sparse-timestamp journey |

        The full-solution total moves with them: 830 at `faafad5`, **849** at HEAD. Every run is
        0 failed / 0 skipped in both columns.
      - ⚠️ **step 5's stated expectation is wrong and is corrected here.** It anticipates two journey
        tests. `SemiPlot.Tests.Journeys` holds **three**: Task 12 landed
        `ArchiveHarnessSmokeTests.TheClonedTemplateAnswersAnExtentReadThroughTheRealProvider` in the
        same project. Read step 5 as: both named journeys must be listed as passed, and the run must
        report `failed 0`, `skipped 0` over 3 total
      - ⚠️ **step 7's second command does not return nothing, and cannot before Task 16.** It returns
        37 lines. **0 of them are in a `.cs`, `.csproj`, `.slnx`, `.axaml` or `.yml` file** — the
        retirement is complete in code. 23 are inside this plan file, which is the record of the work
        and keeps the names on purpose; the section excludes `docs/plans/completed` and
        `docs/plans/roadmaps` for exactly that reason but cannot exclude a plan still sitting in
        `docs/plans/`. The other 14 are the documentation Task 16 owns and has not yet rewritten:
        `CLAUDE.md:217`, `:223`; `docs/architecture/data-integration.md:76`, `:77`, `:606`;
        `docs/architecture/overview.md:68`, `:69`, `:76`, `:112`, `:126`;
        `docs/architecture/postgres-topology.md:88`, `:116`; `docs/plans/backlog.md:9`;
        `readme.md:37`. Re-run the command after Task 16 and expect only this plan's own lines
      - ⚠️ deviation on step 4: it asks for the run "with no container runtime running". Docker could
        not be stopped on this machine. `SemiPlot.Tests` holds no gated test, so no container state
        can reach it; the run is 360 passed / 0 skipped either way
      - ⚠️ deviation on step 6: same reason, so the no-runtime leg was reached the way Tasks 12 to 14
        record — `SEMIPLOT_TEST_PG` pointed at a server with `SEMIBASE_EXE` unset. All three tests
        report `[SKIP]` with the reason *"SEMIBASE_EXE is not set: SEMIPLOT_TEST_PG names a server
        this suite provisions by spawning semibase, so the variable must point at the binary."* The
        run ends `failed 0`. What was **not** verified is the wording when the runtime itself is the
        absent thing
      - ⚠️ measured, step 10, against the bench raised exactly as `bench.md:187-200` describes — image
        built from `SemiPlot.Tests.Data/bench`, container on port 55432, `semiplot_app` cloned from
        `semiplot_provisioned`, seeded `--end 2026-08-01T00:00:00 --days 1 --pens 8 --seed 1`:
        - the writer printed one line per tick — `appended 0 rows up to ...` on ticks the change
          lattice does not cross and `appended 16 rows up to ...` on ticks it does — and did not exit
        - `max(t)` before the writer started: `2026-07-31 23:59:59.269`. First reading, at local wall
          clock `2026-08-27T14:17:10.68`: `2026-08-27 14:17:10`. **The writer starts at "now", not at
          the archive's own maximum** — one tick moved the edge 27 days forward
        - second reading 10.24 s later at `14:17:20.92`: `2026-08-27 14:17:20`, later than the first
          by the elapsed wall-clock time
        - `pg_inherits` count: **3** — `tp2026m07d31` (the seeded day), `tpdefault`, and
          `tp2026m08d27` (the day the follow run writes into). The seeded day plus `tpdefault` plus
          exactly one more, as stated
        - Ctrl+C: the process printed `stopped` and **exited 0**, measured through
          `GenerateConsoleCtrlEvent(CTRL_C_EVENT)` and `Process.ExitCode`. The archive stayed readable
          and a third `max(t)` read `2026-08-27 14:17:20`, unmoved from the second
        - ⚠️ a first attempt read `max(t)` 0.18 s **before** the writer's first row-bearing tick
          committed and therefore still saw the seeded `2026-07-31 23:59:59.269`. That is a race in
          the measurement, not in the writer: at the default `--change-seconds 5` with `--follow 1`
          four ticks in five append nothing. The reading above is the rerun, taken after the first
          `appended 16 rows` line
      - ⚠️ measured, step 11, with the writer still running and `archive-connection.yaml` naming
        `semiplot_app` on `localhost:55432` as `semiplot_reader`, `source_time_zone: Europe/Moscow`,
        `poll_interval_ms: 1000`:
        - the application started and held a window titled `SemiPlot - Trend Viewer`, `Responding`
          True, for the whole run
        - `pg_stat_activity` carried **2** `semiplot_reader` backends while it ran, and
          `pg_stat_user_tables.idx_scan` on `semiplot_tags` is 8, so the catalogue was read
        - the application log carries **0 `[ERR]`, 0 `[FTL]` and 0 `[WRN]` lines**
        - the poll is visible in the log at `--logging-level debug`: a baseline line — *"The realtime
          baseline for 8 variables is 2026-08-27T14:21:05.0000000."* — then one line per second,
          including *"The realtime poll read 8 rows past 2026-08-27T14:21:20.0000000: 8 samples, 0
          dropped for a null value, 0 marking the last sample before a break."* **The composed
          application reads the rows the writer appends, at the writer's cadence.**
        - `max(t)` moved from `2026-08-27 14:19:45` to `2026-08-27 14:20:10` across the 25 s window
          the application was reading
        - ⚠️ observed, and not a failure: `seq_scan` on the live-edge partition `tp2026m08d27` grew by
          28 over that 25 s while `idx_scan` stayed at 56. The partition held a few hundred rows at
          that moment, so the planner picks a sequential scan over an index probe. The `EXPLAIN` guard
          runs against the seeded, analysed template, where `ThePollPlanReachesItsRowsThroughAnIndex`
          passes
        - ⚠️ deviation: the run used `--log-file <scratchpad>\semiplot.log` rather than the default
          `C:\DISTR\Logs\SemiPlot\semiplot.log`, and a second run at `--logging-level debug`. At
          `--logging-level information` the log file **is never created**: nothing in the composed
          application logs at Information or above on a clean start, so "carries no `[ERR]` or
          `[FTL]` line" is vacuously true there. The debug run is what makes it a positive statement
        - ⚠️ **not verified, and it needs a person at the screen.** Whether *the curve advances to the
          right on its own with the sticky toolbar state on* was not observed — no window was watched,
          and no claim is made about it. What an operator must see: the toolbar's sticky state on, the
          right-hand edge of every curve stepping right roughly once a second while the `--follow`
          writer runs, the window's right bound moving with it, and no banner row over the chart.
          Everything above this line was read from the server and the log and needs no screen
- [x] run the full suite: `dotnet test SemiPlot.slnx -c Release` with `SEMIPLOT_REQUIRE_DB=1`, and
      expect two containers — one per container-holding project
      - ⚠️ measured: 360 + 467 + 3 = **830 passed, 0 failed, 0 skipped**, green on two consecutive
        runs. Sampling `docker ps` through one of them shows **two** distinct suite containers over
        the run — `strange_elion` and `xenodochial_keldysh`, both on `semiplot-bench:fc0cf5409512` —
        one per container-holding project, beside `testcontainers-ryuk`. They do not overlap: the
        projects run one after another, so the peak is one suite container at a time
      - ⚠️ superseded on the total only: at HEAD it is 362 + 483 + 4 = **849 passed, 0 failed,
        0 skipped**. The two-container observation is unchanged
- [x] confirm the scope guard held: no coordinator batching change, no bucketing, no change to
      `Sample`, no change to `SeederOptions`' shape, no shape assertion or DDL transcription, no
      compose file, no second orchestration mechanism, and exactly three new error types
      - ⚠️ measured against `origin/master...HEAD`:
        - `TrendCoordinator.cs` is +19 lines and **none of them touch batching**:
          `BuildRealtimeBatches`, `BuildRealtimeBatch`, `BuildColumn` and `_batchWindow` are
          byte-identical. The addition is the `ConnectionFaults` republish and its disposal
          - ⚠️ superseded by the review pass (`6e1056c`). `BuildRealtimeBatches` and `_batchWindow`
            are still byte-identical at HEAD; `BuildColumn` is deleted and `BuildRealtimeBatch`
            now folds per pen. The batching *stream* is untouched, the fold is not — see the
            narrowing note under **Scope guard, honoured throughout**
        - no bucketing: the only `bucket` tokens in the tree are `MinMaxDecimator`'s existing
          client-side ones. No statement added by this slice groups or buckets server-side
        - `SemiPlot/SemiPlot.Core/Trends/Sample.cs` — untouched, absent from the diff
        - `SeederOptions.cs` changed, as Task 11's file list allows, but its **shape** did not: the
          record header is the same eight positional members in the same order, and the five
          `Default*` constants and `_knownOptions` are unchanged. The change is the tokeniser moving
          to `OptionTokens` and one `--follow` line in the usage text
        - no shape assertion or DDL transcription: the three statements added to `ArchiveStatements`
          are `RealtimePoll`, `RealtimeBaseline` and `DefaultPartitionOccupancy`. None reads
          `information_schema` or `pg_attribute`; the unexpected shape is still reached from the
          `42703` a real read answers
        - no compose file: `git ls-files` matches no `docker-compose*` and no `compose.y*ml`
        - no second orchestration mechanism: `SemiPlot.Tests.Journeys` reuses
          `PostgresContainerFixture` across the project reference and adds only a local
          `[CollectionDefinition]` (`ArchiveJourneyCollection.cs`), which the plan prescribes. It
          carries no `xunit.runner.json`
        - exactly three new error types: `ArchiveConnectionLostError`,
          `ArchiveDefaultPartitionNotEmptyError` and `ArchiveShapeUnexpectedError` — the only
          additions under `SemiPlot.Core/Data/Errors/`, and 8 + 3 is the `HaveCount(11)` the coverage
          test now asserts
- [x] confirm `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
      - ⚠️ measured: exit 0, no output

### Task 16: [Final] Update documentation

**Files:**
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/bench.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`
- Modify: `docs/plans/backlog.md`
- Modify: `CLAUDE.md`
- Modify: `readme.md`

- [x] `testing-strategy.md`: rewrite `:105-130`. The sentence that `SemiPlot.Tests` holds no gated
      test and keeps `failSkips` stays true and stays; the boundaries section gains
      `SemiPlot.Tests.Journeys` as the third project, with its reference direction, its absent
      `xunit.runner.json`, and the reason a gated journey can live in neither of the other two —
      `failSkips` has no per-test scope, and the data suite may not reference the UI
- [x] `data-integration.md`: rewrite the **Realtime** section (`:414-433`) so it describes the poll
      that ships instead of naming the slice that would build it, including that `Connected` is
      reported on each subscription's first successful tick, that a null `v` is dropped, and that a
      `q = 32` row opens no gap on the realtime seam; mark the fresh tail implemented at `:318-320`
      and state the bound, the skip rule and the per-pen exclusion; add the two realtime statements
      under their own headings beside the poll at `:225-235`, and the health statement with them;
      correct the implementations sentence at `:75-77` and the every-implementation wording at
      `:437-442`; add the three new error types to the table at `:498-506` and change "Seven public
      types" at `:521` to ten Core types plus the UI-local one; drop the `--use-stub` step at
      `:606-607`
- [x] `bench.md`: add `--follow` to the ownership table (`:9-13`) and a recipe section of its own,
      stating that it appends layer `0` only, that it writes the machine's local wall clock, and that
      it starts at "now" rather than at the archive's maximum; replace the paragraph at `:171-175`,
      which says no CI job hosts Avalonia and a container at once, with what the `journey-tests` job
      now does; add the live-edge question to the table at `:213-219`
- [x] `overview.md`: delete the stub from the diagram (`:68-69`), the sentence at `:76`, the switch
      table row at `:112` and the sentence at `:126`
- [x] `postgres-topology.md`: delete the stub node at `:88` and the sentence at `:116`
- [x] `CLAUDE.md`: correct `:217` and `:222-223`, which name `SemiPlot.DataSource.Stub` as the
      current stub and as what the seeder must not reference; add `SemiPlot.Tests.Journeys` to the
      test-project table and to the reference-direction rule
- [x] `readme.md:37` and `docs/plans/backlog.md:9`: state that the viewer reads the PostgreSQL
      archive
- [x] `docs/plans/backlog.md`: record the one behaviour this slice names and does not close — a break
      that opens at the live edge draws as a held line until the next history read, because `Sample`
      carries no null channel
- [x] stamp the roadmap slice `postgres-live-edge-and-demo` DONE with its plan path, PR number and
      branch, in the same pull request
      - ⚠️ no pull request exists at exec time, so the PR field reads "opened at delivery,
        with this stamp in it" rather than a fabricated number. The plan path is recorded as
        `docs/plans/completed/...`, where the delivery step's move lands it, matching every other
        DONE slice
- [x] moving this plan to `docs/plans/completed/` is the delivery step's work, done with the roadmap
      stamp above rather than as a checkbox of its own
      - ⚠️ deferred to the delivery step — exec never moves the plan

## Post-Completion
*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification**

Acceptance Evidence steps 10 and 11 are run by hand against the application bench in
`docs/architecture/bench.md:169-233`. Step 10 needs no screen; step 11 does.

**Acceptance items the roadmap names for the operator, which this slice does not settle**

Three judgments need the demo stand and stay open when the roadmap closes: whether a break, a rung
change and a live edge look right on screen; whether the vendor's thinning rule matches what
`LayerThinner` assumes, which needs a real SCADA writing; and whether the window is legible to
someone running a process.

A measurement from the demo bench showing a wide-window read slow enough to notice is the condition
that re-adds the dropped `postgres-bucketed-read` slice.

**Remaining slices**

None. `postgres-live-edge-and-demo` is the last PENDING slice of
`docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`. Every other slice is DONE except
`postgres-bucketed-read`, which is DROPPED. The roadmap's close condition — every slice not marked
DROPPED carries a merged PR, and the application draws real history, follows a live edge moved by
the `--follow` writer, selects layers by window width, breaks the line only where the archive says a
break occurred, and `SemiPlot.DataSource.Stub` no longer exists — holds once this slice's pull
request merges.

**Known limitations recorded rather than fixed**

*The live edge stops for the repeated hour at the autumn fall-back.* `RealtimePoll` binds
`t > @lastSeen` in the archive's own naive local wall clock, so when the clock steps back an hour the
rows of the second pass carry timestamps the poll has already seen and are never delivered. The
operator sees a chart whose live edge stops advancing for up to an hour while the status row still
reads healthy — the reads succeed, so no fault is raised. History over the same range is unaffected:
it is read by window rather than by a moving bound, and `HistoryRowFold` drops the second pass on its
own ascending check. The edge resumes by itself at the end of the repeated hour.

*A `--follow` seeder restart across a backwards local clock step fails on the primary key.* The
seeder's follow mode generates rows from the local wall clock, so a run restarted after the clock
has stepped back regenerates timestamps an earlier run already wrote, and the `COPY` is refused by
`public.trends`'s primary key. The operator sees the seeder exit with a duplicate-key error and no
row written; the archive itself is untouched. It affects the bench only — the SCADA is what writes a
production archive.

## Verify it yourself

Run before shipping. Every check names the commit it fails at, so a green result is read rather than
assumed.

**1. The tree, at HEAD.**

```powershell
dotnet build SemiPlot.slnx -c Release
dotnet format SemiPlot.slnx --verify-no-changes
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj
dotnet test SemiPlot/SemiPlot.Tests.Journeys/SemiPlot.Tests.Journeys.csproj
```

0 warnings and 0 errors; exit 0; then 362, 483 and 4 passed, each 0 failed and 0 skipped. A skip in
either gated suite means the container runtime is missing, not that the suite is green.

**2. The fabricated break — the defect this review found.**

```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj `
  --filter "FullyQualifiedName~Coordinator_RealtimeTimestampsOnlyOnePenSampled_BreakNeitherPen"
```

Passes at HEAD. Check out `faafad5`, copy the test in and it fails: pen 1's `CenterPoints` come out
`[1, NaN, 2, NaN, 3, NaN]` — one `NaN` at every timestamp only pen 2 sampled. The journey half is
`LiveEdgeArchiveJourneyTests.RowsOnAVariableOfTheirOwnReachTheChartWithoutBreakingAnyPen`, which
writes 8 pens on 8 distinct seconds in one `COPY`. Both are new at `6e1056c`.

**3. The fresh tail no longer pays for a pen that keeps nothing.**

```powershell
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~APenWithNoCoarseRowDoesNotCostAFreshPenATailRead"
```

Passes at HEAD, fails at `07b8c11` through `40fe232`: one pen with no coarse row pulled `tailStart`
down to the clamp, so a `Day`-layer window read 24 h of raw rows on every query and then discarded
them. `FreshTailBoundTests` is 14 methods / 26 cases at HEAD, 17 cases at `07b8c11`.

**4. The banner says what to do about the fault.**

```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj `
  --filter "FullyQualifiedName~ArchiveConnectionMessage_OnAFault_CarriesTheStateAndItsRemedy"
```

The case asserts the rendered row differs from `IError.Message` and carries the remedy. New at
`b903229`; before it the row was the error's own message and ended without a next step.

**5. A stalled tick raises the banner in seconds, not in minutes.**

```powershell
$env:SEMIPLOT_REQUIRE_DB="1"
dotnet test SemiPlot/SemiPlot.Tests.Data/SemiPlot.Tests.Data.csproj `
  --filter "FullyQualifiedName~ATicksStatementCarriesItsOwnBoundAndNotTheDataSourceBackstop"
```

Two rows, baseline and poll, each asserting `CommandTimeout` 10 off a command the shipped path made.
New at `263b58d`; before it a tick inherited `ArchiveDataSource`'s five-minute backstop.

**6. The one check no automated test covers: the curve advancing on screen.**

The acceptance run proved the application reads the writer's rows at the writer's cadence — the
debug log carries one poll line per second and `max(t)` moved while it read — but nobody watched a
window.
This one needs a person at the screen.

1. Raise the bench and seed it exactly as `docs/architecture/bench.md` states under *The application
   bench*: `--end 2026-08-01T00:00:00 --days 1 --pens 8 --seed 1` into `semiplot_app`.
2. Start the writer and leave it running:
   ```powershell
   dotnet run --project SemiPlot/SemiPlot.Tools.ArchiveSeeder/SemiPlot.Tools.ArchiveSeeder.csproj -- `
     --connection "Host=localhost;Port=55432;Database=semiplot_app;Username=scada_writer;Password=<writer>" `
     --follow 1 --pens 8 --seed 1 --change-seconds 5
   ```
   Wait for the first `appended 16 rows up to ...` line. At `--change-seconds 5` four ticks in five
   append nothing, so a screen watched before that line shows a static chart for a good reason.
3. Point `archive-connection.yaml` at `semiplot_app` on `localhost:55432` as `semiplot_reader`, with
   `source_time_zone` naming **this machine's** zone and `poll_interval_ms: 1000`. The writer writes
   the machine's local wall clock; a mismatched zone puts the live edge one offset away from where
   the viewer looks for it.
4. Start the viewer with the log turned up:
   ```powershell
   dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- `
     --config-dir <dir> --log-file <dir>\semiplot.log --logging-level debug
   ```
5. Add every pen, leave the window on the extent the chart seeds itself with, and confirm **Sticky**
   is on in the toolbar.
6. Watch for one minute. What must be true:
   - the right-hand edge of every curve steps right roughly once a second, all eight together;
   - the window's right bound moves with it — the axis labels advance, rather than the curve
     compressing into a fixed window;
   - no curve draws a segment running backwards, and no gap opens that the archive did not record;
   - no banner row appears over the chart;
   - the log carries one *"The realtime poll read N rows past ..."* line per second.

A chart that stops advancing while the log keeps reading rows is the fall-back limitation named in
**Post-Completion**, not a regression — check the machine's local clock before filing anything.

**Executed by exec:**

- branch: postgres-live-edge-and-demo

**The 16 tasks and their commits**

| # | Task | Commit |
| --- | --- | --- |
| 1 | Retire `SemiPlot.DataSource.Stub` | `46ee54e` refactor(data): retire the stub data source |
| 2 | `RealtimePoll` — statements, engine and their pins | `eca9b25` feat(postgres): poll the archive for samples past the last seen |
| 3 | `PostgresDataProvider.Subscribe` over the engine | `bf4cfed` feat(postgres): serve the live edge from Subscribe |
| 4 | `ArchiveConnectionState` on the `IDataProvider` seam | `683dd5c` feat(core): report the archive connection state |
| 5 | The archive-status banner | `61e7e1c` feat(ui): show the archive status in the main window |
| 6 | `ArchiveShapeUnexpectedError` on the `42703` arm | `1876c88` feat(core): name an unexpected archive column shape |
| 7 | Archive health and the default-partition warning | `c71d053` feat(startup): warn when the default partition holds rows |
| 8 | The seam guard in `TrendPenState.AppendRealtime` | `1fa3f8b` fix(chart): keep the history-to-realtime seam monotonic |
| 9 | The fresh tail | `07b8c11` feat(postgres): fill the coarse window's tail from the raw layer |
| 10 | Appending in `ArchiveWriter`, idempotent partitions | `5d6ac9c` feat(seeder): append rows into a seeded archive |
| 11 | The `--follow` demo writer | `e2001b1` feat(seeder): grow the archive on a wall-clock cadence |
| 12 | The journey project and its CI job | `39640cb` build(tests): add the end-to-end journey project |
| 13 | Journey — a seeded break renders as a broken line | `3aca997` test(journeys): render a seeded break as a broken line |
| 14 | Journey — a live insert arrives exactly once | `faafad5` test(journeys): deliver a live insert exactly once |
| 15 | Verify acceptance criteria | `42d3870` test: record the acceptance evidence |
| 16 | [Final] Update documentation | `73d49a2` docs: describe the live edge and the journey layer |

**Review phases**

Two ran: a three-agent comprehensive pass over the whole branch, then a single-agent critical pass
over the result. The external `codex` phase **did not run** — `codex` is not installed on this
machine, and nothing was substituted in its place.

**What the review changed**

*The one that matters.* A realtime batch appended a `null` for every pen that had no sample at a
given timestamp, and a `null` is how a break is encoded. A real archive writes per variable, on
change, with a deadband, so two variables rarely share a `t` — every pen therefore gained a
fabricated break at every timestamp that was not its own. No test caught it: the demo writer puts
every pen on one shared lattice, and both journeys write one row per variable at a single timestamp.
Fixed by making the `null` unrepresentable rather than merely unused — `PenRealtimeValues` carries
the pen's own `TimestampsUtc` beside a non-nullable `IReadOnlyList<double>` (`6e1056c`).

Then, grouped:

- the banner showed a state with no remedy; both rows now render `StartupFailureMapper.Describe`,
  detail plus remedy (`b903229`);
- the fresh-tail bound took the global minimum seam, so one pen with no rows forced a layer period
  of raw rows — a day at `Day` — on every coarse query, for rows the merge then discarded
  (`7d1e081`);
- the fault report was pinned at its threshold instead of a running count, which would have meant
  re-raising the state, and rewriting the banner, on every failed tick (`999074a`);
- the poll tick inherited `ArchiveDataSource`'s five-minute backstop and now carries its own 10 s
  bound, so a server that accepts connections and then stops answering raises the banner in half a
  minute rather than fifteen (`263b58d`);
- five test gaps the reviewers proved reachable: the fault-to-success crossing, a subscription over
  an entirely empty archive, per-subscription `Connected` identity, the dropped-subscription proof
  made non-vacuous by a control subscription, and the fresh-tail bound's own rows (`40fe232` and the
  rows added with each fix above);
- a long list of stale documentation, every count recounted from the code rather than carried
  forward: Core error types, `ArchiveStatements` constants and their pins, binders, the `EXPLAIN`
  cases, the journey tests, the test projects (`9d0426a`, `57eb502`).

**Declined or recorded rather than fixed**

Both are clock behaviour and both are named limitations in **Post-Completion** above.

- *The live edge stalls for the repeated hour at the autumn fall-back.* `t > @lastSeen` compares
  naive local time, so the second pass over a repeated hour carries timestamps the poll has already
  seen. Not fixed: the fix belongs to whatever writes the archive, the reads succeed throughout so
  nothing is silently lost, history over the same range is unaffected, and the edge resumes on its
  own at the end of the hour.
- *A `--follow` restart across a backwards local clock step fails on the primary key.* The seeder
  regenerates timestamps an earlier run already wrote and `public.trends` refuses the `COPY`. Bench
  only — the SCADA is what writes a production archive.

**Final measured numbers at HEAD**

| Check | Result |
| --- | --- |
| `dotnet build SemiPlot.slnx -c Release` | 0 warnings, 0 errors |
| `dotnet format SemiPlot.slnx --verify-no-changes` | exit 0 |
| `SemiPlot.Tests` | 362 passed, 0 failed, 0 skipped |
| `SemiPlot.Tests.Data`, `SEMIPLOT_REQUIRE_DB=1` | 483 passed, 0 failed, 0 skipped |
| `SemiPlot.Tests.Journeys`, `SEMIPLOT_REQUIRE_DB=1` | 4 passed, 0 failed, 0 skipped |

The critical pass returned **NO CRITICAL FINDINGS**.

**Pending for a separate default-branch change, after this pull request merges**

Neither may ride on this branch: a roadmap edit never travels on a slice's feature branch, so both
were reverted off it at `7ab57fe`, and `docs/plans/roadmaps/` is byte-identical to `master` at HEAD.

1. Stamp the slice `postgres-live-edge-and-demo` **DONE** in
   `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`, with the merged pull request
   number and the archived plan path `docs/plans/completed/20260824-postgres-live-edge-and-demo.md`.
2. Correct that roadmap's close conditions: "both test projects" is now **all three test projects**,
   `SemiPlot.Tests.Journeys` having landed in this slice.
