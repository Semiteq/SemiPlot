# UI theme

Every colour this tree's own AXAML paints resolves to a key in
`SemiPlot/SemiPlot.UI/Styles/Palette.axaml`, and so does every Semi surface the key table below
names. Two variants are defined, `Light` and `Dark`, both carrying the same key set; the `theme` key
of the `app/` section chooses which one the application runs on. The values are the JetBrains palette, the
same one the sibling SemiStep installation uses. What the palette does not cover keeps Semi's own
variant-aware colour, which the section below states.

`Semi.Avalonia` 12.0.3 replaced `FluentTheme`. `App.axaml` is an include manifest and nothing else:
`<semi:SemiTheme/>` and `<semi:ColorPickerSemiTheme/>` (`Semi.Avalonia.ColorPicker` 12.0.3, the theme
of the pen editor's `ColorPicker` from `Avalonia.Controls.ColorPicker` 12.0.5) and
`<semi:DataGridSemiTheme/>` (`Semi.Avalonia.DataGrid` 12.0.0, the theme of the pen editor's `DataGrid` from
`Avalonia.Controls.DataGrid` 12.0.1, which depends on Avalonia 12.0.5) in `Application.Styles`
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
| `TextBoxPlaceholderForeground` | Every `TextBox` watermark: the pen editor's filter box `PenListFilter`, its colour box, the new-group-name field of its groups pane, and the chart's marker label editor | `#818594` | `#6F737A` |
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
| `ComboBoxItemForeground`, `ComboBoxItemFocusForeground`, `ComboBoxItemPointeroverForeground`, `ComboBoxItemSelectedForeground` | An item of the open line-style list at rest, focused, hovered and selected | `#000000` | `#DFE1E5` |
| `ComboBoxIconPointeroverForeground` | The line-style box's arrow under the pointer | `#000000` | `#DFE1E5` |
| `ComboBoxDisabledForeground` | The line-style box's value while a refresh runs | `#A8ADBD` | `#5A5D63` |
| `ComboBoxSelectorPressedBorderBrush` | The line-style box's border while its list is open | `#3574F0` | `#3574F0` |
| `ComboBoxItemSelectedBackground` | The selected item of the open line-style list | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `ComboBoxPopupBackground` | The open line-style list | `#F7F8FA` | `#2B2D30` |
| `ComboBoxPopupBorderBrush` | The same list's border | `#EBECF0` | `#393B40` |
| `CheckBoxDefaultDisabledBorderBrush` | The form's unchecked visibility and scale type boxes while a refresh runs | `#EBECF0` | `#393B40` |
| `FlyoutForeground` | Text in the colour picker's flyout | `#000000` | `#DFE1E5` |
| `FlyoutBackground` | The colour picker's flyout | `#F7F8FA` | `#2B2D30` |
| `FlyoutBorderBrush` | The same flyout's border | `#EBECF0` | `#393B40` |
| `ColorSpectrumBorderBrush` | The picker spectrum's edge | `#EBECF0` | `#393B40` |
| `ColorViewTabItemSelectedForeground` | The icon of the picker's selected tab | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonCheckedBackground` | The chosen colour model on the picker's components tab | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonForeground` | The other colour model's caption | `#3574F0` | `#3574F0` |
| `ColorViewRadioButtonBackground` | The other colour model's ground | `#FFFFFF` | `#1E1F22` |
| `CheckBoxCheckedPointeroverBackground`, `CheckBoxCheckedPointeroverBorderBrush` | A hovered checked box: a groups pane membership box, the form's visibility box, a legend box | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedPressedBackground`, `CheckBoxCheckedPressedBorderBrush`, `CheckBoxPressedBorderBrush` | A pressed box, checked or not | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedDisabledBackground`, `CheckBoxCheckedDisabledBorderBrush` | A checked box that is disabled: a groups pane membership box while its write runs | `#A8ADBD` | `#5A5D63` |
| `DataGridColumnHeaderForeground` | A column header of the pen list or the group grid, sorted or not | `#818594` | `#6F737A` |
| `DataGridLineBrush` | The line under each pen editor grid's headers | `#EBECF0` | `#393B40` |
| `DataGridRowSelectedBackground`, `DataGridRowSelectedPointeroverBackground` | The pen list's selected row, hovered or not; the same fill as the sidebar's active row | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |

The accent is the one value deliberately equal across the variants: it does not change with the
ground it sits on.

Every other Semi surface keeps Semi's stock colour, and those follow the variant on their own.
Measured on 2026-09-11 off real controls in a headless window: `Button.Background`, an unchecked
`ToggleButton.Background` and `TextBox.Background` are Semi's translucent overlay, `#2E3238` at 0.05
under `Light` and `#FFFFFF` at 0.12 under `Dark`; the `PART_ClearButton` and `PART_RevealButton`
glyph foregrounds of a `TextBox` are `#1C1F23` at 0.62 and `#F9F9F9` at 0.6. They are correct as they
stand; the palette overrides a key only where Semi's stock value does not match the JetBrains one.
The pen editor's controls keep Semi's value in the same places, measured on 2026-09-28: the
translucent hover and press overlays of list rows, combo items and checkboxes, the picker's icons
at 0.62 and 0.35, the white check glyphs, and the black and white contrast edges of the picker's
sliders. The pen list keeps Semi's hover overlay on a row and Semi's hover and press fills on a header.
The probe of the `DataGrid` found two cell keys it leaves Semi's: `DataGridCellCurrentBorderBrush` strokes
the current cell's `Rectangle#CurrencyVisual`, and `DataGridCellFocusBorderBrush` the `Rectangle` in a
pressed cell's `Panel#FocusVisual`. The pen editor hides both parts in both of its grids with one
window-wide style on Semi's own triggers, `DataGridCell:current /template/ Rectangle#CurrencyVisual` and
`DataGridCell:focus /template/ Panel#FocusVisual`; a selector without the pseudo-class loses to Semi's
`:current` trigger. That style is the editor's only cell-outline rule, so a clicked pen row shows the row
highlight and no cell outline. The group grid shows no highlight either: two window styles on Semi's own
`:selected` trigger repaint a selected group row as an unselected one.
`DataGrid#GroupList DataGridRow:selected /template/ Border#BackgroundBorder` takes
`{DynamicResource DataGridRowBackground}`, and the same selector with `:selected:pointerover` takes
`{DynamicResource DataGridRowPointeroverBackground}`, the two keys Semi paints a row from at rest and
hovered.
Adding a control means running the same marker probe for it, not copying a key list.
The `destructive` button class paints from this tree's own keys; its style arms, and why each targets the
button's presenter, are under `#the-applications-own-surfaces`.

`ThemeTests.EverySemiControl_PaintsItselfFromThePalette` shows a real `Button`, `TextBlock`, `TextBox`
and checked `CheckBox` under both variants and reads their resolved brushes back;
`ThemeTests.EveryToggleAndScrollSurface_PaintsItselfFromThePalette` covers the rest of what the window
holds, a `ToggleButton` in both check states, an unchecked and an indeterminate `CheckBox` and a
`ScrollViewer` thumb;
`ThemeTests.EveryMenuSurface_PaintsItselfFromThePalette` opens a real `Menu` and reads the caption at
rest and open, a leaf, a checked item's glyph, a `Separator`, the flyout chrome and an `ItemsControl`
row. The `Ellipse` of a message-panel row carries no Semi key: its fill is one of this tree's own
severity brushes, gated by `MessagePanelViewTests`.
The pen editor's and the export window's controls have five tests of their own:
`TheDestructiveButton_PaintsItselfFromThePalette` (a `destructive` button at rest, hovered and pressed, its
caption's contrast on each, and disabled),
`EveryDataGridSurface_PaintsItselfFromThePalette` (a `DataGrid` header, the line under the headers, the cell
text and the selected row's `Border#BackgroundBorder` at rest and hovered),
`EveryComboBoxSurface_PaintsItselfFromThePalette` (the line-style list open, hovered and disabled, and
a disabled `CheckBox`), `EveryColourPickerSurface_PaintsItselfFromThePalette` (the picker's flyout on
the spectrum and on the components tab) and
`EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette` (a box checked and hovered,
pressed checked and unchecked, and checked while disabled).
`APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` reads the 0.25 opacity of the four translucent
keys back under both variants, `DataGridRowSelectedBackground` and `DataGridRowSelectedPointeroverBackground`
among them.
Resolving a key through `Application.TryGetResource` cannot fail while an override is inert, so a test
written that way proves nothing about the retint. A control the tree gains is added to one of those
tests in every state it reaches.

## Semi's own control strings

`SemiTheme` keys its built-in strings by specific culture and falls through to `zh-CN` for a neutral
one, so `App.ApplyAppearance` calls `SemiTheme.OverrideLocaleResources` with
`App.SemiLocaleFor(settings?.Locale ?? StartupSequence.BootstrapLocale)`, outside the
`settings is not null` guard that the variant sits inside: a settings failure
has no configured locale and reads its window in the bootstrap one.
`AppConfigurationTests.ASettingsFailure_StillHandsSemiTheBootstrapLocale` calls `App.ConfigureFailed` with
null settings and reads `STRING_MENU_COPY` back off `Application.Resources`, so moving the call
inside the guard turns it red. The surfaces this tree shows are the window's own `Menu` and the
context menu of the axis scale panel's fields.

## Corner radius sits outside the theme dictionaries

`Styles/Palette.axaml` overrides seven keys at 4 px: `ButtonCornerRadius`,
`TextBoxDefaultCornerRadius`, `CheckBoxBoxCornerRadius`, `ComboBoxSelectorCornerRadius`,
`ColorPickerCornerRadius`, `DataGridRowCornerRadius` and `DataGridColumnHeaderCornerRadius`. They sit at the top level of the dictionary, not inside
`ResourceDictionary.ThemeDictionaries`: a radius does not change with the variant. The same
`StaticResource` alias as above is why the component keys are overridden and `SemiBorderRadiusSmall`
is not.
`ThemeTests.EveryCornerRadius_ComesFromThePalette` reads all seven back off a real `Button`, `TextBox`,
`CheckBox`, `ComboBox` (its `Background` border), `ColorPicker` (its `PART_Background` border) and
`DataGrid` (a row's `Border#BackgroundBorder` and a header's `Border#HeaderBackground`) under both
variants. Semi paints 3 without the overrides, so the control is what proves them live.

A control the tree gains brings its own key, with one exception that carries none. The combo box's drop-down
and the picker's flyout keep Semi's 6 px, as the menu flyout does.

## The application's own surfaces

Semi owns the controls; these twenty keys are ours, and each exists in both variants.

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Navigation bar, legend panel, message panel, status bar, startup-failure window's message panel, minimap frame, chart hover readout, the plot's figure background | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Chart area and the plot's data background, minimap strip canvas, the sidebar's resize handle, the ground of the active pen's reading at each marker | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | The border of the navigation bar, legend panel, message panel and status bar, of the startup-failure window's message panel, of the minimap frame, of the chart hover readout, of the export window's pen list, of the pen editor's pen list, pen pane, groups pane and group grid, and the ring of a pen list row's colour dot | `#EBECF0` | `#393B40` |
| `AppSubtleLineBrush` | Plot grid, the line above every sidebar group header but the first, the line under the sidebar's top row, the status bar's separator after the connection indicator, the navigation bar's group separator | `#EBECF0` | `#393B40` |
| `AppMinorGridLineBrush` | The plot's minor gridlines, drawn on a log10 axis only | `#F4F5F7` | `#2B2D30` |
| `AppSecondaryForegroundBrush` | Minimap end labels, hover line and hover time, chart crosshair, plot axis furniture, the chart's empty-catalogue message, the sidebar group header caption, its fold chevron and a row's unit, the axis scale panel's unit, the message panel's empty text and an entry's remedy, repeat count and time, the pen editor's groups cell, the pen pane's empty hint, its title's id and its mask preview, the groups pane's subtitle, a group row's pen count and "..." glyph, and the delete confirmation's note | `#818594` | `#6F737A` |
| `AppAccentBrush` | Minimap window highlight border, the active sidebar row's left bar, a group header's fold chevron under the pointer (`overview.md#the-sidebar-fold`) | `#3574F0` | `#3574F0` |
| `AppAccentFillBrush` | Minimap window highlight fill, the active sidebar row's background | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `AppSeverityErrorBrush` | The message panel's dot on an `Error` entry; a form's invalid field border, the warning glyph of the pen form and the axis scale panel, and a form's message line | `#DB3B4B` | `#E55765` |
| `AppSeverityWarningBrush` | The same dot on a `Warning` entry | `#E3AE4D` | `#F2C55C` |
| `AppSeverityInfoBrush` | The same dot on an `Info` entry | `#3574F0` | `#3574F0` |
| `AppConnectionOkBrush` | The status bar's connection indicator, `connection-ok` | `#208A3C` | `#5FAD65` |
| `AppConnectionFaultBrush` | The same indicator, `connection-fault` | `#DB3B4B` | `#E55765` |
| `AppMarkerLineBrush` | The operator's time marker lines on the plot (`#the-plot`), and the tick of every shown marker on the minimap strip | `#6F737A` | `#A8ADBD` |
| `AppRecipeMarkerLineBrush` | The recipe marker lines on the plot | `#834DF0` | `#A571E6` |
| `AppMarkerLabelBrush` | Every time marker label on the plot | `#000000` | `#DFE1E5` |
| `AppDestructiveBackgroundBrush` | A `destructive` button at rest: the groups pane's confirm-delete button | `#C7303F` | `#C7303F` |
| `AppDestructivePointeroverBackgroundBrush` | The same button under the pointer | `#B02A38` | `#B02A38` |
| `AppDestructivePressedBackgroundBrush` | The same button pressed | `#992431` | `#992431` |
| `AppDestructiveForegroundBrush` | The same button's caption in all three states | `#FFFFFF` | `#FFFFFF` |

`Styles/Buttons.axaml`, included from `App.axaml` after `Forms.axaml`, styles `Button.destructive` from the
four `AppDestructive*` keys; Semi's `Danger` class is not used. Semi paints every button state on the
template's `ContentPresenter#PART_ContentPresenter`, so each setter targets that presenter: a setter on the
`Button` paints the rest state only, and Semi's hover and press triggers repaint the presenter over it. The
rest arm is `Button.destructive:not(:disabled) /template/ ContentPresenter#PART_ContentPresenter`, because a
presenter arm without the pseudo-class also beats Semi's `:disabled` trigger; the `:pointerover` and
`:pressed` arms follow it. A disabled `destructive` button therefore paints what a disabled default button
paints. White on the three backgrounds measures about 5.4:1, 6.5:1 and 7.9:1 (WCAG relative luminance),
above the 4.5:1 a 14 px caption needs; `AppSeverityErrorBrush`'s `#DB3B4B` gives 4.4:1 and fills no
button. The values are equal across the variants, as the accent is, because a filled button carries its
own ground. `ThemeTests.TheDestructiveButton_PaintsItselfFromThePalette` asserts each surface, the ratio
of 4.5:1 or more, and the disabled paint.

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
- An invalid field shows the `invalid` border and a warning glyph inside the box. The field binds
  `Classes.invalid` to its `Is*Valid` flag, and `Styles/Forms.axaml` paints that border with
  `AppSeverityErrorBrush`. The glyph is a 16 px `PathIcon` of `WarningGeometry` (`Styles/Icons.axaml`) in
  `AppSeverityErrorBrush`, 8 px from the box's right edge, set as the box's `InnerRightContent` through a
  `<Template>` setter, so every invalid box builds its own icon. The setter is the one style
  `.invalid-glyph TextBox.invalid` in `Styles/Forms.axaml`: a form adopts the glyph by putting the class
  `invalid-glyph` on the panel that holds its fields. The pen editor's form (`Panel#PenForm`) and the axis
  scale panel's bounds (`Grid#AxisScaleBounds`) carry it; another form adopts it when it is next touched, and
  until then marks an invalid field by the border alone.
  `PenFormPaneViewTests.AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` gates two boxes
  invalid at once, each with its own visible glyph inside its bounds and nothing resized, and
  `AValidField_ShowsNoWarningGlyph` the valid state.
  `AxisScalePanelLayoutTests.AnInvalidBound_ShowsTheWarningGlyphInsideTheBoxWithoutResizing`,
  `AValidBound_ShowsNoWarningGlyph` and `ABoundCorrected_DropsTheWarningGlyph` gate the panel's two bounds the
  same way.
- One message line sits left of the buttons, or on its own full-width row above them (the export window), or
  as the last row of a form (the time window panel, and the axis scale panel below its buttons). It is a
  `TextBlock.form-message`: two lines of 20 px
  reserved, wrapped, trimmed past the second. It shows the rule the first invalid field breaks, in form
  order, and is empty while every field is valid. A notice that is not an error, such as the settings
  dialog's restart notice with its Restart now button in the same cell, shares the same line and gives
  way to an error, the button with it.
- Each message is one short line in both languages, `ui-text.md#the-settings-windows-text`.

The invalid style targets the template part, not the control. Semi paints a `TextBox` border on
`Border#PART_ContentPresenterBorder` from its own state styles: `Transparent` at rest and
`TextBoxFocusBorderBrush` on focus. A setter on `TextBox.BorderBrush` loses to both. A `NumericUpDown`
draws its border on the same part of its inner `TextBox#PART_TextBox`, so its selector nests two
`/template/` steps. A `CalendarDatePicker` is different: its selector is
`CalendarDatePicker.invalid /template/ Border#Background`, the border of the picker's own template, and
`CsvExportWindowTests` reads the rendered brush to prove it. `Forms.axaml` follows `SemiTheme` in `Application.Styles`, so at equal priority it
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

The export window (`Export/CsvExportWindow`) is 390 px wide with `SizeToContent="Height"` and
`CanResize="False"`, a 20 px margin and 12 px between its three rows: the form grid, the message line and the
buttons. The message line is its own full-width row above a right-aligned button row, as in the axis scale
panel. The form grid is `Auto,*` at 14 px and holds two lines per edge, then the pens header, a bordered pen
list and the row count. The first line is the edge label, centred on the date, and a `CalendarDatePicker`
stretched over the field column. The second line is a nested grid `110,*` at 8 px under the date: the 110 px
time `TextBox`, then the marker `ComboBox` stretched to the date's right edge; closed, it shows the marker's
label alone, and its open list the time and the label (`ui-text.md#the-export-windows-text`). The lines of an edge stand 10 px
apart; the end label and date carry a 10 px top margin, so 20 px separate the edges, and nothing draws between
them. Every control keeps Semi's 32 px height, and the picker keeps Semi's text box padding. The dates, the
marker boxes, the pen list, the message row and the button row end at
one line, 20 px from the window edge.

The pen list is the `Border` `ExportPens`: 1 px of `AppBorderBrush`, no background of its own, sized to its
rows up to a `MaxHeight` of 226 px, eight 28 px rows plus the border. Inside it the `ScrollViewer`
`ExportPensScroller` keeps its vertical scroll bar visible (`VerticalScrollBarVisibility="Auto"`,
`AllowAutoHide="False"`) and disables the horizontal one: a pen label is a `TextBlock` with
`TextTrimming="CharacterEllipsis"`, and the full label is the box's tooltip. The scroller holds the
`ItemsControl` `ExportPenItems` over a `VirtualizingStackPanel`, so 500 pens realise only the rows on screen.
Each row is the `CheckBox` alone with `Margin="6,0"`, the pen editor's cell margin, so a box stands 6 px off the
list border. A row carries one state, its tick (`desktop-ui` One state per row): the list has no row selection
and paints no row fill under the pointer, only the checkbox's own hover, press and focus visuals.
`CsvExportWindowTests.ThePenList_IsCheckboxesWithNoRowSelection` finds no `SelectingItemsControl` and no
`ISelectable` control inside `ExportPens`, and exactly one focusable control per row, the `CheckBox`.

The list is one Tab stop. `ExportPenItems` sets `KeyboardNavigation.TabNavigation="Once"`, so Tab enters at a
box, Tab or Shift+Tab from any box leaves the list, and Shift+Tab from `Save as...` returns to the box focused
last. Up, Down, PageUp, PageDown, Home and End move the focus between the boxes through `OnPenItemsKeyDown`, a
tunnel handler the constructor registers on `ExportPenItems`. It runs ahead of `ItemsControl`'s own bubble
handler, which skips the non-focusable row containers and would scroll the whole list on one press. The handler
scrolls the target row into view and focuses its box; at either end it consumes the key and the focus stays.
PageUp and PageDown move the focus by the number of whole rows the viewport holds, eight at the list's
`MaxHeight`, and stop at the list ends; the scroller's own page scroll would leave the focus on a box the list
then recycles. Space toggles the focused box through the `CheckBox`'s own key handling; the window adds no
handler for it. `TheList_IsOneTabStop_AndUpDownWalkEveryBoxInOrder` and `SpaceOnAFocusedPenBox_TogglesItsTick`
gate the keyboard.

The message line carries the form
messages, the progress and success lines and the Excel notice, which are longer than the settings dialog's.
Measured on 2026-10-14 with Skia and HarfBuzz, the real Win32 platform, the Semi theme, Inter at 14 px, 50
pens `Damper 01` to `Damper 50`:

| | English | Russian |
| --- | --- | --- |
| Window size, every state below | 390 x 609 px, an estimate | 390 x 609 px, an estimate |
| `Save as...`, `Cancel` and `Close` buttons | 90, 73 and 64 px | 145, 82 and 88 px |
| Message row, list, right edge of the dates, the marker boxes and the `Close` button | 350 px; 370 px | 350 px; 370 px |
| Edge grid: label, date; time box, marker box | 32, 304; 110, 186 px | 52, 284; 110, 166 px |
| Pens header at "1 of 50" and "50 of 50" | 97 and 106 px | 112 and 121 px |
| Excel notice on one line, lines in the row | 574 px, 2 | 612 px, 2 |
| Success line with the 44-character suggested file name, one line, lines in the row | 454 px, 2 | 509 px, 2 |
| Longest form message on one line (lines in the row) | 253 px, end not after start (1) | 274 px, start time unreadable (1) |
| A 120-character pen name `W...W` in the list | trimmed to 314 px of a 348 px viewport, no horizontal extent | same |

The window keeps one size (50 pens, the list at its `MaxHeight`) with every field valid, with the Excel
notice (a count of 2 000 000), with each of the seven problems in turn and with the success line. Its height is
an estimate: 515 px measured with one 32 px line per edge, plus the two second lines of 32 px, their two 10 px
row gaps and the 10 px top margin of the end edge, 94 px; the stand check of #160 measures it. Every message
fits the two lines of the 350 px row (700 px of capacity), and every form message fits one. The Russian Excel
notice took three lines at 701 px, so its wording lost the opening "in the range" clause; the English one fit as it was.
The floor is the Russian buttons, 145 + 82 + 88 px and two 8 px gaps, 331 px, plus the 40 px margin: 371 px.
The label column decides the date's width: 284 px in Russian and 304 px in English. The picker's chrome
is Semi's 12 px text box padding on each side, the 8 px gap, the 16 px calendar icon and 8 px after it, 56 px,
which leaves the Russian date 228 px. In Inter at 14 px the widest `dd.MM.yyyy` measures 79.6 px (the widest
digit repeated over the mask, which bounds every date) and 13.10.2026 72.8 px.
`UnderRussian_TheWidestDateInInter_FitsTheStartPickersTextViewportOnTheStand` gates the fit: the headless
shaper advances every glyph by the font size, so the test measures the label and the date masks in the Inter
font the app ships, through Skia, corrects the realised layout by the label column's real width, requires the
corrected date to be the 350 px form less the 14 px column gap and that label column, within 1 px, and requires
the corrected text viewport to hold the widest date plus 2 px. Skia's `MeasureText` sums the advances without
kerning, an upper bound on the HarfBuzz width of a digit string. The manual stand check of the export by
markers (#160) checks the whole layout by eye. The success line names the file and not the path; the full path
is the line's tooltip. The headless tests measure with the test font, which is about twice as wide, so none asserts the
wrapping and the button row overflows the window there;
`CsvExportWindowTests.TheWindow_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid` gates the rule on the
window's and the `Save as...` button's `Bounds`, and
`TheDatesTheMarkerBoxesTheListAndTheMessage_EndAtOneRightEdge` gates the alignment and the 20 px right margin
in both languages, and `EachEdge_PutsTheDateOverTheTimeAndTheMarkerBox_AndTheTabOrderFollows` the two lines,
the 8, 10 and 20 px gaps, the centred label and the 32 px controls. A longer message is measured again in both
languages before it ships.

### The axis scale panel

`Chart/AxisScalePanel` is a `Flyout` form under the same rule. The panel is 280 px wide, with rows that exist
in every state: the header with the pen name and its unit right after it, the two `TextBox` rows of the
bounds grid, the Scale type row (the label `PenFormScaleTypeLabel` in the label column and the Logarithmic
`CheckBox` in the field column), the Autoscale toggle and the Restore initial scale button one per row across
the full width, and the reserved message line (`form-message`, 40 px) last, so its empty band reads as the
bottom margin. Each bound is a 140 px `TextBox` aligned left in the field column, on Semi's default fill.

The Logarithmic box starts at the fields' left edge. The bounds grid and the Scale type grid `AxisScaleType`
each name their `Auto` label column `SharedSizeGroup="AxisScaleLabel"` under the outer grid's
`Grid.IsSharedSizeScope`, so the column is the widest of the three labels in each language and the form has
one label edge and one field edge (`desktop-ui` Alignment). Both grids use a 14 px `ColumnSpacing`. The box
keeps its own grid outside `AxisScaleBounds`, so focus onto it ends the pair's edit
(`trend-interaction.md#the-axis-scale-panel`). The box is `HorizontalAlignment="Left"`, so only the box and
its text toggle the flag, and a click on the label or on the empty right part of the row gives the panel the
keyboard; `AxisScalePanelLayoutTests.AClickOnTheScaleTypeLabelOrBesideTheBox_LeavesTheFlagAndGivesThePanelTheKeyboard`
gates both clicks. `AxisScalePanelLayoutTests.TheLogarithmicBox_StartsAtTheFieldEdge_UnderTheScaleTypeLabel` gates
the edge and the shared column in both cultures, and `TheScaleTypeRow_FitsThePanelInInter` the fit in Inter: the
label column, the gap and the 140 px field leave at least 24 px of the panel free, and the label column, the
gap and the box fit the panel.

The Autoscale toggle keeps Semi's checked look, the one the navigation bar's Sticky and Delta toggles show.
The panel has no apply button. An invalid or empty field takes the `invalid` border and the warning glyph,
and a refused Logarithmic click leaves the box as it was and fills the message line, so nothing appears or
disappears.

Measured on 2026-10-16 with Skia and HarfBuzz, in the real theme at 14 px on the Windows machine fonts, off a
realised panel with no view model, so the header row is empty:

| | English | Russian |
| --- | --- | --- |
| Panel, before the Scale type row | 280 x 264 px | 280 x 264 px |
| Field column start, field width, before the Scale type row | 82.0 px, 140 px | 88.0 px, 140 px |
| Logarithmic scale checkbox, before the Scale type row | 141 x 19 px, ends 141.0 px from the left | 199 x 19 px, ends 199.0 px from the left |
| Button text in a 280 x 32 px button, Autoscale and Restore initial scale | 69.1 and 135.8 px | 99.1 and 145.8 px |
| `AxisScaleBoundInvalid` | 188.6 px, one line | 211.0 px, one line |
| `AxisScaleBoundsRequired` | 125.2 px, one line | 152.7 px, one line |
| `AxisScaleMinimumBelowMaximum` | 226.2 and 66.5 px, two lines | 228.3 and 78.6 px, two lines |
| `ScaleLogMinimumPositive` | 255.5 and 73.8 px, two lines | 283.2 and 185.4 px, two lines (the first with its trailing space) |
| Panel size with each message shown | unchanged | unchanged |

The three rows marked "before the Scale type row" predate the current layout; the current row, measured in
Inter at 14 px through Skia by `TheScaleTypeRow_FitsThePanelInInter` (2026-11-03):

| Inter | English | Russian |
| --- | --- | --- |
| Labels Maximum, Minimum, Scale type | 63.7, 59.8, 69.6 px | 72.8, 66.1, 75.0 px |
| Label column | 69.6 px | 75.0 px |
| Logarithmic box, its 24.0 px chrome included | 101.9 px | 149.2 px |

The submenu header with the pen "Damper valve 01" in mTorr, measured at 360 px: 181.3 px in a 270 px item in
English, 215.1 px in a 304 px item in Russian.

The 40 px `form-message` line (two 20 px lines, `MaxLines` 2) holds every message with nothing trimmed, so the
panel keeps one height whether the line is empty, holds a one-line rule or holds a two-line one. A longer
label or message is measured the same way before it ships.
`AxisScalePanelViewTests.TheMessageLine_KeepsThePanelsSize_EmptyWithAOneLineRuleAndWithTheLogRule` gates the
size under the test font, in both cultures. It does not check the trimming: the test font is about twice as
wide, so the measurement above is the only evidence that nothing is trimmed.

### The time window panel

`Chart/TimeWindowPanel` is a `Flyout` form under the same rule, 360 px wide, with four rows that exist in every
state: Start, End, Width and the reserved message line (`form-message error`, 40 px) across all three columns.
Its grid has an `Auto` label column, each time box is a 110 px `TextBox`, and the third column, `*`, holds a
`CalendarDatePicker` on the Start and End rows and the presets
`DropDownButton` on the Width row, each stretched to the row's end. The panel has no buttons: Enter, a preset
and the close commit (`trend-interaction.md#the-time-window-panel`). All five fields bind `Classes.invalid`:
the two dates, the two times and the width. An end that is not after the start, or a span over 365 days,
marks both end fields.

`TimeWindowPanelViewTests.TheMessageLine_KeepsThePanelsSize_EmptyAndWithTheLongestRule` gates the size under
the test font in `en-US` and `ru-RU`: the sizes of the panel, the message line and the flyout presenter are
equal with the line empty, with the start time unreadable and with the start time skipped by the
spring-forward change, the longest rule in both languages. The panel has not been measured with Skia, HarfBuzz and the real theme as
the axis scale panel was, so nothing yet shows that the Russian skipped-time rule fits the two reserved lines
untrimmed; it is measured that way before a longer label or message ships.

### The marker label editor

The chart's label editor (`MarkerLabelFlyout` in `Chart/TrendChartView.axaml`) is a `Flyout` form under the
same rule, 360 px wide, with two rows and 10 px between them: the label `TextBox` (`MaxLength = 80`) and a
right-aligned row of OK and Cancel. No field can be invalid: the text is trimmed, an empty one means no
label, and the box takes no 81st character. The editor therefore reserves no message line, no field binds
`Classes.invalid`, and the watermark `MarkerLabelPlaceholder` says the label is optional. The width is the
axis scale panel's and the time window panel's: a 640 px form overflowed the flyout presenter, which
clipped the box's right edge and the Cancel button. A label longer than the box
scrolls inside it. No headless test sees the clipping, so the width is checked by eye on the demo stand
(`trend-interaction.md#the-time-markers`).

### A resizable window keeps its fixed parts

The pen editor is the one form the operator may resize, because its pen list grows with the catalogue.
`PenEditor/PenEditorWindow.axaml` takes `Width` from `PenEditorWindow.DesignedWidth`, 1440 px, and `MinWidth`
from `PenEditorWindow.MinimumWidth`, 1272 px, `Height` from `PenEditorWindow.DesignedHeight`, 720 px, and
`MinHeight` from `PenEditorWindow.MinimumHeight`, 536 px, with `SizeToContent="Manual"` and `CanResize="True"`: the operator may resize it, and nothing inside it sizes
the window.

`MainWindow`'s factory for the editor sets `Width = PenEditorWindow.OpeningWidthFor(WorkingAreaWidth())`
before `DialogOpener.ShowOwnedDialog` shows the window. `OpeningWidthFor` is pure and returns
`Math.Max(MinimumWidth, Math.Min(DesignedWidth, workingAreaWidth))`. `WorkingAreaWidth()` reads the screen
`Screens.ScreenFromWindow(this)` reports for the main window, `WorkingArea.Width / Scaling` in
device-independent pixels, and returns `double.PositiveInfinity` with no screen, so the window then opens
at `DesignedWidth`. The width is set before the window is shown because `CenterOwner` centres the width
the window has when it is shown; a width changed after `Show` would move only the right edge. The working
area counts the frame, so on a screen narrower than 1440 px the client width equals the area's width and
the frame overhangs it by its border. A screen narrower than 1272 px opens the window at its minimum.

The root grid has `Margin="16"` and two rows 16 px apart. The top row is the top bar `PenEditorTopBar`, a
`*,Auto,Auto` grid with 8 px column spacing. Column 0 holds the filter box `PenListFilter`,
`PenEditorWindow.FilterBoxWidth` (280 px) wide and left-aligned, so its left edge is the root margin and the
pen list border's left edge. The box is wider than the 240 px of a free-text field because it carries a
sentence-long placeholder rather than one value, with room left for typed text past it. It realises at the
button's height with no `Height` set, so the bar stays 32 px. Columns 1 and 2 hold `PenEditorAddedCount` and
`PenEditorRefreshButton` (`Padding="12,2"`), so both stand right-aligned and the button's right edge is the
groups pane's. The top bar carries no notice. The second row is a `*,16,Auto,16,Auto` grid of the bordered
pen list, the pen pane `PenFormPanel` and the groups pane `GroupsPanel`. The rule above holds for every part
but the two grids:

- Resizing grows the two grids only. The pen list takes the spare width and height; the group grid takes
  the spare height of its pane. The pen pane (`PenEditorWindow.PenPaneWidth`, 420 px) and the groups pane
  (`PenEditorWindow.GroupsPaneWidth`, 340 px) keep their widths, and every other row of the groups pane exists in every state.
- At `MinWidth` the pen list's border box is 1272 - 4 x 16 - 420 - 340 = 448 px and its grid 438 px, which
  holds Id, Name and Unit at their opening widths (84 + 200 + 96 = 380 px). The rest scrolls
  horizontally, and both panes keep their widths. At the designed 1440 px the grid is 606 px, its viewport
  598 px with the vertical scroll bar shown, and every column fits with no horizontal scroll.
- `MinHeight` is the fixed rows plus four list rows: margin 16 + top bar 32 + 16 + groups pane + margin 16.
  The groups pane is 2 border + 32 padding + title 24 + 4 + subtitle 20 + 8 + new-group row 32 + 8 + list
  + 8 + action block 116 + 8 + message 40 = 302 px plus the list; the list's border, padding, 32 px header
  and four 28 px rows are 2 + 8 + 32 + 112 = 154 px. That is 80 + 456 = 536 px. Below 702 px the pen form
  scrolls inside its pane; a `MinHeight` of 702 would not fit the working area of a 1366 x 768 screen less
  its title bar. The opening height of 720 px holds 16 + 32 + 16 + 622 + 16 = 702 px, so the whole form
  shows.
- A pen row and a group row are each `RowHeight`, 28 px, and both grids virtualise. The rows reach 28 px
  through a window-scoped `DataGridCellMinHeight` of 28, which the cell theme reads through
  `DynamicResource`; Semi's own 32 px minimum would raise every row.
  `PenListViewTests.ARow_ShowsThePenAsStoredUnderItsHeader` pins the resource to `RowHeight`.
- Both grids take the window-wide header style, 12 px `SemiBold`, and the window-wide cell-outline rule
  (`#how-the-retint-reaches-a-control`). Each pane has its own reserved `form-message` line.

#### The pen list

The pen list is the `DataGrid` `PenList` inside a `Border` of 1 px `AppBorderBrush`, `CornerRadius` 4 and
4 px padding. It is read-only, selects one row, shows column headers only, draws no grid lines and lets no
column move. Its five columns are `DataGridTemplateColumn`s, each `CanUserSort`, `CanUserResize` and sorted
by its `PenRowOrder` comparer (`overview.md#the-pen-and-group-editor`):

| Column | Opening width | Cell |
| --- | --- | --- |
| Id | 84 px, `PenListIdColumnWidth` | `RowId`, the pen id |
| Name | 200 px | The 12 x 12 px colour dot `RowColor`, ringed with 1 px `AppBorderBrush`, then `RowName` 8 px right of it |
| Unit | 96 px | `RowUnit` |
| On start | 120 px | `RowEnabledOnStart`, a 12 x 12 px `CheckGeometry` glyph, centred, shown while the pen starts visible |
| Groups | 96 px, then the fill rule | `RowGroups`, the pen's groups in `AppSecondaryForegroundBrush` |

- A header renders at 12 px `SemiBold`. Semi's header pads its text 12 px from the left and lays out its sort
  arrow at a fixed size with an 8 px right margin whether the column is sorted or not, so a sorted header is
  as wide as an unsorted one and the arrow never pushes the text into an ellipsis.
  `HeaderMeasuredColumnsTests.AnUnsortedHeader_ReservesTheSortArrowSlot` gates it.
- Every column opens no narrower than its realised header. `Tables/HeaderMeasuredColumns.FitsColumns` on the
  grid measures every visible column's header unconstrained once all of them are realised, sets the column's
  `MinWidth` to the ceiling of that width and raises a narrower declared `Width` to it. The header is matched
  to its column through `DataGridColumn.GetColumnContainingElement`, never through its text.
- The column marked `HeaderMeasuredColumns.IsFill`, Groups, takes the spare width: its `Width` is
  `max(MinWidth, viewport - others)`, where the viewport is the headers presenter's width less the vertical
  scroll bar's while that bar is visible and `others` the sum of the other visible columns' `ActualWidth`.
  The rule runs on every `LayoutUpdated` while the grid is attached and writes only when the target differs
  by more than 0.5 px. So the Groups column grows and shrinks with the window, takes back what a resize of
  another column gives up, and stops at its minimum, past which the grid scrolls horizontally. The fill
  column has a pixel width, never `*`, so the last column keeps a grab edge.
- The fit runs in the `LayoutUpdated` of the layout pass that realises the headers, and the pass it
  invalidates is laid out before the frame is committed, so the first frame shows the fitted widths;
  nothing is posted to the dispatcher. The behaviour adds `LayoutUpdated` on attach and removes it on
  detach, and caches the headers presenter and the scroll bar so a pass walks no row's visuals.
- The Groups header is measured while the filler header still stands right of it, so its 1 px
  `VerticalSeparator` is part of that measure and is hidden once the fill closes the gap: the Groups
  minimum may sit 1 px over the header's later unconstrained width.
- A text cell is `TextBlock.grid-cell`: centred vertically, trimmed with an ellipsis, and carrying its own
  text as a tooltip (`$self.Text`). The class sets no margin; each cell sets its own. `RowId` and `RowUnit`
  stand 12 px from the cell's left edge and 8 px from its right. The fill column's cell keeps 12 px on both
  sides, so its text ends 12 px before the vertical scroll bar. The name cell's grid carries the 12 and 8 px
  insets and its `RowName` none. The glyph cell carries no tooltip: its
  column opens no narrower than its header, wider than the glyph.
- A clicked row shows Semi's row highlight from `DataGridRowSelectedBackground` and no cell outline. The
  window style that hides the currency rectangle and the focus visual is under
  `#how-the-retint-reaches-a-control`.

#### The pen pane

The pane is the `Border` `PenFormPanel`, `PenEditorWindow.PenPaneWidth` (420 px) wide, with 16 px padding, a 1 px `AppBorderBrush` border and
`CornerRadius` 4, disabled while a refresh runs. With no pen selected it shows only the hint
`PenFormEmptyHint`, centred both ways in `AppSecondaryForegroundBrush`. With a pen selected it shows the
`ScrollViewer` `PenFormScroller` (`Focusable="False"`, no horizontal scrolling), which holds the form:

- The title, a 24 px row of two 16 px `SemiBold` blocks 8 px apart: the id `PenFormTitleId` in
  `AppSecondaryForegroundBrush`, then the name `PenFormTitleName`, trimmed, with the full name as its tooltip.
- Three section headers, Label, Appearance and On start: `TextBlock.form-section`, `SemiBold`, 20 px high,
  16 px above and 8 px below.
- Under each header a grid of the window resource `PenFormColumns` (`x:Shared="False"`): a 112 px label
  column and a 240 px field column, every row 32 px. Each grid is a `Grid.form-grid`, whose window style sets
  12 px between the columns, 8 px between the rows and `HorizontalAlignment="Left"`: stretched across the scroller, a grid widens both pixel columns by a pixel
  and puts every field 125 px off the label edge.
- Every label is `TextBlock.form-label` in the label column; every field, the two checkboxes included,
  starts at the field column's edge, 124 px right of the label edge. Name and Line stretch across the 240 px
  field column; Unit, Mask, Colour, Minimum and Maximum carry their own 120 px width. The colour row is the 120 px `FormColor` box
  and, 8 px right of it, the 32 x 32 px `FormColorPicker` with no padding, its drop-down arrow hidden, so the
  picker is the swatch alone. The mask preview is a trimmed `TextBlock` with its text as a tooltip.
- The On start section holds the visibility box, `FormScale` and the scale type box. `FormScale` is the scale
  pair's focus scope and its own `PenFormColumns` grid of two 32 px rows, Minimum and Maximum, spanning both
  columns of the section's grid; the scale type box is the row under it, outside the scope, on the same field
  edge.
- The reserved `form-message` line, 40 px, 16 px under the last section.

The form's height is fixed by the AXAML: title 24 px; three section headers at 16 + 20 + 8 px; rows
4 x 32 + 3 x 8, 2 x 32 + 8, and 32 + (2 x 32 + 8) + 32 + 2 x 8 px; the message line 16 + 40 px. That is
24 + 132 + 152 + 72 + 152 + 56 = 588 px, inside the pane's 32 px of padding and 2 px of border, 622 px in
all. At the opening height of 720 px the scroller needs no scrolling; a lower window scrolls the form rather
than clipping it.

An invalid field takes the `invalid` class and the warning glyph (`#a-form-never-resizes-on-validation`).
The line-style combo box and the checkboxes refuse no value, and `Forms.axaml` styles `invalid` on
`TextBox`, `NumericUpDown` and `CalendarDatePicker` only, not on a `ComboBox` or a `CheckBox`, so a failed
write of either shows on the message line and in the panel only. The scale type box refuses a tick while the
pen stores a minimum at or below zero; the refusal shows on the message line alone, and a minimum that breaks
the rule while the box is ticked marks the minimum `TextBox` `invalid`.

#### The groups pane

The pane is the `Border` `GroupsPanel`, `PenEditorWindow.GroupsPaneWidth` (340 px) wide, with 16 px padding, a 1 px `AppBorderBrush` border and
`CornerRadius` 4, disabled while a refresh runs; its content is 306 px wide. Its grid has the rows
`Auto,Auto,*,Auto,Auto`, 8 px apart:

- The title `GroupsTitle`, `TextBlock.form-title`, 24 px, and 4 px under it the subtitle `GroupsSubtitle`,
  a `TextBlock.pane-line` in `AppSecondaryForegroundBrush`. The window style `pane-line` sets one 20 px line,
  centred and trimmed.
- The new-group row: `NewGroupName` and `CreateGroupButton` in a `*,Auto` grid, 8 px apart.
- The `DataGrid` `GroupList` inside a `Border` of 1 px `AppBorderBrush`, `CornerRadius` 4 and 4 px padding,
  so the grid is 296 px wide less the vertical scroll bar while it shows. It is read-only, selects one row
  without showing it (`#how-the-retint-reaches-a-control`), shows column headers only, draws no grid
  lines, sorts nothing, lets no column move and carries `HeaderMeasuredColumns.FitsColumns`.
- The action block `GroupActionBlock`, 116 px in every state.
- The reserved `form-message` line `GroupsMessage`, 40 px.

| Column | Header | Opening width | Resize | Cell |
| --- | --- | --- | --- | --- |
| Membership | none | 36 px | no | `MembershipBox`, centred |
| Group | `PenGroupsColumnGroup` | 120 px, then the fill rule (`IsFill`) | yes | A `*,Auto` grid, 8 px column spacing, `Margin="12,0,8,0"`: `GroupName`, a `TextBlock.grid-cell` with its text as a tooltip, then `GroupMemberCount`, right-aligned in `AppSecondaryForegroundBrush` with the tooltip `PenGroupsMemberCountTip` |
| Actions | `PenGroupsColumnActions` | 40 px, raised to its header | no | `GroupActionsButton`, `BorderlessButton` theme, 28 x 24 px, no padding, 12 px from the cell's left edge, the tooltip `PenGroupsActionsTip`, holding a 16 x 16 px `MoreGeometry` `PathIcon` in `AppSecondaryForegroundBrush`; its `MenuFlyout` opens `BottomEdgeAlignedLeft` |

- The membership and action columns do not resize: each holds one fixed-size control, and a wider column
  shows nothing more. Group stands in the middle because the box a membership list is read by stands
  first and the row's menu last; `FitFillColumn` fits a fill column wherever it stands, and Actions keeps
  the last grab edge.
- `MembershipBox` binds `IsChecked` to `Membership.IsMember` `Mode=OneWay` with `FallbackValue=False`,
  `Command` to `Membership.ToggleMembershipCommand` and `IsEnabled` to `Membership` through
  `ObjectConverters.IsNotNull`. With no pen selected the compiled binding through the null `Membership`
  reads the fallback, so the box is disabled and unchecked, never indeterminate; while a write runs, the
  command's `IsExecuting` disables it.
- The grid declares its three columns statically, binds no column `IsVisible` and disables rather than
  hides a membership box, so the `HeaderMeasuredColumns` measure, which latches on its first success, sees
  every column; a grid with no group still realises the headers.
- The action block is as tall as its tallest state, the confirmation: question 20 + 8 + count 20 + 8 +
  note 20 + 8 + buttons 32 = 116 px. `GroupRenameBlock` holds the 20 px label `PenGroupsNameLabel`, then a
  `*,Auto` grid, 8 px apart, of the `TextBox` `GroupRename` (inside a `Panel` whose `DataContext` is
  `PendingRename`, with `Classes.invalid` bound to `!IsNameValid`) and `GroupRenameDoneButton`.
  `GroupDeletionBlock` holds `GroupDeletionQuestion`, `GroupDeletionCount` and `GroupDeletionNote` (in
  `AppSecondaryForegroundBrush`), each a `TextBlock.pane-line`, the question with its text as a tooltip, then a
  right-aligned row, 8 px apart, of `ConfirmDeleteButton` (`Classes="destructive"`) and
  `CancelDeleteButton`. No state moves anything else in the pane.

Measured in Inter with 40 groups, so the vertical scroll bar shows
(`TheGroupColumns_LeaveTheNameAtLeast100PxInInter`): in `en` Actions takes 81.5 px, Group 169.5 px and the
name text 115.3 px; in `ru` 95.2, 155.8 and 101.6 px. The name text is Group less the 12 and 8 px cell
insets, the 8 px gap and the width of "999".

#### What gates the layout

`EveryPenFormLabel_FitsTheLabelColumnInInter` measures each of the form's ten label texts in the Inter font
the app ships, at the label's `FontSize`, through Skia (`SemiPlot.Tests.Unit/UI/InterMeasure.cs`), in `en` and `ru`: each is at
most 112 px. The headless tests measure with the test font, which is about twice as wide, so every other
gate reads the realised layout. The longest form message, `PenFormMaskInvalid`, measured 476.6 px in English
and 480.3 px in Russian on one line on 2026-09-30, so it wraps into the second reserved line; the wrapped
line has not been measured in the pane. Measured on 2026-09-29, with a three-digit added count the count needs
210 px in English and 294 px in Russian, and the button 250 px for "Refresh pen list" and 334 px for «Обновить
список перьев»; with the 280 px filter box and the 8 px gaps the top bar needs 756 px in English and 924 px
in Russian, inside the 1240 px it has at `MinWidth`. Measured on 2026-11-03 in Inter at the box's
`FontSize` (`ThePlaceholder_FitsTheFilterBoxInInter`), the filter placeholder is 157.6 px in English and
217.3 px in Russian, inside the box's 254 px inner width, 280 px less its `Padding` and `BorderThickness` on
both sides. The groups pane's 340 px is chosen; its texts are gated in Inter against the realised
`GroupsSubtitle` width, 306 px. The headless font also pushes the pen list's Groups column partly past the
viewport at the designed 1440 px, so `EachHeaderClick_OrdersTheListByItsColumn` scrolls that column in
through `PenEditorWindowDriver.ScrollColumnIntoView` before it clicks the header. The realised list raises
each fixed width to its header in the headless font, so
`AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter` reads the declared widths from an unshown
`PenEditorWindow`. Measured in Inter at 1440 px with 100 pens, the viewport is 598 px and the columns take
84 + 200 + 96 + 120 px plus the Groups header, 78.5 px in `en` and 81.1 px in `ru`: 578.5 and 581.1 px. A
220 px Name column would take 598.5 and 601.1 px and scroll.

| Test | Part it gates |
| --- | --- |
| `PenEditorLayoutViewTests.TheWindow_ShowsThePenListThePenPaneAndTheGroupsPaneSideBySide` | No `TabControl`; the pen list's border, the 420 px pen pane and the 340 px groups pane on one top, 16 px apart, 16 px from the window's edges, the top bar 16 px above them |
| `PenEditorLayoutViewTests.TheTopBar_StandsAboveThePanesRightAlignedAndTheVisibilityColumnReadsOnStart` | The count and the button 8 px apart above the panes, the button's right edge on the groups pane's, and the On start header |
| `PenEditorLayoutViewTests.TheFilterBox_StandsLeftInTheTopBar280PxWideWithItsPlaceholder` | The filter box inside the top bar, `FilterBoxWidth` wide, its left edge on the pen list border's, the box and the bar at the button's height, its placeholder, and its right edge left of the count |
| `PenEditorLayoutViewTests.ThePlaceholder_FitsTheFilterBoxInInter` | The filter placeholder in Inter, in `en` and `ru`, inside the box's width less its `Padding` and `BorderThickness` |
| `PenEditorLayoutViewTests.AtTheMinimumWidth_BothPanesKeepTheirWidthAndThePenListScrolls` | The opening at `DesignedWidth`; at `MinWidth` both panes at their widths, every pen column at or above its minimum, Id and Name inside the viewport, and the horizontal scroll |
| `PenEditorLayoutViewTests.BothPanes_StayFixedWhenTheWindowWidens` | Both panes unchanged and the pen list 300 px wider when the window widens by 300 px |
| `PenEditorLayoutViewTests.TheOpeningWidth_TakesTheDesignedWidthWhereTheWorkingAreaAllowsAndNeverLessThanTheMinimum` | `OpeningWidthFor` over working areas of 1920, 1440, 1366, 1280 and 1024 px and the infinite width of no screen |
| `PenEditorLayoutViewTests.AtTheMinimumHeight_EveryFixedRowShowsAndEachListShowsFourRows` | At `MinHeight` 536 px the confirmation inside the action block, the message line inside the pane, and four rows in each grid |
| `MainWindowViewTests.EditPensAndGroups_ClickedOnTheRealisedWindow_OpensTheEditorOverIt` | The opening width from the main window's screen; the headless screen reports 1920 px, so the window opens at 1440 px |
| `PenListViewTests.AtTheOpeningWidth_EveryPenListColumnFitsTheViewportInInter` | Every pen column inside the viewport at 1440 px in Inter, in `en` and `ru` |
| `PenListViewTests.EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader` | `CanUserResize` on every column; each minimum between its header's unconstrained width and 1 px more |
| `PenListViewTests.ARow_ShowsThePenAsStoredUnderItsHeader` | The cells as stored, the colour dot inside the name cell, the 28 px row |
| `PenListViewTests.EveryTextCell_TrimsWithAnEllipsisAndCarriesItsTextAsATooltip` | The trimming and the tooltip of every text cell |
| `PenListViewTests.AFullCatalogue_RealisesOnlyTheRowsOnScreen` | The virtualised rows |
| `PenListViewTests.AClickedRow_ShowsTheRowHighlightAndNoCellOutline` | The selection fill and no visible currency rectangle or focus visual |
| `PenFormPaneViewTests.WithNoPenSelected_ThePaneShowsOnlyTheHint`, `PenFormPaneViewTests.AClickedPen_FillsTheFormUnderAOneLineTitle` | The hint and the one-line title |
| `PenFormPaneViewTests.TheForm_HasThreeSectionsOnOneLabelColumnAndOneFieldEdge`, `PenFormPaneViewTests.EveryField_HasTheWidthOfItsContentType`, `PenFormPaneViewTests.TheColourSwatchPicker_IsSquare` | The sections, the 124 px field edge, the field widths, the 32 x 32 px swatch 8 px right of the colour box |
| `PenFormPaneViewTests.AtTheOpeningSize_ThePaneShowsTheWholeForm` | The form's extent inside the scroller's viewport at 1440 x 720 px |
| `PenEditorViewTests.AnUnusableMask_WritesNothingRevertsMarksAndSaysWhyWithoutResizing` | The window's and the pane's `Bounds` equal before and after the mask turns invalid |
| `PenGroupsViewTests.AGroupRow_ShowsTheNameAndTheCountWithItsTooltip` | The name, the count right of it inside the name cell, its tooltip, the 28 px row |
| `PenGroupsViewTests.ARowClick_ShowsNoSelectionHighlightAndNoCellOutline` | A clicked group row painted as an unclicked one at rest and hovered; no visible currency rectangle or focus visual |
| `PenGroupsViewTests.TheGroupGrid_SortsNothingOnAHeaderClick` | The row order and no sort arrow after a header click |
| `PenGroupsViewTests.EveryGroupColumn_OpensNoNarrowerThanItsHeader` | With no group and with two: each minimum between its header's unconstrained width and 1 px more; only Group resizes |
| `PenGroupsViewTests.WithNoPenSelected_EveryMembershipBoxIsDisabledAndUnchecked` | The disabled, unchecked box with no pen selected |
| `PenGroupsViewTests.TheRowMenu_HoldsRenameTheFoldFlagAndDelete` | The menu's items in order and the button's tooltip |
| `PenGroupsViewTests.TheActionBlock_KeepsItsBoundsEmptyRenamingAndAskingToDelete` | The action block's and the pane's `Bounds` equal in the three states, every visible part inside the block |
| `PenGroupsViewTests.TheConfirmButton_IsDestructiveFromThePaletteNotSemisDanger` | The `destructive` class on the confirm button, and no `Danger` |
| `PenGroupsViewTests.AFullCatalogue_RealisesOnlyTheGroupRowsOnScreen` | The virtualised group rows |
| `PenGroupsViewTests.TheGroupColumns_LeaveTheNameAtLeast100PxInInter` | At least 100 px of name text beside the membership and action columns in Inter, in `en` and `ru` |
| `PenGroupsViewTests.ThePaneTexts_FitTheirLinesInInter` | The subtitle, the count line for 999 pens and the note each on one line of the pane in Inter, in `en` and `ru` |

`HeaderMeasuredColumnsTests` gates the behaviour on a bare grid: the header-width minimum, a declared width
above it kept, the fit inside the layout pass that realises the headers, the reserved arrow slot, the fill
column taking, shrinking to its minimum and growing back with the viewport, a resize handed to the fill
column, the visible vertical scroll bar left out of the viewport, and the fit after the grid is detached and
attached again.

## How the variant reaches the application

`Startup/AppSettingsLoader` reads `theme` into `AppSettings.Theme`
(an `AppThemeVariant`, `Startup/AppSettings.cs`), and `App.ApplyAppearance`, which `App.ConfigureStarted` and
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

ScottPlot paints five surfaces from its own defaults, and `Chart/ChartPalette.cs` moves all five
under the palette keys:

| ScottPlot surface | Key |
| --- | --- |
| `Plot.FigureBackground.Color` | `AppPanelBackgroundBrush` |
| `Plot.DataBackground.Color` | `AppContentBackgroundBrush` |
| `Plot.Grid.MajorLineColor` | `AppSubtleLineBrush` |
| `Plot.Grid.MinorLineColor` | `AppMinorGridLineBrush` |
| Per axis: `Label.ForeColor`, `TickLabelStyle.ForeColor`, `MajorTickStyle.Color`, `MinorTickStyle.Color`, `FrameLineStyle.Color` | `AppSecondaryForegroundBrush` |

The five keys are constants in a dictionary the same assembly ships, so `Apply` resolves them
unconditionally and throws when one does not resolve to a brush; a palette key that stops resolving
is a defect in the palette, not a state to paint around. All five brushes are opaque, and `Apply`
reads the ARGB value alone: `AppAccentFillBrush` is the one key declaring `Opacity`, and no ScottPlot
surface takes it.

`ChartPalette.Apply` takes `AvaPlot.Plot`, not the view model's: the two are one instance once a view
model is bound, and a view with no view model has no chart view model, where an unpainted plot
would be a white rectangle inside a dark window.

`Chart/TrendChartView.axaml.cs` calls it from three places, and each is a different reason. `OnLoaded`
paints the control's plot and requests a frame; `ActualThemeVariantChanged` repaints and requests a frame;
the `ScalesRevision` subscription paints an axis the scale model created after bind time. That third path
runs once per pointer move during a pan, so it compares `TrendChartViewModel.AxisCount` with the count
of the last paint and does nothing when the axis set did not grow, and it never requests a frame itself
because every site that bumps `ScalesRevision` already follows with
`RequestRedraw()`, which the view joins into the next display frame.

The time markers take three keys of their own, outside the five surfaces. `ChartPalette.MarkerColors`
resolves `AppMarkerLineBrush`, `AppRecipeMarkerLineBrush` and `AppMarkerLabelBrush` under the variant into
a `MarkerPalette`, and `ApplyChartPalette` hands it to the marker layer after every `Apply`, so a variant
switch repaints every marker line. `ChartMarkerLines.ApplyPalette` keeps the palette and recolours each
line by its marker's kind, and a line the layer adds later takes the same colours. The operator's line is
a neutral grey, so it does not compete with the pens' colours, and the label is the variant's default
text colour; those four values are tones the palette already carries. The recipe line is a purple apart
from the accent blue, the severity red and amber and the connection green. A marker's kind picks its line
colour, every label draws in the one label colour, and a marker carries no colour of its own. The palette
also carries `AppContentBackgroundBrush` as the background of the active pen's reading at each marker,
drawn at 85 % opacity so the number stays legible over the pen lines; the reading's dot takes the active
pen's colour.
`ChartPaletteTests.MarkerColors_ResolveEveryMarkerKeyUnderTheVariant` resolves every marker key under both
variants, and `ChartMarkerViewTests.AfterAThemeVariantSwitch_EveryMarkerDrawsInTheNewVariant` reads an
existing line and a later one after a switch; `AfterAThemeVariantSwitch_ARecipeLineKeepsTheRecipeColour`
reads a recipe line.

The minor gridlines take their colour on every axis but draw only on a log10 axis: `ChartAxisBinder` sets
their width to 1 px when the drawn axis is logarithmic and to 0 otherwise (`charting.md#log10-y-axis`).
`MinorLineColor` has no getter, so a test reads `Grid.YAxisStyle.MinorLineStyle.Color`.

`ChartPaletteTests` asserts the five surfaces on a real `Plot` against the resolved palette under both
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
