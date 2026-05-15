# P0 slideshow discovery — Algorithm choice (FR-SL-02, FR-SL-03)

**Status:** Locked for P0 MVP  
**Related:** PRD §5 (Algorithms A/B/C); `slideshow-algorithm-pick`

## Decision

**P0 implements Algorithm A — streaming discovery with uniform random over the full discovered set.**

Background enumeration walks the tree (no full prescan before the first slide). Every **new** tree “Next” draws **uniformly at random** from **all image paths discovered so far** in that session (including while discovery is still running). Paths are stored in an append-only **discovered path store**: up to **`DiscoveredPathsInMemoryMax`** paths stay in RAM; additional paths **spill** to a temporary length-prefixed UTF-8 file with per-record byte offsets so random access stays O(1) per pick without holding every path string in RAM.

**Display history:** viewed slides are kept in a linear list with a cursor. **Previous** moves back along that list; **Next** either **redoes** forward if the user had gone back, or appends a **new** uniform random slide. If the draw would be the same path as the current slide (e.g. only one path has been discovered), **Next** does **not** append a duplicate history entry — the overlay stays at 1/1. The on-screen overlay shows **history position** and **discovered path count** (see coordination doc).

Algorithm B (periodic partial shuffle) remains an **optional optimization** or fallback if profiling shows better behavior on specific devices; **do not** ship B as default without A/B comparison on C-50k corpus.

Algorithm C (persistent index) is **P2** per PRD.

## Rationale

- Satisfies **FR-SL-02** without full-tree enumeration at start: enumerator runs in background; UI can start after **minimum pool size** is met.
- **FR-SL-03 / fairness:** each new slide is actually uniform over the growing discovered set (not a capped LRU subset), while **NFR-PF-03** is met by RAM cap + disk spill instead of unbounded `List<string>`.
- **Child-folder order during discovery is shuffled** so enumeration is not locked to alphabetical depth-first order.

## User-facing disclosure (FR-SL-03)

Ship in **Settings → About random fairness…** (or help panel) under **“Random order fairness”**:

> **Random slideshow** walks your folder in the background (no full scan before the first slide). Each **Next** picks an image **uniformly at random** from every path **discovered so far** in this session. Discovery order among subfolders is **shuffled** so slides are not stuck in the first few directories. The app keeps **up to 50,000** discovered paths in memory; if there are more, older paths move to a **temporary on-disk list** so new files still enter the draw while RAM stays bounded. **Previous** steps back through slides you have actually seen; **Next** after that moves forward again, or picks a new random image when you are already at the latest slide. **Reshuffle** (if exposed) clears the session and starts discovery again. For a persistent on-disk index before any image shows, see the product roadmap (**Algorithm C**).

**Configurable (defaults locked for P0):**

| Setting | Purpose | **Locked default** |
|---------|---------|-------------------|
| `Slideshow.MinPoolBeforeStart` | Minimum discovered **image** count before first slide | **24** images. If the folder tree yields fewer than 24 images total, start once the **enumerator has confirmed** that many or exhausted the tree. |
| `Slideshow.DiscoveredPathsInMemoryMax` | Max paths held as `string` in RAM before spill | **50,000** (see `SlideshowAlgorithmDefaults.DiscoveredPathsInMemoryMax`). |
| `Slideshow.ReservoirMax` | **Legacy constant** (superseded by full store + spill); retained for compatibility in code | **2000** (unused for sampling) |
| `Slideshow.Reshuffle` | Programmatic / tests (`TreeSlideshowSession.Reshuffle`); not bound in shipped profiles — start a **new** slideshow from browse instead | (no default user chord) |

**Rationale for 24 / 50k:** **24** preserves **NFR-PF-01** startup feel; **50k** caps string metadata in RAM on huge trees while spill keeps correctness for uniform sampling.

## Engineering notes (implementation)

- **Enumeration for tree slideshow** calls [`RecursiveImageEnumerator.EnumerateAsync`](../../src/ImageHoard.Core/Services/RecursiveImageEnumerator.cs) with a session `Random` so **per-directory child order** (subfolders and immediate image files) is **shuffled** before DFS. Other callers (e.g. browse batch collection) use the overload **without** shuffle so listing order stays deterministic.
- **Discovered path store:** [`SlideshowDiscoveredPathStore`](../../src/ImageHoard.Core/Slideshow/SlideshowDiscoveredPathStore.cs) + [`TreeSlideshowSession`](../../src/ImageHoard.Core/Slideshow/TreeSlideshowSession.cs) append each enumerated path, update `DiscoveredImageCount`, and draw `Random.Next(count)` for each new forward step (excluding the current path when `count > 1` to avoid trivial immediate repeats).
- **Sibling navigation in slideshow** uses a separate ordered list for the current file’s directory, **not** the tree path store; see [slideshow-tree-vs-folder-scope.md](./slideshow-tree-vs-folder-scope.md).
- **PRNG:** use a single well-seeded generator for discovery shuffle and for slide picks; sibling overlay does not advance tree session state.

## Acceptance

1. Slideshow starts without pre-scanning entire tree (instrumentation: directory enumeration count vs images shown at t=0, t=5s).
2. Help/settings text includes the fairness paragraph above or equivalent.
3. Long-run memory stays under **NFR-PF-03** limits on C-50k (RAM cap + spill; no unbounded path string list).
