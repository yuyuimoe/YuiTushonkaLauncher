# Open questions

Everything still unresolved, with who/what resolves it.

## Blocks Phase 8 (export/import)

1. **Threat model for `.sptpack` — defaults implemented, authenticity still open.** The baseline is now
   code and documented as [D19](04-decisions.md#d19-a-sptpack-is-a-list-of-names-never-a-payload): no
   binaries, no URLs from the package, writes only into files a named mod owns, strict validation, content
   digest against the exporter's, review before write. What is **not** decided: should a package be
   *signed*, and should it embed a hash of the producing install? Without either, the share code proves
   integrity only, never who made the package. Still worth a dedicated grilling session before 1.0, because
   it changes the format (a `formatVersion` 2).

2. **Share-code semantics — decided.** The code is the SHA-256 of the canonical *composition* (not of the
   file): it excludes `createdAt`, `appVersion`, `instanceName` and display names, so two exports of the
   same composition match. See D19.

## Blocks nothing now, but must be settled before 1.0

3. **SPT version upgrade path.** In-place `Restore()` → swap runtime → re-`Patch()` on the same
   instance, or "create a new instance and copy `user/`"? Affects `ICompatibilityService.CanUpgradeSpt`
   and the UI flow.
4. **Multi-instance server ports.** Fixed default 6969 with automatic conflict resolution, or the
   server's config file edited per instance? Need to confirm where the 4.x server reads its port from
   (`SPT_Data/configs/http.json` observed — see [03-verified-facts](03-verified-facts.md)).
5. **Bundle sync.** `/singleplayer/bundles` + `/files/bundle/` look like an asset sync we might need
   to drive. Confirm semantics before Phase 10; a wrong guess wastes download bandwidth.
6. **`.sptpack` MIME + shell integration.** Double-click import on Windows (registry) and Linux
   (`.desktop` + MIME XML). Deferred to Phase 14.
7. **Update channel for the app itself.** The official launcher has a signed manifest updater
   (`UpdateManifest`/`ManifestSignature`/`UpdateTransaction`). Do we build an equivalent, or ship via
   GitHub releases only?

## Non-blocking watch items

8. **Trimming/AOT** — dropped ([D14](04-decisions.md#d14-no-native-aot)); revisit only if startup time
   becomes a complaint and `ViewLocator` is gone.
9. **7z/rar mod archives** — the Forge hosts arbitrary archives (Dropbox links, 7z, rar). Phase 5
   starts with zip; 7z support reuses the `SevenZip` abstraction pattern from the official launcher.
   Confirm which formats actually appear in the catalogue before building a native dependency.
10. **Linux hardening** — the official launcher's `LinuxHelper` runs `winecfg`/`winetricks` inside the
    prefix. Decide how much of that belongs in a launcher (convenience) versus a distro tool.
11. **Patched-with-an-older-delta detection.** The guard sees sizes only, so a file patched by a previous
    SPT version (same input, different output) looks like "neither build" and is re-patched from its
    backup — correct, but a *same-size* modification is indistinguishable. A hash of the expected output
    would close it; the delta carries none. Options: record the SHA-256 of our own output in `PatchInfo`
    (already done) and compare on the next run, or ship per-release hashes with the app.
12. **Byte-identity with the official launcher.** Needs a fresh install patched by the *current* official
    launcher; only an 8 KiB-older output exists locally. Cheap to settle whenever someone runs the
    official launcher once on a new 4.1.6 install.
13. **Pack UI wording for untrusted names.** The names in a package are unauthenticated, so the review
    screen must always show the GUID and the Forge-confirmed name next to them (the CLI already prints the
    GUID). Settle the layout in phase 12.
14. **Config files the installer calls non-config.** `IsConfig` still misses `.txt` (and the integrity check
    therefore does not list an edited `MOTD.txt` under `ModifiedConfigs`). The export no longer depends on
    the flag; decide in phase 12 whether integrity should use the same extension rule.

