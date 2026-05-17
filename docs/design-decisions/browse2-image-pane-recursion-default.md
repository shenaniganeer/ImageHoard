# Browse2 image pane: immediate folder vs subtree recursion

**Status:** Locked for Browse2  
**Traceability:** **FR-BR-02** (image list for current folder), **FR-BR-01** (folder tree hierarchy separate from file list)

## Problem

The legacy **`includeSubfoldersInList`** setting defaults to **true** and drives the **mixed** `TreeView` (folders + files) where “include subfolders” applies to the **combined** list. Browse2 splits **folder tree** and **image list**; tying the image pane to the legacy flag caused a **flat recursive** image list by default and confused the folder pane (child folders belong in the tree, not as recursive file rows).

## Decision

1. **New persisted flag:** `ui.browse2ImagePaneIncludeSubfolders` (JSON), mirrored on `UiLayoutState.Browse2ImagePaneIncludeSubfolders`.
2. **Default `false`:** the image pane lists **only immediate** image-format files in the **selected folder** (same directory), unless the user opts in.
3. **Decoupled from** `ui.includeSubfoldersInList`: legacy browse continues to use the old flag; Browse2 does **not** import it for `ImagePaneController.IncludeSubfolders`.
4. **Chrome:** `BrowserV2Host` exposes a checkbox **“Include subfolders in image list”** bound to the new flag.
5. **Optional command:** `browse2.toggleImagePaneSubtreeRecursion` — no default chord in shipped KeyboardOnly profile; user may bind in Preferences → Hotkeys.

## Related

- [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md)  
- [command-registry.md](./command-registry.md) (`browse2.*` rows)

## Revision history

| Date | Change |
|------|--------|
| 2026-05-16 | Initial ADR: separate setting, default off, checkbox + optional toggle command. |
