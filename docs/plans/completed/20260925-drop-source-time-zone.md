# Read the archive in the machine's time zone

## Overview

`connection/connection.yaml` carries `source_time_zone` (`ConfigFiles/connection/connection.yaml:6`), and the
loader refuses to start without it (`PostgresConnectionLoader.cs:183`, `:240-254`). The key names the zone in
which the SCADA stamps `trends.t`, a `timestamp without time zone` (`docs/architecture/scada-archive.md:78`,
`:100`). The SCADA, its PostgreSQL archive and the viewer always run on one machine, and an archive is never
opened on another. The SCADA stamps rows in the machine's Windows zone (`[DEC:machine-time-zone]`), so
the key always equals the machine's zone and carries no information. It goes.

The archive-to-UTC conversion stays: the read path converts every row to UTC and every query bound back
(`HistoryRowFold.cs:39`, `BucketedRowFold.cs:41`, `PostgresDataProvider.cs:141-142`, `:257`, `:273`), the live
edge compares with `DateTime.UtcNow`, and the display converts UTC to local (`LocalTimeAxis.cs:12`,
`ChartHoverReadout.cs:39`, `MinimapViewModel.cs:144`). Only the zone's source changes: `TimeZoneInfo.Local`
in place of a configured identifier.

The bench seeder already stamps rows on the machine's clock (`DateTime.Now` at `Converge.cs:43` and
`Program.cs:95`), so the viewer and the writer agree by construction.

## Context (from discovery)

- `SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`: `:29` the DTO property, `:53`
  `SourceTimeZoneKey`, `:67-70` the deserializer with `IgnoreUnmatchedProperties()` (a leftover key in an old
  file loads clean once the DTO property is gone), `:111` `ResolveTimeZone` call, `:144-146`
  `Map(dto, sourceTimeZone)`, `:183` the key in the required-field list, `:240-254` `ResolveTimeZone` raising
  `ConnectionFileProblem.UnknownTimeZone`.
- `PostgresConnectionSettings.cs:16` `TimeZoneInfo SourceTimeZone`, `:60` in `ToString`.
- `PostgresDataServiceCollectionExtensions.cs:22` `new ArchiveTimeConverter(settings.SourceTimeZone)`.
- `SemiPlot.Core/Data/Errors/ConnectionFileError.cs:10` `UnknownTimeZone`;
  `SemiPlot.UI/Messages/ArchiveFailureMapper.cs:113`; `Resources.resx:171`, `Resources.ru.resx:171`
  (`FailureConnectionFileUnknownTimeZoneRemedy`).
- `SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs:22`, `:37` write the key; `Converge.cs:62` passes it.
- `SemiPlot.Tests.Integration/ArchiveProviderFactory.cs:17`, `:43` pin `Europe/Berlin` through
  `PostgresConnectionSettings.SourceTimeZone`; a zone other than UTC is what makes "converted exactly once"
  observable (`:15-16`). Seven integration classes build a parallel `ArchiveTimeConverter` from it
  (`RealtimeSubscriptionTests:39`, `RealtimePollReadTests:53`, `RealtimeEmptyArchiveTests:34`,
  `PostgresHistoryReadTests:75`, `PostgresExtentReadTests:134`, `BreakRenderArchiveJourneyTests:58`,
  `LiveEdgeArchiveJourneyTests:34`); `ExplainPlanTests` uses `Utc`.
- Tests that read the removed symbols or assert a configured zone (unit project):
  `Core/Configuration/ConfigurationSectionTests.cs:180` (`dto.SourceTimeZone`),
  `UI/Settings/SettingsViewModelTests.cs:197-210` (`SourceTimeZoneKey` at `:209`),
  `ConnectionFileWriterTests.cs:46`, `DeliveredConfigurationTests.cs:73`,
  `UI/Settings/SettingsSaveTests.cs:227-239` (`ARefusalKeepsTheLoadersCause`, which uses an unknown zone to
  make the loader fail), `Errors/DataErrorTests.cs:25`, `:29`, `UI/Messages/ArchiveFailureMapperTests.cs:325`,
  `UI/Messages/FailureSeverityTests.cs:94`, and in `Postgres/PostgresConnectionLoaderTests.cs` the `_validFields`
  row at `:30`, `:59-68`, `:180`, `:206-217`, `:305-321`, `:339-350`; integration `ConvergeTests.cs:53`.
- Docs that state the key: `data-integration.md:20`, `:202`, `:208`, `:325` (error-kind table), `:434`,
  `:449-451`, `:526-527`; `overview.md:207-208`; `bench.md:234`, `:309`; `scada-archive.md:103-104`.
  `readme.md:47` names the zone in the settings-window bullet (it stays in `connection.yaml`); `CLAUDE.md`
  does not name it.

## Development Approach

- Testing approach: regular, code first, then tests, within the same task.
- Each task ends with `dotnet test SemiPlot.slnx` green and is its own commit.
- Update this plan when the scope changes during implementation.

## Testing Strategy

- The zone stays an injected `TimeZoneInfo` on `PostgresConnectionSettings`, so no test downstream of the
  loader depends on the machine's zone: the integration suite keeps `ArchiveProviderFactory`'s
  `Europe/Berlin`, and CI's UTC runner keeps observing the conversion.
- The loader's own suite keeps one test that a file without the key loads with `TimeZoneInfo.Local`: it is the
  only pin on the zone's source. It tells `Local` from `Utc` on a machine whose zone is not UTC and cannot on
  the UTC runner; that is accepted.
- A test outside the loader's suite that compares a loaded zone with a configured identifier or with
  `TimeZoneInfo.Local` pins nothing after the change and is deleted, not rewritten.
- Tests over provider errors assert by type and field, never on wording (CLAUDE.md).

## Acceptance Evidence

1. **The key is gone from the code.** `git grep -n "SourceTimeZoneKey\|UnknownTimeZone\|ResolveTimeZone" -- SemiPlot ConfigFiles`
   returns nothing, and `git grep -n "source_time_zone" -- SemiPlot ConfigFiles` returns exactly one line: the
   leftover-key test in `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`.
2. **The loader fills the machine's zone.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PostgresConnectionLoader"`
   passes with a non-zero count; one test loads a file with no `source_time_zone` and gets
   `SourceTimeZone == TimeZoneInfo.Local`, and one loads a file that still carries the key and succeeds.
3. **The integration suite keeps its pinned zone.** `dotnet test SemiPlot/SemiPlot.Tests.Integration/SemiPlot.Tests.Integration.csproj`
   passes, and `git diff master -- SemiPlot/SemiPlot.Tests.Integration/ArchiveProviderFactory.cs` is empty.
4. **The shipped set and converge carry no zone.** `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~DeliveredConfigurationTests|FullyQualifiedName~ConnectionFileWriterTests"`
   passes with a non-zero count, and `git grep -n "source_time_zone" -- ConfigFiles` returns nothing.
5. **Both suites green.** `dotnet test SemiPlot.slnx`; `dotnet format SemiPlot.slnx --verify-no-changes`;
   `dotnet terse` over the touched `.cs` files.
6. **The docs describe no configured zone.** `git grep -n "source_time_zone\|UnknownTimeZone\|configured source time zone" -- docs/architecture`
   returns nothing.
7. **The SCADA stamps rows in the machine's zone: a recorded decision, not a measurement.** The dump
   `[MEAS:dump-20260805]` shows the SCADA process stamps `t` on its own clock (gaps align with project
   stop/start rows in `messages` within 30 ms; day partitions are created for the SCADA's yesterday, today and
   tomorrow), but not which zone that clock is in: every stamp is zone-less and no row can be compared with a
   known instant. The clock is taken to be in the machine's Windows zone. The decision is recorded as
   `[DEC:machine-time-zone]` in `sources.md` and cited at `scada-archive.md:102`.
   `git grep -n "DEC:machine-time-zone" docs/architecture` returns the `sources.md` row and the
   `scada-archive.md` citation.
8. **Stand (manual).** `dotnet run --project SemiPlot/SemiPlot.AppHost`: the viewer opens on the live archive
   and the newest point sits at the current local time on the axis.

## Progress Tracking

- Mark completed items `[x]` when done; new tasks get `+`, blockers `!`.

## Solution Overview

**The zone stays a seam, its source changes.** `PostgresConnectionSettings.SourceTimeZone` stays a
`TimeZoneInfo`; `PostgresConnectionLoader` fills it with `TimeZoneInfo.Local` instead of resolving a
configured identifier. `AddPostgresData()` is unchanged. Tests that build settings directly
(`ArchiveProviderFactory`, `ConnectionSettingsFactory`) keep passing a fixed zone, so nothing downstream of
the loader changes.

**The key's code goes with it.** `SourceTimeZoneKey`, the DTO property, the required-field entry,
`ResolveTimeZone`, `ConnectionFileProblem.UnknownTimeZone`, its mapper arm and its remedy in both resx files
are deleted, and so are the tests over them. `IgnoreUnmatchedProperties()` makes a leftover key in an
existing file harmless.

**Nothing writes the key.** `ConnectionFileWriter` loses its zone parameter and line, `converge` stops passing
`TimeZoneInfo.Local.Id`, and the shipped `connection.yaml` loses the line.

## Technical Details

- `PostgresConnectionLoader.Map(dto)` passes `TimeZoneInfo.Local` to the settings constructor; the zone
  parameter `Map` first kept had one constant argument and was dropped in review. `StartupProbe.Run` logs
  the applied zone's identifier at Information once the section loads.
- `SettingsViewModelTests.ASaveThatRewritesTheConnectionFileKeepsTheTimeZone` is deleted:
  `ConfigurationSectionWriterTests.AKeyTheFormatDoesNotModelSurvivesTheWrite` (`:71-82`) already pins that an
  unedited key survives a rewrite, and `SettingsViewModelTests.ASaveSendsOnlyTheChangedKeyOfASection`
  (`:180-195`) pins the port write through the view model.
- `SettingsSaveTests.ARefusalKeepsTheLoadersCause` keeps what it pins (a save refusal keeps the loader's
  `CausedBy` exception) through a different failure: the sandbox file's port becomes `"scada-01"`, and the
  test asserts `Kind == ConnectionFileProblem.Unparseable`, the `Path`, and a single `ExceptionalError` whose
  `Exception` is a `YamlException` (`SettingsSaveTests.cs:240`), the assertion
  `AnUnparseableSectionCarriesItsCausingExceptionAndNotItsText` makes over the same port
  (`PostgresConnectionLoaderTests.cs:330`).
- In `PostgresConnectionLoaderTests`: the `source_time_zone` row leaves `_validFields` (`:30`), so every other
  test uses the new file shape; `:59-68` becomes the `TimeZoneInfo.Local` test; the zone row at `:180` goes;
  `:206-217` and `:339-350` are deleted; in `:305-321` the zone row is replaced by
  `KindOf(Compose(Replace("host", "scada-01")))` mapped to `HostNotIPv4`, keeping three states under the same
  name; the leftover-key test adds the key back explicitly.
- `DataErrorTests.cs:29`'s reason becomes another field (`"host is blank"`) when the zone row at `:25` goes.

## Implementation Steps

### Task 1: The loader takes the machine's zone

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileError.cs`
- Modify: `SemiPlot/SemiPlot.UI/Messages/ArchiveFailureMapper.cs`
- Modify: `SemiPlot/SemiPlot.UI/Localization/Resources.resx`, `Resources.ru.resx`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/Postgres/PostgresConnectionLoaderTests.cs`,
  `Errors/DataErrorTests.cs`, `UI/Messages/ArchiveFailureMapperTests.cs`, `UI/Messages/FailureSeverityTests.cs`,
  `UI/Settings/SettingsSaveTests.cs`, `UI/Settings/SettingsViewModelTests.cs`,
  `Core/Configuration/ConfigurationSectionTests.cs`, `ConnectionFileWriterTests.cs`,
  `DeliveredConfigurationTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Integration/ConvergeTests.cs`

- [x] `Map(dto, TimeZoneInfo.Local)`; delete the DTO property, `SourceTimeZoneKey`, the required-field entry
      and `ResolveTimeZone`
- [x] delete `ConnectionFileProblem.UnknownTimeZone`, its mapper arm and its remedy key in both resx files
- [x] `PostgresConnectionLoaderTests`: the edits Technical Details lists, including the `TimeZoneInfo.Local`
      test and the leftover-key test
- [x] delete the rows over the removed arm (`DataErrorTests.cs:25` with `:29`'s reason moved to `host`,
      `ArchiveFailureMapperTests.cs:325`, `FailureSeverityTests.cs:94`) and rewrite
      `SettingsSaveTests.ARefusalKeepsTheLoadersCause` over `Unparseable` as Technical Details states
- [x] delete the zone assertions at `ConfigurationSectionTests.cs:180`, `ConnectionFileWriterTests.cs:46`,
      `DeliveredConfigurationTests.cs:73` and integration `ConvergeTests.cs:53`, and delete
      `SettingsViewModelTests.ASaveThatRewritesTheConnectionFileKeepsTheTimeZone`
- [x] run tests - must pass before task 2

### Task 2: Nothing writes the key

**Files:**
- Modify: `SemiPlot/SemiPlot.Tools.ArchiveSeeder/ConnectionFileWriter.cs`, `Converge.cs`
- Modify: `ConfigFiles/connection/connection.yaml`
- Modify: every test whose inline YAML still carries `source_time_zone` (`git grep -n "source_time_zone" -- SemiPlot`),
  except the leftover-key test

- [x] `ConnectionFileWriter` loses the zone parameter and line; `converge` stops passing it
- [x] the shipped `connection.yaml` loses the line
- [x] remove the key from every inline test YAML but the leftover-key test
- [x] acceptance item 1's two greps give their expected output
- [x] run tests - must pass before task 3

### Task 3: Update documentation

- [x] `data-integration.md`: the responsibility row (`:20`), the time section (`:202`, `:208`: the provider
      converts in the machine's zone, because the SCADA, the archive and the viewer share one machine), the
      error-kind table (`:325`), the connection-file sample and key list (`:434`, `:449-451`), and triage
      step 5 (`:526-527`)
- [x] `overview.md:207-208`, `bench.md:234`, `:309`, and `scada-archive.md:103-104`: no configured zone
- [x] add `[DEC:machine-time-zone]` to `sources.md` (the SCADA, its archive and the viewer share one machine,
      and the SCADA stamps `t` in that machine's Windows zone) and cite it at `scada-archive.md:100`
- [x] acceptance items 6 and 7 give their expected output

### Task 4: Verify acceptance criteria

- [x] run acceptance items 1 to 7 and record each result and count
      ! measured 2026-09-25; items 1, 2 and 7 re-run at `72990a2`: item 1, the first grep returns nothing and
      the `source_time_zone` grep returns one line, `PostgresConnectionLoaderTests.cs:72`
      (`ALeftoverTimeZoneKeyIsIgnored`); item 2, the `PostgresConnectionLoader` filter passed 52 of 52,
      including `AValidSectionCarriesTheMachinesTimeZone` (`:59`) and `ALeftoverTimeZoneKeyIsIgnored` (`:70`);
      item 3, the integration project passed 97 of 97 and `git diff master -- .../ArchiveProviderFactory.cs`
      is empty; item 4, the
      `DeliveredConfigurationTests|ConnectionFileWriterTests` filter passed 6 of 6 and the `ConfigFiles` grep
      returns nothing; item 5, `dotnet test SemiPlot.slnx` passed 1169 unit and 97 integration tests,
      `dotnet format --verify-no-changes` exit 0, `dotnet terse` over the 16 `.cs` files changed against
      `master` exit 0; item 6, the grep returns nothing; item 7, the grep returns `sources.md:76` and
      `scada-archive.md:102` plus four `data-integration.md` citations (`:20`, `:209`, `:454`, `:534`) and
      `overview.md:171`
- [x] acceptance item 8 is the operator's (walked on the stand)
      ! not run by the agent: `dotnet run --project SemiPlot/SemiPlot.AppHost` and the newest point at the
      current local time are the operator's stand walk
- [x] the move of this plan to `docs/plans/completed/` happens in the shipping commit
      ! not done by the agent: ship moves the plan in the shipping commit

## Post-Completion

- SemiBase's commissioning docs still tell the installer to write the zone
  (`SemiBase/docs/architecture/overview.md:68`, `SemiBase/docs/deployment.md:180`). The key is ignored, so
  nothing breaks; a SemiBase issue corrects the two lines.
- If an installation ever shows every reading shifted by a whole number of hours, the SCADA stamps rows in a
  zone other than the machine's: the key returns as an optional override defaulting to the machine's zone.
  The check: `SELECT max(t) FROM trends WHERE l = 0;` beside the Windows clock while the SCADA runs.

**Executed by exec:**
- branch: drop-source-time-zone

## Verify it yourself

Automated, from the repository root:

```powershell
git grep -n "source_time_zone" -- SemiPlot ConfigFiles
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~PostgresConnectionLoader"
dotnet test SemiPlot.slnx
```

The grep prints one line, the leftover-key test. The filter passes 52, including
`AValidSectionCarriesTheMachinesTimeZone` (a zone other than UTC on the machine is what makes it tell `Local`
from `Utc`; on the UTC CI runners it cannot) and `ALeftoverTimeZoneKeyIsIgnored`. The full run is 1169 unit
and 97 integration; `ArchiveProviderFactory` still pins `Europe/Berlin`, so the integration suite does not
depend on the machine's zone.

On the stand (`dotnet run --project SemiPlot/SemiPlot.AppHost`), acceptance item 8: the viewer opens on the
live archive, the newest point sits at the current local time on the axis, and `%TEMP%\SemiPlot\Logs\semiplot.log`
carries "Reading the archive in the time zone Russian Standard Time" (or the machine's zone id). An existing
dev-config whose `connection.yaml` still carries `source_time_zone` loads as before.
