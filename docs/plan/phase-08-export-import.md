# Phase 8 — Export and import

**Branch:** `feature/phase-08-export-import` · **Depends on:** Phases 5, 6.

**Status:** ☑ done (dialogs moved to Phase 12). Delivered: `PackManifest` + canonical serializer and share
code, `PackRules`/`PackPathGuard`, `PackReader` (every check before anything is returned), deterministic
`PackWriter`, `PackService` (`Export`, `PlanImport`, `Import`) and `--export` / `--import [--apply]`. 575
tests; Core 96.9 %, Infrastructure 93.6 %. Verified on a real round trip with two mods installed from the
Forge: identical share code, byte-identical mod trees, edited configs carried over, six hostile packages
refused. Three bugs from earlier phases surfaced on the way and were fixed (see below).

## Goal

Share an instance's *composition* (mods + exact versions + modified configs) as a portable file, and
rebuild it on another machine without shipping a single binary.

## Format — `.sptpack`

A zip containing:

```
manifest.json          metadata + mod list + config hashes (canonical JSON, sorted keys)
configs/<instance-relative path>          only files whose hash differs from install time
README.txt                                human-readable summary (optional, generated)
```

`manifest.json` fields: `formatVersion`, `createdAt`, `appVersion`, `instanceName`,
`sptVersion`, `tarkovVersion`, `layout`, `mods[]` (`guid`, `forgeId`, `name`, `version`, `kind`,
`pinned`), `addons[]`, `configs[]` (`path`, `sha256`, `originalSha256`), and `files[]` for
integrity-only entries. `shareCode` = SHA-256 of the canonical manifest.

## Tasks

- [x] `ManifestWriter`/`ManifestReader` with a canonical serializer (sorted keys, `\n`, no BOM) and a
      `formatVersion` that fails closed on unknown values.
- [x] `ExportService`: read instance state, filter configs by `IntegrityService` diff, build the zip
      with deterministic entry order and timestamps, write `shareCode`.
- [x] **Never** include binaries: an allowlist of extensions (`.json`, `.ini`, `.toml`, `.yaml`,
      `.txt`, `.cfg`) plus a hard blocklist for `.dll`, `.exe`, `.bundle`, `.pak`, `.dat` — and a test
      asserting a mod DLL cannot end up in a package.
- [x] `ImportService`: read manifest (schema validation) → show a plan (what will be installed,
      what conflicts, what cannot be found on Forge) → download exact versions → install via
      `ModInstaller` → apply configs through `ILinkAwareFileWriter` → verify hashes.
- [x] Security baseline (full threat model is
      [an open question](../plan/90-open-questions.md#blocks-phase-8-exportimport)): zip-slip guard,
      `configs/**`-only allowlist, entry-count/size caps, no symlinks, and a review screen before any
      write.
- [ ] UI: export dialog (name, include/exclude configs, resulting share code + copy button). Still open.
- [x] UI: import review and apply, reached when the shell opens a `.sptpack` (`PackOpenService`): the plan, the
      share code, what would change and what is unavailable, and a confirmation before any write.
- [x] CLI-friendly: `--export <instance> <out.sptpack>` / `--import <file> --into <dir>` so the flow is
      testable and scriptable.

## Acceptance criteria

- Round trip: export an instance with one edited config → delete it → import the package into a fresh
  instance → identical mod set (GUID + version), identical config content, `IntegrityService` clean.
- A package with an entry named `../../evil.json` or an absolute path is rejected before writing.
- A package containing a `.dll` is rejected with a specific error.
- Importing a package whose manifest `formatVersion` is newer than the app fails with an upgrade hint.
- Same `shareCode` for two exports of an unchanged instance (determinism).

## Deviations

- **One service, not two.** `PackService` exports, plans and imports; the plan and the import share the
  same validated contents.
- **`files[]` became `contentSha256` per mod.** Listing every file would run into the manifest size cap
  (one real mod has 93); one digest of the as-shipped hashes gives the same tamper evidence.
- **`.xml` is not shareable** (the plan's allowlist does not list it, although the installer treats `.xml` as
  a config). An edited `.xml` is reported in `Skipped` instead of vanishing.
- **`--import FILE --instance NAME`, not `--into DIR`.** The import goes into an existing instance (create
  it with `--create-instance`), and applies only with `--apply`: the CLI's review screen.
- **A config may only be written into a tracked file of a mod the package names**, which the plan did not
  ask for but the threat model needs ([D19](04-decisions.md#d19-a-sptpack-is-a-list-of-names-never-a-payload)).
- **Applying a config moves the integrity baseline**, otherwise "IntegrityService clean" after an import
  would be impossible (an edited config is drift by definition).
- UI dialogs deferred to phase 12.

## Bugs from earlier phases found here

- **Phase 3:** versions with build metadata (`0.3.1+1`, `1.8.0+h4`) were rejected, so one such version made a
  mod's whole version list fail. `SemanticVersion` now carries `Build`.
- **Phase 5:** a download without an identity name was named after the last URL segment (the version).
- **Phase 6:** `AddonService` ignored `AddonInstallRequest.ForgeId`, so addons could not be exported; it now
  records the catalogue row and `Source = Forge`.

## Notes

- Configs are the only user-authored state worth sharing; profiles and logs stay local.
- The package must be importable on a machine that has never seen the source instance, so versions are
  exact, not ranges.
