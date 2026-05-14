# Browser navigation, delete/archive wizard, and folder tree coordination

Agents and contributors: follow repository-wide guidance in [`AGENTS.md`](../../AGENTS.md) (source-of-truth order, PRD traceability, and closing `ImageHoard.App` before `dotnet build` / `dotnet test`).

This note describes how **image navigation**, **delete/move (wizard) processing**, and the **browser `TreeView`** interact. The subsystem spans several `MainWindow` partials; behavior is sensitive to **concurrent async** work.

## Maintenance rule

Any pull request that changes **keyboard/pointer dispatch for browse navigation**, **delete/archive wizard destructive flows**, **browser tree Find overlay**, **browser tree population, selection, or viewport scrolling**, or **preview enqueue/drain** in this area **must update this document** in the same PR (diagrams, flags, method names, or new entry points). Prefer linking to API symbols over pasting large code blocks.

## Input routing

| Path | Location | Notes |
|------|----------|-------|
| Text input vs profile chords | `MainWindow.TryDispatchInputCommand` (`MainWindow.xaml.cs`) | When focus is inside a standard text control (`IsInsideTextInput`), returns without matching merged keyboard bindings so **PreviewKeyDown** tunneling does not mark keys handled before the editor consumes them (inline rename in the browser `TreeView`, preferences fields, etc.). |
| Root-only browse/sort keys | `MainWindow.RootGrid_KeyDown` → `ShouldDeferAppKeyboardShortcuts` (`MainWindow.xaml.cs`) | Defers F2 rename, Enter→fullscreen from the tree, and `HandleSortKeyboardShortcuts` (K/D/U) while `IsInsideTextInput` **or** `_renameTargetNode != null` (covers the dispatcher tick before rename `TextBox` focus lands). |
| Command execution | `MainWindow.TryExecuteInputCommand` (`MainWindow.xaml.cs`) | Central switch on `commandId`. |
| Wizard overlay open | Same file | When `IsDeleteArchiveWizardOverlayOpen`, only a whitelist of commands runs; others return without acting. |
| Find-in-tree overlay open | Same file | When `IsBrowserFindOverlayOpen`, only `ui.escape` and `browse.findInTree` run; other `commandId` values return without acting. |
| Preferences overlay open | Same file | Whitelist includes `ui.escape`, `settings.open`, and `browse.findInTree` (opening Find hides Preferences first). |
| Browser pane mutation | `_browserPaneMutationDepth` / `IsBrowserPaneMutationInProgress` (`MainWindow.xaml.cs`) | Non-zero while the tree and preview context are being reconciled after wizard deletes/moves (and related paths). Browse-style `nav.*` commands return **handled** without navigating so input does not interleave with tree updates. |
| Content dialog from wizard | `_contentDialogModalDepth` (`MainWindow.xaml.cs`) | Used to suppress spurious tree collapse during modal focus churn (`FolderTree_OnCollapsed` in `MainWindow.BrowserPane.cs`). |
| Wheel / pointer chords | `TryDispatchPointerWheelBindings`, pointer handlers (`MainWindow.xaml.cs`) | Eventually call `TryExecuteInputCommand`. |

While the delete/archive wizard is visible, **Escape** and **Close** are limited when `IsBrowserPaneMutationInProgress` is true: the overlay root handler and `TryDismissTopModalForEscape` consume Escape without dismissing; Close is disabled (`DeleteArchiveWizardPanel.SetBrowserPaneMutationBlocking`).

## Find in tree overlay

| Concern | Location | Notes |
|---------|----------|-------|
| Command | `browse.findInTree` | Default chord **Control+F** in `defaults/input-profiles/keyboard-only.v1.json`; rebindable in Preferences (see `CommandCatalog`). |
| Chrome | Browse → Find in tree… | `MainWindow.xaml`; handler `BrowseFindInTree_Click` / `ShowBrowserFindOverlay` (`MainWindow.BrowserFind.cs`). |
| Mutual exclusion | `ShowBrowserFindOverlay`, `ShowOrActivatePreferences`, `ShowOrActivateDeleteArchiveWizard` | Opening Find hides Preferences and the delete/archive wizard; opening Preferences or the wizard hides Find. |
| Escape stack | `TryDismissTopModalForEscape` (`MainWindow.ModalOverlays.cs`) | Order: **Find** → Preferences → delete/archive wizard. |
| Host + panel | `BrowserFindPanel`, `RunBrowserFindSearchFromPanelAsync`, `BrowserFindStepMatchAsync`, `BrowserFindApplyMatchAsync` (`MainWindow.BrowserFind.cs`) | Shallow mode uses `EnumerateVisibleFolderTreeNodesPreorder` / `CollectVisibleImageNodesPreorder` (`MainWindow.BrowserPane.cs`); deep mode enumerates the filesystem under `_currentFolderPath` on a thread-pool task with cancellation. Navigation reuses `TryEnsureFolderPathMaterializedAsync` (folder matches pass `expandAndPopulateDestinationFolder: false` so only ancestors expand—the hit folder row stays collapsed), `SyncBrowseTreeSelection`, `TrySyncBrowseTreeSelectionToImagePathAsync`, and `ScheduleBrowserTreeViewportAfterMutation`. |
| Keyboard vs query box | `BrowserFindPanel` + `TryDispatchInputCommand` | Global merged chords are not dispatched while focus is inside a `TextBox` (`IsInsideTextInput`). The query box handles **Enter** (run search), **Right** at end of text and **Left** at start (next/previous match when matches exist). The overlay root handles **Left**/**Right** when focus is not in a text field so arrows still advance matches from buttons and options. **`IsDefault` on Search** is not used in XAML (WinUI markup compiler rejects `IsDefault` on this `UserControl` button); Enter still runs search from the query field. |

## Navigation pipeline (non-slideshow)

1. **`BrowseNavigateByStep`** (`MainWindow.BrowserPane.cs`) increments the nav counter and starts **`BrowseNavigateByStepAsync`** without awaiting (fire-and-forget).
2. **`BrowseNavigateByStepAsync`** resolves browse context from `_browseNavAnchorPath`, tree selection, `_currentImageFullPath`, `_currentFolderPath`, loads paths, computes index, then **`EnqueuePreviewNavigation`** and tree sync.
3. **`BrowseNavigateSiblingFolderFromInput`** → **`BrowseNavigateSiblingFolderAsync`** for `nav.nextDirectory` / `nav.prevDirectory`; uses **`_populateBrowserGeneration`** to drop stale results after repopulation.

When `IsBrowserPaneMutationInProgress` is true, **`BrowseNavigateByStep`** returns immediately (no async work); **`BrowseNavigateByStepAsync`** and **`BrowseNavigateSiblingFolderAsync`** no-op at entry.

## Preview drain

`MainWindow.PreviewNavigation.cs`: **`EnqueuePreviewNavigation`**, **`DrainPreviewNavigationCoreAsync`**, coalescing, **`SyncTreeSelectionToImagePath`**. Slideshow arrow keys use **`TryHandleSlideshowKeys`** (`MainWindow.P0.cs`), which returns false (no navigation) when `IsBrowserPaneMutationInProgress` is true.

## Destructive / reconciliation pipeline (wizard + browser)

| Step | API | Role |
|------|-----|------|
| User confirms delete/move | `WizardExecute*` in `MainWindow.DeleteArchiveWizard.cs` | File system and navigation side effects. **Move to archive** builds a case-insensitive union of (optional) inverse-keep paths and all **Delete**-flagged image paths in the folder, then runs the same recycle/permanent batch as other wizard deletes before `MergeMoveDirectoryAsync`. |
| Reconcile tree + preview | `RefreshBrowserPaneAfterWizardImageDeletesAsync`, `RefreshBrowserPaneAfterWizardUndoAsync` (`MainWindow.BrowserPane.cs`) | Syncs image rows with disk, updates aggregates, refocuses selection. Wrapped with **`EnterBrowserPaneMutation` / `LeaveBrowserPaneMutation`** (try/finally). |
| Pick next image or folder row | `CommitIncrementalFolderPreviewAndSelectionAsync` | Uses `BrowserTreeRefocusAfterWizardContext` when supplied. |
| Deferred resort flush (wizard) | `PrepareBrowserTreeViewportAfterWizardMutation` (`MainWindow.BrowserPane.cs`) | When folder list sort is **aggregate size** or **image file count**, `RequestCoalescedFolderResortForTouchedFolderPaths` debounces sibling resorts. Wizard `Refresh*` stops that timer and applies any **pending** coalesced sibling sorts **after** commit and **before** the awaited viewport so the tree order matches sort keys when pinning. No-op for name / date sorts (no deferred pending set). |
| Viewport | `ScheduleBrowserTreeViewportAfterMutation` (fire-and-forget) and **`ScheduleBrowserTreeViewportAfterMutationAsync`** (awaitable) | Pins the browsed folder row (or selection) into view after mutations; retries on `DispatcherQueue` when containers are not realized. |

Long-running wizard work **outside** `Refresh*` (e.g. per-file recycle loop, directory merge/move, recycle of whole folder) also runs under **`EnterBrowserPaneMutation`** so navigation stays blocked until refresh and **awaited** viewport scheduling complete.

## Races addressed

1. **Overlay dismissed while work continues** — Escape/Close could hide the wizard while `async` work still ran; `IsDeleteArchiveWizardOverlayOpen` no longer blocked `nav.*`. Mutation depth blocks browse commands and async browse entry points until teardown finishes.
2. **Overlapping `BrowseNavigateByStepAsync` instances** — Still possible when depth is zero; with depth, new navigations are suppressed during mutation.
3. **Viewport vs navigation** — `RefreshBrowserPaneAfterWizard*` awaits **`ScheduleBrowserTreeViewportAfterMutationAsync`** before clearing mutation depth so the tree scroll pass finishes (within the implemented completion contract) before navigation unblocks.
4. **Viewport vs deferred folder resort** — Without a pre-viewport flush, the awaited scroll could run while sibling folder rows were still in pre-resort order, then a timer-driven `FlushCoalescedFolderResorts` would reorder and rely on a second fire-and-forget viewport. Wizard `Refresh*` applies pending deferred resorts first so the awaited pass aligns the browsed folder after **final** order.

## Related design docs

- [`docs/design-decisions/browser-folder-tree-path-to-node-index.md`](../design-decisions/browser-folder-tree-path-to-node-index.md) — path ↔ node indexing.
- [`docs/design-decisions/browser-folder-tree-virtualization-itemsrepeater.md`](../design-decisions/browser-folder-tree-virtualization-itemsrepeater.md) — virtualization direction.
