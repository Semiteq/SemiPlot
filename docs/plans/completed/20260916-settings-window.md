# Settings window: the values that are meant to change, edited in the window

## Overview

The viewer reads its interface settings from the `app/` section folder and its archive connection from
`connection/`, both once at startup, and refuses to start when either is incomplete: `AppSettingsLoader`
fails a missing or unknown `locale` or `theme` (`SemiPlot.UI/Startup/AppSettingsLoader.cs:56-68`, the rule
at `:93-106`), and `PostgresConnectionLoader.Load` fails a missing, blank or out-of-range field
(`SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:56`). An operator who has to
change any of it edits YAML by hand. The set that ships carries an empty `password`
(`ConfigFiles/connection/connection.yaml:5`), which is a named startup failure by design, so the very
first thing every installation needs is a hand edit of a file the operator has never seen.

This change gives the operator a settings window holding the values that are meant to change:

- **Language and theme**, written back into the `app/` section folder.
- **The archive connection**: host, port, database, user, password and poll interval, written back into
  the `connection/` section folder. The password is stored in plain text.
- **`Edit` -> `Settings`** in the menu bar. The `Edit` menu does not exist: the window chrome ships no
  menu item without a command behind it (`SemiPlot.UI/MainWindow/AppMenuBar.axaml`), and Settings is the
  first command it holds.

Everything the window writes takes effect at the next start. Nothing is applied live.

`source_time_zone` is not in the window. It is not a display preference: `ArchiveTimeConverter` is
constructed from it at service registration
(`SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:22`) and it decides how every
archive timestamp is interpreted. It is commissioning data about the SCADA installation that the operator
can neither change nor act on, so the window leaves it in the file, where every rewrite keeps it.

**Out of this plan.** The palette editor: the twelve `App*` keys live inside `ThemeDictionaries`
(`SemiPlot.UI/Styles/Palette.axaml:40-51`) and Semi's own stock surfaces do not follow them, so editing
them produces a half-recoloured window, and the light/dark switch already covers what an operator
asks for. The pen and group editor: its own plan, `docs/plans/20260924-pen-and-group-editor.md`.

## Context (from discovery)

Files and components involved:

- `SemiPlot.Core/Configuration/ConfigurationSection.cs`: reads a section folder whole. Every `*.yaml` is
  parsed on its own and merged at the key level, the files ordered with `StringComparer.Ordinal` (`:51`).
  A key carried by two files fails naming both (`:75-77`). **`Merge` already builds an ownership map at
  `:61` and discards it**; it is what a writer needs and it exists. Every file is read with
  `File.ReadAllText` (`:97`).
- `SemiPlot.Core/Configuration/ConfigurationSectionError.cs:12-20`: `SectionProblem` has six arms,
  `DirectoryMissing`, `NoFiles`, `Unlistable`, `Unreadable`, `DuplicateKey` and `KeyConflict`. None of them
  describes a file that cannot be written or a key no file carries.
  `SemiPlot.UI/Messages/ConfigurationSectionFailureMapper.cs:28-85` throws `ArgumentOutOfRangeException`
  on an arm it does not know, and `SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs:34-43,146-149`
  requires its severity table to cover every member of the enum.
- `SemiPlot.UI/Startup/AppSettingsLoader.cs:40`: `Load(sectionDirectory)` returning `Result<AppSettings>`;
  `LocaleKey` and `ThemeKey` are `internal const` at `:25` and `:27`, and accepted values come from
  `SettingsVocabulary` (`AppSettings.cs:32-45`), which already pairs each enum member with its YAML token.
- `SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:56`: the same shape for the
  connection, with field presence (`:129`), range (`:165`) and time-zone (`:88`) validation as separate
  steps. Its deserializer carries `IgnoreUnmatchedProperties`, so a typed round trip drops any key the
  DTO does not model; `schema` is one such key (`PostgresConnectionLoader.cs:31`, defaulted at `:108`).
  `PostgresConnectionSettings` carries the zone as a `TimeZoneInfo`, not the identifier text the file
  holds (`PostgresConnectionSettings.cs:16`).
- Every loader error names the directory it read: `ConfigurationSectionError.Directory`,
  `AppSettingsError.Path` (built at `AppSettingsLoader.cs:121`) and `ConnectionFileError.Path` (built at
  `PostgresConnectionLoader.cs:206,222`), and the mappers render it
  (`ConfigurationSectionFailureMapper.cs:33,36`, `ArchiveFailureMapper.cs:83,89,95,107`). All three are
  classes with get-only properties, not records.
- `SemiPlot.UI/MainWindow/AboutDialog.axaml`: the dialog precedent, `SizeToContent="WidthAndHeight"`,
  `CanResize="False"`, `WindowStartupLocation="CenterOwner"`, `ShowDialog` awaited from the window's
  code-behind (`MainWindow.axaml.cs:71`), with the view model asking through an observable and the view
  doing the opening.
- `SemiPlot.UI/MainWindow/MainWindowViewModel.cs`: `AboutRequests`/`ExitRequests` are private subjects
  (`:22,24`) exposed `AsObservable` (`:59,61`); the commands only ask (`:46-49`).
- `SemiPlot.UI/MainWindow/AboutInfo.cs:11-14`: reads the directory back out of
  `Environment.GetCommandLineArgs()`, a hidden global this plan does not copy.
- `SemiPlot.UI/App.axaml.cs:59-80`: `CreateMainWindow` builds the startup-failure window with no
  container (`:71`); `App.Run` (`:97`) receives no configuration directory today.
  `Program.ReportStartupFailure` (`Program.cs:68-75`) has two callers: a failed argument parse
  (`Program.cs:21-24`), where no directory exists, and a failed `LogFileTarget.Prepare`
  (`Program.cs:28-31`), where `ConfigDir` is known. Both callers pass null, because the settings window
  does not fix a log path.
- `SemiPlot.UI/Messages/ResultReporting.cs`: the one route from a failed `Result` to the operator.
  `MessagePanelViewModel.Report` (`MessagePanelViewModel.cs:54`) opens the panel row for a new entry.

Related patterns found:

- The window chrome's menu rule: a menu item without a command or children is not added, and
  `AppMenuBarTests` walks the declared `Items`, requires one or the other with no exemption, and names
  the leaves it must reach (`AppMenuBarTests.cs:40-43`).
- A checkable menu item reads its flag `Mode=OneWay` and writes it only through the command it invokes.
- `SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs:15-41` is the existing YAML writer, and it is
  the shape this plan must **not** copy: one hardcoded template replacing a whole named file.
- `SemiPlot.Tests.Unit/DeliveredConfigurationTests.cs:21` pins the shipped `password: ""`.

Dependencies identified:

- None outside this repository. The settings window touches `SemiPlot.UI`, `SemiPlot.Core` and
  `SemiPlot.DataSource.Postgres` only, and depends on no SemiBase change.

## Development Approach

- **testing approach**: Regular (code first, then tests), matching the rest of this repository.
- complete each task fully before moving to the next
- make small, focused changes
- **every task includes new or updated tests**, listed as their own checklist items
- **all tests pass before the next task starts**
- **update this plan when scope changes during implementation**
- `dotnet build SemiPlot.slnx` stays at 0 warnings, `dotnet format SemiPlot.slnx --verify-no-changes`
  and `dotnet terse` over the touched files stay at exit 0
- every resource key lands in the task that first reads it, so each task compiles on its own

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit`, which sets `failSkips`, so nothing gated may live there. Pure
  logic uses `[Fact]`; anything touching ReactiveUI, Avalonia or a realised control uses
  `[AvaloniaFact]`. Every test class carries all three traits; the settings tests take
  `[Trait("Area", "Di")]` for the loader and writer work and `[Trait("Area", "Chart")]` for the view,
  matching how this tree already tags its UI tests.
- **no container test is needed**: nothing here reaches a database. The connection the window writes is
  read at the next start by code that already has container coverage.
- **the view is asserted off realised controls**, the way `AppMenuBarTests` and `MessagePanelViewTests`
  already do it, because a binding that resolves to nothing is invisible to a view-model test.
- **file tests work on a copy**: every test that writes copies its folder into a temporary directory
  first; the shipped set is the copy `DeliveredConfigurationTests` already reads from the test output
  (`AppContext.BaseDirectory/ConfigFiles`).
- Tests assert by error type and structured field, never on message wording.

## Acceptance Evidence

Each automatable item is a command with the result it must produce. `SemiPlot.Tests.Unit` is written
`<unit>` below: `SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`. A `--filter` that matches
nothing exits 0, so each run is read for a non-zero passed count.

1. **A key is written into the file that owns it, and no other file is rewritten.**
   `dotnet test <unit> --filter "FullyQualifiedName~ConfigurationSectionWriter"` passes, covering a
   folder holding `a.yaml` with `locale` and `b.yaml` with `theme`: staging a new `locale` rewrites
   `a.yaml` only, the staged `b.yaml` is byte-identical to the original, and `ConfigurationSection.Read`
   over the staged folder succeeds.
   Today (2026-09-16): no writer exists, and `ConfigurationSection.Merge` computes the ownership map at
   `ConfigurationSection.cs:61` and returns only the merged text.

2. **A key the format does not model survives the write.**
   The same filter covers a file carrying `locale`, `theme` and an unmodelled `operator_note`: after
   writing `theme`, `operator_note` is still there with its value. This is the test that fails if the
   writer round-trips through a DTO, because both loaders carry `IgnoreUnmatchedProperties`
   (`AppSettingsLoader.cs:37`, `PostgresConnectionLoader.cs:53`).

3. **A value that also reads as YAML survives as text.**
   The same filter covers passwords `on`, `no`, `~`, `null`, `123`, `12:34` and `#comment`: each is
   written, read back through `PostgresConnectionLoader.Load` and compared to what was typed. A writer
   without `WithQuotingNecessaryStrings(true)` writes `~` and `null` bare, and both read back as null,
   which the loader refuses as a `MissingField` password; `ConfigurationSection.cs:22-26` carries that
   setting for the read path and states why.

4. **An invalid value never reaches the file, and the refusal reaches the panel.**
   - `dotnet test <unit> --filter "FullyQualifiedName~SettingsSave"` passes, covering a save carrying the
     locale token `de` and a save carrying port `70000`: each is refused with the loader's own error
     (`AppSettingsError` with `ValueInvalid`, `ConnectionFileError` with `OutOfRange`) and every file on
     disk is byte-identical to what it was. The refusal comes from running the production loader over a
     staged copy, not from a second copy of the rules.
   - `dotnet test <unit> --filter "FullyQualifiedName~SettingsViewModel"` passes, covering the same
     refused port: the message panel gains exactly one entry and `IsRestartPending` stays false.

5. **A save that survives validation reloads to what was typed.**
   The `SettingsSave` filter covers a round trip: save every field, then `AppSettingsLoader.Load` and
   `PostgresConnectionLoader.Load` over the real folders return exactly the values saved, including a
   password holding a space and a `#`.

6. **A save that cannot write every target writes none.**
   The `SettingsSave` filter covers a save editing `theme` and `host` with `connection/connection.yaml`
   read-only: the save fails with `ConfigurationSectionError` of `SectionProblem.Unwritable` naming
   `connection.yaml`, and both `app/app.yaml` and `connection/connection.yaml` are byte-identical to what
   they were.

7. **A save writes only the keys the operator changed.**
   The `SettingsSave` filter covers two writers of one folder: a `host` another writer changed on disk
   after this window loaded survives this window's save of `port`, and the same key saved twice ends at
   the value of the second save.

8. **The shipped set is fixed in the window.**
   - The `SettingsViewModel` filter covers a view model built from a copy of `ConfigFiles/`, whose
     password is `""`: every field is populated from the files and the password field is empty.
   - The `SettingsSave` filter covers a save over an unmodified copy of `ConfigFiles/` that fills only
     the password: the save promotes, and both `AppSettingsLoader.Load` and
     `PostgresConnectionLoader.Load` over the copy succeed.

9. **A refusal names the operator's folder, never the staging one.**
   The `SettingsSave` filter covers a refused locale and a refused port: the returned error's `Path`
   equals the real section directory, and the mapped detail from `ArchiveFailureMapper.Map` contains
   that directory and not `.settings-staging-`.

10. **Every menu leaf still acts, and Settings exists only where a directory does.**
    - `dotnet test <unit> --filter "FullyQualifiedName~AppMenuBar"` passes with `EditSettings` added to
      the walked names at `AppMenuBarTests.cs:43`, and no exemption added.
    - `dotnet test <unit> --filter "FullyQualifiedName~MainWindowViewModelTests"` passes, covering a view
      model built with no configuration directory: `ShowSettingsCommand` exists and cannot execute.

11. **The window shows what was loaded.**
    `dotnet test <unit> --filter "FullyQualifiedName~SettingsViewTests"` realises the dialog against a
    view model built from a directory (a copy of `ConfigFiles/` with a filled password) and reads the
    values back off the controls: the language and theme combo boxes sit on the loaded members, the
    connection fields hold the file's text, and the restart notice is hidden before a save and visible after one that succeeded.

12. **The operator text is complete and in both languages.**
    `dotnet test <unit> --filter "FullyQualifiedName~Resources"` passes: both sets carry the same keys,
    every key resolves to a non-empty value, and every Russian value differs from the neutral one outside
    the three glyph keys. No colour literal appears:
    `git grep -nE '#[0-9A-Fa-f]{6,8}\b' -- 'SemiPlot/SemiPlot.UI/*.axaml' ':!SemiPlot/SemiPlot.UI/Styles/*'`
    returns nothing.

13. **The ownership entry point has a production caller.**
    `git grep -n "ReadOwned" -- SemiPlot ':!SemiPlot/SemiPlot.Tests.*'` prints the declaration in
    `SemiPlot.Core/Configuration/ConfigurationSection.cs`, calls to it in
    `SemiPlot.UI/Settings/SettingsSave.cs` from the save and from `SettingsSave.ReadOwned`, and the
    window's call to `SettingsSave.ReadOwned` in `SemiPlot.UI/MainWindow/MainWindowViewModel.cs`.
    `git grep -nw "Owners" -- SemiPlot ':!SemiPlot/SemiPlot.Tests.*'` prints a read in
    `SemiPlot.Core/Configuration/ConfigurationSectionWriter.cs`.

14. **The window fixes a new installation by hand.** Manual, in order. Prerequisite: an archive the
    viewer can reach, such as the bench `docs/architecture/bench.md` provisions, and its connection
    values.
    1. Copy the tracked set without filling the password:
       `New-Item -ItemType Directory -Force <dir> | Out-Null; Copy-Item ConfigFiles\* <dir> -Recurse -Force`.
       The copy's `connection/connection.yaml` carries `password: ""`.
    2. `dotnet run --project SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj -- --config-dir <dir> --log-file <path> --logging-level Information`.
       The startup-failure window opens and its failure names `password`.
    3. `Edit` -> `Settings`. The dialog opens holding every value of the copy and the password field
       empty.
    4. Type the archive's host, port, database, user and password, and save. The dialog shows the notice
       that the change takes effect at the next start.
    5. Close the viewer and run step 2's command again. The chart opens on the archive.
    6. `Edit` -> `Settings`, change the theme, save. The notice appears in the dialog and the main window
       keeps its colours.
    7. Restart. The window comes up in the new theme.
    8. Change the language, save, restart. The whole window, the menu included, is in the other language.
    9. Record `Get-FileHash <dir>\connection\connection.yaml`, type port `70000`, save. The dialog stays
       open and the main window's message panel, below the dialog, shows an entry naming `port`.
    10. Run `Get-FileHash <dir>\connection\connection.yaml` again. The hash equals the one from step 9.

    Steps 1 to 5 decide whether this window earns its place: they are the case the shipped
    `ConfigFiles/connection/connection.yaml:5` puts every new installation into.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with a plus prefix
- document blockers with a warning prefix
- update this plan if the implementation deviates from the scope above

## Solution Overview

**The window edits only keys that already exist.** Every key the window edits is required by its loader:
a missing `locale`, `theme`, `host`, `port`, `database`, `user`, `password`, `source_time_zone` or
`poll_interval_ms` is a startup failure (`AppSettingsLoader.cs:93-96`, `PostgresConnectionLoader.cs:129-158`).
The shipped set carries all of them, the empty password included, so a folder copied from it has an owner
for every key. The writer therefore needs no policy for "where does a new key go": it needs the owner of an
existing one, which `ConfigurationSection.Merge` already computes and throws away
(`ConfigurationSection.cs:61`). A key no file carries, because the operator removed it by hand, is refused
with `SectionProblem.KeyAbsent` naming the key and the section; the writer never picks a file for it.

**The window shows the files, not the typed settings.** The view model populates every field from the
raw merged mapping of each section, one string per key, never from `AppSettingsLoader` or
`PostgresConnectionLoader`. The startup-failure window is where the window is needed most, and there the
typed loaders fail by definition: the shipped blank password fails `ValidateFields`
(`PostgresConnectionLoader.cs:136,144`) and an unknown locale fails `ParseKey`
(`AppSettingsLoader.cs:93-106`). A typed read would open the window empty exactly then. The typed loaders
run only at save, over the staged copy. A token outside the vocabulary, such as a locale the file spells
`de`, leaves its combo box without a selection and the save disabled until the operator picks one.

**The writer edits a mapping, never a template.** It parses the owning file into
`Dictionary<string, object?>`, sets the key, and serializes the whole mapping back through the same
serializer `ConfigurationSection` uses (`ConfigurationSection.cs:24-26`), so the quoting rule lives in
one place. Keys the format does not model survive, which a typed round trip through either DTO would
silently drop. Comments do not survive: YamlDotNet does not round-trip them, and this is stated in
`docs/architecture/overview.md` rather than worked around. A file that owns no changed key is not
rewritten at all.

**A save writes only what the operator changed, over a fresh read.** The view model keeps the text it
loaded and sends the save only the keys whose text differs from it. The save re-reads both section
folders at that moment and applies those keys to what is on disk now, so a change another viewer
instance saved to a different key survives. Two saves of one key resolve to the later one.

**Validation is the production loader, run over a staged copy.** The save writes the candidate folders
into a staging directory laid out like the real one, runs `AppSettingsLoader.Load` and
`PostgresConnectionLoader.Load` against it, and promotes only when both succeed. A second copy of the
rules would drift from the first; this cannot, because it is the first. The same approach already gates
the shipped configuration set through the production loaders in `DeliveredConfigurationTests`.

**Promotion checks every target, then moves one file at a time.** Before the first move the save opens
every target for writing and closes it again; a target that refuses fails the whole save with
`SectionProblem.Unwritable` and nothing is moved. Each move is `File.Move(staged, target, overwrite:
true)` on one volume, which replaces the file in one step, so a failure leaves the original rather than
a truncated file. A move can still fail after the check, when another process opens the target between
the two. Then the files moved before it stay promoted and the rest keep their old values. That residue
is not guaranteed to load: on the startup-failure path the old values are the invalid ones that stopped
the start. The panel reports the failure, and the next save re-reads the folders and writes again.

**The view model asks and the view opens the dialog**, the shape the About dialog already uses:
`MainWindowViewModel` builds the `SettingsViewModel` and emits it on `SettingsRequests`, and the window's
code-behind shows it with `ShowDialog` and disposes it when the dialog closes
(`MainWindow.axaml.cs:33,67-78`). The About shape is kept over an injected dialog service because the
dialog returns nothing to its caller, and the repository has this one precedent to follow. The settings
view model does not reference a view type.

**A failed save goes to the message panel** through `ResultReporting`, like every other failure. The
mapper already turns an `AppSettingsError`, a `ConnectionFileError` and a `ConfigurationSectionError`
into a title, a detail and a remedy in both languages (`Messages/ArchiveFailureMapper.cs:16-29`), so a
rejected save reads the same as the startup failure for the same value, which is the point: the
operator sees one wording for one problem. The dialog stays open on a refused save, and the entry lands
in the main window's message panel behind the modal dialog; `Report` opens the panel row for a new entry
(`MessagePanelViewModel.cs:54`).

**The restart notice is the dialog's own.** A successful save sets `IsRestartPending` on the settings
view model, and a `TextBlock` in the dialog bound to it tells the operator the change takes effect at
the next start. It is not a message-panel entry: the panel carries failures, and
`AppStatusBarViewModel.ConnectionRestored` is its single message with no error behind it.

**The settings window is reachable from the startup-failure window.** That window is built with no
container (`App.axaml.cs:59-80`) and shows the failure that stopped the start; for a new installation
that failure is the empty password. The menu bar is already on it, so `Edit` -> `Settings` is there too.
`App` hands the configuration directory to `MainWindowViewModel` on both paths, as a `string?`
constructor argument. When the argument parse itself failed there is no directory, and
`ShowSettingsCommand` exists but cannot execute. A failed `LogFileTarget.Prepare` takes the same route
with null, although its directory is known: the settings window does not fix a log path.

**Nothing is applied live.** The theme could be (`Application.RequestedThemeVariant` is a property), and
the palette keys could be (every palette reference is a `DynamicResource`;
`git grep -n DynamicResource -- '*.axaml' | wc -l` counts 40 on 2026-09-24), but the language cannot:
AXAML reads text as `{x:Static text:Resources.Key}`, resolved once when the control is built, so a live
switch means rebuilding every view. One rule for all values is what the operator can predict, and the
groundwork for a live theme stays where it is.

## Technical Details

**Ownership, returned rather than discarded.** `ConfigurationSection.Read` keeps its signature. A second
entry point, `ConfigurationSection.ReadOwned(string sectionDirectory, ConfigurationSectionName section)`,
returns `Result<OwnedSection>`, built by the walk that already runs:

```csharp
public sealed record OwnedSection(
	IReadOnlyDictionary<string, string> Values,
	IReadOnlyDictionary<string, string> Owners);
```

`Values` holds each top-level key whose value is a scalar, as its text, with a null scalar as the empty
string; a nested value is not something the window edits and is left out. `Owners` maps every key to the
name of the file that carries it. Both are `OrdinalIgnoreCase`, matching the conflict check, because the
loaders match their DTO properties case-sensitively and two files spelling one key differently must not
merge. `ReadOwned` fails with exactly the errors `Read` returns for the same folder.

**What the window writes:**

| Section | Key | Source of the accepted set |
| --- | --- | --- |
| `app` | `locale` | `SettingsVocabulary.Languages` (`AppSettings.cs:35-39`) |
| `app` | `theme` | `SettingsVocabulary.Themes` (`AppSettings.cs:41-45`) |
| `connection` | `host` | free text, non-blank |
| `connection` | `port` | `PostgresConnectionLoader.ValidateRanges` (`:165`) |
| `connection` | `database` | free text, non-blank |
| `connection` | `user` | free text, non-blank |
| `connection` | `password` | free text, may hold YAML-significant characters |
| `connection` | `poll_interval_ms` | `PostgresConnectionLoader.ValidateRanges` (`:165`) |

`source_time_zone` and `schema` are never in a save's edits: the first because the window shows it
read-only, the second because the window does not model it at all. Both survive through the mapping
edit when their file is rewritten.

**Scalars round-trip as strings.** The writer's untyped parse reads every scalar as a string, and
`WithQuotingNecessaryStrings(true)` quotes a string that would otherwise read as another type. An
untouched `port` or `poll_interval_ms` that shares a file with a changed key is therefore re-emitted as
`port: "5432"`. It still loads: the read path already serializes the merged mapping through the same
setting, and the typed deserializer converts the quoted scalar to `int` (`ConfigurationSection.cs:22-26`,
`:85`). A file with no changed key is not rewritten, so its keys keep their spelling and its comments.

**The save's signature.** Raw tokens in, so a token outside the vocabulary reaches the loader and is
refused there:

```csharp
public static Result Save(
	string configDirectory,
	IReadOnlyDictionary<ConfigurationSectionName, IReadOnlyDictionary<string, string>> edits,
	ILogger logger)
```

The `logger` carries the one outcome that is logged rather than returned: a staging directory that
cannot be removed after promotion.

The section folders are `StartupSequence.SettingsDirectoryName` (`app`) and
`StartupProbe.ConnectionDirectoryName` (`connection`) under `configDirectory`. Nothing escapes `Save` as
an exception; every failure is an error in the result.

**The staging directory.** Each save stages under
`<config-dir>/.settings-staging-<Path.GetRandomFileName()>/`, with `app/` and `connection/` inside it.
It sits on the same volume as the targets, so `File.Move` is a rename that replaces the target. The name
is unique per save, so two instances saving at once never share one. The loaders read only `app/` and
`connection/` under `--config-dir`, so the staging directory is invisible to every start. A staging
directory that cannot be created fails the save with `Unwritable` for the section, with `FileNames`
carrying the staging folder's name, and nothing is written; `FileNames` is never empty, because the
mapper reads `error.FileNames[0]` (`ConfigurationSectionFailureMapper.cs:56`). The save removes the
staging directory on every path. One that cannot be removed after promotion is logged and does not turn
the save into a failure. A staging directory left by a killed process is inert and the next save does
not reuse it.

**Errors name the real folder.** The loaders run over the staging directory, so every error they return
names a staging path. `SettingsSave` rebuilds each returned `ConfigurationSectionError`,
`AppSettingsError` and `ConnectionFileError` with the real section directory in place of the staged one,
keeping every other field, and carries the original's reasons over with `CausedBy`. The three types are
classes with get-only properties, not records, so a `with` expression is not available; a new instance
of the same type is. A cause's exception text may still name the staging path, and only the log reads it.

**New section problems.** `SectionProblem` gains two arms, each with a detail and a remedy in both
resource sets; the title is chosen per section and already exists for both:

| Arm | Raised by | Names |
| --- | --- | --- |
| `Unwritable` | the promotion check, a move that fails after it, and a staging directory that cannot be created | the file, or the staging folder, in `FileNames` |
| `KeyAbsent` | the writer, for an edited key no file of the section carries | the key, in `Key` |

A sharing violation from a move onto a file another instance holds open for its read
(`ConfigurationSection.cs:97`) is an `Unwritable` failure reported through the panel. It is not retried.

**Processing flow of a save:**

1. The view model builds `edits` from the fields whose text differs from what it loaded.
2. `CreateFromTask` runs `SettingsSave.Save` through `Task.Run`.
3. For each section, `ReadOwned` re-reads the real folder.
4. The writer copies every file of the section into the staging directory and rewrites each file that
   owns an edited key; an edited key with no owner fails with `KeyAbsent`.
5. `AppSettingsLoader.Load` and `PostgresConnectionLoader.Load` run against the staged folders; a failure
   is returned with the real directory in it.
6. Every rewritten file's target is opened for writing and closed; a refusal fails with `Unwritable` and
   nothing moves.
7. Each rewritten file is moved over its target. Files the save did not rewrite are not moved.
8. The staging directory is removed.
9. On failure the view model reports the error to the message panel and the dialog stays open; on
   success it sets `IsRestartPending`.

**Parallel instances.** Several viewers may run against one configuration directory. The last save wins
per key: a save writes only the keys its operator changed, over a read taken at save time, so a key
another instance saved survives a save of a different key, and the same key resolves to the last save.
Each save stages in its own directory. The window between the fresh read and the move is not locked: a
save from another instance landing in it, on the same file, is overwritten by this one. That race is
accepted; the cost is one value re-typed, and a lock file would outlive a killed process.

**The tracked set is not a target by design.** Nothing stops `--config-dir ConfigFiles`, and a save there
writes into the tracked files. `DeliveredConfigurationTests.cs:21` pins `password: ""`, so a committed
filled password turns CI red; no further guard is added.

## What Goes Where

- **Implementation Steps** (`[ ]`): everything achievable in this repository: the ownership map, the
  writer and its new section problems, the staged save, the view model, the view, the configuration
  directory reaching the main window, the menu, the tests and the documentation.
- **Post-Completion** (no checkboxes): the work this plan deliberately leaves out, and consequences to
  watch.

## Implementation Steps

### Task 1: Return the key ownership the section reader already computes

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSection.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Core/Configuration/ConfigurationSectionTests.cs`

- [x] add `OwnedSection` as Technical Details states, in the same file as `ConfigurationSection` because
      nothing else produces it
- [x] add `ReadOwned`, keeping `Read` as it is so no caller changes; both share the walk at
      `ConfigurationSection.cs:58-86`, and `Read` serializes the merged mapping as it does today
- [x] build `Owners` from the map the walk already fills at `:61`, and `Values` from the scalars it
      merges, both `OrdinalIgnoreCase`
- [x] make the serializer at `:24-26` reachable from the writer inside `SemiPlot.Core`, so the quoting rule
      stays in one place
- [x] write tests: one file, several files, a nested value left out of `Values`, a null scalar read as
      the empty string, a key spelled in two cases across two files still failing
- [x] write tests for the failure arms, which must return the same errors as `Read`
- [x] run tests - must pass before task 2

### Task 2: Stage a section with each key written into its owning file

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSectionWriter.cs`
- Modify: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSectionError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ConfigurationSectionFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Configuration/ConfigurationSectionWriterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Messages/FailureSeverityTests.cs`

- [x] add `SectionProblem.Unwritable` and `SectionProblem.KeyAbsent`, with their arms in
      `ConfigurationSectionError.Describe` (`ConfigurationSectionError.cs:53-67`), whose default arm would
      otherwise call them an unreadable file
- [x] add both arms to `ConfigurationSectionFailureMapper.SectionDetail` and `SectionRemedy`, and their
      detail and remedy keys to both resource sets, with different Russian values, following
      `docs/architecture/ui-text.md`
- [x] add both arms to the severity table at `FailureSeverityTests.cs:34-43` as `Error`
- [x] the writer takes the fresh `OwnedSection`, the edits, the real section directory and a staging
      section directory; it copies every file of the section into staging, rewrites each file that owns
      an edited key, and returns the names of the files it rewrote
- [x] a rewrite parses the owning file through `ConfigurationSection`'s own deserializer and `ParseFile`
      (`ConfigurationSection.cs:89-113`), made reachable inside `SemiPlot.Core` like the serializer, never
      a second deserializer; it sets the keys and serializes the whole mapping back through the shared
      serializer
- [x] a rewrite replaces a key under the spelling the file carries: `Owners` is `OrdinalIgnoreCase`, but the
      parsed mapping is an ordinal `Dictionary<string, object?>` (`ConfigurationSection.cs:60,106`) and
      `FindRepeatedKey` is case-insensitive (`:119`), so the writer finds the key case-insensitively,
      removes it and sets the canonical key
- [x] an edited key with no owner fails with `KeyAbsent`; a file that cannot be read fails with
      `Unreadable`; nothing escapes as an exception
- [x] write tests: the owning file is rewritten and the other staged files are byte-identical copies
      (acceptance item 1)
- [x] write tests: an unmodelled key survives the write (item 2)
- [x] write tests: a file carrying `Locale:` with `locale` changed stages without `DuplicateKey`
- [x] write tests: `on`, `no`, `~`, `null`, `123`, `12:34` and `#comment` survive as text through
      `PostgresConnectionLoader.Load` over the staged folder (item 3)
- [x] write tests for the error arms: an edited key no file carries, an owning file removed between the
      read and the write
- [x] write mapper tests: each new arm maps to its own detail and remedy and names its file or key
- [x] run tests - must pass before task 3

### Task 3: Save by validating a staged copy with the production loaders

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Settings/SettingsSave.cs`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsSaveTests.cs`

- [x] implement `SettingsSave.Save` with the signature and the processing flow Technical Details states:
      a fresh `ReadOwned` per section, staging under `<config-dir>/.settings-staging-<random>/`
- [x] a staging directory that cannot be created fails with `Unwritable` for the section, `FileNames`
      carrying the staging folder's name, and nothing is written
- [x] run `AppSettingsLoader.Load` and `PostgresConnectionLoader.Load` against the staged folders, and
      rebuild every returned error with the real section directory
- [x] open every target for writing before the first move, with `FileMode.Open` and `FileAccess.Write`,
      which never truncates, and fail with `Unwritable` naming the file if one refuses
- [x] promote each rewritten file with `File.Move(staged, target, overwrite: true)`; a failed move returns
      `Unwritable` with its exception as the cause and is not retried
- [x] remove the staging directory on every path; a removal that fails after promotion is logged and
      the save still succeeds
- [x] write tests: the locale token `de`, port `70000` and a blank password each refuse the save and
      leave every file byte-identical (acceptance item 4)
- [x] write tests: a valid save reloads through both production loaders to exactly what was saved
      (item 5)
- [x] write tests: a read-only `connection.yaml` with edits to both sections fails with `Unwritable` and
      leaves both files byte-identical (item 6); the test clears the read-only attribute in its cleanup
- [x] write tests: a read-only `<config-dir>` refuses the save with `Unwritable` and leaves every file
      byte-identical
- [x] write tests: an untouched key another writer changed on disk survives a save of a different key;
      the same key saved twice ends at the second value (item 7)
- [x] write tests: an unmodified copy of `ConfigFiles/` with only the password filled promotes and both
      loaders succeed (item 8)
- [x] write tests: a refused save's error carries the real section directory and its mapped detail
      contains no `.settings-staging-` segment (item 9)
- [x] write tests: no `.settings-staging-*` directory remains after a successful or a refused save
+ [x] promote with `File.Replace` instead of `File.Move`, copying the target's Unix mode onto the staged
      file first, so a save keeps the target's ACL on Windows and its mode on Unix; tests pin both, each
      returning early on the other OS
+ [x] a staging folder that cannot be created names `<config-dir>`, the folder that refused, and
      `SettingsSave` creates the section folders inside it, so the writer creates no folder
  ! untested: a staging folder that cannot be removed after promotion is logged and the save succeeds;
    nothing in a test can hold the random staging name open before the save removes it
- [x] run tests - must pass before task 4

### Task 4: Add the settings view model

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelTests.cs`

- [x] construct it from the configuration directory, the two `Result<OwnedSection>` reads, the message
      panel and a logger; it reads no file itself
- [x] populate every field from `OwnedSection.Values`, never from a typed loader; a section whose read
      failed is reported to the panel once, its fields stay blank and the save cannot execute
- [x] expose the language and theme choices from `SettingsVocabulary` with their labels, adding the label
      keys to both resource sets; the loaded token matches an entry by the loader's rule, trimmed and
      `OrdinalIgnoreCase` (`AppSettingsLoader.cs:100`), and a token outside the vocabulary selects nothing
- [x] expose the connection fields as text, with `source_time_zone` read-only and the path of its owning
      file beside it
- [x] keep the loaded text and build the save's edits from the fields that differ from it
- [x] `SaveCommand` as `ReactiveCommand.CreateFromTask`, running `SettingsSave.Save` through `Task.Run`;
      `canExecute` derived with `WhenAnyValue` from both sections having loaded, a language and a theme
      selected, and no required field blank
- [x] report a failed save through `ResultReporting` to the panel; set `IsRestartPending` after a save
      that succeeded and take the saved text as the new loaded text
- [x] write tests: a view model built from a directory populates every field
- [x] write tests: a view model built from a copy of `ConfigFiles/` populates every field and leaves the
      password empty (acceptance item 8)
- [x] write tests: a locale token outside the vocabulary selects no language and populates every other
      field
- [x] write tests: `theme: Dark` selects the dark entry
- [x] write tests: a refused save adds one panel entry and leaves `IsRestartPending` false (item 4)
- [x] write tests: changing only the theme leaves `connection/connection.yaml` byte-identical
- [x] write tests: `canExecute` is false while a required field is blank
- [x] run tests - must pass before task 5

### Task 5: Add the settings dialog

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`
- Create: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewTests.cs`

- [x] lay the dialog out on the `AboutDialog.axaml` precedent: `SizeToContent`, `CanResize="False"`,
      `WindowStartupLocation="CenterOwner"`, `x:DataType` on the root
- [x] add the restart notice as a `TextBlock` whose visibility is bound to `IsRestartPending`
- [x] every colour resolves to a `Palette.axaml` key; no literal in the markup
- [x] add the dialog's title, field labels, save button and notice text to both resource sets, written to
      the standard in `docs/architecture/ui-text.md`
- [x] write tests: the realised dialog shows the loaded values and the time-zone field is read-only
      (acceptance item 11)
- [x] write tests: the save button is disabled while a required field is blank, read off the control
- [x] write tests: the notice is hidden before a save and visible after one that succeeded
- [x] run tests - must pass before task 6

### Task 6: Carry the configuration directory to the main window view model

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/UiServiceCollectionExtensions.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupData.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewModelTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/InitializeServicesTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Di/CompositionRootTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/EmptyCatalogueStartupTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs`

- [x] `MainWindowViewModel` takes the configuration directory as a `string?` constructor argument
- [x] the container path: `AddUi(string configDirectory)` registers `MainWindowViewModel` through a
      factory that passes it, and `StartupProbe.BuildArchiveServiceProvider` (`StartupProbe.cs:42-44`)
      takes `options.ConfigDir` from `StartupProbe.Run` (`:35`)
- [x] the failure path: `App.Run` and `App.Configure` take the directory as `string?` and
      `CreateMainWindow` passes it at `App.axaml.cs:71`; `Program.Main` passes `options.Value.ConfigDir`
      at `Program.cs:43,51`, and `ReportStartupFailure` passes null at `:72` for both of its callers, the
      failed argument parse (`:21-24`) and the failed `LogFileTarget.Prepare` (`:28-31`), because the
      settings window does not fix a log path
- [x] update the `App.Run` cref at `StartupData.cs:10` to the new signature
- [x] add `SettingsRequests` as a private subject exposed `AsObservable`, the shape `AboutRequests` uses
- [x] add `ShowSettingsCommand` as `ReactiveCommand.CreateFromTask`: its body reads both sections with
      `ReadOwned` through `Task.Run`, builds a `SettingsViewModel` with the window's own panel and logger,
      and emits it; `canExecute` is false when the directory is null
- [x] pass a directory, or null where the test is about the failure path, at every existing construction
      and `AddUi` call the Files block lists
- [x] write tests: with no directory, `ShowSettingsCommand` exists and cannot execute (acceptance item 10)
- [x] write tests: with a directory, executing the command emits one `SettingsViewModel` whose fields hold
      the directory's values
- [x] run tests - must pass before task 7

### Task 7: Add the Edit menu and open the dialog

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/AppMenuBar.axaml`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/App.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/AppMenuBarTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`

- [x] add `EditMenu` between `File` and `View`, holding `EditSettings` bound to `ShowSettingsCommand`, and
      the two header keys to both resource sets
- [x] subscribe to `SettingsRequests` in `OnLoaded` beside `AboutRequests` (`MainWindow.axaml.cs:33`);
      the `async void` handler shows the dialog with `ShowDialog(this)`, disposes the view model when it
      returns, and reports a throw through `MainWindowViewModel.ReportFailure`, as `ShowAbout` does
      (`:67-78`)
- [x] update the comment at `App.axaml.cs:63-65`: the settings window also reports into the
      startup-failure window's own panel
- [x] write tests: extend the `Contain` list at `AppMenuBarTests.cs:43` with `EditSettings`, and assert by
      name that `EditMenu` contains `EditSettings`; the walk still passes with no exemption
- [x] write tests: the startup-failure window carries `EditSettings` with its command
- [x] run tests - must pass before task 8

### Task 8: Verify acceptance criteria

- [x] run every command in `## Acceptance Evidence` items 1 to 13 and record each passed count or output
  ! measured 2026-09-24 after the review fixes: `ConfigurationSectionWriter` 20 passed (items 1-3),
    `SettingsSave` 16 (items 4-9), `SettingsViewModel` 20 (items 4, 7, 8), `AppMenuBar` 8 and
    `MainWindowViewModelTests` 8 (item 10), `SettingsViewTests` 3 (item 11), `Resources` 8 (item 12); the
    colour `git grep` prints nothing (item 12); `ReadOwned` prints the declaration at
    `ConfigurationSection.cs:34` and calls at `SettingsSave.cs:86` and `MainWindowViewModel.cs:198,200`,
    `Owners` prints the read at `ConfigurationSectionWriter.cs:62` (item 13). Re-measured after the
    smells fixes: the same counts; `ReadOwned` now prints `ConfigurationSection.cs:34`, calls at
    `SettingsSave.cs:46,118`, the `SettingsSave.ReadOwned` declaration at `SettingsSave.cs:40` and its
    call at `MainWindowViewModel.cs:197`; `Owners` still reads at `ConfigurationSectionWriter.cs:62`. Item 7's per-key diff is
    also pinned in the view model: sending every field of a changed section fails
    `ASaveSendsOnlyTheChangedKeyOfASection`, 1 failed of 16, and passes once restored
- [x] run the full suite: `dotnet test SemiPlot.slnx`
  ! measured 2026-09-24 after the review fixes: Unit 1135 passed, Integration 97 passed, 0 failed,
    0 skipped; unchanged after the smells fixes
- [x] `dotnet build SemiPlot.slnx` at 0 warnings, `dotnet format SemiPlot.slnx --verify-no-changes`
      at exit 0, `dotnet terse` over the touched files at exit 0
  ! measured 2026-09-24: build 0 warnings 0 errors; format exit 0; terse exit 0 over the 30 `.cs` files
    `git diff master...HEAD` names plus the amended writer test
- [x] prove one new test by mutation: break the writer's ownership lookup so every key goes to the
      first file, and confirm the item 1 test goes red
  ! measured 2026-09-24: first run stayed green, 16 of 16, because the item 1 test edited `locale`, which
    the first file `a.yaml` already owns. [deviation] the test became a theory that also edits `theme`,
    owned by `b.yaml`; under the mutation that case goes red with `KeyAbsent`, 1 failed of 17. Writer
    restored, 17 of 17 pass
- [x] prove the item 3 test by mutation: build the writer's serializer without
      `WithQuotingNecessaryStrings(true)`, and confirm the `~` and `null` cases go red
  ! measured 2026-09-24: exactly the `~` and `null` cases fail, 2 failed of 17; serializer restored
- [x] walk the manual checklist, item 14, and record what each step showed (walked by the operator on the stand; the resize on validation it found is Task 12)
  ! needs an operator at the stand with a reachable archive; the operator walks it before ship

### Task 9: Update documentation

- [x] `docs/architecture/overview.md`: the window writes back into the section folders, one key into its
      owning file, only the keys the operator changed, over a read taken at save; the staging directory
      and its removal; last write wins per key and the accepted read-to-move race; comments do not
      survive a rewrite, and scalars round-trip as strings, so an untouched `port` or `poll_interval_ms`
      in a rewritten file may come back quoted and still loads; a file with no changed key is not
      rewritten
- [x] `docs/architecture/ui-text.md`: the new operator strings, including the two section problems
- [x] `CLAUDE.md`: the settings window is the viewer's only writer of the section folders, and it edits
      only keys that already exist; the `App.Run` signature in the `.AfterSetup(...)` paragraph (`:247`)
      gains the configuration directory; `AddUi()` at `:228` becomes `AddUi(string configDirectory)`
- [x] `docs/architecture/data-integration.md`: `App.Run(null, failure)` at `:459` and
      `App.Run(AppSettings?, Result<StartupData>)` at `:481` gain the configuration directory
- [x] `readme.md`: the operator fills the password in the window rather than in a text editor
- [x] move this plan to `docs/plans/completed/`
  ! left to ship: the plan moves in the ship commit

## Post-Completion

*No checkboxes: these need action outside this codebase.*

**What this plan deliberately leaves out**

- The palette editor. The twelve `App*` keys sit inside `ThemeDictionaries` and Semi's stock surfaces do
  not follow them, so editing them recolours half the window.
- The pen and group editor and its menu item: `docs/plans/20260924-pen-and-group-editor.md`, which adds a
  second child to the `Edit` menu this plan creates.
- Anything applied without a restart.

**Known consequences to watch**

- The demo stand removes `%TEMP%\SemiPlot\ConfigFiles` when it stops (`AppHost/DemoDirectories.cs:71`)
  and `converge` overwrites `connection/connection.yaml` on every run
  (`Tools.ArchiveSeeder/Converge.cs:55`). A value typed into the settings window during a demo run does
  not survive either. That is what the demo stand is for, and the settings window is not the place to
  change it.
- The password is stored in plain text, in a file an operator can read. How it is protected is a
  separate decision, and the window does not prejudge it: it writes the `password` key the loader
  already reads.
- The write probe checks write access to each target file, not the folder rights `File.Replace` also needs.
  A folder that refuses the replace after `app/` was promoted leaves the save half-applied: `app.yaml` is new,
  the panel shows `Unwritable`, the restart notice stays off. Nothing is lost and a retry re-reads and writes
  again. When a target does go missing, the panel names the file; the path of its `.replaced` copy is in the
  log only.

**Executed by exec:**
- branch: settings-window

## Verify it yourself

Automated, from the repository root:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ConfigurationSectionWriter|FullyQualifiedName~SettingsSave|FullyQualifiedName~SettingsViewModel|FullyQualifiedName~AppMenuBar|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~SettingsViewTests|FullyQualifiedName~Resources"
dotnet test SemiPlot.slnx
```

The first prints 83 passed (20 + 16 + 20 + 8 + 8 + 3 + 8); the second 1135 unit and 97 integration.
`SettingsSaveTests` pins the two save properties a reader cannot see from the dialog: a 0600 target keeps
its mode on Unix and an explicit ACL rule survives on Windows (`File.Replace`), and a key another writer
changed survives a save of a different key (`ASaveSendsOnlyTheChangedKeyOfASection` in
`SettingsViewModelTests`, red under a per-section diff).

On a real archive, acceptance item 14 steps 1 to 10 above. Steps 1 to 5 are the new-installation path:
the viewer starts on the shipped empty password, `Правка` -> `Настройки` fills it, and the next start opens
the chart.

### Task 10: Drop the time zone from the dialog (+)

`source_time_zone` is commissioning data about the archive, not an operator setting: the dialog shows it
read-only, the operator can neither change it nor act on it. The dialog stops showing it; the key stays in
`connection/connection.yaml` and survives every rewrite as any unmodelled key does. Removing the key itself
is a separate change.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml`, `SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Settings/SettingsViewModelTests.cs`, `SettingsViewTests.cs`,
  and any other test that reads the removed members
- Modify: `docs/architecture/overview.md`, `docs/architecture/ui-text.md`, `readme.md`, this plan's
  Acceptance item 14 step 3

- [x] remove the time-zone field, its file line and their view-model members, and the resource keys only
      they use
- [x] a test that a save still leaves `source_time_zone` in `connection.yaml` unchanged
- [x] update the docs and item 14 step 3 so nothing describes the removed field
- [x] run `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse`
  ! measured 2026-09-25: Unit 1136 passed, Integration 97 passed, 0 failed; build 0 warnings; format and
    terse exit 0. `SettingsSave.SectionDirectory` became private, its only outside caller gone; item 11
    no longer names the time-zone field either

### Task 11: Type the connection fields, and an IPv4 host everywhere (+)

Every connection field is a plain `TextBox`, so the dialog accepts text in `port` and the error surfaces
only when the staged copy fails to load. Each field takes only what its loader accepts, and `host` is an
IPv4 address everywhere: the dialog and `PostgresConnectionLoader` apply one rule.

- `port`: `NumericUpDown`, integers only, `LowestPort`..`HighestPort` (the loader's constants).
- `poll_interval_ms`: `NumericUpDown`, integers only, greater than zero (`ValidateRanges`).
- `host`: an IPv4 literal of exactly four dot-separated decimal octets, each 0 to 255; no hostname, no IPv6,
  no shortened form (`127.1`), no leading `+`/whitespace inside. One predicate, public on
  `PostgresConnectionLoader` (or beside it), used by the loader's validation and by the view model. A host
  that fails it is a named startup failure (`ConnectionFileProblem` gains the arm it needs, with mapper,
  both resx files and `FailureSeverityTests`), never a connect-time surprise.
- `database`, `user`: non-empty text; `password`: masked text, as today.
- The save stays disabled while any field fails its rule, and the failing field shows why (a resource
  string, both languages). A file value the rule rejects (a non-numeric port, a hostname) loads into the
  dialog as an empty or invalid field rather than crashing it.
- The shipped `ConfigFiles/connection/connection.yaml` says `host: 127.0.0.1`. Everything that writes or
  builds a connection with `localhost` moves to `127.0.0.1`: `SemiPlot.AppHost/AppHost.cs` connection
  strings (so `converge` writes an IPv4 host), the seeder's `ConnectionFileWriter` inputs, the container
  test fixtures and any test YAML. `DeliveredConfigurationTests` keeps the shipped set loading.

**Files:** discover them; at least `PostgresConnectionLoader.cs`, `ConnectionFileError.cs` (or its problem
enum), the connection failure mapper, `Resources.resx`, `Resources.ru.resx`, `SettingsDialog.axaml`,
`SettingsViewModel.cs`, `ConfigFiles/connection/connection.yaml`, `SemiPlot.AppHost/AppHost.cs`, the
integration fixtures, and the tests of each; docs: `data-integration.md` (the `host` key and the new
failure), `overview.md#the-settings-window`, `ui-text.md`, `readme.md`, `CLAUDE.md` if it names `localhost`.

- [x] one IPv4 predicate; the loader refuses a non-IPv4 host with a named failure
- [x] the dialog: `NumericUpDown` for port and poll interval, the host rule, per-field messages, save gated
- [x] every `localhost` connection source in the repository moves to `127.0.0.1`; `git grep -n "localhost"`
      over code, config and tests returns only lines that are not a PostgreSQL host
- [x] tests: the predicate (valid, hostname, IPv6, `127.1`, `256.0.0.1`, empty); the loader failure; the
      dialog refusing text in port through the realised control; a hostname in the file loading into an
      invalid host field with save disabled
- [x] docs updated
- [x] run `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse`
  ! measured 2026-09-25: Unit 1188 passed, Integration 97 passed, 0 failed; build 0 warnings; format
    and terse exit 0. The shipped file said `host: localhost`, not `127.0.0.1` as the section above
    states; it now says `127.0.0.1`. `localhost` left by `git grep`: the Aspire dashboard URLs in
    `.run/Live demo.run.xml` and `AppHost/Properties/launchSettings.json` (HTTP, not PostgreSQL), the
    `psql --host localhost` readiness wait that runs inside the container, the fixture's
    `localhost` -> `127.0.0.1` mapping of the Testcontainers host, and tests and docs naming it as a
    refused host

### Task 12: A form never resizes on validation (+)

The dialog sizes to its content (`SizeToContent="WidthAndHeight"`), so a per-field error line under
`host` widens and lengthens the window and moves the buttons. The norm for every form in this
application, the settings dialog first and the palette editor later:

- the dialog has a fixed width chosen to fit the longest label and field in both languages; its height is
  fixed by content that does not change with validation (no `SizeToContent` on width, no row that appears);
- an invalid field is marked by its border only: a style keyed on a class (for example `invalid`) paints
  the border with `AppSeverityErrorBrush`; the class is bound to the field's `Is*Valid` flag;
- one message line sits left of the buttons, with room reserved for two lines of text that wraps, and shows
  the message of the first invalid field in form order; empty when every field is valid;
- messages are one short line in both languages (for example "IPv4: четыре числа 0–255").

**Files:** `SettingsDialog.axaml` (and a style file if the class style belongs in `Styles/`),
`SettingsViewModel.cs` (the first-invalid message), both resx files, `SettingsViewTests.cs`,
`SettingsViewModelTests.cs`; docs: the norm stated once in `docs/architecture/ui-theme.md` (or `ui-text.md`
for the message rule) with a one-line pointer from `CLAUDE.md`'s UI section.

- [x] fixed-size dialog, the border class and style, the reserved message line, the per-field lines removed
- [x] the view model exposes the first invalid field's message; messages shortened in both languages
- [x] view test: the realised dialog's `Bounds` are identical before and after typing an invalid host, and
      the message line shows the host message and the host field carries the class
- [x] document the norm and point to it from `CLAUDE.md`
- [x] run `dotnet test SemiPlot.slnx`, `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse`
  ! measured 2026-09-25: Unit 1196 passed, Integration 97 passed, 0 failed; build 0 warnings; format and
    terse exit 0. Width 440 px: natural width with Skia and HarfBuzz is 245 px English, 278 px Russian;
    440 leaves the Russian message line 196 px, where every message fits two lines; the dialog is
    440 x 462 in both languages, valid and invalid. The style lives in `Styles/Forms.axaml`, included
    after `SemiTheme`; it targets `Border#PART_ContentPresenterBorder`, because Semi paints that part on
    focus, and a `SettingsViewTests` theory reads it back under both variants and fails without the
    include. The restart notice shares the message line, so no row appears after a save.
    `SettingsFieldRequired` now takes the field label as `{0}`
  ! 2026-09-25 test cut: redundant settings, host-rule and view tests removed; Unit 1174 passed. The
    border test now runs under the light variant only. `TheDialog_KeepsItsSizeAndItsButtonsWhenAFieldTurnsInvalid`
    fails with the `Height` setter of `TextBlock.form-message` removed (dialog 528 x 488 against 528 x 480)
