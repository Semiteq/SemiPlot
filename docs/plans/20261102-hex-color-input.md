# Hex colour input on the shared formatted-field behaviour

## Overview

- The pen form's colour box (`FormColor`) is a free text box. The operator can delete the `#` or
  paste `1F77B4`, and the form then refuses the value with `PenFormColorInvalid`
  (`PenFormRules.cs:21-23`), because only a seven-character `#` form parses
  (`PenColorConverters.cs:15-22`); lower-case digits with the `#` are accepted and stored as typed.
- The fix gives the box the same input behaviour the time and date boxes already have: the box
  keeps only the characters the format allows and inserts the fixed ones itself. For a colour that
  means hex digits only, upper-cased, the leading `#` maintained by the behaviour, the caret kept on
  the digit it followed, and a paste with or without `#` accepted. The placeholder shows
  `#RRGGBB`.
- The behaviour stays one class with one algorithm. Because it will no longer serve times only,
  `Navigation/TimeFieldInput` and `TimeFieldLayout` move to `Input/FormattedFieldInput` and
  `FieldLayout`.
- The pen form's commit semantics do not change: the box still writes when its edit ends,
  through `PenFormViewModel.EndEditAsync(PenField.Color)`.

## Context (from discovery)

Roadmap: docs/plans/roadmaps/20261102-visual-chore-roadmap.md — slice hex-color-input

- Files and components involved:
  - `SemiPlot/SemiPlot.UI/Navigation/TimeFieldInput.cs:10-141`: the `TimeFieldLayout` enum with
    `ClockTime`, `Date` and `Duration` (`:11-21`), and the attached `Layout` property
    (`:28-29`). The class handler subscribes `TextChanged` when a layout is set (`:46-54`).
    `OnTextChanged` counts the digits before the caret, regroups, and sets the text and the caret
    with `SetCurrentValue` (`:56-73`).
  - Consumers of the behaviour: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanel.axaml:24, :41, :71, :101`
    and `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml:40, :81, :121`.
  - `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/TimeFieldInputTests.cs:18-112`: theories for the
    three layouts, the caret after a new colon (`:66-77`), clearing (`:79-89`), a box without a
    layout (`:91-101`). Typing goes through `HeadlessInput.Type`, which clicks the box first, so the
    box has focus while it types (`HeadlessInput.cs:66-71`).
  - `docs/architecture/charting.md:602-607` describes `Navigation/TimeFieldInput`.
  - The colour box `FormColor` and the `ColorPicker` beside it:
    `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml:345-360`. The box carries
    `local:PenEditorWindow.Field="Color"`, so its edit ends on its own (`PenEditorWindow.axaml:347`,
    `PenEditorWindow.axaml.cs:152-177`).
  - `PenFormViewModel.Color` is a draft property (`PenFormViewModel.cs:76-79`), seeded from the
    stored pen (`:284-285`). `PickColorAsync` formats a picked colour and ends the edit
    (`:186-190`).
  - `PenColorConverters`: `StoredColorLength = 7` (`PenColorConverters.cs:8`), `TryParse`
    (`:15-22`), `Format` (`:26`).
  - View tests on the box: the stored colour is shown as stored (`PenEditorViewTests.cs:231`), a
    picked swatch writes one change and shows its hex (`:498-517`), and a pen with no colour opens
    with the `invalid` class (`:541-564`).
- Related patterns: an attached behaviour configured by an enum, registered with
  `AddClassHandler<TextBox>`. Field formats are literals, not resources
  (`docs/architecture/ui-text.md:87-97`, "What stays a literal").
- Dependencies: none new.

## Development Approach

- **Testing approach**: TDD. Each behaviour case is written as a failing test first, then
  implemented.
- Complete each task fully before moving to the next.
- Make small, focused changes.
- **CRITICAL: every task MUST include new/updated tests** for the code changes in that task.
- **CRITICAL: all tests must pass before starting the next task.**
- **CRITICAL: update this plan file when scope changes during implementation.**
- Run tests after each change.
- The time, date and duration layouts keep their behaviour exactly.

## Testing Strategy

- **Unit tests**: `FormattedFieldInputTests` (the renamed `TimeFieldInputTests`) keeps every
  existing case unchanged and gains the hex layout's cases. `PenEditorViewTests` gains the colour
  box's end-to-end cases through the realised window.
- **E2E**: none beyond the headless view tests; the project has no separate e2e suite.
- Test classes carry the three traits (`Component`, `Area`, `Category`), as `AGENTS.md` requires.

## Acceptance Evidence

The defect is automatable. Today hex digits typed without the `#` are refused, and the `#` can be
deleted.

At HEAD the new view test `ALowerCaseHexTypedWithoutTheHash_WritesTheUpperCaseColour` fails. It
types into the empty colour box of Power, the fixture pen stored with no colour.
`PenFormRules.ColorRule` accepts only what `PenColorConverters.TryParse` parses, which requires
length 7 and a leading `#`, so the box keeps `1f77b4`, the edit end refuses it, and
`_editor.Changes` stays empty.

After the fix:

```powershell
dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj --filter "FullyQualifiedName~FormattedFieldInputTests|FullyQualifiedName~PenEditorViewTests"
```

It must report 0 failed. These named tests must be present and pass:

- `AHexColorField_KeepsHexDigitsUpperCasesThemAndPutsTheHash` (theory: `1f77b4` → `#1F77B4`,
  `#1f77b4` → `#1F77B4`, `#1F77B4` → `#1F77B4`, `1g2h3` → `#123`, `1F77B4AA` → `#1F77B4`,
  `#` → empty)
- `AHexDigitTypedAfterTheHash_KeepsTheCaretAfterThatDigit`
- `ANonHexCharacterTypedAfterTheHash_LeavesTheTextAndTheCaretAfterTheHash`
- `BackspaceOnTheHash_KeepsTheTextAndTheCaretAfterTheHash`
- `BackspaceOnTheOnlyDigit_EmptiesTheBox`
- `SelectAllAndBackspace_EmptiesAFilledHexBox`
- `APasteWithOrWithoutTheHash_IsNormalised`
- `DeleteOnTheHash_KeepsTheTextAndPutsTheCaretAfterTheHash`
- `ACutOfTheHash_PutsTheHashBack`
- `UndoAfterTypedDigits_LeavesANormalisedText` (redo runs through the same `KeyDown` branch and the
  same `CanRedo` guard, so this case covers it)
- `AValueSetInCode_StaysAsSet` (theory: `red`, `#1f77b4`, unfocused and focused)
- `AValueSetInCodeAfterBackspaceAtTheStart_StaysAsSet`
- `ALowerCaseHexTypedWithoutTheHash_WritesTheUpperCaseColour` (view)
- `ARefusedDraftRevertedOnEnter_ShowsTheStoredColourAsStored` (view; passes at HEAD as well, a
  guard on the operator-edit flag rather than a reproduction)
- `AStoredLowerCaseColour_ShowsAsStoredAndWritesNothingOnFocusAndLeave` (view; passes at HEAD as
  well, a regression guard rather than a reproduction)

The existing cases of the three time layouts and the view tests at `PenEditorViewTests.cs:231`,
`:498-517` and `:541-564` pass unchanged.

## Progress Tracking

- Mark completed items with `[x]` immediately when done.
- Add newly discovered tasks with the ➕ prefix.
- Document issues and blockers with the ⚠️ prefix.
- Keep the plan in sync with the work done.

## Solution Overview

- **One behaviour, one more layout.** `FormattedFieldInput` keeps its shape: an attached `Layout`
  of type `FieldLayout?` that subscribes `TextChanged`. It normalises the text and puts the caret
  back after the same number of allowed characters. `FieldLayout` gains `HexColor` beside
  `ClockTime`, `Date` and `Duration`. The pipeline becomes "allowed characters of the layout →
  normalised text → caret index", and each layout supplies two things: which characters it
  allows, and how they are laid out.
- **The hex layout normalises only what the operator enters.** The editor shows a colour as
  stored, `red` or `#1f77b4` included (`docs/architecture/overview.md:529-531`), and a binding
  write must stay as written: a normalised `#1F77B4` would differ from the stored text and the
  next edit end would write it. The behaviour therefore marks the operator's routes into the box
  (typing, Backspace, Delete, undo, redo, cut, paste) and normalises on the `TextChanged` they
  cause; a `TextChanged` raised by a binding write, such as the revert after a refused Enter
  (`PenEditorWindow.axaml.cs:179-195`, `PenFormViewModel.cs:243-258`), leaves the text as set. The
  three time layouts keep normalising on every change.
- **Why a flag on the operator's routes.** In Avalonia 12.0.5 every operator route writes `Text`
  through `SetCurrentValue`, and `TextChangedEventArgs` carries no origin, so nothing public tells
  a binding write from an edit; the list of operator routes is closed (typing, Backspace, Delete,
  undo, redo, cut, paste). Normalising in the view model cannot tell `Seed` from the binding, a
  `ConvertBack`-only converter lets the echo move the caret, always normalising writes a stored
  `#1f77b4` back as `#1F77B4` on focus and leave, and intercepting each route is a second
  algorithm beside the time layouts' normaliser.
- **Caret rule for a prefixed format.** The caret's position is counted in hex digits before it.
  It returns after that many digits, plus one for the `#`. With no digit before it, it returns
  right after the `#` (index 1), or to 0 when the text is empty.
- **Overflow.** More than six digits keep the first six, which is how the clock layout already
  treats a seventh digit (`TimeFieldInputTests.cs:24`).
- **The placeholder `#RRGGBB` is a format literal.** It lives beside the stored length in
  `PenColorConverters` and is listed under `ui-text.md`'s "What stays a literal".
- **Rename and move.** `Navigation/TimeFieldInput.cs` becomes `Input/FormattedFieldInput.cs`, with
  namespace `SemiPlot.UI.Input`, and `TimeFieldLayout` becomes `FieldLayout`. The two AXAML
  consumers change their `xmlns` prefix and attribute name only. The test class moves to
  `UI/Input/FormattedFieldInputTests.cs`.

## Technical Details

- `FieldLayout`: `ClockTime`, `Date`, `Duration`, `HexColor`.
- Normalising a `HexColor` text:
  - Take the characters that pass `char.IsAsciiHexDigit`, upper-cased with
    `char.ToUpperInvariant`, at most 6.
  - The result is empty when no digit is left, otherwise `#` plus the digits.
- Caret for `HexColor`: `digitsBeforeCaret` counts hex digits in the raw text before
  `CaretIndex`. The new index is `min(digitsBeforeCaret, 6) + 1` when the result is not empty, and
  0 when it is empty.
- The `HexColor` layout keeps a per-box "operator edit pending" flag in a private attached
  property, `OperatorEditPendingProperty`, registered with
  `AvaloniaProperty.RegisterAttached<TextBox, bool>("OperatorEditPending", typeof(FormattedFieldInput))`
  beside `LayoutProperty`. `OnLayoutChanged` subscribes the flag's routes only when the new layout
  is `HexColor`, and removes them whenever the layout changes:
  `AddHandler(InputElement.TextInputEvent, ..., RoutingStrategies.Tunnel)`,
  `AddHandler(InputElement.KeyDownEvent, ..., RoutingStrategies.Tunnel)`, `PastingFromClipboard`,
  `CuttingToClipboard` and `LostFocus`.
  - The `TextInput` handler sets the flag.
  - The `KeyDown` handler sets it for Backspace and Delete when the key will change the text (a
    non-empty selection, or Backspace with the caret past 0, or Delete with the caret before the
    end), and for an undo or redo gesture when `CanUndo` or `CanRedo` holds, matching the gestures
    with `KeyGesture.Matches` against `Application.Current?.PlatformSettings?.HotkeyConfiguration`
    `.Undo` and `.Redo`, the lists `TextBox.OnKeyDown` reads. Any other key clears the flag. The
    handler tunnels because `TextBox.OnKeyDown` raises `PastingFromClipboard` and
    `CuttingToClipboard` from inside the key's own handling, and a bubble handler would run after
    them and clear the flag they set.
  - `PastingFromClipboard` and `CuttingToClipboard` set the flag.
  - `LostFocus` clears it, so a route that changed nothing (a selection replaced by the same text,
    Shift+Delete with no selection, a paste of an empty clipboard) leaves no flag for the next
    binding write.
  - `OnTextChanged` reads and clears the flag first, before the unchanged-text return, and
    normalises a `HexColor` box only when the flag was set.
  - The time layouts subscribe none of these routes and normalise on every change.
  - The `TextBox` posts `TextChanged` at `DispatcherPriority.Normal`, so a test drives
    `Dispatcher.UIThread.RunJobs()` after each gesture, as `HeadlessInput` does.
- `public const string FieldFormat = "#RRGGBB"` in `PenColorConverters`, used as
  `PlaceholderText="{x:Static local:PenColorConverters.FieldFormat}"` on `FormColor`.
- `FormColor` gains `input:FormattedFieldInput.Layout="HexColor"`. Its `Text` binding, its
  `Field` attachment and its `invalid` class binding stay.
- Data flow after the change: the operator types. The behaviour normalises `TextBox.Text` on the
  `TextChanged` the operator's edit caused. The two-way binding moves the normalised text into `PenFormViewModel.Color`.
  The edit ends on Tab, Enter, a click elsewhere or the close, as today. `EndEditAsync` then
  writes `PenSettingChange.Color`.

## What Goes Where

- **Implementation Steps**: code, tests, and the `charting.md`, `overview.md` and `ui-text.md`
  updates in this repository.
- **Post-Completion**: the manual check in the running viewer, and the remaining roadmap slices.

## Implementation Steps

### Task 1: Rename and move the input behaviour without changing it

**Files:**
- Create: `SemiPlot/SemiPlot.UI/Input/FormattedFieldInput.cs` (moved from `Navigation/TimeFieldInput.cs`)
- Delete: `SemiPlot/SemiPlot.UI/Navigation/TimeFieldInput.cs`
- Modify: `SemiPlot/SemiPlot.UI/Chart/TimeWindowPanel.axaml`
- Modify: `SemiPlot/SemiPlot.UI/Export/CsvExportWindow.axaml`
- Create: `SemiPlot/SemiPlot.Tests.Unit/UI/Input/FormattedFieldInputTests.cs` (moved from `UI/Navigation/TimeFieldInputTests.cs`)
- Delete: `SemiPlot/SemiPlot.Tests.Unit/UI/Navigation/TimeFieldInputTests.cs`

- [x] `git mv` the class to `Input/FormattedFieldInput.cs`, set the namespace to `SemiPlot.UI.Input`, rename the class to `FormattedFieldInput` and the enum to `FieldLayout`; keep every member's behaviour
- [x] in `TimeWindowPanel.axaml` and `CsvExportWindow.axaml`, replace `xmlns:navigation="clr-namespace:SemiPlot.UI.Navigation"` (line 6) with `xmlns:input="clr-namespace:SemiPlot.UI.Input"`, and `navigation:TimeFieldInput.Layout` with `input:FormattedFieldInput.Layout` (`TimeWindowPanel.axaml` lines 24, 41, 71, 101; `CsvExportWindow.axaml` lines 40, 81, 121)
- [x] `git mv` the test class to `UI/Input/FormattedFieldInputTests.cs`, rename it, set its namespace to `SemiPlot.Tests.Unit.UI.Input`, and keep every case, `[Collection(ProcessGlobalStateCollection.Name)]` and the three traits as they are (`Area` stays `Chart`)
- [x] confirm `git grep -n "TimeFieldInput\|TimeFieldLayout" -- SemiPlot` prints nothing
- [x] run `dotnet build SemiPlot.slnx` and the moved tests; all existing cases pass

### Task 2: Add the HexColor layout, tests first

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/Input/FormattedFieldInput.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/Input/FormattedFieldInputTests.cs`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs`

- [x] add to `HeadlessInput` the overload `Type(TopLevel topLevel, TextBox textBox, string text, int caretIndex)`, which clicks the box, sets `CaretIndex` to `caretIndex`, sends the text and runs the jobs, and the overload `Press(TopLevel topLevel, PhysicalKey key, RawInputModifiers modifiers)`, to which the existing `Press` delegates with `RawInputModifiers.None`
- [x] write the failing theory `AHexColorField_KeepsHexDigitsUpperCasesThemAndPutsTheHash` with the six cases listed under Acceptance Evidence
- [x] write the failing cases `AHexDigitTypedAfterTheHash_KeepsTheCaretAfterThatDigit` (start `#ABCDEF`, type `1` at caret 1 through the caret overload of `HeadlessInput.Type`: text `#1ABCDE`, caret 2) and `ANonHexCharacterTypedAfterTheHash_LeavesTheTextAndTheCaretAfterTheHash` (start `#ABCDEF`, type `g` at caret 1 the same way: text `#ABCDEF`, caret 1)
- [x] write the failing case `BackspaceOnTheHash_KeepsTheTextAndTheCaretAfterTheHash` (click into `#1F77B4`, set `CaretIndex` to 1, `HeadlessInput.Press` Backspace: text `#1F77B4`, caret 1) and `BackspaceOnTheOnlyDigit_EmptiesTheBox` (click into `#1`, caret at the end, Backspace: text empty, caret 0)
- [x] write the failing case `SelectAllAndBackspace_EmptiesAFilledHexBox` through `HeadlessInput.Clear`
- [x] write the failing case `APasteWithOrWithoutTheHash_IsNormalised` as an `async Task` theory over `1f77b4` and `#1f77b4`: `await TopLevel.GetTopLevel(field)!.Clipboard!.SetTextAsync(pasted)`, then `field.Paste()` and `Dispatcher.UIThread.RunJobs()`; the empty box shows `#1F77B4`. The headless platform supplies `HeadlessClipboardImplStub` as the clipboard, and the class runs in `ProcessGlobalStateCollection`, which a process-wide clipboard needs
- [x] write the failing cases `DeleteOnTheHash_KeepsTheTextAndPutsTheCaretAfterTheHash` (click into `#1F77B4`, set `CaretIndex` to 0, `HeadlessInput.Press` Delete: text `#1F77B4`, caret 1), `ACutOfTheHash_PutsTheHashBack` (`#1F77B4`, `SelectionStart` 0 and `SelectionEnd` 1, `field.Cut()`, `Dispatcher.UIThread.RunJobs()`: text `#1F77B4`) and `UndoAfterTypedDigits_LeavesANormalisedText` (type `1f77b4` into an empty box, press Ctrl+Z through `HeadlessInput.Press` with `RawInputModifiers.Control`: the text matches `^(#[0-9A-F]{1,6})?$`)
- [x] write the failing theory `AValueSetInCode_StaysAsSet` (`red`, `#1f77b4` set through `field.Text` stay as set, with the box unfocused and again after a click into the box)
- [x] write the failing case `AValueSetInCodeAfterBackspaceAtTheStart_StaysAsSet` (click into `#1F77B4`, press Home, press Backspace, then set `field.Text = "red"` in code: text `red`)
- [x] add `FieldLayout.HexColor`, its normalising arm, its caret rule and the operator-edit flag; keep the three time layouts' paths unchanged
- [x] run the class's tests: every new and existing case passes

### Task 3: Use the HexColor layout on the pen form's colour box

**Files:**
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenColorConverters.cs`
- Modify: `SemiPlot/SemiPlot.UI/PenEditor/PenEditorWindow.axaml`
- Modify: `SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`

- [x] write the failing view test `ALowerCaseHexTypedWithoutTheHash_WritesTheUpperCaseColour`: select Power (`RowAt(window, 2)`, stored with no colour, so the box opens empty), type `1f77b4` into `FormColor`, press Tab; `_editor.Changes` equals `new FakeEditorCall.Change(Power, new PenSettingChange.Color("#1F77B4"))` and the box shows `#1F77B4`
- [x] write the view test `AStoredLowerCaseColour_ShowsAsStoredAndWritesNothingOnFocusAndLeave`: a pen stored with `#1f77b4` shows `#1f77b4`; a click into the box and a Tab out write nothing
- [x] write the view test `ARefusedDraftRevertedOnEnter_ShowsTheStoredColourAsStored` (passes at HEAD; it goes red against a layout that normalises every change, so it guards the operator-edit flag): a pen stored with colour `red`; select it, type `1` into `FormColor`, press Enter; the box shows `red`, carries the `invalid` class, the message is `PenFormColorInvalid`, and `_editor.Changes` is empty
- [x] add `public const string FieldFormat = "#RRGGBB"` to `PenColorConverters`
- [x] add `xmlns:input="clr-namespace:SemiPlot.UI.Input"` to the root of `PenEditorWindow.axaml`, then set `input:FormattedFieldInput.Layout="HexColor"` and `PlaceholderText="{x:Static local:PenColorConverters.FieldFormat}"` on `FormColor`; Task 4's build is the check that XamlX resolves a `const` through `x:Static`
- [x] run `PenEditorViewTests` and `PenFormViewModelTests`: the new cases and the existing colour cases (`PenEditorViewTests.cs:231`, `:498-517`, `:541-564`; `PenFormViewModelTests.cs:84-87`) pass

### Task 4: Verify acceptance criteria

- [x] run the command under Acceptance Evidence: 0 failed, every test named under Acceptance Evidence present
- [x] run `dotnet test SemiPlot/SemiPlot.Tests.Unit/SemiPlot.Tests.Unit.csproj`: 0 failed
- [x] run `dotnet build SemiPlot.slnx`: 0 warnings, 0 errors (0 errors; the only warnings are the 2 pre-existing NU1902 for MessagePack 2.5.302 in `SemiPlot.AppHost`, a project this branch does not touch)
- [x] run `dotnet format SemiPlot.slnx --verify-no-changes`, then `dotnet tool restore; dotnet terse SemiPlot/SemiPlot.UI/Input/FormattedFieldInput.cs SemiPlot/SemiPlot.UI/PenEditor/PenColorConverters.cs SemiPlot/SemiPlot.Tests.Unit/UI/HeadlessInput.cs SemiPlot/SemiPlot.Tests.Unit/UI/Input/FormattedFieldInputTests.cs SemiPlot/SemiPlot.Tests.Unit/UI/PenEditor/PenEditorViewTests.cs`: both exit 0

### Task 5: [Final] Update documentation

- [ ] `docs/architecture/charting.md:602-607`: rename the entry to `Input/FormattedFieldInput`; add the `HexColor` layout, its operator-edit flag and its caret rule; keep "Selection, Backspace and paste stay the `TextBox`'s own" for the time layouts and state that a `HexColor` box normalises after those routes and leaves a binding write as set; add the pen form's colour box to its consumers
- [ ] `docs/architecture/overview.md`, the editor's Pens row (line 525): the colour box normalises to `#RRGGBB` as typed and shows a stored value as stored
- [ ] `docs/architecture/ui-text.md`, "What stays a literal": add the colour box's `#RRGGBB` placeholder (`PenColorConverters.FieldFormat`)
- [ ] move this plan to `docs/plans/completed/`

## Post-Completion

**Manual verification** in the running viewer (`Edit` -> `Pens and groups`):

1. Type `e15759` into the colour box of a pen; the box shows `#E15759` as you type.
2. Paste `#1f77b4`; the box shows `#1F77B4`.
3. Put the caret right after `#` and type `g`; nothing changes and the caret stays after `#`.
4. Press Tab; the pen's colour changes once in the running chart (through `PenCatalogueSync`).
5. Select a pen whose stored colour is lower case or a name; the box shows it unchanged, marked
   invalid where the rule refuses it.

**Remaining slices** of `docs/plans/roadmaps/20261102-visual-chore-roadmap.md`:

- pen-editor-pens-pane: a `DataGrid` pen list and a sectioned 420 px form; closes #111.
- pen-editor-groups-pane: the tabs go; the groups become a third pane with a row menu.
- pen-list-filter: the top bar's filter over id, name and group.
- export-pen-choice-list: the export pen list becomes checkboxes only.
- axis-scale-checkbox-column: the log checkbox moves to the field column.
- ui-spacing-scale: every remaining inset moves onto the 4/8/12/16/24/32 scale.
