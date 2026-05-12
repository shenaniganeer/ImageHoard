# Default input profiles — keyboard-only and mouse-only (FR-IN-*)

**Status:** Locked defaults for MVP implementation  
**Related:** FR-IN-01 … FR-IN-06, FR-ST-02, FR-SR-02  
**Shipped artifacts:** `defaults/input-profiles/` (JSON + schema + index)

## Shipped files

| File | Purpose |
|------|---------|
| [`defaults/input-profiles/index.json`](../../defaults/input-profiles/index.json) | Built-in profile list and **aliases** (`KeyboardFirst` → `KeyboardOnly`, `MouseFirstSort` → `MouseOnly`) for FR-IN-02 / FR-ST-02 |
| [`defaults/input-profiles/keyboard-only.v1.json`](../../defaults/input-profiles/keyboard-only.v1.json) | **Keyboard-only** default bindings |
| [`defaults/input-profiles/mouse-only.v1.json`](../../defaults/input-profiles/mouse-only.v1.json) | **Mouse-only** default bindings |
| [`defaults/input-profiles/profile.schema.v1.json`](../../defaults/input-profiles/profile.schema.v1.json) | JSON Schema for import/export validation (FR-IN-01) |

## FR-IN-* traceability

| ID | How this document + JSON satisfy it |
|----|-------------------------------------|
| **FR-IN-01** | Chords use `profile.schema.v1.json` (`keyboard`, `mouseButton`, `mouseWheel`, `mouseChord`, `mouseWheelTilt`); keyboard `keys` use [keyboard-key-identifiers.md](./keyboard-key-identifiers.md). Import/export is one JSON object per profile with `commandId` → chord list (OR semantics within a command). |
| **FR-IN-02** | Two built-in profiles: **`KeyboardOnly`** and **`MouseOnly`**, plus aliases above; user profiles duplicate these in user settings (out of scope here). |
| **FR-IN-03** | Mouse-only profile: wheel advances; primary / middle / right set flags; **X1 / X2** drive high-level pipeline steps (see §Pipeline). |
| **FR-IN-04** | `X3`–`X5` enumerated in schema when the OS exposes them; see §Gaming mice / practical limits. |
| **FR-IN-05** | Destructive commits from **X1 / X2** use **double-confirm**: first press opens FR-SR-03 blocking summary; **second press of the same side button within 2 s** *or* explicit **Confirm** with primary button completes. Duplicates across profiles are avoided by construction; runtime must still warn on user edits. |
| **FR-IN-06** | No gesture-only core action; mouse-only uses buttons, wheel, and optional tilt; slideshow scope has a documented non-gesture fallback (chrome control). |

## Profile model

- **`KeyboardOnly`:** Every command in the minimum command set has at least one **keyboard** chord; mouse is optional and omitted from the shipped file.
- **`MouseOnly`:** Same command set using **mouse / wheel / chord / tilt**; default chords avoid **keyboard modifiers** except **`view.panPreview`** (uses `Shift` + primary button so pan does not collide with unmodified primary click for sort). Where hardware lacks tilt or `X3`, the app exposes **minimal on-screen controls** for `slideshow.toggleScope` (implementation detail; QA uses hardware from [test-plan-reference-hw.md](../test-plan-reference-hw.md) when available).

## Command IDs (minimum set)

Extended commands (browse, slideshow, viewer, settings): [command-registry.md](./command-registry.md).

| `commandId` | Description |
|-------------|-------------|
| `nav.nextImage` | Next image in current context |
| `nav.prevImage` | Previous image |
| `nav.firstImage` | First image in current folder list |
| `nav.lastImage` | Last image in current folder list |
| `sort.flagKeep` | Set state Keep |
| `sort.flagDelete` | Set state Delete |
| `sort.flagUnset` | Clear to Unset |
| `sort.commitBatchDelete` | Open / confirm batch delete (FR-SR-03 / FR-SR-04) |
| `sort.moveToArchive` | Open / confirm move to archive (FR-SR-05) |
| `sort.undoLastFlag` | Undo last decision |
| `slideshow.toggleScope` | Tree session ↔ Folder scope (FR-SL-06) |
| `ui.fullscreen` | Toggle fullscreen |
| `ui.escape` | Back / close dialog (MouseOnly default: left+right chord; see merged note below) |
| `view.clearSelection` | Clear image selection and blank preview when not fullscreen |
| `view.panPreview` | Pan primary preview when it scrolls (modifier + drag; see shipped MouseOnly profile) |

## Pipeline bindings: wheel, X1, X2 (normative)

These are the **mouse-first sort** flows the PRD calls out (FR-IN-03). State machine for **destructive** side buttons is shared.

### Wheel (vertical)

| Direction | Command | Notes |
|-----------|---------|--------|
| **Rotate down** | `nav.nextImage` | Advances in large directory / preview context (design: same as list or single-image advance—implementation binds to active focus). |
| **Rotate up** | `nav.prevImage` | |

**Modifier + wheel** is reserved for user rebinding and conflict detection (FR-IN-05); **defaults** do not use keyboard modifiers on the mouse-only profile.

### X1 (typical “browser back”)

| Phase | Behavior |
|-------|----------|
| **First press** | Opens **`sort.commitBatchDelete`** flow: FR-SR-03 summary dialog; **no files deleted yet**. |
| **Second press** (same button within **2 s** while dialog focused) **or** **Confirm** with primary button | Completes the destructive path per FR-SR-04 semantics locked in [FR-SR-04-batch-delete-semantics.md](./FR-SR-04-batch-delete-semantics.md). |
| **Cancel / loss of focus** | Returns without delete; next X1 starts a **new** confirmation cycle. |

### X2 (typical “browser forward”)

| Phase | Behavior |
|-------|----------|
| **First press** | Opens **`sort.moveToArchive`** wizard (dry-run, collision policy, path confirm). |
| **Second press** within **2 s** while confirm UI focused **or** explicit Confirm | Executes batch move per FR-SR-05. |

**Rationale:** Single accidental side-button bumps must not delete or move (FR-IN-05 + NFR-RL-01).

### Primary buttons (flags, not pipeline)

| Input | Command |
|-------|---------|
| **Left click** (short, in viewer) | `sort.flagKeep` |
| **Right click** (short, in viewer) | `sort.flagDelete` |
| **Middle click** | `sort.flagUnset` |

**Context menu vs delete:** **Short right-click** (< **400 ms**, press–release without move) **only** toggles delete flag in the image viewer. **Long right-click** (≥ **400 ms**) opens the Windows context menu. **Drag** with right button down suppresses both until release (treat as gesture cancel).

### Other mouse-only chords (summary)

| Command | Default |
|---------|---------|
| `sort.undoLastFlag` | **Wheel tilt right**, else **triple middle click** within ~**600 ms** |
| `slideshow.toggleScope` | **Wheel tilt left**, else **`X3`** if present, else **toolbar / on-screen scope** control |
| `ui.fullscreen` | **Double left** on viewer chrome / safe hit target |
| `ui.escape` | **Left + right chord** (simultaneous within ~**50 ms**); must not fire while a drag is active |

## KeyboardOnly — default bindings

Identifiers use [keyboard-key-identifiers.md](./keyboard-key-identifiers.md) (`Key*`, `Arrow*`, modifier tokens).

| Command | Default binding |
|---------|-----------------|
| `nav.nextImage` | `ArrowRight`, `ArrowDown`, `Space` |
| `nav.prevImage` | `ArrowLeft`, `ArrowUp`, `Backspace` |
| `nav.firstImage` | `Home` |
| `nav.lastImage` | `End` |
| `sort.flagKeep` | `KeyK` |
| `sort.flagDelete` | `KeyD` |
| `sort.flagUnset` | `KeyU` |
| `sort.commitBatchDelete` | `Control`+`Shift`+`Delete` |
| `sort.moveToArchive` | `Control`+`Shift`+`KeyM` |
| `sort.undoLastFlag` | `Control`+`KeyZ` |
| `slideshow.toggleScope` | `Tab` |
| `ui.fullscreen` | `F11`, `Enter` (when list focused) |
| `view.clearSelection` | `Escape` |
| `ui.escape` | *(no keyboard chord in KeyboardOnly file; merged profile includes MouseOnly left+right chord only)* |

**Merged built-in profile:** `Escape` is bound to `view.clearSelection` on the keyboard side so the same keyboard chord is not listed twice under different commands (FR-IN-05). Fullscreen still handles `Escape` to exit fullscreen when `view.clearSelection` declines in fullscreen mode.

## Gaming mice / practical limits (FR-IN-04)

- Prefer **`X1` / `X2`** for pipeline; map **`X3`+** when `WM_XBUTTON*` (or equivalent) exposes distinct ids.
- Some vendors remap side buttons to duplicate keyboard keys or duplicate each other—document in app help; user fixes via rebind JSON.
- If **raw input** is required later for reliable high-button-id capture, capture in a spike note (out of scope for this table).

## Checklist for QA (usability §11)

- [ ] **MouseOnly:** Process **30** images in a staging folder **without keyboard** (wheel + buttons + X1/X2 confirms).
- [ ] **KeyboardOnly:** Same scenario **without mouse**.
- [ ] **No destructive commit** on single accidental **X1** or **X2** press (double-confirm verified).
- [ ] **Long right-click** opens menu; **short right-click** sets delete flag only.
