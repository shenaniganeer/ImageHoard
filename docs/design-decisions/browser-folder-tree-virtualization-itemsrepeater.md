# Browser folder tree: virtualized flat list (ItemsRepeater)

**Status:** Accepted — **Browser V2** implements this direction (`ImageHoard.App.BrowserV2` / `FolderTreeView`); the shipped browser column is Browse2-only.  
**Traceability:** **FR-BR-01**–**FR-BR-07** (browser tree + sorts + cache), **NFR-PF-05** (responsive UI under aggregate sorts), **NFR-PF-06** (degraded I/O must not hard-freeze the tree host)  
**Related:** [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md); [browser-tree-viewport-anchor-persistence.md](./browser-tree-viewport-anchor-persistence.md); [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md) (**superseded** for new code — legacy `TreeView` only); [folder-aggregate-metrics-model.md](./folder-aggregate-metrics-model.md); [architecture-bootstrap.md](../tech-design/architecture-bootstrap.md)

## Problem

WinUI `TreeView` does not virtualize like `ListView` + `VirtualizingStackPanel`. Large sibling lists plus sort/metrics updates drive costly WinRT `IList` mutations (`ResortFolderSiblingBlock`, header sync) and tree walks (`FindFolderTreeNodeByPath` / depth-first enumeration). Evidence: Speedscope / UI-thread profiles (see repo debugging doc).

## Accepted strategy (locked)

**Presenter:** `Microsoft.UI.Xaml.Controls.ItemsRepeater` bound to an `ObservableCollection` of **flat line** view models (Browser V2: **folder rows only** in the tree; images live in the sibling **`ImagePaneView`** — see [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md)).

**Model:** A single **visible hierarchy projection**: each line carries depth, path, expand/collapse state, `HasChildren` / `HasUnrealizedChildren` equivalents, and sort/metrics fields. Expand/collapse **splices** the flat list only (insert/remove logical lines for revealed subtrees) so the repeater recycles containers for viewport rows.

**Sort / metrics:** Sibling ordering is applied on the **model** segment with minimal collection churn; avoid O(n) per-element `RemoveAt` storms on WinRT lists where a bulk replace or diff is cheaper.

**Governance:** Browser V2 tree code must match this ADR. If `ItemsRepeater` proves a blocker for a specific interaction, **fallback** allowed: `ListView` + `VirtualizingStackPanel` with the **same** line view model — document the switch here and in bootstrap; do not pivot to third-party trees without a new maintainer decision. (Folders-only tree + image pane is an intentional UX split, not an unapproved pivot.)

## Alternatives considered (not chosen for MVP direction)

| Alternative | Why not now |
|-------------|-------------|
| Community virtualizing tree control | Extra dependency, integration risk, keyboard/a11y parity unknown |
| Single mixed `TreeView` (folders + images) | Retained only in the **legacy** host; Browser V2 uses coordinated **two-pane** folder tree + image list |
| Capped `TreeView` children (hide depth) | Does not fix aggregate-sort + metrics hot path on expanded large folders |

## UX / behavior parity scope (checklist)

Migration off `TreeViewNode` must preserve, at minimum:

- Selection and `SelectionChanged` semantics (or equivalent focus/selection model)
- Expand/collapse + lazy child load (`HasUnrealizedChildren` story becomes model-driven)
- Keyboard navigation and context menu targets
- Rename, favorites, slideshow-from-folder, browse sequential nav (`BrowseSequentialNavIndex` assumptions)
- Path overlays and deferred resort sorts for aggregate folder ordering
- `_folderTreeEntryByPath` invariants for **legacy** domain operations where still referenced; Browser V2 uses **`FolderTreeFlatModel`** path→row index (see [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md))

## Path index (supersedes `TreeViewNode` map for new code)

[browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md) is **superseded** for Browse2: use **`FolderTreeFlatModel`** / `RowIndexByPath` (or equivalent) keyed like the legacy entry map (`StringComparer.OrdinalIgnoreCase`). The legacy **`_folderTreeNodeByPath`** contract applied only to the retired mixed **`TreeView`** host.

## Impact on architecture bootstrap

[architecture-bootstrap.md](../tech-design/architecture-bootstrap.md) states WinUI 3 as the UI shell; it does not mandate `TreeView` for the browser. Add a **revision row** (see table below): browser **folder** rows may be hosted on **`ItemsRepeater`** (or documented `ListView` fallback) with virtualization; the **image** list uses a sibling virtualized pane; domain logic stays in **ImageHoard.Core**; line VMs live in **ImageHoard.App**.

| Date | Change |
|------|--------|
| 2026-05-13 | Browser tree virtualization direction: ItemsRepeater + flat projection (this ADR); `TreeView` remains until phased migration. |
| 2026-05-16 | Record Browser V2 implementation stance; folders-only tree + image pane; link rewrite + viewport ADRs; supersede path-to-node for new paths. |
| 2026-05-16 | Browse2 folder rows: six columns (indent, chevron, name, size, images, date) with View-menu column visibility; pinned headers collapsed in V2 — headers live in `BrowserV2Host`; draggable folder/image splitter persisted as `browse2PaneColumns`. |

## Implementation notes (Browser V2)

- **Scroll anchoring** is mandatory for tree mutations: capture top realized row + offset **before** applying a `FlatModelDelta`, restore **after** layout (see [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md)). This replaces `BrowserTreeViewportPump` **`LayoutUpdated`** retry loops for the new presenter.
- **Cold boot** restores **selection**, **expansion**, and **viewport anchor** rows (see [browser-tree-viewport-anchor-persistence.md](./browser-tree-viewport-anchor-persistence.md)); `lastActedFsObject` remains the separate action anchor.
- **Metrics / sorts:** `FsMap` + `FsDiffStream` feed the flat model so aggregate sorts (**FR-BR-06**, **NFR-PF-05**) do not require per-node `TreeView` walks.
