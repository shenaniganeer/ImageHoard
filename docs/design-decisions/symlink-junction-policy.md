# Symlink and junction policy (NFR-SC-01, PRD §7)

**Status:** Locked for P0  
**Related:** FR-BR-01, FR-SL-01, FR-SR-05 (batch moves), NFR-PF-03

## Definitions

- **Symlink:** reparse-point directory or file symlink.  
- **Junction:** directory junction (common on Windows).

## Global constants

| Constant | Value |
|----------|--------|
| `MaxSymlinkDepth` | **4** directory symlink/junction traversals from the **user-selected root** for a given operation |
| `MaxVisitedCanonical` | Cycle detection: maintain a set of **volume + file ID** or **normalized long path** keys; abort branch on repeat |

## Browse / folder tree (FR-BR-01)

- **Follow** directory symlinks/junctions when listing children, up to `MaxSymlinkDepth`.  
- **Do not follow** file symlinks as separate tree nodes (show the symlink file as a single row if it is an image; target resolution for *open* uses OS `CreateFile` behavior).  
- On depth exceed or cycle: **skip** branch, log debug line, optional toast “Skipped deep link (name)”.

## Slideshow enumeration (FR-SL-01, Algorithm A)

- Same as browse for **directory** traversal: follow up to `MaxSymlinkDepth`.  
- **Never** double-count the same **canonical file** in one session after cycle detection (if same inode seen again, skip).

## Sort mode batch delete / move (FR-SR-05, FR-SR-04)

- When deleting or moving **by explicit path list**, operate on **paths user sees** (symlink file vs target follows OS `DeleteFile` / `MoveFileEx` semantics).  
- When scanning a **staging folder tree** for “all images,” **follow** directory symlinks with same depth cap; **log** when a symlink caused inclusion of content outside the parent folder (user-visible in operation log optional).

## NAS

- No special case beyond timeouts (NFR-PF-06); depth cap prevents runaway traversal on misconfigured shares.

## Acceptance

1. Fixture with cyclic junction: enumerator terminates without hang.  
2. Depth 5 chain: level 5+ not listed in browse.
