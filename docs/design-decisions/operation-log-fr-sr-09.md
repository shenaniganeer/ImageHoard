# Operation log (FR-SR-09, NFR-RL-01)

**Status:** Locked — **enabled by default** for destructive batches (opt-out in Settings)  
**Related:** FR-SR-09, NFR-RL-01, [fr-st-01-settings-persistence.md](./fr-st-01-settings-persistence.md)

## Purpose

Append-only evidence of batch **delete**, **move to archive**, and optional **rename** outcomes for support and safety audits.

## Format

- **JSON Lines** (`.jsonl`), one object per line, UTF-8.  
- Path: see [fr-st-01-settings-persistence.md](./fr-st-01-settings-persistence.md) (`logs\operations.jsonl`).

## Record schema (minimum)

```json
{
  "schemaVersion": 1,
  "id": "uuid",
  "utc": "2026-05-11T12:00:00Z",
  "operation": "BatchDelete|BatchDeleteDeleteFlaggedOnly|MoveToArchive|RenameMove|DeleteFolderRecycle",
  "summary": { "ok": 42, "failed": 1, "skipped": 0 },
  "entries": [
    { "path": "\\\\server\\share\\a\\b.jpg", "result": "Ok|Failed|Skipped", "detail": "optional message" }
  ]
}
```

## Rotation

- When file exceeds **20 MB**, rotate: rename to `operations_YYYYMMDD_HHMMSS.jsonl.zip` optional P1; P0 may **truncate** head after copying last **100k** lines to `operations.archive.jsonl` — simplest P0: **stop append** and show “Log full—clear logs” until user clears.

## Privacy

- Log **full paths** as returned by the app (UNC visible). **Do not** log credentials.  
- If future features add HTTP, do not log auth headers (P4).

## Settings

- Toggle **“Log destructive operations”** (default **on**).  
- Button **“Clear logs”** removes `operations.jsonl` and archives.

## NAS test alignment

Failed operations append `result: "Failed"` with SMB error text where available (ties to [test-plan-nas-smb-unc.md](../test-plan-nas-smb-unc.md)).
