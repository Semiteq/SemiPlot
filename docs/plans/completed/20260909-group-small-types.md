# Group small types into their consumers' files

## Overview

`CLAUDE.md`, File Layout: a file holds one concept named for its primary type plus the small types
only it uses; related small types group together rather than each getting a three-line file, and a
grouped type moves out when it gains a second consumer. The tree has drifted both ways: five files
hold a small type that only its producing file and at most one other reader use, and two types that
several files read sit inside another type's file. This pass moves them, changes no behaviour, and corrects the stale
backlog entry that described the drift.

## Context (from discovery)

Inventory of 2026-09-09 over every non-test `.cs` file under 25 lines of code. Consumer counts
exclude the declaring file and tests.

Folds (the producing file plus at most one outside reader; the splits below apply the same measure in reverse):

| file | type | consumer | lands in |
| --- | --- | --- | --- |
| `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionDto.cs` (24 lines) | `PostgresConnectionDto`, internal sealed class | `PostgresConnectionLoader.cs` (218 lines) | `PostgresConnectionLoader.cs` |
| `SemiPlot/SemiPlot.UI/Legend/TrendLegendGroupViewModel.cs` | public sealed record | `TrendLegendViewModel.cs` (29 lines) and `TrendLegendView.axaml` `x:DataType` | `TrendLegendViewModel.cs` (same namespace, the XAML reference is unchanged) |
| `SemiPlot/SemiPlot.UI/Chart/ChartPressAction.cs` | public enum | `ChartPressRouter.cs` (17 lines) returns it; `TrendChartView.axaml.cs:164-177` switches on the router's result | `ChartPressRouter.cs` |
| `SemiPlot/SemiPlot.UI/Chart/DataRectPixels.cs`, `SemiPlot/SemiPlot.UI/Chart/OverlayPlacement.cs` | two readonly record structs | `ChartCursorOverlay.cs` (18 lines) produces both; `TrendChartView.axaml.cs:386,392` is the only outside reader | `ChartCursorOverlay.cs` |

`ChartPressRouter.cs` and `ChartCursorOverlay.cs` become the hosts of their groups; neither moves into
`TrendChartView.axaml.cs` (423 lines of code-behind).

Splits (a grouped type with outside consumers):

| host file | type | outside consumers | lands in |
| --- | --- | --- | --- |
| `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs:10` | `EnvelopeColumn` readonly record struct | `EnvelopePath.cs`, `TrendPenState.cs`, `TrendChartViewModel.cs` | `Chart/EnvelopeColumn.cs` |
| `SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs:6` | `FetchRange` readonly record struct | `HistoryRequest.cs` (embeds it as `Range`), `TrendChartViewModel.cs` | `Chart/FetchRange.cs` |
| `SemiPlot/SemiPlot.UI/Chart/EnvelopePath.cs:6` | `EnvelopePoint` readonly record struct | `EnvelopeLine.cs`, which strokes the path | stays in `EnvelopePath.cs`: the producing file holds it and one outside reader is not a split trigger, the rule that keeps `DataRectPixels` with `ChartCursorOverlay` |

Stays as it is, with the reason:

- `Chart/LeftButtonTool.cs`: `ChartPressRouter` and `TrendChartViewModel.cs:126-127` both read it.
- `Chart/HistoryRequest.cs`: 23 lines, read by `ChartHistoryRequestDebouncer` and
  `TrendChartViewModel.cs:415,421,431`.
- `Chart/ChartCursorReader.cs`: one of four collaborators whose only production consumer is
  `TrendChartViewModel.cs` (`ChartDeltaCursorReader` 52 lines of code, `ChartAxisBinder` 57,
  `ChartRealtimeApplier` 36, `ChartCursorReader` 16). Folding the smallest alone is grouping by size,
  which File Layout forbids by name; the host is 563 lines, past the 300 preference; and
  `charting.md:172` lists the two readers as one family.
- `Chart/ChartAxisEdit.cs`: its one consumer is `TrendChartView.axaml.cs:254`, 423 lines and not a
  host. `ChartAxisRegion` neither reads it nor is read by it, and `ChartAxisEditTests` pins it on its
  own.
- `Core/Trends/MinimapGeometry.cs`: public static class, 24 lines of code, one consumer,
  `UI/Minimap/MinimapViewModel.cs` in another project. A fold across a project boundary is not a fold.
- The remaining files at or under 25 lines of code (`ArchiveConnectionState`, `IDataProvider`,
  `AggregationLayer`, `PenLineStyle`, `PenRealtimeValues`, `ArchiveTimeConverter`, `BenchRoles`,
  `SeedFiller`, `SeederException`, `SyntheticPen`, `HistoryColumnTarget`, `LocalTimeAxis`,
  `ArchiveFailureView`, `StartupData`, `StartupReadTimedOutError`) each have two or more production
  consumers.
- `Chart/NavigationWindow.cs`, `Core/Trends/*` DTOs (`Sample`, `Pen`, `ArchiveExtent`, `RealtimeBatch`,
  `PenScale*`, `ScaleMode`, `DeltaReadout`), `Core/Data/Errors/*`, seeder options and
  `ArchiveRow`: two or more consumers each, several across projects.
- `Legend/LegendConverters.cs`: read from `TrendLegendView.axaml` alone; a converter beside a view
  model is a different concept, not a helper of it.
- `UiServiceCollectionExtensions.cs`, `PostgresDataServiceCollectionExtensions.cs`: composition API
  read from `StartupProbe.cs:44` in another project.
- XAML code-behind pairs (`MainWindow.axaml.cs`, `TrendToolbarView.axaml.cs`, `TrendLegendView.axaml.cs`):
  the file name is bound to the `.axaml`.
- `docs/plans/backlog.md:122-128`, "Group small related types the Go way": lists `HistoryRequest.cs`
  at 10 lines (it is 23, with two consumers), and two of the three fold targets named there are refuted (`LeftButtonTool` has a second consumer, `HistoryRequest`
  is read by the view model). The entry is replaced by the tables above and removed when the pass ships.

## Development Approach

- **testing approach**: Regular. No behaviour changes; the existing tests are the net.
- one commit per table row is not required; one commit per host file is: a reviewer reads "these
  types now live in `ChartCursorOverlay.cs`" as one hunk pair
- every move keeps the namespace, the visibility and the member order; only the file changes
- `dotnet build`, `dotnet format --verify-no-changes` and `dotnet terse` over the touched files exit 0
  before each commit; the hook enforces the last two

## Testing Strategy

- **unit tests**: `SemiPlot.Tests.Unit` stays at its count on `master`; no test moves unless its
  subject's file name changed and the test class is named for the file
- **integration tests**: unaffected

## Acceptance Evidence

```sh
dotnet build SemiPlot.slnx                                   # 0 warnings, 0 errors
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # the count master reports, 0 failed
git diff --name-status --diff-filter=AD master...HEAD -- '*.cs'   # six D lines, two A lines
for t in PostgresConnectionDto TrendLegendGroupViewModel ChartPressAction DataRectPixels OverlayPlacement ConnectionFileProblem; do git ls-files "*/$t.cs"; done   # prints nothing
git ls-files SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs SemiPlot/SemiPlot.UI/Chart/FetchRange.cs   # prints both
```

## Progress Tracking

- mark completed items with `[x]` immediately when done
- add newly discovered tasks with ➕ prefix
- document issues/blockers with ⚠️ prefix

## Solution Overview

Each move is a cut and paste of a type declaration with its `using` needs merged into the host file,
followed by `git rm` of the source. A split is the reverse: the type gets a file named for it, the
host loses the declaration. `InternalsVisibleTo` already covers the test projects, so an `internal`
type keeps its visibility after the move. Member order inside a host follows the tree: the small type comes
first, before the primary type, as `EnvelopeLine.cs:10`, `EnvelopePath.cs:6` and `HistoryPrefetch.cs:6`
already do. `docs/architecture/charting.md` needs no edit: its module layout names types, not files, and
no type changes its name.

## Implementation Steps

### Task 1: Fold the single-consumer types into their hosts

**Files:**
- Modify: `SemiPlot/SemiPlot.DataSource.Postgres/Configuration/PostgresConnectionLoader.cs`, `SemiPlot/SemiPlot.UI/Legend/TrendLegendViewModel.cs`, `SemiPlot/SemiPlot.UI/Chart/ChartPressRouter.cs`, `SemiPlot/SemiPlot.UI/Chart/ChartCursorOverlay.cs`
- Delete: `PostgresConnectionDto.cs`, `TrendLegendGroupViewModel.cs`, `ChartPressAction.cs`, `DataRectPixels.cs`, `OverlayPlacement.cs`

- [x] move each type of the folds table into its host, before the host's primary type as `EnvelopeLine.cs:10` does, merging `using` directives; `git rm` the source file
- [x] `TrendLegendView.axaml` `x:DataType` still resolves (`dotnet build SemiPlot/SemiPlot.UI/SemiPlot.UI.csproj` proves it: XamlIl fails a broken `x:DataType`)
- [x] `dotnet build SemiPlot.slnx`: 0 warnings, 0 errors; `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - the count `master` reports, 0 failed
- [x] commit through the hook, one commit per host file or one for the task

### Task 2: Give the shared column and range types their own files

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Chart/EnvelopeColumn.cs`, `SemiPlot/SemiPlot.UI/Chart/FetchRange.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/EnvelopeLine.cs`, `SemiPlot/SemiPlot.UI/Chart/HistoryPrefetch.cs`

- [x] move `EnvelopeColumn` from `EnvelopeLine.cs:10` to `EnvelopeColumn.cs`, its doc comment with it
- [x] move `FetchRange` from `HistoryPrefetch.cs:6` to `FetchRange.cs`, its doc comment with it
- [x] `dotnet build SemiPlot.slnx`: 0 warnings, 0 errors; `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj` - the count `master` reports, 0 failed
- [x] commit through the hook

### Task 3: Verify acceptance criteria

- [x] run every command of the Acceptance Evidence section and record the outputs here
  - build: 0 warnings, 0 errors; format --verify-no-changes: exit 0; unit tests: 777 passed, 0 failed
  - `--name-status`: six D (`PostgresConnectionDto`, `TrendLegendGroupViewModel`, `ChartPressAction`,
    `DataRectPixels`, `OverlayPlacement`, `ConnectionFileProblem`), two A (`EnvelopeColumn`, `FetchRange`); the `ls-files` loop
    printed nothing; both new files tracked
- [x] `git log --oneline master..HEAD` lists only commits of this plan: the plan commit, four folds, two splits, one family fold
- [x] `dotnet test SemiPlot.slnx` with the Docker bench: unit 777 passed, integration 85 passed,
  0 failed, 0 skipped, as on `master`

### Task 4: Update documentation

**Files:**
- Modify: `docs/plans/backlog.md`

- [x] `docs/plans/backlog.md:122-128`: remove the "Group small related types the Go way" entry
- [ ] move this plan to `docs/plans/completed/` (left in place; the delivery step archives it)

### Task 5: Fold the connection-file problem enum into its error

The second File Layout clause, not the first: a closed type family stays in one file named for the
family. `ConnectionFileProblem` is the vocabulary of one field of `ConnectionFileError`; its three
readers all hold the error. `ArchiveError` and `ArchiveFault` are the sibling pair and stay as they are.

**Files:**
- Modify: `SemiPlot/SemiPlot.Core/Data/Errors/ConnectionFileError.cs`
- Delete: `ConnectionFileProblem.cs`

- [x] move the enum, with its doc comment, before the error type; `git rm` the source file
- [x] `dotnet build SemiPlot.slnx`: 0 warnings, 0 errors; `dotnet format SemiPlot.slnx --verify-no-changes` exits 0
- [x] unit 777 passed, integration 85 passed, 0 failed
- [x] commit through the hook

## Post-Completion

None. The pass is self-contained; `CLAUDE.md` already states the rule.
