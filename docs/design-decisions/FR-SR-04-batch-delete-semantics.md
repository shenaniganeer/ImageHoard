# FR-SR-04 — Sort mode: batch delete semantics (locked)

**Status:** Locked for MVP  
**Related:** FR-SR-01 (tri-state), FR-SR-03 (pre-commit summary), G-03  
**Resolved:** `resolve-sort-semantics`

## Decision

**MVP batch delete removes every image whose decision state is _not_ `Keep`.**

Equivalently: **only images explicitly marked `Keep` survive** this step; both `Delete` and `Unset` are included in the deletion set.

This is the **inverse-keep** interpretation (not the “delete only items marked `Delete`” interpretation).

## Rationale

- Matches **G-03** (“flag keep/delete, then **batch delete unflagged**”): in a tri-state workflow, “unflagged” is read as **not marked Keep** (no positive keep decision), which is the usual staging **cull** pattern: mark winners, discard the rest.
- **Persona A** (gallery hoarder): optimize for speed through large folders; the safe mental model is “I marked what I want; everything else goes.”
- The **`Delete`** flag remains useful for **summary, sorting the list, keyboard flow, and intent** before commit; at commit time it does not *exclude* those files from deletion—they are already not `Keep`.

## FR-SR-03 (unset) at commit time

Pre-commit dialog must show counts: **`Keep`**, **`Delete`**, **`Unset`**.

**MVP policy:** **Block** starting batch delete while **`Unset` > 0**, with copy that points users to finish reviewing (aligns with FR-SR-03 “block” option and avoids silent mass-delete of never-seen files).

Optional **later** setting (not MVP): “Allow delete with unreviewed items” treating `Unset` like non-keepers—would relax the block without changing FR-SR-04’s definition of *what* gets deleted once allowed.

## UX copy (English, MVP)

Use these strings (or trivial tense/plural variants) so engineering and tests share one source of truth.

### Blocked commit (`Unset` > 0)

- **Title:** `Can’t delete yet`
- **Body:** `You still have **{unset}** image(s) with no decision. Mark each image **Keep** or **Delete** before running batch delete.`
- **Primary:** `OK` (dismiss)

### Allowed commit (`Unset` = 0; every image is `Keep` or `Delete`)

Placeholders: **`{notKeepCount}`** = images with state `Delete` (equals “not `Keep`” in this gate). **`{keepCount}`** = images with state `Keep`.

- **Title:** `Delete non-keepers?`
- **Body:** `Recycle Bin will receive **{notKeepCount}** image(s) (**not** marked **Keep**). **{keepCount}** image(s) marked **Keep** are unchanged. Deletes use the Recycle Bin per Windows settings.`
- **Primary (destructive):** `Delete {notKeepCount} image(s)`
- **Secondary:** `Cancel`

Optional second line when **`{markedDelete}`** is shown in a detailed summary panel: `Marked **Delete**: **{markedDelete}** · Marked **Keep**: **{keepCount}**` (should match `{notKeepCount}` + `{keepCount}` = total in scope).

### Status / toast (after success)

- `Sent **{n}** image(s) to the Recycle Bin (not marked **Keep**).`

### In-app glossary / help (one line)

- `Batch delete removes every image that is **not** marked **Keep** once you have reviewed them all (no **Unset** left).`

## Acceptance tests (traceability)

1. **Given** three files: `Keep`, `Delete`, `Unset` — **when** user attempts batch delete — **then** commit is **blocked** while `Unset` exists.
2. **Given** two files: `Keep`, `Delete` (no `Unset`) — **when** user confirms batch delete — **then** only the `Delete` file is recycled; `Keep` remains.
3. **Given** two files: `Keep`, `Unset` — **when** user sets `Unset` → `Delete` and confirms — **then** the former `Unset` is recycled (same as any non-keep).

## Explicit non-choice

**Not MVP:** “Delete **only** images marked `Delete`” (explicit-delete-only). If we ever add it, it would be a **settings toggle** with separate copy; default remains inverse-keep as above.
