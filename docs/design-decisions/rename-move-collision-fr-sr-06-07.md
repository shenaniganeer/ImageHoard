# Rename patterns and collision policy (FR-SR-06, FR-SR-07)

**Status:** Locked for P0  
**Related:** FR-SR-05 (move parent), FR-SR-06, FR-SR-07

## UX — dry run (FR-SR-06)

Before any batch move/rename:

1. User selects **rename template** (dropdown of presets + custom field).  
2. Show **preview table**: columns `Source`, `Destination`, `Status` (`OK`, `Collision`, `Invalid`).  
3. **Primary** blocked until user checks **“I have reviewed the preview”** if any row is `Collision` or `Invalid` unless user changes policy to auto-suffix.

## Pattern language (v1 tokens)

String template with `{tokens}`; literal braces escaped as `{{` and `}}`.

| Token | Meaning |
|-------|---------|
| `{OriginalName}` | Filename without extension |
| `{OriginalNameFull}` | Full filename with extension |
| `{Ext}` | Extension including dot, lowercased |
| `{ParentFolder}` | Immediate parent directory name |
| `{DateModified}` | `yyyy-MM-dd` from file mtime (UTC or local—match EXIF token TZ below) |
| `{DateTaken}` | EXIF DateTimeOriginal if present; else **empty string** (show warning in preview for that row) |
| `{Seq:N}` | Zero-padded sequence width **N** starting at **1** per batch order (stable sort by source path) |

**Example:** `{DateTaken}_{ParentFolder}_{OriginalName}_{Seq:4}{Ext}`

### Sanitization

Replace Windows-forbidden characters `\ / : * ? " < > |` with `_`; trim trailing dots/spaces; max single path segment **120** chars (configurable later); if truncation loses uniqueness, append hash suffix **before** extension.

## Collision policy (FR-SR-07)

User-selectable per batch:

| Policy | Behavior |
|--------|----------|
| **Abort** | Stop batch; no partial writes (transactional intent—report first collision) |
| **Skip** | Leave conflicting sources in place; continue others |
| **Auto-suffix** | Append ` (2)`, ` (3)`, … before extension |

Default for MVP: **Auto-suffix** for rename collisions; **Abort** for move if destination directory missing (user must create path).

## Cross-volume moves (FR-SR-05)

Rename preview runs on **final path**; execute as **copy + verify + delete** only if a single-step atomic move is not possible—surface progress (out of scope for exact progress UI here, but must not report success until destination flush succeeds).

## Acceptance

1. Fixture batch with two files mapping to same destination under **Abort** → no file moved.  
2. Same under **Auto-suffix** → both land with distinct names.
