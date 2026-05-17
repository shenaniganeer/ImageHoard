---
name: Restore browser context menus
overview: Restore the same user-facing browser pane context menu actions that existed on main, on Browse2 surfaces (toolbar, folder tree, image list), implemented in whatever way fits the current Browser V2 layout and coordinator model.
todos:
  - id: menu-content-parity
    content: "Ensure all three surfaces expose the same seven actions as main (see body): toolbar path row, folder tree row, image list row."
  - id: replace-minimal-folder-flyout
    content: "Replace or subsume FolderTreeView’s small expand/collapse/explorer-only flyout so folder rows expose full main parity, not a second competing mini-menu."
  - id: image-list-coverage
    content: "Add image-row context access in Browse2 (split pane) equivalent to main’s file row in the combined tree."
  - id: command-behavior
    content: "Each action uses the correct target (folder under pointer vs file under pointer vs current browse folder on toolbar); align with coordinator selection and existing BrowseShell helpers."
  - id: verify-manual
    content: "Manual check of all seven actions from toolbar, folder row, and image row."
---

# Restore browser context menus (user outcomes, Browse2-aligned)

## Goal

Restore **user-facing functional parity** with **main** for browser pane context menus: the **same commands** and **same intent** (which folder or file they apply to). **How** the menu is built (flyout type, events, hosting control) is left to the implementer and should follow **how Browser V2 is already structured** (`BrowserV2Host`, `FolderTreeView`, `ImagePaneView`, `CrossPaneCoordinator`, [`MainWindow.BrowseShell.cs`](src/ImageHoard.App/MainWindow.BrowseShell.cs)), not a mechanical copy of legacy `TreeView` wiring.

## User-facing menu content (main parity)

Wherever a browser context menu appears for **toolbar**, **folder row**, or **image row**, users should get the same **set of actions** as on main:

1. **Refresh** — resync/refreshes listings for the relevant scope (current browse / selected folder / subtree as appropriate to Browse2).
2. **Rename** — rename the selected folder or the selected image file.
3. **Delete** — delete/recycle the current selection (folder or file), consistent with existing Browse2 delete rules.
4. **Reveal in Explorer** — open Explorer on the folder or file path.
5. **Start slideshow from folder** — start slideshow rooted at the appropriate folder (file row → parent folder).
6. **Open archive wizard in this folder** — open the wizard for the appropriate folder.
7. **Add folder to favorites** — add the appropriate folder to favorites.

**Toolbar** path strip: same seven actions for the **current browse folder** (main already gated on a valid folder path where applicable).

## Browse2-specific expectations

- **Folder tree** and **image list** are separate controls; together they replace the old single `TreeView` that held both folders and files. **Both** need access to the full command set where main had it on a row (folders in tree; files were tree rows then — now they are list rows).
- The current **folder-only mini menu** (expand / collapse / open in Explorer) in [`FolderTreeView.xaml`](src/ImageHoard.App/BrowserV2/FolderTreeView.xaml) is **not** sufficient for parity; it should be **replaced or merged** so users do not get a weaker second menu on folder rows.
- Implementation should **reuse existing** Browse2 selection, refresh, rename, delete, wizard, slideshow, and favorites flows already used from keyboard/menu elsewhere—only add the **entry points** from context menus where missing.

## Acceptance criteria

- Right-click **toolbar** path area: seven actions; no menu when there is no valid browse folder (match main behavior).
- Right-click **folder tree** row: seven actions; targets the folder that was clicked (after selection follows that row, if that is how Browse2 already behaves).
- Right-click **image list** row: seven actions; file-oriented commands target the file; folder-rooted commands (slideshow, archive wizard, favorites) target the **containing folder**, like main for an `ImageRow`.
- No lingering **duplicate** user-visible context UI on folder rows that hides main-parity actions (one clear menu per surface).

## Out of scope

- Prescribing `MenuFlyout` vs `ContextFlyout`, `ShowAt` vs declarative XAML, synthetic `TreeViewNode`s, or specific cross-control events—these are implementation details.
- Optional doc updates unless the team wants coordination doc churn; not required for functional parity.
