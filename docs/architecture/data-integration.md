# Data Integration (Simple-Scada 2)

How SemiPlot reads real-time tags and historical archive data from Simple-Scada 2.
Simple-Scada exposes **no single official data API**; the design below combines two
read paths plus a fallback. All concrete schema/endpoint facts are
**documented-but-unverified** until checked against a live, configured project.

## IDataProvider abstraction

The UI depends only on this abstraction. The real Simple-Scada integration sits behind it
and is swappable with the stub.

- **Identity:** a pen/tag is identified by a Simple-Scada `ProjectVarId` (long) and a name.
- **Realtime:** subscribe to a set of tag ids → stream of samples `(tagId, timestamp, value)`.
- **History:** `query(tagIds, from, to, layer)` → one series per tag, each a columnar set of
  `(timestamp, value)`. `layer` selects archive resolution (raw / minute / hour / day).
- **Archive extent:** `QueryArchiveExtentAsync()` → `ArchiveExtent(FirstUtc, LastUtc)` — the full
  stored time span, consumed by the archive-overview minimap (charting.md / trend-interaction.md).
- **Quality:** intentionally omitted from the current abstraction (`Sample` carries no
  `quality`). It returns with the real provider, which will surface the archive `q` column for gap
  rendering.

`IDataProvider` and its DTOs (`Pen` / `Sample` / `PenHistoryEnvelope` / `ArchiveExtent`) live in
`SemiPlot.Core`. The concrete provider lives in a separate `SemiPlot.DataSource.*` project — the
current stub is `SemiPlot.DataSource.Stub` (which also owns the stub-only `MinMaxDecimator`), so Core
holds only the abstraction + DTOs and real providers slot in as sibling projects.

Implementations:

- `RandomStubDataProvider` (`SemiPlot.DataSource.Stub`) — **current**. Emits deterministic-ish random
  walks for a set of synthetic pens (realtime stream + synthesized history); `QueryArchiveExtentAsync`
  returns a synthetic depth (now − 7 days … now). Lets the whole UI be built and tested with no SCADA
  present.
- `SimpleScadaDataProvider` (future `SemiPlot.DataSource.*` sibling) — **future**. Realtime via OPC UA
  client; history via SQL; optional TCP fallback. Not implemented yet.

## Host↔viewer data contract

> **Superseded.** The original Host↔JS JSON message bridge (WebView2 `PostWebMessageAsJson` ↔
> `window.chrome.webview.postMessage`) is **removed**. The in-process Avalonia + ScottPlot viewer
> replaced it: `TrendCoordinator` exposes realtime as `IObservable<RealtimeBatch>` and history as
> `IObservable<TrendHistory>` / `QueryHistoryAsync`, with inbound `RequestHistory` / `SetLayer`
> calls — all strongly typed in-process, no JSON, no `type` discriminator (see charting.md). The
> records below describe the same logical payloads in their current typed form.

The coordinator and view models exchange these `SemiPlot.Core.Trends` records (in-process, typed):

| Record                | Direction        | Payload |
| --------------------- | ---------------- | ------- |
| `Pen` catalog         | provider → UI    | `IDataProvider.Pens`: `ProjectVarId`, name, group, color, line style — read once on start. |
| `RealtimeBatch`       | coordinator → VM | `Timestamps`: union timeline; `Pens`: `[{ PenId, Values: double?[] }]` index-aligned (`null` = gap). |
| `TrendHistory`        | coordinator → VM | `Layer` + `Pens`: per-pen `PenHistoryEnvelope` (`Timestamps` + `Min` + `Max` + `Center`; NaN = gap). |
| `RequestHistory(...)` | VM → coordinator | `penIds`, `fromUtc`, `toUtc` — re-queries at the current layer. |
| `SetLayer(layer)`     | VM → coordinator | `raw\|minute\|hour\|day` — updates the layer and re-queries the last window. |
| `ArchiveExtent`       | provider → minimap | `FirstUtc`, `LastUtc` — full stored span, via `QueryArchiveExtentAsync()`. |

The Simple-Scada integration below (OPC UA + SQL) is unaffected by this change.

## Integration options (ranked)

### A. Direct archive DB read (SQL) — primary for history
Connect a read-only SQL client to the project's configured archive engine and query the v2
archive tables. Engine is per-project: **MySQL ≥ 5.6.2, MS SQL Server (2016 SP1+), or
PostgreSQL ≥ 12**. (SQLite/Firebird drivers shipped with Simple-Scada belong to the
Stimulsoft reporting engine, not the archive store.)
- Gives: archive only. **Not realtime** — the server buffers writes in memory (up to ~2M
  records) and batch-flushes, so recent samples lag.
- Status: schema documented (RU manual), used in practice; **not sanctioned as an external API**
  (may change between versions). Connection params live in the encrypted `System.itgr` project
  file — obtain from the customer project (Editor → Settings → Database).

### B. Built-in OPC UA server — primary for realtime
Enable the UA server per project; connect as a read-only OPC UA client to
`opc.tcp://<host>:<port>`, browse the tag tree (group structure preserved), subscribe to live
values. Read-only mode and user/password auth available; the client cert must be trusted
server-side first.
- Gives: live tags only (no documented HDA/history).
- Status: **confirmed as a real feature** — `Editor.exe` (managed project configurator) contains
  `OPCUAServer` / `UAServer` tokens, and the RU manual documents `opcuaset.html`. On a fresh
  demo only a `UA-client` certificate exists under `%ProgramData%\Simple-Scada 2\Certificates\`;
  the server cert is generated when the UA server is enabled and first run. Final live
  confirmation = enable UA server in a project + connect with UaExpert.

### C. Local TCP protocol to Server.exe (127.0.0.1:8753) — fallback
`ssclib.dll` (managed) connects to `127.0.0.1:8753` with length-prefixed binary frames.
Opcodes include `cqGetData=1` (realtime) and a report/history path; history requests carry
`TimeFrom/TimeTo`, per-column `ProjectVarID (long)`, aggregation `ProcessingType (byte)`, and
`Period (int)`.
- Gives: **both** realtime and server-computed aggregated history (avg/min/max/integral/…).
- Status: unofficial, reverse-engineered, hardcoded to localhost (viewer or a small bridge must
  run on the SCADA host), version-fragile. Use only if B is unavailable or DB creds/schema cannot
  be obtained.

### Avoid
- **Web WebSocket / "REST":** the `Web/` module is a proprietary browser HMI over WebSocket
  (`/sgc/...`), payloads in Google.Protobuf, default port 8755 — no documented third-party
  contract. (`Web/pipes/` is pipeline graphic sprites, not IPC named pipes.)
- **Scripting push (TM_HTTP / file / RunSQL):** documented but architecturally a *push* and
  requires editing the customer's project — out of scope for a passive read-only viewer.
- **Native protobuf server↔client protocol:** compiled into packed native binaries, not reusable.

## Archive DB schema (archive system v2)

Default since 2.5.15.0 (PostgreSQL since 2.6.1.0). From the RU manual `tablestruct.html`,
corroborated by report column aliases. **Confirm against a live DB before relying on it.**

**`trends`** — historical tag values:

| col | meaning |
| --- | ------- |
| `id` | variable identifier (join key to tag name) |
| `t`  | timestamp of value change |
| `v`  | value |
| `q`  | quality (OPC-UA quality code; `0x00`/`0x10`/`0x20` = good; low nibble flags gap start/end) |
| `l`  | archive layer / decimation: `0`=raw, `1`=minute, `2`=hour, `3`=day |

**`messages`** — alarms / events:

| col | meaning |
| --- | ------- |
| `t`   | time |
| `gid` | group id (sentinels: `-2` boundary, `-3` auth, `-4` operator actions, `-5` client connect, `-6` project) |
| `mid` | message id |
| `k`   | type: `0`=alarm, `1`=warning, `2`=normal |
| `n`   | object name |
| `v`   | message text |
| `uid` | user id |
| `r`   | recover/clear time |
| `c`   | acknowledge time (doc rendering may show a Cyrillic `с` — verify exact column name) |

To confirm on a live project DB:

- **`id` → tag name** mapping is not in the public schema. An opt-in "create variable table"
  action produces **`variables_data`** (`ID` + name + description) — the join table, but it may
  not exist in a given project. Confirm, or find another name source.
- Column data types per engine; exact acknowledge-column name in `messages`.
- Archive **v1** (legacy) has a different per-variable table structure and is incompatible — check
  per project.

## Real-time access

1. **OPC UA server (B)** — standard UA client subscription. Live values only. NodeId/namespace
   scheme is undocumented — browse empirically.
2. **TCP `cqGetData=1` (C)** — fallback; works regardless of the OPC UA question, localhost-bound.

The archive DB (A) is **not** suitable for realtime (in-memory write queue + batch flush).

## Open items to verify on the running demo

1. Live-confirm the OPC UA server (enable in a project → `UA-server` cert appears → UaExpert
   connects to `opc.tcp://host:port`).
2. Target project's archive version (v1/v2) and engine (MySQL/MSSQL/PostgreSQL).
3. DB connection params (from the project config).
4. Presence of `variables_data` (the `id`→name table) and exact `messages` column names/types.
5. Whether the OPC UA server is HDA-capable (assume no).
