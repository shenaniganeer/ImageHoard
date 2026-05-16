# FR-ST-01 — Settings location, portable mode, and derived paths

**Status:** Locked for P0 implementation  
**Related:** FR-ST-01, FR-ST-02, FR-ST-03, FR-BR-07 (folder metrics cache), NFR-IN-01

## Modes

### A. Standard (default)

Per-user, machine-local data (survives reboot; not roamed by default):

| Artifact | Path |
|----------|------|
| Main settings JSON | `%LocalAppData%\ImageHoard\settings.json` |
| User input profiles / overrides | `%LocalAppData%\ImageHoard\profiles\` |
| Folder metrics cache (FR-BR-07) | `%LocalAppData%\ImageHoard\cache\folder-metrics\` |
| Thumbnail / decode cache (if enabled) | `%LocalAppData%\ImageHoard\cache\thumbnails\` |
| Operation log (FR-SR-09) | `%LocalAppData%\ImageHoard\logs\operations.jsonl` |

Use **`LocalAppData`** (not `Roaming`) for large caches to avoid slow sync profiles on domain-joined machines.

### B. Portable

When **portable mode** is on, **all** of the above live under a single directory next to the executable:

| Root | Path |
|------|------|
| Portable data root | `<ExeDir>\ImageHoardData\` |

Subfolders mirror standard mode: `settings.json`, `profiles\`, `cache\folder-metrics\`, `cache\thumbnails\`, `logs\operations.jsonl`.

**How portable is enabled (any one):**

1. First-launch wizard: user checks **“Portable mode (store settings next to app)”**, or  
2. Environment variable `IMAGEHOARD_PORTABLE=1` before first run, or  
3. Presence of marker file `<ExeDir>\ImageHoard.portable` (empty file is enough).

**Switching modes:** Show a blocking dialog: “Restart required to change storage mode”; export/import copies JSON + `profiles\` only (caches can be cleared).

## FR-ST-02 persistence keys

Minimum keys: recent roots, last window geometry, last archive target, **last input profile id**, portable flag. Session **`paths.lastActedFsObject`** (cold-boot browser tree anchor) and **`paths.browserTree.expandedFolderPaths`** (no scroll offsets) are persisted with browse state; see [`browser-navigation-wizard-tree-coordination.md`](../tech-design/browser-navigation-wizard-tree-coordination.md).

## FR-ST-03 Clear cache

**Settings → Storage → Clear caches** removes:

- `cache\folder-metrics\`
- `cache\thumbnails\` (if present)

Does **not** delete `settings.json`, `profiles\`, or `logs\` unless user opts into **“Clear logs”** separately.

## Acceptance

1. Cold install creates only the chosen root’s directories on first save.  
2. Folder metrics model can reference concrete paths without “TBD” (see [folder-aggregate-metrics-model.md](./folder-aggregate-metrics-model.md)).  
3. UNC paths in settings JSON are stored as typed strings; no credentials in JSON (use Windows credential manager / OS if ever needed—out of P0).
