# Retarget the UI and its test project to `net10.0`

## Overview

`SemiPlot.UI` and `SemiPlot.Tests` target `net10.0-windows`
(`SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:4`, `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj:4`),
which forbids them from building on a Linux runner. The suffix is a leftover of the WPF era: the only
Windows coupling left anywhere in the UI and Core is `.UseWin32()` at
`SemiPlot/SemiPlot.UI/App.axaml.cs:99`.

That restriction costs the roadmap its end-to-end journeys. `ubuntu-latest` is the only runner that
starts a container (`.github/workflows/ci.yml:56-57` runs the container-gated suite there, and the
comment at `:62-63` records that Windows runners cannot run Linux containers), so a journey spanning
a seeded database and a rendered chart has no host while `SemiPlot.Tests` cannot build on Linux.

This slice removes the restriction and nothing else. The application still ships on Windows, keeps
`.UseWin32()` and keeps `OutputType=WinExe`.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md — slice linux-test-target

- `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:4` — `<TargetFramework>net10.0-windows</TargetFramework>`;
  `:5` `OutputType=WinExe`; `:19` references `Avalonia.Win32`, `:20` `Avalonia.Skia`.
- `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj:4` — the same TFM; `:27` takes a project reference on
  `SemiPlot.UI`, which is why the suffix propagates.
- `SemiPlot/Directory.Build.props:5` — every other project is plain `net10.0`; `:4` sets
  `ArtifactsPath`, so obj and bin for every project live under `SemiPlot/Artifacts`.
- `SemiPlot/SemiPlot.UI/App.axaml.cs:99` — `.UseWin32()`, the sole Windows binding. A grep over
  `SemiPlot.UI` and `SemiPlot.Core` for `DllImport`, `Registry`, `Microsoft.Win32` and
  `System.Windows` returns nothing else.
- `SemiPlot/SemiPlot.Tests/TestAppBuilder.cs` — the test platform is
  `AppBuilder.Configure<App>().UseHeadless(...).UseReactiveUI(...)`. No Win32, no Skia: the headless
  platform supplies its own rendering and text shaping.
- `SemiPlot/SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests.cs` — calls
  `App.BuildAvaloniaApp()`, whose chain contains `.UseWin32()`. Its own XML doc states that
  `AppBuilder.Configure` and each `Use*` call only store a delegate, so reading the three initialisers
  back initialises no platform. This is the one test that touches the Win32 call site, and it does not
  execute it.
- `SemiPlot/Directory.Packages.props:8-15` — all Avalonia packages are 12.0.5, referenced by plain
  package name with no runtime identifier anywhere.
- `.github/workflows/ci.yml:30-31` — `build-and-test` on `windows-latest` builds `SemiPlot.slnx` and
  runs `SemiPlot.Tests`; `:56-57` — `data-tests` on `ubuntu-latest` restores, builds and runs only
  `SemiPlot.Tests.Data.csproj`.
- `.github/workflows/ci.yml:6-13` and `:15-22` — the path filter is an allow-list naming
  `.github/workflows/ci.yml`, `.editorconfig`, `SemiPlot/**`, `SemiPlot.slnx`, `global.json` and
  `sql/**`. Documentation runs no job because it is not listed, not because of the trailing
  `"!**.md"`, which matches nothing the allow-list admitted.
- `SemiPlot/.run/Debug.run.xml:18` — `PROJECT_TFM` is `net10.0-windows`. Tracked, and inside the CI
  path filter through `SemiPlot/**`.

### Measured 2026-08-21 at `c64dbb7`

These are measurements, not assumptions. Each was taken before this plan was written.

- `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release` → **370 passed, 0 skipped,
  0 failed** on Windows.
- `Avalonia.Win32` and `Avalonia.Desktop` 12.0.5 each ship `lib/net10.0` and `lib/net8.0`, and
  `Avalonia.Desktop`'s nuspec declares only those two dependency groups. A probe project on plain
  `net10.0` with `OutputType=WinExe`, referencing `Avalonia.Win32`/`Desktop`/`Skia`/`HarfBuzz` and
  calling `.UseWin32()`, builds with **zero warnings both on Windows and inside
  `mcr.microsoft.com/dotnet/sdk:10.0`**. The retarget is mechanically sound.
- `SkiaSharp.NativeAssets.Linux` is **already in the dependency graph**, and
  `SemiPlot/Artifacts/bin/SemiPlot.Tests/release/runtimes/linux-x64/native/libSkiaSharp.so` is
  already produced by the Windows build. No NuGet package is missing.
- The retargeted suite in a bare `mcr.microsoft.com/dotnet/sdk:10.0` → **102 failed, 268 passed**.
  Cause: `ldd libSkiaSharp.so` reports `libfontconfig.so.1 => not found`. The image carries no
  fontconfig, no freetype and no `/usr/share/fonts`.
- The same run after `apt-get install -y libfontconfig1` → **370 passed, 0 skipped, 0 failed**.

**The failure is 102 tests wide, not one.** `ScottPlot.Plot`'s constructor resolves a default typeface
(`Plot..ctor` → `Plottables.Benchmark..ctor` → `LabelStyle..ctor` → `Fonts.get_DefaultFontStyle` →
`SystemFontResolver.InstalledSansFont` → `SKTypeface.get_Default`), and
`SemiPlot.UI.Chart.TrendChartViewModel`'s constructor builds a `Plot`. Every test that constructs the
chart view model binds native Skia, not only the one that rasterises.

ASSUMPTION: `ubuntu-latest` provides `libfontconfig.so.1` without an install step. GitHub's Ubuntu
images ship fontconfig, so the new job most likely needs nothing — but this repository has never run
the UI suite on that runner, and Task 3 settles it on the first CI run rather than asserting it.

## Development Approach

- **Testing approach**: Regular. The change is a project-file edit; the existing suite is the test,
  and the new work is proving it runs on a second platform.
- Complete each task fully before moving to the next.
- **All tests must pass before starting the next task.**
- The suite's count is the guard: 370 passed, 0 skipped on Windows before the change, and the same
  370 after it, on both platforms.

## Testing Strategy

- **Unit and integration tests**: no test is added or removed. `SemiPlot.Tests` holds the guard and
  its content does not change in this slice.
- **The new platform is the thing under test.** A Linux run of the existing suite is the evidence,
  obtained locally in a container before any push, then confirmed on CI.
- **E2E**: none here. The journeys this slice unblocks belong to `postgres-live-edge-and-demo`.

## Acceptance Evidence

Every item below is a runnable command with the result it must produce.

**Evidence 1 — the solution still builds on Windows.**
```powershell
dotnet build SemiPlot.slnx -c Release
```
Exit 0, zero warnings introduced.

**Evidence 2 — the Windows suite is unchanged.**
```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
```
370 passed, 0 skipped, 0 failed — identical to the pre-change measurement on `c64dbb7`.

**Evidence 3 — the suite runs on Linux, measured locally before any push.**
```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 sh -c `
  "apt-get update -qq && apt-get install -y -qq --no-install-recommends libfontconfig1 && `
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release -p:ArtifactsPath=/tmp/artifacts"
```
370 passed, 0 skipped, 0 failed.

Two parts of that command are load-bearing and must not be dropped. `libfontconfig1` is what
`libSkiaSharp.so` links against, and without it 102 tests fail for a reason that does not exist on
the CI runner. `-p:ArtifactsPath=/tmp/artifacts` keeps the container's obj and bin out of the bind
mount: `Directory.Build.props:4` assigns `ArtifactsPath` unconditionally, so only a command-line
global property redirects it, and without the redirect the container rewrites
`Artifacts/obj/SemiPlot.Tests/project.assets.json` and overwrites the Windows `SemiPlot.Tests.exe`
that Evidence 2 measures.

**Evidence 4 — the application still targets Windows.**
```powershell
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -c Release
```
Exit 0, and `App.axaml.cs` still contains `.UseWin32()`, and the csproj still contains
`<OutputType>WinExe</OutputType>`.

**Evidence 5 — formatting and encoding are intact.**
```powershell
dotnet format SemiPlot.slnx --verify-no-changes
```
Exit 0. Every tracked `.cs` file still begins `ef bb bf`.

**Evidence 6 — CI runs three jobs and all pass.** On the pull request: `Build & Test`
(`windows-latest`), the new UI job (`ubuntu-latest`), and `Data Tests` (`ubuntu-latest`).

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update this plan if implementation deviates from the original scope

## Solution Overview

Two project files lose the `-windows` suffix. CI gains a third job that runs `SemiPlot.Tests` on
`ubuntu-latest`; it needs no database, no container and no `semibase`, because `SemiPlot.Tests` has no
gated test — its measured skip count is zero.

The Windows job stays exactly as it is. It is what proves the suite on the platform the application
ships on, and the roadmap keeps it for that reason rather than as redundancy.

Removing the TFM removes the reason `CLAUDE.md` gives for the two test projects existing separately.
The projects do not merge — that is settled and recorded as rejected in the roadmap — so the
justification is corrected to the one that survives: the dependency graph.

## Technical Details

**Why the suffix can go.** A `-windows` TFM enables the Windows-only surface of the BCL and marks the
assembly as platform-specific. Nothing in `SemiPlot.UI` or `SemiPlot.Core` uses that surface.
`Avalonia.Win32` is an ordinary NuGet package selecting a backend at run time, not a platform-gated
API, so referencing it from a plain `net10.0` assembly is legal; the backend simply never initialises
off Windows. The application never runs off Windows, so nothing initialises it there.

**Why the headless tests are unaffected.** `TestAppBuilder` composes `UseHeadless`, which registers
its own rendering, windowing and text-shaping subsystems. The production chain in
`App.BuildAvaloniaApp` is only ever *read* by `AppBuilderCompositionTests`, never invoked.

**Where the risk actually sits — and it is not a package.** The native assets are already shipped and
already copied. What Skia additionally needs is an operating-system library, `libfontconfig.so.1`,
present on any desktop-flavoured Linux and absent from the bare .NET SDK image. That distinction
decides the remedy: nothing is added to the dependency graph, and the only open question is whether a
given runner provides the library.

**The `Artifacts` layout does not change.** `ArtifactsPivots` appends a TFM segment only when
`TargetFrameworks` (plural) is set, so the output path stays `Artifacts/bin/SemiPlot.UI/<config>` and
`SemiPlot/.run/Debug.run.xml`'s `EXE_PATH` at `:3` stays valid. Only its `PROJECT_TFM` at `:18` goes
stale.

**The CI job's shape.** It mirrors `data-tests` minus the semibase step and minus
`SEMIPLOT_REQUIRE_DB`, restoring and building `SemiPlot.Tests.csproj` alone rather than the whole
solution, so a failure names the project.

## What Goes Where

- **Implementation Steps** — the project files, the CI workflow, the IDE run configuration, and the
  documents that state the target framework as the reason for the test-project split.
- **Post-Completion** — nothing requires manual verification; every acceptance item is a command.

## Implementation Steps

### Task 1: Retarget both projects to `net10.0`

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`

- [x] change `<TargetFramework>` from `net10.0-windows` to `net10.0` in both files, leaving
      `OutputType`, `StartupObject`, `InternalsVisibleTo` and every package reference untouched
- [x] run `dotnet build SemiPlot.slnx -c Release` and confirm exit 0 with no new warning (Evidence 1)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release` and confirm 370
      passed, 0 skipped, 0 failed (Evidence 2)
- [x] confirm `App.axaml.cs:99` still reads `.UseWin32()` and the UI csproj still reads
      `<OutputType>WinExe</OutputType>` (Evidence 4)
- [x] run tests — must pass before task 2

### Task 2: Confirm the suite on Linux locally

**Files:**
- No production file changes expected. If the measurement contradicts what is recorded above, that
  contradiction is the finding and this plan records it before anything is changed to accommodate it.

- [x] run Evidence 3 exactly as written, including `libfontconfig1` and
      `-p:ArtifactsPath=/tmp/artifacts`, and record the reported counts in this plan
- [x] confirm the count is 370 passed, 0 skipped, 0 failed — the same three numbers as Windows
- [x] confirm the host tree was not disturbed: `git status` clean apart from tracked edits, and
      `SemiPlot/Artifacts/obj/SemiPlot.Tests/project.assets.json` still carries Windows-style package
      paths rather than container ones
- [x] re-run Evidence 2 on Windows afterwards and confirm the count is still 370
- [x] add no package reference. `SkiaSharp.NativeAssets.Linux` is already in the graph and
      `libSkiaSharp.so` is already in the output; a missing OS library is not fixed by NuGet, and
      switching to the `NoDependencies` variant would change the dependency graph to solve a problem
      that does not exist on the CI runner
- [x] run tests — must pass before task 3

#### Measured 2026-08-21 at `7c2a12f`

Evidence 3 ran under bash rather than PowerShell — the same command, the backtick continuations
folded into one line, the bind mount given as the absolute path `C:/Users/admin/projects/SemiPlot`
under `MSYS_NO_PATHCONV=1`. Both load-bearing parts were kept: `libfontconfig1` and
`-p:ArtifactsPath=/tmp/artifacts`.

- Container run in `mcr.microsoft.com/dotnet/sdk:10.0` after
  `apt-get install -y --no-install-recommends libfontconfig1`:
  `Passed!  - Failed:     0, Passed:   370, Skipped:     0, Total:   370, Duration: 2 s -
  SemiPlot.Tests.dll (net10.0)`. Restore ran from the network; build output landed in
  `/tmp/artifacts/bin/...`, confirming the redirect held.
- `apt-get` pulled `libfontconfig1` plus `libfreetype6`, `fontconfig-config`, `libpng16-16t64`,
  `fonts-dejavu-core` and `fonts-dejavu-mono` as dependencies, so the fonts Skia resolves arrive with
  the library rather than needing a separate step.
- Host tree undisturbed after the container run: `git status --porcelain` empty; the md5 of
  `SemiPlot/Artifacts/obj/SemiPlot.Tests/project.assets.json` unchanged at
  `d2f26e520d03c718936321006bb4fa14`, its `packageFolders` still
  `C:\Users\admin\.nuget\packages\`; `SemiPlot/Artifacts/bin/SemiPlot.Tests/release/SemiPlot.Tests.exe`
  untouched at its pre-run timestamp.
- Evidence 2 re-run on Windows afterwards: **370 passed, 0 skipped, 0 failed**.

No package reference was added and no production file changed. The measurement matches what the
Context section records; there is no contradiction to carry forward.

### Task 3: Add the Linux CI leg

**Files:**
- Modify: `.github/workflows/ci.yml`

- [x] add a third job, `ui-tests-linux`, `runs-on: ubuntu-latest`, with `permissions: contents: read`,
      checkout with `persist-credentials: false` and `setup-dotnet` from `global.json`, matching the
      two existing jobs
- [x] restore, build and test `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj` alone, so a failure
      names the project rather than the solution
- [x] set no `SEMIPLOT_REQUIRE_DB` and add no semibase step: `SemiPlot.Tests` has no gated test, which
      its measured skip count of zero confirms
- [x] settle the `libfontconfig.so.1` question explicitly rather than silently. Run the job once
      without an install step; if the runner provides the library the job passes and a comment records
      that it relies on the image providing it, and if it does not, add
      `sudo apt-get install -y libfontconfig1` as a named step with the failure text that justified it
- [x] add a comment stating why this job exists — that `ubuntu-latest` is the only runner able to host
      a container alongside the UI, which is what the end-to-end journeys will need
- [x] add no step and change no setting in `build-and-test` or `data-tests` (a later review round
      amended the `data-tests` comment; nothing else in either job moved)
- [x] the guard here is the workflow itself: confirm the file parses (`gh workflow view` or a push to
      the branch) and that all three jobs are listed on the pull request. `dotnet test` observes
      nothing in this task

#### Measured 2026-08-21

`.github/workflows/ci.yml` gains `ui-tests-linux` between `build-and-test` and `data-tests`:
35 insertions and 2 deletions against `master`. `build-and-test` is byte-identical to what it was;
`data-tests` differs only in the wording of its `SEMIPLOT_REQUIRE_DB` comment, rewritten by a later
review round, with no step and no setting changed. `python -c "import
yaml; yaml.safe_load(...)"` parses the file and reports three jobs — `build-and-test` (Build & Test,
`windows-latest`), `ui-tests-linux` (UI Tests (Linux), `ubuntu-latest`), `data-tests` (Data Tests,
`ubuntu-latest`) — with the new job carrying the same five steps as the Windows one: checkout,
setup .NET, restore, build, test. `gh workflow view` was not used: it reads the workflow from the
default branch on the server, and this branch is unpushed.

**The `libfontconfig.so.1` question stays open by design, and the first CI run settles it.** The job
ships with no install step and a comment saying it relies on the runner image providing the library.
If the run fails, the signature is roughly 102 tests failing at once rather than one, because every
test constructing `TrendChartViewModel` builds a `ScottPlot.Plot` and that resolves a default
typeface through native Skia. The remedy is then a named step running
`sudo apt-get install -y libfontconfig1` ahead of the test step, and that one package is enough:
Task 2 measured that it pulls `libfreetype6`, `fontconfig-config`, `libpng16-16t64`,
`fonts-dejavu-core` and `fonts-dejavu-mono` with it, so no separate font package is needed. That
remedy is recorded in the workflow comment where a future reader meets the failure.

### Task 4: Correct the test-split justification and the stale TFM references

**Files:**
- Modify: `CLAUDE.md`
- Modify: `docs/architecture/testing-strategy.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/README.md`
- Modify: `SemiPlot/.run/Debug.run.xml`

- [x] rewrite `CLAUDE.md`'s test-split section: the target framework is no longer the reason the two
      projects exist. The surviving reason is the dependency graph — `SemiPlot.Tests.Data` references
      only Core, the provider and the seeder, so the data suite and its CI job build and run without
      Avalonia, ScottPlot and SkiaSharp; an xunit v3 project is one executable, so the container
      lifecycle and the Avalonia dispatcher stay in separate processes; and `SemiPlot.Tests.Data` is
      the sole assembly named by the provider's `InternalsVisibleTo`
- [x] update the `SemiPlot.Tests` row of that section's table from `net10.0-windows` to `net10.0`
- [x] update `SemiPlot/.run/Debug.run.xml:18`, whose `PROJECT_TFM` goes stale on the retarget; leave
      `EXE_PATH` at `:3` alone, because the artifacts layout does not change
- [x] update the paragraph in `docs/architecture/testing-strategy.md` that names the Windows TFM as a
      standing constraint — it is removed here, so the paragraph stating it as current must go
- [x] update both TFM statements in `docs/architecture/overview.md` — the platform row at `:19` and
      the sentence at `:94` — and the platform row in `docs/architecture/README.md`, so each says the
      application ships on Windows while the projects target plain `net10.0`
- [x] confirm nothing else states the TFM as a constraint:
      `git grep -n "net10.0-windows" -- ':!docs/plans'` returns only intended hits
- [x] this task edits Markdown and one IDE file; the guard is the grep above, not the test suite

#### Measured 2026-08-21

`git grep -n "net10.0-windows" -- ':!docs/plans'` returns **no hits at all** — every reference outside
`docs/plans` is gone, so there is no intended hit left to keep. The plan documents themselves still
carry the string as the record of what was changed, which is why the pathspec excludes them.

`dotnet build SemiPlot.slnx -c Release` after the edits: exit 0, 0 warnings, 0 errors. No Markdown
file gained a BOM; all four still begin with `23 20` (`# `).

Beyond the five files the task names, `docs/architecture/overview.md`'s version note also claimed the
target framework was "the only thing still separating" the two test projects. It states the same
removed reason as `CLAUDE.md` did, so it is corrected to point at the dependency graph and
`testing-strategy.md` rather than left to contradict them.

### Task 5: Verify acceptance criteria

- [x] run every Evidence item and record what each reported
- [x] confirm the Windows count and the Linux count are both 370 passed, 0 skipped, 0 failed
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and confirm exit 0 (Evidence 5)
- [x] confirm every tracked `.cs` file still begins `ef bb bf`
- [x] confirm the scope guard held: `.UseWin32()` present, `OutputType=WinExe` present, no end-to-end
      test added, the two test projects still two, no package reference added

#### Measured 2026-08-21 at `8dcceff`

Every item was re-run against the branch as it stands rather than read back from the earlier tasks,
because those measured before later tasks edited files.

| Evidence | Command | Reported |
| --- | --- | --- |
| 1 | `dotnet build SemiPlot.slnx -c Release` | exit 0, 0 warnings, 0 errors |
| 2 | `dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release` | 370 passed, 0 skipped, 0 failed, `SemiPlot.Tests.dll (net10.0)` |
| 3 | the container run described below | `Passed!  - Failed:     0, Passed:   370, Skipped:     0, Total:   370, Duration: 4 s - SemiPlot.Tests.dll (net10.0)` |
| 4 | `dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -c Release` | exit 0, 0 warnings; `App.axaml.cs:99` reads `.UseWin32()`; the csproj reads `<OutputType>WinExe</OutputType>` |
| 5 | `dotnet format SemiPlot.slnx --verify-no-changes` | exit 0, no output |
| 6 | CI | cannot run locally: the branch is unpushed, so no run exists yet. The first CI run settles it, including the open `libfontconfig.so.1` question |

Evidence 3 again ran under bash, the backtick continuations folded into one line, the bind mount
given as the absolute path `C:/Users/admin/projects/SemiPlot` under `MSYS_NO_PATHCONV=1`. Both
load-bearing parts were kept: `libfontconfig1` and `-p:ArtifactsPath=/tmp/artifacts`. Build output
landed in `/tmp/artifacts/bin/SemiPlot.Tests/release/`, so the redirect held.

Host tree undisturbed after the container run: `git status --porcelain` empty; the md5 of
`SemiPlot/Artifacts/obj/SemiPlot.Tests/project.assets.json` unchanged at
`d2f26e520d03c718936321006bb4fa14`, its `packageFolders` still `C:\Users\admin\.nuget\packages\`;
`SemiPlot/Artifacts/bin/SemiPlot.Tests/release/SemiPlot.Tests.exe` untouched at its pre-run timestamp
of `2026-08-21 12:10:35`.

**Encoding, checked in both directions.** All 205 tracked `.cs` files begin `ef bb bf`; none is
missing the mark. Of 36 tracked `.md` files, 35 carry no BOM and one does:
`docs/plans/completed/20260819-postgres-wire-up.md`. That file is byte-identical on `master` and is
absent from this branch's diff, so its BOM predates this slice; it is recorded here rather than
changed by a slice that has no business touching it.

**Scope guard, read off `git diff master...HEAD`.** No insertion count is recorded here. The count
includes this plan file, so every review round that writes a line into the plan invalidates it; it
went stale twice before it was dropped. What the guard asserts instead is the file list, which only a
real scope breach changes. The diff touches exactly thirteen files and nothing else: the retarget
itself is `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`, `SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj`
and `SemiPlot/.run/Debug.run.xml`; the tests are
`SemiPlot/SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests.cs` and
`SemiPlot/SemiPlot.Tests/xunit.runner.json`; CI is `.github/workflows/ci.yml`; the paper trail is
`CLAUDE.md`, `docs/architecture/README.md`, `docs/architecture/bench.md`,
`docs/architecture/overview.md`, `docs/architecture/testing-strategy.md` and
`docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md`; and this plan file. The UI
csproj edit is a single deletion, the `<TargetFramework>` line; the test csproj
deletes the same line and adds the four-line `None Update` item that copies `xunit.runner.json` to
the output directory. `App.axaml.cs` is not in the diff at all, so `.UseWin32()` stands untouched at
`:99`, and `<OutputType>WinExe</OutputType>` appears as an unchanged context line in the UI csproj
diff. `SemiPlot.slnx` is not in the diff and still lists exactly two test projects, `SemiPlot.Tests`
and `SemiPlot.Tests.Data`. `SemiPlot/Directory.Packages.props` and `SemiPlot/Directory.Build.props`
are not in the diff either, and the `PackageReference` plus `PackageVersion` counts are identical
between `master` and `HEAD`: 15 in the UI csproj, 10 in the test csproj, 29 in
`Directory.Packages.props`.

Two files under `SemiPlot.Tests` do change, and both belong to this slice rather than to the
end-to-end work. `UI/Startup/AppBuilderCompositionTests.cs` gains the
`WindowingSubsystemName == "Win32"` and `RenderingSubsystemName == "Skia"` assertions, which replace
the structural marker the `-windows` target framework used to be, and renames its one test method to
say so. `xunit.runner.json` is new and sets `failSkips: true`, which puts the "0 skipped" half of
this slice's own evidence under the runner instead of under a reader's eye. The branch adds no test
case and no test project, so no end-to-end test arrived.

### Task 6: [Final] Update documentation

**Files:**
- Modify: `docs/plans/roadmaps/20260810-postgres-data-source-roadmap.md` — only if a finding
  contradicts what the slice asserts

- [x] the roadmap slice states the open question as whether SkiaSharp's native assets arrive
      transitively. They do, and the real question was an OS library. Correct that sentence in the
      same pull request rather than leaving the two documents to disagree
- [ ] deferred to the delivery step — exec never moves the plan: move this plan to
      `docs/plans/completed/`

#### Measured 2026-08-21

The `linux-test-target` slice's fourth paragraph no longer asks whether SkiaSharp's `linux-x64`
native assets arrive transitively through `Avalonia.Skia`. It now states what Task 2 measured: the
package is already in the graph and `libSkiaSharp.so` is already in the output, the missing piece is
`libfontconfig.so.1`, its absence fails every test that constructs the chart view model rather than
one, and the only open question is whether the CI runner image carries the library — settled by
the first run of the new job, which ships with no install step and a comment naming the remedy.
No other paragraph and no other slice was touched; the edit is 8 insertions for 4 deletions.

`check-inert.sh` reports `inert`. The file carries no BOM (it still begins `23 20`, `# `), and its
longest-line count fell from 113 to 112, so no line over 100 characters was introduced.

#### Review follow-up 2026-08-21

Code review changed five things the tasks above did not plan, each recorded here so the plan does not
misreport the branch.

- Both csproj files now declare **no** `<TargetFramework>` at all rather than `net10.0`: that value
  duplicated `SemiPlot/Directory.Build.props:5`, which the other five projects already inherit.
- `SemiPlot/SemiPlot.Tests/UI/Startup/AppBuilderCompositionTests.cs` additionally asserts
  `WindowingSubsystemName == "Win32"` and `RenderingSubsystemName == "Skia"`. The `-windows` suffix was
  itself the structural marker of the shipped backend; with it gone, the scope guard "keeps
  `.UseWin32()`" had no test behind it. Reading the names initialises no platform, so the assertion
  holds on Linux too.
- `SemiPlot/SemiPlot.Tests/xunit.runner.json` sets `failSkips: true`, copied to the output directory by
  a `None Update` item. `dotnet test` exits 0 on a skip, so nothing enforced the "0 skipped" half of
  this slice's evidence on either leg. Verified by a temporary `[Fact(Skip = ...)]` probe, which
  reported `FAIL_SKIP` and failed the run; the probe was then removed.
- `docs/architecture/bench.md` lost two stale claims Task 4's file list did not name. Its *test bench*
  section no longer derives the Linux CI run from the `net10.0` target framework, and its *application
  bench* section no longer states that no CI runner can host Avalonia and a container at once — that
  was the constraint this slice removes, left standing as false prose. The section now says the
  end-to-end job does not exist yet and names `postgres-live-edge-and-demo` as its owner.
- The `ui-tests-linux` comment above the test step in `.github/workflows/ci.yml` fell from ten lines
  to two. Task 3 wrote the whole reasoning chain into the workflow — the absent `SEMIPLOT_REQUIRE_DB`,
  the failure signature, the package the remedy pulls in — where the plan already carries it. The two
  surviving lines state the dependency the job relies on and the command that fixes it, which is what
  a reader meeting a red job needs.

One more edit was reverted rather than kept: commit `6b44a6a` also rewrote `readme.md`'s Avalonia and
ScottPlot version numbers. They are stale, but the staleness has no bearing on the target framework
and the same section carries a larger stale claim (`RandomStubDataProvider` as the current data
source, against `AddPostgresData` as the composition root's default). Both belong to a slice that owns
the readme; this branch leaves `readme.md` byte-identical to `master`.

Re-run after the changes: Evidence 1 exit 0 / 0 warnings, Evidence 2 **370 passed, 0 skipped,
0 failed**, Evidence 3 in the container **370 passed, 0 skipped, 0 failed**, Evidence 5 exit 0.

The `libfontconfig.so.1` question stays open by design: the job still ships with no install step, and
the workflow comment names the remedy in two lines.

#### Review follow-up 2026-08-21, second round

A second review pass corrected claims rather than behaviour. No production code changed.

- `CLAUDE.md`'s Linux-leg paragraph overstated the gate. It claimed a `C:\` path or a Windows-only
  API added to `SemiPlot.UI` or `SemiPlot.Tests` fails the Linux leg; neither does on its own.
  `StartupOptions.DefaultConfigDir` is `C:\DISTR\Config\SemiPlot` and `StartupOptionsTests` asserts
  on it, yet Evidence 3 is green — a Windows path used as a string is only a string comparison. A
  Windows-only BCL call raises `CA1416` as a warning, and no project sets `TreatWarningsAsErrors`
  and no CI step passes `-warnaserror`, so it fails the leg only when a test executes the call. The
  paragraph now states what the leg proves — both projects compile there, every test passes under
  the headless platform — and names that real hazard. The `<TargetFramework>` inheritance sentence
  went with it: the value is already in the test table, and inheritance is uniform across all seven
  projects.
- The `failSkips` policy moved to `docs/architecture/testing-strategy.md`, beside the project split
  that decides it, with `CLAUDE.md` left a pointer. `testing-strategy.md:123` already assigns gate
  policy to the tests, and `CLAUDE.md` is the overview file.
- The `ui-tests-linux` fontconfig comment no longer names a test count. "Roughly 102 tests" measures
  today's 370-test suite in one image; the stable signature is `ldd libSkiaSharp.so` reporting
  `libfontconfig.so.1 => not found`, surfacing as a mass failure of every test that constructs
  `TrendChartViewModel`. The comment now names those instead.
- The `SEMIPLOT_REQUIRE_DB` comment in `data-tests` gave one reason for two jobs that omit the
  variable. `ui-tests-linux` is on `ubuntu-latest`, so "Windows runners cannot run Linux containers"
  does not cover it; its reason is that `SemiPlot.Tests` has no gated test. Both reasons went in
  here, were cut by the comment audit below, and are back in the shipped comment.
- `AppBuilderCompositionTests` dropped two assertions the round-one additions subsume. Verified
  against Avalonia 12.0.5: `AppBuilder.UseWindowingSubsystem(Action, string)` and
  `UseRenderingSubsystem(Action, string)` write the initializer and the name in one call, and
  `UseWin32`/`UseSkia` pass `"Win32"`/`"Skia"` — so a name assertion cannot pass with a null
  initializer. `TextShapingSubsystemInitializer.Should().NotBeNull()` stays: no name of it is
  asserted, so nothing else covers it.
- `docs/architecture/bench.md` no longer dates a sentence to this branch. "`ubuntu-latest` can now
  host both, which is what the `linux-test-target` slice unblocked" became "`ubuntu-latest` hosts
  both, and `postgres-live-edge-and-demo` owns the job", matching the forward-looking slice-naming
  pattern at `data-integration.md:430`.
- The roadmap slice's *Blast radius* lists what shipped: two project files, `.run/Debug.run.xml`,
  the workflow, the new `xunit.runner.json`, `AppBuilderCompositionTests`, and the four
  `docs/architecture` files alongside `CLAUDE.md`. `check-inert.sh` still reports `inert`, and the
  Status/Plan/PR/Branch fields were not touched.

Re-run after the changes: Evidence 1 exit 0 / 0 warnings, Evidence 2 **370 passed, 0 skipped,
0 failed**, Evidence 3 in the container **370 passed, 0 skipped, 0 failed**, Evidence 5 exit 0.

#### Review follow-up 2026-08-21, comment audit and restore

A comment audit (`8d018bc`) judged the workflow comments against the branch diff alone, without
reading what `master` already carried, and cut two clauses that were load-bearing. A following pass
restored the meaning without restoring the length.

- The `SEMIPLOT_REQUIRE_DB` comment in `data-tests` was cut back to its first sentence, which left
  both omitting jobs unexplained. It again names why each omits the variable: `build-and-test` runs
  on a Windows runner, which cannot host a Linux container, and `SemiPlot.Tests` has no gated test to
  require one. Three lines, against master's two and the audit's one.
- The comment above `ui-tests-linux` was cut to "`ubuntu-latest` is the only runner that can host a
  Linux container alongside the suite", which describes a job that starts no container. It now says
  what the job proves — the UI suite runs on Linux — and states plainly that this job starts none,
  with the container reason kept as why the leg is on this runner.
- The audit also dropped one sentence from the `AppBuilderCompositionTests` class comment, the one
  saying text shaping is read as an initializer because no name of it is asserted. That cut stands:
  the same fact is recorded in the round above, and the assertion reads plainly enough without it.

Evidence re-run after the restore: Evidence 1 exit 0, Evidence 2 **370 passed, 0 skipped, 0 failed**,
Evidence 5 exit 0. The workflow parses under `yaml.safe_load` with the same three jobs.

## Post-Completion

*Items requiring manual intervention or external systems — no checkboxes, informational only*

**Nothing requires manual verification.** Every acceptance item is a command. The local container run
de-risks the retarget before any push, but it is not authoritative: the SDK image and `ubuntu-latest`
carry different sets of operating-system libraries, which is precisely the axis this slice's one
remaining assumption sits on. CI is the authority.

**Remaining slices**

- `semibase-container-provisioning` — the bench provisions from a container instead of a binary
  resolved from `PATH`; blocked until SemiBase publishes its image.
- `harness-and-cold-path-cleanup` — roughly 1,500 lines of apparatus with no consumer.
- `archive-schema-ownership` — the archive DDL moves to SemiBase behind a flag; blocked until SemiBase
  ships the flag and the `verify` column check.
- `postgres-live-edge-and-demo` — the realtime poll, the fresh tail, the `--follow` writer and the
  stub's retirement.

**Executed by exec:**

- branch: linux-test-target

## Verify it yourself

Every acceptance item is a command, and each was re-run at the branch tip. Reproduce them in this
order.

**1. The Windows suite is unchanged.** `master` reports 370 passed, 0 skipped, 0 failed; so does the
branch. The count is the guard — a retarget that broke anything moves it.

```powershell
dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release
```

**2. The same suite runs on Linux.** This is the change's whole point, and it is measurable on a
Windows machine because the SDK image supplies the platform. `libfontconfig1` and the
`ArtifactsPath` redirect are both load-bearing: without the first, 102 tests fail on a native library
`libSkiaSharp.so` links against; without the second, the container writes into the same
`SemiPlot/Artifacts` tree the Windows build uses, and the next Windows measurement reads a tree the
check itself corrupted.

```powershell
docker run --rm -v "${PWD}:/src" -w /src mcr.microsoft.com/dotnet/sdk:10.0 sh -c `
  "apt-get update -qq && apt-get install -y -qq --no-install-recommends libfontconfig1 && `
   dotnet test SemiPlot/SemiPlot.Tests/SemiPlot.Tests.csproj -c Release -p:ArtifactsPath=/tmp/artifacts"
```

**3. The application still ships on Windows.** The retarget removed the TFM suffix, not the Windows
target. `App.axaml.cs:99` still binds Win32 and the csproj still builds a `WinExe`; the built
executable's PE subsystem stays 2 (GUI), so no console window appeared.

```powershell
dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -c Release
```

**4. The backend is now pinned by a test, not by the TFM.** Before this branch the `-windows` suffix
was the structural marker of the Windows target; with it gone, swapping `.UseWin32()` for
`.UsePlatformDetect()` would have left all 370 tests green.
`AppBuilderCompositionTests.BuildAvaloniaApp_RegistersWin32SkiaAndTextShaping` asserts
`WindowingSubsystemName == "Win32"` and `RenderingSubsystemName == "Skia"`. To see it bite, change
`.UseWin32()` to `.UsePlatformDetect()` in `App.axaml.cs:99` and re-run item 1 — the test fails.
Revert afterwards.

**5. A skipped test is now a failure.** `SemiPlot.Tests` has no gated test, so a skip there is a
mistake rather than a policy. `SemiPlot/SemiPlot.Tests/xunit.runner.json` sets `failSkips`. To see it
bite, add `[Fact(Skip = "probe")]` to any test and re-run item 1 — the run reports `FAIL_SKIP` and
exits non-zero. Remove the probe afterwards. `SemiPlot.Tests.Data` deliberately carries no such file,
so its database-gated skips still report as skips.

**What this branch cannot prove.** Whether GitHub's `ubuntu-latest` image provides
`libfontconfig.so.1` is settled by the first run of the new `ui-tests-linux` job and by nothing
before it. If it does not, the failure is unmistakable — every test constructing
`TrendChartViewModel` fails at once, not one — and the remedy sits in a comment beside the test step.
