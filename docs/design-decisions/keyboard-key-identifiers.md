# Keyboard chord key identifiers (FR-IN-01)

**Status:** Locked vocabulary for `kind: "keyboard"` chords  
**Related:** FR-IN-01, `defaults/input-profiles/profile.schema.v1.json`

## Normative rules

1. Each `keys` array is ordered **modifiers first**, then **main key** (if any).  
2. **Modifier tokens** (synthetic, not physical `ControlLeft`/`ControlRight`): `Control`, `Shift`, `Alt`, `Win` — case-sensitive, ASCII as written.  
3. **Non-modifier keys** use **HTML / MDN `KeyboardEvent.code`** style identifiers where they exist (e.g. `KeyK`, `KeyD`, `ArrowRight`, `F11`, `Space`, `Enter`, `Tab`, `Backspace`, `Delete`, `Escape`, `PageDown`, `PageUp`, `Digit1` … `Digit0`).  
4. If MDN `code` is ambiguous for a legacy key, prefer the **Key\*** / **Digit\*** / **Arrow\*** / **F\*** spellings from MDN.

## Examples (valid)

| Chord | `keys` array |
|-------|----------------|
| Ctrl+Shift+Delete | `["Control", "Shift", "Delete"]` |
| K | `["KeyK"]` |
| Arrow Right | `["ArrowRight"]` |
| F11 | `["F11"]` |

## Invalid

- Mixing single-letter aliases like `"K"` instead of `"KeyK"` (reject on import / normalize on load—implementation choice, but **files in repo** use `Key*`).  
- `"Right"` alone for arrow (use `ArrowRight`).

## Schema

The JSON Schema description for `keys` references this document; examples in schema must match the rules above.

## Migration

Profiles using legacy single letters should be normalized on import to `Key*` equivalents.
