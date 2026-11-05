# Phase 6 — Dependencies, addons and migrating existing installs

**Branch:** `feature/phase-06-dependencies-migration` · **Depends on:** Phase 5.

**Status:** ☑ done (UI dialogs moved to Phase 12). Delivered: `DependencyResolver` (batched catalogue
lookup per level, Forge `/mods/dependencies` plus local `Dependency` rows, cycle detection, topological
order, already-installed detection, problems instead of exceptions), `ModUpdateService` (four buckets →
decisions, pins honoured, `includePinned` override), `AddonService` (parent lookup,
`mod_version_constraint` validation, placement next to the parent, replace-on-reinstall, uninstall) with
`InstalledMod.ParentModGuid` to keep hashes/integrity/uninstall working, and `MigrationService`
(`AdoptAsync` registers an existing folder without touching a file, `ScanAsync` merges by GUID and
classifies recognized/unknown/missing GUID + orphan files, `ApplyAsync` records rows and hashes
idempotently). 325 tests; Core 95.6 %, Infrastructure 90.4 %. Verified on a real instance: 2 mods
recognized, 0 orphans.

## Goal

Resolve dependency trees, manage addons, and adopt a hand-made SPT install into a managed instance
without losing anything.

## Tasks

- [x] `DependencyResolver` completion: combine three sources — Forge `/mods/dependencies`,
      `IModMetadata.ModDependencies`/`Incompatibilities` from installed DLLs, and the Forge version
      record — into one `ResolutionPlan` (install order, required/optional, conflicts, already
      satisfied). Detect cycles and version conflicts with actionable messages.
- [x] `AddonService`: list/add/remove addons for an installed mod; validate
      `AddonVersion.mod_version_constraint` against the installed mod version
      ([D10](../plan/04-decisions.md#d10-separate-tables-for-mods-and-addons)); addon files land inside
      the parent mod's directory or `BepInEx/plugins` depending on the archive (`ArchiveInspector`
      decides).
- [x] `MigrationService` (adopt an existing install):
      - [ ] scan `BepInEx/plugins/**/*.dll` + `SPT_Runtime/user/mods/**` of a folder the user points at;
      - [ ] identify each mod by GUID (`IModIdentityReader`), batch-query Forge by `guids=` in chunks;
      - [ ] produce a **migration plan**: recognised mods (with Forge ids/versions), unrecognised,
            duplicates/conflicts, and files that belong to no mod;
      - [ ] show the plan; on confirm, register the instance, link/copy its files per the policy, and
            record integrity hashes;
      - [ ] never delete or move the source folder — adoption is additive, the original stays usable.
- [ ] `PrepatcherValidator` (deferred with the UI: it only affects load-time warnings): a mod with `HasPrepatcher=true` must have `user/patchers/{ModGuid}`;
      surface a load-blocking warning otherwise ([D16](../plan/04-decisions.md#d16-doorstop-and-bepinex-are-managed-for-developers)).
- [ ] UI: dependency resolution dialog (tree + checkboxes), addon tab, migration wizard.

## Acceptance criteria

- Given a fixture mod set A→B→C (C required by B) the resolver installs C, B, A in that order in one
  batch, and refuses when B requires `^2.0.0` while C is `1.4.0`.
- Adopting `base_tarkov41` (or a copy of it) produces a managed instance whose mod list matches a
  fresh scan, with no file inside the source folder modified or removed.
- Addon install is refused when its `mod_version_constraint` excludes the installed mod version.
- Uninstalling a dependency that another installed mod requires is refused; `force` explains the
  consequence before doing it.

## Deviations

- **Addons are stored as `InstalledMod` rows with `ParentModGuid`** instead of a separate
  `InstalledAddon` table: files, hashes, integrity and uninstall already work per installation, and a
  second table would duplicate all of it.
- **The resolver reads dependencies from Forge** (plus local `Dependency` rows). Parsing
  `IModMetadata.ModDependencies` out of IL is not feasible with the literal scanner, and Forge is the
  only source that knows dependencies of mods that are not installed yet.
- **`ModUpdateService` landed here** (moved from phase 5, it needed the compatibility handling).
- **`PrepatcherValidator` deferred** with the UI work.
- UI dialogs deferred to phase 12, as in phases 4 and 5.

## Notes

- Migration is the feature that makes the app adoptable by existing players, and the GUID is the only
  reliable join key — a folder-name match would be guesswork.
- Batch `guids=` requests (the API accepts comma-separated lists) to stay inside 300 req/min.
