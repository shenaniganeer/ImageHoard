# Slideshow — Tree session, sibling overlay, and browse handoff (FR-SL-06, FR-SL-07)

**Status:** Supersedes the prior “tree vs folder scope toggle” model  
**Related:** [slideshow-algorithm-p0.md](./slideshow-algorithm-p0.md), [input-default-profiles.md](./input-default-profiles.md), [command-registry.md](./command-registry.md)

## UI indicator (FR-SL-06)

- **Persistent minimal badge** in fullscreen corner: **`TREE`** when `nav.nextImage` / `nav.prevImage` advance the **random tree** session, vs **`SIBLINGS`** when the separate sibling commands are active (ordered list for the parent directory of the on-screen file).

## Tree session (`nav.nextImage` / `nav.prevImage` in slideshow)

- Draw from **Algorithm A** reservoir (see [slideshow-algorithm-p0.md](./slideshow-algorithm-p0.md)) rooted at the **slideshow start folder**.
- Invoking tree next/prev **clears** any active sibling overlay so the next draw is unambiguously from the tree again.

## Sibling overlay (`slideshow.siblingNextImage` / `slideshow.siblingPrevImage`)

- Build / reuse an **ordered sibling list** for `Directory.GetParent(currentImagePath)` (same filter and default name order as sequential browse).
- **Does not** mutate tree reservoir, PRNG, or enumerator state; tree session keeps running in the background.
- Implemented as an overlay on `SlideshowCoordinator` (see `SiblingImageNavigator`).

## Switch to browse at current location (`slideshow.switchToBrowseAtCurrentLocation`)

- Exits fullscreen UI, sets **browse** context to the **parent folder** of the current slide and selects that image in the tree.
- **Does not** call `TreeSlideshowSession.StopEnumeration()` — the user can **`slideshow.start`** and choose **Resume** to continue the same tree session without a full rediscovery pass.

## Resume (FR-SL-07)

- Leaving fullscreen (F11 / Enter / Escape) **suspends** slideshow UI (`_slideshowUiActive = false`) but keeps the coordinator until the user clears selection in a way that discards the session or starts a **new** tree slideshow from the resume dialog.
- While a session is **suspended**, ordinary image row selection in the tree **does not** discard the coordinator (so keep/delete culling does not destroy the random-walk state).

## Edge case — single sibling

**Locked behavior:** With **one** image in the folder, sibling next/prev **wrap** to the same file (same as `SiblingImageNavigator`); optional toast may be shown by the app (implementation detail).

## Acceptance tests

1. Start tree slideshow → sibling next/prev → tree next → verify tree random state was not reset by sibling-only navigation (overlay clears on tree nav).
2. Start tree slideshow → **Switch to browse** → flag images → **Resume** from dialog → verify enumerator was not restarted (`DiscoveredImageCount` monotonic vs discard + fresh start).
3. Three siblings in one folder → sibling order matches Explorer name sort.
