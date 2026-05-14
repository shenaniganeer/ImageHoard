# Debugging runtime stack traces (agent playbook)

This document is for **human and autonomous agents** when diagnosing hangs, first-chance exception floods, or crashes from **debugger call stacks** (Visual Studio, VS Code + C#, WinDbg, crash dumps). It encodes patterns discovered while debugging ImageHoard startup issues.

## How to read a managed stack

1. **Start at the top frame you own** (your assembly, e.g. `ImageHoard.App.dll`, `ImageHoard.Core.dll`). Skip `[External Code]` / framework frames unless you need parameter semantics.
2. **Treat the immediate caller chain as the causal path** — the method at the top of *your* code is where your logic met the failure (or where an exception was first observed).
3. **Note `async` state machine frames** — names like `Method>d__N.MoveNext()` mean the failure line maps to a **source line in the original async method** (the debugger shows the mapped line; trust that line number).

## First-chance exceptions vs real failures

- The debug console may log **“Exception thrown …”** many times for **first-chance** exceptions that are **caught** and handled. High counts alone do not prove an unhandled crash.
- They **do** matter when they indicate **tight loops** (retry storms), **expensive handling**, or **UI-thread work** combined with exceptions.
- To break only when useful: use an **exception breakpoint** on the concrete type (e.g. `System.IO.IOException`) and inspect **message**, **HResult**, and **stack** once or twice — not every iteration if it spams.

## Visual Studio Code (not full Visual Studio)

- There is **no** top-level **Debug** menu like Visual Studio. Use **Run and Debug** (`Ctrl+Shift+D`), the **Run** menu (**Start Debugging** / `F5`), and the **BREAKPOINTS** section (**Add Exception Breakpoint…** or Command Palette **“Debug: Add Exception Breakpoint”**).
- **Parallel Stacks** is not available the same way; use the **CALL STACK** panel and switch the **active thread** from the dropdown to see whether the **UI thread** is blocked in I/O or waiting.
- This repo may only ship **attach** configurations in `.vscode/launch.json`; attach to `ImageHoard.App` after launch if needed.

## File I/O: `IOException` “being used by another process”

Typical meanings on Windows:

| Situation | What to check |
|-----------|----------------|
| **Same process, concurrent writers** | Two `async`/background tasks appending or opening the same file **without shared write serialization**. Search for `SemaphoreSlim`, `Task.Run`, parallel scans, or **permissive concurrency** (e.g. `(2,2)`) around the same path. |
| **Read vs write sharing** | `File.AppendAllTextAsync` / default opens may not allow a second writer. **Serialize appends** to a single JSONL/log file, or use one writer queue — do not rely on two concurrent appends. |
| **Another process** | Second app instance, indexer, AV, sync client — confirm with a single instance and Process Explorer handle list if needed. |

### ImageHoard case study (folder metrics cache)

- **Symptom:** Many first-chance `IOException` on startup; UI unresponsive for a long time.
- **Stack (example):** `FolderMetricsCacheStore.AppendSnapshotAsync` ← `MainWindow.StartFolderMetricsWorkAsync` (`MainWindow.BrowserPane.cs`).
- **Path:** `%LocalAppData%\ImageHoard\cache\folder-metrics.jsonl` (see `AppDataPaths.FolderMetricsCachePath`).
- **Cause:** `StartFolderMetricsWorkAsync` used `_folderMetricsConcurrency = new SemaphoreSlim(2, 2)`, so **two** metrics jobs could call `AppendSnapshotAsync` on the **same** file concurrently → exclusive file access conflict.
- **Mitigation in code:** `FolderMetricsCacheStore` uses a **static** `SemaphoreSlim(1, 1)` around **append** and **clear** so JSONL access is serialized process-wide for that store.

When extending metrics or other **append-only caches**, keep this pattern: **one gate per durable file** if multiple tasks can write it.

## Speedscope (dotnet-trace export)

Exports from **Microsoft.Diagnostics.Tracing.TraceEvent** (or similar) to Speedscope JSON are useful for **confirming call chains** and **relative** cost dominance on hot paths. Treat them as complementary to debugger stacks: same idea (deepest app frame as policy meeting cost), different aggregation rules.

### File shape

The export is typically **one large JSON object** with keys such as:

- **`shared.frames`**: array of `{ "name": "..." }` — frame index → display name (often fully qualified symbols).
- **`profiles`**: usually one profile object.
- **`$schema`**: URL for Speedscope’s JSON schema (handy for tooling).

### `profiles[0].type` and `events`

For these exports, **`profiles[0].type`** is commonly **`evented`** (not `sampled`).

**`profiles[0].events`** is an array of objects shaped like:

- **`"type"`**: **`"O"`** (open) or **`"C"`** (close) — bracket a stack frame’s active interval.
- **`"frame"`**: integer index into **`shared.frames`**.
- **`"at"`**: timestamp in **seconds** (relative to the start of the trace).

Correct pairing: maintain a stack on each **`O`** (push frame id and open time). On each **`C`**, pop the matching depth and attribute elapsed wall time for that visit to the frame’s **inclusive** total for that interval.

### Inclusive time, double-counting, and “exclusive”

Summed **`O`/`C` durations are inclusive**: every ancestor frame on the stack accumulates the **same** child interval, so **parent totals and child totals both include** that interval. Summing all frames’ inclusive totals **far exceeds** real wall-clock time.

**Implications:**

- **Do not** read the largest inclusive total as “this function alone consumed X seconds of wall time.”
- Use inclusive totals for **ranking** which symbols dominate and for **pairing** with the flame/time-order view in [speedscope.app](https://www.speedscope.app).
- For **exclusive** time (time in a frame excluding descendants), use Speedscope’s UI or a tool that computes exclusive intervals from the same event stream — not naive per-frame inclusive sums across the whole profile.

### Correlating with the WinUI UI thread

Under WinUI, work running on the **UI thread** often appears under framework frames such as **`Microsoft.InteractiveExperiences.Projection!...DispatcherQueueHandler.Do_Abi_Invoke`** — the dispatcher callback wrapper. Time **under** that handler is work that **competes directly** with input and layout; correlate with `HasThreadAccess` and known `DispatcherQueue` work when writing notes.

`DispatcherQueueHandler` names **where** stalled work ran, not **why** — follow down to the **deepest application frame** (e.g. your assembly) for the actionable cause.

### speedscope.app vs scripted summaries

| Use | When |
|-----|------|
| **[speedscope.app](https://www.speedscope.app)** | Interactive **flame**, **sandwich**, and **left-heavy** views; precise **exclusive** interpretation; drilling from a hot frame into children. |
| **Scripted top-N (e.g. repo `tools/` helper)** | Reproducible **inclusive** ranking over many traces, CI or chat attachments, filtering by substring (e.g. `ImageHoard`) — same **stack pairing** logic as above, documented so agents do not mis-parse `O`/`C` or treat summed inclusive totals as wall-clock budgets. |

When grepping or hand-parsing JSON, remember **`events`** is the time-ordered stream of opens/closes — not a flat histogram keyed by frame without stack discipline.

### ImageHoard / WinUI regression workflow (Speedscope tiers)

When comparing traces after browser-tree performance work, align notes with the phased mitigations:

| Tier | Typical hot frames (examples) | Code touchpoints |
|------|-------------------------------|------------------|
| **A** | `FindFolderTreeNodeByPath`, `EnumerateNodesDepthFirst` under folder resort / metrics | `_folderTreeNodeByPath` fast path + `FindFolderTreeNodeByPath` fallback in `CollectResortSiblingListsForFolderPath` (`MainWindow.BrowserPane.cs`) |
| **B** | `ResortFolderSiblingBlock`, `FlushCoalescedFolderResorts` | Coalesce deferred aggregate resorts by **sibling `IList<TreeViewNode>` identity** so one parent’s children list is not revisited once per metrics child |
| **C** | `ProcessPendingFolderMetricsSnapshotsBatched`, `ApplyFolderMetricsSnapshotCore` | Chunk size / max chunks per dispatcher callback; defer `RequestCoalescedFolderResortForTouchedFolderPaths` until the pending snapshot queue is **drained** for the current burst |

Re-record with `dotnet trace` (or your usual exporter), open in [speedscope.app](https://www.speedscope.app) for flame/sandwich views, and optionally run [`tools/speedscope-top.ps1`](../../tools/speedscope-top.ps1) for a reproducible inclusive top-N table across multiple `.speedscope.json` files.

### Repo helper: `tools/speedscope-top.ps1`

From the repo root (PowerShell):

```powershell
.\tools\speedscope-top.ps1 -Path .\trace.speedscope.json -Top 30 -Filter ImageHoard
```

- **`-Filter`**: case-insensitive substring on frame names (omit for all frames).
- Output is **inclusive** time from `O`/`C` pairing — use for ranking and diffing traces, not as exclusive wall-clock for a single function.

## What to capture for the next agent or PR

When reporting a runtime issue, paste or attach:

1. **Exception type and full message** (and HResult if shown).
2. **Call stack** from **your** top frame through 5–10 frames (including async mapped lines).
3. **Whether the faulting thread is the UI thread** (if responsiveness is the issue).
4. **Relevant paths** (redact usernames if needed but keep structure, e.g. `…\ImageHoard\cache\…`).

That is enough to reason about locking, wrong path, or network/UNC behavior without guessing.
