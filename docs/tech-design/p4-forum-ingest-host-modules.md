# P4 Forum / HTTP ingest — host resolver modules and security (FR-IG, NFR-IG)

**Status:** Technical design for Phase 4  
**Related:** PRD §10; `forum-ingest-host-modules`

## Architecture

```
IngestJobController
  ├── HttpFetchService (TLS 1.2+, redirects limited, User-Agent configurable)
  ├── HtmlLinkExtractor (AngleSharp or similar; max HTML size cap)
  ├── LinkFollowPolicy (same registrable domain, depth, max pages)
  └── HostResolverPipeline (ordered list of IHostModule)
        ├── each module: TryResolve(Uri pageUrl, HtmlContext) -> IEnumerable<DirectImageUri>
        └── fallback: GenericImgAnchorScanner
DownloadQueue (rate limit, retry, disk writer to staging per FR-IG-04)
```

## `IHostModule` contract

```csharp
// Pseudocode — language-agnostic contract
interface IHostModule {
  string Id { get; }              // e.g. "imgbox.com"
  bool CanHandle(Uri uri);
  Task<IReadOnlyList<Uri>> ResolveDirectImagesAsync(Uri pageUri, CancellationToken ct);
}
```

- Modules **must not** execute arbitrary scripts from HTML; **URL parsing + known API patterns only**.
- **Timeout** per module call (e.g. 10 s).

## v1 host module list (FR-IG-03 starter set)

Prioritize hosts common in forum posts (expand iteratively):

1. Direct file extensions in `<a href>` / `<img src>` (fallback).
2. **imgbox.com**, **pixhost.to**, **imagebam.com**, **postimg.cc** (representative; exact list from product + legal review).

Each module ships as **separate DLL** or folder `Plugins/Hosts/*.dll` for isolation (optional).

## Rate limiting (NFR-IG-03, FR-IG-06)

- Default **global concurrency:** 2 simultaneous image downloads; **per-host:** 2.
- **Backoff:** HTTP 429 → Respect `Retry-After` or exponential 2^n seconds max 120 s.

## SSRF / abuse (NFR-IG-01)

- **Allowlist schemes:** `https`, `http` (user toggle; http off by default).
- **Blocklist:** `file://`, `ftp://`, **`http://127.0.0.1`** / `localhost` in **fetched** HTML resolved links for P4 v1 (document: may reduce legit intranet image servers).
- **No** server-side request to RFC1918 from **cloud-hosted** future versions — N/A for desktop-only MVP P4.

## Logging (FR-IG-08)

- JSON lines: `{ "jobId", "sourceUrl", "startedUtc", "entries": [ { "url", "ok", "bytes", "error" } ] }`.

## Compliance (NFR-IG-02)

- First-run consent + **Settings** checkbox: user acknowledges **ToS** and **copyright** responsibility.

## Tests

- Fixture HTML files in `tests/fixtures/ingest/*.html` → golden extracted URL lists.
- Mock HTTP 429 → verify backoff.
- SSRF: HTML contains `file:///C:/Windows` link → **skipped** with log.
