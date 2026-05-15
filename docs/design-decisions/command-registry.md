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
| `slideshow.start` | Start tree slideshow from current browse folder | FR-SL-02 | Yes (KeyboardOnly default `Control+Shift+S`) |
| `slideshow.toggleScope` | Tree ↔ Folder scope | FR-SL-06 | Yes |
| `slideshow.reshuffle` | New random session | FR-SL-04 | **P1 default chord** (toolbar in P0) |
| `slideshow.skipUnsupported` | Skip unsupported file | FR-SL-05 | Chrome + optional bind P1 |
| `slideshow.deleteCurrent` | Delete current slide (dangerous) | FR-SL-05 | **Off** by default; if enabled, double-confirm; P1 default chord |
| `browse.toggleSubfolderInclusion` | Toggle “include subfolders” for **list** | FR-BR-02 | View menu **P0**; bind P1 |
| `browse.openGoToPath` | Open “go to path” dialog | FR-BR-04 | Chrome P0; bind P1 |
| `browse.openBookmarks` | Open bookmarks / favorites manager | FR-BR-04 | File → Favorites **P0**; bind P1 |
| `browse.addBookmark` | Bookmark current root/folder | FR-BR-04 | Chrome P0; bind P1 |
| `browse.revealInExplorer` | Reveal current file in Explorer | FR-BR-05 | Chrome P0; bind P1 |
| `browse.findInTree` | Find in browser folder tree (partial name match, shallow or deep under browse root) | FR-BR-02 | Yes (KeyboardOnly default `Control+F`) |
| `browse.treeNext` | Browser tree: move selection to next visible row (folder or image) while tree has focus | FR-BR-02 | Yes (KeyboardOnly default `ArrowDown`; overlaps `nav.nextImage`, disambiguated by focus) |
| `browse.treePrevious` | Browser tree: move selection to previous visible row | FR-BR-02 | Yes (KeyboardOnly default `ArrowUp`; overlaps `nav.prevImage`) |
| `browse.treeExpand` | Browser tree: expand current folder row (or parent folder when an image row is selected) | FR-BR-02 | Yes (KeyboardOnly default `ArrowRight`; overlaps `nav.nextImage`) |
| `browse.treeCollapse` | Browser tree: collapse current folder row (or parent folder when an image row is selected) | FR-BR-02 | Yes (KeyboardOnly default `ArrowLeft`; overlaps `nav.prevImage`) |
| `browse.treeDelete` | Browser tree: delete the selected folder/image row to Recycle Bin; **ContentDialog** confirms; uses `WizardExecuteImageRecycleOrPermanentBatchAsync` / `ExecuteSendFolderToRecycleBinAfterConfirmAsync` with paths deduped under browse root | FR-BR-02 / FR-SR-08 | Yes (KeyboardOnly default `Delete` while tree has focus) |
| `view.cycleFitMode` | Cycle fit / fill / 1:1 (or next mode) | FR-VW-01 | Chrome P0; bind **recommended P0** `KeyV` or toolbar |
| `view.panPreview` | Pan primary preview (modifier + drag) when image exceeds pane | FR-VW-01 | Yes (MouseOnly default `Shift` + primary click drag; scrollbars also) |
| `view.zoomIn` | Zoom in (when not slideshow-only) | FR-VW-01 | P1 |
| `view.zoomOut` | Zoom out | FR-VW-01 | P1 |
| `view.clearSelection` | Clear browser image selection and blank preview (no-op in fullscreen for dispatch; `Escape` exits fullscreen instead) | FR-VW-01 | Yes (KeyboardOnly default `Escape`; no shipped MouseOnly keyboard chord) |
| `settings.open` | Open **Preferences** window (Options → Preferences…) | FR-ST-01 | Yes (KeyboardOnly default `Control+P`; Options → Preferences remains P0 chrome) |
| `settings.clearCaches` | FR-ST-03 clear folder metrics + thumbnails | FR-ST-03 | Chrome P0; bind P1 |

**Keyboard `Escape` and FR-IN-05:** Shipped **KeyboardOnly** binds `Escape` to `view.clearSelection`, not `ui.escape`, so the merged built-in profile does not register the same keyboard chord on two commands. `ui.escape` keeps the **MouseOnly** left+right chord; merged defaults still include that chord under `ui.escape`. In windowed mode, `ui.escape` also clears selection and preview when triggered by that chord (same effect as `view.clearSelection` for keyboard `Escape`).

## P0 UX rule

**Chrome-only** commands must be reachable within **≤3 clicks** from the main window without rebinding (menus or overflow menu acceptable).

## Default chord suggestions (non-normative until added to JSON)

When promoting to shipped profiles:

- `slideshow.start`: `Control+Shift+S` (KeyboardOnly); shipped in `keyboard-only.v1.json`.  
- `slideshow.reshuffle`: `Control+Shift+R` (KeyboardOnly); mouse: **none** by default (avoid accidents).  
- `nav.cycleNavigationMode`: `Control+Shift+N` (KeyboardOnly); shipped in `keyboard-only.v1.json`.  
- `view.cycleFitMode`: `KeyV` (KeyboardOnly).  
- `view.panPreview`: `Shift` + primary click drag on preview (MouseOnly merged profile; rebinding in Preferences).  
- `browse.revealInExplorer`: `Control+Shift+E`.
- `browse.findInTree`: `Control+F` (KeyboardOnly); shipped in `keyboard-only.v1.json`.
- `settings.open`: `Control+P` (KeyboardOnly); shipped in `keyboard-only.v1.json`.

## Acceptance

1. Settings UI lists **all** `commandId` rows above for rebinding where `P0 binding` ≠ “Chrome only”.  
2. Conflict detection (FR-IN-05) runs across the full registry, not only the minimum set.
