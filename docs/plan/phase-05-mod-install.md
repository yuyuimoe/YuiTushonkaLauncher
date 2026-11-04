# Phase 5 — Mod install and GUID identification

**Branch:** `feature/phase-05-mod-install` · **Depends on:** Phases 3, 4.

## Goal

Install server and client mods into an instance, identify what is already installed by GUID, and track
integrity so local edits are detectable.

## Tasks

- [ ] `IModIdentityReader` with two implementations
      ([D9](../plan/04-decisions.md#d9-two-mod-identity-readers)):
      - `BepInPluginIdentityReader` — client DLLs: `[BepInPlugin(guid, name, version)]` via
        `MetadataLoadContext`;
      - `ServerModIdentityReader` — server DLLs: find the type implementing
        `SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata`, read `ModGuid`, `Name`, `Author`,
        `Version`, `SptVersion`, `ModDependencies`, `Incompatibilities`, `HasPrepatcher`;
      - shared `MetadataLoadContext` resolver fed from the instance's `SPT_Runtime/*.dll` +
        `BepInEx/core/*.dll` (never the app's own assemblies).
- [ ] `ArchiveInspector`: read an archive **without extracting**, classify entries into
      `ClientPlugin`, `ServerMod`, `BepInExCore`, `Config`, `Docs`, decide the mod kind, and find the
      assembly to identify.
- [ ] `SafeExtractor`: zip-slip-proof extraction using `RelativePath` (no `..`, no rooted paths, no
      symlinks, case-collision check, size cap), into a temp dir, then move into place.
- [ ] `ModInstaller`: download (cache hit first) → inspect → identify → link/copy per the policy →
      write rows (`Mod`, `ModVersion`, files, hashes) in one transaction.
      Client mods → `BepInEx/plugins/<Name>/`, server mods → `SPT_Runtime/user/mods/<Name>/`.
- [ ] `ModUninstaller`: refuse when another installed mod depends on it (unless forced); remove files,
      report orphans.
- [ ] `ModUpdateService`: `/mods/updates` + local constraints → per-mod decisions, pinning respected
      ([D12](../plan/04-decisions.md#d12-spt-and-tarkov-compatibility-matrix)); updates go through
      `ILinkAwareFileWriter` so a linked file is detached before replacement.
- [ ] `IntegrityService`: SHA-256 of every installed file at install time; `Verify(instance)` reports
      added/removed/modified files; used by export (Phase 8) and by the UI badge.
- [ ] "Pin version" flag: blocks update suggestions, still allows explicit reinstall.
- [ ] Unrecognised mods: registered with `Recognition.Unrecognized`, visible in the UI, never
      auto-updated, never block launch.
- [ ] UI: mod list per instance (name, GUID, version, status), install-from-Forge flow,
      install-from-file flow, update badges.

## Acceptance criteria

- Installing a client mod and a server mod into a fresh instance puts the DLLs in
  `BepInEx/plugins/<Name>` and `SPT_Runtime/user/mods/<Name>` respectively, and the GUID shown in the
  UI equals the one in the DLL (verified against a fixture DLL for each kind).
- A `.zip` containing `../escape.dll` is rejected and nothing is written.
- Reinstalling the same version is a no-op; installing a newer version replaces files atomically.
- `IntegrityService` flags a hand-edited config as `Modified`, and export later includes it.
- A mod whose `SptConstraint` excludes the instance's SPT version cannot be installed through the UI
  and is rejected by the service with a explicit reason.

## Notes

- `MetadataLoadContext` cannot *execute* code — it only reads metadata, which is exactly what we need
  for hostile input.
- Archive formats: zip first; 7z support behind the same interface when a real 7z mod appears
  ([90-open-questions](../plan/90-open-questions.md)).
- `BepInEx/plugins/**` is never shared between instances — installing mods must not affect the base
  install ([D7](../plan/04-decisions.md#d7-per-path-link-policy)).
