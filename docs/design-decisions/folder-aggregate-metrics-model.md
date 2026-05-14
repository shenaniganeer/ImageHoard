# Folder aggregate metrics — cached model (FR-BR-06, FR-BR-07, NFR-PF-05)

**Status:** Engineering data model for P0  
**Related:** FR-BR-06, FR-BR-07, FR-ST-03; `folder-aggregate-metrics`

## Goals

- Support sorting **sibling directories** at one tree level by: **name**, **folder mtime**, **aggregate byte size**, **total file count**, **image file count** (allowlist extensions).
- **Never block** UI indefinitely: incremental scan with **cancel**, **progress**, optional **persisted cache** (FR-BR-07).

## Extension allowlist (image file count)

Default extensions (lowercase): `.jpg`, `.jpeg`, `.png`, `.gif`, `.webp`, `.bmp`, `.tif`, `.tiff`, `.heic`, `.heif`, `.jfif`, `.jxl`  
(Configurable in settings JSON.)

## Record: `FolderMetricsSnapshot`

Per **directory path** (normalized long path string):

| Field | Type | Notes |
|-------|------|-------|
| `path` | string | Absolute path key |
| `scanDepth` | enum | `ImmediateChildren` \| `FullSubtree` \| `MaxDepthN` |
| `computedAtUtc` | datetime | When scan finished or partial |
| `folderMtimeUtc` | datetime? | From `File.GetLastWriteTimeUtc` on directory |
| `aggregateSizeBytes` | int64 | Sum of **file** lengths under scope |
| `totalFileCount` | int32 | All files under scope |
| `imageFileCount` | int32 | Files matching allowlist |
| `status` | enum | `Complete` \| `Partial` \| `Stale` |
| `error` | string? | Last failure message |

**Tie-break for stable sort:** when primary key equal, sort by `path` ordinal (FR-BR-03 stable sort spirit).

## Cache: `FolderMetricsCache` (FR-BR-07)

- **Storage:** SQLite or append-only JSONL under the folder-metrics directory defined in [fr-st-01-settings-persistence.md](./fr-st-01-settings-persistence.md) (`cache\folder-metrics\` beneath the active data root—`%LocalAppData%\ImageHoard\` or portable `ImageHoardData\`).
- **Invalidation:**
  - **Coarse:** on app start, optionally mark all `Stale` if global TTL exceeded (e.g. 24 h).
  - **Fine:** when user navigates into folder, compare `folderMtimeUtc` with FS; if directory mtime newer → recompute that subtree root lazily.
  - **Manual:** FR-ST-03 “Clear cache” wipes this store + thumbnail index.

## Scan algorithm (worker)

1. **BFS or stack** bounded by `scanDepth`; yield **batches** of N directories (e.g. 500) to UI thread for merge sort labels.
2. For each folder row at **current parent level**, subtree stats computed by **single DFS** from that child when user selects “expensive” sort—or precompute top-K visible rows first (engineering tradeoff).
3. **Cancel token** propagates; partial snapshots get `Partial` + last good counts.

## UI contract (NFR-PF-05)

- Sort mode dropdown triggers job; show **spinner + %** or “Computing… **1234** / **?** folders**.
- Switching away cancels job; no crash.

## UI merge path (WinUI host)

When metrics snapshots are merged on the UI thread, updating **`TreeViewNode.HasUnrealizedChildren`** must not require a full-tree lookup each time. The app maintains **`_folderTreeNodeByPath`** next to **`_folderTreeEntryByPath`**; anyone who adds, removes, or rekeys folder rows must keep that map in sync. See [browser-folder-tree-path-to-node-index.md](./browser-folder-tree-path-to-node-index.md).

## Tests

- Fixture: 3-level tree with known sizes → all five sort modes match golden totals.
- Cancel mid-scan → no corruption; cache `Partial` or absent.
