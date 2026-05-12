# MVP thumbnail scope — PM decision (open question §12 PRD)

**Status:** Locked for P0 MVP  
**Related:** PRD §12 item 2, NFR-PF-*; `mvp-thumbnail-scope`

## Decision

**P0 ships list-first browsing with optional small thumbnail column — not a full thumbnail grid as the primary mode.**

### What ships in MVP

1. **Primary browser:** **virtualized text list** (name, date, size, optional tiny 32–48 px thumb) for current folder; must handle **10k+** rows without loading all decode pipelines at once.
2. **Fullscreen viewer:** primary surface for sort decisions and slideshow (FR-VW-01, FR-SR-02).
3. **Optional toggle:** “Large thumbnails” view **P1** unless engineering finishes early—if included in P0, it must be **lazy** and **off by default** for NAS paths.

### Rationale

- **NFR-PF-01 / NFR-PF-06:** NAS and huge folders punish eager thumbnail grids.
- **Persona A:** wheel-through list + fullscreen matches mouse-first flow without duplicating a heavy grid.
- **IRFanView comparison:** fast list + big preview is closer to power-user mental model than grid-only.

### Out of scope for MVP

- Masonry / infinite grid as default.
- Thumbnail cache warming entire tree on open.

### Acceptance

- Cold open on C-10k list remains within **NFR-PF-01** on Reference A.
- Toggling sort columns does not decode every image in folder.
