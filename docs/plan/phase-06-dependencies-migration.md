# Phase 6 — Dependencies, addons and migrating existing installs

**Branch:** `feature/phase-06-dependencies-migration` · **Depends on:** Phase 5.

## Goal

Resolve dependency trees, manage addons, and adopt a hand-made SPT install into a managed instance
without losing anything.

## Tasks

- [ ] `DependencyResolver` completion: combine three sources — Forge `/mods/dependencies`,
      `IModMetadata.ModDependencies`/`Incompatibilities` from installed DLLs, and the Forge version
      record — into one `ResolutionPlan` (install order, required/optional, conflicts, already
      satisfied). Detect cycles and version conflicts with actionable messages.
- [ ] `AddonService`: list/add/remove addons for an installed mod; validate
      `AddonVersion.mod_version_constraint` against the installed mod version
      ([D10](../plan/04-decisions.md#d10-separate-tables-for-mods-and-addons)); addon files land inside
      the parent mod's directory or `BepInEx/plugins` depending on the archive (`ArchiveInspector`
      decides).
- [ ] `MigrationService` (adopt an existing install):
      - [ ] scan `BepInEx/plugins/**/*.dll` + `SPT_Runtime/user/mods/**` of a folder the user points at;
      - [ ] identify each mod by GUID (`IModIdentityReader`), batch-query Forge by `guids=` in chunks;
      - [ ] produce a **migration plan**: recognised mods (with Forge ids/versions), unrecognised,
            duplicates/conflicts, and files that belong to no mod;
      - [ ] show the plan; on confirm, register the instance, link/copy its files per the policy, and
            record integrity hashes;
      - [ ] never delete or move the source folder — adoption is additive, the original stays usable.
- [ ] `PrepatcherValidator`: a mod with `HasPrepatcher=true` must have `user/patchers/{ModGuid}`;
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

## Notes

- Migration is the feature that makes the app adoptable by existing players, and the GUID is the only
  reliable join key — a folder-name match would be guesswork.
- Batch `guids=` requests (the API accepts comma-separated lists) to stay inside 300 req/min.
