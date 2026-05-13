# NAS / SMB / UNC test matrix and degraded-mode UX (NFR-PF-06, NFR-RL-02)

**Status:** QA / engineering matrix  
**Related:** FR-BR-01, FR-SR-05, PRD §8.1; `nas-network-test-matrix`

## Path styles (all must pass smoke)

| ID | Path form | Example |
|----|-----------|---------|
| P1 | Mapped drive | `Z:\Photos\archive` |
| P2 | UNC long | `\\NAS\share\Photos\archive` |
| P3 | UNC with space | `\\NAS\share name\folder` |
| P4 | DFS (if available in lab) | Document pass/fail |

## Operations per matrix cell

For each of **P1–P3**, run:

1. **List** 1k images (virtualized list open).
2. **Fullscreen next/prev** 50 random files (measure latency vs [test-plan-reference-hw.md](./test-plan-reference-hw.md) NAS target).
3. **Sort mode:** flag 5, batch delete (Recycle Bin), verify.
4. **Move to archive** across share boundary if applicable (staging on `Z:`, archive on `\\NAS\...`). Exercise the delete/archive wizard: confirm **ContentDialog** for move; with a folder that has **subfolders**, confirm the dialog shows **recursive file count** and **total size** (or graceful fallback if enumeration fails on the share).
5. **Slideshow** Tree session start + Folder scope toggle.

## Failure injection (NFR-RL-02)

| Scenario | Expected UX |
|----------|-------------|
| Disconnect Ethernet mid-read | Non-blocking **toast + retry**; no silent blank state; input remains responsive |
| SMB session timeout | Same; append failure line to operation log per [operation-log-fr-sr-09.md](../design-decisions/operation-log-fr-sr-09.md) |
| Share offline at app start | Favorites pointing to dead path show **offline badge**; open path → modal with **Retry** / **Edit path** |
| Insufficient permissions | Clear **403-equivalent** message; no partial “success” for move batch |

## Timeouts / retries (NFR-PF-06)

| Operation | Suggested default (tune in impl) |
|-----------|----------------------------------|
| Metadata enumerate | 30 s per batch with cancel |
| File open for decode | 15 s; then skip with reason |
| Copy/move large folder | Progress + **pause**; retry 3× exponential backoff |

## Read-ahead (optional)

- When Tree slideshow shows image N, **prefetch** N+1 URL/path decode on background thread; **cancel** if user navigates away.

## CI

- Mark full matrix **manual / nightly**; CI uses local loopback SMB or skip with `[Trait("NAS")]` equivalent.
