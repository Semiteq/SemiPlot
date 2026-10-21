# OPC UA block in the settings window

## Overview

- The settings window edits the `opc/` section: an OPC UA block after the database block, in the same
  window, with the fields `host` and `port`. The operator no longer edits `opc/opc.yaml` by hand.
- The block follows the rules the window already applies to `app/` and `connection/`: it edits only keys
  that already exist, each in the file that owns it, and only the keys the operator changed. The
  production loader `OpcUaConnectionLoader` validates a staged copy before any file is replaced, and a
  saved change takes effect at the next start.
- `host` follows `NetworkAddress.IsIPv4`, `port` the shared bounds `NetworkAddress.LowestPort` to
  `NetworkAddress.HighestPort`, as the `connection/` fields do.
- The startup-failure window offers Settings only when all three sections read and carry every key the
  dialog edits. Today it reads two: an `opc/` folder that is missing, holds no `*.yaml`, holds an
  unreadable file or lacks a key still offers a dialog that cannot fix it
  (`SemiPlot.UI/Startup/StartupFailureViewModel.cs:36`, `:99-104`). This slice closes that gap.
- Roadmap thesis, for orientation only: while the OPC UA session is up, every point past a pen's newest
  archived row comes from OPC UA; the archive replaces a live point only when a history read covers it.
  This slice touches no part of that path.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261019-opc-live-edge-roadmap.md — slice opc-settings

- Base: master `5f8ea2c` (#145, slice `opc-live-source`), whose tree is identical to `c9201ef`. Every
  citation below is read there.
- The `opc/` section as it stands:
  - `ConfigFiles/opc/opc.yaml` carries `host: 127.0.0.1` and `port: 49875`.
  - `SemiPlot.DataSource.OpcUa/Configuration/OpcUaConnectionLoader.cs:21-60`: `HostKey` and `PortKey` are
    `ConnectionFile.HostKey` and `ConnectionFile.PortKey` (`:23`, `:25`); `Load` reads the folder with
    `ConfigurationSection.Read` (`:33`) and checks it with `ConnectionFile.Validate` (`:48`). Every failure
    is a `ConnectionFileError` or a `ConfigurationSectionError` of section `Opc` (`:16-20`).
  - The certificate store root is `<opc dir>/pki` (`:27`, `:58`); the loader only computes the path.
  - `SemiPlot.Core/Configuration/ConfigurationSectionError.cs:6-11`: `ConfigurationSectionName` has `App`,
    `Connection`, `Opc`.
  - `SemiPlot.UI/Startup/StartupProbe.cs:25` declares `OpcDirectoryName = "opc"`.
  - `SemiPlot.Core/Configuration/ConnectionFile.cs:13`, `:15`: `HostKey = "host"`, `PortKey = "port"`, shared
    by both loaders; `PostgresConnectionLoader.cs:33`, `:35` alias the same constants.
  - `SemiPlot.Core/Configuration/NetworkAddress.cs:8`, `:10`, `:17`: `LowestPort`, `HighestPort`, `IsIPv4`.
- The settings window:
  - `SemiPlot.UI/Settings/SettingsSave.cs:28-38`: `_sections` holds two `SectionLoader`s, `App` and
    `Connection`. `ReadOwned` returns a two-element tuple (`:41-49`), and its summary says "Both sections"
    (`:40`). `FirstAbsentKey(app, connection)` returns a bare key (`:52-58`). `StageAndPromote` (`:103`),
    `CreateStagingDirectory` (`:244`) and `SectionDirectory` (`:62`) iterate `_sections`. `InRealDirectory`
    (`:155-169`) rebuilds `ConfigurationSectionError`, `AppSettingsError` and `ConnectionFileError`, the last
    keeping `file.Section` (`:163-164`), so an `Opc` refusal already maps to the real `opc/` folder.
  - `SemiPlot.Core/Configuration/ConfigurationSectionWriter.cs:91` lists only top-level `*.yaml` files of a
    section, so staging never copies or touches `opc/pki`.
  - `SemiPlot.UI/Settings/SettingsViewModel.cs` (470 lines): the constructor (`:38-92`, 55 lines) takes `app`
    and `connection` results; `AppKeys` and `ConnectionKeys` (`:95-108`); `Host`, `Port` and their
    `Is*Valid` (`:139-160`, `:195-197`); `canSave` over seven `WhenAnyValue` inputs (`:69-85`);
    `FirstBrokenRule` in form order (`:305-345`); `SaveAsync` builds the per-section edit map and sets
    `IsRestartPending` when a key other than `theme` changed (`:271-303`); `RefusalOf` (`:389-404`); `PortOf`
    reads `PostgresConnectionLoader.PortKey` (`:418-427`).
  - `SemiPlot.UI/Settings/SettingsDialog.axaml`: width 640, `SizeToContent="Height"` (`:12-13`); the form
    grid has nine `Auto` rows (`:31`); the connection header row carries `Margin="0,10,0,0"` (`:66-70`); each
    field binds `Classes.invalid` to its `Is*Valid` (`:80`, `:92`); the message line is
    `SettingsValidationMessage` (`:143-146`).
  - `SemiPlot.UI/Startup/StartupFailureViewModel.cs:36` sets `OffersSettings` from `EverySavedKeyPresent`,
    which reads `app` and `connection` only (`:99-104`); its summary says "both configuration sections"
    (`:61-64`).
  - Callers of `SettingsSave.ReadOwned`: `SettingsViewModel.OpenAsync` (`SettingsViewModel.cs:236`),
    `StartupFailureViewModel.EverySavedKeyPresent` (`:101`), `SettingsViewModelStand.Build`
    (`Tests.Unit/UI/Settings/SettingsViewModelStand.cs:16`). `MainWindowViewModel.cs:218` reaches the dialog
    through `OpenAsync` only.
- Text: `Resources.resx:621-680` holds the `Settings*` keys. `SettingsHostInvalid` reads "Host: expected an
  IP address such as 127.0.0.1" (`:666-668`), `SettingsPortInvalid` "Port: a whole number 1–65535"
  (`:669-671`), `SettingsKeyAbsent` "Key '{0}': no file of its section carries it. Add it to a file by
  hand." (`:678-680`). The OPC failure titles name the section "OPC UA connection settings" /
  "подключения к серверу OPC UA" (`Resources.resx:279-280`, `Resources.ru.resx:279-280`).
  `ResourcesTests` pins composite placeholders in `CompositeFormats_KeepTheirPlaceholder` (`:78-90`),
  requires every neutral key in the Russian set (`:93`), a Russian value that differs (`:112`) and the
  same placeholder indices (`:132`).
- Layout rule: `docs/architecture/ui-theme.md:176-193` (a form never resizes on validation; one message
  line, two reserved lines, wrapped, trimmed past the second), `:205-214` (the 640 px width and the bounds
  gate `SettingsViewTests.TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`) and `:216-231` (the
  dated Skia and HarfBuzz measurements; the message cell is 457 px in English and 388 px in Russian; the
  headless test font is about twice as wide, so no test asserts trimming; a longer label or message is
  measured in both languages before it ships).
- Tests that pin the surface: `SettingsSaveTests`, `SettingsViewModelTests` (fields `_appDirectory`,
  `_connectionDirectory`, `:27-37`; `AMissingSectionFolderIsReported...(bool appAbsent, bool
  connectionAbsent)`, `:145-167`; `ChangingOnlyTheTheme_LeavesTheConnectionFileByteIdentical...`, `:213`),
  `SettingsViewTests`, `StartupFailureWindowTests` (`TheSettingsButton_IsHiddenWhenASectionCannotSave`,
  `:92-123`), and the helpers `SettingsSaveSandbox` (`DirectoryOf` is a two-way ternary, `:62-65`),
  `SettingsViewModelStand`, `ShippedConfiguration` (`CopyTo` copies every tracked file, `opc/` included,
  `:21-34`).
- Docs that state the window does not edit `opc/`: `AGENTS.md:33-39`, `docs/architecture/overview.md:430-432`
  and `:941`, `docs/architecture/data-integration.md:1774-1775`, `readme.md:74-76`. Docs that count two
  sections: `overview.md:206`, `:454-458`, `:460-462`, `:478`, `:485`; `data-integration.md:1821`;
  `charting.md:648-649`; `ui-text.md:203-212`.

## Development Approach

- **testing approach**: Regular (code first, then tests), as the settings window's existing suites are
  written.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
  - tests are not optional - they are a required part of the checklist
  - write unit tests for new functions/methods
  - write unit tests for modified functions/methods
  - add new test cases for new code paths
  - update existing test cases if behavior changes
  - tests cover both success and error scenarios
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change
- maintain backward compatibility: an installed `opc/opc.yaml` loads unchanged, and a save rewrites only
  the file that owns an edited key

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` only. Every new test opens no socket and starts no container.
  View-model and view tests are `[AvaloniaFact]`/`[AvaloniaTheory]`; `SettingsSaveTests` stays plain
  `[Fact]`/`[Theory]`. Every new test is a member of an existing class, which already carries the three
  traits.
- **headless UI tests** stand in for e2e: every click, clear and typed text goes through
  `HeadlessInput` (`Click`, `Clear`, `Type`, `Press`), per `AGENTS.md`.
- Gate: `dotnet build SemiPlot.slnx` (warnings are errors) and
  `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`. The integration project is not
  touched and runs only in Task 3.

## Acceptance Evidence

The defect is reproducible from code, so the evidence is headless tests.

1. The failure-window gap: `EverySavedKeyPresent` never reads `opc/` (`StartupFailureViewModel.cs:101-103`),
   so with the copied `opc/` folder deleted `OffersSettings` is true. The new `opc-missing-directory` case of
   `StartupFailureWindowTests.TheSettingsButton_IsHiddenWhenASectionCannotSave` asserts it is false. Task 1
   runs that case red before the `StartupFailureViewModel` edit and green after it.
2. The fix end to end: `StartupFailureWindowTests.TheSettingsDialog_OverARefusedOpcPort_WritesTheFixedPortIntoTheOpcFile`
   (Task 2) writes `port: 70000` into `opc/opc.yaml` of a shipped copy with the password filled, builds the
   failure view model, clicks `StartupFailureSettings`, finds `SettingsOpcPort` empty with the `invalid`
   class and Save disabled, types `49875`, clicks Save, and asserts
   `OpcUaConnectionLoader.Load(<copy>/opc)` succeeds with port 49875, `connection/connection.yaml` and
   `app/app.yaml` byte-identical to before the save, and the window's panel empty.
3. Command, run from the repository root; it must report zero failures:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SemiPlot.Tests.Unit.UI.Settings|FullyQualifiedName~StartupFailureWindowTests|FullyQualifiedName~ResourcesTests"
   ```

4. What no headless test measures, because the test font is about twice as wide (`ui-theme.md:228-229`):
   the dialog's height and whether each new message fits its two reserved lines. Task 3 measures both in
   both languages; the smoke checklist in Post-Completion repeats them on the operator's screen.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix
- update plan if implementation deviates from original scope
- keep plan in sync with actual work done

## Solution Overview

- `SettingsSave` gains a third `SectionLoader`: `ConfigurationSectionName.Opc`, `StartupProbe.OpcDirectoryName`,
  `OpcUaConnectionLoader.Load(directory).ToResult()`. Staging, the write probe, promotion and the error
  rebuild already loop `_sections` or handle the OPC loader's error types, so the save path needs no other
  change.
- `ReadOwned` returns a three-element tuple `(App, Connection, Opc)`. A tuple matches today's shape and its
  three callers deconstruct it; a record buys nothing for three fields. (Fixer phase 2: replaced by
  `IReadOnlyList<SectionRead>` in `_sections` order, `SectionRead(Section, Owned)`, because three same-typed
  positional results let a swapped pair compile and widened every signature they passed through.)
- `FirstAbsentKey(app, connection, opc)` returns the section with the key,
  `(ConfigurationSectionName Section, string Key)?`. With `host` and `port` now in two sections, a message
  that names only the key does not tell the operator which file to fix. (Fixer phase 2: it takes the
  `SectionRead` list and returns `AbsentKey(Section, Key)?`, reading each section's keys through
  `SettingsViewModel.KeysOf`.)
- `SettingsKeyAbsent` names the section header first and the key second: "{0}: no file carries the key
  '{1}'. Add it to a file by hand." The `ui-text.md` rule that each message opens with what it names holds,
  and the section header is the name.
- `SettingsViewModel` gains the block as flat members beside the connection fields: `OpcHost`, `OpcPort`,
  `IsOpcHostValid`, `IsOpcPortValid`, `OpcKeys`, and a third loaded snapshot. The names are prefixed because
  `Host` and `Port` already belong to the archive connection.
- The block's rows reuse the labels `SettingsHostLabel` and `SettingsPortLabel` under a new header
  `SettingsOpcHeader`. Its two rules get their own messages, `SettingsOpcHostInvalid` and
  `SettingsOpcPortInvalid`, because the message line sits left of the buttons, not under the field, and
  "Host: ..." would name the archive host.
- The startup-failure gate reads all three sections. An `opc/` value the loader refuses (port 70000, a host
  name) still offers Settings: the dialog opens that value as an invalid field and saves the fix, which is
  the case the gate exists for.
- Size: the constructor is already 55 lines, past the 50-line method cap, and the slice adds to it, so the
  slice extracts two named private methods from it: `Populate(app, connection, opc)` seeds every field from
  the section values, and `CanSave(bool canWriteSections)` returns the save's `IObservable<bool>`. Neither
  changes a binding. (Fixer phase 2: with `CanSave` gone the constructor is about 35 lines, so `Populate` and
  its `[MemberNotNull]` were inlined back into it.) The class grew from 470 to 562 lines, past the 300-line preference; the review fixes
  then dropped `CanSave` for `ValidationMessage.Length == 0`, inlined the `Current*` wrappers and
  `Populate`, passed the sections as one list and dropped restating summaries, leaving 512. It is not split here: a host-and-port child view model, which would also remove the duplicated
  `Host`/`OpcHost` setter shape, moves the existing bindings, the `ValidationMessage` notifications and
  about forty test references with it, and cuts little of the size. `docs/plans/backlog.md` records the
  split and the seams that do cut it.

## Technical Details

### `SettingsSave` (Task 1)

```csharp
new(
	ConfigurationSectionName.Opc,
	StartupProbe.OpcDirectoryName,
	directory => OpcUaConnectionLoader.Load(directory).ToResult())

internal static (Result<OwnedSection> App, Result<OwnedSection> Connection, Result<OwnedSection> Opc)
	ReadOwned(string configDirectory)

internal static (ConfigurationSectionName Section, string Key)? FirstAbsentKey(
	OwnedSection app, OwnedSection connection, OwnedSection opc)
```

`FirstAbsentKey` checks `AppKeys`, then `ConnectionKeys`, then `OpcKeys`, the dialog's order.

Fixer phase 2 changed both signatures; the order holds:

```csharp
internal sealed record SectionRead(ConfigurationSectionName Section, Result<OwnedSection> Owned);
internal sealed record AbsentKey(ConfigurationSectionName Section, string Key);

internal static IReadOnlyList<SectionRead> ReadOwned(string configDirectory)
internal static AbsentKey? FirstAbsentKey(IReadOnlyList<SectionRead> sections)
```

### `SettingsViewModel`

| Member | Rule | Task |
| --- | --- | --- |
| constructor | takes `Result<OwnedSection> opc` after `connection`; `OpenAsync` and `SettingsViewModelStand` pass it (fixer phase 2: takes the one `IReadOnlyList<SectionRead>`, and is internal because `SectionRead` is) | 1 |
| `Populate(app, connection, opc)` | private; seeds every field, the OPC pair included, from the section values (fixer phase 2: inlined back into the constructor) | 1 |
| `RefusalOf(app, connection, opc)` | unreadable `app`, then `connection`, then `opc`, then the first absent key with its section header (fixer phase 2: `RefusalOf(sections)`, the first failed `SectionRead` in the list's order) | 1 |
| `OpcKeys` | `host` -> `vm.OpcHost`, `port` -> `TextOf(vm.OpcPort)`; keys `OpcUaConnectionLoader.HostKey`, `PortKey` | 1 |
| `OpcHost` | setter raises `IsOpcHostValid` and `ValidationMessage`, the shape of `Host` (`:139-148`) | 1 |
| `OpcPort` | `decimal?`, null while not a whole number in range; setter raises `IsOpcPortValid` and `ValidationMessage` | 1 |
| `IsOpcHostValid` | `NetworkAddress.IsIPv4(OpcHost)` | 1 |
| `IsOpcPortValid` | `IsPort(OpcPort)` | 1 |
| `PortOf(values)` | reads `ConnectionFile.PortKey`, the constant both loaders alias; called for both sections (review: takes the text, read through each loader's `PortKey`) | 1 |
| `CanSave(bool canWriteSections)` | private; nine `WhenAnyValue` inputs, the seven today plus `IsOpcHostValid`, `IsOpcPortValid`. ReactiveUI 23.2.28 ships overloads up to 12 selectors (the package's `ReactiveUI.xml` lists `WhenAnyValue``14`) (review: replaced by `ValidationMessage.Length == 0`, the same conjunction in one place) | 2 |
| `FirstBrokenRule` | language, theme, host, port, database, user, password, OPC host, OPC port | 2 |
| `SaveAsync` | adds `[ConfigurationSectionName.Opc] = opcEdits`; `IsRestartPending` also when `opcEdits.Count > 0` | 2 |

In Task 1 the OPC members exist, fill from the file and validate, but nothing binds them, `canSave` does not
read them and the save does not send them.

### `SettingsDialog.axaml` (Task 2)

- `RowDefinitions` at `:31` gains three `Auto` rows (twelve in all).
- Row 9: `TextBlock` with `SettingsOpcHeader`, `Grid.ColumnSpan="2"`, `Margin="0,10,0,0"`, bold, as `:66-70`.
- Row 10: `SettingsHostLabel`; `TextBox Name="SettingsOpcHost"`, `Classes.invalid="{Binding !IsOpcHostValid}"`,
  `Text="{Binding OpcHost}"`.
- Row 11: `SettingsPortLabel`; `NumericUpDown Name="SettingsOpcPort"` with the attributes of `:88-98`,
  bound to `OpcPort` and `!IsOpcPortValid`.

### Text (resx, both files)

| Key | English | Russian | Task |
| --- | --- | --- | --- |
| `SettingsOpcHeader` (new) | OPC UA connection | Подключение к серверу OPC UA | 1 |
| `SettingsKeyAbsent` (changed, two placeholders) | {0}: no file carries the key '{1}'. Add it to a file by hand. | {0}: ни один файл не содержит ключ '{1}'. Добавьте его в файл вручную. | 1 |
| `SettingsOpcHostInvalid` (new) | OPC UA host: expected an IP address such as 127.0.0.1 | Сервер OPC UA: ожидается IP-адрес вида 127.0.0.1 | 2 |
| `SettingsOpcPortInvalid` (new) | OPC UA port: a whole number 1–65535 | Порт OPC UA: целое число 1–65535 | 2 |

The range dash in `SettingsOpcPortInvalid` is the one `SettingsPortInvalid` carries, so the two port
messages read alike.

### Save flow

Unchanged steps of `overview.md#the-settings-window` 1-7, now over three folders: the view model sends
the changed keys of each section; `ReadOwned` re-reads three folders; the staging folder gets
`app/`, `connection/` and `opc/`; `AppSettingsLoader.Load`, `PostgresConnectionLoader.Load` and
`OpcUaConnectionLoader.Load` run over the staged folders; every rewritten target is probed, then replaced.
An `opc/` file with no edited key is copied byte for byte and never rewritten, so an archive-only save
leaves `opc/opc.yaml` untouched.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): code, tests, the measurement and documentation in this
  repository.
- **Post-Completion** (no checkboxes): the smoke on the operator's screen, the screenshot recapture, and the
  remaining roadmap slice.

## Implementation Steps

### Task 1: Read, gate and save the `opc/` section

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsSave.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupFailureViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsSaveSandbox.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelStand.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsSaveTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupFailureWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Localization/ResourcesTests.cs`

- [x] `SettingsSave`: add the `Opc` `SectionLoader` to `_sections`; `ReadOwned` returns `(App, Connection,
      Opc)` and its summary (`:40`) says "Every section the dialog edits"; `FirstAbsentKey(app, connection,
      opc)` returns `(Section, Key)?` over `AppKeys`, `ConnectionKeys`, `OpcKeys`
- [x] `SettingsViewModel` state: the constructor takes `Result<OwnedSection> opc`; `OpenAsync` passes it;
      `ValuesOf(opc)` reports an unreadable `opc/` to the panel as the other two sections do; extract
      `Populate(app, connection, opc)`; add `OpcHost`, `OpcPort`, `IsOpcHostValid`, `IsOpcPortValid` and
      `OpcKeys`; `PortOf` reads `ConnectionFile.PortKey`
- [x] `SettingsViewModel.RefusalOf` gains the `opc` arm and names the section header in the absent-key
      message (`App` -> `SettingsInterfaceHeader`, `Connection` -> `SettingsConnectionHeader`, `Opc` ->
      `SettingsOpcHeader`)
- [x] resx: add `SettingsOpcHeader`; change `SettingsKeyAbsent` to the two-placeholder text in both files
- [x] write the `opc-missing-directory` case of `StartupFailureWindowTests.TheSettingsButton_IsHiddenWhenASectionCannotSave`
      and run it before the next step: it fails
- [x] `StartupFailureViewModel.EverySavedKeyPresent` reads all three sections and requires all three to
      succeed and `FirstAbsentKey` to be null; the `OffersSettings` summary says "every configuration
      section the dialog edits" instead of "both"; the `opc-missing-directory` case now passes
- [x] test helpers: `SettingsSaveSandbox` gains `OpcDirectory` and `LoadOpc()`, and `DirectoryOf` becomes a
      switch over the three sections; `SettingsViewModelStand.Build` passes the third result;
      `SettingsViewModelTests` gains an `_opcDirectory` field beside `_appDirectory` and
      `_connectionDirectory`
- [x] `SettingsSaveTests`: `AnInvalidValueIsRefusedAndNoFileChanges` gains `(Opc, "port", "70000",
      OutOfRange)` and `(Opc, "host", "scada-01", HostNotIPv4)`;
      `ARefusedSaveNamesTheOperatorsFolderAndNeverTheStagingOne` gains `(Opc, "port", "70000")`; both theories
      also assert `(error as ConnectionFileError)?.Section.Should().Be(section)`; new
      `AnOpcEditReachesTheOpcFileAndLeavesTheOtherSectionsByteIdentical` (edit `port` to `55875`, load it
      through `OpcUaConnectionLoader`, compare `app/` and `connection/` bytes with a snapshot); new
      `TheCertificateStoreUnderTheOpcSectionSurvivesASaveUntouched` (a file under `opc/pki/own/certs/` is
      byte-identical after an `opc` save and no staging folder remains)
- [x] `SettingsViewModelTests`, refusal: `AnAppFileWithoutALocale...` and `AConnectionFileWithoutAPort...`
      assert the two-argument `FormatSettingsKeyAbsent(header, key)`; new
      `AnOpcFileWithoutAHostRefusesTheSaveAndNamesTheSectionAndTheKey` (message is
      `FormatSettingsKeyAbsent(SettingsOpcHeader, "host")`); `AMissingSectionFolderIsReported...` takes a
      `ConfigurationSectionName` parameter in place of its two bools, with one row per section; new
      `AnUnreadableOpcSectionIsReportedOnceAndBlocksTheSave` (`host: [unclosed`, one panel entry, message
      `FormatSettingsSectionUnreadable(SettingsOpcHeader)`)
- [x] `SettingsViewModelTests`, the OPC fields: `ADirectoryPopulatesEveryField` writes `opc/opc.yaml` and
      asserts `OpcHost`, `OpcPort`; `TheShippedSetPopulatesEveryField...` asserts `127.0.0.1` and `49875`; new
      `AnOpcPortTheRuleRejectsLoadsAsAnEmptyField` (`port: not-a-number`, `port: 70000`); new
      `AHostNameInTheOpcFileLoadsIntoAnInvalidOpcHostField`
- [x] `StartupFailureWindowTests.TheSettingsButton_IsHiddenWhenASectionCannotSave` gains `opc-no-files`,
      `opc-unreadable-file` and `opc-key-absent`; new `TheSettingsButton_IsShownOverAnOpcValueTheLoaderRefuses`
      (`port: 70000` in `opc/opc.yaml`, `OffersSettings` true)
- [x] `ResourcesTests.CompositeFormats_KeepTheirPlaceholder` adds
      `Resources.SettingsKeyAbsent.Should().Contain("{0}").And.Contain("{1}")`
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
      - must pass before Task 2

### Task 2: The OPC UA block in the dialog

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupFailureWindowTests.cs`

- [x] `SettingsViewModel`: the third loaded snapshot through `SpelledAsInTheFile`; extract
      `CanSave(bool canWriteSections)` over nine inputs; `FirstBrokenRule` arms for the OPC host and port
      after the password; `SaveAsync` sends `opcEdits` and sets `IsRestartPending` when any `opc` key changed
- [x] `SettingsDialog.axaml`: three rows after the password row as in Technical Details
- [x] resx: add `SettingsOpcHostInvalid` and `SettingsOpcPortInvalid` in both files
- [x] `SettingsViewModelTests`: `EveryFieldTheWindowEditsReachesTheFileTheLoadersRead` sets and loads the OPC
      pair; `TheSaveCannotExecuteWhileATextFieldIsBlank` gains `OpcHost`; new
      `TheSaveCannotExecuteWhileTheOpcPortIsNotAWholeNumberInRange` (null, 0, 65536, 49875.5); new
      `AnOpcSaveSetsTheRestartNotice`; `ChangingOnlyTheTheme_LeavesTheConnectionFileByteIdentical...` also
      compares `opc/opc.yaml`; `TheValidationMessageNamesTheFirstInvalidFieldInFormOrder` ends with an invalid
      OPC host, then an empty OPC port, then empty
- [x] `SettingsViewTests`: `TheRealisedDialog_ShowsTheLoadedValues` asserts `SettingsOpcHost` `127.0.0.1` and
      `SettingsOpcPort` `49875`; new `TheDialog_KeepsItsSizeAndItsButtonsWhenAnOpcFieldTurnsInvalid` (types
      `scada-01` into `SettingsOpcHost`, compares dialog and Save `Bounds`); new
      `AnInvalidOpcHost_NamesTheOpcFieldOnTheMessageLine` (`SettingsValidationMessage` reads
      `SettingsOpcHostInvalid`, `SettingsOpcHost` carries `invalid`, `SettingsHost` does not)
- [x] `StartupFailureWindowTests`: new
      `TheSettingsDialog_OverARefusedOpcPort_WritesTheFixedPortIntoTheOpcFile`, the journey of Acceptance
      Evidence item 2
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
      - must pass before Task 3

### Task 3: Verify acceptance criteria

- [x] verify every Overview point: the block edits `host` and `port` of `opc/`, existing keys only, only
      changed keys, validated on a staged copy, the restart notice after an OPC save, the three-section gate
      (each point read in the code and backed by a named test of Tasks 1-2)
- [x] verify the scope guard: `git diff --stat origin/master...HEAD` lists no file under
      `SemiPlot.DataSource.OpcUa`, `Chart/`, `Bridge/` or `ConfigFiles/`, and no change to
      `OpcUaConnectionLoader`, `ConnectionFile` or `NetworkAddress` (14 files, none matches)
- [x] measure the realised dialog with Skia and HarfBuzz, the real theme, at 14 px with 20 px lines, the
      method of `ui-theme.md:216-231`, in English and Russian: the dialog's height with every field valid; the
      single-line width of `SettingsOpcHostInvalid`, `SettingsOpcPortInvalid` and `SettingsKeyAbsent` with
      `SettingsOpcHeader` and `port`, each against the message cell (457 px English, 388 px Russian). Each
      fits its two reserved lines untrimmed; a message that does not is shortened in both files and measured
      again (2026-10-21: dialog 640 x 575 px in both languages, every state; English 365.2, 263.8 and
      487.0 px, Russian 354.3, 248.8 and 727.0 px; all untrimmed, the key-absent message in two lines in both;
      nothing shortened)
- [x] run the Acceptance Evidence command; zero failures (132/132)
- [x] run the full suite: `dotnet test SemiPlot.slnx` (the integration project needs a container runtime)
      (unit 2523/2523, integration 205/205)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched `.cs` files;
      both exit 0 (terse over 10 files)

### Task 4: [Final] Update documentation

**Files:**
- Modify: `AGENTS.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/ui-text.md`
- Modify: `docs/architecture/ui-theme.md`
- Modify: `docs/architecture/charting.md`
- Modify: `docs/plans/backlog.md`
- Modify: `readme.md`

- [x] `AGENTS.md:33-39`: drop "and it edits no key of `opc/` yet"; the failure window offers it "only when
      all three sections it edits, `app/`, `connection/` and `opc/`, read and carry every key it edits"
- [x] `overview.md#the-settings-window`: `:430-432` lists `host` and `port` in `opc/` and drops the by-hand
      sentence; `:445-452` says the OPC host and port follow the same rules; `:454-458`, `:460-462` and `:478`
      count three sections; `:485` adds `OpcUaConnectionLoader.Load`; `:206` and `:941` lose "both"/"yet"
- [x] `data-integration.md:1774-1775`: the settings window edits `opc/` under its usual rules, effective at
      the next start; `:1821` counts three sections
- [x] `ui-text.md:203-212`: three section headers including `SettingsOpcHeader`; `SettingsKeyAbsent` takes
      the section header as `{0}` and the key as `{1}`; the form-order list ends with `SettingsOpcHostInvalid`,
      `SettingsOpcPortInvalid`
- [x] `ui-theme.md:216-231`: the dated rows of the Task 3 measurement (dialog height, the three messages,
      both languages)
- [x] `charting.md:648-649`: the modal edits the `app/`, `connection/` and `opc/` section folders
- [x] `backlog.md`: one entry for splitting `SettingsViewModel` into a host-and-port child view model shared
      by the archive and OPC UA blocks, with the class size and the duplicated setter shape as the reason
- [x] `readme.md:74-76`: the settings window sets the OPC UA server address; drop the sentence that sends the
      operator to `opc/opc.yaml`
- [x] `git grep -n -i "opc.*by hand\|edits no key of\|does not edit .*opc\|both sections\|both section folders\|two section headers" -- AGENTS.md readme.md docs/architecture`
      prints only `data-integration.md:1493` and `ui-text.md:239`, which name the port bounds of
      `connection/` and `opc/` and stay (2026-10-21: those two lines only, `ui-text.md:243` after the edits
      above it)
- [x] move this plan to `docs/plans/completed/` (left in place: archiving belongs to the delivery step)

## Post-Completion

*Items requiring manual intervention or external systems - no checkboxes, informational only*

**Manual verification:**

1. Start the viewer against `SemiPlot/Artifacts/dev-config` in English: `Edit` -> `Settings` shows the
   "OPC UA connection" block after the archive block with `127.0.0.1` and the copy's port.
2. Type `scada-01` into the OPC host: the field takes the error border, the message line reads the OPC
   host rule untrimmed, and the dialog does not change size.
3. Switch the language to Russian, restart, repeat step 2: the Russian message fits the two reserved lines
   untrimmed.
4. Remove `port` from `opc/opc.yaml`, reopen the dialog: the message line names "Подключение к серверу OPC
   UA" and `port`, untrimmed, and Save is disabled.
5. Restore the file, set the OPC port to the demo stand's 55875, save, press Restart now: the status bar's
   OPC indicator reaches "OPC UA connected".
6. Write `port: 70000` into the copy's `opc/opc.yaml` and start: the failure window offers Settings, the
   dialog fixes the port, Restart now starts a copy that connects.

**Screenshot:** `readme.md:87` embeds `docs/img/settings.png`, which still shows the removed poll field and
no OPC block. It needs a recapture by hand after this slice merges.

**Remaining slices:**

- live-hold-to-edge: the grey ahead-break span and the held line to the live edge, render only.

**Executed by exec:**

- branch: opc-settings

## Verify it yourself

1. The failure-window gap. On master `5f8ea2c` `StartupFailureViewModel.EverySavedKeyPresent` reads
   `app/` and `connection/` only, so a copy whose `opc/` folder is deleted still offers Settings. Task 1
   ran the new `opc-missing-directory` row red before the gate edit ("Expected viewModel.OffersSettings
   to be False, but found True") and green after it; the row ships in `67d7979`. Run:

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~TheSettingsButton_IsHiddenWhenASectionCannotSave|FullyQualifiedName~TheSettingsButton_IsShownOverAnOpcValueTheLoaderRefuses"
   ```

   By hand: delete `opc/` from a copy of `ConfigFiles` with the password filled and start the viewer on
   it. The failure window names the `opc/` failure and shows no Settings button. Before this branch it
   showed the button over a dialog that could not fix the fault.
2. The fix end to end. `TheSettingsDialog_OverARefusedOpcPort_WritesTheFixedPortIntoTheOpcFile` writes
   `port: 70000` into the copy's `opc/opc.yaml`, opens the dialog from the failure window, types 49875 and
   saves; `OpcUaConnectionLoader.Load` then reads 49875, and `app.yaml` and `connection.yaml` stay
   byte-identical. By hand this is Post-Completion step 6. Before this branch the dialog had no OPC UA
   block, so the failure window offered no way to fix the port.
3. The whole acceptance set, from the repository root; it reports zero failures (138 on `16a57f5`):

   ```powershell
   dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~SemiPlot.Tests.Unit.UI.Settings|FullyQualifiedName~StartupFailureWindowTests|FullyQualifiedName~ResourcesTests"
   ```

4. What no headless test shows: the dialog's height and whether the new messages fit. Task 3 measured
   640 x 575 px in both languages and every message untrimmed; the tightest is the English
   `SettingsKeyAbsent` with the OPC UA header, whose first line is 445.6 of 457 px. Post-Completion steps
   1 to 4 check this on the operator's screen.
