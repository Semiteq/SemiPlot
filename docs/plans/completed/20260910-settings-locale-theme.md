# Application settings, Russian interface and the JetBrains palette

## Overview

Three changes that share one file and one startup path, delivered together because each of the other
two needs a key in the first.

- A required `ui/app.yaml` in the configuration directory, holding the interface language and the
  theme variant. A missing file, a missing key or an unparsable value stops the application with the
  startup failure the operator already sees for a missing connection file.
- A Russian interface: `Resources.ru.resx` beside the neutral English set, the culture chosen by
  `locale`, and the startup failure window localized with the rest.
- `Semi.Avalonia` with the JetBrains palette applied through `ThemeDictionaries`, both variants
  defined, `theme` choosing which one loads. Every colour literal in the tree resolves to a key, and
  the four ScottPlot surfaces the library paints from its own defaults come under the same palette.

Closes #85, #84 and #83.

## Context

- `SemiPlot.UI/StartupOptions.cs:10-14` puts configuration in `C:\DISTR\Config\SemiPlot` and logs in
  `C:\DISTR\Logs\SemiPlot`, with `--config-dir` overriding the first.
- `Startup/StartupProbe.cs:28-33` loads `archive-connection.yaml` as the first step of startup and
  fails the whole `Result` when it cannot. `docs/architecture/overview.md:104-106` records that the
  file is required and that an installation without one shows the startup failure instead of a chart.
- `App.axaml.cs:66-83` is `App.Run(Result<StartupData>)`. Inside `AfterSetup` it returns early when
  the result is failed, after mapping the first error into `_startupFailure`. Anything that must hold
  on the failure path has to be assigned above that return.
- `MainWindow/ArchiveFailureMapper.cs:26-36` turns an `IError` into a title, a detail and a remedy by
  switching on the error type. `ConnectionFileError` carries a `ConnectionFileProblem` kind and the
  path it looked at.
- `Localization/Resources.resx` holds fourteen keys in one neutral English set.
  `SemiPlot.UI.csproj:7` sets `<NeutralLanguage>en</NeutralLanguage>`; without it the analyzer
  baseline fails with `CA1824`.
- `docs/architecture/ui-text.md:44-52` records why the accessor comes from
  `Microsoft.CodeAnalysis.ResxSourceGenerator` rather than MSBuild: the generator runs inside the
  compiler, so the accessor exists before XamlIl rewrites the assembly and `{x:Static}` resolves on a
  cold build. The generated type carries a settable `Culture`, which `ui-text.md:14` records as never
  assigned.
- `App.axaml` is eight lines: a `FluentTheme` pinned to `Light`, no resource dictionary, no included
  styles.
- Eighteen colour literals live on the controls that use them, in three files: `MainWindow.axaml`
  at 26, 36, 44, 59, 70, 82 and 92; `Minimap/MinimapView.axaml` at 12, 14, 16, 22, 26, 32, 37 and 39;
  `Chart/TrendChartView.axaml` at 18, 22 and 23. Seven of them are the named colour `Gray`.
- Nothing assigns ScottPlot's figure background, data background, grid or axis colours, so the plot
  paints the library defaults inside a border painted `#1E1E1E`.
- `SemiPlot/Directory.Build.props:4` sets `<ArtifactsPath>` to `SemiPlot/Artifacts`, so intermediates
  live at `SemiPlot/Artifacts/obj/<project>` and there is no `obj` or `bin` beside a project file.
- `.gitignore:66` ignores `artifacts/`, and `SemiPlot/Artifacts/bench-config/archive-connection.yaml`
  is ignored by it. That directory is converge output, not a checked-in asset.
- `SemiPlot/Directory.Packages.props` manages versions centrally. `YamlDotNet` 18.1.0 is pinned there
  and referenced by `SemiPlot.DataSource.Postgres`, not by `SemiPlot.UI`.
- `SemiPlot.Tests.Unit` runs its classes in parallel and overrides no collection behaviour.
- The sibling SemiStep retints eight `Semi.Avalonia` tokens inside a `Light` theme dictionary, and
  overrides the component corner-radius keys outside it, because Semi's component keys alias
  `SemiBorderRadiusSmall` through `StaticResource` and the primitive cannot be retargeted.

## Development Approach

- Regular: code first, then tests, in the same task.
- Every task ends green: `dotnet build SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes`
  and `dotnet terse` over the touched files all pass before the next task starts.
- The task order keeps the bench working at every boundary: converge writes `ui/app.yaml` before the
  application requires it, so no task leaves `SemiPlot.AppHost` unable to start.

## Testing Strategy

- Unit tests in `SemiPlot.Tests.Unit`: the settings loader's success and every failure shape, the
  startup ordering, the resource-set parity, the failure mapper's arms, the theme variant.
- Headless Avalonia tests for anything needing the application object: the variant the settings
  choose, and a palette key resolving in both variants.
- Tests that mutate `CultureInfo.DefaultThreadCurrentUICulture` or the application's theme variant
  share one xunit collection with `ResourcesTests` and restore the previous value in a `finally`,
  the way `AppBuilderCompositionTests` restores `Logger.Sink`. Both are process-global, and the suite
  runs classes in parallel.
- No container is involved; nothing here touches the archive. The converge path is covered by testing
  the writer over a temporary directory, as `ConnectionFileWriterTests` already does, not by running
  `Converge.RunAsync`, which opens a connection.

## Acceptance Evidence

Run from the repository root. Items 1 and 7 need the bench stand up, so that a failure the evidence
does not name cannot stand in for the one it does.

1. **A configuration directory without the file stops the application.** With the bench running and a
   configuration directory holding a valid `archive-connection.yaml` and no `ui/app.yaml`:

   ```powershell
   dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir <dir>
   ```

   The window shows the startup failure naming the missing `ui/app.yaml` and the remedy, and no chart.
   Before this plan the same command draws a chart.

2. **Every failure shape is a typed error, not an exception.**

   ```powershell
   dotnet test SemiPlot.slnx --filter "FullyQualifiedName~AppSettingsLoaderTests"
   ```

   Covers a missing file, an unreadable file, each missing key, each invalid value and a malformed
   document, asserted by error type and field, never by message wording.

3. **The settings are read before the connection file.**

   ```powershell
   dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StartupSequenceTests"
   ```

   Pointed at a directory whose `ui/app.yaml` is invalid and whose `archive-connection.yaml` is
   absent, the sequence fails with the settings error. A `ConnectionFileError` there proves the order
   is wrong.

4. **The two resource sets agree.**

   ```powershell
   dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ResourcesTests"
   ```

   Fails when a key exists in one set and not the other, and when the `ru` satellite does not load at
   all. Reading the sets, not the generated accessor, is what makes this able to fail.

5. **A cold build resolves every `{x:Static}` on the first pass.**

   ```powershell
   git clean -xdf SemiPlot/Artifacts/obj/SemiPlot.UI SemiPlot/Artifacts/bin/SemiPlot.UI
   dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj
   ```

   Zero warnings, zero errors, no `AVLN2000`. Cleaning `SemiPlot/SemiPlot.UI/obj` cleans nothing,
   because `ArtifactsPath` moves the intermediates.

6. **No colour literal survives outside the palette.** The command returns nothing and exits 1:

   ```powershell
   git grep -nE '#[0-9A-Fa-f]{6,8}\b|"(Gray|Black|White)"' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'
   ```

   Today it returns eighteen hits across three files, which is the red state this replaces.

7. **Manual smoke, one observable outcome per step.**
   1. Start with `theme: light`. The window chrome, the panels and the plot share one light palette,
      and the minimap keeps no dark strip of its own.
   2. Start with `theme: dark`. The same surfaces, the plot's figure and data background included, are
      dark, and every label stays readable.
   3. Start with `locale: en`. Captions are English while numbers and timestamps keep the machine's
      format.
   4. Break the archive connection and start with `locale: en`. The connection failure is read in
      English, which is the configured language.
   5. Set `locale: klingon`. The settings failure names the key and its accepted values, and is read
      in Russian, which is the bootstrap language, because no configured language exists yet.

## Solution Overview

**One required file, read before Avalonia.** The settings load runs immediately after the command line
is parsed and the logger created, before `StartupProbe.Run`. The language has to be set before any
window is constructed, and the connection file must not be read first, or a broken archive would mask
a broken configuration.

**No default a production run can reach.** The loader returns a typed failure for a missing file, a
missing key and an unparsable value. There is no `AppSettings.Default`, and nothing substitutes a
value the file does not carry. Configuration comes from the file, and the file is part of the
delivery.

**Bootstrap language and bootstrap theme.** A failure that reports the settings file itself is emitted
before any `locale` or `theme` is known. Those strings use Russian, set before the file is read, and
that window renders on the light variant that `App.axaml` declares. Every other string follows
`locale`, and every other window follows `theme`. The declared variant in `App.axaml` is also what the
headless test builders see, since they construct `App` directly and never call `App.Run`.

**Signature.** `App.Run(AppSettings? settings, Result<StartupData> startup)`. The settings are null only
when the settings load itself failed, so an archive failure still renders on the configured theme.
`RequestedThemeVariant` is assigned above the early return at `App.axaml.cs:73`, so it holds on both
paths.

**Language.** The neutral set stays English and `Resources.ru.resx` compiles to a `ru` satellite. Only
`CultureInfo.DefaultThreadCurrentUICulture` is assigned; `Resources.Culture` stays unassigned so the
thread culture remains the single writer, and `CurrentCulture` is left alone, so numbers and
timestamps keep following the machine as they do today.

**Theme.** `Semi.Avalonia` replaces `FluentTheme`. Its semantic tokens are retinted to the JetBrains
palette in `Light` and `Dark` dictionaries, the application's own surface keys sit beside them, and
`theme` chooses the variant. The font is the system stack; no font ships with the application.

## Technical Details

### The file

`<config-dir>/ui/app.yaml`, YAML, underscored keys, unknown keys ignored, matching the deserializer
the connection loader builds at `PostgresConnectionLoader.cs:50-53`.

```yaml
locale: ru     # ru | en
theme: light   # light | dark
```

Both keys are required. An absent key is a failure, not a fallback.

### Errors

| Kind | Raised when |
| --- | --- |
| `NotFound` | The file is not at `<config-dir>/ui/app.yaml` |
| `Unreadable` | The file exists and cannot be read or parsed |
| `KeyMissing` | A required key is absent or blank; the error names the key |
| `ValueInvalid` | A key holds a value outside its set; the error names the key and the accepted values |

One error type carrying a kind and the offending key, mirroring `ConnectionFileError` and its
`ConnectionFileProblem`. `ArchiveFailureMapper.Map` gains an arm for it and writes the remedy, which
is the one place a remedy is written.

### Startup order

| Step | Why here |
| --- | --- |
| `StartupOptions.Parse` | `--config-dir` names the directory the settings live in |
| `CreateLogger` | A settings failure has to reach the log |
| Load `ui/app.yaml` | Before the connection file, so a broken archive cannot mask a broken configuration |
| Culture set from `locale` | Before any window exists |
| `StartupProbe.Run` | Unchanged |
| `App.Run(settings, startup)` | The variant is applied in `AfterSetup`, above the failure return |

The ordered steps live in an internal method so the order itself is testable; `Program.Main` calls it
and does nothing else with them. A settings failure short-circuits with null settings and a failed
`Result<StartupData>` carrying the settings error.

### Semi tokens to retint

The non-obvious half of the theme work is which keys Semi's own templates read. The eight
`SemiColor*` primitives below, taken from the sibling's `App.axaml`, were the plan's answer and are
wrong: measured during execution, an override of any of them in `Application.Resources` moves no
control, because Semi's templates read derived component keys that `StaticResource`-resolve the
primitives inside Semi's own dictionaries. The values are right and the roles are right; the keys are
not. `docs/architecture/ui-theme.md` carries the component keys that were shipped instead, with the
measurement. The table stays here as the record of what the plan assumed:

| Key | Role | Light | Dark |
| --- | --- | --- | --- |
| `SemiColorPrimary` | Accent, focus, selection | `#3574F0` | `#3574F0` |
| `SemiColorText0` | Primary text | `#000000` | `#DFE1E5` |
| `SemiColorText1` | Body text | `#000000` at 0.8 opacity | `#DFE1E5` at 0.8 opacity |
| `SemiColorText2` | Secondary text | `#818594` | `#6F737A` |
| `SemiColorText3` | Tertiary text | `#A8ADBD` | `#5A5D63` |
| `SemiColorBackground1` | Panel surface | `#F7F8FA` | `#2B2D30` |
| `SemiColorBorder` | Border and divider | `#EBECF0` | `#393B40` |
| `SemiColorDisabledText` | Disabled text | `#A8ADBD` | `#5A5D63` |

The dark variant needs no key beyond the light set, which was the open question here: verified by
resolving every key Semi and the palette contribute under both variants.

Corner radius is separate. Semi's component radius keys alias `SemiBorderRadiusSmall` through
`StaticResource`, so overriding the primitive does nothing; the component keys are overridden
directly, at the top level of the dictionary and **not** inside `ThemeDictionaries`. For this tree
that is `ButtonCornerRadius`, `TextBoxDefaultCornerRadius` and `CheckBoxBoxCornerRadius`; the controls
behind the other keys the sibling overrides do not exist here yet.

### The application's own surfaces

Semi owns the controls. These are ours, and each key exists in both variants:

| Surface | Consumer | Light | Dark |
| --- | --- | --- | --- |
| Panel background | Legend panel, toolbar, banner rows | `#F7F8FA` | `#2B2D30` |
| Content background | Chart area, minimap canvas | `#FFFFFF` | `#1E1F22` |
| Border | The seven separators | `#EBECF0` | `#393B40` |
| Subtle line | Minimap baseline, plot grid | `#EBECF0` | `#393B40` |
| Secondary text | Minimap extent labels, crosshair | `#818594` | `#6F737A` |
| Accent | Minimap window highlight | `#3574F0` | `#3574F0` |

Pen colours are not tokens. They come from the archive and stay per pen.

### The plot

Four surfaces move under the same keys: `Plot.FigureBackground`, `Plot.DataBackground`, the grid's
major line colour and the axis and tick colours. All four exist in the shipped ScottPlot 5.1.59
assembly; the exact member paths are confirmed against it before use.

## Implementation Steps

### Task 1: Read the settings file

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/AppSettings.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/AppSettingsLoader.cs`
- Create: `SemiPlot/SemiPlot.UI/Startup/AppSettingsError.cs`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppSettingsLoaderTests.cs`

- [x] add the `YamlDotNet` package reference to `SemiPlot.UI`
- [x] add `AppSettings(UiLanguage Locale, AppThemeVariant Theme)` with no default instance and no default constants reachable from production
- [x] add `AppSettingsError` carrying a kind and the offending key, shaped after `ConnectionFileError`
- [x] add `AppSettingsLoader.Load(string filePath)` returning `Result<AppSettings>`, with the deserializer configured as at `PostgresConnectionLoader.cs:50-53`, and no exception escaping for any input including a blank path
- [x] write tests for a well-formed file, a missing file, an unreadable file, each missing key, each invalid value and a malformed document, asserting by error type and field
- [x] run tests, must pass before task 2

### Task 2: Make converge deliver the file

**Files:**
- Create: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/AppSettingsFileWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Converge.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Tools/AppSettingsFileWriterTests.cs`
- Modify: `docs/architecture/bench.md`

- [x] add a writer that puts `ui/app.yaml` into a directory with `locale: ru` and `theme: light`, creating the `ui` subdirectory
- [x] call it from the converge path beside the existing `ConnectionFileWriter` call
- [x] reword the `--config-dir` description at `SeederCommand.cs:85`, which names only the connection file
- [x] write tests over a temporary directory, as `ConnectionFileWriterTests` does; do not test through `Converge.RunAsync`, which opens a connection
- [x] record the second file in the converge section of `bench.md`
- [x] run tests, must pass before task 3

This task lands before the file becomes required, so the bench stand keeps starting at every boundary.
`SemiPlot/Artifacts/bench-config` is ignored by `.gitignore:66`, so the file cannot be committed there
and converge is the only mechanism that delivers it.

### Task 3: Add the Russian resource set

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`

- [x] add `Resources.ru.resx` with every key the neutral set carries, hand-edited, no byte order mark, no `xsd:schema` block
- [x] confirm the SDK takes the culture from the file name and emits a `ru` satellite; add an explicit `EmbeddedResource` item only if it does not
- [x] extend `ResourcesTests` to compare key sets between the neutral set and `GetResourceSet(new CultureInfo("ru"), createIfNotExists: true, tryParents: false)`, failing when either side has a key the other lacks or an empty value, and when the satellite does not load
- [x] put the culture-mutating tests in one collection with the existing `ResourcesTests` and restore the previous culture in a `finally`
- [x] run the cold-build check from the acceptance evidence
- [x] run tests, must pass before task 4

### Task 4: Localize the startup failure window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: the failure mapper tests under `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/`

- [x] decide the formatting mechanism and wire it: either add `EmitFormatMethods="true"` to the resx item so the generator emits `Format*` methods, or call `string.Format(CultureInfo.CurrentCulture, ...)` explicitly at each site. The existing formatted keys go through XAML `StringFormat` and are no precedent for a mapper written in C#
- [x] move every title, detail and remedy in the mapper to a key in both sets, keeping host names, paths and SQLSTATEs as arguments
- [x] keep the log message templates as literals; the operator does not read them
- [x] update the mapper tests to assert against the resource values rather than inline strings
- [x] run tests, must pass before task 5

### Task 5: Wire the settings into startup

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupSequenceTests.cs`

- [x] add an internal method holding the ordered steps: load the settings, apply the culture, run the probe; returning the settings and the startup result
- [x] set the bootstrap UI culture to Russian before the load, and the configured one immediately after it
- [x] call the method from `Program.Main` and pass both results to `App.Run(AppSettings?, Result<StartupData>)`
- [x] assign `RequestedThemeVariant` from the settings above the early return at `App.axaml.cs:73`, so an archive failure still renders on the configured variant and a settings failure renders on the one `App.axaml` declares
- [x] add the `AppSettingsError` arm to `ArchiveFailureMapper.Map`, with its strings in both resource sets from the start
- [x] write tests pinning the order, the mapped strings for every kind, and that a settings failure carries no settings
- [x] run tests, must pass before task 6

### Task 6: Move to Semi.Avalonia and define the palette

**Files:**
- Modify: `SemiPlot/Directory.Packages.props`
- Modify: `SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml`
- Create: `SemiPlot/SemiPlot.UI/Styles/Palette.axaml`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] pin `Semi.Avalonia` 12.0.3 centrally and reference it from `SemiPlot.UI`; drop `Avalonia.Themes.Fluent` from `Directory.Packages.props:13`, `SemiPlot.UI.csproj:23` and `App.axaml:6`
- [x] add `Styles/Palette.axaml` with `Light` and `Dark` theme dictionaries holding the eight Semi tokens and the application's own surface keys, and the component corner-radius keys at the top level, outside the theme dictionaries
- [x] make `App.axaml` an include manifest: the Semi theme plus the palette, `RequestedThemeVariant="Light"` kept as the declared bootstrap variant, no inline style bodies
- [x] run the application on the dark variant and record which further Semi keys need a dark value; the sibling proves the light set only (corrected 2026-09-11: retinting the eight `SemiColor*` tokens is inert. Semi's component keys alias its tokens through `StaticResource` inside Semi's own dictionaries, so the nearest declaration wins and no template ever reads an application-level override. The palette overrides the component keys instead, and the set was established by giving every candidate a marker colour and reading back every brush in a window holding one of each control this tree has. `docs/architecture/ui-theme.md#how-the-retint-reaches-a-control`)
- [x] write a headless test asserting the variant follows the settings and that real Semi controls paint themselves from the palette under both variants, sharing the collection from task 3 and restoring the variant in a `finally`. Resolving a key through `Application.TryGetResource` proves nothing while an override is inert, so the test reads controls
- [x] run tests, must pass before task 7

### Task 7: Replace every colour literal with a key

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Minimap/MinimapView.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/ThemeTests.cs`

- [x] replace the seven `Gray` borders and the chart background in `MainWindow.axaml` with the border and panel keys
- [x] replace the minimap's border, strip, canvas, baseline, both labels and the window highlight with keys, and remove the dark palette the strip carries
- [x] replace the crosshair and hover-readout colours in `TrendChartView.axaml` with keys
- [x] extend the headless test to assert the chart border and the minimap strip resolve to different brushes under the two variants
- [x] run the literal search from the acceptance evidence; it must return nothing
- [x] run tests, must pass before task 8

### Task 8: Paint the plot from the same palette

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/ChartPalette.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TrendChartView.axaml.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Chart/ChartPaletteTests.cs`

- [x] verify the ScottPlot member paths for figure background, data background, grid major line colour and axis and tick colours against 5.1.59 before relying on them (reflection over the shipped assembly: `Plot.FigureBackground` and `Plot.DataBackground` are `BackgroundStyle` fields carrying `Color`; `Plot.Grid` is a `DefaultGrid` with `MajorLineColor`; axis furniture is per `IAxis`: `Label.ForeColor`, `TickLabelStyle.ForeColor`, `MajorTickStyle.Color`, `MinorTickStyle.Color`, `FrameLineStyle.Color`)
- [x] add a type that reads the palette keys and applies them to those four surfaces
- [x] apply it where the plot is bound to the control, and re-apply it when the theme variant changes
- [x] write a test asserting the four surfaces on a real `Plot` carry the palette's colours after application, and that they change with the variant; do not assert that two constants differ
- [x] run tests, must pass before task 9

### Task 9: Verify acceptance criteria

- [x] run every command in the Acceptance Evidence section and record the results (table below; items 2 to 6 pass, items 1 and 7 need an operator)
- [x] verify a configuration directory without `ui/app.yaml` fails as described, with the bench up (skipped as written - the bench stand is down and the outcome is a window; proven instead by `StartupSequenceTests.AMissingSettingsFile_FailsBeforeTheConnectionFileIsRead`, `ArchiveFailureMapperTests.AppSettingsNotFound_SendsTheOperatorToTheFile` and `MainWindowViewModelTests.StartupFailure_WhenSet_MakesThePanelVisibleAndTheChartNull`)
- [x] verify each invalid value names its key and its accepted values (`AppSettingsLoaderTests.AValueOutsideItsSetYieldsTheValueInvalidDiscriminator` asserts `Key` and `AcceptedValues` for `locale: klingon` and `theme: sepia`; `ArchiveFailureMapperTests.AppSettingsValueInvalid_NamesTheKeyAndItsAcceptedValues` asserts both reach the detail line)
- [x] walk the five manual smoke steps (skipped - needs an operator at the screen; the programmatic half of each step is named in the table below)
- [x] run the full suite: `dotnet test SemiPlot.slnx` (839 passed in `SemiPlot.Tests.Unit`, 85 passed in `SemiPlot.Tests.Integration`, 0 failed, 0 skipped)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files (both exit 0; terse over the 23 `.cs` files of `git diff --name-only master...HEAD`)

**Results, 2026-09-11**

| # | Evidence | Result |
| --- | --- | --- |
| 1 | Missing `ui/app.yaml` stops the application | Not verified as written. Docker runs but no bench container is up, and the outcome is a window. The chain is green in the suite: the sequence fails with `AppSettingsProblem.NotFound`, the mapper turns it into the three texts, and the view model hides the chart. The remainder is visual. |
| 2 | `--filter "FullyQualifiedName~AppSettingsLoaderTests"` | Pass. 21 passed, 0 failed, 0 skipped. |
| 3 | `--filter "FullyQualifiedName~StartupSequenceTests"` | Pass. 7 passed, 0 failed, 0 skipped. |
| 4 | `--filter "FullyQualifiedName~ResourcesTests"` | Pass. 6 passed, 0 failed, 0 skipped. |
| 5 | Cold build of `SemiPlot.UI` after `git clean -xdf` | Pass. 0 warnings, 0 errors, no `AVLN2000`. |
| 6 | Colour literal search | Pass. No output, exit 1. |
| 7 | Five manual smoke steps | Not verified. Steps 1 and 2 have `ThemeTests.EverySemiControl_PaintsItselfFromThePalette`, `ThemeTests.TheChartBorderAndTheMinimapStrip_TakeTheirBrushFromTheVariant` and `ChartPaletteTests.Apply_PaintsEverySurfaceWithTheVariantsPalette`; step 3 has `StartupSequenceTests.TheConfiguredLocale_BecomesTheDefaultUiCulture` and `ResourcesTests.TheUiCulture_SelectsTheRussianSet`; step 4 has `ArchiveFailureMapperTests`, which reads every expected string from the resource set; step 5 has `StartupSequenceTests.ASettingsFailure_LeavesTheBootstrapCultureInForce` plus the invalid-value mapper test. What remains is the visual judgement each step asks for: shared palette, readable labels, no stray dark strip. |

### Task 10: Update documentation

**Files:**
- Modify: `docs/architecture/ui-text.md`
- Create: `docs/architecture/ui-theme.md`
- Modify: `docs/architecture/overview.md`
- Modify: `CLAUDE.md`

- [x] rewrite the "One language, no culture wiring" section of `ui-text.md`: two sets, the satellite, what `locale` selects, what stays on `CurrentCulture`, that `Resources.Culture` stays unassigned, and the bootstrap language for a settings failure (now "Two sets, one selector")
- [x] replace the "What stays a literal" entry for the failure window with the log templates and the delta suffixes, which are what remains (three entries: the delta suffixes, the log templates, and the error `Message` values that only reach the log)
- [x] add `ui-theme.md`: the Semi tokens, the application's own surface keys, why the corner-radius keys sit outside the theme dictionaries, how the variant is chosen, and the rule that a colour literal in AXAML is a defect with the gate command that catches it
- [x] add `ui/app.yaml` to the configuration section and the command-line table in `overview.md`, stating that it is required and that converge writes it for the bench
- [x] point `CLAUDE.md` at the new document in one line, without restating it
- [x] move this plan to `docs/plans/completed/` - deferred to the delivery step, which runs after the operator has tested the branch. Filing it under `completed/` now would record a completion that has not happened and break the paths the later review and stats phases read.

## Post-Completion

**Manual verification**

The five smoke steps in the Acceptance Evidence section, run on the bench stand with
`dotnet run --project SemiPlot/SemiPlot.AppHost`.

**External work**

The JetBrains token table and the layout guidance exist as two project-local skills in the sibling
SemiStep repository, `avalonia-visual-style` and `avalonia-form-layout`. Two projects need them now,
so they move to the `confs-cc` marketplace through `marketplace-ops` and both repositories consume
them from there. That release is separate from this plan and touches neither repository's source. The
token values this plan needs are written down above, so the move does not gate it.

**Follow-on**

Per-machine runtime state, meaning window geometry, sidebar width and which panels are open, is
deliberately absent here. It is state rather than configuration, its loss is harmless, and it arrives
with the issues that create it, in a file whose absence is not a failure.

Error, warning and success colours are absent for the same reason: nothing in this tree consumes them
until the status bar and message panel of #80.

**Executed by exec:**

- branch: settings-locale-theme

## Verify it yourself

The theme half of this plan was found inert twice during the run and fixed twice, so the checks
below are written to catch that failure mode rather than to confirm the file exists.

1. **The settings file is required and is read first.** Point the application at a configuration
   directory holding a valid `archive-connection.yaml` and no `ui/app.yaml`:

   ```powershell
   dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir <dir>
   ```

   The window shows the startup failure naming the missing `ui/app.yaml`, in Russian, and no chart.
   On `9ab318f` the same command draws a chart. The ordering is pinned by
   `dotnet test SemiPlot.slnx --filter "FullyQualifiedName~StartupSequenceTests"`: with an invalid
   `ui/app.yaml` and an absent connection file, the failure is the settings error, never a
   `ConnectionFileError`.

2. **A Semi control really takes the JetBrains palette.** This is the check the run got wrong twice,
   because `Application.TryGetResource` reads back the palette's own declaration and passes whether
   or not a control ever sees it.

   ```powershell
   dotnet test SemiPlot.slnx --filter "FullyQualifiedName~ThemeTests"
   ```

   `EverySemiControl_PaintsItselfFromThePalette` and
   `EveryToggleAndScrollSurface_PaintsItselfFromThePalette` show real controls and read their
   resolved brushes. A checked toggle must be `#3574F0`, not Semi's stock `#0064FA` light or
   `#54A9FF` dark.

3. **Both variants on the bench stand.** `dotnet run --project SemiPlot/SemiPlot.AppHost`, once with
   `theme: light` and once with `theme: dark` in `SemiPlot/Artifacts/bench-config/ui/app.yaml`.
   Under dark, nothing stays light: chrome, panels, minimap, and the plot's own figure and data
   background. Stop the archive and restart to see the failure path, where the chart area is painted
   too.

4. **Both languages.** `locale: ru` and `locale: en` in the same file. Captions change; numbers and
   timestamps keep the machine's format either way. An unknown value such as `locale: klingon` names
   the key and its accepted values, in Russian, because no configured language exists at that point.

5. **No colour literal survives.** The gate, which returns nothing and exits 1:

   ```powershell
   git grep -nE '#[0-9A-Fa-f]{6,8}\b|"(Gray|Black|White)"' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'
   ```

   On `9ab318f` it returns eighteen hits across three files.
