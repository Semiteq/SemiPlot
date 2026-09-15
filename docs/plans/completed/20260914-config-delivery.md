# Configuration delivery: sections as folders, a shipped set, explicit launch keys

## Overview

The application requires two configuration files and the repository contains neither. The settings
file exists only as a string literal inside the bench seeder
(`SemiPlot/SemiPlot.Tools.ArchiveSeeder/AppSettingsFileWriter.cs:18-19`), and the connection file
only as values the seeder computes at run time. An installation is assembled by hand, and nothing in
the build says whether the file an operator is given still parses.

Four changes, one delivery:

- **Configuration becomes a tree of section folders.** Each section is a folder under `--config-dir`,
  and every section folder behaves the same way: the loader reads every `*.yaml` in it and merges
  them. A key present in two files of one folder is a startup failure naming the key and both files,
  not a value one file wins.
- **`ConfigFiles/` holds the set that ships**, tracked and gated by the production loaders.
- **The launch keys `--config-dir`, `--log-file` and `--logging-level` become required.** A missing
  key, an unknown key or an unusable value stops the start and says so in the failure window, which
  is the only channel a `WinExe` has.
- **The live demo gets its own copy under the temporary directory.** The AppHost lays `ConfigFiles`
  into `%TEMP%\SemiPlot\ConfigFiles`, points the log at `%TEMP%\SemiPlot\Logs`, hands both paths to
  the processes it launches, and removes the configuration when the stand stops. Nothing writes into
  the tracked set.

The archive password is out of scope. `connection/connection.yaml` ships with an empty `password`,
which is already a named startup failure
(`SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs:146-153`), so the
repository carries no credential and a forgotten edit fails loudly rather than silently.

## Context (from discovery)

- `ConfigFiles/` exists at the repository root and is empty, so git tracks nothing under it
  (measured 2026-09-14: `git ls-files ConfigFiles` returns nothing, and the path is not gitignored).
- Both loaders name exactly one file today: `AppSettingsLoader` reads `<config-dir>/ui/app.yaml`
  (`SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs:13-25`) and `PostgresConnectionLoader` reads
  `<config-dir>/archive-connection.yaml` (`SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs:20,28`).
  Both build their deserializer with `UnderscoredNamingConvention` and `IgnoreUnmatchedProperties`
  (`AppSettingsLoader.cs:33-36`, `PostgresConnectionLoader.cs:49-52`), both read through
  `File.ReadAllText` (`AppSettingsLoader.cs:71`, `PostgresConnectionLoader.cs:116`), and both map
  every thrown exception to a reportable failure rather than letting it escape.
- `SemiPlot.Core` references only FluentResults and System.Reactive
  (`SemiPlot/SemiPlot.Core/SemiPlot.Core.csproj:4-5`). `SemiPlot.UI` and
  `SemiPlot.DataSource.Postgres` each reference Core and each already carry YamlDotNet
  (`SemiPlot.UI.csproj:36,40`, `SemiPlot.DataSource.Postgres.csproj:9,13`); they do not reference one
  another, so anything both need lives in Core.
- YamlDotNet is pinned at 18.1.0 (`SemiPlot/Directory.Packages.props:37`) and
  `DeserializerBuilder.WithDuplicateKeyChecking()` exists there, documented to throw when a duplicate
  key is found.
- `StartupOptions.Parse` accepts anything: an absent key falls back to a constant
  (`SemiPlot/SemiPlot.UI/StartupOptions.cs:10-16,21-23`), an unknown key is dropped by a `switch`
  with no default arm (`:28-41`), and an unusable logging level writes to the standard error stream
  and continues (`:66-69`). `SemiPlot.UI` is `OutputType=WinExe`
  (`SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj:4`), so that stream reaches nobody when the application
  starts from a shortcut.
- `CultureInfo.DefaultThreadCurrentUICulture` has exactly one writer,
  `StartupSequence.ApplyCulture` (`StartupSequence.cs:29,49-52`), reached only from
  `StartupSequence.Run`. `App.axaml:5` declares `RequestedThemeVariant="Light"`, so a window opened
  before any configuration exists already has a variant but no locale.
- Failures already have a window and a mapper. `ArchiveFailureMapper.Map` switches on the error type
  (`SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs:26-35`), falls through to `MapUnknown`
  (`:34,187`), and every string it produces is a resource key in both sets.
- `PostgresConnectionLoader.Load` short-circuits on the first failed check: `ValidateFields` at `:71`,
  then `ValidateRanges` at `:78`, then `ResolveTimeZone` at `:85`. `ConnectionFileError` carries
  `Path`, `Kind` and `Reason` and no field-name collection; `ValidateFields` composes the names into
  the `Reason` sentence (`:234`).
- The bench password is a compile-time constant, not a generated value: `BenchRoles.ReaderPassword`
  (`SemiPlot/SemiPlot.Tools.ArchiveSeeder/BenchRoles.cs:22`), repeated in
  `SemiPlot/SemiPlot.AppHost/AppHost.cs:13` and written into the connection file from the constant
  (`Converge.cs:60`).
- The AppHost builds its persistent directory at `AppHost.cs:19` and puts the log inside it at `:20`,
  passing both at `:39` and `:48`. One stand runs at a time by construction: a fixed host port with
  `isProxied: false` (`:14,27`) and one database name.
- The Aspire AppHost SDK adds a project resource without a compile reference, which is why the bench
  role names are repeated in the AppHost rather than read from the seeder (`AppHost.cs:2-4`).
- The sibling SemiStep reads four of its seven sections folder-wide and three by exact file name;
  making that uniform is Semiteq/SemiStep#188.

## Development Approach

- **testing approach**: Regular - code first, tests in the same task.
- complete each task fully before moving to the next
- make small, focused changes
- **CRITICAL: every task MUST include new/updated tests** for code changes in that task
- **CRITICAL: all tests must pass before starting next task** - no exceptions
- **CRITICAL: update this plan file when scope changes during implementation**
- run tests after each change

Three project rules bind every task:

- `SemiPlot.Tests.Unit` sets `failSkips`, so no gated test may live there. A test that needs Windows
  or a container belongs in `SemiPlot.Tests.Integration`.
- The build runs under `TreatWarningsAsErrors` with the style analyzers on
  (`SemiPlot/Directory.Build.props:10`); `dotnet format SemiPlot.slnx --verify-no-changes` and
  `dotnet terse` must both exit 0 before a task is done.
- Each task leaves the whole solution compiling. Task boundaries follow the compile graph, not the
  narrative: a task that changes a public signature carries every file that reads it.

The live demo is broken from task 2 until task 7: the loaders read the new section folders while the
seeder and the AppHost still produce the old layout. That is expected and is not a defect to chase.

## Testing Strategy

- **unit tests**: required for every task.
- The section reader is pure and carries the edge cases the merge exists for: an absent folder, a
  folder with no `*.yaml`, an empty `*.yaml`, two files that agree, two files that conflict on a key,
  a duplicate key inside one file, and a file that begins with a byte order mark.
- The merge round trip is pinned by a test, not assumed. The reader deserializes each file to a
  dictionary and reserializes the merged result, which is lossy on scalar typing: a value returns as
  a string and is re-emitted unquoted. Both current DTOs recover correctly, and a test over the
  shipped connection body is what keeps that true.
- The shipped configuration set is gated by the production loaders, not by a copy of their rules.
  Both loaders read the tracked folders straight from the test output directory, so a broken
  delivered file fails the build.
- Argument parsing is pure and tested directly. Every new failure arm reaches `ArchiveFailureMapper`
  in a test, because the window is the only place an operator sees it.
- The AppHost is not unit tested. Its temporary-directory lifecycle is verified by the manual smoke
  run in Acceptance Evidence.

## Acceptance Evidence

Each item is a command with the result it must produce, against the measured before-state.

1. **The set is tracked.**
   `git ls-files ConfigFiles` lists `ConfigFiles/app/app.yaml` and
   `ConfigFiles/connection/connection.yaml`. Today (2026-09-14): returns nothing.

2. **A section merges its folder.**
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ConfigurationSection"`
   passes, covering: two files whose keys are disjoint merge into one mapping; two files that both
   carry `locale` fail with an error naming `locale` and both file names; a file carrying `locale`
   twice fails naming `locale` and that one file; an absent folder and a folder with no `*.yaml` fail
   with different problems; an empty `*.yaml` contributes nothing and fails nothing; a file written
   with a byte order mark still contributes its first key.

3. **The merge preserves the shipped connection values.**
   The same filter covers a test feeding the exact body of `ConfigFiles/connection/connection.yaml`
   through `ConfigurationSection.Read` into `PostgresConnectionDto` and asserting every field:
   `port` as `5432`, `poll_interval_ms` as `1000`, `source_time_zone` as `Europe/Moscow`, `password`
   as the empty string.

4. **The delivered settings parse.**
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~DeliveredConfiguration"`
   passes, asserting `AppSettingsLoader` on the shipped `app/` folder yields
   `new AppSettings(UiLanguage.Ru, AppThemeVariant.Light)`.

5. **The delivered connection section carries no credential, and everything else in it is valid.**
   The same filter covers two tests: the shipped `connection/` folder fails with
   `ConnectionFileProblem.MissingField` whose reason names `password`; and the same folder with a
   password substituted loads successfully with the port, the interval and the time zone parsed.
   The second test exists because `Load` short-circuits at `ValidateFields` (`:71`), so without it
   three quarters of the delivered file is never validated.

6. **No launch key has a default.**
   `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~StartupOptionsTests"`
   passes with `Parse([])`, `Parse(["--nonsense", "value"])` and `Parse(["--config-dir"])` all
   returning failed results. Today all three succeed and return the constants
   (`SemiPlot/SemiPlot.Tests.Unit/UI/StartupOptionsTests.cs:17-24,70-84`).

7. **The seeder no longer writes the settings file.**
   `git grep -n AppSettingsFileWriter -- '*.cs'` returns nothing. Today it returns the writer, its
   test class and the call in the converge verb.

8. **The demo runs off the repository set and leaves nothing behind.** Manual, in order:
   1. `dotnet run --project SemiPlot/SemiPlot.AppHost`
   2. the viewer opens on a chart with data, in Russian, on the light variant
   3. while it runs, `%TEMP%\SemiPlot\ConfigFiles` holds `app\app.yaml` and
      `connection\connection.yaml`, and the second one carries the bench reader password rather than
      an empty one
   4. `git status --short` reports no change under `ConfigFiles/`
   5. stop the AppHost; `%TEMP%\SemiPlot\ConfigFiles` is gone and `%TEMP%\SemiPlot\Logs\semiplot.log`
      still holds the run
   6. start the AppHost again; the previous log is gone and a new one is being written

9. **A missing key is reported where the operator can see it, in Russian.** Manual:
   `dotnet run --project SemiPlot/SemiPlot.UI` with no arguments opens the failure window naming the
   missing key, with Russian text on the light variant, and the process exits 1. Today it starts
   against `C:\DISTR\Config\SemiPlot`.

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with the plus prefix
- document blockers with the warning prefix
- keep the plan in sync with the work actually done

## Solution Overview

**A section is a folder, and every folder behaves identically.** A loader is given a directory, not a
file name. It reads every `*.yaml` in it and merges them into one mapping. Uniformity is the point:
an operator who learns the rule in one folder has learned it everywhere, and a section that grows
from one file to several needs no code change.

**Merging happens at the key level, never at the text level.** Each file is read with
`File.ReadAllText` and parsed on its own into a `Dictionary<string, object?>` with
`WithDuplicateKeyChecking()`, so a key repeated inside one file fails naming that file; then the
dictionaries are merged, and a key present in two files fails naming both. Concatenating the file
texts and parsing once is the cheaper-looking route and is unsound: a file saved without a trailing
newline glues its last key to the next file's first, a `---` or `...` marker turns the result into a
multi-document stream that `Deserialize<T>` refuses, and a `%YAML` directive is a syntax error. All
three report a parser error naming no file, because by then there is only one text. A byte order
mark is not among the reasons: `File.ReadAllText` strips it on either route, which is why the reader
must go through it rather than through a stream with an explicit encoding.

**Files are ordered by `StringComparer.Ordinal`.** With conflicts failing rather than resolving,
order decides nothing about values; it decides only which file an error names first, and an ordinal
order is the same on both CI legs.

**The launch keys carry no defaults.** `StartupOptions.Parse` returns `Result<StartupOptions>` and the
three `Default*` constants are deleted rather than kept as a fallback nobody may reach. The production
paths move to the installer, which is the only component that knows where a site put its files.

**An argument failure is a startup failure.** It takes the route a configuration failure already
takes: a typed error, `ArchiveFailureMapper`, the failure window, exit 1. This removes the one report
that could not reach the operator, the standard error stream of a windowed executable. Because this
path never enters `StartupSequence.Run`, it applies the bootstrap culture itself; without that the
window falls back to the neutral English resource set while every other failure window is Russian.

**The demo consumes a copy, never the original.** The AppHost owns `%TEMP%\SemiPlot`: it copies the
tracked set into `ConfigFiles` beneath it, points `--log-file` into `Logs` beneath it, and removes
`ConfigFiles` when the stand stops. `converge` overwrites `connection/connection.yaml` in that copy
by name rather than adding a second file, because a second file carrying the same keys is exactly
what the section rule rejects.

**`Logs` is swept at the next start, not on stop.** The viewer holds `semiplot.log` open through a
Serilog file sink declared `shared: true` (`SemiPlot/SemiPlot.UI/Program.cs:79-87`), so deleting the
directory during shutdown races that handle; and the log of a run that failed is the one thing worth
keeping after the stand is gone. The sweep deletes file by file and tolerates one still held open,
so a viewer that outlives its stand cannot stop the next one from starting.

## Technical Details

### The delivered set

```
ConfigFiles/
  app/app.yaml
  connection/connection.yaml
```

`app/app.yaml`:

```yaml
locale: ru
theme: light
```

`connection/connection.yaml` carries every key `PostgresConnectionLoader` requires, with the
credential left empty so it cannot start unedited:

```yaml
host: localhost
port: 5432
database: semiplot
user: semiplot_reader
password: ""
source_time_zone: Europe/Moscow
poll_interval_ms: 1000
```

`schema` is the one optional key, defaulted to `public` by the loader, and is left out.

### The section reader

New, in `SemiPlot.Core` because both consumers reference Core and neither references the other:

```
SemiPlot.Core/Configuration/ConfigurationSection.cs
SemiPlot.Core/Configuration/ConfigurationSectionError.cs
```

`SemiPlot.Core.csproj` gains a `PackageReference` to YamlDotNet.

```csharp
public static Result<string> Read(string sectionDirectory, ConfigurationSectionName section)
```

The returned string is the merged mapping serialized once, which the caller hands to its own typed
deserializer unchanged. Steps:

1. The directory is absent -> `SectionProblem.DirectoryMissing`. It exists but holds no `*.yaml` ->
   `SectionProblem.NoFiles`. The two have different remedies, so they are different problems.
2. `Directory.GetFiles(directory, "*.yaml")`, ordered with `StringComparer.Ordinal`.
3. Each file is read with `File.ReadAllText` and deserialized on its own to
   `Dictionary<string, object?>` with `WithDuplicateKeyChecking()`. A throw becomes
   `SectionProblem.Unreadable` naming that file. An empty file deserializes to null and contributes
   nothing; it is not a failure and must not reach step 4 as a null.
4. The dictionaries are merged while recording the owning file of each key. A key already owned
   becomes `SectionProblem.KeyConflict` naming the key and both files.
5. The merged dictionary is serialized and returned.

`ConfigurationSectionError` carries the section identity, the directory, the problem, the key when
there is one, and the file names involved. The section identity exists so the window can keep saying
"interface settings" and "archive connection" with their own remedies instead of collapsing both into
one message about a path.

Step 5 is lossy on scalar typing: every value comes back from step 3 as a string and is re-emitted
unquoted, so a deliberately quoted `"5432"` returns as a bare `5432`. Both current DTOs are unharmed,
and acceptance item 3 is what keeps that from changing silently.

### What the loaders become

| | Today | After |
| --- | --- | --- |
| `AppSettingsLoader.Load` | `(string filePath)`, `<config-dir>/ui/app.yaml` | `(string sectionDirectory)`, `<config-dir>/app` |
| `PostgresConnectionLoader.Load` | `(string filePath)`, `<config-dir>/archive-connection.yaml` | `(string sectionDirectory)`, `<config-dir>/connection` |

Each loader's own `Read` step is replaced by a call to `ConfigurationSection.Read`. Validation and the
typed deserialize stay where they are, and so do the problems that describe them.

File access moves out of the loaders, and the problems that described file access go with it:
`AppSettingsProblem.NotFound` (`AppSettingsError.cs:7`) and `ConnectionFileProblem.NotFound`
(`ConnectionFileError.cs:7`) lose their only writer and are deleted, together with their mapper
branches and their strings in both resource sets. `Unreadable` and `Unparseable` are decided by
inspection during task 2 rather than assumed: the typed deserialize of the merged text still runs
inside each loader, so an arm still reachable from there stays and an arm that is not is deleted with
the rest. A rule of the repository decides this, not taste: an enum arm with no production writer is
deleted, not tested.

### Argument parsing

```csharp
public static Result<StartupOptions> Parse(string[] args)
```

Failure arms, each a `StartupArgumentsError` carrying the key it is about:

| Condition | Reported as |
| --- | --- |
| A required key is absent | `Missing`, naming the key |
| A key appears with no value after it | `ValueMissing`, naming the key |
| A key the parser does not know | `Unknown`, naming what was given |
| `--logging-level` with an unusable value | `ValueInvalid`, naming the key and the accepted set |

`Console.Error.WriteLine` leaves `StartupOptions` entirely; the window is the only report.

`ArchiveFailureMapper` gains an arm that is not about the archive. The name is left alone: it is the
one place a remedy is written, which is the property that matters, and renaming it would touch every
call site for no behavioural gain.

### Startup order

`Program.Main` gains one step ahead of everything else, because the logger's own path is an argument:

1. `StartupOptions.Parse(args)`. On failure: apply the bootstrap culture, create no logger, show the
   window through `App.Run(null, failure)`, return 1.
2. `CreateLogger(options.LogFilePath, options.LoggingLevel)`, unchanged, now on values that exist.
3. `StartupSequence.Run(options)`, unchanged in shape (`StartupSequence.cs:28-40`).

Step 1 produces no log line by construction: there is no file to write to, and the argument that
would have named it is the thing that failed. The variant needs no handling, because `App.axaml:5`
declares `Light` and every pre-configuration window already lands on it.

### The demo's directories

`%TEMP%\SemiPlot\ConfigFiles` and `%TEMP%\SemiPlot\Logs`, both fixed paths. One stand runs at a time,
so no per-run suffix is needed and a crashed run is cleaned by the next start.

In `SemiPlot/SemiPlot.AppHost/AppHost.cs`, replacing the two paths built at `:19-20`:

1. Remove both directories if an earlier run left them, file by file, tolerating one still held open
   by a viewer that outlived its stand.
2. Create both, and copy the tracked `ConfigFiles` into the first. The source is two levels above
   `builder.AppHostDirectory`, which is `SemiPlot/SemiPlot.AppHost`, not one as the existing
   `Path.Combine(builder.AppHostDirectory, "..", "Artifacts", ...)` at `:19`. A missing source
   directory fails the AppHost immediately rather than producing an empty copy the viewer reports
   later.
3. Pass the configuration path as `--config-dir` to `converge` and to the viewer, the log path as
   `--log-file`, and add the now-required `--logging-level` to the viewer's arguments (`:48` passes
   two of the three keys today).
4. Remove `ConfigFiles` alone on `IHostApplicationLifetime.ApplicationStopping`, resolved from
   `DistributedApplication.Services` after `builder.Build()`. `DistributedApplication` is an `IHost`,
   so the lifetime is registered by the host infrastructure. `ApplicationStopping` does not fire when
   the AppHost is killed rather than stopped; step 1 is the backstop for that path alone.

## Implementation Steps

### Task 1: Add the section reader to Core

**Files:**
- Create: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSection.cs`
- Create: `SemiPlot/SemiPlot.Core/Configuration/ConfigurationSectionError.cs`
- Modify: `SemiPlot/SemiPlot.Core/SemiPlot.Core.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Unit/Core/Configuration/ConfigurationSectionTests.cs`

- [x] add the YamlDotNet package reference to `SemiPlot.Core`
- [x] add `ConfigurationSectionError` with a `SectionProblem` enum (`DirectoryMissing`, `NoFiles`, `Unreadable`, `KeyConflict`), the section identity, the directory, the key and the file names
- [x] add `ConfigurationSection.Read`: ordinal file order, `File.ReadAllText` per file, `WithDuplicateKeyChecking()`, a null deserialize result treated as an empty contribution, merge with an ownership map, serialize once
- [x] write tests for the merge of two disjoint files and for the ordering being ordinal
- [x] write tests for the failure cases: the same key in two files, the same key twice in one file, an absent folder, a folder with no `*.yaml`
- [x] write tests for the two cases that must not fail: an empty `*.yaml`, and a file written with a byte order mark whose first key must still arrive
- [x] write the round-trip test feeding the connection body through the reader into `PostgresConnectionDto` and asserting the port, the interval, the time zone and the empty password
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - must pass before task 2

### Task 2: Move both loaders onto the section reader

Everything that reads the two path constants moves in this task, including the seeder's settings
writer, so the solution compiles when the task ends.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Startup/AppSettingsLoader.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/AppSettingsError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupProbe.cs`
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileError.cs`
- Delete: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/AppSettingsFileWriter.cs`
- Delete: `SemiPlot/SemiPlot.Tests.Unit/Tools/AppSettingsFileWriterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/Converge.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppSettingsLoaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupSequenceTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ConvergeTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs` (the two `NotFound` branches and the
  `ConnectionFileProblem.Unreadable` remedy lose their writer, so the mapper must drop them to compile)
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/ArchiveFailureMapperTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Errors/DataErrorTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/AppConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/ConnectionFileWriterTests.cs`

- [x] change both `Load` methods to take a section directory and read it through `ConfigurationSection.Read`
- [x] rename `StartupSequence.SettingsDirectoryName` to `app`, delete `SettingsFileName` and `SettingsPath`, delete `StartupProbe.ConnectionFileName`, point the probe at the `connection` directory
- [x] delete `AppSettingsProblem.NotFound` and `ConnectionFileProblem.NotFound`, and decide by inspection whether `Unreadable` and `Unparseable` keep a production writer, deleting whichever does not
- [x] delete `AppSettingsFileWriter`, its call at `Converge.cs:66` and its test class, which are the remaining callers of `SettingsPath`
- [x] update the four dependent test files (`StartupSequenceTests.cs:109,113`, `StartupProbeTests.cs:207`, `ConvergeTests.cs:53`, and both loader test classes) to address section directories
- [x] write a test per loader for a section failure surfacing as a failed `Result` rather than a throw
- [x] run `dotnet test SemiPlot.slnx` - must pass before task 3

### Task 3: Report a section failure in the startup window

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/ArchiveFailureMapperTests.cs`

- [x] add the `ConfigurationSectionError` arm to `ArchiveFailureMapper.Map`, switching on the section identity so the two sections keep their own titles and remedies
- [x] add title, detail and remedy keys for each `SectionProblem` arm to both resource files, keeping the placeholders identical between the sets
- [x] make the conflict detail name the key and both files, since that is the whole reason the check exists
- [x] delete the mapper branches and the resource strings left without a writer by task 2, in both sets, and the tests that pinned them
- [x] write mapper tests covering every new arm
- [x] run tests - must pass before task 4

### Task 4: Ship the configuration set and gate it with the production loaders

**Files:**
- Create: `ConfigFiles/app/app.yaml`
- Create: `ConfigFiles/connection/connection.yaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`
- Create: `SemiPlot/SemiPlot.Tests.Unit/DeliveredConfigurationTests.cs`

- [x] write `ConfigFiles/app/app.yaml` with `locale: ru` and `theme: light`
- [x] write `ConfigFiles/connection/connection.yaml` with every required key and an empty `password`
- [x] add a `None Include` item copying `ConfigFiles\**\*` into the test output directory, linked under `ConfigFiles`
- [x] write a test asserting `AppSettingsLoader` on the shipped `app` folder yields `(Ru, Light)`
- [x] write a test asserting `PostgresConnectionLoader` on the shipped `connection` folder fails with `ConnectionFileProblem.MissingField` and a reason naming `password`; the reason is prose, so assert the kind structurally and the name as a substring
- [x] write a test that substitutes a password into the shipped body and asserts a successful load with the port, interval and time zone parsed, so the three quarters of the file past `ValidateFields` are gated too
- [x] run tests - must pass before task 5

### Task 5: Make the launch keys required and report a bad one in the window

`Program.Main` reads `Parse`'s result, so the signature change and the window wiring are one task.

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/StartupOptions.cs`
- Create: `SemiPlot/SemiPlot.UI/StartupArgumentsError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Program.cs`
- Modify: `SemiPlot/SemiPlot.UI/Startup/StartupSequence.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/StartupOptionsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Startup/StartupProbeTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/ArchiveFailureMapperTests.cs`

- [x] add `StartupArgumentsError` with a `StartupArgumentsProblem` enum (`Missing`, `ValueMissing`, `Unknown`, `ValueInvalid`), the key and the accepted values
- [x] change `Parse` to return `Result<StartupOptions>`, delete `DefaultConfigDir`, `DefaultLogFilePath` and `DefaultLoggingLevel`, reject an unknown argument and a valued argument with nothing after it, and drop the `Console.Error` fallback
- [x] expose the bootstrap culture from `StartupSequence` so a path that never runs it can still apply it
- [x] rewire `Program.Main`: parse first, and on failure apply the bootstrap culture, create no logger, run `App.Run(null, failure)` and return 1
- [x] add the `StartupArgumentsError` arm to `ArchiveFailureMapper.Map` with title, detail and remedy keys in both resource sets
- [x] rewrite the tests that assert defaults so they assert the failure and its key, and fix `StartupProbeTests.cs:203` for the new signature
- [x] write tests for each failure arm, for the all-arguments success case, and mapper tests asserting the key and the accepted values reach the text
- [x] run `dotnet test SemiPlot.slnx` - must pass before task 6

### Task 6: Point the seeder at the new layout

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs`
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/SeederCommand.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/ConnectionFileWriterTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ConvergeTests.cs`

- [x] make `ConnectionFileWriter` write `connection/connection.yaml`, creating the section directory
- [x] correct the `--config-dir` help text at `SeederCommand.cs:85`, which names both old files
- [x] update the writer's test for the new path
- [x] assert in the converge test that the written section loads through `PostgresConnectionLoader`
- [x] confirm `git grep -n AppSettingsFileWriter -- '*.cs'` returns nothing
- [x] run `dotnet test SemiPlot.slnx` - must pass before task 7

### Task 7: Give the live demo its own directories

**Files:**
- Modify: `SemiPlot/SemiPlot.AppHost/AppHost.cs`
- Create: `SemiPlot/SemiPlot.AppHost/DemoDirectories.cs`

- [x] add `DemoDirectories` owning `%TEMP%\SemiPlot\ConfigFiles` and `%TEMP%\SemiPlot\Logs`: remove both at start file by file tolerating a held-open log, create both, copy the tracked set into the first
- [x] resolve the tracked `ConfigFiles` two levels above `builder.AppHostDirectory`, failing immediately when it is absent
- [x] replace the two paths built at `AppHost.cs:19-20`, pass them as `--config-dir` and `--log-file`, and add `--logging-level` to the viewer's arguments
- [x] remove `ConfigFiles` on `ApplicationStopping`, resolved from `DistributedApplication.Services`
- [x] delete the orphaned `SemiPlot/Artifacts/bench-config` by hand and confirm nothing still names it
- [x] run `dotnet build SemiPlot.slnx` and start the stand once to confirm the copy lands - must pass before task 8

### Task 8: Verify acceptance criteria

- [x] run every command in Acceptance Evidence and record the result
- [x] run the manual demo smoke list end to end, including the restart that proves the log sweep
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes` and `dotnet terse` over the touched files; both exit 0
- [x] run `dotnet build SemiPlot.slnx` and `dotnet test SemiPlot.slnx`

### Task 9: Update documentation

**Files:**
- Modify: `CLAUDE.md`
- Modify: `readme.md`
- Modify: `docs/architecture/overview.md`
- Modify: `docs/architecture/bench.md`
- Modify: `docs/architecture/data-integration.md`
- Modify: `docs/architecture/postgres-topology.md`
- Modify: `docs/architecture/ui-text.md`
- Modify: `docs/architecture/ui-theme.md`
- Modify: `docs/architecture/charting.md`

- [x] `overview.md`: state the section rule and the merge, replace the two-file table (`:104-112`) with the section layout, drop the Default column from the command-line table (`:118-131`) and state that all three keys are required
- [x] `data-integration.md`: correct the connection path and the startup order (`:344,375,378`)
- [x] `bench.md`: replace the `ui/app.yaml` paragraphs (`:222-232`) with the demo directories, correct the standalone `converge` recipe (`:209-214`), and correct the log path at `:247`
- [x] `CLAUDE.md`: correct the converge recipe (`:55,63`) and add the three required keys to the run commands
- [x] `readme.md`: rewrite the configuration row (`:52`) and the run comment (`:63`)
- [x] correct the remaining path mentions: `postgres-topology.md:96`, `ui-text.md:19`, `ui-theme.md:6`, `charting.md:205,211`
- [x] move this plan to `docs/plans/completed/` (not moved - archiving belongs to the delivery step, which runs after the operator has tested the branch)

## Post-Completion

*No checkboxes: these need action outside this codebase.*

**Cards to file after this lands**

- **Installer.** `ConfigFiles` reaches a machine by hand until one exists. The installer copies the
  set to `C:\DISTR\Config\SemiPlot`, creates the log directory, and writes a shortcut carrying all
  three keys. SemiStep's `Installer/SemiStep.iss:75-84` is the working precedent.
- **Application settings window.** #85 is closed on the file alone; its other half, window size and
  position, sidebar width, which panels are open, the minimap span, and writing the file back
  atomically, has no card. Two constraints the section rule creates for it: the writer targets one
  named file inside `app/` rather than "the settings file", and it must not write a key another file
  in that folder already owns, or the next start fails on a conflict the save planted.
  `SemiPlot.UI` writes no file at all today, so the write path is new work. #81 already reserves the
  menu entry that opens it.
- **Archive credentials.** Where the connection password comes from and how it is stored. The empty
  `password` shipped here is the deliberate placeholder it starts from.
- **Reconnect without a restart.** The container is built once with the connection string baked in
  (`SemiPlot/SemiPlot.DataSource.Postgres/PostgresDataServiceCollectionExtensions.cs:21`) and the
  whole view-model graph is constructed from the first successful read
  (`SemiPlot/SemiPlot.UI/App.axaml.cs:128-170`). The view models already replace and dispose cleanly
  (`SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs:80-95,110-118`); what is missing is an
  owner for `TrendCoordinator`, a rebindable `ObserveArchiveConnection` (`:57-62` throws on a second
  call by construction), and a window that exists while no connection does. Depends on #80 and #81.

**Known consequences to watch**

- A second `SemiPlot.DataSource.*` provider gets its own section folder. Two providers sharing
  `connection/` would merge `host` and `port` into one mapping and conflict by design.
- `Directory.GetFiles(dir, "*.yaml")` matches case-insensitively on Windows and case-sensitively on
  Linux, so a fixture named `APP2.YAML` is read by the `unit-windows` leg and ignored by the `linux`
  leg. Test fixtures stay lowercase.
- The merge's reserialization is lossy on scalar typing. A future key whose string value looks like
  `no`, `on`, `1.10` or `12:30` needs its own round-trip test before it is trusted.

**Manual verification**

- The demo smoke list in Acceptance Evidence is manual and is run on a machine with Docker available.
- A start with no arguments, with one key missing, and with an unknown key: each must open the
  failure window naming the key, in Russian, on the light variant.

**Executed by exec:**

- branch: config-delivery

## Verify it yourself

Each check names what fails before the change and what passes after. `9ab318f` is the last commit
before this branch; the post-fix commits are on `config-delivery`.

### The repository ships the configuration

```powershell
git ls-files ConfigFiles
```

Lists `ConfigFiles/app/app.yaml` and `ConfigFiles/connection/connection.yaml`. On `master` it
returns nothing: the directory exists and is empty.

### The shipped files are gated by the production loaders

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~DeliveredConfiguration"
```

Four tests. The app section yields `(Ru, Light)`; the connection section fails with
`ConnectionFileProblem.MissingField` naming `password`, which is the proof that no credential was
committed; the same body with a password substituted loads with the port, interval and time zone
parsed; and the seeder's target name is asserted equal to the folder the loader reads, so renaming
either constant fails the build rather than the installation.

Break it deliberately: put a password into `ConfigFiles/connection/connection.yaml` and the second
test goes red.

### A section folder merges, and a conflict names both files

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~ConfigurationSection"
```

Twenty-three tests. The ones worth reading are the two conflict cases: the same key in two files
fails naming the key and both file names, and the same key twice in one file fails naming the key
and that one file. To see it by hand, copy `ConfigFiles/app/app.yaml` to
`ConfigFiles/app/app-old.yaml` and start the viewer; the failure window names `locale` and both
files. Delete the copy afterwards.

### No launch key has a default

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~StartupOptionsTests"
```

Twenty-five tests. `Parse([])`, `Parse(["--nonsense", "value"])`, `Parse(["--config-dir"])` and a
blank value for any key all return failed results. On `9ab318f` the first three succeed and return
`C:\DISTR\Config\SemiPlot`.

By hand:

```powershell
dotnet run --project SemiPlot/SemiPlot.UI
```

Opens the failure window naming the first missing key, in Russian on the light variant, and exits
1. Nothing is written to the standard error stream, which is the point: a `WinExe` has no console
when it starts from a shortcut.

### An unusable log path is reported rather than swallowed

```powershell
dotnet run --project SemiPlot/SemiPlot.UI -- --config-dir SemiPlot\Artifacts\dev-config --log-file SemiPlot\Artifacts --logging-level info
```

`--log-file` names an existing folder, so the file cannot be opened. The window names the path and
the reason, and the process exits 1. Before this branch the run continued with a null sink and said
nothing anywhere.

### The demo stand runs off the repository set and leaves nothing behind

```powershell
dotnet run --project SemiPlot/SemiPlot.AppHost
```

While it runs, `%TEMP%\SemiPlot\ConfigFiles` holds `app\app.yaml` and `connection\connection.yaml`,
the second carrying the bench reader password that `converge` wrote over the shipped empty one.
`git status --short -- ConfigFiles` reports nothing: the tracked set is never written to.

Stop the stand with Ctrl+C in its console, not by killing the process — `ApplicationStopping` is
what removes the configuration copy, and a kill skips it. After the stop, `%TEMP%\SemiPlot\ConfigFiles`
is gone and `%TEMP%\SemiPlot\Logs\semiplot.log` still holds the run. Start it again and the previous
log is swept.

### The seeder no longer knows the settings format

```powershell
git grep -n AppSettingsFileWriter -- '*.cs'
```

Returns nothing. On `9ab318f` it returns the writer, its test class and the call in the converge
verb.
