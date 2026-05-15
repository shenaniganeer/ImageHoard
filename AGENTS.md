# ImageHoard — agent instructions

Human and autonomous agents should follow this file when changing the repository.

## Source of truth order

1. Design decisions: [`docs/design-decisions/`](docs/design-decisions/)  
2. Technical design: [`docs/tech-design/`](docs/tech-design/)  
3. Shipped defaults: [`defaults/`](defaults/)  
4. Product requirements: [`.cursor/plans/imagehoard_prd_v1_384a64a0.plan.md`](.cursor/plans/imagehoard_prd_v1_384a64a0.plan.md) — if narrative conflicts with `docs/`, **prefer `docs/`** until the PRD is updated (see PRD §12).

## Traceability

- Implement and review against **FR-** / **NFR-** / **G-** IDs from the PRD.  
- Each commit or PR description should cite the IDs it satisfies or advances.  
- Prefer automated tests with temp filesystem fixtures where the PRD calls for them (PRD §13).

## Stack and bootstrap

First implementation work should align with [`docs/tech-design/architecture-bootstrap.md`](docs/tech-design/architecture-bootstrap.md) (WinUI 3, .NET, WIC, xUnit, CI). Adjust only with an ADR update.

## Debugging stacks and first-chance exceptions

When interpreting **debugger call stacks**, **first-chance exception** spam, or **file lock** `IOException`s, use [`docs/tech-design/debugging-runtime-stack-traces.md`](docs/tech-design/debugging-runtime-stack-traces.md) (includes VS Code notes, **Speedscope** export parsing notes, the folder-metrics JSONL concurrency case study, and [`tools/speedscope-top.ps1`](tools/speedscope-top.ps1) for scripted inclusive top-N summaries).

## Local build and test (avoid wasted runs)

The WinUI host (`ImageHoard.App.exe`), including any instance started with `dotnet run --project src\ImageHoard.App\...`, keeps file handles on outputs under `src/ImageHoard.App/bin/`. **Before** `dotnet build` or `dotnet test` on the solution, confirm the app is not running; otherwise the command may fail on copy/lock errors or burn time on a doomed build.

Quick check from PowerShell in the repo root:

```powershell
Get-Process -Name ImageHoard.App -ErrorAction SilentlyContinue
```

If a process is listed, close the app (or stop the `dotnet run` session), then build or test. To re-run tests without invoking MSBuild, use `dotnet test ... --no-build` after a successful build, as long as you have not changed code that requires recompiling the app outputs.

## Resolving new gaps with the maintainer

When a requirement is ambiguous or missing from `docs/`:

1. Propose a **recommended default** with a short rationale.  
2. List **alternatives** only if they materially change behavior, security, or tests.  
3. **Wait for explicit approval** before editing the PRD or design docs—do not treat silence as consent.  
4. After approval, write the decision into the appropriate ADR and proceed.

## Pre-coding readiness artifacts

The following were added to close planning gaps; extend them with new ADRs rather than duplicating long spec in chat:

| Document | Purpose |
|----------|---------|
| [`docs/design-decisions/fr-st-01-settings-persistence.md`](docs/design-decisions/fr-st-01-settings-persistence.md) | Settings paths, portable mode |
| [`docs/design-decisions/command-registry.md`](docs/design-decisions/command-registry.md) | Full `commandId` registry |
| [`docs/design-decisions/keyboard-key-identifiers.md`](docs/design-decisions/keyboard-key-identifiers.md) | Keyboard chord key strings |
| [`docs/design-decisions/symlink-junction-policy.md`](docs/design-decisions/symlink-junction-policy.md) | Symlink depth and cycles |
| [`docs/design-decisions/rename-move-collision-fr-sr-06-07.md`](docs/design-decisions/rename-move-collision-fr-sr-06-07.md) | Rename tokens, collisions |
| [`docs/design-decisions/operation-log-fr-sr-09.md`](docs/design-decisions/operation-log-fr-sr-09.md) | Operation JSONL log |
| [`docs/design-decisions/mvp-assumptions-ux.md`](docs/design-decisions/mvp-assumptions-ux.md) | Dual monitor, i18n, themes, NAS disconnect |
| [`docs/design-decisions/browser-folder-tree-path-to-node-index.md`](docs/design-decisions/browser-folder-tree-path-to-node-index.md) | Browser `TreeView`: `_folderTreeNodeByPath` maintenance vs `_folderTreeEntryByPath` |
| [`docs/design-decisions/browser-folder-tree-virtualization-itemsrepeater.md`](docs/design-decisions/browser-folder-tree-virtualization-itemsrepeater.md) | Virtualized browser tree: ItemsRepeater + flat projection (accepted); supersedes `TreeViewNode`-centric index guidance when implemented |
| [`docs/design-decisions/slideshow-algorithm-p0.md`](docs/design-decisions/slideshow-algorithm-p0.md) | Tree slideshow: streaming discovery, uniform sampling over full discovered set, RAM + spill path store, display history + redo, shuffle vs browse enumeration (FR-SL-02/03, NFR-PF-03) |
| [`docs/tech-design/browser-navigation-wizard-tree-coordination.md`](docs/tech-design/browser-navigation-wizard-tree-coordination.md) | Browser `TreeView`, image navigation, delete/archive wizard, preview pipeline, **tree slideshow overlay list position**; **update when changing any of these** |

**Tree slideshow maintenance:** If you change **enumeration order** (`RecursiveImageEnumerator` slideshow overload), **discovered path store or sampling** (`SlideshowDiscoveredPathStore`, `TreeSlideshowSession.TryMoveNext` / history), **`SlideshowCoordinator` sibling overlay or overlay metrics**, **fullscreen slideshow UI**, or **`ApplyOverlayListPositionFromTreeAsync`**, update [`slideshow-algorithm-p0.md`](docs/design-decisions/slideshow-algorithm-p0.md) and this coordination doc when behavior is user-visible; keep **Settings → About random fairness…** (`SlideshowFairnessHelp_Click`) aligned with the ADR; extend [`TreeSlideshowSessionTests`](tests/ImageHoard.Tests/TreeSlideshowSessionTests.cs) / [`RecursiveImageEnumeratorTests`](tests/ImageHoard.Tests/RecursiveImageEnumeratorTests.cs) / [`SlideshowCoordinatorTests`](tests/ImageHoard.Tests/SlideshowCoordinatorTests.cs) for regressions. Cite **FR-SL-** IDs in commit/PR text when slideshow behavior changes.

## Scaffold expectation

Until `src/` exists, the first engineering milestone is **solution scaffold + CI smoke** (`dotnet build` / `dotnet test` on Windows), then PRD backlog FRs in §13 order.
