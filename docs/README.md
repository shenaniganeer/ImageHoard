# ImageHoard — documentation index



Product requirements live in **`.cursor/plans/imagehoard_prd_v1_384a64a0.plan.md`** (do not duplicate; link PRD by path in repo). **§12** in that file indexes resolved topics to `docs/`.



Repository **agent instructions**: [`../AGENTS.md`](../AGENTS.md).



## Test plans



| Document | Purpose |

|----------|---------|

| [test-plan-reference-hw.md](./test-plan-reference-hw.md) | Reference hardware + benchmark corpora (**NFR-PF-***) |

| [test-plan-nas-smb-unc.md](./test-plan-nas-smb-unc.md) | NAS/SMB/UNC matrix + failure UX (**NFR-PF-06**, **NFR-RL-02**) |



## Design decisions



| Document | PRD trace |

|----------|-----------|

| [FR-SR-04-batch-delete-semantics.md](./design-decisions/FR-SR-04-batch-delete-semantics.md) | FR-SR-03, FR-SR-04 |

| [slideshow-algorithm-p0.md](./design-decisions/slideshow-algorithm-p0.md) | FR-SL-02, FR-SL-03 |

| [input-default-profiles.md](./design-decisions/input-default-profiles.md) | FR-IN-01 … FR-IN-06; shipped JSON under [`defaults/input-profiles/`](../defaults/input-profiles/) |

| [command-registry.md](./design-decisions/command-registry.md) | FR-IN-01; FR-BR-02/04/05; FR-SL-04/05; FR-VW-01; FR-ST-03 |

| [keyboard-key-identifiers.md](./design-decisions/keyboard-key-identifiers.md) | FR-IN-01 (`keys` vocabulary) |

| [mvp-thumbnail-scope.md](./design-decisions/mvp-thumbnail-scope.md) | MVP browsing surface |

| [folder-aggregate-metrics-model.md](./design-decisions/folder-aggregate-metrics-model.md) | FR-BR-06, FR-BR-07 |

| [browser-folder-tree-path-to-node-index.md](./design-decisions/browser-folder-tree-path-to-node-index.md) | FR-BR-06, FR-BR-07 (UI merge / `TreeView` index maintenance) |

| [fr-st-01-settings-persistence.md](./design-decisions/fr-st-01-settings-persistence.md) | FR-ST-01, FR-ST-02, FR-ST-03 |

| [archive-path-inference-fr-ar.md](./design-decisions/archive-path-inference-fr-ar.md) | FR-AR-01 … FR-AR-04 |

| [slideshow-tree-vs-folder-scope.md](./design-decisions/slideshow-tree-vs-folder-scope.md) | FR-SL-06, FR-SL-07 |

| [symlink-junction-policy.md](./design-decisions/symlink-junction-policy.md) | NFR-SC-01; PRD §7 |

| [rename-move-collision-fr-sr-06-07.md](./design-decisions/rename-move-collision-fr-sr-06-07.md) | FR-SR-06, FR-SR-07 |

| [operation-log-fr-sr-09.md](./design-decisions/operation-log-fr-sr-09.md) | FR-SR-09, NFR-RL-01 |

| [mvp-assumptions-ux.md](./design-decisions/mvp-assumptions-ux.md) | FR-VW-01 persistence; display / i18n / theme / NAS UX |



## Technical design



| Document | PRD trace |

|----------|-----------|

| [architecture-bootstrap.md](./tech-design/architecture-bootstrap.md) | Stack, CI, NFR-IN-01, decode notes |

| [debugging-runtime-stack-traces.md](./tech-design/debugging-runtime-stack-traces.md) | Agent playbook: reading stacks, first-chance exceptions, VS Code debugging, file-lock IO patterns (folder-metrics cache case) |

| [p4-forum-ingest-host-modules.md](./tech-design/p4-forum-ingest-host-modules.md) | FR-IG-*, NFR-IG-* |



## Implementation order



Follow PRD **§13 Traceability** backlog; use design docs as acceptance references when building the Windows app.


