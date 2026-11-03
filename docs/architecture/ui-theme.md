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
| `ListBoxItemPointeroverForeground`, `ListBoxItemPressedForeground`, `ListBoxItemSelectedForeground` | A hovered, pressed or selected row of the group list | `#000000` | `#DFE1E5` |
| `ListBoxItemSelectedBackground`, `ListBoxItemSelectedPointeroverBackground` | The selected row, hovered or not; the same fill as the sidebar's active row | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
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
| `CheckBoxCheckedPointeroverBackground`, `CheckBoxCheckedPointeroverBorderBrush` | A hovered checked box: a membership box, the form's visibility box, a legend box | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedPressedBackground`, `CheckBoxCheckedPressedBorderBrush`, `CheckBoxPressedBorderBrush` | A pressed box, checked or not | `#3574F0` | `#3574F0` |
| `CheckBoxCheckedDisabledBackground`, `CheckBoxCheckedDisabledBorderBrush` | A checked box that is disabled: a membership box while its write runs | `#A8ADBD` | `#5A5D63` |
| `DataGridColumnHeaderForeground` | A pen list column header, sorted or not | `#818594` | `#6F737A` |
| `DataGridLineBrush` | The line under the pen list's headers | `#EBECF0` | `#393B40` |
| `DataGridRowSelectedBackground`, `DataGridRowSelectedPointeroverBackground` | The pen list's selected row, hovered or not; the same fill as the group list's selected row | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |

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
sliders. The pen list keeps Semi's hover overlay on a row and Semi's hover and press fills on a header.
The probe of the `DataGrid` found two cell keys it leaves Semi's: `DataGridCellCurrentBorderBrush` strokes
the current cell's `Rectangle#CurrencyVisual`, and `DataGridCellFocusBorderBrush` the `Rectangle` in a
pressed cell's `Panel#FocusVisual`. The pen editor hides both parts with one window style on Semi's own
triggers, `DataGrid#PenList DataGridCell:current /template/ Rectangle#CurrencyVisual` and
`DataGrid#PenList DataGridCell:focus /template/ Panel#FocusVisual`; a selector without the pseudo-class
loses to Semi's `:current` trigger. That style is the pen list's only cell-outline rule, so a clicked row
shows the row highlight and no cell outline.
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
The pen editor's controls have five tests of their own: `EveryTabAndListSurface_PaintsItselfFromThePalette`
(a `TabControl` and a `ListBox` at rest, hovered, pressed and selected),
`EveryDataGridSurface_PaintsItselfFromThePalette` (a `DataGrid` header, the line under the headers, the cell
text and the selected row's `Border#BackgroundBorder` at rest and hovered),
`EveryComboBoxSurface_PaintsItselfFromThePalette` (the line-style list open, hovered and disabled, and
a disabled `CheckBox`), `EveryColourPickerSurface_PaintsItselfFromThePalette` (the picker's flyout on
the spectrum and on the components tab) and
`EveryCheckBoxStateAMembershipBoxReaches_PaintsItselfFromThePalette` (a box checked and hovered,
pressed checked and unchecked, and checked while disabled).
`APaletteKeyCarryingOpacity_KeepsItUnderBothVariants` reads the 0.25 opacity of the six translucent
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

A control the tree gains brings its own key, with two exceptions that carry none. A `ListBoxItem` and a
line-style `TabItem` paint 0 px in Semi, square as the surfaces around them. The combo box's drop-down
and the picker's flyout keep Semi's 6 px, as the menu flyout does.

## The application's own surfaces

Semi owns the controls; these sixteen keys are ours, and each exists in both variants.

| Key | Consumers | Light | Dark |
| --- | --- | --- | --- |
| `AppPanelBackgroundBrush` | Navigation bar, legend panel, message panel, status bar, startup-failure window's message panel, minimap frame, chart hover readout, the plot's figure background | `#F7F8FA` | `#2B2D30` |
| `AppContentBackgroundBrush` | Chart area and the plot's data background, minimap strip canvas, the sidebar's resize handle, the ground of the active pen's reading at each marker | `#FFFFFF` | `#1E1F22` |
| `AppBorderBrush` | The border of the navigation bar, legend panel, message panel and status bar, of the startup-failure window's message panel, of the minimap frame, of the chart hover readout, of the export window's pen list, of the pen editor's pen list and pen pane, and the ring of a pen list row's colour dot | `#EBECF0` | `#393B40` |
| `AppSubtleLineBrush` | Plot grid, the line above every sidebar group header but the first, the line under the sidebar's top row, the status bar's separator after the connection indicator, the navigation bar's group separator | `#EBECF0` | `#393B40` |
| `AppMinorGridLineBrush` | The plot's minor gridlines, drawn on a log10 axis only | `#F4F5F7` | `#2B2D30` |
| `AppSecondaryForegroundBrush` | Minimap end labels, hover line and hover time, chart crosshair, plot axis furniture, the chart's empty-catalogue message, the sidebar group header caption, its fold chevron and a row's unit, the axis scale panel's unit, the message panel's empty text and an entry's remedy, repeat count and time, the pen editor's groups cell, the pen pane's empty hint, its title's id and its mask preview | `#818594` | `#6F737A` |
| `AppAccentBrush` | Minimap window highlight border, the active sidebar row's left bar, a group header's fold chevron under the pointer (`overview.md#the-sidebar-fold`) | `#3574F0` | `#3574F0` |
| `AppAccentFillBrush` | Minimap window highlight fill, the active sidebar row's background | `#3574F0` at 0.25 opacity | `#3574F0` at 0.25 opacity |
| `AppSeverityErrorBrush` | The message panel's dot on an `Error` entry; a form's invalid field border, the pen form's warning glyph and a form's message line | `#DB3B4B` | `#E55765` |
| `AppSeverityWarningBrush` | The same dot on a `Warning` entry | `#E3AE4D` | `#F2C55C` |
| `AppSeverityInfoBrush` | The same dot on an `Info` entry | `#3574F0` | `#3574F0` |
| `AppConnectionOkBrush` | The status bar's connection indicator, `connection-ok` | `#208A3C` | `#5FAD65` |
| `AppConnectionFaultBrush` | The same indicator, `connection-fault` | `#DB3B4B` | `#E55765` |
| `AppMarkerLineBrush` | The operator's time marker lines on the plot (`#the-plot`), and the tick of every shown marker on the minimap strip | `#6F737A` | `#A8ADBD` |
| `AppRecipeMarkerLineBrush` | The recipe marker lines on the plot | `#834DF0` | `#A571E6` |
| `AppMarkerLabelBrush` | Every time marker label on the plot | `#000000` | `#DFE1E5` |

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
  `<Template>` setter, so every invalid box builds its own icon. The pen editor's form carries the glyph
  through its window-scoped style `Panel#PenForm TextBox.invalid`; another form adopts it when it is next
  touched, and until then marks an invalid field by the border alone.
  `PenEditorViewTests.AnInvalidField_ShowsTheWarningGlyphInsideTheBoxWithoutResizing` gates two boxes
  invalid at once, each with its own visible glyph inside its bounds and nothing resized, and
  `AValidField_ShowsNoWarningGlyph` the valid state.
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
one line, 20 px from the window edge. The list sizes to its rows up to a `MaxHeight` of 226 px (eight 28 px
rows plus the border; a window-scoped `ListBoxItem` style zeroes Semi's vertical item padding and `MinHeight`,
as in the pen editor, and keeps a 6 px horizontal padding, the pen editor's cell margin, so a checkbox stands
6 px off the list border), is bordered with `AppBorderBrush` and keeps its vertical scroll bar visible
(`AllowAutoHide="False"`). Its horizontal scroll bar is disabled: a pen label is a `TextBlock` with
`TextTrimming="CharacterEllipsis"` and the full label is the row's tooltip. The message line carries the form
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
bounds grid, the Logarithmic scale `CheckBox` row, the Autoscale toggle and the Restore initial scale button
one per row across the full width, and the reserved message line (`form-message`, 40 px) last, so its empty
band reads as the bottom margin. Each bound is a 140 px `TextBox` aligned left in the field column, on
Semi's default fill. The checkbox spans the panel from the left edge under the labels. The Autoscale toggle
keeps Semi's checked look, the one the navigation bar's Sticky and Delta toggles show. The panel has no apply
button. An invalid or empty field takes the `invalid` border, and a refused Logarithmic scale click leaves
the box as it was and fills the message line, so nothing appears or disappears.

Measured on 2026-10-16 with Skia and HarfBuzz, in the real theme at 14 px on the Windows machine fonts, off a
realised panel with no view model, so the header row is empty:

| | English | Russian |
| --- | --- | --- |
| Panel | 280 x 264 px | 280 x 264 px |
| Field column start, field width | 82.0 px, 140 px | 88.0 px, 140 px |
| Logarithmic scale checkbox | 141 x 19 px, ends 141.0 px from the left | 199 x 19 px, ends 199.0 px from the left |
| Button text in a 280 x 32 px button, Autoscale and Restore initial scale | 69.1 and 135.8 px | 99.1 and 145.8 px |
| `AxisScaleBoundInvalid` | 188.6 px, one line | 211.0 px, one line |
| `AxisScaleBoundsRequired` | 125.2 px, one line | 152.7 px, one line |
| `AxisScaleMinimumBelowMaximum` | 226.2 and 66.5 px, two lines | 228.3 and 78.6 px, two lines |
| `ScaleLogMinimumPositive` | 255.5 and 73.8 px, two lines | 283.2 and 185.4 px, two lines (the first with its trailing space) |
| Panel size with each message shown | unchanged | unchanged |

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
`PenEditor/PenEditorWindow.axaml` opens at a fixed `Width` and `Height`, 1272 x 720 px, with
`MinWidth="1260"`, `SizeToContent="Manual"` and `CanResize="True"`: the operator may widen it, and nothing
inside it sizes the window. The smallest screen the editor opens on is 1280 px wide; the 720 px height exceeds
the working area of a 768 px screen. The rule above holds for every part but the two lists:

- Resizing grows the pen list and the membership list only. The pen list takes the width and the height
  the `Pens` tab gives it; the tab's grid is `*,16,420`, the list, a 16 px gap and the pen pane. The pane
  (420 px wide), the `Groups` tab's side panel (320 px wide), its confirmation row (32 px high) and the
  bottom bar keep fixed sizes, made of rows that exist in every state.
- A pen row, a group row and a membership row are each `RowHeight`, 28 px, and all three lists
  virtualise. The pen list's rows reach 28 px through a window-scoped `DataGridCellMinHeight` of 28, which
  the cell theme reads through `DynamicResource`; Semi's own 32 px minimum would raise every row.
- The bottom bar, on both tabs, holds the added count and the refresh button in a right-aligned `Auto,Auto`
  grid, 12 px apart, and carries no notice. Each tab has its own reserved `form-message` line.

#### The pen list

The pen list is the `DataGrid` `PenList` inside a `Border` of 1 px `AppBorderBrush`, `CornerRadius` 4 and
4 px padding. It is read-only, selects one row, shows column headers only, draws no grid lines and lets no
column move. Its five columns are `DataGridTemplateColumn`s, each `CanUserSort`, `CanUserResize` and sorted
by its `PenRowOrder` comparer (`overview.md#the-pen-and-group-editor`):

| Column | Opening width | Cell |
| --- | --- | --- |
| Id | 84 px | `RowId`, the pen id |
| Name | 220 px | The 12 x 12 px colour dot `RowColor`, ringed with 1 px `AppBorderBrush`, then `RowName` 8 px right of it |
| Unit | 96 px | `RowUnit` |
| On start | 120 px | `RowEnabledOnStart`, a 12 x 12 px `CheckGeometry` glyph, centred, shown while the pen starts visible |
| Groups | 96 px, then the fill rule | `RowGroups`, the pen's groups in `AppSecondaryForegroundBrush` |

- A header renders at 12 px `SemiBold`. Semi's header pads its text 12 px from the left and lays out its sort
  arrow at a fixed size with an 8 px right margin whether the column is sorted or not, so a sorted header is
  as wide as an unsorted one and the arrow never pushes the text into an ellipsis.
  `HeaderMeasuredColumnsTests.AnUnsortedHeader_ReservesTheSortArrowSlot` gates it.
- Every column opens no narrower than its realised header. `Tables/HeaderMeasuredColumns.IsEnabled` on the
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
- A text cell is `TextBlock.grid-cell`: 12 px from the cell's left edge and 8 px from its right, centred
  vertically, trimmed with an ellipsis, and carrying its own text as a tooltip (`$self.Text`). The fill
  column's cell keeps 12 px on both sides, so its text ends 12 px before the vertical scroll bar. The name
  cell's grid carries the 12 and 8 px insets and its `RowName` none. The glyph cell carries no tooltip: its
  column opens no narrower than its header, wider than the glyph.
- A clicked row shows Semi's row highlight from `DataGridRowSelectedBackground` and no cell outline. The
  window style that hides the currency rectangle and the focus visual is under
  `#how-the-retint-reaches-a-control`.

#### The pen pane

The pane is the `Border` `PenFormPanel`, 420 px wide, with 16 px padding, a 1 px `AppBorderBrush` border and
`CornerRadius` 4, disabled while a refresh runs. With no pen selected it shows only the hint
`PenFormEmptyHint`, centred both ways in `AppSecondaryForegroundBrush`. With a pen selected it shows the
`ScrollViewer` `PenFormScroller` (`Focusable="False"`, no horizontal scrolling), which holds the form:

- The title, a 24 px row of two 16 px `SemiBold` blocks 8 px apart: the id `PenFormTitleId` in
  `AppSecondaryForegroundBrush`, then the name `PenFormTitleName`, trimmed, with the full name as its tooltip.
- Three section headers, Label, Appearance and On start: `TextBlock.form-section`, `SemiBold`, 20 px high,
  16 px above and 8 px below.
- Under each header a grid of the window resource `PenFormColumns` (`x:Shared="False"`): a 112 px label
  column and a 240 px field column, 12 px apart, 8 px between rows, every row 32 px. Each grid is
  `HorizontalAlignment="Left"`: stretched across the scroller, a grid widens both pixel columns by a pixel
  and puts every field 125 px off the label edge.
- Every label is `TextBlock.form-label` in the label column; every field, the two checkboxes included,
  starts at the field column's edge, 124 px right of the label edge, with its width set on itself: Name and
  Line 240 px; Unit, Mask, Colour, Minimum and Maximum 120 px. The colour row is the 120 px `FormColor` box
  and, 8 px right of it, the 32 x 32 px `FormColorPicker` with no padding, its drop-down arrow hidden, so the
  picker is the swatch alone. The mask preview is a trimmed `TextBlock` with its text as a tooltip.
- The On start section holds the visibility box, `FormScale` and the scale type box. `FormScale` is the scale
  pair's focus scope and its own `PenFormColumns` grid of two 32 px rows, Minimum and Maximum, spanning both
  columns of the section's grid; the scale type box is the row under it, outside the scope, on the same field
  edge.
- The reserved `form-message` line, 40 px, 16 px under the last section.

The form's height is fixed by the AXAML: title 24 px; three section headers at 16 + 20 + 8 px; rows
4 x 32 + 3 x 8, 2 x 32 + 8, and 32 + (2 x 32 + 8) + 32 + 2 x 8 px; the message line 16 + 40 px. That is
24 + 132 + 152 + 72 + 152 + 56 = 588 px, inside the pane's 32 px of padding and 2 px of border. At 1272 x 720
px the scroller needs no scrolling; a taller form scrolls rather than clips.

An invalid field takes the `invalid` class and the warning glyph (`#a-form-never-resizes-on-validation`).
The line-style combo box and the checkboxes refuse no value, and `Forms.axaml` styles `invalid` on
`TextBox`, `NumericUpDown` and `CalendarDatePicker` only, not on a `ComboBox` or a `CheckBox`, so a failed
write of either shows on the message line and in the panel only. The scale type box refuses a tick while the
pen stores a minimum at or below zero; the refusal shows on the message line alone, and a minimum that breaks
the rule while the box is ticked marks the minimum `TextBox` `invalid`.

#### What gates the layout

`EveryPenFormLabel_FitsTheLabelColumnInInter` measures each of the form's ten label texts in the Inter font
the app ships, at the label's `FontSize`, through Skia (`UI/InterMeasure.cs`), in `en` and `ru`: each is at
most 112 px. The headless tests measure with the test font, which is about twice as wide, so every other
gate reads the realised layout. The longest form message, `PenFormMaskInvalid`, measured 476.6 px in English
and 480.3 px in Russian on one line on 2026-09-30, so it wraps into the second reserved line; the wrapped
line has not been measured in the pane. Measured on 2026-09-29, with a three-digit added count the bottom bar
needs 472 px in English, 210 px for the count, 12 px between and 250 px for "Refresh pen list", and 640 px in
Russian, 294 + 12 + 334 px for «Обновить список перьев»; both fit the 1260 px minimum width. The side panel's
320 px and the confirmation row's 32 px are chosen, not measured; the confirmation text spans the tab's width
and trims only past it.

| Test in `PenEditorViewTests` | Part it gates |
| --- | --- |
| `TheWindow_OpensOnA1280PxScreenAndAtItsMinimumWidthKeepsThePaneAndEveryColumnMinimum` | The opening width under 1280 px; at `MinWidth` a 420 px pane, every column at or above its minimum and the columns inside the list's viewport |
| `EveryColumn_IsResizableAndOpensNoNarrowerThanItsHeader` | `CanUserResize` on every column; each minimum between its header's unconstrained width and 1 px more |
| `ARow_ShowsThePenAsStoredUnderItsHeader` | The cells as stored, the colour dot inside the name cell, the 28 px row |
| `EveryTextCell_TrimsWithAnEllipsisAndCarriesItsTextAsATooltip` | The trimming and the tooltip of every text cell |
| `AFullCatalogue_RealisesOnlyTheRowsOnScreen` | The virtualised rows |
| `AClickedRow_ShowsTheRowHighlightAndNoCellOutline` | The selection fill and no visible currency rectangle or focus visual |
| `WithNoPenSelected_ThePaneShowsOnlyTheHint`, `AClickedPen_FillsTheFormUnderAOneLineTitle` | The hint and the one-line title |
| `TheForm_HasThreeSectionsOnOneLabelColumnAndOneFieldEdge`, `EveryField_HasTheWidthOfItsContentType`, `TheColourSwatchPicker_IsSquare` | The sections, the 124 px field edge, the field widths, the 32 x 32 px swatch 8 px right of the colour box |
| `ThePane_Stays420PxWideWhenTheWindowWidens` | The fixed pane |
| `AtTheOpeningSize_ThePaneShowsTheWholeForm` | The form's extent inside the scroller's viewport at 1272 x 720 px |
| `AnUnusableMask_WritesNothingRevertsMarksAndSaysWhyWithoutResizing` | The window's and the pane's `Bounds` equal before and after the mask turns invalid |

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
