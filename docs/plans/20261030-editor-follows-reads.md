# The pen editor follows catalogue reads

## Overview

Semiteq/SemiPlot#140 item 3: an open pen editor reconciles each catalogue read per field and never
rebuilds a form. A field with no draft and no queued write takes the stored value; a draft keeps it.
`IPenCatalogueEditor.ReadAsync` is deleted; the editor opens on the sync's snapshot and follows its
reads. Split out of `20261029-catalogue-read-and-group-fold.md` (Decision 3 there); it starts after
that plan ships.

## Decisions

- **Reconcile to state, not to deltas.** The editor subscribes to `PenCatalogueSync.Changes` and
  reconciles its rows and groups by id against `read.Stored`: rows join, leave, or revise in place
  through `PenRowViewModel.Revise(StoredPen)`. A read the applier rebased over still reaches the
  editor whole, so no change can be lost between two emissions.
- **Refresh awaits its own read.** `ArchiveReadLoop` gains `Task<Result<T>> ReadNowAsync()`: it wakes
  the loop like `ReadNow()` and completes with the outcome of the first read that starts after the
  call (a read in flight at the call does not count). `PenCatalogueSync.ReadNowAsync()` exposes it
  and completes after `Changes` has emitted, so refresh never finishes ahead of reconciliation.
  The loop keeps reporting read failures itself; refresh reports none of its own.
- **Open on the snapshot.** `PenCatalogueSync.Current` is the last read it compared against (the
  rebased read after a failed apply); `MainWindowViewModel.RequestPenEditorAsync` builds the editor
  on `Current.Stored` and issues no read.

## Context

- Today `MainWindowViewModel.RequestPenEditorAsync` reads `IPenCatalogueEditor.ReadAsync`, and
  `PenEditorViewModel.RefreshAsync` rebuilds rows and groups whole after `RegisterNewPensAsync`,
  reading again through `ReadAfterEveryQueuedCallAsync` and keeping selections by id.
- The per-field follow mechanism exists for the editor's own writes: `PenFormViewModel` seeds only
  fields whose draft still equals the previous pen value (`Follow`, `Shows`), and `PenRowViewModel`
  tracks queued writes. The groups tab queues renames in `PenGroupViewModel`.
- `PenEditorViewModel._isReorderingRows` brackets a programmatic row change against the table's
  selection write-back; joins and leaves reuse it.
- `ArchiveReadLoop` (`SemiPlot/SemiPlot.UI/Bridge/ArchiveReadLoop.cs`) wakes through a
  `TaskCompletionSource` that `ReadNow()` completes; with the synchronous `FakeDataProvider` a whole
  read can finish inside `ReadNow()`.

## Implementation Steps

### Task 1: the snapshot and the awaited read

- [ ] `ArchiveReadLoop.ReadNowAsync()` with the "first read started after the call" rule; unit tests over a `TestScheduler`: a call during a read completes with the next read, a call while waiting completes with the read it wakes, a failed read completes the task with the failure
- [ ] `PenCatalogueSync.Current` and `ReadNowAsync()`, completing after `Changes` emitted
- [ ] `RequestPenEditorAsync` opens the editor on `Current.Stored`; `MainWindowViewModelTests`: opening issues no editor read

### Task 2: pen rows and forms reconcile per field

- [ ] the editor subscribes to `Changes` while open and reconciles rows by id behind `_isReorderingRows`; `Revise(StoredPen)` keeps queued writes winning over the read
- [ ] the form follows revisions through `Follow`/`Shows`; a drafted field keeps its draft and its later write wins; the selection and the open form instance stay across joins and leaves
- [ ] tests: an external revision updates untouched fields and leaves a draft alone; a queued write still wins; a realised table keeps the selected form and its draft across a join and a leave

### Task 3: groups and memberships reconcile

- [ ] groups join and leave by id; a revision updates name, membership and `FoldedOnStart` in place; a queued rename survives a read that changed the stored name; the selection stays
- [ ] tests: rename survival, the fold field following when untouched, selection across a join and a leave

### Task 4: refresh through the sync

- [ ] refresh registers new pens, then awaits `PenCatalogueSync.ReadNowAsync()`; `ReadAfterEveryQueuedCallAsync` is deleted
- [ ] `IPenCatalogueEditor.ReadAsync` and `CatalogueNormaliser.ProjectStoredPens` are deleted; the editor tests move to the snapshot or the fake
- [ ] tests: registration lands the new pens in the editor; a refresh registering zero pens over the synchronous fake completes at once

### Task 5: documentation

- [ ] `overview.md` (The pen and group editor: the snapshot rule becomes the follow rule), `data-integration.md` (The catalogue while the viewer runs), `AGENTS.md` (the editor's read rule); the snapshot-rule text is replaced, not appended beside
- [ ] `dotnet test SemiPlot.slnx` green
