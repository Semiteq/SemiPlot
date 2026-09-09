# Make the source ASCII

## Overview

Every `.cs` file in the repository is plain ASCII: no UTF-8 byte order mark, no typographic
punctuation, no Greek letters. The pre-commit hook already enforces that through `terse`
(`.githooks/pre-commit:13-25`, `git show :file | dotnet terse --stdin file`), and today it rejects
every commit that stages a `.cs` file, because each of the 203 tracked files starts with the
`EF BB BF` mark `.editorconfig:25` (`charset = utf-8-bom`) demands, and 64 further findings
(`U+2014`, `U+0394`, `U+2026`) sit in comments and in the strings the operator reads.

The change has four parts:

- the encoding rule flips to `charset = utf-8` and `dotnet format` strips the mark from every file;
- the punctuation in comments becomes ASCII;
- the strings the operator reads move into `Resources.resx` with a compile-time accessor, so the
  glyphs that have no ASCII form (`Δt`, `Δy`, the `—` placeholder) live in a resource file that
  `terse` does not read;
- CI runs `terse` over the tree, so a clone without the hook cannot regress the invariant.

## Context (from discovery)

- `.editorconfig:25` `charset = utf-8-bom` under `[*.cs]`; `.editorconfig:306` covers `resx` with the
  inherited `utf-8`. `.gitattributes` normalises line endings only (`*.cs text eol=lf diff=csharp`);
  git never inserts or removes a byte order mark.
- `dotnet terse SemiPlot` on 2026-09-09: 65 findings, all `non-ascii`; after restoring the
  `<paramref>` tags, 64. Eleven sit in string literals: `SemiPlot/SemiPlot.UI/Chart/ChartDeltaCursorReader.cs:46,48`
  (`"—"`, `$"Δt ...   Δy ..."`), `SemiPlot/SemiPlot.UI/Chart/ChartHoverReadout.cs:42` (`"—"`),
  `SemiPlot/SemiPlot.UI/Legend/TrendLegendRowViewModel.cs:97,126` (`"—"`),
  `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs:78,86` (an em dash inside a remedy sentence),
  `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHoverReadoutTests.cs:40,91` (`"Pen 2: —"`),
  `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs:925` (`Contain("Δt")`),
  `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs:80` (an em dash in an
  assertion message). The remaining 53 are in `//` and `///` comments.
- Operator strings outside `.cs`: `SemiPlot/SemiPlot.UI/Toolbar/TrendToolbarView.axaml:16-42` (Autoscale,
  Min, Max, Set Limits, `Layer: {0}`, Jump to Now, Sticky, Delta),
  `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml:15` (`Title="SemiPlot — Trend Viewer"`) and `:62`
  (the empty-catalogue message). `.axaml` files carry no byte order mark today and `MainWindow.axaml:15`
  already builds with a non-ASCII title; XML defaults to UTF-8.
- `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureView.cs:3-8` states the failure view is built with no
  resource lookup because it reaches the operator before configuration is loaded;
  `ArchiveFailureMapper.cs` holds 68 English literals with interpolation. They stay literals.
- The hover readout composes `pen.Pen.Name`, `": "` and `FormatValue(value)` at
  `SemiPlot/SemiPlot.UI/Chart/ChartHoverReadout.cs:25-27`; number and date formatting use
  `CultureInfo.CurrentCulture` (`ChartHoverReadout.cs:37,42`, `ChartDeltaCursorReader.cs:46`). That stays:
  the readout is what the operator reads on their own machine.
- There is no resource file in the repository (`git ls-files '*.resx'` is empty) and no
  `Localization` namespace. `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:3-7` has one `PropertyGroup`
  (`OutputType`, `StartupObject`); packages are versioned centrally in
  `SemiPlot/Directory.Packages.props:4,8`.
- Tests reference `SemiPlot.UI` directly (`CLAUDE.md`, Test section), so a test may read `Resources.Key`.
- `.github/workflows/ci.yml:88-89` runs `dotnet format SemiPlot.slnx --verify-no-changes` on the Linux
  job only; neither job runs `terse`. `dotnet format` reports nothing for a non-ASCII character in a
  file without a byte order mark (verified 2026-09-09 on a scratch project).
- `.git-blame-ignore-revs` does not exist. 154 of the 203 files open with a `using` line, which the
  strip commit rewrites.

### Measured on 2026-09-09 (scratch worktree of this repository)

- Roslyn compiles a BOM-less ASCII file identically on this machine (ANSI code page 1251); the code
  page fallback engages only for bytes that are not valid UTF-8, which an ASCII-only tree never holds.
- `dotnet format` under `charset = utf-8`: fix mode strips the mark byte-exactly and exits 0, verify mode
  exits 2 with `error CHARSET` on a file that still carries one, and it never adds one back.
- `dotnet new class` on SDK 10.0.401 writes the mark regardless of `.editorconfig`; a file from a
  template passes the hook only after one `dotnet format SemiPlot.slnx --include <file>`.
- Every generated file under `SemiPlot/Artifacts/obj/**` (`*.AssemblyInfo.cs`, `*.GlobalUsings.g.cs`,
  Aspire `*.ProjectMetadata.g.cs`) is already BOM-less; none is tracked.
- Resource accessor, cold build (`SemiPlot/Artifacts` deleted first) with `{x:Static l:Resources.X}` in
  `TrendToolbarView.axaml` and `Resources.X` in `ChartDeltaCursorReader.cs`:
  - MSBuild `StronglyTypedFileName` metadata on the `EmbeddedResource`: cold build fails with
    `AVLN2000: Unable to resolve "Resources.ToolbarAutoscale"`, the second build passes. The generated
    class is `internal`.
  - `Microsoft.CodeAnalysis.ResxSourceGenerator` `3.12.0-beta1.25218.8` with
    `<EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true" Public="true" />`:
    cold build 0 errors, 0 warnings; the four `TrendChartViewModelTests` cases matching `Delta` pass,
    so the lookup returns `Δt` at runtime. The generated type is
    `public static class SemiPlot.UI.Localization.Resources` with `ResourceManager`, `Culture` and one
    `public static string Key => GetResourceString("Key")` per entry.
  - Both variants need `<NeutralLanguage>en</NeutralLanguage>`, otherwise the analyzer baseline fails
    the build with `CA1824`.
  - The generator package has no stable release (38 versions on nuget.org, all prerelease). It is the
    package dotnet/roslyn and dotnet/roslyn-analyzers build their own resources with.

## Development Approach

- **testing approach**: Regular (code first, then tests)
- complete each task fully before moving to the next
- every task that changes behaviour updates the tests that pin it; the tasks that change bytes only
  (Task 1, Task 2) are verified by the two gates, not by new tests
- all tests must pass before starting the next task
- update this plan file when scope changes during implementation
- `terse` and `dotnet format --verify-no-changes` over the touched files exit 0 before each commit; the
  hook enforces both, so a commit that goes through is proof
- no `--no-verify` after Task 1: the hook is the acceptance gate of this plan

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` (768 on `master`) must stay green; the three tests that assert
  on `Δt`, `Δy` and `—` switch to the resource accessor
- **integration tests**: `SemiPlot.Tests.Integration` (85) unaffected in behaviour; one assertion
  message loses an em dash
- no e2e suite exists

## Acceptance Evidence

Reproduce today (2026-09-09, branch `terse-gate`):

```sh
dotnet terse SemiPlot                       # 64 findings, all non-ascii, exit 1
for f in $(git ls-files '*.cs'); do git show ":$f" | dotnet terse --stdin "$f"; done | grep -c U+FEFF   # 203
git ls-files '*.cs' | wc -l                 # 203
```

After the plan, all three commands change and every one is runnable in CI:

```sh
dotnet terse $(git ls-files '*.cs')         # exit 0, no output
for f in $(git ls-files '*.cs'); do git show ":$f" | dotnet terse --stdin "$f" || echo "FAIL $f"; done   # no FAIL line
dotnet format SemiPlot.slnx --verify-no-changes   # exit 0
git diff --stat master...HEAD -- '*.cs' | tail -1 # 207 files: the BOM change, the edits below and four new test files
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 777 passed, 0 failed
rm -rf SemiPlot/Artifacts && dotnet build SemiPlot.slnx   # cold build, 0 warnings, 0 errors
```

The cold build is the evidence for the resource accessor; the hook run on the commit of Task 3 is
the evidence that the tree is ASCII-only in the index, not only in the working copy.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview

**Encoding.** `[*.cs]` gets `charset = utf-8`; `dotnet format SemiPlot.slnx` rewrites the 203 files.
No hand-rolled stripper: `sed` and `Set-Content` on Windows can change line endings or re-encode the
non-ASCII lines, and `dotnet format` is the tool the hook and CI already trust. The strip is its own
commit so the diff is 203 one-line hunks and nothing else.

**Comments.** Each of the 53 comment findings becomes ASCII by hand, sentence by sentence: an em dash
becomes a comma, a period or a colon depending on what the sentence needs; `…` becomes `..` inside a
range and a period elsewhere; `Δy` in prose becomes "delta y". No knowledge is dropped; a comment that
cannot be made ASCII without losing a fact is reported, not shortened.

**Operator strings.** `SemiPlot/SemiPlot.UI/Localization/Resources.resx` holds every string the
operator reads that is neither a failure remedy nor a log line, under `SemiPlot.UI.Localization.Resources`
generated at compile time by `Microsoft.CodeAnalysis.ResxSourceGenerator`. The generator runs inside
the C# compiler, so the accessor exists before XamlIl rewrites the assembly and `{x:Static}` resolves on
a cold build; MSBuild's `StronglyTypedFileName` route does not, and a committed `Designer.cs` costs a
third file per key. One neutral English set, no satellite assembly, no culture wiring:
`CultureInfo.DefaultThreadCurrentUICulture` and `Resources.Culture` are never assigned, because there is
one language. `NeutralLanguage` is `en`.

`ArchiveFailureMapper` keeps its literals: the failure window opens before configuration is read, the
strings interpolate host names and SQLSTATEs, and the operator reads them alongside the English log.
Its two em dashes become ASCII in place. `ArchiveFailureView.cs:3-8` keeps saying so.

Tests that pin a glyph assert through the accessor (`Resources.NoValuePlaceholder`), so the resource
value can change without the test naming it.

**CI.** The Linux job restores the tool manifest and runs `terse` over `git ls-files '*.cs'` right after
the format check, so a pull request from a clone without `core.hooksPath` fails there.

**Blame.** After the pull request merges, `.git-blame-ignore-revs` names the merge commit; GitHub reads
the file on its own, local git needs `git config blame.ignoreRevsFile .git-blame-ignore-revs`. The hash
exists only after the merge, so this is a Post-Completion step.

**Out of scope.** The canonical `.editorconfig` asset in the `project-layout` skill, SemiStep and NtoLib
carry the same `utf-8-bom` line; they are separate changes in their own repositories. Grouping small
types into their consumers' files is `docs/plans/20260909-group-small-types.md`.

## Technical Details

Resource keys, all in `Localization/Resources.resx`, values in English:

| key | value | consumer |
| --- | --- | --- |
| `DeltaTimeLabel` | `Δt` | `ChartDeltaCursorReader.FormatReadout` |
| `DeltaValueLabel` | `Δy` | `ChartDeltaCursorReader.FormatReadout` |
| `NoValuePlaceholder` | `—` | `ChartDeltaCursorReader.cs:46`, `ChartHoverReadout.FormatValue`, `TrendLegendRowViewModel.cs:97,126` |
| `ToolbarAutoscale` | `Autoscale` | `TrendToolbarView.axaml:16` |
| `ToolbarMinPlaceholder` | `Min` | `TrendToolbarView.axaml:20` |
| `ToolbarMaxPlaceholder` | `Max` | `TrendToolbarView.axaml:24` |
| `ToolbarSetLimits` | `Set Limits` | `TrendToolbarView.axaml:27` |
| `ToolbarLayerFormat` | `Layer: {0}` | `TrendToolbarView.axaml:30` |
| `ToolbarJumpToNow` | `Jump to Now` | `TrendToolbarView.axaml:33` |
| `ToolbarSticky` | `Sticky` | `TrendToolbarView.axaml:36` |
| `ToolbarDelta` | `Delta` | `TrendToolbarView.axaml:40` |
| `StatusPenCountFormat` | `Pens: {0}` | `MainWindow.axaml:86` |
| `WindowTitle` | `SemiPlot — Trend Viewer` | `MainWindow.axaml:15` |
| `EmptyCatalogueMessage` | the sentence at `MainWindow.axaml:62` | `MainWindow.axaml:62` |

Project wiring (`SemiPlot.UI.csproj`):

```xml
<PropertyGroup>
  <NeutralLanguage>en</NeutralLanguage>
</PropertyGroup>
<ItemGroup>
  <PackageReference Include="Microsoft.CodeAnalysis.ResxSourceGenerator" PrivateAssets="all" />
  <EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true" />
</ItemGroup>
```

with `<PackageVersion Include="Microsoft.CodeAnalysis.ResxSourceGenerator" Version="3.12.0-beta1.25218.8"/>`
in `Directory.Packages.props`. The version is pinned exactly; a prerelease package floats only if asked to.
The generated class is `internal` and its members `public`: `InternalsVisibleTo` reaches both test
projects and XamlIl rewrites the same assembly, so the item needs no `Public="true"`.

XAML: `xmlns:text="clr-namespace:SemiPlot.UI.Localization"` on the root element, then
`Content="{x:Static text:Resources.ToolbarAutoscale}"`; the layer label keeps its binding with
`StringFormat="{x:Static text:Resources.ToolbarLayerFormat}"`. ASSUMPTION: Avalonia's `StringFormat` accepts
an `x:Static` value; if it does not, the label becomes a view-model property formatted with the
resource, as `TrendToolbarViewModel` already does for `DeltaReadoutText`.

CI step, Linux job, after `format check`:

```yaml
      - name: comment gate
        run: |
          dotnet tool restore
          dotnet terse $(git ls-files '*.cs')
```

## Implementation Steps

### Task 1: Flip the encoding rule and strip the byte order mark

**Files:**
- Modify: `.editorconfig`
- Modify: every tracked `.cs` file (203, one line each)

- [x] `.editorconfig:25` becomes `charset = utf-8`
- [x] run `dotnet format SemiPlot.slnx`; confirm `git diff --stat -- '*.cs' | tail -1` reports 203 files, 203 insertions, 203 deletions, and `git diff -- .editorconfig` one line
- [x] confirm the change is the mark alone: `git diff --numstat -- '*.cs' | awk '$1 != 1 || $2 != 1'` prints nothing, and `head -c 3 <file> | xxd -p` on three files no longer reads `efbbbf`
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` exits 0; `dotnet build SemiPlot.slnx` 0 warnings, 0 errors
- [x] commit this task alone (`git commit --no-verify` is allowed for this one commit: the tree still holds the 64 non-ASCII characters the hook will reject until Task 2 and Task 3 land)

### Task 2: Make the comments ASCII

**Files:**
- Modify: the files `dotnet terse SemiPlot` names on lines that are `//` or `///` comments (53 findings on 2026-09-09)

- [x] for each finding, rewrite the sentence in ASCII without dropping a fact: em dash to comma, period or colon; `…` to `..` in a range (`ChartNavigationControllerTests.cs:168`), a period elsewhere; `Δy` to "delta y" (`DeltaCursorModel.cs:3`)
- [x] `dotnet terse SemiPlot` reports only the eleven string-literal findings from the Context section
- [x] `dotnet format SemiPlot.slnx --verify-no-changes` exits 0 (a rewrapped `///` line must stay under 120 columns)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - 768 passed (comments only, but the hook needs a green tree)
- [x] commit through the hook. The hook lints staged files only, so stage every file whose findings are all gone; a file that also holds a string finding of Task 3 (`TrendChartViewModelTests.cs` holds both) stays unstaged and commits with Task 3

### Task 3: Move the operator strings into `Resources.resx`

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`, `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.UI/Chart/ChartDeltaCursorReader.cs`, `SemiPlot/SemiPlot.UI/Chart/ChartHoverReadout.cs`, `SemiPlot/SemiPlot.UI/Legend/TrendLegendRowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Toolbar/TrendToolbarView.axaml`, `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`, `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureView.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartHoverReadoutTests.cs`, `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/TrendChartViewModelTests.cs`, `SemiPlot/SemiPlot.Tests.Integration/Journeys/LiveEdgeArchiveJourneyTests.cs`

- [x] add the package version to `Directory.Packages.props` and the `PackageReference`, `EmbeddedResource Update` and `NeutralLanguage` to `SemiPlot.UI.csproj` as in Technical Details
- [x] create `Resources.resx` with the thirteen keys of the table; the file is UTF-8 without a byte order mark, values verbatim
- [x] replace the C# literals: `ChartDeltaCursorReader.cs:46,48` read `Resources.NoValuePlaceholder`, `Resources.DeltaTimeLabel`, `Resources.DeltaValueLabel`; `ChartHoverReadout.cs:42` and `TrendLegendRowViewModel.cs:97,126` read `Resources.NoValuePlaceholder`
- [x] replace the XAML literals in `TrendToolbarView.axaml:16-42` and `MainWindow.axaml:15,62` with `{x:Static l:Resources.Key}`. ASSUMPTION RESOLVED: Avalonia accepts `StringFormat={x:Static l:Resources.ToolbarLayerFormat}`; the cold build compiles it and a headless probe rendered `Layer: Raw`, so the label keeps its binding and no view-model property was added
- [x] `ArchiveFailureMapper.cs:78,86`: the em dash becomes a colon or a period; `ArchiveFailureView.cs:3-8` keeps the reason its literals stay in code, in ASCII (the summary was already ASCII and already states the reason, so it needed no edit)
- [x] update the three tests to assert through the accessor: `ChartHoverReadoutTests.cs:40,91` expect `$"Pen 2: {Resources.NoValuePlaceholder}"` and its sibling, `TrendChartViewModelTests.cs:925` expects `Resources.DeltaTimeLabel` and `Resources.DeltaValueLabel`; `LiveEdgeArchiveJourneyTests.cs:80` loses its em dash
- [x] write a test in `SemiPlot.Tests.Unit` that pins the accessor: every key in the table resolves to a non-empty string and `Resources.DeltaTimeLabel` starts with `Δ`, spelled `"\u0394"` so the test file itself stays ASCII (`SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`)
- [x] `rm -rf SemiPlot/Artifacts && dotnet build SemiPlot.slnx`: 0 warnings, 0 errors on a cold build
- [x] `dotnet terse $(git ls-files '*.cs')` exits 0 with no output; `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - 776 passed (768 plus the eight tests the accessor brought)
- [x] commit through the hook, no `--no-verify`; the hook passing on a commit that stages `.cs` files is the first proof the index is ASCII
- ➕ `StatusPenCountFormat` (`Pens: {0}`, `MainWindow.axaml:86`) joined the table in review: the
  Solution Overview rule covers it and the literal had been missed, so the key set is fourteen
- ➕ the `EmbeddedResource` item dropped `Public="true"` in review: the generated class is
  `internal`, `InternalsVisibleTo` reaches both test projects and XamlIl rewrites the same assembly,
  so a cold build and the unit run stay green without it
- ➕ the AXAML prefix is `text:`, not the `l:` the checkbox above records: the review pass renamed it
  in both views and in the docs, because every neighbouring prefix (`local`, `toolbar`, `legend`,
  `minimap`) is a full word

### Task 4: Run the comment gate in CI

**Files:**
- Modify: `.github/workflows/ci.yml`

- [x] add the `comment gate` step to the Linux job after `format check` (`ci.yml:88-89`), as in Technical Details
- [x] add `.config/dotnet-tools.json` and `.githooks/**` to both `paths` lists (`ci.yml:6-13,15-22`) so a tool bump or a hook change runs the workflow
- [x] verify the step locally with the same command line: `dotnet tool restore && dotnet terse $(git ls-files '*.cs')` exits 0
- [x] verify the step catches a regression: stage a scratch `.cs` with `//` and an em dash, run the same command, confirm exit 1, remove the file
- [x] commit through the hook
- ⚠️ `terse` reports no byte order mark in file mode, so the `comment gate` step is the character
  gate only; the `format check` step ahead of it is the byte order mark gate (`error CHARSET`)

### Task 5: Verify acceptance criteria

- ⚠️ the review pass replaced the hand-listed 13-case theory with three reflective facts and added
  `ChartDeltaCursorReaderTests`, `TrendToolbarViewTests` and one legend case, so the recorded counts
  below are the re-run ones; a second review pass then added the toolbar placeholder case and moved
  the two toolbar test classes onto one `ToolbarTestBuilder`: 207 tracked `.cs`, 777 unit tests
- [x] run every command of the Acceptance Evidence section and record the outputs here
  - `dotnet terse $(git ls-files '*.cs')`: exit 0, no output
  - stdin loop over the index: no FAIL line, no finding
  - `dotnet format SemiPlot.slnx --verify-no-changes`: exit 0
  - `git ls-files '*.cs' | wc -l`: 207
  - `git diff --stat master...HEAD -- '*.cs' | tail -1`: 207 files changed
  - `rm -rf SemiPlot/Artifacts && dotnet build SemiPlot.slnx`: cold build, 0 warnings, 0 errors
  - `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 777 passed, 0 failed, 0 skipped, 46 s
- [x] `git log --oneline master..HEAD` lists only commits of this plan
  - the plan commits from `c6ace47`, plus `243df7e` and `05f83bd` of `terse-gate`, the branch this one
    is based on: expected, not a defect
- [x] `dotnet test SemiPlot.slnx` with the Docker bench: 768 + new unit, 85 integration passed (⚠️ actual: unit 777 passed; integration not run)
  - `docker info` exit 1, `npipe:////./pipe/dockerDesktopLinuxEngine` absent and no Docker Desktop on this machine,
    so `SemiPlot.Tests.Integration` was skipped; CI's Linux job runs it
- [x] open the viewer against the bench (`dotnet run --project SemiPlot/SemiPlot.AppHost`): the window title, the toolbar captions, the delta readout with `Δt`/`Δy` and the `—` placeholder in the legend render as before (skipped - not automatable)
  - a human should confirm the window title, the toolbar captions, the delta readout labels and the legend
    no-value placeholder still render; the headless tests cover the rendering path

### Task 6: Update documentation

**Files:**
- Modify: `CLAUDE.md`, `docs/architecture/README.md`, `docs/architecture/charting.md`, `docs/plans/backlog.md`
- Create: `docs/architecture/ui-text.md`

- [x] `docs/architecture/ui-text.md`: where operator text lives (`Resources.resx`, one English set, no culture wiring), the generator and why it beats `StronglyTypedFileName` (the cold-build measurement), what stays a literal (`ArchiveFailureMapper`, log templates), and the rule that a glyph without an ASCII form is a resource, never a `\u` escape in code
- [x] `docs/architecture/README.md`: index entry for `ui-text.md`
- [x] `CLAUDE.md` Build section: `charset = utf-8`, a file from `dotnet new` needs one `dotnet format SemiPlot.slnx --include <file>` before it passes the hook, CI runs `terse`; the Comments section says "ASCII only" beside "English only"
- [x] `docs/architecture/charting.md` module layout: `Localization/Resources.resx` and the accessor
- [x] `docs/plans/backlog.md`: note under Tooling that the canonical `.editorconfig` asset, SemiStep and NtoLib still carry `charset = utf-8-bom`
- [ ] move this plan to `docs/plans/completed/` (left in place; the delivery step archives it)

## Verify it yourself

Every check runs from the repository root on branch `ascii-source`.

1. The tree is ASCII, in the working copy and in the index:

```sh
dotnet terse $(git ls-files '*.cs')
for f in $(git ls-files '*.cs'); do git show ":$f" | dotnet terse --stdin "$f" || echo "FAIL $f"; done
```

Both are silent and exit 0. On `master` the first prints 65 findings and exits 1, and the second
prints a `U+FEFF` line for all 203 files, which is why no commit touching a `.cs` file could pass
the hook before this branch.

2. No byte order mark and no non-ASCII byte in any tracked source:

```sh
file $(git ls-files) | grep -i "with BOM"
grep -rlP "[^[:ascii:]]" $(git ls-files '*.cs')
```

Both print nothing. `readme.md` and the older documents under `docs/` keep their Russian text on
purpose; the ASCII rule covers `.cs` sources and the documents this branch wrote.

3. The build and the tests, from cold output:

```sh
rm -rf SemiPlot/Artifacts && dotnet build SemiPlot.slnx
dotnet format SemiPlot.slnx --verify-no-changes
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj
```

0 warnings and 0 errors; format exits 0; 777 tests pass. The cold build is what proves the resource
accessor: `Microsoft.CodeAnalysis.ResxSourceGenerator` runs inside the compiler, so
`{x:Static text:Resources.Key}` resolves on the first pass. The MSBuild `StronglyTypedFileName`
route fails that same build with `AVLN2000` (measured 2026-09-09), which is why it is not used.

4. The hook blocks a regression. Write a scratch file with one non-ASCII character, stage it, and
try to commit:

```sh
cat > SemiPlot/SemiPlot.Core/Probe.cs <<'EOF'
namespace SemiPlot.Core;

// a comment with an em dash - here, typed as U+2014
public sealed class Probe { }
EOF
git add SemiPlot/SemiPlot.Core/Probe.cs && git commit -m "probe"
git rm -q --cached SemiPlot/SemiPlot.Core/Probe.cs && rm SemiPlot/SemiPlot.Core/Probe.cs
```

Replace the `-` in that comment with a real U+2014 before staging. The commit is refused with
`Probe.cs:3: non-ascii  non-ASCII character U+2014`. A file carrying a byte order mark and no other
non-ASCII character is refused too, because the hook reads the staged content through `--stdin`;
`terse` in file mode does not see a mark, which is why CI pairs its `comment gate` step with
`format check`, whose `error CHARSET` is the byte-order-mark gate.

5. The viewer renders the resource strings. This is the one manual check, because no headless test
loads `MainWindow.axaml`:

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost
```

Read the window title, the toolbar captions, the layer label, and hover a pen to see the delta
readout. Expect the product name and `Trend Viewer` in the title bar, the seven toolbar captions,
`Layer: Raw`, and the two delta labels carrying the Greek capital delta. A pen with no value in the
window shows the placeholder in the legend.

## Post-Completion

**Executed by exec:**

- branch: ascii-source


**After the pull request merges**

- Add `.git-blame-ignore-revs` at the repository root naming the merge commit of this pull request
  (the hash exists only after the merge), and run `git config blame.ignoreRevsFile .git-blame-ignore-revs`
  on each clone. One-line follow-up pull request.

**Operator checks on this machine**

- Rider, Add Class into `SemiPlot.Core`, then `head -c 3 <file> | xxd -p`: the answer decides whether
  Rider honours `charset = utf-8` on new files. If it writes `efbbbf`, set Settings, Editor, File
  Encodings, "Create UTF-8 files" to "with NO BOM".
- The same check in Visual Studio if it is used for this repository.

**Other repositories**

- `project-layout` skill asset `assets/editorconfig/csharp.editorconfig:25`, SemiStep (546 of 547 `.cs`
  with a mark), NtoLib (625 of 625): the same flip, each through its own pull request via
  `marketplace-ops` for the asset.
- `terse`: report the byte order mark in file mode the way `--stdin` does, so both modes agree; treat
  control characters other than tab, CR and LF as violations; the two evasions the 2026-09-09 probe
  passed (two three-line `//` runs split by a blank line, four trailing `//` comments in a row).
