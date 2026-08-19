# The PostgreSQL topology, at a glance

Who owns what, who writes what, and what SemiPlot is allowed to touch. The authoritative text is in
`postgres-instance.md`, `scada-archive.md` and `data-integration.md`; this page is the map, not the
contract.

## Ownership and the write direction

Three parties touch one database and only one of them writes the archive.

```mermaid
flowchart TB
    subgraph vendor["Simple-Scada 2 — the vendor"]
        scada["SCADA runtime"]
    end

    subgraph semibase["SemiBase — Go, public, pinned v0.1.0"]
        prov["semibase create / config / verify"]
    end

    subgraph db["PostgreSQL 17 — 14 is the declared floor"]
        direction TB
        trends[("trends<br/>PARTITION BY RANGE (t)<br/>PK tpk (id, l, t)")]
        msgs[("messages")]
        tags[("semiplot_tags")]
        roles["roles · grants<br/>default-privileges chain<br/>config deltas"]
    end

    subgraph semiplot["SemiPlot — C#, private"]
        prov2["PostgresDataProvider"]
    end

    scada -- "creates and writes" --> trends
    scada -- "creates and writes" --> msgs
    prov -- "creates, never writes rows" --> tags
    prov -- "creates" --> roles
    prov2 -- "SELECT only" --> trends
    prov2 -- "SELECT only" --> tags

    classDef write stroke-width:3px
    class scada,prov write
```

`messages` is created by the SCADA and is not read by any shipped query. SemiPlot creates no object
inside the database and runs nothing there: no summary table, trigger, function, scheduled job or
extension. Any write, `ALTER` or `CREATE` it issues is a defect, and the server answers `42501`.

## What the archive holds

The vendor writes every sample once at `l = 0`, then copies a thinned selection into three coarser
layers. Nothing derives them at read time — they already exist.

```mermaid
flowchart LR
    subgraph t["trends — one table, four layers in the l column"]
        direction TB
        l0["l = 0 — raw<br/>every sample as written"]
        l1["l = 1 — minute<br/>first, last, min, max per period"]
        l2["l = 2 — hour"]
        l3["l = 3 — day"]
    end

    l0 -- "thinned by the vendor" --> l1
    l1 --> l2
    l2 --> l3

    note["l=3 ⊆ l=2 ⊆ l=1 ⊆ l=0<br/>up to four points per period,<br/>so point spacing is period ÷ 4"]
    l3 -.-> note

    style note fill:none,stroke-dasharray:3 3
```

Partitions are daily ranges over `t`, named `tpYYYYmMMdDD`, with `tpdefault` catching anything that
misses one. A non-empty `tpdefault` means a partition was missing at write time — a fault signal, not
a normal state.

The primary key is `(id, l, t)` and its leading column is `id`. Every query therefore carries the
variable list, or it cannot use the key and reads every partition instead.

## How SemiPlot reads it

```mermaid
flowchart TB
    ui["Chart · minimap · toolbar"]
    coord["TrendCoordinator"]
    iface{{"IDataProvider"}}
    stub["RandomStubDataProvider<br/>--use-stub only"]
    pg["PostgresDataProvider<br/>registered by default"]

    subgraph plumbing["SemiPlot.DataSource.Postgres"]
        direction TB
        stmts["ArchiveStatements<br/>every statement, one place"]
        ds["ArchiveDataSource<br/>connection · command bound"]
        conv["ArchiveTimeConverter<br/>naive local ⇄ UTC"]
        mapper["ArchiveExceptionMapper<br/>SQLSTATE → typed error"]
        loader["PostgresConnectionLoader<br/>archive-connection.yaml<br/>nine keys"]
    end

    errs["SemiPlot.Core/Data/Errors<br/>seven sealed types"]

    ui --> coord --> iface
    iface -.-> stub
    iface --> pg
    pg --> stmts
    pg --> ds
    pg --> conv
    pg --> mapper
    loader --> ds
    mapper --> errs

    style stub stroke-dasharray:4 4
```

The composition root resolves `PostgresDataProvider`. The stub is reachable only through
`--use-stub` and is never a fallback from a failed archive: an archive that does not answer opens an
error window, which `data-integration.md` states under **Startup**. Every one of the seven public
error types maps to a state in that window, and a reflection coverage test fails when one does not.
Three of the four provider members are implemented — the pen catalogue, the archive
extent and the windowed history read. `Subscribe` returns an empty sequence until
`postgres-realtime-poll` fills it.

## The four provisioning states, and the fifth

A client can start at any point in provisioning, and every state below is normal rather than a crash.

```mermaid
stateDiagram-v2
    direction TB
    [*] --> NoServer
    NoServer: nothing answers
    NoDatabase: server answers, database absent
    NoTrends: database present, trends absent
    NoTags: trends present, semiplot_tags absent
    EmptyTags: semiplot_tags present but empty
    EmptyArchive: trends present, no rows
    Ready: catalogue and rows present

    NoServer --> NoDatabase: server started
    NoDatabase --> NoTrends: semibase create
    NoTrends --> NoTags: SCADA runs once
    NoTags --> EmptyTags: semibase create
    EmptyTags --> EmptyArchive: variables configured
    EmptyArchive --> Ready: archiving runs

    note right of NoTags
        typed failures:
        unreachable · database missing
        not initialised, carrying the table
    end note

    note right of EmptyArchive
        successes, not failures:
        empty catalogue → empty pen list
        empty archive → ArchiveExtent.Empty
    end note
```

The split matters and is settled: a **missing** `semiplot_tags` raises `42P01` and is a typed failure
carrying the table name, while an **empty** one is a successful read of zero rows. Both stay
distinguishable, which is what SemiBase requires — provisioning skipped versus commissioning
unfinished — and no error type exists for the empty case, because the database answered correctly and
nothing is broken.

## The bench

The same provisioning path runs against a container, so a broken grant fails a test rather than
commissioning day.

```mermaid
flowchart LR
    tests["SemiPlot.Tests.Data<br/>gated integration tests"]
    fix["PostgresContainerFixture"]
    sb["semibase create<br/>pinned v0.1.0 binary"]
    seed["ArchiveSeeder<br/>deterministic, golden digest"]
    tmpl[("template database")]
    clone[("clone per test class")]

    tests --> fix --> sb --> tmpl
    seed --> tmpl
    tmpl -- "cloned" --> clone
    clone --> tests

    gate["DatabaseGate<br/>no runtime → skip with a reason<br/>SEMIPLOT_REQUIRE_DB=1 → fail"]
    tests -.-> gate

    style gate fill:none,stroke-dasharray:3 3
```

The seeder writes as `scada_writer` and creates the archive tables the way the SCADA does; every read
in the tests connects as `semiplot_reader`. Nothing in this repository defines a role or a grant.
