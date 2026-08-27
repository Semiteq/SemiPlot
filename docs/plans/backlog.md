# Backlog — deferred follow-ups

Items deliberately deferred out of the replatform (`completed/20260616-avalonia-scottplot-replatform.md`)
and the fix pass (`completed/20260617-trend-viewer-fixes.md`). Not bugs blocking the current build;
each is a scoped future task.

## Functional / integration

- **Real data provider (accuracy).** The viewer reads the PostgreSQL archive through
  `PostgresDataProvider` — one read-only connection serving the catalogue, the extent, the history
  window and the live-edge poll, specified in `docs/architecture/data-integration.md`. What is still
  missing is a real archive: the decimation and extent paths have been exercised against the seeded
  bench and against a customer dump, not against production volumes on a running tool.

- **A break that opens at the live edge draws as a held line.** `Sample` carries no null channel, so
  the realtime poll emits a `q = 32` row as an ordinary sample and cannot reconstruct the gap the way
  `HistoryRowFold` does from a null value. The line holds at the last value until the next history
  read covers that span and redraws it as a break. Closing it means giving the realtime seam a way to
  carry an absence — a nullable sample, or a separate break signal — which is a change to
  `IDataProvider` and to every consumer of `RealtimeBatch`, so it is its own task.

- **Minimap — further work.** The current strip shows the window position over the extent (visible marker +
  extent labels) but no data preview. Wanted: a richer overview (e.g. a downsampled trace/heat preview of the
  archive, clearer window handles, possibly per-pen presence). Treat as its own task.

## Rendering / performance

- **GPU render backend (engine) — consider, not now.** Smoothness is acceptable after the pixel-width
  history target + cheaper FillY bands + single redraw path. The remaining ceiling is CPU SkiaSharp
  projecting/rasterising ~100 plottables (50 Scatter + 50 FillY) per frame for 50 pens. If higher pen counts
  or larger windows demand it, evaluate a Skia GL/Vulkan backend for `AvaPlot`, and/or a band-on-demand /
  visible-pen cap (render the min/max band only for the active or a bounded set of pens).

## Maintainability

- **`ChartInteractionViewModel` extraction.** `TrendChartViewModel` is ~596 lines (over the 300 soft limit).
  Extract the cursor + delta + drag clusters into a nested interaction sub-VM (mirror the existing
  `Navigation` sub-VM pattern). Deferred during the fix pass as risky public-surface churn with no net saving;
  do it as a focused refactor.
  The layer-ladder slice (`20260810-layer-ladder-spacing`) added a fourth cluster to the same file —
  canvas-width tracking (`ReportDataAreaWidth`, `_reportedColumnTarget`) plus the startup request gate
  (`_isInitialHistoryInFlight`, `_hasDeferredHistoryRequery`, `ReleaseInitialHistoryGate`) — and deferred
  its extraction as out of scope for a ladder-arithmetic change. The history-lifecycle cluster is the
  better first cut: `ChartHistoryRequestDebouncer` already owns the latest-wins half of it, so the gate,
  the sequence counters and `RequestHistory` belong beside it rather than in the view model.

- **~~Latent: coordinator history sequence frozen at 1.~~ Resolved (20260618-post-review-fixes, Task 4).**
  The standalone coordinator history path (`RequestHistory`/`SetLayer`/`HistoryResults`) was removed.
  `RequestInitialHistory` now awaits `QueryHistoryAsync` directly and applies through the same
  `NextHistorySequence()` counter as every gesture re-query, so both entry points draw a fresh monotonic
  stamp from one unified counter — the frozen-sequence hazard no longer exists.

## Tooling

- **NU1903 advisory.** Transitive `Tmds.DBus.Protocol` 0.21.2 (pulled by Avalonia, unused on the Win32
  target) carries a high-severity NuGet advisory. Track for a transitive bump when Avalonia updates it.

- **Test unification (with the Avalonia 12 bump).** See `CLAUDE.md` — unify `SemiPlot.Core.Tests` (xunit.v3)
  and `SemiPlot.Tests` (xunit v2 + Avalonia.Headless.XUnit) onto xunit.v3 once Avalonia is bumped to 12.0.x
  (where `Avalonia.Headless.XUnit` targets xunit.v3; verify `ScottPlot.Avalonia` on 12 first).
