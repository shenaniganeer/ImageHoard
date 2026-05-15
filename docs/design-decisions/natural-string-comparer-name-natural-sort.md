# Natural string comparer — Name (natural) sort

**Status:** Describes `NaturalStringComparer` behavior for **Name (natural)** ordering across the app.  
**Related:** [`src/ImageHoard.Core/NaturalStringComparer.cs`](../../src/ImageHoard.Core/NaturalStringComparer.cs), [`tests/ImageHoard.Tests/NaturalStringComparerTests.cs`](../../tests/ImageHoard.Tests/NaturalStringComparerTests.cs)

## Where it is used

The same comparer instance (`NaturalStringComparer.OrdinalIgnoreCase`) backs **Name (natural)** / `NameNatural` ordering in:

- [`FolderDirectorySort.CompareNameNatural`](../../src/ImageHoard.Core/Browse/FolderDirectorySort.cs) — directory listings  
- [`BrowseContextImageSequence.OrderImageFileEntries`](../../src/ImageHoard.Core/Browse/BrowseContextImageSequence.cs) — browse image sequence  
- [`MainWindow.ApplyListSort`](../../src/ImageHoard.App/MainWindow.xaml.cs) — preview list rows  
- [`CompareNameNaturalFolder`](../../src/ImageHoard.App/MainWindow.BrowserPane.cs) — folder tree display labels  

Plain **Name** (non-natural) sorts use `StringComparer.OrdinalIgnoreCase` instead.

## Normative rules

1. **ASCII digit runs** (`0`–`9`): compared by **numeric value** when both runs parse as `ulong` (up to 19 digits). If numeric values tie, order by **ordinal** comparison of the digit substrings. If a run is too long to parse as `ulong`, fall back to **ordinal ignore-case** on that run.  
2. **Non-digit text runs** (maximal spans of characters that are not ASCII digits): compared left-to-right with:  
   - **Non-letters before letters:** a code point where `char.IsLetter` is false sorts before a code point where `char.IsLetter` is true. (So symbols, punctuation, spaces, etc. sort before Unicode letters.)  
   - **Two non-letters:** ordinal (`char` order).  
   - **Two letters:** compare by `char.ToUpperInvariant`; if equal, treat as equal at that position and advance (case-insensitive letter ordering).  
   - **End of one run before the other:** the shorter run sorts first (prefix before extension).  
3. **Mixed position** (one side is an ASCII digit, the other is not at the same string index): if the non-digit code point is **not** a Unicode letter (`char.IsLetter` false), it is a **symbol** for this rule and sorts **before** the ASCII digit. Otherwise (digit vs letter), compare the two code units with `char.ToUpperInvariant` and ordinal order (digits still sort before letters at this boundary, e.g. `1` before `a`).

## Rationale (portable Core)

`ImageHoard.Core` targets portable `net8.0`. Explorer-style logical compare (`StrCmpLogicalW`) is Windows-native and not used here; this comparer keeps ordering **managed, testable, and OS-agnostic** while matching product expectations for numeric chunks and symbol/letter ordering in text runs.

## Tests

Regressions and edge cases belong in [`NaturalStringComparerTests`](../../tests/ImageHoard.Tests/NaturalStringComparerTests.cs). Any other test that asserts a specific **Name (natural)** ordering should be updated when this ADR changes.
