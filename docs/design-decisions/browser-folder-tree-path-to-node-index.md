# Browser folder tree: path-to-`TreeViewNode` index (maintenance)

**Status:** **Superseded** for new browser code — Browser V2 uses **`FolderTreeFlatModel`** / path→flat-row indexing per [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md) and [browser-folder-tree-virtualization-itemsrepeater.md](./browser-folder-tree-virtualization-itemsrepeater.md). **Still authoritative** for the legacy WinUI **`TreeView`** host (`MainWindow.BrowserPane.cs` and related partials) until that host is removed.

**Related:** [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md); [folder-aggregate-metrics-model.md](./folder-aggregate-metrics-model.md) (FR-BR-06, FR-BR-07, NFR-PF-05); cold-boot UI work (folder metrics snapshot merge); [browser-folder-tree-virtualization-itemsrepeater.md](./browser-folder-tree-virtualization-itemsrepeater.md)

## Why this exists

Applying folder metrics to **`TreeViewNode.HasUnrealizedChildren`** used to resolve the node by walking the entire WinUI `TreeView` (`FindFolderTreeNodeByPath` / depth-first enumeration). That pattern dominated UI-thread time when many cached snapshots applied at once.

The host keeps a parallel index: **`Dictionary<string, TreeViewNode> _folderTreeNodeByPath`** in `MainWindow.BrowserPane.cs`, keyed like **`_folderTreeEntryByPath`** (`StringComparer.OrdinalIgnoreCase`, key = folder full path at registration time). Hot-path metrics merge uses **`TryGetValue`** instead of scanning the visual tree. The same index is used first when resolving a folder row’s **sibling WinRT list** for deferred aggregate resorts (`TryGetResortSiblingListForFolderPath` → `CollectResortSiblingListsForFolderPath`), with **`FindFolderTreeNodeByPath`** only as a fallback when the map misses.

## Maintenance contract

Treat **`_folderTreeNodeByPath`** as a **structural invariant** of the browser folder tree: for every folder row that exists in the tree and is indexed in **`_folderTreeEntryByPath`**, the same path should map to the **`TreeViewNode`** whose **`Content`** is that row’s **`FolderTreeEntry`**, until the row is removed or the path key changes.

### When changing browser tree code, you must

1. **Register with the host node** whenever a new folder **`TreeViewNode`** is created and its **`FolderTreeEntry`** is indexed: call **`RegisterFolderTreeIndex(entry, hostTreeViewNode)`** (do not rely on the optional `hostNode` default for new rows—omit the node only if you intentionally have no `TreeViewNode` for that path, which will skip `HasUnrealizedChildren` updates for that path).

2. **Remove** the path from **`_folderTreeNodeByPath`** whenever the corresponding folder node is removed from the tree or the path key is invalidated **before** a new key is registered—use **`UnregisterFolderTreeNodeIndex(path)`** (or **`ResetBrowserFolderMetricsState`** for a full browse reset), in lockstep with **`_folderTreeEntryByPath`** / aggregate maps for that path.

3. **Rename / subtree edits:** On in-place rename, old paths must be unregistered and every affected **`FolderTreeEntry`** re-registered with its **`TreeViewNode`** under the new path (same pattern as today for **`_folderTreeEntryByPath`**).

4. **Path key alignment:** Background metrics and cache keys use filesystem paths; the index uses **`entry.Path`** as stored when the row was created. If you introduce a new code path that enqueues metrics or merges snapshots, ensure the **`path`** string matches the dictionary key convention used at **`RegisterFolderTreeIndex`** time, or normalize in one place so **`TryGetValue`** does not silently miss.

### Review checklist

- Grep for **`_folderTreeEntryByPath`**, **`RegisterFolderTreeIndex`**, **`UnregisterFolderTreeNodeIndex`**, **`_folderTreeNodeByPath`**, and any new **remove/reparent/rename** of folder **`TreeViewNode`** instances; confirm the node map is updated on the same transitions as the entry map.

Failure modes if the index drifts: stale or missing **`HasUnrealizedChildren`** / expand probes for a folder row, with no compile-time error.
