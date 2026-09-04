# Analyzer baseline

## Overview

The repository builds with the analyzers muted: `SemiPlot/Directory.Build.props` sets no
`TreatWarningsAsErrors`, no `EnforceCodeStyleInBuild` and no `AnalysisMode`, so the style rules
`.editorconfig` declares reach the IDE and never the build, and the "0 warnings" gate every task
claims measures only compiler warnings. This plan brings the tree to the canonical baseline the
`dev:project-layout` skill ships: the canonical `.editorconfig` and `Directory.Build.props`, a
repository `nuget.config` that names one package source, and a tree that passes the baseline with
zero warnings at build and zero diagnostics under `dotnet format`, so both the build and the
pre-commit hook fail on a style or quality regression from then on.

## Context (from discovery)

Measured 2026-09-04 on `master` at 9fbd93f with SDK 10.0.400, the canonical `.editorconfig` and
`Directory.Build.props` applied, the `nuget.config` below in place and
`-p:TreatWarningsAsErrors=false` on the build: 172 warning sites over 68 files (92 file-and-rule
pairs); `dotnet format SemiPlot.slnx --verify-no-changes` exits 2 with 222 diagnostics, because
it also sees the rules whose `:silent` option suffix the canonical `category-Style.severity =
warning` line overrides.

| Rule | Sites | Seen by | What it is |
| --- | --- | --- | --- |
| IDE0300, IDE0301, IDE0305, IDE0028 | 73 | build and format | collection expressions and simplified collection initialisation |
| IDE1006 | 41 | build and format | local `const` in camelCase; the canonical naming rule scopes constants to `field, local` |
| IDE0130 | 26 | build and format | namespace `SemiPlot.Tests.Integration` under the folder `SemiPlot.Tests.Integration/Integration/` |
| IDE0048 | 24 | format only | parentheses for clarity |
| IDE0046 | 24 | format only | `if`/`return` pairs rewritten as conditional expressions |
| IDE0032 | 19 | format only | auto-property over a field with a trivial property |
| CA1305 | 10 | build and format | formatting without an `IFormatProvider`, all in UI text and the two tests that build the same text |
| IDE0004 | 5 | build and format | redundant casts in unit tests |
| IDE0042, IDE0033 | 6 | format only | tuple deconstruction and explicit tuple names |
| CA1001 | 4 | build and format | a type owning a disposable field without `IDisposable` |
| CA1822 | 4 | build and format | members that can be static (`TrendNavigationModel.cs`, `PostgresDataProvider.cs`, `RealtimeEmptyArchiveTests.cs`, `RealtimePollReadTests.cs`); the `dotnet format` fixer leaves all four untouched |
| IDE0290 | 4 | build and format | primary constructor candidates (`MinMaxDecimator.cs`, `ArchiveExceptionMapper.cs`, `FakeDataProvider.cs`, `ChartAxisBinder.cs`) |
| CA1875, CA1816, CA1806, CS1574 | 3, 1, 1, 1 | build and format | `ExplainPlanTests.cs`; `ClonedArchiveTest.cs`; `ArchiveTimeConverterTests.cs:28`; `StartupData.cs` |

With `TreatWarningsAsErrors` left on and no repository `nuget.config`, the build never reaches the
compiler: NU1507 (three package sources under central package management from the machine's
global NuGet configuration) becomes 7 restore errors first.

The fixer run measured on that tree, `dotnet format style --diagnostics IDE0300 IDE0301 IDE0305
IDE0028 IDE0004 IDE0290` plus `dotnet format analyzers --diagnostics CA1822`: clears IDE0301,
IDE0305, IDE0028, IDE0004 and IDE0290; leaves 5 IDE0300 (`PenHistoryEnvelopeTests.cs:47-50`,
`PenScaleModelTests.cs:88`) and all 4 CA1822; orphans two usings (IDE0005 in
`ArchiveStatusBannerTests.cs:1` and `MainWindowViewModelTests.cs:1`); rewrites the views'
`CompositeDisposable` initialisers from `new()` to `[]`, after which CA1001 stops firing on the two
views (2 of 4 remain). BOM and CRLF survive the fixers.

- the canonical files: `~/.claude/plugins/cache/confs-cc/dev/0.11.0/skills/project-layout/assets/editorconfig/csharp.editorconfig`
  and `assets/msbuild/Directory.Build.props` (lines 10-20 carry `TreatWarningsAsErrors`,
  `WarningsNotAsErrors` for NU1900-NU1904, `EnforceCodeStyleInBuild`, `AnalysisMode`,
  `GenerateDocumentationFile` with CS1591 muted)
- the repository's `SemiPlot/Directory.Build.props:12-28` carries three project-specific groups the
  canonical file lacks: `Version`, the Release symbol settings, and the win-x64 publish defaults
- the repository's `.editorconfig` (306 lines) differs from the canonical file by: the analyzer
  baseline block, the constants naming scope `field, local`, `csharp_style_namespace_declarations =
  file_scoped:warning` (canonical line 145), `dotnet_diagnostic.IDE1006.severity = warning`
  (canonical line 211, what makes the naming regression proof fire), and the test relaxations under
  `[**/*.Tests*/**.cs]` (CA1707, CA1861, SYSLIB1045), whose glob covers both test projects
- the repository's `.git/hooks/pre-commit` runs `dotnet format --verify-no-changes` and, on a
  difference, runs `dotnet format` over the tree; every commit on this branch therefore has to be
  format-clean under the new configuration, not only build-clean
- `SemiPlot/SemiPlot.Tests.Integration/` holds `Integration/` (26 files, every one
  `namespace SemiPlot.Tests.Integration;`) and `Journeys/` (3 files, namespace
  `SemiPlot.Tests.Integration.Journeys`); the csproj copies `..\bench\**` with a `Link`, which the
  move does not touch; the only documents naming `Tests.Integration/Integration` are
  `docs/architecture/bench.md:14` and `docs/architecture/testing-strategy.md:68,155`
- `TrendChartView.axaml.cs:30` and `MinimapView.axaml.cs:17` hold a `CompositeDisposable` filled and
  cleared in `OnDataContextChanged` (`TrendChartView.axaml.cs:81`, `MinimapView.axaml.cs:36`);
  `TrendChartView.axaml.cs:210` formats the axis bound the editor shows and line 246 parses it back
  with a provider-less `double.TryParse`; `ChartHoverReadoutTests.cs:96` and
  `MinimapViewModelTests.cs:72-73` build their expectations with the same `ToString` formats the
  production code uses
- `ArchiveTimeConverterTests.cs:28` is `Action act = () => new ArchiveTimeConverter(null!);`, a
  throwing-constructor assertion, which is the CA1806 site
- `.github/workflows/ci.yml` builds `SemiPlot.slnx` in Release on both jobs and runs no
  `dotnet format`; its `paths:` filter lists `.editorconfig`, `SemiPlot/**`, `SemiPlot.slnx` and
  `global.json`
- the development machine's `dotnet nuget list source` lists `nuget.org` and two other sources;
  the repository has no `nuget.config`; all 30 entries of
  `SemiPlot/Directory.Packages.props` are public packages, and a restore with `<clear/>` plus
  `nuget.org` resolves all seven projects including the `Aspire.AppHost.Sdk/13.5.3` project SDK

## Development Approach

- **testing approach**: Regular. The change is mechanical; the existing suites are the evidence
- one task is one commit; the branch ships as one pull request
- every commit is format-clean: the pre-commit hook reruns `dotnet format` on a difference, so
  Task 1 lands the configuration and the fixer run together, and every later task leaves
  `dotnet format SemiPlot.slnx --verify-no-changes` at exit 0
- the interim build gate for Tasks 1 to 4 is
  `dotnet build SemiPlot.slnx -p:TreatWarningsAsErrors=false` (measured: the command-line
  property overrides the one in `Directory.Build.props`), followed by
  `dotnet test <csproj> --no-build` for both suites; Task 5 removes the override and the count is
  zero
- a rule is muted only where the canonical file already mutes it, or where the task states why
  the rule fights a house convention; such a mute goes into the canonical asset as well
- **CRITICAL: all tests must pass before starting the next task**
- **CRITICAL: update this plan file when scope changes during implementation**

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit`, 705 tests, no container
- **integration tests**: `SemiPlot.Tests.Integration`, 80 tests, Docker Desktop running
- a fix that changes behaviour (`IDisposable` on the provider, the culture of a formatted string)
  gets a test where the existing suite does not already pin the behaviour

## Acceptance Evidence

Run from the repository root after Task 6, with Docker Desktop running for the integration suite:

```powershell
dotnet restore SemiPlot.slnx 2>&1 | Select-String NU1507        # prints nothing
dotnet build SemiPlot.slnx                                        # 0 warnings, 0 errors, TreatWarningsAsErrors on
dotnet format SemiPlot.slnx --verify-no-changes                   # exit 0
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --no-build                # 705 passed, 0 skipped
dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --no-build  # 80 passed, 0 skipped
Test-Path SemiPlot/SemiPlot.Tests.Integration/Integration          # False
```

The regression the baseline exists to catch, proved once and reverted:

```powershell
# add `const int badName = 1;` as a local in any method, then
dotnet build SemiPlot.slnx                                        # fails with error IDE1006
# turn a guard `if (x) return a; return b;` into a ternary in any method, then
dotnet format SemiPlot.slnx --verify-no-changes                   # exit 0: IDE0046 is muted on purpose
```

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview

- **One package source in the repository.** `nuget.config` at the root clears the inherited sources
  and names `nuget.org`. Every other source stays in the machine's own NuGet configuration and
  never in the repository.
- **Canonical files, project groups kept, two rules muted with a reason.** `.editorconfig` is the
  canonical file plus `dotnet_diagnostic.IDE0045.severity = silent` and
  `dotnet_diagnostic.IDE0046.severity = silent`: a guard clause followed by a return is the house
  shape (`csharp.md`, "Guard clauses"), and the rule rewrites it into nested conditionals, which
  the stash this plan replaces already showed at `PostgresDataProvider.cs`. `Directory.Build.props`
  is the canonical file plus the three project-specific groups it already carries.
- **Configuration and fixers land in one commit.** With the hook reformatting on commit, the
  configuration cannot land ahead of the formatting it demands. Task 1 applies both and reviews
  the fixer diff by hand before committing.
- **Folder equals namespace.** The `Integration/` folder inside `SemiPlot.Tests.Integration` is a
  leftover of the old `Tests.Data/Integration` split; its 26 files move to the project root, where
  their namespace already says they belong. `Journeys/` stays as the one subfolder.
- **One culture for UI text.** Every CA1305 site formats text an operator reads or edits, and the
  axis editor parses its own text back, so all of them and their tests name
  `CultureInfo.CurrentCulture` explicitly; no site gets the invariant culture.
- **The gate goes on last, and CI enforces both halves.** Task 5 removes the build override, adds
  `dotnet format --verify-no-changes` to the Linux job so the format-only rules are enforced where
  no hook runs, and adds `nuget.config` to the workflow's `paths:` filter.

## Technical Details

### nuget.config

```xml
<?xml version="1.0" encoding="utf-8"?>
<configuration>
  <packageSources>
    <clear />
    <add key="nuget.org" value="https://api.nuget.org/v3/index.json" />
  </packageSources>
</configuration>
```

### Directory.Build.props

The canonical `PropertyGroup` (lines 3-21 of the asset) replaces lines 3-10 of the repository file;
the `Version`, Release and publish groups at lines 12-28 follow it unchanged.

### .editorconfig

The canonical asset verbatim, plus, in the analyzer baseline block:

```
# A guard clause followed by a return is the house shape; the conditional-expression rewrite
# nests ternaries and hides the guard.
dotnet_diagnostic.IDE0045.severity = silent
dotnet_diagnostic.IDE0046.severity = silent
```

IDE0048, IDE0032, IDE0042 and IDE0033 stay at the canonical severity and their fixers run: the
parentheses, auto-properties and tuple names they produce are within what `csharp.md` says
`dotnet format` owns.

### The fixer run and what it leaves

`dotnet format SemiPlot.slnx` (style and analyzers, no `--diagnostics` filter) applies every fixer
the configuration enables. Reviewed by hand in the diff before the commit:

- a `new List<T>(capacity)` turned into a collection expression loses the capacity; a pre-sized
  buffer on a hot path (csharp.md "Hot paths") keeps its constructor and is reverted
- a `CompositeDisposable` initialiser rewritten from `new()` to `[]` in the two views is kept: the
  field is still a `CompositeDisposable`, and the view's lifecycle in `OnDataContextChanged` is
  unchanged
- the two usings the rewrites orphan (IDE0005) are removed
- the 5 IDE0300 sites the fixer leaves are rewritten by hand

### CA1001

- `PostgresDataProvider` owns `_connectionFaults` (a `Subject`); it implements `IDisposable` and
  completes the stream through the synchronized wrapper, never disposing the raw subject (a
  `Subject<T>` holds no unmanaged resource, and disposing it would make a late poll `OnNext` throw).
  `AddPostgresData` registers it as a singleton, so the container disposes the provider.
  A unit test pins that a disposed provider completes the subject.
- `FakeDataProvider` in the unit tests owns no `IDisposable` field: a test double's lifetime ends
  with the test, so CA1001 is muted for `[**/*.Tests*/**.cs]` and the fake implements only
  `IDataProvider`, nothing to dispose.
- `TrendChartView` and `MinimapView` fill and clear their `CompositeDisposable` in
  `OnDataContextChanged`; that lifecycle is right for a control Avalonia never disposes, and after
  Task 1 the analyzer no longer reports the field (the collection-expression initialiser is what
  changed). No design change and no suppression.

### CA1305

Ten sites, all UI text: `ChartDeltaCursorReader.cs`, `ChartHoverReadout.cs` (two),
`TrendChartView.axaml.cs:210` with its parse partner at line 246, `TrendLegendRowViewModel.cs`,
`MinimapViewModel.cs` (seven production sites), and the three tests `ChartHoverReadoutTests.cs:96`
and `MinimapViewModelTests.cs:73-74`. Each `ToString` and interpolation names
`CultureInfo.CurrentCulture`, `double.TryParse` at line 246 takes `NumberStyles.Float` and
`CultureInfo.CurrentCulture`, and the two tests build their expectations with the same provider so
actual and expected agree on any machine.

### CA1806

`ArchiveTimeConverterTests.cs:28` keeps its meaning: `Action act = () => _ = new
ArchiveTimeConverter(null!);`. The assertion on the throw is unchanged.

## What Goes Where

- Implementation Steps: the config files, the moves, the fixes, CI, the docs
- Post-Completion: the stash on the development machine, the global tool install caveat, the
  canonical asset update

## Implementation Steps

### Task 1: One package source, the canonical configuration and the fixer run

**Files:**
- Create: `nuget.config`
- Modify: `.editorconfig`, `SemiPlot/Directory.Build.props`, every file `dotnet format` touches
  (collection expressions in about 40 files, parentheses, auto-properties, tuple names, casts,
  primary constructors, the two orphaned usings, the 5 IDE0300 leftovers)

- [x] `nuget.config` per Technical Details; `dotnet restore SemiPlot.slnx` prints no NU1507
- [x] `.editorconfig` per Technical Details: canonical verbatim plus the IDE0045/IDE0046 mute
- [x] `SemiPlot/Directory.Build.props`: the canonical `PropertyGroup` replaces lines 3-10, the
      three project-specific groups stay
- [x] `dotnet format SemiPlot.slnx`, then the by-hand review per Technical Details: capacity
      reverts recorded here, the two IDE0005 usings removed, the 5 IDE0300 sites rewritten
      (no capacity revert was needed: the IDE0290 fixer moved `new List<T>(capacity)` into
      `new(capacity)` field initialisers in `MinMaxDecimator.EnvelopeBuilder` and kept the
      argument; the two orphaned usings and every IDE0300 site were cleared by the same run,
      so no site was left to rewrite by hand)
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` exit 0; BOM and CRLF intact on every
      touched file (`git diff --stat` shows no whole-file rewrites)
- [x] `dotnet build SemiPlot.slnx -p:TreatWarningsAsErrors=false` compiles; record the remaining
      warning count here: 21 warnings, 0 errors (9 CA1305, 4 CA1822, 3 CA1875, 2 CA1001,
      1 CS1574, 1 CA1816, 1 CA1806). Baseline before the run was 182
- [x] both test suites green with `--no-build` after that build
- [x] run tests - must pass before Task 2 (703 unit, 80 integration, 0 failed, 0 skipped)

⚠️ `dotnet format SemiPlot.slnx` unfiltered crashes with
`System.NotSupportedException: Changing document properties is not supported` while IDE0130
fires: the namespace fixer wants to move documents, which `MSBuildWorkspace` refuses, and the
whole solution change is rejected, so no other fixer applies either. The pre-commit hook runs
that same command, so Task 1 could not commit until IDE0130 was gone. Task 2's 26 `git mv`
calls and Task 3's 41 local-`const` renames (IDE1006 has no fix-all provider) therefore landed
inside Task 1's commit, which its own "`--verify-no-changes` exit 0" checkbox demands. Tasks 2
and 3 keep their remaining items: the doc references and the CS1574 cref.

### Task 2: Folder equals namespace in the integration project

**Files:**
- Move: `SemiPlot/SemiPlot.Tests.Integration/Integration/*.cs` (26 files) to
  `SemiPlot/SemiPlot.Tests.Integration/`
- Modify: `docs/architecture/bench.md:14`, `docs/architecture/testing-strategy.md:68,155`

- [x] `git mv` the 26 files; namespaces stay `SemiPlot.Tests.Integration`; `Integration/` is gone
      (done in Task 1, verified: commit `62f3f19` carries the 26 `git mv` renames, `ls
      SemiPlot/SemiPlot.Tests.Integration/` shows no `Integration/` subfolder)
- [x] `git grep -n 'Tests.Integration/Integration'` finds nothing outside `docs/plans`
      (rewrote `docs/architecture/bench.md:14` and `docs/architecture/testing-strategy.md:68,155`)
- [x] IDE0130 count under the override is zero; `dotnet format --verify-no-changes` exit 0
- [x] both test suites green
- [x] run tests - must pass before Task 3 (703 unit, 80 integration, 0 failed, 0 skipped)

### Task 3: Naming and the cref

**Files:**
- Modify: `SemiPlot/SemiPlot.AppHost/AppHost.cs`, `SemiPlot/SemiPlot.UI/Program.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/Core/Data/MinMaxDecimatorTests.cs`, `Errors/DataErrorTests.cs`,
  `Postgres/PostgresConnectionLoaderTests.cs`, `Postgres/RealtimePollTests.cs`,
  `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`

- [x] every local `const` PascalCase (IDE1006 count zero) (done in Task 1, verified: 0 IDE1006)
- [x] `StartupData.cs` cref resolves (CS1574 zero): the cref named `App.Run(StartupData)`, but
      `App.Run` takes `Result<StartupData>`; changed to
      `App.Run(FluentResults.Result{StartupData})`, the XML-doc brace syntax for a closed generic
- [x] `dotnet format --verify-no-changes` exit 0; both test suites green
- [x] run tests - must pass before Task 4 (703 unit, 80 integration, 0 failed, 0 skipped)

### Task 4: Format providers, disposables, static members and the singletons

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartDeltaCursorReader.cs`, `Chart/ChartHoverReadout.cs`,
  `Chart/TrendChartView.axaml.cs`, `Legend/TrendLegendRowViewModel.cs`, `Minimap/MinimapViewModel.cs`,
  `SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataProvider.cs`,
  `SemiPlot/SemiPlot.Core/Trends/TrendNavigationModel.cs`,
  `SemiPlot/SemiPlot.Tests.Unit/UI/Bridge/FakeDataProvider.cs`, `UI/Chart/ChartHoverReadoutTests.cs`,
  `UI/Minimap/MinimapViewModelTests.cs`, `Postgres/ArchiveTimeConverterTests.cs`,
  `SemiPlot/SemiPlot.Tests.Integration/ClonedArchiveTest.cs`, `ExplainPlanTests.cs`,
  `RealtimeEmptyArchiveTests.cs`, `RealtimePollReadTests.cs`
- Create: the `PostgresDataProvider.Dispose` case in `SemiPlot/SemiPlot.Tests.Unit/Postgres/`

- [x] CA1305 per Technical Details, ten sites, `CurrentCulture` everywhere, the parse at
      `TrendChartView.axaml.cs:246` included
- [x] CA1001 per Technical Details: `PostgresDataProvider` and `FakeDataProvider` implement
      `IDisposable`; the provider test pins the completed subject
      (`PostgresCompositionTests.DisposingTheProviderCompletesTheConnectionFaultStream`; both
      `Dispose` bodies call `OnCompleted()` before `Dispose()`, because disposing a `Subject`
      alone never completes its subscribers)
- [x] CA1822: the four members static, by hand (five in the end: `ReadWindowAsync` turning static
      uncovered `PostgresDataProvider.FillFreshTailAsync`, which then reported the same rule)
- [x] CA1875 (three sites in `ExplainPlanTests`), CA1816 (`ClonedArchiveTest.DisposeAsync` drops
      `GC.SuppressFinalize` or the class becomes what the rule expects, whichever is smaller),
      CA1806 per Technical Details (the class carries no `GC.SuppressFinalize` to drop, so
      `DisposeAsync` opens with the call the rule asks for; `Regex.Count(plan)` replaces the three
      `Regex.Matches(plan).Count` reads)
- [x] warning count under the override is zero; `dotnet format --verify-no-changes` exit 0
- [x] both test suites green
- [x] run tests - must pass before Task 5 (705 unit, 80 integration, 0 failed, 0 skipped)

### Task 5: Turn the gate on

**Files:**
- Modify: `.github/workflows/ci.yml`, `CLAUDE.md` (Build section),
  `docs/architecture/testing-strategy.md` if it describes the warning gate

- [x] `dotnet build SemiPlot.slnx` without any override: 0 warnings, 0 errors
- [x] `ci.yml`: a `dotnet format SemiPlot.slnx --verify-no-changes` step on the Linux job after
      the build; `nuget.config` added to both `paths:` lists
- [x] both regression proofs under Acceptance Evidence performed once and reverted: a local
      `const int badName = 1;` in `MinMaxDecimator.Decimate` failed `dotnet build SemiPlot.slnx`
      with `error IDE1006` (naming rule violation for `badName`); rewriting the guard in
      `TrendNavigationModel.ClampWidthSpan` (`if (width > _maximumWidth) { return _maximumWidth; }
      return width;`) as `return width > _maximumWidth ? _maximumWidth : width;` left `dotnet
      format SemiPlot.slnx --verify-no-changes` at exit 0. Both edits reverted with `git checkout --`
- [x] CI green on both jobs: PR #56, run 33880908630, both `Unit (Windows)` and
      `Unit and integration (Linux)` (with its new `format check` step) passed
- [x] `CLAUDE.md` Build section states that style and quality rules fail the build and the
      format check, and that `nuget.config` hides every other feed from a tool install run inside
      the repository
- [x] run tests - must pass before Task 6 (705 unit, 80 integration, 0 failed, 0 skipped)

### Task 6: Verify acceptance criteria

- [x] run every command under Acceptance Evidence: `dotnet restore SemiPlot.slnx 2>&1 | Select-String
      NU1507` printed nothing; `dotnet build SemiPlot.slnx` gave `Предупреждений: 0`, `Ошибок: 0`
      (`TreatWarningsAsErrors` on, no override); `dotnet format SemiPlot.slnx --verify-no-changes`
      exit 0, no output; `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
      --no-build` gave `не пройдено 0, пройдено 705, пропущено 0, всего 705`; `dotnet test
      SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --no-build` gave
      `не пройдено 0, пройдено 80, пропущено 0, всего 80`; `Test-Path
      SemiPlot/SemiPlot.Tests.Integration/Integration` (as `test -d ... && echo True || echo
      False`) printed `False`. The two regression proofs, each performed on a scratch edit and
      reverted with `git checkout --`: a `const int badName = 1;` local added in
      `MinMaxDecimator.Decimate` made `dotnet build SemiPlot.slnx` fail with `error IDE1006:
      Нарушение правила именования... badName` (plus unrelated CS0219/IDE0059 noise from the
      unused local, expected and harmless); rewriting the guard in
      `TrendNavigationModel.ClampWidthSpan` (`if (width > _maximumWidth) { return _maximumWidth; }
      return width;`) as `return width > _maximumWidth ? _maximumWidth : width;` left `dotnet
      format SemiPlot.slnx --verify-no-changes` at exit 0 with no output, confirming the IDE0046
      mute holds. Both files reverted; `git status --short` shows no leftover diff on either.
- [x] `lint-comments SemiPlot` exit 0, no output
- [x] test counts: 705 unit (703 baseline plus the two deterministic provider-disposal tests -
      `DisposingTheProviderCompletesTheConnectionFaultStream` and `DisposingTwiceDoesNotThrow` -
      that remain after the third, racy disposal test was deleted in `39657ba`), 80 integration -
      confirmed by the test runs above, both 0 failed and 0 skipped

### Task 7: Update documentation

- [x] `docs/architecture/README.md` and `overview.md` if either names the warning policy
      (neither names the policy; nothing to change)
- [x] move this plan to `docs/plans/completed/` (left in place - `ship:ship`'s `archive-plans.sh`
      moves it inside the delivery commit)

## Post-Completion

**Manual**

- `git stash drop stash@{0}` on the development machine: the `analyzer-baseline` stash carried the
  same intent against an older tree and conflicts with everything this plan lands
- a global dotnet tool that comes from a source other than `nuget.org` is installed or updated
  from outside the repository directory, or with `--add-source`, because `nuget.config` clears the
  inherited sources inside it
- the IDE0045/IDE0046 mute and its reason, and the `dotnet_diagnostic.CA1001.severity = none`
  test-block mute (a test double's lifetime ends with the test; it owns no disposable field worth
  an `IDisposable`), go into the canonical `assets/editorconfig/csharp.editorconfig` through
  `marketplace-ops`, so the next project starts from the same file

**Executed by exec:**
- branch: analyzer-baseline

## Verify it yourself

Run from the repository root on `analyzer-baseline`, Docker Desktop running for the integration
suite.

1. The gate is on:
   ```powershell
   dotnet build SemiPlot.slnx
   ```
   0 warnings, 0 errors. On `master` the same command reports 14 NU1507 warnings and, with the
   canonical configuration dropped in, 172 analyzer warnings.
2. The gate bites:
   ```powershell
   # add `const int badName = 1;` inside any method, then
   dotnet build SemiPlot.slnx        # error IDE1006, then revert the edit
   ```
3. The format check is clean and enforced:
   ```powershell
   dotnet format SemiPlot.slnx --verify-no-changes   # exit 0
   ```
   The pre-commit hook runs the same command; the Linux CI job now runs it as its own step
   (run 33880908630 on PR #56 is the first green one).
4. One package source, none private:
   ```powershell
   dotnet restore SemiPlot.slnx 2>&1 | Select-String NU1507   # prints nothing
   Get-Content nuget.config                                    # nuget.org only
   ```
5. Tests unchanged in behaviour, two added:
   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --no-build                # 705 passed, 0 skipped
   dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj --no-build  # 80 passed, 0 skipped
   ```
   The two additions are `DisposingTheProviderCompletesTheConnectionFaultStream` and
   `DisposingTwiceDoesNotThrow` in `SemiPlot.Tests.Unit/Postgres/PostgresCompositionTests.cs`;
   the pre-fix provider (`master`) has no `Dispose` at all.
6. The integration project has no `Integration/` subfolder:
   ```powershell
   Test-Path SemiPlot/SemiPlot.Tests.Integration/Integration   # False
   ```
7. The one behaviour change a user can see: the axis-limit editor and the hover readout format
   numbers in the machine's current culture, and the editor parses its own text back with the
   same culture, so `1,5` on a ru-RU machine round-trips. There is no headless repro for the
   click path; `ChartHoverReadoutTests` and `MinimapViewModelTests` pin the format.
