---
name: ImageHoard PRD v1.5
overview: PRD for Windows-first ImageHoard—staging/NAS archive, browse/sort/slideshow, input remapping, Phase P4 **forum/HTTP ingest** with **comparable tools** (VRipper, JDownloader 2, etc.) for parity research—and Android/video extensions.
todos:
  - id: resolve-sort-semantics
    content: "Designer/PM: Lock FR-SR-04 (keep vs delete semantics) and UX copy"
    status: completed
  - id: define-reference-hw
    content: "Eng: Add reference HW + benchmark corpus sizes to test plan (NFR-PF-*)"
    status: completed
  - id: slideshow-algorithm-pick
    content: "Eng: Choose Algorithm A vs B for P0; document uniformity in user-facing settings/help"
    status: completed
  - id: input-profiles-table
    content: "Design: Ship keyboard-only + mouse-only default profiles; document wheel/X1/X2 pipeline bindings (FR-IN-*)"
    status: completed
  - id: mvp-thumbnail-scope
    content: "PM: Decide thumbnail grid vs list-only for MVP (open question §12)"
    status: completed
  - id: folder-aggregate-metrics
    content: "Eng: Define cached folder stats model for sort-by-size/count (FR-BR-06, NFR-PF-05)"
    status: completed
  - id: archive-inference-rules
    content: "PM/Design: Specify inference UX (confidence, override) and template/rule format for FR-AR-*"
    status: completed
  - id: slideshow-scope-toggle
    content: "Design: Slideshow scope indicator + default sibling ordering in Folder scope (FR-SL-06/07)"
    status: completed
  - id: nas-network-test-matrix
    content: "Eng: NAS/SMB/UNC matrix + degraded-mode UX (NFR-PF-06, NFR-RL-02)"
    status: completed
  - id: forum-ingest-host-modules
    content: "Eng/TechDesign (P4): Host resolver modules + SSRF/rate-limit story for FR-IG / NFR-IG"
    status: completed
isProject: false
---

# ImageHoard — Product Requirements Document (PRD v1.5)

**Document purpose:** Single source of truth for product design iteration and for **agentic development** (each requirement has a stable ID; acceptance criteria are testable).

**Product working name:** ImageHoard (replace if branding changes).

**Competitive anchor:** IRFanView (speed, format breadth, lightweight feel) extended with **collection-scale navigation**, **decision sorting**, and **minimal-up-front indexing** for slideshows.

---

## 0. Background: typical workflow and storage

**Source material:** The user frequently **downloads image galleries** from the web. A **gallery** here means a set of **roughly 50–200+ images** (order of magnitude; can vary) saved together as one acquisition batch.

**Staging (“download folder”):** Galleries land in an **arbitrary staging folder** chosen by the user. It is **not** assumed to be the Windows **%USERPROFILE%\\Downloads** directory unless the user points the app there. Over time this staging tree may accumulate **hundreds to thousands** of gallery folders **before** the user runs a **sort/cull** pass.

**Where staging lives:** The staging parent is **presumed local** (SSD/HDD) but **may be on NAS** (SMB/UNC or mapped drive). The app must treat **network-backed paths** as first-class (see **§6**).

**Archive:** After sorting, material is **moved** into an **archive** hierarchy. **Post-sort archival storage is almost certainly NAS** in typical use. Moves may be **staging → archive on same NAS**, **local → NAS**, or **NAS → NAS** depending on setup.

**Consumption / slideshow:** **Slideshow viewing is expected** primarily against the **NAS archive root** or **favorited subfolders** of that archive (large recursive trees, mixed depths).

This section informs **personas**, **performance expectations**, and **failure handling** (latency, dropouts) but does not change filesystem-first doctrine (§1).

**Future (P4):** Optional **HTTP/forum ingest** (**§10**) may populate staging from a pasted URL instead of manual browser download.

## 1. Vision and product thesis

**One-liner:** A fast, lightweight desktop app for browsing, viewing, and **decision-sorting** massive image libraries (deep folders, 10k+ files, multi-TB), optimized for **fullscreen operation** with **one-handed mouse workflows** as a first-class design target (keyboard parity retained), with **recursive random slideshows** that avoid heavy pre-scanning.

**Design principles**

- **Speed over chrome:** Sub-100ms perceived navigation **on local SSD** where hardware allows; on **NAS**, target **best-effort responsiveness** with explicit progress/cancel and no UI hang (see **NFR-PF-06**).
- **Explicit destructive safety:** Deletes and moves are reversible where the OS supports it, confirmable, and logged.
- **Progressive disclosure:** Core paths are one-handed (mouse *or* keyboard); advanced operations exist but do not clutter the fast path.
- **Input equity:** Any command may be bound to **keyboard, mouse buttons (including side / “thumb” buttons), wheel actions, and optional gestures**—with **gaming mice** (many discrete buttons) explicitly in scope where the OS exposes them.
- **Folder truth:** The filesystem is canonical; the app does not require a proprietary library database for core browsing (optional caches only for folder aggregates and slideshow).

---

## 2. Goals, non-goals, and phases

### 2.1 Goals (MVP, Windows)

- G-01: Browse folders and images with **immediate usability** on collections with **≥50k images** across **deep nesting** without mandatory full-library indexing.
- G-02: View **common raster formats** (see §4.2) with correct orientation and reasonable color accuracy.
- G-03: **Sort mode:** rapid pass through **staging** (a gallery folder or subtree), **flag keep/delete**, then **batch delete** unflagged and **relocate** to the **archive** root, with optional **rename**—paths may span **local and NAS** (§0).
- G-04: **Slideshow mode:** **random order** across a chosen root and **all descendants**, with **lazy discovery** (no full upfront index required by default), plus **FR-SL-06/07** **scope toggle** between that **Tree session** and **sibling-only** browsing in the current folder.
- G-05: **Fullscreen + unified input:** core actions mappable across **keyboard, mouse, wheel, and optional gestures**; **two default profiles** (mouse-first sort/browse and keyboard-first); import/export binding sets.
- G-06: **Portable Android later:** architectural choices documented (§8) so UI/logic split and I/O abstractions do not block a port.
- G-07 (secondary): **Archive assist:** suggest a destination path under the archive root by **inferring conventions** from existing archive **naming and hierarchy** (user always confirms).

### 2.2 Non-goals (MVP)

- NG-01: **Cloud-first** libraries, IME syncing, or multi-user collaboration.
- NG-02: **RAW** photo developer parity (dedicated RAW converters).
- NG-03: **AI auto-tagging**, face recognition, or semantic search (may be future).
- NG-04: **Full** duplicate detection beyond optional byte-size/hash (optional future).
- NG-05: **Built-in HTTP/forum scraping and host-specific downloaders** in **P0–P2** (see **§10**, Phase **P4**).

### 2.3 Phases

| Phase | Scope |
|--------|--------|
| **P0 MVP** | Windows browse, view, sort/archive **local and UNC/mapped NAS paths**, recursive random slideshow (lazy), unified input bindings, folder aggregate sorts (cached), basic settings |
| **P1** | Polish: thumbnails policy, metadata panel, undo integration, performance profiles, edge-case formats; **mouse gestures**; **archive path inference (G-07, FR-AR-*)** |
| **P2** | Optional lightweight index/cache for faster random slideshow on repeated roots; richer inference (“learned” rules) |
| **P3 (extension)** | **Video** browsing/slideshow/sort with codec/container constraints per platform |
| **P4 (extension)** | **Forum / HTTP gallery ingest**—paste link, resolve **common image hosts**, download into **staging** under **forum-context folder name** (FR-IG-*) |

---

## 3. Personas and primary scenarios

**Persona A — Gallery hoarder / archivist:** Frequently sources **web galleries** (today: manual download into **§0 staging**; **future P4:** optional **ingest from forum URL** per **FR-IG-***) that accrues **many** unprocessed gallery directories; needs to **cull fast**, delete noise, keep winners, then **move** survivors into a **NAS archive** hierarchy.

**Persona B — Reference collector:** Needs **non-destructive** browsing and slideshow across messy trees (**often NAS archive root or favorites**) without waiting for a full scan bar.

**Persona C — Power user:** Demands **rebindings across devices**, scripts later (out of MVP unless required), predictable behavior on huge paths, and **gaming-mouse** workflows.

**Critical scenario (Sort, mouse-first):** Open **staging gallery folder** (local or NAS) → fullscreen → **scroll wheel** (or bound wheel action) advances through **large directory listing or image preview** → **mouse buttons** flag keep/delete per binding → **mouse buttons 4–5** (typical **X1/X2 / back-forward**) trigger pipeline steps such as **commit delete** and **move to archive** on **NAS** (exact defaults are design-owned) → confirm summaries as today.

**Critical scenario (Sort, keyboard):** Same as prior: advance image → flag → finish pass → confirm → delete/move/rename (**cross-volume paths** acceptable).

**Critical scenario (Slideshow):** Pick root (typical: **NAS archive root** or **favorite subtree**) → start random fullscreen → tree-random advance across **many nested gallery folders** → on an interesting image, **one action** switches to **Folder scope** to **next/previous among siblings** in that image’s directory → **same action** returns to **Tree session** without losing the prior random traverse state (see **FR-SL-06, FR-SL-07**).

---

## 4. Functional requirements

### 4.1 Browsing and navigation

| ID | Requirement | Acceptance criteria (high level) |
|----|-------------|----------------------------------|
| FR-BR-01 | **Folder tree or path-based navigation** with lazy loading of directory children; paths include **local drive**, **mapped drive**, and **UNC** (`\\\\server\\share\\…`) | Opening a drive/root with many subfolders does not block UI; children load on expand or enter; **NAS latency** may defer metadata (see NFR-PF-06) |
| FR-BR-02 | **Image list** for current folder with optional subfolder inclusion toggle (default: current folder only for list; slideshow separate) | Toggling inclusion updates list within performance budget; progress indicator if scan exceeds threshold |
| FR-BR-03 | **Sorting** of list by name, date modified, size | Order matches OS-reported metadata; stable sort on tie |
| FR-BR-04 | **Fast jump**: go to path, bookmarks/favorites (minimum: saved path list) | User can return to saved roots in ≤2 actions from fullscreen |
| FR-BR-05 | **Unsupported file type** handling | Graceful skip + toast/status; no crash; offer “reveal in Explorer” |
| FR-BR-06 | **Folder navigation modes:** sort/filter **directories** at the current level by **name**, **date modified** (folder mtime), **aggregate size** (sum of file sizes under subtree, configurable depth or “full subtree”), **file count**, and **image file count** (per extension allowlist) | UI shows progress/cached values for expensive modes; sort order stable on tie |
| FR-BR-07 | **Folder metrics cache:** optional persistent cache invalidating on observed changes where feasible | Clearing cache user-controlled (ties to FR-ST-03) |

### 4.2 Viewing

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-VW-01 | **Fullscreen** viewer with fit modes: fit, fill, 1:1, custom zoom | Mode persists per session or per setting (spec in design) |
| FR-VW-02 | **EXIF orientation** respected for JPEG/WebP where present | Portrait shots display upright |
| FR-VW-03 | **Animated GIF/WebP** plays by default with pause toggle | CPU use bounded by frame skip policy when configured |
| FR-VW-04 | **Previous/next** in current context (folder, filter, or slideshow queue) | Latency targets in §6 (NFR-PF-02) |
| FR-VW-05 | **Histogram / basic metadata** optional panel (P1 if not MVP) | If MVP deferred, document as P1 with ID kept |

**Formats (MVP baseline):** JPEG, PNG, GIF (incl. animated), BMP, WebP (incl. animated if supported by stack), TIFF (common subsets), HEIF/HEIC *best-effort* (platform dependent—explicitly “optional” if codec missing). ICO optional.

### 4.3 Sort mode (culling / archive pipeline)

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-SR-01 | Per-image **state**: at least **Keep**, **Delete**, **Unset** (tri-state or binary+unset per design) | State visible in UI; **keyboard and/or mouse** bindings cycle or direct-set |
| FR-SR-02 | **Advance** automatically or manually after flag (design choice; both acceptable if configurable) | User can process **100+ images** using **mouse-only** *or* **keyboard-only** per chosen profile |
| FR-SR-03 | **Summary** before destructive ops: counts keep/delete/unset | Blocking dialog; unset handling policy explicit (block / treat as keep / treat as delete) |
| FR-SR-04 | **Delete unflagged** means: delete all without Keep OR delete all marked Delete—**exact semantics fixed in UX copy** to avoid ambiguity | Acceptance tests for both interpretations; pick one for MVP |
| FR-SR-05 | **Move parent directory** to user-configured **archive root**, preserving relative structure or flattening—**configurable**. **Source may be local or NAS; destination may be NAS**; cross-root moves must complete atomically **per batch** with clear partial-failure reporting | Integration test on temp FS + **network fixture** (optional CI skip) |
| FR-SR-06 | **Rename rules** before move: patterns may include sequence counters, date from EXIF, original name tokens, parent folder name | Dry-run preview mandatory |
| FR-SR-07 | **Collision policy** on rename/move: skip, auto-suffix, abort batch | User-selectable |
| FR-SR-08 | **Recycle Bin** use on Windows by default for deletes | Confirm in acceptance tests |
| FR-SR-09 | **Operation log** (JSON or CSV append-only) optional MVP; recommended P0 if agents implement safety | Log path configurable |

*Designer note:* Resolve FR-SR-04 copy early (“delete everything not explicitly kept” vs “delete only marked delete”).

### 4.4 Slideshow (recursive random, minimal indexing)

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-SL-01 | User selects **root folder**; slideshow includes **all nested images** | Verified on fixture tree with mixed depths |
| FR-SL-02 | **Random order** without requiring full directory enumeration at start | Startup begins within slideshow startup budget (define alongside NFR-PF-01 in test plan); enumeration continues in background |
| FR-SL-03 | **Uniformity policy** documented: see §5 algorithms; MVP may implement **Algorithm A** with known bias tradeoff | Product copy discloses behavior |
| FR-SL-04 | **Reshuffle** / “new random session” clears session memory appropriately | No unbounded memory growth over days-long run |
| FR-SL-05 | **Skip** unsupported; **delete** optionally disabled in slideshow (safety) | Destructive **inputs** off by default; slideshow delete uses one **ContentDialog** before delete; if the Recycle Bin is unavailable, permanent delete proceeds **without** a second in-app confirm for that command |
| FR-SL-06 | **Slideshow scope toggle:** a **single bindable command** switches between **Tree session** and **Folder scope**. **Tree session:** `next`/`previous` follow the **recursive random** walk rooted at the **original slideshow root** (FR-SL-01–03). **Folder scope:** `next`/`previous` move only among **sibling image files** in the **parent directory of the image currently on screen** (order: **same rule as image list** for that folder—default **name ascending** if none chosen) | **Persistent visible/minimal indicator** of active scope; toggle latency ≤ **FR-SL-02** startup class (instant feel); works when session started deep under a huge tree |
| FR-SL-07 | **Resume Tree session:** leaving **Folder scope** restores **Tree session** with **prior random state preserved** (pending queue / seen set / enumerator position / PRNG state—implementation-defined) so the user does **not** implicitly restart the whole-archive shuffle unless they invoke **FR-SL-04** | Acceptance: toggle in/out repeatedly; advancing in Tree session **does not** reset on return; reshuffle still explicit |

### 4.5 Input: keyboard, mouse, gaming devices, gestures

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-IN-01 | **Unified binding model:** any command can be assigned to **keyboard** (incl. chords), **mouse button** (left/middle/right, **X1/X2**, and additional buttons **when the OS/runtime exposes discrete button ids**), **wheel** delta (optionally with **Ctrl/Alt/Shift** modifiers), and **mouse gestures** (**P1**; stroke patterns → commands) | One settings surface lists all commands; **import/export JSON** includes device-specific bindings |
| FR-IN-02 | **Default profiles:** ship at least **Mouse-first sort/browse** and **Keyboard-first**; user can duplicate/edit | Profile switch without restart (preferred) or documented limitation |
| FR-IN-03 | **Mouse-first reference flow** for decision sorting: **wheel** advances through items in the **large directory** (list or preview—design picks); **primary/side clicks** set flags per binding; **mouse buttons 4–5** (typical **browser back/forward**) available for **high-level actions** (e.g. **commit delete**, **move/rename to archive**, **undo last flag**) | Exact default bindings are a **design deliverable** documented in the checklist |
| FR-IN-04 | **Gaming mice / high button count:** support as many buttons as the platform delivers through standard input APIs; **document practical limits** (e.g. vendor drivers that collapse to duplicate events) | Test matrix includes at least **5-button** mouse; spike note for **raw input** if needed |
| FR-IN-05 | **Conflict detection** across bindings and **modifier+wheel** chords | Clear warning; block ambiguous duplicates for destructive actions by default |
| FR-IN-06 | **Accessibility:** high contrast theme optional; scalable UI text (**P1** acceptable); ensure **not gesture-only** for any core action | WCAG-oriented stretch goal |

### 4.6 Archive path inference (secondary; target **P1**)

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-AR-01 | **Suggest destination** under user’s **archive root** by analyzing **existing archive tree**: folder **naming patterns** (dates, sources, artists, etc.), **depth conventions**, and sibling distributions | Shows **ranked suggestions** with **confidence**; user **accepts or edits** path before move |
| FR-AR-02 | **Heuristics** run **locally** on paths/mtimes only unless user opts into deeper scans; must stay responsive on large archives (incremental / background) | Cancellation; progress |
| FR-AR-03 | **Manual override always wins;** never auto-move without confirmation | Regressions tested |
| FR-AR-04 | **User-defined rules (P2):** save templates (e.g. path patterns, preferred depth) informed by observed archive habits | Optional |

### 4.7 Settings and persistence

| ID | Requirement | Acceptance criteria |
|----|-------------|-------------------|
| FR-ST-01 | Settings file location documented (portable vs AppData) | Clean install/reinstall preserves paths user chooses |
| FR-ST-02 | Recent roots, last window geom, last sort archive target, **last input profile** | Restored on launch |
| FR-ST-03 | **Clear cache** control if thumbnail/index/**folder metrics** caches exist | User-visible button |

---

## 5. Slideshow discovery algorithms (normative options for engineering)

**Algorithm A — Streaming random with lookahead (MVP candidate):** Background enumerator walks tree incrementally; slideshow draws next item from a **reservoir** or shuffled buffer of discovered files; may bias early files unless buffer minimum reached before start (configurable “minimum pool size”). *Tradeoff:* slight startup delay vs fairness.

**Algorithm B — Periodic partial shuffle:** Walk tree in random path order; amortized uniform over long runs; bounded memory.

**Algorithm C — Optional index (P2):** User-triggered or scheduled lightweight index (paths + mtimes) stored locally; true uniform shuffle and instant next; **never required** for P0 if G-01 holds.

**Folder scope (FR-SL-06):** When **Folder scope** is active, navigation is **not** drawn from the tree-random reservoir; the app builds (or reuses) an **ordered sibling list** for the current parent directory only. **Tree session** state **continues in the background** per **FR-SL-07**.

**Requirement:** PRD mandates **FR-SL-02** satisfaction via A or B in P0; C is additive.

---

## 6. Non-functional requirements (NFR)

| ID | Category | Requirement |
|----|----------|-------------|
| NFR-PF-01 | Performance | Cold open on folder with 10k images: first image visible ≤ **2 s** on **reference local SSD*** (NAS: **best-effort**; show progress if slower—tie to NFR-PF-06) |
| NFR-PF-02 | Performance | Next/previous: **≤ 150 ms** p95 for ≤32MP JPEG on **reference local SSD***; on **NAS**, target **≤ 500 ms** p95 under “healthy” lab SMB (define in test plan) OR show **non-blocking** loading state without freezing input |
| NFR-PF-03 | Scalability | Memory stable when traversing 100k+ file trees: **streaming enumeration**, no full path list in RAM unless user opts in |
| NFR-PF-04 | Disk | Large images: **decode budget**—show progressive or downsampled preview if decode exceeds threshold (design) |
| NFR-PF-05 | Responsiveness | **Folder aggregate** sorts (FR-BR-06): UI remains interactive; heavy metrics **stream** results with cancel; full subtree scan **never blocks** main thread indefinitely |
| NFR-PF-06 | Network storage | **UNC / mapped SMB** paths supported; **timeout/retry** policy for I/O; **read-ahead** optional for next slideshow image; UI **never** hard-freezes on slow shares—cancel/feedback for long ops |
| NFR-RL-01 | Reliability | No silent data loss: failed deletes/moves reported with retry and log entry |
| NFR-RL-02 | Reliability | **Transient NAS errors** (disconnect, timeout): user-visible message; safe retry; **no partial state** that implies success |
| NFR-SC-01 | Security | **P0–P3:** **No third-party cloud / telemetry** calls; **LAN/NAS file I/O** via OS is in scope. **Defensive path handling** (symlink loops, MAX_PATH mitigation via long paths on Windows). **P4 (FR-IG):** **user-initiated outbound HTTPS** to parsed hosts is in scope; still **no unsolicited telemetry** |
| NFR-IN-01 | Installer | **P0 baseline:** portable **x64 zip** for dev/beta; **MSIX** optional **P1** (Store / auto-update). File associations **optional P1**. See [`docs/tech-design/architecture-bootstrap.md`](../../docs/tech-design/architecture-bootstrap.md). |

\*Define **reference HW** and **NAS lab setup** in test plan (e.g. mid-range laptop + local SSD; SMB share on wired LAN).

---

## 7. Data, privacy, and safety

- **No cloud dependency** for core features; processing runs on the user machine; libraries may live on **LAN/NAS** (§0, NFR-PF-06). No telemetry required for MVP (if added later: opt-in).
- **Destructive operations:** Use Recycle Bin where possible; “Hold Shift for permanent delete” pattern optional.
- **Symlinks/junctions:** Normative policy in [`docs/design-decisions/symlink-junction-policy.md`](../../docs/design-decisions/symlink-junction-policy.md).
- **Permissions:** Read-only media: block delete with clear error.

---

## 8. Platform strategy

### 8.1 Windows (primary)

- Target: **Windows 10/11** x64; **long path** support documented.
- Storage: **local volumes** and **SMB** (mapped drive letter or **UNC**). **Credential / offline / mid-session disconnect** behavior: see [`docs/test-plan-nas-smb-unc.md`](../../docs/test-plan-nas-smb-unc.md) and [`docs/design-decisions/mvp-assumptions-ux.md`](../../docs/design-decisions/mvp-assumptions-ux.md).
- Input: **keyboard + mouse** (incl. **XButton1/XButton2** and additional buttons per FR-IN-04); **optional tablet pen** later.

### 8.2 Android (future)

**Portability requirements (NFR-AN-01..03):**

- **NFR-AN-01:** Core domain logic **free of Win32-only APIs** (file I/O behind interfaces).
- **NFR-AN-02:** UI layer swappable; **no hard dependency** on desktop-only windowing in view models.
- **NFR-AN-03:** Touch-first mapping mirrors **desktop input intents** (swipes = next/prev; long-press = flag); binding model conceptually aligns with FR-IN-* where platform allows.

**Storage note:** Scoped storage on Android may force “SAF” document trees; PRD accepts that **archive move** semantics differ per platform.

---

## 9. Video extension (Phase 3)

**Goal:** Parity where feasible: browse tree, fullscreen play, random recursive slideshow, sort pipeline with **transcoding disabled** in MVP video phase.

| ID | Requirement |
|----|-------------|
| FR-VD-01 | Containers/codecs per platform capability matrix (documented) |
| FR-VD-02 | Thumbnail generation async; never block slideshow start |
| FR-VD-03 | Destructive actions require stronger confirmation when triggered from slideshow | Same policy for keyboard, mouse, and gestures |

---

## 10. Forum / HTTP gallery ingest (Phase 4 extension)

**Goal:** Optional **network-assisted acquisition**: user supplies an **HTTP(S) URL** (typically a **forum thread**, index page, or gallery page). The app **fetches** the page, **traverses** links to **common image hosts** and **direct image URLs**, resolves to **downloadable assets**, and saves files under the user’s **staging root** (§0) inside a **new subfolder** named from **forum/thread context** (title, board, date—**sanitized** for filesystem rules).

**Comparable products (benchmark / parity research):** **VRipper** ([vripper-project](https://github.com/dev-claw/vripper-project))-class forum/gallery rippers, **[JDownloader 2](https://jdownloader.org/jdownloader2)** (link containers, host plugins, queues, resumable downloads), and similar **bulk HTTP acquisition** tools. Use them to judge **user expectations** (preview, host coverage, retries, naming)—**not** as code or UI to replicate. ImageHoard P4 stays **narrow**: forum/gallery URL → **staging** → existing **sort/archive** pipeline.

**Out of scope for first P4 milestone (unless promoted):** paywalled sites without user-supplied auth, CAPTCHA farms, DRM, or **bulk** site-wide crawling beyond the single supplied URL’s reachable graph **within a configurable depth/link budget**.

| ID | Requirement | Acceptance criteria |
|----|-------------|---------------------|
| FR-IG-01 | **Paste or enter URL** to start an ingest job; supports **HTTPS**; shows **obvious outbound network** consent on first use | Clear UX; cancellable job |
| FR-IG-02 | **HTML parse & link extraction** from the page; follow **internal** thread/pagination links **within budget** (max pages, max links—configurable) | Fixture tests with saved HTML; no unbounded memory |
| FR-IG-03 | **Image host resolution:** pluggable rules for **common hosts** (hotlink pages → direct file URL); **fallback** to generic `<img src>`, `<a href>` image extensions | Document supported hosts v1; graceful skip for unknown with log |
| FR-IG-04 | **Staging output:** writes to user-configured **staging parent**; creates **one folder per job** named from **context** (e.g. sanitized thread title + short hash or date if collision) | Windows-invalid chars stripped; path length safe |
| FR-IG-05 | **Preview before download:** user reviews **estimated file list** (count, hosts, sizes if known); confirm to start | Dry-run mode |
| FR-IG-06 | **Download engine:** concurrent fetches with **rate limit** and **retry**; per-file failure does not abort whole job unless configured | Resumable or idempotent re-run policy documented |
| FR-IG-07 | **Dedup within job** by URL and optional content hash; **collision** filenames get safe suffix | Tests for duplicate thumbnails vs fullres |
| FR-IG-08 | **Session log** (URL, timestamp, files OK/fail) append-only; ties to FR-SR-09 patterns where sensible | User-readable path |

**NFR-IG (Phase 4)**

| ID | Requirement |
|----|-------------|
| NFR-IG-01 | **Security:** TLS by default; optional **cert pinning** out of scope; block local network SSRF to **metadata only**—document risk for file:// and `localhost` in HTML |
| NFR-IG-02 | **Compliance posture:** in-app **disclaimer**: user must obey **site ToS** and copyright; app provides tool only |
| NFR-IG-03 | **Performance:** host modules must not block UI; progress/cancel; default **conservative** concurrency for NAS staging targets |

**Dependency note:** P4 **revises** **NFR-SC-01** for ingest features only: **outbound HTTPS** to user-initiated hosts is allowed; still **no unsolicited third-party telemetry**.

---

## 11. Metrics and acceptance gates

- **Performance benchmarks** scripted against sample corpuses (1k, 10k, 50k files) on **local** and **NAS-mounted** corpora where feasible.
- **Usability:** **Two tracks:** (1) **mouse-first**—core sort/browse tasks without keyboard; (2) **keyboard-first**—same tasks without mouse; timed against profile checklists.
- **Crash-free sessions** target during beta: **≥99.5%** (adjust per team norms).

---

## 12. Traceability index: implementation source of truth

### 12.1 Precedence

When narrative in this PRD and repository `docs/` disagree, **prefer `docs/`** (design decisions + technical design) and **`defaults/`** shipped JSON until this PRD is amended. Agents implement against **FR/NFR IDs** with acceptance drawn from linked ADRs.

### 12.2 Former PRD “open questions” — resolved in `docs/`

| Topic | Document |
|--------|----------|
| FR-SR-04 semantics + UX copy | [`docs/design-decisions/FR-SR-04-batch-delete-semantics.md`](../../docs/design-decisions/FR-SR-04-batch-delete-semantics.md) |
| MVP thumbnail grid vs list | [`docs/design-decisions/mvp-thumbnail-scope.md`](../../docs/design-decisions/mvp-thumbnail-scope.md) |
| FR-IN-03 defaults (wheel, flags, X1/X2 double-confirm) | [`docs/design-decisions/input-default-profiles.md`](../../docs/design-decisions/input-default-profiles.md); shipped JSON [`defaults/input-profiles/`](../../defaults/input-profiles/) |
| FR-AR-01 v1 heuristics vs P2 learning | [`docs/design-decisions/archive-path-inference-fr-ar.md`](../../docs/design-decisions/archive-path-inference-fr-ar.md) |
| FR-SL-06 / FR-SL-07 Tree vs Folder scope + single-image folder | [`docs/design-decisions/slideshow-tree-vs-folder-scope.md`](../../docs/design-decisions/slideshow-tree-vs-folder-scope.md) |
| Slideshow Algorithm A + fairness copy | [`docs/design-decisions/slideshow-algorithm-p0.md`](../../docs/design-decisions/slideshow-algorithm-p0.md) |
| Folder aggregate metrics + cache | [`docs/design-decisions/folder-aggregate-metrics-model.md`](../../docs/design-decisions/folder-aggregate-metrics-model.md) |
| NAS/SMB/UNC test matrix + failure UX | [`docs/test-plan-nas-smb-unc.md`](../../docs/test-plan-nas-smb-unc.md) |
| Reference HW + corpora | [`docs/test-plan-reference-hw.md`](../../docs/test-plan-reference-hw.md) |
| P4 ingest architecture + starter host list | [`docs/tech-design/p4-forum-ingest-host-modules.md`](../../docs/tech-design/p4-forum-ingest-host-modules.md) |
| Stack, image pipeline, CI, NFR-IN-01 | [`docs/tech-design/architecture-bootstrap.md`](../../docs/tech-design/architecture-bootstrap.md) |
| Settings paths (portable vs LocalAppData) | [`docs/design-decisions/fr-st-01-settings-persistence.md`](../../docs/design-decisions/fr-st-01-settings-persistence.md) |
| Extended `commandId` registry (P0 vs P1 / chrome) | [`docs/design-decisions/command-registry.md`](../../docs/design-decisions/command-registry.md) |
| Keyboard key string vocabulary | [`docs/design-decisions/keyboard-key-identifiers.md`](../../docs/design-decisions/keyboard-key-identifiers.md) |
| Symlink / junction policy | [`docs/design-decisions/symlink-junction-policy.md`](../../docs/design-decisions/symlink-junction-policy.md) |
| FR-SR-06 / FR-SR-07 rename + collision | [`docs/design-decisions/rename-move-collision-fr-sr-06-07.md`](../../docs/design-decisions/rename-move-collision-fr-sr-06-07.md) |
| FR-SR-09 operation log | [`docs/design-decisions/operation-log-fr-sr-09.md`](../../docs/design-decisions/operation-log-fr-sr-09.md) |
| Dual monitor, i18n, themes, offline NAS MVP | [`docs/design-decisions/mvp-assumptions-ux.md`](../../docs/design-decisions/mvp-assumptions-ux.md) |

### 12.3 Remaining open or deferred (not blocking P0 core)

| Item | Status |
|------|--------|
| **Gesture engine (FR-IN gestures, P1)** | Built-in vs third-party hooks; security review at P1 kickoff—no gesture-only core actions in P0 per FR-IN-06. |
| **FR-IG (P4)** exact v1 host allowlist beyond starter set | Product + legal review; [`p4-forum-ingest-host-modules.md`](../../docs/tech-design/p4-forum-ingest-host-modules.md) lists initial candidates. |
| **FR-IG-04** folder naming template detail | Refine with first ingest UX pass; sanitize rules in P4 tech design apply. |
| **FR-IG** auth for private threads | Post–first P4 milestone: cookies / header file vs embedded login—out of P0–P3. |

---

## 13. Traceability for agentic development

**Conventions**

- Implement against **IDs** (FR-*, NFR-*, G-*); agents cite IDs in commits/PRs.
- Each PR maps to **1+ IDs**; no orphan refactors without ID.
- **Acceptance:** For each FR, add automated test where possible (filesystem temp harness); manual test script for **input profiles** (keyboard + mouse).

**Minimum MVP backlog order (suggested)**

1. FR-BR-01, FR-VW-01, FR-VW-02, FR-VW-04  
2. FR-SL-01–07 (+ §5 Algorithm A/B)  
3. FR-SR-01–03, FR-SR-05–08  
4. FR-IN-01–03, FR-ST-01–02  
5. FR-BR-06–07 (folder sorts + cache)  
6. FR-SR-06–07 (rename preview)  
7. NFR-PF-01–06, NFR-RL-01–02 hardening  
8. FR-AR-* (P1), FR-IN gestures (P1)  
9. **P4:** FR-IG-01–08, NFR-IG-01–03 (forum/HTTP ingest—after core app stable)  

## 14. Glossary

- **Sort mode:** Decision workflow (keep/delete) with batch filesystem effects—not “sorting” as in ordering pixels.
- **Lazy discovery:** Enumerate tree incrementally without building a full in-memory file list first.
- **Archive root:** Configured destination base path for completed folder moves.
- **Folder aggregate sort:** Ordering sibling folders by statistics of their contents (size, counts) rather than only folder name.
- **Tree session:** Slideshow navigation mode where next/previous participate in the **recursive random** walk from the **user-chosen slideshow root**.
- **Folder scope (slideshow):** Temporary mode where next/previous move only among **sibling images** in the directory of the file currently shown; **Tree session** state is **suspended**, not discarded (FR-SL-07).
- **Staging folder:** User-chosen tree where **downloaded galleries** accumulate before sort (**not** necessarily Windows “Downloads”).
- **Gallery (acquisition batch):** A folder or bundle of images from one download, typically **~50–200+** files (variable).
- **HTTP / forum ingest (P4):** User-initiated download of images discovered from a **forum or gallery URL** into **staging**, using **host-specific** link resolution (**FR-IG-***).

---

*End of PRD v1.5 — §10 adds comparable ingest tools (VRipper, JDownloader 2).*
