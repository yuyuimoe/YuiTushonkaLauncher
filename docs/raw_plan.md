# Development Plan — SPT Manager (Instance and Mod Manager for SPT)

> Translated from the original Portuguese draft (2026-xx). Review findings and open questions live in
> [`raw_plan_review.md`](./raw_plan_review.md).

## 1. Project Overview

**SPT Manager** is a **cross-platform desktop application (Windows and Linux)** whose goal is to
**fully replace the official Single Player Tarkov (SPT) launcher**, while also providing advanced
instance and mod management features inspired by **Prism Launcher**.

The manager is responsible for:

- **Managing multiple SPT instances**, combining game files (Tarkov) and SPT files stored in separate
  folders, merged through **hard links** to save disk space.
- **Installing, updating and pinning mod and addon versions**, integrating with the **official Forge
  API** for search, compatibility, dependencies and new-version notifications.
- **Applying the `Assembly-CSharp.dll` binary patch** before launching the game, using
  `SharpHDiffPatch.Core` and `zlib.net`, replicating the official launcher's logic (`SPT.ByteBanger`).
- **Identifying installed mods by GUID** extracted from the compiled DLL (the `ModMetadata` class),
  enabling reliable migration of existing installations and correlation with the API.
- **Exporting and importing instances** through custom archives that contain only metadata and
  modified configuration files (detected by hash), with no binaries.
- **Managing profiles, servers and server-side mods**, with automatic mod synchronization when
  connecting to a server.
- **Running the SPT server in the background**, with a unified log viewer (server, game and Proton)
  and log export.
- **Providing first-class Linux support**, with Proton/Wine management, prefixes, environment
  variables and ecosystem integration (similar to Lutris).

---

## 2. Technical Stack

- **.NET 10** — mandatory because of compatibility with the SPT 4.1+ ecosystem and recommended by
  Avalonia 12.
- **Avalonia 12** + `CommunityToolkit.Mvvm` for the UI (MVVM pattern).
- **`SharpHDiffPatch.Core`** (NuGet) for applying binary patches (`.hdiff`).
- **`zlib.net`** (NuGet) for the compression/decompression operations used by the official launcher.
- **`System.Reflection.MetadataLoadContext`** for inspecting mod DLLs without loading them into the
  current process, extracting the GUID from the `ModMetadata` class.
- **`Microsoft.Extensions.DependencyInjection`** for dependency injection.
- **`Serilog`** for structured logging.
- **`Refit` or `HttpClient`** for Forge API integration.
- **`Microsoft.Data.Sqlite` + `Dapper` or `EF Core`** for local persistence.
- **`SharpCompress` or `System.IO.Compression`** for handling the custom export/import archives.

---

## 3. Solution Architecture

The solution is organized in layers, mirroring the official launcher's separation to guarantee
testability and clear responsibilities.

| Project | Responsibility |
|---|---|
| **`SPTManager.Core`** | Domain models, service interfaces and pure business rules. |
| **`SPTManager.Patching`** | Dedicated library for binary patching (isolating `SharpHDiffPatch.Core` and `zlib.net`). |
| **`SPTManager.Infrastructure`** | Concrete implementations: Forge API client, SQLite repositories, file system/hard links, processes, Proton/Wine integration and DLL metadata reading. |
| **`SPTManager.UI`** (Avalonia 12) | Views, ViewModels and visual resources. |
| **`SPTManager.Tests`** | Unit and integration tests. |

---

## 4. Domain Model

The core entities and value objects the coding agent should implement:

- **`Instance`**: ID, Name, Path, SPT Version, Tarkov Version, Type (Windows/Linux), Execution Profile
  (Proton/Wine), Mod List, Patch State.
- **`Mod`**: GUID (primary key), Name, Author, Installed Version, Pinned Version, Type
  (Server/Client), Dependencies.
- **`Addon`**: Similar to `Mod`, but with its own lifecycle and tables.
- **`ModVersion`**: Version, SPT Compatibility (SemVer), Dependencies, Download URL, Hash.
- **`SptVersion`**: SPT version (e.g. `4.1.0`) and the corresponding Tarkov version.
- **`Dependency`**: Dependent mod/addon, Type (Required/Optional), Minimum Version.
- **`ModList`**: List of mods with pinned versions and sharing metadata (unique hash).
- **`Profile` / `Server`**: Connection settings and the list of mods required for synchronization.
- **`ModMetadata`**: Data extracted from the mod DLL (GUID, Name, Version, Author).
- **`PatchInfo`**: Patch state of an instance (patched or not, patch version, hash of the original
  `Assembly-CSharp.dll`).

> **Note on mod identification:** the GUID extracted from the DLL (`ModMetadata`) is the **canonical
> key** for correlating a locally installed mod with its Forge API entry. It is the basis for migrating
> existing installations, resolving dependencies and checking for updates.

---

## 5. Implementation Phases

The roadmap below is incremental and produces concrete, testable deliverables at every phase.

### Phase 0 — Preparation and Field Research

**Goal:** Consolidate knowledge about the API and the ecosystem.

- Map every Forge API endpoint: `GET /mods`, `GET /mod/{id}`, `GET /mod/{id}/versions`,
  `GET /mods/dependencies`, `GET /mods/updates`, `GET /addons`, `GET /spt/versions` and the public
  list endpoints.
- Document the folder structure of an SPT instance (e.g. `BepInEx/plugins` for client mods,
  `SPT_Runtime/user/mods` for server mods).
- Study Prism Launcher's hard link implementation to replicate the logic (link individual files, never
  directories).
- Study the official SPT launcher (`SP-Tushonka/launcher`, branch `4.1.x-dev`) to understand the patch
  engine (`SPT.ByteBanger`) and the startup flow.

**Acceptance criteria:** internal document with all endpoints, folder structures and the official
launcher flow mapped out.

---

### Phase 1 — Project Foundation and Infrastructure

**Goal:** Create the solution and configure DI, logging and the folder structure.

- Create the solution with the five projects (`Core`, `Patching`, `Infrastructure`, `UI`, `Tests`).
- Configure `CommunityToolkit.Mvvm` in the UI project.
- Configure `Serilog` with console and file sinks.
- Configure the DI container in `App.axaml.cs`.
- Create a named `HttpClient` for the Forge API with a descriptive `User-Agent` (required by the API).

**Acceptance criteria:** Avalonia "Hello World" application running on Windows and Linux, with working
DI and logging.

---

### Phase 2 — Domain Model and Persistence

**Goal:** Implement the entities and the local database layer.

- Create the domain classes and enums (see section 4).
- Implement the `DbContext` (EF Core) or Dapper repositories over SQLite.
- Create the initial migrations: `Instances`, `Mods`, `Addons`, `ModVersions`, `SptVersions`,
  `ModLists`, `PatchInfo`.
- Implement a `SettingsService` for user preferences (default instance path, preferred Proton version,
  etc.).

**Acceptance criteria:** unit tests that create, read and update an `Instance` in the database.

---

### Phase 3 — Forge API Client

**Goal:** Integrate with the official API for all network operations.

- Create interfaces and implementations for `IModService`, `IAddonService`, `ISptVersionService`,
  `IDependencyResolver`, `IUpdateChecker`.
- Implement local caching (in memory or SQLite) to avoid repeated calls and respect the rate limit
  (~300 req/min).
- Implement network error handling and paginated responses.
- Create a `ModListService` to fetch public lists and generate share codes (via hash of `manifest.json`).

**Acceptance criteria:** integration tests (with a mock HTTP layer, e.g. WireMock) simulating mod,
version and dependency lookups.

---

### Phase 4 — Instance Management, Hard Links and State Detection

**Goal:** Create, clone and delete instances with hard links, and detect the patch state.

- Implement `IFileLinkService` with `CreateHardLink(source, destination)` and `CreateSymbolicLink`.
- Create the `InstanceService`:
  - `CreateInstance(sptVersion, tarkovVersion, profile)` — downloads or locates the base files and
    builds the structure.
  - `CloneInstance(source, destination, useHardLinks: true)` — hard links for binaries, copies for
    configuration.
- Implement file system detection: hard links only work within the same volume; warn the user otherwise.
- **Detect whether an existing instance's `Assembly-CSharp.dll` is already patched** (via signature or
  known hash). Store that state in `PatchInfo`.

**Acceptance criteria:** create two instances and verify that the binaries share the same inode (Linux)
or `FileIndex` (Windows), saving disk space.

---

### Phase 5 — Mod Installation and GUID-based Identification

**Goal:** Install mods and identify them unambiguously through the GUID extracted from the DLL.

- Implement `ModMetadataReader` using `MetadataLoadContext` to open the DLL without loading it into the
  runtime. Extract the GUID from the custom attribute on `ModMetadata`.
- **Validation against the Forge API:** after extracting the GUID, query the API for official metadata,
  validate the mod, fetch the latest version and check dependencies.
- **Fallback:** if the DLL has no GUID or the GUID is not found in the API, mark the mod as
  "Unrecognized" and notify the user. Installation may proceed, but without automatic updates or
  dependency resolution.
- Implement `ModInstallerService`:
  - Download the mod from the Forge API.
  - Extract to a temporary folder (with path sanitization, preventing *zip slip*).
  - Determine whether it is a server or client mod.
  - Create hard links for binary files in the instance.
  - Copy configuration files (`.json`, `.ini`, `.toml`, etc.) into the instance, since they are
    instance-specific.
- Implement `ModUpdateService`: compare installed versions with the latest ones from the API and notify.
- Implement **"Pin Version"**: prevent automatic updates of a mod.
- Implement `IntegrityService`: compute the SHA-256 of the original files at install time and store it to
  detect local modifications.

**Acceptance criteria:** install a mod, verify that binaries are hard links, configuration files are
copies, and that the GUID was extracted and correlated with the API.

---

### Phase 6 — Dependencies, Addons and Migration of Existing Installations

**Goal:** Resolve dependencies, manage addons and migrate existing installations via GUID.

- Use the `GET /mods/dependencies` endpoint to obtain the dependency tree.
- Implement `DependencyResolver`, which detects hard and optional dependencies, checks version conflicts
  and suggests batch installation.
- Implement `AddonService` analogously to `ModService`, with separate tables and screens.
- Implement `MigrationService`:
  - Scan `BepInEx/plugins` and `SPT_Runtime/user/mods`.
  - For each DLL, run `ModMetadataReader` and extract GUIDs.
  - Query the API in batches to identify every mod.
  - Build a "migration plan" listing recognized and unrecognized mods; the user decides whether to
    proceed.
  - After confirmation, create hard links and register the instance in the database.

**Acceptance criteria:** migrate a manual installation and verify that mods are recognized via GUID and
that configurations are preserved.

---

### Phase 7 — Binary Patch System (Launcher Core)

**Goal:** Implement the `Assembly-CSharp.dll` patch using `SharpHDiffPatch.Core`, replicating
`SPT.ByteBanger`.

This is the most critical phase for replacing the official launcher — the patch turns the online game
into the offline version.

- **Obtaining the patch files:** the official launcher stores patches in `SPT_Data/Launcher/Patches/`.
  The manager must locate or download those `.hdiff` files.
- **Implementing `BinaryPatcherService`:**
  - Use `HDiffPatch` from `SharpHDiffPatch.Core` to initialize and apply the patch.
  - Subscribe to the `LoggerEvent` and `PatchEvent` events to report progress in the UI.
  - **Backup and rollback:** back up the original `Assembly-CSharp.dll` before applying; restore it
    automatically on failure.
- **Version validation:** apply the patch only if the `Assembly-CSharp.dll` version matches the expected
  one (verified by version and checksums).

**Acceptance criteria:** starting from a clean Tarkov installation, apply the patch and verify that the
resulting `Assembly-CSharp.dll` is identical (by hash) to the one produced by the official launcher.

---

### Phase 8 — List Export and Sharing

**Goal:** Implement the custom archive format and list sharing.

- Define the `.sptpack` format (a ZIP containing):
  - `manifest.json` (metadata: SPT version, Tarkov version, mod list with GUIDs and exact versions).
  - A `configs/` folder with the configuration files that were modified (detected by hash).
- Implement `ExportService`:
  - For each mod, compare the current hash of its configuration files with the stored original hash.
    Include it in the package when they differ.
  - **Never** include binaries — metadata and configuration only.
- Implement `ImportService`:
  - Read `manifest.json`, download the mods from the API at the exact versions, install them with hard
    links and apply the configuration files from the package.
- Implement **unique share code generation** for the list (SHA-256 of `manifest.json`).

**Acceptance criteria:** export a modified instance, import it on another machine and obtain exactly the
same set of mods and configurations.

---

### Phase 9 — Linux Support (Proton/Wine)

**Goal:** Manage game and server execution on Linux.

- Create `LinuxRuntimeService` that detects installed Proton/Wine versions (via `steam` or the default
  folders).
- Implement Wine prefix creation and selection (`WINEPREFIX`).
- Integrate with `umu` (Unified Launcher for Windows Games on Linux) or the Lutris API to run the game
  with the selected Proton version.
- In the UI, add an advanced settings section (similar to Lutris): runner selection, environment
  variables (e.g. `DXVK_HUD`), etc.

**Acceptance criteria:** launch the SPT server through Proton from the manager and capture logs
correctly.

---

### Phase 10 — Profiles, Servers and Synchronization

**Goal:** Manage server connections and synchronize mods.

- Implement the `ServerProfile` concept (address, port, list of required mods).
- Create `ServerSyncService` that, on connect, compares local mods with the server's mods (via API or a
  server file) and downloads/updates the missing ones.
- Manage game profiles (SPT profile folders) and allow switching between them.

**Acceptance criteria:** connect to a test server and verify that missing mods are installed
automatically.

---

### Phase 11 — Full Launch Flow and Logs

**Goal:** Orchestrate the complete startup sequence (patch → validation → server → game) and unify logs.

- `LaunchService` executes the sequence:
  1. Check whether the instance needs patching; if so, run `BinaryPatcherService`.
  2. Validate the integrity of the patched files.
  3. Start the SPT server in the background (with `RedirectStandardOutput`/`RedirectStandardError`).
  4. Start the game (via Proton/Wine on Linux, natively on Windows) with the correct arguments.
- Create `ProcessManager` and `LogService` that collect logs from the server, the game (when possible)
  and Proton.
- Create the `LogWindow` (View) with tabs or per-source filters, plus export to `.txt` or `.log`.
- The UI must reflect every step of the process (e.g. "Patching binaries...", "Starting server...",
  "Starting game...").

**Acceptance criteria:** start the server and the game from the manager, watch logs in real time and
export them.

---

### Phase 12 — Complete User Interface

**Goal:** Build every screen and navigation flow.

- Main screen: instance list with status (outdated mods, pending patch, etc.).
- Instance details screen: tabs for Mods, Addons, Settings, Logs, Patch.
- Dialogs: instance creation, import, export, dependency resolution, update notifications.
- Notification panel (toast) for new mod versions.

**Acceptance criteria:** smooth navigation across all screens, with visual feedback for asynchronous
operations (progress bars, spinners).

---

### Phase 13 — Testing, QA and Polish

**Goal:** Guarantee stability and quality.

- Unit tests for all business services (especially dependency resolution, export/import, hard links and
  GUID extraction).
- Integration tests for the API client (WireMock or similar).
- Manual testing on Windows 10/11 and on a Linux distribution (Ubuntu/Fedora) with Proton.
- Code audit to enforce MVVM and DI patterns.

**Acceptance criteria:** test coverage > 70% in the `Core` and `Infrastructure` projects; no known
critical bugs.

---

### Phase 14 — Packaging and Distribution

**Goal:** Produce installers and artifacts for end users.

- Configure GitHub Actions for automated builds on Windows and Linux.
- Windows: MSI installer via WiX, or `dotnet publish` with `PublishSingleFile`.
- Linux: AppImage or `.tar.gz`.
- Write user documentation (README) covering installation and usage.

**Acceptance criteria:** a user can download the artifact and run the manager without additional
dependencies.

---

## 6. Final Considerations for the Coding Agent

- **Prioritize Phase 3 (API), Phase 5 (Mods/GUID) and Phase 7 (Binary patch)** — they are the heart of
  the system and what distinguishes it from a plain mod manager.
- **The GUID is the canonical mod identity key.** Use `MetadataLoadContext` to extract DLL metadata
  safely, without loading it into the current process.
- **Hard links are file system sensitive.** Implement checks and clear messages when the user tries to
  create an instance on a different volume or on a file system that does not support links (e.g. FAT32).
- **File security:** when extracting mods, sanitize paths to prevent *zip slip*.
- **Testability:** keep business rules in the `Core` layer and inject infrastructure dependencies through
  interfaces. This enables fast, reliable tests.
- **Binary patching:** always back up `Assembly-CSharp.dll` before applying a patch and validate the
  result by hash. Restore automatically on failure.
- **Official launcher as reference:** whenever behaviour is unclear (patching, startup sequence,
  arguments), consult the `SP-Tushonka/launcher` repository (branch `4.1.x-dev`).
- **Prism Launcher as reference:** for instance behaviour, hard links and export, consult the Prism
  Launcher documentation and source code.

With this roadmap, the coding agent has a clear, incremental path to build **SPT Manager**: starting
from the foundation and API integration, moving through binary patching and GUID-based mod
identification, and ending with a complete, cross-platform product capable of fully replacing the
official SPT launcher.
