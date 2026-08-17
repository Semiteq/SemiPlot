# SemiPlot.DataSource.Postgres — read-only provider over the Simple-Scada archive

## Overview

Implement `IDataProvider` against the Simple-Scada 2 PostgreSQL archive as a sibling
`SemiPlot.DataSource.Postgres` project, replacing `RandomStubDataProvider` in production while the
stub stays for tests and demos. This satisfies `trend-feature-spec.md` §DA-1.

The design is settled and documented; this file holds only the work.

| What | Where |
| --- | --- |
| The archive as it exists — tables, layers, quality marks, hazards | `docs/architecture/scada-archive.md` |
| The contract — responsibility zones, `IDataProvider`, exact SQL, layer ladder, error semantics | `docs/architecture/data-integration.md` |
| The database instance — configuration, roles, our objects, provisioning | `docs/architecture/postgres-instance.md` |
| Why there are no summary tables or maintenance service | `docs/architecture/history-read-path-evaluation.md` |
| Citation registry | `docs/architecture/sources.md` |

Two facts govern every task below and are easy to get wrong: every query must constrain `l`, or
points return up to four times; and every query must carry the variable list, or it cannot use
`PRIMARY KEY (id, l, t)` and scans a whole partition.

## Superseded

**This plan is superseded by `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`**, which
slices the same work into independently shippable branches. Read the roadmap first; use this file only
for the archive facts above and the SQL detail, and treat its task list as stale. Three specifics are
known wrong:

- Task 4 says "load `Pens` at startup". `IDataProvider.Pens` no longer exists — the catalogue is
  `Task<Result<IReadOnlyList<Pen>>> QueryPensAsync()` (slice `provider-pen-query-seam`). Whether an
  empty or missing `semiplot_tags` is an empty success or a typed failure is `postgres-catalog-and-extent`'s
  open question, not settled here.
- Task 4 also creates `sql/semiplot_tags.sql`. The table is created by `semibase create`, so the DDL
  is not this repository's to own.
- Every task puts its tests under `SemiPlot.Tests/Data/...`. Data-source tests live in the separate
  `SemiPlot.Tests.Data` project, which owns the gated database harness.

## Development approach

- Regular: implement, then add or update tests in the same task.
- Pure logic — time conversion, layer selection, statement construction, envelope assembly — plain
  `[Fact]` unit tests, no database.
- Database-touching tests — `[Trait("Category","Integration")]`, `[Trait("Area","Data")]`, gated on a
  reachable test database and skipping cleanly when absent, so the default suite stays green.
- All tests live in the single `SemiPlot.Tests` project per `CLAUDE.md`.
- Complete each task fully; non-integration tests green before the next.

## Testing strategy

- **Unit:** time-zone round trips; `LayerForWidth` thresholds and hysteresis; generated statement
  text and parameter names pinned per operation; envelope assembly including gap anchors.
- **Integration (gated):** against a disposable database seeded with the exact `trends` shape —
  pens from `semiplot_tags`, extent, history per layer, gap reconstruction, realtime append,
  dropped connection yielding a failed `Result`. Plus `EXPLAIN` assertions that the windowed history
  query and the realtime poll use `tpk`.
- No UI test changes: the provider sits below the view-model seam, which uses `FakeDataProvider`.

## Progress tracking

Mark completed items `[x]`; add ➕ for new tasks and ⚠️ for blockers; keep this file in sync.

## Implementation steps

### Task 1: Scaffold the project and the DI seam

**Files:** create `SemiPlot/SemiPlot.DataSource.Postgres/SemiPlot.DataSource.Postgres.csproj`,
`PostgresDataProvider.cs`, `DataSourceServiceCollectionExtensions.cs`; modify
`SemiPlot/Directory.Packages.props`, `SemiPlot.slnx`.

- [ ] project references `SemiPlot.Core` only — no UI, no Stub
- [ ] add `Npgsql` through central package management
- [ ] `PostgresDataProvider` implements `IDataProvider` with not-implemented bodies, settings and
      logger injected through the constructor
- [ ] `AddPostgresData(this IServiceCollection)` registers `IDataProvider` as a singleton
- [ ] smoke test: the provider type resolves from the container
- [ ] tests pass

### Task 2: Connection configuration

**Files:** create `Configuration/PostgresConnectionSettings.cs`, `PostgresConnectionDto.cs`,
`PostgresConnectionLoader.cs`; modify `Directory.Packages.props`; create
`SemiPlot.Tests/Data/Postgres/PostgresConnectionLoaderTests.cs`.

- [ ] settings record: host, port, database, username, password, source time zone, poll interval,
      schema, statement timeout, plus a file-version field
- [ ] YAML DTO with underscored member names; loader returns `Result`, validates the version
- [ ] build the connection string with `NpgsqlConnectionStringBuilder`, never by concatenation
- [ ] tests: valid file, missing file, bad version, missing fields
- [ ] tests pass

### Task 3: Time boundary

**Files:** create `ArchiveTimeConverter.cs` and its tests.

- [ ] naive local `t` interpreted in the configured zone → `DateTime(Kind = Utc)`
- [ ] UTC window bounds → naive local for query parameters
- [ ] resolve the `TimeZoneInfo` once; .NET 10 accepts IANA identifiers on Windows
- [ ] tests: round trip, a known instant, documented daylight-saving behaviour
- [ ] tests pass

### Task 4: `semiplot_tags` and the pen catalogue

**Files:** create `sql/semiplot_tags.sql`; modify `PostgresDataProvider.cs`; create
`SemiPlot.Tests/Data/Postgres/PostgresPensTests.cs`.

- [ ] DDL per `postgres-instance.md`
- [ ] load `Pens` at startup; an empty or missing table yields an empty list, not a failure
- [ ] map `line_style` to `PenLineStyle`
- [ ] gated integration test: seeded tags produce the expected pens
- [ ] tests pass

### Task 5: Archive extent

**Files:** modify `PostgresDataProvider.cs`; create `PostgresExtentTests.cs`.

- [ ] per-variable bounded subqueries as specified in `data-integration.md` — a bare `min(t)` over
      the whole table is a full scan and must not be used
- [ ] empty archive yields an empty extent; connection error yields a failed `Result`
- [ ] gated integration tests for all three cases
- [ ] tests pass

### Task 6: History — direct layer read

**Files:** modify `PostgresDataProvider.cs`; create `ArchiveSql.cs` and `ArchiveSqlTests.cs`,
`PostgresHistoryTests.cs`.

- [ ] all statement text lives in `ArchiveSql` and nowhere else
- [ ] windowed read `WHERE id = ANY(@ids) AND l = @layer AND t >= @from AND t < @to ORDER BY id, t`
- [ ] rows folded into `PenHistoryEnvelope` through the existing decimator, timestamps converted at
      the boundary, strictly ascending
- [ ] unit tests pin statement text and parameters; gated integration test over a seeded window
- [ ] tests pass

### Task 7: History — server-side reduction to pixel columns

**Files:** modify `ArchiveSql.cs`, `PostgresDataProvider.cs`; extend both test files.

- [ ] `date_bin` bucketing with the window start as origin, returning at most one row per column per
      pen, carrying minimum, maximum, first, last, edge timestamps, edge qualities and break count
- [ ] the provider chooses between this and the direct read by the expected row count
- [ ] unit tests on the generated statement; gated integration test comparing bucketed output against
      the same window read raw
- [ ] tests pass

### Task 8: Layer ladder

**Files:** modify `SemiPlot.Core/Trends/AggregationLayer.cs` and the layer-selection model; update
their tests.

- [ ] point spacing becomes period ÷ 4 — 15 s, 15 min, 6 h — replacing the current period-valued
      `ToSampleInterval`, which makes every threshold four times too conservative
- [ ] select the coarsest layer satisfying `window / targetColumnCount ≥ spacing`, with 10% hysteresis
- [ ] fresh-tail patch: the segment newer than the coarse layer's newest row is read from `l = 0`
      and concatenated at the seam
- [ ] tests: thresholds for several column counts, no chatter across a boundary, seam continuity
- [ ] tests pass

### Task 9: Gaps and quality

**Files:** modify the envelope assembly; create `GapReconstructionTests.cs`.

- [ ] `q = 32` inserts a NaN anchor after the sample; `q = 16` resumes the line
- [ ] absence of rows without a preceding `q = 32` renders as a horizontal continuation, not a break
- [ ] the same reconstruction from bucketed rows via `q_first`, `q_last`, `breaks`
- [ ] tests over both paths, including a break spanning several buckets
- [ ] tests pass

### Task 10: Realtime

**Files:** modify `PostgresDataProvider.cs`; create `PostgresRealtimeTests.cs`.

- [ ] cold observable polling `WHERE id = ANY(@ids) AND l = 0 AND t > @lastSeen ORDER BY t` on the
      injected data scheduler; disposal stops it
- [ ] a query error logs and drops the tick; it never throws on the UI thread and never terminates
      the observable
- [ ] never emit a timestamp at or before the last delivered one (§DA-7)
- [ ] gated integration test: appended rows arrive once, in order, without duplicates
- [ ] `EXPLAIN` assertion that the poll uses `tpk`
- [ ] tests pass

### Task 11: Startup compatibility probe

**Files:** modify `PostgresDataProvider.cs`; create `SchemaProbeTests.cs`.

- [ ] on startup verify the column shape of `trends` against `information_schema`
- [ ] distinguish and report: no connection, no `trends`, unexpected shape, `tpdefault` not empty
- [ ] each condition maps to the operator-facing state listed in `data-integration.md`
- [ ] tests pass

### Task 12: Development seeder

**Files:** create `SemiPlot.Tools.ArchiveSeeder/` (console, excluded from release);
modify `SemiPlot.slnx`; create `sql/semiplot_dev.sql`.

- [ ] `semiplot_dev.sql` reproduces the verified `trends` shape: range partitioning by day, primary
      key `(id, l, t)`, `timestamp(3) without time zone`, plus `semiplot_tags`
- [ ] backfill writes raw samples through `COPY`, reusing the stub's synthetic generators
- [ ] the seeder also writes plausible coarse layers and gap markers, so the layer ladder and gap
      reconstruction can be exercised without a SCADA
- [ ] live mode appends samples so realtime polling has something to see
- [ ] dry-run self-check on the generated rows, no database needed
- [ ] tests pass

### Task 13: Composition

**Files:** modify `SemiPlot.UI/Program.cs` and the composition extensions; create
`PostgresCompositionTests.cs`.

- [ ] a valid connection file under `--config-dir` selects the Postgres provider, otherwise the stub
- [ ] a malformed file logs loudly and falls back to the stub rather than crashing
- [ ] DI test for both branches
- [ ] tests pass

### Task 14: Verify acceptance criteria

- [ ] §DA-1 history and extent from PostgreSQL; a dropped connection yields a failed `Result`
- [ ] §DA-2, §DA-5 a coarse window returns far fewer rows and preserves spikes
- [ ] §DA-3 layer transitions at the computed thresholds
- [ ] §DA-7 realtime never emits at or before the last history timestamp
- [ ] §DA-8 marked breaks split the line; long unchanged runs do not
- [ ] §RT-1, §RT-2 realtime arrives in batches and sticky still follows
- [ ] `dotnet test SemiPlot.slnx` — integration tests skip cleanly without a test database
- [ ] `dotnet format SemiPlot.slnx`

### Task 15: Documentation

- [ ] reconcile the architecture docs with what was actually built, where they differ
- [ ] record the measured write rate and the chosen retention depth in `postgres-instance.md`,
      replacing the `UNDECIDED` markers
- [ ] move this plan to `docs/plans/completed/`

## Post-completion

External or manual, no checkboxes.

**Confirm the layer selection rule.** Run the experiment described at the end of
`scada-archive.md` on a real installation and record the outcome there. If it refutes extreme
preservation, reopen `history-read-path-evaluation.md` at the option recorded as the fallback.

**Commissioning measurements.** Write rate overall and per variable; decide the retention depth and
the disk size from them; tune the archiving interval and deadband of the few variables that dominate
the stream.

**Fill `semiplot_tags`** with the real variable list, groups, units and colours.

**Instance provisioning** on the operator machine per `postgres-instance.md`, including the
read-only role and the connection file.
