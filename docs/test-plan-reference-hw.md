# Reference hardware and benchmark corpora (NFR-PF-*)

**Status:** Engineering baseline for performance acceptance (NFR-PF-01, NFR-PF-02, NFR-PF-03).  
**Related:** PRD §6, §11; `define-reference-hw`

## Reference machine A — “Local SSD baseline”

Use for **NFR-PF-01** (cold open ≤ 2 s on 10k images) and **NFR-PF-02** (next/previous ≤ 150 ms p95 for ≤32 MP JPEG).

| Attribute | Spec |
|-----------|------|
| OS | Windows 11 x64, current patch level |
| CPU | Mid-range laptop/desktop, ≥ 4 cores / 8 threads (e.g. Intel Core i5 / AMD Ryzen 5 class, ≤ 3 years old) |
| RAM | 16 GB |
| Primary storage | **NVMe or SATA SSD**, local NTFS volume |
| Display | 1080p; GPU integrated is acceptable |
| Network | Not used for local-only benchmarks |

**Image corpus assumptions:** JPEG decode path; files on **local SSD** (not RAM disk unless explicitly labeled).

## Reference machine B — “NAS lab”

Use for **NFR-PF-02** NAS branch (≤ 500 ms p95 under healthy SMB **or** non-blocking loading) and **NFR-PF-06** / **NFR-RL-02** scenarios.

| Attribute | Spec |
|-----------|------|
| Client | Same class as Reference A, wired **Ethernet** (1 Gbps preferred; document if Wi-Fi only) |
| Server | SMB-capable NAS or Windows file share on same LAN |
| Path style | **UNC** `\\server\share\...` and **mapped drive** `Z:\...` (both must appear in matrix; see [test-plan-nas-smb-unc.md](./test-plan-nas-smb-unc.md)) |
| Latency | “Healthy lab”: RTT to NAS &lt; 5 ms typical; document actual ping and SMB version |

## Benchmark corpus sizes

Synthetic or copied trees; **image extensions** per PRD MVP baseline (JPEG, PNG, WebP, etc.). Record generation script or source in CI notes.

| Corpus ID | Approx. image count | Folder shape | Purpose |
|-----------|---------------------|--------------|---------|
| C-1k | 1,000 | Flat or shallow (≤ 5 folders) | Smoke perf, regression |
| C-10k | 10,000 | Mixed depth (e.g. 100 folders × 100 files + noise) | **NFR-PF-01** cold open |
| C-50k | 50,000 | Deep nesting (≥ 8 levels), mixed sizes | **NFR-PF-03** enumeration / memory |
| C-gallery | 50–200 per folder | Many sibling folders (simulates staging) | Sort mode UX, folder metrics |

**File size mix (optional per corpus):** include ≥ 20% “large” files (e.g. 8–32 MP equivalent JPEG) for **NFR-PF-04** decode budget tests.

## Slideshow startup budget (FR-SL-02)

Align with **NFR-PF-01** spirit: first slideshow frame from chosen root should appear within **same order of magnitude** as cold open on comparable tree depth—**document measured baseline** on C-10k after implementation (target: user-perceived start without full-tree index).

## Automation

- **CI:** run C-1k on Reference A (or cloud Windows runner with SSD); **optional** skip C-50k / NAS on PR.
- **Nightly / manual:** C-10k, C-50k, NAS matrix per [test-plan-nas-smb-unc.md](./test-plan-nas-smb-unc.md)).

## Revision log

| Date | Change |
|------|--------|
| (initial) | Baseline from PRD NFR-PF-* |
