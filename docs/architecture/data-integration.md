# Data integration: the boundary between SemiPlot and the SCADA

This document defines the contract: who owns what, what SemiPlot asks the database and the SCADA's
OPC UA server, and what it does with the answers. The archive itself is described in
`scada-archive.md`; the instance SemiPlot reads is in `postgres-instance.md`; the OPC UA server as
measured is in `scada-archive-observed.md#the-opc-ua-server`. Requirement identifiers (`DA-`, `RT-`)
refer to `trend-feature-spec.md`. Claim provenance follows `sources.md`.

There is no application server. The desktop client connects to PostgreSQL for history, the extent,
the catalogue, the markers and the export, and to the SCADA's built-in OPC UA server for the live
edge `[DEC:opc-live-edge]`. This document and the code of the two data-source projects are the entire
integration surface.

## Responsibility zones

| Concern | Simple-Scada | SemiPlot | Neither — we add it |
| --- | --- | --- | --- |
| Schema of `trends` / `messages`, partition creation, writes, thinning | owns | reads only | |
| Executing retention (deleting old partitions) | owns | | |
| Choosing the retention depth | setting lives in the SCADA project | decision is ours `[DEC:common-retention]` | |
| PostgreSQL instance: installation, configuration, roles, backup, upgrade | client of it | client of it — provisioned by SemiBase, see `postgres-instance.md` | |
| Variable number to name mapping | absent | | `semiplot_tags` `[DEC:semiplot-tags]` |
| Knowledge of the archive's time zone | not stored anywhere | takes the machine's zone `[DEC:machine-time-zone]` | |
| Layer choice, decimation, gap rendering, envelope assembly | | owns | |
| Realtime freshness | the OPC UA server's sampling and publishing; the archive's write and flush cadence | the subscription plan: 100 ms publishing and sampling, queue 1 | |

Two rules follow and are not negotiable: SemiPlot never writes to vendor objects
`[DEC:read-only-consumer]`, and every additive object is prefixed `semiplot_`
`[DEC:additive-objects]`.

## The provider surface

The UI reads through `IDataProvider` (`SemiPlot.Core/Data/IDataProvider.cs`), the chart's live edge
follows `ILiveSource` (below), and two write paths sit apart from both: the pen editor writes through
`IPenCatalogueEditor` (The pen catalogue editor, below), and the chart's time markers are read and
written through `IMarkerStore` (The marker store, below). The interfaces and their DTOs live in
`SemiPlot.Core`; each concrete provider is a sibling `SemiPlot.DataSource.*` project, so Core never
references a data source. `PostgresDataProvider` in `SemiPlot.DataSource.Postgres` is the only
implementation of `IDataProvider`; tests build fakes against the interface.

```csharp
public interface IDataProvider
{
    Task<Result<IReadOnlyList<Pen>>> QueryPensAsync();

    Task<Result<IReadOnlyList<PenHistoryEnvelope>>> QueryHistoryAsync(
        IReadOnlyList<int> penIds,
        DateTime fromUtc,
        DateTime toUtc,
        AggregationLayer layer,
        int targetColumnCount,
        CancellationToken cancellationToken = default);

    Task<Result<ArchiveExtent>> QueryArchiveExtentAsync();

    // Archive wall-clock days, ascending: the days in [firstDay, lastDay] that hold a raw row of a catalogue pen.
    Task<Result<IReadOnlyList<DateOnly>>> QueryArchiveDaysAsync(
        DateOnly firstDay,
        DateOnly lastDay,
        CancellationToken cancellationToken = default);
}
```

| Type | Shape | Notes |
| --- | --- | --- |
| `Pen` | `PenId`, `Name`, `Groups`, `Color`, `Unit`, `Format`, `EnabledOnStart`, `ScaleMinOnStart`, `ScaleMaxOnStart`, `LogScaleOnStart`, `LineStyle` | `PenId` is the archive's `trends.id`. Equality compares `Groups` element by element, ordinal, so two reads of one stored pen are equal; `LogScaleOnStart` takes part in it, so a pen whose flag alone changed is revised. |
| `Sample` | `PenId`, `TimestampUtc`, `Value` | Realtime element. Timestamps are UTC by the time they leave the live source. A null `Value` is a break the live source reported: the pen has no data from that moment until its next sample. |
| `PenHistoryEnvelope` | parallel `Timestamps` / `Min` / `Max` / `Center`, strictly ascending, `NaN` marks a gap | One per pen per history query. |
| `ArchiveExtent` | `FirstUtc`, `LastUtc`, `IsEmpty` | The span of the configured variables, consumed by the minimap (`TM-4`). `ArchiveExtent.Empty` is the no-span form. |
| `AggregationLayer` | `Raw`, `Minute`, `Hour` | Maps one-to-one onto the archive's `l` column. |
| `ArchiveConnectionState` | `Fault`, `IsConnected` | `Fault` is null while the archive answers and carries the typed error while it does not. No member of `IDataProvider` returns it: `PenCatalogueSync` derives it from its catalogue reads (The connection state the catalogue read reports, below). |

Contract points every implementation keeps:

- `QueryHistoryAsync`'s window is half-open — `fromUtc` inclusive, `toUtc` exclusive. The order of
  the envelopes is unspecified; consumers key by `PenId`.
- A pen with neither a window row nor a seed row gets no envelope rather than an empty one.
  `TrendChartViewModel.ApplyHistory` clears the curve of every requested pen the result omits.
- Every read answers with a `Result`. Only `OperationCanceledException` leaves the provider as an
  exception, which `ArchiveExceptionMapper` rethrows rather than mapping. Nothing else crosses to the
  UI thread (`DA-1`).
- The provider checks no argument of `QueryHistoryAsync`; its callers keep the invariants. The layer
  always comes from `LayerForWidth`, never from a cast. The column count is
  `HistoryColumnTarget.FromDataAreaWidth`'s clamp to 256…2048 times the fetched range's
  `FetchRange.ColumnFactor`, one per window the range spans, or the minimap's constant 250. The window keeps `fromUtc` before `toUtc`: `TrendNavigationModel` holds it at
  least one second wide, and the minimap's bounds are `min(t)` and `max(t)` of the archive.
- A history read whose `cancellationToken` is cancelled ends in `OperationCanceledException`, never in
  a failed `Result`. The chart treats only that exception, while its own token is cancelled, as its
  own cancellation.
- `QueryArchiveDaysAsync` takes both days inclusive and answers in the archive's wall-clock days, the
  local days its partitions cut, with no time conversion (Time boundary, below). A day "holds a row" when
  at least one `l = 0` row of a catalogue pen is dated that day; a partition that exists is no evidence,
  because a partition is created ahead of its day and outlives a stopped server. The time window panel and
  the export window call it, both through `TrendCoordinator.QueryArchiveDaysAsync`
  (`trend-interaction.md#the-time-window-panel`, `overview.md#the-export-window`). A cancelled read ends in
  `OperationCanceledException`, as a history read does.

The CSV export reads through a third interface, `IArchiveRowReader`
(`SemiPlot.Core/Data/IArchiveRowReader.cs`), implemented by `PostgresArchiveRowReader` and registered by
`AddPostgresData` beside the editor over the same `NpgsqlDataSource`. Its consumer is the export window
alone, and its contract differs from `IDataProvider`'s: stored rows, not envelopes, in the archive's
wall-clock time (Time boundary, below).

| Member | Answers |
| --- | --- |
| `CountRowsAsync(penIds, from, to)` | The `l = 0` rows of the pens inside `[from, to)` plus one seed row per pen that has one, as a `long`. |
| `ReadSeedRowsAsync(penIds, from)` | Per pen its last `l = 0` row before `from`, not older than one day, ordered by time with ties in `penIds` order, as `StoredArchiveRow(PenId, Time, Value)` structs. |
| `ReadSliceAsync(penIds, from, to)` | Every `l = 0` row of the pens inside `[from, to)` in one statement, ordered by time with ties in `penIds` order, as the same structs. No decimation. |

A row carries its pen's id because a slice mixes pens, and no quality: the file has no `q` column. `penIds`
holds distinct catalogue ids (`semiplot_tags.id` is the primary key); the count probes seeds over
`DISTINCT unnest(@ids)`, so a repeated id would make the count and the file differ.

Both bounds are naive wall-clock values, `from` inclusive and `to` exclusive. A read answers with a `Result`;
only `OperationCanceledException` leaves the reader as an exception, ahead of `ArchiveExceptionMapper`, as
in `QueryHistoryAsync`. A failure maps with the relation `trends`. `ReadSeedRowsAsync` runs one `ExportSeedRow`
probe per pen on one connection, then sorts the collected rows by time with a stable sort, so equal times keep
the `penIds` order. `ReadSliceAsync` runs one statement, reads every row into a list and returns it after the
reader closes, so the statement's duration depends on the server alone and never on the disk the file goes to.

`ArchiveRowSlices` (`SemiPlot.Core/Export/ArchiveRowSlices.cs`) holds the slice `Width`, 15 minutes, and `Tile`,
which cuts `[from, to)` into consecutive `[a, min(a + Width, to))` slices with no gap and no overlap. At the
density floor (a pen writes at most 10 rows per second, `scada-archive.md`) a 15 minute slice is 144 000 rows
for 16 pens and 450 000 for 50, which the reader holds as one list. Measured on 2026-10-14 over a day of
50 pens at 100 ms (43.2 million rows), a 50 pen slice took 0.36 s cold and 0.63 s warm, far under the role's
30 s `statement_timeout`, so the width stayed at 15 minutes. The export reads one such slice at a time; a 24 h
range is 96 slice statements plus one seed probe per pen.

The live edge reads through a port of its own, `ILiveSource` (`SemiPlot.Core/Data/ILiveSource.cs`), with
three members. `OpcUaLiveSource` in `SemiPlot.DataSource.OpcUa` implements it over one OPC UA session to the
SCADA's built-in server, and `AddOpcUaLiveSource` registers it as the singleton `ILiveSource`. It reads no
database, so `SemiPlot.DataSource.OpcUa` references Core only, never the Postgres project. Its one consumer
in the UI is `TrendCoordinator`, which `TrendWindow.Build` hands both interfaces. The contract, the source,
and what the coordinator does with a source that breaks it are under Realtime, below.

```csharp
public interface ILiveSource
{
    IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<int> penIds);

    // Hot, replays the current state to each subscriber, never faults or completes before disposal.
    IObservable<LiveSourceState> States { get; }

    // Hot, one error per pen fault, never faults or completes before disposal.
    IObservable<LiveSourceError> PenFaults { get; }
}
```

| Type | Shape | Notes |
| --- | --- | --- |
| `LiveSourceState` | `IsConnected`, `Fault` | `SemiPlot.Core/Data`. `Connecting` (not connected, no fault) is the state before the first session; `Connected` while a session is up; `FaultedBy(error)` while no session is up and the source knows why. |
| `LiveSourceError` | `Kind` (`LiveSourceFault`), `Endpoint`, `UnknownPenIds`, `NotNumericPenIds` | `SemiPlot.Core/Data/Errors`. `Unreachable`, `EndpointRefused` and `ProjectMissing` ride on `States`; `PensUnserved` rides on `PenFaults`. The stack's status reaches the log only, on the source's own state line. |

## The pen catalogue editor

The write path is a second interface, `IPenCatalogueEditor` (`SemiPlot.Core/Data/IPenCatalogueEditor.cs`),
and `IDataProvider` stays read-only. The interface declares everything the editor window may do and
nothing more:

```csharp
public interface IPenCatalogueEditor
{
    Task<Result<PenCatalogue>> ReadAsync();
    Task<Result<int>> RegisterNewPensAsync();
    Task<Result> ChangeAsync(StoredPen pen, PenSettingChange change);
    Task<Result<int>> CreateGroupAsync(string name);
    Task<Result> RenameGroupAsync(StoredGroup group, string name);
    Task<Result> DeleteGroupAsync(StoredGroup group);
    Task<Result> SetMembershipAsync(StoredPen pen, StoredGroup group, bool isMember);
}
```

No member adds a pen by hand, deletes one or changes an `id`: the key is the SCADA variable number and
belongs to the SCADA. The `semiplot` role draws the same line, with a column-level `UPDATE` of the eight
settings columns and no `INSERT` or `DELETE` on `semiplot_tags` (`postgres-instance.md`).
`PenCatalogueEditorSurfaceTests` pins the seven method names.

The read interface stays read-only because the chart consumes it. A write member on `IDataProvider`
would put a write in reach of every chart consumer; with the write paths apart, `Chart/` and `Legend/`
name neither `IPenCatalogueEditor` nor `IMarkerStore`, `IDataProvider` carries no `Insert`, `Update`,
`Write` or `Save`, and greps in `AGENTS.md` hold both. The chart writes time markers only, through its own
port `Chart/IMarkerChannel` (The marker store, below). An axis the operator rescales stays a session edit
(`charting.md`).

`PostgresPenCatalogueEditor` in `SemiPlot.DataSource.Postgres` is the only implementation.
`AddPostgresData()` registers it by factory beside `IDataProvider`, over the same `NpgsqlDataSource`
singleton, so one role and one connection string serve reads and writes.

| Type | Shape | Notes |
| --- | --- | --- |
| `PenCatalogue` | `Pens`, `Groups` | The catalogue as stored. |
| `StoredPen` | `Id`, `Name`, `Unit`, `Format`, `Color`, `LineStyle`, `EnabledOnStart`, `ScaleMinOnStart`, `ScaleMaxOnStart`, `LogScaleOnStart` | One `semiplot_tags` row; a null field is a stored `NULL`. |
| `StoredGroup` | `Id`, `Name`, `MemberPenIds` | Member ids ascending; the member count is `MemberPenIds.Count`. |
| `PenSettingChange` | `Name`, `Unit`, `Format`, `Color`, `LineStyle`, `EnabledOnStart`, `ScaleOnStart(Min, Max)`, `LogScaleOnStart` | A closed family, one arm per column; the scale pair is one arm. |

A parameter record carries the id the statement binds and the name a fault names. The implementation
reads only `Id` and `Name` from it, and no statement compares old values in its `WHERE`.

### The stored read

`ReadAsync` runs `StoredPens` and `StoredGroups` on one connection, with `PenCatalogRelations` as the
failure detail. It returns the values as stored, not as `PostgresDataProvider.ReadPen` normalises them:
a `NULL` colour stays null instead of `#808080`, and a mask `PenValueFormat.IsAcceptable` refuses comes
back as written instead of null. The form opens such a value as an invalid field, the way the settings
window opens a file value its rule refuses (`overview.md#the-settings-window`). `line_style` goes
through `StoredLineStyle.Read`, which the provider's read calls too; a value it does not recognise reads
as interpolated and is logged at the level the caller's function gives, asked only for such a value.
The editor gives `Warning` on every read, and the provider gives `Warning` once per pen and stored value
and `Debug` after that (`#the-catalogue-while-the-viewer-runs`, below).

The editor reads fresh when its window opens and after every refresh, never from the startup
catalogue: a form seeded from the startup read would revert a field to a value another instance has
already replaced.

### One setting, one column, one statement

| Operation | Constant | What the statement keeps |
| --- | --- | --- |
| `ChangeAsync`, every arm but `ScaleOnStart` | `UpdatePenName`, `UpdatePenUnit`, `UpdatePenFormat`, `UpdatePenColor`, `UpdatePenLineStyle`, `UpdatePenEnabledOnStart`, `UpdatePenLogScaleOnStart` | `UPDATE semiplot_tags SET <column> = @value WHERE id = @id`, the column fixed in the constant and never interpolated. A null unit or mask binds `NULL`; the form turns an empty field into null. |
| `ChangeAsync`, `ScaleOnStart` | `UpdatePenScaleOnStart` | Both bounds in one statement: `semiplot_tags_scale_paired` refuses a half-set pair between two. |
| `CreateGroupAsync` | `CreateGroup` | `INSERT ... RETURNING id`; the result is the new group's id. |
| `RenameGroupAsync` | `RenameGroup` | One row by id. |
| `DeleteGroupAsync` | `DeleteGroup` | `ON DELETE CASCADE` on `semiplot_pen_groups.group_id` removes the memberships. The pens stay, and a pen left in no group falls under the Ungrouped header at the next catalogue read. |
| `SetMembershipAsync` | `AddMembership`, `RemoveMembership` | Idempotent: adding a membership that exists is `ON CONFLICT (pen_id, group_id) DO NOTHING`, and removing one that does not touches no row and succeeds. |
| `RegisterNewPensAsync` | `RegisterNewPens` | `SELECT semiplot_register_new_pens();`, whose answer is the number of pens added. |

Every update, rename and delete must touch exactly one row. None touched means the pen or group is
gone, and the editor returns `ArchiveFault.RowGone`, never success. A change carries one column, so two
instances editing different settings of one pen never overwrite each other; two writes of the same
column keep the last one. Every parameter is bound with its type and every `NOT NULL` column the
editor writes takes a non-null value, so no statement it issues can raise `23502` or `22P02`.
`ArchiveStatementTextTests` does not pin these constants; `PenCatalogueEditorTests` runs every one of
them as `semiplot` against a container clone, which also proves the grant.

### Registration from the refresh button only

`semiplot_register_new_pens()` is SemiBase's (`sql/semiplot_register.sql`: `SECURITY DEFINER`,
`EXECUTE` for `semiplot` and none for `PUBLIC`). It adds a pen for every key in `trends` that has none:
named by its number, one of twelve colours by `id % 12`, `enabled_on_start = false`, no unit, no mask,
no scale and `log_scale_on_start = false`.

The viewer calls it from one place, `PenEditorViewModel.RefreshCommand`, and never at start:

- A registered pen is hidden and named by its number, so the chart has nothing new to draw after it.
  The operator opens the editor to name and switch the pen on either way.
- The function finds the keys by a loose index scan whose cost grows with keys times partitions.
  SemiBase measures 119.0 ms for 50 keys over 91 partitions and extrapolates, without measuring,
  2.6 s to 4.8 s for 500 keys over 365 partitions (SemiBase `docs/architecture/provisioning.md:294-314`).
  At start, that cost would land on every start.
- A database whose provisioning lacks the function answers `42883`. At start that would fail every
  start; behind the button it fails the one refresh the operator pressed.

Two refreshes from two instances do not conflict. A call started while another is uncommitted waits on
the first call's keys, then skips them under `ON CONFLICT (id) DO NOTHING` and returns 0 with no fault.
`PenRegistrationTests` holds the first call's transaction open from a second connection and checks
exactly that, so the editor takes no advisory lock.

### The catalogue while the viewer runs

The editor writes the tables, and every running viewer reads them again: `Bridge/PenCatalogueSync`
issues `QueryPensAsync` every 5 s and at once after each write the editor lands, and the chart and the
sidebar apply what changed (`overview.md#the-live-catalogue`). Several instances may run on one
machine, and the last write wins per column. The read side keeps three rules for that loop:

- `PenCatalog` orders by `tag.name, tag.id`, so two pens that share a name keep one order across reads.
- `QueryPensAsync` builds a new group list per row, and `Pen` compares `Groups` element by element, so
  an unchanged catalogue reads as unchanged.
- `PostgresDataProvider` warns about a stored value it normalises, a `NULL` colour, a mask the rule
  refuses or an unknown `line_style`, once per process for each pen, column and stored value, and logs
  a repeat at `Debug`. The provider keeps the set of warned triples. A hand-edited row would otherwise
  write a warning every 5 s.

A read that adds or removes a pen moves the live edge onto the new pen set (Realtime, below).

## The marker store

A time marker is a vertical line the operator places on the chart at a moment
(`trend-interaction.md#the-time-markers`). It is one row of `semiplot_markers`, a SemiBase table:

| Column | Type | Use |
| --- | --- | --- |
| `id` | `bigint GENERATED ALWAYS AS IDENTITY PRIMARY KEY` | The marker's key; the server assigns it |
| `t` | `timestamptz NOT NULL` | The marker's moment, an exact instant (Time boundary, below) |
| `label` | `text CHECK (char_length(label) BETWEEN 1 AND 80)` | Optional: no label is `NULL`, and the check refuses `''` |

The table has no index, because every read takes all of it. It is not partitioned and is outside any
retention, so a marker outlives the day partition it points into. SemiBase v0.6.0 provisions it and grants
`semiplot` `SELECT, INSERT, UPDATE, DELETE` on it (`postgres-instance.md#the-semiplot-role`).

```csharp
public interface IMarkerStore
{
    Task<Result<IReadOnlyList<StoredMarker>>> ReadAsync(CancellationToken cancellationToken = default);
    Task<Result<long>> AddAsync(DateTime timeUtc, string? label);
    Task<Result> MoveAsync(StoredMarker marker, DateTime timeUtc);
    Task<Result> RenameAsync(StoredMarker marker, string? label);
    Task<Result> DeleteAsync(StoredMarker marker);
}

public sealed record StoredMarker(long Id, DateTime TimeUtc, string? Label);
```

`StoredMarker` is one row as stored: `TimeUtc` has `DateTimeKind.Utc`, and a null `Label` is a stored
`NULL`. `PostgresMarkerStore` in `SemiPlot.DataSource.Postgres` is the only implementation.
`AddPostgresData()` registers it by factory as a singleton beside the editor, over the same
`NpgsqlDataSource`, so the one `semiplot` role and connection string serve it too. Its one consumer is
`Markers/MarkerSync` (`overview.md#the-live-markers`). The chart names no Core interface for markers: it
reads and writes through `Chart/IMarkerChannel`, its own port, which `MarkerSync` implements.

| Operation | Constant | What the statement keeps |
| --- | --- | --- |
| `ReadAsync` | `StoredMarkers` | `SELECT id, t, label FROM semiplot_markers ORDER BY t, id`: the whole table, in the order the chart keeps. |
| `AddAsync` | `AddMarker` | `INSERT ... RETURNING id`; the result is the new marker's id. |
| `MoveAsync` | `MoveMarker` | `UPDATE semiplot_markers SET t = @t WHERE id = @id`. |
| `RenameAsync` | `RenameMarker` | `UPDATE semiplot_markers SET label = @label WHERE id = @id`; a null label binds `NULL`. |
| `DeleteAsync` | `DeleteMarker` | One row by id. Idempotent: a delete that touches no row succeeds, because the operator wanted the marker gone, as `RemoveMembership` does. |

A move or a rename must touch exactly one row. None touched means another window or an administrator
removed the marker, and the store returns `ArchiveFault.MarkerGone`, never success. `t` binds as
`NpgsqlDbType.TimestampTz` with `DateTimeKind.Utc` and reads back with `Kind = Utc`; no
`ArchiveTimeConverter` runs on the path. Every relation-naming failure carries `MarkersRelation`,
`semiplot_markers` (Error semantics, below). A write kind names the marker's label, or, for a marker with
none, its moment as local `yyyy-MM-dd HH:mm:ss` in the current culture: a refused add or rename names the
label asked for, and `MarkerGone` and every other refusal name the marker as stored. The chart's label
editor trims the text and caps it at 80 characters, so the chart never reaches the check; a direct write
that does gets `ValueRejected`.

`ReadAsync` takes the read loop's stop token and rethrows `OperationCanceledException` ahead of the
mapper; the four writes take no token. `ArchiveStatementTextTests` does not pin these constants.
`MarkerStoreTests` (`SemiPlot.Tests.Integration`) runs every one of them as `semiplot` against a
`CloneSource.Provisioned` clone, which also proves the grant, and pins the round trip of both UTC instants
of the repeated autumn hour.

## Operation to statement

Every statement the provider, the editor, the row reader and the marker store issue is
a constant in `SemiPlot.DataSource.Postgres/ArchiveStatements.cs`; parameters are always bound. The
bench seeder and the test harness own SQL of their own (`bench.md`). `ArchiveStatementTextTests` pins
each constant below clause by clause except `PenCatalog` and `ArchiveExtent`; `ExplainPlanTests`
asserts the plan shape of each one except `PenCatalog` against a container. The editor's and the
marker store's statements are in their own tables above.

| Operation | Constant | What the statement must keep, and why |
| --- | --- | --- |
| Pen catalogue | `PenCatalog` | `semiplot_tags` left-joined through `semiplot_pen_groups` to `semiplot_groups`, the names aggregated with `array_agg` under `GROUP BY tag.id`: a pen in two groups stays one row and a pen in none survives the outer join. `ORDER BY tag.name, tag.id`, because a pen has no single group to sort by, and the id keeps two pens that share a name in one order across reads. An empty table is an empty list; a missing one is `ArchiveFault.TableMissing` naming all three relations. |
| Archive extent | `ArchiveExtent` | Rooted at `semiplot_tags`, one `min(t)`/`max(t)` subquery pair per configured `id` at `l = 0`. A bare `min(t)` over `trends` cannot use `PRIMARY KEY (id, l, t)` and scans the archive. Nulls map to `ArchiveExtent.Empty`; an empty catalogue over a full archive is also `Empty`, since no pen could draw it. |
| Archive days | `ArchiveDays` | `generate_series(@firstDay, @lastDay, interval '1 day')` filtered by `EXISTS` over `semiplot_tags` with the extent's lateral probe per `id`: `l = 0 AND t >= day.start AND t < day.start + interval '1 day' ORDER BY t LIMIT 1`, then `ORDER BY 1`. The probe stops at the first row of each pen's day, and the `ORDER BY t` keeps it one index edge rather than a `Bitmap Heap Scan` over the pen's whole day ("Read cost"). Both days bind as `Timestamp` values of kind `Unspecified` at midnight, unconverted. An empty catalogue returns no day; a failure maps with `TagCatalogAndTrendsRelations`, `semiplot_tags, trends`. |
| History | `BucketedHistoryWindow` | Two branches at `l = @layer` under one outer `ORDER BY id, t`. The seed branch reads per pen one row strictly before `@from`, bounded to the wider of the window and one day: the bound prunes older partitions from the seed's `Merge Append`, and the seed keeps a steady variable on the chart as a horizontal line. The server reduces the window branch to one row per column: `GROUP BY id, segment, date_bin(@bucket, t, @from)`. `segment` counts the `q = 32` markers strictly before each row, so a marker closes its own bucket and no bucket straddles a break. Each bucket carries `min(v)`, `max(v)`, whether it ends in a gap, and the bucket's newest non-null sample as the column's timestamp and value, which is what the legend reads at the cursor. A `q = 32` marker and a null `v` both end the bucket in a gap. `BucketedRowFold` groups by consecutive identifier, so the single total ordering keeps each pen one run. Every layer takes this statement: a coarse row is a verbatim raw row and the markers are copied into the coarse layers (`scada-archive.md#layers`), so the buckets and the break split hold at `Minute` and `Hour` as at `Raw`. `@bucket` is `(to - from) / targetColumnCount`, never below one millisecond, the column's own resolution, and truncated down to whole microseconds (`PostgresDataProvider.BucketFor`). |
| Export, seed row | `ExportSeedRow` | One pen's last `l = 0` row strictly before `@from` and not older than one day: `t < @from AND t >= @from - interval '1 day'`, `ORDER BY t DESC LIMIT 1`. The strict bound keeps a boundary row from returning on both export statements; the one-day bound is the history statement's seed bound for a window of a day or less, which the export range always is. A pen last written earlier has no seed. |
| Export, slice | `ExportSliceRows` | The `l = 0` rows of every requested pen with `id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to`, columns `id, t, v`, `ORDER BY t, array_position(@ids, id)`: time first, equal times in the order of `@ids`, which is the checklist order. No `date_bin`, no `q` column and no `q` filter: a break marker row comes out as a row. |
| Export, count | `ExportRowCount` | `count(*)` of `id = ANY(@ids) AND l = 0 AND t >= @from AND t < @to`, plus the number of pens that have a seed, counted through a lateral `LIMIT 1` probe per distinct id. One statement over the whole range. Measured on 2026-10-14 over a day of 50 pens at 100 ms: 5.5 s cold and 5.1 s warm for 43.2 million rows, 2.1 s and 1.6 s for 16 pens, so the count stays one statement and no per-slice count exists. |

The seed and the slice are separate statements rather than a `UNION ALL`, because the seed is one index probe
per pen and the slice is one range read over every pen. The slice reaches its rows through the index on
`(id, l, t)` and sorts them by `(t, array_position)` above the scan, a `Sort` that spills to temporary files at
the default `work_mem` (5.4 MB for 16 pens and 16.7 MB for 50 on the bench); that is the cost of one table
ordered by time across pens, and the statement stays well inside its timeout. `ExplainPlanTests` asserts for
the seed, the slice and the count that no sequential scan reads a row-holding partition, and for the slice
that an index or bitmap reaches the rows and that one day partition is read.
`ExplainPlanTests.ArchiveDays_ReadsThroughTheIndex` asserts that the per-pen probe reaches `trends` by an
index scan, and that no day partition is read by a sequential scan or a `Bitmap Heap Scan`.

Five statements bind parameters through a binder of their own, each pinned against its statement's
parameter names: `PostgresDataProvider.BindLocalBucketedWindow` and `BindArchiveDays`, and
`PostgresArchiveRowReader.BindSeed` and `BindPensRange`, which binds
`ids`, `from` and `to` for both `ExportRowCount` and `ExportSliceRows`. A gap explanation over `messages` is
designed but not shipped; nothing issues it.

One history read runs in pen groups. `PostgresDataProvider.QueryHistoryAsync` removes duplicate ids,
sorts the rest ascending, and `Chunk` cuts them into contiguous groups of `ceil(n / HistoryReadConcurrency)`
pens, so the largest group holds as many pens as an even split into `HistoryReadConcurrency` groups would,
and a small read runs fewer groups: five pens run as 2, 2 and 1. `HistoryReadConcurrency` is 4, and
`AddPostgresData` hands it to the provider's constructor. The read runs three phases, and each statement
phase runs under a `CancellationTokenSource` of its own, linked to the caller's token:

1. The `BucketedHistoryWindow` statement of every group, concurrently, each on its own pooled
   connection.
2. At a coarse layer, the seams over the rows of all groups and one `FreshTail.Start` across every
   pen ("Layer ladder", Fresh tail).
3. The fresh-tail statements of every group, concurrently, at that one start.

Each phase awaits every group with `Task.WhenAll`, so the read returns only once each connection is
back in the pool. A failing group cancels the others of its phase, and its exception reaches the mapper. The groups'
rows concatenate in ascending pen id, the `ORDER BY id, t` run that `FreshTail.Merge` and
`BucketedRowFold` take, so the envelopes come back in ascending pen id. One tail start across every pen
keeps a pen's envelope the same whatever group it sits in:
`PostgresHistoryReadTests.AReadSplitIntoPenGroupsReturnsWhatOneStatementReturns` reads fresh, lagging,
reaching and late pens as one group and, named descending with one id repeated, in pen groups, and
compares the envelopes pen by pen at `Raw` and at `Minute`. The ids must be sorted: `FreshTail.Merge`
takes the smaller head pen id of the coarse and the tail rows as the next run, and `BucketedRowFold.Fold`
makes one envelope per run of one pen id, so groups concatenated out of order split a pen into two runs.
`AGroupedReadCancelledMidStatementReturnsEveryConnectionBeforeItThrows` holds four
statements on a lock of `trends`, cancels the read and finds no busy connection after the throw.
`AGroupFailingWhileTheOthersWaitFailsTheReadAfterEveryConnectionReturns` caps the pool one connection
short of the groups: three statements wait on the lock while the fourth group fails at the pool wait, and
the read comes back a failed `Result` with an `ArchiveError`, not an `OperationCanceledException` of a
sibling group, with no busy connection.

Four groups is the measured knee on the bench's 10 CPUs. The server plans `BucketedHistoryWindow` with
no `Gather` at any setting ("Read cost"), so one statement uses one core, and pen groups are the only
way a read uses more. Split into concurrent statements over pen groups, the visible-width Minute read
takes, as wall time of one pgbench round: over 30 pens 1 045 ms in one statement, 806 ms in two, 683 ms
in four and 530 ms in eight; over 50 pens 2 083 ms, 1 238 ms, 988 ms and 844 ms, 2.1x at four and 2.5x
at eight. Four leaves the remaining cores to the SCADA on the same machine. The data source sets no
`MaxPoolSize`, so Npgsql's default pool of 100 connections serves four per read.

At `Raw`, `BucketedHistoryWindow` reaches the window rows through the index on `(id, l, t)`, and on
the bench the planner sorts them by `(id, t)` before the window aggregate rather than reading them in
index order, at every width from two minutes to eight hours.
`ExplainPlanTests.TheBucketedRawPlanFeedsItsWindowAggregateThroughAnIndex` therefore pins, at `Raw`,
what holds either way: no sequential scan over a row-holding partition, the aggregate's input an index
scan or a bitmap over one, and the seed walk bounded to the wider of the window and one day. On the
archive "Read cost" (under Layer ladder) describes, a coarse read over a long window takes most day
partitions through a bitmap or a sequential scan, so no plan shape at `Minute` or `Hour` holds either
way and none is gated.

### What one history query covers

The chart reads only the pens it draws. `ChartHistoryPlanner.Next` (`SemiPlot.UI/Chart/ChartHistoryPlanner.cs`)
makes the one request decision, over the range in hand and the pens read for it, which the planner keeps.
`TrendChartViewModel` asks it from `OnNavigationWindowChanged`, `RequestWindowInForce`, `SetPenVisibility`
and `ApplyHistory`. A request names the visible pens in catalogue order. A hidden pen is never
read and keeps its last envelope (`charting.md#applying-a-catalogue-read`); the live source still
subscribes every pen of the catalogue, hidden ones included, because a monitored item per pen costs the
server one sample every 100 ms whatever is drawn (Realtime, below). The decision answers, in this order:

1. Nothing, when no pen is visible.
2. The visible window, when the range in hand does not cover the window in view.
3. The visible pens missing from the pens read for the range in hand, read alone over that range at the
   column count that range was read at. A pen switched on takes this read.
4. Nothing, when the range in hand carries its margins.
5. The window with its margins, unless they add no time that can hold rows.

Every window the range in hand does not cover, after a pan, a zoom, a layer change, a column-target
change or a jump, reads the visible window first. `HistoryPrefetch.Visible`
(`SemiPlot.UI/Chart/HistoryPrefetch.cs`) reads the window in view at the column target. A sticky window, a window whose end reaches the newest
sample, and a chart that has seen no sample yet keep the right margin in that first read, at twice the
column target (`ChartHistoryPlanner.ReachesTheNewestSample`): nothing lies past the live edge, so the
margin costs no rows, and the live samples the live source appends during the read survive its apply (below).
When that result lands, the decision asks for `HistoryPrefetch.Expand`. Every read therefore bins at one
column per pixel. The decision asks nothing instead when `Expand` spans the same times as the first read,
which happens when its left edge clamps at the first sample and the first read carried the right margin.
The cost: the server reads the visible window twice, for a detached window 1.33x the rows of one
`Expand` read; a pan made after the first paint and before the margins land reads again; startup reads
twice.

A read without a margin on a side keeps the columns in hand past that side. A read a gesture issues
lands after the gesture moved on: a zoom out has widened the window past it, a drag has moved the
window off it, and a drag right after a zoom moves off the zoom's own read. Replacing every column with
that read would blank the strips the range in hand still drew, for one read cycle, up to the 400 ms
`Sample` cap plus the read. `EnvelopeSplice.Over` (`SemiPlot.UI/Chart/EnvelopeSplice.cs`) therefore lays
each pen's read over that pen's envelope in hand when the read's range overlaps the time that envelope
was read over: the columns in hand before the read's first column and its range start stay when the
range carries no left margin, and those at or past the range end and after the read's last column stay
when it carries no right margin. `TrendChartViewModel.ApplyHistory` stores the spliced envelope, loads it
into the line, and then records its time: the read's range, widened to the time in hand on each side
that kept columns. The cursor, the delta readout and the auto scale therefore read what is drawn. The
columns kept were read at the range before, possibly at another layer or bucket width, so for that cycle
the line can change resolution at the read's edges. The `Expand` read that follows carries both margins
and replaces every column, so nothing kept outlives it. A read that does not overlap a pen's time keeps
none of its columns, and neither does a pen with no envelope in hand or one whose load threw, which has
no time recorded: a column kept across time no read covered would draw a straight segment over it.

Each pen's time is kept apart from the range in hand. The range in hand is the gate, and the gate shuts
while a read is applied, on a fold over Raw columns (below), when a read lands without every visible pen,
and on a catalogue change. None of these changes what an envelope in hand was read over, so a read that
lands with the gate shut still keeps the columns past it. That is the state of a zoom out across the
Raw ceiling: the live samples fold into the Raw columns while the first coarse read runs, and the Raw columns
still fill the strips the next notch widens past that read. The pens of one apply can hold envelopes of
two ranges: a pen switched on while the range in hand still held it keeps the envelope of that range,
while the read in flight brings the other pens a new one. Each pen's read is therefore matched against
that pen's own time.

`FetchRange` carries the margin windows it was built with on each side, and its `ColumnFactor` is one
plus their sum. `HistoryPrefetch.Covers` reads the range's own margins and compares the column target
against the visible target times that factor. The left edge is clamped to the archive's first sample,
in `Visible` as in `Expand`; the right edge is not, because a range reaching past the live edge is a
successful empty read. The provider's fresh tail therefore clamps at the present rather than at that
edge ("Layer ladder"). A new window is drawn from the last fetch while it stays inside the inner band,
half of each side's own margin in from that fetched edge, so a pan issues no query until the margin is
half spent. A side read without a margin has no band to spend: a range read without margins covers
exactly its window, and any pan off it reads again. A range that reaches the first sample has no left
edge to approach, because no row lies before it: `HistoryPrefetch.Covers` takes `Navigation.FirstSample` and holds the left side
of such a range whatever margin the clamp left. A window that starts before the first sample is
therefore read once and drawn from that read; an archive younger than the window opens on one, and a
zoom out past the archive followed by a jump to now leaves one. A first sample that moves back, after
a catalogue read adds a pen with older rows, reopens the question:
`TrendChartViewModel.WidenToArchiveExtent` asks `Covers` again with the new first sample, and a range
clamped at the old one no longer holds its left side, so the window in view is read once more from the
new first sample. A zoom, a layer change and a column-target change always re-fetch, because each
changes the resolution the range was read at.

Two column counts travel on one request, and the gate reads only one of them. `HistoryRequest.Range`
carries the quantized count `ChartNavigationController.TargetColumnCount` holds, which a deadband
keeps still while the Y tick labels widen and narrow the data rect by a pixel mid-gesture;
`HistoryRequest.TargetColumnCount` carries the unquantized pixel width the provider decimates to.
A pen read alone over the range in hand takes the unquantized count that range was read at, not the
width reported since, so it bins on the `date_bin` grid of the pens it joins.
The range travels on the request so the result alone says which band the envelopes in hand describe:
`ChartHistoryPlanner` opens the gate on the range that came back, never on the one last asked for, and
only when the pens read for that range include every visible pen. A read issued before the catalogue
added a pen holds no row of it, so its result leaves the gate shut and the window in view is asked for
again with every visible pen. That request is the one the catalogue read already pushed, so it leaves
that read running: across the live-edge switch of a catalogue change a kept pen receives only its
newest value (Realtime, below), and only a read of every visible pen fills the changes in between
(`charting.md#applying-a-catalogue-read`).

The pen set travels beside the range. `ChartHistoryPlanner` keeps the pens read for the range in hand
beside it: `ChartHistoryPlanner.Land` adds a result's pens to the set when the result is for the same
range, and replaces the set when it is for another. The window counts as fetched only while every visible
pen is in the set. The set is never pruned when a catalogue read removes a pen, because it is only
compared against the visible pens.

A gesture that never goes quiet still fetches. `ChartHistoryRequestDebouncer` merges the trailing
`Throttle` of 150 ms with a `Sample` of 400 ms over the same requests, so a continuous drag issues one
query per 400 ms and one more after it stops. `Throttle`, `Sample` and `Merge` hand an emission on
while they hold their own locks, and `Request` takes the `Sample` lock on the UI thread, so the merged
stream passes through `ObserveOn(dataScheduler)` and the debouncer admits and starts each query on the
data scheduler after those locks are released, where a provider that blocks before its first `await`
holds up no `Request`. The production data scheduler is `DefaultScheduler.Instance`, which Rx serves as a
long-running scheduler, so `ObserveOn` drains on one dedicated thread per debouncer, one per chart window:
the thread starts with the first admission, waits blocked between admissions and ends at `Dispose`. That
thread, never a pool thread, carries each admission and the synchronous part of each query start.

The chart alone knows what is drawn. It asks for a window only while its fetched range does not cover
the window in view, and otherwise pushes `RequestNothing`. The debouncer keeps no drawn window of its
own. It stamps each request with the number of successful reads delivered so far, and drops a request
the last successful read answers: the same pens, window and column target, pushed before that read was
delivered. That is how the two heads emitting one push, and a drag that stops on the window being
read, issue one query. A request pushed after that read was delivered is the chart asking again, and it
reaches the archive.

A read that is still in flight, that came back failed, or whose apply threw covers nothing, so the
chart asks for the same window again. `TrendChartViewModel.ApplyHistory` has the planner forget its
range in hand before it loads the envelopes and land the read only after, so an apply that throws part way leaves the gate
shut and the window in view is asked for again at the next move. A catalogue read that adds or removes
a pen drops envelopes outside that apply, so it clears the fetched range and asks for the window in
view with every visible pen. A pen removed and named again takes back its old place in the pen order, so
that request can name the same pens and window as the read applied before the removal; it is pushed
after that read was delivered, so it is read.

A fold over Raw columns opens the gate too. At a coarse layer a realtime sample folds into the last
column, and while the coarse read is in flight, failed or never applied, the columns in memory are still
Raw: every sample of that phase folds into one Raw column. `TrendChartViewModel.ApplyRealtimeBatch`
therefore clears the fetched range when it folds and that range is Raw, so a zoom back to the same Raw
width reads the window again instead of drawing one straight segment across the folded span. It clears
the gate only, so the columns kept past a margin-less read stay (above). A fold
over a coarse range clears nothing: it widens a coarse column and leaves nothing to re-read, so a pan
inside a coarse band still reads nothing.

An apply keeps the live tail past its snapshot. A history read reads the archive as of its statement's
snapshot, and the live source keeps appending live columns while the read runs; it only moves forward,
so it never delivers those samples again. The archive also trails the OPC UA stream by its own write and
flush lag (`scada-archive-observed.md#the-opc-ua-server`), so the live tail is often the only data past
the archive's newest row. A replace that dropped it would leave a hole up to that lag wide, drawn as
one straight segment until a gesture re-reads the window. The archive replaces a live point only where
a history read covers it `[DEC:opc-live-edge]`, and nothing re-reads history when the archive catches up.
`EnvelopeLine.ReplaceColumns` therefore keeps the columns past the last X the previous replace wrote
that lie after the new last column and at or before the right edge of the range the read asked for, at
every layer: after a coarse envelope a kept column is a live sample the coarse read did not hold, and the
next fold widens it (`charting.md`, "Realtime append / live-edge join"). A requested pen absent from the result
keeps its tail the same way. The fold into the last coarse column is not kept: a sample folded after the
next coarse read's snapshot leaves with the column it widened, and an extreme it carried shows again only
when a later read covers that span. A pan into the past asks for a range that ends before the follow
phase's tail, so the apply drops that tail and the band just read keeps the whole column buffer; a tail
kept there would hold up to 100 000 columns and push the band out of the buffer within a few appends.
The history columns a margin-less side keeps (above) are not live appends, and the tail past them goes
the same way.
Samples arriving after such a pan still append far right of the band, as they did before. A drop past
the requested end also unjoins the pen's line from the live stream, so its next live value opens behind
a break (Realtime, The break ahead of a first live sample): `ReplaceColumns` returns whether it dropped a
column there. A first read at the live edge keeps its right margin for this rule: its range ends one
window width past the window, so the columns the live source appends while it runs lie inside that range
and survive the replace, and a sticky window never takes the drop.

One query runs at a time, and the newest request that arrived while it ran runs when it lands. No
request waits when the newest push needs no read, and the window waiting behind a query is dropped
when that query's read answers it. The two heads treat the query in flight differently:

- A paced request never cancels it. A read slower than the cap interval therefore still completes
  during the gesture and lands, so the strip fills while the drag moves.
- A trailing request is the gesture's end. When it finds a query running for another window, it
  cancels that query, so the final window starts as soon as the cancelled one completes instead of
  waiting up to the 300 s command timeout behind a read for a window the gesture has left. The
  cancelled query completes once the server acknowledges the cancel, or when Npgsql breaks the socket
  after `CancellationTimeout`.

A gesture can end back inside the band the envelopes in hand cover, after a read for another band
started. A pan inside the fetched band then pushes `RequestNothing`: it reads nothing, drops the
request waiting behind the running query, and as the gesture's end it cancels the read for the band
the drag left. The gate is open only while the range in hand holds every visible pen, so that push
never cancels a read issued for a pen the catalogue added or the operator switched on since. A read that failed changes nothing on screen and leaves the
gate as it was, so a gesture whose read failed still ends on the window drawn before it; an apply that
throws after the envelopes load leaves the gate open on them, with the same effect. A read that lands
while the drag is still moving is applied, and the window in view is re-requested from the range that
arrived.

"Trailing" means 150 ms without a request, not the release of a mouse button. Input that arrives in
steps more than 150 ms apart, such as wheel notches, key presses or a window resize, ends a gesture
at every step, so each step cancels the read the previous one started, and no step's read lands until
the input pauses for one whole read. On the bench a detached window's first read at 2048 columns takes
124 to 816 ms by median at 8 to 50 visible pens ("Read cost", under Layer ladder), up to several times
that gap; a pan past the band takes that same read first. A sticky window's first read at a coarse
layer also runs the fresh tail, and the margin read spans three visible widths; neither is timed there.
The trade-off is that a slow read shows
the final window sooner and the intermediate ones not at all.

`Dispose` cancels the running query as well. A cancelled query reports nothing and starts the request
waiting behind it; the last window a gesture asked for is the last one applied. Each query runs under its own `CancellationTokenSource`, which the debouncer
creates under its gate and cancels with `CancelAsync`: the flag flips at once, and the callbacks run
on the thread pool. Npgsql's callback opens a second connection to send the cancel request, bounded
by the 15 s connect timeout, and blocks until the server closes it; run under the gate and Rx's own
locks, it would freeze the UI thread's next request and the window's close for that long. The source
is never disposed: it holds no timer and no wait handle, and disposing it before the queued callbacks
run drops them, so the provider would never hear of the cancel. Only a cancellation the debouncer
asked for is silent: an `OperationCanceledException` while its token is still live is a failed read
like any other.

The token is the trailing `cancellationToken` of `IDataProvider.QueryHistoryAsync`, threaded through
`TrendCoordinator.QueryHistoryAsync`. `PostgresDataProvider` links it into the
`CancellationTokenSource` of each statement phase, which a failing pen group also cancels, and passes that
phase's token to every
connection open, every `ExecuteReaderAsync` and every row read of the bucketed window and the fresh
tail, and
`QueryHistoryAsync` rethrows the `OperationCanceledException` it ends in from a catch of its
own, ahead of the mapper, so a cancelled read never becomes a failed `Result`. `PostgresHistoryReadTests.AReadCancelledMidStatementThrowsInsteadOfFailing`
cancels a Raw and a Minute read held on a lock of `trends`; both run `BucketedHistoryWindow`. A cancelled command costs one extra
connection for the cancel request, and its own connection for at most two seconds more: Npgsql first
sends PostgreSQL the cancel request, and if no answer arrives within `CancellationTimeout` (2000 ms by
default, which the connection string keeps) it breaks the socket. A broken connection leaves the pool,
and the pool opens a new one. A grouped read has up to four commands in flight, one per pen group
(Operation to statement), so its cancel opens up to four cancel connections instead of one, and each
group's connection follows the same rule. The backend behind a broken socket can keep running the
abandoned statement until it notices the dead connection, so for that stretch the server runs two
history reads for one chart.

A failed read reaches the chart. `ChartHistoryRequestDebouncer` delivers the `Result`'s errors on the
UI scheduler, a thrown query joining the same channel as an `ExceptionalError`;
`TrendChartViewModel.OnHistoryQueryFailed` logs them and re-applies the axis over the envelopes still
in hand. Skipping the axis there would freeze it with no way back, because the gate that would ask
again opens only on a result. A throw out of the apply, and any fault the pipeline itself raises,
reach the same channel: without that the subscription issuing every history query would tear down,
and the chart would stop reading the archive for the rest of the session.

The buckets make a read smaller in rows returned, not cheaper on the server: at every layer the window
function still reads and sorts every row of the range. The first read of a gesture spans the visible
window, and the margin read three visible windows.
`PostgresHistoryReadTests.TheWidestRawWindowThePrefetchMarginAsksForComesBackBucketed` records
`EXPLAIN (ANALYZE, TIMING OFF)` for the widest Raw read the margin can ask for, and the plan's row
counts and timing are what say what it costs; "Read cost", under Layer ladder, measures every layer.

Every history read writes one `Debug` entry when it ends, at the same
level as the chart's latency line per live batch (Realtime, below), so a run at `--logging-level Debug`
interleaves both. The
entry carries the layer, the UTC range, the column target, the count of distinct pens read, the pen group count, the outcome (`Read`,
`Failed` or `Cancelled`), the envelope count of a `Read` and the milliseconds since the read started.
`PostgresDataProvider.QueryHistoryAsync` writes the entry from one `finally`: the outcome starts as
`Failed`, the success path sets `Read`, and the catch of `OperationCanceledException` ahead of the mapper
sets `Cancelled` and rethrows.
`PostgresHistoryReadTests.AHistoryReadLogsItsRequestOutcomeAndDuration` pins the entry of a Raw and a
Minute read, `ADroppedTrendsTableFailsNamingTrends` the `Failed` one and
`AReadCancelledMidStatementThrowsInsteadOfFailing` the `Cancelled` one.

## Layer ladder

The archive offers three resolutions; the renderer needs about one point per pixel column. The rule:
**choose the coarsest layer whose point spacing still fits inside one pixel column.** A coarse layer
holds the minimum and the maximum of each calendar period, two rows per period
(`scada-archive.md#layers`). The point spacing is half that real spacing, so a coarse layer takes over
once it fills at least every other column (`AggregationLayerExtensions.ToPointSpacing`):

| Layer | `l` | Period | Point spacing |
| --- | --- | --- | --- |
| `Raw` | 0 | — | the archiving interval |
| `Minute` | 1 | minute | 15 s |
| `Hour` | 2 | hour | 15 min |

The hour layer is the coarsest one SemiPlot reads, and every build writes it. An archive from a build
before 2.7.5.0 also holds `l = 3` rows (`scada-archive.md#layers`); nothing reads them.

`ChartNavigationController` expresses the rule as an upper bound for `Raw` and `Minute`, shrunk when
more pens are visible than the ladder is sized for:
`ceiling(layer) = nextCoarser(layer).ToPointSpacing() × TargetColumnCount × min(1, ReferencePenCount / max(1, visiblePens))`.
The raw layer's own spacing, the SCADA's per-variable archiving interval that the client cannot know,
never enters the comparison. `Hour` tops the ladder and has no ceiling: every window wider than the
minute ceiling reads it, up to the 365-day window cap (`TrendNavigationModel`).

| Columns | Visible pens | `Raw` up to | `Minute` up to | `Hour` |
| --- | --- | --- | --- | --- |
| 256 | 16 or fewer | 64 min | 64 h | above, to the 365-day cap |
| 2048 | 16 or fewer | 8 h 32 min | 512 h (21 d 8 h) | above, to the 365-day cap |
| 2048 | 30 | 4 h 33 min 4 s | 11 d 9 h 4 min | above, to the 365-day cap |
| 2048 | 50 | 2 h 43 min 50.4 s | 6 d 19 h 50 min 24 s | above, to the 365-day cap |

The column count is live. `HistoryColumnTarget.FromDataAreaWidth` maps the view's data-area width
to 256…2048 columns; `ChartNavigationController.SetTargetColumnCount` quantises that to the nearest
power of two with a 10 % deadband, and only the quantised count moves the ceilings or triggers a
re-query. The history query keeps the unquantised count, because that decides resolution rather than
layer. Inside the deadband the drawn resolution can lag the canvas by up to `2 × 1.1² ≈ 2.42`; the
next navigation gesture closes the gap.

Two adjustments the ladder needs are implemented:

- **Hysteresis.** Layers switch on thresholds separated by a margin, so a window hovering on a
  boundary does not flip layer on every wheel notch. The layer in force holds to 1.1x its ceiling.
- **Fresh tail** (`FreshTail`, in the provider). A period's coarse rows arrive with the variable's next
  batch after the period closes: at the hour layer within 3 minutes for fast variables and up to 47
  minutes later for slow ones (`scada-archive.md#layers`). A window reaching "now" is therefore short of
  the open period, and of every closed period whose rows have not arrived yet, at its right edge. The
  provider reads that edge from `l = 0` and merges it per pen. The seam is per pen: the newest coarse
  timestamp returned, or the window start when none was. A layer fresh within one of its own points reads no
  tail; otherwise the tail starts at the earliest seam of every pen the read names, whatever pen group
  it sits in, clamped to four point spacings back from the
  tail edge, and reads raw rows up to the window end. The tail edge is the window end or the present,
  whichever is earlier: `PostgresDataProvider` reads the present from its injected `TimeProvider`
  and converts it with `ToArchiveLocal`. The window end alone cannot serve, because the chart asks
  for one visible window width past the visible end (`HistoryPrefetch.Expand`, and
  `HistoryPrefetch.Visible` at the live edge); in follow mode that
  edge lies a whole window in the future, the clamp passes every seam, and a Minute read stops up to
  one period short of the raw newest. A window wholly in the past clamps at its own end. A pen
  whose seam precedes the tail's start contributes no tail row, because a range no
  row covers is not a gap and would draw as one straight segment. The clamp is one period, so a pen
  whose newest closed period has not arrived yet seams more than one period back, reads no tail, and
  its line stops more than one period short of the raw newest; `docs/plans/backlog.md` tracks the
  wider clamp. The tail is read through
  `BucketedHistoryWindow` at `l = 0` with the coarse read's own `@bucket`: a tail narrower than one
  column folds into one column, and a tail at a fine bucket keeps every raw row.

The pen factor holds the first read of a zoom under one second on the bench. A read's server time
grows with the pens it names and with its width ("Read cost"), so past the reference count the factor
narrows the finer layers in proportion. `ReferencePenCount` is 16: the largest visible pen count at
which both the `Raw` and the `Minute` hysteresis-edge reads, at the unshrunk ceilings and with pen
groups, have a median at or under 1 000 ms end to end. Measured 2026-10-13 under the conditions of
"Read cost", the median of 88 timed calls in eight rounds per case:

| Visible pens | 8 | 16 | 24 | 30 | 40 | 50 |
| --- | --- | --- | --- | --- | --- | --- |
| `Raw` hysteresis edge, 9 h 23 min 12 s | 157 ms | 397 ms | 565 ms | 564 ms | 808 ms | 1 012 ms |
| `Minute` hysteresis edge, 23 d 11 h 12 min | 406 ms | 837 ms | 1 141 ms | 1 290 ms | 2 053 ms | 2 208 ms |

The `Minute` hysteresis edge is the first case past 1 000 ms, at 24 pens. Four more rounds of 16 and 24
pens alone, on a quieter host, gave 647 ms and 963 ms; pooled over all twelve rounds the 24-pen case is
1 053 ms, still over. The count is a constant measured on the bench, and changing it takes a rebuild.

`TrendChartViewModel` hands the visible pen count to `ChartNavigationController.SetVisiblePenCount`
after every catalogue apply and every visibility change, ahead of the request decision. A count that
moves the layer raises `WindowChanged`, and the window in view is read at the new layer. The minimap
passes one pen to `LayerForWidth` and keeps the unshrunk ladder (`MinimapBandFeed`).

The cost: past 16 visible pens the chart reads the coarser layer over windows the plain rule gives to
the finer one. The hysteresis widens only the layer in force, so a pen toggle ratchets: switching pens
on can move the chart to the coarser layer, and switching one of them off again keeps it there. The
chart returns to the finer layer once enough pens go off, or the window narrows enough, that the finer
layer's ceiling without the hysteresis margin holds the window.

The envelope is correct at every layer because the vendor's selection keeps each period's minimum and
maximum, checked over 10 340 variable-minutes and 193 variable-hours (`scada-archive.md#layers`).

### Read cost

Measured 2026-10-13 on the `SemiPlot/bench` image on PostgreSQL 17.11 with the `semibase site` settings
passed as `-c` flags: `shared_buffers=2GB`, `effective_cache_size=4GB`, `work_mem=64MB`,
`maintenance_work_mem=512MB`, `max_wal_size=8GB`, `checkpoint_timeout=30min`, `wal_compression=on`,
`random_page_cost=1.1`. Docker Desktop 29.8.1 with 10 CPUs and 16 GB; .NET SDK 10.0.401 on runtime
10.0.12, Npgsql 10.0.3, Release build; provider time zone `Russian Standard Time` (UTC+3, no DST);
`SHOW jit` over the provider's data source reads `off`; four pen groups. The archive holds 50 pens with
random values and `q = 0`, all ending 2026-10-12 00:00: raw rows at 1 per second over 30 h, minute rows
at :10 and :40 s over 72 days, hour rows at :10 and :40 min over 1 096 days. That is 5 400 000 raw,
10 368 000 minute and 2 630 400 hour rows in 1 096 day partitions from
`PartitionScript.CreateStatements`, `VACUUM ANALYZE` after the load.

The tables time the first read a zoom or a pan past the band issues, end to end through
`IDataProvider.QueryHistoryAsync` of a provider composed by `AddPostgresData`, as the viewer composes
it: the pool checkout, the window
statements, Npgsql's binary decode and the fold. They are not the first paint: the debouncer issues the
read 150 ms after the gesture goes quiet, and the apply and the frame follow the read, so the 50-pen Hour
window cap's 816 ms puts the first paint past 966 ms. `E` is the archive end. A case is a detached window of
width `w` over `[E - 2w, E - w]`, pens 1 to n. It ends before the newest sample and is not sticky, so
`HistoryPrefetch.Visible` reads it with no margin at 2048 columns, and its left edge lies after the first
sample. Each width is the widest that `ChartNavigationController.LayerForWidth` still answers with the
case's layer at that pen count, entered from the coarser layer for a ceiling and from the layer itself
for a hysteresis edge, so each ceiling carries the pen factor ("Layer ladder"). Every envelope holds
2 049 columns: 2 048 buckets and the seed row.

| Case | 8 pens (factor 1) | 30 pens (factor 0.533) | 50 pens (factor 0.32) |
| --- | --- | --- | --- |
| Raw ceiling | 8 h 32 min | 4 h 33 min 4 s | 2 h 43 min 50.4 s |
| Raw hysteresis edge | 9 h 23 min 12 s | 5 h 0 min 22.4 s | 3 h 0 min 13.44 s |
| Minute ceiling | 21 d 8 h | 11 d 9 h 4 min | 6 d 19 h 50 min 24 s |
| Minute hysteresis edge | 23 d 11 h 12 min | 12 d 12 h 22 min 24 s | 7 d 12 h 13 min 26.4 s |
| Hour window cap | 365 d | 365 d | 365 d |

Other sessions' container tests ran on the host, and single calls spiked up to 3.6 s, so every case ran
in eight rounds of 2 warm-up and 11 timed calls, the order of pen counts and cases reversed every round.
A time is the median of the 88 timed calls in ms, min to max in brackets.

| Case | 8 pens | 30 pens | 50 pens |
| --- | --- | --- | --- |
| Raw ceiling | 124 (74-257) | 280 (241-772) | 413 (278-1 923) |
| Raw hysteresis edge | 141 (81-222) | 296 (257-519) | 405 (301-1 642) |
| Minute ceiling | 305 (255-436) | 624 (526-3 507) | 685 (554-2 060) |
| Minute hysteresis edge | 318 (274-395) | 712 (552-3 515) | 747 (631-3 627) |
| Hour window cap | 206 (179-349) | 535 (421-2 804) | 816 (665-2 076) |

Every median is at most 1 000 ms. The highest is the 50-pen Hour window cap at 816 ms, whose round
medians run from 705 ms to 979 ms.

Under the same server settings, the server execution time of one `BucketedHistoryWindow` statement at
the Minute layer, 15 min bucket, every pen changing every second:

| Read | 8 pens | 24 pens | 30 pens | 50 pens |
| --- | --- | --- | --- | --- |
| Minute ceiling, `Expand` range (64 d at 6 144 columns) | 1 421 ms | 4 243 ms | - | - |
| Minute ceiling, visible window (21.3 d at 2 048 columns) | 462 ms | 1 396 ms | 1 893 ms | 3 159 ms |

- Read time is linear in the pens named and in the width: at 8 pens the `Expand` read executes in 70 ms
  at 1 d visible, 469 ms at 7 d and 998 ms at 14 d. The margin read costs about three times the first
  read, so the first read carries a margin only where it holds no rows.
- Pen groups spread one read over four cores ("Operation to statement").
- `work_mem` above the site's 64 MB changes nothing: every sort of these reads already runs in memory.

Rejected ways to make a read cheaper:

- Server-side parallel query. With `max_parallel_workers_per_gather` 0, 2 or 4, and with
  `parallel_setup_cost = 0` and `parallel_tuple_cost = 0`, no plan of `BucketedHistoryWindow` holds a
  `Gather`, and the time does not change.
- A client cache of the coarse layers. The `date_bin` grid is anchored at each request's own `@from`, so
  a cached layer serves only a request on an identical grid. Coarse rows arrive up to 47 minutes after
  their period closes (`scada-archive.md#layers`), so a cached tail goes stale.
- A session `work_mem`. The server's `semibase site` value already holds every sort in memory.

Measured 2026-10-12 under the server defaults, the margin read at 8 pens in one pen group: default
`work_mem` 4 MB, `shared_buffers` 128 MB, `jit_above_cost` 100 000 and the server default `jit = on`,
on `postgres:17.11-alpine`, with the host and toolchain above. The archive holds 8 pens loaded as above.
A chart case is a visible window of width `w` ending at `E - w`; `HistoryPrefetch.Expand` turns it into
the fetched range `[E - 3w, E]` at three times the column count, so the fresh tail runs. A hysteresis
edge is 1.1x the unshrunk ceiling. The minimap reads the whole archive span of one pen at
`MinimapColumns`, with no `Expand`. A time is the median of 44 timed calls in four rounds of 2 warm-up
and 11 timed calls, min to max in brackets. Rows per pen are the window statement's, then `+` the
fresh-tail statement's, each with its seed row.

| Case | Visible width | Columns | Layer | Fetched range | Column target | Pens | Rows per pen | Columns per pen | Time |
| --- | --- | --- | --- | --- | --- | --- | --- | --- | --- |
| Raw ceiling | 8 h 32 min | 2048 | Raw | 25 h 36 min | 6 144 | 8 | 6 145 | 6 145 | 699 ms (546-820) |
| Raw hysteresis edge | 9 h 23 min 12 s | 2048 | Raw | 28 h 9 min 36 s | 6 144 | 8 | 6 145 | 6 145 | 768 ms (695-1 022) |
| Minute ceiling | 512 h | 2048 | Minute | 64 d | 6 144 | 8 | 6 145 + 2 | 6 146 | 1 446 ms (1 381-1 693) |
| Minute hysteresis edge | 563.2 h | 2048 | Minute | 70.4 d | 6 144 | 8 | 6 145 + 2 | 6 146 | 1 617 ms (1 508-2 253) |
| Hour, window cap | 365 d | 2048 | Hour | 1 095 d | 6 144 | 8 | 6 145 + 2 | 6 146 | 657 ms (614-992) |
| Hour, narrow canvas | 365 d | 256 | Hour | 1 095 d | 768 | 8 | 769 + 2 | 770 | 604 ms (570-772) |
| Minimap | 1 096 d (the archive span) | - | Hour | 1 096 d | 250 | 1 | 250 + 2 | 251 | 150 ms (142-170) |

- At the Minute ceiling the window aggregate reads 1 474 560 coarse rows, and under 4 MB the group sort
  spills 9.8 MB to disk; `EXPLAIN (ANALYZE, BUFFERS)` under `jit = off` executes in 1 440 ms. The
  row-level coarse read, which sends per pen a seed row and every coarse row of the window and folds
  them on the client, takes 1 982 ms and 2 193 ms for the two Minute cases.
- Under 4 MB the Hour window cap sorts the window's 420 480 rows on disk (15.6 MB) ahead of the window
  aggregate. The row-level coarse read takes 562 ms there, and 125 ms for the minimap.
- JIT: the Hour window cap executes in 542 ms with 50 ms of planning under `jit = off`, and in 2 054 ms
  with 270 ms of planning under `jit = on`, of which JIT takes 1 506 ms. SemiPlot's sessions therefore
  run with `jit = off` ("Configuration").
- The plan `20261012-drop-day-layer.md` holds the per-round medians, the row-level coarse read's full
  table and the single-statement measurements.

The archive days read (`ArchiveDays`) was measured 2026-10-15 on the `SemiPlot/bench` image, PostgreSQL
17.11 under the server defaults (`shared_buffers` 128 MB, `work_mem` 4 MB, `random_page_cost` 4), session
`jit = off`, Docker 29.8.2 with 10 CPUs and 16 GB. The archive is `ArchiveSeeder --days 31 --pens 8
--seed 1 --end 2026-02-02T00:00:00`: 8 566 416 raw rows in 31 day partitions and `tpdefault`, with
`ANALYZE` on `trends` and `semiplot_tags`. The read covers all 31 days, as `semiplot`, from psql.

- `EXPLAIN (ANALYZE)`: each probed day runs one `Index Only Scan` on its own partition, one row and one
  loop each; every other partition stays out of that probe, and `tpdefault` shows `(never executed)`.
  The first pen holds a row on every day, so `semiplot_tags` is read one row per day.
- Execution 2.5 ms, planning 8.0 ms. Wall time in psql: 9.3 ms for the first read, 2.1 ms and 2.2 ms for
  the next two.
- A day without rows costs one probe per catalogue pen instead of one, because `EXISTS` stops only at a
  row. Measured on the same seed with a stopped stretch: the rows of 2026-01-12 .. 2026-01-21, ten days,
  deleted at every layer, then `VACUUM ANALYZE` on `trends`. Over 2026-01-02 .. 2026-02-01 the read returns
  the 21 days with rows; each of the ten empty partitions runs its `Index Only Scan` 8 times with no row,
  `semiplot_tags` is read 3 rows per day on average, and `tpdefault` shows `(never executed)`. Execution
  1.2 ms, planning 6.8 ms; wall time in psql 11.1 ms for the first read, 9.2 ms and 9.0 ms for the next
  two. An empty day cost 8 probes of 0.001 ms each; at that rate a stopped year at 50 pens would add
  about 18 ms of execution (extrapolated, not measured). Planning grows with the partitions the range
  covers, rows or not.
- The probe's `ORDER BY t` is what keeps it an index edge. With a bare `LIMIT 1`, on a one-day seed, the
  planner takes a `Bitmap Heap Scan` whose `Bitmap Index Scan` reads 33 998 index entries to return one
  row; over three days the read executes in 1.57 ms there and in 0.27 ms with the ordering.

## Time boundary

The archive stores naive local wall-clock time; everything above the provider is UTC. The one column
SemiPlot reads that is not naive local is `semiplot_markers.t`, a `timestamptz` (The marker store, above).

- Reading: `t` is interpreted in the machine's time zone and converted to
  `DateTime(Kind = Utc)` by `ArchiveTimeConverter.ToUtc`.
- Query bounds: UTC window edges are converted back to naive local (`ToArchiveLocal`) before binding.
- The conversion happens only at the provider edge. Display-local rendering is a separate, later
  conversion in `LocalTimeAxis`.
- Four paths skip `ArchiveTimeConverter`: the export path, the archive days read, the marker store and
  the live source.
- The export path: `PostgresArchiveRowReader` takes no `ArchiveTimeConverter`. It
  binds the range bounds as `Timestamp` values as typed, reads `t` with `GetDateTime`
  (`DateTimeKind.Unspecified`) and returns it in `StoredArchiveRow.Time` unconverted. The export window
  takes each bound as a date from a `CalendarDatePicker` plus a typed `HH:mm:ss` time and reads the pair
  as a wall-clock value, and the CSV writes the stored `t` verbatim. Because
  nothing converts through UTC, the repeated autumn hour loses no row. The conversions on that path go
  from UTC to wall-clock through the archive's zone (`PostgresConnectionSettings.SourceTimeZone`): the
  window's prefill of the navigation edges, and the calendars' first-sample and newest-sample days
  (`ArchiveDayCalendar.Seed`). Each is a function and drops nothing.
- The archive days read: `QueryArchiveDaysAsync` binds two `DateOnly` days as midnight `Timestamp` values
  and returns each day as a `date`, both in the archive's wall-clock days, the ones its partitions cut. The time
  window panel and the export window compute the days they ask for in `Navigation/ArchiveDayCalendar`: from
  the local day of the first sample, or an earlier selected start date, to the local day of the newest
  sample, each through `WallClockRangeForm.WallClockDay` in production's `archiveZone`. The
  panel's typed edges go the other way through `TimeZoneInfo.ConvertTimeToUtc` in the same zone: an
  ambiguous autumn hour takes the standard offset, under the cosmetic rule below, and a time the spring
  change skips is a form problem, never converted (`trend-interaction.md#the-time-window-panel`).
- The marker store: the viewer knows the exact instant it places a marker at, so `semiplot_markers.t` is a
  `timestamptz` and `PostgresMarkerStore` binds it as `TimestampTz` with `Kind = Utc` and reads it back as
  UTC, unconverted. A naive local value would be ambiguous in the repeated autumn hour; an instant is not,
  so the store returns a moment in either half of that hour as it was written.
  `MarkerStoreTests.AMomentWrittenAsUtcReadsBackEqualThroughTheRepeatedHour` pins both halves. No
  `LegacyTimestampBehavior` switch is set. The chart does not reach both halves: its axis is local wall
  clock, and `LocalTimeAxis.FromAxis` turns a pixel of the repeated hour into one instant only, so the chart
  places a marker in that half alone, and a marker another client stored in the other half draws but takes
  no hover, drag or menu (`trend-interaction.md#the-time-markers`).
- The live source: a notification's `SourceTimestamp` is true UTC on the SCADA
  (`scada-archive-observed.md#the-opc-ua-server`), so `LiveValue` turns it into `Sample.TimestampUtc` with
  `Kind = Utc` and no conversion, and falls back to the `ServerTimestamp` the same way. `OpcUaLiveSource`
  takes no zone, and `git grep -n -E "ArchiveTimeConverter|TimeZoneInfo|ToLocalTime|ToUniversalTime" --
  SemiPlot/SemiPlot.DataSource.OpcUa` prints nothing. `OpcUaLiveSourceTests` runs the demo server in
  `Asia/Tokyo` and checks every stamp against the wall clock in UTC, because the CI Linux leg runs in UTC,
  where a stamp read as local passes unnoticed.

The database does not record the zone. The SCADA, its archive and the viewer share one machine, so
the provider converts in that machine's zone `[DEC:machine-time-zone]`: `PostgresConnectionLoader`
fills `PostgresConnectionSettings.SourceTimeZone` with `TimeZoneInfo.Local`, and no file names a zone.
Tests that build the settings directly pass a fixed zone through the same property. Daylight-saving
transitions are accepted as cosmetic, and the archive stores no offset to make them anything else.
`BucketedRowFold` keeps a bucket only when its converted timestamp exceeds the previous kept one for
that pen, which is what keeps the envelope strictly ascending: at the spring gap that drops the one
or two buckets the conversion put out of order; at the autumn fall-back it drops the whole repeated
hour, for every pen, once a year, and stamps the surviving hour an hour late. Pinned by
`BucketedRowFoldTests.ABucketWhoseConvertedTimestampDoesNotAdvanceIsDropped` and
`BucketedRowFoldTests.ASkippedLocalHourDropsTheBucketTheConversionPutsOutOfOrder`.

## Quality and gaps

The provider maps the archive's quality marks onto the envelope's gap representation (`DA-8`):

| Archive | Envelope |
| --- | --- |
| `q = 0` | ordinary point |
| `q = 32` (last sample before a break) | point kept, then a `NaN` anchor inserted after it; it reaches the live edge only through a history read that covers it ("Realtime") |
| `q = 16` (first sample after a break) | point kept; the line resumes here |
| absence of rows without a preceding `q = 32` | no anchor — the value simply did not change |

The last row is the whole reason the marks exist: treating every row gap as a break would shred a
steady signal, and ignoring the marks would draw a straight line across hours of missing data.

Every layer takes one route. `BucketedHistoryWindow` keys its buckets on `segment`, the count of
`q = 32` markers strictly before each row, so a marker closes its own bucket, and the bucket's `breaks`
flag carries it. `BucketedRowFold` writes, after a kept bucket whose flag is set, one `NaN` column at
the bucket's converted timestamp plus one tick. One tick is below `timestamp(3)` resolution, so no real
row lands inside it, and it is added on the UTC side, clear of daylight-saving boundaries. `q = 16`
takes no branch: the next bucket resumes the line. A seed row carries its own `q`, so a seed marked
`32` opens the window inside a gap. The coarse layers hold the markers too (`scada-archive.md#layers`),
so a break splits a `Minute` or `Hour` bucket as it splits a `Raw` one. No decimator runs on the
client. `RealArchiveGapTests` drives the fold with rows lifted from the customer dump, one row per
bucket, including a 4 min 18 s absence carrying no marker. This route and its one tick are the only
place a `q = 32` row becomes a break: the live source reads no archive row, and its own breaks come from
the OPC UA session and the item status ("Realtime").

A null `v` sets the same flag, because a bucket is the finest resolution a read has: `min(v)`,
`max(v)` and the newest-value aggregate all skip a null, so without the flag a bucket holding data
and nulls together would come back as an ordinary column and the line would run straight across the
null run. The column's timestamp is the newest **non-null** sample's, coupled to the value beside
it; a bucket of nothing but nulls keeps `max(t)` and reads back `NaN` in all three series.
`scada-archive.md` records that `v` was never
null in the measured archive, so no integration fact can reach this: the seeded archive writes no
nulls either, and `ArchiveStatementTextTests` plus `BucketedRowFoldTests` pin the rule instead.

## Realtime

The live edge reads through `ILiveSource` (The provider surface, above). Every live source keeps this
contract:

- **Cold per call.** No sample flows until a subscription opens. Each subscription does its own work, and
  disposing it stops that work.
- **The sample stream never completes and never faults.** A failure inside a source is the source's to log
  or to report out of band, on `States` or `PenFaults`, and it emits nothing on the stream.
  `OpcUaLiveSourceTests` and `OpcUaLiveSourceRestartTests` assert that every subscription they open,
  through a server restart and a cut transport, ends with no `OnCompleted` and no `OnError`
  (`SampleRecorder.Terminations`).
- **A break is a `Sample` whose `Value` is null,** at a moment the source itself observes: the pen has no
  data from that moment until its next sample.
- **Timestamps are UTC.**
- **An identifier the source does not serve emits no sample** and leaves the stream running. The source
  reports it once on `PenFaults` (The pen subscriptions, below).
  `OpcUaLiveSourceTests.TwoUnknownPens_RaiseOneFaultNamingBoth_AndASecondSubscriptionOfTheSameSetRaisesNone`
  pins it.
- **`States` replays the current state to each subscriber,** `Connecting` before the first session. It and
  `PenFaults` are hot, never fault, and complete only when the source is disposed.

Ordering is not part of the port: the chart appends a pen's sample only past that pen's last column
(`DA-7`) and drops any other.

Either terminal message breaks the contract, and neither crashes the window. `TrendCoordinator` catches
an `OnError` after the `Switch` and ahead of the `Publish`: `ReportTerminalFailure` writes one
`RealtimeFailures` entry and swaps in `Observable.Empty`, which completes `RealtimeBatches` and drops the
subscription to the pen set. Nothing rebuilds it, so the live edge stays empty for the rest of the
window's life (No failure stops at the log, below). An `OnCompleted` is not caught: `Switch` idles on a
completed inner stream, so the live edge goes silent with no entry until the next `SetPens` opens a new
subscription. `TrendCoordinatorTests.ALiveSourceThatCompletes_LeavesTheLiveEdgeSilentUntilTheNextSetPens`
pins the second case.

Batching, the union timeline and the hand-off to the UI scheduler happen above the live source, in
`TrendCoordinator` (`charting.md`). The coordinator also republishes `States` as `LiveSourceStates` and
`PenFaults` as `LivePenFaults`, each built once in its constructor with `ObserveOn` the UI scheduler. It is
the one holder of `ILiveSource` in the UI besides `TrendWindow`: `git grep -n "ILiveSource" --
SemiPlot/SemiPlot.UI` prints `Bridge/TrendCoordinator.cs` and `MainWindow/TrendWindow.cs` lines only. The
status bar follows `LiveSourceStates` (The session state the live source reports, below).
`TrendChartViewModel` reports each `LivePenFaults` error through
`ResultReporting.TryReportFailure(MessagePanelViewModel, IError, ILogger)`, which maps it with
`ArchiveFailureMapper` and so keeps the `Warning` of its kind; the `ReportFailure(Exception)` path would
wrap it as an `ExceptionalError` and show it as an `Error`.

The pen set can change while the viewer runs. `TrendCoordinator` holds it in a
`BehaviorSubject<IReadOnlyList<int>>` seeded from its constructor's pens and subscribes through
`Select(_liveSource.Subscribe)` and `Switch()`, ahead of the `Buffer`. `SetPens` pushes the list it
is handed, which the chart builds fresh for each call, and the chart calls it only when the id set it
shows differs from `PenIds`, the subject's current set, so the coordinator carries no change check of its
own. The chart hands it every pen it shows, hidden ones included.
`RealtimeBatches` stays one published stream: the chart's one subscription, which lives as long as
the window, sees no change. A switch disposes the old subscription, which deletes its OPC UA
subscription, and opens a new one on the same session; measured, the delete takes 33 ms and the new
subscription is created in 2 ms. The new subscription's first notification of each pen carries the
newest stamp already delivered and that value again, which the chart drops under `DA-7`. A change made
inside the switch's gap arrives as the pen's newest value at its own stamp, and the changes before it in
that gap are lost, as a queue of 1 loses them anywhere. The switch writes no break:
`OpcUaLiveSourceTests.ASecondSubscriptionOfTheSameSet_OnTheLiveSession_WritesNoBreak_AndStartsAtEachPensNewestStamp`
pins it. Every set the chart pushes holds only pens of the provider's own catalogue. `Dispose` completes
the subject.

One read clamps at the viewer's own clock: the fresh tail of a coarse history read ("Layer ladder").
That holds because the SCADA, its archive and the viewer share one machine
`[DEC:machine-time-zone]`. Reading the archive's `now()` on the open connection would remove even that
dependency, at the cost of one round trip per coarse read and a clock no test can set. A skew larger
than the coarse layer's own lag skips the tail, which draws no wrong row: the series stops at the
coarse newest, as it did before the tail existed.
`PostgresHistoryReadTests.TheRangeTheChartAsksForReachesTheRawLayersNewest` and
`AClockBehindTheFreshSeamReadsNoTailRow` pin the clamp under a fixed clock. A test fixes the provider's
clock by passing a `TimeProvider` to `ArchiveProviderFactory.Build`, which registers it ahead of
`AddPostgresData`'s `TryAddSingleton(TimeProvider.System)`. The live source reads no clock of the
viewer's: every stamp and every break it writes comes from the server's `SourceTimestamp`.

`TrendChartViewModel.ApplyRealtimeBatch` writes one `Debug` line per batch it applies, "Live batch
applied: {ValueCount} values, {LagMilliseconds} ms after the newest", where the lag is the apply moment
minus the newest value's `TimestampUtc`. Breaks are left out of the count and of the newest stamp, and a
batch of breaks only writes no line. The 1 s acceptance figure of the live edge is read from this line
on the SCADA, not asserted. `TrendChartViewModelTests.ALiveBatch_LogsOneLagLineFromItsNewestValueNotFromABreak`
pins the line.

### The break ahead of a first live sample

The chart writes this break, in `TrendPenState`, and only the chart. The source cannot: it carries no
chart state, and `Switch` restarts its subscription on every pen-set change, so a break it wrote ahead
of each subscription's first sample would land past every kept pen's last live column and draw a gap
after each catalogue change. The chart knows each pen's last column and whether the pen's line is
already joined to the live stream. A chart-side break also carries a meaning of its own: the chart marks
it `BreakKind.Ahead` and shades its span grey on the drawn pen (`charting.md#the-ahead-break-shade`), and
a source's break, a lost session or a `Bad` status, keeps the ordinary look.

The rule, per pen (`TrendPenState`'s `LiveJoin`; the full table is in
`charting.md#per-pen-plottable-envelopeline`):

- A pen's line is **joined** to the live stream once a live value has reached it, taken or not, except
  for a rejected value after a source break (below). A new `TrendPenState` starts `Unjoined`, so the
  window's first live sample of every pen, and the first sample of a pen a catalogue read adds, are first
  samples.
- A live value that reaches an unjoined line at or before the line's last column (the line already
  covers that moment, as when the archive already holds a slow pen's last change) is rejected at `Raw`
  and folded at a coarse layer, as for a joined line, and the line joins.
- A live value that reaches an unjoined line past its last column, or an empty line, goes through
  `EnvelopeLine.AppendAfterBreak`: a `NaN` column 1 ms ahead of the value, then the value, and the line
  joins. No `NaN` column is written when the line already ends on a gap, or when 1 ms ahead of the value
  is not past the last column. At a coarse layer the value opens a new column past the break, and the
  values after it fold into that column, which keeps its opening X (`EnvelopeLine.FoldIntoLastColumn`),
  so the live line resumes from the start of its first live bucket (`charting.md`).
- A break from the source appends its `NaN` column and unjoins the line (`UnjoinedBySourceBreak`),
  whether the line takes the column or rejects it. A rejected break is the case of a pen whose live
  samples all lay inside the archive's span, a constant pen whose archive holds a later row: without the
  unjoin, the first sample of the next session would draw a straight line across the outage. A value
  the line then rejects, such as a recreated session's replay (The breaks the source writes, below),
  re-confirms the line (`ReconfirmedAfterSourceBreak`), which holds it to the live edge again while it
  ends on a value (`charting.md#the-hold-to-the-live-edge`), but does not join it: the next value past the
  last column still opens behind a `NaN` column, and that column is `BreakKind.Ordinary`, because it
  stands in for the outage. This holds at every layer: while the line is unjoined by a source break, a
  coarse layer rejects a value at or before the last column too, rather than fold it into the bucket the
  outage cut.
- A history apply that drops live columns past the read's requested end unjoins the line (`Unjoined`),
  because the live run it held is gone and the read does not reach past its end. At a coarse layer the
  same holds for a read that replaces the bucket the newest live value folded into, the live bucket or an
  archive bucket a joined pen's values fold into, while that value lies past both the read's requested end
  and its last row: the value is gone with the bucket. `EnvelopeLine.ReplaceColumns` returns whether it
  lost a live value either way. The latest unjoin
  wins, so after a source break and then such an apply the next value past the last column opens behind an
  ahead-break. A sticky window's read keeps its right margin past the live edge (What one history query
  covers, above), so following the edge never takes this branch. A pan into
  the past does take it: its read ends before the live tail, and between the archive's newest row and
  the clock (the archive lag) those dropped columns were the only copy of the OPC UA points. A return to
  the edge within the archive lag therefore shows a gap there, behind a new ahead-break, until a later
  history read covers that span from the archive.
- A pen-set change touches no `TrendPenState`, so a kept pen stays joined.
- `CurrentValue` takes a live value only when the line took it, and stays the last real value.

The ahead-break is a live append like any other: `ReplaceColumns` keeps it while it lies past the new
history end and drops it once a read covers it, so the gap closes only when a history read reaches the
first live sample, and nothing closes it on its own `[DEC:opc-live-edge]`.

The step is 1 ms because the chart's X axis keeps milliseconds only (`LocalTimeAxis.ToAxis` is
`ToOADate`): a break one tick ahead maps onto the sample's own X, and `EnvelopeLine` rejects an append
at or before the last X (`DA-7`).
`TrendPenStateTests.Realtime_BreakOneTickAfterTheValue_IsRejectedAndOneMillisecondAfterIt_AddsANaNColumn`
pins that step for a break after a value, and the `Realtime_AFirstValue...`, `Realtime_OnAnEmptyLine_...`,
`Realtime_ARejectedBreak_...`, `Realtime_ARejectedReplayAfterARejectedBreak_...`,
`Realtime_ASourceBreakThenADroppingHistoryApply_...`, `Realtime_AfterAHistoryApply_...`,
`FoldRealtime_AFirstValue...` and `FoldRealtime_AReplayAfterARejectedBreak_...` cases of
`TrendPenStateTests` pin the rule.
`TrendChartViewModelTests.ACatalogueThatAddsAPen_BreaksOnlyTheAddedPensFirstLiveValue` pins the pen-set
change, and
`OpcUaLiveEdgeJourneyTests.AServedValue_ReachesTheChartBehindABreak_AndMovesTheStickyWindowToTheNewestStamp`
drives a value served over OPC UA by the demo server to a live column 1 ms behind its `NaN` column, a
`BreakKind.Ahead` one.

### The OPC UA live source

`OpcUaLiveSource` in `SemiPlot.DataSource.OpcUa` serves the live edge from the SCADA's built-in OPC UA
server over the OPC Foundation client stack (`OPCFoundation.NetStandard.Opc.Ua.Client` 2.0.1). Its
endpoint is `opc.tcp://<host>:<port>` from the `opc/` section (Configuration, The OPC UA section, below).
The server, as measured, is in `scada-archive-observed.md#the-opc-ua-server`.

| Type | Holds |
| --- | --- |
| `OpcUaLiveSource` | The port: `Subscribe`, `States`, `PenFaults`, the lazy start, `Dispose` and `DisposeAsync` |
| `OpcUaSession` | The connect loop, the session's events, the browse on every new session id, the published state |
| `ServerCalls` | The stack calls: discovery, connect, browse, subscription create and delete, close |
| `SerialLoop` | The one reader every state input and every notification runs on; a work item that throws is logged and the next one runs |
| `ConnectBackoff` | The waits between passes of the connect loop |
| `LiveSessionStates` | The state as a pure function of the stack's transitions, the connect loop's outcomes and the grace |
| `PenSubscriptionSet`, `PenSubscription` | The open subscriptions, the project they are built under, and each subscription's samples and breaks |
| `SubscriptionPlan`, `ProjectFolder`, `EndpointChoice`, `LiveValue` | The subscription shape, the project folder and pen nodes, the endpoint pick, the value mapping |
| `DeliveredStamps`, `UnservedPens` | The newest stamp per pen and the breaks placed after it; the pens a subscription cannot serve |
| `ClientApplicationConfiguration`, `StackTelemetry` | The validated client configuration; the stack's telemetry over the container's `ILoggerFactory` |

**One session per process, started by the first `Subscribe`.** The container builds the source when
`TrendWindow.Build` resolves it, and its constructor opens nothing:
`OpcUaCompositionTests.ResolvingAndDisposingWithNoSubscription_ReturnsAtOnce_AndTheSourceNeverLeftConnecting`
pins that. The first subscription starts the connect loop, which lives until the container disposes the
source. The source takes `DefaultScheduler.Instance` for its timers itself, not the `IScheduler`
`AddPostgresData` registers.

**The connect loop owns discovery; `ManagedSession` owns reconnection.** One pass:

1. `GetEndpointsAsync` on the configured URL under a 10 s bound. A listener that accepts and never answers
   holds it 90 s.
2. `EndpointChoice.Pick` takes the endpoint with `SecurityMode None`, policy `#None` and an anonymous user
   token. It overwrites the endpoint's `EndpointUrl` with the configured one and keeps its token policies
   as the server advertised them, so the `PolicyId` the client presents is always the one that server
   issued (`Anonymous` on Simple-Scada, `1` on the stock 2.0.1 server). Discovery stays for that reason
   even though the URL is pinned. No such endpoint is the setup fault `EndpointRefused`.
3. A `ConfiguredEndpoint` with `UpdateBeforeConnect = false`, so neither the first connect nor a
   reconnect dials the address the server advertises; with the default `true` the session moved to the
   server's own URL (measured). `ManagedSession` connects with session name `SemiPlot`, a 60 s session
   timeout, an anonymous identity and `MaxTotalReconnectTime = Timeout.InfiniteTimeSpan`; at a finite
   bound the session passes through `Failover` to `Disconnected` (measured). `ConnectAsync` retries
   inside the call until a server answers.
4. A browse of `Objects` for the project folder under a 10 s bound, because it runs on the source's loop:
   the one child whose browse name is `root` in the namespace `http://simple-scada.com/UA` and whose
   string identifier matches `<projectId>root_<n>` (`ProjectFolder.ProjectIdOf`). None, or more than one,
   is the setup fault `ProjectMissing`, and the source closes the session. A browse that fails or passes
   its bound ends the pass as one no server answered.
5. Every open subscription is created, and the state turns `Connected`.
6. The pass waits until the session reaches `Closed` without the source closing it, or the source is
   disposed, then closes and disposes the session.

A pass that throws is logged at `Error` and counts as one no server answered, so no failure ends the
connect loop; the configuration is validated again on the next pass if its validation was what threw. A
pass that still held its session when it threw lets the session go as lost first.

`ConnectBackoff` sets the wait before the next pass: 1 s doubling to a cap of 5 s while no server
answers, 30 s after a setup fault, 1 s after a session that ended. A viewer started while the project is
stopped therefore sees the server within about 5 s of its start, plus the 2 s a refused attempt costs on
Windows. A setup fault is one the operator fixes in the SCADA or in `opc/`; the source reports it once and
keeps trying, so a fix, or a project folder that appears after the server first answers, reaches the
chart without a restart. `ConnectBackoffTests.ASetupFault_IsReportedOnce_AndRetriedAfterThirtySeconds`
pins both setup faults over `LiveSessionStates` and `ConnectBackoff`, because the demo server always has a
`None` endpoint and a project folder.

Once connected, the stack recreates the session and its subscriptions in place after a server restart,
a killed server process or a transport outage longer than the session timeout, and the same
`MonitoredItem` handlers fire again (measured). Each `Connected` whose session id differs from the last
one, a first connect or a recreation, browses `Objects` again (`LiveSessionStates.IsNewSession`); a
reactivated session keeps its id and is not browsed. `ProjectFolder.Next` decides what the browse means:
the same project ID keeps the subscriptions and reads every item's status, a changed one deletes and
recreates every open subscription, because the recreated items still name the old nodes, and none is
`ProjectMissing`. The project-ID change is pinned by unit tests only (`ProjectFolderTests.Next_...`),
because the demo server's project ID is a constant.

**The configuration is validated, and its stores stay empty.**
`ClientApplicationConfiguration.CreateValidatedAsync` builds an `ApplicationConfiguration` of type `Client`,
application name `SemiPlot`, URI `urn:localhost:SemiPlot`, four directory stores under
`<config-dir>/opc/pki/{own,issuer,trusted,rejected}` (`own` with subject `CN=SemiPlot`),
`TransportQuotas.OperationTimeout = 15000` and `DefaultSessionTimeout = 60000`, runs `ValidateAsync`, and
loads no application certificate. An unvalidated configuration makes `ManagedSession.ConnectAsync` retry
forever. Validation and every session create no file or folder
(`ClientApplicationConfigurationTests.ValidationCreatesNoFileOrFolder`), and a section folder is read
top-level only, so the store paths never reach the `opc/` loader.

**One loop serializes the source.** The stack raises `ConnectionStateChanged`, `KeepAlive` and every
notification on its own threads; each handler only queues the work onto `SerialLoop`, which runs every
state change, every subscription creation and delete, and every emission in order. `LiveSessionStates`,
`PenSubscriptionSet`, `DeliveredStamps`, `UnservedPens` and `ReportedUnservedSets` therefore hold no lock.
A work item that throws is logged at `Error` and the loop runs the next one, so one failure stops no
state change and no notification after it. An event whose sender is not the current session reports
nothing. `States` is a `BehaviorSubject` seeded `Connecting` and `PenFaults` a `Subject`, each behind
`Subject.Synchronize` and exposed through `AsObservable()`.

**A close the source issues is not a loss.** Before the source closes a session of its own (a setup
fault, a failed browse) it lets go of it (`OpcUaSession.EndSession`), and an event whose sender is not
the session the source holds reports nothing, so the `Closing` and `Closed` that follow publish no
`Unreachable` and write no break. Disposal stops the loops first, and a stopped loop publishes nothing.
`DisposeAsync` cancels both loops; the pass that holds a session closes it under a 2 s bound, because a
`CloseSession` to a peer that never answers would wait the 15 s operation timeout, then disposes it;
then both subjects complete, in a `finally`, so a failed close leaves neither open. `Dispose` runs that
close on the pool and waits at most 3 s (`Task.Run(...).Wait`): the container is disposed synchronously
when `Main` returns, and no continuation may need `Main`'s thread. An abandoned close finishes on the
pool, and a fault it ends in is still logged. `OpcUaSessionTests.DisposeOnAConnectedSource_UnderAContextThatDropsEveryPost_ReturnsWithinItsBound`
and `DisposingTheSourceWhileItReconnects_ReturnsWithinItsBound_PublishesNothing_AndDialsNoMore` pin it.

**Logging.** The stack logs through the container's `ILoggerFactory`. While the server is down its client
channel logs one `Error` per failed connect attempt, 3 to 33 s apart (measured), which says nothing the
source's own state line does not, so `AddOpcUaLiveSource` adds a filter that raises
`Opc.Ua.Bindings.UaSCUaBinaryClientChannel` to `Critical`; no other category of the stack is filtered
(`OpcUaCompositionTests.TheStackChannelLogsOnlyCriticalLines_AndEveryOtherCategoryKeepsItsLevel`). The
`Error` "RepublishAsync is only supported when the session uses the classic subscription engine", which
the stack writes after a reactivated session, stays: it is rare and names a stack defect. The source's
own lines: one `Information` per published state, "OPC UA session to {Endpoint}: {State}, status
{Status}", carrying the status of the event or of the last failed keep-alive (the event that leaves
`Connected` carries none, measured); `Information` with the exception for a failed browse; one `Warning`
per pen fault, per item the server did not create for another reason, per failed subscription create or
delete and per session dispose that threw; one `Debug` per break it writes; `Debug` for a failed
discovery or connect and for a close that gave up at its bound; `Error` for a connect pass that threw and
for a loop work item that threw.

### The session state the live source reports

`LiveSessionStates` decides every state the source publishes on `States`:

- `Connecting` at start.
- `Connected` on every connect, the first included, which the loop reports itself because the stack
  raises no event before `ConnectAsync` returns.
- Leaving `Connected` without the source closing the session publishes `FaultedBy(Unreachable)` and a
  loss, on which every open subscription writes its loss breaks (The breaks the source writes, below).
- The grace: `OpcUaLiveSource.Grace`, 30 s after the loop starts, publishes `FaultedBy(Unreachable)`
  once when nothing was published before it; after a first session or a setup fault it publishes nothing.
- A setup fault publishes `FaultedBy(EndpointRefused)` or `FaultedBy(ProjectMissing)`.
- A pass that ends with no session publishes `FaultedBy(Unreachable)` once the grace no longer owns the
  state, so a server that goes down after a setup fault reads as unreachable rather than as the fault it
  answered with; within the grace it publishes nothing.
- A state is published only when `IsConnected` or the fault's kind changes, so a setup fault retried
  every 30 s and the stack's repeated `Reconnecting` events publish nothing new.

The grace exists because the stand starts the viewer and the OPC UA server side by side, and the demo
server takes 1.5 to 3.5 s to start warm and 14 to 17 s cold: without it a stand start would write a
fault entry and a recovery entry. `Connecting` is neutral on the bar, so the grace hides nothing; a viewer
beside a stopped project shows `Connecting` for 30 s and then the fault. A session that is lost reports
the fault as soon as the stack leaves `Connected`: 4 to 5 s after the loss with the 5 s keep-alive, and
14.7 s for a peer that stalls with its socket open. It returns 0.4 to 4.7 s after the server answers
again (measured, `scada-archive-observed.md#the-opc-ua-server`).

`MainWindow/AppStatusBarViewModel` renders the state as the bar's second indicator, after the archive's
and in the same shape: `IsLiveConnected`, `IsLiveFaulted` and `LiveConnectionText`, "OPC UA connecting",
"OPC UA connected" or "OPC UA not answering". `Connecting` carries neither palette class, so the
indicator keeps the bar's ordinary text colour during the grace. A fault is mapped by
`ArchiveFailureMapper.Map` into one panel entry, `Warning` for `Unreachable` and `Error` for
`EndpointRefused` and `ProjectMissing`; the first `Connected` after a fault writes one `Info` entry,
`LiveConnectionRestored`. `TrendWindow.Build` hands the bar `coordinator.LiveSourceStates`, and the bar is
the indicator's single writer. The chart's live hold reads the same `States`, through the same
`LiveSourceStates`, for "the session is up": `TrendChartViewModel` hands `IsConnected` to
`Chart/ChartLiveHold`, and only `Connected` holds a pen (`charting.md#the-hold-to-the-live-edge`).
`LiveSessionStatesTests` pins the decisions, `AppStatusBarViewModelTests` the indicator, and
`OpcUaSessionTests` the sequences against the demo server: `ARunningServer_TakesConnectingThenConnected`,
`NoServer_TakesUnreachableAfterTheGrace_ThenConnectedOnceAServerStartsOnThePort`,
`AServerRestartedOnItsPort_TakesConnectedUnreachableConnected`,
`AListenerThatAcceptsAndCloses_KeepsTheSourceInDiscoveryWithNoSetupFault` and
`AListenerThatAcceptsAndNeverAnswers_IsDialledAgainAfterTheDiscoveryBound`, which pins the 10 s discovery
bound.

### The pen subscriptions

`Subscribe(penIds)` is an `Observable.Create` that registers one `PenSubscription` with the source,
starting the loop on the first call, and returns a disposable that unregisters it. Registered while the
session is `Connected`, its OPC UA subscription is created at once; otherwise at the next `Connected`. A
pen set registered while the session is down emits the loss breaks of its pens at once, as a set open
over the loss would have (The breaks the source writes, below). A creation runs under a 10 s bound, so a
session that drops in the middle does not hold the loop; a failed creation logs a `Warning`, its
subscription is deleted, and the creation is tried again 10 s later while the session stays connected,
and at the next `Connected`. Disposal deletes the OPC UA subscription at once while the session is
connected. One disposed while the session is down, or whose delete failed, goes to a stale list and gets
one more delete at the next `Connected`, because `ManagedSession`'s delete waits for the session to
return and would hold the loop; it emits nothing in the meantime. A failed delete logs a `Warning`, and
every delete is bounded at 2 s and ends when the source is disposed.

`SubscriptionPlan` shapes every subscription: publishing interval 100 ms, `KeepAliveCount` 10,
`LifetimeCount` 600 (the stack's default of 100 logs "A smaller minLifetimeInterval 10000ms than session
timeout 60000ms"), `TimestampsToReturn.Both`. Each pen is one monitored item: sampling interval 100 ms,
`QueueSize = 1`, the oldest value discarded, node `ns=<index of http://simple-scada.com/UA>;s=<projectId><penId>`
(`ProjectFolder.NodeOf`), and the pen id as its `Handle`. The queue is 1 because Simple-Scada answers a
queue above 1 with an empty `Good` first value stamped `MinValue`, so a static variable never gets its
value; the demo server does not reproduce it, so
`SubscriptionPlanTests.AMonitoredItemsQueueIsOne_BecauseSimpleScadaSendsAnEmptyFirstValueAboveIt` pins
the 1 and its name carries the reason. With a queue of 1, the changes between two publishes reach the
source as the newest one only.

`LiveValue` maps each notification, which the handler queues onto the loop; each one emits one list,
and the coordinator buffers them:

- A scalar of a numeric built-in type (`SByte`, `Byte`, `Int16`, `UInt16`, `Int32`, `UInt32`, `Int64`,
  `UInt64`, `Float`, `Double`) converts to `double`. The measured SCADA serves a bool as `Byte`.
- `Good`, with or without info bits (the measured `0x00000010`), and `Uncertain` draw the value. `Bad` is
  a break (The breaks the source writes, below), and so is a `NaN` or an infinity, which the chart would
  otherwise draw as a value and show as the pen's current reading.
- The stamp is the `SourceTimestamp`, else the `ServerTimestamp`; a value with neither is dropped with a
  `Debug` line.
- Anything else, `Boolean`, `String`, `DateTime`, an array or a null, is not numeric: the pen joins its
  subscription's unserved pens, and a pen with a delivered value also emits one break at the
  notification's stamp, raised to the pen's newest delivered stamp (The breaks the source writes, below).

**One entry per subscription for the pens the source cannot serve.** `UnservedPens` collects a
subscription's unknown pens, whose item reads `BadNodeIdUnknown` (the real server answers an unknown node
with `Created = false` and that status), and its non-numeric pens. It reports them as one
`LiveSourceError` of kind `PensUnserved` that lists both groups, once every item has answered (a value,
a `Bad` or an unstamped value counts as an answer) or 5 s after the subscription is created, whichever
comes first, and again when the set grows later: a value that turns non-numeric, an item unknown after a
recreated session, whose item statuses the source reads again. A pen the server serves again, a created
item or a numeric value, leaves the set, so a later report names only the pens unserved then; a set
that only shrinks writes no entry. `ReportedUnservedSets` keeps every set the
process has reported, so a pen-set change or a recreated session that meets the same set writes nothing;
a site with retired variables would otherwise get one entry per pen set at every catalogue change, since
the panel coalesces only an entry equal to its newest one. An item the server refuses for any other
status logs a `Warning` and is not waited for. A pen unserved from its first notification draws the
archive's values only; a pen that turns non-numeric keeps the live run it drew up to its break.
`PenSubscriptionTests` routes a `Bad`, a non-numeric, a numeric-then-non-numeric and an unstamped
notification through one subscription, raised by the stack's own `MonitoredItem.SaveValueInCache`, with no
session.

### The breaks the source writes

**A lost session writes one break per pen the source has delivered, at once.** When the session leaves
`Connected` without the source closing it, every open subscription emits, for each of its pens with a
delivered value, a break 1 ms after that pen's newest delivered `SourceTimestamp`
(`DeliveredStamps.LossBreaks`), as one list. The stamp is the server's clock, so a skew between the
SCADA's machine and the viewer's cannot push the break past the next session's samples, and 1 ms is the
axis' smallest step (The break ahead of a first live sample, above). `DeliveredStamps` keeps the newest
stamp per pen for the process, across subscriptions, so a session lost in the moment after a pen-set
change still breaks the kept pens: a subscription its subscriber already let go of emits nothing and
marks no pen, and the new pen set, registered behind the loss, emits the breaks itself
(`PenSubscriptionSetTests.ALossQueuedAheadOfAPenSetChange_BreaksTheKeptPensOnTheNewSet`). A pen with no
delivered value gets none: its line is unjoined or its last column is not live. A pen whose last
emission is already a break gets no second one.

Every first notification of a restarted server carries the server's start instant, later than any
break, so after a project stop, a restart or a kill the break never lands at or after the next
session's first sample. A server that kept running is the one case where it lands after. A session the
stack reactivates, after an outage shorter than the 60 s session timeout, delivers nothing for an
unchanged pen. A session it recreates (a 70 s outage, measured) delivers an unchanged pen's value again
with its original `SourceTimestamp`, which lies before the break, and the chart drops it (`DA-7`); a pen
that changed during the outage arrives with the stamp of its newest change. So an unchanged pen keeps its
gap until its next change, which is honest about the outage and costs a constant pen its line past the
gap until a history read covers it. Waiting for the next session to place the break exactly would leave
the outage without a break while it lasts.

The chart's live hold follows the same two cases (`charting.md#the-hold-to-the-live-edge`). A loss ends
every hold, because the session leaves `Connected`. A recreated session's replay re-confirms an unchanged
pen: when the pen's loss break was rejected, because the archive holds a later row, the line still ends on
that row and is held from it to the edge again, and the pen's next change still opens behind an ordinary
`NaN` column, so no line crosses the outage. When the loss break was appended, the line ends on the gap and
holds nothing until the next change. A reactivated session re-delivers nothing for an unchanged pen, so
the pen stays unheld until its next change; a read of the current values after a reactivation would
close that, and it is a backlog entry (`docs/plans/backlog.md`). A value that turns non-numeric ends the
pen's hold through its break, below, so the chart never holds a value the server no longer states.

**`Bad` writes a break.** Its stamp is the notification's `SourceTimestamp`, else its `ServerTimestamp`,
raised to at least 1 ms past the pen's newest delivered stamp (`DeliveredStamps.BadBreak`). A `Bad` with
neither stamp and no delivered value writes nothing, and a pen whose last emission is a break takes no
second one, whether or not it has a delivered value.

**A value that turns non-numeric writes a break.** A pen with a delivered value whose notification is no
longer a number emits one break at that notification's `SourceTimestamp`, else its `ServerTimestamp`,
raised to the pen's newest delivered stamp when it lies behind it (`DeliveredStamps.NotNumericBreak`). The
raise stops at that stamp: a break placed where no sample lies, such as 1 ms past it, would move the
chart's newest sample, the hold's edge, to a moment the server never stated. It still reaches that stamp,
because the coordinator orders one pen's samples in a 100 ms batch by stamp: a break behind the newest
value in the same batch would reach the chart ahead of the value, and the value would rejoin the line and
hold it. At the newest value's stamp the stable sort keeps the value first, and the chart rejects the break
as at or before the line's last column; a rejected break still unjoins the line, which ends its hold
(`TrendChartLiveHoldTests.ABreakAtTheStampOfTheValueAheadOfItInOneBatch_EndsTheHold`). A pen with no
delivered value or a notification with neither stamp writes nothing, and a pen whose last emission is a
break takes no second one.

`DeliveredStampsTests` pins the rules.
`OpcUaLiveSourceRestartTests.AServerRestartedOnItsPort_BreaksEachPenOnceAtItsNewestStamp_BeforeTheNewSessionsSamples`
restarts the demo server on its port and finds exactly one break per pen at its newest stamp plus 1 ms,
all before the first sample of the new session, which is stamped at or after the restart.
`ACutTransport_BreaksEachPenOnce_ResumesPastTheBreaks_AndASubscriptionDisposedInTheCutStaysQuiet`
cuts a `LoopbackRelay` between the source and the server for 8 s, longer than the detection and shorter
than the session timeout: one break per pen, and samples resuming past the breaks. It cannot tell a
reactivated session from a recreated one, which the measurement does (a 5 s cut keeps the session id,
`scada-archive-observed.md#the-opc-ua-server`). What a closed subscription emits is pinned without a
session by `PenSubscriptionTests`; that its OPC UA subscription is deleted at the next `Connected` has no
test, because the demo server exposes no subscription count (`testing-strategy.md`).

### The connection state the catalogue read reports

The provider reports no connection state. `Bridge/PenCatalogueSync` derives
the state from its own catalogue reads and exposes it as `ConnectionStates`, an
`IObservable<ArchiveConnectionState>` on the UI thread that its `Dispose` completes. `Bridge/ArchiveReadLoop`
decides each state and emits it through the sink `PenCatalogueSync` hands it. `MarkerSync` hands no sink,
so the marker loop raises no state.

- A read fails for lack of a connection when its first error is an `ArchiveError` of kind
  `ArchiveFault.Unreachable`: a refused or reset socket, a client bound firing, an `NpgsqlException`
  without a SQLSTATE, or a server that refuses or ends the session with SQLSTATE class `08`,
  `57P01` (admin shutdown), `57P02` (crash shutdown), `57P03` (starting up) or `53300` (too many connections). This is
  the connection class. `57014` is not in it: the server answered and ended the statement, so it stays
  `QueryTimedOut`. Any other kind, and an error that is not an `ArchiveError`, is not connection class.
- The first successful read emits `Connected`, and so does the first success after a raised fault;
  every other success emits nothing. The state goes out before the read is applied, so an apply that
  throws leaves the state right.
- Three connection-class failures in a row raise the fault, not one: Npgsql opens a fresh physical
  connection after a reset, so one failed read is ordinary. The state carries
  `ArchiveFault.ConnectionLost` with the failing error's host, port and database, and the threshold
  `ArchiveConnectionState.ConsecutiveFailuresBeforeFault` (3) as its detail, not a running count.
- The fault is raised once per outage. The loop emits nothing more until a read succeeds. A failure of
  another kind ends the run of connection-class failures.
- So after the fault, a failure of another kind leaves the bar on the fault while the server answers.
  The indicator turns back only at the next success. The other kind's entry reaches the panel at once,
  because the failed reads before it count toward the third in a row.
- Each connection-class failure writes one log warning naming the read and its run of connection-class
  failures (`ConsecutiveConnectionFailures`). It never reaches the panel from the loop; the bar's one
  entry stands for it. A failure of another kind logs its run of failed reads of any kind
  (`ConsecutiveFailures`).
- A read that throws instead of answering adds one panel entry at once and leaves both counts as they
  were. A read that lands after `Dispose` emits nothing.

`TrendWindow.Build` calls `ReadNow()` right after `Start()`, so the first `Connected`, or the run of
failures, starts when the window opens (`overview.md#the-read`). Each read waits for a connection under
the 15 s connect bound (`PostgresConnectionSettings.ConnectTimeoutSeconds`) and runs its statement under
its own 5 s bound (Configuration, below), and the loop waits 5 s after each read before the next. The
fault lands with the third failed read of that sequence; how long that takes depends on where in the
wait the outage begins and on which bound ends each read.

One rule covers the two readers of the shared `NpgsqlDataSource` that repeat on their own: the
catalogue loop and the marker loop. A connection-class failure is logged by the reader that meets it
and reaches the operator through this state and the bar's one `ConnectionLost` entry. Any other kind
reaches the panel through the two loops' own route, from the third failure in a row, coalescing repeats
into the newest entry (`overview.md#a-failed-read`). A `TableMissing`, an `AccessDenied` or a role
`statement_timeout` on `trends` reaches the panel through the minimap's band reads, every 2 to 60 s and
the first failure of each outage (`trend-interaction.md#archive-overview-minimap`), and through the
chart's history read on the next gesture. The band reads run only while the minimap has an extent and a
drawn pen. So an outage makes one entry from the catalogue state, and the minimap's first failed band
read of the outage adds an `Unreachable` entry of its own. One case reaches only the log: a stalled
marker statement (No failure stops at the log, below). The OPC UA session is no reader of this data
source; its state is the bar's second indicator (Realtime, The session state the live source reports).

`MainWindow/AppStatusBarViewModel` renders the state: the archive indicator reads connected or not, and a
fault is mapped by `Messages/ArchiveFailureMapper.Map` into one `Warning` entry in the message panel.
The archive indicator has a single writer, `PenCatalogueSync.ConnectionStates`, which `TrendWindow.Build` hands its
constructor before it starts the sync, so no state precedes the subscription. The first catalogue read
reports `Connected` too, so the recovery entry is written only once a fault has been seen; otherwise
every launch would announce a connection it never lost. The handler is wrapped: the sync raises the
state from inside its read loop, ahead of the read's apply, so a throw out of the handler would skip
that read's apply.

## Error semantics

| Situation | Provider result | What the operator sees |
| --- | --- | --- |
| Connection refused or DNS failure at startup | failed `Result` | The startup-failure window opens, titled "No connection to the archive", naming the host and port, with a remedy |
| Connection lost mid-session | failed `Result` on the query | Chart keeps the data it has |
| No OPC UA session: the server does not answer within the 30 s grace, or a session is lost | `LiveSourceFault.Unreachable` on `ILiveSource.States`; the source keeps connecting; a lost session writes one break per delivered pen | The OPC UA indicator turns to the fault state and one `Warning` entry appears; the chart keeps the archive and its live points; the first session after it restores the indicator and adds one `Info` entry |
| The OPC UA server offers no `None` endpoint with anonymous access, or holds no project folder | `LiveSourceFault.EndpointRefused` or `ProjectMissing` on `States`; retried every 30 s | One `Error` entry; the chart draws the archive only |
| The OPC UA server does not know a pen, or serves a value that is not a number | `LiveSourceFault.PensUnserved` on `ILiveSource.PenFaults`, once per pen set | One `Warning` entry naming the unknown and the non-numeric pen ids; those pens draw the archive only, and a pen that turns non-numeric breaks at that notification's stamp, raised to its newest delivered stamp |
| An `opc/` section that is missing, unreadable, unparseable or carries a bad value | the loader's failed `Result` | The startup-failure window, as for `connection/`; an unreachable OPC UA server never fails the start |
| Three consecutive catalogue reads fail for lack of a connection | `ArchiveFault.ConnectionLost` on `PenCatalogueSync.ConnectionStates`; the loop keeps reading | The status indicator turns to the fault state and one `Warning` entry appears; the first catalogue read that succeeds restores the indicator and adds one `Info` entry |
| A column the read needs is absent (`42703`) | `ArchiveFault.ShapeUnexpected` with the server's detail | "The archive has an unexpected shape" — run `semibase site`, then find what altered the table |
| Query timeout (`57014`) | `ArchiveFault.QueryTimedOut` | "The archive ended the statement"; `statement_timeout` is the `semiplot` role's own setting |
| The database does not exist (`3D000`) | `ArchiveFault.DatabaseMissing` | "The archive is not provisioned" — run `semibase site` |
| Credentials refused or a grant missing (`28P01`, `28000`, `42501`) | `ArchiveFault.AccessDenied` | "The archive refused access" — the user, password or grants |
| A relation or function a statement needs does not exist (`42P01`, `42883`) | `ArchiveFault.TableMissing` whose detail names every relation that statement touches, or `semiplot_register_new_pens()` | "The archive is not provisioned" — run `semibase site`, which creates them all |
| The catalogue re-read fails mid-session | failed `Result`; `PenCatalogueSync` keeps its snapshot | A connection-class failure logs one warning and goes to the bar's `ConnectionLost` entry, never to an entry of its own. Any other kind: nothing for the first two failures in a row, one log warning each; from the third one panel entry, coalesced with the ones after it. The next success applies everything stored since the last success |
| The marker re-read fails mid-session | failed `Result`; `MarkerSync` emits nothing and the chart keeps the markers it shows | As for the catalogue: a connection-class failure logs one warning and goes to the bar's entry, which the catalogue read raises; any other kind writes two log warnings, then one panel entry from the third failure in a row. Against a database without `semiplot_markers` that entry is a `TableMissing` Error about 10 s after the window opens, and the chart works without markers |
| `semiplot_tags` present but empty | empty pen list, success | The chart area's own empty state, naming `Edit` -> `Pens and groups` -> `Refresh pen list`: no key has been registered as a pen yet. Not a message-panel entry: an empty catalogue is a state, not something that happened |
| Archive present but no rows in the window | success, empty envelope list | Empty chart, no error |
| A check constraint refuses a written value (`23514`) | `ArchiveFault.ValueRejected` | "The archive refused the value"; the field reverts, and the message line says why |
| A group name another group already carries (`23505`) | `ArchiveFault.NameTaken` | "The name is already taken" |
| A write names a pen or group that is gone (`23503`, or no row touched) | `ArchiveFault.RowGone` | "The entry no longer exists"; a refresh shows what the catalogue holds now |
| A move or rename names a time marker that is gone (no row touched) | `ArchiveFault.MarkerGone` | "The marker no longer exists"; the chart drops the marker |

### Two error planes

Inside a data source a failure is whatever Npgsql, the OPC UA stack, the file system or the YAML parser
produced. At the boundary it is mapped onto one of the sealed public types in `SemiPlot.Core/Data/Errors/`,
with the original riding `.CausedBy(...)` for the log. The live source's type lives in Core too, not in
`SemiPlot.DataSource.OpcUa`, because `FailureSeverityTests` scans only the Core and UI assemblies for
error types: one declared in a data-source project would fall into `MapUnknown` as an `Error` with every
test green. The contract is the type and its fields; messages
are built in the base constructor and reach no operator, so tests assert on type and fields, never
on message text.

The rule that decides whether a type exists: **a public error type exists if and only if a distinct
operator-visible failure sentence exists.** Operator-visible states that are not failures — an
empty window, an empty `semiplot_tags` — travel in the success channel.

| Type | Fields |
| --- | --- |
| `ConnectionFileError` | section (`Connection` \| `Opc`), path, kind (`Unparseable` \| `MissingField` \| `OutOfRange` \| `HostNotIPv4`), field names |
| `ConfigurationSectionError` | section (`App` \| `Connection` \| `Opc`), directory, problem (`DirectoryMissing` \| `NoFiles` \| `Unlistable` \| `Unreadable` \| `DuplicateKey` \| `KeyConflict` \| `Unwritable` \| `KeyAbsent`), key, file names |
| `ArchiveError` | kind (`ArchiveFault`), host, port, database, detail |
| `LiveSourceError` | kind (`LiveSourceFault`: `Unreachable` \| `EndpointRefused` \| `ProjectMissing` \| `PensUnserved`), endpoint, unknown pen ids, non-numeric pen ids |

`ConfigurationSectionError` lives in `SemiPlot.Core/Configuration/` and is raised by the section
reader, ahead of any typed deserialize, so reaching the archive at all is not a precondition for it,
and by the settings save: `Unwritable` for a target or staging folder it cannot write, `KeyAbsent` for
an edited key no file carries (`overview.md#the-settings-window`).
Reading a section folder is the only file access on the connection path, so `ConnectionFileError`
carries no file-access kind. Both connection sections, `connection/` and `opc/`, raise it, and its
`Section` picks the operator's title; its message reads "The {Section} configuration folder '{path}' is
invalid ({kind})". The host rule and the port bounds both sections share are `NetworkAddress.IsIPv4`,
`LowestPort` and `HighestPort` in `SemiPlot.Core/Configuration`.

| `ArchiveFault` | Raised by | Detail |
| --- | --- | --- |
| `Unreachable` | a socket failure, a client bound firing, any `NpgsqlException` without a SQLSTATE, a session the server refuses or ends: SQLSTATE class `08`, `57P01`, `57P02`, `57P03`, `53300` | empty |
| `AccessDenied` | `28P01`, `28000`, `42501` | the username |
| `DatabaseMissing` | `3D000` | empty |
| `TableMissing` | `42P01`, `42883` | every relation the failing statement touches: `trends` for the history read, `semiplot_tags, trends` for the archive extent and the archive days, all three catalogue tables for the pen catalogue and every editor write, `semiplot_markers` for every marker statement; `semiplot_register_new_pens()` for the registration |
| `ShapeUnexpected` | `42703` | the server's own message |
| `QueryTimedOut` | `57014` | empty |
| `ConnectionLost` | three consecutive catalogue reads that fail as `Unreachable` | the number of failures that raised it |
| `ValueRejected` | `23514` on an editor or marker write | the pen's name, or the marker's label or local moment |
| `NameTaken` | `23505` on an editor write | the group name asked for |
| `RowGone` | `23503` on an editor write, or an update, rename or delete that touched no row | the pen or group name |
| `MarkerGone` | a marker move or rename that touched no row | the marker's label, or its local moment |
| `ReadFailed` | any other SQLSTATE, or a client-side throw, on a read or a write | the SQLSTATE, or empty |

The write kinds come from `ArchiveExceptionMapper.MapWrite(exception, subject, relation)`. It maps
`23514`, `23505` and `23503` to `ValueRejected`, `NameTaken` and `RowGone` carrying `subject`, and sends
every other exception to the read classification with the `relation` its caller gives, the relations the
caller's statement touches. The pen editor passes `PenCatalogRelations`: every editor write touches one of
those three relations, and one provisioning run creates all three. The marker store passes
`MarkersRelation`, so a marker write against a missing table names `semiplot_markers` and no pen table.
`RowGone(subject)` is the form for an editor update that touched no row, and `MarkerGone(subject)` the
form for a marker move or rename that touched none; a marker's subject is in The marker store, above.
The subject is the pen's committed name for a pen
change, including a `Name` change; for a rename it is the new name when the server refuses it and the
stored name when the group is gone; a delete names the group; a membership names the pen when the
server reports the pen's key `semiplot_pen_groups_pen_id_fkey` violated, and the group otherwise. No word of either language lives in `Detail`: the mapper wraps the
subject in each kind's resourced detail. A `ReadFailed` with no detail is a fault in this code:
`ArchiveFailureLog.LogIfUnexpected`, which the provider, the editor, the row reader and the marker store
all call, logs it with the exception.

`AccessDenied`, `Unreachable`, `QueryTimedOut`, `TableMissing` and `ReadFailed` serve reads and writes
alike, so their operator text and their `ArchiveError.Describe` lines name a statement, never a read
(`ui-text.md#the-pen-editors-failure-text`). `ReadFailed` stays the default arm of both mappings and
keeps its name, which the operator never reads. There is no `WriteFailed` kind: its title, detail and
remedy would repeat the `ReadFailed` ones. A missing registration function is a missing
relation to the operator, so `42883` takes the `TableMissing` remedy, "run semibase site".

`ArchiveFailureMapper` (`SemiPlot.UI/Messages/`) turns each kind into a title, a detail, a remedy
and a `MessageSeverity`, and is the one place a remedy is written. The severity is decided in the
mapper rather than at the call site, so a future error type gets one from the `MapUnknown` arm
without anyone remembering: `AccessDenied`, `TableMissing`, `DatabaseMissing` and `ShapeUnexpected`
are `Error`, because nothing recovers until someone changes a grant, a schema or a provisioning run;
`Unreachable`, `ConnectionLost`, `QueryTimedOut` and `ReadFailed` are `Warning`, because the reads
retry by themselves. `ValueRejected`, `NameTaken` and `RowGone` are `Warning` too: the operator's
own edit was refused, and the next edit is the remedy. `MarkerGone` is `Warning` as well: the chart
drops the marker, and the operator adds it again if it is still needed.
`FailureSeverityTests.EveryArchiveFault_CarriesTheSeverityTheTableNames` pins that split. `ConnectionLost` never opens the startup-failure window; it is an entry in the message panel
under a chart that works.

The live source's arm, `MapLiveSource`, splits the same way. `Unreachable` and `PensUnserved` are
`Warning`: the chart keeps drawing the archive and the source keeps trying. `EndpointRefused` and
`ProjectMissing` are `Error`: nothing recovers until the operator changes the SCADA's OPC UA server or
the `opc/` section, and their remedies name the 30 s retry as a literal. Each detail names the endpoint;
the `PensUnserved` detail lists the unknown and the non-numeric pen ids, each group named and an empty
one left out. `FailureSeverityTests.EveryLiveSourceFault_CarriesTheSeverityTheTableNames` and
`LiveSourceFaultTable_CoversEveryMemberOfTheEnum` pin it, beside the `ArchiveFault` table.

The twelve rows above are the `ArchiveError` arm alone. `Map` has eleven further arms: the startup
arguments, the log file, the configuration section, the app settings, the connection file, the live
source, the startup read timeout, the unknown instance host, the export file, an `IExceptionalError` and
the `_` fallback. Every one of them but the live source's is `Error`. The startup arguments, the log file and the startup read timeout
are reachable only at startup, where nothing recovers until the operator edits something. The app
settings, the configuration section and the connection file arms are also reachable later, from the live
theme's reload and the settings save, and the same holds there (`overview.md#the-live-theme`). The
unknown instance host comes from `New window` and `Restart` (`overview.md#another-instance`), the export
file from an export, and the last two arms from any point of a session. `MessageSeverity.Info` has two
writers in the whole tree, both in `AppStatusBarViewModel`: the archive indicator's recovery entry
`ConnectionRestored` and the OPC UA indicator's `LiveConnectionRestored`. `SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`
holds the `ArchiveFault` severity table and asserts it covers `Enum.GetValues<ArchiveFault>()`; the
app-settings severities are pinned in `ArchiveFailureMapperTests`, and `EveryEnumMapperArm_AnswersForEveryMemberOfItsEnum`
calls every other enum-switching arm with every member, so a new member that reaches the throw turns red.

### No failure stops at the log

Every failure on the data path reaches the operator through `Messages/MessagePanelViewModel`, not
only the log. That includes the failed history query in `Chart/TrendChartViewModel` (which runs its
`ApplyAxisModel` / `RequestRedraw` recovery after the report), the failed extent query in `Minimap/MinimapViewModel`, and the unobserved
`LoadExtentAsync` task the composition root starts. `ApplyRealtimeBatch` carries the
try-report-continue shape that `Chart/ChartHistoryRequestDebouncer.Deliver` applies to the history it
hands on, so one bad batch does not end the realtime subscription. `Deliver` guards that hand-off
only: the result it reports instead is reported by a handler that guards itself. The catalogue and
the marker read loops report any failure but a lost connection from the third in a row, the threshold
that also raises the connection fault (`ArchiveConnectionState.ConsecutiveFailuresBeforeFault`); the
first two log a warning (`overview.md#a-failed-read`). The minimap's band and extent reads each report
the first failure of an outage and log every repeat at Warning
(`trend-interaction.md#archive-overview-minimap`).

A failure for lack of a connection follows one rule in the two readers that repeat on their own, the
catalogue loop and the marker loop: the reader logs it, and it reaches the operator through the
catalogue read's state and the status bar's one `ConnectionLost` entry (The connection state the
catalogue read reports, above). One case stays in the log, written at every failure: a marker statement
that stalls while the catalogue answers. The marker read has no bound of its own and runs to the
connection string's 300 s backstop, so after a stall the marker loop can sit on a dead pooled connection
for up to 300 s after the bar has written the recovery entry.

The live source follows the same rule on its own indicator. A lost or absent OPC UA session is one
`Unreachable` state on `ILiveSource.States`, which the bar turns into one panel entry. The source logs each
state it publishes at `Information` and every failed discovery or connect at `Debug`, and writes no
entry of its own. A connect pass that throws is logged at `Error` and reaches the operator the same way,
as a pass that reached no server. A pen
the server cannot serve is one `PensUnserved` entry per pen set, which `TrendChartViewModel` reports from
`TrendCoordinator.LivePenFaults` through the guarded `ResultReporting.TryReportFailure`. Two cases reach
only the log. A failed subscription create or delete is logged at `Warning`; a create is tried again
10 s later and at the next `Connected`, a delete once more at the next `Connected`. The subscription's
pens draw no live value meanwhile, and the indicator still reads connected. A work item of the source's
`SerialLoop` that throws is logged at `Error`: the source has no operator channel but its state and its
pen faults, and the loop runs on, so the states and samples after it still arrive.

`Messages/ResultReporting` treats the log and the panel as two independent sinks: the panel edit runs
first and the log line from a `finally`, so whichever of the two refuses the failure, the other still
has it. The panel's `Report` answers whether it coalesced, and that answer picks the log level, so the
demotion rule lives in one place rather than being evaluated twice. A result carrying several errors
becomes one entry from `Errors[0]` and the rest reach the log as warnings, which is the overload the
chart's history failures take as well. `ReportFailure` lets a refusal reach its caller;
`TryReportFailure` is the form for a caller whose escape would end an Rx stream or a dispatcher job,
and it logs the refusal once, guarded, because the log sink itself is one of the two things that can
have thrown. Its scheduler overload carries the report to the UI thread first, for the two callers
that report from a thread of their own.

A handler that runs detached — an Rx `onNext`, an Rx `onError`, a job posted to the UI scheduler — wraps
its whole body, not its report alone, and hands the throw to `TryReportFailure`:
`TrendChartViewModel.OnHistoryQueryFailed`, `MainWindow/AppStatusBarViewModel.ApplyConnectionState` and
`ApplyLiveSourceState`, `Bridge/ArchiveReadLoop.RunAsync`, the loop of both the catalogue and the marker sync, whose guard
covers each read and its apply but not the wait between reads, and lets the cancellation of its own stop
end the loop unreported, since the marker read honours that token,
`MainWindow/PenCatalogueApplier.ApplyAsync`, the last through
`TrendChartViewModel.ReportFailure`, the minimap band's `Minimap/MinimapBandFeed.ApplyRead`, whose guard
covers the next read's scheduling too, and `TryRequestRead`, which the schedule and the pen-change
subscription call, `Minimap/MinimapViewModel.TryApplyExtent` for the extent read once more after a
blank one, `Chart/TimeWindowPanelViewModel.TryApplyDays` for the archive days read, whose failed result
and throw both go through `TrendChartViewModel.ReportFailure`, and `App.ApplyTheme`, which holds a failure
for the first window until it exists and has only the log after the failure window closes
(`overview.md#the-live-theme`). The
guard belongs to the handler rather than to whoever invokes it, because the recovery around the report
would otherwise escape the same way the report can.
`Minimap/MinimapViewModel.LoadExtentAsync` carries no guard of its own: its apply runs through
`Observable.Start` on the UI scheduler and awaits it, so a throw reaches the awaiter, and both awaiters
report it, `TrendWindow.StartExtentLoad`'s continuation and `PenCatalogueApplier.ApplyAsync`'s catch.
`TrendChartViewModel.ReportFailure`, `MainWindowViewModel.ReportFailure`,
`StartupFailureViewModel.ReportFailure`, the `LoadExtentAsync` continuation and the ReactiveUI observer
call the guarded form directly.

A history query reissued on every pan is what the panel's coalescing exists for: during an outage a
ten-second drag produces roughly twenty-five identical failures, and they become one entry with a
repeat count and a moving last-seen time. Coalescing is against the newest entry only, so a
different failure in between starts a new one, and the key is the whole mapped view rather than its
title. The list is capped at `MessagePanelViewModel.MaximumEntries` (200) with the oldest dropped:
this viewer runs for weeks. The operator's shortest way to it is either of the status bar's two
connection indicators, each a `Button` bound to the panel's own `ToggleCommand`, the same command the
View menu invokes. `overview.md` holds the panel's place in the window and the reach of the ReactiveUI
exception observer under it.

`TrendCoordinator.RealtimeBatches` needs a guard of its own. Its
projection runs inside `Select`, and Rx turns a throwing projection into `OnError`, which would end
the `Publish().RefCount()` stream for every subscriber. `TryBuildRealtimeBatch` catches instead,
skips the window and publishes the exception on `TrendCoordinator.RealtimeFailures`. A terminal
failure out of the live source takes the same channel: a `Catch` ahead of the `Publish` turns it into
one `RealtimeFailures` message and completes the batch stream, so `RealtimeBatches` never faults and
the one failure is not counted once per subscriber of it. `TrendChartViewModel` is the only reader of
that channel and of `LivePenFaults`, and it reports both through the guarded
`ResultReporting.TryReportFailure` because an escape would end the subscription that feeds it. `TrendChartView`'s own six subscriptions, the two
panels' `CloseRequests`, `IsDeltaModeEnabled`, `ScalesRevision`, `RedrawRequested` and the marker layer's
`Changed`, take `ReportFailureOf` as their `onError`: it writes a `Log.Error` line naming the pipeline,
then reports through `TrendChartViewModel.ReportFailure`, because a pipeline that ends stops repainting
the chart.

A marker write reports once, in `MarkerSync`: a failed write goes to the panel through
`ResultReporting.ReportFailure` and returns to `ChartMarkers`, which reverts the marker and
reports nothing more. `ChartMarkers` subscribes the channel's `Reads` with
`TrendChartViewModel.ReportFailure` as its `onError`. The chart view's marker handlers, the capture loss
that ends a drag, the press that adds or deletes a marker, the menu's Add and Delete and the label editor's OK and key
handlers, are `async void` with the
whole body in `try/catch`. Each `catch` reaches the panel through `TrendChartViewModel.ReportFailure`.

The pen editor reports the same way. A failed write reverts its field, puts the title
`ArchiveFailureMapper.Map` gives the error on the form's message line, and adds the full entry to the
panel through `ResultReporting.ReportFailure`; a failed refresh reports and leaves the tables as they
were. The window's code-behind handlers are `async void` with the whole body in `try/catch`, and the
`catch` reaches the panel through `PenEditorViewModel.ReportFailure`, which takes the guarded form. The
export window follows the same shape through `CsvExportViewModel.ReportFailure`, which also puts the
title on the window's message line, and its four detached handlers (the tick `onNext`, `ApplyCount`, the
progress job and the day read's apply) run through `CsvExportViewModel.Guarded`; the code-behind's
blackout sync catches into the same `ReportFailure`. The time window panel's code-behind wraps
`Opened`, `Close`, the pickers' Return commit and the blackout sync in one `Run` guard whose `catch`
reaches the panel through `TimeWindowPanelViewModel.ReportFailure`, and the day read's `onError` reports
through `TrendChartViewModel.ReportFailure`.

`57014` maps unconditionally to `QueryTimedOut`. The statements a caller cancels are a history read the
chart superseded, the export's count, seed and slice reads, the time window panel's archive days read,
which `TimeWindowPanelViewModel.Close()` cancels when the panel closes before it lands, and the export
window's archive days read, which `CsvExportViewModel.Dispose` cancels when the window closes before it
lands. Npgsql raises
`OperationCanceledException` for a cancellation it requested, which the provider and the row reader rethrow
ahead of the mapper; a `57014` that reaches the mapper is therefore the server's own timeout or an
administrator's cancel. Of `IDataProvider`, only `QueryHistoryAsync` and `QueryArchiveDaysAsync` take a
`CancellationToken`, and of `IArchiveRowReader` its three reads; no member of `IPenCatalogueEditor` takes one,
and of `IMarkerStore` only `ReadAsync` does, which the marker loop cancels when its window is disposed.

## Configuration

The `connection/` section folder under `--config-dir`, which is a required launch key with no
default (`overview.md`). `ConfigurationSection.Read` merges every `*.yaml` in that folder into one
mapping and `PostgresConnectionLoader` deserializes it. Every key but `schema` is required; an
absent required key is reported, an unknown key ignored. `schema` defaults to `public` when absent:

```yaml
host: 127.0.0.1
port: 5432
database: semiplot_dev
user: semiplot
password: "change me"
```

The set that ships is `ConfigFiles/connection/connection.yaml` in the repository, with an empty
`password` so it cannot start unedited, and `SemiPlot.Tests.Unit/DeliveredConfigurationTests` runs
this loader over it. An installed file that still carries the retired `poll_interval_ms` key loads, since
an unknown key is ignored (`PostgresConnectionLoaderTests.ALeftoverPollIntervalKeyIsIgnored`): the
installer keeps a site's own `connection.yaml` on upgrade.

`host` is an IPv4 address of exactly four decimal numbers from 0 to 255, such as `127.0.0.1`.
`NetworkAddress.IsIPv4` (`SemiPlot.Core/Configuration`) is the one rule: the loader refuses anything else
with `HostNotIPv4`, the `opc/` loader does the same, and the settings window disables its save on the same
predicate. `port` is a whole number from `NetworkAddress.LowestPort` to `HighestPort`, 1 to 65535, in both
sections. A host name,
`localhost` included, an IPv6 address, a shortened form such as `127.1`, and an octet with a leading
zero, which some resolvers read as octal, are all refused at startup rather than at the first connect.
`IPAddress.TryParse` is not the rule, because it accepts `1` and `127.1`.

The file names no time zone: the provider reads the archive in the machine's zone (Time boundary,
above). The viewer therefore runs on the SCADA machine, and `host` is normally `127.0.0.1`. A viewer
pointed at a SCADA machine in another zone shifts every reading by the difference between the two
zones, and no key overrides the zone `[DEC:machine-time-zone]`.

The file states no query bound: `statement_timeout` belongs to the `semiplot` role and SemiBase owns
it (`postgres-instance.md`). The connection
string carries `Command Timeout=300` as a client backstop. One read overrides it with a bound of its
own: the catalogue read (5 s, `PostgresDataProvider.CreateCatalogueCommand`), whose failures raise the
connection fault. Loading returns a `Result`; a malformed file is reported at startup, not at
first query. The password is stored in plain text; the mitigation is a role that cannot write the
archive.

The connection string carries `Options=-c jit=off`, so every session of the one `NpgsqlDataSource`
(extent, history, catalogue, the pen catalogue editor, the marker store and the row reader) runs without
JIT, because JIT
compilation takes 1 506 ms of the 2 054 ms the chart's Hour read at the window cap executes in under
`jit = on`, against 542 ms under `jit = off`, measured 2026-10-12 ("Read cost", under Layer ladder);
`jit` is a session setting, and no vendor object or role default changes `[DEC:read-only-consumer]`. A
psql session against the same server runs under the server default, so a coarse read timed by hand
there can take over a second longer than the viewer's.

### The OPC UA section

The `opc/` section folder under `--config-dir` names the SCADA's OPC UA server for the live edge
(Realtime, above). `ConfigurationSection.Read` merges its `*.yaml` files as it merges `connection/`'s,
and `OpcUaConnectionLoader` (`SemiPlot.DataSource.OpcUa/Configuration`) deserializes the mapping into
`OpcUaConnectionSettings(Host, Port, CertificateStoreRoot)`, whose `EndpointUrl` is
`opc.tcp://<host>:<port>`. Both keys are required; an unknown key is ignored:

```yaml
host: 127.0.0.1
port: 49875
```

Both loaders parse and check through `SemiPlot.Core/Configuration/ConnectionFile`, so `host` follows the
`connection/` rule, `NetworkAddress.IsIPv4`, and `port` its bounds, 1 to 65535. An
absent key, a bare `key:` with no value and a `host` of blanks are `MissingField`, a port out of range
`OutOfRange`, a host that is no IPv4 address `HostNotIPv4` and a port that is not a number, `port: ""`
included, `Unparseable`, each a `ConnectionFileError` of section
`Opc`, whose title reads "SemiPlot cannot read the OPC UA connection settings". The shipped set is
`ConfigFiles/opc/opc.yaml`, `127.0.0.1` and the real Simple-Scada's port 49875, and
`DeliveredConfigurationTests.TheShippedOpcSectionLoads` runs this loader over it. The installer copies
`ConfigFiles` with `onlyifdoesntexist`, so an upgraded site gains `opc/opc.yaml` and keeps its own
`connection.yaml`.

The certificate store root is `<config-dir>/opc/pki`, the folder the four directory stores of the client
configuration sit under (Realtime, The OPC UA live source). The section is read top-level only, so a
`.yaml` file under `pki` is never parsed (`OpcUaConnectionLoaderTests.AYamlFileUnderThePkiFolderIsNotRead`).
The settings window edits `host` and `port` of `opc/` under the rules it applies to every section
(`overview.md#the-settings-window`), and a saved change takes effect at the next start.

## Startup

Startup splits at the Avalonia boundary because `AfterSetup` is synchronous: a blocking read inside
it would hold Avalonia's setup. `StartupSequence.Run` (`SemiPlot.UI/Startup/StartupSequence.cs`)
therefore holds the ordered blocking steps and `Program.Main` calls it ahead of
`BuildAvaloniaApp()`, while the reads `TrendWindow.Build` starts inside `AfterSetup` are
asynchronous.

`StartupOptions.Parse(args)` runs ahead of all of it, because the logger's own path is an argument. It
returns `Result<StartupOptions>`, and on failure `Program.Main` applies the bootstrap culture, creates no
logger, opens the failure window through `App.RunFailed(null, failure, options: null)` and returns 1. On
success `LogFileTarget.Prepare` opens the file that `--log-file` names, creating its folder, and takes
the same route on failure, passing the parsed options so the window still offers Restart and Settings:
Serilog's file sink reports its own open failure only to `Serilog.Debugging.SelfLog` and then writes
nowhere, so a mistyped path would otherwise start the viewer with no log and no report. Only then is the
logger created, and `Program.Main` writes one Information line naming the configuration directory and the
logging level; `StartupProbe.Run` writes one more naming the time zone once the connection section loads.
A healthy run reaches Information nowhere else, so above that level the file `Prepare` opened stays empty
until the first failure.

`StartupSequence.Run` then takes these steps in order:

1. Set the bootstrap UI culture to Russian, so a failure naming the settings section itself can be
   read.
2. Load the `<ConfigDir>/app` section and apply its `locale`. A failure here short-circuits with null
   settings, before the connection section is touched, so a broken archive cannot mask a broken
   configuration (`ui-text.md`, `ui-theme.md`).
3. `StartupProbe.Run`: load the `<ConfigDir>/connection` section, then the `<ConfigDir>/opc` section
   (`StartupProbe.OpcDirectoryName`), and register `AddPostgresData(settings)`,
   `AddOpcUaLiveSource(opcSettings)` and `AddUi()`. An `opc/` folder that is missing, unreadable,
   unparseable or carries a bad value fails the start with the section's own entry, and no container is
   built.
4. Resolve `IDataProvider`, read the pen catalogue, then the archive extent.

Nothing on the startup path connects to the OPC UA server. The container constructs `OpcUaLiveSource`
when `TrendWindow.Build` resolves it, and the first live subscription starts its session in the
background, so an unreachable OPC UA server never fails the start: the window opens, the OPC UA
indicator reads connecting, and after the 30 s grace it reads not answering
(`StartupProbeOpcSectionTests.AValidOpcSection_PassesTheStartToTheArchiveReadAndOpensNoOpcSocket` holds a
listener on the `opc/` port and finds no connection). `StartupProbeTests.Run_WithNoOpcSection_FailsOnThatSectionBeforeAnyRead`
and `Run_WithAnOpcPortOfZero_FailsOnThatSectionBeforeAnyRead` pin the failing starts.

The container, the pens and the extent cross the boundary in a `StartupData` record, so `TrendWindow.Build` awaits nothing. `Program.Main` passes the settings, the `StartupData` or the first `IError`, and the parsed `StartupOptions` to `App.RunStarted(AppSettings?, StartupData, StartupOptions)` on
success and to `App.RunFailed(AppSettings?, IError, StartupOptions?)` on failure, the configuration
directory reaching the settings window and the keys reaching `InstanceLauncher` on both paths (the failure window offers Settings only when all three sections read and every
key the dialog edits is present; `overview.md#another-instance`): on success `App` builds one
`TrendWindow` (`overview.md#one-window-per-process`); on failure `App` maps the error through
`ArchiveFailureMapper` and opens `Startup/StartupFailureWindow`, which names what broke and what to do
and holds a message panel of its own. That window has no chart, legend or minimap and no service
provider. There is
no second data source to fall back to: synthetic data would let an operator read invented numbers as
process data. `Program.Main` returns 1 once that window closes.

- **The startup reads are bounded by the caller**, `Task.WaitAsync` at `StartupProbe.DefaultReadBound`
  (30 s). The expiring bound is `StartupReadTimedOutError`, distinct from `ArchiveFault.QueryTimedOut`,
  which means the server ended the read.
- **The read bound sits above the connect timeout** (`PostgresConnectionSettings.ConnectTimeoutSeconds`,
  15 s), so an unreachable host fails inside the connect attempt and reports `Unreachable` rather
  than a timeout. `StartupProbeTests.DefaultReadBound_StaysAboveTheConnectTimeout` pins the ordering.
- **The catalogue read fails as `Unreachable` past 5 s**, its own statement bound (Configuration,
  above), ahead of the 30 s caller bound. A server that answers the catalogue more slowly than 5 s at
  start, under a lock a provisioning run holds for one, fails the start as `Unreachable` rather than as
  `StartupReadTimedOutError`. The failure window's Restart is the remedy; one statement keeps one bound.
- **A throw on the startup path is a failed `Result`.** `StartupProbe.ReadAsync` catches, logs with
  the stack, disposes the container and returns an `ExceptionalError`, which `ArchiveFailureMapper`
  maps through its `IExceptionalError` arm.

An empty pen catalogue is a successful start: the window opens, draws nothing, and the chart area
states that the catalogue is empty and names the way in, `Edit` -> `Pens and groups` ->
`Refresh pen list` (`Chart/TrendChartViewModel.HasNoPens`, `ui-text.md#the-pen-editors-text`). It is a state of the
chart, not an entry in the message panel. Logging is configured before the probe runs; the log path
and the argument list are in `overview.md`.

No startup code calls `IPenCatalogueEditor`. The container constructs it when `TrendWindow.Build`
resolves it inside `.AfterSetup(...)`, and its constructor issues no statement. `RegisterNewPensAsync`
has one caller, `PenEditorViewModel`, so every write the viewer issues follows an operator action. No
startup step reads or writes a marker either: `TrendWindow.Build` starts `MarkerSync` and asks for its
first read, which runs asynchronously, so a database without `semiplot_markers` never blocks the start,
and every marker write follows a gesture on the chart.

## Field triage

When a chart is empty, check in this order. Each step distinguishes a different failure.

1. Is the database reachable at all? A connection failure is reported distinctly from an empty
   archive.
2. Does `trends` exist? If not, provisioning did not complete: `semibase site` creates it and
   `semiplot_tags` in one run.
3. `SELECT max(t) FROM trends WHERE id = <one known id> AND l = 0` — if the newest sample is old,
   archiving has stopped and the problem is on the SCADA side.
4. Is the pen present in `semiplot_tags`? An unmapped variable cannot be drawn.
   `Refresh pen list` in `Edit` -> `Pens and groups` registers it as a hidden pen, which every running viewer lists within
   5 s, switched off, and draws once it is switched on.
5. Does the window overlap the data? Compare against the extent. An offset of a whole number of
   hours means the SCADA stamps rows in a zone other than the machine's, and
   `[DEC:machine-time-zone]` does not hold on this installation (`sources.md`). At `information` or more
   verbose, the startup log line `Reading the archive in the time zone ...` names the zone the viewer
   applied.
6. Does `tpdefault` hold rows with `t >= current_date`? Only those mean the SCADA failed to create a
   day partition; its other rows, the coarse layers' 1899-12-30 junk rows and late rows of dropped days,
   are no fault (`scada-archive.md`). The server truncates `tpdefault` at 02:00 and at every project
   start, so run the check before 02:00. Such rows are not a cause of an empty chart, because every read
   still returns them, but partition elimination is lost for reads that cannot skip that partition.

When the chart draws history but no live edge, check the OPC UA side in this order:

1. Does the OPC UA indicator read connected? Not answering means no session to the `opc/` endpoint: is
   the SCADA project running, and do `host` and `port` in `opc/` name its server? The log's
   `OPC UA session to ...` lines carry each state and the stack's status. An `Error` entry means the
   server refuses the `None` endpoint with anonymous access, or holds no project folder.
2. Is there a `PensUnserved` entry? Its pens are unknown to the server or serve no number, and draw the
   archive only.
3. Run the viewer at `--logging-level Debug`: each applied live batch writes "Live batch applied: ...
   values, ... ms after the newest". No line means no value arrives; a large lag means the values arrive
   late, from the server or across the clocks of the two machines.

When the chart draws a straight segment across a span the archive holds, or fills slowly, run the
viewer at `--logging-level Debug` and match the segment's timestamps against the history-read entries
and the live batch lines around them: each history read logs its layer, range, outcome and duration
(`#what-one-history-query-covers`). A gap past the archive's newest row that does not close is the
ahead-break, shaded grey on the drawn pen, or a loss break waiting for a history read to cover it
(Realtime, above); a gesture that
re-reads the window closes it where the archive now holds rows.
