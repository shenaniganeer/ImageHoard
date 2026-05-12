# P0 slideshow discovery — Algorithm choice (FR-SL-02, FR-SL-03)

**Status:** Locked for P0 MVP  
**Related:** PRD §5 (Algorithms A/B/C); `slideshow-algorithm-pick`

## Decision

**P0 implements Algorithm A — streaming random with lookahead (reservoir / shuffled buffer of discovered files).**

Algorithm B (periodic partial shuffle) remains an **optional optimization** or fallback if profiling shows better memory behavior on specific devices; **do not** ship B as default without A/B comparison on C-50k corpus.

Algorithm C (persistent index) is **P2** per PRD.

## Rationale

- Satisfies **FR-SL-02** without full-tree enumeration at start: enumerator runs in background; UI can start after **minimum pool size** is met.
- Bounded memory via configurable **pool / reservoir cap** (aligns with **NFR-PF-03**).
- Industry-familiar tradeoff: slight early bias vs fairness until pool fills—acceptable if disclosed (FR-SL-03).

## User-facing disclosure (FR-SL-03)

Ship in **Settings → Slideshow** (or help panel) under **“Random order fairness”**:

> **Random slideshow** picks images from a **running sample** of files discovered so far under your chosen folder. Until enough files have been discovered, order is **approximately random**; over time and across the whole session it evens out. **Reshuffle** starts a new random session. For perfectly uniform random over the entire tree before any image shows, use a future **index** option (when available).

**Configurable (defaults locked for P0):**

| Setting | Purpose | **Locked default** |
|---------|---------|-------------------|
| `Slideshow.MinPoolBeforeStart` | Minimum discovered **image** count before first slide | **24** images. If the folder tree yields fewer than 24 images total, start once the **enumerator has confirmed** that many or exhausted the tree. |
| `Slideshow.ReservoirMax` | Hard cap on pending random pool entries | **2000** image paths (metadata only). If exceeded, evict oldest **non-current** entries from the reservoir using LRU policy while preserving current Tree session position. |
| `Slideshow.Reshuffle` | User action only (FR-SL-04); clears session pool and seen set per product spec | (no default value—action) |

**Rationale for 24 / 2000:** balances early-order bias (FR-SL-03 disclosure) with **NFR-PF-01** startup feel on C-10k corpora; 2000 caps RAM on huge trees per **NFR-PF-03**.

## Engineering notes

- **Folder scope (FR-SL-06)** uses sibling list, **not** reservoir; see [slideshow-tree-vs-folder-scope.md](./slideshow-tree-vs-folder-scope.md).
- **PRNG:** use a single well-seeded generator for reservoir sampling; persist **only** Tree-session state when toggling Folder scope (FR-SL-07).

## Acceptance

1. Slideshow starts without pre-scanning entire tree (instrumentation: directory enumeration count vs images shown at t=0, t=5s).
2. Help/settings text includes the fairness paragraph above or equivalent.
3. Long-run memory stays under **NFR-PF-03** limits on C-50k.
