# Browser tree rewrite — three-layer architecture (Browse2 / Browser V2)

**Status:** Shipped — Browse2 (`BrowserV2Host`) is the only browser path; legacy mixed `TreeView` host removed.  
**Traceability:** **FR-BR-01** (lazy folder navigation), **FR-BR-02** (image list coordinated with tree), **FR-BR-03** (stable ordering), **FR-BR-04** (favorites / return to roots), **FR-BR-05** (unsupported types — unchanged host concerns), **FR-BR-06** / **FR-BR-07** (folder sorts + metrics cache fed from `FsMap` / scanners), **NFR-PF-05** (UI stays interactive during aggregate work), **NFR-PF-06** (network paths — throttled / cancellable scans, no hard-freeze)

**Related:** [browser-folder-tree-virtualization-itemsrepeater.md](./browser-folder-tree-virtualization-itemsrepeater.md); [browser-tree-viewport-anchor-persistence.md](./browser-tree-viewport-anchor-persistence.md); [favorite-filesystem-map-p0.md](./favorite-filesystem-map-p0.md) and [fs map disk cache plan](../../.cursor/plans/fs_map_disk_cache_a17d36c0.plan.md) (`FsMap` persistence); [browser-navigation-wizard-tree-coordination.md](../tech-design/browser-navigation-wizard-tree-coordination.md); superseded index guidance [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md) (legacy `TreeView` only)

## Problem

The shipped WinUI **`TreeView`** host (`MainWindow.BrowserPane.cs` and partials) mixes folder rows and image rows, relies on **`TreeViewNode`** walks and WinRT list resorts for large folders, and coordinates navigation, wizard mutations, Find, and cold boot through **`_populateBrowserGeneration`**, **`BrowserTreeViewportPump`** (`LayoutUpdated` retries), and **`EnterBrowserPaneMutation`** depth. That stack is hard to keep correct under concurrent async work and produces visible scroll jumps when the model mutates (see [browser-navigation-wizard-tree-coordination.md](../tech-design/browser-navigation-wizard-tree-coordination.md), “Races addressed”).

## Decision (target architecture)

Split responsibilities into **three layers** with a **single writer** to filesystem truth in Core and **one coalesced UI application per dispatcher tick** in the presenter.

### 1. Core — `ImageHoard.Core.Browse2` (presentation-agnostic)

| Component | Role |
|-----------|------|
| **`FsMap`** | Per **browse root** (`FsMapWorkspace.IndexRoot`): folder rows (path, parent, name, mtime, `hasChildren`, aggregates). **Favorites** (dedupe-minimal via `FavoriteIndexRoots`) get on-disk maps under `cache/browse2-fs-maps/` for preload and fast cold boot; any other browsed folder uses the same pipeline with a **transient** in-memory workspace until promoted to a favorite. Mtime-trusted invalidation and in-place resort per the fs-map plan. |
| **`FsBackgroundScanner`** | One full DFS pass per **favorite-backed** (persistent) workspace after first user-visible paint; throttled work units; writes `FsMap`; cancellable on exit. Transient browse-only workspaces are not scanned here (they rebuild via targeted refresh / navigation). **No** always-on `FileSystemWatcher`. |
| **`FsTargetedRefresher`** | Re-lists one folder and invalidates immediate children’s mtime trust. **Fast path for correct first paint:** `CrossPaneCoordinator.ColdBoot` always schedules `RefreshAsync(IndexRoot)`; user expand schedules refresh when the map row is unverified (`LastVerifiedAtUtc == null`) or claims subfolders but has **no** child rows yet. **No** always-on `FileSystemWatcher`. |
| **`FsChangeApplier`** | Single entry for app-driven mutations (rename, move, recycle, archive): patch `FsMap` from operation outcome, emit diffs. |
| **`FsDiffStream`** | Typed events for all subscribers (tree + image pane): e.g. `FolderAdded`, `FolderRemoved`, `FolderRenamed`, `FolderRefreshed`, `AggregatesUpdated`. Same stream drives both panes. |
| **`FolderTreeFlatModel`** | Visible-line projection: `IReadOnlyList<FolderRow>` with path, depth, expanded flag, `HasChildren`, aggregates. Expand/collapse **splices** row ranges (insert/remove); each tick emits at most one **`FlatModelDelta`** (insert/remove/update spans). |
| **`ExpansionState`** | `HashSet<string>` of expanded folder paths (capped; persisted with snapshot). **Index root** is **implicitly expanded** in `FolderTreeFlatModel` projection so immediate child folders appear even when the root is not stored in expansion state; collapsing the index root is a no-op. |
| **`SelectionState`** | Selected folder path (persisted). |
| **`ViewportAnchor`** | Top-of-viewport semantic anchor (persisted); see [browser-tree-viewport-anchor-persistence.md](./browser-tree-viewport-anchor-persistence.md). |
| **`BrowserTreeStore`** | Settings block for expansion, selection, anchor, browse root snapshot. |

### 2. App — `ImageHoard.App.BrowserV2`

| Component | Role |
|-----------|------|
| **`TreeController`** | Owns flat model + expansion + selection; subscribes to **`FsDiffStream`**; computes **`FlatModelDelta`**; posts updates to the UI thread. |
| **`ImagePaneController`** | Current folder path, sort, include subfolders, find scope; subscribes to **`FsDiffStream`** filtered by current-folder prefix. |
| **`CrossPaneCoordinator`** | Replaces glue in `MainWindow.BrowserPane.cs` for V2: image step → tree reveal, wizard reconciliation, slideshow **overlay list position** (`TryGetSlideshowOverlayListPosition` / browse mimic), `lastActedFsObject` capture where still needed. **Tree slideshow** does not change image-pane folder per slide (see coordination doc). |
| **`BrowserFindController`** | Query `FsMap` (folder names) and image pane (file names); folder hit → `TreeController.RevealAndSelect`; file hit → select in pane + reveal parent in tree. |

### 3. Presentation — WinUI 3

| Control | Role |
|---------|------|
| **`FolderTreeView`** | `ScrollViewer` + **`ItemsRepeater`** over flat folder rows only (folders-only tree; image rows live in **`ImagePaneView`**). Custom keyboard, focus, selection, context menu, accessibility. |
| **`ImagePaneView`** | Virtualized list of image rows (existing flat-list pattern is acceptable). |
| **`BrowserFindOverlay`** | Reuse `BrowserFindPanel` chrome; backing logic moves to **`BrowserFindController`**. |

## Scroll anchoring contract (G3)

For **every** model-mutating operation (expand, collapse, FS diff, wizard patch, sort change):

1. **Capture** viewport anchor: topmost **realized** row’s folder path + pixel offset within that row (see viewport ADR for persistence shape).
2. **Compute** `FlatModelDelta`.
3. **Apply** a single **`ItemsRepeater`** / collection update batch on the dispatcher tick.
4. **After** layout: resolve anchor row to an index; **`ScrollViewer.ChangeView`** to `(rowIndex × rowHeight) + offsetWithinRow`.

Mutations far above or below the viewport must produce **no visible scroll jump** for the user (replaces `LayoutUpdated`-retry viewport pump for tree scrolling).

## `FsDiffStream` (contract sketch)

Consumers treat the stream as **ordered**, **single-source** truth for UI convergence:

- **`FolderAdded`**, **`FolderRemoved`**, **`FolderRenamed`** — structural tree changes.
- **`FolderRefreshed`** — children listing / mtime reconciliation for one folder.
- **`AggregatesUpdated`** — byte/image counts changed for one or more paths (feeds **FR-BR-06** UI without blocking the main thread indefinitely — **NFR-PF-05**).

Exact payload types evolve in code; this ADR locks **semantics**: tree and image pane **never** independently rescan for the same user-visible refresh — they react to the same diff batching rules.

## Shipped layout

- Core under **`ImageHoard.Core.Browse2`**, presenters under **`ImageHoard.App.BrowserV2`**.
- **`MainWindow.xaml`** hosts **`BrowserV2Host`** only for the browser column.
- Wizard / find / slideshow coordinate through **`CrossPaneCoordinator`** and related controllers (see [browser-navigation-wizard-tree-coordination.md](../tech-design/browser-navigation-wizard-tree-coordination.md)).

## Live listing vs `FsMap` cache (first paint)

- **`IFileSystem.ListDirectoryAsync`** (via **`FsTargetedRefresher`**) is the **authoritative fast path** for “what folders exist as immediate children of this path right now.”
- **`FsMap`** is a **cache** for structure + aggregates: the UI must not wait for a cold or empty map before showing the index root row (**`FsMapWorkspace`** seeds a placeholder root on load) or before listing immediate children under the root (**cold boot + expand** both trigger targeted refresh).
- **`FsBackgroundScanner`** deepens **favorite-backed** maps opportunistically after first paint; the main thread never blocks on it for navigation.

## Revision history

| Date | Change |
|------|--------|
| 2026-05-16 | Initial ADR: three-layer model, `FsDiffStream`, scroll anchoring, PRD traceability. |
| 2026-05-16 | Hierarchy fix: implicit index-root expansion, cold-boot `RefreshAsync(IndexRoot)`, expand-triggered targeted refresh, live list as fast path vs map cache. |
| 2026-05-16 | Refinement: index root row hidden from flat projection; `FolderTreeFlatModel` sort kinds + aggregate resort on `AggregatesUpdated`; `FsTargetedRefresher.RefreshAggregatesForDirectChildrenAsync` + `EmitAggregatesUpdatedForPath` for Browse2 header sorts; folder-removed parent relist for chevron honesty. |
| 2026-05-16 | Shipped Browse2-only: removed `ui.useBrowserV2`; favorites = preload + on-disk `FsMap` roots (dedupe-minimal); other browse roots use transient in-memory workspaces; background scanner skips non-persistent workspaces. |
