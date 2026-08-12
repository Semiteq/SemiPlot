# UI Trend Viewer (Stub-Backed)

## Overview

Stand up the SemiPlot desktop trend-viewer UI end-to-end against a **random data stub**.
This delivers a runnable WPF + WebView2 application rendering interactive multi-axis trends
with uPlot, driven entirely by synthetic data — no SCADA, no OPC UA, no SQL.

- **Problem it solves:** lets the entire UI (chart, pens, axes, cursor, legend, layers, time
  navigation) be built, demoed, and tested before any real Simple-Scada integration exists.
- **Key benefit:** the UI depends only on `IDataProvider`; the real providers drop in later
  behind the same abstraction with zero UI changes.
- **Integration:** establishes the project skeleton (mirroring SemiStep), the host↔JS bridge,
  and the data-provider contract that the future `SimpleScadaDataProvider` will implement.

Authoritative design: `docs/architecture/overview.md`, `charting.md`, `data-integration.md`.
Conventions: `CLAUDE.md`.

## Context (from discovery)

- **Greenfield** — no code yet; only `docs/architecture/*` and `CLAUDE.md`. Git initialized.
- **Stack (locked):** .NET 10, C# 14, Windows. WPF host + Microsoft WebView2; uPlot (MIT) web
  frontend as static ES modules under `SemiPlot.UI/Web/` (no npm build). Host→JS via
  `WebView2.PostWebMessageAsJson` (batched per frame); JS→host via web messages.
- **Project organization mirrors SemiStep** (sibling repository):
  `*.Core` / `*.UI` / `*.Tests`, `Directory.Build.props` + `Directory.Packages.props` (central
  package management), DI extension methods, FluentResults, Serilog, xunit.v3 with traits.
- **Realtime abstraction:** `System.Reactive` `IObservable<>` (matches SemiStep). `Buffer(timespan)`
  gives per-frame batching of realtime samples for free.
- **Out of scope (deferred):** real OPC UA + SQL providers, auth, installer, auto-update;
  chart toolbar extras (snapshot / save / print / favorite) and the Values / Settings tabs from
  `charting.md` — this milestone ships only the core trend view plus a time-nav / layer toolbar.
- **Assertions:** SemiStep uses FluentAssertions 8.x, which is **commercial** (Xceed license) and
  conflicts with the $0 constraint. SemiPlot uses **AwesomeAssertions** (MIT, FA-compatible API)
  instead. Revisit only if the user confirms FA licensing is already cleared org-wide.

## Development Approach

- **Testing approach: Regular** (code first, then tests, within the same task).
- Complete each task fully before the next. Small, focused changes.
- **Every task with C# logic includes new/updated tests** (success + error/edge cases) as
  separate checklist items, and all tests must pass before the next task.
- **Frontend (Web/JS) tasks** have no C# unit harness; they are verified by **manual visual run**
  (called out per task and in Post-Completion). Host-side logic that supports them (message
  contract, coordinator, layer re-query) IS unit-tested on the C# side.
- Keep YAGNI: no real data sources, no abstractions beyond what the stub + UI need now.

## Testing Strategy

All tests carry the full trait triplet, e.g. `[Trait("Component","Core")] [Trait("Area","Data")]
[Trait("Category","Unit")]`, mirroring SemiStep.

- **Unit (Component=Core, Area=Data):** DTO invariants, `AggregationLayer` spacing, and
  `RandomStubDataProvider`. Determinism is asserted **only for `QueryHistoryAsync`** (pure function
  of pens/from/to/layer/seed). Realtime is asserted for *which* pens emit (subscribed only) and that
  values are finite/in-range, not for an exact wall-clock sequence.
- **Unit (Component=UI, Area=Bridge):** message-contract JSON round-trip (incl. an **unknown/malformed
  `type` is ignored, no crash**); the coordinator batching realtime via `Buffer` and emitting to an
  abstracted channel (`ITrendChannel`); inbound `request-history` / `set-layer` handling against a fake
  provider; subscription is disposed on coordinator dispose.
- **Smoke (Component=UI, Area=Di):** the DI graph resolves root services available at that point.
- Frontend rendering, cursor, legend interactions: **manual visual verification** (Post-Completion).

## Progress Tracking

- Mark completed items `[x]` immediately. New tasks prefixed ➕, blockers ⚠️.
- Keep this file in sync with actual work; update scope here if it changes.

## Solution Overview

Layered, provider-abstracted:

- **SemiPlot.Core** — pure domain + data abstraction. `IDataProvider` exposes a realtime
  `IObservable<IReadOnlyList<Sample>>` (via `Subscribe`) and `Task<Result<...>> QueryHistoryAsync`.
  `RandomStubDataProvider` is the only implementation now. No WPF/WebView dependency.
- **SemiPlot.UI** — WPF shell hosting WebView2. A `TrendCoordinator` subscribes to the provider,
  buffers realtime per frame, and pushes messages to JS via an `ITrendChannel` (WebView2-backed in
  production, fake in tests). Inbound JS messages (history request, layer change) are dispatched
  back to the provider. The `Web/` folder is the uPlot viewer (vanilla ES modules).
- **SemiPlot.Tests** — xunit.v3, traited, covering Core and UI host logic.

Key decisions:

- **Provider-first abstraction** so the stub and the future Simple-Scada provider are
  interchangeable — the UI never sees a data source.
- **`ITrendChannel` seam** over `WebView2.PostWebMessageAsJson` so the coordinator is unit-testable
  without a live WebView2.
- **Columnar message shape** (timestamps + per-pen value arrays) — uPlot's native, fastest input.

## Technical Details

- **Repo layout:** solution and projects live under `<repo>/SemiPlot/` — `SemiPlot/SemiPlot.slnx`,
  `SemiPlot/SemiPlot.Core`, etc. — mirroring SemiStep's `<repo>/SemiStep/SemiStep.slnx` nesting.
  All build/test paths in this plan are relative to the repository root.
- **SDK pin:** root `global.json` pins the SDK (`10.0.100`, `rollForward: latestFeature`), like SemiStep.
- **TFMs:** `Directory.Build.props` sets `net10.0`. `SemiPlot.UI` overrides to `net10.0-windows`
  with `<UseWPF>true</UseWPF>`. `SemiPlot.Tests` targets `net10.0-windows` (it references UI).
  `SemiPlot.Core` stays `net10.0`. Core and UI expose internals via `InternalsVisibleTo SemiPlot.Tests`.
- **DTOs (Core):** `Pen(long ProjectVarId, string Name, string Group, string Color)`;
  `Sample(long PenId, DateTime TimestampUtc, double Value)`;
  `Series(long PenId, IReadOnlyList<DateTime> Timestamps, IReadOnlyList<double> Values)`;
  `enum AggregationLayer { Raw, Minute, Hour, Day }` with a `ToSampleInterval()` helper.
  (`Quality` is intentionally omitted now — YAGNI; it returns with the real provider / gap rendering.)
- **`Subscribe` contract:** cold per call; the coordinator owns the single subscription and disposes it
  on shutdown. The returned subscription token is `IDisposable`. The future `SimpleScadaDataProvider`
  may multiplex one OPC UA subscription to many consumers behind this same shape.
- **Stub RNG:** `QueryHistoryAsync` is a pure function — each pen's series derived deterministically from
  `(seed, penId, tickIndex)` via a stateless hash-based walk, so the same inputs reproduce byte-for-byte.
  Realtime reuses the same per-`(penId, tickIndex)` function (no shared mutable `Random`, thread-safe).
- **Message contract (UI.Bridge):** host→JS `init-pens`, `realtime-batch` (columnar), `history-result`;
  JS→host `request-history`, `set-layer`. `System.Text.Json`, camelCase. **Inbound dispatch is manual**:
  read the `type` string, switch, then deserialize the concrete record (avoids STJ polymorphism quirks);
  unknown `type` is logged and ignored.
- **Batching:** `provider.Subscribe(pens).Buffer(TimeSpan.FromMilliseconds(~33))` → one
  `realtime-batch` per ~frame.

## What Goes Where

- **Implementation Steps** (`[ ]`): all code, tests, and in-repo docs.
- **Post-Completion** (no checkboxes): manual visual runs and the deferred real providers.

## Implementation Steps

### Task 1: Solution and build infrastructure

**Files:**
- Create: `global.json` (repo root)
- Create: `SemiPlot/SemiPlot.slnx`
- Create: `SemiPlot/Directory.Build.props`
- Create: `SemiPlot/Directory.Packages.props`
- Create: `SemiPlot/SemiPlot.Core/SemiPlot.Core.csproj`
- Create: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Create: `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`

- [x] Root `global.json` pins SDK `10.0.100`, `rollForward: latestFeature` (mirror SemiStep).
- [x] `Directory.Build.props`: `net10.0`, `LangVersion 14`, `ImplicitUsings/Nullable enable`,
      `ArtifactsPath=Artifacts`, `Version 0.0.0`, win-x64 self-contained on `_IsPublishing`.
- [x] `Directory.Packages.props` (central): Microsoft.Web.WebView2; Microsoft.Extensions.DependencyInjection(.Abstractions);
      Microsoft.Extensions.Logging(.Abstractions); Serilog (+ Extensions.Logging, Sinks.Console, Sinks.File);
      System.Reactive; FluentResults; xunit.v3 (+ runner.visualstudio, Microsoft.NET.Test.Sdk);
      **AwesomeAssertions** (MIT — not FluentAssertions, per the $0 constraint).
- [x] Three csproj with TFMs per Technical Details; refs: UI→Core, Tests→Core+UI; `SemiPlot.slnx` lists all three.
- [x] `InternalsVisibleTo SemiPlot.Tests` on Core and UI (mirror SemiStep).
- [x] `dotnet build SemiPlot/SemiPlot.slnx` succeeds (verification — scaffolding, no unit tests yet).

### Task 2: Core DTOs and `IDataProvider`

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Trends/Pen.cs`, `Sample.cs`, `Series.cs`, `AggregationLayer.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/IDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Tests/Core/Trends/AggregationLayerTests.cs`

- [x] Define the four DTOs (records) per Technical Details (no `Quality` field).
- [x] `AggregationLayer.ToSampleInterval()` (Raw→1s, Minute→1m, Hour→1h, Day→1d) — single source of truth.
- [x] `IDataProvider`: `IReadOnlyList<Pen> Pens { get; }`; `IObservable<IReadOnlyList<Sample>> Subscribe(IReadOnlyList<long> penIds)`
      (cold per call; consumer owns/disposes the subscription);
      `Task<Result<IReadOnlyList<Series>>> QueryHistoryAsync(IReadOnlyList<long> penIds, DateTime fromUtc, DateTime toUtc, AggregationLayer layer)`.
      Document the lifecycle (cold, disposable, single-owner) in XML or a short remark.
- [x] Tests: `ToSampleInterval` mapping for each layer (success + that values are distinct/ordered).
- [x] `dotnet test --filter "Area=Data"` passes.

### Task 3: `RandomStubDataProvider` + synthetic pen catalog

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Data/SyntheticPenCatalog.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/RandomStubDataProvider.cs`
- Create: `SemiPlot/SemiPlot.Core/Data/DataServiceCollectionExtensions.cs` (`AddData()`)
- Create: `SemiPlot/SemiPlot.Tests/Core/Data/RandomStubDataProviderTests.cs`

- [x] `SyntheticPenCatalog`: ~30–60 grouped pens — heaters×16, dampers×16, gas lines×10, pressures, powers —
      each with deterministic color, group, and value range (matching the domain in `charting.md`).
- [x] Stateless walk function `Value(seed, penId, tickIndex)` (pure hash-based, thread-safe, in-range
      per the pen's range) — the single source of synthetic values.
- [x] `RandomStubDataProvider`: `QueryHistoryAsync` emits points at `layer.ToSampleInterval()` over
      `[from,to]` using the walk function → fully reproducible; returns `Result.Ok` (and `Result.Fail` on
      `from > to`). Realtime `IObservable` ticks on an injectable `IScheduler`, emitting samples from the
      same walk function keyed by tick.
- [x] `AddData()` registers `IDataProvider` → `RandomStubDataProvider` (singleton).
- [x] Tests: **same inputs ⇒ identical history** (byte-for-byte); history timestamps honor range + layer
      spacing; realtime (TestScheduler) emits **only** for subscribed pen ids and values stay finite/in-range;
      `from > to` → `Result.Fail`; empty pen list handled.
- [x] `dotnet test --filter "Area=Data"` passes.

### Task 4: WPF shell hosting WebView2 + static Web serving + bootstrap

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Program.cs`, `App.xaml`, `App.xaml.cs`
- Create: `SemiPlot/SemiPlot.UI/MainWindow.xaml`, `MainWindow.xaml.cs`
- Create: `SemiPlot/SemiPlot.UI/UiDi.cs` (`AddUi()`), logging bootstrap (Serilog)
- Create: `SemiPlot/SemiPlot.UI/Web/index.html`, `Web/app.js` (placeholder), `Web/styles.css`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` (copy `Web/**` to output)
- Create: `SemiPlot/SemiPlot.Tests/UI/Di/CompositionRootTests.cs`

- [x] WPF app builds a DI container (Core `AddData()` + UI `AddUi()`) and Serilog logging. (`AddTrends()`
      / `TrendCoordinator` are added in Task 5 — not referenced here.)
- [x] `MainWindow` hosts `WebView2`; serve `Web/` via `SetVirtualHostNameToFolderMapping` and navigate to it.
- [x] `Web/index.html` + `app.js` placeholder logs a bridge handshake to confirm host↔JS works.
- [x] Test: composition root resolves only what exists now — `IDataProvider`, logging, and the main
      window VM (smoke). Coordinator resolution is asserted in Task 5.
- [x] `dotnet build` succeeds; `dotnet test --filter "Area=Di"` passes. (App visual run → Post-Completion.)

### Task 5: Host↔JS message contract + `TrendCoordinator`

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Bridge/TrendMessages.cs` (records + `type` discriminators)
- Create: `SemiPlot/SemiPlot.UI/Bridge/ITrendChannel.cs`, `WebViewTrendChannel.cs`
- Create: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs`
- Create: `SemiPlot/SemiPlot.UI/Bridge/TrendServiceCollectionExtensions.cs` (`AddTrends()`)
- Create: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendMessageContractTests.cs`, `TrendCoordinatorTests.cs`

- [x] Message DTOs: host→JS `init-pens`, `realtime-batch` (columnar), `history-result`;
      JS→host `request-history`, `set-layer`. `System.Text.Json`, camelCase, `type` discriminator.
- [x] Inbound dispatch is **manual**: read `type`, switch, deserialize the concrete record; unknown
      `type` is logged and ignored (no STJ `[JsonPolymorphic]`).
- [x] `ITrendChannel.Post(json)` seam; `WebViewTrendChannel` wraps `PostWebMessageAsJson`.
- [x] `TrendCoordinator : IDisposable`: on start send `init-pens`; `provider.Subscribe(pens).Buffer(~33ms)`
      → serialize → `ITrendChannel`; handle inbound `request-history` → `QueryHistoryAsync` → `history-result`;
      `set-layer` updates current layer and re-queries. `Dispose()` tears down the realtime subscription.
- [x] Tests: every message round-trips through JSON; **unknown/malformed inbound `type` is ignored, no crash**;
      coordinator batches realtime (TestScheduler) to one message per buffer window and posts to a fake
      `ITrendChannel`; `request-history`/`set-layer` invoke the provider and post a result; provider failure
      (`Result.Fail`) surfaces, no crash; `Dispose()` stops further realtime posts.
- [x] `dotnet test --filter "Area=Bridge"` passes.

### Task 6: uPlot viewer baseline (Web)

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Web/vendor/uPlot.iife.min.js`, `vendor/uPlot.min.css` (vendored, MIT)
- Create: `SemiPlot/SemiPlot.UI/Web/chart.js`, `Web/messages.js`
- Modify: `SemiPlot/SemiPlot.UI/Web/app.js`, `Web/index.html`

- [x] Vendor uPlot static assets (uPlot 1.6.32 from jsDelivr → `Web/vendor/uPlot.iife.min.js` + `uPlot.min.css`).
- [x] `messages.js`: listen on `window.chrome.webview` messages; dispatch `init-pens`/`history-result`/`realtime-batch`;
      helpers `postRequestHistory` / `postSetLayer` to post back to the host.
- [x] `chart.js`: build a uPlot chart with a time X axis; render history series (columnar); append realtime batches.
- [x] Baseline: multiple pens on a single shared Y axis; `app.js` requests an initial history window for a default
      pen set on `init-pens`, renders the result, then appends realtime. (Manual visual verification — see Post-Completion.)
- [x] Host wiring (deferred from earlier tasks, done here so the baseline shows data): `AddTrends()` now registers a
      `Func<ITrendChannel,TrendCoordinator>` factory; `Program` calls `AddTrends()`; `MainWindow` builds a
      `WebViewTrendChannel` over the live `CoreWebView2`, starts the coordinator on `NavigationCompleted` (so the JS
      listener is attached before `init-pens`), routes inbound web messages into `HandleInboundAsync`, and disposes the
      coordinator on window close. Build green, all 39 tests pass.

### Task 7: Pens & mini-legend (Web)

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Web/legend.js`
- Modify: `SemiPlot/SemiPlot.UI/Web/chart.js`, `Web/app.js`, `Web/styles.css`

- [x] Mini-legend: columns checkbox / color / name / current value; pens grouped (heaters/dampers/gas/…).
- [x] Per-pen visibility toggle (no chart rebuild); add/remove pens at runtime.
- [x] Current-value column updates from realtime batches. (Manual visual verification.)

### Task 8: Axes — independent multi-axis + shared scale (Web)

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Web/axes.js`
- Modify: `SemiPlot/SemiPlot.UI/Web/chart.js`

- [x] Per-pen independent Y axis with its own min/max (uPlot named scales).
- [x] Shared-scale grouping: several pens on one common axis.
- [x] Selecting the active pen surfaces its scale on the primary axis (legacy behavior). (Manual visual.)

### Task 9: Cursor, time navigation, aggregation-layer selector

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Web/cursor.js`, `Web/toolbar.js`
- Modify: `SemiPlot/SemiPlot.UI/Web/chart.js`, `Web/app.js`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/TrendCoordinator.cs` (layer-driven re-query, if needed)
- Modify: `SemiPlot/SemiPlot.Tests/UI/Bridge/TrendCoordinatorTests.cs`

- [x] Vertical cursor reading every visible pen's value at the cursor X (live legend values).
- [x] Time navigation: zoom/pan, jump to start/end, range select.
- [x] Aggregation-layer selector → JS `set-layer` → host re-queries history at the new layer.
- [x] Test (host): `set-layer` triggers a re-query at the selected layer and posts a fresh `history-result`.
- [x] `dotnet test --filter "Area=Bridge"` passes. (Cursor/nav visuals → Post-Completion.)

### Task 10: Verify acceptance criteria

- [x] Verify the **core** charting features from `docs/architecture/charting.md` are present against the
      stub: pens (add/remove + visibility), per-pen multi-axis, shared-scale grouping, cursor-reads-all-pens,
      mini-legend, aggregation layers, time navigation. Toolbar extras (snapshot/save/print/favorite) and
      Values/Settings tabs are explicitly deferred (see Overview out-of-scope) — not required here.
- [x] `dotnet build SemiPlot/SemiPlot.slnx`.
- [x] `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` (full suite green).
- [x] manual visual run (deferred - not automatable, see Post-Completion)

### Task 11: Update documentation and close plan

- [x] Update `CLAUDE.md` build/test commands if the scaffold deviated; update `docs/architecture/*` if patterns changed.
- [x] Update `docs/architecture/data-integration.md` if the `IDataProvider` shape shifted during implementation.
- [x] move plan to completed/ (deferred — kept in place for the exec review/finalize phases)

## Post-Completion

*Items requiring manual intervention or external systems — informational only.*

**Manual verification:**
- Visual run of the WPF app: chart loads, realtime updates flow, history renders, cursor reads all pens,
  legend toggles work, multi-axis vs shared scale behave, layer selector re-queries, time navigation is smooth.
- Performance sanity: large synthesized history (hundreds of thousands of points) pans/zooms without lag.

**Deferred (separate future plans):**
- `SimpleScadaDataProvider`: realtime via OPC UA client, history via SQL (`trends`/`messages`), TCP fallback
  (`docs/architecture/data-integration.md`). Live-verify the OPC UA server (UaExpert) and the archive DB schema.
- Packaging: WebView2 Fixed-Version runtime + installer/auto-update (Velopack).

## Open Risks

- **uPlot multi-axis UX** at 30–60 pens with several simultaneous independent scales — layout/readability
  needs visual tuning; only a real run confirms it.
- **Per-frame batch interval** (~33 ms) may need tuning under high pen counts to keep marshalling cheap.
- **WPF + WebView2 test reach:** WebView2-backed paths are excluded from unit tests by the `ITrendChannel`
  seam; the live navigation/handshake is only manually verified.
