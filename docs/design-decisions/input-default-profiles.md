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
| **FR-IN-01** | Chords use `profile.schema.v1.json` (`keyboard`, `mouseButton`, `mouseWheel` with optional `heldButtons`, `mouseChord`, `mouseWheelTilt`); keyboard `keys` use [keyboard-key-identifiers.md](./keyboard-key-identifiers.md). Import/export is one JSON object per profile with `commandId` → chord list (OR semantics within a command). |
| **FR-IN-02** | Two built-in profiles: **`KeyboardOnly`** and **`MouseOnly`**, plus aliases above; user profiles duplicate these in user settings (out of scope here). |
| **FR-IN-03** | Mouse-only profile: wheel advances; primary / middle / right set flags; **X1 / X2** open the delete/archive wizard (see §Pipeline). |
| **FR-IN-04** | `X3`–`X5` enumerated in schema when the OS exposes them; see §Gaming mice / practical limits. |
| **FR-IN-05** | Destructive commits use explicit controls in the delete/archive wizard plus **ContentDialog** confirmation before inverse-keep delete, delete-flagged-only, move-to-archive, enriched confirm for delete-folder when subfolders exist, and **browser tree delete** (`browse.treeDelete`); merged profiles avoid duplicate keyboard chords. |
| **FR-IN-06** | No gesture-only core action; mouse-only uses buttons, wheel, and optional tilt; slideshow **switch-to-browse** has keyboard **`Tab`** fallback when tilt / `X3` are unavailable. |

## Profile model

- **`KeyboardOnly`:** Every command in the minimum command set has at least one **keyboard** chord; mouse is optional and omitted from the shipped file.
- **`MouseOnly`:** Same command set using **mouse / wheel / chord / tilt**; default chords avoid **keyboard modifiers** except **`view.panPreview`** (uses `Shift` + primary button so pan does not collide with unmodified primary click for sort). Slideshow uses **`mouseWheel` + `heldButtons`** for sibling navigation while **Right** is held; **`slideshow.switchToBrowseAtCurrentLocation`** uses wheel tilt left or **`X3`** when the OS exposes it—otherwise use keyboard **`Tab`** in fullscreen slideshow (see [test-plan-reference-hw.md](../test-plan-reference-hw.md) for QA hardware).

## Command IDs (minimum set)

Extended commands (browse, slideshow, viewer, settings): [command-registry.md](./command-registry.md).

| `commandId` | Description |
|-------------|-------------|
| `nav.nextImage` | Next image in current context |
| `nav.prevImage` | Previous image |
| `nav.firstImage` | First image in current folder list |
| `nav.lastImage` | Last image in current folder list |
| `nav.nextDirectory` | Next sibling folder under the parent of the folder containing the current image (or browse root when no image context); same sort as folder tree |
| `nav.prevDirectory` | Previous sibling folder under the parent of the folder containing the current image (or browse root when no image context); same sort as folder tree |
| `nav.cycleNavigationMode` | Cycle browse navigation mode (folder list filter for sequential navigation) |
| `sort.flagKeep` | Set state Keep |
| `sort.flagDelete` | Set state Delete |
| `sort.flagUnset` | Clear to Unset |
| `sort.deleteArchiveWizard` | Modeless delete/archive wizard: inverse-keep delete, delete-flagged-only, rename/move/delete parent folder of current image; destructive actions require **ContentDialog** confirm; move/delete-folder show **subtree file count + size** when the folder has immediate subfolders | FR-SR-03/04/05 |
| `sort.commitBatchDelete` | *(legacy)* same as `sort.deleteArchiveWizard` | FR-SR-03/04 |
| `sort.moveToArchive` | *(legacy)* same as `sort.deleteArchiveWizard` | FR-SR-05 |
| `sort.clearAllFlags` | Clear all sort flags (in-memory session); **Sort → Clear all flags** menu (P0 chrome) |
| `slideshow.start` | Start or resume tree slideshow (see shipped profile + app dialog) |
| `slideshow.switchToBrowseAtCurrentLocation` | Fullscreen slideshow → browse parent folder of current slide while retaining tree session for resume |
| `slideshow.siblingNextImage` | Slideshow: next sibling image in current file’s directory |
| `slideshow.siblingPrevImage` | Slideshow: previous sibling image in current file’s directory |
| `ui.fullscreen` | Toggle fullscreen |
| `ui.escape` | Back / close dialog (MouseOnly default: left+right chord; see merged note below) |
| `view.clearSelection` | Clear image selection and blank preview when not fullscreen |
| `view.panPreview` | Pan primary preview when it scrolls (modifier + drag; see shipped MouseOnly profile) |
| `browse.treeDelete` | Delete the selected browser tree row to Recycle Bin (tree focus + `Delete`); selection is single-row on `FolderTree` |

## Pipeline bindings: wheel, X1, X2 (normative)

These are the **mouse-first sort** flows the PRD calls out (FR-IN-03). Side buttons open the delete/archive wizard (modeless); choosing delete or move there still opens a **modal confirm** on the main window before files change.

### Wheel (vertical)

| Direction | Command | Notes |
|-----------|---------|--------|
| **Rotate down** | `nav.nextImage` | Advances in large directory / preview context (design: same as list or single-image advance—implementation binds to active focus). |
| **Rotate up** | `nav.prevImage` | |

**Modifier + wheel** is reserved for user rebinding and conflict detection (FR-IN-05); **defaults** do not use keyboard modifiers on the mouse-only profile.

**Mouse buttons held during vertical wheel:** `mouseWheel` chords may include optional `heldButtons` (unique button names from the schema: `Left`, `Middle`, `Right`, `X1`, `X2`, …). When `heldButtons` is non-empty, the chord matches only if that exact set of mouse buttons is down when the wheel moves, using buttons observable from the OS (WinUI reports Left through X2; X3+ may appear in JSON but are not read from pointer state). Omitted or empty `heldButtons` means plain wheel regardless of mouse button state. Dispatch tries bindings that specify `heldButtons` before plain `mouseWheel` bindings so combinations such as X1 + wheel can override default wheel navigation when both are bound.

### X1 / X2 (side buttons)

| Button | Behavior |
|--------|----------|
| **X1** | Opens the modeless **`sort.deleteArchiveWizard`** window. |
| **X2** | Opens the same **`sort.deleteArchiveWizard`** window. |

**Rationale:** A single press does not delete or move files; the wizard lists scoped actions, and each destructive commit is confirmed in a dialog (see shipped `mouse-only.v1.json` notes).

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
| `sort.clearAllFlags` | **Wheel tilt right**, else **triple middle click** within ~**600 ms** |
| `slideshow.switchToBrowseAtCurrentLocation` | **Wheel tilt left**, else **`X3`** if present, else keyboard **`Tab`** in slideshow |
| `slideshow.siblingNextImage` | **Right** button held + wheel **Down** |
| `slideshow.siblingPrevImage` | **Right** button held + wheel **Up** |
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
| `nav.nextDirectory` | `Control`+`Alt`+`PageDown` |
| `nav.prevDirectory` | `Control`+`Alt`+`PageUp` |
| `nav.cycleNavigationMode` | `Control`+`Shift`+`KeyN` |
| `sort.flagKeep` | `KeyK` |
| `sort.flagDelete` | `KeyD` |
| `sort.flagUnset` | `KeyU` |
| `sort.deleteArchiveWizard` | `Control`+`Shift`+`Delete`, `Control`+`Shift`+`KeyM` |
| `sort.clearAllFlags` | `Control`+`KeyZ` |
| `slideshow.start` | `Control`+`Shift`+`KeyS` |
| `slideshow.switchToBrowseAtCurrentLocation` | `Tab` |
| `slideshow.siblingNextImage` | `Control`+`Alt`+`ArrowRight` |
| `slideshow.siblingPrevImage` | `Control`+`Alt`+`ArrowLeft` |
| `settings.open` | `Control`+`KeyP` |
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
