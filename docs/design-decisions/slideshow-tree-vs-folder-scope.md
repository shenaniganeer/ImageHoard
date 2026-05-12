# Slideshow — Tree session vs Folder scope (FR-SL-06, FR-SL-07)

**Status:** Design locked for implementation  
**Related:** PRD §4.4, §5; `slideshow-scope-toggle`

## UI indicator (FR-SL-06)

- **Persistent minimal badge** in fullscreen corner: **`TREE`** vs **`FOLDER`**.
- Optional: subtle border tint difference (accessibility: not color-only—include text badge).

## Toggle command

- **Single bindable command:** `slideshow.toggleScope` (see [input-default-profiles.md](./input-default-profiles.md)).
- **Latency:** instant mode switch; no full tree rescan.

## Tree session behavior

- `nav.nextImage` / `nav.prevImage` draw from **Algorithm A** reservoir (see [slideshow-algorithm-p0.md](./slideshow-algorithm-p0.md)) rooted at **original slideshow root**.
- **State to persist while in Folder scope:** PRNG seed/state, reservoir contents (or pointer + caps), **seen set** for “no repeat until reshuffle” if product defines that, **enumerator bookmark** (last DFS stack).

## Folder scope behavior

- Build **ordered sibling list** for `Directory.GetParent(currentImagePath)`:
  - **Filter:** same image allowlist as main app.
  - **Order:** match **current folder list sort** for that directory; **default name ascending** (case-insensitive ordinal) if user never set sort for that folder.
- `next` / `prev` walk this list **only**; **do not** mutate Tree reservoir order.

## Resume Tree session (FR-SL-07)

- On toggle **back to TREE**: resume **exactly** from suspended Tree state — next Tree `next` should behave as if Folder scope never happened (unless user deleted/moved current file—then **skip missing** with toast).
- **Reshuffle (FR-SL-04)** remains explicit user action; leaving Folder scope does **not** reshuffle.

## Edge case — single sibling (PRD §12 Q9)

**Locked behavior:** In Folder scope with **one** image, `next` and `prev` **no-op** (wrap to self) and show **toast** once: “Only one image in this folder.” **Do not** auto-switch to Tree session.

## Acceptance tests

1. Start Tree slideshow → toggle Folder → browse siblings → toggle Tree → verify no reset of random sequence (statistical smoke: log 20 draws before/after).
2. Folder scope with 3 siblings → order matches Explorer name sort.
3. Delete current file on disk while in Folder scope → graceful skip + message.
