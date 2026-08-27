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

    subgraph semibase["SemiBase — Go, public"]
        prov["semibase site — a site<br/>semibase bench — the test bench"]
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

    scada -- "writes rows" --> trends
    scada -- "creates and writes" --> msgs
    prov -- "creates, never writes rows" --> trends
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
    pg["PostgresDataProvider<br/>the only implementation"]

    subgraph plumbing["SemiPlot.DataSource.Postgres"]
        direction TB
        stmts["ArchiveStatements<br/>every statement, one place"]
        ds["ArchiveDataSource<br/>connection · command bound"]
        conv["ArchiveTimeConverter<br/>naive local ⇄ UTC"]
        mapper["ArchiveExceptionMapper<br/>SQLSTATE → typed error"]
        loader["PostgresConnectionLoader<br/>archive-connection.yaml<br/>nine keys"]
    end

    errs["SemiPlot.Core/Data/Errors<br/>ten sealed types"]

    ui --> coord --> iface
    iface --> pg
    pg --> stmts
    pg --> ds
    pg --> conv
    pg --> mapper
    loader --> ds
    mapper --> errs
```

The composition root resolves `PostgresDataProvider`, and there is nothing else to resolve: an
archive that does not answer opens an error window rather than falling back to invented data, which
`data-integration.md` states under **Startup**. `StartupFailureMapper` turns each public error type
into a title, a detail and a remedy — Core's ten plus the UI-local `StartupReadTimedOutError`,
eleven in all — and a reflection coverage test fails when one has no arm; two of them reach the
operator as a banner row over a working chart rather than as a window. Every member
of `IDataProvider` is implemented — the pen catalogue, the archive extent, the windowed history read
and the live-edge poll.

## The three provisioning states, and the fourth

A client can start at any point in provisioning, and every state below is normal rather than a crash.

```mermaid
stateDiagram-v2
    direction TB
    [*] --> NoServer
    NoServer: nothing answers
    NoDatabase: server answers, database absent
    NoTables: database present, archive tables absent
    EmptyTags: semiplot_tags present but empty
    EmptyArchive: trends present, no rows
    Ready: catalogue and rows present

    NoServer --> NoDatabase: server started
    NoDatabase --> NoTables: provisioning interrupted
    NoTables --> EmptyTags: provisioning completed
    EmptyTags --> EmptyArchive: variables configured
    EmptyArchive --> Ready: archiving runs

    note right of NoTables
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

`semibase site` creates `public.trends` and `semiplot_tags` in one run, so `NoTables` is not a
stage a site passes through — it is a provisioning that stopped part-way, or a table removed after
one. Both tables are absent for the same reason and both come back from the same command, so the
client reports whichever table the failing statement names and sends the operator to `semibase site`
either way.

The split matters and is settled: a **missing** `semiplot_tags` raises `42P01` and is a typed failure
carrying the table name, while an **empty** one is a successful read of zero rows. Both stay
distinguishable, which is what SemiBase requires — provisioning skipped versus commissioning
unfinished — and no error type exists for the empty case, because the database answered correctly and
nothing is broken.

## The bench

The same provisioner runs against a container, so a broken grant fails a test rather than
commissioning day. It arrives as a layer of the bench image and runs from the entrypoint's init
hook, before the published port opens, so nothing is resolved from the machine running the suite.

```mermaid
flowchart LR
    tests["SemiPlot.Tests.Data<br/>gated integration tests"]
    fix["PostgresContainerFixture<br/>builds the bench image"]
    sb["semibase bench<br/>init hook, unix socket"]
    src[("semiplot_provisioned")]
    seed["ArchiveSeeder<br/>deterministic, golden digest"]
    tmpl[("template database")]
    clone[("clone per test class")]

    tests --> fix --> sb --> src
    src -- "cloned" --> tmpl
    seed --> tmpl
    tmpl -- "cloned" --> clone
    clone --> tests

    gate["DatabaseGate<br/>no runtime → skip with a reason<br/>SEMIPLOT_REQUIRE_DB=1 → fail"]
    tests -.-> gate

    style gate fill:none,stroke-dasharray:3 3
```

The provisioning creates `public.trends` empty; the seeder writes into it as `scada_writer`, the way
the SCADA writes its own, and adds only the day partitions its rows land in. Every read in the tests
connects as `semiplot_reader`. Nothing in this repository defines a table, a role or a grant.
