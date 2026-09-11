# UI theme

Every colour this tree's own AXAML paints resolves to a key in
`SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, and so does every Semi surface the key table below
names. Two variants are defined, `Light` and `Dark`, both carrying the same key set; the `theme` key
of `ui/app.yaml` chooses which one the application runs on. The values are the JetBrains palette, the
same one the sibling SemiStep installation uses. What the palette does not cover keeps Semi's own
variant-aware colour, which the section below states.

`Semi.Avalonia` 12.0.3 replaced `FluentTheme`. `App.axaml` is an include manifest and nothing else:
`<semi:SemiTheme/>` in `Application.Styles`, one `ResourceInclude` pointing at the palette, and
`RequestedThemeVariant="Light"` as the declared bootstrap variant. No style body lives at the
application root.

## How the retint reaches a control

Semi's own semantic tokens (`SemiColorPrimary`, `SemiColorText0`, ...) cannot be retinted from the
application. Semi defines each of them inside its own dictionary and its component keys alias them
through `StaticResource`, which resolves against the nearest declaration at load time, so Semi's own
value always wins and an override in `Application.Resources` is never read by a template. Measured on
2026-09-11 with `SemiColorPrimary` set to `#3574F0`: `Application.TryGetResource` returned the
override, while `ButtonDefaultPrimaryForeground` stayed on Semi's `#0064FA` and a real `Button`
painted itself with it.

What a template does read is the component key, through `DynamicResource`, so overriding the
component key works and that is what `Styles/Palette.axaml` does. A key belongs in the palette when a
control this tree contains reads it; the set below was established by giving every candidate key a
marker colour and reading back every brush in the visual tree of a window holding one of each
control, in each state the tree can reach.

| Key | Control and state | Light | Dark |
| --- | --- | --- | --- |
| `TextBlockDefaultForeground` | `TextBlock` | `#000000` | `#DFE1E5` |
| `TextBoxForeground` | `TextBox` | `#000000` | `#DFE1E5` |
| `CheckBoxForeground` | `CheckBox` | `#000000` | `#DFE1E5` |
| `WindowDefaultForeground` | `Window`, and every control inheriting from it | `#000000` | `#DFE1E5` |
| `MenuItemForeground` | The `TextBox` context menu | `#000000` | `#DFE1E5` |
| `TextBoxPlaceholderForeground` | The toolbar's two placeholders | `#818594` | `#6F737A` |
| `TextBlockDisabledForeground` | Disabled `TextBlock` | `#A8ADBD` | `#5A5D63` |
| `TextBoxDisabledForeground` | Disabled `TextBox` | `#A8ADBD` | `#5A5D63` |
| `ButtonDefaultDisabledForeground` | A toolbar button whose command cannot execute | `#A8ADBD` | `#5A5D63` |
| `ButtonDefaultPrimaryForeground` | Every toolbar button's caption, and an unchecked `ToggleButton` | `#3574F0` | `#3574F0` |
| `ButtonSolidPrimaryBackground` | A checked `ToggleButton`, which the toolbar's Sticky toggle is on the first frame | `#3574F0` | `#3574F0` |
| `ButtonSolidPrimaryBorderBrush` | The same control's border | `#3574F0` | `#3574F0` |
| `CheckBoxDefaultBorderBrush` | The legend's unchecked box, one per pen | `#EBECF0` | `#393B40` |
| `ScrollBarThumbForeground` | The legend's scrollbar thumb | `#A8ADBD` | `#5A5D63` |
| `CheckBoxCheckedDefaultBackground` | The legend's checked box | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedDefaultBorderBrush` | The legend's checked box | `#3574F0` | `#3574F0` |
| `CheckBoxPointeroverBorderBrush` | The legend's hovered box | `#3574F0` | `#3574F0` |
| `TextBoxFocusBorderBrush` | The focused axis-bound editor | `#3574F0` | `#3574F0` |
| `WindowDefaultBackground` | The window ground | `#FFFFFF` | `#1E1F22` |
| `MenuFlyoutBackground` | The `TextBox` context menu | `#F7F8FA` | `#2B2D30` |
| `MenuFlyoutBorderBrush` | The `TextBox` context menu | `#EBECF0` | `#393B40` |

The accent is the one value deliberately equal across the variants: it does not change with the
ground it sits on.

Every other Semi surface keeps Semi's stock colour, and those follow the variant on their own.
Measured on 2026-09-11 off real controls in a headless window: `Button.Background`, an unchecked
`ToggleButton.Background` and `TextBox.Background` are Semi's translucent overlay, `#2E3238` at 0.05
under `Light` and `#FFFFFF` at 0.12 under `Dark`; the `PART_ClearButton` and `PART_RevealButton`
glyph foregrounds of a `TextBox` are `#1C1F23` at 0.62 and `#F9F9F9` at 0.6. They are correct as they
stand; the palette overrides a key only where Semi's stock value does not match the JetBrains one.
Adding a control means running the same marker probe for it, not copying a key list.

`ThemeTests.EverySemiControl_PaintsItselfFromThePalette` shows a real `Button`, `TextBlock`, `TextBox`
and checked `CheckBox` under both variants and reads their resolved brushes back;
`ThemeTests.EveryToggleAndScrollSurface_PaintsItselfFromThePalette` covers the rest of what the window
holds, a `ToggleButton` in both check states, an unchecked `CheckBox` and a `ScrollViewer` thumb.
Resolving a key through `Application.TryGetResource` cannot fail while an override is inert, so a test
written that way proves nothing about the retint. A control the tree gains is added to one of those two
tests in every state it reaches.

## Semi's own control strings

`SemiTheme` keys its built-in strings by specific culture and falls through to `zh-CN` for a neutral
one, so `App.Configure` calls `SemiTheme.OverrideLocaleResources` with
`App.SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale)`, outside the
`settings is not null` guard that the variant sits inside (`App.axaml.cs:91-92`): a settings failure
has no configured locale and reads its window in the bootstrap one.
`AppConfigurationTests.ASettingsFailure_StillHandsSemiTheBootstrapLocale` calls `App.Configure` with
null settings and reads `STRING_MENU_COPY` back off `Application.Resources`, so moving the call
inside the guard turns it red. The surface this tree shows is the context menu of the axis-bound
editor's `TextBox`.

## Corner radius sits outside the theme dictionaries

`Styles/Palette.axaml` overrides `ButtonCornerRadius`, `TextBoxDefaultCornerRadius` and
`CheckBoxBoxCornerRadius` at the top level of the dictionary, not inside
`ResourceDictionary.ThemeDictionaries`: a radius does not change with the variant. The same
`StaticResource` alias as above is why the component keys are overridden and `SemiBorderRadiusSmall`
is not.
`ThemeTests.EveryCornerRadius_ComesFromThePalette` reads all three back off a real `Button`, `TextBox`
and `CheckBox` under both variants. Semi paints 3 without the overrides, so the control is what proves
them live.

The three keys are the controls this tree has. A control the tree gains brings its own key.

## The application's own surfaces

Semi owns the controls; these seven keys are ours, and each exists in both variants.

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Toolbar, legend panel, message panel, connection banner, status bar, startup failure panel, minimap frame, chart hover readout | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Chart area, minimap strip canvas | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | Every separator in the three views | `#EBECF0` | `#393B40` |
| `AppSubtleLineBrush` | Minimap baseline, plot grid | `#EBECF0` | `#393B40` |
| `AppSecondaryForegroundBrush` | Minimap extent labels, chart crosshair, plot axis furniture | `#818594` | `#6F737A` |
| `AppAccentBrush` | Minimap window highlight border | `#3574F0` | `#3574F0` |
| `AppAccentFillBrush` | Minimap window highlight fill | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |

Pen colours are not theme keys. They come from the archive with the pen and stay per pen under both
variants.

## How the variant reaches the application

`Startup/AppSettingsLoader` reads `theme` into `AppSettings.Theme`
(`Startup/AppSettings.cs:14-19`), and `App.Configure`, which `App.Run` hands to `AfterSetup`,
assigns `RequestedThemeVariant` from it at `App.axaml.cs:84-87`, above the failure return. So an
archive failure still renders on the configured variant. `settings` is null only when the settings load
itself failed; that window renders on the `Light` variant `App.axaml:5` declares, which is also
what the headless test builders see, since they construct `App` directly and never call `App.Run`.

`SettingsVocabulary`, in the same file as the enums, holds every spelling of a settings value: the
yaml token the loader matches, the UI culture, Semi's specific culture and the `ThemeVariant`. A
further language or theme is one row there, and `App.VariantFor`, `App.SemiLocaleFor` and
`StartupSequence.CultureFor` read it rather than switching on the enum themselves.

`ui-text.md` owns the other half of the same file, the `locale` key.

## The plot

ScottPlot paints four surfaces from its own defaults, and `Chart/ChartPalette.cs` moves all four
under the palette keys:

| ScottPlot surface | Key |
| --- | --- |
| `Plot.FigureBackground.Color` | `AppPanelBackgroundBrush` |
| `Plot.DataBackground.Color` | `AppContentBackgroundBrush` |
| `Plot.Grid.MajorLineColor` | `AppSubtleLineBrush` |
| Per axis: `Label.ForeColor`, `TickLabelStyle.ForeColor`, `MajorTickStyle.Color`, `MinorTickStyle.Color`, `FrameLineStyle.Color` | `AppSecondaryForegroundBrush` |

The four keys are constants in a dictionary the same assembly ships, so `Apply` resolves them
unconditionally and throws when one does not resolve to a brush; a palette key that stops resolving
is a defect in the palette, not a state to paint around. All four brushes are opaque, and `Apply`
reads the ARGB value alone: `AppAccentFillBrush` is the one key declaring `Opacity`, and no ScottPlot
surface takes it.

`ChartPalette.Apply` takes `AvaPlot.Plot`, not the view model's: the two are one instance once a view
model is bound, and on the startup-failure path there is no chart view model, where an unpainted plot
would be a white rectangle inside a dark window.

`Chart/TrendChartView.axaml.cs` calls it from three places, and each is a different reason. `OnLoaded`
paints the control's plot and refreshes it; `ActualThemeVariantChanged` repaints and refreshes it; the
`ScalesRevision` subscription paints an axis the scale model created after bind time. That third path
runs once per pointer move during a pan, so it compares `TrendChartViewModel.AxisCount` with the count
of the last paint and does nothing when the axis set did not grow, and it never calls
`PlotControl.Refresh()` because every site that bumps `ScalesRevision` already follows with
`RequestRedraw()`, which the view samples at the frame budget.

`ChartPaletteTests` asserts the four surfaces on a real `Plot` against the resolved palette under both
variants, and `TrendChartViewTests.ALoadedView_RepaintsThePlotWhenTheApplicationVariantChanges` with
its unloaded counterpart covers the wiring: the subscription, and that `OnUnloaded` drops it.
`TrendChartViewTests.AViewWithNoViewModel_StillPaintsTheChartAreaFromThePalette` covers the
startup-failure path.

## A colour literal in AXAML is a defect

The palette is the only place a colour is written. A literal on a control cannot follow the variant,
so it survives the switch as a stray light or dark patch. This command finds one, and returns
nothing today:

```powershell
git grep -nE '#[0-9A-Fa-f]{6,8}\b|"(Gray|Black|White)"' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'
```

No CI job runs it; the suite catches the same thing from the other side.
`ThemeTests.TheChartBorderAndTheMinimapStrip_TakeTheirBrushFromTheVariant` shows the main window and
reads both surfaces back under each variant, and a surface left on a literal resolves to one brush
under both.

Consumers read their keys with `DynamicResource`, not `StaticResource`: a `StaticResource` alias
resolves once and stops following the variant, which is the same mechanism that makes the
corner-radius keys inert inside a theme dictionary.
