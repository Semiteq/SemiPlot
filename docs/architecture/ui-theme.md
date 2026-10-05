# UI theme

Every colour this tree's own AXAML paints resolves to a key in
`SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, and so does every Semi surface the key table below
names. Two variants are defined, `Light` and `Dark`, both carrying the same key set; the `theme` key
of the `app/` section chooses which one the application runs on. The values are the JetBrains palette, the
same one the sibling SemiStep installation uses. What the palette does not cover keeps Semi's own
variant-aware colour, which the section below states.

`Semi.Avalonia` 12.0.3 replaced `FluentTheme`. `App.axaml` is an include manifest and nothing else:
`<semi:SemiTheme/>` and `<semi:ColorPickerSemiTheme/>` (`Semi.Avalonia.ColorPicker` 12.0.3, the theme
of the pen editor's `ColorPicker` from `Avalonia.Controls.ColorPicker` 12.0.5) in `Application.Styles`
followed by one `StyleInclude` of `Styles/Forms.axaml`, one
`ResourceInclude` pointing at the palette, and `RequestedThemeVariant="Light"` as the declared bootstrap
variant. No style body lives at the application root.

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
| `MenuItemForeground` | Every menu caption at rest: the menu bar's File, Edit, View and Help, their items, the check glyph, and the `TextBox` context menu | `#000000` | `#DFE1E5` |
| `MenuItemPointeroverForeground` | A menu caption while its submenu is open or the pointer is over it; Semi ships this brighter than its own text colour | `#000000` | `#DFE1E5` |
| `TextBoxPlaceholderForeground` | A `TextBox` watermark: the new-group-name field on the pen editor's `Groups` tab | `#818594` | `#6F737A` |
| `TextBlockDisabledForeground` | Disabled `TextBlock` | `#A8ADBD` | `#5A5D63` |
| `TextBoxDisabledForeground` | Disabled `TextBox` | `#A8ADBD` | `#5A5D63` |
| `ButtonDefaultDisabledForeground` | A button whose command cannot execute | `#A8ADBD` | `#5A5D63` |
| `ButtonDefaultPrimaryForeground` | Every navigation-bar button's caption, and an unchecked `ToggleButton` | `#3574F0` | `#3574F0` |
| `ButtonSolidPrimaryBackground` | A checked `ToggleButton`, which the navigation bar's Sticky toggle is on the first frame | `#3574F0` | `#3574F0` |
| `ButtonSolidPrimaryBorderBrush` | The same control's border | `#3574F0` | `#3574F0` |
| `CheckBoxDefaultBorderBrush` | The legend's unchecked box, one per pen and one per group header | `#EBECF0` | `#393B40` |
| `ScrollBarThumbForeground` | The legend's scrollbar thumb | `#A8ADBD` | `#5A5D63` |
| `CheckBoxCheckedDefaultBackground` | The legend's checked box, and a group header's indeterminate box | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedDefaultBorderBrush` | The legend's checked box, and a group header's indeterminate box | `#3574F0` | `#3574F0` |
| `CheckBoxPointeroverBorderBrush` | The legend's hovered box | `#3574F0` | `#3574F0` |
| `TextBoxFocusBorderBrush` | The focused field of the axis scale panel | `#3574F0` | `#3574F0` |
| `WindowDefaultBackground` | The window ground | `#FFFFFF` | `#1E1F22` |
| `MenuFlyoutBackground` | Every open submenu, the `TextBox` context menu included | `#F7F8FA` | `#2B2D30` |
| `MenuFlyoutBorderBrush` | The same submenu's border | `#EBECF0` | `#393B40` |
| `MenuItemSeparatorBackground` | The `Separator`s in the View menu | `#EBECF0` | `#393B40` |
| `TabItemLineHeaderForeground` | A pen editor tab header at rest | `#818594` | `#6F737A` |
| `TabItemLineHeaderPointeroverForeground`, `TabItemLineHeaderSelectedForeground` | A hovered tab header, and the selected one | `#000000` | `#DFE1E5` |
| `TabItemLinePipeSelectedBackground` | The selected tab's underline | `#3574F0` | `#3574F0` |
| `TabControlSeparatorBorderBrush` | The line under the tab headers | `#EBECF0` | `#393B40` |
| `ListBoxItemPointeroverForeground`, `ListBoxItemPressedForeground`, `ListBoxItemSelectedForeground` | A hovered, pressed or selected row of the pen table and the group list | `#000000` | `#DFE1E5` |
| `ListBoxItemSelectedBackground`, `ListBoxItemSelectedPointeroverBackground` | The selected row, hovered or not; the same fill as the sidebar's active row | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `ComboBoxItemForeground`, `ComboBoxItemFocusForeground`, `ComboBoxItemPointeroverForeground`, `ComboBoxItemSelectedForeground` | An item of the open line-style list at rest, focused, hovered and selected | `#000000` | `#DFE1E5` |
| `ComboBoxIconPointeroverForeground` | The line-style box's arrow under the pointer | `#000000` | `#DFE1E5` |
| `ComboBoxDisabledForeground` | The line-style box's value while no pen is selected | `#A8ADBD` | `#5A5D63` |
| `ComboBoxSelectorPressedBorderBrush` | The line-style box's border while its list is open | `#3574F0` | `#3574F0` |
| `ComboBoxItemSelectedBackground` | The selected item of the open line-style list | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `ComboBoxPopupBackground` | The open line-style list | `#F7F8FA` | `#2B2D30` |
| `ComboBoxPopupBorderBrush` | The same list's border | `#EBECF0` | `#393B40` |
| `CheckBoxDefaultDisabledBorderBrush` | The form's "on start" box while no pen is selected | `#EBECF0` | `#393B40` |
| `FlyoutForeground` | Text in the colour picker's flyout | `#000000` | `#DFE1E5` |
| `FlyoutBackground` | The colour picker's flyout | `#F7F8FA` | `#2B2D30` |
| `FlyoutBorderBrush` | The same flyout's border | `#EBECF0` | `#393B40` |
| `ColorSpectrumBorderBrush` | The picker spectrum's edge | `#EBECF0` | `#393B40` |
| `ColorViewTabItemSelectedForeground` | The icon of the picker's selected tab | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonCheckedBackground` | The chosen colour model on the picker's components tab | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonForeground` | The other colour model's caption | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonBackground` | The other colour model's ground | `#FFFFFF` | `#1E1F22` |
| `CheckBoxCheckedPointeroverBackground`, `CheckBoxCheckedPointeroverBorderBrush` | A hovered checked box: a membership box, the form's "on start" box, a legend box | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedPressedBackground`, `CheckBoxCheckedPressedBorderBrush`, `CheckBoxPressedBorderBrush` | A pressed box, checked or not | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedDisabledBackground`, `CheckBoxCheckedDisabledBorderBrush` | A checked box that is disabled: a membership box while its write runs | `#A8ADBD` | `#5A5D63` |

The accent is the one value deliberately equal across the variants: it does not change with the
ground it sits on.

Every other Semi surface keeps Semi's stock colour, and those follow the variant on their own.
Measured on 2026-09-11 off real controls in a headless window: `Button.Background`, an unchecked
`ToggleButton.Background` and `TextBox.Background` are Semi's translucent overlay, `#2E3238` at 0.05
under `Light` and `#FFFFFF` at 0.12 under `Dark`; the `PART_ClearButton` and `PART_RevealButton`
glyph foregrounds of a `TextBox` are `#1C1F23` at 0.62 and `#F9F9F9` at 0.6. They are correct as they
stand; the palette overrides a key only where Semi's stock value does not match the JetBrains one.
The pen editor's controls keep Semi's value in the same places, measured on 2026-09-28: the
translucent hover and press overlays of tabs, list rows, combo items and checkboxes, the picker's icons
at 0.62 and 0.35, the white check glyphs, and the black and white contrast edges of the picker's
sliders.
Adding a control means running the same marker probe for it, not copying a key list.

`ThemeTests.EverySemiControl_PaintsItselfFromThePalette` shows a real `Button`, `TextBlock`, `TextBox`
and checked `CheckBox` under both variants and reads their resolved brushes back;
`ThemeTests.EveryToggleAndScrollSurface_PaintsItselfFromThePalette` covers the rest of what the window
holds, a `ToggleButton` in both check states, an unchecked and an indeterminate `CheckBox` and a
`ScrollViewer` thumb;
`ThemeTests.EveryMenuSurface_PaintsItselfFromThePalette` opens a real `Menu` and reads the caption at
rest and open, a leaf, a checked item's glyph, a `Separator`, the flyout chrome and an `ItemsControl`
row. The `Ellipse` of a message-panel row carries no Semi key: its fill is one of this tree's own
severity brushes, gated by `MessagePanelViewTests`.
The pen editor's controls have four tests of their own: `EveryTabAndListSurface_PaintsItselfFromThePalette`
(a `TabControl` and a `ListBox` at rest, hovered, pressed and selected),
`EveryComboBoxSurface_PaintsItselfFromThePalette` (the line-style list open, hovered and disabled, and
a disabled `CheckBox`), `EveryColourPickerSurface_PaintsItselfFromThePalette` (the picker's flyout on
the spectrum and on the components tab) and
`EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette` (a box checked and hovered,
pressed checked and unchecked, and checked while disabled).
`APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` reads the 0.25 opacity of the four translucent
keys back under both variants.
Resolving a key through `Application.TryGetResource` cannot fail while an override is inert, so a test
written that way proves nothing about the retint. A control the tree gains is added to one of those
tests in every state it reaches.

## Semi's own control strings

`SemiTheme` keys its built-in strings by specific culture and falls through to `zh-CN` for a neutral
one, so `App.ApplyAppearance` calls `SemiTheme.OverrideLocaleResources` with
`App.SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale)`, outside the
`settings is not null` guard that the variant sits inside (`App.axaml.cs:133-141`): a settings failure
has no configured locale and reads its window in the bootstrap one.
`AppConfigurationTests.ASettingsFailure_StillHandsSemiTheBootstrapLocale` calls `App.ConfigureFailed` with
null settings and reads `STRING_MENU_COPY` back off `Application.Resources`, so moving the call
inside the guard turns it red. The surfaces this tree shows are the window's own `Menu` and the
context menu of the axis scale panel's fields.

## Corner radius sits outside the theme dictionaries

`Styles/Palette.axaml` overrides five keys at 4 px: `ButtonCornerRadius`,
`TextBoxDefaultCornerRadius`, `CheckBoxBoxCornerRadius`, `ComboBoxSelectorCornerRadius` and
`ColorPickerCornerRadius`. They sit at the top level of the dictionary, not inside
`ResourceDictionary.ThemeDictionaries`: a radius does not change with the variant. The same
`StaticResource` alias as above is why the component keys are overridden and `SemiBorderRadiusSmall`
is not.
`ThemeTests.EveryCornerRadius_ComesFromThePalette` reads all five back off a real `Button`, `TextBox`,
`CheckBox`, `ComboBox` (its `Background` border) and `ColorPicker` (its `PART_Background` border) under
both variants. Semi paints 3 without the overrides, so the control is what proves them live.

A control the tree gains brings its own key, with two exceptions that carry none. A `ListBoxItem` and a
line-style `TabItem` paint 0 px in Semi, square as the surfaces around them. The combo box's drop-down
and the picker's flyout keep Semi's 6 px, as the menu flyout does.

## The application's own surfaces

Semi owns the controls; these twelve keys are ours, and each exists in both variants.

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Navigation bar, legend panel, message panel, status bar, startup-failure window's message panel, minimap frame, chart hover readout | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Chart area, minimap strip canvas, the sidebar's resize handle | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | Every separator in the three views | `#EBECF0` | `#393B40` |
| `AppSubtleLineBrush` | Plot grid, the line above every sidebar group header but the first, the status bar's separator after the connection indicator, the navigation bar's group separator | `#EBECF0` | `#393B40` |
| `AppSecondaryForegroundBrush` | Minimap end labels, hover line and hover time, chart crosshair, plot axis furniture, the sidebar group header caption and a row's unit | `#818594` | `#6F737A` |
| `AppAccentBrush` | Minimap window highlight border, the active sidebar row's left bar | `#3574F0` | `#3574F0` |
| `AppAccentFillBrush` | Minimap window highlight fill, the active sidebar row's background | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `AppSeverityErrorBrush` | The message panel's dot on an `Error` entry; a form's invalid field border and its message line | `#DB3B4B` | `#E55765` |
| `AppSeverityWarningBrush` | The same dot on a `Warning` entry | `#E3AE4D` | `#F2C55C` |
| `AppSeverityInfoBrush` | The same dot on an `Info` entry | `#3574F0` | `#3574F0` |
| `AppConnectionOkBrush` | The status bar's connection indicator, `connection-ok` | `#208A3C` | `#5FAD65` |
| `AppConnectionFaultBrush` | The same indicator, `connection-fault` | `#DB3B4B` | `#E55765` |

Pen colours are not theme keys. They come from the archive with the pen and stay per pen under both
variants. The pen editor's colour swatch is the pen's own colour through `LegendConverters.HexToBrush`,
the converter the sidebar dot and the minimap band use, and a pen with no colour shows a transparent
swatch.

## A form never resizes on validation

Every form in this application follows one layout rule, the settings dialog first. Validation changes
colour and text, never size:

- A dialog has a fixed `Width` and `SizeToContent="Height"`. Its height comes from rows that exist in
  every state; no row appears or disappears with validation. A window the operator may resize keeps
  the same rule for every part but its lists (A resizable window keeps its fixed parts, below).
- An invalid field is marked by its border alone. The field binds `Classes.invalid` to its `Is*Valid`
  flag, and `Styles/Forms.axaml` paints that border with `AppSeverityErrorBrush`.
- One message line sits left of the buttons. It is a `TextBlock.form-message`: two lines of 20 px
  reserved, wrapped, trimmed past the second. It shows the rule the first invalid field breaks, in form
  order, and is empty while every field is valid. A notice that is not an error, such as the settings
  dialog's restart notice with its Restart now button in the same cell, shares the same line and gives
  way to an error, the button with it.
- Each message is one short line in both languages, `ui-text.md#the-settings-windows-text`.

The invalid style targets the template part, not the control. Semi paints a `TextBox` border on
`Border#PART_ContentPresenterBorder` from its own state styles: `Transparent` at rest and
`TextBoxFocusBorderBrush` on focus. A setter on `TextBox.BorderBrush` loses to both. A `NumericUpDown`
draws its border on the same part of its inner `TextBox#PART_TextBox`, so its selector nests two
`/template/` steps. `Forms.axaml` follows `SemiTheme` in `Application.Styles`, so at equal priority it
wins. `SettingsViewTests.AnInvalidField_PaintsItsBorderWithTheErrorBrushFocusedOrNot` reads the
rendered `BorderBrush` of both parts, focused and at rest, under both variants; it fails without the
include.

The settings dialog is 640 px wide, with a 20 px margin and 10 px between rows. Measured on 2026-09-25
with Skia and HarfBuzz, every field valid, `SizeToContent="WidthAndHeight"`: 245 px in English and 278 px in
Russian, so the labels and fields fit either way, and at 440 px the dialog held its size in both languages
with each field invalid in turn. The 528 px width that the 1.2 times rule gave is superseded by the
restart-notice measurement below, which set 640 px; a message that still does not fit wraps into the
second reserved line. A longer label, button or message is measured the same way before it ships.
`SettingsViewTests.TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` gates the rule: the
dialog's and the save button's `Bounds` are equal before and after an invalid host.

The restart notice shares its cell with the Restart now button, which narrows the notice by the button and a
12 px margin. Measured on 2026-09-30 with Skia and HarfBuzz, the real theme, at 14 px with 20 px lines:

| | English | Russian |
| --- | --- | --- |
| Restart now button | 111 px | 135 px |
| Save and Close buttons | 59 and 64 px | 104 and 88 px |
| Notice text on one line | 224 px | 325 px |
| Notice cell beside the button at 528 px wide | 222 px, two lines | 129 px, trimmed past the second line |
| Notice cell beside the button at 640 px wide | 334 px, one line | 241 px, two lines, the first 235 px wide |
| Message cell when the notice is hidden, at 640 px | 457 px | 388 px |

At 528 px the Russian notice did not fit its two lines, so the dialog is 640 px wide: the cell holds the
Russian notice in two lines with 482 px of line capacity for its 325 px. The headless tests measure with the
test font, which is about twice as wide, so no test asserts the trimming; `SettingsViewTests` gates the
button's and the notice's effect on the dialog's and the Save button's `Bounds` instead. A longer notice or
label is measured again in both languages before it ships.

`SettingsRestartNoticeThemeApplied`, the notice after a save that also changed the theme, was measured the
same way on 2026-10-01 in the realised 640 px dialog, with the notice showing beside the button: 304 px on one
line in English, in a 334 px cell, and 420 px in Russian, which wraps in its 241 x 40 px cell into
"Сохранено. Тема применена," at 208.6 px and "остальное после перезапуска." at 210.5 px, two lines with
nothing trimmed.

### The axis scale panel

`Chart/AxisScalePanel` is a `Flyout` form under the same rule. The panel is 360 px wide, with rows that exist
in every state: the pen name and unit, two `TextBox` rows, the reserved message line (`form-message`, 40 px) and
two rows of buttons, Autoscale and Restore initial scale side by side, Apply across both. An invalid or empty
field takes the `invalid` border and Apply is disabled, so nothing appears or disappears.

Measured with Skia and HarfBuzz, in the real theme, with the pen "Damper valve 01" in mTorr:

| | English | Russian |
| --- | --- | --- |
| Panel | 360 x 235 px | 360 x 235 px |
| Flyout presenter | 386 x 261 px | 386 x 261 px |
| Widest button text, Restore initial scale | 127.9 px in a 176 px button | 137.3 px in a 176 px button |
| Longest message text | 292.7 px | 306.9 px |
| Panel and presenter size with the pair inverted | unchanged | unchanged |
| Panel and presenter size with a field empty | unchanged | unchanged |
| Panel and presenter size with a field unreadable | unchanged | unchanged |
| Submenu header text, "Damper valve 01" | 181.3 px, in a 270 px item | 215.1 px, in a 304 px item |

The presenter is the 360 px panel plus 8 px padding and a 1 px border on each side, a 378 x 253 px box, plus a
4 px margin on each side that the theme reserves for the shadow. The 360 px width holds the longest message on
one line in both languages. Each button is half the width, so the widest label keeps 176 - 24 - 137.3 = 14.7 px
in Russian and 176 - 24 - 127.9 = 24.1 px in English. A longer label or message is measured the same way before
it ships. `AxisScalePanelViewTests.AnInvertedPair_ShowsTheMessageKeepsThePanelsSizeAndEnterWritesNothing` and
`AnEmptyField_ShowsTheMessageKeepsThePanelsSizeAndEnterWritesNothing` gate the size under the test font, in both
cultures.

### A resizable window keeps its fixed parts

The pen editor is the one form the operator may resize, because its table grows with the catalogue.
`PenEditor/PenEditorWindow.axaml` opens at a fixed `Width` and `Height`, 1180 x 720 px, with
`MinWidth="1100"`, `SizeToContent="Manual"` and `CanResize="True"`: the operator may widen it, and nothing
inside it sizes the window. The smallest screen the editor opens on is 1280 px wide; the 720 px height exceeds
the working area of a 768 px screen. The rule above holds for every part but the two lists:

- Resizing grows the pen table and the membership list only. The form panel
  (240 px high), the `Groups` tab's side panel (320 px wide), its confirmation row (32 px high) and the
  bottom bar keep fixed sizes, made of rows that
  exist in every state.
- A pen row, a group row and a membership row are each `RowHeight`, 28 px, and all three lists
  virtualise.
- The pen table's header row and its row template read one column list, the window's
  `PenTableColumns` resource: 72, 112, 68, 220, 96, 96, 104, 104 and 104 px, and the groups column takes
  the rest. The resource is `x:Shared="False"`, so every grid gets its own copy. The membership row's id
  column reads the same `IdColumnWidth`.
- The bottom bar, on both tabs, holds the added count and the refresh button in a right-aligned `Auto,Auto`
  grid, 12 px apart, and carries no notice. Each tab has its own reserved `form-message` line.
- An invalid field is marked by the `invalid` class alone. The line-style combo box and the "on start"
  checkbox refuse no value, and `Forms.axaml` styles `invalid` on `TextBox` and `NumericUpDown` only, so
  a failed write of either shows on the message line and in the panel only.
- A header button wraps its text (`TextWrapping="Wrap"`) and every header button is 44 px high, so the
  two-line scale headers do not change the row's height between cultures. The 44 px is chosen, not measured.

Measured on 2026-09-30 with Skia and HarfBuzz, the composite Inter font at 14 px, every field valid. The fixed
columns total 976 px and the window margin is 24 px, so no fixed column clips from 1000 px up. `MinWidth` 1100
leaves the groups column 100 px, and it gets 180 px at the opening width. The two `PenEditorColumnScale*`
headers measure 106.9 and 109.4 px in English and 145.2 and 147.3 px in Russian on one line, and wrap to two
lines of at most 81.4 px in English and 79.1 px in Russian, inside the 90 px a 104 px column leaves after its
padding and border. The widest other Russian header is `PenEditorColumnOnStart` at 86.1 px, and the widest
line-style label is `PenLineStyleStepped` at 69 px. Each fixed column is at least 1.1 times its widest header
line (the wrapped line for the two `PenEditorColumnScale*` headers) or fixed-vocabulary cell, plus the 12 px
cell margin. At 1180 x 720 px the table shows 11 rows. The form's natural size is 703 x 195 px in English and
778 x 195 px in Russian, inside a 1156 x 232 px area. The longest form message is `PenFormMaskInvalid` at
476.6 px in English and 480.3 px in Russian on a 1156 px line; `PenFormScaleInverted` is 371.3 px in English
and 434.0 px in Russian. Measured on 2026-09-29, with a three-digit added count the bottom bar needs 472 px in
English, 210 px for the count, 12 px between and 250 px for "Refresh pen list", and 640 px in Russian, 294 +
12 + 334 px for «Обновить список перьев»; both fit the 1100 px minimum width.
The side panel's 320 px and the confirmation row's 32 px are chosen, not measured; the confirmation
text spans the tab's width and trims only past it.
`PenEditorViewTests.AnUnusableMask_WritesNothingRevertsMarksAndSaysWhyWithoutResizing` gates the size
half of the rule: the window's and the form panel's `Bounds` are equal before and after the mask turns
invalid.
`PenEditorViewTests.TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthClipsNoFixedColumn` gates the width,
the 100 px groups column at `MinWidth` and equal header button heights.

## How the variant reaches the application

`Startup/AppSettingsLoader` reads `theme` into `AppSettings.Theme`
(`Startup/AppSettings.cs:14-19`), and `App.ApplyAppearance`, which `App.ConfigureStarted` and
`App.ConfigureFailed` call inside `AfterSetup`, assigns `RequestedThemeVariant` from it before either path
builds a window. So an archive failure still renders on the configured variant. `settings` is null only when the settings load
itself failed; that window renders on the `Light` variant `App.axaml:5` declares, which is also
what the headless test builders see, since they construct `App` directly and never call `App.RunStarted`.
After the start, `App.ApplyTheme` is the variant's one writer: it applies a theme any process saved
(`overview.md#the-live-theme`).

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
model is bound, and a view with no view model has no chart view model, where an unpainted plot
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
`TrendChartViewTests.AViewWithNoViewModel_StillPaintsTheChartAreaFromThePalette` covers a view
with no view model.

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
