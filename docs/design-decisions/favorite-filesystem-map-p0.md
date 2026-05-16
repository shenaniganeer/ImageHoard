# Favorite filesystem map (P0)

**Status:** Implemented (ImageHoard.Core + WinUI host)  
**Related:** [folder-aggregate-metrics-model.md](./folder-aggregate-metrics-model.md) (FR-BR-06/07), [fr-st-01-settings-persistence.md](./fr-st-01-settings-persistence.md) (cache paths), [browser-tree-rewrite-architecture.md](./browser-tree-rewrite-architecture.md) (`FsMap` alignment), [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md) (legacy `TreeView` metrics merge)

## Purpose

Persist **per-directory subtree metrics** for **favorited index roots** (deduped nested favorites share one JSON file) so large sibling folder lists can **sort by aggregate size or image count** using **warm values** immediately after navigation, then converge as live scans complete. This complements the append-only **global** `folder-metrics.jsonl` cache by scoping durable rows to **favorite subtrees** in `cache/favorite-fs-maps/`.

**Browse2 (`FsMap` under `cache/browse2-fs-maps/`):** Favorites are **preload hints** plus dedupe-minimal **on-disk** map roots; any other browsed folder still runs the full Browse2 tree + image pipeline with a **transient in-memory** workspace (no second-class UI — restart rescans until the path is favorited).

## Index roots (no duplicate maps)

`FavoriteIndexRoots.ComputeMinimalIndexRoots` drops any favorite that is a **strict subdirectory** of another favorite (directory-boundary prefix). One map file per remaining root (`favorite-fs-map-{sha256}.v1.json`).

## Trust and invalidation

- **Authoritative listing:** `ListDirectoryAsync` for the browsed folder is unchanged.
- **Map rows:** updated when a **full subtree** `FolderMetricsSnapshot` is trusted or freshly scanned; also persisted when serving a trusted row from global metrics cache.
- **Purge:** On delete/archive wizard success, undo restore, or folder node removal, affected **ancestor directory prefixes** are removed from favorite map files so stale aggregates are not reused until rescanned.
- **FR-ST-03:** `AppSettingsStore.ClearCaches` deletes favorite map files alongside `folder-metrics.jsonl`.

## UI integration

- Before populating browser roots, maps are **seeded** into `_folderAggregateBytesByPath` / `_folderImageFileCountByPath`; new `FolderTreeEntry` rows call `TrySeedFolderEntryFromFavoriteFilesystemMap` after the usual pending sizing state.
- **Background:** after deferred browser chrome startup, each index root is queued once for low-priority full-subtree metrics (reuses existing `StartFolderMetricsWorkAsync`), throttled two roots per dispatcher drain tick.
- **Viewport:** coalesced aggregate resort flush schedules `ScheduleBrowserTreeViewportAfterMutation(GetBrowserTreeViewportPinPathAfterBrowseCommit())` so the browsed folder row stays anchored after resort.

## Tests

`FavoriteIndexRootsTests`, `FavoriteFilesystemMapStoreTests` cover deduplication rules and JSON round-trip + purge.
