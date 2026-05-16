# Command registry — `commandId` beyond default profiles (FR-IN-01)

**Status:** Locked for P0 naming; bindings may be P1 where noted  
**Related:** FR-BR-02, FR-BR-04, FR-BR-05, FR-SL-04, FR-SL-05, FR-VW-01, FR-ST-03, FR-IN-01

This document extends the **minimum** command set in [input-default-profiles.md](./input-default-profiles.md). Each row states whether the command is **bindable** in P0, **chrome-only** (no default chord), or **P1**.

| `commandId` | Description | PRD | P0 binding |
|-------------|-------------|------|------------|
| `nav.nextImage` | Next image | FR-VW-04 | Yes (defaults shipped) |
| `nav.prevImage` | Previous image | FR-VW-04 | Yes |
| `nav.firstImage` | First image in folder list | FR-VW-04 | Yes (KeyboardOnly default `Home`) |
| `nav.lastImage` | Last image in folder list | FR-VW-04 | Yes (KeyboardOnly default `End`) |
| `nav.nextDirectory` | Next sibling folder under the parent of the folder containing the current image (or browse root when no image context); same sort as folder tree | FR-VW-04 | Yes (KeyboardOnly default `Control+Alt+PageDown`) |
| `nav.prevDirectory` | Previous sibling folder under the parent of the folder containing the current image (or browse root when no image context); same sort as folder tree | FR-VW-04 | Yes (KeyboardOnly default `Control+Alt+PageUp`) |
| `nav.cycleNavigationMode` | Cycle browse navigation mode (restrict next/prev/first/last to All / Keep only / Not Keep / Unflagged only / Delete only); **Browse → Navigation mode** shows options and accelerator | FR-VW-04 | Yes (KeyboardOnly default `Control+Shift+N`) |
| `sort.flagKeep` | Keep | FR-SR-01 | Yes |
| `sort.flagDelete` | Delete | FR-SR-01 | Yes |
| `sort.flagUnset` | Unset | FR-SR-01 | Yes |
| `sort.deleteArchiveWizard` | Delete/archive wizard (scoped to parent folder of current image): inverse-keep delete, delete-flagged-only, rename folder, move folder to archive, delete folder to Recycle Bin; **ContentDialog** confirms before delete/move commits; subtree **file count + size** when the folder has immediate subfolders (move + delete folder) | FR-SR-03/04/05 | Yes |
| `sort.commitBatchDelete` | *(legacy alias)* same handler as `sort.deleteArchiveWizard` | FR-SR-03/04 | Yes |
| `sort.moveToArchive` | *(legacy alias)* same handler as `sort.deleteArchiveWizard` | FR-SR-05 | Yes |
| `sort.clearAllFlags` | Clear all sort flags (in-memory session); **Sort → Clear all flags** menu (P0 chrome) | FR-SR-02 | Yes |
| `slideshow.start` | Resume or start tree slideshow (**ContentDialog** when a suspended session exists; otherwise start at current browse folder) | FR-SL-02 / FR-SL-07 | Yes (KeyboardOnly default `Control+Shift+S`) |
| `slideshow.switchToBrowseAtCurrentLocation` | Leave fullscreen slideshow UI, keep tree session for resume, open parent folder of current slide in browse | FR-SL-06 / FR-SL-07 | Yes (KeyboardOnly default `Tab`; MouseOnly wheel tilt left / `X3` when exposed) |
| `slideshow.siblingNextImage` | In slideshow: next image among siblings in the current file’s directory (does not advance tree slideshow history) | FR-SL-06 | Yes (MouseOnly default **Right**+wheel **Down**; KeyboardOnly `Control+Alt+ArrowRight`) |
| `slideshow.siblingPrevImage` | In slideshow: previous sibling image in that directory | FR-SL-06 | Yes (MouseOnly **Right**+wheel **Up**; KeyboardOnly `Control+Alt+ArrowLeft`) |
| `slideshow.deleteCurrent` | Delete current slideshow image: **ContentDialog** before delete; if the Recycle Bin is unavailable, permanent delete runs **without** the extra permanent-delete prompts used elsewhere for batch/tree delete. Requires **Settings → Library → Allow delete in slideshow**; bind under **Preferences → Hotkeys** | FR-SL-05 | **Off** by default (no shipped chord); user may assign a chord in Hotkeys |
| `browse.toggleSubfolderInclusion` | Toggle “include subfolders” for **list** | FR-BR-02 | View menu **P0**; bind P1 |
| `browse.openGoToPath` | Open “go to path” dialog | FR-BR-04 | Chrome P0; bind P1 |
| `browse.openBookmarks` | Open bookmarks / favorites manager | FR-BR-04 | File → Favorites **P0**; bind P1 |
| `browse.addBookmark` | Bookmark current root/folder | FR-BR-04 | Chrome P0; bind P1 |
| `browse.revealInExplorer` | Reveal current file in Explorer | FR-BR-05 | Chrome P0; bind P1 |
| `browse.findInTree` | Find in browser folder tree (partial name match, shallow or deep under browse root) | FR-BR-02 | Yes (KeyboardOnly default `Control+F`) |
| `browse2.refreshTree` | Browse2: re-list index root, current folder, and expanded paths from disk (merge into `FsMap`, emit diffs) | FR-BR-01 / NFR-PF-05 | Yes (KeyboardOnly default `F5`) |
| `browse2.toggleImagePaneSubtreeRecursion` | Browse2: toggle recursive image list (include subfolders in **image pane** only) | FR-BR-02 | **Off** by default (no shipped chord); see [browse2-image-pane-recursion-default.md](./browse2-image-pane-recursion-default.md) |
| `browse.treeNext` | Browser tree: move selection to next visible row (folder or image) while tree has focus | FR-BR-02 | Yes (KeyboardOnly default `ArrowDown`; overlaps `nav.nextImage`, disambiguated by focus) |
| `browse.treePrevious` | Browser tree: move selection to previous visible row | FR-BR-02 | Yes (KeyboardOnly default `ArrowUp`; overlaps `nav.prevImage`) |
| `browse.treeExpand` | Browser tree: expand current folder row (or parent folder when an image row is selected) | FR-BR-02 | Yes (KeyboardOnly default `ArrowRight`; overlaps `nav.nextImage`) |
| `browse.treeCollapse` | Browser tree: collapse current folder row (or parent folder when an image row is selected) | FR-BR-02 | Yes (KeyboardOnly default `ArrowLeft`; overlaps `nav.prevImage`) |
| `browse.treeDelete` | Browser tree: delete the selected folder/image row to Recycle Bin; **ContentDialog** confirms; uses `WizardExecuteImageRecycleOrPermanentBatchAsync` / `ExecuteSendFolderToRecycleBinAfterConfirmAsync` with paths deduped under browse root | FR-BR-02 / FR-SR-08 | Yes (KeyboardOnly default `Delete` while tree has focus) |
| `view.cycleFitMode` | Cycle shrink only / shrink & stretch / 1:1 (or next mode) | FR-VW-01 | Chrome P0; bind **recommended P0** `KeyV` or toolbar |
| `view.panPreview` | Pan primary preview (modifier + drag) when image exceeds pane | FR-VW-01 | Yes (MouseOnly default `Shift` + primary click drag; scrollbars also) |
| `view.zoomIn` | Zoom in (primary preview + fullscreen); **10%** steps from current scale | FR-VW-01 | Yes (KeyboardOnly `Control+Equal` / `Control+NumpadAdd`; MouseOnly `Control`+wheel **Up**) |
| `view.zoomOut` | Zoom out | FR-VW-01 | Yes (KeyboardOnly `Control+Minus` / `Control+NumpadSubtract`; MouseOnly `Control`+wheel **Down**) |
| `view.zoomResetFit` | Reset preview zoom to default fit for current **Image fit** mode | FR-VW-01 | Yes (KeyboardOnly default `Control+Shift+Equal`) |
| `view.zoomActualPixels` | Set preview zoom to **original decoded resolution** (1:1 DIPs) | FR-VW-01 | Yes (KeyboardOnly default `Control+Shift+Minus`) |
| `view.clearSelection` | Clear browser image selection and blank preview (no-op in fullscreen for dispatch; `Escape` exits fullscreen instead) | FR-VW-01 | Yes (KeyboardOnly default `Escape`; no shipped MouseOnly keyboard chord) |
| `settings.open` | Open **Preferences** window (Options → Preferences…) | FR-ST-01 | Yes (KeyboardOnly default `Control+P`; Options → Preferences remains P0 chrome) |
| `settings.clearCaches` | FR-ST-03 clear folder metrics + thumbnails | FR-ST-03 | Chrome P0; bind P1 |

**Keyboard `Escape` and FR-IN-05:** Shipped **KeyboardOnly** binds `Escape` to `view.clearSelection`, not `ui.escape`, so the merged built-in profile does not register the same keyboard chord on two commands. `ui.escape` keeps the **MouseOnly** left+right chord; merged defaults still include that chord under `ui.escape`. In windowed mode, `ui.escape` also clears selection and preview when triggered by that chord (same effect as `view.clearSelection` for keyboard `Escape`).

## P0 UX rule

**Chrome-only** commands must be reachable within **≤3 clicks** from the main window without rebinding (menus or overflow menu acceptable).

## Default chord suggestions (non-normative until added to JSON)

When promoting to shipped profiles:

- `slideshow.start`: `Control+Shift+S` (KeyboardOnly); shipped in `keyboard-only.v1.json`.  
- `slideshow.switchToBrowseAtCurrentLocation`: `Tab` (KeyboardOnly); shipped.  
- `slideshow.siblingNextImage` / `slideshow.siblingPrevImage`: shipped (see `input-default-profiles.md`).  
- `nav.cycleNavigationMode`: `Control+Shift+N` (KeyboardOnly); shipped in `keyboard-only.v1.json`.  
- `view.cycleFitMode`: `KeyV` (KeyboardOnly).  
- `view.panPreview`: `Shift` + primary click drag on preview (MouseOnly merged profile; rebinding in Preferences).  
- `browse.revealInExplorer`: `Control+Shift+E`.
- `browse.findInTree`: `Control+F` (KeyboardOnly); shipped in `keyboard-only.v1.json`.  
- `view.zoomIn` / `view.zoomOut` / `view.zoomResetFit` / `view.zoomActualPixels`: shipped in `keyboard-only.v1.json` and `mouse-only.v1.json` (Ctrl+wheel).
- `settings.open`: `Control+P` (KeyboardOnly); shipped in `keyboard-only.v1.json`.

## Acceptance

1. Settings UI lists **all** `commandId` rows above for rebinding where `P0 binding` ≠ “Chrome only”.  
2. Conflict detection (FR-IN-05) runs across the full registry, not only the minimum set.
