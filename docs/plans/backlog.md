# Backlog — deferred follow-ups

Items deliberately deferred out of the replatform (`completed/20260616-avalonia-scottplot-replatform.md`)
and the fix pass (`completed/20260617-trend-viewer-fixes.md`). Not bugs blocking the current build;
each is a scoped future task.

## Functional / integration

- **Real data provider (accuracy).** The viewer reads the PostgreSQL archive through
  `PostgresDataProvider`, one read-only Npgsql data source (a connection pool) serving the catalogue,
  the extent and the history window; a history read holds up to four pooled connections at once,
  specified in `docs/architecture/data-integration.md`. The live edge reads the SCADA's OPC UA server,
  not the archive (`data-integration.md#the-opc-ua-live-source`). What is still
  missing is a real archive: the decimation and extent paths have been exercised against the seeded
  bench and against a customer dump, not against production volumes on a running tool.

- **Two settings saves can lose one of them.** `SettingsSave.Save` reads a section at stage time and
  promotes its copy later; a save from another process that replaces the same file in between is
  overwritten (`overview.md#the-settings-window`). With the live theme the loss shows: every window
  returns to the overwriting save's theme and the instance whose save was lost gets no message. The fix
  is a lock file in `<config-dir>` opened with `FileShare.None` around stage and promote, waited on with a
  bound and reported as `Unwritable` past it; the operating system drops the lock with the handle, so a
  killed process leaves none behind. Deferred from the external review of `live-theme` (2026-10-01):
  the race needs two operators saving the same file within one save's span.

- **The fresh-tail clamp is narrower than the vendor's coarse-row lag.** `FreshTail.Start` clamps the
  tail at one period back from the tail edge (`FreshTail.cs`, `spacing * 4`). The vendor writes a
  period's coarse rows with the variable's next batch, up to 47 minutes after an hour closes for a slow
  variable (`scada-archive.md#layers`). Until those rows arrive the pen's seam lies more than one period
  back, the pen reads no tail, and the right edge of an Hour chart can stop up to about two hours short
  of the raw newest (`data-integration.md#layer-ladder`, Fresh tail). The fix widens the clamp to two
  periods plus the lag measured on a plant archive and re-checks the tail's row count at that width.

- **Minimap — further work.** The strip draws the drawn pen's min/max band under the window marker. Still
  wanted: clearer window handles, and possibly a per-pen presence row. Treat as its own task.

## Rendering / performance

- **The strip a drag exposes still lags behind the gesture.** Measured on the stand on 2026-09-07 after
  the culled `EnvelopeLine` polyline: the frame is cheap, but a pan past the prefetched band waits on the
  history read plus the 150 ms trailing throttle before the strip fills. A pan past the band reads the
  visible window first, and a read the drag issues mid-gesture keeps the columns in hand past its sides
  when it lands, so the strip the drag moved onto stays drawn while the range in hand covered it; the
  wait is that read. Read time is linear in width, and the visible-window read at the Raw ceiling takes
  124 ms with 8 visible pens and 413 ms with 50, measured 2026-10-13 on the bench
  (`data-integration.md#read-cost`). A drag that outruns the columns in hand before its read lands still
  shows an empty strip. A cache of the coarse layers
  is not the fix. The `date_bin` grid is anchored at each request's own `@from`, so a cached layer
  serves only a request on an identical grid, and coarse rows arrive up to 47 minutes after their period
  closes (`scada-archive.md#layers`), so a cached tail goes stale. Still open: a Raw margin several
  windows wide, now that the frame no longer pays for buffer width. The server cannot read a Raw window
  faster than its rows; pen groups only spread them over four connections.

- **~~The Minute read the chart issues takes about 1.5 s, and the Hour read spills its sort.~~
  Resolved.** The chart reads only the visible pens and the visible window first, the provider reads
  them in four concurrent pen groups, and the Raw and Minute ceilings shrink past 16 visible pens
  (`data-integration.md#what-one-history-query-covers`, `data-integration.md#operation-to-statement`,
  `data-integration.md#layer-ladder`).
  Measured 2026-10-13 end to end on the bench under the `semibase site` settings
  (`data-integration.md#read-cost`), the first read of every case at 8, 30 and 50 visible pens has a
  median at or under 816 ms. The 1 446 ms and 1 617 ms of 2026-10-12 timed the three-width margin read
  of 8 pens under the server defaults. Under the site's `work_mem` of 64 MB no sort spills to disk.

- **GPU render backend (engine) — consider, not now.** Smoothness is acceptable after the pixel-width
  history target + the culled `EnvelopeLine` polyline + single redraw path. The remaining ceiling is CPU
  SkiaSharp projecting/rasterising one plottable per pen for 50 pens. If higher pen counts or larger
  windows demand it, evaluate a Skia GL/Vulkan backend for `AvaPlot`, and/or a visible-pen cap.

## Maintainability

- **`ChartInteractionViewModel` extraction.** `TrendChartViewModel` is over the 300-line soft limit; the
  history request decision and the reported width already live in `Chart/ChartHistoryPlanner`, and the
  latest-wins request in `ChartHistoryRequestDebouncer`. Extract the cursor + delta + drag clusters into a
  nested interaction sub-VM (mirror the existing `Navigation` sub-VM pattern). Deferred during the fix pass
  as risky public-surface churn with no net saving; do it as a focused refactor.

- **Splitting the settings view model.** `Settings/SettingsViewModel` is 512 lines, past the 300-line
  preference. The seams that cut the size are the edit tracking (`_loaded*`, `Current`, `SpelledAsInTheFile`,
  `EditsOf`) and the validation (`FirstBrokenRule`, 49 lines, at the method cap, with the `Is*Valid` rules).
  A host-and-port child view model is a separate change for the duplicate, not for the size: the archive and
  OPC UA blocks repeat one shape (`Host`/`OpcHost` and `Port`/`OpcPort` carry the same setter, each raising
  its `Is*Valid` flag and `ValidationMessage`, over the same `NetworkAddress` rule). It removes about 25
  lines, needs `ValidationMessage` to follow a child's change, and moves the dialog's bindings and about forty
  test references. Both were deferred out of `20261021-opc-settings.md` as focused refactors.

- **~~Latent: coordinator history sequence frozen at 1.~~ Resolved (20260618-post-review-fixes, Task 4).**
  The standalone coordinator history path (`RequestHistory`/`SetLayer`/`HistoryResults`) was removed.
  `RequestInitialHistory` now awaits `QueryHistoryAsync` directly and applies through the same
  `NextHistorySequence()` counter as every gesture re-query, so both entry points draw a fresh monotonic
  stamp from one unified counter — the frozen-sequence hazard no longer exists.

## Platform audit leftovers

Small cleanups outside `completed/20260929-window-per-process.md`. Each is independent; fold one into a
nearby edit.
Paths are under `SemiPlot/SemiPlot.UI/` unless they name another project.

- **Disposed guards set inconsistently.** `ObjectDisposedException.ThrowIf` opens 11 members of
  `Chart/TrendChartViewModel.cs` (`:219` to `:390`) and 2 of `Minimap/MinimapViewModel.cs` (`:92`, `:102`).
  Some guard real work: `RequestInitialHistory` (`:219`) and `ApplyCatalogue` (`:231`) push into the
  debouncer and the live edge. Others are missing on the paths the guarded `SetAxisLimits` (`:390`) shares:
  `AutoscalePen` (`:375`), `RestoreInitialScale(int)` (`:382`) and `WidenToArchiveExtent` (`:259`) carry no
  guard. Decide the guard set per member against `csharp.md`'s rule: guard only where a late call corrupts
  state or leaks.
- **`ChartCursorReader` has one caller.** It wraps `CursorReadoutModel.ReadAt` for
  `TrendChartViewModel.MoveCursor` alone (`Chart/TrendChartViewModel.cs:63`, `:329`).
- **Hand-rolled log-level switch.** `StartupOptions.ParseLogLevel` (`StartupOptions.cs:105-117`) restates
  `Enum.TryParse<LogEventLevel>(ignoreCase: true)` plus the `info` alias. `Enum.TryParse` alone also
  accepts a numeric string (`"42"` parses to an undefined level) and a comma list, which would widen the
  closed list in `LoggingLevelValues` and `readme.md`; a replacement checks `Enum.IsDefined` and rejects
  numeric input.
- **`ThrowIfNull` on repository-internal APIs.** `SemiPlot.Core/Trends/PenHistoryEnvelope.cs:12-15`,
  `ArchiveTimeConverter.cs:13` in `SemiPlot.DataSource.Postgres`, and the two
  `PostgresDataProvider` entry points at `:80` and `:127`.
- **Two copies of the empty-reading format.** `TrendLegendRowViewModel.FormatReading`
  (`Legend/TrendLegendRowViewModel.cs:65-68`) and `ChartHoverReadout.FormatValue` (`Chart/ChartHoverReadout.cs:43-48`)
  both map no value to `Resources.NoValuePlaceholder` around `PenValueFormat.Format`.
- **A property read only by tests.** No production code reads `TrendChartViewModel.ScaleSettings`
  (`Chart/TrendChartViewModel.cs:112`).
- **Hand-rolled pluralisation.** `PostgresConnectionLoader.Invalid`
  (`SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:236-237`) picks "field"/"fields"
  and "is"/"are" by count.
- **`throw exception;` loses the stack trace.** `ArchiveExceptionMapper.Map`
  (`SemiPlot.DataSource.Postgres/ArchiveExceptionMapper.cs:28`) rethrows `OperationCanceledException` that way.
- **Double UI hop.** `MinimapViewModel.LoadExtentAsync` resumes its `await` on the UI thread its callers run
  on, then posts `ApplyExtent` to the UI scheduler again (`Minimap/MinimapViewModel.cs:94-95`). Under a
  `TestScheduler` no `SynchronizationContext` exists, and the `Observable.Start` at `:95` is the one route
  that brings `ApplyExtent` back, so dropping `:95` is no fix: keep one of the two marshalling routes.
- **Allocation per pointer move.** Every `MoveCursor` (`Chart/TrendChartView.axaml.cs:305`) builds an array
  in `ChartCursorReader.ReadAt` (`Chart/ChartCursorReader.cs:13-16`) and a dictionary in
  `CursorReadoutModel.ReadAt` (`SemiPlot.Core/Trends/CursorReadoutModel.cs:9`).
- **An idle chart keeps a 400 ms timer.** The debouncer's `Sample` runs on the data scheduler for the chart's
  lifetime, idle or not (`Chart/ChartHistoryRequestDebouncer.cs:55-57`, `Chart/TrendChartViewModel.cs:25`).

## Tooling

- **NU1903 advisory.** Transitive `Tmds.DBus.Protocol` 0.21.2 (pulled by Avalonia, unused on the Win32
  target) carries a high-severity NuGet advisory. Track for a transitive bump when Avalonia updates it.

- **The byte order mark survives in the other repositories.** This repository is on `charset = utf-8`
  for `[*.cs]`, but the canonical `.editorconfig` asset of the `project-layout` skill
  (`assets/editorconfig/csharp.editorconfig`, line 25) still says `charset = utf-8-bom`, and so does
  every clone that copied it: SemiStep carries the mark in 546 of its 547 `.cs` files, NtoLib in 625 of
  625. Each is its own change in its own repository, the skill asset through `marketplace-ops`.

- **~~Test unification (with the Avalonia 12 bump).~~ Resolved.** Avalonia is on 12.0.5 and all three test
  projects target xunit v3. `SemiPlot.Core.Tests` no longer exists; the suites are now split by dependency
  graph and skip policy rather than by framework, which `AGENTS.md` states.

## Test harness and the demo bench

Deferred out of the harness simplification (`completed/20260828-simplify-the-test-harness.md`). None is a
defect in the shipped tree; each is a scoped follow-up an audit named.

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

## Tooling and layout

- **xunit.v3 4.0.0 with adapter 4.0.0.** Released 2026-08-14; the repository is on 3.2.2 with
  `xunit.runner.visualstudio` 3.1.5. It brings full test parallelization regardless of collections (with
  per-collection opt-out), makes Microsoft Testing Platform v2 the default and drops MTP v1. An upgrade is
  its own PR after `20260903-harness-debloat.md`: the harness clones independent databases per class, so
  parallel gated classes against one server are fine, and the one thing to reword is the
  `ArchiveDatabaseCollection` comment about classes not racing.
