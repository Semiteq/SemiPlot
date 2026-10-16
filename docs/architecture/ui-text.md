# UI text

Every string the operator reads lives in `SemiPlot/SemiPlot.UI/Localization/Resources.resx` and its
Russian counterpart `Resources.ru.resx`, the startup failure window included. The C# source is plain
ASCII: `.editorconfig:25` gives `[*.cs]` `charset = utf-8`, CI's `format check` step gates the byte
order mark, and the pre-commit hook and CI's `comment gate` step run `terse` over the characters. So
a glyph with no ASCII form is a resource value: the delta labels (`U+0394` plus `t` and `y`), the
no-value placeholder (`U+2014`) and the window title all sit in the resx. The Russian set is
Cyrillic throughout, and the same rule is what keeps it out of the code.

The Russian set calls a pen «перо» (plural «перья») everywhere: the sidebar, the editor, the menu
and the failure texts. «Параметр» is not used for a pen.

## Two sets, one selector

`SemiPlot.UI.csproj:7` sets `<NeutralLanguage>en</NeutralLanguage>`, so the neutral set is English
and lives in the assembly itself. `Localization/Resources.ru.resx` carries the same keys and
compiles to a `ru` satellite; the SDK takes the culture from the file name, so the project needs no
`EmbeddedResource` item for it. Only the neutral set carries the `GenerateSource` item, because one
accessor serves both.

The `locale` key of the `app/` section selects between them. `Startup/StartupSequence.Run` sets
`CultureInfo.DefaultThreadCurrentUICulture` twice, both times through its private `ApplyCulture`: to
`ru` through `ApplyBootstrapCulture` before the settings section is read, and to the configured language
immediately after. Both writes happen before Avalonia is configured, so no window exists on the wrong
language. The argument failure and the log file failure never reach `Run`, so
`Program.ReportStartupFailure` calls `ApplyBootstrapCulture` itself; without that their windows would
fall back to the neutral English set while every other failure window is Russian. `ui-theme.md` owns the other half of the same section, the `theme` key.

`Resources.Culture` stays unassigned. The generated accessor reads
`ResourceManager.GetString(key, Culture)`, and a null `Culture` falls through to
`CultureInfo.CurrentUICulture`, which leaves the thread culture the single writer.

A failure reporting the settings section itself, a failure reporting the launch arguments, and a
log file `LogFileTarget.Prepare` cannot open are all emitted before any `locale` is known, so those
windows are read in the bootstrap language, Russian. The comment above
`ArchiveFailureMapper.MapAppSettings` states the settings case in one line; the other two go through
`Program.ReportStartupFailure`, which applies the culture itself.

Number and timestamp formatting is a separate question and stays on `CultureInfo.CurrentCulture`:
`Chart/ChartHoverReadout` and `Chart/ChartDeltaCursorReader` format what the operator reads on their own
machine, and the generated `Format*` methods pass the same null `Culture` to `string.Format`, so an
argument inside a failure detail follows the machine too. A pen's reading is the one number with a rule
of its own, and `SemiPlot.Core.Trends.PenValueFormat` is that rule's single home: it applies the pen's
stored mask under `CultureInfo.CurrentCulture`, and both the sidebar row and the chart's hover readout
render through it, so one reading never appears in two shapes on one screen.

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
`string.Format(Culture, value, ...)`. `Messages/ArchiveFailureMapper` is written in C# and reads
those: `Resources.FormatFailureArchiveUnreachableDetail(archive)`. A label bound in AXAML keeps
`StringFormat` instead, which is why the property and the `Format*` method both exist for the same
key. `InternalsVisibleTo` carries it into the two test projects and
XamlIl rewrites the same assembly, so the item needs no `Public="true"`. C# reads
`Resources.NoValuePlaceholder`. AXAML declares `xmlns:text="clr-namespace:SemiPlot.UI.Localization"`
on the root element and reads `Content="{x:Static text:Resources.NavigationJumpToNow}"`. `StringFormat`
accepts the same value, so a formatted label keeps its binding rather than growing a view-model
property:

```xml
<TextBlock Text="{Binding RepeatCount, StringFormat={x:Static text:Resources.MessagePanelRepeatFormat}}"/>
```

`NeutralLanguage` is not decoration: without it the analyzer baseline fails the build with `CA1824`.

## Why the generator and not MSBuild

The generator runs inside the compiler, so the accessor exists before XamlIl rewrites the assembly
and `{x:Static}` resolves on a cold build. MSBuild's `StronglyTypedFileName` writes the class between
targets instead: measured on 2026-09-09, a cold build fails with
`AVLN2000: Unable to resolve "Resources.NavigationJumpToNow"` and only the second build passes.

## What stays a literal

- The `h`, `m` and `s` suffixes `Chart/ChartDeltaCursorReader.FormatDeltaTime` glues to its
  numbers. A unit suffix glued to a number is part of the format, not a caption.
- The CSV export's file format, which belongs to the file and not to the window: the non-finite spellings
  `NaN`, `Infinity` and `-Infinity` (`ArchiveCsvWriter`), and the time boxes' format `HH:mm:ss`
  (`WallClockRangeForm.TimeFieldFormat`). The header names follow the interface language; the delimiter, the decimal separator and the
  timestamp's millisecond separator follow the Windows regional settings (`#the-export-windows-text`).
- The time window panel's field formats: the time boxes' `HH:mm:ss` (`WallClockRangeForm.TimeFieldFormat`,
  the export's constant) and the width box's `h:mm:ss` (`WindowWidthText.FieldFormat`). Both are typed and
  read with the invariant culture, and the messages take them as `{0}` (`#the-time-window-panels-text`).
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
  repository, and neither has a Russian form to give. Every mapped arm above them is resourced. So a
  programming error reads as English developer text in a Russian window, the message panel included.
  That is the trade taken for an error class the operator cannot act on: the alternative is a
  sentence that says nothing, and the log carries the stack either way. It also widens what the
  window shows: the text of these arms stands on screen as well as in the log, so a driver-level
  exception over a malformed connection string can echo the string it was handed. The connection
  file's own arms name host, port, database and role and never the password.

## What the mapper's two consumers read

`Messages/ArchiveFailureMapper.Map` is the one place operator-facing failure text is written, and two
surfaces read it: the startup-failure window, which shows a single `ArchiveFailureView`, and the
message panel, which keeps a list of them. `Map` yields the title, the detail and the remedy
separately, and the panel shows all three. The coalescing key is the whole `ArchiveFailureView`:
`MessagePanelViewModel.Report` compares the incoming view with the newest entry by record equality,
so two failures sharing a title but differing in detail stay two entries. The pen editor's message
lines read the title alone, beside the panel entry the same failure adds
(`#the-pen-editors-text`).

Because both surfaces read the same arms, no title may name a phase: a mid-session ReactiveUI exception
reaches the panel under the same title as a startup one. `FailureThrownTitle` reads "SemiPlot failed
unexpectedly" / "Произошла непредвиденная ошибка", and its detail names the exception rather than the
startup sequence.

The aggregation layer is named the same way. `AppStatusBarViewModel.LayerNameOf` switches each
`AggregationLayer` member onto a resx key and the bar reads `StatusLayerFormat` around it, so the
Russian window reads "Слой: сырой" rather than the `ToString()` of the enum. A switch, not a table,
and the name is read at each call so the window's language decides. Its `_ => throw` arm suppresses
CS8509, so no compiler check stands behind it: a fourth `AggregationLayer` member turns
`Core/Trends/AggregationLayerTests.LayerCodes_KeepTheirOrdinalContract` red, which asserts
`Enum.GetValues<AggregationLayer>()` against the three members it names.
`ArchiveFailureMapper.NameOf` for `StartupRead` is the same shape.

## The message panel's text

`Messages/MessagePanelView.axaml` reads the panel's own strings; each entry's title, detail and remedy
come from the mapper (`#what-the-mappers-two-consumers-read`):

| Key | English / Russian | Where |
| --- | --- | --- |
| `MessagePanelTitle`, `MessagePanelClear` | "Messages" / "Сообщения", "Clear" / "Очистить" | The panel's header and its clear button |
| `MessagePanelEmpty` | "No messages" / "Сообщений нет" | Under the header while the panel holds no entry |
| `MessagePanelRepeatFormat` | "repeated {0} times" / "повторений: {0}" | Beside an entry that repeated, `{0}` the repeat count, through `StringFormat` |

## The navigation bar's text

`Navigation/NavigationBarView.axaml` reads the bar's three buttons:

| Key | English / Russian | Where |
| --- | --- | --- |
| `NavigationJumpToNow` | "Jump to now" / "Перейти к текущему времени" | The jump button |
| `NavigationSticky`, `NavigationDelta` | "Sticky" / "Следовать за текущим временем", "Delta" / "Приращение" | The two toggle buttons |

## The sidebar's text

`Legend/TrendLegendView.axaml` reads the sidebar's strings through `TrendLegendViewModel` and
`TrendLegendGroupViewModel`. Pen and group names are data and pass through no resource.

| Key | English / Russian | Where |
| --- | --- | --- |
| `LegendCollapsePanel`, `LegendExpandPanel` | "Collapse" / "Свернуть", "Expand" / "Развернуть" | The panel's toggle button, its text and accessible name (`TrendLegendViewModel.ToggleText`) |
| `LegendUngroupedHeader` | "Ungrouped" / "Без группы" | The last group's header, over the pens that belong to no group, in capitals through `LegendConverters.ToCapitals`; absent while no pen has a group |
| `LegendGroupSwitch` | "Show or hide every pen of group {0}" / "Показать или скрыть все перья группы {0}" | The accessible name of a group header's check box, `{0}` the group name (`TrendLegendGroupViewModel.SwitchName`) |

## The settings window's text

The `Edit` menu is `MenuEdit` with two items, `MenuEditSettings` and `MenuEditPensAndGroups`
(`#the-pen-editors-text`). `Settings/SettingsDialog.axaml` reads `SettingsTitle`, the two section headers
`SettingsInterfaceHeader` and `SettingsConnectionHeader`, one `Settings*Label` per field, `SettingsSave`
and `SettingsClose`. The language and theme choices read `SettingsLanguageRussian`,
`SettingsLanguageEnglish`, `SettingsThemeLight` and `SettingsThemeDark`: the combo box shows the label
and the file keeps the token `SettingsVocabulary` pairs with it. The dialog's message line, left of the
buttons, shows `SettingsSectionUnreadable` with the section header as `{0}` when a section fails to read,
else `SettingsKeyAbsent` with the first edited key no file of its section carries as `{0}`, else the rule
the first invalid field breaks, in form order: `SettingsFieldRequired` for an unchosen language or theme,
`SettingsHostInvalid`, `SettingsPortInvalid`, `SettingsFieldRequired` for a blank database, user or
password, and `SettingsPollIntervalInvalid`. `SettingsFieldRequired` takes the field's label as `{0}`.
`SettingsViewModel.ValidationMessage` picks it. The line is not under the field, so each message opens with the field it names, and each is one
short line in both languages ("Host: expected an IP address such as 127.0.0.1");
`ui-theme.md#a-form-never-resizes-on-validation` holds the width it has to fit. `SettingsRestartNotice`
is the dialog's own text after a save that changed any key but `theme`, on the same line while no field is invalid;
`SettingsRestartNoticeThemeApplied` replaces it when that save also changed `theme`, which already applies,
and `SettingsRestartNow` ("Restart now", «Перезапустить») is the button beside it. It is not a panel
entry, because the panel carries failures. `MenuFileNewWindow` names the `File` item that starts another
window, and `StartupFailureRestart` the failure window's button; `StartupFailureRestart` and
`SettingsRestartNow` share «Перезапустить», one term for one action. `FailureInstanceHostUnknownTitle`,
`...Detail` and `...Remedy` are the text of a copy that cannot start because the running process names no
executable.

A refused save reads the same text as the startup failure for the same value, because both go through
`ArchiveFailureMapper.Map`. The save adds two `SectionProblem` arms, each with a detail and a remedy
and the section's existing title: `FailureConfigurationSectionUnwritableDetail` and `...Remedy` for a
file or staging folder the save cannot write, and `FailureConfigurationSectionKeyAbsentDetail` and
`...Remedy` for an edited key no file of the folder carries. A refused connection folder reads one detail per `ConnectionFileProblem`:
`FailureConnectionFileUnparseableDetail` (path), `FailureConnectionFileMissingFieldDetail` and
`FailureConnectionFileOutOfRangeDetail` (path, comma-joined field names) and
`FailureConnectionFileHostNotIPv4Detail` (path, the host field name), each with the remedy of the same arm
(`FailureConnectionFileHostNotIPv4Remedy` for `HostNotIPv4`). `FailureConnectionFileOutOfRangeRemedy` takes
the three `PostgresConnectionLoader` range constants (lowest port, highest port, lowest poll interval).
`ArchiveFailureMapper` therefore references `SemiPlot.DataSource.Postgres`, the only provider; a second
provider with its own bounds moves them onto `ConnectionFileError`. `ConnectionFileError` carries field names,
never prose, so the detail follows the UI language. A refusal from a loader names the
operator's section folder, never the staging copy it read; a staging folder that cannot be created is
the one detail naming `<config-dir>` and the staging folder (`overview.md#the-settings-window`).

## The pen editor's text

`MenuEditPensAndGroups` is the `Edit` menu's second item, "Pens and groups" / "Перья и группы", and
carries no ellipsis, as `MenuEditSettings` carries none. `PenEditor/PenEditorWindow.axaml` reads
`PenEditorTitle`, the tab headers `PenEditorPensTab` and `PenEditorGroupsTab`, and the eleven column
headers `PenEditorColumn*`. The form's labels reuse the column-header keys, one term per concept; its one
label of its own is `PenFormMaskPreviewLabel`. The line-style cell and the combo box read
`PenLineStyleInterpolated` and `PenLineStyleStepped` through `PenLineStyleConverters.ToLabel`, a
`FuncValueConverter` consumed through `{x:Static}`. The visibility column and field read
`PenEditorColumnOnStart`, "On start" / "При запуске", because the flag is the visibility a pen starts
with and a change of it switches nothing on a running chart. The scale columns and fields read
`PenEditorColumnScaleMinOnStart` and `PenEditorColumnScaleMaxOnStart`, "Initial scale, min" / "Начальная
шкала, от" and "Initial scale, max" / "Начальная шкала, до", because the stored pair is the pen's initial
scale, the term the axis scale panel's button `AxisScaleInitialScale` ("Restore initial scale" / "Вернуть
начальную") and the `View` menu item `MenuViewInitialScale` ("Restore initial scale" / "Вернуть начальную
шкалу") use: it applies when the pen enters a chart and on that command, and it changes no pen already
shown. The three scale messages open with the same term, "Initial scale:" / "Начальная шкала:". The log
column and field read `PenEditorColumnLogScaleOnStart`, "Initial scale, logarithmic" / "Начальная
шкала, логарифмическая": the flag is a start value like the pair, so it takes the pair's term. The
label names the scale type the way Excel, MasterSCADA 4D and Simple-Scada 2 do and leaves the base out;
the chart builds base 10 only (`charting.md#log10-y-axis`).

The bottom bar reads the button `PenEditorRefresh`, "Refresh pen list" / «Обновить список перьев», which
names what it refreshes, and `PenEditorAddedCount`, "Pens added: {0}" / "Добавлено перьев: {0}", empty
until the first successful refresh. It carries no notice: every running chart follows what the editor
writes through the catalogue read (`overview.md#the-live-catalogue`), except the scale pair and the log
scale flag, which change no pen already shown (`overview.md#what-a-read-changes`), so there is nothing to
wait for. The `Groups` tab reads `PenGroupsNewNamePlaceholder`, one of the tree's two watermarks beside
the marker label editor's `MarkerLabelPlaceholder` (`#the-marker-menu-and-label-editors-text`),
`PenGroupsCreate`, `PenGroupsNameLabel`, `PenGroupsDelete`, which is also the confirm button's caption,
`PenGroupsCancel` and `PenGroupsMembersHeader`. `PenGroupsDeleteConfirmation` takes the group name as
`{0}` and its member count as `{1}`, and is a snapshot taken when delete is pressed. The added count and
the confirmation put the counted noun first in both languages, "Pens in the group: {1}" / "Перьев в
группе: {1}", so neither needs a plural form.

The pen form's message line shows the first rule a draft breaks, in form order: `PenFormNameRequired`,
`PenFormMaskInvalid`, `PenFormColorInvalid`, `PenFormScaleBoundInvalid`, `PenFormScaleHalfSet`,
`PenFormScaleInverted` and `ScaleLogMinimumPositive`. The `Groups` tab's line shows
`PenGroupsNameRequired`. The line is not under the field, so each message opens with the field it names,
as the settings messages do. `ScaleLogMinimumPositive`, "A logarithmic scale needs a minimum above zero"
/ "Для логарифмической шкалы минимум должен быть больше нуля", is the one exception: the
axis scale panel shows the same rule with the same key, so it names the rule rather than a form field.
The form shows it for a scale draft with a minimum at or below zero while the flag is ticked, and as the
refusal of a tick while the pen stores such a minimum. With every draft valid, a line shows the last
refusal, and for a failed write that text is the title `ArchiveFailureMapper.Map` gives the error: the
mapper stays the one owner of failure wording, and the full entry goes to the message panel.

`EmptyCatalogueMessage`, the chart area's text over an empty catalogue, names the way in: open the
editor, press "Refresh pen list", then name and switch on the pens. It is a
format over the three labels the operator clicks: `{0}` is `MenuEdit`, `{1}` `MenuEditPensAndGroups`
and `{2}` `PenEditorRefresh`. The text withdraws when a catalogue read gives the chart its first
pens, in the same session (`TrendChartViewModel.HasNoPens`). `Chart/TrendChartView.axaml` reads it through a `MultiBinding` whose
`StringFormat` is `{x:Static text:Resources.EmptyCatalogueMessage}` over three `x:Static` sources, the
AXAML form of the generated `Resources.FormatEmptyCatalogueMessage` accessor, so a relabelled menu item or button changes the sentence
with it. `TrendChartViewTests.EmptyCatalogueMessage_ShowsWithNoPensAndWithdrawsWhenOneArrives` compares
the realised text with `Resources.FormatEmptyCatalogueMessage(...)` over the three labels.

### The pen editor's failure text

The editor adds three `ArchiveFault` kinds, each with a title, a detail and a remedy in both sets:
`FailureArchiveValueRejected*`, `FailureArchiveNameTaken*` and `FailureArchiveRowGone*`. Each detail
wraps `{0}`, the archive, and `{1}`, the pen or group name `ArchiveError.Detail` carries, so no word of
either language lives in `Detail`. Each is a `Warning`, because the operator's next edit is the remedy.

The marker store adds a fourth write kind, `FailureArchiveMarkerGone*` ("The marker no longer exists" /
"Метка больше не существует"), with a title, a detail and a remedy in both sets. Its detail wraps `{0}`,
the archive, and `{1}`, the marker's label or its local moment, which `ArchiveError.Detail` carries
(`data-integration.md#the-marker-store`). The remedy names the cause, another window or an administrator
removed the marker, says that the chart no longer shows it and asks for it to be added again if it is
still needed. It is a `Warning`. The Russian term for a time marker is "метка" in every key, and
"подпись" for its label.

The kinds a write shares with a read name a statement, never a read, because the same text serves
both. The shared keys are `FailureArchiveAccessDeniedTitle` and `...Remedy`,
`FailureArchiveTableMissingDetail` and `...Remedy`, `FailureArchiveQueryTimedOutTitle`, `...Detail` and
`...Remedy`, `FailureArchiveReadFailedTitle` and `...Detail`, and `FailureArchiveReadUnnamedDetail`.
The `TableMissing` pair carries no word "table", because a missing function takes the same arm. Each
Russian value uses the placeholder indices of the neutral one, which
`ResourcesTests.EveryRussianValue_UsesThePlaceholderIndicesOfTheNeutralOne` gates, and the timed-out
detail carries `(SQLSTATE 57014)`, which `ArchiveFailureMapperTests` asserts. `ArchiveError.Describe`, the
English log line, names a statement the same way (`data-integration.md#two-error-planes`).

## The axis scale panel's text

`Chart/AxisScalePanel` reads `AxisScaleMaximumLabel` and `AxisScaleMinimumLabel` ("Maximum" / "Максимум",
"Minimum" / "Минимум") and `AxisScaleInitialScale`; it has no apply button, because the pair is written
when its edit ends (`trend-interaction.md#the-axis-scale-panel`). Its Autoscale button reads
`MenuViewAutoscale`, the same key as the menu item, so one word names the command in both places. The
pen's name and unit are data, shown in two `TextBlock`s, so no format key joins them.
The message line shows `AxisScaleBoundInvalid` ("Enter a number in each field" / "Введите в каждом поле
число") for a field that is not a number in the operator's regional format, `AxisScaleBoundsRequired`
("Enter both bounds" / "Укажите обе границы") for an empty field and `AxisScaleMinimumBelowMaximum` ("The
minimum must be below the maximum" / "Минимум должен быть меньше максимума") for an inverted or equal
pair. The checkbox under Minimum reads `AxisScaleLogarithmic` ("Logarithmic scale" / "Логарифмическая
шкала"), the scale type in the industry form the pen editor's log column uses, and
`ScaleLogMinimumPositive`, the pen form's key, fills the message line for a minimum at or below zero on a
logarithmic axis and for a tick the chart refuses over a manual minimum at or below zero. A tick refused
over a pair that does not read shows that pair's rule instead. The Russian rule wraps to two lines in the
panel, which its reserved line holds (`ui-theme.md#the-axis-scale-panel`). The two fields are `TextBox`es
seeded through `PenValueFormat.Format` with the pen's mask and read through `PenFormRules.TryReadBound`,
the pen editor's rule, so the decimal separator is the current culture's and "150.5" under `ru-RU` is
refused, never read as 150. A field left as seeded keeps the exact bound.

The View menu's submenu header is `MenuViewPenScale` ("Pen scale" / "Шкала пера") while no pen's axis is
drawn and the format `MenuViewPenScaleFormat` ("Pen scale: {0}" / "Шкала пера «{0}»") over the drawn pen's
name otherwise. `MainWindowViewModel.PenScaleHeader` derives it; `AppMenuBarTests` covers the header with a
pen, without one, with every pen hidden and after a rename.

## The export window's text

`MenuFileExport` ("Export to CSV..." / "Экспорт в CSV...") is the `File` item between `New window` and
`Exit`. `Export/CsvExportWindow.axaml` reads `ExportWindowTitle`, `ExportStartLabel`, `ExportEndLabel`,
`ExportDateFormat`, `ExportPensHeader`, `ExportSaveAs`, `ExportCancel` and `ExportClose`; the code-behind
reads `ExportWindowTitle` and `ExportFileTypeName` for the save dialog's title and filter. The row count
below the checklist is `ExportCountRows` ("{0} rows" / "Строк: {0}") or `ExportCountUnavailable`.
`ExportPensHeader` is a format, "Pens ({0} of {1})" / "Перья ({0} из {1})", filled by a `MultiBinding` over the
ticked and total pen counts; `CsvExportWindowTests` compares it with `Resources.FormatExportPensHeader`.

`ExportDateFormat` is `dd.MM.yyyy` in Russian and `yyyy-MM-dd` in English, the date half of the file
timestamp. Both are digits and literal separators, so the machine's culture decides neither their shape nor
the parse of the picker's text; the shape follows the interface language. `CalendarDatePicker` runs with
`SelectedDateFormat="Custom"` and this string as `CustomDateFormatString`. The picker's popup month names and
first day of week still follow `CultureInfo.CurrentCulture`, the machine's, as every other date and number on
screen does (`#two-sets-one-selector`): setting `CurrentCulture` from the interface language would move the decimal separator of every pen
reading and of the axis panel's bound parse.

The message line shows one of these, in this order: the first broken form rule, the export's status, the
Excel notice.

| Key | Shown when |
| --- | --- |
| `ExportMessageStartDateMissing`, `ExportMessageEndDateMissing` | A date picker holds no date. |
| `ExportMessageStartUnreadable`, `ExportMessageEndUnreadable` | A time box does not read as `WallClockRangeForm.TimeFieldFormat`; the text takes the format as `{0}`. |
| `ExportMessageEndNotAfterStart`, `ExportMessageSpanOverLimit`, `ExportMessageNoPenTicked` | The end is not after the start, the range is over `ExportRangeForm.MaxSpan` (`{0}` hours), no pen is ticked. |
| `ExportProgress`, `ExportProgressUnknownTotal`, `ExportSuccess`, `ExportCancelled` | An export runs ("Written {0} of {1}", or "{0} rows written" while no count has landed; the total never falls below the rows written), ends ({0} rows, `{1}` the file name; the full path is the line's tooltip), or is cancelled. |
| `ExportExcelNotice` | The count is above `ExportMessageLine.ExcelDataRowLimit` (1 048 575), the data rows Excel opens; the text takes the limit as `{0}`, grouped in the current culture. |

A failed export shows the title the mapper assigns in the same line. `ExportFileError` has its own arm in
`ArchiveFailureMapper.Map`: `FailureExportFileTitle`, `FailureExportFileDetail` (path, reason) and
`FailureExportFileRemedy`. Its reason is the runtime's own `IOException` or `UnauthorizedAccessException`
message, which no resource translates, as `#what-stays-a-literal` records for the mapper's last arms; for a
partial file that cannot be deleted `ExportFileError.IsPartialFile` is set and the mapper puts the message inside
`ExportPartialNotRemoved`. The message
line takes the `error` class while a form rule is broken or the last export failed.

The CSV dialect has two sources. The header follows `AppSettings.Locale`: `ExportCsvHeaderPen`,
`ExportCsvHeaderTime` and `ExportCsvHeaderValue` ("Pen", "Time", "Value" / "Перо", "Время", "Значение"),
read by `App.CsvDialectFor` with `Resources.ResourceManager.GetString(key, entry.UiCulture)` for the
interface language whatever the thread's UI culture is. The delimiter, the decimal sign and the timestamp's
millisecond separator follow the Windows regional settings, because Excel opens a double-clicked CSV with the
regional list separator and decimal sign, not with the program's language. `CsvDialect.For(CultureInfo.CurrentCulture, header)`
reads them once at start, when `App.CsvDialectFor` builds the dialect: the delimiter is
`TextInfo.ListSeparator`, the decimal sign is `NumberFormat.NumberDecimalSeparator`, the timestamp is
`yyyy-MM-dd HH:mm:ss` + decimal sign + `fff`, and numbers are written in "R" format by the invariant
culture with only the decimal separator replaced. A decimal sign other than `.` or `,` becomes `.`. A list
separator is kept only when it is `;`, `,`, a tab or `|` and differs from the decimal sign; any other
(a space, `-`, `:`, a digit, a letter, `"`, CR, LF, or more than one character) becomes `;` when the
decimal sign is `,` and `,` otherwise, because the timestamp and the value are written unquoted
(`CsvDialectTests`). A change of the language applies at the next
start, as every other text does. The save dialog's suggested name is
`semiplot_yyyyMMdd_HHmmss-yyyyMMdd_HHmmss.csv` with invariant digits in both languages.

## The time window panel's text

`Chart/TimeWindowPanel` reads `TimeWindowStartLabel`, `TimeWindowEndLabel` and `TimeWindowWidthLabel`
("Start" / "Начало", "End" / "Конец", "Width" / "Ширина") for its three rows and `TimeWindowPresets`
("Presets" / "Быстрый выбор") for the presets button. The menu items read `TimeWindowPresetOneMinute`,
`TimeWindowPresetTwoMinutes`, `TimeWindowPresetThreeMinutes`, `TimeWindowPresetFiveMinutes`,
`TimeWindowPresetTenMinutes`, `TimeWindowPresetThirtyMinutes` and `TimeWindowPresetOneHour` ("1 min" /
"1 мин" .. "1 h" / "1 ч"), through `TimeWindowPanelViewModel.Presets`. The two date pickers reuse
`ExportDateFormat` (`#the-export-windows-text`), so a date has one shape per language in both windows.

The message line shows the first rule the form breaks, the edges' rules ahead of the width's
(`trend-interaction.md#the-time-window-panel`), from `TimeWindowForm.Message`:

| Key | Shown when |
| --- | --- |
| `TimeWindowMessageStartDateMissing` | The start picker holds no date. |
| `TimeWindowMessageStartUnreadable` | The start time box does not read as `WallClockRangeForm.TimeFieldFormat`; the text takes the format as `{0}`. |
| `TimeWindowMessageEndDateMissing` | The end picker holds no date. |
| `TimeWindowMessageEndUnreadable` | The end time box does not read as `WallClockRangeForm.TimeFieldFormat`; the text takes the format as `{0}`. |
| `TimeWindowMessageEndNotAfterStart` | The end is not after the start. |
| `TimeWindowMessageTimeSkipped` | An edge is a local time the spring-forward change skips. |
| `TimeWindowMessageSpanOverLimit` | The edges are more than `TrendNavigationModel.MaximumWidth` apart; the text takes its days as `{0}`. |
| `TimeWindowMessageWidthUnreadable` | The width box does not read as `WindowWidthText.FieldFormat`; the text takes the format as `{0}`. |
| `TimeWindowMessageWidthOutOfRange` | The width is outside `TrendNavigationModel.MinimumWidth` .. `MaximumWidth`; the text takes both, formatted by `WindowWidthText.Format`, as `{0}` and `{1}`. |

The line always carries the `error` class, because it shows nothing but a broken rule. A failed day read
reaches the message panel, not this line.

## The marker menu and label editor's text

`Chart/TrendChartView.axaml` reads every string of the time markers
(`trend-interaction.md#the-time-markers`):

| Key | English / Russian | Where |
| --- | --- | --- |
| `MarkerMenuAdd` | "Add marker here" / "Добавить метку здесь" | The chart menu, off a marker |
| `MarkerMenuRename`, `MarkerMenuDelete` | "Rename" / "Переименовать", "Delete" / "Удалить" | The marker menu |
| `MarkerLabelPlaceholder` | "Label (optional)" / "Подпись (необязательно)" | The label box's watermark |
| `MarkerLabelOk`, `MarkerLabelCancel` | "OK" / "ОК", "Cancel" / "Отмена" | The editor's buttons |

The editor has no message line: nothing in it can be invalid, and the watermark says that an empty box
is allowed (`ui-theme.md#the-marker-label-editor`). A failed marker write reaches the message panel,
never the editor, which is closed by then. A label is operator data and passes through no resource.

## The About dialog's text

`MainWindow/AboutDialog.axaml` reads the dialog's strings. It opens from `MenuHelpAbout` and from the
startup failure window's `StartupFailureAbout`; its first line is `WindowTitle`, and the version and the
configuration directory are data (`MainWindow/AboutInfo`).

| Key | English / Russian | Where |
| --- | --- | --- |
| `AboutTitle` | "About SemiPlot" / "О программе SemiPlot" | The window title |
| `AboutVersionLabel`, `AboutConfigurationLabel` | "Version:" / "Версия:", "Configuration:" / "Настройки:" | The labels left of the version and the configuration directory |
| `AboutConfigurationUnknown` | "unknown" / "не определены" | In place of the directory when the launch arguments do not parse (`AboutInfo.For`) |
| `AboutClose` | "Close" / "Закрыть" | The close button |

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
   ordinary key needs no test of its own. A key whose value carries a glyph gets one assertion in
   `GlyphValues_KeepTheCodePointsTheOperatorSees`. A placeholder needs none of its own:
   `EveryRussianValue_UsesThePlaceholderIndicesOfTheNeutralOne` compares the `{N}` indices of every
   key across the two sets.

Both resx files are hand-edited. An IDE resx editor rewrites the header, adds the `xsd:schema` block
and a byte order mark; revert that.

A test that writes `CultureInfo.CurrentUICulture` or
`CultureInfo.DefaultThreadCurrentUICulture` joins `ProcessGlobalStateCollection` and restores the
previous value in a `finally`, and so does a test that **reads** a `Resources` value, because a write
landing between the production read and the test's own read is an intermittent failure. The culture is
process-global and the project runs its other classes in parallel.

A glyph with no ASCII form belongs in the resx and nowhere else, never as a `\u` escape in
production code: the escape hides the string from whoever reads the code, and `terse` does not read
`.resx`. A test file may spell the code points it pins as escapes, so the test file stays ASCII.
