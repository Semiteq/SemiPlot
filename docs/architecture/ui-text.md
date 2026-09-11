# UI text

Every string the operator reads lives in `SemiPlot/SemiPlot.UI/Localization/Resources.resx` and its
Russian counterpart `Resources.ru.resx`, the startup failure window included. The C# source is plain
ASCII: `.editorconfig:25` gives `[*.cs]` `charset = utf-8`, CI's `format check` step gates the byte
order mark, and the pre-commit hook and CI's `comment gate` step run `terse` over the characters. So
a glyph with no ASCII form is a resource value: the delta labels (`U+0394` plus `t` and `y`), the
no-value placeholder (`U+2014`) and the window title all sit in the resx. The Russian set is
Cyrillic throughout, and the same rule is what keeps it out of the code.

## Two sets, one selector

`SemiPlot.UI.csproj:7` sets `<NeutralLanguage>en</NeutralLanguage>`, so the neutral set is English
and lives in the assembly itself. `Localization/Resources.ru.resx` carries the same keys and
compiles to a `ru` satellite; the SDK takes the culture from the file name, so the project needs no
`EmbeddedResource` item for it. Only the neutral set carries the `GenerateSource` item, because one
accessor serves both.

The `locale` key of `ui/app.yaml` selects between them. `Startup/StartupSequence.Run` sets
`CultureInfo.DefaultThreadCurrentUICulture` twice: to `ru` before the settings file is read
(`StartupSequence.cs:18`, `:29`), and to the configured language immediately after
(`:38`). Both writes happen before Avalonia is configured, so no window exists on the
wrong language. `ui-theme.md` owns the other half of the same file, the `theme` key.

`Resources.Culture` stays unassigned. The generated accessor reads
`ResourceManager.GetString(key, Culture)`, and a null `Culture` falls through to
`CultureInfo.CurrentUICulture`, which leaves the thread culture the single writer.

A failure reporting the settings file itself is emitted before any `locale` is known, so that window
is read in the bootstrap language, Russian. `MainWindow/ArchiveFailureMapper.cs:38-39` states the
same in one line.

Number and timestamp formatting is a separate question and stays on `CultureInfo.CurrentCulture`:
`Chart/ChartHoverReadout`, `Chart/ChartDeltaCursorReader` and `Legend/TrendLegendRowViewModel` format
what the operator reads on their own machine, and the generated `Format*` methods pass the same null
`Culture` to `string.Format`, so an argument inside a failure detail follows the machine too.

## The generated accessor

`Microsoft.CodeAnalysis.ResxSourceGenerator` produces the accessor. The version is pinned exactly in
`SemiPlot/Directory.Packages.props`; the package has no stable release, and it is the one
dotnet/roslyn builds its own resources with. `SemiPlot.UI.csproj` carries the reference with
`PrivateAssets="all"` and one item:

```xml
<EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true" EmitFormatMethods="true"/>
```

The generated type is `internal static class SemiPlot.UI.Localization.Resources`, one
`public static string` per entry. `EmitFormatMethods` adds, for every value carrying a `{0}`,
an `internal static string Format<Key>(object? p0, ...)` that calls
`string.Format(Culture, value, ...)`. `MainWindow/ArchiveFailureMapper` is written in C# and reads
those: `Resources.FormatFailureArchiveUnreachableDetail(archive)`. A label bound in AXAML keeps
`StringFormat` instead, which is why the property and the `Format*` method both exist for the same
key. `InternalsVisibleTo` carries it into the two test projects and
XamlIl rewrites the same assembly, so the item needs no `Public="true"`. C# reads
`Resources.NoValuePlaceholder`. AXAML declares `xmlns:text="clr-namespace:SemiPlot.UI.Localization"`
on the root element and reads `Content="{x:Static text:Resources.ToolbarAutoscale}"`. `StringFormat`
accepts the same value, so a formatted label keeps its binding rather than growing a view-model
property:

```xml
<TextBlock Text="{Binding ActiveLayer, StringFormat={x:Static text:Resources.ToolbarLayerFormat}}"/>
```

`NeutralLanguage` is not decoration: without it the analyzer baseline fails the build with `CA1824`.

## Why the generator and not MSBuild

The generator runs inside the compiler, so the accessor exists before XamlIl rewrites the assembly
and `{x:Static}` resolves on a cold build. MSBuild's `StronglyTypedFileName` writes the class between
targets instead: measured on 2026-09-09, a cold build fails with
`AVLN2000: Unable to resolve "Resources.ToolbarAutoscale"` and only the second build passes.

## What stays a literal

- The `h`, `m` and `s` suffixes `Chart/ChartDeltaCursorReader.FormatDeltaTime:55-67` glues to its
  numbers. A unit suffix glued to a number is part of the format, not a caption.
- Log message templates, in `Program.cs` and every `ILogger` call. The operator never reads them on
  the screen; the log is read by whoever triages the machine.
- `AppSettingsError.Describe` and the other error messages, the two read names
  `StartupReadTimedOutError.Describe` glues into its line included. They reach the log through
  `Program.LogStartupFailure`, which stays English for whoever triages the machine; what the operator
  reads is the mapper's output, where `ArchiveFailureMapper.NameOf` turns the same `StartupRead` into
  `FailureStartupReadPenCatalogue` or `FailureStartupReadArchiveExtent`.
- The one sentence the runtime wrote itself. `ArchiveFailureMapper.MapThrown` puts
  `Exception.Message` inside a resourced detail, and `MapUnknown` renders an unmapped `IError.Message`
  as the detail. Both are the last arm of the switch, both come from a library rather than from this
  repository, and neither has a Russian form to give. Every mapped arm above them is resourced.

## Adding a key

1. Add the `<data>` entry to `Resources.resx`, value in English, and the same key to
   `Resources.ru.resx` with its Russian value. A key in one set and not the other fails
   `ResourcesTests.TheRussianSet_CarriesEveryKeyTheNeutralSetCarries`, and a Russian value whose
   `{N}` indices differ from the English ones fails
   `ResourcesTests.EveryRussianValue_UsesThePlaceholderIndicesOfTheNeutralOne`.
2. Read it as `Resources.<Key>` in C#, `Resources.Format<Key>(...)` when it carries a placeholder
   and the caller is C#, or `{x:Static text:Resources.<Key>}` in AXAML; the build regenerates the
   accessor.
3. `SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs` enumerates the generated accessors by
   reflection and pins that each resolves to a non-empty string, and reads both sets back through
   `ResourceManager.GetResourceSet(culture, createIfNotExists: true, tryParents: false)`, so an
   ordinary key needs no test of its own. A key whose value carries a glyph or a `{0}` placeholder
   gets one assertion there.

Both resx files are hand-edited. An IDE resx editor rewrites the header, adds the `xsd:schema` block
and a byte order mark; revert that.

A test that writes `CultureInfo.CurrentUICulture` or
`CultureInfo.DefaultThreadCurrentUICulture` joins `ProcessGlobalStateCollection` and restores the
previous value in a `finally`, and so does a test that **reads** a `Resources` value, because a write
landing between the production read and the test's own read is an intermittent failure. The culture is
process-global and the project runs its other classes in parallel.

A glyph with no ASCII form belongs in the resx and nowhere else, never as a `\u` escape in
production code: the escape hides the string from whoever reads the code, and `terse` does not read
`.resx`. The only escapes in the repository are the three in `ResourcesTests`, which spell the code
points they pin while the test file stays ASCII.
