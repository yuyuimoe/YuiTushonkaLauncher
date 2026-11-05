# Phase 5 — Mod install and GUID identification

**Branch:** `feature/phase-05-mod-install` · **Depends on:** Phases 3, 4.

**Status:** ☑ done (UI list/install flow moved to Phase 12). Delivered: `IModIdentityReader` reading the
`[BepInPlugin]` attribute and, for server mods, the string literals of the `IModMetadata` type straight
out of the IL (no assembly is loaded or executed), `ArchiveInspector` (kind, candidate assemblies, root
wrapper, unsafe entries) without extracting, `SafeExtractor` (rooted/traversing entries rejected before
any write), `InstancePathMapper` (game-root archive paths → `gamefiles/` + `spt/`), `ModInstaller`
(download or local archive → inspect → identify → relocate → copy through the link-aware writer →
hashes/rows, idempotent reinstall, replace-on-difference, uninstall with dependency guard, pinning) and
`IntegrityService` (modified/missing files, modified configs). 296 tests; Core 95.6 %, Infrastructure
90.5 %. Verified with two real archives: `com.mpstark.dynamicmaps` (Both) and `fika.ghostfenixx.svm`
(Server), both `Recognized` with `integrity clean=True`.

## Goal

Install server and client mods into an instance, identify what is already installed by GUID, and track
integrity so local edits are detectable.

## Tasks

- [x] `IModIdentityReader` with two implementations (attribute + `IModMetadata` literals)
      ([D9](../plan/04-decisions.md#d9-two-mod-identity-readers)):
      - `BepInPluginIdentityReader` — client DLLs: `[BepInPlugin(guid, name, version)]` via
        `MetadataLoadContext`;
      - `ServerModIdentityReader` — server DLLs: find the type implementing
        `SPTarkov.Server.Core.Models.Spt.Mod.IModMetadata`, read `ModGuid`, `Name`, `Author`,
        `Version`, `SptVersion`, `ModDependencies`, `Incompatibilities`, `HasPrepatcher`;
      - shared `MetadataLoadContext` resolver fed from the instance's `SPT_Runtime/*.dll` +
        `BepInEx/core/*.dll` (never the app's own assemblies).
- [x] `ArchiveInspector`: read an archive **without extracting**, classify entries into
      `ClientPlugin`, `ServerMod`, `BepInExCore`, `Config`, `Docs`, decide the mod kind, and find the
      assembly to identify.
- [x] `SafeExtractor`: zip-slip-proof extraction using `RelativePath` (no `..`, no rooted paths, no
      symlinks, case-collision check, size cap), into a temp dir, then move into place.
- [x] `ModInstaller`: download (cache hit first) → inspect → identify → link/copy per the policy →
      write rows (`Mod`, `ModVersion`, files, hashes) in one transaction.
      Client mods → `BepInEx/plugins/<Name>/`, server mods → `SPT_Runtime/user/mods/<Name>/`.
- [x] `ModUninstaller` (inside `ModInstaller`, dependency guard + force): refuse when another installed mod depends on it (unless forced); remove files,
      report orphans.
- [ ] `ModUpdateService` (phase 6 — needs the resolver): `/mods/updates` + local constraints → per-mod decisions, pinning respected
      ([D12](../plan/04-decisions.md#d12-spt-and-tarkov-compatibility-matrix)); updates go through
      `ILinkAwareFileWriter` so a linked file is detached before replacement.
- [x] `IntegrityService`: SHA-256 of every installed file at install time; `Verify(instance)` reports
      added/removed/modified files; used by export (Phase 8) and by the UI badge.
- [x] "Pin version" flag: blocks update suggestions, still allows explicit reinstall.
- [x] Unrecognised mods: registered with `Recognition.Unrecognized`, visible in the UI, never
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

## Deviations

- **Identity is read from metadata, not by loading the assembly.** The phase file proposed
  `MetadataLoadContext`; measuring two real mods showed the values live in IL literals, and MLC cannot
  instantiate types anyway. The reader parses the IL (`ldstr`) instead, so no mod code runs. Trade-off:
  a mod that computes its GUID at run time stays `GuidMissing`, which the plan already allows.
- **`InstancePathMapper` was not in the plan** and is essential: archives are game-root relative.
- **`ModUpdateService` moved to phase 6** (it needs the dependency resolver).
- UI deferred to phase 12, as in phase 4.

## Notes

- `MetadataLoadContext` cannot *execute* code — it only reads metadata, which is exactly what we need
  for hostile input.
- Archive formats: zip first; 7z support behind the same interface when a real 7z mod appears
  ([90-open-questions](../plan/90-open-questions.md)).
- `BepInEx/plugins/**` is never shared between instances — installing mods must not affect the base
  install ([D7](../plan/04-decisions.md#d7-per-path-link-policy)).
