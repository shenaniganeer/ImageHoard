# Manual QA: browser tree viewport after image deletion

Use this checklist when validating fixes for the folder tree scrolling too far “up” after deleting multiple images (viewport offset roughly proportional to delete count).

## Preconditions

- Windows host build of `ImageHoard.App`.
- A browse folder with nested subfolders and enough images that the browser `TreeView` requires vertical scrolling.
- Delete/archive wizard path that removes several images in one operation (Recycle Bin or confirmed permanent delete, as applicable).

## Steps

1. Open a browse root that contains at least one subfolder with several images.
2. Expand ancestors so a **nested** folder row and its image rows are visible.
3. Scroll the browser tree so the **current context folder** is **not** at the top of the viewport (mid-list scroll).
4. Select an image in that folder (or rely on current preview context) and delete **N** images via the wizard (N ≥ 3).
5. After the refresh completes, observe the tree:
   - The browsed folder row should remain aligned with the prior pin behavior (browsed folder at top of viewport when that row exists, else selection), **without** jumping upward by approximately N row heights.
6. Repeat with **N = 1** and **N = 10** to confirm behavior is stable and not tied to a single count.

## Optional regression checks

- Undo restore after delete: tree should still pin reasonably (`RefreshBrowserPaneAfterWizardUndoAsync`).
- Folder list sort modes that trigger deferred resort (aggregate size / image count): delete images, wait for resort window to flush, confirm no double “snap” from stacked viewport passes.

## Related implementation

- `SynchronizeFolderImageRowsWithDiskAsync` in `MainWindow.BrowserPane.cs`: reconciles image rows with a single `children.Clear()` + rebuild when structure changes, avoiding per-row `RemoveAt` for deletions.
- `ScheduleBrowserTreeViewportAfterMutationAsync`: coalesces multiple schedule requests into one drain loop so refresh and folder-resort flush do not stack competing `BringIntoView` passes.
