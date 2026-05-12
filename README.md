# ImageHoard

Windows-first tool for browsing, sorting, and slideshowing large image libraries (see PRD in `.cursor/plans/`).

## Build (Windows)

Prerequisites: [.NET 8 SDK](https://dotnet.microsoft.com/download/dotnet/8.0) and the **Windows application development** workload in Visual Studio 2022 (or equivalent Windows App SDK / WinUI build support).

```powershell
dotnet restore ImageHoard.sln
dotnet build ImageHoard.sln -c Debug
dotnet test ImageHoard.sln -c Debug
```

Run the WinUI host (x64):

```powershell
dotnet run --project src\ImageHoard.App\ImageHoard.App.csproj -c Debug
```

**Windows App Runtime (unpackaged WinUI):** If the app fails to start with a missing-runtime message, install the runtime that matches the app’s Windows App SDK line (currently **2.0.1**). From an **elevated** PowerShell in the repo root:

```powershell
powershell -ExecutionPolicy Bypass -File .\scripts\install-windows-app-runtime.ps1
```

Or run the installer Microsoft ships for that build (after downloading from the same URL the script uses): accept the UAC prompt when asked.

## Docs

All engineering decisions, test matrices, and technical design live under **[`docs/`](./docs/README.md)**.

## Stack (P0 baseline)

See **[`docs/tech-design/architecture-bootstrap.md`](./docs/tech-design/architecture-bootstrap.md)** (.NET + WinUI 3 + WIC, portable zip distribution for early builds).

## PRD to code

- Sort batch delete semantics: [`docs/design-decisions/FR-SR-04-batch-delete-semantics.md`](./docs/design-decisions/FR-SR-04-batch-delete-semantics.md)
- Slideshow Algorithm A + fairness copy: [`docs/design-decisions/slideshow-algorithm-p0.md`](./docs/design-decisions/slideshow-algorithm-p0.md)
- Default keyboard/mouse profiles: [`docs/design-decisions/input-default-profiles.md`](./docs/design-decisions/input-default-profiles.md)
- Shipped default profile JSON: [`defaults/input-profiles/`](./defaults/input-profiles/) (`index.json`, `keyboard-only.v1.json`, `mouse-only.v1.json`, schema)
- **Agent instructions:** [`AGENTS.md`](./AGENTS.md)

Application code lives under [`src/`](./src/) and [`tests/`](./tests/).
