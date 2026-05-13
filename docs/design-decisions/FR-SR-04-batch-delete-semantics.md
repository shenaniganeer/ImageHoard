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

## Wizard: delete-flagged-only (scoped folder)

The **delete/archive wizard** offers a separate action that recycles **only** paths currently marked **`Delete`** within the **parent folder of the current image** (no `Unset` gate). This does **not** change FR-SR-04 inverse-keep semantics; it is an additional, explicit commit path logged as `BatchDeleteDeleteFlaggedOnly`.

## Delete/archive wizard: confirmations, subtree summary, and inverse-keep before move (shipped)

**Pre-commit `ContentDialog` (main window `XamlRoot`):** Before running destructive work, the user must confirm:

- **Delete images not marked Keep** (inverse-keep in the working folder, non-recursive image list).
- **Delete images marked Delete** (delete-flagged-only).
- **Move parent folder to archive** (shows source and destination paths; default button is **Cancel**).

**Delete parent folder to Recycle Bin** already used a confirm dialog; when the working folder has **at least one immediate subdirectory**, the dialog also shows **recursive file count** and **total size** for the entire tree under that folder (same subtree scan as below).

**Move to archive — subfolders:** If the working folder has **at least one immediate subdirectory**, the move confirm dialog includes a **full-tree** line: total **file count** and **aggregate size** under the folder (implementation: [`FolderMetricsScanner.ScanSubtreeAsync`](../../src/ImageHoard.Core/Metrics/FolderMetricsScanner.cs), same symlink depth / cycle rules as other metrics). If subtree enumeration fails, the dialog still allows proceed with a short “could not measure” line (avoids blocking moves on flaky NAS paths).

**Inverse-keep before archive:** When the preference **inverse-keep delete before move** is on, non-keepers are deleted through the **same** recycle / permanent-delete batch path as the inverse-keep button (undo for recycled paths, operation log). If the user cancels the **“Permanent delete may be required”** preflight for that batch, the **move is not performed**.

**Relationship to FR-SR-03 (unset):** Wizard inverse-keep uses **`GetInverseKeepDeletionSetIgnoringUnsetGate`** — images with no decision (`Unset`) are included in the deletion set unless marked **`Keep`**. That differs from the **strict unset block** described in §FR-SR-03 and the blocked-commit copy below, which remain normative for any **other** entry point that adopts that gate. The acceptance scenarios in §Acceptance tests assume the gated policy unless stated otherwise.

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

The following assume a **batch delete entry point that implements FR-SR-03 unset blocking** (see §Relationship to FR-SR-03). The delete/archive wizard’s inverse-keep button does **not** apply that block.

1. **Given** three files: `Keep`, `Delete`, `Unset` — **when** user attempts batch delete — **then** commit is **blocked** while `Unset` exists.
2. **Given** two files: `Keep`, `Delete` (no `Unset`) — **when** user confirms batch delete — **then** only the `Delete` file is recycled; `Keep` remains.
3. **Given** two files: `Keep`, `Unset` — **when** user sets `Unset` → `Delete` and confirms — **then** the former `Unset` is recycled (same as any non-keep).

## Explicit non-choice

**Not MVP:** “Delete **only** images marked `Delete`” (explicit-delete-only). If we ever add it, it would be a **settings toggle** with separate copy; default remains inverse-keep as above.
