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

Ship in **Settings → About random fairness…** (or help panel) under **“Random order fairness”**:

> **Random slideshow** picks each slide **at random** from the set of image paths **discovered so far** under your chosen folder (streaming background walk; **no full-tree prescan** before the first slide). While the tree is still being discovered, that set grows; when more than **2000** paths have been seen, the app keeps a **bounded pool** and **rotates out older pool entries (LRU)** so new discoveries can appear—still without loading every path into RAM at once. **Child-folder order during discovery is shuffled** so the walk is not locked to alphabetical depth-first order (which used to cluster early picks in the first few folders). Once discovery has finished, each “next” is uniform over **all** images under the root (same bounded pool applies if there are more than 2000 images). Starting a **new** slideshow (from the resume/start flow) creates a fresh session; the **`TreeSlideshowSession.Reshuffle`** API remains for tests or future chrome. For perfectly uniform random with a persistent on-disk index before any image shows, use a future **index** option (when available).

**Configurable (defaults locked for P0):**

| Setting | Purpose | **Locked default** |
|---------|---------|-------------------|
| `Slideshow.MinPoolBeforeStart` | Minimum discovered **image** count before first slide | **24** images. If the folder tree yields fewer than 24 images total, start once the **enumerator has confirmed** that many or exhausted the tree. |
| `Slideshow.ReservoirMax` | Hard cap on pending random pool entries | **2000** image paths (metadata only). If exceeded, evict oldest **non-current** entries from the reservoir using LRU policy while preserving current Tree session position. |
| `Slideshow.Reshuffle` | Programmatic / tests (`TreeSlideshowSession.Reshuffle`); not bound in shipped profiles — start a **new** slideshow from browse instead | (no default user chord) |

**Rationale for 24 / 2000:** balances early-order bias (FR-SL-03 disclosure) with **NFR-PF-01** startup feel on C-10k corpora; 2000 caps RAM on huge trees per **NFR-PF-03**.

## Engineering notes (implementation)

- **Enumeration for tree slideshow** calls [`RecursiveImageEnumerator.EnumerateAsync`](../../src/ImageHoard.Core/Services/RecursiveImageEnumerator.cs) with a session `Random` so **per-directory child order** (subfolders and immediate image files) is **shuffled** before DFS. Other callers (e.g. browse batch collection) use the overload **without** shuffle so listing order stays deterministic.
- **Pool:** [`TreeSlideshowSession`](../../src/ImageHoard.Core/Slideshow/TreeSlideshowSession.cs) moves paths from a concurrent inbound queue into a reservoir up to **`ReservoirMax`**. When the reservoir is full and new paths arrive, the implementation **evicts the LRU reservoir slot** (never the on-screen `_current`, which is not stored in the reservoir) and replaces it with the new path so inbound does not stall indefinitely.
- **Sibling navigation in slideshow** uses a separate ordered list for the current file’s directory, **not** the reservoir; see [slideshow-tree-vs-folder-scope.md](./slideshow-tree-vs-folder-scope.md).
- **PRNG:** use a single well-seeded generator for reservoir sampling; sibling overlay does not advance tree state.

## Acceptance

1. Slideshow starts without pre-scanning entire tree (instrumentation: directory enumeration count vs images shown at t=0, t=5s).
2. Help/settings text includes the fairness paragraph above or equivalent.
3. Long-run memory stays under **NFR-PF-03** limits on C-50k.
