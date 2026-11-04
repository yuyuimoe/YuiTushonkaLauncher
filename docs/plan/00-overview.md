# Overview

## What this is

`YuiTushonkaLauncher` is a cross-platform (Windows + Linux) desktop application that **replaces the
official SPT launcher** and adds Prism-Launcher-style instance and mod management on top.

SPT ("Single Player Tarkov") turns live Tarkov into an offline game: the game client is patched, a
local .NET server emulates the Escape-from-Tarkov backend, and BepInEx plugins extend both sides.
Today the user drives that with three separate tools — a launcher, a web-based mod browser, and manual
folder juggling. This app owns the whole loop:

- **Instances** — several independent SPT installs sharing one game copy through hard links.
- **Mods** — install, pin, update, resolve dependencies, identify installed mods by GUID.
- **Patching** — apply the SPT binary delta, detect an already-patched client, roll back.
- **Running** — start the server, start the game (natively on Windows, through Proton on Linux),
  surface all logs, and stop everything cleanly.
- **Sharing** — export/import an instance's metadata + modified configs (never binaries).

## Scope

In scope:

- Instance lifecycle (create, clone, rename, delete) with a hard-link, copy-on-write file layer.
- Forge API (`https://sp-mod.com/api/v0`) integration: search, versions, dependencies, updates,
  addons, SPT versions, categories.
- Mod install/uninstall/pin/update, server + client mods, integrity tracking.
- SPT binary patching (`.delta` via `SharpHDiffPatch.Core`), backup/restore.
- Local SPT server lifecycle and profile management.
- Unified log viewer for server, game and Proton with rotation/compression/export.
- Linux runtime management: Proton selection, Wine prefix, environment variables.
- Export/import of instance packages (`.sptpack`).

Out of scope (explicitly):

- Downloading Tarkov. The user supplies game files produced by the official SPT installer
  (see [D4](04-decisions.md#d4-tarkov-files-are-user-supplied-spt-files-are-downloaded)).
- Running an online Tarkov session, account login to BSG, purchasing the game.
- Being a general-purpose mod downloader for other games.
- Native AOT (dropped — see [D14](04-decisions.md#d14-no-native-aot)).

## Stack

| Concern | Choice | Note |
|---|---|---|
| Runtime | **.NET 10** (`net10.0`) | Mandatory: SPT itself requires .NET 10, so every target machine already has it ([D15](04-decisions.md#d15-net-10-is-mandatory)). |
| UI | **Avalonia 12** + `CommunityToolkit.Mvvm` | Already in the repo scaffold; compiled bindings on. |
| Persistence | **EF Core + Microsoft.Data.Sqlite** | [D2](04-decisions.md#d2-ef-core-over-dapper-with-sqlite). |
| Patching | **`SharpHDiffPatch.Core`** | Same library the official launcher uses. |
| DLL inspection | **`System.Reflection.MetadataLoadContext`** | Reads mod assemblies without loading them. |
| HTTP | `HttpClient` + `System.Threading.RateLimiting` | No API key needed; respects `x-ratelimit-*`. |
| Archives | `SharpCompress` / `System.IO.Compression` | `.sptpack` is a zip; mod downloads are zip/7z/rar. |
| Logging | **Serilog** | Console + rolling file, sinks per subsystem. |
| DI | **`Microsoft.Extensions.DependencyInjection`** | `App.axaml.cs` composition root. |
| i18n | JSON locale files | Mirrors SPT's `SPT_Data/Launcher/Locales/*.json` convention. |

## Guiding principles

1. **The user's game files are sacred.** Never patch, move or delete anything outside the instance
   folder without an explicit, reversible step.
2. **No silent destruction.** Every write to a hard-linked file detaches it first; every patch backs
   up; every destructive action is undoable or confirmed.
3. **Offline-first.** The app must be fully usable with the network off (only install/update and
   metadata refresh need the Forge API).
4. **Evidence over assumption.** Anything about SPT behaviour comes from a real install or the
   official source, and is recorded in [03-verified-facts](03-verified-facts.md).
5. **Docs and comments in English.** Conversation is Portuguese; nothing written into the repo is.
