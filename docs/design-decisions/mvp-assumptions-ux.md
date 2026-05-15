# MVP assumptions — display, language, theme, NAS disconnect

**Status:** Locked for P0 unless PRD amended  
**Related:** PRD §12 historical items (dual monitor, i18n, themes, offline NAS)

## Viewer fit modes (FR-VW-01)

- **Shrink only**, **Shrink & stretch**, **1:1**, and **custom zoom** cycle via `view.cycleFitMode` (see [command-registry.md](./command-registry.md)).  
- **Persistence:** **per session** only in P0 (reset on app restart). **Per-user default** from Settings is **P1** unless schedule allows.

## Dual monitor

- **Fullscreen** targets the **monitor hosting the window** when the user presses fullscreen (FR-VW-01).  
- No **span-all-monitors** mode in P0.  
- Remember last monitor ID **best-effort** (fallback: primary if ID missing).

## Internationalization

- **English UI only** for P0.  
- File paths displayed as returned by the OS (Unicode).  
- Date/time format follows **user regional settings** for display strings.

## Themes

- Follow **Windows app mode** (light/dark) via WinUI `ThemeListener` / equivalent.  
- **High contrast** when OS high contrast active (FR-IN-06 stretch—at minimum do not ship colors that break HC black/white).

## Offline NAS / mid-session disconnect

- Any I/O failure shows **non-blocking** error surface (NFR-PF-06, NFR-RL-02): toast + **Retry** + **Go offline** (disable actions that need that path).  
- **Slideshow:** skip to next with message; **do not** crash.  
- **Sort batch:** pause with **Resume** after path returns; **Abort** leaves no “success” state for incomplete groups (NFR-RL-02).

## Acceptance

Manual script: disconnect Ethernet during slideshow and during move-to-archive preview—verify UI remains responsive and no silent success.
