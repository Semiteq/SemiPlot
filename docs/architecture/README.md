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
- [data-integration.md](./data-integration.md) — how SemiPlot reads from Simple-Scada 2
  (OPC UA realtime + SQL archive), the archive schema, the `IDataProvider` abstraction, and
  the random stub used until real sources are wired.

## Locked decisions (summary)

| Area            | Decision                                                                    |
| --------------- | --------------------------------------------------------------------------- |
| Platform        | .NET 10 (`net10.0-windows`), Windows, C# 14                                 |
| Desktop shell   | Avalonia 11.3.8 (Win32 + Skia + FluentTheme), ReactiveUI MVVM               |
| Chart renderer  | ScottPlot 5 (`ScottPlot.Avalonia` 5.1.57, MIT, SkiaSharp) — native control  |
| Realtime source | OPC UA client to Simple-Scada's built-in UA server                          |
| History source  | Read-only SQL against the Simple-Scada archive DB (`trends` / `messages`)   |
| Fallback source | Local TCP protocol on `127.0.0.1:8753` (undocumented, reverse-engineered)   |
| Budget          | $0 — free / OSS components only                                             |
