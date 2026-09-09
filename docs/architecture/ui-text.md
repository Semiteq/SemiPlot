# UI text

Every string the operator reads on the trend screen lives in
`SemiPlot/SemiPlot.UI/Localization/Resources.resx`, in one neutral English set. The failure window is
the exception, and "What stays a literal" says why. The C# source is plain ASCII: `.editorconfig:25`
gives `[*.cs]` `charset = utf-8`, CI's `format check` step gates the byte order mark, and the
pre-commit hook and CI's `comment gate` step run `terse` over the characters. So a glyph with no
ASCII form is a resource value: the delta labels (`U+0394` plus `t` and `y`), the no-value
placeholder (`U+2014`) and the window title all sit in the resx.

## One language, no culture wiring

`SemiPlot.UI.csproj` sets `<NeutralLanguage>en</NeutralLanguage>`. There is no second resource set
and no satellite assembly. `CultureInfo.DefaultThreadCurrentUICulture` and `Resources.Culture` are
never assigned, because there is one language to select.

Number and timestamp formatting is a separate question and stays on `CultureInfo.CurrentCulture`:
`Chart/ChartHoverReadout`, `Chart/ChartDeltaCursorReader` and `Legend/TrendLegendRowViewModel` format
what the operator reads on their own machine.

## The generated accessor

`Microsoft.CodeAnalysis.ResxSourceGenerator` produces the accessor. The version is pinned exactly in
`SemiPlot/Directory.Packages.props`; the package has no stable release, and it is the one
dotnet/roslyn builds its own resources with. `SemiPlot.UI.csproj` carries the reference with
`PrivateAssets="all"` and one item:

```xml
<EmbeddedResource Update="Localization/Resources.resx" GenerateSource="true"/>
```

The generated type is `internal static class SemiPlot.UI.Localization.Resources`, one
`public static string` per entry. `InternalsVisibleTo` carries it into the two test projects and
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

- `MainWindow/ArchiveFailureMapper` keeps its titles, details and remedies in code. The failure
  window opens before configuration is read, and the strings interpolate host names and SQLSTATEs.
  `MainWindow/ArchiveFailureView` states the same reason in its summary.
- The `h`, `m` and `s` suffixes `Chart/ChartDeltaCursorReader.FormatDeltaTime` glues to its numbers.
  A unit suffix glued to a number is part of the format, not a caption.
- Log message templates. The operator never reads them on the screen.

## Adding a key

1. Add the `<data>` entry to `Resources.resx`, value in English.
2. Read it as `Resources.<Key>` in C# or `{x:Static text:Resources.<Key>}` in AXAML; the build
   regenerates the accessor.
3. `SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs` enumerates the generated accessors by
   reflection and pins that each resolves to a non-empty string, so an ordinary key needs no test of
   its own. A key whose value carries a glyph or a `{0}` placeholder gets one assertion there.

The resx is hand-edited. An IDE resx editor rewrites the header, adds the `xsd:schema` block and a
byte order mark; revert that.

A glyph with no ASCII form belongs in the resx and nowhere else, never as a `\u` escape in
production code: the escape hides the string from whoever reads the code, and `terse` does not read
`.resx`. The only escapes in the repository are the three in `ResourcesTests`, which spell the code
points they pin while the test file stays ASCII.
