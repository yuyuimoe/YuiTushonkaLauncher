# Architecture

## Projects

Solution: `YuiTushonkaLauncher.slnx`. Five projects, dependencies point inward only.

| Project | Responsibility | References |
|---|---|---|
| `YuiTushonkaLauncher.Core` | Domain model, service interfaces, business rules (dependency resolution, link policy, patch planning, package manifests). No I/O beyond abstractions. | — |
| `YuiTushonkaLauncher.Patching` | Isolates `SharpHDiffPatch.Core`: `.delta` discovery, apply, backup, restore, verification. | Core |
| `YuiTushonkaLauncher.Infrastructure` | EF Core + SQLite, Forge API client, file-system/link layer, DLL metadata readers, process management, Proton/umu, archiver. | Core, Patching |
| `YuiTushonkaLauncher.Ui` | Avalonia views, view models, locale JSON, theme. Composition root lives here. | Core, Infrastructure |
| `YuiTushonkaLauncher.Tests` | Unit tests (Core/Patching/Infrastructure), HTTP-mocked integration tests, `Avalonia.Headless` UI tests. | all |

Rules:

- `Core` never references `Infrastructure`; contracts live in Core, implementations in Infrastructure.
- `Ui` talks to Core interfaces only; DI registration is the single place that knows Infrastructure.
- `Patching` is separate from Infrastructure so the patch engine can be swapped (`hdiff` → another
  delta format) without touching file-system or DB code.

## Layered responsibilities

```
Ui (Avalonia, MVVM, i18n)
   │ depends on
Core (entities, IRule/IService contracts, pure logic)
   ▲ implements
Infrastructure (EF Core, Forge HTTP, filesystem/link, MetadataLoadContext, processes, Proton)
   │ uses
Patching (SharpHDiffPatch wrapper)
```

## The file layer (the heart of the app)

An instance is a directory that *looks* like a full SPT install but shares everything unchanged with a
base install. Three primitives, all in `IFileLinkService`:

| Operation | Use |
|---|---|
| `CreateHardLink(src, dst)` | share a file that must stay byte-identical across instances |
| `Detach(path)` / copy-on-write | turn a link into a private file **before** writing to it |
| `CopyTree(src, dst)` | private trees (configs, profiles, mods) |

Interop reality: .NET has no managed hard-link API. `CreateHardLink` needs `link()` on Unix and
`CreateHardLinkW` on Windows (P/Invoke), plus `errno`/`GetLastError` mapping to domain errors
(`EXDEV`, `EPERM`, `EMLINK`, `ENOSPC`, `ERROR_NOT_SAME_DEVICE`). Link counts (`st_nlink`,
`GetFileInformationByHandle`) are needed to know whether a file is shared before overwriting.

### Link policy

| Path | Policy |
|---|---|
| `EscapeFromTarkov.exe`, `UnityPlayer.dll`, `EscapeFromTarkov_Data/**` | hard link |
| `EscapeFromTarkov_Data/Managed/Assembly-CSharp.dll` | **copy** — the patch rewrites it ([D5](04-decisions.md#d5-assembly-csharpdll-is-never-linked)) |
| any `*.spt-bak` | copy (belongs to the instance's patch state) |
| `BepInEx/core/**`, `BepInEx/patchers/**`, `winhttp.dll`, `doorstop_config.ini` | hard link (runtime, shared) |
| `BepInEx/plugins/**` | per-instance directory; **never** linked between instances |
| `BepInEx/config/**` | per-instance, copied |
| `SPT_Runtime/SPT_Data/**` (except `configs/`) | hard link |
| `SPT_Runtime/SPT_Data/configs/**` | per-instance, copied |
| `SPT_Runtime/user/**` (logs, profiles, mods, cache, sptsettings, sptappdata, certs) | per-instance, created empty or copied |
| `SPT_Runtime/SPT.Server*`, `SPT.Launcher*` | hard link (we replace the launcher; never launched directly) |

The base install itself is never modified. Anything not in the table defaults to *copy* — a new path
that appears in a future SPT release must never be silently shared.

### Copy-on-write invariant

> **Never write through a link.** Before any write, rename/compare `st_nlink`; if the file is shared,
> copy it to a temp file in the same directory, `fsync`, then atomically rename over the original.

Enforced in one place: `ILinkAwareFileWriter.Write(path, bytes/stream)`, used by every writer
(config edits, patch output, mod update, import). Phase 4 ships a test that fails if a shared file's
inode changes.

## Data flow

```
Forge API (sp-mod.com/api/v0)
   │  HttpClient + rate limiter + response cache
   ▼
ISptForgeClient ──► SqliteCache / entities (EF Core)
   │
   ▼
Domain services (ModCatalog, DependencyResolver, UpdateChecker)
   │
   ▼
Instance operations (InstanceService, ModInstaller, FilePatcher, Launcher)
   │  IFileLinkService + ILinkAwareFileWriter
   ▼
Instance directory on disk  (+ SPT server process + game process)
```

## Process model

- **SPT server**: long-lived child process (`SPT.Server[.Linux]`), stdout/stderr redirected into the
  log pipeline, graceful shutdown (server's own shutdown endpoint, then `SIGTERM`, then kill after a
  timeout). The server must be up before the game is launched and is shut down with the app.
- **Game**: `EscapeFromTarkov.exe` on Windows; on Linux through `umu`/Proton with the selected prefix.
- **Helpers**: `winecfg`/`winetricks`/`regedit` inside the prefix for the advanced Linux panel.
- Every child gets its own log source and its exit code is captured; no orphaned processes on app
  exit or crash (job object on Windows, `PR_SET_PDEATHSIG`-equivalent handling on Linux).

## Logging

`ILogService` receives tagged records from four producers (app, server, game, Proton). Serilog writes
per-source rolling files under `user/logs/`; when a file rotates it is gzipped and kept in
`user/logs/archive/` with a retention policy (count + age), Prism-Launcher style: the UI shows the
current session and can open the archive for older runs and export any range.
