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

## Scaffold expectation

Until `src/` exists, the first engineering milestone is **solution scaffold + CI smoke** (`dotnet build` / `dotnet test` on Windows), then PRD backlog FRs in §13 order.
