# Browser tree — viewport anchor persistence (Browser V2)

**Status:** Accepted (schema + cold-boot algorithm; persists with `paths.browserTree`)  
**Traceability:** **FR-BR-01** (predictable tree viewport after navigation), **FR-BR-04** (return to saved browse context), **NFR-PF-05** (avoid disorienting full-tree relayout / scroll thrash when metrics arrive)

**Related:** [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md); [fr-st-01-settings-persistence.md](./fr-st-01-settings-persistence.md); [browser-navigation-wizard-tree-coordination.md](../tech-design/browser-navigation-wizard-tree-coordination.md) (legacy cold boot uses **`paths.lastActedFsObject`** only)

## Problem

Persisting only **`paths.lastActedFsObject`** (action anchor for find, wizard refocus, cold-boot **BringIntoView**) does not capture **passive** scroll position within a long sibling list. After restart, users can land on the correct selection but the **viewport** may reset, violating predictable browse chrome (product goal **G2** in the browser tree rewrite plan).

## Decision

Add a dedicated **viewport anchor** alongside expansion and selection in the browser tree settings DTO. Keep **`paths.lastActedFsObject`** as today: it remains the **semantic** anchor for “what did I last act on?” (Find hit, image step, wizard refocus), **not** the same as “what row was pinned at the top of the scroll viewer?”.

### Settings shape (DTO)

Extend **`BrowserTreeSettingsDto`** (names illustrative; match `AppSettingsModels.cs` when implemented):

```csharp
internal sealed class BrowserTreeSettingsDto
{
    public string? SnapshotBrowseRoot { get; set; }
    public List<string>? ExpandedFolderPaths { get; set; }
    public string? SelectedFolderPath { get; set; }
    public ViewportAnchorDto? ViewportAnchor { get; set; }
}

internal sealed class ViewportAnchorDto
{
    public string? AnchorFolderPath { get; set; }
    public double OffsetWithinRowPx { get; set; }
}
```

- **`AnchorFolderPath`** — folder path of the **anchor row** (the topmost realized row at capture time, or a stable equivalent chosen by the presenter).
- **`OffsetWithinRowPx`** — vertical offset in **physical pixels** (or DIPs per implementation) from the top of that row to the viewport’s visible top edge — allows sub-row precision when row heights vary slightly.

### Cold-boot restore algorithm

1. Load **`BrowserTreeStore`** (expansion, selection, anchor, snapshot root).
2. Build **`FolderTreeFlatModel`** from **`FsMap`** (no whole-tree `ListDirectory` — map is hot enough for structure).
3. Apply **`ExpansionState`** (splice expanded ranges).
4. Resolve **`ViewportAnchor.AnchorFolderPath`** to a **flat row index**.  
   - **If found:** `ChangeView` to that row’s vertical offset + **`OffsetWithinRowPx`**.  
   - **If not found** (folder removed): walk **ancestors** until the nearest path that exists in the model; use that row as anchor with offset **0** (or clamp offset if the row is shorter than stored offset).
5. If **`SelectedFolderPath`** differs from the anchor row’s path, **set selection** without scrolling away from the restored viewport (unless product rules require reveal-in-view for selection).
6. **Background:** `FsBackgroundScanner` runs after first paint; any `FsDiffStream` events are applied with **scroll anchoring** (see [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md)) so disk divergence does not jump the viewport.

### Capture rules (runtime)

Before each **batched** flat-model mutation, the **`FolderTreeView`** (or `TreeController`) captures the anchor using the **topmost realized** repeater item mapped to a folder path, plus offset within row. After mutation + layout, the same anchor is restored numerically.

### Interaction with `lastActedFsObject`

| Mechanism | Purpose |
|-----------|---------|
| **`ViewportAnchor`** | Passive scroll + row offset across sessions. |
| **`paths.lastActedFsObject`** | Active “last operated file/folder” for Find, wizard, and legacy cold-boot alignment in the **`TreeView`** host. |

Browser V2 may still **read** `lastActedFsObject` for wizard/find coordination even when viewport position is driven primarily by **`ViewportAnchor`**.

## Revision history

| Date | Change |
|------|--------|
| 2026-05-16 | Initial ADR: DTO shape, cold-boot steps, separation from `lastActedFsObject`. |
