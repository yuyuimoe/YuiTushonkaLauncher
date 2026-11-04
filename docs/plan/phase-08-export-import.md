# Phase 8 — Export and import

**Branch:** `feature/phase-08-export-import` · **Depends on:** Phases 5, 6.

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

- [ ] `ManifestWriter`/`ManifestReader` with a canonical serializer (sorted keys, `\n`, no BOM) and a
      `formatVersion` that fails closed on unknown values.
- [ ] `ExportService`: read instance state, filter configs by `IntegrityService` diff, build the zip
      with deterministic entry order and timestamps, write `shareCode`.
- [ ] **Never** include binaries: an allowlist of extensions (`.json`, `.ini`, `.toml`, `.yaml`,
      `.txt`, `.cfg`) plus a hard blocklist for `.dll`, `.exe`, `.bundle`, `.pak`, `.dat` — and a test
      asserting a mod DLL cannot end up in a package.
- [ ] `ImportService`: read manifest (schema validation) → show a plan (what will be installed,
      what conflicts, what cannot be found on Forge) → download exact versions → install via
      `ModInstaller` → apply configs through `ILinkAwareFileWriter` → verify hashes.
- [ ] Security baseline (full threat model is
      [an open question](../plan/90-open-questions.md#blocks-phase-8-exportimport)): zip-slip guard,
      `configs/**`-only allowlist, entry-count/size caps, no symlinks, and a review screen before any
      write.
- [ ] UI: export dialog (name, include/exclude configs, resulting share code + copy button), import
      dialog with the plan review.
- [ ] CLI-friendly: `--export <instance> <out.sptpack>` / `--import <file> --into <dir>` so the flow is
      testable and scriptable.

## Acceptance criteria

- Round trip: export an instance with one edited config → delete it → import the package into a fresh
  instance → identical mod set (GUID + version), identical config content, `IntegrityService` clean.
- A package with an entry named `../../evil.json` or an absolute path is rejected before writing.
- A package containing a `.dll` is rejected with a specific error.
- Importing a package whose manifest `formatVersion` is newer than the app fails with an upgrade hint.
- Same `shareCode` for two exports of an unchanged instance (determinism).

## Notes

- Configs are the only user-authored state worth sharing; profiles and logs stay local.
- The package must be importable on a machine that has never seen the source instance, so versions are
  exact, not ranges.
