# Architecture bootstrap — Windows desktop (P0)

**Status:** Locked baseline for first implementation pass  
**Related:** NFR-IN-01, NFR-PF-*, NFR-SC-01 (P0–P3), PRD §8.1, FR-BR-*, FR-VW-*

## Summary

| Area | Choice | Rationale |
|------|--------|-----------|
| Runtime | **.NET 8+** (LTS), **C#** | Strong async I/O for NAS, tooling, test ecosystem |
| UI shell | **WinUI 3** (Windows App SDK) | First-party Windows 10/11, modern windowing, viable path to MSIX (P1) |
| Image decode | **WIC** (`Windows.Graphics.Imaging` / `BitmapDecoder`) as primary | Broad format coverage on Windows, EXIF orientation; aligns with PRD MVP formats |
| HEIF / HEIC | **Best-effort** | If codec missing, show skip + status (PRD §4.2); no silent install of proprietary codecs from app |
| HTML (P4 only) | **AngleSharp** (or similar) | Already assumed in [p4-forum-ingest-host-modules.md](./p4-forum-ingest-host-modules.md) |
| Unit / integration tests | **xUnit** + **FluentAssertions** (optional) | Common .NET CI defaults |
| CI | **GitHub Actions** `windows-latest`: `dotnet restore`, `dotnet build`, `dotnet test` | Matches [test-plan-reference-hw.md](../test-plan-reference-hw.md) automation notes |

## Repository layout (suggested)

```
src/
  ImageHoard.App/          # WinUI host, views, platform services
  ImageHoard.Core/          # Domain: paths, slideshow reservoir, sort state (NFR-AN-01: no Win32 here)
tests/
  ImageHoard.Tests/      # xUnit; filesystem temp harnesses per PRD §13
docs/                      # existing ADRs
defaults/                  # shipped input profiles
```

Adjust names if the solution template differs; keep **Core** free of Win32-only APIs behind abstractions (`IFileSystem`, `IImageDecoder`).

## Long paths and UNC

- Enable **long path** awareness in app manifest + `\\?\` / `\\?\UNC\` normalization in the filesystem abstraction (PRD §6 NFR-SC-01).
- Treat **mapped drives** and **UNC** as first-class in tests (see [test-plan-nas-smb-unc.md](../test-plan-nas-smb-unc.md)).

## NFR-IN-01 — Distribution

| Phase | Channel |
|-------|---------|
| **P0 / dev / beta** | **Portable x64 zip** (self-contained or framework-dependent publish); no Store requirement |
| **P1** | **MSIX** optional for Microsoft Store / smoother updates; **file associations** optional P1 |

Portable layout and settings paths: [fr-st-01-settings-persistence.md](../design-decisions/fr-st-01-settings-persistence.md).

## Decode budget (NFR-PF-04)

Implement **downscaled decode** for list thumbs and fast preview when full decode exceeds a configurable threshold (exact ms TBD in implementation; document in release notes once measured on Reference A).

## Alternatives considered (short)

- **WPF:** Mature, fewer WinUI rough edges; still valid if team prefers XAML-only. WinUI kept for long-term alignment with Windows App SDK.
- **Avalonia:** Cross-platform earlier; more risk for gaming-mouse / Win32 edge cases in P0—revisit if Android port timeline moves up.
- **SkiaSharp-only decode:** Possible; WIC preferred first to lean on OS codecs for TIFF/HEIF variance.

## Revision

| Date | Change |
|------|--------|
| (initial) | Baseline stack + NFR-IN-01 |
