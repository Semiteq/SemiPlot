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

- **~~Test unification (with the Avalonia 12 bump).~~ Resolved.** Avalonia is on 12.0.5 and all three test
  projects target xunit v3. `SemiPlot.Core.Tests` no longer exists; the suites are now split by dependency
  graph and skip policy rather than by framework, which `CLAUDE.md` states.

## Test harness and the demo bench

Deferred out of the harness simplification (`completed/20260828-simplify-the-test-harness.md`). None is a
defect in the shipped tree; each is a scoped follow-up an audit named.

- **The stand and the fixture build the bench image differently.** `scripts/bench-demo.ps1` builds
  `semiplot-bench:manual` with no build arguments, while `PostgresContainerFixture` builds its own tag with
  `BASE_IMAGE` and a resolved `PROVISIONER_IMAGE` digest. The stand can therefore run a different provisioner
  than the tests do, and nothing says so. Either give the script the same two arguments or state in
  `docs/architecture/bench.md` that the stand deliberately tracks the floating tag.

- **The freshness bound lives in two copies.** `StaleArchiveGuard.MaximumAge` (five minutes) and the script's
  `$LiveWithin` are held in sync by a comment in each file. Drift fails loud — the writer refuses and names
  the script — so this is hygiene, not a hazard. It collapses to one owner only if the convergence moves into
  C#, which was considered and rejected on cost; revisit only alongside that.

- **An unspent cut list, about 200 lines.** An over-engineering audit proposed more than the two cuts taken
  (the teardown leak audit and the break-marker validator). Still on the table: `ProvisionerResolution` with
  its staleness reason and version probe, whose whole yield is one stderr line; the outer `Result<T>` layer in
  `ProvisionerImage`, whose only caller turns a failure straight into an exception; `FollowOptions`'
  pen-count and change-rate validators, which duplicate `SeederOptions`'; and the `Func<>[]` validator array,
  which a `Bind` chain expresses without the loop. Each is small and independent — fold one into any nearby
  edit rather than making a pass of them.

- **The IDE strips a before-launch task from `.run/*.xml`.** It drops a `RunConfigurationTask` it cannot
  resolve while it reloads the file, and writes the file back without it; the fingerprint is a missing
  trailing newline, which the IDE's serializer omits and an editor does not. The trigger is an external edit
  to those files while the project is open. It happened three times during one session. CI now fails the push
  when either demo child loses the entry, which is a detector rather than a cure — after editing any
  `.run/*.xml` from outside, run `git diff .run/` once the IDE has synced.

- **`completed/20260828-simplify-the-test-harness.md` describes machinery that was later cut.** Its Task 3
  checklist and two ➕ notes state that teardown asserts surviving clones and whether the container still
  answers. That audit was removed before the branch shipped. The file is a point-in-time record and its own
  convention is that later notes supersede earlier ones, so this is noted rather than rewritten — read the
  code, not the plan, for what teardown does.
