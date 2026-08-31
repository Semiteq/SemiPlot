# SemiPlot Architecture

Machine-readable architecture docs (English). These describe the stable design, not the
decision process. For build/test/style conventions see the root `CLAUDE.md`; for
implementation plans see `docs/plans/`.

## Documents

- [overview.md](./overview.md) — purpose, technology stack, components, data flow, deployment.
- [charting.md](./charting.md) — the trend-viewer UI: ScottPlot 5 renderer and the required
  charting features (pens, multi-axis per-pen scaling, cursor, aggregation layers, time navigation).
- [trend-interaction.md](./trend-interaction.md) — behavior spec: time navigation, sticky scroll,
  axis management, cursors, decimation, rendering; the locked decisions log.
- [data-integration.md](./data-integration.md) — the contract between SemiPlot and the archive
  database: responsibility zones, the `IDataProvider` surface, the statement each operation issues
  and what it must keep, layer selection, the time boundary, gap mapping, error semantics, the
  startup path and field triage.
- [scada-archive.md](./scada-archive.md) — the Simple-Scada 2 archive as it exists: tables and
  columns, archive layers, quality marks and gaps, write and retention behaviour, reader hazards,
  and what remains unverified.
- [postgres-instance.md](./postgres-instance.md) — the instance as SemiPlot consumes it: the reader
  role's contract, the `semiplot_tags` columns read, the provisioning states the client must
  survive, retention and capacity. Installation, configuration, provisioning order and the role
  definitions are owned by SemiBase (`github.com/Semiteq/SemiBase`) and cross-referenced there.
- [sources.md](./sources.md) — citation convention and the registry every factual claim resolves
  against: vendor manual pages, vendor forum topics, our own measurements, our own decisions.
- [bench.md](./bench.md) — the seeded PostgreSQL bench: the seeder and the demo writer, the lattice
  they share, the layer thinning, the container fixture and its template-and-clone lifecycle, the
  application bench recipe, and the headless render and input guards.

- [testing-strategy.md](./testing-strategy.md) — what each kind of test owns: the unit /
  integration / end-to-end distinction drawn on this repository's own files, why the three test
  projects are three, the ownership map for every piece of the bench, and what pins each kind of
  dependency.

The one data file referenced from the docs above is the committed 140-row real-archive slice,
[`SemiPlot.Tests.Data/Fixtures/real-archive-rows.csv`](../../SemiPlot/SemiPlot.Tests.Data/Fixtures).
The README beside it records where it came from. No archive schema is carried in this repository:
SemiBase creates `public.trends`, as `bench.md` and `postgres-instance.md` state.

- [trend-feature-spec.md](./trend-feature-spec.md) — canonical requirements + acceptance rubric for
  the trend canvas, derived from MasterSCADA "MasterTrend". The design docs above cross-reference its
  feature IDs (TM-/AY-/PN-/CU-/DA-/RT-/MS-) instead of restating requirements; MasterSCADA-derived
  definitions take precedence wherever they conflict.

## Decision records

- [grafana-vs-build-evaluation.md](./grafana-vs-build-evaluation.md) — Grafana (stock / ECharts panel
  / custom plugin) vs non-Grafana tools vs building SemiPlot. Outcome: continue SemiPlot; Grafana
  rejected (3 MUST blockers: per-pen Y axis, per-pen Y-layer resize, T1/T2 measurement cursors). This
  captures the decision process, not stable design.
- [history-read-path-evaluation.md](./history-read-path-evaluation.md) — where the history read path
  reduces data. Outcome: read the SCADA's own archive layers; build no summary tables, aggregator
  service, scheduler or extensions of our own.

## Locked decisions (summary)

| Area            | Decision                                                                    |
| --------------- | --------------------------------------------------------------------------- |
| Platform        | .NET 10 (`net10.0`), ships on Windows, C# 14                                |
| Desktop shell   | Avalonia 12.0.5 (Win32 + Skia + HarfBuzz + FluentTheme), ReactiveUI MVVM    |
| Chart renderer  | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.59, MIT, SkiaSharp) — native control  |
| Data source     | One read-only PostgreSQL connection to the Simple-Scada archive (`trends` / `messages`) — history, extent and realtime alike. No application server, no OPC UA client, no local TCP protocol |
| Wide windows    | The SCADA's own archive layers (`l = 1/2/3`); no summary tables, aggregator service, scheduler or extensions of ours — see `history-read-path-evaluation.md` |
| Retention       | One depth for all archived data; coarse layers cannot outlive raw data       |
| Tag names       | Our own `semiplot_tags` table, filled by hand — the archive stores numbers only |
| Budget          | $0 — free / OSS components only                                             |
| Visualization   | Build SemiPlot (custom); Grafana rejected — see `grafana-vs-build-evaluation.md` |
