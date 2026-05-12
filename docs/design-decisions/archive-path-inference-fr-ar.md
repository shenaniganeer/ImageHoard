# Archive path inference — UX and rule format (FR-AR-*)

**Status:** P1 specification (secondary goal G-07)  
**Related:** FR-AR-01 … FR-AR-04; `archive-inference-rules`

## UX (FR-AR-01, FR-AR-03)

When user initiates **move to archive** (FR-SR-05):

1. Show **“Suggested destinations”** panel under archive root with **ranked list** (max 5).
2. Each row: **relative path** (e.g. `2025\ForumName\`), **confidence** label (`High` / `Medium` / `Low`), **one-line reason** (e.g. “Matches 12 existing folders with prefix `2025-05-`”).
3. User **must** select a row **or** edit path text **or** browse tree — **no auto-move** without explicit confirm (FR-AR-03).
4. **Override:** typed path wins; suggestion engine only prefills.

## Confidence scoring (FR-AR-01 MVP heuristics)

**v1 (P1) — path + mtime only (FR-AR-02):**

| Signal | Weight (example) |
|--------|------------------|
| Same **parent folder name** as existing archive subtree | +40 |
| **Year** from source folder name matches existing year bucket `YYYY` | +30 |
| **Sibling count** under candidate parent similar to historical moves | +10 |
| **Levenshtein** distance from last N user-chosen destinations | +20 |

Normalize to 0–100; **High** ≥ 70, **Medium** 40–69, **Low** &lt; 40.

## Background / cancellation (FR-AR-02)

- Walk archive root index in **chunks** (e.g. 2000 dirs per tick); **cancellation** on dialog close.
- **No** full file content scan in v1.

## User-defined rules (FR-AR-04, P2) — JSON schema sketch

```json
{
  "version": 1,
  "rules": [
    {
      "id": "rule-forum-dates",
      "when": { "sourcePathRegex": "(?i)forum.*" },
      "suggest": {
        "template": "{archiveRoot}\\{year}\\{sourceParentName}\\",
        "variables": ["year", "sourceParentName"]
      },
      "priority": 10
    }
  ]
}
```

`year` extracted from folder name `\d{4}` or file mtime.

## Tests

- Golden archive tree + source folder → expected top suggestion path.
- Manual override path → engine suggestion ignored on commit.
