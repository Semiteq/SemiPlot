# Non-modal settings, export and pen editor windows

## Overview

- The settings window, the CSV export window and the pen editor (`Edit` -> `Pens and groups`) open with
  `ShowDialog(owner)` today (`DialogOpener.cs:24`, `:27-42`; `MainWindow.axaml.cs:36-50`). A modal child disables
  its owner on Win32, so the operator cannot pan, read or use the chart while one is open. Data does not stop:
  only the owner's input does.
- The three windows open with `Show(owner)`. Each stays owned: above the main window and minimised with it. The
  operator works in the chart while they are open.
- One window of each kind per main window. A second menu request brings the open window to the front and builds
  no second view model. A second `Export to CSV...` keeps the open window's range and reads nothing (operator
  decision, 2026-11-09).
- Each of the three windows holds every close, its own or its owner's, until its own work ends, then closes
  again, or closes the owner that asked: the pen editor its drain, the settings window a running save, the export
  window a cancelled export and the deletion of its partial file. The check runs on every close, so work started
  after an earlier hold is held too. So the main window's close, from the title bar, `File` -> `Exit` or `Restart
  now`, writes a pending edit once, cuts no save, leaves no `.partial` file and disposes nothing under work in
  flight.
- One close per main window: a second `Restart now` or `Exit` while a close is held starts no second copy.
- Escape closes each of the three windows; the pen editor still drains first (operator decision, 2026-11-09).
- The operator's decision that the pen editor writes its pending text edit when the operator leaves it
  (2026-11-09) already holds: losing activation clears the window's focus, so the field's own `LostFocus` writes
  the edit (Context). This plan changes no code for it and pins it with two tests.
- The About dialog stays modal: it names the product, the version and the configuration directory
  (`charting.md:684-685`) and has one Close button (`AboutDialog.axaml.cs:13-16`), so a non-modal About would add
  an instance guard for nothing the operator can use beside it.
- The startup failure window stays as it is, and the settings it opens stay modal: that window has nothing else
  to interact with, and keeping `DialogOpener.ShowSettings` for it is the change with the least code.
- Scope guard: no change to the editor's commit rules, to the export's file format or to the save's staging rules.
  The settings save and the export expose a task the window waits on; the editor's queue exposes whether it is
  idle; Escape is new in the three windows. No change to Core or the data source.

## Context (from discovery)

Branch `non-modal-windows`, HEAD `638db7f` (verified 2026-11-09: `git rev-parse --short HEAD`). Its merge base
with `master` is `df129f7`. No issue is named for this work, so the PR body closes none.

Ordering with `docs/plans/20261030-editor-follows-reads.md`: this plan goes first. That plan deletes
`IPenCatalogueEditor.ReadAsync` and opens the editor on the sync's snapshot (`20261030-editor-follows-reads.md:6-8`,
`:24-26`), so when it runs it updates the two read-based tests of Task 3
(`ASecondPenEditorRequest_ActivatesTheOpenWindow_AndReadsTheCatalogueOnce`,
`ThePenEditorCommand_CannotExecuteWhileItsReadRuns`).

Decompiled citations come from `ilspycmd` 10.0.0.8330, run 2026-11-09, and are written `<file> (decompiled):N`:

| Written as | Type | Assembly |
| --- | --- | --- |
| `Window.cs` | `Avalonia.Controls.Window` | `~/.nuget/packages/avalonia/12.0.5/lib/net10.0/Avalonia.Controls.dll` |
| `WindowBase.cs` | `Avalonia.Controls.WindowBase` | same |
| `TopLevel.cs` | `Avalonia.Controls.TopLevel` | same |
| `InputElement.cs` | `Avalonia.Input.InputElement` | `avalonia/12.0.5`, `Avalonia.Base.dll` |
| `Lifetime.cs` | `Avalonia.Controls.ApplicationLifetimes.ClassicDesktopStyleApplicationLifetime` | `Avalonia.Controls.dll` |
| `Headless.cs` | `Avalonia.Headless.HeadlessWindowImpl` | `avalonia.headless/12.0.5`, `Avalonia.Headless.dll` |
| `W32.cs` | `Avalonia.Win32.WindowImpl` | `avalonia.win32/12.0.5`, `Avalonia.Win32.dll` |
| `RC2.cs` | `ReactiveUI.ReactiveCommand<TParam, TResult>` | ReactiveUI 23.2.28, `ReactiveUI.dll` |

Rerun `ilspycmd -t <type> <dll>` to reproduce the line numbers.

### How each window opens today

- `DialogOpener` (`SemiPlot/SemiPlot.UI/MainWindow/DialogOpener.cs`): `ShowAbout` awaits `ShowDialog(owner)` in an
  `async void` with a catch to `reportFailure` (`:9-20`); `ShowSettings` calls `ShowOwnedDialog` with a
  `SettingsDialog` (`:22-25`); `ShowOwnedDialog` awaits `createDialog().ShowDialog(owner)`, reports a throw, and
  disposes the view model in `finally` (`:27-42`).
- `MainWindow.OnLoaded` subscribes five request streams into `_requests` (`MainWindow.axaml.cs:24-52`):
  `ExitRequests` to `Close()` (`:33`), `AboutRequests` (`:34-35`), `SettingsRequests` (`:36-37`),
  `PenEditorRequests` with `Width = PenEditorWindow.OpeningWidthFor(WorkingAreaWidth())` (`:38-47`), and
  `ExportRequests` (`:48-50`). `OnUnloaded` clears them (`:54-59`).
- `MainWindowViewModel` builds each view model before the request reaches the view:
  - `ShowSettingsCommand` is `CreateFromTask(RequestSettingsAsync)` (`MainWindowViewModel.cs:108`), which awaits
    `SettingsViewModel.OpenAsync` (`:232-236`). `OpenAsync` reads both section folders on the pool
    (`SettingsViewModel.cs:252-267`).
  - `ShowPenEditorCommand` is `CreateFromTask(RequestPenEditorAsync)` (`:109`): one `ReadAsync` round trip, then a
    new `PenEditorViewModel` (`:238-255`).
  - `ExportCommand` is `ReactiveCommand.Create(RequestExport, ...)` (`:110-112`); `RequestExport` snapshots the pens
    and the visible window (`:223-230`). The `CsvExportViewModel` constructor starts the archive-days read and the
    throttled count (`CsvExportViewModel.cs:80-81`, `:249-263`, `:398-409`).
  - The request summaries say the listener owns each view model and disposes it when its window closes
    (`MainWindowViewModel.cs:125-132`).
- `StartupFailureWindow.OnLoaded` subscribes `SettingsRequests` to `DialogOpener.ShowSettings` (`:32-33`);
  `StartupFailureViewModel` builds per request (`StartupFailureViewModel.cs:125-131`). The failure window is the
  application's main window, not an owned one (`App.axaml.cs:74-90`).

### The work each window runs

- Pen editor: `OnClosing` lets the close through once `_drain` has completed, otherwise cancels and starts
  `DrainAsync` once (`PenEditorWindow.axaml.cs:111-123`). `DrainAsync` takes the recorded text edit, then the scale
  pair, awaits `WhenIdleAsync`, reports a throw in its own catch (`:144-147`), and posts `Close` in `finally`
  (`:148-151`). The text edit is recorded on `GotFocus` (`:170-180`) and taken on `LostFocus` (`:182-195`); the
  scale pair ends when focus leaves both boxes (`:221-239`). A server refusal reports to the panel and returns
  (`PenFormViewModel.cs:157-175`).
- The editor's queue is a task chain whose `_tail` is the last call queued (`ArchiveCallQueue.cs:16`, `:22-29`);
  `WhenIdleAsync` waits until the tail it read is still the last (`:38-48`). `PenEditorViewModel` owns it
  (`PenEditorViewModel.cs:30`, `:48`) and exposes only `WhenIdleAsync` (`:132-135`).
- Settings: `SaveCommand` is `CreateFromTask(SaveAsync, canSave)` (`SettingsViewModel.cs:91`); `SaveAsync` awaits
  `Task.Run(() => SettingsSave.Save(...))` (`:305`) and sets `IsRestartPending` after a non-theme edit (`:317-320`).
  `Save` returns a `Result` and stages and promotes the files (`SettingsSave.cs:65-93`). `overview.md:424-426`
  already names the risk: "closing the window would end the process inside the save".
- Export: `ExportCommand` is `CreateFromTask<string>(ExportAsync, ...)` (`CsvExportViewModel.cs:101-102`).
  `ExportAsync` awaits `Task.Run(() => _fileExporter.ExportAsync(...), token)` and catches every exception
  (`:451-485`). `Dispose` cancels the export and does not wait (`:231-235`). A cancel deletes the partial file
  (`overview.md:1097-1098`), on the pool, after the window closed (`overview.md:1104-1107`).

### How Avalonia 12.0.5 closes an owner and its owned windows

- `Close()` and the platform close both run `ShouldCancelClose` (`Window.cs (decompiled):677-707`).
- `ClosingBehavior` defaults to `OwnerAndChildWindows` (`:550`). Under it, `ShouldCancelClose` asks every owned
  window first, with `WindowCloseReason.OwnerWindowClosing` when the owner's reason is `WindowClosing`; if any child
  cancels, the owner's `OnClosing` is not called and the close is cancelled (`:723-748`).
- With no child cancelling, `CloseInternal` closes every owned window before the owner's platform window, and
  clears `Owner` (`:711-721`).
- `WindowClosingEventArgs.CloseReason` is public (`WindowClosingEventArgs`, decompiled, `:13`).
- `Show(owner)` throws only before anything is shown (`Window.cs (decompiled):982-1014`); `IsDialog` is public and
  true only for `ShowDialog` (`:522`, `:1036`); `UpdateEnabled` disables the owner only while a dialog child exists
  (`:1192-1204`).
- `Closed` is raised synchronously by the platform: Win32 on `WM_DESTROY` (`W32.cs (decompiled):816-817`), headless
  from `Dispose` (`Headless.cs (decompiled):172-174`).
- `WindowBase.HandleActivated` raises `Activated` with no guard on `IsActive` (`WindowBase.cs (decompiled):360-368`).
- Headless: `SetEnabled` is empty (`Headless.cs (decompiled):571-573`), so a click on a headless owner works with a
  modal child open; `Activate` posts `Activated` (`:227-235`).
- The lifetime keeps the default `ShutdownMode` (`git grep -n ShutdownMode -- SemiPlot/SemiPlot.UI` prints nothing),
  `OnLastWindowClose`; its shutdown cancels the main loop and calls `Dispatcher.UIThread.InvokeShutdown()`
  (`Lifetime.cs (decompiled):52-54`, `:184-186`).

So today a main window close with the editor holding a write is cancelled by the editor, which drains and closes
itself while the main window stays open. Modal input makes that unreachable on Win32; `Show(owner)` makes it
reachable.

### Leaving the editor already ends its edit

- On Win32, `WM_KILLFOCUS` raises the platform `LostFocus` unless an IME composition runs (`W32.cs
  (decompiled):1300-1308`). `TopLevel` subscribes to it (`TopLevel.cs (decompiled):491`) and clears the focused
  element of that window (`:888-893`). So the field loses focus, `OnFieldLostFocus` writes the text edit, and the
  scale pair's focus-within turns false and writes the pair (`PenEditorWindow.axaml.cs:182-195`, `:221-239`).
- Headless, a test reaches the same path through `window.PlatformImpl!.LostFocus?.Invoke()`. `Hide()` also clears
  the focus (`InputElement.cs (decompiled):1517-1532`), so a test that hides the window cannot tell a deactivation
  handler from today's code.

### ReactiveUI 23.2.28 commands

- `CanExecute` is the given `canExecute` combined with `!IsExecuting` (`RC2.cs (decompiled):107-111`), so a menu
  click during a running `CreateFromTask` command is refused.
- `Dispose` disposes only the `CanExecute` subscription (`:197-205`); the execution subjects stay live, so disposing
  a command while its own body runs raises nothing.
- A throw out of a command's task reaches `ThrownExceptions` (`:161-165`).

### Restart, exit and new window

- `ExitCommand` emits on `_exitRequests` (`MainWindowViewModel.cs:104-105`), which `MainWindow` turns into `Close()`
  (`MainWindow.axaml.cs:33`). `RestartApplication` starts a copy and emits on `_exitRequests` when it started
  (`MainWindowViewModel.cs:199-214`); nothing stops a second call. `RestartNowCommand` runs it
  (`SettingsViewModel.cs:92-95`, `MainWindowViewModel.cs:235`).
- `NewWindowCommand` calls `StartCopy()` and keeps the window (`MainWindowViewModel.cs:103`); `InstanceLauncher.Start`
  runs `Process.Start` (`InstanceLauncher.cs:42-59`, `:94-97`): another process, no owner in this one.
- `App` disposes the `TrendWindow` from the main window's `Closed` (`App.axaml.cs:99-105`).

### Escape and keyboard

- No window of the three binds Escape: `git grep -n "Escape" -- "SemiPlot/SemiPlot.UI/*.axaml"` prints only
  `AxisScalePanel.axaml:17` and `TimeWindowPanel.axaml:18`, and no `IsCancel` occurs in `SemiPlot.UI`.
- Escape inside an open drop-down is pinned: `CsvExportWindowTests.cs:1106-1112` opens the start marker box and
  presses Escape on the window before picking. Escape on the open colour flyout is pinned at
  `PenEditorViewTests.cs:921-923`.
- The main window has no keyboard shortcut: no `KeyBinding`, `HotKey` or `InputGesture` in `MainWindow.axaml` or
  `AppMenuBar.axaml` (`:13-96`), and no access key in the headers (`Resources.resx:93-94`).

### Tests that exist

- `MainWindowViewTests` (traits `Component=UI`, `Area=Chart`, `Category=Unit`, `:40-42`):
  `EditPensAndGroups_...` (`:45-87`, disposal probe `:82-85`), `ExportToCsv_..._AndClosingItCancelsTheExport`
  (`:89-130`, the close and its immediate assertions `:121-125`), `EditSettings_...` (`:132-164`, probe
  `:159-162`), `TheRestartNowButton_...` (`:166-188`), and the private `MenuItemNamed` (`:424-430`).
- `MainWindowViewModelTests` request tests run one `Execute()` per kind and test (`:65`, `:88`, `:213`, `:240`; and
  `OpenPenEditorAsync` once at `:101`, `:122`, `:144`).
- `AppMenuBarTests.ExitItem_ClosesTheWindow` (`:160-176`).
- `StartupFailureWindowTests.TheSettingsButton_OpensTheDialogOverTheWindowAndTheWindowDisposesItsViewModel` (`:189`)
  and `TheSettingsDialog_OverAnEmptyPassword_WritesItIntoTheExistingConnectionFileAndRestarts` (`:230`).
- `PenEditorViewTests.ANameTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone` (`:391-416`).
- Helpers: `NewWindowStand` over a `TestScheduler` as the UI scheduler (`MainWindowTestBuilder.cs:105`, `:144-158`),
  the static class `MainWindowTestBuilder` (`:70`); `FakePenCatalogueEditor.HoldNextCall` gates the next call, the
  read included, and its gate resumes the held call inline (`FakePenCatalogueEditor.cs:65-75`, `:90-93`,
  `:130-140`), `Changes` (`:47`), `ChangeResult` (`:53`); `FakeArchiveRowReader.OnSeeds` and `OnCount`
  (`FakeArchiveRowReader.cs:20-26`); `PenEditorWindowDriver.Realise`, `Close`, `Retype`, `RowAt`
  (`PenEditorWindowDriver.cs:17-33`, `:50`); `PenEditorFixture.ViewCatalogue` (`:26`); `HeadlessWait.Until` (`:15`);
  `HeadlessInput` (`:14-79`); `TrendNavigationModel.SetWindow` (`TrendNavigationModel.cs:76`).

### Docs that name the modality or the requests

- `docs/architecture/overview.md:61-64` (the diagram calls the three windows dialogs), `:257` ("the settings
  dialog"), `:180-182` and `:422-426` (Restart now; the save risk), `:676-678`, `:758-761`, `:1046`, `:1102-1107`.
- `docs/architecture/charting.md:677` (the code-behind's five requests, "settings dialog"), `:684-693` (each "the
  modal").
- `docs/architecture/ui-theme.md:528-529` (`DialogOpener.ShowOwnedDialog`).
- `readme.md`: `grep -in "modal\|dialog" readme.md` prints nothing.

## Development Approach

- **Testing approach**: TDD. Each task writes its first test, runs it red at `638db7f`, then changes the code. A
  test marked "green at HEAD" is a regression pin; the plan says why it cannot be red. Task 9 is pins only: it
  changes no production code.
- Complete each task fully before the next; every task ends with its tests passing.
- **CRITICAL: every task MUST include new/updated tests** for the code it changes.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**

## Testing Strategy

- **View tests**: a new class `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`, traits
  `Component=UI`, `Area=Chart`, `Category=Unit`. It realises `MainWindow` over `NewWindowStand`, whose UI scheduler is
  a `TestScheduler`, never an immediate one (`AGENTS.md`, Test). Editor-only tests stay in `PenEditorViewTests`.
- Every click, cleared field, typed text and key press goes through `HeadlessInput`. `MenuItemNamed` moves from
  `MainWindowViewTests.cs:424-430` to `MainWindowTestBuilder` as a shared lookup, so the new class carries no helper
  of its own; a menu item is clicked on the `TopLevel` of its popup, as `MainWindowViewTests.cs:60-64` does.
- Commands run as `.Execute().Subscribe()` where the test must not await, as `AppMenuBarTests.cs:172` does.
- Headless cannot show modality through input (Context), so the red signal for "not modal" is `Window.IsDialog`;
  the menu click on the main window is the interaction check beside it.
- A held close is read through the window's `Closing` event: `OnClosing` calls `base.OnClosing`, which raises
  `Closing`, only on the pass the helper lets through, so a value snapshotted in a `Closing` handler is the state at
  the moment the window really closes.
- A disposed view model is read through a disposed command, the pattern of `MainWindowViewTests.cs:82-85`: the
  input is reset so the command is disabled before the close, and set after it; a command that stays disabled was
  disposed. The export's probe is its count token, cancelled by `Dispose` (`CsvExportViewModel.cs:231-235`).
  "Disposed exactly once" splits into two observable halves: not disposed while its window is open, disposed after
  it closed. A second dispose cannot be observed without a test-only member, which `base.md` forbids; the opener
  makes it unreachable (Solution Overview).
- **E2E**: none; the project has no separate e2e suite. Win32 behaviour is a Post-Completion check.

## Acceptance Evidence

The defect is automatable. At `638db7f`, `OwnedWindowsTests.ThePenEditor_IsOwnedAndNotModal_AndTheMainMenuStillRuns`
fails at `IsDialog` (true), and `ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow` fails
because the main window is still visible after the held write lands.

After the change:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~OwnedWindowsTests|FullyQualifiedName~OwnedWindowClosingTests|FullyQualifiedName~MainWindowViewTests|FullyQualifiedName~MainWindowViewModelTests|FullyQualifiedName~AppMenuBarTests|FullyQualifiedName~PenEditorViewTests|FullyQualifiedName~CsvExportWindowTests|FullyQualifiedName~StartupFailureWindowTests"
```

It must report 0 failed. These named tests must be present and pass:

- `OwnedWindowsTests.TheSettingsWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns` - Task 1
- `OwnedWindowsTests.ASecondSettingsRequest_ActivatesTheOpenWindow_AndBuildsNoSecondViewModel` - Task 1
- `OwnedWindowsTests.AClosedSettingsWindow_DisposesItsViewModel_AndTheNextRequestBuildsANewOne` (green at HEAD) -
  Task 1
- `MainWindowViewTests.EditSettings_ClickedOnTheRealisedWindow_OpensTheDialogOverIt` (unchanged) - Task 1
- `MainWindowViewTests.TheRestartNowButton_OfTheSettingsDialog_StartsACopyAndClosesTheWindow` (panel assertion
  added) - Task 1
- `StartupFailureWindowTests.TheSettingsButton_OpensTheDialogOverTheWindowAndTheWindowDisposesItsViewModel`
  (unchanged) - Task 1
- `StartupFailureWindowTests.TheSettingsDialog_OverAnEmptyPassword_WritesItIntoTheExistingConnectionFileAndRestarts`
  (unchanged) - Task 1
- `OwnedWindowsTests.TheExportWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns` - Task 2
- `OwnedWindowsTests.ASecondExportRequest_ActivatesTheOpenWindow_AndKeepsItsRange` - Task 2
- `OwnedWindowsTests.ThePenEditor_IsOwnedAndNotModal_AndTheMainMenuStillRuns` - Task 3
- `OwnedWindowsTests.ASecondPenEditorRequest_ActivatesTheOpenWindow_AndReadsTheCatalogueOnce` - Task 3
- `OwnedWindowsTests.ThePenEditorCommand_CannotExecuteWhileItsReadRuns` (green at HEAD) - Task 3
- `MainWindowViewTests.EditPensAndGroups_ClickedOnTheRealisedWindow_OpensTheEditorOverIt` (unchanged) - Task 3
- `OwnedWindowClosingTests.ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow` (theory: `Close`,
  `Exit`, `Restart`) - Task 4, the `Restart` row extended in Task 7
- `OwnedWindowClosingTests.ClosingTheMainWindow_WhileTheEditorDrainsItsOwnClose_StillClosesTheMainWindow` - Task 4
- `OwnedWindowClosingTests.AWriteRefusedDuringTheDrain_StillClosesTheMainWindow` - Task 4
- `PenEditorViewTests.AnEditTypedDuringAHeldDrain_IsWrittenBeforeTheWindowCloses` - Task 4
- `PenEditorViewTests.ANameTypedThenTheWindowClosed_WritesOnceBeforeTheWindowIsGone` (unchanged) - Task 4
- `OwnedWindowClosingTests.ClosingTheMainWindow_DuringASave_ClosesItOnlyAfterTheSaveEnds` - Task 5
- `OwnedWindowClosingTests.ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile` - Task 6
- `MainWindowViewTests.ExportToCsv_ClickedOnTheRealisedWindow_OpensTheExportOverIt_AndClosingItCancelsTheExport`
  (waits with `HeadlessWait.Until`) - Task 6
- `OwnedWindowClosingTests.WorkStartedBetweenTwoResumes_IsHeldByTheNextClose` - Task 6
- `AppMenuBarTests.ExitItem_ClosesTheWindow` (unchanged) - Task 7
- `OwnedWindowsTests.Escape_ClosesTheSettingsWindow` - Task 8
- `OwnedWindowsTests.Escape_ClosesTheExportWindow` - Task 8
- `PenEditorViewTests.Escape_WritesTheTypedNameOnceThenClosesTheEditor` - Task 8
- `CsvExportWindowTests` marker box test at `:1089-1124` (window-visible assertion added; green at HEAD) - Task 8
- `PenEditorViewTests` colour flyout test at `:921-923` (window-visible assertion added; green at HEAD) - Task 8
- `PenEditorViewTests.ANameTypedThenTheWindowLosesFocus_WritesItOnce` (green at HEAD, regression pin: Win32's
  `WM_KILLFOCUS` already clears the focus and the field's `LostFocus` writes, Context) - Task 9
- `PenEditorViewTests.AScalePairTypedThenTheWindowLosesFocus_WritesItOnce` (green at HEAD, regression pin: the
  pair's focus-within turns false on the same path, Context) - Task 9
- ➕ Review fixes, each red against the guard it pins:
  `OwnedWindowsTests.ASecondSettingsRequest_RestoresTheMinimisedWindow`,
  `OwnedWindowsTests.ShiftEscape_LeavesTheSettingsWindowOpen`,
  `OwnedWindowsTests.AWindowThatFailsToBuild_IsReported_ForgottenAndDisposedOnce`,
  `OwnedWindowClosingTests.ClosingTheMainWindow_WhileAWriteRunsWithNoEditOpen_WaitsForTheWrite`,
  `OwnedWindowClosingTests.ClosingTheSettingsWindow_DuringASave_ClosesItOnlyAfterTheSaveEnds`; the `Exit` row of
  the close theory runs `RestartApplication` during the held write, and
  `MainWindowViewTests.ExportToCsv_ClickedOnTheRealisedWindow_OpensTheExportOverIt_AndClosingItCancelsTheExport`
  pins the export window's own close as a hold

`$BASE` is `638db7f`. If `git merge-base --is-ancestor 638db7f HEAD` exits 1 after a rebase, `$BASE` is
`$(git merge-base HEAD origin/master)`.

Gates, each run fresh:

```powershell
dotnet build SemiPlot.slnx                                   # 0 errors, no warning beyond the NU1902 the base reports
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj   # 0 failed
dotnet format SemiPlot.slnx --verify-no-changes              # exit 0
dotnet tool restore; dotnet terse <every .cs file this branch touches>  # exit 0
git grep -nE "static void (Click|Clear|Type|Press)\(" -- SemiPlot/SemiPlot.Tests.Unit   # only HeadlessInput.cs lines
git grep -n "ShowDialog(" -- SemiPlot/SemiPlot.UI            # only DialogOpener.cs, in ShowAbout and ShowSettings
git grep -n "ShowOwnedDialog" -- SemiPlot docs/architecture  # prints nothing
git grep -n "WindowCloseReason" -- SemiPlot/SemiPlot.UI      # only Windows/OwnedWindowClosing.cs
git grep -n "Deactivated" -- SemiPlot/SemiPlot.UI/PenEditor  # prints nothing: no deactivation handler
git grep -nw "ShowDialog\|modal" -- docs/architecture | grep -v "non-modal"   # only About and the failure window's settings
git diff $BASE...HEAD --stat -- SemiPlot/SemiPlot.Core SemiPlot/SemiPlot.DataSource.Postgres ConfigFiles   # prints nothing
```

Owned-window placement, minimising and the native file picker cannot be seen headlessly; Post-Completion covers them.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **`DialogOpener.ShowOrActivate` shows a window non-modally, once.** When an owned window already shows the view
  model, it calls `Activate()`. Otherwise it builds the window, hooks `Closed` to a release that runs once, and
  calls `Show(owner)`.
- **`MainWindowViewModel` keeps the open view model of each kind.** Three fields, `_openSettings`, `_openPenEditor`,
  `_openExport`. A request while a field is set emits the same instance again and reads, builds and snapshots
  nothing. `ForgetClosedWindow(object viewModel)` clears the matching field.
- **One shared closing shape, `MainWindow/OwnedWindowClosing(Window window, Func<Task?> pendingWork)`.** Each of
  the three windows builds one in its constructor over its own `PendingWork` method, and calls `Lets(e)` from
  `OnClosing` and `CloseOnEscape(e)` from `OnKeyDown`. `Lets` asks `pendingWork()` on every close that finds no hold
  running; a task cancels the close, and when it ends the helper posts a close of the owner that asked, or of the
  window itself.
- **Each window names its own work, and returns null when idle.** The editor: its drain, when a text edit or a
  scale pair is recorded or the queue is busy (`PenEditorViewModel.IsIdle`, over a new `ArchiveCallQueue.IsIdle`).
  Settings: `SettingsViewModel.SaveInFlight`. Export: `CsvExportViewModel.StopExport()`, which cancels the run and
  returns its task.
- **One close per main window.** `MainWindowViewModel` sets `_isClosing` in `Exit` and in a `RestartApplication`
  that started its copy; a later `RestartApplication` returns before `StartCopy()`.
- **Nothing else changes.** About and the failure window's settings keep `ShowDialog`. Leaving the editor keeps
  ending its edit through today's focus path.

Design decisions, each with its reason:

- **`Show(owner)`, not `Show()`.** An owned window stays above its owner and minimises with it (Win32 owner
  semantics; `Owner` sets the platform parent, `Window.cs (decompiled):1481-1488`). It joins `OwnedWindows`, which
  the instance check and Avalonia's close cascade read (`:164`, `:711-748`). ASSUMPTION: minimise-with-owner holds
  on Win32 for a non-dialog owned window; headless `SetParent` is empty, so only Post-Completion check 2 sees it.
- **The guard lives in `MainWindowViewModel`, the activation in `DialogOpener`.** Every view model is built in
  `MainWindowViewModel` before the request reaches the view (`MainWindowViewModel.cs:223-255`). A view-side guard
  sees the view model only after it is built, so it can only dispose a surplus after a second section read, a
  second catalogue round trip (`:240`) or a second export view model that started its archive-days read
  (`CsvExportViewModel.cs:81`). The field check is one `if` per request method. The view keeps what only it can do:
  find the window and call `Activate()`.
- **The same instance is re-emitted.** `ShowOrActivate` matches the window by `ReferenceEquals(window.DataContext,
  viewModel)`, so one request stream per kind carries both the open and the activate, and the request types do not
  change.
- **Two clicks during one read build one view model.** A running `CreateFromTask` command cannot execute
  (`RC2.cs (decompiled):107-111`), so the second click is refused and the field check sees the first result.
  `ThePenEditorCommand_CannotExecuteWhileItsReadRuns` pins it.
- **The view still disposes, in `Closed`, once.** The release forgets the reference, then disposes, inside a
  try/catch that routes a throw to `reportFailure`, and a local flag makes it run once whichever path reaches it.
  `MainWindowViewModel` still disposes only its own commands and subjects (`AGENTS.md`, Dependency Injection;
  `overview.md:151-152`). The dispose is synchronous: `Closed` comes from `WM_DESTROY` on Win32 and from `Dispose`
  headless (Context), and `CloseInternal` closes the owned windows before the main window (`Window.cs
  (decompiled):711-721`), so each view model is disposed before `App` disposes the composition from the main
  window's own `Closed` (`App.axaml.cs:103`). A posted dispose could miss the end of the main loop (Context).
  Disposing `RestartNowCommand` from `Closed` while its own body is on the stack (`Restart now` ->
  `RestartApplication` -> `_exitRequests` -> `Close()` -> the cascade -> `Closed`) is safe, because a ReactiveUI
  command's `Dispose` leaves its execution subjects live (`RC2.cs (decompiled):197-205`).
- **A failed `Show` closes the built window.** `Show(owner)` throws only before the window is shown
  (`Window.cs (decompiled):990-1014`), so the window exists and holds a platform window that no close has released.
  The catch reports the throw, calls `window?.Close()` to release it, and runs the release; the once-flag keeps a
  `Closed` raised by that close from disposing twice.
- **A second Export request keeps the open window's range.** The export opens over the pens and window visible at
  the request (`MainWindowViewModel.cs:223-230`). A second request brings it to the front with its fields as the
  operator left them and reads nothing. This is the operator's decision of 2026-11-09.
- **One closing helper, three callers.** The three windows run the same rule: hold a close until my own work ends,
  then close again, or close the owner that asked. Avalonia already asks the owned windows first and closes them
  first (`Window.cs (decompiled):711-748`); its only gap is that a child that cancels ends the owner's close for
  good. One class closes that gap for all three. Rejected: `MainWindow` sets `ClosingBehavior = OwnerWindowOnly` and
  rebuilds the cascade, about 25 lines plus a pending-close flag in the window, and it would still need each child
  to report its own work.
- **The helper asks again on every close.** A hold that has ended does not let later closes through by itself:
  the next close calls `pendingWork()` again, and work started after the first hold, a new edit or a new save, is
  held in turn. So each `pendingWork` returns null when its window is idle, or the posted re-close would hold
  forever.
- **The editor's idle check is one property on the queue.** `ArchiveCallQueue.IsIdle => _tail.IsCompleted`, read
  through `PenEditorViewModel.IsIdle`, is the smaller choice: making `DrainAsync` return null needs the same queue
  state, plus a second exit path in an `async` method. The editor's `PendingWork` returns null when `_textEdit` and
  `_scaleForm` are null and the queue is idle, else `DrainAsync()`. A recorded edit with no change drains in one hop
  and writes nothing (`PenFormViewModel.cs:150-153`).
- **The drain does not loop.** An edit recorded during a held drain, by focus moving into another field, is
  recorded in `_textEdit` (`PenEditorWindow.axaml.cs:170-180`). The helper's posted re-close asks `PendingWork`
  again, finds it, and drains again before the window closes. Typing more into the field the drain already ended is
  not recorded again until focus re-enters it; that text goes with the closing window.
- **Every close is held, the window's own included.** The editor's own close already drains
  (`PenEditorWindow.axaml.cs:111-123`). The settings window's own close during a save now waits for the save to end,
  and the export window's own close during an export cancels it and waits for the exporter to delete the partial
  file. So no close of any kind disposes a view model under its work, and the `.partial` gap of
  `overview.md:1104-1107` closes for both routes.
- **Owner-initiated means any reason but `WindowClosing`.** The helper keeps the owner when
  `e.CloseReason != WindowCloseReason.WindowClosing`: `OwnerWindowClosing` is what the cascade passes
  (`Window.cs (decompiled):732`), and an application or OS shutdown is an owner close too. The owner is read in
  `OnClosing`, because `CloseInternal` clears it (`:720`), and recorded only when a hold starts or runs, so a close
  the helper let through leaves no stale owner for a later own close.
- **The helper reports nothing.** Each work reports its own failures: `DrainAsync` keeps its catch
  (`PenEditorWindow.axaml.cs:144-147`), `ExportAsync` catches every exception (`CsvExportViewModel.cs:472-479`), and a
  throw out of `SaveAsync` reaches `SaveCommand.ThrownExceptions` (`RC2.cs (decompiled):161-165`). `CloseAfterAsync`
  awaits the work with `ConfigureAwaitOptions.SuppressThrowing`, the option `ArchiveCallQueue` already uses
  (`ArchiveCallQueue.cs:11-12`), and posts the close.
- **The export waits for its partial file.** `StopExport()` cancels the run and returns the `ExportAsync` task,
  which ends only after the exporter's task has deleted the partial file, because `ExportAsync` awaits it
  (`CsvExportViewModel.cs:466-468`). ASSUMPTION: a cancel that lands before `Task.Run` starts the exporter cancels the
  proxy without running it, so no partial file exists. Guard: `ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile`.
- **The settings save is held by its continuation.** `SaveInFlight` is the `SaveAsync` task while it runs.
  ASSUMPTION: under `[AvaloniaFact]` the continuation after `await Task.Run` resumes on the UI dispatcher, so the
  save stays in flight until the test pumps jobs. Guard: the held-save test asserts `IsRestartPending` is still false
  right after the main window's `Close()`.
- **Leaving the editor needs no code.** The operator's decision that a pending text edit is written when the
  operator leaves the editor already holds: Win32 clears the window's focus on `WM_KILLFOCUS`, and the field's
  `LostFocus` and the scale pair's focus-within write it once (Context). Task 9 pins both paths through the platform
  `LostFocus` callback. An IME composition in progress defers the platform `LostFocus` (`W32.cs
  (decompiled):1301-1304`); Post-Completion check 4 covers the plain case.
- **A long or failed drain, save or export holds the main window open.** None of the three has a timeout. Until the
  work ends, the main window and every owned window stay open and usable. A further close request adds nothing: the
  helper starts no second run while one is held. A further `Restart now` or `Exit` starts no second copy
  (`_isClosing`). A `Restart now` after a title-bar close still starts one copy, because the title-bar close reaches
  no view model while a child cancels it (`Window.cs (decompiled):723-748`). A write the server refuses reports to
  the panel and the log (`PenFormViewModel.cs:172`, `ResultReporting.cs:40-56`), and the owner then closes as asked:
  the panel entry closes with the window, the log keeps it.
- **The late close is accepted.** The main window closes when the last held work ends, not at the click. A request
  whose read lands after the main window unloaded emits to no subscriber, as today; its view model is not shown.
- **Every close route is one path.** The title bar runs `HandleClosing`, and `Close()` runs `CloseCore`; both call
  `ShouldCancelClose` (`Window.cs (decompiled):677-707`). `Exit` and `Restart now` reach `Close()` through
  `ExitRequests` (`MainWindow.axaml.cs:33`). `Restart now` starts the copy before the close, as today
  (`MainWindowViewModel.cs:199-205`); the copy's catalogue loop re-reads every 5 s (`AGENTS.md`, UI), so it shows
  a drained edit within one interval.
- **`File` -> `New window` is not affected.** The copy is another process (`InstanceLauncher.cs:94-97`).
- **The failure window's settings stay modal.** `StartupFailureWindow` keeps `DialogOpener.ShowSettings`. A
  non-modal window there would need the same guard in a second view model for a window whose only other controls
  are its own buttons. `ShowOwnedDialog` loses its `MainWindow` callers and folds into `ShowSettings`, its one
  remaining caller (`base.md`, Dead code). The settings window's hold works there too: its own close during a save
  waits for the save.
- **Escape closes on the unhandled key only.** `CloseOnEscape` acts on an Escape with no modifier that no control
  handled. Avalonia calls `OnKeyDown` through a class handler that skips handled events, and the helper checks
  `Handled` too, so an open drop-down or flyout that takes Escape keeps the window. ASSUMPTION: the combo box, the
  colour flyout and the calendar popups mark Escape handled. Guard: the two pins in Task 8; the calendar is
  Post-Completion check 6.
- **No keyboard routing is needed.** The main window has no shortcut (Context).

## Technical Details

### `MainWindow/OwnedWindowClosing.cs` (new)

```csharp
/// <summary>How an owned window closes: Escape closes it, and its own work holds a close until the work ends.</summary>
internal sealed class OwnedWindowClosing(Window window, Func<Task?> pendingWork)
{
	private Task? _held;
	private Window? _ownerToClose;

	/// <summary>False after cancelling the close; the window, or the owner that asked, closes when the work ends.</summary>
	public bool Lets(WindowClosingEventArgs e)
	{
		var owner = e.CloseReason != WindowCloseReason.WindowClosing ? window.Owner as Window : null;

		if (_held is { IsCompleted: false })
		{
			_ownerToClose ??= owner;
			e.Cancel = true;

			return false;
		}

		if (pendingWork() is not { } work)
		{
			return true;
		}

		_ownerToClose = owner;
		_held = CloseAfterAsync(work);
		e.Cancel = true;

		return false;
	}

	public void CloseOnEscape(KeyEventArgs e)
	{
		if (e is { Key: Key.Escape, KeyModifiers: KeyModifiers.None, Handled: false })
		{
			e.Handled = true;
			window.Close();
		}
	}

	private async Task CloseAfterAsync(Task work)
	{
		await work.ConfigureAwait(ConfigureAwaitOptions.ContinueOnCapturedContext | ConfigureAwaitOptions.SuppressThrowing);

		var owner = _ownerToClose;
		_ownerToClose = null;
		Dispatcher.UIThread.Post(owner is not null ? owner.Close : window.Close);
	}
}
```

`_ownerToClose` is assigned before `CloseAfterAsync` starts, so a work that has already completed still reads it.

### `MainWindow/DialogOpener.cs`

- `ShowOwnedDialog` (`:27-42`) folds into `ShowSettings` (`:22-25`), which keeps `ShowDialog` and its `finally`.
- New method:

  ```csharp
  public static void ShowOrActivate(
  	Window owner, IDisposable viewModel, Func<Window> createWindow, Action<object> forget,
  	Action<Exception> reportFailure)
  {
  	if (owner.OwnedWindows.FirstOrDefault(window => ReferenceEquals(window.DataContext, viewModel)) is { } open)
  	{
  		open.Activate();

  		return;
  	}

  	var released = false;
  	Window? shown = null;

  	try
  	{
  		shown = createWindow();
  		shown.Closed += (_, _) => Release();
  		shown.Show(owner);
  	}
  	catch (Exception exception)
  	{
  		reportFailure(exception);
  		shown?.Close();
  		Release();
  	}

  	void Release()
  	{
  		if (released)
  		{
  			return;
  		}

  		released = true;
  		try
  		{
  			forget(viewModel);
  			viewModel.Dispose();
  		}
  		catch (Exception exception)
  		{
  			reportFailure(exception);
  		}
  	}
  }
  ```

### `MainWindow/MainWindow.axaml.cs`

- `:36-50`: the three subscriptions call `DialogOpener.ShowOrActivate(this, model, () => new ... { DataContext =
  model }, viewModel.ForgetClosedWindow, viewModel.ReportFailure)`; the editor's factory keeps its `Width` (`:44`).

### `MainWindow/MainWindowViewModel.cs`

- Fields after `_instanceLauncher` (`:43`): `_openSettings`, `_openPenEditor`, `_openExport`, `bool _isClosing`.
- `RequestSettingsAsync`, `RequestPenEditorAsync` and `RequestExport` open with: when the field is set, emit it and
  return. A build stores the view model before it emits.
- `ExitCommand` (`:104-105`) sets `_isClosing` before it emits. `RestartApplication` (`:199-205`) returns when
  `_isClosing` is set, and sets it when the copy started, before it emits.
- `public void ForgetClosedWindow(object viewModel)` clears the field that references `viewModel`.
- The summaries at `:125-132`: a request carries the open view model again while its window is open; the listener
  disposes it when the window closes, after handing it to `ForgetClosedWindow`.
- About 40 lines over 261: at the 300-line preference. If it passes, the three fields and their checks move into a
  private nested holder in the same file, and the run records a ⚠️ under Task 7.

### `Settings/SettingsViewModel.cs`, `Settings/SettingsDialog.axaml.cs`

- `private Task? _save;`; `SaveCommand = ReactiveCommand.CreateFromTask(() => _save = SaveAsync(), canSave)` (`:91`);
  `internal Task? SaveInFlight => _save is { IsCompleted: false } save ? save : null;`.
- `SettingsDialog`'s constructor builds `_closing = new OwnedWindowClosing(this, () => (DataContext as
  SettingsViewModel)?.SaveInFlight)`; `OnClosing` calls `base.OnClosing(e)` only when `_closing.Lets(e)` is true;
  `OnKeyDown` calls `_closing.CloseOnEscape(e)` after `base`.

### `Export/CsvExportViewModel.cs`, `Export/CsvExportWindow.axaml.cs`

- `private Task? _export;`; `ExportCommand = ReactiveCommand.CreateFromTask<string>(path => _export =
  ExportAsync(path), ...)` (`:101-102`); `internal Task? StopExport()` returns null when `_export` is null or done,
  else cancels `_exportCancellation` and returns `_export`.
- `CsvExportWindow`'s constructor builds `_closing` over `() => (DataContext as CsvExportViewModel)?.StopExport()`;
  a new `OnClosing` override calls `base.OnClosing(e)` only when `_closing.Lets(e)` is true; `OnClosed`
  (`:32-38`) keeps its `_blackoutSync` disposal; `OnKeyDown` calls `CloseOnEscape`.

### `Bridge/ArchiveCallQueue.cs`, `PenEditor/PenEditorViewModel.cs`

- `ArchiveCallQueue`: `public bool IsIdle => _tail.IsCompleted;` with a one-line summary.
- `PenEditorViewModel`: `public bool IsIdle => _archiveCallQueue.IsIdle;` beside `WhenIdleAsync` (`:132-135`).

### `PenEditor/PenEditorWindow.axaml.cs`

- `_drain` (`:50`) goes; `private readonly OwnedWindowClosing _closing;` is built in the constructor over
  `PendingWork`.
- `PendingWork()` returns null when `_textEdit is null`, `_scaleForm is null` and
  `(DataContext as PenEditorViewModel)?.IsIdle != false`, else `DrainAsync()`.
- `OnClosing` (`:111-123`): `if (_closing.Lets(e)) { base.OnClosing(e); }`. The comment at `:111` goes; the order is
  in `overview.md#the-pen-and-group-editor`.
- `DrainAsync` (`:125-152`) keeps its steps and its catch (`:144-147`) and loses the `finally` post (`:148-151`),
  which the helper now owns.
- `OnKeyDown` calls `_closing.CloseOnEscape(e)` after `base`.

### Test changes

| Test | Today | After |
| --- | --- | --- |
| `EditPensAndGroups_...` `:78-85` | closes the editor, VM disposed | unchanged: no edit recorded, so the close passes or drains in one hop |
| `ExportToCsv_...` `:121-125` | closes the export, then `RunJobs()` and an immediate `OwnedWindows` check | `HeadlessWait.Until(() => window.OwnedWindows.Count == 0)`: the export window now waits for the cancelled export |
| `EditSettings_...` `:155-162` | closes the dialog, VM disposed | unchanged: no save runs |
| `TheRestartNowButton_...` `:183-187` | one `RunJobs()`, window closed | adds `MessagePanel.Entries.Should().BeEmpty()` |
| `ExitItem_ClosesTheWindow` `:172-175` | no owned window | unchanged |
| `PenEditorViewTests` close tests `:391-416` | editor's own close | unchanged: no owner, so the helper closes the editor |
| `CsvExportWindowTests` `:1111` | Escape closes the drop-down | adds `window.IsVisible.Should().BeTrue()` |
| `PenEditorViewTests` `:921-923` | Escape closes the flyout | adds the window-visible assertion |
| `MainWindowViewTests.MenuItemNamed` `:424-430` | private | moves to `MainWindowTestBuilder` |

No view-model-only test requests one kind twice (Context), so none meets the re-emitted instance without a window
to forget it. A future test that does calls `ForgetClosedWindow(first)` between the two requests.

## What Goes Where

- **Implementation Steps** (`[ ]` checkboxes): each window's open and activate, each window's hold, the one-close
  flag, Escape, the leave-the-editor pins, the docs, the gates.
- **Post-Completion** (no checkboxes): the Win32 checks.

## Implementation Steps

### Task 1: Show the settings window non-modally, once per main window, tests first

**Files:**
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowTestBuilder.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/DialogOpener.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`

- [x] move `MenuItemNamed` from `MainWindowViewTests.cs:424-430` to `MainWindowTestBuilder`; create
      `OwnedWindowsTests` with the three traits
- [x] write `[AvaloniaFact]` `TheSettingsWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns`: over
      `ShippedConfiguration.CopyToTemporaryDirectory()` and `NewWindowStand(configDirectory: ...)`, open
      `Edit` -> `Settings` by menu clicks, `HeadlessWait.Until` a `SettingsDialog` is owned; assert `Owner` is the
      main window and `IsDialog` is false; click `View` -> `ViewLegend` and assert `IsLegendVisible` is false with
      the dialog open
- [x] run it at HEAD: red at `IsDialog` (true); note it under this task. Red with the production change stashed:
      `Expected dialog.IsDialog to be False ... but found True`; the second-request test red too, the
      closed-window test green
- [x] write `ASecondSettingsRequest_ActivatesTheOpenWindow_AndBuildsNoSecondViewModel`: open, `RunJobs()`, count
      `Activated` from here, set `Password = ""` so `SaveCommand` is disabled; run
      `ShowSettingsCommand.Execute().Subscribe()` and `RunJobs()`; assert one owned `SettingsDialog`, the same
      instance and `DataContext`, `Activated` counted once; set `Password = "secret"` and assert `SaveCommand` can
      execute (not disposed). Red at HEAD: two owned dialogs
- [x] write `AClosedSettingsWindow_DisposesItsViewModel_AndTheNextRequestBuildsANewOne`: `Password = ""`, close,
      `Password = "secret"`, `SaveCommand` cannot execute; request again; a new owned dialog over another view model
      (green at HEAD: pins the forget path)
- [x] add `MessagePanel.Entries.Should().BeEmpty()` to `TheRestartNowButton_...` after `:184`
- [x] add `ShowOrActivate` as Technical Details states; `ShowOwnedDialog` stays for export and the editor until
      Task 3
- [x] add `_openSettings`, its check in `RequestSettingsAsync`, and `ForgetClosedWindow` (settings arm)
- [x] point `SettingsRequests` (`MainWindow.axaml.cs:36-37`) at `ShowOrActivate`; the failure window keeps
      `ShowSettings`
- [x] run `OwnedWindowsTests`, `MainWindowViewTests`, `MainWindowViewModelTests`, `StartupFailureWindowTests`: all
      pass

### Task 2: Show the export window non-modally, once per main window, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`

- [x] write `TheExportWindow_IsOwnedAndNotModal_AndTheMainMenuStillRuns`: `NewWindowStand([new Pen(1, "Pen 1", [],
      "#ff0000")])`, open `File` -> `Export to CSV...`, assert `Owner`, `IsDialog` false, the `ViewLegend` click
      (red at HEAD: `IsDialog` true). Red before the production change:
      `Expected exportWindow.IsDialog to be False ... but found True`
- [x] write `ASecondExportRequest_ActivatesTheOpenWindow_AndKeepsItsRange`: a `FakeArchiveRowReader` whose
      `OnCount` records each token; open the export, record `Form.From`, `Form.To` and the count calls; move the
      chart with `ChartViewModel.Navigation.SetWindow(from.AddHours(-1), to.AddHours(-1))`
      (`TrendNavigationModel.cs:76`); run `ExportCommand.Execute().Subscribe()`; assert one owned `CsvExportWindow`,
      the same view model, `Activated` counted once, `Form.From` and `Form.To` unchanged, no new count call, and the
      open count's token not cancelled (red at HEAD: two owned windows). Red before the production change: two
      owned windows. The count is held on its token and started by advancing `CountThrottle`, and the command is
      awaited (Task 1's note)
- [x] add `_openExport`, its check in `RequestExport`, and its `ForgetClosedWindow` arm
- [x] point `ExportRequests` (`MainWindow.axaml.cs:48-50`) at `ShowOrActivate`
- [x] run `OwnedWindowsTests` and `MainWindowViewTests`: all pass

### Task 3: Show the pen editor non-modally, once per main window, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/DialogOpener.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`

- [x] write `ThePenEditor_IsOwnedAndNotModal_AndTheMainMenuStillRuns`: a `FakePenCatalogueEditor` with
      `ReadResult = Result.Ok(PenEditorFixture.ViewCatalogue)`, open `Edit` -> `Pens and groups`, assert `Owner`,
      `IsDialog` false, the `ViewLegend` click (red at HEAD). Red before the production change:
      `Expected editor.IsDialog to be False ... but found True`
- [x] write `ASecondPenEditorRequest_ActivatesTheOpenWindow_AndReadsTheCatalogueOnce`: open, request again with
      `ShowPenEditorCommand.Execute().Subscribe()`; assert one owned `PenEditorWindow`, the same view model,
      `Activated` counted once, `Calls` equal to one `FakeEditorCall.Read` (red at HEAD: two windows, two reads). Red before the production change: two owned editors
- [x] write `ThePenEditorCommand_CannotExecuteWhileItsReadRuns`: `HoldNextCall()`, `Execute().Subscribe()`;
      `((ICommand)ShowPenEditorCommand).CanExecute(null)` is false while the gate holds and true after (green at
      HEAD)
- [x] add `_openPenEditor`, its check, and its `ForgetClosedWindow` arm; point `PenEditorRequests`
      (`MainWindow.axaml.cs:38-47`) at `ShowOrActivate` with the `Width` factory; fold `ShowOwnedDialog` into
      `ShowSettings`
- [x] run `OwnedWindowsTests`, `MainWindowViewTests`, `MainWindowViewModelTests`, `PenEditorViewTests`: all pass. ⚠️ `MainWindowViewModel.cs` is 308
      lines, past the 300-line preference; Task 7 moves the fields into a nested holder as Technical Details states

### Task 4: Hold every close of the pen editor for its drain, the owner's included, tests first

**Files:**
- Create: `SemiPlot/SemiPlot.UI/MainWindow/OwnedWindowClosing.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Bridge/ArchiveCallQueue.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`

- [x] write `[AvaloniaTheory]` `ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow` with
      `[InlineData("Close")]`, `[InlineData("Exit")]`, `[InlineData("Restart")]`, over a stand with a configuration
      copy, one pen, the fake editor and `start: started.Add`: open settings, export and the editor; set
      `Password = ""` and `Groups.NewGroupName = ""`; in the editor click `RowAt(editor, 0)` and `Retype` `FormName`
      to "Argon2"; `HoldNextCall()`; run the route (`window.Close()`, `ExitCommand.Execute().Subscribe()`, or
      `RestartApplication()`); `RunJobs()`. Assert the main window and the three owned windows are visible and
      `Changes` holds one name change. Release the gate; `HeadlessWait.Until(() => !window.IsVisible)`. Assert
      `OwnedWindows` is empty, `Changes` still holds one change; then set `Password = "secret"` and
      `NewGroupName = "Gas"` and assert both commands stay disabled, and the export's count token is cancelled. For
      `Restart`, `started` holds one start. Red at HEAD: the main window is still visible after the release.
      Red before the production change, all three rows: `Timed out waiting for the main window to close after
      the write`
- [x] write `ClosingTheMainWindow_WhileTheEditorDrainsItsOwnClose_StillClosesTheMainWindow`: hold the write,
      `editor.Close()`, `RunJobs()`, `window.Close()`, `RunJobs()`, release; the main window closes, one change (red
      at HEAD). Red before the production change: `Timed out waiting for the main window to close after the drain`
- [x] write `AWriteRefusedDuringTheDrain_StillClosesTheMainWindow`: `ChangeResult` is a
      `FakePenCatalogueEditor.Refusal(ArchiveFault.Unreachable, ...)` failure; hold, close the main window, release;
      the main window closes, one change (red at HEAD). Red before the production change: `Timed out waiting for
      the main window to close after the refused write`
- [x] write `PenEditorViewTests.AnEditTypedDuringAHeldDrain_IsWrittenBeforeTheWindowCloses`: subscribe the editor's
      `Closing` event to snapshot `Changes` (raised only on the pass that lets the close through, Testing Strategy);
      retype `FormName`, hold, `window.Close()`, `RunJobs()`; click `FormUnit` and type "bar"; release;
      `HeadlessWait.Until` the window is gone; assert the snapshot holds the name change and the unit change, once
      each (red at HEAD: the snapshot holds the name only). Red before the production change: the snapshot holds
      one item. The unit is retyped (`Retype`, a click then a clear) so the expected value does not depend on
      the caret
- [x] add `ArchiveCallQueue.IsIdle`, `PenEditorViewModel.IsIdle`, `OwnedWindowClosing` and the editor's
      `PendingWork`, `OnClosing` and `DrainAsync` changes as Technical Details states; Escape waits for Task 8.
      `OwnedWindowClosing.CloseOnEscape` lands now with the class, as Technical Details writes it; its callers
      come in Task 8
- [x] run `OwnedWindowsTests`, `PenEditorViewTests`, `MainWindowViewTests`: all pass

### Task 5: Hold every close of the settings window for a running save, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml.cs`

- [x] write `ClosingTheMainWindow_DuringASave_ClosesItOnlyAfterTheSaveEnds`: open settings, set `Password =
      "secret"`, snapshot `IsRestartPending` in the dialog's `Closing` event; run `SaveCommand.Execute().Subscribe()`
      and, with no `RunJobs()` between, `window.Close()`; assert `IsRestartPending` is still false (the continuation
      ASSUMPTION) and the main window is visible; `HeadlessWait.Until(() => !window.IsVisible)`; assert the snapshot
      is true and the connection file holds the password (red at HEAD: the main window closes at once, with the
      snapshot false). Red before the production change: `Expected window.IsVisible to be True because the
      settings window holds the close while its save runs, but found False`; the continuation ASSUMPTION held
- [x] add `_save` and `SaveInFlight` to `SettingsViewModel`, and the helper to `SettingsDialog`
- [x] run `OwnedWindowsTests`, `MainWindowViewTests`, `StartupFailureWindowTests` and the `Settings` tests: all pass

### Task 6: Hold every close of the export window until a cancelled export has deleted its partial file, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/MainWindowViewTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportViewModel.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs`

- [x] write `ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile`, the pattern of `MainWindowViewTests.cs:89-130`:
      hold the seed read until its token is cancelled; start the export with `ExportCommand.Execute(path).Subscribe(_
      => { }, _ => { })`; await the token; snapshot `File.Exists(out.csv.partial)` in the export window's `Closing`
      event; `window.Close()`, then at once, before any `RunJobs()`, assert `window.IsVisible` is true (the
      deterministic red at HEAD: the cascade closes the main window inside `Close()`); then
      `HeadlessWait.Until(() => !window.IsVisible)`; assert the token is cancelled, the snapshot is false, and the
      partial file does not exist now. Red before the production change: `Expected window.IsVisible to be True
      because the export window holds the close until its cancelled export ends, but found False`
- [x] write `WorkStartedBetweenTwoResumes_IsHeldByTheNextClose`, two holding children: open the editor and the
      export; give the export a seed read that awaits a test gate `exportGate` and ignores its token; start the
      export; in the editor retype `FormName`, `gate1 = HoldNextCall()`; `window.Close()`, `RunJobs()`: the main
      window is visible. Release `gate1`, `RunJobs()`: the editor's resume closes nothing, because the export holds;
      the main window and the editor are visible. In the editor click `FormUnit`, type "bar", `gate2 =
      HoldNextCall()`. Release `exportGate`, `HeadlessWait.Until` the unit change is in `Changes`: the export's
      resume posts `owner.Close()`, the cascade reaches the editor, which finds the recorded edit and holds again,
      so `CloseInternal` never runs; assert the main window and the export window are both visible and the export
      command no longer executes. Release `gate2`, `HeadlessWait.Until(() => !window.IsVisible)`; assert `Changes`
      holds the name and the unit once each (red at HEAD: the main window closes at the first `Close()`; with a
      one-shot hold, the editor lets the export's resume through and the unit is never written). Red before the
      production change: the test's cleanup fails with an `IOException` on `out.csv.partial`, because the main
      window closed at once and the export held on its gate keeps the file open
- [x] change `ExportToCsv_..._AndClosingItCancelsTheExport` at `:121-125`: after `exportWindow.Close()`,
      `await HeadlessWait.Until(() => window.OwnedWindows.Count == 0)` replaces the `RunJobs()` and the immediate check
- [x] add `_export` and `StopExport()` to `CsvExportViewModel`, and the helper to `CsvExportWindow`
- [x] run `OwnedWindowsTests`, `MainWindowViewTests`, `CsvExportWindowTests`, `CsvExportViewModelTests`: all pass

### Task 7: One close per main window, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/MainWindow/MainWindowViewModel.cs`

- [x] extend the `Restart` row of `ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow`: a
      second `RestartApplication()` while the write is held; assert `started` holds one start before and after the
      release (red at HEAD and after Task 6: two starts). Red before the production change, the `Restart` row
      only: `started` held two starts
- [x] add `_isClosing` to `ExitCommand` and `RestartApplication` as Technical Details states
- [x] run `OwnedWindowsTests`, `MainWindowViewModelTests`, `AppMenuBarTests`, `MainWindowViewTests`: all pass (65).
      ⚠️ The three fields and `ForgetClosedWindow`'s arms moved into the private nested `OpenWindows`;
      `MainWindowViewModel.cs` is 333 lines with it, still past the 300-line preference, because a nested holder
      keeps its lines in the file

### Task 8: Escape closes each of the three windows, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/MainWindow/OwnedWindowsTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Export/CsvExportWindowTests.cs`
- Modify: `SemiPlot/SemiPlot.UI/Settings/SettingsDialog.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml.cs`

- [x] write `Escape_ClosesTheSettingsWindow` and `Escape_ClosesTheExportWindow`: open the window,
      `HeadlessInput.Press(dialog, PhysicalKey.Escape)`, `RunJobs()`; the window is closed and its view model
      disposed (red at HEAD: still open)
- [x] write `PenEditorViewTests.Escape_WritesTheTypedNameOnceThenClosesTheEditor`: retype `FormName`, press Escape,
      `HeadlessWait.Until` the window is gone; one name change (red at HEAD)
- [x] add `window.IsVisible.Should().BeTrue()` after the Escape at `CsvExportWindowTests.cs:1111` and at
      `PenEditorViewTests.cs:923` (green at HEAD: pins that a handled Escape keeps the window)
- [x] add `OnKeyDown` overrides calling `CloseOnEscape` in the three windows
- [x] run `OwnedWindowsTests`, `PenEditorViewTests`, `CsvExportWindowTests`: all pass (112).
      ⚠️ The Escape now closes the window on key down, so the headless key release met a disposed window;
      `HeadlessInput.Press` skips the release once the press closed its `TopLevel`. The pen editor pin sits in
      `PickColor`, the helper that held `:921-923`

### Task 9: Pin that leaving the pen editor writes its pending edit once (no production change)

**Files:**
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`

- [x] write `ANameTypedThenTheWindowLosesFocus_WritesItOnce`: realise the editor, retype `FormName`, raise
      `window.PlatformImpl!.LostFocus?.Invoke()` (the path Win32's `WM_KILLFOCUS` takes, Context), `RunJobs()`;
      assert one name change before any close; then close through `PenEditorWindowDriver.Close`; still one change
      (green at HEAD, regression pin)
- [x] write `AScalePairTypedThenTheWindowLosesFocus_WritesItOnce`: type both scale bounds, raise the same callback,
      `RunJobs()`; one scale change before any close; close; still one (green at HEAD, regression pin)
- [x] run them at HEAD and after Task 8: green both times; note it under this task
- [x] run `PenEditorViewTests`: all pass

Result (2026-11-09): both pins green at HEAD `3cf943f`, which already carries Task 8, so the one run covers both
points; `PenEditorViewTests` 37 of 37 pass. The reference assembly of Avalonia 12.0.5 marks
`ITopLevelImpl.LostFocus` internal (CS0122), so the tests raise the callback through reflection in
`RaisePlatformLostFocus`; at run time the property is public.

### Task 10: Update documentation

- [x] `docs/architecture/overview.md:61-64`: the diagram names the three as windows ("pens and groups window",
      "range to CSV file window", "app and connection settings"), About stays "version and config folder"
- [x] `overview.md:257`: "the settings window" in place of "the settings dialog"
- [x] `overview.md#the-settings-window` (`:413`): owned and non-modal from the main window, once per window; modal
      from the failure window; Escape closes it; `:424-426`: any close during a save, the main window's included,
      waits for the save to end (`SaveInFlight`), so the process no longer ends inside it; the command rule for
      Restart now stays
- [x] `overview.md:180-182`: a second Restart now or Exit during a held close starts no second copy
- [x] `overview.md:676-678`: shown through `DialogOpener.ShowOrActivate`, once; a second request activates; the view
      disposes on `Closed`
- [x] `overview.md:758-761`: every close, the owner's included, is held by `OwnedWindowClosing` while an edit is
      recorded or the queue is busy, and asks again on the next close, so an edit made during a drain is written
      before the window closes; an owner close resumes on the owner; Escape closes through the drain. Leaving the
      editor clears its focus, so the field's `LostFocus` writes the pending edit. Name
      `OwnedWindowsTests.ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow` and
      `PenEditorViewTests.ANameTypedThenTheWindowLosesFocus_WritesItOnce`
- [x] `overview.md:1046`: "an owned, non-modal window"; a second request brings it to the front over its range
- [x] `overview.md:1102-1107`: any close of the window during an export, its own or the main window's, cancels the
      export and waits until `StopExport()`'s task ends, so the exporter has deleted the partial file before the
      window closes; the sentence on the `.partial` gap goes. Name
      `ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile`
- [x] `overview.md#one-window-per-process` (`:108`): the three owned windows, one of each kind, close with the trend
      window after their own work
- [x] `docs/architecture/charting.md:677`: "settings window" in place of "settings dialog"; `:686`, `:689`, `:693`:
      "the owned, non-modal window"; `:684` About stays "the modal"; add `MainWindow/OwnedWindowClosing` beside
      `DialogOpener`
- [x] `docs/architecture/ui-theme.md:528-529`: `DialogOpener.ShowOrActivate`
- [x] `readme.md`: no change; rerun `grep -in "modal\|dialog" readme.md`: prints nothing

Result (2026-11-09): ⚠️ the shared rules (open or activate, the release on `Closed`, the close hold, Escape) are
stated once in a new `overview.md#the-owned-windows`; the per-window passages state their own work and link to it.
`charting.md` had no `DialogOpener` entry, so `DialogOpener` and `OwnedWindowClosing` got one bullet together.

### Task 11: Verify acceptance criteria

- [x] run the filtered command under Acceptance Evidence: 0 failed, every named test present (note the pass count)
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
- [x] run `dotnet build SemiPlot.slnx`: 0 errors, no warning beyond the NU1902 the base reports
- [x] set `$BASE` as Acceptance Evidence states
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore` and `dotnet terse` over every
      `.cs` file `git diff --name-only $BASE...HEAD` lists: both exit 0
- [x] run every git grep gate under Acceptance Evidence: each prints what it must
- [x] move this plan to `docs/plans/completed/`

Result (2026-11-09): the filtered command passed 181 of 181, every named test present and green (the close theory
with its 3 rows); the full unit suite passed 2764 of 2764; the build reported 0 errors and only the AppHost NU1902;
`$BASE` is `638db7f` (still an ancestor); format and terse over the 17 touched `.cs` files exited 0; every git grep
gate printed what it must (`ShowDialog(` only at `DialogOpener.cs:13` in `ShowAbout` and `:74` in `ShowSettings`).

## Post-Completion

**Manual verification** in the running viewer on Windows, in both languages:

1. Open settings, export and the pen editor. Each stays above the main window; a drag and a zoom on the chart work
   while each is open; the sidebar and the menus respond.
2. Minimise the main window: the three owned windows go with it and come back with it.
3. `Edit` -> `Settings` a second time brings the open window to the front; the same for the other two.
4. In the editor, type a name and click the chart: the name is written. Type a name and close the main window from
   the title bar: the main window closes after the name is written.
5. Start an export, then close the main window: the export stops, the main window closes after it, and no
   `.partial` file is left in the target folder. Start another export and close the export window itself: the same.
6. Escape closes each of the three windows; Escape on an open calendar of the export, a combo box of settings or
   the editor's colour flyout closes only that popup.
7. Open the export's `Save as` file picker, then close the main window from the taskbar: note whether the picker
   blocks the close and whether anything is left open.
8. Open a calendar popup of the export, then close the main window: everything closes and nothing is left.

**Executed by exec:**
- branch: non-modal-windows

## Verify it yourself

1. Non-modal. With each window open, a menu click on the main window runs its command, and the window reports
   `IsDialog` false (`TheSettingsWindow_...`, `TheExportWindow_...`, `ThePenEditor_IsOwnedAndNotModal_...`).
2. One instance. A second request activates the open window and builds nothing; the editor reads once, the export
   keeps its range (`ASecondSettingsRequest_...`, `ASecondExportRequest_...`, `ASecondPenEditorRequest_...`).
3. Close order. A main window close, by `Close`, `Exit` or `Restart`, waits for the editor's held write, then closes
   every owned window and itself with one write and one copy started
   (`ClosingTheMainWindow_WaitsForTheEditorsWrite_ThenClosesEveryOwnedWindow`); work started between two resumes is
   held by the next close (`WorkStartedBetweenTwoResumes_IsHeldByTheNextClose`).
4. Save and export. The close waits for a running save, and for a cancelled export to delete its partial file
   (`ClosingTheMainWindow_DuringASave_...`, `ClosingTheMainWindow_DuringAnExport_LeavesNoPartialFile`).
5. Escape. Escape closes each window, the editor after its drain (`Escape_...`).
6. Leaving the editor. A focus loss writes the pending edit once; these two tests are green at HEAD, regression pins
   for behaviour this plan keeps (`ANameTypedThenTheWindowLosesFocus_WritesItOnce`,
   `AScalePairTypedThenTheWindowLosesFocus_WritesItOnce`).

Run all of it: the Acceptance Evidence command of this plan (186 passed, 0 failed on `5ef05b9`).
