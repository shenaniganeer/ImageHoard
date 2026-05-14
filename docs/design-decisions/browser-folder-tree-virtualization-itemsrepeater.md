# Browser folder tree: virtualized flat list (ItemsRepeater)

**Status:** Accepted direction (Phase 0 governance); **implementation through Phase 1+ pending**  
**Traceability:** NFR-PF-* (UI responsiveness), FR-BR-* (browser), folder metrics UX  
**Related:** [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md); [folder-aggregate-metrics-model.md](./folder-aggregate-metrics-model.md); [architecture-bootstrap.md](../tech-design/architecture-bootstrap.md)

## Problem

WinUI `TreeView` does not virtualize like `ListView` + `VirtualizingStackPanel`. Large sibling lists plus sort/metrics updates drive costly WinRT `IList` mutations (`ResortFolderSiblingBlock`, header sync) and tree walks (`FindFolderTreeNodeByPath` / depth-first enumeration). Evidence: Speedscope / UI-thread profiles (see repo debugging doc).

## Accepted strategy (lock before Phase 2 implementation)

**Presenter:** `Microsoft.UI.Xaml.Controls.ItemsRepeater` bound to an `ObservableCollection` of **flat line** view models (one row per visible line: folder, image, or header marker).

**Model:** A single **visible hierarchy projection**: each line carries depth, path, expand/collapse state, `HasChildren` / `HasUnrealizedChildren` equivalents, and sort/metrics fields. Expand/collapse **splices** the flat list only (insert/remove logical lines for revealed subtrees) so the repeater recycles containers for viewport rows.

**Sort / metrics:** Sibling ordering is applied on the **model** segment with minimal collection churn; avoid O(n) per-element `RemoveAt` storms on WinRT lists where a bulk replace or diff is cheaper.

**Governance:** Phase 2+ code must match this ADR. If Phase 1 spike proves an ItemsRepeater blocker, **fallback** allowed: `ListView` + `VirtualizingStackPanel` with the **same** line view model — document the switch here and in bootstrap; do not pivot to third-party trees or two-pane explorer without a new maintainer decision.

## Alternatives considered (not chosen for MVP direction)

| Alternative | Why not now |
|-------------|-------------|
| Community virtualizing tree control | Extra dependency, integration risk, keyboard/a11y parity unknown |
| Two-pane explorer (separate folder / file lists) | Large UX change; breaks single mixed tree assumptions in current host |
| Capped `TreeView` children (hide depth) | Does not fix aggregate-sort + metrics hot path on expanded large folders |

## UX / behavior parity scope (Phase 3 checklist)

Migration off `TreeViewNode` must preserve, at minimum:

- Selection and `SelectionChanged` semantics (or equivalent focus/selection model)
- Expand/collapse + lazy child load (`HasUnrealizedChildren` story becomes model-driven)
- Keyboard navigation and context menu targets
- Rename, favorites, slideshow-from-folder, browse sequential nav (`BrowseSequentialNavIndex` assumptions)
- Path overlays and deferred resort sorts for aggregate folder ordering
- `_folderTreeEntryByPath` invariants for domain operations; any shadow path→presenter maps documented below

Feature flag for staged rollout is optional but recommended if parity testing runs long.

## Impact on path-to-node index doc

Today [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md) describes `_folderTreeNodeByPath` as a **`TreeViewNode`** index for hot metrics merge.

**After migration:** the presenter is no longer `TreeViewNode`-centric. Either:

1. **Replace** the index with `path → line presenter` / row handle for the flat model, with the same maintenance contract (register on row create, unregister on remove/rename), **or**
2. **Retain** a shadow map only where legacy `TreeView` code paths remain during transition.

Phase 2+ should **update or supersede** the path-to-node ADR when the host swap lands so agents do not assume `TreeViewNode` for new code paths.

## Impact on architecture bootstrap

[architecture-bootstrap.md](../tech-design/architecture-bootstrap.md) states WinUI 3 as the UI shell; it does not mandate `TreeView` for the browser. Add a **revision row** (see table below): browser folder + file mixed list may be hosted on **ItemsRepeater** (or documented ListView fallback) with virtualization; domain logic stays in **ImageHoard.Core**; new line VMs live in **ImageHoard.App**.

| Date | Change |
|------|--------|
| 2026-05-13 | Browser tree virtualization direction: ItemsRepeater + flat projection (this ADR); `TreeView` remains until phased migration. |

## Draft vs lock

- **Phase 0–1:** This document may carry small edits (spike learnings, fallback wording).
- **Before Phase 2:** Strategy and fallback are **locked** so the flat projection schema does not churn mid-implementation.
