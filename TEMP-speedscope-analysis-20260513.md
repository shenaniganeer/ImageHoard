# Temporary: Speedscope analysis — ImageHoard cold launch / UI thread

**Purpose:** Agentic reference from a single trace. Safe to delete after the performance work is merged or superseded.

**Source file:** `ImageHoard.App.exe_20260513_185422.speedscope.json` (repo root)  
**Exporter:** Microsoft.Diagnostics.Tracing.TraceEvent 3.1.23.0  
**Trace span (wall on UI thread profile):** ~77,045 ms (~77 s). Event timestamps are **milliseconds** from trace start.

## Method (for reproducibility)

- Parsed `shared.frames` and `profiles[].events` (type `O` / `C`, fields `frame`, `at`).
- **UI thread** identified as profile **`Thread (45836)`**: only profile with `ImageHoard.App!ImageHoard.App.Program.Main` and large `DispatcherQueueHandler.Do_Abi_Invoke` inclusive time matching trace span.
- **Inclusive** time per frame on that profile only (nested intervals; do not sum inclusive across frames for global % without care).
- **`UNMANAGED_CODE_TIME`:** CPU not attributed to managed frames (native/kernel/JIT/etc.); interpret alongside managed stacks.

## Findings (UI thread — dominant story)

Dispatcher work is dominated by:

`DispatcherQueueHandler.Do_Abi_Invoke` → **`MainWindow.ProcessPendingFolderMetricsSnapshotsBatched`** → **`ApplyFolderMetricsSnapshotCore`** → **`ApplyHasUnrealizedChildrenFromImmediateMetricsSnapshot`** → **`FindFolderTreeNodeByPath`** (`IList<TreeViewNode>`) → **`EnumerateNodesDepthFirst`** (state machines `d__152` / `d__153`).

Rough **inclusive** share of ~77 s on that thread (from prior aggregation):

| ~% of UI thread interval | Frame / area |
|--------------------------|--------------|
| ~85% | `ProcessPendingFolderMetricsSnapshotsBatched` |
| ~84% | `ApplyFolderMetricsSnapshotCore`, `ApplyHasUnrealizedChildrenFromImmediateMetricsSnapshot`, `FindFolderTreeNodeByPath` |
| ~78% / ~73% | `EnumerateNodesDepthFirst` (`d__152`, `d__153`) |
| ~64% | WinUI WinRT **TreeView** enumeration: `IIterable<TreeViewNode>.First` / `GetEnumerator` |
| ~31% | WinRT **`MarshalInspectable.FromAbi`**, **`ComWrappersSupport.CreateRcwForComObject`** |
| ~2% | `Path.GetFullPath` / `PathHelper.Normalize` |
| ~2% | `StartFolderMetricsWorkAsync` → `ProcessFolderMetricsDiscoveryQueueTick` → **`FolderMetricsCacheStore.TryGetLatestSnapshotForPathAsync`** |

**Conclusion:** Long **UI-thread** time is tied to **applying folder-metrics snapshots** while **walking the live `TreeView`** (expensive WinRT/COM per node), not a small isolated hotspot.

## Other threads

Additional profiles mostly show **`UNMANAGED_CODE_TIME`** / **`CPU_TIME`** with little managed `ImageHoard.*` stack—background/idle attribution, not the main UI freeze narrative.

## Follow-up directions (not implemented here)

- Move batch snapshot application off the UI thread, or chunk with `DispatcherQueue` yield.
- Reduce full-tree scans: path → node map, or apply deltas without `FindFolderTreeNodeByPath` over entire visual tree.
- Defer metrics application until after first frame / idle.

---

*Generated from chat analysis of the speedscope JSON; re-run `dotnet-trace` and compare new exports after changes.*
